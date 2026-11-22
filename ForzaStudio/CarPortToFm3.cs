namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;

    // Brings a car from Forza Motorsport 4 into Forza Motorsport 3 (docs/PORT_FM4_TO_FM3.md). Everything is written under
    // OutputRoot (a copy of the FM3 folder's Media layout: the car and wheel zips, gamedb.slt, the string tables, zipmanifest.xml),
    // never into the FM3 folder itself; copying that tree over a FM3 DEV folder is the install.
    //  - model     CarbinModel.ConvertMotorsport4ToMotorsport3; brakes lose the _lod0 suffix; textures through Fm3Textures;
    //  - database  the car's rows (shared columns from FM4, the others from a FM3 donor car's row of the table), Data_Car on
    //              the donor's row (GaugeID, FM3-only columns), PI mapped to the FM3 scale; engines, drivetrains and wheels FM3
    //              already has (same Id and MediaName) are FM3's own, an FM4-only engine or drivetrain becomes the donor's;
    //  - strings   DisplayName and ModelShort in FM3's Data_Car.str tables (new key above the table's largest).
    public class CarPortToFm3
    {
        public string SourceRoot;       // FM4 game folder
        public string PackRoot;         // optional: a staged FM4 DLC pack (DlcSource.Stage + Materialize) whose rows, files and strings come first, the FM4 folder filling in the rest
        public string TargetRoot;       // FM3 game folder (read: database, strings, donor files)
        public string OutputRoot;       // where the result goes (required)
        public string MediaName;        // the FM4 car
        public string DonorMediaName;   // a FM3 car whose rows and zip headers are the templates (default: the same car in FM3, else the first FM3 car with a zip)
        public string SoundDonorMediaName;   // an FM3 car whose engine and model tuning files the car takes (default: the same car in FM3, else the donor)
        public string RenameTo;         // the car's name in FM3 (default: MediaName, with _FM4 added when FM3 has a car of that name)
        public bool AllowFm4OnlyDash;   // port a dash that uses properties no FM3 dash has (default: such a dash is left out, GaugeID -1)
        // The window's choices, as the FM4 window's (user 2026-11-22: FM3's car tables are FM4's):
        public bool Arcade = true;      // Data_Car.IsArcade (before: the template's value)
        public long? MakeId;            // an FM3 make (List_CarMake.ID, also one just created in OutputRoot) instead of the car's own
        // (No brake choice here: the generic brakes and their angles are written by the FM4-form step with the model, CarPort.Brakes;
        // an FM4 car keeps its own brakes and angles.)
        public readonly StringBuilder Report = new StringBuilder();

        private SqliteDatabase source;
        private SqliteDatabase target;
        private long carId;
        private long donorId;
        private bool inFm3;   // FM3 already has a car of this MediaName (read before the port writes its rows)
        private string carName;
        private string donorZip;
        private readonly List<string> newZips = new List<string>();
        private readonly List<string> changedZips = new List<string>();
        private readonly List<long> wheelIds = new List<long>();
        private readonly Dictionary<string, HashSet<long>> takenElsewhere = new Dictionary<string, HashSet<long>>(StringComparer.OrdinalIgnoreCase);   // owner table -> ids of the other databases (OtherDatabases)

        // An id of owner (Data_Engine / Data_Drivetrain / List_Wheels) used in another database the car will meet: the other FM3 folder
        // (DEV / clean) or an FM3 package in Xenia.
        private bool TakenElsewhere(string owner, long id)
        {
            HashSet<long> ids;
            return this.takenElsewhere.TryGetValue(owner, out ids) && ids.Contains(id);
        }

        // The next id of owner free in the target and in every other database.
        private long FreeId(string owner, string key)
        {
            long fresh = DbIds.Next(this.target, owner, key, ForzaGame.Motorsport3);
            while (this.TakenElsewhere(owner, fresh) || Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM " + SqliteDatabase.Quote(owner) + " WHERE " + SqliteDatabase.Quote(key) + " = ?", fresh), CultureInfo.InvariantCulture) > 0)
            {
                fresh++;
            }
            return fresh;
        }

        public void Run()
        {
            if (string.IsNullOrEmpty(this.OutputRoot))
            {
                throw new InvalidOperationException("OutputRoot is required: the FM3 folder is not written.");
            }
            if (GameInfo.Detect(this.SourceRoot) != ForzaGame.Motorsport4 || GameInfo.Detect(this.TargetRoot) != ForzaGame.Motorsport3)
            {
                throw new InvalidOperationException("The source must be a Forza Motorsport 4 folder and the target a Forza Motorsport 3 folder.");
            }
            System.Diagnostics.Stopwatch lap = System.Diagnostics.Stopwatch.StartNew();
            List<KeyValuePair<string, double>> times = new List<KeyValuePair<string, double>>();
            this.source = SqliteDatabase.FromBytes(File.ReadAllBytes(this.Db(this.SourceRoot)));
            if (this.PackRoot != null)
            {
                DlcSource.MergeRows(this.source, this.Db(this.PackRoot));
            }
            this.target = SqliteDatabase.FromBytes(File.ReadAllBytes(this.Read(this.Db(this.TargetRoot))));
            Lap(lap, times, "open databases");
            try
            {
                long sourceId = Convert.ToInt64(this.Need(this.source.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", this.MediaName), "the FM4 database has no car " + this.MediaName));
                this.ChooseDonor();
                this.carName = this.RenameTo ?? this.MediaName;
                this.inFm3 = this.target.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", this.MediaName) != null;
                if (this.RenameTo == null && (this.target.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", this.MediaName) != null || DlcCatalog.PackOf(ForzaGame.Motorsport3, this.MediaName) != null))   // also a car of FM3's second disc or DLC
                {
                    this.carName = this.MediaName + "_FM4";
                }
                DbIds.PackContent = null;
                this.carId = DbIds.Next(this.target, "Data_Car", "Id", ForzaGame.Motorsport3);
                while (this.carId != sourceId && Convert.ToInt64(this.source.Scalar("SELECT count(*) FROM Data_Car WHERE Id = ?", this.carId)) > 0)
                {
                    this.carId++;   // the id is also written into a copy of FM4's database: it must not be another FM4 car's (id 1314 is the McLaren F1)
                }
                // The car id must also be free where the car will meet other cars: the game folder Xenia plays (FM3_Extracted_DEV,
                // with cars installed there) and the FM3 packages in Xenia. A pack built against the clean FM3 took 1712, the id of
                // a Veyron installed in the DEV folder: the merge failed and neither pack car showed (user 2026-11-21).
                List<SqliteDatabase> others = this.OtherDatabases();
                while (others.Exists(delegate(SqliteDatabase db) { return DbIds.Taken(db, this.carId); })
                    || (this.carId != sourceId && Convert.ToInt64(this.source.Scalar("SELECT count(*) FROM Data_Car WHERE Id = ?", this.carId)) > 0))
                {
                    this.carId++;
                }
                // The engine, drivetrain and wheel ids of those databases too: a copied one must not take them (MoveOutOfGameRange,
                // PortWheel). Two packs built one after the other gave their FM2 cars the same engines 1641.. and wheels 12345..
                // (user 2026-11-22).
                foreach (SqliteDatabase db in others)
                {
                    foreach (string[] owner in new string[][] { new string[] { "Data_Engine", "EngineID" }, new string[] { "Data_Drivetrain", "DrivetrainID" }, new string[] { "List_Wheels", "ID" } })
                    {
                        if (!DbIds.HasTable(db, owner[0]))
                        {
                            continue;
                        }
                        HashSet<long> ids;
                        if (!this.takenElsewhere.TryGetValue(owner[0], out ids))
                        {
                            ids = new HashSet<long>();
                            this.takenElsewhere[owner[0]] = ids;
                        }
                        foreach (object[] row in db.Query("SELECT " + owner[1] + " FROM " + owner[0]).Rows)
                        {
                            if (row[0] != null && !(row[0] is DBNull))
                            {
                                ids.Add(Convert.ToInt64(row[0], CultureInfo.InvariantCulture));
                            }
                        }
                    }
                    db.Dispose();
                }
                while (true)
                {
                    try
                    {
                        DbIds.RenumberCar(SqliteDatabase.FromBytes(this.source.ToBytes()), sourceId, this.carId);
                        break;
                    }
                    catch (InvalidOperationException e)
                    {
                        if (e.Message.IndexOf("is already used", StringComparison.Ordinal) < 0)
                        {
                            throw;
                        }
                        this.carId++;   // rows of other FM4 cars already sit on this id range in the tables keyed by car
                    }
                }
                this.donorId = Convert.ToInt64(this.donorDb.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", this.DonorMediaName));
                this.source.Execute("UPDATE Data_Car SET MediaName = ? WHERE Id = ?", this.carName, sourceId);
                this.Line("car {0} ported as {1}: FM3 id {2} (FM4 id {3}), donor {4} (id {5})", this.MediaName, this.carName, this.carId, sourceId, this.DonorMediaName, this.donorId);
                foreach (string line in DbIds.RenumberCar(this.source, sourceId, this.carId))
                {
                    this.Line("  {0}", line);
                }
                Lap(lap, times, "setup (donor, ids against the installed packs, renumbering)");
                this.PortDatabase();
                Lap(lap, times, "database");
                this.PortStrings();
                Lap(lap, times, "strings");
                this.PortThumbnail();
                Lap(lap, times, "thumbnail");
                this.BuildCarZip();
                Lap(lap, times, "car zip");
                this.PortSound();
                Lap(lap, times, "sound");
                this.BuildWheelZips();
                Lap(lap, times, "wheel zips");
                this.CheckDatabase();
                Lap(lap, times, "database check");
                this.Write(this.Db(this.TargetRoot), this.target.ToBytes());
                Lap(lap, times, "write database");
                this.UpdateManifest();
                Lap(lap, times, "manifest");
                this.Line("done: {0}", this.OutputRoot);
                this.Times(times);
            }
            finally
            {
                this.source.Dispose();
                this.target.Dispose();
            }
        }

        #region Database

        private void PortDatabase()
        {
            Dictionary<string, List<string>> from = DbIds.Schema(this.source);
            Dictionary<string, List<string>> to = DbIds.Schema(this.target);
            string[] carColumns = { "Ordinal", "CarId", "CarID", "CarOrdinal" };
            int tables = 0;
            int rows = 0;
            foreach (KeyValuePair<string, List<string>> table in to)
            {
                List<string> sourceColumns;
                if (!from.TryGetValue(table.Key, out sourceColumns) || table.Key == "Data_Car" || Array.IndexOf(NotCopied, table.Key) >= 0)
                {
                    continue;
                }
                string column = null;
                foreach (string name in carColumns)
                {
                    if (Has(table.Value, name) && Has(sourceColumns, name))
                    {
                        column = name;
                        break;
                    }
                }
                bool body = column == null && (table.Key.StartsWith("List_UpgradeCarBody", StringComparison.Ordinal) || table.Key == "Data_CarBody") && table.Value.Count > 0 && table.Value[0] == "Id";
                if (column == null && !body)
                {
                    continue;
                }
                string where = body ? "Id / 1000 = " + this.carId : SqliteDatabase.Quote(column) + " = " + this.carId;
                string donorWhere = body ? "Id / 1000 = " + this.donorId : SqliteDatabase.Quote(column) + " = " + this.donorId;
                int copied = this.CopyRows(table.Key, sourceColumns, table.Value, where, donorWhere, true);
                if (copied > 0)
                {
                    tables++;
                    rows += copied;
                }
            }
            this.Line("database: {0} rows in {1} car tables + Data_Car", rows, tables);
            this.EnginesAndDrivetrains(from, to);
            this.Dangling(from, to);
            this.PortCarRow(from["Data_Car"], to["Data_Car"]);
            this.PortWheel(from, to);
            this.CockpitCamera();
            this.TuneForMotorsport3();
        }


        // FM4 handling values on FM3's scale: the inverse of CarPort.TuneMotorsport3Handling, whose rules were measured on the 315 cars both
        // games share. CMHeight - stock ride height, spring rates, dampers and anti-roll bars / the FM4 class ratio, steering angle / 1.2,
        // angular velocities 105 -> 70 / 140, ABS release and braking friction back, chassis friction scales, no camber change.
        private void TuneForMotorsport3()
        {
            long low = this.carId * 1000;
            long high = low + 999;
            object ride = this.target.Scalar("SELECT avg(p.DefRideHeight) FROM List_UpgradeSpringDamper u JOIN List_SpringDamperPhysics p ON p.SpringDamperPhysicsID IN (u.FrontSpringDamperPhysicsID, u.RearSpringDamperPhysicsID) WHERE u.Ordinal = ? AND u.IsStock = 1", this.carId);
            object cls = this.source.Scalar("SELECT ClassID FROM Data_Car WHERE Id = ?", this.carId);
            if (ride != null && !(ride is DBNull))
            {
                double height = Convert.ToDouble(ride, CultureInfo.InvariantCulture);
                this.target.Execute("UPDATE List_UpgradeCarBodyWeight SET CMHeight = round(CMHeight - ?, 4) WHERE CarBodyID BETWEEN ? AND ?", height, low, high);
                this.Line("  FM3 handling: CMHeight - stock ride height {0:0.000} m", height);
            }
            if (cls != null && !(cls is DBNull))
            {
                int c = (int) Math.Max(0, Math.Min(9, Convert.ToInt64(cls, CultureInfo.InvariantCulture)));
                double spring = 1 / CarPort.SpringClassRatio[c];
                double bump = 1 / CarPort.DamperBumpRatio[c];
                double rebound = 1 / CarPort.DamperReboundRatio[c];
                this.target.Execute(CarPort.ScaleSql("List_SpringDamperPhysics", "SpringRate", "SpringDamperPhysicsID"), spring, spring, spring, low, high);
                this.target.Execute(CarPort.ScaleSql("List_SpringDamperPhysics", "DampenBumpRate", "SpringDamperPhysicsID"), bump, bump, bump, low, high);
                this.target.Execute(CarPort.ScaleSql("List_SpringDamperPhysics", "DampenReboundRate", "SpringDamperPhysicsID"), rebound, rebound, rebound, low, high);
                this.Line("  FM3 handling: spring rates / {0}, dampers bump / {1}, rebound / {2} (FM4 class {3})", CarPort.SpringClassRatio[c], CarPort.DamperBumpRatio[c], CarPort.DamperReboundRatio[c], c);
                if (c < CarPort.SwayFrontRatio.Length)
                {
                    double front = 1 / CarPort.SwayFrontRatio[c];
                    double rear = 1 / CarPort.SwayRearRatio[c];
                    this.target.Execute(CarPort.ScaleSql("List_AntiSwayPhysics", "SwaybarStiffness", "AntiSwayPhysicsID"), front, front, front, low, low + 99);
                    this.target.Execute(CarPort.ScaleSql("List_AntiSwayPhysics", "SwaybarStiffness", "AntiSwayPhysicsID"), rear, rear, rear, low + 100, low + 199);
                    this.Line("  FM3 handling: anti-roll bars front / {0}, rear / {1}", CarPort.SwayFrontRatio[c], CarPort.SwayRearRatio[c]);
                }
                else
                {
                    this.Line("  FM3 handling: anti-roll bars left as they are (class {0}: ratios known for classes 0-6)", c);
                }
            }
            this.target.Execute("UPDATE Data_Car SET SteerMaxAngle = round(SteerMaxAngle / 1.2, 4), SteerMaxAngVelTurning = CASE WHEN SteerMaxAngVelTurning = 105 THEN 70 ELSE SteerMaxAngVelTurning END, "
                + "SteerMaxAngVelStraighten = CASE WHEN SteerMaxAngVelStraighten = 105 THEN 140 ELSE SteerMaxAngVelStraighten END, SteerAngVelCountersteer = CASE WHEN SteerAngVelCountersteer = 105 THEN 140 ELSE SteerAngVelCountersteer END WHERE Id = ?", this.carId);
            this.target.Execute("UPDATE List_UpgradeBrakes SET ReleasePointABSRearFullSteer = CASE WHEN ReleasePointABSRearFullSteer = 0.75 THEN 1 ELSE ReleasePointABSRearFullSteer END, "
                + "GameFrictionScaleBraking = CASE round(GameFrictionScaleBraking, 4) WHEN 1.0025 THEN 1.01 WHEN 1.005 THEN 1.03 WHEN 1.01 THEN 1.05 ELSE GameFrictionScaleBraking END WHERE Ordinal = ? AND IsStock = 1", this.carId);
            // FM3 has one friction scale per axle where FM4 has lateral and longitudinal (equal): FM4's deficit from 1 is 0.7 x FM3's on the front,
            // and the rear is half the front's deficit where FM3's rear is 1.
            foreach (object[] row in this.source.Query("SELECT Id, FrontLatFrictionScale, RearLatFrictionScale FROM List_UpgradeCarBodyChassisStiffness WHERE CarbodyId BETWEEN ? AND ?", low, high).Rows)
            {
                double front4 = Convert.ToDouble(row[1], CultureInfo.InvariantCulture);
                double rear4 = Convert.ToDouble(row[2], CultureInfo.InvariantCulture);
                double front3 = 1 - ((1 - front4) / 0.7);
                double rear3 = (Math.Abs(rear4 - (1 - ((1 - front4) / 2))) < 0.0026) ? 1 : 1 - ((1 - rear4) / 0.7);
                this.target.Execute("UPDATE List_UpgradeCarBodyChassisStiffness SET FrontFrictionScale = ?, RearFrictionScale = ? WHERE Id = ?", Math.Round(front3, 3), Math.Round(rear3, 3), row[0]);
            }
            this.target.Execute("UPDATE List_SpringDamperPhysics SET CamberChangeRatioCompression = 0, CamberChangeRatioExpansion = 0 WHERE SpringDamperPhysicsID BETWEEN ? AND ? AND SuspensionPhysicsTypeID = 0 AND CamberChangeRatioCompression = 0.25 AND CamberChangeRatioExpansion = 0.25", low, high);
            this.Line("  FM3 handling: steering angle / 1.2, angular velocities 70 / 140, ABS release 1, braking friction scale, chassis friction scales and camber change back to FM3's");
        }

        // The eye is the same point in both games, measured from a different one: FM4's CarPartPositions part 9 (steering wheel) plus
        // CameraOverrides.CamCockpitOffset, FM3's part 4 plus the offset (over the 315 cars both games share the two eyes differ by
        // 0.3 cm mean, 0.7 cm in X, 3 cm in Y, 8 cm in Z). An all-zero FM4 offset means the game's default (0, 0.21, -0.51).
        private void CockpitCamera()
        {
            SqliteDatabase.Result wheel = this.source.Query("SELECT PartPosX, PartPosY, PartPosZ FROM CarPartPositions WHERE Ordinal = ? AND PartId = 9", this.carId);
            SqliteDatabase.Result reference = this.target.Query("SELECT PartPosX, PartPosY, PartPosZ FROM CarPartPositions WHERE Ordinal = ? AND PartId = 4", this.carId);
            SqliteDatabase.Result offset = this.source.Query("SELECT CamCockpitOffsetX, CamCockpitOffsetY, CamCockpitOffsetZ FROM CameraOverrides WHERE CarId = ?", this.carId);
            if (wheel.Rows.Count == 0 || reference.Rows.Count == 0 || offset.Rows.Count == 0)
            {
                this.Line("  cockpit camera: no part 9, part 4 or CameraOverrides row, left as copied");
                return;
            }
            double[] eye = new double[3];
            double[] fromFm4 = new double[3];
            for (int i = 0; i < 3; i++)
            {
                fromFm4[i] = Convert.ToDouble(offset.Rows[0][i], CultureInfo.InvariantCulture);
            }
            if (fromFm4[0] == 0 && fromFm4[1] == 0 && fromFm4[2] == 0)
            {
                fromFm4 = new double[] { 0, 0.21, -0.51 };
            }
            for (int i = 0; i < 3; i++)
            {
                eye[i] = Math.Round(Convert.ToDouble(wheel.Rows[0][i], CultureInfo.InvariantCulture) + fromFm4[i] - Convert.ToDouble(reference.Rows[0][i], CultureInfo.InvariantCulture), 4);
            }
            this.target.Execute("UPDATE CameraOverrides SET CamCockpitOffsetX = ?, CamCockpitOffsetY = ?, CamCockpitOffsetZ = ? WHERE CarId = ?", eye[0], eye[1], eye[2], this.carId);
            this.Line("  cockpit camera: FM4 part 9 + offset ({0}, {1}, {2}) -> FM3 part 4 + offset ({3}, {4}, {5})", fromFm4[0], fromFm4[1], fromFm4[2], eye[0], eye[1], eye[2]);
        }

        // FM3's own database rules on what this port changed (DbIntegrity with FM3's relation map and id limits): a row that
        // points at nothing, or a key inside FM3's own range (the base game's and its packages'), is what made the Horizon
        // 458s crash when bought (2026-11-21). Reported, never fatal: the result is written either way.
        private void CheckDatabase()
        {
            try
            {
                using (SqliteDatabase before = SqliteDatabase.FromBytes(File.ReadAllBytes(this.Read(this.Db(this.TargetRoot)))))
                {
                    DbIntegrity.Result result = DbIntegrity.Compare(before, this.target, ForzaGame.Motorsport3, null, false);
                    List<string> lines = new List<string>(result.Errors);
                    lines.AddRange(result.Warnings);
                    if (lines.Count == 0)
                    {
                        this.Line("database check (FM3's rules): the car's rows point at rows that exist and no key is inside FM3's own range");
                        return;
                    }
                    this.Line("WARNING database check (FM3's rules): {0} thing(s) not regular, the car may crash in game:", lines.Count);
                    for (int i = 0; i < lines.Count && i < 12; i++)
                    {
                        this.Line("  {0}", lines[i]);
                    }
                    if (lines.Count > 12)
                    {
                        this.Line("  ... and {0} more of the same kind", lines.Count - 12);
                    }
                }
            }
            catch (Exception e)
            {
                this.Line("database check (FM3's rules): not run, {0}", e.Message);
            }
        }

        // Rows of FM4's career (the events a car races in) mean nothing in FM3; CarExceptions is FM4's own.
        private static readonly string[] NotCopied = { "EventParticipants", "CarExceptions" };

        // What the copied rows point at that FM3 lacks: an aero set (FM4's rear wing) is added from FM4, a tire compound FM3 has not
        // (FM4's drag tires) is dropped with its upgrade row.
        private void Dangling(Dictionary<string, List<string>> from, Dictionary<string, List<string>> to)
        {
            // Every table of the car that names an aero set (FM4's rear wing, and the front bumper: the Horizon 458s have an
            // adjustable front bumper; its two aero sets were left behind and the cars crashed when bought, 2026-11-21). A
            // set FM3 lacks is added from FM4 under a key of the car (car * 1000 + n) when it is not one already.
            foreach (KeyValuePair<string, List<string>> table in to)
            {
                if (table.Key == "List_AeroPhysics" || !Has(table.Value, "AeroPhysicsID") || !from.ContainsKey("List_AeroPhysics"))
                {
                    continue;
                }
                string name = SqliteDatabase.Quote(table.Key);
                foreach (object[] row in this.target.Query("SELECT DISTINCT AeroPhysicsID FROM " + name + " WHERE AeroPhysicsID IS NOT NULL AND AeroPhysicsID NOT IN (SELECT AeroPhysicsID FROM List_AeroPhysics)").Rows)
                {
                    long id = Convert.ToInt64(row[0], CultureInfo.InvariantCulture);
                    if (Convert.ToInt64(this.source.Scalar("SELECT count(*) FROM List_AeroPhysics WHERE AeroPhysicsID = ?", id), CultureInfo.InvariantCulture) == 0)
                    {
                        continue;   // not this car's: FM4 has no such set either
                    }
                    int copied = this.CopyRows("List_AeroPhysics", from["List_AeroPhysics"], to["List_AeroPhysics"], "AeroPhysicsID = " + id, "1 = 1", false);
                    long written = id;
                    long own = (this.carId * 1000) + (id % 1000);
                    if (id / 1000 != this.carId && Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM List_AeroPhysics WHERE AeroPhysicsID = ?", own), CultureInfo.InvariantCulture) == 0)
                    {
                        this.target.Execute("UPDATE List_AeroPhysics SET AeroPhysicsID = ? WHERE AeroPhysicsID = ?", own, id);
                        this.target.Execute("UPDATE " + name + " SET AeroPhysicsID = ? WHERE AeroPhysicsID = ?", own, id);
                        written = own;
                    }
                    this.Line("  aero physics {0} ({1}): not in FM3, {2} row added from FM4{3}", id, table.Key, copied, (written != id) ? " as " + written : "");
                }
            }
            int dropped = this.target.Execute("DELETE FROM List_UpgradeTireCompound WHERE Ordinal = ? AND TireCompoundID NOT IN (SELECT TireCompoundID FROM List_TireCompound)", this.carId);
            if (dropped > 0)
            {
                this.Line("  tire compound: {0} upgrade row(s) with a compound FM3 lacks (FM4's drag tires) left out", dropped);
            }
        }

        private int copiedTables;
        private readonly List<string> copiedTableNames = new List<string>();

        // An engine or drivetrain just copied from FM4 (CopyPowertrain) whose id is inside FM3's own range (the largest id
        // of the base game and every official package, docs/GameIdLimits.json) gets a free id above it. Such an id is not
        // in the FM3 folder's database but can be a package's: FM3's 458 Italia of the Hot Holidays pack has engine 579 and
        // drivetrain 527, the ids the Horizon 458s carry too, and the copies written under those ids crashed the game when
        // the car was bought (user 2026-11-21). The rows move with their own keys (Id = id * 1000 + n) and the car's
        // List_UpgradeEngine / List_UpgradeDrivetrain rows follow; nothing else in FM3 points at them. Returns the id used.
        private long MoveOutOfGameRange(Dictionary<string, List<string>> to, string owner, string key, string carTable, long id)
        {
            long largest = GameIdLimits.Max(ForzaGame.Motorsport3, owner, key);
            bool elsewhere = this.TakenElsewhere(owner, id);   // above FM3's range but an installed pack's (or the DEV folder's) already
            if ((largest < 0 || id > largest) && !elsewhere)
            {
                return id;
            }
            long fresh = this.FreeId(owner, key);
            int rows = 0;
            foreach (string table in this.copiedTableNames)
            {
                string k = SqliteDatabase.Quote(key);
                if (Has(to[table], "Id") && table != owner)
                {
                    this.target.Execute("UPDATE " + SqliteDatabase.Quote(table) + " SET Id = ? * 1000 + (Id % 1000) WHERE " + k + " = ? AND Id / 1000 = ?", fresh, id, id);
                }
                rows += Convert.ToInt32(this.target.Scalar("SELECT count(*) FROM " + SqliteDatabase.Quote(table) + " WHERE " + k + " = ?", id), CultureInfo.InvariantCulture);
                this.target.Execute("UPDATE " + SqliteDatabase.Quote(table) + " SET " + k + " = ? WHERE " + k + " = ?", fresh, id);
            }
            this.target.Execute("UPDATE " + SqliteDatabase.Quote(carTable) + " SET " + SqliteDatabase.Quote(key) + " = ? WHERE Ordinal = ? AND " + SqliteDatabase.Quote(key) + " = ?", fresh, this.carId, id);
            this.Line(elsewhere ? "  {0} {1} is used by an installed pack or the other FM3 folder: written as {3} ({4} rows)"
                : "  {0} {1} is inside FM3's own range (up to {2}, base game and packages): written as {3} so it cannot meet a package's ({4} rows)", key, id, largest, fresh, rows);
            return fresh;
        }

        // An engine or drivetrain FM4 has and FM3 lacks: its row of owner (Data_Engine / Data_Drivetrain) and the rows of the List_Upgrade tables
        // keyed by it, FM3-only columns from the donor's engine or drivetrain, then what those rows point at that FM3 lacks (engine configs,
        // powertrains) added from FM4. The car's own List_UpgradeEngine / List_UpgradeDrivetrain rows were copied with the car tables.
        private void CopyPowertrain(Dictionary<string, List<string>> from, Dictionary<string, List<string>> to, string owner, string key, long id, long donor)
        {
            this.copiedTables = 0;
            string where = SqliteDatabase.Quote(key) + " = " + id;
            string donorWhere = SqliteDatabase.Quote(key) + " = " + donor;
            List<string> tables = new List<string>();
            tables.Add(owner);
            foreach (string name in to.Keys)
            {
                if (name.StartsWith("List_Upgrade", StringComparison.Ordinal) && name != "List_UpgradeEngine" && name != "List_UpgradeDrivetrain" && Has(to[name], key) && from.ContainsKey(name) && Has(from[name], key))
                {
                    tables.Add(name);
                }
            }
            this.copiedTableNames.Clear();
            foreach (string table in tables)
            {
                if (this.CopyRows(table, from[table], to[table], where, donorWhere, false) > 0)
                {
                    this.copiedTables++;
                    this.copiedTableNames.Add(table);
                }
            }
            foreach (GameDbSchema.Relation relation in GameDbSchema.From(owner))
            {
                if (relation.IsComposite || !to.ContainsKey(relation.Target) || !from.ContainsKey(relation.Target) || !Has(to[owner], relation.Column) || !Has(to[relation.Target], relation.TargetColumn))
                {
                    continue;
                }
                object value = this.target.Scalar("SELECT " + SqliteDatabase.Quote(relation.Column) + " FROM " + SqliteDatabase.Quote(owner) + " WHERE " + SqliteDatabase.Quote(key) + " = " + id);
                if (value == null || value is DBNull || Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM " + SqliteDatabase.Quote(relation.Target) + " WHERE " + SqliteDatabase.Quote(relation.TargetColumn) + " = ?", value)) > 0)
                {
                    continue;
                }
                int added = this.CopyRows(relation.Target, from[relation.Target], to[relation.Target], SqliteDatabase.Quote(relation.TargetColumn) + " = " + Convert.ToString(value, CultureInfo.InvariantCulture), "1 = 1", false);
                this.Line("  {0}.{1} {2}: not in FM3, {3} row added from FM4", relation.Target, relation.TargetColumn, value, added);
            }
        }

        // FM4's torque curve is FM3's full-throttle curve (see CarPort.ConvertMotorsport3TorqueCurves), so the inverse copies its values (FM3 stops
        // at 150 points) under a new id, with the engine id FM3 stores in the curve. FM3's zero-throttle (engine braking) curve has a shape FM4
        // dropped, keeping only its scale (ZeroThrottleTorqueScale): the shape is that of an FM3 curve with the same number of points (the
        // donor's when it has one), scaled by it. Approximate, there is no FM4 data to rebuild it from.
        private void ConvertTorqueCurves(Dictionary<string, List<string>> to, long engine, long donorEngine)
        {
            List<string> columns = to["List_TorqueCurve"];
            int converted = 0;
            foreach (object[] cam in this.target.Query("SELECT Id, TorqueCurveFullThrottleID FROM List_UpgradeEngineCamshaft WHERE EngineID = ?", engine).Rows)
            {
                SqliteDatabase.Result full = this.source.Query("SELECT * FROM List_TorqueCurve WHERE TorqueCurveID = ?", cam[1]);
                if (full.Rows.Count == 0)
                {
                    continue;
                }
                int count = Convert.ToInt32(full.Rows[0][Array.IndexOf(full.Columns, "NumTorqueValues")], CultureInfo.InvariantCulture);
                object zeroScale = full.Rows[0][Array.IndexOf(full.Columns, "ZeroThrottleTorqueScale")];
                long fullId = DbIds.Next(this.target, "List_TorqueCurve", "TorqueCurveID", ForzaGame.Motorsport3);
                this.Insert("List_TorqueCurve", columns, this.CurveValues(columns, full, fullId, engine, null));
                SqliteDatabase.Result shape = this.target.Query("SELECT * FROM List_TorqueCurve WHERE TorqueCurveID IN (SELECT TorqueCurveZeroThrottleID FROM List_UpgradeEngineCamshaft WHERE EngineID = ?) AND NumTorqueValues = ? LIMIT 1", donorEngine, count);
                if (shape.Rows.Count == 0)
                {
                    shape = this.target.Query("SELECT * FROM List_TorqueCurve WHERE TorqueCurveID IN (SELECT TorqueCurveZeroThrottleID FROM List_UpgradeEngineCamshaft) AND NumTorqueValues = ? LIMIT 1", count);
                }
                if (shape.Rows.Count == 0)
                {
                    shape = this.target.Query("SELECT * FROM List_TorqueCurve WHERE TorqueCurveID IN (SELECT TorqueCurveZeroThrottleID FROM List_UpgradeEngineCamshaft WHERE EngineID = ?) LIMIT 1", donorEngine);
                }
                long zeroId = DbIds.Next(this.target, "List_TorqueCurve", "TorqueCurveID", ForzaGame.Motorsport3);
                this.Insert("List_TorqueCurve", columns, this.CurveValues(columns, shape, zeroId, engine, zeroScale));
                this.target.Execute("UPDATE List_UpgradeEngineCamshaft SET TorqueCurveFullThrottleID = ?, TorqueCurveZeroThrottleID = ? WHERE Id = ?", fullId, zeroId, cam[0]);
                converted++;
            }
            if (converted > 0)
            {
                this.Line("  List_TorqueCurve: {0} FM4 curves written for engine {1} (zero-throttle curve: shape of an FM3 curve, FM4's scale)", converted * 2, engine);
            }
        }

        // One List_TorqueCurve row for the columns of FM3's table from a row of either game's table, with the given id, engine and (when not null) scale.
        private object[] CurveValues(List<string> columns, SqliteDatabase.Result row, long id, long engine, object scale)
        {
            object[] values = new object[columns.Count];
            for (int c = 0; c < columns.Count; c++)
            {
                int at = Array.FindIndex(row.Columns, delegate(string s) { return string.Equals(s, columns[c], StringComparison.OrdinalIgnoreCase); });
                string name = columns[c];
                if (string.Equals(name, "TorqueCurveID", StringComparison.OrdinalIgnoreCase))
                {
                    values[c] = id;
                }
                else if (string.Equals(name, "EngineID", StringComparison.OrdinalIgnoreCase))
                {
                    values[c] = engine;
                }
                else if (string.Equals(name, "TorqueScale", StringComparison.OrdinalIgnoreCase) && scale != null)
                {
                    values[c] = scale;
                }
                else
                {
                    values[c] = (at >= 0) ? row.Rows[0][at] : (object) 0.0;
                }
            }
            return values;
        }

        // An engine or drivetrain FM3 has under the same id and MediaName is FM3's own; one it lacks is copied from FM4, one it has under another name is the donor's.
        private void EnginesAndDrivetrains(Dictionary<string, List<string>> from, Dictionary<string, List<string>> to)
        {
            foreach (object[] row in this.target.Query("SELECT DISTINCT EngineID FROM List_UpgradeEngine WHERE Ordinal = ?", this.carId).Rows)
            {
                long engine = Convert.ToInt64(row[0], CultureInfo.InvariantCulture);
                object media = this.source.Scalar("SELECT MediaName FROM Data_Engine WHERE EngineID = ?", engine);
                object present = this.target.Scalar("SELECT MediaName FROM Data_Engine WHERE EngineID = ?", engine);
                if (present != null && media != null && string.Equals((string) present, (string) media, StringComparison.OrdinalIgnoreCase))
                {
                    this.Line("  engine {0} ({1}): FM3's own", engine, present);
                    continue;
                }
                object replacement = this.donorDb.Scalar("SELECT EngineID FROM List_UpgradeEngine WHERE Ordinal = ? AND EngineID <> ? ORDER BY EngineID LIMIT 1", this.donorId, engine);
                if (replacement == null)
                {
                    throw new InvalidOperationException("Engine " + engine + " is not in FM3 and the donor has none.");
                }
                if (present == null && media != null)
                {
                    this.CopyPowertrain(from, to, "Data_Engine", "EngineID", engine, Convert.ToInt64(replacement, CultureInfo.InvariantCulture));
                    long engineAs = this.MoveOutOfGameRange(to, "Data_Engine", "EngineID", "List_UpgradeEngine", engine);
                    this.ConvertTorqueCurves(to, engineAs, Convert.ToInt64(replacement, CultureInfo.InvariantCulture));
                    this.Line("  engine {0} ({1}): not in FM3's folder, copied from FM4 (rows of {2} engine tables, torque curves converted){3}", engine, media, this.copiedTables,
                        (engineAs != engine) ? " as engine " + engineAs : "");
                    continue;
                }
                this.target.Execute("UPDATE List_UpgradeEngine SET EngineID = ? WHERE Ordinal = ? AND EngineID = ?", replacement, this.carId, engine);
                this.Line("  engine {0} ({1}): not in FM3, the donor's engine {2} used", engine, media, replacement);
            }
            foreach (object[] row in this.target.Query("SELECT DISTINCT DrivetrainID FROM List_UpgradeDrivetrain WHERE Ordinal = ?", this.carId).Rows)
            {
                long drivetrain = Convert.ToInt64(row[0], CultureInfo.InvariantCulture);
                if (this.target.Scalar("SELECT count(*) FROM Data_Drivetrain WHERE DrivetrainID = ?", drivetrain) is long && Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM Data_Drivetrain WHERE DrivetrainID = ?", drivetrain)) > 0)
                {
                    this.Line("  drivetrain {0}: FM3's own", drivetrain);
                    continue;
                }
                object replacement = this.donorDb.Scalar("SELECT DrivetrainID FROM List_UpgradeDrivetrain WHERE Ordinal = ? AND DrivetrainID <> ? ORDER BY DrivetrainID LIMIT 1", this.donorId, drivetrain);
                if (replacement == null)
                {
                    throw new InvalidOperationException("Drivetrain " + drivetrain + " is not in FM3 and the donor has none.");
                }
                if (Convert.ToInt64(this.source.Scalar("SELECT count(*) FROM Data_Drivetrain WHERE DrivetrainID = ?", drivetrain), CultureInfo.InvariantCulture) > 0)
                {
                    this.CopyPowertrain(from, to, "Data_Drivetrain", "DrivetrainID", drivetrain, Convert.ToInt64(replacement, CultureInfo.InvariantCulture));
                    long drivetrainAs = this.MoveOutOfGameRange(to, "Data_Drivetrain", "DrivetrainID", "List_UpgradeDrivetrain", drivetrain);
                    this.Line("  drivetrain {0}: not in FM3's folder, copied from FM4 (rows of {1} drivetrain tables){2}", drivetrain, this.copiedTables, (drivetrainAs != drivetrain) ? " as drivetrain " + drivetrainAs : "");
                    continue;
                }
                this.target.Execute("UPDATE List_UpgradeDrivetrain SET DrivetrainID = ? WHERE Ordinal = ? AND DrivetrainID = ?", replacement, this.carId, drivetrain);
                this.Line("  drivetrain {0}: not in FM3, the donor's drivetrain {1} used", drivetrain, replacement);
            }
        }

        private void PortWheel(Dictionary<string, List<string>> from, Dictionary<string, List<string>> to)
        {
            object wheel = this.source.Scalar("SELECT StockWheelID FROM Data_Car WHERE Id = ?", this.carId);
            if (wheel == null)
            {
                return;
            }
            long id = Convert.ToInt64(wheel, CultureInfo.InvariantCulture);
            object media = this.source.Scalar("SELECT MediaName FROM List_Wheels WHERE ID = ?", id);
            object present = this.target.Scalar("SELECT MediaName FROM List_Wheels WHERE ID = ?", id);
            if (present != null && media != null && string.Equals((string) present, (string) media, StringComparison.OrdinalIgnoreCase))
            {
                this.Line("  wheel {0} ({1}): FM3's own, its zip is copied from FM4 only when FM3 lacks it", id, present);
                this.wheelIds.Add(id);
                return;
            }
            if (present != null)
            {
                // The id is another wheel's here (an earlier car of the pack): CopyRows would write the row under a free id but the
                // car would keep pointing at the other wheel (pack FM2_TEST 2026-11-22: five cars on the RX-7's wheel). The free id
                // is chosen first and the car's rows follow it.
                long fresh = this.FreeId("List_Wheels", "ID");
                this.source.Execute("UPDATE List_Wheels SET ID = ? WHERE ID = ?", fresh, id);
                if (DbIds.HasTable(this.source, "WheelExceptions"))
                {
                    this.source.Execute("UPDATE WheelExceptions SET WheelID = ? WHERE WheelID = ?", fresh, id);
                }
                this.source.Execute("UPDATE Data_Car SET StockWheelID = ? WHERE Id = ?", fresh, this.carId);
                this.target.Execute("UPDATE Data_Car SET StockWheelID = ? WHERE Id = ?", fresh, this.carId);
                this.Line("  wheel {0} ({1}): the id is {2}'s in this folder, written as wheel {3}", id, media, present, fresh);
                id = fresh;
            }
            this.CopyRows("List_Wheels", from["List_Wheels"], to["List_Wheels"], "ID = " + id, "ID = (SELECT StockWheelID FROM Data_Car WHERE Id = " + this.donorId + ")", false);
            if (from.ContainsKey("WheelExceptions") && to.ContainsKey("WheelExceptions"))
            {
                this.CopyRows("WheelExceptions", from["WheelExceptions"], to["WheelExceptions"], "WheelID = " + id, "WheelID = (SELECT StockWheelID FROM Data_Car WHERE Id = " + this.donorId + ")", false);
            }
            this.wheelIds.Add(id);
            this.Line("  wheel {0} ({1}): not in FM3's folder, its rows added and its zip converted", id, media);
            // As for engines (MoveOutOfGameRange): a wheel id inside FM3's own range can be a package's (the 458 Italia's
            // wheel 10722 is in the Hot Holidays pack, with a zip of the same name). It gets a free id and the ported car's
            // name, the name the game gives a car's stock wheel, so neither its rows nor its zip can meet a package's.
            long largest = GameIdLimits.Max(ForzaGame.Motorsport3, "List_Wheels", "ID");
            bool elsewhere = this.TakenElsewhere("List_Wheels", id);   // an installed pack's wheel id (or the other FM3 folder's)
            if (((largest >= 0 && id <= largest) || elsewhere) && media is string)
            {
                long fresh = this.FreeId("List_Wheels", "ID");
                string name = this.carName;
                if (Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM List_Wheels WHERE MediaName = ? COLLATE NOCASE AND ID <> ?", name, id), CultureInfo.InvariantCulture) > 0
                    || GamePaths.FindMedia(this.TargetRoot, "wheels", name) != null)
                {
                    name = (string) media;      // FM3 has a wheel of the car's name already: only the id moves
                }
                this.target.Execute("UPDATE List_Wheels SET ID = ?, MediaName = ? WHERE ID = ?", fresh, name, id);
                if (to.ContainsKey("WheelExceptions"))
                {
                    this.target.Execute("UPDATE WheelExceptions SET WheelID = ? WHERE WheelID = ?", fresh, id);
                }
                this.target.Execute("UPDATE Data_Car SET StockWheelID = ? WHERE Id = ? AND StockWheelID = ?", fresh, this.carId, id);
                if (!string.Equals(name, (string) media, StringComparison.OrdinalIgnoreCase))
                {
                    this.wheelNames[(string) media] = name;
                }
                this.Line("  wheel id {0} is inside FM3's own range (up to {1}, base game and packages): written as wheel {2} named {3}", id, largest, fresh, name);
            }
        }

        private readonly Dictionary<string, string> wheelNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // source wheel MediaName -> the name it is written under

        // Data_Car: the donor's FM3 row, the shared columns from the FM4 row but the FM3 ones (GaugeID, FM3-only columns).
        private void PortCarRow(List<string> fromColumns, List<string> toColumns)
        {
            SqliteDatabase.Result donor = this.donorDb.Query("SELECT * FROM Data_Car WHERE Id = ?", this.donorId);
            SqliteDatabase.Result row = this.source.Query("SELECT * FROM Data_Car WHERE Id = ?", this.carId);
            if (row.Rows.Count == 0 || donor.Rows.Count == 0)
            {
                throw new InvalidOperationException("Data_Car row missing.");
            }
            object[] values = (object[]) donor.Rows[0].Clone();
            string[] keep = { "GaugeID", "IsArcade", "MakeIcon", "HasLivery" };
            for (int i = 0; i < toColumns.Count; i++)
            {
                int index = fromColumns.FindIndex(delegate(string c) { return string.Equals(c, toColumns[i], StringComparison.OrdinalIgnoreCase); });
                if (index >= 0 && Array.FindIndex(keep, delegate(string k) { return string.Equals(k, toColumns[i], StringComparison.OrdinalIgnoreCase); }) < 0)
                {
                    values[i] = row.Rows[0][index];
                }
            }
            int makeIndex = toColumns.FindIndex(delegate(string c) { return c == "MakeID"; });
            int arcadeIndex = toColumns.FindIndex(delegate(string c) { return c == "IsArcade"; });
            if (arcadeIndex >= 0)
            {
                values[arcadeIndex] = this.Arcade ? 1L : 0L;
                this.Line("Data_Car IsArcade {0} (the window's choice)", this.Arcade ? 1 : 0);
            }
            if (makeIndex >= 0 && this.MakeId.HasValue)
            {
                if (Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM List_CarMake WHERE ID = ?", this.MakeId.Value)) == 0)
                {
                    throw new InvalidOperationException("FM3 has no make " + this.MakeId.Value + " (a new make is written into the folder before the car).");
                }
                values[makeIndex] = this.MakeId.Value;
                this.Line("make: FM3's make {0} (the window's choice)", this.MakeId.Value);
            }
            if (makeIndex >= 0 && Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM List_CarMake WHERE ID = ?", values[makeIndex])) == 0)
            {
                this.PortMake(Convert.ToInt64(values[makeIndex], CultureInfo.InvariantCulture));
            }
            // MakeIcon is the make's own IconPath in FM3 (the donor's would show its make's logo on every car).
            int iconIndex = toColumns.FindIndex(delegate(string c) { return c == "MakeIcon"; });
            if (iconIndex >= 0 && makeIndex >= 0)
            {
                object icon = this.target.Scalar("SELECT IconPath FROM List_CarMake WHERE ID = ?", values[makeIndex]);
                if (icon is string && ((string) icon).Length > 0)
                {
                    values[iconIndex] = icon;
                }
            }
            // PI on the FM3 scale: the inverse of the FM3 -> FM4 map, fitted on the cars both games have.
            int pi = toColumns.FindIndex(delegate(string c) { return c == "PerformanceIndex"; });
            if (pi >= 0)
            {
                List<double[]> pairs = CarPort.PerformanceIndexPairs(this.target, this.source, -1, this.TargetRoot, this.SourceRoot);
                List<double[]> inverse = new List<double[]>();
                foreach (double[] pair in pairs)
                {
                    inverse.Add(new double[] { pair[1], pair[0] });
                }
                inverse.Sort(delegate(double[] a, double[] b) { return a[0].CompareTo(b[0]); });
                double fm4 = Convert.ToDouble(values[pi], CultureInfo.InvariantCulture);
                double fm3 = CarPort.MapPerformanceIndex(inverse, fm4);
                values[pi] = fm3;
                int cls = toColumns.FindIndex(delegate(string c) { return c == "ClassID"; });
                if (cls >= 0)
                {
                    values[cls] = CarPort.ClassFor(this.target, fm3, this.carId);
                }
                this.Line("  PI {0:0.0000} (FM4) -> {1:0.0000} (FM3 scale, {2} cars shared), class {3}", fm4, fm3, pairs.Count, (cls >= 0) ? CarPort.ClassName(this.target, values[cls]) : "?");
            }
            this.target.Execute("DELETE FROM Data_Car WHERE Id = ?", this.carId);
            this.Insert("Data_Car", toColumns, values);
            this.Line("  Data_Car: FM4 row on the FM3 row of {0} (GaugeID: the dash step sets it)", this.DonorMediaName);
        }

        private int CopyRows(string table, List<string> sourceColumns, List<string> targetColumns, string where, string donorWhere, bool deleteFirst)
        {
            SqliteDatabase.Result rows = this.source.Query("SELECT * FROM " + SqliteDatabase.Quote(table) + " WHERE " + where);
            if (rows.Rows.Count == 0)
            {
                return 0;
            }
            SqliteDatabase.Result template = this.donorDb.Query("SELECT * FROM " + SqliteDatabase.Quote(table) + " WHERE " + donorWhere + " LIMIT 1");
            if (template.Rows.Count == 0)
            {
                template = this.target.Query("SELECT * FROM " + SqliteDatabase.Quote(table) + " LIMIT 1");
            }
            if (deleteFirst)
            {
                this.target.Execute("DELETE FROM " + SqliteDatabase.Quote(table) + " WHERE " + where);
            }
            foreach (object[] row in rows.Rows)
            {
                object[] values = new object[targetColumns.Count];
                for (int i = 0; i < targetColumns.Count; i++)
                {
                    int index = sourceColumns.FindIndex(delegate(string c) { return string.Equals(c, targetColumns[i], StringComparison.OrdinalIgnoreCase); });
                    values[i] = (index >= 0) ? row[index] : ((template.Rows.Count > 0) ? template.Rows[0][i] : null);
                }
                // A key of another row of FM3 (Combo_Engines.EngineComboID): the next free one.
                string key = this.SingleKey(table);
                int keyIndex = (key == null) ? -1 : targetColumns.FindIndex(delegate(string c) { return string.Equals(c, key, StringComparison.OrdinalIgnoreCase); });
                if (keyIndex >= 0 && values[keyIndex] is long && Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM " + SqliteDatabase.Quote(table) + " WHERE " + SqliteDatabase.Quote(key) + " = ?", values[keyIndex])) > 0)
                {
                    long fresh = DbIds.Next(this.target, table, key, ForzaGame.Motorsport3);
                    this.Line("  {0}.{1} {2} is another row's in FM3: {3}", table, key, values[keyIndex], fresh);
                    values[keyIndex] = fresh;
                }
                this.Insert(table, targetColumns, values);
            }
            return rows.Rows.Count;
        }

        // The single-column primary key of a target table (PRAGMA table_info pk), else null.
        private string SingleKey(string table)
        {
            string key = null;
            foreach (object[] column in this.target.Query("PRAGMA table_info(" + SqliteDatabase.Quote(table) + ")").Rows)
            {
                if (Convert.ToInt64(column[5], CultureInfo.InvariantCulture) > 0)
                {
                    if (key != null)
                    {
                        return null;
                    }
                    key = (string) column[1];
                }
            }
            return key;
        }

        private void Insert(string table, List<string> columns, object[] values)
        {
            List<string> quoted = columns.ConvertAll<string>(delegate(string c) { return SqliteDatabase.Quote(c); });
            string marks = string.Join(",", columns.ConvertAll<string>(delegate(string c) { return "?"; }).ToArray());
            this.target.Execute("INSERT INTO " + SqliteDatabase.Quote(table) + " (" + string.Join(",", quoted.ToArray()) + ") VALUES (" + marks + ")", values);
        }

        private static bool Has(List<string> columns, string name)
        {
            return columns.Exists(delegate(string c) { return string.Equals(c, name, StringComparison.OrdinalIgnoreCase); });
        }

        #endregion

        #region Strings

        // Data_Car.DisplayName and ModelShort: a new key in FM3's Data_Car.str of every language, the text from FM4's table of the
        // same language (English when FM4 lacks it), the table id the donor's row uses.
        private void PortStrings()
        {
            string[] columns = { "DisplayName", "ModelShort" };
            object[] names = new object[columns.Length];
            long sourceId = this.carId;
            string targetTables = Path.Combine(Path.Combine(this.TargetRoot, "Media"), "StringTables");
            string sourceTables = Path.Combine(Path.Combine(this.SourceRoot, "Media"), "StringTables");
            Dictionary<string, StrFile> targetFiles = new Dictionary<string, StrFile>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, StrFile> sourceFiles = new Dictionary<string, StrFile>(StringComparer.OrdinalIgnoreCase);
            foreach (string zip in Directory.GetFiles(targetTables, "*.zip"))
            {
                string language = Path.GetFileNameWithoutExtension(zip);
                ForzaZip archive = new ForzaZip(this.Read(zip));
                ForzaZip.Entry entry = archive.Find("Data_Car.str");
                string sourceZip = Path.Combine(sourceTables, language + ".zip");
                if (entry == null)
                {
                    continue;
                }
                string packZip = (this.PackRoot == null) ? null : Path.Combine(Path.Combine(Path.Combine(this.PackRoot, "Media"), "StringTables"), language + ".zip");
                if (packZip != null && File.Exists(packZip) && new ForzaZip(packZip).Find("Data_Car.str") != null)
                {
                    sourceZip = packZip;   // the pack's own table holds its cars' names
                }
                else if (!File.Exists(sourceZip))
                {
                    sourceZip = Path.Combine(sourceTables, "EN.zip");
                }
                ForzaZip sourceArchive = new ForzaZip(sourceZip);
                ForzaZip.Entry sourceEntry = sourceArchive.Find("Data_Car.str");
                if (sourceEntry == null)
                {
                    continue;
                }
                targetFiles[language] = new StrFile(archive.Read(entry));
                sourceFiles[language] = new StrFile(sourceArchive.Read(sourceEntry));
            }
            int next = 0;
            foreach (StrFile file in targetFiles.Values)
            {
                foreach (StrFile.Entry entry in file.Entries)
                {
                    if (entry.Key < 0xFFFF && entry.Key > next)
                    {
                        next = entry.Key;
                    }
                }
            }
            for (int c = 0; c < columns.Length; c++)
            {
                // The FM4 row (the source copy was renumbered to carId).
                object value = this.source.Scalar("SELECT " + columns[c] + " FROM Data_Car WHERE Id = ?", this.carId);
                uint stringId;
                if (!(value is string) || !((string) value).StartsWith("_&") || !uint.TryParse(((string) value).Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out stringId))
                {
                    continue;
                }
                int key = (int) (stringId & 0xFFFF);
                next++;
                foreach (KeyValuePair<string, StrFile> language in targetFiles)
                {
                    StrFile.Entry text = null;
                    foreach (StrFile.Entry entry in sourceFiles[language.Key].Entries)
                    {
                        if (entry.Key == key)
                        {
                            text = entry;
                            break;
                        }
                    }
                    if (text == null)
                    {
                        // The source table of this language lacks the key (FM2 / FH names staged in some languages only): the
                        // English text, as the comment above says. A language left out made the pack's CHS Data_Car.str
                        // unchanged, so the pack had no CHS table at all (validator ERROR, 2026-11-22).
                        text = FindKey(sourceFiles, "EN", key);
                    }
                    if (text != null)
                    {
                        language.Value.Set(next, text.Text, text.Name);
                    }
                }
                object donorValue = this.donorDb.Scalar("SELECT " + columns[c] + " FROM Data_Car WHERE Id = ?", this.donorId);
                uint high = 0;
                if (donorValue is string && ((string) donorValue).StartsWith("_&"))
                {
                    high = uint.Parse(((string) donorValue).Substring(2), CultureInfo.InvariantCulture) & 0xFFFF0000u;
                }
                names[c] = "_&" + (high | (uint) next).ToString(CultureInfo.InvariantCulture);
                this.target.Execute("UPDATE Data_Car SET " + columns[c] + " = ? WHERE Id = ?", names[c], this.carId);
                this.Line("  Data_Car.{0}: FM3 string key {1:X4} ({2})", columns[c], next, names[c]);
            }
            foreach (KeyValuePair<string, StrFile> language in targetFiles)
            {
                string zip = Path.Combine(targetTables, language.Key + ".zip");
                ForzaZip archive = new ForzaZip(this.Read(zip));
                archive.Replace(archive.Find("Data_Car.str"), language.Value.ToBytes());
                Directory.CreateDirectory(Path.GetDirectoryName(this.Out(zip)));
                archive.Save(this.Out(zip));
                this.changedZips.Add(zip);
            }
            this.Line("strings: {0} keys in Data_Car.str of {1} languages", columns.Length, targetFiles.Count);
        }

        private static StrFile.Entry FindKey(Dictionary<string, StrFile> files, string language, int key)
        {
            StrFile file;
            if (!files.TryGetValue(language, out file))
            {
                return null;
            }
            foreach (StrFile.Entry entry in file.Entries)
            {
                if (entry.Key == key)
                {
                    return entry;
                }
            }
            return null;
        }

        #endregion

        #region Zips

        private void ChooseDonor()
        {
            if (this.DonorMediaName == null)
            {
                this.DonorMediaName = this.MediaName;
            }
            // A template car of an FM3 package (second disc or DLC, user 2026-11-21: "i donor li voglio anche dai dlc come per
            // fm4"): DonorRoot is the package laid out as a game folder (DlcSource.Stage + Materialize, by the caller). Its
            // rows are read from a copy of FM3's database with the package's rows put in; nothing of it is written to the
            // result, and the package itself is only read.
            this.donorDb = this.target;
            if (this.DonorRoot != null && this.target.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", this.DonorMediaName) == null)
            {
                string packDb = this.Db(this.DonorRoot);
                if (File.Exists(packDb))
                {
                    this.donorDb = SqliteDatabase.FromBytes(this.target.ToBytes());
                    DlcSource.MergeRows(this.donorDb, packDb);
                }
            }
            this.donorZip = this.FindDonorMedia("cars", this.DonorMediaName);
            if (this.donorZip == null || this.donorDb.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", this.DonorMediaName) == null)
            {
                throw new InvalidOperationException("The FM3 template car " + this.DonorMediaName + " has no zip or no database row, in the FM3 folder or in its package: choose another one.");
            }
        }

        // A zip of the template car (or its wheel): the FM3 folder's, else its package's.
        private string FindDonorMedia(string folder, string name)
        {
            if (name == null)
            {
                return null;
            }
            return GamePaths.FindMedia(this.TargetRoot, folder, name) ?? ((this.DonorRoot == null) ? null : GamePaths.FindMedia(this.DonorRoot, folder, name));
        }

        // A car of the FM3 folder itself (zip in Media\cars, picture in UI\Textures.zip, stock wheel zip): the model for what
        // must exist in the folder when the template car is a package car (its zipmanifest line, the thumbnail's place).
        // Id, MediaName, Thumbnail, stock wheel MediaName; empty strings when the folder has none.
        private string[] folderCar;

        private string[] FolderCar()
        {
            if (this.folderCar != null)
            {
                return this.folderCar;
            }
            this.folderCar = new string[] { "", "", "", "" };
            foreach (object[] row in this.target.Query("SELECT c.Id, c.MediaName, c.Thumbnail, w.MediaName FROM Data_Car c LEFT JOIN List_Wheels w ON w.ID = c.StockWheelID ORDER BY c.Id").Rows)
            {
                string media = row[1] as string;
                string picture = row[2] as string;
                string wheel = row[3] as string;
                if (media == null || picture == null || wheel == null || GamePaths.FindMedia(this.TargetRoot, "cars", media) == null || GamePaths.FindMedia(this.TargetRoot, "wheels", wheel) == null)
                {
                    continue;
                }
                GamePaths.GameFile file = GamePaths.Locate(this.TargetRoot, picture);
                if (file != null && file.InZip)
                {
                    this.folderCar = new string[] { Convert.ToString(row[0], CultureInfo.InvariantCulture), media, picture, wheel };
                    break;
                }
            }
            return this.folderCar;
        }

        // Databases whose car ids the new car must not take (OtherDatabases): the FM3 game folders besides the target (its test
        // copy <folder>_DEV and the folder it is the copy of) and the merge databases of the FM3 packages in Xenia.
        public string XeniaContent;     // Xenia's FM3 content folder, or null

        private List<SqliteDatabase> OtherDatabases()
        {
            List<SqliteDatabase> found = new List<SqliteDatabase>();
            List<string> paths = new List<string>();
            string root = this.TargetRoot.TrimEnd('\\', '/');
            foreach (string folder in new string[] { root + "_DEV", root.EndsWith("_DEV", StringComparison.OrdinalIgnoreCase) ? root.Substring(0, root.Length - 4) : null })
            {
                if (folder != null && File.Exists(this.Db(folder)))
                {
                    paths.Add(this.Db(folder));
                }
            }
            paths.AddRange(DbIds.PackDatabases(this.XeniaContent));
            foreach (string path in paths)
            {
                try
                {
                    found.Add(SqliteDatabase.FromBytes(File.ReadAllBytes(path)));
                }
                catch (Exception)
                {
                }
            }
            if (paths.Count > 0)
            {
                this.Line("car id: free also in {0} other database(s) (the game folder's test copy or original, FM3 packages in Xenia)", found.Count);
            }
            return found;
        }

        public string DonorRoot;        // the template car is a car of an FM3 package: that package laid out as a game folder (else null)
        private SqliteDatabase donorDb; // where the template car's rows are read: FM3's database, or a copy with the package's rows

        private void BuildCarZip()
        {
            string path = this.FindSourceMedia("cars", this.MediaName);
            if (path == null)
            {
                throw new InvalidOperationException("The FM4 folder has no zip for " + this.MediaName + ".");
            }
            ForzaZip from = new ForzaZip(path);
            ForzaZip donor = new ForzaZip(this.donorZip);
            ForzaZip car = ForzaZip.CreateLike(donor);
            string oldPrefix = this.MediaName.ToLowerInvariant();
            string newPrefix = this.carName.ToLowerInvariant();
            int models = 0;
            int textures = 0;
            foreach (ForzaZip.Entry entry in from.Entries)
            {
                string name = entry.Name.ToLowerInvariant();
                if (name == "versiondata.xml" || name == "buildnumber.txt" || name.EndsWith("_build_report.html") || name == "port_log.txt")
                {
                    continue;
                }
                if (name.StartsWith("digitalgauge/") || name.StartsWith("digitalgauge\\"))
                {
                    continue;   // FM3 keeps the dash outside the car zip (PortDash)
                }
                byte[] data = from.Read(entry);
                if (name == "shadersettings.xml" && donor.Find("shadersettings.xml") != null)
                {
                    // FM4's file names FM4-only materials (lights_glass) and parameters; FM3's own car file names FM3's.
                    data = donor.Read(donor.Find("shadersettings.xml"));
                    this.Line("shadersettings.xml: the donor's (FM4's names materials FM3 lacks)");
                }
                if (name.EndsWith(".carbin"))
                {
                    CarbinModel model = new CarbinModel(data);
                    model.ConvertMotorsport4ToMotorsport3();
                    this.RenameMaterials(model, name);
                    data = model.ToBytes();
                    // Every mesh into the draw pass of its FM3 material and every part's see-through flag as its meshes say
                    // (MaterialSlots, FM3's rule on all its stock files): a review fix gave glass to a mesh left in the
                    // opaque pass, which FM3 does not draw (FH traffic Yukon tail light glass lights_gls_* 2026-11-22, as
                    // the FH2 595 in FM4 on 2026-11-08).
                    MaterialSlots slots = new MaterialSlots(data);
                    data = slots.ToBytes();
                    if (slots.Fixes.Count > 0)
                    {
                        this.Line("  {0}: draw pass / see-through flag as FM3 has them: {1}", name, string.Join(", ", slots.Fixes.ToArray()));
                    }
                    if (name.Contains("_caliper") || name.Contains("_rotor"))
                    {
                        name = name.Replace("_lod0.carbin", ".carbin");
                    }
                    models++;
                }
                else if (name.EndsWith(".xds"))
                {
                    data = Fm3Textures.FromMotorsport4(data, name);
                    textures++;
                }
                if (name.StartsWith(oldPrefix))
                {
                    name = newPrefix + name.Substring(oldPrefix.Length);
                }
                car.Add(name, data);
            }
            string target = Path.Combine(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "Cars"), this.carName + ".zip");
            this.Line("car zip: {0} models, {1} textures converted, {2} entries (headers of {3})", models, textures, car.Entries.Count, this.DonorMediaName);
            this.ReviewMaterials();
            this.SaveNew(car, target, null);
            this.PortDash(from);
        }

        // The FM4 car's digital dash as FM3 keeps it: Data_Car.GaugeID -> List_DigitalGauges.MediaName "Dash_<car>" -> Media\DigitalGauges\Dash_<car>.zip
        // (dash_<car>.fbf/.bgf/.bsg, Fm3Dash) and the images in Media\UI\Textures.zip under UI4\Dashboards\<car>\ (converted by Fm3Textures; an image
        // whose file FM3's shared MFD_* folders already hold keeps FM3's). A car without a dash gets GaugeID -1, not the donor's dash. A dash that
        // uses properties no FM3 dash has is left out unless AllowFm4OnlyDash.
        private void PortDash(ForzaZip from)
        {
            string stem = "digitalgauge/dash_" + this.MediaName.ToLowerInvariant();
            ForzaZip.Entry fbfEntry = from.Find(stem + ".fbf") ?? from.Find(stem.Replace('/', (char) 92) + ".fbf");
            ForzaZip.Entry bgfEntry = from.Find(stem + ".bgf") ?? from.Find(stem.Replace('/', (char) 92) + ".bgf");
            ForzaZip.Entry bsgEntry = from.Find(stem + ".bsg") ?? from.Find(stem.Replace('/', (char) 92) + ".bsg");
            if (fbfEntry == null || bgfEntry == null || bsgEntry == null)
            {
                this.target.Execute("UPDATE Data_Car SET GaugeID = -1 WHERE Id = ?", this.carId);
                this.Line("dash: the FM4 car has none, GaugeID -1 (not the donor's)");
                return;
            }
            byte[] bgfData = from.Read(bgfEntry);
            AnarkBgf bgf = AnarkBgf.Read(bgfData);
            List<string> fm4Only = Fm3Dash.Fm4OnlyProperties(bgf);
            if (fm4Only.Count > 0)
            {
                this.Line("dash: uses properties no FM3 dash has ({0}){1}", string.Join(", ", fm4Only.ToArray()), this.AllowFm4OnlyDash ? ", ported anyway" : ", left out (GaugeID -1)");
                if (!this.AllowFm4OnlyDash)
                {
                    this.target.Execute("UPDATE Data_Car SET GaugeID = -1 WHERE Id = ?", this.carId);
                    return;
                }
            }
            string texturesPath = Path.Combine(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "UI"), "Textures.zip");
            ForzaZip textures = new ForzaZip(this.Read(texturesPath));
            Dictionary<string, string> shared = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ForzaZip.Entry entry in textures.Entries)
            {
                string entryName = entry.Name.Replace((char) 92, '/');
                if (entryName.StartsWith("UI4/Dashboards/MFD_", StringComparison.OrdinalIgnoreCase) && !shared.ContainsKey(Path.GetFileNameWithoutExtension(entryName)))
                {
                    shared[Path.GetFileNameWithoutExtension(entryName)] = entryName;
                }
            }
            Dictionary<string, byte[]> images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            List<string> sharedUsed = new List<string>();
            List<string> missing = new List<string>();
            const string ui = @"GAME:\MEDIA\UI\TEXTURES\";
            string folder = this.carName.ToUpperInvariant();
            List<string> unknownFonts = new List<string>();
            byte[] fbf = Fm3Dash.ToMotorsport3(from.Read(fbfEntry), bgf, delegate(string path)
            {
                string name = Path.GetFileNameWithoutExtension(path.Replace((char) 92, '/'));
                string sharedEntry;
                if (shared.TryGetValue(name, out sharedEntry))
                {
                    if (!sharedUsed.Contains(name))
                    {
                        sharedUsed.Add(name);
                    }
                    return ui + sharedEntry.Substring(0, sharedEntry.Length - 4).Replace('/', (char) 92).ToUpperInvariant() + ".TGA";
                }
                if (!images.ContainsKey(name))
                {
                    ForzaZip.Entry image = from.Find("digitalgauge/" + name + ".xds") ?? from.Find("digitalgauge\\" + name + ".xds");
                    if (image == null)
                    {
                        if (!missing.Contains(name))
                        {
                            missing.Add(name);
                        }
                        return null;
                    }
                    images[name] = Fm3Textures.FromMotorsport4(from.Read(image), image.Name);
                }
                return ui + @"UI4\DASHBOARDS\" + folder + (char) 92 + name.ToUpperInvariant() + ".TGA";
            }, unknownFonts);
            int fontsChanged;
            List<string> unknownBgf = new List<string>();
            bgfData = DashFonts.Fix(bgfData, out fontsChanged, unknownBgf, true);
            foreach (string font in unknownBgf)
            {
                if (!unknownFonts.Contains(font))
                {
                    unknownFonts.Add(font);
                }
            }
            string mediaName = "Dash_" + this.carName;
            object existing = this.target.Scalar("SELECT GaugeID FROM List_DigitalGauges WHERE MediaName = ? COLLATE NOCASE", mediaName);
            long gauge = (existing == null) ? DbIds.Next(this.target, "List_DigitalGauges", "GaugeID", ForzaGame.Motorsport3) : Convert.ToInt64(existing, CultureInfo.InvariantCulture);
            if (existing == null)
            {
                this.target.Execute("INSERT INTO List_DigitalGauges (GaugeID, MediaName) VALUES (?, ?)", gauge, mediaName);
            }
            this.target.Execute("UPDATE Data_Car SET GaugeID = ? WHERE Id = ?", gauge, this.carId);
            // The new gauge zip takes its headers from an FM3 dash zip.
            string likeName = this.target.Scalar("SELECT g.MediaName FROM List_DigitalGauges g WHERE g.GaugeID <> ? ORDER BY g.GaugeID LIMIT 1", gauge) as string;
            string like = (likeName == null) ? null : Path.Combine(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "DigitalGauges"), likeName + ".zip");
            if (like == null || !File.Exists(like))
            {
                this.Line("dash: WARNING no FM3 dash zip to copy headers from, the car goes without a dash");
                this.target.Execute("UPDATE Data_Car SET GaugeID = -1 WHERE Id = ?", this.carId);
                return;
            }
            ForzaZip gauges = ForzaZip.CreateLike(new ForzaZip(like));
            string entryStem = "dash_" + this.carName.ToLowerInvariant();
            gauges.Add(entryStem + ".fbf", fbf);
            gauges.Add(entryStem + ".bgf", bgfData);
            gauges.Add(entryStem + ".bsg", from.Read(bsgEntry));
            this.SaveNew(gauges, Path.Combine(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "DigitalGauges"), mediaName + ".zip"), like);
            if (images.Count > 0)
            {
                foreach (KeyValuePair<string, byte[]> image in images)
                {
                    string entryName = "UI4/Dashboards/" + this.carName + "/" + image.Key;
                    if (textures.Find(entryName + ".xds") != null)
                    {
                        textures.Replace(textures.Find(entryName + ".xds"), image.Value);
                    }
                    else
                    {
                        textures.Add(entryName + ".xds", image.Value);
                    }
                }
                Directory.CreateDirectory(Path.GetDirectoryName(this.Out(texturesPath)));
                textures.Save(this.Out(texturesPath));
                if (!this.changedZips.Contains(texturesPath))
                {
                    this.changedZips.Add(texturesPath);
                }
            }
            this.Line("dash: gauge {0} {1}: {2} images added to UI\\Textures.zip, {3} shared MFD images of FM3 used, {4} font names changed", gauge, mediaName, images.Count, sharedUsed.Count, fontsChanged);
            if (missing.Count > 0)
            {
                this.Line("dash: WARNING images not in the FM4 zip, left as they were: {0}", string.Join(", ", missing.ToArray()));
            }
            if (unknownFonts.Count > 0)
            {
                this.Line("dash: WARNING font {0} has no FM3 equivalent rule", string.Join(", ", unknownFonts.ToArray()));
            }
        }

        // Materials FM4 added that FM3 draws with another name (lights_glass is FM3's detail_glass_clear on the Supra's headlights).
        private static readonly string[][] MaterialNames = { new string[] { "lights_glass", "detail_glass_clear" } };

        // The review of the car's materials for FM3 (2026-11-21; the target is FM3, so FM3's cars are the measure): a
        // material no FM3 car uses in that kind of file and LOD (ShaderProfiles.IsUsed, the survey of FM3's 311 cars) is an
        // issue, with FM3's own materials as choices. A fix (Fixes "material:<name>" -> an FM3 material) is the user's
        // choice, done here on every model file, brakes included; where the chosen name is not FM3's in a file but its
        // _lod0 twin is (tail_light / tail_light_lod0), the twin is written. Nothing is renamed on its own.
        public Dictionary<string, object> Fixes;
        public readonly List<CarPort.Issue> Issues = new List<CarPort.Issue>();
        private readonly List<string> unknownOrder = new List<string>();
        private readonly Dictionary<string, List<string>> unknownParts = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string[]>> unknownPlaces = new Dictionary<string, List<string[]>>(StringComparer.Ordinal);   // list, part
        private readonly Dictionary<string, List<string>> unknownNow = new Dictionary<string, List<string>>(StringComparer.Ordinal);          // the names a fix gave it
        private int materialsRenamed;
        private readonly List<string> emptyParts = new List<string>();   // <part>_LODn dummies left out of the review

        private static string LodTwin(string material)
        {
            return material.EndsWith("_lod0", StringComparison.Ordinal) ? material.Substring(0, material.Length - 5) : material + "_lod0";
        }

        private void RenameMaterials(CarbinModel model, string fileName)
        {
            foreach (CarbinModel.Section section in model.Sections)
            {
                foreach (CarbinModel.SubSection sub in section.SubSections)
                {
                    foreach (string[] pair in MaterialNames)
                    {
                        if (sub.Name == pair[0])
                        {
                            sub.Name = pair[1];
                        }
                    }
                    string original = sub.Name;
                    string list = Fm4MaterialUse.ListName(false, fileName, sub.Lod);
                    if (CarPort.IsEmptyPart(section, sub))
                    {
                        // <part>_LODn: an empty-part dummy (a 0.2 m cube or a few-index LOD 2-5 subsection, FH traffic and
                        // FM4's own cars alike), never seen; out of the review as in the FM4 port (user 2026-10-18,
                        // again 2026-11-22: bumperFa_LOD3..5 / bumperRa_LOD1..4 listed in the FM3 Review)
                        if (!this.emptyParts.Contains(original))
                        {
                            this.emptyParts.Add(original);
                        }
                        continue;
                    }
                    object chosen;
                    if (this.Fixes != null && this.Fixes.TryGetValue("material:" + original, out chosen) && chosen is string && ((string) chosen).Length > 0)
                    {
                        string to = (string) chosen;
                        if (!ShaderProfiles.IsUsed(ShaderProfile.Fm3, list, to) && ShaderProfiles.IsUsed(ShaderProfile.Fm3, list, LodTwin(to)))
                        {
                            to = LodTwin(to);
                        }
                        if (to != original)
                        {
                            sub.Name = to;
                            this.materialsRenamed++;
                        }
                    }
                    if (ShaderProfiles.IsUsed(ShaderProfile.Fm3, list, sub.Name))
                    {
                        continue;
                    }
                    if (!this.unknownParts.ContainsKey(original))
                    {
                        this.unknownOrder.Add(original);
                        this.unknownParts[original] = new List<string>();
                        this.unknownPlaces[original] = new List<string[]>();
                        this.unknownNow[original] = new List<string>();
                    }
                    if (!this.unknownParts[original].Contains(section.Name))
                    {
                        this.unknownParts[original].Add(section.Name);
                    }
                    string part = section.Name;
                    if (!this.unknownPlaces[original].Exists(delegate(string[] p) { return p[0] == list && p[1] == part; }))
                    {
                        this.unknownPlaces[original].Add(new string[] { list, part });
                    }
                    if (sub.Name != original && !this.unknownNow[original].Contains(sub.Name))
                    {
                        this.unknownNow[original].Add(sub.Name);
                    }
                }
            }
        }

        // The issues of the materials RenameMaterials found not FM3's, with their choices: first what FM3's cars give those
        // parts (recommended: the materials found on the most of them, then on the most cars), then FM3's other materials
        // of those lists. Names are shown once (tail_light stands for tail_light_lod0 too, see RenameMaterials).
        private void ReviewMaterials()
        {
            if (this.materialsRenamed > 0)
            {
                this.Line("materials: {0} mesh(es) renamed to the FM3 material chosen in the review", this.materialsRenamed);
            }
            if (this.emptyParts.Count > 0)
            {
                this.Line("materials: empty parts, not reviewed: {0}", string.Join(", ", this.emptyParts.ToArray()));
            }
            List<string> left = new List<string>();
            foreach (string material in this.unknownOrder)
            {
                string where = string.Join(" ", this.unknownParts[material].ToArray());
                CarPort.Issue issue = new CarPort.Issue();
                issue.Key = "material:" + material;
                issue.Kind = "material";
                issue.Where = where;
                issue.Value = material;
                Dictionary<string, Dictionary<string, int>> byPart = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);   // material -> part -> cars
                List<string> others = new List<string>();
                foreach (string[] place in this.unknownPlaces[material])
                {
                    foreach (KeyValuePair<string, int> pair in ShaderProfiles.PartMaterials(ShaderProfile.Fm3, place[0], place[1]))
                    {
                        string name = pair.Key.EndsWith("_lod0", StringComparison.Ordinal) ? LodTwin(pair.Key) : pair.Key;
                        Dictionary<string, int> cars;
                        if (!byPart.TryGetValue(name, out cars))
                        {
                            cars = new Dictionary<string, int>(StringComparer.Ordinal);
                            byPart[name] = cars;
                        }
                        int before;
                        cars.TryGetValue(place[1], out before);
                        cars[place[1]] = Math.Max(before, pair.Value);
                    }
                    foreach (KeyValuePair<string, int> pair in ShaderProfiles.PartMaterials(ShaderProfile.Fm3, place[0], null))
                    {
                        string name = pair.Key.EndsWith("_lod0", StringComparison.Ordinal) ? LodTwin(pair.Key) : pair.Key;
                        if (!others.Contains(name))
                        {
                            others.Add(name);
                        }
                    }
                }
                int partCount = this.unknownParts[material].Count;
                List<string> ranked = new List<string>(byPart.Keys);
                ranked.Sort(delegate(string a, string b)
                {
                    int c = byPart[b].Count.CompareTo(byPart[a].Count);
                    if (c == 0)
                    {
                        c = byPart[b].Values.Max().CompareTo(byPart[a].Values.Max());
                    }
                    return (c != 0) ? c : string.CompareOrdinal(a, b);
                });
                foreach (string name in ranked)
                {
                    CarPort.Choice choice = new CarPort.Choice();
                    choice.Value = name;
                    choice.Label = (partCount > 1)
                        ? string.Format(CultureInfo.InvariantCulture, "{0}   (FM3: on {1} of these {2} parts, up to {3} cars)", name, byPart[name].Count, partCount, byPart[name].Values.Max())
                        : string.Format(CultureInfo.InvariantCulture, "{0}   (FM3: {1} cars on {2})", name, byPart[name].Values.Max(), where);
                    choice.Recommended = issue.Choices.Count < 10;
                    issue.Choices.Add(choice);
                }
                others.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string name in others)
                {
                    if (!byPart.ContainsKey(name))
                    {
                        CarPort.Choice choice = new CarPort.Choice();
                        choice.Value = name;
                        choice.Label = name;
                        issue.Choices.Add(choice);
                    }
                }
                List<string> now = this.unknownNow[material];
                issue.Problem = (now.Count > 0)
                    ? string.Format("material {0}, now {1}: no FM3 car uses that there (renders black)", material, string.Join(" / ", now.ToArray()))
                    : string.Format("material {0}: no FM3 car uses it there (renders black)", material);
                if (ranked.Count > 0)
                {
                    issue.Problem += "; FM3 cars use " + ranked[0] + " most there";
                }
                issue.Suggested = null;     // the user picks
                this.Issues.Add(issue);
                left.Add((now.Count > 0) ? material + " (now " + string.Join(" / ", now.ToArray()) + ")" : material);
            }
            if (left.Count > 0)
            {
                this.Line("WARNING materials: {0} material(s) no FM3 car uses may render black; choose a fix in the Review tab (or Rename Material in the editor): {1}", left.Count, string.Join(", ", left.ToArray()));
            }
            else
            {
                this.Line("materials: every material of the car's models is one FM3's cars use in that kind of file and LOD");
            }
        }

        private void BuildWheelZips()
        {
            foreach (long id in this.wheelIds)
            {
                string media = this.source.Scalar("SELECT MediaName FROM List_Wheels WHERE ID = ?", id) as string;
                string writeAs;
                if (!this.wheelNames.TryGetValue(media ?? "", out writeAs))
                {
                    writeAs = media;    // PortWheel renames a wheel whose id is inside FM3's own range
                }
                string targetZip = GamePaths.FindMedia(this.TargetRoot, "wheels", writeAs);
                if (targetZip != null)
                {
                    this.Line("wheel zip {0}: FM3 has it, kept", media);
                    continue;
                }
                string path = this.FindSourceMedia("wheels", media);
                string donorWheel = this.donorDb.Scalar("SELECT w.MediaName FROM List_Wheels w JOIN Data_Car c ON c.StockWheelID = w.ID WHERE c.Id = ?", this.donorId) as string;
                string donorPath = (donorWheel == null) ? null : this.FindDonorMedia("wheels", donorWheel);
                if (path == null || donorPath == null)
                {
                    this.Line("wheel zip {0}: not found, skipped", media);
                    continue;
                }
                ForzaZip from = new ForzaZip(path);
                ForzaZip wheel = ForzaZip.CreateLike(new ForzaZip(donorPath));
                foreach (ForzaZip.Entry entry in from.Entries)
                {
                    string name = entry.Name.ToLowerInvariant();
                    if (name == "versiondata.xml" || name == "buildnumber.txt" || name.EndsWith("_build_report.html"))
                    {
                        continue;
                    }
                    byte[] data = from.Read(entry);
                    if (name.EndsWith(".carbin"))
                    {
                        CarbinModel model = new CarbinModel(data);
                        if (model.Type == 3)
                        {
                            // FM3 draws the stock wheel at 1.085 x the car's nominal rim radius (no morph): the conversion sizes LOD 0
                            // when the model is outside the band of FM3's own wheels (FM2 rims are modelled smaller).
                            object inches = this.target.Scalar("SELECT FrontWheelDiameterIN FROM Data_Car WHERE Id = ?", this.carId);
                            if (inches != null && !(inches is DBNull))
                            {
                                model.Motorsport3WheelRadius = 1.085 * Convert.ToDouble(inches, CultureInfo.InvariantCulture) * 0.0127;
                            }
                            // FM3 fits its tire on the LOD 0 wheel_black ring; a wheel without it (FM2-born) shows no tire.
                            string seat = model.AddTireSeatRing();
                            if (seat != null)
                            {
                                this.Line("  wheel {0}: {1}", media, seat);
                            }
                        }
                        string converted = model.ConvertMotorsport4ToMotorsport3();
                        foreach (string line in (converted ?? "").Replace("\r\n", "\n").Split('\n'))
                        {
                            if (line.Contains("sized for FM3"))
                            {
                                this.Line("  wheel {0}: {1}", media, line.Trim());
                            }
                        }
                        data = model.ToBytes();
                    }
                    else if (name.EndsWith(".xds"))
                    {
                        data = Fm3Textures.FromMotorsport4(data, name);
                    }
                    wheel.Add((writeAs != media && name == media.ToLowerInvariant() + ".carbin") ? writeAs.ToLowerInvariant() + ".carbin" : name, data);
                    // FM3 keeps a copy of each wheelN.xds as wheelN_lod0.xds (FM4 shares the file).
                    // A wheel that came from Forza Horizon carries its own wheelN_lod0.xds (as FM3's do): that file is used.
                    if (name.StartsWith("wheel") && name.EndsWith(".xds") && !name.Contains("_lod0") && name != "wheel.xds"
                        && from.Find(name.Replace(".xds", "_lod0.xds")) == null)
                    {
                        wheel.Add(name.Replace(".xds", "_lod0.xds"), data);
                    }
                }
                string target = Path.Combine(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "Wheels"), writeAs + ".zip");
                this.Line("wheel zip {0}: {1} entries (headers of {2})", writeAs, wheel.Entries.Count, donorWheel);
                this.SaveNew(wheel, target, null);
            }
        }

        // likePath: the existing FM3 zip whose zipmanifest.xml line the new zip's line copies (null: the donor car's zip, or a wheel's).
        private void SaveNew(ForzaZip archive, string targetPath, string likePath)
        {
            string output = this.Out(targetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            archive.Save(output);
            this.newZips.Add(targetPath);
            if (likePath != null)
            {
                this.newZipLike[targetPath] = likePath;
            }
        }

        private readonly Dictionary<string, string> newZipLike = new Dictionary<string, string>();

        private void UpdateManifest()
        {
            string manifest = this.Out(ZipManifest.PathFor(this.TargetRoot));
            if (!File.Exists(manifest))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(manifest));
                File.Copy(ZipManifest.PathFor(this.TargetRoot), manifest);
            }
            foreach (string path in this.changedZips)
            {
                if (!ZipManifest.Update(manifest, ZipManifest.GamePath(this.TargetRoot, path), this.Out(path)))
                {
                    this.Line("zipmanifest.xml: no line for {0}", Path.GetFileName(path));
                }
            }
            foreach (string path in this.newZips)
            {
                bool wheel = path.IndexOf(Path.DirectorySeparatorChar + "Wheels" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0;
                string explicitLike;
                string like = this.newZipLike.TryGetValue(path, out explicitLike) ? explicitLike : wheel ? this.FindDonorMedia("wheels", this.donorDb.Scalar("SELECT w.MediaName FROM List_Wheels w JOIN Data_Car c ON c.StockWheelID = w.ID WHERE c.Id = ?", this.donorId) as string) : this.donorZip;
                if (like == null || !like.StartsWith(this.TargetRoot, StringComparison.OrdinalIgnoreCase))
                {
                    // the template is a package car: its zip is not in FM3's manifest, the line of a car of the FM3 folder is the model
                    like = wheel ? GamePaths.FindMedia(this.TargetRoot, "wheels", this.FolderCar()[3]) : GamePaths.FindMedia(this.TargetRoot, "cars", this.FolderCar()[1]);
                }
                if (like == null || !ZipManifest.Add(manifest, ZipManifest.GamePath(this.TargetRoot, path), this.Out(path), ZipManifest.GamePath(this.TargetRoot, like)))
                {
                    this.Line("zipmanifest.xml: no line like {0} to copy for {1}", like, Path.GetFileName(path));
                }
            }
            this.Line("zipmanifest.xml: {0} lines added", this.newZips.Count);
        }

        #endregion

        #region Make

        // A make FM3 lacks (MG): its List_CarMake row on the FM4 id (free in FM3, the country must exist), DisplayName and Profile in
        // List_CarMake.str of every FM3 language (the FM4 keys when free in all of them, else the next free from FFFE down; a language the
        // source lacks gets English), and its logos converted into UI\Textures.zip: FM4's Manufacturer_Logos_I one under Manufacturer_Logos
        // and Manufacturer_Logos_I, the _SM one under the path the row's IconPathLight names. No titles or livery decals: FM3 has none.
        private void PortMake(long make)
        {
            SqliteDatabase.Result row = this.source.Query("SELECT DisplayName, ManufacturerCode, CountryID, IconPath, IconPathLight, Profile FROM List_CarMake WHERE ID = ?", make);
            if (row.Rows.Count == 0)
            {
                throw new InvalidOperationException("Make " + make + " is in neither FM3 nor the FM4 database.");
            }
            object[] source = row.Rows[0];
            if (Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM List_Country WHERE CountryID = ?", source[2])) == 0)
            {
                throw new InvalidOperationException("The make's country " + source[2] + " is not in FM3 (adding countries to FM3 is not done yet).");
            }
            string targetTables = Path.Combine(Path.Combine(this.TargetRoot, "Media"), "StringTables");
            Dictionary<string, StrFile> files = new Dictionary<string, StrFile>(StringComparer.OrdinalIgnoreCase);
            foreach (string zip in Directory.GetFiles(targetTables, "*.zip"))
            {
                ForzaZip archive = new ForzaZip(this.Read(zip));
                ForzaZip.Entry entry = archive.Find("List_CarMake.str");
                if (entry != null)
                {
                    files[Path.GetFileNameWithoutExtension(zip)] = new StrFile(archive.Read(entry));
                }
            }
            uint high = 0;
            object other = this.target.Scalar("SELECT DisplayName FROM List_CarMake WHERE DisplayName LIKE '\\_&%' ESCAPE '\\' LIMIT 1");
            if (other is string)
            {
                high = uint.Parse(((string) other).Substring(2), CultureInfo.InvariantCulture) & 0xFFFF0000u;
            }
            string[] ids = new string[2];
            for (int c = 0; c < 2; c++)
            {
                string value = Convert.ToString(source[c == 0 ? 0 : 5], CultureInfo.InvariantCulture);
                uint stringId;
                int key = (value.StartsWith("_&", StringComparison.Ordinal) && uint.TryParse(value.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out stringId)) ? (int) (stringId & 0xFFFF) : -1;
                bool used = key < 0;
                foreach (StrFile file in files.Values)
                {
                    used = used || file.Contains(key);
                }
                if (used)
                {
                    key = 0xFFFE;
                    while (key > 0 && (this.KeyUsed(files, key) || (c == 1 && ids[0] == "_&" + (high | (uint) key).ToString(CultureInfo.InvariantCulture))))
                    {
                        key--;
                    }
                }
                foreach (KeyValuePair<string, StrFile> language in files)
                {
                    StrFile.Entry text = this.SourceString(language.Key, "List_CarMake.str", value);
                    if (text != null)
                    {
                        language.Value.Set(key, text.Text, text.Name);
                    }
                    else
                    {
                        language.Value.Set(key, Convert.ToString(source[1], CultureInfo.InvariantCulture), (c == 0 ? "IDS_DisplayName_" : "IDS_Profile_") + key.ToString(CultureInfo.InvariantCulture));
                    }
                }
                ids[c] = "_&" + (high | (uint) key).ToString(CultureInfo.InvariantCulture);
            }
            foreach (KeyValuePair<string, StrFile> language in files)
            {
                string zip = Path.Combine(targetTables, language.Key + ".zip");
                ForzaZip archive = new ForzaZip(this.Read(zip));
                archive.Replace(archive.Find("List_CarMake.str"), language.Value.ToBytes());
                Directory.CreateDirectory(Path.GetDirectoryName(this.Out(zip)));
                archive.Save(this.Out(zip));
                this.changedZips.Add(zip);
            }
            string normal = Convert.ToString(source[3], CultureInfo.InvariantCulture).Replace("Manufacturer_Logos_I" + (char) 92, "Manufacturer_Logos" + (char) 92);
            string small = Convert.ToString(source[4], CultureInfo.InvariantCulture);
            this.target.Execute("INSERT INTO List_CarMake (ID, DisplayName, ManufacturerCode, CountryID, IconPath, IconPathLight, Profile) VALUES (?, ?, ?, ?, ?, ?, ?)", make, ids[0], source[1], source[2], normal, small, ids[1]);

            // The logos: FM4's _I picture for both big folders, the _SM one for the small.
            GamePaths.GameFile big = this.LocateSource(Convert.ToString(source[3], CultureInfo.InvariantCulture));
            GamePaths.GameFile little = this.LocateSource(small);
            GamePaths.GameFile like = GamePaths.Locate(this.TargetRoot, Convert.ToString(this.target.Scalar("SELECT IconPath FROM List_CarMake WHERE ID <> ? LIMIT 1", make), CultureInfo.InvariantCulture));
            if (big == null || little == null || like == null || !like.InZip)
            {
                this.Line("make {0}: row added, logos not found (the make shows without its logo)", source[1]);
                return;
            }
            ForzaZip textures = new ForzaZip(this.Read(like.ZipPath));
            string prefix = like.EntryName.Substring(0, like.EntryName.IndexOf("Manufacturer_Logos", StringComparison.OrdinalIgnoreCase));
            string name = Path.GetFileName(big.InZip ? big.EntryName : big.FilePath);
            string smallName = Path.GetFileName(little.InZip ? little.EntryName : little.FilePath);
            byte[] bigData = Fm3Textures.FromMotorsport4(GamePaths.Read(big), name);
            byte[] smallData = Fm3Textures.FromMotorsport4(GamePaths.Read(little), name);
            foreach (string[] file in new string[][] { new string[] { prefix + "Manufacturer_Logos/" + name, "big" }, new string[] { prefix + "Manufacturer_Logos_I/" + name, "big" }, new string[] { prefix + "Manufacturer_Logos_SM/" + smallName, "small" } })
            {
                byte[] data = (file[1] == "big") ? bigData : smallData;
                if (textures.Find(file[0]) != null)
                {
                    textures.Replace(textures.Find(file[0]), data);
                }
                else
                {
                    textures.Add(file[0], data);
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(this.Out(like.ZipPath)));
            textures.Save(this.Out(like.ZipPath));
            this.changedZips.Add(like.ZipPath);
            this.Line("make {0} ({1}, id {2}): row added (country {3}), name and profile keys {4} / {5} in {6} languages, 3 logos in {7}", source[1], this.target.Scalar("SELECT ManufacturerCode FROM List_CarMake WHERE ID = ?", make), make, source[2], ids[0], ids[1], files.Count, Path.GetFileName(like.ZipPath));
        }

        private bool KeyUsed(Dictionary<string, StrFile> files, int key)
        {
            foreach (StrFile file in files.Values)
            {
                if (file.Contains(key))
                {
                    return true;
                }
            }
            return false;
        }

        private GamePaths.GameFile LocateSource(string gamePath)
        {
            return ((this.PackRoot == null) ? null : GamePaths.Locate(this.PackRoot, gamePath)) ?? GamePaths.Locate(this.SourceRoot, gamePath);
        }

        // The entry of a source string value ("_&n") in the table of the language, the pack's table first, then FM4's; English when the language is missing.
        private StrFile.Entry SourceString(string language, string table, string value)
        {
            uint stringId;
            if (!value.StartsWith("_&", StringComparison.Ordinal) || !uint.TryParse(value.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out stringId))
            {
                return null;
            }
            int key = (int) (stringId & 0xFFFF);
            foreach (string lang in new string[] { language, "EN" })
            {
                foreach (string root in new string[] { this.PackRoot, this.SourceRoot })
                {
                    string zip = (root == null) ? null : Path.Combine(Path.Combine(Path.Combine(root, "Media"), "StringTables"), lang + ".zip");
                    if (zip == null || !File.Exists(zip))
                    {
                        continue;
                    }
                    ForzaZip archive = new ForzaZip(zip);
                    ForzaZip.Entry entry = archive.Find(table);
                    if (entry == null)
                    {
                        continue;
                    }
                    foreach (StrFile.Entry text in new StrFile(archive.Read(entry)).Entries)
                    {
                        if (text.Key == key)
                        {
                            return text;
                        }
                    }
                }
            }
            return null;
        }

        #endregion

        #region Thumbnail

        // The car's picture in the car list: FM4's thumbnail (768x288) fitted into the size of the donor's FM3 one (512x256, the
        // rest transparent), named by the new car id, in the donor's folder of UI\Textures.zip; Data_Car.Thumbnail points at it.
        private void PortThumbnail()
        {
            string thumbnail = this.source.Scalar("SELECT Thumbnail FROM Data_Car WHERE Id = ?", this.carId) as string;
            string donorThumbnail = this.donorDb.Scalar("SELECT Thumbnail FROM Data_Car WHERE Id = ?", this.donorId) as string;
            GamePaths.GameFile from = (this.PackRoot == null ? null : GamePaths.Locate(this.PackRoot, thumbnail)) ?? GamePaths.Locate(this.SourceRoot, thumbnail);
            // a template car of a package has its picture in the package: the size and place are then those of a car of the FM3 folder
            GamePaths.GameFile like = GamePaths.Locate(this.TargetRoot, donorThumbnail) ?? GamePaths.Locate(this.TargetRoot, this.FolderCar()[2]);
            if (from == null || like == null || !like.InZip)
            {
                this.Line("thumbnail: not found, skipped (the row keeps the donor's picture)");
                this.target.Execute("UPDATE Data_Car SET Thumbnail = ? WHERE Id = ?", donorThumbnail, this.carId);
                return;
            }
            XdsTexture picture = new XdsTexture(GamePaths.Read(from));
            XdsTexture model = new XdsTexture(GamePaths.Read(like));
            XdsTexture result = XdsTexture.Create(model.Width, model.Height, DxtFormat.A8R8G8B8, false, model.LevelCount);
            byte[] pixels = picture.DecodeLevel(0);
            int pw = picture.GetLevel(0).Width;
            int ph = picture.GetLevel(0).Height;
            double scale = Math.Min((double) model.Width / pw, (double) model.Height / ph);
            int nw = (int) Math.Round(pw * scale);
            int nh = (int) Math.Round(ph * scale);
            int ox = (model.Width - nw) / 2;
            int oy = (model.Height - nh) / 2;
            byte[] level = new byte[model.Width * model.Height * 4];
            for (int y = 0; y < nh; y++)
            {
                for (int x = 0; x < nw; x++)
                {
                    double fx = ((x + 0.5) / scale) - 0.5;
                    double fy = ((y + 0.5) / scale) - 0.5;
                    int x0 = Math.Max(0, Math.Min(pw - 1, (int) Math.Floor(fx)));
                    int y0 = Math.Max(0, Math.Min(ph - 1, (int) Math.Floor(fy)));
                    int x1 = Math.Min(pw - 1, x0 + 1);
                    int y1 = Math.Min(ph - 1, y0 + 1);
                    double tx = Math.Max(0, Math.Min(1, fx - x0));
                    double ty = Math.Max(0, Math.Min(1, fy - y0));
                    for (int c = 0; c < 4; c++)
                    {
                        double top = (pixels[((y0 * pw) + x0) * 4 + c] * (1 - tx)) + (pixels[((y0 * pw) + x1) * 4 + c] * tx);
                        double bottom = (pixels[((y1 * pw) + x0) * 4 + c] * (1 - tx)) + (pixels[((y1 * pw) + x1) * 4 + c] * tx);
                        level[((((oy + y) * model.Width) + ox + x) * 4) + c] = (byte) Math.Round((top * (1 - ty)) + (bottom * ty));
                    }
                }
            }
            int w = model.Width;
            int h = model.Height;
            for (int l = 0; l < model.LevelCount; l++)
            {
                result.WriteLevelBlocks(l, level);
                int nextW = Math.Max(1, w / 2);
                int nextH = Math.Max(1, h / 2);
                byte[] next = new byte[nextW * nextH * 4];
                for (int y = 0; y < nextH; y++)
                {
                    for (int x = 0; x < nextW; x++)
                    {
                        for (int c = 0; c < 4; c++)
                        {
                            int sum = 0;
                            for (int k = 0; k < 4; k++)
                            {
                                sum += level[((((Math.Min(h - 1, (y * 2) + (k >> 1))) * w) + Math.Min(w - 1, (x * 2) + (k & 1))) * 4) + c];
                            }
                            next[(((y * nextW) + x) * 4) + c] = (byte) ((sum + 2) / 4);
                        }
                    }
                }
                level = next;
                w = nextW;
                h = nextH;
            }
            string fileName = "Thumbnail_" + this.carId.ToString(CultureInfo.InvariantCulture) + ".xds";
            string folder = (donorThumbnail.IndexOf((char) 92) >= 0) ? donorThumbnail.Substring(0, donorThumbnail.LastIndexOf((char) 92) + 1) : "game:" + (char) 92 + "Media" + (char) 92 + "UI" + (char) 92 + "Textures" + (char) 92 + "Thumbnails" + (char) 92;
            this.target.Execute("UPDATE Data_Car SET Thumbnail = ? WHERE Id = ?", folder + fileName, this.carId);
            ForzaZip textures = new ForzaZip(this.Read(like.ZipPath));
            string entry = like.EntryName.Substring(0, like.EntryName.LastIndexOf('/') + 1) + fileName;
            if (textures.Find(entry) != null)
            {
                textures.Replace(textures.Find(entry), result.ToBytes());
            }
            else
            {
                textures.Add(entry, result.ToBytes());
            }
            Directory.CreateDirectory(Path.GetDirectoryName(this.Out(like.ZipPath)));
            textures.Save(this.Out(like.ZipPath));
            this.changedZips.Add(like.ZipPath);
            this.Line("thumbnail: {0}x{1} -> {2}x{3} ({4} levels), {5} in {6}", pw, ph, model.Width, model.Height, model.LevelCount, entry, Path.GetFileName(like.ZipPath));
        }

        #endregion

        #region Sound

        // FM3 gives a car its sound by name: <name>_ET.xml in Audio\cars\Engines\EngineTuning.zip (engine settings and the harmonic
        // tuning files, banks and volumes it uses) and <name>_CMT.xml in Audio\cars\CarModelTuning.zip. FM4's engine tuning embeds its
        // harmonic tunings and points at FM4's banks, so it is not converted yet: the car takes the files of an FM3 car under its own name.
        // Default: the same car in FM3 when there is one (its own sound), else the donor's.
        private void PortSound()
        {
            string sound = this.SoundDonorMediaName;
            bool convert = false;
            if (sound == null)
            {
                bool inFm3 = this.inFm3;
                convert = !inFm3;   // a car FM3 lacks gets FM4's own tuning files converted, not another car's
                sound = inFm3 ? this.MediaName : this.DonorMediaName;
            }
            string[][] kinds =
            {
                new string[] { "Engines", "EngineTuning.zip", "_ET.xml" },
                new string[] { null, "CarModelTuning.zip", "_CMT.xml" }
            };
            foreach (string[] kind in kinds)
            {
                string path = Path.Combine(Path.Combine(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "Audio"), "cars"), kind[0] == null ? kind[1] : Path.Combine(kind[0], kind[1]));
                if (!File.Exists(path))
                {
                    this.Line("sound: {0} not found, skipped", kind[1]);
                    continue;
                }
                ForzaZip zip = new ForzaZip(this.Read(path));
                if (convert && kind[2] == "_ET.xml")
                {
                    this.PortEngineTunings(zip, kind, path);
                    continue;
                }
                byte[] data = convert ? this.ConvertedTuning(zip, kind, this.MediaName, this.carName) : null;
                if (data == null)
                {
                    ForzaZip.Entry file = zip.Find(sound + kind[2]);
                    if (file == null)
                    {
                        this.Line("sound: {0} has no {1}{2}, the car stays silent", kind[1], sound, kind[2]);
                        continue;
                    }
                    data = zip.Read(file);
                    this.Line("sound: {0}{1} = FM3's {2}{1}", this.carName, kind[2], sound);
                }
                string name = this.carName + kind[2];
                if (zip.Find(name) != null)
                {
                    zip.Replace(zip.Find(name), data);
                }
                else
                {
                    zip.Add(name, data);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(this.Out(path)));
                zip.Save(this.Out(path));
                this.changedZips.Add(path);
            }
        }

        // The game looks the engine tuning up by the ENGINE's MediaName (Data_Engine), not the car's: the C1 uses CIT_DS3_11_ET.xml, the DS3's. Every engine of the
        // car that FM3 has no tuning for gets FM4's file of that engine name, converted (found by the DLC validator on the first FM3 pack, 2026-11-02).
        private void PortEngineTunings(ForzaZip zip, string[] kind, string path)
        {
            bool changed = false;
            foreach (object[] row in this.target.Query("SELECT DISTINCT e.MediaName FROM List_UpgradeEngine u JOIN Data_Engine e ON e.EngineID = u.EngineID WHERE u.Ordinal = ?", this.carId).Rows)
            {
                string media = row[0] as string;
                if (media == null)
                {
                    continue;
                }
                if (zip.Find(media + kind[2]) != null)
                {
                    this.Line("sound: {0}{1} is FM3's own", media, kind[2]);
                    continue;
                }
                byte[] data = this.ConvertedTuning(zip, kind, media, media);
                if (data == null)
                {
                    this.Line("sound: WARNING no engine tuning for {0}: the engine stays silent", media);
                    continue;
                }
                zip.Add(media + kind[2], data);
                changed = true;
            }
            // Also under the car's own name (what this port wrote before): harmless, and right if some path looks it up by car.
            if (zip.Find(this.carName + kind[2]) == null)
            {
                byte[] own = this.ConvertedTuning(zip, kind, this.MediaName, this.carName);
                if (own != null)
                {
                    zip.Add(this.carName + kind[2], own);
                    changed = true;
                }
            }
            if (changed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(this.Out(path)));
                zip.Save(this.Out(path));
                this.changedZips.Add(path);
            }
        }

        // FM4's <car>_ET.xml / <car>_CMT.xml cut to the elements and attributes FM3's own files have (FM4 added HarmonicTunings, cylinder and
        // blow-off settings, the electric motor volume), or null when FM4 has none. For the engine tuning the harmonic tuning files and sound
        // banks it points at are made sure to exist in FM3.
        private byte[] ConvertedTuning(ForzaZip fm3Zip, string[] kind, string fm4Name, string fm3Name)
        {
            ForzaZip fm4;
            ForzaZip.Entry entry = FindEntry(this.SourceAudioZips(kind[0] ?? string.Empty, kind[1]), fm4Name + kind[2], out fm4);
            if (entry == null)
            {
                this.Line("sound: FM4 has no {0}{1}", fm4Name, kind[2]);
                return null;
            }
            System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
            doc.LoadXml(Encoding.UTF8.GetString(fm4.Read(entry)).TrimStart('﻿'));
            HashSet<string> shape = XmlShape(fm3Zip, kind[2]);
            int cut = Prune(doc.DocumentElement, string.Empty, shape);
            this.Line("sound: {0}{1} converted from FM4's {2}{1} ({3} elements/attributes FM3 does not have left out)", fm3Name, kind[2], fm4Name, cut);
            if (kind[2] == "_ET.xml")
            {
                this.HarmonicTunings(doc);
            }
            return XmlBytes(doc);
        }

        private static byte[] XmlBytes(System.Xml.XmlDocument doc)
        {
            MemoryStream stream = new MemoryStream();
            System.Xml.XmlWriterSettings settings = new System.Xml.XmlWriterSettings();
            settings.OmitXmlDeclaration = true;
            settings.Indent = true;
            settings.IndentChars = "  ";
            settings.Encoding = new UTF8Encoding(false);
            using (System.Xml.XmlWriter writer = System.Xml.XmlWriter.Create(stream, settings))
            {
                doc.Save(writer);
            }
            return stream.ToArray();
        }

        // "/Engine/EngineSettings" and "/Engine/EngineSettings@audio_rpm_idle" for every element and attribute of the files ending with suffix.
        private static HashSet<string> XmlShape(ForzaZip zip, string suffix)
        {
            HashSet<string> shape = new HashSet<string>();
            foreach (ForzaZip.Entry entry in zip.Entries)
            {
                if (!entry.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
                try
                {
                    doc.LoadXml(Encoding.UTF8.GetString(zip.Read(entry)).TrimStart('﻿'));
                }
                catch (System.Xml.XmlException)
                {
                    continue;
                }
                Shape(doc.DocumentElement, string.Empty, shape);
            }
            return shape;
        }

        private static void Shape(System.Xml.XmlElement element, string parent, HashSet<string> shape)
        {
            string path = parent + "/" + element.Name;
            shape.Add(path);
            foreach (System.Xml.XmlAttribute attribute in element.Attributes)
            {
                shape.Add(path + "@" + attribute.Name);
            }
            foreach (System.Xml.XmlNode child in element.ChildNodes)
            {
                if (child is System.Xml.XmlElement)
                {
                    Shape((System.Xml.XmlElement) child, path, shape);
                }
            }
        }

        // Removes what shape lacks; the number of elements and attributes removed.
        private static int Prune(System.Xml.XmlElement element, string parent, HashSet<string> shape)
        {
            string path = parent + "/" + element.Name;
            int cut = 0;
            foreach (System.Xml.XmlAttribute attribute in new List<System.Xml.XmlAttribute>(element.Attributes.Cast<System.Xml.XmlAttribute>()))
            {
                if (!shape.Contains(path + "@" + attribute.Name))
                {
                    element.Attributes.Remove(attribute);
                    cut++;
                }
            }
            foreach (System.Xml.XmlElement child in new List<System.Xml.XmlElement>(element.ChildNodes.OfType<System.Xml.XmlElement>()))
            {
                if (!shape.Contains(path + "/" + child.Name))
                {
                    element.RemoveChild(child);
                    cut++;
                }
                else
                {
                    cut += Prune(child, path, shape);
                }
            }
            return cut;
        }

        // The pack's file first, then the FM4 folder's.
        private string FindSourceMedia(string folder, string name)
        {
            string found = (this.PackRoot == null) ? null : GamePaths.FindMedia(this.PackRoot, folder, name);
            return found ?? GamePaths.FindMedia(this.SourceRoot, folder, name);
        }

        // The zips of Media\Audio\cars\<sub folder>\<zip name> that exist in the pack and in the FM4 folder, the pack's first.
        private List<ForzaZip> SourceAudioZips(string folder, string zip)
        {
            List<ForzaZip> found = new List<ForzaZip>();
            foreach (string root in new string[] { this.PackRoot, this.SourceRoot })
            {
                if (root != null && File.Exists(Path.Combine(this.AudioFolder(root, folder), zip)))
                {
                    found.Add(new ForzaZip(Path.Combine(this.AudioFolder(root, folder), zip)));
                }
            }
            return found;
        }

        private static ForzaZip.Entry FindEntry(List<ForzaZip> zips, string name, out ForzaZip zip)
        {
            foreach (ForzaZip candidate in zips)
            {
                ForzaZip.Entry entry = candidate.Find(name);
                if (entry != null)
                {
                    zip = candidate;
                    return entry;
                }
            }
            zip = null;
            return null;
        }

        private string AudioFolder(string root, string folder)
        {
            return Path.Combine(Path.Combine(Path.Combine(Path.Combine(root, "Media"), "Audio"), "cars"), folder);
        }

        // The harmonic tuning files an engine tuning names (…_HT.xml): FM3's own are kept (other FM3 cars use them), the ones FM3 lacks are
        // added from FM4's HarmonicTuning.zip cut to FM3's shape; every one needs its sound bank (the Soundbank element's name) in FM3's
        // Soundbanks folders, copied from FM4's when missing (both games use FSB4 with XMA audio).
        private void HarmonicTunings(System.Xml.XmlDocument doc)
        {
            string fm3Path = Path.Combine(this.AudioFolder(this.TargetRoot, "Engines"), "HarmonicTuning.zip");
            if (!File.Exists(fm3Path))
            {
                this.Line("sound: FM3 has no HarmonicTuning.zip, harmonic tunings not checked");
                return;
            }
            ForzaZip fm3 = new ForzaZip(this.Read(fm3Path));
            List<ForzaZip> fm4 = this.SourceAudioZips("Engines", "HarmonicTuning.zip");
            HashSet<string> shape = null;
            SortedDictionary<string, bool> names = new SortedDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Xml.XmlAttribute attribute in doc.SelectNodes("//@*"))
            {
                if (attribute.Value.EndsWith("_HT.xml", StringComparison.OrdinalIgnoreCase))
                {
                    names[attribute.Value] = true;
                }
            }
            bool added = false;
            foreach (string name in names.Keys)
            {
                byte[] data = null;
                ForzaZip.Entry have = fm3.Find(name);
                if (have != null)
                {
                    data = fm3.Read(have);
                }
                else
                {
                    ForzaZip fromZip;
                    ForzaZip.Entry from = FindEntry(fm4, name, out fromZip);
                    if (from == null)
                    {
                        this.Line("sound: WARNING {0} is in neither game's HarmonicTuning.zip", name);
                        continue;
                    }
                    if (shape == null)
                    {
                        shape = XmlShape(fm3, "_HT.xml");
                    }
                    System.Xml.XmlDocument ht = new System.Xml.XmlDocument();
                    ht.LoadXml(Encoding.UTF8.GetString(fromZip.Read(from)).TrimStart('﻿'));
                    Prune(ht.DocumentElement, string.Empty, shape);
                    data = XmlBytes(ht);
                    fm3.Add(name, data);
                    added = true;
                    this.Line("sound: harmonic tuning {0} not in FM3, added from FM4", name);
                }
                System.Xml.XmlDocument parsed = new System.Xml.XmlDocument();
                parsed.LoadXml(Encoding.UTF8.GetString(data).TrimStart('﻿'));
                string bank = parsed.DocumentElement.GetAttribute("name");
                if (bank.Length > 0)
                {
                    this.EnsureBank(bank);
                }
            }
            if (added)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(this.Out(fm3Path)));
                fm3.Save(this.Out(fm3Path));
                this.changedZips.Add(fm3Path);
            }
        }

        // The sound bank file in FM3's Engines\Soundbanks tree (under OutputRoot or the FM3 folder), else copied from FM4's tree into the same sub folder.
        private void EnsureBank(string bank)
        {
            string fm3Root = Path.Combine(this.AudioFolder(this.TargetRoot, "Engines"), "Soundbanks");
            string fm4Root = null;
            string from = null;
            string outRoot = this.Out(fm3Root);
            if (FindFile(fm3Root, bank) != null || FindFile(outRoot, bank) != null)
            {
                return;
            }
            foreach (string root in new string[] { this.PackRoot, this.SourceRoot })
            {
                string tree = (root == null) ? null : Path.Combine(this.AudioFolder(root, "Engines"), "Soundbanks");
                from = (tree == null) ? null : FindFile(tree, bank);
                if (from != null)
                {
                    fm4Root = tree;
                    break;
                }
            }
            if (from == null)
            {
                this.Line("sound: WARNING sound bank {0} is in neither game", bank);
                return;
            }
            string folder = Path.GetDirectoryName(from).Substring(Path.GetFullPath(fm4Root).TrimEnd((char) 92).Length).Trim((char) 92);
            string destination = fm3Root;
            foreach (string part in folder.Split(new char[] { (char) 92 }, StringSplitOptions.RemoveEmptyEntries))
            {
                string existing = Directory.Exists(destination) ? Array.Find(Directory.GetDirectories(destination), delegate(string d) { return string.Equals(Path.GetFileName(d), part, StringComparison.OrdinalIgnoreCase); }) : null;
                destination = (existing != null) ? existing : Path.Combine(destination, part);
            }
            string target = Path.Combine(this.Out(destination), bank);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(from, target, true);
            this.Line("sound: bank {0} not in FM3, copied from FM4 ({1} bytes) to {2}", bank, new FileInfo(from).Length, folder);
        }

        private static string FindFile(string root, string name)
        {
            if (!Directory.Exists(root))
            {
                return null;
            }
            string[] found = Directory.GetFiles(root, name, SearchOption.AllDirectories);
            return (found.Length > 0) ? found[0] : null;
        }

        #endregion

        #region Files

        private string Db(string root)
        {
            return Path.Combine(Path.Combine(Path.Combine(root, "Media"), "db"), "gamedb.slt");
        }

        // A target file as an earlier step of this port left it under OutputRoot, else the FM3 folder's.
        private string Read(string path)
        {
            string output = this.Out(path);
            return File.Exists(output) ? output : path;
        }

        private string Out(string path)
        {
            string full = Path.GetFullPath(path);
            string root = Path.GetFullPath(this.TargetRoot).TrimEnd((char) 92) + (char) 92;
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(path + " is not under the FM3 folder.");
            }
            return Path.Combine(this.OutputRoot, full.Substring(root.Length));
        }

        private void Write(string path, byte[] data)
        {
            string output = this.Out(path);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllBytes(output, data);
        }

        private object Need(object value, string error)
        {
            if (value == null)
            {
                throw new InvalidOperationException(error);
            }
            return value;
        }

        private void Line(string format, params object[] args)
        {
            this.Report.AppendLine(string.Format(CultureInfo.InvariantCulture, format, args));
        }

        // Port timings, as CarPort writes them: one per step, restarting the stopwatch.
        internal static void Lap(System.Diagnostics.Stopwatch lap, List<KeyValuePair<string, double>> times, string step)
        {
            times.Add(new KeyValuePair<string, double>(step, lap.Elapsed.TotalSeconds));
            lap.Reset();
            lap.Start();
        }

        // The slowest steps first, those under a quarter of a second left out (as CarPort's report).
        private void Times(List<KeyValuePair<string, double>> times)
        {
            List<KeyValuePair<string, double>> slow = times.FindAll(delegate(KeyValuePair<string, double> t) { return t.Value >= 0.25; });
            slow.Sort(delegate(KeyValuePair<string, double> a, KeyValuePair<string, double> b) { return b.Value.CompareTo(a.Value); });
            foreach (KeyValuePair<string, double> item in slow)
            {
                this.Line("time: {0} {1:0.0} s", item.Key, item.Value);
            }
        }

        #endregion
    }
}
