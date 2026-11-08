namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Media;
    using System.Text;
    using System.Text.RegularExpressions;

    // Listens to the engine sound of a car (Change Car Sound). The sound of an engine is its tuning file <MediaName>_ET.xml
    // (Engines\enginetuning.zip): per part (exhaust, intake, engine ambience) it names a harmonic tuning (<name>_HT.xml,
    // harmonictuning.zip) that names the sound bank (Soundbanks\lod1\<bank>.fsb) and lists its loops with the rpm each was
    // recorded at. The loop nearest to the asked position of the rev range is decoded from the bank by vgmstream (the same
    // decoder as the turbo preview) into a WAV in the temp folder and played in a loop. The files are looked up in the
    // places given: a DLC pack's zips (an added car carries its own tuning and banks) and the FM4 game folders.
    internal sealed class EnginePreview
    {
        public static readonly string[] Parts = { "Exhaust", "EngineIntake", "EngineAmbient" };

        private static readonly Regex Loop = new Regex("<Loop\\s+wavebankindex=\"(\\d+)\"\\s+rpm_min=\"(\\d+)\"\\s+rpm_sample=\"(\\d+)\"", RegexOptions.IgnoreCase);
        private static readonly Regex Bank = new Regex("Soundbank\\s+name=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        private static SoundPlayer player;

        private readonly List<string> gameRoots = new List<string>();
        private readonly List<ForzaZip> packZips = new List<ForzaZip>();

        // packFolder: the work folder of a DLC pack (its zips are searched first); gameRoots: FM4 folders (the Media folder inside).
        public EnginePreview(string packFolder, params string[] roots)
        {
            if (!string.IsNullOrEmpty(packFolder) && Directory.Exists(packFolder))
            {
                foreach (string file in Directory.GetFiles(packFolder, "*.zip", SearchOption.AllDirectories))
                {
                    if (Path.GetFileName(file).StartsWith("StringTables", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    try
                    {
                        this.packZips.Add(new ForzaZip(file));
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            foreach (string root in roots)
            {
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root) && !this.gameRoots.Contains(root))
                {
                    this.gameRoots.Add(root);
                }
            }
        }

        // The text file called fileName: in the pack's zips, then in the game's loose folder and zip of that kind.
        private byte[] FindData(string fileName, string zipFile, string looseFolder)
        {
            return this.FindData(fileName, "Engines", zipFile, looseFolder);
        }

        private byte[] FindData(string fileName, string folder, string zipFile, string looseFolder)
        {
            foreach (ForzaZip zip in this.packZips)
            {
                byte[] data = ReadEntry(zip, fileName);
                if (data != null)
                {
                    return data;
                }
            }
            foreach (string root in this.gameRoots)
            {
                string engines = Path.Combine(Path.Combine(Path.Combine(Path.Combine(root, "Media"), "audio"), "cars"), folder);
                string loose = Path.Combine(Path.Combine(engines, looseFolder), fileName);
                if (File.Exists(loose))
                {
                    return File.ReadAllBytes(loose);
                }
                string zipPath = Path.Combine(engines, zipFile);
                if (File.Exists(zipPath))
                {
                    byte[] data = ReadEntry(new ForzaZip(zipPath), fileName);
                    if (data != null)
                    {
                        return data;
                    }
                }
            }
            return null;
        }

        private static byte[] ReadEntry(ForzaZip zip, string fileName)
        {
            foreach (ForzaZip.Entry entry in zip.Entries)
            {
                string name = entry.Name.Replace('\\', '/');
                if (name.Equals(fileName, StringComparison.OrdinalIgnoreCase) || name.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return zip.Read(entry);
                }
            }
            return null;
        }

        // The bank file on disk (a pack's is written to the temp folder), or null.
        private string FindBank(string bankName, bool extract)
        {
            foreach (ForzaZip zip in this.packZips)
            {
                byte[] data = ReadEntry(zip, bankName);
                if (data != null)
                {
                    if (!extract)
                    {
                        return zip.FilePath;
                    }
                    string folder = Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudioEngine"), "banks");
                    Directory.CreateDirectory(folder);
                    string file = Path.Combine(folder, Math.Abs((zip.FilePath + "|" + data.Length).GetHashCode()).ToString("x8", CultureInfo.InvariantCulture) + "_" + bankName);
                    if (!File.Exists(file) || new FileInfo(file).Length != data.Length)
                    {
                        File.WriteAllBytes(file, data);
                    }
                    return file;
                }
            }
            foreach (string root in this.gameRoots)
            {
                string banks = Path.Combine(Path.Combine(Path.Combine(Path.Combine(Path.Combine(root, "Media"), "audio"), "cars"), "Engines"), "soundbanks");
                if (Directory.Exists(banks))
                {
                    string[] found = Directory.GetFiles(banks, bankName, SearchOption.AllDirectories);
                    if (found.Length > 0)
                    {
                        return found[0];
                    }
                }
            }
            return null;
        }

        // The car's model tuning (<car>_CMT.xml: where its engine, exhaust and intake sit on the car), or null. A car without one has no sound model.
        public byte[] FindModel(string carMedia)
        {
            return this.FindData(carMedia + "_CMT.xml", "", "CarModelTuning.zip", "CarModelTuning");
        }

        // The engine tuning <engineMedia>_ET.xml (pack first, then the game), or null.
        public byte[] FindTuning(string engineMedia)
        {
            return this.FindData(engineMedia + "_ET.xml", "enginetuning.zip", "enginetuning");
        }

        // A harmonic tuning by its file name (<name>_HT.xml), or null.
        public byte[] FindHarmonic(string fileName)
        {
            return this.FindData(fileName, "harmonictuning.zip", "harmonictuning");
        }

        // What is missing of the engine's sound (the files of its tuning, harmonic tunings and banks), one text each; empty when complete.
        public List<string> Diagnose(string engineMedia)
        {
            List<string> problems = new List<string>();
            byte[] tuning = this.FindData(engineMedia + "_ET.xml", "enginetuning.zip", "enginetuning");
            if (tuning == null)
            {
                problems.Add("engine tuning " + engineMedia + "_ET.xml is in neither the pack nor the game");
                return problems;
            }
            List<string> seen = new List<string>();
            foreach (Match reference in Regex.Matches(Encoding.GetEncoding(28591).GetString(tuning), "\"([^\"]+_HT\\.xml)\"", RegexOptions.IgnoreCase))
            {
                string ht = reference.Groups[1].Value;
                if (ht.StartsWith("NA.", StringComparison.OrdinalIgnoreCase) || seen.Contains(ht.ToLowerInvariant()))
                {
                    continue;
                }
                seen.Add(ht.ToLowerInvariant());
                byte[] harmonic = this.FindData(ht, "harmonictuning.zip", "harmonictuning");
                if (harmonic == null)
                {
                    problems.Add("harmonic tuning " + ht + " (of " + engineMedia + ") is missing: that level is silent");
                    continue;
                }
                string harmonicText = Encoding.GetEncoding(28591).GetString(harmonic);
                Match bank = Bank.Match(harmonicText);
                if (bank.Success && !bank.Groups[1].Value.Equals("NA.fsb", StringComparison.OrdinalIgnoreCase) && this.FindBank(bank.Groups[1].Value, false) != null)
                {
                    // the loops against the bank's own header (a loop on a sample the bank does not have is silent or a crash)
                    string file = this.FindBank(bank.Groups[1].Value, true);
                    if (file != null)
                    {
                        problems.AddRange(SoundIndex.CheckBankFile(ht, harmonicText, file).ConvertAll<string>(delegate(string p) { return p + " (of " + engineMedia + ")"; }));
                    }
                }
                else if (bank.Success && !bank.Groups[1].Value.Equals("NA.fsb", StringComparison.OrdinalIgnoreCase))
                {
                    string package = SoundBankIndex.PackageOf(ForzaGame.Motorsport4, bank.Groups[1].Value);
                    problems.Add((package != null)
                        ? "sound bank " + bank.Groups[1].Value + " (of " + ht + ") is only in the installed package \"" + package + "\": the sound works while that package is installed"
                        : "sound bank " + bank.Groups[1].Value + " (of " + ht + ") is missing: that level is silent");
                }
            }
            return problems;
        }

        // Plays the loop of the car's engine (MediaName) nearest to 'position' (0 = idle, 1 = redline) of the part. Returns what is
        // playing, or throws a readable message.
        // A part the engine does not have (its harmonic tuning is NA.xml, as the R8 V10's exhaust and intake) is skipped for the next.
        public string Play(string media, string part, double position)
        {
            List<string> order = new List<string>();
            order.Add(part);
            order.AddRange(Parts);
            Exception first = null;
            foreach (string candidate in order)
            {
                try
                {
                    string played = this.PlayPart(media, candidate, position);
                    return (candidate == part) ? played : played + " (" + part + " is not in this engine: " + candidate + " plays)";
                }
                catch (InvalidOperationException exception)
                {
                    first = first ?? exception;
                }
            }
            throw first ?? new InvalidOperationException(media + " has no engine sound.");
        }

        private string PlayPart(string media, string part, double position)
        {
            byte[] tuning = this.FindData(media + "_ET.xml", "enginetuning.zip", "enginetuning");
            if (tuning == null)
            {
                throw new FileNotFoundException("The engine tuning " + media + "_ET.xml was not found in the game or the pack.");
            }
            Match upgrade = Regex.Match(Encoding.GetEncoding(28591).GetString(tuning), "<" + part + ">\\s*<Upgrade([^>]*)/?>", RegexOptions.Singleline);
            Match l0 = upgrade.Success ? Regex.Match(upgrade.Groups[1].Value, "L0=\"([^\"]+)\"") : Match.Empty;
            if (!l0.Success)
            {
                throw new InvalidOperationException(media + " has no " + part + " sound.");
            }
            string ht = l0.Groups[1].Value;
            if (ht.StartsWith("NA.", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(media + " has no " + part + " sound (NA).");
            }
            byte[] harmonic = this.FindData(ht, "harmonictuning.zip", "harmonictuning");
            if (harmonic == null)
            {
                throw new FileNotFoundException("The harmonic tuning " + ht + " was not found.");
            }
            string text = Encoding.GetEncoding(28591).GetString(harmonic);
            Match bank = Bank.Match(text);
            if (!bank.Success)
            {
                throw new InvalidDataException(ht + " names no sound bank.");
            }
            SortedDictionary<int, int> byRpm = new SortedDictionary<int, int>();       // rpm_sample -> wavebankindex
            foreach (Match loop in Loop.Matches(text))
            {
                byRpm[int.Parse(loop.Groups[3].Value, CultureInfo.InvariantCulture)] = int.Parse(loop.Groups[1].Value, CultureInfo.InvariantCulture);
            }
            if (byRpm.Count == 0)
            {
                throw new InvalidDataException(ht + " has no loops.");
            }
            List<KeyValuePair<int, int>> loops = new List<KeyValuePair<int, int>>(byRpm);
            KeyValuePair<int, int> chosen = loops[(int) Math.Round(Math.Max(0, Math.Min(1, position)) * (loops.Count - 1))];
            string file = this.FindBank(bank.Groups[1].Value, true);
            if (file == null)
            {
                throw new FileNotFoundException("The sound bank " + bank.Groups[1].Value + " was not found.");
            }
            string wav = Decode(file, chosen.Value + 1);
            Stop();
            player = new SoundPlayer(wav);
            player.PlayLooping();
            return bank.Groups[1].Value + ", loop at " + chosen.Key.ToString(CultureInfo.InvariantCulture) + " rpm";
        }

        // One sample (1-based subsong) of the bank as a WAV, kept in the temp folder.
        private static string Decode(string bank, int subsong)
        {
            if (!File.Exists(TurboPreview.Decoder))
            {
                throw new FileNotFoundException("vgmstream-cli.exe is missing next to the program (vgmstream folder).");
            }
            string folder = Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudioEngine"), "wav");
            Directory.CreateDirectory(folder);
            FileInfo info = new FileInfo(bank);
            string key = Math.Abs((info.FullName + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks).GetHashCode()).ToString("x8", CultureInfo.InvariantCulture);
            string wav = Path.Combine(folder, key + "_" + subsong.ToString(CultureInfo.InvariantCulture) + ".wav");
            if (File.Exists(wav))
            {
                return wav;
            }
            ProcessStartInfo start = new ProcessStartInfo(TurboPreview.Decoder, "-i -s " + subsong.ToString(CultureInfo.InvariantCulture) + " -o \"" + wav + "\" \"" + bank + "\"");
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            using (Process process = Process.Start(start))
            {
                string error = null;
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { error += e.Data + "\n"; };
                process.BeginErrorReadLine();
                process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0 || !File.Exists(wav))
                {
                    throw new IOException("vgmstream could not decode " + Path.GetFileName(bank) + " (sample " + subsong + "):\n" + error);
                }
            }
            return wav;
        }

        public static void Stop()
        {
            if (player != null)
            {
                player.Stop();
                player.Dispose();
                player = null;
            }
        }
    }
}
