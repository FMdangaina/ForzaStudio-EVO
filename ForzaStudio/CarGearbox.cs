namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    // What the user asks of a car's own gearbox (Duplicate Car, TODO D-10).
    public sealed class GearboxSpec
    {
        public double[] Forward;                            // the forward ratios, 1st gear first (1 to 8 of them)
        public double Reverse;                              // the reverse ratio, as FM4 stores it (negative)
        public double FinalDrive;                           // the final drive ratio of the stock level
    }

    // A copy of a car's drivetrain (Data_Drivetrain and its transmission, clutch, differential and driveline levels)
    // under a DrivetrainID of its own, with the stock gearbox set to the ratios asked for. FM4 stores the reverse in
    // GearRatio0, the forward gears in GearRatio1..8 (-1 = no such gear), the table's NumGears counts the reverse too
    // and Data_Car.NumGears the forward gears only. The upgrade levels keep their own shape: with the same number of
    // gears each gear (and the final drive) is the source level's scaled by the change of the stock one, with another
    // number they take the ratios asked for. Run inside the caller's transaction, before the integrity check.
    public static partial class CarGearbox
    {
        public static long StockDrivetrain(SqliteDatabase db, long carId)
        {
            object id = db.Scalar("SELECT DrivetrainID FROM List_UpgradeDrivetrain WHERE Ordinal = ? AND IsStock = 1 ORDER BY Level LIMIT 1", carId);
            return (id == null || id == DBNull.Value) ? -1 : Convert.ToInt64(id, CultureInfo.InvariantCulture);
        }

        // The stock gearbox of a drivetrain as a spec (what the dialog starts from), or null.
        public static GearboxSpec Read(SqliteDatabase db, long drivetrain)
        {
            SqliteDatabase.Result r = db.Query("SELECT FinalDriveRatio, NumGears, GearRatio0, GearRatio1, GearRatio2, GearRatio3, GearRatio4, GearRatio5, GearRatio6, GearRatio7, GearRatio8 FROM List_UpgradeDrivetrainTransmission WHERE DrivetrainID = ? AND IsStock = 1 ORDER BY Level LIMIT 1", drivetrain);
            if (r.Rows.Count == 0)
            {
                return null;
            }
            object[] row = r.Rows[0];
            GearboxSpec spec = new GearboxSpec();
            spec.FinalDrive = Real(row[0]);
            spec.Reverse = Real(row[2]);
            List<double> gears = new List<double>();
            for (int i = 3; i < 11; i++)
            {
                if (Real(row[i]) > 0)
                {
                    gears.Add(Real(row[i]));
                }
            }
            spec.Forward = gears.ToArray();
            return spec;
        }

        public static long Clone(SqliteDatabase db, long carId, GearboxSpec spec, List<string> plan)
        {
            return Clone(db, carId, spec, plan, ForzaGame.Motorsport4);
        }

        // game: FM4 or FM3 (FM3's drivetrain tables are FM4's: Data_Drivetrain, List_UpgradeDrivetrain*): its Id limits.
        public static long Clone(SqliteDatabase db, long carId, GearboxSpec spec, List<string> plan, ForzaGame game)
        {
            if (game != ForzaGame.Motorsport3)
            {
                game = ForzaGame.Motorsport4;
            }
            if (spec.Forward == null || spec.Forward.Length < 1 || spec.Forward.Length > 8)
            {
                throw new InvalidOperationException("A gearbox has 1 to 8 forward gears.");
            }
            foreach (double ratio in spec.Forward)
            {
                if (!(ratio > 0))
                {
                    throw new InvalidOperationException("Every forward gear ratio must be above 0.");
                }
            }
            if (!(spec.FinalDrive > 0))
            {
                throw new InvalidOperationException("The final drive ratio must be above 0.");
            }
            long old = StockDrivetrain(db, carId);
            if (old < 0)
            {
                throw new InvalidOperationException("The car has no stock drivetrain in List_UpgradeDrivetrain.");
            }
            string[] tables = { "List_UpgradeDrivetrainTransmission", "List_UpgradeDrivetrainClutch", "List_UpgradeDrivetrainDifferential", "List_UpgradeDrivetrainDriveline" };
            Dictionary<string, List<string>> schema = DbIds.Schema(db);

            // The new DrivetrainID: free, and DrivetrainID * 1000 + 0..999 above FM4's largest key and free in every part table.
            long id = DbIds.Next(db, "Data_Drivetrain", "DrivetrainID", game);
            while (true)
            {
                bool free = true;
                foreach (string table in tables)
                {
                    string key = DbIds.KeyColumn(db, table, schema[table]);
                    free = free && id * 1000 > GameIdLimits.Max(game, table, key)
                        && Convert.ToInt64(db.Scalar("SELECT count(*) FROM " + SqliteDatabase.Quote(table) + " WHERE " + SqliteDatabase.Quote(key) + " BETWEEN ? AND ?", id * 1000, (id * 1000) + 999), CultureInfo.InvariantCulture) == 0;
                }
                if (free)
                {
                    break;
                }
                id++;
            }

            SqliteDatabase.Result data = db.Query("SELECT * FROM Data_Drivetrain WHERE DrivetrainID = ?", old);
            if (data.Rows.Count == 0)
            {
                throw new InvalidOperationException("Data_Drivetrain has no drivetrain " + old + ".");
            }
            object[] dataRow = (object[]) data.Rows[0].Clone();
            dataRow[Array.FindIndex(data.Columns, delegate(string c) { return string.Equals(c, "DrivetrainID", StringComparison.OrdinalIgnoreCase); })] = id;
            Insert(db, "Data_Drivetrain", data.Columns, dataRow);

            GearboxSpec stock = Read(db, old);
            int count = spec.Forward.Length;
            bool sameCount = stock != null && stock.Forward.Length == count;
            int levels = 0;
            foreach (string table in tables)
            {
                SqliteDatabase.Result parts = db.Query("SELECT * FROM " + SqliteDatabase.Quote(table) + " WHERE DrivetrainID = ?", old);
                string key = parts.Columns[0];
                foreach (object[] part in parts.Rows)
                {
                    object[] row = (object[]) part.Clone();
                    long oldKey = Convert.ToInt64(row[0], CultureInfo.InvariantCulture);
                    row[0] = (id * 1000) + (oldKey % 1000);
                    Set(parts.Columns, row, "DrivetrainID", id);
                    if (table == "List_UpgradeDrivetrainTransmission")
                    {
                        bool isStock = Convert.ToInt64(Get(parts.Columns, row, "IsStock"), CultureInfo.InvariantCulture) == 1;
                        double[] forward = new double[count];
                        double reverse = spec.Reverse, finalDrive = spec.FinalDrive;
                        for (int i = 0; i < count; i++)
                        {
                            forward[i] = spec.Forward[i];
                        }
                        if (!isStock && sameCount)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                forward[i] = Real(Get(parts.Columns, row, "GearRatio" + (i + 1))) * spec.Forward[i] / stock.Forward[i];
                            }
                            finalDrive = Real(Get(parts.Columns, row, "FinalDriveRatio")) * spec.FinalDrive / stock.FinalDrive;
                            reverse = Real(Get(parts.Columns, row, "GearRatio0")) * spec.Reverse / stock.Reverse;
                        }
                        Set(parts.Columns, row, "NumGears", (long) (count + 1));
                        Set(parts.Columns, row, "FinalDriveRatio", Math.Round(finalDrive, 3));
                        Set(parts.Columns, row, "GearRatio0", Math.Round(reverse, 3));
                        for (int i = 1; i <= 8; i++)
                        {
                            Set(parts.Columns, row, "GearRatio" + i, (i <= count) ? Math.Round(forward[i - 1], 3) : -1.0);
                        }
                        levels++;
                    }
                    Insert(db, table, parts.Columns, row);
                }
            }

            // The car's stock drivetrain row points at the copy; the conversions and swaps stay as they are.
            int moved = db.Execute("UPDATE List_UpgradeDrivetrain SET DrivetrainID = ? WHERE Ordinal = ? AND DrivetrainID = ?", id, carId, old);
            db.Execute("UPDATE Data_Car SET NumGears = ? WHERE Id = ?", (long) count, carId);
            plan.Add(string.Format(CultureInfo.InvariantCulture, "gearbox: drivetrain {0} copied as {1} ({2} transmission levels), {3} gears, final drive {4:0.###}, reverse {5:0.###}; {6} List_UpgradeDrivetrain row of the car now point at it",
                old, id, levels, count, spec.FinalDrive, spec.Reverse, moved));
            return id;
        }

        public static string Describe(GearboxSpec spec)
        {
            List<string> ratios = new List<string>();
            foreach (double r in spec.Forward)
            {
                ratios.Add(r.ToString("0.###", CultureInfo.InvariantCulture));
            }
            return string.Format(CultureInfo.InvariantCulture, "{0} gears ({1}), reverse {2:0.###}, final drive {3:0.###}", spec.Forward.Length, string.Join(" / ", ratios.ToArray()), spec.Reverse, spec.FinalDrive);
        }

        private static object Get(string[] columns, object[] row, string name)
        {
            return row[Array.FindIndex(columns, delegate(string c) { return string.Equals(c, name, StringComparison.OrdinalIgnoreCase); })];
        }

        private static void Set(string[] columns, object[] row, string name, object value)
        {
            int index = Array.FindIndex(columns, delegate(string c) { return string.Equals(c, name, StringComparison.OrdinalIgnoreCase); });
            if (index >= 0)
            {
                row[index] = value;
            }
        }

        private static double Real(object value)
        {
            return (value == null || value == DBNull.Value) ? 0 : Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        private static void Insert(SqliteDatabase db, string table, string[] columns, object[] row)
        {
            List<string> quoted = new List<string>();
            List<string> marks = new List<string>();
            foreach (string c in columns)
            {
                quoted.Add(SqliteDatabase.Quote(c));
                marks.Add("?");
            }
            db.Execute("INSERT INTO " + SqliteDatabase.Quote(table) + " (" + string.Join(",", quoted.ToArray()) + ") VALUES (" + string.Join(",", marks.ToArray()) + ")", row);
        }
    }
}
