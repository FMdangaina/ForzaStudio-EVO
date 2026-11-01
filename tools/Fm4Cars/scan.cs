using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using ForzaStudio;

// Builds docs/FM4/Fm4Cars.tsv: every car of FM4 (the game's database) and of its official DLC packages installed in Xenia.
// Id, MediaName, name, year, make, placement, drive, type, weight, PI, DLC package (empty = the game itself).
class Scan
{
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
        return Run(a[0], a.Length > 1 ? a[1] : null);
    }

    static string Cell(object o)
    {
        return (o == null) ? "" : Convert.ToString(o, CultureInfo.InvariantCulture).Replace('\t', ' ').Replace('\n', ' ');
    }

    static int Run(string output, string folder)
    {
        string game = @"F:\Xenia\Games\FM4_Extracted";
        string content = folder ?? InstalledDlc.ContentFolder(ForzaGame.Motorsport4) ?? @"F:\Xenia\Emulators\Xenia Canary\content\0000000000000000\4D530910\00000002";
        long limit = GameIdLimits.Max(ForzaGame.Motorsport4, "Data_Car", "Id");
        StringTable names = StringTable.Load(@"F:\Xenia\Games\FM4_Extracted_DEV", "EN");
        Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        StringBuilder text = new StringBuilder();
        int baseCars = 0, dlcCars = 0;
        string sql = "SELECT Id, MediaName, DisplayName, Year, MakeID, EnginePlacementID, DriveTypeID, CarTypeID, CurbWeight, PerformanceIndex FROM Data_Car ORDER BY Id";
        using (SqliteDatabase db = SqliteDatabase.FromBytes(File.ReadAllBytes(Path.Combine(game, @"Media\db\gamedb.slt"))))
        {
            foreach (object[] r in db.Query(sql).Rows)
            {
                string media = r[1] as string;
                if (media == null || Convert.ToInt64(r[0]) > limit || seen.ContainsKey(media)) continue;
                seen[media] = true;
                string name = names.Lookup("Data_Car", "DisplayName", r[2] as string);
                text.Append(Cell(r[0]) + "\t" + media + "\t" + (string.IsNullOrEmpty(name) ? media : name));
                for (int i = 3; i < 10; i++) text.Append("\t" + Cell(r[i]));
                text.AppendLine("\t");
                baseCars++;
            }
        }
        List<KeyValuePair<string, byte[]>> merges = new List<KeyValuePair<string, byte[]>>();
        Dictionary<string, InstalledDlc> packs = new Dictionary<string, InstalledDlc>();
        foreach (InstalledDlc dlc in InstalledDlc.Scan(ForzaGame.Motorsport4, content, null))
            if (dlc.Merge != null) { merges.Add(new KeyValuePair<string, byte[]>(dlc.Name, dlc.Merge)); packs[dlc.Name] = dlc; }
        foreach (KeyValuePair<string, byte[]> m in merges)
        {
            // The game's texts with this package's own tables on top (its Data_Car.str names its cars).
            StringTable dlcNames = StringTable.Load(@"F:\Xenia\Games\FM4_Extracted_DEV", "EN");
            foreach (KeyValuePair<string, byte[]> file in packs[m.Key].Source.StringFiles("EN")) dlcNames.Overlay(file.Key, file.Value);
            using (SqliteDatabase db = SqliteDatabase.FromBytes(m.Value))
            {
                if (!DbIds.HasTable(db, "Data_Car")) continue;
                foreach (object[] r in db.Query(sql).Rows)
                {
                    string media = r[1] as string;
                    if (media == null || Convert.ToInt64(r[0]) > limit || seen.ContainsKey(media)) continue;
                    seen[media] = true;
                    string name = dlcNames.Lookup("Data_Car", "DisplayName", r[2] as string);
                    text.Append(Cell(r[0]) + "\t" + media + "\t" + (string.IsNullOrEmpty(name) ? media : name));
                    for (int i = 3; i < 10; i++) text.Append("\t" + Cell(r[i]));
                    text.AppendLine("\t" + m.Key);
                    dlcCars++;
                }
            }
        }
        File.WriteAllText(output, text.ToString(), new UTF8Encoding(false));
        Console.WriteLine("game " + baseCars + ", DLC " + dlcCars + " (" + merges.Count + " packages with a database), limit " + limit);
        return 0;
    }
}
