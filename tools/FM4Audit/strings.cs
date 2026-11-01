using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using ForzaStudio;

class Au
{
    // set name -> lang -> table -> StrFile
    static SortedDictionary<string, Dictionary<string, Dictionary<string, StrFile>>> sets = new SortedDictionary<string, Dictionary<string, Dictionary<string, StrFile>>>();

    static void AddZip(string set, ForzaZip z, string forcedLang)
    {
        if (!sets.ContainsKey(set)) sets[set] = new Dictionary<string, Dictionary<string, StrFile>>(StringComparer.OrdinalIgnoreCase);
        foreach (ForzaZip.Entry en in z.Entries)
        {
            if (en.IsDirectory || !en.Name.EndsWith(".str", StringComparison.OrdinalIgnoreCase)) continue;
            string[] parts = en.Name.Replace((char) 92, (char) 47).Split((char) 47);
            string table = Path.GetFileNameWithoutExtension(parts[parts.Length - 1]);
            string lang = forcedLang ?? parts[parts.Length - 2];
            Dictionary<string, StrFile> d;
            if (!sets[set].TryGetValue(lang, out d)) { d = new Dictionary<string, StrFile>(StringComparer.OrdinalIgnoreCase); sets[set][lang] = d; }
            try { d[table] = new StrFile(z.Read(en)); } catch (Exception e) { Console.WriteLine("BAD " + set + " " + lang + "/" + table + " " + e.Message); }
        }
    }

    static int Main(string[] a)
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object s, ResolveEventArgs e) { string p = Path.Combine(@"C:\Users\Dan\Desktop\ForzaEditor\bin\Debug", new AssemblyName(e.Name).Name + ".exe"); return File.Exists(p) ? Assembly.LoadFrom(p) : null; };
        return Run();
    }

    static int Run()
    {
        string root = @"F:\Xenia\Games\FM4_Extracted\Media\StringTables";
        foreach (string f in Directory.GetFiles(root, "*.zip")) { try { AddZip("BASE", new ForzaZip(f), Path.GetFileNameWithoutExtension(f)); } catch (Exception e) { Console.WriteLine("BASE ERR " + f + " " + e.Message); } }
        string content = @"F:\Xenia\Emulators\Xenia Canary\content\0000000000000000";
        int n = 0;
        foreach (string f in Directory.GetFiles(content, "*", SearchOption.AllDirectories))
        {
            if (!StfsReader.IsPackage(f)) continue;
            StfsReader p; try { p = StfsReader.Open(f); } catch (Exception) { continue; }
            if (GameInfo.Name(GameInfo.FromTitleId(p.TitleId)) != "Forza Motorsport 4") continue;
            string dir = Path.Combine(Path.GetTempPath(), "strau" + (n++));
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            p.Extract(dir);
            foreach (string x in Directory.GetFiles(dir, "StringTables*.zip", SearchOption.AllDirectories))
                AddZip(p.DisplayName, new ForzaZip(x), null);
            foreach (string x in Directory.GetFiles(dir, "stringtables.xml", SearchOption.AllDirectories))
                if (n == 3) Console.WriteLine("--- stringtables.xml of " + p.DisplayName + "\n" + File.ReadAllText(x));
            Directory.Delete(dir, true);
        }
        Console.WriteLine("sets: " + sets.Count);
        foreach (var set in sets)
        {
            Console.WriteLine("=== " + set.Key + "  languages=" + set.Value.Count);
            var tables = new SortedDictionary<string, bool>();
            foreach (var l in set.Value) foreach (string t in l.Value.Keys) tables[t] = true;
            foreach (string t in tables.Keys)
            {
                // reference language EN (or first)
                StrFile en = null; string enLang = null;
                foreach (string cand in new[] { "EN", "en" }) { Dictionary<string, StrFile> d; if (set.Value.TryGetValue(cand, out d) && d.TryGetValue(t, out en)) { enLang = cand; break; } }
                var issues = new List<string>();
                if (en == null) issues.Add("no EN");
                foreach (var l in set.Value)
                {
                    StrFile f;
                    if (!l.Value.TryGetValue(t, out f)) { issues.Add(l.Key + ": MISSING table"); continue; }
                    // order / dups / names
                    var seen = new Dictionary<int, bool>(); int prev = -1; bool sorted = true; int dup = 0, empty = 0, moji = 0, noname = 0, rankBad = 0;
                    foreach (StrFile.Entry e in f.Entries)
                    {
                        if (e.Key < prev) sorted = false; prev = e.Key;
                        if (seen.ContainsKey(e.Key)) dup++; seen[e.Key] = true;
                        if (string.IsNullOrEmpty(e.Text)) empty++;
                        else if (e.Text.IndexOf('\uFFFD') >= 0 || e.Text.Contains("\u00C3") || e.Text.Contains("\u00E2\u20AC")) moji++;
                        if (e.Name == null) noname++;
                    }
                    if (!sorted) issues.Add(l.Key + ": keys not sorted");
                    if (dup > 0) issues.Add(l.Key + ": " + dup + " duplicate keys");
                    if (empty > 0) issues.Add(l.Key + ": " + empty + " empty texts");
                    if (moji > 0) issues.Add(l.Key + ": " + moji + " texts with broken characters");
                    if (noname > 0 && noname < f.Entries.Count) issues.Add(l.Key + ": " + noname + " keys without name");
                    if (false)
                    {
                        var ek = new Dictionary<int, bool>(); foreach (StrFile.Entry e in en.Entries) ek[e.Key] = true;
                        int miss = 0, extra = 0; var lk = new Dictionary<int, bool>();
                        foreach (StrFile.Entry e in f.Entries) { lk[e.Key] = true; if (!ek.ContainsKey(e.Key)) extra++; }
                        foreach (int k in ek.Keys) if (!lk.ContainsKey(k)) miss++;
                        if (miss > 0 || extra > 0) issues.Add(l.Key + ": " + miss + " keys missing vs EN, " + extra + " extra");
                    }
                }
                {
                    var union = new Dictionary<int, int>();
                    foreach (var l in set.Value) { StrFile f; if (l.Value.TryGetValue(t, out f)) foreach (StrFile.Entry e in f.Entries) { int c; union.TryGetValue(e.Key, out c); union[e.Key] = c + 1; } }
                    foreach (var l in set.Value)
                    {
                        StrFile f; if (!l.Value.TryGetValue(t, out f)) continue;
                        var has = new Dictionary<int, bool>(); foreach (StrFile.Entry e in f.Entries) has[e.Key] = true;
                        int miss = 0; int first = -1; foreach (int k in union.Keys) if (!has.ContainsKey(k)) { miss++; if (first < 0) first = k; }
                        if (miss > 0) issues.Add(l.Key + ": " + miss + " keys of the other languages missing (e.g. " + first + ")");
                    }
                }
                Console.WriteLine("  " + t + "  (" + (en != null ? en.Entries.Count : -1) + " keys EN)  " + (issues.Count == 0 ? "ok" : issues.Count + " issues"));
                int shown = 0; foreach (string s in issues) { if (shown++ < 6) Console.WriteLine("      " + s); }
            }
        }
        // cross-set: same table, same key, different EN text
        {
            var keysByTable = new SortedDictionary<string, SortedDictionary<int, bool>>(StringComparer.Ordinal);
            foreach (var set in sets) foreach (var lang in set.Value) foreach (var tf in lang.Value)
            {
                SortedDictionary<int, bool> ks; if (!keysByTable.TryGetValue(tf.Key, out ks)) { ks = new SortedDictionary<int, bool>(); keysByTable[tf.Key] = ks; }
                foreach (StrFile.Entry e in tf.Value.Entries) ks[e.Key] = true;
            }
            var js = new StringBuilder();
            js.Append("{\n  \"format\": \"forza-string-keys\",\n  \"version\": 1,\n  \"notes\": \"Every string key used in each .str table of FM4 (base game and every DLC package, all languages). A new key must not be in the list of its table.\",\n  \"FM4\": {\n");
            int ti = 0;
            foreach (var t in keysByTable)
            {
                js.Append("    \"" + t.Key + "\": [");
                bool first = true; foreach (int k in t.Value.Keys) { if (!first) js.Append(","); js.Append(k); first = false; }
                js.Append("]").Append(++ti < keysByTable.Count ? ",\n" : "\n");
            }
            js.Append("  }\n}\n");
            File.WriteAllText(@"C:\Users\Dan\Desktop\ForzaEditor\docs\FM4\GameStringKeys.json", js.ToString(), new UTF8Encoding(false));
        }
        Console.WriteLine("=== cross-set key conflicts (EN)");
        var nm = new Dictionary<string, string>(); var conf = new Dictionary<string, int>(); int nameClash = 0, textDiff = 0, textDiffEmpty = 0; var clashSamples = new List<string>(); var owner = new Dictionary<string, string>(); var text = new Dictionary<string, string>();
        foreach (var set in sets)
        {
            Dictionary<string, StrFile> d; if (!set.Value.TryGetValue("EN", out d)) continue;
            foreach (var t in d)
                foreach (StrFile.Entry e in t.Value.Entries)
                {
                    string id = t.Key + "#" + e.Key;
                    if (owner.ContainsKey(id)) { if (nm[id] != e.Name) { nameClash++; if (clashSamples.Count < 12) clashSamples.Add(id + " " + owner[id] + ":" + nm[id] + " vs " + set.Key + ":" + e.Name); } if (text[id] != e.Text) { textDiff++; if (string.IsNullOrEmpty(text[id]) || string.IsNullOrEmpty(e.Text)) textDiffEmpty++; } if (text[id] != e.Text) { int cc; conf.TryGetValue(t.Key, out cc); conf[t.Key] = cc + 1; if (cc < 2) Console.WriteLine("  " + id + " " + owner[id] + " <> " + set.Key + "  [" + Cut(text[id]) + "] vs [" + Cut(e.Text) + "]"); } }
                    else { owner[id] = set.Key; text[id] = e.Text; nm[id] = e.Name; }
                }
        }
        Console.WriteLine("name clashes (same table+key, different IDS name): " + nameClash + "; text differs: " + textDiff + " (one side empty: " + textDiffEmpty + ")"); foreach (string cs in clashSamples) Console.WriteLine("   " + cs);
        return 0;
    }
    static void Dummy() {}
    static string Cut(string s) { s = s ?? "null"; return s.Length > 40 ? s.Substring(0, 40) : s; }
}
