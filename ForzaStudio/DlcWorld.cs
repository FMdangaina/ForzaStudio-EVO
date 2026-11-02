namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;

    // Everything a DLC package to install has to live beside: the game's database and the merge databases, car
    // names, offer numbers and files of the packages already in Xenia's content folder (STFS files and extracted
    // folders, read in place). The package being installed is left out, by path.
    public sealed class DlcWorld : IDisposable
    {
        public readonly List<DlcRelocate.Other> Others = new List<DlcRelocate.Other>();
        public readonly List<string> MediaNames = new List<string>();
        public readonly List<int> Offers = new List<int>();
        public readonly List<string> MergeFiles = new List<string>();      // the merge databases as files, for DbIntegrity.CheckMerge
        public readonly List<string> Skipped = new List<string>();
        public readonly List<string> Packages = new List<string>();        // "name (title) offer"
        public readonly Dictionary<string, DlcSource> Files = new Dictionary<string, DlcSource>(StringComparer.OrdinalIgnoreCase);
        public string GameRoot;
        public byte[] GameDb;
        public ForzaGame Game;

        // FM3 string tables: the game's per language, then the layers the installed packs ship (StringTables_pri_<P>.zip), by priority.
        // A pack's table REPLACES the whole table of a lower priority, so a pack to install is rebuilt on top of all of them (DlcFixer.MoveStringsFm3).
        public sealed class StringLayer
        {
            public int Priority;
            public string Pack;
            public readonly Dictionary<string, Dictionary<string, StrFile>> Tables = new Dictionary<string, Dictionary<string, StrFile>>(StringComparer.OrdinalIgnoreCase);   // language -> table -> file
        }

        public readonly List<StringLayer> StringLayers = new List<StringLayer>();
        public readonly List<string> Languages = new List<string>();

        // The Benchmark string table (the base game's 10 stats-test strings) per language: the game's, then the layers the
        // installed packs ship, lowest offer first. A new pack's names go into this table (see DlcFixer.MoveStrings).
        public readonly Dictionary<string, StrFile> BenchmarkBase = new Dictionary<string, StrFile>(StringComparer.OrdinalIgnoreCase);
        public readonly List<KeyValuePair<int, Dictionary<string, StrFile>>> BenchmarkPacks = new List<KeyValuePair<int, Dictionary<string, StrFile>>>();

        private string temp;

        public static DlcWorld Load(string content, string gameRoot, string exclude)
        {
            DlcWorld world = new DlcWorld();
            world.GameRoot = gameRoot;
            world.Game = GameInfo.Detect(gameRoot);
            world.LoadLanguages();
            string gameDb = Path.Combine(Path.Combine(Path.Combine(gameRoot, "Media"), "db"), "gamedb.slt");
            if (!File.Exists(gameDb))
            {
                throw new InvalidOperationException("The game folder has no Media\\db\\gamedb.slt: " + gameRoot);
            }
            world.GameDb = File.ReadAllBytes(gameDb);
            DlcRelocate.Other game = new DlcRelocate.Other();
            game.Name = "the game";
            game.IsGame = true;
            game.Db = SqliteDatabase.FromBytes(world.GameDb);
            world.Others.Add(game);
            world.temp = Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "World_" + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(world.temp);
            world.LoadBenchmark();
            string skip = (exclude == null) ? null : Path.GetFullPath(exclude).TrimEnd('\\');
            if (!string.IsNullOrEmpty(content) && Directory.Exists(content))
            {
                List<string> candidates = new List<string>();
                foreach (string item in Directory.GetFiles(content, "*", SearchOption.AllDirectories))
                {
                    candidates.Add(item);
                }
                foreach (string item in Directory.GetDirectories(content, "*", SearchOption.TopDirectoryOnly))
                {
                    if (Directory.Exists(Path.Combine(Path.Combine(item, "Media"), "DLCZips")))
                    {
                        candidates.Add(item);
                    }
                }
                foreach (string item in candidates)
                {
                    string full = Path.GetFullPath(item).TrimEnd('\\');
                    if (skip != null && (string.Equals(full, skip, StringComparison.OrdinalIgnoreCase) || full.StartsWith(skip + "\\", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                    bool folder = Directory.Exists(item);
                    try
                    {
                        if (!folder && !StfsReader.IsPackage(item))
                        {
                            continue;
                        }
                        DlcSource source = folder ? DlcSource.Open(item) : DlcSource.OpenPackage(item, true);
                        if (source == null || source.Problem != null)
                        {
                            continue;
                        }
                        byte[] merge = source.Merge();
                        if (merge == null)
                        {
                            continue;
                        }
                        world.Add(Path.GetFileName(item), source, merge);
                    }
                    catch (Exception e)
                    {
                        world.Skipped.Add(Path.GetFileName(item) + ": " + e.Message);
                    }
                }
            }
            foreach (string kind in new string[] { "cars", "wheels" })
            {
                string folder = Path.Combine(Path.Combine(gameRoot, "Media"), kind);
                if (Directory.Exists(folder))
                {
                    foreach (string zip in Directory.GetFiles(folder, "*.zip"))
                    {
                        world.MediaNames.Add(Path.GetFileNameWithoutExtension(zip));
                    }
                }
            }
            return world;
        }

        private void Add(string name, DlcSource source, byte[] merge)
        {
            DlcRelocate.Other other = new DlcRelocate.Other();
            other.Name = name + " (" + (source.Title ?? "") + ")";
            other.Db = SqliteDatabase.FromBytes(merge);
            this.Others.Add(other);
            string file = Path.Combine(this.temp, this.MergeFiles.Count.ToString(CultureInfo.InvariantCulture) + ".slt");
            File.WriteAllBytes(file, merge);
            this.MergeFiles.Add(file);
            foreach (string car in source.Cars())
            {
                this.MediaNames.Add(car);
            }
            foreach (string path in source.MountedPaths())
            {
                this.Files[Normal(path)] = source;
            }
            this.Offers.Add(source.Offer);
            if (this.Game == ForzaGame.Motorsport3)
            {
                this.AddStringLayer(name, source);
            }
            this.Packages.Add(other.Name + " offer " + source.Offer.ToString(CultureInfo.InvariantCulture));
            Dictionary<string, StrFile> layer = new Dictionary<string, StrFile>(StringComparer.OrdinalIgnoreCase);
            foreach (string language in new List<string>(this.BenchmarkBase.Keys))
            {
                foreach (KeyValuePair<string, byte[]> strFile in source.StringFiles(language))
                {
                    if (string.Equals(strFile.Key, "Benchmark", StringComparison.OrdinalIgnoreCase))
                    {
                        layer[language] = new StrFile(strFile.Value);
                    }
                }
            }
            if (layer.Count > 0)
            {
                this.BenchmarkPacks.Add(new KeyValuePair<int, Dictionary<string, StrFile>>(source.Offer, layer));
            }
        }

        private void LoadLanguages()
        {
            string folder = Path.Combine(Path.Combine(this.GameRoot, "Media"), "StringTables");
            if (Directory.Exists(folder))
            {
                foreach (string zip in Directory.GetFiles(folder, "*.zip"))
                {
                    this.Languages.Add(Path.GetFileNameWithoutExtension(zip));
                }
            }
        }

        private void AddStringLayer(string name, DlcSource source)
        {
            StringLayer layer = new StringLayer();
            layer.Pack = name;
            layer.Priority = source.Offer + 150;
            foreach (string path in source.LayerPaths())
            {
                string file = Path.GetFileNameWithoutExtension(path);
                int at = file.LastIndexOf("_pri_", StringComparison.OrdinalIgnoreCase);
                int priority;
                if (file.StartsWith("StringTables", StringComparison.OrdinalIgnoreCase) && at > 0 && int.TryParse(file.Substring(at + 5), NumberStyles.None, CultureInfo.InvariantCulture, out priority))
                {
                    layer.Priority = priority;
                }
            }
            foreach (string language in this.Languages)
            {
                Dictionary<string, StrFile> tables = new Dictionary<string, StrFile>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, byte[]> strFile in source.StringFiles(language))
                {
                    tables[strFile.Key] = new StrFile(strFile.Value);
                }
                if (tables.Count > 0)
                {
                    layer.Tables[language] = tables;
                }
            }
            if (layer.Tables.Count > 0)
            {
                this.StringLayers.Add(layer);
                this.StringLayers.Sort(delegate(StringLayer a, StringLayer b) { return a.Priority.CompareTo(b.Priority); });
            }
        }

        private readonly Dictionary<string, Dictionary<int, List<string>>> knownTexts = new Dictionary<string, Dictionary<int, List<string>>>(StringComparer.OrdinalIgnoreCase);

        // Every text a key has in the game or in any installed pack's version of a table (a pack's table carries the strings of the packs it was built on,
        // so a text found here is inherited, not the pack's own).
        public Dictionary<int, List<string>> KnownTexts(string language, string table)
        {
            string id = language + "|" + table;
            Dictionary<int, List<string>> found;
            if (this.knownTexts.TryGetValue(id, out found))
            {
                return found;
            }
            found = new Dictionary<int, List<string>>();
            List<StrFile> files = new List<StrFile>();
            string zipPath = Path.Combine(Path.Combine(Path.Combine(this.GameRoot, "Media"), "StringTables"), language + ".zip");
            if (File.Exists(zipPath))
            {
                ForzaZip archive = new ForzaZip(zipPath);
                ForzaZip.Entry entry = archive.Find(table + ".str");
                if (entry != null)
                {
                    files.Add(new StrFile(archive.Read(entry)));
                }
            }
            foreach (StringLayer layer in this.StringLayers)
            {
                Dictionary<string, StrFile> tables;
                StrFile file;
                if (layer.Tables.TryGetValue(language, out tables) && tables.TryGetValue(table, out file))
                {
                    files.Add(file);
                }
            }
            foreach (StrFile file in files)
            {
                foreach (StrFile.Entry text in file.Entries)
                {
                    List<string> texts;
                    if (!found.TryGetValue(text.Key, out texts))
                    {
                        texts = new List<string>();
                        found[text.Key] = texts;
                    }
                    if (!texts.Contains(text.Text))
                    {
                        texts.Add(text.Text);
                    }
                }
            }
            this.knownTexts[id] = found;
            return found;
        }

        // The highest string priority of the installed packs (0 when none).
        public int TopStringPriority()
        {
            int top = 0;
            foreach (StringLayer layer in this.StringLayers)
            {
                top = Math.Max(top, layer.Priority);
            }
            return top;
        }

        // A string table of a language as the game and the installed packs leave it: the game's, then each layer's over it; a copy, free to change.
        public StrFile StringTable(string language, string table)
        {
            StrFile result = null;
            string zipPath = Path.Combine(Path.Combine(Path.Combine(this.GameRoot, "Media"), "StringTables"), language + ".zip");
            if (File.Exists(zipPath))
            {
                ForzaZip archive = new ForzaZip(zipPath);
                ForzaZip.Entry entry = archive.Find(table + ".str");
                if (entry != null)
                {
                    result = new StrFile(archive.Read(entry));
                }
            }
            foreach (StringLayer layer in this.StringLayers)
            {
                Dictionary<string, StrFile> tables;
                StrFile file;
                if (!layer.Tables.TryGetValue(language, out tables) || !tables.TryGetValue(table, out file))
                {
                    continue;
                }
                if (result == null)
                {
                    result = new StrFile(file.ToBytes());
                    continue;
                }
                foreach (StrFile.Entry text in file.Entries)
                {
                    result.Set(text.Key, text.Text, text.Name);
                }
            }
            return result;
        }

        // The base game's Benchmark.str of every language (Media\StringTables\<LANG>.zip).
        private void LoadBenchmark()
        {
            string folder = Path.Combine(Path.Combine(this.GameRoot, "Media"), "StringTables");
            if (!Directory.Exists(folder))
            {
                return;
            }
            foreach (string zip in Directory.GetFiles(folder, "*.zip"))
            {
                ForzaZip archive = new ForzaZip(zip);
                ForzaZip.Entry entry = archive.Find("Benchmark.str");
                if (entry != null)
                {
                    this.BenchmarkBase[Path.GetFileNameWithoutExtension(zip)] = new StrFile(archive.Read(entry));
                }
            }
        }

        // The Benchmark table of a language as the game and every installed pack leave it (the pack with the highest
        // offer wins a key); a copy, free to change.
        public StrFile Benchmark(string language)
        {
            StrFile baseFile;
            StrFile result = this.BenchmarkBase.TryGetValue(language, out baseFile) ? new StrFile(baseFile.ToBytes()) : null;
            List<KeyValuePair<int, Dictionary<string, StrFile>>> packs = new List<KeyValuePair<int, Dictionary<string, StrFile>>>(this.BenchmarkPacks);
            packs.Sort(delegate(KeyValuePair<int, Dictionary<string, StrFile>> a, KeyValuePair<int, Dictionary<string, StrFile>> b) { return a.Key.CompareTo(b.Key); });
            foreach (KeyValuePair<int, Dictionary<string, StrFile>> pack in packs)
            {
                StrFile layer;
                if (!pack.Value.TryGetValue(language, out layer))
                {
                    continue;
                }
                if (result == null)
                {
                    result = new StrFile(layer.ToBytes());
                    continue;
                }
                foreach (StrFile.Entry text in layer.Entries)
                {
                    result.Set(text.Key, text.Text, text.Name);
                }
            }
            return result;
        }

        // "game:\Media\X" or "Media/X" -> "media/x".
        private static string Normal(string path)
        {
            string key = path.Replace('\\', '/');
            if (key.StartsWith("game:/", StringComparison.OrdinalIgnoreCase))
            {
                key = key.Substring(6);
            }
            return key.TrimStart('/');
        }

        // A file of an installed pack by its mounted path ("Media/audio/Cars/CarModelTuning/X_CMT.xml"), or null.
        public byte[] ReadPackFile(string path)
        {
            DlcSource source;
            return this.Files.TryGetValue(Normal(path), out source) ? source.ReadMounted(Normal(path)) : null;
        }

        // A car's model tuning (its sound model) from an installed pack, else from the game's CarModelTuning.zip.
        public byte[] ReadCarModelTuning(string mediaName)
        {
            string name = mediaName + "_CMT.xml";
            byte[] data = this.ReadPackFile("Media/audio/Cars/CarModelTuning/" + name);
            if (data != null)
            {
                return data;
            }
            string zip = Path.Combine(Path.Combine(Path.Combine(Path.Combine(this.GameRoot, "Media"), "audio"), "cars"), "CarModelTuning.zip");
            if (!File.Exists(zip))
            {
                return null;
            }
            ForzaZip archive = new ForzaZip(zip);
            ForzaZip.Entry entry = archive.Find(name);
            return (entry == null) ? null : archive.Read(entry);
        }

        // True when the game or an installed pack has the file (a game path).
        public bool FileTaken(string path)
        {
            return this.Files.ContainsKey(Normal(path)) || GamePaths.Locate(this.GameRoot, path) != null;
        }

        public void Dispose()
        {
            foreach (DlcRelocate.Other other in this.Others)
            {
                other.Db.Dispose();
            }
            this.Others.Clear();
            if (this.temp != null && Directory.Exists(this.temp))
            {
                try
                {
                    Directory.Delete(this.temp, true);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
