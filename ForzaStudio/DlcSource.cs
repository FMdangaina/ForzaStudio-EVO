namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Xml;

    // A car DLC package of another Forza (Forza Horizon, later Forza Motorsport 3) as the source of a
    // port, on its own: the base source game is never needed (ROADMAP topic 3, decided 2026-10-17).
    //
    // An official FH1 pack (the extracted folder of the STFS package, 20 checked 2026-10-17):
    //   Media\<offer>.puboffer
    //   Media\DLCZips\zipmount.xml              the zips and folders to mount and where
    //   Media\DLCZips\<offer>_pri_65.zip        the whole Media tree (LZX): media\cars\<car>\,
    //                                           media\wheels\<car>\, media\db\patch\<n>_merge.slt,
    //                                           UI\Textures thumbnails, audio
    //   Media\DLCZips\<offer>_pri_65\Media\...  loose files (sound banks), mounted at the game root
    //   Media\DLCZips\StringTables_pri_<P>.zip  <LANG>/<Table>.str
    //   Media\DLCZips\cameras_pri_<P>.zip, EngineTuning_pri_<P>\, HarmonicTuning_pri_<P>\ ...
    //                                           mounted where zipmount.xml says
    // A zip or folder that zipmount.xml does not list is mounted at the game root (GAME:\), like the
    // _pri_65 ones; a higher priority (<name>_pri_<N>) goes on top.
    //
    // Stage() lays the package out as a source game folder that CarPort reads as it reads a game:
    //   Media\db\gamedb.slt            the package's merge database (only the pack's rows)
    //   Media\cars\<car>.zip           Media\wheels\<wheel>.zip     (stored)
    //   Media\StringTables\<LANG>.zip  <Table>.str
    //   Media\audio\cars\CarModelTuning.zip, Engines\EngineTuning.zip, Engines\HarmonicTuning.zip
    //   Media\audio\cars\Engines\Soundbanks\LOD1\*.fsb, Media\UI\Textures\Thumbnails\*.xds (loose)
    //   DlcSource.txt                  title id, offer, package: GameInfo.Detect reads the game from it
    // An FM3 pack (Hyundai Car Pack, checked 2026-10-18) is the same but has no .puboffer (the offer is in
    // DLCZips\0016_pri_65.zip's name), no ContentOffers in its merge (Media\LicenseMasks\*.sql unlocks the
    // cars instead) and its digital dashes in media\DigitalGauges\<dash>\ (staged as <dash>.zip).
    // What the pack lacks (shared engine sounds, engine swaps only in the base game) the port takes from
    // FM4 or asks in the review.
    // OpenPackage() takes the package file itself (STFS, StfsReader) and extracts it first.
    public class DlcSource
    {
        public const string Marker = "DlcSource.txt";

        public string Folder;           // the package folder (it holds Media\<offer>.puboffer)
        public int Offer;
        public uint TitleId;            // the source game's, from the merge database's ContentOffers.ID
        public ForzaGame Game;
        public string Problem;          // why the package can't be used, or null
        public string Title;            // the package's display name (STFS header), when it was read from a package file

        private readonly List<Layer> layers = new List<Layer>();
        // An FM3 package with cars but no merge database (the cars of the game's second disc, which Xenia installs as
        // DLC, e.g. the American Car Pack): the rows are the base game's, the files are the package's.
        private bool baseDatabase;

        private delegate byte[] Reader();

        private class Layer
        {
            public string Path;         // a zip or a folder, relative to the volume ('\')
            public bool IsFolder;
            public string Mount;        // relative to the game root, "" or "Media\audio\..."
            public int Priority;
            public ForzaZip Zip;        // the zip, once opened
        }

        // One file of a layer: its name relative to the layer's mount ('/'), its size and how to read it.
        private class LayerFile
        {
            public string Key;
            public long Size;
            public Reader Reader;

            public byte[] Value()
            {
                return this.Reader();
            }
        }

        // Where the package's files are: a folder (the extracted package) or the package file itself.
        private abstract class Volume
        {
            public abstract List<string> Files();                   // every file, relative to the package root ('\')
            public abstract byte[] Read(string relative);
            public abstract long Size(string relative);             // -1 when there is no such file
            public abstract string ZipPath(string relative);        // a path ForzaZip opens

            // The first level below a folder: names, and whether each is a folder.
            public Dictionary<string, bool> Children(string folder)
            {
                Dictionary<string, bool> found = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                string prefix = folder.TrimEnd('\\') + "\\";
                foreach (string file in this.Files())
                {
                    if (file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        string rest = file.Substring(prefix.Length);
                        int slash = rest.IndexOf('\\');
                        if (slash < 0)
                        {
                            found[rest] = false;
                        }
                        else
                        {
                            found[rest.Substring(0, slash)] = true;
                        }
                    }
                }
                return found;
            }

            public bool HasFolder(string folder)
            {
                string prefix = folder.TrimEnd('\\') + "\\";
                return this.Files().Exists(delegate(string f) { return f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase); });
            }
        }

        private class DiskVolume : Volume
        {
            private readonly string root;
            private List<string> files;

            public DiskVolume(string root)
            {
                this.root = root;
            }

            public override List<string> Files()
            {
                if (this.files == null)
                {
                    string prefix = this.root.TrimEnd('\\') + "\\";
                    this.files = new List<string>();
                    foreach (string file in Directory.GetFiles(this.root, "*", SearchOption.AllDirectories))
                    {
                        this.files.Add(Path.GetFullPath(file).Substring(prefix.Length));
                    }
                }
                return this.files;
            }

            public override byte[] Read(string relative)
            {
                return File.ReadAllBytes(Path.Combine(this.root, relative));
            }

            public override long Size(string relative)
            {
                string path = Path.Combine(this.root, relative);
                return File.Exists(path) ? new FileInfo(path).Length : -1;
            }

            public override string ZipPath(string relative)
            {
                return Path.Combine(this.root, relative);
            }
        }

        private class StfsVolume : Volume
        {
            private readonly StfsReader package;
            private List<string> files;

            public StfsVolume(StfsReader package)
            {
                this.package = package;
            }

            public override List<string> Files()
            {
                if (this.files == null)
                {
                    this.files = this.package.Files();
                }
                return this.files;
            }

            public override byte[] Read(string relative)
            {
                return this.package.ReadFile(relative);
            }

            public override long Size(string relative)
            {
                return this.package.FileSize(relative);
            }

            public override string ZipPath(string relative)
            {
                string path = "stfs:" + this.package.Path + "|" + relative;
                StfsReader reader = this.package;
                string inner = relative;
                ForzaZip.Mount(path, delegate { return reader.OpenFile(inner); });
                return path;
            }
        }

        private Volume volume;
        private string stamp = "";      // a package file: its path, size and time; the stage is rebuilt when they change

        // The package in a folder: the extracted package itself, or its Media folder.
        public static DlcSource Open(string folder)
        {
            return Open(folder, 0);
        }

        // titleId: the package header's, when the package file was read (0 for a folder).
        private static DlcSource Open(string folder, uint titleId)
        {
            string root = Path.GetFullPath(folder).TrimEnd('\\');
            if (string.Equals(Path.GetFileName(root), "Media", StringComparison.OrdinalIgnoreCase))
            {
                root = Path.GetDirectoryName(root);
            }
            return Open(new DiskVolume(root), root, titleId, true);
        }

        // full false: the layers only (Materialize), not the title and the database.
        private static DlcSource Open(Volume volume, string folder, uint titleId, bool full)
        {
            int offer = ReadOffer(volume);
            if (offer < 0)
            {
                return null;
            }
            DlcSource source = new DlcSource();
            source.volume = volume;
            source.Folder = folder;
            source.Offer = offer;
            source.TitleId = titleId;
            try
            {
                source.ReadLayers();
                if (full)
                {
                    source.ReadTitle();
                }
            }
            catch (Exception e)
            {
                source.Problem = e.Message;
            }
            return source;
        }

        // An Xbox 360 package file (STFS, as the console stores it), read in place: nothing is extracted (it
        // used to be, 6 GB of %TEMP% for 60 packages). Stage() writes what a port reads under
        // %TEMP%\ForzaStudio\DlcSource\<file name>, kept while the file is unchanged. Problem tells why
        // it can't be used: an FM4 package, an incomplete file, no cars.
        public static DlcSource OpenPackage(string file)
        {
            return OpenPackage(file, false);
        }

        // sameGame: an FM4 package is accepted too (the packages installed for the game being viewed, not a port source).
        public static DlcSource OpenPackage(string file, bool sameGame)
        {
            DlcSource failed = new DlcSource();
            failed.Folder = file;
            try
            {
                StfsReader package = StfsReader.Open(file);
                failed.TitleId = package.TitleId;
                failed.Game = GameInfo.FromTitleId(package.TitleId);
                failed.Title = package.DisplayName;
                if (failed.Game == ForzaGame.Motorsport4 && !sameGame)
                {
                    failed.Problem = "a Forza Motorsport 4 package (" + package.DisplayName + "): a port takes cars from FH, FH2 or FM3 packages";
                    return failed;
                }
                FileInfo info = new FileInfo(file);
                string stamp = info.FullName + "|" + info.Length.ToString(CultureInfo.InvariantCulture) + "|" + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
                DlcSource source = Open(new StfsVolume(package), file, package.TitleId, true);
                if (source == null)
                {
                    failed.Problem = "not a car pack (no Media\\DLCZips): " + package.DisplayName;
                    return failed;
                }
                source.stamp = stamp;
                source.Title = package.DisplayName;
                if (source.TitleId == 0)
                {
                    source.TitleId = package.TitleId;
                    source.Game = failed.Game;
                }
                return source;
            }
            catch (Exception e)
            {
                failed.Problem = e.Message;
                return failed;
            }
        }

        // The offer of a package's Media folder: FH1 names a <offer>.puboffer file; FM3 has none, its offer
        // is in the media layer's name (DLCZips\0016_pri_65.zip). -1 when the folder is not a package.
        private static int ReadOffer(Volume volume)
        {
            int offer;
            List<string> offers = new List<string>();
            foreach (KeyValuePair<string, bool> child in volume.Children("Media"))
            {
                if (!child.Value && child.Key.EndsWith(".puboffer", StringComparison.OrdinalIgnoreCase))
                {
                    offers.Add(child.Key);
                }
            }
            if (offers.Count == 1)
            {
                return int.TryParse(Path.GetFileNameWithoutExtension(offers[0]), NumberStyles.None, CultureInfo.InvariantCulture, out offer) ? offer : -1;
            }
            Dictionary<string, bool> zips = volume.Children("Media\\DLCZips");
            if (offers.Count == 0 && zips.Count > 0)
            {
                List<string> layers = new List<string>();
                foreach (KeyValuePair<string, bool> child in zips)
                {
                    if (child.Value ? child.Key.EndsWith("_pri_65", StringComparison.OrdinalIgnoreCase) : child.Key.EndsWith("_pri_65.zip", StringComparison.OrdinalIgnoreCase))
                    {
                        layers.Add(child.Key);
                    }
                }
                foreach (string layer in layers)
                {
                    string name = layer;
                    string number = name.Substring(0, name.IndexOf("_pri_", StringComparison.OrdinalIgnoreCase));
                    // Hex always (0016 = offer 22: the Hyundai pack's merge is 22005_merge.slt and its MultiDiscContentPacks.intID 22; 0010 = 16, VIP; 001B, 000A).
                    if (int.TryParse(number, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out offer))
                    {
                        return offer;
                    }
                }
                // FM2: DLC<n>_pri_<priority> (DLC1_pri_60 = the Nissan pack), the offer is n.
                List<string> fm2 = new List<string>();
                foreach (KeyValuePair<string, bool> child in zips)
                {
                    if (!child.Value && child.Key.StartsWith("DLC", StringComparison.OrdinalIgnoreCase) && child.Key.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && child.Key.IndexOf("_pri_", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        fm2.Add(child.Key);
                    }
                }
                foreach (string layer in fm2)
                {
                    string name = Path.GetFileNameWithoutExtension(layer);
                    int end = name.IndexOf('_');
                    if (end > 3 && int.TryParse(name.Substring(3, end - 3), NumberStyles.None, CultureInfo.InvariantCulture, out offer))
                    {
                        return offer;
                    }
                }
            }
            return -1;
        }

        // The car folders an FM2 package holds (media/cars/<NAME>/ with a .carbin), by layer.
        private List<string> Fm2CarFolders()
        {
            List<string> cars = new List<string>();
            foreach (Layer layer in this.layers)
            {
                string prefix = (layer.Mount.Length > 0) ? layer.Mount.Replace('\\', '/') + "/" : string.Empty;
                foreach (LayerFile file in this.Files(layer))
                {
                    string name = (prefix + file.Key).Replace('\\', '/');
                    string[] part = name.Split('/');
                    if (part.Length == 4 && part[0].Equals("media", StringComparison.OrdinalIgnoreCase) && part[1].Equals("cars", StringComparison.OrdinalIgnoreCase)
                        && part[3].EndsWith(".carbin", StringComparison.OrdinalIgnoreCase) && cars.FindIndex(delegate(string c) { return string.Equals(c, part[2], StringComparison.OrdinalIgnoreCase); }) < 0)
                    {
                        cars.Add(part[2]);
                    }
                }
            }
            return cars;
        }

        // The cars whose files the package holds (media\cars\<name>\ with a .carbin), whatever its database:
        // also FM4's disc 2 (offers 1-4) and FM3's second-disc packs, whose rows are the base game's.
        public List<string> CarFolders()
        {
            return this.Fm2CarFolders();
        }

        // Writes one car's files (media\cars\<car>\...) to <folder>\<car>.zip, stored as the game's car zips, and
        // returns it; null when the package does not hold the car. The package itself is only read.
        public string ExtractCar(string car, string folder)
        {
            string prefix = "media/cars/" + car + "/";
            string work = Path.Combine(folder, car);
            if (Directory.Exists(work))
            {
                Directory.Delete(work, true);
            }
            int count = 0;
            foreach (Layer layer in this.layers)
            {
                string mount = (layer.Mount.Length > 0) ? layer.Mount.Replace('\\', '/') + "/" : string.Empty;
                foreach (LayerFile file in this.Files(layer))
                {
                    string name = (mount + file.Key).Replace('\\', '/');
                    if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || name.Length == prefix.Length)
                    {
                        continue;
                    }
                    string path = Path.Combine(work, name.Substring(prefix.Length).Replace('/', '\\'));
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, file.Value());   // a higher priority layer comes later and replaces it
                    count++;
                }
            }
            if (count == 0)
            {
                return null;
            }
            string zip = work + ".zip";
            DlcPackage.WriteStoredZip(work, zip);
            Directory.Delete(work, true);
            return zip;
        }

        // Every package below a folder (a content folder, 00000002, or a folder of extracted packages).
        public static List<DlcSource> Find(string folder)
        {
            List<DlcSource> found = new List<DlcSource>();
            DlcSource self = Open(folder);
            if (self != null)
            {
                found.Add(self);
                return found;
            }
            foreach (string sub in Directory.GetDirectories(folder))
            {
                found.AddRange(Find(sub));
            }
            return found;
        }

        // True for a folder Stage() built.
        public static bool IsStage(string root)
        {
            return root != null && File.Exists(Path.Combine(root, Marker));
        }

        // True for the stage of an FM2 package (cars still in FM2's form): CarPort lays each car out on an FM4 donor
        // (Fm2Source) from it, with the FM2 game as the rest. The FM2 game folder staged by Fm2Source is not one.
        public static bool IsFm2Raw(string root)
        {
            return IsStage(root) && Array.IndexOf(File.ReadAllLines(Path.Combine(root, Marker)), "raw=fm2") >= 0;
        }

        // The title id written in a staged folder, 0 when it is not one.
        public static uint StageTitleId(string root)
        {
            if (!IsStage(root))
            {
                return 0;
            }
            foreach (string line in File.ReadAllLines(Path.Combine(root, Marker)))
            {
                uint id;
                if (line.StartsWith("title=", StringComparison.Ordinal) && uint.TryParse(line.Substring(6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id))
                {
                    return id;
                }
            }
            return 0;
        }

        private const string DlcZips = "Media\\DLCZips";

        private void ReadLayers()
        {
            Dictionary<string, string> mounts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string list = DlcZips + "\\zipmount.xml";
            if (this.volume.Size(list) >= 0)
            {
                XmlDocument xml = new XmlDocument();
                try
                {
                    xml.Load(new MemoryStream(this.volume.Read(list)));
                }
                catch (XmlException)
                {
                    // Empty in the packages cut short (offers 2100 and 2500).
                    throw new InvalidDataException("zipmount.xml is damaged: the package is incomplete.");
                }
                foreach (XmlElement zip in xml.SelectNodes("/zipmount/zip"))
                {
                    string mount = zip.GetAttribute("Mount");
                    mounts[zip.GetAttribute("Name")] = GamePaths.IsGamePath(mount) ? GamePaths.RelativePath(mount).TrimEnd('\\') : string.Empty;
                }
            }
            List<KeyValuePair<string, bool>> paths = new List<KeyValuePair<string, bool>>();
            foreach (KeyValuePair<string, bool> child in this.volume.Children(DlcZips))
            {
                if (child.Value || child.Key.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    paths.Add(child);
                }
            }
            foreach (KeyValuePair<string, bool> child in paths)
            {
                bool folder = child.Value;
                string name = child.Key;
                string stem = folder ? name : Path.GetFileNameWithoutExtension(name);
                int at = stem.LastIndexOf("_pri_", StringComparison.OrdinalIgnoreCase);
                int priority;
                if (at < 0 || !int.TryParse(stem.Substring(at + 5), NumberStyles.None, CultureInfo.InvariantCulture, out priority))
                {
                    continue;
                }
                Layer layer = new Layer();
                layer.Path = DlcZips + "\\" + name;
                layer.IsFolder = folder;
                layer.Priority = priority;
                string mount;
                layer.Mount = mounts.TryGetValue(name, out mount) ? mount : string.Empty;
                if (!folder)
                {
                    // A package cut short (offers 2100 and 2500: 3 and 9.7 MB of a 53 and 48 MB zip)
                    // fails here.
                    try
                    {
                        layer.Zip = new ForzaZip(this.volume.ZipPath(layer.Path));
                    }
                    catch (Exception e)
                    {
                        throw new InvalidDataException(name + " is damaged or incomplete (" + e.Message + ")");
                    }
                }
                this.layers.Add(layer);
            }
            this.layers.Sort(delegate(Layer a, Layer b) { return a.Priority.CompareTo(b.Priority); });
        }

        // The merge database's ContentOffers.ID starts with the title id (FH1 4D5309C9...).
        private void ReadTitle()
        {
            byte[] merge = this.MergeDatabase();
            if (merge == null && (this.TitleId == Fm2Source.TitleId || this.TitleId == 0))
            {
                // FM2: no database in the package, the cars are loose folders (media\cars\<NAME>\); the rows come
                // from the FM2 game set in Tools > Settings (gamedb) merged with the package (Stage).
                if (this.Fm2CarFolders().Count > 0)
                {
                    this.TitleId = Fm2Source.TitleId;
                    this.Game = ForzaGame.Motorsport2;
                    if (GameInfo.ConfiguredFolder(ForzaGame.Motorsport2) == null)
                    {
                        throw new InvalidDataException("An FM2 package needs the Forza Motorsport 2 game for its database: set the folder in Tools > Settings > Folders.");
                    }
                    return;
                }
                if (this.TitleId == Fm2Source.TitleId)
                {
                    throw new InvalidDataException("The package has no cars (a track or other content).");
                }
            }
            if (merge == null && this.TitleId == InstalledDlc.TitleId(ForzaGame.Motorsport3) && this.Fm2CarFolders().Count > 0)
            {
                this.baseDatabase = true;
                this.Game = ForzaGame.Motorsport3;
                if (GameInfo.ConfiguredFolder(ForzaGame.Motorsport3) == null)
                {
                    throw new InvalidDataException("This FM3 package (cars of the second disc) needs the Forza Motorsport 3 game for its database: set the folder in Tools > Settings > Folders.");
                }
                return;
            }
            if (merge == null && this.TitleId == InstalledDlc.TitleId(ForzaGame.Motorsport4) && this.Fm2CarFolders().Count > 0)
            {
                // FM4's own expansions (the cars of offers 1-4): their rows are in the game's database, the pack holds the files.
                this.baseDatabase = true;
                this.Game = ForzaGame.Motorsport4;
                if (GameInfo.ConfiguredFolder(ForzaGame.Motorsport4) == null)
                {
                    throw new InvalidDataException("This FM4 expansion pack has no database of its own: set the Forza Motorsport 4 folder in the port window.");
                }
                return;
            }
            if (merge == null)
            {
                throw new InvalidDataException("The package has no cars (no db\\patch\\*_merge.slt): rims or other content only.");
            }
            using (SqliteDatabase db = SqliteDatabase.FromBytes(merge))
            {
                string id = (db.Scalar("SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'ContentOffers'") != null) ? db.Scalar("SELECT ID FROM ContentOffers LIMIT 1") as string : null;
                uint title;
                if (id != null && id.Length >= 8 && uint.TryParse(id.Substring(0, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out title))
                {
                    this.TitleId = title;
                }
                else if (this.TitleId == 0 && this.volume.HasFolder("Media\\LicenseMasks"))
                {
                    // FM3: no ContentOffers, the cars are in the merge and unlocked by
                    // Media\LicenseMasks\*.sql (UPDATE Data_Car SET IsPurchased=1 ...), a folder FH1 and FM4 lack.
                    this.TitleId = 0x4D53084D;
                }
                else if (this.TitleId == 0)
                {
                    throw new InvalidDataException("The merge database has no ContentOffers id: the source game is unknown.");
                }
                this.Game = GameInfo.FromTitleId(this.TitleId);
                if (this.Game == ForzaGame.Motorsport2 && this.Fm2CarFolders().Count == 0)
                {
                    throw new InvalidDataException("The package has no cars (a track or other content).");
                }
            }
        }

        // FM2 packs and FM3 second-disc packs: the stage's database is the base game's (with the pack's rows), so
        // only the cars whose files are in the pack belong to it (DlcCatalog).
        public bool UsesBaseDatabase
        {
            get { return this.Game == ForzaGame.Motorsport2 || this.baseDatabase; }
        }

        // The cars of the pack (Data_Car.MediaName of the merge database).
        public List<string> Cars()
        {
            if (this.Game == ForzaGame.Motorsport2 || this.baseDatabase)
            {
                return this.Fm2CarFolders();
            }
            List<string> cars = new List<string>();
            byte[] merge = this.MergeDatabase();
            if (merge == null)
            {
                return cars;
            }
            using (SqliteDatabase db = SqliteDatabase.FromBytes(merge))
            {
                if (!DbIds.HasTable(db, "Data_Car"))
                {
                    return cars;   // a pack with no cars (tracks, other content)
                }
                foreach (object[] row in db.Query("SELECT MediaName FROM Data_Car ORDER BY MediaName").Rows)
                {
                    if (row[0] is string)
                    {
                        cars.Add((string) row[0]);
                    }
                }
            }
            return cars;
        }

        // The package's merge database (db\patch\*_merge.slt), or null.
        public byte[] Merge()
        {
            return this.MergeDatabase();
        }

        // The package's string tables of a language (StringTables_pri_<P>.zip, "<LANG>/<Table>.str"): table name and
        // file, lowest priority first so that applying them in order leaves the highest priority's text.
        public List<KeyValuePair<string, byte[]>> StringFiles(string language)
        {
            List<KeyValuePair<string, byte[]>> found = new List<KeyValuePair<string, byte[]>>();
            foreach (Layer layer in this.layers)
            {
                if (!Path.GetFileName(layer.Path).StartsWith("StringTables", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string prefix = language + "/";
                foreach (LayerFile file in this.Files(layer))
                {
                    string name = file.Key.Replace('\\', '/');
                    if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.EndsWith(".str", StringComparison.OrdinalIgnoreCase))
                    {
                        found.Add(new KeyValuePair<string, byte[]>(Path.GetFileNameWithoutExtension(name), file.Value()));
                    }
                }
            }
            return found;
        }

        // The highest priority db\patch\*_merge.slt of the package.
        private byte[] MergeDatabase()
        {
            byte[] found = null;
            foreach (Layer layer in this.layers)
            {
                string prefix = (layer.Mount.Length > 0) ? layer.Mount.Replace('\\', '/') + "/" : string.Empty;
                foreach (LayerFile file in this.Files(layer))
                {
                    string name = (prefix + file.Key).Replace('\\', '/');
                    if (name.StartsWith("Media/db/patch/", StringComparison.OrdinalIgnoreCase) && name.EndsWith("_merge.slt", StringComparison.OrdinalIgnoreCase))
                    {
                        found = file.Value();
                    }
                }
            }
            return found;
        }

        private Dictionary<string, LayerFile> mounted;

        // Every file of the package as the game sees it: path from the game root ('/', e.g. "Media/cars/x/x_lod0.carbin"),
        // a higher priority layer replacing a lower one. Used by the validator; nothing is extracted.
        public List<string> MountedPaths()
        {
            return new List<string>(this.Mounted().Keys);
        }

        public long MountedSize(string path)
        {
            LayerFile file;
            return this.Mounted().TryGetValue(path.Replace('\\', '/'), out file) ? file.Size : -1;
        }

        public byte[] ReadMounted(string path)
        {
            LayerFile file;
            return this.Mounted().TryGetValue(path.Replace('\\', '/'), out file) ? file.Value() : null;
        }

        // A file of the package volume itself (not of a zip): path from the package root ('\'), or -1.
        public long VolumeSize(string relative)
        {
            return this.volume.Size(relative);
        }

        public byte[] ReadVolume(string relative)
        {
            return this.volume.Read(relative);
        }

        public List<string> LayerPaths()
        {
            return this.layers.ConvertAll<string>(delegate(Layer layer) { return layer.Path; });
        }

        private Dictionary<string, LayerFile> Mounted()
        {
            if (this.mounted == null)
            {
                Dictionary<string, LayerFile> found = new Dictionary<string, LayerFile>(StringComparer.OrdinalIgnoreCase);
                foreach (Layer layer in this.layers)
                {
                    string prefix = (layer.Mount.Length > 0) ? layer.Mount.Replace('\\', '/') + "/" : string.Empty;
                    foreach (LayerFile file in this.Files(layer))
                    {
                        found[(prefix + file.Key).Replace('\\', '/')] = file;
                    }
                }
                this.mounted = found;
            }
            return this.mounted;
        }

        // A layer's files: name relative to its mount ('/'), and how to read it.
        private List<LayerFile> Files(Layer layer)
        {
            List<LayerFile> files = new List<LayerFile>();
            Volume volume = this.volume;
            if (layer.IsFolder)
            {
                string prefix = layer.Path.TrimEnd('\\') + "\\";
                foreach (string file in volume.Files())
                {
                    if (file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        string path = file;
                        LayerFile one = new LayerFile();
                        one.Key = file.Substring(prefix.Length).Replace('\\', '/');
                        one.Size = volume.Size(path);
                        one.Reader = delegate { return volume.Read(path); };
                        files.Add(one);
                    }
                }
                return files;
            }
            ForzaZip zip = layer.Zip ?? new ForzaZip(volume.ZipPath(layer.Path));
            foreach (ForzaZip.Entry entry in zip.Entries)
            {
                if (!entry.IsDirectory)
                {
                    ForzaZip.Entry source = entry;
                    LayerFile one = new LayerFile();
                    one.Key = entry.Name;
                    one.Size = entry.UncompressedSize;
                    one.Reader = delegate { return zip.Read(source); };
                    files.Add(one);
                }
            }
            return files;
        }

        // The package laid out as a source game folder (see the class comment), under
        // %TEMP%\ForzaStudio\DlcSource\<package folder name>; kept and reused while its marker is there.
        public string Stage(StringBuilder report)
        {
            if (this.Problem != null)
            {
                throw new InvalidOperationException(this.Problem);
            }
            string root = StageFolder(Path.GetFileName(this.Folder));
            if (this.UsesBaseDatabase && this.stamp.Length > 0 && this.stamp.IndexOf("|base=", StringComparison.Ordinal) < 0)
            {
                // the stage holds the base game's database: made again when the base game folder changes
                this.stamp += "|base=" + (GameInfo.ConfiguredFolder(this.Game) ?? "");
            }
            if (IsStage(root) && this.StampMatches(root))
            {
                return root;
            }
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
            Directory.CreateDirectory(root);
            try
            {
                return this.Stage(root, report);
            }
            catch (Exception)
            {
                Directory.Delete(root, true);
                throw;
            }
        }

        private static string StageFolder(string name)
        {
            return Path.Combine(Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "DlcSource"), name);
        }

        // A stage of a package file is good while the file is the one it was made from (a folder: always).
        private bool StampMatches(string root)
        {
            if (this.stamp.Length == 0)
            {
                return true;
            }
            foreach (string line in File.ReadAllLines(Path.Combine(root, Marker)))
            {
                if (line.StartsWith("stamp=", StringComparison.Ordinal))
                {
                    return line.Substring(6) == this.stamp;
                }
            }
            return false;
        }

        // What a port needs of a package is written when the port runs, not when the package is listed: the cars
        // (Media\cars\<car>\ -> <car>.zip, 100 MB to 1 GB each, stored), the wheels, the digital dashes and the
        // UI textures (Extra) are read from the package when a car is ported (Materialize). The tracks and the
        // videos are never needed. The rest (database, strings, sound, tuning) is small and written now.
        private enum Kind
        {
            Eager,
            Car,
            Extra,
            Skip,
        }

        private class CarInfo
        {
            public string Name;                 // the folder name as the package spells it
            public bool Real;                   // a .carbin in it (a folder like cars\shared is not a car)
            public Dictionary<string, long> Sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            public string Build = "";           // buildnumber.txt
        }

        // The path of a layer's file in the game: the layer's mount and the file name ('\').
        private static string FullName(Layer layer, string key)
        {
            string name = key.Replace('/', '\\');
            return (layer.Mount.Length > 0) ? layer.Mount.TrimEnd('\\') + "\\" + name : name;
        }

        private static Kind Classify(string full, Dictionary<string, CarInfo> cars)
        {
            string[] part = full.Split('\\');
            if (part.Length < 2 || !part[0].Equals("Media", StringComparison.OrdinalIgnoreCase))
            {
                return Kind.Eager;
            }
            string area = part[1];
            if (area.Equals("cars", StringComparison.OrdinalIgnoreCase))
            {
                CarInfo car;
                return (part.Length >= 4 && cars.TryGetValue(part[2], out car)) ? Kind.Car : Kind.Eager;
            }
            if (area.Equals("wheels", StringComparison.OrdinalIgnoreCase) || area.Equals("DigitalGauges", StringComparison.OrdinalIgnoreCase))
            {
                return (part.Length >= 4) ? Kind.Extra : Kind.Eager;
            }
            if (area.Equals("UI", StringComparison.OrdinalIgnoreCase))
            {
                return (part.Length >= 3 && part[2].Equals("Videos", StringComparison.OrdinalIgnoreCase)) ? Kind.Skip : Kind.Extra;
            }
            if (area.Equals("Tracks", StringComparison.OrdinalIgnoreCase))
            {
                return Kind.Skip;
            }
            return Kind.Eager;
        }

        // The package's real cars: sizes of the models (the traffic test) and the build number, from the
        // layers' directories, without reading the models.
        private Dictionary<string, CarInfo> ScanCars()
        {
            Dictionary<string, CarInfo> cars = new Dictionary<string, CarInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (Layer layer in this.layers)
            {
                foreach (LayerFile file in this.Files(layer))
                {
                    string[] part = FullName(layer, file.Key).Split('\\');
                    if (part.Length != 4 || !part[0].Equals("Media", StringComparison.OrdinalIgnoreCase) || !part[1].Equals("cars", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    CarInfo car;
                    if (!cars.TryGetValue(part[2], out car))
                    {
                        car = new CarInfo();
                        car.Name = part[2];
                        cars[part[2]] = car;
                    }
                    if (part[3].EndsWith(".carbin", StringComparison.OrdinalIgnoreCase))
                    {
                        car.Real = true;
                    }
                    car.Sizes[part[3]] = file.Size;
                    if (part[3].Equals("buildnumber.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        car.Build = Encoding.ASCII.GetString(file.Value()).Trim().Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
                    }
                }
            }
            List<string> notCars = new List<string>();
            foreach (KeyValuePair<string, CarInfo> car in cars)
            {
                if (!car.Value.Real)
                {
                    notCars.Add(car.Key);
                }
            }
            foreach (string name in notCars)
            {
                cars.Remove(name);
            }
            return cars;
        }

        // CarPort.HasPlaceholderCloseUp from the sizes of the entries.
        private static bool Traffic(CarInfo car)
        {
            string prefix = car.Name.ToLowerInvariant();
            long main;
            if (!car.Sizes.TryGetValue(prefix + ".carbin", out main))
            {
                return false;
            }
            foreach (string kind in new string[] { "_lod0", "_cockpit" })
            {
                long close;
                if (!car.Sizes.TryGetValue(prefix + kind + ".carbin", out close) || close < main / 10)
                {
                    return true;
                }
            }
            return false;
        }

        private const string LazyFile = "DlcSource.cars.txt";
        private const string ExtrasDone = "DlcSource.extras";

        private string Stage(string root, StringBuilder report)
        {
            int count = 0;
            int later = 0;
            Dictionary<string, CarInfo> cars = this.ScanCars();
            foreach (Layer layer in this.layers)
            {
                foreach (LayerFile file in this.Files(layer))
                {
                    if (file.Key.EndsWith("_build_report.html", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    Kind kind = Classify(FullName(layer, file.Key), cars);
                    if (kind != Kind.Eager)
                    {
                        if (kind != Kind.Skip)
                        {
                            later++;
                        }
                        continue;
                    }
                    string path = Path.Combine(Path.Combine(root, layer.Mount), file.Key.Replace('/', '\\'));
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, file.Value());
                    count++;
                }
            }
            string media = Path.Combine(root, "Media");

            // The merge database as the game's.
            string patch = Path.Combine(Path.Combine(media, "db"), "patch");
            bool fm2 = this.Game == ForzaGame.Motorsport2;
            bool baseDb = fm2 || this.baseDatabase;
            if (baseDb)
            {
                this.MergeFm2Database(media, report);
            }
            else
            {
                string[] merges = Directory.Exists(patch) ? Directory.GetFiles(patch, "*_merge.slt") : new string[0];
                if (merges.Length != 1)
                {
                    throw new InvalidDataException(string.Format(CultureInfo.InvariantCulture, "The package has {0} merge databases; one is needed.", merges.Length));
                }
                File.Copy(merges[0], Path.Combine(Path.Combine(media, "db"), "gamedb.slt"));
            }

            // Folders the port opens as zips.
            int zips = 0;
            foreach (string kind in new string[] { "cars", "wheels" })
            {
                string parent = Path.Combine(media, kind);
                if (!Directory.Exists(parent))
                {
                    continue;
                }
                foreach (string folder in Directory.GetDirectories(parent))
                {
                    if (kind == "wheels" || Directory.GetFiles(folder, "*.carbin").Length > 0)
                    {
                        zips += ToZip(folder);
                    }
                }
            }
            // FM3 digital dashes (Media\DigitalGauges\<List_DigitalGauges.MediaName>.zip in the game).
            string gauges = Path.Combine(media, "DigitalGauges");
            if (Directory.Exists(gauges))
            {
                foreach (string folder in Directory.GetDirectories(gauges))
                {
                    zips += ToZip(folder);
                }
            }
            string tables = Path.Combine(media, "StringTables");
            if (baseDb)
            {
                // The package's Data_Car.str (the car names) overrides one table of the game's string zip of each
                // language: the game's files fill in what the package lacks.
                Directory.CreateDirectory(tables);
                foreach (string zip in Directory.GetFiles(Path.Combine(Path.Combine(GameInfo.ConfiguredFolder(this.Game), "Media"), "StringTables"), "*.zip"))
                {
                    string language = Path.Combine(tables, Path.GetFileNameWithoutExtension(zip).ToUpperInvariant());
                    Directory.CreateDirectory(language);
                    ForzaZip baseZip = new ForzaZip(zip);
                    foreach (ForzaZip.Entry entry in baseZip.Entries)
                    {
                        string path = Path.Combine(language, entry.Name.Replace('/', '\\'));
                        if (!entry.IsDirectory && !File.Exists(path))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(path));
                            File.WriteAllBytes(path, baseZip.Read(entry));
                        }
                    }
                }
                foreach (string language in Directory.GetDirectories(tables))
                {
                    zips += ToZip(language);
                }
            }
            else if (Directory.Exists(tables))
            {
                foreach (string language in Directory.GetDirectories(tables))
                {
                    zips += ToZip(language);
                }
            }
            string audio = Path.Combine(Path.Combine(media, "audio"), "cars");
            string engines = Path.Combine(audio, "Engines");
            foreach (string folder in new string[] { Path.Combine(audio, "CarModelTuning"), Path.Combine(engines, "EngineTuning"), Path.Combine(engines, "HarmonicTuning") })
            {
                // Always there (an empty zip when the pack has none): the port opens them.
                Directory.CreateDirectory(folder);
                zips += ToZip(folder);
            }

            // The cars the package holds, with what the list needs of them (see Materialize).
            List<string> index = new List<string>();
            foreach (CarInfo car in cars.Values)
            {
                index.Add(car.Name + "\t" + (Traffic(car) ? "1" : "0") + "\t" + car.Build);
            }
            File.WriteAllLines(Path.Combine(root, LazyFile), index.ToArray());
            File.WriteAllLines(Path.Combine(root, Marker), new string[]
            {
                "title=" + this.TitleId.ToString("X8", CultureInfo.InvariantCulture),
                "offer=" + this.Offer.ToString(CultureInfo.InvariantCulture),
                "package=" + this.Folder,
                fm2 ? "raw=fm2" : "raw=",
                "lazy=1",
                "stamp=" + this.stamp,
            });
            if (report != null)
            {
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "DLC {0} (offer {1}, {2}): {3} files in {4}, {5} zips, {6} car and wheel files read when a car is ported", Path.GetFileName(this.Folder), this.Offer, GameInfo.Tag(this.Game), count, root, zips, later));
            }
            return root;
        }

        private static readonly object materializing = new object();

        // The cars of a stage by name (case-insensitive): traffic placeholder, build number. Empty for a stage
        // that was written whole (an old one, an FM2 game's) or a folder that is not one.
        public static Dictionary<string, KeyValuePair<bool, string>> LazyCars(string root)
        {
            Dictionary<string, KeyValuePair<bool, string>> cars = new Dictionary<string, KeyValuePair<bool, string>>(StringComparer.OrdinalIgnoreCase);
            string file = (root == null) ? null : Path.Combine(root, LazyFile);
            if (file != null && File.Exists(file))
            {
                foreach (string line in File.ReadAllLines(file))
                {
                    string[] part = line.Split('\t');
                    if (part.Length >= 2)
                    {
                        cars[part[0]] = new KeyValuePair<bool, string>(part[1] == "1", (part.Length > 2) ? part[2] : "");
                    }
                }
            }
            return cars;
        }

        // Makes the stage ready for porting one car: its zip, and once for the package the wheels, the digital
        // dashes and the UI textures, read from the package. Returns true when the car's zip was written now
        // (Release removes it again). A stage written whole needs nothing.
        public static bool Materialize(string root, string car)
        {
            if (!IsStage(root) || Array.IndexOf(File.ReadAllLines(Path.Combine(root, Marker)), "lazy=1") < 0)
            {
                return false;
            }
            lock (materializing)
            {
                string package = null;
                foreach (string line in File.ReadAllLines(Path.Combine(root, Marker)))
                {
                    if (line.StartsWith("package=", StringComparison.Ordinal))
                    {
                        package = line.Substring(8);
                    }
                }
                string zip = Path.Combine(Path.Combine(Path.Combine(root, "Media"), "cars"), car + ".zip");
                bool needCar = car != null && !File.Exists(zip) && LazyCars(root).ContainsKey(car);
                bool needExtras = !File.Exists(Path.Combine(root, ExtrasDone));
                if (!needCar && !needExtras)
                {
                    return false;
                }
                DlcSource source = OpenLayers(package);
                if (source == null || source.Problem != null)
                {
                    throw new InvalidOperationException("The DLC package can't be read again: " + package + ((source != null) ? " (" + source.Problem + ")" : ""));
                }
                Dictionary<string, CarInfo> cars = source.ScanCars();
                if (needExtras)
                {
                    source.WriteKind(root, Kind.Extra, null, cars);
                    string media = Path.Combine(root, "Media");
                    foreach (string kind in new string[] { "wheels", "DigitalGauges" })
                    {
                        string parent = Path.Combine(media, kind);
                        if (Directory.Exists(parent))
                        {
                            foreach (string folder in Directory.GetDirectories(parent))
                            {
                                ToZip(folder);
                            }
                        }
                    }
                    File.WriteAllText(Path.Combine(root, ExtrasDone), "");
                }
                if (needCar)
                {
                    source.WriteKind(root, Kind.Car, car, cars);
                    string parent = Path.Combine(Path.Combine(root, "Media"), "cars");
                    foreach (string folder in Directory.GetDirectories(parent))
                    {
                        if (string.Equals(Path.GetFileName(folder), car, StringComparison.OrdinalIgnoreCase))
                        {
                            ToZip(folder);
                        }
                    }
                }
                return needCar;
            }
        }

        // The zip Materialize wrote for a car goes again once the port is done: only the cars being ported
        // take room.
        public static void Release(string root, string car)
        {
            try
            {
                string zip = Path.Combine(Path.Combine(Path.Combine(root, "Media"), "cars"), car + ".zip");
                if (File.Exists(zip))
                {
                    File.Delete(zip);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        // The package again, layers only, from the stage's package= line (a package file or an extracted folder).
        private static DlcSource OpenLayers(string package)
        {
            if (File.Exists(package))
            {
                return Open(new StfsVolume(StfsReader.Open(package)), package, 0, false);
            }
            string root = Path.GetFullPath(package).TrimEnd('\\');
            return Open(new DiskVolume(root), root, 0, false);
        }

        // Writes the files of one kind (of one car) under the stage root, as Stage would have.
        private void WriteKind(string root, Kind wanted, string car, Dictionary<string, CarInfo> cars)
        {
            foreach (Layer layer in this.layers)
            {
                foreach (LayerFile file in this.Files(layer))
                {
                    if (file.Key.EndsWith("_build_report.html", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    string full = FullName(layer, file.Key);
                    if (Classify(full, cars) != wanted)
                    {
                        continue;
                    }
                    if (car != null && !full.Split('\\')[2].Equals(car, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    string path = Path.Combine(Path.Combine(root, layer.Mount), file.Key.Replace('/', '\\'));
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, file.Value());
                }
            }
        }

        // FM2: the game's gamedb (Tools > Settings > Folders) with the package's SQL run over it (Media\db\patch\*.sql,
        // Media\LicenseMasks\*.sql: unlocks and garage rows), statement by statement; a statement the old schema can't run
        // is counted and left out. The cars' own rows are the game database's: a car it lacks is named in the report.
        private void MergeFm2Database(string media, StringBuilder report)
        {
            string baseRoot = GameInfo.ConfiguredFolder(this.Game);
            if (baseRoot == null)
            {
                throw new InvalidDataException("Set the " + GameInfo.Name(this.Game) + " folder in Tools > Settings > Folders.");
            }
            string dbDir = Path.Combine(media, "db");
            Directory.CreateDirectory(dbDir);
            string db = Path.Combine(dbDir, "gamedb.slt");
            File.Copy(Path.Combine(Path.Combine(Path.Combine(baseRoot, "Media"), "DB"), "gamedb.slt"), db, true);
            // db\patch\<n>_merge.slt (the new rows, the game's schema) and <n>_execute.sql in the order of n, then the
            // unlocks of Media\LicenseMasks.
            List<string> scripts = new List<string>();
            string patch = Path.Combine(dbDir, "patch");
            if (Directory.Exists(patch))
            {
                List<string> files = new List<string>(Directory.GetFiles(patch, "*_merge.slt"));
                files.AddRange(Directory.GetFiles(patch, "*_execute.sql"));
                files.Sort(delegate(string a, string b)
                {
                    int x, y;
                    int.TryParse(Path.GetFileName(a).Substring(0, Path.GetFileName(a).IndexOf('_')), NumberStyles.None, CultureInfo.InvariantCulture, out x);
                    int.TryParse(Path.GetFileName(b).Substring(0, Path.GetFileName(b).IndexOf('_')), NumberStyles.None, CultureInfo.InvariantCulture, out y);
                    return x.CompareTo(y);
                });
                scripts.AddRange(files);
            }
            string masks = Path.Combine(media, "LicenseMasks");
            if (Directory.Exists(masks))
            {
                scripts.AddRange(Directory.GetFiles(masks, "*.sql"));
            }
            int done = 0;
            int failed = 0;
            int rows = 0;
            using (SqliteDatabase merged = SqliteDatabase.FromBytes(File.ReadAllBytes(db)))
            {
                foreach (string script in scripts)
                {
                    if (script.EndsWith(".slt", StringComparison.OrdinalIgnoreCase))
                    {
                        rows += MergeRows(merged, script);
                        continue;
                    }
                    foreach (string statement in File.ReadAllText(script).Split(';'))
                    {
                        if (statement.Trim().Length == 0)
                        {
                            continue;
                        }
                        try
                        {
                            merged.Execute(statement);
                            done++;
                        }
                        catch (Exception)
                        {
                            failed++;
                        }
                    }
                }
                File.WriteAllBytes(db, merged.ToBytes());
                List<string> missing = new List<string>();
                foreach (string car in this.Fm2CarFolders())
                {
                    if (merged.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", car) == null)
                    {
                        missing.Add(car);
                    }
                }
                if (report != null)
                {
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "DLC {0}: base game gamedb + {4} rows of the package's merge database + {1} SQL statements ({2} skipped){3}", Path.GetFileName(this.Folder), done, failed,
                        (missing.Count > 0) ? "; no row in the merged database: " + string.Join(" ", missing.ToArray()) : "", rows));
                }
            }
        }

        // The rows of a package's merge database put into the game's (same table, the columns both have), a row with the
        // same key replaced; the number of rows.
        internal static int MergeRows(SqliteDatabase target, string mergePath)
        {
            int count = 0;
            using (SqliteDatabase patch = SqliteDatabase.FromBytes(File.ReadAllBytes(mergePath)))
            {
                foreach (object[] table in patch.Query("SELECT name FROM sqlite_master WHERE type = 'table'").Rows)
                {
                    string name = (string) table[0];
                    List<string> have = new List<string>();
                    foreach (object[] column in target.Query("PRAGMA table_info(" + SqliteDatabase.Quote(name) + ")").Rows)
                    {
                        have.Add((string) column[1]);
                    }
                    List<string> columns = new List<string>();
                    foreach (object[] column in patch.Query("PRAGMA table_info(" + SqliteDatabase.Quote(name) + ")").Rows)
                    {
                        if (have.Contains((string) column[1]))
                        {
                            columns.Add(SqliteDatabase.Quote((string) column[1]));
                        }
                    }
                    if (columns.Count == 0)
                    {
                        continue;
                    }
                    string list = string.Join(", ", columns.ToArray());
                    string marks = string.Join(", ", new List<string>(columns).ConvertAll<string>(delegate(string c) { return "?"; }).ToArray());
                    foreach (object[] row in patch.Query("SELECT " + list + " FROM " + SqliteDatabase.Quote(name)).Rows)
                    {
                        target.Execute("INSERT OR REPLACE INTO " + SqliteDatabase.Quote(name) + " (" + list + ") VALUES (" + marks + ")", row);
                        count++;
                    }
                }
            }
            return count;
        }

        // A folder replaced by <folder>.zip of its files.
        private static int ToZip(string folder)
        {
            DlcPackage.WriteStoredZip(folder, folder + ".zip");
            Directory.Delete(folder, true);
            return 1;
        }
    }
}
