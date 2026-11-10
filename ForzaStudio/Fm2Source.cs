namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.Drawing.Imaging;
    using System.Globalization;
    using System.IO;
    using System.Text;

    // Forza Motorsport 2 as a port source (2026-10-21). FM2 files are not in FM4's form (a .carbin chunk tree, a much
    // older database), so one FM2 car is first laid out as a DlcSource-style stage CarPort reads like the FM3 / FH ones:
    //   model     the FM2 main .carbin exported to OBJ (CarModelFolder.ExportLegacy) and imported onto the FM4 donor's
    //             main .carbin (Prepare / Apply); donor parts the FM2 car lacks are removed but wheel and undercarriage;
    //             no _lod0 / _cockpit (the port rebuilds them from LOD 1, as for traffic cars);
    //   textures  FM2's nodamage / damage / lights / damagelights (CarPort.ConvertMotorsport3Texture);
    //   wheel     the car's own wheel (FM2 StockWheelID -> List_Wheels) rebuilt on the donor's wheel, a new List_Wheels row;
    //   thumbnail FM2's 256x128 DXT5 picture on FM4's 768x288 A8R8G8B8 canvas (Thumbnail_<new id>.xds, CarPort);
    //   exhaust   exhaust.txt: the donor's backfire points moved to this car's rear end and ride height (an estimate,
    //             the EXHAUST tab of the model view edits them);
    //   files     brakes, carattribs, shadersettings, physics, liverymasks, versiondata: the donor's;
    //   database  a copy of the FM4 database whose donor row is renamed to the FM2 car (MediaName, DisplayName, ModelShort,
    //             Year, Thumbnail): CarPort.Fm2Data then replaces the rows with the car's own FM2 values.
    // The values of the car itself (Fm2CarData / Fm2CarUpgrades / Fm2CarPowertrain) are applied by CarPort.
    public static class Fm2Source
    {
        public const uint TitleId = 0x4D5307EA;

        private const int ThumbnailWidth = 768;
        private const int ThumbnailHeight = 288;

        // The stage folder of one FM2 car; log gets one line per step.
        // overlay: the stage of an FM2 DLC package (DlcSource), whose car zip, merged database and wheels come before the
        // game's (fm2Root); null for the game itself.
        public static void Stage(string fm2Root, string fm4Root, string car, string donor, string stageDir, List<string> log, string overlay)
        {
            string carLow = car.ToLowerInvariant();
            string donorLow = donor.ToLowerInvariant();
            if (Directory.Exists(stageDir))
            {
                Directory.Delete(stageDir, true);
            }
            string media = Path.Combine(stageDir, "Media");
            Directory.CreateDirectory(media);
            string work = stageDir + "_work";
            if (Directory.Exists(work))
            {
                Directory.Delete(work, true);
            }
            string fm2Zip = Path.Combine(Path.Combine(Path.Combine(fm2Root, "Media"), "cars"), carLow + ".zip");
            string fm2DbPath = Path.Combine(Path.Combine(Path.Combine(fm2Root, "Media"), "DB"), "gamedb.slt");
            if (overlay != null)
            {
                string overlayZip = Path.Combine(Path.Combine(Path.Combine(overlay, "Media"), "cars"), carLow + ".zip");
                if (File.Exists(overlayZip))
                {
                    fm2Zip = overlayZip;
                }
                fm2DbPath = Path.Combine(Path.Combine(Path.Combine(overlay, "Media"), "db"), "gamedb.slt");
            }
            string fm4DbPath = Path.Combine(Path.Combine(Path.Combine(fm4Root, "Media"), "db"), "gamedb.slt");
            ForzaZip source = new ForzaZip(fm2Zip);
            ForzaZip.Entry carbinEntry = source.Find(carLow + ".carbin");
            if (carbinEntry == null)
            {
                throw new InvalidOperationException(car + ".zip has no " + carLow + ".carbin.");
            }
            string donorZipPath = Path.Combine(Path.Combine(Path.Combine(fm4Root, "Media"), "cars"), donor + ".zip");
            ForzaZip donorZip = new ForzaZip(donorZipPath);
            try
            {
                // --- model
                string objDir = Path.Combine(work, carLow);
                int objs = CarModelFolder.ExportLegacy(source.Read(carbinEntry), carLow + ".carbin", objDir, "FM2");
                List<string> fm2Parts = new List<string>();
                foreach (string lod in Directory.GetDirectories(objDir))
                {
                    foreach (string file in Directory.GetFiles(lod, "*.obj"))
                    {
                        string part = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                        if (!fm2Parts.Contains(part))
                        {
                            fm2Parts.Add(part);
                        }
                    }
                }
                log.Add(string.Format("FM2 model {0}: {1} OBJ files, {2} parts", car, objs, fm2Parts.Count));
                CarbinModel main = new CarbinModel(donorZip.Read(donorZip.Find(donorLow + ".carbin")));
                float donorRear = RearEnd(main);
                List<float[]> donorTips = new List<float[]>();
                for (int i = 0; i < main.ExhaustCount; i++)
                {
                    donorTips.Add(main.GetExhaustTip(i));
                }
                Dictionary<string, CarbinModel> models = new Dictionary<string, CarbinModel>();
                models[CarModelFolder.Main] = main;
                Dictionary<string, string> folders = new Dictionary<string, string>();
                folders[CarModelFolder.Main] = objDir;
                CarModelFolder.Plan plan = CarModelFolder.Prepare(folders, models, true, true);
                if (plan.Errors.Count > 0)
                {
                    throw new InvalidOperationException("The FM2 model does not fit the donor: " + string.Join("; ", plan.Errors.ToArray()));
                }
                StringBuilder report = new StringBuilder();
                CarModelFolder.Apply(plan, models, report);
                main = models[CarModelFolder.Main];
                List<string> removed = new List<string>();
                foreach (CarbinModel.Section section in new List<CarbinModel.Section>(main.Sections))
                {
                    string name = section.Name.ToLowerInvariant();
                    if (fm2Parts.Contains(name) || name == "wheel" || name.StartsWith("undercarriage"))
                    {
                        continue;
                    }
                    main.RemoveSection(section.Name);
                    removed.Add(section.Name);
                }
                log.Add(string.Format("model: {0} parts rebuilt or added, donor parts removed: {1}", plan.Parts.Count, string.Join(" ", removed.ToArray())));
                float rear = RearEnd(main);

                string carFolder = Path.Combine(Path.Combine(media, "cars"), car);
                Directory.CreateDirectory(carFolder);

                // --- exhaust: the donor's points at this car's rear end and ride height (estimate, editable)
                double donorRide = 0;
                double ride = 0;
                using (SqliteDatabase fm2Db = SqliteDatabase.FromBytes(File.ReadAllBytes(fm2DbPath)))
                using (SqliteDatabase fm4Db = SqliteDatabase.FromBytes(File.ReadAllBytes(fm4DbPath)))
                {
                    donorRide = ToDouble(fm4Db.Scalar("SELECT FrontStockRideHeight FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", donor));
                    ride = ToDouble(fm2Db.Scalar("SELECT ModelFrontStockRideHeight FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", car));
                }
                // The points come from the car's own exhaust parts: the centre of the rear end of each pipe that reaches the
                // rear (within 0.25 m of the rearmost one). The donor's are only the fallback for a car without exhaust parts.
                List<float[]> modelTips = new List<float[]>();
                float pipesEnd = float.MinValue;
                foreach (CarbinModel.Section section in main.Sections)
                {
                    if (section.Name.ToLowerInvariant().StartsWith("exhaust"))
                    {
                        pipesEnd = Math.Max(pipesEnd, section.Bounds[2] + section.Bounds[5]);
                    }
                }
                foreach (CarbinModel.Section section in main.Sections)
                {
                    if (section.Name.ToLowerInvariant().StartsWith("exhaust") && section.Bounds[2] + section.Bounds[5] > pipesEnd - 0.25f)
                    {
                        float[] tip = { section.Bounds[0], section.Bounds[1], section.Bounds[2] + section.Bounds[5] };
                        bool same = false;
                        foreach (float[] other in modelTips)
                        {
                            same |= Math.Abs(other[0] - tip[0]) < 0.05f && Math.Abs(other[1] - tip[1]) < 0.05f;
                        }
                        if (!same)
                        {
                            modelTips.Add(tip);
                        }
                    }
                }
                if (modelTips.Count > 0)
                {
                    StringBuilder tips = new StringBuilder();
                    tips.AppendLine("# backfire points, x y z (model axes): the rear end of each of the car's own exhaust pipes.");
                    tips.AppendLine("# Edit them in the EXHAUST tab of the model view or here (one line per pipe).");
                    foreach (float[] tip in modelTips)
                    {
                        tips.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:0.####} {1:0.####} {2:0.####}", tip[0], tip[1], tip[2]));
                    }
                    File.WriteAllText(Path.Combine(stageDir, "exhaust.txt"), tips.ToString());
                    log.Add(string.Format(CultureInfo.InvariantCulture, "exhaust.txt: {0} point(s) computed from the car's exhaust parts", modelTips.Count));
                }
                else if (donorTips.Count > 0)
                {
                    log.Add("exhaust: the car has no exhaust parts; the donor's points moved to its rear end and ride height (an estimate)");
                    StringBuilder tips = new StringBuilder();
                    tips.AppendLine("# backfire points, x y z (model axes): the donor's moved to this car's rear end (z) and ride height (y).");
                    tips.AppendLine("# An estimate: edit them in the EXHAUST tab of the model view or here (one line per pipe).");
                    foreach (float[] tip in donorTips)
                    {
                        tips.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:0.####} {1:0.####} {2:0.####}", tip[0], tip[1] + (donorRide - ride), tip[2] + (rear - donorRear)));
                    }
                    File.WriteAllText(Path.Combine(stageDir, "exhaust.txt"), tips.ToString());
                    log.Add(string.Format(CultureInfo.InvariantCulture, "exhaust.txt: {0} point(s) of the donor, z {1:+0.###;-0.###}, y {2:+0.###;-0.###}", donorTips.Count, rear - donorRear, donorRide - ride));
                }
                byte[] mainBytes = main.ToBytes();
                new CarbinModel(mainBytes);     // reread check
                File.WriteAllBytes(Path.Combine(carFolder, carLow + ".carbin"), mainBytes);

                // --- textures
                foreach (string texture in new string[] { "nodamage", "damage", "lights", "damagelights" })
                {
                    ForzaZip.Entry entry = source.Find(texture + ".xds");
                    if (entry == null)
                    {
                        log.Add("  no " + texture + ".xds in the FM2 car");
                        continue;
                    }
                    File.WriteAllBytes(Path.Combine(carFolder, texture + ".xds"), CarPort.ConvertMotorsport3Texture(source.Read(entry), texture + ".xds"));
                }

                // --- the donor's files
                foreach (ForzaZip.Entry entry in donorZip.Entries)
                {
                    string name = entry.Name.ToLowerInvariant();
                    bool brake = name.Contains("_caliper") || name.Contains("_rotor");
                    if (!(brake || name == "carattribs.xml" || name == "shadersettings.xml" || name.StartsWith("physics/") || name.StartsWith("liverymasks/") || name == "versiondata.xml"))
                    {
                        continue;
                    }
                    string target = brake ? carLow + name.Substring(donorLow.Length) : entry.Name;
                    string path = Path.Combine(carFolder, target.Replace('/', '\\'));
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, donorZip.Read(entry));
                }
                DlcPackage.WriteStoredZip(carFolder, carFolder + ".zip");
                Directory.Delete(carFolder, true);

                // --- database
                string dbDir = Path.Combine(media, "db");
                Directory.CreateDirectory(dbDir);
                string db = Path.Combine(dbDir, "gamedb.slt");
                File.Copy(fm4DbPath, db);
                using (SqliteDatabase fm2Db = SqliteDatabase.FromBytes(File.ReadAllBytes(fm2DbPath)))
                using (SqliteDatabase stageDb = SqliteDatabase.FromBytes(File.ReadAllBytes(db)))
                {
                    SqliteDatabase.Result row = fm2Db.Query("SELECT Id, DisplayName, ModelShort, Year, StockWheelID FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", car);
                    if (row.Rows.Count == 0)
                    {
                        throw new InvalidOperationException("The FM2 database has no car " + car + ".");
                    }
                    object[] fm2Row = row.Rows[0];
                    long fm2Id = Convert.ToInt64(fm2Row[0]);
                    // A car of this name already in the target (ported earlier): not the one the port reads. Not when the
                    // donor has the car's own name (FM4's FER_GTO_84 as donor of FM2's): that row is the donor itself.
                    if (!string.Equals(car, donor, StringComparison.OrdinalIgnoreCase))
                    {
                        stageDb.Execute("DELETE FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", car);
                    }
                    string thumbnail = "game:\\Media\\UI\\Textures\\Thumbnails\\Thumbnail_" + fm2Id.ToString(CultureInfo.InvariantCulture) + ".xds";
                    stageDb.Execute("UPDATE Data_Car SET MediaName = ?, DisplayName = ?, ModelShort = ?, Year = ?, Thumbnail = ? WHERE MediaName = ? COLLATE NOCASE", car, fm2Row[1], fm2Row[2], fm2Row[3], thumbnail, donor);
                    log.Add(string.Format("database: donor row renamed to {0} ({1} / {2}, {3})", car, fm2Row[1], fm2Row[2], fm2Row[3]));
                    StageWheel(overlay, fm2Root, fm4Root, fm2Db, stageDb, media, work, car, donor, fm2Row[4], log);
                    File.WriteAllBytes(db, stageDb.ToBytes());
                    Thumbnail(overlay, fm2Root, media, fm2Id, log);
                }

                // --- strings (FM2's), empty audio zips, marker
                string tables = Path.Combine(media, "StringTables");
                Directory.CreateDirectory(tables);
                string tablesRoot = (overlay != null && Directory.Exists(Path.Combine(Path.Combine(overlay, "Media"), "StringTables"))) ? overlay : fm2Root;
                foreach (string zip in Directory.GetFiles(Path.Combine(Path.Combine(tablesRoot, "Media"), "StringTables"), "*.zip"))
                {
                    File.Copy(zip, Path.Combine(tables, Path.GetFileName(zip).ToUpperInvariant().Replace(".ZIP", ".zip")));
                }
                string audio = Path.Combine(Path.Combine(media, "audio"), "cars");
                string engines = Path.Combine(audio, "Engines");
                foreach (string folder in new string[] { Path.Combine(audio, "CarModelTuning"), Path.Combine(engines, "EngineTuning"), Path.Combine(engines, "HarmonicTuning") })
                {
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, "readme.txt"), "empty");
                    DlcPackage.WriteStoredZip(folder, folder + ".zip");
                    Directory.Delete(folder, true);
                }
                File.WriteAllLines(Path.Combine(stageDir, DlcSource.Marker), new string[] { "title=" + TitleId.ToString("X8", CultureInfo.InvariantCulture), "offer=0", "package=" + fm2Zip });
                log.Add("stage ready: " + stageDir);
            }
            finally
            {
                if (Directory.Exists(work))
                {
                    Directory.Delete(work, true);
                }
            }
        }

        // The car's own wheel: FM2 StockWheelID -> List_Wheels.MediaName, rebuilt on the FM4 donor's wheel (its LOD 0 is
        // dropped, the port rebuilds it from LOD 1), a List_Wheels row (the id of the same wheel in the target, else the
        // next free one) and its WheelNormOffsets. A car without a findable wheel keeps the donor's.
        private static void StageWheel(string overlay, string fm2Root, string fm4Root, SqliteDatabase fm2Db, SqliteDatabase stageDb, string media, string work, string car, string donor, object stockWheel, List<string> log)
        {
            SqliteDatabase.Result wheel = fm2Db.Query("SELECT MediaName, Mass FROM List_Wheels WHERE ID = ?", stockWheel);
            SqliteDatabase.Result donorWheel = stageDb.Query("SELECT w.MediaName FROM Data_Car c JOIN List_Wheels w ON w.ID = c.StockWheelID WHERE c.MediaName = ? COLLATE NOCASE", car);
            if (wheel.Rows.Count == 0 || !(wheel.Rows[0][0] is string) || donorWheel.Rows.Count == 0 || !(donorWheel.Rows[0][0] is string))
            {
                log.Add("wheel: the FM2 car's wheel or the donor's is not in the databases, the donor's stays");
                return;
            }
            string wheelMedia = (string) wheel.Rows[0][0];
            string wheelLow = wheelMedia.ToLowerInvariant();
            string donorWheelMedia = (string) donorWheel.Rows[0][0];
            string fm2WheelZip = Path.Combine(Path.Combine(Path.Combine(fm2Root, "Media"), "wheels"), wheelLow + ".zip");
            if (overlay != null && File.Exists(Path.Combine(Path.Combine(Path.Combine(overlay, "Media"), "wheels"), wheelLow + ".zip")))
            {
                fm2WheelZip = Path.Combine(Path.Combine(Path.Combine(overlay, "Media"), "wheels"), wheelLow + ".zip");
            }
            string donorWheelZip = Path.Combine(Path.Combine(Path.Combine(fm4Root, "Media"), "wheels"), donorWheelMedia + ".zip");
            if (!File.Exists(fm2WheelZip) || !File.Exists(donorWheelZip))
            {
                log.Add("wheel: " + wheelMedia + " or the donor's wheel zip is missing, the donor's stays");
                return;
            }
            ForzaZip wheelSource = new ForzaZip(fm2WheelZip);
            ForzaZip.Entry wheelCarbin = wheelSource.Find(wheelLow + ".carbin");
            if (wheelCarbin == null)
            {
                log.Add("wheel: " + wheelMedia + " has no carbin, the donor's stays");
                return;
            }
            string objDir = Path.Combine(work, wheelLow);
            CarModelFolder.ExportLegacy(wheelSource.Read(wheelCarbin), wheelLow + ".carbin", objDir, "FM2");
            ForzaZip wheelDonor = new ForzaZip(donorWheelZip);
            CarbinModel model = new CarbinModel(wheelDonor.Read(wheelDonor.Find(donorWheelMedia.ToLowerInvariant() + ".carbin")));
            DonorTires(objDir, model, log);
            // The donor's LOD 0 (a different mesh) is dropped, the port rebuilds it from LOD 1.
            File.AppendAllText(Path.Combine(objDir, CarModelFolder.ManifestName), "obj 0 lod0/" + model.Sections[0].Name + ".obj\r\n");
            Dictionary<string, CarbinModel> models = new Dictionary<string, CarbinModel>();
            models[CarModelFolder.Main] = model;
            Dictionary<string, string> folders = new Dictionary<string, string>();
            folders[CarModelFolder.Main] = objDir;
            CarModelFolder.Plan plan = CarModelFolder.Prepare(folders, models, true, true);
            if (plan.Errors.Count > 0)
            {
                log.Add("wheel: " + wheelMedia + " does not fit the donor's wheel (" + string.Join("; ", plan.Errors.ToArray()) + "), the donor's stays");
                return;
            }
            CarModelFolder.Apply(plan, models, new StringBuilder());
            model = models[CarModelFolder.Main];
            // Header floats 0x18..: empty (+-3.4e38) so the port rebuilds the tyre fit from the new LOD 1.
            for (int i = 0; i < 12; i++)
            {
                byte[] bytes = BitConverter.GetBytes(3.4e38f);
                for (int k = 0; k < 4; k++)
                {
                    model.Head[0x18 + (i * 4) + k] = bytes[3 - k];
                }
            }
            string wheelFolder = Path.Combine(Path.Combine(media, "wheels"), wheelMedia);
            Directory.CreateDirectory(wheelFolder);
            File.WriteAllBytes(Path.Combine(wheelFolder, wheelLow + ".carbin"), model.ToBytes());
            foreach (string texture in new string[] { "wheel", "wheel0", "wheel1", "wheel2", "wheel3" })
            {
                ForzaZip.Entry entry = wheelSource.Find(texture + ".xds");
                if (entry != null)
                {
                    File.WriteAllBytes(Path.Combine(wheelFolder, texture + ".xds"), CarPort.ConvertMotorsport3Texture(wheelSource.Read(entry), texture + ".xds"));
                }
            }
            DlcPackage.WriteStoredZip(wheelFolder, wheelFolder + ".zip");
            Directory.Delete(wheelFolder, true);

            // List_Wheels: the id the same wheel already has in the target (a reinstall), else the next free one.
            long id;
            using (SqliteDatabase target = SqliteDatabase.FromBytes(File.ReadAllBytes(Path.Combine(Path.Combine(Path.Combine(fm4Root, "Media"), "db"), "gamedb.slt"))))
            {
                object known = target.Scalar("SELECT ID FROM List_Wheels WHERE MediaName = ? COLLATE NOCASE", wheelMedia);
                id = (known != null) ? Convert.ToInt64(known) : DbIds.Next(target, "List_Wheels", "ID");
            }
            stageDb.Execute("DELETE FROM List_Wheels WHERE MediaName = ? COLLATE NOCASE OR ID = ?", wheelMedia, id);
            stageDb.Execute("INSERT INTO List_Wheels (ID, MediaName, PartManufacturerID, Mass, Price, IsStock, Lip, CenterCap, Logo, RGB, BrakeDrawOffset, IconPath) VALUES (?, ?, 0, ?, 0, 1, 0, 0, 0, 0, 0, ?)", id, wheelMedia, wheel.Rows[0][1] ?? (object) 40.0, wheelMedia);
            stageDb.Execute("UPDATE Data_Car SET StockWheelID = ? WHERE MediaName = ? COLLATE NOCASE", id, car);
            SqliteDatabase.Result offset = fm2Db.Query("SELECT NormOffset FROM WheelNormOffsets WHERE Id = ?", stockWheel);
            if (offset.Rows.Count > 0)
            {
                stageDb.Execute("DELETE FROM WheelNormOffsets WHERE Id = ?", id);
                stageDb.Execute("INSERT INTO WheelNormOffsets (Id, NormOffset) VALUES (?, ?)", id, offset.Rows[0][0]);
            }
            log.Add("wheel: " + wheelMedia + " rebuilt on the donor's " + donorWheelMedia + ", List_Wheels " + id);
        }

        // FM2's thumbnail (Media\UI\Textures.zip, Thumbnails\Thumbnail_<id>.xds) in FM4's format, loose in the stage where
        // the database row points.
        private static void Thumbnail(string overlay, string fm2Root, string media, long fm2Id, List<string> log)
        {
            // An FM2 DLC package has its pictures loose (Media\UI\Textures\Thumbnails\Thumbnail_<id>.xds).
            string loose = (overlay == null) ? null : Path.Combine(Path.Combine(Path.Combine(Path.Combine(Path.Combine(overlay, "Media"), "UI"), "Textures"), "Thumbnails"), "Thumbnail_" + fm2Id.ToString(CultureInfo.InvariantCulture) + ".xds");
            if (loose != null && File.Exists(loose))
            {
                byte[] picture = ThumbnailToMotorsport4(File.ReadAllBytes(loose));
                string target = Path.Combine(Path.Combine(Path.Combine(media, "UI"), "Textures"), "Thumbnails");
                Directory.CreateDirectory(target);
                File.WriteAllBytes(Path.Combine(target, "Thumbnail_" + fm2Id.ToString(CultureInfo.InvariantCulture) + ".xds"), picture);
                log.Add("thumbnail: from the DLC package, " + new XdsTexture(picture));
                return;
            }
            string zipPath = Path.Combine(Path.Combine(Path.Combine(fm2Root, "Media"), "UI"), "Textures.zip");
            if (!File.Exists(zipPath))
            {
                log.Add("thumbnail: " + zipPath + " not found");
                return;
            }
            ForzaZip.Entry entry = new ForzaZip(zipPath).Find("Thumbnails/Thumbnail_" + fm2Id.ToString(CultureInfo.InvariantCulture) + ".xds");
            if (entry == null)
            {
                log.Add("thumbnail: Thumbnail_" + fm2Id + ".xds is not in the FM2 UI textures");
                return;
            }
            byte[] converted = ThumbnailToMotorsport4(new ForzaZip(zipPath).Read(entry));
            string folder = Path.Combine(Path.Combine(Path.Combine(media, "UI"), "Textures"), "Thumbnails");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "Thumbnail_" + fm2Id.ToString(CultureInfo.InvariantCulture) + ".xds"), converted);
            log.Add("thumbnail: FM2 " + new XdsTexture(new ForzaZip(zipPath).Read(entry)) + " -> FM4 " + new XdsTexture(converted));
        }

        // The FM4 list thumbnail is 768x288 A8R8G8B8 (the FM2 one 256x128 DXT5): the picture scaled to the height,
        // centred on a transparent canvas.
        public static byte[] ThumbnailToMotorsport4(byte[] fm2Xds)
        {
            XdsTexture source = new XdsTexture(fm2Xds);
            using (Bitmap picture = source.GetLevelBitmap(0))
            using (Bitmap canvas = new Bitmap(ThumbnailWidth, ThumbnailHeight, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(canvas))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    int width = Math.Min(ThumbnailWidth, picture.Width * ThumbnailHeight / picture.Height);
                    graphics.DrawImage(picture, new Rectangle((ThumbnailWidth - width) / 2, 0, width, ThumbnailHeight));
                }
                return CanvasToXds(canvas);
            }
        }

        // The list picture of a car without one: a dark plate with the word, same 768x288 A8R8G8B8 as the others.
        public static byte[] GenericThumbnail()
        {
            using (Bitmap canvas = new Bitmap(ThumbnailWidth, ThumbnailHeight, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(canvas))
                using (Font font = new Font("Segoe UI", 40f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Brush text = new SolidBrush(Color.FromArgb(200, 200, 200, 200)))
                using (StringFormat centre = new StringFormat())
                {
                    graphics.Clear(Color.FromArgb(160, 24, 24, 28));
                    graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    centre.Alignment = StringAlignment.Center;
                    centre.LineAlignment = StringAlignment.Center;
                    graphics.DrawString("NO PICTURE", font, text, new RectangleF(0, 0, ThumbnailWidth, ThumbnailHeight), centre);
                }
                return CanvasToXds(canvas);
            }
        }

        private static byte[] CanvasToXds(Bitmap canvas)
        {
            byte[] bgra = new byte[ThumbnailWidth * ThumbnailHeight * 4];
            BitmapData data = canvas.LockBits(new Rectangle(0, 0, ThumbnailWidth, ThumbnailHeight), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < ThumbnailHeight; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + ((long) y * data.Stride)), bgra, y * ThumbnailWidth * 4, ThumbnailWidth * 4);
            }
            canvas.UnlockBits(data);
            XdsTexture result = XdsTexture.Create(ThumbnailWidth, ThumbnailHeight, DxtFormat.A8R8G8B8, false, 1);
            result.WriteLevelBlocks(0, bgra);
            return result.ToBytes();
        }

        // The model's rear end: the largest z of its parts' boxes (Section.Bounds = centre x y z, half extents x y z; z
        // points to the rear), wheels and brakes left out.
        // FM2's tire (LOD 1-5) has UV for FM2's tire texture, which FM4's tire technique does not read (the UV land
        // at random). The tire of each LOD is the donor's own FM4 tire of the same LOD, stretched onto the FM2 tire:
        // radially from the FM2 tire's inner to its outer radius, along the axle (X) over its width. The OBJ files of
        // the FM2 wheel are rewritten before the wheel is built on the donor (the donor's other materials stay out).
        private static void DonorTires(string objDir, CarbinModel donor, List<string> log)
        {
            CarbinModel.Section section = donor.Sections[0];
            foreach (string lodDir in Directory.GetDirectories(objDir))
            {
                string folder = Path.GetFileName(lodDir).ToLowerInvariant();
                uint lod;
                if (!folder.StartsWith("lod") || !uint.TryParse(folder.Substring(3), out lod) || lod == 0)
                {
                    continue;
                }
                foreach (string file in Directory.GetFiles(lodDir, "*.obj"))
                {
                    string temp = Path.Combine(Path.GetTempPath(), "ForzaStudio_tire_" + Guid.NewGuid().ToString("N") + ".obj");
                    try
                    {
                        ObjMesh fm2 = ObjMesh.Load(file);
                        ObjMesh.Material own = FindTire(fm2);
                        if (own == null)
                        {
                            log.Add("wheel LOD " + lod + ": the FM2 wheel has no tire material (" + MaterialNames(fm2) + "), left as it is");
                            continue;
                        }
                        if (ObjMesh.ExportLod(section, lod, temp, "donor tire", "x.mtl") == 0)
                        {
                            log.Add("wheel LOD " + lod + ": the donor's wheel has no LOD " + lod + ", the FM2 tire stays");
                            continue;
                        }
                        ObjMesh.Material from = FindTire(ObjMesh.Load(temp));
                        if (from == null)
                        {
                            log.Add("wheel LOD " + lod + ": the donor's wheel has no tire in this LOD, the FM2 tire stays");
                            continue;
                        }
                        float[] a = TireRange(own), b = TireRange(from);      // radius min, radius max, x min, x max
                        if (b[1] - b[0] < 1e-4f || b[3] - b[2] < 1e-4f)
                        {
                            continue;
                        }
                        float radial = (a[1] - a[0]) / (b[1] - b[0]), axial = (a[3] - a[2]) / (b[3] - b[2]);
                        List<ObjMesh.Corner[]> scaled = new List<ObjMesh.Corner[]>();
                        foreach (ObjMesh.Corner[] triangle in from.Triangles)
                        {
                            ObjMesh.Corner[] copy = new ObjMesh.Corner[3];
                            for (int i = 0; i < 3; i++)
                            {
                                ObjMesh.Corner c = triangle[i], d = new ObjMesh.Corner();
                                float r = (float) Math.Sqrt((c.Position.Y * c.Position.Y) + (c.Position.Z * c.Position.Z));
                                float factor = (r < 1e-6f) ? 1f : (a[0] + ((r - b[0]) * radial)) / r;
                                d.Position = new Microsoft.Xna.Framework.Vector3(((c.Position.X - ((b[2] + b[3]) / 2)) * axial) + ((a[2] + a[3]) / 2), c.Position.Y * factor, c.Position.Z * factor);
                                d.Uv = c.Uv;
                                d.HasUv = c.HasUv;
                                d.Normal = c.Normal;
                                d.HasNormal = c.HasNormal;
                                copy[i] = d;
                            }
                            scaled.Add(copy);
                        }
                        own.Triangles = scaled;
                        WriteObj(fm2, file);
                        log.Add(string.Format(CultureInfo.InvariantCulture, "wheel LOD {0}: tire = the donor's, radius {1:0.###}..{2:0.###} -> {3:0.###}..{4:0.###} (x{5:0.##}), width x{6:0.##}", lod, b[0], b[1], a[0], a[1], radial, axial));
                    }
                    finally
                    {
                        if (File.Exists(temp))
                        {
                            File.Delete(temp);
                        }
                    }
                }
            }
        }

        private static ObjMesh.Material FindTire(ObjMesh mesh)
        {
            foreach (ObjMesh.Material material in mesh.Materials)
            {
                string name = material.Name.ToLowerInvariant();
                if (name == "tire" || name == "tyre")
                {
                    return material;
                }
            }
            return null;
        }

        private static string MaterialNames(ObjMesh mesh)
        {
            List<string> names = new List<string>();
            foreach (ObjMesh.Material material in mesh.Materials)
            {
                names.Add(material.Name);
            }
            return string.Join(" ", names.ToArray());
        }

        // Radius about the axle (min, max) and x (min, max) of a material's vertices.
        private static float[] TireRange(ObjMesh.Material material)
        {
            float[] range = { float.MaxValue, 0f, float.MaxValue, float.MinValue };
            foreach (ObjMesh.Corner[] triangle in material.Triangles)
            {
                foreach (ObjMesh.Corner c in triangle)
                {
                    float r = (float) Math.Sqrt((c.Position.Y * c.Position.Y) + (c.Position.Z * c.Position.Z));
                    range[0] = Math.Min(range[0], r);
                    range[1] = Math.Max(range[1], r);
                    range[2] = Math.Min(range[2], c.Position.X);
                    range[3] = Math.Max(range[3], c.Position.X);
                }
            }
            return range;
        }

        // A plain OBJ (one vertex, uv and normal per triangle corner, one group per material).
        private static void WriteObj(ObjMesh mesh, string path)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            using (StreamWriter obj = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                int index = 1;
                foreach (ObjMesh.Material material in mesh.Materials)
                {
                    obj.WriteLine("g " + material.Name);
                    obj.WriteLine("usemtl " + material.Name);
                    foreach (ObjMesh.Corner[] triangle in material.Triangles)
                    {
                        foreach (ObjMesh.Corner c in triangle)
                        {
                            obj.WriteLine(string.Format(inv, "v {0:R} {1:R} {2:R}", c.Position.X, c.Position.Y, c.Position.Z));
                            obj.WriteLine(string.Format(inv, "vt {0:R} {1:R}", c.Uv.X, c.Uv.Y));
                            obj.WriteLine(string.Format(inv, "vn {0:R} {1:R} {2:R}", c.Normal.X, c.Normal.Y, c.Normal.Z));
                        }
                        obj.WriteLine("f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}", index, index + 1, index + 2);
                        index += 3;
                    }
                }
            }
        }

        private static float RearEnd(CarbinModel model)
        {
            float rear = float.MinValue;
            foreach (CarbinModel.Section section in model.Sections)
            {
                string name = section.Name.ToLowerInvariant();
                if (name.StartsWith("wheel") || name.StartsWith("undercarriage") || name.StartsWith("caliper") || name.StartsWith("rotor"))
                {
                    continue;
                }
                rear = Math.Max(rear, section.Bounds[2] + section.Bounds[5]);
            }
            return rear;
        }

        private static double ToDouble(object value)
        {
            return (value == null) ? 0 : Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
    }
}
