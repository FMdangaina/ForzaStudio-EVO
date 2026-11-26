using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ForzaStudio
{
    // The record of a port into FM2, kept in the car's zip as port_log.txt (the name the FM4 port uses for the same
    // purpose): the source, the template and the user's choices (upgrade variants left out, materials, engine sound),
    // then the install report. A port again over the car reads it back, so it keeps the original choices
    // (user 2026-11-26: a re-port must keep them; the 17 cars of the rewrite had to get theirs back by hand).
    internal sealed class Fm2PortRecord
    {
        public const string EntryName = "port_log.txt";
        private const string Title = "ForzaStudio EVO port into FM2";
        private const string LogMark = "--- install report ---";

        public string SourceGame = "";          // GameInfo.Name of the source
        public string SourceMediaName = "";
        public string SourcePack = "";          // the DLC pack's name, or ""
        public string Template = "";            // the FM2 car the copy was made from
        public List<string> Skip = new List<string>();
        public Dictionary<string, string> CarMaterials = new Dictionary<string, string>(StringComparer.Ordinal);
        public Dictionary<string, string> WheelMaterials = new Dictionary<string, string>(StringComparer.Ordinal);
        public string UseSoundOf;               // null: the source's sound was ported
        public List<string> Log = new List<string>();

        public string ToText()
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine(Title);
            text.AppendLine("written=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            text.AppendLine("source game=" + this.SourceGame);
            text.AppendLine("source car=" + this.SourceMediaName);
            text.AppendLine("source pack=" + this.SourcePack);
            text.AppendLine("template=" + this.Template);
            text.AppendLine("skip=" + string.Join(",", this.Skip.ToArray()));
            foreach (KeyValuePair<string, string> pair in this.CarMaterials)
            {
                text.AppendLine("car material=" + pair.Key + ":" + pair.Value);
            }
            foreach (KeyValuePair<string, string> pair in this.WheelMaterials)
            {
                text.AppendLine("wheel material=" + pair.Key + ":" + pair.Value);
            }
            text.AppendLine("sound=" + (string.IsNullOrEmpty(this.UseSoundOf) ? "port" : "use:" + this.UseSoundOf));
            text.AppendLine(LogMark);
            foreach (string line in this.Log)
            {
                text.AppendLine(line);
            }
            return text.ToString();
        }

        // null: not a record of this kind.
        public static Fm2PortRecord Parse(string text)
        {
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 0 || lines[0].Trim() != Title)
            {
                return null;
            }
            Fm2PortRecord record = new Fm2PortRecord();
            bool log = false;
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (log)
                {
                    if (line.Length > 0 || i < lines.Length - 1)
                    {
                        record.Log.Add(line);
                    }
                    continue;
                }
                if (line == LogMark)
                {
                    log = true;
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq < 0)
                {
                    continue;
                }
                string key = line.Substring(0, eq);
                string value = line.Substring(eq + 1);
                switch (key)
                {
                    case "source game": record.SourceGame = value; break;
                    case "source car": record.SourceMediaName = value; break;
                    case "source pack": record.SourcePack = value; break;
                    case "template": record.Template = value; break;
                    case "skip":
                        record.Skip.AddRange(value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                        break;
                    case "car material":
                    case "wheel material":
                        int colon = value.IndexOf(':');
                        if (colon > 0)
                        {
                            (key == "car material" ? record.CarMaterials : record.WheelMaterials)[value.Substring(0, colon)] = value.Substring(colon + 1);
                        }
                        break;
                    case "sound":
                        record.UseSoundOf = value.StartsWith("use:", StringComparison.Ordinal) ? value.Substring(4) : null;
                        break;
                }
            }
            return record;
        }

        // The record in the car zip, or null (no zip, no record, another kind of port_log.txt).
        public static Fm2PortRecord Read(string carZip)
        {
            if (carZip == null || !File.Exists(carZip))
            {
                return null;
            }
            try
            {
                ForzaZip zip = new ForzaZip(carZip);
                ForzaZip.Entry entry = zip.Find(EntryName);
                return (entry == null) ? null : Parse(Encoding.UTF8.GetString(zip.Read(entry)));
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Into the car zip (replaced when there), then the zip's zipmanifest.xml line.
        public void Write(string gameRoot, string carZip)
        {
            ForzaZip zip = new ForzaZip(carZip);
            byte[] data = Encoding.UTF8.GetBytes(this.ToText());
            ForzaZip.Entry entry = zip.Find(EntryName);
            if (entry != null)
            {
                zip.Replace(entry, data);
            }
            else
            {
                zip.Add(EntryName, data);
            }
            zip.Save(carZip);
            string gamePath = ZipManifest.GamePath(gameRoot, carZip);
            if (gamePath == null || !ZipManifest.Update(ZipManifest.PathFor(gameRoot), gamePath, carZip))
            {
                throw new InvalidOperationException("zipmanifest.xml has no line for " + carZip + ".");
            }
        }
    }
}
