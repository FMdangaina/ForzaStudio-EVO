namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;

    // Add To Package: the cars of a pack trial (Car Port window) go into a DLC package that already exists, an STFS
    // file in Xenia's content folder, instead of a new package. The package keeps its offer, its name and its cars.
    //   1. the package is extracted to a work folder (as Open DLC Package does);
    //   2. a second package is built from the trial for the SAME offer in a temporary content folder (DlcPackage.BuildFolder):
    //      the ids were chosen above the installed packs, the strings are built on the installed packs' layers, so the
    //      new package's string tables already hold the old package's strings;
    //   3. database: the new merge's rows are appended to the package's merge (DlcPackage.AppendMerge);
    //      strings: the new StringTables_pri_<P>.zip replaces the old one (old keys the new tables lack are copied in,
    //      the extra pack name the build made is dropped), stringtables.xml / zipmount.xml follow;
    //      files: the new package's Media files go into the pack zip (<offer>_pri_65.zip), same bytes are skipped;
    //   4. the work folder is packed again into the same file with its own header (StfsPackage.Rewrite), the original
    //      kept in bak\ when backups are on.
    // Nothing is touched until the whole new package is built.
    public static class DlcPackageAdd
    {
        public static string Add(string packageFile, string cleanRoot, string targetRoot, string trialRoot, string installedContent, StringBuilder report)
        {
            StfsReader package = StfsReader.Open(packageFile);
            if (GameInfo.FromTitleId(package.TitleId) != ForzaGame.Motorsport4)
            {
                throw new InvalidOperationException(Path.GetFileName(packageFile) + " is not an FM4 package.");
            }
            string work = Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "DlcAdd_" + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture));
            try
            {
                string old = Path.Combine(work, "old");
                report.AppendLine("extracting " + Path.GetFileName(packageFile) + " \"" + package.DisplayName + "\"...");
                package.Extract(old);
                int offer = Offer(old);
                string zips = Path.Combine(Path.Combine(old, "Media"), "DLCZips");
                string packPath = null;
                string stringsPath = null;
                foreach (string file in Directory.GetFiles(zips, "*_pri_*.zip"))
                {
                    if (Path.GetFileName(file).StartsWith("StringTables", StringComparison.OrdinalIgnoreCase))
                    {
                        stringsPath = file;
                    }
                    else
                    {
                        packPath = file;
                    }
                }
                if (packPath == null || stringsPath == null)
                {
                    throw new InvalidOperationException("The package has no car pack zip or no string tables zip in Media\\DLCZips (is it a Forza Studio / Aiden-Piercee package?).");
                }

                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "package offer {0}; building the new cars for it:", offer));
                string content = Path.Combine(work, "new");
                string made = DlcPackage.BuildFolder(cleanRoot, targetRoot, trialRoot, content, installedContent, offer, package.DisplayName, report);
                string madeZips = Path.Combine(Path.Combine(made, "Media"), "DLCZips");
                string madeLoose = Path.Combine(Path.Combine(madeZips, offer.ToString(CultureInfo.InvariantCulture) + "_pri_65"), "Media");
                string madeStrings = null;
                foreach (string file in Directory.GetFiles(madeZips, "StringTables_pri_*.zip"))
                {
                    madeStrings = file;
                }

                report.AppendLine("adding to the package:");
                ForzaZip pack = new ForzaZip(packPath);
                ForzaZip.Entry mergeEntry = null;
                foreach (ForzaZip.Entry entry in pack.Entries)
                {
                    string name = entry.Name.Replace('\\', '/');
                    if (name.StartsWith("Media/db/patch/", StringComparison.OrdinalIgnoreCase) && name.EndsWith("_merge.slt", StringComparison.OrdinalIgnoreCase))
                    {
                        mergeEntry = entry;
                    }
                }
                if (mergeEntry == null)
                {
                    throw new InvalidOperationException("The package has no merge database (Media\\db\\patch\\*_merge.slt).");
                }
                string newMerge = Path.Combine(Path.Combine(madeLoose, "db"), "patch");
                newMerge = Path.Combine(newMerge, offer.ToString(CultureInfo.InvariantCulture) + "000_merge.slt");
                byte[] oldMerge = pack.Read(mergeEntry);
                byte[] merged = DlcPackage.AppendMerge(oldMerge, File.ReadAllBytes(newMerge), offer, report);
                CheckCars(oldMerge, merged);
                int nameKey = OfferNameKey(oldMerge);

                // Files.
                string root = madeLoose.TrimEnd('\\') + "\\";
                string[] all = Directory.GetFiles(madeLoose, "*", SearchOption.AllDirectories);
                Array.Sort(all, StringComparer.OrdinalIgnoreCase);
                int added = 0;
                int same = 0;
                int replaced = 0;
                foreach (string file in all)
                {
                    string relative = file.Substring(root.Length).Replace('\\', '/');
                    if (relative.StartsWith("db/patch/", StringComparison.OrdinalIgnoreCase) && relative.EndsWith("_merge.slt", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (relative.EndsWith("_build_report.html", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    string entryName = "Media/" + relative;
                    byte[] data = File.ReadAllBytes(file);
                    ForzaZip.Entry existing = pack.Find(entryName);
                    if (existing == null)
                    {
                        pack.Add(entryName, data);
                        added++;
                    }
                    else if (existing.Crc == ForzaZip.Crc32(data) && existing.UncompressedSize == data.Length)
                    {
                        same++;
                    }
                    else
                    {
                        pack.Replace(existing, data);
                        replaced++;
                        report.AppendLine("  replaced " + entryName);
                    }
                }
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "files: {0} added, {1} replaced, {2} already the same", added, replaced, same));
                pack.Replace(mergeEntry, merged);

                // Strings.
                string stringsName = Path.GetFileName(madeStrings);
                ForzaZip strings = new ForzaZip(madeStrings);
                ForzaZip oldStrings = new ForzaZip(stringsPath);
                string stringsReport = MergeStrings(strings, oldStrings, offer, nameKey);
                report.AppendLine(stringsReport);

                // Nothing was written to the package yet; now the work folder changes and the file is packed again.
                string newStringsPath = Path.Combine(zips, stringsName);
                pack.Save(packPath);
                strings.Save(newStringsPath + ".new");
                if (!string.Equals(stringsPath, newStringsPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(stringsPath);
                }
                if (File.Exists(newStringsPath))
                {
                    File.Delete(newStringsPath);
                }
                File.Move(newStringsPath + ".new", newStringsPath);
                Lists(zips, Path.GetFileName(stringsPath), stringsName, Path.Combine(madeZips, "stringtables.xml"));

                string output = packageFile + ".new";
                try
                {
                    StfsPackage.Rewrite(old, output, package.Header);
                    StfsReader check = StfsReader.Open(output);
                    if (check.Files().Count == 0)
                    {
                        throw new InvalidOperationException("The rewritten package is empty.");
                    }
                    if (BackupPolicy.Enabled)
                    {
                        string folder = Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), "bak");
                        Directory.CreateDirectory(folder);
                        string backup = Path.Combine(folder, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + "_bak_" + Path.GetFileName(packageFile));
                        File.Copy(packageFile, backup, true);
                        report.AppendLine("original kept: " + backup);
                    }
                    File.Delete(packageFile);
                    File.Move(output, packageFile);
                }
                finally
                {
                    if (File.Exists(output))
                    {
                        File.Delete(output);
                    }
                }
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "package {0} written ({1:0.0} MB)", packageFile, new FileInfo(packageFile).Length / 1048576.0));
                return packageFile;
            }
            finally
            {
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

        // The offer of an extracted package: Media\<offer>.puboffer.
        private static int Offer(string folder)
        {
            foreach (string file in Directory.GetFiles(Path.Combine(folder, "Media"), "*.puboffer"))
            {
                int offer;
                if (int.TryParse(Path.GetFileNameWithoutExtension(file), NumberStyles.None, CultureInfo.InvariantCulture, out offer))
                {
                    return offer;
                }
            }
            throw new InvalidOperationException("The package has no Media\\<offer>.puboffer.");
        }

        // The cars the new merge adds must be new ones: a car (id or MediaName) already in the package is a mistake.
        private static void CheckCars(byte[] old, byte[] merged)
        {
            using (SqliteDatabase before = SqliteDatabase.FromBytes(old))
            using (SqliteDatabase after = SqliteDatabase.FromBytes(merged))
            {
                if (!DbIds.HasTable(after, "Data_Car"))
                {
                    return;
                }
                long ids = Convert.ToInt64(after.Scalar("SELECT count(DISTINCT Id) FROM Data_Car"));
                long names = Convert.ToInt64(after.Scalar("SELECT count(DISTINCT MediaName COLLATE NOCASE) FROM Data_Car"));
                long rows = Convert.ToInt64(after.Scalar("SELECT count(*) FROM Data_Car"));
                if (ids != rows || names != rows)
                {
                    throw new InvalidOperationException("A car of the new set is already in the package (same Id or MediaName). Take it out of the pack list, or delete it from the package first (File > Delete Car from DLC Package).");
                }
            }
        }

        // The ContentOffers string key of the package's name (the low 16 bits of its DisplayName "_&n").
        private static int OfferNameKey(byte[] merge)
        {
            using (SqliteDatabase db = SqliteDatabase.FromBytes(merge))
            {
                string name = db.Scalar("SELECT DisplayName FROM ContentOffers LIMIT 1") as string;
                uint id;
                if (name != null && name.StartsWith("_&", StringComparison.Ordinal) && uint.TryParse(name.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out id))
                {
                    return (int) (id & 0xFFFF);
                }
            }
            return -1;
        }

        // 'strings' (the new package's tables) gets the keys only the old package's tables have, and loses the second pack
        // name the build added to ContentOffers; a table only the old package has is copied.
        private static string MergeStrings(ForzaZip strings, ForzaZip oldStrings, int offer, int nameKey)
        {
            int copiedKeys = 0;
            int copiedTables = 0;
            int droppedNames = 0;
            string displayName = "IDS_DisplayName_" + DlcPackage.OfferId(offer);
            string description = "IDS_Description_" + DlcPackage.OfferId(offer);
            foreach (ForzaZip.Entry entry in oldStrings.Entries.ToArray())
            {
                if (entry.IsDirectory)
                {
                    continue;
                }
                ForzaZip.Entry now = strings.Find(entry.Name);
                if (now == null)
                {
                    strings.Add(entry.Name, oldStrings.Read(entry));
                    copiedTables++;
                    continue;
                }
                StrFile result = new StrFile(strings.Read(now));
                StrFile before = new StrFile(oldStrings.Read(entry));
                bool changed = false;
                foreach (StrFile.Entry text in before.Entries)
                {
                    if (!result.Contains(text.Key))
                    {
                        result.Set(text.Key, text.Text, text.Name);
                        copiedKeys++;
                        changed = true;
                    }
                }
                if (entry.Name.EndsWith("ContentOffers.str", StringComparison.OrdinalIgnoreCase) && nameKey >= 0)
                {
                    // The build made new keys for the pack name; the package keeps the keys its offer row names.
                    List<int> extra = new List<int>();
                    foreach (StrFile.Entry text in result.Entries)
                    {
                        if ((text.Name == displayName || text.Name == description) && text.Key != nameKey && !before.Contains(text.Key))
                        {
                            extra.Add(text.Key);
                        }
                    }
                    foreach (int key in extra)
                    {
                        changed = result.Remove(key) || changed;
                        droppedNames++;
                    }
                }
                if (changed)
                {
                    strings.Replace(now, result.ToBytes());
                }
            }
            return string.Format(CultureInfo.InvariantCulture, "strings: {0} old keys carried over, {1} old tables copied, {2} duplicate pack-name keys dropped", copiedKeys, copiedTables, droppedNames);
        }

        // stringtables.xml: the tables of both packages; zipmount.xml: the new strings zip in place of the old one.
        private static void Lists(string zips, string oldStrings, string newStrings, string madeTables)
        {
            string tables = Path.Combine(zips, "stringtables.xml");
            SortedDictionary<string, bool> names = new SortedDictionary<string, bool>(StringComparer.Ordinal);
            foreach (string file in new string[] { tables, madeTables })
            {
                if (!File.Exists(file))
                {
                    continue;
                }
                foreach (Match m in Regex.Matches(File.ReadAllText(file, Encoding.ASCII), "<table\\s+Name=\"([^\"]+)\"", RegexOptions.IgnoreCase))
                {
                    names[m.Groups[1].Value] = true;
                }
            }
            List<string> lines = new List<string>();
            foreach (string name in names.Keys)
            {
                lines.Add("   <table Name=\"" + name + "\" /> ");
            }
            DlcPackage.WriteXml(tables, "stringtables", lines.ToArray());
            string mount = Path.Combine(zips, "zipmount.xml");
            if (File.Exists(mount) && !string.Equals(oldStrings, newStrings, StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(mount, File.ReadAllText(mount, Encoding.ASCII).Replace(oldStrings, newStrings), Encoding.ASCII);
            }
        }
    }
}
