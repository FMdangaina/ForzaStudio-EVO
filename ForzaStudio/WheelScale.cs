namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    // Does a wheel model (Media\wheels\<name>.zip, carbin type 3) follow the car's rim size from the
    // database? FM4 draws LOD 0 blended towards its morph target (the 16-byte Morph_LOD0 table, see
    // CarbinModel.WheelMorphTable) by the rim size; most FM4 wheels model LOD 0 at the 15-inch reference
    // (largest YZ radius ~0.194) with a target ~0.130 larger. A wheel without that table (or with a target
    // equal to the vertices) is drawn at its modelled size whatever the database says: not scalable.
    // MakeScalable converts it as the FM2 port does (CarbinModel.MakeWheelMorph + header floats 8 and 10).
    public static class WheelScale
    {
        public const double Reference = 0.194;     // LOD 0 rim radius of FM4's 15-inch reference wheels
        private const int HeaderFloats = 0x18;     // the 12 wheel floats of the header (8 and 10 = target radius)

        public sealed class Status
        {
            public bool IsWheel;
            public bool Scalable;
            public double Lod0Radius;              // largest YZ radius LOD 0 draws (the rim at its modelled size)
            public double TargetRadius;            // the morph target's (0 when there is none)
            public double HeaderRadius;            // header float 8
            public string Reason = "";

            public override string ToString()
            {
                if (!this.IsWheel)
                {
                    return "not a wheel model";
                }
                if (this.Scalable)
                {
                    return string.Format(CultureInfo.InvariantCulture, "scalable: LOD 0 rim radius {0:0.000} ({1:0.#}\" as modelled), morph target {2:0.000}, header {3:0.000}",
                        this.Lod0Radius, Inches(this.Lod0Radius), this.TargetRadius, this.HeaderRadius);
                }
                return string.Format(CultureInfo.InvariantCulture, "NOT scalable ({0}): LOD 0 rim radius {1:0.000} (~{2:0.#}\"), drawn at this size whatever the database rim size",
                    this.Reason, this.Lod0Radius, Inches(this.Lod0Radius));
            }
        }

        // Rim diameter in inches from a radius in metres (the 15" reference rim is ~0.19).
        public static double Inches(double radius)
        {
            return radius * 2 / 0.0254;
        }

        public static Status Check(CarbinModel model)
        {
            Status status = new Status();
            status.IsWheel = model != null && model.Type == 3 && !model.IsHorizon && model.Sections.Count > 0;
            if (!status.IsWheel)
            {
                return status;
            }
            if (model.Head != null && model.Head.Length >= HeaderFloats + 48)
            {
                status.HeaderRadius = ReadFloat(model.Head, HeaderFloats + 32);
            }
            bool allMorph = true;
            foreach (CarbinModel.Section section in model.Sections)
            {
                double[] lod0 = CarbinModel.Positions(section, section.Lod0);
                double radius = Lod0Radius(section, lod0);
                if (radius < 1e-6)
                {
                    continue;
                }
                status.Lod0Radius = Math.Max(status.Lod0Radius, radius);
                CarbinModel.Table table = section.MorphLod0;
                if (!section.HasMorphs || table.Stride != 16 || table.Count != section.Lod0.Count || table.Data.Length < table.Count * 16)
                {
                    allMorph = false;
                    status.Reason = section.HasMorphs ? "Morph_LOD0 table missing or not 16 bytes per vertex" : "no Morph_LOD0 table";
                    continue;
                }
                double target = 0;
                double moved = 0;
                for (int i = 0; i < table.Count; i++)
                {
                    double[] a = Polar(table.Data, i * 16);
                    double[] b = Polar(table.Data, (i * 16) + 8);
                    target = Math.Max(target, Math.Sqrt((a[1] * a[1]) + (a[2] * a[2])));
                    moved = Math.Max(moved, Math.Abs(a[3] - b[3]));
                }
                status.TargetRadius = Math.Max(status.TargetRadius, target);
                if (moved < 0.005)
                {
                    allMorph = false;
                    status.Reason = "Morph_LOD0 target equals the vertices";
                }
            }
            status.Scalable = allMorph && status.Lod0Radius > 0 && status.TargetRadius > status.Lod0Radius + 0.01;
            if (!status.Scalable && status.Reason.Length == 0)
            {
                status.Reason = "morph target not larger than LOD 0";
            }
            return status;
        }

        // LOD 0 -> 15" reference + morph table, header floats 8 and 10 = the target radius. Returns a line for the report.
        public static string MakeScalable(CarbinModel model)
        {
            Status before = Check(model);
            double target = model.MakeWheelMorph(Reference);
            byte[] bytes = BitConverter.GetBytes((float) target);
            for (int f = 8; f <= 10; f += 2)
            {
                for (int k = 0; k < 4; k++)
                {
                    model.Head[HeaderFloats + (f * 4) + k] = bytes[3 - k];
                }
            }
            return string.Format(CultureInfo.InvariantCulture, "LOD 0 rim {0:0.000} ({1:0.#}\") -> 15\" reference {2:0.000} with morph table, header radius {3:0.0000}",
                before.Lod0Radius, Inches(before.Lod0Radius), Reference, target);
        }

        // LOD 0 at a fixed rim radius (no resizing with the database rim size): morph target = vertices,
        // header floats 8 and 10 = that radius. Returns a line for the report.
        public static string MakeTrueSize(CarbinModel model, double radius)
        {
            Status before = Check(model);
            double value = model.MakeWheelTrueSize(radius);
            byte[] bytes = BitConverter.GetBytes((float) value);
            for (int f = 8; f <= 10; f += 2)
            {
                for (int k = 0; k < 4; k++)
                {
                    model.Head[HeaderFloats + (f * 4) + k] = bytes[3 - k];
                }
            }
            return string.Format(CultureInfo.InvariantCulture, "LOD 0 rim {0:0.000} ({1:0.#}\") -> fixed {2:0.000} ({3:0.#}\"), morph target = vertices",
                before.Lod0Radius, Inches(before.Lod0Radius), value, Inches(value));
        }

        // Largest YZ radius of the vertices LOD 0 subsections draw (as MakeWheelMorph measures it).
        private static double Lod0Radius(CarbinModel.Section section, double[] lod0)
        {
            double radius = 0;
            foreach (CarbinModel.SubSection sub in section.SubSections)
            {
                if (sub.Lod != 0)
                {
                    continue;
                }
                foreach (int index in sub.Indices)
                {
                    if (!CarbinModel.IsRestart(sub, index) && index * 3 + 2 < lod0.Length)
                    {
                        double y = lod0[(index * 3) + 1];
                        double z = lod0[(index * 3) + 2];
                        radius = Math.Max(radius, Math.Sqrt((y * y) + (z * z)));
                    }
                }
            }
            return radius;
        }

        // One morph entry half: (y/L, x/L, L, z/L) big-endian halves -> (x, y, z, L).
        private static double[] Polar(byte[] data, int offset)
        {
            double[] v = new double[4];
            for (int k = 0; k < 4; k++)
            {
                v[k] = Half((ushort) ((data[offset + (k * 2)] << 8) | data[offset + (k * 2) + 1]));
            }
            double length = v[2];
            return new double[] { v[1] * length, v[0] * length, v[3] * length, length };
        }

        private static double Half(ushort h)
        {
            int sign = (h >> 15) != 0 ? -1 : 1;
            int exponent = (h >> 10) & 0x1F;
            int mantissa = h & 0x3FF;
            if (exponent == 0)
            {
                return sign * mantissa * Math.Pow(2, -24);
            }
            return sign * (1 + (mantissa / 1024.0)) * Math.Pow(2, exponent - 15);
        }

        private static float ReadFloat(byte[] data, int offset)
        {
            byte[] value = { data[offset + 3], data[offset + 2], data[offset + 1], data[offset] };
            return BitConverter.ToSingle(value, 0);
        }
    }
}
