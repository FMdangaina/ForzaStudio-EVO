namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    // A DLC package's merge database laid over its base game's database, as the game mounts it (a pack's rows REPLACE the
    // game's rows with the same key: FM4 checked in game 2026-11-11). A port whose data are learned from the cars both games
    // share (CarDataToFm2) needs the whole source game, not the pack's rows alone (user 2026-11-24: FH1 GTX1, pack offer
    // 1900, into FM2 "unione db base + pacchetto").
    // Per table of the merge that the base has: the base rows whose first column holds a value of the merge rows' first
    // column go, then the merge rows go in (the columns both have). Merge tables have no primary key: the first column is
    // the key (Id, Ordinal, CarId...), as everywhere in the merge databases.
    public static class SourceDatabaseMerge
    {
        public static SqliteDatabase Merge(byte[] baseDatabase, byte[] mergeDatabase, List<string> report)
        {
            SqliteDatabase result = SqliteDatabase.FromBytes(baseDatabase);
            SqliteDatabase merge = SqliteDatabase.FromBytes(mergeDatabase);
            foreach (object[] t in merge.Query("SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name").Rows)
            {
                string table = Convert.ToString(t[0], CultureInfo.InvariantCulture);
                if (result.Scalar("SELECT name FROM sqlite_master WHERE type = 'table' AND name = ?", table) == null)
                {
                    report.Add(table + ": not in the base database, left out");
                    continue;
                }
                List<string> baseColumns = Columns(result, table);
                List<string> columns = Columns(merge, table).FindAll(delegate(string c) { return baseColumns.Exists(delegate(string b) { return string.Equals(b, c, StringComparison.OrdinalIgnoreCase); }); });
                if (columns.Count == 0)
                {
                    continue;
                }
                string list = string.Join(", ", columns.ConvertAll(delegate(string c) { return SqliteDatabase.Quote(c); }).ToArray());
                SqliteDatabase.Result rows = merge.Query("SELECT " + list + " FROM " + SqliteDatabase.Quote(table));
                if (rows.Rows.Count == 0)
                {
                    continue;
                }
                string key = SqliteDatabase.Quote(Columns(merge, table)[0]);
                HashSet<string> keys = new HashSet<string>();
                int keyIndex = columns.FindIndex(delegate(string c) { return SqliteDatabase.Quote(c) == key; });
                int replaced = 0;
                foreach (object[] r in rows.Rows)
                {
                    if (keyIndex >= 0 && keys.Add(Convert.ToString(r[keyIndex], CultureInfo.InvariantCulture)))
                    {
                        replaced += result.Execute("DELETE FROM " + SqliteDatabase.Quote(table) + " WHERE " + key + " = ?", r[keyIndex]);
                    }
                }
                string marks = string.Join(", ", columns.ConvertAll(delegate(string c) { return "?"; }).ToArray());
                foreach (object[] r in rows.Rows)
                {
                    result.Execute("INSERT INTO " + SqliteDatabase.Quote(table) + " (" + list + ") VALUES (" + marks + ")", r);
                }
                report.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1} rows from the pack, {2} base rows replaced", table, rows.Rows.Count, replaced));
            }
            return result;
        }

        private static List<string> Columns(SqliteDatabase db, string table)
        {
            List<string> columns = new List<string>();
            foreach (object[] r in db.Query("PRAGMA table_info(" + SqliteDatabase.Quote(table) + ")").Rows)
            {
                columns.Add(Convert.ToString(r[1], CultureInfo.InvariantCulture));
            }
            return columns;
        }
    }
}
