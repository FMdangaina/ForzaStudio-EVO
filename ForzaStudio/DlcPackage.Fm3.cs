namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    // A ported car as a Forza Motorsport 3 DLC pack for Xenia (a folder under content\0000000000000000\4D53084D\00000002).
    // Read from the official packs (Hyundai Car Pack, offer 22; VIP Car Pack, offer 16) on 2026-11-02; same family as FM4's:
    //   Media\DLCZips\zipmount.xml, stringtables.xml, StringTables_pri_<offer + 150>.zip (the WHOLE table, as FM4)
    //   Media\DLCZips\<offer, hex 4 digits>_pri_65\Media\...   the loose files (Xbox 360: the same folder as a zip), among them
    //       db\patch\<offer>005_merge.slt     the new rows; the pack itself is one MultiDiscContentPacks row (not ContentOffers)
    //   Media\LicenseMasks\00000001.sql       UPDATE Data_Car SET IsPurchased=1 for the pack's cars (the merge rows come IsInstalled=1,
    //                                          IsPurchased=0; LicenseMask 1 of the pack row is what runs this file)
    // The package's id (folder name, MultiDiscContentPacks.ID) = 4D53084D0CCF + offer as 4 hex digits.
    public static partial class DlcPackage
    {
        public const string TitleIdFm3 = "4D53084D";

        public static string OfferIdFm3(int offer)
        {
            return TitleIdFm3 + "0CCF" + offer.ToString("X4", CultureInfo.InvariantCulture);
        }

        public static string PackagePathFm3(string content, int offer)
        {
            return Path.Combine(content, OfferIdFm3(offer));
        }

        // The first offer number above the game's and the installed packs' (at least 100; the game's last is 99).
        public static int FreeOfferFm3(string targetRoot, string content)
        {
            int offer = 99;
            using (SqliteDatabase db = SqliteDatabase.FromBytes(File.ReadAllBytes(GameDb(targetRoot))))
            {
                object max = db.Scalar("SELECT max(intID) FROM MultiDiscContentPacks");
                if (max != null && max != DBNull.Value)
                {
                    offer = Math.Max(offer, Convert.ToInt32(max, CultureInfo.InvariantCulture));
                }
            }
            if (!string.IsNullOrEmpty(content) && Directory.Exists(content))
            {
                foreach (string item in Directory.GetFileSystemEntries(content))
                {
                    string name = Path.GetFileName(item);
                    int value;
                    if (name.Length >= 16 && name.StartsWith(TitleIdFm3 + "0CCF", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(name.Substring(12, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                    {
                        offer = Math.Max(offer, value);
                    }
                    else if (File.Exists(item) && StfsReader.IsPackage(item))
                    {
                        try
                        {
                            DlcSource installed = DlcSource.OpenPackage(item, true);
                            if (installed.Problem == null && installed.Game == ForzaGame.Motorsport3)
                            {
                                offer = Math.Max(offer, installed.Offer);
                            }
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
            }
            return offer + 1;
        }

        // The pack for Xenia: "target" is the FM3 the port ran against (read only), "trial" the port's output, "content" the
        // FM3 marketplace folder (the package goes in as <content>\<id>, with its Xenia header). Returns the folder.
        public static string BuildFolderFm3(string targetRoot, string trialRoot, string content, int offer, string name, StringBuilder report)
        {
            return BuildFolderFm3(targetRoot, trialRoot, content, content, offer, name, report);
        }

        // installed: the FM3 content folder whose packages' string tables the new tables are built on (Xenia's; "content" is a temporary folder for a console package).
        public static string BuildFolderFm3(string targetRoot, string trialRoot, string content, string installed, int offer, string name, StringBuilder report)
        {
            string layerTemp = Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "StringLayers_" + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture));
            try
            {
                List<StringLayer> layers = StringLayer.Installed(installed, layerTemp, report, ForzaGame.Motorsport3);
                return BuildFolderFm3With(targetRoot, trialRoot, content, installed, layers, offer, name, report);
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

        private static string BuildFolderFm3With(string targetRoot, string trialRoot, string content, string installed, List<StringLayer> layers, int offer, string name, StringBuilder report)
        {
            string id = OfferIdFm3(offer);
            string package = PackagePathFm3(content, offer);
            if (Directory.Exists(package))
            {
                throw new IOException("The package folder already exists: " + package);
            }
            string zips = Path.Combine(Path.Combine(package, "Media"), "DLCZips");
            string loose = Path.Combine(Path.Combine(zips, offer.ToString("X4", CultureInfo.InvariantCulture) + "_pri_65"), "Media");
            Directory.CreateDirectory(loose);

            report.AppendLine("database:");
            byte[] merge = BuildMerge(File.ReadAllBytes(GameDb(targetRoot)), File.ReadAllBytes(GameDb(trialRoot)), offer, report, true);
            merge = ClearOfInstalledFm3(targetRoot, installed, package, merge, offer, report);

            // Strings: every table the port changed, whole (the game's + the installed packs' + the port's), and the pack's name.
            report.AppendLine("strings:");
            int priority = offer + 150;
            foreach (StringLayer layer in layers)
            {
                priority = Math.Max(priority, layer.Priority + 1);
            }
            string strings = "StringTables_pri_" + priority.ToString(CultureInfo.InvariantCulture) + ".zip";
            int displayKey;
            int descriptionKey;
            int longKey;
            List<string> tables = BuildStringsFm3(targetRoot, trialRoot, Path.Combine(zips, strings), id, name, layers, out displayKey, out descriptionKey, out longKey, report);

            long hi;
            using (SqliteDatabase game = SqliteDatabase.FromBytes(File.ReadAllBytes(GameDb(targetRoot))))
            {
                hi = StringTable(game.Scalar("SELECT DisplayName FROM Data_Car WHERE DisplayName LIKE '\\_&%' ESCAPE '\\' LIMIT 1")) >> 16;
            }
            List<long> cars = new List<long>();
            using (SqliteDatabase patch = SqliteDatabase.FromBytes(merge))
            {
                patch.Execute("UPDATE MultiDiscContentPacks SET DashDisplayName = ?, Description = ?, LongDescription = ?",
                    "_&" + ((hi << 16) | (long) displayKey).ToString(CultureInfo.InvariantCulture),
                    "_&" + ((hi << 16) | (long) descriptionKey).ToString(CultureInfo.InvariantCulture),
                    "_&" + ((hi << 16) | (long) longKey).ToString(CultureInfo.InvariantCulture));
                // As the official packs: the cars come installed and not bought; the license-mask file buys them.
                patch.Execute("UPDATE Data_Car SET IsInstalled = 1, IsPurchased = 0");
                foreach (object[] row in patch.Query("SELECT Id FROM Data_Car ORDER BY Id").Rows)
                {
                    cars.Add(Convert.ToInt64(row[0], CultureInfo.InvariantCulture));
                }
                merge = patch.ToBytes();
            }
            string patchFolder = Path.Combine(Path.Combine(loose, "db"), "patch");
            Directory.CreateDirectory(patchFolder);
            File.WriteAllBytes(Path.Combine(patchFolder, offer.ToString(CultureInfo.InvariantCulture) + "005_merge.slt"), merge);

            report.AppendLine("files:");
            CopyPortedFilesFm3(targetRoot, trialRoot, loose, report);

            string masks = Path.Combine(Path.Combine(package, "Media"), "LicenseMasks");
            Directory.CreateDirectory(masks);
            File.WriteAllText(Path.Combine(masks, "00000001.sql"), "UPDATE Data_Car SET IsPurchased=1 WHERE ID IN (" + string.Join(",", cars.ConvertAll<string>(delegate(long car) { return car.ToString(CultureInfo.InvariantCulture); }).ToArray()) + ");", Encoding.ASCII);

            WriteXml(Path.Combine(zips, "zipmount.xml"), "zipmount",
                new string[] { "   <zip Name=\"" + strings + "\" Mount=\"game:\\Media\\StringTables\\\"  /> " });
            List<string> lines = tables.ConvertAll<string>(delegate(string table) { return "   <table Name=\"" + table + "\" /> "; });
            WriteXml(Path.Combine(zips, "stringtables.xml"), "stringtables", lines.ToArray());

            string header = HeaderPathFm3(content, offer);
            Directory.CreateDirectory(Path.GetDirectoryName(header));
            File.WriteAllBytes(header, XeniaHeader(id, name, TitleIdFm3, "Forza Motorsport 3"));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "package {0}: offer {1} \"{2}\", {3} car(s), strings priority {4}", package, offer, name, cars.Count, priority));
            return package;
        }

        // The pack's keys (engines, drivetrains, upgrade rows...) must not be the ones an installed pack has (the port chose them against the game alone): DlcRelocate
        // moves them in the merge, no file follows. A car Id or MediaName or the offer that an installed pack has stops the build: the files are named after them.
        private static byte[] ClearOfInstalledFm3(string targetRoot, string installed, string package, byte[] merge, int offer, StringBuilder report)
        {
            if (string.IsNullOrEmpty(installed) || !Directory.Exists(installed))
            {
                return merge;
            }
            using (DlcWorld world = DlcWorld.Load(installed, targetRoot, package))
            using (SqliteDatabase patch = SqliteDatabase.FromBytes(merge))
            {
                if (world.Offers.Contains(offer))
                {
                    throw new InvalidOperationException("Offer " + offer + " is used by an installed pack: choose another offer number.");
                }
                foreach (object[] row in patch.Query("SELECT Id, MediaName FROM Data_Car").Rows)
                {
                    string car = Convert.ToString(row[1], CultureInfo.InvariantCulture);
                    if (world.MediaNames.Contains(car))
                    {
                        throw new InvalidOperationException("The car " + car + " is in an installed pack already.");
                    }
                    foreach (DlcRelocate.Other other in world.Others)
                    {
                        if (DbIds.HasTable(other.Db, "Data_Car") && DbIds.Taken(other.Db, Convert.ToInt64(row[0], CultureInfo.InvariantCulture)))
                        {
                            throw new InvalidOperationException("The car Id " + row[0] + " of " + car + " is used by " + other.Name + ": port it again (the port takes the next free Id).");
                        }
                    }
                }
                DlcRelocate relocate = new DlcRelocate(patch, world.Others, ForzaGame.Motorsport3);
                relocate.UsedMediaNames = world.MediaNames;
                relocate.UsedOffers = world.Offers;
                relocate.FileTaken = world.FileTaken;
                relocate.MoveRoots();
                relocate.MoveKeys();
                foreach (string line in relocate.Log)
                {
                    report.AppendLine("  " + line);
                }
                return patch.ToBytes();
            }
        }

        public static string HeaderPathFm3(string content, int offer)
        {
            string full = Path.GetFullPath(content).TrimEnd('\\');
            return Path.Combine(Path.Combine(Path.Combine(Path.GetDirectoryName(full), "Headers"), Path.GetFileName(full)), OfferIdFm3(offer) + ".header");
        }

        public static void RemoveFm3(string content, int offer)
        {
            string package = PackagePathFm3(content, offer);
            if (Directory.Exists(package))
            {
                Directory.Delete(package, true);
            }
            string header = HeaderPathFm3(content, offer);
            if (File.Exists(header))
            {
                File.Delete(header);
            }
        }

        // The pack row: ID, intID and PubOfferingID = the offer, LicenseLevel 1, LicenseMask 1 (what the official one-car-group packs have), IsDLC.
        private static void AddFm3Offer(SqliteDatabase game, SqliteDatabase merge, int offer, StringBuilder report)
        {
            string id = OfferIdFm3(offer);
            if (Convert.ToInt64(game.Scalar("SELECT count(*) FROM MultiDiscContentPacks WHERE intID = ? OR ID = ?", (long) offer, id)) > 0)
            {
                throw new InvalidOperationException("Offer " + offer + " is already in the game database.");
            }
            Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            values["ID"] = id;
            values["DashDisplayName"] = string.Empty;
            values["Description"] = string.Empty;
            values["LongDescription"] = string.Empty;
            values["intID"] = (long) offer;
            values["PubOfferingID"] = (long) offer;
            values["LicenseLevel"] = 1L;
            values["LicenseMask"] = 1L;
            values["IsDLC"] = 1L;
            List<string> columns = CreateLike(game, merge, "MultiDiscContentPacks");
            Insert(merge, "MultiDiscContentPacks", columns, new List<object[]> { RowOf(columns, values) });
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  offer {0} ({1}): MultiDiscContentPacks 1", offer, id));
        }

        // Every table the port changed in a language zip of the trial, as the game's table plus the installed packs' strings plus the
        // port's; the pack's name goes into Data_Car.str under three free keys in every language.
        private static List<string> BuildStringsFm3(string targetRoot, string trialRoot, string output, string id, string name, List<StringLayer> layers, out int displayKey, out int descriptionKey, out int longKey, StringBuilder report)
        {
            SortedDictionary<string, StrFile> files = new SortedDictionary<string, StrFile>(StringComparer.Ordinal);
            Dictionary<string, bool> tables = new Dictionary<string, bool>(StringComparer.Ordinal);
            ForzaZip model = null;
            foreach (string trialZip in Directory.GetFiles(Path.Combine(Path.Combine(trialRoot, "Media"), "StringTables"), "*.zip"))
            {
                string language = Path.GetFileNameWithoutExtension(trialZip);
                string targetZip = Path.Combine(Path.Combine(Path.Combine(targetRoot, "Media"), "StringTables"), Path.GetFileName(trialZip));
                if (!File.Exists(targetZip))
                {
                    continue;
                }
                ForzaZip trial = new ForzaZip(trialZip);
                ForzaZip target = new ForzaZip(targetZip);
                if (model == null)
                {
                    model = target;
                }
                foreach (ForzaZip.Entry entry in trial.Entries)
                {
                    ForzaZip.Entry before = target.Find(entry.Name);
                    if (!entry.Name.EndsWith(".str", StringComparison.OrdinalIgnoreCase) || before == null || (before.Crc == entry.Crc && before.UncompressedSize == entry.UncompressedSize))
                    {
                        continue;
                    }
                    StrFile ported = new StrFile(trial.Read(entry));
                    StrFile old = new StrFile(target.Read(before));
                    StrFile result = new StrFile(target.Read(before));
                    StringLayer.Merge(result, layers, language, entry.Name);
                    int added = 0;
                    foreach (StrFile.Entry text in ported.Entries)
                    {
                        StrFile.Entry was = Find(old, text.Key);
                        if (was != null && was.Text == text.Text && was.Name == text.Name)
                        {
                            continue;
                        }
                        StrFile.Entry present = Find(result, text.Key);
                        if (present != null && !SameString(present, text))
                        {
                            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "{0}: key {1:X4} is \"{2}\" in an installed pack, \"{3}\" of this pack cannot take it (the {4} table).",
                                Path.GetFileNameWithoutExtension(entry.Name), text.Key, present.Text, text.Text, language));
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
            }
            if (model == null)
            {
                throw new InvalidOperationException("No string tables to build on (" + targetRoot + ").");
            }
            List<StrFile> carFiles = new List<StrFile>();
            foreach (KeyValuePair<string, StrFile> pair in files)
            {
                if (pair.Key.EndsWith("/Data_Car", StringComparison.Ordinal))
                {
                    carFiles.Add(pair.Value);
                }
            }
            if (carFiles.Count == 0)
            {
                throw new InvalidOperationException("The port changed no Data_Car.str: nothing to build the pack's name on.");
            }
            displayKey = FreeKey(carFiles, 0xFFFE);
            descriptionKey = FreeKey(carFiles, displayKey - 1);
            longKey = FreeKey(carFiles, descriptionKey - 1);
            foreach (StrFile file in carFiles)
            {
                file.Set(displayKey, name, "IDS_DashDisplayName_" + id);
                file.Set(descriptionKey, name, "IDS_Description_" + id);
                file.Set(longKey, name, "IDS_LongDescription_" + id);
            }
            model.Entries.RemoveAll(delegate(ForzaZip.Entry entry) { return entry.Method != ForzaZip.MethodLzx; });
            ForzaZip archive = ForzaZip.CreateLike(model);
            foreach (KeyValuePair<string, StrFile> pair in files)
            {
                archive.Add(pair.Key + ".str", pair.Value.ToBytes());
            }
            archive.Save(output);
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0}: {1} tables in {2} languages; the pack's name as keys {3:X4}, {4:X4}, {5:X4}", Path.GetFileName(output), tables.Count, files.Count / Math.Max(tables.Count, 1), displayKey, descriptionKey, longKey));
            List<string> list = new List<string>(tables.Keys);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        // The files the port added or changed, loose under the pack's Media: a zip's new entries land in the folder the zip is mounted at
        // (Cars\X.zip -> cars\X\, UI\Textures.zip -> UI\Textures\, the audio tuning zips), other new files as they are. The database, the
        // string tables and the zip manifest are the pack's own files.
        private static void CopyPortedFilesFm3(string targetRoot, string trialRoot, string loose, StringBuilder report)
        {
            string trialMedia = Path.Combine(trialRoot, "Media");
            string targetMedia = Path.Combine(targetRoot, "Media");
            int files = 0;
            foreach (string file in Directory.GetFiles(trialMedia, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(trialMedia.Length).TrimStart('\\');
                string first = relative.Split('\\')[0];
                if (string.Equals(first, "db", StringComparison.OrdinalIgnoreCase) || string.Equals(first, "StringTables", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(relative, "zipmanifest.xml", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string targetFile = Path.Combine(targetMedia, relative);
                if (relative.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    ForzaZip trial = new ForzaZip(file);
                    ForzaZip target = File.Exists(targetFile) ? new ForzaZip(targetFile) : null;
                    string mount = Path.Combine(loose, relative.Substring(0, relative.Length - 4));
                    int count = 0;
                    foreach (ForzaZip.Entry entry in trial.Entries)
                    {
                        ForzaZip.Entry before = (target != null) ? target.Find(entry.Name) : null;
                        if (entry.Name.EndsWith("/", StringComparison.Ordinal) || entry.Name.EndsWith("_build_report.html", StringComparison.OrdinalIgnoreCase)
                            || (before != null && before.Crc == entry.Crc && before.UncompressedSize == entry.UncompressedSize))
                        {
                            continue;
                        }
                        string path = Path.Combine(mount, entry.Name.Replace('/', '\\'));
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        File.WriteAllBytes(path, trial.Read(entry));
                        count++;
                    }
                    if (count > 0)
                    {
                        report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0}: {1} file(s)", relative, count));
                        files += count;
                    }
                }
                else if (!File.Exists(targetFile))
                {
                    string path = Path.Combine(loose, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.Copy(file, path, true);
                    report.AppendLine("  " + relative);
                    files++;
                }
            }
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0} files in all", files));
        }
    }
}
