namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;

    // FM4 ONLY (2026-11-16). FM4's own performance numbers of a stock car, computed as default.xex does (the fm4perf harness,
    // E:\DEV\fm4dev\ST02\perf\fm4, rules and addresses in docs/FM4/PI_AND_RATINGS.md): the stock engine's torque table and
    // peaks, the five ratings (828A81E8: means of sim measurements over speeds) and the PI (828A7880), a simulated lap on the
    // virtual track of Media\Physics\PI.xml with FM4's corner geometry, exit spirals and per drive type scales. On the 504 rows
    // of FM4_Extracted: Braking 502, Handling 500, Sim peaks 501, Speed 466/471, Acceleration 485/501, Launch 494/502, PI 296
    // within 0.001, 500 within 0.005 (max 0.0054; 0.001 .. 0.005 accepted by the user 2026-11-16). Misses: hybrids / EVs.
    // Never FM2's or FM3's code (Fm2Perf, Fm3Perf) and never used for another game: each game has its own calculation.
    public static class Fm4Perf
    {
        public sealed class Result
        {
            public double Pi = double.NaN;      // NaN when Media\Physics\PI.xml is missing
            public double LapTime;              // seconds on the PI track
            public double SpeedRating, AccelerationRating, LaunchRating, BrakingRating, HandlingRating;
            public double TopSpeed, Accel, Launch, Brake, Corner;   // the measurements behind the ratings (m/s, m/s2)
            public double Mass, FrontWeight;    // kg, front share 0..1 (Data_Car.CurbWeight x 100, WeightDistribution)
            public double PeakT, PeakTw, PeakP, PeakPw, RedlineW;   // Data_Car units (N.m / 100, rad/s, W / 100)
        }

        // The Data_Car columns the game itself rewrites together (UPDATE string 82015F80, as FM3).
        public static readonly string[] Columns =
        {
            "PerformanceIndex", "ClassID", "SpeedRating", "AccelerationRating", "LaunchRating", "BrakingRating", "HandlingRating",
            "SimPeakPower", "SimPeakAngVel", "SimPeakTorque", "SimPeakTorqueAngVel", "SimRedlineAngVel", "CurbWeight", "WeightDistribution"
        };

        private const double G = 9.80665, K = 0.01 * 9.80665 * 2.2239485e-4, MPH = 0.44704, HP = 7.456999, W = 0.1047197, DEG = 0.0174532924;
        private const double WheelInertiaScale = 1.25;  // a FIT: List_TireCompound.MomentInertia x 1.25 (FM4's per-axle value at 0x123C / 0x1A7C not traced)
        private const double OwnGearsBelowLevel = 2;    // a FIT: the PI lap uses the car's own gears when the stock transmission Level < 2 (config + 0xC80 not traced)

        private static double D(object o)
        {
            return (o == null || o is DBNull) ? 0 : Convert.ToDouble(o, CultureInfo.InvariantCulture);
        }

        private static double Clamp(double v, double lo, double hi)
        {
            return Math.Max(lo, Math.Min(hi, v));
        }

        public static string PiXml(string media)
        {
            return Path.Combine(Path.Combine(media ?? "", "Physics"), "PI.xml");
        }

        // A copy with an engine or gearbox of its own: its PI, class, five ratings, Sim peaks, weight and weight distribution
        // computed as the game does and stored in Data_Car as a WHOLE set (never a part: all or nothing). The Sim times, top
        // speed and the figures of the car's page are not part of the game's calculation: they stay scaled from the source's
        // (CarPerformance). media: the GAME's Media folder (Physics\PI.xml; a pack's stage has none, CarDuplicate.PerfMedia).
        // userSet: Data_Car columns typed by the user, kept.
        public static void Update(SqliteDatabase db, SqliteDatabase original, long sourceId, long carId, string media, ICollection<string> userSet, List<string> plan)
        {
            Result c = Compute(db, carId, media);
            if (c == null)
            {
                plan.Add("performance (FM4): not recomputed (the car's engine, gearbox, weight or tires could not be read); the PI, class and ratings stay the source's");
                return;
            }
            if (double.IsNaN(c.Pi))
            {
                plan.Add("performance (FM4): not recomputed (no " + PiXml(media) + "); the PI, class and ratings stay the source's");
                return;
            }
            List<string> have = DbIds.Schema(db)["Data_Car"];
            foreach (string name in Columns)
            {
                string column = name;
                if (!have.Exists(delegate(string x) { return string.Equals(x, column, StringComparison.OrdinalIgnoreCase); }))
                {
                    plan.Add("performance (FM4): not recomputed (Data_Car has no " + column + "); the PI, class and ratings stay the source's");
                    return;
                }
            }

            CarPerformance.Update(db, original, sourceId, carId, userSet, plan, true);

            bool piTyped = userSet != null && userSet.Contains("PerformanceIndex");
            double pi = piTyped ? D(db.Scalar("SELECT PerformanceIndex FROM Data_Car WHERE Id = ?", carId)) : c.Pi;
            object classId = CarPort.ClassFor(db, pi, carId) ?? db.Scalar("SELECT ClassID FROM Data_Car WHERE Id = ?", carId);   // the class follows the PI
            object[] values =
            {
                c.Pi, classId, c.SpeedRating, c.AccelerationRating, c.LaunchRating, c.BrakingRating, c.HandlingRating,
                c.PeakP, c.PeakPw, c.PeakT, c.PeakTw, c.RedlineW, c.Mass * 0.01, c.FrontWeight
            };
            List<string> skipped = new List<string>();
            for (int i = 0; i < Columns.Length; i++)
            {
                if (userSet != null && userSet.Contains(Columns[i]))
                {
                    skipped.Add(Columns[i]);
                    continue;
                }
                db.Execute("UPDATE Data_Car SET " + SqliteDatabase.Quote(Columns[i]) + " = ? WHERE Id = ?", values[i], carId);
            }
            double oldPi = D(original.Scalar("SELECT PerformanceIndex FROM Data_Car WHERE Id = ?", sourceId));
            plan.Add(string.Format(CultureInfo.InvariantCulture,
                "performance (FM4, the game's own calculation): PI {0} -> {1} (lap {2:0.0} s), speed {3:0.00}, acceleration {4:0.00}, launch {5:0.00}, braking {6:0.00}, handling {7:0.00}, {8:0} kW at {9:0} rpm, {10:0} N.m at {11:0} rpm, {12:0} kg, {13:0.0}% front{14}",
                Shown(original, oldPi), Shown(db, pi), c.LapTime, c.SpeedRating, c.AccelerationRating, c.LaunchRating, c.BrakingRating, c.HandlingRating,
                c.PeakP / 10, c.PeakPw / W, c.PeakT * 100, c.PeakTw / W, c.Mass, c.FrontWeight * 100,
                skipped.Count == 0 ? "" : "; typed by you, kept: " + string.Join(", ", skipped.ToArray())));
        }

        // "C 412": the class of the PI and the number the game shows.
        public static string Shown(SqliteDatabase db, double pi)
        {
            return CarPort.ClassName(db, CarPort.ClassFor(db, pi, -1)) + " " + CarPerformance.ShownPi(db, pi).ToString(CultureInfo.InvariantCulture);
        }

        // null when the car cannot be computed (rows missing). media: the game's Media folder; without Physics\PI.xml the
        // ratings and peaks are computed and Pi stays NaN.
        public static Result Compute(SqliteDatabase db, long id, string media)
        {
            try
            {
                Model model = new Model(db);
                model.LoadPi(PiXml(media));
                return model.Car(id);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // The log rating of a measurement (828A81E8, scale 7; FM3 6.9), 3 .. 10.
        private static double Rate(double x, double lo, double hi)
        {
            return Clamp(((Math.Log(Math.Max(x, 0.01)) - Math.Log(lo)) / (Math.Log(hi) - Math.Log(lo)) * 7) + 3, 3, 10);
        }

        private static double Lerp(double x, double x0, double x1, double y0, double y1)
        {
            double lo = Math.Min(x0, x1), hi = Math.Max(x0, x1);
            if (x <= lo)
            {
                return x0 <= x1 ? y0 : y1;
            }
            if (x >= hi)
            {
                return x0 <= x1 ? y1 : y0;
            }
            return y0 + ((x - x0) / (x1 - x0) * (y1 - y0));
        }

        // The engine's boost interpolation: flat ends, measured from the lower x.
        private static double ELerp(double x, double x0, double x1, double y0, double y1)
        {
            double lo = Math.Min(x0, x1), hi = Math.Max(x0, x1);
            if (x <= lo)
            {
                return y0;
            }
            if (x >= hi)
            {
                return y1;
            }
            return y0 + ((x - lo) / (hi - lo) * (y1 - y0));
        }

        private sealed class Tyre
        {
            public double X0, X1, Y0, Y1, Scale = 1;

            public double Mu(double load)
            {
                return Lerp(load, this.X0, this.X1, this.Y0, this.Y1) * this.Scale;
            }
        }

        // ---- the stock engine ----
        private sealed class Engine
        {
            public double Redline, MaxRpm, Step, Cut, PeakT, PeakTw, PeakP, PeakPw, RedlineW;
            public double[] Torque;

            // The torque table is looked up as if n x step spanned n - 1 intervals (the game's quirk, as in FM2).
            public double AtSim(double w)
            {
                double[] t = this.Torque;
                int n = Math.Min(t.Length, 151);
                double xMax = n * this.Step * W;
                double x = Clamp(w, 0, xMax), f = x * (n - 1) / xMax;
                int i = Math.Min((int) f, n - 2);
                double a = i < t.Length ? t[i] : 0, b = i + 1 < t.Length ? t[i + 1] : 0;
                return a + ((f - i) * (b - a));
            }
        }

        // One database and one PI.xml: every method of the fm4perf harness, instance state instead of statics.
        private sealed class Model
        {
            private readonly SqliteDatabase db;
            private readonly List<string[]> keyed = new List<string[]>();   // table, key column, kind, has MassDiff, DragScale, WeightDistDiff, TorqueScale
            private double[] piGears;                                       // the standard gears' redline speeds, m/s
            private double piMin, piMax;
            private List<double[]> piSeg;                                   // segments: straight m, len1, len2, p, radius m, along
            private readonly double[] piStraight = new double[3], piCorner = new double[3], piTimeScale = new double[3];   // FWD, RWD, AWD
            private Rec[] tb;

            public Model(SqliteDatabase db)
            {
                this.db = db;
                // Every List_Upgrade table with stock rows, by its key: the car (Ordinal), its body, engine or drivetrain.
                foreach (object[] t in db.Query("SELECT name, sql FROM sqlite_master WHERE type = 'table' AND name LIKE 'List_Upgrade%'").Rows)
                {
                    string name = (string) t[0], sql = (string) t[1], key = null, kind = null;
                    if (sql.IndexOf("[Ordinal]", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        key = "Ordinal";
                        kind = "car";
                    }
                    else if (sql.IndexOf("[CarBodyId]", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        key = "CarBodyId";
                        kind = "body";
                    }
                    else if (sql.IndexOf("[EngineID]", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        key = "EngineID";
                        kind = "engine";
                    }
                    else if (sql.IndexOf("[DrivetrainID]", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        key = "DrivetrainID";
                        kind = "drive";
                    }
                    if (key == null || sql.IndexOf("[IsStock]", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                    this.keyed.Add(new string[] { name, key, kind, sql.Contains("[MassDiff]") ? "1" : "", sql.Contains("[DragScale]") ? "1" : "", sql.Contains("[WeightDistDiff]") ? "1" : "", sql.Contains("[TorqueScale]") ? "1" : "" });
                }
            }

            private Dictionary<string, object> Row(string sql, params object[] args)
            {
                SqliteDatabase.Result r = this.db.Query(sql, args);
                if (r.Rows.Count == 0)
                {
                    return null;
                }
                Dictionary<string, object> d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < r.Columns.Length; i++)
                {
                    d[r.Columns[i]] = r.Rows[0][i];
                }
                return d;
            }

            // Media\Physics\PI.xml (828A88F0): the lap time range, the standard gears and the track.
            public void LoadPi(string path)
            {
                this.piGears = null;
                if (!File.Exists(path))
                {
                    return;
                }
                try
                {
                    string x = File.ReadAllText(path);
                    Func<string, string, double> attr = delegate(string tag, string name)
                    {
                        int i = tag.IndexOf(name + "=\"", StringComparison.Ordinal);
                        int e = tag.IndexOf('"', i + name.Length + 2);
                        return double.Parse(tag.Substring(i + name.Length + 2, e - i - name.Length - 2).Trim(), CultureInfo.InvariantCulture);
                    };
                    List<double> gears = new List<double>();
                    List<double[]> track = new List<double[]>();
                    double min = 0, max = 0, width = 0;
                    string[] dts = { "FWD", "RWD", "AWD" };
                    foreach (string raw in x.Split('<'))
                    {
                        string tag = raw.Trim();
                        if (tag.StartsWith("Misc ", StringComparison.Ordinal))
                        {
                            min = attr(tag, "MinPITimeSeconds");
                            max = attr(tag, "MaxPITimeSeconds");
                            width = attr(tag, "TrackWidthMeters");
                            for (int i = 0; i < 3; i++)
                            {
                                this.piCorner[i] = attr(tag, dts[i] + "AccelFrictionScaleWhileCornering");
                                this.piStraight[i] = attr(tag, dts[i] + "AccelFrictionScaleWhileStraight");
                                this.piTimeScale[i] = attr(tag, dts[i] + "LapTimeScale");
                            }
                        }
                        else if (tag.StartsWith("Gear ", StringComparison.Ordinal))
                        {
                            gears.Add(attr(tag, "RedlineSpeedMPH") * MPH);
                        }
                        else if (tag.StartsWith("Segment ", StringComparison.Ordinal))
                        {
                            track.Add(new double[] { attr(tag, "StraightLengthMeters"), attr(tag, "CornerRadiusMeters"), attr(tag, "CornerAngleDegrees") });
                        }
                    }
                    if (gears.Count == 0 || track.Count == 0 || !(max > min) || !(width > 0))
                    {
                        return;
                    }
                    // each corner's geometry; each straight is shortened by the previous corner's exit spiral overrun, min 50 m
                    List<double[]> seg = new List<double[]>();
                    foreach (double[] s in track)
                    {
                        double[] g = Geo(s[1], s[2], width);
                        seg.Add(new double[] { s[0], g[0], g[1], g[2], s[1], g[3] });
                    }
                    int ns = seg.Count;
                    double[] straight = new double[ns];
                    for (int i = 0; i < ns; i++)
                    {
                        straight[i] = Math.Max(50, seg[i][0] - seg[(i + ns - 1) % ns][5]);
                    }
                    for (int i = 0; i < ns; i++)
                    {
                        seg[i][0] = straight[i];
                    }
                    this.piMin = min;
                    this.piMax = max;
                    this.piSeg = seg;
                    this.piGears = gears.ToArray();
                }
                catch (Exception)
                {
                    this.piGears = null;    // an unreadable PI.xml: no PI
                }
            }

            private Tyre MakeTyre(Dictionary<string, object> comp, string multiCol, double width, double brand)
            {
                Dictionary<string, object> mc = this.Row("SELECT * FROM List_TireFrictionMultiCurve WHERE FrictionMultiCurveID = ?", comp[multiCol]);
                Tyre t = new Tyre { X0 = D(mc["MinLoadCurve"]), X1 = D(mc["MaxLoadCurve"]) };
                for (int j = 0; j < 2; j++)
                {
                    Dictionary<string, object> c = this.Row("SELECT * FROM List_TireFrictionCurve WHERE FrictionCurveID = ?", mc["TireFrictionCurveID" + j]);
                    int n = Convert.ToInt32(c["NumCurveValues"], CultureInfo.InvariantCulture);
                    double best = 0;
                    for (int i = 0; i < n && c.ContainsKey("v" + i); i++)
                    {
                        best = Math.Max(best, D(c["v" + i]));
                    }
                    if (j == 0)
                    {
                        t.Y0 = best * D(c["FrictionScale"]);
                    }
                    else
                    {
                        t.Y1 = best * D(c["FrictionScale"]);
                    }
                }
                t.Scale = Lerp(width, D(comp["TireFricWidth0"]), D(comp["TireFricWidth1"]), D(comp["TireFricScale0"]), D(comp["TireFricScale1"])) * brand;
                return t;
            }

            // [downforce, drag, balance "Two"] per (m/s)^2 of a stock wing row; slider < 0 = the row's default.
            private double[] Wing(string table, string key, long id, double slider)
            {
                Dictionary<string, object> u = this.Row("SELECT AeroPhysicsID FROM " + table + " WHERE " + key + " = ? AND IsStock = 1", id);
                Dictionary<string, object> ap = u == null ? null : this.Row("SELECT * FROM List_AeroPhysics WHERE AeroPhysicsID = ?", u["AeroPhysicsID"]);
                if (ap == null)
                {
                    return new double[3];
                }
                double s = slider >= 0 ? slider : D(ap["DefaultTuneSlider"]);
                const string p = "DFTorqueScaleTwo";
                double bal = s <= D(ap[p + "SliderInput1"])
                    ? Lerp(s, D(ap[p + "SliderInput0"]), D(ap[p + "SliderInput1"]), D(ap[p + "TorqueScaleOutput0"]), D(ap[p + "TorqueScaleOutput1"]))
                    : Lerp(s, D(ap[p + "SliderInput1"]), D(ap[p + "SliderInput2"]), D(ap[p + "TorqueScaleOutput1"]), D(ap[p + "TorqueScaleOutput2"]));
                return new double[]
                {
                    (D(ap["Downforce0"]) + ((D(ap["Downforce1"]) - D(ap["Downforce0"])) * s)) * K * 100,
                    (D(ap["Drag0"]) + ((D(ap["Drag1"]) - D(ap["Drag0"])) * s)) * K * 100,
                    bal
                };
            }

            // The stock engine's torque table (N.m / 100 per step) and peaks: camshaft curve x the stock parts' TorqueScale,
            // boost of the stock turbo / supercharger by the previous point's power, drop-off by rpm.
            private Engine BuildEngine(long engine)
            {
                SqliteDatabase.Result cv = this.db.Query("SELECT RedlineRPM, TorqueCurveMaxRPM, NumRPMEntriesArray, TorqueCurveFullThrottleID FROM List_UpgradeEngineCamshaft WHERE EngineID = ? AND IsStock = 1", engine);
                if (cv.Rows.Count == 0)
                {
                    return null;
                }
                Engine e = new Engine { Redline = D(cv.Rows[0][0]), MaxRpm = D(cv.Rows[0][1]) };
                int n = Convert.ToInt32(cv.Rows[0][2], CultureInfo.InvariantCulture);
                SqliteDatabase.Result curve = this.db.Query("SELECT * FROM List_TorqueCurve WHERE TorqueCurveID = ?", cv.Rows[0][3]);
                if (curve.Rows.Count == 0 || n < 2)
                {
                    return null;
                }
                double scale = D(curve.Rows[0][Array.IndexOf(curve.Columns, "TorqueScale")]);
                int v0 = Array.IndexOf(curve.Columns, "v0");
                double mult = 1;
                foreach (string[] t in this.keyed)
                {
                    if (t[2] != "engine" || t[6] == "")
                    {
                        continue;
                    }
                    foreach (object[] r in this.db.Query("SELECT TorqueScale FROM " + SqliteDatabase.Quote(t[0]) + " WHERE EngineID = ? AND IsStock = 1", engine).Rows)
                    {
                        mult += D(r[0]) - 1;
                    }
                }
                double ic = 1;
                object[] tb = null, sc = null;
                foreach (string tt in new string[] { "List_UpgradeEngineTurboSingle", "List_UpgradeEngineTurboTwin", "List_UpgradeEngineTurboQuad" })
                {
                    SqliteDatabase.Result q = this.db.Query("SELECT MaxScale, PowerMaxScale, MinScale, PowerMinScale, RobScale, TorqueDropOffRPM0, TorqueDropOffRPM1, TorqueDropOffScale0, TorqueDropOffScale1 FROM " + tt + " WHERE EngineID = ? AND IsStock = 1", engine);
                    if (q.Rows.Count > 0 && Math.Abs(D(q.Rows[0][0]) - 1) >= 0.001 && tb == null)
                    {
                        tb = q.Rows[0];
                    }
                }
                foreach (string st in new string[] { "List_UpgradeEngineDSC", "List_UpgradeEngineCSC" })
                {
                    SqliteDatabase.Result q = this.db.Query("SELECT ZeroRPMScale, RedlineRPMScale, RobScale, TorqueDropOffRPM0, TorqueDropOffRPM1, TorqueDropOffScale0, TorqueDropOffScale1 FROM " + st + " WHERE EngineID = ? AND IsStock = 1", engine);
                    if (q.Rows.Count > 0 && sc == null)
                    {
                        sc = q.Rows[0];
                    }
                }
                SqliteDatabase.Result icq = this.db.Query("SELECT MaxScaleScale FROM List_UpgradeEngineIntercooler WHERE EngineID = ? AND IsStock = 1", engine);
                if (icq.Rows.Count > 0)
                {
                    ic = D(icq.Rows[0][0]);
                }
                if (tb != null)
                {
                    mult += D(tb[4]) - 1;
                }
                if (sc != null)
                {
                    mult += D(sc[2]) - 1;
                }
                double tMin = 0, tMax = 0, sZero = 0, sRed = 0;
                if (tb != null)
                {
                    tMin = (D(tb[2]) - 1 + mult) / mult;
                    tMax = (D(tb[0]) - 1 + ic - 1 + mult) / mult;
                }
                if (sc != null)
                {
                    sZero = (D(sc[0]) - 1 + mult) / mult;
                    sRed = (D(sc[1]) - 1 + ic - 1 + mult) / mult;
                }
                e.Step = e.MaxRpm / (n - 1);
                e.Cut = 400;
                double prevPower = 0;
                List<double> list = new List<double>();
                for (int i = 0; i < n && i < 151; i++)
                {
                    double rpm = i * e.Step, t = D(curve.Rows[0][v0 + i]) * scale * 0.01;
                    if (t > 0)
                    {
                        t *= mult;
                    }
                    double w = rpm * W, tf = 1, sf = 1;
                    if (tb != null)
                    {
                        tf = ELerp(prevPower, D(tb[3]) * HP, D(tb[1]) * HP, tMin, tMax);
                        if (tf > 1)
                        {
                            double k = tf - 1;
                            tf = ELerp(w, D(tb[5]) * W, D(tb[6]) * W, 1 + (D(tb[7]) * k), 1 + (D(tb[8]) * k));
                        }
                    }
                    if (sc != null)
                    {
                        sf = ELerp(w, 0, e.Redline * W, sZero, sRed);
                        if (sf > 1)
                        {
                            double k = sf - 1;
                            sf = ELerp(w, D(sc[3]) * W, D(sc[4]) * W, 1 + (D(sc[5]) * k), 1 + (D(sc[6]) * k));
                        }
                    }
                    t *= 1 + (tf - 1) + (sf - 1);
                    prevPower = t * w;
                    if (rpm >= 400 && t <= 0)
                    {
                        e.Cut = rpm;
                        break;
                    }
                    list.Add(t);
                    if (rpm > e.Redline + 1e-6)
                    {
                        continue;
                    }
                    if (t > e.PeakT + 1e-9)
                    {
                        e.PeakT = t;
                        e.PeakTw = w;
                    }
                    if (t * w > e.PeakP + 1e-9)
                    {
                        e.PeakP = t * w;
                        e.PeakPw = w;
                    }
                }
                e.Torque = list.ToArray();
                e.RedlineW = Math.Min(e.Redline, e.Cut - e.Step) * W;
                return e.Torque.Length < 2 ? null : e;
            }

            // ---- the car (fm3perf Car): stock rows by Ordinal, CarBodyId, EngineID, DrivetrainID ----
            public Result Car(long id)
            {
                Dictionary<string, object> car = this.Row("SELECT * FROM Data_Car WHERE Id = ?", id);
                Dictionary<string, object> ub = this.Row("SELECT CarBodyID FROM List_UpgradeCarBody WHERE Ordinal = ? AND IsStock = 1", id);
                if (car == null || ub == null)
                {
                    return null;
                }
                long body = Convert.ToInt64(ub["CarBodyID"], CultureInfo.InvariantCulture);
                Dictionary<string, object> cb = this.Row("SELECT * FROM Data_CarBody WHERE Id = ?", body);
                Dictionary<string, object> w = this.Row("SELECT * FROM List_UpgradeCarBodyWeight WHERE CarBodyId = ? AND IsStock = 1", body);
                Dictionary<string, object> tc = this.Row("SELECT TireCompoundID FROM List_UpgradeTireCompound WHERE Ordinal = ? AND IsStock = 1", id);
                Dictionary<string, object> ue = this.Row("SELECT EngineID FROM List_UpgradeEngine WHERE Ordinal = ? AND IsStock = 1", id);
                Dictionary<string, object> ud = this.Row("SELECT DrivetrainID FROM List_UpgradeDrivetrain WHERE Ordinal = ? AND IsStock = 1", id);
                if (cb == null || w == null || tc == null || ue == null || ud == null)
                {
                    return null;
                }
                long engine = Convert.ToInt64(ue["EngineID"], CultureInfo.InvariantCulture), drive = Convert.ToInt64(ud["DrivetrainID"], CultureInfo.InvariantCulture);
                Dictionary<string, object> twf = this.Row("SELECT FrontTireWidth FROM List_UpgradeCarBodyTireWidthFront WHERE CarBodyId = ? AND IsStock = 1", body);
                Dictionary<string, object> twr = this.Row("SELECT RearTireWidth FROM List_UpgradeCarBodyTireWidthRear WHERE CarBodyId = ? AND IsStock = 1", body);
                double wf = twf == null ? D(car["FrontTireWidthMM"]) : D(twf["FrontTireWidth"]), wr = twr == null ? D(car["RearTireWidthMM"]) : D(twr["RearTireWidth"]);
                Dictionary<string, object> comp = this.Row("SELECT * FROM List_TireCompound WHERE TireCompoundID = ?", tc["TireCompoundID"]);
                Dictionary<string, object> br = this.Row("SELECT FrictionScale, PeakSASRScale FROM Combo_TireBrandCompound WHERE TireBrandId = ? AND TireCompoundId = ?", car["TireBrandID"], tc["TireCompoundID"]);
                double brand = br == null ? 1 : D(br["FrictionScale"]);
                // the friction scales per axle, from the stock chassis stiffness row: lateral for cornering, longitudinal for
                // braking and acceleration (FM3 has one pair and brakes with the front value)
                Dictionary<string, object> cs = this.Row("SELECT * FROM List_UpgradeCarBodyChassisStiffness WHERE CarBodyId = ? AND IsStock = 1", body);
                double gripF = cs == null ? 1 : D(cs["FrontLatFrictionScale"]), gripR = cs == null ? 1 : D(cs["RearLatFrictionScale"]);
                double longF = cs == null ? 1 : D(cs["FrontLongFrictionScale"]), longR = cs == null ? 1 : D(cs["RearLongFrictionScale"]);

                double m = D(w["Mass"]), wF = D(w["CMBackFront"]), dragProduct = 1;
                foreach (string[] t in this.keyed)
                {
                    long key = t[2] == "car" ? id : t[2] == "body" ? body : t[2] == "engine" ? engine : drive;
                    string cols = (t[3] != "" ? "MassDiff" : "0") + ", " + (t[4] != "" ? "DragScale" : "1") + ", " + (t[5] != "" ? "WeightDistDiff" : "0");
                    foreach (object[] x in this.db.Query("SELECT " + cols + " FROM " + SqliteDatabase.Quote(t[0]) + " WHERE " + t[1] + " = ? AND IsStock = 1", key).Rows)
                    {
                        m += D(x[0]);
                        dragProduct *= (x[1] == null || x[1] is DBNull) ? 1 : D(x[1]);
                        wF += D(x[2]);
                    }
                }
                double L = D(cb["ModelWheelbase"]), aF = L * (1 - wF);
                double tF = D(cb["ModelFrontTrackOuter"]) - (wf * 0.001), tR = D(cb["ModelRearTrackOuter"]) - (wr * 0.001);
                // body downforce without the wings (the PI laps add the wings at their own sliders)
                double dfF0 = D(car["BodyAeroForwardDownforceFront"]) * K * 100, dfR0 = D(car["BodyAeroForwardDownforceRear"]) * K * 100;
                double bodyDrag = D(car["BodyAeroLongitudinalDrag"]) * K * 100 * Clamp(D(car["GameDragScale"]), 0.5, 1.5);
                Dictionary<string, object> rbp = this.Row("SELECT * FROM List_UpgradeCarBodyRearBumper WHERE CarBodyID = ? AND IsStock = 1", body);
                if (rbp != null)
                {
                    dfF0 += D(rbp["BodyAeroForwardDownforceFront"]) * K * 100;
                    dfR0 += D(rbp["BodyAeroForwardDownforceRear"]) * K * 100;
                }
                double[] fw = this.Wing("List_UpgradeCarBodyFrontBumper", "CarBodyID", body, -1), rw = this.Wing("List_UpgradeRearWing", "Ordinal", id, -1);
                double p1 = ((1 - wF) * fw[2]) + wF, p2 = (wF * rw[2]) + (1 - wF);
                double dfF = dfF0 + (p1 * fw[0]) + ((1 - p2) * rw[0]), dfR = dfR0 + ((1 - p1) * fw[0]) + (p2 * rw[0]);
                double drag = (bodyDrag * dragProduct) + fw[1] + rw[1];      // N per (m/s)^2

                const double unit = 1 / G;
                Tyre lonF = this.MakeTyre(comp, "FrictionMultiCurveLongitudinalID", wf, brand), lonR = this.MakeTyre(comp, "FrictionMultiCurveLongitudinalID", wr, brand);
                Tyre latF = this.MakeTyre(comp, "FrictionMultiCurveLateralID", wf, brand), latR = this.MakeTyre(comp, "FrictionMultiCurveLateralID", wr, brand);
                double tfB = D(comp["TorqueFreeLongFrictionScaleBrake"]), tfL = D(comp["TorqueFreeLatFrictionScale"]);
                Dictionary<string, object> bk = this.Row("SELECT GameFrictionScaleBraking FROM List_UpgradeBrakes WHERE Ordinal = ? AND IsStock = 1", id);
                if (bk != null)
                {
                    tfB *= D(bk["GameFrictionScaleBraking"]);
                }
                double h = D(w["CMHeight"]), dhF = 0, dhR = 0;
                Dictionary<string, object> sd = this.Row("SELECT FrontSpringDamperPhysicsID, RearSpringDamperPhysicsID FROM List_UpgradeSpringDamper WHERE Ordinal = ? AND IsStock = 1", id);
                if (sd != null)
                {
                    Dictionary<string, object> sf = this.Row("SELECT DefRideHeight FROM List_SpringDamperPhysics WHERE SpringDamperPhysicsID = ?", sd["FrontSpringDamperPhysicsID"]);
                    Dictionary<string, object> sr = this.Row("SELECT DefRideHeight FROM List_SpringDamperPhysics WHERE SpringDamperPhysicsID = ?", sd["RearSpringDamperPhysicsID"]);
                    if (sf != null && sr != null)
                    {
                        double mid = 0.5 * (D(sf["DefRideHeight"]) + D(sr["DefRideHeight"]));      // FM4: the CM height is CMHeight only (FM3 adds mid)
                        dhF = D(sf["DefRideHeight"]) - mid;
                        dhR = -dhF;
                    }
                }

                // ---- braking and cornering: means over 8 speeds 50 .. 120 mph (82818020, 822F6D18) ----
                Result res = new Result { Mass = m, FrontWeight = wF };
                for (int s = 0; s < 8; s++)
                {
                    double v = (50 + (70.0 * s / 7)) * MPH, q = v * v, qF = dfF * q, qR = dfR * q, qD = drag * q;
                    double wt = m * G, nf = (wF * wt) + qF, nr = ((1 - wF) * wt) + qR, nF0 = nf, nR0 = nr, kF = 0, kR = 0;
                    double num = (aF * wt) + (L * qR), tot = wt + qF + qR;
                    for (int i = 0; i < 6; i++)
                    {
                        kF = lonF.Mu(0.5 * nf * unit) * longF;
                        kR = lonR.Mu(0.5 * nr * unit) * longR;
                        nr = Clamp((num - (h * kF * nf) - (h * kR * nr)) / L, 0, tot);
                        nf = tot - nr;
                    }
                    double m0 = nF0 / 2, m1 = nF0 / 2, m2 = nR0 / 2, m3 = nR0 / 2, j0 = 0, j1 = 0, j2 = 0, j3 = 0;
                    for (int i = 0; i < 6; i++)
                    {
                        j0 = latF.Mu(m0 * unit) * gripF;
                        j1 = latF.Mu(m1 * unit) * gripF;
                        j2 = latR.Mu(m2 * unit) * gripR;
                        j3 = latR.Mu(m3 * unit) * gripR;
                        double eF = ((j0 * m0) + (j1 * m1)) * (h + dhF) / tF, eR = ((j2 * m2) + (j3 * m3)) * (h + dhR) / tR;
                        m0 = Math.Max((nF0 / 2) - eF, 0);
                        m1 = Math.Min((nF0 / 2) + eF, nF0);
                        m3 = Math.Max((nR0 / 2) - eR, 0);
                        m2 = Math.Min((nR0 / 2) + eR, nR0);
                    }
                    res.Brake += ((qD / m) + (tfB * ((kF * nf) + (kR * nr)) / m)) / 8;
                    res.Corner += (tfL * ((j0 * m0) + (j1 * m1) + (j2 * m2) + (j3 * m3)) / m) / 8;
                }

                // ---- engine, gears, top speed, acceleration and launch (82818568) ----
                Engine eng = this.BuildEngine(engine);
                Dictionary<string, object> tr = this.Row("SELECT * FROM List_UpgradeDrivetrainTransmission WHERE DrivetrainID = ? AND IsStock = 1", drive);
                if (eng == null || tr == null)
                {
                    return null;
                }
                res.PeakT = eng.PeakT;
                res.PeakTw = eng.PeakTw;
                res.PeakP = eng.PeakP;
                res.PeakPw = eng.PeakPw;
                res.RedlineW = eng.RedlineW;
                double fd = D(tr["FinalDriveRatio"]);
                int ng = Math.Min(9, Convert.ToInt32(tr["NumGears"], CultureInfo.InvariantCulture));   // NumGears counts the reverse (GearRatio0)
                double[] gears = new double[ng];
                for (int i = 0; i < ng; i++)
                {
                    gears[i] = D(tr["GearRatio" + i]);
                }
                Dictionary<string, object> dt = this.Row("SELECT DrivetypeID FROM Data_Drivetrain WHERE DrivetrainID = ?", drive);
                long dtype = dt == null ? 2 : Convert.ToInt64(dt["DrivetypeID"], CultureInfo.InvariantCulture);     // 1 FWD, 2 RWD, 3 AWD
                double rF = (wf * 0.001 * D(car["FrontTireAspect"]) * 0.01) + (D(car["FrontWheelDiameterIN"]) * 0.0127);
                double rR = (wr * 0.001 * D(car["RearTireAspect"]) * 0.01) + (D(car["RearWheelDiameterIN"]) * 0.0127);
                double R = dtype == 2 ? rR : rF, gts = Clamp(D(car["GameTorqueScale"]), 0.5, 1.5);
                double vPow = Math.Pow(gts * eng.PeakP * 100 / drag, 1.0 / 3.0);
                double vGear = (eng.Redline + eng.MaxRpm) * 0.5 * W / gears[ng - 1] / fd * R;
                res.TopSpeed = Math.Min(vPow, vGear);
                double cAcc = D(comp["TorqueFreeLongFrictionScaleAccel1"]);
                Func<double, double> accAt = delegate(double v)
                {
                    double q = v * v, tw0 = -1;
                    for (int g = 1; g < ng; g++)
                    {
                        double gr = gears[g];
                        if (gr <= 0)
                        {
                            continue;
                        }
                        if (v < eng.RedlineW * R / (fd * gr))
                        {
                            tw0 = gr * fd * eng.AtSim(gr * fd * v / R) * 100 * gts;
                            break;
                        }
                    }
                    if (tw0 < 0)
                    {
                        return 0;
                    }
                    double aw = m * G, dF = dfF * q, dR = dfR * q, aTot = aw + dF + dR, aNF = (wF * aw) + dF, aNR = ((1 - wF) * aw) + dR, aFF = 0, aFR = 0;
                    for (int i = 0; i < 6; i++)
                    {
                        aFF = cAcc * lonF.Mu(0.5 * aNF * unit) * longF * aNF;
                        aFR = cAcc * lonR.Mu(0.5 * aNR * unit) * longR * aNR;
                        if (dtype == 1)
                        {
                            double cap = Math.Max(rF * aFF, 1e-5);
                            aFF *= Math.Min(tw0, cap) / cap;
                            aFR = 0;
                        }
                        else if (dtype == 2)
                        {
                            double cap = Math.Max(rR * aFR, 1e-5);
                            aFR *= Math.Min(tw0, cap) / cap;
                            aFF = 0;
                        }
                        else
                        {
                            double cap = Math.Max((rF * aFF) + (rR * aFR), 1e-5), k = Math.Min(tw0, cap) / cap;
                            aFF *= k;
                            aFR *= k;
                        }
                        aNR = Clamp(((aF * aw) + (L * dR) + (h * (aFF + aFR) / cAcc)) / L, 0, aTot);
                        aNF = aTot - aNR;
                    }
                    return (aFF + aFR - (drag * q)) / m;
                };
                for (int s = 0; s < 13; s++)
                {
                    res.Accel += accAt((40 + (40.0 * s / 12)) * MPH) / 13;
                    res.Launch += accAt((20 + (20.0 * s / 12)) * MPH) / 13;
                }
                res.BrakingRating = Rate(res.Brake, 10.1341, 22.54);
                res.HandlingRating = Rate(res.Corner, 8.87586, 19.7);
                res.SpeedRating = Clamp(((res.TopSpeed - 58) * 0.1590909) + 3, 3, 10);
                res.AccelerationRating = Rate(res.Accel, 1.35997, 11.99);
                res.LaunchRating = Rate(res.Launch, 2.92428, 13.65);

                // ---- PI: the lap of Media\Physics\PI.xml FM4 version (828A7880, lap 82894450) ----
                if (this.piGears == null)
                {
                    return res;
                }
                Dictionary<string, object> cl = this.Row("SELECT ClutchInTime, ClutchOutTime FROM List_UpgradeDrivetrainClutch WHERE DrivetrainID = ? AND IsStock = 1", drive);
                double shift = (cl == null ? 0 : (D(cl["ClutchInTime"]) + D(cl["ClutchOutTime"])) * 0.5) + D(tr["GearShiftTime"]);
                Dictionary<string, object> er = this.Row("SELECT MomentInertia FROM Data_Engine WHERE EngineID = ?", engine);
                Dictionary<string, object> fwr = this.Row("SELECT MomentInertia FROM List_UpgradeEngineFlywheel WHERE EngineID = ? AND IsStock = 1", engine);
                Dictionary<string, object> dlr = this.Row("SELECT MomentInertia FROM List_UpgradeDrivetrainDriveline WHERE DrivetrainID = ? AND IsStock = 1", drive);
                double iEng = (er == null ? 0 : D(er["MomentInertia"])) + (fwr == null ? 0 : D(fwr["MomentInertia"]));
                double iAB = D(tr["MomentInertia"]) + (dlr == null ? 0 : D(dlr["MomentInertia"])), iTyre = D(comp["MomentInertia"]) * WheelInertiaScale;
                double wStd = eng.Redline * W;      // the standard gears are sized on the redline
                // peak slip angle of the lateral curves (as FM3): x sidewall scale x PeakSASRScale, clamped 5..20 degrees at the max load
                double sasr = br == null ? 1 : D(br["PeakSASRScale"]);
                Dictionary<string, object> latMc = this.Row("SELECT * FROM List_TireFrictionMultiCurve WHERE FrictionMultiCurveID = ?", comp["FrictionMultiCurveLateralID"]);
                Func<double, double, double[]> slips = delegate(double width, double aspect)
                {
                    double[] s = new double[2];
                    for (int j = 0; j < 2; j++)
                    {
                        Dictionary<string, object> c = this.Row("SELECT * FROM List_TireFrictionCurve WHERE FrictionCurveID = ?", latMc["TireFrictionCurveID" + j]);
                        int cnt = Convert.ToInt32(c["NumCurveValues"], CultureInfo.InvariantCulture);
                        double bestV = 0;
                        int bi = 0;
                        for (int i = 0; i < cnt && c.ContainsKey("v" + i); i++)
                        {
                            double y = D(c["v" + i]) * D(c["FrictionScale"]);
                            if (y > bestV)
                            {
                                bestV = y;
                                bi = i;
                            }
                        }
                        double f2 = Lerp(width * aspect * 1e-5, 0.0762, 0.1397, 1.0, 1.25) * sasr;
                        s[j] = bi * D(latMc["MaxSlip"]) * DEG * f2 / (cnt - 1);
                    }
                    double lim = 1, hi = 20 * DEG, lo = 5 * DEG;
                    if (s[1] > hi)
                    {
                        lim = hi / s[1];
                    }
                    else if (s[1] < lo)
                    {
                        lim = lo / s[1];
                    }
                    s[0] *= lim;
                    s[1] *= lim;
                    return s;
                };
                double[] slF = slips(wf, D(car["FrontTireAspect"])), slR = slips(wr, D(car["RearTireAspect"]));
                // the PI's cornering: each tyre's friction x cos(peak slip angle at its load)
                Func<Tyre, double[], double, double> muLat = delegate(Tyre t, double[] sl, double load)
                {
                    return t.Mu(load) * Math.Cos(Lerp(load, t.X0, t.X1, sl[0], sl[1]));
                };
                // the lap's gears: the standard PI.xml gears (sized on the redline) or the car's own forward gears
                double[] piGears = this.piGears, lapRatio, lapGs;
                if (!(D(tr["Level"]) < OwnGearsBelowLevel))
                {
                    lapGs = piGears;
                    lapRatio = new double[piGears.Length];
                    for (int g = 0; g < piGears.Length; g++)
                    {
                        lapRatio[g] = R * wStd / (piGears[g] * fd);
                    }
                }
                else
                {
                    lapRatio = new double[ng - 1];
                    lapGs = new double[ng - 1];
                    for (int g = 1; g < ng; g++)
                    {
                        lapRatio[g - 1] = gears[g];
                        lapGs[g - 1] = R * wStd / (gears[g] * fd);
                    }
                }
                int nG = lapRatio.Length;
                int di = dtype == 1 ? 0 : dtype == 2 ? 1 : 2;      // PI.xml's FWD / RWD / AWD scales
                double best = double.MaxValue;
                // three laps: the stock setup, both aero sliders at 0, both at 1; the shortest counts
                for (int variant = 0; variant < 3; variant++)
                {
                    double sl = variant == 0 ? -1 : variant == 1 ? 0 : 1;
                    double[] fw2 = this.Wing("List_UpgradeCarBodyFrontBumper", "CarBodyID", body, sl), rw2 = this.Wing("List_UpgradeRearWing", "Ordinal", id, sl);
                    double q1 = ((1 - wF) * fw2[2]) + wF, q2 = (wF * rw2[2]) + (1 - wF);
                    double vF = dfF0 + (q1 * fw2[0]) + ((1 - q2) * rw2[0]), vR = dfR0 + ((1 - q1) * fw2[0]) + (q2 * rw2[0]);
                    double vDrag = (bodyDrag * dragProduct) + fw2[1] + rw2[1];
                    if (variant > 0 && fw2[0] == 0 && rw2[0] == 0 && fw2[1] == 0 && rw2[1] == 0)
                    {
                        break;      // no wings: the same lap
                    }
                    Func<double, double> brakeAt = delegate(double v)
                    {
                        double q = v * v, qF = vF * q, qR = vR * q, qD = vDrag * q;
                        double wt = m * G, nf = (wF * wt) + qF, nr = ((1 - wF) * wt) + qR, kF = 0, kR = 0, num = (aF * wt) + (L * qR), tot = wt + qF + qR;
                        for (int i = 0; i < 6; i++)
                        {
                            kF = lonF.Mu(0.5 * nf * unit) * longF;
                            kR = lonR.Mu(0.5 * nr * unit) * longR;
                            nr = Clamp((num - (h * kF * nf) - (h * kR * nr)) / L, 0, tot);
                            nf = tot - nr;
                        }
                        return (qD / m) + (tfB * ((kF * nf) + (kR * nr)) / m);
                    };
                    // FM4 PI cornering (82818F28): the rear lateral force is capped by the yaw balance, rear <= front x (1 - wF) / wF;
                    // [0] the lateral acceleration, [1] capped / uncapped rear (the rear tyres' share of their grip, for the corner acceleration)
                    double capK = Math.Max(1 - wF, 0.001) / wF;
                    Func<double, double[]> cornerAt = delegate(double v)
                    {
                        double q = v * v, qF = vF * q, qR = vR * q, wt = m * G, nF0 = (wF * wt) + qF, nR0 = ((1 - wF) * wt) + qR;
                        double m0 = nF0 / 2, m1 = nF0 / 2, m2 = nR0 / 2, m3 = nR0 / 2, j0 = 0, j1 = 0, j2 = 0, j3 = 0, front = 0, rear = 0, rearU = 1;
                        for (int i = 0; i < 6; i++)
                        {
                            j0 = muLat(latF, slF, m0 * unit) * gripF;
                            j1 = muLat(latF, slF, m1 * unit) * gripF;
                            j2 = muLat(latR, slR, m2 * unit) * gripR;
                            j3 = muLat(latR, slR, m3 * unit) * gripR;
                            front = (j0 * m0) + (j1 * m1);
                            rearU = (j2 * m2) + (j3 * m3);
                            rear = Math.Min(rearU, capK * front);
                            double eF = front * (h + dhF) / tF, eR = rear * (h + dhR) / tR;
                            m0 = Math.Max((nF0 / 2) - eF, 0);
                            m1 = Math.Min((nF0 / 2) + eF, nF0);
                            m3 = Math.Max((nR0 / 2) - eR, 0);
                            m2 = Math.Min((nR0 / 2) + eR, nR0);
                        }
                        return new double[] { tfL * (front + rear) / m, rear / Math.Max(rearU, 0.0001) };
                    };
                    // the PI's acceleration: the lap's gears, v >= 2.2352 m/s, wheel torque x KE / (KE + 1) (the drivetrain's inertia);
                    // sF, sR: the share of each axle's longitudinal grip left (PI.xml straight scale, or what the corner leaves)
                    Func<double, double, double, double> accPI = delegate(double v0, double sF, double sR)
                    {
                        double v = Math.Max(v0, 2.2352), q = v * v, tw0 = -1;
                        for (int g = 0; g < nG; g++)
                        {
                            double gr = lapRatio[g];
                            if (v < eng.RedlineW * R / (fd * gr))
                            {
                                double we = gr * fd * v / R;
                                tw0 = gr * fd * eng.AtSim(we) * 100 * gts;
                                double wr2 = v / rR / we, wf2 = v / rF / we;
                                double iEq = iEng + (iAB / (gr * gr)) + (2 * iTyre * wr2 * wr2) + (2 * iTyre * wf2 * wf2);
                                double ke = m * v * v / (iEq * we * we);
                                tw0 *= ke / (ke + 1);
                                break;
                            }
                        }
                        if (tw0 < 0)
                        {
                            return 0;
                        }
                        double aw = m * G, dF = vF * q, dR = vR * q, aTot = aw + dF + dR, aNF = (wF * aw) + dF, aNR = ((1 - wF) * aw) + dR, aFF = 0, aFR = 0;
                        for (int i = 0; i < 6; i++)
                        {
                            aFF = cAcc * lonF.Mu(0.5 * aNF * unit) * longF * aNF * sF;
                            aFR = cAcc * lonR.Mu(0.5 * aNR * unit) * longR * aNR * sR;
                            if (dtype == 1)
                            {
                                double cap = Math.Max(rF * aFF, 1e-5);
                                aFF *= Math.Min(tw0, cap) / cap;
                                aFR = 0;
                            }
                            else if (dtype == 2)
                            {
                                double cap = Math.Max(rR * aFR, 1e-5);
                                aFR *= Math.Min(tw0, cap) / cap;
                                aFF = 0;
                            }
                            else
                            {
                                double cap = Math.Max((rF * aFF) + (rR * aFR), 1e-5), k = Math.Min(tw0, cap) / cap;
                                aFF *= k;
                                aFR *= k;
                            }
                            aNR = Clamp(((aF * aw) + (L * dR) + (h * (aFF + aFR) / cAcc)) / L, 0, aTot);
                            aNF = aTot - aNR;
                        }
                        return (aFF + aFR - (vDrag * q)) / m;
                    };
                    double[] dragDec = new double[nG];
                    for (int g = 0; g < nG; g++)
                    {
                        dragDec[g] = -(vDrag * lapGs[g] * lapGs[g]) / m;
                    }
                    double time = this.Lap(brakeAt, cornerAt, accPI, dragDec, lapGs, shift, this.piStraight[di], this.piCorner[di]) * this.piTimeScale[di];
                    if (time < best)
                    {
                        best = time;
                    }
                }
                res.LapTime = best;
                res.Pi = Clamp((this.piMax - Clamp(best, this.piMin, this.piMax)) / (this.piMax - this.piMin), 0, 1);
                return res;
            }

            // ---- the corner geometry (82892A00) ----
            // Radius R for half the angle, then an exit spiral, curvature 1/R -> 0.001 as t^p over 40 exact arcs, turning the
            // other half; p by a 10-step bisection so the spiral ends TrackWidth outside the circle's exit.
            // Returns len1 (the constant radius half), len2 (the spiral), p, along (how far the spiral runs past the exit).
            private static double[] Geo(double r, double deg, double width)
            {
                double th = Math.Abs(deg) * DEG, half = th * 0.5, len1 = r * th * 0.5;
                double ex = r * (1 - Math.Cos(half)), ey = r * Math.Sin(half), nx = -Math.Cos(half), ny = Math.Sin(half);
                double p = 1, lo = 0, hi = -1, len2 = 0, k0 = 1 / r, dk = 0.001 - k0, along = 0;
                for (int it = 0; it < 10; it++)
                {
                    double sum = 0;
                    for (int j = 0; j < 40; j++)
                    {
                        sum += (Math.Pow(j * 0.025641026, p) * dk) + k0;
                    }
                    double ds = half / sum, phi = 0, x = 0, y = 0;
                    len2 = 0;
                    for (int j = 0; j < 40; j++)
                    {
                        double k = (Math.Pow(j * 0.025641026, p) * dk) + k0, rr = 1 / k, dth = k * ds;
                        len2 += dth * rr;
                        x += rr * (Math.Cos(phi) - Math.Cos(phi + dth));
                        y += rr * (Math.Sin(phi + dth) - Math.Sin(phi));
                        phi += dth;
                    }
                    double dev = ((x - ex) * nx) + ((y - ey) * ny);
                    along = ((x - ex) * ny) - ((y - ey) * nx);
                    if (dev > width)
                    {
                        lo = p;
                        p = hi >= 0 ? (hi + p) * 0.5 : p * 2;
                    }
                    else if (dev < width)
                    {
                        hi = p;
                        p = (lo + p) * 0.5;
                    }
                    else
                    {
                        break;
                    }
                }
                return new double[] { len1, len2, p, along };
            }

            // ---- the lap (82892220 table of 26 speeds, 82893BE0 straight, 82893E98 corner, 82894450 lap) ----
            private const double VMax = 111.815186, Step = VMax * 0.04, Inv = 25.0 / VMax, K95 = 0.95;

            private sealed class Rec
            {
                public double V, A, Rad, Ratio, B, CumD, Dt, Dist, Acc;
            }

            // the lap state: distance in the part, speed, time, shifting, shift time left, gear
            private double d, v, time, left, shiftTime, sCorner;
            private bool shifting;
            private int gear, n;
            private double[] gs, drag;
            private Func<double, double, double, double> accFn;

            private static void Idx(double v, out int lo, out int hi, out double frac)
            {
                double vc = Math.Max(0, Math.Min(VMax, v));
                lo = Math.Min((int) (Inv * vc), 24);
                hi = lo + 1;
                frac = (vc - (lo * Step)) * Inv;
            }

            private double AccAt(double v)
            {
                int lo, hi;
                double f;
                Idx(v, out lo, out hi, out f);
                return this.tb[lo].Acc + ((this.tb[hi].Acc - this.tb[lo].Acc) * f);
            }

            private double RatioAt(double v)
            {
                int lo, hi;
                double f;
                Idx(v, out lo, out hi, out f);
                return this.tb[lo].Ratio + ((this.tb[hi].Ratio - this.tb[lo].Ratio) * f);
            }

            private double CornerSpeed(double radius)
            {
                Rec[] tb = this.tb;
                for (int i = 24; i >= 0; i--)
                {
                    if (!((radius - tb[i + 1].Rad) * (radius - tb[i].Rad) > 0))
                    {
                        return Lerp(radius, tb[i].Rad, tb[i + 1].Rad, tb[i].V, tb[i + 1].V);
                    }
                }
                return radius < tb[25].Rad ? tb[0].V : tb[25].V;
            }

            // Can the car still brake from v to the target speed in the distance left?
            private bool CanContinue(double v, double target, double left)
            {
                if (!(v > target))
                {
                    return true;
                }
                Rec[] tb = this.tb;
                int lo, hi, lo2, hi2;
                double f, f2;
                Idx(v, out lo, out hi, out f);
                double dd = tb[lo].CumD + ((tb[hi].CumD - tb[lo].CumD) * f);
                if (!(dd > left))
                {
                    return true;
                }
                Idx(target, out lo2, out hi2, out f2);
                double s;
                if (lo == lo2)
                {
                    s = (f - f2) * tb[hi].Dist;
                }
                else
                {
                    s = tb[hi].Dist * f;
                    for (int k = lo; k > hi2; k--)
                    {
                        s += tb[k].Dist;
                    }
                    s += tb[hi2].Dist * (1 - f2);
                }
                return !(s > left);
            }

            private double BrakeTime(double v, double target)
            {
                if (!(v > target))
                {
                    return 0;
                }
                Rec[] tb = this.tb;
                int lo, hi, lo2, hi2;
                double f, f2;
                Idx(v, out lo, out hi, out f);
                Idx(target, out lo2, out hi2, out f2);
                if (lo == lo2)
                {
                    return (f - f2) * tb[hi].Dt;
                }
                double t = tb[hi].Dt * f;
                for (int k = lo; k > hi2; k--)
                {
                    t += tb[k].Dt;
                }
                return t + (tb[hi2].Dt * (1 - f2));
            }

            private void GearFor()
            {
                this.gear = 0;
                if (this.n == 0)
                {
                    return;
                }
                while (this.gear < this.n && !(this.v < this.gs[this.gear]))
                {
                    this.gear++;
                }
            }

            // the corner acceleration (828927F8): friction circle, u = the front's lateral share; the rear's = u x the table's ratio
            private double CornerAcc(double vv, double u)
            {
                double f8 = Clamp(u, 0, 1), f13 = Math.Sqrt(1 - (f8 * f8)), c = this.sCorner;
                double ur = Clamp(this.RatioAt(vv) * u, 0, 1);
                double sF = Math.Max(0, f13 - (1 - c)), sR = Math.Max(0, Math.Sqrt(1 - (ur * ur)) - (1 - c));
                return Math.Max(0, this.accFn(vv, sF, sR));
            }

            private double Lap(Func<double, double> brake, Func<double, double[]> corner, Func<double, double, double, double> acc, double[] dragDec, double[] gearSpeeds, double shift, double sStraight, double sCor)
            {
                this.tb = new Rec[26];
                this.accFn = acc;
                this.sCorner = sCor;
                for (int i = 0; i < 26; i++)
                {
                    Rec r = new Rec { V = i * Step };
                    this.tb[i] = r;
                    double[] c = corner(r.V);
                    r.A = c[0];
                    r.Ratio = c[1];
                    r.Rad = r.V * r.V / Math.Max(r.A, 0.001);
                    r.B = Math.Max(brake(r.V), 0.001);
                    if (i > 0)
                    {
                        r.Dt = Step / r.B;
                        r.Dist = ((r.Dt * r.B * 0.5) + r.V) * r.Dt;
                        r.CumD = this.tb[i - 1].CumD + r.Dist;
                    }
                    r.Acc = acc(r.V, sStraight, sStraight);
                }
                this.gs = gearSpeeds;
                this.drag = dragDec;
                this.n = gearSpeeds.Length;
                this.shiftTime = shift;
                int nSeg = this.piSeg.Count;
                double s95 = Math.Sqrt(K95);
                double[] cs = new double[nSeg];
                for (int i = 0; i < nSeg; i++)
                {
                    cs[i] = this.CornerSpeed(this.piSeg[i][4]);
                }
                this.v = s95 * cs[nSeg - 1];
                this.time = 0;
                this.shifting = false;
                this.left = 0;
                this.d = 0;
                this.GearFor();
                for (int i = 0; i < nSeg; i++)
                {
                    this.Straight(this.piSeg[i][0], s95 * cs[i]);
                    this.Corner(this.piSeg[i], cs[i]);
                }
                return this.time;
            }

            private void EndShift(double dt)
            {
                if (!this.shifting)
                {
                    return;
                }
                this.left -= dt;
                if (!(this.left > 1e-5))
                {
                    this.left = 0;
                    this.shifting = false;
                    this.gear++;
                }
            }

            private void Straight(double len, double target)
            {
                this.d = 0;
                bool cont = true;
                int guard = 0;
                while (cont && guard++ < 100000)
                {
                    double d0 = this.d, v0 = this.v, t0 = this.time;
                    double a, dt;
                    if (this.shifting)
                    {
                        dt = this.left;
                        a = this.drag[this.gear];
                    }
                    else if (this.gear < this.n && this.n >= 2 && this.v > this.gs[this.gear])
                    {
                        this.shifting = true;
                        this.left = this.shiftTime;
                        dt = this.shiftTime;
                        a = this.drag[this.gear];
                    }
                    else
                    {
                        a = this.AccAt(this.v);
                        dt = 0.2;
                    }
                    double dv = dt * a;
                    this.time += dt;
                    this.d = (((dv * 0.5) + this.v) * dt) + this.d;
                    this.v += dv;
                    if (this.d > len)
                    {
                        double over = Math.Min((this.d - len) / this.v, dt);
                        this.v -= over * a;
                        this.time -= over;
                        this.d = len;
                        dt -= over;
                        cont = false;
                    }
                    this.EndShift(dt);
                    if (!this.CanContinue(this.v, target, len - this.d))
                    {
                        // the braking point: bisect the step, then brake down to the corner's speed
                        double lo = 0, hi = dt, v6 = this.v, t5 = this.time;
                        for (int k = 0; k < 10; k++)
                        {
                            double t = (hi + lo) * 0.5, d2 = t * a;
                            t5 = t0 + t;
                            v6 = v0 + d2;
                            double dd = (((d2 * 0.5) + v0) * t) + d0;
                            if (this.CanContinue(v6, target, len - dd))
                            {
                                lo = t;
                            }
                            else
                            {
                                hi = t;
                            }
                        }
                        this.time = this.BrakeTime(v6, target) + t5;
                        this.shifting = false;
                        this.d = len;
                        this.v = target;
                        this.left = 0;
                        this.GearFor();
                        cont = false;
                    }
                }
                this.v = Math.Min(this.v, target);
            }

            // seg: straight, len1, len2, p, radius, along
            private void Corner(double[] seg, double cs)
            {
                double f28 = cs, f31 = Math.Sqrt(K95) * cs;
                this.v = Math.Min(this.v, f31);
                this.d = 0;
                bool phase1 = true, more = true;
                double len = seg[1];
                int guard = 0;
                while (more && guard++ < 100000)
                {
                    bool wasPhase1 = phase1;
                    if (!phase1)
                    {
                        double f = this.d / seg[2], k0 = 1 / seg[4];
                        double k = ((0.001 - k0) * Math.Pow(f, seg[3])) + k0;
                        f31 = f28 = this.CornerSpeed(1 / k);
                    }
                    double a, dt;
                    if (this.shifting)
                    {
                        a = this.drag[this.gear];
                        dt = this.left;
                    }
                    else if (this.v > this.gs[Math.Min(this.gear, this.n - 1)] && this.gear < this.n && this.n >= 2)
                    {
                        this.shifting = true;
                        this.left = this.shiftTime;
                        a = this.drag[this.gear];
                        dt = this.shiftTime;
                    }
                    else if (wasPhase1 && !(this.v < f31))
                    {
                        a = 0;
                        dt = (len - this.d) / this.v * 1.001;
                    }
                    else
                    {
                        a = this.CornerAcc(this.v, this.v * this.v / (f28 * f28));
                        dt = 0.2;
                    }
                    if ((a * dt) + this.v > f31)
                    {
                        a = Math.Max(f31 - this.v, 0) / Math.Max(dt, 0.0001);
                    }
                    double stepD = (this.v + (0.5 * a * dt)) * dt;
                    this.time += dt;
                    this.v = Math.Min(this.v + (a * dt), f31);
                    this.d += stepD;
                    if (!(this.d < len))
                    {
                        double tOver = Clamp((this.d - len) / stepD, 0, 1) * dt;
                        this.v -= tOver * a;
                        this.time -= tOver;
                        this.d = 0;
                        dt -= tOver;
                        if (phase1)
                        {
                            phase1 = false;
                            len = seg[2];
                        }
                        else
                        {
                            more = false;
                        }
                    }
                    this.EndShift(dt);
                }
            }
        }
    }
}
