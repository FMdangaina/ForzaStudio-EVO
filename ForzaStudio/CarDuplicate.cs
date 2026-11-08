namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;

    // Copies a car installed in an FM4 folder under a new MediaName and Id (ROADMAP 18.3). FM4 is the only
    // reference; the rules come from the audit of the base game and its 16 DLC packages (tools/FM4Audit):
    //   Id        the first free car Id above every car of FM4 and its DLC (DbIds.InstallFirstCar), also free
    //             as car * 1000 in the car body rows;
    //   database  DbIds.RenumberCar on a copy of the game database moves the car's rows (car columns and the
    //             keys car * 1000 + n) to the new Id; the rows that are new in that copy are inserted. Rows
    //             that only point at the car (an event's car, a showroom challenge) are not copied;
    //   strings   DisplayName / ModelShort get a key of their own in Data_Car.str of every language, a key no
    //             FM4 package uses (GameStringKeys), named IDS_DisplayName_<Id> / IDS_ModelShort_<Id>;
    //   files     the car zip under the new name (every entry name and the text files, case kept), the
    //             thumbnail Thumbnail_<Id>.xds in the folder of the source's, zipmanifest.xml lines;
    //   wheel     a stock wheel of its own: List_Wheels row (next free ID, MediaName = the new car's), its
    //             WheelExceptions / WheelNormOffsets rows, Media\wheels\<new>.zip and the car's TireA/B/C<new>.zip.
    // The engine, drivetrain and the other shared rows stay shared with the source car; a wizard
    // step gives the copy its own engine. Plan() reports without writing, Run() does it, every file
    // backed up first.
    public class CarDuplicate
    {
        public string TargetRoot;
        public string SourceMediaName;
        public string NewMediaName;
        public string XeniaContent;                 // installed packs: their car Ids stay free
        public string DisplayName;                  // EN text for every language lacking its own; null = the source's
        public string ModelShort;
        public Dictionary<string, string> DisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);     // language (EN, IT...) -> text
        public Dictionary<string, string> ModelShorts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public long? Year;
        public long? MakeId;
        public Dictionary<string, object> Data = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);              // Data_Car column -> value, applied last
        public string BackupFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bak");
        public StringBuilder Report = new StringBuilder();
        public long NewId;
        public EngineSpec Engine;                   // an engine of its own for the copy (CarEngine); null = the source's, shared
        public GearboxSpec Gearbox;                 // a gearbox (drivetrain) of its own for the copy (CarGearbox); null = the source's, shared

        // A DLC pack instead of the game folder: the copy is built on a staged part of the game (the database,
        // the string tables, the source's zip and thumbnail zip), then DlcPackage.BuildFolder turns what
        // changed into the pack in Xenia's content folder. The game folder itself is not touched.
        public string PackContent;                  // Xenia's content folder; null = write into the game folder
        public int PackOffer;                       // the pack's offer number
        public string PackName;                     // the name the pack has in the game's store
        public bool ReplacePack;                    // the offer exists in PackContent: remove it first
        public string ConsoleOutput;                // a folder: the pack is saved there as an STFS package file (Xbox 360; Xenia reads it too) instead of a folder in PackContent
        public byte[] Thumbnail;                    // the picture the dashboard shows for the package file (64 x 64 PNG), or null
        public string PackageFile;                  // the package file that was written (ConsoleOutput)

        private string stamp;

        public List<string> Plan()
        {
            List<string> plan = this.Execute(false);
            if (this.PackContent != null || this.ConsoleOutput != null)
            {
                plan.Insert(0, string.Format(CultureInfo.InvariantCulture, "destination: a new DLC pack \"{0}\" (offer {1}) {2}; the game folder is not changed", this.PackName, this.PackOffer, (this.ConsoleOutput != null) ? "as a package file in " + this.ConsoleOutput : "in " + this.PackContent));
                this.Report.Insert(0, plan[0] + "\r\n");
            }
            return plan;
        }

        public void Run()
        {
            if (this.PackContent == null && this.ConsoleOutput == null)
            {
                this.Execute(true);
                return;
            }
            this.RunPack();
        }

        private void RunPack()
        {
            if (string.IsNullOrEmpty(this.PackName))
            {
                throw new InvalidOperationException("The pack needs a name.");
            }
            if (this.PackOffer <= 0)
            {
                throw new InvalidOperationException("The pack needs an offer number.");
            }
            string work = (this.ConsoleOutput != null) ? Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "PackFile_" + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture)) : null;
            string content = (work != null) ? Path.Combine(work, "00000002") : this.PackContent;      // the folder the pack is built in (a temporary one for a package file)
            string installed = (work != null) ? (this.XeniaContent ?? content) : content;           // whose strings and ids the pack is built on
            if (work == null && Directory.Exists(DlcPackage.PackagePath(content, this.PackOffer)))
            {
                if (!this.ReplacePack)
                {
                    throw new InvalidOperationException("A package with offer " + this.PackOffer + " is already in Xenia's content folder.");
                }
                DlcPackage.Remove(content, this.PackOffer);
            }
            string stage = Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "DuplicatePack_" + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture));
            try
            {
                this.Stage(stage);
                CarDuplicate inner = new CarDuplicate();
                inner.TargetRoot = stage;
                inner.SourceMediaName = this.SourceMediaName;
                inner.NewMediaName = this.NewMediaName;
                inner.XeniaContent = this.XeniaContent ?? this.PackContent ?? installed;
                inner.DisplayName = this.DisplayName;
                inner.ModelShort = this.ModelShort;
                inner.Year = this.Year;
                inner.MakeId = this.MakeId;
                foreach (KeyValuePair<string, string> pair in this.DisplayNames)
                {
                    inner.DisplayNames[pair.Key] = pair.Value;
                }
                foreach (KeyValuePair<string, string> pair in this.ModelShorts)
                {
                    inner.ModelShorts[pair.Key] = pair.Value;
                }
                foreach (KeyValuePair<string, object> pair in this.Data)
                {
                    inner.Data[pair.Key] = pair.Value;
                }
                inner.Engine = this.Engine;
                inner.Gearbox = this.Gearbox;
                inner.BackupFolder = Path.Combine(stage, "bak");
                inner.Run();
                this.NewId = inner.NewId;
                this.Report.Append(inner.Report.ToString());
                StringBuilder text = new StringBuilder();
                List<string> others = DbIds.PackDatabases(installed);
                string package = DlcPackage.BuildFolder(this.TargetRoot, this.TargetRoot, stage, content, installed, this.PackOffer, this.PackName, text);

                // The package's database as the game will merge it into its own (and the other packs'): the
                // same integrity rules, so a pack never leaves a broken database behind.
                string offerFolder = this.PackOffer.ToString(CultureInfo.InvariantCulture) + "_pri_65";
                string patch = Path.Combine(Path.Combine(Path.Combine(Path.Combine(Path.Combine(Path.Combine(package, "Media"), "DLCZips"), offerFolder), "Media"), "db"), "patch");
                string mergeFile = Path.Combine(patch, this.PackOffer.ToString(CultureInfo.InvariantCulture) + "000_merge.slt");
                DbIntegrity.Result merged = DbIntegrity.CheckMerge(File.ReadAllBytes(Path.Combine(Path.Combine(this.Media, "db"), "gamedb.slt")), others, File.ReadAllBytes(mergeFile), ForzaGame.Motorsport4);
                if (!merged.Ok)
                {
                    throw new InvalidOperationException("The pack's database would break the game's, nothing was left in Xenia:\r\n  " + string.Join("\r\n  ", merged.Errors.ToArray()));
                }
                this.Report.AppendLine("pack database check: merged into the game's database the rules hold");
                foreach (string line in merged.Warnings)
                {
                    this.Report.AppendLine("  warning: " + line);
                }
                if (this.ConsoleOutput != null)
                {
                    string file = DlcPackage.BuildConsolePackage(package, this.ConsoleOutput, this.PackName, this.Thumbnail, text);
                    this.PackageFile = file;
                }
                this.Report.AppendLine("DLC package \"" + this.PackName + "\" (offer " + this.PackOffer + ") " + ((this.ConsoleOutput != null) ? "as a package file in " + this.ConsoleOutput : "in " + this.PackContent) + ":");
                this.Report.Append(text.ToString());
            }
            catch (Exception)
            {
                try
                {
                    DlcPackage.Remove(content, this.PackOffer);
                }
                catch (IOException)
                {
                }
                throw;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(stage))
                    {
                        Directory.Delete(stage, true);
                    }
                    if (work != null && Directory.Exists(work))
                    {
                        Directory.Delete(work, true);
                    }
                }
                catch (IOException)
                {
                }
            }
        }

        // The files a copy reads or changes, at the same place under a temporary root.
        private void Stage(string stage)
        {
            List<string> files = new List<string>();
            files.Add(Path.Combine(Path.Combine(this.Media, "db"), "gamedb.slt"));
            files.Add(ZipManifest.PathFor(this.TargetRoot));
            files.Add(Path.Combine(Path.Combine(this.Media, "cars"), this.SourceMediaName + ".zip"));
            files.AddRange(Directory.GetFiles(Path.Combine(this.Media, "StringTables"), "*.zip"));
            string audio = Path.Combine(Path.Combine(this.Media, "audio"), "cars");
            foreach (string tuning in new string[] { Path.Combine(audio, "CarModelTuning.zip"), Path.Combine(Path.Combine(audio, "Engines"), "enginetuning.zip") })
            {
                if (File.Exists(tuning))
                {
                    files.Add(tuning);
                }
            }
            using (SqliteDatabase db = SqliteDatabase.FromBytes(File.ReadAllBytes(files[0])))
            {
                // the stock wheel's zip and the car's own tire zips, copied under the new name
                string wheels = Path.Combine(this.Media, "wheels");
                string wheel = db.Scalar("SELECT w.MediaName FROM List_Wheels w JOIN Data_Car c ON c.StockWheelID = w.ID WHERE c.MediaName = ? COLLATE NOCASE", this.SourceMediaName) as string;
                List<string> wheelNames = new List<string>();
                if (wheel != null)
                {
                    wheelNames.Add(wheel);
                }
                wheelNames.Add("TireA" + this.SourceMediaName);
                wheelNames.Add("TireB" + this.SourceMediaName);
                wheelNames.Add("TireC" + this.SourceMediaName);
                foreach (string name in wheelNames)
                {
                    string zip = Path.Combine(wheels, name + ".zip");
                    if (File.Exists(zip))
                    {
                        files.Add(zip);
                    }
                }
                string thumbnail = db.Scalar("SELECT Thumbnail FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", this.SourceMediaName) as string;
                if (!string.IsNullOrEmpty(thumbnail))
                {
                    GamePaths.GameFile from = GamePaths.Locate(this.TargetRoot, thumbnail);
                    if (from != null && from.InZip)
                    {
                        files.Add(from.ZipPath);
                    }
                }
            }
            string root = this.TargetRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (string file in files)
            {
                if (!file.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(file + " is not under the game folder.");
                }
                string copy = Path.Combine(stage, file.Substring(root.Length));
                Directory.CreateDirectory(Path.GetDirectoryName(copy));
                File.Copy(file, copy, true);
            }
        }

        private string Media
        {
            get { return Path.Combine(this.TargetRoot, "Media"); }
        }

        // The name is a file name of the game: letters, digits, underscore.
        public static bool IsValidName(string name)
        {
            return name != null && Regex.IsMatch(name, "^[A-Za-z0-9_]{3,40}$");
        }

        private List<string> Execute(bool apply)
        {
            this.stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
            List<string> plan = new List<string>();
            if (!IsValidName(this.NewMediaName))
            {
                throw new InvalidOperationException("The new MediaName must be 3-40 letters, digits or underscores (like ACU_NSX_97).");
            }
            string carsFolder = Path.Combine(this.Media, "cars");
            string sourceZipPath = Path.Combine(carsFolder, this.SourceMediaName + ".zip");
            string newZipPath = Path.Combine(carsFolder, this.NewMediaName + ".zip");
            if (!File.Exists(sourceZipPath))
            {
                throw new InvalidOperationException("The FM4 folder has no " + this.SourceMediaName + ".zip. If it is a car of a DLC pack, copying DLC cars is not supported yet (TODO NOW 2b): copy a car of the base game, or port the DLC car.");
            }
            if (File.Exists(newZipPath))
            {
                throw new InvalidOperationException(this.NewMediaName + ".zip already exists.");
            }
            string dbPath = Path.Combine(Path.Combine(this.Media, "db"), "gamedb.slt");
            string manifestPath = ZipManifest.PathFor(this.TargetRoot);
            SqliteDatabase db = SqliteDatabase.FromBytes(File.ReadAllBytes(dbPath));
            SqliteDatabase original = SqliteDatabase.FromBytes(File.ReadAllBytes(dbPath));
            SqliteDatabase work = null;
            bool writing = false;
            try
            {
                object sourceValue = db.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", this.SourceMediaName);
                if (sourceValue == null)
                {
                    throw new InvalidOperationException("The database has no car " + this.SourceMediaName + ".");
                }
                if (db.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", this.NewMediaName) != null)
                {
                    throw new InvalidOperationException("The database already has a car " + this.NewMediaName + ".");
                }
                long sourceId = Convert.ToInt64(sourceValue, CultureInfo.InvariantCulture);
                string reason;
                DbIds.PackContent = this.XeniaContent;
                this.NewId = DbIds.CarId(db, db, this.NewMediaName, DbIds.PackDatabases(this.XeniaContent), DbIds.InstallFirstCar, false, out reason);
                plan.Add(string.Format(CultureInfo.InvariantCulture, "{0} (id {1}) -> {2}: id {3}, {4}", this.SourceMediaName, sourceId, this.NewMediaName, this.NewId, reason));

                // Database: the rows of a renumbered copy that the game database lacks.
                work = SqliteDatabase.FromBytes(db.ToBytes());
                plan.AddRange(DbIds.RenumberCar(work, sourceId, this.NewId));
                Dictionary<string, List<string>> schema = DbIds.Schema(db);
                List<string[]> carColumns = DbIds.CarColumns(schema);
                // Every write to the database is one transaction: an error puts all of it back.
                db.Begin();
                writing = true;
                int inserted = 0;
                List<string> inserts = new List<string>();
                List<string> skipped = new List<string>();
                foreach (KeyValuePair<string, List<string>> table in schema)
                {
                    List<string> columns = table.Value;
                    string key = DbIds.KeyColumn(db, table.Key, columns);
                    int keyIndex = (key == null) ? -1 : columns.FindIndex(delegate(string c) { return string.Equals(c, key, StringComparison.OrdinalIgnoreCase); });
                    List<int> carIndexes = new List<int>();
                    foreach (string[] car in carColumns)
                    {
                        if (string.Equals(car[0], table.Key, StringComparison.OrdinalIgnoreCase))
                        {
                            carIndexes.Add(columns.FindIndex(delegate(string c) { return string.Equals(c, car[1], StringComparison.OrdinalIgnoreCase); }));
                        }
                    }
                    bool keyIsCar = keyIndex >= 0 && carIndexes.Contains(keyIndex);
                    SqliteDatabase.Result before = db.Query("SELECT * FROM " + SqliteDatabase.Quote(table.Key));
                    SqliteDatabase.Result after = work.Query("SELECT * FROM " + SqliteDatabase.Quote(table.Key));
                    Dictionary<string, bool> had = new Dictionary<string, bool>();
                    foreach (object[] row in before.Rows)
                    {
                        had[RowText(row)] = true;
                    }
                    int copied = 0;
                    foreach (object[] row in after.Rows)
                    {
                        if (had.ContainsKey(RowText(row)))
                        {
                            continue;
                        }
                        bool take;
                        if (table.Key == "Data_Car")
                        {
                            take = Convert.ToInt64(row[0], CultureInfo.InvariantCulture) == this.NewId;
                        }
                        else if (keyIndex >= 0 && row[keyIndex] is long && (long) row[keyIndex] >= this.NewId * 1000 && (long) row[keyIndex] <= (this.NewId * 1000) + 999)
                        {
                            take = true;
                        }
                        else if (carIndexes.Count > 0 && (keyIndex < 0 || keyIsCar))
                        {
                            take = false;
                            foreach (int index in carIndexes)
                            {
                                take = take || (index >= 0 && row[index] is long && (long) row[index] == this.NewId);
                            }
                        }
                        else if (string.Equals(table.Key, "Combo_Engines", StringComparison.OrdinalIgnoreCase) && keyIndex >= 0 && carIndexes.Exists(delegate(int i) { return i >= 0 && row[i] is long && (long) row[i] == this.NewId; }))
                        {
                            // The old engine table has a counter of its own (EngineComboID), not car * 1000 + n: the
                            // copy's row (same engine, same stock flag) gets the next free key.
                            row[keyIndex] = DbIds.Next(db, table.Key, key, ForzaGame.Motorsport4);
                            take = true;
                        }
                        else
                        {
                            take = false;
                        }
                        if (!take)
                        {
                            bool pointsAtCar = false;
                            foreach (int index in carIndexes)
                            {
                                pointsAtCar = pointsAtCar || (index >= 0 && row[index] is long && (long) row[index] == this.NewId);
                            }
                            if (pointsAtCar)
                            {
                                skipped.Add(table.Key);
                            }
                            continue;
                        }
                        List<string> quoted = columns.ConvertAll<string>(delegate(string c) { return SqliteDatabase.Quote(c); });
                        string sql = "INSERT INTO " + SqliteDatabase.Quote(table.Key) + " (" + string.Join(",", quoted.ToArray()) + ") VALUES (" + string.Join(",", columns.ConvertAll<string>(delegate(string c) { return "?"; }).ToArray()) + ")";
                        db.Execute(sql, row);
                        copied++;
                    }
                    if (copied > 0)
                    {
                        inserted += copied;
                        inserts.Add(table.Key + " " + copied);
                    }
                }
                plan.Add(string.Format(CultureInfo.InvariantCulture, "database: {0} rows in {1} tables ({2})", inserted, inserts.Count, string.Join(", ", inserts.ToArray())));
                if (skipped.Count > 0)
                {
                    plan.Add("  rows that only point at the car were not copied: " + string.Join(", ", skipped.ToArray()));
                }

                // Data_Car of the copy.
                string thumbnailName = "Thumbnail_" + this.NewId.ToString(CultureInfo.InvariantCulture) + ".xds";
                string oldThumbnail = db.Scalar("SELECT Thumbnail FROM Data_Car WHERE Id = ?", sourceId) as string;
                string newThumbnail = null;
                if (!string.IsNullOrEmpty(oldThumbnail))
                {
                    newThumbnail = oldThumbnail.Substring(0, oldThumbnail.LastIndexOf('\\') + 1) + thumbnailName;
                }
                db.Execute("UPDATE Data_Car SET MediaName = ? WHERE Id = ?", this.NewMediaName, this.NewId);
                if (newThumbnail != null)
                {
                    db.Execute("UPDATE Data_Car SET Thumbnail = ? WHERE Id = ?", newThumbnail, this.NewId);
                }
                if (this.Year.HasValue)
                {
                    db.Execute("UPDATE Data_Car SET Year = ? WHERE Id = ?", this.Year.Value, this.NewId);
                }
                if (this.MakeId.HasValue)
                {
                    object makeName = db.Scalar("SELECT DisplayName FROM List_CarMake WHERE ID = ?", this.MakeId.Value);
                    if (makeName == null)
                    {
                        throw new InvalidOperationException("List_CarMake has no make " + this.MakeId.Value + ".");
                    }
                    db.Execute("UPDATE Data_Car SET MakeID = ?, MakeName = ? WHERE Id = ?", this.MakeId.Value, makeName, this.NewId);
                    object icon = db.Scalar("SELECT MakeIcon FROM Data_Car WHERE MakeID = ? AND Id <> ? LIMIT 1", this.MakeId.Value, this.NewId);
                    if (icon != null)
                    {
                        db.Execute("UPDATE Data_Car SET MakeIcon = ? WHERE Id = ?", icon, this.NewId);
                    }
                }
                foreach (KeyValuePair<string, object> pair in this.Data)
                {
                    if (!schema["Data_Car"].Exists(delegate(string c) { return string.Equals(c, pair.Key, StringComparison.OrdinalIgnoreCase); }))
                    {
                        throw new InvalidOperationException("Data_Car has no column " + pair.Key + ".");
                    }
                    db.Execute("UPDATE Data_Car SET " + SqliteDatabase.Quote(pair.Key) + " = ? WHERE Id = ?", pair.Value, this.NewId);
                }

                string soundSource = null, ownEngineMedia = null;
                bool turboSound = false;
                // The copy's own engine (CarEngine), before the checks. Its sound is set up the way the port does it: the
                // engine keeps a MediaName of its own (the car's) and gets an engine tuning file of that name, a copy of the
                // chosen engine's (or the source's) one, written below.
                if (this.Engine != null)
                {
                    long engineId = CarEngine.Clone(db, this.NewId, this.Engine, plan);
                    soundSource = Convert.ToString(db.Scalar("SELECT MediaName FROM Data_Engine WHERE EngineID = ?", engineId), CultureInfo.InvariantCulture);
                    ownEngineMedia = this.NewMediaName;
                    long aspiration = Convert.ToInt64(db.Scalar("SELECT AspirationID_Stock FROM Data_Engine WHERE EngineID = ?", engineId), CultureInfo.InvariantCulture);
                    turboSound = aspiration >= 2 && aspiration <= 4;
                    while (db.Scalar("SELECT EngineID FROM Data_Engine WHERE MediaName = ? COLLATE NOCASE", ownEngineMedia) != null)
                    {
                        ownEngineMedia += "_E";
                    }
                    db.Execute("UPDATE Data_Engine SET MediaName = ? WHERE EngineID = ?", ownEngineMedia, engineId);
                }
                // The copy's own gearbox (CarGearbox); the performance numbers follow the engine and the gearbox.
                if (this.Gearbox != null)
                {
                    CarGearbox.Clone(db, this.NewId, this.Gearbox, plan);
                }
                if (this.Engine != null || this.Gearbox != null)
                {
                    CarPerformance.Update(db, original, sourceId, this.NewId, this.Data.Keys, plan);
                }

                // The copy's own stock wheel: a List_Wheels row of its own under the copy's name (FM4's stock wheels are
                // named after their car, 454 of 503, IconPath = MediaName 466 of 469), its WheelExceptions and
                // WheelNormOffsets rows; the wheel zip is written below. Never the source's wheel (user 2026-11-07).
                string sourceWheelMedia = null, newWheelMedia = null;
                object sourceWheel = db.Scalar("SELECT StockWheelID FROM Data_Car WHERE Id = ?", sourceId);
                if (sourceWheel != null && sourceWheel != DBNull.Value)
                {
                    long oldWheel = Convert.ToInt64(sourceWheel, CultureInfo.InvariantCulture);
                    sourceWheelMedia = db.Scalar("SELECT MediaName FROM List_Wheels WHERE ID = ?", oldWheel) as string;
                    if (sourceWheelMedia == null)
                    {
                        throw new InvalidOperationException("The source's stock wheel " + oldWheel + " has no List_Wheels row.");
                    }
                    newWheelMedia = this.NewMediaName;
                    if (db.Scalar("SELECT ID FROM List_Wheels WHERE MediaName = ? COLLATE NOCASE", newWheelMedia) != null || File.Exists(Path.Combine(Path.Combine(this.Media, "wheels"), newWheelMedia + ".zip")))
                    {
                        throw new InvalidOperationException("A wheel " + newWheelMedia + " already exists.");
                    }
                    long newWheel = DbIds.Next(db, "List_Wheels", "ID");
                    CopyRow(db, "List_Wheels", "ID", oldWheel, newWheel);
                    db.Execute("UPDATE List_Wheels SET MediaName = ? WHERE ID = ?", newWheelMedia, newWheel);
                    db.Execute("UPDATE List_Wheels SET IconPath = ? WHERE ID = ? AND IconPath = ? COLLATE NOCASE", newWheelMedia, newWheel, sourceWheelMedia);
                    int extra = CopyRow(db, "WheelExceptions", "WheelID", oldWheel, newWheel) + CopyRow(db, "WheelNormOffsets", "Id", oldWheel, newWheel);
                    db.Execute("UPDATE Data_Car SET StockWheelID = ? WHERE Id = ?", newWheel, this.NewId);
                    plan.Add(string.Format(CultureInfo.InvariantCulture, "wheel: {0} (List_Wheels {1}), a copy of {2} (id {3}){4}", newWheelMedia, newWheel, sourceWheelMedia, oldWheel, (extra > 0) ? ", with its WheelExceptions / WheelNormOffsets rows" : string.Empty));
                }

                // The integrity rules (DbIntegrity, DbIds.CheckCar): nothing is written when one breaks.
                List<string> errors = DbIds.CheckCar(db, this.NewId, sourceId, this.NewMediaName);
                DbIntegrity.Result check = DbIntegrity.Compare(original, db, ForzaGame.Motorsport4, null);
                errors.AddRange(check.Errors);
                if (errors.Count > 0)
                {
                    throw new InvalidOperationException("Database check failed, nothing was written:\r\n  " + string.Join("\r\n  ", errors.ToArray()));
                }
                db.Commit();
                writing = false;
                plan.Add("database check: the rules hold (keys above FM4's largest and unique, every id points at a row, the car's Data_CarBody row, ModelId = Id)");
                foreach (string line in check.Summary)
                {
                    plan.Add("  " + line);
                }
                foreach (string line in check.Warnings)
                {
                    plan.Add("  warning: " + line);
                }

                // Strings: a key of its own in Data_Car.str of every language.
                Dictionary<string, StrFile> files = new Dictionary<string, StrFile>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, ForzaZip> stringZips = new Dictionary<string, ForzaZip>(StringComparer.OrdinalIgnoreCase);
                string tables = Path.Combine(this.Media, "StringTables");
                foreach (string zipPath in Directory.GetFiles(tables, "*.zip"))
                {
                    ForzaZip archive = new ForzaZip(zipPath);
                    ForzaZip.Entry entry = archive.Find("Data_Car.str");
                    if (entry != null)
                    {
                        stringZips[zipPath] = archive;
                        files[zipPath] = new StrFile(archive.Read(entry));
                    }
                }
                string[][] stringColumns = { new string[] { "DisplayName", "IDS_DisplayName_" }, new string[] { "ModelShort", "IDS_ModelShort_" } };
                foreach (string[] column in stringColumns)
                {
                    string value = db.Scalar("SELECT " + column[0] + " FROM Data_Car WHERE Id = ?", sourceId) as string;
                    uint stringId;
                    if (value == null || !value.StartsWith("_&", StringComparison.Ordinal) || !uint.TryParse(value.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out stringId))
                    {
                        continue;
                    }
                    int oldKey = (int) (stringId & 0xFFFF);
                    int newKey = GameStringKeys.Free("Data_Car", files.Values);
                    Dictionary<string, string> perLanguage = (column[0] == "DisplayName") ? this.DisplayNames : this.ModelShorts;
                    string common = (column[0] == "DisplayName") ? this.DisplayName : this.ModelShort;
                    foreach (KeyValuePair<string, StrFile> file in files)
                    {
                        string language = Path.GetFileNameWithoutExtension(file.Key);
                        string text = null;
                        string given;
                        if (perLanguage.TryGetValue(language, out given) && !string.IsNullOrEmpty(given))
                        {
                            text = given;
                        }
                        else if (!string.IsNullOrEmpty(common))
                        {
                            text = common;
                        }
                        else
                        {
                            foreach (StrFile.Entry old in file.Value.Entries)
                            {
                                if (old.Key == oldKey)
                                {
                                    text = old.Text;
                                }
                            }
                        }
                        if (!string.IsNullOrEmpty(text))
                        {
                            file.Value.Set(newKey, text, column[1] + this.NewId.ToString(CultureInfo.InvariantCulture));
                        }
                    }
                    string newValue = "_&" + ((stringId & 0xFFFF0000u) | (uint) newKey).ToString(CultureInfo.InvariantCulture);
                    db.Execute("UPDATE Data_Car SET " + column[0] + " = ? WHERE Id = ?", newValue, this.NewId);
                    plan.Add(string.Format(CultureInfo.InvariantCulture, "strings: {0} key {1} (free in FM4 and its DLC) in {2} languages", column[0], newKey, files.Count));
                }

                // Car zip under the new name.
                ForzaZip sourceZip = new ForzaZip(sourceZipPath);
                ForzaZip newZip = ForzaZip.CreateLike(sourceZip);
                Regex name = new Regex(Regex.Escape(this.SourceMediaName), RegexOptions.IgnoreCase);
                string lower = this.NewMediaName.ToLowerInvariant();
                MatchEvaluator keepCase = delegate(Match m) { return (m.Value == m.Value.ToLowerInvariant()) ? lower : this.NewMediaName; };
                Encoding bytes = Encoding.GetEncoding(28591);
                int renamed = 0;
                int edited = 0;
                foreach (ForzaZip.Entry entry in sourceZip.Entries)
                {
                    if (entry.IsDirectory)
                    {
                        continue;
                    }
                    byte[] data = sourceZip.Read(entry);
                    string entryName = name.Replace(entry.Name, keepCase);
                    if (entryName != entry.Name)
                    {
                        renamed++;
                    }
                    string ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                    if (ext == ".xml" || ext == ".txt" || ext == ".html")
                    {
                        string text = bytes.GetString(data);
                        string replaced = name.Replace(text, keepCase);
                        if (replaced != text)
                        {
                            data = bytes.GetBytes(replaced);
                            edited++;
                        }
                    }
                    newZip.Add(entryName, data);
                }
                plan.Add(string.Format(CultureInfo.InvariantCulture, "car zip: {0} files, {1} renamed, {2} text files with the name replaced", newZip.Entries.Count, renamed, edited));

                // Wheel zip and the car's own tire zips (Media\wheels\TireA/B/C<car>.zip, found by the car's name, D-16)
                // under the new names, the same renaming as the car zip.
                string wheelsFolder = Path.Combine(this.Media, "wheels");
                List<KeyValuePair<string, ForzaZip>> wheelZips = new List<KeyValuePair<string, ForzaZip>>();
                List<string> wheelLikes = new List<string>();
                if (sourceWheelMedia != null)
                {
                    string from = Path.Combine(wheelsFolder, sourceWheelMedia + ".zip");
                    if (!File.Exists(from))
                    {
                        throw new InvalidOperationException("The FM4 folder has no Media\\wheels\\" + sourceWheelMedia + ".zip (the source's stock wheel).");
                    }
                    wheelZips.Add(new KeyValuePair<string, ForzaZip>(Path.Combine(wheelsFolder, newWheelMedia + ".zip"), RenamedZip(from, sourceWheelMedia, newWheelMedia)));
                    wheelLikes.Add(sourceWheelMedia);
                    plan.Add("wheel zip: Media\\wheels\\" + newWheelMedia + ".zip (copy of " + sourceWheelMedia + ".zip)");
                }
                foreach (string kind in new string[] { "A", "B", "C" })
                {
                    string from = Path.Combine(wheelsFolder, "Tire" + kind + this.SourceMediaName + ".zip");
                    string to = Path.Combine(wheelsFolder, "Tire" + kind + this.NewMediaName + ".zip");
                    if (File.Exists(from) && !File.Exists(to))
                    {
                        wheelZips.Add(new KeyValuePair<string, ForzaZip>(to, RenamedZip(from, "Tire" + kind + this.SourceMediaName, "Tire" + kind + this.NewMediaName)));
                        wheelLikes.Add("Tire" + kind + this.SourceMediaName);
                        plan.Add("tire zip: Media\\wheels\\" + Path.GetFileName(to) + " (copy of " + Path.GetFileName(from) + ")");
                    }
                }

                // Thumbnail: the source's file under the new name, in the same zip and folder.
                ForzaZip thumbnailZip = null;
                string thumbnailZipPath = null;
                if (oldThumbnail != null)
                {
                    GamePaths.GameFile from = GamePaths.Locate(this.TargetRoot, oldThumbnail);
                    if (from != null && from.InZip)
                    {
                        thumbnailZipPath = from.ZipPath;
                        thumbnailZip = new ForzaZip(from.ZipPath);
                        thumbnailZip.Add(from.EntryName.Substring(0, from.EntryName.LastIndexOf('/') + 1) + thumbnailName, GamePaths.Read(from));
                        plan.Add("thumbnail: " + thumbnailName + " in " + Path.GetFileName(from.ZipPath));
                    }
                    else
                    {
                        plan.Add("thumbnail: the source's file was not found in a zip, none made");
                    }
                }

                // Sound: the game looks a car's audio up by its own name (audio\cars\CarModelTuning.zip: <car>_CMT.xml, where its
                // engine, exhaust and transmission sit) and its engine's by the engine's MediaName (Engines\enginetuning.zip:
                // <engine>_ET.xml). The copy gets both; the harmonic tunings and sound banks the tuning names stay the game's.
                string audioFolder = Path.Combine(Path.Combine(this.Media, "audio"), "cars");
                string modelPath = Path.Combine(audioFolder, "CarModelTuning.zip");
                string tuningPath = Path.Combine(Path.Combine(audioFolder, "Engines"), "enginetuning.zip");
                ForzaZip modelZip = AddCopy(modelPath, this.SourceMediaName + "_CMT.xml", this.NewMediaName + "_CMT.xml", plan, "sound: car model tuning");
                // A turbo engine on the setup of a natural one (TurboVolume 0, as the Furai's) would whistle never: FM4's 137 turbo
                // engines all have TurboVolume 0.2 - 0.9 (median 0.5), the turbo sound's level in the tuning file.
                ForzaZip tuningZip = null;
                if (ownEngineMedia != null)
                {
                    // The sound engine's stock redline / idle against the copy's own: the audio range follows the ratio.
                    double redlineFactor = 1.0, idleFactor = 1.0;
                    object soundEngine = original.Scalar("SELECT EngineID FROM Data_Engine WHERE MediaName = ? COLLATE NOCASE", soundSource);
                    if (soundEngine != null && soundEngine != DBNull.Value)
                    {
                        SqliteDatabase.Result was = original.Query("SELECT RedlineRPM, StallRPM FROM List_UpgradeEngineCamshaft WHERE EngineID = ? AND IsStock = 1 ORDER BY Level LIMIT 1", soundEngine);
                        SqliteDatabase.Result now = db.Query("SELECT RedlineRPM, StallRPM FROM List_UpgradeEngineCamshaft WHERE EngineID = (SELECT EngineID FROM Data_Engine WHERE MediaName = ? COLLATE NOCASE) AND IsStock = 1 ORDER BY Level LIMIT 1", ownEngineMedia);
                        if (was.Rows.Count > 0 && now.Rows.Count > 0)
                        {
                            double wasRedline = Convert.ToDouble(was.Rows[0][0], CultureInfo.InvariantCulture), wasStall = Convert.ToDouble(was.Rows[0][1], CultureInfo.InvariantCulture);
                            double nowRedline = Convert.ToDouble(now.Rows[0][0], CultureInfo.InvariantCulture), nowStall = Convert.ToDouble(now.Rows[0][1], CultureInfo.InvariantCulture);
                            redlineFactor = (wasRedline > 0 && nowRedline > 0) ? nowRedline / wasRedline : 1.0;
                            idleFactor = (wasStall > 0 && nowStall > 0) ? nowStall / wasStall : 1.0;
                        }
                    }
                    bool turboNow = turboSound;
                    double rf = redlineFactor, inf = idleFactor;
                    EngineSpec engineSpec = this.Engine;
                    tuningZip = AddCopy(tuningPath, soundSource + "_ET.xml", ownEngineMedia + "_ET.xml", plan, "sound: engine tuning (the engine's setup is " + soundSource + "'s)",
                        delegate(byte[] data, List<string> lines) { return TuneEngineSound(data, lines, turboNow, rf, inf, engineSpec); });
                }

                if (apply)
                {
                    List<string> locked = new List<string>();
                    List<string> targets = new List<string>();
                    targets.Add(dbPath);
                    targets.Add(manifestPath);
                    targets.AddRange(files.Keys);
                    if (modelZip != null)
                    {
                        targets.Add(modelPath);
                    }
                    if (tuningZip != null)
                    {
                        targets.Add(tuningPath);
                    }
                    if (thumbnailZipPath != null)
                    {
                        targets.Add(thumbnailZipPath);
                    }
                    foreach (string path in targets)
                    {
                        try
                        {
                            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                            {
                            }
                        }
                        catch (IOException)
                        {
                            locked.Add(Path.GetFileName(path));
                        }
                    }
                    if (locked.Count > 0)
                    {
                        throw new IOException("Files in use (close the game or the emulator first): " + string.Join(", ", locked.ToArray()));
                    }

                    // New files first, the database last: an error before it leaves the game as it was.
                    newZip.Save(newZipPath);
                    foreach (KeyValuePair<string, ForzaZip> wheelZip in wheelZips)
                    {
                        wheelZip.Value.Save(wheelZip.Key);
                    }
                    List<string> changed = new List<string>();
                    foreach (KeyValuePair<string, StrFile> file in files)
                    {
                        this.Backup(file.Key);
                        ForzaZip archive = stringZips[file.Key];
                        archive.Replace(archive.Find("Data_Car.str"), file.Value.ToBytes());
                        archive.Save(file.Key);
                        changed.Add(file.Key);
                    }
                    if (thumbnailZip != null)
                    {
                        this.Backup(thumbnailZipPath);
                        thumbnailZip.Save(thumbnailZipPath);
                        changed.Add(thumbnailZipPath);
                    }
                    if (modelZip != null)
                    {
                        this.Backup(modelPath);
                        modelZip.Save(modelPath);
                        changed.Add(modelPath);
                    }
                    if (tuningZip != null)
                    {
                        this.Backup(tuningPath);
                        tuningZip.Save(tuningPath);
                        changed.Add(tuningPath);
                    }
                    this.Backup(dbPath);
                    string temp = dbPath + ".tmp";
                    File.WriteAllBytes(temp, db.ToBytes());
                    File.Delete(dbPath);
                    File.Move(temp, dbPath);
                    this.Backup(manifestPath);
                    foreach (string path in changed)
                    {
                        ZipManifest.Update(manifestPath, ZipManifest.GamePath(this.TargetRoot, path), path);
                    }
                    string like = "game:\\media\\cars\\" + this.SourceMediaName.ToLowerInvariant() + ".zip";
                    if (!ZipManifest.Add(manifestPath, ZipManifest.GamePath(this.TargetRoot, newZipPath), newZipPath, like))
                    {
                        throw new InvalidOperationException("zipmanifest.xml has no line like " + like + ".");
                    }
                    for (int i = 0; i < wheelZips.Count; i++)
                    {
                        string wheelLike = "game:\\media\\wheels\\" + wheelLikes[i].ToLowerInvariant() + ".zip";
                        if (!ZipManifest.Add(manifestPath, ZipManifest.GamePath(this.TargetRoot, wheelZips[i].Key), wheelZips[i].Key, wheelLike))
                        {
                            throw new InvalidOperationException("zipmanifest.xml has no line like " + wheelLike + ".");
                        }
                    }
                    plan.Add(string.Format(CultureInfo.InvariantCulture, "zipmanifest.xml: {0} lines updated, {1} added", changed.Count, 1 + wheelZips.Count));
                }
            }
            finally
            {
                if (writing)
                {
                    db.Rollback();
                }
                db.Dispose();
                original.Dispose();
                if (work != null)
                {
                    work.Dispose();
                }
            }
            foreach (string line in plan)
            {
                this.Report.AppendLine(line);
            }
            return plan;
        }

        // The zip with a copy of one of its files under another name (in memory; null and a plan line when it has no such file).
        private delegate byte[] EntryEdit(byte[] data, List<string> plan);

        // The engine tuning file of a copy's own engine, made for the engine it describes: the turbo level (see the call) and the
        // rpm range the sound is played over. audio_rpm_redline / audio_rpm_idle sit near the engine's redline and stall rpm in
        // FM4 (median 1.02 and 1.00 times; the Furai's own are 1.075 and 1.6): the copy keeps the sound engine's ratio to its
        // physics, so the sound runs up to the new redline and idles at the new idle.
        private static byte[] TuneEngineSound(byte[] data, List<string> plan, bool turbo, double redlineFactor, double idleFactor, EngineSpec spec)
        {
            string text = System.Text.Encoding.UTF8.GetString(data);
            if (turbo)
            {
                // The whistle's level (the user's, or 0.5 when the sound engine has none) and the blow-off sample of the three slots.
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(text, "TurboVolume=\"([^\"]*)\"");
                double now;
                if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out now) && (spec != null && spec.TurboVolume.HasValue || now <= 0))
                {
                    double volume = (spec != null && spec.TurboVolume.HasValue) ? spec.TurboVolume.Value : 0.5;
                    text = text.Substring(0, m.Index) + "TurboVolume=\"" + volume.ToString("0.000000", CultureInfo.InvariantCulture) + "\"" + text.Substring(m.Index + m.Length);
                    plan.Add(string.Format(CultureInfo.InvariantCulture, "sound: TurboVolume {0} -> {1} in the engine tuning (the engine is turbocharged; FM4's turbo engines use 0.2 - 0.9)", now, volume));
                }
                if (spec != null && spec.BovLevel > 0)
                {
                    for (int slot = 1; slot <= 3; slot++)
                    {
                        text = System.Text.RegularExpressions.Regex.Replace(text, "blowoffL" + slot + "Level=\"[^\"]*\"", "blowoffL" + slot + "Level=\"" + spec.BovLevel + "\"");
                        text = System.Text.RegularExpressions.Regex.Replace(text, "blowoffL" + slot + "Choice=\"[^\"]*\"", "blowoffL" + slot + "Choice=\"" + spec.BovChoice + "\"");
                    }
                    plan.Add("sound: blow-off L" + spec.BovLevel + "_BlowOff_" + spec.BovChoice + " in the three blow-off slots of the engine tuning");
                }
            }
            string[] names = { "audio_rpm_redline", "audio_rpm_idle" };
            double[] factors = { redlineFactor, idleFactor };
            for (int i = 0; i < names.Length; i++)
            {
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(text, names[i] + "=\"([^\"]*)\"");
                double was;
                if (factors[i] > 0 && Math.Abs(factors[i] - 1.0) > 0.001 && m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out was) && was > 0)
                {
                    double value = Math.Round(was * factors[i]);
                    text = text.Substring(0, m.Index) + names[i] + "=\"" + value.ToString(CultureInfo.InvariantCulture) + "\"" + text.Substring(m.Index + m.Length);
                    plan.Add(string.Format(CultureInfo.InvariantCulture, "sound: {0} {1:0} -> {2:0} in the engine tuning (follows the engine's {3})", names[i], was, value, i == 0 ? "redline" : "idle"));
                }
            }
            return System.Text.Encoding.UTF8.GetBytes(text);
        }

        private static ForzaZip AddCopy(string zipPath, string from, string to, List<string> plan, string what)
        {
            return AddCopy(zipPath, from, to, plan, what, null);
        }

        private static ForzaZip AddCopy(string zipPath, string from, string to, List<string> plan, string what, EntryEdit edit)
        {
            if (!File.Exists(zipPath))
            {
                plan.Add(what + ": " + Path.GetFileName(zipPath) + " not found, nothing added");
                return null;
            }
            ForzaZip zip = new ForzaZip(zipPath);
            ForzaZip.Entry entry = zip.Find(from);
            if (entry == null)
            {
                plan.Add(what + ": " + from + " is not in " + Path.GetFileName(zipPath) + ", nothing added");
                return null;
            }
            if (zip.Find(to) != null)
            {
                plan.Add(what + ": " + to + " is already in " + Path.GetFileName(zipPath));
                return null;
            }
            byte[] bytes = zip.Read(entry);
            zip.Add(to, (edit != null) ? edit(bytes, plan) : bytes);
            plan.Add(what + ": " + to + " added to " + Path.GetFileName(zipPath) + " (copy of " + from + ")");
            return zip;
        }

        // The rows of 'table' whose 'key' is oldId, inserted again with newId. Returns how many.
        private static int CopyRow(SqliteDatabase db, string table, string key, long oldId, long newId)
        {
            if (!DbIds.HasTable(db, table))
            {
                return 0;
            }
            SqliteDatabase.Result rows = db.Query("SELECT * FROM " + SqliteDatabase.Quote(table) + " WHERE " + SqliteDatabase.Quote(key) + " = ?", oldId);
            List<string> columns = new List<string>(rows.Columns);
            int keyAt = columns.FindIndex(delegate(string c) { return string.Equals(c, key, StringComparison.OrdinalIgnoreCase); });
            List<string> quoted = columns.ConvertAll<string>(delegate(string c) { return SqliteDatabase.Quote(c); });
            string sql = "INSERT INTO " + SqliteDatabase.Quote(table) + " (" + string.Join(",", quoted.ToArray()) + ") VALUES (" + string.Join(",", columns.ConvertAll<string>(delegate(string c) { return "?"; }).ToArray()) + ")";
            foreach (object[] row in rows.Rows)
            {
                row[keyAt] = newId;
                db.Execute(sql, row);
            }
            return rows.Rows.Count;
        }

        // A zip of the game copied with 'from' replaced by 'to' (case kept) in the entry names and the text files.
        private static ForzaZip RenamedZip(string path, string from, string to)
        {
            ForzaZip source = new ForzaZip(path);
            ForzaZip copy = ForzaZip.CreateLike(source);
            Regex name = new Regex(Regex.Escape(from), RegexOptions.IgnoreCase);
            string lower = to.ToLowerInvariant();
            MatchEvaluator keepCase = delegate(Match m) { return (m.Value == m.Value.ToLowerInvariant()) ? lower : to; };
            Encoding bytes = Encoding.GetEncoding(28591);
            foreach (ForzaZip.Entry entry in source.Entries)
            {
                if (entry.IsDirectory)
                {
                    continue;
                }
                byte[] data = source.Read(entry);
                string ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                if (ext == ".xml" || ext == ".txt" || ext == ".html")
                {
                    data = bytes.GetBytes(name.Replace(bytes.GetString(data), keepCase));
                }
                copy.Add(name.Replace(entry.Name, keepCase), data);
            }
            return copy;
        }

        // A row as text, to tell the rows a copy added from the ones it had.
        private static string RowText(object[] row)
        {
            StringBuilder text = new StringBuilder();
            foreach (object value in row)
            {
                if (value == null || value == DBNull.Value)
                {
                    text.Append("\u0001null|");
                }
                else if (value is byte[])
                {
                    text.Append(Convert.ToBase64String((byte[]) value)).Append('|');
                }
                else
                {
                    text.Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append('|');
                }
            }
            return text.ToString();
        }

        private void Backup(string path)
        {
            if (!File.Exists(path) || !BackupPolicy.Enabled)
            {
                return;
            }
            Directory.CreateDirectory(this.BackupFolder);
            string copy = Path.Combine(this.BackupFolder, this.stamp + "_bak_" + Path.GetFileName(path));
            for (int i = 2; File.Exists(copy); i++)
            {
                copy = Path.Combine(this.BackupFolder, this.stamp + "_bak" + i + "_" + Path.GetFileName(path));
            }
            File.Copy(path, copy);
        }
    }
}
