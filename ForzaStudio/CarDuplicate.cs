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
    public partial class CarDuplicate
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
        public long? ChosenId;                      // the car Id chosen by the user (DbIds.ChosenCarIdProblem); null = the first free one
        public EngineSpec Engine;                  // an engine of its own for the copy (CarEngine); null = the source's, shared
        public GearboxSpec Gearbox;                 // a gearbox (drivetrain) of its own for the copy (CarGearbox); null = the source's, shared
        public string PerfMedia;                    // the GAME's Media folder for the performance numbers: FM2 AI\PerfPrediction, FM3 / FM4 Physics\PI.xml (a pack's stage has neither); null = this.Media

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
        private ForzaGame game = ForzaGame.Motorsport4;
        public byte[] PlannedDatabase;              // after Plan(): the game database with the copy, as Run() would write it

        // The game of TargetRoot (default.xex); a folder without one (the staged copy of a DLC pack build) is FM4's.
        public ForzaGame Game
        {
            get
            {
                if (this.GameOverride.HasValue)
                {
                    return this.GameOverride.Value;     // the staged copy of a pack build is the open game's
                }
                ForzaGame found = (this.TargetRoot != null) ? GameInfo.Detect(this.TargetRoot) : ForzaGame.Unknown;
                return (found == ForzaGame.Motorsport2 || found == ForzaGame.Motorsport3) ? found : ForzaGame.Motorsport4;
            }
        }

        public List<string> Plan()
        {
            if (this.SourcePackage != null)
            {
                // A package car is staged (taken out of the package) only when the copy is made: the plan says what will happen.
                List<string> outline = new List<string>();
                outline.Add("source: " + this.SourceMediaName + " from the installed package " + Path.GetFileName(this.SourcePackage) + " (only read)");
                outline.Add("the car, its wheel, thumbnail and sound files and its database rows are taken out of the package, then copied as a car of the game folder");
                outline.Add("the copy keeps the package rows it points at (a shared engine...) that the game lacks; the database rules are checked before anything is written");
                outline.Add((this.PackContent != null || this.ConsoleOutput != null) ? string.Format(CultureInfo.InvariantCulture, "destination: a new DLC pack \"{0}\" (offer {1})", this.PackName, this.PackOffer) : "destination: the game folder (backups first)");
                if (this.Fm2ProfileNote != null)
                {
                    outline.Add(this.Fm2ProfileNote);
                }
                foreach (string line in outline)
                {
                    this.Report.AppendLine(line);
                }
                return outline;
            }
            List<string> plan = this.Execute(false);
            if (this.PackContent != null || this.ConsoleOutput != null)
            {
                plan.Insert(0, string.Format(CultureInfo.InvariantCulture, "destination: a new DLC pack \"{0}\" (offer {1}) {2}; the game folder is not changed", this.PackName, this.PackOffer, (this.ConsoleOutput != null) ? "as a package file in " + this.ConsoleOutput : "in " + this.PackContent));
                this.Report.Insert(0, plan[0] + "\r\n");
            }
            if (this.Fm2ProfileNote != null)
            {
                plan.Add(this.Fm2ProfileNote);
                this.Report.AppendLine(this.Fm2ProfileNote);
            }
            return plan;
        }

        // FM2 (user, checked in game 2026-11-15): a car added to the game folder is unlocked only in a profile made after it;
        // an existing save keeps its own car list (game limit). DLC packs are unlocked by their licence and have no such limit.
        private string Fm2ProfileNote
        {
            get
            {
                return (GameInfo.Detect(this.TargetRoot) == ForzaGame.Motorsport2 && this.PackContent == null && this.ConsoleOutput == null)
                    ? "NOTE (FM2): a car added to the game folder is unlocked only in a NEW profile; an existing save keeps it locked (game limit). A DLC pack destination has no such limit."
                    : null;
            }
        }

        public void Run()
        {
            if (this.PackContent == null && this.ConsoleOutput == null && this.SourcePackage != null)
            {
                this.RunFromPackageToGame();   // CarDuplicate.Package.cs
                return;
            }
            if (this.PackContent == null && this.ConsoleOutput == null)
            {
                this.Execute(true);
                if (this.RefreshNames)
                {
                    // The new names are in the game folder's tables; the installed DLC replace those tables (DlcPackage.Names).
                    DlcPackage.RefreshNamesPack(this.Game, this.TargetRoot, this.NamesContent(), this.Report);
                }
                return;
            }
            this.RunPack();
        }

        public bool RefreshNames = true;            // false for the staged copy inside a DLC pack build
        public ForzaGame? GameOverride;             // the staged copy (no default.xex) of a pack build: the open game's

        // The package file landed in a Xenia content folder of its game: the open game's, or one holding a names pack.
        private bool PackageInXenia(ForzaGame packGame)
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(this.PackageFile));
            foreach (string content in new string[] { this.XeniaContent, InstalledDlc.ContentFolder(packGame) })
            {
                if (!string.IsNullOrEmpty(content) && string.Equals(Path.GetFullPath(content).TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return DlcPackage.FindNamesPack(folder, packGame) != null;
        }

        // Xenia's content folder of the game: whose DLC string tables the names pack is built on.
        private string NamesContent()
        {
            // XeniaContent is the open game's content folder (Duplicate Car window); a harness may leave it out
            return this.XeniaContent ?? InstalledDlc.ContentFolder(this.Game);
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
            // FM3: DlcPackage.Fm3 (folder <title>0CCF<offer>, MultiDiscContentPacks row), checked in game 2026-11-02;
            // FM2: DlcPackage.Fm2 (folder DLC<offer>_FS, no offer row, licence mask with StockGarage), not checked in game yet.
            ForzaGame packGame = this.Game;
            if (work == null && Directory.Exists(PackagePath(packGame, content, this.PackOffer)))
            {
                if (!this.ReplacePack)
                {
                    throw new InvalidOperationException("A package with offer " + this.PackOffer + " is already in Xenia's content folder.");
                }
                this.RemovePack(content, packGame);
            }
            string stage = Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "DuplicatePack_" + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture));
            try
            {
                if (this.SourcePackage != null)
                {
                    this.BuildStageFromPackage(stage, false);   // CarDuplicate.Package.cs: the stage as a game car's copy leaves it
                }
                else
                {
                this.Stage(stage);
                CarDuplicate inner = new CarDuplicate();
                inner.TargetRoot = stage;
                inner.RefreshNames = false;
                inner.GameOverride = packGame;
                inner.SourceMediaName = this.SourceMediaName;
                inner.NewMediaName = this.NewMediaName;
                inner.XeniaContent = this.XeniaContent ?? this.PackContent ?? installed;
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
                foreach (KeyValuePair<string, object> pair in this.Data)
                {
                    inner.Data[pair.Key] = pair.Value;
                }
                inner.Engine = this.Engine;
                inner.Gearbox = this.Gearbox;
                inner.PerfMedia = this.PerfMedia ?? this.Media;   // the stage has no AI\PerfPrediction (FM2) / Physics\PI.xml (FM3, FM4): the game's
                inner.BackupFolder = Path.Combine(stage, "bak");
                inner.Run();
                this.NewId = inner.NewId;
                this.Report.Append(inner.Report.ToString());
                }
                StringBuilder text = new StringBuilder();
                List<string> others = DbIds.PackDatabases(installed);
                string offerText = this.PackOffer.ToString(CultureInfo.InvariantCulture);
                string package, offerFolder, mergeName;
                if (packGame == ForzaGame.Motorsport3)
                {
                    package = DlcPackage.BuildFolderFm3(this.TargetRoot, stage, content, installed, this.PackOffer, this.PackName, text);
                    offerFolder = this.PackOffer.ToString("X4", CultureInfo.InvariantCulture) + "_pri_65";
                    mergeName = offerText + "005_merge.slt";
                }
                else if (packGame == ForzaGame.Motorsport2)
                {
                    package = DlcPackage.BuildFolderFm2(this.TargetRoot, stage, content, installed, this.PackOffer, this.PackName, text);
                    offerFolder = DlcPackage.LayerFm2(this.PackOffer);
                    mergeName = offerText + "0000_merge.slt";
                }
                else
                {
                    package = DlcPackage.BuildFolder(this.TargetRoot, this.TargetRoot, stage, content, installed, this.PackOffer, this.PackName, text);
                    offerFolder = offerText + "_pri_65";
                    mergeName = offerText + "000_merge.slt";
                }

                // The package's database as the game will merge it into its own (and the other packs'): the
                // same integrity rules, so a pack never leaves a broken database behind.
                string patch = Path.Combine(Path.Combine(Path.Combine(Path.Combine(Path.Combine(Path.Combine(package, "Media"), "DLCZips"), offerFolder), "Media"), "db"), "patch");
                string mergeFile = Path.Combine(patch, mergeName);
                DbIntegrity.Result merged = DbIntegrity.CheckMerge(File.ReadAllBytes(Path.Combine(Path.Combine(this.Media, "db"), "gamedb.slt")), others, File.ReadAllBytes(mergeFile), packGame);
                if (!merged.Ok)
                {
                    throw new InvalidOperationException("The pack's database would break the game's, nothing was left in Xenia:\r\n  " + string.Join("\r\n  ", merged.Errors.ToArray()));
                }
                this.Report.AppendLine("pack database check: merged into the game's database the rules hold");
                foreach (string line in merged.Warnings)
                {
                    this.Report.AppendLine("  warning: " + line);
                }
                if (packGame == ForzaGame.Motorsport2)
                {
                    DlcPackage.ZipLayerFm2(package, this.PackOffer, text);
                }
                if (this.ConsoleOutput != null)
                {
                    string file = (packGame == ForzaGame.Motorsport3) ? DlcPackage.BuildConsolePackage(package, this.ConsoleOutput, this.PackName, this.Thumbnail, text, DlcPackage.TitleIdFm3, "Forza Motorsport 3")
                        : (packGame == ForzaGame.Motorsport2) ? DlcPackage.BuildConsolePackage(package, this.ConsoleOutput, this.PackName, this.Thumbnail, text, DlcPackage.TitleIdFm2, "Forza Motorsport 2")
                        : DlcPackage.BuildConsolePackage(package, this.ConsoleOutput, this.PackName, this.Thumbnail, text);
                    this.PackageFile = file;
                }
                this.Report.AppendLine("DLC package \"" + this.PackName + "\" (offer " + this.PackOffer + ") " + ((this.ConsoleOutput != null) ? "as a package file in " + this.ConsoleOutput : "in " + this.PackContent) + ":");
                this.Report.Append(text.ToString());
                if (work == null && this.RefreshNames)
                {
                    // the names pack (priority 999) is above the new pack's string tables: rebuilt on them, the new car keeps its name
                    DlcPackage.RefreshNamesPack(packGame, this.TargetRoot, content, this.Report);
                }
                else if (this.RefreshNames && this.PackageFile != null && this.PackageInXenia(packGame))
                {
                    // A package file written straight into Xenia's content folder (in game 2026-11-16: FM3 Supra SZ showed
                    // "ForzaStudio EVO names (FM3)"): the names pack there was not rebuilt, its tables (priority 999) lacked
                    // the new keys and used the same free ones for its own title. Rebuilt on the new package too.
                    DlcPackage.RefreshNamesPack(packGame, this.TargetRoot, Path.GetDirectoryName(this.PackageFile), this.Report);
                }
            }
            catch (Exception)
            {
                try
                {
                    this.RemovePack(content, packGame);
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

        private static string PackagePath(ForzaGame game, string content, int offer)
        {
            return (game == ForzaGame.Motorsport3) ? DlcPackage.PackagePathFm3(content, offer)
                : (game == ForzaGame.Motorsport2) ? DlcPackage.PackagePathFm2(content, offer)
                : DlcPackage.PackagePath(content, offer);
        }

        private void RemovePack(string content, ForzaGame game)
        {
            if (game == ForzaGame.Motorsport3)
            {
                DlcPackage.RemoveFm3(content, this.PackOffer);
            }
            else if (game == ForzaGame.Motorsport2)
            {
                DlcPackage.RemoveFm2(content, this.PackOffer);
            }
            else
            {
                DlcPackage.Remove(content, this.PackOffer);
            }
        }

        // The files a copy reads or changes, at the same place under a temporary root (also the FM2 port window's pack
        // stage: one call per template car, before any car is ported into it).
        internal void Stage(string stage)
        {
            List<string> files = new List<string>();
            files.Add(Path.Combine(Path.Combine(this.Media, "db"), "gamedb.slt"));
            files.Add(ZipManifest.PathFor(this.TargetRoot));
            files.Add(Path.Combine(Path.Combine(this.Media, "cars"), this.SourceMediaName + ".zip"));
            files.AddRange(Directory.GetFiles(Path.Combine(this.Media, "StringTables"), "*.zip"));
            string audio = Path.Combine(Path.Combine(this.Media, "audio"), "cars");
            // FM4 / FM3: the car model and engine tunings; FM2: EmitterData and DSPTuning (by the car's name)
            foreach (string tuning in new string[] { Path.Combine(audio, "CarModelTuning.zip"), Path.Combine(Path.Combine(audio, "Engines"), "enginetuning.zip"),
                Path.Combine(audio, "EmitterData.zip"), Path.Combine(Path.Combine(audio, "Engines"), "DSPTuning.zip") })
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
            // One spelling of the folder (F:/x and F:\x): the zipmanifest.xml paths are made from it and the files' paths.
            this.TargetRoot = Path.GetFullPath(this.TargetRoot).TrimEnd(Path.DirectorySeparatorChar);
            this.game = this.Game;
            string gameTag = GameInfo.Tag(this.game);
            string carsFolder = Path.Combine(this.Media, "cars");
            string sourceZipPath = Path.Combine(carsFolder, this.SourceMediaName + ".zip");
            if (!File.Exists(sourceZipPath))
            {
                throw new InvalidOperationException("The " + gameTag + " folder has no " + this.SourceMediaName + ".zip. If it is a car of a DLC pack, copying DLC cars is not supported yet (TODO NOW 2b): copy a car of the base game, or port the DLC car.");
            }
            // The game's own spelling of the file names (FM2 names every car zip in lower case): the copy's follows it.
            sourceZipPath = ActualPath(sourceZipPath);
            bool lowerNames = Path.GetFileName(sourceZipPath) == Path.GetFileName(sourceZipPath).ToLowerInvariant();
            string newZipPath = Path.Combine(Path.GetDirectoryName(sourceZipPath), (lowerNames ? this.NewMediaName.ToLowerInvariant() : this.NewMediaName) + ".zip");
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
                if (this.ChosenId.HasValue)
                {
                    string idProblem = DbIds.ChosenCarIdProblem(db, this.ChosenId.Value, DbIds.PackCarIds(DbIds.PackDatabases(this.XeniaContent)), this.game);
                    if (idProblem != null)
                    {
                        throw new InvalidOperationException(idProblem);
                    }
                    this.NewId = this.ChosenId.Value;
                    reason = "chosen by the user";
                }
                else if (this.game == ForzaGame.Motorsport4)
                {
                    this.NewId = DbIds.CarId(db, db, this.NewMediaName, DbIds.PackDatabases(this.XeniaContent), DbIds.InstallFirstCar, false, out reason);
                }
                else
                {
                    // FM3: above every car of the game and its official DLC (GameIdLimits); FM2: 659-1000 first, then from 1025 with a
                    // warning (DbIds.Fm2UsableCarId, 1001-1024 are the official DLC's); both free in the packs of the game's own Xenia content folder.
                    long first = (this.game == ForzaGame.Motorsport2) ? DbIds.Fm2FirstFreeCarId : Math.Max(1, GameIdLimits.MaxCar(this.game) + 1);
                    this.NewId = DbIds.CarId(db, db, this.NewMediaName, DbIds.PackDatabases(this.XeniaContent), first, false, out reason);
                    if (this.game == ForzaGame.Motorsport2 && !DbIds.Fm2UsableCarId(this.NewId))
                    {
                        this.NewId = DbIds.CarId(db, db, this.NewMediaName, DbIds.PackDatabases(this.XeniaContent), DbIds.Fm2SkipDlcIds(this.NewId), false, out reason);
                    }
                }
                if (this.game == ForzaGame.Motorsport2 && DbIds.Fm2CarIdWarning(this.NewId) != null)
                {
                    plan.Add("WARNING: " + DbIds.Fm2CarIdWarning(this.NewId));
                }
                plan.Add(string.Format(CultureInfo.InvariantCulture, "{0} (id {1}) -> {2}: id {3}, {4}", this.SourceMediaName, sourceId, this.NewMediaName, this.NewId, reason));

                // Database: the rows of a renumbered copy that the game database lacks.
                work = SqliteDatabase.FromBytes(db.ToBytes());
                plan.AddRange(DbIds.RenumberCar(work, sourceId, this.NewId, false, this.game));
                // FM2 / FM3: the car's rows keyed by a counter get new keys (FM2's physics, aero, prices, engine combo).
                HashSet<string> cloned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (this.game != ForzaGame.Motorsport4)
                {
                    plan.AddRange(RenumberCounters(work, db, sourceId, this.NewId, this.game, cloned));
                }
                Dictionary<string, List<string>> schema = DbIds.Schema(db);
                List<string[]> carColumns = DbIds.CarColumns(schema, this.game);
                // Every write to the database is one transaction: an error puts all of it back.
                db.Begin();
                writing = true;
                int inserted = 0;
                List<string> inserts = new List<string>();
                List<string> skipped = new List<string>();
                foreach (KeyValuePair<string, List<string>> table in schema)
                {
                    List<string> columns = table.Value;
                    string key = DbIds.KeyColumn(db, table.Key, columns, this.game);
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
                        else if (this.game != ForzaGame.Motorsport4 && keyIndex >= 0 && row[keyIndex] is long && cloned.Contains(table.Key + "#" + Convert.ToString(row[keyIndex], CultureInfo.InvariantCulture)))
                        {
                            take = true;   // a row RenumberCounters gave the copy (FM2 aero)
                        }
                        else if (this.game != ForzaGame.Motorsport4 && carIndexes.Count > 0)
                        {
                            // FM2 / FM3: every row of the car's own tables (counter keys already new), never the game's
                            // content that only names the car (events, races, rewards, the starting garage; user 2026-11-14).
                            take = false;
                            if (!PointsAtCarOnly(table.Key))
                            {
                                foreach (int index in carIndexes)
                                {
                                    take = take || (index >= 0 && row[index] is long && (long) row[index] == this.NewId);
                                }
                            }
                        }
                        else if (carIndexes.Count > 0 && (keyIndex < 0 || keyIsCar))
                        {
                            take = false;
                            foreach (int index in carIndexes)
                            {
                                take = take || (index >= 0 && row[index] is long && (long) row[index] == this.NewId);
                            }
                        }
                        else if (this.game == ForzaGame.Motorsport4 && string.Equals(table.Key, "Combo_Engines", StringComparison.OrdinalIgnoreCase) && keyIndex >= 0 && carIndexes.Exists(delegate(int i) { return i >= 0 && row[i] is long && (long) row[i] == this.NewId; }))
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
                    db.Execute("UPDATE Data_Car SET MakeID = ? WHERE Id = ?", this.MakeId.Value, this.NewId);
                    // FM3 / FM4 keep the make's name and icon in Data_Car too; FM2 has neither column.
                    if (schema["Data_Car"].Exists(delegate(string c) { return string.Equals(c, "MakeName", StringComparison.OrdinalIgnoreCase); }))
                    {
                        db.Execute("UPDATE Data_Car SET MakeName = ? WHERE Id = ?", makeName, this.NewId);
                    }
                    if (schema["Data_Car"].Exists(delegate(string c) { return string.Equals(c, "MakeIcon", StringComparison.OrdinalIgnoreCase); }))
                    {
                        object icon = db.Scalar("SELECT MakeIcon FROM Data_Car WHERE MakeID = ? AND Id <> ? LIMIT 1", this.MakeId.Value, this.NewId);
                        if (icon != null)
                        {
                            db.Execute("UPDATE Data_Car SET MakeIcon = ? WHERE Id = ?", icon, this.NewId);
                        }
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
                if (this.Engine != null && this.game == ForzaGame.Motorsport2)
                {
                    // FM2 (CarEngine.Fm2.cs): every level copied; the sound stays the car's (EmitterData + DSPTuning by the CAR's
                    // name, copied below), the engine keeps the source's MediaName (FM2 has no per-engine tuning file).
                    CarEngine.CloneFm2(db, this.NewId, this.Engine, plan);
                }
                else if (this.Engine != null)
                {
                    long engineId = CarEngine.Clone(db, this.NewId, this.Engine, plan, this.game);
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
                if (this.Gearbox != null && this.game == ForzaGame.Motorsport2)
                {
                    CarGearbox.ApplyFm2(db, this.NewId, this.Gearbox, plan);   // FM2: the copy's own List_UpgradeTransmission rows
                }
                else if (this.Gearbox != null)
                {
                    CarGearbox.Clone(db, this.NewId, this.Gearbox, plan, this.game);
                }
                if ((this.Engine != null || this.Gearbox != null) && this.game == ForzaGame.Motorsport2)
                {
                    // FM2: the game's own calculation (Arcade and the dealer show the stored numbers, 2026-11-16).
                    Fm2Perf.Update(db, original, sourceId, this.NewId, this.PerfMedia ?? this.Media, this.Data.Keys, plan);
                }
                else if ((this.Engine != null || this.Gearbox != null) && this.game == ForzaGame.Motorsport3)
                {
                    // FM3: the game's own calculation too (the lap of Media\Physics\PI.xml, docs/FM3/PI_AND_RATINGS.md), as a whole set.
                    Fm3Perf.Update(db, original, sourceId, this.NewId, this.PerfMedia ?? this.Media, this.Data.Keys, plan);
                }
                else if ((this.Engine != null || this.Gearbox != null) && this.game == ForzaGame.Motorsport4)
                {
                    // FM4: its own lap of Media\Physics\PI.xml (docs/FM4/PI_AND_RATINGS.md), as a whole set.
                    Fm4Perf.Update(db, original, sourceId, this.NewId, this.PerfMedia ?? this.Media, this.Data.Keys, plan);
                }
                else if (this.Engine != null || this.Gearbox != null)
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
                    long newWheel = DbIds.Next(db, "List_Wheels", "ID", this.game);
                    CopyRow(db, "List_Wheels", "ID", oldWheel, newWheel);
                    db.Execute("UPDATE List_Wheels SET MediaName = ? WHERE ID = ?", newWheelMedia, newWheel);
                    db.Execute("UPDATE List_Wheels SET IconPath = ? WHERE ID = ? AND IconPath = ? COLLATE NOCASE", newWheelMedia, newWheel, sourceWheelMedia);
                    int extra = CopyRow(db, "WheelExceptions", "WheelID", oldWheel, newWheel) + CopyRow(db, "WheelNormOffsets", "Id", oldWheel, newWheel);
                    db.Execute("UPDATE Data_Car SET StockWheelID = ? WHERE Id = ?", newWheel, this.NewId);
                    plan.Add(string.Format(CultureInfo.InvariantCulture, "wheel: {0} (List_Wheels {1}), a copy of {2} (id {3}){4}", newWheelMedia, newWheel, sourceWheelMedia, oldWheel, (extra > 0) ? ", with its WheelExceptions / WheelNormOffsets rows" : string.Empty));
                }

                // FM2 lists its cars from StockGarage: the copy's row, built as the official DLC build theirs, when the source has one.
                if (this.game == ForzaGame.Motorsport2 && (Fm2StockGarage.HasRow(original, sourceId) || this.SourceFromPackage))   // a DLC car's row comes from its licence mask
                {
                    Fm2StockGarage.Write(db, this.NewId);
                    plan.Add("StockGarage: +1 row (Id " + this.NewId.ToString(CultureInfo.InvariantCulture) + "), the official DLC query (FM2 lists its cars from this table)");
                }
                else if (this.game == ForzaGame.Motorsport2)
                {
                    plan.Add("StockGarage: no row, the source " + this.SourceMediaName + " has none either (an unlock or special car)");
                }
                if (this.game == ForzaGame.Motorsport2)
                {
                    // FM2's career dealer sells a car from the driver level its Rewards_DriverLevel rows of TypeId 3 give per region
                    // (Mustang GT 05: region 3 at 0, 1 at 12, 2 at 14). Without them the copy is never for sale (user 2026-11-14:
                    // "locked"). The copy gets the source's; the other types (1, 2: a car given at a level) stay the game's.
                    int dealer = Fm2StockGarage.CopyDealerLevels(db, sourceId, this.NewId);
                    plan.Add(string.Format(CultureInfo.InvariantCulture, "Rewards_DriverLevel: +{0} row(s) (CarId {1}, TypeId 3: the dealer sells it from the source's driver levels)", dealer, this.NewId));
                    // A copy in the game folder whose source has no dealer / Arcade rows (disc 2 / DLC car) would stay locked:
                    // it gets them open (user 2026-11-15). Packs never carry these tables (DlcPackage.Fm2).
                    if (this.PackContent == null && this.ConsoleOutput == null)
                    {
                        int opened = (dealer == 0) ? Fm2StockGarage.DefaultDealerLevels(db, this.NewId) : 0;
                        if (opened > 0)
                        {
                            plan.Add(string.Format(CultureInfo.InvariantCulture, "Rewards_DriverLevel: +{0} row(s) (CarId {1}, TypeId 3, driver level 0 in every region: the source has none)", opened, this.NewId));
                        }
                        if (Fm2StockGarage.EnsureArcadeList(db, this.NewId))
                        {
                            plan.Add("ArcadeCarList: +1 row (CarId " + this.NewId.ToString(CultureInfo.InvariantCulture) + ", Level 0 = open; the source has none)");
                        }
                    }
                }
                if (this.game == ForzaGame.Motorsport2 && Fm2StockGarage.EnableArcade(db, this.NewId))
                {
                    plan.Add("NewProfile_Arcade_Garage: +1 row (CarId " + this.NewId.ToString(CultureInfo.InvariantCulture) + "), FM2 copies are always in Arcade (user rule)");
                }

                // The integrity rules (DbIntegrity, DbIds.CheckCar): nothing is written when one breaks.
                List<string> errors = DbIds.CheckCar(db, this.NewId, sourceId, this.NewMediaName, this.game);
                DbIntegrity.Result check = DbIntegrity.Compare(original, db, this.game, null);
                errors.AddRange(check.Errors);
                if (errors.Count > 0)
                {
                    throw new InvalidOperationException("Database check failed, nothing was written:\r\n  " + string.Join("\r\n  ", errors.ToArray()));
                }
                db.Commit();
                writing = false;
                plan.Add("database check: the rules hold (keys above " + gameTag + "'s largest and unique, every id points at a row" + (this.game == ForzaGame.Motorsport2 ? "" : ", the car's Data_CarBody row, ModelId = Id") + ")");
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
                HashSet<int> dlcKeys = DlcPackage.InstalledStringKeys(this.game, this.NamesContent(), "Data_Car.str");   // the names pack merges them over the game's
                foreach (string[] column in stringColumns)
                {
                    string value = db.Scalar("SELECT " + column[0] + " FROM Data_Car WHERE Id = ?", sourceId) as string;
                    uint stringId;
                    if (value == null || !value.StartsWith("_&", StringComparison.Ordinal) || !uint.TryParse(value.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out stringId))
                    {
                        continue;
                    }
                    int oldKey = (int) (stringId & 0xFFFF);
                    int newKey = GameStringKeys.Free("Data_Car", files.Values, dlcKeys);
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
                    plan.Add(string.Format(CultureInfo.InvariantCulture, "strings: {0} key {1} (free in the game's tables and FM4's DLC list) in {2} languages", column[0], newKey, files.Count));
                }
                if (this.game == ForzaGame.Motorsport2 && Fm2StockGarage.HasRow(db, this.NewId))
                {
                    Fm2StockGarage.Write(db, this.NewId);   // again: its DisplayName / ModelName are Data_Car's new string keys
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
                        throw new InvalidOperationException("The " + gameTag + " folder has no Media\\wheels\\" + sourceWheelMedia + ".zip (the source's stock wheel).");
                    }
                    from = ActualPath(from);
                    bool lowerWheel = Path.GetFileName(from) == Path.GetFileName(from).ToLowerInvariant();
                    wheelZips.Add(new KeyValuePair<string, ForzaZip>(Path.Combine(Path.GetDirectoryName(from), (lowerWheel ? newWheelMedia.ToLowerInvariant() : newWheelMedia) + ".zip"), RenamedZip(from, sourceWheelMedia, newWheelMedia)));
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
                ForzaZip modelZip;
                ForzaZip tuningZip = null;
                if (this.game == ForzaGame.Motorsport2)
                {
                    // FM2 finds both by the CAR's name: Audio\Cars\EmitterData.zip <car>_ED.xml (324 cars) and
                    // Audio\Cars\Engines\DSPTuning.zip <car>_DSPTuning.xml (351; the harmonic tunings and banks it names stay the
                    // game's). A copy without them would be silent. The suffix keeps the source's spelling (_DSPtuning / _DSPTuning).
                    modelPath = Path.Combine(audioFolder, "EmitterData.zip");
                    tuningPath = Path.Combine(Path.Combine(audioFolder, "Engines"), "DSPTuning.zip");
                    modelZip = AddCopy(modelPath, this.SourceMediaName + "_ED.xml", this.NewMediaName + "_ED.xml", plan, "sound: emitter data");
                    // The engine sound: the DSP tuning of the car chosen in the engine dialog (its engine's MediaName is that car's,
                    // 321 of 321), else the source's. The emitter data (where engine and exhausts sit) stays the source's.
                    object chosenSound;
                    string soundCar = (this.Engine != null && this.Engine.Data.TryGetValue("MediaName", out chosenSound) && chosenSound is string) ? (string) chosenSound : this.SourceMediaName;
                    string dsp = EntryNameLike(tuningPath, soundCar + "_DSPTuning.xml");
                    if (dsp == null && soundCar != this.SourceMediaName)
                    {
                        plan.Add("sound: " + soundCar + "_DSPTuning.xml is not in DSPTuning.zip, the source's sound is kept");
                        soundCar = this.SourceMediaName;
                        dsp = EntryNameLike(tuningPath, soundCar + "_DSPTuning.xml");
                    }
                    // An own engine: the tuning's audio_rpm_max / audio_rpm_min (the rpm range the sound plays over, FM2's form of FM4's
                    // audio_rpm_redline / _idle) follow the new redline and idle against the sound car's, so the sound runs up to the new limiter.
                    EntryEdit tune = null;
                    if (this.Engine != null)
                    {
                        object soundEngine = original.Scalar("SELECT EngineID FROM Data_Engine WHERE MediaName = ? COLLATE NOCASE ORDER BY EngineID LIMIT 1", soundCar);
                        double[] was = Fm2CamRpm(original, (soundCar != this.SourceMediaName && soundEngine != null) ? Convert.ToInt64(soundEngine, CultureInfo.InvariantCulture) : CarEngine.StockEngine(original, sourceId));
                        double[] now = Fm2CamRpm(db, CarEngine.StockEngine(db, this.NewId));
                        long newAspiration = Convert.ToInt64(db.Scalar("SELECT AspirationID_Stock FROM Data_Engine WHERE EngineID = ?", CarEngine.StockEngine(db, this.NewId)), CultureInfo.InvariantCulture);
                        bool turbo = newAspiration >= 2 && newAspiration <= 4;
                        double? turboVolume = this.Engine.TurboVolume;
                        double rf = (was != null && now != null && was[0] > 0) ? now[0] / was[0] : 1.0;
                        double inf = (was != null && now != null && was[1] > 0) ? now[1] / was[1] : 1.0;
                        tune = delegate(byte[] data, List<string> lines) { return TuneFm2Sound(data, lines, rf, inf, turbo, turboVolume); };
                    }
                    tuningZip = (dsp == null) ? null : AddCopy(tuningPath, dsp, this.NewMediaName + dsp.Substring(soundCar.Length), plan, "sound: engine DSP tuning" + ((soundCar != this.SourceMediaName) ? " (of " + soundCar + ")" : ""), tune);
                    if (dsp == null)
                    {
                        plan.Add("sound: engine DSP tuning: " + this.SourceMediaName + "_DSPTuning.xml is not in DSPTuning.zip, nothing added");
                    }
                }
                else
                {
                    modelZip = AddCopy(modelPath, this.SourceMediaName + "_CMT.xml", this.NewMediaName + "_CMT.xml", plan, "sound: car model tuning");
                }
                // A turbo engine on the setup of a natural one (TurboVolume 0, as the Furai's) would whistle never: FM4's 137 turbo
                // engines all have TurboVolume 0.2 - 0.9 (median 0.5), the turbo sound's level in the tuning file.
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

                // Plan(): the database the copy would write, for the tests (fm4dev\ST02\dupall.exe); nothing is written.
                this.PlannedDatabase = apply ? null : db.ToBytes();

                if (apply)
                {
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
                    // A missing file is not "in use" (FileNotFoundException is an IOException: a game folder without
                    // zipmanifest.xml was reported as "zipmanifest.xml in use"); read-only files are made writable;
                    // the others name the process that keeps them (FileLocks, 2026-11-14).
                    List<string> locked = FileLocks.Busy(targets, delegate(string path) { return Path.GetFileName(path); });
                    if (locked.Count > 0)
                    {
                        throw new IOException(FileLocks.Message(locked));
                    }
                    // Every zipmanifest.xml line this copy adds or updates, found before anything is written: a missing one
                    // stops the copy here, not after the files and the database (2026-11-14: a manifest path failed last and
                    // left a half-made copy in FM2_Extracted_DEV).
                    List<string> manifestProblems = new List<string>();
                    List<string> written = new List<string>();
                    written.Add(newZipPath);
                    foreach (KeyValuePair<string, ForzaZip> wheelZip in wheelZips)
                    {
                        written.Add(wheelZip.Key);
                    }
                    foreach (string path in targets)
                    {
                        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            written.Add(path);
                        }
                    }
                    foreach (string path in written)
                    {
                        if (ZipManifest.GamePath(this.TargetRoot, path) == null)
                        {
                            manifestProblems.Add(path + ": no GAME:\\ path under " + this.TargetRoot);
                        }
                    }
                    if (!ZipManifest.Lists(manifestPath, "game:\\media\\cars\\" + this.SourceMediaName.ToLowerInvariant() + ".zip"))
                    {
                        manifestProblems.Add("zipmanifest.xml has no line for the source's car zip");
                    }
                    for (int i = 0; i < wheelLikes.Count; i++)
                    {
                        if (!ZipManifest.Lists(manifestPath, "game:\\media\\wheels\\" + wheelLikes[i].ToLowerInvariant() + ".zip"))
                        {
                            manifestProblems.Add("zipmanifest.xml has no line for the wheel zip " + wheelLikes[i]);
                        }
                    }
                    if (manifestProblems.Count > 0)
                    {
                        throw new InvalidOperationException("Nothing was written: " + string.Join("; ", manifestProblems.ToArray()));
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

        // FM2: the stock CamValves level's redline and idle (StallRPM) of an engine, or null.
        private static double[] Fm2CamRpm(SqliteDatabase db, long engine)
        {
            if (engine < 0)
            {
                return null;
            }
            SqliteDatabase.Result r = db.Query("SELECT RedlineRPM, StallRPM FROM " + CarEngine.Fm2CamTable + " WHERE EngineID = ? AND IsStock = 1 ORDER BY Level LIMIT 1", engine);
            return (r.Rows.Count == 0) ? null : new double[] { Convert.ToDouble(r.Rows[0][0], CultureInfo.InvariantCulture), Convert.ToDouble(r.Rows[0][1], CultureInfo.InvariantCulture) };
        }

        // FM2's DSPTuning: audio_rpm_max / audio_rpm_min times the redline / idle change (FM4's TuneEngineSound rule).
        // A turbo engine: TurboVolume of every upgrade level = the user's, or 0.5 where it is 0 (the whistle would never sound).
        private static byte[] TuneFm2Sound(byte[] data, List<string> plan, double redlineFactor, double idleFactor, bool turbo, double? turboVolume)
        {
            string text = System.Text.Encoding.UTF8.GetString(data);
            if (turbo)
            {
                int changed = 0;
                text = System.Text.RegularExpressions.Regex.Replace(text, "TurboVolume=\"([^\"]*)\"", delegate(System.Text.RegularExpressions.Match m)
                {
                    double now;
                    bool zero = !double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out now) || now <= 0;
                    if (!turboVolume.HasValue && !zero)
                    {
                        return m.Value;
                    }
                    changed++;
                    return "TurboVolume=\"" + (turboVolume.HasValue ? turboVolume.Value : 0.5).ToString("0.000000", CultureInfo.InvariantCulture) + "\"";
                });
                if (changed > 0)
                {
                    plan.Add(string.Format(CultureInfo.InvariantCulture, "sound: TurboVolume of {0} upgrade level(s) set to {1} in the DSP tuning (the engine is turbocharged)", changed, turboVolume.HasValue ? turboVolume.Value : 0.5));
                }
            }
            string[] names = { "audio_rpm_max", "audio_rpm_min" };
            double[] factors = { redlineFactor, idleFactor };
            for (int i = 0; i < names.Length; i++)
            {
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(text, names[i] + "=\"([^\"]*)\"");
                double was;
                if (factors[i] > 0 && Math.Abs(factors[i] - 1.0) > 0.001 && m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out was) && was > 0)
                {
                    double value = Math.Round(was * factors[i]);
                    text = text.Substring(0, m.Index) + names[i] + "=\"" + value.ToString(CultureInfo.InvariantCulture) + "\"" + text.Substring(m.Index + m.Length);
                    plan.Add(string.Format(CultureInfo.InvariantCulture, "sound: {0} {1:0} -> {2:0} in the DSP tuning (follows the engine's {3})", names[i], was, value, i == 0 ? "redline" : "idle"));
                }
            }
            return System.Text.Encoding.UTF8.GetBytes(text);
        }

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
                if (spec != null && spec.BovLevel > 0 && text.IndexOf("blowoffL1Level", StringComparison.OrdinalIgnoreCase) >= 0)   // FM3's tuning has no blow-off slots
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

        // The name an entry of the zip has (any case), or null.
        private static string EntryNameLike(string zipPath, string name)
        {
            if (!File.Exists(zipPath))
            {
                return null;
            }
            ForzaZip.Entry entry = new ForzaZip(zipPath).Find(name);
            return (entry == null) ? null : entry.Name;
        }

        // The path with the spelling the file system has (the game's own: FM2 names its zips in lower case).
        private static string ActualPath(string path)
        {
            string folder = Path.GetDirectoryName(path);
            foreach (string file in Directory.GetFiles(folder))
            {
                if (string.Equals(Path.GetFileName(file), Path.GetFileName(path), StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }
            return path;
        }

        // Tables that only name the car (the game's content: events, races, rewards, the starting garage). A copy is never
        // put in them (user 2026-11-14). FM4's procedure skips them by their key (an Id of their own, not the car's).
        private static bool PointsAtCarOnly(string table)
        {
            return string.Equals(table, "EventParticipants", StringComparison.OrdinalIgnoreCase)
                || string.Equals(table, "Races", StringComparison.OrdinalIgnoreCase)
                || string.Equals(table, "EventShowroomChallenges", StringComparison.OrdinalIgnoreCase)
                || table.StartsWith("Rewards_", StringComparison.OrdinalIgnoreCase)
                || table.StartsWith("NewProfile_", StringComparison.OrdinalIgnoreCase);
        }

        // Whether a row of 'table' belongs to the car: one of its car columns holds it, or its key is car * 1000 + n.
        private static string BelongsWhere(Dictionary<string, List<string>> schema, List<string[]> carColumns, SqliteDatabase db, string table, ForzaGame game, long car, List<object> args)
        {
            List<string> parts = new List<string>();
            foreach (string[] column in carColumns)
            {
                if (string.Equals(column[0], table, StringComparison.OrdinalIgnoreCase))
                {
                    parts.Add(SqliteDatabase.Quote(column[1]) + " = ?");
                    args.Add(car);
                }
            }
            string key = DbIds.KeyColumn(db, table, schema[table], game);
            if (key != null)
            {
                parts.Add(SqliteDatabase.Quote(key) + " BETWEEN ? AND ?");
                args.Add(car * 1000);
                args.Add((car * 1000) + 999);
            }
            return (parts.Count == 0) ? null : "(" + string.Join(" OR ", parts.ToArray()) + ")";
        }

        // FM2 (any game whose car rows are keyed by a counter, not car * 1000 + n): in the renumbered working copy 'work', the
        // new car's rows still hold the source's counter keys. (1) In a table with a car column, each such key gets the next
        // free value (above the game's largest, GameIdLimits, and every key of 'db' and 'work'), and the new car's rows that
        // point at it (the game's relation map) follow. (2) A row without a car column that only the source car's rows point at
        // (FM2's List_AeroPhysics: 347 rows of one car, 8 shared) is cloned under a new key for the copy; a row other cars use
        // stays shared. Keys made are added to 'cloned' (table#key). Returns the plan lines.
        private static List<string> RenumberCounters(SqliteDatabase work, SqliteDatabase db, long sourceId, long newId, ForzaGame game, HashSet<string> cloned)
        {
            List<string> lines = new List<string>();
            Dictionary<string, List<string>> schema = DbIds.Schema(work);
            List<string[]> carColumns = DbIds.CarColumns(schema, game);
            Dictionary<string, long> next = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            Func<string, string, long> take = delegate(string table, string key)
            {
                long value;
                if (!next.TryGetValue(table, out value))
                {
                    value = Math.Max(DbIds.Next(db, table, key, game), DbIds.Next(work, table, key, game));
                }
                next[table] = value + 1;
                return value;
            };
            List<string> done = new List<string>();
            // (1)
            foreach (string[] column in carColumns)
            {
                string table = column[0];
                string key = DbIds.KeyColumn(work, table, schema[table], game);
                if (key == null || string.Equals(key, column[1], StringComparison.OrdinalIgnoreCase) || PointsAtCarOnly(table))
                {
                    continue;
                }
                string t = SqliteDatabase.Quote(table), k = SqliteDatabase.Quote(key), c = SqliteDatabase.Quote(column[1]);
                List<long> olds = new List<long>();
                foreach (object[] row in work.Query("SELECT DISTINCT " + k + " FROM " + t + " WHERE " + c + " = ? AND NOT (" + k + " BETWEEN ? AND ?)", newId, newId * 1000, (newId * 1000) + 999).Rows)
                {
                    if (row[0] is long)
                    {
                        olds.Add((long) row[0]);
                    }
                }
                foreach (long old in olds)
                {
                    long now = take(table, key);
                    work.Execute("UPDATE " + t + " SET " + k + " = ? WHERE " + c + " = ? AND " + k + " = ?", now, newId, old);
                    Repoint(work, schema, carColumns, game, table, key, old, now, newId);
                    cloned.Add(table + "#" + now.ToString(CultureInfo.InvariantCulture));
                }
                if (olds.Count > 0)
                {
                    done.Add(string.Format(CultureInfo.InvariantCulture, "{0}.{1} {2}", table, key, olds.Count));
                }
            }
            // (2)
            foreach (GameDbSchema.Relation relation in GameDbSchema.For(game))
            {
                List<string> targetColumns;
                if (relation.IsComposite || !CloneWhenOwned(relation.Target) || !schema.ContainsKey(relation.Table) || !schema.TryGetValue(relation.Target, out targetColumns)
                    || carColumns.Exists(delegate(string[] cc) { return string.Equals(cc[0], relation.Target, StringComparison.OrdinalIgnoreCase); })
                    || string.Equals(relation.Target, "Data_Car", StringComparison.OrdinalIgnoreCase) || PointsAtCarOnly(relation.Table))
                {
                    continue;
                }
                string targetKey = DbIds.KeyColumn(work, relation.Target, targetColumns, game);
                if (targetKey == null || !string.Equals(targetKey, relation.TargetColumn, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                List<object> args = new List<object>();
                string mine = BelongsWhere(schema, carColumns, work, relation.Table, game, newId, args);
                if (mine == null)
                {
                    continue;
                }
                string rc = SqliteDatabase.Quote(relation.Column);
                List<long> values = new List<long>();
                foreach (object[] row in work.Query("SELECT DISTINCT " + rc + " FROM " + SqliteDatabase.Quote(relation.Table) + " WHERE " + mine, args.ToArray()).Rows)
                {
                    if (row[0] is long && !GameDbSchema.IsNone((long) row[0]) && !cloned.Contains(relation.Target + "#" + ((long) row[0]).ToString(CultureInfo.InvariantCulture))
                        && !((long) row[0] >= newId * 1000 && (long) row[0] <= (newId * 1000) + 999))
                    {
                        values.Add((long) row[0]);
                    }
                }
                foreach (long value in values)
                {
                    if (!OnlyCarUses(db, schema, carColumns, game, relation.Target, targetKey, value, sourceId))
                    {
                        continue;   // other cars' rows point at it too: shared, the copy points at the same row
                    }
                    // FM3 keys its aero rows car * 1000 + n (207 of 207 owned rows): the clone stays in the copy's range, at the
                    // same n when free. FM2 numbers them with a counter: the next free one.
                    long now = (game == ForzaGame.Motorsport3) ? FreeInCarRange(work, relation.Target, targetKey, newId, value) : take(relation.Target, targetKey);
                    string tt = SqliteDatabase.Quote(relation.Target), tk = SqliteDatabase.Quote(targetKey);
                    List<string> quoted = targetColumns.ConvertAll<string>(delegate(string x) { return SqliteDatabase.Quote(x); });
                    string select = string.Join(", ", targetColumns.ConvertAll<string>(delegate(string x) { return string.Equals(x, targetKey, StringComparison.OrdinalIgnoreCase) ? "?" : SqliteDatabase.Quote(x); }).ToArray());
                    work.Execute("INSERT INTO " + tt + " (" + string.Join(", ", quoted.ToArray()) + ") SELECT " + select + " FROM " + tt + " WHERE " + tk + " = ?", now, value);
                    Repoint(work, schema, carColumns, game, relation.Target, targetKey, value, now, newId);
                    cloned.Add(relation.Target + "#" + now.ToString(CultureInfo.InvariantCulture));
                    done.Add(string.Format(CultureInfo.InvariantCulture, "{0}.{1} {2} -> {3}", relation.Target, targetKey, value, now));
                }
            }
            if (done.Count > 0)
            {
                lines.Add("rows keyed by a counter, new keys for the copy: " + string.Join(", ", done.ToArray()));
            }
            return lines;
        }

        // The tables without a car column whose rows are a car's own data (fm4dev\ST02\own.exe over every car): FM2's
        // List_AeroPhysics (347 rows of one car, 8 shared). Not the engine (a plain copy shares it, as in FM4), nor a lookup
        // list (a part maker one car alone uses stays one row).
        private static bool CloneWhenOwned(string table)
        {
            return string.Equals(table, "List_AeroPhysics", StringComparison.OrdinalIgnoreCase);
        }

        // A key car * 1000 + n of the car 'car' that table.key does not use yet: n of 'old' first, else the first free n.
        private static long FreeInCarRange(SqliteDatabase work, string table, string key, long car, long old)
        {
            string sql = "SELECT count(*) FROM " + SqliteDatabase.Quote(table) + " WHERE " + SqliteDatabase.Quote(key) + " = ?";
            long first = (car * 1000) + (old % 1000);
            if (Convert.ToInt64(work.Scalar(sql, first), CultureInfo.InvariantCulture) == 0)
            {
                return first;
            }
            for (long n = 0; n < 1000; n++)
            {
                if (Convert.ToInt64(work.Scalar(sql, (car * 1000) + n), CultureInfo.InvariantCulture) == 0)
                {
                    return (car * 1000) + n;
                }
            }
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "{0}.{1}: no free key in {2}000 - {2}999 for the copy.", table, key, car));
        }

        // The new car's rows (BelongsWhere) that point at table.key = old point at now.
        private static void Repoint(SqliteDatabase work, Dictionary<string, List<string>> schema, List<string[]> carColumns, ForzaGame game, string table, string key, long old, long now, long newId)
        {
            foreach (GameDbSchema.Relation relation in GameDbSchema.Into(game, table))
            {
                if (relation.IsComposite || !string.Equals(relation.TargetColumn, key, StringComparison.OrdinalIgnoreCase) || !schema.ContainsKey(relation.Table))
                {
                    continue;
                }
                List<object> args = new List<object>();
                args.Add(now);
                args.Add(old);
                string mine = BelongsWhere(schema, carColumns, work, relation.Table, game, newId, args);
                if (mine == null)
                {
                    continue;
                }
                string c = SqliteDatabase.Quote(relation.Column);
                work.Execute("UPDATE " + SqliteDatabase.Quote(relation.Table) + " SET " + c + " = ? WHERE " + c + " = ? AND " + mine, args.ToArray());
            }
        }

        // True when, in the game's database 'db', only the rows of the car 'car' point at table.key = value.
        private static bool OnlyCarUses(SqliteDatabase db, Dictionary<string, List<string>> schema, List<string[]> carColumns, ForzaGame game, string table, string key, long value, long car)
        {
            foreach (GameDbSchema.Relation relation in GameDbSchema.Into(game, table))
            {
                if (relation.IsComposite || !string.Equals(relation.TargetColumn, key, StringComparison.OrdinalIgnoreCase) || !schema.ContainsKey(relation.Table))
                {
                    continue;
                }
                List<object> args = new List<object>();
                args.Add(value);
                string mine = BelongsWhere(schema, carColumns, db, relation.Table, game, car, args);
                string where = SqliteDatabase.Quote(relation.Column) + " = ?" + ((mine == null) ? "" : " AND NOT " + mine);
                if (Convert.ToInt64(db.Scalar("SELECT count(*) FROM " + SqliteDatabase.Quote(relation.Table) + " WHERE " + where, args.ToArray()), CultureInfo.InvariantCulture) > 0)
                {
                    return false;
                }
            }
            return true;
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
