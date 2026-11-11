namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Windows.Forms;

    // Cars of the game's database whose files are not in the game folder but in a package installed for the game
    // (Xenia content\...\<title id>\00000002): FM4's disc 2 (the four packages of offers 1-4) and FM3's second-disc
    // packs, whose rows are in the disc 1 gamedb.slt, and the DLC packs. The packages are read in the background once
    // per game folder; Open Car Model writes the car's own files to a stored zip under %TEMP%\ForzaStudio\PackageCars
    // and opens it. The packages are never written: edits of that zip stay in the copy.
    public partial class ArchiveEditorForm
    {
        private class PackageCar
        {
            public string File;     // the package file
            public string Title;    // its display name
        }

        // Game folder -> media name -> package; a game folder being read maps to null.
        private static readonly Dictionary<string, Dictionary<string, PackageCar>> packageCars = new Dictionary<string, Dictionary<string, PackageCar>>(StringComparer.OrdinalIgnoreCase);
        // A car zip written from a package -> the game folder it belongs to (GameRoot while it is open).
        private static readonly Dictionary<string, string> packageCarRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static string PackageCarsRoot
        {
            get { return Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "PackageCars"); }
        }

        // The game folder of a car zip written from a package, or null.
        private static string PackageCarRoot(string zipPath)
        {
            if (string.IsNullOrEmpty(zipPath))
            {
                return null;
            }
            string root;
            try
            {
                return packageCarRoots.TryGetValue(Path.GetFullPath(zipPath), out root) ? root : null;
            }
            catch (ArgumentException)
            {
                return null;   // a mounted path (stfs:...)
            }
        }

        // The installed package that holds the car, or null. scanning: the packages of this game folder are still being
        // read (the car header is shown again when they are).
        private PackageCar PackageOfCar(string root, string media, out bool scanning)
        {
            scanning = false;
            if (root == null || string.IsNullOrEmpty(media))
            {
                return null;
            }
            Dictionary<string, PackageCar> cars;
            if (packageCars.TryGetValue(root, out cars))
            {
                scanning = cars == null;
                PackageCar found;
                return (cars != null && cars.TryGetValue(media, out found)) ? found : null;
            }
            ForzaGame game = GameInfo.Detect(root);
            string folder = (game == ForzaGame.Unknown) ? null : InstalledDlc.ContentFolder(game);
            if (folder == null)
            {
                packageCars[root] = new Dictionary<string, PackageCar>(StringComparer.OrdinalIgnoreCase);
                return null;
            }
            packageCars[root] = null;
            scanning = true;
            uint title = InstalledDlc.TitleId(game);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                Dictionary<string, PackageCar> list = new Dictionary<string, PackageCar>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (string file in Directory.GetFiles(folder))
                    {
                        try
                        {
                            if (!StfsReader.IsPackage(file) || ModPack.IsModPack(file))   // a mod pack replaces a car, it is not where the car comes from
                            {
                                continue;
                            }
                            DlcSource source = DlcSource.OpenPackage(file, true);
                            if (source == null || source.TitleId != title)
                            {
                                continue;
                            }
                            foreach (string car in source.CarFolders())
                            {
                                if (!list.ContainsKey(car))
                                {
                                    PackageCar one = new PackageCar();
                                    one.File = file;
                                    one.Title = string.IsNullOrEmpty(source.Title) ? Path.GetFileName(file) : source.Title;
                                    list[car] = one;
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // a package that can't be read holds no car for the view
                        }
                    }
                }
                catch (Exception)
                {
                }
                this.PostToForm(delegate
                {
                    packageCars[root] = list;
                    if (this.gameDb != null && this.dbCarId >= 0 && string.Equals(this.GameRoot, root, StringComparison.OrdinalIgnoreCase))
                    {
                        this.UpdateCarHeader(this.dbCarId);
                    }
                });
            });
            return null;
        }

        // The base game's gamedb.slt shown for a package without a database of its own (BaseDatabase), or null.
        private ForzaZip.Entry baseDbEntry;
        private const string BaseDbName = "gamedb.slt (base game)";

        // The cars of a pack zip (media/cars/<name>/ with a .carbin).
        private static List<string> PackZipCars(ForzaZip zip)
        {
            List<string> cars = new List<string>();
            foreach (ForzaZip.Entry entry in zip.Entries)
            {
                string[] part = entry.Name.Replace('\\', '/').Split('/');
                if (part.Length == 4 && part[0].Equals("media", StringComparison.OrdinalIgnoreCase) && part[1].Equals("cars", StringComparison.OrdinalIgnoreCase)
                    && part[3].EndsWith(".carbin", StringComparison.OrdinalIgnoreCase) && !cars.Exists(delegate(string c) { return c.Equals(part[2], StringComparison.OrdinalIgnoreCase); }))
                {
                    cars.Add(part[2]);
                }
            }
            return cars;
        }

        private static bool HasMergeDb(ForzaZip zip)
        {
            foreach (ForzaZip.Entry entry in zip.Entries)
            {
                string name = (entry.FullPath ?? entry.Name).Replace('\\', '/');
                if (name.IndexOf("db/patch/", StringComparison.OrdinalIgnoreCase) >= 0 && name.EndsWith("_merge.slt", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        // The zip of the package's work folder that holds the cars (Media\DLCZips\<offer>_pri_<n>.zip), or null.
        private string PackCarsZip()
        {
            string found = null;
            long size = -1;
            foreach (ForzaZip.Entry entry in this.zip.Entries)
            {
                string name = entry.Name.Replace('\\', '/');
                if (entry.FullPath == null || !IsArchive(name) || name.IndexOf("DLCZips/", StringComparison.OrdinalIgnoreCase) < 0
                    || Path.GetFileName(name).StartsWith("StringTables", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                long length = new FileInfo(entry.FullPath).Length;
                if (length > size)
                {
                    found = entry.FullPath;
                    size = length;
                }
            }
            return found;
        }

        // File > Base Game Database: a package with cars but no database of its own (FM4's disc 2, offers 1-4; FM3's
        // second-disc packs) has its rows in the base game's gamedb.slt (disc 1: the FM4 folder of the port window,
        // Tools > Settings > Folders for the others). It is shown read-only over the pack's zip, so Open Car Model opens
        // the pack's own car (edits go into the package on Save); the pack's cars are marked like DLC cars.
        private void ShowBaseDatabase()
        {
            if (this.package == null || this.zip == null || !this.InPackage(this.zip.FilePath))
            {
                MessageBox.Show(this, "Open a DLC package first (File > Open DLC Package).", "Base Game Database", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (this.zip.IsFolder)
            {
                string cars = this.PackCarsZip();
                if (cars == null)
                {
                    MessageBox.Show(this, "The package has no zip in Media\\DLCZips.", "Base Game Database", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                this.NavigateFrom(delegate { this.OpenArchive(cars); });
                if (this.zip == null || this.zip.IsFolder)
                {
                    return;   // not opened (unsaved changes kept)
                }
            }
            if (HasMergeDb(this.zip))
            {
                MessageBox.Show(this, "This pack has its own database (media\\db\\patch\\*_merge.slt): open that one.", "Base Game Database", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string root = this.GameRoot;
            string db = (root == null) ? null : Path.Combine(Path.Combine(Path.Combine(root, "Media"), "db"), "gamedb.slt");
            if (db == null || !File.Exists(db))
            {
                ForzaGame game = GameInfo.FromTitleId(this.package.TitleId);
                MessageBox.Show(this, "The base game's gamedb.slt was not found. Set the " + GameInfo.Name(game) + " folder (disc 1): "
                    + ((game == ForzaGame.Motorsport4) ? "the FM4 folder of the port window (Tools > Port Car)." : "Tools > Settings > Folders."),
                    "Base Game Database", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                if (this.baseDbEntry == null || !string.Equals(this.baseDbEntry.FullPath, db, StringComparison.OrdinalIgnoreCase) || this.gameDbEntry != this.baseDbEntry)
                {
                    ForzaZip.Entry entry = new ForzaZip.Entry();
                    entry.Name = BaseDbName;
                    entry.FullPath = db;
                    this.baseDbEntry = entry;
                    this.ShowGameDb(entry, File.ReadAllBytes(db));
                    this.gameDb.ReadOnly = true;
                    this.gameDb.ReadOnlyReason = "This is the base game's database (" + db + "), shown read-only for the package's cars: open it from the game folder to edit it.";
                    List<string> cars = PackZipCars(this.zip);
                    string pack = (this.package.DisplayName.Length > 0) ? this.package.DisplayName : Path.GetFileName(this.package.Path);
                    foreach (object[] row in this.gameDb.Sql.Query("SELECT Id, MediaName FROM Data_Car").Rows)
                    {
                        string media = row[1] as string;
                        if (row[0] is long && media != null && cars.Exists(delegate(string c) { return c.Equals(media, StringComparison.OrdinalIgnoreCase); }))
                        {
                            this.gameDb.DlcCars[(long) row[0]] = pack;
                        }
                    }
                    this.lblDbReference.Text = "Base game database (read-only)";
                    this.lblDbReference.ToolTipText = db;
                    this.SelectDbSection(this.dbSection);
                    this.SetStatus(string.Format("{0}: {1} cars of this pack in the base game's database {2} (read-only). Open Car Model opens the pack's own car; Save writes the package.",
                        pack, this.gameDb.DlcCars.Count, db));
                }
                else
                {
                    this.ShowView(this.pnlGameDb);
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Base Game Database", MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        // The car's files read from a package to a stored zip under PackageCarsRoot, kept while the package is unchanged;
        // null when the package does not hold the car. source: the package already opened, or null.
        private static string PackageCarZip(DlcSource source, string packageFile, string media)
        {
            string folder = Path.Combine(PackageCarsRoot, Path.GetFileName(packageFile));
            string zip = Path.Combine(folder, media + ".zip");
            if (File.Exists(zip) && File.GetLastWriteTimeUtc(zip) >= File.GetLastWriteTimeUtc(packageFile))
            {
                return zip;
            }
            Directory.CreateDirectory(folder);
            if (source == null)
            {
                source = DlcSource.OpenPackage(packageFile, true);
            }
            return (source == null) ? null : source.ExtractCar(media, folder);
        }

        // A car copy whose Save writes a mod pack (ModPack): its package is an original one.
        private class ModCar
        {
            public string Content;      // the Xenia content folder the original package is in
            public string Media;
            public string PackageFile;  // null: a car of the game itself (DVD1)
            public string PackageTitle;
            public string Root;         // the game folder (free offer numbers)
            public bool Pending;        // the copy was saved but the mod pack not written yet (Xenia open, error)
        }

        private static readonly Dictionary<string, ModCar> modCars = new Dictionary<string, ModCar>(StringComparer.OrdinalIgnoreCase);

        // True for a package the official DLC list knows (an original, never written: its cars are changed through mod
        // packs). Only FM4's for now: the mod pack's folder name and header are FM4's.
        private static bool IsOriginalPackage(string file)
        {
            DlcCatalog.Pack pack = DlcCatalog.Find(DlcCatalog.ReadHeader(file));
            return pack != null && pack.Game == ForzaGame.Motorsport4;
        }

        private static ModCar ModCarOf(string zipPath)
        {
            ModCar car;
            try
            {
                return (zipPath != null && modCars.TryGetValue(Path.GetFullPath(zipPath), out car)) ? car : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        // After a save of a car copy of an original package: writes its mod pack (Xenia closed first).
        private bool WriteModPack(ModCar car)
        {
            if (CarPortForm.XeniaRunning())
            {
                car.Pending = true;
                MessageBox.Show(this, "The copy of " + car.Media + " is saved, but Xenia is open and keeps the packages in use.\n\nClose Xenia, then Save again: the mod pack is written then.",
                    "Mod Pack", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.SetStatus(car.Media + ": copy saved, mod pack NOT written yet (close Xenia, then Save again).");
                return false;
            }
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                this.SetStatus("Writing the mod pack of " + car.Media + "...");
                // a car of an original DLC: standalone (wheels, offer mapping, names), for players without that DLC
                ModPack.Standalone standalone = (car.PackageFile == null) ? null : ModPack.StandaloneFrom(car.PackageFile, car.Media, car.Root);
                // the car's database copy (File > Database of This Car): every row of the car goes into the pack's merge
                ModPack.CarDatabase database = null;
                ForzaZip.Entry dbEntry = this.zip.Find(ModCarDb.EntryName);
                if (dbEntry != null)
                {
                    database = new ModPack.CarDatabase();
                    database.Working = this.zip.Read(dbEntry);
                    database.Reference = ModCarDb.Reference(car.Root, car.PackageFile);
                }
                string folder = ModPack.Write(car.Content, car.Root, car.Media, this.zip, standalone, car.PackageTitle, car.PackageFile, CarPortForm.PackThumbnail(this.Icon), database);
                car.Pending = false;
                this.SetStatus(string.Format("{0}: mod pack written in Xenia ({1}, priority {2}{3}{5}). {4} is not touched; delete the mod pack folder to get the original car back.",
                    car.Media, Path.GetFileName(folder), ModPack.Priority, (standalone != null) ? ", standalone: works without " + car.PackageTitle : "", car.PackageTitle,
                    (database != null) ? ", with the car's database rows" : ""));
                if (database != null && database.Report.ToString().IndexOf("WARNING", StringComparison.Ordinal) >= 0)
                {
                    MessageBox.Show(this, "The mod pack is written. Some database changes cannot go into a mod pack:\n\n" + database.Report.ToString().Trim(), "Mod Pack", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return true;
            }
            catch (Exception exception)
            {
                car.Pending = true;
                MessageBox.Show(this, "The copy is saved but the mod pack was not written:\n\n" + exception.Message, "Mod Pack", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return false;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        // File > Database of This Car (user 2026-11-11): the database of the car copy open (a game or original DLC car whose
        // Save writes a mod pack). A copy of the game's database (+ the original package's merge + the car's mod pack merge)
        // kept in the car copy zip as ModCarDb.EntryName; edits are saved with the car, and Save puts every row of the car
        // in the mod pack's merge. The game's gamedb.slt is never written.
        private void ShowModCarDatabase()
        {
            ModCar mod = (this.zip == null) ? null : ModCarOf(this.zip.FilePath);
            if (mod == null)
            {
                MessageBox.Show(this, "Open a car of the game or of an original DLC first (Open Car Model from the game's database): its Save writes a mod pack, and this is its database.",
                    "Database of This Car", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ForzaZip.Entry entry = this.zip.Find(ModCarDb.EntryName);
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                if (entry == null)
                {
                    this.SetStatus("Building the database of " + mod.Media + " (game database + its packages)...");
                    byte[] reference = ModCarDb.Reference(mod.Root, mod.PackageFile);
                    string modFile = ModPack.Find(mod.Content, mod.Media);
                    byte[] working = ModCarDb.Working(reference, (modFile == null) ? null : ModPack.ReadMerge(modFile));
                    entry = this.zip.Add(ModCarDb.EntryName, working);
                    this.currentEntry = null;
                    this.PopulateTree();
                    this.UpdateTitle();
                }
                ForzaZip.Entry open = entry;
                this.NavigateFrom(delegate
                {
                    this.SelectEntry(open.Name);   // the tree shows it (the database view)
                    if (this.gameDbEntry != open)
                    {
                        this.currentEntry = open;
                        this.ShowGameDb(open, this.zip.Read(open));
                    }
                    object id = (this.gameDb == null) ? null : this.gameDb.Sql.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", mod.Media);
                    if (id is long)
                    {
                        this.SelectDbItem(DbSection.Cars, (long) id);
                    }
                    this.SetStatus(mod.Media + ": the car's database (a copy). Edit it here; File > Save writes the car AND its database rows into the mod pack. The game's gamedb.slt is never written.");
                });
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Database of This Car", MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        // ForzaStudio.ini [ModPack] GameCars (default on): Open Car Model of an FM4 game car (DVD1 Media\cars\<car>.zip) opens a
        // copy and Save writes a mod pack; off = the game's zip is opened and saved in place as before.
        private static bool GameCarsAsModPacks
        {
            get { return AppSettings.Get("ModPack.GameCars", "1") != "0"; }
        }

        // An FM4 game car (DVD1) opened as a copy whose Save writes its mod pack. False when it can't (no Xenia content
        // folder set): the caller opens the game's zip as before.
        private bool OpenGameCarAsMod(string carZip, string media, string root)
        {
            string content = InstalledDlc.ContentFolder(ForzaGame.Motorsport4);
            if (content == null || GameInfo.Detect(root) != ForzaGame.Motorsport4)
            {
                return false;
            }
            string copy = Path.Combine(Path.Combine(PackageCarsRoot, "Game"), media + ".zip");
            string zip;
            string modFolder;
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                modFolder = ModPack.Find(content, media);
                string modZip = modFolder;   // the mod pack file (its time = when it was written)
                ModCar known = ModCarOf(copy);
                Directory.CreateDirectory(Path.GetDirectoryName(copy));
                if (modZip != null && !(known != null && known.Pending) && (!File.Exists(copy) || File.GetLastWriteTimeUtc(copy) < File.GetLastWriteTimeUtc(modZip)))
                {
                    zip = ModPack.ExtractCar(modFolder, media, copy);
                }
                else if (modZip == null && !(known != null && known.Pending))
                {
                    File.Copy(carZip, copy, true);   // a fresh copy of the game's car (an old copy of a removed mod pack goes)
                    File.SetLastWriteTimeUtc(copy, DateTime.UtcNow);
                    zip = copy;
                }
                else
                {
                    zip = copy;
                }
                if (zip == null)
                {
                    return false;
                }
                ModCar mod = new ModCar();
                mod.Content = content;
                mod.Media = media;
                mod.PackageTitle = "the game's own car (" + Path.GetFileName(carZip) + ")";
                mod.Root = root;
                mod.Pending = known != null && known.Pending;
                modCars[Path.GetFullPath(zip)] = mod;
                packageCarRoots[Path.GetFullPath(zip)] = root;
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Open Car Model", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return true;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            string opened = zip;
            string from = (modFolder != null) ? "its mod pack " + Path.GetFileName(modFolder) : Path.GetFileName(carZip);
            this.NavigateFrom(delegate
            {
                this.OpenArchive(opened);
                this.SetStatus(string.Format("{0}: a copy of {1}. Save writes a mod pack in Xenia (priority {2}); the game's files are never written. Its database rows: File > Database of This Car.",
                    media, from, ModPack.Priority));
            });
            return true;
        }

        // Writes the car's files from its package to a zip (kept while the package is unchanged) and opens it. A car of an
        // original package opens its mod pack's version when there is one, and Save writes the mod pack.
        private void OpenPackageCar(PackageCar package, string media, string root)
        {
            string zip;
            ModCar mod = null;
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                this.SetStatus("Reading " + media + " from the installed package " + package.Title + "...");
                if (IsOriginalPackage(package.File))
                {
                    mod = new ModCar();
                    // Xenia's FM4 content folder (Settings), else the folder the package is in
                    mod.Content = InstalledDlc.ContentFolder(ForzaGame.Motorsport4) ?? Path.GetDirectoryName(Path.GetFullPath(package.File));
                    mod.Media = media;
                    mod.PackageFile = package.File;
                    mod.PackageTitle = package.Title;
                    mod.Root = root;
                }
                string modFolder = (mod == null) ? null : ModPack.Find(mod.Content, media);
                string modZip = modFolder;   // the mod pack file (its time = when it was written)
                string copy = Path.Combine(Path.Combine(PackageCarsRoot, Path.GetFileName(package.File)), media + ".zip");
                ModCar known = ModCarOf(copy);
                if (modZip != null && !(known != null && known.Pending) && (!File.Exists(copy) || File.GetLastWriteTimeUtc(copy) < File.GetLastWriteTimeUtc(modZip)))
                {
                    // the mod pack's version (a copy saved later, whose mod pack is still pending, is kept)
                    Directory.CreateDirectory(Path.GetDirectoryName(copy));
                    zip = ModPack.ExtractCar(modFolder, media, copy);
                }
                else
                {
                    zip = PackageCarZip(null, package.File, media);
                }
                if (zip == null)
                {
                    MessageBox.Show(this, media + " was not found in the package " + package.Title + ".", "Open Car Model", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                packageCarRoots[Path.GetFullPath(zip)] = root;
                if (mod != null)
                {
                    mod.Pending = known != null && known.Pending;
                    modCars[Path.GetFullPath(zip)] = mod;
                }
                if (mod != null && modFolder != null)
                {
                    package = new PackageCar { File = package.File, Title = package.Title + ", with its mod pack " + Path.GetFileName(modFolder) };
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Open Car Model", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            string opened = zip;
            bool original = mod != null;
            this.NavigateFrom(delegate
            {
                this.OpenArchive(opened);
                this.SetStatus(original
                    ? string.Format("{0}: a copy read from the original package {1} ({2}). Save writes a mod pack in Xenia (priority {3}); the original package is never written. Its database rows: File > Database of This Car.",
                        media, package.Title, Path.GetFileName(package.File), ModPack.Priority)
                    : string.Format("{0}: a copy read from the installed package {1} ({2}). Edits stay in the copy, the package is not written.",
                        media, package.Title, Path.GetFileName(package.File)));
            });
        }
    }
}
