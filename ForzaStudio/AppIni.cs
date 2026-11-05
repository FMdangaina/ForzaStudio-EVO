namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;

    // ForzaStudio.ini next to the exe: ALL the tool's settings in one file (user 2026-10-25). Sections:
    //   [App]                 folders, viewer, music, last used paths (key=value; a value runs to the end of the line,
    //                         a semicolon in it is part of it); read and written by AppSettings
    //   [Ids] [PI] [Tires] [Damage] [Lod0]            the port (PortSettings)
    //   [General] [Drivetrain swap] [List_Upgrade...]  the generic upgrades of ported cars (CarUpgrades)
    // A first start (or a missing file) builds it from the old Port.ini, Upgrades.ini and settings.txt when they exist
    // (moved to bak\), else from the defaults.
    internal static class AppIni
    {
        public const string FileName = "ForzaStudio.ini";
        public const string AppSection = "App";

        // Keys the [App] section is born with when nothing older gives them.
        private static readonly string[] AppDefaults = new string[]
        {
            "Viewer.Textures=1", "Viewer.BodyColor=8C8C8C", "Language=EN", "MusicAutoPlay=1", "MusicShuffle=0"
        };

        public static string Path
        {
            get { return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName); }
        }

        private static string Legacy(string name)
        {
            return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
        }

        // The file, made when missing. False when the folder is read-only (the callers then use the defaults).
        public static bool Ensure()
        {
            string path = Path;
            if (File.Exists(path))
            {
                return true;
            }
            try
            {
                StringBuilder text = new StringBuilder();
                text.AppendLine("; ForzaStudio EVO " + System.Windows.Forms.Application.ProductVersion + " - ALL settings in this one file. Edit freely, here or in Tools > Settings.");
                text.AppendLine("; [App] folders, viewer, music, last used paths (key=value, a value may contain a semicolon).");
                text.AppendLine("; [Ids] [PI] [Tires] [Damage] [Lod0] the port.  [General] [Drivetrain swap] [List_Upgrade...] the generic upgrades.");
                text.AppendLine();
                string port = Legacy("Port.ini");
                string upgrades = Legacy("Upgrades.ini");
                string settings = Legacy("settings.txt");
                text.AppendLine(Strip(File.Exists(port) ? File.ReadAllText(port) : PortSettings.DefaultIni));
                text.AppendLine();
                text.AppendLine(Strip(File.Exists(upgrades) ? File.ReadAllText(upgrades) : CarUpgrades.DefaultIni));
                text.AppendLine();
                text.AppendLine("[" + AppSection + "]");
                List<string> keys = new List<string>();
                if (File.Exists(settings))
                {
                    foreach (string line in File.ReadAllLines(settings))
                    {
                        int equals = line.IndexOf('=');
                        if (equals > 0)
                        {
                            keys.Add(line.Substring(0, equals).Trim());
                            text.AppendLine(line.Substring(0, equals).Trim() + "=" + line.Substring(equals + 1).Trim());
                        }
                    }
                }
                foreach (string pair in AppDefaults)
                {
                    if (!keys.Contains(pair.Substring(0, pair.IndexOf('='))))
                    {
                        text.AppendLine(pair);
                    }
                }
                File.WriteAllText(path, text.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n"), Encoding.UTF8);
                string bak = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bak");
                foreach (string old in new string[] { port, upgrades, settings })
                {
                    if (File.Exists(old))
                    {
                        Directory.CreateDirectory(bak);
                        string target = System.IO.Path.Combine(bak, System.IO.Path.GetFileName(old));
                        if (File.Exists(target))
                        {
                            File.Delete(target);
                        }
                        File.Move(old, target);
                    }
                }
                return true;
            }
            catch (IOException)
            {
                return File.Exists(path);
            }
            catch (UnauthorizedAccessException)
            {
                return File.Exists(path);
            }
        }

        // A legacy file without its BOM char and trailing blank lines.
        private static string Strip(string text)
        {
            return text.TrimStart('﻿').Replace("\r\n", "\n").TrimEnd('\n');
        }

        // ---- the [App] section: key=value lines, the value is the rest of the line ----

        private static Dictionary<string, string> app;
        private static DateTime appTime;

        public static string GetApp(string key, string fallback)
        {
            if (!Ensure())
            {
                return fallback;
            }
            try
            {
                DateTime time = File.GetLastWriteTimeUtc(Path);
                if (app == null || time != appTime)
                {
                    Dictionary<string, string> found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    bool inside = false;
                    foreach (string raw in File.ReadAllText(Path).Replace("\r\n", "\n").Split('\n'))
                    {
                        string line = raw.Trim();
                        if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                        {
                            inside = string.Equals(line.Substring(1, line.Length - 2).Trim(), AppSection, StringComparison.OrdinalIgnoreCase);
                            continue;
                        }
                        int equals = line.IndexOf('=');
                        if (inside && equals > 0 && !line.StartsWith(";", StringComparison.Ordinal))
                        {
                            found[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
                        }
                    }
                    app = found;
                    appTime = time;
                }
                string value;
                return app.TryGetValue(key, out value) ? value : fallback;
            }
            catch (IOException)
            {
                return fallback;
            }
        }

        // The [App] keys in file order, as the settings grid lists them: key, value, a short note by the key's prefix.
        public static List<string[]> AppEntries()
        {
            List<string[]> entries = new List<string[]>();
            if (!Ensure())
            {
                return entries;
            }
            bool inside = false;
            foreach (string raw in File.ReadAllText(Path).Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    inside = string.Equals(line.Substring(1, line.Length - 2).Trim(), AppSection, StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                int equals = line.IndexOf('=');
                if (inside && equals > 0 && !line.StartsWith(";", StringComparison.Ordinal))
                {
                    string key = line.Substring(0, equals).Trim();
                    string note = "";
                    if (key.StartsWith("Dir.", StringComparison.OrdinalIgnoreCase))
                    {
                        note = "folder where the file dialog of this kind opens";
                    }
                    else if (key.StartsWith("Folder.", StringComparison.OrdinalIgnoreCase))
                    {
                        note = "game folder (Tools > Settings > FOLDERS)";
                    }
                    else if (key.StartsWith("CarPort.", StringComparison.OrdinalIgnoreCase))
                    {
                        note = "remembered by the port window";
                    }
                    else if (key.StartsWith("Music", StringComparison.OrdinalIgnoreCase))
                    {
                        note = "music player";
                    }
                    else if (key.StartsWith("Viewer.", StringComparison.OrdinalIgnoreCase))
                    {
                        note = key.EndsWith("BodyColor", StringComparison.OrdinalIgnoreCase) ? "body colour in the model viewport, RRGGBB hex" : "model viewport";
                    }
                    entries.Add(new string[] { key, line.Substring(equals + 1).Trim(), note });
                }
            }
            return entries;
        }

        public static void SetApp(string key, string value)
        {
            if (!Ensure())
            {
                return;
            }
            try
            {
                List<string> lines = new List<string>(File.ReadAllText(Path).Replace("\r\n", "\n").TrimEnd('\n').Split('\n'));
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
                        if (string.Equals(line.Substring(1, line.Length - 2).Trim(), AppSection, StringComparison.OrdinalIgnoreCase))
                        {
                            start = i;
                        }
                    }
                }
                if (start < 0)
                {
                    lines.Add("");
                    lines.Add("[" + AppSection + "]");
                    lines.Add(key + "=" + value);
                }
                else
                {
                    int found = -1;
                    int last = start;
                    for (int i = start + 1; i < end; i++)
                    {
                        string line = lines[i].Trim();
                        if (line.Length > 0)
                        {
                            last = i;
                        }
                        int equals = line.IndexOf('=');
                        if (equals > 0 && !line.StartsWith(";", StringComparison.Ordinal) && string.Equals(line.Substring(0, equals).Trim(), key, StringComparison.OrdinalIgnoreCase))
                        {
                            found = i;
                        }
                    }
                    if (found >= 0)
                    {
                        lines[found] = key + "=" + value;
                    }
                    else
                    {
                        lines.Insert(last + 1, key + "=" + value);
                    }
                }
                File.WriteAllText(Path, string.Join("\r\n", lines.ToArray()) + "\r\n", Encoding.UTF8);
                app = null;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
