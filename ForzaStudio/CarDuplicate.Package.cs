namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    // Duplicate with a source car that is not in the game folder (ST-02, 2026-11-14): a car of an installed package, the game's
    // disc 2 (FM3 0013-0015, FM4 offers 1-4: rows in gamedb.slt, files in the package) or a DLC pack (rows in its merge too).
    // The package is only read. The copy is made as for a car of the game folder, on a staged slice of the game:
    //   1  stage     gamedb.slt = the game's + the package's merge (ModCarDb.Apply), the source's car zip, stock wheel / tyre zips
    //                taken out of the package (deflate for FM2), its thumbnail and sound files (FM2 EmitterData / DSPTuning, FM3/FM4
    //                tunings: every package file under media\audio or media\UI naming the car or its engine) added to staged copies
    //                of the game zips that hold those folders;
    //   2  copy      the usual Duplicate on the stage (CarDuplicate.Execute), its names from the package's string tables;
    //   3  clean     what came from the package is taken out again; the database = the game's + the copy's rows + the package rows
    //                the copy points at (a shared DLC engine and its curves, by the game's relation map) that the game lacks;
    //   4  write     the stage is what a copy of a game car leaves: a DLC pack is built from it (RunPack), or its files go into the
    //                game folder (backups, zipmanifest.xml lines).
    // A copy in the game folder gets the flags the game folder's cars have (FM2 IsDLC 0 / IsPurchased 1 / ContentId 0, FM3 / FM4
    // IsInstalled 1 / IsPurchased 1), the most common values among the cars whose zip is in Media\cars.
    public partial class CarDuplicate
    {
        public string SourcePackage;                // the package (STFS file or folder) the source car comes from; null = the game folder
        public bool SourceFromPackage;              // set on the staged copy: the source is a package car (FM2: StockGarage row for the copy)

        private static readonly string[] FlagColumns = { "IsDLC", "IsPurchased", "ContentId", "IsInstalled" };

        // The package of an installed car, opened for reading.
        public static DlcSource OpenSourcePackage(string package, ForzaGame game)
        {
            DlcSource source = Directory.Exists(package) ? DlcSource.Open(package) : DlcSource.OpenPackage(package, true);
            if (source == null)
            {
                throw new InvalidOperationException(package + " is not a package of the game.");
            }
            if (source.Problem != null)
            {
                throw new InvalidOperationException(Path.GetFileName(package) + ": " + source.Problem);
            }
            if (source.Game == ForzaGame.Unknown)
            {
                source.Game = game;
            }
            return source;
        }

        // The game database with the package's rows (its merge, when it has one: disc 2 packs have none, their rows are the game's).
        public static byte[] PackageReference(string gameRoot, DlcSource source)
        {
            byte[] game = File.ReadAllBytes(Path.Combine(Path.Combine(Path.Combine(gameRoot, "Media"), "db"), "gamedb.slt"));
            byte[] merge = source.Merge();
            return (merge == null) ? game : ModCarDb.Apply(game, new List<byte[]> { merge });
        }

        // The game folder (not a pack) as destination, the source in a package.
        private void RunFromPackageToGame()
        {
            this.stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
            string stage = Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "DuplicateFromPack_" + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture));
            try
            {
                this.BuildStageFromPackage(stage, true);
                this.CopyStageToGame(stage);
            }
            finally
            {
                TryDelete(stage);
            }
            if (this.RefreshNames)
            {
                DlcPackage.RefreshNamesPack(this.Game, this.TargetRoot, this.NamesContent(), this.Report);
            }
        }

        // Steps 1-3 into 'stage'; forGame: the copy goes into the game folder (its flags as the game folder's cars).
        private void BuildStageFromPackage(string stage, bool forGame)
        {
            ForzaGame game = this.Game;
            this.TargetRoot = Path.GetFullPath(this.TargetRoot).TrimEnd(Path.DirectorySeparatorChar);
            DlcSource source = OpenSourcePackage(this.SourcePackage, game);
            Dictionary<string, Func<byte[]>> packFiles = source.GameFiles();
            string media = this.Media;
            string stageMedia = Path.Combine(stage, "Media");
            this.Report.AppendLine("source: " + this.SourceMediaName + " from the package " + (string.IsNullOrEmpty(source.Title) ? Path.GetFileName(this.SourcePackage) : "\"" + source.Title + "\" (" + Path.GetFileName(this.SourcePackage) + ")") + "; the package is only read");

            // 1 stage: the game's files the copy changes, the database with the package's rows.
            List<string> files = new List<string>();
            string manifest = ZipManifest.PathFor(this.TargetRoot);
            if (File.Exists(manifest))
            {
                files.Add(manifest);
            }
            files.AddRange(Directory.GetFiles(Path.Combine(media, "StringTables"), "*.zip"));
            // As Stage(): the sound tunings the copy adds to (FM4 / FM3 car model and engine tunings, FM2 EmitterData / DSPTuning).
            string audio = Path.Combine(Path.Combine(media, "audio"), "cars");
            foreach (string tuning in new string[] { Path.Combine(audio, "CarModelTuning.zip"), Path.Combine(Path.Combine(audio, "Engines"), "enginetuning.zip"),
                Path.Combine(audio, "EmitterData.zip"), Path.Combine(Path.Combine(audio, "Engines"), "DSPTuning.zip") })
            {
                if (File.Exists(tuning))
                {
                    files.Add(ActualPath(tuning));
                }
            }
            foreach (string file in files)
            {
                this.StageCopy(file, stage);
            }
            byte[] reference = PackageReference(this.TargetRoot, source);
            string stageDb = Path.Combine(Path.Combine(stageMedia, "db"), "gamedb.slt");
            Directory.CreateDirectory(Path.GetDirectoryName(stageDb));
            File.WriteAllBytes(stageDb, reference);

            long sourceId;
            string wheel;
            string thumbnail;
            List<string> names = new List<string>();      // the file names that are the car's: its media name, its engines' media names
            using (SqliteDatabase db = SqliteDatabase.FromBytes(reference))
            {
                object id = db.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", this.SourceMediaName);
                if (id == null)
                {
                    throw new InvalidOperationException("Neither the game database nor the package's has a car " + this.SourceMediaName + ".");
                }
                sourceId = Convert.ToInt64(id, CultureInfo.InvariantCulture);
                wheel = db.Scalar("SELECT w.MediaName FROM List_Wheels w JOIN Data_Car c ON c.StockWheelID = w.ID WHERE c.Id = ?", sourceId) as string;
                thumbnail = db.Scalar("SELECT Thumbnail FROM Data_Car WHERE Id = ?", sourceId) as string;
                names.Add(this.SourceMediaName);
                if (DbIds.HasTable(db, "List_UpgradeEngine") && DbIds.HasTable(db, "Data_Engine"))
                {
                    foreach (object[] row in db.Query("SELECT DISTINCT e.MediaName FROM Data_Engine e JOIN List_UpgradeEngine u ON u.EngineID = e.EngineID WHERE u.Ordinal = ?", sourceId).Rows)
                    {
                        string engine = row[0] as string;
                        if (!string.IsNullOrEmpty(engine) && !names.Exists(delegate(string n) { return string.Equals(n, engine, StringComparison.OrdinalIgnoreCase); }))
                        {
                            names.Add(engine);
                        }
                    }
                }
                this.ResolveNames(source, packFiles, db, sourceId);
            }

            // The car and wheel zips out of the package, as the game spells them (FM2: lower case).
            bool lower = game == ForzaGame.Motorsport2;
            Directory.CreateDirectory(Path.Combine(stageMedia, "cars"));
            Directory.CreateDirectory(Path.Combine(stageMedia, "wheels"));
            string carZip = source.ExtractMedia("cars", this.SourceMediaName, Path.Combine(stageMedia, "cars"), lower ? this.SourceMediaName.ToLowerInvariant() : this.SourceMediaName);
            if (carZip == null)
            {
                throw new InvalidOperationException("The package has no media\\cars\\" + this.SourceMediaName + " folder.");
            }
            List<string> injected = new List<string>();            // staged files that came from the package: taken out again
            injected.Add(carZip);
            List<string> wheelNames = new List<string>();
            if (!string.IsNullOrEmpty(wheel))
            {
                wheelNames.Add(wheel);
            }
            wheelNames.Add("TireA" + this.SourceMediaName);
            wheelNames.Add("TireB" + this.SourceMediaName);
            wheelNames.Add("TireC" + this.SourceMediaName);
            foreach (string name in wheelNames)
            {
                string zip;
                string gameZip = Path.Combine(Path.Combine(media, "wheels"), name + ".zip");
                if (File.Exists(gameZip))
                {
                    zip = this.StageCopy(gameZip, stage);            // a wheel of the game (a DLC car on a base wheel)
                }
                else
                {
                    zip = source.ExtractMedia("wheels", name, Path.Combine(stageMedia, "wheels"), lower ? name.ToLowerInvariant() : name);
                }
                if (zip != null)
                {
                    injected.Add(zip);
                }
            }

            // The staged manifest lists the zips taken out of the package (Execute copies the source's lines for the copy's zips).
            string stagedManifest = File.Exists(manifest) ? Path.Combine(stage, manifest.Substring(this.TargetRoot.Length + 1)) : null;
            if (stagedManifest != null)
            {
                foreach (string zip in injected)
                {
                    string like = null;
                    foreach (string neighbour in Directory.GetFiles(Path.Combine(media, Path.GetFileName(Path.GetDirectoryName(zip))), "*.zip"))
                    {
                        string neighbourPath = ZipManifest.GamePath(this.TargetRoot, neighbour);
                        if (ZipManifest.Lists(manifest, neighbourPath))
                        {
                            like = neighbourPath;
                            break;
                        }
                    }
                    string gamePath = ZipManifest.GamePath(stage, zip);
                    if (like != null && !ZipManifest.Lists(stagedManifest, gamePath))
                    {
                        ZipManifest.Add(stagedManifest, gamePath, zip, like);
                    }
                }
            }

            // Thumbnail and sound files: into staged copies of the game zips that hold their folders.
            List<KeyValuePair<string, string>> entries = new List<KeyValuePair<string, string>>();   // staged zip -> entry added
            string thumbnailPath = (thumbnail != null && GamePaths.IsGamePath(thumbnail)) ? GamePaths.RelativePath(thumbnail).Replace('\\', '/') : null;
            if (thumbnailPath != null)
            {
                // the zip of the game that holds the thumbnails (a disc 2 car's is the game's own, a DLC car's is added below)
                string thumbnailEntry;
                string thumbnailZip = ZipHolding(this.TargetRoot, thumbnailPath, out thumbnailEntry);
                if (thumbnailZip != null)
                {
                    this.StageCopy(thumbnailZip, stage);
                }
            }
            foreach (KeyValuePair<string, Func<byte[]>> file in packFiles)
            {
                string path = file.Key.Replace('\\', '/');
                string lowerPath = path.ToLowerInvariant();
                bool isThumbnail = thumbnailPath != null && (string.Equals(path, thumbnailPath, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.ChangeExtension(path, null), Path.ChangeExtension(thumbnailPath, null), StringComparison.OrdinalIgnoreCase));
                bool isSound = lowerPath.StartsWith("media/audio/", StringComparison.Ordinal)
                    && names.Exists(delegate(string n) { return Path.GetFileName(lowerPath).IndexOf(n.ToLowerInvariant(), StringComparison.Ordinal) >= 0; });
                if (!isThumbnail && !isSound)
                {
                    continue;
                }
                string entryName;
                string gameZip = ZipHolding(this.TargetRoot, path, out entryName);
                if (gameZip == null)
                {
                    string loose = Path.Combine(stage, path.Replace('/', '\\'));
                    if (!File.Exists(Path.Combine(this.TargetRoot, path.Replace('/', '\\'))))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(loose));
                        File.WriteAllBytes(loose, file.Value());
                        // A loose sound file (FM3 / FM4 .fsb banks of the engine) stays: the copy plays it, also without the package.
                        if (!isSound)
                        {
                            injected.Add(loose);
                        }
                        else
                        {
                            this.Report.AppendLine("  sound: " + path + " from the package, kept for the copy (loose, as the game has its banks)");
                        }
                    }
                    continue;
                }
                string staged = this.StageCopy(gameZip, stage);
                ForzaZip zip = new ForzaZip(staged);
                if (zip.Find(entryName) != null)
                {
                    continue;   // the game has it already
                }
                zip.Add(entryName, file.Value());
                SaveZip(zip, staged);
                entries.Add(new KeyValuePair<string, string>(staged, entryName));
            }
            this.Report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  staged from the package: {0} zip(s) ({1}), {2} file(s) into the game's zips", injected.Count,
                string.Join(", ", injected.ConvertAll<string>(delegate(string f) { return Path.GetFileName(f); }).ToArray()), entries.Count));

            // 2 copy: the usual Duplicate on the stage.
            CarDuplicate inner = new CarDuplicate();
            inner.TargetRoot = stage;
            inner.RefreshNames = false;
            inner.GameOverride = game;
            inner.SourceFromPackage = true;
            inner.SourceMediaName = this.SourceMediaName;
            inner.NewMediaName = this.NewMediaName;
            inner.XeniaContent = this.XeniaContent ?? this.PackContent;
            inner.DisplayName = this.DisplayName;
            inner.ModelShort = this.ModelShort;
            inner.Year = this.Year;
            inner.MakeId = this.MakeId;
            inner.ChosenId = this.ChosenId;
            foreach (KeyValuePair<string, string> pair in this.DisplayNames)
            {
                inner.DisplayNames[pair.Key] = pair.Value;
            }
            foreach (KeyValuePair<string, string> pair in this.ModelShorts)
            {
                inner.ModelShorts[pair.Key] = pair.Value;
            }
            if (forGame)
            {
                foreach (KeyValuePair<string, object> flag in this.GameFolderFlags())
                {
                    inner.Data[flag.Key] = flag.Value;
                }
            }
            foreach (KeyValuePair<string, object> pair in this.Data)
            {
                inner.Data[pair.Key] = pair.Value;
            }
            inner.Engine = this.Engine;
            inner.Gearbox = this.Gearbox;
            inner.PerfMedia = this.PerfMedia ?? this.Media;   // the stage has no AI\PerfPrediction: the game's network
            if (inner.Engine == null && this.PackageOnlyEngine(reference, sourceId))
            {
                // A DLC car's engine is the package's (EngineID and its rows above the game's): shared, the copy would carry the DLC's
                // ids into the game (they clash with the pack). It gets an engine of its own with the same values instead.
                inner.Engine = new EngineSpec();
                this.Report.AppendLine("  engine: the source's is the package's own, the copy gets one of its own with the same values");
            }
            if (inner.Gearbox == null)
            {
                GearboxSpec gearbox = this.PackageOnlyGearbox(reference, sourceId);
                if (gearbox != null)
                {
                    inner.Gearbox = gearbox;   // as the engine: the package's drivetrain rows would carry the DLC's ids
                    this.Report.AppendLine("  gearbox: the source's drivetrain is the package's own, the copy gets one of its own with the same ratios (" + CarGearbox.Describe(gearbox) + ")");
                }
            }
            inner.BackupFolder = Path.Combine(stage, "bak");
            inner.Run();
            this.NewId = inner.NewId;
            this.Report.Append(inner.Report.ToString());
            TryDelete(Path.Combine(stage, "bak"));

            // 3 clean: the package's files out again, the database = the game's + the copy's + the package rows it needs.
            foreach (string file in injected)
            {
                File.Delete(file);
            }
            Dictionary<string, List<string>> byZip = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> entry in entries)
            {
                List<string> list;
                if (!byZip.TryGetValue(entry.Key, out list))
                {
                    byZip[entry.Key] = list = new List<string>();
                }
                list.Add(entry.Value);
            }
            foreach (KeyValuePair<string, List<string>> zipEntries in byZip)
            {
                ForzaZip zip = new ForzaZip(zipEntries.Key);
                foreach (string name in zipEntries.Value)
                {
                    ForzaZip.Entry entry = zip.Find(name);
                    if (entry != null)
                    {
                        zip.Remove(entry);
                    }
                }
                SaveZip(zip, zipEntries.Key);
            }
            byte[] clean = this.CleanDatabase(File.ReadAllBytes(Path.Combine(Path.Combine(media, "db"), "gamedb.slt")), reference, File.ReadAllBytes(stageDb), game, sourceId);
            File.WriteAllBytes(stageDb, clean);
        }

        // The source's names (EN and per language) from the package's string tables when the caller gave none: the game's own tables
        // lack a DLC car's keys.
        private void ResolveNames(DlcSource source, Dictionary<string, Func<byte[]>> packFiles, SqliteDatabase db, long sourceId)
        {
            string[][] columns = { new string[] { "DisplayName" }, new string[] { "ModelShort" } };
            foreach (string[] column in columns)
            {
                bool isDisplay = column[0] == "DisplayName";
                if (!string.IsNullOrEmpty(isDisplay ? this.DisplayName : this.ModelShort))
                {
                    continue;
                }
                string value = db.Scalar("SELECT " + column[0] + " FROM Data_Car WHERE Id = ?", sourceId) as string;
                uint stringId;
                if (value == null || !value.StartsWith("_&", StringComparison.Ordinal) || !uint.TryParse(value.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out stringId))
                {
                    continue;
                }
                int key = (int) (stringId & 0xFFFF);
                Dictionary<string, string> perLanguage = isDisplay ? this.DisplayNames : this.ModelShorts;
                foreach (KeyValuePair<string, Func<byte[]>> file in packFiles)
                {
                    string[] part = file.Key.Replace('\\', '/').Split('/');
                    if (part.Length != 4 || !part[1].Equals("StringTables", StringComparison.OrdinalIgnoreCase) || !part[3].Equals("Data_Car.str", StringComparison.OrdinalIgnoreCase)
                        || perLanguage.ContainsKey(part[2]))
                    {
                        continue;
                    }
                    try
                    {
                        foreach (StrFile.Entry entry in new StrFile(file.Value()).Entries)
                        {
                            if (entry.Key == key && !string.IsNullOrEmpty(entry.Text))
                            {
                                perLanguage[part[2]] = entry.Text;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // a table that can't be read gives no name; the copy's falls back to the EN one or none
                    }
                }
                string en;
                if (perLanguage.TryGetValue("EN", out en))
                {
                    if (isDisplay)
                    {
                        this.DisplayName = en;
                    }
                    else
                    {
                        this.ModelShort = en;
                    }
                }
            }
        }

        // True when one of the car's engines is not in the game's own database (it comes with the package).
        private bool PackageOnlyEngine(byte[] reference, long sourceId)
        {
            using (SqliteDatabase game = SqliteDatabase.FromBytes(File.ReadAllBytes(Path.Combine(Path.Combine(this.Media, "db"), "gamedb.slt"))))
            using (SqliteDatabase db = SqliteDatabase.FromBytes(reference))
            {
                if (!DbIds.HasTable(db, "List_UpgradeEngine") || !DbIds.HasTable(game, "Data_Engine"))
                {
                    return false;
                }
                foreach (object[] row in db.Query("SELECT DISTINCT EngineID FROM List_UpgradeEngine WHERE Ordinal = ?", sourceId).Rows)
                {
                    if (game.Scalar("SELECT EngineID FROM Data_Engine WHERE EngineID = ?", row[0]) == null)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // The source's stock gearbox when its drivetrain is not in the game's own database (FM3 / FM4 Data_Drivetrain), else null.
        private GearboxSpec PackageOnlyGearbox(byte[] reference, long sourceId)
        {
            using (SqliteDatabase game = SqliteDatabase.FromBytes(File.ReadAllBytes(Path.Combine(Path.Combine(this.Media, "db"), "gamedb.slt"))))
            using (SqliteDatabase db = SqliteDatabase.FromBytes(reference))
            {
                if (!DbIds.HasTable(db, "Data_Drivetrain") || !DbIds.HasTable(db, "List_UpgradeDrivetrain"))
                {
                    return null;
                }
                long drivetrain = CarGearbox.StockDrivetrain(db, sourceId);
                if (drivetrain < 0 || game.Scalar("SELECT DrivetrainID FROM Data_Drivetrain WHERE DrivetrainID = ?", drivetrain) != null)
                {
                    return null;
                }
                return CarGearbox.Read(db, drivetrain);
            }
        }

        // The flags of the game folder's cars (those whose zip is in Media\cars): the most common value of each flag column.
        private Dictionary<string, object> GameFolderFlags()
        {
            Dictionary<string, object> flags = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            string cars = Path.Combine(this.Media, "cars");
            HashSet<string> zips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string zip in Directory.GetFiles(cars, "*.zip"))
            {
                zips.Add(Path.GetFileNameWithoutExtension(zip));
            }
            using (SqliteDatabase db = SqliteDatabase.FromBytes(File.ReadAllBytes(Path.Combine(Path.Combine(this.Media, "db"), "gamedb.slt"))))
            {
                List<string> columns = DbIds.Schema(db)["Data_Car"];
                foreach (string flag in FlagColumns)
                {
                    if (!columns.Exists(delegate(string c) { return string.Equals(c, flag, StringComparison.OrdinalIgnoreCase); }))
                    {
                        continue;
                    }
                    Dictionary<string, int> counts = new Dictionary<string, int>();
                    Dictionary<string, object> values = new Dictionary<string, object>();
                    foreach (object[] row in db.Query("SELECT MediaName, " + SqliteDatabase.Quote(flag) + " FROM Data_Car").Rows)
                    {
                        if (!zips.Contains(Convert.ToString(row[0], CultureInfo.InvariantCulture)))
                        {
                            continue;
                        }
                        string text = Convert.ToString(row[1], CultureInfo.InvariantCulture);
                        int count;
                        counts.TryGetValue(text, out count);
                        counts[text] = count + 1;
                        values[text] = row[1];
                    }
                    string best = null;
                    foreach (KeyValuePair<string, int> pair in counts)
                    {
                        if (best == null || pair.Value > counts[best])
                        {
                            best = pair.Key;
                        }
                    }
                    if (best != null)
                    {
                        flags[flag] = values[best];
                    }
                }
            }
            return flags;
        }

        // The game's database + the rows the copy added on the stage + the package rows they point at that the game lacks (and those
        // rows' own children that belong to no other car: a shared DLC engine's torque curves). Checked by DbIntegrity.
        private byte[] CleanDatabase(byte[] gameDb, byte[] reference, byte[] staged, ForzaGame game, long sourceId)
        {
            using (SqliteDatabase target = SqliteDatabase.FromBytes(gameDb))
            using (SqliteDatabase before = SqliteDatabase.FromBytes(reference))
            using (SqliteDatabase after = SqliteDatabase.FromBytes(staged))
            using (SqliteDatabase original = SqliteDatabase.FromBytes(gameDb))
            {
                Dictionary<string, List<string>> schema = DbIds.Schema(after);
                List<string[]> carColumns = DbIds.CarColumns(schema, game);
                List<KeyValuePair<string, object[]>> queue = new List<KeyValuePair<string, object[]>>();
                int copied = 0;
                target.Begin();
                foreach (KeyValuePair<string, List<string>> table in schema)
                {
                    if (!DbIds.HasTable(target, table.Key))
                    {
                        continue;
                    }
                    HashSet<string> had = new HashSet<string>();
                    if (DbIds.HasTable(before, table.Key))
                    {
                        foreach (object[] row in before.Query("SELECT * FROM " + SqliteDatabase.Quote(table.Key)).Rows)
                        {
                            had.Add(RowText(row));
                        }
                    }
                    HashSet<string> inGame = new HashSet<string>();
                    foreach (object[] row in target.Query("SELECT * FROM " + SqliteDatabase.Quote(table.Key)).Rows)
                    {
                        inGame.Add(RowText(row));
                    }
                    foreach (object[] row in after.Query("SELECT * FROM " + SqliteDatabase.Quote(table.Key)).Rows)
                    {
                        string text = RowText(row);
                        if (had.Contains(text) || inGame.Contains(text))
                        {
                            continue;
                        }
                        Insert(target, table.Key, table.Value, row);
                        inGame.Add(text);
                        queue.Add(new KeyValuePair<string, object[]>(table.Key, row));
                        copied++;
                    }
                }
                // The package rows the copy points at keep the package's keys (identical rows: with the pack installed its merge
                // replaces them by the same values); the id rule (keys above the game's and every DLC's) is not theirs.
                HashSet<string> packKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int needed = 0;
                List<string> neededTables = new List<string>();
                List<GameDbSchema.Relation> relations = GameDbSchema.For(game);
                for (int i = 0; i < queue.Count; i++)
                {
                    string table = queue[i].Key;
                    object[] row = queue[i].Value;
                    List<string> columns = schema[table];
                    foreach (GameDbSchema.Relation relation in relations)
                    {
                        if (relation.IsComposite || !string.Equals(relation.Table, table, StringComparison.OrdinalIgnoreCase) || !DbIds.HasTable(before, relation.Target) || !DbIds.HasTable(target, relation.Target)
                            || IsCarTable(relation.Target))
                        {
                            continue;   // never another car of the package
                        }
                        int index = columns.FindIndex(delegate(string c) { return string.Equals(c, relation.Column, StringComparison.OrdinalIgnoreCase); });
                        if (index < 0 || !(row[index] is long) || GameDbSchema.IsNone((long) row[index]))
                        {
                            continue;
                        }
                        string where = " WHERE " + SqliteDatabase.Quote(relation.TargetColumn) + " = ?";
                        if (Convert.ToInt64(target.Scalar("SELECT COUNT(*) FROM " + SqliteDatabase.Quote(relation.Target) + where, row[index]), CultureInfo.InvariantCulture) > 0)
                        {
                            continue;
                        }
                        foreach (object[] packRow in before.Query("SELECT * FROM " + SqliteDatabase.Quote(relation.Target) + where, row[index]).Rows)
                        {
                            Insert(target, relation.Target, schema[relation.Target], packRow);
                            PackKeys(packKeys, target, relation.Target, schema[relation.Target], packRow, game);
                            queue.Add(new KeyValuePair<string, object[]>(relation.Target, packRow));
                            needed++;
                            if (!neededTables.Contains(relation.Target))
                            {
                                neededTables.Add(relation.Target);
                            }
                            // its children that belong to no car (or to the copy): an engine's curves, not another car's upgrade rows
                            foreach (GameDbSchema.Relation into in relations)
                            {
                                if (into.IsComposite || !string.Equals(into.Target, relation.Target, StringComparison.OrdinalIgnoreCase) || !DbIds.HasTable(before, into.Table)
                                    || !DbIds.HasTable(target, into.Table) || PointsAtCarOnly(into.Table))
                                {
                                    continue;
                                }
                                int keyIndex = schema[relation.Target].FindIndex(delegate(string c) { return string.Equals(c, into.TargetColumn, StringComparison.OrdinalIgnoreCase); });
                                if (keyIndex < 0 || !(packRow[keyIndex] is long))
                                {
                                    continue;
                                }
                                List<string> childColumns = schema[into.Table];
                                List<int> carIndexes = new List<int>();
                                foreach (string[] car in carColumns)
                                {
                                    if (string.Equals(car[0], into.Table, StringComparison.OrdinalIgnoreCase))
                                    {
                                        carIndexes.Add(childColumns.FindIndex(delegate(string c) { return string.Equals(c, car[1], StringComparison.OrdinalIgnoreCase); }));
                                    }
                                }
                                HashSet<string> childInGame = new HashSet<string>();
                                foreach (object[] r in target.Query("SELECT * FROM " + SqliteDatabase.Quote(into.Table) + " WHERE " + SqliteDatabase.Quote(into.Column) + " = ?", packRow[keyIndex]).Rows)
                                {
                                    childInGame.Add(RowText(r));
                                }
                                foreach (object[] child in before.Query("SELECT * FROM " + SqliteDatabase.Quote(into.Table) + " WHERE " + SqliteDatabase.Quote(into.Column) + " = ?", packRow[keyIndex]).Rows)
                                {
                                    bool otherCar = false;
                                    foreach (int carIndex in carIndexes)
                                    {
                                        otherCar = otherCar || (carIndex >= 0 && child[carIndex] is long && !GameDbSchema.IsNone((long) child[carIndex]) && (long) child[carIndex] != this.NewId);
                                    }
                                    if (otherCar || childInGame.Contains(RowText(child)) || IsCarTable(into.Table))
                                    {
                                        continue;
                                    }
                                    Insert(target, into.Table, childColumns, child);
                                    PackKeys(packKeys, target, into.Table, childColumns, child, game);
                                    childInGame.Add(RowText(child));
                                    queue.Add(new KeyValuePair<string, object[]>(into.Table, child));
                                    needed++;
                                    if (!neededTables.Contains(into.Table))
                                    {
                                        neededTables.Add(into.Table);
                                    }
                                }
                            }
                        }
                    }
                }
                if (game == ForzaGame.Motorsport2 && Fm2StockGarage.Has(target))
                {
                    target.Execute("DELETE FROM " + SqliteDatabase.Quote(Fm2StockGarage.Table) + " WHERE Id = ?", this.NewId);
                    if (Fm2StockGarage.HasRow(after, this.NewId))
                    {
                        Fm2StockGarage.Write(target, this.NewId);
                    }
                }
                DbIntegrity.Result check = DbIntegrity.Compare(original, target, game, null);
                List<string> errors = DbIds.CheckCar(target, this.NewId, sourceId, this.NewMediaName, game);
                foreach (string error in check.Errors)
                {
                    bool packRow = false;
                    foreach (string key in packKeys)
                    {
                        packRow = packRow || (error.StartsWith(key, StringComparison.OrdinalIgnoreCase) && error.IndexOf("is not above", key.Length, StringComparison.Ordinal) == key.Length);
                    }
                    if (packRow)
                    {
                        this.Report.AppendLine("  " + error.Substring(0, error.IndexOf(" is not above", StringComparison.Ordinal)) + ": the package's own row, kept with the package's key (identical to it)");
                    }
                    else
                    {
                        errors.Add(error);
                    }
                }
                if (errors.Count > 0)
                {
                    target.Rollback();
                    throw new InvalidOperationException("Database check failed (the copy of a package car), nothing was written:\r\n  " + string.Join("\r\n  ", errors.ToArray()));
                }
                target.Commit();
                this.Report.AppendLine(string.Format(CultureInfo.InvariantCulture, "package source: the game's database + the copy's {0} rows + {1} row(s) of the package the copy points at{2}", copied, needed,
                    (neededTables.Count > 0) ? " (" + string.Join(", ", neededTables.ToArray()) + ")" : string.Empty));
                return target.ToBytes();
            }
        }

        private static bool IsCarTable(string table)
        {
            return string.Equals(table, "Data_Car", StringComparison.OrdinalIgnoreCase) || string.Equals(table, "Data_CarBody", StringComparison.OrdinalIgnoreCase);
        }

        // "Table.Key = value " of a package row copied as is (the start of DbIntegrity's id-rule error for it).
        private static void PackKeys(HashSet<string> keys, SqliteDatabase db, string table, List<string> columns, object[] row, ForzaGame game)
        {
            string key = DbIds.KeyColumn(db, table, columns, game);
            int index = (key == null) ? -1 : columns.FindIndex(delegate(string c) { return string.Equals(c, key, StringComparison.OrdinalIgnoreCase); });
            if (index >= 0)
            {
                keys.Add(table + "." + key + " = " + Convert.ToString(row[index], CultureInfo.InvariantCulture) + " ");
            }
        }

        private static void Insert(SqliteDatabase db, string table, List<string> columns, object[] row)
        {
            List<string> quoted = columns.ConvertAll<string>(delegate(string c) { return SqliteDatabase.Quote(c); });
            db.Execute("INSERT INTO " + SqliteDatabase.Quote(table) + " (" + string.Join(",", quoted.ToArray()) + ") VALUES (" + string.Join(",", columns.ConvertAll<string>(delegate(string c) { return "?"; }).ToArray()) + ")", row);
        }

        // Step 4 for the game folder: every staged file that differs from the game's goes in (backed up first); zips listed in
        // zipmanifest.xml get their line updated, new zips a line like a neighbour's.
        private void CopyStageToGame(string stage)
        {
            string root = Path.GetFullPath(stage).TrimEnd('\\') + "\\";
            string manifest = ZipManifest.PathFor(this.TargetRoot);
            List<string> written = new List<string>();
            foreach (string file in Directory.GetFiles(stage, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetFullPath(file).Substring(root.Length);
                string target = Path.Combine(this.TargetRoot, relative);
                if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(manifest), StringComparison.OrdinalIgnoreCase))
                {
                    continue;   // the game's manifest is updated line by line below
                }
                if (File.Exists(target))
                {
                    target = ActualPath(target);
                    if (SameBytes(file, target))
                    {
                        continue;
                    }
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                this.Backup(target);
                File.Copy(file, target, true);
                written.Add(target);
            }
            foreach (string file in written)
            {
                this.Report.AppendLine("saved " + file.Substring(this.TargetRoot.Length + 1));
                if (!file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || !File.Exists(manifest))
                {
                    continue;
                }
                string gamePath = ZipManifest.GamePath(this.TargetRoot, file);
                if (ZipManifest.Lists(manifest, gamePath))
                {
                    ZipManifest.Update(manifest, gamePath, file);
                    continue;
                }
                string like = null;
                foreach (string neighbour in Directory.GetFiles(Path.GetDirectoryName(file), "*.zip"))
                {
                    string neighbourPath = ZipManifest.GamePath(this.TargetRoot, neighbour);
                    if (!written.Contains(neighbour) && ZipManifest.Lists(manifest, neighbourPath))
                    {
                        like = neighbourPath;
                        break;
                    }
                }
                if (like == null || !ZipManifest.Add(manifest, gamePath, file, like))
                {
                    this.Report.AppendLine("  warning: zipmanifest.xml has no line like " + gamePath);
                }
            }
        }

        private string StageCopy(string gameFile, string stage)
        {
            string root = this.TargetRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string copy = Path.Combine(stage, gameFile.Substring(root.Length));
            if (!File.Exists(copy))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(copy));
                File.Copy(gameFile, copy, true);
            }
            return copy;
        }

        // The game zip that holds a path ("media/audio/cars/EmitterData/x.xml" -> Media\audio\cars\EmitterData.zip, entry "x.xml"):
        // the deepest folder of the path that is a zip of the game, as the game mounts them; null when none is.
        private static string ZipHolding(string root, string path, out string entryName)
        {
            string[] parts = path.Replace('\\', '/').Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = parts.Length - 1; i >= 1; i--)
            {
                string zip = Path.Combine(root, string.Join("\\", parts, 0, i)) + ".zip";
                if (File.Exists(zip))
                {
                    entryName = string.Join("/", parts, i, parts.Length - i);
                    ForzaZip found = new ForzaZip(zip);
                    // the game's spelling of the entry's folders when it has them
                    string folder = Path.GetDirectoryName(entryName.Replace('/', '\\'));
                    if (!string.IsNullOrEmpty(folder))
                    {
                        string prefix = folder.Replace('\\', '/') + "/";
                        foreach (ForzaZip.Entry entry in found.Entries)
                        {
                            if (entry.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            {
                                entryName = entry.Name.Substring(0, prefix.Length) + entryName.Substring(prefix.Length);
                                break;
                            }
                        }
                    }
                    return ActualPath(zip);
                }
            }
            entryName = null;
            return null;
        }

        private static void SaveZip(ForzaZip zip, string path)
        {
            string temp = path + ".new";
            zip.Save(temp);
            File.Delete(path);
            File.Move(temp, path);
        }

        private static bool SameBytes(string a, string b)
        {
            FileInfo x = new FileInfo(a);
            FileInfo y = new FileInfo(b);
            if (x.Length != y.Length)
            {
                return false;
            }
            byte[] p = File.ReadAllBytes(a);
            byte[] q = File.ReadAllBytes(b);
            for (int i = 0; i < p.Length; i++)
            {
                if (p[i] != q[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static void TryDelete(string folder)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
