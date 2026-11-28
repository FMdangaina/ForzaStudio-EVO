namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Xml.Linq;

    // The engine sound of an FM4 / FM3 / FH car into FM2 (user 2026-11-22: "the sound must be the source game's").
    //
    // FM2 (study 2026-11-23, E:\DEV\fm4dev\PortToFM2\snd\sndcensus.exe, docs\PORT_TO_FM2.md section 6):
    //   Data_Engine.MediaName (= the car's, 310 of 310) -> DSPTuning.zip\<name>_DSPTuning.xml: per exhaust level 0-3 an HT
    //   (HarmonicTuning.zip\<bank>_HT.xml) and the DSP of the exhaust and, per engine level 0-3, of EngineA / EngineB;
    //   the HT's groups EngineA / EngineB / ExhaustL (/ ExhaustR) address ONE bank, Soundbanks\LOD1 and LOD2\<bank>.fsb.
    //   EmitterData.zip\<car>_ED.xml: emitter positions and the four listeners.
    //   Banks: FSB3 (version 3.1) only: FM2's FMOD knows "FSB3" and "FSB2" (default.xex), no FSB4. Codecs compiled in:
    //   FSB, WAV / raw PCM and XMA (the game's own banks are XMA1); no ADPCM.
    // The sources: ET (EngineTuning.zip) -> three HTs (EngineIntake, EngineAmbient, Exhaust L/R), three FSB4 XMA2 banks
    //   (FM3: some FSB3), the DSP in the ET's HarmonicTunings EmissionGroup0 / 1 / 2 (= intake / ambient / exhaust: same
    //   values as the HTs' own DSP) or, without them, in the HTs; CarModelTuning.zip\<car>_CMT.xml: emitters and the
    //   listeners Front / Follow / Cockpit / Default (= FM2's four CarListener, in that order).
    // Mapping: EngineIntake -> EngineA, EngineAmbient -> EngineB, Exhaust -> ExhaustL / ExhaustR (a stereo exhaust bank
    //   addresses its right channels as wave index + samples: one mono FM2 sample per channel).
    // Audio: decoded by vgmstream, written as 16-bit big endian PCM in FSB3 (bank flag 8 = big endian PCM): no XMA1
    //   encoder exists here. LOD2 (FM2's smaller banks) = the same loops at half the rate. In-game check pending.
    // Values FM2 has but the source lacks (emitter distances, a listener without a position...): FM2's median over its
    //   own ED files, never the template's.
    public sealed class Fm2SoundPort
    {
        public string SourceRoot;           // the source game folder (FM4 / FM3 / FH), or a DLC package staged as one
        public string SourceFallbackRoot;   // a staged package: its base game, for the ETs / HTs / banks / CMT the pack lacks (null: none)
        public string SourceEngineMedia;    // the source stock engine's Data_Engine.MediaName (its ET)
        public string SourceCarMedia;       // the source car's MediaName (its CMT)
        public string TargetRoot;           // the FM2 folder
        public string CarMedia;             // the FM2 car's MediaName (= its stock engine's)
        public string BackupFolder;         // copies of the files before the first write (null: none)
        public bool Write;                  // false: build everything, write nothing to the game (banks go to OutputFolder)
        public string OutputFolder;         // dry run / checks: the built banks, HT, DSP and ED as files (null: none)
        // "Use sound" (user 2026-11-25: "port sound / use sound", for the traffic cars' odd sounds): an FM2 car's MediaName;
        // its DSPTuning (the banks, HTs and DSP of each exhaust level) is copied under the car's name and no bank is built.
        // The ED (emitter positions: the car's own body) is still built from the source's CMT. null: port the source's sound.
        public string UseSoundOf;
        public List<string> Log = new List<string>();

        private sealed class Wave
        {
            public string Name;
            public short[] Pcm;
            public int Rate;
        }

        private sealed class Bank
        {
            public string Name;
            public string Exhaust, Intake, Ambient;         // source HT names
            public List<Wave> Waves = new List<Wave>();
            public XElement Ht;
        }

        private static string Decoder
        {
            get { return Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vgmstream"), "vgmstream-cli.exe"); }
        }

        private string SourceEngines { get { return EnginesOf(this.SourceRoot); } }

        private static string EnginesOf(string root)
        {
            return Path.Combine(Path.Combine(Path.Combine(Path.Combine(root, "Media"), "Audio"), "Cars"), "Engines");
        }

        // The source roots in order: the package (or game), then its base game.
        private IEnumerable<string> SourceRoots()
        {
            yield return this.SourceRoot;
            if (!string.IsNullOrEmpty(this.SourceFallbackRoot))
            {
                yield return this.SourceFallbackRoot;
            }
        }
        private string TargetCars { get { return Path.Combine(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "Audio"), "Cars"); } }

        private Dictionary<string, string> sourceHt;
        private readonly Dictionary<string, List<short[]>[]> decoded = new Dictionary<string, List<short[]>[]>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> decodedRate = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public void Run()
        {
            if (!string.IsNullOrEmpty(this.UseSoundOf))
            {
                this.RunUse();
                return;
            }
            if (!File.Exists(Decoder))
            {
                throw new FileNotFoundException("vgmstream-cli.exe is missing next to the program (vgmstream folder).");
            }
            if (this.Write && Path.GetFullPath(this.TargetRoot).TrimEnd('\\').EndsWith("_Extracted", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Never write to an _Extracted folder: use the _DEV copy.");
            }
            XElement et = null;
            foreach (string root in this.SourceRoots())
            {
                et = et ?? XmlEntry(Path.Combine(EnginesOf(root), "EngineTuning.zip"), this.SourceEngineMedia + "_ET.xml");
            }
            if (et == null)
            {
                throw new InvalidOperationException("The source has no " + this.SourceEngineMedia + "_ET.xml.");
            }
            this.sourceHt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in this.SourceRoots().Reverse())
            {
                foreach (KeyValuePair<string, string> kv in ReadTexts(Path.Combine(EnginesOf(root), "HarmonicTuning.zip")))
                {
                    this.sourceHt[kv.Key] = kv.Value;       // the package's own HTs over the base game's
                }
            }

            // exhaust levels 0-3: exhaust Ln, intake Ln, ambient L0 (FM2 has one bank per exhaust level)
            List<Bank> banks = new List<Bank>();
            Bank[] perLevel = new Bank[4];
            for (int level = 0; level < 4; level++)
            {
                string exh = UpgradeHt(et, "Exhaust", level), intake = UpgradeHt(et, "EngineIntake", level), amb = UpgradeHt(et, "EngineAmbient", 0);
                if (exh == null)
                {
                    throw new InvalidOperationException("The source ET has no exhaust HT.");
                }
                Bank same = banks.Find(delegate(Bank b) { return b.Exhaust == exh && b.Intake == intake && b.Ambient == amb; });
                if (same == null)
                {
                    same = new Bank { Exhaust = exh, Intake = intake, Ambient = amb };
                    same.Name = StripHt(exh);
                    if (banks.Exists(delegate(Bank b) { return string.Equals(b.Name, same.Name, StringComparison.OrdinalIgnoreCase); }))
                    {
                        same.Name += "_" + level.ToString(CultureInfo.InvariantCulture);
                    }
                    banks.Add(same);
                }
                perLevel[level] = same;
            }
            foreach (Bank bank in banks)
            {
                this.BuildBank(bank);
            }

            XElement dsp = this.BuildDsp(et, perLevel);
            XElement ed = this.BuildEd();

            // write
            if (this.OutputFolder != null)
            {
                Directory.CreateDirectory(this.OutputFolder);
                foreach (Bank bank in banks)
                {
                    File.WriteAllBytes(Path.Combine(this.OutputFolder, bank.Name + ".fsb"), Fsb3(bank.Waves, 1));
                    File.WriteAllBytes(Path.Combine(this.OutputFolder, bank.Name + "_LOD2.fsb"), Fsb3(bank.Waves, 2));
                    File.WriteAllBytes(Path.Combine(this.OutputFolder, bank.Name + "_HT.xml"), Xml(bank.Ht));
                }
                File.WriteAllBytes(Path.Combine(this.OutputFolder, this.CarMedia + "_DSPTuning.xml"), Xml(dsp));
                File.WriteAllBytes(Path.Combine(this.OutputFolder, this.CarMedia + "_ED.xml"), Xml(ed));
            }
            if (!this.Write)
            {
                this.Log.Add("dry run: nothing written to the game");
                return;
            }
            string engines = Path.Combine(this.TargetCars, "Engines");
            foreach (Bank bank in banks)
            {
                foreach (string lod in new string[] { "LOD1", "LOD2" })
                {
                    string path = Path.Combine(Path.Combine(Path.Combine(engines, "Soundbanks"), lod), bank.Name + ".fsb");
                    this.Backup(path, lod);
                    File.WriteAllBytes(path, Fsb3(bank.Waves, lod == "LOD1" ? 1 : 2));
                    this.Log.Add("written: " + path);
                }
            }
            Dictionary<string, byte[]> hts = new Dictionary<string, byte[]>();
            foreach (Bank bank in banks)
            {
                hts[bank.Name + "_HT.xml"] = Xml(bank.Ht);
            }
            this.PutEntries(Path.Combine(engines, "HarmonicTuning.zip"), hts);
            this.PutEntries(Path.Combine(engines, "DSPTuning.zip"), new Dictionary<string, byte[]> { { this.CarMedia + "_DSPTuning.xml", Xml(dsp) } });
            this.PutEntries(Path.Combine(this.TargetCars, "EmitterData.zip"), new Dictionary<string, byte[]> { { this.CarMedia + "_ED.xml", Xml(ed) } });
        }

        // "Use sound": the FM2 car's DSPTuning under this car's name (FM2 finds it by Data_Engine.MediaName = the car's), the ED
        // from the source's CMT as a port builds it.
        private void RunUse()
        {
            if (this.Write && Path.GetFullPath(this.TargetRoot).TrimEnd('\\').EndsWith("_Extracted", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Never write to an _Extracted folder: use the _DEV copy.");
            }
            string engines = Path.Combine(this.TargetCars, "Engines");
            XElement dsp = XmlEntry(Path.Combine(engines, "DSPTuning.zip"), this.UseSoundOf + "_DSPTuning.xml");
            if (dsp == null)
            {
                throw new InvalidOperationException("FM2 has no " + this.UseSoundOf + "_DSPTuning.xml: that car's sound cannot be used.");
            }
            dsp.SetAttributeValue("name", this.CarMedia);
            List<string> levels = new List<string>();
            XElement exhaust = dsp.Element("Exhaust");
            if (exhaust != null)
            {
                foreach (XElement upgrade in exhaust.Elements("Upgrade"))
                {
                    levels.Add(StripHt((string) upgrade.Attribute("Tuning") ?? "?"));
                }
            }
            this.Log.Add(string.Format("sound: the FM2 sound of {0} (user's choice): {1}_DSPTuning.xml = a copy of {0}'s, exhaust levels 0-3 = {2}; no bank built",
                this.UseSoundOf, this.CarMedia, string.Join(" / ", levels.ToArray())));
            XElement ed = this.BuildEd();
            if (this.OutputFolder != null)
            {
                Directory.CreateDirectory(this.OutputFolder);
                File.WriteAllBytes(Path.Combine(this.OutputFolder, this.CarMedia + "_DSPTuning.xml"), Xml(dsp));
                File.WriteAllBytes(Path.Combine(this.OutputFolder, this.CarMedia + "_ED.xml"), Xml(ed));
            }
            if (!this.Write)
            {
                this.Log.Add("dry run: nothing written to the game");
                return;
            }
            this.PutEntries(Path.Combine(engines, "DSPTuning.zip"), new Dictionary<string, byte[]> { { this.CarMedia + "_DSPTuning.xml", Xml(dsp) } });
            this.PutEntries(Path.Combine(this.TargetCars, "EmitterData.zip"), new Dictionary<string, byte[]> { { this.CarMedia + "_ED.xml", Xml(ed) } });
        }

        // ---- banks ----

        private static string StripHt(string ht)
        {
            return ht.EndsWith("_HT.xml", StringComparison.OrdinalIgnoreCase) ? ht.Substring(0, ht.Length - 7) : Path.GetFileNameWithoutExtension(ht);
        }

        // The HT of a role at an upgrade level. "NA.xml" means "no bank on this level" (FH1 ETs: Exhaust / EngineIntake L0 = NA.xml,
        // L1-L3 real, AUD_R8GT_11): the port built a bank "NA" out of it, which gave FM2 a disc read error (2026-11-27). Such a
        // level takes the nearest level with a real HT (the lower one first on a tie); none at all = no HT.
        private static string UpgradeHt(XElement et, string role, int level)
        {
            XElement upgrade = et.Element(role) == null ? null : et.Element(role).Element("Upgrade");
            if (upgrade == null)
            {
                return null;
            }
            string name = (string) upgrade.Attribute("L" + level.ToString(CultureInfo.InvariantCulture)) ?? (string) upgrade.Attribute("L0");
            if (!IsNoBank(name))
            {
                return name;
            }
            for (int distance = 1; distance < 4; distance++)
            {
                foreach (int other in new int[] { level - distance, level + distance })
                {
                    string near = (other < 0 || other > 3) ? null : (string) upgrade.Attribute("L" + other.ToString(CultureInfo.InvariantCulture));
                    if (!IsNoBank(near))
                    {
                        return near;
                    }
                }
            }
            return null;
        }

        private static bool IsNoBank(string ht)
        {
            return string.IsNullOrEmpty(ht) || string.Equals(ht, "NA.xml", StringComparison.OrdinalIgnoreCase);
        }

        private XElement SourceHt(string name)
        {
            string text;
            if (name == null || !this.sourceHt.TryGetValue(name, out text))
            {
                return null;
            }
            return Parse(text);
        }

        private void BuildBank(Bank bank)
        {
            XElement ht = new XElement("Soundbank", new XAttribute("name", bank.Name));
            Dictionary<string, int> index = new Dictionary<string, int>();
            int loops = 0;
            foreach (string[] role in new string[][]
            {
                new string[] { bank.Intake, "EngineIntake", "EngineA" },
                new string[] { bank.Ambient, "EngineAmbient", "EngineB" },
                new string[] { bank.Exhaust, "ExhaustL", "ExhaustL" },
                new string[] { bank.Exhaust, "ExhaustR", "ExhaustR" },
            })
            {
                XElement source = this.SourceHt(role[0]);
                XElement group = source == null ? null : source.Element(role[1]);
                if (group == null)
                {
                    if (role[1] != "ExhaustR")
                    {
                        this.Log.Add(string.Format("bank {0}: the source has no {1} ({2})", bank.Name, role[1], role[0] ?? "no HT"));
                    }
                    continue;
                }
                string file = (string) source.Attribute("name");
                if (!file.EndsWith(".fsb", StringComparison.OrdinalIgnoreCase))
                {
                    file += ".fsb";
                }
                List<short[]>[] channels = this.Decode(file);
                int samples = channels[0].Count;
                XElement target = new XElement(role[2]);
                foreach (XElement loop in group.Elements("Loop"))
                {
                    int wave = (int) loop.Attribute("wavebankindex");
                    int sample = wave % samples, channel = wave / samples;
                    if (channel >= channels.Length || channels[channel][sample] == null)
                    {
                        // FM4's own HTs address ExhaustR as the left index + the samples on mono banks too (GT-R SpecV
                        // 6N1_R32_Exh): the same mono sample
                        channel = 0;
                    }
                    if (channel >= 2 || channels[channel][sample] == null)
                    {
                        throw new InvalidOperationException(string.Format("{0}: wave {1} is not in {2} ({3} samples, {4} channels)", role[0], wave, file, samples, channels.Length));
                    }
                    string key = file + "|" + sample.ToString(CultureInfo.InvariantCulture) + "|" + channel.ToString(CultureInfo.InvariantCulture);
                    int at;
                    if (!index.TryGetValue(key, out at))
                    {
                        at = bank.Waves.Count;
                        index[key] = at;
                        string name = role[2].Replace("Engine", "Eng").Replace("haust", "") + "_" + ((string) loop.Attribute("rpm_sample")).PadLeft(5, '0');
                        bank.Waves.Add(new Wave { Name = name.Length > 29 ? name.Substring(0, 29) : name, Pcm = channels[channel][sample], Rate = this.decodedRate[file] });
                    }
                    XElement copy = new XElement(loop);
                    copy.SetAttributeValue("wavebankindex", at);
                    target.Add(copy);
                    loops++;
                }
                ht.Add(target);
            }
            bank.Ht = ht;
            long bytes = bank.Waves.Sum(delegate(Wave w) { return (long) w.Pcm.Length * 2; });
            this.Log.Add(string.Format(CultureInfo.InvariantCulture, "bank {0}: {1} samples, {2} loops ({3} / {4} / {5}), LOD1 {6:0.0} MB PCM at {7} Hz, LOD2 half",
                bank.Name, bank.Waves.Count, loops, StripHt(bank.Intake ?? "-"), StripHt(bank.Ambient ?? "-"), StripHt(bank.Exhaust), bytes / 1048576.0,
                string.Join("/", bank.Waves.Select(delegate(Wave w) { return w.Rate.ToString(CultureInfo.InvariantCulture); }).Distinct().ToArray())));
        }

        // Every sample of a source bank, per channel ([channel][sample], null where a sample has fewer channels).
        private List<short[]>[] Decode(string file)
        {
            List<short[]>[] result;
            if (this.decoded.TryGetValue(file, out result))
            {
                return result;
            }
            string path = null;
            foreach (string root in this.SourceRoots())
            {
                string candidate = Path.Combine(Path.Combine(Path.Combine(EnginesOf(root), "Soundbanks"), "LOD1"), file);
                if (path == null && File.Exists(candidate))
                {
                    path = candidate;
                }
            }
            if (path == null)
            {
                throw new FileNotFoundException("The source bank " + file + " is missing (Soundbanks\\LOD1).");
            }
            FsbInfo info;
            using (FileStream stream = File.OpenRead(path))
            {
                info = FsbInfo.Read(stream);
            }
            int maxChannels = Math.Max(1, info.Channels.Max());
            result = new List<short[]>[maxChannels];
            for (int c = 0; c < maxChannels; c++)
            {
                result[c] = new List<short[]>();
            }
            string folder = Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "Fm2SoundPort");
            Directory.CreateDirectory(folder);
            int rate = 0;
            // One file name per process (2026-11-27): with a fixed "decode.wav" two ports into FM2 at the same time (two
            // windows, a harness) hit "file in use" and the sound step failed.
            string wav = Path.Combine(folder, "decode_" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + ".wav");
            for (int s = 0; s < info.Samples; s++)
            {
                Run(Decoder, "-i -s " + (s + 1).ToString(CultureInfo.InvariantCulture) + " -o \"" + wav + "\" \"" + path + "\"");
                int channels, sampleRate;
                short[] pcm;
                try
                {
                    pcm = ReadWav(File.ReadAllBytes(wav), out channels, out sampleRate);
                }
                finally
                {
                    File.Delete(wav);
                }
                rate = sampleRate;
                int frames = pcm.Length / channels;
                for (int c = 0; c < maxChannels; c++)
                {
                    if (c >= channels)
                    {
                        result[c].Add(null);
                        continue;
                    }
                    short[] mono = new short[frames];
                    for (int f = 0; f < frames; f++)
                    {
                        mono[f] = pcm[(f * channels) + c];
                    }
                    result[c].Add(mono);
                }
            }
            this.decoded[file] = result;
            this.decodedRate[file] = rate;
            return result;
        }

        private static void Run(string exe, string arguments)
        {
            ProcessStartInfo start = new ProcessStartInfo(exe, arguments);
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
                if (process.ExitCode != 0)
                {
                    throw new IOException("vgmstream failed (" + arguments + "):\n" + error);
                }
            }
        }

        private static short[] ReadWav(byte[] data, out int channels, out int rate)
        {
            channels = 1;
            rate = 0;
            int p = 12;
            while (p + 8 <= data.Length)
            {
                string id = Encoding.ASCII.GetString(data, p, 4);
                int size = BitConverter.ToInt32(data, p + 4);
                if (id == "fmt ")
                {
                    channels = BitConverter.ToInt16(data, p + 10);
                    rate = BitConverter.ToInt32(data, p + 12);
                    if (BitConverter.ToInt16(data, p + 22) != 16)
                    {
                        throw new InvalidDataException("vgmstream gave a WAV that is not 16-bit");
                    }
                }
                else if (id == "data")
                {
                    int count = Math.Min(size, data.Length - (p + 8)) / 2;
                    short[] pcm = new short[count];
                    Buffer.BlockCopy(data, p + 8, pcm, 0, count * 2);
                    return pcm;
                }
                p += 8 + size + (size & 1);
            }
            throw new InvalidDataException("vgmstream gave a WAV without data");
        }

        // PCM byte order and the bank's FMOD_FSB_SOURCE_BIGENDIANPCM flag (8). 2026-11-24 in game: big endian + flag 8 =
        // only hiss in FM2 (FM2's FMOD does not read it that way). Test round: one variant per car (PcmTest in snd2).
        public bool PcmBigEndian;
        public bool PcmBigEndianFlag;

        // FSB3 version 3.1 (FM2's), one 0x50-byte record per sample, 16-bit mono PCM. step 2 = half the rate
        // (pairs averaged: FM2's LOD2 banks are its smaller ones). Lengths even (4-byte aligned data).
        private byte[] Fsb3(List<Wave> waves, int step)
        {
            List<short[]> pcm = new List<short[]>();
            foreach (Wave w in waves)
            {
                short[] src = w.Pcm;
                int n = src.Length / step;
                n -= n & 1;
                short[] dst = new short[n];
                for (int i = 0; i < n; i++)
                {
                    int sum = 0;
                    for (int k = 0; k < step; k++)
                    {
                        sum += src[(i * step) + k];
                    }
                    dst[i] = (short) (sum / step);
                }
                pcm.Add(dst);
            }
            const int Record = 0x50;
            int dataSize = pcm.Sum(delegate(short[] s) { return s.Length * 2; });
            byte[] file = new byte[24 + (Record * waves.Count) + dataSize];
            Encoding.ASCII.GetBytes("FSB3").CopyTo(file, 0);
            Put32(file, 4, waves.Count);
            Put32(file, 8, Record * waves.Count);
            Put32(file, 12, dataSize);
            Put32(file, 16, 0x00030001);
            Put32(file, 20, this.PcmBigEndianFlag ? 0x08 : 0);     // FMOD_FSB_SOURCE_BIGENDIANPCM
            int r = 24, d = 24 + (Record * waves.Count);
            for (int i = 0; i < waves.Count; i++)
            {
                short[] s = pcm[i];
                Put16(file, r, Record);
                byte[] name = Encoding.ASCII.GetBytes(waves[i].Name);
                Array.Copy(name, 0, file, r + 2, Math.Min(29, name.Length));
                Put32(file, r + 32, s.Length);
                Put32(file, r + 36, s.Length * 2);
                Put32(file, r + 40, 0);
                Put32(file, r + 44, s.Length - 1);
                Put32(file, r + 48, 0x00100130);        // ENABLEFX (as FM2's banks) | 16BITS | MONO | SIGNED
                Put32(file, r + 52, waves[i].Rate / step);
                Put16(file, r + 56, 255);
                Put16(file, r + 58, 128);
                Put16(file, r + 60, 255);
                Put16(file, r + 62, 1);
                Buffer.BlockCopy(BitConverter.GetBytes(1f), 0, file, r + 64, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(10000f), 0, file, r + 68, 4);
                for (int k = 0; k < s.Length; k++)
                {
                    file[d + (k * 2) + (this.PcmBigEndian ? 0 : 1)] = (byte) (s[k] >> 8);
                    file[d + (k * 2) + (this.PcmBigEndian ? 1 : 0)] = (byte) s[k];
                }
                r += Record;
                d += s.Length * 2;
            }
            return file;
        }

        private static void Put32(byte[] b, int o, int v)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, o, 4);
        }

        private static void Put16(byte[] b, int o, int v)
        {
            Buffer.BlockCopy(BitConverter.GetBytes((ushort) v), 0, b, o, 2);
        }

        // ---- DSPTuning ----

        private XElement Dsp(XElement et, int group, string ht)
        {
            XElement groups = et.Element("HarmonicTunings");
            XElement g = groups == null ? null : groups.Element("EmissionGroup" + group.ToString(CultureInfo.InvariantCulture));
            if (g == null)
            {
                XElement source = this.SourceHt(ht);
                g = source == null ? null : source.Element("DSP");
            }
            if (g == null)
            {
                throw new InvalidOperationException("No DSP for emission group " + group + " (" + ht + ")");
            }
            return g;
        }

        private static IEnumerable<XElement> DspBlocks(XElement dsp)
        {
            foreach (string name in new string[] { "Volume", "PEQ", "Lowpass" })
            {
                XElement block = dsp.Element(name);
                if (block == null)
                {
                    throw new InvalidOperationException("The source DSP has no " + name);
                }
                yield return new XElement(block);
            }
        }

        private XElement BuildDsp(XElement et, Bank[] perLevel)
        {
            XElement settings = et.Element("EngineSettings");
            XElement volumes = et.Element("VolumeAdjustments");
            XElement root = new XElement("Car",
                new XAttribute("name", this.CarMedia),
                new XAttribute("audio_rpm_min", (string) settings.Attribute("audio_rpm_idle")),
                new XAttribute("audio_rpm_max", (string) settings.Attribute("audio_rpm_redline")));
            if (et.Element("GlobalCarVolume") != null)
            {
                root.Add(new XElement(et.Element("GlobalCarVolume")));
            }
            XElement exhaust = new XElement("Exhaust");
            for (int level = 0; level < 4; level++)
            {
                Bank bank = perLevel[level];
                XElement upgrade = new XElement("Upgrade",
                    new XAttribute("level", level),
                    new XAttribute("Tuning", bank.Name + "_HT.xml"),
                    new XAttribute("VolumeAdjustment", Attr(volumes, "EngineVolume", "0.800000")),
                    new XAttribute("TransmissionVolume", Attr(volumes, "TransmissionVolume", "0.400000")),
                    new XAttribute("TurboVolume", Attr(volumes, "TurboVolume", "0.300000")),
                    new XAttribute("SuperChargerVolume", Attr(volumes, "SuperChargerVolume", "0.200000")),
                    new XAttribute("SuspensionVolume", Attr(volumes, "SuspensionVolume", "0.500000")));
                upgrade.Add(DspBlocks(this.Dsp(et, 2, bank.Exhaust)));
                XElement engine = new XElement("Engine");
                for (int e = 0; e < 4; e++)
                {
                    engine.Add(new XElement("Upgrade", new XAttribute("level", e),
                        new XElement("EngineA", DspBlocks(this.Dsp(et, 0, bank.Intake))),
                        new XElement("EngineB", DspBlocks(this.Dsp(et, 1, bank.Ambient)))));
                }
                upgrade.Add(engine);
                exhaust.Add(upgrade);
            }
            root.Add(exhaust);
            this.Log.Add(string.Format("DSPTuning {0}_DSPTuning.xml: exhaust levels 0-3 = {1}; DSP from the source {2}", this.CarMedia,
                string.Join(" / ", perLevel.Select(delegate(Bank b) { return b.Name; }).ToArray()),
                et.Element("HarmonicTunings") != null ? "ET (EmissionGroup0/1/2)" : "HTs"));
            return root;
        }

        private static string Attr(XElement e, string name, string fallback)
        {
            return (e == null || e.Attribute(name) == null) ? fallback : (string) e.Attribute(name);
        }

        // ---- EmitterData ----

        private XElement BuildEd()
        {
            // FM2's median of every value over its own ED files, on the structure most of them share
            Dictionary<string, string> texts = ReadTexts(Path.Combine(this.TargetCars, "EmitterData.zip"));
            Dictionary<string, List<double>> values = new Dictionary<string, List<double>>();
            Dictionary<string, int> shapes = new Dictionary<string, int>();
            Dictionary<string, XElement> shapeOf = new Dictionary<string, XElement>();
            foreach (KeyValuePair<string, string> kv in texts)
            {
                XElement doc = Parse(kv.Value);
                if (doc == null)
                {
                    continue;
                }
                string shape = Shape(doc);
                int n;
                shapes.TryGetValue(shape, out n);
                shapes[shape] = n + 1;
                shapeOf[shape] = doc;
                foreach (XElement e in doc.Descendants())
                {
                    foreach (XAttribute a in e.Attributes())
                    {
                        double v;
                        if (double.TryParse((string) a, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                        {
                            string key = PathOf(e) + "@" + a.Name.LocalName;
                            if (!values.ContainsKey(key))
                            {
                                values[key] = new List<double>();
                            }
                            values[key].Add(v);
                        }
                    }
                }
            }
            string common = shapes.OrderByDescending(delegate(KeyValuePair<string, int> kv) { return kv.Value; }).First().Key;
            XElement ed = new XElement(shapeOf[common]);
            ed.SetAttributeValue("name", this.CarMedia);
            foreach (XElement e in ed.Descendants())
            {
                foreach (XAttribute a in e.Attributes())
                {
                    List<double> list;
                    if (values.TryGetValue(PathOf(e) + "@" + a.Name.LocalName, out list))
                    {
                        list.Sort();
                        a.Value = F(list[list.Count / 2]);
                    }
                }
            }
            int fromSource = 0;
            XElement cmt = null;
            foreach (string root in this.SourceRoots())
            {
                cmt = cmt ?? XmlEntry(Path.Combine(Path.Combine(Path.Combine(Path.Combine(root, "Media"), "Audio"), "Cars"), "CarModelTuning.zip"), this.SourceCarMedia + "_CMT.xml");
            }
            if (cmt == null)
            {
                this.Log.Add("ED: the source has no " + this.SourceCarMedia + "_CMT.xml: FM2's median emitters and listeners");
                return ed;
            }
            XElement chassis = cmt.Element("Chassis");
            foreach (string[] map in new string[][] { new string[] { "EngineIntake", "EngineA" }, new string[] { "EngineAmbient", "EngineB" }, new string[] { "ExhaustL", "ExhaustL" }, new string[] { "ExhaustR", "ExhaustR" }, new string[] { "Transmission", "Transmission" } })
            {
                XElement from = chassis == null ? null : chassis.Element(map[0]);
                XElement to = ed.Element(map[1]);
                if (from == null || to == null)
                {
                    continue;
                }
                fromSource += Copy(from.Element("OriginPosition"), to.Element("OriginPosition"), "x,y,z", "x,y,z");
                fromSource += Copy(from.Element("OriginOrientation"), to.Element("OriginOrientation"), "i,j,k", "i,j,k");
                fromSource += Copy(from.Element("Cone"), to.Element("Cone"), "innerAngle,outerAngle,outerVolume,rotateX,rotateY", "innerAngle,outerAngle,outerVolume,rotateX,rotateY");
            }
            XElement basic = cmt.Element("Listener") == null ? null : cmt.Element("Listener").Element("Basic");
            List<XElement> listeners = ed.Elements("CarListener").ToList();
            string[] names = { "Front", "Follow", "Cockpit", "Default" };
            for (int i = 0; i < names.Length && i < listeners.Count && basic != null; i++)
            {
                XElement from = basic.Element(names[i]);
                XElement to = listeners[i];
                if (from == null)
                {
                    continue;
                }
                fromSource += Copy(from.Element("OriginPosition"), to.Element("OriginPosition"), "i,j,k", "x,y,z");
                fromSource += Copy(from.Element("OriginOrientation"), to.Element("OriginForward"), "x,y,z", "i,j,k");
                fromSource += Copy(from.Element("EmissionGroupVolumes"), to.Element("EmissionGroupVolumes"), "EngineIntake,EngineAmbient,ExhaustL,ExhaustR", "EngineA,EngineB,ExhaustL,ExhaustR");
                fromSource += Copy(from.Element("OtherVolumes"), to.Element("OtherVolumes"), "TireVolume,TransmissionVolume,TurboVolume,SuperChargerVolume,CarWindVolume,CollisionVolume,AmbientVolume,SuspensionVolume", "TireVolume,TransmissionVolume,TurboVolume,SuperChargerVolume,CarWindVolume,CollisionVolume,AmbientVolume,SuspensionVolume");
                foreach (string[] pan in new string[][] { new string[] { "EngineIntake", "EngineA" }, new string[] { "EngineAmbient", "EngineB" }, new string[] { "ExhaustL", "ExhaustL" }, new string[] { "ExhaustR", "ExhaustR" } })
                {
                    fromSource += Copy(from.Element("SurroundPan" + pan[0]), to.Element("SurroundPan" + pan[1]), "Pan3d,Left,Right,Center,Lfe,LeftRear,RightRear", "Pan3d,Left,Right,Center,Lfe,LeftRear,RightRear");
                }
            }
            this.Log.Add(string.Format("ED {0}_ED.xml: {1} values from the source CMT, the rest (emitter distances, listener up / rotation) FM2's median of {2} cars", this.CarMedia, fromSource, texts.Count));
            return ed;
        }

        private static int Copy(XElement from, XElement to, string fromNames, string toNames)
        {
            if (from == null || to == null)
            {
                return 0;
            }
            string[] f = fromNames.Split(','), t = toNames.Split(',');
            int n = 0;
            for (int i = 0; i < f.Length; i++)
            {
                XAttribute a = from.Attribute(f[i]);
                if (a != null)
                {
                    to.SetAttributeValue(t[i], (string) a);
                    n++;
                }
            }
            return n;
        }

        private static string Shape(XElement doc)
        {
            return string.Join("/", doc.Descendants().Select(delegate(XElement e) { return e.Name.LocalName; }).ToArray());
        }

        // Element path with sibling numbers (CarListener[2]/OtherVolumes).
        private static string PathOf(XElement e)
        {
            List<string> parts = new List<string>();
            for (XElement x = e; x.Parent != null; x = x.Parent)
            {
                int n = x.ElementsBeforeSelf(x.Name).Count();
                parts.Insert(0, x.Name.LocalName + "[" + n.ToString(CultureInfo.InvariantCulture) + "]");
            }
            return string.Join("/", parts.ToArray());
        }

        private static string F(double v)
        {
            return v.ToString("0.000000", CultureInfo.InvariantCulture);
        }

        // ---- zips and files ----

        private static XElement Parse(string text)
        {
            try
            {
                return XDocument.Parse(text.TrimStart('\uFEFF', ' ', '\r', '\n', '\t')).Root;
            }
            catch (System.Xml.XmlException)
            {
                return null;
            }
        }

        private static Dictionary<string, string> ReadTexts(string zipPath)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(zipPath))
            {
                return result;
            }
            ForzaZip zip = new ForzaZip(zipPath);
            foreach (ForzaZip.Entry entry in zip.Entries)
            {
                if (!entry.IsDirectory && entry.UncompressedSize > 0)
                {
                    result[Path.GetFileName(entry.Name)] = Encoding.UTF8.GetString(zip.Read(entry));
                }
            }
            return result;
        }

        private static XElement XmlEntry(string zipPath, string name)
        {
            if (!File.Exists(zipPath))
            {
                return null;
            }
            ForzaZip zip = new ForzaZip(zipPath);
            foreach (ForzaZip.Entry entry in zip.Entries)
            {
                if (string.Equals(Path.GetFileName(entry.Name), name, StringComparison.OrdinalIgnoreCase))
                {
                    return Parse(Encoding.UTF8.GetString(zip.Read(entry)));
                }
            }
            return null;
        }

        private static byte[] Xml(XElement root)
        {
            StringBuilder text = new StringBuilder("<?xml version=\"1.0\" ?>\r\n");
            text.Append(root.ToString().Replace("\n", "\r\n").Replace("\r\r\n", "\r\n"));
            text.Append("\r\n");
            return Encoding.ASCII.GetBytes(text.ToString());
        }

        private void Backup(string path, string sub)
        {
            if (this.BackupFolder == null || !File.Exists(path))
            {
                return;
            }
            string folder = Path.Combine(this.BackupFolder, sub);
            Directory.CreateDirectory(folder);
            string copy = Path.Combine(folder, Path.GetFileName(path));
            if (!File.Exists(copy))
            {
                File.Copy(path, copy);
            }
        }

        // Replaces (same name, any case) or adds entries, keeping the zip's own compression; zipmanifest.xml updated.
        private void PutEntries(string zipPath, Dictionary<string, byte[]> entries)
        {
            this.Backup(zipPath, "zips");
            ForzaZip zip = new ForzaZip(zipPath);
            foreach (KeyValuePair<string, byte[]> kv in entries)
            {
                ForzaZip.Entry found = zip.Entries.Find(delegate(ForzaZip.Entry e) { return string.Equals(Path.GetFileName(e.Name), kv.Key, StringComparison.OrdinalIgnoreCase); });
                if (found != null)
                {
                    zip.Replace(found, kv.Value);
                    this.Log.Add(string.Format("{0}: {1} replaced", Path.GetFileName(zipPath), found.Name));
                }
                else
                {
                    zip.Add(kv.Key, kv.Value);
                    this.Log.Add(string.Format("{0}: {1} added", Path.GetFileName(zipPath), kv.Key));
                }
            }
            zip.Save(zipPath);
            string manifest = ZipManifest.PathFor(this.TargetRoot);
            string gamePath = ZipManifest.GamePath(this.TargetRoot, zipPath);
            if (gamePath == null || !ZipManifest.Update(manifest, gamePath, zipPath))
            {
                throw new InvalidOperationException("zipmanifest.xml has no line for " + zipPath + ".");
            }
        }
    }
}
