namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    // FM2 ONLY (2026-11-16). FM2's own performance numbers of a stock car, computed as default.xex does (the fm2perf harness,
    // E:\DEV\fm4dev\ST02\perf, rules in docs/FM2/PI_AND_RATINGS.md): the stock engine's torque table and peaks (82637108),
    // top speed (8264A5B8) -> SpeedRating, the mean acceleration 20..80 mph (82643CF8) -> AccelerationRating, and the PI of the
    // game's neural network (Media\AI\PerfPrediction\carperfpred.nn, inputs 8259E660, best of GameTorqueScale x 1 .. 0.65,
    // 8259E9A0). On the 310 stock FM2 cars: Sim peaks 310/310, SpeedRating 310/310, PI 310/310 within 0.005 (294 within 0.001).
    // Why: Arcade and the dealer show the STORED Data_Car / StockGarage values (only the career garage recomputes them), so a
    // copy whose engine changed must store its own (in game 2026-11-16: Talon TSi made natural, Arcade D 999 -> D 193).
    // FM3 and FM4 have their own calculations, not studied yet: this class is never used for them.
    public static class Fm2Perf
    {
        public sealed class Result
        {
            public double Pi = double.NaN;      // NaN when the network files are missing
            public double SpeedRating, AccelerationRating, TopSpeed;
            public double PeakT, PeakTw, PeakP, PeakPw, RedlineW;   // Data_Car units (N.m / 100, rad/s, W / 100)
        }

        private const double G = 9.80665, K = 0.01 * 9.80665 * 2.2239485e-4, V = 53.6448, HP = 7.456999, W = 0.1047197;

        private static double D(object o)
        {
            return (o == null || o is DBNull) ? 0 : Convert.ToDouble(o, CultureInfo.InvariantCulture);
        }

        private static double Clamp(double v, double lo, double hi)
        {
            return Math.Max(lo, Math.Min(hi, v));
        }

        private static Dictionary<string, object> Row(SqliteDatabase db, string sql, params object[] args)
        {
            SqliteDatabase.Result r = db.Query(sql, args);
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

        private static object Get(Dictionary<string, object> row, string column)
        {
            object value;
            return (row != null && row.TryGetValue(column, out value)) ? value : null;
        }

        // A copy with an engine or gearbox of its own: its PI, class, speed / acceleration ratings and Sim peaks computed and
        // stored in Data_Car (StockGarage copies them from there, Fm2StockGarage.Write). SimTopSpeed (the game's own measurement,
        // not this top speed) is the source's times the change of the computed top speed. Braking and handling stay the
        // source's (the engine does not change them), the Sim times too. userSet: Data_Car columns typed by the user, kept.
        public static void Update(SqliteDatabase db, SqliteDatabase original, long sourceId, long carId, string media, ICollection<string> userSet, List<string> plan)
        {
            string netDir = Path.Combine(Path.Combine(media, "AI"), "PerfPrediction");
            Result c = Compute(db, carId, netDir);
            if (c == null)
            {
                plan.Add("performance (FM2): not recomputed (the car's engine, gearbox, weight or tires could not be read); the PI and ratings stay the source's");
                return;
            }
            Result s = Compute(original, sourceId, netDir);
            Dictionary<string, object> set = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            set["SpeedRating"] = c.SpeedRating;
            set["AccelerationRating"] = c.AccelerationRating;
            set["SimPeakTorque"] = c.PeakT;
            set["SimPeakTorqueAngVel"] = c.PeakTw;
            set["SimPeakPower"] = c.PeakP;
            set["SimPeakAngVel"] = c.PeakPw;
            set["SimRedlineAngVel"] = c.RedlineW;
            double storedTop = D(db.Scalar("SELECT SimTopSpeed FROM Data_Car WHERE Id = ?", carId));
            if (s != null && s.TopSpeed > 0 && storedTop > 0)
            {
                set["SimTopSpeed"] = storedTop * c.TopSpeed / s.TopSpeed;
            }
            long classId = Convert.ToInt64(D(db.Scalar("SELECT ClassID FROM Data_Car WHERE Id = ?", carId)));
            if (!double.IsNaN(c.Pi))
            {
                set["PerformanceIndex"] = c.Pi;
                if (classId < 6)
                {
                    classId = ClassFor(db, c.Pi);       // R4..R1 (6..9) are race classes: kept
                    set["ClassID"] = classId;
                }
            }
            List<string> skipped = new List<string>();
            foreach (KeyValuePair<string, object> kv in set)
            {
                if (userSet != null && userSet.Contains(kv.Key))
                {
                    skipped.Add(kv.Key);
                    continue;
                }
                db.Execute("UPDATE Data_Car SET " + SqliteDatabase.Quote(kv.Key) + " = ? WHERE Id = ?", kv.Value, carId);
            }
            double oldPi = D(original.Scalar("SELECT PerformanceIndex FROM Data_Car WHERE Id = ?", sourceId));
            plan.Add(string.Format(CultureInfo.InvariantCulture,
                "performance (FM2, the game's own calculation): PI {0} -> {1}, speed {2:0.00}, acceleration {3:0.00}, {4:0} kW at {5:0} rpm, {6:0} N.m at {7:0} rpm{8}{9}",
                Shown(db, oldPi), double.IsNaN(c.Pi) ? "not computed (no " + Path.Combine(netDir, "carperfpred.nn") + ")" : Shown(db, c.Pi),
                c.SpeedRating, c.AccelerationRating, c.PeakP / 10, c.PeakPw * 60 / (2 * Math.PI), c.PeakT * 100, c.PeakTw * 60 / (2 * Math.PI),
                set.ContainsKey("SimTopSpeed") ? string.Format(CultureInfo.InvariantCulture, ", SimTopSpeed {0:0.0} m/s", set["SimTopSpeed"]) : "",
                skipped.Count == 0 ? "" : "; typed by you, kept: " + string.Join(", ", skipped.ToArray())));
        }

        // CarClasses D..U (Id 0..5): the first whose MaxPerformanceIndex holds the PI.
        private static long ClassFor(SqliteDatabase db, double pi)
        {
            long last = 5;
            foreach (object[] r in db.Query("SELECT Id, MaxPerformanceIndex FROM CarClasses WHERE Id < 6 ORDER BY MaxPerformanceIndex").Rows)
            {
                last = Convert.ToInt64(r[0]);
                if (pi <= D(r[1]))
                {
                    return last;
                }
            }
            return last;
        }

        // "D 194": linear inside the class (MaxPerformanceIndex -> MaxDisplayPerformanceIndex), from (0, 100); above 0.72 = 999.
        public static string Shown(SqliteDatabase db, double pi)
        {
            double lo = 0, dlo = 100;
            string[] letters = { "D", "C", "B", "A", "S", "U" };
            foreach (object[] r in db.Query("SELECT Id, MaxPerformanceIndex, MaxDisplayPerformanceIndex FROM CarClasses WHERE Id < 6 ORDER BY MaxPerformanceIndex").Rows)
            {
                int id = Convert.ToInt32(r[0]);
                double max = D(r[1]), disp = D(r[2]);
                if (pi <= max || id == 5)
                {
                    double shown = Math.Round(dlo + ((pi - lo) / (max - lo) * (disp - dlo)));
                    return (id < letters.Length ? letters[id] : "?") + " " + shown.ToString(CultureInfo.InvariantCulture);
                }
                lo = max;
                dlo = disp;
            }
            return pi.ToString("0.000", CultureInfo.InvariantCulture);
        }

        // ---- the car (fm2perf "grip exact" mode) ----

        public static Result Compute(SqliteDatabase db, long id, string netDir)
        {
            try
            {
                return ComputeOrThrow(db, id, netDir);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Result ComputeOrThrow(SqliteDatabase db, long id, string netDir)
        {
            Dictionary<string, object> car = Row(db, "SELECT * FROM Data_Car WHERE Id = ?", id);
            Dictionary<string, object> w = Row(db, "SELECT * FROM List_UpgradeWeight WHERE Ordinal = ? AND IsStock = 1", id);
            Dictionary<string, object> tc = Row(db, "SELECT TireCompoundID FROM List_UpgradeTireCompound WHERE Ordinal = ? AND IsStock = 1", id);
            Dictionary<string, object> tw = Row(db, "SELECT * FROM List_UpgradeTireWidth WHERE Ordinal = ? AND IsStock = 1", id);
            if (car == null || w == null || tc == null)
            {
                return null;
            }
            Fm2Car fc = Fm2Car.Build(db, id);
            if (fc == null || fc.Eng == null || fc.Gears.Length < 2)
            {
                return null;
            }
            Dictionary<string, object> comp = Row(db, "SELECT * FROM List_TireCompound WHERE TireCompoundID = ?", tc["TireCompoundID"]);
            Dictionary<string, object> br = Row(db, "SELECT FrictionScale, PeakSASRScale FROM Combo_TireBrandCompound WHERE TireBrandId = ? AND TireCompoundId = ?", car["TireBrandID"], tc["TireCompoundID"]);
            double brand = br == null ? 1 : D(br["FrictionScale"]);
            Dictionary<string, object> mod = Row(db, "SELECT FrontFrictionScale, RearFrictionScale FROM TireMod_FrictionScale WHERE Ordinal = ?", id);
            double gripF = mod == null ? 1 : D(mod["FrontFrictionScale"]);
            double gripR = mod == null ? gripF : D(mod["RearFrictionScale"]);
            double wf = tw == null ? D(car["StockTire-FrontWidth-mm"]) : D(tw["FrontTireWidth"]);
            double wr = tw == null ? D(car["StockTire-RearWidth-mm"]) : D(tw["RearTireWidth"]);

            // Mass: List_UpgradeWeight + MassDiff of every stock part.
            double m = D(w["Mass"]);
            foreach (object[] t in db.Query("SELECT name FROM sqlite_master WHERE type = 'table' AND name LIKE 'List_Upgrade%' AND sql LIKE '%MassDiff%' AND sql LIKE '%Ordinal%'").Rows)
            {
                foreach (object[] md in db.Query("SELECT MassDiff FROM " + SqliteDatabase.Quote((string) t[0]) + " WHERE Ordinal = ? AND IsStock = 1", id).Rows)
                {
                    m += D(md[0]);
                }
            }
            double wF = D(w["CMBackFront"]), L = D(car["ModelWheelbase"]), aF = L * (1 - wF);
            double h = D(w["CMHeight"]) + D(car["ModelFrontStockRideHeight"]);
            double tF = D(car["ModelFrontTrackOuter"]) - (wf * 0.001), tR = D(car["ModelRearTrackOuter"]) - (wr * 0.001);
            double q0 = K * V * V * 100;
            double dfF = D(car["BodyAeroForwardDownforceFront"]) * q0, dfR = D(car["BodyAeroForwardDownforceRear"]) * q0;
            double bodyDrag = D(car["BodyAeroLongitudinalDrag"]) * q0 * Clamp(D(car["GameDragScale"]), 0.5, 1.5);
            double drag = bodyDrag;

            // Aero: the stock rear bumper's body downforce, the wings (front bumper, rear wing; balance curve "Two"), the parts' DragScale.
            Dictionary<string, object> rb = Row(db, "SELECT * FROM List_UpgradeRearBumper WHERE Ordinal = ? AND IsStock = 1", id);
            if (rb != null && rb.ContainsKey("BodyAeroForwardDownforceFront"))
            {
                dfF += D(rb["BodyAeroForwardDownforceFront"]) * q0;
                dfR += D(rb["BodyAeroForwardDownforceRear"]) * q0;
            }
            double[] fw = Wing(db, "List_UpgradeFrontBumper", id), rw = Wing(db, "List_UpgradeRearWing", id);
            double p1 = ((1 - wF) * fw[2]) + wF, p2 = (wF * rw[2]) + (1 - wF);
            dfF += (p1 * fw[0]) + ((1 - p2) * rw[0]);
            dfR += ((1 - p1) * fw[0]) + (p2 * rw[0]);
            drag += fw[1] + rw[1];
            drag += bodyDrag * (fc.DragProduct - 1);

            const double unit = 1 / G;
            Tyre lonF = MakeTyre(db, comp, "FrictionMultiCurveLongitudinalID", wf, brand);
            Tyre lonR = MakeTyre(db, comp, "FrictionMultiCurveLongitudinalID", wr, brand);
            Tyre latF = MakeTyre(db, comp, "FrictionMultiCurveLateralID", wf, brand);
            Tyre latR = MakeTyre(db, comp, "FrictionMultiCurveLateralID", wr, brand);
            double tfB = D(comp["TorqueFreeLongFrictionScaleBrake"]), tfL = D(comp["TorqueFreeLatFrictionScale"]);
            Dictionary<string, object> bk = Row(db, "SELECT GameFrictionScaleBraking FROM List_UpgradeBrakes WHERE Ordinal = ? AND IsStock = 1", id);
            if (bk != null)
            {
                tfB *= D(bk["GameFrictionScaleBraking"]);
            }
            double dhF = 0, dhR = 0;
            Dictionary<string, object> sd = Row(db, "SELECT FrontSpringDamperPhysicsID, RearSpringDamperPhysicsID FROM List_UpgradeSpringDamper WHERE Ordinal = ? AND IsStock = 1", id);
            if (sd != null)
            {
                Dictionary<string, object> sf = Row(db, "SELECT DefRideHeight FROM List_SpringDamperPhysics WHERE SpringDamperPhysicsID = ?", sd["FrontSpringDamperPhysicsID"]);
                Dictionary<string, object> sr = Row(db, "SELECT DefRideHeight FROM List_SpringDamperPhysics WHERE SpringDamperPhysicsID = ?", sd["RearSpringDamperPhysicsID"]);
                if (sf != null && sr != null)
                {
                    double mid = 0.5 * (D(sf["DefRideHeight"]) + D(sr["DefRideHeight"]));
                    h = D(w["CMHeight"]) + mid;
                    dhF = D(sf["DefRideHeight"]) - mid;
                    dhR = -dhF;
                }
            }

            // Braking (82643870) and cornering (82643698) at speed v: [brake m/s2, corner m/s2, front-right load, rear-left load].
            Func<double, double[]> calcV = delegate(double v)
            {
                double q = (v / V) * (v / V), qF = dfF * q, qR = dfR * q, qD = drag * q;
                double wt = m * G, nf = (wF * wt) + qF, nr = ((1 - wF) * wt) + qR, nF0 = nf, nR0 = nr, kF = 0, kR = 0;
                double num = (aF * wt) + (L * qR), tot = wt + qF + qR;
                for (int i = 0; i < 6; i++)
                {
                    kF = lonF.Mu(0.5 * nf * unit) * gripF;
                    kR = lonR.Mu(0.5 * nr * unit) * gripF;
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
                return new double[] { (qD / m) + (tfB * ((kF * nf) + (kR * nr)) / m), tfL * ((j0 * m0) + (j1 * m1) + (j2 * m2) + (j3 * m3)) / m, m1 * unit, m2 * unit };
            };

            // Acceleration at speed v (82643DF0): geared (shift at the redline) or, for the PI, held at the peak power speed.
            double cAcc = D(comp["TorqueFreeLongFrictionScaleAccel1"]);
            Func<double, bool, double, double> accAt = delegate(double v, bool cvt, double torqueMul)
            {
                double gts = fc.GameTorqueScale * torqueMul, q = (v / V) * (v / V), R = fc.DrivenRadius, fd = fc.FinalDrive, tw0 = -1;
                if (cvt)
                {
                    double wP = fc.Eng.PeakPw;
                    tw0 = R * wP / v * fc.Eng.AtSim(wP) * 100 * gts;
                }
                else
                {
                    for (int g = 1; g < fc.Gears.Length; g++)
                    {
                        double gr = fc.Gears[g];
                        if (gr <= 0)
                        {
                            continue;
                        }
                        if (v < fc.Eng.RedlineW * R / (fd * gr))
                        {
                            tw0 = gr * fd * fc.Eng.AtSim(gr * fd * v / R) * 100 * gts;
                            break;
                        }
                    }
                }
                if (tw0 < 0)
                {
                    return 0;
                }
                double aw = m * G, dF = dfF * q, dR = dfR * q, aTot = aw + dF + dR, aNF = (wF * aw) + dF, aNR = ((1 - wF) * aw) + dR, aFF = 0, aFR = 0;
                for (int i = 0; i < 6; i++)
                {
                    aFF = cAcc * lonF.Mu(0.5 * aNF * unit) * gripF * aNF;
                    aFR = cAcc * lonR.Mu(0.5 * aNR * unit) * gripF * aNR;
                    if (fc.DriveType == 0)
                    {
                        double cap = Math.Max(fc.RadiusFront * aFF, 1e-5);
                        aFF *= Math.Min(tw0, cap) / cap;
                        aFR = 0;
                    }
                    else if (fc.DriveType == 1)
                    {
                        double cap = Math.Max(fc.RadiusRear * aFR, 1e-5);
                        aFR *= Math.Min(tw0, cap) / cap;
                        aFF = 0;
                    }
                    else
                    {
                        double cap = Math.Max((fc.RadiusFront * aFF) + (fc.RadiusRear * aFR), 1e-5), k = Math.Min(tw0, cap) / cap;
                        aFF *= k;
                        aFR *= k;
                    }
                    aNR = Clamp(((aF * aw) + (L * dR) + (h * (aFF + aFR) / cAcc)) / L, 0, aTot);
                    aNF = aTot - aNR;
                }
                return (aFF + aFR - (drag * q)) / m;
            };

            Result res = new Result();
            res.PeakT = fc.Eng.PeakT;
            res.PeakTw = fc.Eng.PeakTw;
            res.PeakP = fc.Eng.PeakP;
            res.PeakPw = fc.Eng.PeakPw;
            res.RedlineW = fc.Eng.RedlineW;
            res.TopSpeed = fc.TopSpeed();
            res.SpeedRating = Clamp(((res.TopSpeed - 55) * 0.109139219) + 3, 3, 10);
            double sum = 0;
            for (int s = 0; s <= 12; s++)
            {
                sum += accAt(8.9408 + (s / 12.0 * 26.8224), false, 1);
            }
            res.AccelerationRating = Clamp(((sum / 13) - 3.788) * 0.6448598 + 3, 3, 10);

            // PI (8259E660): 18 inputs. Slip scale = PeakSASRScale (brand) x StartPressureAffectPeakSASR at 30 psi.
            Net net = Net.Load(netDir);
            if (net == null)
            {
                return res;
            }
            Dictionary<string, object> pc = Row(db, "SELECT * FROM List_TireAffectCurve WHERE AffectCurveID = ?", comp["AffectCurveStartPressureAffectPeakSASRID"]);
            double sasr = (br == null ? 1 : D(br["PeakSASRScale"])) * (pc != null ? AffectCurve(pc, 30) : 1);
            latF.SlipScale = sasr;
            latR.SlipScale = sasr;
            double[] x = new double[18];
            for (int k = 1; k <= 3; k++)
            {
                double[] bc = calcV(k * 2 * 11.176);
                x[(3 * k) - 3] = net.Norm(bc[0], "Brake");
                x[(3 * k) - 2] = 0.5 * (latF.Slip(bc[2]) + latR.Slip(bc[3]));       // the slip angle goes in raw
                x[(3 * k) - 1] = net.Norm(bc[1], "Corner");
            }
            for (int k = 1; k <= 7; k++)
            {
                x[8 + k] = net.Norm(accAt(k * 11.176, true, 1), "Accel");
            }
            double moiy = 1200 / (m * ((D(w["BlockDimX"]) * D(w["BlockDimX"])) + (D(w["BlockDimZ"]) * D(w["BlockDimZ"]))));
            x[16] = net.Norm(moiy, "MOIY");
            x[17] = fc.DriveType * 0.5;
            double pi = net.Run(x);
            // 8259E9A0: the best of GameTorqueScale x 1, 0.95 .. 0.65.
            foreach (double mul in new double[] { 0.95, 0.9, 0.85, 0.8, 0.75, 0.7, 0.65 })
            {
                for (int k = 1; k <= 7; k++)
                {
                    x[8 + k] = net.Norm(accAt(k * 11.176, true, mul), "Accel");
                }
                pi = Math.Max(pi, net.Run(x));
            }
            res.Pi = pi;
            return res;
        }

        // A stock wing's [downforce N, drag N, balance curve "Two"] at V (List_AeroPhysics, stock slider = DefaultTuneSlider).
        private static double[] Wing(SqliteDatabase db, string table, long id)
        {
            Dictionary<string, object> u = Row(db, "SELECT AeroPhysicsID FROM " + table + " WHERE Ordinal = ? AND IsStock = 1", id);
            Dictionary<string, object> ap = u == null ? null : Row(db, "SELECT * FROM List_AeroPhysics WHERE AeroPhysicsID = ?", u["AeroPhysicsID"]);
            if (ap == null)
            {
                return new double[3];
            }
            double s = D(ap["DefaultTuneSlider"]), q0 = K * V * V * 100;
            const string p = "DFTorqueScaleTwo";
            double bal = s <= D(ap[p + "SliderInput1"])
                ? Lerp(s, D(ap[p + "SliderInput0"]), D(ap[p + "SliderInput1"]), D(ap[p + "TorqueScaleOutput0"]), D(ap[p + "TorqueScaleOutput1"]))
                : Lerp(s, D(ap[p + "SliderInput1"]), D(ap[p + "SliderInput2"]), D(ap[p + "TorqueScaleOutput1"]), D(ap[p + "TorqueScaleOutput2"]));
            return new double[] { (D(ap["Downforce0"]) + ((D(ap["Downforce1"]) - D(ap["Downforce0"])) * s)) * q0, (D(ap["Drag0"]) + ((D(ap["Drag1"]) - D(ap["Drag0"])) * s)) * q0, bal };
        }

        // Clamped lerp that follows the order of x0 / x1.
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

        // 82658300 + 826583F0: List_TireAffectCurve, uniform clamped lerp of v0..v(N-1) over MinInput..MaxInput, x OutputScale.
        private static double AffectCurve(Dictionary<string, object> c, double x)
        {
            int n = Convert.ToInt32(c["NumValues"], CultureInfo.InvariantCulture);
            double lo = D(c["MinInput"]), hi = D(c["MaxInput"]);
            x = Clamp(x, Math.Min(lo, hi), Math.Max(lo, hi));
            double f = (x - lo) * (n - 1) / (hi - lo);
            int i = Math.Min((int) f, n - 2);
            return (D(c["v" + i]) + ((f - i) * (D(c["v" + (i + 1)]) - D(c["v" + i])))) * D(c["OutputScale"]);
        }

        private sealed class Tyre
        {
            public double X0, X1, Y0, Y1, Scale = 1, S0, S1, SlipScale = 1;

            public double Mu(double load)
            {
                return Lerp(load, this.X0, this.X1, this.Y0, this.Y1) * this.Scale;
            }

            public double Slip(double load)
            {
                return Lerp(load, this.X0, this.X1, this.S0, this.S1) * this.SlipScale;
            }
        }

        // Tyre friction from List_TireFrictionMultiCurve: peak of each curve x FrictionScale, x the width scale, x the brand.
        private static Tyre MakeTyre(SqliteDatabase db, Dictionary<string, object> comp, string multiCol, double width, double brand)
        {
            Dictionary<string, object> mc = Row(db, "SELECT * FROM List_TireFrictionMultiCurve WHERE FrictionMultiCurveID = ?", comp[multiCol]);
            Dictionary<string, object> c0 = Row(db, "SELECT * FROM List_TireFrictionCurve WHERE FrictionCurveID = ?", mc["TireFrictionCurveID0"]);
            Dictionary<string, object> c1 = Row(db, "SELECT * FROM List_TireFrictionCurve WHERE FrictionCurveID = ?", mc["TireFrictionCurveID1"]);
            Func<Dictionary<string, object>, double> peak = delegate(Dictionary<string, object> c)
            {
                int n = Convert.ToInt32(c["NumCurveValues"], CultureInfo.InvariantCulture);
                double best = 0;
                for (int i = 0; i < n && c.ContainsKey("v" + i); i++)
                {
                    best = Math.Max(best, D(c["v" + i]));
                }
                return best * D(c["FrictionScale"]);
            };
            int cnt = Convert.ToInt32(c0["NumCurveValues"], CultureInfo.InvariantCulture);
            Func<Dictionary<string, object>, int> peakIndex = delegate(Dictionary<string, object> c)
            {
                double best = 0;
                int bi = 0;
                for (int i = 0; i < cnt; i++)
                {
                    double y = D(Get(c, "v" + i)) * D(c["FrictionScale"]);
                    if (y > best)
                    {
                        best = y;
                        bi = i;
                    }
                }
                return bi;
            };
            Tyre t = new Tyre { X0 = D(mc["MinLoadCurve"]), X1 = D(mc["MaxLoadCurve"]), Y0 = peak(c0), Y1 = peak(c1) };
            double step = D(mc["MaxSlip"]) * 0.0174532924 / (cnt - 1);
            t.S0 = peakIndex(c0) * step;
            t.S1 = peakIndex(c1) * step;
            t.Scale = Lerp(width, D(comp["TireFricWidth0"]), D(comp["TireFricWidth1"]), D(comp["TireFricScale0"]), D(comp["TireFricScale1"])) * brand;
            return t;
        }

        // ---- the stock engine (82594120 / 82593FA8 / 82637108) ----

        private sealed class Engine
        {
            public double Redline, MaxRpm, Step, Cut;
            public double[] Torque;                                 // N.m / 100 per point, boost applied, up to the cut-off
            public double PeakT, PeakTw, PeakP, PeakPw, RedlineW;

            private static double Lerp(double x, double x0, double x1, double y0, double y1)
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

            public static Engine Build(SqliteDatabase db, long engine)
            {
                SqliteDatabase.Result cv = db.Query("SELECT RedlineRPM, TorqueCurveMaxRPM, NumRPMEntriesArray, TorqueCurveFullThrottleID FROM List_UpgradeEngineCamValves WHERE EngineID = ? AND IsStock = 1", engine);
                if (cv.Rows.Count == 0)
                {
                    return null;
                }
                Engine e = new Engine();
                e.Redline = D(cv.Rows[0][0]);
                e.MaxRpm = D(cv.Rows[0][1]);
                int n = Convert.ToInt32(cv.Rows[0][2], CultureInfo.InvariantCulture);
                SqliteDatabase.Result curve = db.Query("SELECT * FROM List_TorqueCurve WHERE TorqueCurveID = ?", cv.Rows[0][3]);
                if (curve.Rows.Count == 0 || n < 2)
                {
                    return null;
                }
                double scale = D(curve.Rows[0][Array.IndexOf(curve.Columns, "TorqueScale")]);
                int v0 = Array.IndexOf(curve.Columns, "v0");
                double mult = 1;
                foreach (string t in new string[] { "List_UpgradeEngineBlock", "List_UpgradeEngineExhaust", "List_UpgradeEngineFuelSystem", "List_UpgradeEngineIgnition", "List_UpgradeEngineIntake" })
                {
                    foreach (object[] r in db.Query("SELECT TorqueScale FROM " + t + " WHERE EngineID = ? AND IsStock = 1", engine).Rows)
                    {
                        mult += D(r[0]) - 1;
                    }
                }
                double ic = 1;
                object[] tb = null, sc = null;
                SqliteDatabase.Result tbq = db.Query("SELECT MaxScale, PowerMaxScale, MinScale, PowerMinScale, RobScale, TorqueDropOffRPM0, TorqueDropOffRPM1, TorqueDropOffScale0, TorqueDropOffScale1 FROM List_UpgradeEngineTurbo WHERE EngineID = ? AND IsStock = 1", engine);
                if (tbq.Rows.Count > 0 && Math.Abs(D(tbq.Rows[0][0]) - 1) >= 0.001)
                {
                    tb = tbq.Rows[0];
                }
                foreach (string st in new string[] { "List_UpgradeEngineDSC", "List_UpgradeEngineCSC" })
                {
                    SqliteDatabase.Result q = db.Query("SELECT ZeroRPMScale, RedlineRPMScale, RobScale, TorqueDropOffRPM0, TorqueDropOffRPM1, TorqueDropOffScale0, TorqueDropOffScale1 FROM " + st + " WHERE EngineID = ? AND IsStock = 1", engine);
                    if (q.Rows.Count > 0 && sc == null)
                    {
                        sc = q.Rows[0];
                    }
                }
                SqliteDatabase.Result icq = db.Query("SELECT MaxScaleScale FROM List_UpgradeEngineIntercooler WHERE EngineID = ? AND IsStock = 1", engine);
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
                        tf = Lerp(prevPower, D(tb[3]) * HP, D(tb[1]) * HP, tMin, tMax);
                        if (tf > 1)
                        {
                            double k = tf - 1;
                            tf = Lerp(w, D(tb[5]) * W, D(tb[6]) * W, 1 + (D(tb[7]) * k), 1 + (D(tb[8]) * k));
                        }
                    }
                    if (sc != null)
                    {
                        sf = Lerp(w, 0, e.Redline * W, sZero, sRed);
                        if (sf > 1)
                        {
                            double k = sf - 1;
                            sf = Lerp(w, D(sc[3]) * W, D(sc[4]) * W, 1 + (D(sc[5]) * k), 1 + (D(sc[6]) * k));
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

            // 82557700 over the sim table: entries at i * step, looked up as if n * step spanned n - 1 intervals.
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

        // ---- the car's drivetrain and drag ----

        private sealed class Fm2Car
        {
            public Engine Eng;
            public double GameTorqueScale, DragProduct = 1, BodyDrag, FrontWingDrag, RearWingDrag, FinalDrive, RadiusFront, RadiusRear;
            public double[] Gears;          // Gears[0] = reverse
            public int DriveType;           // 0 FWD, 1 RWD, 2 AWD

            public double DrivenRadius
            {
                get { return this.DriveType == 1 ? this.RadiusRear : this.RadiusFront; }
            }

            private static double WingDrag(SqliteDatabase db, string table, long car)
            {
                Dictionary<string, object> part = Row(db, "SELECT AeroPhysicsID FROM " + table + " WHERE Ordinal = ? AND IsStock = 1", car);
                Dictionary<string, object> aero = part == null ? null : Row(db, "SELECT * FROM List_AeroPhysics WHERE AeroPhysicsID = ?", part["AeroPhysicsID"]);
                if (aero == null)
                {
                    return 0;
                }
                double s = D(aero["DefaultTuneSlider"]);
                return (D(aero["Drag0"]) + ((D(aero["Drag1"]) - D(aero["Drag0"])) * s)) * K;
            }

            public static Fm2Car Build(SqliteDatabase db, long id)
            {
                Fm2Car c = new Fm2Car();
                Dictionary<string, object> car = Row(db, "SELECT * FROM Data_Car WHERE Id = ?", id);
                Dictionary<string, object> eng = Row(db, "SELECT EngineID FROM List_UpgradeEngine WHERE Ordinal = ? AND IsStock = 1", id);
                Dictionary<string, object> tr = Row(db, "SELECT * FROM List_UpgradeTransmission WHERE Ordinal = ? AND IsStock = 1", id);
                if (car == null || eng == null || tr == null)
                {
                    return null;
                }
                long engine = Convert.ToInt64(eng["EngineID"], CultureInfo.InvariantCulture);
                c.Eng = Engine.Build(db, engine);
                c.GameTorqueScale = Clamp(D(car["GameTorqueScale"]), 0.5, 1.5);
                double gameDrag = Clamp(D(car["GameDragScale"]), 0.5, 1.5);
                // Product of DragScale of every stock part row (car tables by Ordinal, engine tables by EngineID).
                foreach (object[] t in db.Query("SELECT name, sql FROM sqlite_master WHERE type = 'table' AND name LIKE 'List_Upgrade%' AND sql LIKE '%DragScale%'").Rows)
                {
                    string name = (string) t[0], sql = (string) t[1];
                    string key = sql.Contains("Ordinal") ? "Ordinal" : "EngineID";
                    foreach (object[] r in db.Query("SELECT DragScale FROM " + SqliteDatabase.Quote(name) + " WHERE " + key + " = ? AND IsStock = 1", key == "Ordinal" ? id : engine).Rows)
                    {
                        c.DragProduct *= D(r[0]);
                    }
                }
                c.BodyDrag = D(car["BodyAeroLongitudinalDrag"]) * K * c.DragProduct * gameDrag;
                c.FrontWingDrag = WingDrag(db, "List_UpgradeFrontBumper", id);
                c.RearWingDrag = WingDrag(db, "List_UpgradeRearWing", id);
                c.FinalDrive = D(tr["FinalDriveRatio"]);
                int ng = Math.Min(8, Convert.ToInt32(tr["NumGears"], CultureInfo.InvariantCulture));
                c.Gears = new double[Math.Max(0, ng)];
                for (int i = 0; i < c.Gears.Length; i++)
                {
                    c.Gears[i] = D(Get(tr, "GearRatio" + i));
                }
                // 82596A40: tyre radius = width * aspect + rim (stock sizes).
                c.RadiusFront = (D(car["StockTire-FrontWidth-mm"]) * 0.001 * D(car["StockTire-FrontAspect-pct"]) * 0.01) + (D(car["StockTire-FrontWheel-in"]) * 0.0127);
                c.RadiusRear = (D(car["StockTire-RearWidth-mm"]) * 0.001 * D(car["StockTire-RearAspect-pct"]) * 0.01) + (D(car["StockTire-RearWheel-in"]) * 0.0127);
                Dictionary<string, object> dt = Row(db, "SELECT DriveType FROM Data_Engine e LEFT JOIN List_DriveType d ON e.DriveTypeID = d.ID WHERE e.EngineID = ?", engine);
                string drive = dt == null ? "RWD" : Convert.ToString(dt["DriveType"], CultureInfo.InvariantCulture);
                c.DriveType = drive == "FWD" ? 0 : drive == "AWD" ? 2 : 1;
                return c;
            }

            // 8264A5B8: min(power / drag limit, top gear at (redline + TorqueCurveMaxRPM) / 2).
            public double TopSpeed()
            {
                double vPow = Math.Pow(this.GameTorqueScale * this.Eng.PeakP / (this.RearWingDrag + this.FrontWingDrag + this.BodyDrag), 1.0 / 3.0);
                double w = (this.Eng.Redline + this.Eng.MaxRpm) * 0.5 * W;
                double vGear = w / this.Gears[this.Gears.Length - 1] / this.FinalDrive * this.DrivenRadius;
                return Math.Min(vPow, vGear);
            }
        }

        // ---- the game's network (827D0BC8): carperfpred.nn + carperfpredcnst.bin, big-endian ----

        private sealed class Net
        {
            private static readonly Dictionary<string, Net> Cache = new Dictionary<string, Net>(StringComparer.OrdinalIgnoreCase);
            private int[] neurons, inputsPer;
            private double response;
            private double[] weights;
            private Dictionary<string, double> constants;

            public static Net Load(string dir)
            {
                Net net;
                if (Cache.TryGetValue(dir, out net))
                {
                    return net;
                }
                string nn = Path.Combine(dir, "carperfpred.nn"), cn = Path.Combine(dir, "carperfpredcnst.bin");
                if (File.Exists(nn) && File.Exists(cn))
                {
                    byte[] b = File.ReadAllBytes(nn);
                    Dictionary<string, int[]> h = Header(b);
                    net = new Net();
                    int n = h["numNeuronsPerLayer"][0];
                    net.neurons = new int[n];
                    net.inputsPer = new int[n];
                    for (int i = 0; i < n; i++)
                    {
                        net.neurons[i] = BeInt(b, h["numNeuronsPerLayer"][1] + (4 * i));
                        net.inputsPer[i] = BeInt(b, h["numInputsPerLayer"][1] + (4 * i));
                    }
                    net.response = BeDouble(b, h["activationResponse"][1]);
                    net.weights = new double[h["weights"][0]];
                    for (int i = 0; i < net.weights.Length; i++)
                    {
                        net.weights[i] = BeDouble(b, h["weights"][1] + (8 * i));
                    }
                    byte[] cb = File.ReadAllBytes(cn);
                    net.constants = new Dictionary<string, double>();
                    foreach (KeyValuePair<string, int[]> kv in Header(cb))
                    {
                        net.constants[kv.Key] = BeDouble(cb, kv.Value[1]);
                    }
                }
                Cache[dir] = net;
                return net;
            }

            // name:type:offset:?:size lines up to "MLPDataEnd" -> name = [size, offset].
            private static Dictionary<string, int[]> Header(byte[] b)
            {
                Dictionary<string, int[]> d = new Dictionary<string, int[]>();
                string text = Encoding.ASCII.GetString(b, 0, Math.Min(b.Length, 400));
                foreach (string line in text.Split('\n'))
                {
                    string[] p = line.Split(':');
                    if (p.Length == 5)
                    {
                        d[p[0]] = new int[] { int.Parse(p[2], CultureInfo.InvariantCulture), int.Parse(p[4].Trim(), CultureInfo.InvariantCulture) };
                    }
                    if (line.StartsWith("MLPDataEnd", StringComparison.Ordinal))
                    {
                        break;
                    }
                }
                return d;
            }

            private static int BeInt(byte[] b, int o)
            {
                return (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
            }

            private static double BeDouble(byte[] b, int o)
            {
                byte[] x = new byte[8];
                Array.Copy(b, o, x, 0, 8);
                Array.Reverse(x);
                return BitConverter.ToDouble(x, 0);
            }

            // (x - min) / (max - min), clamped 0..1, NaN -> 0.
            public double Norm(double x, string key)
            {
                double y = (x - this.constants["min" + key]) / (this.constants["max" + key] - this.constants["min" + key]);
                return double.IsNaN(y) ? 0 : Clamp(y, 0, 1);
            }

            // Per neuron: net = sum(w x) - w_bias, out = 1 / (1 + exp(-net / activationResponse)).
            public double Run(double[] x)
            {
                int k = 0;
                for (int layer = 0; layer < this.neurons.Length; layer++)
                {
                    double[] y = new double[this.neurons[layer]];
                    for (int j = 0; j < y.Length; j++)
                    {
                        double sum = 0;
                        for (int i = 0; i < this.inputsPer[layer]; i++)
                        {
                            sum += this.weights[k++] * x[i];
                        }
                        sum -= this.weights[k++];
                        y[j] = 1.0 / (1.0 + Math.Exp(-sum / this.response));
                    }
                    x = y;
                }
                return x[0];
            }
        }
    }
}
