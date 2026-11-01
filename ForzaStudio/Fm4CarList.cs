namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using System.Text;

    // Every car of Forza Motorsport 4 and of its official DLC packages (docs/FM4/Fm4Cars.tsv, built by tools/Fm4Cars; embedded in
    // the exe), so the program does not read the game's database and the installed packages to know what FM4 already has.
    // One line per car: Id, MediaName, name, year, make, engine placement, drive, car type, weight, PI, DLC package (empty = the game).
    public static class Fm4CarList
    {
        public sealed class Entry
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
            public string Dlc;
        }

        private static List<Entry> cars;

        public static List<Entry> Cars
        {
            get
            {
                if (cars == null)
                {
                    cars = Read();
                }
                return cars;
            }
        }

        private static List<Entry> Read()
        {
            List<Entry> list = new List<Entry>();
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Fm4Cars.tsv"))
            {
                if (stream == null)
                {
                    return list;
                }
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string[] f = line.Split('\t');
                        if (f.Length < 11)
                        {
                            continue;
                        }
                        Entry e = new Entry();
                        e.Id = Convert.ToInt64(f[0], CultureInfo.InvariantCulture);
                        e.MediaName = f[1];
                        e.Name = f[2];
                        e.Year = Num(f[3]);
                        e.Make = Num(f[4]);
                        e.Placement = Num(f[5]);
                        e.Drive = Num(f[6]);
                        e.Type = Num(f[7]);
                        e.Weight = Real(f[8]);
                        e.Pi = Real(f[9]);
                        e.Dlc = f[10];
                        list.Add(e);
                    }
                }
            }
            return list;
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
    }
}
