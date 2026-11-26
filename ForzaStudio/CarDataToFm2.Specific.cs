namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    // FM2 columns the source does not have but that follow from the converted data (approved 2026-11-23, checked on FM2's
    // own cars with E:\DEV\fm4dev\PortToFM2\gearchk.exe):
    //   - Bore-m / Stroke-m: FM2 keeps pi/4 x bore^2 x stroke x cylinders = Disp_cc (median 1.000, 80 % within 1 %), and
    //     bore / stroke = 1.054 (median of 313 engines): both from the displacement and List_Cylinders.Number. Rotary
    //     engines (Rotors > 0) keep FM2's typical values.
    //   - SpeedInGear1..7-mph: RedlineRPM of the stock camshaft / 60 x 2 pi x the driven tire's radius / (GearRatio[n] x
    //     FinalDriveRatio) in mph, GearRatio0 being reverse (FM2: 755 of 1702 gears within 2 mph, median error 2.9 mph,
    //     against 13-40 mph of the typical value: FM2's own come partly from spec sheets). Gears the car lacks: 0.
    public partial class CarDataToFm2
    {
        private const double Fm2BoreStroke = 1.054;

        private void BoreAndStroke(Dictionary<string, object> engine)
        {
            double disp = D(Get(engine, "Disp_cc"));
            SqliteDatabase.Result cyl = this.Target.Query("SELECT Number, Rotors FROM List_Cylinders WHERE CylinderID = ?", Get(engine, "CylinderID"));
            if (disp <= 0 || cyl.Rows.Count == 0 || D(cyl.Rows[0][1]) > 0 || D(cyl.Rows[0][0]) <= 0)
            {
                this.Note("Data_Engine", "Bore-m / Stroke-m", "stock", "rotary or no cylinder count: FM2's typical values");
                return;
            }
            double perCylinder = disp / 1e6 / D(cyl.Rows[0][0]);       // m^3
            double bore = Math.Pow(4 * perCylinder * Fm2BoreStroke / Math.PI, 1.0 / 3.0);
            engine["Bore-m"] = Math.Round(bore, 4);
            engine["Stroke-m"] = Math.Round(bore / Fm2BoreStroke, 4);
            this.Note("Data_Engine", "Bore-m / Stroke-m", "stock", string.Format(CultureInfo.InvariantCulture, "from Disp_cc and {0} cylinders, bore / stroke {1} (FM2's median)", D(cyl.Rows[0][0]), Fm2BoreStroke));
        }

        // After the transmission, the engine and Data_Car are written.
        private void SpeedInGears()
        {
            SqliteDatabase.Result t = this.Target.Query("SELECT NumGears, FinalDriveRatio, GearRatio0, GearRatio1, GearRatio2, GearRatio3, GearRatio4, GearRatio5, GearRatio6, GearRatio7 FROM List_UpgradeTransmission WHERE Ordinal = ? AND IsStock = 1", this.Car);
            object redline = this.Target.Scalar("SELECT v.RedlineRPM FROM List_UpgradeEngine e JOIN List_UpgradeEngineCamValves v ON v.EngineID = e.EngineID AND v.IsStock = 1 WHERE e.Ordinal = ? AND e.IsStock = 1", this.Car);
            bool front = this.portGroup != null && this.portGroup.StartsWith("1|", StringComparison.Ordinal);
            string side = front ? "Front" : "Rear";
            SqliteDatabase.Result tire = this.Target.Query("SELECT \"StockTire-" + side + "Width-mm\", \"StockTire-" + side + "Aspect-pct\", \"StockTire-" + side + "Wheel-in\" FROM Data_Car WHERE Id = ?", this.Car);
            if (t.Rows.Count == 0 || redline == null || tire.Rows.Count == 0)
            {
                this.Note("Data_Car", "SpeedInGear*", "stock", "no stock transmission or camshaft: FM2's typical values stay");
                return;
            }
            double radius = (D(tire.Rows[0][2]) * 0.0254 / 2) + (D(tire.Rows[0][0]) / 1000.0 * D(tire.Rows[0][1]) / 100.0);
            double rpm = D(redline), final = D(t.Rows[0][1]);
            int gears = (int) D(t.Rows[0][0]) - 1;      // FM2's NumGears counts reverse (305 of 305 cars: last speed gear = NumGears - 1)
            List<string> sets = new List<string>();
            List<object> values = new List<object>();
            for (int g = 1; g <= 7; g++)
            {
                if (!HasColumn("Data_Car", "SpeedInGear" + g + "-mph"))
                {
                    continue;
                }
                double ratio = D(t.Rows[0][2 + g]);
                double mph = (g <= gears && ratio > 0 && final > 0) ? rpm / 60.0 * 2 * Math.PI * radius / (ratio * final) * 2.23694 : 0;
                sets.Add("\"SpeedInGear" + g + "-mph\" = ?");
                values.Add(Math.Round(mph));
            }
            values.Add(this.Car);
            this.Target.Execute("UPDATE Data_Car SET " + string.Join(", ", sets.ToArray()) + " WHERE Id = ?", values.ToArray());
            this.Note("Data_Car", "SpeedInGear*", "stock", string.Format(CultureInfo.InvariantCulture, "computed: redline {0} rpm, {1} tire radius {2:0.000} m, final {3}, {4} forward gears", rpm, side.ToLowerInvariant(), radius, final, gears));
        }

        // List_UpgradeEngineFuelSystem.Carbureted from the source engine's Data_Engine.Carbureted (FM4 holds it per engine).
        // FM2: a carburetted stock engine keeps carburettors on fuel system levels 1-2 (30 of 30 rows) and gets injection on
        // level 3 (21 of 21); an injected one is 0 on every level (460 of 460).
        private void Carburetion(long engine, long sourceEngine)
        {
            if (!Array.Exists(Columns(this.Source, "Data_Engine"), delegate(string c) { return string.Equals(c, "Carbureted", StringComparison.OrdinalIgnoreCase); }))
            {
                return;     // the source game does not say: FM2's typical value stays (CarDataToFm2.Typical.cs)
            }
            long carb = (long) Math.Round(D(this.Source.Scalar("SELECT Carbureted FROM Data_Engine WHERE EngineID = ?", sourceEngine)));
            this.Target.Execute("UPDATE List_UpgradeEngineFuelSystem SET Carbureted = CASE WHEN IsStock = 0 AND Level >= 3 THEN 0 ELSE ? END WHERE EngineID = ?", carb, engine);
            this.Note("List_UpgradeEngineFuelSystem", "Carbureted", "all", "source engine's Carbureted " + carb + (carb != 0 ? " (FM2: level 3 injection)" : ""));
        }

        // FM2's stock engine row names the car's make as its maker (List_UpgradeEngine.ManufacturerID = Data_Car.MakeID on
        // 310 of 310 cars). After WriteCar, so the make is the converted one.
        private void EngineMaker()
        {
            int n = this.Target.Execute("UPDATE List_UpgradeEngine SET ManufacturerID = (SELECT MakeID FROM Data_Car WHERE Id = ?) WHERE Ordinal = ? AND IsStock = 1", this.Car, this.Car);
            this.Note("List_UpgradeEngine", "ManufacturerID", "stock", "the car's MakeID (FM2: 310 of 310 cars)" + (n == 0 ? " - no stock row" : ""));
        }

        // Hood camera (Data_Car.CamHoodHeightOffset / CamHoodZOffset) from the FM2 model just written (Model), fitted on 228
        // FM2 cars (PortToFM2\hoodcam.exe, leave one car out):
        //   height = -0.322427 + 0.684925 x hood top (hooda / hood, LOD 1): mean error 0.039 m against 0.053 of FM2's median;
        //   z = -0.1151 + 0.035471 wheel z + 0.085601 seat z + 0.312733 hood front - 0.027513 hood back - 1.060102 windscreen
        //       front (glassF min z): 0.143 against 0.195. Held within FM2's median +- 3 spreads.
        // No model, or no hood / windscreen / body part: FM2's typical values stay.
        public Fm2CarbinModel Model;

        private void HoodCamera()
        {
            if (this.Model == null || !HasColumn("Data_Car", "CamHoodHeightOffset") || !HasColumn("Data_Car", "CamHoodZOffset"))
            {
                return;
            }
            double[] hood = this.PartBox(delegate(string n) { return string.Equals(n, "hooda", StringComparison.OrdinalIgnoreCase) || string.Equals(n, "hood", StringComparison.OrdinalIgnoreCase); });
            double[] glass = this.PartBox(delegate(string n) { return string.Equals(n, "glassF", StringComparison.OrdinalIgnoreCase); });
            if (hood == null || glass == null || this.Model.DriverAnchors == null || this.Model.DriverAnchors.Length < 96)
            {
                this.Note("Data_Car", "CamHood*", "stock", "no hood / windscreen part or driver anchors: FM2's typical values stay");
                return;
            }
            double wheelZ = BigFloat(this.Model.DriverAnchors, 8), seatZ = BigFloat(this.Model.DriverAnchors, 64 + 8);
            double height = -0.322427 + (0.684925 * hood[4]);
            double z = -0.1151 + (0.035471 * wheelZ) + (0.085601 * seatZ) + (0.312733 * hood[2]) - (0.027513 * hood[5]) - (1.060102 * glass[2]);
            height = Math.Max(-0.02, Math.Min(0.38, height));
            z = Math.Max(-0.62, Math.Min(1.06, z));
            this.Target.Execute("UPDATE Data_Car SET CamHoodHeightOffset = ?, CamHoodZOffset = ? WHERE Id = ?", Math.Round(height, 3), Math.Round(z, 3), this.Car);
            this.Note("Data_Car", "CamHood*", "stock", string.Format(CultureInfo.InvariantCulture, "from the model: hood top {0:0.000}, hood front {1:0.000}, windscreen front {2:0.000} -> height {3:0.000}, z {4:0.000}", hood[4], hood[2], glass[2], height, z));
        }

        // min x y z, max x y z of the LOD 1 vertices of the model's parts the test picks, or null.
        private double[] PartBox(Predicate<string> pick)
        {
            double[] box = { double.MaxValue, double.MaxValue, double.MaxValue, double.MinValue, double.MinValue, double.MinValue };
            bool any = false;
            foreach (Fm2CarbinModel.Part part in this.Model.Parts)
            {
                if (!pick(part.Name))
                {
                    continue;
                }
                double[] positions = this.Model.Positions(part);
                int count = positions.Length / 3;
                foreach (Fm2CarbinModel.Mesh mesh in part.Meshes)
                {
                    if (mesh.Lod != 1)
                    {
                        continue;
                    }
                    foreach (int i in mesh.Indices)
                    {
                        if (i < 0 || i >= count)
                        {
                            continue;
                        }
                        for (int k = 0; k < 3; k++)
                        {
                            box[k] = Math.Min(box[k], positions[(i * 3) + k]);
                            box[3 + k] = Math.Max(box[3 + k], positions[(i * 3) + k]);
                        }
                        any = true;
                    }
                }
            }
            return any ? box : null;
        }

        private static double BigFloat(byte[] d, int o)
        {
            return BitConverter.ToSingle(new byte[] { d[o + 3], d[o + 2], d[o + 1], d[o] }, 0);
        }

        // FM2's zero-throttle torque curve shape (user 2026-11-23: the last template data left; approved "2"): the median
        // over FM2's 321 stock zero-throttle curves, each resampled to 151 points over 0..TorqueCurveMaxRPM without its end
        // marker (PortToFM2\zeroshape.exe; leave one engine out: mean error 0.064 against 0.091 of another engine's shape,
        // which is what the template gave). Used when the source has no zero-throttle curve of its own (FM4 / FH: only
        // ZeroThrottleTorqueScale); the scale: the source's, else FM2's median 161.964.
        private const double Fm2ZeroScale = 161.964;

        private static readonly double[] Fm2ZeroShape =
        {
            0.1000, 0.0944, 0.0888, 0.0832, 0.0776, 0.0720, 0.0664, 0.0608, 0.0552, 0.0495, 0.0431, 0.0354, 0.0246, 0.0138, 0.0031, -0.0082,
            -0.0196, -0.0298, -0.0400, -0.0508, -0.0604, -0.0697, -0.0800, -0.0896, -0.0994, -0.1091, -0.1186, -0.1275, -0.1377, -0.1484,
            -0.1581, -0.1676, -0.1772, -0.1871, -0.1971, -0.2071, -0.2171, -0.2267, -0.2360, -0.2459, -0.2556, -0.2647, -0.2739, -0.2831,
            -0.2922, -0.3019, -0.3121, -0.3222, -0.3323, -0.3424, -0.3526, -0.3627, -0.3728, -0.3829, -0.3931, -0.4032, -0.4133, -0.4231,
            -0.4326, -0.4420, -0.4514, -0.4609, -0.4706, -0.4808, -0.4911, -0.5014, -0.5117, -0.5219, -0.5322, -0.5422, -0.5518, -0.5614,
            -0.5711, -0.5807, -0.5904, -0.6000, -0.6096, -0.6193, -0.6289, -0.6386, -0.6482, -0.6578, -0.6675, -0.6771, -0.6868, -0.6964,
            -0.7061, -0.7157, -0.7253, -0.7350, -0.7446, -0.7543, -0.7639, -0.7735, -0.7832, -0.7930, -0.8028, -0.8126, -0.8225, -0.8323,
            -0.8421, -0.8519, -0.8618, -0.8716, -0.8814, -0.8912, -0.9011, -0.9109, -0.9207, -0.9305, -0.9404, -0.9502, -0.9600, -0.9698,
            -0.9796, -0.9895, -0.9993, -1.0000, -1.0000, -1.0000, -1.0000, -1.0000, -1.0000, -1.0000, -1.0000, -1.0000, -1.0609, -1.1217,
            -1.1826, -1.2434, -1.3043, -1.3651, -1.4260, -1.4868, -1.5477, -1.6085, -1.6694, -1.7302, -1.7911, -1.8519, -1.9128, -1.9737,
            -2.0345, -2.0954, -2.1562, -2.2171, -2.2779, -2.3388, -2.3996, -2.4605, -2.5213,
        };

        // The shape over 'count' points of the new curve (the last one the end marker, like every FM2 curve).
        private static double[] ZeroShape(int count)
        {
            double[] withMarker = new double[Fm2ZeroShape.Length + 1];
            Array.Copy(Fm2ZeroShape, withMarker, Fm2ZeroShape.Length);
            withMarker[Fm2ZeroShape.Length] = -2.63;
            return Resample(withMarker, 1.0, count, 1.0);
        }

        private bool HasColumn(string table, string column)
        {
            return Array.Exists(Columns(this.Target, table), delegate(string c) { return string.Equals(c, column, StringComparison.OrdinalIgnoreCase); });
        }

        // The stock engine's MediaName is the car's (FM2: 310 of 310 cars): it names the engine sound file,
        // DSPTuning.zip\<MediaName>_DSPTuning.xml (Fm2SoundPort writes it from the source game's sound). Duplicate Car
        // left the template's name, so the ported car played the template's engine.
        private void EngineMedia()
        {
            int n = this.Target.Execute("UPDATE Data_Engine SET MediaName = (SELECT MediaName FROM Data_Car WHERE Id = ?) WHERE EngineID IN (SELECT EngineID FROM List_UpgradeEngine WHERE Ordinal = ? AND IsStock = 1)", this.Car, this.Car);
            this.Note("Data_Engine", "MediaName", "stock", "the car's MediaName (FM2: 310 of 310 cars)" + (n == 0 ? " - no stock engine" : ""));
        }

        // Brake pistons and sizes inside what FM2 draws (user 2026-11-24: the Q7 V12 TDI had no front calipers). FM2's
        // List_UpgradeBrakes holds Front/RearCaliperPistons 2, 4 or 6 only (895 of 895 rows) and Front/RearBrakeSize 8-15;
        // FM4's Q7 has 8 pistons and size 16 in front, and FM2 drew no caliper there (the rear, 4 / 14, was fine). Every
        // row of the car: pistons -> FM2's nearest value (the higher on a tie), size held within FM2's range. The values
        // come from the FM2 database's other cars.
        private void BrakeRange()
        {
            List<double> pistons = new List<double>();
            foreach (object[] r in this.Target.Query("SELECT DISTINCT FrontCaliperPistons FROM List_UpgradeBrakes WHERE Ordinal <> ? UNION SELECT DISTINCT RearCaliperPistons FROM List_UpgradeBrakes WHERE Ordinal <> ?", this.Car, this.Car).Rows)
            {
                pistons.Add(D(r[0]));
            }
            object[] range = this.Target.Query("SELECT MIN(MIN(FrontBrakeSize, RearBrakeSize)), MAX(MAX(FrontBrakeSize, RearBrakeSize)) FROM List_UpgradeBrakes WHERE Ordinal <> ?", this.Car).Rows[0];
            if (pistons.Count == 0 || range[0] == null || range[0] is DBNull)
            {
                return;
            }
            double low = D(range[0]), high = D(range[1]);
            List<string> changed = new List<string>();
            foreach (object[] r in this.Target.Query("SELECT Id, Level, FrontCaliperPistons, RearCaliperPistons, FrontBrakeSize, RearBrakeSize FROM List_UpgradeBrakes WHERE Ordinal = ?", this.Car).Rows)
            {
                double[] now = { D(r[2]), D(r[3]), D(r[4]), D(r[5]) };
                double[] fit = { Nearest(pistons, now[0]), Nearest(pistons, now[1]), Math.Max(low, Math.Min(high, now[2])), Math.Max(low, Math.Min(high, now[3])) };
                if (now[0] == fit[0] && now[1] == fit[1] && now[2] == fit[2] && now[3] == fit[3])
                {
                    continue;
                }
                this.Target.Execute("UPDATE List_UpgradeBrakes SET FrontCaliperPistons = ?, RearCaliperPistons = ?, FrontBrakeSize = ?, RearBrakeSize = ? WHERE Id = ?", (long) fit[0], (long) fit[1], (long) fit[2], (long) fit[3], r[0]);
                changed.Add(string.Format(CultureInfo.InvariantCulture, "level {0}: pistons {1}/{2} -> {3}/{4}, size {5}/{6} -> {7}/{8}", r[1], now[0], now[1], fit[0], fit[1], now[2], now[3], fit[2], fit[3]));
            }
            this.Note("List_UpgradeBrakes", "Caliper pistons / brake size", "all", changed.Count == 0 ? "within FM2's values" : string.Join("; ", changed.ToArray()) + string.Format(CultureInfo.InvariantCulture, " (FM2: pistons {0}, size {1}-{2})", string.Join("/", pistons.OrderBy(delegate(double p) { return p; }).Select(delegate(double p) { return p.ToString(CultureInfo.InvariantCulture); }).ToArray()), low, high));
        }

        private static double Nearest(List<double> values, double v)
        {
            double best = values[0];
            foreach (double x in values)
            {
                if (Math.Abs(x - v) < Math.Abs(best - v) || (Math.Abs(x - v) == Math.Abs(best - v) && x > best))
                {
                    best = x;
                }
            }
            return best;
        }

        // Factory colours (Combo_Colors): the source car's rows replace the template's (user 2026-11-23: "i colori del gioco
        // source non vengono portati"). FM2, FM3, FM4, FH1 and FH2 store RGB the same way (the same car holds the same
        // numbers in FM2 and FM4: F430, Supra RZ, R34). Sequence, Metallic, RGB and the two texture names are kept; FM4's
        // ID / ColorClassId and Combo_Colors_Exceptions (second colour, stripes) have no FM2 column.
        private void Colors()
        {
            if (!HasTable(this.Source, "Combo_Colors"))
            {
                this.Note("Combo_Colors", "*", "all", "the source game has no Combo_Colors: the template's colours stay");
                return;
            }
            List<Dictionary<string, object>> rows = Rows(this.Source, "SELECT Sequence, Metallic, RGB, BaseTexture, DamageTexture FROM Combo_Colors WHERE Ordinal = ? ORDER BY Sequence", this.SourceCar);
            if (rows.Count == 0)
            {
                this.Note("Combo_Colors", "*", "all", "the source car has no colours: the template's colours stay");
                return;
            }
            this.Target.Execute("DELETE FROM Combo_Colors WHERE Ordinal = ?", this.Car);
            foreach (Dictionary<string, object> r in rows)
            {
                this.Target.Execute("INSERT INTO Combo_Colors (Ordinal, Sequence, Metallic, RGB, BaseTexture, DamageTexture) VALUES (?, ?, ?, ?, ?, ?)",
                    this.Car, r["Sequence"], r["Metallic"], r["RGB"], (r["BaseTexture"] as string) ?? "nodamage.tga", (r["DamageTexture"] as string) ?? "damage.tga");
            }
            this.Note("Combo_Colors", "*", "all", rows.Count + " colours from the source car");
        }

        // The stock wheel row (List_Wheels) Duplicate Car copied from the template: its Mass is the source wheel's. Only a
        // wheel no other FM2 car uses as its stock wheel (the wheel port gives the car its own).
        // IconPath: FM2's is ...\Licensed_Rims\<the wheel's own MediaName>.tga on 373 of 373 wheels, and a picture exists for 1
        // of its 282 stock wheels (E:\DEV\fm4dev\PortToFM2\shtest\wh2.cs, 2026-11-26): the path is made from the wheel's own
        // name, so the template rim's picture path does not stay (user rule: nothing of the template).
        private void WheelMass()
        {
            object ownWheel = this.Target.Scalar("SELECT StockWheelID FROM Data_Car WHERE Id = ?", this.Car);
            if (ownWheel != null && Convert.ToInt64(this.Target.Scalar("SELECT COUNT(*) FROM Data_Car WHERE StockWheelID = ?", ownWheel), CultureInfo.InvariantCulture) == 1)
            {
                object media = this.Target.Scalar("SELECT MediaName FROM List_Wheels WHERE ID = ?", ownWheel);
                object icon = this.Target.Scalar("SELECT IconPath FROM List_Wheels WHERE ID = ?", ownWheel);
                if (media is string && icon is string && ((string) icon).IndexOf('\\') >= 0)
                {
                    string path = ((string) icon).Substring(0, ((string) icon).LastIndexOf('\\') + 1) + (string) media + ".tga";
                    this.Target.Execute("UPDATE List_Wheels SET IconPath = ? WHERE ID = ?", path, ownWheel);
                    this.Note("List_Wheels", "IconPath", "stock", "the wheel's own name, as FM2's 373 wheels: " + path);
                }
            }
            object fm2Wheel = this.Target.Scalar("SELECT StockWheelID FROM Data_Car WHERE Id = ?", this.Car);
            object mass = this.Source.Scalar("SELECT w.Mass FROM Data_Car c JOIN List_Wheels w ON w.ID = c.StockWheelID WHERE c.Id = ?", this.SourceCar);
            if (fm2Wheel == null || mass == null || mass is DBNull
                || Convert.ToInt64(this.Target.Scalar("SELECT COUNT(*) FROM Data_Car WHERE StockWheelID = ?", fm2Wheel), CultureInfo.InvariantCulture) != 1)
            {
                this.Note("List_Wheels", "Mass", "stock", "no own stock wheel or no source mass: the template's stays");
                return;
            }
            this.Target.Execute("UPDATE List_Wheels SET Mass = ? WHERE ID = ?", D(mass), fm2Wheel);
            this.Note("List_Wheels", "Mass", "stock", string.Format(CultureInfo.InvariantCulture, "source wheel's {0}", D(mass)));
        }
    }
}
