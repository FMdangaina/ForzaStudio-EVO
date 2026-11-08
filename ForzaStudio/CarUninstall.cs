namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;

    // Takes a car that CarPort installed (Install In FM4) out of an FM4 folder again, so the folder is
    // as before the port for that car (and the car can go into a DLC pack, which is built against an FM4
    // without it). Only a ported car: its zip must carry port_log.txt, the port's own record. Removed:
    //   database  the car's rows (Data_Car, the tables the port copies by Ordinal / CarId / CarID /
    //             CarOrdinal where the schema map says it is the car, List_UpgradeCarBody* by Id / 1000),
    //             then what the log says the port added for it (engine, drivetrain, torque curves, wheel,
    //             referenced rows, make) when no other car uses it any more;
    //   strings   the car's DisplayName / ModelShort (and an added make's) in every string table;
    //   files     the car zip, the wheel zip (when its List_Wheels row went), its engine tunings, car
    //             model tuning and thumbnail; zipmanifest.xml lines removed or updated.
    // Harmonic tunings and sound banks the port brought stay: other tunings may play them.
    // Plan() lists everything without writing; Run() does it, every file backed up first.
    public class CarUninstall
    {
        public string TargetRoot;
        public string MediaName;
        public string BackupFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bak");
        public StringBuilder Report = new StringBuilder();

        // The columns CarPort copies a car's rows by (PortDatabase).
        private static readonly string[] CarColumns = { "Ordinal", "CarId", "CarID", "CarOrdinal" };

        private string stamp;

        public List<string> Plan()
        {
            return this.Execute(false);
        }

        public void Run()
        {
            this.Execute(true);
        }

        private string Media
        {
            get { return Path.Combine(this.TargetRoot, "Media"); }
        }

        private List<string> Execute(bool apply)
        {
            this.stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
            List<string> plan = new List<string>();
            string carZipPath = Path.Combine(Path.Combine(this.Media, "cars"), this.MediaName + ".zip");
            if (!File.Exists(carZipPath))
            {
                throw new InvalidOperationException("The FM4 folder has no " + this.MediaName + ".zip.");
            }
            ForzaZip carZip = new ForzaZip(carZipPath);
            ForzaZip.Entry logEntry = carZip.Find("port_log.txt");
            // A ported car has the port's log; a car made by Duplicate Car has none (its rows are found by its Id, its own engine by
            // an EngineID above FM4's). Either way only a car above FM4's own ids goes (checked below).
            bool ported = logEntry != null;
            string log = ported ? Encoding.UTF8.GetString(carZip.Read(logEntry)) : "";
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
                long carId = Convert.ToInt64(idValue);
                if (!ported && carId <= GameIdLimits.Max(ForzaGame.Motorsport4, "Data_Car", "Id"))
                {
                    throw new InvalidOperationException(this.MediaName + " has no port_log.txt and its Id " + carId + " is not above FM4's: it is a car of the game itself, so it is not deleted.");
                }
                plan.Add(string.Format(CultureInfo.InvariantCulture, "car {0}, id {1}", this.MediaName, carId));

                // What the car points at, read before its rows go.
                List<long> engines = Longs(db, "SELECT DISTINCT EngineID FROM List_UpgradeEngine WHERE Ordinal = ?", carId);
                List<long> drivetrains = Longs(db, "SELECT DISTINCT DrivetrainID FROM List_UpgradeDrivetrain WHERE Ordinal = ?", carId);
                object wheel = db.Scalar("SELECT StockWheelID FROM Data_Car WHERE Id = ?", carId);
                string wheelMedia = (wheel == null || wheel == DBNull.Value) ? null : db.Scalar("SELECT MediaName FROM List_Wheels WHERE ID = ?", Convert.ToInt64(wheel)) as string;
                object make = db.Scalar("SELECT MakeID FROM Data_Car WHERE Id = ?", carId);
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
                Dictionary<long, string> engineMedia = new Dictionary<long, string>();
                List<long> curves = new List<long>();
                List<string> curveColumns = Columns(db, "List_UpgradeEngineCamshaft").FindAll(delegate(string c) { return c.StartsWith("TorqueCurve", StringComparison.OrdinalIgnoreCase) && c.EndsWith("ID", StringComparison.OrdinalIgnoreCase); });
                foreach (long engine in engines)
                {
                    engineMedia[engine] = db.Scalar("SELECT MediaName FROM Data_Engine WHERE EngineID = ?", engine) as string;
                    foreach (string column in curveColumns)
                    {
                        foreach (long curve in Longs(db, "SELECT " + column + " FROM List_UpgradeEngineCamshaft WHERE EngineID = ? AND " + column + " IS NOT NULL", engine))
                        {
                            if (!curves.Contains(curve))
                            {
                                curves.Add(curve);
                            }
                        }
                    }
                }

                // 1. The car's own rows.
                int carRows = 0;
                foreach (string table in Tables(db))
                {
                    List<string> columns = Columns(db, table);
                    string where = null;
                    if (table == "Data_Car")
                    {
                        where = "Id = " + carId;
                    }
                    else if (table.StartsWith("List_UpgradeCarBody", StringComparison.Ordinal) && columns.Count > 0 && columns[0] == "Id" && !Has(columns, "Ordinal"))
                    {
                        where = "Id / 1000 = " + carId;
                    }
                    else
                    {
                        foreach (string column in CarColumns)
                        {
                            GameDbSchema.Relation relation = Has(columns, column) ? GameDbSchema.Find(table, column) : null;
                            if (relation != null && relation.Target == "Data_Car")
                            {
                                where = SqliteDatabase.Quote(column) + " = " + carId;
                                break;
                            }
                        }
                    }
                    if (where != null)
                    {
                        carRows += this.Delete(db, table, where, apply, plan);
                    }
                }

                // 2. What the port added for the car (the log's "  Table: n rows (Column ids)" and
                // "  Table: n rows (where, referenced by T)" lines), once nothing else uses it.
                int shared = 0;
                foreach (Match line in Regex.Matches(log, @"^  (\w+): \d+ rows \((\w+) ([\d,]+)\)\r?$", RegexOptions.Multiline))
                {
                    string table = line.Groups[1].Value;
                    string column = line.Groups[2].Value;
                    foreach (string text in line.Groups[3].Value.Split(','))
                    {
                        long id = long.Parse(text, CultureInfo.InvariantCulture);
                        string user = StillUsed(db, column, id);
                        if (user != null)
                        {
                            plan.Add(string.Format(CultureInfo.InvariantCulture, "  kept {0} {1} = {2}: {3}", table, column, id, user));
                            continue;
                        }
                        shared += this.Delete(db, table, SqliteDatabase.Quote(column) + " = " + id, apply, plan);
                    }
                }
                foreach (Match line in Regex.Matches(log, @"^  (\w+): \d+ rows \((\w+) = (-?\d+), referenced by \w+\)\r?$", RegexOptions.Multiline))
                {
                    string table = line.Groups[1].Value;
                    string column = line.Groups[2].Value;
                    long id = long.Parse(line.Groups[3].Value, CultureInfo.InvariantCulture);
                    string user = Referenced(db, table, column, id);
                    if (user != null)
                    {
                        plan.Add(string.Format(CultureInfo.InvariantCulture, "  kept {0} {1} = {2}: {3}", table, column, id, user));
                        continue;
                    }
                    shared += this.Delete(db, table, SqliteDatabase.Quote(column) + " = " + id, apply, plan);
                }
                if (!ported)
                {
                    // The own engine of a copy (Data_Engine above FM4's ids, no car left on it).
                    long lastEngine = GameIdLimits.Max(ForzaGame.Motorsport4, "Data_Engine", "EngineID");
                    foreach (long engine in engines)
                    {
                        string user = StillUsed(db, "EngineID", engine);
                        if (engine > lastEngine && user == null)
                        {
                            shared += this.Delete(db, "Data_Engine", "EngineID = " + engine, apply, plan);
                        }
                        else if (user != null)
                        {
                            plan.Add(string.Format(CultureInfo.InvariantCulture, "  kept Data_Engine EngineID = {0}: {1}", engine, user));
                        }
                    }
                    // The own stock wheel of a copy (List_Wheels above FM4's ids, no car left on it); its zip goes below.
                    if (wheel != null && wheel != DBNull.Value)
                    {
                        long wheelId = Convert.ToInt64(wheel);
                        string user = StillUsed(db, "WheelID", wheelId);
                        if (wheelId > GameIdLimits.Max(ForzaGame.Motorsport4, "List_Wheels", "ID") && user == null)
                        {
                            shared += this.Delete(db, "List_Wheels", "ID = " + wheelId, apply, plan);
                            foreach (string[] t in new string[][] { new string[] { "WheelExceptions", "WheelID" }, new string[] { "WheelNormOffsets", "Id" } })
                            {
                                if (Tables(db).Contains(t[0]))
                                {
                                    shared += this.Delete(db, t[0], t[1] + " = " + wheelId, apply, plan);
                                }
                            }
                            if (wheelMedia != null)
                            {
                                log += "\nwheel zip: Media\\wheels\\" + wheelMedia + ".zip\n";
                            }
                        }
                        else if (user != null)
                        {
                            plan.Add(string.Format(CultureInfo.InvariantCulture, "  kept List_Wheels ID = {0}: {1}", wheelId, user));
                        }
                    }
                    foreach (string kind in new string[] { "A", "B", "C" })
                    {
                        log += "\ntire zip: Media\\wheels\\Tire" + kind + this.MediaName + ".zip\n";
                    }
                }
                // Engine part rows the log does not list (the aspiration conversions CarUpgrades adds to
                // a naturally aspirated engine, 14.1f): every List_UpgradeEngine* row of an engine whose
                // Data_Engine row went above.
                foreach (long engine in engines)
                {
                    if (db.Scalar("SELECT EngineID FROM Data_Engine WHERE EngineID = ?", engine) != null)
                    {
                        continue;
                    }
                    foreach (string table in Tables(db))
                    {
                        if (table.StartsWith("List_UpgradeEngine", StringComparison.Ordinal) && table != "List_UpgradeEngine" && Has(Columns(db, table), "EngineID"))
                        {
                            shared += this.Delete(db, table, "EngineID = " + engine, apply, plan);
                        }
                    }
                }
                // FM3 torque curves get new ids the log does not list: the curves of the car's engines.
                foreach (long curve in curves)
                {
                    if (StillUsed(db, "TorqueCurveID", curve) == null)
                    {
                        shared += this.Delete(db, "List_TorqueCurve", "TorqueCurveID = " + curve, apply, plan);
                    }
                }
                // A make the port added (PortMake), when no car is left under it.
                Match madeMake = Regex.Match(log, @"^make (\d+) \([^)]*\): not in FM4", RegexOptions.Multiline);
                List<string> makeStrings = new List<string>();
                List<string> titleStrings = new List<string>();     // the make's titles (MakeTitles) go with it
                string titleBase = null;
                List<string> decalStrings = new List<string>();     // the make's own decal (MakeDecals) too
                string decalEntry = null;
                if (madeMake.Success && make != null && long.Parse(madeMake.Groups[1].Value, CultureInfo.InvariantCulture) == Convert.ToInt64(make))
                {
                    long makeId = Convert.ToInt64(make);
                    if (Convert.ToInt64(db.Scalar("SELECT count(*) FROM Data_Car WHERE MakeID = ?", makeId)) == 0)
                    {
                        foreach (string column in new string[] { "DisplayName", "Profile" })
                        {
                            string value = db.Scalar("SELECT " + column + " FROM List_CarMake WHERE ID = ?", makeId) as string;
                            if (value != null && value.StartsWith("_&", StringComparison.Ordinal))
                            {
                                makeStrings.Add(value);
                            }
                        }
                        if (MakeTitles.Supported(db))
                        {
                            titleBase = db.Scalar("SELECT IconPathBase FROM List_CarMake WHERE ID = ?", makeId) as string;
                            string pattern = (titleBase ?? string.Empty).Replace("_", "\\_") + "\\_Affinity\\_%";
                            if (!string.IsNullOrEmpty(titleBase))
                            {
                                foreach (object[] row in db.Query("SELECT UnlockDescription, UnlockTitleString FROM " + MakeTitles.Table + " WHERE UnlockName LIKE ? ESCAPE '\\'", pattern).Rows)
                                {
                                    titleStrings.Add(row[0] as string);
                                    titleStrings.Add(row[1] as string);
                                }
                                shared += this.Delete(db, MakeTitles.Table, "UnlockName LIKE '" + pattern.Replace("'", "''") + "' ESCAPE '\\'", apply, plan);
                            }
                        }
                        if (MakeDecals.Supported(db))
                        {
                            // The make's own decal (Sequence 1, shown by no other make) goes with it: its row, name and carbin.
                            object own = db.Scalar("SELECT Livery_DecalID FROM " + MakeDecals.Sort + " WHERE MakeID = ? AND Sequence = 1 AND Livery_DecalID NOT IN (SELECT Livery_DecalID FROM " + MakeDecals.Sort + " WHERE MakeID <> ?)", makeId, makeId);
                            if (own != null && own != DBNull.Value)
                            {
                                long ownId = Convert.ToInt64(own, CultureInfo.InvariantCulture);
                                string ownFile = db.Scalar("SELECT Filename FROM " + MakeDecals.Table + " WHERE ID = ?", ownId) as string;
                                string ownName = db.Scalar("SELECT DisplayName FROM " + MakeDecals.Table + " WHERE ID = ?", ownId) as string;
                                if (ownName != null && ownName.StartsWith("_&", StringComparison.Ordinal))
                                {
                                    decalStrings.Add(ownName);
                                }
                                if (ownFile != null)
                                {
                                    decalEntry = "decals/" + ownFile.Substring(ownFile.LastIndexOfAny(new char[] { '\\', '/' }) + 1);
                                }
                                shared += this.Delete(db, MakeDecals.Table, "ID = " + ownId, apply, plan);
                            }
                        }
                        shared += this.Delete(db, "List_CarMake", "ID = " + makeId, apply, plan);
                        shared += this.Delete(db, "Livery_DecalsSortOrder", "MakeID = " + makeId, apply, plan);
                    }
                    else
                    {
                        plan.Add(string.Format(CultureInfo.InvariantCulture, "  kept List_CarMake ID = {0}: other cars use the make", makeId));
                    }
                }
                plan.Add(string.Format(CultureInfo.InvariantCulture, "database: {0} car rows, {1} rows the port added for it", carRows, shared));

                // 3. Strings no row uses any more.
                List<int> carKeys = UnusedKeys(db, "Data_Car", new string[] { "DisplayName", "ModelShort" }, carStrings);
                List<int> makeKeys = UnusedKeys(db, "List_CarMake", new string[] { "DisplayName", "Profile" }, makeStrings);

                // 4. Files.
                Dictionary<string, ForzaZip> changed = new Dictionary<string, ForzaZip>(StringComparer.OrdinalIgnoreCase);
                List<string> deleted = new List<string>();
                this.RemoveStrings("Data_Car.str", carKeys, changed, plan);
                this.RemoveStrings("List_CarMake.str", makeKeys, changed, plan);
                titleStrings.RemoveAll(delegate(string value) { return value == null; });
                this.RemoveStrings(MakeTitles.Table + ".str", UnusedKeys(db, MakeTitles.Table, new string[] { "UnlockDescription", "UnlockTitleString" }, titleStrings), changed, plan);
                decalStrings.RemoveAll(delegate(string value) { return value == null; });
                if (decalStrings.Count > 0)
                {
                    this.RemoveStrings(MakeDecals.Table + ".str", UnusedKeys(db, MakeDecals.Table, new string[] { "DisplayName" }, decalStrings), changed, plan);
                }
                if (decalEntry != null)
                {
                    this.RemoveEntry(MakeDecals.LiveryPath(this.TargetRoot), decalEntry, changed, plan);
                }
                string rulesPath = MakeTitles.GameRules(this.TargetRoot);
                string rulesText = null;
                if (titleBase != null && File.Exists(rulesPath))
                {
                    rulesText = MakeTitles.RemoveRule(File.ReadAllText(rulesPath), titleBase);
                    plan.Add(rulesText == null ? "  no Drive_" + titleBase + " rule in " + MakeTitles.RulesFile : "file Media\\GameTunableSettings\\BadgesAndTitles\\" + MakeTitles.RulesFile + " (the Drive_" + titleBase + " rule)");
                }
                deleted.Add(carZipPath);
                plan.Add("file Media\\cars\\" + Path.GetFileName(carZipPath));
                // the car's own tire models (Media\wheels\TireA/B/C<car>.zip, D-16) go with the car
                foreach (Match zip in Regex.Matches(log, @"^tire zip: (Media\\wheels\\[^ \r\n]+\.zip)", RegexOptions.Multiline))
                {
                    string tirePath = Path.Combine(this.TargetRoot, zip.Groups[1].Value);
                    if (File.Exists(tirePath) && !deleted.Contains(tirePath))
                    {
                        deleted.Add(tirePath);
                        plan.Add("file " + zip.Groups[1].Value);
                    }
                }
                if (wheel != null && Convert.ToInt64(db.Scalar("SELECT count(*) FROM List_Wheels WHERE ID = ?", Convert.ToInt64(wheel))) == 0)
                {
                    foreach (Match zip in Regex.Matches(log, @"^wheel zip: (Media\\wheels\\[^ \r\n]+\.zip)", RegexOptions.Multiline))
                    {
                        string path = Path.Combine(this.TargetRoot, zip.Groups[1].Value);
                        if (File.Exists(path) && !deleted.Contains(path))
                        {
                            deleted.Add(path);
                            plan.Add("file " + zip.Groups[1].Value);
                        }
                    }
                }
                string engines4 = Path.Combine(Path.Combine(Path.Combine(Path.Combine(this.Media, "audio"), "Cars"), "Engines"), "enginetuning.zip");
                foreach (KeyValuePair<long, string> engine in engineMedia)
                {
                    // Only when the engine went AND no other engine has that MediaName (a copy made to play another car's
                    // sound has its MediaName: that tuning file is the other car's).
                    if (engine.Value != null && db.Scalar("SELECT EngineID FROM Data_Engine WHERE EngineID = ?", engine.Key) == null
                        && Convert.ToInt64(db.Scalar("SELECT count(*) FROM Data_Engine WHERE MediaName = ? COLLATE NOCASE", engine.Value)) == 0)
                    {
                        this.RemoveEntry(engines4, engine.Value + "_ET.xml", changed, plan);
                    }
                }
                if (!ported || Regex.IsMatch(log, "^car model tuning " + Regex.Escape(this.MediaName) + "_CMT.xml added", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                {
                    this.RemoveEntry(Path.Combine(Path.Combine(Path.Combine(this.Media, "audio"), "Cars"), "CarModelTuning.zip"), this.MediaName + "_CMT.xml", changed, plan);
                }
                Match thumb = Regex.Match(log, @"^thumbnail: (\S+) in (Media\\\S+\.zip)", RegexOptions.Multiline);
                if (thumb.Success && thumbnail != null)
                {
                    string file = Path.GetFileName(thumbnail.Replace('\\', '/'));
                    if (Convert.ToInt64(db.Scalar("SELECT count(*) FROM Data_Car WHERE Thumbnail LIKE ?", "%" + file)) == 0)
                    {
                        this.RemoveEntry(Path.Combine(this.TargetRoot, thumb.Groups[2].Value), thumb.Groups[1].Value, changed, plan);
                    }
                }
                if (!ported && thumbnail != null)
                {
                    // A copy's thumbnail (Thumbnail_<id>.xds in the zip the source's is in), when no other car shows the same file.
                    GamePaths.GameFile from = GamePaths.Locate(this.TargetRoot, thumbnail);
                    string file = Path.GetFileName(thumbnail.Replace('\\', '/'));
                    if (from != null && from.InZip && Convert.ToInt64(db.Scalar("SELECT count(*) FROM Data_Car WHERE Thumbnail LIKE ?", "%" + file)) == 0)
                    {
                        this.RemoveEntry(from.ZipPath, from.EntryName, changed, plan);
                    }
                }
                plan.Add("kept: harmonic tunings and sound banks (other engine tunings may play them)");

                if (!apply)
                {
                    return plan;
                }

                // Write: backups first, then the database, the zips, the deletions and the manifest.
                string manifest = ZipManifest.PathFor(this.TargetRoot);
                this.Backup(manifest);
                this.Save(dbPath, db.ToBytes());
                if (rulesText != null)
                {
                    this.Backup(rulesPath);
                    File.WriteAllText(rulesPath, rulesText, new UTF8Encoding(false));
                    this.Line("saved {0}", this.Relative(rulesPath));
                }
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
                this.Line("zipmanifest.xml: {0} lines updated, {1} removed", changed.Count, deleted.Count);
            }
            return plan;
        }

        // Deletes (or only counts) the rows; the plan gets one line per table.
        private int Delete(SqliteDatabase db, string table, string where, bool apply, List<string> plan)
        {
            if (Convert.ToInt64(db.Scalar("SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = ?", table)) == 0)
            {
                return 0;
            }
            int count = Convert.ToInt32(db.Scalar("SELECT count(*) FROM " + SqliteDatabase.Quote(table) + " WHERE " + where));
            if (count == 0)
            {
                return 0;
            }
            // Run on the in-memory copy even in the plan, so the "still used" checks see the result.
            db.Execute("DELETE FROM " + SqliteDatabase.Quote(table) + " WHERE " + where);
            plan.Add(string.Format(CultureInfo.InvariantCulture, "  {0}: {1} rows ({2})", table, count, where.Replace("[", "").Replace("]", "")));
            if (apply)
            {
                this.Line("  {0}: {1} rows deleted ({2})", table, count, where.Replace("[", "").Replace("]", ""));
            }
            return count;
        }

        // Who still uses an engine, drivetrain, wheel or torque curve id, or null.
        internal static string StillUsed(SqliteDatabase db, string column, long id)
        {
            string[][] users =
            {
                new string[] { "EngineID", "List_UpgradeEngine", "EngineID" },
                new string[] { "DrivetrainID", "List_UpgradeDrivetrain", "DrivetrainID" },
                new string[] { "ID", "Data_Car", "StockWheelID" },
                new string[] { "WheelID", "Data_Car", "StockWheelID" },
            };
            foreach (string[] user in users)
            {
                if (string.Equals(column, user[0], StringComparison.OrdinalIgnoreCase))
                {
                    return Convert.ToInt64(db.Scalar("SELECT count(*) FROM " + user[1] + " WHERE " + user[2] + " = ?", id)) > 0 ? "used by " + user[1] : null;
                }
            }
            if (string.Equals(column, "TorqueCurveID", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string table in Tables(db))
                {
                    if (table == "List_TorqueCurve")
                    {
                        continue;
                    }
                    foreach (string c in Columns(db, table))
                    {
                        if (c.StartsWith("TorqueCurve", StringComparison.OrdinalIgnoreCase) && c.EndsWith("ID", StringComparison.OrdinalIgnoreCase)
                            && Convert.ToInt64(db.Scalar("SELECT count(*) FROM " + SqliteDatabase.Quote(table) + " WHERE " + SqliteDatabase.Quote(c) + " = ?", id)) > 0)
                        {
                            return "used by " + table;
                        }
                    }
                }
                return null;
            }
            return "not a column the uninstall knows";
        }

        // A row CopyReferences added (a key other rows point at): still referenced by a column named as
        // the port reads references (the key's own name, or the table name without prefix + ID).
        internal static string Referenced(SqliteDatabase db, string table, string keyColumn, long id)
        {
            List<string> names = new List<string>();
            if (!string.Equals(keyColumn, "Id", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(keyColumn);
            }
            int prefix = table.IndexOf('_');
            names.Add(((prefix >= 0) ? table.Substring(prefix + 1) : table) + "ID");
            foreach (string other in Tables(db))
            {
                if (other == table)
                {
                    continue;
                }
                foreach (string c in Columns(db, other))
                {
                    foreach (string name in names)
                    {
                        if (string.Equals(c, name, StringComparison.OrdinalIgnoreCase)
                            && Convert.ToInt64(db.Scalar("SELECT count(*) FROM " + SqliteDatabase.Quote(other) + " WHERE " + SqliteDatabase.Quote(c) + " = ?", id)) > 0)
                        {
                            return "referenced by " + other;
                        }
                    }
                }
            }
            return null;
        }

        // The 16-bit string keys of the values that no row of the table still names.
        internal static List<int> UnusedKeys(SqliteDatabase db, string table, string[] columns, List<string> values)
        {
            List<int> keys = new List<int>();
            foreach (string value in values)
            {
                uint id;
                if (!uint.TryParse(value.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out id))
                {
                    continue;
                }
                bool used = false;
                foreach (string column in columns)
                {
                    foreach (object[] row in db.Query("SELECT " + column + " FROM " + table + " WHERE " + column + " LIKE '\\_&%' ESCAPE '\\'").Rows)
                    {
                        uint other;
                        if (row[0] is string && uint.TryParse(((string) row[0]).Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out other) && (other & 0xFFFF) == (id & 0xFFFF))
                        {
                            used = true;
                        }
                    }
                }
                if (!used && !keys.Contains((int) (id & 0xFFFF)))
                {
                    keys.Add((int) (id & 0xFFFF));
                }
            }
            return keys;
        }

        private void RemoveStrings(string strName, List<int> keys, Dictionary<string, ForzaZip> changed, List<string> plan)
        {
            if (keys.Count == 0)
            {
                return;
            }
            string tables = Path.Combine(this.Media, "StringTables");
            int files = 0;
            foreach (string zipPath in Directory.GetFiles(tables, "*.zip"))
            {
                ForzaZip zip = Open(zipPath, changed, false);
                ForzaZip.Entry entry = zip.Find(strName);
                if (entry == null)
                {
                    continue;
                }
                StrFile str = new StrFile(zip.Read(entry));
                int removed = 0;
                foreach (int key in keys)
                {
                    removed += str.Remove(key) ? 1 : 0;
                }
                if (removed > 0)
                {
                    zip.Replace(entry, str.ToBytes());
                    changed[zipPath] = zip;
                    files++;
                }
            }
            plan.Add(string.Format(CultureInfo.InvariantCulture, "strings: {0} keys ({1}) out of {2} in {3} languages", keys.Count,
                string.Join(", ", keys.ConvertAll<string>(delegate(int k) { return k.ToString("X4", CultureInfo.InvariantCulture); }).ToArray()), strName, files));
        }

        private void RemoveEntry(string zipPath, string name, Dictionary<string, ForzaZip> changed, List<string> plan)
        {
            if (!File.Exists(zipPath))
            {
                return;
            }
            ForzaZip zip = Open(zipPath, changed, false);
            ForzaZip.Entry entry = zip.Find(name);
            if (entry == null)
            {
                return;
            }
            zip.Remove(entry);
            changed[zipPath] = zip;
            plan.Add(string.Format("entry {0} in {1}", name, this.Relative(zipPath)));
        }

        private static ForzaZip Open(string zipPath, Dictionary<string, ForzaZip> changed, bool add)
        {
            ForzaZip zip;
            if (!changed.TryGetValue(zipPath, out zip))
            {
                zip = new ForzaZip(zipPath);
                if (add)
                {
                    changed[zipPath] = zip;
                }
            }
            return zip;
        }

        // Every file the uninstall rewrites or deletes must be free (Xenia keeps zips open).
        private void CheckWritable(string dbPath, string carZipPath)
        {
            List<string> files = new List<string>();
            files.Add(dbPath);
            files.Add(carZipPath);
            files.Add(ZipManifest.PathFor(this.TargetRoot));
            files.Add(Path.Combine(Path.Combine(this.Media, "UI"), "Textures.zip"));
            files.AddRange(Directory.GetFiles(Path.Combine(this.Media, "StringTables"), "*.zip"));
            List<string> busy = new List<string>();
            foreach (string file in files)
            {
                if (!File.Exists(file))
                {
                    continue;
                }
                try
                {
                    using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                    }
                }
                catch (IOException)
                {
                    busy.Add(this.Relative(file));
                }
                catch (UnauthorizedAccessException)
                {
                    busy.Add(this.Relative(file));
                }
            }
            if (busy.Count > 0)
            {
                throw new IOException("Files in use (close the game or the emulator first): " + string.Join(", ", busy.ToArray()));
            }
        }

        private void Save(string path, byte[] data)
        {
            this.Backup(path);
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, data);
            File.Delete(path);
            File.Move(temp, path);
            this.Line("saved {0}", this.Relative(path));
        }

        private void Backup(string path)
        {
            if (!File.Exists(path) || !BackupPolicy.Enabled)
            {
                return;
            }
            Directory.CreateDirectory(this.BackupFolder);
            string copy = Path.Combine(this.BackupFolder, this.stamp + "_uninstall_bak_" + Path.GetFileName(path));
            for (int i = 2; File.Exists(copy); i++)
            {
                copy = Path.Combine(this.BackupFolder, this.stamp + "_uninstall_bak" + i + "_" + Path.GetFileName(path));
            }
            File.Copy(path, copy);
        }

        private void Line(string format, params object[] args)
        {
            this.Report.AppendLine(string.Format(CultureInfo.InvariantCulture, format, args));
        }

        private string Relative(string path)
        {
            return path.StartsWith(this.TargetRoot, StringComparison.OrdinalIgnoreCase) ? path.Substring(this.TargetRoot.TrimEnd('\\').Length + 1) : path;
        }

        internal static List<long> Longs(SqliteDatabase db, string sql, params object[] args)
        {
            List<long> list = new List<long>();
            foreach (object[] row in db.Query(sql, args).Rows)
            {
                if (row[0] != null && !list.Contains(Convert.ToInt64(row[0])))
                {
                    list.Add(Convert.ToInt64(row[0]));
                }
            }
            return list;
        }

        internal static List<string> Tables(SqliteDatabase db)
        {
            List<string> tables = new List<string>();
            foreach (object[] row in db.Query("SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name").Rows)
            {
                tables.Add((string) row[0]);
            }
            return tables;
        }

        internal static List<string> Columns(SqliteDatabase db, string table)
        {
            List<string> columns = new List<string>();
            foreach (object[] column in db.Query("PRAGMA table_info(" + SqliteDatabase.Quote(table) + ")").Rows)
            {
                columns.Add((string) column[1]);
            }
            return columns;
        }

        internal static bool Has(List<string> columns, string name)
        {
            return columns.Exists(delegate(string c) { return string.Equals(c, name, StringComparison.OrdinalIgnoreCase); });
        }
    }
}
