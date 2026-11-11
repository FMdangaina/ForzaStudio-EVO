namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Windows.Forms;

    // The editor's own list of cars to take something from (user 2026-11-11: Copy Part From Another Car shows the cars
    // the program knows, not a file dialog). First the source is chosen (user 2026-11-11: "choose the game first, don't
    // load everything"): the other cars of the pack zip being edited, one game, or another file. Only then are that
    // game's cars read: GAME:\Media\cars\*.zip of its folders (the open car's game, Tools > Settings > Folders, the FM4
    // folder of the port window) and the cars of its packages installed in Xenia (disc 2, DLC; read in place, a car is
    // written to a cached copy under PackageCarsRoot when picked). FM2 is left out: CarbinModel does not read its carbin.
    public partial class ArchiveEditorForm
    {
        private enum CarSourceKind
        {
            OtherFile,
            ThisPack,
            Game,        // step 1 only: a game whose cars are listed next
            GameZip,
            Package
        }

        private sealed class CarSource
        {
            public CarSourceKind Kind;
            public ForzaGame GameId;
            public string Media = "";
            public string Name = "";
            public string Game = "";
            public string Where = "";
            public string Path;          // GameZip: the car zip; Package: the package file
            public string PackageTitle;

            public override string ToString()
            {
                return this.Media;
            }
        }

        private sealed class PackageCarEntry
        {
            public string File;
            public string Title;
            public string Car;
        }

        // Xenia content folder -> the cars of its packages (mod packs left out), read once per session.
        private static readonly Dictionary<string, List<PackageCarEntry>> contentFolderCars = new Dictionary<string, List<PackageCarEntry>>(StringComparer.OrdinalIgnoreCase);

        private static readonly ForzaGame[] CarSourceGames = { ForzaGame.Motorsport4, ForzaGame.Motorsport3, ForzaGame.Horizon1, ForzaGame.Horizon2 };

        // The cars of every package of a game in its Xenia content folder.
        private static List<PackageCarEntry> ContentFolderCars(string folder, uint title)
        {
            List<PackageCarEntry> list;
            if (contentFolderCars.TryGetValue(folder, out list))
            {
                return list;
            }
            list = new List<PackageCarEntry>();
            try
            {
                foreach (string file in Directory.GetFiles(folder))
                {
                    try
                    {
                        if (!StfsReader.IsPackage(file) || ModPack.IsModPack(file))
                        {
                            continue;
                        }
                        DlcSource source = DlcSource.OpenPackage(file, true);
                        if (source == null || source.TitleId != title)
                        {
                            continue;
                        }
                        string packTitle = string.IsNullOrEmpty(source.Title) ? System.IO.Path.GetFileName(file) : source.Title;
                        foreach (string car in source.CarFolders())
                        {
                            PackageCarEntry one = new PackageCarEntry();
                            one.File = file;
                            one.Title = packTitle;
                            one.Car = car;
                            list.Add(one);
                        }
                    }
                    catch (Exception)
                    {
                        // a package that can't be read holds no car for the list
                    }
                }
            }
            catch (Exception)
            {
            }
            contentFolderCars[folder] = list;
            return list;
        }

        // The game folders of a game the editor knows (Settings / port window, and the open car's game), no doubles.
        private List<string> CarSourceRoots(ForzaGame game)
        {
            List<string> roots = new List<string>();
            List<string> folders = new List<string>();
            string configured = GameInfo.ConfiguredFolder(game);
            if (configured != null)
            {
                folders.Add(configured);
            }
            string own = this.GameRoot;
            if (own != null && GameInfo.Detect(own) == game)
            {
                folders.Add(own);
            }
            foreach (string root in folders)
            {
                string full = SafeFullPath(root).TrimEnd('\\');
                if (!roots.Exists(delegate(string r) { return r.Equals(full, StringComparison.OrdinalIgnoreCase); }))
                {
                    roots.Add(full);
                }
            }
            return roots;
        }

        // The cars of one game: its folders' Media\cars zips and its installed packages.
        private List<CarSource> CarSourcesOfGame(ForzaGame game)
        {
            List<CarSource> sources = new List<CarSource>();
            Dictionary<string, string> fm4Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (game == ForzaGame.Motorsport4)
            {
                foreach (Fm4CarList.Entry entry in Fm4CarList.Cars)
                {
                    fm4Names[entry.MediaName] = entry.Name;
                }
            }
            string open = (this.zip == null || this.zip.FilePath == null) ? null : SafeFullPath(this.zip.FilePath);
            string name;
            foreach (string root in this.CarSourceRoots(game))
            {
                string cars = System.IO.Path.Combine(System.IO.Path.Combine(root, "Media"), "cars");
                if (!Directory.Exists(cars))
                {
                    continue;
                }
                foreach (string path in Directory.GetFiles(cars, "*.zip"))
                {
                    string media = System.IO.Path.GetFileNameWithoutExtension(path);
                    if (media.Equals("Shared", StringComparison.OrdinalIgnoreCase) || (open != null && SafeFullPath(path).Equals(open, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                    CarSource one = new CarSource();
                    one.Kind = CarSourceKind.GameZip;
                    one.GameId = game;
                    one.Media = media;
                    one.Game = GameInfo.Tag(game);
                    one.Where = root;
                    one.Path = path;
                    one.Name = fm4Names.TryGetValue(media, out name) ? name : "";
                    sources.Add(one);
                }
            }

            string content = InstalledDlc.ContentFolder(game);
            if (content != null)
            {
                this.SetStatus("Reading the cars of the " + GameInfo.Tag(game) + " packages installed in Xenia...");
                foreach (PackageCarEntry entry in ContentFolderCars(content, InstalledDlc.TitleId(game)))
                {
                    CarSource one = new CarSource();
                    one.Kind = CarSourceKind.Package;
                    one.GameId = game;
                    one.Media = entry.Car;
                    one.Game = GameInfo.Tag(game);
                    one.Where = entry.Title + " (" + System.IO.Path.GetFileName(entry.File) + ")";
                    one.Path = entry.File;
                    one.PackageTitle = entry.Title;
                    one.Name = fm4Names.TryGetValue(entry.Car, out name) ? name : "";
                    sources.Add(one);
                }
            }
            sources.Sort(delegate(CarSource a, CarSource b) { return StringComparer.OrdinalIgnoreCase.Compare(a.Media, b.Media); });
            return sources;
        }

        // Step 1: this pack, one game, or another file. Nothing is read here but the folder names.
        private CarSource PickCarSourceGroup(string title, List<string> packCars)
        {
            List<object[]> rows = new List<object[]>();
            if (packCars.Count > 0)
            {
                CarSource pack = new CarSource();
                pack.Kind = CarSourceKind.ThisPack;
                pack.Media = "This pack";
                pack.Where = packCars.Count + " other car(s) in the zip being edited";
                rows.Add(new object[] { pack, pack.Where });
            }
            foreach (ForzaGame game in CarSourceGames)
            {
                List<string> where = this.CarSourceRoots(game);
                if (InstalledDlc.ContentFolder(game) != null)
                {
                    where.Add("Xenia packages");
                }
                if (where.Count == 0)
                {
                    continue;
                }
                CarSource one = new CarSource();
                one.Kind = CarSourceKind.Game;
                one.GameId = game;
                one.Media = GameInfo.Name(game);
                one.Where = string.Join(" + ", where.ToArray());
                rows.Add(new object[] { one, one.Where });
            }
            CarSource other = new CarSource();
            other.Kind = CarSourceKind.OtherFile;
            other.Media = "(another file...)";
            other.Where = "car zip, pack zip, DLC package or _lod0.carbin";
            rows.Add(new object[] { other, other.Where });
            using (PickDialog dialog = new PickDialog(title + ": where from?", new string[] { "Source", "Where" }, new int[] { 200, 520 }, rows, null))
            {
                dialog.AcceptLabel = delegate(int n) { return "Next"; };
                return (dialog.ShowDialog(this) == DialogResult.OK) ? (CarSource) dialog.SelectedKey : null;
            }
        }

        // The editor's car list; null = cancelled. Kind OtherFile = the caller opens a file dialog.
        private CarSource PickKnownCar(string title, List<string> packCars)
        {
            CarSource group = this.PickCarSourceGroup(title, packCars);
            if (group == null || group.Kind == CarSourceKind.OtherFile)
            {
                return group;
            }
            List<CarSource> sources = new List<CarSource>();
            if (group.Kind == CarSourceKind.ThisPack)
            {
                packCars.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string car in packCars)
                {
                    CarSource one = new CarSource();
                    one.Kind = CarSourceKind.ThisPack;
                    one.Media = car;
                    one.Where = "this pack";
                    sources.Add(one);
                }
            }
            else
            {
                try
                {
                    Cursor.Current = Cursors.WaitCursor;
                    sources = this.CarSourcesOfGame(group.GameId);
                }
                finally
                {
                    Cursor.Current = Cursors.Default;
                }
                this.SetStatus(string.Format("{0}: {1} cars.", group.Media, sources.Count));
            }
            List<object[]> rows = new List<object[]>();
            foreach (CarSource source in sources)
            {
                rows.Add(new object[] { source, source.Name, source.Where });
            }
            using (PickDialog dialog = new PickDialog(title + ": " + group.Media, new string[] { "Car", "Name", "Where" }, new int[] { 260, 240, 380 }, rows, null))
            {
                dialog.AcceptLabel = delegate(int n) { return "Show its parts"; };
                return (dialog.ShowDialog(this) == DialogResult.OK) ? (CarSource) dialog.SelectedKey : null;
            }
        }
    }
}
