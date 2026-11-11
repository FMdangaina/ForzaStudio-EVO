namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Xml;

    // Engine sound and the thumbnail.
    public partial class CarPort
    {
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
                    // The user's choice: that FM4 car's whole sound setup (its banks are FM4's), turned into the car's
                    // cylinder count when the two engines differ.
                    this.Line("engine sound {0}: {1}'s FM4 setup {2}_ET.xml, the user's choice", media, this.SoundFrom, chosenEngine);
                    this.Put(engineTuning, media + "_ET.xml", this.ConvertCylinders(media, engineTuning.Read(chosenTuning), chosenEngine, this.EngineCylinders(this.target, chosenEngine), harmonicPath, harmonic, sourceHarmonic));
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
                    int setupCylinders = this.EngineCylinders(this.target, setup);
                    if (setupCylinders == 0 && !picked)
                    {
                        setupCylinders = this.EngineCylinders(null, setup);
                    }
                    data = this.ConvertCylinders(media, data, setup, setupCylinders, harmonicPath, harmonic, sourceHarmonic);
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
            this.PlaceEngineSound(carModels);
        }

        // The cylinders of an engine (Data_Engine.MediaName -> List_Cylinders.Number) in db (null = the donor's database), 0 if unknown.
        private int EngineCylinders(SqliteDatabase db, string engineMedia)
        {
            if (string.IsNullOrEmpty(engineMedia))
            {
                return 0;
            }
            // The CylinderID read from the engine's database, its meaning from the target's List_Cylinders (the same table
            // in FH1 and FM4; a pack's merge database has none). -1 = a rotary (Rotors = 1): no cylinder rule for those.
            const string Sql = "SELECT CylinderID FROM Data_Engine WHERE MediaName = ? COLLATE NOCASE";
            try
            {
                object id = (db != null) ? db.Scalar(Sql, engineMedia) : this.DonorScalar(Sql, engineMedia);
                if (id == null || id is DBNull)
                {
                    return 0;
                }
                SqliteDatabase.Result row = this.target.Query("SELECT Number, Rotors FROM List_Cylinders WHERE CylinderID = ?", id);
                if (row.Rows.Count == 0 || row.Rows[0][0] == null || row.Rows[0][0] is DBNull)
                {
                    return 0;
                }
                object rotors = row.Rows[0][1];
                if (rotors != null && !(rotors is DBNull) && Convert.ToInt64(rotors, CultureInfo.InvariantCulture) != 0)
                {
                    return -1;
                }
                return Convert.ToInt32(row.Rows[0][0], CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        // Another car's FM4 engine tuning used for this car's engine 'media' (the user's pick, or the stand-in for an
        // incomplete source sound): when the two engines' cylinders differ, the tuning's harmonic tunings are turned into
        // the car's count (SoundConvert.Cylinders, rule checked in game on the FH1 Fiat 595 2026-11-08: rpm x M / N, new
        // <name>_<N>cyl_HT.xml on the same banks, num_cylinders scaled). Returns the tuning to write.
        // The review shows what the rule does from the engine's data (Fixes "soundcyl:<engine>" = Data_Engine.CylinderID):
        // a different pick changes the engine's data (the source's can be wrong: FH1 has the 2-cylinder Fiat 595 at 3),
        // and the sound follows it (user 2026-11-08: "proponi da db, se devo fare override io cambio i dati").
        private byte[] ConvertCylinders(string media, byte[] tuning, string from, int fromCylinders, string harmonicPath, ForzaZip harmonic, ForzaZip sourceHarmonic)
        {
            string key = "soundcyl:" + media;
            object fix;
            object cylinderId = this.target.Scalar("SELECT CylinderID FROM Data_Engine WHERE MediaName = ? COLLATE NOCASE", media);
            if (this.Fixes.TryGetValue(key, out fix) && fix != null && cylinderId != null
                && Convert.ToString(fix, CultureInfo.InvariantCulture) != Convert.ToString(cylinderId, CultureInfo.InvariantCulture))
            {
                this.target.Execute("UPDATE Data_Engine SET CylinderID = ? WHERE MediaName = ? COLLATE NOCASE", fix, media);
                this.Line("  review fix: Data_Engine.CylinderID of {0} {1} -> {2}", media, cylinderId, fix);
                cylinderId = fix;
            }
            int cylinders = this.EngineCylinders(this.target, media);
            // Only a conversion (or a rotary engine) is something to review: the same count is regular, the sound is used
            // as it is and the review stays empty (user 2026-11-11: the Lingenfelter showed "1 thing to fix" on every trial).
            bool converted = cylinders > 0 && fromCylinders > 0 && cylinders != fromCylinders;
            bool rotaryEngine = cylinders < 0 || fromCylinders < 0;
            if (!converted && !rotaryEngine)
            {
                this.Line("  engine sound {0}: {1} ({2} cylinders) used as it is, the engine has the same count", media, from, fromCylinders);
            }
            if (cylinderId != null && !(cylinderId is DBNull) && (converted || rotaryEngine))
            {
                Issue issue = new Issue();
                issue.Key = key;
                issue.Kind = "sound";
                issue.Where = "Data_Engine.CylinderID (" + media + ")";
                issue.Value = Convert.ToString(cylinderId, CultureInfo.InvariantCulture);
                issue.Problem = (cylinders < 0 || fromCylinders < 0)
                    ? string.Format(CultureInfo.InvariantCulture, "engine sound of {0}: a rotary engine, not converted; pick the cylinders if the engine's data is wrong", from)
                    : (cylinders > 0 && fromCylinders > 0 && cylinders != fromCylinders)
                        ? string.Format(CultureInfo.InvariantCulture, "engine sound of {0} ({1} cylinders) converted to {2} cylinders, the engine's data; pick another count to change the data", from, fromCylinders, cylinders)
                        : string.Format(CultureInfo.InvariantCulture, "engine sound of {0} ({1} cylinders) used as it is: the engine has {2}; pick another count to change the data (the sound follows)", from, fromCylinders, cylinders);
                foreach (object[] row in this.target.Query("SELECT CylinderID, Number, Rotors FROM List_Cylinders ORDER BY CylinderID").Rows)
                {
                    Choice choice = new Choice();
                    choice.Value = row[0];
                    bool rotary = row[2] != null && !(row[2] is DBNull) && Convert.ToInt64(row[2], CultureInfo.InvariantCulture) != 0;
                    choice.Label = string.Format(CultureInfo.InvariantCulture, "{0}  {1} {2}", row[0], row[1], rotary ? "rotors" : "cylinders");
                    issue.Choices.Add(choice);
                    if (Convert.ToString(row[0], CultureInfo.InvariantCulture) == issue.Value)
                    {
                        issue.Suggested = row[0];
                    }
                }
                this.Issues.Add(issue);
                this.Line("  REVIEW {0}", issue.Problem);
            }
            if (cylinders < 0 || fromCylinders < 0)
            {
                this.Line("  engine sound {0}: {1} is a rotary engine, sound not converted (the cylinder rule is for piston engines)", media, (cylinders < 0) ? "the car's" : from);
                return tuning;
            }
            if (cylinders <= 0 || fromCylinders <= 0 || cylinders == fromCylinders)
            {
                if (cylinders > 0 && fromCylinders <= 0)
                {
                    this.Line("  WARNING engine sound {0}: the cylinders of {1} are not known, sound not converted to {2} cylinders", media, from, cylinders);
                }
                return tuning;
            }
            ForzaZip changed = this.OpenChanged(harmonicPath);
            string packHarmonic = this.DonorAudio("HarmonicTuning.zip");
            ForzaZip donorHarmonic = (packHarmonic != null) ? new ForzaZip(packHarmonic) : null;
            Func<string, byte[]> read = delegate(string name)
            {
                foreach (ForzaZip zip in new ForzaZip[] { changed, harmonic, sourceHarmonic, donorHarmonic })
                {
                    ForzaZip.Entry entry = (zip != null) ? zip.Find(name) : null;
                    if (entry != null)
                    {
                        return zip.Read(entry);
                    }
                }
                return null;
            };
            SoundConvert.Result result;
            try
            {
                result = SoundConvert.Cylinders(tuning, read, fromCylinders, cylinders);
            }
            catch (InvalidOperationException exception)
            {
                this.Line("  WARNING engine sound {0}: not converted to {1} cylinders: {2}", media, cylinders, exception.Message);
                return tuning;
            }
            Encoding latin = Encoding.GetEncoding(28591);
            foreach (KeyValuePair<string, string> file in result.Harmonics)
            {
                this.Put(changed, file.Key, latin.GetBytes(file.Value));
            }
            this.Line("  engine sound {0}: {1}'s {2}-cylinder sound turned into a {3}-cylinder one (same banks, rpm x {2} / {3})", media, from, fromCylinders, cylinders);
            foreach (string line in result.Log)
            {
                this.Line("    " + line);
            }
            if (result.Harmonics.Count > 0)
            {
                this.donorFormat.Add(string.Format(CultureInfo.InvariantCulture, "engine sound {0}: {1} converted from {2} to {3} cylinders", media, from, fromCylinders, cylinders));
            }
            return latin.GetBytes(result.Tuning);
        }

        // The car's engine sound position: when its model tuning puts the engine on the other side of the cabin than
        // Data_Car.EnginePlacementID (1 front, 2 mid, 3 rear; FH1 Fiat 595: rear in the DB, front in the CMT), the CMT
        // gets FM4's positions and listener pans / volumes for that placement (SoundConvert.Place, checked in game
        // 2026-11-08). A CMT that agrees with the placement stays the source's.
        private void PlaceEngineSound(ForzaZip carModels)
        {
            ForzaZip.Entry entry = carModels.Find(this.CarName + "_CMT.xml");
            object value = this.target.Scalar("SELECT EnginePlacementID FROM Data_Car WHERE Id = ?", this.carId);
            if (entry == null || value == null || value is DBNull)
            {
                return;
            }
            // Fixes "placement:<car>" = Data_Car.EnginePlacementID picked in the review: the car's data changes, the rule follows.
            string key = "placement:" + this.CarName;
            object fix;
            if (this.Fixes.TryGetValue(key, out fix) && fix != null && Convert.ToString(fix, CultureInfo.InvariantCulture) != Convert.ToString(value, CultureInfo.InvariantCulture))
            {
                this.target.Execute("UPDATE Data_Car SET EnginePlacementID = ? WHERE Id = ?", fix, this.carId);
                this.Line("  review fix: Data_Car.EnginePlacementID {0} -> {1}", value, fix);
                value = fix;
            }
            long placement = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            if (placement < 1 || placement > 3)
            {
                return;
            }
            Encoding latin = Encoding.GetEncoding(28591);
            string cmt = latin.GetString(carModels.Read(entry));
            bool cmtFront = SoundConvert.EngineInFront(cmt);
            if (cmtFront == (placement == 1) && fix == null)
            {
                return;
            }
            {
                Issue issue = new Issue();
                issue.Key = key;
                issue.Kind = "sound";
                issue.Where = "Data_Car.EnginePlacementID (" + this.CarName + ")";
                issue.Value = placement.ToString(CultureInfo.InvariantCulture);
                issue.Problem = (cmtFront == (placement == 1))
                    ? "engine sound position: the car model tuning agrees with the engine placement picked, kept"
                    : string.Format(CultureInfo.InvariantCulture, "engine sound position: the car model tuning has the engine {0} the cabin, the car's data says {1}: moved to {1}; pick another placement to change the data", cmtFront ? "in front of" : "behind", (placement == 1) ? "front" : (placement == 2) ? "mid" : "rear");
                string[] names = { "", "front", "mid", "rear" };
                for (int p = 1; p <= 3; p++)
                {
                    Choice choice = new Choice();
                    choice.Value = (long) p;
                    choice.Label = p.ToString(CultureInfo.InvariantCulture) + "  " + names[p];
                    issue.Choices.Add(choice);
                }
                issue.Suggested = placement;
                this.Issues.Add(issue);
                this.Line("  REVIEW {0}", issue.Problem);
            }
            if (cmtFront == (placement == 1))
            {
                return;
            }
            List<string> log = new List<string>();
            try
            {
                cmt = SoundConvert.Place(cmt, placement >= 2, log);
            }
            catch (InvalidOperationException exception)
            {
                this.Line("  WARNING car model tuning {0}_CMT.xml: engine placement {1} but the engine sound sits on the other side, not moved: {2}", this.CarName, placement, exception.Message);
                return;
            }
            this.Put(carModels, this.CarName + "_CMT.xml", latin.GetBytes(cmt));
            this.Line("  car model tuning {0}_CMT.xml: engine {1} in Data_Car but on the other side of the cabin in the CMT: moved", this.CarName, (placement == 1) ? "front" : (placement == 2) ? "mid" : "rear");
            foreach (string line in log)
            {
                this.Line("    " + line);
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
    }
}
