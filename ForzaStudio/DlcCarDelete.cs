namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;

    // Takes one car out of a DLC package's work folder (Open DLC Package): the same job CarUninstall does in a
    // game folder, on the pack's own files. The pack's zip (Media\DLCZips\<offer>_pri_<n>.zip) holds the car's
    // folder, its wheel folder, engine and car-model tunings, thumbnail and the merge database
    // (db\patch\<offer>000_merge.slt, only the rows the pack adds); StringTables_pri_<offer>.zip holds the
    // strings. Removed:
    //   database  the car's rows in the merge (by the schema map, as CarUninstall), what the port's log in the car
    //             folder says it added for it when no other car of the pack uses it, the offer's mapping row;
    //   strings   the car's DisplayName / ModelShort (and an added make's) in every language;
    //   files     Media\cars\<car>\, the wheel folder when its List_Wheels row went, the engine tuning when its
    //             Data_Engine row went, the car model tuning, the thumbnail when no other car shows it.
    // Plan() lists everything without writing; Run() writes the two zips (the caller packs the package again).
    public class DlcCarDelete
    {
        public string PackageFolder;
        public string MediaName;
        public StringBuilder Report = new StringBuilder();

        private string PackZipPath
        {
            get
            {
                foreach (string file in Directory.GetFiles(Path.Combine(Path.Combine(this.PackageFolder, "Media"), "DLCZips"), "*_pri_*.zip"))
                {
                    if (!Path.GetFileName(file).StartsWith("StringTables_", StringComparison.OrdinalIgnoreCase))
                    {
                        return file;
                    }
                }
                throw new InvalidOperationException("The package has no car pack zip (Media\\DLCZips\\<offer>_pri_<n>.zip).");
            }
        }

        private static ForzaZip.Entry MergeEntry(ForzaZip zip)
        {
            foreach (ForzaZip.Entry entry in zip.Entries)
            {
                string name = entry.Name.Replace('\\', '/');
                if (name.StartsWith("Media/db/patch/", StringComparison.OrdinalIgnoreCase) && name.EndsWith("_merge.slt", StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }
            return null;
        }

        // The MediaName of every car the package adds.
        public static List<string> Cars(string packageFolder)
        {
            DlcCarDelete reader = new DlcCarDelete();
            reader.PackageFolder = packageFolder;
            ForzaZip zip = new ForzaZip(reader.PackZipPath);
            ForzaZip.Entry merge = MergeEntry(zip);
            List<string> cars = new List<string>();
            if (merge == null)
            {
                return cars;
            }
            using (SqliteDatabase db = SqliteDatabase.FromBytes(zip.Read(merge)))
            {
                if (CarUninstall.Tables(db).Contains("Data_Car"))
                {
                    foreach (object[] row in db.Query("SELECT MediaName FROM Data_Car ORDER BY MediaName").Rows)
                    {
                        cars.Add(Convert.ToString(row[0], CultureInfo.InvariantCulture));
                    }
                }
            }
            return cars;
        }

        public List<string> Plan()
        {
            return this.Execute(false);
        }

        public void Run()
        {
            this.Execute(true);
        }

        private List<string> Execute(bool apply)
        {
            List<string> plan = new List<string>();
            string packPath = this.PackZipPath;
            ForzaZip pack = new ForzaZip(packPath);
            ForzaZip.Entry mergeEntry = MergeEntry(pack);
            if (mergeEntry == null)
            {
                throw new InvalidOperationException("The pack has no merge database (Media\\db\\patch\\*_merge.slt).");
            }
            string folderPrefix = "Media/cars/" + this.MediaName + "/";
            ForzaZip.Entry logEntry = pack.Find(folderPrefix + "port_log.txt");
            string log = (logEntry != null) ? Encoding.UTF8.GetString(pack.Read(logEntry)) : "";

            using (SqliteDatabase db = SqliteDatabase.FromBytes(pack.Read(mergeEntry)))
            {
                object idValue = db.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", this.MediaName);
                if (idValue == null)
                {
                    throw new InvalidOperationException("The pack's database has no car " + this.MediaName + ".");
                }
                long carId = Convert.ToInt64(idValue);
                if (Convert.ToInt64(db.Scalar("SELECT count(*) FROM Data_Car")) < 2)
                {
                    throw new InvalidOperationException(this.MediaName + " is the only car of this package: a package needs one car. Delete the package file instead.");
                }
                plan.Add(string.Format(CultureInfo.InvariantCulture, "car {0}, id {1}", this.MediaName, carId));

                List<long> engines = CarUninstall.Longs(db, "SELECT DISTINCT EngineID FROM List_UpgradeEngine WHERE Ordinal = ?", carId);
                object wheel = db.Scalar("SELECT StockWheelID FROM Data_Car WHERE Id = ?", carId);
                string wheelMedia = (wheel == null) ? null : db.Scalar("SELECT MediaName FROM List_Wheels WHERE ID = ?", Convert.ToInt64(wheel)) as string;
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
                List<string> curveColumns = CarUninstall.Columns(db, "List_UpgradeEngineCamshaft").FindAll(delegate(string c) { return c.StartsWith("TorqueCurve", StringComparison.OrdinalIgnoreCase) && c.EndsWith("ID", StringComparison.OrdinalIgnoreCase); });
                foreach (long engine in engines)
                {
                    engineMedia[engine] = db.Scalar("SELECT MediaName FROM Data_Engine WHERE EngineID = ?", engine) as string;
                    foreach (string column in curveColumns)
                    {
                        foreach (long curve in CarUninstall.Longs(db, "SELECT " + column + " FROM List_UpgradeEngineCamshaft WHERE EngineID = ? AND " + column + " IS NOT NULL", engine))
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
                foreach (string table in CarUninstall.Tables(db))
                {
                    List<string> columns = CarUninstall.Columns(db, table);
                    string where = null;
                    if (table == "Data_Car")
                    {
                        where = "Id = " + carId;
                    }
                    else if (table == "ContentOffersMapping")
                    {
                        where = "ContentId = " + carId + " AND ContentType = 1";
                    }
                    else if (table.StartsWith("List_UpgradeCarBody", StringComparison.Ordinal) && columns.Count > 0 && columns[0] == "Id" && !CarUninstall.Has(columns, "Ordinal"))
                    {
                        where = "Id / 1000 = " + carId;
                    }
                    else
                    {
                        foreach (string column in new string[] { "Ordinal", "CarId", "CarID", "CarOrdinal" })
                        {
                            GameDbSchema.Relation relation = CarUninstall.Has(columns, column) ? GameDbSchema.Find(table, column) : null;
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

                // 2. What the port added for the car, once no other car of the pack uses it.
                int shared = 0;
                foreach (Match line in Regex.Matches(log, @"^  (\w+): \d+ rows \((\w+) ([\d,]+)\)\r?$", RegexOptions.Multiline))
                {
                    string table = line.Groups[1].Value;
                    string column = line.Groups[2].Value;
                    foreach (string text in line.Groups[3].Value.Split(','))
                    {
                        long id = long.Parse(text, CultureInfo.InvariantCulture);
                        string user = CarUninstall.StillUsed(db, column, id);
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
                    string user = CarUninstall.Referenced(db, table, column, id);
                    if (user != null)
                    {
                        plan.Add(string.Format(CultureInfo.InvariantCulture, "  kept {0} {1} = {2}: {3}", table, column, id, user));
                        continue;
                    }
                    shared += this.Delete(db, table, SqliteDatabase.Quote(column) + " = " + id, apply, plan);
                }
                // Engines the log does not list (a copy's own engine, aspiration conversions): an engine of the car with no user left.
                foreach (long engine in engines)
                {
                    if (CarUninstall.StillUsed(db, "EngineID", engine) != null)
                    {
                        continue;
                    }
                    shared += this.Delete(db, "Data_Engine", "EngineID = " + engine, apply, plan);
                    foreach (string table in CarUninstall.Tables(db))
                    {
                        if (table.StartsWith("List_UpgradeEngine", StringComparison.Ordinal) && table != "List_UpgradeEngine" && CarUninstall.Has(CarUninstall.Columns(db, table), "EngineID"))
                        {
                            shared += this.Delete(db, table, "EngineID = " + engine, apply, plan);
                        }
                    }
                }
                // The car's own stock wheel in the pack (a copy's, which the log does not list): no car of the pack left on it.
                if (wheel != null && wheel != DBNull.Value && DbIds.HasTable(db, "List_Wheels") && db.Scalar("SELECT ID FROM List_Wheels WHERE ID = ?", Convert.ToInt64(wheel)) != null
                    && CarUninstall.StillUsed(db, "WheelID", Convert.ToInt64(wheel)) == null)
                {
                    long wheelId = Convert.ToInt64(wheel);
                    shared += this.Delete(db, "List_Wheels", "ID = " + wheelId, apply, plan);
                    foreach (string[] t in new string[][] { new string[] { "WheelExceptions", "WheelID" }, new string[] { "WheelNormOffsets", "Id" } })
                    {
                        if (DbIds.HasTable(db, t[0]))
                        {
                            shared += this.Delete(db, t[0], t[1] + " = " + wheelId, apply, plan);
                        }
                    }
                }
                foreach (long curve in curves)
                {
                    if (CarUninstall.StillUsed(db, "TorqueCurveID", curve) == null)
                    {
                        shared += this.Delete(db, "List_TorqueCurve", "TorqueCurveID = " + curve, apply, plan);
                    }
                }
                // A make the port added, when no car of the pack is left under it.
                List<string> makeStrings = new List<string>();
                Match madeMake = Regex.Match(log, @"^make (\d+) \([^)]*\): not in FM4", RegexOptions.Multiline);
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
                        shared += this.Delete(db, "List_CarMake", "ID = " + makeId, apply, plan);
                        shared += this.Delete(db, "Livery_DecalsSortOrder", "MakeID = " + makeId, apply, plan);
                    }
                }
                plan.Add(string.Format(CultureInfo.InvariantCulture, "database: {0} car rows, {1} rows the port added for it", carRows, shared));

                // 3. Strings no row uses any more.
                List<int> carKeys = CarUninstall.UnusedKeys(db, "Data_Car", new string[] { "DisplayName", "ModelShort" }, carStrings);
                List<int> makeKeys = CarUninstall.UnusedKeys(db, "List_CarMake", new string[] { "DisplayName", "Profile" }, makeStrings);
                ForzaZip strings = null;
                string stringsPath = null;
                foreach (string file in Directory.GetFiles(Path.Combine(Path.Combine(this.PackageFolder, "Media"), "DLCZips"), "StringTables_pri_*.zip"))
                {
                    stringsPath = file;
                }
                int stringFiles = 0;
                if (stringsPath != null && (carKeys.Count > 0 || makeKeys.Count > 0))
                {
                    strings = new ForzaZip(stringsPath);
                    stringFiles += this.RemoveStrings(strings, "Data_Car.str", carKeys);
                    stringFiles += this.RemoveStrings(strings, "List_CarMake.str", makeKeys);
                }
                plan.Add(string.Format(CultureInfo.InvariantCulture, "strings: {0} car keys, {1} make keys, {2} string files changed", carKeys.Count, makeKeys.Count, stringFiles));

                // 4. Files of the pack zip.
                List<ForzaZip.Entry> gone = new List<ForzaZip.Entry>();
                gone.AddRange(pack.Entries.FindAll(delegate(ForzaZip.Entry e) { return e.Name.Replace('\\', '/').StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase); }));
                plan.Add(string.Format(CultureInfo.InvariantCulture, "files: {0} in Media\\cars\\{1}\\", gone.Count, this.MediaName));
                List<string> wheelFolders = new List<string>();
                foreach (Match zip in Regex.Matches(log, @"^(?:wheel|tire) zip: Media\\wheels\\([^ \r\n\\]+)\.zip", RegexOptions.Multiline))
                {
                    wheelFolders.Add(zip.Groups[1].Value);
                }
                if (wheelMedia != null && !wheelFolders.Contains(wheelMedia))
                {
                    wheelFolders.Add(wheelMedia);
                }
                foreach (string kind in new string[] { "A", "B", "C" })
                {
                    // the car's own tire models are named after the car (D-16)
                    if (!wheelFolders.Contains("Tire" + kind + this.MediaName))
                    {
                        wheelFolders.Add("Tire" + kind + this.MediaName);
                    }
                }
                if (wheel != null && Convert.ToInt64(db.Scalar("SELECT count(*) FROM List_Wheels WHERE ID = ?", Convert.ToInt64(wheel))) == 0)
                {
                    foreach (string folder in wheelFolders)
                    {
                        this.Collect(pack, "Media/wheels/" + folder + "/", gone, plan);
                    }
                }
                foreach (KeyValuePair<long, string> engine in engineMedia)
                {
                    if (engine.Value != null && db.Scalar("SELECT EngineID FROM Data_Engine WHERE EngineID = ?", engine.Key) == null
                        && Convert.ToInt64(db.Scalar("SELECT count(*) FROM Data_Engine WHERE MediaName = ? COLLATE NOCASE", engine.Value)) == 0)
                    {
                        this.Collect(pack, "Media/audio/Cars/Engines/enginetuning/" + engine.Value + "_ET.xml", gone, plan);
                    }
                }
                this.Collect(pack, "Media/audio/Cars/CarModelTuning/" + this.MediaName + "_CMT.xml", gone, plan);
                if (thumbnail != null)
                {
                    string file = Path.GetFileName(thumbnail.Replace('\\', '/'));
                    if (file.Length > 0 && Convert.ToInt64(db.Scalar("SELECT count(*) FROM Data_Car WHERE Thumbnail LIKE ?", "%" + file)) == 0)
                    {
                        foreach (ForzaZip.Entry entry in pack.Entries)
                        {
                            if (entry.Name.Replace('\\', '/').EndsWith("/" + file, StringComparison.OrdinalIgnoreCase) && !gone.Contains(entry))
                            {
                                gone.Add(entry);
                                plan.Add("file " + entry.Name);
                            }
                        }
                    }
                }
                plan.Add("kept: harmonic tunings and sound banks (other engine tunings may play them)");

                if (!apply)
                {
                    return plan;
                }

                foreach (ForzaZip.Entry entry in gone)
                {
                    pack.Remove(entry);
                }
                pack.Replace(mergeEntry, db.ToBytes());
                pack.Save(packPath);
                this.Line("saved {0}: {1} files out, merge database rewritten", Path.GetFileName(packPath), gone.Count);
                if (strings != null)
                {
                    strings.Save(stringsPath);
                    this.Line("saved {0}", Path.GetFileName(stringsPath));
                }
            }
            return plan;
        }

        // Adds the entry with this name, or every entry below this folder (name ending in '/'), to the files going.
        private void Collect(ForzaZip pack, string name, List<ForzaZip.Entry> gone, List<string> plan)
        {
            int count = 0;
            foreach (ForzaZip.Entry entry in pack.Entries)
            {
                string entryName = entry.Name.Replace('\\', '/');
                bool match = name.EndsWith("/", StringComparison.Ordinal) ? entryName.StartsWith(name, StringComparison.OrdinalIgnoreCase) : string.Equals(entryName, name, StringComparison.OrdinalIgnoreCase);
                if (match && !gone.Contains(entry))
                {
                    gone.Add(entry);
                    count++;
                }
            }
            if (count > 0)
            {
                plan.Add(string.Format(CultureInfo.InvariantCulture, "files: {0} {1}", count, name.Replace('/', '\\')));
            }
        }

        private int RemoveStrings(ForzaZip strings, string strName, List<int> keys)
        {
            if (keys.Count == 0)
            {
                return 0;
            }
            int files = 0;
            foreach (ForzaZip.Entry entry in strings.Entries)
            {
                if (!entry.Name.Replace('\\', '/').EndsWith("/" + strName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                StrFile str = new StrFile(strings.Read(entry));
                int removed = 0;
                foreach (int key in keys)
                {
                    removed += str.Remove(key) ? 1 : 0;
                }
                if (removed > 0)
                {
                    strings.Replace(entry, str.ToBytes());
                    files++;
                }
            }
            return files;
        }

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
            return count;
        }

        private void Line(string format, params object[] args)
        {
            this.Report.AppendLine(string.Format(CultureInfo.InvariantCulture, format, args));
        }
    }
}
