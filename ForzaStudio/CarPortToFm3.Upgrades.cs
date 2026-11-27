namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    // The FM3 window's Upgrades tab (docs/PORT_PARITY.md D3, user 2026-11-27 "fai tutta la D"): engine swaps left out or added
    // and the drive type conversion, as the FM4 window has them. FM3's car tables are FM4's, so the rows are written as
    // CarPort writes them into FM4 (List_UpgradeEngine level > 0, IsStock 0; CarUpgrades for the drivetrain).
    public partial class CarPortToFm3
    {
        // An FM4 car's swaps the user left out: their rows go before the engines are looked at.
        private void RemoveSwaps()
        {
            if (this.Fixes == null)
            {
                return;
            }
            foreach (KeyValuePair<string, object> fix in this.Fixes)
            {
                long engine;
                if (fix.Key.StartsWith(CarPort.SwapRemoveKey, StringComparison.Ordinal)
                    && long.TryParse(fix.Key.Substring(CarPort.SwapRemoveKey.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out engine))
                {
                    int removed = this.target.Execute("DELETE FROM List_UpgradeEngine WHERE Ordinal = ? AND EngineID = ? AND Level > 0", this.carId, engine);
                    this.Line("  engine swap {0}: left out (Upgrades tab){1}", engine, (removed == 0) ? ", not a swap of this car" : "");
                }
            }
        }

        // FM3 engines the user added as swaps: one List_UpgradeEngine row each at the next level, after the car's own engines
        // were resolved (an FM3 engine id may be another engine in FM4). Mass and weight distribution as the game's own swaps,
        // ManufacturerID = the car's make (CarPort.AddSwaps does the same in FM4).
        private void AddFm3Swaps()
        {
            if (this.Fixes == null)
            {
                return;
            }
            SqliteDatabase.Result stock = this.target.Query("SELECT * FROM List_UpgradeEngine WHERE Ordinal = ? AND Level = 0", this.carId);
            if (stock.Rows.Count == 0)
            {
                return;
            }
            int engineColumn = Array.FindIndex(stock.Columns, delegate(string c) { return string.Equals(c, "EngineID", StringComparison.OrdinalIgnoreCase); });
            List<KeyValuePair<long, long>> added = new List<KeyValuePair<long, long>>();
            foreach (KeyValuePair<string, object> fix in this.Fixes)
            {
                long engine;
                if (fix.Key.StartsWith(Fm3SwapAddKey, StringComparison.Ordinal)
                    && long.TryParse(fix.Key.Substring(Fm3SwapAddKey.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out engine))
                {
                    added.Add(new KeyValuePair<long, long>(engine, (fix.Value is long) ? (long) fix.Value : 6800));
                }
            }
            added.Sort(delegate(KeyValuePair<long, long> a, KeyValuePair<long, long> b) { return a.Key.CompareTo(b.Key); });
            foreach (KeyValuePair<long, long> pick in added)
            {
                long engine = pick.Key;
                object name = this.target.Scalar("SELECT MediaName FROM Data_Engine WHERE EngineID = ?", engine);
                if (name == null)
                {
                    this.Line("  engine swap {0}: not an FM3 engine, not added", engine);
                    continue;
                }
                if (Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM List_UpgradeEngine WHERE Ordinal = ? AND EngineID = ?", this.carId, engine), CultureInfo.InvariantCulture) > 0)
                {
                    this.Line("  engine swap {0} ({1}): the car has it already", engine, name);
                    continue;
                }
                long level = Convert.ToInt64(this.target.Scalar("SELECT max(Level) FROM List_UpgradeEngine WHERE Ordinal = ?", this.carId), CultureInfo.InvariantCulture) + 1;
                long id = (this.carId * 1000) + level;
                while (Convert.ToInt64(this.target.Scalar("SELECT count(*) FROM List_UpgradeEngine WHERE Id = ?", id), CultureInfo.InvariantCulture) > 0)
                {
                    id++;
                }
                object[] row = (object[]) stock.Rows[0].Clone();
                double stockMass = Number(this.target.Scalar("SELECT [EngineMass-kg] FROM Data_Engine WHERE EngineID = ?", row[engineColumn]));
                double newMass = Number(this.target.Scalar("SELECT [EngineMass-kg] FROM Data_Engine WHERE EngineID = ?", engine));
                // the car's Data_Car row is written after the engines (PortCarRow): weight, engine placement and make are the source's (its row
                // in the source copy carries the FM3 name, carName)
                double curb = Number(this.source.Scalar("SELECT CurbWeight FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", this.carName));
                object placement = this.source.Scalar("SELECT EnginePlacementID FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", this.carName);
                double massDiff = Math.Round(newMass - stockMass, 3);
                double side = (placement != null && !(placement is DBNull) && Convert.ToInt64(placement, CultureInfo.InvariantCulture) == 1) ? 0.3 : -0.3;
                object make = this.MakeId.HasValue ? (object) this.MakeId.Value : this.source.Scalar("SELECT MakeID FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", this.carName);
                string[] quoted = new string[stock.Columns.Length];
                string[] marks = new string[stock.Columns.Length];
                for (int i = 0; i < stock.Columns.Length; i++)
                {
                    quoted[i] = SqliteDatabase.Quote(stock.Columns[i]);
                    marks[i] = "?";
                    switch (stock.Columns[i].ToLowerInvariant())
                    {
                        case "id": row[i] = id; break;
                        case "level": row[i] = level; break;
                        case "engineid": row[i] = engine; break;
                        case "isstock": row[i] = 0L; break;
                        case "price": row[i] = pick.Value; break;
                        case "massdiff": row[i] = massDiff; break;
                        case "weightdistdiff": row[i] = (curb > 0) ? Math.Round(massDiff * side / (curb * 100), 6) : 0.0; break;
                        case "manufacturerid": row[i] = (make != null && !(make is DBNull)) ? Convert.ToInt64(make, CultureInfo.InvariantCulture) : 0L; break;
                    }
                }
                this.target.Execute("INSERT INTO List_UpgradeEngine (" + string.Join(", ", quoted) + ") VALUES (" + string.Join(", ", marks) + ")", row);
                this.Line("  engine swap {0} ({1}): added (Upgrades tab), level {2}, mass {3:+0.#;-0.#;0} kg, {4} Cr, maker {5}", engine, name, level, massDiff, pick.Value, (make == null || make is DBNull) ? "none" : Convert.ToString(make, CultureInfo.InvariantCulture));
            }
        }

        // An FM4 car's drive type conversion picked in the Upgrades tab: CarUpgrades on FM3's database (its car tables are FM4's;
        // the new drivetrain id is 100000 + car id, above every FM3 id).
        private void DriveConversion()
        {
            object drive;
            if (this.Fixes == null || !this.Fixes.TryGetValue(CarPort.DriveKey, out drive) || !(drive is string) || (string) drive == "none")
            {
                return;
            }
            List<string> notes = new List<string>();
            CarUpgrades upgrades = new CarUpgrades(this.target, this.carId, CarUpgrades.LoadIni(notes));
            upgrades.DriveOverride = (string) drive;
            upgrades.ApplyDrivetrainOnly();
            this.Line("  drive type: {0} (Upgrades tab)", drive);
            foreach (string line in upgrades.Lines)
            {
                this.Line("{0}", line);
            }
        }

        private static double Number(object value)
        {
            return (value == null || value is string || value is DBNull) ? 0 : Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
    }
}
