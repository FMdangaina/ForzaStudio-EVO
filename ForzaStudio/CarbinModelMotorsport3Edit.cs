namespace ForzaStudio
{
    using System;
    using Microsoft.Xna.Framework;

    // Part edits (move / rotate / scale, also one material) on an FM3 .carbin opened in its own layout
    // (new CarbinModel(data, true)), user 2026-11-18: "editor posizione, rotazione e dimensione non abilitato su FM3".
    // The FM3 vertex keeps everything but what the edit changes:
    //  - position: 4 halves at 0 (unit direction from origin + bounds centre, then the distance), as the FM4 -> FM3 port
    //    writes it (Motorsport3Buffer); Positions / Encode switch to it for FM3 sections (IsMotorsport3Layout);
    //  - frame: the normal (11:11:10 word at 16 on 40-byte car vertices, at 12 on 16-byte brake and 44-byte wheel ones) and,
    //    on 40-byte vertices, the frame's Z axis at 36; rewritten only when the edit turns them (a move or an even scale
    //    leaves those bytes as they are);
    //  - UV1 / UV2, the S16 values, the damage word and the rest: never touched (user rule: UVs are never changed).
    public partial class CarbinModel
    {
        // An FM3 section's buffer: FM3 marker 2 and FM3's vertex sizes (FM4 / FH vertices are 32 bytes; a section being
        // converted between the games has its new buffers in the other game's size, so it is read right on both sides).
        private static bool IsMotorsport3Layout(Section section, VertexBuffer buffer)
        {
            return section.Marker != null && section.Marker.Length == 4 && section.Marker[3] == 2 && section.Marker[0] == 0
                && (buffer.Stride == 40 || buffer.Stride == 16 || buffer.Stride == 44);
        }

        public static bool IsMotorsport3Buffer(Section section, VertexBuffer buffer)
        {
            return IsMotorsport3Layout(section, buffer);
        }

        // UV of set 0 / 1 in FM4 units, whatever the buffer's game (UV Mapper on FM3, user 2026-11-21): FM3 vertices hold
        // the UV itself as halves (UV1 at 8, UV2 at 12 on 40-byte car vertices); an FM3 mesh has no UV range (the 8
        // floats are 0 on 18050 of 18050 meshes, fm4dev\ST03\uvt\dbg2). FM4 / FH: unorm16 in the mesh's range (VertexUv).
        public static Vector2 LayoutUv(Section section, VertexBuffer buffer, int v, SubSection sub, int set)
        {
            if (!IsMotorsport3Layout(section, buffer))
            {
                return VertexUv(buffer, v, sub, set);
            }
            int o = (v * buffer.Stride) + 8 + (set * 4);
            return new Vector2(HalfAt(buffer.Data, o), HalfAt(buffer.Data, o + 2));
        }

        // The UV sets a buffer holds: FM3 40-byte car vertices and FM4 / FH ones 2, other FM3 sizes (brakes, wheels) 1.
        public static int LayoutUvSets(Section section, VertexBuffer buffer)
        {
            return (IsMotorsport3Layout(section, buffer) && buffer.Stride != 40) ? 1 : 2;
        }

        // Writes one vertex's UV (FM4 units) in the buffer's own encoding; FM3: the value as a half; FM4 / FH:
        // unit = (value - offset) / scale as unorm16 (the caller keeps the range).
        public static void WriteLayoutUv(Section section, VertexBuffer buffer, int v, SubSection sub, int set, float u, float w)
        {
            int o = (v * buffer.Stride) + 8 + (set * 4);
            if (IsMotorsport3Layout(section, buffer))
            {
                WriteHalfBig(buffer.Data, o, u);
                WriteHalfBig(buffer.Data, o + 2, w);
                return;
            }
            float su = sub.Uv[(set * 4) + 1], sw = sub.Uv[(set * 4) + 3];
            double uu = (Math.Abs(su) < 1e-12f) ? 0 : (u - sub.Uv[set * 4]) / su;
            double ww = (Math.Abs(sw) < 1e-12f) ? 0 : (w - sub.Uv[(set * 4) + 2]) / sw;
            int a = Math.Max(0, Math.Min(65535, (int) Math.Round(uu * 65535)));
            int b = Math.Max(0, Math.Min(65535, (int) Math.Round(ww * 65535)));
            buffer.Data[o] = (byte) (a >> 8);
            buffer.Data[o + 1] = (byte) a;
            buffer.Data[o + 2] = (byte) (b >> 8);
            buffer.Data[o + 3] = (byte) b;
        }

        // The vertex normal whatever the buffer's game: FM3 11:11:10 word (at 16 on 40-byte vertices, else at 12), FM4 / FH
        // the frame's X axis.
        public static Vector3 LayoutNormal(Section section, VertexBuffer buffer, int v)
        {
            if (!IsMotorsport3Layout(section, buffer))
            {
                return VertexNormal(buffer, v);
            }
            return ReadPacked111110(buffer.Data, (v * buffer.Stride) + ((buffer.Stride == 40) ? 16 : 12));
        }

        private static float HalfAt(byte[] d, int o)
        {
            int h = (d[o] << 8) | d[o + 1];
            int s = (h >> 15) & 1, e = (h >> 10) & 31, m = h & 1023;
            float value = (e == 0) ? (m / 1024f) * (float) Math.Pow(2, -14) : ((e == 31) ? float.NaN : (1 + (m / 1024f)) * (float) Math.Pow(2, e - 15));
            return (s == 1) ? -value : value;
        }

        private static void Motorsport3Encode(Section section, VertexBuffer buffer, double[] positions)
        {
            for (int i = 0; i < buffer.Count; i++)
            {
                int o = i * buffer.Stride;
                double[] d = new double[3];
                double length = 0;
                for (int c = 0; c < 3; c++)
                {
                    double center = (section.Bounds[3 + c] + section.Bounds[6 + c]) / 2;
                    d[c] = positions[(i * 3) + c] - section.Bounds[c] - center;
                    length += d[c] * d[c];
                }
                length = Math.Sqrt(length);
                for (int c = 0; c < 3; c++)
                {
                    WriteHalf(buffer.Data, o + (c * 2), (length > 1e-12) ? d[c] / length : 0);
                }
                WriteHalf(buffer.Data, o + 6, length);
            }
        }

        // FM3 brake sections carry no bounds (model space positions): an edit keeps them at 0.
        private static bool Motorsport3NoBounds(Section section)
        {
            if (!IsMotorsport3Layout(section, (section.Lod0.Count > 0) ? section.Lod0 : section.Lod))
            {
                return false;
            }
            foreach (float value in section.Bounds)
            {
                if (value != 0)
                {
                    return false;
                }
            }
            return true;
        }

        // Shared with the FM2 writer (Fm2CarbinModelEdit.cs): FM2 vertices use the same halves and 11:11:10 words.
        internal static void WriteHalfBig(byte[] data, int offset, double value)
        {
            WriteHalf(data, offset, value);
        }

        internal static Vector3 ReadPacked111110(byte[] data, int offset)
        {
            return Packed111110(data, offset);
        }

        internal static void WritePacked111110Big(byte[] data, int offset, Vector3 v)
        {
            WritePacked111110(data, offset, v);
        }

        internal static bool KeepsDirectionsOf(Quaternion rotation, Matrix normalMatrix)
        {
            return KeepsDirections(rotation, normalMatrix);
        }

        // No rotation and an even scale (normal matrix = k * identity): directions do not change.
        private static bool KeepsDirections(Quaternion rotation, Matrix n)
        {
            if (Math.Abs(Math.Abs(rotation.W) - 1f) > 1e-7f)
            {
                return false;
            }
            float k = n.M11;
            const float eps = 1e-6f;
            return Math.Abs(n.M22 - k) <= eps * Math.Abs(k) && Math.Abs(n.M33 - k) <= eps * Math.Abs(k)
                && Math.Abs(n.M12) <= eps && Math.Abs(n.M13) <= eps && Math.Abs(n.M21) <= eps
                && Math.Abs(n.M23) <= eps && Math.Abs(n.M31) <= eps && Math.Abs(n.M32) <= eps;
        }

        // The tangent frame of vertex i after an edit, in the buffer's own layout (FM4 / FH quaternion or FM3 axes).
        private static void TransformVertexFrame(Section section, VertexBuffer buffer, int i, Quaternion rotation, Matrix normalMatrix)
        {
            if (!IsMotorsport3Layout(section, buffer))
            {
                TransformFrame(buffer.Data, (i * buffer.Stride) + 16, rotation, normalMatrix);
                return;
            }
            if (KeepsDirections(rotation, normalMatrix))
            {
                return;     // a move or an even scale: the FM3 frame bytes stay as they are
            }
            int o = i * buffer.Stride;
            int at = o + ((buffer.Stride == 40) ? 16 : 12);
            Vector3 normal = Packed111110(buffer.Data, at);
            if (normal.LengthSquared() < 1e-6f)
            {
                return;
            }
            normal.Normalize();
            Vector3 turned = Vector3.TransformNormal(normal, normalMatrix);
            if (turned.LengthSquared() < 1e-12f)
            {
                return;
            }
            turned.Normalize();
            if (Vector3.Dot(turned, normal) < 0.9999999f)
            {
                WritePacked111110(buffer.Data, at, turned);
            }
            if (buffer.Stride == 40)
            {
                Vector3 axis = Packed111110(buffer.Data, o + 36);
                if (axis.LengthSquared() < 1e-6f)
                {
                    return;
                }
                axis.Normalize();
                Vector3 axisTurned = Vector3.Transform(axis, rotation);
                axisTurned -= turned * Vector3.Dot(axisTurned, turned);
                if (axisTurned.LengthSquared() < 1e-12f)
                {
                    return;
                }
                axisTurned.Normalize();
                if (Vector3.Dot(axisTurned, axis) < 0.9999999f)
                {
                    WritePacked111110(buffer.Data, o + 36, axisTurned);
                }
            }
        }
    }
}
