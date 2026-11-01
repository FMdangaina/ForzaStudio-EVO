namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    // The integrity rules of the game database, in one place (ROADMAP 1.3 / 1.4.2). A writer (the SQL
    // console, the car duplicate, the port) compares the database before and after what it did, and the
    // change is refused when any rule breaks. The rules are about the rows the change added, removed or
    // replaced, so a database that was already untidy is not blamed for what it had:
    //   keys         a new row's key is above the largest key FM4 and its DLC use (GameIdLimits) and the
    //                table has no key twice (the tables have no primary keys, so nothing else stops it);
    //   references   no strict relation of GameDbSchema (no dangling id in the original game) gets more
    //                dangling rows than it had: a new row points at rows that exist, nothing that is
    //                pointed at is removed or renumbered; loose relations are only reported;
    //   cars         a new Data_Car row has ModelId = Id, its Data_CarBody row (Id * 1000) and a MediaName
    //                of its own. In the other tables a car's Id is the Ordinal (or CarId) column and the
    //                references rule keeps it on an existing Data_Car.Id; Ordinal is never a counter
    //                (FM4: Ordinal = Data_Car.Id, so no MAX + 1).
    public static class DbIntegrity
    {
        public class Result
        {
            public readonly List<string> Errors = new List<string>();      // the change must not be made
            public readonly List<string> Warnings = new List<string>();    // reported, not blocking
            public readonly List<string> Summary = new List<string>();     // what the change does, per table

            public bool Ok
            {
                get { return this.Errors.Count == 0; }
            }

            // The text shown to the user before a write.
            public string Text()
            {
                StringBuilder text = new StringBuilder();
                foreach (string line in this.Errors)
                {
                    text.AppendLine("ERROR: " + line);
                }
                foreach (string line in this.Warnings)
                {
                    text.AppendLine("warning: " + line);
                }
                foreach (string line in this.Summary)
                {
                    text.AppendLine(line);
                }
                return text.ToString().TrimEnd();
            }
        }

        private class TableChange
        {
            public string Table;
            public string Key;                                  // the key column, null when the table has none
            public List<object[]> Added = new List<object[]>();
            public List<object[]> Removed = new List<object[]>();
            public List<long> NewKeys = new List<long>();       // keys that were not in the table before
        }

        // before / after: the same database before and after the change. tables: the tables to compare, or
        // null for every table (a change that can reach several, like a car copy).
        public static Result Compare(SqliteDatabase before, SqliteDatabase after, ForzaGame game, ICollection<string> tables)
        {
            return Compare(before, after, game, tables, true);
        }

        // referencesBlock false: a strict relation that gets dangling rows and a new key at or below FM4's
        // largest are warnings (the car port: rows copied from another game keep their Ids and are checked in
        // game); a key twice and a broken car stay errors.
        public static Result Compare(SqliteDatabase before, SqliteDatabase after, ForzaGame game, ICollection<string> tables, bool referencesBlock)
        {
            Result result = new Result();
            Dictionary<string, List<string>> schema = DbIds.Schema(after);
            Dictionary<string, List<string>> old = DbIds.Schema(before);
            List<TableChange> changes = new List<TableChange>();
            List<string> names = new List<string>();
            foreach (string name in schema.Keys)
            {
                if (name.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (tables != null && !Contains(tables, name))
                {
                    continue;
                }
                names.Add(name);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string name in old.Keys)
            {
                if (!schema.ContainsKey(name) && !name.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
                {
                    result.Errors.Add("the table " + name + " is gone");
                }
            }
            foreach (string name in names)
            {
                if (!old.ContainsKey(name))
                {
                    result.Errors.Add("the table " + name + " did not exist: tables are not created");
                    continue;
                }
                TableChange change = Diff(before, after, name, schema[name]);
                if (change.Added.Count > 0 || change.Removed.Count > 0)
                {
                    changes.Add(change);
                }
            }

            foreach (TableChange change in changes)
            {
                Keys(result, before, after, change, game, referencesBlock);
                result.Summary.Add(Describe(change));
            }
            References(result, before, after, changes, referencesBlock);
            Cars(result, after, changes);
            return result;
        }

        // A DLC package's merge database (the db/patch/<offer>000_merge.slt file) as the game applies it: its
        // rows are added to the game database, after the merges of the packages installed before it.
        // Returns the rules checked on the result against the game plus those other packages.
        public static Result CheckMerge(byte[] gameDb, ICollection<string> otherMerges, byte[] merge, ForzaGame game)
        {
            Result result = new Result();
            using (SqliteDatabase before = SqliteDatabase.FromBytes(gameDb))
            {
                foreach (string other in otherMerges)
                {
                    try
                    {
                        using (SqliteDatabase patch = SqliteDatabase.FromBytes(System.IO.File.ReadAllBytes(other)))
                        {
                            AddRows(before, patch, true);
                        }
                    }
                    catch (Exception)
                    {
                        // an installed package that cannot be read does not decide for this one
                    }
                }
                using (SqliteDatabase after = SqliteDatabase.FromBytes(before.ToBytes()))
                using (SqliteDatabase patch = SqliteDatabase.FromBytes(merge))
                {
                    try
                    {
                        AddRows(after, patch);
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add("the package's database does not merge into the game's: " + ex.Message);
                        return result;
                    }
                    Result check = Compare(before, after, game, null);
                    result.Errors.AddRange(check.Errors);
                    result.Warnings.AddRange(check.Warnings);
                    result.Summary.AddRange(check.Summary);
                }
            }
            return result;
        }

        // The rows of every table of a merge database, added to the same table of db (columns by name).
        internal static void AddRows(SqliteDatabase db, SqliteDatabase merge)
        {
            AddRows(db, merge, false);
        }

        // skipDuplicates: a row whose key the database already has is left out, not an error (an installed package that
        // shares rows with the game: the rest of its rows still count, a half-added package hid its makes from the check).
        internal static void AddRows(SqliteDatabase db, SqliteDatabase merge, bool skipDuplicates)
        {
            Dictionary<string, List<string>> own = DbIds.Schema(db);
            foreach (KeyValuePair<string, List<string>> table in DbIds.Schema(merge))
            {
                List<string> columns;
                if (table.Key.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase) || !own.TryGetValue(table.Key, out columns))
                {
                    continue;
                }
                List<string> shared = table.Value.FindAll(delegate(string c) { return columns.Exists(delegate(string x) { return string.Equals(x, c, StringComparison.OrdinalIgnoreCase); }); });
                if (shared.Count == 0)
                {
                    continue;
                }
                string names = string.Join(", ", shared.ConvertAll<string>(delegate(string c) { return SqliteDatabase.Quote(c); }).ToArray());
                string marks = string.Join(", ", shared.ConvertAll<string>(delegate(string c) { return "?"; }).ToArray());
                foreach (object[] row in merge.Query("SELECT " + names + " FROM " + SqliteDatabase.Quote(table.Key)).Rows)
                {
                    db.Execute((skipDuplicates ? "INSERT OR IGNORE INTO " : "INSERT INTO ") + SqliteDatabase.Quote(table.Key) + " (" + names + ") VALUES (" + marks + ")", row);
                }
            }
        }

        private static bool Contains(ICollection<string> list, string name)
        {
            foreach (string item in list)
            {
                if (string.Equals(item, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        // The rows of a table that are in one copy and not in the other (a row counted as many times as it
        // is repeated). An UPDATE is a removed row and an added one.
        private static TableChange Diff(SqliteDatabase before, SqliteDatabase after, string table, List<string> columns)
        {
            TableChange change = new TableChange();
            change.Table = table;
            change.Key = DbIds.KeyColumn(after, table, columns);
            string select = "SELECT * FROM " + SqliteDatabase.Quote(table);
            SqliteDatabase.Result was = before.Query(select);
            SqliteDatabase.Result now = after.Query(select);
            Dictionary<string, int> counts = new Dictionary<string, int>();
            foreach (object[] row in was.Rows)
            {
                string text = RowText(row);
                int n;
                counts.TryGetValue(text, out n);
                counts[text] = n + 1;
            }
            foreach (object[] row in now.Rows)
            {
                string text = RowText(row);
                int n;
                if (counts.TryGetValue(text, out n) && n > 0)
                {
                    counts[text] = n - 1;
                }
                else
                {
                    change.Added.Add(row);
                }
            }
            Dictionary<string, int> left = counts;
            foreach (object[] row in was.Rows)
            {
                string text = RowText(row);
                int n;
                if (left.TryGetValue(text, out n) && n > 0)
                {
                    left[text] = n - 1;
                    change.Removed.Add(row);
                }
            }
            return change;
        }

        // A row as text, to tell the rows of two copies apart.
        public static string RowText(object[] row)
        {
            StringBuilder text = new StringBuilder();
            foreach (object value in row)
            {
                if (value == null || value == DBNull.Value)
                {
                    text.Append("\u0001null|");
                }
                else if (value is byte[])
                {
                    text.Append(Convert.ToBase64String((byte[]) value)).Append('|');
                }
                else
                {
                    text.Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append('|');
                }
            }
            return text.ToString();
        }

        private static int KeyIndex(SqliteDatabase db, TableChange change)
        {
            if (change.Key == null)
            {
                return -1;
            }
            int index = 0;
            foreach (object[] column in db.Query("PRAGMA table_info(" + SqliteDatabase.Quote(change.Table) + ")").Rows)
            {
                if (string.Equals((string) column[1], change.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
                index++;
            }
            return -1;
        }

        // Keys: new keys above FM4's largest, and no key twice.
        private static void Keys(Result result, SqliteDatabase before, SqliteDatabase after, TableChange change, ForzaGame game, bool block)
        {
            int index = KeyIndex(after, change);
            if (index < 0)
            {
                return;
            }
            Dictionary<long, int> was = new Dictionary<long, int>();
            foreach (object[] row in before.Query("SELECT " + SqliteDatabase.Quote(change.Key) + " FROM " + SqliteDatabase.Quote(change.Table)).Rows)
            {
                if (row[0] is long)
                {
                    int n;
                    was.TryGetValue((long) row[0], out n);
                    was[(long) row[0]] = n + 1;
                }
            }
            Dictionary<long, int> now = new Dictionary<long, int>();
            foreach (object[] row in after.Query("SELECT " + SqliteDatabase.Quote(change.Key) + " FROM " + SqliteDatabase.Quote(change.Table)).Rows)
            {
                if (row[0] is long)
                {
                    int n;
                    now.TryGetValue((long) row[0], out n);
                    now[(long) row[0]] = n + 1;
                }
            }
            // A column the table already repeats (StartGridPositions.id, Combo_Colors.Ordinal...) is not its key.
            bool unique = true;
            foreach (KeyValuePair<long, int> pair in was)
            {
                unique = unique && pair.Value == 1;
            }
            long limit = GameIdLimits.Max(game, change.Table, change.Key);
            int reported = 0;
            foreach (KeyValuePair<long, int> pair in now)
            {
                int had;
                was.TryGetValue(pair.Key, out had);
                if (had == 0)
                {
                    change.NewKeys.Add(pair.Key);
                    if (limit >= 0 && !IdFromOffer(change.Table, change.Key) && pair.Key <= limit && reported++ < 5)
                    {
                        (block ? result.Errors : result.Warnings).Add(string.Format(CultureInfo.InvariantCulture,
                            "{0}.{1} = {2} is not above FM4's largest ({3}, base game and every DLC): use {4} or more (the next free one is {5})",
                            change.Table, change.Key, pair.Key, limit, limit + 1, DbIds.Next(after, change.Table, change.Key, game)));
                    }
                }
                if (unique && pair.Value > had && pair.Value > 1)
                {
                    result.Errors.Add(string.Format(CultureInfo.InvariantCulture, "{0}.{1} = {2} would be in the table {3} times (a key is unique)", change.Table, change.Key, pair.Key, pair.Value));
                }
            }
            change.NewKeys.Sort();
        }

        // ContentOffers.OfferId is built from the pack's offer number (0x4D530910FF000000 + the number, the
        // number being the pack's PubOfferingID), so the rule of the largest Id belongs to PubOfferingID.
        private static bool IdFromOffer(string table, string key)
        {
            return string.Equals(table, "ContentOffers", StringComparison.OrdinalIgnoreCase) && string.Equals(key, "OfferId", StringComparison.OrdinalIgnoreCase);
        }

        // References: the dangling rows of every strict relation that touches a changed table must not
        // grow; the loose ones are only reported.
        private static void References(Result result, SqliteDatabase before, SqliteDatabase after, List<TableChange> changes, bool block)
        {
            Dictionary<string, bool> touched = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (TableChange change in changes)
            {
                touched[change.Table] = true;
            }
            if (touched.Count == 0)
            {
                return;
            }
            foreach (GameDbSchema.Relation relation in GameDbSchema.Relations)
            {
                if (!touched.ContainsKey(relation.Table) && !touched.ContainsKey(relation.Target))
                {
                    continue;
                }
                if (!DbIds.HasTable(after, relation.Table) || !DbIds.HasTable(after, relation.Target) || !DbIds.HasTable(before, relation.Table) || !DbIds.HasTable(before, relation.Target))
                {
                    continue;
                }
                long was;
                long now;
                try
                {
                    string where = GameDatabase.DanglingWhere(relation);
                    string count = "SELECT count(*) FROM " + SqliteDatabase.Quote(relation.Table) + " WHERE " + where;
                    was = Convert.ToInt64(before.Scalar(count), CultureInfo.InvariantCulture);
                    now = Convert.ToInt64(after.Scalar(count), CultureInfo.InvariantCulture);
                }
                catch (InvalidOperationException)
                {
                    continue;   // a column the relation names is not in this database
                }
                if (now <= was)
                {
                    continue;
                }
                string text = string.Format(CultureInfo.InvariantCulture, "{0}: {1} {2} {3}.{4} ids that do not exist{5}",
                    relation, now - was, (now - was) == 1 ? "row points at" : "rows point at", relation.Target, relation.TargetColumn, Sample(after, relation));
                if (relation.Strict && block)
                {
                    result.Errors.Add(text);
                }
                else
                {
                    result.Warnings.Add(text + (relation.Strict ? " (the original game has none: look at it in game)" : " (this relation already dangles in the original game)"));
                }
            }
        }

        private static string Sample(SqliteDatabase db, GameDbSchema.Relation relation)
        {
            try
            {
                List<string> values = new List<string>();
                string columns = string.Join(", ", Array.ConvertAll<string, string>(relation.Columns, delegate(string c) { return SqliteDatabase.Quote(c); }));
                foreach (object[] row in db.Query("SELECT DISTINCT " + columns + " FROM " + SqliteDatabase.Quote(relation.Table) + " WHERE " + GameDatabase.DanglingWhere(relation) + " LIMIT 4").Rows)
                {
                    values.Add(string.Join("/", Array.ConvertAll<object, string>(row, delegate(object o) { return Convert.ToString(o, CultureInfo.InvariantCulture); })));
                }
                return values.Count == 0 ? "" : " (" + string.Join(", ", values.ToArray()) + ")";
            }
            catch (InvalidOperationException)
            {
                return "";
            }
        }

        // Cars: a Data_Car row that was added or rewritten is a whole car.
        private static void Cars(Result result, SqliteDatabase after, List<TableChange> changes)
        {
            TableChange cars = changes.Find(delegate(TableChange c) { return string.Equals(c.Table, "Data_Car", StringComparison.OrdinalIgnoreCase); });
            if (cars == null || cars.Added.Count == 0 || cars.Key == null)
            {
                return;
            }
            List<string> columns = DbIds.Schema(after)["Data_Car"];
            int key = columns.FindIndex(delegate(string c) { return string.Equals(c, cars.Key, StringComparison.OrdinalIgnoreCase); });
            bool model = columns.Exists(delegate(string c) { return string.Equals(c, "ModelId", StringComparison.OrdinalIgnoreCase); });
            bool body = DbIds.HasTable(after, "Data_CarBody");
            foreach (object[] row in cars.Added)
            {
                if (key < 0 || !(row[key] is long))
                {
                    continue;
                }
                long id = (long) row[key];
                object name = after.Scalar("SELECT MediaName FROM Data_Car WHERE Id = ?", id);
                if (model)
                {
                    object modelId = after.Scalar("SELECT ModelId FROM Data_Car WHERE Id = ?", id);
                    if (modelId != null && Convert.ToInt64(modelId, CultureInfo.InvariantCulture) != id)
                    {
                        result.Errors.Add(string.Format(CultureInfo.InvariantCulture, "Data_Car.ModelId = {0}, not the car's Id {1} (FM4: ModelId = Id on every car)", modelId, id));
                    }
                }
                if (body && Convert.ToInt64(after.Scalar("SELECT count(*) FROM Data_CarBody WHERE Id = ?", id * 1000), CultureInfo.InvariantCulture) == 0)
                {
                    result.Errors.Add(string.Format(CultureInfo.InvariantCulture, "Data_CarBody: the car {0} has no row {1} (car * 1000)", id, id * 1000));
                }
                if (name is string && Convert.ToInt64(after.Scalar("SELECT count(*) FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", name), CultureInfo.InvariantCulture) != 1)
                {
                    result.Errors.Add(string.Format(CultureInfo.InvariantCulture, "Data_Car.MediaName = {0} is on more than one car", name));
                }
            }
        }

        // "Data_Car: +1 row (Id 1638), -0" for the summary shown before a write.
        private static string Describe(TableChange change)
        {
            StringBuilder text = new StringBuilder(change.Table).Append(": ");
            int replaced = 0;
            if (change.Key != null && change.Added.Count > 0 && change.Removed.Count > 0)
            {
                // The rows an UPDATE rewrote keep their key.
                replaced = Math.Min(change.Added.Count - change.NewKeys.Count, change.Removed.Count);
                replaced = Math.Max(0, replaced);
            }
            int added = change.Added.Count - replaced;
            int removed = change.Removed.Count - replaced;
            List<string> parts = new List<string>();
            if (added > 0)
            {
                string keys = "";
                if (change.NewKeys.Count > 0)
                {
                    keys = (change.NewKeys.Count <= 6)
                        ? " (" + change.Key + " " + string.Join(", ", change.NewKeys.ConvertAll<string>(delegate(long k) { return k.ToString(CultureInfo.InvariantCulture); }).ToArray()) + ")"
                        : " (" + change.Key + " " + change.NewKeys[0].ToString(CultureInfo.InvariantCulture) + " to " + change.NewKeys[change.NewKeys.Count - 1].ToString(CultureInfo.InvariantCulture) + ")";
                }
                parts.Add("+" + added + (added == 1 ? " row" : " rows") + keys);
            }
            if (removed > 0)
            {
                parts.Add("-" + removed + (removed == 1 ? " row" : " rows"));
            }
            if (replaced > 0)
            {
                parts.Add(replaced + (replaced == 1 ? " row" : " rows") + " changed");
            }
            return text.Append(string.Join(", ", parts.ToArray())).ToString();
        }
    }
}
