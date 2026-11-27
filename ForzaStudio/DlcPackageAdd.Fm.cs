namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;

    // Add To Package for FM2 and FM3 (docs/PORT_PARITY.md: the FM2 and FM3 windows get what FM4's Add To Package does, user
    // 2026-11-27 "fai tutto in un colpo"): the cars of a pack trial go into a package that already exists, the same steps as
    // Add (FM4) on the two games' own layouts:
    //   package  a folder pack in Xenia's content folder (FM2 DLC<offer>, FM3 4D53084D0CCF<offer hex>, as this program builds
    //            them) or an STFS file (Build for Xbox 360, or another one of that game);
    //   layer    FM2 Media\DLCZips\DLC<offer>_pri_65(.zip), merge media\db\patch\<offer>0000_merge.slt; FM3 <offer hex>_pri_65(.zip),
    //            merge Media\db\patch\<offer>005_merge.slt, pack row MultiDiscContentPacks (stays the package's);
    //   strings  FM2 DLC<offer>Overrides1_pri_<P>.zip, FM3 StringTables_pri_<P>.zip: the new build's tables (whole, on the
    //            installed packs' layers) with the keys only the old ones have;
    //   masks    Media\LicenseMasks: written again for every car of the package (FM2 both files with the StockGarage insert,
    //            FM3 00000001.sql).
    // A zipped layer is unpacked, the new files added, and written again with the game's method (FM2 deflate, FM3 LZX). The
    // merge has to pass the integrity check against the game and the other installed packs before anything is written; the
    // folder or file is then replaced (the old one kept in bak\ when backups are on).
    public static partial class DlcPackageAdd
    {
        public static string AddFm(ForzaGame game, string package, string targetRoot, string trialRoot, string installedContent, string name, StringBuilder report)
        {
            if (game != ForzaGame.Motorsport2 && game != ForzaGame.Motorsport3)
            {
                throw new ArgumentException("FM2 or FM3 only.");
            }
            bool fm2 = game == ForzaGame.Motorsport2;
            bool isFile = File.Exists(package);
            if (!isFile && !Directory.Exists(package))
            {
                throw new FileNotFoundException("No package " + package + ".");
            }
            string work = Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "DlcAddFm_" + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture));
            try
            {
                string old = Path.Combine(work, "old");
                StfsReader stfs = null;
                if (isFile)
                {
                    stfs = StfsReader.Open(package);
                    if (GameInfo.FromTitleId(stfs.TitleId) != game)
                    {
                        throw new InvalidOperationException(Path.GetFileName(package) + " is not an " + GameInfo.Tag(game) + " package.");
                    }
                    report.AppendLine("extracting " + Path.GetFileName(package) + " \"" + stfs.DisplayName + "\"...");
                    stfs.Extract(old);
                    if (string.IsNullOrEmpty(name))
                    {
                        name = stfs.DisplayName;
                    }
                }
                else
                {
                    CopyTree(package, old);
                }
                string zips = Path.Combine(Path.Combine(old, "Media"), "DLCZips");
                if (!Directory.Exists(zips))
                {
                    throw new InvalidOperationException("The package has no Media\\DLCZips.");
                }
                int offer = LayerOffer(zips, fm2);
                string layerName = fm2 ? DlcPackage.LayerFm2(offer) : offer.ToString("X4", CultureInfo.InvariantCulture) + "_pri_65";
                string layerZip = Path.Combine(zips, layerName + ".zip");
                string layer = Path.Combine(zips, layerName);
                bool zipped = File.Exists(layerZip);
                if (zipped)
                {
                    // unpacked next to the zip, written again at the end
                    ForzaZip archive = new ForzaZip(layerZip);
                    foreach (ForzaZip.Entry entry in archive.Entries)
                    {
                        if (entry.IsDirectory)
                        {
                            continue;
                        }
                        string path = Path.Combine(layer, entry.Name.Replace('/', '\\'));
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        File.WriteAllBytes(path, archive.Read(entry));
                    }
                }
                else if (!Directory.Exists(layer))
                {
                    throw new InvalidOperationException("The package has no " + layerName + " layer (zip or folder) in Media\\DLCZips.");
                }
                string oldMerge = FindMerge(layer);
                if (oldMerge == null)
                {
                    throw new InvalidOperationException("The package has no merge database (db\\patch\\*_merge.slt) in " + layerName + ".");
                }
                string stringsPattern = fm2 ? "DLC" + offer.ToString(CultureInfo.InvariantCulture) + "Overrides1_pri_*.zip" : "StringTables_pri_*.zip";
                string[] oldStringsFiles = Directory.GetFiles(zips, stringsPattern);
                if (oldStringsFiles.Length != 1)
                {
                    throw new InvalidOperationException("The package has " + oldStringsFiles.Length + " string zips (" + stringsPattern + "), one expected.");
                }
                string oldStringsPath = oldStringsFiles[0];

                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "package offer {0}{1}; building the new cars for it:", offer, zipped ? " (zipped layer)" : " (folder layer)"));
                string content = Path.Combine(work, "new");
                // the other installed packs: the package's own merge left out by its bytes (a zipped layer's or an STFS file's
                // merge is a copy in the temp folder, not under the package's path)
                string own = package.TrimEnd('\\');
                byte[] ownMerge = File.ReadAllBytes(oldMerge);
                List<string> others = DbIds.PackDatabases(installedContent);
                others.RemoveAll(delegate(string path) { return path.StartsWith(own + "\\", StringComparison.OrdinalIgnoreCase) || (new FileInfo(path).Length == ownMerge.Length && Same(File.ReadAllBytes(path), ownMerge)); });
                string made = fm2 ? DlcPackage.BuildFolderFm2(targetRoot, trialRoot, content, installedContent, offer, name ?? "", report)
                    : DlcPackage.BuildFolderFm3(targetRoot, trialRoot, content, installedContent, offer, name ?? "", report, package);   // the package itself is not "another installed pack with that offer"
                string madeZips = Path.Combine(Path.Combine(made, "Media"), "DLCZips");
                string madeLayer = Path.Combine(madeZips, layerName);
                string newMerge = FindMerge(madeLayer);
                string[] madeStringsFiles = Directory.GetFiles(madeZips, stringsPattern);
                if (newMerge == null || madeStringsFiles.Length != 1)
                {
                    throw new InvalidOperationException("The new build has no merge or no string zip.");
                }

                report.AppendLine("adding to the package:");
                byte[] oldBytes = File.ReadAllBytes(oldMerge);
                byte[] merged = DlcPackage.AppendMerge(oldBytes, File.ReadAllBytes(newMerge), offer, report, fm2 ? null : "MultiDiscContentPacks");
                CheckCars(oldBytes, merged);
                List<long> cars = new List<long>();
                using (SqliteDatabase db = SqliteDatabase.FromBytes(merged))
                {
                    foreach (object[] row in db.Query("SELECT Id FROM Data_Car ORDER BY Id").Rows)
                    {
                        cars.Add(Convert.ToInt64(row[0], CultureInfo.InvariantCulture));
                    }
                    if (fm2)
                    {
                        merged = SeparateContentIds(db, oldBytes, report) ?? merged;
                    }
                }
                DbIntegrity.Result check = DbIntegrity.CheckMerge(File.ReadAllBytes(CarPortFm2Chain.FindDatabase(targetRoot)), others, merged, game);
                if (!check.Ok)
                {
                    throw new InvalidOperationException("The package's database with the new cars would break the game's, nothing was written:\r\n  " + string.Join("\r\n  ", check.Errors.ToArray()));
                }
                report.AppendLine("database check: the package's merge with the new cars, merged into the game's, keeps the rules");

                // Files: the new layer's into the old one (same bytes skipped), the merge replaced.
                string root = madeLayer.TrimEnd('\\') + "\\";
                int added = 0, same = 0, replaced = 0;
                foreach (string file in Directory.GetFiles(madeLayer, "*", SearchOption.AllDirectories))
                {
                    string relative = file.Substring(root.Length);
                    if (relative.EndsWith("_merge.slt", StringComparison.OrdinalIgnoreCase) || relative.EndsWith("_build_report.html", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    string destination = Path.Combine(layer, relative);
                    byte[] data = File.ReadAllBytes(file);
                    if (!File.Exists(destination))
                    {
                        added++;
                    }
                    else if (Same(File.ReadAllBytes(destination), data))
                    {
                        same++;
                        continue;
                    }
                    else
                    {
                        replaced++;
                        report.AppendLine("  replaced " + relative);
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.WriteAllBytes(destination, data);
                }
                File.WriteAllBytes(oldMerge, merged);
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "files: {0} added, {1} replaced, {2} already the same", added, replaced, same));
                if (zipped)
                {
                    File.Delete(layerZip);
                    int count = DlcPackage.WriteStoredZip(layer, layerZip, null, fm2);
                    Directory.Delete(layer, true);
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0}.zip written again: {1} files ({2})", layerName, count, fm2 ? "deflate, as FM2's" : "as the game's zips"));
                }

                // Strings: the new tables with the old-only keys, in place of the old zip.
                ForzaZip strings = new ForzaZip(madeStringsFiles[0]);
                report.AppendLine(MergeStrings(strings, new ForzaZip(oldStringsPath), offer, -1));
                string newStringsPath = Path.Combine(zips, Path.GetFileName(madeStringsFiles[0]));
                strings.Save(newStringsPath + ".new");
                File.Delete(oldStringsPath);
                if (File.Exists(newStringsPath))
                {
                    File.Delete(newStringsPath);
                }
                File.Move(newStringsPath + ".new", newStringsPath);
                Lists(zips, Path.GetFileName(oldStringsPath), Path.GetFileName(newStringsPath), Path.Combine(madeZips, "stringtables.xml"));

                // Licence masks: every car of the package.
                string masks = Path.Combine(Path.Combine(old, "Media"), "LicenseMasks");
                Directory.CreateDirectory(masks);
                if (fm2)
                {
                    StringBuilder sql = new StringBuilder();
                    foreach (long car in cars)
                    {
                        sql.Append("UPDATE Data_Car SET IsPurchased=1 WHERE Id=" + car.ToString(CultureInfo.InvariantCulture) + ";\r\n");
                    }
                    sql.Append("\r\n\r\n");
                    sql.Append(Fm2StockGarage.LicenseInsert(cars));
                    File.WriteAllText(Path.Combine(masks, "00000001.sql"), sql.ToString(), Encoding.ASCII);
                    File.WriteAllText(Path.Combine(masks, "00000002.sql"), sql.ToString(), Encoding.ASCII);
                }
                else
                {
                    File.WriteAllText(Path.Combine(masks, "00000001.sql"), "UPDATE Data_Car SET IsPurchased=1 WHERE ID IN (" + string.Join(",", cars.ConvertAll<string>(delegate(long car) { return car.ToString(CultureInfo.InvariantCulture); }).ToArray()) + ");", Encoding.ASCII);
                }
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "licence masks: {0} cars", cars.Count));

                // The package itself, replaced only now.
                string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
                string bak = Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), "bak");
                if (isFile)
                {
                    string output = package + ".new";
                    try
                    {
                        StfsPackage.Rewrite(old, output, stfs.Header);
                        if (StfsReader.Open(output).Files().Count == 0)
                        {
                            throw new InvalidOperationException("The rewritten package is empty.");
                        }
                        if (BackupPolicy.Enabled)
                        {
                            Directory.CreateDirectory(bak);
                            string backup = BackupPolicy.Copy(package, Path.Combine(bak, stamp + "_bak_" + Path.GetFileName(package)));
                            report.AppendLine("original kept: " + backup);
                        }
                        File.Delete(package);
                        File.Move(output, package);
                    }
                    finally
                    {
                        if (File.Exists(output))
                        {
                            File.Delete(output);
                        }
                    }
                }
                else
                {
                    if (BackupPolicy.Enabled)
                    {
                        string backup = Path.Combine(bak, stamp + "_bak_" + Path.GetFileName(own));
                        CopyTree(package, backup);
                        report.AppendLine("original kept: " + backup);
                    }
                    Directory.Delete(package, true);
                    CopyTree(old, package);
                }
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "package {0} written: {1} cars in all", package, cars.Count));
                return package;
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

        // The packages of a game a car can be added to: folder packs (as this program builds them) and STFS files of the game in
        // Xenia's content folder. Each: path, a label.
        public static List<KeyValuePair<string, string>> Packages(ForzaGame game, string content)
        {
            List<KeyValuePair<string, string>> found = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(content) || !Directory.Exists(content))
            {
                return found;
            }
            bool fm2 = game == ForzaGame.Motorsport2;
            foreach (string folder in Directory.GetDirectories(content))
            {
                string zips = Path.Combine(Path.Combine(folder, "Media"), "DLCZips");
                if (!Directory.Exists(zips))
                {
                    continue;
                }
                string name = Path.GetFileName(folder);
                bool ours = fm2 ? Regex.IsMatch(name, "^DLC[0-9]+$", RegexOptions.IgnoreCase) : name.StartsWith(DlcPackage.TitleIdFm3 + "0CCF", StringComparison.OrdinalIgnoreCase);
                if (ours)
                {
                    int offer;
                    try
                    {
                        offer = LayerOffer(zips, fm2);
                    }
                    catch (InvalidOperationException)
                    {
                        continue;
                    }
                    found.Add(new KeyValuePair<string, string>(folder, "folder pack, offer " + offer.ToString(CultureInfo.InvariantCulture)));
                }
            }
            foreach (string file in Directory.GetFiles(content))
            {
                try
                {
                    if (!StfsReader.IsPackage(file))
                    {
                        continue;
                    }
                    StfsReader stfs = StfsReader.Open(file);
                    if (GameInfo.FromTitleId(stfs.TitleId) == game)
                    {
                        found.Add(new KeyValuePair<string, string>(file, "package file \"" + stfs.DisplayName + "\""));
                    }
                }
                catch (Exception)
                {
                    // not a package this program reads
                }
            }
            return found;
        }

        // The offer of a package's layer: FM2 DLC<offer>_pri_65(.zip), FM3 <offer hex>_pri_65(.zip).
        private static int LayerOffer(string zips, bool fm2)
        {
            Regex layer = fm2 ? new Regex("^DLC([0-9]+)_pri_65(\\.zip)?$", RegexOptions.IgnoreCase) : new Regex("^([0-9A-F]{4})_pri_65(\\.zip)?$", RegexOptions.IgnoreCase);
            foreach (string item in Directory.GetFileSystemEntries(zips))
            {
                Match m = layer.Match(Path.GetFileName(item));
                if (m.Success)
                {
                    return fm2 ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : int.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                }
            }
            throw new InvalidOperationException("The package has no " + (fm2 ? "DLC<offer>_pri_65" : "<offer>_pri_65") + " layer in Media\\DLCZips.");
        }

        private static string FindMerge(string layer)
        {
            if (!Directory.Exists(layer))
            {
                return null;
            }
            foreach (string file in Directory.GetFiles(layer, "*_merge.slt", SearchOption.AllDirectories))
            {
                if (file.IndexOf("\\db\\patch\\", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return file;
                }
            }
            return null;
        }

        // FM2: every car of a pack has a ContentId of its own (BuildFolderFm2); a new car with an id the package's old cars use
        // (the package was not installed when the trial ran) gets the next free one. null: nothing changed.
        private static byte[] SeparateContentIds(SqliteDatabase merged, byte[] oldMerge, StringBuilder report)
        {
            HashSet<long> oldCars = new HashSet<long>();
            HashSet<long> used = new HashSet<long>();
            using (SqliteDatabase old = SqliteDatabase.FromBytes(oldMerge))
            {
                if (!DbIds.HasTable(old, "Data_Car"))
                {
                    return null;
                }
                foreach (object[] row in old.Query("SELECT Id, ContentId FROM Data_Car").Rows)
                {
                    oldCars.Add(Convert.ToInt64(row[0], CultureInfo.InvariantCulture));
                    if (row[1] != null && !(row[1] is DBNull))
                    {
                        used.Add(Convert.ToInt64(row[1], CultureInfo.InvariantCulture));
                    }
                }
            }
            long next = 0;
            foreach (long id in used)
            {
                next = Math.Max(next, id + 1);
            }
            bool changed = false;
            foreach (object[] row in merged.Query("SELECT Id, ContentId FROM Data_Car ORDER BY Id").Rows)
            {
                long car = Convert.ToInt64(row[0], CultureInfo.InvariantCulture);
                if (oldCars.Contains(car) || row[1] == null || row[1] is DBNull || !used.Contains(Convert.ToInt64(row[1], CultureInfo.InvariantCulture)))
                {
                    continue;
                }
                merged.Execute("UPDATE Data_Car SET ContentId = ? WHERE Id = ?", next, car);
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  car {0}: ContentId {1} is an old car's of the package, {2} instead", car, row[1], next));
                used.Add(next++);
                changed = true;
            }
            return changed ? merged.ToBytes() : null;
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static void CopyTree(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from))
            {
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
            }
            foreach (string folder in Directory.GetDirectories(from))
            {
                CopyTree(folder, Path.Combine(to, Path.GetFileName(folder)));
            }
        }
    }
}
