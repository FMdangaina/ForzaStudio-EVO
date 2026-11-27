namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Windows.Forms;

    // What the FM4 window had and the FM3 windows lacked (docs/PORT_PARITY.md D, user 2026-11-27 "fai tutta la D in un colpo"):
    //   D1  the source's PI and class in the car list, the chosen car's on the status line;
    //   D2  Uninstall... (Import): a car this window ported (id above FM3's own) out of the FM3 folder again (CarUninstall);
    //   D3  Upgrades tab: the source's swaps left out / put back, FM3 engines added as swaps, the drive type conversion
    //       (the FM4 window's keys in the car's fixes: CarPort.SwapRemoveKey / DriveKey, CarPortToFm3.Fm3SwapAddKey);
    //   D4  DLC: First car id + Free (the cars take the next free ids from it);
    //   D5  DLC: the pack remembered between sessions (ForzaStudio.ini, PortFm3.Pack*), restored for the same source folder.
    internal sealed partial class CarPortFm3Form
    {
        private Button uninstall;
        private TextBox packFirstCar;
        private ListView swapList;
        private Button addSwap;
        private Button removeSwap;
        private ComboBox driveChoice;
        private Label driveStock;
        private SourceCar swapCar;
        private bool fillingSwaps;

        private sealed class SwapRow
        {
            public long Engine;
            public bool Stock;
            public bool Added;      // an FM3 engine added in this tab
        }

        private sealed class DriveItem
        {
            public string Value;    // null: no choice (as before)
            public string Label;

            public override string ToString()
            {
                return this.Label;
            }
        }

        // ----- D1 -----

        private void ShowSourcePi()
        {
            SourceCar car = (this.cars.SelectedItems.Count == 1) ? (SourceCar) this.cars.SelectedItems[0].Tag : null;
            if (car == null || car.Pi < 0)
            {
                return;
            }
            string tag = GameInfo.Tag(this.sourceGame);
            this.status.Text = string.Format(CultureInfo.InvariantCulture, "{0} ({1}, id {2}): {3}PI {1} {4:0.000} (FM3's PI and class are computed by the port: its log).{5}",
                car.Name, tag, car.Id, (car.Class.Length > 0) ? car.Class + ", " : "", car.Pi, (car.Fm3Id > 0) ? "  In FM3: " + car.Fm3Text + "." : "");
        }

        // ----- D2 -----

        private void BuildUninstallButton(Panel options)
        {
            this.uninstall = new Button();
            this.uninstall.Text = "Uninstall...";
            this.uninstall.Size = new Size(100, 28);
            this.uninstall.Location = new Point(this.install.Right + 6, this.install.Top);
            new ToolTip().SetToolTip(this.uninstall, "Take this ported car out of the FM3 folder again (never one of FM3's own cars): the report lists what goes first");
            this.uninstall.Click += delegate { this.Uninstall(); };
            options.Controls.Add(this.uninstall);
        }

        private void Uninstall()
        {
            SourceCar car = (this.cars.SelectedItems.Count == 1) ? (SourceCar) this.cars.SelectedItems[0].Tag : null;
            string target = this.targetBox.Text.Trim();
            if (car == null || car.Fm3Id <= 0 || !Directory.Exists(target))
            {
                return;
            }
            CarUninstall job = new CarUninstall();
            job.TargetRoot = target;
            job.MediaName = car.Fm3Media;
            this.report.Text = string.Format("Uninstall {0} (car {1}) from {2}: what goes\r\n", job.MediaName, car.Fm3Id, target);
            this.status.Text = "Listing what goes with " + car.Name + "...";
            this.Background(delegate
            {
                List<string> plan = job.Plan();
                return delegate
                {
                    this.report.Text += string.Join("\r\n", plan.ToArray()) + "\r\n";
                    if (MessageBox.Show(this, string.Format("Uninstall {0} (car {1}, {2}) from the FM3 folder?\n\n{3}\n\nThe report lists every database row, string and file that goes (only what the port added and no other car uses). Every file is backed up first to\n{4}\n\nIf you own this car in an FM3 garage, sell it first: the saved car can crash the game.",
                        car.Name, car.Fm3Id, car.Fm3Media, target, job.BackupFolder), this.Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                    {
                        this.report.Text += "\r\ncancelled, nothing changed\r\n";
                        this.status.Text = "Uninstall cancelled.";
                        return;
                    }
                    if (!XeniaGuard.Confirm(this, this.Text, "the Forza Motorsport 3 files"))
                    {
                        return;
                    }
                    this.status.Text = "Uninstalling " + car.Name + "...";
                    this.Background(delegate
                    {
                        job.Report = new StringBuilder();
                        job.Run();
                        return delegate
                        {
                            this.report.Text += "\r\n" + job.Report.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\nuninstalled\r\n";
                            this.report.SelectionStart = this.report.TextLength;
                            this.report.ScrollToCaret();
                            long id = car.Fm3Id;
                            if (string.Equals(car.Fm3Media, car.MediaName, StringComparison.OrdinalIgnoreCase))
                            {
                                car.InFm3 = false;     // the name was the port's: FM3 has no car of that name any more
                            }
                            car.Fm3Id = 0;
                            foreach (ListViewItem item in this.items)
                            {
                                if (item.Tag == car)
                                {
                                    item.SubItems[7].Text = car.Fm3Text;
                                    item.ForeColor = car.InFm3 ? ForzaTheme.TextDim : car.Traffic ? ForzaTheme.Yellow : ForzaTheme.Text;
                                }
                            }
                            this.Filter();
                            this.status.Text = car.Name + " (car " + id + ") is out of the FM3 folder: it can be ported again.";
                        };
                    });
                };
            });
        }

        // ----- D3 -----

        private Panel BuildSwapPanel()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Top;
            panel.Height = 178;

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
            this.swapList.Columns.Add("Swap", 380);
            this.swapList.SelectedIndexChanged += delegate { this.UpdateSwapButtons(); };
            StyleList(this.swapList);
            Panel buttons = new Panel();
            buttons.Dock = DockStyle.Bottom;
            buttons.Height = 34;
            this.addSwap = new Button();
            this.addSwap.Text = "Add FM3 Engine...";
            this.addSwap.Size = new Size(140, 26);
            this.addSwap.Location = new Point(6, 4);
            new ToolTip().SetToolTip(this.addSwap, "Add an engine FM3 has as a swap (next free level; its parts and sound are FM3's)");
            this.addSwap.Click += delegate { this.AddFm3Swap(); };
            this.removeSwap = new Button();
            this.removeSwap.Text = "Remove";
            this.removeSwap.Size = new Size(110, 26);
            this.removeSwap.Location = new Point(152, 4);
            this.removeSwap.Click += delegate { this.ToggleSwap(); };
            buttons.Controls.Add(this.addSwap);
            buttons.Controls.Add(this.removeSwap);
            swaps.Controls.Add(this.swapList);
            swaps.Controls.Add(buttons);
            swaps.Controls.Add(CarPortForm.SectionHeader("Engine swaps"));

            Panel side = new Panel();
            side.Dock = DockStyle.Right;
            side.Width = 360;
            side.Padding = new Padding(10, 0, 0, 0);
            Control header = CarPortForm.SectionHeader("Drive type");
            header.Dock = DockStyle.None;
            header.Bounds = new Rectangle(10, 0, 340, 28);
            this.driveStock = new Label();
            this.driveStock.Location = new Point(14, 36);
            this.driveStock.AutoSize = true;
            this.driveChoice = new ComboBox();
            this.driveChoice.DropDownStyle = ComboBoxStyle.DropDownList;
            this.driveChoice.Location = new Point(14, 58);
            this.driveChoice.Width = 330;
            this.driveChoice.SelectedIndexChanged += delegate { this.DriveChosen(); };
            Label note = new Label();
            note.Location = new Point(14, 90);
            note.Size = new Size(336, 80);
            note.ForeColor = ForzaTheme.TextDim;
            note.Text = "A conversion is an upgrade in the shop (a drivetrain of the other type at the next level), as FM3's own. "
                + "The choices are used by the next trial run, the install and the DLC pack.";
            side.Controls.Add(header);
            side.Controls.Add(this.driveStock);
            side.Controls.Add(this.driveChoice);
            side.Controls.Add(note);

            panel.Controls.Add(swaps);
            panel.Controls.Add(side);
            this.ShowSwaps(null);
            return panel;
        }

        // The car's own choices (the review's fixes and the Upgrades tab's), made when first written.
        private Dictionary<string, object> ChoicesOf(SourceCar car)
        {
            Dictionary<string, object> chosen;
            if (!this.fixes.TryGetValue(car.MediaName, out chosen))
            {
                chosen = new Dictionary<string, object>();
                this.fixes[car.MediaName] = chosen;
            }
            return chosen;
        }

        private void ShowSwaps(SourceCar car)
        {
            if (this.swapList == null)
            {
                return;
            }
            this.swapCar = car;
            this.fillingSwaps = true;
            this.swapList.Items.Clear();
            this.driveChoice.Items.Clear();
            this.addSwap.Enabled = car != null;
            this.driveChoice.Enabled = car != null;
            this.driveStock.Text = "";
            if (car == null)
            {
                this.fillingSwaps = false;
                this.UpdateSwapButtons();
                return;
            }
            Dictionary<string, object> chosen;
            if (!this.fixes.TryGetValue(car.MediaName, out chosen))
            {
                chosen = new Dictionary<string, object>();
            }
            string stock = "?";
            if (this.sourceGame != ForzaGame.Motorsport2)
            {
                try
                {
                    using (SqliteDatabase db = SqliteDatabase.FromBytes(File.ReadAllBytes(Db(car.Root ?? this.sourceBox.Text.Trim()))))
                    {
                        foreach (object[] row in db.Query("SELECT u.Level, u.EngineID, e.EngineName, e.MediaName FROM List_UpgradeEngine u LEFT JOIN Data_Engine e ON e.EngineID = u.EngineID WHERE u.Ordinal = ? ORDER BY u.Level", car.Id).Rows)
                        {
                            long level = Convert.ToInt64(row[0], CultureInfo.InvariantCulture);
                            long engine = Convert.ToInt64(row[1], CultureInfo.InvariantCulture);
                            SwapRow tag = new SwapRow();
                            tag.Engine = engine;
                            tag.Stock = level == 0;
                            bool removed = chosen.ContainsKey(CarPort.SwapRemoveKey + engine.ToString(CultureInfo.InvariantCulture));
                            string media = row[3] as string;
                            string present;
                            bool inFm3 = media != null && this.fm3Engines.TryGetValue(engine, out present) && string.Equals(present, media, StringComparison.OrdinalIgnoreCase);
                            ListViewItem item = new ListViewItem(level.ToString(CultureInfo.InvariantCulture));
                            item.SubItems.Add(string.IsNullOrEmpty(row[2] as string) ? "engine " + engine : (string) row[2]);
                            item.SubItems.Add(media ?? "");
                            item.SubItems.Add(tag.Stock ? "stock engine" : removed ? "left out (Put Back to keep it)" : "the source's swap" + (inFm3 ? " (FM3 has this engine)" : ", copied into FM3 by the port"));
                            item.Tag = tag;
                            if (removed)
                            {
                                item.ForeColor = ForzaTheme.TextDim;
                            }
                            this.swapList.Items.Add(item);
                        }
                        object type = db.Scalar("SELECT d.DrivetypeID FROM List_UpgradeDrivetrain u JOIN Data_Drivetrain d ON d.DrivetrainID = u.DrivetrainID WHERE u.Ordinal = ? AND u.Level = 0", car.Id);
                        stock = (type == null || type is DBNull) ? "?" : DriveName(Convert.ToInt64(type, CultureInfo.InvariantCulture));
                    }
                }
                catch (Exception e)
                {
                    this.swapList.Items.Add(new ListViewItem(new string[] { "", "the source's swaps not read: " + e.Message, "", "" }));
                }
            }
            else
            {
                this.swapList.Items.Add(new ListViewItem(new string[] { "", "FM2 car: its swaps come with its FM4 form (step 1)", "", "FM3 engines can be added" }));
            }
            foreach (KeyValuePair<string, object> fix in chosen)
            {
                long engine;
                if (!fix.Key.StartsWith(CarPortToFm3.Fm3SwapAddKey, StringComparison.Ordinal)
                    || !long.TryParse(fix.Key.Substring(CarPortToFm3.Fm3SwapAddKey.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out engine))
                {
                    continue;
                }
                string media;
                this.fm3Engines.TryGetValue(engine, out media);
                ListViewItem item = new ListViewItem("+");
                item.SubItems.Add("FM3 engine " + engine);
                item.SubItems.Add(media ?? "");
                item.SubItems.Add(string.Format(CultureInfo.InvariantCulture, "FM3 engine added here, {0} Cr (next free level)", fix.Value));
                SwapRow tag = new SwapRow();
                tag.Engine = engine;
                tag.Added = true;
                item.Tag = tag;
                this.swapList.Items.Add(item);
            }
            this.driveStock.Text = "Stock: " + stock + ".";
            this.AddDrive(null, "No choice (as before)");
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
            this.fillingSwaps = false;
            this.UpdateSwapButtons();
        }

        private void AddDrive(string value, string label)
        {
            DriveItem item = new DriveItem();
            item.Value = value;
            item.Label = label;
            this.driveChoice.Items.Add(item);
        }

        private static string DriveName(long type)
        {
            return (type == 1) ? "FWD" : (type == 2) ? "RWD" : (type == 3) ? "AWD" : "?";
        }

        private SwapRow SelectedSwap
        {
            get { return (this.swapList.SelectedItems.Count == 1) ? this.swapList.SelectedItems[0].Tag as SwapRow : null; }
        }

        private void UpdateSwapButtons()
        {
            SwapRow row = this.SelectedSwap;
            Dictionary<string, object> chosen;
            bool removed = row != null && this.swapCar != null && this.fixes.TryGetValue(this.swapCar.MediaName, out chosen)
                && chosen.ContainsKey(CarPort.SwapRemoveKey + row.Engine.ToString(CultureInfo.InvariantCulture));
            this.removeSwap.Enabled = row != null && !row.Stock;
            this.removeSwap.Text = removed ? "Put Back" : "Remove";
        }

        // Remove: an added FM3 engine goes, a source swap is left out (or put back).
        private void ToggleSwap()
        {
            SwapRow row = this.SelectedSwap;
            SourceCar car = this.swapCar;
            if (row == null || row.Stock || car == null)
            {
                return;
            }
            Dictionary<string, object> chosen = this.ChoicesOf(car);
            string engine = row.Engine.ToString(CultureInfo.InvariantCulture);
            if (row.Added)
            {
                chosen.Remove(CarPortToFm3.Fm3SwapAddKey + engine);
            }
            else if (!chosen.Remove(CarPort.SwapRemoveKey + engine))
            {
                chosen[CarPort.SwapRemoveKey + engine] = true;
            }
            this.SwapsChanged(car);
        }

        private void AddFm3Swap()
        {
            SourceCar car = this.swapCar;
            string target = this.targetBox.Text.Trim();
            if (car == null || !Directory.Exists(target))
            {
                return;
            }
            List<object[]> rows = new List<object[]>();
            using (SqliteDatabase db = SqliteDatabase.FromBytes(File.ReadAllBytes(Db(target))))
            {
                foreach (object[] engine in db.Query("SELECT e.EngineID, e.EngineName, e.MediaName, e.[EngineMass-kg], (SELECT count(DISTINCT Ordinal) FROM List_UpgradeEngine u WHERE u.EngineID = e.EngineID) FROM Data_Engine e ORDER BY e.EngineName").Rows)
                {
                    rows.Add(engine);
                }
            }
            Panel extra = new Panel();
            extra.Size = new Size(520, 50);
            NumericUpDown price = PickDialog.NumberField(extra, "Price (credits)", 0, 6800, 10000000, 0);
            using (PickDialog dialog = new PickDialog("Add FM3 Engine Swap - " + car.Name, new string[] { "Id", "Engine", "Media name", "Mass (kg)", "Cars" }, new int[] { 50, 300, 150, 70, 45 }, rows, extra))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedKey == null)
                {
                    return;
                }
                long engine = Convert.ToInt64(dialog.SelectedKey, CultureInfo.InvariantCulture);
                foreach (ListViewItem item in this.swapList.Items)
                {
                    SwapRow row = item.Tag as SwapRow;
                    if (row != null && row.Engine == engine && (row.Added || this.fm3Engines.ContainsKey(engine)))
                    {
                        MessageBox.Show(this, "The car has that engine already.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                }
                this.ChoicesOf(car)[CarPortToFm3.Fm3SwapAddKey + engine.ToString(CultureInfo.InvariantCulture)] = (long) price.Value;
            }
            this.SwapsChanged(car);
        }

        private void DriveChosen()
        {
            DriveItem item = this.driveChoice.SelectedItem as DriveItem;
            SourceCar car = this.swapCar;
            if (this.fillingSwaps || item == null || car == null)
            {
                return;
            }
            Dictionary<string, object> chosen = this.ChoicesOf(car);
            if (item.Value == null)
            {
                chosen.Remove(CarPort.DriveKey);
            }
            else
            {
                chosen[CarPort.DriveKey] = item.Value;
            }
            this.SwapsChanged(car);
        }

        // A choice made: shown again; a pack car's trial no longer holds.
        private void SwapsChanged(SourceCar car)
        {
            this.ShowSwaps(car);
            if (this.mode == PortMode.Dlc && this.packTrialFolder != null && this.pack.Exists(delegate(PackItem p) { return p.Car == car; }))
            {
                this.packTrialFolder = null;
                this.status.Text = car.Name + ": upgrades changed, Trial Pack again.";
            }
            else
            {
                this.status.Text = car.Name + ": upgrades changed, used by the next trial run" + ((this.mode == PortMode.Dlc) ? " and the pack." : " and the install.");
            }
            if (this.mode == PortMode.Dlc)
            {
                this.SavePack();
            }
            this.UpdateButtons();
        }

        // ----- D6: Add To Package (as the FM4 window's) -----

        private Button addToPackage;

        private void AddToPackage()
        {
            string target = this.targetBox.Text.Trim();
            string trial = this.packTrialFolder;
            string content = this.XeniaContent();
            if (trial == null || content == null || !Directory.Exists(target))
            {
                return;
            }
            string package = CarPortFm2Form.PickPackage(this, ForzaGame.Motorsport3, content, this.Text);
            if (package == null)
            {
                return;
            }
            if (MessageBox.Show(this, "Add the " + this.pack.Count + " car(s) of the last Trial Pack to\n" + package + "?\n\nThe package keeps its offer and its cars; the new cars' rows, strings and files are added. "
                + (BackupPolicy.Enabled ? "The original is kept in bak\\ first." : "Backups are off: the original is not kept."), this.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            if (!XeniaGuard.Confirm(this, this.Text, "FM3's DLC packages"))
            {
                return;
            }
            string name = File.Exists(package) ? null : this.packName.Text.Trim();
            this.status.Text = "Adding the cars to " + Path.GetFileName(package) + "...";
            this.Background(delegate
            {
                StringBuilder text = new StringBuilder();
                text.AppendLine("Add To Package: " + package);
                string failed = null;
                try
                {
                    DlcPackageAdd.AddFm(ForzaGame.Motorsport3, package, target, trial, content, name, text);
                    DlcPackage.RefreshNamesPack(ForzaGame.Motorsport3, target, content, text);
                }
                catch (Exception e)
                {
                    failed = e.Message;
                }
                return delegate
                {
                    this.report.Text = ((failed != null) ? "ADD TO PACKAGE FAILED: " + failed + "\r\nThe package was not changed.\r\n\r\n" : "") + text.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
                    this.status.Text = (failed != null) ? "Add To Package failed: " + failed : "The cars are in " + Path.GetFileName(package) + ". Start FM3 in Xenia.";
                };
            });
        }

        // ----- D4 -----

        private void BuildFirstCarRow(Panel panel)
        {
            Label label = new Label();
            label.Text = "First car id:";
            label.Location = new Point(190, 69);
            label.AutoSize = true;
            this.packFirstCar = new TextBox();
            this.packFirstCar.Location = new Point(262, 66);
            this.packFirstCar.Width = 50;
            this.packFirstCar.MaxLength = 6;
            this.packFirstCar.Text = AppSettings.Get("PortFm3.PackFirstCar", "");
            this.packFirstCar.TextChanged += delegate
            {
                AppSettings.Set("PortFm3.PackFirstCar", this.packFirstCar.Text.Trim());
                this.packTrialFolder = null;
                this.UpdateButtons();
            };
            new ToolTip().SetToolTip(this.packFirstCar, "The pack's first car id: the cars take the next free ones from it, never one of the FM3 folder or of another installed pack (empty: the first free id)");
            Button free = new Button();
            free.Text = "Free";
            free.Location = new Point(316, 65);
            free.Size = new Size(46, 24);
            new ToolTip().SetToolTip(free, "Put the first car id above FM3's own that neither the FM3 folder nor an installed pack uses");
            free.Click += delegate { this.SuggestFirstCar(); };
            panel.Controls.Add(label);
            panel.Controls.Add(this.packFirstCar);
            panel.Controls.Add(free);
        }

        private long FirstCar()
        {
            long id;
            return (this.packFirstCar != null && long.TryParse(this.packFirstCar.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0) ? id : 0;
        }

        // The installed packs' merges but this pack's own (its car ids are given again by the trial).
        private static List<string> OtherPacks3(string content, int offer)
        {
            List<string> others = DbIds.PackDatabases(content);
            if (content != null && offer > 0)
            {
                string own = DlcPackage.PackagePathFm3(content, offer);
                others.RemoveAll(delegate(string path) { return path.StartsWith(own, StringComparison.OrdinalIgnoreCase); });
            }
            return others;
        }

        // The first car id from 'from' on that neither the database nor an installed pack uses (0: none in 100000).
        private static long FreeFm3CarId(SqliteDatabase db, long from, Dictionary<long, string> packCars)
        {
            for (long id = from; id < from + 100000; id++)
            {
                if (DbIds.ChosenCarIdProblem(db, id, packCars, ForzaGame.Motorsport3) == null)
                {
                    return id;
                }
            }
            return 0;
        }

        private void SuggestFirstCar()
        {
            string target = this.targetBox.Text.Trim();
            if (!Directory.Exists(target))
            {
                return;
            }
            try
            {
                using (SqliteDatabase db = SqliteDatabase.FromBytes(File.ReadAllBytes(Db(target))))
                {
                    long from = Math.Max(GameIdLimits.MaxCarBase(ForzaGame.Motorsport3), 0) + 1;
                    long id = FreeFm3CarId(db, from, DbIds.PackCarIds(OtherPacks3(this.XeniaContent(), this.Offer())));
                    if (id > 0)
                    {
                        this.packFirstCar.Text = id.ToString(CultureInfo.InvariantCulture);
                    }
                }
            }
            catch (Exception e)
            {
                this.status.Text = "The free car id was not found: " + e.Message;
            }
        }

        // ----- D5 -----

        private const char PackField = '|';
        private const string PackFix = ";;";

        private static string FixesText(Dictionary<string, object> chosen)
        {
            List<string> parts = new List<string>();
            if (chosen != null)
            {
                foreach (KeyValuePair<string, object> fix in chosen)
                {
                    string value = (fix.Value is string) ? "s:" + (string) fix.Value
                        : (fix.Value is bool) ? "b:" + ((bool) fix.Value ? "1" : "0")
                        : (fix.Value is long || fix.Value is int) ? "l:" + Convert.ToInt64(fix.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
                        : (fix.Value is double) ? "d:" + ((double) fix.Value).ToString("R", CultureInfo.InvariantCulture)
                        : null;
                    if (value != null && fix.Key.IndexOf('=') < 0)
                    {
                        parts.Add(fix.Key + "=" + value);
                    }
                }
            }
            return string.Join(PackFix, parts.ToArray());
        }

        private static Dictionary<string, object> ParseFixes(string text)
        {
            Dictionary<string, object> chosen = new Dictionary<string, object>();
            foreach (string part in text.Split(new string[] { PackFix }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0 || part.Length < eq + 3 || part[eq + 2] != ':')
                {
                    continue;
                }
                string key = part.Substring(0, eq);
                string value = part.Substring(eq + 3);
                switch (part[eq + 1])
                {
                    case 's': chosen[key] = value; break;
                    case 'b': chosen[key] = value == "1"; break;
                    case 'l': long l; if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) { chosen[key] = l; } break;
                    case 'd': double d; if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) { chosen[key] = d; } break;
                }
            }
            return chosen;
        }

        // Every change of the pack (and of a pack car's upgrades) is saved at once: car, its DLC pack, template, choices, fixes.
        private void SavePack()
        {
            if (this.mode != PortMode.Dlc)
            {
                return;
            }
            AppSettings.Set("PortFm3.PackSource", this.sourceBox.Text.Trim());
            int old;
            int.TryParse(AppSettings.Get("PortFm3.PackCars", "0"), NumberStyles.None, CultureInfo.InvariantCulture, out old);
            for (int i = 0; i < Math.Max(old, this.pack.Count); i++)
            {
                string key = "PortFm3.PackCar" + i.ToString(CultureInfo.InvariantCulture);
                if (i >= this.pack.Count)
                {
                    AppSettings.Set(key, "");
                    continue;
                }
                PackItem item = this.pack[i];
                CarChoices c = item.Choices ?? new CarChoices();
                Dictionary<string, object> chosen;
                this.fixes.TryGetValue(item.Car.MediaName, out chosen);
                string[] fields =
                {
                    item.Car.MediaName, item.Car.Dlc ?? "", item.Donor ?? "", c.Arcade ? "1" : "0",
                    c.MakeId.HasValue ? c.MakeId.Value.ToString(CultureInfo.InvariantCulture) : "", c.Sound ?? "",
                    (c.Brakes != null) ? c.Brakes.ToString() : "", FixesText(chosen)
                };
                AppSettings.Set(key, string.Join(PackField.ToString(), fields));
            }
            AppSettings.Set("PortFm3.PackCars", this.pack.Count.ToString(CultureInfo.InvariantCulture));
        }

        // After Load Cars: the pack saved for this source folder, its cars found again in the list (another source: an empty
        // pack, the saved one left as it is).
        private void RestorePack(List<SourceCar> list, string source)
        {
            if (!string.Equals(AppSettings.Get("PortFm3.PackSource", ""), source, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            int count;
            int.TryParse(AppSettings.Get("PortFm3.PackCars", "0"), NumberStyles.None, CultureInfo.InvariantCulture, out count);
            StringBuilder notes = new StringBuilder();
            List<PackItem> restored = new List<PackItem>();
            for (int i = 0; i < count; i++)
            {
                string[] f = AppSettings.Get("PortFm3.PackCar" + i.ToString(CultureInfo.InvariantCulture), "").Split(PackField);
                if (f.Length < 8)
                {
                    continue;
                }
                SourceCar car = list.Find(delegate(SourceCar c) { return string.Equals(c.MediaName, f[0], StringComparison.OrdinalIgnoreCase) && string.Equals(c.Dlc ?? "", f[1], StringComparison.OrdinalIgnoreCase); });
                if (car == null)
                {
                    notes.AppendLine("pack: " + f[0] + (f[1].Length > 0 ? " (" + f[1] + ")" : "") + " is not in the source any more, left out");
                    continue;
                }
                PackItem item = new PackItem();
                item.Car = car;
                item.Donor = f[2];
                item.Choices = new CarChoices();
                item.Choices.Arcade = f[3] != "0";
                long make;
                if (long.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out make))
                {
                    item.Choices.MakeId = make;
                }
                item.Choices.Sound = (f[5].Length > 0) ? f[5] : null;
                item.Choices.Brakes = (f[6].Length > 0) ? CarPort.BrakeChoice.Parse(f[6]) : null;
                Dictionary<string, object> chosen = ParseFixes(f[7]);
                if (chosen.Count > 0)
                {
                    this.fixes[car.MediaName] = chosen;
                }
                restored.Add(item);
            }
            if (restored.Count == 0 && notes.Length == 0)
            {
                return;
            }
            this.pack.Clear();
            this.pack.AddRange(restored);
            string status = this.status.Text;
            this.PackChanged();     // the list, and saved again without the cars left out
            this.status.Text = restored.Count + " car(s) in the pack (kept from before). " + status;
            if (notes.Length > 0)
            {
                this.report.AppendText("\r\n" + notes.ToString());
            }
        }
    }
}
