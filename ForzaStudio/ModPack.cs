namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    // Mod pack (user 2026-11-10: "never touch the original files"): a car of the game (DVD1) or of an ORIGINAL DLC package
    // (one DlcCatalog knows: FM4 disc 2, the official car packs) is never changed where it is. Its edited files go into a
    // small Xbox 360 package of their own (an STFS file named by its content id, as the official ones; Xenia loads it from
    // its content folder without a header file, a modded console too):
    //   ForzaStudioMod.txt                       which car (car=), offer, source; how the editor finds the pack again
    //   Media\<offer>.puboffer
    //   Media\DLCZips\<offer>_pri_999.zip        media/cars/<car>/... (names in lower case)
    // The game mounts a DLCZips zip at game:\ with the priority in its name. Checked in game 2026-11-10 (as folders):
    //   - priority 99 beat disc 2's 0002_pri_65.zip (Camaro 35th, R34 V-Spec II);
    //   - priority 99 with the car's own spelling did NOT beat DVD1's media\cars\<car>.zip (zipmanifest priority 80);
    //     priority 999 with lower case names (as the zipmanifest spells them) did (R33 V-Spec). Both are used for all.
    // A car of an original DLC is made standalone, so the pack works for players without that DLC (R34 checked in game
    // with disc 2 removed: buyable, name, drive, sound; with disc 2 present it shows once): its wheels too, a merge database
    // mapping the car to the mod's own offer (DlcPackage.BuildOfferMerge), and the original pack's string tables (car
    // names) with its zipmount.xml. The car's database rows stay the base game's. Deleting the file gives the original back.
    //
    // One mod pack per car; the next Save of the same car replaces its file (the content id, so the name, changes).
    public static class ModPack
    {
        public const int Priority = 999;
        public const string MarkerName = "ForzaStudioMod.txt";
        public const string GameName = "Forza Motorsport 4";

        // What a standalone mod pack takes from the original DLC besides the car's own files.
        public sealed class Standalone
        {
            public long CarId;                                         // Data_Car.Id: mapped to the mod's offer
            public byte[] GameDb;                                      // the base game's gamedb.slt (offer checks)
            public Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);       // zip names (media/wheels/...)
            public Dictionary<string, byte[]> DlcZipFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase); // beside the zip in DLCZips
        }

        // The marker of a package file (its lines as key=value), or null when the file is not a mod pack. An original
        // package (DlcCatalog) is skipped without opening it.
        public static Dictionary<string, string> Marker(string file)
        {
            try
            {
                if (!StfsReader.IsPackage(file) || DlcCatalog.Find(DlcCatalog.ReadHeader(file)) != null)
                {
                    return null;
                }
                StfsReader package = StfsReader.Open(file);
                if (!package.Files().Exists(delegate(string f) { return f.Equals(MarkerName, StringComparison.OrdinalIgnoreCase); }))
                {
                    return null;
                }
                Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in Encoding.UTF8.GetString(package.ReadFile(MarkerName)).Split('\n'))
                {
                    int equals = line.IndexOf('=');
                    if (equals > 0)
                    {
                        values[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
                    }
                }
                return values.ContainsKey("car") ? values : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static bool IsModPack(string file)
        {
            return Marker(file) != null;
        }

        // The mod pack file of a car in the game's Xenia content folder, or null.
        public static string Find(string content, string media)
        {
            if (content == null || !Directory.Exists(content))
            {
                return null;
            }
            foreach (string file in Directory.GetFiles(content))
            {
                Dictionary<string, string> marker = Marker(file);
                if (marker != null && marker["car"].Equals(media, StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }
            return null;
        }

        // Writes the car's files from a mod pack file to a car zip (names relative to the car folder, like
        // DlcSource.ExtractCar) for editing; null when the mod pack has none.
        public static string ExtractCar(string file, string media, string zipPath)
        {
            StfsReader package = StfsReader.Open(file);
            string inside = package.Files().Find(delegate(string f)
            {
                string name = Path.GetFileName(f);
                return f.Replace('\\', '/').StartsWith("Media/DLCZips/", StringComparison.OrdinalIgnoreCase) && name.IndexOf("_pri_", StringComparison.OrdinalIgnoreCase) > 0
                    && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("StringTables", StringComparison.OrdinalIgnoreCase);
            });
            if (inside == null)
            {
                return null;
            }
            string mount = "stfs:modpack|" + file + "|" + inside;
            ForzaZip.Mount(mount, delegate { return package.OpenFile(inside); });
            ForzaZip zip = new ForzaZip(mount);
            Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            string prefix = "media/cars/" + media + "/";
            foreach (ForzaZip.Entry entry in zip.Entries)
            {
                string name = entry.Name.Replace('\\', '/');
                if (!entry.IsDirectory && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.Length > prefix.Length)
                {
                    files[name.Substring(prefix.Length)] = zip.Read(entry);
                }
            }
            return (files.Count == 0) ? null : WriteZip(files, zipPath);
        }

        // A car zip (names relative to the car folder) from files, LZX like the game's.
        public static string WriteZip(Dictionary<string, byte[]> files, string zipPath)
        {
            string work = zipPath + "_files";
            if (Directory.Exists(work))
            {
                Directory.Delete(work, true);
            }
            foreach (KeyValuePair<string, byte[]> file in files)
            {
                string path = Path.Combine(work, file.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, file.Value);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath));
            string temp = zipPath + ".tmp";
            DlcPackage.WriteStoredZip(work, temp);
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }
            File.Move(temp, zipPath);
            Directory.Delete(work, true);
            return zipPath;
        }

        // Creates or replaces the mod pack of a car from its edited car zip (entries relative to the car folder).
        // targetRoot: the FM4 game folder (free offer numbers), may be null. standalone: for a car of an original DLC, else
        // null. thumbnail: PNG for the package, may be null. Returns the mod pack file.
        public static string Write(string content, string targetRoot, string media, ForzaZip car, Standalone standalone, string sourceTitle, string sourceFile, byte[] thumbnail)
        {
            if (content == null)
            {
                throw new InvalidOperationException("The Xenia content folder of the game is not set (Tools > Settings > Folders).");
            }
            string old = Find(content, media);
            int offer = 0;
            string kept;
            Dictionary<string, string> oldMarker = (old == null) ? null : Marker(old);
            if (oldMarker != null && oldMarker.TryGetValue("offer", out kept))
            {
                int.TryParse(kept, NumberStyles.None, CultureInfo.InvariantCulture, out offer);
            }
            if (offer <= 0)
            {
                offer = FreeOffer(content, targetRoot);
            }
            string offerText = offer.ToString(CultureInfo.InvariantCulture);

            string work = Path.Combine(Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "ModPack"), media);
            if (Directory.Exists(work))
            {
                Directory.Delete(work, true);
            }
            // The zip's files under their lower case names.
            string files = Path.Combine(work, "files");
            string carPrefix = ("media/cars/" + media + "/").ToLowerInvariant();
            int count = 0;
            foreach (ForzaZip.Entry entry in car.Entries)
            {
                if (entry.IsDirectory || entry.IsDeleted)
                {
                    continue;
                }
                Stage(files, carPrefix + entry.Name.Replace('\\', '/'), car.Read(entry));
                count++;
            }
            if (count == 0)
            {
                throw new InvalidOperationException("The car zip has no file.");
            }
            if (standalone != null)
            {
                foreach (KeyValuePair<string, byte[]> file in standalone.Files)
                {
                    Stage(files, file.Key, file.Value);
                }
                if (standalone.CarId > 0 && standalone.GameDb != null)
                {
                    StringBuilder report = new StringBuilder();
                    byte[] merge = DlcPackage.BuildOfferMerge(standalone.GameDb, offer, new List<long> { standalone.CarId }, report);
                    Stage(files, "media/db/patch/" + offerText + "000_merge.slt", merge);
                }
            }

            // The package's folder.
            string package = Path.Combine(work, "package");
            string mediaFolder = Path.Combine(package, "Media");
            string zips = Path.Combine(mediaFolder, "DLCZips");
            Directory.CreateDirectory(zips);
            DlcPackage.WriteStoredZip(files, Path.Combine(zips, offerText + "_pri_" + Priority.ToString(CultureInfo.InvariantCulture) + ".zip"));
            if (standalone != null)
            {
                foreach (KeyValuePair<string, byte[]> file in standalone.DlcZipFiles)
                {
                    File.WriteAllBytes(Path.Combine(zips, file.Key), file.Value);
                }
            }
            File.WriteAllBytes(Path.Combine(mediaFolder, offerText + ".puboffer"), new byte[] { 0x20, 0x0D, 0x0A });
            StringBuilder marker = new StringBuilder();
            marker.Append("car=" + media + "\r\n");
            marker.Append("offer=" + offerText + "\r\n");
            marker.Append("source=" + (sourceTitle ?? "") + "\r\n");
            marker.Append("sourceFile=" + Path.GetFileName(sourceFile ?? "") + "\r\n");
            marker.Append("standalone=" + ((standalone != null) ? "1" : "0") + "\r\n");
            marker.Append("written=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\r\n");
            marker.Append("Made by ForzaStudio EVO: this package replaces the car's files (priority " + Priority + "). Delete the package to get the original car back.\r\n");
            File.WriteAllText(Path.Combine(package, MarkerName), marker.ToString());

            string temp = Path.Combine(work, "package.stfs");
            string id = StfsPackage.Write(package, temp, uint.Parse(DlcPackage.TitleId, NumberStyles.HexNumber, CultureInfo.InvariantCulture), 2,
                media + " (mod)", "ForzaStudio EVO mod pack: " + media + " replaces the car of " + (sourceTitle ?? "the game"), GameName, thumbnail);
            string target = Path.Combine(content, id);
            if (old != null && File.Exists(old))
            {
                File.Delete(old);
            }
            if (File.Exists(target))
            {
                File.Delete(target);
            }
            File.Move(temp, target);
            Directory.Delete(work, true);
            return target;
        }

        private static void Stage(string stage, string name, byte[] data)
        {
            string path = Path.Combine(stage, name.ToLowerInvariant().Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, data);
        }

        // What an original FM4 package holds for a car besides media/cars/<car>: media/wheels/<car>/ from its DLCZips zips and
        // loose <x>_pri_<n>\Media folders, its string tables and zipmount.xml (the car's name). The package is only read.
        public static Standalone StandaloneFrom(string packageFile, string media, string gameRoot)
        {
            Standalone result = new Standalone();
            string db = (gameRoot == null) ? null : Path.Combine(Path.Combine(Path.Combine(gameRoot, "Media"), "db"), "gamedb.slt");
            if (db != null && File.Exists(db))
            {
                result.GameDb = File.ReadAllBytes(db);
                using (SqliteDatabase game = SqliteDatabase.FromBytes(result.GameDb))
                {
                    object id = game.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", media);
                    result.CarId = (id is long) ? (long) id : 0;
                }
            }
            string wheels = ("media/wheels/" + media + "/").ToLowerInvariant();
            StfsReader package = StfsReader.Open(packageFile);
            foreach (string file in package.Files())
            {
                string name = file.Replace('\\', '/');
                const string Zips = "Media/DLCZips/";
                if (!name.StartsWith(Zips, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string rest = name.Substring(Zips.Length);
                if (rest.IndexOf('/') < 0)
                {
                    if (rest.StartsWith("StringTables", StringComparison.OrdinalIgnoreCase) || rest.Equals("zipmount.xml", StringComparison.OrdinalIgnoreCase)
                        || rest.Equals("stringtables.xml", StringComparison.OrdinalIgnoreCase))
                    {
                        result.DlcZipFiles[rest] = package.ReadFile(file);
                    }
                    else if (rest.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && rest.IndexOf("_pri_", StringComparison.OrdinalIgnoreCase) > 0)
                    {
                        string inside = file;
                        string mount = "stfs:modpack|" + packageFile + "|" + file;
                        ForzaZip.Mount(mount, delegate { return package.OpenFile(inside); });
                        ForzaZip zip = new ForzaZip(mount);
                        foreach (ForzaZip.Entry entry in zip.Entries)
                        {
                            string entryName = entry.Name.Replace('\\', '/');
                            if (!entry.IsDirectory && entryName.StartsWith(wheels, StringComparison.OrdinalIgnoreCase))
                            {
                                result.Files[entryName.ToLowerInvariant()] = zip.Read(entry);
                            }
                        }
                    }
                    continue;
                }
                // loose: <x>_pri_<n>/Media/wheels/<car>/...
                int media0 = rest.IndexOf("/Media/", StringComparison.OrdinalIgnoreCase);
                if (media0 > 0)
                {
                    string inGame = ("media/" + rest.Substring(media0 + "/Media/".Length)).ToLowerInvariant();
                    if (inGame.StartsWith(wheels, StringComparison.Ordinal))
                    {
                        result.Files[inGame] = package.ReadFile(file);
                    }
                }
            }
            return result;
        }

        // Every mod pack file of the content folder with its marker (car, offer, source, standalone, written).
        public static List<KeyValuePair<string, Dictionary<string, string>>> List(string content)
        {
            List<KeyValuePair<string, Dictionary<string, string>>> packs = new List<KeyValuePair<string, Dictionary<string, string>>>();
            if (content == null || !Directory.Exists(content))
            {
                return packs;
            }
            foreach (string file in Directory.GetFiles(content))
            {
                Dictionary<string, string> marker = Marker(file);
                if (marker != null)
                {
                    packs.Add(new KeyValuePair<string, Dictionary<string, string>>(file, marker));
                }
            }
            packs.Sort(delegate(KeyValuePair<string, Dictionary<string, string>> a, KeyValuePair<string, Dictionary<string, string>> b)
            {
                return string.Compare(a.Value["car"], b.Value["car"], StringComparison.OrdinalIgnoreCase);
            });
            return packs;
        }

        // Deletes a car's mod pack file. False when there is none.
        public static bool Remove(string content, string media)
        {
            string file = Find(content, media);
            if (file == null)
            {
                return false;
            }
            File.Delete(file);
            return true;
        }

        // A number no installed package (folders, the mod packs' markers, the game's database when its folder is known) uses.
        private static int FreeOffer(string content, string targetRoot)
        {
            int offer = 0;
            try
            {
                if (targetRoot != null && File.Exists(Path.Combine(Path.Combine(Path.Combine(targetRoot, "Media"), "db"), "gamedb.slt")))
                {
                    offer = DlcPackage.FreeOffer(targetRoot, content);
                }
            }
            catch (Exception)
            {
                offer = 0;
            }
            foreach (int installed in DlcPackage.InstalledOffers(content))
            {
                offer = Math.Max(offer, installed + 1);
            }
            foreach (string file in Directory.GetFiles(content))
            {
                Dictionary<string, string> marker = Marker(file);
                int used;
                if (marker != null && marker.ContainsKey("offer") && int.TryParse(marker["offer"], NumberStyles.None, CultureInfo.InvariantCulture, out used))
                {
                    offer = Math.Max(offer, used + 1);
                }
            }
            offer = Math.Max(offer, 91);
            while (Directory.Exists(DlcPackage.PackagePath(content, offer)))
            {
                offer++;
            }
            return offer;
        }
    }
}
