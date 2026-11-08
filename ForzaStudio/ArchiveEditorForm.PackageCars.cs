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
                            if (!StfsReader.IsPackage(file))
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

        // Writes the car's files from its package to a zip (kept while the package is unchanged) and opens it.
        private void OpenPackageCar(PackageCar package, string media, string root)
        {
            string zip;
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                string folder = Path.Combine(PackageCarsRoot, Path.GetFileName(package.File));
                zip = Path.Combine(folder, media + ".zip");
                if (!File.Exists(zip) || File.GetLastWriteTimeUtc(zip) < File.GetLastWriteTimeUtc(package.File))
                {
                    this.SetStatus("Reading " + media + " from the installed package " + package.Title + "...");
                    Directory.CreateDirectory(folder);
                    DlcSource source = DlcSource.OpenPackage(package.File, true);
                    zip = (source == null) ? null : source.ExtractCar(media, folder);
                    if (zip == null)
                    {
                        MessageBox.Show(this, media + " was not found in the package " + package.Title + ".", "Open Car Model", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                }
                packageCarRoots[Path.GetFullPath(zip)] = root;
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
            this.NavigateFrom(delegate
            {
                this.OpenArchive(opened);
                this.SetStatus(string.Format("{0}: a copy read from the installed package {1} ({2}). Edits stay in the copy, the package is not written.",
                    media, package.Title, Path.GetFileName(package.File)));
            });
        }
    }
}
