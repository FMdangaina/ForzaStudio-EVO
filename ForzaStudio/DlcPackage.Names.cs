namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    // Names pack (user 2026-11-14, ST-02: "a DLC that only puts the str files, automatic, for FM2, FM3 and FM4").
    // Found: every car DLC of FM2 / FM3 (and FM4) ships WHOLE string tables (FM3 Data_Car.str 924-1096 entries, the base
    // cars included) in a DLCZips zip mounted at game:\Media\StringTables\; the game reads the highest priority table,
    // not the game folder's. A car added to the game folder (Duplicate Car) had its name in the base table only, so with
    // any DLC installed it showed no name (FM3 Supra copy: name back once the DLC were moved away).
    //
    // The names pack is one small Xbox 360 package per game (STFS file in Xenia's content folder, marker ForzaStudioNames.txt):
    //   Media\DLCZips\StringTables_pri_999.zip   <LANG>/<Table>.str for every table an installed DLC replaces: the game
    //                                            folder's table + every installed layer, lowest priority first (what the
    //                                            game would show) -> the game folder's own additions survive
    //   Media\DLCZips\zipmount.xml, stringtables.xml
    //   FM3: the pack row (MultiDiscContentPacks, as the FM3 packs built by DlcPackage.Fm3, checked in game 2026-11-02) in
    //        Media\DLCZips\<offer hex>_pri_65\Media\db\patch\<offer>005_merge.slt, its name under free Data_Car keys;
    //   FM4: Media\<offer>.puboffer (as ModPack, checked in game 2026-11-10).
    // It is rebuilt (replaced) after every change of the game folder's strings (Duplicate Car, Uninstall); with no DLC
    // string table installed it is not needed and removed. Original DLC are never written.
    public static partial class DlcPackage
    {
        public const string NamesMarker = "ForzaStudioNames.txt";
        public const int NamesPriority = 999;          // at least; above every installed layer (FM2 DLCBeta has 999 itself, a new pack takes the highest + 1)

        // The names pack of the game in the content folder, or null.
        public static string FindNamesPack(string content, ForzaGame game)
        {
            if (string.IsNullOrEmpty(content) || !Directory.Exists(content))
            {
                return null;
            }
            foreach (string file in Directory.GetFiles(content))
            {
                Dictionary<string, string> marker = NamesPackMarker(file);
                if (marker != null && GameInfo.FromTitleId(StfsReader.Open(file).TitleId) == game)
                {
                    return file;
                }
            }
            return null;
        }

        public static Dictionary<string, string> NamesPackMarker(string file)
        {
            try
            {
                if (!StfsReader.IsPackage(file))
                {
                    return null;
                }
                StfsReader package = StfsReader.Open(file);
                string name = package.Files().Find(delegate(string f) { return f.Equals(NamesMarker, StringComparison.OrdinalIgnoreCase); });
                if (name == null)
                {
                    return null;
                }
                Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in Encoding.UTF8.GetString(package.ReadFile(name)).Split('\n'))
                {
                    int equals = line.IndexOf('=');
                    if (equals > 0)
                    {
                        values[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
                    }
                }
                return values;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Rebuilds the game's names pack from the game folder's string tables and the installed DLC's. Returns the package
        // file, or null when no installed DLC replaces a string table (an old names pack is then removed). The old pack is
        // put back when the build fails.
        public static string RebuildNamesPack(ForzaGame game, string gameRoot, string content, StringBuilder report)
        {
            if (string.IsNullOrEmpty(content) || !Directory.Exists(content))
            {
                report.AppendLine("names pack: skipped, Xenia's content folder of " + GameInfo.Tag(game) + " is not known (Tools > Settings > Folders)");
                return null;
            }
            string work = Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "NamesPack_" + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture));
            string old = FindNamesPack(content, game);
            Dictionary<string, string> oldMarker = (old == null) ? null : NamesPackMarker(old);
            string held = null;
            try
            {
                Directory.CreateDirectory(work);
                if (old != null)
                {
                    held = Path.Combine(work, "old.stfs");
                    File.Move(old, held);
                }
                StringBuilder layerReport = new StringBuilder();
                List<StringLayer> layers = StringLayer.Installed(content, Path.Combine(work, "layers"), layerReport, game);
                Dictionary<string, bool> tables = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                foreach (StringLayer layer in layers)
                {
                    foreach (string table in layer.Tables())
                    {
                        tables[table] = true;
                    }
                }
                if (tables.Count == 0)
                {
                    report.AppendLine("names pack: not needed, no installed DLC replaces a string table" + ((old != null) ? " (the old one was removed)" : ""));
                    held = null;   // stays deleted
                    return null;
                }

                // The tables: the game folder's + every layer, in priority order.
                string gameTables = Path.Combine(Path.Combine(gameRoot, "Media"), "StringTables");
                SortedDictionary<string, StrFile> files = new SortedDictionary<string, StrFile>(StringComparer.Ordinal);
                ForzaZip model = null;
                foreach (string zipPath in Directory.GetFiles(gameTables, "*.zip"))
                {
                    string language = Path.GetFileNameWithoutExtension(zipPath);
                    ForzaZip zip = new ForzaZip(zipPath);
                    foreach (string table in tables.Keys)
                    {
                        ForzaZip.Entry entry = zip.Find(table);
                        if (entry == null)
                        {
                            continue;
                        }
                        StrFile result = new StrFile(zip.Read(entry));
                        StringLayer.Merge(result, layers, language, table);
                        files[language + "/" + entry.Name] = result;
                        if (model == null)
                        {
                            model = zip;
                        }
                    }
                }
                if (model == null)
                {
                    throw new InvalidOperationException("The game folder has none of the string tables the DLC replace (" + gameTables + ").");
                }

                int offer = 0;
                string kept;
                if (oldMarker != null && oldMarker.TryGetValue("offer", out kept))
                {
                    int.TryParse(kept, NumberStyles.None, CultureInfo.InvariantCulture, out offer);
                }
                string package = Path.Combine(work, "package");
                string mediaFolder = Path.Combine(package, "Media");
                string zips = Path.Combine(mediaFolder, "DLCZips");
                Directory.CreateDirectory(zips);
                string title = "ForzaStudio EVO names (" + GameInfo.Tag(game) + ")";

                if (game == ForzaGame.Motorsport3)
                {
                    if (offer <= 0)
                    {
                        offer = FreeOfferFm3(gameRoot, content);
                    }
                    List<StrFile> carFiles = new List<StrFile>();
                    foreach (KeyValuePair<string, StrFile> pair in files)
                    {
                        if (pair.Key.EndsWith("/Data_Car.str", StringComparison.OrdinalIgnoreCase))
                        {
                            carFiles.Add(pair.Value);
                        }
                    }
                    if (carFiles.Count == 0)
                    {
                        throw new InvalidOperationException("No installed FM3 DLC replaces Data_Car.str: nothing to put the pack's name in.");
                    }
                    string id = OfferIdFm3(offer);
                    int displayKey = FreeKey(carFiles, 0xFFFE);
                    int descriptionKey = FreeKey(carFiles, displayKey - 1);
                    int longKey = FreeKey(carFiles, descriptionKey - 1);
                    foreach (StrFile file in carFiles)
                    {
                        file.Set(displayKey, title, "IDS_DashDisplayName_" + id);
                        file.Set(descriptionKey, title, "IDS_Description_" + id);
                        file.Set(longKey, title, "IDS_LongDescription_" + id);
                    }
                    byte[] merge;
                    using (SqliteDatabase gameDb = SqliteDatabase.FromBytes(File.ReadAllBytes(GameDb(gameRoot))))
                    using (SqliteDatabase patch = SqliteDatabase.FromBytes(new byte[0]))
                    {
                        patch.Execute("PRAGMA page_size = 1024");
                        AddFm3Offer(gameDb, patch, offer, report);
                        long hi = StringTable(gameDb.Scalar("SELECT DisplayName FROM Data_Car WHERE DisplayName LIKE '\\_&%' ESCAPE '\\' LIMIT 1")) >> 16;
                        patch.Execute("UPDATE MultiDiscContentPacks SET DashDisplayName = ?, Description = ?, LongDescription = ?",
                            "_&" + ((hi << 16) | (long) displayKey).ToString(CultureInfo.InvariantCulture),
                            "_&" + ((hi << 16) | (long) descriptionKey).ToString(CultureInfo.InvariantCulture),
                            "_&" + ((hi << 16) | (long) longKey).ToString(CultureInfo.InvariantCulture));
                        merge = patch.ToBytes();
                    }
                    string patchFolder = Path.Combine(Path.Combine(Path.Combine(Path.Combine(zips, offer.ToString("X4", CultureInfo.InvariantCulture) + "_pri_65"), "Media"), "db"), "patch");
                    Directory.CreateDirectory(patchFolder);
                    File.WriteAllBytes(Path.Combine(patchFolder, offer.ToString(CultureInfo.InvariantCulture) + "005_merge.slt"), merge);
                    string masks = Path.Combine(mediaFolder, "LicenseMasks");
                    Directory.CreateDirectory(masks);
                    File.WriteAllText(Path.Combine(masks, "00000001.sql"), "UPDATE Data_Car SET IsPurchased=1 WHERE 0;", Encoding.ASCII);   // no car: a no-op, as the pack row's licence mask 1 names this file
                }
                else if (game == ForzaGame.Motorsport4)
                {
                    if (offer <= 0)
                    {
                        offer = FreeOffer(gameRoot, content);
                        foreach (int installed in InstalledOffers(content))
                        {
                            offer = Math.Max(offer, installed + 1);
                        }
                    }
                    File.WriteAllBytes(Path.Combine(mediaFolder, offer.ToString(CultureInfo.InvariantCulture) + ".puboffer"), new byte[] { 0x20, 0x0D, 0x0A });
                }

                // Above every installed layer (FM2's DLCBeta strings are priority 999; a pack the editor builds takes the highest + 1).
                int priority = NamesPriority;
                foreach (StringLayer layer in layers)
                {
                    priority = Math.Max(priority, layer.Priority + 1);
                }

                // The string zip, as the official ones (LZX entries, <LANG>/<Table>.str).
                string strings = "StringTables_pri_" + priority.ToString(CultureInfo.InvariantCulture) + ".zip";
                ForzaZip.KeepGameMethod(model);   // LZX, deflate for FM2
                ForzaZip archive = ForzaZip.CreateLike(model);
                foreach (KeyValuePair<string, StrFile> pair in files)
                {
                    archive.Add(pair.Key, pair.Value.ToBytes());
                }
                archive.Save(Path.Combine(zips, strings));
                WriteXml(Path.Combine(zips, "zipmount.xml"), "zipmount", new string[] { "   <zip Name=\"" + strings + "\" Mount=\"game:\\Media\\StringTables\\\"  /> " });
                List<string> lines = new List<string>();
                foreach (string table in tables.Keys)
                {
                    lines.Add("   <table Name=\"" + Path.GetFileNameWithoutExtension(table) + "\" /> ");
                }
                lines.Sort(StringComparer.Ordinal);
                WriteXml(Path.Combine(zips, "stringtables.xml"), "stringtables", lines.ToArray());

                StringBuilder marker = new StringBuilder();
                marker.Append("names=1\r\n");
                marker.Append("game=" + GameInfo.Tag(game) + "\r\n");
                marker.Append("offer=" + offer.ToString(CultureInfo.InvariantCulture) + "\r\n");
                marker.Append("written=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\r\n");
                marker.Append("Made by ForzaStudio EVO: the string tables the installed DLC replace, with the names of the cars added to the game folder (priority " + priority + ": above every installed pack). Rebuilt by the editor; delete it to go back to the DLC's tables.\r\n");
                File.WriteAllText(Path.Combine(package, NamesMarker), marker.ToString());

                string temp = Path.Combine(work, "package.stfs");
                string contentId = StfsPackage.Write(package, temp, InstalledDlc.TitleId(game), 2, title,
                    "String tables of the installed DLC plus the names of the cars ForzaStudio EVO added to the game folder.", GameTitle(game), null);
                string target = Path.Combine(content, contentId);
                if (File.Exists(target))
                {
                    File.Delete(target);
                }
                File.Move(temp, target);
                held = null;
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "names pack: {0} ({1} table(s) x {2} language(s), {3} installed layer(s), priority {4}{5})",
                    Path.GetFileName(target), tables.Count, files.Count / Math.Max(tables.Count, 1), layers.Count, priority, (offer > 0) ? ", offer " + offer.ToString(CultureInfo.InvariantCulture) : ""));
                return target;
            }
            finally
            {
                if (held != null && old != null && File.Exists(held) && !File.Exists(old))
                {
                    File.Move(held, old);   // the build failed: the old pack goes back
                }
                try
                {
                    if (Directory.Exists(work))
                    {
                        Directory.Delete(work, true);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        // The keys of a table ("Data_Car.str") the installed DLC of the game hold (names pack left out): a key the game folder
        // adds must be none of them, or the names pack's merge would give it the DLC's text.
        public static HashSet<int> InstalledStringKeys(ForzaGame game, string content, string table)
        {
            HashSet<int> keys = new HashSet<int>();
            if (string.IsNullOrEmpty(content) || !Directory.Exists(content))
            {
                return keys;
            }
            string temp = Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "StringKeys_" + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture));
            try
            {
                Dictionary<int, string> found = new Dictionary<int, string>();
                foreach (StringLayer layer in StringLayer.Installed(content, temp, new StringBuilder(), game))
                {
                    layer.Collect(table, found);
                }
                foreach (int key in found.Keys)
                {
                    keys.Add(key);
                }
            }
            finally
            {
                try
                {
                    if (Directory.Exists(temp))
                    {
                        Directory.Delete(temp, true);
                    }
                }
                catch (IOException)
                {
                }
            }
            return keys;
        }

        // The rebuild for a change of the game folder; never stops the caller (its problem goes into the report).
        public static void RefreshNamesPack(ForzaGame game, string gameRoot, string content, StringBuilder report)
        {
            try
            {
                RebuildNamesPack(game, gameRoot, content ?? InstalledDlc.ContentFolder(game), report);
            }
            catch (Exception e)
            {
                report.AppendLine("names pack: NOT rebuilt: " + e.Message + " (the new names show only without DLC installed)");
            }
        }

        internal static string GameTitle(ForzaGame game)
        {
            switch (game)
            {
                case ForzaGame.Motorsport2: return "Forza Motorsport 2";
                case ForzaGame.Motorsport3: return "Forza Motorsport 3";
                default: return "Forza Motorsport 4";
            }
        }
    }
}
