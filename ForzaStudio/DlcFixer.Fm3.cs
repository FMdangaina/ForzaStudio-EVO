namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;

    // "Validate DLC for installation" for a Forza Motorsport 3 package (2026-11-02). The same steps as FM4's (DlcRelocate moves the ids,
    // the files follow), with FM3's own offer and string rules, read from the official packs:
    //   offer     the MultiDiscContentPacks row (ID 4D53084D0CCF<offer, 4 hex digits>); the files carry it: <hex4>_pri_65(.zip),
    //             db\patch\<offer>005_merge.slt and <offer>000_execute.sql (decimal), StringTables_pri_<priority>.zip;
    //   cars      Media\LicenseMasks\*.sql and db\patch\*_execute.sql list the cars' Ids: they follow the Ids DlcRelocate gave;
    //   strings   a pack's string table replaces the whole table of lower priority, so the table is rebuilt: the game's + every installed
    //             pack's + the pack's own strings, a key an installed pack uses for another text moves to a free key (the rows follow),
    //             and the layer goes above every installed one.
    public sealed partial class DlcFixer
    {
        public ForzaGame Game = ForzaGame.Motorsport4;

        private bool Fm3
        {
            get { return this.Game == ForzaGame.Motorsport3; }
        }

        // Offer 22 -> the files named after it.
        private void RenameOfferFm3(DlcRelocate relocate)
        {
            if (relocate.Offer == relocate.OldOffer || relocate.OldOffer == 0)
            {
                return;
            }
            string was = relocate.OldOffer.ToString(CultureInfo.InvariantCulture);
            string now = relocate.Offer.ToString(CultureInfo.InvariantCulture);
            foreach (string suffix in new string[] { "005_merge.slt", "000_execute.sql" })
            {
                foreach (FLayer layer in this.layers)
                {
                    if (layer.IsFolder)
                    {
                        continue;
                    }
                    foreach (ForzaZip.Entry entry in new List<ForzaZip.Entry>(layer.Zip.Entries))
                    {
                        string name = entry.Name.Replace('\\', '/');
                        string leaf = name.Substring(name.LastIndexOf('/') + 1);
                        if (!string.Equals(leaf, was + suffix, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        string renamed = name.Substring(0, name.LastIndexOf('/') + 1) + now + suffix;
                        layer.Zip.Rename(entry, renamed);
                        layer.Dirty = true;
                        if (suffix == "005_merge.slt")
                        {
                            this.mergeKey = renamed;
                        }
                        this.Log.AppendLine("files: " + was + suffix + " -> " + now + suffix);
                    }
                }
            }
            string oldHex = relocate.OldOffer.ToString("X4", CultureInfo.InvariantCulture);
            string newHex = relocate.Offer.ToString("X4", CultureInfo.InvariantCulture);
            string zips = Path.Combine(Path.Combine(this.root, "Media"), "DLCZips");
            string mount = Path.Combine(zips, "zipmount.xml");
            string mountText = File.Exists(mount) ? File.ReadAllText(mount) : null;
            foreach (FLayer layer in this.layers)
            {
                if (!layer.Name.StartsWith(oldHex + "_pri_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                this.RenameLayer(layer, newHex + layer.Name.Substring(oldHex.Length), ref mountText);
            }
            if (mountText != null)
            {
                File.WriteAllText(mount, mountText);
            }
        }

        private void RenameLayer(FLayer layer, string spelled, ref string mountText)
        {
            string zips = Path.Combine(Path.Combine(this.root, "Media"), "DLCZips");
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

        // The cars' Ids in Media\LicenseMasks\*.sql (outside the layers) and in the layers' db\patch\*_execute.sql follow the new Ids.
        private void FixCarIdsFm3(DlcRelocate relocate)
        {
            if (relocate.CarIds.Count == 0)
            {
                return;
            }
            string masks = Path.Combine(Path.Combine(this.root, "Media"), "LicenseMasks");
            if (Directory.Exists(masks))
            {
                foreach (string file in Directory.GetFiles(masks, "*.sql"))
                {
                    string text = File.ReadAllText(file);
                    string changed = MapIds(text, relocate.CarIds);
                    if (changed != text)
                    {
                        File.WriteAllText(file, changed, Encoding.ASCII);
                        this.Log.AppendLine("files: LicenseMasks\\" + Path.GetFileName(file) + ": car Ids follow the database");
                    }
                }
            }
            foreach (FLayer layer in this.layers)
            {
                foreach (string key in this.Keys(layer))
                {
                    if (!key.EndsWith("_execute.sql", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    string text = Encoding.ASCII.GetString(this.ReadKey(layer, key));
                    string changed = MapIds(text, relocate.CarIds);
                    if (changed != text)
                    {
                        this.WriteKey(layer, key, Encoding.ASCII.GetBytes(changed));
                        this.Log.AppendLine("files: " + key + ": car Ids follow the database");
                    }
                }
            }
        }

        // The numbers inside "IN (...)" lists: an old Id becomes the new one.
        private static string MapIds(string sql, Dictionary<long, long> ids)
        {
            return Regex.Replace(sql, @"IN\s*\(([^)]*)\)", delegate(Match list)
            {
                string[] parts = list.Groups[1].Value.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    long id;
                    long now;
                    if (long.TryParse(parts[i].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out id) && ids.TryGetValue(id, out now))
                    {
                        parts[i] = now.ToString(CultureInfo.InvariantCulture);
                    }
                }
                return "IN (" + string.Join(",", parts) + ")";
            }, RegexOptions.IgnoreCase);
        }

        // The pack's string tables rebuilt on the game's and the installed packs' (see the class comment).
        private void MoveStringsFm3(DlcWorld world)
        {
            List<FLayer> stringLayers = this.layers.FindAll(delegate(FLayer l) { return l.Name.StartsWith("StringTables", StringComparison.OrdinalIgnoreCase); });
            if (stringLayers.Count == 0)
            {
                this.Log.AppendLine("strings: the package has no string tables.");
                return;
            }
            Regex entryName = new Regex(@"^(?<lang>[^/]+)/(?<table>[^/]+)\.str$", RegexOptions.IgnoreCase);
            // Pass 1: every language's pack table and the merged table below it; the keys that clash.
            Dictionary<string, bool> clash = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);     // table|key
            Dictionary<string, Dictionary<int, bool>> usedKeys = new Dictionary<string, Dictionary<int, bool>>(StringComparer.OrdinalIgnoreCase);   // table -> keys used anywhere
            List<string[]> work = new List<string[]>();     // layer name, entry key, language, table
            foreach (FLayer layer in stringLayers)
            {
                foreach (string key in this.Keys(layer))
                {
                    Match m = entryName.Match(key);
                    if (!m.Success)
                    {
                        continue;
                    }
                    string language = m.Groups["lang"].Value;
                    string table = m.Groups["table"].Value;
                    StrFile own = new StrFile(this.ReadKey(layer, key));
                    Dictionary<int, List<string>> known = world.KnownTexts(language, table);
                    Dictionary<int, bool> used;
                    if (!usedKeys.TryGetValue(table, out used))
                    {
                        used = new Dictionary<int, bool>();
                        usedKeys[table] = used;
                    }
                    foreach (StrFile.Entry text in own.Entries)
                    {
                        used[text.Key] = true;
                        List<string> texts;
                        if (known.TryGetValue(text.Key, out texts) && !texts.Contains(text.Text))
                        {
                            clash[table + "|" + text.Key.ToString(CultureInfo.InvariantCulture)] = true;
                        }
                    }
                    foreach (int key2 in known.Keys)
                    {
                        used[key2] = true;
                    }
                    work.Add(new string[] { layer.Name, key, language, table });
                }
            }
            Dictionary<string, int> remap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string item in new List<string>(clash.Keys))
            {
                string table = item.Substring(0, item.IndexOf('|'));
                int old = int.Parse(item.Substring(item.IndexOf('|') + 1), CultureInfo.InvariantCulture);
                Dictionary<int, bool> used = usedKeys[table];
                int free = 0xFFFD;
                while (free > 0 && used.ContainsKey(free))
                {
                    free--;
                }
                if (free == 0)
                {
                    throw new InvalidOperationException(table + ".str has no free string key.");
                }
                used[free] = true;
                remap[item] = free;
                this.Log.AppendLine(string.Format(CultureInfo.InvariantCulture, "strings: {0} key {1:X4} is another text in the game or an installed pack: this pack's string moves to {2:X4}", table, old, free));
            }
            // Pass 2: the tables written, the pack's own strings (the ones the game and the packs below do not have) on top.
            int own2 = 0;
            foreach (string[] item in work)
            {
                FLayer layer = stringLayers.Find(delegate(FLayer l) { return l.Name == item[0]; });
                StrFile pack = new StrFile(this.ReadKey(layer, item[1]));
                StrFile result = world.StringTable(item[2], item[3]) ?? new StrFile(pack.ToBytes());
                Dictionary<int, List<string>> known = world.KnownTexts(item[2], item[3]);
                foreach (StrFile.Entry text in pack.Entries)
                {
                    int key = text.Key;
                    int moved;
                    if (remap.TryGetValue(item[3] + "|" + key.ToString(CultureInfo.InvariantCulture), out moved))
                    {
                        key = moved;
                    }
                    List<string> texts;
                    if (moved != 0 || !known.TryGetValue(key, out texts) || !texts.Contains(text.Text))
                    {
                        result.Set(key, text.Text, text.Name);
                        own2++;
                    }
                }
                this.WriteKey(layer, item[1], result.ToBytes());
            }
            if (remap.Count > 0)
            {
                this.OpenMerge();
                DlcPackage.RemapStrings(this.merge, remap, this.Log);
                this.mergeDirty = true;
            }
            // The layer above every installed one.
            int top = world.TopStringPriority();
            foreach (FLayer layer in stringLayers)
            {
                if (layer.Priority > top)
                {
                    continue;
                }
                int priority = top + 1;
                int at = layer.Name.LastIndexOf("_pri_", StringComparison.OrdinalIgnoreCase);
                string spelled = layer.Name.Substring(0, at + 5) + priority.ToString(CultureInfo.InvariantCulture) + Path.GetExtension(layer.Name);
                string mount = Path.Combine(Path.Combine(Path.Combine(this.root, "Media"), "DLCZips"), "zipmount.xml");
                string mountText = File.Exists(mount) ? File.ReadAllText(mount) : null;
                layer.Priority = priority;
                this.RenameLayer(layer, spelled, ref mountText);
                if (mountText != null)
                {
                    File.WriteAllText(mount, mountText);
                }
            }
            this.Log.AppendLine(string.Format(CultureInfo.InvariantCulture, "strings: {0} table(s) rebuilt on the game's and {1} installed pack(s)' strings, {2} string(s) of the pack on top.", work.Count, world.StringLayers.Count, own2));
        }
    }
}
