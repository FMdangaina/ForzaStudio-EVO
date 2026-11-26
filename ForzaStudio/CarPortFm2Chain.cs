namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    // A car of FM4 / FM3 / FH1 / FH2 installed in an FM2 game folder, the whole chain the port window runs
    // (docs/PORT_TO_FM2.md): Duplicate Car of the FM2 template under the car's name, CarPortToFm2 (model, wheel,
    // textures, masks), CarDataToFm2 with FM2's own performance numbers (database), Fm2SoundPort (engine sound).
    // No window here: the caller asks about Xenia first and shows Log.
    internal sealed class CarPortFm2Chain
    {
        public string SourceRoot;           // the source game folder, or a DLC package staged by StageDlc
        public string SourceBaseRoot;       // a DLC package: its game folder (sound files the pack lacks); else null
        public string SourceMediaName;
        public string TargetRoot;           // the FM2 folder
        public string TemplateMediaName;    // the FM2 car the copy is made from
        public string NewMediaName;         // the car's name in FM2 (the source's)
        public string DisplayName;
        public string ModelShort;
        public long Year;
        public List<string> SkipParts = new List<string>();     // upgrade variants the user left out
        public Dictionary<string, string> CarMaterials = new Dictionary<string, string>(StringComparer.Ordinal);     // the user's choices: source material -> FM2 material
        public Dictionary<string, string> WheelMaterials = new Dictionary<string, string>(StringComparer.Ordinal);
        public string UseSoundOf;           // "Use sound": an FM2 car whose engine sound the car gets; null: "Port sound" (the source's)
        public long MakeId = -1;            // the Make chosen in the window (FM2 List_CarMake.ID); -1: the source's (added when FM2 lacks it)
        public List<CarDataToFm2.Fm2Swap> Swaps;      // the Upgrades tab's engine swaps / drive conversion; null: the source's swaps
        public bool Again;                  // port again over the installed FM2 car NewMediaName (ported before): no new copy, its
                                            // car id, upgrade ids and engine id stay; TemplateMediaName = the template of the first port
        public string SourceGameName = "";  // for the record (port_log.txt in the car zip)
        public string SourcePack = "";
        public List<string> Log = new List<string>();
        public long CarId;
        public bool SoundDone;
        public Action<string> Progress;

        private void Say(string text)
        {
            if (this.Progress != null)
            {
                this.Progress(text);
            }
        }

        public static string FindDatabase(string root)
        {
            string usual = Path.Combine(Path.Combine(Path.Combine(root, "Media"), "db"), "gamedb.slt");
            if (File.Exists(usual))
            {
                return usual;
            }
            string media = Path.Combine(root, "Media");
            string[] found = Directory.Exists(media) ? Directory.GetFiles(media, "gamedb.slt", SearchOption.AllDirectories) : new string[0];
            if (found.Length == 0)
            {
                throw new FileNotFoundException("No gamedb.slt under " + media + ".");
            }
            return found[0];
        }

        // The upgrade variants do not fit: true when the parts kept overflow one of FM2's two shared buffers.
        public static bool Fits(List<Fm2CarbinModel.PlanPart> plan, ICollection<string> skip, out int seeThrough, out int opaque)
        {
            seeThrough = 0;
            opaque = 0;
            foreach (Fm2CarbinModel.PlanPart part in plan)
            {
                if (skip != null && skip.Contains(part.Name))
                {
                    continue;
                }
                if (part.SeeThrough)
                {
                    seeThrough += part.Vertices;
                }
                else
                {
                    opaque += part.Vertices;
                }
            }
            return seeThrough <= 65536 && opaque <= 65536;
        }

        // A DLC package of the source game as a port source: its stage copied to the program's temp folder, the database
        // there = the game's with the pack's merge laid over it (SourceDatabaseMerge), the car and its stock wheel taken
        // out of the package as zips. The copy is made once per package and reused. Returns the folder.
        public static string StageDlc(DlcSource pack, string stage, string baseRoot, string car, List<string> log)
        {
            string name = Path.GetFileName(stage.TrimEnd('\\'));
            string root = Path.Combine(Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "Fm2PortSource"), name);
            string marker = Path.Combine(root, "fm2port.merged");
            if (!File.Exists(marker))
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
                CopyFolder(stage, root);
                string packDb = FindDatabase(root);
                List<string> lines = new List<string>();
                using (SqliteDatabase merged = SourceDatabaseMerge.Merge(File.ReadAllBytes(FindDatabase(baseRoot)), File.ReadAllBytes(packDb), lines))
                {
                    File.WriteAllBytes(packDb, merged.ToBytes());
                }
                if (log != null)
                {
                    log.Add("DLC package: its database laid over the game's (" + lines.Count + " tables)");
                }
                File.WriteAllText(marker, baseRoot);
            }
            string wheel;
            using (SqliteDatabase db = SqliteDatabase.FromBytes(File.ReadAllBytes(FindDatabase(root))))
            {
                wheel = Convert.ToString(db.Scalar("SELECT w.MediaName FROM Data_Car c JOIN List_Wheels w ON w.ID = c.StockWheelID WHERE c.MediaName = ? COLLATE NOCASE", car), CultureInfo.InvariantCulture);
            }
            string cars = Path.Combine(Path.Combine(root, "Media"), "cars");
            string wheels = Path.Combine(Path.Combine(root, "Media"), "wheels");
            if (GamePaths.FindCar(root, car) == null && pack.ExtractMedia("cars", car, cars, car) == null)
            {
                throw new InvalidOperationException("The package does not hold the files of " + car + ".");
            }
            if (!string.IsNullOrEmpty(wheel) && GamePaths.FindMedia(root, "wheels", wheel) == null)
            {
                pack.ExtractMedia("wheels", wheel, wheels, wheel);     // null: the game's own wheel, read from the game folder by nobody; the FM2 wheel then stays
            }
            return root;
        }

        private static void CopyFolder(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from))
            {
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
            }
            foreach (string folder in Directory.GetDirectories(from))
            {
                CopyFolder(folder, Path.Combine(to, Path.GetFileName(folder)));
            }
        }

        // Throws when a step fails; Log says how far it went. The sound step alone does not stop the install: SoundDone.
        public void Run()
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string backup = BackupPolicy.Enabled ? Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bak"), "fm2port_" + stamp) : null;
            string dbPath = FindDatabase(this.TargetRoot);

            // before anything is written: the parts must fit
            this.Say("Checking the model...");
            CarPortToFm2 check = new CarPortToFm2();
            check.SourceRoot = this.SourceRoot;
            check.SourceMediaName = this.SourceMediaName;
            check.TargetRoot = this.TargetRoot;
            check.TemplateMediaName = this.TemplateMediaName;
            int seeThrough, opaque;
            check.CarMaterials = this.CarMaterials;
            if (!Fits(check.Plan(), new HashSet<string>(this.SkipParts, StringComparer.OrdinalIgnoreCase), out seeThrough, out opaque))
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                    "The car does not fit FM2's 65536 vertices per buffer ({0} see-through, {1} opaque): leave more variants out.", seeThrough, opaque));
            }
            // the car's make: FM2 must have it or be able to take it (Fm2LookupPort), before anything is written
            using (SqliteDatabase source = SqliteDatabase.FromBytes(File.ReadAllBytes(FindDatabase(this.SourceRoot))))
            using (SqliteDatabase target = SqliteDatabase.FromBytes(File.ReadAllBytes(dbPath)))
            {
                string makeProblem = (this.MakeId > 0) ? null : Fm2LookupPort.MakeProblem(source, target, Fm2LookupPort.SourceMake(source, this.SourceMediaName));
                if (makeProblem != null)
                {
                    throw new InvalidOperationException("The car's make cannot go into FM2: " + makeProblem + ". Nothing was written.");
                }
            }

            // 1. the copy of the template (a port again keeps the installed car: its rows are converted again in step 3)
            if (this.Again)
            {
                using (SqliteDatabase target = SqliteDatabase.FromBytes(File.ReadAllBytes(dbPath)))
                {
                    object id = target.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", this.NewMediaName);
                    if (id == null || GamePaths.FindCar(this.TargetRoot, this.NewMediaName) == null)
                    {
                        throw new InvalidOperationException("Port again: FM2 has no installed car " + this.NewMediaName + " (row and zip). Nothing was written.");
                    }
                    if (Convert.ToInt64(id, CultureInfo.InvariantCulture) <= GameIdLimits.MaxCarBase(ForzaGame.Motorsport2))   // MaxCar (1024) counts the official DLC ids, which ports reuse (659-1000)
                    {
                        throw new InvalidOperationException("Port again: " + this.NewMediaName + " is one of FM2's own cars, never written over. Nothing was written.");
                    }
                    this.Log.Add("== step 1 of 4 skipped: porting again over " + this.NewMediaName + ", car " + id + " of FM2 (car, upgrade and engine ids kept)");
                }
            }
            else
            {
                this.Say("Copying the FM2 template car...");
                CarDuplicate copy = new CarDuplicate();
                copy.TargetRoot = this.TargetRoot;
                copy.SourceMediaName = this.TemplateMediaName;
                copy.NewMediaName = this.NewMediaName;
                copy.XeniaContent = InstalledDlc.ContentFolder(ForzaGame.Motorsport2);
                copy.DisplayName = this.DisplayName;
                copy.ModelShort = string.IsNullOrEmpty(this.ModelShort) ? this.DisplayName : this.ModelShort;
                copy.Year = this.Year;
                try
                {
                    copy.Run();
                }
                finally
                {
                    this.Log.AddRange(copy.Report.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries));
                }
                this.Log.Add("== step 1 of 4 done: " + this.NewMediaName + " is car " + copy.NewId + " of FM2");
            }

            // 2. model, wheel, textures, livery masks
            this.Say("Porting the model and the textures...");
            CarPortToFm2 port = new CarPortToFm2();
            port.SourceRoot = this.SourceRoot;
            port.SourceMediaName = this.SourceMediaName;
            port.TargetRoot = this.TargetRoot;
            port.TargetMediaName = this.NewMediaName;
            // a fresh copy: its own model is the template's, its zips are new files; a port again: the first port's template,
            // the car's zips backed up before they are written over
            port.TemplateMediaName = this.Again ? this.TemplateMediaName : null;
            port.BackupFolder = (this.Again && backup != null) ? Path.Combine(backup, "model") : null;
            port.SkipParts.AddRange(this.SkipParts);
            port.CarMaterials = this.CarMaterials;
            port.WheelMaterials = this.WheelMaterials;
            try
            {
                port.Run();
            }
            finally
            {
                this.Log.AddRange(port.Log);
            }
            this.Log.Add("== step 2 of 4 done: model, wheel, textures, livery masks");

            // 3. the database, then FM2's own performance numbers
            this.Say("Converting the database rows...");
            byte[] bytes = File.ReadAllBytes(dbPath);
            using (SqliteDatabase original = SqliteDatabase.FromBytes(bytes))
            using (SqliteDatabase db = SqliteDatabase.FromBytes(bytes))
            using (SqliteDatabase source = SqliteDatabase.FromBytes(File.ReadAllBytes(FindDatabase(this.SourceRoot))))
            {
                long car = Convert.ToInt64(db.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", this.NewMediaName), CultureInfo.InvariantCulture);
                long template = Convert.ToInt64(db.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", this.TemplateMediaName), CultureInfo.InvariantCulture);
                long sourceCar = Convert.ToInt64(source.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", this.SourceMediaName), CultureInfo.InvariantCulture);
                this.CarId = car;
                ForzaZip carZip = new ForzaZip(GamePaths.FindCar(this.TargetRoot, this.NewMediaName));
                Fm2CarbinModel model = null;
                foreach (ForzaZip.Entry entry in carZip.Entries)
                {
                    if (entry.Name.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase))
                    {
                        model = new Fm2CarbinModel(carZip.Read(entry));
                        break;
                    }
                }
                HashSet<string> parts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Fm2CarbinModel.Part part in model.Parts)
                {
                    parts.Add(part.Name);
                }
                // lookups FM2 lacks (make, cylinders, part makers): rows into the database being converted, so the car keeps
                // the source's meaning; strings and logos are written after the check
                Fm2LookupPort lookups = new Fm2LookupPort();
                lookups.SourceRoot = this.SourceRoot;
                lookups.SourceFallbackRoot = this.SourceBaseRoot;
                lookups.TargetRoot = this.TargetRoot;
                lookups.BackupFolder = backup;
                lookups.Begin(source, db);
                try
                {
                    if (this.MakeId <= 0)
                    {
                        lookups.PrepareMake(Fm2LookupPort.SourceMake(source, this.SourceMediaName));   // a chosen Make: the source's is not added
                    }
                }
                finally
                {
                    this.Log.AddRange(lookups.Log);
                    lookups.Log.Clear();
                }
                CarDataToFm2 data = new CarDataToFm2();
                data.IdResolver = lookups.Resolve;
                data.Target = db;
                data.Source = source;
                data.Car = car;
                data.SourceCar = sourceCar;
                data.ModelParts = parts;
                data.Model = model;
                data.Swaps = this.Swaps;
                data.MakeOverride = this.MakeId;
                try
                {
                    data.Run();
                }
                finally
                {
                    this.Log.AddRange(data.Log);
                }
                Fm2Perf.Result perf = Fm2Perf.Compute(db, car, Path.Combine(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "AI"), "PerfPrediction"));
                if (perf == null || double.IsNaN(perf.Pi))
                {
                    throw new InvalidOperationException("FM2's performance numbers could not be computed for the car: the database was not written.");
                }
                long carClass = 5;
                foreach (object[] row in db.Query("SELECT Id, MaxPerformanceIndex FROM CarClasses WHERE Id < 6 ORDER BY MaxPerformanceIndex").Rows)
                {
                    carClass = Convert.ToInt64(row[0], CultureInfo.InvariantCulture);
                    if (perf.Pi <= Convert.ToDouble(row[1], CultureInfo.InvariantCulture))
                    {
                        break;
                    }
                }
                db.Execute("UPDATE Data_Car SET PerformanceIndex = ?, ClassID = ?, SpeedRating = ?, AccelerationRating = ?, SimPeakTorque = ?, SimPeakTorqueAngVel = ?, SimPeakPower = ?, SimPeakAngVel = ?, SimRedlineAngVel = ? WHERE Id = ?",
                    perf.Pi, carClass, perf.SpeedRating, perf.AccelerationRating, perf.PeakT, perf.PeakTw, perf.PeakP, perf.PeakPw, perf.RedlineW, car);
                this.Log.Add(string.Format(CultureInfo.InvariantCulture, "performance (FM2's calculation): PI {0}, speed {1:0.00}, acceleration {2:0.00}, {3:0} N.m at {4:0} rpm, {5:0} kW at {6:0} rpm",
                    Fm2Perf.Shown(db, perf.Pi), perf.SpeedRating, perf.AccelerationRating, perf.PeakT * 100, perf.PeakTw * 60 / (2 * Math.PI), perf.PeakP / 10, perf.PeakPw * 60 / (2 * Math.PI)));
                if (Fm2StockGarage.HasRow(db, car))
                {
                    Fm2StockGarage.Write(db, car);
                }
                DbIntegrity.Result integrity = DbIntegrity.Compare(original, db, ForzaGame.Motorsport2, null);
                List<string> errors = new List<string>(integrity.Errors);
                errors.AddRange(DbIds.CheckCar(db, car, template, this.NewMediaName, ForzaGame.Motorsport2));
                if (errors.Count > 0)
                {
                    foreach (string error in errors)
                    {
                        this.Log.Add("  ERROR " + error);
                    }
                    throw new InvalidOperationException("The converted database rows fail FM2's integrity rules (" + errors[0] + "): the database was not written.");
                }
                if (backup != null)
                {
                    Directory.CreateDirectory(backup);
                    File.WriteAllBytes(Path.Combine(backup, "gamedb_before_data.slt"), bytes);
                }
                lookups.Commit();
                this.Log.AddRange(lookups.Log);
                File.WriteAllBytes(dbPath, db.ToBytes());
            }
            this.Log.Add("== step 3 of 4 done: database");

            // 4. the engine sound
            this.Say(string.IsNullOrEmpty(this.UseSoundOf) ? "Building the engine sound (the longest step)..." : "Giving the car the sound of " + this.UseSoundOf + "...");
            Fm2SoundPort sound = new Fm2SoundPort();
            try
            {
                string engine;
                using (SqliteDatabase source = SqliteDatabase.FromBytes(File.ReadAllBytes(FindDatabase(this.SourceRoot))))
                {
                    engine = Convert.ToString(source.Scalar("SELECT e.MediaName FROM Data_Car c JOIN List_UpgradeEngine u ON u.Ordinal = c.Id AND u.IsStock = 1 JOIN Data_Engine e ON e.EngineID = u.EngineID WHERE c.MediaName = ? COLLATE NOCASE", this.SourceMediaName), CultureInfo.InvariantCulture);
                }
                sound.SourceRoot = this.SourceRoot;
                sound.SourceFallbackRoot = this.SourceBaseRoot;
                sound.SourceEngineMedia = engine;
                sound.SourceCarMedia = this.SourceMediaName;
                sound.TargetRoot = this.TargetRoot;
                sound.CarMedia = this.NewMediaName;
                sound.UseSoundOf = this.UseSoundOf;
                sound.Write = true;
                sound.BackupFolder = (backup != null) ? Path.Combine(backup, "sound") : null;
                sound.Run();
                this.Log.AddRange(sound.Log);
                byte[] now = File.ReadAllBytes(dbPath);
                using (SqliteDatabase db = SqliteDatabase.FromBytes(now))
                {
                    db.Execute("UPDATE Data_Engine SET MediaName = ? WHERE EngineID IN (SELECT u.EngineID FROM Data_Car c JOIN List_UpgradeEngine u ON u.Ordinal = c.Id AND u.IsStock = 1 WHERE c.MediaName = ? COLLATE NOCASE)", this.NewMediaName, this.NewMediaName);
                    File.WriteAllBytes(dbPath, db.ToBytes());
                }
                this.SoundDone = true;
                this.Log.Add("== step 4 of 4 done: engine sound");
            }
            catch (Exception e)
            {
                this.Log.AddRange(sound.Log);
                this.Log.Add("== step 4 of 4 FAILED, the car keeps the FM2 template's engine sound: " + e.Message);
            }
            this.WriteRecord();
        }

        // The port's record into the car zip (Fm2PortRecord): what a port again reads back. A failure here leaves the car
        // as installed; only the choices are not kept for a later port again.
        private void WriteRecord()
        {
            Fm2PortRecord record = new Fm2PortRecord();
            record.SourceGame = this.SourceGameName ?? "";
            record.SourceMediaName = this.SourceMediaName;
            record.SourcePack = this.SourcePack ?? "";
            record.Template = this.TemplateMediaName;
            record.Skip.AddRange(this.SkipParts);
            foreach (KeyValuePair<string, string> pair in this.CarMaterials)
            {
                record.CarMaterials[pair.Key] = pair.Value;
            }
            foreach (KeyValuePair<string, string> pair in this.WheelMaterials)
            {
                record.WheelMaterials[pair.Key] = pair.Value;
            }
            record.UseSoundOf = this.UseSoundOf;
            record.Swaps = (this.Swaps != null) ? new List<CarDataToFm2.Fm2Swap>(this.Swaps) : null;
            record.MakeId = this.MakeId;
            record.Log.AddRange(this.Log);
            try
            {
                record.Write(this.TargetRoot, GamePaths.FindCar(this.TargetRoot, this.NewMediaName));
                this.Log.Add("record: " + Fm2PortRecord.EntryName + " in the car zip (source, template, your choices) for a later port again");
            }
            catch (Exception e)
            {
                this.Log.Add("record: " + Fm2PortRecord.EntryName + " NOT written (" + e.Message + "): a later port again will not know your choices");
            }
        }
    }
}
