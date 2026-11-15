namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    // FM2's own engine for Duplicate Car (ST-02, 2026-11-14). FM2's engine tables are not FM4's: the camshaft levels are
    // List_UpgradeEngineCamValves (RedlineRPM, StallRPM, TorqueCurveMaxRPM, NumRPMEntriesArray, TorqueCurveFullThrottleID,
    // TorqueCurveZeroThrottleID), the other parts Block, Exhaust, FuelSystem, Ignition, Intake, Intercooler, Turbo, CSC, DSC
    // (Id = EngineID * 1000 + level), the displacement is Data_Engine.Disp_cc, the torque curves are counters with an
    // EngineID (151 values, FM4's grid and -2.63 end marker). The copy keeps EVERY level of the source (FM2 has no
    // generic upgrade rules here): the parts under the new EngineID, the curves under new counters; a new redline scales
    // the levels' rpm, a new idle sets StallRPM, a designed curve is put on every level (RebuildCurves). Aspiration and
    // boost (2026-11-15): ConvertAspirationFm2 (forced induction rows from the FM2 engine of that aspiration closest in boost).
    public static partial class CarEngine
    {
        public const string Fm2CamTable = "List_UpgradeEngineCamValves";

        public static long CloneFm2(SqliteDatabase db, long carId, EngineSpec spec, List<string> plan)
        {
            ForzaGame game = ForzaGame.Motorsport2;
            long old = StockEngine(db, carId);
            if (old < 0)
            {
                throw new InvalidOperationException("The car has no stock engine in List_UpgradeEngine.");
            }
            Dictionary<string, List<string>> schema = DbIds.Schema(db);
            SqliteDatabase.Result engine = db.Query("SELECT * FROM Data_Engine WHERE EngineID = ?", old);
            if (engine.Rows.Count == 0)
            {
                throw new InvalidOperationException("Data_Engine has no engine " + old + ".");
            }
            object aspiration, boost;
            int aspIndex = Array.FindIndex(engine.Columns, delegate(string c) { return string.Equals(c, "AspirationID_Stock", StringComparison.OrdinalIgnoreCase); });
            int barIndex = Array.FindIndex(engine.Columns, delegate(string c) { return string.Equals(c, "StockBoost-bar", StringComparison.OrdinalIgnoreCase); });
            long oldAspiration = (aspIndex >= 0) ? Convert.ToInt64(engine.Rows[0][aspIndex], CultureInfo.InvariantCulture) : 1;
            double oldBar = (barIndex >= 0) ? Real(engine.Rows[0][barIndex]) : 0;
            long newAspiration = spec.Data.TryGetValue("AspirationID_Stock", out aspiration) ? Convert.ToInt64(aspiration, CultureInfo.InvariantCulture) : oldAspiration;
            double newBar = spec.Data.TryGetValue("StockBoost-bar", out boost) ? Real(boost) : oldBar;
            if (newAspiration < 1 || newAspiration > 6)
            {
                throw new InvalidOperationException("FM2: aspiration " + newAspiration + " has no engine to take its parts from (FM2 uses 1-6: natural, turbo, twin turbo, quad turbo, DSC, CSC).");
            }
            if (newAspiration <= 1)
            {
                newBar = 0;
                spec.Data["StockBoost-bar"] = 0.0;      // a natural engine has no boost (199 of 199 FM2 natural engines)
            }
            List<string> partTables = new List<string>();
            foreach (KeyValuePair<string, List<string>> table in schema)
            {
                if (table.Key != "Data_Engine" && table.Key != "List_UpgradeEngine" && table.Key != "Combo_Engines" && table.Key != "List_TorqueCurve"
                    && table.Value.Exists(delegate(string c) { return string.Equals(c, "EngineID", StringComparison.OrdinalIgnoreCase); }))
                {
                    partTables.Add(table.Key);
                }
            }
            partTables.Sort(StringComparer.OrdinalIgnoreCase);

            // The new EngineID: free, EngineID * 1000 + 0..999 above FM2's largest key (base game and DLC) and free in every part table.
            long id = DbIds.Next(db, "Data_Engine", "EngineID", game);
            while (true)
            {
                bool free = true;
                foreach (string table in partTables)
                {
                    string key = DbIds.KeyColumn(db, table, schema[table], game);
                    if (key == null)
                    {
                        continue;
                    }
                    long limit = GameIdLimits.Max(game, table, key);
                    free = free && id * 1000 > limit
                        && Convert.ToInt64(db.Scalar("SELECT count(*) FROM " + SqliteDatabase.Quote(table) + " WHERE " + SqliteDatabase.Quote(key) + " BETWEEN ? AND ?", id * 1000, (id * 1000) + 999), CultureInfo.InvariantCulture) == 0;
                }
                if (free)
                {
                    break;
                }
                id++;
            }

            // Data_Engine.
            object[] row = (object[]) engine.Rows[0].Clone();
            for (int i = 0; i < engine.Columns.Length; i++)
            {
                string column = engine.Columns[i];
                object value;
                if (string.Equals(column, "EngineID", StringComparison.OrdinalIgnoreCase))
                {
                    row[i] = id;
                }
                else if (string.Equals(column, "EngineName", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(spec.Name))
                {
                    row[i] = spec.Name;
                }
                else if (string.Equals(column, "Disp_cc", StringComparison.OrdinalIgnoreCase) && spec.DisplacementCc.HasValue)
                {
                    row[i] = spec.DisplacementCc.Value;
                }
                else if (string.Equals(column, "MediaName", StringComparison.OrdinalIgnoreCase))
                {
                    // FM2 keeps the source's: the sound goes by the CAR's name (a chosen sound is the DSPTuning file CarDuplicate copies).
                }
                else if (spec.Data.TryGetValue(column, out value))
                {
                    row[i] = value;
                }
            }
            Insert(db, "Data_Engine", engine.Columns, row);
            plan.Add(string.Format(CultureInfo.InvariantCulture, "engine: Data_Engine {0} copied as {1}{2}", old, id, string.IsNullOrEmpty(spec.Name) ? "" : " \"" + spec.Name + "\""));

            // Redline and idle of the source's stock level.
            double oldRedline = 0;
            object stockRedline = schema.ContainsKey(Fm2CamTable) ? db.Scalar("SELECT RedlineRPM FROM " + Fm2CamTable + " WHERE EngineID = ? AND IsStock = 1 ORDER BY Level LIMIT 1", old) : null;
            if (stockRedline != null && stockRedline != DBNull.Value)
            {
                oldRedline = Convert.ToDouble(stockRedline, CultureInfo.InvariantCulture);
            }
            double redlineRatio = (spec.RedlineRpm.HasValue && oldRedline > 0) ? spec.RedlineRpm.Value / oldRedline : 1.0;
            CurvePlan curvePlan = null;
            if (schema.ContainsKey(Fm2CamTable) && (spec.Curve != null || redlineRatio != 1.0))
            {
                SqliteDatabase.Result stockCam = db.Query("SELECT RedlineRPM, TorqueCurveMaxRPM, TorqueCurveFullThrottleID FROM " + Fm2CamTable + " WHERE EngineID = ? AND IsStock = 1 ORDER BY Level LIMIT 1", old);
                if (stockCam.Rows.Count > 0)
                {
                    curvePlan = new CurvePlan();
                    curvePlan.OldStock = ReadCurve(db, Convert.ToInt64(stockCam.Rows[0][2], CultureInfo.InvariantCulture), Real(stockCam.Rows[0][1]));
                    curvePlan.Design = spec.Curve;
                    curvePlan.Ratio = redlineRatio;
                    curvePlan.StockRedline = Real(stockCam.Rows[0][0]) * redlineRatio;
                    curvePlan.Margin = 500.0;
                }
            }

            // Every part level of the engine, and the torque curves they point at.
            Dictionary<long, long> curves = new Dictionary<long, long>();
            List<string> copied = new List<string>();
            curveEngine = id;
            foreach (string table in partTables)
            {
                string key = DbIds.KeyColumn(db, table, schema[table], game);
                if (key == null)
                {
                    continue;
                }
                SqliteDatabase.Result rows = db.Query("SELECT * FROM " + SqliteDatabase.Quote(table) + " WHERE EngineID = ? ORDER BY " + SqliteDatabase.Quote(key), old);
                int keyIndex = Array.FindIndex(rows.Columns, delegate(string c) { return string.Equals(c, key, StringComparison.OrdinalIgnoreCase); });
                bool cams = string.Equals(table, Fm2CamTable, StringComparison.OrdinalIgnoreCase);
                foreach (object[] part in rows.Rows)
                {
                    object[] copy = (object[]) part.Clone();
                    long oldKey = Convert.ToInt64(copy[keyIndex], CultureInfo.InvariantCulture);
                    long newKey = (oldKey >= old * 1000 && oldKey < (old * 1000) + 1000) ? (id * 1000) + (oldKey - (old * 1000)) : DbIds.Next(db, table, key, game);
                    for (int i = 0; i < rows.Columns.Length; i++)
                    {
                        string column = rows.Columns[i];
                        if (i == keyIndex)
                        {
                            copy[i] = newKey;
                        }
                        else if (string.Equals(column, "EngineID", StringComparison.OrdinalIgnoreCase))
                        {
                            copy[i] = id;
                        }
                        else if (cams && redlineRatio != 1.0 && copy[i] != null && copy[i] != DBNull.Value && Real(copy[i]) > 0
                            && (column.EndsWith("RPM", StringComparison.OrdinalIgnoreCase) && !string.Equals(column, "StallRPM", StringComparison.OrdinalIgnoreCase)))
                        {
                            copy[i] = Real(copy[i]) * redlineRatio;     // RedlineRPM, StartRPM, TorqueCurveMaxRPM, the shift points
                        }
                        else if (cams && spec.IdleRpm.HasValue && string.Equals(column, "StallRPM", StringComparison.OrdinalIgnoreCase))
                        {
                            copy[i] = spec.IdleRpm.Value;
                        }
                        else if (column.StartsWith("TorqueCurve", StringComparison.OrdinalIgnoreCase) && column.EndsWith("ID", StringComparison.OrdinalIgnoreCase) && copy[i] != null && copy[i] != DBNull.Value)
                        {
                            long oldCurve = Convert.ToInt64(copy[i], CultureInfo.InvariantCulture);
                            long newCurve;
                            if (!curves.TryGetValue(oldCurve, out newCurve))
                            {
                                newCurve = DbIds.Next(db, "List_TorqueCurve", "TorqueCurveID", game);
                                CopyCurve(db, oldCurve, newCurve, 1.0, null, null, false, 1.0);
                                curves[oldCurve] = newCurve;
                            }
                            copy[i] = newCurve;
                        }
                    }
                    Insert(db, table, rows.Columns, copy);
                }
                if (rows.Rows.Count > 0)
                {
                    copied.Add(table.Replace("List_UpgradeEngine", "") + " " + rows.Rows.Count);
                }
            }
            curveEngine = -1;
            plan.Add(string.Format(CultureInfo.InvariantCulture, "engine: {0} part rows, every level ({1}), {2} torque curves{3}{4}",
                SumOf(copied), string.Join(", ", copied.ToArray()), curves.Count,
                (redlineRatio != 1.0) ? string.Format(CultureInfo.InvariantCulture, ", redline {0} -> {1} rpm", oldRedline, spec.RedlineRpm.Value) : "",
                spec.DisplacementCc.HasValue ? string.Format(CultureInfo.InvariantCulture, ", Disp_cc {0}", spec.DisplacementCc.Value) : ""));
            ConvertAspirationFm2(db, schema, id, oldAspiration, oldBar, newAspiration, newBar, plan);
            if (curvePlan != null)
            {
                RebuildCurves(db, id, curvePlan, plan, game, Fm2CamTable);
            }

            // The car's stock engine row and its engine combo point at the copy; the car's Data_Car numbers that mirror the engine.
            int moved = db.Execute("UPDATE List_UpgradeEngine SET EngineID = ? WHERE Ordinal = ? AND EngineID = ?", id, carId, old);
            plan.Add("engine: " + moved + " List_UpgradeEngine row of the car now point at engine " + id + " (the source car keeps " + old + ")");
            if (schema.ContainsKey("Combo_Engines"))
            {
                db.Execute("UPDATE Combo_Engines SET EngineID = ? WHERE Ordinal = ? AND EngineID = ?", id, carId, old);
            }
            UpdateSimulation(db, schema, carId, id, plan, Fm2CamTable, old);
            return id;
        }

        // FM2's forced induction tables (Id = EngineID * 1000 + n): the stock turbo / supercharger and its upgrade levels, and
        // for a natural engine the conversions it can buy. Checked on the game database 2026-11-15: turbo engines (aspiration 2-4)
        // have a stock List_UpgradeEngineTurbo row and a stock intercooler; DSC (5) / CSC (6) a stock row in their table and a
        // stock intercooler; natural ones (1) only non-stock rows (turbo / DSC / CSC conversions, intercooler levels). The stock
        // MaxScale (turbo) and ZeroRPMScale = RedlineRPMScale (DSC / CSC) are 1 + StockBoost-bar / 2, FM4's law.
        private static readonly string[] Fm2InductionTables = { "List_UpgradeEngineTurbo", "List_UpgradeEngineDSC", "List_UpgradeEngineCSC", "List_UpgradeEngineIntercooler" };

        // The copy's aspiration and boost. Same aspiration, other boost: the boost scales move by (1 + new / 2) / (1 + old / 2).
        // Another aspiration: the copy's forced induction rows are replaced by those of the FM2 engine of that aspiration whose
        // boost is the closest (natural: whose displacement is the closest), the boost scales moved to the copy's boost and the
        // turbo's PowerMaxScale (kW where the boost is full) by the ratio of the two engines' power.
        private static void ConvertAspirationFm2(SqliteDatabase db, Dictionary<string, List<string>> schema, long engine, long oldAspiration, double oldBar, long aspiration, double bar, List<string> plan)
        {
            ForzaGame game = ForzaGame.Motorsport2;
            if (aspiration == oldAspiration)
            {
                if (aspiration > 1 && Math.Abs(bar - oldBar) > 0.001 && oldBar > 0)
                {
                    double factor = (1 + (bar / 2)) / (1 + (oldBar / 2));
                    int rows = ScaleBoostFm2(db, schema, engine, factor);
                    plan.Add(string.Format(CultureInfo.InvariantCulture, "engine: boost {0} -> {1} bar, {2} turbo / supercharger rows scaled by {3:0.####} (MaxScale = 1 + bar / 2)", oldBar, bar, rows, factor));
                }
                return;
            }

            // Natural: every turbo / supercharger / intercooler row of the engine goes and none is copied from another
            // engine (as 78 of 199 FM2 natural engines and the M3 E92 DLC). Rows copied from a pattern engine (Dino) gave
            // PI 999 in game (PT Cruiser test 2026-11-15).
            if (aspiration == 1)
            {
                int dropped = 0;
                foreach (string table in Fm2InductionTables)
                {
                    if (schema.ContainsKey(table))
                    {
                        dropped += db.Execute("DELETE FROM " + table + " WHERE EngineID = ?", engine);
                    }
                }
                plan.Add(string.Format(CultureInfo.InvariantCulture, "engine: aspiration {0} -> 1: {1} turbo / supercharger / intercooler rows removed, none added (natural engine without forced induction upgrades)", oldAspiration, dropped));
                return;
            }

            // The pattern: an FM2 engine of that aspiration with stock rows in its table.
            string own = (aspiration >= 2 && aspiration <= 4) ? "List_UpgradeEngineTurbo" : (aspiration == 5) ? "List_UpgradeEngineDSC" : (aspiration == 6) ? "List_UpgradeEngineCSC" : null;
            double disp = Real(db.Scalar("SELECT Disp_cc FROM Data_Engine WHERE EngineID = ?", engine));
            double power = Real(db.Scalar("SELECT EngineGraphingMaxPower FROM Data_Engine WHERE EngineID = ?", engine));
            long pattern = -1;
            double best = double.MaxValue, patternBar = 0, patternPower = 0;
            foreach (object[] candidate in db.Query("SELECT EngineID, \"StockBoost-bar\", Disp_cc, EngineGraphingMaxPower FROM Data_Engine WHERE AspirationID_Stock = ? AND EngineID <> ? ORDER BY EngineID", aspiration, engine).Rows)
            {
                long cid = Convert.ToInt64(candidate[0], CultureInfo.InvariantCulture);
                bool usable = (own != null)
                    ? schema.ContainsKey(own) && Convert.ToInt64(db.Scalar("SELECT count(*) FROM " + own + " WHERE EngineID = ? AND IsStock = 1", cid), CultureInfo.InvariantCulture) > 0
                    : schema.ContainsKey("List_UpgradeEngineTurbo") && Convert.ToInt64(db.Scalar("SELECT count(*) FROM List_UpgradeEngineTurbo WHERE EngineID = ?", cid), CultureInfo.InvariantCulture) > 0;
                if (!usable)
                {
                    continue;
                }
                double distance = (own != null) ? Math.Abs(Real(candidate[1]) - bar) : Math.Abs(Real(candidate[2]) - disp) / 1000.0;
                if (distance < best)
                {
                    best = distance;
                    pattern = cid;
                    patternBar = Real(candidate[1]);
                    patternPower = Real(candidate[3]);
                }
            }
            if (pattern < 0)
            {
                throw new InvalidOperationException("FM2: no engine of aspiration " + aspiration + " has forced induction rows to copy.");
            }

            int removed = 0, added = 0;
            foreach (string table in Fm2InductionTables)
            {
                if (schema.ContainsKey(table))
                {
                    removed += db.Execute("DELETE FROM " + table + " WHERE EngineID = ?", engine);
                }
            }
            double boostFactor = (aspiration > 1 && patternBar > 0) ? (1 + (bar / 2)) / (1 + (patternBar / 2)) : 1.0;
            double powerFactor = (power > 0 && patternPower > 0) ? power / patternPower : 1.0;
            foreach (string table in Fm2InductionTables)
            {
                if (!schema.ContainsKey(table))
                {
                    continue;
                }
                string key = DbIds.KeyColumn(db, table, schema[table], game) ?? "Id";
                SqliteDatabase.Result rows = db.Query("SELECT * FROM " + table + " WHERE EngineID = ? ORDER BY " + SqliteDatabase.Quote(key), pattern);
                int keyIndex = Array.FindIndex(rows.Columns, delegate(string c) { return string.Equals(c, key, StringComparison.OrdinalIgnoreCase); });
                int zeroIndex = Array.FindIndex(rows.Columns, delegate(string c) { return string.Equals(c, "ZeroRPMScale", StringComparison.OrdinalIgnoreCase); });
                int redIndex = Array.FindIndex(rows.Columns, delegate(string c) { return string.Equals(c, "RedlineRPMScale", StringComparison.OrdinalIgnoreCase); });
                foreach (object[] part in rows.Rows)
                {
                    object[] copy = (object[]) part.Clone();
                    // A CSC's ZeroRPMScale (238 of 243 rows below its RedlineRPMScale: little boost at low rpm) stays; a DSC's
                    // (277 of 277 equal to RedlineRPMScale) follows the boost.
                    bool zeroFollows = zeroIndex >= 0 && redIndex >= 0 && Math.Abs(Real(part[zeroIndex]) - Real(part[redIndex])) < 0.001;
                    for (int i = 0; i < rows.Columns.Length; i++)
                    {
                        string column = rows.Columns[i];
                        if (i == zeroIndex && !zeroFollows)
                        {
                            continue;
                        }
                        if (i == keyIndex)
                        {
                            long oldKey = Convert.ToInt64(copy[i], CultureInfo.InvariantCulture);
                            long n = (oldKey >= pattern * 1000 && oldKey < (pattern * 1000) + 1000) ? oldKey - (pattern * 1000) : 0;
                            while (Convert.ToInt64(db.Scalar("SELECT count(*) FROM " + table + " WHERE " + SqliteDatabase.Quote(key) + " = ?", (engine * 1000) + n), CultureInfo.InvariantCulture) > 0)
                            {
                                n++;
                            }
                            copy[i] = (engine * 1000) + n;
                        }
                        else if (string.Equals(column, "EngineID", StringComparison.OrdinalIgnoreCase))
                        {
                            copy[i] = engine;
                        }
                        else if (copy[i] is double && (string.Equals(column, "MaxScale", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(column, "ZeroRPMScale", StringComparison.OrdinalIgnoreCase) || string.Equals(column, "RedlineRPMScale", StringComparison.OrdinalIgnoreCase)))
                        {
                            copy[i] = Math.Round((double) copy[i] * boostFactor, 4);
                        }
                        else if (copy[i] is double && string.Equals(column, "PowerMaxScale", StringComparison.OrdinalIgnoreCase))
                        {
                            copy[i] = Math.Round((double) copy[i] * powerFactor, 2);
                        }
                    }
                    Insert(db, table, rows.Columns, copy);
                    added++;
                }
            }
            plan.Add(string.Format(CultureInfo.InvariantCulture,
                "engine: aspiration {0} -> {1}: {2} turbo / supercharger / intercooler rows removed, {3} made on the pattern of engine {4} ({5} bar; boost scales x {6:0.####}, turbo PowerMaxScale x {7:0.###} for the power)",
                oldAspiration, aspiration, removed, added, pattern, patternBar, boostFactor, powerFactor));
        }

        // Every turbo MaxScale and supercharger ZeroRPMScale / RedlineRPMScale of the engine times factor; the rows changed.
        private static int ScaleBoostFm2(SqliteDatabase db, Dictionary<string, List<string>> schema, long engine, double factor)
        {
            int rows = 0;
            if (schema.ContainsKey("List_UpgradeEngineTurbo"))
            {
                rows += db.Execute("UPDATE List_UpgradeEngineTurbo SET MaxScale = round(MaxScale * ?, 4) WHERE EngineID = ?", factor, engine);
            }
            foreach (string table in new string[] { "List_UpgradeEngineDSC", "List_UpgradeEngineCSC" })
            {
                if (schema.ContainsKey(table))
                {
                    // ZeroRPMScale only where it equals RedlineRPMScale (every DSC row; a CSC's lower one stays).
                    rows += db.Execute("UPDATE " + table + " SET ZeroRPMScale = CASE WHEN abs(ZeroRPMScale - RedlineRPMScale) < 0.001 THEN round(ZeroRPMScale * ?, 4) ELSE ZeroRPMScale END, RedlineRPMScale = round(RedlineRPMScale * ?, 4) WHERE EngineID = ?", factor, factor, engine);
                }
            }
            return rows;
        }
    }

    // FM2's own gearbox: FM2 has no drivetrain table, the car's List_UpgradeTransmission rows (car * 1000 + level, copied for the
    // copy already) are its gearbox: GearRatio0 = reverse, GearRatio1..7 (-1 = none), NumGears counts the reverse,
    // Data_Car.StockGears the forward gears. The stock level takes the ratios asked for; the upgrade levels keep their shape
    // (each gear scaled by the stock one's change) with the same number of gears, else take the ratios asked for.
    public static partial class CarGearbox
    {
        public static void ApplyFm2(SqliteDatabase db, long carId, GearboxSpec spec, List<string> plan)
        {
            if (spec.Forward == null || spec.Forward.Length < 1 || spec.Forward.Length > 7)
            {
                throw new InvalidOperationException("An FM2 gearbox has 1 to 7 forward gears.");
            }
            foreach (double ratio in spec.Forward)
            {
                if (!(ratio > 0))
                {
                    throw new InvalidOperationException("Every forward gear ratio must be above 0.");
                }
            }
            if (!(spec.FinalDrive > 0))
            {
                throw new InvalidOperationException("The final drive ratio must be above 0.");
            }
            SqliteDatabase.Result parts = db.Query("SELECT * FROM List_UpgradeTransmission WHERE Ordinal = ? ORDER BY Level", carId);
            if (parts.Rows.Count == 0)
            {
                throw new InvalidOperationException("The car has no List_UpgradeTransmission rows.");
            }
            GearboxSpec stock = ReadFm2(db, carId);
            int count = spec.Forward.Length;
            bool sameCount = stock != null && stock.Forward.Length == count;
            foreach (object[] part in parts.Rows)
            {
                object[] row = (object[]) part.Clone();
                bool isStock = Convert.ToInt64(Get(parts.Columns, row, "IsStock"), CultureInfo.InvariantCulture) == 1;
                double[] forward = (double[]) spec.Forward.Clone();
                double reverse = spec.Reverse, finalDrive = spec.FinalDrive;
                if (!isStock && sameCount)
                {
                    for (int i = 0; i < count; i++)
                    {
                        forward[i] = Real(Get(parts.Columns, row, "GearRatio" + (i + 1))) * spec.Forward[i] / stock.Forward[i];
                    }
                    finalDrive = Real(Get(parts.Columns, row, "FinalDriveRatio")) * spec.FinalDrive / stock.FinalDrive;
                    reverse = Real(Get(parts.Columns, row, "GearRatio0")) * spec.Reverse / stock.Reverse;
                }
                List<string> sets = new List<string>();
                List<object> values = new List<object>();
                sets.Add("NumGears = ?");
                values.Add((long) (count + 1));
                sets.Add("FinalDriveRatio = ?");
                values.Add(Math.Round(finalDrive, 3));
                sets.Add("GearRatio0 = ?");
                values.Add(Math.Round(reverse, 3));
                for (int i = 1; i <= 7; i++)
                {
                    sets.Add("GearRatio" + i + " = ?");
                    values.Add((i <= count) ? Math.Round(forward[i - 1], 3) : -1.0);
                }
                values.Add(Convert.ToInt64(Get(parts.Columns, row, "Id"), CultureInfo.InvariantCulture));
                db.Execute("UPDATE List_UpgradeTransmission SET " + string.Join(", ", sets.ToArray()) + " WHERE Id = ?", values.ToArray());
            }
            db.Execute("UPDATE Data_Car SET StockGears = ? WHERE Id = ?", (long) count, carId);
            plan.Add(string.Format(CultureInfo.InvariantCulture, "gearbox: the copy's {0} List_UpgradeTransmission levels, {1} gears, final drive {2:0.###}, reverse {3:0.###} (FM2: the car's own rows; Data_Car.StockGears {1})",
                parts.Rows.Count, count, spec.FinalDrive, spec.Reverse));
        }

        // The stock gearbox of an FM2 car (List_UpgradeTransmission, IsStock), or null.
        public static GearboxSpec ReadFm2(SqliteDatabase db, long carId)
        {
            SqliteDatabase.Result r = db.Query("SELECT FinalDriveRatio, NumGears, GearRatio0, GearRatio1, GearRatio2, GearRatio3, GearRatio4, GearRatio5, GearRatio6, GearRatio7 FROM List_UpgradeTransmission WHERE Ordinal = ? AND IsStock = 1 ORDER BY Level LIMIT 1", carId);
            if (r.Rows.Count == 0)
            {
                return null;
            }
            object[] row = r.Rows[0];
            GearboxSpec spec = new GearboxSpec();
            spec.FinalDrive = Real(row[0]);
            spec.Reverse = Real(row[2]);
            List<double> gears = new List<double>();
            for (int i = 3; i < 10; i++)
            {
                if (Real(row[i]) > 0)
                {
                    gears.Add(Real(row[i]));
                }
            }
            spec.Forward = gears.ToArray();
            return spec;
        }
    }
}
