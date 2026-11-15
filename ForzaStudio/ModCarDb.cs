namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    // The database of a car whose Save writes a mod pack (ModPack), user 2026-11-11: "all the car's tables in the merge;
    // control of the car starting from the open carbin". The game's gamedb.slt is never written for such a car.
    //
    //   reference  the game's gamedb.slt + the merges of the original package the car comes from (an official car pack's
    //              rows are in its merge; FM4 disc 2's are in gamedb.slt itself);
    //   working    the reference + the car's mod pack merge: the copy the user edits (entry EntryName of the car copy zip,
    //              File > Database of This Car), saved with the car;
    //   merge      what the mod pack carries (BuildMerge): EVERY row of the car (Owned) plus every other row the user added
    //              or changed, with the mod's offer.
    //
    // Checked in game 2026-11-11 (R34, offer 411): a merge row whose key the game already has REPLACES the game's row
    // (Data_Car 445 Year 1977, BaseCost 77777), a new row is added (Combo_Colors 445006, a 6th paint); no crash.
    // Tables without a key (CarPartPositions, CarInvalidDefaultParts, KinectSplitscreenCars, StripeColors...) are
    // different: a row there cannot replace the game's one, so only rows the game lacks go in, and a changed row is
    // reported (the game would keep the old row beside the new one; not checked in game yet).
    public static class ModCarDb
    {
        // The working copy inside the car copy zip (never staged as a car file).
        public const string EntryName = "_car_database.slt";

        // The reference: the game's database and the original package's merges.
        public static byte[] Reference(string gameRoot, string packageFile)
        {
            string db = (gameRoot == null) ? null : Path.Combine(Path.Combine(Path.Combine(gameRoot, "Media"), "db"), "gamedb.slt");
            if (db == null || !File.Exists(db))
            {
                throw new InvalidOperationException("The game's gamedb.slt was not found" + ((db != null) ? " (" + db + ")" : "") + ".");
            }
            byte[] game = File.ReadAllBytes(db);
            List<byte[]> merges = (packageFile == null) ? new List<byte[]>() : PackageMerges(packageFile);
            return (merges.Count == 0) ? game : Apply(game, merges);
        }

        // The working copy: the reference with the mod pack's merge (null: none yet).
        public static byte[] Working(byte[] reference, byte[] modMerge)
        {
            return (modMerge == null) ? reference : Apply(reference, new List<byte[]> { modMerge });
        }

        // The merges of a package (media/db/patch/*_merge.slt in its DLCZips zips and loose folders), by name.
        public static List<byte[]> PackageMerges(string packageFile)
        {
            SortedDictionary<string, byte[]> found = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            StfsReader package = StfsReader.Open(packageFile);
            const string Zips = "Media/DLCZips/";
            foreach (string file in package.Files())
            {
                string name = file.Replace('\\', '/');
                if (!name.StartsWith(Zips, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string rest = name.Substring(Zips.Length);
                if (rest.IndexOf('/') < 0)
                {
                    if (rest.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && rest.IndexOf("_pri_", StringComparison.OrdinalIgnoreCase) > 0
                        && !rest.StartsWith("StringTables", StringComparison.OrdinalIgnoreCase))
                    {
                        string inside = file;
                        string mount = "stfs:modcardb|" + packageFile + "|" + file;
                        ForzaZip.Mount(mount, delegate { return package.OpenFile(inside); });
                        ForzaZip zip = new ForzaZip(mount);
                        foreach (ForzaZip.Entry entry in zip.Entries)
                        {
                            string entryName = entry.Name.Replace('\\', '/');
                            if (!entry.IsDirectory && IsMergeName(entryName))
                            {
                                found[Path.GetFileName(entryName)] = zip.Read(entry);
                            }
                        }
                    }
                }
                else if (IsMergeName(rest))
                {
                    found[Path.GetFileName(rest)] = package.ReadFile(file);
                }
            }
            return new List<byte[]>(found.Values);
        }

        private static bool IsMergeName(string name)
        {
            return name.IndexOf("db/patch/", StringComparison.OrdinalIgnoreCase) >= 0 && name.EndsWith("_merge.slt", StringComparison.OrdinalIgnoreCase);
        }

        // A database with merges applied the way the game does: a row whose key is there replaces it, a new row is added.
        // Offer rows are left out (the copy is for editing the car; the mod's offer is written by BuildMerge).
        public static byte[] Apply(byte[] game, List<byte[]> merges)
        {
            using (SqliteDatabase db = SqliteDatabase.FromBytes(game))
            {
                Dictionary<string, List<string>> own = DbIds.Schema(db);
                db.Execute("BEGIN");
                foreach (byte[] bytes in merges)
                {
                    using (SqliteDatabase merge = SqliteDatabase.FromBytes(bytes))
                    {
                        foreach (KeyValuePair<string, List<string>> table in DbIds.Schema(merge))
                        {
                            List<string> columns;
                            if (Skipped(table.Key) || !own.TryGetValue(table.Key, out columns))
                            {
                                continue;
                            }
                            List<string> shared = table.Value.FindAll(delegate(string c) { return columns.Exists(delegate(string x) { return string.Equals(x, c, StringComparison.OrdinalIgnoreCase); }); });
                            if (shared.Count == 0)
                            {
                                continue;
                            }
                            bool keyed = PrimaryKey(db, table.Key) != null;
                            string names = string.Join(", ", shared.ConvertAll<string>(delegate(string c) { return SqliteDatabase.Quote(c); }).ToArray());
                            string marks = string.Join(", ", shared.ConvertAll<string>(delegate(string c) { return "?"; }).ToArray());
                            string same = string.Join(" AND ", shared.ConvertAll<string>(delegate(string c) { return SqliteDatabase.Quote(c) + " IS ?"; }).ToArray());
                            foreach (object[] row in merge.Query("SELECT " + names + " FROM " + SqliteDatabase.Quote(table.Key)).Rows)
                            {
                                if (keyed)
                                {
                                    db.Execute("INSERT OR REPLACE INTO " + SqliteDatabase.Quote(table.Key) + " (" + names + ") VALUES (" + marks + ")", row);
                                }
                                else if (Convert.ToInt64(db.Scalar("SELECT count(*) FROM " + SqliteDatabase.Quote(table.Key) + " WHERE " + same, row)) == 0)
                                {
                                    db.Execute("INSERT INTO " + SqliteDatabase.Quote(table.Key) + " (" + names + ") VALUES (" + marks + ")", row);
                                }
                            }
                        }
                    }
                }
                db.Execute("COMMIT");
                return db.ToBytes();
            }
        }

        // Tables a car's mod pack never carries: the offers (the mod's own are written apart), the new-profile seeds.
        private static bool Skipped(string table)
        {
            return table.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase) || table.StartsWith("ContentOffers", StringComparison.OrdinalIgnoreCase)
                || table.StartsWith("NewProfile_", StringComparison.OrdinalIgnoreCase) || string.Equals(table, "MultiDiscContentPacks", StringComparison.OrdinalIgnoreCase);
        }

        // The column of the table's PRIMARY KEY, or null (28 FM4 tables and every merge have none).
        public static string PrimaryKey(SqliteDatabase db, string table)
        {
            string key = null;
            foreach (object[] column in db.Query("PRAGMA table_info(" + SqliteDatabase.Quote(table) + ")").Rows)
            {
                if (column[5] is long && (long) column[5] > 0)
                {
                    if (key != null)
                    {
                        return null;   // composite
                    }
                    key = (string) column[1];
                }
            }
            return key;
        }

        // The car's rows: table -> SQL condition (with the values) that picks them. As DbIds.RenumberCar sees a car:
        // Data_Car, every row whose car column is the car, the body part rows (List_UpgradeCarBody*, Id = car * 1000 + n),
        // and, following the relations, every row those point at in the car's range car * 1000 .. car * 1000 + 999
        // (Data_CarBody, physics, aero...). Shared rows (engines, wheels of other cars) are not the car's.
        public static Dictionary<string, List<KeyValuePair<string, object>>> Owned(SqliteDatabase db, long car)
        {
            return Owned(db, car, ForzaGame.Motorsport4);
        }

        // game: whose car columns and relation map (FM2 / FM3 have their own, ST-02).
        public static Dictionary<string, List<KeyValuePair<string, object>>> Owned(SqliteDatabase db, long car, ForzaGame game)
        {
            Dictionary<string, List<string>> schema = DbIds.Schema(db);
            Dictionary<string, List<KeyValuePair<string, object>>> owned = new Dictionary<string, List<KeyValuePair<string, object>>>(StringComparer.OrdinalIgnoreCase);
            long low = car * 1000;
            long high = low + 999;
            Add(owned, "Data_Car", "Id = ?", car);
            foreach (string[] column in DbIds.CarColumns(schema, game))
            {
                if (!Skipped(column[0]))
                {
                    Add(owned, column[0], SqliteDatabase.Quote(column[1]) + " = ?", car);
                }
            }
            foreach (KeyValuePair<string, List<string>> table in schema)
            {
                if (table.Key.StartsWith("List_UpgradeCarBody", StringComparison.Ordinal) && table.Value.Exists(delegate(string c) { return string.Equals(c, "Id", StringComparison.OrdinalIgnoreCase); }))
                {
                    Add(owned, table.Key, "Id BETWEEN ? AND ?", new object[] { low, high });
                }
            }
            // Rows in the car's range that owned rows point at, until nothing new is found.
            List<string> done = new List<string>();
            bool more = true;
            while (more)
            {
                more = false;
                foreach (string owner in new List<string>(owned.Keys))
                {
                    if (done.Contains(owner))
                    {
                        continue;
                    }
                    done.Add(owner);
                    foreach (GameDbSchema.Relation relation in GameDbSchema.From(game, owner))
                    {
                        List<string> targetColumns;
                        if (relation.IsComposite || Skipped(relation.Target) || !schema.ContainsKey(owner) || !schema[owner].Exists(delegate(string c) { return string.Equals(c, relation.Column, StringComparison.OrdinalIgnoreCase); })
                            || !schema.TryGetValue(relation.Target, out targetColumns) || !targetColumns.Exists(delegate(string c) { return string.Equals(c, relation.TargetColumn, StringComparison.OrdinalIgnoreCase); }))
                        {
                            continue;
                        }
                        foreach (KeyValuePair<string, object> where in owned[owner].ToArray())
                        {
                            object[] args = (where.Value as object[]) ?? new object[] { where.Value };
                            string c = SqliteDatabase.Quote(relation.Column);
                            object[] values = new object[args.Length + 2];
                            args.CopyTo(values, 0);
                            values[args.Length] = low;
                            values[args.Length + 1] = high;
                            foreach (object[] row in db.Query("SELECT DISTINCT " + c + " FROM " + SqliteDatabase.Quote(owner) + " WHERE (" + where.Key + ") AND " + c + " BETWEEN ? AND ?", values).Rows)
                            {
                                if (Add(owned, relation.Target, SqliteDatabase.Quote(relation.TargetColumn) + " = ?", row[0]))
                                {
                                    more = true;
                                    done.Remove(relation.Target);
                                }
                            }
                        }
                    }
                }
            }
            return owned;
        }

        private static bool Add(Dictionary<string, List<KeyValuePair<string, object>>> owned, string table, string where, object value)
        {
            List<KeyValuePair<string, object>> list;
            if (!owned.TryGetValue(table, out list))
            {
                list = new List<KeyValuePair<string, object>>();
                owned[table] = list;
            }
            string text = where + "|" + Text(value);
            if (list.Exists(delegate(KeyValuePair<string, object> w) { return w.Key + "|" + Text(w.Value) == text; }))
            {
                return false;
            }
            list.Add(new KeyValuePair<string, object>(where, value));
            return true;
        }

        private static string Text(object value)
        {
            object[] many = value as object[];
            if (many != null)
            {
                return string.Join(",", Array.ConvertAll<object, string>(many, delegate(object o) { return Convert.ToString(o, CultureInfo.InvariantCulture); }));
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        // The mod pack's merge: the mod's offer (the car mapped to it when the pack is standalone), every row of the car,
        // and every other row of the working copy that the reference lacks (added or changed). A row only the reference
        // has cannot be removed by a merge: reported.
        public static byte[] BuildMerge(byte[] working, byte[] reference, int offer, string media, bool mapCar, StringBuilder report)
        {
            return BuildMerge(working, reference, offer, media, mapCar, report, ForzaGame.Motorsport4);
        }

        // FM2: tables no official FM2 merge nor DLCBeta carries; with them the game hung on its loading screen (DLC8, 2026-11-14).
        // NewProfile_Arcade_Garage is left out by Skipped already.
        private static readonly string[] Fm2NeverInMerge = { Fm2StockGarage.Table, "Rewards_DriverLevel", "ArcadeCarList", "CarExceptions", "Combo_Engines", "WheelNormOffsets" };

        private static bool LeftOut(string table, ForzaGame game)
        {
            return game == ForzaGame.Motorsport2 && Array.Exists(Fm2NeverInMerge, delegate(string t) { return string.Equals(t, table, StringComparison.OrdinalIgnoreCase); });
        }

        // FM3: a merge with only the mod pack's pack row (MultiDiscContentPacks), for a mod pack without the car's database.
        public static byte[] OfferOnlyMergeFm3(string gameRoot, int offer)
        {
            using (SqliteDatabase game = SqliteDatabase.FromBytes(File.ReadAllBytes(Path.Combine(Path.Combine(Path.Combine(gameRoot, "Media"), "db"), "gamedb.slt"))))
            using (SqliteDatabase merge = SqliteDatabase.FromBytes(new byte[0]))
            {
                merge.Execute("PRAGMA page_size = 1024");
                DlcPackage.AddFm3Offer(game, merge, offer, new StringBuilder());
                return merge.ToBytes();
            }
        }

        // game: FM4 (ContentOffers, mapping when standalone), FM3 (its pack row, MultiDiscContentPacks), FM2 (no offer row; the tables
        // FM2 merges never carry are left out, a change there is reported).
        public static byte[] BuildMerge(byte[] working, byte[] reference, int offer, string media, bool mapCar, StringBuilder report, ForzaGame game)
        {
            using (SqliteDatabase after = SqliteDatabase.FromBytes(working))
            using (SqliteDatabase before = SqliteDatabase.FromBytes(reference))
            using (SqliteDatabase merge = SqliteDatabase.FromBytes(new byte[0]))
            {
                merge.Execute("PRAGMA page_size = 1024");
                object id = after.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", media);
                if (!(id is long))
                {
                    throw new InvalidOperationException("The car's database has no Data_Car row for " + media + ".");
                }
                long car = (long) id;
                Dictionary<string, List<KeyValuePair<string, object>>> owned = Owned(after, car, game);
                int ownRows = 0;
                int changedRows = 0;
                int tables = 0;
                merge.Execute("BEGIN");
                foreach (KeyValuePair<string, List<string>> table in DbIds.Schema(after))
                {
                    if (Skipped(table.Key))
                    {
                        continue;
                    }
                    string quoted = SqliteDatabase.Quote(table.Key);
                    bool known = DbIds.HasTable(before, table.Key);
                    bool keyed = PrimaryKey(after, table.Key) != null;
                    // the reference's rows, counted (a table without a key may repeat a row)
                    Dictionary<string, int> old = new Dictionary<string, int>(StringComparer.Ordinal);
                    if (known)
                    {
                        foreach (object[] row in before.Query("SELECT * FROM " + quoted).Rows)
                        {
                            string key = RowText(row);
                            int n;
                            old.TryGetValue(key, out n);
                            old[key] = n + 1;
                        }
                    }
                    // the car's own rows of this table (rowids), all of them for a keyed table
                    Dictionary<long, bool> mine = new Dictionary<long, bool>();
                    List<KeyValuePair<string, object>> wheres;
                    if (owned.TryGetValue(table.Key, out wheres))
                    {
                        foreach (KeyValuePair<string, object> where in wheres)
                        {
                            object[] args = (where.Value as object[]) ?? new object[] { where.Value };
                            foreach (object[] row in after.Query("SELECT rowid FROM " + quoted + " WHERE " + where.Key, args).Rows)
                            {
                                mine[(long) row[0]] = true;
                            }
                        }
                    }
                    // FM2: no table has a key or an index (its merges only ADD rows, as the official packs do): a row whose logical
                    // key (Id, counter key) the reference has is a changed game row, which a merge cannot replace (it would be a
                    // second row with the same Id). It stays out and is reported.
                    string logical = (game == ForzaGame.Motorsport2 && !keyed && known) ? DbIds.KeyColumn(after, table.Key, table.Value, game) : null;
                    int logicalIndex = (logical == null) ? -1 : table.Value.FindIndex(delegate(string c) { return string.Equals(c, logical, StringComparison.OrdinalIgnoreCase); });
                    HashSet<string> referenceKeys = new HashSet<string>(StringComparer.Ordinal);
                    if (logicalIndex >= 0)
                    {
                        foreach (object[] row in before.Query("SELECT " + SqliteDatabase.Quote(logical) + " FROM " + quoted).Rows)
                        {
                            referenceKeys.Add(Convert.ToString(row[0], CultureInfo.InvariantCulture));
                        }
                    }
                    int unchangeable = 0;
                    List<object[]> rows = new List<object[]>();
                    int added = 0;
                    foreach (object[] full in after.Query("SELECT rowid, * FROM " + quoted).Rows)
                    {
                        object[] row = new object[full.Length - 1];
                        Array.Copy(full, 1, row, 0, row.Length);
                        string key = RowText(row);
                        int n;
                        bool inReference = old.TryGetValue(key, out n) && n > 0;
                        if (inReference)
                        {
                            old[key] = n - 1;
                        }
                        if (!inReference && logicalIndex >= 0 && referenceKeys.Contains(Convert.ToString(row[logicalIndex], CultureInfo.InvariantCulture)))
                        {
                            unchangeable++;
                            continue;
                        }
                        if (!inReference)
                        {
                            rows.Add(row);
                            added++;
                        }
                        else if (keyed && mine.ContainsKey((long) full[0]))
                        {
                            rows.Add(row);
                            ownRows++;
                        }
                    }
                    // what the reference has and the copy lost: changed rows of a keyed table are replaced, anything else stays in game
                    List<string> keyValues = new List<string>();
                    string keyColumn = keyed ? PrimaryKey(after, table.Key) : null;
                    int keyIndex = (keyColumn == null) ? -1 : table.Value.FindIndex(delegate(string c) { return string.Equals(c, keyColumn, StringComparison.OrdinalIgnoreCase); });
                    if (keyIndex >= 0)
                    {
                        foreach (object[] row in rows)
                        {
                            keyValues.Add(Convert.ToString(row[keyIndex], CultureInfo.InvariantCulture));
                        }
                    }
                    if (unchangeable > 0)
                    {
                        report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  WARNING {0}: {1} changed game row(s) stay out: an FM2 merge only adds rows (no key to replace one), the game keeps its own", table.Key, unchangeable));
                    }
                    foreach (KeyValuePair<string, int> left in old)
                    {
                        if (left.Value <= 0 || logicalIndex >= 0)
                        {
                            continue;   // (FM2: the changed rows are reported above; a row the copy lost stays in game)
                        }
                        string first = left.Key.Split('\u0001')[(keyIndex >= 0) ? keyIndex : 0];
                        if (keyIndex >= 0 && keyValues.Contains(first))
                        {
                            changedRows++;
                            continue;   // replaced by the merge's row with the same key
                        }
                        report.AppendLine(string.Format(CultureInfo.InvariantCulture, keyed
                            ? "  WARNING {0}: a row the game has was removed; a mod pack cannot remove it, it stays in game: {1}"
                            : "  WARNING {0} (table without key): a game row was changed or removed; the game keeps it beside the mod's row: {1}",
                            table.Key, left.Key.Replace('\u0001', '|')));
                    }
                    if (rows.Count == 0)
                    {
                        continue;
                    }
                    if (LeftOut(table.Key, game))
                    {
                        if (added > 0)
                        {
                            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  WARNING {0}: {1} new or changed rows stay out (an FM2 merge with this table hangs the game)", table.Key, added));
                        }
                        continue;
                    }
                    CreateLike(after, merge, table.Key, table.Value);
                    string names = string.Join(",", table.Value.ConvertAll<string>(delegate(string c) { return SqliteDatabase.Quote(c); }).ToArray());
                    string marks = string.Join(",", table.Value.ConvertAll<string>(delegate(string c) { return "?"; }).ToArray());
                    foreach (object[] row in rows)
                    {
                        merge.Execute("INSERT INTO " + quoted + " (" + names + ") VALUES (" + marks + ")", row);
                    }
                    tables++;
                    if (added > 0)
                    {
                        report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0}: {1} rows ({2} new or changed)", table.Key, rows.Count, added));
                    }
                }
                merge.Execute("COMMIT");
                if (game == ForzaGame.Motorsport3)
                {
                    DlcPackage.AddFm3Offer(before, merge, offer, report);
                }
                else if (game != ForzaGame.Motorsport2)
                {
                    DlcPackage.AddOffer(before, merge, offer, mapCar ? new List<long> { car } : new List<long>(), report);
                }
                report.Insert(0, string.Format(CultureInfo.InvariantCulture, "car {0} ({1}): merge with {2} tables, the car's rows + {3} rows changed by the mod, offer {4}\r\n",
                    car, media, tables, changedRows, offer));
                return merge.ToBytes();
            }
        }

        private static void CreateLike(SqliteDatabase from, SqliteDatabase merge, string table, List<string> columns)
        {
            List<string> definitions = new List<string>();
            foreach (object[] column in from.Query("PRAGMA table_info(" + SqliteDatabase.Quote(table) + ")").Rows)
            {
                string type = column[2] as string;
                definitions.Add(SqliteDatabase.Quote((string) column[1]) + (string.IsNullOrEmpty(type) ? string.Empty : " " + type));
            }
            merge.Execute("CREATE TABLE " + SqliteDatabase.Quote(table) + " (" + string.Join(",", definitions.ToArray()) + ")");
        }

        private static string RowText(object[] row)
        {
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < row.Length; i++)
            {
                if (i > 0)
                {
                    text.Append('\u0001');
                }
                object value = row[i];
                if (value == null || value is DBNull)
                {
                    text.Append("\u0002");
                }
                else if (value is double)
                {
                    text.Append(((double) value).ToString("R", CultureInfo.InvariantCulture));
                }
                else if (value is byte[])
                {
                    text.Append(Convert.ToBase64String((byte[]) value));
                }
                else
                {
                    text.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                }
            }
            return text.ToString();
        }
    }
}
