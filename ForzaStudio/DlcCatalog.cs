namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using System.Text;

    // What the official DLC packages of every Forza hold, known in advance (docs/DlcCatalog.tsv, built by tools/DlcCatalog;
    // embedded in the exe), so the port window lists the cars of the packages installed in Xenia without opening them
    // (user 2026-11-08). Only the package's STFS header is read (Content ID); the package itself is opened and staged
    // (DlcSource.Stage) only when one of its cars is chosen. A package that is not in the catalog (renamed and changed,
    // home-made, a newer dump) is read the old way.
    //
    // The key is the header's Content ID (0x32C, 20 bytes) plus the file size: the official file name is the same
    // Content ID (hex) + "4D", but a renamed copy (4D53084D0CCF0013) still matches.
    //
    // Lines (tab separated):
    //   P  game  contentId  size  file  offer  title  note          one per package; note = why it has no cars, or ""
    //   C  contentId  Id  MediaName  name  year  make  placement  drive  type  weight  PI  traffic  build  files
    public static class DlcCatalog
    {
        public const string ResourceName = "DlcCatalog.tsv";

        public sealed class Pack
        {
            public ForzaGame Game;
            public string ContentId;
            public long Size;
            public string File;         // the official file name, as dumped
            public int Offer;
            public string Title;
            public string Note;
            public List<CarRow> Cars = new List<CarRow>();
        }

        public sealed class CarRow
        {
            public long Id;
            public string MediaName;
            public string Name;
            public long Year;
            public long Make;
            public long Placement;
            public long Drive;
            public long Type;
            public double Weight;
            public double Pi;
            public bool Traffic;        // placeholder close-up models (DlcSource.Traffic)
            public string Build;        // buildnumber.txt, "" when none (FM3)
            public bool HasFiles;       // the car's model folder is in the package
        }

        public sealed class Header
        {
            public uint TitleId;
            public string ContentId;
            public string DisplayName;
            public long Size;
        }

        private static Dictionary<string, Pack> packs;

        private static Dictionary<string, Pack> Packs
        {
            get
            {
                if (packs == null)
                {
                    packs = new Dictionary<string, Pack>(StringComparer.OrdinalIgnoreCase);
                    using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
                    {
                        if (stream != null)
                        {
                            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                            {
                                Read(reader, packs);
                            }
                        }
                    }
                }
                return packs;
            }
        }

        public static ICollection<Pack> All
        {
            get { return Packs.Values; }
        }

        // The STFS header of a package file (0x500 bytes read), or null when the file is not a package.
        public static Header ReadHeader(string file)
        {
            try
            {
                using (FileStream stream = System.IO.File.OpenRead(file))
                {
                    byte[] header = new byte[0x500];
                    if (stream.Length < 0xA000 || stream.Read(header, 0, header.Length) != header.Length)
                    {
                        return null;
                    }
                    string magic = Encoding.ASCII.GetString(header, 0, 4);
                    if (magic != "CON " && magic != "LIVE" && magic != "PIRS")
                    {
                        return null;
                    }
                    Header result = new Header();
                    result.TitleId = (uint) ((header[0x360] << 24) | (header[0x361] << 16) | (header[0x362] << 8) | header[0x363]);
                    result.ContentId = BitConverter.ToString(header, 0x32C, 20).Replace("-", "");
                    result.DisplayName = Encoding.BigEndianUnicode.GetString(header, 0x411, 0x80).TrimEnd('\0');
                    result.Size = stream.Length;
                    return result;
                }
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        // The catalog's package for this header, or null.
        public static Pack Find(Header header)
        {
            Pack pack;
            return (header != null && Packs.TryGetValue(header.ContentId, out pack) && pack.Size == header.Size) ? pack : null;
        }

        private static void Read(TextReader reader, Dictionary<string, Pack> into)
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string[] f = line.Split('\t');
                if (f[0] == "P" && f.Length >= 8)
                {
                    Pack pack = new Pack();
                    pack.Game = GameFromTag(f[1]);
                    pack.ContentId = f[2];
                    pack.Size = Num(f[3]);
                    pack.File = f[4];
                    pack.Offer = (int) Num(f[5]);
                    pack.Title = f[6];
                    pack.Note = f[7];
                    into[pack.ContentId] = pack;
                }
                else if (f[0] == "C" && f.Length >= 15)
                {
                    Pack pack;
                    if (!into.TryGetValue(f[1], out pack))
                    {
                        continue;
                    }
                    CarRow car = new CarRow();
                    car.Id = Num(f[2]);
                    car.MediaName = f[3];
                    car.Name = f[4];
                    car.Year = Num(f[5]);
                    car.Make = Num(f[6]);
                    car.Placement = Num(f[7]);
                    car.Drive = Num(f[8]);
                    car.Type = Num(f[9]);
                    car.Weight = Real(f[10]);
                    car.Pi = Real(f[11]);
                    car.Traffic = f[12] == "1";
                    car.Build = f[13];
                    car.HasFiles = f[14] == "1";
                    pack.Cars.Add(car);
                }
            }
        }

        private static ForzaGame GameFromTag(string tag)
        {
            foreach (ForzaGame game in new ForzaGame[] { ForzaGame.Motorsport2, ForzaGame.Motorsport3, ForzaGame.Motorsport4, ForzaGame.Horizon1, ForzaGame.Horizon2 })
            {
                if (GameInfo.Tag(game) == tag)
                {
                    return game;
                }
            }
            return ForzaGame.Unknown;
        }

        private static long Num(string text)
        {
            long value;
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : -1;
        }

        private static double Real(string text)
        {
            double value;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        private static string Cell(object value)
        {
            return (value == null) ? "" : Convert.ToString(value, CultureInfo.InvariantCulture).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        #region Building (tools/DlcCatalog)

        // Every package of a game's content folder: opened, staged (DlcSource.Stage, as the port window would) and its
        // cars written as catalog lines. log: one line per package.
        public static void Build(ForzaGame game, string folder, TextWriter output, TextWriter log)
        {
            uint title = InstalledDlc.TitleId(game);
            List<string> files = new List<string>(Directory.GetFiles(folder));
            files.Sort(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, bool> done = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                Header header = ReadHeader(file);
                if (header == null || header.TitleId != title || done.ContainsKey(header.ContentId))
                {
                    continue;
                }
                done[header.ContentId] = true;
                List<string> cars = new List<string>();
                string note = "";
                int offer = 0;
                try
                {
                    DlcSource source = DlcSource.OpenPackage(file, true);
                    offer = source.Offer;
                    if (source.Problem != null)
                    {
                        note = source.Problem;
                    }
                    else
                    {
                        string root = source.Stage(null);
                        cars = CarLines(header.ContentId, root, source.UsesBaseDatabase);
                        if (cars.Count > 0 && HomeMade(game, cars))
                        {
                            // A pack built with this program (its cars above the game's own Ids): it changes, so it is never in the catalog.
                            if (log != null)
                            {
                                log.WriteLine("{0} {1}: left out, a home-made pack ({2})", GameInfo.Tag(game), Path.GetFileName(file), header.DisplayName);
                            }
                            continue;
                        }
                        if (cars.Count == 0)
                        {
                            note = "no cars in its database";
                        }
                    }
                }
                catch (Exception e)
                {
                    note = e.Message;
                }
                output.WriteLine(string.Join("\t", new string[] { "P", GameInfo.Tag(game), header.ContentId, header.Size.ToString(CultureInfo.InvariantCulture), Path.GetFileName(file), offer.ToString(CultureInfo.InvariantCulture), Cell(header.DisplayName), Cell(note) }));
                foreach (string car in cars)
                {
                    output.WriteLine(car);
                }
                if (log != null)
                {
                    log.WriteLine("{0} {1} offer {2}: {3} cars{4} ({5})", GameInfo.Tag(game), Path.GetFileName(file), offer, cars.Count, (note.Length > 0) ? ", " + note : "", header.DisplayName);
                }
            }
        }

        // Every car Id above the largest of the game and its official DLC (GameIdLimits).
        private static bool HomeMade(ForzaGame game, List<string> cars)
        {
            long limit = GameIdLimits.Max(game, "Data_Car", "Id");
            if (limit <= 0)
            {
                return false;
            }
            foreach (string line in cars)
            {
                long id;
                if (!long.TryParse(line.Split('\t')[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out id) || id <= limit)
                {
                    return false;
                }
            }
            return true;
        }

        // The cars of a stage, as CarPortForm lists them (same columns), with the stage's traffic flag and build number.
        private static List<string> CarLines(string contentId, string root, bool baseDatabase)
        {
            List<string> lines = new List<string>();
            Dictionary<string, KeyValuePair<bool, string>> lazy = DlcSource.LazyCars(root);
            string db = Path.Combine(Path.Combine(Path.Combine(root, "Media"), "db"), "gamedb.slt");
            if (!System.IO.File.Exists(db))
            {
                return lines;
            }
            StringTable names = null;
            try
            {
                names = StringTable.Load(root, "EN");
            }
            catch (Exception)
            {
            }
            using (SqliteDatabase data = SqliteDatabase.FromBytes(System.IO.File.ReadAllBytes(db)))
            {
                if (!DbIds.HasTable(data, "Data_Car"))
                {
                    return lines;
                }
                bool drive = Array.Exists(data.Query("SELECT * FROM Data_Car LIMIT 1").Columns, delegate(string c) { return string.Equals(c, "DriveTypeID", StringComparison.OrdinalIgnoreCase); });
                string driveColumn = drive ? "DriveTypeID" : "(SELECT DrivetrainId FROM Powertrains WHERE Powertrains.Id = Data_Car.PowertrainID)";
                foreach (object[] row in data.Query("SELECT Id, MediaName, DisplayName, Year, MakeID, EnginePlacementID, " + driveColumn + ", CarTypeID, CurbWeight, PerformanceIndex FROM Data_Car ORDER BY Id").Rows)
                {
                    string media = row[1] as string;
                    if (media == null)
                    {
                        continue;
                    }
                    KeyValuePair<bool, string> listed;
                    bool files = lazy.TryGetValue(media, out listed);
                    if (!files && System.IO.File.Exists(Path.Combine(Path.Combine(Path.Combine(root, "Media"), "cars"), media + ".zip")))
                    {
                        // a stage written whole (an older one): the zip is there
                        files = true;
                        listed = new KeyValuePair<bool, string>(CarPort.HasPlaceholderCloseUp(Path.Combine(Path.Combine(Path.Combine(root, "Media"), "cars"), media + ".zip"), media), "");
                    }
                    if (baseDatabase && !files)
                    {
                        continue;   // a base game car, not one of the pack's
                    }
                    string name = (names != null) ? names.Lookup("Data_Car", "DisplayName", row[2] as string) : null;
                    List<string> f = new List<string> { "C", contentId, Cell(row[0]), media, Cell(string.IsNullOrEmpty(name) ? media : name) };
                    for (int i = 3; i < 10; i++)
                    {
                        f.Add(Cell(row[i]));
                    }
                    f.Add(files && listed.Key ? "1" : "0");
                    f.Add(files ? Cell(listed.Value) : "");
                    f.Add(files ? "1" : "0");
                    lines.Add(string.Join("\t", f.ToArray()));
                }
            }
            return lines;
        }

        #endregion
    }
}
