namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Windows.Forms;

    // UPGRADES tab (user 2026-10-20): the selected car's engine swaps (the source's, which can be left
    // out, and FM4 engines added) and its drive type conversion. The choices go with the review's fixes
    // (CarPort.SwapRemoveKey / SwapAddKey / DriveKey), so the next trial run, the install and the DLC
    // pack use them; the generic levels themselves follow Upgrades.ini (CarUpgrades).
    internal partial class CarPortForm
    {
        private TabPage upgradesPage;
        private Label upgradesTitle;
        private ListView swapList;
        private Button addSwap;
        private Button removeSwap;
        private ComboBox driveChoice;
        private Label driveStock;
        private Label genericNote;
        private Car upgradesCar;
        private bool fillingUpgrades;
        private readonly Dictionary<string, SqliteDatabase> openDbs = new Dictionary<string, SqliteDatabase>(StringComparer.OrdinalIgnoreCase);

        private sealed class SwapRow
        {
            public long Engine;
            public bool Stock;
            public bool Added;      // an FM4 engine added in this tab
        }

        private sealed class DriveItem
        {
            public string Value;    // null = the ini's rule
            public string Label;

            public override string ToString()
            {
                return this.Label;
            }
        }

        private void BuildUpgradesPage()
        {
            this.upgradesPage = new TabPage("Upgrades");
            Panel page = new Panel();
            page.Dock = DockStyle.Fill;

            this.upgradesTitle = new Label();
            this.upgradesTitle.Dock = DockStyle.Top;
            this.upgradesTitle.Height = 24;
            this.upgradesTitle.Font = ForzaTheme.Bold;
            this.upgradesTitle.Padding = new Padding(6, 5, 0, 0);
            this.upgradesTitle.Text = "Select a car.";

            // Engine swaps (left), drive type and generic upgrades (right).
            Panel swaps = new Panel();
            swaps.Dock = DockStyle.Fill;
            this.swapList = new ListView();
            this.swapList.Dock = DockStyle.Fill;
            this.swapList.View = View.Details;
            this.swapList.FullRowSelect = true;
            this.swapList.MultiSelect = false;
            this.swapList.HideSelection = false;
            this.swapList.Columns.Add("Level", 50);
            this.swapList.Columns.Add("Engine", 260);
            this.swapList.Columns.Add("Media name", 150);
            this.swapList.Columns.Add("Swap", 300);
            this.swapList.SelectedIndexChanged += delegate { this.UpdateSwapButtons(); };
            this.StyleList(this.swapList);
            Panel swapButtons = new Panel();
            swapButtons.Dock = DockStyle.Bottom;
            swapButtons.Height = 34;
            this.addSwap = new Button();
            this.addSwap.Text = "Add FM4 Engine...";
            this.addSwap.Size = new Size(140, 26);
            this.addSwap.Location = new Point(6, 4);
            this.addSwap.Click += delegate { this.AddFm4Swap(); };
            new ToolTip().SetToolTip(this.addSwap, "Add an FM4 engine as a swap (its parts and sound are FM4's)");
            this.removeSwap = new Button();
            this.removeSwap.Text = "Remove";
            this.removeSwap.Size = new Size(110, 26);
            this.removeSwap.Location = new Point(152, 4);
            this.removeSwap.Click += delegate { this.ToggleSwap(); };
            swapButtons.Controls.Add(this.addSwap);
            swapButtons.Controls.Add(this.removeSwap);
            swaps.Controls.Add(this.swapList);
            swaps.Controls.Add(swapButtons);
            swaps.Controls.Add(SectionHeader("Engine swaps"));

            Panel side = new Panel();
            side.Dock = DockStyle.Right;
            side.Width = 380;
            side.Padding = new Padding(10, 0, 0, 0);
            this.driveStock = new Label();
            this.driveStock.Location = new Point(14, 36);
            this.driveStock.AutoSize = true;
            this.driveChoice = new ComboBox();
            this.driveChoice.DropDownStyle = ComboBoxStyle.DropDownList;
            this.driveChoice.Location = new Point(14, 58);
            this.driveChoice.Width = 340;
            this.driveChoice.SelectedIndexChanged += delegate { this.DriveChosen(); };
            this.genericNote = new Label();
            this.genericNote.Location = new Point(14, 128);
            this.genericNote.Size = new Size(350, 52);
            this.genericNote.ForeColor = ForzaTheme.TextDim;
            Button openIni = new Button();
            openIni.Text = "Edit " + CarUpgrades.FileName + "...";
            openIni.Size = new Size(150, 26);
            openIni.Location = new Point(14, 184);
            openIni.Click += delegate { this.OpenUpgradesIni(); };
            Control driveHeader = SectionHeader("Drive type");
            driveHeader.Dock = DockStyle.None;
            driveHeader.Bounds = new Rectangle(10, 0, 360, 28);
            Control genericHeader = SectionHeader("Generic upgrades");
            genericHeader.Dock = DockStyle.None;
            genericHeader.Bounds = new Rectangle(10, 94, 360, 28);
            side.Controls.Add(driveHeader);
            side.Controls.Add(this.driveStock);
            side.Controls.Add(this.driveChoice);
            side.Controls.Add(genericHeader);
            side.Controls.Add(this.genericNote);
            side.Controls.Add(openIni);

            page.Controls.Add(swaps);
            page.Controls.Add(side);
            page.Controls.Add(this.upgradesTitle);
            this.upgradesPage.Controls.Add(page);
            this.FormClosed += delegate
            {
                foreach (SqliteDatabase db in this.openDbs.Values)
                {
                    db.Dispose();
                }
                this.openDbs.Clear();
            };
            this.ShowUpgrades(null);
        }

        // The game database of a game folder, read once per window.
        private SqliteDatabase GameDb(string root)
        {
            string path = Path.Combine(Path.Combine(Path.Combine(root, "Media"), "db"), "gamedb.slt");
            SqliteDatabase db;
            if (!this.openDbs.TryGetValue(path, out db))
            {
                if (!File.Exists(path))
                {
                    return null;
                }
                db = SqliteDatabase.FromBytes(File.ReadAllBytes(path));
                this.openDbs[path] = db;
            }
            return db;
        }

        // The game databases change with an install: read them again next time.
        private void ForgetGameDbs()
        {
            foreach (SqliteDatabase db in this.openDbs.Values)
            {
                db.Dispose();
            }
            this.openDbs.Clear();
        }

        private Dictionary<string, object> FixesOf(Car car)
        {
            Dictionary<string, object> chosen;
            if (!this.fixes.TryGetValue(car.MediaName, out chosen))
            {
                chosen = new Dictionary<string, object>();
                this.fixes[car.MediaName] = chosen;
            }
            return chosen;
        }

        private void ShowUpgrades(Car car)
        {
            this.upgradesCar = car;
            this.fillingUpgrades = true;
            this.swapList.Items.Clear();
            this.driveChoice.Items.Clear();
            bool usable = car != null && (car.State == Status.New || car.State == Status.Ported || car.State == Status.InPack);
            // The list stays enabled (empty when not usable): Windows draws a disabled list light grey.
            this.driveChoice.Enabled = usable;
            this.addSwap.Enabled = usable;
            if (!usable)
            {
                this.upgradesTitle.Text = (car == null) ? "Select a car." : car.Name + ": not portable, nothing to choose.";
                this.driveStock.Text = "";
                this.genericNote.Text = "";
                this.fillingUpgrades = false;
                this.UpdateSwapButtons();
                return;
            }
            this.upgradesTitle.Text = car.Name + ": engine swaps and drive type, used by the next trial run, the install and the DLC pack";
            Dictionary<string, object> chosen = this.FixesOf(car);
            SqliteDatabase source = null;
            SqliteDatabase target = null;
            try
            {
                // A catalog DLC car: its package is opened and staged now, the first time one of its cars is chosen.
                source = this.GameDb(this.RootOf(car, null) ?? this.sourceBox.Text.Trim());
                target = Directory.Exists(this.targetBox.Text.Trim()) ? this.GameDb(this.targetBox.Text.Trim()) : null;
            }
            catch (Exception ex)
            {
                this.upgradesTitle.Text = car.Name + ": the game databases could not be read (" + ex.Message + ")";
            }

            // The source's engines (stock + swaps), then the FM4 engines added here.
            if (source != null)
            {
                foreach (object[] row in source.Query("SELECT u.Level, u.EngineID, e.EngineName, e.MediaName FROM List_UpgradeEngine u LEFT JOIN Data_Engine e ON e.EngineID = u.EngineID WHERE u.Ordinal = ? ORDER BY u.Level", car.Id).Rows)
                {
                    long level = Convert.ToInt64(row[0]);
                    long engine = Convert.ToInt64(row[1]);
                    SwapRow tag = new SwapRow();
                    tag.Engine = engine;
                    tag.Stock = level == 0;
                    string name = row[2] as string;
                    string media = row[3] as string;
                    bool inFm4 = target != null && target.Scalar("SELECT EngineID FROM Data_Engine WHERE EngineID = ?", engine) != null;
                    if (string.IsNullOrEmpty(name) && target != null)
                    {
                        name = target.Scalar("SELECT EngineName FROM Data_Engine WHERE EngineID = ?", engine) as string;
                    }
                    bool removed = chosen.ContainsKey(CarPort.SwapRemoveKey + engine.ToString(CultureInfo.InvariantCulture));
                    string note = tag.Stock ? "stock engine"
                        : removed ? "left out (Put Back to keep it)"
                        : (media == null && !inFm4) ? "the source's swap; in neither game's data here: the review asks for an FM4 engine"
                        : "the source's swap" + (inFm4 ? " (FM4 has this engine)" : "");
                    ListViewItem item = new ListViewItem(level.ToString(CultureInfo.InvariantCulture));
                    item.SubItems.Add(string.IsNullOrEmpty(name) ? "engine " + engine : CleanEngineName(name));
                    item.SubItems.Add(media ?? "");
                    item.SubItems.Add(note);
                    item.Tag = tag;
                    if (removed)
                    {
                        item.ForeColor = ForzaTheme.TextDim;
                    }
                    this.swapList.Items.Add(item);
                }
            }
            foreach (KeyValuePair<string, object> fix in chosen)
            {
                long engine;
                if (!fix.Key.StartsWith(CarPort.SwapAddKey, StringComparison.Ordinal) || !long.TryParse(fix.Key.Substring(CarPort.SwapAddKey.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out engine))
                {
                    continue;
                }
                SqliteDatabase.Result info = (target == null) ? null : target.Query("SELECT EngineName, MediaName FROM Data_Engine WHERE EngineID = ?", engine);
                ListViewItem item = new ListViewItem("+");
                item.SubItems.Add((info != null && info.Rows.Count > 0) ? CleanEngineName(info.Rows[0][0] as string) : "engine " + engine);
                item.SubItems.Add((info != null && info.Rows.Count > 0) ? (info.Rows[0][1] as string ?? "") : "");
                item.SubItems.Add(string.Format(CultureInfo.InvariantCulture, "FM4 engine added here, {0} Cr (next free level)", fix.Value));
                SwapRow tag = new SwapRow();
                tag.Engine = engine;
                tag.Added = true;
                item.Tag = tag;
                this.swapList.Items.Add(item);
            }

            // Drive type: the ini's rule, or the user's pick for this car.
            string stock = DriveName(car.Drive);
            Dictionary<string, Dictionary<string, string>> ini = CarUpgrades.LoadIni(new List<string>());
            string rule = Ini(ini, "Drivetrain swap", stock, "").ToUpperInvariant();
            this.driveStock.Text = "Stock: " + stock + ".  " + CarUpgrades.FileName + " rule: " + ((rule.Length > 0 && rule != stock) ? stock + " → " + rule : "no conversion") + ".";
            this.AddDrive(null, "As the " + CarUpgrades.FileName + " rule" + ((rule.Length > 0 && rule != stock) ? " (→ " + rule + ")" : " (none)"));
            this.AddDrive("none", "No conversion");
            foreach (string type in new string[] { "RWD", "AWD", "FWD" })
            {
                if (type != stock)
                {
                    this.AddDrive(type, stock + " → " + type);
                }
            }
            object drive;
            string picked = chosen.TryGetValue(CarPort.DriveKey, out drive) ? drive as string : null;
            foreach (DriveItem item in this.driveChoice.Items)
            {
                if (item.Value == picked)
                {
                    this.driveChoice.SelectedItem = item;
                }
            }
            if (this.driveChoice.SelectedIndex < 0)
            {
                this.driveChoice.SelectedIndex = 0;
            }

            string cars = Ini(ini, "General", "Cars", "traffic").ToLowerInvariant();
            bool generic = cars == "missing" || (cars == "traffic" && car.Traffic);
            this.genericNote.Text = generic
                ? "Street / Sport / Race levels for every part that has only the stock one (" + (car.Traffic ? "traffic car" : "Cars = missing") + "). Values in " + CarUpgrades.FileName + "."
                : "None: " + CarUpgrades.FileName + " has Cars = " + cars + (cars == "traffic" ? " and this is not a traffic car" : "") + ". A conversion picked above still applies.";
            this.fillingUpgrades = false;
            this.UpdateSwapButtons();
        }

        private void AddDrive(string value, string label)
        {
            DriveItem item = new DriveItem();
            item.Value = value;
            item.Label = label;
            this.driveChoice.Items.Add(item);
        }

        private static string Ini(Dictionary<string, Dictionary<string, string>> ini, string section, string key, string fallback)
        {
            Dictionary<string, string> values;
            string value;
            return (ini.TryGetValue(section, out values) && values.TryGetValue(key, out value)) ? value : fallback;
        }

        private static string DriveName(long type)
        {
            return (type == 1) ? "FWD" : (type == 2) ? "RWD" : (type == 3) ? "AWD" : "?";
        }

        // "V12   -   Enzo Ferrari" -> "V12 - Enzo Ferrari".
        private static string CleanEngineName(string name)
        {
            if (name == null)
            {
                return "";
            }
            while (name.Contains("  "))
            {
                name = name.Replace("  ", " ");
            }
            return name.Trim();
        }

        private SwapRow SelectedSwap
        {
            get { return (this.swapList.SelectedItems.Count == 1) ? this.swapList.SelectedItems[0].Tag as SwapRow : null; }
        }

        private void UpdateSwapButtons()
        {
            SwapRow row = this.SelectedSwap;
            bool removed = row != null && this.upgradesCar != null && this.FixesOf(this.upgradesCar).ContainsKey(CarPort.SwapRemoveKey + row.Engine.ToString(CultureInfo.InvariantCulture));
            this.removeSwap.Enabled = row != null && !row.Stock;
            this.removeSwap.Text = removed ? "Put Back" : "Remove";
        }

        // Remove: an added FM4 engine goes, a source swap is left out (or put back).
        private void ToggleSwap()
        {
            SwapRow row = this.SelectedSwap;
            Car car = this.upgradesCar;
            if (row == null || row.Stock || car == null)
            {
                return;
            }
            Dictionary<string, object> chosen = this.FixesOf(car);
            string engine = row.Engine.ToString(CultureInfo.InvariantCulture);
            if (row.Added)
            {
                chosen.Remove(CarPort.SwapAddKey + engine);
            }
            else if (!chosen.Remove(CarPort.SwapRemoveKey + engine))
            {
                chosen[CarPort.SwapRemoveKey + engine] = true;
            }
            this.UpgradesChanged(car);
        }

        private void AddFm4Swap()
        {
            Car car = this.upgradesCar;
            SqliteDatabase target = Directory.Exists(this.targetBox.Text.Trim()) ? this.GameDb(this.targetBox.Text.Trim()) : null;
            if (car == null || target == null)
            {
                return;
            }
            List<object[]> rows = new List<object[]>();
            foreach (object[] engine in target.Query("SELECT e.EngineID, e.EngineName, e.MediaName, e.[EngineMass-kg], (SELECT count(DISTINCT Ordinal) FROM List_UpgradeEngine u WHERE u.EngineID = e.EngineID) FROM Data_Engine e ORDER BY e.EngineName").Rows)
            {
                engine[1] = CleanEngineName(engine[1] as string);
                rows.Add(engine);
            }
            Panel extra = new Panel();
            extra.Size = new Size(520, 50);
            NumericUpDown price = PickDialog.NumberField(extra, "Price (credits)", 0, 6800, 10000000, 0);
            using (PickDialog dialog = new PickDialog("Add FM4 Engine Swap - " + car.Name, new string[] { "Id", "Engine", "Media name", "Mass (kg)", "Cars" }, new int[] { 50, 300, 150, 70, 45 }, rows, extra))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedKey == null)
                {
                    return;
                }
                long engine = Convert.ToInt64(dialog.SelectedKey);
                foreach (ListViewItem item in this.swapList.Items)
                {
                    SwapRow row = item.Tag as SwapRow;
                    if (row != null && row.Engine == engine)
                    {
                        MessageBox.Show(this, "The car has that engine already.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                }
                this.FixesOf(car)[CarPort.SwapAddKey + engine.ToString(CultureInfo.InvariantCulture)] = (long) price.Value;
            }
            this.UpgradesChanged(car);
        }

        private void DriveChosen()
        {
            DriveItem item = this.driveChoice.SelectedItem as DriveItem;
            Car car = this.upgradesCar;
            if (this.fillingUpgrades || item == null || car == null)
            {
                return;
            }
            Dictionary<string, object> chosen = this.FixesOf(car);
            if (item.Value == null)
            {
                chosen.Remove(CarPort.DriveKey);
            }
            else
            {
                chosen[CarPort.DriveKey] = item.Value;
            }
            this.UpgradesChanged(car);
        }

        // A choice made: shown again, and a pack car's status says whether its trial still matches.
        private void UpgradesChanged(Car car)
        {
            int selected = (this.swapList.SelectedIndices.Count == 1) ? this.swapList.SelectedIndices[0] : -1;
            this.ShowUpgrades(car);
            if (selected >= 0 && selected < this.swapList.Items.Count)
            {
                this.swapList.Items[selected].Selected = true;
            }
            this.details.Text = car.Name + ": upgrade choices changed, run the trial again" + ((this.FindEntry(car.MediaName) != null) ? " (also the pack trial)." : ".");
            if (this.FindEntry(car.MediaName) != null)
            {
                this.UpdatePackInfo();
            }
        }

        // Upgrades.ini in Tools > Settings, on its UPGRADES tab (user 2026-10-20: a tab, not Notepad).
        private void OpenUpgradesIni()
        {
            using (SettingsForm settings = new SettingsForm("Upgrades"))
            {
                settings.ShowDialog(this);
            }
            this.ShowUpgrades(this.upgradesCar);   // the drive rule and notes may have changed
        }
    }
}
