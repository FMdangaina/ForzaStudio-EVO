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
