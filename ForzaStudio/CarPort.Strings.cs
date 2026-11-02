namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Xml;

    // The car's names and titles in the string tables.
    public partial class CarPort
    {
        #region Strings

        // DisplayName / ModelShort are "_&<id>": the low 16 bits are the key in Data_Car.str. FH2 and
        // FM4 numbered their strings apart, so an FH2 key can be another car's in FM4 (14 of the 73
        // FH2 cars FM4 lacks: FOR_TransitSSV_11's DDD8 is IDS_DisplayName_1076 there). Such a string
        // gets a key free in every language, and the database value follows it (same high bits); a
        // car ported before finds its string again by name (IDS_...).
        private void PortStrings()
        {
            // The packs installed in Xenia's content folder hold string keys too (a pack's table is a layer over the game's):
            // a new key goes above everything the game and those packs use.
            string layerTemp = Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "StringLayers_" + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture));
            try
            {
                List<DlcPackage.StringLayer> layers = DlcPackage.StringLayer.Installed(this.XeniaContent, layerTemp, new StringBuilder());
                this.PortStrings("Data_Car", "Id", this.carId, layers, "DisplayName", "ModelShort");
                if (this.newMake.HasValue)
                {
                    this.PortStrings("List_CarMake", "ID", this.newMake.Value, layers, "DisplayName", "Profile");
                }
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

        // The titles of a make the port added (MakeTitles): FM4's "<Make> Fan / Enthusiast / Aficionado" for it,
        // the unlock rows and strings now, the rule with the files (WriteTitleRule).
        private void PortTitles()
        {
            if (!this.newMake.HasValue)
            {
                return;
            }
            string logoBase = this.target.Scalar("SELECT IconPathBase FROM List_CarMake WHERE ID = ?", this.newMake.Value) as string;
            Dictionary<string, ForzaZip> zips = new Dictionary<string, ForzaZip>(StringComparer.OrdinalIgnoreCase);
            foreach (string zip in Directory.GetFiles(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "StringTables"), "*.zip"))
            {
                ForzaZip archive = this.OpenChanged(zip);
                if (archive.Find(MakeTitles.Table + ".str") != null)
                {
                    zips[zip] = archive;
                }
            }
            string name = null;
            string reference = this.target.Scalar("SELECT DisplayName FROM List_CarMake WHERE ID = ?", this.newMake.Value) as string;
            uint id;
            if (reference != null && reference.StartsWith("_&") && uint.TryParse(reference.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out id))
            {
                string english = Path.Combine(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "StringTables"), "EN.zip");
                ForzaZip.Entry table = this.OpenChanged(english).Find("List_CarMake.str");
                StrFile.Entry text = (table == null) ? null : Find(new StrFile(this.OpenChanged(english).Read(table)), (int) (id & 0xFFFF));
                name = (text == null) ? null : text.Text;
            }
            if (string.IsNullOrEmpty(logoBase) || string.IsNullOrEmpty(name))
            {
                this.Line("  titles: the make has no logo name or English name, none added");
                return;
            }
            StringBuilder lines = new StringBuilder();
            this.titleRule = MakeTitles.Add(this.target, this.newMake.Value, name, logoBase, zips, lines);
            this.titleBase = logoBase;
            foreach (string line in lines.ToString().Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                this.Line("{0}", line);
            }
            this.PortDecals(name);
        }

        // The livery decals of a make the port added (MakeDecals): when PortMake kept no Livery_DecalsSortOrder row (the
        // source's rows name decals FM4 lacks, as FH2's Bowler: its own logo decal), the make gets the common decals
        // and its own decal, the source's texture put on FM4's decal carbin. Without the source's decal file
        // nothing is added.
        private void PortDecals(string name)
        {
            if (!MakeDecals.Supported(this.target) || !MakeDecals.Supported(this.source)
                || Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM " + MakeDecals.Sort + " WHERE MakeID = ?", this.newMake.Value)) > 0)
            {
                return;
            }
            string file = this.source.Scalar("SELECT d.Filename FROM " + MakeDecals.Sort + " s JOIN " + MakeDecals.Table + " d ON d.ID = s.Livery_DecalID WHERE s.MakeID = ? AND s.Sequence = 1", this.newMake.Value) as string;
            string sourceLivery = MakeDecals.LiveryPath(this.SourceRoot);
            if (file == null || !File.Exists(sourceLivery))
            {
                this.Line("  decals: the source has no decal of its own for the make, none added");
                return;
            }
            ForzaZip from = new ForzaZip(sourceLivery);
            ForzaZip.Entry own = from.Find("decals/" + file.Substring(file.LastIndexOfAny(new char[] { '\\', '/' }) + 1));
            string livery = MakeDecals.LiveryPath(this.TargetRoot);
            if (own == null || !File.Exists(livery))
            {
                this.Line("  decals: the source's decal {0} is not in its Livery.zip, none added", file);
                return;
            }
            ForzaZip target = this.OpenChanged(livery);
            ForzaZip.Entry model = target.Find(MakeDecals.TemplateEntry);
            if (model == null)
            {
                this.Line("  decals: Livery.zip has no {0} to build the decal on, none added", MakeDecals.TemplateEntry);
                return;
            }
            byte[] carbin = MakeDecals.BuildCarbin(MakeDecals.ReadTexels(from.Read(own)), target.Read(model));
            Dictionary<string, ForzaZip> zips = new Dictionary<string, ForzaZip>(StringComparer.OrdinalIgnoreCase);
            foreach (string zip in Directory.GetFiles(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "StringTables"), "*.zip"))
            {
                zips[zip] = this.OpenChanged(zip);
            }
            StringBuilder lines = new StringBuilder();
            this.Put(target, MakeDecals.Add(this.target, this.newMake.Value, name, target, zips, lines), carbin);
            foreach (string line in lines.ToString().Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                this.Line("{0}", line);
            }
        }

        // The rule of PortTitles: added to the game's BadgeAndTitleRules.xml (backed up), which is the file the game
        // reads; in a trial folder a file of its own, which the pack build joins into the pack's <offer>.xml.
        private void WriteTitleRule()
        {
            if (this.OutputRoot == null)
            {
                string rules = MakeTitles.GameRules(this.TargetRoot);
                string text = File.ReadAllText(rules);
                if (text.IndexOf("id=\"Drive_" + this.titleBase + "\"", StringComparison.Ordinal) >= 0)
                {
                    this.Line("titles: {0} has Drive_{1} already", MakeTitles.RulesFile, this.titleBase);
                    return;
                }
                this.Write(rules, new UTF8Encoding(false).GetBytes(MakeTitles.AddRule(text, this.titleRule)));
                return;
            }
            this.Write(Path.Combine(MakeTitles.RulesFolder(this.TargetRoot), this.titleBase + ".xml"), new UTF8Encoding(false).GetBytes(MakeTitles.Document(new string[] { this.titleRule })));
        }

        // The strings of one row (table.keyColumn = id) into <table>.str of every FM4 language; a
        // language the source lacks gets the source's EN text (FM3 has no BR, GB, NL).
        private void PortStrings(string table, string keyColumn, long id, List<DlcPackage.StringLayer> layers, params string[] columns)
        {
            Dictionary<int, string> installedKeys = new Dictionary<int, string>();
            foreach (DlcPackage.StringLayer layer in layers)
            {
                layer.Collect(table + ".str", installedKeys);
            }
            string targetTables = Path.Combine(Path.Combine(this.TargetRoot, "Media"), "StringTables");
            string sourceTables = Path.Combine(Path.Combine(this.SourceRoot, "Media"), "StringTables");
            string strName = table + ".str";
            List<string> zips = new List<string>();
            Dictionary<string, StrFile> sourceFiles = new Dictionary<string, StrFile>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, StrFile> targetFiles = new Dictionary<string, StrFile>(StringComparer.OrdinalIgnoreCase);
            foreach (string targetZip in Directory.GetFiles(targetTables, "*.zip"))
            {
                string sourceZip = Path.Combine(sourceTables, Path.GetFileName(targetZip));
                if (!File.Exists(sourceZip))
                {
                    sourceZip = Path.Combine(sourceTables, "EN.zip");
                }
                ForzaZip sourceArchive = new ForzaZip(sourceZip);
                ForzaZip.Entry sourceEntry = sourceArchive.Find(strName);
                ForzaZip archive = this.OpenChanged(targetZip);
                ForzaZip.Entry entry = archive.Find(strName);
                if (sourceEntry == null || entry == null)
                {
                    continue;
                }
                zips.Add(targetZip);
                sourceFiles[targetZip] = new StrFile(sourceArchive.Read(sourceEntry));
                targetFiles[targetZip] = new StrFile(archive.Read(entry));
            }

            int keys = 0;
            string rowWhere = SqliteDatabase.Quote(keyColumn) + " = ?";
            foreach (string column in columns)
            {
                object value = this.source.Scalar("SELECT " + column + " FROM " + table + " WHERE " + rowWhere, id);
                uint stringId;
                if (!(value is string) || !((string) value).StartsWith("_&") || !uint.TryParse(((string) value).Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out stringId))
                {
                    continue;
                }
                int key = (int) (stringId & 0xFFFF);
                string name = null;
                foreach (string zip in zips)
                {
                    StrFile.Entry text = Find(sourceFiles[zip], key);
                    if (text != null && text.Name != null)
                    {
                        name = text.Name;
                        break;
                    }
                }
                // A renamed car (beside FM4's own) never reuses FM4's string of that name: it gets a key of its own.
                bool renamed = this.RenameTo != null && table == "Data_Car";
                int newKey = this.StringKey(targetFiles, installedKeys, key, renamed ? null : name);                uint high = this.TargetStringHigh(table, column, rowWhere, id, stringId & 0xFFFF0000u);
                if (high != (stringId & 0xFFFF0000u))
                {
                    this.Line("  {0}.{1}: string table id {2:X4} is FM4's {3:X4}", table, column, stringId >> 16, high >> 16);
                }
                if (newKey != key || high != (stringId & 0xFFFF0000u))
                {
                    string newValue = "_&" + (high | (uint) newKey).ToString(CultureInfo.InvariantCulture);
                    this.target.Execute("UPDATE " + table + " SET " + SqliteDatabase.Quote(column) + " = ? WHERE " + rowWhere, newValue, id);
                    if (table == "List_CarMake" && column == "DisplayName")
                    {
                        // Data_Car.MakeName is the make's own string.
                        this.target.Execute("UPDATE Data_Car SET MakeName = ? WHERE MakeID = ?", newValue, id);
                    }
                    if (newKey != key)
                    {
                        this.Line("  {0}.{1}: string key {2:X4} is another row's in FM4, {3:X4} used ({4})", table, column, key, newKey, newValue);
                    }
                }
                foreach (string zip in zips)
                {
                    StrFile.Entry text = Find(sourceFiles[zip], key);
                    if (text != null)
                    {
                        targetFiles[zip].Set(newKey, renamed ? text.Text + this.DisplaySuffix : text.Text, text.Name);
                    }
                }
                keys++;
            }
            foreach (string zip in zips)
            {
                ForzaZip archive = this.OpenChanged(zip);
                archive.Replace(archive.Find(strName), targetFiles[zip].ToBytes());
            }
            this.Line("strings: {0} keys in {1} of {2} languages", keys, strName, zips.Count);
        }

        private static StrFile.Entry Find(StrFile file, int key)
        {
            foreach (StrFile.Entry entry in file.Entries)
            {
                if (entry.Key == key)
                {
                    return entry;
                }
            }
            return null;
        }

        // The high 16 bits of a "_&<id>" value name the string table: FM4, FH1 and FH2 write CD49 in
        // Data_Car.DisplayName / ModelShort, FM2 A949, so an FM2 value kept as it is pointed FM4 at
        // another table and the car showed with no name (TOY_SupraVeilside99_98, user 2026-10-20). The
        // high bits FM4's other rows use in that column; 'own' when it has none.
        private uint TargetStringHigh(string table, string column, string rowWhere, long id, uint own)
        {
            object other = this.target.Scalar("SELECT " + SqliteDatabase.Quote(column) + " FROM " + table + " WHERE NOT (" + rowWhere + ") AND " + SqliteDatabase.Quote(column) + " LIKE '\\_&%' ESCAPE '\\' LIMIT 1", id);
            uint value;
            if (!(other is string) || !uint.TryParse(((string) other).Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out value))
            {
                return own;
            }
            return value & 0xFFFF0000u;
        }

        // The FM4 key for an FH2 string: its own when free in every language or already holding this
        // string (same name), the key a port before gave the name, else the highest key free everywhere
        // (FFFF ends the key list).
        // 'installed': the keys of this table that the installed packs hold. Such a key is never free (its owner's strings would
        // hide or be hidden), and a new key is the highest key in use + 1 across the game, the packs and this port, so two
        // ports in a row never meet on the same key; only when that passes FFFE does the search go down from FFFE.
        private int StringKey(Dictionary<string, StrFile> files, Dictionary<int, string> installed, int key, string name)
        {
            bool free = !installed.ContainsKey(key);
            foreach (StrFile file in files.Values)
            {
                StrFile.Entry existing = Find(file, key);
                if (existing != null && (name == null || existing.Name != name))
                {
                    free = false;
                }
            }
            if (free)
            {
                return key;
            }
            if (name != null)
            {
                foreach (StrFile file in files.Values)
                {
                    foreach (StrFile.Entry entry in file.Entries)
                    {
                        if (entry.Name == name)
                        {
                            return entry.Key;
                        }
                    }
                }
            }
            int highest = 0;
            foreach (StrFile file in files.Values)
            {
                foreach (StrFile.Entry entry in file.Entries)
                {
                    if (entry.Key < 0xFFFF && entry.Key > highest)
                    {
                        highest = entry.Key;
                    }
                }
            }
            foreach (int used in installed.Keys)
            {
                if (used < 0xFFFF && used > highest)
                {
                    highest = used;
                }
            }
            if (highest < 0xFFFE)
            {
                return highest + 1;
            }
            for (int candidate = 0xFFFE; candidate > 0; candidate--)
            {
                bool used = installed.ContainsKey(candidate);
                foreach (StrFile file in files.Values)
                {
                    used = used || file.Contains(candidate);
                }
                if (!used)
                {
                    return candidate;
                }
            }
            throw new InvalidOperationException("The string table has no free string key.");
        }

        #endregion
    }
}
