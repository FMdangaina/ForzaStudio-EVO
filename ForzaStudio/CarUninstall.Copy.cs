namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;

    // Uninstall in FM2 / FM3 (user 2026-11-14, ST-02): a car the editor added (Duplicate Car, a port) goes again, found
    // through the game's relation map (GameDbSchema.For), not through FM4's ids or a port log:
    //   1. the car's rows: car columns (CarColumns of the game's map), keys car * 1000 + n (not engine parts), FM2's
    //      StockGarage and Arcade garage;
    //   2. then, again and again, the rows those rows pointed at (physics, aero, prices, engine combo, own engine, wheel,
    //      torque curves...) when the editor made them (key above the game's and its official DLC's largest, GameIdLimits)
    //      and no car row uses them any more, with the rows hanging off them (rows of tables without a car column that
    //      point at them: engine parts, wheel offsets...). A row of the game itself is never deleted;
    //   3. the copy's strings no row names any more, its zips (car, own wheel), thumbnail, sound files (FM3 <car>_CMT.xml,
    //      FM2 <car>_ED.xml + <car>_DSPTuning.xml, the own engine's <engine>_ET.xml), the zip manifest; the names pack after.
    // Only a car above the game's largest Id (base game and its DLC) can go.
    public partial class CarUninstall
    {
        private List<string> ExecuteCopy(bool apply, ForzaGame game)
        {
            List<string> plan = new List<string>();
            string tag = GameInfo.Tag(game);
            string carZipPath = FindFile(Path.Combine(this.Media, "cars"), this.MediaName + ".zip");
            bool zipExists = carZipPath != null;
            if (carZipPath == null)
            {
                carZipPath = Path.Combine(Path.Combine(this.Media, "cars"), this.MediaName + ".zip");
            }
            string dbPath = Path.Combine(Path.Combine(this.Media, "db"), "gamedb.slt");
            if (apply)
            {
                this.CheckWritable(dbPath, carZipPath);
            }
            using (SqliteDatabase db = SqliteDatabase.FromBytes(File.ReadAllBytes(dbPath)))
            {
                object idValue = db.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", this.MediaName);
                if (idValue == null)
                {
                    throw new InvalidOperationException("The database has no car " + this.MediaName + ".");
                }
                long carId = Convert.ToInt64(idValue, CultureInfo.InvariantCulture);
                // The game folder's database is the base game's (the DLC's rows live in their packs, merged in game): a car above the
                // base game's largest was added (a copy, or a port that may carry its official DLC's Id: FM4 -> FM3 LEX_LFA_10 1315).
                long top = GameIdLimits.MaxCarBase(game);
                if (top < 0)
                {
                    top = GameIdLimits.MaxCar(game);
                }
                if (carId <= top)
                {
                    throw new InvalidOperationException(this.MediaName + " (Id " + carId + ") is a car of " + tag + " itself (the base game ends at " + top + "): it is not deleted.");
                }
                plan.Add(string.Format(CultureInfo.InvariantCulture, "car {0}, id {1} ({2})", this.MediaName, carId, tag));
                if (!zipExists)
                {
                    plan.Add("no Media\\cars\\" + this.MediaName + ".zip: only the rows and files left are removed");
                }

                Dictionary<string, List<string>> schema = DbIds.Schema(db);
                this.uninstallGame = game;
                this.uninstallSchema = schema;
                this.uninstallDashes.Clear();
                List<GameDbSchema.Relation> relations = GameDbSchema.For(game).FindAll(delegate(GameDbSchema.Relation r)
                {
                    return !r.IsComposite && schema.ContainsKey(r.Table) && schema.ContainsKey(r.Target)
                        && Has(schema[r.Table], r.Column) && Has(schema[r.Target], r.TargetColumn);
                });
                List<string[]> carColumns = DbIds.CarColumns(schema, game);

                // What the car names, read before its rows go.
                string thumbnail = db.Scalar("SELECT Thumbnail FROM Data_Car WHERE Id = ?", carId) as string;
                List<string> carStrings = new List<string>();
                foreach (string column in new string[] { "DisplayName", "ModelShort" })
                {
                    string value = db.Scalar("SELECT " + column + " FROM Data_Car WHERE Id = ?", carId) as string;
                    if (value != null && value.StartsWith("_&", StringComparison.Ordinal))
                    {
                        carStrings.Add(value);
                    }
                }
                Dictionary<long, string> wheelMedia = new Dictionary<long, string>();
                Dictionary<long, string> engineMedia = new Dictionary<long, string>();
                // FM2: the lookups the port added (Fm2LookupPort: makes, part makers, cylinders), read before the car goes
                List<object[]> addedLookups = (game == ForzaGame.Motorsport2) ? Fm2AddedLookups(db) : new List<object[]>();

                // 1. The car's own rows; what they point at becomes a candidate.
                Dictionary<string, Dictionary<long, bool>> candidates = new Dictionary<string, Dictionary<long, bool>>(StringComparer.OrdinalIgnoreCase);
                int carRows = 0;
                foreach (KeyValuePair<string, List<string>> table in schema)
                {
                    List<string> where = new List<string>();
                    if (table.Key == "Data_Car" || table.Key == Fm2StockGarage.Table)
                    {
                        where.Add("Id = " + carId);
                    }
                    foreach (string[] column in carColumns)
                    {
                        if (string.Equals(column[0], table.Key, StringComparison.OrdinalIgnoreCase))
                        {
                            where.Add(SqliteDatabase.Quote(column[1]) + " = " + carId);
                        }
                    }
                    string key = DbIds.KeyColumn(db, table.Key, table.Value, game);
                    if (key != null && table.Key != "Data_Car" && !Has(table.Value, "EngineID") && Has(table.Value, key))
                    {
                        where.Add(SqliteDatabase.Quote(key) + " BETWEEN " + (carId * 1000) + " AND " + ((carId * 1000) + 999));
                    }
                    if (where.Count == 0)
                    {
                        continue;
                    }
                    carRows += this.DeleteCollecting(db, table.Key, string.Join(" OR ", where.ToArray()), relations, candidates, wheelMedia, engineMedia, apply, plan);
                }

                // 2. What only the car used, when the editor made it.
                int shared = 0;
                for (bool again = true; again; )
                {
                    again = false;
                    foreach (string target in new List<string>(candidates.Keys))
                    {
                        string[] parts = target.Split('.');
                        string table = parts[0];
                        string column = parts[1];
                        long stock = GameIdLimits.Max(game, table, column);
                        foreach (long id in new List<long>(candidates[target].Keys))
                        {
                            candidates[target].Remove(id);
                            if (GameDbSchema.IsNone(id) || id <= stock || stock < 0)
                            {
                                continue;   // a row of the game (or a table GameIdLimits does not know): never deleted
                            }
                            if (Convert.ToInt64(db.Scalar("SELECT count(*) FROM " + SqliteDatabase.Quote(table) + " WHERE " + SqliteDatabase.Quote(column) + " = ?", id), CultureInfo.InvariantCulture) == 0)
                            {
                                continue;
                            }
                            string user = this.UsedByCar(db, relations, carColumns, table, column, id);
                            if (user != null)
                            {
                                plan.Add(string.Format(CultureInfo.InvariantCulture, "  kept {0}.{1} = {2}: {3}", table, column, id, user));
                                continue;
                            }
                            // the rows hanging off it (tables without a car column pointing at it), then the row itself
                            foreach (GameDbSchema.Relation into in relations)
                            {
                                if (string.Equals(into.Target, table, StringComparison.OrdinalIgnoreCase) && string.Equals(into.TargetColumn, column, StringComparison.OrdinalIgnoreCase)
                                    && IsDependent(db, schema, into, game))
                                {
                                    shared += this.DeleteCollecting(db, into.Table, SqliteDatabase.Quote(into.Column) + " = " + id, relations, candidates, wheelMedia, engineMedia, apply, plan);
                                }
                            }
                            shared += this.DeleteCollecting(db, table, SqliteDatabase.Quote(column) + " = " + id, relations, candidates, wheelMedia, engineMedia, apply, plan);
                            again = true;
                        }
                    }
                }
                plan.Add(string.Format(CultureInfo.InvariantCulture, "database: {0} car rows, {1} rows the editor made for it", carRows, shared));

                // 3. Strings and files.
                Dictionary<string, ForzaZip> changed = new Dictionary<string, ForzaZip>(StringComparer.OrdinalIgnoreCase);
                List<string> deleted = new List<string>();
                this.RemoveStrings("Data_Car.str", UnusedKeys(db, "Data_Car", new string[] { "DisplayName", "ModelShort" }, carStrings), changed, plan);
                if (game == ForzaGame.Motorsport2)
                {
                    this.Fm2RemoveLookups(db, addedLookups, changed, apply, plan);
                }
                if (zipExists)
                {
                    deleted.Add(carZipPath);
                    plan.Add("file " + this.Relative(carZipPath));
                }
                foreach (KeyValuePair<long, string> wheel in wheelMedia)
                {
                    // the own wheel's zip, when its row went and no wheel row names that zip any more
                    string path = (wheel.Value == null) ? null : FindFile(Path.Combine(this.Media, "wheels"), wheel.Value + ".zip");
                    if (path != null && db.Scalar("SELECT ID FROM List_Wheels WHERE ID = ?", wheel.Key) == null
                        && Convert.ToInt64(db.Scalar("SELECT count(*) FROM List_Wheels WHERE MediaName = ? COLLATE NOCASE", wheel.Value), CultureInfo.InvariantCulture) == 0 && !deleted.Contains(path))
                    {
                        deleted.Add(path);
                        plan.Add("file " + this.Relative(path));
                    }
                }
                if (thumbnail != null && Convert.ToInt64(db.Scalar("SELECT count(*) FROM Data_Car WHERE Thumbnail = ? COLLATE NOCASE", thumbnail), CultureInfo.InvariantCulture) == 0)
                {
                    GamePaths.GameFile from = GamePaths.Locate(this.TargetRoot, thumbnail);
                    if (from != null && from.InZip)
                    {
                        this.RemoveEntry(from.ZipPath, from.EntryName, changed, plan);
                    }
                }
                string audio = Path.Combine(Path.Combine(this.Media, "Audio"), "Cars");
                if (game == ForzaGame.Motorsport2)
                {
                    this.RemoveEntryAnyCase(Path.Combine(audio, "EmitterData.zip"), this.MediaName + "_ED.xml", changed, plan);
                    this.RemoveEntryAnyCase(Path.Combine(Path.Combine(audio, "Engines"), "DSPTuning.zip"), this.MediaName + "_DSPTuning.xml", changed, plan);
                }
                else
                {
                    this.RemoveEntryAnyCase(Path.Combine(audio, "CarModelTuning.zip"), this.MediaName + "_CMT.xml", changed, plan);
                }
                foreach (KeyValuePair<long, string> engine in engineMedia)
                {
                    // the own engine's tuning, when the engine went and no engine has that MediaName any more
                    if (engine.Value != null && db.Scalar("SELECT EngineID FROM Data_Engine WHERE EngineID = ?", engine.Key) == null
                        && Convert.ToInt64(db.Scalar("SELECT count(*) FROM Data_Engine WHERE MediaName = ? COLLATE NOCASE", engine.Value), CultureInfo.InvariantCulture) == 0)
                    {
                        this.RemoveEntryAnyCase(Path.Combine(Path.Combine(audio, "Engines"), "EngineTuning.zip"), engine.Value + "_ET.xml", changed, plan);
                    }
                }
                if (game != ForzaGame.Motorsport2)
                {
                    this.Fm3SoundAndDash(db, changed, deleted, plan);
                }

                if (!apply)
                {
                    return plan;
                }
                string manifest = ZipManifest.PathFor(this.TargetRoot);
                this.Backup(manifest);
                this.Save(dbPath, db.ToBytes());
                foreach (KeyValuePair<string, ForzaZip> pair in changed)
                {
                    this.Backup(pair.Key);
                    pair.Value.Save(pair.Key);
                    ZipManifest.Update(manifest, ZipManifest.GamePath(this.TargetRoot, pair.Key), pair.Key);
                    this.Line("saved {0}", this.Relative(pair.Key));
                }
                foreach (string path in deleted)
                {
                    this.Backup(path);
                    File.Delete(path);
                    ZipManifest.Remove(manifest, ZipManifest.GamePath(this.TargetRoot, path));
                    this.Line("deleted {0}", this.Relative(path));
                }
                if (!zipExists && ZipManifest.Remove(manifest, ZipManifest.GamePath(this.TargetRoot, carZipPath)))
                {
                    this.Line("zipmanifest.xml: line of the missing {0}.zip removed", this.MediaName);
                }
                this.Line("zipmanifest.xml: {0} lines updated, {1} removed", changed.Count, deleted.Count);
            }
            return plan;
        }

        // Deletes the rows (in memory; written by the caller) after taking what they point at as candidates.
        private int DeleteCollecting(SqliteDatabase db, string table, string where, List<GameDbSchema.Relation> relations, Dictionary<string, Dictionary<long, bool>> candidates,
            Dictionary<long, string> wheelMedia, Dictionary<long, string> engineMedia, bool apply, List<string> plan)
        {
            foreach (GameDbSchema.Relation relation in relations)
            {
                if (!string.Equals(relation.Table, table, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                foreach (object[] row in db.Query("SELECT " + SqliteDatabase.Quote(relation.Column) + " FROM " + SqliteDatabase.Quote(table) + " WHERE " + where).Rows)
                {
                    if (!(row[0] is long))
                    {
                        continue;
                    }
                    long id = (long) row[0];
                    string target = relation.Target + "." + relation.TargetColumn;
                    Dictionary<long, bool> ids;
                    if (!candidates.TryGetValue(target, out ids))
                    {
                        candidates[target] = ids = new Dictionary<long, bool>();
                    }
                    ids[id] = true;
                }
            }
            if (string.Equals(table, "List_Wheels", StringComparison.OrdinalIgnoreCase))
            {
                foreach (object[] row in db.Query("SELECT ID, MediaName FROM List_Wheels WHERE " + where).Rows)
                {
                    wheelMedia[Convert.ToInt64(row[0], CultureInfo.InvariantCulture)] = row[1] as string;
                }
            }
            if (string.Equals(table, "Data_Engine", StringComparison.OrdinalIgnoreCase))
            {
                foreach (object[] row in db.Query("SELECT EngineID, MediaName FROM Data_Engine WHERE " + where).Rows)
                {
                    engineMedia[Convert.ToInt64(row[0], CultureInfo.InvariantCulture)] = row[1] as string;
                }
            }
            if (string.Equals(table, "List_DigitalGauges", StringComparison.OrdinalIgnoreCase) && Has(this.uninstallSchema[table], "MediaName"))
            {
                foreach (object[] row in db.Query("SELECT MediaName FROM List_DigitalGauges WHERE " + where).Rows)
                {
                    if (row[0] is string)
                    {
                        this.uninstallDashes.Add((string) row[0]);   // FM3: Media\DigitalGauges\<MediaName>.zip (CarPortToFm3.PortDash)
                    }
                }
            }
            return this.Delete(db, table, where, apply, plan);
        }

        // A row of a table with a car column (some car's data) or Data_Car that still points at the id, or null.
        private string UsedByCar(SqliteDatabase db, List<GameDbSchema.Relation> relations, List<string[]> carColumns, string table, string column, long id)
        {
            foreach (GameDbSchema.Relation relation in relations)
            {
                if (!string.Equals(relation.Target, table, StringComparison.OrdinalIgnoreCase) || !string.Equals(relation.TargetColumn, column, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (IsDependent(db, this.uninstallSchema, relation, this.uninstallGame))
                {
                    continue;   // a row hanging off the target (engine part, wheel offset): it goes with it
                }
                if (relation.Table == table && string.Equals(relation.Column, column, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                long count = Convert.ToInt64(db.Scalar("SELECT count(*) FROM " + SqliteDatabase.Quote(relation.Table) + " WHERE " + SqliteDatabase.Quote(relation.Column) + " = ?", id), CultureInfo.InvariantCulture);
                if (count > 0)
                {
                    return "used by " + relation.Table + "." + relation.Column;
                }
            }
            return null;
        }

        // ---- FM2: what the port added to the game's lookups for its cars (Fm2LookupPort, B6 of docs/PORT_PARITY.md) ----

        // Each: { table, key column, id, string table, string values (string[]), texture paths (string[]) }. Only rows
        // above FM2's own (GameIdLimits): makes > 53, part makers > 289, cylinders > 10.
        private static List<object[]> Fm2AddedLookups(SqliteDatabase db)
        {
            List<object[]> found = new List<object[]>();
            long makes = GameIdLimits.Max(ForzaGame.Motorsport2, "List_CarMake", "ID");
            foreach (object[] r in db.Query("SELECT ID, DisplayName, Profile, IconPath, IconPathLight FROM List_CarMake WHERE ID > ?", makes).Rows)
            {
                found.Add(new object[] { "List_CarMake", "ID", Convert.ToInt64(r[0], CultureInfo.InvariantCulture), "List_CarMake.str",
                    new string[] { r[1] as string, r[2] as string }, new string[] { r[3] as string, r[4] as string } });
            }
            long makers = GameIdLimits.Max(ForzaGame.Motorsport2, "List_PartManufacturer", "Id");
            foreach (object[] r in db.Query("SELECT Id, LogoPath FROM List_PartManufacturer WHERE Id > ?", makers).Rows)
            {
                found.Add(new object[] { "List_PartManufacturer", "Id", Convert.ToInt64(r[0], CultureInfo.InvariantCulture), null, new string[0], new string[] { r[1] as string } });
            }
            long cylinders = GameIdLimits.Max(ForzaGame.Motorsport2, "List_Cylinders", "CylinderID");
            foreach (object[] r in db.Query("SELECT CylinderID, Description FROM List_Cylinders WHERE CylinderID > ?", cylinders).Rows)
            {
                found.Add(new object[] { "List_Cylinders", "CylinderID", Convert.ToInt64(r[0], CultureInfo.InvariantCulture), "List_Cylinders.str", new string[] { r[1] as string }, new string[0] });
            }
            return found;
        }

        // An added lookup no car uses any more goes with its strings and logo. Part makers: the relation map has no link from
        // the upgrade tables' ManufacturerID / ManufacturerId to them, so their use is counted here (every List_Upgrade*
        // table but List_UpgradeEngine, whose ManufacturerID is the car's make). Makes and cylinders the car's rows went
        // with (step 2): their strings and logo.
        private void Fm2RemoveLookups(SqliteDatabase db, List<object[]> added, Dictionary<string, ForzaZip> changed, bool apply, List<string> plan)
        {
            List<string[]> makerColumns = new List<string[]>();
            foreach (KeyValuePair<string, List<string>> table in DbIds.Schema(db))
            {
                if (!table.Key.StartsWith("List_Upgrade", StringComparison.Ordinal) || table.Key == "List_UpgradeEngine")
                {
                    continue;
                }
                foreach (string column in table.Value)
                {
                    if (string.Equals(column, "ManufacturerID", StringComparison.OrdinalIgnoreCase))
                    {
                        makerColumns.Add(new string[] { table.Key, column });
                    }
                }
            }
            foreach (object[] lookup in added)
            {
                string table = (string) lookup[0];
                string column = (string) lookup[1];
                long id = (long) lookup[2];
                bool gone = db.Scalar("SELECT " + column + " FROM " + table + " WHERE " + column + " = ?", id) == null;
                if (!gone && table == "List_PartManufacturer")
                {
                    long users = 0;
                    foreach (string[] use in makerColumns)
                    {
                        users += Convert.ToInt64(db.Scalar("SELECT count(*) FROM " + SqliteDatabase.Quote(use[0]) + " WHERE " + SqliteDatabase.Quote(use[1]) + " = ?", id), CultureInfo.InvariantCulture);
                    }
                    if (users > 0)
                    {
                        continue;
                    }
                    this.Delete(db, table, "Id = " + id, apply, plan);
                    gone = true;
                }
                if (!gone)
                {
                    continue;   // another car still uses it (Data_Car.MakeID, the engines' CylinderID)
                }
                string[] strings = (string[]) lookup[4];
                List<string> values = new List<string>();
                foreach (string value in strings)
                {
                    if (value != null && value.StartsWith("_&", StringComparison.Ordinal))
                    {
                        values.Add(value);
                    }
                }
                if (lookup[3] != null && values.Count > 0)
                {
                    string[] columns = (table == "List_CarMake") ? new string[] { "DisplayName", "Profile" } : new string[] { "Description" };
                    this.RemoveStrings((string) lookup[3], UnusedKeys(db, table, columns, values), changed, plan);
                }
                List<string> logos = new List<string>();
                foreach (string path in (string[]) lookup[5])
                {
                    if (string.IsNullOrEmpty(path) || logos.Contains(path.ToLowerInvariant()))
                    {
                        continue;
                    }
                    logos.Add(path.ToLowerInvariant());
                    GamePaths.GameFile file = GamePaths.Locate(this.TargetRoot, path);
                    if (file != null && file.InZip)
                    {
                        this.RemoveEntry(file.ZipPath, file.EntryName, changed, plan);
                    }
                }
            }
        }

        private ForzaGame uninstallGame;
        private Dictionary<string, List<string>> uninstallSchema;
        private readonly List<string> uninstallDashes = new List<string>();     // List_DigitalGauges.MediaName of the rows that went

        // FM3 (docs/PORT_PARITY.md, real run 2026-11-27: a ported car's uninstall left these behind): what CarPortToFm3 wrote for
        // the car besides its own engine's <engine>_ET.xml:
        //   - <car>_ET.xml (the FM3 name's engine tuning) when no engine has that MediaName;
        //   - the harmonic tunings (Engines\HarmonicTuning.zip) the removed ETs named and no remaining ET names, then the sound
        //     banks (Engines\Soundbanks\...\<bank>.fsb) those named and no remaining harmonic tuning names: the port added them
        //     from FM4 when FM3 lacked them; one FM3 or another car still uses stays;
        //   - the dash: Media\DigitalGauges\<gauge MediaName>.zip of a List_DigitalGauges row that went, and its images
        //     UI4/Dashboards/<car>/ in UI\Textures.zip.
        private void Fm3SoundAndDash(SqliteDatabase db, Dictionary<string, ForzaZip> changed, List<string> deleted, List<string> plan)
        {
            string engines = Path.Combine(Path.Combine(Path.Combine(this.Media, "Audio"), "Cars"), "Engines");
            string etPath = FindFile(engines, "EngineTuning.zip");
            if (etPath != null && Convert.ToInt64(db.Scalar("SELECT count(*) FROM Data_Engine WHERE MediaName = ? COLLATE NOCASE", this.MediaName), CultureInfo.InvariantCulture) == 0)
            {
                this.RemoveEntry(etPath, this.MediaName + "_ET.xml", changed, plan);
            }
            System.Text.RegularExpressions.Regex htName = new System.Text.RegularExpressions.Regex("\"([^\"<>]+_HT\\.xml)\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            System.Text.RegularExpressions.Regex bankName = new System.Text.RegularExpressions.Regex("Soundbank\\s+name=\"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            HashSet<string> hts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (etPath != null)
            {
                ForzaZip before = new ForzaZip(etPath);
                ForzaZip now = Open(etPath, changed, false);
                HashSet<string> left = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (ForzaZip.Entry entry in now.Entries)
                {
                    left.Add(entry.Name);
                }
                foreach (ForzaZip.Entry entry in before.Entries)
                {
                    if (!left.Contains(entry.Name) && !entry.IsDirectory)
                    {
                        foreach (System.Text.RegularExpressions.Match m in htName.Matches(System.Text.Encoding.UTF8.GetString(before.Read(entry))))
                        {
                            hts.Add(m.Groups[1].Value);
                        }
                    }
                }
                if (hts.Count > 0)
                {
                    foreach (ForzaZip.Entry entry in now.Entries)
                    {
                        if (!entry.IsDirectory)
                        {
                            foreach (System.Text.RegularExpressions.Match m in htName.Matches(System.Text.Encoding.UTF8.GetString(now.Read(entry))))
                            {
                                hts.Remove(m.Groups[1].Value);   // another engine's tuning still uses it
                            }
                        }
                    }
                }
            }
            string htPath = FindFile(engines, "HarmonicTuning.zip");
            HashSet<string> banks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (htPath != null && hts.Count > 0)
            {
                ForzaZip htZip = Open(htPath, changed, false);
                foreach (string ht in hts)
                {
                    ForzaZip.Entry entry = htZip.Find(ht);
                    if (entry != null)
                    {
                        foreach (System.Text.RegularExpressions.Match m in bankName.Matches(System.Text.Encoding.UTF8.GetString(htZip.Read(entry))))
                        {
                            banks.Add(m.Groups[1].Value);
                        }
                        this.RemoveEntry(htPath, ht, changed, plan);
                        htZip = Open(htPath, changed, false);
                    }
                }
                if (banks.Count > 0)
                {
                    foreach (ForzaZip.Entry entry in htZip.Entries)
                    {
                        if (!entry.IsDirectory)
                        {
                            foreach (System.Text.RegularExpressions.Match m in bankName.Matches(System.Text.Encoding.UTF8.GetString(htZip.Read(entry))))
                            {
                                banks.Remove(m.Groups[1].Value);   // a remaining harmonic tuning still plays it
                            }
                        }
                    }
                }
            }
            string soundbanks = Path.Combine(engines, "Soundbanks");
            if (banks.Count > 0 && Directory.Exists(soundbanks))
            {
                foreach (string file in Directory.GetFiles(soundbanks, "*.fsb", SearchOption.AllDirectories))
                {
                    if (banks.Contains(Path.GetFileName(file)) && !deleted.Contains(file))
                    {
                        deleted.Add(file);
                        plan.Add("file " + this.Relative(file));
                    }
                }
            }
            foreach (string dash in this.uninstallDashes)
            {
                string path = FindFile(Path.Combine(this.Media, "DigitalGauges"), dash + ".zip");
                if (path != null && Convert.ToInt64(db.Scalar("SELECT count(*) FROM List_DigitalGauges WHERE MediaName = ? COLLATE NOCASE", dash), CultureInfo.InvariantCulture) == 0 && !deleted.Contains(path))
                {
                    deleted.Add(path);
                    plan.Add("file " + this.Relative(path));
                }
            }
            string textures = FindFile(Path.Combine(this.Media, "UI"), "Textures.zip");
            if (textures != null)
            {
                string folder = "UI4/Dashboards/" + this.MediaName + "/";
                List<string> images = new List<string>();
                foreach (ForzaZip.Entry entry in Open(textures, changed, false).Entries)
                {
                    if (entry.Name.Replace('\\', '/').StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                    {
                        images.Add(entry.Name);
                    }
                }
                foreach (string image in images)
                {
                    this.RemoveEntry(textures, image, changed, plan);
                }
            }
        }

        // A row that hangs off what it points at and goes with it: an extension of the target (the pointing column is the
        // row's own key: WheelNormOffsets.Id -> List_Wheels.ID), or an engine part of an engine (List_UpgradeEngine* without a
        // car column -> Data_Engine). Any other pointing row is a use: an editor-made torque curve two own engines share stays.
        private static bool IsDependent(SqliteDatabase db, Dictionary<string, List<string>> schema, GameDbSchema.Relation relation, ForzaGame game)
        {
            List<string> columns;
            if (relation.Table == "Data_Car" || !schema.TryGetValue(relation.Table, out columns) || relation.Table == relation.Target)
            {
                return false;
            }
            // an engine's parts (List_UpgradeEngine* without a car column) and its torque curves (FM2 / FM3 List_TorqueCurve.EngineID)
            if (string.Equals(relation.Target, "Data_Engine", StringComparison.OrdinalIgnoreCase) && string.Equals(relation.Column, "EngineID", StringComparison.OrdinalIgnoreCase)
                && !Has(columns, "Ordinal") && (relation.Table.StartsWith("List_UpgradeEngine", StringComparison.OrdinalIgnoreCase) || string.Equals(relation.Table, "List_TorqueCurve", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
            // a drivetrain's parts (FM3 / FM4 List_UpgradeDrivetrainClutch / Differential / Driveline / Transmission, no car column):
            // without this the drivetrain a port copied stayed with its 16 part rows, "used" by its own clutch (2026-11-27)
            if (string.Equals(relation.Target, "Data_Drivetrain", StringComparison.OrdinalIgnoreCase) && string.Equals(relation.Column, "DrivetrainID", StringComparison.OrdinalIgnoreCase)
                && !Has(columns, "Ordinal") && relation.Table.StartsWith("List_UpgradeDrivetrain", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            string key = DbIds.KeyColumn(db, relation.Table, columns, game);
            return key != null && string.Equals(key, relation.Column, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasCarColumn(List<string[]> carColumns, string table)
        {
            return carColumns.Exists(delegate(string[] c) { return string.Equals(c[0], table, StringComparison.OrdinalIgnoreCase); });
        }

        private void RemoveEntryAnyCase(string zipPath, string name, Dictionary<string, ForzaZip> changed, List<string> plan)
        {
            string found = FindFile(Path.GetDirectoryName(zipPath), Path.GetFileName(zipPath));
            if (found != null)
            {
                this.RemoveEntry(found, name, changed, plan);   // ForzaZip.Find ignores case
            }
        }

        // The file of the folder with that name in any case (FM2 spells its zips in lower case), or null.
        private static string FindFile(string folder, string name)
        {
            if (!Directory.Exists(folder))
            {
                return null;
            }
            foreach (string file in Directory.GetFiles(folder))
            {
                if (string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }
            return null;
        }
    }
}
