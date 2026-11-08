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
    public partial class CarPort
    {
        public string SourceRoot;       // FH2 (or FH) game folder (with media)
        public string TargetRoot;       // FM4 game folder
        public string MediaName;        // the car, e.g. MCL_P1_13
        public string DonorMediaName;   // an FM4 car close to it, e.g. MCL_MP412C_11 (brakes, templates)
        public string DonorRoot;        // the donor is a car of an installed DLC pack: that pack laid out as a game folder (found by ResolveDonor), else null
        public string Tires = "normal"; // "normal": the source's own tire models (BuildTireZips); "offroad" / "small": the program's Service\OffroadTires / SmallTires models under the car's name
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

    }
}
