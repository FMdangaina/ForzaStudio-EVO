namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    // The port's own rules, sections [Ids] [PI] [Tires] [Damage] [Lod0] of ForzaStudio.ini (AppIni; it was Port.ini
    // before 2026-10-25; ROADMAP 14.1 "all port settings in clear .ini files", 12.3 Settings window): car and row
    // Ids, the PI line of car DLC packages, the drag tyre row. Tools > Settings > PORT edits them. The generic
    // upgrade rules (CarUpgrades) are in the same file.
    internal static class PortSettings
    {
        public const string FileName = AppIni.FileName;

        public const string DefaultIni =
@"; ForzaStudio EVO - port settings (ROADMAP C2, 14.1)
;
; Edit freely, here or in Tools > Settings > PORT. The sections [General] ... [List_Upgrade...] below are the
; generic upgrades of ported cars.

[Ids]
; No fixed Ids here any more: every new Id (cars and rows) is one above the largest the database, FM4 + its DLC
; (GameIdLimits) and the packs installed in Xenia's content folder use (user 2026-10-25).
; A DLC pack asks for its own first car Id in the pack window.

[PI]
; Car DLC packages come without their base game to compare with: FM4 PI = Slope x source PI + Offset,
; fit on the cars that game shares with FM4 (FH1 2026-10-17, error 0.0145; FM3 2026-10-18, 315 cars,
; error 0.0191).
FH1.Slope = 1.189
FH1.Offset = -0.363
FM3.Slope = 0.7779
FM3.Offset = 0.1668

[Tires]
; FM4 sells drag tyres (compound 9) to 502 of its 503 cars, always level 4 for 10000 Cr. A ported car
; without them gets that row (the next free level if this one is taken). on / off.
DragTires = on
DragLevel = 4
DragPrice = 10000

[Damage]
; How the per-vertex damage words (crash deformation) of a ported FH car are written (ROADMAP B1, checked in game 2026-10-25).
; donor = the words of an FM4 car, vertex by vertex: the same car's own FM4 file when FM4 has it (nearest vertex of the same
; section), else the chosen donor car's, matched by place in each section's box. A part the FM4 car lacks takes the words
; of the nearest part. FH's own words are another quantity (crushed offset in metres): FM4 barely deforms a car with them
; as they are, and turned into FM4 directions they made the car explode in game (2026-11-10), so they are never used.
; donorcar = always the donor car, even when FM4 has the car itself (to try that approximation on a car both games have).
Words = donor
; Added to the `damage` of every DamageScalars point in the CarAttribs of a car ported from FH1 / FH2 (speed 5 / 35 / 98).
; FM4's standard is 0.5 / 1.5 / 2.5; +1 on all levels deforms a ported car much better (checked in game 2026-10-27: Bowler in
; a pack). FM3 and FM2 ports are fine as they are and never get it. 0 = leave the values as they are.
ScalarBonus = 1

[Backup]
; on = a file the tool is about to overwrite (the port, uninstall, duplicate, the editor's saves, thumbnails, packages) is copied
; to bak\ next to the exe first. off = no copies at all: bak\ does not grow (a port copies UI\Textures.zip, 436 MB, every time).
Enabled = on

[Lod0]
; on = a part the donor's _lod0 file has, the car's _lod0 lacks and its cockpit file has (the headlights, which FH keeps
; in the cockpit file) moves into the _lod0 file, where FM4 keeps it and where it deforms; off = the FH layout.
MoveSections = on
";

        public static string IniPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName); }
        }

        private static Dictionary<string, Dictionary<string, string>> cache;
        private static DateTime cacheTime;

        public static string Get(string section, string key, string fallback)
        {
            Dictionary<string, string> values;
            string value;
            return (Load().TryGetValue(section, out values) && values.TryGetValue(key, out value) && value.Length > 0) ? value : fallback;
        }

        public static double GetDouble(string section, string key, double fallback)
        {
            double value;
            return double.TryParse(Get(section, key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        public static long GetLong(string section, string key, long fallback)
        {
            return (long) Math.Round(GetDouble(section, key, fallback));
        }

        public static bool GetBool(string section, string key, bool fallback)
        {
            string value = Get(section, key, fallback ? "on" : "off").ToLowerInvariant();
            return value == "on" || value == "1" || value == "yes" || value == "true";
        }

        // The file's sections (re-read when it changes), with the default sections it lacks.
        private static Dictionary<string, Dictionary<string, string>> Load()
        {
            string path = IniPath;
            DateTime time = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            if (cache != null && time == cacheTime)
            {
                return cache;
            }
            if (time == DateTime.MinValue)
            {
                IniFile.WriteDefaults(path, DefaultIni);
                time = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            }
            cache = IniFile.Load(path, DefaultIni);
            cacheTime = time;
            return cache;
        }
    }

    // [Backup] Enabled of ForzaStudio.ini: whether the copies in bak\ are made at all.
    internal static class BackupPolicy
    {
        public static bool Enabled
        {
            get { return PortSettings.GetBool("Backup", "Enabled", true); }
        }
    }

    // Plain .ini files of the tool (Port.ini, Upgrades.ini): "; comments", [sections], key = value.
    internal static class IniFile
    {
        // A section as written: its comment (the lines under the header before the first key) and its
        // keys in order, each with the comment lines just above it.
        public sealed class Section
        {
            public string Name;
            public string Comment = "";
            public List<string[]> Entries = new List<string[]>();   // key, value, note
        }

        public static List<Section> Layout(string text)
        {
            List<Section> sections = new List<Section>();
            Section section = null;
            List<string> comments = new List<string>();
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith(";", StringComparison.Ordinal))
                {
                    comments.Add(line.Substring(1).Trim());
                    continue;
                }
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    section = new Section();
                    section.Name = line.Substring(1, line.Length - 2).Trim();
                    sections.Add(section);
                    comments.Clear();
                    continue;
                }
                int comment = line.IndexOf(';');
                string body = (comment >= 0) ? line.Substring(0, comment) : line;
                int equals = body.IndexOf('=');
                if (section == null || equals <= 0)
                {
                    if (line.Length == 0 && section != null && section.Entries.Count == 0 && comments.Count > 0)
                    {
                        // A blank line ends the section's own comment.
                        section.Comment = string.Join(" ", comments.ToArray());
                        comments.Clear();
                    }
                    continue;
                }
                if (section.Entries.Count == 0 && section.Comment.Length == 0 && comments.Count > 0 && LooksLikeSectionNote(section.Name))
                {
                    section.Comment = string.Join(" ", comments.ToArray());
                    comments.Clear();
                }
                string note = string.Join(" ", comments.ToArray());
                if (comment >= 0)
                {
                    note = (note + " " + line.Substring(comment + 1).Trim()).Trim();
                }
                section.Entries.Add(new string[] { body.Substring(0, equals).Trim(), body.Substring(equals + 1).Trim(), note });
                comments.Clear();
            }
            return sections;
        }

        // Sections whose first comment describes the section, not its first key.
        private static bool LooksLikeSectionNote(string name)
        {
            return name.StartsWith("Aspiration", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Drivetrain", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("List_", StringComparison.OrdinalIgnoreCase);
        }

        // The file's sections; the sections it lacks come from the defaults (added to them later).
        public static Dictionary<string, Dictionary<string, string>> Load(string path, string defaults)
        {
            Dictionary<string, Dictionary<string, string>> sections = CarUpgrades.ParseIni(File.Exists(path) ? File.ReadAllText(path) : defaults);
            foreach (KeyValuePair<string, Dictionary<string, string>> section in CarUpgrades.ParseIni(defaults))
            {
                if (!sections.ContainsKey(section.Key))
                {
                    sections[section.Key] = section.Value;
                }
            }
            return sections;
        }

        // Writes the defaults (Windows line ends); false when the folder is read-only.
        // The tool's one file (ForzaStudio.ini) is built whole by AppIni (the old files folded in, else the defaults of every part).
        public static bool WriteDefaults(string path, string defaults)
        {
            if (string.Equals(path, AppIni.Path, StringComparison.OrdinalIgnoreCase))
            {
                return AppIni.Ensure();
            }
            try
            {
                File.WriteAllText(path, defaults.Replace("\r\n", "\n").Replace("\n", "\r\n"), Encoding.UTF8);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        // The sections the defaults text holds are put back to their default lines (the rest of the file is untouched);
        // a section the file lacks is added at its end. Used by the Restore Defaults buttons of the one-file ini.
        public static void ResetSections(string path, string defaults)
        {
            AppIni.Ensure();
            List<string> lines = new List<string>(File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd('\n').Split('\n'));
            List<string> source = new List<string>(defaults.TrimStart('﻿').Replace("\r\n", "\n").TrimEnd('\n').Split('\n'));
            for (int i = 0; i < source.Count; i++)
            {
                string head = source[i].Trim();
                if (!(head.StartsWith("[", StringComparison.Ordinal) && head.EndsWith("]", StringComparison.Ordinal)))
                {
                    continue;
                }
                int stop = source.Count;
                for (int j = i + 1; j < source.Count; j++)
                {
                    string next = source[j].Trim();
                    if (next.StartsWith("[", StringComparison.Ordinal) && next.EndsWith("]", StringComparison.Ordinal))
                    {
                        stop = j;
                        break;
                    }
                }
                List<string> block = source.GetRange(i, stop - i);
                while (block.Count > 1 && block[block.Count - 1].Trim().Length == 0)
                {
                    block.RemoveAt(block.Count - 1);
                }
                int start = lines.FindIndex(delegate(string line) { return string.Equals(line.Trim(), head, StringComparison.OrdinalIgnoreCase); });
                if (start < 0)
                {
                    lines.Add("");
                    lines.AddRange(block);
                    continue;
                }
                int end = lines.Count;
                for (int j = start + 1; j < lines.Count; j++)
                {
                    string next = lines[j].Trim();
                    if (next.StartsWith("[", StringComparison.Ordinal) && next.EndsWith("]", StringComparison.Ordinal))
                    {
                        end = j;
                        break;
                    }
                }
                bool blankAfter = end < lines.Count;
                lines.RemoveRange(start, end - start);
                if (blankAfter)
                {
                    block.Add("");
                }
                lines.InsertRange(start, block);
            }
            File.WriteAllText(path, string.Join("\r\n", lines.ToArray()) + "\r\n", Encoding.UTF8);
        }

        // One value changed in place: the comments and the order of the file stay; a key the section
        // lacks is added at its end, a missing section at the end of the file.
        public static void Set(string path, string defaults, string section, string key, string value)
        {
            if (!File.Exists(path))
            {
                WriteDefaults(path, defaults);
            }
            List<string> lines = new List<string>(File.ReadAllText(path).Replace("\r\n", "\n").Split('\n'));
            int start = -1;
            int end = lines.Count;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    if (start >= 0)
                    {
                        end = i;
                        break;
                    }
                    if (string.Equals(line.Substring(1, line.Length - 2).Trim(), section, StringComparison.OrdinalIgnoreCase))
                    {
                        start = i;
                    }
                }
            }
            if (start < 0)
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0)
                {
                    lines.Add("");
                }
                lines.Add("[" + section + "]");
                lines.Add(key + " = " + value);
            }
            else
            {
                int found = -1;
                int last = start;
                for (int i = start + 1; i < end; i++)
                {
                    string line = lines[i];
                    int comment = line.IndexOf(';');
                    string body = (comment >= 0) ? line.Substring(0, comment) : line;
                    int equals = body.IndexOf('=');
                    if (body.Trim().Length > 0)
                    {
                        last = i;
                    }
                    if (equals > 0 && string.Equals(body.Substring(0, equals).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    {
                        found = i;
                    }
                }
                if (found >= 0)
                {
                    string line = lines[found];
                    int comment = line.IndexOf(';');
                    lines[found] = line.Substring(0, line.IndexOf('=')).TrimEnd() + " = " + value + ((comment >= 0) ? "   " + line.Substring(comment) : "");
                }
                else
                {
                    lines.Insert(last + 1, key + " = " + value);
                }
            }
            File.WriteAllText(path, string.Join("\r\n", lines.ToArray()), Encoding.UTF8);
        }
    }
}
