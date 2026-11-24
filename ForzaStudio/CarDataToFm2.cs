namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    // Port INTO FM2, step 2: the car's database rows (opened 2026-11-22; study in E:\DEV\fm4dev\PortToFM2: shared.cs,
    // cols.cs, schema.cs). The FM2 car exists already (a Duplicate Car copy of the FM2 template car, step 1 put the model in
    // it); this rewrites its rows from the source car (FM4 / FM3 / FH: the FM4 table layout).
    //
    // Every row is converted on its own (user 2026-11-22: "i calcoli sono tutti da fare con pezzo singolo"): one FM2 row per
    // source row (each upgrade part, each level), each value from the source row's value by the rule of ITS table, column and
    // level. The rules are learned at run time from the cars BOTH games have (same MediaName; FM2 / FM4: 188 cars), on the rows
    // of the same level (and the same PartsStringId for body kit parts):
    //   copy    the two games store the same value (80 % of the pairs within 0.5 %);
    //   const   FM2 stores one value whatever the source has (90 %: a game constant, e.g. damper clamps 30 against 10);
    //   offset  FM2 = source + d (80 % of the pairs);
    //   ratio   FM2 = source x the median FM2 / source ratio of the pairs;
    //   map     ids (columns named ...ID): the FM2 id the pairs show most for the source id;
    //   keep    no rule (too few pairs, or no source column): the FM2 template car's value of that row.
    // Rows are matched to the template's rows by level for the columns only FM2 has.
    //
    // Structure that differs (study 2026-11-22):
    //   - FM4 splits front / rear tables (tire width, rim size, anti-roll bars): one FM2 row per level, the two merged;
    //   - drivetrain parts are keyed by the car in FM2, by the drivetrain in FM4; the flywheel by the car in FM2;
    //   - the engine: an own FM2 engine (CarEngine.CloneFm2 of the template's, then every table rewritten). FM2 and FM4 build
    //     the torque the same way (curve x (1 + sum of the stock parts' TorqueScale - 1), turbo by the previous point's power:
    //     Fm2Perf / Fm4Perf), so camshaft -> CamValves with its own curves, Displacement -> Block, TurboSingle / Twin / Quad ->
    //     Turbo; the stock TorqueScale of the parts FM2 lacks (valves, pistons, manifold, oil cooling, restrictor) goes into
    //     the stock Block row so the stock torque stays the source's; their upgrade levels are lost (reported). Engine swaps
    //     are not ported (reported). Zero-throttle curve: the source's own (FM3) or the template's shape with the source's
    //     ZeroThrottleTorqueScale (FM4 / FH);
    //   - physics rows (springs, anti-roll bars, aero) are converted the same way into new counters; an aero row FM2 shares
    //     between most cars at that level (bumper level 1...) is taken as shared.
    // Not here: PI / class / ratings / Sim peaks (Fm2Perf after this), the sound, names, the thumbnail.
    public partial class CarDataToFm2
    {
        public SqliteDatabase Target;           // FM2 database with the copy
        public SqliteDatabase Source;           // the source game's database
        public long Car;                        // the copy's Id in FM2
        public long SourceCar;                  // the car's Id in the source game
        public List<string> Log = new List<string>();
        public List<string> RuleLog = new List<string>();     // every rule used, once
        public string LearnExclude;             // a MediaName left out of the learning (checks: the car converted is not its own teacher)
        public ICollection<string> ModelParts;  // the FM2 model's part names: a body kit upgrade whose part the model lacks is not ported (null: no check)

        // Body kit tables: the model part group, the stock row's PartsStringId (letter a) and the race part's id
        // (E:\DEV\fm4dev\PortToFM2\aeromap.cs on every FM2 and FM4 car: id = stock id + letter - 'a', the same in both games).
        private static readonly object[][] KitParts = new object[][]
        {
            new object[] { "List_UpgradeFrontBumper", "bumperF", 1, 27 },
            new object[] { "List_UpgradeRearBumper", "bumperR", 28, 54 },
            new object[] { "List_UpgradeSideSkirt", "skirtL", 82, -1 },
            new object[] { "List_UpgradeHood", "hood", 108, -1 },
            new object[] { "List_UpgradeRearWing", "wing", 55, 81 },
        };

        // The model part a body kit row shows, or null.
        private static string KitPart(string table, double partsStringId)
        {
            foreach (object[] kit in KitParts)
            {
                if (string.Equals((string) kit[0], table, StringComparison.OrdinalIgnoreCase))
                {
                    int id = (int) partsStringId, stock = (int) kit[2], race = (int) kit[3];
                    if (id == race)
                    {
                        return (string) kit[1] + "race";
                    }
                    return (id >= stock && id - stock < 26) ? (string) kit[1] + (char) ('a' + (id - stock)) : null;
                }
            }
            return null;
        }

        private const ForzaGame Game = ForzaGame.Motorsport2;
        private const int MinPairs = 12;
        private readonly Dictionary<string, List<object[]>> samples = new Dictionary<string, List<object[]>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Rule> rules = new Dictionary<string, Rule>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<long, int>> sharedAero = new Dictionary<string, Dictionary<long, int>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> logged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ---- the table pairs ----

        private sealed class Pair
        {
            public string Table;                // FM2 table
            public string Key;                  // FM2 key: "car" (Ordinal) or "engine" (EngineID)
            public string SourceKey;            // "body" (CarBodyID = car x 1000), "car" (Ordinal), "drive", "engine"
            public bool Parts;                  // body kit: rows matched by PartsStringId too
            public bool Alternatives;           // the first source table with rows (turbo)
            public string[][] Sources;          // { table, "column>renamed;..." }

            public Pair(string table, string key, string sourceKey, bool parts, params string[][] sources)
            {
                this.Table = table;
                this.Key = key;
                this.SourceKey = sourceKey;
                this.Parts = parts;
                this.Sources = sources;
            }
        }

        private static string[] S(string table)
        {
            return new string[] { table, "" };
        }

        private static string[] S(string table, string renames)
        {
            return new string[] { table, renames };
        }

        private static readonly Pair[] CarPairs = new Pair[]
        {
            new Pair("List_UpgradeFrontBumper", "car", "body", true, S("List_UpgradeCarBodyFrontBumper")),
            new Pair("List_UpgradeRearBumper", "car", "body", true, S("List_UpgradeCarBodyRearBumper")),
            new Pair("List_UpgradeSideSkirt", "car", "body", true, S("List_UpgradeCarBodySideSkirt")),
            new Pair("List_UpgradeHood", "car", "body", true, S("List_UpgradeCarBodyHood")),
            new Pair("List_UpgradeRearWing", "car", "car", true, S("List_UpgradeRearWing")),
            new Pair("List_UpgradeWeight", "car", "body", false, S("List_UpgradeCarBodyWeight")),
            new Pair("List_UpgradeBrakes", "car", "car", false, S("List_UpgradeBrakes")),
            new Pair("List_UpgradeTireCompound", "car", "car", false, S("List_UpgradeTireCompound")),
            new Pair("List_UpgradeTireWidth", "car", "body", false, S("List_UpgradeCarBodyTireWidthFront"), S("List_UpgradeCarBodyTireWidthRear")),
            new Pair("List_UpgradeRimSize", "car", "car", false, S("List_UpgradeRimSizeFront"), S("List_UpgradeRimSizeRear")),
            new Pair("List_UpgradeAntiSway", "car", "car", false, S("List_UpgradeAntiSwayFront", "AntiSwayPhysicsID>FrontAntiSwayPhysicsID"), S("List_UpgradeAntiSwayRear", "AntiSwayPhysicsID>RearAntiSwayPhysicsID")),
            new Pair("List_UpgradeSpringDamper", "car", "car", false, S("List_UpgradeSpringDamper")),
            new Pair("List_UpgradeTransmission", "car", "drive", false, S("List_UpgradeDrivetrainTransmission")),
            new Pair("List_UpgradeClutch", "car", "drive", false, S("List_UpgradeDrivetrainClutch")),
            new Pair("List_UpgradeDifferential", "car", "drive", false, S("List_UpgradeDrivetrainDifferential")),
            new Pair("List_UpgradeDriveline", "car", "drive", false, S("List_UpgradeDrivetrainDriveline")),
            new Pair("List_UpgradeFlywheel", "car", "engine", false, S("List_UpgradeEngineFlywheel")),
        };

        private static readonly Pair[] EnginePairs = new Pair[]
        {
            new Pair("List_UpgradeEngineCamValves", "engine", "engine", false, S("List_UpgradeEngineCamshaft")),
            new Pair("List_UpgradeEngineBlock", "engine", "engine", false, S("List_UpgradeEngineDisplacement")),
            new Pair("List_UpgradeEngineIntake", "engine", "engine", false, S("List_UpgradeEngineIntake")),
            new Pair("List_UpgradeEngineExhaust", "engine", "engine", false, S("List_UpgradeEngineExhaust")),
            new Pair("List_UpgradeEngineFuelSystem", "engine", "engine", false, S("List_UpgradeEngineFuelSystem")),
            new Pair("List_UpgradeEngineIgnition", "engine", "engine", false, S("List_UpgradeEngineIgnition")),
            new Pair("List_UpgradeEngineIntercooler", "engine", "engine", false, S("List_UpgradeEngineIntercooler")),
            new Pair("List_UpgradeEngineCSC", "engine", "engine", false, S("List_UpgradeEngineCSC")),
            new Pair("List_UpgradeEngineDSC", "engine", "engine", false, S("List_UpgradeEngineDSC")),
            new Pair("List_UpgradeEngineTurbo", "engine", "engine", false, S("List_UpgradeEngineTurboSingle"), S("List_UpgradeEngineTurboTwin"), S("List_UpgradeEngineTurboQuad")),
        };

        // Source engine tables FM2 has none of: their stock TorqueScale goes into the stock Block row.
        private static readonly string[] SourceOnlyTorqueTables = new string[]
        {
            "List_UpgradeEngineValves", "List_UpgradeEnginePistonsCompression", "List_UpgradeEngineManifold", "List_UpgradeEngineOilCooling", "List_UpgradeEngineRestrictorPlate",
        };

        // FM2 Data_Car column <- source column (Data_Car + Data_CarBody + the box measures), when the names differ.
        private static readonly Dictionary<string, string> CarRenames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "StockTire-FrontWidth-mm", "FrontTireWidthMM" }, { "StockTire-FrontAspect-pct", "FrontTireAspect" }, { "StockTire-FrontWheel-in", "FrontWheelDiameterIN" },
            { "StockTire-RearWidth-mm", "RearTireWidthMM" }, { "StockTire-RearAspect-pct", "RearTireAspect" }, { "StockTire-RearWheel-in", "RearWheelDiameterIN" },
            { "StockGears", "NumGears" }, { "FrontWeight-pct", "WeightDistribution" },
            { "ModelFrontStockRideHeight", "FrontStockRideHeight" }, { "ModelRearStockRideHeight", "RearStockRideHeight" },
            { "BottomCenterWheelbasePosz", "BottomCenterWheelbasePosZ" },
            { "Wheelbase-m", "ModelWheelbase" }, { "FrontTrack-m", "ModelFrontTrackOuter" }, { "RearTrack-m", "ModelRearTrackOuter" },
            { "Length-m", "BoxLength" }, { "Width-m", "BoxWidth" }, { "Height-m", "BoxHeight" },
            // 2026-11-23 (approved): FM2 spec columns against FM4's simulated values; the learner keeps a rule only when the
            // shared cars show one, else FM2's typical value (CarDataToFm2.Typical.cs)
            { "Dist:BrakeFrom60mph-m", "SimBrakeDistance60MPH" }, { "Dist:BrakeFrom100mph-m", "SimBrakeDistance100MPH" },
            { "LateralG", "SimLatGees60MPH" }, { "TurnsLockToLock", "SteerMaxAngle" }, { "DragCoefficient", "BodyAeroLongitudinalDrag" },
        };

        // Data_Car columns that are not converted here: keys, names, files, what Fm2Perf computes after.
        private static readonly string[] CarSkip = new string[]
        {
            "Id", "DisplayName", "ModelShort", "MediaName", "Thumbnail", "StockWheelID", "IsPurchased", "ContentId", "IsUnicorn", "HasLivery",
            "AIPlayerID", "PerformanceIndex", "ClassID", "SpeedRating", "AccelerationRating", "SimPeakPower", "SimPeakAngVel", "SimPeakTorque",
            "SimPeakTorqueAngVel", "SimRedlineAngVel", "UnlockID-Arcade", "IsDLC",
        };

        // ---- rules ----

        private sealed class Rule
        {
            public string Kind = "keep";
            public double A;
            public double ZeroTo = double.NaN;             // ratio: the FM2 value when the source has 0
            public Dictionary<string, object> Map;
            public bool Integer;
            public int Pairs;
            public Dictionary<string, double> Groups;       // group: FM2's value per (drive, car type) group, over the base rule
            public double BaseError = double.NaN, GroupError = double.NaN;

            public string Text()
            {
                if (this.Groups != null)
                {
                    return string.Format(CultureInfo.InvariantCulture, "group value ({0} groups, error {1:0.###} against {2:0.###} of {3})", this.Groups.Count, this.GroupError, this.BaseError, this.BaseText());
                }
                return this.BaseText();
            }

            public string BaseText()
            {
                switch (this.Kind)
                {
                    case "const": return "const " + Num(this.A);
                    case "offset": return "offset " + (this.A >= 0 ? "+" : "") + Num(this.A);
                    case "ratio": return "ratio x" + Num(this.A) + (double.IsNaN(this.ZeroTo) ? "" : " (0 -> " + Num(this.ZeroTo) + ")");
                    case "map": return "map (" + this.Map.Count + " ids)";
                }
                return this.Kind;
            }
        }

        private static string Num(double v)
        {
            return v.ToString("0.#####", CultureInfo.InvariantCulture);
        }

        private static bool IsId(string column)
        {
            return column.EndsWith("ID", StringComparison.Ordinal) || column.EndsWith("Id", StringComparison.Ordinal);
        }

        // A number, also when the table stores it as text (FM4's body kit tables hold PartsStringId as "27": read as 0
        // before 2026-11-22, which broke the body kit pairing and the model-part filter).
        private static bool Numeric(object o, out double v)
        {
            v = 0;
            if (o == null || o is DBNull || o is byte[])
            {
                return false;
            }
            if (o is string)
            {
                return double.TryParse((string) o, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            }
            v = Convert.ToDouble(o, CultureInfo.InvariantCulture);
            return true;
        }

        // A value in the storage type FM2 uses for that column (the template row's value): FM2 keeps some ids as text
        // (PartsStringId: 798 of 798 front bumper rows), numbers as numbers.
        private static object LikeTemplate(object value, object keep)
        {
            double v;
            if (keep is string && !(value is string) && Numeric(value, out v))
            {
                return (Math.Abs(v - Math.Round(v)) < 1e-12) ? ((long) Math.Round(v)).ToString(CultureInfo.InvariantCulture) : v.ToString("R", CultureInfo.InvariantCulture);
            }
            if ((keep is long || keep is int || keep is double) && value is string)
            {
                return AsNumber(value);
            }
            return value;
        }

        // A source value as a number when it is numeric text (whole numbers as long).
        private static object AsNumber(object o)
        {
            double v;
            if (o is string && Numeric(o, out v))
            {
                return (Math.Abs(v - Math.Round(v)) < 1e-12 && Math.Abs(v) < 9e15) ? (object) (long) Math.Round(v) : v;
            }
            return o;
        }

        private static string Key(string table, string column, string level)
        {
            return table + "|" + column + "|" + level;
        }

        // The group of the car being learned (FM2 drive type | car type) and of the car being ported (the source's).
        private string learningGroup, portGroup;
        private string fm2PortGroup;     // the same group in FM2's numbering (Powertrains.DrivetrainId | CarTypeID): FM2's typical values

        private void Sample(string table, string column, string level, object fm2, object source)
        {
            foreach (string cls in (level == "stock") ? new string[] { level, "all" } : new string[] { level, "upg", "all" })
            {
                string key = Key(table, column, cls);
                List<object[]> list;
                if (!this.samples.TryGetValue(key, out list))
                {
                    this.samples[key] = list = new List<object[]>();
                }
                list.Add(new object[] { fm2, source, this.learningGroup });
            }
        }

        // The value the base rule gives (no group), for the error comparison.
        private static double Predict(Rule rule, double source, double fallback)
        {
            switch (rule.Kind)
            {
                case "copy": return source;
                case "const": return rule.A;
                case "offset": return source + rule.A;
                case "ratio": return (Math.Abs(source) <= 1e-9) ? (double.IsNaN(rule.ZeroTo) ? 0 : rule.ZeroTo) : source * rule.A;
            }
            return fallback;
        }

        // Some FM2 columns are conventions of the game by kind of car, not a scale of the source's value (GameTorqueScale:
        // 0.9 front / rear drive road cars, 0.85 all-wheel drive, about 1 race cars; FM4 has 0.7-1.0 per car). When FM2's
        // typical value of the car's group (drive type, car type; 5 pairs or more) misses less than the base rule (70 % of its
        // mean error or less), the group's value is used; a car of a group without one gets the base rule.
        private static void TryGroups(Rule rule, List<object[]> list)
        {
            Dictionary<string, Dictionary<double, int>> byGroup = new Dictionary<string, Dictionary<double, int>>();
            foreach (object[] p in list)
            {
                double a;
                if (p[2] == null || !Numeric(p[0], out a))
                {
                    continue;
                }
                string g = (string) p[2];
                if (!byGroup.ContainsKey(g))
                {
                    byGroup[g] = new Dictionary<double, int>();
                }
                Count(byGroup[g], Math.Round(a, 6));
            }
            Dictionary<string, double> modes = new Dictionary<string, double>();
            foreach (KeyValuePair<string, Dictionary<double, int>> g in byGroup)
            {
                int n = 0;
                foreach (int c in g.Value.Values)
                {
                    n += c;
                }
                if (n >= 5)
                {
                    modes[g.Key] = Mode(g.Value);
                }
            }
            if (modes.Count == 0)
            {
                return;
            }
            double baseError = 0, groupError = 0;
            int count = 0;
            foreach (object[] p in list)
            {
                double a, b;
                if (!Numeric(p[0], out a) || !Numeric(p[1], out b))
                {
                    continue;
                }
                double scale = Math.Max(Math.Abs(a), 1e-6);
                double basePrediction = Predict(rule, b, b);
                double mode;
                double groupPrediction = (p[2] != null && modes.TryGetValue((string) p[2], out mode)) ? mode : basePrediction;
                baseError += Math.Abs(basePrediction - a) / scale;
                groupError += Math.Abs(groupPrediction - a) / scale;
                count++;
            }
            if (count == 0)
            {
                return;
            }
            rule.BaseError = baseError / count;
            rule.GroupError = groupError / count;
            if (rule.BaseError > 1e-9 && rule.GroupError <= 0.7 * rule.BaseError)
            {
                rule.Groups = modes;
            }
        }

        private Rule Learn(string key, string column)
        {
            Rule rule;
            if (this.rules.TryGetValue(key, out rule))
            {
                return rule;
            }
            rule = new Rule();
            this.rules[key] = rule;
            List<object[]> list;
            if (!this.samples.TryGetValue(key, out list) || list.Count < MinPairs)
            {
                rule.Pairs = (list == null) ? 0 : list.Count;
                return rule;
            }
            rule.Pairs = list.Count;
            List<double[]> pairs = new List<double[]>();
            bool integer = true;
            foreach (object[] p in list)
            {
                double a, b;
                if (Numeric(p[0], out a) && Numeric(p[1], out b))
                {
                    pairs.Add(new double[] { a, b });
                    integer &= p[0] is long || p[0] is int;
                }
            }
            if (pairs.Count < MinPairs)
            {
                return rule;
            }
            rule.Integer = integer;
            int equal = 0;
            Dictionary<double, int> fm2Values = new Dictionary<double, int>();
            Dictionary<double, int> offsets = new Dictionary<double, int>();
            List<double> ratios = new List<double>();
            List<double> zeroTo = new List<double>();
            foreach (double[] p in pairs)
            {
                if (Math.Abs(p[0] - p[1]) <= (0.005 * Math.Max(Math.Abs(p[0]), Math.Abs(p[1]))) + 1e-9)
                {
                    equal++;
                }
                Count(fm2Values, Math.Round(p[0], 6));
                Count(offsets, Math.Round(p[0] - p[1], 4));
                if (Math.Abs(p[1]) > 1e-9)
                {
                    ratios.Add(p[0] / p[1]);
                }
                else
                {
                    zeroTo.Add(p[0]);
                }
            }
            double mode = Mode(fm2Values), offset = Mode(offsets);
            if (IsId(column))
            {
                if (equal >= 0.9 * pairs.Count)
                {
                    rule.Kind = "copy";
                    return rule;
                }
                Dictionary<string, Dictionary<string, int>> votes = new Dictionary<string, Dictionary<string, int>>();
                foreach (double[] p in pairs)
                {
                    string from = Num(p[1]);
                    if (!votes.ContainsKey(from))
                    {
                        votes[from] = new Dictionary<string, int>();
                    }
                    Dictionary<string, int> v = votes[from];
                    v[Num(p[0])] = (v.ContainsKey(Num(p[0])) ? v[Num(p[0])] : 0) + 1;
                }
                rule.Kind = "map";
                rule.Map = new Dictionary<string, object>();
                foreach (KeyValuePair<string, Dictionary<string, int>> v in votes)
                {
                    string best = null;
                    int most = -1;
                    foreach (KeyValuePair<string, int> c in v.Value)
                    {
                        if (c.Value > most)
                        {
                            most = c.Value;
                            best = c.Key;
                        }
                    }
                    rule.Map[v.Key] = (long) double.Parse(best, CultureInfo.InvariantCulture);
                }
                return rule;
            }
            if (equal >= 0.8 * pairs.Count)
            {
                rule.Kind = "copy";
            }
            else if (fm2Values[Math.Round(mode, 6)] >= 0.9 * pairs.Count)
            {
                rule.Kind = "const";
                rule.A = mode;
            }
            else if (offsets[Math.Round(offset, 4)] >= 0.8 * pairs.Count)
            {
                rule.Kind = "offset";
                rule.A = offset;
            }
            else if (ratios.Count >= MinPairs)
            {
                ratios.Sort();
                rule.Kind = "ratio";
                rule.A = ratios[ratios.Count / 2];
                if (zeroTo.Count > 0)
                {
                    zeroTo.Sort();
                    rule.ZeroTo = zeroTo[zeroTo.Count / 2];
                }
            }
            if (rule.Kind != "const" && rule.Kind != "keep")
            {
                TryGroups(rule, list);
            }
            return rule;
        }

        private static void Count(Dictionary<double, int> d, double v)
        {
            d[v] = (d.ContainsKey(v) ? d[v] : 0) + 1;
        }

        private static double Mode(Dictionary<double, int> d)
        {
            double best = 0;
            int most = -1;
            foreach (KeyValuePair<double, int> kv in d)
            {
                if (kv.Value > most)
                {
                    most = kv.Value;
                    best = kv.Key;
                }
            }
            return best;
        }

        // The FM2 value of 'column' of a row of 'table' at 'level' from the source value; 'keep' = the template's.
        // A value the rules leave to the template ("the template's kept") where the template has no row either (FH1 pack
        // GTX1, 2026-11-24: turbo upgrades from maker 167, an id FM2 lacks, on a template engine without turbo rows): FM2's
        // typical value, never NULL (FM2 has no NULL there; the car crashed FM2 when driven).
        private object Convert2(string table, string column, string level, object source, object keep)
        {
            object value = this.Convert2Core(table, column, level, source, keep);
            if ((value == null || value is DBNull) && (keep == null || keep is DBNull))
            {
                return this.NoRule(table, column, level, keep, "no value and no template row");
            }
            return value;
        }

        private object Convert2Core(string table, string column, string level, object source, object keep)
        {
            Rule rule = null;
            string used = null;
            foreach (string cls in (level == "stock") ? new string[] { level, "all" } : new string[] { level, "upg", "all" })
            {
                Rule r = this.Learn(Key(table, column, cls), column);
                if (r.Kind != "keep")
                {
                    rule = r;
                    used = cls;
                    break;
                }
            }
            if (rule == null)
            {
                return this.NoRule(table, column, level, keep, "no rule from the source");
            }
            this.Note(table, column, level, rule.Text() + " [" + used + ", " + rule.Pairs + " pairs]");
            if (IsId(column))
            {
                object id = this.ConvertId(rule, source, keep);
                // an id no FM2 row of this table uses (a lookup FM2 lacks: FM3's FamilyModelID 26) keeps the template's
                if (id != null && !(id is DBNull) && HasTable(this.Target, table) && !Equals(id, keep)
                    && this.Target.Scalar("SELECT 1 FROM " + SqliteDatabase.Quote(table) + " WHERE " + SqliteDatabase.Quote(column) + " = ? LIMIT 1", id) == null)
                {
                    this.Note(table, column, level, "id " + id + " not used by any FM2 row: the template's kept");
                    return keep;
                }
                return id;
            }
            double v;
            if (rule.Kind == "map")
            {
                object mapped;
                if (Numeric(source, out v) && rule.Map.TryGetValue(Num(v), out mapped))
                {
                    return mapped;
                }
                return keep;
            }
            double grouped;
            if (rule.Groups != null && this.portGroup != null && rule.Groups.TryGetValue(this.portGroup, out grouped))
            {
                return Typed(grouped, rule.Integer, keep);
            }
            if (rule.Kind == "copy")
            {
                return (source == null || source is DBNull) ? keep : AsNumber(source);
            }
            if (rule.Kind == "const")
            {
                return Typed(rule.A, rule.Integer, keep);
            }
            if (!Numeric(source, out v))
            {
                return keep;
            }
            if (rule.Kind == "offset")
            {
                return Typed(v + rule.A, rule.Integer, keep);
            }
            if (Math.Abs(v) <= 1e-9)
            {
                return double.IsNaN(rule.ZeroTo) ? Typed(0, rule.Integer, keep) : Typed(rule.ZeroTo, rule.Integer, keep);
            }
            return Typed(v * rule.A, rule.Integer, keep);
        }

        private object ConvertId(Rule rule, object source, object keep)
        {
            double v;
            if (rule.Kind == "map")
            {
                object mapped;
                return (Numeric(source, out v) && rule.Map.TryGetValue(Num(v), out mapped)) ? mapped : keep;
            }
            if (rule.Kind == "copy")
            {
                return (source == null || source is DBNull) ? keep : AsNumber(source);
            }
            if (rule.Kind == "const")
            {
                return (long) Math.Round(rule.A);
            }
            return keep;
        }

        private static object Typed(double v, bool integer, object keep)
        {
            if (integer || keep is long)
            {
                return (long) Math.Round(v);
            }
            return Math.Round(v, 6);
        }

        private void Note(string table, string column, string level, string text)
        {
            string line = table + "." + column + " [" + level + "]: " + text;
            if (this.logged.Add(line))
            {
                this.RuleLog.Add(line);
            }
        }

        // ---- rows ----

        private static Dictionary<string, object> RowOf(SqliteDatabase.Result r, object[] row)
        {
            Dictionary<string, object> d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < r.Columns.Length; i++)
            {
                d[r.Columns[i]] = row[i];
            }
            return d;
        }

        private static List<Dictionary<string, object>> Rows(SqliteDatabase db, string sql, params object[] args)
        {
            List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
            SqliteDatabase.Result r = db.Query(sql, args);
            foreach (object[] row in r.Rows)
            {
                list.Add(RowOf(r, row));
            }
            return list;
        }

        private static object Get(Dictionary<string, object> row, string column)
        {
            object v;
            return (row != null && row.TryGetValue(column, out v)) ? v : null;
        }

        private static double D(object o)
        {
            double v;
            return Numeric(o, out v) ? v : 0;
        }

        private static bool HasTable(SqliteDatabase db, string table)
        {
            return DbIds.HasTable(db, table);
        }

        private static string Level(Dictionary<string, object> row)
        {
            return (D(Get(row, "IsStock")) == 1) ? "stock" : "L" + Num(D(Get(row, "Level")));
        }

        private static long StockDrivetrain(SqliteDatabase db, long car)
        {
            if (!HasTable(db, "List_UpgradeDrivetrain"))
            {
                return -1;
            }
            object id = db.Scalar("SELECT DrivetrainID FROM List_UpgradeDrivetrain WHERE Ordinal = ? AND IsStock = 1 ORDER BY Level LIMIT 1", car);
            return (id == null || id is DBNull) ? -1 : Convert.ToInt64(id, CultureInfo.InvariantCulture);
        }

        // The source rows of a pair, merged by level when the pair has front / rear tables; ordered by level (and Sequence).
        private static List<Dictionary<string, object>> SourceRows(SqliteDatabase db, Pair pair, long car)
        {
            object key;
            string column;
            switch (pair.SourceKey)
            {
                case "body": column = "CarBodyID"; key = car * 1000; break;
                case "car": column = "Ordinal"; key = car; break;
                case "drive": column = "DrivetrainID"; key = StockDrivetrain(db, car); break;
                default: column = "EngineID"; key = CarEngine.StockEngine(db, car); break;
            }
            List<Dictionary<string, object>> merged = new List<Dictionary<string, object>>();
            foreach (string[] source in pair.Sources)
            {
                if (!HasTable(db, source[0]))
                {
                    continue;
                }
                List<Dictionary<string, object>> rows = Rows(db, "SELECT * FROM " + SqliteDatabase.Quote(source[0]) + " WHERE " + column + " = ?", key);
                foreach (string rename in source[1].Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = rename.Split('>');
                    foreach (Dictionary<string, object> row in rows)
                    {
                        if (row.ContainsKey(parts[0]))
                        {
                            row[parts[1]] = row[parts[0]];
                            row.Remove(parts[0]);
                        }
                    }
                }
                if (pair.Sources.Length > 1 && pair.Table == "List_UpgradeEngineTurbo")
                {
                    if (rows.Count > 0)
                    {
                        merged = rows;
                        break;
                    }
                    continue;
                }
                if (merged.Count == 0)
                {
                    merged = rows;
                    continue;
                }
                // a second table (rear): its columns go into the row of the same level; MassDiff and Price add up
                foreach (Dictionary<string, object> row in rows)
                {
                    Dictionary<string, object> into = merged.Find(delegate(Dictionary<string, object> m) { return D(Get(m, "Level")) == D(Get(row, "Level")); });
                    if (into == null)
                    {
                        merged.Add(row);
                        continue;
                    }
                    foreach (KeyValuePair<string, object> kv in row)
                    {
                        if (string.Equals(kv.Key, "MassDiff", StringComparison.OrdinalIgnoreCase) || string.Equals(kv.Key, "Price", StringComparison.OrdinalIgnoreCase))
                        {
                            into[kv.Key] = D(Get(into, kv.Key)) + D(kv.Value);
                        }
                        else if (!into.ContainsKey(kv.Key))
                        {
                            into[kv.Key] = kv.Value;
                        }
                    }
                }
            }
            merged.Sort(delegate(Dictionary<string, object> a, Dictionary<string, object> b)
            {
                int c = D(Get(a, "Level")).CompareTo(D(Get(b, "Level")));
                return (c != 0) ? c : D(Get(a, "Sequence")).CompareTo(D(Get(b, "Sequence")));
            });
            return merged;
        }

        private static List<Dictionary<string, object>> TargetRows(SqliteDatabase db, Pair pair, long key)
        {
            List<Dictionary<string, object>> rows = Rows(db, "SELECT * FROM " + SqliteDatabase.Quote(pair.Table) + " WHERE " + (pair.Key == "engine" ? "EngineID" : "Ordinal") + " = ?", key);
            rows.Sort(delegate(Dictionary<string, object> a, Dictionary<string, object> b)
            {
                int c = D(Get(a, "Level")).CompareTo(D(Get(b, "Level")));
                return (c != 0) ? c : D(Get(a, "Sequence")).CompareTo(D(Get(b, "Sequence")));
            });
            return rows;
        }

        // the source column for an FM2 column of a converted row (same name, any case)
        private static string SourceColumn(Dictionary<string, object> source, string fm2Column)
        {
            return source.ContainsKey(fm2Column) ? fm2Column : null;
        }

        private static string PhysicsTable(string column)
        {
            if (column.IndexOf("SpringDamperPhysics", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "List_SpringDamperPhysics";
            }
            if (column.IndexOf("AntiSwayPhysics", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "List_AntiSwayPhysics";
            }
            return column.IndexOf("AeroPhysics", StringComparison.OrdinalIgnoreCase) >= 0 ? "List_AeroPhysics" : null;
        }

        private static string PhysicsKey(string table)
        {
            return table.Substring("List_".Length) + "ID";
        }

        // ---- learning ----

        private void LearnAll()
        {
            int cars = 0;
            foreach (object[] car in this.Target.Query("SELECT Id, MediaName FROM Data_Car").Rows)
            {
                object other = this.Source.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", car[1]);
                if (other == null || Convert.ToInt64(car[0], CultureInfo.InvariantCulture) == this.Car || string.Equals(this.LearnExclude, car[1] as string, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                long c2 = Convert.ToInt64(car[0], CultureInfo.InvariantCulture), cs = Convert.ToInt64(other, CultureInfo.InvariantCulture);
                cars++;
                this.learningGroup = Convert.ToString(this.Target.Scalar("SELECT p.DrivetrainId FROM Data_Car c JOIN Powertrains p ON p.Id = c.PowertrainID WHERE c.Id = ?", c2), CultureInfo.InvariantCulture)
                    + "|" + Convert.ToString(this.Target.Scalar("SELECT CarTypeID FROM Data_Car WHERE Id = ?", c2), CultureInfo.InvariantCulture);
                this.LearnCar(c2, cs);
                long e2 = CarEngine.StockEngine(this.Target, c2), es = CarEngine.StockEngine(this.Source, cs);
                if (e2 >= 0 && es >= 0)
                {
                    this.LearnRow("Data_Engine", "stock", this.Fm2Engine(e2), this.SourceEngine(es, cs), null);
                    foreach (Pair pair in EnginePairs)
                    {
                        this.LearnPair(pair, e2, cs);
                    }
                }
                foreach (Pair pair in CarPairs)
                {
                    this.LearnPair(pair, c2, cs);
                }
            }
            this.Log.Add(string.Format("rules learned from the {0} cars both games have", cars));
        }

        private void LearnCar(long c2, long cs)
        {
            List<Dictionary<string, object>> a = Rows(this.Target, "SELECT * FROM Data_Car WHERE Id = ?", c2);
            Dictionary<string, object> b = this.SourceCarRow(cs);
            if (a.Count == 0 || b == null)
            {
                return;
            }
            foreach (KeyValuePair<string, object> kv in a[0])
            {
                if (Array.Exists(CarSkip, delegate(string s) { return string.Equals(s, kv.Key, StringComparison.OrdinalIgnoreCase); }))
                {
                    continue;
                }
                string from = CarRenames.ContainsKey(kv.Key) ? CarRenames[kv.Key] : kv.Key;
                if (b.ContainsKey(from))
                {
                    this.Sample("Data_Car", kv.Key, "stock", kv.Value, b[from]);
                }
            }
        }

        // Data_Car of the source car with its Data_CarBody columns and the box measures.
        private Dictionary<string, object> SourceCarRow(long car)
        {
            List<Dictionary<string, object>> rows = Rows(this.Source, "SELECT * FROM Data_Car WHERE Id = ?", car);
            if (rows.Count == 0)
            {
                return null;
            }
            Dictionary<string, object> row = rows[0];
            if (HasTable(this.Source, "Data_CarBody"))
            {
                List<Dictionary<string, object>> body = Rows(this.Source, "SELECT * FROM Data_CarBody WHERE Id = ?", car * 1000);
                if (body.Count > 0)
                {
                    foreach (KeyValuePair<string, object> kv in body[0])
                    {
                        if (!kv.Key.Equals("Id", StringComparison.OrdinalIgnoreCase))
                        {
                            row[kv.Key] = kv.Value;
                        }
                    }
                    row["BoxLength"] = D(Get(row, "PristineBoundingBoxMaxZ")) - D(Get(row, "PristineBoundingBoxMinZ"));
                    row["BoxWidth"] = D(Get(row, "PristineBoundingBoxMaxX")) - D(Get(row, "PristineBoundingBoxMinX"));
                    row["BoxHeight"] = D(Get(row, "PristineBoundingBoxMaxY"));
                }
            }
            return row;
        }

        private Dictionary<string, object> Fm2Engine(long engine)
        {
            List<Dictionary<string, object>> rows = Rows(this.Target, "SELECT * FROM Data_Engine WHERE EngineID = ?", engine);
            return (rows.Count > 0) ? rows[0] : null;
        }

        // Data_Engine of the source with Disp_cc (the stock displacement) and DriveTypeID (the car's).
        private Dictionary<string, object> SourceEngine(long engine, long car)
        {
            List<Dictionary<string, object>> rows = Rows(this.Source, "SELECT * FROM Data_Engine WHERE EngineID = ?", engine);
            if (rows.Count == 0)
            {
                return null;
            }
            Dictionary<string, object> row = rows[0];
            object disp = HasTable(this.Source, "List_UpgradeEngineDisplacement")
                ? this.Source.Scalar("SELECT Disp FROM List_UpgradeEngineDisplacement WHERE EngineID = ? AND IsStock = 1 ORDER BY Level LIMIT 1", engine) : null;
            if (disp == null || disp is DBNull)
            {
                disp = this.Source.Scalar("SELECT Displacement FROM Data_Car WHERE Id = ?", car);
            }
            if (disp != null && !(disp is DBNull))
            {
                row["Disp_cc"] = disp;
            }
            object drive = this.Source.Scalar("SELECT DriveTypeID FROM Data_Car WHERE Id = ?", car);
            if (drive != null)
            {
                row["DriveTypeID"] = drive;
            }
            return row;
        }

        private void LearnRow(string table, string level, Dictionary<string, object> fm2, Dictionary<string, object> source, string[] skip)
        {
            if (fm2 == null || source == null)
            {
                return;
            }
            foreach (KeyValuePair<string, object> kv in fm2)
            {
                if (skip != null && Array.Exists(skip, delegate(string s) { return string.Equals(s, kv.Key, StringComparison.OrdinalIgnoreCase); }))
                {
                    continue;
                }
                string from = SourceColumn(source, kv.Key);
                if (from != null)
                {
                    this.Sample(table, kv.Key, level, kv.Value, source[from]);
                }
            }
        }

        private static readonly string[] RowSkip = new string[] { "Id", "Ordinal", "EngineID", "CarBodyID", "Level", "IsStock", "Sequence" };

        // Level numbers per table, as FM2 level - source level, stock rows and upgrades apart (2026-11-22, every FM2 table:
        // the level is the part's grade, 0-3, the stock part's level depends on the car and no upgrade is at or under it;
        // FM4 numbers the same way in most tables: offset 0; FM2's tire width / rim size start at 1).
        private readonly Dictionary<string, Dictionary<double, int>> stockLevels = new Dictionary<string, Dictionary<double, int>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<double, int>> levelOffsets = new Dictionary<string, Dictionary<double, int>>(StringComparer.OrdinalIgnoreCase);

        // Spring / anti-roll bar ranges: per physics column side and level, how often FM2 keeps Min = Max (a part you cannot
        // tune: FM2's stock and street levels) out of the pairs.
        private readonly Dictionary<string, int[]> fixedRanges = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);

        private void CountLevel(Dictionary<string, Dictionary<double, int>> d, string table, double v)
        {
            if (!d.ContainsKey(table))
            {
                d[table] = new Dictionary<double, int>();
            }
            Count(d[table], v);
        }

        // The FM2 level of a converted row.
        private long Fm2Level(string table, Dictionary<string, object> source)
        {
            Dictionary<double, int> d;
            Dictionary<string, Dictionary<double, int>> offsets = (D(Get(source, "IsStock")) == 1) ? this.stockLevels : this.levelOffsets;
            return (long) (D(Get(source, "Level")) + (offsets.TryGetValue(table, out d) ? Mode(d) : 0));
        }

        // Rows of the two games paired: stock with stock, then the upgrades in order (by PartsStringId for body kit parts).
        private static List<KeyValuePair<Dictionary<string, object>, Dictionary<string, object>>> PairRows(List<Dictionary<string, object>> a, List<Dictionary<string, object>> b, bool parts)
        {
            List<KeyValuePair<Dictionary<string, object>, Dictionary<string, object>>> pairs = new List<KeyValuePair<Dictionary<string, object>, Dictionary<string, object>>>();
            foreach (bool stock in new bool[] { true, false })
            {
                List<Dictionary<string, object>> x = a.FindAll(delegate(Dictionary<string, object> r) { return (D(Get(r, "IsStock")) == 1) == stock; });
                List<Dictionary<string, object>> y = b.FindAll(delegate(Dictionary<string, object> r) { return (D(Get(r, "IsStock")) == 1) == stock; });
                if (parts)
                {
                    bool[] used = new bool[y.Count];
                    foreach (Dictionary<string, object> r in x)
                    {
                        int j = y.FindIndex(delegate(Dictionary<string, object> s) { return D(Get(s, "PartsStringId")) == D(Get(r, "PartsStringId")); });
                        if (j >= 0 && !used[j])
                        {
                            used[j] = true;
                            pairs.Add(new KeyValuePair<Dictionary<string, object>, Dictionary<string, object>>(r, y[j]));
                        }
                    }
                    continue;
                }
                for (int i = 0; i < Math.Min(x.Count, y.Count); i++)
                {
                    pairs.Add(new KeyValuePair<Dictionary<string, object>, Dictionary<string, object>>(x[i], y[i]));
                }
            }
            return pairs;
        }

        private void LearnPair(Pair pair, long fm2Key, long sourceCar)
        {
            if (!HasTable(this.Target, pair.Table))
            {
                return;
            }
            List<Dictionary<string, object>> a = TargetRows(this.Target, pair, fm2Key);
            List<Dictionary<string, object>> b = SourceRows(this.Source, pair, sourceCar);
            foreach (KeyValuePair<Dictionary<string, object>, Dictionary<string, object>> p in PairRows(a, b, pair.Parts))
            {
                Dictionary<string, object> row = p.Key;
                int j = b.IndexOf(p.Value);
                {
                    if (D(Get(row, "IsStock")) == 1)
                    {
                        this.CountLevel(this.stockLevels, pair.Table, D(Get(row, "Level")) - D(Get(b[j], "Level")));
                    }
                    else
                    {
                        this.CountLevel(this.levelOffsets, pair.Table, D(Get(row, "Level")) - D(Get(b[j], "Level")));
                    }
                    string level = Level(row);
                    List<string> skip = new List<string>(RowSkip);
                    foreach (KeyValuePair<string, object> kv in row)
                    {
                        string physics = PhysicsTable(kv.Key);
                        if (physics == null)
                        {
                            continue;
                        }
                        skip.Add(kv.Key);
                        object other = Get(b[j], kv.Key);
                        if (other == null || kv.Value == null || kv.Value is DBNull || other is DBNull)
                        {
                            continue;
                        }
                        string side = pair.Table + "." + kv.Key;
                        if (physics == "List_AeroPhysics")
                        {
                            Dictionary<long, int> ids;
                            if (!this.sharedAero.TryGetValue(side + "|" + level, out ids))
                            {
                                this.sharedAero[side + "|" + level] = ids = new Dictionary<long, int>();
                            }
                            long id = Convert.ToInt64(kv.Value, CultureInfo.InvariantCulture);
                            ids[id] = (ids.ContainsKey(id) ? ids[id] : 0) + 1;
                        }
                        List<Dictionary<string, object>> pa = Rows(this.Target, "SELECT * FROM " + physics + " WHERE " + PhysicsKey(physics) + " = ?", kv.Value);
                        List<Dictionary<string, object>> pb = Rows(this.Source, "SELECT * FROM " + physics + " WHERE " + PhysicsKey(physics) + " = ?", other);
                        if (pa.Count > 0 && pb.Count > 0)
                        {
                            this.LearnRow(side, level, pa[0], pb[0], new string[] { PhysicsKey(physics), "Ordinal" });
                            foreach (KeyValuePair<string, object> c in pa[0])
                            {
                                if (!c.Key.StartsWith("Def", StringComparison.Ordinal) || !pa[0].ContainsKey("Min" + c.Key.Substring(3)) || !pa[0].ContainsKey("Max" + c.Key.Substring(3)))
                                {
                                    continue;
                                }
                                string k = side + "|" + level + "|" + c.Key.Substring(3);
                                if (!this.fixedRanges.ContainsKey(k))
                                {
                                    this.fixedRanges[k] = new int[2];
                                }
                                this.fixedRanges[k][1]++;
                                if (Math.Abs(D(pa[0]["Min" + c.Key.Substring(3)]) - D(pa[0]["Max" + c.Key.Substring(3)])) < 1e-6)
                                {
                                    this.fixedRanges[k][0]++;
                                }
                            }
                        }
                    }
                    this.LearnRow(pair.Table, level, row, b[j], skip.ToArray());
                }
            }
        }

        // ---- writing ----

        // A converted row: every FM2 column from the source row by its rule, else the template row's value.
        private Dictionary<string, object> ConvertRow(string table, string level, string[] columns, Dictionary<string, object> source, Dictionary<string, object> template, string[] skip)
        {
            Dictionary<string, object> row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (string column in columns)
            {
                object keep = Get(template, column);
                if (skip != null && Array.Exists(skip, delegate(string s) { return string.Equals(s, column, StringComparison.OrdinalIgnoreCase); }))
                {
                    row[column] = keep;
                    continue;
                }
                string from = (source != null) ? SourceColumn(source, column) : null;
                row[column] = (from == null) ? this.KeepNote(table, column, level, keep) : LikeTemplate(this.Convert2(table, column, level, source[from], keep), keep);
            }
            return row;
        }

        private object KeepNote(string table, string column, string level, object keep)
        {
            return this.NoRule(table, column, level, keep, "no source column");
        }

        private static string[] Columns(SqliteDatabase db, string table)
        {
            List<string> list = new List<string>();
            foreach (object[] r in db.Query("PRAGMA table_info(" + SqliteDatabase.Quote(table) + ")").Rows)
            {
                list.Add((string) r[1]);
            }
            return list.ToArray();
        }

        private static void Insert(SqliteDatabase db, string table, string[] columns, Dictionary<string, object> row)
        {
            List<string> names = new List<string>();
            List<string> marks = new List<string>();
            List<object> values = new List<object>();
            foreach (string column in columns)
            {
                names.Add(SqliteDatabase.Quote(column));
                marks.Add("?");
                object v = Get(row, column);
                values.Add((v == null) ? (object) DBNull.Value : v);
            }
            db.Execute("INSERT INTO " + SqliteDatabase.Quote(table) + " (" + string.Join(", ", names.ToArray()) + ") VALUES (" + string.Join(", ", marks.ToArray()) + ")", values.ToArray());
        }

        private static Dictionary<string, object> TemplateFor(List<Dictionary<string, object>> rows, Dictionary<string, object> source, bool[] used)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    bool sameStock = D(Get(rows[i], "IsStock")) == D(Get(source, "IsStock"));
                    bool sameLevel = D(Get(rows[i], "Level")) == D(Get(source, "Level"));
                    if ((pass == 0 && sameStock && sameLevel && !used[i]) || (pass == 1 && sameStock && sameLevel) || (pass == 2 && sameStock))
                    {
                        used[i] = true;
                        return rows[i];
                    }
                }
            }
            return (rows.Count > 0) ? rows[rows.Count - 1] : null;
        }

        // The rows of one pair rewritten: the copy's rows (the template) out, one converted row per source row in.
        private int WritePair(Pair pair, long fm2Key, long sourceCar, List<string> dropped)
        {
            if (!HasTable(this.Target, pair.Table))
            {
                return 0;
            }
            string[] columns = Columns(this.Target, pair.Table);
            List<Dictionary<string, object>> template = TargetRows(this.Target, pair, fm2Key);
            List<Dictionary<string, object>> source = SourceRows(this.Source, pair, sourceCar);
            if (source.Count == 0)
            {
                if (pair.Key == "car")
                {
                    this.Log.Add(pair.Table + ": the source has no rows, the template's stay");
                }
                return 0;
            }
            // body kit ids FM2 knows in this table (the others name parts FM2 has no string for)
            // (used by 2 other cars at least: a test copy left in the folder does not teach FM2 a part string)
            HashSet<double> knownParts = new HashSet<double>();
            if (pair.Parts)
            {
                foreach (object[] r in this.Target.Query("SELECT PartsStringId FROM " + SqliteDatabase.Quote(pair.Table) + " WHERE Ordinal <> ? GROUP BY PartsStringId HAVING COUNT(DISTINCT Ordinal) >= 2", fm2Key).Rows)
                {
                    knownParts.Add(D(r[0]));
                }
            }
            // the levels FM2 uses in this table (FM2 has no level 4: FM4's level 4 rows have nowhere to go)
            HashSet<long> knownLevels = new HashSet<long>();
            foreach (object[] r in this.Target.Query("SELECT Level FROM " + SqliteDatabase.Quote(pair.Table) + " WHERE " + (pair.Key == "engine" ? "EngineID" : "Ordinal") + " <> ? GROUP BY Level HAVING COUNT(*) >= 2", fm2Key).Rows)
            {
                knownLevels.Add(Convert.ToInt64(r[0], CultureInfo.InvariantCulture));
            }
            this.Target.Execute("DELETE FROM " + SqliteDatabase.Quote(pair.Table) + " WHERE " + (pair.Key == "engine" ? "EngineID" : "Ordinal") + " = ?", fm2Key);
            bool[] used = new bool[template.Count];
            long baseId = fm2Key * 1000;
            Dictionary<string, object> stockSource = source.Find(delegate(Dictionary<string, object> s) { return D(Get(s, "IsStock")) == 1; });
            long stockLevel = (stockSource != null) ? this.Fm2Level(pair.Table, stockSource) : long.MinValue;
            HashSet<long> ids = new HashSet<long>();
            int sequence = 0, written = 0;
            foreach (Dictionary<string, object> src in source)
            {
                if (pair.Parts && this.ModelParts != null && D(Get(src, "IsStock")) != 1)
                {
                    string part = KitPart(pair.Table, D(Get(src, "PartsStringId")));
                    if (part != null && !this.ModelParts.Contains(part))
                    {
                        dropped.Add(string.Format(CultureInfo.InvariantCulture, "{0} level {1} PartsStringId {2}: the FM2 model has no {3}", pair.Table, Get(src, "Level"), Get(src, "PartsStringId"), part));
                        continue;
                    }
                }
                if (pair.Parts && !knownParts.Contains(D(Get(src, "PartsStringId"))))
                {
                    dropped.Add(string.Format(CultureInfo.InvariantCulture, "{0} level {1} PartsStringId {2} (FM2 has no such part string)", pair.Table, Get(src, "Level"), Get(src, "PartsStringId")));
                    continue;
                }
                long fm2Level = this.Fm2Level(pair.Table, src);
                if (D(Get(src, "IsStock")) != 1 && fm2Level <= stockLevel)
                {
                    dropped.Add(string.Format(CultureInfo.InvariantCulture, "{0} level {1} (FM2 level {2}: not above the stock part's {3}, FM2 has no such upgrade)", pair.Table, Get(src, "Level"), fm2Level, stockLevel));
                    continue;
                }
                if (knownLevels.Count > 0 && !knownLevels.Contains(fm2Level))
                {
                    dropped.Add(string.Format(CultureInfo.InvariantCulture, "{0} level {1} (FM2 level {2}: no FM2 car has it){3}", pair.Table, Get(src, "Level"), fm2Level,
                        pair.Parts ? " PartsStringId " + Get(src, "PartsStringId") : ""));
                    continue;
                }
                Dictionary<string, object> probe = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                probe["Level"] = fm2Level;
                probe["IsStock"] = Get(src, "IsStock");
                string level = Level(probe);
                Dictionary<string, object> keep = TemplateFor(template, probe, used);
                List<string> skip = new List<string>(RowSkip);
                foreach (string column in columns)
                {
                    if (PhysicsTable(column) != null)
                    {
                        skip.Add(column);
                    }
                }
                Dictionary<string, object> row = this.ConvertRow(pair.Table, level, columns, src, keep, skip.ToArray());
                long slot = Convert.ToInt64(D(Get(src, "Id")), CultureInfo.InvariantCulture) % 1000;
                while (ids.Contains(baseId + slot))
                {
                    slot++;
                }
                ids.Add(baseId + slot);
                row["Id"] = baseId + slot;
                row[pair.Key == "engine" ? "EngineID" : "Ordinal"] = fm2Key;
                row["Level"] = fm2Level;
                row["IsStock"] = LikeTemplate(Get(src, "IsStock"), Get(keep, "IsStock"));
                if (Array.Exists(columns, delegate(string c) { return string.Equals(c, "Sequence", StringComparison.OrdinalIgnoreCase); }))
                {
                    row["Sequence"] = (long) sequence++;
                }
                if (pair.Parts)
                {
                    row["PartsStringId"] = LikeTemplate(Get(src, "PartsStringId"), Get(keep, "PartsStringId"));
                }
                foreach (string column in columns)
                {
                    string physics = PhysicsTable(column);
                    if (physics != null)
                    {
                        row[column] = this.WritePhysics(pair.Table + "." + column, physics, level, Get(src, column), Get(keep, column));
                    }
                }
                Insert(this.Target, pair.Table, columns, row);
                written++;
            }
            return written;
        }

        // A physics row for a converted row: shared FM2 row when FM2 shares one at that level, else a new counter.
        private object WritePhysics(string side, string table, string level, object sourceId, object templateId)
        {
            string key = PhysicsKey(table);
            if (table == "List_AeroPhysics")
            {
                Dictionary<long, int> ids;
                if (this.sharedAero.TryGetValue(side + "|" + level, out ids) && ids.Count > 0)
                {
                    long best = 0;
                    int most = 0, total = 0;
                    foreach (KeyValuePair<long, int> kv in ids)
                    {
                        total += kv.Value;
                        if (kv.Value > most)
                        {
                            most = kv.Value;
                            best = kv.Key;
                        }
                    }
                    if (most * 2 >= total && total >= MinPairs)
                    {
                        this.Note(side, "(row)", level, string.Format("shared FM2 row {0} ({1} of {2} pairs)", best, most, total));
                        return best;
                    }
                }
            }
            List<Dictionary<string, object>> src = (sourceId == null || sourceId is DBNull) ? new List<Dictionary<string, object>>() : Rows(this.Source, "SELECT * FROM " + table + " WHERE " + key + " = ?", sourceId);
            List<Dictionary<string, object>> tpl = (templateId == null || templateId is DBNull) ? new List<Dictionary<string, object>>() : Rows(this.Target, "SELECT * FROM " + table + " WHERE " + key + " = ?", templateId);
            if (src.Count == 0)
            {
                return templateId;
            }
            string[] columns = Columns(this.Target, table);
            Dictionary<string, object> row = this.ConvertRow(side, level, columns, src[0], (tpl.Count > 0) ? tpl[0] : null, new string[] { key, "Ordinal" });
            // ranges: each column converted on its own can leave Min above Max; FM2 keeps Min = Def = Max where its cars do
            foreach (string column in columns)
            {
                if (!column.StartsWith("Def", StringComparison.Ordinal))
                {
                    continue;
                }
                string name = column.Substring(3);
                if (!row.ContainsKey("Min" + name) || !row.ContainsKey("Max" + name))
                {
                    continue;
                }
                double def = D(row[column]), min = D(row["Min" + name]), max = D(row["Max" + name]);
                int[] f;
                if (this.fixedRanges.TryGetValue(side + "|" + level + "|" + name, out f) && f[0] * 2 >= f[1])
                {
                    row["Min" + name] = row[column];
                    row["Max" + name] = row[column];
                    this.Note(side, "Min/Max" + name, level, string.Format("= Def{0} (FM2 fixes it on {1} of {2} pairs)", name, f[0], f[1]));
                }
                else
                {
                    row["Min" + name] = Math.Min(min, def);
                    row["Max" + name] = Math.Max(max, def);
                    this.Note(side, "Min/Max" + name, level, "Min <= Def <= Max");
                }
            }
            long id = DbIds.Next(this.Target, table, key, Game);
            row[key] = id;
            if (Array.Exists(columns, delegate(string c) { return string.Equals(c, "Ordinal", StringComparison.OrdinalIgnoreCase); }))
            {
                row["Ordinal"] = this.Car;
            }
            Insert(this.Target, table, columns, row);
            return id;
        }

        // ---- run ----

        public void Run()
        {
            if (Rows(this.Target, "SELECT Id FROM Data_Car WHERE Id = ?", this.Car).Count == 0)
            {
                throw new InvalidOperationException("The FM2 database has no car " + this.Car + ".");
            }
            if (Rows(this.Source, "SELECT Id FROM Data_Car WHERE Id = ?", this.SourceCar).Count == 0)
            {
                throw new InvalidOperationException("The source database has no car " + this.SourceCar + ".");
            }
            // the ported car's group: the source's drive type (1 front, 2 rear, 3 all wheels: FM2's numbering too) and car type
            this.portGroup = Convert.ToString(this.Source.Scalar("SELECT DriveTypeID FROM Data_Car WHERE Id = ?", this.SourceCar), CultureInfo.InvariantCulture)
                + "|" + Convert.ToString(this.Source.Scalar("SELECT CarTypeID FROM Data_Car WHERE Id = ?", this.SourceCar), CultureInfo.InvariantCulture);
            this.fm2PortGroup = this.portGroup;     // drive type: FM2's numbering too; car type: the same ids (Data_Car.CarTypeID)
            this.LearnAll();
            this.learningGroup = null;
            this.Log.Add("group of the car (drive type | car type): " + this.portGroup);
            List<string> dropped = new List<string>();

            // aero rows the copy owns now (Duplicate Car made them): removed when nothing points at them any more
            List<long> oldAero = new List<long>();
            foreach (string table in new string[] { "List_UpgradeFrontBumper", "List_UpgradeRearWing" })
            {
                foreach (object[] r in this.Target.Query("SELECT AeroPhysicsID FROM " + table + " WHERE Ordinal = ?", this.Car).Rows)
                {
                    oldAero.Add(Convert.ToInt64(r[0], CultureInfo.InvariantCulture));
                }
            }
            List<Dictionary<string, object>> oldSprings = Rows(this.Target, "SELECT SpringDamperPhysicsID AS Id FROM List_SpringDamperPhysics WHERE Ordinal = ?", this.Car);
            List<Dictionary<string, object>> oldSway = Rows(this.Target, "SELECT AntiSwayPhysicsID AS Id FROM List_AntiSwayPhysics WHERE Ordinal = ?", this.Car);

            // car-keyed tables
            foreach (Pair pair in CarPairs)
            {
                int n = this.WritePair(pair, this.Car, this.SourceCar, dropped);
                if (n > 0)
                {
                    this.Log.Add(string.Format("{0}: {1} rows from the source", pair.Table, n));
                }
            }
            foreach (Dictionary<string, object> r in oldSprings)
            {
                this.Target.Execute("DELETE FROM List_SpringDamperPhysics WHERE SpringDamperPhysicsID = ? AND NOT EXISTS (SELECT 1 FROM List_UpgradeSpringDamper WHERE FrontSpringDamperPhysicsID = ? OR RearSpringDamperPhysicsID = ?)", r["Id"], r["Id"], r["Id"]);
            }
            foreach (Dictionary<string, object> r in oldSway)
            {
                this.Target.Execute("DELETE FROM List_AntiSwayPhysics WHERE AntiSwayPhysicsID = ? AND NOT EXISTS (SELECT 1 FROM List_UpgradeAntiSway WHERE FrontAntiSwayPhysicsID = ? OR RearAntiSwayPhysicsID = ?)", r["Id"], r["Id"], r["Id"]);
            }
            foreach (long id in oldAero)
            {
                this.Target.Execute("DELETE FROM List_AeroPhysics WHERE AeroPhysicsID = ? AND NOT EXISTS (SELECT 1 FROM List_UpgradeFrontBumper WHERE AeroPhysicsID = ?) AND NOT EXISTS (SELECT 1 FROM List_UpgradeRearWing WHERE AeroPhysicsID = ?)", id, id, id);
            }

            this.WriteEngine(dropped);
            this.WriteCar();
            this.SpeedInGears();
            this.EngineMaker();
            this.EngineMedia();
            this.HoodCamera();
            this.Colors();
            this.WheelMass();
            this.BrakeRange();
            foreach (string line in dropped)
            {
                this.Log.Add("not ported: " + line);
            }
        }

        private void WriteCar()
        {
            List<Dictionary<string, object>> before = Rows(this.Target, "SELECT * FROM Data_Car WHERE Id = ?", this.Car);
            Dictionary<string, object> source = this.SourceCarRow(this.SourceCar);
            List<string> sets = new List<string>();
            List<object> values = new List<object>();
            int converted = 0;
            foreach (KeyValuePair<string, object> kv in before[0])
            {
                if (Array.Exists(CarSkip, delegate(string s) { return string.Equals(s, kv.Key, StringComparison.OrdinalIgnoreCase); }))
                {
                    continue;
                }
                string from = CarRenames.ContainsKey(kv.Key) ? CarRenames[kv.Key] : kv.Key;
                object v = source.ContainsKey(from) ? LikeTemplate(this.Convert2("Data_Car", kv.Key, "stock", source[from], kv.Value), kv.Value) : this.KeepNote("Data_Car", kv.Key, "stock", kv.Value);
                if (source.ContainsKey(from))
                {
                    converted++;
                }
                sets.Add(SqliteDatabase.Quote(kv.Key) + " = ?");
                values.Add(v ?? DBNull.Value);
            }
            values.Add(this.Car);
            this.Target.Execute("UPDATE Data_Car SET " + string.Join(", ", sets.ToArray()) + " WHERE Id = ?", values.ToArray());
            this.Log.Add(string.Format("Data_Car: {0} columns from the source car, the others the template's (names, PI and ratings: later)", converted));
        }

        // ---- engine ----

        // A port run again (2026-11-23, R33: the race bumper) cloned the car's own engine of the first run (2151 -> 2152) and
        // left the old one in the database with no car. The engine the clone replaced is deleted from every engine table
        // when it is not one of FM2's (EngineID above GameIdLimits, base game and DLC) and no car uses it any more.
        private bool DropOldOwnEngine(long old)
        {
            if (old <= GameIdLimits.Max(ForzaGame.Motorsport2, "Data_Engine", "EngineID"))
            {
                return false;
            }
            long users = Convert.ToInt64(this.Target.Scalar("SELECT count(*) FROM List_UpgradeEngine WHERE EngineID = ?", old), CultureInfo.InvariantCulture)
                + Convert.ToInt64(this.Target.Scalar("SELECT count(*) FROM Combo_Engines WHERE EngineID = ?", old), CultureInfo.InvariantCulture);
            if (users > 0)
            {
                return false;
            }
            int rows = 0;
            foreach (KeyValuePair<string, List<string>> table in DbIds.Schema(this.Target))
            {
                if (table.Value.Exists(delegate(string c) { return string.Equals(c, "EngineID", StringComparison.OrdinalIgnoreCase); }))
                {
                    rows += this.Target.Execute("DELETE FROM " + SqliteDatabase.Quote(table.Key) + " WHERE EngineID = ?", old);
                }
            }
            this.Log.Add(string.Format(CultureInfo.InvariantCulture, "engine: the car's previous own engine {0} deleted ({1} rows, no car uses it)", old, rows));
            return true;
        }

        // Every row of FM2 engine 'from' becomes engine 'to' (free): EngineID in every table that has it, and the part keys
        // EngineID x 1000 + level (Id) of the engine part tables. Not StockGarage: rewritten from Data_Car afterwards.
        public static int RenumberFm2Engine(SqliteDatabase db, long from, long to)
        {
            int rows = 0;
            foreach (KeyValuePair<string, List<string>> table in DbIds.Schema(db))
            {
                if (!table.Value.Exists(delegate(string c) { return string.Equals(c, "EngineID", StringComparison.OrdinalIgnoreCase); }))
                {
                    continue;
                }
                string q = SqliteDatabase.Quote(table.Key);
                if (table.Value.Exists(delegate(string c) { return string.Equals(c, "Id", StringComparison.Ordinal); }))
                {
                    db.Execute("UPDATE " + q + " SET Id = Id - ? + ? WHERE EngineID = ? AND Id BETWEEN ? AND ?", from * 1000, to * 1000, from, from * 1000, (from * 1000) + 999);
                }
                rows += db.Execute("UPDATE " + q + " SET EngineID = ? WHERE EngineID = ?", to, from);
            }
            return rows;
        }

        private void WriteEngine(List<string> dropped)
        {
            long templateEngine = CarEngine.StockEngine(this.Target, this.Car);
            long sourceEngine = CarEngine.StockEngine(this.Source, this.SourceCar);
            if (templateEngine < 0 || sourceEngine < 0)
            {
                this.Log.Add("engine: no stock engine in one of the games, the template's stays");
                return;
            }
            Dictionary<string, object> templateCam = Rows(this.Target, "SELECT * FROM List_UpgradeEngineCamValves WHERE EngineID = ? AND IsStock = 1 ORDER BY Level LIMIT 1", templateEngine)[0];
            List<string> plan = new List<string>();
            long engine = CarEngine.CloneFm2(this.Target, this.Car, new EngineSpec(), plan);
            this.Log.Add("engine: own FM2 engine " + engine + " (copy of the template's " + templateEngine + ", then every table from the source's " + sourceEngine + ")");

            // Data_Engine
            Dictionary<string, object> src = this.SourceEngine(sourceEngine, this.SourceCar);
            Dictionary<string, object> old = this.Fm2Engine(engine);
            string[] columns = Columns(this.Target, "Data_Engine");
            Dictionary<string, object> row = this.ConvertRow("Data_Engine", "stock", columns, src, old, new string[] { "EngineID", "MediaName" });
            this.BoreAndStroke(row);
            // EngineName: FM2 holds a plain engine code ("RB26DETT") on 20 % of its engines and "" on the rest; FM4's text is
            // an internal description ("V6TT - Nissan GT-R '10"), not a code: FM2's "" (user 2026-11-23)
            row["EngineName"] = "";
            this.Note("Data_Engine", "EngineName", "stock", "\"\" (FM2's most common; FM4's text is a description, not an engine code)");
            this.Target.Execute("DELETE FROM Data_Engine WHERE EngineID = ?", engine);
            Insert(this.Target, "Data_Engine", columns, row);

            // the curves of the cloned camshaft levels go, the new ones come with the converted rows
            foreach (object[] r in this.Target.Query("SELECT TorqueCurveFullThrottleID, TorqueCurveZeroThrottleID FROM List_UpgradeEngineCamValves WHERE EngineID = ?", engine).Rows)
            {
                this.Target.Execute("DELETE FROM List_TorqueCurve WHERE TorqueCurveID IN (?, ?)", r[0], r[1]);
            }
            foreach (Pair pair in EnginePairs)
            {
                int n = this.WritePair(pair, engine, this.SourceCar, dropped);
                if (n == 0)
                {
                    // the source has no such part (a natural engine's turbo...): the clone's rows of the template go too
                    if (HasTable(this.Target, pair.Table) && Rows(this.Target, "SELECT Id FROM " + pair.Table + " WHERE EngineID = ?", engine).Count > 0 && SourceRows(this.Source, pair, this.SourceCar).Count == 0)
                    {
                        this.Target.Execute("DELETE FROM " + SqliteDatabase.Quote(pair.Table) + " WHERE EngineID = ?", engine);
                        this.Log.Add(pair.Table + ": the source engine has none, the template's rows removed");
                    }
                    continue;
                }
                this.Log.Add(string.Format("{0}: {1} rows from the source", pair.Table, n));
            }
            this.WriteCurves(engine, sourceEngine, templateCam);
            this.FoldTorqueScales(engine, sourceEngine, dropped);

            // List_UpgradeEngine: the stock row only (engine swaps are not ported)
            List<Dictionary<string, object>> swaps = Rows(this.Source, "SELECT * FROM List_UpgradeEngine WHERE Ordinal = ? AND IsStock = 0", this.SourceCar);
            foreach (Dictionary<string, object> swap in swaps)
            {
                dropped.Add("engine swap " + Get(swap, "EngineID") + " (level " + Get(swap, "Level") + ")");
            }
            List<Dictionary<string, object>> mine = Rows(this.Target, "SELECT * FROM List_UpgradeEngine WHERE Ordinal = ? ORDER BY IsStock DESC, Level", this.Car);
            Dictionary<string, object> stockSource = Rows(this.Source, "SELECT * FROM List_UpgradeEngine WHERE Ordinal = ? AND IsStock = 1", this.SourceCar)[0];
            string[] engineColumns = Columns(this.Target, "List_UpgradeEngine");
            Dictionary<string, object> stock = this.ConvertRow("List_UpgradeEngine", "stock", engineColumns, stockSource, mine[0], RowSkip);
            stock["Id"] = this.Car * 1000;
            stock["Ordinal"] = this.Car;
            stock["EngineID"] = engine;
            stock["Level"] = Get(mine[0], "Level");       // FM2's numbering (the template's stock row)
            stock["IsStock"] = 1L;
            this.Target.Execute("DELETE FROM List_UpgradeEngine WHERE Ordinal = ?", this.Car);
            Insert(this.Target, "List_UpgradeEngine", engineColumns, stock);
            if (HasTable(this.Target, "Combo_Engines"))
            {
                int gone = this.Target.Execute("DELETE FROM Combo_Engines WHERE Ordinal = ? AND EngineID <> ?", this.Car, engine);
                if (gone > 0)
                {
                    this.Log.Add("Combo_Engines: " + gone + " swap rows of the template removed");
                }
            }
            this.Carburetion(engine, sourceEngine);
            // last: WriteCurves still reads the previous engine's zero-throttle curve. A re-run keeps the car's engine id
            // (2026-11-23: a saved game held the 458 with engine 2154; the re-run made it 2157 and deleted 2154, and FM2
            // crashed saving the profile): the new rows take the old id back.
            if (this.DropOldOwnEngine(templateEngine))
            {
                int moved = RenumberFm2Engine(this.Target, engine, templateEngine);
                this.Log.Add(string.Format(CultureInfo.InvariantCulture, "engine: the new rows take the car's engine id {0} back ({1} rows; saved games keep pointing at it)", templateEngine, moved));
            }
        }

        // Every converted CamValves row gets its full-throttle curve from the source level's curve (resampled to FM2's 151
        // values when the source has more) and a zero-throttle curve (the source's own, or the template's shape with the
        // source's ZeroThrottleTorqueScale).
        private void WriteCurves(long engine, long sourceEngine, Dictionary<string, object> templateCam)
        {
            double templateZeroScale;
            double[] templateZero;
            CarEngine.CurveRow(this.Target, Convert.ToInt64(Get(templateCam, "TorqueCurveZeroThrottleID"), CultureInfo.InvariantCulture), out templateZeroScale, out templateZero);
            double templateMax = D(Get(templateCam, "TorqueCurveMaxRPM"));
            int capacity = 0;
            string[] curveColumns = Columns(this.Target, "List_TorqueCurve");
            foreach (string c in curveColumns)
            {
                if (c.Length > 1 && c[0] == 'v' && char.IsDigit(c[1]))
                {
                    capacity++;
                }
            }
            List<Dictionary<string, object>> sourceCams = Rows(this.Source, "SELECT * FROM List_UpgradeEngineCamshaft WHERE EngineID = ? ORDER BY Level", sourceEngine);
            foreach (Dictionary<string, object> cam in Rows(this.Target, "SELECT * FROM List_UpgradeEngineCamValves WHERE EngineID = ? ORDER BY Level", engine))
            {
                Dictionary<string, object> from = sourceCams.Find(delegate(Dictionary<string, object> s) { return this.Fm2Level("List_UpgradeEngineCamValves", s) == (long) D(Get(cam, "Level")) && D(Get(s, "IsStock")) == D(Get(cam, "IsStock")); });
                double scale;
                double[] values;
                if (from == null || !CarEngine.CurveRow(this.Source, Convert.ToInt64(Get(from, "TorqueCurveFullThrottleID"), CultureInfo.InvariantCulture), out scale, out values) || values.Length < 2)
                {
                    throw new InvalidOperationException("The source camshaft level " + Get(cam, "Level") + " has no torque curve.");
                }
                double maxRpm = D(Get(from, "TorqueCurveMaxRPM"));
                double[] full = Resample(values, maxRpm, capacity, maxRpm);
                // FM4 / FH keep the zero-throttle scale in the full curve's row; FM3 has a zero-throttle curve of its own
                double zeroScale = Array.Exists(Columns(this.Source, "List_TorqueCurve"), delegate(string c) { return string.Equals(c, "ZeroThrottleTorqueScale", StringComparison.OrdinalIgnoreCase); })
                    ? D(this.Source.Scalar("SELECT ZeroThrottleTorqueScale FROM List_TorqueCurve WHERE TorqueCurveID = ?", Get(from, "TorqueCurveFullThrottleID"))) : 0;
                double[] zero;
                double zs;
                object sourceZero = Get(from, "TorqueCurveZeroThrottleID");
                double[] own;
                if (sourceZero != null && !(sourceZero is DBNull) && CarEngine.CurveRow(this.Source, Convert.ToInt64(sourceZero, CultureInfo.InvariantCulture), out zs, out own) && own.Length >= 2)
                {
                    zero = Resample(own, maxRpm, full.Length, maxRpm);
                }
                else
                {
                    // FM2's typical shape over the new curve's points (CarDataToFm2.Specific.cs), never the template's
                    zero = ZeroShape(full.Length);
                    zs = (zeroScale > 0) ? zeroScale : Fm2ZeroScale;
                }
                long fullId = DbIds.Next(this.Target, "List_TorqueCurve", "TorqueCurveID", Game);
                this.WriteCurve(curveColumns, fullId, engine, scale, full);
                long zeroId = DbIds.Next(this.Target, "List_TorqueCurve", "TorqueCurveID", Game);
                this.WriteCurve(curveColumns, zeroId, engine, zs, zero);
                this.Target.Execute("UPDATE List_UpgradeEngineCamValves SET TorqueCurveFullThrottleID = ?, TorqueCurveZeroThrottleID = ?, NumRPMEntriesArray = ?, TorqueCurveMaxRPM = ? WHERE Id = ?",
                    fullId, zeroId, (long) full.Length, maxRpm, Get(cam, "Id"));
                // FM2's shift points (no source column: the template level's) follow the redline
                object templateRedline = this.Target.Scalar("SELECT RedlineRPM FROM List_UpgradeEngineCamValves WHERE EngineID = ? AND Level = ? ORDER BY IsStock DESC LIMIT 1", Get(templateCam, "EngineID"), Get(cam, "Level"))
                    ?? Get(templateCam, "RedlineRPM");
                double redlineRatio = D(Get(cam, "RedlineRPM")) / Math.Max(1, D(templateRedline));
                this.Target.Execute("UPDATE List_UpgradeEngineCamValves SET ShiftDownZeroThrottleRPM = ROUND(ShiftDownZeroThrottleRPM * ?), ShiftUpZeroThrottleRPM = ROUND(ShiftUpZeroThrottleRPM * ?), ShiftDownFullThrottleRPM = ROUND(ShiftDownFullThrottleRPM * ?), ShiftUpFullThrottleRPM = ROUND(ShiftUpFullThrottleRPM * ?) WHERE Id = ?",
                    redlineRatio, redlineRatio, redlineRatio, redlineRatio, Get(cam, "Id"));
                this.Note("List_UpgradeEngineCamValves", "Shift*RPM", "all", "template level x (redline / the template level's redline)");
                this.Log.Add(string.Format(CultureInfo.InvariantCulture, "torque curve level {0}: {1} values to {2:0} rpm, scale {3:0.####} (source {4} values), zero throttle scale {5:0.####}",
                    Get(cam, "Level"), full.Length, maxRpm, scale, values.Length, zs));
            }
        }

        // values (the last one the end marker) at i x maxRpm / (n - 1), resampled to 'count' points (at most 'capacity') over
        // 'newMax' rpm, by rpm; the end marker stays last.
        private static double[] Resample(double[] values, double maxRpm, int count, double newMax)
        {
            int n = values.Length;
            if (Math.Abs(maxRpm - newMax) < 1e-6 && n <= count)
            {
                return (double[]) values.Clone();
            }
            int m = Math.Min(count, Math.Max(2, n));
            double[] result = new double[m];
            for (int j = 0; j < m - 1; j++)
            {
                double rpm = j * newMax / (m - 1);
                double f = Math.Min(n - 2, rpm * (n - 1) / maxRpm);
                int i = (int) Math.Floor(f);
                double t = f - i;
                double b = (i + 1 < n - 1) ? values[i + 1] : values[n - 2];
                result[j] = values[i] + ((b - values[i]) * t);
            }
            result[m - 1] = values[n - 1];
            return result;
        }

        private void WriteCurve(string[] columns, long id, long engine, double scale, double[] values)
        {
            Dictionary<string, object> row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (string c in columns)
            {
                row[c] = 0.0;
            }
            row["TorqueCurveID"] = id;
            row["EngineID"] = engine;
            row["TorqueScale"] = scale;
            row["NumTorqueValues"] = (long) values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                row["v" + i] = values[i];
            }
            Insert(this.Target, "List_TorqueCurve", columns, row);
        }

        // The stock TorqueScale of the source's parts FM2 has no table for: added (each - 1) to the stock Block row, so the
        // stock torque is the source's (both games add the parts up: 1 + sum of (TorqueScale - 1)).
        private void FoldTorqueScales(long engine, long sourceEngine, List<string> dropped)
        {
            double extra = 0;
            List<string> parts = new List<string>();
            foreach (string table in SourceOnlyTorqueTables)
            {
                if (!HasTable(this.Source, table))
                {
                    continue;
                }
                foreach (Dictionary<string, object> r in Rows(this.Source, "SELECT * FROM " + table + " WHERE EngineID = ?", sourceEngine))
                {
                    if (D(Get(r, "IsStock")) == 1)
                    {
                        double s = D(Get(r, "TorqueScale"));
                        if (s > 0 && Math.Abs(s - 1) > 1e-9)
                        {
                            extra += s - 1;
                            parts.Add(table.Substring("List_UpgradeEngine".Length) + " " + Num(s));
                        }
                    }
                    else
                    {
                        dropped.Add(string.Format(CultureInfo.InvariantCulture, "engine upgrade {0} level {1} (FM2 has no such part)", table.Substring("List_UpgradeEngine".Length), Get(r, "Level")));
                    }
                }
            }
            if (parts.Count == 0)
            {
                return;
            }
            object id = this.Target.Scalar("SELECT Id FROM List_UpgradeEngineBlock WHERE EngineID = ? AND IsStock = 1", engine);
            if (id == null)
            {
                this.Log.Add("WARNING: stock TorqueScale of " + string.Join(", ", parts.ToArray()) + " not carried over (no stock Block row)");
                return;
            }
            this.Target.Execute("UPDATE List_UpgradeEngineBlock SET TorqueScale = TorqueScale + ? WHERE Id = ?", extra, id);
            this.Log.Add(string.Format(CultureInfo.InvariantCulture, "engine: stock TorqueScale of {0} added to the stock Block row ({1:+0.####})", string.Join(", ", parts.ToArray()), extra));
        }
    }
}
