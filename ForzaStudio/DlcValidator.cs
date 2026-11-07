namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Xml;

    // Checks a DLC package (an STFS package file or the extracted folder) and lists every problem it finds,
    // without writing anything. Four areas:
    //   structure  the package opens, zipmount.xml and stringtables.xml are well formed and name what exists,
    //              the .puboffer matches the offer, no empty or unreadable file (deep: every file is unpacked);
    //   database   the merge database merges into the game's (tables and columns exist), DbIntegrity's rules
    //              (keys, references, cars) on the result, the ContentOffers row and its OfferId, each car's
    //              Data_CarBody row and ModelId;
    //   files      per car: its folder and LOD0 model, the thumbnail, the wheel folder, the engine tuning;
    //   sound      per engine of a car: <engine>_ET.xml, the <name>_HT.xml harmonic tunings it names, the
    //              <Soundbank name> .fsb of each, the car model tuning (<car>_CMT.xml);
    //   strings    every language holds the same tables and keys, the _&<n> references of the merge database
    //              point at a key that exists, the pack's own names are not empty.
    // What the package lacks is looked up in the base game (Tools > Settings > Folders, FM4: the port target)
    // and, when that is not set, only reported as "not checked". A file that is in neither the package nor
    // the base game may still be in another installed package, so those are warnings, not errors.
    public static class DlcValidator
    {
        public const int Info = 0;
        public const int Warning = 1;
        public const int Error = 2;

        public class Finding
        {
            public int Level;
            public string Area;
            public string Text;
        }

        public class Report
        {
            public string Package;
            public string Name;
            public int Offer;
            public ForzaGame Game;
            public int Cars;
            public readonly List<Finding> Findings = new List<Finding>();
            public readonly List<string[]> Missing = new List<string[]>();      // file name, car: in no package and not in the game
            public readonly List<string[]> Elsewhere = new List<string[]>();    // file name, package name, package file, mounted path: only another package has it

            public int Count(int level)
            {
                int n = 0;
                foreach (Finding f in this.Findings)
                {
                    if (f.Level == level)
                    {
                        n++;
                    }
                }
                return n;
            }

            public string Verdict
            {
                get
                {
                    int errors = this.Count(Error);
                    int warnings = this.Count(Warning);
                    if (errors > 0)
                    {
                        return errors.ToString(CultureInfo.InvariantCulture) + " error(s), " + warnings.ToString(CultureInfo.InvariantCulture) + " warning(s)";
                    }
                    return (warnings > 0) ? "OK, " + warnings.ToString(CultureInfo.InvariantCulture) + " warning(s)" : "OK";
                }
            }

            public string Text()
            {
                StringBuilder text = new StringBuilder();
                text.AppendLine(this.Name + "  [" + this.Verdict + "]");
                foreach (int level in new int[] { Error, Warning, Info })
                {
                    foreach (Finding f in this.Findings)
                    {
                        if (f.Level == level)
                        {
                            text.AppendLine("  " + Label(level) + " " + f.Area + ": " + f.Text);
                        }
                    }
                }
                return text.ToString();
            }
        }

        public static string Label(int level)
        {
            return (level == Error) ? "ERROR  " : (level == Warning) ? "warning" : "info   ";
        }

        private class Context
        {
            public Report Report;
            public DlcSource Source;
            public string BaseRoot;                                  // the base game folder, or null
            public byte[] BaseDb;
            public SqliteDatabase Merge;                             // the package's database, or null
            public SqliteDatabase Base;                              // the base game's, or null
            public Dictionary<string, List<string>> ByName;          // file name (lower case) -> mounted paths
            public List<OtherPack> Others;                           // the other packages of the folder
            public ForzaZip BaseEngineTuning;
            public ForzaZip BaseHarmonic;
            public ForzaZip BaseModelTuning;

            public void Add(int level, string area, string text)
            {
                Finding f = new Finding();
                f.Level = level;
                f.Area = area;
                f.Text = text;
                this.Report.Findings.Add(f);
            }
        }

        // path: a package file or an extracted package folder. deep: unpack every file to catch damaged data.
        public static Report Validate(string path, bool deep)
        {
            Report report = new Report();
            report.Package = path;
            report.Name = Path.GetFileName(path.TrimEnd('\\'));
            Context cx = new Context();
            cx.Report = report;
            DlcSource source;
            try
            {
                source = Directory.Exists(path) ? DlcSource.Open(path) : DlcSource.OpenPackage(path, true);
            }
            catch (Exception e)
            {
                cx.Add(Error, "structure", "cannot be opened: " + e.Message);
                return report;
            }
            if (source == null)
            {
                cx.Add(Error, "structure", "not a DLC package (no Media\\<offer>.puboffer, no Media\\DLCZips).");
                return report;
            }
            if (!string.IsNullOrEmpty(source.Title))
            {
                report.Name = source.Title + " (" + report.Name + ")";
            }
            report.Offer = source.Offer;
            report.Game = source.Game;
            if (source.Problem != null)
            {
                cx.Add(Error, "structure", source.Problem);
                return report;
            }
            cx.Source = source;
            try
            {
                cx.Add(Info, "structure", string.Format(CultureInfo.InvariantCulture, "offer {0}, game {1}", source.Offer, GameInfo.Tag(source.Game)));
                Index(cx);
                Structure(cx, deep);
                OpenDatabases(cx);
                Database(cx);
                Cars(cx);
                Strings(cx);
            }
            catch (Exception e)
            {
                cx.Add(Error, "validator", "stopped by an unexpected error: " + e.Message);
            }
            finally
            {
                if (cx.Merge != null)
                {
                    cx.Merge.Dispose();
                }
                if (cx.Base != null)
                {
                    cx.Base.Dispose();
                }
            }
            CollapseLanguages(report);
            return report;
        }

        // The same string finding in every language ("DE/List_CarMake.str lacks key 4235") becomes one line that names
        // the languages: "[DE, ES, FR] List_CarMake.str lacks key 4235".
        private static void CollapseLanguages(Report report)
        {
            List<Finding> result = new List<Finding>();
            Dictionary<string, Finding> groups = new Dictionary<string, Finding>();
            Dictionary<string, List<string>> languages = new Dictionary<string, List<string>>();
            foreach (Finding f in report.Findings)
            {
                Match m = (f.Area == "strings") ? Regex.Match(f.Text, "^([A-Za-z]{2,3})/(.+)$") : Match.Empty;
                if (!m.Success)
                {
                    result.Add(f);
                    continue;
                }
                string key = f.Level.ToString(CultureInfo.InvariantCulture) + "|" + m.Groups[2].Value;
                Finding group;
                if (!groups.TryGetValue(key, out group))
                {
                    group = new Finding();
                    group.Level = f.Level;
                    group.Area = f.Area;
                    group.Text = m.Groups[2].Value;
                    groups[key] = group;
                    languages[key] = new List<string>();
                    result.Add(group);
                }
                languages[key].Add(m.Groups[1].Value);
            }
            foreach (KeyValuePair<string, Finding> group in groups)
            {
                List<string> list = languages[group.Key];
                group.Value.Text = ((list.Count > 1) ? "[" + string.Join(", ", list.ToArray()) + "] " : list[0] + ": ") + group.Value.Text;
            }
            report.Findings.Clear();
            report.Findings.AddRange(result);
        }

        private static void Index(Context cx)
        {
            cx.ByName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in cx.Source.MountedPaths())
            {
                string name = path.Substring(path.LastIndexOf('/') + 1);
                List<string> list;
                if (!cx.ByName.TryGetValue(name, out list))
                {
                    list = new List<string>();
                    cx.ByName[name] = list;
                }
                list.Add(path);
            }
        }

        // ----- structure -----

        private static void Structure(Context cx, bool deep)
        {
            DlcSource source = cx.Source;
            bool fm3 = source.Game == ForzaGame.Motorsport3;
            if (!fm3 && source.Game != ForzaGame.Motorsport2)
            {
                string offer = "Media\\" + source.Offer.ToString(CultureInfo.InvariantCulture) + ".puboffer";
                if (source.VolumeSize(offer) < 0)
                {
                    cx.Add(Error, "structure", offer + " is missing.");
                }
            }
            int empty = 0;
            foreach (string path in source.MountedPaths())
            {
                if (source.MountedSize(path) == 0 && !path.EndsWith("/", StringComparison.Ordinal))
                {
                    empty++;
                    if (empty <= 10)
                    {
                        cx.Add(Warning, "structure", "empty file: " + path);
                    }
                }
            }
            if (empty > 10)
            {
                cx.Add(Warning, "structure", (empty - 10).ToString(CultureInfo.InvariantCulture) + " more empty files.");
            }

            // zipmount.xml: every zip it names is in the package.
            string list = "Media\\DLCZips\\zipmount.xml";
            if (source.VolumeSize(list) < 0)
            {
                cx.Add(fm3 ? Info : Warning, "structure", "no zipmount.xml (every zip is mounted at the game root).");
            }
            else
            {
                List<string> layers = source.LayerPaths();
                try
                {
                    XmlDocument xml = new XmlDocument();
                    xml.Load(new MemoryStream(source.ReadVolume(list)));
                    foreach (XmlElement zip in xml.SelectNodes("/zipmount/zip"))
                    {
                        string name = zip.GetAttribute("Name");
                        bool found = layers.Exists(delegate(string layer) { return layer.EndsWith("\\" + name, StringComparison.OrdinalIgnoreCase); });
                        if (!found)
                        {
                            cx.Add(Error, "structure", "zipmount.xml names " + name + ", which the package does not have.");
                        }
                        string mount = zip.GetAttribute("Mount");
                        if (mount.Length > 0 && !GamePaths.IsGamePath(mount))
                        {
                            cx.Add(Error, "structure", "zipmount.xml: the mount of " + name + " (" + mount + ") is not a game: path.");
                        }
                    }
                    foreach (string layer in layers)
                    {
                        string file = layer.Substring(layer.LastIndexOf('\\') + 1);
                        bool named = false;
                        foreach (XmlElement zip in xml.SelectNodes("/zipmount/zip"))
                        {
                            named = named || string.Equals(zip.GetAttribute("Name"), file, StringComparison.OrdinalIgnoreCase);
                        }
                        if (!named && file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && file.StartsWith("StringTables", StringComparison.OrdinalIgnoreCase))
                        {
                            cx.Add(Warning, "structure", file + " is not in zipmount.xml: the game mounts it at the root, not on Media\\StringTables.");
                        }
                    }
                }
                catch (XmlException e)
                {
                    cx.Add(Error, "structure", "zipmount.xml is not valid XML: " + e.Message);
                }
            }

            // stringtables.xml: every table named has a .str in every language of the package.
            string tablesList = "Media\\DLCZips\\stringtables.xml";
            if (source.VolumeSize(tablesList) >= 0)
            {
                try
                {
                    XmlDocument xml = new XmlDocument();
                    xml.Load(new MemoryStream(source.ReadVolume(tablesList)));
                    Dictionary<string, Dictionary<string, bool>> have = StringFiles(cx);
                    foreach (XmlElement table in xml.SelectNodes("/stringtables/table"))
                    {
                        string name = table.GetAttribute("Name");
                        foreach (KeyValuePair<string, Dictionary<string, bool>> language in have)
                        {
                            if (!language.Value.ContainsKey(name))
                            {
                                cx.Add(Error, "structure", "stringtables.xml names " + name + ", missing in the " + language.Key + " strings.");
                            }
                        }
                    }
                }
                catch (XmlException e)
                {
                    cx.Add(Error, "structure", "stringtables.xml is not valid XML: " + e.Message);
                }
            }

            if (deep)
            {
                int bad = 0;
                int total = 0;
                int models = 0;
                foreach (string path in source.MountedPaths())
                {
                    total++;
                    byte[] bytes;
                    try
                    {
                        bytes = source.ReadMounted(path);
                    }
                    catch (Exception e)
                    {
                        bad++;
                        cx.Add(Error, "structure", "cannot be unpacked: " + path + " (" + e.Message + ")");
                        continue;
                    }
                    if (path.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase))
                    {
                        models += CheckModel(cx, path, bytes) ? 1 : 0;
                    }
                }
                cx.Add(Info, "structure", string.Format(CultureInfo.InvariantCulture, "deep check: {0} files unpacked, {1} damaged.", total, bad));
                cx.Add(Info, "models", string.Format(CultureInfo.InvariantCulture, "deep check: {0} .carbin models checked against the FM4 format (CarbinValidator).", models));
            }
        }

        // Deep check of one car model: the FM4 format as the game reads it (CarbinValidator). FH / FM3 files in a
        // package are not checked (an FM4 package holds FM4 models after a port). Returns whether it was checked.
        private static bool CheckModel(Context cx, string path, byte[] bytes)
        {
            CarbinModel model;
            try
            {
                model = new CarbinModel(bytes);
            }
            catch (Exception e)
            {
                cx.Add(Warning, "models", path + ": not read as a car model (" + e.Message + ")");
                return false;
            }
            if (model.IsHorizon)
            {
                cx.Add(Warning, "models", path + ": FH model format (not converted to FM4)");
                return false;
            }
            List<CarbinValidator.Finding> found = CarbinValidator.Check(model, path);
            const int shown = 6;
            for (int i = 0; i < found.Count && i < shown; i++)
            {
                cx.Add(found[i].Error ? Error : Warning, "models", path + ": " + found[i].Where + ": " + found[i].Text);
            }
            if (found.Count > shown)
            {
                cx.Add(Warning, "models", string.Format(CultureInfo.InvariantCulture, "{0}: {1} more format findings", path, found.Count - shown));
            }
            return true;
        }

        // ----- databases -----

        private static void OpenDatabases(Context cx)
        {
            byte[] merge = cx.Source.Merge();
            if (merge != null)
            {
                try
                {
                    cx.Merge = SqliteDatabase.FromBytes(merge);
                }
                catch (Exception e)
                {
                    cx.Add(Error, "database", "the merge database cannot be opened: " + e.Message);
                }
            }
            cx.BaseRoot = GameInfo.ConfiguredFolder(cx.Source.Game);
            if (cx.BaseRoot == null)
            {
                cx.Add(Warning, "base game", "the " + GameInfo.Tag(cx.Source.Game) + " game folder is not set (Tools > Settings > Folders): what the package takes from the game is not checked.");
                return;
            }
            string db = Path.Combine(Path.Combine(Path.Combine(cx.BaseRoot, "Media"), "db"), "gamedb.slt");
            cx.Add(Info, "base game", "compared with the game folder " + cx.BaseRoot + " (rows already in its database, from packages merged into it, show as conflicts).");
            if (File.Exists(db))
            {
                cx.BaseDb = File.ReadAllBytes(db);
                cx.Base = SqliteDatabase.FromBytes(cx.BaseDb);
            }
            string engines = Path.Combine(Path.Combine(Path.Combine(Path.Combine(cx.BaseRoot, "Media"), "audio"), "cars"), "Engines");
            cx.BaseEngineTuning = OpenZip(Path.Combine(engines, "EngineTuning.zip"));
            cx.BaseHarmonic = OpenZip(Path.Combine(engines, "HarmonicTuning.zip"));
            cx.BaseModelTuning = OpenZip(Path.Combine(Path.Combine(Path.Combine(Path.Combine(cx.BaseRoot, "Media"), "audio"), "cars"), "CarModelTuning.zip"));
        }

        private static ForzaZip OpenZip(string path)
        {
            try
            {
                return File.Exists(path) ? new ForzaZip(path) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void Database(Context cx)
        {
            if (cx.Merge == null)
            {
                if (cx.Source.Merge() == null)
                {
                    cx.Add(Info, "database", "no merge database (the package's rows are in the game's own database).");
                }
                return;
            }
            SqliteDatabase merge = cx.Merge;
            Dictionary<string, List<string>> own = DbIds.Schema(merge);
            int rows = 0;
            foreach (KeyValuePair<string, List<string>> table in own)
            {
                if (table.Key.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                rows += Convert.ToInt32(merge.Scalar("SELECT count(*) FROM " + SqliteDatabase.Quote(table.Key)));
            }
            cx.Add(Info, "database", string.Format(CultureInfo.InvariantCulture, "merge database: {0} tables, {1} rows.", own.Count, rows));

            if (cx.Base == null)
            {
                return;
            }
            Dictionary<string, List<string>> game = DbIds.Schema(cx.Base);
            foreach (KeyValuePair<string, List<string>> table in own)
            {
                if (table.Key.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                List<string> columns;
                if (!game.TryGetValue(table.Key, out columns))
                {
                    cx.Add(Warning, "database", "table " + table.Key + " is not in the game's database: the game ignores it.");
                    continue;
                }
                foreach (string column in table.Value)
                {
                    if (!columns.Exists(delegate(string c) { return string.Equals(c, column, StringComparison.OrdinalIgnoreCase); }))
                    {
                        cx.Add(Error, "database", "column " + table.Key + "." + column + " is not in the game's table: the merge cannot add its rows.");
                    }
                }
            }
            if (cx.Source.Game == ForzaGame.Motorsport4)
            {
                DbIntegrity.Result check = DbIntegrity.CheckMerge(cx.BaseDb, new List<string>(), cx.Source.Merge(), ForzaGame.Motorsport4);
                // the per-row lists below replace the integrity check's counts of dangling ids
                Lines(cx, Error, "database", check.Errors.FindAll(delegate(string s) { return s.IndexOf("that do not exist", StringComparison.Ordinal) < 0 && s.IndexOf("is not above FM4", StringComparison.Ordinal) < 0 && !s.StartsWith("the package's database does not merge", StringComparison.Ordinal); }));
                Lines(cx, Warning, "database", check.Warnings.FindAll(delegate(string s) { return s.IndexOf("that do not exist", StringComparison.Ordinal) < 0 && s.IndexOf("is not above FM4", StringComparison.Ordinal) < 0 && !s.StartsWith("the package's database does not merge", StringComparison.Ordinal); }));
                CheckOffer(cx);
            }
            try
            {
                using (SqliteDatabase merged = SqliteDatabase.FromBytes(cx.BaseDb))
                {
                    DbIntegrity.AddRows(merged, merge);
                    Keys(cx, merged, own);
                    Dangling(cx, merged, own);
                }
            }
            catch (Exception e)
            {
                // the same failure is reported by the integrity check above; find the rows that collide
                cx.Add(Info, "database", "the package's rows do not merge into the game's database (" + e.Message + "): the colliding rows are listed under keys.");
                KeysAgainstGame(cx, own, e.Message);
            }
            if (cx.Source.Game == ForzaGame.Motorsport4)
            {
                cx.Add(Info, "database", "FM4 integrity rules checked (keys above FM4's largest, references, cars).");
            }
            else
            {
                cx.Add(Info, "database", "integrity rules (keys above FM4's, references) are FM4's: not run for " + GameInfo.Tag(cx.Source.Game) + ".");
            }
        }

        private static string Str(object value)
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        // "Table[Key=7]" for a row of the package's table, or its first values when the table has no key.
        private static string RowName(string table, string key, List<string> columns, object[] row)
        {
            int at = (key == null) ? -1 : columns.FindIndex(delegate(string c) { return string.Equals(c, key, StringComparison.OrdinalIgnoreCase); });
            if (at >= 0)
            {
                return table + "[" + key + "=" + Str(row[at]) + "]";
            }
            StringBuilder text = new StringBuilder(table + "[");
            for (int i = 0; i < row.Length && i < 3; i++)
            {
                text.Append(i == 0 ? "" : ", ").Append(columns[i]).Append('=').Append(Str(row[i]));
            }
            return text.Append(']').ToString();
        }

        // Keys of the package's rows, on the game's database with the package added: a key that was already there
        // (the package would replace or double a game row), and a key at or below FM4's largest.
        // When the merge fails: the package's rows whose key is already in the game's table, for the column the
        // failure names ("UNIQUE constraint failed: Table.Column") and for each table's key column.
        private static void KeysAgainstGame(Context cx, Dictionary<string, List<string>> own, string failure)
        {
            Match named = Regex.Match(failure, "failed: (\\w+)\\.(\\w+)");
            foreach (KeyValuePair<string, List<string>> table in own)
            {
                if (table.Key.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase) || !DbIds.HasTable(cx.Base, table.Key))
                {
                    continue;
                }
                string key = DbIds.KeyColumn(cx.Merge, table.Key, table.Value);
                if (named.Success && string.Equals(named.Groups[1].Value, table.Key, StringComparison.OrdinalIgnoreCase))
                {
                    key = named.Groups[2].Value;
                }
                if (key == null)
                {
                    continue;
                }
                try
                {
                    // the columns both tables have, compared row against row: an identical row is a harmless repeat
                    List<string> gameColumns = DbIds.Schema(cx.Base)[table.Key];
                    List<string> shared = table.Value.FindAll(delegate(string c) { return gameColumns.Exists(delegate(string g) { return string.Equals(g, c, StringComparison.OrdinalIgnoreCase); }); });
                    string select = string.Join(", ", shared.ConvertAll<string>(delegate(string c) { return SqliteDatabase.Quote(c); }).ToArray());
                    int at = shared.FindIndex(delegate(string c) { return string.Equals(c, key, StringComparison.OrdinalIgnoreCase); });
                    Dictionary<string, string> inGame = new Dictionary<string, string>();
                    foreach (object[] row in cx.Base.Query("SELECT " + select + " FROM " + SqliteDatabase.Quote(table.Key)).Rows)
                    {
                        inGame[Str(row[at])] = DbIntegrity.RowText(row);
                    }
                    int shown = 0;
                    int identical = 0;
                    foreach (object[] row in cx.Merge.Query("SELECT " + select + " FROM " + SqliteDatabase.Quote(table.Key)).Rows)
                    {
                        string game;
                        if (!inGame.TryGetValue(Str(row[at]), out game))
                        {
                            continue;
                        }
                        if (game == DbIntegrity.RowText(row))
                        {
                            identical++;
                        }
                        else if (shown++ < 40)
                        {
                            cx.Add(Error, "keys", table.Key + "." + key + " = " + Str(row[at]) + " is in the game's table with other values (package: " + DbIntegrity.RowText(row) + "; game: " + game + "): the merge fails on this row.");
                        }
                    }
                    if (identical > 0)
                    {
                        cx.Add(Warning, "keys", table.Key + ": " + identical.ToString(CultureInfo.InvariantCulture) + " row(s) repeat the game's row exactly (same " + key + ", same values). The merge still fails on them (UNIQUE), so they should be left out of the package's database.");
                    }
                }
                catch (Exception)
                {
                    // a column the game's table lacks: reported under database
                }
            }
        }
        // A file the package lacks: found in another package of the same folder it is only information, else the
        // given level. The other packages are read once, by file name. What is missing and what another package
        // has is kept in the report for the fixer.
        private static void Absent(Context cx, int level, string car, string name, string text)
        {
            if (level != Info)
            {
                OtherPack pack = OtherPackage(cx, name);
                if (pack != null)
                {
                    cx.Report.Elsewhere.Add(new string[] { name, pack.Name, pack.File, pack.Names[name] });
                    cx.Add(Info, car, name + " is not in this package or the game, but package " + pack.Name + " has it (it must stay installed).");
                    return;
                }
                cx.Report.Missing.Add(new string[] { name, car });
                text += " Other packages of the folder were searched too.";
            }
            cx.Add(level, car, text);
        }

        private class OtherPack
        {
            public string Name;
            public string File;
            public Dictionary<string, string> Names;    // file name -> mounted path
        }

        private static OtherPack OtherPackage(Context cx, string name)
        {
            if (cx.Others == null)
            {
                cx.Others = new List<OtherPack>();
                string self = Path.GetFullPath(cx.Report.Package.TrimEnd('\\'));
                string parent = Path.GetDirectoryName(self);
                if (parent != null && Directory.Exists(parent))
                {
                    foreach (string other in Directory.GetFileSystemEntries(parent))
                    {
                        if (string.Equals(Path.GetFullPath(other), self, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        try
                        {
                            DlcSource source = Directory.Exists(other) ? DlcSource.Open(other) : (StfsReader.IsPackage(other) ? DlcSource.OpenPackage(other, true) : null);
                            if (source == null || source.Problem != null)
                            {
                                continue;
                            }
                            OtherPack pack = new OtherPack();
                            pack.File = other;
                            pack.Name = string.IsNullOrEmpty(source.Title) ? Path.GetFileName(other) : source.Title + " (" + Path.GetFileName(other) + ")";
                            pack.Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            foreach (string path in source.MountedPaths())
                            {
                                pack.Names[path.Substring(path.LastIndexOf('/') + 1)] = path;
                            }
                            cx.Others.Add(pack);
                        }
                        catch (Exception)
                        {
                            // not a package
                        }
                    }
                }
            }
            foreach (OtherPack other in cx.Others)
            {
                if (other.Names.ContainsKey(name))
                {
                    return other;
                }
            }
            return null;
        }

        private static void Keys(Context cx, SqliteDatabase merged, Dictionary<string, List<string>> own)
        {
            foreach (KeyValuePair<string, List<string>> table in own)
            {
                if (table.Key.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase) || !DbIds.HasTable(merged, table.Key))
                {
                    continue;
                }
                string key = DbIds.KeyColumn(cx.Merge, table.Key, table.Value);
                if (key == null)
                {
                    continue;
                }
                Dictionary<string, bool> twice = new Dictionary<string, bool>();
                foreach (object[] row in merged.Query("SELECT " + SqliteDatabase.Quote(key) + " FROM " + SqliteDatabase.Quote(table.Key) + " GROUP BY " + SqliteDatabase.Quote(key) + " HAVING count(*) > 1").Rows)
                {
                    twice[Str(row[0])] = true;
                }
                long limit = (cx.Source.Game == ForzaGame.Motorsport4) ? GameIdLimits.Max(ForzaGame.Motorsport4, table.Key, key) : -1;
                int shown = 0;
                List<string> low = new List<string>();
                int at = table.Value.FindIndex(delegate(string c) { return string.Equals(c, key, StringComparison.OrdinalIgnoreCase); });
                foreach (object[] row in cx.Merge.Query("SELECT * FROM " + SqliteDatabase.Quote(table.Key)).Rows)
                {
                    object value = row[at];
                    if (twice.ContainsKey(Str(value)))
                    {
                        if (shown++ < 40)
                        {
                            cx.Add(Error, "keys", table.Key + "." + key + " = " + Str(value) + " is in the game's table (or twice in the package) already: the game gets two rows with the same key.");
                        }
                    }
                    else if (limit >= 0 && value is long && (long) value <= limit && !(table.Key.Equals("ContentOffers", StringComparison.OrdinalIgnoreCase) && key.Equals("OfferId", StringComparison.OrdinalIgnoreCase)))
                    {
                        low.Add(Str(value));
                    }
                }
                if (low.Count > 0)
                {
                    cx.Add(Warning, "keys", string.Format(CultureInfo.InvariantCulture, "{0}.{1}: {2} key(s) at or below FM4's largest ({3}), e.g. {4}: another pack may use the same keys (normal for an official pack).",
                        table.Key, key, low.Count, limit, string.Join(", ", low.GetRange(0, Math.Min(5, low.Count)).ToArray())));
                }
            }
        }

        // Every row of the package whose reference (GameDbSchema relation) points at a row that is in neither the
        // package nor the game. Strict relations never dangle in the original game: an error. Loose ones already
        // dangle in the game: information only.
        private static void Dangling(Context cx, SqliteDatabase merged, Dictionary<string, List<string>> own)
        {
            int shown = 0;
            int skipped = 0;
            foreach (GameDbSchema.Relation relation in GameDbSchema.Relations)
            {
                List<string> columns;
                if (!own.TryGetValue(relation.Table, out columns) || !DbIds.HasTable(merged, relation.Target))
                {
                    continue;
                }
                try
                {
                    string[] from = relation.Columns;
                    string[] to = relation.TargetColumns;
                    int[] at = new int[from.Length];
                    for (int i = 0; i < from.Length; i++)
                    {
                        string name = from[i];
                        at[i] = columns.FindIndex(delegate(string c) { return string.Equals(c, name, StringComparison.OrdinalIgnoreCase); });
                        if (at[i] < 0)
                        {
                            throw new InvalidOperationException();
                        }
                    }
                    Dictionary<string, bool> targets = new Dictionary<string, bool>();
                    string select = string.Join(", ", Array.ConvertAll<string, string>(to, delegate(string c) { return SqliteDatabase.Quote(c); }));
                    foreach (object[] row in merged.Query("SELECT " + select + " FROM " + SqliteDatabase.Quote(relation.Target)).Rows)
                    {
                        targets[Join(row)] = true;
                    }
                    string key = DbIds.KeyColumn(cx.Merge, relation.Table, columns);
                    foreach (object[] row in cx.Merge.Query("SELECT * FROM " + SqliteDatabase.Quote(relation.Table)).Rows)
                    {
                        object[] value = new object[at.Length];
                        bool none = false;
                        for (int i = 0; i < at.Length; i++)
                        {
                            value[i] = row[at[i]];
                            none = none || value[i] == null || (value[i] is long && GameDbSchema.IsNone((long) value[i]));
                        }
                        if (none || targets.ContainsKey(Join(value)))
                        {
                            continue;
                        }
                        if (!relation.Strict)
                        {
                            skipped++;
                            continue;
                        }
                        if (shown++ >= 80)
                        {
                            continue;
                        }
                        cx.Add(Error, "references", string.Format(CultureInfo.InvariantCulture, "{0}: {1}.{2} = {3} points at {4}.{5}, which has no such row (neither in the package nor in the game).",
                            RowName(relation.Table, key, columns, row), relation.Table, string.Join(",", from), Join(value), relation.Target, string.Join(",", to)));
                    }
                }
                catch (InvalidOperationException)
                {
                    // a column the relation names is not in this database
                }
            }
            if (shown > 80)
            {
                cx.Add(Error, "references", (shown - 80).ToString(CultureInfo.InvariantCulture) + " more dangling references.");
            }
            if (skipped > 0)
            {
                cx.Add(Info, "references", skipped.ToString(CultureInfo.InvariantCulture) + " row(s) point at nothing in relations that already dangle in the original game (normal).");
            }
            if (shown == 0)
            {
                cx.Add(Info, "references", "no row of the package points at a missing row.");
            }
        }

        private static string Join(object[] row)
        {
            return string.Join("/", Array.ConvertAll<object, string>(row, delegate(object o) { return Str(o); }));
        }

        private static void Lines(Context cx, int level, string area, List<string> lines)
        {
            int shown = 0;
            foreach (string line in lines)
            {
                if (++shown > 25)
                {
                    break;
                }
                cx.Add(level, area, line);
            }
            if (lines.Count > 25)
            {
                cx.Add(level, area, "... and " + (lines.Count - 25).ToString(CultureInfo.InvariantCulture) + " more.");
            }
        }

        private static void CheckOffer(Context cx)
        {
            SqliteDatabase merge = cx.Merge;
            if (!DbIds.HasTable(merge, "ContentOffers"))
            {
                cx.Add(Error, "database", "the merge has no ContentOffers row: the game does not list the pack.");
                return;
            }
            SqliteDatabase.Result offers = merge.Query("SELECT OfferId, PubOfferingID FROM ContentOffers");
            if (offers.Rows.Count == 0)
            {
                cx.Add(Error, "database", "ContentOffers is empty.");
                return;
            }
            long expected = long.Parse(DlcPackage.OfferId(cx.Source.Offer), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            foreach (object[] row in offers.Rows)
            {
                long id = Convert.ToInt64(row[0], CultureInfo.InvariantCulture);
                if (id != expected)
                {
                    cx.Add(Warning, "database", string.Format(CultureInfo.InvariantCulture, "ContentOffers.OfferId {0:X} is not the one built from offer {1} ({2:X}).", id, cx.Source.Offer, expected));
                }
                if (row[1] != null && Convert.ToInt64(row[1], CultureInfo.InvariantCulture) != cx.Source.Offer)
                {
                    cx.Add(Warning, "database", "ContentOffers.PubOfferingID " + Convert.ToString(row[1], CultureInfo.InvariantCulture) + " is not the offer number " + cx.Source.Offer.ToString(CultureInfo.InvariantCulture) + ".");
                }
            }
        }

        // The first answer of a query in the package's database, then the game's; null when neither has one.
        private static object Lookup(Context cx, string sql, params object[] args)
        {
            foreach (SqliteDatabase db in new SqliteDatabase[] { cx.Merge, cx.Base })
            {
                if (db == null)
                {
                    continue;
                }
                try
                {
                    object value = db.Scalar(sql, args);
                    if (value != null)
                    {
                        return value;
                    }
                }
                catch (Exception)
                {
                    // a table the database does not have
                }
            }
            return null;
        }

        // ----- cars: files and sound -----

        private static bool HasPrefix(Context cx, string prefix)
        {
            foreach (string path in cx.Source.MountedPaths())
            {
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool InPackage(Context cx, string fileName)
        {
            return cx.ByName.ContainsKey(fileName);
        }

        private static void Cars(Context cx)
        {
            List<string> cars = cx.Source.Cars();
            cx.Report.Cars = cars.Count;
            if (cars.Count == 0)
            {
                cx.Add(Warning, "cars", "the package adds no car.");
                return;
            }
            cx.Add(Info, "cars", cars.Count.ToString(CultureInfo.InvariantCulture) + " car(s): " + string.Join(", ", cars.ToArray()));
            List<string> checkedTunings = new List<string>();
            foreach (string car in cars)
            {
                long id = -1;
                if (cx.Merge != null && DbIds.HasTable(cx.Merge, "Data_Car"))
                {
                    object value = cx.Merge.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", car);
                    id = (value == null) ? -1 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
                }
                CarFiles(cx, car, id);
                if (id >= 0)
                {
                    CarRows(cx, car, id);
                    CarSound(cx, car, id, checkedTunings);
                }
            }
        }

        private static void CarFiles(Context cx, string car, long id)
        {
            string folder = "Media/cars/" + car + "/";
            bool folderFound = HasPrefix(cx, folder) || cx.Source.MountedSize("Media/cars/" + car + ".zip") >= 0;
            if (!folderFound)
            {
                cx.Add(Error, car, "no car folder (Media/cars/" + car + "/) in the package: the car has no model.");
                return;
            }
            int carbins = 0;
            bool lod0 = false;
            foreach (string path in cx.Source.MountedPaths())
            {
                if (path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && path.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase))
                {
                    carbins++;
                    lod0 = lod0 || path.EndsWith("_lod0.carbin", StringComparison.OrdinalIgnoreCase) || path.IndexOf("lod0", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            if (carbins == 0 && cx.Source.MountedSize("Media/cars/" + car + ".zip") < 0)
            {
                cx.Add(Error, car, "the car folder holds no .carbin.");
            }
            else if (!lod0 && carbins > 0)
            {
                cx.Add(Error, car, "the car folder has no LOD0 .carbin (the car would not show).");
            }
            if (cx.Merge == null || id < 0)
            {
                return;
            }
            // thumbnail
            object thumbnail = cx.Merge.Scalar("SELECT Thumbnail FROM Data_Car WHERE Id = ?", id);
            string thumb = thumbnail as string;
            if (!string.IsNullOrEmpty(thumb))
            {
                string name = Path.GetFileName(thumb.Replace('\\', '/'));
                if (!InPackage(cx, name) && !InPackage(cx, Path.ChangeExtension(name, ".xds")))
                {
                    cx.Add(Warning, car, "thumbnail " + name + " is not in the package (the car list shows a blank picture unless the game has it).");
                }
            }
            // wheels
            object wheel = cx.Merge.Scalar("SELECT StockWheelID FROM Data_Car WHERE Id = ?", id);
            if (wheel != null)
            {
                long wheelId = Convert.ToInt64(wheel, CultureInfo.InvariantCulture);
                string media = Lookup(cx, "SELECT MediaName FROM List_Wheels WHERE ID = ?", wheelId) as string;
                if (media == null)
                {
                    cx.Add(cx.Base == null ? Warning : Error, car, "StockWheelID " + wheelId.ToString(CultureInfo.InvariantCulture) + " has no List_Wheels row (package or game).");
                }
                else if (!HasPrefix(cx, "Media/wheels/" + media + "/") && cx.Source.MountedSize("Media/wheels/" + media + ".zip") < 0
                    && (cx.BaseRoot == null || !File.Exists(Path.Combine(Path.Combine(Path.Combine(cx.BaseRoot, "Media"), "wheels"), media + ".zip"))))
                {
                    cx.Add(cx.BaseRoot == null ? Warning : Error, car, "wheel model " + media + " is in neither the package nor the game (the car has no wheels).");
                }
                else if (cx.Base != null && !HasPrefix(cx, "Media/wheels/" + media + "/") && cx.Source.MountedSize("Media/wheels/" + media + ".zip") < 0)
                {
                    // The package brings no wheel of its own: the id resolves to a wheel the game gives another car
                    // (a source-game wheel id that means a different wheel in the game: 599XX got Sauber's).
                    object other = null;
                    try
                    {
                        other = cx.Base.Scalar("SELECT MediaName FROM Data_Car WHERE StockWheelID = ? AND Id <> ? LIMIT 1", wheelId, id);
                    }
                    catch (Exception)
                    {
                    }
                    if (other != null)
                    {
                        cx.Add(Warning, car, "StockWheelID " + wheelId.ToString(CultureInfo.InvariantCulture) + " is wheel " + media + ", the stock wheel of the game's " + Convert.ToString(other, CultureInfo.InvariantCulture) + ": check it is the car's own wheel.");
                    }
                }
            }
        }

        private static void CarRows(Context cx, string car, long id)
        {
            SqliteDatabase merge = cx.Merge;
            object modelId = merge.Scalar("SELECT ModelId FROM Data_Car WHERE Id = ?", id);
            if (modelId != null && Convert.ToInt64(modelId, CultureInfo.InvariantCulture) != id)
            {
                cx.Add(Error, car, "Data_Car.ModelId " + Convert.ToString(modelId, CultureInfo.InvariantCulture) + " is not the car's Id " + id.ToString(CultureInfo.InvariantCulture) + ".");
            }
            if (DbIds.HasTable(merge, "Data_CarBody") && Convert.ToInt64(merge.Scalar("SELECT count(*) FROM Data_CarBody WHERE Id = ?", id * 1000)) == 0)
            {
                cx.Add(Error, car, "no Data_CarBody row " + (id * 1000).ToString(CultureInfo.InvariantCulture) + ".");
            }
            if (DbIds.HasTable(merge, "List_UpgradeEngine") && Convert.ToInt64(merge.Scalar("SELECT count(*) FROM List_UpgradeEngine WHERE Ordinal = ?", id)) == 0
                && Lookup(cx, "SELECT 1 FROM List_UpgradeEngine WHERE Ordinal = ? LIMIT 1", id) == null)
            {
                cx.Add(Error, car, "no List_UpgradeEngine row: the car has no engine.");
            }
        }

        private static void CarSound(Context cx, string car, long id, List<string> checkedTunings)
        {
            SqliteDatabase.Result engines;
            try
            {
                engines = cx.Merge.Query("SELECT DISTINCT EngineID FROM List_UpgradeEngine WHERE Ordinal = ?", id);
            }
            catch (Exception)
            {
                return;
            }
            foreach (object[] row in engines.Rows)
            {
                long engineId = Convert.ToInt64(row[0], CultureInfo.InvariantCulture);
                string media = Lookup(cx, "SELECT MediaName FROM Data_Engine WHERE EngineID = ?", engineId) as string;
                if (media == null)
                {
                    cx.Add(cx.Base == null ? Warning : Error, car, "engine " + engineId.ToString(CultureInfo.InvariantCulture) + " has no Data_Engine row (package or game): no sound, maybe a crash.");
                    continue;
                }
                if (checkedTunings.Contains(media.ToLowerInvariant()))
                {
                    continue;
                }
                checkedTunings.Add(media.ToLowerInvariant());
                EngineTuning(cx, car, media);
            }
            string model = car + "_CMT.xml";
            if (!InPackage(cx, model) && (cx.BaseModelTuning == null || cx.BaseModelTuning.Find(model) == null))
            {
                Absent(cx, cx.BaseModelTuning == null ? Info : Warning, car, model, "car model tuning " + model + " is in neither the package nor the game.");
            }
        }

        private static byte[] ReadTuning(Context cx, string name, ForzaZip gameZip)
        {
            List<string> paths;
            if (cx.ByName.TryGetValue(name, out paths))
            {
                return cx.Source.ReadMounted(paths[paths.Count - 1]);
            }
            if (gameZip != null)
            {
                ForzaZip.Entry entry = gameZip.Find(name);
                if (entry != null)
                {
                    return gameZip.Read(entry);
                }
            }
            return null;
        }

        private static void EngineTuning(Context cx, string car, string media)
        {
            string file = media + "_ET.xml";
            byte[] data = ReadTuning(cx, file, cx.BaseEngineTuning);
            if (data == null)
            {
                Absent(cx, cx.BaseEngineTuning == null ? Info : Error, car, file, "engine tuning " + file + " is in neither the package nor the game: the engine is silent.");
                return;
            }
            string text = Encoding.UTF8.GetString(data);
            try
            {
                XmlDocument xml = new XmlDocument();
                xml.LoadXml(text.TrimStart('﻿'));
            }
            catch (XmlException e)
            {
                cx.Add(Error, car, file + " is not valid XML: " + e.Message);
                return;
            }
            int references = 0;
            List<string> missingHarmonics = new List<string>();
            List<string> banks = new List<string>();
            Dictionary<string, string[]> harmonicOf = new Dictionary<string, string[]>();      // bank (lower) -> harmonic tuning name, its text
            foreach (string element in new string[] { "EngineAmbient", "EngineIntake", "Exhaust" })
            {
                Match block = Regex.Match(text, "<" + element + ">(.*?)</" + element + ">", RegexOptions.Singleline);
                if (!block.Success)
                {
                    cx.Add(Warning, car, file + " has no <" + element + "> block (no " + element.ToLowerInvariant().Replace("engine", "engine ") + " sound).");
                    continue;
                }
                foreach (Match reference in Regex.Matches(block.Groups[1].Value, "(\\w+)=\"([^\"]+_HT\\.xml)\"", RegexOptions.IgnoreCase))
                {
                    references++;
                    string harmonic = reference.Groups[2].Value;
                    byte[] ht = ReadTuning(cx, harmonic, cx.BaseHarmonic);
                    if (ht == null)
                    {
                        if (!missingHarmonics.Contains(harmonic.ToLowerInvariant()))
                        {
                            missingHarmonics.Add(harmonic.ToLowerInvariant());
                            Absent(cx, cx.BaseHarmonic == null ? Info : Error, car, harmonic, file + " (" + element + ", from level " + reference.Groups[1].Value + ") names " + harmonic + ", in neither the package nor the game: that part of the engine sound is silent.");
                        }
                        continue;
                    }
                    Match bank = Regex.Match(Encoding.UTF8.GetString(ht), "<Soundbank\\s+name=\"([^\"]+)\"", RegexOptions.IgnoreCase);
                    if (!bank.Success)
                    {
                        cx.Add(Warning, car, harmonic + " names no <Soundbank>.");
                    }
                    else if (!string.Equals(bank.Groups[1].Value, "NA.fsb", StringComparison.OrdinalIgnoreCase) && !banks.Contains(bank.Groups[1].Value.ToLowerInvariant()))
                    {
                        banks.Add(bank.Groups[1].Value.ToLowerInvariant());
                        harmonicOf[bank.Groups[1].Value.ToLowerInvariant()] = new string[] { harmonic, Encoding.GetEncoding(28591).GetString(ht) };
                    }
                }
            }
            if (references == 0)
            {
                cx.Add(Error, car, file + " names no harmonic tuning (*_HT.xml): the engine is silent.");
            }
            foreach (string bank in banks)
            {
                bool inGame = false;
                string gameBank = null;
                if (cx.BaseRoot != null)
                {
                    string root = Path.Combine(Path.Combine(Path.Combine(Path.Combine(Path.Combine(cx.BaseRoot, "Media"), "audio"), "cars"), "Engines"), "Soundbanks");
                    gameBank = Path.Combine(Path.Combine(root, "LOD1"), bank);
                    inGame = File.Exists(gameBank);
                }
                if (!InPackage(cx, bank) && !inGame)
                {
                    Absent(cx, cx.BaseRoot == null ? Info : Error, car, bank, "sound bank " + bank + " (of " + file + ") is in neither the package nor the game: that sound is missing.");
                    continue;
                }
                // The bank the game would read: the package's own, else the game's. Its header against the loops of the
                // harmonic tuning that names it (a loop on a sample the bank does not have), and a package bank that
                // shadows the game's with different bytes (the cars of the game that play it change too).
                try
                {
                    byte[] own = InPackage(cx, bank) ? cx.Source.ReadMounted(cx.ByName[bank][cx.ByName[bank].Count - 1]) : null;
                    byte[] bankBytes = own ?? File.ReadAllBytes(gameBank);
                    string[] harmonic = harmonicOf[bank];
                    foreach (string problem in SoundIndex.CheckBankData(harmonic[0], harmonic[1], bank, bankBytes))
                    {
                        cx.Add(Error, car, problem + " (" + file + ").");
                    }
                    if (own != null && inGame && new FileInfo(gameBank).Length != own.Length)
                    {
                        cx.Add(Warning, car, "sound bank " + bank + " is in the package and in the game with different sizes (" + own.Length.ToString("N0", CultureInfo.InvariantCulture)
                            + " and " + new FileInfo(gameBank).Length.ToString("N0", CultureInfo.InvariantCulture) + " bytes): the cars of the game that play it would change too.");
                    }
                }
                catch (Exception e)
                {
                    cx.Add(Warning, car, "sound bank " + bank + " could not be checked: " + e.Message);
                }
            }
        }

        // ----- strings -----

        // language -> tables ("<Table>" without .str) of the package's string layers, from the mounted paths
        // Media/StringTables/<LANG>/<Table>.str
        private static Dictionary<string, Dictionary<string, bool>> StringFiles(Context cx)
        {
            Dictionary<string, Dictionary<string, bool>> result = new Dictionary<string, Dictionary<string, bool>>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in cx.Source.MountedPaths())
            {
                string[] part = path.Split('/');
                if (part.Length == 4 && part[1].Equals("StringTables", StringComparison.OrdinalIgnoreCase) && part[3].EndsWith(".str", StringComparison.OrdinalIgnoreCase))
                {
                    Dictionary<string, bool> tables;
                    if (!result.TryGetValue(part[2], out tables))
                    {
                        tables = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                        result[part[2]] = tables;
                    }
                    tables[Path.GetFileNameWithoutExtension(part[3])] = true;
                }
            }
            return result;
        }

        private static void Strings(Context cx)
        {
            Dictionary<string, Dictionary<string, bool>> languages = StringFiles(cx);
            if (languages.Count == 0)
            {
                cx.Add(Info, "strings", "the package has no string tables.");
                return;
            }
            // every table in every language: the union is what each language should have
            Dictionary<string, bool> union = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (Dictionary<string, bool> tables in languages.Values)
            {
                foreach (string table in tables.Keys)
                {
                    union[table] = true;
                }
            }
            Dictionary<string, Dictionary<string, Dictionary<int, StrFile.Entry>>> parsed = new Dictionary<string, Dictionary<string, Dictionary<int, StrFile.Entry>>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, Dictionary<string, bool>> language in languages)
            {
                foreach (string table in union.Keys)
                {
                    if (!language.Value.ContainsKey(table))
                    {
                        cx.Add(Warning, "strings", language.Key + " lacks the table " + table + " that other languages have.");
                    }
                }
                Dictionary<string, Dictionary<int, StrFile.Entry>> byTable = new Dictionary<string, Dictionary<int, StrFile.Entry>>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, byte[]> file in cx.Source.StringFiles(language.Key))
                {
                    try
                    {
                        StrFile str = new StrFile(file.Value);
                        Dictionary<int, StrFile.Entry> keys = new Dictionary<int, StrFile.Entry>();
                        foreach (StrFile.Entry entry in str.Entries)
                        {
                            keys[entry.Key] = entry;
                        }
                        byTable[file.Key] = keys;
                    }
                    catch (Exception e)
                    {
                        cx.Add(Error, "strings", language.Key + "/" + file.Key + ".str is damaged: " + e.Message);
                    }
                }
                parsed[language.Key] = byTable;
            }
            cx.Add(Info, "strings", string.Format(CultureInfo.InvariantCulture, "{0} language(s), {1} table(s).", languages.Count, union.Count));

            // the same keys in every language (a key missing from one shows blank text in that language)
            foreach (string table in union.Keys)
            {
                Dictionary<int, bool> all = new Dictionary<int, bool>();
                foreach (Dictionary<string, Dictionary<int, StrFile.Entry>> byTable in parsed.Values)
                {
                    Dictionary<int, StrFile.Entry> keys;
                    if (byTable.TryGetValue(table, out keys))
                    {
                        foreach (int key in keys.Keys)
                        {
                            all[key] = true;
                        }
                    }
                }
                foreach (KeyValuePair<string, Dictionary<string, Dictionary<int, StrFile.Entry>>> language in parsed)
                {
                    Dictionary<int, StrFile.Entry> keys;
                    if (language.Key.Equals("DEV", StringComparison.OrdinalIgnoreCase) || language.Key.Equals("LOC", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;   // the developers' pseudo locales hold part of the keys
                    }
                    if (!language.Value.TryGetValue(table, out keys))
                    {
                        continue;
                    }
                    List<string> missing = new List<string>();
                    foreach (int key in all.Keys)
                    {
                        if (!keys.ContainsKey(key))
                        {
                            missing.Add(key.ToString(CultureInfo.InvariantCulture));
                        }
                    }
                    if (missing.Count > 0)
                    {
                        missing.Sort();
                        cx.Add(Warning, "strings", string.Format(CultureInfo.InvariantCulture, "{0}/{1}.str lacks key(s) {2}{3} that other languages have: the game shows blank text there.",
                            language.Key, table, string.Join(", ", missing.GetRange(0, Math.Min(8, missing.Count)).ToArray()), missing.Count > 8 ? " and " + (missing.Count - 8).ToString(CultureInfo.InvariantCulture) + " more" : ""));
                    }
                }
            }

            // the _&<n> references of the merge database
            if (cx.Merge == null)
            {
                return;
            }
            List<string> tableNames = new List<string>(union.Keys);
            Dictionary<int, bool> anyKey = new Dictionary<int, bool>();
            foreach (Dictionary<string, Dictionary<int, StrFile.Entry>> byTable in parsed.Values)
            {
                foreach (Dictionary<int, StrFile.Entry> keys in byTable.Values)
                {
                    foreach (int key in keys.Keys)
                    {
                        anyKey[key] = true;
                    }
                }
            }
            int bad = 0;
            foreach (KeyValuePair<string, List<string>> table in DbIds.Schema(cx.Merge))
            {
                if (table.Key.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                foreach (string column in table.Value)
                {
                    SqliteDatabase.Result result = cx.Merge.Query("SELECT DISTINCT " + SqliteDatabase.Quote(column) + " FROM " + SqliteDatabase.Quote(table.Key) + " WHERE " + SqliteDatabase.Quote(column) + " LIKE '\\_&%' ESCAPE '\\'");
                    foreach (object[] row in result.Rows)
                    {
                        uint reference;
                        string text = row[0] as string;
                        if (text == null || !uint.TryParse(text.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out reference))
                        {
                            continue;
                        }
                        int key = (int) (reference & 0xFFFF);
                        bool known = anyKey.ContainsKey(key);
                        if (!known && cx.Source.Game == ForzaGame.Motorsport4)
                        {
                            foreach (string name in tableNames)
                            {
                                known = known || GameStringKeys.Used(name, key);
                            }
                        }
                        if (!known)
                        {
                            bad++;
                            if (bad <= 25)
                            {
                                cx.Add(Info, "strings", table.Key + "." + column + " = " + text + ": key " + key.ToString(CultureInfo.InvariantCulture) + " is in none of the package's tables (a base game string, or missing).");
                            }
                        }
                    }
                }
            }
            if (DbIds.HasTable(cx.Merge, "ContentOffers"))
            {
                // the pack's own name in English (or the first language) must not be empty
                foreach (KeyValuePair<string, Dictionary<string, Dictionary<int, StrFile.Entry>>> language in parsed)
                {
                    Dictionary<int, StrFile.Entry> keys;
                    if (!language.Value.TryGetValue("ContentOffers", out keys))
                    {
                        continue;
                    }
                    foreach (object[] row in cx.Merge.Query("SELECT DisplayName, Description FROM ContentOffers").Rows)
                    {
                        for (int i = 0; i < 2; i++)
                        {
                            uint reference;
                            string text = row[i] as string;
                            if (text == null || !text.StartsWith("_&", StringComparison.Ordinal) || !uint.TryParse(text.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out reference))
                            {
                                continue;
                            }
                            StrFile.Entry entry;
                            if (!keys.TryGetValue((int) (reference & 0xFFFF), out entry) || string.IsNullOrEmpty(entry.Text))
                            {
                                cx.Add(Warning, "strings", language.Key + "/ContentOffers: the pack's " + ((i == 0) ? "name" : "description") + " (key " + (reference & 0xFFFF).ToString(CultureInfo.InvariantCulture) + ") is missing or empty in the package's table: the game lists the pack with a blank " + ((i == 0) ? "name" : "description") + " unless its own strings hold the key.");
                            }
                        }
                    }
                }
            }
            if (bad > 25)
            {
                cx.Add(Warning, "strings", (bad - 25).ToString(CultureInfo.InvariantCulture) + " more unresolved references.");
            }
        }
    }
}
