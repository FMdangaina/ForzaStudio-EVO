namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using Microsoft.Xna.Framework;

    // FM4 -> FM3 conversion of a .carbin: the inverse of ConvertMotorsport3ToMotorsport4 (CarbinModelMotorsport3.cs), so the
    // model is then written in FM3's layout by ToBytes (sections end with Lod0, 9-byte subsection heads, no UV floats).
    // Tested on the Supra RZ '98 (both games): FM3 -> FM4 -> FM3 against FM3's file (docs/PORT_FM4_TO_FM3.md).
    //  - header: format block [0x10, 0x10, 1, N] -> [0xD, 0xB, 1, N]; the 0x50 bytes FM4 cars have before it (wheels: the 12
    //    measured floats) go; of FM4's 15 blocks of 0x24 bytes FM3 keeps the first 4; FM4's three words before the final 8
    //    bytes go; brakes get FM3's 0 end floats;
    //  - sections: marker 5 -> 2; FM4's Pre word is FM3's word before VbTest2; Post9, the damage tables and Mid go;
    //    subsections get FM3's constant 9 bytes (2, 0, 2) and lose UV offset/scale and the fixed 36 bytes;
    //  - vertices: positions as 4 halves (unit direction from origin + centre, then the distance), UVs as halves, the frame's
    //    X (normal) and Z axes as 11:11:10 words. 40 bytes on car parts: FM4's 4 S16 values at 20, the damage word at 28 (FM4's
    //    damage table, 000003FF on parts that do not deform), 0 at 32 (a word FM4 dropped; 0 on 11170 of the FM3 Supra's
    //    80434 main-file vertices), the Z axis at 36. 16 bytes on brakes (position, uv0, normal), 44 on wheels (position,
    //    uv0, normal, the 4 S16 values, then FM3's constant 20 bytes).
    public partial class CarbinModel
    {
        private static readonly byte[] Motorsport3SubHead = { 0, 0, 0, 2, 0, 0, 0, 0, 2 };

        private bool morphBaked;   // a wheel whose lod0 was set to its true size (BakeWheelMorph)

        // The last 20 bytes of FM3's 44-byte wheel vertices: the same on all 10218 vertices of the Supra's wheel.
        private static readonly byte[] Motorsport3WheelTail = { 0, 0, 3, 0xFF, 0, 0, 0, 0, 0x3F, 0x80, 0, 0, 0, 0, 3, 0xFF, 0, 0, 0, 0 };

        public string ConvertMotorsport4ToMotorsport3()
        {
            if (this.IsMotorsport3 || this.IsHorizon)
            {
                throw new InvalidOperationException("Not a Forza Motorsport 4 model.");
            }
            System.Text.StringBuilder report = new System.Text.StringBuilder();
            bool brake = this.Type == 1;
            bool wheel = this.Type == 3;
            this.Head = this.ConvertMotorsport4Head(brake, wheel);
            foreach (Section section in this.Sections)
            {
                this.ConvertMotorsport4Section(section, brake, wheel, report);
            }
            if (this.morphBaked)
            {
                // lod0 is now at the true size like the 183 FM3 wheels with a flag of 0: the word at 4 (1 on 109 of the 130 FM3 wheels with a
                // 15-inch lod0, a leftover of FM3's export, also copied into FM4 files) and the float after it go.
                Array.Clear(this.Head, 4, 8);
            }
            this.Type = wheel ? 2u : 1u;
            this.motorsport3 = true;
            return report.ToString();
        }

        private byte[] ConvertMotorsport4Head(bool brake, bool wheel)
        {
            byte[] head = this.Head;
            int format = brake ? 4 : (wheel ? 0x18 : 0x104);
            int block = format + (brake ? 0 : (wheel ? 48 : 0x50));
            if (head.Length < block + 16 || ReadU32(head, block) != 0x10 || ReadU32(head, block + 4) != 0x10)
            {
                throw new NotSupportedException("FM4 header layout not recognised.");
            }
            int records = (int) ReadU32(head, block + 12);
            int tail = block + 16 + (records * 140);
            int blocks = tail + 0x114;
            if (head.Length != blocks + 0x88 + (11 * 0x24) + 8 + 12 + 8)
            {
                throw new NotSupportedException("FM4 header layout not recognised.");
            }
            using (MemoryStream output = new MemoryStream())
            {
                byte[] prefix = new byte[format];
                Buffer.BlockCopy(head, 0, prefix, 0, format);
                prefix[0] = 0;
                prefix[1] = 0;
                prefix[2] = 0;
                prefix[3] = (byte) (wheel ? 2 : 1);
                // The rest of the prefix is leftover memory in both games, but for the floats of main files; a car file
                // whose word at 4 is small would not read as FM3 (IsMotorsport3File): FM3's lod0 / cockpit value then.
                if (!brake && !wheel && ReadU32(prefix, 4) < 0x10000)
                {
                    prefix[4] = 0;
                    prefix[5] = 0x3A;
                    prefix[6] = 0;
                    prefix[7] = 0;
                }
                output.Write(prefix, 0, prefix.Length);
                WriteU32(output, 0xD);
                WriteU32(output, 0xB);
                output.Write(head, block + 8, 8 + (records * 140) + 0x114);
                output.Write(head, blocks, 0x88);
                int ends = blocks + 0x88 + (11 * 0x24);
                if (brake)
                {
                    output.Write(new byte[8], 0, 8);
                }
                else
                {
                    output.Write(head, ends, 8);
                }
                output.Write(head, head.Length - 8, 8);
                return output.ToArray();
            }
        }

        private void ConvertMotorsport4Section(Section section, bool brake, bool wheel, System.Text.StringBuilder report)
        {
            double[] lod = Positions(section, section.Lod);
            double[] lod0 = Positions(section, section.Lod0);
            if (wheel)
            {
                string baked = this.BakeWheelMorph(section, lod, lod0);
                this.morphBaked = this.morphBaked || baked != null;
                if (baked != null)
                {
                    report.AppendLine(section.Name + ": " + baked);
                    if (OutsideBounds(section.Bounds, lod0))
                    {
                        GrowBounds(section, lod, lod0);
                    }
                }
            }
            bool deformable = !wheel && section.IsDeformable;
            SubSection[] lodOwner = Owners(section, false);
            SubSection[] lod0Owner = Owners(section, true);
            if (brake)
            {
                // FM3 brakes carry no bounds: positions are model space.
                section.Bounds = new float[9];
            }
            int stride = brake ? 16 : (wheel ? 44 : 40);
            section.Lod = Motorsport3Buffer(section, section.Lod, lod, lodOwner, deformable ? section.DamageLod : null, stride);
            section.Lod0 = Motorsport3Buffer(section, section.Lod0, lod0, lod0Owner, deformable ? section.DamageLod0 : null, stride);

            section.Marker = new byte[] { 0, 0, 0, 2 };
            section.VbTest1 = (section.Pre != null && section.Pre.Length == 4) ? ReadU32(section.Pre, 0) : 0;
            section.Pre = new byte[0];
            // Unk1c float 0: 1.0 on every FM4 section; FM3 has 1.0 or 0 (see Motorsport3PartFlag), 1.0 in wheel files.
            if (!wheel && !Motorsport3PartFlag(section.Name))
            {
                section.Unk1c[0] = 0;
                section.Unk1c[1] = 0;
            }
            section.Post9 = new byte[0];
            section.Mid = new byte[0];
            section.DamageLod = new Table();
            section.DamageLod0 = new Table();
            foreach (SubSection sub in section.SubSections)
            {
                sub.Pre5 = (byte[]) Motorsport3SubHead.Clone();
                sub.Fixed36 = new byte[0];
            }
            report.AppendFormat(CultureInfo.InvariantCulture, "{0}: lod {1} lod0 {2} vertices{3}\r\n", section.Name, section.Lod.Count, section.Lod0.Count, deformable ? ", deformable" : "");
        }

        // FM4 draws most wheels' lod0 rim at a 15-inch reference and resizes it with a morph table (16 bytes per vertex: the morph
        // target first, see WheelMorphTable) to the car's rim; FM3 has no morph and holds the true size in lod0. The game's amount is
        // taken from the LOD 1 rim, which is at the car's size: t = (LOD 1 inner_rim radius - lod0 base) / (morph target - lod0 base),
        // on 3 FM4 wheels 0.41 (Cobalt SS, LOD 1 0.248 between 0.194 and 0.325) and 0 (C1, a 15-inch rim already). lod0 becomes
        // base + t * (target - base). Returns a report line, null when the wheel has no morph table or needs none.
        private string BakeWheelMorph(Section section, double[] lod, double[] lod0)
        {
            Table table = section.DamageLod0;
            int count = lod0.Length / 3;
            if (table == null || table.Stride != 16 || table.Count != count || count == 0)
            {
                return null;
            }
            double[] target = new double[lod0.Length];
            for (int i = 0; i < count; i++)
            {
                double length = Half(table.Data, (i * 16) + 4);
                target[(i * 3) + 1] = Half(table.Data, i * 16) * length;
                target[i * 3] = Half(table.Data, (i * 16) + 2) * length;
                target[(i * 3) + 2] = Half(table.Data, (i * 16) + 6) * length;
            }
            string material = "inner_rim";
            double final = this.WheelMaterial(section, material, false, lod, lod0)[2];
            double baseRadius = this.WheelMaterial(section, material, true, lod, lod0)[2];
            double targetRadius = this.WheelMaterial(section, material, true, lod, target)[2];
            if (final <= 0 || targetRadius - baseRadius < 0.01)
            {
                return null;
            }
            double t = Math.Max(0, Math.Min(1, (final - baseRadius) / (targetRadius - baseRadius)));
            if (t < 0.01)
            {
                return string.Format(CultureInfo.InvariantCulture, "lod0 rim radius {0:0.0000} already the LOD 1 rim's {1:0.0000}, no morph applied", baseRadius, final);
            }
            for (int i = 0; i < lod0.Length; i++)
            {
                lod0[i] += t * (target[i] - lod0[i]);
            }
            return string.Format(CultureInfo.InvariantCulture, "lod0 morphed by {0:0.000} towards its target (rim radius {1:0.0000} -> {2:0.0000}, the LOD 1 rim's {3:0.0000})", t, baseRadius, baseRadius + (t * (targetRadius - baseRadius)), final);
        }

        private static bool OutsideBounds(float[] bounds, double[] positions)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                int c = i % 3;
                if (positions[i] < bounds[c] + bounds[3 + c] || positions[i] > bounds[c] + bounds[6 + c])
                {
                    return true;
                }
            }
            return false;
        }

        // FM3's Unk1c float 0 (meaning unknown, FM4 dropped it to 1.0): 1.0 on the bolt-on parts, bumpers, hood, skirts, trunk,
        // wings, mirrors and exhausts, 0 on the rest (body, glass, frames, interior, gauges). The value of most sections of
        // that name on 40 FM3 cars (fm4fm3\f0_census.txt: e.g. bumperF 98 of 117, wing 126 of 131, body 2 of 80 at 1.0); the
        // FM3 artists did not set it the same way on every car, so the FM4 data cannot give it back exactly.
        private static bool Motorsport3PartFlag(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.StartsWith("bumperframe"))
            {
                return false;
            }
            foreach (string part in new string[] { "bumperf", "bumperr", "hood", "skirt", "trunk", "wing", "mirror", "exhaust" })
            {
                if (n.StartsWith(part))
                {
                    return true;
                }
            }
            return false;
        }

        // The subsection drawing each vertex of the Lod0 (lod0) or Lod buffer, for its UV offset/scale; null when none does.
        private static SubSection[] Owners(Section section, bool lod0)
        {
            VertexBuffer buffer = lod0 ? section.Lod0 : section.Lod;
            SubSection[] owner = new SubSection[buffer.Count];
            foreach (SubSection sub in section.SubSections)
            {
                if ((sub.Lod == 0) != lod0)
                {
                    continue;
                }
                foreach (int index in sub.Indices)
                {
                    if (!IsRestart(sub, index) && index < owner.Length && owner[index] == null)
                    {
                        owner[index] = sub;
                    }
                }
            }
            return owner;
        }

        private static VertexBuffer Motorsport3Buffer(Section section, VertexBuffer buffer, double[] positions, SubSection[] owner, Table damage, int stride)
        {
            VertexBuffer result = new VertexBuffer();
            if (buffer.Count == 0)
            {
                return result;     // FM3 writes an empty buffer with stride 0
            }
            result.Count = buffer.Count;
            result.Stride = stride;
            result.Data = new byte[buffer.Count * stride];
            SubSection plain = new SubSection();
            plain.Uv = new float[] { 0, 1, 0, 1, 0, 1, 0, 1 };
            int uvSets = (stride == 40) ? 2 : 1;
            for (int i = 0; i < buffer.Count; i++)
            {
                int o = i * stride;
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
                    WriteHalf(result.Data, o + (c * 2), (length > 1e-12) ? d[c] / length : 0);
                }
                WriteHalf(result.Data, o + 6, length);
                SubSection sub = owner[i] ?? plain;
                for (int set = 0; set < uvSets; set++)
                {
                    Vector2 uv = VertexUv(buffer, i, sub, set);
                    WriteHalf(result.Data, o + 8 + (set * 4), uv.X);
                    WriteHalf(result.Data, o + 10 + (set * 4), uv.Y);
                }
                Quaternion q = Frame(buffer, i);
                Vector3 normal = Vector3.Transform(Vector3.UnitX, q);
                int fm4 = i * buffer.Stride;
                if (stride == 40)
                {
                    WritePacked111110(result.Data, o + 16, normal);
                    Buffer.BlockCopy(buffer.Data, fm4 + 24, result.Data, o + 20, 8);
                    if (damage != null && damage.Stride == 4 && i < damage.Count)
                    {
                        Buffer.BlockCopy(damage.Data, i * 4, result.Data, o + 28, 4);
                    }
                    else
                    {
                        WriteBigU32(result.Data, o + 28, Motorsport3PlainDamage);
                    }
                    WritePacked111110(result.Data, o + 36, Vector3.Transform(Vector3.UnitZ, q));
                }
                else
                {
                    WritePacked111110(result.Data, o + 12, normal);
                    if (stride == 44)
                    {
                        Buffer.BlockCopy(buffer.Data, fm4 + 24, result.Data, o + 16, 8);
                        Buffer.BlockCopy(Motorsport3WheelTail, 0, result.Data, o + 24, 20);
                    }
                }
            }
            return result;
        }

        private static void WriteHalf(byte[] data, int offset, double value)
        {
            ushort half = ToHalf(value);
            data[offset] = (byte) (half >> 8);
            data[offset + 1] = (byte) half;
        }

        private static void WriteBigU32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte) (value >> 24);
            data[offset + 1] = (byte) (value >> 16);
            data[offset + 2] = (byte) (value >> 8);
            data[offset + 3] = (byte) value;
        }

        // Inverse of Packed111110: x bits 0-10 and y bits 11-21 (x 1023), z bits 22-31 (x 511), signed.
        private static void WritePacked111110(byte[] data, int offset, Vector3 v)
        {
            int x = Math.Max(-1023, Math.Min(1023, (int) Math.Round(v.X * 1023)));
            int y = Math.Max(-1023, Math.Min(1023, (int) Math.Round(v.Y * 1023)));
            int z = Math.Max(-511, Math.Min(511, (int) Math.Round(v.Z * 511)));
            uint word = (uint) (x & 0x7FF) | ((uint) (y & 0x7FF) << 11) | ((uint) (z & 0x3FF) << 22);
            WriteBigU32(data, offset, word);
        }
    }
}
