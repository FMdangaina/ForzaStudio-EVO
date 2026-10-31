namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;

    // "Validate DLC for installation" (user 2026-10-31): the package is extracted to a temporary folder, DlcRelocate
    // moves its database ids out of the way of the game and of the other installed packages, the files that
    // go with the new ids are renamed (car and wheel folders, thumbnails, offer file names), and SaveTo packs the
    // result into a new STFS file. The original package is never written.
    public sealed partial class DlcFixer
    {
        public DlcRelocate Relocation;

        private readonly List<string> stale = new List<string>();      // files of renamed layers, deleted once the new ones are saved

        public void Relocate(DlcWorld world)
        {
            this.OpenMerge();
            if (this.merge == null)
            {
                throw new InvalidOperationException("The package has no merge database (Media\\db\\patch\\*_merge.slt).");
            }
            DlcRelocate relocate = new DlcRelocate(this.merge, world.Others, ForzaGame.Motorsport4);
            relocate.UsedMediaNames = world.MediaNames;
            relocate.UsedOffers = world.Offers;
            relocate.FileTaken = world.FileTaken;
            relocate.Run();
            this.mergeDirty = true;
            this.Relocation = relocate;
            foreach (string line in relocate.Log)
            {
                this.Log.AppendLine(line);
            }
            foreach (string line in relocate.Unresolved)
            {
                this.Log.AppendLine("NOT MOVED: " + line);
            }
            this.RenameCars(relocate);
            this.CarSounds(relocate, world);
            this.RenameThumbnails(relocate);
            this.RenameOffer(relocate);
        }

        // Car and wheel folders and files under the new MediaName; the text files that name the car follow.
        private void RenameCars(DlcRelocate relocate)
        {
            Encoding latin = Encoding.GetEncoding(28591);
            foreach (KeyValuePair<string, string> pair in relocate.MediaNames)
            {
                string now = pair.Value;
                string lower = now.ToLowerInvariant();
                // The car's own tire zips are called Tire[ABC]<car>.zip.
                Regex name = new Regex("(?:(?<![A-Za-z0-9])|(?<=Tire[ABC]))" + Regex.Escape(pair.Key) + "(?![A-Za-z0-9])", RegexOptions.IgnoreCase);
                MatchEvaluator keep = delegate(Match m) { return (m.Value == m.Value.ToLowerInvariant()) ? lower : now; };
                int renamed = 0;
                int edited = 0;
                foreach (FLayer layer in this.layers)
                {
                    if (layer.IsFolder)
                    {
                        continue;
                    }
                    foreach (ForzaZip.Entry entry in new List<ForzaZip.Entry>(layer.Zip.Entries))
                    {
                        string spelled = name.Replace(entry.Name, keep);
                        if (spelled == entry.Name)
                        {
                            continue;
                        }
                        layer.Zip.Rename(entry, spelled);
                        layer.Dirty = true;
                        renamed++;
                        string extension = Path.GetExtension(spelled).ToLowerInvariant();
                        if (extension == ".xml" || extension == ".txt")
                        {
                            string text = latin.GetString(layer.Zip.Read(entry));
                            string replaced = name.Replace(text, keep);
                            if (replaced != text)
                            {
                                layer.Zip.Replace(entry, latin.GetBytes(replaced));
                                edited++;
                            }
                        }
                    }
                }
                this.Log.AppendLine(string.Format(CultureInfo.InvariantCulture, "files: {0} -> {1}: {2} entries renamed, {3} text files edited", pair.Key, now, renamed, edited));
            }
        }

        // The game finds a car's sound model by the car's name (<car>_CMT.xml): a car under a new MediaName gets the file of the
        // old name (the package's own is renamed with the car; else an installed pack's or the game's), the name replaced in it.
        // The engine's tuning is looked up by the engine's MediaName, which does not change.
        private void CarSounds(DlcRelocate relocate, DlcWorld world)
        {
            Encoding latin = Encoding.GetEncoding(28591);
            Dictionary<string, string> names = this.NamesOf();
            foreach (KeyValuePair<string, string> pair in relocate.MediaNames)
            {
                string file = pair.Value + "_CMT.xml";
                if (names.ContainsKey(file))
                {
                    continue;
                }
                byte[] data = world.ReadCarModelTuning(pair.Key);
                if (data == null)
                {
                    this.Log.AppendLine("sound: " + pair.Key + "_CMT.xml is in neither the package, an installed pack nor the game: " + pair.Value + " has no sound model (the original had none either).");
                    continue;
                }
                string text = Regex.Replace(latin.GetString(data), "(?<![A-Za-z0-9])" + Regex.Escape(pair.Key) + "(?![A-Za-z0-9])", pair.Value, RegexOptions.IgnoreCase);
                this.WriteKey(this.mergeLayer, "Media/audio/Cars/CarModelTuning/" + file, latin.GetBytes(text));
                this.Log.AppendLine("sound: " + file + " made from " + pair.Key + "_CMT.xml");
            }
        }

        private void RenameThumbnails(DlcRelocate relocate)
        {
            foreach (KeyValuePair<string, string> pair in relocate.Thumbnails)
            {
                string from = pair.Key.Replace('\\', '/');
                string to = pair.Value.Replace('\\', '/');
                if (from.StartsWith("game:/", StringComparison.OrdinalIgnoreCase))
                {
                    from = from.Substring(6);
                    to = to.Substring(6);
                }
                string key;
                FLayer layer = this.Locate(from, out key);
                if (layer == null || layer.IsFolder)
                {
                    this.Log.AppendLine("files: thumbnail " + from + " is not in the package (nothing to rename)");
                    continue;
                }
                layer.Zip.Rename(layer.Zip.Find(key), to.Substring(Prefix(layer).Length));
                layer.Dirty = true;
                this.Log.AppendLine("files: " + from + " -> " + to);
            }
        }

        // The offer number is in the names of the package's files: <offer>.puboffer, <offer>_pri_65.zip, StringTables_pri_<50000 + offer>.zip,
        // db\patch\<offer>000_merge.slt, GameTunableSettings\BadgesAndTitles\<offer>.xml and the zipmount.xml line.
        private void RenameOffer(DlcRelocate relocate)
        {
            if (relocate.Offer == relocate.OldOffer || relocate.OldOffer == 0)
            {
                return;
            }
            string was = relocate.OldOffer.ToString(CultureInfo.InvariantCulture);
            string now = relocate.Offer.ToString(CultureInfo.InvariantCulture);
            string oldMerge = this.mergeKey;
            string newMerge = oldMerge.Substring(0, oldMerge.LastIndexOf('/') + 1) + now + "000_merge.slt";
            if (string.Equals(oldMerge.Substring(oldMerge.LastIndexOf('/') + 1), was + "000_merge.slt", StringComparison.OrdinalIgnoreCase))
            {
                this.mergeLayer.Zip.Rename(this.mergeLayer.Zip.Find(oldMerge), newMerge);
                this.mergeLayer.Dirty = true;
                this.mergeKey = newMerge;
                this.Log.AppendLine("files: " + oldMerge.Substring(oldMerge.LastIndexOf('/') + 1) + " -> " + now + "000_merge.slt");
            }
            foreach (FLayer layer in this.layers)
            {
                if (layer.IsFolder)
                {
                    continue;
                }
                foreach (ForzaZip.Entry entry in new List<ForzaZip.Entry>(layer.Zip.Entries))
                {
                    string[] part = entry.Name.Split('/');
                    if (part.Length >= 2 && string.Equals(part[part.Length - 2], "BadgesAndTitles", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(part[part.Length - 1], was + ".xml", StringComparison.OrdinalIgnoreCase))
                    {
                        part[part.Length - 1] = now + ".xml";
                        layer.Zip.Rename(entry, string.Join("/", part));
                        layer.Dirty = true;
                        this.Log.AppendLine("files: BadgesAndTitles\\" + was + ".xml -> " + now + ".xml");
                    }
                }
            }
            string zips = Path.Combine(Path.Combine(this.root, "Media"), "DLCZips");
            string mount = Path.Combine(zips, "zipmount.xml");
            string mountText = File.Exists(mount) ? File.ReadAllText(mount) : null;
            foreach (FLayer layer in this.layers)
            {
                string spelled = null;
                int number = 0;
                int at = layer.Name.LastIndexOf("_pri_", StringComparison.OrdinalIgnoreCase);
                if (layer.Name.StartsWith(was + "_pri_", StringComparison.OrdinalIgnoreCase))
                {
                    spelled = now + layer.Name.Substring(was.Length);
                }
                else if (at > 0 && layer.Name.StartsWith("StringTables", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(Path.GetFileNameWithoutExtension(layer.Name).Substring(at + 5), NumberStyles.None, CultureInfo.InvariantCulture, out number) && number == 50000 + relocate.OldOffer)
                {
                    spelled = layer.Name.Substring(0, at + 5) + (50000 + relocate.Offer).ToString(CultureInfo.InvariantCulture) + Path.GetExtension(layer.Name);
                    layer.Priority = 50000 + relocate.Offer;
                }
                if (spelled == null)
                {
                    continue;
                }
                string path = Path.Combine(zips, spelled);
                if (!layer.IsFolder)
                {
                    this.stale.Add(layer.Path);
                    layer.Dirty = true;
                }
                else
                {
                    Directory.Move(layer.Path, path);
                }
                if (mountText != null)
                {
                    mountText = mountText.Replace("\"" + layer.Name + "\"", "\"" + spelled + "\"");
                }
                this.Log.AppendLine("files: " + layer.Name + " -> " + spelled);
                layer.Name = spelled;
                layer.Path = path;
            }
            if (mountText != null)
            {
                File.WriteAllText(mount, mountText);
            }
            string offer = Path.Combine(Path.Combine(this.root, "Media"), was + ".puboffer");
            if (File.Exists(offer))
            {
                File.Move(offer, Path.Combine(Path.Combine(this.root, "Media"), now + ".puboffer"));
                this.Log.AppendLine("files: " + was + ".puboffer -> " + now + ".puboffer");
            }
        }

        // ----- strings: the Benchmark table -----
        // A pack's string table REPLACES the whole table of lower priority (Data_Car, ContentOffers...), so two packs hide
        // each other's car and pack names. The way out (the Bathurst port, analysed 2026-10-31): ship only Benchmark.str,
        // the base game's 10-string stats-test table, which no database column uses: the base 10 + the Benchmark layers of the
        // installed packs + the pack's own strings under keys free in all of them, all languages. The database then names
        // the strings as "_&" + (0x7CE9 << 16 | key), 0x7CE9 being the id of the Benchmark table.
        private const int BenchmarkId = 0x7CE9;

        private sealed class StringUse
        {
            public string Table;        // the pack's table the string is in
            public int Key;
            public int NewKey;
        }

        public void MoveStrings(DlcWorld world)
        {
            if (this.merge == null)
            {
                throw new InvalidOperationException("The package has no merge database.");
            }
            this.LoadStrings();
            if (this.strings.Count == 0)
            {
                this.Log.AppendLine("strings: the package has no string tables.");
                return;
            }
            string english = null;
            Dictionary<string, bool> shipped = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, Dictionary<string, StrRef>> language in this.strings)
            {
                english = (english == null || language.Key.Equals("EN", StringComparison.OrdinalIgnoreCase)) ? language.Key : english;
                foreach (string table in language.Value.Keys)
                {
                    shipped[table] = true;
                }
            }
            if (shipped.ContainsKey("Benchmark"))
            {
                this.Log.AppendLine("strings: the package already ships Benchmark.str, not changed.");
                return;
            }
            // Every "_&N" in the database: table, column, value.
            List<string[]> refs = new List<string[]>();
            Dictionary<string, List<string>> schema = DbIds.Schema(this.merge);
            foreach (KeyValuePair<string, List<string>> table in schema)
            {
                if (table.Key.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                foreach (string column in table.Value)
                {
                    foreach (object[] row in this.merge.Query("SELECT DISTINCT " + SqliteDatabase.Quote(column) + " FROM " + SqliteDatabase.Quote(table.Key)).Rows)
                    {
                        string text = row[0] as string;
                        uint id;
                        if (text != null && text.StartsWith("_&", StringComparison.Ordinal) && uint.TryParse(text.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out id))
                        {
                            refs.Add(new string[] { table.Key, column, text });
                        }
                    }
                }
            }

            // The table id of a table's strings, learnt from the table's own columns (Data_Car.DisplayName -> Data_Car).
            Dictionary<uint, string> tableOf = new Dictionary<uint, string>();
            foreach (string[] reference in refs)
            {
                uint id = uint.Parse(reference[2].Substring(2), CultureInfo.InvariantCulture);
                if (shipped.ContainsKey(reference[0]) && this.HasString(english, reference[0], (int) (id & 0xFFFF)))
                {
                    tableOf[id >> 16] = reference[0];
                }
            }

            // The strings the package ships and the database uses.
            Dictionary<string, StringUse> uses = new Dictionary<string, StringUse>(StringComparer.Ordinal);
            List<string[]> mine = new List<string[]>();
            foreach (string[] reference in refs)
            {
                uint id = uint.Parse(reference[2].Substring(2), CultureInfo.InvariantCulture);
                string table;
                if (!tableOf.TryGetValue(id >> 16, out table) || !this.HasString(english, table, (int) (id & 0xFFFF)))
                {
                    continue;
                }
                mine.Add(reference);
                string name = table + "/" + (id & 0xFFFF).ToString(CultureInfo.InvariantCulture);
                if (!uses.ContainsKey(name))
                {
                    StringUse use = new StringUse();
                    use.Table = table;
                    use.Key = (int) (id & 0xFFFF);
                    uses[name] = use;
                }
            }
            if (uses.Count == 0)
            {
                this.Log.AppendLine("strings: the database uses none of the package's own strings.");
                return;
            }

            // The Benchmark table of every language, and the keys free in all of them.
            Dictionary<string, StrFile> benchmark = new Dictionary<string, StrFile>(StringComparer.OrdinalIgnoreCase);
            Dictionary<int, bool> taken = new Dictionary<int, bool>();
            StrFile fallback = world.Benchmark(english) ?? world.Benchmark("EN");
            if (fallback == null)
            {
                this.Log.AppendLine("strings: the game has no Benchmark.str to build on; strings left as they are.");
                return;
            }
            foreach (string language in this.strings.Keys)
            {
                StrFile file = world.Benchmark(language) ?? new StrFile(fallback.ToBytes());
                benchmark[language] = file;
                foreach (StrFile.Entry entry in file.Entries)
                {
                    taken[entry.Key] = true;
                }
            }
            List<StringUse> order = new List<StringUse>(uses.Values);
            order.Sort(delegate(StringUse a, StringUse b) { return a.Key.CompareTo(b.Key); });
            int search = 0x4000;
            foreach (StringUse use in order)
            {
                int key = use.Key;
                if (taken.ContainsKey(key))
                {
                    key = search;
                    while (taken.ContainsKey(key))
                    {
                        key++;
                    }
                    search = key + 1;
                }
                taken[key] = true;
                use.NewKey = key;
            }
            foreach (KeyValuePair<string, StrFile> language in benchmark)
            {
                foreach (StringUse use in order)
                {
                    StrFile.Entry text = this.EntryOfString(language.Key, use.Table, use.Key) ?? this.EntryOfString(english, use.Table, use.Key);
                    language.Value.Set(use.NewKey, text.Text, text.Name);
                }
            }

            // The database.
            int changed = 0;
            foreach (string[] reference in mine)
            {
                uint id = uint.Parse(reference[2].Substring(2), CultureInfo.InvariantCulture);
                string table = tableOf[id >> 16];
                StringUse use = uses[table + "/" + (id & 0xFFFF).ToString(CultureInfo.InvariantCulture)];
                string value = "_&" + ((((uint) BenchmarkId) << 16) | (uint) use.NewKey).ToString(CultureInfo.InvariantCulture);
                changed += this.merge.Execute("UPDATE " + SqliteDatabase.Quote(reference[0]) + " SET " + SqliteDatabase.Quote(reference[1]) + " = ? WHERE " + SqliteDatabase.Quote(reference[1]) + " = ?", value, reference[2]);
            }
            this.mergeDirty = true;

            // The string layer: the tables the database no longer uses go, Benchmark.str comes.
            Dictionary<string, bool> moved = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (StringUse use in order)
            {
                moved[use.Table] = true;
            }
            FLayer layer = null;
            foreach (KeyValuePair<string, Dictionary<string, StrRef>> language in this.strings)
            {
                string folder = null;
                foreach (string table in new List<string>(language.Value.Keys))
                {
                    if (!moved.ContainsKey(table))
                    {
                        continue;
                    }
                    StrRef reference = language.Value[table];
                    layer = reference.Layer;
                    folder = reference.Entry.Name.Substring(0, reference.Entry.Name.LastIndexOf('/') + 1);
                    layer.Zip.MarkDeleted(reference.Entry);
                    layer.Dirty = true;
                    language.Value.Remove(table);
                }
                if (folder != null)
                {
                    layer.Zip.Add(folder + "Benchmark.str", benchmark[language.Key].ToBytes());
                }
            }
            if (layer != null)
            {
                this.WriteStringTableList(moved);
            }
            foreach (string table in shipped.Keys)
            {
                if (!moved.ContainsKey(table))
                {
                    this.Log.AppendLine("strings: the package still ships the table " + table + " (the database uses none of its strings): it hides the same table of other packs.");
                }
            }
            this.Log.AppendLine(string.Format(CultureInfo.InvariantCulture, "strings: {0} strings of {1} table(s) now in Benchmark.str ({2} languages, keys {3}), {4} database values changed; the tables {5} are no longer shipped", order.Count, moved.Count, benchmark.Count, DescribeKeys(order), changed, string.Join(", ", new List<string>(moved.Keys).ToArray())));
        }

        // Repairs a pack built before DlcPackage moved clashing string keys: its own strings sit on keys that a pack of lower
        // priority uses for other strings, so the pack's table (which replaces the whole table) hides that pack's names
        // (Evora S on every car). 'installed' are the layers of the other packs (DlcPackage.StringLayer.Installed); the
        // ones below this pack's own priority count. Each clashing key of the pack moves to a free key, the rows follow,
        // and the table gets the lower packs' strings back. Returns the number of keys moved.
        public int RekeyStrings(string content)
        {
            string layerTemp = Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "StringLayers_" + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture));
            try
            {
                StringBuilder report = new StringBuilder();
                List<DlcPackage.StringLayer> installed = DlcPackage.StringLayer.Installed(content, layerTemp, report);
                this.Log.Append(report.ToString());
                return this.RekeyStrings(installed);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(layerTemp))
                    {
                        Directory.Delete(layerTemp, true);
                    }
                }
                catch (IOException)
                {
                }
            }
        }

        internal int RekeyStrings(List<DlcPackage.StringLayer> installed)
        {
            this.OpenMerge();
            if (this.merge == null)
            {
                throw new InvalidOperationException("The package has no merge database.");
            }
            this.LoadStrings();
            int priority = -1;
            foreach (FLayer layer in this.layers)
            {
                if (!layer.IsFolder && layer.Name.StartsWith("StringTables", StringComparison.OrdinalIgnoreCase))
                {
                    priority = Math.Max(priority, layer.Priority);
                }
            }
            if (priority < 0 || this.strings.Count == 0)
            {
                this.Log.AppendLine("strings: the package has no string tables.");
                return 0;
            }
            List<DlcPackage.StringLayer> lower = installed.FindAll(delegate(DlcPackage.StringLayer l) { return l.Priority < priority; });
            string english = this.strings.ContainsKey("EN") ? "EN" : new List<string>(this.strings.Keys)[0];

            // Every "_&" value of the database; which table a table id is (the table whose own strings most of its
            // values name), and the keys of each table the rows use.
            List<string[]> refs = new List<string[]>();
            Dictionary<uint, Dictionary<string, int>> votes = new Dictionary<uint, Dictionary<string, int>>();
            foreach (KeyValuePair<string, List<string>> table in DbIds.Schema(this.merge))
            {
                if (table.Key.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                foreach (string column in table.Value)
                {
                    foreach (object[] row in this.merge.Query("SELECT DISTINCT " + SqliteDatabase.Quote(column) + " FROM " + SqliteDatabase.Quote(table.Key) + " WHERE " + SqliteDatabase.Quote(column) + " LIKE '\\_&%' ESCAPE '\\'").Rows)
                    {
                        uint id;
                        string text = row[0] as string;
                        if (text == null || !uint.TryParse(text.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out id))
                        {
                            continue;
                        }
                        refs.Add(new string[] { table.Key, text });
                        if (this.HasString(english, table.Key, (int) (id & 0xFFFF)))
                        {
                            Dictionary<string, int> counts;
                            if (!votes.TryGetValue(id >> 16, out counts))
                            {
                                counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                                votes[id >> 16] = counts;
                            }
                            int n;
                            counts.TryGetValue(table.Key, out n);
                            counts[table.Key] = n + 1;
                        }
                    }
                }
            }
            Dictionary<uint, string> tableOf = new Dictionary<uint, string>();
            foreach (KeyValuePair<uint, Dictionary<string, int>> vote in votes)
            {
                int best = 0;
                foreach (KeyValuePair<string, int> count in vote.Value)
                {
                    if (count.Value > best)
                    {
                        best = count.Value;
                        tableOf[vote.Key] = count.Key;
                    }
                }
            }
            Dictionary<string, List<uint>> mine = new Dictionary<string, List<uint>>(StringComparer.OrdinalIgnoreCase);
            foreach (string[] reference in refs)
            {
                uint id = uint.Parse(reference[1].Substring(2), CultureInfo.InvariantCulture);
                string owner;
                if (!tableOf.TryGetValue(id >> 16, out owner) || !this.HasString(english, owner, (int) (id & 0xFFFF)))
                {
                    continue;
                }
                List<uint> keys;
                if (!mine.TryGetValue(owner, out keys))
                {
                    keys = new List<uint>();
                    mine[owner] = keys;
                }
                if (!keys.Contains(id & 0xFFFF))
                {
                    keys.Add(id & 0xFFFF);
                }
            }

            Dictionary<string, int> remap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<uint>> owned in mine)
            {
                string table = owned.Key;
                // The strings of the lower packs, per language.
                Dictionary<string, StrFile> below = new Dictionary<string, StrFile>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, Dictionary<string, StrRef>> language in this.strings)
                {
                    StrRef reference;
                    if (!language.Value.TryGetValue(table, out reference))
                    {
                        continue;
                    }
                    StrFile empty = new StrFile(this.Str(reference).ToBytes());
                    foreach (StrFile.Entry entry in new List<StrFile.Entry>(empty.Entries))
                    {
                        empty.Remove(entry.Key);
                    }
                    DlcPackage.StringLayer.Merge(empty, lower, language.Key, table + ".str");
                    below[language.Key] = empty;
                }
                if (!below.ContainsKey(english))
                {
                    continue;
                }
                // The keys that clash (English decides; the other languages follow).
                StrFile ownEnglish = this.Str(this.strings[english][table]);
                Dictionary<int, int> moves = new Dictionary<int, int>();
                foreach (uint key in owned.Value)
                {
                    StrFile.Entry own = EntryOf(ownEnglish, (int) key);
                    StrFile.Entry theirs = EntryOf(below[english], (int) key);
                    if (own == null || theirs == null || DlcPackage.SameString(own, theirs))
                    {
                        continue;
                    }
                    int free = 0xFFFD;
                    while (free > 0)
                    {
                        bool used = moves.ContainsValue(free) || owned.Value.Contains((uint) free);
                        foreach (KeyValuePair<string, StrFile> language in below)
                        {
                            used = used || language.Value.Contains(free) || this.Str(this.strings[language.Key][table]).Contains(free);
                        }
                        if (!used)
                        {
                            break;
                        }
                        free--;
                    }
                    if (free <= 0)
                    {
                        throw new InvalidOperationException(table + ".str has no free string key.");
                    }
                    moves[(int) key] = free;
                    remap[table + "|" + key.ToString(CultureInfo.InvariantCulture)] = free;
                    this.Log.AppendLine(string.Format(CultureInfo.InvariantCulture, "strings: {0} key {1:X4} is \"{2}\" in a lower pack, \"{3}\" of this pack moves to {4:X4}", table, key, theirs.Text, own.Text, free));
                }
                if (moves.Count == 0 && below[english].Entries.Count == 0)
                {
                    continue;
                }
                // The table: the pack's strings on their new keys, then every string of the lower packs back.
                foreach (KeyValuePair<string, StrFile> language in below)
                {
                    StrRef reference = this.strings[language.Key][table];
                    StrFile file = this.Str(reference);
                    foreach (KeyValuePair<int, int> move in moves)
                    {
                        StrFile.Entry own = EntryOf(file, move.Key);
                        if (own != null)
                        {
                            file.Set(move.Value, own.Text, own.Name);
                        }
                    }
                    foreach (StrFile.Entry text in language.Value.Entries)
                    {
                        if (!moves.ContainsKey(text.Key) && owned.Value.Contains((uint) text.Key))
                        {
                            continue;
                        }
                        StrFile.Entry now = EntryOf(file, text.Key);
                        if (now == null || now.Text != text.Text || now.Name != text.Name)
                        {
                            file.Set(text.Key, text.Text, text.Name);
                        }
                    }
                    reference.Dirty = true;
                }
            }
            if (remap.Count > 0)
            {
                StringBuilder report = new StringBuilder();
                DlcPackage.RemapStrings(this.merge, remap, report);
                this.Log.Append(report.ToString());
                this.mergeDirty = true;
            }
            this.Log.AppendLine(string.Format(CultureInfo.InvariantCulture, "strings: {0} key(s) moved, the tables hold the strings of the {1} lower layer(s) again", remap.Count, lower.Count));
            return remap.Count;
        }

        private static string DescribeKeys(List<StringUse> order)
        {
            List<string> text = new List<string>();
            foreach (StringUse use in order)
            {
                text.Add(use.Key == use.NewKey ? use.Key.ToString(CultureInfo.InvariantCulture) : use.Key.ToString(CultureInfo.InvariantCulture) + "->" + use.NewKey.ToString(CultureInfo.InvariantCulture));
                if (text.Count == 6)
                {
                    text.Add("...");
                    break;
                }
            }
            return string.Join(" ", text.ToArray());
        }

        private StrFile.Entry EntryOfString(string language, string table, int key)
        {
            Dictionary<string, StrRef> tables;
            StrRef reference;
            if (!this.strings.TryGetValue(language, out tables) || !tables.TryGetValue(table, out reference))
            {
                return null;
            }
            return EntryOf(this.Str(reference), key);
        }

        private bool HasString(string language, string table, int key)
        {
            return this.EntryOfString(language, table, key) != null;
        }

        // stringtables.xml lists the tables the package replaces: the moved ones go, Benchmark comes.
        private void WriteStringTableList(Dictionary<string, bool> moved)
        {
            string path = Path.Combine(Path.Combine(Path.Combine(this.root, "Media"), "DLCZips"), "stringtables.xml");
            List<string> names = new List<string>();
            if (File.Exists(path))
            {
                System.Xml.XmlDocument xml = new System.Xml.XmlDocument();
                xml.Load(path);
                foreach (System.Xml.XmlElement table in xml.SelectNodes("/stringtables/table"))
                {
                    string name = table.GetAttribute("Name");
                    if (!moved.ContainsKey(name))
                    {
                        names.Add(name);
                    }
                }
            }
            names.Add("Benchmark");
            StringBuilder text = new StringBuilder();
            text.Append("<?xml version=\"1.0\" ?> \r\n<stringtables> \r\n");
            foreach (string name in names)
            {
                text.Append("   <table Name=\"" + name + "\" /> \r\n");
            }
            text.Append("</stringtables> \r\n");
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        }

        // The package's merge database as it is now.
        public byte[] MergeBytes()
        {
            return this.merge.ToBytes();
        }

        // Writes the changed layers and the database into the extracted copy and packs it as an STFS file.
        public void SaveTo(string output)
        {
            if (!this.isFile)
            {
                throw new InvalidOperationException("Only a package file can be installed this way (not an extracted folder).");
            }
            if (this.mergeDirty && this.merge != null)
            {
                this.WriteKey(this.mergeLayer, this.mergeKey, this.merge.ToBytes());
            }
            if (this.strings != null)
            {
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
            }
            foreach (FLayer layer in this.layers)
            {
                if (!layer.IsFolder && layer.Dirty)
                {
                    layer.Zip.Save(layer.Path);
                    this.Log.AppendLine("saved " + layer.Name);
                }
            }
            foreach (string old in this.stale)
            {
                if (File.Exists(old))
                {
                    File.Delete(old);
                }
            }
            this.stale.Clear();
            string partial = output + ".part";
            try
            {
                StfsPackage.Rewrite(this.root, partial, this.package.Header);
                StfsReader check = StfsReader.Open(partial);
                if (check.Files().Count == 0)
                {
                    throw new InvalidOperationException("The rewritten package is empty.");
                }
                if (File.Exists(output))
                {
                    File.Delete(output);
                }
                File.Move(partial, output);
            }
            finally
            {
                if (File.Exists(partial))
                {
                    File.Delete(partial);
                }
            }
            this.Log.AppendLine("package written: " + output);
        }
    }
}

