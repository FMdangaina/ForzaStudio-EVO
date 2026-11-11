namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Xml;

    // The car's database rows: ids, columns, ratings, makes, upgrades, tires, swaps (steps of Run).
    public partial class CarPort
    {
        #region Database

        // FM2 stage: the stage database holds the donor's rows under the FM2 name; the FM2 car's own data
        // (Fm2CarData / Fm2CarUpgrades / Fm2CarPowertrain) replaces them in the in-memory source copy, which
        // already has the car's final Id, before PortDatabase copies the rows. The FM2 game folder is the
        // one the stage's package= line points into (Media\cars\<car>.zip).

        private void Fm2Data()
        {
            string fm2Root = null;
            foreach (string line in File.ReadAllLines(Path.Combine(this.SourceRoot, DlcSource.Marker)))
            {
                if (line.StartsWith("package=", StringComparison.OrdinalIgnoreCase))
                {
                    string package = line.Substring("package=".Length);
                    fm2Root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(package)));
                }
            }
            string fm2Db = (fm2Root == null) ? null : Path.Combine(Path.Combine(Path.Combine(fm2Root, "Media"), "DB"), "gamedb.slt");
            if (fm2Db == null || !File.Exists(fm2Db))
            {
                this.Line("FM2 data: the FM2 game folder (stage package= line) has no Media\\DB\\gamedb.slt, the donor's rows stay");
                return;
            }
            using (SqliteDatabase fm2 = SqliteDatabase.FromBytes(File.ReadAllBytes(fm2Db)))
            {
                this.Line("FM2 data from {0}:", fm2Db);
                this.ReadSourcePistons(fm2);   // the stage's rows are the donor's: the FM2 car's own stock brakes
                object fm2PiValue = fm2.Scalar("SELECT PerformanceIndex FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", this.MediaName);
                if (fm2PiValue != null)
                {
                    this.fm2Pi = Convert.ToDouble(fm2PiValue, CultureInfo.InvariantCulture);
                    this.fm2PiPairs = PerformanceIndexPairs(fm2, this.target, this.carId, this.SourceRoot, this.TargetRoot, this.OutputRoot);
                }
                foreach (string line in Fm2CarData.Apply(this.source, fm2, this.MediaName, null))
                {
                    this.Line("  {0}", line);
                }
                foreach (string line in Fm2CarUpgrades.Apply(this.source, fm2, this.MediaName))
                {
                    this.Line("  {0}", line);
                }
                foreach (string line in Fm2CarPowertrain.Apply(this.source, fm2, this.MediaName))
                {
                    this.Line("  {0}", line);
                }
            }
        }

        private Dictionary<string, List<string>> Schema(SqliteDatabase db)
        {
            Dictionary<string, List<string>> schema = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (object[] row in db.Query("SELECT name FROM sqlite_master WHERE type = 'table'").Rows)
            {
                List<string> columns = new List<string>();
                foreach (object[] column in db.Query("PRAGMA table_info(" + SqliteDatabase.Quote((string) row[0]) + ")").Rows)
                {
                    columns.Add((string) column[1]);
                }
                schema[(string) row[0]] = columns;
            }
            return schema;
        }

        // Car tables that belong to the source game's own career, not to the car: FH's EventParticipants
        // put the car into FH events whose ids are other FM4 events (AUD_R8GT_11: 7 rows into FM4
        // events 70, 84, 90, 208, 217, 235 with Priority 0 and ColorSequence 2-4; FM4's own rows have
        // Priority 1..800 and ColorSequence 0).
        private static readonly string[] SourceGameOnly = { "EventParticipants" };

        private void PortDatabase()
        {
            Dictionary<string, List<string>> from = this.Schema(this.source);
            Dictionary<string, List<string>> to = this.Schema(this.target);
            string[] carColumns = { "Ordinal", "CarId", "CarID", "CarOrdinal" };
            int tables = 0;
            int rows = 0;
            List<object[]> engineRows = new List<object[]>();
            foreach (KeyValuePair<string, List<string>> table in to)
            {
                List<string> sourceColumns;
                if (!from.TryGetValue(table.Key, out sourceColumns) || table.Key == "Data_Car" || Array.IndexOf(SourceGameOnly, table.Key) >= 0)
                {
                    continue;
                }
                string where = null;
                foreach (string column in carColumns)
                {
                    if (Has(table.Value, column) && Has(sourceColumns, column))
                    {
                        where = SqliteDatabase.Quote(column) + " = " + this.carId;
                        break;
                    }
                }
                if (where == null && table.Key.StartsWith("List_UpgradeCarBody", StringComparison.Ordinal) && table.Value.Count > 0 && table.Value[0] == "Id")
                {
                    where = "Id / 1000 = " + this.carId;      // body part tables: Id = car * 1000 + level
                }
                if (where == null)
                {
                    continue;
                }
                int copied = this.CopyRows(table.Key, sourceColumns, table.Value, where);
                if (copied > 0)
                {
                    tables++;
                    rows += copied;
                }
            }
            this.PortCarRow(from["Data_Car"], to["Data_Car"]);
            this.Line("database: {0} rows in {1} car tables + Data_Car", rows, tables);

            // Engine, drivetrain, torque curves, wheel: rows the car refers to that FM4 lacks. An engine or
            // drivetrain another FM4 car already uses is FM4's own and stays as it is (FER_458Spider_12's
            // Enzo engine swap, EngineID 87, is used by 13 FM4 cars; copying it replaced their rows).
            List<long> engines = new List<long>();
            this.RemoveSwaps();
            foreach (long engine in this.Ids("SELECT DISTINCT EngineID FROM List_UpgradeEngine WHERE Ordinal = ?"))
            {
                if (this.Fixes.ContainsKey(SwapRemoveKey + engine.ToString(CultureInfo.InvariantCulture)))
                {
                    continue;
                }
                object media = this.source.Scalar("SELECT MediaName FROM Data_Engine WHERE EngineID = ?", engine);
                object present = this.target.Scalar("SELECT MediaName FROM Data_Engine WHERE EngineID = ?", engine);
                if (media == null)
                {
                    // A DLC package without its base game (DlcSource) lacks the engine rows of the swaps
                    // only the base game has. Engine ids mean the same in FH1 and FM4 (27 of 27 checked,
                    // 2026-10-17): FM4's engine is used. One FM4 lacks too stays for the review
                    // (ReviewDatabase: List_UpgradeEngine.EngineID, the user picks an FM4 engine).
                    this.Line(present != null ? "  engine {0}: not in the " + this.game + " source, FM4's {1} (same id) used" : "  engine {0}: neither in the " + this.game + " source nor in FM4 (swap of the base game), the review asks for an FM4 engine", engine, present);
                    continue;
                }
                if (present != null && !string.Equals((string) present, (string) media, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(string.Format("Engine {0} is {1} in FM4 and {2} in " + this.game + ".", engine, present, media));
                }
                if (Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM List_UpgradeEngine WHERE EngineID = ? AND Ordinal <> ?", engine, this.carId)) > 0)
                {
                    this.Line("  engine {0} ({1}): FM4's own, kept", engine, present);
                    continue;
                }
                engines.Add(engine);
                if (media != null)
                {
                    this.engineMedia.Add((string) media);
                }
            }
            this.CopyById(from, to, "EngineID", engines, "List_UpgradeEngine");
            List<long> drivetrains = new List<long>();
            foreach (long drivetrain in this.Ids("SELECT DISTINCT DrivetrainID FROM List_UpgradeDrivetrain WHERE Ordinal = ?"))
            {
                if (Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM List_UpgradeDrivetrain WHERE DrivetrainID = ? AND Ordinal <> ?", drivetrain, this.carId)) > 0)
                {
                    this.Line("  drivetrain {0}: FM4's own, kept", drivetrain);
                    continue;
                }
                drivetrains.Add(drivetrain);
            }
            this.CopyById(from, to, "DrivetrainID", drivetrains, "List_UpgradeDrivetrain");
            if (this.motorsport3)
            {
                this.ConvertMotorsport3TorqueCurves(to, engines);
            }
            else
            {
                this.CopyTorqueCurves(from, to, engines);
            }
            object wheel = this.source.Scalar("SELECT StockWheelID FROM Data_Car WHERE Id = ?", this.carId);
            if (wheel != null && this.Motorsport3Wheel(from, to, Convert.ToInt64(wheel)))
            {
                wheel = null;   // mapped to another FM4 id (Motorsport3Wheel)
            }
            if (wheel != null)
            {
                this.wheelIds.Add(Convert.ToInt64(wheel));
                // A wheel FM4 lists for a car other than this one is FM4's own (a port before this one
                // added the car's wheel together with the car).
                if (Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM Data_Car WHERE StockWheelID = ? AND Id <> ?", Convert.ToInt64(wheel), this.carId)) > 0)
                {
                    this.originalWheels.Add(Convert.ToInt64(wheel));
                }
                else
                {
                    this.CopyById(from, to, "ID", this.wheelIds, null, "List_Wheels");
                    this.CopyById(from, to, "WheelID", this.wheelIds, null, "WheelExceptions");
                    if (this.fm2Stage && from.ContainsKey("WheelNormOffsets") && to.ContainsKey("WheelNormOffsets"))
                    {
                        // The wheel's offset along its axle (FM2 and FM4 both have it; FM2's Veilside 0.01636).
                        this.CopyById(from, to, "Id", this.wheelIds, null, "WheelNormOffsets");
                    }
                }
            }
            this.CopyReferences(from, to);
            this.PortMake(from, to);
            if (this.motorsport3)
            {
                this.FillMotorsport4Columns(from, to);
                this.TuneMotorsport3Handling();
            }
            this.StockTires();
            this.DragTires();
            this.AddSwaps();
            this.GenericUpgrades();
            this.ReviewDatabase();
        }

        // Logo folders of UI\Textures.zip the game builds from IconPathBase (L_MAN_<base>_Logo.xds).
        private static readonly string[] LogoFolders = CarMake.LogoFolders;

        // A make FM4 has no row for comes from the source game: FM3's Porsche (MakeID 39, 28 FM3-only
        // cars) is the only one. FM4 still has its logos in every folder of UI\Textures.zip and its id
        // in List_PartAttribute and three body kit tables, but no List_CarMake row, names or livery
        // decals. The port adds the row, its names (PortStrings: DisplayName, Profile in
        // List_CarMake.str) and its Livery_DecalsSortOrder rows (the livery editor's decals for the
        // make; FM3's 69 Porsche rows all name decals FM4 has, same ids and files). FM4's rows have
        // IconPathBase, the logo's name part (L_MAN_<base>_Logo), and IconPath / IconPathLight in the
        // _I / _SM folders (FM3: Manufacturer_Logos, Manufacturer_Logos_SM\..._lt).
        private void PortMake(Dictionary<string, List<string>> from, Dictionary<string, List<string>> to)
        {
            object make = this.target.Scalar("SELECT MakeID FROM Data_Car WHERE Id = ?", this.carId);
            if (make == null || !from.ContainsKey("List_CarMake") || !to.ContainsKey("List_CarMake"))
            {
                return;
            }
            if (this.MakeId.HasValue && Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM List_CarMake WHERE ID = ?", this.MakeId.Value)) > 0)
            {
                this.target.Execute("UPDATE Data_Car SET MakeID = ? WHERE Id = ?", this.MakeId.Value, this.carId);
                this.Line("make {0} ({1}) -> FM4 make {2} ({3}), the user's choice", make, this.source.Scalar("SELECT ManufacturerCode FROM List_CarMake WHERE ID = ?", make), this.MakeId.Value, this.target.Scalar("SELECT ManufacturerCode FROM List_CarMake WHERE ID = ?", this.MakeId.Value));
                // Other rows of the car pointing at the old make (List_UpgradeDrivetrain.ManufacturerId)
                // follow it in the review step, unless the user fixed them otherwise.
                foreach (GameDbSchema.Relation relation in GameDbSchema.Into("List_CarMake"))
                {
                    string key = relation.Table + "." + relation.Column + "=" + Convert.ToString(make, CultureInfo.InvariantCulture);
                    if (!this.Fixes.ContainsKey(key))
                    {
                        this.Fixes[key] = this.MakeId.Value;
                    }
                }
                make = this.MakeId.Value;
            }
            if (Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM List_CarMake WHERE ID = ?", make)) > 0)
            {
                // FM4 names a car's make with the make's own string (all 504 cars); a make an earlier
                // port added may have got another string key.
                if (Has(to["Data_Car"], "MakeName"))
                {
                    this.target.Execute("UPDATE Data_Car SET MakeName = (SELECT DisplayName FROM List_CarMake WHERE ID = ?) WHERE Id = ?", make, this.carId);
                }
                return;
            }
            string where = "ID = " + Convert.ToInt64(make).ToString(CultureInfo.InvariantCulture);
            if (this.CopyRows("List_CarMake", from["List_CarMake"], to["List_CarMake"], where) == 0)
            {
                return;     // the source has no row either: the review asks for an FM4 make
            }
            this.newMake = Convert.ToInt64(make);
            string icon = Convert.ToString(this.source.Scalar("SELECT IconPath FROM List_CarMake WHERE " + where), CultureInfo.InvariantCulture);
            Match name = Regex.Match(Path.GetFileNameWithoutExtension(icon.Replace('\\', '/')), "^L_MAN_(.+?)_Logo", RegexOptions.IgnoreCase);
            if (name.Success && Has(to["List_CarMake"], "IconPathBase") && !Has(from["List_CarMake"], "IconPathBase"))
            {
                string logos = @"GAME:\Media\UI\Textures\UI4\Logos\";
                string file = @"\L_MAN_" + name.Groups[1].Value + "_Logo.tga";
                this.target.Execute("UPDATE List_CarMake SET IconPathBase = ?, IconPath = ?, IconPathLight = ? WHERE " + where,
                    name.Groups[1].Value, logos + "Manufacturer_Logos_I" + file, logos + "Manufacturer_Logos_SM" + file);
            }
            this.Line("make {0} ({1}): not in FM4, the " + this.game + " row added", make, this.source.Scalar("SELECT ManufacturerCode FROM List_CarMake WHERE " + where));
            if (name.Success)
            {
                List<string> missing = new List<string>();
                try
                {
                    ForzaZip textures = new ForzaZip(this.In(Path.Combine(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "UI"), "Textures.zip")));
                    foreach (string folder in LogoFolders)
                    {
                        if (textures.Find("UI4/Logos/" + folder + "/L_MAN_" + name.Groups[1].Value + "_Logo.xds") == null)
                        {
                            missing.Add(folder);
                        }
                    }
                }
                catch (IOException)
                {
                    missing.Add("UI\\Textures.zip unreadable");
                }
                if (missing.Count > 0 && !missing.Contains("UI\\Textures.zip unreadable") && !this.skipThumbnail)
                {
                    // A make FM4 has no logo for (Bowler from FH2): the source game's own logos, when it has the same files in the
                    // same layout (FH1 / FH2 and FM4 share the six folders, sizes and format), go into the target's Textures.zip
                    // (a pack then carries them as loose files). Without logos the game draws a black square.
                    string targetTextures = Path.Combine(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "UI"), "Textures.zip");
                    string sourceTextures = Path.Combine(Path.Combine(Path.Combine(this.SourceRoot, "Media"), "UI"), "Textures.zip");
                    if (File.Exists(sourceTextures))
                    {
                        ForzaZip sourceZip = new ForzaZip(sourceTextures);
                        ForzaZip to2 = this.OpenChanged(targetTextures);
                        List<string> stillMissing = new List<string>();
                        foreach (string folder in missing)
                        {
                            string entryName = "UI4/Logos/" + folder + "/L_MAN_" + name.Groups[1].Value + "_Logo.xds";
                            ForzaZip.Entry logo = sourceZip.Find(entryName);
                            ForzaZip.Entry reference = to2.Find("UI4/Logos/" + folder + "/L_MAN_Acura_Logo.xds");
                            bool same = false;
                            if (logo != null && reference != null)
                            {
                                XdsTexture a = new XdsTexture(sourceZip.Read(logo)), b = new XdsTexture(to2.Read(reference));
                                same = a.Width == b.Width && a.Height == b.Height && a.Codec == b.Codec;
                            }
                            if (same)
                            {
                                this.Put(to2, entryName, sourceZip.Read(logo));
                            }
                            else
                            {
                                stillMissing.Add(folder);
                            }
                        }
                        this.Line("  logos: {0} of {1} copied from {2}'s Textures.zip into the target's (same size and format as FM4's)", missing.Count - stillMissing.Count, missing.Count, this.game);
                        missing = stillMissing;
                    }
                }
                this.Line(missing.Count == 0 ? "  logos: L_MAN_{0}_Logo in all {1} logo folders" : "  WARNING logos: L_MAN_{0}_Logo missing in " + string.Join(", ", missing.ToArray()) + " (the game draws a black square)", name.Groups[1].Value, LogoFolders.Length);
            }
            if (from.ContainsKey("Livery_DecalsSortOrder") && to.ContainsKey("Livery_DecalsSortOrder"))
            {
                string decals = "MakeID = " + Convert.ToInt64(make).ToString(CultureInfo.InvariantCulture);
                int copied = this.CopyRows("Livery_DecalsSortOrder", from["Livery_DecalsSortOrder"], to["Livery_DecalsSortOrder"], decals);
                int dropped = this.target.Execute("DELETE FROM Livery_DecalsSortOrder WHERE " + decals + " AND Livery_DecalID NOT IN (SELECT ID FROM Livery_Decals)");
                this.Line("  livery decals: {0} Livery_DecalsSortOrder rows{1}", copied - dropped, (dropped > 0) ? string.Format(CultureInfo.InvariantCulture, " ({0} name decals FM4 lacks, left out)", dropped) : "");
            }
        }

        // FM4's cockpit camera hangs on CarPartPositions PartId 9, the steering wheel point, which FM3
        // lacks (PartId 0-8) and 13 FH1 traffic cars have as 0,0,0: either way the cockpit view sits at
        // 0,0,0. It is rebuilt from the converted cockpit model's steering_wheel section (CarCamera),
        // and a hood camera left at the origin is placed from it (CarCamera.SetHoodFromSteeringWheel).
        private void SteeringWheelPosition()
        {
            CarCamera current = CarCamera.Load(this.target, this.CarName);
            if (!this.traffic && current != null && current.HasSteeringWheel && current.SteeringWheel != Microsoft.Xna.Framework.Vector3.Zero)
            {
                return;
            }
            Microsoft.Xna.Framework.Vector3 point = Microsoft.Xna.Framework.Vector3.Zero;
            bool found = false;
            foreach (ForzaZip.Entry entry in this.carZip.Entries)
            {
                if (entry.Name.EndsWith("_cockpit.carbin", StringComparison.OrdinalIgnoreCase))
                {
                    found = CarCamera.SteeringWheelFromModel(new CarbinModel(this.carZip.Read(entry)), out point);
                    break;
                }
            }
            if (!found)
            {
                this.Line("  WARNING cockpit camera: no steering_wheel part in the cockpit model and no CarPartPositions part 9; the cockpit view will sit at 0,0,0 (set it in the model view, Cockpit Camera)");
                return;
            }
            CarCamera camera = CarCamera.Load(this.target, this.CarName);
            float oldX = camera.CockpitOffset.X;
            Microsoft.Xna.Framework.Vector3 oldOffset = camera.CockpitOffset;
            bool centred = camera.SetSteeringWheelFromModel(point);
            this.Line("  cockpit camera: CarPartPositions part 9 (steering wheel point) {0}, rebuilt from the cockpit model's steering_wheel", camera.HasSteeringWheel ? "is 0,0,0" : "missing");
            SqliteDatabase.Result reference = this.motorsport3 && !this.traffic ? this.target.Query("SELECT PartPosX, PartPosY, PartPosZ FROM CarPartPositions WHERE Ordinal = ? AND PartId = 4", this.carId) : null;
            if (reference != null && reference.Rows.Count > 0)
            {
                // The eye is the same point in both games, measured from FM3's part 4 or FM4's part 9 (inverse of CarPortToFm3.CockpitCamera):
                // FM4 offset = FM3 part 4 + FM3 offset - the steering wheel point rebuilt from the model.
                Microsoft.Xna.Framework.Vector3 part4 = new Microsoft.Xna.Framework.Vector3(Convert.ToSingle(reference.Rows[0][0], CultureInfo.InvariantCulture), Convert.ToSingle(reference.Rows[0][1], CultureInfo.InvariantCulture), Convert.ToSingle(reference.Rows[0][2], CultureInfo.InvariantCulture));
                camera.CockpitOffset = new Microsoft.Xna.Framework.Vector3((float) Math.Round(part4.X + oldOffset.X - point.X, 4), (float) Math.Round(part4.Y + oldOffset.Y - point.Y, 4), (float) Math.Round(part4.Z + oldOffset.Z - point.Z, 4));
                this.Line("  cockpit camera: FM3 eye = part 4 {0} + offset {1}; FM4 offset {2} from the model's steering wheel point", part4, oldOffset, camera.CockpitOffset);
            }
            else if (centred)
            {
                this.Line("  cockpit camera: CamCockpitOffsetX {0} -> 0 (FM4 centres the eye on the steering wheel)", oldX.ToString("0.###", CultureInfo.InvariantCulture));
            }
            foreach (string line in camera.Save(this.target))
            {
                this.Line("    {0}", line);
            }
            if (this.traffic)
            {
                this.TrafficCameras(camera);
            }
            string hood = camera.SetHoodFromSteeringWheel(this.target);
            if (hood != null)
            {
                this.Line("    {0}", hood);
            }
        }

        // FH traffic cars (close-up files rebuilt) were never driven in their game and their camera data
        // is not usable (BUS_TOUR_12: hood camera in the middle of the bus, follow offsets of +3 m, part 9
        // 0,0,0, parts 2-3 at 0,0.1,0 and on the roof centre): it is all generated (decided by the user
        // 2026-10-18). CameraOverrides: every column 0 as on FM4 cars (follow offsets 0 on 502 of 503
        // cars), the hood from the steering wheel point (SetHoodFromSteeringWheel). CarPartPositions,
        // measured on the 313 FM4 cars against the main model's body section box (database axes, Z
        // forward): parts 0/1 (front lights) x = -/+0.675 of the box's max x, y = box min y + 0.489,
        // z = front end - 0.216; parts 2/3 (rear lights) the same x, y = min y + 0.635, z = rear end
        // + 0.143; part 4 = part 9 + (0, -0.057, 0.015). Parts 5-8, the wheels, stay the source's.
        private void TrafficCameras(CarCamera camera)
        {
            List<string> columns = new List<string>();
            foreach (object[] column in this.target.Query("PRAGMA table_info(CameraOverrides)").Rows)
            {
                string name = (string) column[1];
                if (name != "CarId")
                {
                    columns.Add(SqliteDatabase.Quote(name) + " = 0");
                }
            }
            this.target.Execute("UPDATE CameraOverrides SET " + string.Join(", ", columns.ToArray()) + " WHERE CarId = ?", this.carId);
            camera.CockpitOffset = Microsoft.Xna.Framework.Vector3.Zero;
            this.Line("  traffic car: camera data generated, the source's is not used");
            this.Line("    CameraOverrides {0}: all offsets 0 (FM4's default cameras)", this.carId);

            CarbinModel.Section body = new CarbinModel(this.carZip.Read(this.carZip.Find(this.CarName.ToLowerInvariant() + ".carbin"))).Find("body");
            if (body == null)
            {
                this.Line("    CarPartPositions 0-3 kept: the main model has no body section");
            }
            else
            {
                float[] b = body.Bounds;
                float maxX = Math.Max(Math.Abs(b[0] + b[3]), Math.Abs(b[0] + b[6]));
                float minY = b[1] + b[4];
                float front = -(b[2] + b[5]), rear = -(b[2] + b[8]);   // model Z backward
                float x = 0.675f * maxX;
                this.SetPart(0, -x, minY + 0.489f, front - 0.216f, "front light L");
                this.SetPart(1, x, minY + 0.489f, front - 0.216f, "front light R");
                this.SetPart(2, -x, minY + 0.635f, rear + 0.143f, "rear light L");
                this.SetPart(3, x, minY + 0.635f, rear + 0.143f, "rear light R");
            }
            this.SetPart(4, camera.SteeringWheel.X, camera.SteeringWheel.Y - 0.057f, camera.SteeringWheel.Z + 0.015f, "next to the steering wheel");
        }

        private void SetPart(long part, float x, float y, float z, string what)
        {
            double px = Math.Round((double) x, 6), py = Math.Round((double) y, 6), pz = Math.Round((double) z, 6);
            if (this.target.Execute("UPDATE CarPartPositions SET PartPosX = ?, PartPosY = ?, PartPosZ = ? WHERE Ordinal = ? AND PartId = ?", px, py, pz, this.carId, part) == 0)
            {
                this.target.Execute("INSERT INTO CarPartPositions (ID, Ordinal, PartId, PartPosX, PartPosY, PartPosZ) VALUES (?, ?, ?, ?, ?, ?)", (this.carId * 1000) + part, this.carId, part, px, py, pz);
            }
            this.Line(string.Format(CultureInfo.InvariantCulture, "    CarPartPositions {0} part {1} ({2}) = {3}, {4}, {5}", this.carId, part, what, px, py, pz));
        }

        // FM3 torque curves -> FM4 (measured on the 941 camshaft rows of the engines both games have):
        // FM4 kept FM3's full-throttle curve (same values) under an id EngineID * 1000 + n, and FM3's
        // zero-throttle curve became the full curve's ZeroThrottleTorqueScale (= that curve's
        // TorqueScale). FM3 curves have up to 150 points, FM4's columns go to v185 (0 past the count).
        private void ConvertMotorsport3TorqueCurves(Dictionary<string, List<string>> to, List<long> engines)
        {
            List<string> columns = to["List_TorqueCurve"];
            int converted = 0;
            foreach (long engine in engines)
            {
                foreach (object[] cam in this.source.Query("SELECT Id, Level, TorqueCurveFullThrottleID, TorqueCurveZeroThrottleID FROM List_UpgradeEngineCamshaft WHERE EngineID = ?", engine).Rows)
                {
                    if (cam[2] == null)
                    {
                        continue;
                    }
                    SqliteDatabase.Result full = this.source.Query("SELECT * FROM List_TorqueCurve WHERE TorqueCurveID = ?", Convert.ToInt64(cam[2]));
                    if (full.Rows.Count == 0)
                    {
                        continue;
                    }
                    object zeroScale = (cam[3] != null) ? this.source.Scalar("SELECT TorqueScale FROM List_TorqueCurve WHERE TorqueCurveID = ?", Convert.ToInt64(cam[3])) : null;
                    long id = (engine * 1000) + Convert.ToInt64(cam[1]);
                    while (Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM List_TorqueCurve WHERE TorqueCurveID = ?", id)) > 0)
                    {
                        id++;
                    }
                    object[] values = new object[columns.Count];
                    for (int c = 0; c < columns.Count; c++)
                    {
                        int at = Array.FindIndex(full.Columns, delegate(string s) { return string.Equals(s, columns[c], StringComparison.OrdinalIgnoreCase); });
                        if (string.Equals(columns[c], "TorqueCurveID", StringComparison.OrdinalIgnoreCase))
                        {
                            values[c] = id;
                        }
                        else if (string.Equals(columns[c], "ZeroThrottleTorqueScale", StringComparison.OrdinalIgnoreCase))
                        {
                            values[c] = (zeroScale != null) ? zeroScale : (object) 0.0;
                        }
                        else
                        {
                            values[c] = (at >= 0) ? full.Rows[0][at] : (object) 0.0;
                        }
                    }
                    List<string> quoted = columns.ConvertAll<string>(delegate(string s) { return SqliteDatabase.Quote(s); });
                    this.target.Execute("INSERT INTO List_TorqueCurve (" + string.Join(",", quoted.ToArray()) + ") VALUES (" + string.Join(",", columns.ConvertAll<string>(delegate(string s) { return "?"; }).ToArray()) + ")", values);
                    this.target.Execute("UPDATE List_UpgradeEngineCamshaft SET TorqueCurveFullThrottleID = ? WHERE Id = ?", id, Convert.ToInt64(cam[0]));
                    this.writtenRows.Add(new string[] { "List_TorqueCurve", "TorqueCurveID = " + id });
                    converted++;
                }
            }
            if (converted > 0)
            {
                this.Line("  List_TorqueCurve: {0} FM3 curves under FM4 ids (zero-throttle curve -> ZeroThrottleTorqueScale)", converted);
            }
        }

        // FM3 wheel ids are FM4's but for two (707 renamed, 751 HOL_2_CommodoreVE_09 is FM4's
        // HOL_88_CommodoreVE_11): an id FM4 gives another wheel is mapped to the FM4 id of the same wheel
        // name, or to a new id. True when the wheel was handled here.
        private bool Motorsport3Wheel(Dictionary<string, List<string>> from, Dictionary<string, List<string>> to, long id)
        {
            string media = this.source.Scalar("SELECT MediaName FROM List_Wheels WHERE ID = ?", id) as string;
            string present = this.target.Scalar("SELECT MediaName FROM List_Wheels WHERE ID = ?", id) as string;
            if (media == null || present == null || string.Equals(media, present, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            object same = this.target.Scalar("SELECT ID FROM List_Wheels WHERE MediaName = ? COLLATE NOCASE", media);
            long newId;
            if (same != null)
            {
                newId = Convert.ToInt64(same);
                this.originalWheels.Add(newId);
            }
            else
            {
                newId = DbIds.Next(this.target, "List_Wheels", "ID");
                this.CopyRowAs("List_Wheels", from["List_Wheels"], to["List_Wheels"], "ID", id, newId);
                if (to.ContainsKey("WheelExceptions") && from.ContainsKey("WheelExceptions"))
                {
                    this.CopyRowAs("WheelExceptions", from["WheelExceptions"], to["WheelExceptions"], "WheelID", id, newId);
                }
                if (this.fm2Stage && to.ContainsKey("WheelNormOffsets") && from.ContainsKey("WheelNormOffsets"))
                {
                    this.CopyRowAs("WheelNormOffsets", from["WheelNormOffsets"], to["WheelNormOffsets"], "Id", id, newId);
                }
            }
            this.wheelIds.Add(newId);
            this.target.Execute("UPDATE Data_Car SET StockWheelID = ? WHERE Id = ?", newId, this.carId);
            this.Line("  wheel {0}: " + this.game + " id {1} is {2} in FM4, {3} id {4} used", media, id, present, (same != null) ? "its FM4" : "new", newId);
            return true;
        }

        // The source rows whose 'key' is oldId, written with newId (common columns).
        private void CopyRowAs(string table, List<string> sourceColumns, List<string> targetColumns, string key, long oldId, long newId)
        {
            List<string> columns = targetColumns.FindAll(delegate(string c) { return Has(sourceColumns, c); });
            List<string> quoted = columns.ConvertAll<string>(delegate(string c) { return SqliteDatabase.Quote(c); });
            int keyAt = columns.FindIndex(delegate(string c) { return string.Equals(c, key, StringComparison.OrdinalIgnoreCase); });
            foreach (object[] row in this.source.Query("SELECT " + string.Join(",", quoted.ToArray()) + " FROM " + SqliteDatabase.Quote(table) + " WHERE " + SqliteDatabase.Quote(key) + " = ?", oldId).Rows)
            {
                row[keyAt] = newId;
                this.target.Execute("INSERT INTO " + SqliteDatabase.Quote(table) + " (" + string.Join(",", quoted.ToArray()) + ") VALUES (" + string.Join(",", columns.ConvertAll<string>(delegate(string c) { return "?"; }).ToArray()) + ")", row);
            }
            this.writtenRows.Add(new string[] { table, SqliteDatabase.Quote(key) + " = " + newId });
        }

        // The FM4 columns FM3 lacks, in every row the port wrote, filled as FM4 filled them on the 315
        // cars both games have: stock ride heights = the car body's (452 of 503 FM4 cars),
        // CarPartPositions.ID = car * 1000 + PartId, chassis lateral and longitudinal friction = FM3's one
        // friction scale (194 rows equal, FM4 retuned the others); every other column the value FM4 uses
        // most (IsDrivable 1, MaxStretchDeltaFromRideHeight 0.5 on all 2686 rows, CVTShiftTime -1,
        // camera offsets 0...). Torque curves are written whole by ConvertMotorsport3TorqueCurves.
        private void FillMotorsport4Columns(Dictionary<string, List<string>> from, Dictionary<string, List<string>> to)
        {
            Dictionary<string, List<string>> wheres = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string[] written in this.writtenRows)
            {
                List<string> list;
                if (!wheres.TryGetValue(written[0], out list))
                {
                    list = new List<string>();
                    wheres[written[0]] = list;
                }
                if (!list.Contains(written[1]))
                {
                    list.Add(written[1]);
                }
            }
            List<string> filled = new List<string>();
            foreach (KeyValuePair<string, List<string>> table in wheres)
            {
                List<string> sourceColumns;
                List<string> targetColumns;
                if (table.Key == "List_TorqueCurve" || !from.TryGetValue(table.Key, out sourceColumns) || !to.TryGetValue(table.Key, out targetColumns))
                {
                    continue;
                }
                string where = "(" + string.Join(") OR (", table.Value.ToArray()) + ")";
                foreach (string column in targetColumns)
                {
                    if (Has(sourceColumns, column))
                    {
                        continue;
                    }
                    string t = SqliteDatabase.Quote(table.Key);
                    string c = SqliteDatabase.Quote(column);
                    // Combo_Colors.ID is FM4's INTEGER PRIMARY KEY: SQLite already gave the rows max(ID) + 1
                    // (1473002 after car 1473's colours, 2026-11-07), never NULL. FM4 numbers 2961 of its 2962
                    // rows car * 1000 + Sequence, as DbIds.CheckCar expects.
                    if (table.Key == "Combo_Colors" && column == "ID" && Has(sourceColumns, "Sequence"))
                    {
                        if (this.target.Execute("UPDATE Combo_Colors SET ID = Ordinal * 1000 + Sequence WHERE (" + where + ") AND ID <> Ordinal * 1000 + Sequence") > 0)
                        {
                            filled.Add("Combo_Colors.ID = car * 1000 + Sequence");
                        }
                        continue;
                    }
                    string missing = " WHERE (" + where + ") AND " + c + " IS NULL";
                    if (Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM " + t + missing)) == 0)
                    {
                        continue;
                    }
                    if (table.Key == "Data_Car" && (column == "FrontStockRideHeight" || column == "RearStockRideHeight"))
                    {
                        string body = "Model" + column;
                        this.target.Execute("UPDATE Data_Car SET " + c + " = round((SELECT " + SqliteDatabase.Quote(body) + " FROM Data_CarBody WHERE Data_CarBody.Id = Data_Car.Id * 1000), 4)" + missing);
                        filled.Add(table.Key + "." + column + " = the car body's");
                    }
                    else if (table.Key == "CarPartPositions" && column == "ID")
                    {
                        this.target.Execute("UPDATE CarPartPositions SET ID = Ordinal * 1000 + PartId" + missing);
                        filled.Add("CarPartPositions.ID = car * 1000 + PartId");
                    }
                    else if (table.Key == "List_UpgradeCarBodyChassisStiffness" && column.EndsWith("FrictionScale", StringComparison.Ordinal) && Has(sourceColumns, column.StartsWith("Front", StringComparison.Ordinal) ? "FrontFrictionScale" : "RearFrictionScale"))
                    {
                        string old = column.StartsWith("Front", StringComparison.Ordinal) ? "FrontFrictionScale" : "RearFrictionScale";
                        foreach (object[] row in this.target.Query("SELECT Id FROM " + t + missing).Rows)
                        {
                            this.target.Execute("UPDATE " + t + " SET " + c + " = ? WHERE Id = ?", this.source.Scalar("SELECT " + old + " FROM " + t + " WHERE Id = ?", row[0]), row[0]);
                        }
                        filled.Add(table.Key + "." + column + " = FM3 " + old);
                    }
                    else
                    {
                        object common = this.target.Scalar("SELECT " + c + " FROM " + t + " WHERE " + c + " IS NOT NULL GROUP BY " + c + " ORDER BY count(*) DESC LIMIT 1");
                        if (common == null)
                        {
                            continue;
                        }
                        this.target.Execute("UPDATE " + t + " SET " + c + " = ?" + missing, common);
                        filled.Add(table.Key + "." + column + " = " + Convert.ToString(common, CultureInfo.InvariantCulture));
                    }
                }
            }
            if (filled.Count > 0)
            {
                this.Line("  FM4 columns FM3 lacks: {0}", string.Join("; ", filled.ToArray()));
            }
            object gauge = null;
            if (from.ContainsKey("Data_Car") && Has(from["Data_Car"], "GaugeID"))
            {
                gauge = this.source.Scalar("SELECT GaugeID FROM Data_Car WHERE Id = ?", this.carId);
            }
            if (gauge != null && !(gauge is DBNull) && Convert.ToInt64(gauge) != -1)
            {
                // FM4 does not use GaugeID (-1 on all 503 cars); the dash goes into the car zip.
                this.target.Execute("UPDATE Data_Car SET GaugeID = -1 WHERE Id = ?", this.carId);
                if (Convert.ToInt64(gauge) > 0 && this.source.Scalar("SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'List_DigitalGauges'") != null)
                {
                    this.motorsport3Dash = Convert.ToString(this.source.Scalar("SELECT MediaName FROM List_DigitalGauges WHERE GaugeID = ?", gauge), CultureInfo.InvariantCulture);
                }
                this.Line("  digital dash: FM3 gauge {0} ({1}); Data_Car.GaugeID = -1 as on every FM4 car", gauge, string.IsNullOrEmpty(this.motorsport3Dash) ? "not in List_DigitalGauges" : this.motorsport3Dash);
            }
        }

        // FM4's spring rate over FM3's by FM4 class (median per class on the 315 cars both games have,
        // docs/FM3_GRIP_CM_SPRINGS.md).
        internal static readonly double[] SpringClassRatio = { 0.605, 0.748, 0.748, 0.805, 0.805, 0.857, 0.903, 0.941, 0.973, 1.0 };

        // FM3 centre of mass and springs on FM4's scale: FM4 CMHeight = FM3 CMHeight + stock ride height
        // (sd 2 cm), FM4 spring rates = FM3 x class ratio (median error 3.4 %), dampers and anti-roll bars
        // likewise by class. Without it FM3 ports sit too low and stiff, hence too much grip. Ride height
        // is left as it is.
        private void TuneMotorsport3Handling()
        {
            object ride = this.target.Scalar("SELECT avg(p.DefRideHeight) FROM List_UpgradeSpringDamper u JOIN List_SpringDamperPhysics p ON p.SpringDamperPhysicsID IN (u.FrontSpringDamperPhysicsID, u.RearSpringDamperPhysicsID) WHERE u.Ordinal = ? AND u.IsStock = 1", this.carId);
            object cls = this.target.Scalar("SELECT ClassID FROM Data_Car WHERE Id = ?", this.carId);
            long low = this.carId * 1000;
            long high = low + 999;
            if (ride != null && !(ride is DBNull))
            {
                double height = Convert.ToDouble(ride, CultureInfo.InvariantCulture);
                this.target.Execute("UPDATE List_UpgradeCarBodyWeight SET CMHeight = round(CMHeight + ?, 4) WHERE CarBodyID BETWEEN ? AND ?", height, low, high);
                this.Line("  FM3 handling: CMHeight + stock ride height {0:0.000} m", height);
            }
            if (cls != null && !(cls is DBNull))
            {
                long c = Math.Max(0, Math.Min(9, Convert.ToInt64(cls)));
                double ratio = SpringClassRatio[c];
                // Min and Max are the tuning slider's limits (FM4 keeps 20 / 150, 1 / 12, 1 / 40 on the rows that have them), so
                // they follow the scale only on the fixed parts, where they equal the default.
                this.target.Execute(ScaleSql("List_SpringDamperPhysics", "SpringRate", "SpringDamperPhysicsID"), ratio, ratio, ratio, low, high);
                this.Line("  FM3 handling: spring rates x {0} (FM4 class {1})", ratio, c);
                // Dampers: rebound = sqrt of the spring ratio, bump = rebound x 1.333 (clamps and bump stops are equal in both games).
                double bump = DamperBumpRatio[c];
                double rebound = DamperReboundRatio[c];
                this.target.Execute(ScaleSql("List_SpringDamperPhysics", "DampenBumpRate", "SpringDamperPhysicsID"), bump, bump, bump, low, high);
                this.target.Execute(ScaleSql("List_SpringDamperPhysics", "DampenReboundRate", "SpringDamperPhysicsID"), rebound, rebound, rebound, low, high);
                this.Line("  FM3 handling: dampers bump x {0}, rebound x {1}", bump, rebound);
                // Anti-roll bars (ids car*1000+0xx front, +1xx rear): FM4 stiffer in front, softer behind; FM4's ratios exist for classes 0-6 only.
                if (c < SwayFrontRatio.Length)
                {
                    this.target.Execute(ScaleSql("List_AntiSwayPhysics", "SwaybarStiffness", "AntiSwayPhysicsID"), SwayFrontRatio[c], SwayFrontRatio[c], SwayFrontRatio[c], low, low + 99);
                    this.target.Execute(ScaleSql("List_AntiSwayPhysics", "SwaybarStiffness", "AntiSwayPhysicsID"), SwayRearRatio[c], SwayRearRatio[c], SwayRearRatio[c], low + 100, low + 199);
                    this.Line("  FM3 handling: anti-roll bars front x {0}, rear x {1}", SwayFrontRatio[c], SwayRearRatio[c]);
                }
                else
                {
                    this.Line("  FM3 handling: anti-roll bars left as they are (class {0}: FM4 ratios known for classes 0-6)", c);
                }
            }
            // Steering: on the 315 cars both games have, FM4 = FM3 x 1.2 on the maximum angle (35 -> 42, 30 -> 36, 25 -> 30; 275 + 23 + 15 cars)
            // and 105 on the three angular velocities where FM3 has 70 (turning) or 140 (straighten, countersteer), on all 315.
            this.target.Execute("UPDATE Data_Car SET SteerMaxAngle = round(SteerMaxAngle * 1.2, 4), SteerMaxAngVelTurning = CASE WHEN SteerMaxAngVelTurning = 70 THEN 105 ELSE SteerMaxAngVelTurning END, "
                + "SteerMaxAngVelStraighten = CASE WHEN SteerMaxAngVelStraighten = 140 THEN 105 ELSE SteerMaxAngVelStraighten END, SteerAngVelCountersteer = CASE WHEN SteerAngVelCountersteer = 140 THEN 105 ELSE SteerAngVelCountersteer END WHERE Id = ?", this.carId);
            // Brakes, STOCK row only (FM4's upgrade rows keep FM3's 1.01 / 1.03 / 1.05 and ABS 1): ABS release point 1 -> 0.75 and braking
            // friction scale 1 -> 1, 1.01 -> 1.0025, 1.03 -> 1.005, 1.05 -> 1.01 (all 315 stock rows).
            this.target.Execute("UPDATE List_UpgradeBrakes SET ReleasePointABSRearFullSteer = CASE WHEN ReleasePointABSRearFullSteer = 1 THEN 0.75 ELSE ReleasePointABSRearFullSteer END, "
                + "GameFrictionScaleBraking = CASE round(GameFrictionScaleBraking, 3) WHEN 1.01 THEN 1.0025 WHEN 1.03 THEN 1.005 WHEN 1.05 THEN 1.01 ELSE GameFrictionScaleBraking END WHERE Ordinal = ? AND IsStock = 1", this.carId);
            // Chassis stiffness friction scales (FM4 Lat = Long per axle): FM4's deficit from 1 is about 0.7 x FM3's on the front, on a 0.005
            // grid (315 stock rows: 218 exact, 280 within 0.005, max 0.02); the rear is half the front's deficit where FM3's rear is 1
            // (225 exact, max 0.0075), else 0.7 x FM3's deficit.
            foreach (object[] row in this.target.Query("SELECT Id, FrontLatFrictionScale, RearLatFrictionScale FROM List_UpgradeCarBodyChassisStiffness WHERE CarbodyId BETWEEN ? AND ?", low, high).Rows)
            {
                double front = Convert.ToDouble(row[1], CultureInfo.InvariantCulture);
                double rear = Convert.ToDouble(row[2], CultureInfo.InvariantCulture);
                double newFront = Math.Round((1 - ((1 - front) * 0.7)) / 0.005) * 0.005;
                double newRear = (rear >= 0.9999) ? 1 - ((1 - newFront) / 2) : Math.Round((1 - ((1 - rear) * 0.7)) / 0.005) * 0.005;
                this.target.Execute("UPDATE List_UpgradeCarBodyChassisStiffness SET FrontLatFrictionScale = ?, FrontLongFrictionScale = ?, RearLatFrictionScale = ?, RearLongFrictionScale = ? WHERE Id = ?",
                    Math.Round(newFront, 4), Math.Round(newFront, 4), Math.Round(newRear, 4), Math.Round(newRear, 4), row[0]);
            }
            // Camber change: FM3 has none (0 / 0) on independent suspensions where FM4 has 0.25 / 0.25 (89 of 89 axles); solid axles stay 0.
            this.target.Execute("UPDATE List_SpringDamperPhysics SET CamberChangeRatioCompression = 0.25, CamberChangeRatioExpansion = 0.25 WHERE SpringDamperPhysicsID BETWEEN ? AND ? AND SuspensionPhysicsTypeID = 0 AND CamberChangeRatioCompression = 0 AND CamberChangeRatioExpansion = 0", low, high);
            this.Line("  FM3 handling: steering angle x 1.2, angular velocities 105; ABS release 0.75, braking friction scale as FM4; camber change 0.25 on independent axles");
        }

        // SET Def = Def * r, Min / Max = * r only where they equal the default (fixed parts), for the columns Def/Min/Max<column>.
        internal static string ScaleSql(string table, string column, string key)
        {
            string d = "Def" + column;
            string n = "Min" + column;
            string x = "Max" + column;
            return "UPDATE " + table + " SET " + d + " = " + d + " * ?, " + n + " = CASE WHEN " + n + " = " + d + " THEN " + n + " * ? ELSE " + n + " END, " + x + " = CASE WHEN " + x + " = " + d + " THEN " + x + " * ? ELSE " + x + " END WHERE " + key + " BETWEEN ? AND ?";
        }

        // FM4 / FM3 damper and anti-roll ratios by FM4 class (medians on the cars both games have).
        internal static readonly double[] DamperBumpRatio = { 1.03, 1.153, 1.153, 1.196, 1.196, 1.235, 1.267, 1.293, 1.315, 1.333 };
        internal static readonly double[] DamperReboundRatio = { 0.773, 0.865, 0.865, 0.897, 0.897, 0.926, 0.95, 0.97, 0.987, 1.0 };
        internal static readonly double[] SwayFrontRatio = { 0.844, 1.044, 1.044, 1.124, 1.124, 1.197, 1.261 };
        internal static readonly double[] SwayRearRatio = { 0.839, 0.851, 0.851, 0.916, 0.916, 0.976, 1.027 };

        // Every value of the rows the port wrote that points at a row FM4 lacks: fixed when the user
        // chose a value for it (Fixes), else listed for the review with FM4's own values to pick from.
        private void ReviewDatabase()
        {
            StringTable names = null;
            try
            {
                names = StringTable.Load(this.TargetRoot, "EN");
            }
            catch (Exception)
            {
            }
            List<string> seen = new List<string>();
            foreach (string[] written in this.writtenRows)
            {
                foreach (GameDbSchema.Relation relation in GameDbSchema.From(written[0]))
                {
                    if (!relation.Strict || relation.IsComposite || !this.TableHas(relation.Table, relation.Column) || !this.TableHas(relation.Target, relation.TargetColumn))
                    {
                        continue;
                    }
                    string column = SqliteDatabase.Quote(relation.Column);
                    string where = "(" + written[1] + ") AND " + GameDatabase.DanglingWhere(relation);
                    foreach (object[] row in this.target.Query("SELECT DISTINCT " + column + " FROM " + SqliteDatabase.Quote(relation.Table) + " WHERE " + where).Rows)
                    {
                        string value = Convert.ToString(row[0], CultureInfo.InvariantCulture);
                        string key = relation.Table + "." + relation.Column + "=" + value;
                        object fix;
                        if (this.Fixes.TryGetValue(key, out fix) && fix != null)
                        {
                            this.target.Execute("UPDATE " + SqliteDatabase.Quote(relation.Table) + " SET " + column + " = ? WHERE (" + written[1] + ") AND " + column + " = ?", fix, row[0]);
                            this.Line("  review fix: {0}.{1} {2} -> {3}", relation.Table, relation.Column, value, Convert.ToString(fix, CultureInfo.InvariantCulture));
                            continue;
                        }
                        if (seen.Contains(key))
                        {
                            continue;
                        }
                        seen.Add(key);
                        Issue issue = new Issue();
                        issue.Key = key;
                        issue.Kind = "database";
                        issue.Where = relation.Table + "." + relation.Column;
                        issue.Value = value;
                        issue.Problem = string.Format("{0} {1} has no row in FM4's {2}", relation.Column, value, relation.Target);
                        string label = this.TableHas(relation.Target, "DisplayName") ? ", DisplayName" : "";
                        // Rows without a name string (Data_Engine) are told apart by their MediaName.
                        bool media = label.Length == 0 && this.TableHas(relation.Target, "MediaName");
                        if (media)
                        {
                            label = ", MediaName";
                        }
                        foreach (object[] choice in this.target.Query("SELECT " + SqliteDatabase.Quote(relation.TargetColumn) + label + " FROM " + SqliteDatabase.Quote(relation.Target) + " ORDER BY 1").Rows)
                        {
                            Choice c = new Choice();
                            c.Value = choice[0];
                            string text = media ? choice[1] as string : (label.Length > 0 && names != null) ? names.Lookup(relation.Target, "DisplayName", choice[1] as string) : null;
                            c.Label = Convert.ToString(choice[0], CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(text) ? "" : "  " + text);
                            issue.Choices.Add(c);
                        }
                        this.Issues.Add(issue);
                        this.Line("  REVIEW {0}", issue.Problem);
                    }
                }
            }
        }

        private bool TableHas(string table, string column)
        {
            foreach (object[] row in this.target.Query("PRAGMA table_info(" + SqliteDatabase.Quote(table) + ")").Rows)
            {
                if (string.Equals((string) row[1], column, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        // The user's material fixes (Fixes "material:name" -> FM4 material) for ConvertModel.
        private Dictionary<string, string> MaterialFixes()
        {
            Dictionary<string, string> renames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object> fix in this.Fixes)
            {
                if (fix.Key.StartsWith("material:", StringComparison.Ordinal) && fix.Value is string)
                {
                    renames[fix.Key.Substring("material:".Length)] = (string) fix.Value;
                }
            }
            return renames;
        }

        // The materials no FM4 car uses that are left after the conversion, for the review.
        private void ReviewMaterials()
        {
            foreach (KeyValuePair<string, List<string>> material in this.unknownMaterials)
            {
                Issue issue = new Issue();
                issue.Key = "material:" + material.Key;
                issue.Kind = "material";
                issue.Where = string.Join(" ", material.Value.ToArray());
                issue.Value = material.Key;
                // Not in the port table: no compatible FM4 material (KO). The best choice is preselected:
                // the name rule, the most similar shader, else what FM4 uses most on that part.
                string basis;
                string best = MaterialSuggestions.BestFor(material.Key, this.MediaName + ".carbin", material.Value, out basis);
                issue.Problem = string.Format("KO: material {0} has no compatible FM4 material (renders black){1}", material.Key, (best != null) ? string.Format("; best choice {0} ({1})", best, basis) : "");
                if (best != null && MaterialSuggestions.For(material.Key, this.MediaName + ".carbin", 2, material.Value).FindIndex(delegate(MaterialSuggestions.Entry e) { return e.Name == best; }) < 0)
                {
                    Choice c = new Choice();
                    c.Value = best;
                    c.Label = best + "   (" + basis + ")";
                    c.Recommended = true;
                    issue.Choices.Add(c);
                }
                // Recommended first (green in the review), then the common FM4 materials, then ~ the unusual ones
                foreach (MaterialSuggestions.Entry entry in MaterialSuggestions.For(material.Key, this.MediaName + ".carbin", 2, material.Value))
                {
                    Choice c = new Choice();
                    c.Value = entry.Name;
                    c.Label = entry.ToString();
                    c.Recommended = entry.Recommended;
                    issue.Choices.Add(c);
                }
                issue.Suggested = null;   // no automatic decision: the user picks (best is only shown)
                this.Issues.Add(issue);
                this.Line("  REVIEW KO {0} ({1}): best choice {2}", material.Key, issue.Where, (best != null) ? best + " (" + basis + ")" : "none");
            }
            if (this.unknownMaterials.Count > 0)
            {
                this.Line("  REVIEW {0} materials no FM4 car uses", this.unknownMaterials.Count);
            }
        }

        // FH2 gives a few road cars (CarTypeID 1) DOT or slick tyres as stock (P1, LaFerrari, Veneno: 8
        // RacingSlick at level 3; Veyron SS, R8 Plus...: 7 DOTCompetition). FM4 keeps those for track
        // cars (Diablo GTR, Viper Competition Coupe, Leon Supercup); its road hypercars (Enzo, F50,
        // MC12, One-77, LFA) have 6 MaxPerformance at level 1 and 7, 8, 9 as upgrades. Such a car gets
        // the Enzo's rows (ids car*1000 + 0, 2, 3, 4).
        private void StockTires()
        {
            object type = this.target.Scalar("SELECT CarTypeID FROM Data_Car WHERE Id = ?", this.carId);
            object stock = this.target.Scalar("SELECT TireCompoundID FROM List_UpgradeTireCompound WHERE Ordinal = ? AND IsStock = 1", this.carId);
            if (type == null || Convert.ToInt64(type) != 1 || stock == null || Convert.ToInt64(stock) < 7)
            {
                return;
            }
            this.target.Execute("DELETE FROM List_UpgradeTireCompound WHERE Ordinal = ?", this.carId);
            long[][] rows = { new long[] { 0, 1, 1, 6, 0 }, new long[] { 2, 2, 0, 7, 5000 }, new long[] { 3, 3, 0, 8, 10000 }, new long[] { 4, 4, 0, 9, 10000 } };
            foreach (long[] row in rows)
            {
                this.target.Execute("INSERT INTO List_UpgradeTireCompound (Id, Ordinal, Level, IsStock, TireCompoundID, ManufacturerID, Price, MassDiff, DragScale, WindInstabilityScale) VALUES (?, ?, ?, ?, ?, 0, ?, 0, 1, 1)",
                    (this.carId * 1000) + row[0], this.carId, row[1], row[2], row[3], row[4]);
            }
            this.Line("  tyres: " + this.game + " stock compound {0} on a road car -> FM4's 6 MaxPerformance stock, 7/8/9 as upgrades", stock);
        }

        // FM4 sells drag tyres (9 DragTire) to 502 of its 503 cars, always the same row: level 4, price
        // 10000, no manufacturer. FM3 has no compound 9, and FH1/FH2 have it but give it to no car, so
        // every ported car gets that row (the next free level if 4 is taken). Port.ini [Tires].
        private void DragTires()
        {
            if (!PortSettings.GetBool("Tires", "DragTires", true)
                || Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM List_UpgradeTireCompound WHERE Ordinal = ?", this.carId)) == 0
                || Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM List_UpgradeTireCompound WHERE Ordinal = ? AND TireCompoundID = 9", this.carId)) > 0)
            {
                return;
            }
            long level = Math.Max(PortSettings.GetLong("Tires", "DragLevel", 4), Convert.ToInt64(this.target.Scalar("SELECT max(Level) FROM List_UpgradeTireCompound WHERE Ordinal = ?", this.carId)) + 1);
            long id = (this.carId * 1000) + level;
            while (Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM List_UpgradeTireCompound WHERE Id = ?", id)) > 0)
            {
                id++;
            }
            this.target.Execute("INSERT INTO List_UpgradeTireCompound (Id, Ordinal, Level, IsStock, TireCompoundID, ManufacturerID, Price, MassDiff, DragScale, WindInstabilityScale) VALUES (?, ?, ?, 0, 9, 0, ?, 0, 1, 1)",
                id, this.carId, level, PortSettings.GetLong("Tires", "DragPrice", 10000));
            this.Line("  tyres: no drag tyres in " + this.game + " -> FM4's 9 DragTire added as upgrade level {0}", level);
        }

        // Engine swaps and drive type chosen in the port window's UPGRADES tab (user 2026-10-20), kept
        // with the review's fixes so the trial, the install and the DLC pack all use them:
        //   swapdel:<EngineID>  a swap of the source left out
        //   swapadd:<EngineID>  an FM4 engine added as a swap (value: price)
        //   drive               AWD / RWD / FWD / none: the conversion instead of Upgrades.ini's rule
        public const string SwapRemoveKey = "swapdel:";
        public const string SwapAddKey = "swapadd:";
        public const string DriveKey = "drive";

        // The source's swaps the user left out: their rows go before the engines are copied.
        private void RemoveSwaps()
        {
            foreach (KeyValuePair<string, object> fix in this.Fixes)
            {
                long engine;
                if (fix.Key.StartsWith(SwapRemoveKey, StringComparison.Ordinal)
                    && long.TryParse(fix.Key.Substring(SwapRemoveKey.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out engine))
                {
                    int removed = this.target.Execute("DELETE FROM List_UpgradeEngine WHERE Ordinal = ? AND EngineID = ? AND Level > 0", this.carId, engine);
                    this.Line("  engine swap {0}: left out (port window){1}", engine, (removed == 0) ? ", not a swap of this car" : "");
                }
            }
        }

        // FM4 engines the user added as swaps: one List_UpgradeEngine row each, at the next level, mass
        // and weight distribution as the game's own swaps (new - stock engine mass; +-0.3 of it over the
        // curb weight for a front / other engine, as ArchiveEditorForm.AddEngineSwap), ManufacturerID =
        // the car's make (FM4's swaps). Their parts and sound are FM4's, already there.
        private void AddSwaps()
        {
            SqliteDatabase.Result stock = this.target.Query("SELECT * FROM List_UpgradeEngine WHERE Ordinal = ? AND Level = 0", this.carId);
            if (stock.Rows.Count == 0)
            {
                return;
            }
            foreach (KeyValuePair<string, object> fix in this.Fixes)
            {
                long engine;
                if (!fix.Key.StartsWith(SwapAddKey, StringComparison.Ordinal)
                    || !long.TryParse(fix.Key.Substring(SwapAddKey.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out engine))
                {
                    continue;
                }
                object name = this.target.Scalar("SELECT EngineName FROM Data_Engine WHERE EngineID = ?", engine);
                if (name == null)
                {
                    this.Line("  engine swap {0}: not an FM4 engine, not added", engine);
                    continue;
                }
                if (Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM List_UpgradeEngine WHERE Ordinal = ? AND EngineID = ?", this.carId, engine)) > 0)
                {
                    this.Line("  engine swap {0} ({1}): the car has it already", engine, name);
                    continue;
                }
                long level = Convert.ToInt64(this.target.Scalar("SELECT max(Level) FROM List_UpgradeEngine WHERE Ordinal = ?", this.carId)) + 1;
                long id = (this.carId * 1000) + level;
                while (Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM List_UpgradeEngine WHERE Id = ?", id)) > 0)
                {
                    id++;
                }
                object[] row = (object[]) stock.Rows[0].Clone();
                double stockMass = ToDouble(this.target.Scalar("SELECT [EngineMass-kg] FROM Data_Engine WHERE EngineID = ?", row[Array.FindIndex(stock.Columns, delegate(string c) { return string.Equals(c, "EngineID", StringComparison.OrdinalIgnoreCase); })]));
                double newMass = ToDouble(this.target.Scalar("SELECT [EngineMass-kg] FROM Data_Engine WHERE EngineID = ?", engine));
                double curb = ToDouble(this.target.Scalar("SELECT CurbWeight FROM Data_Car WHERE Id = ?", this.carId));
                object placement = this.target.Scalar("SELECT EnginePlacementID FROM Data_Car WHERE Id = ?", this.carId);
                double massDiff = Math.Round(newMass - stockMass, 3);
                double side = (placement != null && Convert.ToInt64(placement) == 1) ? 0.3 : -0.3;
                object make = this.target.Scalar("SELECT MakeID FROM Data_Car WHERE Id = ?", this.carId);
                long price = (fix.Value is long) ? (long) fix.Value : 6800;
                for (int i = 0; i < stock.Columns.Length; i++)
                {
                    switch (stock.Columns[i].ToLowerInvariant())
                    {
                        case "id": row[i] = id; break;
                        case "level": row[i] = level; break;
                        case "engineid": row[i] = engine; break;
                        case "isstock": row[i] = 0L; break;
                        case "price": row[i] = price; break;
                        case "massdiff": row[i] = massDiff; break;
                        case "weightdistdiff": row[i] = (curb > 0) ? Math.Round(massDiff * side / (curb * 100), 6) : 0.0; break;
                        case "manufacturerid": row[i] = (make != null) ? Convert.ToInt64(make) : 0L; break;
                    }
                }
                string[] marks = new string[stock.Columns.Length];
                string[] quoted = new string[stock.Columns.Length];
                for (int i = 0; i < marks.Length; i++)
                {
                    marks[i] = "?";
                    quoted[i] = SqliteDatabase.Quote(stock.Columns[i]);
                }
                this.target.Execute("INSERT INTO List_UpgradeEngine (" + string.Join(", ", quoted) + ") VALUES (" + string.Join(", ", marks) + ")", row);
                this.Line("  engine swap {0} ({1}): added (port window), level {2}, mass {3:+0.#;-0.#;0} kg, {4} Cr", engine, name, level, massDiff, price);
            }
        }

        private static double ToDouble(object value)
        {
            return (value == null || value is string) ? 0 : Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        // Street / Sport / Race levels for the parts that have only the stock one, and the drive type
        // conversion, by the rules of Upgrades.ini (CarUpgrades, ROADMAP 14.1f). By default only for
        // traffic cars, by the car list's quick test (the close-up rebuild that sets this.traffic runs
        // after the database).
        private void GenericUpgrades()
        {
            List<string> notes = new List<string>();
            CarUpgrades upgrades = new CarUpgrades(this.target, this.carId, CarUpgrades.LoadIni(notes));
            foreach (string note in notes)
            {
                this.Line("{0}", note);
            }
            string sourcePath = GamePaths.FindCar(this.SourceRoot, this.MediaName);
            bool traffic = sourcePath != null && sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && HasPlaceholderCloseUp(sourcePath, this.MediaName);
            object drive;
            if (this.Fixes.TryGetValue(DriveKey, out drive) && drive is string)
            {
                upgrades.DriveOverride = (string) drive;
            }
            if (!upgrades.AppliesTo(traffic))
            {
                this.Line("upgrades: none generated ({0}: Cars rule, {1})", CarUpgrades.FileName, traffic ? "traffic car" : "not a traffic car");
                if (upgrades.DriveOverride != null)
                {
                    upgrades.ApplyDrivetrainOnly();
                }
                else
                {
                    upgrades.RemoveOldConversions();
                }
            }
            else
            {
                upgrades.Apply();
            }
            foreach (string line in upgrades.Lines)
            {
                this.Line("{0}", line);
            }
        }

        // Every "...ID" value in the rows of the car (and of its engine and drivetrain) that names
        // the key of a List_/Data_ table (first column of that name) must exist in FM4: the missing
        // rows come from FH2, and their own references after them (the P1's bumpers and wing point
        // at List_AeroPhysics rows FM4 does not have).
        private void CopyReferences(Dictionary<string, List<string>> from, Dictionary<string, List<string>> to)
        {
            // Reference column -> table: the key column's own name (AeroPhysicsID -> List_AeroPhysics),
            // or for a key named just Id, the table's name + ID (CarBodyID -> Data_CarBody: wheelbase,
            // tracks, bounding box; without it the physics runs on zeros and goes NaN).
            Dictionary<string, string> keyed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> keyColumn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<string>> table in to)
            {
                List<string> sourceColumns;
                if (table.Value.Count == 0 || !(table.Key.StartsWith("List_") || table.Key.StartsWith("Data_")) || !from.TryGetValue(table.Key, out sourceColumns)
                    || sourceColumns.Count == 0 || !string.Equals(sourceColumns[0], table.Value[0], StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string reference = null;
                if (string.Equals(table.Value[0], "Id", StringComparison.OrdinalIgnoreCase))
                {
                    reference = table.Key.Substring(5) + "ID";
                }
                else if (table.Value[0].Length > 2 && table.Value[0].EndsWith("ID", StringComparison.OrdinalIgnoreCase))
                {
                    reference = table.Value[0];
                }
                if (reference != null && !keyed.ContainsKey(reference))
                {
                    keyed[reference] = table.Key;
                    keyColumn[reference] = table.Value[0];
                }
            }
            // Rows to scan: the car's, then whatever gets copied.
            List<string[]> pending = new List<string[]>();
            foreach (KeyValuePair<string, List<string>> table in to)
            {
                foreach (string column in new string[] { "Ordinal", "CarId", "CarID" })
                {
                    if (Has(table.Value, column))
                    {
                        pending.Add(new string[] { table.Key, SqliteDatabase.Quote(column) + " = " + this.carId });
                        break;
                    }
                }
                if (table.Key.StartsWith("List_UpgradeCarBody", StringComparison.Ordinal) && table.Value.Count > 0 && table.Value[0] == "Id")
                {
                    pending.Add(new string[] { table.Key, "Id / 1000 = " + this.carId });
                }
                if (table.Key == "Data_Car")
                {
                    pending.Add(new string[] { table.Key, "Id = " + this.carId });
                }
            }
            foreach (string column in new string[] { "EngineID", "DrivetrainID" })
            {
                foreach (KeyValuePair<string, List<string>> table in to)
                {
                    if (Has(table.Value, column))
                    {
                        pending.Add(new string[] { table.Key, SqliteDatabase.Quote(column) + " IN (SELECT " + column + " FROM " + ((column == "EngineID") ? "List_UpgradeEngine" : "List_UpgradeDrivetrain") + " WHERE Ordinal = " + this.carId + ")" });
                    }
                }
            }
            int copied = 0;
            for (int i = 0; i < pending.Count && i < 5000; i++)
            {
                SqliteDatabase.Result rows = this.target.Query("SELECT * FROM " + SqliteDatabase.Quote(pending[i][0]) + " WHERE " + pending[i][1]);
                for (int c = 0; c < rows.Columns.Length; c++)
                {
                    string column = rows.Columns[c];
                    string table;
                    if (!keyed.TryGetValue(column, out table) || table == pending[i][0])
                    {
                        continue;
                    }
                    foreach (object[] row in rows.Rows)
                    {
                        if (row[c] == null || !(row[c] is long) || (long) row[c] < 0)
                        {
                            continue;
                        }
                        string where = SqliteDatabase.Quote(keyColumn[column]) + " = " + (long) row[c];
                        if (Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM " + SqliteDatabase.Quote(table) + " WHERE " + where)) > 0)
                        {
                            continue;
                        }
                        int added = this.CopyRows(table, from[table], to[table], where);
                        if (added > 0)
                        {
                            copied += added;
                            this.Line("  {0}: {1} rows ({2}, referenced by {3})", table, added, where.Replace("[", "").Replace("]", ""), pending[i][0]);
                            pending.Add(new string[] { table, where });
                        }
                    }
                }
            }
            this.Line("references: {0} more rows copied", copied);
        }

        // Column names are compared as SQLite does, ignoring case (List_UpgradeDrivetrainDriveline has DrivetrainId).
        private static bool Has(List<string> columns, string name)
        {
            return columns.Exists(delegate(string c) { return string.Equals(c, name, StringComparison.OrdinalIgnoreCase); });
        }

        private List<long> Ids(string sql)
        {
            List<long> ids = new List<long>();
            foreach (object[] row in this.source.Query(sql, this.carId).Rows)
            {
                if (row[0] != null)
                {
                    ids.Add(Convert.ToInt64(row[0]));
                }
            }
            return ids;
        }

        // Rows whose 'column' is one of the ids, in every table having that column (or only in
        // 'onlyTable'), when FM4 does not have them yet.
        private void CopyById(Dictionary<string, List<string>> from, Dictionary<string, List<string>> to, string column, List<long> ids, string skipTable, params string[] onlyTable)
        {
            if (ids.Count == 0)
            {
                return;
            }
            List<string> list = new List<string>();
            foreach (long id in ids)
            {
                list.Add(id.ToString(CultureInfo.InvariantCulture));
            }
            string where = SqliteDatabase.Quote(column) + " IN (" + string.Join(",", list.ToArray()) + ")";
            foreach (KeyValuePair<string, List<string>> table in to)
            {
                List<string> sourceColumns;
                if (table.Key == skipTable || !from.TryGetValue(table.Key, out sourceColumns) || !Has(table.Value, column) || !Has(sourceColumns, column))
                {
                    continue;
                }
                if (onlyTable.Length > 0 && Array.IndexOf(onlyTable, table.Key) < 0)
                {
                    continue;
                }
                int copied = this.CopyRows(table.Key, sourceColumns, table.Value, where);
                if (copied > 0)
                {
                    this.Line("  {0}: {1} rows ({2} {3})", table.Key, copied, column, string.Join(",", list.ToArray()));
                }
            }
        }

        private void CopyTorqueCurves(Dictionary<string, List<string>> from, Dictionary<string, List<string>> to, List<long> engines)
        {
            List<long> curves = new List<long>();
            foreach (KeyValuePair<string, List<string>> table in from)
            {
                if (!table.Value.Contains("EngineID") || engines.Count == 0)
                {
                    continue;
                }
                foreach (string column in table.Value)
                {
                    if (column.IndexOf("TorqueCurve", StringComparison.OrdinalIgnoreCase) < 0 || !column.EndsWith("ID", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    foreach (long engine in engines)
                    {
                        foreach (object[] row in this.source.Query("SELECT " + SqliteDatabase.Quote(column) + " FROM " + SqliteDatabase.Quote(table.Key) + " WHERE EngineID = ?", engine).Rows)
                        {
                            if (row[0] != null && !curves.Contains(Convert.ToInt64(row[0])))
                            {
                                curves.Add(Convert.ToInt64(row[0]));
                            }
                        }
                    }
                }
            }
            this.CopyById(from, to, "TorqueCurveID", curves, null, "List_TorqueCurve");
        }

        // Copies the rows matching 'where' (common columns); rows already there are replaced.
        // A row whose primary key FM4 already uses for another row: a key nothing refers to (a plain
        // counter, Combo_Colors_Exceptions.ID: FH2's 670 is car 2016's, FM4's car 315's) gets the next
        // free value; a key other tables refer to stops the port before anything is written.
        private int CopyRows(string table, List<string> sourceColumns, List<string> targetColumns, string where)
        {
            List<string> columns = new List<string>();
            foreach (string column in targetColumns)
            {
                if (Has(sourceColumns, column))
                {
                    columns.Add(column);
                }
            }
            if (columns.Count == 0)
            {
                return 0;
            }
            List<string> quoted = columns.ConvertAll<string>(delegate(string c) { return SqliteDatabase.Quote(c); });
            SqliteDatabase.Result rows = this.source.Query("SELECT " + string.Join(",", quoted.ToArray()) + " FROM " + SqliteDatabase.Quote(table) + " WHERE " + where);
            // The target's rows go even when the source has none: a port over a car installed before
            // must not keep rows that install generated (aspiration conversions, a drive type swap...;
            // user 2026-10-20: reinstall did not take the new choices).
            this.target.Execute("DELETE FROM " + SqliteDatabase.Quote(table) + " WHERE " + where);
            if (rows.Rows.Count == 0)
            {
                return 0;
            }
            this.writtenRows.Add(new string[] { table, where });
            string marks = string.Join(",", columns.ConvertAll<string>(delegate(string c) { return "?"; }).ToArray());
            string insert = "INSERT INTO " + SqliteDatabase.Quote(table) + " (" + string.Join(",", quoted.ToArray()) + ") VALUES (" + marks + ")";
            List<int> key = new List<int>();
            foreach (string column in this.PrimaryKey(table))
            {
                int index = columns.FindIndex(delegate(string c) { return string.Equals(c, column, StringComparison.OrdinalIgnoreCase); });
                if (index < 0)
                {
                    key.Clear();
                    break;
                }
                key.Add(index);
            }
            string keyWhere = string.Join(" AND ", key.ConvertAll<string>(delegate(int i) { return quoted[i] + " = ?"; }).ToArray());
            int renumbered = 0;
            foreach (object[] row in rows.Rows)
            {
                if (key.Count > 0)
                {
                    object[] values = key.ConvertAll<object>(delegate(int i) { return row[i]; }).ToArray();
                    if (Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM " + SqliteDatabase.Quote(table) + " WHERE " + keyWhere, values)) > 0)
                    {
                        if (key.Count != 1 || !(row[key[0]] is long) || this.IsReferenced(table, columns[key[0]]))
                        {
                            throw new InvalidOperationException(string.Format("{0}: the key {1} = {2} of the " + this.game + " row is another row's in FM4, and other tables refer to it.",
                                table, string.Join(",", key.ConvertAll<string>(delegate(int i) { return columns[i]; }).ToArray()),
                                string.Join(",", Array.ConvertAll<object, string>(values, delegate(object v) { return Convert.ToString(v, CultureInfo.InvariantCulture); }))));
                        }
                        row[key[0]] = DbIds.Next(this.target, table, columns[key[0]]);
                        renumbered++;
                    }
                }
                this.target.Execute(insert, row);
            }
            if (renumbered > 0)
            {
                this.Line("  {0}: {1} of {2} rows got a new {3} (FM4 uses theirs)", table, renumbered, rows.Rows.Count, columns[key[0]]);
            }
            return rows.Rows.Count;
        }

        private readonly Dictionary<string, List<string>> primaryKeys = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // The target table's primary key columns, in key order (PRAGMA table_info pk).
        private List<string> PrimaryKey(string table)
        {
            List<string> key;
            if (!this.primaryKeys.TryGetValue(table, out key))
            {
                SortedList<long, string> ordered = new SortedList<long, string>();
                foreach (object[] column in this.target.Query("PRAGMA table_info(" + SqliteDatabase.Quote(table) + ")").Rows)
                {
                    if (Convert.ToInt64(column[5]) > 0)
                    {
                        ordered.Add(Convert.ToInt64(column[5]), (string) column[1]);
                    }
                }
                key = new List<string>(ordered.Values);
                this.primaryKeys[table] = key;
            }
            return key;
        }

        // Whether another FM4 table has a column naming this key, as CopyReferences reads them:
        // the key's own name (AeroPhysicsID), or for a key called Id/ID the table's name without
        // its prefix + ID (Data_CarBody -> CarBodyID).
        private bool IsReferenced(string table, string keyColumn)
        {
            List<string> names = new List<string>();
            if (!string.Equals(keyColumn, "Id", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(keyColumn);
            }
            int prefix = table.IndexOf('_');
            names.Add(((prefix >= 0) ? table.Substring(prefix + 1) : table) + "ID");
            foreach (object[] other in this.target.Query("SELECT name FROM sqlite_master WHERE type = 'table'").Rows)
            {
                if (string.Equals((string) other[0], table, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                foreach (object[] column in this.target.Query("PRAGMA table_info(" + SqliteDatabase.Quote((string) other[0]) + ")").Rows)
                {
                    foreach (string name in names)
                    {
                        if (string.Equals((string) column[1], name, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        // Data_Car: the FH2 row in FM4's columns; PI on the FM4 scale (MapPerformanceIndex), class
        // from FM4's PI ranges, base-game content.
        private void PortCarRow(List<string> sourceColumns, List<string> targetColumns)
        {
            this.CopyRows("Data_Car", sourceColumns, targetColumns, "Id = " + this.carId);
            double pi = Convert.ToDouble(this.source.Scalar("SELECT PerformanceIndex FROM Data_Car WHERE Id = ?", this.carId), CultureInfo.InvariantCulture);
            if (this.fm2Pi >= 0)
            {
                pi = this.fm2Pi;   // FM2 stage: the stage database is the donor's, the PI is the FM2 car's
            }
            double fm4Pi;
            if (this.fm2Pi >= 0 && this.fm2PiPairs != null)
            {
                fm4Pi = MapPerformanceIndex(this.fm2PiPairs, pi);
            }
            else if (!DlcSource.IsStage(this.SourceRoot) || !FixedPerformanceIndex(GameInfo.Detect(this.SourceRoot), pi, out fm4Pi))
            {
                fm4Pi = MapPerformanceIndex(PerformanceIndexPairs(this.source, this.target, this.carId, this.SourceRoot, this.TargetRoot, this.OutputRoot), pi);
            }
            object classId = ClassFor(this.target, fm4Pi, this.carId);
            this.target.Execute("UPDATE Data_Car SET PerformanceIndex = ?, ClassID = ?, ContentId = 0, IsArcade = ?, IsPurchased = 1, IsInstalled = 1 WHERE Id = ?",
                fm4Pi, (classId != null) ? Convert.ToInt64(classId) : 9L, this.Arcade ? 1L : 0L, this.carId);
            this.Line("Data_Car {0}: PI {1:0.000} (" + this.game + ") -> {2:0.000} (FM4), class {3}, IsArcade {4}", this.carId, pi, fm4Pi, ClassName(this.target, classId), this.Arcade ? 1 : 0);
            if (this.fm2Pi < 0)
            {
                this.PortRatings(targetColumns);
            }
        }

        private static readonly string[] RatingColumns = { "SpeedRating", "HandlingRating", "AccelerationRating", "LaunchRating", "BrakingRating" };

        // The five bars of the garage on FM4's scale. The source game's bars are on its own (FH2's speed, acceleration and launch run
        // 0.5 - 0.75 below FM4's on the 140 cars both games have, rmse 1.1 / 0.8 / 0.7 against the car's real FM4 bars), so each
        // bar goes through a line fit on the cars the source and FM4 share (same MediaName), as the PI does: leave-one-out error
        // 0.35 / 0.25 / 0.18 / 0.38 / 0.24 on those cars. Without enough shared cars (a DLC package alone) the bars stay the source's.
        private void PortRatings(List<string> targetColumns)
        {
            foreach (string column in RatingColumns)
            {
                if (!Has(targetColumns, column))
                {
                    return;
                }
            }
            string select = "SELECT MediaName, " + string.Join(", ", RatingColumns);
            Dictionary<string, double[]> horizon = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
            foreach (object[] row in this.source.Query(select + " FROM Data_Car").Rows)
            {
                if (row[0] is string)
                {
                    double[] bars = new double[5];
                    bool all = true;
                    for (int i = 0; i < 5; i++)
                    {
                        all = all && row[1 + i] != null && row[1 + i] != DBNull.Value;
                        bars[i] = (row[1 + i] == null || row[1 + i] == DBNull.Value) ? 0 : Convert.ToDouble(row[1 + i], CultureInfo.InvariantCulture);
                    }
                    if (all)
                    {
                        horizon[(string) row[0]] = bars;
                    }
                }
            }
            List<double[]> fromBars = new List<double[]>();
            List<double[]> toBars = new List<double[]>();
            foreach (object[] row in this.target.Query("SELECT Id, " + string.Join(", ", RatingColumns) + ", MediaName FROM Data_Car").Rows)
            {
                double[] bars;
                string media = row[6] as string;
                if (media != null && Convert.ToInt64(row[0], CultureInfo.InvariantCulture) != this.carId && horizon.TryGetValue(media, out bars)
                    && !IsPorted(this.SourceRoot, this.TargetRoot, media) && (this.OutputRoot == null || !IsPorted(this.SourceRoot, this.OutputRoot, media)))
                {
                    double[] mine = new double[5];
                    bool all = true;
                    for (int i = 0; i < 5; i++)
                    {
                        all = all && row[1 + i] != null && row[1 + i] != DBNull.Value;
                        mine[i] = (row[1 + i] == null || row[1 + i] == DBNull.Value) ? 0 : Convert.ToDouble(row[1 + i], CultureInfo.InvariantCulture);
                    }
                    if (all)
                    {
                        fromBars.Add(bars);
                        toBars.Add(mine);
                    }
                }
            }
            double[] own;
            if (fromBars.Count < 30 || !horizon.TryGetValue(Convert.ToString(this.source.Scalar("SELECT MediaName FROM Data_Car WHERE Id = ?", this.carId), CultureInfo.InvariantCulture), out own))
            {
                this.Line("ratings: kept as the source's ({0} cars shared with FM4, 30 needed to fit the lines)", fromBars.Count);
                return;
            }
            List<string> parts = new List<string>();
            List<object> args = new List<object>();
            List<string> sets = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                double mx = 0, my = 0;
                for (int k = 0; k < fromBars.Count; k++)
                {
                    mx += fromBars[k][i] / fromBars.Count;
                    my += toBars[k][i] / fromBars.Count;
                }
                double sxx = 0, sxy = 0;
                for (int k = 0; k < fromBars.Count; k++)
                {
                    sxx += (fromBars[k][i] - mx) * (fromBars[k][i] - mx);
                    sxy += (fromBars[k][i] - mx) * (toBars[k][i] - my);
                }
                double slope = (sxx > 1e-9) ? sxy / sxx : 1.0;
                double mapped = Math.Max(0, Math.Min(10, my + (slope * (own[i] - mx))));
                sets.Add(SqliteDatabase.Quote(RatingColumns[i]) + " = ?");
                args.Add(mapped);
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1:0.0} -> {2:0.0}", RatingColumns[i].Replace("Rating", ""), own[i], mapped));
            }
            args.Add(this.carId);
            this.target.Execute("UPDATE Data_Car SET " + string.Join(", ", sets.ToArray()) + " WHERE Id = ?", args.ToArray());
            this.Line("ratings ({0} -> FM4 lines on {1} shared cars): {2}", this.game, fromBars.Count, string.Join(", ", parts.ToArray()));
        }

        // (FH2 PI, FM4 PI) of the cars both games have (same MediaName), sorted; the car being
        // ported and the cars ported earlier (their FM4 PI came from this mapping) left out.
        public static List<double[]> PerformanceIndexPairs(SqliteDatabase source, SqliteDatabase target, long exclude, string sourceRoot, string targetRoot)
        {
            return PerformanceIndexPairs(source, target, exclude, sourceRoot, targetRoot, null);
        }

        // outputRoot: a trial folder whose cars (earlier cars of the same DLC pack) are ported ones too.
        public static List<double[]> PerformanceIndexPairs(SqliteDatabase source, SqliteDatabase target, long exclude, string sourceRoot, string targetRoot, string outputRoot)
        {
            Dictionary<string, double> horizon = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (object[] row in source.Query("SELECT MediaName, PerformanceIndex FROM Data_Car").Rows)
            {
                if (row[0] is string && row[1] != null)
                {
                    horizon[(string) row[0]] = Convert.ToDouble(row[1], CultureInfo.InvariantCulture);
                }
            }
            List<double[]> pairs = new List<double[]>();
            foreach (object[] row in target.Query("SELECT Id, MediaName, PerformanceIndex FROM Data_Car").Rows)
            {
                double pi;
                if (Convert.ToInt64(row[0]) != exclude && row[1] is string && row[2] != null && horizon.TryGetValue((string) row[1], out pi)
                    && !IsPorted(sourceRoot, targetRoot, (string) row[1]) && (outputRoot == null || !IsPorted(sourceRoot, outputRoot, (string) row[1])))
                {
                    pairs.Add(new double[] { pi, Convert.ToDouble(row[2], CultureInfo.InvariantCulture) });
                }
            }
            pairs.Sort(delegate(double[] a, double[] b) { return a[0].CompareTo(b[0]); });
            return pairs;
        }

        // FM4 PI for a car of a DLC package ported without its base game (DlcSource): the base game's
        // cars, which MapPerformanceIndex pairs with FM4's, are not there. One line per source game, fit
        // on all the cars it shares with FM4: FH1 (2026-10-17) FM4 = 1.189 FH1 - 0.363, leave-one-out
        // error 0.0145 (the local pairs 0.0130). FM3 (2026-10-18, 315 shared cars, fm4dev\Fm3DLC\tools\
        // pifit): FM4 = 0.7779 FM3 + 0.1668, error 0.0191 (local 0.0184), right FM4 class on 264 of 315.
        // Keeping FM3's displayed PI instead (both games interpolate it in their CarClasses bands, which
        // differ) gets the class right on only 105: FM4 rated FM3's B 470-500 cars A 503-559. Without a
        // line (FM3 DLC before this) the FH2 fallback put the HKS Genesis (FM3 B 497) in F.
        public static bool FixedPerformanceIndex(ForzaGame game, double pi, out double fm4Pi)
        {
            // The lines are in Port.ini [PI] (these defaults).
            fm4Pi = 0;
            if (game == ForzaGame.Horizon1)
            {
                fm4Pi = Math.Max(0.001, Math.Min(0.999, (PortSettings.GetDouble("PI", "FH1.Slope", 1.189) * pi) + PortSettings.GetDouble("PI", "FH1.Offset", -0.363)));
                return true;
            }
            if (game == ForzaGame.Motorsport3)
            {
                fm4Pi = Math.Max(0.001, Math.Min(0.999, (PortSettings.GetDouble("PI", "FM3.Slope", 0.7779) * pi) + PortSettings.GetDouble("PI", "FM3.Offset", 0.1668)));
                return true;
            }
            return false;
        }

        // FM4 PI for an FH2 PI: a line through the 15 shared cars nearest in FH2 PI. On the 138 cars
        // both games have (leave one out) the error is 0.013, against 0.027 for the single line
        // FM4 = 1.025 FH2 - 0.261 fit on PI > 0.7, which puts slow cars a class too high
        // (Ford Ka 0.331: 0.078 for 0.001). Outside the shared cars' range (above the MC12's 0.851,
        // where FH2's hypercars are) it goes on from the edge with that line's slope: the edge fit
        // is steeper and would push road cars into R1.
        public static double MapPerformanceIndex(List<double[]> pairs, double pi)
        {
            const int Near = 15;
            const double Slope = 1.025;
            if (pairs.Count < Near)
            {
                return Math.Max(0.001, Math.Min(0.999, (Slope * pi) - 0.261));
            }
            double inside = Math.Max(pairs[0][0], Math.Min(pairs[pairs.Count - 1][0], pi));
            List<double[]> near = new List<double[]>(pairs);
            near.Sort(delegate(double[] a, double[] b) { return Math.Abs(a[0] - inside).CompareTo(Math.Abs(b[0] - inside)); });
            near.RemoveRange(Near, near.Count - Near);
            double mx = 0;
            double my = 0;
            foreach (double[] p in near)
            {
                mx += p[0] / Near;
                my += p[1] / Near;
            }
            double sxx = 0;
            double sxy = 0;
            foreach (double[] p in near)
            {
                sxx += (p[0] - mx) * (p[0] - mx);
                sxy += (p[0] - mx) * (p[1] - my);
            }
            double slope = (sxx > 1e-12) ? sxy / sxx : Slope;
            return Math.Max(0.001, Math.Min(0.999, my + (slope * (inside - mx)) + (Slope * (pi - inside))));
        }

        // The FM4 class of a PI: the first in CarClasses whose MaxPerformanceIndex reaches it (F 0.157,
        // E 0.2224 ... R1 0.938, U 1); without that table, the class of the car with the highest PI
        // not above it.
        public static object ClassFor(SqliteDatabase target, double pi, long exclude)
        {
            if (Convert.ToInt64(target.Scalar("SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'CarClasses'")) == 1)
            {
                object id = target.Scalar("SELECT Id FROM CarClasses WHERE MaxPerformanceIndex >= ? ORDER BY MaxPerformanceIndex LIMIT 1", pi);
                if (id != null)
                {
                    return id;
                }
            }
            return target.Scalar("SELECT ClassID FROM Data_Car WHERE Id <> ? AND PerformanceIndex <= ? GROUP BY ClassID ORDER BY max(PerformanceIndex) DESC LIMIT 1", exclude, pi);
        }

        // "R2" for class 8 (CarClasses.BadgeTexturePathPrefix CLASS_R2), the number otherwise.
        public static string ClassName(SqliteDatabase target, object classId)
        {
            if (classId == null)
            {
                return "?";
            }
            try
            {
                string prefix = target.Scalar("SELECT BadgeTexturePathPrefix FROM CarClasses WHERE Id = ?", classId) as string;
                if (prefix != null && prefix.StartsWith("CLASS_", StringComparison.OrdinalIgnoreCase))
                {
                    return prefix.Substring(6);
                }
            }
            catch (InvalidOperationException)
            {
            }
            return Convert.ToString(classId, CultureInfo.InvariantCulture);
        }

        // True when the target's car zip is the source's, ported earlier: both carry the same
        // buildnumber.txt (FM4's own cars have Turn 10 builds, e.g. 452838.FER_F40_87.RTMGSLTURNTEN27
        // against FH2's 214061.FER_F40_87.PG-LEAM-BS09).
        public static bool IsPorted(string sourceRoot, string targetRoot, string mediaName)
        {
            try
            {
                string from = Path.Combine(Path.Combine(Path.Combine(sourceRoot, "Media"), "cars"), mediaName + ".zip");
                string to = Path.Combine(Path.Combine(Path.Combine(targetRoot, "Media"), "cars"), mediaName + ".zip");
                // A DLC package's car whose zip is not written yet: the build number is in the stage's list.
                KeyValuePair<bool, string> listed;
                bool inList = !File.Exists(from) && DlcSource.LazyCars(sourceRoot).TryGetValue(mediaName, out listed);
                if ((!File.Exists(from) && !inList) || !File.Exists(to))
                {
                    return false;
                }
                string sourceBuild;
                if (inList)
                {
                    sourceBuild = DlcSource.LazyCars(sourceRoot)[mediaName].Value;
                }
                else
                {
                    ForzaZip a = new ForzaZip(from);
                    ForzaZip.Entry x = a.Find("buildnumber.txt");
                    sourceBuild = (x == null) ? "" : Encoding.ASCII.GetString(a.Read(x)).Trim();
                }
                return IsPortedBuild(targetRoot, mediaName, sourceBuild);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // The same with the source car's build number already known (DlcCatalog: the package is not opened).
        public static bool IsPortedBuild(string targetRoot, string mediaName, string sourceBuild)
        {
            try
            {
                string to = Path.Combine(Path.Combine(Path.Combine(targetRoot, "Media"), "cars"), mediaName + ".zip");
                if (!File.Exists(to))
                {
                    return false;
                }
                ForzaZip b = new ForzaZip(to);
                ForzaZip.Entry y = b.Find("buildnumber.txt");
                if ((sourceBuild ?? "").Length == 0)
                {
                    // FM3 car zips have no build number: a ported car carries the port's log.
                    return b.Find("port_log.txt") != null;
                }
                return y != null && sourceBuild == Encoding.ASCII.GetString(b.Read(y)).Trim();
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static long ToLong(object value, string error)
        {
            if (value == null)
            {
                throw new InvalidOperationException(error);
            }
            return Convert.ToInt64(value);
        }

        #endregion
    }
}
