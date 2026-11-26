namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.Windows.Forms;

    // UPGRADES tab of the FM2 window (B4 of docs/PORT_PARITY.md, user 2026-11-27): the car's engine swaps and its drive
    // conversion, as the FM4 window's tab, and a read-only table of every upgrade level (source / FM2 / note) from the
    // trial's database dry run. FM2 has engine swaps (levels 1-3) and keeps the drive type on the ENGINE
    // (Data_Engine.DriveTypeID): a drive conversion is a swap to a copy of the car's engine with another drive type
    // (CarDataToFm2.WriteSwaps). The choices go with the trial: the install and the port record use them.
    internal sealed partial class CarPortFm2Form
    {
        private static readonly string[] DriveNames = { "none", "FWD", "RWD", "AWD" };

        private TabPage BuildUpgradesPage()
        {
            this.upgradesPage = new TabPage("Upgrades");
            this.upgradesTitle = new Label();
            this.upgradesTitle.Dock = DockStyle.Top;
            this.upgradesTitle.Height = 26;
            this.upgradesTitle.Font = ForzaTheme.Bold;
            this.upgradesTitle.Padding = new Padding(6, 6, 0, 0);
            this.upgradesTitle.Text = "Run a trial on a car: its engine swaps, drive conversion and upgrade levels are listed here.";

            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 196;

            // right: the drive conversion
            Panel drive = new Panel();
            drive.Dock = DockStyle.Right;
            drive.Width = 320;
            Label driveTitle = new Label();
            driveTitle.Text = "Drive type (FM2: the engine's)";
            driveTitle.Font = ForzaTheme.Bold;
            driveTitle.Location = new Point(10, 6);
            driveTitle.AutoSize = true;
            this.driveStock = new Label();
            this.driveStock.Location = new Point(10, 30);
            this.driveStock.AutoSize = true;
            Label conversion = new Label();
            conversion.Text = "Drive conversion:";
            conversion.Location = new Point(10, 60);
            conversion.AutoSize = true;
            this.driveChoice = new ComboBox();
            this.driveChoice.DropDownStyle = ComboBoxStyle.DropDownList;
            this.driveChoice.Items.AddRange(DriveNames);
            this.driveChoice.Location = new Point(130, 57);
            this.driveChoice.Width = 80;
            Label at = new Label();
            at.Text = "level";
            at.Location = new Point(218, 60);
            at.AutoSize = true;
            this.driveLevel = LevelCombo(new Point(256, 57));
            Label driveNote = new Label();
            driveNote.Text = "A swap to a copy of the car's engine\nwith the other drive type, as FM2's own\ncars do (71 of its swaps).";
            driveNote.ForeColor = ForzaTheme.TextDim;
            driveNote.Location = new Point(10, 92);
            driveNote.AutoSize = true;
            this.driveChoice.SelectedIndexChanged += delegate { this.DriveChanged(); };
            this.driveLevel.SelectedIndexChanged += delegate { this.DriveChanged(); };
            drive.Controls.Add(driveTitle);
            drive.Controls.Add(this.driveStock);
            drive.Controls.Add(conversion);
            drive.Controls.Add(this.driveChoice);
            drive.Controls.Add(at);
            drive.Controls.Add(this.driveLevel);
            drive.Controls.Add(driveNote);

            // left: the swaps
            Panel swaps = new Panel();
            swaps.Dock = DockStyle.Fill;
            Panel buttons = new Panel();
            buttons.Dock = DockStyle.Bottom;
            buttons.Height = 36;
            Label levelLabel = new Label();
            levelLabel.Text = "Level:";
            levelLabel.Location = new Point(6, 10);
            levelLabel.AutoSize = true;
            this.swapLevel = LevelCombo(new Point(48, 6));
            new ToolTip().SetToolTip(this.swapLevel, "The level of the selected swap (FM2 has one swap per level, 1-3)");
            this.swapLevel.SelectedIndexChanged += delegate { this.SwapLevelChanged(); };
            Label addLabel = new Label();
            addLabel.Text = "Add FM2 engine:";
            addLabel.Location = new Point(110, 10);
            addLabel.AutoSize = true;
            this.fm2Engines = new ComboBox();
            this.fm2Engines.DropDownStyle = ComboBoxStyle.DropDownList;
            this.fm2Engines.Location = new Point(212, 6);
            this.fm2Engines.Width = 260;
            this.addLevel = LevelCombo(new Point(478, 6));
            this.addEngine = new Button();
            this.addEngine.Text = "Add";
            this.addEngine.Size = new Size(60, 26);
            this.addEngine.Location = new Point(538, 4);
            this.addEngine.Click += delegate { this.AddFm2Engine(); };
            this.removeSwap = new Button();
            this.removeSwap.Text = "Remove";
            this.removeSwap.Size = new Size(76, 26);
            this.removeSwap.Location = new Point(606, 4);
            this.removeSwap.Click += delegate { this.RemoveSwap(); };
            buttons.Controls.Add(levelLabel);
            buttons.Controls.Add(this.swapLevel);
            buttons.Controls.Add(addLabel);
            buttons.Controls.Add(this.fm2Engines);
            buttons.Controls.Add(this.addLevel);
            buttons.Controls.Add(this.addEngine);
            buttons.Controls.Add(this.removeSwap);
            this.swapList = new ListView();
            this.swapList.Dock = DockStyle.Fill;
            this.swapList.View = View.Details;
            this.swapList.CheckBoxes = true;
            this.swapList.FullRowSelect = true;
            this.swapList.MultiSelect = false;
            this.swapList.HideSelection = false;
            this.swapList.Columns.Add("Swap (ticked: ported)", 160);
            this.swapList.Columns.Add("Level", 50);
            this.swapList.Columns.Add("Engine", 300);
            this.swapList.Columns.Add("Drive", 60);
            this.swapList.ItemChecked += delegate(object sender, ItemCheckedEventArgs e) { this.SwapTicked(e.Item); };
            this.swapList.SelectedIndexChanged += delegate { this.SwapSelected(); };
            CarPortFm3Form.StyleList(this.swapList);
            swaps.Controls.Add(this.swapList);
            swaps.Controls.Add(buttons);

            top.Controls.Add(swaps);
            top.Controls.Add(drive);

            this.upgradeTable = new ListView();
            this.upgradeTable.Dock = DockStyle.Fill;
            this.upgradeTable.View = View.Details;
            this.upgradeTable.FullRowSelect = true;
            this.upgradeTable.Columns.Add("Part", 150);
            this.upgradeTable.Columns.Add("Level", 56);
            this.upgradeTable.Columns.Add("Source", 330);
            this.upgradeTable.Columns.Add("FM2 (after the trial's dry run)", 360);
            this.upgradeTable.Columns.Add("Note", 260);
            CarPortFm3Form.StyleList(this.upgradeTable);

            this.upgradesPage.Controls.Add(this.upgradeTable);
            this.upgradesPage.Controls.Add(top);
            this.upgradesPage.Controls.Add(this.upgradesTitle);
            return this.upgradesPage;
        }

        private static ComboBox LevelCombo(Point where)
        {
            ComboBox combo = new ComboBox();
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.Items.AddRange(new object[] { "1", "2", "3" });
            combo.Location = where;
            combo.Width = 52;
            combo.SelectedIndex = 0;
            return combo;
        }

        private static string DriveText(long drive)
        {
            return (drive >= 1 && drive <= 3) ? DriveNames[drive] : "?";
        }

        // The FM2 engines Add offers (LoadCars): every engine of the game (not the ports' own).
        private void FillFm2Engines(List<object[]> engines)
        {
            this.fm2EngineList = engines;
            this.fm2Engines.Items.Clear();
            foreach (object[] engine in engines)
            {
                this.fm2Engines.Items.Add(engine[1] + "  (" + engine[0] + ", " + DriveText(Convert.ToInt64(engine[2], CultureInfo.InvariantCulture)) + ")");
            }
            if (this.fm2Engines.Items.Count > 0)
            {
                this.fm2Engines.SelectedIndex = 0;
            }
        }

        // The selected car's swaps, drive and table (empty without a trial).
        private void ShowUpgrades()
        {
            Trial current = this.CurrentTrial;
            SourceCar car = this.SelectedCar;
            this.upgradesFilling = true;
            this.swapList.BeginUpdate();
            this.swapList.Items.Clear();
            this.upgradeTable.BeginUpdate();
            this.upgradeTable.Items.Clear();
            bool on = current != null && !this.busy;
            this.swapList.Enabled = on;
            this.swapLevel.Enabled = on;
            this.fm2Engines.Enabled = on;
            this.addLevel.Enabled = on;
            this.addEngine.Enabled = on && this.fm2Engines.Items.Count > 0;
            this.removeSwap.Enabled = on;
            this.driveChoice.Enabled = on;
            this.driveLevel.Enabled = on;
            int swaps = 0, ported = 0;
            if (current != null)
            {
                SwapItem drive = null;
                foreach (SwapItem item in current.Swaps)
                {
                    if (item.Swap.Kind == "drive")
                    {
                        drive = item;
                        continue;
                    }
                    swaps++;
                    ported += item.Included ? 1 : 0;
                    ListViewItem row = new ListViewItem((item.Swap.Kind == "fm2") ? "FM2 engine (yours)" : "the source's");
                    row.Tag = item;
                    row.Checked = item.Included;
                    row.SubItems.Add((item.Swap.Level >= 1 && item.Swap.Level <= 3) ? "L" + item.Swap.Level : "L" + item.Swap.Level + " (FM2: 1-3)");
                    row.SubItems.Add(item.Engine);
                    long engineDrive = current.StockDrive;
                    if (item.Swap.Kind == "fm2")
                    {
                        object[] engine = this.fm2EngineList.Find(delegate(object[] e) { return Convert.ToInt64(e[0], CultureInfo.InvariantCulture) == item.Swap.Engine; });
                        engineDrive = (engine != null) ? Convert.ToInt64(engine[2], CultureInfo.InvariantCulture) : 0;
                    }
                    row.SubItems.Add(DriveText(engineDrive));
                    if (item.Swap.Level < 1 || item.Swap.Level > 3)
                    {
                        row.ForeColor = ForzaTheme.Yellow;
                    }
                    else if (!item.Included)
                    {
                        row.ForeColor = ForzaTheme.TextDim;
                    }
                    this.swapList.Items.Add(row);
                }
                this.driveStock.Text = "Stock: " + DriveText(current.StockDrive) + " (the source car's)";
                this.driveChoice.SelectedIndex = (drive != null && drive.Included) ? (int) drive.Swap.DriveType : 0;
                this.driveLevel.SelectedIndex = (drive != null && drive.Swap.Level >= 1 && drive.Swap.Level <= 3) ? (int) drive.Swap.Level - 1 : 0;
                foreach (string[] line in current.Upgrades)
                {
                    ListViewItem row = new ListViewItem(line);
                    if (line[4].StartsWith("not ported", StringComparison.Ordinal))
                    {
                        row.ForeColor = ForzaTheme.Yellow;
                    }
                    else if (line[4].StartsWith("engine swap", StringComparison.Ordinal))
                    {
                        row.ForeColor = ForzaTheme.Blue;
                    }
                    this.upgradeTable.Items.Add(row);
                }
            }
            else
            {
                this.driveStock.Text = "";
                this.driveChoice.SelectedIndex = 0;
            }
            this.swapList.EndUpdate();
            this.upgradeTable.EndUpdate();
            this.upgradesTitle.Text = (current == null) ? ((car == null) ? "Run a trial on a car: its engine swaps, drive conversion and upgrade levels are listed here." : car.Name + ": run a trial with the template chosen.")
                : car.Name + ": " + ported + " of " + swaps + " engine swap(s) ported" + (current.SwapsChanged ? "; CHANGED: Trial Run again to see the database with them (Install uses them as they are)." : ". Tick / untick, change the level, add an FM2 engine or a drive conversion: your choice.");
            this.upgradesFilling = false;
        }

        private void SwapsChanged()
        {
            Trial current = this.CurrentTrial;
            if (current != null)
            {
                current.SwapsChanged = true;
            }
            this.ShowUpgrades();
        }

        private SwapItem SelectedSwap
        {
            get { return (this.swapList.SelectedItems.Count == 1) ? this.swapList.SelectedItems[0].Tag as SwapItem : null; }
        }

        private void SwapTicked(ListViewItem row)
        {
            SwapItem item = row.Tag as SwapItem;
            if (this.upgradesFilling || item == null || item.Included == row.Checked)
            {
                return;
            }
            item.Included = row.Checked;
            this.BeginInvoke((MethodInvoker) this.SwapsChanged);     // not while the list raises its own event
        }

        private void SwapSelected()
        {
            SwapItem item = this.SelectedSwap;
            if (item != null && item.Swap.Level >= 1 && item.Swap.Level <= 3)
            {
                this.upgradesFilling = true;
                this.swapLevel.SelectedIndex = (int) item.Swap.Level - 1;
                this.upgradesFilling = false;
            }
        }

        private void SwapLevelChanged()
        {
            SwapItem item = this.SelectedSwap;
            if (this.upgradesFilling || item == null)
            {
                return;
            }
            item.Swap.Level = this.swapLevel.SelectedIndex + 1;
            this.SwapsChanged();
        }

        private void AddFm2Engine()
        {
            Trial current = this.CurrentTrial;
            int index = this.fm2Engines.SelectedIndex;
            if (current == null || index < 0 || index >= this.fm2EngineList.Count)
            {
                return;
            }
            object[] engine = this.fm2EngineList[index];
            SwapItem item = new SwapItem();
            item.Swap = new CarDataToFm2.Fm2Swap();
            item.Swap.Kind = "fm2";
            item.Swap.Engine = Convert.ToInt64(engine[0], CultureInfo.InvariantCulture);
            item.Swap.Level = this.addLevel.SelectedIndex + 1;
            item.Engine = engine[1] + " (FM2's engine " + engine[0] + ")";
            current.Swaps.Add(item);
            this.SwapsChanged();
        }

        private void RemoveSwap()
        {
            Trial current = this.CurrentTrial;
            SwapItem item = this.SelectedSwap;
            if (current == null || item == null)
            {
                return;
            }
            if (item.Swap.Kind == "source")
            {
                item.Included = false;      // the source's swap stays listed, left out
            }
            else
            {
                current.Swaps.Remove(item);
            }
            this.SwapsChanged();
        }

        private void DriveChanged()
        {
            Trial current = this.CurrentTrial;
            if (this.upgradesFilling || current == null)
            {
                return;
            }
            current.Swaps.RemoveAll(delegate(SwapItem s) { return s.Swap.Kind == "drive"; });
            int drive = this.driveChoice.SelectedIndex;
            if (drive >= 1 && drive <= 3)
            {
                if (drive == current.StockDrive)
                {
                    MessageBox.Show(this, "The car is " + DriveNames[drive] + " already.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    this.ShowUpgrades();
                    return;
                }
                SwapItem item = new SwapItem();
                item.Swap = new CarDataToFm2.Fm2Swap();
                item.Swap.Kind = "drive";
                item.Swap.DriveType = drive;
                item.Swap.Level = this.driveLevel.SelectedIndex + 1;
                item.Engine = "the car's engine as " + DriveNames[drive];
                current.Swaps.Add(item);
            }
            this.SwapsChanged();
        }

        // The swaps a new trial starts from: the ones of the trial before (the user's changes), else the port record's
        // (a car ported before, on the record's template), else the source's swaps, all ticked.
        private static List<SwapItem> StartSwaps(SqliteDatabase source, long sourceCar, Trial before, Fm2PortRecord record, string template, out long stockDrive)
        {
            object drive = source.Scalar("SELECT DriveTypeID FROM Data_Car WHERE Id = ?", sourceCar);
            stockDrive = (drive == null || drive is DBNull) ? 0 : Convert.ToInt64(drive, CultureInfo.InvariantCulture);
            List<SwapItem> items = new List<SwapItem>();
            if (before != null)
            {
                foreach (SwapItem item in before.Swaps)
                {
                    items.Add(item.Copy());
                }
                return items;
            }
            List<CarDataToFm2.Fm2Swap> chosen = (record != null && record.Swaps != null && string.Equals(record.Template, template, StringComparison.OrdinalIgnoreCase)) ? record.Swaps : null;
            foreach (CarDataToFm2.Fm2Swap swap in CarDataToFm2.SourceSwaps(source, sourceCar))
            {
                SwapItem item = new SwapItem();
                item.Swap = swap;
                item.Engine = Convert.ToString(source.Scalar("SELECT MediaName FROM Data_Engine WHERE EngineID = ?", swap.Engine), CultureInfo.InvariantCulture) + " (the source's engine " + swap.Engine + ")";
                if (chosen != null)
                {
                    CarDataToFm2.Fm2Swap kept = chosen.Find(delegate(CarDataToFm2.Fm2Swap s) { return s.Kind == "source" && s.Engine == swap.Engine; });
                    item.Included = kept != null;
                    if (kept != null)
                    {
                        swap.Level = kept.Level;
                    }
                }
                items.Add(item);
            }
            if (chosen != null)
            {
                foreach (CarDataToFm2.Fm2Swap swap in chosen)
                {
                    if (swap.Kind == "source")
                    {
                        continue;
                    }
                    SwapItem item = new SwapItem();
                    item.Swap = CarDataToFm2.Fm2Swap.Parse(swap.ToString());
                    item.Engine = (swap.Kind == "drive") ? "the car's engine as " + DriveText(swap.DriveType) : "FM2's engine " + swap.Engine;
                    items.Add(item);
                }
            }
            return items;
        }
    }
}
