namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    // User rule 2026-11-22 "dalla base va tolto TUTTO", data part (approved 2026-11-23): a column with no rule from the
    // source (no source column, or no rule learned) no longer keeps the FM2 template car's value. It takes FM2's typical
    // value over FM2's own cars (base game and DLC rows: keys up to GameIdLimits, never a ported car):
    //   - one value on 90 % of the rows (game constants: DragScale 1, PartsStringID 0, the DamageModDefIDs...): that value;
    //   - Data_Car: first the cars of the ported car's group (drive type | car type, 8 cars at least);
    //   - ids and text: the most common value; numbers: the most common when it holds half the rows, else the median.
    // Upgrade tables are split stock / upgrade rows (IsStock). A column whose values are all different (a link to the car's
    // own rows: TorqueCurveZeroThrottleID) keeps the row's own value: it is not the template's data but the copy's link.
    // The specific rules (CarDataToFm2.Specific.cs) come before this.
    public partial class CarDataToFm2
    {
        private const double TypicalConstantShare = 0.9;
        private const int TypicalGroupMin = 8;
        private readonly Dictionary<string, object[]> typical = new Dictionary<string, object[]>(StringComparer.OrdinalIgnoreCase);

        // The value for a column without a rule: FM2's typical one (noted), else the row's own value (noted as kept).
        private object NoRule(string table, string column, string level, object keep, string why)
        {
            object[] t = this.Typical(table, column, level);
            if (t == null)
            {
                this.Note(table, column, level, why + ": kept (a link to the car's own rows, or no FM2 rows)");
                return keep;
            }
            this.Note(table, column, level, why + ": " + (string) t[1]);
            return LikeTemplate(t[0], keep);
        }

        // { value, text } or null.
        private object[] Typical(string table, string column, string level)
        {
            string real = RealTable(table);
            string cacheKey = real + "|" + column + "|" + ((level == "stock") ? "stock" : "upg");
            object[] cached;
            if (this.typical.TryGetValue(cacheKey, out cached))
            {
                return cached;
            }
            object[] result = null;
            if (HasTable(this.Target, real))
            {
                List<string> columns = new List<string>(Columns(this.Target, real));
                if (columns.Exists(delegate(string c) { return string.Equals(c, column, StringComparison.OrdinalIgnoreCase); }))
                {
                    result = this.TypicalOf(real, column, level, columns);
                }
            }
            this.typical[cacheKey] = result;
            return result;
        }

        private object[] TypicalOf(string table, string column, string level, List<string> columns)
        {
            Predicate<string> has = delegate(string name) { return columns.Exists(delegate(string c) { return string.Equals(c, name, StringComparison.OrdinalIgnoreCase); }); };
            long maxCar = GameIdLimits.Max(Game, "Data_Car", "Id");
            long maxEngine = GameIdLimits.Max(Game, "Data_Engine", "EngineID");
            string q = SqliteDatabase.Quote(column);
            string sql;
            List<object> args = new List<object>();
            bool grouped = string.Equals(table, "Data_Car", StringComparison.OrdinalIgnoreCase);
            if (grouped)
            {
                sql = "SELECT c." + q + ", CAST(p.DrivetrainId AS TEXT) || '|' || CAST(c.CarTypeID AS TEXT) FROM Data_Car c LEFT JOIN Powertrains p ON p.Id = c.PowertrainID WHERE c.Id <= ? AND c.Id <> ?";
                args.Add(maxCar);
                args.Add(this.Car);
            }
            else
            {
                sql = "SELECT " + q + ", '' FROM " + SqliteDatabase.Quote(table) + " WHERE 1 = 1";
                if (has("Ordinal"))
                {
                    sql += " AND Ordinal <= ? AND Ordinal <> ?";
                    args.Add(maxCar);
                    args.Add(this.Car);
                }
                if (has("EngineID"))
                {
                    sql += " AND EngineID <= ?";
                    args.Add(maxEngine);
                }
                if (has("IsStock"))
                {
                    sql += " AND IsStock = ?";
                    args.Add((level == "stock") ? 1L : 0L);
                }
            }
            SqliteDatabase.Result r = this.Target.Query(sql, args.ToArray());
            List<object> all = new List<object>(), mine = new List<object>();
            foreach (object[] row in r.Rows)
            {
                all.Add(row[0]);
                if (grouped && this.fm2PortGroup != null && string.Equals(Convert.ToString(row[1], CultureInfo.InvariantCulture), this.fm2PortGroup, StringComparison.Ordinal))
                {
                    mine.Add(row[0]);
                }
            }
            if (all.Count == 0)
            {
                return null;
            }
            int distinct = Distinct(all);
            if (distinct > 0.9 * all.Count && all.Count > 20)
            {
                return null;   // every row its own value: a link, not a setting
            }
            object mode;
            double share = Mode(all, out mode);
            if (share >= TypicalConstantShare)
            {
                return new object[] { mode, string.Format(CultureInfo.InvariantCulture, "FM2 constant {0} ({1:0}% of {2} rows)", Show(mode), share * 100, all.Count) };
            }
            List<object> pool = (mine.Count >= TypicalGroupMin) ? mine : all;
            string where = (pool == mine) ? "group " + this.fm2PortGroup + ", " + mine.Count + " cars" : all.Count + " rows";
            share = Mode(pool, out mode);
            List<double> numbers = new List<double>();
            bool numeric = true;
            foreach (object o in pool)
            {
                double v;
                if (Numeric(o, out v))
                {
                    numbers.Add(v);
                }
                else
                {
                    numeric = false;
                }
            }
            if (!numeric || IsId(column) || share >= 0.5)
            {
                return new object[] { mode, string.Format(CultureInfo.InvariantCulture, "FM2 most common {0} ({1:0}%, {2})", Show(mode), share * 100, where) };
            }
            numbers.Sort();
            double median = numbers[numbers.Count / 2];
            return new object[] { median, string.Format(CultureInfo.InvariantCulture, "FM2 median {0} ({1})", Num(median), where) };
        }

        // The FM2 table behind a physics side ("List_UpgradeSpringDamper.FrontSpringDamperPhysicsID" -> its physics table).
        private static string RealTable(string table)
        {
            int dot = table.IndexOf('.');
            if (dot < 0)
            {
                return table;
            }
            string link = table.Substring(dot + 1);
            if (link.EndsWith("SpringDamperPhysicsID", StringComparison.OrdinalIgnoreCase))
            {
                return "List_SpringDamperPhysics";
            }
            if (link.EndsWith("AntiSwayPhysicsID", StringComparison.OrdinalIgnoreCase))
            {
                return "List_AntiSwayPhysics";
            }
            if (link.EndsWith("AeroPhysicsID", StringComparison.OrdinalIgnoreCase))
            {
                return "List_AeroPhysics";
            }
            return table.Substring(0, dot);
        }

        private static string Show(object o)
        {
            return (o == null || o is DBNull) ? "NULL" : ((o is string && ((string) o).Length == 0) ? "\"\"" : Convert.ToString(o, CultureInfo.InvariantCulture));
        }

        private static int Distinct(List<object> values)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (object o in values)
            {
                seen.Add(Show(o));
            }
            return seen.Count;
        }

        private static double Mode(List<object> values, out object mode)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, object> first = new Dictionary<string, object>(StringComparer.Ordinal);
            int best = 0;
            mode = null;
            foreach (object o in values)
            {
                string k = Show(o);
                int c;
                counts.TryGetValue(k, out c);
                counts[k] = ++c;
                if (!first.ContainsKey(k))
                {
                    first[k] = o;
                }
                if (c > best)
                {
                    best = c;
                    mode = first[k];
                }
            }
            return values.Count > 0 ? (double) best / values.Count : 0;
        }
    }
}
