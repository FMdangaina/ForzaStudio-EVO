namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;

    // FM4 gamedb.slt: the game's SQLite database (cars, engines, upgrades, tracks, events...).
    // Knows how the tables reference each other, which columns are ids of other tables and how to
    // show them, and writes every edit with the column's own type.
    public sealed class GameDatabase : IDisposable
    {
        public enum ValueKind
        {
            Integer,
            Real,
            Text
        }

        public class Column
        {
            public string Name;
            public string DeclaredType;
            public ValueKind Kind;
            public bool IsPrimaryKey;
        }

        public class Table
        {
            public string Name;
            public List<Column> Columns = new List<Column>();
            public Column PrimaryKey;   // INTEGER PRIMARY KEY (the rowid), null if the table has none

            public Column Find(string name)
            {
                foreach (Column column in this.Columns)
                {
                    if (string.Equals(column.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return column;
                    }
                }
                return null;
            }
        }

        // An id column shown with the name of the row it points to.
        public class Lookup
        {
            public List<KeyValuePair<long, string>> Items = new List<KeyValuePair<long, string>>();
            private Dictionary<long, string> names = new Dictionary<long, string>();

            public void Add(long value, string name)
            {
                if (!this.names.ContainsKey(value))
                {
                    this.names[value] = name;
                    this.Items.Add(new KeyValuePair<long, string>(value, name));
                }
            }

            public string Describe(long value)
            {
                string name;
                return this.names.TryGetValue(value, out name) && !string.IsNullOrEmpty(name) ? string.Format("{0} - {1}", value, name) : value.ToString(CultureInfo.InvariantCulture);
            }

            public string Name(long value)
            {
                string name;
                return this.names.TryGetValue(value, out name) ? name : null;
            }
        }

        public class Change
        {
            public string Table;
            public long RowId;
            public string Column;
            public object OldValue;
            public object NewValue;

            public override string ToString()
            {
                if (this.Column == null)
                {
                    return string.Format("{0} #{1}: {2}", this.Table, this.RowId, this.NewValue);
                }
                return string.Format("{0} #{1}  {2}: {3} -> {4}", this.Table, this.RowId, this.Column, FormatValue(this.OldValue), FormatValue(this.NewValue));
            }
        }

        // A list of rows related to the record being edited (upgrades of a car, races of an event...).
        public class Related
        {
            public string Label;
            public string Table;
            public string Where;
            public int Count;

            public Related(string label, string table, string where)
            {
                this.Label = label;
                this.Table = table;
                this.Where = where;
            }

            public override string ToString()
            {
                return string.Format("{0}  ({1})", this.Label, this.Count);
            }
        }

        private SqliteDatabase db;
        private Dictionary<string, Table> tables = new Dictionary<string, Table>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, Lookup> lookups = new Dictionary<string, Lookup>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, bool> flags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public List<Change> Changes = new List<Change>();
        public List<string> TableNames = new List<string>();

        // The game plus its installed DLC packages (InstalledDlc.Merge): a view, never written to the game's file.
        public bool ReadOnly;
        public string ReadOnlyReason;   // the message of an edit refused, when not the DLC view's
        public readonly Dictionary<long, string> DlcCars = new Dictionary<long, string>();
        public readonly Dictionary<long, string> DlcTracks = new Dictionary<long, string>();

        private void CheckWritable()
        {
            if (this.ReadOnly)
            {
                throw new InvalidOperationException(this.ReadOnlyReason ?? "This view holds the game plus its installed DLC packages and is read-only. Turn the DLC button off in the toolbar to edit the game's own database.");
            }
        }

        public GameDatabase(byte[] data)
        {
            this.db = SqliteDatabase.FromBytes(data);
            foreach (object[] row in this.db.Query("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name").Rows)
            {
                Table table = new Table();
                table.Name = (string) row[0];
                foreach (object[] info in this.db.Query("PRAGMA table_info(" + SqliteDatabase.Quote(table.Name) + ")").Rows)
                {
                    Column column = new Column();
                    column.Name = (string) info[1];
                    column.DeclaredType = ((info[2] as string) ?? "").ToUpperInvariant();
                    column.Kind = KindOf(column.DeclaredType);
                    column.IsPrimaryKey = Convert.ToInt64(info[5]) != 0;
                    table.Columns.Add(column);
                }
                List<Column> keys = table.Columns.FindAll(delegate(Column c) { return c.IsPrimaryKey; });
                if (keys.Count == 1 && keys[0].DeclaredType == "INTEGER")
                {
                    table.PrimaryKey = keys[0];
                }
                this.tables[table.Name] = table;
                this.TableNames.Add(table.Name);
            }
            this.ownGame = this.DetectGame();
        }

        private readonly ForzaGame ownGame;

        // Which Forza the database belongs to, from its tables alone (it can be opened from anywhere).
        // A partial database (DLC patch) without the telling tables takes its reference's game.
        public ForzaGame Game
        {
            get { return (this.ownGame == ForzaGame.Unknown && this.reference != null) ? this.reference.Game : this.ownGame; }
        }

        // Checked on the five extracted games (2026-11-10): FH1/FH2 have the event hubs (FH2 also the tire
        // brands), FM4 the electric motors, FM2 one upgrade table per part (CamValves) and Data_Car columns
        // with units in the name; FM3 is a full database with none of these.
        private ForzaGame DetectGame()
        {
            if (this.HasTable("EventHubs"))
            {
                return this.HasTable("List_TireBrand") ? ForzaGame.Horizon2 : ForzaGame.Horizon1;
            }
            if (this.HasTable("Data_Motor"))
            {
                return ForzaGame.Motorsport4;
            }
            if (this.HasTable("List_UpgradeEngineCamValves") || this.HasColumn("Data_Car", "FrontWeight-pct"))
            {
                return ForzaGame.Motorsport2;
            }
            return (this.HasTable("Data_Car") && this.HasTable("CarClasses")) ? ForzaGame.Motorsport3 : ForzaGame.Unknown;
        }

        public SqliteDatabase Sql
        {
            get { return this.db; }
        }

        public byte[] ToBytes()
        {
            return this.db.ToBytes();
        }

        public Table GetTable(string name)
        {
            Table table;
            return this.tables.TryGetValue(name, out table) ? table : null;
        }

        public bool HasTable(string name)
        {
            return this.tables.ContainsKey(name);
        }

        public long Count(string table, string where)
        {
            if (!this.HasTable(table))
            {
                return 0;
            }
            return Convert.ToInt64(this.db.Scalar("SELECT count(*) FROM " + SqliteDatabase.Quote(table) + (string.IsNullOrEmpty(where) ? "" : " WHERE " + where)));
        }

        // SQL list of ids for IN (...); the values come from the database itself and are numbers.
        public string IdList(string sql, params object[] args)
        {
            List<string> ids = new List<string>();
            SqliteDatabase.Result result;
            try
            {
                result = this.db.Query(sql, args);
            }
            catch (InvalidOperationException)
            {
                // A partial database (DLC patch) may not have the table.
                return "NULL";
            }
            foreach (object[] row in result.Rows)
            {
                if (row[0] != null)
                {
                    string id = Convert.ToInt64(row[0]).ToString(CultureInfo.InvariantCulture);
                    if (!ids.Contains(id))
                    {
                        ids.Add(id);
                    }
                }
            }
            return (ids.Count == 0) ? "NULL" : string.Join(",", ids.ToArray());
        }

        private static ValueKind KindOf(string declared)
        {
            // SQLite affinity rules.
            if (declared.Contains("INT"))
            {
                return ValueKind.Integer;
            }
            if (declared.Contains("CHAR") || declared.Contains("CLOB") || declared.Contains("TEXT"))
            {
                return ValueKind.Text;
            }
            return ValueKind.Real;
        }

        #region Values

        public static string FormatValue(object value)
        {
            if (value == null || value is DBNull)
            {
                return "NULL";
            }
            if (value is double)
            {
                return ((double) value).ToString("R", CultureInfo.InvariantCulture);
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        // Text typed by the user to a value of the column's type. Both "15,1" and "15.1" are accepted,
        // so a number never ends up stored as text in a numeric column.
        public static object ParseValue(Column column, string text)
        {
            text = (text ?? "").Trim();
            if (column.Kind == ValueKind.Text)
            {
                // Localized text is shown as "Supra RZ   [_&3444126252]": the database keeps the id.
                System.Text.RegularExpressions.Match id = System.Text.RegularExpressions.Regex.Match(text, @"\[(_&\d+)\]$");
                return id.Success ? id.Groups[1].Value : text;
            }
            if (text.Length == 0 || string.Equals(text, "NULL", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            if (column.Kind == ValueKind.Integer)
            {
                // "49 - Toyota" from a list: the id is the leading number.
                int end = 0;
                while (end < text.Length && (char.IsDigit(text[end]) || (end == 0 && text[end] == '-')))
                {
                    end++;
                }
                long number;
                if (end > 0 && long.TryParse(text.Substring(0, end), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number))
                {
                    return number;
                }
                throw new FormatException(string.Format("{0} needs a whole number.", column.Name));
            }
            double real;
            if (double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out real))
            {
                return real;
            }
            throw new FormatException(string.Format("{0} needs a number (for example 1.25).", column.Name));
        }

        public static object Normalize(Column column, object value)
        {
            if (value == null || value is DBNull)
            {
                return null;
            }
            if (value is string)
            {
                return ParseValue(column, (string) value);
            }
            switch (column.Kind)
            {
                case ValueKind.Integer:
                    return Convert.ToInt64(value, CultureInfo.InvariantCulture);
                case ValueKind.Real:
                    return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        #endregion

        #region Edits

        // The rowid of the row whose id (first column) is key. In the full gamedb the id is the
        // INTEGER PRIMARY KEY, so it is the rowid itself; DLC patch tables have no key at all.
        public long RowIdOf(string tableName, long key)
        {
            Table table = this.GetTable(tableName);
            if (table == null || table.Columns.Count == 0)
            {
                return -1;
            }
            if (table.PrimaryKey != null)
            {
                return key;
            }
            object rowId = this.db.Scalar("SELECT rowid FROM " + SqliteDatabase.Quote(table.Name) + " WHERE " + SqliteDatabase.Quote(table.Columns[0].Name) + " = ? LIMIT 1", key);
            return (rowId is long) ? (long) rowId : -1;
        }

        public object[] ReadRow(string table, long rowId)
        {
            SqliteDatabase.Result result = this.db.Query("SELECT * FROM " + SqliteDatabase.Quote(table) + " WHERE rowid = ?", rowId);
            return (result.Rows.Count == 0) ? null : result.Rows[0];
        }

        // Writes one value; returns the row's rowid afterwards (it changes when the primary key is edited).
        public long Update(string tableName, long rowId, string columnName, object value)
        {
            this.CheckWritable();
            Table table = this.GetTable(tableName);
            Column column = table.Find(columnName);
            object newValue = Normalize(column, value);
            object oldValue = this.db.Scalar("SELECT " + SqliteDatabase.Quote(column.Name) + " FROM " + SqliteDatabase.Quote(table.Name) + " WHERE rowid = ?", rowId);
            if (Equals(oldValue, newValue))
            {
                return rowId;
            }
            this.db.Execute("UPDATE " + SqliteDatabase.Quote(table.Name) + " SET " + SqliteDatabase.Quote(column.Name) + " = ? WHERE rowid = ?", newValue, rowId);
            long newRowId = (column == table.PrimaryKey) ? Convert.ToInt64(newValue) : rowId;
            this.Record(table.Name, newRowId, column.Name, oldValue, newValue);
            return newRowId;
        }

        // Copies a row; the copy gets the next free id. Returns its rowid.
        public long DuplicateRow(string tableName, long rowId)
        {
            this.CheckWritable();
            Table table = this.GetTable(tableName);
            List<string> columns = new List<string>();
            foreach (Column column in table.Columns)
            {
                if (column != table.PrimaryKey)
                {
                    columns.Add(SqliteDatabase.Quote(column.Name));
                }
            }
            string list = string.Join(", ", columns.ToArray());
            string name = SqliteDatabase.Quote(table.Name);
            // A table without a key (FM2, DLC patches) whose first column is a unique whole-number id: the copy
            // gets max + 1 there, as a keyed table would, instead of a second row with the same id.
            Column first = (table.PrimaryKey == null && table.Columns.Count > 0) ? table.Columns[0] : null;
            if (first != null && first.Kind == ValueKind.Integer)
            {
                string id = SqliteDatabase.Quote(first.Name);
                object[] stats = this.db.Query("SELECT count(*), count(DISTINCT " + id + "), max(" + id + ") FROM " + name).Rows[0];
                if (stats[2] is long && Convert.ToInt64(stats[0]) == Convert.ToInt64(stats[1]))
                {
                    string select = "? AS " + id + list.Substring(id.Length);
                    this.db.Execute("INSERT INTO " + name + " (" + list + ") SELECT " + select + " FROM " + name + " WHERE rowid = ?", (long) stats[2] + 1, rowId);
                    long added = this.db.LastInsertRowId;
                    this.Record(table.Name, added, null, null, "copy of #" + rowId);
                    return added;
                }
            }
            this.db.Execute("INSERT INTO " + name + " (" + list + ") SELECT " + list + " FROM " + name + " WHERE rowid = ?", rowId);
            long copy = this.db.LastInsertRowId;
            this.Record(table.Name, copy, null, null, "copy of #" + rowId);
            return copy;
        }

        // Inserts a copy of a row with some columns replaced (the new rows of the creation tools keep
        // every other value of a real game row). Returns the new rowid.
        public long CopyRow(string tableName, long templateRowId, Dictionary<string, object> values, string description)
        {
            this.CheckWritable();
            Table table = this.GetTable(tableName);
            List<string> columns = new List<string>();
            List<string> select = new List<string>();
            List<object> args = new List<object>();
            foreach (Column column in table.Columns)
            {
                object value;
                bool replaced = false;
                foreach (KeyValuePair<string, object> pair in values)
                {
                    if (string.Equals(pair.Key, column.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        replaced = true;
                    }
                }
                if (column == table.PrimaryKey && !replaced)
                {
                    continue;   // a new id is picked by SQLite
                }
                columns.Add(SqliteDatabase.Quote(column.Name));
                if (replaced)
                {
                    value = null;
                    foreach (KeyValuePair<string, object> pair in values)
                    {
                        if (string.Equals(pair.Key, column.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            value = Normalize(column, pair.Value);
                        }
                    }
                    select.Add("?");
                    args.Add(value);
                }
                else
                {
                    select.Add(SqliteDatabase.Quote(column.Name));
                }
            }
            args.Add(templateRowId);
            string name = SqliteDatabase.Quote(table.Name);
            this.db.Execute("INSERT INTO " + name + " (" + string.Join(", ", columns.ToArray()) + ") SELECT " + string.Join(", ", select.ToArray()) + " FROM " + name + " WHERE rowid = ?", args.ToArray());
            long rowId = this.db.LastInsertRowId;
            this.Record(table.Name, rowId, null, null, description);
            return rowId;
        }

        // Several columns of one row in one change (a whole torque curve, for example).
        public void UpdateColumns(string tableName, long rowId, Dictionary<string, object> values, string description)
        {
            this.CheckWritable();
            Table table = this.GetTable(tableName);
            List<string> sets = new List<string>();
            List<object> args = new List<object>();
            foreach (KeyValuePair<string, object> pair in values)
            {
                Column column = table.Find(pair.Key);
                sets.Add(SqliteDatabase.Quote(column.Name) + " = ?");
                args.Add(Normalize(column, pair.Value));
            }
            args.Add(rowId);
            this.db.Execute("UPDATE " + SqliteDatabase.Quote(table.Name) + " SET " + string.Join(", ", sets.ToArray()) + " WHERE rowid = ?", args.ToArray());
            this.Record(table.Name, rowId, null, null, description);
        }

        // The largest value of a column (optionally filtered), or fallback.
        public long Max(string table, string column, string where, long fallback)
        {
            if (!this.HasTable(table))
            {
                return fallback;
            }
            object value = this.db.Scalar("SELECT max(" + SqliteDatabase.Quote(column) + ") FROM " + SqliteDatabase.Quote(table) + (string.IsNullOrEmpty(where) ? "" : " WHERE " + where));
            return (value is long) ? (long) value : (value is double ? (long) (double) value : fallback);
        }

        // The Id for a new row of a table (DbIds: its reserved range, else the next after the largest).
        public long NextId(string table, string column)
        {
            return DbIds.Next(this.db, table, column);
        }

        // One typed SQL statement on any table (SqlGuard: the Id rule, only SELECT / INSERT / UPDATE / DELETE).
        // A write is listed with the changes, so File > Save writes it.
        // The diagnostic queries of the SQL console, built from this database.
        public List<SqlDiagnostics.Query> Diagnostics()
        {
            return SqlDiagnostics.For(this.db, ForzaGame.Motorsport4);
        }

        // A diagnostic Forza Studio builds in code (SqlDiagnostics.Query.Build), with the game's names.
        public SqliteDatabase.Result Build(SqlDiagnostics.Builder build)
        {
            StringTable names = null;
            try
            {
                names = this.Strings;
            }
            catch (Exception)
            {
            }
            return build(this.db, names);
        }

        // What a statement would do, per table, without doing it (SqlGuard.Plan).
        public SqlGuard.Outcome PlanSql(string sql)
        {
            return SqlGuard.Plan(this.db, sql, ForzaGame.Motorsport4);
        }

        public SqlGuard.Outcome RunSql(string sql)
        {
            this.CheckWritable();
            SqlGuard.Outcome outcome = SqlGuard.Run(this.db, sql, ForzaGame.Motorsport4);
            if (outcome.Table != null)
            {
                this.Record(outcome.Table, 0, null, null, "SQL (" + outcome.Changed + " rows): " + sql.Trim());
            }
            return outcome;
        }

        public void DeleteRow(string tableName, long rowId)
        {
            this.CheckWritable();
            this.db.Execute("DELETE FROM " + SqliteDatabase.Quote(tableName) + " WHERE rowid = ?", rowId);
            this.Record(tableName, rowId, null, null, "deleted");
        }

        private void Record(string table, long rowId, string column, object oldValue, object newValue)
        {
            Change change = new Change();
            change.Table = table;
            change.RowId = rowId;
            change.Column = column;
            change.OldValue = oldValue;
            change.NewValue = newValue;
            this.Changes.Add(change);
            // Names shown in the lists may have changed.
            this.lookups.Clear();
            this.flags.Clear();
        }

        #endregion

        #region Lookups

        // The list an id column picks from, or null for a plain value.
        public Lookup GetLookup(string tableName, string columnName)
        {
            string key = LookupKey(tableName, columnName) ?? this.RelationLookupKey(tableName, columnName);
            if (key == null)
            {
                return this.IsFlag(tableName, columnName) ? YesNo : null;
            }
            Lookup lookup;
            if (!this.lookups.TryGetValue(key, out lookup))
            {
                lookup = this.BuildLookup(key);
                this.lookups[key] = lookup;
            }
            return lookup;
        }

        private GameDatabase reference;
        private StringTable strings;

        // The game's localized text (null when the string tables are not found).
        public StringTable Strings
        {
            get { return this.strings; }
            set
            {
                this.strings = value;
                this.lookups.Clear();
                if (this.reference != null)
                {
                    this.reference.Strings = value;
                }
            }
        }

        // The localized text of a "_&<id>" value, or null.
        public string Text(string table, string column, object value)
        {
            return (this.strings == null || table == null) ? null : this.strings.Lookup(table, column, value as string);
        }

        // "Supra RZ   [_&3444126252]" for a localized value, the value itself otherwise.
        public string DisplayText(string table, string column, object value)
        {
            string text = this.Text(table, column, value);
            return (text != null) ? text + "   [" + value + "]" : null;
        }

        // The main gamedb.slt, used for names when this one is a partial database (a DLC patch has its
        // new cars but not the makes, classes or tracks they point to).
        public GameDatabase Reference
        {
            get { return this.reference; }
            set
            {
                this.reference = value;
                if (value != null)
                {
                    value.strings = this.strings;
                }
                this.lookups.Clear();
            }
        }

        // A list by name ("make.short", "class.short", "track.short", "make.icon", "car", "engine"...).
        public Lookup GetNamedLookup(string key)
        {
            Lookup lookup;
            if (!this.lookups.TryGetValue(key, out lookup))
            {
                lookup = this.BuildLookup(key);
                this.lookups[key] = lookup;
            }
            return lookup;
        }

        public string LookupName(string key, object id)
        {
            return (id is long) ? this.GetNamedLookup(key).Name((long) id) : null;
        }

        // This database's rows first, then the reference database's.
        private Lookup BuildLookup(string key)
        {
            Lookup lookup = new Lookup();
            string sql = null;
            string table = LookupTable(key);
            string[] relation = key.StartsWith(RelationPrefix, StringComparison.Ordinal) ? key.Substring(RelationPrefix.Length).Split('|') : null;
            if (relation != null)
            {
                table = relation[0];
            }
            foreach (GameDatabase source in new GameDatabase[] { this, this.reference })
            {
                if (source != null && relation != null && source.HasTable(table))
                {
                    sql = RelationLookupSql(source, relation[0], relation[1]);
                }
                else if (source != null && relation == null)
                {
                    sql = LookupSql(source, key);
                }
                if (source == null || sql == null || (table != null && !source.HasTable(table)))
                {
                    continue;
                }
                try
                {
                    SqliteDatabase.Result result = source.db.Query(sql);
                    foreach (object[] row in result.Rows)
                    {
                        if (row[0] == null)
                        {
                            continue;
                        }
                        // Columns after the name are localized text ids, shown first when known.
                        string name = Convert.ToString(row[1], CultureInfo.InvariantCulture);
                        List<string> texts = new List<string>();
                        for (int k = 2; k < row.Length; k++)
                        {
                            string text = this.Text(table, result.Columns[k], row[k]);
                            if (!string.IsNullOrEmpty(text))
                            {
                                texts.Add(text);
                            }
                        }
                        // No name and no text found (FM2 keeps part makers as plain text): the first plain value.
                        for (int k = 2; k < row.Length && name.Length == 0 && texts.Count == 0; k++)
                        {
                            string plain = row[k] as string;
                            if (!string.IsNullOrEmpty(plain) && !plain.StartsWith("_&", StringComparison.Ordinal))
                            {
                                name = plain;
                            }
                        }
                        string joined = string.Join(" - ", texts.ToArray());
                        lookup.Add(Convert.ToInt64(row[0]), (texts.Count == 0) ? name : (name.Length == 0 ? joined : joined + "  ·  " + name));
                    }
                }
                catch (InvalidOperationException)
                {
                }
            }
            return lookup;
        }

        private static string LookupTable(string key)
        {
            switch (key)
            {
                case "make":
                case "make.short":
                case "make.icon":
                    return "List_CarMake";
                case "class":
                case "class.short":
                    return "CarClasses";
                case "placement":
                    return "List_EnginePlacement";
                case "drivetype":
                    return "List_DriveType";
                case "powertrain":
                    return "Powertrains";
                case "material":
                    return "List_MaterialType";
                case "engineconfig":
                    return "List_EngineConfig";
                case "cylinders":
                    return "List_Cylinders";
                case "aspiration":
                    return "List_Aspiration";
                case "timing":
                    return "List_VariableTiming";
                case "country":
                    return "List_Country";
                case "track":
                case "track.short":
                    return "Tracks";
                case "environment":
                    return "Environments";
                case "car":
                    return "Data_Car";
                case "engine":
                    return "Data_Engine";
                case "motor":
                    return "Data_Motor";
                case "tirecompound":
                    return "List_TireCompound";
                case "wheel":
                    return "List_Wheels";
                case "brake":
                    return "List_BrakeType";
                case "partsstring":
                    return "List_PartsStrings";
                case "pitting":
                    return "PittingProfiles";
                case "suspensiontype":
                    return "List_SuspensionPhysicsType";
                case "colorclass":
                    return "List_ColorClass";
                case "partmaker":
                    return "List_PartManufacturer";
            }
            return null;
        }

        // The color of each class badge (letter -> color), as the game draws them.
        public Dictionary<string, System.Drawing.Color> ClassColors()
        {
            Dictionary<string, System.Drawing.Color> colors = new Dictionary<string, System.Drawing.Color>(StringComparer.OrdinalIgnoreCase);
            foreach (GameDatabase source in new GameDatabase[] { this, this.reference })
            {
                if (source == null || !source.HasTable("CarClasses"))
                {
                    continue;
                }
                foreach (object[] row in source.db.Query("SELECT " + source.ClassLetterSql() + ", ClassColor FROM CarClasses").Rows)
                {
                    string letter = row[0] as string;
                    if (letter != null && row[1] is long && !colors.ContainsKey(letter))
                    {
                        colors[letter] = System.Drawing.Color.FromArgb(255, System.Drawing.Color.FromArgb((int) (long) row[1]));
                    }
                }
            }
            return colors;
        }

        // The engines of a car (stock first, then swaps): {EngineID, 1 if stock}.
        public List<long[]> CarEngineIds(long carId)
        {
            List<long[]> engines = new List<long[]>();
            string[] sources = { "SELECT EngineID, IsStock FROM List_UpgradeEngine WHERE Ordinal = ?", "SELECT EngineID, Stock FROM Combo_Engines WHERE Ordinal = ?" };
            string[] tables = { "List_UpgradeEngine", "Combo_Engines" };
            for (int i = 0; i < sources.Length; i++)
            {
                if (!this.HasTable(tables[i]))
                {
                    continue;
                }
                foreach (object[] row in this.db.Query(sources[i], carId).Rows)
                {
                    if (!(row[0] is long))
                    {
                        continue;
                    }
                    long stock = (row[1] is long && (long) row[1] != 0) ? 1 : 0;
                    long[] found = engines.Find(delegate(long[] e) { return e[0] == (long) row[0]; });
                    if (found == null)
                    {
                        engines.Add(new long[] { (long) row[0], stock });
                    }
                    else
                    {
                        found[1] = Math.Max(found[1], stock);
                    }
                }
            }
            engines.Sort(delegate(long[] a, long[] b) { return (a[1] != b[1]) ? b[1].CompareTo(a[1]) : a[0].CompareTo(b[0]); });
            return engines;
        }

        // Cars using an engine, in this database and in the reference one.
        public List<string> EngineUsers(long engineId)
        {
            List<string> cars = new List<string>();
            foreach (GameDatabase source in new GameDatabase[] { this, this.reference })
            {
                if (source == null)
                {
                    continue;
                }
                foreach (string table in new string[] { "List_UpgradeEngine", "Combo_Engines" })
                {
                    if (!source.HasTable(table))
                    {
                        continue;
                    }
                    foreach (object[] row in source.db.Query("SELECT DISTINCT Ordinal FROM " + table + " WHERE EngineID = ?", engineId).Rows)
                    {
                        string name = this.LookupName("car", row[0]) ?? GameDatabase.FormatValue(row[0]);
                        if (!cars.Contains(name))
                        {
                            cars.Add(name);
                        }
                    }
                }
            }
            cars.Sort(StringComparer.OrdinalIgnoreCase);
            return cars;
        }

        private static readonly Lookup YesNo = CreateYesNo();

        private static Lookup CreateYesNo()
        {
            Lookup lookup = new Lookup();
            lookup.Add(0, "No");
            lookup.Add(1, "Yes");
            return lookup;
        }

        // Is*/Has* style 0/1 columns, recognized only if every value really is 0 or 1.
        private bool IsFlag(string tableName, string columnName)
        {
            string name = columnName;
            bool looksLikeFlag = (name.StartsWith("Is") && name.Length > 2 && char.IsUpper(name[2])) || (name.StartsWith("Has") && name.Length > 3 && char.IsUpper(name[3]))
                || name.StartsWith("Allow") || (name.StartsWith("No") && name.Length > 2 && char.IsUpper(name[2])) || name == "Stock" || name == "Carbureted" || name == "Diesel" || name == "Rotary" || name == "Active" || name == "Renderable";
            if (!looksLikeFlag)
            {
                return false;
            }
            Table table = this.GetTable(tableName);
            Column column = (table == null) ? null : table.Find(columnName);
            if (column == null || column.Kind != ValueKind.Integer || column.IsPrimaryKey)
            {
                return false;
            }
            string key = tableName + "." + columnName;
            bool flag;
            if (!this.flags.TryGetValue(key, out flag))
            {
                flag = Convert.ToInt64(this.db.Scalar("SELECT count(*) FROM " + SqliteDatabase.Quote(tableName) + " WHERE " + SqliteDatabase.Quote(columnName) + " NOT IN (0, 1)")) == 0;
                this.flags[key] = flag;
            }
            return flag;
        }

        private static string LookupKey(string table, string column)
        {
            string c = column.ToLowerInvariant();
            string t = table.ToLowerInvariant();
            switch (c)
            {
                case "makeid":
                    return "make";
                case "classid":
                case "targetclass":
                    return "class";
                case "engineplacementid":
                    return "placement";
                case "drivetypeid":
                    return "drivetype";
                case "powertrainid":
                    return "powertrain";
                case "materialtypeid":
                    return "material";
                case "engineconfigid":
                    return "engineconfig";
                case "configid":
                    return (t == "data_engine") ? "engineconfig" : null;
                case "cylinderid":
                    return "cylinders";
                case "aspirationtypeid":
                case "aspirationid_stock":
                    return "aspiration";
                case "variabletimingid":
                    return "timing";
                case "countryid":
                    return "country";
                case "trackid":
                case "tracks_id":
                    return "track";
                case "environmentid":
                    return "environment";
                case "ordinal":
                case "carid":
                case "rewardcarid":
                    return "car";
                case "engineid":
                    return (t == "data_engine") ? null : "engine";
                case "motorid":
                    return (t == "data_motor") ? null : "motor";
                case "tirecompoundid":
                    return (t == "list_tirecompound") ? null : "tirecompound";
                case "stockwheelid":
                    return "wheel";
                case "frontbraketypeid":
                case "rearbraketypeid":
                    return "brake";
                case "partsstringid":
                    return "partsstring";
                case "pittingprofileid":
                case "mppitprofile":
                    return "pitting";
                case "suspensionphysicstypeid":
                    return "suspensiontype";
                case "colorclassid":
                    return "colorclass";
                case "partmanufacturerid":
                case "manufacturerid":
                    // Engine and drivetrain swaps carry the car's make, the other upgrades a parts maker.
                    return (t == "list_upgradeengine" || t == "list_upgradedrivetrain") ? "make" : "partmaker";
            }
            return null;
        }

        // The make's name part of its logo: FM4/FH have it as IconPathBase, FM2/FM3 only inside IconPath
        // (...\L_MAN_AstonMartin_Logo.tga), else the three-letter code.
        private static string MakeNameSql(GameDatabase source)
        {
            if (source.HasColumn("List_CarMake", "IconPathBase"))
            {
                return "coalesce(IconPathBase, ManufacturerCode)";
            }
            return "CASE WHEN instr(IconPath, 'L_MAN_') > 0 AND instr(lower(IconPath), '_logo.') > instr(IconPath, 'L_MAN_') "
                + "THEN substr(IconPath, instr(IconPath, 'L_MAN_') + 6, instr(lower(IconPath), '_logo.') - instr(IconPath, 'L_MAN_') - 6) ELSE ManufacturerCode END";
        }

        // The class letter: FM3/FM4/FH have BadgeTexturePathPrefix (CLASS_R2), FM2 only the badge picture (...\A_CLS-R2.tga).
        public string ClassLetterSql()
        {
            if (this.HasColumn("CarClasses", "BadgeTexturePathPrefix"))
            {
                return "replace(BadgeTexturePathPrefix, 'CLASS_', '')";
            }
            if (this.HasColumn("CarClasses", "BadgePath"))
            {
                return "replace(replace(substr(BadgePath, instr(BadgePath, 'A_CLS-') + 6), '.tga', ''), '.TGA', '')";
            }
            return "CAST(Id AS TEXT)";
        }

        private static string LookupSql(GameDatabase source, string key)
        {
            switch (key)
            {
                case "make":
                    return "SELECT ID, " + MakeNameSql(source) + " || ' (' || ManufacturerCode || ')' FROM List_CarMake ORDER BY 2";
                case "make.short":
                    return "SELECT ID, " + MakeNameSql(source) + " FROM List_CarMake";
                case "make.icon":
                    return "SELECT ID, IconPath FROM List_CarMake";
                case "class.short":
                    return "SELECT Id, " + source.ClassLetterSql() + " FROM CarClasses";
                case "track.short":
                    return "SELECT id, MediaName FROM Tracks";
                case "class":
                    return "SELECT Id, " + source.ClassLetterSql() + " || ' (PI up to ' || MaxDisplayPerformanceIndex || ')' FROM CarClasses ORDER BY Id";
                case "placement":
                    return "SELECT ID, EnginePlacement FROM List_EnginePlacement ORDER BY ID";
                case "drivetype":
                    return "SELECT ID, DriveType FROM List_DriveType ORDER BY ID";
                case "powertrain":
                    return "SELECT p.Id, coalesce(d.DriveType, '?') || ' ' || coalesce(e.EnginePlacement, '?') FROM Powertrains p LEFT JOIN List_DriveType d ON d.ID = p.DrivetrainId LEFT JOIN List_EnginePlacement e ON e.ID = p.EnginePlacementId ORDER BY p.Id";
                case "material":
                    return "SELECT MaterialTypeID, Material FROM List_MaterialType ORDER BY MaterialTypeID";
                case "engineconfig":
                    return "SELECT ConfigID, EngineConfig FROM List_EngineConfig ORDER BY ConfigID";
                case "cylinders":
                    return "SELECT CylinderID, Number || CASE WHEN Rotors <> 0 THEN ' rotors' ELSE ' cylinders' END FROM List_Cylinders ORDER BY CylinderID";
                case "aspiration":
                    return "SELECT AspirationID, Aspiration FROM List_Aspiration ORDER BY AspirationID";
                case "timing":
                    return "SELECT VariableTimingID, VariableTimingType FROM List_VariableTiming ORDER BY VariableTimingID";
                case "country":
                    return "SELECT CountryID, replace(replace(substr(IconPath, instr(IconPath, 'L_FLA_') + 6), '_flag.tga', ''), '_', ' ') FROM List_Country ORDER BY CountryID";
                case "track":
                    // FM2/FM3 have no IsReverse column.
                    return "SELECT id, MediaName || ' (' || Length || ' m' || " + (source.HasColumn("Tracks", "IsReverse") ? "CASE WHEN IsReverse = 1 THEN ', reverse' ELSE '' END" : "''")
                        + " || ')', ShortDisplayName, DisplayName FROM Tracks ORDER BY MediaName, id";
                case "environment":
                    return "SELECT Id, MediaName FROM Environments ORDER BY MediaName";
                case "car":
                    return "SELECT Id, MediaName, ModelShort FROM Data_Car ORDER BY MediaName";
                case "engine":
                    // FM2's EngineName is an empty string.
                    return "SELECT EngineID, coalesce(nullif(EngineName, ''), MediaName) FROM Data_Engine ORDER BY EngineID";
                case "motor":
                    return "SELECT MotorID, coalesce(MotorName, MediaName) FROM Data_Motor ORDER BY MotorID";
                case "tirecompound":
                    return "SELECT TireCompoundID, DisplayName FROM List_TireCompound ORDER BY TireCompoundID";
                case "wheel":
                    return "SELECT ID, MediaName FROM List_Wheels ORDER BY MediaName";
                case "brake":
                    return "SELECT BrakeTypeID, BrakeType FROM List_BrakeType ORDER BY BrakeTypeID";
                case "partsstring":
                    return "SELECT Id, PartsString FROM List_PartsStrings ORDER BY Id";
                case "pitting":
                    return "SELECT id, Name FROM PittingProfiles ORDER BY id";
                case "suspensiontype":
                    return "SELECT SuspensionPhysicsTypeID, Name FROM List_SuspensionPhysicsType ORDER BY SuspensionPhysicsTypeID";
                case "colorclass":
                    return "SELECT ColorClassId, ColorClass FROM List_ColorClass ORDER BY ColorClassId";
                case "partmaker":
                    return "SELECT Id, '', PartManufacturer FROM List_PartManufacturer ORDER BY Id";
            }
            return null;
        }

        #endregion

        #region Relations

        private const string RelationPrefix = "rel:";

        // A literal for SQL built from a value read from the database.
        public static string SqlLiteral(object value)
        {
            if (value == null || value is DBNull)
            {
                return "NULL";
            }
            if (value is string)
            {
                return "'" + ((string) value).Replace("'", "''") + "'";
            }
            return FormatValue(value);
        }

        public bool HasColumn(string table, string column)
        {
            Table found = this.GetTable(table);
            return found != null && found.Find(column) != null;
        }

        // The relation of a column (GameDbSchema), if both ends are in this database (or the target is in the reference one).
        public GameDbSchema.Relation RelationOf(string table, string column)
        {
            GameDbSchema.Relation relation = GameDbSchema.Find(table, column);
            if (relation == null)
            {
                return null;
            }
            foreach (string name in relation.Columns)
            {
                if (!this.HasColumn(table, name))
                {
                    return null;
                }
            }
            bool here = this.HasColumn(relation.Target, relation.TargetColumns[0]);
            bool inReference = this.reference != null && this.reference.HasColumn(relation.Target, relation.TargetColumns[0]);
            return (here || inReference) ? relation : null;
        }

        // Ids of another table get a list of names when that table has something to show.
        private string RelationLookupKey(string table, string column)
        {
            GameDbSchema.Relation relation = this.RelationOf(table, column);
            Table source = this.GetTable(table);
            if (relation == null || relation.IsComposite || source.Find(column).Kind != ValueKind.Integer)
            {
                return null;
            }
            GameDatabase owner = this.HasTable(relation.Target) ? this : this.reference;
            return (NameColumns(owner, relation.Target).Count == 0) ? null : RelationPrefix + relation.Target + "|" + relation.TargetColumn;
        }

        private readonly Dictionary<string, List<string>> nameColumns = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // The text columns that name a row: the plain name (or null), then up to two localized ones
        // ("_&id"). Empty when the table has none. Paths and long descriptions are left out.
        private static List<string> NameColumns(GameDatabase source, string tableName)
        {
            List<string> names;
            if (!source.nameColumns.TryGetValue(tableName, out names))
            {
                names = FindNameColumns(source, tableName);
                source.nameColumns[tableName] = names;
            }
            return names;
        }

        private static List<string> FindNameColumns(GameDatabase source, string tableName)
        {
            string plain = null;
            List<string> localized = new List<string>();
            Table table = source.GetTable(tableName);
            foreach (Column column in table.Columns)
            {
                string c = column.Name;
                if (column.Kind != ValueKind.Text || c.EndsWith("Path") || c.Contains("Image") || c.Contains("Icon") || c.Contains("Thumb") || c.StartsWith("Logo")
                    || c.Contains("Description") || c == "Bio" || c == "Query" || c == "Clause" || c == "Token")
                {
                    continue;
                }
                object sample = source.db.Scalar("SELECT " + SqliteDatabase.Quote(c) + " FROM " + SqliteDatabase.Quote(tableName) + " WHERE " + SqliteDatabase.Quote(c) + " <> '' LIMIT 1");
                string text = sample as string;
                if (text == null || GamePaths.IsGamePath(text))
                {
                    continue;
                }
                if (text.StartsWith("_&"))
                {
                    if (localized.Count < 2)
                    {
                        localized.Add(c);
                    }
                }
                else if (plain == null)
                {
                    plain = c;
                }
            }
            List<string> names = new List<string>();
            if (plain != null || localized.Count > 0)
            {
                names.Add(plain);
                names.AddRange(localized);
            }
            return names;
        }

        private static string RelationLookupSql(GameDatabase source, string table, string key)
        {
            List<string> names = NameColumns(source, table);
            if (names.Count == 0)
            {
                return null;
            }
            List<string> columns = new List<string>();
            columns.Add(SqliteDatabase.Quote(key));
            columns.Add((names[0] == null) ? "''" : "coalesce(" + SqliteDatabase.Quote(names[0]) + ", '')");
            for (int i = 1; i < names.Count; i++)
            {
                columns.Add(SqliteDatabase.Quote(names[i]));
            }
            return "SELECT " + string.Join(", ", columns.ToArray()) + " FROM " + SqliteDatabase.Quote(table) + " ORDER BY " + SqliteDatabase.Quote(key);
        }

        // WHERE for the rows of relation.Table that point at the row with these values (of relation.Target), or null.
        public static string PointingAt(GameDbSchema.Relation relation, Table target, object[] row)
        {
            string[] columns = relation.Columns;
            string[] targetColumns = relation.TargetColumns;
            List<string> parts = new List<string>();
            for (int i = 0; i < columns.Length; i++)
            {
                Column column = target.Find(targetColumns[i]);
                object value = (column == null) ? null : row[target.Columns.IndexOf(column)];
                if (value == null || (value is long && GameDbSchema.IsNone((long) value)))
                {
                    return null;
                }
                parts.Add(SqliteDatabase.Quote(columns[i]) + " = " + SqlLiteral(value));
            }
            return string.Join(" AND ", parts.ToArray());
        }

        // Every row of the other tables that holds this row's id: what would break if it went away.
        public List<Related> ReferencesTo(string tableName, object[] row)
        {
            List<Related> list = new List<Related>();
            Table table = this.GetTable(tableName);
            if (table == null || row == null)
            {
                return list;
            }
            foreach (GameDbSchema.Relation relation in GameDbSchema.Into(table.Name))
            {
                if (this.RelationOf(relation.Table, relation.Column) == null)
                {
                    continue;
                }
                string where = PointingAt(relation, table, row);
                if (where != null)
                {
                    list.Add(new Related(relation.Table + "." + relation.Column, relation.Table, where));
                }
            }
            return this.Counted(list);
        }

        // The rows of a relation whose id is not in the target table.
        public class Orphans
        {
            public GameDbSchema.Relation Relation;
            public int Rows;
            public string Values;
            public string Where;
        }

        public static string DanglingWhere(GameDbSchema.Relation relation)
        {
            string[] columns = relation.Columns;
            string[] targetColumns = relation.TargetColumns;
            List<string> set = new List<string>();
            List<string> match = new List<string>();
            for (int i = 0; i < columns.Length; i++)
            {
                string column = SqliteDatabase.Quote(relation.Table) + "." + SqliteDatabase.Quote(columns[i]);
                set.Add(column + " IS NOT NULL AND " + column + " <> '' AND " + column + " NOT IN (0, -1)");
                match.Add("t." + SqliteDatabase.Quote(targetColumns[i]) + " = " + column);
            }
            return string.Join(" AND ", set.ToArray()) + " AND NOT EXISTS (SELECT 1 FROM " + SqliteDatabase.Quote(relation.Target) + " t WHERE " + string.Join(" AND ", match.ToArray()) + ")";
        }

        // FM4's layout: the strict/loose marks of GameDbSchema were measured on the original FM4
        // database. FM3 lacks the electric motors; FH1/FH2 add the event hubs and keep stale FM4 rows.
        public bool IsFm4Layout
        {
            get { return this.HasTable("Data_Motor") && !this.HasTable("EventHubs"); }
        }

        // Checks every relation whose two tables are in this database. Returns the ones with dangling ids.
        public List<Orphans> CheckReferences(out int checkedRelations)
        {
            List<Orphans> found = new List<Orphans>();
            checkedRelations = 0;
            foreach (GameDbSchema.Relation relation in GameDbSchema.Relations)
            {
                if (this.RelationOf(relation.Table, relation.Column) == null || !this.HasColumn(relation.Target, relation.TargetColumns[0]))
                {
                    continue;
                }
                checkedRelations++;
                string where = DanglingWhere(relation);
                string column = SqliteDatabase.Quote(relation.Table) + "." + SqliteDatabase.Quote(relation.Columns[0]);
                long rows = Convert.ToInt64(this.db.Scalar("SELECT count(*) FROM " + SqliteDatabase.Quote(relation.Table) + " WHERE " + where));
                if (rows == 0)
                {
                    continue;
                }
                List<string> values = new List<string>();
                foreach (object[] value in this.db.Query("SELECT DISTINCT " + column + " FROM " + SqliteDatabase.Quote(relation.Table) + " WHERE " + where + " ORDER BY 1 LIMIT 12").Rows)
                {
                    values.Add(FormatValue(value[0]));
                }
                Orphans orphans = new Orphans();
                orphans.Relation = relation;
                orphans.Rows = (int) rows;
                orphans.Values = string.Join(", ", values.ToArray());
                orphans.Where = where;
                found.Add(orphans);
            }
            return found;
        }

        #endregion

        #region Game structure

        // Data_Car fields grouped like a car sheet. The digits keep the groups in this order.
        public static string CarCategory(string column)
        {
            string c = column;
            if (c == "Id" || c == "Year" || c == "MakeID" || c == "MakeName" || c == "MakeIcon" || c == "DisplayName" || c == "ModelShort" || c == "MediaName" || c == "ModelId"
                || c.StartsWith("Family") || c == "CountryID" || c == "RegionID" || c == "Thumbnail" || c == "ContentId" || c == "CarTypeID")
            {
                return "1  Identity";
            }
            if (c == "ClassID" || c == "CarClassModifier" || c == "PerformanceIndex" || c == "BaseCost" || c == "BaseRarity" || c.EndsWith("Rating"))
            {
                return "2  Class, price and ratings";
            }
            if (c.StartsWith("Is") || c.StartsWith("Has") || c == "WingMask" || c == "GaugeID" || c == "AIPlayerID")
            {
                return "3  Availability and flags";
            }
            if (c == "CurbWeight" || c == "WeightDistribution" || c == "EnginePlacementID" || c == "MaterialTypeID" || c == "PowertrainID" || c == "DriveTypeID" || c == "NumGears"
                || c == "EngineConfigID" || c == "CylinderID" || c == "AspirationTypeId" || c == "Displacement" || c == "SpeedLimiterID" || c == "GameTorqueScale" || c == "GameDragScale")
            {
                return "4  Weight and powertrain";
            }
            if (c.StartsWith("Time:") || c.StartsWith("QuarterMile") || c.StartsWith("TopSpeed") || c.StartsWith("Sim"))
            {
                return "5  Performance figures";
            }
            if (c.Contains("Tire") || c.Contains("Wheel") || c.StartsWith("Caliper") || c.Contains("RideHeight"))
            {
                return "6  Tires, wheels and brakes";
            }
            if (c.StartsWith("Steer") || c.StartsWith("AutoSteer") || c.StartsWith("Assists") || c.StartsWith("FixListing"))
            {
                return "7  Steering and assists";
            }
            if (c.StartsWith("BodyAero"))
            {
                return "8  Aerodynamics";
            }
            return "9  Other";
        }

        // Other tables: names and paths first, then values.
        public static string GenericCategory(Column column, Lookup lookup)
        {
            string c = column.Name;
            if (column.IsPrimaryKey || c.EndsWith("Name") || c == "MediaName" || c.EndsWith("Path") || c == "Description" || c == "Logo" || c == "LogoSmall" || c.EndsWith("Image") || c.EndsWith("ImageName"))
            {
                return "1  Identity";
            }
            if (lookup != null && lookup == YesNo)
            {
                return "3  Flags";
            }
            return "2  Values";
        }

        // Everything that belongs to one car. In FM4 a car's id is called Ordinal in the other tables;
        // body, drivetrain and engine parts hang off the CarBodyID, DrivetrainID and EngineID of its
        // upgrade rows (engine and drivetrain swaps included).
        public List<Related> CarUpgrades(long carId)
        {
            string car = carId.ToString(CultureInfo.InvariantCulture);
            string bodies = this.IdList("SELECT CarBodyID FROM List_UpgradeCarBody WHERE Ordinal = ?", carId);
            string drivetrains = this.IdList("SELECT DrivetrainID FROM List_UpgradeDrivetrain WHERE Ordinal = ?", carId);
            string engines = this.CarEngines(carId);
            string motors = this.HasTable("List_UpgradeMotor") ? this.IdList("SELECT MotorID FROM List_UpgradeMotor WHERE Ordinal = ?", carId) : "NULL";
            List<Related> list = new List<Related>();
            Add(list, "Engine swaps", "List_UpgradeEngine", "Ordinal = " + car);
            Add(list, "Engine: camshaft", "List_UpgradeEngineCamshaft", "EngineID IN (" + engines + ")");
            Add(list, "Engine: displacement", "List_UpgradeEngineDisplacement", "EngineID IN (" + engines + ")");
            Add(list, "Engine: intake", "List_UpgradeEngineIntake", "EngineID IN (" + engines + ")");
            Add(list, "Engine: fuel system", "List_UpgradeEngineFuelSystem", "EngineID IN (" + engines + ")");
            Add(list, "Engine: ignition", "List_UpgradeEngineIgnition", "EngineID IN (" + engines + ")");
            Add(list, "Engine: exhaust", "List_UpgradeEngineExhaust", "EngineID IN (" + engines + ")");
            Add(list, "Engine: valves", "List_UpgradeEngineValves", "EngineID IN (" + engines + ")");
            Add(list, "Engine: pistons / compression", "List_UpgradeEnginePistonsCompression", "EngineID IN (" + engines + ")");
            Add(list, "Engine: manifold", "List_UpgradeEngineManifold", "EngineID IN (" + engines + ")");
            Add(list, "Engine: flywheel", "List_UpgradeEngineFlywheel", "EngineID IN (" + engines + ")");
            Add(list, "Engine: oil cooling", "List_UpgradeEngineOilCooling", "EngineID IN (" + engines + ")");
            Add(list, "Engine: intercooler", "List_UpgradeEngineIntercooler", "EngineID IN (" + engines + ")");
            Add(list, "Engine: single turbo", "List_UpgradeEngineTurboSingle", "EngineID IN (" + engines + ")");
            Add(list, "Engine: twin turbo", "List_UpgradeEngineTurboTwin", "EngineID IN (" + engines + ")");
            Add(list, "Engine: quad turbo", "List_UpgradeEngineTurboQuad", "EngineID IN (" + engines + ")");
            Add(list, "Engine: centrifugal supercharger", "List_UpgradeEngineCSC", "EngineID IN (" + engines + ")");
            Add(list, "Engine: positive displacement supercharger", "List_UpgradeEngineDSC", "EngineID IN (" + engines + ")");
            Add(list, "Engine: restrictor plate", "List_UpgradeEngineRestrictorPlate", "EngineID IN (" + engines + ")");
            Add(list, "Electric motor", "List_UpgradeMotor", "Ordinal = " + car);
            Add(list, "Electric motor parts", "List_UpgradeMotorParts", "MotorID IN (" + motors + ")");
            Add(list, "Drivetrain swaps", "List_UpgradeDrivetrain", "Ordinal = " + car);
            Add(list, "Drivetrain: clutch", "List_UpgradeDrivetrainClutch", "DrivetrainID IN (" + drivetrains + ")");
            Add(list, "Drivetrain: transmission", "List_UpgradeDrivetrainTransmission", "DrivetrainID IN (" + drivetrains + ")");
            Add(list, "Drivetrain: driveline", "List_UpgradeDrivetrainDriveline", "DrivetrainID IN (" + drivetrains + ")");
            Add(list, "Drivetrain: differential", "List_UpgradeDrivetrainDifferential", "DrivetrainID IN (" + drivetrains + ")");
            Add(list, "Drivetrain layout", "Data_Drivetrain", "DrivetrainID IN (" + drivetrains + ")");
            Add(list, "Body", "List_UpgradeCarBody", "Ordinal = " + car);
            Add(list, "Body: weight reduction", "List_UpgradeCarBodyWeight", "CarBodyId IN (" + bodies + ")");
            Add(list, "Body: chassis stiffness", "List_UpgradeCarBodyChassisStiffness", "CarbodyId IN (" + bodies + ")");
            Add(list, "Body: front bumper", "List_UpgradeCarBodyFrontBumper", "CarBodyID IN (" + bodies + ")");
            Add(list, "Body: rear bumper", "List_UpgradeCarBodyRearBumper", "CarBodyID IN (" + bodies + ")");
            Add(list, "Body: hood", "List_UpgradeCarBodyHood", "CarBodyID IN (" + bodies + ")");
            Add(list, "Body: side skirts", "List_UpgradeCarBodySideSkirt", "CarBodyID IN (" + bodies + ")");
            Add(list, "Body: front tire width", "List_UpgradeCarBodyTireWidthFront", "CarBodyId IN (" + bodies + ")");
            Add(list, "Body: rear tire width", "List_UpgradeCarBodyTireWidthRear", "CarBodyId IN (" + bodies + ")");
            Add(list, "Rear wing", "List_UpgradeRearWing", "Ordinal = " + car);
            Add(list, "Brakes", "List_UpgradeBrakes", "Ordinal = " + car);
            Add(list, "Springs and dampers", "List_UpgradeSpringDamper", "Ordinal = " + car);
            Add(list, "Spring / damper physics", "List_SpringDamperPhysics", "Ordinal = " + car);
            Add(list, "Front anti-roll bar", "List_UpgradeAntiSwayFront", "Ordinal = " + car);
            Add(list, "Rear anti-roll bar", "List_UpgradeAntiSwayRear", "Ordinal = " + car);
            Add(list, "Anti-roll bar physics", "List_AntiSwayPhysics", "Ordinal = " + car);
            Add(list, "Tire compound", "List_UpgradeTireCompound", "Ordinal = " + car);
            Add(list, "Front rim size", "List_UpgradeRimSizeFront", "Ordinal = " + car);
            Add(list, "Rear rim size", "List_UpgradeRimSizeRear", "Ordinal = " + car);
            return this.Counted(list);
        }

        public List<Related> CarColors(long carId)
        {
            string car = carId.ToString(CultureInfo.InvariantCulture);
            List<Related> list = new List<Related>();
            Add(list, "Factory colors", "Combo_Colors", "Ordinal = " + car);
            Add(list, "Color exceptions (second color, stripes)", "Combo_Colors_Exceptions", "Ordinal = " + car);
            Add(list, "Stripe colors", "StripeColors", "Ordinal = " + car);
            return this.Counted(list);
        }

        public List<Related> CarOther(long carId)
        {
            string car = carId.ToString(CultureInfo.InvariantCulture);
            List<Related> list = new List<Related>();
            Add(list, "Engines (combo)", "Combo_Engines", "Ordinal = " + car);
            Add(list, "Camera offsets", "CameraOverrides", "CarId = " + car);
            Add(list, "Part positions", "CarPartPositions", "Ordinal = " + car);
            Add(list, "Missing parts (mirrors, windows, hood...)", "CarExceptions", "CarID = " + car);
            Add(list, "Track offsets", "CarTrackOffsets", "Id = " + car);
            Add(list, "Tire friction scale", "TireMod_FrictionScale", "Ordinal = " + car);
            Add(list, "Invalid default parts", "CarInvalidDefaultParts", "Ordinal = " + car);
            Add(list, "Event participations (AI cars)", "EventParticipants", "Ordinal = " + car);
            Add(list, "Showroom challenges", "EventShowroomChallenges", "CarID = " + car);
            Add(list, "Driver level rewards", "Rewards_DriverLevelv3", "RewardCarId = " + car);
            Add(list, "Showroom rewards", "Rewards_ShowroomUnlocks", "RewardCarId = " + car);
            Add(list, "Arcade car list", "ArcadeCarList", "CarId = " + car);
            Add(list, "Demo car list", "Demo_CarList", "Id = " + car);
            Add(list, "Car list filter", "CarListFilters", "CarID = " + car);
            Add(list, "Car experience", "HasCarExperience", "CarId = " + car);
            Add(list, "Kinect split screen", "KinectSplitscreenCars", "Ordinal = " + car);
            Add(list, "IE cars", "IECars", "CarId = " + car);
            return this.Counted(list);
        }

        public List<Related> WheelRelated(long wheelId)
        {
            string wheel = wheelId.ToString(CultureInfo.InvariantCulture);
            List<Related> list = new List<Related>();
            Add(list, "Cars with this stock wheel", "Data_Car", "StockWheelID = " + wheel);
            Add(list, "Normal offset", "WheelNormOffsets", "Id = " + wheel);
            Add(list, "Wheel exceptions", "WheelExceptions", "WheelID = " + wheel);
            return this.Counted(list);
        }

        public List<Related> TrackRelated(long trackId)
        {
            string track = trackId.ToString(CultureInfo.InvariantCulture);
            object[] row = this.ReadRow("Tracks", this.RowIdOf("Tracks", trackId));
            Table tracks = this.GetTable("Tracks");
            List<Related> list = new List<Related>();
            Add(list, "Races on this track", "Races", "TrackId = " + track);
            Add(list, "Start grid", "StartGridPositions", "id = " + Field(tracks, row, "DefaultStartGridPositionsId"));
            Add(list, "Flying start", "FlyingStartPositions", "id = " + Field(tracks, row, "FlyingStartPosId"));
            Add(list, "Environment", "Environments", "Id = " + Field(tracks, row, "EnvironmentId"));
            Add(list, "Times of day", "EnvironmentTimeOfDay", "EnvironmentId = " + Field(tracks, row, "EnvironmentId"));
            Add(list, "Pit zones", "TrackPittingZones", "Tracks_id = " + track);
            Add(list, "AI settings", "TrackSpecificAI", "Tracks_id = " + track);
            Add(list, "Starting merges", "TrackStartingMerges", "Tracks_id = " + track);
            Add(list, "Split points", "SplitPoints", "TrackId = " + track);
            Add(list, "Turn sections", "TurnSections", "TrackId = " + track);
            Add(list, "Drift scoring points", "GraffitiPoints", "TrackId = " + track);
            Add(list, "Hopper races", "HopperRaces", "TrackId = " + track);
            Add(list, "Kinect split screen", "KinectSplitscreenTracks", "TrackId = " + track);
            return this.Counted(list);
        }

        public List<Related> EventRelated(long eventId)
        {
            string id = eventId.ToString(CultureInfo.InvariantCulture);
            object[] row = this.ReadRow("Events", this.RowIdOf("Events", eventId));
            Table events = this.GetTable("Events");
            List<Related> list = new List<Related>();
            Add(list, "Races", "Races", "EventId = " + id);
            Add(list, "Special race targets", "RacesSpecial", "RaceId IN (" + this.IdList("SELECT Id FROM Races WHERE EventId = ?", eventId) + ")");
            Add(list, "AI participants", "EventParticipants", "EventID = " + id);
            Add(list, "Restrictions", "EventRestrictions", "EventID = " + id);
            Add(list, "Showroom challenge cars", "EventShowroomChallenges", "EventID = " + id);
            Add(list, "Bonus", "EventBonuses", "Id = " + Field(events, row, "BonusId"));
            Add(list, "Pitting profile", "PittingProfiles", "id = " + Field(events, row, "PittingProfileID"));
            return this.Counted(list);
        }

        public List<Related> EngineRelated(long engineId)
        {
            string id = engineId.ToString(CultureInfo.InvariantCulture);
            List<Related> list = new List<Related>();
            Add(list, "Upgrade rows (cars using it)", "List_UpgradeEngine", "EngineID = " + id);
            Add(list, "Old engine combos", "Combo_Engines", "EngineID = " + id);
            Add(list, "Torque curves", "List_TorqueCurve", "EngineID = " + id);
            return this.Counted(list);
        }

        public List<Related> MakeRelated(long makeId)
        {
            string id = makeId.ToString(CultureInfo.InvariantCulture);
            List<Related> list = new List<Related>();
            Add(list, "Cars of this make", "Data_Car", "MakeID = " + id);
            Add(list, "Engine upgrades", "List_UpgradeEngine", "ManufacturerID = " + id);
            Add(list, "Drivetrain upgrades", "List_UpgradeDrivetrain", "ManufacturerId = " + id);
            Add(list, "Decal sort order", "Livery_DecalsSortOrder", "MakeID = " + id);
            Add(list, "Part discounts", "Rewards_CarPartDiscounts", "CarMakeId = " + id);
            return this.Counted(list);
        }

        // Part manufacturers (List_PartManufacturer, the brands of the upgrade shop) are not car makes:
        // the engine and drivetrain upgrade rows name a car make, so they are left out.
        public List<Related> PartMakerRelated(long makerId)
        {
            string id = makerId.ToString(CultureInfo.InvariantCulture);
            List<Related> list = new List<Related>();
            foreach (string name in this.TableNames)
            {
                Table table = this.GetTable(name);
                if (name.StartsWith("List_Upgrade", StringComparison.OrdinalIgnoreCase) && table.Find("ManufacturerID") != null
                    && !string.Equals(name, "List_UpgradeEngine", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(name, "List_UpgradeDrivetrain", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(new Related(name.Substring("List_Upgrade".Length), name, "ManufacturerID = " + id));
                }
            }
            foreach (string[] other in new string[][] {
                new string[] { "Wheels", "List_Wheels", "PartManufacturerID" },
                new string[] { "Tire brands", "Combo_TireBrandCompound", "TireBrandId" },
                new string[] { "Part discounts", "Rewards_CarPartDiscounts", "BrandId" } })
            {
                if (this.HasTable(other[1]) && this.GetTable(other[1]).Find(other[2]) != null)
                {
                    list.Add(new Related(other[0], other[1], other[2] + " = " + id));
                }
            }
            return this.Counted(list);
        }

        public List<Related> CountryRelated(long countryId)
        {
            string id = countryId.ToString(CultureInfo.InvariantCulture);
            List<Related> list = new List<Related>();
            Add(list, "Makes", "List_CarMake", "CountryID = " + id);
            Add(list, "Cars", "Data_Car", "CountryID = " + id);
            Add(list, "Environments", "Environments", "CountryId = " + id);
            Add(list, "AI players", "AIPlayers", "CountryId = " + id);
            return this.Counted(list);
        }

        public string CarEngines(long carId)
        {
            List<string> ids = new List<string>();
            foreach (long[] engine in this.CarEngineIds(carId))
            {
                ids.Add(engine[0].ToString(CultureInfo.InvariantCulture));
            }
            return (ids.Count == 0) ? "NULL" : string.Join(",", ids.ToArray());
        }

        public long CarBodyId(long carId)
        {
            if (!this.HasTable("List_UpgradeCarBody"))
            {
                return -1;
            }
            object id = this.db.Scalar("SELECT CarBodyID FROM List_UpgradeCarBody WHERE Ordinal = ? ORDER BY IsStock DESC, Level LIMIT 1", carId);
            return (id == null) ? -1 : Convert.ToInt64(id);
        }

        private static string Field(Table table, object[] row, string column)
        {
            if (row == null)
            {
                return "NULL";
            }
            int index = table.Columns.IndexOf(table.Find(column));
            return (index < 0 || row[index] == null) ? "NULL" : Convert.ToInt64(row[index]).ToString(CultureInfo.InvariantCulture);
        }

        private void Add(List<Related> list, string label, string table, string where)
        {
            if (this.HasTable(table))
            {
                list.Add(new Related(label, table, where));
            }
        }

        // Keeps only the lists that have rows.
        private List<Related> Counted(List<Related> list)
        {
            List<Related> result = new List<Related>();
            foreach (Related related in list)
            {
                try
                {
                    related.Count = (int) this.Count(related.Table, related.Where);
                }
                catch (Exception)
                {
                    // A table of another game version without the column: not listed.
                    continue;
                }
                if (related.Count > 0)
                {
                    result.Add(related);
                }
            }
            return result;
        }

        #endregion

        public void Dispose()
        {
            this.db.Dispose();
        }
    }
}
