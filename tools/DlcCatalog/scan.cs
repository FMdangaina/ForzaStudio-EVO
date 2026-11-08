using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using ForzaStudio;

// Builds docs/DlcCatalog.tsv (embedded in the exe, DlcCatalog.cs) and docs/DLC_CATALOG.md from the DLC packages installed in
// Xenia for every game (CarPort.Xenia in ForzaStudio.ini): what each official package file is and which cars it holds.
// scan.exe <docs folder>    (build: x86 csc /r:..\..\bin\Debug\ForzaStudioEVO.exe, run with the dlls of bin\Debug found)
class Scan
{
    static string exe = @"C:\Users\Dan\Desktop\ForzaEditor\bin\Debug\ForzaStudioEVO.exe";

    static int Main(string[] a)
    {
        string bin = Path.GetDirectoryName(exe);
        // FE_EXE: another build of the exe (obj\x86\Debug while the app keeps bin\Debug open); the dlls stay bin\Debug's.
        string fe = Environment.GetEnvironmentVariable("FE_EXE");
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object s, ResolveEventArgs e)
        {
            string n = new AssemblyName(e.Name).Name;
            if (n == "ForzaStudioEVO" && !string.IsNullOrEmpty(fe)) return Assembly.LoadFrom(fe);
            foreach (string ext in new[] { ".exe", ".dll" })
            {
                string p = Path.Combine(bin, n + ext);
                if (File.Exists(p)) return Assembly.LoadFrom(p);
            }
            return null;
        };
        Directory.SetCurrentDirectory(Path.GetDirectoryName(exe));
        return Run(a[0]);
    }

    static int Run(string docs)
    {
        StringWriter tsv = new StringWriter();
        tsv.NewLine = "\n";
        foreach (ForzaGame game in new[] { ForzaGame.Motorsport2, ForzaGame.Motorsport3, ForzaGame.Motorsport4, ForzaGame.Horizon1, ForzaGame.Horizon2 })
        {
            string content = InstalledDlc.ContentFolder(game);
            if (content == null)
            {
                Console.WriteLine(GameInfo.Tag(game) + ": no content folder in Xenia");
                continue;
            }
            DlcCatalog.Build(game, content, tsv, Console.Out);
        }
        File.WriteAllText(Path.Combine(docs, "DlcCatalog.tsv"), tsv.ToString(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(docs, "DLC_CATALOG.md"), Markdown(tsv.ToString()), new UTF8Encoding(false));
        return 0;
    }

    // The same lines as a page to read: per game, per package, its cars.
    static string Markdown(string tsv)
    {
        StringBuilder md = new StringBuilder();
        md.Append("# Official DLC packages per game\n\n");
        md.Append("Built by `tools/DlcCatalog` from the packages installed in Xenia (content\\0000000000000000\\<title id>\\00000002). ");
        md.Append("The program keeps the same data in `docs/DlcCatalog.tsv` (embedded): the port window lists these cars without opening the packages ");
        md.Append("and opens a package only when one of its cars is chosen. Key = STFS header Content ID (0x32C) + file size, so a renamed copy still matches.\n");
        string game = null;
        foreach (string line in tsv.Split('\n'))
        {
            string[] f = line.Split('\t');
            if (f[0] == "P")
            {
                if (f[1] != game)
                {
                    game = f[1];
                    md.Append("\n## " + game + "\n");
                }
                md.AppendFormat(CultureInfo.InvariantCulture, "\n### {0}\n\n- file `{1}`, {2:0.0} MB, offer {3}\n- Content ID `{4}`\n", f[6], f[4], long.Parse(f[3]) / 1048576.0, f[5], f[2]);
                if (f[7].Length > 0) md.Append("- " + f[7] + "\n");
            }
            else if (f[0] == "C")
            {
                md.AppendFormat("- car {0} `{1}` {2} ({3}){4}{5}\n", f[2], f[3], f[4], f[5], f[12] == "1" ? ", traffic close-up" : "", f[14] == "1" ? "" : ", NO model files in the package");
            }
        }
        return md.ToString();
    }
}
