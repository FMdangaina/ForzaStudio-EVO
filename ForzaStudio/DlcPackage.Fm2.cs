namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    // A car of the editor as a Forza Motorsport 2 DLC pack for Xenia (ST-02, 2026-11-14), laid out as the unreleased DLCBeta
    // folder pack (the one FM2 folder pack known to load) and the official packs (March Car Pack DLC7, Nissan DLC1...):
    //   <content>\DLC<offer>\                          a plain folder, no Xenia header (DLCBeta has none)
    //   Media\DLCZips\zipmount.xml, stringtables.xml   DLC<offer>Overrides1_pri_<P>.zip -> game:\Media\StringTables\ (the WHOLE
    //                                                  tables: game + installed packs + the pack's cars)
    //   Media\DLCZips\DLC<offer>_pri_65\media\...      the loose files (DLCBeta_pri_65): cars\<car>\, wheels\<car>\, UI\Textures\
    //                                                  Thumbnails\, Audio\Cars\EmitterData\, Audio\Cars\Engines\DSPTuning\,
    //                                                  db\patch\<offer>0000_merge.slt
    //   Media\LicenseMasks\00000001.sql, 00000002.sql  UPDATE Data_Car SET IsPurchased=1 + the official StockGarage INSERT (both files,
    //                                                  as every official pack and DLCBeta have them)
    // The merge's car rows as the official ones: IsPurchased 0, IsDLC 1, a ContentId of its own (official 3 - 31, DLCBeta 50), and
    // no StockGarage / Rewards_DriverLevel rows (the licence mask lists the car; a DLC car is not sold by driver level). The car Id is
    // in FM2's free window (DbIds.Fm2FreeWindow: above 1024 a car stays locked). The first try (DLC71_FS, header, pri_66, IsDLC 0,
    // id 1026) was not seen by the game (user 2026-11-14).
    public static partial class DlcPackage
    {
        public const string TitleIdFm2 = "4D5307EA";

        public static string PackagePathFm2(string content, int offer)
        {
            return Path.Combine(content, "DLC" + offer.ToString(CultureInfo.InvariantCulture));
        }

        // The header the first FM2 packs got (DLC<offer>_FS.header): removed with the pack.
        public static string HeaderPathFm2(string content, int offer)
        {
            string full = Path.GetFullPath(content).TrimEnd('\\');
            return Path.Combine(Path.Combine(Path.Combine(Path.GetDirectoryName(full), "Headers"), Path.GetFileName(full)), "DLC" + offer.ToString(CultureInfo.InvariantCulture) + "_FS.header");
        }

        public static void RemoveFm2(string content, int offer)
        {
            foreach (string package in new string[] { PackagePathFm2(content, offer), PackagePathFm2(content, offer) + "_FS" })
            {
                if (Directory.Exists(package))
                {
                    Directory.Delete(package, true);
                }
            }
            string header = HeaderPathFm2(content, offer);
            if (File.Exists(header))
            {
                File.Delete(header);
            }
        }

        // The first offer above FM2's official packs (DLC1 - DLC7; Motegi 4, Road America 5) and the installed ones, at least 8.
        // DLCBeta's 70 (the editor's own reading of its 70001 merge) does not count.
        public static int FreeOfferFm2(string content)
        {
            int offer = 7;
            if (string.IsNullOrEmpty(content) || !Directory.Exists(content))
            {
                return offer + 1;
            }
            foreach (string item in Directory.GetFileSystemEntries(content))
            {
                string name = Path.GetFileName(item);
                try
                {
                    DlcSource source = Directory.Exists(item) ? DlcSource.Open(item) : (StfsReader.IsPackage(item) ? DlcSource.OpenPackage(item, true) : null);
                    if (source != null && (source.Game == ForzaGame.Motorsport2 || source.TitleId == Fm2Source.TitleId) && source.Offer > 0 && source.Offer < 70)
                    {
                        offer = Math.Max(offer, source.Offer);
                    }
                }
                catch (Exception)
                {
                }
                int value;
                if (name.StartsWith("DLC", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.Substring(3).Replace("_FS", ""), NumberStyles.None, CultureInfo.InvariantCulture, out value) && value < 70)
                {
                    offer = Math.Max(offer, value);
                }
            }
            return offer + 1;
        }

        // A ContentId no car of the game or of an installed pack has (official packs 3 - 31, DLCBeta 50): above them all.
        private static long FreeContentIdFm2(string targetRoot, string installed)
        {
            long top = 50;
            using (SqliteDatabase game = SqliteDatabase.FromBytes(File.ReadAllBytes(GameDb(targetRoot))))
            {
                top = Math.Max(top, Convert.ToInt64(game.Scalar("SELECT ifnull(max(ContentId), 0) FROM Data_Car"), CultureInfo.InvariantCulture));
            }
            foreach (string merge in DbIds.PackDatabases(installed))
            {
                try
                {
                    using (SqliteDatabase pack = SqliteDatabase.FromBytes(File.ReadAllBytes(merge)))
                    {
                        if (DbIds.HasTable(pack, "Data_Car") && DbIds.Schema(pack)["Data_Car"].Exists(delegate(string c) { return string.Equals(c, "ContentId", StringComparison.OrdinalIgnoreCase); }))
                        {
                            top = Math.Max(top, Convert.ToInt64(pack.Scalar("SELECT ifnull(max(ContentId), 0) FROM Data_Car"), CultureInfo.InvariantCulture));
                        }
                    }
                }
                catch (Exception)
                {
                }
            }
            return top + 1;
        }

        public static string BuildFolderFm2(string targetRoot, string trialRoot, string content, string installed, int offer, string name, StringBuilder report)
        {
            string layerTemp = Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "StringLayers_" + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture));
            try
            {
                List<StringLayer> layers = StringLayer.Installed(installed, layerTemp, report, ForzaGame.Motorsport2);
                return BuildFolderFm2With(targetRoot, trialRoot, content, installed, layers, offer, name, report);
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

        public static string LayerFm2(int offer)
        {
            return "DLC" + offer.ToString(CultureInfo.InvariantCulture) + "_pri_65";
        }

        // FM2 reads only the zips of Media\DLCZips (xenia.log 2026-11-14: DLCBeta_pri_65 and our loose DLC8_pri_65 folders were
        // never opened, the official dlc<N>_pri_6<x>.zip are), all deflate: the loose layer as DLC<offer>_pri_65.zip in deflate,
        // after the pack's database check has read the loose merge.
        public static void ZipLayerFm2(string package, int offer, StringBuilder report)
        {
            string folder = Path.Combine(Path.Combine(Path.Combine(package, "Media"), "DLCZips"), LayerFm2(offer));
            int count = WriteStoredZip(folder, folder + ".zip", null, true);
            Directory.Delete(folder, true);
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0}.zip: {1} files (deflate, as the official FM2 packs)", LayerFm2(offer), count));
        }

        private static string BuildFolderFm2With(string targetRoot, string trialRoot, string content, string installed, List<StringLayer> layers, int offer, string name, StringBuilder report)
        {
            string package = PackagePathFm2(content, offer);
            if (Directory.Exists(package))
            {
                throw new IOException("The package folder already exists: " + package);
            }
            string offerText = offer.ToString(CultureInfo.InvariantCulture);
            string zips = Path.Combine(Path.Combine(package, "Media"), "DLCZips");
            string loose = Path.Combine(Path.Combine(zips, LayerFm2(offer)), "media");
            Directory.CreateDirectory(loose);

            report.AppendLine("database:");
            byte[] merge = BuildMerge(File.ReadAllBytes(GameDb(targetRoot)), File.ReadAllBytes(GameDb(trialRoot)), offer, report, false, false);
            List<long> cars = new List<long>();
            long contentId = FreeContentIdFm2(targetRoot, installed);
            using (SqliteDatabase patch = SqliteDatabase.FromBytes(merge))
            {
                foreach (object[] row in patch.Query("SELECT Id FROM Data_Car ORDER BY Id").Rows)
                {
                    long car = Convert.ToInt64(row[0], CultureInfo.InvariantCulture);
                    if (!DbIds.Fm2UsableCarId(car))
                    {
                        throw new InvalidOperationException("FM2 pack: car Id " + car + " is below " + DbIds.Fm2FirstFreeCarId + " or one of the official DLC ids " + (DbIds.Fm2LastFreeCarId + 1) + "-" + DbIds.Fm2LastCarId + ".");
                    }
                    if (DbIds.Fm2CarIdWarning(car) != null)
                    {
                        report.AppendLine("  WARNING: " + DbIds.Fm2CarIdWarning(car));
                    }
                    cars.Add(car);
                }
                // As the official packs' car rows: not bought until the licence mask runs, a DLC car, a content id of its own.
                foreach (long car in cars)
                {
                    patch.Execute("UPDATE Data_Car SET IsPurchased = 0, IsDLC = 1, ContentId = ? WHERE Id = ?", contentId++, car);
                }
                // The official merges carry no StockGarage (the licence mask lists the car) and no driver-level rows (a DLC car is sold anyway).
                // No official merge nor DLCBeta has ArcadeCarList, CarExceptions, Combo_Engines, NewProfile_Arcade_Garage or WheelNormOffsets
                // either (offm harness 2026-11-14); with them the game hung on its loading screen (DLC8 Mustang, user 2026-11-14).
                foreach (string table in new string[] { Fm2StockGarage.Table, "Rewards_DriverLevel", "ArcadeCarList", "CarExceptions", "Combo_Engines", "NewProfile_Arcade_Garage", "WheelNormOffsets" })
                {
                    if (DbIds.HasTable(patch, table))
                    {
                        patch.Execute("DROP TABLE " + SqliteDatabase.Quote(table));
                        report.AppendLine("  " + table + ": left out (as the official packs)");
                    }
                }
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  Data_Car: IsPurchased 0, IsDLC 1, ContentId {0} (as the official packs; the licence mask buys and lists the car)", contentId - cars.Count));
                merge = patch.ToBytes();
            }
            string patchFolder = Path.Combine(Path.Combine(loose, "db"), "patch");
            Directory.CreateDirectory(patchFolder);
            File.WriteAllBytes(Path.Combine(patchFolder, offerText + "0000_merge.slt"), merge);

            // Strings: every table the copy changed, whole (the game's + the installed packs' + the copy's).
            report.AppendLine("strings:");
            int priority = 150 + offer;
            foreach (StringLayer layer in layers)
            {
                priority = Math.Max(priority, layer.Priority + 1);
            }
            string strings = "DLC" + offerText + "Overrides1_pri_" + priority.ToString(CultureInfo.InvariantCulture) + ".zip";
            int displayKey, descriptionKey, longKey;
            Dictionary<string, int> remap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            List<string> tables = BuildStringsFm3(targetRoot, trialRoot, Path.Combine(zips, strings), "FM2_" + offerText, name, layers, remap, out displayKey, out descriptionKey, out longKey, report);
            if (remap.Count > 0)
            {
                // string keys an installed pack already used moved: the merge written above follows them
                string mergePath = Path.Combine(patchFolder, offerText + "0000_merge.slt");
                using (SqliteDatabase patch = SqliteDatabase.FromBytes(File.ReadAllBytes(mergePath)))
                {
                    RemapStrings(patch, remap, report);
                    File.WriteAllBytes(mergePath, patch.ToBytes());
                }
            }

            report.AppendLine("files:");
            // the port's sound too (docs/PORT_PARITY.md C3): HarmonicTuning.zip / DSPTuning.zip / EmitterData.zip entries land in
            // Audio\Cars\Engines\HarmonicTuning\, ...\DSPTuning\, Audio\Cars\EmitterData\, the banks in Engines\Soundbanks\LOD1 and
            // LOD2 (DLCBeta's layout); a bank under a name FM2 has goes in when it differs
            CopyPortedFilesFm3(targetRoot, trialRoot, loose, report, true);

            // The licence masks: the cars bought, and listed (StockGarage) as the official packs do; the same in both files.
            StringBuilder sql = new StringBuilder();
            foreach (long car in cars)
            {
                sql.Append("UPDATE Data_Car SET IsPurchased=1 WHERE Id=" + car.ToString(CultureInfo.InvariantCulture) + ";\r\n");
            }
            sql.Append("\r\n\r\n");
            sql.Append(Fm2StockGarage.LicenseInsert(cars));
            string masks = Path.Combine(Path.Combine(package, "Media"), "LicenseMasks");
            Directory.CreateDirectory(masks);
            File.WriteAllText(Path.Combine(masks, "00000001.sql"), sql.ToString(), Encoding.ASCII);
            File.WriteAllText(Path.Combine(masks, "00000002.sql"), sql.ToString(), Encoding.ASCII);

            WriteXml(Path.Combine(zips, "zipmount.xml"), "zipmount", new string[] { "\t<zip Name=\"" + strings + "\" Mount=\"game:\\Media\\StringTables\\\" />" });
            List<string> lines = tables.ConvertAll<string>(delegate(string table) { return "\t<table Name=\"" + table.ToLowerInvariant() + "\" />"; });
            WriteXml(Path.Combine(zips, "stringtables.xml"), "stringtables", lines.ToArray());
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "package {0}: offer {1} \"{2}\", {3} car(s), layer {4}, merge {1}0000_merge.slt, strings priority {5}, no header (as DLCBeta)", package, offer, name, cars.Count, LayerFm2(offer), priority));
            return package;
        }
    }
}
