namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using Microsoft.Xna.Framework;

    // FM3 -> FM4 conversion of a .carbin read with CarbinModel(data, true). Kept apart from the FM4 and
    // FH code: it only runs on IsMotorsport3 models and leaves them as plain FM4 models.
    //
    // Measured on the Supra RZ '98 (both games) and the 119 cars, 244 wheels and 120 Media\brakes files
    // the two games share (fm4dev\FM3_FM4: supracmp, vcorr, structcmp, headscan, wheelrule):
    //  - header: the format block [0xD, 0xB, 1, N] becomes [0x10, 0x10, 1, N]; the N records of 140
    //    bytes and the next 0x114 bytes stay; FM3's 4 blocks of 0x24 bytes become 15 (the 11 new ones
    //    hold the default values 0,0,0,1,0,0,1,0,0); the two floats at the end of the last block (x and
    //    y = -2x, about -0.33) stay, and brakes, where FM3 has 0, get FM4's usual brake values; before the
    //    final (4, 4) FM4 adds (1, 1, 0) on cars, (0, 1, 0) on brakes and wheels. In front of the format
    //    block FM4 cars have 0x50 more bytes (leftover memory; the word at 0x150 is 1) and wheels 12
    //    floats measured on the wheel (see WheelMeasures);
    //  - sections: marker 2 -> 5; FM3's word before VbTest2 is FM4's Pre word (VbTest1 then 0); Post9
    //    (deformable flag), the damage tables and Mid are added; subsections get Pre5 0000000300, their
    //    UV offset/scale and the fixed 36 bytes;
    //  - vertices, 40 bytes on car parts: 4 halves position (origin + xyz * w), uv0 and uv1 as halves,
    //    the normal (u32 11:11:10), the 4 S16 values FM4 keeps at 24..31, the FM4 damage word, a word FM4
    //    dropped and the tangent frame's Z axis (11:11:10). A part is deformable in FM4 exactly when its
    //    damage words are not all 000003FF (115 of 115 Supra parts). 16 bytes (brakes): position, uv0,
    //    normal. 44 bytes (wheels): position, uv0, normal, the 4 S16 values, then constants.
    public partial class CarbinModel
    {
        private const uint Motorsport3PlainDamage = 0x000003FF;

        // FM4's usual end floats of brake headers (x, y = -2x), where FM3 brakes have 0.
        private const float BrakeEndX = -0.3265f;
        private const float BrakeEndY = 0.6550f;

        public string ConvertMotorsport3ToMotorsport4()
        {
            if (!this.IsMotorsport3)
            {
                throw new InvalidOperationException("Not a Forza Motorsport 3 model.");
            }
            System.Text.StringBuilder report = new System.Text.StringBuilder();
            bool brake = this.Type == 1 && ReadU32(this.Head, 4) == 0xD;
            bool wheel = this.Type == 2;
            // The header first: the wheel measures (and wheelMorph) are read from the FM3 vertices.
            this.wheelMorph = 1;
            this.Head = this.ConvertMotorsport3Head(brake, wheel);
            foreach (Section section in this.Sections)
            {
                this.ConvertMotorsport3Section(section, brake, report);
            }
            this.Type = wheel ? 3u : (brake ? 1u : 2u);
            this.motorsport3 = false;
            return report.ToString();
        }

        private byte[] ConvertMotorsport3Head(bool brake, bool wheel)
        {
            byte[] head = this.Head;
            int format = brake ? 4 : (wheel ? 0x18 : 0x104);
            int records = (int) ReadU32(head, format + 12);
            int tail = format + 16 + (records * 140);
            if (ReadU32(head, format) != 0xD || ReadU32(head, format + 4) != 0xB || head.Length != tail + 0x1AC)
            {
                throw new NotSupportedException("FM3 header layout not recognised.");
            }
            using (MemoryStream output = new MemoryStream())
            {
                byte[] prefix = new byte[format];
                Buffer.BlockCopy(head, 0, prefix, 0, format);
                prefix[3] = (byte) (wheel ? 3 : (brake ? 1 : 2));
                output.Write(prefix, 0, prefix.Length);
                if (wheel)
                {
                    foreach (float value in this.WheelMeasures())
                    {
                        WriteF32(output, value);
                    }
                }
                else if (!brake)
                {
                    output.Write(new byte[0x4C], 0, 0x4C);
                    WriteU32(output, 1);
                }
                WriteU32(output, 0x10);
                WriteU32(output, 0x10);
                output.Write(head, format + 8, 8 + (records * 140) + 0x114);
                // Blocks: FM3's four without the end floats, then 11 default ones, then the end floats.
                int blocks = tail + 0x114;
                output.Write(head, blocks, 0x88);
                for (int i = 0; i < 11; i++)
                {
                    foreach (float value in new float[] { 0, 0, 0, 0, 0, 1, 0, 0, 1 })
                    {
                        WriteF32(output, value);
                    }
                }
                float x = ReadF32(head, blocks + 0x88);
                float y = ReadF32(head, blocks + 0x8C);
                if (brake && x == 0 && y == 0)
                {
                    x = BrakeEndX;
                    y = BrakeEndY;
                }
                WriteF32(output, x);
                WriteF32(output, y);
                WriteU32(output, (brake || wheel) ? 0u : 1u);
                WriteU32(output, 1);
                WriteU32(output, 0);
                output.Write(head, blocks + 0x90, 8);
                return output.ToArray();
            }
        }

        private static float ReadF32(byte[] data, int offset)
        {
            return BitConverter.ToSingle(BitConverter.GetBytes(ReadU32(data, offset)), 0);
        }

        private void ConvertMotorsport3Section(Section section, bool brake, System.Text.StringBuilder report)
        {
            double[] lodPositions = Motorsport3Positions(section, section.Lod);
            double[] lod0Positions = Motorsport3Positions(section, section.Lod0);
            if (IsZeroBounds(section.Bounds))
            {
                // FM3 brakes carry no bounds; FM4's are centred on the vertices.
                SetBounds(section, lodPositions, lod0Positions);
            }
            else if (Escapes(section.Bounds, lodPositions) || Escapes(section.Bounds, lod0Positions))
            {
                // Some FM3 vertices lie outside their section's bounds; Encode would pull them in (up to 53 mm), so the bounds grow.
                GrowBounds(section, lodPositions, lod0Positions);
                report.AppendFormat(CultureInfo.InvariantCulture, "{0}: vertices outside the FM3 bounds, bounds grown\r\n", section.Name);
            }

            bool deformable = HasMotorsport3Damage(section.Lod) || HasMotorsport3Damage(section.Lod0);
            section.DamageLod = deformable ? Motorsport3Damage(section.Lod) : new Table();
            section.DamageLod0 = deformable ? Motorsport3Damage(section.Lod0) : new Table();
            bool morph = this.wheelMorph != 1;
            if (morph)
            {
                section.DamageLod = WheelMorphTable(lodPositions, 1);
                section.DamageLod0 = WheelMorphTable(lod0Positions, this.wheelMorph);
            }

            // UV offset/scale per group of subsections sharing vertices (the LODs of a material can).
            foreach (SubSection sub in section.SubSections)
            {
                sub.Pre5 = new byte[] { 0, 0, 0, 3, 0 };
                sub.Fixed36 = new byte[0x24];
                sub.Fixed36[0x23] = 2;
                sub.Uv = new float[8];
            }
            int[] uvOf = new int[0];
            int[] uv0Of = new int[0];
            List<float[]> lodUv = Motorsport3UvGroups(section, false, out uvOf);
            List<float[]> lod0Uv = Motorsport3UvGroups(section, true, out uv0Of);

            section.Lod = this.Motorsport3Vertices(section, section.Lod, lodPositions, false, lodUv, uvOf, brake);
            section.Lod0 = this.Motorsport3Vertices(section, section.Lod0, lod0Positions, true, lod0Uv, uv0Of, brake);

            section.Marker = new byte[] { 0, 0, 0, 5 };
            section.Unk1c[0] = 0x3F;
            section.Unk1c[1] = 0x80;
            section.Unk1c[2] = 0;
            section.Unk1c[3] = 0;
            // Then the mean vertex position relative to the origin (FM4 recomputed it on re-exported parts).
            double[] mean = (lod0Positions.Length > 0) ? lod0Positions : lodPositions;
            if (mean.Length > 0)
            {
                for (int c = 0; c < 3; c++)
                {
                    double sum = 0;
                    for (int i = c; i < mean.Length; i += 3)
                    {
                        sum += mean[i];
                    }
                    byte[] value = BitConverter.GetBytes((float) ((sum / (mean.Length / 3)) - section.Bounds[c]));
                    for (int k = 0; k < 4; k++)
                    {
                        section.Unk1c[4 + (c * 4) + k] = value[3 - k];
                    }
                }
            }
            section.Pre = new byte[] { (byte) (section.VbTest1 >> 24), (byte) (section.VbTest1 >> 16), (byte) (section.VbTest1 >> 8), (byte) section.VbTest1 };
            section.VbTest1 = 0;
            section.Post9 = new byte[] { 0, 0, 0, 1, (byte) ((deformable || morph) ? 1 : 0), 0, 0, 0, 2 };
            section.Mid = new byte[] { 0, 0, 0, 2 };
            report.AppendFormat(CultureInfo.InvariantCulture, "{0}: lod {1} lod0 {2} vertices{3}{4}\r\n", section.Name, section.Lod.Count, section.Lod0.Count, deformable ? ", deformable" : "",
                morph ? string.Format(CultureInfo.InvariantCulture, ", lod0 morph target x{0:0.###}", this.wheelMorph) : "");
        }

        // FM4 wheel morph table (wheels whose lod0 rim is at the 15-inch reference; 284 FM4 wheels, fm4dev\
        // Fm3DLC\tools\morphstat, wheeltbl, wheelpost9): 16 bytes per vertex of each buffer, big-endian
        // halves, the morph target then the vertex itself, each as (y/L, x/L, L, z/L) with L its distance
        // from (0, 0, 0). The game blends the lod0 towards the target to draw the rim at the car's size
        // (the "Morph_LOD0" of the source scenes; Aiden-Piercee on Discord); without it the rim stays at 15 inches
        // (Genesis Coupe HKS from the FM3 DLC, 2026-10-18). FM3 wheels carry no morph, so the target is
        // the vertex with Y and Z scaled by `scale` and X kept, as on FM4's NSX '97, DBR1, V8 Vantage, RS2
        // (most FM4 wheels scale the hub less). The lod (LOD 1-5) table holds the vertex twice, as FM4's
        // 273 such wheels do; the section's Post9 flag is 1.
        private static Table WheelMorphTable(double[] positions, double scale)
        {
            Table table = new Table();
            int count = positions.Length / 3;
            if (count == 0)
            {
                return table;
            }
            table.Count = count;
            table.Stride = 16;
            table.Data = new byte[count * 16];
            for (int i = 0; i < count; i++)
            {
                double x = positions[i * 3];
                double y = positions[(i * 3) + 1];
                double z = positions[(i * 3) + 2];
                WritePolar(table.Data, i * 16, x, y * scale, z * scale);
                WritePolar(table.Data, (i * 16) + 8, x, y, z);
            }
            table.ExtraFor = table.Data;
            return table;
        }

        private static void WritePolar(byte[] data, int offset, double x, double y, double z)
        {
            double length = Math.Sqrt((x * x) + (y * y) + (z * z));
            double[] values = (length < 1e-9) ? new double[4] : new double[] { y / length, x / length, length, z / length };
            for (int k = 0; k < 4; k++)
            {
                ushort half = ToHalf(values[k]);
                data[offset + (k * 2)] = (byte) (half >> 8);
                data[offset + (k * 2) + 1] = (byte) half;
            }
        }

        // IEEE half, rounded to nearest (no infinities or NaN are needed here).
        private static ushort ToHalf(double value)
        {
            int sign = (value < 0) ? 0x8000 : 0;
            double a = Math.Abs(value);
            if (a < Math.Pow(2, -24))
            {
                return (ushort) sign;
            }
            int exponent = (int) Math.Floor(Math.Log(a, 2));
            if (exponent < -14)
            {
                return (ushort) (sign | (int) Math.Round(a / Math.Pow(2, -24)));
            }
            int mantissa = (int) Math.Round(((a / Math.Pow(2, exponent)) - 1) * 1024);
            if (mantissa == 1024)
            {
                mantissa = 0;
                exponent++;
            }
            exponent = Math.Min(exponent, 15);
            return (ushort) (sign | ((exponent + 15) << 10) | mantissa);
        }

        private static bool IsZeroBounds(float[] bounds)
        {
            foreach (float value in bounds)
            {
                if (value != 0)
                {
                    return false;
                }
            }
            return true;
        }

        // True when a vertex is farther from the bounds' centre than the half diagonal, the limit Encode keeps vertices within.
        private static bool Escapes(float[] bounds, double[] positions)
        {
            double scale = Scale(bounds);
            for (int i = 0; i < positions.Length; i += 3)
            {
                double length = 0;
                for (int c = 0; c < 3; c++)
                {
                    double q = (positions[i + c] - bounds[c] - ((bounds[3 + c] + bounds[6 + c]) / 2)) / scale;
                    length += q * q;
                }
                if (Math.Sqrt(length) > 1.0)
                {
                    return true;
                }
            }
            return false;
        }

        // The game's bounds (origin at the vertices' AABB centre, symmetric, rounded outwards) for the vertices of both buffers.
        private static void GrowBounds(Section section, double[] a, double[] b)
        {
            double[] min = { double.MaxValue, double.MaxValue, double.MaxValue };
            double[] max = { double.MinValue, double.MinValue, double.MinValue };
            foreach (double[] p in new double[][] { a, b })
            {
                for (int i = 0; i < p.Length; i++)
                {
                    min[i % 3] = Math.Min(min[i % 3], p[i]);
                    max[i % 3] = Math.Max(max[i % 3], p[i]);
                }
            }
            if (min[0] > max[0])
            {
                return;
            }
            float[] origin;
            float[] half;
            SymmetricBounds(min, max, out origin, out half);
            for (int c = 0; c < 3; c++)
            {
                section.Bounds[c] = origin[c];
                section.Bounds[3 + c] = -half[c];
                section.Bounds[6 + c] = half[c];
            }
        }

        private static void SetBounds(Section section, double[] a, double[] b)
        {
            double[] lo = { double.MaxValue, double.MaxValue, double.MaxValue };
            double[] hi = { double.MinValue, double.MinValue, double.MinValue };
            foreach (double[] p in new double[][] { a, b })
            {
                for (int i = 0; i < p.Length; i++)
                {
                    lo[i % 3] = Math.Min(lo[i % 3], p[i]);
                    hi[i % 3] = Math.Max(hi[i % 3], p[i]);
                }
            }
            if (lo[0] > hi[0])
            {
                return;
            }
            for (int c = 0; c < 3; c++)
            {
                double half = (hi[c] - lo[c]) / 2;
                section.Bounds[c] = (float) ((hi[c] + lo[c]) / 2);
                section.Bounds[3 + c] = (float) -half;
                section.Bounds[6 + c] = (float) half;
            }
        }

        private static double Half(byte[] d, int o)
        {
            int h = (d[o] << 8) | d[o + 1];
            int e = (h >> 10) & 31;
            int m = h & 1023;
            double v = (e == 0) ? (m / 1024.0) * Math.Pow(2, -14) : (e == 31 ? 0 : (1 + (m / 1024.0)) * Math.Pow(2, e - 15));
            return ((h & 0x8000) != 0) ? -v : v;
        }

        // Packed 11:11:10 vector: x bits 0-10, y bits 11-21 (signed, /1023), z bits 22-31 (signed, /511).
        private static Vector3 Packed111110(byte[] d, int o)
        {
            uint w = ReadU32(d, o);
            int x = (int) (w & 0x7FF);
            int y = (int) ((w >> 11) & 0x7FF);
            int z = (int) (w >> 22);
            if (x >= 0x400)
            {
                x -= 0x800;
            }
            if (y >= 0x400)
            {
                y -= 0x800;
            }
            if (z >= 0x200)
            {
                z -= 0x400;
            }
            return new Vector3(x / 1023f, y / 1023f, z / 511f);
        }

        private static double[] Motorsport3Positions(Section section, VertexBuffer buffer)
        {
            double[] positions = new double[buffer.Count * 3];
            for (int i = 0; i < buffer.Count; i++)
            {
                int o = i * buffer.Stride;
                double w = Half(buffer.Data, o + 6);
                for (int c = 0; c < 3; c++)
                {
                    double center = (section.Bounds[3 + c] + section.Bounds[6 + c]) / 2;
                    positions[(i * 3) + c] = section.Bounds[c] + center + (Half(buffer.Data, o + (c * 2)) * w);
                }
            }
            return positions;
        }

        private static bool HasMotorsport3Damage(VertexBuffer buffer)
        {
            if (buffer.Stride != 40)
            {
                return false;
            }
            for (int i = 0; i < buffer.Count; i++)
            {
                if (ReadU32(buffer.Data, (i * 40) + 28) != Motorsport3PlainDamage)
                {
                    return true;
                }
            }
            return false;
        }

        private static Table Motorsport3Damage(VertexBuffer buffer)
        {
            Table table = new Table();
            if (buffer.Count == 0)
            {
                return table;
            }
            table.Count = buffer.Count;
            table.Stride = 4;
            table.Data = new byte[buffer.Count * 4];
            for (int i = 0; i < buffer.Count; i++)
            {
                Buffer.BlockCopy(buffer.Data, (i * 40) + 28, table.Data, i * 4, 4);
            }
            table.ExtraFor = table.Data;
            return table;
        }

        // Groups of subsections of one buffer (Lod0 or Lod) that share vertices, with their UV ranges:
        // per group 8 floats (offset/scale of U, V, U2, V2) as FM4 stores them per subsection.
        private static List<float[]> Motorsport3UvGroups(Section section, bool lod0, out int[] groupOf)
        {
            VertexBuffer buffer = lod0 ? section.Lod0 : section.Lod;
            List<SubSection> subs = new List<SubSection>();
            foreach (SubSection sub in section.SubSections)
            {
                if ((sub.Lod == 0) == lod0)
                {
                    subs.Add(sub);
                }
            }
            int[] parent = new int[subs.Count];
            for (int i = 0; i < parent.Length; i++)
            {
                parent[i] = i;
            }
            int[] owner = new int[buffer.Count];
            for (int i = 0; i < owner.Length; i++)
            {
                owner[i] = -1;
            }
            for (int s = 0; s < subs.Count; s++)
            {
                foreach (int index in subs[s].Indices)
                {
                    if (IsRestart(subs[s], index) || index >= buffer.Count)
                    {
                        continue;
                    }
                    if (owner[index] < 0)
                    {
                        owner[index] = s;
                    }
                    else
                    {
                        int a = Root(parent, owner[index]);
                        int b = Root(parent, s);
                        parent[a] = b;
                    }
                }
            }
            int uvSets = (buffer.Stride == 40) ? 2 : 1;
            Dictionary<int, int> groupIndex = new Dictionary<int, int>();
            List<float[]> ranges = new List<float[]>();
            int[] subGroup = new int[subs.Count];
            for (int s = 0; s < subs.Count; s++)
            {
                int root = Root(parent, s);
                int g;
                if (!groupIndex.TryGetValue(root, out g))
                {
                    g = ranges.Count;
                    groupIndex[root] = g;
                    ranges.Add(new float[] { float.MaxValue, float.MinValue, float.MaxValue, float.MinValue, float.MaxValue, float.MinValue, float.MaxValue, float.MinValue });
                }
                subGroup[s] = g;
            }
            groupOf = new int[buffer.Count];
            for (int i = 0; i < buffer.Count; i++)
            {
                groupOf[i] = (owner[i] < 0) ? -1 : subGroup[owner[i]];
                if (groupOf[i] < 0)
                {
                    continue;
                }
                float[] r = ranges[groupOf[i]];
                for (int channel = 0; channel < uvSets * 2; channel++)
                {
                    float value = (float) Half(buffer.Data, (i * buffer.Stride) + 8 + (channel * 2));
                    r[channel * 2] = Math.Min(r[channel * 2], value);
                    r[(channel * 2) + 1] = Math.Max(r[(channel * 2) + 1], value);
                }
            }
            List<float[]> parameters = new List<float[]>();
            foreach (float[] r in ranges)
            {
                float[] uv = new float[8];
                for (int channel = 0; channel < 4; channel++)
                {
                    if (channel < uvSets * 2 && r[channel * 2] <= r[(channel * 2) + 1])
                    {
                        uv[channel * 2] = r[channel * 2];
                        uv[(channel * 2) + 1] = Math.Max(r[(channel * 2) + 1] - r[channel * 2], 1e-6f);
                    }
                }
                parameters.Add(uv);
            }
            for (int s = 0; s < subs.Count; s++)
            {
                subs[s].Uv = (float[]) parameters[subGroup[s]].Clone();
            }
            return parameters;
        }

        private static int Root(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        // FM3 vertices (40, 16 or 44 bytes) -> FM4 32-byte vertices.
        private VertexBuffer Motorsport3Vertices(Section section, VertexBuffer buffer, double[] positions, bool lod0, List<float[]> uvGroups, int[] groupOf, bool brake)
        {
            VertexBuffer result = new VertexBuffer();
            if (buffer.Count == 0)
            {
                return result;
            }
            int stride = buffer.Stride;
            if (stride != 40 && stride != 16 && stride != 44)
            {
                throw new NotSupportedException(string.Format("{0}: FM3 vertex size {1} is not known.", section.Name, stride));
            }
            result.Count = buffer.Count;
            result.Stride = 0x20;
            result.Data = new byte[buffer.Count * 0x20];
            Encode(section, result, positions);

            // Without the stored frame axis (16- and 44-byte vertices), dP/dv from the UVs of the faces.
            Vector3[] bitangents = null;
            if (stride != 40)
            {
                bitangents = new Vector3[buffer.Count];
                foreach (SubSection sub in section.SubSections)
                {
                    if ((sub.Lod == 0) != lod0)
                    {
                        continue;
                    }
                    foreach (int[] face in StripTriangles(sub))
                    {
                        if (face[0] >= buffer.Count || face[1] >= buffer.Count || face[2] >= buffer.Count)
                        {
                            continue;
                        }
                        Vector3[] p = new Vector3[3];
                        float[] u = new float[3];
                        float[] v = new float[3];
                        for (int k = 0; k < 3; k++)
                        {
                            int i = face[k];
                            p[k] = new Vector3((float) positions[i * 3], (float) positions[(i * 3) + 1], (float) positions[(i * 3) + 2]);
                            u[k] = (float) Half(buffer.Data, (i * stride) + 8);
                            v[k] = (float) Half(buffer.Data, (i * stride) + 10);
                        }
                        Vector3 e1 = p[1] - p[0];
                        Vector3 e2 = p[2] - p[0];
                        float du1 = u[1] - u[0];
                        float dv1 = v[1] - v[0];
                        float du2 = u[2] - u[0];
                        float dv2 = v[2] - v[0];
                        float det = (du1 * dv2) - (du2 * dv1);
                        if (Math.Abs(det) < 1e-12f)
                        {
                            continue;
                        }
                        Vector3 b = ((e2 * du1) - (e1 * du2)) / det;
                        foreach (int i in face)
                        {
                            bitangents[i] += b;
                        }
                    }
                }
            }

            int uvSets = (stride == 40) ? 2 : 1;
            for (int i = 0; i < buffer.Count; i++)
            {
                int o = i * stride;
                byte[] vertex = new byte[0x20];
                Buffer.BlockCopy(result.Data, i * 0x20, vertex, 0, 8);
                float[] uv = (groupOf[i] >= 0) ? uvGroups[groupOf[i]] : new float[] { 0, 1, 0, 1, 0, 1, 0, 1 };
                for (int channel = 0; channel < uvSets * 2; channel++)
                {
                    double value = Half(buffer.Data, o + 8 + (channel * 2));
                    double scale = uv[(channel * 2) + 1];
                    double normalized = (scale > 0) ? (value - uv[channel * 2]) / scale : 0;
                    int raw = Math.Max(0, Math.Min(65535, (int) Math.Round(normalized * 65535)));
                    vertex[8 + (channel * 2)] = (byte) (raw >> 8);
                    vertex[9 + (channel * 2)] = (byte) raw;
                }
                Vector3 normal = Packed111110(buffer.Data, o + ((stride == 40) ? 16 : 12));
                if (stride == 40)
                {
                    WriteFrame(vertex, normal, Vector3.Zero, -Packed111110(buffer.Data, o + 36));
                    Buffer.BlockCopy(buffer.Data, o + 20, vertex, 24, 8);
                }
                else
                {
                    WriteFrame(vertex, normal, Vector3.Zero, bitangents[i]);
                    if (stride == 44)
                    {
                        Buffer.BlockCopy(buffer.Data, o + 16, vertex, 24, 8);
                    }
                    else
                    {
                        WriteTail(vertex, normal, 0x20);
                    }
                }
                Buffer.BlockCopy(vertex, 0, result.Data, i * 0x20, 0x20);
            }
            return result;
        }

        // The 12 floats FM4 wheel headers hold at 0x18 (fitted on FM4's 437 wheels; the material names
        // are the game's): wheel_black (lod0) min x, inner_rim (lod1) min x, lod0 max x, inner_rim (lod1)
        // max x, wheel_black min x again, tire (lod1) min x, wheel_black max x, tire max x, two radii
        // set by the lod0 rim (see Lod0Radius), inner_rim (lod1) radius, tire radius. A missing material
        // falls back on the whole LOD.
        private float[] WheelMeasures()
        {
            Section section = (this.Sections.Count > 0) ? this.Sections[0] : null;
            if (section == null)
            {
                return new float[12];
            }
            double[] lod = Motorsport3Positions(section, section.Lod);
            double[] lod0 = Motorsport3Positions(section, section.Lod0);
            double[] black = this.WheelMaterial(section, "wheel_black", true, lod, lod0);
            double[] inner = this.WheelMaterial(section, "inner_rim", false, lod, lod0);
            double[] tire = this.WheelMaterial(section, "tire", false, lod, lod0);
            double[] all0 = this.WheelMaterial(section, null, true, lod, lod0);
            this.wheelMorph = Lod0MorphScale(all0[2], inner[2]);
            float radius0 = (float) (all0[2] * this.wheelMorph);
            return new float[]
            {
                (float) black[0], (float) inner[0], (float) all0[1], (float) inner[1],
                (float) black[0], (float) tire[0], (float) black[1], (float) tire[1],
                radius0, (float) inner[2], radius0, (float) tire[2],
            };
        }

        // Header floats 8 and 10 = the largest YZ radius of the lod0 morph target. Most FM4 wheels model
        // the lod0 rim at a 15-inch reference size (YZ radius ~0.194) smaller than the lod1 rim, with a
        // morph target (see WheelMorphTable) whose rim reaches that radius + 0.130 (~0.324, 232 of 236
        // such wheels; fm4dev\Fm3DLC\tools\wheelf8, morphstat). A true-size lod0 has no morph and holds
        // its own radius (141 of 142). Returns the target's scale, 1 for a true-size lod0.
        private static double Lod0MorphScale(double lod0Radius, double innerRadius)
        {
            bool reference = lod0Radius > 0.19 && lod0Radius < 0.2 && innerRadius - lod0Radius > 0.02;
            return reference ? (lod0Radius + 0.1301) / lod0Radius : 1;
        }

        private double wheelMorph = 1;

        // A wheel whose LOD 0 is a true-size copy of LOD 1 (an FM2 rim, a traffic rim): LOD 0 becomes the
        // 15-inch reference of FM4 (largest YZ radius 'reference', as the 232 FM4 wheels of Lod0MorphScale) with
        // the morph table of both buffers (WheelMorphTable), so the game resizes it with the car's rim size
        // (rule 11.2, ROADMAP). Returns the morph target's radius, the value of header floats 8 and 10.
        public double MakeWheelMorph(double reference)
        {
            return this.FitWheelLod0(reference, (reference + 0.1301) / reference);
        }

        // The other way round: LOD 0 drawn at a fixed size whatever the car's rim size, its largest YZ radius
        // 'radius' and a morph target equal to the vertices (scale 1). For rims smaller than FM4's smallest
        // stock rim (13"), which the 15" reference morph may not reach (test 2026-11-07, FIA_595_68 12").
        // Returns the radius, the value of header floats 8 and 10.
        public double MakeWheelTrueSize(double radius)
        {
            return this.FitWheelLod0(radius, 1);
        }

        // A tire model (Media\wheels\Tire*.zip, LOD 0 only) at a fixed size: FM4's tires are modelled round the
        // 15" reference rim (inner radius 0.194, the rim's), the morph only stretching the sidewall (outer
        // 0.287 -> 0.414). For a rim under FM4's smallest (13") the tire stays at 15": here every LOD 0 vertex
        // is remapped radially so the inner radius becomes 'inner' and the outer 'outer' (tread profile kept,
        // X untouched), and a morph table becomes the identity. Header float 10 = 'outer'.
        public void MakeTireFixed(double inner, double outer)
        {
            foreach (Section section in this.Sections)
            {
                double[] lod = Positions(section, section.Lod);
                double[] lod0 = Positions(section, section.Lod0);
                double r0 = double.MaxValue;
                double r1 = 0;
                for (int i = 0; i < lod0.Length; i += 3)
                {
                    double r = Math.Sqrt((lod0[i + 1] * lod0[i + 1]) + (lod0[i + 2] * lod0[i + 2]));
                    r0 = Math.Min(r0, r);
                    r1 = Math.Max(r1, r);
                }
                if (r1 - r0 < 1e-6)
                {
                    continue;
                }
                double k = (outer - inner) / (r1 - r0);
                for (int i = 0; i < lod0.Length; i += 3)
                {
                    double y = lod0[i + 1];
                    double z = lod0[i + 2];
                    double r = Math.Sqrt((y * y) + (z * z));
                    double fit = (r < 1e-9) ? 1 : (inner + ((r - r0) * k)) / r;
                    lod0[i + 1] = y * fit;
                    lod0[i + 2] = z * fit;
                }
                if (section.DamageLod0.Stride == 16 && section.DamageLod0.Count == section.Lod0.Count)
                {
                    section.DamageLod0 = WheelMorphTable(lod0, 1);
                }
                Reencode(section, lod, lod0);
            }
            byte[] bytes = BitConverter.GetBytes((float) outer);
            for (int b = 0; b < 4; b++)
            {
                this.Head[0x18 + (10 * 4) + b] = bytes[3 - b];
            }
        }

        private double FitWheelLod0(double reference, double morph)
        {
            double target = 0;
            foreach (Section section in this.Sections)
            {
                double[] lod = Positions(section, section.Lod);
                double[] lod0 = Positions(section, section.Lod0);
                // Largest radius of the vertices LOD 0 draws (the buffer also holds the LOD 1 tire's).
                double radius = 0;
                foreach (SubSection sub in section.SubSections)
                {
                    if (sub.Lod != 0)
                    {
                        continue;
                    }
                    foreach (int index in sub.Indices)
                    {
                        if (!IsRestart(sub, index) && index * 3 < lod0.Length)
                        {
                            radius = Math.Max(radius, Math.Sqrt((lod0[(index * 3) + 1] * lod0[(index * 3) + 1]) + (lod0[(index * 3) + 2] * lod0[(index * 3) + 2])));
                        }
                    }
                }
                if (radius < 1e-6)
                {
                    continue;
                }
                double fit = reference / radius;
                for (int i = 0; i < lod0.Length; i += 3)
                {
                    lod0[i + 1] *= fit;
                    lod0[i + 2] *= fit;
                }
                section.DamageLod = WheelMorphTable(lod, 1);
                section.DamageLod0 = WheelMorphTable(lod0, morph);
                section.Post9[4] = 1;
                Reencode(section, lod, lod0);
                target = reference * morph;
            }
            return target;
        }

        // Min x, max x and largest YZ radius of a material's vertices (null: every material) at LOD 0
        // (lod0) or 1; the whole LOD when the material is not there.
        private double[] WheelMaterial(Section section, string material, bool lod0, double[] lod, double[] lod0Positions)
        {
            double[] result = { double.MaxValue, double.MinValue, 0 };
            for (int pass = 0; pass < 2 && result[0] > result[1]; pass++)
            {
                foreach (SubSection sub in section.SubSections)
                {
                    if (sub.Lod != (lod0 ? 0u : 1u) || (pass == 0 && material != null && sub.Name != material))
                    {
                        continue;
                    }
                    double[] p = lod0 ? lod0Positions : lod;
                    int count = p.Length / 3;
                    foreach (int index in sub.Indices)
                    {
                        if (IsRestart(sub, index) || index >= count)
                        {
                            continue;
                        }
                        result[0] = Math.Min(result[0], p[index * 3]);
                        result[1] = Math.Max(result[1], p[index * 3]);
                        result[2] = Math.Max(result[2], Math.Sqrt((p[(index * 3) + 1] * p[(index * 3) + 1]) + (p[(index * 3) + 2] * p[(index * 3) + 2])));
                    }
                }
            }
            if (result[0] > result[1])
            {
                return new double[3];
            }
            return result;
        }
    }
}
