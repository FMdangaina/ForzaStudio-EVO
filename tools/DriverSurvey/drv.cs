using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using ForzaStudio;

// Read-only survey: CDriverControls (16 vec4) of <car>.carbin, <car>_lod0.carbin and <car>_cockpit.carbin of
// every car zip in a folder, plus the Driver element of carattribs.xml. One TSV line per file.
class Drv
{
    static uint U32(byte[] b, int o)
    {
        return ((uint) b[o] << 24) | ((uint) b[o + 1] << 16) | ((uint) b[o + 2] << 8) | b[o + 3];
    }

    static float F(byte[] b, int o)
    {
        byte[] t = { b[o + 3], b[o + 2], b[o + 1], b[o] };
        return BitConverter.ToSingle(t, 0);
    }

    static string N(float v)
    {
        return v.ToString("0.0000", CultureInfo.InvariantCulture);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string Sections(byte[] file)
    {
        // origin of the sections that matter for the driver
        StringBuilder s = new StringBuilder();
        CarbinModel model = new CarbinModel(file);
        foreach (CarbinModel.Section section in model.Sections)
        {
            string name = section.Name ?? "";
            string low = name.ToLowerInvariant();
            if (low.StartsWith("steering_wheel") || low.StartsWith("seat") || low.StartsWith("driver"))
            {
                s.Append(name + "=" + N(section.Bounds[0]) + "," + N(section.Bounds[1]) + "," + N(section.Bounds[2]) + ";");
            }
        }
        return s.ToString();
    }

    static int Main(string[] a)
    {
        string folder = a[0];
        string output = a[1];
        string only = (a.Length > 2) ? a[2] : null;
        StringBuilder text = new StringBuilder();
        text.Append("car\tfile\temitters\tversion");
        for (int v = 0; v < 16; v++)
        {
            text.Append("\tv" + v + "x\tv" + v + "y\tv" + v + "z\tv" + v + "w");
        }
        text.Append("\tstartLod\tendLod\tsections\tdriver\thands\n");
        int cars = 0, failed = 0;
        foreach (string path in Directory.GetFiles(folder, "*.zip"))
        {
            string prefix = Path.GetFileNameWithoutExtension(path);
            if (only != null && prefix.IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
            try
            {
                ForzaZip zip = new ForzaZip(path);
                string driver = "", hands = "";
                ForzaZip.Entry attributes = zip.Find("carattribs.xml");
                if (attributes != null)
                {
                    string xml = Encoding.UTF8.GetString(zip.Read(attributes));
                    Match m = Regex.Match(xml, "<Driver [^>]*/>");
                    if (m.Success)
                    {
                        driver = Regex.Replace(m.Value, "\\s+", " ");
                    }
                    Match h = Regex.Match(xml, "<DriverHandsOffset [^>]*/>");
                    if (h.Success)
                    {
                        hands = Regex.Replace(h.Value, "\\s+", " ");
                    }
                }
                foreach (string suffix in new string[] { "", "_lod0", "_cockpit" })
                {
                    ForzaZip.Entry entry = zip.Find(prefix + suffix + ".carbin");
                    if (entry == null)
                    {
                        text.Append(prefix + "\t" + (suffix == "" ? "main" : suffix.Substring(1)) + "\tMISSING\n");
                        continue;
                    }
                    byte[] file = zip.Read(entry);
                    text.Append(prefix + "\t" + (suffix == "" ? "main" : suffix.Substring(1)));
                    if (file.Length < 0x164 || U32(file, 0) != 2 || U32(file, 0x154) != 0x10)
                    {
                        text.Append("\tBADHEAD " + U32(file, 0) + " " + U32(file, 0x154) + "\n");
                        continue;
                    }
                    int emitters = (int) U32(file, 0x160);
                    int controls = 0x164 + (emitters * 140);
                    text.Append("\t" + emitters + "\t" + U32(file, controls));
                    for (int v = 0; v < 16; v++)
                    {
                        int o = controls + 4 + (v * 16);
                        text.Append("\t" + N(F(file, o)) + "\t" + N(F(file, o + 4)) + "\t" + N(F(file, o + 8)) + "\t" + N(F(file, o + 12)));
                    }
                    int lod = controls + 4 + (16 * 16);
                    text.Append("\t" + U32(file, lod) + "\t" + U32(file, lod + 4));
                    string sections = "";
                    try
                    {
                        sections = Sections(file);
                    }
                    catch (Exception e)
                    {
                        sections = "ERR " + e.GetType().Name;
                    }
                    text.Append("\t" + sections + "\t" + driver + "\t" + hands + "\n");
                }
                cars++;
            }
            catch (Exception e)
            {
                failed++;
                text.Append(prefix + "\tERROR\t" + e.GetType().Name + ": " + e.Message.Replace('\n', ' ') + "\n");
            }
        }
        File.WriteAllText(output, text.ToString());
        Console.WriteLine("cars " + cars + ", failed " + failed + " -> " + output);
        return 0;
    }
}
