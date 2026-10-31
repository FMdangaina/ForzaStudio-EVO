namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;

    // Ported cars as a Forza Motorsport 4 DLC package, so the game's own files stay untouched.
    //
    // What a package holds was read from an official FM4 car pack (offer 31, 10 cars) and from help
    // given on Discord:
    //   Jew (@mr.abortion)  the fm4dlc toolchain and its notes: the package layout, the merge database,
    //                       the string tables and the STFS writer;
    //   XTD (@xtd_ac)       the folder DLC that Xenia loads as it is, a package built with the Xbox 360
    //                       authoring tool (XLAST) and tests on the console.
    //
    // Layout (content\<profile>\4D530910\00000002\<any name>\):
    //   Media\<offer>.puboffer                 3 bytes
    //   Media\DLCZips\zipmount.xml             the zips to mount and where
    //   Media\DLCZips\stringtables.xml         the tables that get strings
    //   Media\DLCZips\StringTables_pri_<P>.zip <LANG>/<Table>.str, the WHOLE table (the game's strings
    //                                          plus the new ones): it replaces the game's table
    //   Media\DLCZips\<offer>_pri_65\Media\... loose files laid out like the game's Media folder,
    //                                          among them db\patch\<offer>000_merge.slt
    public static class DlcPackage
    {
        public const string TitleId = "4D530910";

        // The package folder for Xenia, built from a trial run of the port: "target" is the game the trial
        // started from, "trial" its output (same layout), "clean" an untouched FM4 whose string tables
        // the package's tables are built on. The folder is <content>\<offer id>; Xenia loads it as it is
        // (XTD), under content\0000000000000000\4D530910\00000002. Returns the folder.
        //   database  db\patch\<offer>000_merge.slt (BuildMerge), the offer named after the pack;
        //   strings   StringTables_pri_<P>.zip: every table the port changed, as the clean game's table
        //             plus the port's strings, and ContentOffers with the pack's name, in every language;
        //   files     a car or wheel zip of the pack's cars whole, of the other zips (audio tunings, UI
        //             textures) the entries the port added or changed, loose files the port added (sound
        //             banks): all loose under <offer>_pri_65\Media, where the game mounts the zip.
        public static string BuildFolder(string cleanRoot, string targetRoot, string trialRoot, string content, int offer, string name, StringBuilder report)
        {
            return BuildFolder(cleanRoot, targetRoot, trialRoot, content, content, offer, name, report);
        }

        // installed: the FM4 content folder whose packages' string tables the new tables are built on
        // (Xenia's; 'content' is a temporary folder when the package is for a console).
        public static string BuildFolder(string cleanRoot, string targetRoot, string trialRoot, string content, string installed, int offer, string name, StringBuilder report)
        {
            string layerTemp = Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "StringLayers_" + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture));
            try
            {
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                List<StringLayer> layers = StringLayer.Installed(installed, layerTemp, report);
                Time(report, watch, "reading the installed packs' string tables");
                return BuildFolderWith(cleanRoot, targetRoot, trialRoot, content, installed, layers, offer, name, report);
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

        // "time: <step> n s" in the report since the last call, the stopwatch starts again.
        private static void Time(StringBuilder report, System.Diagnostics.Stopwatch watch, string step)
        {
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "time: {0} {1:0.0} s", step, watch.Elapsed.TotalSeconds));
            watch.Reset();
            watch.Start();
        }

        private static string BuildFolderWith(string cleanRoot, string targetRoot, string trialRoot, string content, string installed, List<StringLayer> layers, int offer, string name, StringBuilder report)
        {
            string package = Path.Combine(content, OfferId(offer));
            if (Directory.Exists(package))
            {
                throw new IOException("The package folder already exists: " + package);
            }
            string zips = Path.Combine(Path.Combine(package, "Media"), "DLCZips");
            string loose = Path.Combine(Path.Combine(zips, offer.ToString(CultureInfo.InvariantCulture) + "_pri_65"), "Media");
            Directory.CreateDirectory(loose);
            File.WriteAllBytes(Path.Combine(Path.Combine(package, "Media"), offer.ToString(CultureInfo.InvariantCulture) + ".puboffer"), new byte[] { 0x20, 0x0D, 0x0A });

            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            report.AppendLine("database:");
            byte[] targetDb = File.ReadAllBytes(GameDb(targetRoot));
            byte[] merge = BuildMerge(targetDb, File.ReadAllBytes(GameDb(trialRoot)), offer, report);
            Time(report, watch, "merge database");
            Dictionary<string, bool> media = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            long hi;
            using (SqliteDatabase game = SqliteDatabase.FromBytes(targetDb))
            {
                hi = StringTable(game.Scalar("SELECT DisplayName FROM ContentOffers WHERE DisplayName LIKE '\\_&%' ESCAPE '\\' LIMIT 1")) >> 16;
            }

            report.AppendLine("strings:");
            // Above every installed layer (a table of a higher priority replaces the whole table).
            int priority = 50000 + offer;
            foreach (StringLayer layer in layers)
            {
                priority = Math.Max(priority, layer.Priority + 1);
            }
            string strings = "StringTables_pri_" + priority.ToString(CultureInfo.InvariantCulture) + ".zip";
            int displayKey;
            int descriptionKey;
            List<string> tables = BuildStrings(cleanRoot, targetRoot, trialRoot, Path.Combine(zips, strings), offer, name, layers, out displayKey, out descriptionKey, report);
            Time(report, watch, "string tables");
            using (SqliteDatabase patch = SqliteDatabase.FromBytes(merge))
            {
                RelocateKeys(patch, installed, targetRoot, package, report);
                patch.Execute("UPDATE ContentOffers SET DisplayName = ?, Description = ?",
                    "_&" + ((hi << 16) | (long) displayKey).ToString(CultureInfo.InvariantCulture),
                    "_&" + ((hi << 16) | (long) descriptionKey).ToString(CultureInfo.InvariantCulture));
                // The merge holds only the tables that got rows: a car copy adds no wheel row.
                string mediaNames = DbIds.HasTable(patch, "List_Wheels") ? "SELECT MediaName FROM Data_Car UNION SELECT MediaName FROM List_Wheels" : "SELECT MediaName FROM Data_Car";
                foreach (object[] row in patch.Query(mediaNames).Rows)
                {
                    media[(string) row[0]] = true;
                }
                merge = patch.ToBytes();
            }
            string patchFolder = Path.Combine(Path.Combine(loose, "db"), "patch");
            Directory.CreateDirectory(patchFolder);
            File.WriteAllBytes(Path.Combine(patchFolder, offer.ToString(CultureInfo.InvariantCulture) + "000_merge.slt"), merge);

            Time(report, watch, "relocating keys + writing the merge");
            report.AppendLine("files:");
            CopyPortedFiles(targetRoot, trialRoot, loose, media, offer, report);
            Time(report, watch, "copying the ported files");

            // The two lists, in the official files' form (" \r\n" line ends).
            WriteXml(Path.Combine(zips, "zipmount.xml"), "zipmount",
                new string[] { "   <zip Name=\"" + strings + "\" Mount=\"game:\\Media\\StringTables\\\"  /> " });
            List<string> lines = tables.ConvertAll<string>(delegate(string table) { return "   <table Name=\"" + table + "\" /> "; });
            WriteXml(Path.Combine(zips, "stringtables.xml"), "stringtables", lines.ToArray());
            // Xenia lists a content folder only with its header next to it (Headers\<type>\<name>.header);
            // without it the game's enumeration of type 00000002 comes back empty.
            string header = HeaderPath(content, offer);
            Directory.CreateDirectory(Path.GetDirectoryName(header));
            File.WriteAllBytes(header, XeniaHeader(OfferId(offer), name));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "package {0}: offer {1} \"{2}\", strings priority {3}", package, offer, name, priority));
            return package;
        }

        // The package for a real Xbox 360 (modded: RGH/JTAG), from the Xenia package folder: an STFS file
        // (StfsPackage) in <output>\Content\0000000000000000\4D530910\00000002\, ready to copy to the
        // console's Hdd1:\ by FTP. Checked on a console 2026-10-16. Returns the file.
        // Names in an STFS package hold at most 40 characters and brake carbins exceed it
        // (lam_gallardolp570_11_caliperlf_lod0.carbin: 42), so the loose <offer>_pri_65 folder goes in as
        // <offer>_pri_65.zip, as in the official packs (Jew) and XTD's: stored entries in the Forza layout;
        // the game mounts it like the folder. The *_build_report.html files stay out.
        public static string BuildConsolePackage(string packageFolder, string output, string name, byte[] thumbnail, StringBuilder report)
        {
            string stage = Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "Stfs_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
            string temp = stage + ".package";
            try
            {
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                CopyForConsole(packageFolder, stage, report);
                Time(report, watch, "console folder (zips of the _pri folders)");
                string id = StfsPackage.Write(stage, temp, uint.Parse(TitleId, NumberStyles.HexNumber, CultureInfo.InvariantCulture), 2, name,
                    "ForzaStudio EVO DLC pack", "Forza Motorsport 4", thumbnail);
                Time(report, watch, "STFS package");
                // Straight into the folder the user chose: no Content\0000000000000000\<title>\00000002
                // tree (user 2026-10-20); the file name is the content id, as the console wants it.
                Directory.CreateDirectory(output);
                string file = Path.Combine(output, id);
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
                File.Move(temp, file);
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "Xbox 360 package {0} ({1:0.0} MB)", file, new FileInfo(file).Length / 1048576.0));
                return file;
            }
            finally
            {
                if (Directory.Exists(stage))
                {
                    Directory.Delete(stage, true);
                }
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
        }

        // The package folder copied for the console: every <x>_pri_<n> folder under DLCZips as a zip.
        private static void CopyForConsole(string from, string to, StringBuilder report)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from))
            {
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
            }
            foreach (string folder in Directory.GetDirectories(from))
            {
                string name = Path.GetFileName(folder);
                if (name.IndexOf("_pri_", StringComparison.OrdinalIgnoreCase) > 0 && string.Equals(Path.GetFileName(from), "DLCZips", StringComparison.OrdinalIgnoreCase))
                {
                    int count = WriteStoredZip(folder, Path.Combine(to, name + ".zip"));
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0}.zip: {1} files", name, count));
                    continue;
                }
                CopyForConsole(folder, Path.Combine(to, name), report);
            }
        }

        // A zip of a folder's files (names relative to it, '/' separated, sorted), LZX like the game's own zips (stored only
        // when LZX does not make a file smaller), in the layout of
        // the game's zips: version 10, date field 0x21 as Jew's fm4dlc writes them, and the
        // 0x1123 extra field of the central record holding the data offset the game reads.
        internal static int WriteStoredZip(string folder, string path)
        {
            List<string> files = new List<string>();
            foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (!file.EndsWith("_build_report.html", StringComparison.OrdinalIgnoreCase))
                {
                    files.Add(file);
                }
            }
            string root = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            // Sorted by the '/' name, lower case (as the console demo checked on 2026-10-16 was written).
            files.Sort(delegate(string a, string b)
            {
                string x = Path.GetFullPath(a).Substring(root.Length).Replace('\\', '/').ToLowerInvariant();
                string y = Path.GetFullPath(b).Substring(root.Length).Replace('\\', '/').ToLowerInvariant();
                return string.CompareOrdinal(x, y);
            });
            MemoryStream central = new MemoryStream();
            using (FileStream zip = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                BinaryWriter writer = new BinaryWriter(zip);
                foreach (string file in files)
                {
                    byte[] name = Encoding.ASCII.GetBytes(Path.GetFullPath(file).Substring(root.Length).Replace('\\', '/'));
                    byte[] raw = File.ReadAllBytes(file);
                    uint crc = ForzaZip.Crc32(raw);
                    byte[] data = XMemLzx.Compress(raw);
                    ushort method = ForzaZip.MethodLzx;
                    if (data.Length >= raw.Length)
                    {
                        data = raw;
                        method = ForzaZip.MethodStored;
                    }
                    uint offset = (uint) zip.Position;
                    writer.Write(0x04034b50);
                    writer.Write((ushort) 10);
                    writer.Write((ushort) 0);
                    writer.Write(method);
                    writer.Write((ushort) 0);
                    writer.Write((ushort) 0x21);
                    writer.Write(crc);
                    writer.Write(data.Length);
                    writer.Write(raw.Length);
                    writer.Write((ushort) name.Length);
                    writer.Write((ushort) 0);
                    writer.Write(name);
                    uint dataOffset = (uint) zip.Position;
                    writer.Write(data);
                    BinaryWriter record = new BinaryWriter(central);
                    record.Write(0x02014b50);
                    record.Write((ushort) 10);
                    record.Write((ushort) 10);
                    record.Write((ushort) 0);
                    record.Write(method);
                    record.Write((ushort) 0);
                    record.Write((ushort) 0x21);
                    record.Write(crc);
                    record.Write(data.Length);
                    record.Write(raw.Length);
                    record.Write((ushort) name.Length);
                    record.Write((ushort) 8);
                    record.Write((ushort) 0);
                    record.Write((ushort) 0);
                    record.Write((ushort) 0);
                    record.Write(0);
                    record.Write(offset);
                    record.Write(name);
                    record.Write((ushort) 0x1123);
                    record.Write((ushort) 4);
                    record.Write(dataOffset);
                }
                uint start = (uint) zip.Position;
                central.WriteTo(zip);
                writer.Write(0x06054b50);
                writer.Write((ushort) 0);
                writer.Write((ushort) 0);
                writer.Write((ushort) files.Count);
                writer.Write((ushort) files.Count);
                writer.Write((uint) central.Length);
                writer.Write(start);
                writer.Write((ushort) 0);
            }
            return files.Count;
        }

        // Xenia's folder of marketplace content for FM4 (content\0000000000000000\4D530910\00000002), from
        // the emulator's folder: its config's content_root when set, else <emulator>\content.
        public static string XeniaContent(string xenia)
        {
            return XeniaContent(xenia, TitleId);
        }

        // The same for any game's title id (hex, e.g. 4D530910). The folder typed in Settings may be any level of the path:
        // the emulator's folder, its content folder, content\0000000000000000, or the whole ...\<title id>\00000002
        // (user 2026-10-27: he had typed it up to content\0000000000000000).
        public static string XeniaContent(string xenia, string title)
        {
            string root = Path.Combine(xenia, "content");
            string value = XeniaSetting(xenia, "content_root");
            if (!string.IsNullOrEmpty(value))
            {
                root = Path.IsPathRooted(value) ? value : Path.Combine(xenia, value);
            }
            string standard = Path.Combine(Path.Combine(Path.Combine(root, "0000000000000000"), title), "00000002");
            string typed = xenia.TrimEnd('\\', '/');
            string[] candidates = new string[]
            {
                standard,
                Path.Combine(Path.Combine(Path.Combine(Path.Combine(typed, "content"), "0000000000000000"), title), "00000002"),
                Path.Combine(Path.Combine(Path.Combine(typed, "0000000000000000"), title), "00000002"),
                Path.Combine(Path.Combine(typed, title), "00000002"),
                Path.Combine(typed, "00000002"),
                typed,
            };
            foreach (string candidate in candidates)
            {
                if (Directory.Exists(candidate) && candidate.TrimEnd('\\').EndsWith("00000002", StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
            return standard;
        }

        // The emulator's config file (xenia-canary.config.toml, or xenia.config.toml), or null.
        public static string XeniaConfig(string xenia)
        {
            foreach (string name in new string[] { "xenia-canary.config.toml", "xenia.config.toml" })
            {
                string path = Path.Combine(xenia, name);
                if (File.Exists(path))
                {
                    return path;
                }
            }
            return null;
        }

        // A value of the config ("name = value  # comment"), without quotes; null when absent.
        public static string XeniaSetting(string xenia, string name)
        {
            string config = XeniaConfig(xenia);
            if (config == null)
            {
                return null;
            }
            foreach (string line in File.ReadAllLines(config))
            {
                string text = line.Trim();
                if (!text.StartsWith(name + " ") && !text.StartsWith(name + "="))
                {
                    continue;
                }
                string value = text.Substring(text.IndexOf('=') + 1);
                int comment = value.IndexOf('#');
                if (comment >= 0)
                {
                    value = value.Substring(0, comment);
                }
                return value.Trim().Trim('"', '\'');
            }
            return null;
        }

        // The offers of the packages in a content folder (Media\<offer>.puboffer of every package).
        public static List<int> InstalledOffers(string content)
        {
            List<int> offers = new List<int>();
            if (!Directory.Exists(content))
            {
                return offers;
            }
            foreach (string package in Directory.GetDirectories(content))
            {
                string media = Path.Combine(package, "Media");
                if (!Directory.Exists(media))
                {
                    continue;
                }
                foreach (string file in Directory.GetFiles(media, "*.puboffer"))
                {
                    int offer;
                    if (int.TryParse(Path.GetFileNameWithoutExtension(file), NumberStyles.None, CultureInfo.InvariantCulture, out offer))
                    {
                        offers.Add(offer);
                    }
                }
            }
            return offers;
        }

        // An offer number neither the game nor an installed package uses, from 91 (official packs: up
        // to the 40s).
        public static int FreeOffer(string targetRoot, string content)
        {
            // Max of everything installed + 1 (user rule 2026-10-31): FM4 and its official packs, the game's
            // database, the packages in Xenia (puboffer files and their merge databases).
            int offer = (int) GameIdLimits.Max(ForzaGame.Motorsport4, "ContentOffers", "PubOfferingID");
            foreach (string pack in DbIds.PackDatabases(content))
            {
                try
                {
                    using (SqliteDatabase merge = SqliteDatabase.FromBytes(File.ReadAllBytes(pack)))
                    {
                        object top = DbIds.HasTable(merge, "ContentOffers") ? merge.Scalar("SELECT max(PubOfferingID) FROM ContentOffers") : null;
                        if (top != null && top != DBNull.Value)
                        {
                            offer = Math.Max(offer, (int) Convert.ToInt64(top));
                        }
                    }
                }
                catch (Exception)
                {
                }
            }
            using (SqliteDatabase game = SqliteDatabase.FromBytes(File.ReadAllBytes(GameDb(targetRoot))))
            {
                object max = game.Scalar("SELECT max(PubOfferingID) FROM ContentOffers");
                if (max != null)
                {
                    offer = Math.Max(offer, (int) Convert.ToInt64(max));
                }
            }
            foreach (int installed in InstalledOffers(content))
            {
                offer = Math.Max(offer, installed);
            }
            return offer + 1;
        }

        // The pack's cars and wheels the game itself already has as zips (an earlier install into the
        // game): the game mounts its own zips at priority 80, above the package's loose files (65), so
        // it shows those instead of the pack's (seen 2026-10-15: material fixes "not applied").
        public static List<string> ShadowedBy(string targetRoot, IEnumerable<string> mediaNames)
        {
            List<string> found = new List<string>();
            foreach (string media in mediaNames)
            {
                foreach (string kind in new string[] { "cars", "wheels" })
                {
                    string zip = Path.Combine(Path.Combine(Path.Combine(targetRoot, "Media"), kind), media + ".zip");
                    if (File.Exists(zip))
                    {
                        found.Add("Media\\" + kind + "\\" + Path.GetFileName(zip));
                    }
                }
            }
            return found;
        }

        // The package folder of an offer and its Xenia header.
        public static string PackagePath(string content, int offer)
        {
            return Path.Combine(content, OfferId(offer));
        }

        public static string HeaderPath(string content, int offer)
        {
            string full = Path.GetFullPath(content).TrimEnd('\\');
            return Path.Combine(Path.Combine(Path.Combine(Path.GetDirectoryName(full), "Headers"), Path.GetFileName(full)), OfferId(offer) + ".header");
        }

        public static void Remove(string content, int offer)
        {
            string package = PackagePath(content, offer);
            if (Directory.Exists(package))
            {
                Directory.Delete(package, true);
            }
            string header = HeaderPath(content, offer);
            if (File.Exists(header))
            {
                File.Delete(header);
            }
        }

        // The header file Xenia keeps for an installed content folder, as it writes them itself
        // (content\...\Headers\00000001\marketplace.header): 0xA000 bytes, the STFS header ("CON ",
        // header size 0x971A, content type, metadata version 2, title id, display name, title name)
        // followed at 0x971A by the folder name.
        public static byte[] XeniaHeader(string folder, string displayName)
        {
            byte[] header = new byte[0xA000];
            Encoding.ASCII.GetBytes("CON ").CopyTo(header, 0);
            PutBig(header, 0x340, 0x971A);
            PutBig(header, 0x344, 2);                                       // content type: marketplace content
            PutBig(header, 0x348, 2);                                       // metadata version
            PutBig(header, 0x360, uint.Parse(TitleId, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            PutText(header, 0x411, 0x80, displayName);
            PutText(header, 0x1691, 0x80, "Forza Motorsport 4");
            byte[] name = Encoding.ASCII.GetBytes(folder);
            Buffer.BlockCopy(name, 0, header, 0x971A, name.Length);
            return header;
        }

        private static void PutBig(byte[] data, int offset, uint value)
        {
            data[offset] = (byte) (value >> 24);
            data[offset + 1] = (byte) (value >> 16);
            data[offset + 2] = (byte) (value >> 8);
            data[offset + 3] = (byte) value;
        }

        private static void PutText(byte[] data, int offset, int size, string text)
        {
            byte[] bytes = Encoding.BigEndianUnicode.GetBytes(text);
            Buffer.BlockCopy(bytes, 0, data, offset, Math.Min(bytes.Length, size - 2));
        }

        private static string GameDb(string root)
        {
            return Path.Combine(Path.Combine(Path.Combine(root, "Media"), "db"), "gamedb.slt");
        }

        private static long StringTable(object reference)
        {
            uint id;
            string text = reference as string;
            if (text == null || !text.StartsWith("_&") || !uint.TryParse(text.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out id))
            {
                throw new InvalidOperationException("The game database has no ContentOffers string to take the table id from.");
            }
            return id;
        }

        internal static void WriteXml(string path, string root, string[] lines)
        {
            StringBuilder xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\" ?> \r\n<").Append(root).Append("> \r\n");
            foreach (string line in lines)
            {
                xml.Append(line).Append("\r\n");
            }
            xml.Append("</").Append(root).Append("> \r\n");
            File.WriteAllBytes(path, Encoding.ASCII.GetBytes(xml.ToString()));
        }

        // Every table the port changed in a language zip of the trial: the clean game's table plus the
        // strings the port added or changed (so strings an earlier port left in the target game stay
        // out). ContentOffers gets the pack's name under keys free in every language. A package's table
        // replaces the game's whole table (official offer 31: EN Data_Car.str = the game's 1006 keys,
        // same hashes, + 186), so it must hold the game's strings too.
        private static List<string> BuildStrings(string cleanRoot, string targetRoot, string trialRoot, string output, int offer, string name, List<StringLayer> layers, out int displayKey, out int descriptionKey, StringBuilder report)
        {
            int conflicts = 0;
            SortedDictionary<string, StrFile> files = new SortedDictionary<string, StrFile>(StringComparer.Ordinal);
            Dictionary<string, bool> tables = new Dictionary<string, bool>(StringComparer.Ordinal);
            string cleanTables = Path.Combine(Path.Combine(cleanRoot, "Media"), "StringTables");
            ForzaZip model = null;
            foreach (string trialZip in Directory.GetFiles(Path.Combine(Path.Combine(trialRoot, "Media"), "StringTables"), "*.zip"))
            {
                string language = Path.GetFileNameWithoutExtension(trialZip);
                string cleanZip = Path.Combine(cleanTables, Path.GetFileName(trialZip));
                string targetZip = Path.Combine(Path.Combine(Path.Combine(targetRoot, "Media"), "StringTables"), Path.GetFileName(trialZip));
                if (!File.Exists(cleanZip) || !File.Exists(targetZip))
                {
                    continue;
                }
                ForzaZip trial = new ForzaZip(trialZip);
                ForzaZip target = new ForzaZip(targetZip);
                ForzaZip clean = new ForzaZip(cleanZip);
                if (model == null)
                {
                    model = clean;
                }
                foreach (ForzaZip.Entry entry in trial.Entries)
                {
                    ForzaZip.Entry before = target.Find(entry.Name);
                    if (!entry.Name.EndsWith(".str", StringComparison.OrdinalIgnoreCase) || (before != null && before.Crc == entry.Crc && before.UncompressedSize == entry.UncompressedSize))
                    {
                        continue;
                    }
                    ForzaZip.Entry original = clean.Find(entry.Name);
                    if (original == null)
                    {
                        continue;
                    }
                    StrFile ported = new StrFile(trial.Read(entry));
                    StrFile old = (before != null) ? new StrFile(target.Read(before)) : null;
                    StrFile result = new StrFile(clean.Read(original));
                    StringLayer.Merge(result, layers, language, entry.Name);
                    int added = 0;
                    foreach (StrFile.Entry text in ported.Entries)
                    {
                        StrFile.Entry was = (old != null) ? Find(old, text.Key) : null;
                        if (was != null && was.Text == text.Text && was.Name == text.Name)
                        {
                            continue;
                        }
                        StrFile.Entry present = Find(result, text.Key);
                        if (present != null && present.Name != null && text.Name != null && present.Name != text.Name)
                        {
                            // The port chose this key against the game's table alone: an installed pack
                            // already uses it for another string, which the port's now replaces.
                            conflicts++;
                            if (language == "EN")
                            {
                                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  WARNING {0}: key {1:X4} was \"{2}\" in an installed pack, now \"{3}\"", Path.GetFileNameWithoutExtension(entry.Name), text.Key, present.Name, text.Name));
                            }
                        }
                        result.Set(text.Key, text.Text, text.Name);
                        added++;
                    }
                    string table = Path.GetFileNameWithoutExtension(entry.Name);
                    files[language + "/" + table] = result;
                    tables[table] = true;
                    if (language == "EN")
                    {
                        report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0}: {1} strings of the port", table, added));
                    }
                }
                ForzaZip.Entry offers = clean.Find("ContentOffers.str");
                if (offers != null)
                {
                    StrFile offerTable = new StrFile(clean.Read(offers));
                    StringLayer.Merge(offerTable, layers, language, "ContentOffers.str");
                    files[language + "/ContentOffers"] = offerTable;
                }
            }
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  built on the game's strings + {0} installed FM4 pack layers{1}", layers.Count, conflicts > 0 ? ", " + conflicts + " key conflicts (see WARNING)" : ""));
            if (model == null)
            {
                throw new InvalidOperationException("No string tables to build on (clean game: " + cleanRoot + ").");
            }

            // The pack's name: the two highest keys free in every language's ContentOffers.str.
            List<StrFile> offerFiles = new List<StrFile>();
            foreach (KeyValuePair<string, StrFile> pair in files)
            {
                if (pair.Key.EndsWith("/ContentOffers", StringComparison.Ordinal))
                {
                    offerFiles.Add(pair.Value);
                }
            }
            displayKey = FreeKey(offerFiles, 0xFFFE);
            descriptionKey = FreeKey(offerFiles, displayKey - 1);
            string id = OfferId(offer);
            foreach (StrFile file in offerFiles)
            {
                file.Set(displayKey, name, "IDS_DisplayName_" + id);
                file.Set(descriptionKey, name, "IDS_Description_" + id);
            }
            tables["ContentOffers"] = true;
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  ContentOffers: \"{0}\" as keys {1:X4} and {2:X4}", name, displayKey, descriptionKey));

            // Entries <LANG>/<Table>.str, LZX like the game's tables (a stored .str of the game would be
            // the template otherwise: AIDiff.str).
            model.Entries.RemoveAll(delegate(ForzaZip.Entry entry) { return entry.Method != ForzaZip.MethodLzx; });
            ForzaZip archive = ForzaZip.CreateLike(model);
            foreach (KeyValuePair<string, StrFile> pair in files)
            {
                archive.Add(pair.Key + ".str", pair.Value.ToBytes());
            }
            archive.Save(output);
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0}: {1} tables in {2} languages", Path.GetFileName(output), tables.Count, files.Count / Math.Max(tables.Count, 1)));
            List<string> list = new List<string>(tables.Keys);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        // The string tables of an installed FM4 package (StringTables_pri_<P>.zip, "<LANG>/<Table>.str").
        // A table replaces the game's whole table, so a new pack's tables are built on the game's plus
        // every layer's strings, lowest priority first (user 2026-10-22: a pack must hold ALL the strings,
        // then its own).
        private sealed class StringLayer
        {
            public int Priority;
            public string Label;
            private ForzaZip zip;
            private Dictionary<string, ForzaZip.Entry> entries;

            // The layers of the packages in an FM4 content folder: STFS files (the console's form) and
            // extracted folders. Unreadable ones are reported and skipped.
            public static List<StringLayer> Installed(string content, string temp, StringBuilder report)
            {
                List<StringLayer> layers = new List<StringLayer>();
                if (string.IsNullOrEmpty(content) || !Directory.Exists(content))
                {
                    return layers;
                }
                Regex layerName = new Regex(@"^StringTables[^\\/]*_pri_(\d+)\.zip$", RegexOptions.IgnoreCase);
                foreach (string item in Directory.GetFileSystemEntries(content))
                {
                    string label = Path.GetFileName(item);
                    try
                    {
                        if (Directory.Exists(item))
                        {
                            string zips = Path.Combine(Path.Combine(item, "Media"), "DLCZips");
                            if (Directory.Exists(zips))
                            {
                                foreach (string file in Directory.GetFiles(zips, "StringTables*.zip"))
                                {
                                    Match m = layerName.Match(Path.GetFileName(file));
                                    if (m.Success)
                                    {
                                        layers.Add(Open(label, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), file));
                                    }
                                }
                            }
                        }
                        else if (StfsReader.IsPackage(item))
                        {
                            StfsReader package = StfsReader.Open(item);
                            if (GameInfo.FromTitleId(package.TitleId) != ForzaGame.Motorsport4)
                            {
                                continue;
                            }
                            foreach (string file in package.Files())
                            {
                                string folder = Path.GetDirectoryName(file);
                                Match m = layerName.Match(Path.GetFileName(file));
                                if (m.Success && string.Equals(folder, @"Media\DLCZips", StringComparison.OrdinalIgnoreCase))
                                {
                                    Directory.CreateDirectory(temp);
                                    string path = Path.Combine(temp, layers.Count.ToString(CultureInfo.InvariantCulture) + ".zip");
                                    File.WriteAllBytes(path, package.ReadFile(file));
                                    layers.Add(Open(package.DisplayName + " (" + label + ")", int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), path));
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        report.AppendLine("  string layer skipped: " + label + ": " + e.Message);
                    }
                }
                layers.Sort(delegate(StringLayer a, StringLayer b) { return a.Priority.CompareTo(b.Priority); });
                foreach (StringLayer layer in layers)
                {
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  layer {0}: {1}", layer.Priority, layer.Label));
                }
                return layers;
            }

            private static StringLayer Open(string label, int priority, string path)
            {
                StringLayer layer = new StringLayer();
                layer.Label = label;
                layer.Priority = priority;
                layer.zip = new ForzaZip(path);
                layer.entries = new Dictionary<string, ForzaZip.Entry>(StringComparer.OrdinalIgnoreCase);
                foreach (ForzaZip.Entry entry in layer.zip.Entries)
                {
                    layer.entries[entry.Name.Replace('\\', '/')] = entry;
                }
                return layer;
            }

            // The layers' strings of a table into 'result', each key set to the highest layer's text.
            // file: "Data_Car.str".
            public static void Merge(StrFile result, List<StringLayer> layers, string language, string file)
            {
                foreach (StringLayer layer in layers)
                {
                    ForzaZip.Entry entry;
                    if (!layer.entries.TryGetValue(language + "/" + file, out entry))
                    {
                        continue;
                    }
                    foreach (StrFile.Entry text in new StrFile(layer.zip.Read(entry)).Entries)
                    {
                        result.Set(text.Key, text.Text, text.Name);
                    }
                }
            }
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

        private static int FreeKey(List<StrFile> files, int start)
        {
            for (int key = start; key > 0; key--)
            {
                bool used = false;
                foreach (StrFile file in files)
                {
                    used = used || file.Contains(key);
                }
                if (!used)
                {
                    return key;
                }
            }
            throw new InvalidOperationException("ContentOffers.str has no free string key.");
        }

        // The keys the port kept from the source game (engine 636, wheel 780, colour rows next to the target's...) or took
        // next to the target database's are not safe in a pack: they must be above FM4's largest of their column and free in
        // the game and in every package installed in Xenia's content folder. Same move as Validate DLC for Installation
        // (DlcRelocate): engines and drivetrains with their children, then every other single key, references following.
        private static void RelocateKeys(SqliteDatabase patch, string content, string targetRoot, string package, StringBuilder report)
        {
            using (DlcWorld world = DlcWorld.Load(content, targetRoot, package))
            {
                DlcRelocate relocate = new DlcRelocate(patch, world.Others, ForzaGame.Motorsport4);
                relocate.MoveRoots();
                relocate.MoveKeys();
                foreach (string line in relocate.Log)
                {
                    report.AppendLine("  " + line);
                }
                foreach (string line in relocate.Unresolved)
                {
                    report.AppendLine("  NOT MOVED: " + line);
                }
            }
        }

        // The trial's files, loose where the game mounts them (a zip X.zip mounts at the folder X).
        private static void CopyPortedFiles(string targetRoot, string trialRoot, string loose, Dictionary<string, bool> media, int offer, StringBuilder report)
        {
            string trialMedia = Path.Combine(trialRoot, "Media");
            string[] all = Directory.GetFiles(trialMedia, "*", SearchOption.AllDirectories);
            Array.Sort(all, StringComparer.OrdinalIgnoreCase);
            List<string> rules = new List<string>();        // the badge and title rules of the trial's makes
            foreach (string file in all)
            {
                string relative = file.Substring(trialMedia.Length + 1);
                string lower = relative.ToLowerInvariant();
                if (lower.StartsWith("db\\") || lower.StartsWith("stringtables\\") || lower == "zipmanifest.xml" || lower.EndsWith(".tmp"))
                {
                    continue;
                }
                if (lower.StartsWith("gametunablesettings\\badgesandtitles\\") && !lower.EndsWith("\\" + MakeTitles.RulesFile.ToLowerInvariant()))
                {
                    // A pack adds its own rules file to the folder (official offer 31: 30.xml); the game's stays as it is.
                    rules.AddRange(MakeTitles.Blocks(File.ReadAllText(file)));
                    continue;
                }
                string targetFile = Path.Combine(Path.Combine(targetRoot, "Media"), relative);
                if (!lower.EndsWith(".zip"))
                {
                    if (File.Exists(targetFile) && SameBytes(file, targetFile))
                    {
                        continue;
                    }
                    string copy = Path.Combine(loose, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(copy));
                    File.Copy(file, copy);
                    report.AppendLine("  " + relative);
                    continue;
                }
                string folder = Path.Combine(loose, relative.Substring(0, relative.Length - 4));
                string kind = Path.GetFileName(Path.GetDirectoryName(file)).ToLowerInvariant();
                bool whole = (kind == "cars" || kind == "wheels") && media.ContainsKey(Path.GetFileNameWithoutExtension(file));
                ForzaZip trial = new ForzaZip(file);
                ForzaZip target = (!whole && File.Exists(targetFile)) ? new ForzaZip(targetFile) : null;
                int count = 0;
                foreach (ForzaZip.Entry entry in trial.Entries)
                {
                    if (entry.IsDirectory)
                    {
                        continue;
                    }
                    if (target != null)
                    {
                        ForzaZip.Entry before = target.Find(entry.Name);
                        if (before != null && before.Crc == entry.Crc && before.UncompressedSize == entry.UncompressedSize)
                        {
                            continue;
                        }
                    }
                    string path = Path.Combine(folder, entry.Name.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, trial.Read(entry));
                    count++;
                }
                if (count > 0)
                {
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0}: {1} files{2}", relative, count, whole ? " (whole)" : string.Empty));
                }
            }
            if (rules.Count > 0)
            {
                string folder = MakeTitles.RulesFolder(Path.GetDirectoryName(loose));
                Directory.CreateDirectory(folder);
                string name = offer.ToString(CultureInfo.InvariantCulture) + ".xml";
                File.WriteAllText(Path.Combine(folder, name), MakeTitles.Document(rules), new UTF8Encoding(false));
                report.AppendLine("  GameTunableSettings\\BadgesAndTitles\\" + name + ": " + rules.Count + " badge and title rules");
            }
        }

        private static bool SameBytes(string a, string b)
        {
            FileInfo x = new FileInfo(a);
            FileInfo y = new FileInfo(b);
            if (x.Length != y.Length)
            {
                return false;
            }
            byte[] first = File.ReadAllBytes(a);
            byte[] second = File.ReadAllBytes(b);
            for (int i = 0; i < first.Length; i++)
            {
                if (first[i] != second[i])
                {
                    return false;
                }
            }
            return true;
        }

        // The new rows of a ported database, as a merge database (db\patch\<offer>000_merge.slt).
        // The rows are the ones in "ported" that "clean" does not have: the port runs on a copy of the
        // clean database and this diff is everything it wrote. The tables keep the game's columns, without
        // primary keys, as in the official merges (offer 31: "CREATE TABLE [T] ([Id] INTEGER,...)", page
        // size 1024). The offer rows are added: ContentOffers (the pack) and ContentOffersMapping (one row
        // per new car, ContentType 1). A merge can only add rows: rows the port changed or removed in the
        // clean database are reported.
        public static byte[] BuildMerge(byte[] clean, byte[] ported, int offer, StringBuilder report)
        {
            using (SqliteDatabase before = SqliteDatabase.FromBytes(clean))
            using (SqliteDatabase after = SqliteDatabase.FromBytes(ported))
            using (SqliteDatabase merge = SqliteDatabase.FromBytes(new byte[0]))
            {
                merge.Execute("PRAGMA page_size = 1024");
                int tables = 0;
                int rows = 0;
                int replaced = 0;
                int removed = 0;
                List<long> cars = new List<long>();
                foreach (object[] table in after.Query("SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name").Rows)
                {
                    string name = (string) table[0];
                    string quoted = SqliteDatabase.Quote(name);
                    Dictionary<string, int> old = new Dictionary<string, int>(StringComparer.Ordinal);
                    Dictionary<string, bool> oldFirst = new Dictionary<string, bool>(StringComparer.Ordinal);
                    bool known = Convert.ToInt64(before.Scalar("SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = ?", name)) > 0;
                    if (known)
                    {
                        foreach (object[] row in before.Query("SELECT * FROM " + quoted).Rows)
                        {
                            string key = RowKey(row);
                            int count;
                            old.TryGetValue(key, out count);
                            old[key] = count + 1;
                            oldFirst[Value(row[0])] = true;
                        }
                    }
                    List<object[]> added = new List<object[]>();
                    foreach (object[] row in after.Query("SELECT * FROM " + quoted).Rows)
                    {
                        string key = RowKey(row);
                        int count;
                        if (old.TryGetValue(key, out count) && count > 0)
                        {
                            old[key] = count - 1;
                            continue;
                        }
                        added.Add(row);
                        if (oldFirst.ContainsKey(Value(row[0])))
                        {
                            replaced++;
                            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  WARNING {0}: a row with the first column {1} is already in the game (changed by the port?)", name, Value(row[0])));
                        }
                        if (name == "Data_Car")
                        {
                            cars.Add(Convert.ToInt64(row[0]));
                        }
                    }
                    foreach (KeyValuePair<string, int> left in old)
                    {
                        if (left.Value > 0)
                        {
                            removed += left.Value;
                            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  WARNING {0}: the port removed a game row, a merge cannot remove it: {1}", name, left.Key));
                        }
                    }
                    if (added.Count == 0)
                    {
                        continue;
                    }
                    List<string> columns = CreateLike(known ? before : after, merge, name);
                    Insert(merge, name, columns, added);
                    tables++;
                    rows += added.Count;
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0}: {1}", name, added.Count));
                }
                if (cars.Count == 0)
                {
                    throw new InvalidOperationException("The ported database has no new car.");
                }
                AddOffer(before, merge, offer, cars, report);
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "merge: {0} rows in {1} tables, {2} cars, offer {3}; {4} rows already in the game, {5} removed", rows, tables, cars.Count, offer, replaced, removed));
                return merge.ToBytes();
            }
        }

        // The rows of a merge built for the same offer (BuildMerge) added to the merge of a package that already exists
        // (Add To Package): the offer row stays the package's, the new cars' mapping rows continue its numbering
        // (offer * 1000 + n), every other row is appended unless the package has the same row already. A row whose first
        // column is in the package with other values is added anyway and reported, as BuildMerge does for the game.
        public static byte[] AppendMerge(byte[] old, byte[] added, int offer, StringBuilder report)
        {
            using (SqliteDatabase target = SqliteDatabase.FromBytes(old))
            using (SqliteDatabase extra = SqliteDatabase.FromBytes(added))
            {
                if (Convert.ToInt64(target.Scalar("SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'ContentOffers'")) == 0
                    || Convert.ToInt64(target.Scalar("SELECT count(*) FROM ContentOffers WHERE PubOfferingID = ?", (long) offer)) == 0)
                {
                    throw new InvalidOperationException("The package's merge database has no ContentOffers row for offer " + offer + ".");
                }
                int rows = 0;
                int same = 0;
                foreach (object[] table in extra.Query("SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name").Rows)
                {
                    string name = (string) table[0];
                    if (name == "ContentOffers" || name.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    string quoted = SqliteDatabase.Quote(name);
                    bool exists = Convert.ToInt64(target.Scalar("SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = ?", name)) > 0;
                    Dictionary<string, bool> have = new Dictionary<string, bool>(StringComparer.Ordinal);
                    Dictionary<string, bool> haveFirst = new Dictionary<string, bool>(StringComparer.Ordinal);
                    if (exists)
                    {
                        foreach (object[] row in target.Query("SELECT * FROM " + quoted).Rows)
                        {
                            have[RowKey(row)] = true;
                            haveFirst[Value(row[0])] = true;
                        }
                    }
                    List<string> columns = CreateLike(extra, target, name);
                    int idColumn = columns.FindIndex(delegate(string c) { return string.Equals(c, "Id", StringComparison.OrdinalIgnoreCase); });
                    long nextMapping = 0;
                    if (name == "ContentOffersMapping" && idColumn >= 0)
                    {
                        nextMapping = offer * 1000L;
                        foreach (object[] row in target.Query("SELECT Id FROM ContentOffersMapping").Rows)
                        {
                            if (row[0] is long && (long) row[0] >= nextMapping && (long) row[0] < (offer + 1) * 1000L)
                            {
                                nextMapping = (long) row[0] + 1;
                            }
                        }
                    }
                    List<object[]> append = new List<object[]>();
                    foreach (object[] row in extra.Query("SELECT * FROM " + quoted).Rows)
                    {
                        if (name == "ContentOffersMapping" && idColumn >= 0)
                        {
                            row[idColumn] = nextMapping++;
                        }
                        if (have.ContainsKey(RowKey(row)))
                        {
                            same++;
                            continue;
                        }
                        if (haveFirst.ContainsKey(Value(row[0])) && name != "ContentOffersMapping")
                        {
                            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  WARNING {0}: a row with the first column {1} is already in the package (added anyway)", name, Value(row[0])));
                        }
                        append.Add(row);
                    }
                    if (append.Count > 0)
                    {
                        Insert(target, name, columns, append);
                        rows += append.Count;
                        report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0}: +{1}", name, append.Count));
                    }
                }
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "merge: {0} rows added, {1} identical rows already in the package", rows, same));
                return target.ToBytes();
            }
        }

        // The offer id: the title id, then "FF", the offer number in 4 hex digits and "00" (official
        // packs: 4D530910B97C9400 for offer 31; the game's own showroom packs 4D53091000000001-4).
        public static string OfferId(int offer)
        {
            return TitleId + "FF" + offer.ToString("X4", CultureInfo.InvariantCulture) + "00";
        }

        // The pack row and one mapping row per car. IsInstalled and IsPurchased are 1 as in XTD's
        // package (the game's showroom packs have IsPurchased 1 too); the names (DisplayName,
        // Description) are ContentOffers string keys, set when the string tables are written.
        private static void AddOffer(SqliteDatabase game, SqliteDatabase merge, int offer, List<long> cars, StringBuilder report)
        {
            string id = OfferId(offer);
            long offerId = long.Parse(id, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (Convert.ToInt64(game.Scalar("SELECT count(*) FROM ContentOffers WHERE OfferId = ? OR PubOfferingID = ?", offerId, offer)) > 0)
            {
                throw new InvalidOperationException("Offer " + offer + " is already in the game database.");
            }
            Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            values["ID"] = id;
            values["OfferId"] = offerId;
            values["PubOfferingID"] = (long) offer;
            values["LicenseLevel"] = 2L;
            values["LicenseMask"] = 0L;
            values["IsDLC"] = 1L;
            values["ImagePath"] = string.Empty;
            values["ThumbPath"] = string.Empty;
            values["IsInstalled"] = 1L;
            values["IsPurchased"] = 1L;
            values["IsVIP"] = 0L;
            values["Hidden"] = 0L;
            values["IsALaCarte"] = 0L;
            values["SeasonPassOffer"] = 0L;
            List<string> columns = CreateLike(game, merge, "ContentOffers");
            Insert(merge, "ContentOffers", columns, new List<object[]> { RowOf(columns, values) });

            // Mapping ids: offer * 1000 + n (the game's run 1023-1412, offer 31's 1120-1726).
            columns = CreateLike(game, merge, "ContentOffersMapping");
            List<object[]> mapping = new List<object[]>();
            for (int i = 0; i < cars.Count; i++)
            {
                long mappingId = offer * 1000L + i;
                if (Convert.ToInt64(game.Scalar("SELECT count(*) FROM ContentOffersMapping WHERE Id = ?", mappingId)) > 0)
                {
                    throw new InvalidOperationException("ContentOffersMapping id " + mappingId + " is already in the game database.");
                }
                values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                values["Id"] = mappingId;
                values["OfferId"] = offerId;
                values["ContentId"] = cars[i];
                values["ContentType"] = 1L;
                mapping.Add(RowOf(columns, values));
            }
            Insert(merge, "ContentOffersMapping", columns, mapping);
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  offer {0} ({1}): ContentOffers 1, ContentOffersMapping {2}", offer, id, cars.Count));
        }

        // The table in the merge with the game's columns and types, no keys (the official merges' form).
        private static List<string> CreateLike(SqliteDatabase game, SqliteDatabase merge, string table)
        {
            List<string> columns = new List<string>();
            List<string> definitions = new List<string>();
            foreach (object[] column in game.Query("PRAGMA table_info(" + SqliteDatabase.Quote(table) + ")").Rows)
            {
                columns.Add((string) column[1]);
                string type = column[2] as string;
                definitions.Add(SqliteDatabase.Quote((string) column[1]) + (string.IsNullOrEmpty(type) ? string.Empty : " " + type));
            }
            if (Convert.ToInt64(merge.Scalar("SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = ?", table)) == 0)
            {
                merge.Execute("CREATE TABLE " + SqliteDatabase.Quote(table) + " (" + string.Join(",", definitions.ToArray()) + ")");
            }
            return columns;
        }

        private static void Insert(SqliteDatabase merge, string table, List<string> columns, List<object[]> rows)
        {
            List<string> quoted = columns.ConvertAll<string>(delegate(string c) { return SqliteDatabase.Quote(c); });
            string marks = string.Join(",", columns.ConvertAll<string>(delegate(string c) { return "?"; }).ToArray());
            string sql = "INSERT INTO " + SqliteDatabase.Quote(table) + " (" + string.Join(",", quoted.ToArray()) + ") VALUES (" + marks + ")";
            merge.Execute("BEGIN");
            foreach (object[] row in rows)
            {
                merge.Execute(sql, row);
            }
            merge.Execute("COMMIT");
        }

        private static object[] RowOf(List<string> columns, Dictionary<string, object> values)
        {
            object[] row = new object[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                object value;
                row[i] = values.TryGetValue(columns[i], out value) ? value : null;
            }
            return row;
        }

        // A row as text, with the storage class of every value: 1 (integer) and 1.0 (real) differ.
        private static string RowKey(object[] row)
        {
            StringBuilder key = new StringBuilder();
            foreach (object value in row)
            {
                key.Append(Value(value)).Append('\u0001');
            }
            return key.ToString();
        }

        private static string Value(object value)
        {
            if (value == null)
            {
                return "null";
            }
            if (value is long)
            {
                return "i" + ((long) value).ToString(CultureInfo.InvariantCulture);
            }
            if (value is double)
            {
                return "r" + ((double) value).ToString("R", CultureInfo.InvariantCulture);
            }
            return "t" + (string) value;
        }
    }
}
