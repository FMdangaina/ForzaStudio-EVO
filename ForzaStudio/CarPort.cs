namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Xml;

    // Brings a car from Forza Horizon 2 into Forza Motorsport 4, as a new car (measured on the cars
    // both games have; see the notes on each step):
    //   model     the FH2 .carbin files converted to FM4 (CarbinModel.ConvertHorizonToMotorsport4),
    //             FH2-only materials renamed to FM4 ones; brakes (type 1) converted too (ConvertBrake);
    //   files     textures, livery masks, MAX data, animations and gauges as they are (the digital
    //             dash .fbf converted to the FM4 Anark layout, ConvertAnarkLayout); CarAttribs.xml rebuilt in the FM4 version-17
    //             form from the donor's; ShaderSettings.xml cut to the materials and parameters FM4 has;
    //   wheel     the stock wheel's zip converted the same way;
    //   database  every row of the car (FM4 and FH2 share almost all table layouts) plus its engine,
    //             drivetrain, torque curve and wheel rows; PI mapped to the FM4 scale, class from it;
    //             the car gets an Id of its own from 4000 up (DbIds, user 2026-10-20), checked with its
    //             rows before anything is written;
    //   strings   the car's names added to Data_Car.str in every FM4 language;
    //   sound     the engine tuning and car model tuning XML (FH2 reuses FM4 engine samples);
    //   thumbnail the car's thumbnail added to the UI textures;
    //   manifest  every rewritten or new zip listed in zipmanifest.xml.
    // Every file the port replaces is backed up first (<exe>\bak\<stamp>_bak_<name>).
    //
    // Forza Motorsport 3 as the source (2026-10-16, measured on the 315 cars FM3 and FM4 share, same ids):
    //   model     CarbinModel.ConvertMotorsport3ToMotorsport4 (header, sections, 40/16/44-byte vertices);
    //             brakes are caliperlf.carbin in FM3, caliperlf_lod0.carbin in FM4;
    //   textures  colour textures marked gamma, as FM4 does (ConvertMotorsport3Texture);
    //   database  FM4 kept FM3's ids (cars, engines, wheels but two); what FM3 lacks is filled as FM4 did
    //             on the shared cars (FillMotorsport4Columns), torque curves get FM4 ids and the
    //             zero-throttle curve becomes ZeroThrottleTorqueScale (ConvertMotorsport3TorqueCurves);
    //   make      Porsche (MakeID 39), which FM4 lacks, added with its names and livery decals (PortMake);
    //   dash      FM3 keeps the digital dash outside the car: Data_Car.GaugeID -> List_DigitalGauges ->
    //             Media\DigitalGauges\<name>.zip, textures in Media\UI\Textures.zip ui4\dashboards. FM4 has
    //             GaugeID -1 on every car and reads digitalgauge\dash_<car>.* from the car zip; the port
    //             puts the dash there (ConvertMotorsport3Dash) and sets GaugeID -1 (AddMotorsport3Dash).
    public class CarPort
    {
        public string SourceRoot;       // FH2 (or FH) game folder (with media)
        public string TargetRoot;       // FM4 game folder
        public string MediaName;        // the car, e.g. MCL_P1_13
        public string DonorMediaName;   // an FM4 car close to it, e.g. MCL_MP412C_11 (brakes, templates)
        public string DonorRoot;        // the donor is a car of an installed DLC pack: that pack laid out as a game folder (found by ResolveDonor), else null
        public string Tires = "normal"; // "normal": the source's own tire models (BuildTireZips); "offroad": FM4's Hummer H1 tire models under the car's name
        public string RenameTo;         // a car FM4 already has, ported beside it under this MediaName (for tests); null = the source's name
        public string DisplaySuffix;    // text added to the car's DisplayName / ModelShort when renamed, e.g. " FH"
        public bool DeferIntegrity;     // a pack trial: the database rules are checked once for the whole pack, not after every car
        public bool Arcade = true;      // Data_Car.IsArcade
        public long? FirstCarId;        // the first Id to try for the car (a DLC pack's, chosen by the user); null = Port.ini InstallFirstCar (3000)
        public long? MakeId;          // an FM4 make (List_CarMake.ID) to put the car under instead of the source's
        public string SoundFrom;        // an FM4 car whose engine sound setup to use instead of the source's
        public string OutputRoot;       // trial run: files written here (same layout) instead of TargetRoot;
                                        // a file already there (an earlier car of the same DLC pack) is
                                        // read instead of the target's, so the cars of a pack chain
        public string XeniaContent;     // Xenia's FM4 content folder: car Ids of the installed packs stay theirs (DbIds.CarId)
        public string BackupFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bak");
        public StringBuilder Report = new StringBuilder();

        // Review step: what the port found not regular for FM4, and the user's fixes for the next run.
        // A database issue is a value of a row the port wrote that points at a row FM4 lacks, on a
        // relation that has no such value in the original FM4 (GameDbSchema strict), e.g. FH-only
        // FamilyBodyID 27 on the P1. A material issue is a model material no FM4 car uses.
        public class Choice
        {
            public object Value;
            public string Label;
            public bool Recommended;        // drawn in green in the review

            public override string ToString()
            {
                return this.Label;
            }
        }

        public class Issue
        {
            public string Key;              // Fixes key: "Table.Column=value" or "material:name"
            public string Kind;             // "database" or "material"
            public string Where;            // Data_Car.FamilyBodyID, or the model sections
            public string Value;
            public string Problem;
            public List<Choice> Choices = new List<Choice>();
            public object Suggested;        // the choice to preselect, or null
        }

        public List<Issue> Issues = new List<Issue>();
        public Dictionary<string, object> Fixes = new Dictionary<string, object>();
        private readonly List<string[]> writtenRows = new List<string[]>();   // table, where of every row the port wrote
        private readonly Dictionary<string, List<string>> unknownMaterials = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        // The car's name in the target: its own MediaName (the source's) unless it is ported under another (RenameTo).
        private string CarName
        {
            get { return this.RenameTo ?? this.MediaName; }
        }

        private string stamp;
        private bool skipThumbnail;    // Textures.zip locked by the running emulator
        private string game = "FH2";    // the source game in the report: FH (Forza Horizon writes the same files), FH2 or FM3
        private bool motorsport3;       // the source is Forza Motorsport 3
        private bool traffic;           // an FH traffic car: its close-up models were placeholders (RebuildCloseUpModels)
        private SqliteDatabase source;
        private SqliteDatabase target;
        private long carId;
        private long donorId;
        private SqliteDatabase donorDb;  // the donor pack's rows (DonorRoot), when the donor is not in the target database
        private long? newMake;          // a make the port added to List_CarMake (PortMake)
        private string titleRule;       // the new make's Drive_ rule for the badge rules (PortTitles), written with the files
        private string titleBase;       // the new make's logo name, the rule's name
        private string motorsport3Dash; // List_DigitalGauges.MediaName of an FM3 car's dash, or null
        private readonly List<string> engineMedia = new List<string>();
        private bool fm2Stage;
        private double fm2Pi = -1;                  // the FM2 car's PerformanceIndex (FM2 stage), -1 = none
        private List<double[]> fm2PiPairs;          // (FM2 PI, FM4 PI) of the cars both games have
        private readonly List<long> wheelIds = new List<long>();
        private readonly List<long> originalWheels = new List<long>();   // wheels FM4 already had before the port
        private readonly Dictionary<string, ForzaZip> changedZips = new Dictionary<string, ForzaZip>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> newZips = new List<string>();
        private readonly List<string> checkedBanks = new List<string>();   // sound banks FM4 already has (compared once)
        private List<Choice> tuningChoices;   // complete FM4 engine tunings for the review (EngineTuningChoices)
        private Dictionary<string, TuningBanks> tuningBanks;   // their sound banks (SameBanksTuning)
        private ForzaZip carZip;        // the converted car, saved last with port_log.txt in it
        private string carZipPath;
        private readonly Dictionary<string, string> newFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // loose target file -> source file
        private readonly List<string> donorContent = new List<string>();   // what the car took from the donor (DonorReport)
        private readonly List<string> donorFormat = new List<string>();    // donor files used only as a layout model

        // FH2 materials FM4 has no shader for -> FM4 materials (null: the subsection goes).
        private static readonly Dictionary<string, string> MaterialMap = BuildMaterialMap();

        private static Dictionary<string, string> BuildMaterialMap()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            map["taillight2S"] = "tail_light";
            map["taillightst"] = "tail_light";
            map["xenonhead"] = "head_light";
            map["drlwhite"] = "head_light";
            map["drlinlorange"] = "detail_glass_amber";
            map["indicator_left"] = "detail_glass_amber";
            map["indicator_right"] = "detail_glass_amber";
            // Alpha-cut cockpit icons: on the cars both games have, FM4's badge holds FH2's badge plus
            // these (X5M 286 + 573 indices -> 860); gauge_emissive has no alpha and shows the quads.
            map["z_gauge_emissive_alpha"] = "badge";
            map["gauge_emissive_alpha"] = "badge";
            map["deprecated"] = null;

            // The FH1 / FH2 materials of all their cars that FM4 has no shader for, each with the FM4
            // material the compatibility list (MaterialSuggestions: what the name says the part is, the
            // shader similarity, the material FM4 uses on that part) agrees on. Measured on the 175 FH1, 216
            // FH2 and FM3 cars, 2026-10-18 (decided by the user: compatible ones automatic, the others in
            // the review with the best choice preselected).
            // Lights, by what the name says (detail_glass_amber: the amber lens of 148 FM4 cars; lights_amber renders black in game, checked 2026-10-25).
            foreach (string name in new string[] { "oldhead", "fullbeam", "hidhead", "fogwhite", "drlinlwhite", "drlinrwhite", "drlwhiteswitch", "drlwhiteoff" })
            {
                map[name] = "head_light";
            }
            foreach (string name in new string[] { "fogred", "linred", "rinred", "lindtailside", "rindtailside", "lindtailsideunique", "rindtailsideunique", "brinl", "brinr", "drlred", "drlinlred", "drlinrred" })
            {
                map[name] = "tail_light";
            }
            foreach (string name in new string[] { "slorange", "slinlorange", "slinrorange", "drlorange", "drlinrorange", "drlbrakeorange", "sidelightorange" })
            {
                map[name] = "detail_glass_amber";
            }
            map["brkreverse"] = "reverse_light";
            // Any name with "gls" in it is light glass (MapMaterial; user's rule 2026-10-18).
            // The number plate lamp (in the light sections): the most similar shader, reflector (74).
            map["numplate"] = "reflector";
            // The plate itself (`numberplate`, a quad in bumperFrameR / bumperFrameF) is NOT mapped: FM4
            // has no plate technique, so it stays red in the editor and the user picks the material by
            // hand (user 2026-10-20: traffic cars need outside 3D work, nothing automatic here).
            // The gauge range marks of FH cockpits are named after their section; FM4's are gauge_needle (238 cars).
            foreach (string gauge in new string[] { "tach", "speed", "fuel" })
            {
                foreach (string mark in new string[] { "_min", "_mid", "_max" })
                {
                    map[gauge + mark] = "gauge_needle";
                }
            }
            return map;
        }

        // MaterialMap, plus the name rules a list can't hold: any FH name with "gls" in it
        // (lights_gls_tail_light, ...) is the glass over a light, lights_glass.
        private static bool MapMaterial(string name, out string to)
        {
            if (MaterialMap.TryGetValue(name, out to))
            {
                return true;
            }
            if (name.IndexOf("gls", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                to = "lights_glass";
                return true;
            }
            return false;
        }

        // The port makes NO material decision on its own (user 2026-10-25: "togli ogni decisione automatica, segnaliamo
        // TUTTO"): a material FM4 does not use for that kind of file and LOD stays as the source has it, shows in the report
        // and in the review, and is renamed only by the user's choice. MaterialMap / MapMaterial remain as suggestions
        // (SuggestMaterial) for the rename dialog.
        private static bool AutoMap(string name, out string to)
        {
            to = null;
            return false;
        }

        // Every parameter name the FM4 cars' ShaderSettings.xml use (all 315 cars read). FH2 adds
        // ones FM4 has no constant for (TailLightColor, XenonHeadlightColor, DRLWhiteColor, ...).
        private static readonly string[] ShaderParameters = new string[]
        {
            "AmbientColor", "BrakeColor", "CarbonFiberDiffuseColor", "CarbonFiberDiffuseColor2", "CarbonFiberMipBias",
            "CarbonFiberScale", "CarbonFiberSpecularColor2", "CarbonFiberSpecularPower2", "ChromeAmbientScale",
            "ChromeDiffuseScale", "ChromeMaxDiffuseShadowAmt", "ChromeMaxReflectionShadowAmt", "ChromeUseSHLerp",
            "CockpitBumpBias", "CockpitBumpScale", "CockpitHeadlinerColor", "CockpitMetalPaintScale", "CockpitNormalScale",
            "CockpitPaintScale", "CockpitPlasticPaintScale", "CockpitSpecularAmount", "CockpitSpecularAmount2",
            "CockpitSpecularPower", "CockpitSpecularPower2", "CubeMultiplier", "CubeScale", "D3DRS_DepthBias",
            "D3DRS_SlopeScaleDepthBias", "DamageLightsSampler", "DarkValues", "DiffuseColorMultiply", "Direct",
            "DirtCylinderHeight", "DirtOffsetU", "DirtOffsetV", "DirtSpecularKillAmount", "DynamicEnvLightmapDampner",
            "FresnelBiasA", "FresnelIndex", "FresnelMax", "FresnelMin", "FresnelPower", "FresnelScalar", "FresnelScale",
            "GaugeAmbientAmt", "GaugeEmissiveScale", "Glancing", "GlassColor", "GlassMaxOpacity", "GrilleColor",
            "GrilleScale", "LightsSampler", "LightValues", "MinCockpitShadow", "MinCockpitSpecular", "MinDiffuse",
            "MinShadowValue", "MottleDark", "MottleLight", "MottleTile", "PaintColor", "PaintScale", "PristinePaintScale",
            "ReverseLightColor", "ShadowScale", "SHLowerScale", "SHTransition", "SHTransitionWidth", "SHUpperScale",
            "SpecularAmount", "SpecularAmount2", "SpecularAmount3", "SpecularColor", "SpecularCubeScalar", "SpecularPower",
            "StaticEnvLightmapDampner", "StitchColor", "UseDiffuseInsteadOfSH", "UV1Tile1",
        };

        public static string[] ShaderParameterNames
        {
            get { return (string[]) ShaderParameters.Clone(); }
        }

        public static bool IsShaderParameter(string name)
        {
            return Array.Exists(ShaderParameters, delegate(string p) { return string.Equals(p, name, StringComparison.OrdinalIgnoreCase); });
        }

        // The most tick marks an FM4 gauge has (Firebird Trans Am Speed: 19). The FH2 P1 repeats its
        // 4 speed and 3 tach marks 12 times (48 and 36), past what FM4 reads into its fixed table.
        private const int MaxTicks = 19;

        public void Run()
        {
            // An FM2 game folder is laid out as a stage first (Fm2Source: model on the donor's, wheel, thumbnail,
            // the donor's row renamed), in a temporary folder that goes away with the run.
            string original = this.SourceRoot;
            // A DLC package's car, wheels and textures are written from the package now, and the car's zip goes
            // again when the run ends (DlcSource.Materialize).
            bool sourceCarWritten = DlcSource.Materialize(original, this.MediaName);
            try
            {
                this.RunStaged(original);
            }
            finally
            {
                if (sourceCarWritten)
                {
                    DlcSource.Release(original, this.MediaName);
                }
            }
        }

        private void RunStaged(string original)
        {
            string tempStage = null;
            bool dlcRaw = DlcSource.IsFm2Raw(this.SourceRoot);
            if (GameInfo.Detect(this.SourceRoot) == ForzaGame.Motorsport2 && (!DlcSource.IsStage(this.SourceRoot) || dlcRaw) && GameInfo.Detect(this.TargetRoot) == ForzaGame.Motorsport4)
            {
                // An FM2 DLC package (stage with raw=fm2): the cars are its own, the rest (wheels, thumbnails, strings)
                // the FM2 game's, set in Tools > Settings.
                string fm2Game = dlcRaw ? GameInfo.ConfiguredFolder(ForzaGame.Motorsport2) : this.SourceRoot;
                if (fm2Game == null)
                {
                    throw new InvalidOperationException("An FM2 DLC package needs the Forza Motorsport 2 game: set its folder in Tools > Settings > Folders.");
                }
                if (GamePaths.FindCar(this.TargetRoot, this.DonorMediaName) == null)
                {
                    throw new InvalidOperationException("An FM2 car is built on a donor from the FM4 game folder itself, and " + this.DonorMediaName + " is not there (a DLC car?). Pick another donor.");
                }
                tempStage = Path.Combine(Path.GetTempPath(), "ForzaStudio_FM2_" + this.MediaName);
                List<string> stageLog = new List<string>();
                try
                {
                    Fm2Source.Stage(fm2Game, this.TargetRoot, this.MediaName, this.DonorMediaName, tempStage, stageLog, dlcRaw ? this.SourceRoot : null);
                }
                finally
                {
                    foreach (string line in stageLog)
                    {
                        this.Line("FM2 stage: {0}", line);
                    }
                }
                this.SourceRoot = tempStage;
            }
            try
            {
                this.RunCore();
            }
            finally
            {
                this.SourceRoot = original;
                if (tempStage != null && Directory.Exists(tempStage))
                {
                    try
                    {
                        Directory.Delete(tempStage, true);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }

        // The database rules (DbIntegrity) on a whole pack trial: FM4's own database against the trial's, after every car was
        // ported with DeferIntegrity. Returns the report lines; throws with the errors, as the check of a single car does.
        public static List<string> CheckPackIntegrity(string targetRoot, string trialRoot)
        {
            string relative = Path.Combine(Path.Combine("Media", "db"), "gamedb.slt");
            List<string> lines = new List<string>();
            using (SqliteDatabase was = SqliteDatabase.FromBytes(File.ReadAllBytes(Path.Combine(targetRoot, relative))))
            using (SqliteDatabase now = SqliteDatabase.FromBytes(File.ReadAllBytes(Path.Combine(trialRoot, relative))))
            {
                DbIntegrity.Result integrity = DbIntegrity.Compare(was, now, ForzaGame.Motorsport4, null, false);
                if (!integrity.Ok)
                {
                    throw new InvalidOperationException("Database integrity check of the whole pack failed:\r\n  " + string.Join("\r\n  ", integrity.Errors.ToArray()));
                }
                foreach (string line in integrity.Warnings)
                {
                    lines.Add("database warning: " + line);
                }
                lines.Add(string.Format(CultureInfo.InvariantCulture, "database integrity of the whole pack: the keys and the cars' rows hold ({0} tables changed)", integrity.Summary.Count));
            }
            return lines;
        }

        // The time since the last lap, kept as "step|seconds" for the timing lines at the end of a run.
        private static void Lap(System.Diagnostics.Stopwatch lap, List<string> times, string step)
        {
            times.Add(step + "|" + lap.Elapsed.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture));
            lap.Reset();
            lap.Start();
        }

        private void RunCore()
        {
            this.stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
            System.Diagnostics.Stopwatch lap = System.Diagnostics.Stopwatch.StartNew();
            List<string> times = new List<string>();
            ForzaGame sourceGame = GameInfo.Detect(this.SourceRoot);
            // FM2 only as a staged folder whose models and database are already in FM4 form (its .carbin
            // rebuilt on an FM4 donor's, its rows the donor's under the FM2 name): Fm2Source.Stage.
            bool fm2Stage = sourceGame == ForzaGame.Motorsport2 && DlcSource.IsStage(this.SourceRoot);
            if ((sourceGame != ForzaGame.Horizon2 && sourceGame != ForzaGame.Horizon1 && sourceGame != ForzaGame.Motorsport3 && !fm2Stage) || GameInfo.Detect(this.TargetRoot) != ForzaGame.Motorsport4)
            {
                throw new InvalidOperationException("The source must be a Forza Horizon, Forza Horizon 2 or Forza Motorsport 3 folder and the target a Forza Motorsport 4 folder.");
            }
            this.game = GameInfo.Tag(sourceGame);
            this.motorsport3 = sourceGame == ForzaGame.Motorsport3;
            this.fm2Stage = fm2Stage;
            string sourceDbPath = Path.Combine(Path.Combine(Path.Combine(this.SourceRoot, "Media"), "db"), "gamedb.slt");
            string targetDbPath = Path.Combine(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "db"), "gamedb.slt");
            this.source = SqliteDatabase.FromBytes(File.ReadAllBytes(sourceDbPath));
            byte[] targetBefore = File.ReadAllBytes(this.In(targetDbPath));
            this.target = SqliteDatabase.FromBytes(targetBefore);
            try
            {
                // The car gets an Id of its own (DbIds.CarId); the source copy in memory is renumbered
                // to it, so every step below reads and writes that Id.
                long sourceId = ToLong(this.source.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", this.MediaName), "the source database has no car " + this.MediaName);
                string reason;
                if (this.RenameTo != null)
                {
                    // Beside FM4's own car of that name: the in-memory source copy takes the new name, so the
                    // whole port below finds or makes the new car's rows and files (BuildCarZip renames the zip).
                    if (!CarDuplicate.IsValidName(this.RenameTo) || string.Equals(this.RenameTo, this.MediaName, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("The new MediaName must be 3-40 letters, digits or underscores and differ from " + this.MediaName + ".");
                    }
                    this.source.Execute("UPDATE Data_Car SET MediaName = ? WHERE Id = ?", this.RenameTo, sourceId);
                }
                DbIds.PackContent = this.XeniaContent;
                this.carId = DbIds.CarId(this.target, this.source, this.CarName, DbIds.PackDatabases(this.XeniaContent), this.FirstCarId ?? DbIds.InstallFirstCar, !this.FirstCarId.HasValue, out reason);
                this.ResolveDonor();
                this.donorId = ToLong(this.DonorScalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", this.DonorMediaName), "the target database has no car " + this.DonorMediaName);
                object existing = this.target.Scalar("SELECT MediaName FROM Data_Car WHERE Id = ?", this.carId);
                if (existing != null && !string.Equals((string) existing, this.CarName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(string.Format("Car id {0} is {1} in the target database.", this.carId, existing));
                }
                this.Line("car {0}{6}: id {1} ({2}; " + this.game + " id {3}), donor {4} (id {5})", this.MediaName, this.carId, reason, sourceId, this.DonorMediaName, this.donorId, (this.RenameTo != null) ? " ported as " + this.RenameTo : "");
                foreach (string line in DbIds.RenumberCar(this.source, sourceId, this.carId, this.fm2Stage))
                {
                    this.Line("  {0}", line);
                }
                if (this.fm2Stage)
                {
                    this.Fm2Data();
                }
                if (this.OutputRoot == null)
                {
                    this.CheckWritable(targetDbPath);
                }

                Lap(lap, times, "setup (ids against the installed packs, donor, renumbering)");
                this.PortDatabase();
                Lap(lap, times, "database");
                this.PortStrings();
                Lap(lap, times, "strings");
                this.PortTitles();
                Lap(lap, times, "titles");
                this.BuildCarZip();
                Lap(lap, times, "car zip");
                this.SteeringWheelPosition();
                this.ReviewMaterials();
                Lap(lap, times, "wheel position + material review");
                this.BuildWheelZips();
                this.BuildTireZips();
                Lap(lap, times, "wheel + tire zips");
                this.PortSound();
                Lap(lap, times, "sound");
                this.PortThumbnail();
                Lap(lap, times, "thumbnail");
                this.DonorReport();

                // C3: the car's rows are checked before anything is written.
                List<string> errors = DbIds.CheckCar(this.target, this.carId, sourceId, this.CarName);
                if (errors.Count > 0)
                {
                    throw new InvalidOperationException("Database check failed, nothing was written:\r\n  " + string.Join("\r\n  ", errors.ToArray()));
                }
                this.Line("database check: car {0} (Data_Car, Data_CarBody, the keys of its rows) OK", this.carId);
                Lap(lap, times, "donor report + row check");

                // The rules of the whole database (DbIntegrity): keys above FM4's largest and unique, a car's
                // rows complete. Ids that point at nothing are reported only: the rows come from another game.
                // A pack trial sets DeferIntegrity and runs the same rules once on the whole pack at the end (CheckPackIntegrity).
                if (!this.DeferIntegrity)
                {
                    using (SqliteDatabase was = SqliteDatabase.FromBytes(targetBefore))
                    {
                        DbIntegrity.Result integrity = DbIntegrity.Compare(was, this.target, ForzaGame.Motorsport4, null, false);
                        if (!integrity.Ok)
                        {
                            throw new InvalidOperationException("Database integrity check failed, nothing was written:\r\n  " + string.Join("\r\n  ", integrity.Errors.ToArray()));
                        }
                        foreach (string line in integrity.Warnings)
                        {
                            this.Line("database warning: {0}", line);
                        }
                        this.Line("database integrity: the keys and the car's rows hold ({0} tables changed)", integrity.Summary.Count);
                    }
                }
                Lap(lap, times, "database integrity");

                // Write everything: backups first, then files, then the manifest.
                this.Write(targetDbPath, this.target.ToBytes());
                Lap(lap, times, "write database");
                foreach (KeyValuePair<string, ForzaZip> pair in this.changedZips)
                {
                    string output = this.Out(pair.Key);
                    this.Backup(output);
                    pair.Value.Save(output);
                    this.Line("saved {0}", this.Relative(pair.Key));
                    Lap(lap, times, "save " + Path.GetFileName(pair.Key));
                }
                if (this.titleRule != null)
                {
                    this.WriteTitleRule();
                }
                foreach (KeyValuePair<string, string> file in this.newFiles)
                {
                    File.Copy(file.Value, this.Out(file.Key), false);
                    this.Line("added {0}", this.Relative(file.Key));
                }
                // The report travels with the car (FM4 ignores files in a car zip it does not ask for).
                string log = string.Format(CultureInfo.InvariantCulture, "ForzaStudio EVO car port, {0}\r\nsource {1} ({2})\r\ntarget {3}\r\n{4}\r\n",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), this.SourceRoot, this.game, this.TargetRoot,
                    (this.OutputRoot != null) ? "trial run into " + this.OutputRoot : "installed into the game") + "\r\n" + this.Report.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
                this.Put(this.carZip, "port_log.txt", Encoding.UTF8.GetBytes(log));
                this.SaveNew(this.carZip, this.carZipPath);
                this.Line("saved {0}", this.Relative(this.carZipPath));
                Lap(lap, times, "save car zip");
                this.UpdateManifest();
                Lap(lap, times, "manifest");
                // the slowest steps first, those under a quarter of a second left out
                List<KeyValuePair<string, double>> slow = new List<KeyValuePair<string, double>>();
                foreach (string entry in times)
                {
                    int at = entry.LastIndexOf('|');
                    double seconds = double.Parse(entry.Substring(at + 1), CultureInfo.InvariantCulture);
                    if (seconds >= 0.25)
                    {
                        slow.Add(new KeyValuePair<string, double>(entry.Substring(0, at), seconds));
                    }
                }
                slow.Sort(delegate(KeyValuePair<string, double> a, KeyValuePair<string, double> b) { return b.Value.CompareTo(a.Value); });
                foreach (KeyValuePair<string, double> item in slow)
                {
                    this.Line("time: {0} {1:0.0} s", item.Key, item.Value);
                }
            }
            finally
            {
                this.source.Dispose();
                this.target.Dispose();
            }
        }

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
            if (wheel != null && (this.motorsport3 || this.fm2Stage) && this.Motorsport3Wheel(from, to, Convert.ToInt64(wheel)))
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
            bool centred = camera.SetSteeringWheelFromModel(point);
            this.Line("  cockpit camera: CarPartPositions part 9 (steering wheel point) {0}, rebuilt from the cockpit model's steering_wheel", camera.HasSteeringWheel ? "is 0,0,0" : "missing");
            if (centred)
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
        private static readonly double[] SpringClassRatio = { 0.605, 0.748, 0.748, 0.805, 0.805, 0.857, 0.903, 0.941, 0.973, 1.0 };

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
        private static string ScaleSql(string table, string column, string key)
        {
            string d = "Def" + column;
            string n = "Min" + column;
            string x = "Max" + column;
            return "UPDATE " + table + " SET " + d + " = " + d + " * ?, " + n + " = CASE WHEN " + n + " = " + d + " THEN " + n + " * ? ELSE " + n + " END, " + x + " = CASE WHEN " + x + " = " + d + " THEN " + x + " * ? ELSE " + x + " END WHERE " + key + " BETWEEN ? AND ?";
        }

        // FM4 / FM3 damper and anti-roll ratios by FM4 class (medians on the cars both games have).
        private static readonly double[] DamperBumpRatio = { 1.03, 1.153, 1.153, 1.196, 1.196, 1.235, 1.267, 1.293, 1.315, 1.333 };
        private static readonly double[] DamperReboundRatio = { 0.773, 0.865, 0.865, 0.897, 0.897, 0.926, 0.95, 0.97, 0.987, 1.0 };
        private static readonly double[] SwayFrontRatio = { 0.844, 1.044, 1.044, 1.124, 1.124, 1.197, 1.261 };
        private static readonly double[] SwayRearRatio = { 0.839, 0.851, 0.851, 0.916, 0.916, 0.976, 1.027 };

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
                ForzaZip b = new ForzaZip(to);
                ForzaZip.Entry y = b.Find("buildnumber.txt");
                if (sourceBuild.Length == 0)
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

        #region Strings

        // DisplayName / ModelShort are "_&<id>": the low 16 bits are the key in Data_Car.str. FH2 and
        // FM4 numbered their strings apart, so an FH2 key can be another car's in FM4 (14 of the 73
        // FH2 cars FM4 lacks: FOR_TransitSSV_11's DDD8 is IDS_DisplayName_1076 there). Such a string
        // gets a key free in every language, and the database value follows it (same high bits); a
        // car ported before finds its string again by name (IDS_...).
        private void PortStrings()
        {
            this.PortStrings("Data_Car", "Id", this.carId, "DisplayName", "ModelShort");
            if (this.newMake.HasValue)
            {
                this.PortStrings("List_CarMake", "ID", this.newMake.Value, "DisplayName", "Profile");
            }
        }

        // The titles of a make the port added (MakeTitles): FM4's "<Make> Fan / Enthusiast / Aficionado" for it,
        // the unlock rows and strings now, the rule with the files (WriteTitleRule).
        private void PortTitles()
        {
            if (!this.newMake.HasValue)
            {
                return;
            }
            string logoBase = this.target.Scalar("SELECT IconPathBase FROM List_CarMake WHERE ID = ?", this.newMake.Value) as string;
            Dictionary<string, ForzaZip> zips = new Dictionary<string, ForzaZip>(StringComparer.OrdinalIgnoreCase);
            foreach (string zip in Directory.GetFiles(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "StringTables"), "*.zip"))
            {
                ForzaZip archive = this.OpenChanged(zip);
                if (archive.Find(MakeTitles.Table + ".str") != null)
                {
                    zips[zip] = archive;
                }
            }
            string name = null;
            string reference = this.target.Scalar("SELECT DisplayName FROM List_CarMake WHERE ID = ?", this.newMake.Value) as string;
            uint id;
            if (reference != null && reference.StartsWith("_&") && uint.TryParse(reference.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out id))
            {
                string english = Path.Combine(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "StringTables"), "EN.zip");
                ForzaZip.Entry table = this.OpenChanged(english).Find("List_CarMake.str");
                StrFile.Entry text = (table == null) ? null : Find(new StrFile(this.OpenChanged(english).Read(table)), (int) (id & 0xFFFF));
                name = (text == null) ? null : text.Text;
            }
            if (string.IsNullOrEmpty(logoBase) || string.IsNullOrEmpty(name))
            {
                this.Line("  titles: the make has no logo name or English name, none added");
                return;
            }
            StringBuilder lines = new StringBuilder();
            this.titleRule = MakeTitles.Add(this.target, this.newMake.Value, name, logoBase, zips, lines);
            this.titleBase = logoBase;
            foreach (string line in lines.ToString().Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                this.Line("{0}", line);
            }
            this.PortDecals(name);
        }

        // The livery decals of a make the port added (MakeDecals): when PortMake kept no Livery_DecalsSortOrder row (the
        // source's rows name decals FM4 lacks, as FH2's Bowler: its own logo decal), the make gets the common decals
        // and its own decal, the source's texture put on FM4's decal carbin. Without the source's decal file
        // nothing is added.
        private void PortDecals(string name)
        {
            if (!MakeDecals.Supported(this.target) || !MakeDecals.Supported(this.source)
                || Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM " + MakeDecals.Sort + " WHERE MakeID = ?", this.newMake.Value)) > 0)
            {
                return;
            }
            string file = this.source.Scalar("SELECT d.Filename FROM " + MakeDecals.Sort + " s JOIN " + MakeDecals.Table + " d ON d.ID = s.Livery_DecalID WHERE s.MakeID = ? AND s.Sequence = 1", this.newMake.Value) as string;
            string sourceLivery = MakeDecals.LiveryPath(this.SourceRoot);
            if (file == null || !File.Exists(sourceLivery))
            {
                this.Line("  decals: the source has no decal of its own for the make, none added");
                return;
            }
            ForzaZip from = new ForzaZip(sourceLivery);
            ForzaZip.Entry own = from.Find("decals/" + file.Substring(file.LastIndexOfAny(new char[] { '\\', '/' }) + 1));
            string livery = MakeDecals.LiveryPath(this.TargetRoot);
            if (own == null || !File.Exists(livery))
            {
                this.Line("  decals: the source's decal {0} is not in its Livery.zip, none added", file);
                return;
            }
            ForzaZip target = this.OpenChanged(livery);
            ForzaZip.Entry model = target.Find(MakeDecals.TemplateEntry);
            if (model == null)
            {
                this.Line("  decals: Livery.zip has no {0} to build the decal on, none added", MakeDecals.TemplateEntry);
                return;
            }
            byte[] carbin = MakeDecals.BuildCarbin(MakeDecals.ReadTexels(from.Read(own)), target.Read(model));
            Dictionary<string, ForzaZip> zips = new Dictionary<string, ForzaZip>(StringComparer.OrdinalIgnoreCase);
            foreach (string zip in Directory.GetFiles(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "StringTables"), "*.zip"))
            {
                zips[zip] = this.OpenChanged(zip);
            }
            StringBuilder lines = new StringBuilder();
            this.Put(target, MakeDecals.Add(this.target, this.newMake.Value, name, target, zips, lines), carbin);
            foreach (string line in lines.ToString().Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                this.Line("{0}", line);
            }
        }

        // The rule of PortTitles: added to the game's BadgeAndTitleRules.xml (backed up), which is the file the game
        // reads; in a trial folder a file of its own, which the pack build joins into the pack's <offer>.xml.
        private void WriteTitleRule()
        {
            if (this.OutputRoot == null)
            {
                string rules = MakeTitles.GameRules(this.TargetRoot);
                string text = File.ReadAllText(rules);
                if (text.IndexOf("id=\"Drive_" + this.titleBase + "\"", StringComparison.Ordinal) >= 0)
                {
                    this.Line("titles: {0} has Drive_{1} already", MakeTitles.RulesFile, this.titleBase);
                    return;
                }
                this.Write(rules, new UTF8Encoding(false).GetBytes(MakeTitles.AddRule(text, this.titleRule)));
                return;
            }
            this.Write(Path.Combine(MakeTitles.RulesFolder(this.TargetRoot), this.titleBase + ".xml"), new UTF8Encoding(false).GetBytes(MakeTitles.Document(new string[] { this.titleRule })));
        }

        // The strings of one row (table.keyColumn = id) into <table>.str of every FM4 language; a
        // language the source lacks gets the source's EN text (FM3 has no BR, GB, NL).
        private void PortStrings(string table, string keyColumn, long id, params string[] columns)
        {
            string targetTables = Path.Combine(Path.Combine(this.TargetRoot, "Media"), "StringTables");
            string sourceTables = Path.Combine(Path.Combine(this.SourceRoot, "Media"), "StringTables");
            string strName = table + ".str";
            List<string> zips = new List<string>();
            Dictionary<string, StrFile> sourceFiles = new Dictionary<string, StrFile>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, StrFile> targetFiles = new Dictionary<string, StrFile>(StringComparer.OrdinalIgnoreCase);
            foreach (string targetZip in Directory.GetFiles(targetTables, "*.zip"))
            {
                string sourceZip = Path.Combine(sourceTables, Path.GetFileName(targetZip));
                if (!File.Exists(sourceZip))
                {
                    sourceZip = Path.Combine(sourceTables, "EN.zip");
                }
                ForzaZip sourceArchive = new ForzaZip(sourceZip);
                ForzaZip.Entry sourceEntry = sourceArchive.Find(strName);
                ForzaZip archive = this.OpenChanged(targetZip);
                ForzaZip.Entry entry = archive.Find(strName);
                if (sourceEntry == null || entry == null)
                {
                    continue;
                }
                zips.Add(targetZip);
                sourceFiles[targetZip] = new StrFile(sourceArchive.Read(sourceEntry));
                targetFiles[targetZip] = new StrFile(archive.Read(entry));
            }

            int keys = 0;
            string rowWhere = SqliteDatabase.Quote(keyColumn) + " = ?";
            foreach (string column in columns)
            {
                object value = this.source.Scalar("SELECT " + column + " FROM " + table + " WHERE " + rowWhere, id);
                uint stringId;
                if (!(value is string) || !((string) value).StartsWith("_&") || !uint.TryParse(((string) value).Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out stringId))
                {
                    continue;
                }
                int key = (int) (stringId & 0xFFFF);
                string name = null;
                foreach (string zip in zips)
                {
                    StrFile.Entry text = Find(sourceFiles[zip], key);
                    if (text != null && text.Name != null)
                    {
                        name = text.Name;
                        break;
                    }
                }
                // A renamed car (beside FM4's own) never reuses FM4's string of that name: it gets a key of its own.
                bool renamed = this.RenameTo != null && table == "Data_Car";
                int newKey = this.StringKey(targetFiles, key, renamed ? null : name);
                uint high = this.TargetStringHigh(table, column, rowWhere, id, stringId & 0xFFFF0000u);
                if (high != (stringId & 0xFFFF0000u))
                {
                    this.Line("  {0}.{1}: string table id {2:X4} is FM4's {3:X4}", table, column, stringId >> 16, high >> 16);
                }
                if (newKey != key || high != (stringId & 0xFFFF0000u))
                {
                    string newValue = "_&" + (high | (uint) newKey).ToString(CultureInfo.InvariantCulture);
                    this.target.Execute("UPDATE " + table + " SET " + SqliteDatabase.Quote(column) + " = ? WHERE " + rowWhere, newValue, id);
                    if (table == "List_CarMake" && column == "DisplayName")
                    {
                        // Data_Car.MakeName is the make's own string.
                        this.target.Execute("UPDATE Data_Car SET MakeName = ? WHERE MakeID = ?", newValue, id);
                    }
                    if (newKey != key)
                    {
                        this.Line("  {0}.{1}: string key {2:X4} is another row's in FM4, {3:X4} used ({4})", table, column, key, newKey, newValue);
                    }
                }
                foreach (string zip in zips)
                {
                    StrFile.Entry text = Find(sourceFiles[zip], key);
                    if (text != null)
                    {
                        targetFiles[zip].Set(newKey, renamed ? text.Text + this.DisplaySuffix : text.Text, text.Name);
                    }
                }
                keys++;
            }
            foreach (string zip in zips)
            {
                ForzaZip archive = this.OpenChanged(zip);
                archive.Replace(archive.Find(strName), targetFiles[zip].ToBytes());
            }
            this.Line("strings: {0} keys in {1} of {2} languages", keys, strName, zips.Count);
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

        // The high 16 bits of a "_&<id>" value name the string table: FM4, FH1 and FH2 write CD49 in
        // Data_Car.DisplayName / ModelShort, FM2 A949, so an FM2 value kept as it is pointed FM4 at
        // another table and the car showed with no name (TOY_SupraVeilside99_98, user 2026-10-20). The
        // high bits FM4's other rows use in that column; 'own' when it has none.
        private uint TargetStringHigh(string table, string column, string rowWhere, long id, uint own)
        {
            object other = this.target.Scalar("SELECT " + SqliteDatabase.Quote(column) + " FROM " + table + " WHERE NOT (" + rowWhere + ") AND " + SqliteDatabase.Quote(column) + " LIKE '\\_&%' ESCAPE '\\' LIMIT 1", id);
            uint value;
            if (!(other is string) || !uint.TryParse(((string) other).Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out value))
            {
                return own;
            }
            return value & 0xFFFF0000u;
        }

        // The FM4 key for an FH2 string: its own when free in every language or already holding this
        // string (same name), the key a port before gave the name, else the highest key free everywhere
        // (FFFF ends the key list).
        private int StringKey(Dictionary<string, StrFile> files, int key, string name)
        {
            bool free = true;
            foreach (StrFile file in files.Values)
            {
                StrFile.Entry existing = Find(file, key);
                if (existing != null && (name == null || existing.Name != name))
                {
                    free = false;
                }
            }
            if (free)
            {
                return key;
            }
            if (name != null)
            {
                foreach (StrFile file in files.Values)
                {
                    foreach (StrFile.Entry entry in file.Entries)
                    {
                        if (entry.Name == name)
                        {
                            return entry.Key;
                        }
                    }
                }
            }
            for (int candidate = 0xFFFE; candidate > 0; candidate--)
            {
                bool used = false;
                foreach (StrFile file in files.Values)
                {
                    used = used || file.Contains(candidate);
                }
                if (!used)
                {
                    return candidate;
                }
            }
            throw new InvalidOperationException("The string table has no free string key.");
        }

        #endregion

        #region Car and wheel zips

        // The donor's carattribs.xml, the template of the car's. A donor without one (some cars of the DLC packs) lends
        // the first FM4 car's that has it, and the report says so.
        private string CarAttribsTemplate(ForzaZip donor)
        {
            ForzaZip.Entry own = donor.Find("carattribs.xml");
            if (own != null)
            {
                return Encoding.UTF8.GetString(donor.Read(own));
            }
            string cars = Path.Combine(Path.Combine(this.TargetRoot, "Media"), "cars");
            int tried = 0;
            foreach (string zip in Directory.Exists(cars) ? Directory.GetFiles(cars, "*.zip") : new string[0])
            {
                if (++tried > 80)
                {
                    break;
                }
                try
                {
                    ForzaZip other = new ForzaZip(zip);
                    ForzaZip.Entry entry = other.Find("carattribs.xml");
                    if (entry != null)
                    {
                        this.donorContent.Add("carattribs.xml: the template of " + Path.GetFileNameWithoutExtension(zip) + " (the donor " + this.DonorMediaName + " has none)");
                        return Encoding.UTF8.GetString(other.Read(entry));
                    }
                }
                catch (Exception)
                {
                    // not a readable car zip: the next one
                }
            }
            throw new InvalidOperationException("The donor " + this.DonorMediaName + " has no carattribs.xml and no FM4 car zip has one to use instead. Pick another donor.");
        }

        private void BuildCarZip()
        {
            string sourcePath = GamePaths.FindCar(this.SourceRoot, this.MediaName);
            string donorPath = GamePaths.FindCar(this.DonorRoot ?? this.TargetRoot, this.DonorMediaName);
            if (sourcePath == null || donorPath == null || !sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Car zips not found (" + this.MediaName + " in " + this.game + ", " + this.DonorMediaName + " in FM4).");
            }
            ForzaZip from = new ForzaZip(sourcePath);
            ForzaZip donor = new ForzaZip(donorPath);
            ForzaZip car = ForzaZip.CreateLike(donor);
            this.dropPlates = HasPlaceholderCloseUp(sourcePath, this.MediaName);
            if (this.RenameTo != null)
            {
                from = this.RenamedCopy(from);
            }
            this.donorFormat.Add("car zip: entry headers (zip versions, extra field, compression) of " + Path.GetFileName(donorPath));
            this.donorFormat.Add("zipmanifest.xml: the car zip's line modelled on the donor's");
            string prefix = this.CarName.ToLowerInvariant();
            string donorPrefix = this.DonorMediaName.ToLowerInvariant();
            int models = 0;
            List<string> materials = new List<string>();
            byte[] shaderSettings = null;
            // Granny animations: FM4 reads little-endian .gr2 files, FH2 writes big-endian ones (file
            // version 7 at 0x20 as 00000007, measured on the MP4-12C both games have). Without a
            // converter the car goes without them, as FM4 cars without moving parts do.
            bool animations = true;
            int grannyFiles = 0;
            foreach (ForzaZip.Entry entry in from.Entries)
            {
                if (entry.Name.EndsWith(".gr2", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] granny = from.Read(entry);
                    animations &= granny.Length > 0x24 && granny[0x20] == 7 && granny[0x23] == 0;
                    grannyFiles++;
                }
            }
            if (grannyFiles == 0)
            {
                animations = false;
                this.Line("  animations: none in " + this.game + " (no .gr2 files), the donor's left out");
            }
            else if (!animations)
            {
                this.Line("  animations: " + this.game + " .gr2 files are big-endian, left out (no moving parts)");
            }
            foreach (ForzaZip.Entry entry in from.Entries)
            {
                string name = entry.Name.ToLowerInvariant();
                if (name.StartsWith("stripped_") || name == "physicsdefinition.bin" || name == "manufacturercolors.xml")
                {
                    continue;   // FH2 only
                }
                if (!animations && (name.EndsWith(".gr2") || name == "caranimationevents.xml"))
                {
                    continue;
                }
                byte[] data = from.Read(entry);
                if (name.EndsWith(".fbf"))
                {
                    int layers;
                    data = ConvertAnarkLayout(data, out layers);
                    this.Line("  dash {0}: {1} text layers to the FM4 layout", Path.GetFileName(name), layers);
                }
                if (name.EndsWith(".bgf"))
                {
                    data = this.FixDashFonts(data, Path.GetFileName(name));
                }
                if (name.EndsWith(".carbin"))
                {
                    if ((name.Contains("_caliper") || name.Contains("_rotor")) && !name.EndsWith("_lod0.carbin") && this.motorsport3)
                    {
                        name = name.Substring(0, name.Length - ".carbin".Length) + "_lod0.carbin";   // FM3 caliperlf.carbin
                    }
                    if (name.Contains("_caliper") || name.Contains("_rotor"))
                    {
                        // Brakes (type 1): converted; the donor's, renamed, if that fails.
                        try
                        {
                            // Its warnings only for a brake that stays: a placeholder becomes the donor's (ReplacePlaceholderBrakes).
                            StringBuilder warnings = new StringBuilder();
                            data = ConvertBrake(data, Path.GetFileName(name), warnings);
                            if (!IsPlaceholderBrake(data) || donor.Find(donorPrefix + name.Substring(prefix.Length)) == null)
                            {
                                this.Report.Append(warnings.ToString());
                            }
                        }
                        catch (Exception ex)
                        {
                            string description;
                            byte[] generic = this.GenericBrake(car, prefix, name, out description);
                            this.Line("  brake {0}: {1}; {2}", name, ex.Message, (generic != null) ? description + " is used" : "left out");
                            if (generic == null)
                            {
                                continue;
                            }
                            data = generic;
                            this.donorContent.Add("brake " + name + ": " + description + " (conversion failed: " + ex.Message + ")");
                        }
                    }
                    else
                    {
                        data = ConvertModel(data, name.EndsWith("_lod0.carbin"), this.Report, materials, this.MaterialFixes(), this.unknownMaterials, this.DamageReference(name));
                        if (this.dropPlates)
                        {
                            data = this.DropMaterial(data, name, PlateMaterial);
                        }
                        models++;
                    }
                }
                else if (name == "carattribs.xml")
                {
                    List<string> kept = new List<string>();
                    data = ConvertCarAttributes(Encoding.UTF8.GetString(data), this.CarAttribsTemplate(donor), animations, this.Report, kept, !this.motorsport3 && !this.fm2Stage);
                    foreach (string element in kept)
                    {
                        this.donorContent.Add("carattribs.xml " + element);
                    }
                }
                else if (name == "shadersettings.xml")
                {
                    shaderSettings = data;   // needs the materials of every model
                    continue;
                }
                else if (name.EndsWith(".xds") && this.motorsport3)
                {
                    data = ConvertMotorsport3Texture(data, name);
                }
                car.Add(name, data);
            }
            if (this.dropPlates)
            {
                this.unknownMaterials.Remove(PlateMaterial);    // gone from the models: nothing to review
                materials.Remove(PlateMaterial);
            }
            this.AddSharedTextures(from, car);
            this.MoveSectionsToLod0(car, donor, prefix, donorPrefix);
            if (this.dropPlates)
            {
                this.SplitLights(car, prefix);
            }
            this.RebuildCloseUpModels(car, donor, prefix, donorPrefix, materials);
            this.ReplacePlaceholderBrakes(car, donor, prefix, donorPrefix);
            this.ReplacePlaceholderCollision(car, donor, prefix);
            this.CockpitRig(car, donor, prefix, donorPrefix);
            this.DriverHandsFromSteeringWheel(car, prefix);
            if (this.fm2Stage)
            {
                this.NeutralDriverOffsets(car);
                this.ApplyExhaustFile(car, prefix);
            }
            if (shaderSettings != null)
            {
                car.Add("shadersettings.xml", ConvertShaderSettings(Encoding.UTF8.GetString(shaderSettings), materials, this.MaterialFixes(), this.Report));
            }
            if (this.motorsport3 && !string.IsNullOrEmpty(this.motorsport3Dash))
            {
                this.AddMotorsport3Dash(car);
            }
            ForzaZip.Entry version = donor.Find("versiondata.xml");
            if (version != null && car.Find("versiondata.xml") == null)
            {
                car.Add("versiondata.xml", donor.Read(version));
                this.donorContent.Add("car zip versiondata.xml: the donor's (" + this.game + " has none)");
            }
            string path = Path.Combine(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "cars"), this.CarName + ".zip");
            // Saved with the other files at the end, with the report as port_log.txt.
            this.carZip = car;
            this.carZipPath = path;
            this.Line("car zip: {0} entries + port_log.txt, {1} models converted -> {2}", car.Entries.Count, models, this.Relative(path));
        }

        // FH keeps the LOD 0 of the headlights (headlightL / headlightR, deformable) in the cockpit file; FM4 has
        // them in the _lod0 file, which is the one that deforms when the car is alone on the track (MP4-12C: FH
        // lod0 lacks them, FM4 lod0 has them, FM4 cockpit lacks them). So every part the donor's _lod0 file has,
        // the car's _lod0 lacks and the car's cockpit file has moves from the cockpit to the _lod0 file, with
        // its damage words from the same FM4 reference as the other parts (Words = donor). Port.ini [Lod0]
        // MoveSections = off keeps the FH layout. Not for traffic cars (their close-up files are rebuilt).
        private void MoveSectionsToLod0(ForzaZip car, ForzaZip donor, string prefix, string donorPrefix)
        {
            if (this.dropPlates || PortSettings.Get("Lod0", "MoveSections", "on").ToLowerInvariant() == "off")
            {
                return;
            }
            ForzaZip.Entry lodEntry = car.Find(prefix + "_lod0.carbin");
            ForzaZip.Entry cockpitEntry = car.Find(prefix + "_cockpit.carbin");
            ForzaZip.Entry donorEntry = donor.Find(donorPrefix + "_lod0.carbin");
            if (lodEntry == null || cockpitEntry == null || donorEntry == null)
            {
                return;
            }
            CarbinModel lod0 = new CarbinModel(car.Read(lodEntry));
            CarbinModel cockpit = new CarbinModel(car.Read(cockpitEntry));
            CarbinModel reference = new CarbinModel(donor.Read(donorEntry));
            List<string> moved = new List<string>();
            foreach (CarbinModel.Section section in reference.Sections)
            {
                if (lod0.Find(section.Name) == null && cockpit.Find(section.Name) != null)
                {
                    moved.Add(section.Name);
                }
            }
            if (moved.Count == 0)
            {
                return;
            }
            CarbinModel words = this.DamageReference(prefix + "_lod0.carbin");
            foreach (string name in moved)
            {
                // Each subsection keeps its own UV range (the quantisation of its vertices), so the part is moved as it is.
                lod0.ImportSection(cockpit, name, name, false);
                cockpit.RemoveSection(name);
                if (words != null)
                {
                    this.Line("  {0}: {1}", name, lod0.BorrowDamageWords(words, name));
                }
                this.Line("  {0} moved from the cockpit file to the _lod0 file", name);
            }
            car.Replace(lodEntry, lod0.ToBytes());
            car.Replace(cockpitEntry, cockpit.ToBytes());
            this.Line("lod0 parts: {0} moved from the cockpit file to the _lod0 file (FM4 keeps them there; they deform in the LOD 0)", string.Join(", ", moved.ToArray()));
        }

        private ForzaZip damageZip;
        private bool damageZipRead;
        private bool damageByShape;
        private string damagePrefix;

        // B1 test T4 (Port.ini [Damage] Words = donor): the per-vertex damage words from an FM4 model of this file:
        // the same car's own, when FM4 has the car under the source's MediaName (matched by position); else the
        // donor car's (matched by place in each section's box, an approximation: the donor's words are FM4's own,
        // a smooth field over the part). Null when neither exists: the words stay as the Words setting makes them.
        private CarbinModel DamageReference(string entryName)
        {
            string words = PortSettings.Get("Damage", "Words", "donor").ToLowerInvariant();
            if (words != "donor" && words != "donorcar")
            {
                return null;
            }
            if (!this.damageZipRead)
            {
                this.damageZipRead = true;
                string path = GamePaths.FindCar(this.TargetRoot, this.MediaName);
                this.damagePrefix = this.MediaName;
                if (path == null || !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || words == "donorcar")
                {
                    path = GamePaths.FindCar(this.DonorRoot ?? this.TargetRoot, this.DonorMediaName);
                    this.damagePrefix = this.DonorMediaName;
                    this.damageByShape = true;
                }
                if (path != null && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    this.damageZip = new ForzaZip(this.In(path));
                }
                this.Line("damage words: " + ((this.damageZip == null) ? "no FM4 file to take them from, the source's stay"
                    : this.damageByShape ? "from the donor " + this.DonorMediaName + " (not the same car: matched by place in each section's box, Words = donor)" : "from the FM4 car's own files (Words = donor)"));
            }
            if (this.damageZip == null)
            {
                return null;
            }
            string own = entryName;
            if (own.StartsWith(this.CarName, StringComparison.OrdinalIgnoreCase))
            {
                own = this.damagePrefix.ToLowerInvariant() + own.Substring(this.CarName.Length);
            }
            ForzaZip.Entry entry = this.damageZip.Find(own);
            if (entry == null)
            {
                return null;
            }
            CarbinModel reference = new CarbinModel(this.damageZip.Read(entry));
            reference.DamageByShape = this.damageByShape;
            return reference;
        }

        // The source car zip under the new name (RenameTo), in memory: every entry name and the text files carry the
        // new name, case kept (as CarDuplicate does for an FM4 car); buildnumber.txt stays the source's.
        private ForzaZip RenamedCopy(ForzaZip from)
        {
            Regex name = new Regex(Regex.Escape(this.MediaName), RegexOptions.IgnoreCase);
            string lower = this.RenameTo.ToLowerInvariant();
            MatchEvaluator keepCase = delegate(Match m) { return (m.Value == m.Value.ToLowerInvariant()) ? lower : this.RenameTo; };
            Encoding bytes = Encoding.GetEncoding(28591);
            ForzaZip renamed = ForzaZip.CreateLike(from);
            int names = 0;
            int texts = 0;
            foreach (ForzaZip.Entry entry in from.Entries)
            {
                if (entry.IsDirectory)
                {
                    continue;
                }
                byte[] data = from.Read(entry);
                string entryName = name.Replace(entry.Name, keepCase);
                names += (entryName != entry.Name) ? 1 : 0;
                string ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                if ((ext == ".xml" || ext == ".txt" || ext == ".html") && !string.Equals(entry.Name, "buildnumber.txt", StringComparison.OrdinalIgnoreCase))
                {
                    string text = bytes.GetString(data);
                    string replaced = name.Replace(text, keepCase);
                    if (replaced != text)
                    {
                        data = bytes.GetBytes(replaced);
                        texts++;
                    }
                }
                renamed.Add(entryName, data);
            }
            this.Line("renamed {0} -> {1}: {2} entry names, {3} text files", this.MediaName, this.RenameTo, names, texts);
            return renamed;
        }

        // FM4 keeps each light in its own part, the same in the main file and _lod0 (FOR_Fiesta_09 and the
        // other 313 cars): headlightL/R (lamp, chrome, frame, reflector of one front corner), taillightL/R,
        // and their glass in glassLHL/RHL and glassLTL/RTL; L = left (x below the body's centre), front =
        // z below it. FH traffic models merge them (FOR_FocusZX4_05: taillightR holds the headlights,
        // indicators and plate light of both ends, glassRTL the front and rear glass; headlightL/R and
        // taillightL are empty-part cubes), and the close-up files rebuilt from LOD 1 inherited that (user
        // 2026-10-20). So, on cars with placeholder close-ups, before the rebuild: every triangle of those
        // eight parts goes, every LOD, to the part of its corner (CarbinModel.SplitSection), and an
        // empty-part cube in a part that got real geometry goes. Kept generic for FM1/FM2 sources.
        private static readonly string[] LightParts = { "headlightL", "headlightR", "taillightL", "taillightR" };
        private static readonly string[] LightGlassParts = { "glassLHL", "glassRHL", "glassLTL", "glassRTL" };

        private void SplitLights(ForzaZip car, string prefix)
        {
            ForzaZip.Entry entry = car.Find(prefix + ".carbin");
            if (entry == null)
            {
                return;
            }
            CarbinModel model = new CarbinModel(car.Read(entry));
            CarbinModel.Section body = model.Find("body");
            if (body == null)
            {
                return;
            }
            float[] b = body.Bounds;
            double cx = b[0] + ((b[3] + b[6]) / 2);
            double cz = b[2] + ((b[5] + b[8]) / 2);
            bool changed = false;
            List<string> parts = new List<string>(LightParts);
            parts.AddRange(LightGlassParts);
            foreach (string part in parts)
            {
                CarbinModel.Section section = model.Find(part);
                if (section == null || IsEmptyPartSection(section))
                {
                    continue;
                }
                bool glass = Array.IndexOf(LightGlassParts, part) >= 0;
                string report = model.SplitSection(section.Name, delegate(double x, double y, double z)
                {
                    if (Math.Abs(x - cx) < LightPlaceholderReach && Math.Abs(z - cz) < LightPlaceholderReach)
                    {
                        return null;    // a placeholder at the car's centre (no light is there): stays
                    }
                    bool left = x < cx;
                    bool front = z < cz;
                    return glass ? ("glass" + (left ? "L" : "R") + (front ? "HL" : "TL")) : ((front ? "headlight" : "taillight") + (left ? "L" : "R"));
                });
                if (report.Length > 0)
                {
                    changed = true;
                    foreach (string line in report.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        this.Line("  lights: {0}", line);
                    }
                }
            }
            if (!changed)
            {
                return;
            }
            // A placeholder left beside real geometry goes: the empty-part cube (headlightL_LOD1 in
            // headlightL) or the 1 cm quad of VW_Beetle_04 (material body), whatever its name: a mesh of at
            // most EmptyPartSize at the car's centre.
            foreach (string part in parts)
            {
                CarbinModel.Section section = model.Find(part);
                if (section == null)
                {
                    continue;
                }
                List<int> placeholders = new List<int>();
                List<string> names = new List<string>();
                for (int k = 0; k < section.SubSections.Count; k++)
                {
                    double[] box = model.SubSectionBox(section, section.SubSections[k]);
                    bool small = box[3] - box[0] <= EmptyPartSize && box[4] - box[1] <= EmptyPartSize && box[5] - box[2] <= EmptyPartSize;
                    bool central = Math.Abs(((box[0] + box[3]) / 2) - cx) < LightPlaceholderReach && Math.Abs(((box[2] + box[5]) / 2) - cz) < LightPlaceholderReach;
                    if (small && central)
                    {
                        placeholders.Add(k);
                        if (!names.Contains(section.SubSections[k].Name))
                        {
                            names.Add(section.SubSections[k].Name);
                        }
                    }
                }
                if (placeholders.Count > 0 && placeholders.Count < section.SubSections.Count)
                {
                    model.RemoveSubSections(section.Name, placeholders);
                    this.Line("  lights: {0}: placeholder {1} removed (the part has real geometry now)", part, string.Join(", ", names.ToArray()));
                }
            }
            car.Replace(entry, model.ToBytes());
        }

        // Half the side of the square around the body's centre (x, z) where no light sits: placeholders do.
        private const double LightPlaceholderReach = 0.3;

        // FH traffic cars lose their number plate (material `numberplate`, a quad in bumperFrameF/R): FM4
        // has no plate shader, it only stayed red / black and needed outside work (user 2026-10-20: "lo
        // leviamo, non serve"). Decided by the car list's quick test (HasPlaceholderCloseUp), since the
        // models are converted before the close-up rebuild sets this.traffic. A section left empty goes.
        private const string PlateMaterial = "numberplate";
        private bool dropPlates;

        private byte[] DropMaterial(byte[] data, string file, string material)
        {
            CarbinModel model = new CarbinModel(data);
            List<string> sections = new List<string>();
            foreach (CarbinModel.Section section in model.Sections)
            {
                if (section.SubSections.Exists(delegate(CarbinModel.SubSection sub) { return string.Equals(sub.Name, material, StringComparison.OrdinalIgnoreCase); }))
                {
                    sections.Add(section.Name);
                }
            }
            if (sections.Count == 0)
            {
                return data;
            }
            int removed = 0;
            foreach (string section in sections)
            {
                removed += model.RemoveSubSections(section, material);
            }
            this.Line("  {0}: traffic car, number plate removed ({1} meshes in {2})", file, removed, string.Join(" ", sections.ToArray()));
            return model.ToBytes();
        }

        // Colour textures the car's materials take from the source game's Media\cars\Shared.zip (its shaders and
        // Shared.zip\textures.xml, ShaderTextures) that FM4's Shared.zip lacks go into the car zip, so
        // the car carries them (user 2026-10-20: GMC_YukonXL_01's number plates,
        // traffic_numplates_DXT1.xds, FH1 only). FM4's shaders do not read them from there: they are for
        // the editor's preview and for later material work, not a change in game.
        private void AddSharedTextures(ForzaZip from, ForzaZip car)
        {
            ShaderTextures source = ShaderTextures.For(this.SourceRoot);
            ShaderTextures target = ShaderTextures.For(this.TargetRoot);
            if (source.Problem != null || target.SharedZipPath == null || !File.Exists(target.SharedZipPath))
            {
                this.Line("  shared textures: not checked ({0})", source.Problem ?? "no FM4 Media\\cars\\Shared.zip");
                return;
            }
            ForzaZip sourceShared = GamePaths.OpenZip(source.SharedZipPath);
            ForzaZip targetShared = GamePaths.OpenZip(target.SharedZipPath);
            SortedDictionary<string, List<string>> needed = new SortedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (ForzaZip.Entry entry in from.Entries)
            {
                string name = entry.Name.ToLowerInvariant();
                if (!name.EndsWith(".carbin") || name.Contains("_caliper") || name.Contains("_rotor") || name.StartsWith("stripped_"))
                {
                    continue;   // stripped_*: FH2-only copies, not ported
                }
                ShaderTextures.Kind fileKind = name.EndsWith("_cockpit.carbin") ? ShaderTextures.Kind.Cockpit
                    : name.EndsWith("_lod0.carbin") ? ShaderTextures.Kind.Lod0 : ShaderTextures.Kind.Main;
                try
                {
                    using (ForzaCarBin bin = new ForzaCarBin(name, from.Read(entry)))
                    {
                        foreach (ForzaCarSection section in bin.Sections)
                        {
                            foreach (ForzaCarSubSection sub in section.SubSections)
                            {
                                if (this.dropPlates && string.Equals(sub.Name, PlateMaterial, StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;   // removed from the models (DropMaterial): no plate texture
                                }
                                ShaderTextures.Kind kind = (fileKind == ShaderTextures.Kind.Main && sub.Lod == 0) ? ShaderTextures.Kind.Lod0 : fileKind;
                                // Only a material's own colour texture: FH1's `body` reads the plates too
                                // (a switch the traffic cars turn on), so every FH1 car would carry them;
                                // `numberplate` draws with them (user 2026-10-20: only cars with a
                                // material that fits).
                                List<ShaderTextures.Texture> colours = source.ColourTextures(sub.Name, kind);
                                foreach (ShaderTextures.Texture texture in colours.GetRange(0, Math.Min(1, colours.Count)))
                                {
                                    if (texture.Where != ShaderTextures.Place.Shared)
                                    {
                                        continue;
                                    }
                                    List<string> users;
                                    if (!needed.TryGetValue(texture.File, out users))
                                    {
                                        needed[texture.File] = users = new List<string>();
                                    }
                                    if (!users.Contains(sub.Name))
                                    {
                                        users.Add(sub.Name);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.Line("  shared textures: {0} not read ({1})", name, ex.Message);
                }
            }
            foreach (KeyValuePair<string, List<string>> pair in needed)
            {
                ForzaZip.Entry file = sourceShared.Find(pair.Key);
                if (targetShared.Find(pair.Key) != null || file == null || car.Find(pair.Key.ToLowerInvariant()) != null)
                {
                    continue;
                }
                byte[] data = sourceShared.Read(file);
                if (this.motorsport3)
                {
                    data = ConvertMotorsport3Texture(data, pair.Key.ToLowerInvariant());
                }
                car.Add(pair.Key.ToLowerInvariant(), data);
                this.Line("  shared texture {0}: not in FM4's Shared.zip, copied from {1}'s into the car zip (used by {2})", pair.Key, this.game, string.Join(", ", pair.Value.ToArray()));
            }
        }

        // The collision (physics/maxdata.xml: a bevelled box and spheres; Flags 2 = body, 1 = contact
        // points of r 0.05). 44 FH1 and 2 FH2 cars have a placeholder there: 4 spheres of r 0.375, Flags
        // 3, low at the wheel corners (GMC_YukonXL_01, BUS_TOUR_12), and a box that may not fit the
        // model (GMC_YukonXL_01: 26 cm short of the front bumper): in FM4 a guardrail went through the
        // Yukon (user 2026-10-18). FM4 cars have 26-39 spheres (190 of 313: 20 + 12). On a traffic car
        // such a collision is rebuilt. The box from the main model's body section by FM4's rule (313 cars, medians): x =
        // body x - 0.018, top = body top - 0.010, front = body front + 0.096, rear = body rear - 0.046,
        // floor = the ground (wheel centre y - tyre radius, +0.006), bevels 0.611 / 0.744 of the width
        // (front / rear) and 0.819 / 0.805 of the length. The spheres are the donor's, moved from its
        // box into this one axis by axis, radii scaled by the smaller of the x and y ratios, so they stick out
        // of the box as much as the donor's do (the source has
        // nothing; decided for traffic cars' brakes by the user 2026-10-17).
        private const int PlaceholderSpheres = 10;

        private void ReplacePlaceholderCollision(ForzaZip car, ForzaZip donor, string prefix)
        {
            ForzaZip.Entry entry = car.Find("physics/maxdata.xml");
            ForzaZip.Entry template = donor.Find("physics/maxdata.xml");
            ForzaZip.Entry main = car.Find(prefix + ".carbin");
            if (entry == null || template == null || main == null)
            {
                return;
            }
            XmlDocument own = new XmlDocument();
            own.LoadXml(Encoding.UTF8.GetString(car.Read(entry)).Trim('﻿', ' ', '\r', '\n', '\t'));
            XmlElement spheres = (XmlElement) own.SelectSingleNode("/MAXData/Collision/CollSpheres");
            XmlElement box = (XmlElement) own.SelectSingleNode("/MAXData/Collision/BoundingBox");
            if (spheres == null || box == null || spheres.ChildNodes.Count >= PlaceholderSpheres)
            {
                return;
            }
            if (!this.traffic)
            {
                // Driveable FH1 cars have it too (AUD_R8GT_11, MER_S65AMG_12, NIS_GTR_12... 26 of the 44),
                // ported and working: left as it is (decided by the user 2026-10-18, traffic cars only).
                this.Line("  collision: a placeholder of {0} spheres in the source; kept (only traffic cars get it rebuilt)", spheres.ChildNodes.Count);
                return;
            }
            CarbinModel.Section body = new CarbinModel(car.Read(main)).Find("body");
            if (body == null)
            {
                this.Line("  collision: a placeholder of {0} spheres, but the main model has no body section; left as is", spheres.ChildNodes.Count);
                return;
            }
            float[] b = body.Bounds;
            double wheelY = Convert.ToDouble(this.target.Scalar("SELECT PartPosY FROM CarPartPositions WHERE Ordinal = ? AND PartId = 5", this.carId) ?? 0.0, CultureInfo.InvariantCulture);
            object[] tyre = this.target.Query("SELECT FrontTireWidthMM, FrontTireAspect, FrontWheelDiameterIN FROM Data_Car WHERE Id = ?", this.carId).Rows[0];
            double radius = (Convert.ToDouble(tyre[2], CultureInfo.InvariantCulture) * 0.0254 / 2) + (Convert.ToDouble(tyre[0], CultureInfo.InvariantCulture) * Convert.ToDouble(tyre[1], CultureInfo.InvariantCulture) / 100000.0);
            double maxX = Math.Max(Math.Abs(b[0] + b[3]), Math.Abs(b[0] + b[6])) - 0.018;
            double minY = wheelY - radius + 0.006;
            double maxY = b[1] + b[7] - 0.010;
            double maxZ = -(b[2] + b[5]) + 0.096;    // model Z backward
            double minZ = -(b[2] + b[8]) - 0.046;
            // Front and rear from the whole LOD 1 mesh (vertices, all parts): an FH traffic body holds
            // the bumpers too (GMC_YukonXL_01), so the body rule above put the box and the spheres
            // ~0.2 m past both ends (seen in the collision view, user 2026-10-19). The collision's Z is
            // measured from the wheelbase centre (Misc/BottomCenterWheelbasePos z = the middle of the DB
            // wheels' Z): with it FM4's ends spread half as much (314 cars, fm4dev\Traffic\tools\colsurvey)
            // and the Yukon (z 0.188) stopped short at the front and went into a wall at the back
            // (user 2026-10-20). FM4 medians, model axes: box front = mesh front - 0.127, rear = mesh
            // rear + 0.045; body spheres front = mesh front - 0.092, rear = mesh rear - 0.003. The rear is
            // then pulled in 0.05 m more and the front pushed out 0.05 m (box and spheres), tuned in game
            // on the Yukon against walls (user 2026-10-20, "quasi perfetta"). Final rule, Z forward from
            // the wheelbase centre: box front = mesh front - 0.077, rear = mesh rear + 0.095; body
            // spheres front = mesh front - 0.042, rear = mesh rear + 0.047.
            const double RearIn = 0.05;
            const double FrontOut = 0.05;
            XmlElement wheelbase = (XmlElement) own.SelectSingleNode("/MAXData/Misc/BottomCenterWheelbasePos");
            double centreZ = (wheelbase != null && wheelbase.HasAttribute("z")) ? Attribute(wheelbase, "z") : 0;
            double meshFront = double.MinValue, meshRear = double.MaxValue;
            using (ForzaCarBin bin = new ForzaCarBin(prefix, car.Read(main)))
            {
                foreach (ForzaCarSection section in bin.Sections)
                {
                    foreach (ForzaCarSubSection sub in section.SubSections)
                    {
                        if (sub.Lod != 1)
                        {
                            continue;
                        }
                        foreach (int i in sub.Indices)
                        {
                            double z = -sub.Vertices[i].position.Z;
                            meshFront = Math.Max(meshFront, z);
                            meshRear = Math.Min(meshRear, z);
                        }
                    }
                }
            }
            if (meshFront > meshRear)
            {
                maxZ = meshFront - 0.127 + FrontOut - centreZ;
                minZ = meshRear + 0.045 + RearIn - centreZ;
            }
            double width = 2 * maxX, length = maxZ - minZ;

            XmlDocument theirs = new XmlDocument();
            theirs.LoadXml(Encoding.UTF8.GetString(donor.Read(template)).Trim('﻿', ' ', '\r', '\n', '\t'));
            XmlElement donorBox = (XmlElement) theirs.SelectSingleNode("/MAXData/Collision/BoundingBox");
            XmlElement donorSpheres = (XmlElement) theirs.SelectSingleNode("/MAXData/Collision/CollSpheres");
            double dMinX = Attribute(donorBox, "MinX"), dMaxX = Attribute(donorBox, "MaxX"), dMinY = Attribute(donorBox, "MinY"), dMaxY = Attribute(donorBox, "MaxY"), dMinZ = Attribute(donorBox, "MinZ"), dMaxZ = Attribute(donorBox, "MaxZ");
            double sx = width / (dMaxX - dMinX), sy = (maxY - minY) / (dMaxY - dMinY), sz = length / (dMaxZ - dMinZ);

            SetAttribute(box, "MinX", -maxX);
            SetAttribute(box, "MaxX", maxX);
            SetAttribute(box, "MinY", minY);
            SetAttribute(box, "MaxY", maxY);
            SetAttribute(box, "MinZ", minZ);
            SetAttribute(box, "MaxZ", maxZ);
            SetAttribute(box, "BevelFrontX", width * 0.611);
            SetAttribute(box, "BevelFrontZ", length * 0.819);
            SetAttribute(box, "BevelRearX", width * 0.744);
            SetAttribute(box, "BevelRearZ", length * 0.805);
            int placeholder = spheres.ChildNodes.Count;
            spheres.RemoveAll();
            foreach (XmlAttribute attribute in donorSpheres.Attributes)
            {
                spheres.SetAttribute(attribute.Name, attribute.Value);
            }
            foreach (XmlNode node in donorSpheres.ChildNodes)
            {
                XmlElement sphere = node as XmlElement;
                if (sphere == null)
                {
                    continue;
                }
                XmlElement copy = (XmlElement) own.ImportNode(sphere, true);
                SetAttribute(copy, "PosX", (Attribute(sphere, "PosX") - ((dMinX + dMaxX) / 2)) * sx);
                SetAttribute(copy, "PosY", minY + ((Attribute(sphere, "PosY") - dMinY) * sy));
                SetAttribute(copy, "PosZ", minZ + ((Attribute(sphere, "PosZ") - dMinZ) * sz));
                SetAttribute(copy, "Radius", Attribute(sphere, "Radius") * Math.Min(sx, sy));
                spheres.AppendChild(copy);
            }
            if (meshFront > meshRear)
            {
                StretchSpheresZ(spheres, meshFront - 0.092 + FrontOut - centreZ, meshRear - 0.003 + RearIn - centreZ);
            }
            int filled = FillSphereGaps(own, spheres);
            StringBuilder text = new StringBuilder();
            using (XmlWriter writer = XmlWriter.Create(text, new XmlWriterSettings { Indent = true, IndentChars = "  ", OmitXmlDeclaration = true }))
            {
                own.DocumentElement.WriteTo(writer);
            }
            car.Replace(entry, Encoding.UTF8.GetBytes("\r\n" + text.ToString().Replace(" />", "/>") + "\r\n"));
            this.donorContent.Add(string.Format(CultureInfo.InvariantCulture, "collision: the donor's {0} spheres fitted to the car's box (scale x {1:0.###}, y {2:0.###}, z {3:0.###}), the source's is a placeholder of {4}", spheres.ChildNodes.Count, sx, sy, sz, placeholder));
            this.Line(string.Format(CultureInfo.InvariantCulture, "  collision: a placeholder of {0} spheres; box from the model x +-{1:0.###}, y {2:0.###}..{3:0.###}, z {4:0.###}..{5:0.###}, the donor's {6} spheres fitted to it", placeholder, maxX, minY, maxY, minZ, maxZ, spheres.ChildNodes.Count - filled));
            if (filled > 0)
            {
                this.Line("  collision: {0} spheres added between the side spheres the stretch left apart ({1} in all; FM4 cars have at most 39, more is untested)", filled, spheres.ChildNodes.Count);
            }
        }

        // FM4's body spheres (Flags 2) end near the mesh ends (314 cars, medians: front surface = mesh
        // front - 0.092, rear = mesh rear - 0.003, wheelbase-centre axes; colsurvey). The donor's spheres, fitted to the box,
        // fall short of that (GMC_YukonXL_01: 0.23 / 0.14 m inside): their centres are moved along Z,
        // linearly, so the outermost surfaces land there. Radii stay; contact points move with them.
        private static void StretchSpheresZ(XmlElement spheres, double front, double rear)
        {
            List<XmlElement> all = new List<XmlElement>();
            foreach (XmlNode node in spheres.ChildNodes)
            {
                if (node is XmlElement)
                {
                    all.Add((XmlElement) node);
                }
            }
            XmlElement first = null, last = null;
            foreach (XmlElement s in all)
            {
                if (s.GetAttribute("Flags") != "2")
                {
                    continue;
                }
                if (first == null || Attribute(s, "PosZ") + Attribute(s, "Radius") > Attribute(first, "PosZ") + Attribute(first, "Radius"))
                {
                    first = s;
                }
                if (last == null || Attribute(s, "PosZ") - Attribute(s, "Radius") < Attribute(last, "PosZ") - Attribute(last, "Radius"))
                {
                    last = s;
                }
            }
            if (first == null || first == last)
            {
                return;
            }
            double z1 = Attribute(first, "PosZ"), z0 = Attribute(last, "PosZ");
            double t1 = front - Attribute(first, "Radius"), t0 = rear + Attribute(last, "Radius");
            if (z1 - z0 < 0.01 || t1 <= t0)
            {
                return;
            }
            double a = (t1 - t0) / (z1 - z0);
            foreach (XmlElement s in all)
            {
                SetAttribute(s, "PosZ", t0 + ((Attribute(s, "PosZ") - z0) * a));
            }
        }

        // A donor collision stretched along a much longer car (BUS_TOUR_12: the Hummer's x 2.9) leaves gaps
        // between the body spheres (Flags 2) along each side. On each side (x < 0, x > 0) the large ones
        // (at least half the side's largest radius), front to back: two that do not touch get spheres in
        // between, position and radius interpolated, until each touches the next. Returns how many.
        private static int FillSphereGaps(XmlDocument document, XmlElement spheres)
        {
            List<XmlElement> all = new List<XmlElement>();
            foreach (XmlNode node in spheres.ChildNodes)
            {
                if (node is XmlElement)
                {
                    all.Add((XmlElement) node);
                }
            }
            int added = 0;
            foreach (int side in new int[] { -1, 1 })
            {
                List<XmlElement> chain = all.FindAll(delegate(XmlElement s) { return s.GetAttribute("Flags") == "2" && Attribute(s, "PosX") * side > 0.05; });
                double largest = 0;
                foreach (XmlElement s in chain)
                {
                    largest = Math.Max(largest, Attribute(s, "Radius"));
                }
                chain = chain.FindAll(delegate(XmlElement s) { return Attribute(s, "Radius") >= largest / 2; });
                chain.Sort(delegate(XmlElement a, XmlElement b) { return Attribute(b, "PosZ").CompareTo(Attribute(a, "PosZ")); });
                for (int i = 0; i + 1 < chain.Count; i++)
                {
                    XmlElement a = chain[i], b = chain[i + 1];
                    double ra = Attribute(a, "Radius"), rb = Attribute(b, "Radius");
                    double dx = Attribute(b, "PosX") - Attribute(a, "PosX"), dy = Attribute(b, "PosY") - Attribute(a, "PosY"), dz = Attribute(b, "PosZ") - Attribute(a, "PosZ");
                    double distance = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                    int count = (int) Math.Ceiling(distance / (ra + rb)) - 1;
                    for (int k = 1; k <= count; k++)
                    {
                        double t = (double) k / (count + 1);
                        XmlElement sphere = document.CreateElement("Sphere" + (all.Count + added));
                        SetAttribute(sphere, "Radius", ra + ((rb - ra) * t));
                        SetAttribute(sphere, "PosX", Attribute(a, "PosX") + (dx * t));
                        SetAttribute(sphere, "PosY", Attribute(a, "PosY") + (dy * t));
                        SetAttribute(sphere, "PosZ", Attribute(a, "PosZ") + (dz * t));
                        sphere.SetAttribute("Flags", "2");
                        spheres.AppendChild(sphere);
                        added++;
                    }
                }
            }
            return added;
        }

        private static double Attribute(XmlElement element, string name)
        {
            return double.Parse(element.GetAttribute(name), CultureInfo.InvariantCulture);
        }

        private static void SetAttribute(XmlElement element, string name, double value)
        {
            element.SetAttribute(name, value.ToString("0.000000", CultureInfo.InvariantCulture));
        }

        // FH traffic cars' brakes are placeholders (GMC_YukonXL_01: one section of 17 vertices per file,
        // material rotorLF_LOD0...) and their main model has no brake part either: nothing behind the rim
        // (checked by the user 2026-10-17). Source data being absent, such a file (or a caliper the source
        // lacks while it has the rotor) becomes the game's own generic brake of Media\brakes (no donor car's
        // part; idea of the user 2026-10-20): N_caliper_4 / _2 and N_rotor_slotted _LOD0, N = rotor
        // diameter in inches (the file's rotor has a radius of N * 0.0127 m, origin at the wheel centre).
        // The generic caliper sits at +Y: turned about X to +Z on the front, -Z on the rear (what most FM4
        // cars have), mirrored for the right side (rotors are symmetric).
        private const int PlaceholderBrakeVertices = 50;

        private static int BrakeVertices(byte[] data)
        {
            int vertices = 0;
            foreach (CarbinModel.Section section in new CarbinModel(data).Sections)
            {
                vertices += section.Lod.Count + section.Lod0.Count;
            }
            return vertices;
        }

        private static bool IsPlaceholderBrake(byte[] data)
        {
            return BrakeVertices(data) < PlaceholderBrakeVertices;
        }

        // Rotor diameter in inches for the generic brake: the car's own rotor when it has a real one,
        // else the rim minus 4.5 (FM4 cars: 15" rim 10-12", 18" 13-15", 19" 14-15"). 7..16, the generic sizes.
        private int GenericBrakeInches(ForzaZip car, string prefix, bool front)
        {
            ForzaZip.Entry rotor = car.Find(prefix + (front ? "_rotorlf_lod0.carbin" : "_rotorlr_lod0.carbin"));
            double inches = 0;
            if (rotor != null)
            {
                byte[] data = car.Read(rotor);
                if (!IsPlaceholderBrake(data))
                {
                    foreach (CarbinModel.Section section in new CarbinModel(data).Sections)
                    {
                        double[] p = CarbinModel.Positions(section, (section.Lod0.Count > 0) ? section.Lod0 : section.Lod);
                        double radius = 0;
                        for (int i = 0; i < p.Length / 3; i++)
                        {
                            radius = Math.Max(radius, Math.Max(Math.Abs(p[(i * 3) + 1]), Math.Abs(p[(i * 3) + 2])));
                        }
                        inches = Math.Max(inches, radius * 2 / 0.0254);
                    }
                }
            }
            if (inches <= 0)
            {
                string column = front ? "FrontWheelDiameterIN" : "RearWheelDiameterIN";
                inches = Convert.ToDouble(this.target.Scalar("SELECT " + column + " FROM Data_Car WHERE Id = ?", this.carId), CultureInfo.InvariantCulture) - 4.5;
            }
            return Math.Max(7, Math.Min(16, (int) Math.Floor(inches + 0.5)));
        }

        // What goes on a car that has no brakes of its own (FH traffic, FM2, anything the source lacks), asked
        // from the user for each car (2026-10-20, instead of a weight/power rule): the caliper of the front and
        // of the rear (the generic set has 2, 4 and 6 pistons with a LOD0) and the angle the caliper sits at, in
        // degrees around the axle (the row's CaliperAngleFront/Rear, radians, when left out; a value given is
        // written to the row). The rotor is one model per size, the slotted one (user 2026-10-24: the game has
        // no LOD0 plain disc, only the caliper changes); its size comes from the rim.
        public class BrakeChoice
        {
            public int FrontPistons = 2;
            public int RearPistons = 2;
            public double? FrontAngle;
            public double? RearAngle;

            // "2,2,-75,90": front pistons, rear pistons, front/rear angle (empty = the row's)
            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}", this.FrontPistons, this.RearPistons,
                    this.FrontAngle, this.RearAngle);
            }

            // Also reads the older "2,2,0,0,-75,90" (with the rotor types, no longer chosen).
            public static BrakeChoice Parse(string text)
            {
                BrakeChoice choice = new BrakeChoice();
                string[] p = (text ?? "").Split(',');
                int pistons;
                double angle;
                if (p.Length >= 4)
                {
                    int angles = (p.Length >= 6) ? 4 : 2;
                    choice.FrontPistons = (int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out pistons) && pistons >= 2 && pistons <= 6) ? pistons : 2;
                    choice.RearPistons = (int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out pistons) && pistons >= 2 && pistons <= 6) ? pistons : 2;
                    choice.FrontAngle = double.TryParse(p[angles], NumberStyles.Float, CultureInfo.InvariantCulture, out angle) ? (double?) angle : null;
                    choice.RearAngle = double.TryParse(p[angles + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out angle) ? (double?) angle : null;
                }
                return choice;
            }
        }

        public BrakeChoice Brakes = new BrakeChoice();

        // The generic brake keeps the material and the UV transform of the game's own file (Media\brakes): it
        // is the model the showroom takes from Shared when a brake upgrade is fitted, and its material reads the
        // game's shared brake texture. The car's textures are not touched (user 2026-10-22).

        // The generic brake for a car file name (prefix_caliperlf_lod0.carbin...), or null without the game file.
        private byte[] GenericBrake(ForzaZip car, string prefix, string name, out string description)
        {
            description = null;
            bool front = name.Contains("lf_") || name.Contains("rf_");
            bool right = name.Contains("rf_") || name.Contains("rr_");
            bool caliper = name.Contains("_caliper");
            int inches = this.GenericBrakeInches(car, prefix, front);
            string why = string.Format(CultureInfo.InvariantCulture, "chosen: {0} piston", front ? this.Brakes.FrontPistons : this.Brakes.RearPistons);
            string kind = caliper ? "caliper_" + (front ? this.Brakes.FrontPistons : this.Brakes.RearPistons).ToString(CultureInfo.InvariantCulture) : "rotor_slotted";
            string file = string.Format(CultureInfo.InvariantCulture, "{0}_{1}_LOD0.rmb.carbin", inches, kind);
            string path = Path.Combine(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "brakes"), file);
            if (!File.Exists(path))
            {
                return null;
            }
            // The caliper's angle is the row's (CaliperAngleFront/Rear, radians: the model's own angle as the
            // FM4 cars show it, atan2(z, y) of the caliper = minus the value); the generic one sits at +Y.
            string angleColumn = front ? "CaliperAngleFront" : "CaliperAngleRear";
            double? chosen = front ? this.Brakes.FrontAngle : this.Brakes.RearAngle;
            if (chosen != null)
            {
                this.target.Execute("UPDATE Data_Car SET " + angleColumn + " = ? WHERE Id = ?", chosen.Value * Math.PI / 180, this.carId);   // the row follows the model
            }
            double angle = Convert.ToDouble(this.target.Scalar("SELECT " + angleColumn + " FROM Data_Car WHERE Id = ?", this.carId), CultureInfo.InvariantCulture);
            // The car's own brake files hold LOD 0 only (the lod buffer is empty in every FM4 _lod0 brake file): the game takes
            // the other LODs from its generic file without the suffix by itself (user 2026-10-24: LOD 0 from _LOD0, the file
            // without LOD for all the others), so only LOD 0 is written here.
            CarbinModel brake = new CarbinModel(File.ReadAllBytes(path));
            foreach (CarbinModel.Section section in brake.Sections)
            {
                if (!caliper)
                {
                    brake.RemoveSubSections(section.Name, "caliper");   // the rotor's file may carry a caliper too
                }
                else
                {
                    brake.RemoveSubSections(section.Name, "rotor");     // and the 6 piston caliper a rotor
                }
                if (caliper)
                {
                    brake.TransformSection(section.Name, Microsoft.Xna.Framework.Matrix.CreateRotationX((float) -angle));
                }
                if (right)
                {
                    brake.MirrorSection(section.Name, 0);
                }
                string part = name.Substring(name.LastIndexOf('/') + 1);
                part = part.Substring(prefix.Length + 1, part.IndexOf("_lod0") - prefix.Length - 1);   // caliperlf
                section.Name = part.Substring(0, part.Length - 2) + part.Substring(part.Length - 2).ToUpperInvariant();
            }
            description = string.Format(CultureInfo.InvariantCulture, "the game's generic brake {0} ({1}; angle {2:0.00} rad; material and shared texture of the game's file kept)", file, why, angle);
            return brake.ToBytes();
        }

        // An FM2 stage (fm2Stage) takes the game's generic brakes for every position, whatever its files hold
        // (the donor's, no FM2 brake art: decided by the user 2026-10-20 for FM2 traffic).
        private void ReplacePlaceholderBrakes(ForzaZip car, ForzaZip donor, string prefix, string donorPrefix)
        {
            // Calipers the source lacks while it has the rotor (an FM2 stage: every missing file).
            foreach (string position in new string[] { "lf", "lr", "rf", "rr" })
            {
                foreach (string part in new string[] { "caliper", "rotor" })
                {
                    string brakeName = prefix + "_" + part + position + "_lod0.carbin";
                    bool missing = car.Find(brakeName) == null;
                    if (!missing || (!this.fm2Stage && (part != "caliper" || car.Find(prefix + "_rotor" + position + "_lod0.carbin") == null)))
                    {
                        continue;
                    }
                    string description;
                    byte[] generic = this.GenericBrake(car, prefix, brakeName, out description);
                    if (generic != null)
                    {
                        car.Add(brakeName, generic);
                        this.donorContent.Add("brake " + brakeName + ": " + description + " (the source has none)");
                        this.Line("  brake {0}: the source has none, {1}", brakeName, description);
                    }
                }
            }
            foreach (ForzaZip.Entry entry in new List<ForzaZip.Entry>(car.Entries))
            {
                string name = entry.Name.ToLowerInvariant();
                if (!name.EndsWith(".carbin") || (!name.Contains("_caliper") && !name.Contains("_rotor")) || !name.StartsWith(prefix))
                {
                    continue;
                }
                int vertices = BrakeVertices(car.Read(entry));
                if (vertices >= PlaceholderBrakeVertices && !this.fm2Stage)
                {
                    continue;
                }
                string description;
                byte[] generic = this.GenericBrake(car, prefix, name, out description);
                if (generic == null)
                {
                    this.Line("  brake {0}: a placeholder of {1} vertices, and the game has no generic brake of that size; left as is", name, vertices);
                    continue;
                }
                car.Replace(entry, generic);
                this.donorContent.Add(string.Format(CultureInfo.InvariantCulture, "brake {0}: {1}, the source's is a placeholder", name, description));
                this.Line("  brake {0}: {1} vertices, now {2}", name, vertices, description);
            }
        }

        // A quick test for the car list, from the zip directory only: a _lod0 or _cockpit file missing or
        // smaller than a tenth of the main file. Close-up files of real cars are 40% of it and up (FM4 313,
        // FH1, FH2); placeholders 3% at most (18 FH1 and 14 FH2 cars). The port itself decides by vertices.
        public static bool HasPlaceholderCloseUp(string zipPath, string mediaName)
        {
            string prefix = mediaName.ToLowerInvariant();
            ForzaZip car = new ForzaZip(zipPath);
            ForzaZip.Entry main = car.Find(prefix + ".carbin");
            if (main == null)
            {
                return false;
            }
            foreach (string kind in new string[] { "_lod0", "_cockpit" })
            {
                ForzaZip.Entry close = car.Find(prefix + kind + ".carbin");
                if (close == null || close.UncompressedSize < main.UncompressedSize / 10)
                {
                    return true;
                }
            }
            return false;
        }

        // FM4 draws a car up close from its _lod0 (exterior) and _cockpit (interior) files. FH traffic models
        // (GMC_YukonXL_01, FOR_Transit_11, TOY_Camry_07... 19 FH1/FH2 cars) have only placeholders there,
        // 10-17 parts of 17 vertices, and a few have no file at all (FOR_Explorer_10): the car vanishes
        // up close. A close-up file with less than 2% of the main file's vertices (real ones have 5% and
        // up; FM4's lod0 holds more than the main file) is rebuilt from the main file's LOD 1 by FM4's rule
        // (313 cars): the cockpit parts (CockpitPart) go to _cockpit, the rest to _lod0. The cockpit also
        // gets the body, so the cockpit view always shows the car (decided by the user 2026-10-17; such
        // cars often have no hood part). The header is the source's own close-up file's, else the donor's.
        // The seat with the traffic driver merged in (DriverSeat) is replaced by the other one mirrored:
        // FM4 draws its own driver.
        private void RebuildCloseUpModels(ForzaZip car, ForzaZip donor, string prefix, string donorPrefix, List<string> materials)
        {
            ForzaZip.Entry mainEntry = car.Find(prefix + ".carbin");
            if (mainEntry == null)
            {
                return;
            }
            CarbinModel main = new CarbinModel(car.Read(mainEntry));
            bool rebuilt = false;
            bool lod0Rebuilt = false;
            bool cockpitRebuilt = false;
            CarbinModel cockpitModel = null;
            string driverSeat = DriverSeat(main);
            CarbinModel source = main;
            if (driverSeat != null)
            {
                // A bus has no passenger seat: the other seat is an empty part (BUS_TOUR_12 seatR, the
                // 0.2 m cube). Mirrored, it would take the driver's seat away: the seat stays as it is.
                CarbinModel.Section other = main.Find((driverSeat == "seatL") ? "seatR" : "seatL");
                if (other == null || IsEmptyPartSection(other))
                {
                    this.Line("  {0}: the traffic driver is part of it, but the other seat is {1}: kept as it is, with the driver", driverSeat, (other == null) ? "missing" : "an empty part");
                    driverSeat = null;
                }
            }
            if (driverSeat != null)
            {
                // The driver's seat becomes the other seat mirrored across the body's center plane
                // (decided by the user 2026-10-17).
                string twin = (driverSeat == "seatL") ? "seatR" : "seatL";
                CarbinModel.Section body = main.Find("body");
                source = new CarbinModel(main.ToBytes());
                source.RemoveSection(driverSeat);
                source.DuplicateSection(twin, driverSeat);
                source.MirrorSection(driverSeat, (body != null) ? body.Bounds[0] : 0);
            }
            int mainVertices = 0;
            foreach (CarbinModel.Section section in main.Sections)
            {
                mainVertices += section.Lod.Count;
            }
            // Empty parts stay out of the close-up models (decided by the user 2026-10-18): FM4's cockpits
            // have none, and they would be drawn with 'missing'.
            List<string> emptyParts = new List<string>();
            foreach (CarbinModel.Section section in source.Sections)
            {
                if (IsEmptyPartSection(section))
                {
                    emptyParts.Add(section.Name);
                }
            }
            foreach (string kind in new string[] { "_lod0", "_cockpit" })
            {
                string name = prefix + kind + ".carbin";
                ForzaZip.Entry own = car.Find(name);
                byte[] head = null;
                int vertices = 0;
                if (own != null)
                {
                    CarbinModel model = new CarbinModel(car.Read(own));
                    head = model.Head;
                    foreach (CarbinModel.Section section in model.Sections)
                    {
                        vertices += section.Lod0.Count;
                    }
                }
                if (own != null && vertices >= mainVertices * 0.02)
                {
                    continue;
                }
                if (head == null)
                {
                    ForzaZip.Entry template = donor.Find(donorPrefix + kind + ".carbin");
                    if (template == null)
                    {
                        this.Line("  {0}: {1}, and the donor has no such file for the header; left as is", name, (own == null) ? "missing" : "a placeholder");
                        continue;
                    }
                    head = new CarbinModel(donor.Read(template)).Head;
                    this.donorFormat.Add(name + ": header of the donor's " + template.Name);
                }
                bool cockpit = kind == "_cockpit";
                CarbinModel built;
                try
                {
                    built = source.CloseUpFromLod1(head, delegate(string part) { return !emptyParts.Contains(part) && (CockpitPart(part) == cockpit || (cockpit && part == "body")); });
                }
                catch (InvalidDataException ex)
                {
                    this.Line("  {0}: not rebuilt from LOD 1 ({1})", name, ex.Message);
                    continue;
                }
                List<string> parts = new List<string>();
                int built0 = 0;
                int atlasRenames = 0;
                foreach (CarbinModel.Section section in built.Sections)
                {
                    parts.Add(section.Name);
                    built0 += section.Lod0.Count;
                    RenameLod0Tire(section);
                    foreach (CarbinModel.SubSection sub in section.SubSections)
                    {
                        sub.Name = LodVariant(sub.Name, name);
                        // `interior` (the main file's LOD 1 interior material) has no cockpit_ technique:
                        // drawn with the plain one it reads nodamage_LOD0, not the interior atlas its UVs are
                        // laid out on (InteriorAtlas). FM4 itself uses it in 5 of 313 cockpits; leather
                        // (cockpit_leather: the atlas only) is on 262, and the user picked it by hand on the
                        // Focus ZX4 (2026-10-20).
                        if (cockpit && string.Equals(sub.Name, "interior", StringComparison.OrdinalIgnoreCase))
                        {
                            sub.Name = "leather";
                            atlasRenames++;
                        }
                        if (!materials.Contains(sub.Name))
                        {
                            materials.Add(sub.Name);
                        }
                    }
                }
                byte[] data = built.ToBytes();
                if (own != null)
                {
                    car.Replace(own, data);
                }
                else
                {
                    car.Add(name, data);
                }
                this.Line("  {0}: {1} (main file {2}), rebuilt from LOD 1: {3} parts, {4} vertices ({5})", name, (own == null) ? "missing" : "a placeholder of " + vertices + " vertices", mainVertices, parts.Count, built0, string.Join(" ", parts.ToArray()));
                if (atlasRenames > 0)
                {
                    this.Line("  {0}: material interior -> leather on {1} meshes (reads the interior atlas)", name, atlasRenames);
                }
                rebuilt = true;
                lod0Rebuilt |= !cockpit;
                cockpitRebuilt |= cockpit;
                if (cockpit)
                {
                    cockpitModel = built;
                }
            }
            if (!rebuilt)
            {
                return;
            }
            this.traffic = true;
            if (emptyParts.Count > 0)
            {
                this.Line("  close-up models: empty parts left out ({0})", string.Join(" ", emptyParts.ToArray()));
            }
            if (driverSeat != null)
            {
                this.Line("  {0}: the traffic driver is part of it (FM4 draws its own); in the close-up models it is the other seat mirrored", driverSeat);
            }
            // The close-up models sample the _lod0 textures (every FM4 car, 313, has all four); FH traffic cars'
            // are laid out for the placeholders (GMC_YukonXL_01: white wheel arches, another car's hood
            // decal) or missing (lights). The rebuilt models are LOD 1 geometry, mapped on the LOD 1 textures.
            if (lod0Rebuilt)
            {
                foreach (string texture in new string[] { "nodamage", "damage", "lights", "damagelights" })
                {
                    ForzaZip.Entry plain = car.Find(texture + ".xds");
                    if (plain == null)
                    {
                        continue;
                    }
                    ForzaZip.Entry close = car.Find(texture + "_lod0.xds");
                    if (close != null)
                    {
                        car.Replace(close, car.Read(plain));
                    }
                    else
                    {
                        car.Add(texture + "_lod0.xds", car.Read(plain));
                    }
                    this.Line("  {0}_lod0.xds: {1}, now a copy of {0}.xds", texture, (close != null) ? "the placeholder's" : "missing");
                }
            }
            if (cockpitRebuilt && cockpitModel != null)
            {
                this.InteriorAtlas(car, cockpitModel);
            }
            // The placeholders' materials (body_LOD0, hooda_LOD0...) are gone with them: out of the review
            // and of the shader settings.
            List<string> used = new List<string>();
            foreach (ForzaZip.Entry entry in car.Entries)
            {
                string entryName = entry.Name.ToLowerInvariant();
                if (entryName.EndsWith(".carbin") && !entryName.Contains("_caliper") && !entryName.Contains("_rotor"))
                {
                    foreach (CarbinModel.Section section in new CarbinModel(car.Read(entry)).Sections)
                    {
                        foreach (CarbinModel.SubSection sub in section.SubSections)
                        {
                            if (!used.Contains(sub.Name))
                            {
                                used.Add(sub.Name);
                            }
                        }
                    }
                }
            }
            foreach (string material in new List<string>(this.unknownMaterials.Keys))
            {
                if (!used.Contains(material))
                {
                    this.unknownMaterials.Remove(material);
                }
            }
            materials.RemoveAll(delegate(string material) { return !used.Contains(material); });
        }

        // A rebuilt cockpit's interior_lod0.xds is always a copy of nodamage.xds (user 2026-10-20, checked
        // by hand in game: "quando converti traffic prendi il nodamage.xds e fai una copia che chiami
        // interior_lod0.xds"), replacing the source's own when it has one.
        private void InteriorAtlas(ForzaZip car, CarbinModel cockpit)
        {
            ForzaZip.Entry nodamage = car.Find("nodamage.xds");
            if (nodamage == null)
            {
                this.Line("  interior_lod0.xds: no nodamage.xds to copy, left as it is");
                return;
            }
            ForzaZip.Entry interior = car.Find("interior_lod0.xds");
            if (interior != null)
            {
                car.Replace(interior, car.Read(nodamage));
            }
            else
            {
                car.Add("interior_lod0.xds", car.Read(nodamage));
            }
            this.Line("  interior_lod0.xds: {0}, now a copy of nodamage.xds", (interior != null) ? "the source's, replaced" : "missing");
        }

        // FM4's cockpit parts (_cockpit file, never in _lod0; measured on the 313 cars of the game):
        // interior, steering wheel, mirrors, hood, seats, windscreen and front side windows (glass or
        // lexan), the side mirror glass, the A pillars, the roll cage and the gauge needles.
        private static bool CockpitPart(string name)
        {
            string part = Regex.Replace(name, "[0-9]+$", "");
            foreach (string prefix in new string[] { "interior", "steering_wheel", "mirror", "hood", "seat", "a_pillar", "cage", "tach_", "speed_", "fuel_" })
            {
                if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            foreach (string glass in new string[] { "glassF", "glassLF", "glassRF", "glassLM", "glassRM", "lexanF", "lexanLF", "lexanRF" })
            {
                if (string.Equals(part, glass, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        // Anark digital dash layout (.fbf), FH2 version 0x3F0 -> FM4 0x3EF. A text layer is
        //   u32 length + name, 6 bytes, u16 length + default text, 14 bytes, u16 length + font, 33 bytes,
        //   [FH2 only: a float], ...
        // FM4 keeps the default text empty and has no float. The FM4 game reads an FH2 file wrongly past the
        // first text layer (the P1 crashed while loading the race: a 5014-byte copy over its stack).
        // On the 38 dashes both games have, 29 come out byte for byte as FM4's. The others differ in content FH2
        // changed (fonts horizon_dg*, a few values). .bgf/.bsg are the same in both games.
        // Rewritten on AnarkFbf (2026-10-15): the old byte-pattern version assumed an empty tag and an
        // untagged text layer, and broke 9 FH and 6 FH2 dashes (tag "NLOC", 458 Spider, V12 Zagato, RS5...),
        // which FM4 would then read as garbage. Each text record keeps its tag; the 4 bytes dropped are
        // tail bytes 33..36 (the old converter's choice, byte-identical to FM4 on 29 of 38 shared dashes).
        public static byte[] ConvertAnarkLayout(byte[] fh2, out int layers)
        {
            layers = 0;
            if (fh2.Length < 4 || AnarkFbf.ReadU32(fh2, 0) != AnarkFbf.VersionFH2)
            {
                return fh2;
            }
            AnarkFbf fbf = AnarkFbf.Read(fh2);
            fbf.Version = AnarkFbf.VersionFM4;
            foreach (AnarkFbf.Record record in fbf.Records)
            {
                if (record.Type != AnarkFbf.RecordType.Text)
                {
                    continue;
                }
                byte[] tail = new byte[34];
                Array.Copy(record.TextTail, 0, tail, 0, 33);
                tail[33] = record.TextTail[37];
                record.TextTail = tail;
                record.Text = "";
                layers++;
            }
            DashFonts.Fix(fbf, null);
            return fbf.Write();
        }

        // .bgf font names FM4's fontmap does not know become FM4's names (DashFonts); a name with no rule is reported.
        private byte[] FixDashFonts(byte[] bgf, string label)
        {
            int changed;
            List<string> unknown = new List<string>();
            byte[] fixedData = DashFonts.Fix(bgf, out changed, unknown);
            if (changed > 0)
            {
                this.Line("  dash {0}: {1} font names changed to FM4's", label, changed);
            }
            if (unknown.Count > 0)
            {
                this.Line("  WARNING dash {0}: font {1} has no FM4 equivalent rule, FM4 may show a generic font", label, string.Join(", ", unknown.ToArray()));
            }
            return fixedData;
        }

        // The FM3 car's digital dash into the car zip, as FM4 keeps it: digitalgauge/dash_<car>.fbf/.bgf/.bsg
        // and the textures it shows as digitalgauge/<name>.xds (from FM3's Media\UI\Textures.zip, colour
        // textures to FM4's gamma form). A gauge zip can hold more than one dash (Dash_AUD_8_R10_06 has 3):
        // the one named after the car, else after the zip, else the only one.
        private void AddMotorsport3Dash(ForzaZip car)
        {
            string zipPath = Path.Combine(Path.Combine(Path.Combine(this.SourceRoot, "Media"), "DigitalGauges"), this.motorsport3Dash + ".zip");
            if (!File.Exists(zipPath))
            {
                this.Line("  WARNING digital dash: {0} not found, the car goes without it", this.Relative(zipPath));
                return;
            }
            ForzaZip gauges = new ForzaZip(zipPath);
            List<string> stems = new List<string>();
            foreach (ForzaZip.Entry entry in gauges.Entries)
            {
                if (entry.Name.EndsWith(".fbf", StringComparison.OrdinalIgnoreCase))
                {
                    stems.Add(entry.Name.Substring(0, entry.Name.Length - 4));
                }
            }
            string stem = null;
            foreach (string wanted in new string[] { "dash_" + this.MediaName, this.motorsport3Dash })
            {
                foreach (string candidate in stems)
                {
                    if (stem == null && string.Equals(Path.GetFileName(candidate), wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        stem = candidate;
                    }
                }
            }
            if (stem == null && stems.Count == 1)
            {
                stem = stems[0];
            }
            ForzaZip.Entry bgf = (stem != null) ? gauges.Find(stem + ".bgf") : null;
            ForzaZip.Entry bsg = (stem != null) ? gauges.Find(stem + ".bsg") : null;
            if (bgf == null || bsg == null)
            {
                this.Line("  WARNING digital dash: no complete dash in {0} ({1} .fbf files), the car goes without it", Path.GetFileName(zipPath), stems.Count);
                return;
            }
            string texturesPath = Path.Combine(Path.Combine(Path.Combine(this.SourceRoot, "Media"), "UI"), "Textures.zip");
            ForzaZip textures = File.Exists(texturesPath) ? new ForzaZip(texturesPath) : null;
            Dictionary<string, byte[]> images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            List<string> missing = new List<string>();
            List<string> fromFm4 = new List<string>();
            byte[] fbf = ConvertMotorsport3Dash(gauges.Read(gauges.Find(stem + ".fbf")), this.MediaName, delegate(string path)
            {
                // GAME:\MEDIA\UI\TEXTURES\<rest>.TGA -> Textures.zip <rest>.xds
                const string ui = @"GAME:\MEDIA\UI\TEXTURES\";
                if (!path.StartsWith(ui, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                string rest = path.Substring(ui.Length).Replace('\\', '/');
                string name = Path.GetFileNameWithoutExtension(rest);
                if (images.ContainsKey(name))
                {
                    return true;
                }
                ForzaZip.Entry texture = (textures != null) ? textures.Find(Path.ChangeExtension(rest, ".xds")) : null;
                if (texture == null)
                {
                    // Not in the source (an FM3 DLC pack has no Textures.zip): the same image as an FM4
                    // car's dash carries it, already in FM4's format.
                    byte[] fm4 = this.Fm4DashImage(name);
                    if (fm4 == null)
                    {
                        missing.Add(path);
                        return false;
                    }
                    images[name] = fm4;
                    fromFm4.Add(name);
                    return true;
                }
                images[name] = ConvertMotorsport3Texture(textures.Read(texture), texture.Name);
                return true;
            });
            string prefix = "digitalgauge/dash_" + this.MediaName.ToLowerInvariant();
            car.Add(prefix + ".fbf", fbf);
            car.Add(prefix + ".bgf", this.FixDashFonts(gauges.Read(bgf), prefix + ".bgf"));
            car.Add(prefix + ".bsg", gauges.Read(bsg));
            foreach (KeyValuePair<string, byte[]> image in images)
            {
                car.Add("digitalgauge/" + image.Key.ToLowerInvariant() + ".xds", image.Value);
            }
            this.Line("  digital dash: {0}/{1} -> {2}.fbf/.bgf/.bsg, {3} textures from Media\\UI\\Textures.zip", Path.GetFileName(zipPath), Path.GetFileName(stem), prefix, images.Count - fromFm4.Count);
            if (fromFm4.Count > 0)
            {
                this.Line("  digital dash: {0} images not in the source, taken from FM4 cars' dashes: {1}", fromFm4.Count, string.Join(", ", fromFm4.ToArray()));
            }
            if (missing.Count > 0)
            {
                this.Line("  WARNING digital dash: {0} images not found, left pointing at FM3's path: {1}", missing.Count, string.Join(", ", missing.ToArray()));
            }
        }

        // An image of an FM4 car's dash (digitalgauge/<name>.xds in the car zips of the target), or null.
        // The index of the target's dash images is built once per target folder. FM3's shared dash
        // images (ui4\dashboards\mfd_oval, mfd_square01...) are carried by the FM4 cars whose dash came
        // from FM3, the same bytes in every car (checked on the Hyundai pack's 19, 2026-10-18).
        private byte[] Fm4DashImage(string name)
        {
            lock (Fm4DashLock)
            {
                if (fm4DashImages == null || !string.Equals(fm4DashRoot, this.TargetRoot, StringComparison.OrdinalIgnoreCase))
                {
                    fm4DashImages = new Dictionary<string, KeyValuePair<string, string>>(StringComparer.OrdinalIgnoreCase);
                    fm4DashRoot = this.TargetRoot;
                    string cars = Path.Combine(Path.Combine(this.TargetRoot, "Media"), "cars");
                    foreach (string zip in Directory.Exists(cars) ? Directory.GetFiles(cars, "*.zip") : new string[0])
                    {
                        try
                        {
                            foreach (ForzaZip.Entry entry in new ForzaZip(zip).Entries)
                            {
                                string entryName = entry.Name.Replace('\\', '/');
                                if (entryName.StartsWith("digitalgauge/", StringComparison.OrdinalIgnoreCase) && entryName.EndsWith(".xds", StringComparison.OrdinalIgnoreCase)
                                    && !fm4DashImages.ContainsKey(Path.GetFileNameWithoutExtension(entryName)))
                                {
                                    fm4DashImages[Path.GetFileNameWithoutExtension(entryName)] = new KeyValuePair<string, string>(zip, entry.Name);
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // A damaged or foreign zip: not a source of dash images.
                        }
                    }
                }
                KeyValuePair<string, string> found;
                if (!fm4DashImages.TryGetValue(name, out found))
                {
                    return null;
                }
                ForzaZip car = new ForzaZip(found.Key);
                return car.Read(car.Find(found.Value));
            }
        }

        private static readonly object Fm4DashLock = new object();
        private static Dictionary<string, KeyValuePair<string, string>> fm4DashImages;
        private static string fm4DashRoot;

        // FM3 dash scene (.fbf 0x3EE) -> FM4 (0x3EF): text tails get FM4's 34th byte (0 on every FM4
        // file), the default text is emptied as in FM4's files (the .bgf holds it), and every image the
        // takeTexture callback accepts points at the car's own digitalgauge folder,
        // GAME:\MEDIA\CARS\<CAR>\DIGITALGAUGE\<NAME>.TGA. .bgf and .bsg are the same format in both
        // games. Checked on the 59 dashes both games have: everything else in the records as FM4's
        // (they differ only where FM4 re-authored the dash or moved its textures).
        public static byte[] ConvertMotorsport3Dash(byte[] data, string mediaName, Predicate<string> takeTexture)
        {
            AnarkFbf fbf = AnarkFbf.Read(data);
            if (!fbf.IsFM3)
            {
                return data;
            }
            fbf.Version = AnarkFbf.VersionFM4;
            foreach (AnarkFbf.Record record in fbf.Records)
            {
                if (record.Type == AnarkFbf.RecordType.Text)
                {
                    byte[] tail = new byte[34];
                    Array.Copy(record.TextTail, 0, tail, 0, 33);
                    record.TextTail = tail;
                    record.Text = "";
                }
                else if (record.Type == AnarkFbf.RecordType.Image && takeTexture != null && takeTexture(record.Path))
                {
                    record.Path = @"GAME:\MEDIA\CARS\" + mediaName.ToUpperInvariant() + @"\DIGITALGAUGE\" + Path.GetFileNameWithoutExtension(record.Path.Replace('\\', '/')).ToUpperInvariant() + ".TGA";
                }
            }
            DashFonts.Fix(fbf, null);
            return fbf.Write();
        }

        // One FH2 model file in FM4 form: converted, FH2-only materials renamed (lod0 lights to the
        // _lod0 variants FM4 uses there), the 'deprecated' material removed.
        // Calipers and rotors (carbin type 1): the same section layout as the car models, a fixed
        // 0x354-byte header whose only FH2/FM4 difference is the format word. 1838 FH2 files convert
        // and re-read as FM4; of the 870 both games have, 630 come out as FM4's but for the vertex
        // tails (SNORM8 -> SNORM16 rounding), the others differ in content FH2 changed (renamed
        // materials such as rotor -> brake, which FM4 also uses, and remodelled parts).
        // Brake materials are not in ShaderSettings.xml; a name no FM4 brake uses is reported.
        public static byte[] ConvertBrake(byte[] data, string name, StringBuilder report)
        {
            CarbinModel model = new CarbinModel(data, true);
            if (model.Type != 1)
            {
                throw new NotSupportedException("not a type 1 carbin (type " + model.Type + ")");
            }
            ConvertToMotorsport4(model);
            List<string> unknown = new List<string>();
            foreach (CarbinModel.Section section in model.Sections)
            {
                foreach (CarbinModel.SubSection sub in section.SubSections)
                {
                    if (Array.IndexOf(BrakeMaterials, sub.Name) < 0 && !unknown.Contains(sub.Name))
                    {
                        unknown.Add(sub.Name);
                    }
                }
            }
            if (unknown.Count > 0 && report != null)
            {
                report.AppendFormat("  brake {0}: material {1} is not used by any FM4 brake", name, string.Join(", ", unknown.ToArray())).AppendLine();
            }
            return model.ToBytes();
        }

        // An FM3 texture in FM4 form. FM3 stores every texture as linear (fetch dword 0 low byte 0x02)
        // in plain DXT1/DXT5 (0x52/0x54); FM4 marks the colour textures gamma (0xFE: sign x/y/z = 3) in the
        // AS_16 formats (0x73/0x75) and leaves only the normal maps linear (4224 FM3, 11775 FM4 car and
        // wheel textures, 2026-10-16). Left linear, FM4 skips the gamma decode and the car looks washed
        // out (BMW X5 in game). The colours differ too: FM3 holds sRGB values, FM4 the same colours
        // re-encoded for the Xbox 360 gamma curve, FM4 = PWL^-1(sRGB to linear(FM3)), alpha unchanged
        // (Supra in both games, 2026-10-20: interior_lod0 mean error 14.6 -> 2.5, bias 0; with the flag
        // alone the dark tones come out about twice as bright). The curve goes on the DXT colour
        // endpoints of every level. FM3's DXT1 normal maps (FM4: DXN) are kept as they are.
        public static byte[] ConvertMotorsport3Texture(byte[] data, string name)
        {
            string file = Path.GetFileNameWithoutExtension(name).ToLowerInvariant();
            if (data.Length < XdsTexture.HeaderSize || file.Contains("_nrm") || file.Contains("normal"))
            {
                return data;
            }
            XdsTexture texture = new XdsTexture(data);
            if (!texture.IsGamma)
            {
                DxtFormat codec = texture.Codec;
                for (int level = 0; level < texture.LevelCount; level++)
                {
                    byte[] blocks = texture.ReadLevelBlocks(level);
                    if (codec == DxtFormat.A8R8G8B8)
                    {
                        for (int i = 0; i < blocks.Length; i += 4)
                        {
                            blocks[i] = SrgbToGamma360[blocks[i]];
                            blocks[i + 1] = SrgbToGamma360[blocks[i + 1]];
                            blocks[i + 2] = SrgbToGamma360[blocks[i + 2]];
                        }
                    }
                    else if (codec == DxtFormat.Dxt1 || codec == DxtFormat.Dxt3 || codec == DxtFormat.Dxt5)
                    {
                        int size = texture.BytesPerBlock;
                        for (int offset = size - 8; offset < blocks.Length; offset += size)
                        {
                            ConvertColorEndpoints(blocks, offset, codec == DxtFormat.Dxt1);
                        }
                    }
                    texture.WriteLevelBlocks(level, blocks);
                }
            }
            byte[] result = texture.ToBytes();
            result[0x1F] = (byte) ((result[0x1F] & 0x03) | 0xFC);   // fetch 0: sign x, y, z = gamma
            int format = result[0x23] & 0x3F;
            if (format == 0x12 || format == 0x14)
            {
                result[0x23] = (byte) ((result[0x23] & 0xC0) | (format + 0x21));   // DXT1/DXT4_5 -> their AS_16 forms
            }
            return result;
        }

        // The two 565 endpoints of a DXT colour block through SrgbToGamma360. A DXT1 block keeps its
        // mode (c0 > c1: four colours, else three + transparent): when the new endpoints swap order they
        // are swapped back with their indices, and a four-colour block whose endpoints became equal
        // points every texel at endpoint 0 (index 3 would turn transparent).
        private static void ConvertColorEndpoints(byte[] blocks, int offset, bool dxt1)
        {
            int c0 = blocks[offset] | (blocks[offset + 1] << 8);
            int c1 = blocks[offset + 2] | (blocks[offset + 3] << 8);
            uint indices = (uint) (blocks[offset + 4] | (blocks[offset + 5] << 8) | (blocks[offset + 6] << 16) | (blocks[offset + 7] << 24));
            int n0 = ConvertEndpoint(c0);
            int n1 = ConvertEndpoint(c1);
            if (dxt1)
            {
                bool four = c0 > c1;
                if (four && n0 == n1)
                {
                    indices = 0;
                }
                else if ((four && n0 < n1) || (!four && n0 > n1))
                {
                    int swap = n0;
                    n0 = n1;
                    n1 = swap;
                    uint swapped = 0;
                    for (int i = 0; i < 16; i++)
                    {
                        uint index = (indices >> (i * 2)) & 3;
                        index = four ? (index ^ 1) : ((index < 2) ? (index ^ 1) : index);   // 0<->1, 2<->3 / 0<->1
                        swapped |= index << (i * 2);
                    }
                    indices = swapped;
                }
            }
            blocks[offset] = (byte) n0;
            blocks[offset + 1] = (byte) (n0 >> 8);
            blocks[offset + 2] = (byte) n1;
            blocks[offset + 3] = (byte) (n1 >> 8);
            blocks[offset + 4] = (byte) indices;
            blocks[offset + 5] = (byte) (indices >> 8);
            blocks[offset + 6] = (byte) (indices >> 16);
            blocks[offset + 7] = (byte) (indices >> 24);
        }

        private static int ConvertEndpoint(int c)
        {
            return (Endpoint5[(c >> 11) & 0x1F] << 11) | (Endpoint6[(c >> 5) & 0x3F] << 5) | Endpoint5[c & 0x1F];
        }

        // sRGB byte -> Xbox 360 gamma byte: sRGB to linear, then the inverse of the PWL curve the
        // texture unit decodes gamma textures with (64 -> 16, 96 -> 32, 128 -> 64, 192 -> 128, 255 -> 255).
        private static readonly byte[] SrgbToGamma360 = BuildSrgbToGamma360();
        private static readonly int[] Endpoint5 = BuildEndpointMap(5);
        private static readonly int[] Endpoint6 = BuildEndpointMap(6);

        private static byte[] BuildSrgbToGamma360()
        {
            byte[] map = new byte[256];
            for (int v = 0; v < 256; v++)
            {
                double c = v / 255.0;
                double l = 255.0 * ((c <= 0.04045) ? (c / 12.92) : Math.Pow((c + 0.055) / 1.055, 2.4));
                double g = (l < 16) ? (l * 4) : (l < 32) ? (64 + ((l - 16) * 2)) : (l < 128) ? (96 + (l - 32)) : (192 + ((l - 128) * 63.0 / 127.0));
                map[v] = (byte) Math.Max(0, Math.Min(255, (int) Math.Round(g)));
            }
            return map;
        }

        // A 5 or 6 bit endpoint channel through SrgbToGamma360, back to the nearest code of that width.
        private static int[] BuildEndpointMap(int bits)
        {
            int count = 1 << bits;
            int[] map = new int[count];
            for (int code = 0; code < count; code++)
            {
                int target = SrgbToGamma360[(code << (8 - bits)) | (code >> ((2 * bits) - 8))];
                int best = 0;
                for (int other = 1; other < count; other++)
                {
                    int expanded = (other << (8 - bits)) | (other >> ((2 * bits) - 8));
                    int bestExpanded = (best << (8 - bits)) | (best >> ((2 * bits) - 8));
                    if (Math.Abs(expanded - target) < Math.Abs(bestExpanded - target))
                    {
                        best = other;
                    }
                }
                map[code] = best;
            }
            return map;
        }

        // A source model (read with the FM3 opt-in) in FM4 form: FM3 through its own converter, FH (and
        // FM4 files, which it leaves as they are) through ConvertHorizonToMotorsport4.
        private static void ConvertToMotorsport4(CarbinModel model)
        {
            if (model.IsMotorsport3)
            {
                model.ConvertMotorsport3ToMotorsport4();
            }
            else
            {
                model.ConvertHorizonToMotorsport4();
            }
        }

        // Every material name of the 2664 FM4 type-1 carbins (car zips and Media\brakes).
        private static readonly string[] BrakeMaterials = new string[]
        {
            "back", "badge", "black_noOcclude", "brake", "brake_bump_metal", "brake_bump_metal2", "brake_bump_metal3",
            "brake_bump_metal4", "brake_chrome", "brake_smooth_metal", "caliper", "caliper_", "caliper_noOcclude",
            "caliperLF_LOD0", "caliperLR_LOD0", "caliperRF_LOD0", "caliperRR_LOD0", "chrome", "chrome_2", "chrome_noOcclude",
            "detail_glass_clear", "drum", "drum_", "front", "hub", "hub_", "left_side", "Material__2", "Material__4",
            "Material__5", "matte_colors", "matte_colors_NoOcclude_NoShadow", "matte_colors_NoOcclude_NoShadows", "misc",
            "misc_noOcclude", "misc_NoOcclude_NoShadow", "outer_rim", "right_side", "rotor", "rotor_", "top", "undercarriage",
        };

        // True when some FM4 car uses the material in a model of this carbin type (1 brakes, else models).
        public static bool IsFm4Material(string name, int type)
        {
            return (type == 1) ? Array.IndexOf(BrakeMaterials, name) >= 0 : Fm4MaterialUse.UsedAnywhere(name);
        }

        // The FM4 material names offered for a carbin type, sorted, for picking a replacement (models: the
        // common ones, CommonMaterials).
        public static string[] Fm4Materials(int type)
        {
            if (type != 1)
            {
                // only what some FM4 car model really uses (not lights_amber and the like)
                List<string> used = new List<string>();
                foreach (string name in CommonMaterials)
                {
                    if (Fm4MaterialUse.UsedAnywhere(name))
                    {
                        used.Add(name);
                    }
                }
                used.Sort(StringComparer.OrdinalIgnoreCase);
                return used.ToArray();
            }
            string[] names = (string[]) BrakeMaterials.Clone();
            Array.Sort(names, StringComparer.OrdinalIgnoreCase);
            return names;
        }

        // The FM4 name MaterialMap gives an FH2 material, or the FM4 name differing only in case; null if none.
        public static string SuggestMaterial(string name, string fileName)
        {
            string to;
            if (MapMaterial(name, out to) && to != null)
            {
                return LodVariant(to, fileName);
            }
            foreach (string known in Fm4MaterialUse.CarMaterials())
            {
                if (string.Equals(known, name, StringComparison.OrdinalIgnoreCase))
                {
                    return known;
                }
            }
            return null;
        }

        // The name a light material has in a given model file of an FM4 car: tail_light_lod0 in _lod0 and
        // _cockpit (FM4 cockpits never use tail_light), reverse_light_lod0 in _lod0, the plain name in the main model.
        public static string LodVariant(string material, string fileName)
        {
            string plain = (material == "tail_light_lod0" || material == "reverse_light_lod0") ? material.Substring(0, material.Length - 5) : material;
            bool lod0 = fileName.EndsWith("_lod0.carbin", StringComparison.OrdinalIgnoreCase);
            bool cockpit = fileName.EndsWith("_cockpit.carbin", StringComparison.OrdinalIgnoreCase);
            if ((plain == "tail_light" && (lod0 || cockpit)) || (plain == "reverse_light" && lod0))
            {
                return plain + "_lod0";
            }
            return plain;
        }

        // Every material name of the original FM4 car models (.carbin, _lod0, _cockpit of the 317 car zips of
        // the untouched game, 943 models, 150 names; fm4dev\DEV_DLC\tools\matcount, 2026-10-16). An FH material
        // outside this list (after MaterialMap) has no FM4 shader and renders black. The list measured before
        // (on a game holding ported cars) also had 25 FH names (lights_gls_*, fogred, oldhead...) that were
        // then taken for FM4 ones and never reported.
        private static readonly string[] ModelMaterials = new string[]
        {
            "01___Default", "4_MER_SLR_05_damage", "ADV_RGII_Wheel_LOD0", "badge", "badge_chrome", "badge2", "black",
            "black_glass", "black_glass_breakable", "blur_lip", "blur_rim", "body", "body_2", "body_3", "bottom",
            "brake", "brushed_metal", "bump_leather", "bump_leather2", "bump_leather3", "bump_metal", "bump_metal2",
            "bump_metal3", "bump_metal4", "bump_plastic", "bump_plastic2", "bump_plastic3", "bump_plastic4",
            "bump_plastic5", "bump_stitching", "bump_stitchingA", "bumper_frame", "bumperFa_LOD1", "carbon_fiber",
            "carbon_fiber_no_env", "carbon_fiber2", "carbon_fiber3", "chrome", "chrome_2", "chrome_3",
            "chrome_blur_lip", "chrome_blur_rim", "chrome_bump", "chrome_rim", "chrome2", "cloth", "defrost_lines",
            "detail_glass_amber", "detail_glass_clear", "detail_glass_red", "digital_gauge", "drum", "emblem",
            "embossed_metal", "embossed_plastic", "frame", "gauge_emissive", "gauge_needle", "glass", "grille1",
            "grille1_alpha", "grille2", "grille2_alpha", "grille3", "head_light", "headlight_paint", "headliner",
            "headliner2", "inner_rim", "interior", "leather", "lights_amber", "lights_chrome", "lights_glass",
            "lights_metal", "lights_plastic", "liveryBadge", "Material__0", "Material__1", "Material__11",
            "Material__12", "Material__14", "Material__15", "Material__16", "Material__3", "Material__6",
            "matt_colors", "matt_colors2", "matte_colors", "metal", "metal2", "mirrorLeft", "mirrorMiddle",
            "mirrorRight", "misc", "misc2", "mottled", "mottled2", "outer_rim", "plastic", "Plastic2", "plastic2",
            "race_kit_red", "red_glass", "reflector", "reverse_light", "reverse_light_lod0", "rim", "rim_black",
            "rim_bump_metal", "rim_chrome", "rim_chrome_blur_lip", "rim_inner_rim", "rim_outer_rim",
            "rim_wheel_emblem", "rubber", "rubber_trim", "smooth_metal", "smooth_plastic", "solid", "stitching",
            "stripe_badge", "suede", "tail_light", "tail_light_lod0", "Tail_light_lod0", "tail_light_on",
            "tail_light2", "taillightL_LOD1", "taillightL_LOD5", "taillightR_LOD1", "taillightR_LOD5",
            "textured_reflector", "tiling_cloth", "tiling_cloth2", "tire", "undercarriage", "unicolor_carbon_fiber",
            "weatherstrip", "wheel_black", "wheel_emblem", "white_brake", "window", "wingz_LOD0", "wingz_LOD1",
            "wingz_LOD2", "wingz_LOD3", "wingz_LOD4", "wingz_LOD5", "wood",
        };

        // The materials offered as fixes: those of at least 50 of the 317 FM4 cars (67 names). The rare ones
        // (one-off names such as Material__11 or wingz_LOD3) stay valid but are not offered.
        private static readonly string[] CommonMaterials = new string[]
        {
            "badge", "black", "black_glass", "blur_lip", "blur_rim", "body", "body_2", "bottom", "bump_leather",
            "bump_leather2", "bump_plastic", "bump_plastic2", "bump_stitching", "bumper_frame", "carbon_fiber",
            "chrome", "chrome_2", "chrome_rim", "cloth", "defrost_lines", "detail_glass_amber", "detail_glass_clear",
            "detail_glass_red", "digital_gauge", "emblem", "frame", "gauge_emissive", "gauge_needle", "glass",
            "grille1", "grille1_alpha", "grille2", "grille2_alpha", "head_light", "headliner", "inner_rim",
            "interior", "leather", "lights_glass", "matte_colors", "metal", "mirrorLeft", "mirrorMiddle",
            "mirrorRight", "misc", "mottled", "outer_rim", "plastic", "plastic2", "race_kit_red", "reflector",
            "reverse_light", "reverse_light_lod0", "rim", "rubber", "rubber_trim", "solid", "tail_light",
            "tail_light_lod0", "textured_reflector", "tire", "undercarriage", "weatherstrip", "wheel_black",
            "wheel_emblem", "window", "wood",
        };

        public static byte[] ConvertModel(byte[] data, bool lod0, StringBuilder report)
        {
            return ConvertModel(data, lod0, report, null);
        }

        public static byte[] ConvertModel(byte[] data, bool lod0, StringBuilder report, List<string> materials)
        {
            return ConvertModel(data, lod0, report, materials, null, null);
        }

        // An empty part: a section standing for a part the car does not have, drawn as a few cm of geometry
        // with the material <section>_LOD<n>, which no shader has (FM4 draws it with 'missing', too small
        // to see). FH cars use it (BUS_TOUR_12: headlightL, winga, glassT, seatR... as 0.2 m cubes at the
        // origin; also LAM_ReventonRoadster_09, AUD_RS3_11) and so do FM4's own (BMW_M3E30_91 wingz_LOD1,
        // LAN_Stratos_74 bumperFa_LOD1; wingz_LOD0 in two _lod0 files, none in the 313 _cockpit files).
        // Measured 2026-10-18; the largest box seen is 0.33 m. In a section with real parts the empty part is
        // a LOD 2-5 subsection only (wheel_LOD5, seatL_LOD4, bumperFrameR_LOD5: the 0.2 m cube, 23 indices;
        // FM4's PON_FirebirdTransAm_77 taillightL_LOD5: 29), told by its few indices.
        private const float EmptyPartSize = 0.35f;
        private const int EmptyPartIndices = 64;

        public static bool IsEmptyPart(CarbinModel.Section section, string material)
        {
            float[] b = section.Bounds;
            return Regex.IsMatch(material, "^" + Regex.Escape(section.Name) + "_LOD\\d$", RegexOptions.IgnoreCase)
                && b[6] - b[3] <= EmptyPartSize && b[7] - b[4] <= EmptyPartSize && b[8] - b[5] <= EmptyPartSize;
        }

        public static bool IsEmptyPart(CarbinModel.Section section, CarbinModel.SubSection sub)
        {
            return IsEmptyPart(section, sub.Name)
                || (Regex.IsMatch(sub.Name, "^" + Regex.Escape(section.Name) + "_LOD\\d$", RegexOptions.IgnoreCase) && sub.Lod >= 2 && sub.Indices.Count <= EmptyPartIndices);
        }

        // True when every LOD 1 subsection of the section is an empty part.
        private static bool IsEmptyPartSection(CarbinModel.Section section)
        {
            bool any = false;
            foreach (CarbinModel.SubSection sub in section.SubSections)
            {
                if (sub.Lod != 1)
                {
                    continue;
                }
                if (!IsEmptyPart(section, sub.Name))
                {
                    return false;
                }
                any = true;
            }
            return any;
        }

        // 'materials' (when given) collects the material names the converted model uses; 'renames' are
        // the user's fixes from the review (FH material -> FM4 material, before MaterialMap); 'unknown'
        // collects the materials no FM4 car uses that are left, with the sections they sit on.
        public static byte[] ConvertModel(byte[] data, bool lod0, StringBuilder report, List<string> materials, Dictionary<string, string> renames, Dictionary<string, List<string>> unknownOut)
        {
            return ConvertModel(data, lod0, report, materials, renames, unknownOut, null);
        }

        // damageReference: the same car's FM4 model, whose per-vertex damage words replace the source's (B1 test T4).
        public static byte[] ConvertModel(byte[] data, bool lod0, StringBuilder report, List<string> materials, Dictionary<string, string> renames, Dictionary<string, List<string>> unknownOut, CarbinModel damageReference)
        {
            CarbinModel model = new CarbinModel(data, true);
            ConvertToMotorsport4(model);
            if (damageReference != null && !model.IsMotorsport3)
            {
                string line = model.BorrowDamageWords(damageReference);
                if (report != null)
                {
                    report.AppendLine("  " + line);
                }
            }
            Dictionary<string, int> renamed = new Dictionary<string, int>();
            List<string[]> remove = new List<string[]>();
            foreach (CarbinModel.Section section in model.Sections)
            {
                foreach (CarbinModel.SubSection sub in section.SubSections)
                {
                    string to;
                    if (renames != null && renames.TryGetValue(sub.Name, out to))
                    {
                        to = (lod0 && to == "tail_light") ? "tail_light_lod0" : to;
                    }
                    else if (!AutoMap(sub.Name, out to))
                    {
                        continue;
                    }
                    if (to == null)
                    {
                        remove.Add(new string[] { section.Name, sub.Name });
                        continue;
                    }
                    if (lod0)
                    {
                        to = LodVariant(to, "x_lod0.carbin");   // tail_light_lod0, reverse_light_lod0
                    }
                    int count;
                    renamed.TryGetValue(sub.Name + " -> " + to, out count);
                    renamed[sub.Name + " -> " + to] = count + 1;
                    sub.Name = to;
                }
            }
            foreach (string[] pair in remove)
            {
                CarbinModel.Section owner = model.Find(pair[0]);
                if (owner.SubSections.Count > 1)
                {
                    model.RemoveSubSections(pair[0], pair[1]);
                }
                else
                {
                    owner.SubSections[0].Name = "black";
                }
            }
            if (materials != null)
            {
                foreach (CarbinModel.Section section in model.Sections)
                {
                    foreach (CarbinModel.SubSection sub in section.SubSections)
                    {
                        if (!materials.Contains(sub.Name))
                        {
                            materials.Add(sub.Name);
                        }
                    }
                }
            }
            if (report != null && renamed.Count > 0)
            {
                List<string> text = new List<string>();
                foreach (KeyValuePair<string, int> pair in renamed)
                {
                    text.Add(pair.Key + " (" + pair.Value + ")");
                }
                report.AppendLine("  materials: " + string.Join(", ", text.ToArray()) + ((remove.Count > 0) ? string.Format(", {0} 'deprecated' removed", remove.Count) : ""));
            }
            if (report != null)
            {
                // Material -> the sections it sits on.
                Dictionary<string, List<string>> unknown = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                List<string> order = new List<string>();
                foreach (CarbinModel.Section section in model.Sections)
                {
                    foreach (CarbinModel.SubSection sub in section.SubSections)
                    {
                        // what FM4 cars really use in a model of this LOD (Fm4MaterialUse); having a compiled
                        // shader is not enough: lights_amber has one and renders black on a normal car
                        if (Fm4MaterialUse.UsedByCarModel(sub.Lod, sub.Name))
                        {
                            continue;
                        }
                        List<string> sections;
                        if (!unknown.TryGetValue(sub.Name, out sections))
                        {
                            sections = new List<string>();
                            unknown[sub.Name] = sections;
                            order.Add(sub.Name);
                        }
                        if (!sections.Contains(section.Name))
                        {
                            sections.Add(section.Name);
                        }
                    }
                }
                if (order.Count > 0)
                {
                    List<string> text = new List<string>();
                    foreach (string name in order)
                    {
                        text.Add(name + " (" + string.Join(" ", unknown[name].ToArray()) + ")");
                        if (unknownOut != null)
                        {
                            List<string> all;
                            if (!unknownOut.TryGetValue(name, out all))
                            {
                                all = new List<string>();
                                unknownOut[name] = all;
                            }
                            foreach (string section in unknown[name])
                            {
                                if (!all.Contains(section))
                                {
                                    all.Add(section);
                                }
                            }
                        }
                    }
                    report.AppendLine("  WARNING materials no FM4 car uses, may render black: " + string.Join(", ", text.ToArray()));
                }
            }
            return model.ToBytes();
        }

        // FM4 CarAttribs version 17: the donor's file with the car's own gauges, driver placement,
        // part drops and animations (FH2 version 21 writes <animation Version="2" ... inrace="1"/>,
        // FM4 <Animation ... inrace="true" />; FH2 repeats each one per LOD). donorKept (may be null)
        // gets the donor's elements that stay and differ from the car's own (DonorElements).
        public static byte[] ConvertCarAttributes(string fh2, string fm4Donor, bool animations, StringBuilder report, List<string> donorKept)
        {
            return ConvertCarAttributes(fh2, fm4Donor, animations, report, donorKept, false);
        }

        // horizon: the car comes from FH1 / FH2, whose crash damage is raised (Port.ini [Damage] ScalarBonus; FM3 is fine as it is).
        public static byte[] ConvertCarAttributes(string fh2, string fm4Donor, bool animations, StringBuilder report, List<string> donorKept, bool horizon)
        {
            byte[] converted = ConvertCarAttributes(fh2, fm4Donor, animations, report, horizon);
            if (donorKept != null)
            {
                string text = Encoding.UTF8.GetString(converted);
                donorKept.AddRange(DonorElements(text, fh2));
                if (!fh2.Contains("<PartDropValues>") && text.Contains("<PartDropValues>"))
                {
                    donorKept.Add("Damage PartDropValues: the donor's (the car's file has none)");
                }
            }
            return converted;
        }

        // The top-level elements of the converted CarAttribs (but Animations, always the car's own or
        // none, and Damage and CarDebris, the car's or FM4's standard, ConvertDamage) that the car's
        // own file lacks or has with other values: what stays the donor's. Gauges the car has count as
        // its own (their repeated tick marks are cut). Compared as parsed XML, so only whitespace is
        // ignored: "0.0" against "0.000000" counts as different.
        private static List<string> DonorElements(string converted, string source)
        {
            List<string> kept = new List<string>();
            XmlDocument result = new XmlDocument();
            XmlDocument own = new XmlDocument();
            try
            {
                result.LoadXml(converted.Trim());
                own.LoadXml(source.Trim());
            }
            catch (XmlException ex)
            {
                kept.Add("compared not: " + ex.Message + " (the donor's file is the base, only gauges, driver, part drops and animations are the car's)");
                return kept;
            }
            foreach (XmlNode node in result.DocumentElement.ChildNodes)
            {
                XmlElement element = node as XmlElement;
                if (element == null || element.Name == "Animations" || element.Name == "Damage" || element.Name == "CarDebris")
                {
                    continue;
                }
                XmlElement mine = own.DocumentElement[element.Name];
                if (mine == null)
                {
                    kept.Add(element.Name + ": the donor's (the car's file has none)");
                }
                else if (element.Name != "Gauges" && mine.OuterXml != element.OuterXml)
                {
                    kept.Add(element.Name + ": the donor's (the car's differs)");
                }
            }
            return kept;
        }

        public static byte[] ConvertCarAttributes(string fh2, string fm4Donor, bool animations, StringBuilder report)
        {
            return ConvertCarAttributes(fh2, fm4Donor, animations, report, false);
        }

        public static byte[] ConvertCarAttributes(string fh2, string fm4Donor, bool animations, StringBuilder report, bool horizon)
        {
            fh2 = Regex.Replace(fh2, "<(\\w+) NumTicks=\"\\d+\">(.*?)</\\1>", delegate(Match gauge)
            {
                // Repeated marks kept once (in order), at most MaxTicks.
                List<string> ticks = new List<string>();
                MatchCollection marks = Regex.Matches(gauge.Groups[2].Value, "<TickMark\\b[^>]*/>");
                foreach (Match mark in marks)
                {
                    if (!ticks.Contains(mark.Value) && ticks.Count < MaxTicks)
                    {
                        ticks.Add(mark.Value);
                    }
                }
                if (ticks.Count == marks.Count)
                {
                    return gauge.Value;
                }
                if (report != null)
                {
                    report.AppendLine(string.Format("  carattribs: {0} gauge {1} -> {2} tick marks", gauge.Groups[1].Value, marks.Count, ticks.Count));
                }
                Match indent = Regex.Match(gauge.Groups[2].Value, "\\r?\\n([ \\t]*)<TickMark");
                string line = indent.Success ? indent.Value.Substring(0, indent.Value.Length - "<TickMark".Length) : "\r\n      ";
                Match close = Regex.Match(gauge.Groups[2].Value, "\\r?\\n[ \\t]*$");
                return "<" + gauge.Groups[1].Value + " NumTicks=\"" + ticks.Count + "\">" + line + string.Join(line, ticks.ToArray()) + (close.Success ? close.Value : "") + "</" + gauge.Groups[1].Value + ">";
            }, RegexOptions.Singleline);
            string result = fm4Donor;
            foreach (string element in new string[] { "DriverHandsSteeringWheelOverride", "DriverHandsOffset", "SteeringWheelAngleOverride", "Driver" })
            {
                Match from = Regex.Match(fh2, "<" + element + "\\b[^>]*/>");
                Match to = Regex.Match(result, "<" + element + "\\b[^>]*/>");
                if (from.Success && to.Success)
                {
                    result = result.Substring(0, to.Index) + from.Value + result.Substring(to.Index + to.Length);
                }
            }
            foreach (string block in new string[] { "Gauges", "PartDropValues" })
            {
                Match from = Regex.Match(fh2, "<" + block + ">.*?</" + block + ">", RegexOptions.Singleline);
                Match to = Regex.Match(result, "<" + block + ">.*?</" + block + ">", RegexOptions.Singleline);
                if (from.Success && to.Success)
                {
                    result = result.Substring(0, to.Index) + from.Value + result.Substring(to.Index + to.Length);
                }
            }
            result = ConvertDamage(result, fh2, report, horizon);
            Match target = Regex.Match(result, "[ \t]*<Animations>.*?</Animations>\r?\n?", RegexOptions.Singleline);
            if (!animations)
            {
                // No animation files: no <Animations> element (as on FM4 cars without moving parts).
                return Encoding.UTF8.GetBytes(target.Success ? result.Remove(target.Index, target.Length) : result);
            }
            Match source = Regex.Match(fh2, "<Animations>.*?</Animations>", RegexOptions.Singleline);
            if (!source.Success || !Regex.IsMatch(source.Value, "<animation\\b", RegexOptions.IgnoreCase))
            {
                // The car has no animations of its own: the donor's would point at bones and .gr2 files
                // the car lacks (AUD_R8GT_11 got the R8 V10's 12, doors and active wing, and crashed
                // the game while loading its model in the showroom).
                return Encoding.UTF8.GetBytes(target.Success ? result.Remove(target.Index, target.Length) : result);
            }
            target = Regex.Match(result, "<Animations>.*?</Animations>", RegexOptions.Singleline);
            if (target.Success)
            {
                StringBuilder list = new StringBuilder("<Animations>\r\n");
                List<string> seen = new List<string>();
                foreach (Match animation in Regex.Matches(source.Value, "<animation\\b([^>]*)/>", RegexOptions.IgnoreCase))
                {
                    string attributes = Regex.Replace(animation.Groups[1].Value, "\\s*Version=\"[^\"]*\"", "");
                    attributes = Regex.Replace(attributes, "inrace=\"1\"", "inrace=\"true\"");
                    attributes = attributes.Trim();
                    if (seen.Contains(attributes))
                    {
                        continue;
                    }
                    seen.Add(attributes);
                    list.Append("    <Animation " + attributes + " />\r\n");
                }
                list.Append("  </Animations>");
                result = result.Substring(0, target.Index) + list.ToString() + result.Substring(target.Index + target.Length);
            }
            return Encoding.UTF8.GetBytes(result);
        }

        // Damage tuning (2026-10-17, CarAttribs of all FM4/FH2/FH1/FM3 cars read, fm4dev\DonorLog\dmg_*.tsv).
        // FM4's standard, on 247 of 315 cars: DamageScalars at 5/35/98 (speed, m/s by the 98 top: 353
        // km/h) giving damage 0.5/1.5/2.5, PartDropGlobalScalar 1, DamageScrape from 0.01 to 0.3 with
        // ScrapeDamage 0.08, CarDebris with its four piece kinds. FH1 and FH2 have a soft preset no FM4
        // car uses on ~138 cars each (the P1): damage 1.0 only at 200 m/s, PartDropGlobalScalar 0.15,
        // scrape from 500 (off), CarDebris empty: the car stays whole after a crash at 200 km/h. The
        // car's own values are kept, each part that is softer than FM4's goes to FM4's standard:
        //   scalars   damage at 200 km/h (55.6 m/s, linear between the points) under 1.7; the common
        //             FM4 curves give 1.83 and 1.71, FH's soft one 0.30, the 60/120/180 one 0.2, the
        //             zeroed blocks (uninitialised, 18 FM4 and 37 FM3 cars) 0;
        //   value     the Damage multiplier outside 1..10 (zeroed blocks have 1.7e29);
        //   drops     PartDropGlobalScalar under 1 (every FM4 car with a real block has 1);
        //   scrape    starting over 0.3 or ScrapeDamage under 0.08, or missing;
        //   debris    CarDebris empty or missing.
        // The car's PartDropValues stay (the donor's only when the car has none).
        private const double DamageSpeed = 200 / 3.6;
        private const double MinimumDamage = 1.7;
        private static readonly double[][] StandardScalars = { new double[] { 5, 0.3, 0.5 }, new double[] { 35, 1.3, 1.5 }, new double[] { 98, 3.2, 2.5 } };
        private const string StandardScrape = "<DamageScrape ScrapeMinSpeed=\"0.010000\" ScrapeMaxSpeed=\"0.300000\" ScrapeRadius=\"1.300000\" ScrapeDamage=\"0.080000\" DamageBias=\"0.750000\" ScrapeBias=\"0.700000\" GlassDamageCausingBreak=\"0.005000\" />";
        private static readonly string[] StandardDebris = { "Mechanical", "Fiber", "Frame", "Crumb" };

        private static string ConvertDamage(string result, string source, StringBuilder report, bool horizon)
        {
            string newline = result.Contains("\r\n") ? "\r\n" : "\n";
            List<string> notes = new List<string>();

            // CarDebris
            Match sourceDebris = Regex.Match(source, "<CarDebris\\s*/>|<CarDebris>.*?</CarDebris>", RegexOptions.Singleline);
            Match debris = Regex.Match(result, "([ \\t]*)(<CarDebris\\s*/>|<CarDebris>.*?</CarDebris>)", RegexOptions.Singleline);
            if (debris.Success)
            {
                string indent = debris.Groups[1].Value;
                string text;
                if (sourceDebris.Success && Regex.IsMatch(sourceDebris.Value, "<\\w+\\b[^>]*MaxDropCount"))
                {
                    text = sourceDebris.Value;
                }
                else
                {
                    StringBuilder pieces = new StringBuilder("<CarDebris>" + newline);
                    foreach (string piece in StandardDebris)
                    {
                        pieces.Append(indent + "  <" + piece + " MaxDropCount_DamageCosmetic=\"1000\" MaxDropCount_DamageLimited=\"1000\" MaxDropCount_DamageFull=\"1000\" MinDropForce=\"1.000000\" MaxDropForce=\"100.000000\" Area=\"\" />" + newline);
                    }
                    text = pieces.Append(indent + "</CarDebris>").ToString();
                    notes.Add("debris FM4 standard (the car's " + (sourceDebris.Success ? "is empty" : "file has none") + ")");
                }
                result = result.Substring(0, debris.Groups[2].Index) + text + result.Substring(debris.Groups[2].Index + debris.Groups[2].Length);
            }

            // Damage
            Match sourceDamage = Regex.Match(source, "<Damage\\b[^>]*?(/>|>.*?</Damage>)", RegexOptions.Singleline);
            Match damage = Regex.Match(result, "([ \\t]*)(<Damage\\b[^>]*?(/>|>.*?</Damage>))", RegexOptions.Singleline);
            string own = sourceDamage.Success ? sourceDamage.Value : "";
            string ownTag = Regex.Match(own, "^<Damage\\b[^>]*>").Value;

            double value = Number(Attribute(ownTag, "value"));
            string valueText = (value >= 1 && value <= 10) ? Attribute(ownTag, "value") : "1.000000";
            if (valueText != Attribute(ownTag, "value"))
            {
                notes.Add("multiplier 1 (the car's " + (Attribute(ownTag, "value") ?? "none") + ")");
            }

            List<double[]> points = new List<double[]>();
            List<string> scalarLines = new List<string>();
            foreach (Match scalar in Regex.Matches(own, "<DamageScalars\\b[^>]*/>"))
            {
                points.Add(new double[] { Number(Attribute(scalar.Value, "speed")), Number(Attribute(scalar.Value, "radius")), Number(Attribute(scalar.Value, "damage")) });
                scalarLines.Add(scalar.Value);
            }
            double at = DamageAt(points, DamageSpeed);
            if (!(at >= MinimumDamage))
            {
                scalarLines.Clear();
                foreach (double[] point in StandardScalars)
                {
                    scalarLines.Add(string.Format(CultureInfo.InvariantCulture, "<DamageScalars speed=\"{0:0.000000}\" radius=\"{1:0.000000}\" damage=\"{2:0.000000}\" />", point[0], point[1], point[2]));
                }
                notes.Add(string.Format(CultureInfo.InvariantCulture, "scalars FM4 standard (the car's: {0} at 200 km/h, under {1})", (points.Count > 0) ? at.ToString("0.00", CultureInfo.InvariantCulture) : "none", MinimumDamage));
            }

            // User 2026-10-27 (Bowler in a pack, C7 Stingray): +1 on the damage of every DamageScalars point deforms
            // a ported car much better than FM4's standard curve.
            double bonus = horizon ? PortSettings.GetDouble("Damage", "ScalarBonus", 1) : 0;
            if (bonus != 0)
            {
                for (int i = 0; i < scalarLines.Count; i++)
                {
                    string line = scalarLines[i];
                    scalarLines[i] = Regex.Replace(line, "\\bdamage=\"([^\"]*)\"", delegate(Match m)
                    {
                        return "damage=\"" + (Number(m.Groups[1].Value) + bonus).ToString("0.000000", CultureInfo.InvariantCulture) + "\"";
                    });
                }
                notes.Add(string.Format(CultureInfo.InvariantCulture, "scalar damage {0:+0.##;-0.##} on every point (Port.ini [Damage] ScalarBonus)", bonus));
            }

            Match drops = Regex.Match(damage.Success ? damage.Value : "", "<PartDropValues>.*?</PartDropValues>", RegexOptions.Singleline);
            if (!drops.Success)
            {
                drops = Regex.Match(own, "<PartDropValues>.*?</PartDropValues>", RegexOptions.Singleline);
            }

            Match global = Regex.Match(own, "<PartDropGlobalScalar\\b[^>]*/>");
            string globalLine = global.Value;
            if (!(Number(Attribute(global.Value, "value")) >= 1 && Number(Attribute(global.Value, "value")) <= 10))
            {
                globalLine = "<PartDropGlobalScalar value=\"1.000000\" />";
                notes.Add("part drop scale 1 (the car's " + (Attribute(global.Value, "value") ?? "none") + ")");
            }

            Match scrape = Regex.Match(own, "<DamageScrape\\b[^>]*/>");
            string scrapeLine = scrape.Value;
            double scrapeFrom = Number(Attribute(scrape.Value, "ScrapeMinSpeed"));
            double scrapeDamage = Number(Attribute(scrape.Value, "ScrapeDamage"));
            double scrapeRadius = Number(Attribute(scrape.Value, "ScrapeRadius"));
            if (!(scrapeFrom >= 0 && scrapeFrom <= 0.3 && scrapeDamage >= 0.08 && scrapeRadius > 0 && scrapeRadius <= 10))
            {
                scrapeLine = StandardScrape;
                notes.Add(scrape.Success ? string.Format(CultureInfo.InvariantCulture, "scrape FM4 standard (the car's from {0}, damage {1})", Attribute(scrape.Value, "ScrapeMinSpeed"), Attribute(scrape.Value, "ScrapeDamage")) : "scrape FM4 standard (the car's file has none)");
            }

            string at0 = damage.Success ? damage.Groups[1].Value : "  ";
            string inner = at0 + "  ";
            StringBuilder block = new StringBuilder("<Damage Version=\"3\" value=\"" + valueText + "\">" + newline);
            foreach (string line in scalarLines)
            {
                block.Append(inner + line + newline);
            }
            if (drops.Success)
            {
                block.Append(inner + drops.Value + newline);
            }
            block.Append(inner + globalLine + newline);
            block.Append(inner + scrapeLine + newline);
            block.Append(at0 + "</Damage>");
            if (damage.Success)
            {
                result = result.Substring(0, damage.Groups[2].Index) + block.ToString() + result.Substring(damage.Groups[2].Index + damage.Groups[2].Length);
            }
            else
            {
                Match before = Regex.Match(result, "[ \\t]*<Tires\\b|</CarAttributes>");
                if (before.Success)
                {
                    result = result.Substring(0, before.Index) + at0 + block.ToString() + newline + result.Substring(before.Index);
                }
            }

            if (report != null)
            {
                report.AppendLine("  carattribs damage: " + ((notes.Count == 0) ? "the car's own" : "the car's, but " + string.Join("; ", notes.ToArray())));
            }
            return result;
        }

        // Damage at a speed on the DamageScalars curve: linear between the points, the end values past them.
        private static double DamageAt(List<double[]> points, double speed)
        {
            if (points.Count == 0)
            {
                return 0;
            }
            List<double[]> sorted = new List<double[]>(points);
            sorted.Sort(delegate(double[] a, double[] b) { return a[0].CompareTo(b[0]); });
            if (speed <= sorted[0][0])
            {
                return sorted[0][2];
            }
            for (int i = 1; i < sorted.Count; i++)
            {
                if (speed <= sorted[i][0])
                {
                    double[] a = sorted[i - 1];
                    double[] b = sorted[i];
                    return (b[0] > a[0]) ? a[2] + (b[2] - a[2]) * (speed - a[0]) / (b[0] - a[0]) : b[2];
                }
            }
            return sorted[sorted.Count - 1][2];
        }

        private static string Attribute(string element, string name)
        {
            Match match = Regex.Match(element ?? "", "\\b" + Regex.Escape(name) + "=\"([^\"]*)\"");
            return match.Success ? match.Groups[1].Value : null;
        }

        private static double Number(string text)
        {
            double number;
            return (text != null && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) ? number : double.NaN;
        }

        // FM4 ShaderSettings.xml: only the materials the converted models use (FH2-only ones under
        // their FM4 names, tail lights also for the _lod0 variant) and only the parameters FM4 has.
        // FM4 cars list at most 24 materials; the FH2 P1 lists 33, most for materials it renamed.
        // 'renames' are the review's material choices, which the models get before MaterialMap
        // (ConvertModel): without them an entry such as fullbeam stayed under its FH name, no model
        // used it any more and it was dropped, so the chosen FM4 material lost the car's values.
        public static byte[] ConvertShaderSettings(string fh2, List<string> materials, StringBuilder report)
        {
            return ConvertShaderSettings(fh2, materials, null, report);
        }

        public static byte[] ConvertShaderSettings(string fh2, List<string> materials, Dictionary<string, string> renames, StringBuilder report)
        {
            List<string> order = new List<string>();
            Dictionary<string, List<string[]>> settings = new Dictionary<string, List<string[]>>(StringComparer.Ordinal);
            List<string> dropped = new List<string>();
            List<string> parameters = new List<string>(ShaderParameters);
            int read = 0;
            Match root = Regex.Match(fh2, "<ShaderSettings>(.*)</ShaderSettings>", RegexOptions.Singleline);
            foreach (Match section in Regex.Matches(root.Success ? root.Groups[1].Value : fh2, "<(\\w+)>(.*?)</\\1>", RegexOptions.Singleline))
            {
                read++;
                // "cockpit_<material>" sets <material> of the cockpit model (FM4 MP4-12C: cockpit_bump_leather2).
                string prefix = section.Groups[1].Value.StartsWith("cockpit_") ? "cockpit_" : "";
                string name = section.Groups[1].Value.Substring(prefix.Length);
                string to;
                List<string> targets = new List<string>();
                if ((renames != null && renames.TryGetValue(name, out to)) || (AutoMap(name, out to) && to != null))
                {
                    // The lod0 model names its lights tail_light_lod0 / reverse_light_lod0 (LodVariant).
                    targets.Add(to);
                    string lod0 = LodVariant(to, "x_lod0.carbin");
                    if (lod0 != to && prefix.Length == 0)
                    {
                        targets.Add(lod0);
                    }
                }
                else if (to == null && AutoMap(name, out to))
                {
                    // Mapped to nothing (dropped from the models): its settings go too.
                }
                else
                {
                    targets.Add(name);
                }
                foreach (string material in targets)
                {
                    if (materials != null && !materials.Contains(material))
                    {
                        continue;
                    }
                    string target = prefix + material;
                    List<string[]> values;
                    if (!settings.TryGetValue(target, out values))
                    {
                        values = new List<string[]>();
                        settings[target] = values;
                        order.Add(target);
                    }
                    foreach (Match value in Regex.Matches(section.Groups[2].Value, "<(\\w+)\\b[^>]*/>"))
                    {
                        string parameter = value.Groups[1].Value;
                        bool known = parameters.Exists(delegate(string p) { return string.Equals(p, parameter, StringComparison.OrdinalIgnoreCase); });
                        if (!known)
                        {
                            if (!dropped.Contains(parameter))
                            {
                                dropped.Add(parameter);
                            }
                            continue;
                        }
                        if (!values.Exists(delegate(string[] v) { return v[0] == parameter; }))
                        {
                            values.Add(new string[] { parameter, value.Value });
                        }
                    }
                }
            }
            StringBuilder xml = new StringBuilder("<ShaderSettings>\r\n");
            int written = 0;
            foreach (string name in order)
            {
                if (settings[name].Count == 0)
                {
                    continue;
                }
                written++;
                xml.Append("  <" + name + ">\r\n");
                foreach (string[] value in settings[name])
                {
                    xml.Append("    " + value[1] + "\r\n");
                }
                xml.Append("  </" + name + ">\r\n");
            }
            xml.Append("</ShaderSettings>\r\n");
            if (report != null)
            {
                report.AppendLine(string.Format("  shadersettings: {0} -> {1} materials{2}", read, written, (dropped.Count > 0) ? "; parameters FM4 lacks left out: " + string.Join(", ", dropped.ToArray()) : ""));
            }
            return Encoding.UTF8.GetBytes(xml.ToString());
        }

        private void BuildWheelZips()
        {
            string donorWheelMedia = this.DonorScalar("SELECT w.MediaName FROM List_Wheels w JOIN Data_Car c ON c.StockWheelID = w.ID WHERE c.Id = ?", this.donorId) as string;
            foreach (long id in this.wheelIds)
            {
                string media = this.source.Scalar("SELECT MediaName FROM List_Wheels WHERE ID = ?", id) as string;
                if (media == null && (this.motorsport3 || this.fm2Stage))
                {
                    media = this.target.Scalar("SELECT MediaName FROM List_Wheels WHERE ID = ?", id) as string;   // a new id (Motorsport3Wheel)
                }
                string sourcePath = GamePaths.FindMedia(this.SourceRoot, "wheels", media);
                string donorPath = (donorWheelMedia != null) ? (GamePaths.FindMedia(this.DonorRoot ?? this.TargetRoot, "wheels", donorWheelMedia) ?? GamePaths.FindMedia(this.TargetRoot, "wheels", donorWheelMedia)) : null;
                if (sourcePath == null || donorPath == null)
                {
                    this.Line("wheel {0}: zip not found, skipped", media);
                    continue;
                }
                string path = Path.Combine(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "wheels"), media + ".zip");
                object original = this.originalWheels.Contains(id) ? (object) true : null;
                if (File.Exists(path) && original != null)
                {
                    this.Line("wheel {0}: an FM4 wheel, kept", media);
                    continue;
                }
                ForzaZip from = new ForzaZip(sourcePath);
                ForzaZip donor = new ForzaZip(donorPath);
                ForzaZip wheel = ForzaZip.CreateLike(donor);
                foreach (ForzaZip.Entry entry in from.Entries)
                {
                    string name = entry.Name.ToLowerInvariant();
                    byte[] data = from.Read(entry);
                    if (name.EndsWith(".carbin"))
                    {
                        CarbinModel model = new CarbinModel(data, true);
                        ConvertToMotorsport4(model);
                        data = model.ToBytes();
                    }
                    else if (name.EndsWith(".xds") && this.motorsport3)
                    {
                        data = ConvertMotorsport3Texture(data, name);
                    }
                    wheel.Add(name, data);
                }
                this.RebuildWheelLod0(wheel, media);
                ForzaZip.Entry version = donor.Find("versiondata.xml");
                if (version != null && wheel.Find("versiondata.xml") == null)
                {
                    wheel.Add("versiondata.xml", donor.Read(version));
                    this.donorContent.Add("wheel " + media + " versiondata.xml: the donor wheel's (" + this.game + " has none)");
                }
                this.donorFormat.Add("wheel " + media + " zip: entry headers and zipmanifest.xml line of the donor wheel " + donorWheelMedia);
                this.SaveNew(wheel, path);
                this.Line("wheel zip: {0} ({1} entries)", this.Relative(path), wheel.Entries.Count);
            }
        }

        // The "offroad" choice: FM4's Hummer H1 tire models (TireA/B/C of HUM_H1ALpha_06, in the base game) as they are, under
        // this car's name, so the car gets the deep tread of an off-road tire whatever its source has.
        private void OffroadTire(string kind, string wheels)
        {
            const string offroad = "HUM_H1ALpha_06";
            string sourcePath = GamePaths.FindMedia(this.TargetRoot, "wheels", "Tire" + kind + offroad);
            if (sourcePath == null || !sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                this.Line("WARNING tire Tire{0}{1}: the offroad tire Tire{0}{2}.zip is not in the FM4 folder, the generic tire stays", kind, this.CarName, offroad);
                return;
            }
            string path = Path.Combine(wheels, "Tire" + kind + this.CarName + ".zip");
            ForzaZip from = new ForzaZip(sourcePath);
            ForzaZip tire = ForzaZip.CreateLike(from);
            foreach (ForzaZip.Entry entry in from.Entries)
            {
                if (entry.IsDirectory)
                {
                    continue;
                }
                tire.Add(entry.Name.ToLowerInvariant().Replace(("tire" + kind + offroad).ToLowerInvariant(), ("tire" + kind + this.CarName).ToLowerInvariant()), from.Read(entry));
            }
            this.SaveNew(tire, path);
            this.Line("tire zip: {0} ({1} entries, the H1's offroad tire as it is)", this.Relative(path), tire.Entries.Count);
        }

        // A car with its own tire model (the games ship Media\wheels\TireA/B/C<wheel>.zip beside the generic TireA/B/C.zip: the
        // same textures and a heavier tire .carbin, e.g. FH1 / FH2 Raptor and Wrangler; FM3 has none) brings it: the .carbin
        // converted like the wheel's, the textures as they are, laid out like FM4's generic tire zip. An FM4 file of that name
        // stays (D-16). The game looks the zip up by the NAME OF THE CAR (checked in game 2026-10-27: TireA/B/CFOR_SVTRaptor_12_FH.zip
        // is used by the car FOR_SVTRaptor_12_FH, whose wheel is FOR_SVTRaptor_12; named after the wheel it showed the generic tire).
        private void BuildTireZips()
        {
            string wheels = Path.Combine(Path.Combine(this.TargetRoot, "Media"), "wheels");
            // the source's tire zips are named after its car (its stock wheel has the same name in every case seen)
            string stockWheel = this.source.Scalar("SELECT w.MediaName FROM List_Wheels w JOIN Data_Car c ON c.StockWheelID = w.ID WHERE c.Id = ?", this.carId) as string;
            foreach (string kind in new string[] { "A", "B", "C" })
            {
                if (string.Equals(this.Tires, "offroad", StringComparison.OrdinalIgnoreCase))
                {
                    this.OffroadTire(kind, wheels);
                    continue;
                }
                string sourceName = this.MediaName;
                string sourcePath = GamePaths.FindMedia(this.SourceRoot, "wheels", "Tire" + kind + sourceName);
                if (sourcePath == null && stockWheel != null)
                {
                    sourceName = stockWheel;
                    sourcePath = GamePaths.FindMedia(this.SourceRoot, "wheels", "Tire" + kind + sourceName);
                }
                if (sourcePath == null || !sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string media = this.CarName;
                string path = Path.Combine(wheels, "Tire" + kind + media + ".zip");
                string generic = GamePaths.FindMedia(this.TargetRoot, "wheels", "Tire" + kind);
                if (File.Exists(path) || generic == null)
                {
                    this.Line("tire Tire{0}{1}: {2}", kind, media, File.Exists(path) ? "an FM4 file, kept" : "FM4's generic Tire" + kind + ".zip not found, skipped");
                    continue;
                }
                ForzaZip from = new ForzaZip(sourcePath);
                ForzaZip model = new ForzaZip(generic);
                ForzaZip tire = ForzaZip.CreateLike(model);
                int models = 0;
                foreach (ForzaZip.Entry entry in from.Entries)
                {
                    string name = entry.Name.ToLowerInvariant().Replace(("tire" + kind + sourceName).ToLowerInvariant(), ("tire" + kind + media).ToLowerInvariant());
                    if (name == "buildnumber.txt" || name.EndsWith("_build_report.html"))
                    {
                        continue;
                    }
                    byte[] data = from.Read(entry);
                    if (name.EndsWith(".carbin"))
                    {
                        CarbinModel carbin = new CarbinModel(data, true);
                        ConvertToMotorsport4(carbin);
                        data = carbin.ToBytes();
                        models++;
                    }
                    tire.Add(name, data);
                }
                ForzaZip.Entry build = model.Find("buildnumber.txt");
                if (build != null)
                {
                    tire.Add("buildnumber.txt", model.Read(build));
                }
                ForzaZip.Entry version = model.Find("versiondata.xml");
                if (version != null && tire.Find("versiondata.xml") == null)
                {
                    tire.Add("versiondata.xml", model.Read(version));
                    this.donorFormat.Add("tire " + kind + media + " versiondata.xml: the generic tire's (" + this.game + " has none)");
                }
                this.SaveNew(tire, path);
                this.Line("tire zip: {0} ({1} entries, {2} model converted)", this.Relative(path), tire.Entries.Count, models);
            }
        }

        // In LOD 0 the tire technique draws the separate FM4 tire (Media\wheels\tirea: tread and sidewall
        // textures), so a LOD 1 tire copied there renders solid black (GMC_YukonXL_01, checked by the user
        // 2026-10-17). It becomes inner_rim, a LOD 0 rim material (every FM4 wheel) sampling wheel_lod0.xds,
        // which is a copy of the wheel.xds its UVs map.
        private static void RenameLod0Tire(CarbinModel.Section section)
        {
            foreach (CarbinModel.SubSection sub in section.SubSections)
            {
                if (sub.Lod == 0 && sub.Name == "tire")
                {
                    sub.Name = "inner_rim";
                }
            }
        }

        // The _cockpit.carbin header holds the driver rig, big-endian floats in model axes (decoded on the 313
        // FM4 cockpits, 2026-10-17):
        //   0x1C8..0x218  three left/right pairs of hand grips (vec4 each, w 0): on the rim circle 20 degrees
        //                 above the centre, on the rim's outer edge, inner edge and tube middle (GripRadii)
        //   0x228         the steering wheel pivot: the steering_wheel origin (x exact, y/z within 1-4 cm)
        //   0x238         the wheel axis: the normal of the wheel's plane, pointing back (0.8 degrees median)
        //   0x248, 0x258  two points (0x258 on the centre console: the shifter, mirrored with the wheel side)
        //   0x270..0x2FF  four records point / direction / value (eye-level points looking forward)
        // FH traffic cockpits (placeholders) have zeros there: the wheel then turns about the car's origin and
        // swings into the windscreen, and the hands meet in one point (GMC_YukonXL_01; the Syclone's whole
        // cockpit file turned in place, its wheel section alone did not; checked by the user 2026-10-17).
        // When the pivot is 0,0,0: pivot, axis and grips come from the steering_wheel geometry, the other
        // records from the donor's cockpit relative to its wheel (x mirrored when the wheels sit on opposite
        // sides). The same when the pivot is not on the car's own wheel: a source without a _cockpit file gets
        // the donor's header, whose rig belongs to the donor's wheel (FOR_FocusZX4_05 left-hand drive with the
        // right-hand drive Fiesta 09 / Supra RZ 98 pivot: the wheel swung over the roof, user 2026-10-20). The
        // side is the sign of the steering_wheel x: on 312 of 313 FM4 cockpits the pivot x has the same sign
        // (McLaren F1 GTR centred), x < 0 = left (233), > 0 = right (80) (fm4dev\Traffic\tools\drivesurvey).
        private const int RigGrips = 0x1C8;
        private const int RigPivot = 0x228;
        private const int RigAxis = 0x238;
        private const int RigRest = 0x248;
        private const int RigEnd = 0x300;
        // Grip radii against the measured rim (289 FM4 cockpits, fm4dev\Traffic\tools\handsurvey, medians):
        // outer edge x 0.995, inner edge x 1.083, tube middle x 1.031. Rim = vertices beyond 75% of the
        // largest radius about the pivot, in the wheel's plane.
        private static readonly float[] GripRadii = { 0.995f, 1.083f, 1.031f };
        // FM4's grips sit 20 degrees above the centre (median and quartiles, 289 cockpits); the user first
        // asked for 9:15 (0), then chose FM4's standard (2026-10-20).
        private const double GripAngle = 20.0 * Math.PI / 180.0;

        private void CockpitRig(ForzaZip car, ForzaZip donor, string prefix, string donorPrefix)
        {
            ForzaZip.Entry entry = car.Find(prefix + "_cockpit.carbin");
            if (entry == null)
            {
                return;
            }
            CarbinModel cockpit = new CarbinModel(car.Read(entry));
            CarbinModel.Section wheel = cockpit.Find("steering_wheel");
            if (wheel == null || cockpit.Head.Length < RigEnd)
            {
                return;
            }
            Microsoft.Xna.Framework.Vector3 pivot = new Microsoft.Xna.Framework.Vector3(wheel.Bounds[0], wheel.Bounds[1], wheel.Bounds[2]);
            Microsoft.Xna.Framework.Vector3 old = RigVector(cockpit.Head, RigPivot);
            string reason;
            if (old == Microsoft.Xna.Framework.Vector3.Zero)
            {
                reason = "driver rig in the header was empty (wheel pivot 0,0,0)";
            }
            else if (this.fm2Stage)
            {
                // The wheel is the FM2 car's own, the header's rig the donor's: it fits only by chance (AB Flug
                // Supra, whose wheel is the donor's twin; the Foose IS300's hands floated off the rim).
                reason = string.Format(CultureInfo.InvariantCulture, "FM2 stage: the header's rig is the donor's (pivot {0:F4}, {1:F4}, {2:F4}), the steering wheel the car's own", old.X, old.Y, old.Z);
            }
            else if (Math.Abs(old.X - pivot.X) > RigOffWheel || Microsoft.Xna.Framework.Vector3.Distance(old, pivot) > RigOffWheelFar)
            {
                reason = string.Format(CultureInfo.InvariantCulture, "driver rig in the header is not on this car's steering wheel (pivot {0:F4}, {1:F4}, {2:F4}, {3} drive; wheel at x {4:F4}, {5} drive)", old.X, old.Y, old.Z, DriveSide(old.X), pivot.X, DriveSide(pivot.X));
            }
            else
            {
                return;
            }
            Microsoft.Xna.Framework.Vector3 axis = WheelNormal(wheel);
            Microsoft.Xna.Framework.Vector3 up = Microsoft.Xna.Framework.Vector3.Normalize(Microsoft.Xna.Framework.Vector3.Cross(axis, Microsoft.Xna.Framework.Vector3.UnitX));
            if (up.Y < 0)
            {
                up = -up;
            }
            float[] rim = RimRadii(wheel, pivot, axis, up);
            float[] radii = { rim[0], rim[1], (rim[0] + rim[1]) / 2 };
            for (int g = 0; g < GripRadii.Length; g++)
            {
                float across = (float) (GripRadii[g] * radii[g] * Math.Cos(GripAngle));
                float above = (float) (GripRadii[g] * radii[g] * Math.Sin(GripAngle));
                WriteRigVector(cockpit.Head, RigGrips + (g * 32), pivot + (across * Microsoft.Xna.Framework.Vector3.UnitX) + (above * up));
                WriteRigVector(cockpit.Head, RigGrips + (g * 32) + 16, pivot - (across * Microsoft.Xna.Framework.Vector3.UnitX) + (above * up));
            }
            WriteRigVector(cockpit.Head, RigPivot, pivot);
            WriteRigVector(cockpit.Head, RigAxis, axis);
            string rest = "left as they were (the donor has none)";
            ForzaZip.Entry template = donor.Find(donorPrefix + "_cockpit.carbin");
            CarbinModel donorCockpit = (template != null) ? new CarbinModel(donor.Read(template)) : null;
            CarbinModel.Section donorWheel = (donorCockpit != null) ? donorCockpit.Find("steering_wheel") : null;
            if (donorWheel != null && donorCockpit.Head.Length >= RigEnd && RigVector(donorCockpit.Head, RigPivot) != Microsoft.Xna.Framework.Vector3.Zero)
            {
                Microsoft.Xna.Framework.Vector3 donorPivot = RigVector(donorCockpit.Head, RigPivot);
                bool mirror = Math.Sign(donorPivot.X) != Math.Sign(pivot.X);
                Buffer.BlockCopy(donorCockpit.Head, RigRest, cockpit.Head, RigRest, RigEnd - RigRest);
                // Points: 0x248, 0x258, then the four records (point, direction, value) from 0x270, 36 bytes apart.
                List<int> points = new List<int> { 0x248, 0x258 };
                List<int> directions = new List<int>();
                for (int r = 0; r < 4; r++)
                {
                    points.Add(0x270 + (r * 36));
                    directions.Add(0x270 + (r * 36) + 16);
                }
                foreach (int offset in points)
                {
                    Microsoft.Xna.Framework.Vector3 p = RigVector(cockpit.Head, offset);
                    if (p == Microsoft.Xna.Framework.Vector3.Zero)
                    {
                        continue;
                    }
                    Microsoft.Xna.Framework.Vector3 relative = p - donorPivot;
                    relative.X = mirror ? -relative.X : relative.X;
                    WriteRigVector(cockpit.Head, offset, pivot + relative);
                }
                foreach (int offset in directions)
                {
                    Microsoft.Xna.Framework.Vector3 d = RigVector(cockpit.Head, offset);
                    d.X = mirror ? -d.X : d.X;
                    WriteRigVector(cockpit.Head, offset, d);
                }
                rest = "the donor's, moved with the wheel" + (mirror ? " and mirrored" : "");
                this.donorContent.Add(entry.Name + ": cockpit rig points but the wheel and grips (shifter, eye-level records), the donor's moved to the wheel");
            }
            car.Replace(entry, cockpit.ToBytes());
            this.Line("  {0}: {1}: pivot {2:F4}, {3:F4}, {4:F4}, axis {5:F4}, {6:F4}, {7:F4}, hand grips from the wheel; other points {8}", entry.Name, reason, pivot.X, pivot.Y, pivot.Z, axis.X, axis.Y, axis.Z, rest);
        }

        // An FM4 pivot sits on the steering_wheel origin: x exact, y / z within 1-4 cm.
        private const float RigOffWheel = 0.03f;
        private const float RigOffWheelFar = 0.15f;

        private static string DriveSide(float x)
        {
            return (Math.Abs(x) < 0.05f) ? "centre" : ((x < 0) ? "left-hand" : "right-hand");
        }

        // Outer and inner radius of the rim about the pivot, in the wheel's plane (x and up).
        private static float[] RimRadii(CarbinModel.Section wheel, Microsoft.Xna.Framework.Vector3 pivot, Microsoft.Xna.Framework.Vector3 axis, Microsoft.Xna.Framework.Vector3 up)
        {
            CarbinModel.VertexBuffer buffer = (wheel.Lod0.Count > 0) ? wheel.Lod0 : wheel.Lod;
            double[] p = CarbinModel.Positions(wheel, buffer);
            List<double> radii = new List<double>();
            double outer = 0;
            for (int i = 0; i < buffer.Count; i++)
            {
                Microsoft.Xna.Framework.Vector3 d = new Microsoft.Xna.Framework.Vector3((float) p[i * 3], (float) p[(i * 3) + 1], (float) p[(i * 3) + 2]) - pivot;
                double u = d.X;
                double v = Microsoft.Xna.Framework.Vector3.Dot(d, up);
                double r = Math.Sqrt((u * u) + (v * v));
                radii.Add(r);
                outer = Math.Max(outer, r);
            }
            double inner = outer;
            foreach (double r in radii)
            {
                if (r >= 0.75 * outer)
                {
                    inner = Math.Min(inner, r);
                }
            }
            return new float[] { (float) outer, (float) inner };
        }

        // The normal of the wheel's plane (smallest axis of its vertex spread), pointing back (+z).
        private static Microsoft.Xna.Framework.Vector3 WheelNormal(CarbinModel.Section wheel)
        {
            CarbinModel.VertexBuffer buffer = (wheel.Lod0.Count > 0) ? wheel.Lod0 : wheel.Lod;
            double[] p = CarbinModel.Positions(wheel, buffer);
            int count = buffer.Count;
            double[] mean = new double[3];
            for (int i = 0; i < count; i++)
            {
                for (int k = 0; k < 3; k++)
                {
                    mean[k] += p[(i * 3) + k] / count;
                }
            }
            double[,] spread = new double[3, 3];
            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        spread[j, k] += (p[(i * 3) + j] - mean[j]) * (p[(i * 3) + k] - mean[k]);
                    }
                }
            }
            // Power iteration on (trace I - spread): its largest axis is the spread's smallest.
            double trace = spread[0, 0] + spread[1, 1] + spread[2, 2];
            double[] v = { 0, 0.3, 1 };
            for (int iteration = 0; iteration < 200; iteration++)
            {
                double[] w = new double[3];
                for (int j = 0; j < 3; j++)
                {
                    w[j] = trace * v[j];
                    for (int k = 0; k < 3; k++)
                    {
                        w[j] -= spread[j, k] * v[k];
                    }
                }
                double length = Math.Sqrt((w[0] * w[0]) + (w[1] * w[1]) + (w[2] * w[2]));
                if (length < 1e-12)
                {
                    break;
                }
                v = new double[] { w[0] / length, w[1] / length, w[2] / length };
            }
            Microsoft.Xna.Framework.Vector3 normal = new Microsoft.Xna.Framework.Vector3((float) v[0], (float) v[1], (float) v[2]);
            return (normal.Z < 0) ? -normal : normal;
        }

        private static Microsoft.Xna.Framework.Vector3 RigVector(byte[] head, int offset)
        {
            return new Microsoft.Xna.Framework.Vector3(ReadHeadFloat(head, offset), ReadHeadFloat(head, offset + 4), ReadHeadFloat(head, offset + 8));
        }

        private static void WriteRigVector(byte[] head, int offset, Microsoft.Xna.Framework.Vector3 value)
        {
            float[] parts = { value.X, value.Y, value.Z };
            for (int i = 0; i < 3; i++)
            {
                byte[] bytes = BitConverter.GetBytes(parts[i]);
                for (int k = 0; k < 4; k++)
                {
                    head[offset + (i * 4) + k] = bytes[3 - k];
                }
            }
        }

        // CarAttribs DriverHandsOffset places the driver's hands: on 289 of 307 FM4 cars it is the _cockpit
        // steering_wheel origin (model axes) plus (0, -0.403, 0.425) (x within 5 mm; y -0.418..-0.391,
        // z 0.404..0.454 for 80%). FH traffic cars have values unrelated to their wheel (GMC_YukonXL_01:
        // 0.083, -0.420, 0.394 for a wheel at x -0.428): no hands in view; with the offset rebuilt they
        // showed at the wheel (user, 2026-10-17; the wheel's own turning comes from CockpitRig). When x is
        // more than 3 cm off the wheel, the offset is rebuilt from the wheel. On a traffic car y and z count
        // too: FOR_FocusZX4_05 and FOR_F350SuperDuty_08 share -0.359, 0.390, -0.019, a placeholder whose x
        // fell within 3 cm of the Focus wheel while y sat 6 cm off FM4's rule; the hands showed as black blobs
        // up by the windscreen (user, 2026-10-20). Measured from the rig pivot instead (289 FM4 cars,
        // handsurvey) the offset is (0, -0.402, 0.410), within 1 cm for half of them: FM4's pivot sits 1.4 cm
        // behind the origin, a rebuilt pivot on it, so the origin rule left rebuilt hands 1.5 cm off (user saw
        // the hand's grip hole beside the rim, 2026-10-20). The origin rule stays for a cockpit with no pivot.
        private static readonly Microsoft.Xna.Framework.Vector3 HandsFromWheel = new Microsoft.Xna.Framework.Vector3(0f, -0.403f, 0.425f);
        private static readonly Microsoft.Xna.Framework.Vector3 HandsFromPivot = new Microsoft.Xna.Framework.Vector3(0f, -0.402f, 0.410f);
        private const float HandsOffWheel = 0.03f;

        // exhaust.txt in the stage folder (one "x y z" tip per line, model axes, # comments) replaces the exhaust
        // locators of the main carbin, i.e. where the backfire flames come out (the donor's single locator is not at
        // this car's pipes). CarbinModel.SetExhaustTips grows / shrinks the header.
        private void ApplyExhaustFile(ForzaZip car, string prefix)
        {
            string path = Path.Combine(this.SourceRoot, "exhaust.txt");
            ForzaZip.Entry entry = car.Find(prefix + ".carbin");
            if (!File.Exists(path) || entry == null)
            {
                return;
            }
            List<float[]> tips = new List<float[]>();
            foreach (string line in File.ReadAllLines(path))
            {
                string text = line.Split('#')[0].Trim();
                string[] parts = text.Split(new char[] { ' ', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries);
                float x, y, z;
                if (parts.Length >= 3 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
                {
                    tips.Add(new float[] { x, y, z });
                }
            }
            CarbinModel model = new CarbinModel(car.Read(entry));
            if (!model.HasExhaustLocators || tips.Count == 0)
            {
                return;
            }
            int old = model.ExhaustCount;
            model.SetExhaustTips(tips);
            car.Replace(entry, model.ToBytes());
            this.Line("  {0}: exhaust locators {1} -> {2} from exhaust.txt (backfire points)", entry.Name, old, tips.Count);
        }

        // An FM2 stage has no driver data: the Driver element of carattribs is the donor's, whose offsets belong to the
        // donor's seat and wheel. The offsets of the hands, shifter and hand brake are relative to the rig points
        // (computed from this car's wheel by CockpitRig) and are 0 on FM4's cars (Supra: all 0 but the butt), so they
        // become 0, and so does the butt (no rule from the seat is known: docs\DRIVER_POSITION.md, open).
        private void NeutralDriverOffsets(ForzaZip car)
        {
            ForzaZip.Entry attributes = car.Find("carattribs.xml");
            if (attributes == null)
            {
                return;
            }
            string text = Encoding.UTF8.GetString(car.Read(attributes));
            Match driver = Regex.Match(text, "<Driver [^>]*/>");
            if (!driver.Success)
            {
                return;
            }
            string result = Regex.Replace(driver.Value, "((?:ButtOffset|ButtOffsetLOD0|LeftHandOffset|RightHandOffset|LeftHandOffsetLOD0|RightHandOffsetLOD0|ShifterOffset|HandBrakeOffset)\\.[xyz]=\")[^\"]*\"", "${1}0.000000\"");
            if (result != driver.Value)
            {
                car.Replace(attributes, Encoding.UTF8.GetBytes(text.Substring(0, driver.Index) + result + text.Substring(driver.Index + driver.Length)));
                this.Line("  carattribs Driver: butt, hand, shifter and hand brake offsets 0 (computed from the wheel, not the donor's)");
            }
        }

        private void MirrorDriverOffsets(ForzaZip car, ForzaZip donor, string prefix, string donorPrefix)
        {
            ForzaZip.Entry attributes = car.Find("carattribs.xml");
            ForzaZip.Entry cockpit = car.Find(prefix + "_cockpit.carbin");
            ForzaZip.Entry donorCockpit = donor.Find(donorPrefix + "_cockpit.carbin");
            if (attributes == null || cockpit == null || donorCockpit == null)
            {
                return;
            }
            CarbinModel.Section wheel = new CarbinModel(car.Read(cockpit)).Find("steering_wheel");
            CarbinModel.Section donorWheel = new CarbinModel(donor.Read(donorCockpit)).Find("steering_wheel");
            if (wheel == null || donorWheel == null || Math.Sign(wheel.Bounds[0]) == Math.Sign(donorWheel.Bounds[0]))
            {
                return;
            }
            string text = Encoding.UTF8.GetString(car.Read(attributes));
            Match driver = Regex.Match(text, "<Driver [^>]*/>");
            if (!driver.Success)
            {
                return;
            }
            int flipped = 0;
            string result = Regex.Replace(driver.Value, "((?:ButtOffset|ButtOffsetLOD0|LeftHandOffset|RightHandOffset|LeftHandOffsetLOD0|RightHandOffsetLOD0|ShifterOffset|HandBrakeOffset|IKGuideTranslation)\\.x=\")([^\"]*)\"", delegate(Match m)
            {
                float value;
                if (!float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value == 0)
                {
                    return m.Value;
                }
                flipped++;
                return m.Groups[1].Value + (-value).ToString("0.######", CultureInfo.InvariantCulture) + "\"";
            });
            if (flipped > 0)
            {
                car.Replace(attributes, Encoding.UTF8.GetBytes(text.Substring(0, driver.Index) + result + text.Substring(driver.Index + driver.Length)));
                this.Line("  carattribs Driver: {0} x offsets mirrored (donor's wheel on the {1}, this car's on the {2})", flipped, donorWheel.Bounds[0] < 0 ? "left" : "right", wheel.Bounds[0] < 0 ? "left" : "right");
            }
        }

        private void DriverHandsFromSteeringWheel(ForzaZip car, string prefix)
        {
            ForzaZip.Entry attributes = car.Find("carattribs.xml");
            ForzaZip.Entry cockpit = car.Find(prefix + "_cockpit.carbin");
            if (attributes == null || cockpit == null)
            {
                return;
            }
            CarbinModel model = new CarbinModel(car.Read(cockpit));
            CarbinModel.Section wheel = model.Find("steering_wheel");
            if (wheel == null)
            {
                return;
            }
            Microsoft.Xna.Framework.Vector3 pivot = (model.Head.Length >= RigEnd) ? RigVector(model.Head, RigPivot) : Microsoft.Xna.Framework.Vector3.Zero;
            string text = Encoding.UTF8.GetString(car.Read(attributes));
            // The attributes come in any order (FM4 writes w, z, y, x).
            Match match = Regex.Match(text, "<DriverHandsOffset\\b[^>]*/>");
            float x = 0, y = 0, z = 0;
            if (!match.Success)
            {
                return;
            }
            Match mx = Regex.Match(match.Value, "value\\.x=\"([^\"]*)\""), my = Regex.Match(match.Value, "value\\.y=\"([^\"]*)\""), mz = Regex.Match(match.Value, "value\\.z=\"([^\"]*)\"");
            // Other sources keep the rule as it was (only the x, y, z attribute order of the FH files matched).
            if (!this.fm2Stage && !Regex.IsMatch(match.Value, "^<DriverHandsOffset value\\.x=\"[^\"]*\" value\\.y=\"[^\"]*\" value\\.z=\"[^\"]*\""))
            {
                return;
            }
            if (!mx.Success || !my.Success || !mz.Success
                || !float.TryParse(mx.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                || !float.TryParse(my.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out y)
                || !float.TryParse(mz.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out z))
            {
                return;
            }
            Microsoft.Xna.Framework.Vector3 hands = (pivot != Microsoft.Xna.Framework.Vector3.Zero) ? pivot + HandsFromPivot : new Microsoft.Xna.Framework.Vector3(wheel.Bounds[0], wheel.Bounds[1], wheel.Bounds[2]) + HandsFromWheel;
            bool off = Math.Abs(x - hands.X) > HandsOffWheel;
            if (this.traffic || this.fm2Stage)   // an FM2 stage's offset is the donor's, whatever its x
            {
                off |= Math.Abs(y - hands.Y) > HandsOffWheel || Math.Abs(z - hands.Z) > HandsOffWheel;
            }
            if (!off)
            {
                return;
            }
            string values = match.Value;
            values = Regex.Replace(values, "value\\.x=\"[^\"]*\"", string.Format(CultureInfo.InvariantCulture, "value.x=\"{0:F6}\"", hands.X));
            values = Regex.Replace(values, "value\\.y=\"[^\"]*\"", string.Format(CultureInfo.InvariantCulture, "value.y=\"{0:F6}\"", hands.Y));
            values = Regex.Replace(values, "value\\.z=\"[^\"]*\"", string.Format(CultureInfo.InvariantCulture, "value.z=\"{0:F6}\"", hands.Z));
            car.Replace(attributes, Encoding.UTF8.GetBytes(text.Substring(0, match.Index) + values + text.Substring(match.Index + match.Length)));
            this.Line("  carattribs DriverHandsOffset {0}, {1}, {2} is not at the steering wheel ({3:F6}, {7:F6}, {8:F6}): now {4:F6}, {5:F6}, {6:F6} ({9} + FM4's median offset)", mx.Groups[1].Value, my.Groups[1].Value, mz.Groups[1].Value, wheel.Bounds[0], hands.X, hands.Y, hands.Z, wheel.Bounds[1], wheel.Bounds[2], (pivot != Microsoft.Xna.Framework.Vector3.Zero) ? "rig pivot" : "wheel origin");
        }

        // FH traffic models carry a low-poly driver merged into the driver's seat: seatL has 594-926 LOD 1
        // indices against seatR's 70-161 (GMC_YukonXL_01, TOY_Camry_07, AUD_A4Avant_04, FOR_Explorer_10).
        // The seat with more than three times its twin's indices is that one; null when there is none.
        private static string DriverSeat(CarbinModel main)
        {
            int left = 0;
            int right = 0;
            foreach (CarbinModel.Section section in main.Sections)
            {
                foreach (CarbinModel.SubSection sub in section.SubSections)
                {
                    if (sub.Lod != 1)
                    {
                        continue;
                    }
                    if (section.Name == "seatL")
                    {
                        left += sub.Indices.Count;
                    }
                    else if (section.Name == "seatR")
                    {
                        right += sub.Indices.Count;
                    }
                }
            }
            if (left > 0 && right > 0 && left > right * 3)
            {
                return "seatL";
            }
            if (left > 0 && right > 0 && right > left * 3)
            {
                return "seatR";
            }
            return null;
        }

        // In LOD 0 every FM4 wheel (424 of 424) has outer_rim, the rim barrel the tire sits on: a ring at the
        // rim radius spanning the tire width. FM4 draws its own tire (Media\wheels\tirea/TireB/TireC, sized
        // from Data_Car) in LOD 0, and an FH traffic rim rebuilt from LOD 1 has no outer_rim and got none
        // (bare rims, GMC_YukonXL_01, checked by the user 2026-10-17); the LOD 1 tire copied instead drew
        // solid black as "tire" and wrong as "inner_rim". The copied tire becomes outer_rim with zero-area
        // triangles (strip a a a b b b ...) over its inner ring vertices: nothing visible, the ring's extent
        // kept. Whether outer_rim is what the game looks for is a guess being tested.
        private static bool Lod0TireToOuterRim(CarbinModel.Section section)
        {
            bool changed = false;
            double[] positions = CarbinModel.Positions(section, section.Lod0);
            foreach (CarbinModel.SubSection sub in section.SubSections)
            {
                if (sub.Lod != 0 || sub.Name != "tire")
                {
                    continue;
                }
                double inner = double.MaxValue;
                List<int> used = new List<int>();
                foreach (int index in sub.Indices)
                {
                    if (index >= 0 && index < section.Lod0.Count && !used.Contains(index))
                    {
                        used.Add(index);
                        inner = Math.Min(inner, Radius(positions, index));
                    }
                }
                List<int> ring = new List<int>();
                foreach (int index in used)
                {
                    if (Radius(positions, index) <= inner + 0.01)
                    {
                        ring.Add(index);
                        ring.Add(index);
                        ring.Add(index);
                    }
                }
                sub.Name = "outer_rim";
                sub.Indices = ring;
                changed = true;
            }
            return changed;
        }

        // A wheel carbin (type 3) holds 12 big-endian floats at 0x18 that fit FM4's separate tire to the
        // rim, all extents of the geometry along the axis (x) or from it (r), on 6 FM4 wheels checked
        // (2026-10-17, exact to the mm):
        //   0, 4  LOD 0 wheel_black x min   1  LOD 1 inner_rim x min   2  LOD 0 inner_rim x max
        //   3     LOD 1 inner_rim x max     5  LOD 1 tire x min        6  LOD 0 wheel_black x max
        //   7     LOD 1 tire x max          8  LOD 0 inner_rim r max   9  LOD 1 inner_rim r max
        //   10    LOD 0 wheel_black r max   11 LOD 1 tire r max
        // FH traffic wheels, whose LOD 0 is a placeholder, have +-3.4e38 (an empty min/max) in 0, 2, 4, 6
        // and 10 and junk in 8: FM4 then draws no tire (GMC_YukonXL_01, bare rims in game 2026-10-17).
        // When any value is that, all 12 are rebuilt from LOD 1: wheel_black (the bead seat ring, 2-3 mm
        // wide) is taken from the tire's outer edge, the LOD 0 rim as the LOD 1 one. Returns the values
        // written, or null.
        private const int TireHeaderOffset = 0x18;
        private const float BeadSeatWidth = 0.003f;

        private static string TireHeader(CarbinModel wheel)
        {
            if (wheel.Type != 3 || wheel.Head.Length < TireHeaderOffset + 48)
            {
                return null;
            }
            bool empty = false;
            for (int i = 0; i < 12; i++)
            {
                empty |= Math.Abs(ReadHeadFloat(wheel.Head, TireHeaderOffset + (i * 4))) > 1e30f;
            }
            float[] tire = LodExtents(wheel, "tire");
            float[] rim = LodExtents(wheel, "inner_rim");
            if (!empty || tire == null || rim == null)
            {
                return null;
            }
            float[] values =
            {
                tire[0], rim[0], rim[1], rim[1],
                tire[0], tire[0], tire[0] + BeadSeatWidth, tire[1],
                rim[2], rim[2], rim[2], tire[2],
            };
            for (int i = 0; i < 12; i++)
            {
                byte[] bytes = BitConverter.GetBytes(values[i]);
                for (int k = 0; k < 4; k++)
                {
                    wheel.Head[TireHeaderOffset + (i * 4) + k] = bytes[3 - k];
                }
            }
            return string.Join(" ", Array.ConvertAll(values, v => v.ToString("F4", CultureInfo.InvariantCulture)));
        }

        // x min, x max and r max of a material's LOD 1 triangles; null when there are none.
        private static float[] LodExtents(CarbinModel wheel, string material)
        {
            float[] extents = null;
            foreach (CarbinModel.Section section in wheel.Sections)
            {
                double[] positions = null;
                foreach (CarbinModel.SubSection sub in section.SubSections)
                {
                    if (sub.Lod != 1 || sub.Name != material)
                    {
                        continue;
                    }
                    positions = positions ?? CarbinModel.Positions(section, section.Lod);
                    foreach (int index in sub.Indices)
                    {
                        if (index < 0 || index >= section.Lod.Count)
                        {
                            continue;
                        }
                        float x = (float) positions[index * 3];
                        float r = (float) Radius(positions, index);
                        if (extents == null)
                        {
                            extents = new float[] { x, x, r };
                        }
                        extents[0] = Math.Min(extents[0], x);
                        extents[1] = Math.Max(extents[1], x);
                        extents[2] = Math.Max(extents[2], r);
                    }
                }
            }
            return extents;
        }

        private static float ReadHeadFloat(byte[] data, int offset)
        {
            byte[] value = { data[offset + 3], data[offset + 2], data[offset + 1], data[offset] };
            return BitConverter.ToSingle(value, 0);
        }

        // Distance from the wheel axis (X).
        private static double Radius(double[] positions, int index)
        {
            double y = positions[(index * 3) + 1];
            double z = positions[(index * 3) + 2];
            return Math.Sqrt((y * y) + (z * z));
        }

        // FH traffic cars' wheels have a LOD 0 of one triangle (18 FH1 wheels; FM4: none of 442), drawn up
        // close as a small white triangle with no wheel. As for the car's close-up models
        // (RebuildCloseUpModels), LOD 0 is rebuilt from LOD 1 and wheel_lod0.xds becomes a copy of
        // wheel.xds, the texture the LOD 1 UVs map; the tire becomes outer_rim (Lod0TireToOuterRim).
        private void RebuildWheelLod0(ForzaZip wheel, string media)
        {
            foreach (ForzaZip.Entry entry in new List<ForzaZip.Entry>(wheel.Entries))
            {
                if (!entry.Name.ToLowerInvariant().EndsWith(".carbin"))
                {
                    continue;
                }
                CarbinModel model = new CarbinModel(wheel.Read(entry));
                int lod = 0;
                int lod0 = 0;
                foreach (CarbinModel.Section section in model.Sections)
                {
                    lod += section.Lod.Count;
                    lod0 += section.Lod0.Count;
                }
                // An FM2 stage wheel (fm2stage) has FM2's LOD 1-5 on an FM4 wheel's LOD 0: always rebuilt.
                if ((!this.fm2Stage && lod0 >= lod * 0.02) || !model.Lod0FromLod1(delegate(string material) { return true; }))
                {
                    continue;
                }
                int built = 0;
                bool outerRim = false;
                foreach (CarbinModel.Section section in model.Sections)
                {
                    built += section.Lod0.Count;
                    outerRim |= Lod0TireToOuterRim(section);
                }
                string tire = TireHeader(model);
                if (this.fm2Stage)
                {
                    // The rim must resize with the car's rim size: 15" reference LOD 0 + morph table.
                    double target = model.MakeWheelMorph(0.194);
                    for (int f = 8; f <= 10; f += 2)
                    {
                        byte[] bytes = BitConverter.GetBytes((float) target);
                        for (int k = 0; k < 4; k++)
                        {
                            model.Head[TireHeaderOffset + (f * 4) + k] = bytes[3 - k];
                        }
                    }
                    this.Line("    wheel {0} {1}: LOD 0 = 15\" reference (radius 0.194) with morph table, header radius {2:F4}", media, entry.Name, target);
                }
                wheel.Replace(entry, model.ToBytes());
                this.Line("    wheel {0} {1}: LOD 0 {2}, rebuilt from LOD 1: {3} vertices{4}", media, entry.Name, (lod0 > 0) ? "a placeholder of " + lod0 + " vertices" : "missing (LOD 1-4 only; draw lists now LOD 0-5 as in FM4)", built, outerRim ? ", tire -> outer_rim (FM4 tire)" : "");
                if (tire != null)
                {
                    this.Line("    wheel {0} {1}: tire fit in the header rebuilt from LOD 1: {2}", media, entry.Name, tire);
                }
                ForzaZip.Entry plain = wheel.Find("wheel.xds");
                ForzaZip.Entry close = wheel.Find("wheel_lod0.xds");
                if (plain != null)
                {
                    if (close != null)
                    {
                        wheel.Replace(close, wheel.Read(plain));
                    }
                    else
                    {
                        wheel.Add("wheel_lod0.xds", wheel.Read(plain));
                    }
                    this.Line("    wheel {0} wheel_lod0.xds: a copy of wheel.xds", media);
                }
            }
        }

        #endregion

        #region Sound and thumbnail

        // FH2's engine tuning (<engine media>_ET.xml) and car model tuning (<car>_CMT.xml) have the FM4
        // layout and point at harmonic tunings by name. A harmonic tuning FM4 lacks comes from FH2
        // with the sound bank it names (<Soundbank name="2N1_Ducati998_EngAmb.fsb">, a loose FSB4 file
        // in Engines\soundbanks\lod1): the 353 banks both games have are byte-identical and the
        // tunings they share too. Only when FH2 lacks one of them is the donor's engine tuning used.
        private void PortSound()
        {
            string sourceAudio = Path.Combine(Path.Combine(Path.Combine(this.SourceRoot, "Media"), "audio"), "cars");
            string targetAudio = Path.Combine(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "audio"), "Cars");
            ForzaZip sourceTuning = new ForzaZip(Path.Combine(Path.Combine(sourceAudio, "Engines"), "EngineTuning.zip"));
            ForzaZip sourceHarmonic = new ForzaZip(Path.Combine(Path.Combine(sourceAudio, "Engines"), "HarmonicTuning.zip"));
            string sourceBanks = Path.Combine(Path.Combine(Path.Combine(sourceAudio, "Engines"), "Soundbanks"), "LOD1");
            string targetBanks = Path.Combine(Path.Combine(Path.Combine(targetAudio, "Engines"), "soundbanks"), "lod1");
            string harmonicPath = Path.Combine(Path.Combine(targetAudio, "Engines"), "harmonictuning.zip");
            ForzaZip harmonic = new ForzaZip(this.In(harmonicPath));
            List<string> imported = new List<string>();
            List<string> checkedHarmonics = new List<string>();   // in FM4 with all their sound banks
            ForzaZip engineTuning = this.OpenChanged(Path.Combine(Path.Combine(targetAudio, "Engines"), "enginetuning.zip"));
            string donorEngine = this.DonorScalar("SELECT e.MediaName FROM Data_Engine e JOIN List_UpgradeEngine u ON u.EngineID = e.EngineID WHERE u.Ordinal = ? AND u.Level = 0", this.donorId) as string;
            string chosenEngine = (this.SoundFrom != null) ? this.target.Scalar("SELECT e.MediaName FROM Data_Engine e JOIN List_UpgradeEngine u ON u.EngineID = e.EngineID JOIN Data_Car c ON c.Id = u.Ordinal WHERE c.MediaName = ? COLLATE NOCASE AND u.Level = 0", this.SoundFrom) as string : null;
            ForzaZip.Entry chosenTuning = (chosenEngine != null) ? engineTuning.Find(chosenEngine + "_ET.xml") : null;
            if (this.SoundFrom != null && chosenTuning == null)
            {
                this.Line("WARNING engine sound: {0} has no FM4 engine tuning; the source's sound is kept", this.SoundFrom);
            }
            foreach (string media in this.engineMedia)
            {
                if (chosenTuning != null)
                {
                    // The user's choice: that FM4 car's whole sound setup (its banks are FM4's).
                    this.Put(engineTuning, media + "_ET.xml", engineTuning.Read(chosenTuning));
                    this.Line("engine sound {0}: {1}'s FM4 setup {2}_ET.xml, the user's choice", media, this.SoundFrom, chosenEngine);
                    continue;
                }
                ForzaZip.Entry entry = sourceTuning.Find(media + "_ET.xml");
                byte[] data = (entry != null) ? sourceTuning.Read(entry) : null;
                List<string> missing = new List<string>();
                if (data != null)
                {
                    foreach (Match reference in Regex.Matches(Encoding.UTF8.GetString(data), "\"([^\"]+_HT\\.xml)\"", RegexOptions.IgnoreCase))
                    {
                        string name = reference.Groups[1].Value;
                        if (imported.Contains(name) || missing.Contains(name) || checkedHarmonics.Contains(name))
                        {
                            continue;
                        }
                        int result = this.ImportHarmonic(harmonic, sourceHarmonic, harmonicPath, name, sourceBanks, targetBanks);
                        if (result > 0)
                        {
                            imported.Add(name);
                        }
                        else if (result < 0)
                        {
                            missing.Add(name);
                        }
                        else
                        {
                            checkedHarmonics.Add(name);
                        }
                    }
                }
                if (data == null || missing.Count > 0)
                {
                    // The source's sound is incomplete: a whole FM4 engine sound setup is used instead,
                    // the one the user picked in the review (Fixes "soundfull:<engine>"), else the
                    // donor's, and the review asks which car's to use (decided 2026-10-17).
                    string lacks = (missing.Count > 0) ? string.Join(", ", missing.ToArray()) : "its tuning";
                    string fullKey = "soundfull:" + media;
                    object fix;
                    bool picked = this.Fixes.TryGetValue(fullKey, out fix) && fix is string && engineTuning.Find((string) fix + "_ET.xml") != null;
                    string setup = picked ? (string) fix : donorEngine;
                    byte[] fallbackData = (setup != null) ? this.ReadTuning(engineTuning, setup) : null;
                    if (fallbackData == null)
                    {
                        this.Line("engine sound {0}: no tuning", media);
                        continue;
                    }
                    data = fallbackData;
                    this.donorContent.Add(string.Format("engine sound {0}: {1} tuning {2}_ET.xml ({3} lacks {4})", media, picked ? "the picked" : "the donor's", setup, this.game, lacks));
                    this.Line("engine sound {0}: {1} ({2}; " + this.game + " lacks {3})", media, picked ? "the picked FM4 setup" : "the donor's", setup, lacks);
                    // FM4's tunings, but their banks may be missing from this FM4 too: they come from the
                    // source game like the car's own. One still missing leaves a level silent.
                    List<string> silent = new List<string>();
                    foreach (Match reference in Regex.Matches(Encoding.UTF8.GetString(data), "\"([^\"]+_HT\\.xml)\"", RegexOptions.IgnoreCase))
                    {
                        string name = reference.Groups[1].Value;
                        if (!checkedHarmonics.Contains(name) && !imported.Contains(name))
                        {
                            checkedHarmonics.Add(name);
                            if (this.ImportHarmonicAny(harmonic, sourceHarmonic, harmonicPath, name, sourceBanks, targetBanks) < 0)
                            {
                                silent.Add(name);
                                this.Line("  WARNING harmonic tuning {0} of {1}: a sound bank is in neither game", name, setup);
                            }
                        }
                    }
                    if (!picked || silent.Count > 0)
                    {
                        Issue issue = new Issue();
                        issue.Key = fullKey;
                        issue.Kind = "sound";
                        issue.Where = media + "_ET.xml";
                        issue.Value = setup;
                        issue.Problem = string.Format("{0} has no {1}: the engine sound of another car ({2}) is used{3}", this.game, lacks, setup, (silent.Count > 0) ? " (but it misses " + string.Join(", ", silent.ToArray()) + ": silent at some levels)" : "");
                        issue.Choices.AddRange(this.EngineTuningChoices(engineTuning));
                        issue.Suggested = null;   // no suggestion: the user picks the car, else it stays as it is
                        this.Issues.Add(issue);
                        this.Line("  REVIEW {0}", issue.Problem);
                    }
                }
                else
                {
                    this.Line("engine sound {0}: " + this.game + " tuning, its harmonic tunings are all in FM4{1}", media, (imported.Count > 0) ? " (" + imported.Count + " brought from " + this.game + ")" : "");
                    data = this.CompleteEngineTuning(media, data, engineTuning, donorEngine, harmonic, sourceHarmonic);
                    data = this.DscBlowOff(media, data);
                }
                this.Put(engineTuning, media + "_ET.xml", data);
            }

            ForzaZip sourceModels = new ForzaZip(Path.Combine(sourceAudio, "CarModelTuning.zip"));
            ForzaZip carModels = this.OpenChanged(Path.Combine(targetAudio, "CarModelTuning.zip"));
            ForzaZip.Entry modelTuning = sourceModels.Find(this.MediaName + "_CMT.xml");
            if (modelTuning != null)
            {
                byte[] tuning = sourceModels.Read(modelTuning);
                if (this.RenameTo != null)
                {
                    Encoding latin = Encoding.GetEncoding(28591);
                    tuning = latin.GetBytes(Regex.Replace(latin.GetString(tuning), Regex.Escape(this.MediaName), this.RenameTo, RegexOptions.IgnoreCase));
                }
                this.Put(carModels, this.CarName + "_CMT.xml", tuning);
                this.Line("car model tuning {0}_CMT.xml added", this.CarName);
            }
            else if (!string.IsNullOrEmpty(this.DonorMediaName))
            {
                // The source has none (an FM3 DLC car whose audio is in the base game): without <car>_CMT.xml the car has no sound
                // model at all (checked in game 2026-10-30: the R8 Community was silent). The donor's one stands in, the car's name in it.
                ForzaZip.Entry donorModel = carModels.Find(this.DonorMediaName + "_CMT.xml");
                if (donorModel != null)
                {
                    Encoding latin = Encoding.GetEncoding(28591);
                    this.Put(carModels, this.CarName + "_CMT.xml", latin.GetBytes(Regex.Replace(latin.GetString(carModels.Read(donorModel)), Regex.Escape(this.DonorMediaName), this.CarName, RegexOptions.IgnoreCase)));
                    this.donorContent.Add(string.Format("car model tuning {0}_CMT.xml: the donor's {1}_CMT.xml ({2} has none)", this.CarName, this.DonorMediaName, this.game));
                    this.Line("car model tuning {0}_CMT.xml: the donor's ({1} has none)", this.CarName, this.game);
                }
                else
                {
                    this.Line("  WARNING car model tuning: {0} has none and the donor's {1}_CMT.xml is not in the target: the car may be silent", this.game, this.DonorMediaName);
                }
            }
        }

        // A supercharged (DSC, Data_Engine.AspirationID_Stock 5) engine with the source's blow-off slots played a
        // blow-off on throttle lift and no supercharger whine in FM4 (Bowler Nemesis EXR from FH2, slots L1 1/2,
        // L2 3/5, L3 3/5); with the Ford GT's slots (L1 1/1, L2 2/1, L3 3/1) the whine is back and the blow-off
        // gone (user, in game 2026-10-27). Only the DSC engines get them; everything else stays the source's.
        private byte[] DscBlowOff(string media, byte[] data)
        {
            object aspiration = this.target.Scalar("SELECT AspirationID_Stock FROM Data_Engine WHERE MediaName = ? COLLATE NOCASE", media);
            if (aspiration == null || aspiration is DBNull || Convert.ToInt64(aspiration, CultureInfo.InvariantCulture) != 5)
            {
                return data;
            }
            Encoding latin = Encoding.GetEncoding(28591);
            string text = latin.GetString(data);
            string result = text;
            StringBuilder was = new StringBuilder();
            for (int slot = 1; slot <= 3; slot++)
            {
                Match level = Regex.Match(result, "blowoffL" + slot + "Level=\"([^\"]*)\"");
                Match choice = Regex.Match(result, "blowoffL" + slot + "Choice=\"([^\"]*)\"");
                if (!level.Success || !choice.Success)
                {
                    return data;
                }
                was.Append((slot > 1 ? ", " : "") + "L" + slot + " " + level.Groups[1].Value + "/" + choice.Groups[1].Value);
                result = Regex.Replace(result, "blowoffL" + slot + "Level=\"[^\"]*\"", "blowoffL" + slot + "Level=\"" + slot + "\"");
                result = Regex.Replace(result, "blowoffL" + slot + "Choice=\"[^\"]*\"", "blowoffL" + slot + "Choice=\"1\"");
            }
            if (result == text)
            {
                return data;
            }
            this.Line("  engine sound {0}: supercharged (DSC), blow-off slots {1} -> L1 1/1, L2 2/1, L3 3/1 (the Ford GT's: with the source's the game plays a blow-off and no supercharger whine)", media, was.ToString());
            return latin.GetBytes(result);
        }

        // FM3's engine tuning is an older, shorter layout (all 490 FM3 files against FM4's 501): it lacks
        // the DSP blocks every FM4 tuning has (HarmonicTunings EmissionGroup0-2, FocusPEQ, Distortion,
        // Compressor, ShiftVolumeScalar, HeavyBraking, TrashDSP, ExhaustReflection, DSPScalar) and the
        // EngineSettings attributes num_cylinders, blowoffL1-3Level/Choice and opencockpit. FM4 plays it
        // at stock but the engine goes silent fully upgraded (BMW_X5_09 from FM3, 2026-10-17; sound back
        // in game with BMW_X5M_11's blocks added). The source values stay as they are; only what is
        // missing comes from an FM4 tuning: the user's pick in the review (Fixes "sound:<engine>"), else
        // the FM4 tuning playing the same sound banks (SameBanksTuning), else the donor's engine.
        // num_cylinders is the engine's own List_Cylinders.Number.
        private byte[] CompleteEngineTuning(string media, byte[] data, ForzaZip engineTuning, string donorEngine, ForzaZip harmonic, ForzaZip sourceHarmonic)
        {
            string text = Encoding.UTF8.GetString(data);
            if (text.IndexOf("<DSPScalar", StringComparison.Ordinal) >= 0)
            {
                return data;
            }
            string key = "sound:" + media;
            object fix;
            bool picked = this.Fixes.TryGetValue(key, out fix) && fix is string && engineTuning.Find((string) fix + "_ET.xml") != null;
            string shared = null;
            string sameBanks = picked ? null : this.SameBanksTuning(media, text, engineTuning, harmonic, sourceHarmonic, out shared);
            string layoutName = picked ? (string) fix : (sameBanks ?? donorEngine);
            if (sameBanks != null)
            {
                this.Line("  engine sound {0}: DSP blocks from {1}, same sound banks ({2})", media, sameBanks, shared);
            }
            byte[] layoutData = (layoutName != null) ? this.ReadTuning(engineTuning, layoutName) : null;
            string layout = (layoutData != null) ? Encoding.UTF8.GetString(layoutData) : null;
            if (layout == null || layout.IndexOf("<DSPScalar", StringComparison.Ordinal) < 0)
            {
                this.Line("  WARNING engine sound {0}: {1} tuning lacks FM4's DSP blocks and {2}_ET.xml is no complete FM4 tuning; left as is (silent when upgraded)", media, this.game, layoutName);
                return data;
            }

            object cylinders = this.target.Scalar("SELECT c.Number FROM Data_Engine e JOIN List_Cylinders c ON c.CylinderID = e.CylinderID WHERE e.MediaName = ? COLLATE NOCASE", media);
            text = AddMissingAttributes(text, layout, "EngineSettings", (cylinders != null) ? Convert.ToString(cylinders, CultureInfo.InvariantCulture) : null);
            text = AddMissingAttributes(text, layout, "VolumeAdjustments", null);

            // FM4's top-level blocks (children of <Engine>, two-space indent) the source lacks, in FM4's order.
            int open = layout.IndexOf("<Engine>", StringComparison.Ordinal);
            int close = layout.LastIndexOf("</Engine>", StringComparison.Ordinal);
            int end = text.LastIndexOf("</Engine>", StringComparison.Ordinal);
            if (open < 0 || close < open || end < 0)
            {
                this.Line("  WARNING engine sound {0}: no <Engine> element, tuning left as is", media);
                return data;
            }
            string body = layout.Substring(open + 8, close - open - 8);
            MatchCollection blocks = Regex.Matches(body, "\n  <(\\w+)\\b");
            List<string> present = new List<string>();
            foreach (Match m in Regex.Matches(text, "\n  <(\\w+)\\b"))
            {
                present.Add(m.Groups[1].Value);
            }
            StringBuilder added = new StringBuilder();
            List<string> names = new List<string>();
            for (int i = 0; i < blocks.Count; i++)
            {
                string name = blocks[i].Groups[1].Value;
                if (present.Contains(name))
                {
                    continue;
                }
                int stop = (i + 1 < blocks.Count) ? blocks[i + 1].Index : body.Length;
                added.Append(body.Substring(blocks[i].Index, stop - blocks[i].Index).TrimEnd('\r', '\n'));
                names.Add(name);
            }
            string newline = text.Contains("\r\n") ? "\r\n" : "\n";
            string result = text.Substring(0, end).TrimEnd('\r', '\n') + added.ToString().Replace("\r\n", "\n").Replace("\n", newline) + newline + text.Substring(end);
            try
            {
                new XmlDocument().LoadXml(result.TrimStart('\uFEFF'));
            }
            catch (XmlException ex)
            {
                this.Line("  WARNING engine sound {0}: completed tuning is not valid XML ({1}), left as is", media, ex.Message);
                return data;
            }
            this.Line("  engine sound {0}: {1} tuning completed to the FM4 layout from {2}_ET.xml ({3}; num_cylinders {4})", media, this.game, layoutName, string.Join(", ", names.ToArray()), cylinders ?? "from the layout");
            this.donorFormat.Add(string.Format("engine sound {0}: FM4 DSP blocks from {1}_ET.xml ({2} values kept)", media, layoutName, this.game));

            if (!picked)
            {
                Issue issue = new Issue();
                issue.Key = key;
                issue.Kind = "sound";
                issue.Where = media + "_ET.xml";
                issue.Value = layoutName;
                issue.Problem = string.Format("{0} engine tuning has no FM4 DSP blocks (silent when upgraded): those of another car ({1}) are used", this.game, layoutName);
                issue.Choices.AddRange(this.EngineTuningChoices(engineTuning));
                issue.Suggested = null;   // no suggestion: the user picks the car, else it stays as it is
                this.Issues.Add(issue);
                this.Line("  REVIEW {0}", issue.Problem);
            }
            return Encoding.UTF8.GetBytes(result);
        }

        // The FM4 engine tunings a review "sound" issue offers: complete FM4 ones only (with DSPScalar),
        // so an earlier port's uncompleted FM3 tuning is not offered. Read once per run.
        private List<Choice> EngineTuningChoices(ForzaZip engineTuning)
        {
            if (this.tuningChoices == null)
            {
                this.tuningChoices = new List<Choice>();
                List<string> names = new List<string>();
                foreach (ForzaZip.Entry e in engineTuning.Entries)
                {
                    if (e.Name.EndsWith("_ET.xml", StringComparison.OrdinalIgnoreCase)
                        && Encoding.UTF8.GetString(engineTuning.Read(e)).IndexOf("<DSPScalar", StringComparison.Ordinal) >= 0)
                    {
                        names.Add(e.Name.Substring(0, e.Name.Length - 7));
                    }
                }
                names.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string name in names)
                {
                    Choice c = new Choice();
                    c.Value = name;
                    c.Label = name;
                    this.tuningChoices.Add(c);
                }
            }
            return this.tuningChoices;
        }

        // The sound banks an engine tuning plays: the <Soundbank name> of the harmonic tunings its
        // EngineAmbient, EngineIntake and Exhaust upgrades name (lower case), and the exhaust's at L0.
        private class TuningBanks
        {
            public List<string> Banks = new List<string>();
            public string Exhaust;
        }

        private static TuningBanks ReadTuningBanks(string tuning, ForzaZip harmonic, ForzaZip sourceHarmonic)
        {
            TuningBanks result = new TuningBanks();
            foreach (string element in new string[] { "EngineAmbient", "EngineIntake", "Exhaust" })
            {
                Match block = Regex.Match(tuning, "<" + element + ">(.*?)</" + element + ">", RegexOptions.Singleline);
                if (!block.Success)
                {
                    continue;
                }
                foreach (Match reference in Regex.Matches(block.Groups[1].Value, "(\\w+)=\"([^\"]+_HT\\.xml)\"", RegexOptions.IgnoreCase))
                {
                    ForzaZip.Entry entry = harmonic.Find(reference.Groups[2].Value);
                    ForzaZip zip = (entry != null) ? harmonic : sourceHarmonic;
                    if (entry == null && sourceHarmonic != null)
                    {
                        entry = sourceHarmonic.Find(reference.Groups[2].Value);
                    }
                    Match bank = (entry != null) ? Regex.Match(Encoding.UTF8.GetString(zip.Read(entry)), "<Soundbank\\s+name=\"([^\"]+)\"", RegexOptions.IgnoreCase) : Match.Empty;
                    if (!bank.Success || string.Equals(bank.Groups[1].Value, "NA.fsb", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    string name = bank.Groups[1].Value.ToLowerInvariant();
                    if (!result.Banks.Contains(name))
                    {
                        result.Banks.Add(name);
                    }
                    if (element == "Exhaust" && reference.Groups[1].Value == "L0")
                    {
                        result.Exhaust = name;
                    }
                }
            }
            return result;
        }

        // The complete FM4 engine tuning whose DSP blocks suit a source tuning best: FM4's DSP values go
        // with the recordings (fm4dev\Fm3DLC\snd\sndpick.txt, mean DSP distance 0.076 between tunings with
        // the same exhaust bank, 0.132 with another shared bank, 0.151 with none). Ranked by the same
        // exhaust bank, then by the number of shared banks, then by name; null when none shares a bank.
        // The engines of this port are left out (an earlier port of the car is not FM4's).
        private string SameBanksTuning(string media, string tuning, ForzaZip engineTuning, ForzaZip harmonic, ForzaZip sourceHarmonic, out string shared)
        {
            shared = null;
            TuningBanks own = ReadTuningBanks(tuning, harmonic, sourceHarmonic);
            if (own.Banks.Count == 0)
            {
                return null;
            }
            if (this.tuningBanks == null)
            {
                this.tuningBanks = new Dictionary<string, TuningBanks>(StringComparer.OrdinalIgnoreCase);
                foreach (Choice choice in this.EngineTuningChoices(engineTuning))
                {
                    string name = (string) choice.Value;
                    this.tuningBanks[name] = ReadTuningBanks(Encoding.UTF8.GetString(engineTuning.Read(engineTuning.Find(name + "_ET.xml"))), harmonic, null);
                }
            }
            string best = null;
            int bestScore = 0;
            List<string> bestShared = null;
            foreach (KeyValuePair<string, TuningBanks> candidate in this.tuningBanks)
            {
                bool ours = false;
                foreach (string engine in this.engineMedia)
                {
                    ours |= string.Equals(engine, candidate.Key, StringComparison.OrdinalIgnoreCase);
                }
                List<string> common = new List<string>();
                foreach (string bank in candidate.Value.Banks)
                {
                    if (own.Banks.Contains(bank))
                    {
                        common.Add(bank);
                    }
                }
                if (ours || common.Count == 0)
                {
                    continue;
                }
                int score = common.Count + ((own.Exhaust != null && own.Exhaust == candidate.Value.Exhaust) ? 10 : 0);
                if (score > bestScore || (score == bestScore && string.Compare(candidate.Key, best, StringComparison.OrdinalIgnoreCase) < 0))
                {
                    best = candidate.Key;
                    bestScore = score;
                    bestShared = common;
                }
            }
            if (best != null)
            {
                shared = string.Join(", ", bestShared.ToArray());
            }
            return best;
        }

        // <element .../> of the source with the layout's attributes it lacks appended; num_cylinders
        // replaced by the given count when there is one.
        private static string AddMissingAttributes(string text, string layout, string element, string cylinders)
        {
            Regex pattern = new Regex("<" + element + "\\b([^>]*?)/>");
            Match own = pattern.Match(text);
            Match model = pattern.Match(layout);
            if (!own.Success || !model.Success)
            {
                return text;
            }
            List<string> have = new List<string>();
            foreach (Match a in Regex.Matches(own.Groups[1].Value, "(\\w+)=\""))
            {
                have.Add(a.Groups[1].Value);
            }
            StringBuilder extra = new StringBuilder();
            foreach (Match a in Regex.Matches(model.Groups[1].Value, "(\\w+)=\"([^\"]*)\""))
            {
                string name = a.Groups[1].Value;
                if (have.Contains(name))
                {
                    continue;
                }
                string value = (name == "num_cylinders" && cylinders != null) ? cylinders : a.Groups[2].Value;
                extra.Append(" " + name + "=\"" + value + "\"");
            }
            return text.Substring(0, own.Index) + "<" + element + own.Groups[1].Value.TrimEnd() + extra + "/>" + text.Substring(own.Index + own.Length);
        }

        // A harmonic tuning the engine tuning names, made complete in FM4: the tuning itself from the
        // source game when FM4's harmonictuning.zip lacks it, and EVERY sound bank it names
        // (<Soundbank name="12T1_Bently_Exh.fsb">, loose FSB4 files in Engines\soundbanks\lod1, written
        // lower-case like FM4's) that FM4 lacks, even when FM4 has the tuning: the extracted FM4 disc
        // holds 356 banks against FH1's 937, and its own tunings name banks it does not have
        // (MER_S65AMG_12's exhaust 12T1_Bently_Exh: "Disc Read Error" while loading the race).
        // 1 = something imported, 0 = all already in FM4, -1 = the source game lacks a piece.
        // The donor's engine tuning (<engine>_ET.xml): in the game's EngineTuning.zip, or for a donor of a DLC pack in the
        // pack's own. null when neither has it.
        private byte[] ReadTuning(ForzaZip engineTuning, string setup)
        {
            ForzaZip.Entry entry = engineTuning.Find(setup + "_ET.xml");
            if (entry != null)
            {
                return engineTuning.Read(entry);
            }
            string pack = this.DonorAudio("EngineTuning.zip");
            if (pack != null)
            {
                ForzaZip zip = new ForzaZip(pack);
                entry = zip.Find(setup + "_ET.xml");
                if (entry != null)
                {
                    return zip.Read(entry);
                }
            }
            return null;
        }

        // A file of the donor pack's engine audio (Media\audio\cars\Engines\<name>), or null.
        private string DonorAudio(string name)
        {
            if (this.DonorRoot == null)
            {
                return null;
            }
            string path = Path.Combine(Path.Combine(Path.Combine(Path.Combine(Path.Combine(this.DonorRoot, "Media"), "audio"), "cars"), "Engines"), name);
            return (File.Exists(path) || Directory.Exists(path)) ? path : null;
        }

        // ImportHarmonic from the source game, and when it has no such harmonic tuning or bank, from the donor's pack.
        private int ImportHarmonicAny(ForzaZip targetHarmonic, ForzaZip sourceHarmonic, string harmonicPath, string name, string sourceBanks, string targetBanks)
        {
            int result = this.ImportHarmonic(targetHarmonic, sourceHarmonic, harmonicPath, name, sourceBanks, targetBanks);
            string packHarmonic = this.DonorAudio("HarmonicTuning.zip");
            string packBanks = this.DonorAudio(Path.Combine("Soundbanks", "LOD1"));
            if (result < 0 && packHarmonic != null && packBanks != null)
            {
                result = this.ImportHarmonic(targetHarmonic, new ForzaZip(packHarmonic), harmonicPath, name, packBanks, targetBanks);
            }
            return result;
        }

        private int ImportHarmonic(ForzaZip targetHarmonic, ForzaZip sourceHarmonic, string harmonicPath, string name, string sourceBanks, string targetBanks)
        {
            ForzaZip.Entry own = targetHarmonic.Find(name);
            ForzaZip.Entry entry = (own != null) ? own : sourceHarmonic.Find(name);
            if (entry == null)
            {
                return -1;
            }
            byte[] data = ((own != null) ? targetHarmonic : sourceHarmonic).Read(entry);
            MatchCollection banks = Regex.Matches(Encoding.UTF8.GetString(data), "<Soundbank\\s+name=\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (banks.Count == 0 && own == null)
            {
                return -1;
            }
            Dictionary<string, string> copies = new Dictionary<string, string>();
            foreach (Match bank in banks)
            {
                // NA.fsb is the "no sound on this channel" placeholder of NA_EngAmb/Int/Exh_HT.xml: no game
                // has the file and FM4's own cars use it (2 FM4, 51 FH1 tunings), so it is never missing.
                if (string.Equals(bank.Groups[1].Value, "NA.fsb", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string target = Path.Combine(targetBanks, bank.Groups[1].Value.ToLowerInvariant());
                if (this.newFiles.ContainsKey(target) || copies.ContainsKey(target) || this.checkedBanks.Contains(target))
                {
                    continue;
                }
                string source = null;
                if (Directory.Exists(sourceBanks))
                {
                    foreach (string file in Directory.GetFiles(sourceBanks))
                    {
                        if (string.Equals(Path.GetFileName(file), bank.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                        {
                            source = file;
                        }
                    }
                }
                string existing = FindFile(targetBanks, bank.Groups[1].Value) ?? this.InTrial(Path.Combine(targetBanks, bank.Groups[1].Value));
                if (existing != null)
                {
                    // FM4 has a bank of that name: identical bytes are the usual case (all 353 banks FH2
                    // and FM4 share). A different one stays FM4's, since FM4 cars use it, and is reported.
                    this.checkedBanks.Add(target);
                    if (source != null && !SameFile(source, existing))
                    {
                        this.Line("  WARNING sound bank {0}: FM4's differs from the {1} one, FM4's kept", Path.GetFileName(existing), this.game);
                    }
                    continue;
                }
                if (source == null)
                {
                    // Not in the source and not loose in FM4, but an installed FM4 package may carry it (the second disc's
                    // Model Year Car Packs): then it is there, the game loads it from the package (TODO 3.4).
                    // It is copied out of the package into the car, so it does not depend on that package being installed
                    // (a file the game cannot find is an I/O error and a crash).
                    string pack = SoundBankIndex.PackageOf(ForzaGame.Motorsport4, bank.Groups[1].Value);
                    string extracted = (pack != null) ? SoundBankIndex.Extract(ForzaGame.Motorsport4, bank.Groups[1].Value) : null;
                    if (extracted == null)
                    {
                        return -1;
                    }
                    this.Line("  sound bank {0}: taken from the installed package \"{1}\"", bank.Groups[1].Value, pack);
                    copies[target] = extracted;
                    continue;
                }
                copies[target] = source;
            }
            foreach (KeyValuePair<string, string> copy in copies)
            {
                this.newFiles[copy.Key] = copy.Value;
                this.Line("  sound bank {0} from " + this.game + "{1}", Path.GetFileName(copy.Key), (own != null) ? " (FM4 has the tuning, not the bank)" : "");
            }
            if (own == null)
            {
                this.Put(this.OpenChanged(harmonicPath), name, data);
                this.Line("  harmonic tuning {0} from " + this.game, name);
            }
            return (own == null || copies.Count > 0) ? 1 : 0;
        }

        // The file of that name in a folder, any case; null if none.
        private static string FindFile(string folder, string name)
        {
            string path = Path.Combine(folder, name);
            return File.Exists(path) ? path : null;
        }

        private static bool SameFile(string a, string b)
        {
            if (new FileInfo(a).Length != new FileInfo(b).Length)
            {
                return false;
            }
            byte[] x = File.ReadAllBytes(a);
            byte[] y = File.ReadAllBytes(b);
            for (int i = 0; i < x.Length; i++)
            {
                if (x[i] != y[i])
                {
                    return false;
                }
            }
            return true;
        }

        private void PortThumbnail()
        {
            if (this.skipThumbnail)
            {
                this.Line("thumbnail: skipped, Media\\UI\\Textures.zip is in use (the emulator is running)");
                return;
            }
            string thumbnail = this.source.Scalar("SELECT Thumbnail FROM Data_Car WHERE Id = ?", this.carId) as string;
            string donorThumbnail = this.DonorScalar("SELECT Thumbnail FROM Data_Car WHERE Id = ?", this.donorId) as string;
            GamePaths.GameFile from = GamePaths.Locate(this.SourceRoot, thumbnail);
            GamePaths.GameFile like = GamePaths.Locate(this.TargetRoot, donorThumbnail);
            if ((like == null || !like.InZip) && this.DonorRoot != null)
            {
                // a DLC donor's thumbnail is a loose file of its pack: the new one goes in the game's thumbnail zip, beside a car the game has
                foreach (object[] row in this.target.Query("SELECT Thumbnail FROM Data_Car WHERE Thumbnail IS NOT NULL ORDER BY Id LIMIT 30").Rows)
                {
                    like = (row[0] is string) ? GamePaths.Locate(this.TargetRoot, (string) row[0]) : null;
                    if (like != null && like.InZip)
                    {
                        break;
                    }
                }
            }
            if (like == null || !like.InZip)
            {
                this.Line("thumbnail: not found, skipped");
                return;
            }
            ForzaZip textures = this.OpenChanged(like.ZipPath);
            // The file is named by the new car id (the source's Thumbnail_431.xds can be another car's here, or the id
            // of a DLC car), and the database row points at it.
            string fileName = "Thumbnail_" + this.carId.ToString(CultureInfo.InvariantCulture) + ".xds";
            string folder = (thumbnail != null && thumbnail.IndexOf('\\') >= 0) ? thumbnail.Substring(0, thumbnail.LastIndexOf('\\') + 1) : "game:\\Media\\UI\\Textures\\Thumbnails\\";
            this.target.Execute("UPDATE Data_Car SET Thumbnail = ? WHERE Id = ?", folder + fileName, this.carId);
            byte[] picture;
            if (from == null)
            {
                picture = Fm2Source.GenericThumbnail();
                this.Line("thumbnail: the source has none, a generic picture is put");
            }
            else
            {
                picture = GamePaths.Read(from);
            }
            string name = like.EntryName.Substring(0, like.EntryName.LastIndexOf('/') + 1) + fileName;
            this.Put(textures, name, picture);
            this.donorFormat.Add("thumbnail: put in the folder of the donor's (" + like.EntryName.Substring(0, like.EntryName.LastIndexOf('/') + 1) + ")");
            this.Line("thumbnail: {0} in {1}", name, this.Relative(like.ZipPath));
        }

        #endregion

        #region Files

        private ForzaZip OpenChanged(string path)
        {
            ForzaZip archive;
            if (!this.changedZips.TryGetValue(path, out archive))
            {
                archive = new ForzaZip(this.In(path));
                this.changedZips[path] = archive;
            }
            return archive;
        }

        private void Put(ForzaZip archive, string name, byte[] data)
        {
            ForzaZip.Entry entry = archive.Find(name);
            if (entry != null)
            {
                archive.Replace(entry, data);
            }
            else
            {
                archive.Add(name, data);
            }
        }

        private void SaveNew(ForzaZip archive, string path)
        {
            string output = this.Out(path);
            if (File.Exists(output))
            {
                this.Backup(output);
            }
            archive.Save(output);
            this.newZips.Add(path);
        }

        // Where a target file is read: its copy under OutputRoot when an earlier car of the same trial
        // folder (a DLC pack) wrote one, else itself.
        private string In(string path)
        {
            return this.InTrial(path) ?? path;
        }

        // The copy of a target file under OutputRoot, or null.
        private string InTrial(string path)
        {
            if (this.OutputRoot == null)
            {
                return null;
            }
            string copy = Path.Combine(this.OutputRoot, this.Relative(path));
            return File.Exists(copy) ? copy : null;
        }

        // Where a target file is written: itself, or its place under OutputRoot on a trial run.
        private string Out(string path)
        {
            if (this.OutputRoot == null)
            {
                return path;
            }
            string output = Path.Combine(this.OutputRoot, this.Relative(path));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            return output;
        }

        // Before anything is written: every game file the port may replace must be free (the game
        // running in Xenia keeps UI\Textures.zip open; failing halfway would leave rewritten zips
        // without their manifest lines).
        private void CheckWritable(string databasePath)
        {
            string media = Path.Combine(this.TargetRoot, "Media");
            List<string> files = new List<string>();
            files.Add(databasePath);
            files.Add(ZipManifest.PathFor(this.TargetRoot));
            files.Add(MakeTitles.GameRules(this.TargetRoot));
            files.Add(Path.Combine(Path.Combine(media, "UI"), "Textures.zip"));
            files.Add(Path.Combine(Path.Combine(Path.Combine(media, "audio"), "Cars"), "CarModelTuning.zip"));
            files.Add(Path.Combine(Path.Combine(Path.Combine(Path.Combine(media, "audio"), "Cars"), "Engines"), "enginetuning.zip"));
            files.Add(Path.Combine(Path.Combine(Path.Combine(Path.Combine(media, "audio"), "Cars"), "Engines"), "harmonictuning.zip"));
            files.Add(Path.Combine(Path.Combine(media, "cars"), this.CarName + ".zip"));
            files.Add(Path.Combine(Path.Combine(media, "wheels"), this.MediaName + ".zip"));
            string strings = Path.Combine(media, "StringTables");
            if (Directory.Exists(strings))
            {
                files.AddRange(Directory.GetFiles(strings, "*.zip"));
            }
            List<string> busy = new List<string>();
            foreach (string file in files)
            {
                if (!File.Exists(file))
                {
                    continue;
                }
                try
                {
                    using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                    }
                }
                catch (IOException)
                {
                    busy.Add(this.Relative(file));
                }
                catch (UnauthorizedAccessException)
                {
                    busy.Add(this.Relative(file));
                }
            }
            // Xenia keeps Media\UI\Textures.zip open while it runs: the port goes on without the thumbnail
            // (a later port of the same car with the emulator closed adds it).
            string textures = this.Relative(Path.Combine(Path.Combine(media, "UI"), "Textures.zip"));
            if (busy.Remove(textures))
            {
                this.skipThumbnail = true;
            }
            if (busy.Count > 0)
            {
                throw new IOException("Files in use (close the game or the emulator first): " + string.Join(", ", busy.ToArray()));
            }
        }

        private void Write(string path, byte[] data)
        {
            path = this.Out(path);
            if (File.Exists(path))
            {
                this.Backup(path);
            }
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, data);
            File.Delete(path);
            File.Move(temp, path);
            this.Line("saved {0}", this.Relative(path));
        }

        private void Backup(string path)
        {
            // Trial files are not the game's (and a pack's second car would copy UI\Textures.zip again).
            if (!File.Exists(path) || this.OutputRoot != null || !BackupPolicy.Enabled)
            {
                return;
            }
            string folder = this.BackupFolder;
            Directory.CreateDirectory(folder);
            string copy = Path.Combine(folder, this.stamp + "_bak_" + Path.GetFileName(path));
            for (int i = 2; File.Exists(copy); i++)
            {
                copy = Path.Combine(folder, this.stamp + "_bak" + i + "_" + Path.GetFileName(path));
            }
            File.Copy(path, copy);
        }

        // ---- Donor from an installed DLC pack (TODO D-18) ----

        private static readonly Dictionary<string, string> donorRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // The donor is a car of a pack installed in Xenia (not in the target game's folder): the pack is laid out as a game
        // folder once (DlcSource.Stage, kept under %TEMP%) and the donor's zips, wheels and rows are read from there.
        // Nothing is written to the pack.
        private void ResolveDonor()
        {
            this.donorDb = null;
            if (GamePaths.FindCar(this.TargetRoot, this.DonorMediaName) != null)
            {
                return;
            }
            string notFound = "The donor " + this.DonorMediaName + " has no zip in the FM4 folder and no DLC pack installed in Xenia has it" + (string.IsNullOrEmpty(this.XeniaContent) || !Directory.Exists(this.XeniaContent) ? " (Xenia's content folder is not set: Tools > Settings)" : "") + ". Pick another donor.";
            if (string.IsNullOrEmpty(this.XeniaContent) || !Directory.Exists(this.XeniaContent))
            {
                throw new InvalidOperationException(notFound);
            }
            string root;
            if (!donorRoots.TryGetValue(this.XeniaContent + "|" + this.DonorMediaName, out root))
            {
                root = null;
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                List<InstalledDlc> packs = InstalledPacks(this.XeniaContent);
                double scan = watch.Elapsed.TotalSeconds;
                watch.Reset();
                watch.Start();
                foreach (InstalledDlc dlc in packs)
                {
                    if (dlc.Source == null || dlc.Source.Problem != null)
                    {
                        continue;
                    }
                    if (dlc.Source.Cars().Exists(delegate(string c) { return string.Equals(c, this.DonorMediaName, StringComparison.OrdinalIgnoreCase); }))
                    {
                        root = dlc.Source.Stage(new StringBuilder());
                        double stage = watch.Elapsed.TotalSeconds;
                        watch.Reset();
                        watch.Start();
                        DlcSource.Materialize(root, this.DonorMediaName);   // the donor's zip stays: later cars of the run use it again
                        this.Line("time: donor {0}: packs scanned {1:0.0} s, pack staged {2:0.0} s, donor zip written {3:0.0} s", this.DonorMediaName, scan, stage, watch.Elapsed.TotalSeconds);
                        break;
                    }
                }
                if (root != null)
                {
                    donorRoots[this.XeniaContent + "|" + this.DonorMediaName] = root;   // a miss is not kept: a pack installed meanwhile is found next time
                }
            }
            if (root == null)
            {
                throw new InvalidOperationException(notFound);
            }
            this.DonorRoot = root;
            this.donorDb = SqliteDatabase.FromBytes(File.ReadAllBytes(Path.Combine(Path.Combine(Path.Combine(root, "Media"), "db"), "gamedb.slt")));
            this.Line("donor {0}: a car of the DLC pack installed in Xenia (read from {1})", this.DonorMediaName, root);
        }

        private static List<InstalledDlc> installedPacks;
        private static string installedPacksFor;

        // The packs installed in Xenia, read once: the scan opens every package, and every new donor of a DLC pack scanned
        // them all again (43 s for one car of a 5-car trial, 2026-10-31). Read again when the content folder changed.
        private static List<InstalledDlc> InstalledPacks(string content)
        {
            System.Text.StringBuilder signature = new System.Text.StringBuilder(content).Append('|');
            try
            {
                foreach (string entry in Directory.GetFileSystemEntries(content))
                {
                    signature.Append(entry).Append('|').Append(File.GetLastWriteTimeUtc(entry).Ticks).Append(';');
                    if (File.Exists(entry))
                    {
                        signature.Append(new FileInfo(entry).Length).Append(';');
                    }
                }
            }
            catch (IOException)
            {
            }
            string key = signature.ToString();
            if (installedPacks == null || installedPacksFor != key)
            {
                installedPacks = new List<InstalledDlc>(InstalledDlc.Scan(ForzaGame.Motorsport4, content, null));
                installedPacksFor = key;
            }
            return installedPacks;
        }

        // A value of the donor: in the target database, else in its pack's.
        private object DonorScalar(string sql, params object[] args)
        {
            object value = this.target.Scalar(sql, args);
            if ((value == null || value == DBNull.Value) && this.donorDb != null)
            {
                value = this.donorDb.Scalar(sql, args);
            }
            return value;
        }

        // The zipmanifest lines a new zip can be modelled on: the donor's, and for a DLC donor (it has no line in the game's
        // manifest) the lines of cars or wheels the game has.
        private List<string> ManifestLikes(string folder, string donorMedia)
        {
            List<string> likes = new List<string>();
            likes.Add("game:\\media\\" + folder + "\\" + donorMedia.ToLowerInvariant() + ".zip");
            if (this.DonorRoot != null)
            {
                string sql = (folder == "cars") ? "SELECT MediaName FROM Data_Car ORDER BY Id LIMIT 40" : "SELECT MediaName FROM List_Wheels ORDER BY ID LIMIT 40";
                foreach (object[] row in this.target.Query(sql).Rows)
                {
                    if (row[0] is string)
                    {
                        likes.Add("game:\\media\\" + folder + "\\" + ((string) row[0]).ToLowerInvariant() + ".zip");
                    }
                }
            }
            return likes;
        }

        private void UpdateManifest()
        {
            string manifest = this.Out(ZipManifest.PathFor(this.TargetRoot));
            if (this.OutputRoot != null && !File.Exists(manifest))
            {
                File.Copy(ZipManifest.PathFor(this.TargetRoot), manifest);
            }
            this.Backup(manifest);
            foreach (string path in this.changedZips.Keys)
            {
                ZipManifest.Update(manifest, ZipManifest.GamePath(this.TargetRoot, path), this.Out(path));
            }
            foreach (string path in this.newZips)
            {
                string folder = Path.GetFileName(Path.GetDirectoryName(path)).ToLowerInvariant();
                string like = null;
                foreach (string candidate in this.ManifestLikes(folder, (folder == "cars") ? this.DonorMediaName : this.DonorWheel()))
                {
                    like = candidate;
                    if (ZipManifest.Add(manifest, ZipManifest.GamePath(this.TargetRoot, path), this.Out(path), candidate))
                    {
                        like = null;
                        break;
                    }
                }
                if (like != null)
                {
                    throw new InvalidOperationException("zipmanifest.xml has no line like " + like + ".");
                }
            }
            this.Line("zipmanifest.xml: {0} lines updated, {1} added", this.changedZips.Count, this.newZips.Count);
        }

        // What the car took from the donor, at the end of the report: content (files or values the
        // car uses as its own) apart from donor files used only as a layout model.
        private void DonorReport()
        {
            this.Line("");
            this.Line("donor {0}, used for:", this.DonorMediaName);
            this.Line("  content{0}", (this.donorContent.Count == 0) ? ": nothing" : "");
            foreach (string use in this.donorContent)
            {
                this.Line("    " + use.Replace("{", "{{").Replace("}", "}}"));
            }
            this.Line("  layout only{0}", (this.donorFormat.Count == 0) ? ": nothing" : "");
            foreach (string use in this.donorFormat)
            {
                this.Line("    " + use.Replace("{", "{{").Replace("}", "}}"));
            }
            this.Line("");
        }

        private string DonorWheel()
        {
            return (this.DonorScalar("SELECT w.MediaName FROM List_Wheels w JOIN Data_Car c ON c.StockWheelID = w.ID WHERE c.Id = ?", this.donorId) as string) ?? this.DonorMediaName;
        }

        private string Relative(string path)
        {
            return path.StartsWith(this.TargetRoot, StringComparison.OrdinalIgnoreCase) ? path.Substring(this.TargetRoot.TrimEnd('\\').Length + 1) : path;
        }

        private void Line(string format, params object[] args)
        {
            this.Report.AppendLine(string.Format(CultureInfo.InvariantCulture, format, args));
        }

        #endregion
    }
}
