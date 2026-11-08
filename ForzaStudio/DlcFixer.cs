namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Xml;

    // Fix for what DlcValidator finds, on a DLC package (an STFS file or an extracted folder). Nothing is decided
    // behind the user's back: Analyze() lists what could be changed, each item can be ticked off, Apply() does the
    // ticked ones, then packs the package again (the original goes to bak\ when backups are on).
    //   duplicates   rows of the package's merge database that repeat the game's row exactly (same key, same values) are
    //                deleted: they only make the merge fail. Not offered when the game database holds the package's cars
    //                already (then it is not a pristine game and every row would look like a repeat);
    //   strings      a key a language lacks while another has it: the text is copied from EN (else the first language);
    //   pack name    the ContentOffers name / description keys that are empty get the name the user types;
    //   copies       a tuning / sound file only another installed package has is copied in, so the package stands on its
    //                own; a tuning file the package holds under a similar name (ALF_ZagatoTZ3_12_ET.xml for
    //                ALF_ZagatoTZ3Stradale_12) is offered as a copy under the name the database asks for (not ticked).
    // Not fixed here, on purpose: keys that exist in the game with other values, dangling references, the OfferId, and
    // a tuning or sound bank that exists nowhere (the sound is the user's choice: Tools > Change Car Sound).
    public sealed partial class DlcFixer : IDisposable
    {
        public delegate void Step();

        public class Item
        {
            public string Kind;
            public string Text;
            public bool Checked;
            internal Step Apply;
        }

        private class FLayer
        {
            public string Name;
            public string Path;
            public bool IsFolder;
            public string Mount = "";
            public int Priority;
            public ForzaZip Zip;
            public bool Dirty;
        }

        private class StrRef
        {
            public FLayer Layer;
            public ForzaZip.Entry Entry;
            public StrFile File;        // loaded when first needed
            public bool Dirty;
        }

        public readonly List<Item> Items = new List<Item>();
        public readonly StringBuilder Log = new StringBuilder();
        public string PackName = "";
        public string PackDescription = "";
        public string Title = "";
        public bool NeedsName;

        private readonly string source;
        private readonly bool isFile;
        private StfsReader package;
        private string root;
        private string temp;
        private readonly List<FLayer> layers = new List<FLayer>();
        private FLayer mergeLayer;
        private string mergeKey;
        private SqliteDatabase merge;
        private bool mergeDirty;
        private Dictionary<string, Dictionary<string, StrRef>> strings;
        private readonly Dictionary<string, DlcSource> others = new Dictionary<string, DlcSource>(StringComparer.OrdinalIgnoreCase);

        public DlcFixer(string path)
        {
            this.source = path;
            this.isFile = File.Exists(path);
            if (this.isFile)
            {
                this.package = StfsReader.Open(path);
                this.Title = this.package.DisplayName ?? "";
                this.temp = Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "DlcFix_" + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture));
                this.root = Path.Combine(this.temp, "pkg");
                this.Log.AppendLine("extracting " + Path.GetFileName(path) + "...");
                this.package.Extract(this.root);
            }
            else
            {
                this.root = Path.GetFullPath(path).TrimEnd('\\');
                if (string.Equals(Path.GetFileName(this.root), "Media", StringComparison.OrdinalIgnoreCase))
                {
                    this.root = Path.GetDirectoryName(this.root);
                }
                this.Title = Path.GetFileName(this.root);
            }
            this.ReadLayers();
        }

        public void Dispose()
        {
            if (this.merge != null)
            {
                this.merge.Dispose();
                this.merge = null;
            }
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

        // ----- layers -----

        private void ReadLayers()
        {
            string zips = Path.Combine(Path.Combine(this.root, "Media"), "DLCZips");
            if (!Directory.Exists(zips))
            {
                throw new InvalidOperationException("Not a DLC package (no Media\\DLCZips).");
            }
            Dictionary<string, string> mounts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string list = Path.Combine(zips, "zipmount.xml");
            if (File.Exists(list))
            {
                XmlDocument xml = new XmlDocument();
                xml.Load(list);
                foreach (XmlElement zip in xml.SelectNodes("/zipmount/zip"))
                {
                    string mount = zip.GetAttribute("Mount");
                    mounts[zip.GetAttribute("Name")] = GamePaths.IsGamePath(mount) ? GamePaths.RelativePath(mount).TrimEnd('\\') : "";
                }
            }
            foreach (string entry in Directory.GetFileSystemEntries(zips))
            {
                bool folder = Directory.Exists(entry);
                string name = Path.GetFileName(entry);
                if (!folder && !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string stem = folder ? name : Path.GetFileNameWithoutExtension(name);
                int at = stem.LastIndexOf("_pri_", StringComparison.OrdinalIgnoreCase);
                int priority;
                if (at < 0 || !int.TryParse(stem.Substring(at + 5), NumberStyles.None, CultureInfo.InvariantCulture, out priority))
                {
                    continue;
                }
                FLayer layer = new FLayer();
                layer.Name = name;
                layer.Path = entry;
                layer.IsFolder = folder;
                layer.Priority = priority;
                string mountPath;
                layer.Mount = mounts.TryGetValue(name, out mountPath) ? mountPath : "";
                if (!folder)
                {
                    layer.Zip = new ForzaZip(entry);
                }
                this.layers.Add(layer);
            }
            this.layers.Sort(delegate(FLayer a, FLayer b) { return a.Priority.CompareTo(b.Priority); });
        }

        private static string Prefix(FLayer layer)
        {
            return (layer.Mount.Length > 0) ? layer.Mount.Replace('\\', '/') + "/" : "";
        }

        // The keys (relative to the layer's mount, '/') of every file of a layer.
        private List<string> Keys(FLayer layer)
        {
            List<string> keys = new List<string>();
            if (layer.IsFolder)
            {
                string start = layer.Path.TrimEnd('\\') + "\\";
                foreach (string file in Directory.GetFiles(layer.Path, "*", SearchOption.AllDirectories))
                {
                    keys.Add(file.Substring(start.Length).Replace('\\', '/'));
                }
            }
            else
            {
                foreach (ForzaZip.Entry entry in layer.Zip.Entries)
                {
                    if (!entry.IsDirectory)
                    {
                        keys.Add(entry.Name.Replace('\\', '/'));
                    }
                }
            }
            return keys;
        }

        private byte[] ReadKey(FLayer layer, string key)
        {
            if (layer.IsFolder)
            {
                return File.ReadAllBytes(Path.Combine(layer.Path, key.Replace('/', '\\')));
            }
            ForzaZip.Entry entry = layer.Zip.Find(key);
            return (entry == null) ? null : layer.Zip.Read(entry);
        }

        private void WriteKey(FLayer layer, string key, byte[] data)
        {
            if (layer.IsFolder)
            {
                string file = Path.Combine(layer.Path, key.Replace('/', '\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllBytes(file, CarbinModel.ForGame(file, data));
                return;
            }
            ForzaZip.Entry entry = layer.Zip.Find(key);
            if (entry != null)
            {
                layer.Zip.Replace(entry, data);
            }
            else
            {
                layer.Zip.Add(key, data);
            }
            layer.Dirty = true;
        }

        // The highest layer holding the file of this mounted path ('/'), with its key; null when none does.
        private FLayer Locate(string mounted, out string key)
        {
            FLayer found = null;
            key = null;
            foreach (FLayer layer in this.layers)
            {
                string prefix = Prefix(layer);
                foreach (string k in this.Keys(layer))
                {
                    if (string.Equals(prefix + k, mounted, StringComparison.OrdinalIgnoreCase))
                    {
                        found = layer;
                        key = k;
                    }
                }
            }
            return found;
        }

        // Every mounted path of the package, name -> path.
        private Dictionary<string, string> NamesOf()
        {
            Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (FLayer layer in this.layers)
            {
                string prefix = Prefix(layer);
                foreach (string k in this.Keys(layer))
                {
                    string path = prefix + k;
                    names[path.Substring(path.LastIndexOf('/') + 1)] = path;
                }
            }
            return names;
        }

        // ----- analysis -----

        public void Analyze(DlcValidator.Report report)
        {
            this.OpenMerge();
            this.LoadStrings();
            this.Duplicates(report);
            this.MissingStrings();
            this.PackNameItem();
            this.Copies(report);
            if (this.Items.Count == 0)
            {
                this.Log.AppendLine("nothing here can be fixed automatically.");
            }
        }

        private void OpenMerge()
        {
            if (this.merge != null)
            {
                return;     // already open: reading it again would drop what Relocate / RepairMakes changed in memory
            }
            foreach (FLayer layer in this.layers)
            {
                foreach (string k in this.Keys(layer))
                {
                    string path = Prefix(layer) + k;
                    if (path.StartsWith("Media/db/patch/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("_merge.slt", StringComparison.OrdinalIgnoreCase))
                    {
                        this.mergeLayer = layer;
                        this.mergeKey = k;
                    }
                }
            }
            if (this.mergeLayer != null)
            {
                this.merge = SqliteDatabase.FromBytes(this.ReadKey(this.mergeLayer, this.mergeKey));
            }
        }

        private void Duplicates(DlcValidator.Report report)
        {
            if (this.merge == null)
            {
                return;
            }
            string baseRoot = GameInfo.ConfiguredFolder(report.Game);
            string baseDb = (baseRoot == null) ? null : Path.Combine(Path.Combine(Path.Combine(baseRoot, "Media"), "db"), "gamedb.slt");
            if (baseDb == null || !File.Exists(baseDb))
            {
                this.Log.AppendLine("duplicate rows: the game folder of " + GameInfo.Tag(report.Game) + " is not set, not checked.");
                return;
            }
            using (SqliteDatabase game = SqliteDatabase.FromBytes(File.ReadAllBytes(baseDb)))
            {
                Dictionary<string, List<string>> own = DbIds.Schema(this.merge);
                Dictionary<string, List<string>> gameSchema = DbIds.Schema(game);
                if (own.ContainsKey("Data_Car") && gameSchema.ContainsKey("Data_Car"))
                {
                    Dictionary<string, bool> inGame = new Dictionary<string, bool>();
                    foreach (object[] row in game.Query("SELECT Id FROM Data_Car").Rows)
                    {
                        inGame[Convert.ToString(row[0], CultureInfo.InvariantCulture)] = true;
                    }
                    foreach (object[] row in this.merge.Query("SELECT Id FROM Data_Car").Rows)
                    {
                        if (inGame.ContainsKey(Convert.ToString(row[0], CultureInfo.InvariantCulture)))
                        {
                            this.Log.AppendLine("duplicate rows: the game database already holds this package's cars (Data_Car.Id " + Convert.ToString(row[0], CultureInfo.InvariantCulture) + "): it is not a pristine game, so no row is offered for deletion.");
                            return;
                        }
                    }
                }
                foreach (KeyValuePair<string, List<string>> table in own)
                {
                    List<string> gameColumns;
                    if (table.Key.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase) || !gameSchema.TryGetValue(table.Key, out gameColumns))
                    {
                        continue;
                    }
                    string key = DbIds.KeyColumn(this.merge, table.Key, table.Value);
                    if (key == null)
                    {
                        continue;
                    }
                    List<string> shared = table.Value.FindAll(delegate(string c) { return gameColumns.Exists(delegate(string g) { return string.Equals(g, c, StringComparison.OrdinalIgnoreCase); }); });
                    int at = shared.FindIndex(delegate(string c) { return string.Equals(c, key, StringComparison.OrdinalIgnoreCase); });
                    if (at < 0)
                    {
                        continue;
                    }
                    string select = string.Join(", ", shared.ConvertAll<string>(delegate(string c) { return SqliteDatabase.Quote(c); }).ToArray());
                    Dictionary<string, string> gameRows = new Dictionary<string, string>();
                    foreach (object[] row in game.Query("SELECT " + select + " FROM " + SqliteDatabase.Quote(table.Key)).Rows)
                    {
                        gameRows[Convert.ToString(row[at], CultureInfo.InvariantCulture)] = DbIntegrity.RowText(row);
                    }
                    List<object> same = new List<object>();
                    foreach (object[] row in this.merge.Query("SELECT " + select + " FROM " + SqliteDatabase.Quote(table.Key)).Rows)
                    {
                        string text;
                        if (gameRows.TryGetValue(Convert.ToString(row[at], CultureInfo.InvariantCulture), out text) && text == DbIntegrity.RowText(row))
                        {
                            same.Add(row[at]);
                        }
                    }
                    if (same.Count == 0)
                    {
                        continue;
                    }
                    string tableName = table.Key;
                    string keyName = key;
                    List<object> keys = same;
                    Item item = new Item();
                    item.Kind = "database";
                    item.Checked = true;
                    item.Text = string.Format(CultureInfo.InvariantCulture, "{0}: delete {1} row(s) identical to the game's ({2} {3}...{4})", tableName, keys.Count, keyName, Convert.ToString(keys[0], CultureInfo.InvariantCulture), Convert.ToString(keys[keys.Count - 1], CultureInfo.InvariantCulture));
                    item.Apply = delegate
                    {
                        foreach (object value in keys)
                        {
                            this.merge.Execute("DELETE FROM " + SqliteDatabase.Quote(tableName) + " WHERE " + SqliteDatabase.Quote(keyName) + " = ?", value);
                        }
                        this.mergeDirty = true;
                        this.Log.AppendLine(string.Format(CultureInfo.InvariantCulture, "database: {0} row(s) deleted from {1}", keys.Count, tableName));
                    };
                    this.Items.Add(item);
                }
            }
        }

        private void LoadStrings()
        {
            if (this.strings != null)
            {
                return;     // loaded: a second load would drop the tables a step before changed (RekeyStrings, then MoveStrings)
            }
            this.strings = new Dictionary<string, Dictionary<string, StrRef>>(StringComparer.OrdinalIgnoreCase);
            foreach (FLayer layer in this.layers)
            {
                if (layer.IsFolder || !layer.Name.StartsWith("StringTables", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                foreach (ForzaZip.Entry entry in layer.Zip.Entries)
                {
                    string[] part = entry.Name.Replace('\\', '/').Split('/');
                    if (part.Length < 2 || !part[part.Length - 1].EndsWith(".str", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    string language = part[part.Length - 2];
                    string table = Path.GetFileNameWithoutExtension(part[part.Length - 1]);
                    Dictionary<string, StrRef> tables;
                    if (!this.strings.TryGetValue(language, out tables))
                    {
                        tables = new Dictionary<string, StrRef>(StringComparer.OrdinalIgnoreCase);
                        this.strings[language] = tables;
                    }
                    StrRef reference = new StrRef();
                    reference.Layer = layer;
                    reference.Entry = entry;
                    tables[table] = reference;
                }
            }
        }

        private static bool Pseudo(string language)
        {
            return language.Equals("DEV", StringComparison.OrdinalIgnoreCase) || language.Equals("LOC", StringComparison.OrdinalIgnoreCase);
        }

        private StrFile Str(StrRef reference)
        {
            if (reference.File == null)
            {
                reference.File = new StrFile(reference.Layer.Zip.Read(reference.Entry));
            }
            return reference.File;
        }

        private static StrFile.Entry EntryOf(StrFile file, int key)
        {
            return file.Entries.Find(delegate(StrFile.Entry e) { return e.Key == key; });
        }

        private void MissingStrings()
        {
            Dictionary<string, bool> tableNames = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (Dictionary<string, StrRef> tables in this.strings.Values)
            {
                foreach (string table in tables.Keys)
                {
                    tableNames[table] = true;
                }
            }
            foreach (string table in tableNames.Keys)
            {
                string tableName = table;
                // key -> the language to copy the text from (EN first)
                Dictionary<int, string> sourceLanguage = new Dictionary<int, string>();
                List<string> languages = new List<string>(this.strings.Keys);
                languages.Sort(delegate(string a, string b)
                {
                    int ra = a.Equals("EN", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                    int rb = b.Equals("EN", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                    return (ra != rb) ? ra.CompareTo(rb) : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                });
                Dictionary<string, Dictionary<int, bool>> has = new Dictionary<string, Dictionary<int, bool>>(StringComparer.OrdinalIgnoreCase);
                foreach (string language in languages)
                {
                    StrRef reference;
                    if (Pseudo(language) || !this.strings[language].TryGetValue(table, out reference))
                    {
                        continue;
                    }
                    Dictionary<int, bool> keys = new Dictionary<int, bool>();
                    foreach (StrFile.Entry entry in this.Str(reference).Entries)
                    {
                        keys[entry.Key] = true;
                        if (!sourceLanguage.ContainsKey(entry.Key))
                        {
                            sourceLanguage[entry.Key] = language;
                        }
                    }
                    has[language] = keys;
                }
                List<string[]> todo = new List<string[]>();   // language, key
                foreach (KeyValuePair<string, Dictionary<int, bool>> language in has)
                {
                    foreach (int key in sourceLanguage.Keys)
                    {
                        if (!language.Value.ContainsKey(key))
                        {
                            todo.Add(new string[] { language.Key, key.ToString(CultureInfo.InvariantCulture) });
                        }
                    }
                }
                if (todo.Count == 0)
                {
                    continue;
                }
                Dictionary<int, string> from = sourceLanguage;
                List<string[]> fill = todo;
                List<string> missingKeys = new List<string>();
                foreach (string[] one in todo)
                {
                    if (!missingKeys.Contains(one[1]))
                    {
                        missingKeys.Add(one[1]);
                    }
                }
                Item item = new Item();
                item.Kind = "strings";
                item.Checked = true;
                item.Text = string.Format(CultureInfo.InvariantCulture, "{0}.str: add {1} missing text(s) (keys {2}{3}), copied from the language that has them", table, todo.Count,
                    string.Join(", ", missingKeys.GetRange(0, Math.Min(5, missingKeys.Count)).ToArray()), missingKeys.Count > 5 ? "..." : "");
                item.Apply = delegate
                {
                    foreach (string[] one in fill)
                    {
                        int key = int.Parse(one[1], CultureInfo.InvariantCulture);
                        StrRef sourceRef = this.strings[from[key]][tableName];
                        StrFile.Entry original = EntryOf(this.Str(sourceRef), key);
                        StrRef target = this.strings[one[0]][tableName];
                        this.Str(target).Set(key, original.Text, original.Name);
                        target.Dirty = true;
                    }
                    this.Log.AppendLine(string.Format(CultureInfo.InvariantCulture, "strings: {0} text(s) added to {1}", fill.Count, tableName));
                };
                this.Items.Add(item);
            }
        }

        private void PackNameItem()
        {
            if (this.merge == null || !DbIds.HasTable(this.merge, "ContentOffers"))
            {
                return;
            }
            List<int> keys = new List<int>();
            foreach (object[] row in this.merge.Query("SELECT DisplayName, Description FROM ContentOffers").Rows)
            {
                for (int i = 0; i < 2; i++)
                {
                    string text = row[i] as string;
                    uint reference;
                    if (text == null || !text.StartsWith("_&", StringComparison.Ordinal) || !uint.TryParse(text.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out reference))
                    {
                        keys.Add(-1);
                        continue;
                    }
                    keys.Add((int) (reference & 0xFFFF));
                }
            }
            if (keys.Count < 2)
            {
                return;
            }
            int nameKey = keys[0];
            int descriptionKey = keys[1];
            int empty = 0;
            foreach (Dictionary<string, StrRef> tables in this.strings.Values)
            {
                StrRef reference;
                if (!tables.TryGetValue("ContentOffers", out reference))
                {
                    continue;
                }
                StrFile.Entry name = (nameKey >= 0) ? EntryOf(this.Str(reference), nameKey) : null;
                if (nameKey >= 0 && (name == null || string.IsNullOrEmpty(name.Text)))
                {
                    empty++;
                }
            }
            if (empty == 0)
            {
                return;
            }
            this.NeedsName = true;
            this.PackName = this.Title;
            this.PackDescription = this.Title;
            Item item = new Item();
            item.Kind = "strings";
            item.Checked = true;
            item.Text = string.Format(CultureInfo.InvariantCulture, "pack name and description (ContentOffers keys {0} and {1}) are empty in {2} language(s): write the name below in all of them", nameKey, descriptionKey, empty);
            item.Apply = delegate
            {
                if (this.PackName.Trim().Length == 0)
                {
                    this.Log.AppendLine("pack name: no name typed, left empty.");
                    return;
                }
                string description = (this.PackDescription.Trim().Length == 0) ? this.PackName : this.PackDescription;
                foreach (KeyValuePair<string, Dictionary<string, StrRef>> language in this.strings)
                {
                    StrRef reference;
                    if (!language.Value.TryGetValue("ContentOffers", out reference))
                    {
                        continue;
                    }
                    StrFile file = this.Str(reference);
                    StrFile.Entry name = EntryOf(file, nameKey);
                    StrFile.Entry text = EntryOf(file, descriptionKey);
                    if (nameKey >= 0 && (name == null || string.IsNullOrEmpty(name.Text)))
                    {
                        file.Set(nameKey, this.PackName, (name != null) ? name.Name : null);
                        reference.Dirty = true;
                    }
                    if (descriptionKey >= 0 && (text == null || string.IsNullOrEmpty(text.Text)))
                    {
                        file.Set(descriptionKey, description, (text != null) ? text.Name : null);
                        reference.Dirty = true;
                    }
                }
                this.Log.AppendLine("pack name: \"" + this.PackName + "\" written in every language where it was empty");
            };
            this.Items.Add(item);
        }

        private static string Suffix(string name)
        {
            foreach (string suffix in new string[] { "_ET.xml", "_CMT.xml", "_HT.xml", ".fsb" })
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return suffix;
                }
            }
            return null;
        }

        // The package's own file of the same kind whose name is the missing name plus or minus a part
        // (ALF_ZagatoTZ3_12 and ALF_ZagatoTZ3Stradale_12); exactly one, else null.
        private static string SimilarName(string name, Dictionary<string, string> names)
        {
            string suffix = Suffix(name);
            if (suffix == null)
            {
                return null;
            }
            string stem = name.Substring(0, name.Length - suffix.Length);
            string found = null;
            foreach (string candidate in names.Keys)
            {
                if (!candidate.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) || candidate.Length < 8 || string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string other = candidate.Substring(0, candidate.Length - suffix.Length);
                if (other.Length < 6 || !string.Equals(other.Substring(0, 3), stem.Substring(0, Math.Min(3, stem.Length)), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                // the same words: one name begins like the other up to the year, or the other has the year and a tail
                string a = stem.ToLowerInvariant();
                string b = other.ToLowerInvariant();
                string headA = a.Substring(0, a.LastIndexOf('_') > 0 ? a.LastIndexOf('_') : a.Length);
                string headB = b.Substring(0, b.LastIndexOf('_') > 0 ? b.LastIndexOf('_') : b.Length);
                bool similar = headA.StartsWith(headB, StringComparison.Ordinal) || headB.StartsWith(headA, StringComparison.Ordinal) || b.StartsWith(a + "_", StringComparison.Ordinal) || a.StartsWith(b + "_", StringComparison.Ordinal);
                if (!similar)
                {
                    continue;
                }
                if (found != null)
                {
                    return null;    // more than one: not a safe guess
                }
                found = candidate;
            }
            return found;
        }

        private void Copies(DlcValidator.Report report)
        {
            if (this.mergeLayer == null)
            {
                return;
            }
            Dictionary<string, string> names = this.NamesOf();
            List<string> done = new List<string>();
            foreach (string[] other in report.Elsewhere)
            {
                if (done.Contains(other[0].ToLowerInvariant()))
                {
                    continue;
                }
                done.Add(other[0].ToLowerInvariant());
                string[] copy = other;
                Item item = new Item();
                item.Kind = "files";
                item.Checked = true;
                item.Text = copy[0] + ": copy from package " + copy[1] + " (the package then stands on its own)";
                item.Apply = delegate
                {
                    DlcSource pack;
                    if (!this.others.TryGetValue(copy[2], out pack))
                    {
                        pack = Directory.Exists(copy[2]) ? DlcSource.Open(copy[2]) : DlcSource.OpenPackage(copy[2], true);
                        this.others[copy[2]] = pack;
                    }
                    byte[] data = pack.ReadMounted(copy[3]);
                    string prefix = Prefix(this.mergeLayer);
                    if (data == null || !copy[3].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        this.Log.AppendLine("files: " + copy[0] + " could not be read from " + copy[1]);
                        return;
                    }
                    this.WriteKey(this.mergeLayer, copy[3].Substring(prefix.Length), data);
                    this.Log.AppendLine("files: " + copy[0] + " copied from " + copy[1]);
                };
                this.Items.Add(item);
            }
            foreach (string[] missing in report.Missing)
            {
                string wanted = missing[0];
                if (done.Contains(wanted.ToLowerInvariant()))
                {
                    continue;
                }
                done.Add(wanted.ToLowerInvariant());
                string similar = SimilarName(wanted, names);
                if (similar == null)
                {
                    this.Log.AppendLine("files: " + wanted + " (" + missing[1] + ") exists nowhere: choose its sound with Tools > Change Car Sound.");
                    continue;
                }
                string from = names[similar];
                string newName = wanted;
                Item item = new Item();
                item.Kind = "files";
                item.Checked = false;
                item.Text = string.Format(CultureInfo.InvariantCulture, "{0}: copy {1} under this name (similar name, car {2}: check that it is the right one)", wanted, similar, missing[1]);
                item.Apply = delegate
                {
                    string key;
                    FLayer layer = this.Locate(from, out key);
                    if (layer == null)
                    {
                        return;
                    }
                    int slash = key.LastIndexOf('/');
                    string target = ((slash >= 0) ? key.Substring(0, slash + 1) : "") + newName;
                    this.WriteKey(layer, target, this.ReadKey(layer, key));
                    this.Log.AppendLine("files: " + newName + " made from " + from.Substring(from.LastIndexOf('/') + 1));
                };
                this.Items.Add(item);
            }
        }

        // ----- apply -----

        // Does the ticked items, writes everything back and packs the package again. Returns the backup's path
        // (null when backups are off).
        public string Apply(List<Item> chosen)
        {
            if (chosen.Count == 0)
            {
                return null;
            }
            foreach (Item item in chosen)
            {
                item.Apply();
            }
            if (this.mergeDirty && this.merge != null)
            {
                this.WriteKey(this.mergeLayer, this.mergeKey, this.merge.ToBytes());
            }
            foreach (Dictionary<string, StrRef> tables in this.strings.Values)
            {
                foreach (StrRef reference in tables.Values)
                {
                    if (reference.Dirty)
                    {
                        reference.Layer.Zip.Replace(reference.Entry, reference.File.ToBytes());
                        reference.Layer.Dirty = true;
                    }
                }
            }
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
            string backup = null;
            string bakFolder = Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), "bak");
            foreach (FLayer layer in this.layers)
            {
                if (layer.IsFolder || !layer.Dirty)
                {
                    continue;
                }
                if (!this.isFile && BackupPolicy.Enabled)
                {
                    Directory.CreateDirectory(bakFolder);
                    backup = Path.Combine(bakFolder, stamp + "_bak_" + layer.Name);
                    File.Copy(layer.Path, backup, true);
                }
                layer.Zip.Save(layer.Path);
                this.Log.AppendLine("saved " + layer.Name);
            }
            if (!this.isFile)
            {
                return backup;
            }
            string output = this.source + ".new";
            try
            {
                StfsPackage.Rewrite(this.root, output, this.package.Header);
                StfsReader check = StfsReader.Open(output);
                if (check.Files().Count == 0)
                {
                    throw new InvalidOperationException("The rewritten package is empty.");
                }
                if (BackupPolicy.Enabled)
                {
                    Directory.CreateDirectory(bakFolder);
                    backup = Path.Combine(bakFolder, stamp + "_bak_" + Path.GetFileName(this.source));
                    File.Copy(this.source, backup, true);
                    this.Log.AppendLine("original kept: " + backup);
                }
                File.Delete(this.source);
                File.Move(output, this.source);
            }
            finally
            {
                if (File.Exists(output))
                {
                    File.Delete(output);
                }
            }
            this.Log.AppendLine("package written: " + this.source);
            return backup;
        }
    }
}
