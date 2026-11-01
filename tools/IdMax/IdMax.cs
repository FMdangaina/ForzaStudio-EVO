using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using ForzaStudio;

// IdMax: one file per game (docs/FM2/IdMax.json, FM3, FM4, docs/HZN/IdMax.FH1.json and FH2) with EVERY table of the game's database, its key and the
// largest Id of each key / id column in the game and in its official DLC packages. Forza Studio embeds the files
// (GameIdLimits.cs): a new row's key is one above the largest of its column. Replaces the combined GameIdLimits.json.
//
//   idmax <docs folder> [skip words]    "skip words": packages whose name contains one are not official (default: Trial)
//
// Sources: <games>\<GAME>_Extracted (every .slt below) and the packages of Xenia's content folder (every .slt in the
// package and in its Media\DLCZips\*.zip, the string table zips left out).
class IdMax
{
    static readonly string[] Keys = { "FM2", "FM3", "FM4", "FH1", "FH2" };
    static readonly Dictionary<string, string> Names = new Dictionary<string, string> {
        { "FM2", "Forza Motorsport 2" }, { "FM3", "Forza Motorsport 3" }, { "FM4", "Forza Motorsport 4" }, { "FH1", "Forza Horizon" }, { "FH2", "Forza Horizon 2" } };
    static readonly Dictionary<string, string> Titles = new Dictionary<string, string> {
        { "FM2", "4D5307EA" }, { "FM3", "4D53084D" }, { "FM4", "4D530910" }, { "FH1", "4D5309C9" }, { "FH2", "4D530AA4" } };

    class Col { public bool Key; public bool IdLike; public bool Numeric; public long? Min, Max; public string MaxIn; public Dictionary<string, long> By = new Dictionary<string, long>(); }
    class Table { public SortedDictionary<string, Col> Cols = new SortedDictionary<string, Col>(StringComparer.Ordinal); public List<string> Order = new List<string>(); public List<string> Pk = new List<string>(); public long Rows; }
    class Archive { public string Id, Kind, Name; public List<string> Dbs = new List<string>(); public List<long> PubOffers = new List<long>(); }
    class Game { public List<Archive> Archives = new List<Archive>(); public SortedDictionary<string, Table> Tables = new SortedDictionary<string, Table>(StringComparer.Ordinal); }

    static readonly Dictionary<string, Game> Data = new Dictionary<string, Game>();
    static List<string> skipWords = new List<string>();
    static string temp;

    static int Main(string[] a)
    {
        string exe = @"C:\Users\Dan\Desktop\ForzaEditor\bin\Debug\ForzaStudioEVO.exe";
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object s, ResolveEventArgs e)
        {
            string n = new AssemblyName(e.Name).Name;
            foreach (string ext in new[] { ".exe", ".dll" })
            {
                string p = Path.Combine(Path.GetDirectoryName(exe), n + ext);
                if (File.Exists(p)) return Assembly.LoadFrom(p);
            }
            return null;
        };
        return Run(a);
    }

    static int Run(string[] a)
    {
        string output = a[0];
        skipWords.Add("Trial");
        for (int i = 1; i < a.Length; i++) skipWords.Add(a[i]);
        temp = Path.Combine(Path.GetTempPath(), "IdMax_" + DateTime.Now.Ticks);
        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(output);
        foreach (string k in Keys) Data[k] = new Game();

        foreach (string k in Keys)
        {
            string root = @"F:\Xenia\Games\" + k + "_Extracted";
            if (!Directory.Exists(root)) { Console.WriteLine("no base game folder " + root); continue; }
            Archive b = new Archive { Id = "base", Kind = "base", Name = "base game" };
            Data[k].Archives.Add(b);
            foreach (string f in Directory.GetFiles(root, "*.slt", SearchOption.AllDirectories))
            {
                b.Dbs.Add(f.Substring(root.Length + 1));
                Db(k, b, File.ReadAllBytes(f));
            }
        }

        string content = @"F:\Xenia\Emulators\Xenia Canary\content\0000000000000000";
        List<string> files = new List<string>(Directory.GetFiles(content, "*", SearchOption.AllDirectories));
        files.Sort(StringComparer.OrdinalIgnoreCase);
        foreach (string f in files)
        {
            if (!StfsReader.IsPackage(f)) continue;
            StfsReader p;
            try { p = StfsReader.Open(f); } catch (Exception e) { Console.WriteLine("ERR open " + f + ": " + e.Message); continue; }
            string key = KeyOf(p.TitleId);
            if (key == null) continue;
            string name = p.DisplayName ?? "";
            bool skip = false;
            foreach (string w in skipWords) if (name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) skip = true;
            if (skip) { Console.WriteLine("SKIP (not official) " + key + " " + Path.GetFileName(f) + " " + name); continue; }
            Archive ar = new Archive { Id = Path.GetFileName(f), Kind = "package", Name = name };
            Data[key].Archives.Add(ar);
            Console.WriteLine(key + " " + ar.Id + " " + name);
            foreach (string rel in p.Files())
            {
                string r = rel.Replace('\\', '/');
                string lower = r.ToLowerInvariant();
                try
                {
                    if (lower.EndsWith(".puboffer") || lower.EndsWith(".offer"))
                    {
                        long n;
                        if (long.TryParse(Path.GetFileNameWithoutExtension(r), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) ar.PubOffers.Add(n);
                    }
                    else if (lower.EndsWith(".slt"))
                    {
                        ar.Dbs.Add(r);
                        Db(key, ar, p.ReadFile(rel));
                    }
                    else if (lower.EndsWith(".zip") && lower.IndexOf("stringtables") < 0 && lower.IndexOf("dlczips") >= 0)
                    {
                        string zipPath = Path.Combine(temp, "pack.zip");
                        using (Stream s = p.OpenFile(rel)) using (FileStream o = File.Create(zipPath)) s.CopyTo(o);
                        ForzaZip z = new ForzaZip(zipPath);
                        foreach (ForzaZip.Entry en in z.Entries)
                            if (!en.IsDirectory && en.Name.EndsWith(".slt", StringComparison.OrdinalIgnoreCase))
                            {
                                ar.Dbs.Add(r + "!" + en.Name);
                                Db(key, ar, z.Read(en));
                            }
                    }
                }
                catch (Exception e) { Console.WriteLine("ERR " + f + " " + rel + ": " + e.Message); }
            }
        }
        foreach (string k in Keys)
        {
            bool horizon = k.StartsWith("FH", StringComparison.Ordinal);
            string dir = Path.Combine(output, horizon ? "HZN" : k);
            Directory.CreateDirectory(dir);
            WriteGame(k, Path.Combine(dir, horizon ? "IdMax." + k + ".json" : "IdMax.json"));
        }
        try { Directory.Delete(temp, true); } catch (IOException) { }
        return 0;
    }

    static string KeyOf(uint titleId)
    {
        ForzaGame g = GameInfo.FromTitleId(titleId);
        switch (g)
        {
            case ForzaGame.Motorsport2: case ForzaGame.Motorsport3: case ForzaGame.Motorsport4: case ForzaGame.Horizon1: case ForzaGame.Horizon2:
                return GameIdLimits.Key(g);
            default:
                return null;
        }
    }

    static bool IdLike(string c)
    {
        return c == "id" || c.EndsWith("Id", StringComparison.Ordinal) || c.EndsWith("ID", StringComparison.Ordinal) || c.EndsWith("_id", StringComparison.Ordinal) || c.EndsWith("Ordinal", StringComparison.Ordinal);
    }

    static void Db(string key, Archive ar, byte[] data)
    {
        Game game = Data[key];
        using (SqliteDatabase db = SqliteDatabase.FromBytes(data))
        {
            foreach (object[] t in db.Query("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'").Rows)
            {
                string table = (string) t[0];
                Table tab;
                if (!game.Tables.TryGetValue(table, out tab)) game.Tables[table] = tab = new Table();
                SqliteDatabase.Result info = db.Query("PRAGMA table_info(" + SqliteDatabase.Quote(table) + ")");
                List<string> numeric = new List<string>();
                SortedList<int, string> pks = new SortedList<int, string>();
                List<string> order = new List<string>();
                foreach (object[] c in info.Rows)
                {
                    string name = (string) c[1];
                    int pk = Convert.ToInt32(c[5]);
                    bool isInt = Convert.ToString(c[2]).ToUpperInvariant().IndexOf("INT") >= 0;
                    order.Add(name);
                    if (pk > 0) pks[pk] = name;
                    if (isInt) numeric.Add(name);
                    Col col;
                    if (!tab.Cols.TryGetValue(name, out col)) { tab.Cols[name] = col = new Col(); tab.Order.Add(name); }
                    col.Numeric = col.Numeric || isInt;
                    col.IdLike = IdLike(name);
                }
                foreach (string pk in pks.Values) if (!tab.Pk.Contains(pk)) tab.Pk.Add(pk);
                StringBuilder q = new StringBuilder("SELECT count(*)");
                foreach (string c in numeric) q.Append(",min(" + SqliteDatabase.Quote(c) + "),max(" + SqliteDatabase.Quote(c) + ")");
                q.Append(" FROM " + SqliteDatabase.Quote(table));
                object[] row = db.Query(q.ToString()).Rows[0];
                long rows = Convert.ToInt64(row[0]);
                if (rows > tab.Rows) tab.Rows = rows;
                for (int i = 0; i < numeric.Count; i++)
                {
                    Col col = tab.Cols[numeric[i]];
                    if (row[1 + 2 * i] == null || row[1 + 2 * i] is DBNull || row[2 + 2 * i] == null || row[2 + 2 * i] is DBNull) continue;
                    long min = Convert.ToInt64(row[1 + 2 * i], CultureInfo.InvariantCulture), max = Convert.ToInt64(row[2 + 2 * i], CultureInfo.InvariantCulture);
                    long cur;
                    if (!col.By.TryGetValue(ar.Id, out cur) || max > cur) col.By[ar.Id] = max;
                    if (!col.Min.HasValue || min < col.Min.Value) col.Min = min;
                }
            }
        }
    }

    static string Esc(string s)
    {
        StringBuilder b = new StringBuilder();
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < 32) b.Append(' ');
            else b.Append(c);
        }
        return b.ToString();
    }

    static void WriteGame(string key, string path)
    {
        Game g = Data[key];
        // Keys: the declared primary key; else the first id column of the table; else none.
        foreach (Table t in g.Tables.Values)
        {
            List<string> keys = new List<string>(t.Pk);
            string source = "primaryKey";
            if (keys.Count == 0)
            {
                source = "none";
                foreach (string c in t.Order)
                    if (t.Cols[c].Numeric && t.Cols[c].IdLike) { keys.Add(c); source = "firstIdColumn"; break; }
            }
            t.Pk = keys;
            foreach (string k in keys) t.Cols[k].Key = true;
            tableSource[t] = source;
        }
        StringBuilder o = new StringBuilder();
        o.AppendLine("{");
        o.AppendLine("  \"format\": \"forza-id-max\",");
        o.AppendLine("  \"version\": 2,");
        o.AppendLine("  \"game\": \"" + key + "\",");
        o.AppendLine("  \"name\": \"" + Names[key] + "\",");
        o.AppendLine("  \"titleId\": \"" + Titles[key] + "\",");
        o.AppendLine("  \"generated\": \"" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "\",");
        o.AppendLine("  \"notes\": [");
        o.AppendLine("    \"Every table of the game's database. max = largest value of the column in the base game and in its official DLC packages (archives); a new row's key is max + 1 or more.\",");
        o.AppendLine("    \"keys = the primary key; without one (keySource firstIdColumn) the first id column; keySource none = the table has no id (nothing to number).\",");
        o.AppendLine("    \"columns = the keys and every other id column (…Id, …ID, Ordinal) that holds an integer. maxIn / byArchive say which archive has the largest.\",");
        o.AppendLine("    \"Packs made by Forza Studio or anyone else are NOT counted here; Forza Studio reads the installed packages at run time (DbIds, DlcWorld).\"");
        o.AppendLine("  ],");
        o.AppendLine("  \"archives\": [");
        for (int i = 0; i < g.Archives.Count; i++)
        {
            Archive a = g.Archives[i];
            o.Append("    { \"id\": \"" + Esc(a.Id) + "\", \"kind\": \"" + a.Kind + "\", \"name\": \"" + Esc(a.Name) + "\", \"databases\": [");
            for (int d = 0; d < a.Dbs.Count; d++) o.Append((d > 0 ? ", " : "") + "\"" + Esc(a.Dbs[d]) + "\"");
            o.Append("], \"pubOffers\": [" + string.Join(", ", a.PubOffers.ConvertAll<string>(delegate(long n) { return n.ToString(CultureInfo.InvariantCulture); }).ToArray()) + "] }");
            o.AppendLine(i < g.Archives.Count - 1 ? "," : "");
        }
        o.AppendLine("  ],");
        o.AppendLine("  \"tables\": {");
        int ti = 0;
        foreach (KeyValuePair<string, Table> kv in g.Tables)
        {
            Table t = kv.Value;
            o.Append("    \"" + Esc(kv.Key) + "\": { \"rows\": " + t.Rows.ToString(CultureInfo.InvariantCulture) + ", \"keySource\": \"" + tableSource[t] + "\", \"keys\": [");
            for (int k = 0; k < t.Pk.Count; k++) o.Append((k > 0 ? ", " : "") + "\"" + Esc(t.Pk[k]) + "\"");
            o.Append("], \"columns\": {");
            List<string> shown = new List<string>();
            foreach (string c in t.Order) if (t.Cols[c].Key || (t.Cols[c].Numeric && t.Cols[c].IdLike)) shown.Add(c);
            for (int c = 0; c < shown.Count; c++)
            {
                Col col = t.Cols[shown[c]];
                string maxIn = null;
                long max = long.MinValue;
                foreach (Archive a in g.Archives)
                {
                    long v;
                    if (col.By.TryGetValue(a.Id, out v) && v > max) { max = v; maxIn = a.Id; }
                }
                o.AppendLine(c == 0 ? "" : ",");
                o.Append("      \"" + Esc(shown[c]) + "\": { \"key\": " + (col.Key ? "true" : "false") + ", \"numeric\": " + (col.Numeric ? "true" : "false"));
                if (maxIn != null)
                {
                    o.Append(", \"max\": " + max.ToString(CultureInfo.InvariantCulture) + ", \"maxIn\": \"" + Esc(maxIn) + "\", \"min\": " + col.Min.Value.ToString(CultureInfo.InvariantCulture) + ", \"byArchive\": {");
                    bool first = true;
                    foreach (Archive a in g.Archives)
                    {
                        long v;
                        if (col.By.TryGetValue(a.Id, out v)) { o.Append((first ? " " : ", ") + "\"" + Esc(a.Id) + "\": " + v.ToString(CultureInfo.InvariantCulture)); first = false; }
                    }
                    o.Append(" }");
                }
                o.Append(" }");
            }
            o.Append(shown.Count > 0 ? "\n    } }" : " } }");
            o.AppendLine(++ti < g.Tables.Count ? "," : "");
        }
        o.AppendLine("  }");
        o.AppendLine("}");
        File.WriteAllText(path, o.ToString(), new UTF8Encoding(false));
        int keyed = 0, none = 0, withMax = 0;
        foreach (Table t in g.Tables.Values)
        {
            if (t.Pk.Count == 0) none++; else keyed++;
            bool any = false;
            foreach (string k in t.Pk) if (t.Cols[k].By.Count > 0) any = true;
            if (any) withMax++;
        }
        Console.WriteLine(key + ": " + g.Tables.Count + " tables, " + g.Archives.Count + " archives, " + keyed + " with a key (" + withMax + " with a max), " + none + " without an id -> " + path);
    }

    static readonly Dictionary<Table, string> tableSource = new Dictionary<Table, string>();
}
