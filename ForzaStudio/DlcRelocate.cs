namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    // The database side of "Validate DLC for installation": the ids of a DLC package's merge database are moved
    // out of the way of the game and of the other installed packages (user 2026-10-31: EAGLE at id 30 in one pack
    // and FORD at id 30 in the other -> the second moves to the first free id and takes all its rows with it;
    // the package to install is the only one that is ever changed).
    //   game rows      a row identical to the game's (same key, same values) is left out of the package: the merge
    //                  fails on it (UNIQUE) and the game has the row anyway;
    //   offer          the offer number / OfferId of the pack, when another pack has it (ContentOffers, mapping);
    //   cars           a car whose Id another car has gets the first free Id above every car of the game and the
    //                  installed packs (DbIds.RenumberCar: car columns, keys car * 1000 + n, references); a car
    //                  whose MediaName another car or a folder has gets <name>_2, _3...; thumbnails follow the Id;
    //   engines and drivetrains   a key another pack or the game has moves with the rows built on it
    //                  (engine * 1000 + level: upgrades, torque curves) and with every column that names it;
    //   other keys     every other table with a single key column: a key the others have, with other values
    //                  (or any values, except in the catalogue tables a car only points at: makes, family models,
    //                  manufacturers... which packs re-ship identically) moves to the next free key, references
    //                  by GameDbSchema and by column name follow.
    // Nothing is written outside the database it is given; the files that go with the changes (car folders,
    // thumbnails, offer file names) are listed in CarIds, MediaNames, Thumbnails and OldOffer / Offer.
    public sealed class DlcRelocate
    {
        public sealed class Other
        {
            public string Name;
            public bool IsGame;
            public SqliteDatabase Db;
        }

        private sealed class Hit
        {
            public Other Other;
            public string Text;
            public List<string> Columns;
        }

        private sealed class TableIndex
        {
            public readonly Dictionary<string, List<Hit>> Map = new Dictionary<string, List<Hit>>(StringComparer.Ordinal);
            public long Max;
        }

        public readonly List<string> Log = new List<string>();
        public readonly Dictionary<long, long> CarIds = new Dictionary<long, long>();
        public readonly Dictionary<string, string> MediaNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Thumbnails = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public int OldOffer;
        public int Offer;
        public int Moved;                   // rows whose key changed (cars not counted)
        public int Dropped;                 // rows left out because the game has them
        public readonly List<string> Unresolved = new List<string>();

        // Names of car folders / zips that other packs and the game have and no database row tells.
        public ICollection<string> UsedMediaNames = new List<string>();
        // Offer numbers of installed packages, from their file names.
        public ICollection<int> UsedOffers = new List<int>();
        // True when a file (a game path like game:\Media\UI\Textures\Thumbnails\Thumbnail_1.xds) exists in the game or another pack.
        public Predicate<string> FileTaken;

        private readonly SqliteDatabase db;
        private readonly List<Other> others;
        private readonly ForzaGame game;
        private readonly Dictionary<string, List<string>> schema;
        private readonly Dictionary<string, string[]> keyCache = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TableIndex> indexCache = new Dictionary<string, TableIndex>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, string>> packText = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        private readonly Dictionary<Other, Dictionary<string, List<string>>> otherSchema = new Dictionary<Other, Dictionary<string, List<string>>>();
        private readonly List<string[]> carColumns;
        private Dictionary<string, bool> catalogue;

        public DlcRelocate(SqliteDatabase pack, List<Other> others, ForzaGame game)
        {
            this.db = pack;
            this.others = others;
            this.game = game;
            this.schema = DbIds.Schema(pack);
            this.carColumns = DbIds.CarColumns(this.schema);
            foreach (Other other in others)
            {
                this.otherSchema[other] = DbIds.Schema(other.Db);
            }
        }

        // ----- helpers -----

        private static string Q(string name)
        {
            return SqliteDatabase.Quote(name);
        }

        private static string Text(object value)
        {
            return (value == null || value == DBNull.Value) ? "" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static bool Has(List<string> columns, string column)
        {
            return columns.Exists(delegate(string c) { return string.Equals(c, column, StringComparison.OrdinalIgnoreCase); });
        }

        private static string Join(object[] row, int[] at)
        {
            if (at.Length == 1)
            {
                return Text(row[at[0]]);
            }
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < at.Length; i++)
            {
                text.Append(i == 0 ? "" : "|").Append(Text(row[at[i]]));
            }
            return text.ToString();
        }

        private static string Names(IEnumerable<string> columns)
        {
            List<string> quoted = new List<string>();
            foreach (string column in columns)
            {
                quoted.Add(Q(column));
            }
            return string.Join(", ", quoted.ToArray());
        }

        private IEnumerable<string> Tables()
        {
            List<string> tables = new List<string>();
            foreach (string table in this.schema.Keys)
            {
                if (!table.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
                {
                    tables.Add(table);
                }
            }
            tables.Sort(StringComparer.OrdinalIgnoreCase);
            return tables;
        }

        // The key columns of a table of the package: its primary key, else its first column when that is an Id
        // column with a distinct value in every row. A car column (CarId, Ordinal...) is the car's, not a key.
        private string[] KeyColumns(string table)
        {
            string[] cached;
            if (this.keyCache.TryGetValue(table, out cached))
            {
                return cached;
            }
            SortedList<long, string> ordered = new SortedList<long, string>();
            foreach (object[] row in this.db.Query("PRAGMA table_info(" + Q(table) + ")").Rows)
            {
                if (row[5] is long && (long) row[5] > 0)
                {
                    ordered[(long) row[5]] = (string) row[1];
                }
            }
            string[] result = new string[0];
            List<string> columns = this.schema[table];
            if (ordered.Count > 0)
            {
                result = new string[ordered.Count];
                ordered.Values.CopyTo(result, 0);
            }
            else if (columns.Count > 0 && columns[0].EndsWith("id", StringComparison.OrdinalIgnoreCase)
                && Convert.ToInt64(this.db.Scalar("SELECT count(*) FROM " + Q(table))) == Convert.ToInt64(this.db.Scalar("SELECT count(DISTINCT " + Q(columns[0]) + ") FROM " + Q(table))))
            {
                result = new string[] { columns[0] };
            }
            if (result.Length == 1)
            {
                string only = result[0];
                if (this.carColumns.Exists(delegate(string[] c) { return string.Equals(c[0], table, StringComparison.OrdinalIgnoreCase) && string.Equals(c[1], only, StringComparison.OrdinalIgnoreCase); }))
                {
                    result = new string[0];
                }
            }
            this.keyCache[table] = result;
            return result;
        }

        // The rows of the other databases (the game first) under the table's key.
        private TableIndex Index(string table)
        {
            TableIndex index;
            if (this.indexCache.TryGetValue(table, out index))
            {
                return index;
            }
            index = new TableIndex();
            this.indexCache[table] = index;
            string[] key = this.KeyColumns(table);
            if (key.Length == 0)
            {
                return index;
            }
            foreach (Other other in this.others)
            {
                List<string> theirs;
                if (!this.otherSchema[other].TryGetValue(table, out theirs))
                {
                    continue;
                }
                List<string> shared = this.schema[table].FindAll(delegate(string c) { return Has(theirs, c); });
                int[] at = new int[key.Length];
                bool all = true;
                for (int i = 0; i < key.Length; i++)
                {
                    string column = key[i];
                    at[i] = shared.FindIndex(delegate(string c) { return string.Equals(c, column, StringComparison.OrdinalIgnoreCase); });
                    all = all && at[i] >= 0;
                }
                if (!all)
                {
                    continue;
                }
                foreach (object[] row in other.Db.Query("SELECT " + Names(shared) + " FROM " + Q(table)).Rows)
                {
                    string ks = Join(row, at);
                    Hit hit = new Hit();
                    hit.Other = other;
                    hit.Columns = shared;
                    hit.Text = DbIntegrity.RowText(row);
                    List<Hit> list;
                    if (!index.Map.TryGetValue(ks, out list))
                    {
                        list = new List<Hit>();
                        index.Map[ks] = list;
                    }
                    list.Add(hit);
                    long number;
                    if (key.Length == 1 && long.TryParse(ks, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                    {
                        index.Max = Math.Max(index.Max, number);
                    }
                }
            }
            return index;
        }

        // The package's row under a key as text, on the columns another database shares with it.
        private string PackRowText(string table, List<string> columns, string keyString)
        {
            string signature = table + "\n" + string.Join(",", columns.ToArray());
            Dictionary<string, string> rows;
            if (!this.packText.TryGetValue(signature, out rows))
            {
                rows = new Dictionary<string, string>(StringComparer.Ordinal);
                string[] key = this.KeyColumns(table);
                int[] at = new int[key.Length];
                for (int i = 0; i < key.Length; i++)
                {
                    string column = key[i];
                    at[i] = columns.FindIndex(delegate(string c) { return string.Equals(c, column, StringComparison.OrdinalIgnoreCase); });
                }
                foreach (object[] row in this.db.Query("SELECT " + Names(columns) + " FROM " + Q(table)).Rows)
                {
                    rows[Join(row, at)] = DbIntegrity.RowText(row);
                }
                this.packText[signature] = rows;
            }
            string text;
            return rows.TryGetValue(keyString, out text) ? text : null;
        }

        private void Forget()
        {
            this.indexCache.Clear();
            this.packText.Clear();
            this.keyCache.Clear();
        }

        private long PackMax(string table, string column)
        {
            object top = this.db.Scalar("SELECT max(" + Q(column) + ") FROM " + Q(table));
            return (top == null || top == DBNull.Value || !(top is long)) ? 0 : (long) top;
        }

        // The next free key of a single-column key: above the package's, the others' and the game's limits.
        private long NextKey(string table, string column)
        {
            long top = Math.Max(this.PackMax(table, column), this.Index(table).Max);
            return Math.Max(top, GameIdLimits.Max(this.game, table, column)) + 1;
        }

        // A key that is not above FM4's largest of its column (base game and every DLC): DbIntegrity's rule, so a key
        // like that moves even when nothing installed uses it yet.
        private bool BelowGameLimit(string table, string column, object value)
        {
            if (!(value is long))
            {
                return false;
            }
            long limit = GameIdLimits.Max(this.game, table, column);
            return limit >= 0 && (long) value <= limit;
        }

        // Tables a car only points at (makes, family models, manufacturers...): packs re-ship their rows identically.
        private bool IsCatalogue(string table)
        {
            if (this.catalogue == null)
            {
                this.catalogue = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                List<string> queue = new List<string>();
                foreach (GameDbSchema.Relation relation in GameDbSchema.From("Data_Car"))
                {
                    if (!string.Equals(relation.Target, "List_Wheels", StringComparison.OrdinalIgnoreCase) && !this.catalogue.ContainsKey(relation.Target))
                    {
                        this.catalogue[relation.Target] = true;
                        queue.Add(relation.Target);
                    }
                }
                for (int i = 0; i < queue.Count; i++)
                {
                    foreach (GameDbSchema.Relation relation in GameDbSchema.From(queue[i]))
                    {
                        if (!this.catalogue.ContainsKey(relation.Target))
                        {
                            this.catalogue[relation.Target] = true;
                            queue.Add(relation.Target);
                        }
                    }
                }
            }
            return this.catalogue.ContainsKey(table);
        }

        // ----- 1. rows the game has -----

        public void DropGameRows()
        {
            foreach (string table in this.Tables())
            {
                string[] key = this.KeyColumns(table);
                if (key.Length == 0)
                {
                    continue;
                }
                TableIndex index = this.Index(table);
                int[] at = new int[key.Length];
                for (int i = 0; i < at.Length; i++)
                {
                    at[i] = i;
                }
                List<object[]> gone = new List<object[]>();
                foreach (object[] row in this.db.Query("SELECT " + Names(key) + " FROM " + Q(table)).Rows)
                {
                    List<Hit> hits;
                    if (!index.Map.TryGetValue(Join(row, at), out hits))
                    {
                        continue;
                    }
                    Hit inGame = hits.Find(delegate(Hit h) { return h.Other.IsGame; });
                    if (inGame != null && this.PackRowText(table, inGame.Columns, Join(row, at)) == inGame.Text)
                    {
                        gone.Add(row);
                    }
                }
                if (gone.Count == 0)
                {
                    continue;
                }
                string where = " WHERE ";
                for (int i = 0; i < key.Length; i++)
                {
                    where += (i == 0 ? "" : " AND ") + Q(key[i]) + " = ?";
                }
                foreach (object[] row in gone)
                {
                    this.db.Execute("DELETE FROM " + Q(table) + where, row);
                }
                this.Dropped += gone.Count;
                this.Log.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1} row(s) identical to the game's left out", table, gone.Count));
            }
            this.Forget();
        }

        // ----- 2. offer -----

        // FM3: the pack is a MultiDiscContentPacks row (ID = 4D53084D0CCF + the offer as 4 hex digits, intID = PubOfferingID = the offer).
        // The offer is free when no other package or the game has the number or the ID.
        private void MoveOfferFm3()
        {
            if (Convert.ToInt64(this.db.Scalar("SELECT count(*) FROM MultiDiscContentPacks")) == 0)
            {
                return;
            }
            object[] own = this.db.Query("SELECT ID, intID FROM MultiDiscContentPacks ORDER BY intID LIMIT 1").Rows[0];
            string ownId = Text(own[0]);
            this.OldOffer = Convert.ToInt32(own[1], CultureInfo.InvariantCulture);
            this.Offer = this.OldOffer;
            long top = Math.Max(99, GameIdLimits.Max(this.game, "MultiDiscContentPacks", "intID"));
            bool clash = false;
            foreach (int used in this.UsedOffers)
            {
                top = Math.Max(top, used);
                clash = clash || used == this.OldOffer;
            }
            foreach (Other other in this.others)
            {
                if (!DbIds.HasTable(other.Db, "MultiDiscContentPacks"))
                {
                    continue;
                }
                foreach (object[] row in other.Db.Query("SELECT ID, intID FROM MultiDiscContentPacks").Rows)
                {
                    if (row[1] is long)
                    {
                        top = Math.Max(top, (long) row[1]);
                        clash = clash || (long) row[1] == this.OldOffer;
                    }
                    clash = clash || string.Equals(Text(row[0]), ownId, StringComparison.OrdinalIgnoreCase);
                }
            }
            if (!clash)
            {
                this.Log.Add("offer " + this.OldOffer + ": free, kept");
                return;
            }
            this.Offer = (int) top + 1;
            string id = DlcPackage.OfferIdFm3(this.Offer);
            this.db.Execute("UPDATE MultiDiscContentPacks SET ID = ?, intID = ?, PubOfferingID = ? WHERE ID = ?", id, (long) this.Offer, (long) this.Offer, ownId);
            this.Log.Add("offer " + this.OldOffer + " is used by another package or the game: now " + this.Offer + " (ID " + id + ")");
            this.Forget();
        }

        public void MoveOffer()
        {
            if (!DbIds.HasTable(this.db, "ContentOffers") && DbIds.HasTable(this.db, "MultiDiscContentPacks"))
            {
                this.MoveOfferFm3();
                return;
            }
            if (!DbIds.HasTable(this.db, "ContentOffers") || Convert.ToInt64(this.db.Scalar("SELECT count(*) FROM ContentOffers")) == 0)
            {
                return;
            }
            object[] own = this.db.Query("SELECT PubOfferingID, OfferId FROM ContentOffers LIMIT 1").Rows[0];
            this.OldOffer = Convert.ToInt32(own[0], CultureInfo.InvariantCulture);
            this.Offer = this.OldOffer;
            long ownOfferId = Convert.ToInt64(own[1], CultureInfo.InvariantCulture);
            long top = Math.Max(90, GameIdLimits.Max(this.game, "ContentOffers", "PubOfferingID"));
            bool clash = false;
            foreach (int used in this.UsedOffers)
            {
                top = Math.Max(top, used);
                clash = clash || used == this.OldOffer;
            }
            foreach (Other other in this.others)
            {
                if (!DbIds.HasTable(other.Db, "ContentOffers"))
                {
                    continue;
                }
                foreach (object[] row in other.Db.Query("SELECT PubOfferingID, OfferId FROM ContentOffers").Rows)
                {
                    if (row[0] is long)
                    {
                        top = Math.Max(top, (long) row[0]);
                        clash = clash || (long) row[0] == this.OldOffer;
                    }
                    clash = clash || (row[1] is long && (long) row[1] == ownOfferId);
                }
            }
            if (!clash)
            {
                this.Log.Add("offer " + this.OldOffer + ": free, kept");
                return;
            }
            this.Offer = (int) top + 1;
            string id = DlcPackage.OfferId(this.Offer);
            long offerId = long.Parse(id, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            this.db.Execute("UPDATE ContentOffers SET ID = ?, OfferId = ?, PubOfferingID = ? WHERE OfferId = ?", id, offerId, (long) this.Offer, ownOfferId);
            if (DbIds.HasTable(this.db, "ContentOffersMapping"))
            {
                long was = this.OldOffer * 1000L;
                long now = this.Offer * 1000L;
                long next = 0;
                foreach (object[] row in this.db.Query("SELECT Id FROM ContentOffersMapping WHERE OfferId = ? ORDER BY Id", ownOfferId).Rows)
                {
                    long old = (long) row[0];
                    long to = (old >= was && old < was + 1000) ? now + (old - was) : now + 500 + next++;
                    this.db.Execute("UPDATE ContentOffersMapping SET Id = ?, OfferId = ? WHERE Id = ?", to, offerId, old);
                }
            }
            this.Log.Add("offer " + this.OldOffer + " is used by another package: now " + this.Offer + " (OfferId " + id + ")");
            this.Forget();
        }

        // ----- 3. cars -----

        public void MoveCars()
        {
            if (!DbIds.HasTable(this.db, "Data_Car"))
            {
                return;
            }
            Dictionary<long, bool> ids = new Dictionary<long, bool>();
            Dictionary<string, bool> names = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            long top = GameIdLimits.Max(this.game, "Data_Car", "Id");
            foreach (Other other in this.others)
            {
                if (!DbIds.HasTable(other.Db, "Data_Car"))
                {
                    continue;
                }
                foreach (object[] row in other.Db.Query("SELECT Id, MediaName FROM Data_Car").Rows)
                {
                    if (row[0] is long)
                    {
                        ids[(long) row[0]] = true;
                        top = Math.Max(top, (long) row[0]);
                    }
                    names[Text(row[1])] = true;
                }
            }
            foreach (string used in this.UsedMediaNames)
            {
                names[used] = true;
            }
            top = Math.Max(top, this.PackMax("Data_Car", "Id"));
            List<object[]> cars = this.db.Query("SELECT Id, MediaName FROM Data_Car ORDER BY Id").Rows;
            foreach (object[] car in cars)
            {
                long id = (long) car[0];
                string name = Text(car[1]);
                bool newId = ids.ContainsKey(id);
                bool newName = names.ContainsKey(name);
                if (!newId && !newName)
                {
                    this.Thumbnail(id, false);
                    continue;
                }
                long now = id;
                if (newId)
                {
                    now = top + 1;
                    while (this.CarTaken(now))
                    {
                        now++;
                    }
                    top = now;
                    foreach (string line in DbIds.RenumberCar(this.db, id, now))
                    {
                        this.Log.Add("  " + line);
                    }
                    this.CarIds[id] = now;
                    this.Log.Add(string.Format(CultureInfo.InvariantCulture, "car {0} ({1}): Id {2} is used by another car, now {3}", name, id, id, now));
                }
                if (newName)
                {
                    string fresh = this.FreeName(name, names);
                    this.RenameCar(name, fresh);
                    names[fresh] = true;
                    this.MediaNames[name] = fresh;
                    this.Log.Add(string.Format(CultureInfo.InvariantCulture, "car {0}: MediaName is used by another car or folder, now {1}", name, fresh));
                }
                this.Thumbnail(now, newId);
            }
            this.Forget();
        }

        private bool CarTaken(long id)
        {
            if (DbIds.Taken(this.db, id))
            {
                return true;
            }
            foreach (Other other in this.others)
            {
                if (DbIds.HasTable(other.Db, "Data_Car") && DbIds.Taken(other.Db, id))
                {
                    return true;
                }
            }
            return false;
        }

        private string FreeName(string name, Dictionary<string, bool> names)
        {
            for (int n = 2; ; n++)
            {
                string tag = "_" + n.ToString(CultureInfo.InvariantCulture);
                string candidate = ((name.Length + tag.Length > 40) ? name.Substring(0, 40 - tag.Length) : name) + tag;
                if (!names.ContainsKey(candidate) && Convert.ToInt64(this.db.Scalar("SELECT count(*) FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", candidate)) == 0)
                {
                    return candidate;
                }
            }
        }

        // The car's MediaName in the rows built on it (Data_Car, its wheel). Engines keep theirs: it names the
        // engine's tuning file, which is the game's.
        private void RenameCar(string old, string now)
        {
            foreach (string table in this.Tables())
            {
                if (table.StartsWith("Data_Engine", StringComparison.OrdinalIgnoreCase) || table.StartsWith("Data_Motor", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                foreach (string column in new string[] { "MediaName", "IconPath" })
                {
                    if (!Has(this.schema[table], column))
                    {
                        continue;
                    }
                    int changed = this.db.Execute("UPDATE " + Q(table) + " SET " + Q(column) + " = ? WHERE " + Q(column) + " = ? COLLATE NOCASE", now, old);
                    if (changed > 0)
                    {
                        this.Log.Add(string.Format(CultureInfo.InvariantCulture, "  {0}.{1}: {2} row(s)", table, column, changed));
                    }
                }
            }
        }

        // The thumbnail follows the car's Id (Thumbnail_<Id>.xds in the folder it already uses) when the car moved or
        // when its file is one another pack or the game has.
        private void Thumbnail(long id, bool moved)
        {
            if (!Has(this.schema["Data_Car"], "Thumbnail"))
            {
                return;
            }
            string path = this.db.Scalar("SELECT Thumbnail FROM Data_Car WHERE Id = ?", id) as string;
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            int slash = path.LastIndexOfAny(new char[] { '\\', '/' });
            string want = path.Substring(0, slash + 1) + "Thumbnail_" + id.ToString(CultureInfo.InvariantCulture) + ".xds";
            if (string.Equals(want, path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (!moved && (this.FileTaken == null || !this.FileTaken(path)))
            {
                return;
            }
            this.db.Execute("UPDATE Data_Car SET Thumbnail = ? WHERE Id = ?", want, id);
            this.Thumbnails[path] = want;
            this.Log.Add("  thumbnail " + path.Substring(slash + 1) + " -> " + want.Substring(slash + 1));
        }

        // ----- 4. engines and drivetrains -----

        public void MoveRoots()
        {
            this.MoveRoots("Data_Engine", "EngineID");
            this.MoveRoots("Data_Drivetrain", "DrivetrainID");
        }

        private void MoveRoots(string table, string column)
        {
            if (!this.schema.ContainsKey(table) || !Has(this.schema[table], column))
            {
                return;
            }
            TableIndex index = this.Index(table);
            if (this.KeyColumns(table).Length != 1)
            {
                return;
            }
            foreach (object[] row in this.db.Query("SELECT " + Q(column) + " FROM " + Q(table) + " ORDER BY 1").Rows)
            {
                if (!(row[0] is long))
                {
                    continue;
                }
                bool used = index.Map.ContainsKey(Text(row[0]));
                if (!used && !this.BelowGameLimit(table, column, row[0]))
                {
                    continue;
                }
                long old = (long) row[0];
                long now = this.NextRoot(table, column);
                this.MoveKey(table, column, old, now, true);
                this.Moved++;
                this.Log.Add(string.Format(CultureInfo.InvariantCulture, "{0}.{1} {2} {3}: now {4}", table, column, old, used ? "is used by another package or the game" : "is not above FM4's largest", now));
            }
            this.Forget();
        }

        // A root key whose range key * 1000 + n is free in the others too.
        private long NextRoot(string table, string column)
        {
            long top = this.NextKey(table, column) - 1;
            foreach (GameDbSchema.Relation relation in GameDbSchema.Into(table))
            {
                if (relation.IsComposite || !this.schema.ContainsKey(relation.Table))
                {
                    continue;
                }
                string[] key = this.KeyColumns(relation.Table);
                if (key.Length == 1 && !string.Equals(key[0], relation.Column, StringComparison.OrdinalIgnoreCase))
                {
                    top = Math.Max(top, Math.Max(this.PackMax(relation.Table, key[0]), Math.Max(this.Index(relation.Table).Max, GameIdLimits.Max(this.game, relation.Table, key[0]))) / 1000);
                }
            }
            return top + 1;
        }

        // Moves a key and what depends on it. children: the rows of the tables that name the key and are keyed
        // old * 1000 + n move to now * 1000 + n.
        private void MoveKey(string table, string column, long old, long now, bool children)
        {
            if (children)
            {
                foreach (GameDbSchema.Relation relation in GameDbSchema.Into(table))
                {
                    if (relation.IsComposite || !string.Equals(relation.TargetColumn, column, StringComparison.OrdinalIgnoreCase)
                        || !this.schema.ContainsKey(relation.Table) || !Has(this.schema[relation.Table], relation.Column))
                    {
                        continue;
                    }
                    string[] key = this.KeyColumns(relation.Table);
                    if (key.Length != 1 || string.Equals(key[0], relation.Column, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    long low = old * 1000;
                    foreach (object[] child in this.db.Query("SELECT DISTINCT " + Q(key[0]) + " FROM " + Q(relation.Table) + " WHERE " + Q(relation.Column) + " = ? AND " + Q(key[0]) + " BETWEEN ? AND ?", old, low, low + 999).Rows)
                    {
                        if (child[0] is long)
                        {
                            this.MoveKey(relation.Table, key[0], (long) child[0], (now * 1000) + ((long) child[0] - low), false);
                        }
                    }
                }
            }
            if (children && string.Equals(table, "Data_Engine", StringComparison.OrdinalIgnoreCase) && this.schema.ContainsKey("List_TorqueCurve"))
            {
                // An engine's torque curves are keyed engine * 1000 + level whether or not their EngineID column is filled.
                string[] curve = this.KeyColumns("List_TorqueCurve");
                if (curve.Length == 1)
                {
                    long low = old * 1000;
                    foreach (object[] child in this.db.Query("SELECT " + Q(curve[0]) + " FROM " + Q("List_TorqueCurve") + " WHERE " + Q(curve[0]) + " BETWEEN ? AND ?", low, low + 999).Rows)
                    {
                        if (child[0] is long)
                        {
                            this.MoveKey("List_TorqueCurve", curve[0], (long) child[0], (now * 1000) + ((long) child[0] - low), false);
                        }
                    }
                }
            }
            this.db.Execute("UPDATE " + Q(table) + " SET " + Q(column) + " = ? WHERE " + Q(column) + " = ?", now, old);
            List<string[]> refs = new List<string[]>();
            foreach (GameDbSchema.Relation relation in GameDbSchema.Into(table))
            {
                if (!relation.IsComposite && string.Equals(relation.TargetColumn, column, StringComparison.OrdinalIgnoreCase)
                    && this.schema.ContainsKey(relation.Table) && Has(this.schema[relation.Table], relation.Column))
                {
                    refs.Add(new string[] { relation.Table, relation.Column });
                }
            }
            // Columns of the same name that the relation map lacks (Combo_Engines.EngineID).
            if (column.Length > 3)
            {
                foreach (string other in this.Tables())
                {
                    foreach (string c in this.schema[other])
                    {
                        if (string.Equals(c, column, StringComparison.OrdinalIgnoreCase) && !string.Equals(other, table, StringComparison.OrdinalIgnoreCase)
                            && !refs.Exists(delegate(string[] r) { return string.Equals(r[0], other, StringComparison.OrdinalIgnoreCase) && string.Equals(r[1], c, StringComparison.OrdinalIgnoreCase); }))
                        {
                            refs.Add(new string[] { other, c });
                        }
                    }
                }
            }
            foreach (string[] reference in refs)
            {
                this.db.Execute("UPDATE " + Q(reference[0]) + " SET " + Q(reference[1]) + " = ? WHERE " + Q(reference[1]) + " = ?", now, old);
            }
        }

        // ----- 5. every other key -----

        public void MoveKeys()
        {
            foreach (string table in this.Tables())
            {
                if (string.Equals(table, "ContentOffers", StringComparison.OrdinalIgnoreCase) || string.Equals(table, "ContentOffersMapping", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string[] key = this.KeyColumns(table);
                if (key.Length != 1)
                {
                    continue;
                }
                TableIndex index = this.Index(table);
                if (index.Map.Count == 0 && GameIdLimits.Max(this.game, table, key[0]) < 0)
                {
                    continue;
                }
                bool catalogue = this.IsCatalogue(table);
                int moved = 0;
                foreach (object[] row in this.db.Query("SELECT " + Q(key[0]) + " FROM " + Q(table) + " ORDER BY 1").Rows)
                {
                    List<Hit> hits;
                    string ks = Text(row[0]);
                    bool used = index.Map.TryGetValue(ks, out hits);
                    if (!used && !this.BelowGameLimit(table, key[0], row[0]))
                    {
                        continue;
                    }
                    if (used && catalogue && hits.TrueForAll(delegate(Hit h) { return this.PackRowText(table, h.Columns, ks) == h.Text; }))
                    {
                        continue;
                    }
                    if (!(row[0] is long))
                    {
                        this.Unresolved.Add(string.Format(CultureInfo.InvariantCulture, "{0}.{1} = {2} is used by {3} and is not a number: not moved", table, key[0], ks, hits[0].Other.Name));
                        continue;
                    }
                    long now = this.NextKey(table, key[0]);
                    this.MoveKey(table, key[0], (long) row[0], now, false);
                    moved++;
                    if (moved <= 3)
                    {
                        this.Log.Add(string.Format(CultureInfo.InvariantCulture, "{0}.{1} {2} {3}: now {4}", table, key[0], ks, used ? "is used by " + hits[0].Other.Name : "is not above FM4's largest", now));
                    }
                }
                if (moved > 0)
                {
                    this.Moved += moved;
                    if (moved > 3)
                    {
                        this.Log.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1} keys moved in all", table, moved));
                    }
                    this.indexCache.Remove(table);
                    this.packText.Clear();
                }
            }
        }

        public void Run()
        {
            this.DropGameRows();
            this.MoveOffer();
            this.MoveCars();
            this.MoveRoots();
            this.MoveKeys();
        }
    }
}
