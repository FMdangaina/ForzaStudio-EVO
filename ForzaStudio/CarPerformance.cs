namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    // The numbers of a stock car's straight-line performance, from a small longitudinal simulation of what the
    // database holds: the stock engine's torque curve, the stock gearbox, the tires, weight, drag and traction.
    // It is only used as a RATIO between two states of the same car (the source and its copy with an engine of
    // its own): the game's own numbers (Data_Car.SimTime*, SimTopSpeed, the ratings, PerformanceIndex) are scaled
    // by how much the simulated car changed, so what the simulation gets wrong in absolute terms cancels.
    public sealed class PerfResult
    {
        public double T60;              // s to 60 mph
        public double T100;             // s to 100 mph
        public double QuarterTime;      // s
        public double QuarterSpeed;     // m/s at the end of the quarter mile
        public double TopSpeed;         // m/s
        public double PeakPower;        // W
    }

    public static class CarPerformance
    {
        // Calibrated on the 501 stock cars of FM4 (tools in the session of 2026-10-24).
        public static double DragScale = 0.0027;           // N / (m/s)^2 per unit of Data_Car.BodyAeroLongitudinalDrag
        public static double Efficiency = 0.86;            // engine to wheels
        public static double Grip = 1.15;                  // tire friction used for the traction limit
        public static double Rolling = 0.013;              // rolling resistance coefficient

        private static double Real(object value)
        {
            return (value == null || value == DBNull.Value) ? 0 : Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        // null when the car cannot be simulated, also when the game's tables are not FM4's / FM3's (FM2: no DriveTypeID, its
        // camshaft and gearbox tables are others): the caller then keeps the source's numbers.
        public static PerfResult Simulate(SqliteDatabase db, long carId)
        {
            try
            {
                return SimulateCore(db, carId);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static PerfResult SimulateCore(SqliteDatabase db, long carId)
        {
            SqliteDatabase.Result car = db.Query("SELECT CurbWeight, WeightDistribution, DriveTypeID, BodyAeroLongitudinalDrag, FrontTireWidthMM, FrontTireAspect, FrontWheelDiameterIN, RearTireWidthMM, RearTireAspect, RearWheelDiameterIN FROM Data_Car WHERE Id = ?", carId);
            if (car.Rows.Count == 0)
            {
                return null;
            }
            object[] c = car.Rows[0];
            double mass = Real(c[0]) * 100;
            double front = Real(c[1]);
            long drive = (long) Real(c[2]);
            double drag = Real(c[3]) * DragScale;
            bool frontDrive = drive == 1;
            double width = Real(frontDrive ? c[4] : c[7]), aspect = Real(frontDrive ? c[5] : c[8]), wheel = Real(frontDrive ? c[6] : c[9]);
            double radius = ((wheel * 25.4 / 2) + (width * aspect / 100)) / 1000;
            if (mass <= 0 || radius <= 0)
            {
                return null;
            }

            object engineId = db.Scalar("SELECT EngineID FROM List_UpgradeEngine WHERE Ordinal = ? AND IsStock = 1 ORDER BY Level LIMIT 1", carId);
            if (engineId == null || engineId == DBNull.Value)
            {
                return null;
            }
            long engine = Convert.ToInt64(engineId, CultureInfo.InvariantCulture);
            SqliteDatabase.Result cam = db.Query("SELECT RedlineRPM, TorqueCurveMaxRPM, StallRPM, TorqueCurveFullThrottleID FROM List_UpgradeEngineCamshaft WHERE EngineID = ? AND IsStock = 1 ORDER BY Level LIMIT 1", engine);
            if (cam.Rows.Count == 0)
            {
                return null;
            }
            double redline = Real(cam.Rows[0][0]), maxRpm = Real(cam.Rows[0][1]), stall = Real(cam.Rows[0][2]);
            double scale;
            double[] values;   // by column name: FM3's curve has an EngineID before TorqueScale
            if (!CarEngine.CurveRow(db, Convert.ToInt64(cam.Rows[0][3], CultureInfo.InvariantCulture), out scale, out values) || redline <= 0 || maxRpm <= 0)
            {
                return null;
            }
            int count = values.Length;
            if (count < 2)
            {
                return null;
            }
            double factor, boost;
            CarEngine.StockMultipliers(db, engine, out factor, out boost);
            double aspiration = Real(db.Scalar("SELECT AspirationID_Stock FROM Data_Engine WHERE EngineID = ?", engine));
            double all = scale * factor * ((aspiration > 1) ? boost : 1);
            double[] torque = new double[count];
            for (int i = 0; i < count; i++)
            {
                torque[i] = values[i] * all;
            }
            double inertia = Real(db.Scalar("SELECT MomentInertia FROM Data_Engine WHERE EngineID = ?", engine));

            object drivetrain = db.Scalar("SELECT DrivetrainID FROM List_UpgradeDrivetrain WHERE Ordinal = ? AND IsStock = 1 ORDER BY Level LIMIT 1", carId);
            if (drivetrain == null || drivetrain == DBNull.Value)
            {
                return null;
            }
            SqliteDatabase.Result gearbox = db.Query("SELECT FinalDriveRatio, NumGears, GearShiftTime, GearRatio1, GearRatio2, GearRatio3, GearRatio4, GearRatio5, GearRatio6, GearRatio7, GearRatio8 FROM List_UpgradeDrivetrainTransmission WHERE DrivetrainID = ? AND IsStock = 1 ORDER BY Level LIMIT 1", Convert.ToInt64(drivetrain, CultureInfo.InvariantCulture));
            if (gearbox.Rows.Count == 0)
            {
                return null;
            }
            object[] g = gearbox.Rows[0];
            double finalDrive = Real(g[0]);
            int forward = Math.Min(8, (int) Real(g[1]) - 1);
            double shift = Math.Max(0.1, Real(g[2]));
            List<double> ratios = new List<double>();
            for (int i = 0; i < forward; i++)
            {
                double r = Real(g[3 + i]);
                if (r > 0)
                {
                    ratios.Add(r * finalDrive);
                }
            }
            if (ratios.Count == 0 || finalDrive <= 0)
            {
                return null;
            }

            double drivenShare = (drive == 3) ? 1.0 : (frontDrive ? front : 1 - front);
            double wheels = mass * 0.02;
            PerfResult result = new PerfResult();
            double peakTorqueRpm = 0, bestTorque = 0;
            for (int i = 0; i < count; i++)
            {
                double rpm = i * maxRpm / (count - 1);
                if (rpm > redline)
                {
                    break;
                }
                if (torque[i] > bestTorque)
                {
                    bestTorque = torque[i];
                    peakTorqueRpm = rpm;
                }
                result.PeakPower = Math.Max(result.PeakPower, torque[i] * rpm * 2 * Math.PI / 60);
            }
            double launch = Math.Max(stall + 300, Math.Min(peakTorqueRpm, redline * 0.7));
            double v = 0, x = 0, t = 0;
            int gear = 0;
            double dt = 0.005;
            double waiting = 0;
            bool got60 = false, got100 = false, gotQuarter = false;
            double previousV = 0;
            for (int step = 0; step < 100000 && t < 240; step++)
            {
                double ratio = ratios[gear];
                double rpm = Math.Max(launch, v / radius * ratio * 60 / (2 * Math.PI));
                double force = 0;
                if (waiting > 0)
                {
                    waiting -= dt;
                }
                else
                {
                    force = TorqueAt(torque, maxRpm, redline, rpm) * ratio * Efficiency / radius;
                    if (gear + 1 < ratios.Count)
                    {
                        double nextRpm = v / radius * ratios[gear + 1] * 60 / (2 * Math.PI);
                        double nextForce = TorqueAt(torque, maxRpm, redline, Math.Max(nextRpm, 1)) * ratios[gear + 1] * Efficiency / radius;
                        if (rpm >= redline * 0.999 || (nextRpm >= launch && nextForce > force * 1.02))
                        {
                            gear++;
                            waiting = shift;
                            force = 0;
                        }
                    }
                    double limit = Grip * mass * 9.81 * Math.Min(1.0, drivenShare + (0.12 * Math.Min(1.0, force / (mass * 9.81))));
                    force = Math.Min(force, limit);
                    if (rpm > redline * 1.0005 && gear + 1 >= ratios.Count)
                    {
                        force = 0;
                    }
                }
                double effective = mass + wheels + (inertia * ratio * ratio / (radius * radius)) * (waiting > 0 ? 0 : 1);
                double resist = (drag * v * v) + (Rolling * mass * 9.81);
                double a = (force - resist) / effective;
                v += a * dt;
                if (v < 0)
                {
                    v = 0;
                }
                x += v * dt;
                t += dt;
                if (!got60 && v >= 26.8224)
                {
                    result.T60 = t;
                    got60 = true;
                }
                if (!got100 && v >= 44.704)
                {
                    result.T100 = t;
                    got100 = true;
                }
                if (!gotQuarter && x >= 402.336)
                {
                    result.QuarterTime = t;
                    result.QuarterSpeed = v;
                    gotQuarter = true;
                }
                if (a < 0.002 && waiting <= 0 && step > 2000 && Math.Abs(v - previousV) < 1e-6)
                {
                    break;
                }
                previousV = v;
                result.TopSpeed = Math.Max(result.TopSpeed, v);
            }
            if (!got100)
            {
                result.T100 = 0;
            }
            return result;
        }

        // A copy with an engine of its own: the source's Sim times, top speed, ratings and PerformanceIndex scaled by how
        // much the simulated car changed (checked on the 501 stock cars: the simulation is within about 3% on top speed
        // and quarter mile, 8% on 0-60; the ratings follow the Sim columns with R2 0.90-0.97 and the PI the ratings with
        // R2 0.989, as in FM4's own data). The Sim peak power / torque are CarEngine's. Handling and braking stay (they
        // come from the lateral g and the brake distance, which the engine does not change). userSet = Data_Car columns
        // the user typed in the wizard: left as typed.
        // timesOnly (FM3 / FM4: Fm3Perf, Fm4Perf): only the Sim times, top speed and the shown figures are scaled; the ratings, PI and class are
        // the game's own calculation, written by the caller.
        public static void Update(SqliteDatabase db, SqliteDatabase original, long sourceId, long carId, ICollection<string> userSet, List<string> plan, bool timesOnly = false)
        {
            PerfResult s = Simulate(original, sourceId);
            PerfResult c = Simulate(db, carId);
            if (s == null || c == null || s.T60 <= 0 || c.T60 <= 0 || s.TopSpeed <= 0 || c.TopSpeed <= 0 || s.QuarterSpeed <= 0 || c.QuarterSpeed <= 0 || s.QuarterTime <= 0 || c.QuarterTime <= 0)
            {
                plan.Add(timesOnly
                    ? "performance (Sim times): not scaled (the car's engine, gearbox or tires could not be simulated); the times and shown figures stay the source's"
                    : "performance: not recomputed (the car's engine, gearbox or tires could not be simulated); the times, ratings and PI stay the source's");
                return;
            }
            string[] names = { "SimTimeTo60MPH", "SimTimeTo100MPH", "SimTimeQuarterMile", "SimSpeedQuarterMile", "SimTopSpeed", "SpeedRating", "AccelerationRating", "LaunchRating", "PerformanceIndex", "ClassID" };
            foreach (string name in names)
            {
                if (!DbIds.Schema(db)["Data_Car"].Exists(delegate(string column) { return string.Equals(column, name, StringComparison.OrdinalIgnoreCase); }))
                {
                    plan.Add("performance: not recomputed (Data_Car has no " + name + ")");
                    return;
                }
            }
            object[] row = original.Query("SELECT " + string.Join(", ", names) + " FROM Data_Car WHERE Id = ?", sourceId).Rows[0];
            double t60 = Real(row[0]), t100 = Real(row[1]), tq = Real(row[2]), vq = Real(row[3]), top = Real(row[4]);
            double k60 = c.T60 / s.T60;
            double k100 = (s.T100 > 0 && c.T100 > 0) ? c.T100 / s.T100 : k60;
            double kq = c.QuarterTime / s.QuarterTime, kvq = c.QuarterSpeed / s.QuarterSpeed, ktop = c.TopSpeed / s.TopSpeed;
            double nt60 = t60 * k60, nt100 = t100 * k100, ntq = tq * kq, nvq = vq * kvq, ntop = top * ktop;
            // FM4's bars run 0 to 10 (all 503 cars: maximum exactly 10, also Veyron and Zonda R), and its PI follows the capped bars.
            double speed = Math.Min(10, Math.Max(0, Real(row[5]) + (0.14631 * (ntop - top))));
            double acceleration = Math.Min(10, Math.Max(0, Real(row[6]) + (-0.21026 * (nt60 - t60)) + (0.13644 * (nvq - vq))));
            double launch = Math.Min(10, Math.Max(0, Real(row[7]) + (-0.61434 * (nt60 - t60))));
            double pi = Math.Max(0, Real(row[8]) + (0.01328 * (speed - Real(row[5]))) + (0.05059 * (acceleration - Real(row[6]))) + (0.01559 * (launch - Real(row[7]))));
            object classId = timesOnly ? row[9] : (CarPort.ClassFor(db, pi, carId) ?? row[9]);
            if (timesOnly)
            {
                db.Execute("UPDATE Data_Car SET SimTimeTo60MPH = ?, SimTimeTo100MPH = ?, SimTimeQuarterMile = ?, SimSpeedQuarterMile = ?, SimTopSpeed = ? WHERE Id = ?",
                    nt60, nt100, ntq, nvq, ntop, carId);
            }
            else
            {
                db.Execute("UPDATE Data_Car SET SimTimeTo60MPH = ?, SimTimeTo100MPH = ?, SimTimeQuarterMile = ?, SimSpeedQuarterMile = ?, SimTopSpeed = ?, SpeedRating = ?, AccelerationRating = ?, LaunchRating = ?, PerformanceIndex = ?, ClassID = ? WHERE Id = ?",
                    nt60, nt100, ntq, nvq, ntop, speed, acceleration, launch, pi, classId, carId);
            }

            // The figures the car's page shows (0-60, top speed...): scaled the same way, unless the user typed them.
            object[][] shown = {
                new object[] { "Time:0-60-sec", k60 }, new object[] { "Time:0-100-sec", k100 }, new object[] { "QuarterMileTime-sec", kq },
                new object[] { "QuarterMileSpeed-mph", kvq }, new object[] { "TopSpeed-mph", ktop } };
            List<string> scaled = new List<string>();
            foreach (object[] pair in shown)
            {
                string column = (string) pair[0];
                if (userSet.Contains(column) || !DbIds.Schema(db)["Data_Car"].Exists(delegate(string x) { return string.Equals(x, column, StringComparison.OrdinalIgnoreCase); }))
                {
                    continue;
                }
                double old = Real(original.Scalar("SELECT " + SqliteDatabase.Quote(column) + " FROM Data_Car WHERE Id = ?", sourceId));
                if (old > 0)
                {
                    db.Execute("UPDATE Data_Car SET " + SqliteDatabase.Quote(column) + " = ? WHERE Id = ?", Math.Round(old * (double) pair[1], 1), carId);
                    scaled.Add(column);
                }
            }
            if (timesOnly)
            {
                plan.Add(string.Format(CultureInfo.InvariantCulture, "performance (Sim times, scaled source -> copy): 0-60 {0:0.0} -> {1:0.0} s, top speed {2:0} -> {3:0} km/h, quarter mile {4:0.0} -> {5:0.0} s; shown figures scaled: {6}",
                    t60, nt60, top * 3.6, ntop * 3.6, tq, ntq, scaled.Count == 0 ? "none" : string.Join(", ", scaled.ToArray())));
                return;
            }
            plan.Add(string.Format(CultureInfo.InvariantCulture, "performance (simulated, source -> copy): 0-60 {0:0.0} -> {1:0.0} s, top speed {2:0} -> {3:0} km/h, quarter mile {4:0.0} -> {5:0.0} s; PI {6:0} -> {7:0} (class {8}); shown figures scaled: {9}",
                t60, nt60, top * 3.6, ntop * 3.6, tq, ntq, (Real(row[8]) * 1000) + 100, (pi * 1000) + 100, classId, scaled.Count == 0 ? "none" : string.Join(", ", scaled.ToArray())));
        }

        // The PI the game shows (A 542) from Data_Car.PerformanceIndex: a line between the points of CarClasses
        // (MaxPerformanceIndex -> MaxDisplayPerformanceIndex: 0.4177 -> 500, 0.53 -> 600...), rounded up. Checked on a
        // ported Bowler: stored 0.4639 = 541.1, shown 542. Below the first class it runs from (0, 100).
        public static int ShownPi(SqliteDatabase db, double pi)
        {
            List<double[]> points = ClassPoints(db);
            double lastX = 0, lastY = 100;
            foreach (double[] p in points)
            {
                if (pi <= p[0])
                {
                    return (int) Math.Ceiling(lastY + ((p[1] - lastY) * (pi - lastX) / Math.Max(1e-9, p[0] - lastX)));
                }
                lastX = p[0];
                lastY = p[1];
            }
            return (int) Math.Ceiling(lastY);
        }

        // The Data_Car.PerformanceIndex the game shows as this PI (the middle of the integer's step).
        public static double StoredPi(SqliteDatabase db, int shown)
        {
            List<double[]> points = ClassPoints(db);
            double target = shown - 0.5;
            double lastX = 0, lastY = 100;
            foreach (double[] p in points)
            {
                if (target <= p[1])
                {
                    return lastX + ((p[0] - lastX) * (target - lastY) / Math.Max(1e-9, p[1] - lastY));
                }
                lastX = p[0];
                lastY = p[1];
            }
            return lastX;
        }

        // FM4's CarClasses (MaxPerformanceIndex, MaxDisplayPerformanceIndex), for a database that has no such table (a DLC pack's merge database).
        private static readonly double[][] Fm4Classes = {
            new double[] { 0.157, 200 }, new double[] { 0.2224, 275 }, new double[] { 0.282493, 350 }, new double[] { 0.347, 425 }, new double[] { 0.4177, 500 },
            new double[] { 0.53, 600 }, new double[] { 0.6233, 700 }, new double[] { 0.7245, 800 }, new double[] { 0.796, 875 }, new double[] { 0.938, 998 }, new double[] { 1, 999 } };
        private static readonly string[] Fm4ClassNames = { "F", "E", "D", "C", "B", "A", "S", "R3", "R2", "R1", "U" };

        private static List<double[]> ClassPoints(SqliteDatabase db)
        {
            List<double[]> points = new List<double[]>();
            if (DbIds.HasTable(db, "CarClasses"))
            {
                foreach (object[] row in db.Query("SELECT MaxPerformanceIndex, MaxDisplayPerformanceIndex FROM CarClasses ORDER BY MaxPerformanceIndex").Rows)
                {
                    points.Add(new double[] { Real(row[0]), Real(row[1]) });
                }
            }
            if (points.Count == 0)
            {
                points.AddRange(Fm4Classes);
            }
            return points;
        }

        // The class id (CarClasses.Id: F 0, E 1 ... R1 9, U 10) of a stored PI, and its letter.
        public static int ClassOf(SqliteDatabase db, double pi)
        {
            List<double[]> points = ClassPoints(db);
            for (int i = 0; i < points.Count; i++)
            {
                if (pi <= points[i][0])
                {
                    return i;
                }
            }
            return points.Count - 1;
        }

        public static string ClassLetter(int classId)
        {
            return (classId >= 0 && classId < Fm4ClassNames.Length) ? Fm4ClassNames[classId] : "?";
        }

        private static double TorqueAt(double[] torque, double maxRpm, double redline, double rpm)
        {
            int count = torque.Length;
            double step = maxRpm / (count - 1);
            double position = rpm / step;
            if (position <= 0)
            {
                return torque[0];
            }
            if (position >= count - 1)
            {
                return torque[count - 1];
            }
            int i = (int) Math.Floor(position);
            double t = position - i;
            return torque[i] + ((torque[i + 1] - torque[i]) * t);
        }
    }
}
