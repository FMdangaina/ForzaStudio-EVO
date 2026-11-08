namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using System.Text.RegularExpressions;

    // Engine sound rules checked in game on the FH1 Fiat 595 (2026-11-08, TODO 10.7, tests 6 and 8):
    //  - Cylinders: an N-cylinder sound from an M-cylinder bank. Same firing rate, so rpm(N) = rpm(M) x M / N
    //    (4 cylinders at 1000 rpm fire as 2 at 2000). Every Loop's rpm_sample / rpm_min / rpm_max of the harmonic tuning
    //    x M / N, except the first rpm_min and the last rpm_max (the ends of the range) kept. The bank is the same fsb; the
    //    scaled tuning is a new file <name>_<N>cyl_HT.xml. M = the leading number of the HT name (4N3_..., 8N2_...), else
    //    the cylinders of the donor's engine.
    //    The ET's EngineSettings num_cylinders scales the same way (FM4 writes it as the cylinders or as half of them,
    //    e.g. 2 for most 4-cylinder ETs: the donor's own convention is kept): round(donor value x N / M), at least 1, 0 kept.
    //  - Engine position (the user picks front / mid / rear): the car model tuning (CMT) <Chassis> EngineIntake and
    //    EngineAmbient z as a ratio of ExhaustL z, and the <Listener> volumes / surround pans that differ between FM4's
    //    front- and mid-engined cars, set to their FM4 medians (E:\DEV\fm4dev\Fm1_595\cmtplace.cs: 372 front, 112 mid
    //    cars whose CMT agrees with Data_Car.EnginePlacementID; only 1 such rear car, so rear = mid). Only the audio reads
    //    the CMT; Data_Car.EnginePlacementID is not used by the sound.
    internal static class SoundConvert
    {
        public const double IntakeRatio = 0.72;            // engine behind: z / ExhaustL z
        public const double AmbientRatio = 0.76;
        public const double FrontIntakeRatio = -0.687;     // engine in front: z / ExhaustL z
        public const double FrontAmbientRatio = -0.574;

        // Listener attribute (element path under <Car>/<Listener>, @attribute) -> FM4 median for engine in front, behind.
        private static readonly object[][] ListenerTable =
        {
            new object[] { "Advanced/Cockpit/SurroundPanEngineAmbient@Center", 1.0, 0.2 },
            new object[] { "Advanced/Cockpit/SurroundPanEngineAmbient@Left", 0.6, 0.4 },
            new object[] { "Advanced/Cockpit/SurroundPanEngineAmbient@LeftRear", 0.0, 1.0 },
            new object[] { "Advanced/Cockpit/SurroundPanEngineAmbient@Right", 0.6, 0.4 },
            new object[] { "Advanced/Cockpit/SurroundPanEngineAmbient@RightRear", 0.0, 1.0 },
            new object[] { "Advanced/Cockpit/SurroundPanEngineIntake@Center", 1.0, 0.4 },
            new object[] { "Advanced/Cockpit/SurroundPanEngineIntake@Left", 0.6, 0.5 },
            new object[] { "Advanced/Cockpit/SurroundPanEngineIntake@LeftRear", 0.0, 1.0 },
            new object[] { "Advanced/Cockpit/SurroundPanEngineIntake@Right", 0.6, 0.5 },
            new object[] { "Advanced/Cockpit/SurroundPanEngineIntake@RightRear", 0.0, 1.0 },
            new object[] { "Advanced/Cockpit/SurroundPanExhaustL@Center", 0.5, 0.3 },
            new object[] { "Advanced/Cockpit/SurroundPanExhaustR@Center", 0.5, 0.3 },
            new object[] { "Advanced/Cockpit/SurroundPanExhaustR@Right", 0.5, 0.3 },
            new object[] { "Advanced/Follow/SurroundPanExhaustL@Right", 0.5, 0.0 },
            new object[] { "Advanced/Front/SurroundPanEngineAmbient@Center", 1.0, 0.2 },
            new object[] { "Advanced/Front/SurroundPanEngineAmbient@Left", 0.6, 0.4 },
            new object[] { "Advanced/Front/SurroundPanEngineAmbient@LeftRear", 0.0, 1.0 },
            new object[] { "Advanced/Front/SurroundPanEngineAmbient@Right", 0.6, 0.4 },
            new object[] { "Advanced/Front/SurroundPanEngineAmbient@RightRear", 0.0, 1.0 },
            new object[] { "Advanced/Front/SurroundPanEngineIntake@Center", 1.0, 0.4 },
            new object[] { "Advanced/Front/SurroundPanEngineIntake@Left", 0.6, 0.5 },
            new object[] { "Advanced/Front/SurroundPanEngineIntake@LeftRear", 0.0, 1.0 },
            new object[] { "Advanced/Front/SurroundPanEngineIntake@Right", 0.6, 0.5 },
            new object[] { "Advanced/Front/SurroundPanEngineIntake@RightRear", 0.0, 1.0 },
            new object[] { "Advanced/Front/SurroundPanExhaustL@Center", 0.5, 0.3 },
            new object[] { "Advanced/Front/SurroundPanExhaustR@Center", 0.5, 0.3 },
            new object[] { "Advanced/Front/SurroundPanExhaustR@Right", 0.5, 0.3 },
            new object[] { "Basic/Cockpit/EmissionGroupVolumes@EngineAmbient", 0.8, 0.78 },
            new object[] { "Basic/Cockpit/EmissionGroupVolumes@EngineIntake", 1.0, 0.9 },
            new object[] { "Basic/Cockpit/EmissionGroupVolumes@ExhaustL", 0.4, 0.35 },
            new object[] { "Basic/Cockpit/EmissionGroupVolumes@ExhaustR", 0.5, 0.45 },
            new object[] { "Basic/Cockpit/SurroundPanEngineAmbient@Center", 1.0, 0.6 },
            new object[] { "Basic/Cockpit/SurroundPanEngineAmbient@Left", 0.6, 1.0 },
            new object[] { "Basic/Cockpit/SurroundPanEngineAmbient@LeftRear", 0.0, 1.0 },
            new object[] { "Basic/Cockpit/SurroundPanEngineAmbient@Right", 0.6, 1.0 },
            new object[] { "Basic/Cockpit/SurroundPanEngineAmbient@RightRear", 0.0, 1.0 },
            new object[] { "Basic/Cockpit/SurroundPanEngineIntake@Center", 1.0, 0.6 },
            new object[] { "Basic/Cockpit/SurroundPanEngineIntake@Left", 0.6, 1.0 },
            new object[] { "Basic/Cockpit/SurroundPanEngineIntake@LeftRear", 0.0, 1.0 },
            new object[] { "Basic/Cockpit/SurroundPanEngineIntake@Right", 0.6, 1.0 },
            new object[] { "Basic/Cockpit/SurroundPanEngineIntake@RightRear", 0.0, 1.0 },
            new object[] { "Basic/Cockpit/SurroundPanExhaustL@Center", 0.5, 0.6 },
            new object[] { "Basic/Cockpit/SurroundPanExhaustR@Center", 0.5, 0.6 },
            new object[] { "Basic/Follow/SurroundPanExhaustL@Right", 0.5, 0.0 },
            new object[] { "Basic/Front/EmissionGroupVolumes@EngineAmbient", 0.8, 0.65 },
            new object[] { "Basic/Front/EmissionGroupVolumes@EngineIntake", 0.8, 0.65 },
            new object[] { "Basic/Front/EmissionGroupVolumes@ExhaustL", 0.8, 0.65 },
            new object[] { "Basic/Front/EmissionGroupVolumes@ExhaustR", 0.8, 0.65 },
            new object[] { "Basic/Front/SurroundPanEngineAmbient@Center", 1.0, 0.6 },
            new object[] { "Basic/Front/SurroundPanEngineAmbient@Left", 0.6, 1.0 },
            new object[] { "Basic/Front/SurroundPanEngineAmbient@LeftRear", 0.0, 1.0 },
            new object[] { "Basic/Front/SurroundPanEngineAmbient@Right", 0.6, 1.0 },
            new object[] { "Basic/Front/SurroundPanEngineAmbient@RightRear", 0.0, 1.0 },
            new object[] { "Basic/Front/SurroundPanEngineIntake@Center", 1.0, 0.6 },
            new object[] { "Basic/Front/SurroundPanEngineIntake@Left", 0.6, 1.0 },
            new object[] { "Basic/Front/SurroundPanEngineIntake@LeftRear", 0.0, 1.0 },
            new object[] { "Basic/Front/SurroundPanEngineIntake@Right", 0.6, 1.0 },
            new object[] { "Basic/Front/SurroundPanEngineIntake@RightRear", 0.0, 1.0 },
            new object[] { "Basic/Front/SurroundPanExhaustL@Center", 0.5, 0.6 },
            new object[] { "Basic/Front/SurroundPanExhaustR@Center", 0.5, 0.6 },
        };

        private static readonly Encoding Latin = Encoding.GetEncoding(28591);
        private static readonly Regex HtReference = new Regex("\"([^\"]+)_HT\\.xml\"", RegexOptions.IgnoreCase);

        public sealed class Result
        {
            public string Tuning;                                                    // the new engine tuning text
            public readonly Dictionary<string, string> Harmonics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);  // new HT file name -> text
            public readonly List<string> Log = new List<string>();
        }

        // The cylinders of the bank a harmonic tuning plays (the leading number of its name), or 0.
        public static int BankCylinders(string htName)
        {
            Match m = Regex.Match(htName, "^([0-9]+)");
            int n;
            return (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) ? n : 0;
        }

        // The harmonic tuning's loops for the rpm factor (see the class note).
        public static string ScaleHarmonic(string text, double factor)
        {
            Regex loop = new Regex("<Loop [^>]*/>");
            int count = loop.Matches(text).Count, index = 0;
            return loop.Replace(text, delegate(Match m)
            {
                int k = index++;
                string line = m.Value;
                foreach (string attribute in new string[] { "rpm_min", "rpm_sample", "rpm_max" })
                {
                    if ((attribute == "rpm_min" && k == 0) || (attribute == "rpm_max" && k == count - 1))
                    {
                        continue;
                    }
                    string name = attribute;
                    line = Regex.Replace(line, name + "=\"([0-9.]+)\"", delegate(Match v)
                    {
                        double rpm = double.Parse(v.Groups[1].Value, CultureInfo.InvariantCulture) * factor;
                        return name + "=\"" + Math.Round(rpm).ToString(CultureInfo.InvariantCulture) + "\"";
                    });
                }
                return line;
            });
        }

        // The donor's engine tuning turned into a 'target'-cylinder one. readHarmonic: HT file name -> bytes or null.
        // donorCylinders: used when an HT name has no leading number. HTs already of 'target' cylinders are kept as they are.
        public static Result Cylinders(byte[] tuning, Func<string, byte[]> readHarmonic, int donorCylinders, int target)
        {
            if (target <= 0)
            {
                throw new ArgumentException("The number of cylinders must be 1 or more.");
            }
            Result result = new Result();
            string et = Latin.GetString(tuning);
            List<string> names = new List<string>();
            foreach (Match m in HtReference.Matches(et))
            {
                string name = m.Groups[1].Value;
                if (!names.Exists(delegate(string s) { return s.Equals(name, StringComparison.OrdinalIgnoreCase); }))
                {
                    names.Add(name);
                }
            }
            foreach (string name in names)
            {
                int bank = BankCylinders(name);
                if (bank <= 0)
                {
                    bank = donorCylinders;
                }
                if (bank <= 0)
                {
                    throw new InvalidOperationException("The cylinders of the bank of " + name + "_HT.xml are not known (no leading number in its name, no cylinders in the donor's engine).");
                }
                if (bank == target)
                {
                    result.Log.Add(name + "_HT.xml: already " + target + " cylinders, kept");
                    continue;
                }
                byte[] data = readHarmonic(name + "_HT.xml");
                if (data == null)
                {
                    throw new InvalidOperationException("The harmonic tuning " + name + "_HT.xml was not found.");
                }
                double factor = (double) bank / target;
                string newName = name + "_" + target.ToString(CultureInfo.InvariantCulture) + "cyl_HT.xml";
                result.Harmonics[newName] = ScaleHarmonic(Latin.GetString(data), factor);
                et = Regex.Replace(et, "\"" + Regex.Escape(name + "_HT.xml") + "\"", "\"" + newName + "\"", RegexOptions.IgnoreCase);
                result.Log.Add(string.Format(CultureInfo.InvariantCulture, "{0}_HT.xml: {1} cylinders -> {2}, rpm x {3:0.###} -> {4}", name, bank, target, factor, newName));
            }
            if (result.Harmonics.Count > 0 && donorCylinders > 0 && donorCylinders != target)
            {
                et = Regex.Replace(et, "(<EngineSettings\\b[^>]*\\bnum_cylinders=\")([0-9]+)(\")", delegate(Match m)
                {
                    int was = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    int now = (was == 0) ? 0 : Math.Max(1, (int) Math.Round((double) was * target / donorCylinders, MidpointRounding.AwayFromZero));
                    result.Log.Add("ET num_cylinders " + was + " -> " + now + " (x " + target + " / " + donorCylinders + ")");
                    return m.Groups[1].Value + now.ToString(CultureInfo.InvariantCulture) + m.Groups[3].Value;
                });
            }
            result.Tuning = et;
            return result;
        }

        private static readonly string Origin = "<OriginPosition x=\"[-0-9.eE+]+\" y=\"[-0-9.eE+]+\" z=\"";

        private static Match Z(string cmt, string section)
        {
            return Regex.Match(cmt, "<Chassis>.*?<" + section + ">\\s*" + Origin + "([-0-9.eE+]+)\"", RegexOptions.Singleline);
        }

        private static double Number(Match m)
        {
            return double.Parse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        // True when the CMT has the engine in front of the cabin (EngineAmbient z > 0, z + = front) and the exhaust behind.
        public static bool EngineInFront(string cmt)
        {
            Match ambient = Z(cmt, "EngineAmbient"), exhaust = Z(cmt, "ExhaustL");
            return ambient.Success && exhaust.Success && Number(ambient) > 0 && Number(exhaust) < 0;
        }

        // The CMT with the engine in front of (behind = false) or behind the cabin (mid / rear): positions and listener
        // values (see the class note); log gets what changed.
        public static string Place(string cmt, bool behind, List<string> log)
        {
            Match exhaust = Z(cmt, "ExhaustL");
            if (!exhaust.Success)
            {
                throw new InvalidOperationException("The car model tuning has no <Chassis> ExhaustL position.");
            }
            double z = Number(exhaust);
            if (z >= 0)
            {
                throw new InvalidOperationException("The exhaust of this car model tuning is not behind the middle of the car (z " + z.ToString(CultureInfo.InvariantCulture) + "): the engine cannot be placed from it.");
            }
            cmt = SetZ(cmt, "EngineIntake", z * (behind ? IntakeRatio : FrontIntakeRatio), log);
            cmt = SetZ(cmt, "EngineAmbient", z * (behind ? AmbientRatio : FrontAmbientRatio), log);
            return SetListener(cmt, behind ? 2 : 1, log);
        }

        // Every Listener attribute of the table set to the value of column 'column' (1 front, 2 behind); others untouched.
        private static string SetListener(string cmt, int column, List<string> log)
        {
            Dictionary<string, double> table = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (object[] row in ListenerTable)
            {
                table[(string) row[0]] = (double) row[column];
            }
            int changed = 0;
            List<string> stack = new List<string>();
            string result = Regex.Replace(cmt, "<(/?)([A-Za-z0-9_]+)([^>]*?)(/?)>", delegate(Match m)
            {
                if (m.Groups[1].Value == "/")
                {
                    if (stack.Count > 0)
                    {
                        stack.RemoveAt(stack.Count - 1);
                    }
                    return m.Value;
                }
                string tag = m.Value;
                if (stack.Count >= 2 && stack[0] == "Car" && stack[1] == "Listener")
                {
                    string path = string.Join("/", stack.GetRange(2, stack.Count - 2).ToArray());
                    path = (path.Length > 0 ? path + "/" : "") + m.Groups[2].Value;
                    string attributes = Regex.Replace(m.Groups[3].Value, "([A-Za-z0-9_]+)=\"([-0-9.eE+]+)\"", delegate(Match a)
                    {
                        double value;
                        if (!table.TryGetValue(path + "@" + a.Groups[1].Value, out value))
                        {
                            return a.Value;
                        }
                        string text = value.ToString("0.000000", CultureInfo.InvariantCulture);
                        if (text != a.Groups[2].Value)
                        {
                            changed++;
                        }
                        return a.Groups[1].Value + "=\"" + text + "\"";
                    });
                    tag = "<" + m.Groups[2].Value + attributes + m.Groups[4].Value + ">";
                }
                if (m.Groups[4].Value != "/")
                {
                    stack.Add(m.Groups[2].Value);
                }
                return tag;
            });
            if (log != null)
            {
                log.Add("CMT listener: " + changed + " value(s) set to FM4's " + (column == 2 ? "mid-engine" : "front-engine") + " medians (surround pans, volumes)");
            }
            return result;
        }

        private static string SetZ(string cmt, string section, double z, List<string> log)
        {
            Regex r = new Regex("(<Chassis>.*?<" + section + ">\\s*" + Origin + ")([-0-9.eE+]+)(\")", RegexOptions.Singleline);
            if (!r.IsMatch(cmt))
            {
                throw new InvalidOperationException("The car model tuning has no <Chassis> " + section + " position.");
            }
            return r.Replace(cmt, delegate(Match m)
            {
                string value = z.ToString("0.000000", CultureInfo.InvariantCulture);
                if (log != null)
                {
                    log.Add("CMT " + section + " z " + m.Groups[2].Value + " -> " + value);
                }
                return m.Groups[1].Value + value + m.Groups[3].Value;
            }, 1);
        }
    }
}
