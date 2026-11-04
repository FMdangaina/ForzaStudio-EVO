namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    // What the user asks of a car's own engine (Duplicate Car, ROADMAP 2.3.2).
    public sealed class EngineSpec
    {
        public string Name;                                 // Data_Engine.EngineName; null = the source engine's
        public Dictionary<string, object> Data = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);   // Data_Engine column -> new value
        public double? DisplacementCc;                      // the stock displacement; the upgrade levels scale with it
        public double[] Curve;                              // the stock curve as base N.m (before the parts and the boost), one value per 100 rpm from 0 rpm, to the redline + the margin; null = the source's, stretched with the redline
        public double? RedlineRpm;                          // the stock redline; the upgrade levels and the curve's rpm scale with it
        public double? IdleRpm;                             // StallRPM of every camshaft level of the copy
        public double? TurboVolume;                         // the engine tuning file's TurboVolume (the whistle's level); null = the sound engine's (0.5 when it has none)
        public int BovLevel;                                // the blow-off sample of turbos.fev ("L<level>_BlowOff_<choice>") written to the file's three blowoff slots; 0 = the sound engine's own
        public int BovChoice;
    }

    // A copy of a car's stock engine under an EngineID of its own (the Duplicate Car wizard shares the source's
    // engine unless it is asked for this): the Data_Engine row, every List_UpgradeEngine* row of the engine and
    // the torque curves its camshafts point at. FM4 numbers the rows of an engine EngineID * 1000 + n (camshaft
    // Id = its torque curve's id), kept here; the copy's ids are above every id of FM4 and its DLC (the first
    // free EngineID, which is above 933). Run inside the caller's transaction, before the integrity check.
    public static class CarEngine
    {
        // The stock engine's id of a car, or -1.
        public static long StockEngine(SqliteDatabase db, long carId)
        {
            object id = db.Scalar("SELECT EngineID FROM List_UpgradeEngine WHERE Ordinal = ? AND IsStock = 1 ORDER BY Level LIMIT 1", carId);
            return (id == null || id == DBNull.Value) ? -1 : Convert.ToInt64(id, CultureInfo.InvariantCulture);
        }

        public static long Clone(SqliteDatabase db, long carId, EngineSpec spec, List<string> plan)
        {
            ForzaGame game = ForzaGame.Motorsport4;
            long old = StockEngine(db, carId);
            if (old < 0)
            {
                throw new InvalidOperationException("The car has no stock engine in List_UpgradeEngine.");
            }
            Dictionary<string, List<string>> schema = DbIds.Schema(db);
            List<string> partTables = new List<string>();
            foreach (KeyValuePair<string, List<string>> table in schema)
            {
                if (table.Key != "Data_Engine" && table.Key != "List_UpgradeEngine" && table.Key != "Combo_Engines" && table.Key != "List_TorqueCurve" && table.Value.Exists(delegate(string c) { return string.Equals(c, "EngineID", StringComparison.OrdinalIgnoreCase); }))
                {
                    partTables.Add(table.Key);
                }
            }
            partTables.Sort(StringComparer.OrdinalIgnoreCase);

            // The new EngineID: free, and EngineID * 1000 + 0..999 above FM4's largest key and free in every part table.
            long id = DbIds.Next(db, "Data_Engine", "EngineID", game);
            List<string> keyed = new List<string>(partTables);
            keyed.Add("List_TorqueCurve");
            while (true)
            {
                bool free = true;
                foreach (string table in keyed)
                {
                    string key = DbIds.KeyColumn(db, table, schema[table]);
                    if (key == null)
                    {
                        continue;
                    }
                    long limit = GameIdLimits.Max(game, table, key);
                    string k = SqliteDatabase.Quote(key);
                    free = free && id * 1000 > limit
                        && Convert.ToInt64(db.Scalar("SELECT count(*) FROM " + SqliteDatabase.Quote(table) + " WHERE " + k + " BETWEEN ? AND ?", id * 1000, (id * 1000) + 999), CultureInfo.InvariantCulture) == 0;
                }
                if (free)
                {
                    break;
                }
                id++;
            }

            // Data_Engine.
            SqliteDatabase.Result engine = db.Query("SELECT * FROM Data_Engine WHERE EngineID = ?", old);
            if (engine.Rows.Count == 0)
            {
                throw new InvalidOperationException("Data_Engine has no engine " + old + ".");
            }
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
                else if (spec.Data.TryGetValue(column, out value))
                {
                    row[i] = value;
                }
                else if ((string.Equals(column, "EngineGraphingMaxTorque", StringComparison.OrdinalIgnoreCase) || string.Equals(column, "EngineGraphingMaxPower", StringComparison.OrdinalIgnoreCase)) && row[i] is double)
                {
                    row[i] = (double) row[i];
                }
            }
            Insert(db, "Data_Engine", engine.Columns, row);
            // A diesel's engine inertia is double a petrol one's (user 2026-10-25; FM4's 8 diesels: 1.6 - 1.8 times per kg; the flywheel is the
            // same): a copy made diesel from a petrol engine gets it, one that is diesel already keeps its own.
            int dieselColumn = Array.FindIndex(engine.Columns, delegate(string c) { return string.Equals(c, "Diesel", StringComparison.OrdinalIgnoreCase); });
            int inertiaColumn = Array.FindIndex(engine.Columns, delegate(string c) { return string.Equals(c, "MomentInertia", StringComparison.OrdinalIgnoreCase); });
            if (dieselColumn >= 0 && inertiaColumn >= 0 && Real(row[dieselColumn]) != 0 && Real(engine.Rows[0][dieselColumn]) == 0 && !spec.Data.ContainsKey("MomentInertia"))
            {
                db.Execute("UPDATE Data_Engine SET MomentInertia = MomentInertia * 2 WHERE EngineID = ?", id);
                plan.Add(string.Format(CultureInfo.InvariantCulture, "engine: diesel, inertia doubled ({0:0.####} -> {1:0.####})", Real(row[inertiaColumn]), Real(row[inertiaColumn]) * 2));
            }
            plan.Add(string.Format(CultureInfo.InvariantCulture, "engine: Data_Engine {0} copied as {1}{2}", old, id, string.IsNullOrEmpty(spec.Name) ? "" : " \"" + spec.Name + "\""));

            // The car's Data_Car numbers that mirror the engine.
            Dictionary<string, string> carColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            carColumns["CylinderID"] = "CylinderID";
            carColumns["ConfigID"] = "EngineConfigID";
            carColumns["AspirationID_Stock"] = "AspirationTypeId";
            foreach (KeyValuePair<string, string> pair in carColumns)
            {
                object value;
                if (spec.Data.TryGetValue(pair.Key, out value) && schema["Data_Car"].Exists(delegate(string c) { return string.Equals(c, pair.Value, StringComparison.OrdinalIgnoreCase); }))
                {
                    db.Execute("UPDATE Data_Car SET " + SqliteDatabase.Quote(pair.Value) + " = ? WHERE Id = ?", value, carId);
                }
            }

            // The upgrade parts and the torque curves.
            double oldDisplacement = 0;
            object stockDisplacement = schema.ContainsKey("List_UpgradeEngineDisplacement") ? db.Scalar("SELECT Disp FROM List_UpgradeEngineDisplacement WHERE EngineID = ? AND IsStock = 1 ORDER BY Level LIMIT 1", old) : null;
            if (stockDisplacement != null && stockDisplacement != DBNull.Value)
            {
                oldDisplacement = Convert.ToDouble(stockDisplacement, CultureInfo.InvariantCulture);
            }
            double displacementRatio = (spec.DisplacementCc.HasValue && oldDisplacement > 0) ? spec.DisplacementCc.Value / oldDisplacement : 1.0;
            double oldRedline = 0;
            object stockRedline = schema.ContainsKey("List_UpgradeEngineCamshaft") ? db.Scalar("SELECT RedlineRPM FROM List_UpgradeEngineCamshaft WHERE EngineID = ? AND IsStock = 1 ORDER BY Level LIMIT 1", old) : null;
            if (stockRedline != null && stockRedline != DBNull.Value)
            {
                oldRedline = Convert.ToDouble(stockRedline, CultureInfo.InvariantCulture);
            }
            double redlineRatio = (spec.RedlineRpm.HasValue && oldRedline > 0) ? spec.RedlineRpm.Value / oldRedline : 1.0;
            Dictionary<long, long> curves = new Dictionary<long, long>();
            // The torque curves. FM4 keeps one value per 100 rpm from 0 rpm and ends the list with a marker (-2.63) at
            // TorqueCurveMaxRPM, NumTorqueValues / NumRPMEntriesArray counting it (61 values = 6000 rpm). The copy gets only the
            // stock parts of the source (the stock level is what the user designs); the upgrade levels come from the generic
            // rules (CarUpgrades, Upgrades.ini). Once they exist, every camshaft level's curve is built on that grid from the
            // designed stock curve (RebuildCurves): values up to the highest level's redline + 300 rpm (diesel) or 500 (petrol),
            // 0 above a level's own limit, the marker right after.
            int dieselAt = Array.FindIndex(engine.Columns, delegate(string c) { return string.Equals(c, "Diesel", StringComparison.OrdinalIgnoreCase); });
            double margin = (dieselAt >= 0 && Real(row[dieselAt]) != 0) ? 300.0 : 500.0;
            CurvePlan curvePlan = null;
            if (schema.ContainsKey("List_UpgradeEngineCamshaft"))
            {
                SqliteDatabase.Result stockCam = db.Query("SELECT RedlineRPM, TorqueCurveMaxRPM, TorqueCurveFullThrottleID FROM List_UpgradeEngineCamshaft WHERE EngineID = ? AND IsStock = 1 ORDER BY Level LIMIT 1", old);
                if (stockCam.Rows.Count > 0)
                {
                    OldCurve oldStock = ReadCurve(db, Convert.ToInt64(stockCam.Rows[0][2], CultureInfo.InvariantCulture), Real(stockCam.Rows[0][1]));
                    curvePlan = new CurvePlan();
                    curvePlan.OldStock = oldStock;
                    curvePlan.Design = spec.Curve;
                    curvePlan.Ratio = redlineRatio;
                    curvePlan.StockRedline = Real(stockCam.Rows[0][0]) * redlineRatio;
                    curvePlan.Margin = margin;
                    if (spec.Curve != null)
                    {
                        double newPeak = 0, oldPeak = 0;
                        foreach (double v in spec.Curve)
                        {
                            newPeak = Math.Max(newPeak, v);
                        }
                        foreach (double v in oldStock.Values)
                        {
                            oldPeak = Math.Max(oldPeak, v * oldStock.Scale);
                        }
                        if (newPeak > 0 && oldPeak > 0)
                        {
                            // The graph limits of the engine follow its peak.
                            db.Execute("UPDATE Data_Engine SET EngineGraphingMaxTorque = EngineGraphingMaxTorque * ?, EngineGraphingMaxPower = EngineGraphingMaxPower * ? WHERE EngineID = ?", newPeak / oldPeak, newPeak / oldPeak, id);
                        }
                    }
                }
            }
            // A changed boost moves every turbo level's MaxScale by FM4's law (MaxScale = 1 + bar / 2, true on all 137 turbo cars).
            double boostRatio = 1.0;
            object newBar, newAsp;
            int barIndex = Array.FindIndex(engine.Columns, delegate(string c) { return string.Equals(c, "StockBoost-bar", StringComparison.OrdinalIgnoreCase); });
            int aspIndex = Array.FindIndex(engine.Columns, delegate(string c) { return string.Equals(c, "AspirationID_Stock", StringComparison.OrdinalIgnoreCase); });
            if (barIndex >= 0 && aspIndex >= 0 && spec.Data.TryGetValue("StockBoost-bar", out newBar))
            {
                double was = Real(engine.Rows[0][barIndex]);
                double now = Real(newBar);
                long asp = spec.Data.TryGetValue("AspirationID_Stock", out newAsp) ? Convert.ToInt64(newAsp, CultureInfo.InvariantCulture) : Convert.ToInt64(engine.Rows[0][aspIndex], CultureInfo.InvariantCulture);
                if ((asp == 2 || asp == 3) && was > 0)
                {
                    boostRatio = (1.0 + (now / 2.0)) / (1.0 + (was / 2.0));
                }
            }
            List<string> copied = new List<string>();
            foreach (string table in partTables)
            {
                List<string> columns = schema[table];
                string key = DbIds.KeyColumn(db, table, columns);
                if (key == null)
                {
                    continue;
                }
                SqliteDatabase.Result rows = db.Query("SELECT * FROM " + SqliteDatabase.Quote(table) + " WHERE EngineID = ?", old);
                int count = 0;
                foreach (object[] part in rows.Rows)
                {
                    int stockColumn = Array.FindIndex(rows.Columns, delegate(string c) { return string.Equals(c, "IsStock", StringComparison.OrdinalIgnoreCase); });
                    if (stockColumn >= 0 && Convert.ToInt64(part[stockColumn], CultureInfo.InvariantCulture) != 1)
                    {
                        continue;       // only the stock part: the upgrade levels are generated (CarUpgrades.ApplyEngine)
                    }
                    object[] copy = (object[]) part.Clone();
                    long oldKey = 0;
                    int keyIndex = Array.FindIndex(rows.Columns, delegate(string c) { return string.Equals(c, key, StringComparison.OrdinalIgnoreCase); });
                    oldKey = Convert.ToInt64(copy[keyIndex], CultureInfo.InvariantCulture);
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
                        else if (string.Equals(table, "List_UpgradeEngineDisplacement", StringComparison.OrdinalIgnoreCase) && string.Equals(column, "Disp", StringComparison.OrdinalIgnoreCase) && copy[i] != null && copy[i] != DBNull.Value)
                        {
                            copy[i] = (long) Math.Round(Convert.ToDouble(copy[i], CultureInfo.InvariantCulture) * displacementRatio);
                        }
                        else if (string.Equals(table, "List_UpgradeEngineCamshaft", StringComparison.OrdinalIgnoreCase) && redlineRatio != 1.0
                            && (string.Equals(column, "RedlineRPM", StringComparison.OrdinalIgnoreCase) || string.Equals(column, "StartRPM", StringComparison.OrdinalIgnoreCase) || string.Equals(column, "TorqueCurveMaxRPM", StringComparison.OrdinalIgnoreCase))
                            && copy[i] != null && copy[i] != DBNull.Value)
                        {
                            copy[i] = Convert.ToDouble(copy[i], CultureInfo.InvariantCulture) * redlineRatio;
                        }
                        else if (boostRatio != 1.0 && table.StartsWith("List_UpgradeEngineTurbo", StringComparison.OrdinalIgnoreCase) && string.Equals(column, "MaxScale", StringComparison.OrdinalIgnoreCase) && copy[i] is double)
                        {
                            copy[i] = (double) copy[i] * boostRatio;
                        }
                        else if (string.Equals(table, "List_UpgradeEngineCamshaft", StringComparison.OrdinalIgnoreCase) && spec.IdleRpm.HasValue && string.Equals(column, "StallRPM", StringComparison.OrdinalIgnoreCase))
                        {
                            copy[i] = spec.IdleRpm.Value;
                        }
                        else if (column.StartsWith("TorqueCurve", StringComparison.OrdinalIgnoreCase) && column.EndsWith("ID", StringComparison.OrdinalIgnoreCase) && copy[i] != null && copy[i] != DBNull.Value)
                        {
                            long oldCurve = Convert.ToInt64(copy[i], CultureInfo.InvariantCulture);
                            long newCurve;
                            if (!curves.TryGetValue(oldCurve, out newCurve))
                            {
                                newCurve = (oldCurve == oldKey) ? newKey : (id * 1000) + 900 + curves.Count;
                                if (Convert.ToInt64(db.Scalar("SELECT count(*) FROM List_TorqueCurve WHERE TorqueCurveID = ?", newCurve), CultureInfo.InvariantCulture) > 0)
                                {
                                    newCurve = DbIds.Next(db, "List_TorqueCurve", "TorqueCurveID", game);
                                }
                                CopyCurve(db, oldCurve, newCurve, 1.0, null, null, false, 1.0);       // rebuilt on FM4's grid by RebuildCurves
                                curves[oldCurve] = newCurve;
                            }
                            copy[i] = newCurve;
                        }
                    }
                    Insert(db, table, rows.Columns, copy);
                    count++;
                }
                if (count > 0)
                {
                    copied.Add(table.Replace("List_UpgradeEngine", "") + " " + count);
                }
            }
            plan.Add(string.Format(CultureInfo.InvariantCulture, "engine: {0} stock part rows ({1}){2}{3}",
                SumOf(copied), string.Join(", ", copied.ToArray()),
                (displacementRatio != 1.0) ? string.Format(CultureInfo.InvariantCulture, ", displacement {0} -> {1} cc", oldDisplacement, spec.DisplacementCc.Value) : "",
                (redlineRatio != 1.0) ? string.Format(CultureInfo.InvariantCulture, ", redline {0} -> {1} rpm", oldRedline, spec.RedlineRpm.Value) : ""));

            // The stock turbo (made, moved or taken away to match the aspiration chosen). Its PowerMaxScale is a power in kW: the
            // boost builds up with the engine's power and is full from there. FM4's turbos (137 cars): PowerMaxScale = the power
            // the car makes at its torque-peak rpm (median 1.04 times it for a single turbo, 0.6 for a twin), so the torque plateau
            // starts as soon as the boost is full. The copy gets that from its own curve (the rows it was copied from are another
            // engine's power). The upgrade levels follow it by the generic ratios.
            ConvertAspiration(db, schema, id, Convert.ToInt64(engine.Rows[0][Math.Max(0, aspIndex)], CultureInfo.InvariantCulture), plan);
            ConvertSupercharger(db, schema, id, Convert.ToInt64(engine.Rows[0][Math.Max(0, aspIndex)], CultureInfo.InvariantCulture), plan);
            ConvertManifold(db, schema, id, plan);
            if (curvePlan != null)
            {
                double fullBoostKw = KilowattsAtTorquePeak(db, id, curvePlan.NewStock((int) Math.Ceiling((curvePlan.StockRedline + margin) / 100.0) + 1), curvePlan.StockRedline);
                if (fullBoostKw > 0)
                {
                    foreach (string turbo in new string[] { "List_UpgradeEngineTurboSingle", "List_UpgradeEngineTurboTwin", "List_UpgradeEngineTurboQuad" })
                    {
                        if (schema.ContainsKey(turbo))
                        {
                            double share = turbo.EndsWith("Single", StringComparison.Ordinal) ? 1.0 : 0.6;
                            db.Execute("UPDATE " + SqliteDatabase.Quote(turbo) + " SET PowerMaxScale = ? WHERE EngineID = ? AND IsStock = 1", Math.Round(fullBoostKw * share, 2), id);
                        }
                    }
                    plan.Add(string.Format(CultureInfo.InvariantCulture, "engine: turbo PowerMaxScale = {0:0.#} kW (single) / {1:0.#} kW (twin): the power at the torque peak, the boost is full from there", fullBoostKw, fullBoostKw * 0.6));
                }
            }

            // The upgrade levels: the generic rules of the traffic cars (Upgrades.ini), on the stock parts above. A diesel gets
            // no supercharger, only (naturally aspirated one) the turbo.
            List<string> notes = new List<string>();
            CarUpgrades generic = new CarUpgrades(db, carId, CarUpgrades.LoadIni(notes));
            generic.ApplyEngine(id);
            foreach (string line in notes)
            {
                plan.Add("engine: " + line);
            }
            foreach (string line in generic.Lines)
            {
                plan.Add("engine upgrades:" + line);
            }
            if (curvePlan != null)
            {
                RebuildCurves(db, id, curvePlan, plan);
            }

            // The car's stock engine row points at the copy; the swap engines stay as they are.
            int moved = db.Execute("UPDATE List_UpgradeEngine SET EngineID = ? WHERE Ordinal = ? AND EngineID = ?", id, carId, old);
            plan.Add("engine: " + moved + " List_UpgradeEngine row of the car now point at engine " + id + " (the source car keeps " + old + ")");
            if (schema.ContainsKey("Combo_Engines"))
            {
                // The old engine table: the car's row (not the rows of the other cars that use the engine).
                db.Execute("UPDATE Combo_Engines SET EngineID = ? WHERE Ordinal = ? AND EngineID = ?", id, carId, old);
            }
            // Data_Car.Displacement = the stock displacement row's Disp (equal on all 502 FM4 cars); the showroom reads it from there.
            if (schema["Data_Car"].Exists(delegate(string c) { return string.Equals(c, "Displacement", StringComparison.OrdinalIgnoreCase); }) && schema.ContainsKey("List_UpgradeEngineDisplacement"))
            {
                object disp = db.Scalar("SELECT Disp FROM List_UpgradeEngineDisplacement WHERE EngineID = ? AND IsStock = 1 ORDER BY Level LIMIT 1", id);
                if (disp != null && disp != DBNull.Value)
                {
                    db.Execute("UPDATE Data_Car SET Displacement = ? WHERE Id = ?", disp, carId);
                    plan.Add("engine: Data_Car.Displacement = " + Convert.ToString(disp, CultureInfo.InvariantCulture) + " cc (what the showroom shows)");
                }
            }
            UpdateSimulation(db, schema, carId, id, plan);
            return id;
        }


        // The supercharger table of an aspiration (5 = positive displacement, 6 = centrifugal), or null.
        private static string SuperTable(long aspiration)
        {
            return (aspiration == 5) ? "List_UpgradeEngineDSC" : (aspiration == 6) ? "List_UpgradeEngineCSC" : null;
        }

        // The copy's supercharger rows follow its aspiration. FM4's supercharged engines (26 DSC, 3 CSC) have a stock row
        // (IsStock = 1, ZeroRPMScale = RedlineRPMScale = 1 + boost / 2 for the DSC; the CSC's redline scale is the same) and
        // two upgrade levels; a natural engine only has conversion levels (IsStock = 0). An engine made supercharged takes the
        // rows of the FM4 engine of that aspiration whose boost is the closest, the scales moved by the ratio of the two
        // (1 + bar / 2); an engine made natural or turbo loses its supercharger rows, the generic rules add the conversions.
        private static void ConvertSupercharger(SqliteDatabase db, Dictionary<string, List<string>> schema, long engine, long oldAspiration, List<string> plan)
        {
            SqliteDatabase.Result now = db.Query("SELECT AspirationID_Stock, \"StockBoost-bar\" FROM Data_Engine WHERE EngineID = ?", engine);
            long aspiration = Convert.ToInt64(now.Rows[0][0], CultureInfo.InvariantCulture);
            double bar = Real(now.Rows[0][1]);
            string to = SuperTable(aspiration);
            if (aspiration == oldAspiration || (to == null && SuperTable(oldAspiration) == null))
            {
                return;
            }
            foreach (string table in new string[] { "List_UpgradeEngineCSC", "List_UpgradeEngineDSC" })
            {
                if (schema.ContainsKey(table))
                {
                    int removed = db.Execute("DELETE FROM " + SqliteDatabase.Quote(table) + " WHERE EngineID = ?", engine);
                    if (removed > 0)
                    {
                        plan.Add("engine: " + removed + " supercharger rows of " + table + " removed (the aspiration changed)");
                    }
                }
            }
            if (to == null || !schema.ContainsKey(to))
            {
                return;
            }
            long source = -1;
            double sourceBar = 0, bestGap = double.MaxValue;
            foreach (object[] candidate in db.Query("SELECT EngineID, \"StockBoost-bar\" FROM Data_Engine WHERE AspirationID_Stock = ?", aspiration).Rows)
            {
                long candidateId = Convert.ToInt64(candidate[0], CultureInfo.InvariantCulture);
                double gap = Math.Abs(Real(candidate[1]) - bar);
                if (candidateId == engine || gap >= bestGap
                    || Convert.ToInt64(db.Scalar("SELECT count(*) FROM " + SqliteDatabase.Quote(to) + " WHERE EngineID = ? AND IsStock = 1", candidateId), CultureInfo.InvariantCulture) == 0)
                {
                    continue;
                }
                source = candidateId;
                sourceBar = Real(candidate[1]);
                bestGap = gap;
            }
            if (source < 0)
            {
                throw new InvalidOperationException("No FM4 engine has supercharger rows in " + to + " to copy.");
            }
            double ratio = (1.0 + (bar / 2.0)) / (1.0 + (sourceBar / 2.0));
            SqliteDatabase.Result pattern = db.Query("SELECT * FROM " + SqliteDatabase.Quote(to) + " WHERE EngineID = ? ORDER BY Level", source);
            string key = DbIds.KeyColumn(db, to, schema[to]);
            foreach (object[] row in pattern.Rows)
            {
                object[] copy = (object[]) row.Clone();
                for (int i = 0; i < pattern.Columns.Length; i++)
                {
                    string column = pattern.Columns[i];
                    if (string.Equals(column, key, StringComparison.OrdinalIgnoreCase))
                    {
                        copy[i] = (engine * 1000) + (Convert.ToInt64(row[i], CultureInfo.InvariantCulture) % 1000);
                    }
                    else if (string.Equals(column, "EngineID", StringComparison.OrdinalIgnoreCase))
                    {
                        copy[i] = engine;
                    }
                    else if (copy[i] is double && (string.Equals(column, "RedlineRPMScale", StringComparison.OrdinalIgnoreCase)
                        || (aspiration == 5 && string.Equals(column, "ZeroRPMScale", StringComparison.OrdinalIgnoreCase))))
                    {
                        copy[i] = Math.Round((double) copy[i] * ratio, 4);
                    }
                }
                Insert(db, to, pattern.Columns, copy);
            }
            plan.Add(string.Format(CultureInfo.InvariantCulture, "engine: {0} supercharger rows made in {1} ({2} bar, scales x{3:0.###}) on the pattern of engine {4} ({5} bar)", pattern.Rows.Count, to, bar, ratio, source, sourceBar));
        }

        // The turbo table of an aspiration (FM4 keeps the stock turbo and its upgrade levels in one of three tables by
        // turbo count; superchargers and natural engines have no rows there), or null.
        private static string TurboTable(long aspiration)
        {
            return (aspiration == 2) ? "List_UpgradeEngineTurboSingle" : (aspiration == 3) ? "List_UpgradeEngineTurboTwin" : (aspiration == 4) ? "List_UpgradeEngineTurboQuad" : null;
        }

        // The copy's aspiration against the source's: the turbo rows follow. A natural or supercharged engine made turbo gets
        // a stock turbo (MaxScale = 1 + bar / 2) and two upgrade levels, on the pattern of the FM4 engine of that aspiration
        // whose boost is the closest; a turbo made natural or supercharged loses its rows; single <-> twin <-> quad moves
        // the rows to the other table. Without the stock row the game sees a natural engine and the car shows no power.
        // Returns the peak power (kW) of the engine whose rows were used as the pattern (0 when none was).
        private static double ConvertAspiration(SqliteDatabase db, Dictionary<string, List<string>> schema, long engine, long oldAspiration, List<string> plan)
        {
            SqliteDatabase.Result now = db.Query("SELECT AspirationID_Stock, \"StockBoost-bar\" FROM Data_Engine WHERE EngineID = ?", engine);
            long aspiration = Convert.ToInt64(now.Rows[0][0], CultureInfo.InvariantCulture);
            double bar = Real(now.Rows[0][1]);
            if (aspiration <= 1 && bar != 0)
            {
                db.Execute("UPDATE Data_Engine SET \"StockBoost-bar\" = 0 WHERE EngineID = ?", engine);
            }
            string from = TurboTable(oldAspiration), to = TurboTable(aspiration);
            if (from == to)
            {
                return 0;
            }
            if (from != null && schema.ContainsKey(from))
            {
                SqliteDatabase.Result rows = db.Query("SELECT * FROM " + SqliteDatabase.Quote(from) + " WHERE EngineID = ?", engine);
                db.Execute("DELETE FROM " + SqliteDatabase.Quote(from) + " WHERE EngineID = ?", engine);
                if (to != null && schema.ContainsKey(to))
                {
                    foreach (object[] row in rows.Rows)
                    {
                        InsertCommon(db, to, schema[to], rows.Columns, row);
                    }
                    plan.Add(string.Format(CultureInfo.InvariantCulture, "engine: {0} turbo rows moved from {1} to {2}", rows.Rows.Count, from, to));
                    return 0;
                }
                plan.Add(string.Format(CultureInfo.InvariantCulture, "engine: {0} turbo rows of {1} removed (no turbo any more)", rows.Rows.Count, from));
                if (schema.ContainsKey("List_UpgradeEngineIntercooler"))
                {
                    // The intercooler goes with the turbo; the generic rules add the conversion levels a natural engine has.
                    db.Execute("DELETE FROM List_UpgradeEngineIntercooler WHERE EngineID = ?", engine);
                }
            }
            if (to == null || !schema.ContainsKey(to))
            {
                return 0;
            }
            // A natural or supercharged engine made turbo: rows from the FM4 engine of that aspiration with the closest boost.
            long source = -1;
            double sourceBar = 0, bestGap = double.MaxValue;
            foreach (object[] candidate in db.Query("SELECT EngineID, \"StockBoost-bar\" FROM Data_Engine WHERE AspirationID_Stock = ?", aspiration).Rows)
            {
                long candidateId = Convert.ToInt64(candidate[0], CultureInfo.InvariantCulture);
                double gap = Math.Abs(Real(candidate[1]) - bar);
                if (candidateId == engine || gap >= bestGap
                    || Convert.ToInt64(db.Scalar("SELECT count(*) FROM " + SqliteDatabase.Quote(to) + " WHERE EngineID = ? AND IsStock = 1", candidateId), CultureInfo.InvariantCulture) == 0)
                {
                    continue;
                }
                source = candidateId;
                sourceBar = Real(candidate[1]);
                bestGap = gap;
            }
            if (source < 0)
            {
                throw new InvalidOperationException("No FM4 engine has turbo rows in " + to + " to copy.");
            }
            double ratio = (1.0 + (bar / 2.0)) / (1.0 + (sourceBar / 2.0));
            SqliteDatabase.Result pattern = db.Query("SELECT * FROM " + SqliteDatabase.Quote(to) + " WHERE EngineID = ? AND IsStock = 1 ORDER BY Level", source);
            string key = DbIds.KeyColumn(db, to, schema[to]);
            foreach (object[] row in pattern.Rows)
            {
                object[] copy = (object[]) row.Clone();
                for (int i = 0; i < pattern.Columns.Length; i++)
                {
                    string column = pattern.Columns[i];
                    if (string.Equals(column, key, StringComparison.OrdinalIgnoreCase))
                    {
                        copy[i] = (engine * 1000) + (Convert.ToInt64(row[i], CultureInfo.InvariantCulture) % 1000);
                    }
                    else if (string.Equals(column, "EngineID", StringComparison.OrdinalIgnoreCase))
                    {
                        copy[i] = engine;
                    }
                    else if (string.Equals(column, "MaxScale", StringComparison.OrdinalIgnoreCase) && copy[i] is double)
                    {
                        copy[i] = Math.Round((double) copy[i] * ratio, 4);
                    }
                }
                Insert(db, to, pattern.Columns, copy);
            }
            if (schema.ContainsKey("List_UpgradeEngineIntercooler"))
            {
                // Every FM4 turbo engine has its stock intercooler (81 of 81 single turbo ones), the upgrade levels above it; a
                // natural engine only has the conversion levels. The stock one comes from the same pattern engine.
                db.Execute("DELETE FROM List_UpgradeEngineIntercooler WHERE EngineID = ?", engine);
                SqliteDatabase.Result cooler = db.Query("SELECT * FROM List_UpgradeEngineIntercooler WHERE EngineID = ? AND IsStock = 1", source);
                string coolerKey = DbIds.KeyColumn(db, "List_UpgradeEngineIntercooler", schema["List_UpgradeEngineIntercooler"]);
                foreach (object[] row in cooler.Rows)
                {
                    object[] copy = (object[]) row.Clone();
                    for (int i = 0; i < cooler.Columns.Length; i++)
                    {
                        if (string.Equals(cooler.Columns[i], coolerKey, StringComparison.OrdinalIgnoreCase))
                        {
                            copy[i] = (engine * 1000) + (Convert.ToInt64(row[i], CultureInfo.InvariantCulture) % 1000);
                        }
                        else if (string.Equals(cooler.Columns[i], "EngineID", StringComparison.OrdinalIgnoreCase))
                        {
                            copy[i] = engine;
                        }
                    }
                    Insert(db, "List_UpgradeEngineIntercooler", cooler.Columns, copy);
                }
                plan.Add("engine: stock intercooler made (the turbo's), its upgrade levels follow from the generic rules");
            }
            plan.Add(string.Format(CultureInfo.InvariantCulture, "engine: {0} stock turbo row made in {1} ({2} bar) on the pattern of engine {3} ({4} bar)", pattern.Rows.Count, to, bar, source, sourceBar));
            return Real(db.Scalar("SELECT d.SimPeakPower FROM Data_Car d JOIN List_UpgradeEngine u ON u.Ordinal = d.Id AND u.IsStock = 1 WHERE u.EngineID = ? LIMIT 1", source)) / 10.0;
        }

        // FM4's intake manifold part follows the aspiration: all 337 naturally aspirated engines have a stock manifold row, none of
        // the 168 turbo / supercharged ones has one (checked on the game database 2026-11-03). A copy made natural gets the stock row
        // (the generic rules then add its upgrade levels), a copy made turbo / supercharged loses its rows. Without the stock row a
        // natural engine is incomplete in the menu and the car's performance falls apart (a Supra made natural showed F 100 after a
        // turbo was bought and sold).
        private static void ConvertManifold(SqliteDatabase db, Dictionary<string, List<string>> schema, long engine, List<string> plan)
        {
            const string table = "List_UpgradeEngineManifold";
            if (!schema.ContainsKey(table))
            {
                return;
            }
            long aspiration = Convert.ToInt64(db.Scalar("SELECT AspirationID_Stock FROM Data_Engine WHERE EngineID = ?", engine), CultureInfo.InvariantCulture);
            long rows = Convert.ToInt64(db.Scalar("SELECT count(*) FROM " + table + " WHERE EngineID = ?", engine), CultureInfo.InvariantCulture);
            if (aspiration > 1 && rows > 0)
            {
                db.Execute("DELETE FROM " + table + " WHERE EngineID = ?", engine);
                plan.Add(string.Format(CultureInfo.InvariantCulture, "engine: {0} manifold rows removed (FM4's turbo / supercharged engines have none)", rows));
            }
            else if (aspiration <= 1 && rows == 0)
            {
                List<string> columns = schema[table];
                object[] row = new object[columns.Count];
                for (int i = 0; i < columns.Count; i++)
                {
                    switch (columns[i].ToLowerInvariant())
                    {
                        case "id": row[i] = engine * 1000; break;
                        case "engineid": row[i] = engine; break;
                        case "level": row[i] = 1L; break;
                        case "isstock": row[i] = 1L; break;
                        case "torquescale": row[i] = 1.0; break;
                        default: row[i] = 0L; break;
                    }
                }
                Insert(db, table, columns.ToArray(), row);
                plan.Add("engine: stock manifold row made (every FM4 natural engine has one), its upgrade levels follow from the generic rules");
            }
        }

        // The power (kW) an engine of the database makes where its torque first reaches its peak (99.5%): the base curve (N.m,
        // 100 rpm apart) times the stock parts' scale and the boost, as UpdateSimulation does.
        private static double KilowattsAtTorquePeak(SqliteDatabase db, long engine, double[] baseCurve, double redline)
        {
            double factor, boost;
            StockMultipliers(db, engine, out factor, out boost);
            if (Real(db.Scalar("SELECT AspirationID_Stock FROM Data_Engine WHERE EngineID = ?", engine)) <= 1)
            {
                boost = 1;
            }
            double peak = 0;
            for (int i = 0; i < baseCurve.Length && i * 100.0 <= redline; i++)
            {
                peak = Math.Max(peak, baseCurve[i]);
            }
            for (int i = 0; i < baseCurve.Length && i * 100.0 <= redline; i++)
            {
                if (baseCurve[i] >= 0.995 * peak)
                {
                    return baseCurve[i] * factor * boost * (i * 100.0) * 2.0 * Math.PI / 60.0 / 1000.0;
                }
            }
            return 0;
        }

        // The peak power (kW) of an engine of the database with a base torque curve (N.m, 100 rpm apart) up to a redline: the
        // curve times the stock parts' scale and the boost, as UpdateSimulation does.
        private static double PeakKilowatts(SqliteDatabase db, long engine, double[] baseCurve, double redline)
        {
            double factor, boost;
            StockMultipliers(db, engine, out factor, out boost);
            if (Real(db.Scalar("SELECT AspirationID_Stock FROM Data_Engine WHERE EngineID = ?", engine)) <= 1)
            {
                boost = 1;
            }
            double best = 0;
            for (int i = 0; i < baseCurve.Length; i++)
            {
                double rpm = i * 100.0;
                if (rpm > redline)
                {
                    break;
                }
                best = Math.Max(best, baseCurve[i] * factor * boost * rpm * 2.0 * Math.PI / 60.0 / 1000.0);
            }
            return best;
        }

        // A row of one table into another that has fewer or more columns: by column name, the rest left out.
        private static void InsertCommon(SqliteDatabase db, string table, List<string> columns, string[] fromColumns, object[] row)
        {
            List<string> names = new List<string>();
            List<object> values = new List<object>();
            for (int i = 0; i < fromColumns.Length; i++)
            {
                if (columns.Exists(delegate(string c) { return string.Equals(c, fromColumns[i], StringComparison.OrdinalIgnoreCase); }))
                {
                    names.Add(SqliteDatabase.Quote(fromColumns[i]));
                    values.Add(row[i]);
                }
            }
            string marks = string.Join(",", names.ConvertAll<string>(delegate(string n) { return "?"; }).ToArray());
            db.Execute("INSERT INTO " + SqliteDatabase.Quote(table) + " (" + string.Join(",", names.ToArray()) + ") VALUES (" + marks + ")", values.ToArray());
        }

        // Data_Car's Sim* numbers from the copy's stock torque curve (checked on the 502 stock cars of FM4: the redline
        // is exact, the peak torque within 5% on 353 of them, 332 within 1%, the rest supercharged or boosted):
        // SimPeakTorque and SimPeakPower are N.m and W divided by 100, the angular velocities rad/s.
        private static void UpdateSimulation(SqliteDatabase db, Dictionary<string, List<string>> schema, long carId, long engine, List<string> plan)
        {
            string[] columns = { "SimPeakTorque", "SimPeakTorqueAngVel", "SimPeakPower", "SimPeakAngVel", "SimRedlineAngVel" };
            foreach (string column in columns)
            {
                if (!schema["Data_Car"].Exists(delegate(string c) { return string.Equals(c, column, StringComparison.OrdinalIgnoreCase); }))
                {
                    return;
                }
            }
            SqliteDatabase.Result cam = db.Query("SELECT RedlineRPM, TorqueCurveMaxRPM, NumRPMEntriesArray, TorqueCurveFullThrottleID FROM List_UpgradeEngineCamshaft WHERE EngineID = ? AND IsStock = 1 ORDER BY Level LIMIT 1", engine);
            if (cam.Rows.Count == 0)
            {
                return;
            }
            double redline = Real(cam.Rows[0][0]);
            double maxRpm = Real(cam.Rows[0][1]);
            SqliteDatabase.Result curve = db.Query("SELECT * FROM List_TorqueCurve WHERE TorqueCurveID = ?", Convert.ToInt64(cam.Rows[0][3], CultureInfo.InvariantCulture));
            if (curve.Rows.Count == 0)
            {
                return;
            }
            object[] values = curve.Rows[0];
            double scale = Real(values[1]);
            int count = Math.Min((int) Real(values[2]), values.Length - 3);
            if (count < 2)
            {
                return;
            }
            double factor, boost;
            StockMultipliers(db, engine, out factor, out boost);
            if (Real(db.Scalar("SELECT AspirationID_Stock FROM Data_Engine WHERE EngineID = ?", engine)) <= 1)
            {
                boost = 1;
            }
            double bestTorque = 0, torqueSpeed = 0, bestPower = 0, powerSpeed = 0;
            for (int i = 0; i < count; i++)
            {
                double rpm = i * maxRpm / (count - 1);
                if (rpm > redline)
                {
                    break;
                }
                double torque = Real(values[3 + i]) * scale * factor * boost;
                double speed = rpm * 2 * Math.PI / 60;
                if (torque > bestTorque)
                {
                    bestTorque = torque;
                    torqueSpeed = speed;
                }
                if (torque * speed > bestPower)
                {
                    bestPower = torque * speed;
                    powerSpeed = speed;
                }
            }
            db.Execute("UPDATE Data_Car SET SimPeakTorque = ?, SimPeakTorqueAngVel = ?, SimPeakPower = ?, SimPeakAngVel = ?, SimRedlineAngVel = ? WHERE Id = ?",
                bestTorque / 100, torqueSpeed, bestPower / 100, powerSpeed, redline * 2 * Math.PI / 60, carId);
            plan.Add(string.Format(CultureInfo.InvariantCulture, "engine: Data_Car Sim numbers: peak torque {0:0} N.m at {1:0} rpm, peak power {2:0} kW at {3:0} rpm, redline {4:0} rpm",
                bestTorque, torqueSpeed * 60 / (2 * Math.PI), bestPower / 1000, powerSpeed * 60 / (2 * Math.PI), redline));
        }

        // What the game multiplies the stock torque curve by to show the car's torque: factor = the TorqueScale of the
        // engine's stock parts, boost = the stock turbo's MaxScale (1 when the engine has none). The boost only counts
        // for a turbocharged / supercharged engine (AspirationID_Stock above 1).
        public static void StockMultipliers(SqliteDatabase db, long engine, out double factor, out double boost)
        {
            factor = 1;
            foreach (string part in new string[] { "Intake", "FuelSystem", "Ignition", "Exhaust", "Valves", "PistonsCompression", "Displacement", "Manifold", "OilCooling", "RestrictorPlate" })
            {
                string table = "List_UpgradeEngine" + part;
                if (DbIds.HasTable(db, table))
                {
                    double s = Real(db.Scalar("SELECT TorqueScale FROM " + table + " WHERE EngineID = ? AND IsStock = 1 LIMIT 1", engine));
                    factor *= (s > 0) ? s : 1;
                }
            }
            boost = BoostGain(Real(db.Scalar("SELECT AspirationID_Stock FROM Data_Engine WHERE EngineID = ?", engine)),
                Real(db.Scalar("SELECT \"StockBoost-bar\" FROM Data_Engine WHERE EngineID = ?", engine)));
        }

        // What forced induction multiplies the stock torque curve by. The game takes it from the engine's boost pressure
        // (Data_Engine.StockBoost-bar), not from the turbo rows' MaxScale: a supercharged engine has no such row and still
        // gains. Fitted on FM4's own cars against Data_Car.SimPeakTorque / SimPeakPower (the same ratio at the torque and at
        // the power peak, so it does not depend on the rpm): turbo (1 single, 2 twin: ids 2 and 3) 137 cars, mean error 0.16%,
        // worst 2.4%; supercharger (id 5) 26 cars, worst 0.7%; id 6 only 3 cars (worst 8%). 1 for a naturally aspirated engine.
        public static double BoostGain(double aspiration, double bar)
        {
            if (aspiration <= 1 || bar <= 0)
            {
                return 1.0;
            }
            if (aspiration == 2 || aspiration == 3)
            {
                return 1.0 + (0.39069 * bar) + (0.02434 * bar * bar);
            }
            if (aspiration == 5)
            {
                return 1.0 + (0.4119 * bar);
            }
            return 1.0 + (0.3218 * bar);
        }

        private static double Real(object value)
        {
            return (value == null || value == DBNull.Value) ? 0 : Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        private static int SumOf(List<string> parts)
        {
            int total = 0;
            foreach (string part in parts)
            {
                total += int.Parse(part.Substring(part.LastIndexOf(' ') + 1), CultureInfo.InvariantCulture);
            }
            return total;
        }

        private static double[] CurveValues(SqliteDatabase db, long curveId)
        {
            SqliteDatabase.Result curve = db.Query("SELECT * FROM List_TorqueCurve WHERE TorqueCurveID = ?", curveId);
            if (curve.Rows.Count == 0)
            {
                return null;
            }
            object[] row = curve.Rows[0];
            int count = Math.Min((int) Real(row[2]), row.Length - 3);
            double[] values = new double[Math.Max(0, count)];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = Real(row[3 + i]);
            }
            return values;
        }

        // The stock level's curve is the drawn shape; the upgrade levels' curves are changed by the same ratio point by point.
        // A torque curve of the database: TorqueScale x the values, one per Step rpm from 0; the last value of the list is
        // FM4's end marker (-2.63) and is not part of the curve.
        private sealed class OldCurve
        {
            public double Scale;
            public double Step = 100;
            public double[] Values = new double[0];         // normalised, marker left out
            public double ZeroThrottleRatio;                // ZeroThrottleTorqueScale / TorqueScale

            // N.m at an rpm: linear between the points, the last value held past the end.
            public double At(double rpm)
            {
                if (this.Values.Length == 0)
                {
                    return 0;
                }
                double p = rpm / this.Step;
                if (p <= 0)
                {
                    return this.Values[0] * this.Scale;
                }
                if (p >= this.Values.Length - 1)
                {
                    return this.Values[this.Values.Length - 1] * this.Scale;
                }
                int i = (int) p;
                return (this.Values[i] + ((this.Values[i + 1] - this.Values[i]) * (p - i))) * this.Scale;
            }
        }

        // What the copy's curves are built from: the source's stock curve and the stock curve the user designed.
        private sealed class CurvePlan
        {
            public OldCurve OldStock;
            public double[] Design;             // the designed stock curve, base N.m, 100 rpm apart; null = the source's, stretched
            public double Margin;
            public double Ratio;                // the redline's change: the source's curve is stretched by it
            public double StockRedline;         // the stock level's redline of the copy

            // The new stock curve on a grid of points: the design, then (past its end, for an upgrade level's higher redline) the
            // source's shape going on from its last value.
            public double[] NewStock(int points)
            {
                double[] values = new double[points];
                for (int i = 0; i < points; i++)
                {
                    double rpm = i * 100.0;
                    double v;
                    if (this.Design == null)
                    {
                        v = this.OldStock.At(rpm / this.Ratio);
                    }
                    else if (i < this.Design.Length)
                    {
                        v = this.Design[i];
                    }
                    else
                    {
                        double a = this.OldStock.At(((this.Design.Length - 1) * 100.0) / this.Ratio);
                        v = (a > 1e-6) ? this.Design[this.Design.Length - 1] * this.OldStock.At(rpm / this.Ratio) / a : 0;
                    }
                    values[i] = Math.Max(0, v);
                }
                return values;
            }
        }

        private static OldCurve ReadCurve(SqliteDatabase db, long curveId, double maxRpm)
        {
            SqliteDatabase.Result curve = db.Query("SELECT * FROM List_TorqueCurve WHERE TorqueCurveID = ?", curveId);
            if (curve.Rows.Count == 0)
            {
                throw new InvalidOperationException("List_TorqueCurve has no curve " + curveId + ".");
            }
            object[] row = curve.Rows[0];
            OldCurve result = new OldCurve();
            result.Scale = Real(row[1]);
            int listed = Math.Min((int) Real(row[2]), row.Length - 4);
            int count = Math.Max(1, listed - 1);
            result.Values = new double[count];
            for (int i = 0; i < count; i++)
            {
                result.Values[i] = Real(row[3 + i]);
            }
            result.Step = (listed > 1 && maxRpm > 0) ? maxRpm / (listed - 1) : 100.0;
            int zero = Array.FindIndex(curve.Columns, delegate(string c) { return string.Equals(c, "ZeroThrottleTorqueScale", StringComparison.OrdinalIgnoreCase); });
            result.ZeroThrottleRatio = (zero >= 0 && result.Scale != 0) ? Real(row[zero]) / result.Scale : 0.4;
            return result;
        }

        // Every camshaft level's curve of the new engine on FM4's grid, once the generic levels exist. The stock level's curve is
        // the designed one; an upgrade level's is it times the TorqueScale ratio the generic rules gave that level (Curve.TorqueScale
        // of Upgrades.ini). Each runs to the end of the grid (like all of FM4's, no zero above the level's own redline); then the marker (-2.63); TorqueScale = the peak, the
        // values normalised to it. NumRPMEntriesArray and TorqueCurveMaxRPM of every level follow the highest redline.
        private static void RebuildCurves(SqliteDatabase db, long engine, CurvePlan plan, List<string> report)
        {
            SqliteDatabase.Result cams = db.Query("SELECT Id, IsStock, RedlineRPM, TorqueCurveFullThrottleID FROM List_UpgradeEngineCamshaft WHERE EngineID = ? ORDER BY Level", engine);
            double highest = 0;
            foreach (object[] cam in cams.Rows)
            {
                highest = Math.Max(highest, Real(cam[2]));
            }
            int points = (int) Math.Ceiling((highest + plan.Margin) / 100.0) + 1;
            if (points + 1 > 186)
            {
                throw new InvalidOperationException("The redline is too high for a torque curve (at most 18500 rpm).");
            }
            double[] stock = plan.NewStock(points);
            double stockScale = 0;
            double zeroRatio = 0.4;
            foreach (object[] cam in cams.Rows)
            {
                if (Convert.ToInt64(cam[1], CultureInfo.InvariantCulture) == 1)
                {
                    OldCurve own = ReadCurve(db, Convert.ToInt64(cam[3], CultureInfo.InvariantCulture), 0);
                    stockScale = own.Scale;
                    zeroRatio = own.ZeroThrottleRatio;
                }
            }
            if (stockScale <= 0)
            {
                stockScale = 1;
            }
            foreach (object[] cam in cams.Rows)
            {
                long curveId = Convert.ToInt64(cam[3], CultureInfo.InvariantCulture);
                double multiplier = ReadCurve(db, curveId, 0).Scale / stockScale;
                // FM4 never cuts a level's curve at its own redline: all 504 stock and 918 upgrade curves stay above 0 up to the end of
                // the grid (the highest level's redline + margin), so the redline limiter is the game's, not a torque that dies.
                double[] absolute = new double[points];
                for (int i = 0; i < points; i++)
                {
                    absolute[i] = stock[i] * multiplier;
                }
                WriteCurve(db, curveId, absolute, points, zeroRatio);
                db.Execute("UPDATE List_UpgradeEngineCamshaft SET NumRPMEntriesArray = ?, TorqueCurveMaxRPM = ? WHERE Id = ?", (long) (points + 1), points * 100.0, Convert.ToInt64(cam[0], CultureInfo.InvariantCulture));
            }
            report.Add(string.Format(CultureInfo.InvariantCulture, "engine: {0} torque curves of {1} values (0 - {2:0} rpm, 100 rpm apart, end marker -2.63), torque kept above 0 to the end of the grid as FM4 does (highest redline + {3:0} rpm)", cams.Rows.Count, points + 1, points * 100.0, plan.Margin));
        }

        // Writes one curve (the row keeps its id): TorqueScale = the peak, the values normalised, the marker after the last.
        private static void WriteCurve(SqliteDatabase db, long curveId, double[] absolute, int points, double zeroRatio)
        {
            double peak = 0;
            foreach (double v in absolute)
            {
                peak = Math.Max(peak, v);
            }
            if (peak <= 0)
            {
                peak = 1;
            }
            SqliteDatabase.Result curve = db.Query("SELECT * FROM List_TorqueCurve WHERE TorqueCurveID = ?", curveId);
            object[] row = (object[]) curve.Rows[0].Clone();
            int first = Array.FindIndex(curve.Columns, delegate(string c) { return string.Equals(c, "v0", StringComparison.OrdinalIgnoreCase); });
            int zero = Array.FindIndex(curve.Columns, delegate(string c) { return string.Equals(c, "ZeroThrottleTorqueScale", StringComparison.OrdinalIgnoreCase); });
            for (int i = 0; i < curve.Columns.Length; i++)
            {
                string column = curve.Columns[i];
                if (string.Equals(column, "TorqueScale", StringComparison.OrdinalIgnoreCase))
                {
                    row[i] = peak;
                }
                else if (string.Equals(column, "NumTorqueValues", StringComparison.OrdinalIgnoreCase))
                {
                    row[i] = (long) (points + 1);
                }
                else if (i == zero)
                {
                    row[i] = zeroRatio * peak;
                }
                else if (first >= 0 && i >= first && i < zero)
                {
                    int n = i - first;
                    row[i] = (n < points) ? absolute[n] / peak : (n == points) ? -2.63 : 0.0;
                }
            }
            db.Execute("DELETE FROM List_TorqueCurve WHERE TorqueCurveID = ?", curveId);
            Insert(db, "List_TorqueCurve", curve.Columns, row);
        }

        // keep = the share of the curve's rpm range up to the level's redline plus 300 (diesel) or 500 rpm (petrol): the points
        // above it get 0 N.m, so the engine of the copy has no torque past its rev limiter (FM4's curves run past the redline).
        private static void CopyCurve(SqliteDatabase db, long from, long to, double scale, double[] shape, double[] stockShape, bool stockLevel, double keep)
        {
            SqliteDatabase.Result curve = db.Query("SELECT * FROM List_TorqueCurve WHERE TorqueCurveID = ?", from);
            if (curve.Rows.Count == 0)
            {
                throw new InvalidOperationException("List_TorqueCurve has no curve " + from + ".");
            }
            object[] row = (object[]) curve.Rows[0].Clone();
            for (int i = 0; i < curve.Columns.Length; i++)
            {
                if (string.Equals(curve.Columns[i], "TorqueCurveID", StringComparison.OrdinalIgnoreCase))
                {
                    row[i] = to;
                }
                else if (string.Equals(curve.Columns[i], "TorqueScale", StringComparison.OrdinalIgnoreCase) && row[i] is double)
                {
                    row[i] = (double) row[i] * scale;
                }
                else if (shape != null && stockShape != null && i >= 3 && i - 3 < shape.Length && row[i] is double)
                {
                    double was = (double) row[i];
                    row[i] = stockLevel ? shape[i - 3] : ((stockShape[i - 3] > 0.02) ? was * shape[i - 3] / stockShape[i - 3] : was);
                }
            }
            int entries = Math.Min((int) Real(row[2]), row.Length - 3);
            if (keep > 0 && keep < 1 && entries > 1)
            {
                for (int i = 0; i < entries; i++)
                {
                    if ((double) i / (entries - 1) > keep + 1e-9 && row[3 + i] is double)
                    {
                        row[3 + i] = 0.0;
                    }
                }
            }
            Insert(db, "List_TorqueCurve", curve.Columns, row);
        }

        private static void Insert(SqliteDatabase db, string table, string[] columns, object[] row)
        {
            List<string> quoted = new List<string>();
            List<string> marks = new List<string>();
            foreach (string c in columns)
            {
                quoted.Add(SqliteDatabase.Quote(c));
                marks.Add("?");
            }
            db.Execute("INSERT INTO " + SqliteDatabase.Quote(table) + " (" + string.Join(",", quoted.ToArray()) + ") VALUES (" + string.Join(",", marks.ToArray()) + ")", row);
        }
    }
}
