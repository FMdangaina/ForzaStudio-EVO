namespace ForzaStudio
{
    using Microsoft.Xna.Framework;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.RegularExpressions;

    // The analogue gauges of a car's cockpit (found in default.xex 2026-11-07, E:\DEV\fm4dev\XexCars\NOTES_CARBIN.md):
    //  - the cockpit .carbin header (CBaseCarModelData, version 0x10) holds 15 gauges in a fixed order, each
    //    GaugePivot (vec4, model axes), GaugeAxis (vec4, stored as (-x, -y, z) of the model axis: the dial normal is
    //    (-ax, -ay, az) on every FM4 car, mean 0.9 degrees, axisconv harness), MaxRotation (float, radians). Gauge.Axis
    //    is the MODEL axis (Read / Write convert);
    //  - the needle the game turns is the section <name>_min (82906630 looks up "<name>_min*"); _mid and _max are
    //    the needle drawn at half and full scale by the artist. Turning _min about the model axis by +MaxRotation
    //    (right hand rule) lands on _max, and by half on _mid, at 0.0 - 1.8 mm on every FM4 car checked (gaugecheck2);
    //  - carattribs.xml <Gauges><Speed NumTicks="n"><TickMark Param="needle 0..1" Value="m/s, rpm..."/> maps the
    //    reading to the needle; NumTicks="0" = the reading is used as it is (fuel, the Volvo 850 boost). One mark
    //    gives nothing to interpolate (FH2 Delta S4 boost: needle spinning or gone); no FM4 file has one.
    public static class CarGauges
    {
        public const int Count = 15;
        public const int RecordSize = 36;

        // Header order = carattribs element names (FM4 / FH2 CarAttribs version >= 16 list all 15).
        public static readonly string[] XmlNames = { "Speed", "Tach", "Fuel", "Boost", "OilTemp", "OilPressure", "WaterTemp", "Volt", "GaugeA", "GaugeB", "GaugeC", "GaugeD", "GaugeE", "GaugeF", "Power" };

        public sealed class Gauge
        {
            public int Index;
            public Vector3 Pivot;
            public Vector3 Axis;
            public float MaxRotation;

            public string XmlName
            {
                get { return XmlNames[this.Index]; }
            }

            // The needle sections: speed_min, speed_mid, speed_max...
            public string SectionPrefix
            {
                get { return XmlNames[this.Index].ToLowerInvariant() + "_"; }
            }

            public bool IsDefault
            {
                // Unused record: pivot 0, header axis (0, 1, 0) = model (0, -1, 0), max 1.
                get { return this.Pivot == Vector3.Zero && this.Axis == -Vector3.UnitY && this.MaxRotation == 1f; }
            }

            public Gauge Copy()
            {
                return (Gauge) this.MemberwiseClone();
            }

            // Turns a point of the _min needle to the needle position t (0 = min, 1 = max).
            public Matrix NeedleTransform(float t)
            {
                if (this.Axis.LengthSquared() < 1e-12f)
                {
                    return Matrix.Identity;
                }
                return Matrix.CreateTranslation(-this.Pivot) * Matrix.CreateFromAxisAngle(Vector3.Normalize(this.Axis), t * this.MaxRotation) * Matrix.CreateTranslation(this.Pivot);
            }
        }

        // Offset of gauge 0 in the header of a car carbin (type 2 / FH converted), or -1. Checked two ways: walking the
        // header (emitters, driver controls, StartLOD / EndLOD) and from its end (2 floats, a u32, BoneMappingTable, 8 bytes).
        public static int HeaderOffset(byte[] head)
        {
            if (head == null || head.Length < 0x300 || ReadU32(head, 0) != 2 || ReadU32(head, 0x154) != 0x10)
            {
                return -1;
            }
            uint emitters = ReadU32(head, 0x160);
            if (emitters > 64)
            {
                return -1;
            }
            int walked = 0x164 + ((int) emitters * 140) + 4 + (16 * 16) + 8;
            int fromEnd = head.Length - 28 - (Count * RecordSize);
            return (walked == fromEnd) ? walked : -1;
        }

        public static Gauge[] Read(byte[] head)
        {
            int start = HeaderOffset(head);
            if (start < 0)
            {
                return null;
            }
            Gauge[] gauges = new Gauge[Count];
            for (int i = 0; i < Count; i++)
            {
                int o = start + (i * RecordSize);
                Gauge g = new Gauge();
                g.Index = i;
                g.Pivot = new Vector3(ReadFloat(head, o), ReadFloat(head, o + 4), ReadFloat(head, o + 8));
                g.Axis = new Vector3(-ReadFloat(head, o + 16), -ReadFloat(head, o + 20), ReadFloat(head, o + 24));
                g.MaxRotation = ReadFloat(head, o + 32);
                gauges[i] = g;
            }
            return gauges;
        }

        // The w components (+12, +28) are left as they are (0 in every file).
        public static void Write(byte[] head, Gauge gauge)
        {
            int start = HeaderOffset(head);
            if (start < 0)
            {
                throw new InvalidOperationException("The model header has no gauge table in the layout this editor knows.");
            }
            int o = start + (gauge.Index * RecordSize);
            WriteFloat(head, o, gauge.Pivot.X);
            WriteFloat(head, o + 4, gauge.Pivot.Y);
            WriteFloat(head, o + 8, gauge.Pivot.Z);
            WriteFloat(head, o + 16, -gauge.Axis.X);
            WriteFloat(head, o + 20, -gauge.Axis.Y);
            WriteFloat(head, o + 24, gauge.Axis.Z);
            WriteFloat(head, o + 32, gauge.MaxRotation);
        }

        // ---- carattribs tick marks: (Param = needle 0..1, Value = reading) ----

        public static List<PointF2> ReadTicks(string carAttributes, string name, out bool present)
        {
            List<PointF2> ticks = new List<PointF2>();
            Match gauge = GaugeElement(carAttributes, name);
            present = gauge.Success;
            if (gauge.Success)
            {
                foreach (Match mark in Regex.Matches(gauge.Value, "<TickMark\\b[^>]*/>"))
                {
                    ticks.Add(new PointF2(Attribute(mark.Value, "Param"), Attribute(mark.Value, "Value")));
                }
            }
            return ticks;
        }

        // Replaces (or adds inside <Gauges>) the gauge's element; no marks = NumTicks="0".
        public static string WriteTicks(string carAttributes, string name, List<PointF2> ticks)
        {
            string element;
            if (ticks.Count == 0)
            {
                element = "<" + name + " NumTicks=\"0\" />";
            }
            else
            {
                List<string> lines = new List<string>();
                foreach (PointF2 tick in ticks)
                {
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "      <TickMark Param=\"{0:0.000000}\" Value=\"{1:0.000000}\" />", tick.X, tick.Y));
                }
                element = "<" + name + " NumTicks=\"" + ticks.Count + "\">\r\n" + string.Join("\r\n", lines.ToArray()) + "\r\n    </" + name + ">";
            }
            Match gauge = GaugeElement(carAttributes, name);
            if (gauge.Success)
            {
                return carAttributes.Substring(0, gauge.Index) + element + carAttributes.Substring(gauge.Index + gauge.Length);
            }
            Match close = Regex.Match(carAttributes, "[ \\t]*</Gauges>");
            if (close.Success)
            {
                return carAttributes.Substring(0, close.Index) + "    " + element + "\r\n" + carAttributes.Substring(close.Index);
            }
            Match empty = Regex.Match(carAttributes, "<Gauges\\s*/>");
            if (empty.Success)
            {
                return carAttributes.Substring(0, empty.Index) + "<Gauges>\r\n    " + element + "\r\n  </Gauges>" + carAttributes.Substring(empty.Index + empty.Length);
            }
            throw new InvalidOperationException("carattribs.xml has no <Gauges> element.");
        }

        private static Match GaugeElement(string text, string name)
        {
            Match block = Regex.Match(text, "<Gauges>.*?</Gauges>", RegexOptions.Singleline);
            if (!block.Success)
            {
                return Match.Empty;
            }
            Regex element = new Regex("<" + name + "\\b[^>]*?/>|<" + name + "\\b[^>]*>.*?</" + name + ">", RegexOptions.Singleline);
            Match m = element.Match(text, block.Index, block.Length);
            return m;
        }

        // The reading at needle position t, from the marks (linear between marks, clamped at both ends);
        // with no marks the needle position is the reading itself.
        public static float ValueAt(List<PointF2> ticks, float t)
        {
            if (ticks.Count == 0)
            {
                return t;
            }
            if (ticks.Count == 1)
            {
                return ticks[0].Y;
            }
            if (t <= ticks[0].X)
            {
                return ticks[0].Y;
            }
            for (int i = 1; i < ticks.Count; i++)
            {
                if (t <= ticks[i].X)
                {
                    float span = ticks[i].X - ticks[i - 1].X;
                    float f = (span > 1e-9f) ? (t - ticks[i - 1].X) / span : 0f;
                    return ticks[i - 1].Y + (f * (ticks[i].Y - ticks[i - 1].Y));
                }
            }
            return ticks[ticks.Count - 1].Y;
        }

        // Problems the game would show: one mark, marks not rising, a needle without header data.
        public static List<string> Check(Gauge gauge, List<PointF2> ticks, bool hasNeedle)
        {
            List<string> problems = new List<string>();
            if (ticks.Count == 1)
            {
                problems.Add("one tick mark: FM4 needs none or at least 2 (the needle spins or vanishes)");
            }
            for (int i = 1; i < ticks.Count; i++)
            {
                if (ticks[i].X < ticks[i - 1].X || ticks[i].Y < ticks[i - 1].Y)
                {
                    problems.Add("tick marks not in rising order (" + (i + 1) + ")");
                    break;
                }
            }
            if (hasNeedle && gauge.IsDefault)
            {
                problems.Add("the needle has no pivot / axis in the model header (it does not turn)");
            }
            if (gauge.Axis.LengthSquared() < 1e-8f)
            {
                problems.Add("axis is zero");
            }
            return problems;
        }

        // ---- fit from the three poses ----

        // Pivot, axis and MaxRotation from the _min, _mid and _max needles (same vertices in the same order):
        // each vertex runs on a circle through its three positions; the circles' normals give the axis, their
        // centres the pivot, the turn min -> mid -> max the angle (made positive by the axis direction).
        // error = mean distance (m) between _min turned by the result and _max.
        public static bool Fit(double[] min, double[] mid, double[] max, out Vector3 pivot, out Vector3 axis, out float angle, out double error)
        {
            return Fit(min, mid, max, Vector3.Zero, out pivot, out axis, out angle, out error);
        }

        // keepAxis (not zero): the axis the header has already; only pivot and turn are fitted (it is the needles'
        // plane normal within a degree on FM4 cars).
        public static bool Fit(double[] min, double[] mid, double[] max, Vector3 keepAxis, out Vector3 pivot, out Vector3 axis, out float angle, out double error)
        {
            pivot = Vector3.Zero;
            axis = Vector3.UnitY;
            angle = 0f;
            error = double.NaN;
            int n = min.Length / 3;
            if (n == 0 || mid.Length != min.Length || max.Length != min.Length)
            {
                return false;
            }
            // A needle is nearly a line, so its three poses alone leave the axis loose (tens of degrees on FM4
            // cars); the three needles lie flat on the dial, so the axis is the normal of their common plane.
            double[] all = new double[min.Length * 3];
            Array.Copy(min, 0, all, 0, min.Length);
            Array.Copy(mid, 0, all, min.Length, min.Length);
            Array.Copy(max, 0, all, min.Length * 2, min.Length);
            if (!PlaneNormal(all, out axis))
            {
                return false;
            }
            Vector3 centreSum = Vector3.Zero;
            double weightSum = 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = P(min, i), b = P(mid, i), c = P(max, i);
                // The circle of each vertex is taken in the dial plane.
                a -= axis * Vector3.Dot(a - P(min, 0), axis);
                b -= axis * Vector3.Dot(b - P(min, 0), axis);
                c -= axis * Vector3.Dot(c - P(min, 0), axis);
                Vector3 u = b - a, v = c - a, w = Vector3.Cross(u, v);
                float w2 = w.LengthSquared();
                if (w2 < 1e-16f)
                {
                    continue;
                }
                Vector3 centre = a + (((Vector3.Cross(v, w) * u.LengthSquared()) + (Vector3.Cross(w, u) * v.LengthSquared())) / (2f * w2));
                centreSum += centre * w2;
                weightSum += w2;
            }
            if (weightSum <= 0)
            {
                return false;
            }
            // The centre in the dial plane, moved along the axis to the needles' mean depth.
            pivot = centreSum / (float) weightSum;
            Vector3 mean = Vector3.Zero;
            for (int i = 0; i < n; i++)
            {
                mean += P(min, i);
            }
            mean /= n;
            pivot += axis * Vector3.Dot(mean - pivot, axis);
            double angleSum = 0;
            double angleWeight = 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = Flat(P(min, i) - pivot, axis), b = Flat(P(mid, i) - pivot, axis), c = Flat(P(max, i) - pivot, axis);
                float r = a.Length();
                if (r < 1e-5f || b.Length() < 1e-5f || c.Length() < 1e-5f)
                {
                    continue;
                }
                double turn = Signed(a, b, axis) + Signed(b, c, axis);
                angleSum += turn * r;
                angleWeight += r;
            }
            if (angleWeight <= 0)
            {
                return false;
            }
            angle = (float) (angleSum / angleWeight);
            if (angle < 0)
            {
                axis = -axis;
                angle = -angle;
            }
            if (keepAxis.LengthSquared() > 1e-10f)
            {
                // The game's axis: pivot and turn refined so that _min turned about it lands on _max.
                Vector3 fixedAxis = Vector3.Normalize(keepAxis);
                if (Vector3.Dot(fixedAxis, axis) < 0)
                {
                    fixedAxis = -fixedAxis;
                }
                axis = fixedAxis;
                Refine(min, max, axis, ref pivot, ref angle);
                if (angle < 0)
                {
                    axis = -axis;
                    angle = -angle;
                }
                if (Vector3.Dot(axis, keepAxis) < 0)
                {
                    // Written with the header's own sign: the same turn.
                    axis = -axis;
                    angle = -angle;
                }
            }
            error = PoseError(min, max, pivot, axis, angle);
            return true;
        }

        // Coordinate descent on pivot (3) and angle (1), steps halving from 4 mm / 0.1 rad to 0.005 mm.
        private static void Refine(double[] min, double[] max, Vector3 axis, ref Vector3 pivot, ref float angle)
        {
            double best = PoseError(min, max, pivot, axis, angle);
            float step = 0.004f, turn = 0.1f;
            while (step > 5e-6f)
            {
                bool better = false;
                for (int k = 0; k < 4; k++)
                {
                    foreach (int sign in new int[] { 1, -1 })
                    {
                        Vector3 p = pivot;
                        float a = angle;
                        if (k == 0) p.X += sign * step;
                        else if (k == 1) p.Y += sign * step;
                        else if (k == 2) p.Z += sign * step;
                        else a += sign * turn;
                        double e = PoseError(min, max, p, axis, a);
                        if (e < best)
                        {
                            best = e;
                            pivot = p;
                            angle = a;
                            better = true;
                        }
                    }
                }
                if (!better)
                {
                    step /= 2f;
                    turn /= 2f;
                }
            }
        }

        // ---- header axis against the needles' plane ----

        // FM4 header axes are the needles' plane normal within about 4 degrees; the ACU_NSX_97 speed axis is 19.6
        // degrees off (T10 swapped its X and Y): turned about it the needle sweeps a cone into the dial.
        public const double AxisTiltWarning = 5.0;

        // Degrees between the header axis and the normal of the plane through the _min, _mid and _max needles,
        // NaN when it cannot be measured (pose missing, vertex counts differ, zero axis).
        public static double AxisTilt(Gauge gauge, double[] min, double[] mid, double[] max)
        {
            if (gauge == null || min == null || mid == null || max == null || min.Length == 0 || mid.Length != min.Length || max.Length != min.Length || gauge.Axis.LengthSquared() < 1e-12f)
            {
                return double.NaN;
            }
            double[] all = new double[min.Length * 3];
            Array.Copy(min, 0, all, 0, min.Length);
            Array.Copy(mid, 0, all, min.Length, min.Length);
            Array.Copy(max, 0, all, min.Length * 2, min.Length);
            Vector3 normal;
            if (!PlaneNormal(all, out normal))
            {
                return double.NaN;
            }
            double cos = Math.Abs(Vector3.Dot(Vector3.Normalize(gauge.Axis), Vector3.Normalize(normal)));
            return MathHelper.ToDegrees((float) Math.Acos(Math.Min(1.0, cos)));
        }

        // The tab / validator text for a tilted axis, or null when it is within AxisTiltWarning.
        public static string AxisTiltProblem(Gauge gauge, double[] min, double[] mid, double[] max)
        {
            double tilt = AxisTilt(gauge, min, mid, max);
            if (double.IsNaN(tilt) || tilt <= AxisTiltWarning)
            {
                return null;
            }
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "header axis {0:0.0} degrees off the plane of the {1}min/mid/max needles (FM4 cars: under {2:0}): the needle turns on a cone into the dial - Fit From Poses with \"Fit also the axis\" fixes it",
                tilt, gauge.SectionPrefix, AxisTiltWarning);
        }

        // The _min, _mid, _max needle positions (model axes) of a gauge in a model, null where a pose is missing
        // (same reading as the GAUGES tab: LOD0 buffer, else the LOD one).
        public static void ReadPoses(CarbinModel model, Gauge gauge, out double[] min, out double[] mid, out double[] max)
        {
            min = null;
            mid = null;
            max = null;
            foreach (CarbinModel.Section section in model.Sections)
            {
                string lower = section.Name.ToLowerInvariant();
                if (!lower.StartsWith(gauge.SectionPrefix))
                {
                    continue;
                }
                CarbinModel.VertexBuffer buffer = (section.Lod0.Count > 0) ? section.Lod0 : section.Lod;
                string pose = lower.Substring(gauge.SectionPrefix.Length);
                if (pose.StartsWith("min") && min == null)
                {
                    min = CarbinModel.Positions(section, buffer);
                }
                else if (pose.StartsWith("mid") && mid == null)
                {
                    mid = CarbinModel.Positions(section, buffer);
                }
                else if (pose.StartsWith("max") && max == null)
                {
                    max = CarbinModel.Positions(section, buffer);
                }
            }
        }

        // Mean distance (m) between the _min needle turned by angle about the axis and the _max needle.
        public static double PoseError(double[] min, double[] max, Vector3 pivot, Vector3 axis, float angle)
        {
            int n = Math.Min(min.Length, max.Length) / 3;
            if (n == 0 || axis.LengthSquared() < 1e-12f)
            {
                return double.NaN;
            }
            Matrix turn = Matrix.CreateTranslation(-pivot) * Matrix.CreateFromAxisAngle(Vector3.Normalize(axis), angle) * Matrix.CreateTranslation(pivot);
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                sum += (Vector3.Transform(P(min, i), turn) - P(max, i)).Length();
            }
            return sum / n;
        }

        // Normal of the best plane through the points: the eigenvector of the smallest eigenvalue of their
        // covariance (Jacobi rotations on the 3 x 3 matrix).
        private static bool PlaneNormal(double[] p, out Vector3 normal)
        {
            normal = Vector3.UnitY;
            int n = p.Length / 3;
            if (n < 3)
            {
                return false;
            }
            double[] mean = new double[3];
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < 3; k++)
                {
                    mean[k] += p[(i * 3) + k] / n;
                }
            }
            double[,] c = new double[3, 3];
            for (int i = 0; i < n; i++)
            {
                for (int r = 0; r < 3; r++)
                {
                    for (int s = 0; s < 3; s++)
                    {
                        c[r, s] += (p[(i * 3) + r] - mean[r]) * (p[(i * 3) + s] - mean[s]);
                    }
                }
            }
            double[,] v = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            for (int sweep = 0; sweep < 50; sweep++)
            {
                double off = Math.Abs(c[0, 1]) + Math.Abs(c[0, 2]) + Math.Abs(c[1, 2]);
                if (off < 1e-18)
                {
                    break;
                }
                for (int r = 0; r < 2; r++)
                {
                    for (int s = r + 1; s < 3; s++)
                    {
                        if (Math.Abs(c[r, s]) < 1e-20)
                        {
                            continue;
                        }
                        double theta = 0.5 * Math.Atan2(2 * c[r, s], c[s, s] - c[r, r]);
                        double cs = Math.Cos(theta), sn = Math.Sin(theta);
                        for (int k = 0; k < 3; k++)
                        {
                            double ckr = c[k, r], cks = c[k, s];
                            c[k, r] = (cs * ckr) - (sn * cks);
                            c[k, s] = (sn * ckr) + (cs * cks);
                        }
                        for (int k = 0; k < 3; k++)
                        {
                            double crk = c[r, k], csk = c[s, k];
                            c[r, k] = (cs * crk) - (sn * csk);
                            c[s, k] = (sn * crk) + (cs * csk);
                        }
                        for (int k = 0; k < 3; k++)
                        {
                            double vkr = v[k, r], vks = v[k, s];
                            v[k, r] = (cs * vkr) - (sn * vks);
                            v[k, s] = (sn * vkr) + (cs * vks);
                        }
                    }
                }
            }
            int smallest = 0;
            for (int k = 1; k < 3; k++)
            {
                if (c[k, k] < c[smallest, smallest])
                {
                    smallest = k;
                }
            }
            normal = new Vector3((float) v[0, smallest], (float) v[1, smallest], (float) v[2, smallest]);
            if (normal.LengthSquared() < 1e-12f)
            {
                return false;
            }
            normal.Normalize();
            return true;
        }

        private static double Signed(Vector3 from, Vector3 to, Vector3 axis)
        {
            return Math.Atan2(Vector3.Dot(axis, Vector3.Cross(from, to)), Vector3.Dot(from, to));
        }

        private static Vector3 Flat(Vector3 v, Vector3 axis)
        {
            return v - (axis * Vector3.Dot(v, axis));
        }

        private static Vector3 P(double[] p, int i)
        {
            return new Vector3((float) p[i * 3], (float) p[(i * 3) + 1], (float) p[(i * 3) + 2]);
        }

        private static float Attribute(string element, string name)
        {
            Match m = Regex.Match(element, "\\b" + name + "=\"([^\"]*)\"");
            float value;
            return (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) ? value : 0f;
        }

        private static uint ReadU32(byte[] b, int o)
        {
            return (uint) ((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);
        }

        private static float ReadFloat(byte[] b, int o)
        {
            byte[] le = { b[o + 3], b[o + 2], b[o + 1], b[o] };
            return BitConverter.ToSingle(le, 0);
        }

        private static void WriteFloat(byte[] b, int o, float value)
        {
            byte[] le = BitConverter.GetBytes(value);
            for (int k = 0; k < 4; k++)
            {
                b[o + k] = le[3 - k];
            }
        }
    }

    // A tick mark: X = needle position 0..1 (Param), Y = reading (Value).
    public struct PointF2
    {
        public float X;
        public float Y;

        public PointF2(float x, float y)
        {
            this.X = x;
            this.Y = y;
        }
    }
}
