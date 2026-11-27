namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    // Brings a car from Forza Horizon 1 or 2, or from the Forza Motorsport 2 game folder, into Forza Motorsport 3 by
    // chaining the two ports that exist (docs/PORT_FH_TO_FM3.md, route A, user 2026-11-21):
    //   step 1  CarPort (FH / FM2 -> FM4) as a trial run into a temporary folder: the car in FM4 form (zip, wheel, database with
    //           its rows, string tables, sound files, thumbnail). The FM4 folder is only read.
    //   step 2  CarPortToFm3 with that folder as its pack (PackRoot): the FM4-form car into FM3, under OutputRoot.
    // Nothing is written into a game folder. FM3 has no tyres per car, so step 1 always runs with the source's own tyres
    // and step 2 does not carry them (FM3 draws its generic tyre).
    public class CarPortViaFm4
    {
        public string SourceRoot;       // Forza Horizon, Forza Horizon 2 or Forza Motorsport 2 game folder
        public string Fm4Root;          // Forza Motorsport 4 game folder (read only: the form the car passes through)
        public string TargetRoot;       // Forza Motorsport 3 game folder (read only)
        public string OutputRoot;       // where the FM3 result goes (required)
        public string MediaName;        // the Horizon car
        public string DonorMediaName;   // the FM3 template car (as CarPortToFm3.DonorMediaName)
        public string Fm4DonorMediaName;   // the FM4 template car of step 1; null = the closest FM4 car (ClosestFm4Car)
        public bool AllowFm4OnlyDash;
        public string XeniaContent;     // Xenia's FM3 content folder: the car id is kept free of its packages (CarPortToFm3.XeniaContent)
        public string DonorRoot;        // the FM3 template car is a car of an FM3 package: that package laid out as a game folder (CarPortToFm3.DonorRoot)
        public Dictionary<string, object> Fixes;   // the review's choices for step 1 (CarPort.Fixes: issue key -> value); null = none
        // The FM3 window's choices (user 2026-11-22, as the FM4 window's): the brakes for a car without its own go to step 1 (the
        // generic brake models are FM4-form files, converted by step 2); arcade, make and sound are FM3's, for step 2.
        public CarPort.BrakeChoice Brakes;   // null = the car's own
        public bool Arcade = true;
        public long? MakeId;            // an FM3 make; null = the car's own
        public long? ChosenCarId;       // the DLC window's First car id (CarPortToFm3.ChosenCarId); null = the first free id
        public string SoundFrom;        // an FM3 car whose sound files the car takes; null = its own, converted
        // A pack trial: the FM4-form folder shared by every car of the pack (kept, the caller deletes it). Each car's step 1 then
        // sees the earlier cars' FM4 forms and takes ids of its own, as the FM4 window's pack trial does. Without it every step 1
        // started from a clean FM4 and every FM2 car got the same car / drivetrain / wheel ids (pack FM2_TEST 2026-11-22: five cars
        // on the RX-7's wheel and drivetrain, the game looped when one was bought). null = a folder of its own (one car).
        public string SharedStage;
        public readonly List<CarPort.Issue> Issues = new List<CarPort.Issue>();   // after Run: what step 1 found not regular for FM4, with its choices
        public readonly StringBuilder Report = new StringBuilder();
        public string SecondReport = "";   // the report of step 2 alone (what the FM3 port changed or could not do)
        public string PortedAs;         // the car's name in FM3, after Run

        public static bool IsHorizon(string root)
        {
            ForzaGame game = GameInfo.Detect(root);
            return game == ForzaGame.Horizon1 || game == ForzaGame.Horizon2;
        }

        // The sources that reach FM3 through their FM4 form: FH1, FH2 and the FM2 game folder (user 2026-11-21:
        // FM2 -> FM3 by the same chain; step 1 lays the FM2 car out on an FM4 template as the FM4 window does, Fm2Source).
        // An FM2 DLC package staged as a game folder (raw=fm2) too, 2026-11-22: step 1 (CarPort) lays its car out with the FM2
        // game's wheels, thumbnails and strings, as the FM4 window does with such a package.
        public static bool GoesThroughFm4(string root)
        {
            return IsHorizon(root) || (GameInfo.Detect(root) == ForzaGame.Motorsport2 && (!DlcSource.IsStage(root) || DlcSource.IsFm2Raw(root)));
        }

        // What a renamed port gets after its name, as the FM4 window does (CarPortForm.SuffixFor): _FM2 for FM2, _FH for both Horizons.
        private static string Suffix(string sourceRoot)
        {
            return (GameInfo.Detect(sourceRoot) == ForzaGame.Motorsport2) ? "_FM2" : "_FH";
        }

        private static string Renamed(string mediaName, string suffix)
        {
            return (mediaName.Length + suffix.Length > 40 ? mediaName.Substring(0, 40 - suffix.Length) : mediaName) + suffix;
        }

        public void Run()
        {
            if (string.IsNullOrEmpty(this.OutputRoot))
            {
                throw new InvalidOperationException("OutputRoot is required: no game folder is written.");
            }
            if (!GoesThroughFm4(this.SourceRoot))
            {
                throw new InvalidOperationException("The source must be a Forza Horizon, Forza Horizon 2 or Forza Motorsport 2 folder.");
            }
            string sourceName = GameInfo.Name(GameInfo.Detect(this.SourceRoot));
            string sourceTag = GameInfo.Tag(GameInfo.Detect(this.SourceRoot));
            string suffix = Suffix(this.SourceRoot);
            if (this.Fm4Root == null || GameInfo.Detect(this.Fm4Root) != ForzaGame.Motorsport4)
            {
                throw new InvalidOperationException("A " + sourceName + " car reaches Forza Motorsport 3 through its Forza Motorsport 4 form: set the Forza Motorsport 4 folder in Tools > Settings > Folders.");
            }
            string stage = this.SharedStage ?? Path.Combine(Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "CarPortViaFm4"), this.MediaName + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(stage);
            System.Diagnostics.Stopwatch lap = System.Diagnostics.Stopwatch.StartNew();
            System.Diagnostics.Stopwatch total = System.Diagnostics.Stopwatch.StartNew();
            List<KeyValuePair<string, double>> times = new List<KeyValuePair<string, double>>();
            try
            {
                // ---- step 1: Horizon -> FM4 form ----
                string fm4Donor = this.Fm4DonorMediaName ?? ClosestFm4Car(this.SourceRoot, this.Fm4Root, this.MediaName);
                CarPortToFm3.Lap(lap, times, "FM4 template search");
                // FM4 has a car of this name: the FM4 form sits beside it under another name, as the FM4 window does
                string fm4Name = HasCar(this.Fm4Root, this.MediaName) ? Renamed(this.MediaName, suffix) : null;
                this.Report.AppendLine("STEP 1 of 2: " + sourceName + " -> Forza Motorsport 4 form (temporary, the FM4 folder is not written); FM4 template " + fm4Donor);
                // The review of the FM4 port (Horizon materials no FM4 car uses, database values FM4 lacks) is the user's:
                // this run applies the fixes chosen so far (Fixes) and leaves what it finds in Issues for the window to show
                // (user 2026-11-21: "devo fare io i fix"; nothing is replaced on its own).
                CarPort first = this.FirstStep(stage, fm4Donor, fm4Name, this.Fixes);   // FirstStep leaves the material fixes out: they are FM3 names, for step 2
                try
                {
                    first.Run();
                    foreach (CarPort.Issue issue in first.Issues)
                    {
                        if (issue.Kind != "material")
                        {
                            this.Issues.Add(issue);     // database values: as the FM4 port reports them
                        }
                    }
                }
                finally
                {
                    this.Report.Append(Indent(first.Report.ToString()));
                }
                CarPortToFm3.Lap(lap, times, "step 1 (" + sourceTag + " -> FM4 form)");
                this.Report.AppendLine("  FM4's id rules and FM4's material review are not run on this temporary form: the ids are given by step 2 and the materials are checked against FM3's cars, below.");

                // ---- step 2: FM4 form -> FM3 ----
                CarPortToFm3 second = new CarPortToFm3();
                second.SourceRoot = this.Fm4Root;
                second.PackRoot = stage;
                second.TargetRoot = this.TargetRoot;
                second.OutputRoot = this.OutputRoot;
                second.MediaName = first.RenameTo ?? this.MediaName;
                second.DonorMediaName = this.DonorMediaName ?? ClosestFm4Car(this.SourceRoot, this.TargetRoot, this.MediaName);   // no template given: the closest FM3 car
                CarPortToFm3.Lap(lap, times, "FM3 template search");
                second.AllowFm4OnlyDash = this.AllowFm4OnlyDash;
                second.DonorRoot = this.DonorRoot;
                second.XeniaContent = this.XeniaContent;
                second.Fixes = this.Fixes;      // the material fixes are FM3 names: step 2 writes them
                second.Arcade = this.Arcade;
                second.MakeId = this.MakeId;
                second.ChosenCarId = this.ChosenCarId;   // the DLC window's First car id
                second.SoundDonorMediaName = this.SoundFrom;   // (the brakes and their angles were written by step 1, with the model)
                // the Horizon name again when FM3 has no car of that name
                // FM3 has a car of that name too, in its folder or in a package of its second disc / DLC (the 458 Italia
                // FER_F142_10: a second car of that name crashed the game): the port sits beside it as <name>_FH.
                bool inFm3 = HasCar(this.TargetRoot, this.MediaName) || DlcCatalog.PackOf(ForzaGame.Motorsport3, this.MediaName) != null;
                if (inFm3)
                {
                    second.RenameTo = Renamed(this.MediaName, suffix);
                    this.Report.AppendLine("  Forza Motorsport 3 has a car named " + this.MediaName + " (in its folder or in a second disc / DLC package): ported beside it as " + second.RenameTo);
                }
                else if (first.RenameTo != null)
                {
                    second.RenameTo = this.MediaName;
                }
                this.Report.AppendLine();
                this.Report.AppendLine("STEP 2 of 2: Forza Motorsport 4 form -> Forza Motorsport 3");
                try
                {
                    second.Run();
                }
                finally
                {
                    this.SecondReport = second.Report.ToString();
                    this.Report.Append(this.SecondReport);
                }
                CarPortToFm3.Lap(lap, times, "step 2 (FM4 form -> FM3)");
                this.PortedAs = second.RenameTo ?? second.MediaName;
                this.Issues.AddRange(second.Issues);   // the materials, reviewed for FM3 by step 2
            }
            finally
            {
                try
                {
                    if (this.SharedStage == null)
                    {
                        Directory.Delete(stage, true);   // a shared pack stage stays for the next car
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
                // the whole trial: each part, then the total (the steps' own times are in their reports above)
                CarPortToFm3.Lap(lap, times, "temporary folder cleanup");
                this.Report.AppendLine();
                foreach (KeyValuePair<string, double> item in times)
                {
                    this.Report.AppendLine(string.Format(CultureInfo.InvariantCulture, "time {0} -> FM3: {1} {2:0.0} s", sourceTag, item.Key, item.Value));
                }
                this.Report.AppendLine(string.Format(CultureInfo.InvariantCulture, "time {0} -> FM3: total {1:0.0} s", sourceTag, total.Elapsed.TotalSeconds));
            }
        }

        // The FH -> FM4 port as a trial run into 'stage', with the review's fixes when it runs again.
        private CarPort FirstStep(string stage, string donor, string renameTo, Dictionary<string, object> fixes)
        {
            CarPort first = new CarPort();
            first.SourceRoot = this.SourceRoot;
            first.TargetRoot = this.Fm4Root;
            first.MediaName = this.MediaName;
            first.OutputRoot = stage;
            first.BackupFolder = Path.Combine(stage, "bak");
            first.DonorMediaName = donor;
            first.RenameTo = renameTo;
            first.DeferIntegrity = true;   // the rules of FM4's whole database (ids above FM4's largest...) do not apply to a form that never reaches FM4
            if (this.Brakes != null)
            {
                first.Brakes = this.Brakes;   // a car without brakes of its own (FM2, traffic): the generic ones chosen in the window
            }
            if (fixes != null)
            {
                first.Fixes = new Dictionary<string, object>();
                foreach (KeyValuePair<string, object> fix in fixes)
                {
                    if (!fix.Key.StartsWith("material:", StringComparison.Ordinal))
                    {
                        first.Fixes[fix.Key] = fix.Value;   // database values; the materials are FM3 names, written by step 2
                    }
                }
            }
            return first;
        }

        private static string Indent(string text)
        {
            StringBuilder result = new StringBuilder();
            foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
            {
                // FM4's own material review is not this port's: the materials are checked against FM3's cars (ReviewMaterials)
                if (line.IndexOf("REVIEW ", StringComparison.Ordinal) >= 0 || line.IndexOf("materials no FM4 car uses", StringComparison.Ordinal) >= 0)
                {
                    continue;
                }
                if (line.Length > 0)
                {
                    result.AppendLine("  " + line);
                }
            }
            return result.ToString();
        }

        private static string Db(string root)
        {
            return Path.Combine(Path.Combine(Path.Combine(root, "Media"), "db"), "gamedb.slt");
        }

        private static bool HasCar(string root, string mediaName)
        {
            using (SqliteDatabase db = SqliteDatabase.FromBytes(File.ReadAllBytes(Db(root))))
            {
                return db.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", mediaName) != null;
            }
        }

        private sealed class Car
        {
            public string MediaName;
            public long Year;
            public long Make;
            public long Placement;
            public long Drive;
            public long Type;
            public double Weight;
        }

        private static List<Car> Cars(string root)
        {
            List<Car> cars = new List<Car>();
            using (SqliteDatabase db = SqliteDatabase.FromBytes(File.ReadAllBytes(Db(root))))
            {
                bool drive = false;
                foreach (object[] column in db.Query("PRAGMA table_info(Data_Car)").Rows)
                {
                    drive |= string.Equals((string) column[1], "DriveTypeID", StringComparison.OrdinalIgnoreCase);
                }
                string driveColumn = drive ? "DriveTypeID" : "(SELECT DrivetrainId FROM Powertrains WHERE Powertrains.Id = Data_Car.PowertrainID)";
                foreach (object[] row in db.Query("SELECT MediaName, Year, MakeID, EnginePlacementID, " + driveColumn + ", CarTypeID, CurbWeight FROM Data_Car ORDER BY Id").Rows)
                {
                    if (!(row[0] is string))
                    {
                        continue;
                    }
                    Car car = new Car();
                    car.MediaName = (string) row[0];
                    car.Year = Long(row[1]);
                    car.Make = Long(row[2]);
                    car.Placement = Long(row[3]);
                    car.Drive = Long(row[4]);
                    car.Type = Long(row[5]);
                    car.Weight = (row[6] == null || row[6] is DBNull) ? 0 : Convert.ToDouble(row[6], CultureInfo.InvariantCulture);
                    cars.Add(car);
                }
            }
            return cars;
        }

        private static long Long(object value)
        {
            return (value == null || value is DBNull) ? -1 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        // The car of another game's folder (FM4 for step 1, FM3 for step 2) closest to the Horizon one, by the score of the FM4 port window (same make 4, engine placement 2,
        // drive 2, type 1, minus the years apart / 15 and 3 x the weight difference): the template of step 1 (brakes
        // fallback, CarAttribs / versiondata templates). Only cars with a zip in the FM4 folder.
        public static string ClosestFm4Car(string sourceRoot, string fm4Root, string mediaName)
        {
            Car car = null;
            foreach (Car candidate in Cars(sourceRoot))
            {
                if (string.Equals(candidate.MediaName, mediaName, StringComparison.OrdinalIgnoreCase))
                {
                    car = candidate;
                }
            }
            if (car == null)
            {
                throw new InvalidOperationException("The source database has no car " + mediaName + ".");
            }
            string best = null;
            double bestScore = double.MinValue;
            foreach (Car donor in Cars(fm4Root))
            {
                if (GamePaths.FindMedia(fm4Root, "cars", donor.MediaName) == null)
                {
                    continue;
                }
                double score = 0;
                score += (car.Make == donor.Make) ? 4 : 0;
                score += (car.Placement == donor.Placement) ? 2 : 0;
                score += (car.Drive == donor.Drive) ? 2 : 0;
                score += (car.Type == donor.Type) ? 1 : 0;
                score -= Math.Abs(car.Year - donor.Year) / 15.0;
                score -= (car.Weight > 0) ? 3 * Math.Abs(car.Weight - donor.Weight) / car.Weight : 0;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = donor.MediaName;
                }
            }
            if (best == null)
            {
                throw new InvalidOperationException("The target game folder has no car to use as a template.");
            }
            return best;
        }
    }
}
