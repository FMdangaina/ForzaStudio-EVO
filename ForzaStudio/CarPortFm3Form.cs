namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Windows.Forms;

    // Tools > Import Car From Another Forza > Into FM3 > From FM4, and Tools > Create DLC Package > For FM3 (CarPortToFm3, DlcPackage.Fm3).
    // The same screen as the FM4 window (CarPortForm): brand bar and game banner, the two game folders, the cars, the selected car's
    // template, the pack on the right (Dlc), the report under both. The folders start from Tools > Settings (FM4 = the port target,
    // FM3 = Folder.FM3, Xenia) and the last ones used. Own class because CarPortForm is built around FM4 as the target.
    internal sealed class CarPortFm3Form : Form
    {
        private sealed class SourceCar
        {
            public long Id;
            public string MediaName;
            public string Name;
            public long Year;
            public string Root;         // the staged DLC pack the car comes from, or null for the FM4 folder itself
            public string Dlc;          // the pack's name, or ""
            public bool InFm3;
        }

        // Dlc mode: cars go into a pack list, then Trial Pack, Build DLC Package / Build for Xbox 360.
        private sealed class PackItem
        {
            public SourceCar Car;
            public string Donor;
        }

        private readonly PortMode mode;
        private readonly TextBox sourceBox;
        private readonly TextBox targetBox;
        private readonly Button load;
        private readonly TextBox search;
        private readonly CheckBox showInFm3;
        private readonly CheckBox includeDlc;
        private ListView reviewList;
        private ListView upgradesList;
        private Label upgradesTitle;
        private Dictionary<long, string> fm3Engines = new Dictionary<long, string>();
        private HashSet<long> fm3Drivetrains = new HashSet<long>();
        private readonly ListView cars;
        private readonly ComboBox donors;
        private readonly Button trial;
        private readonly Button install;
        private readonly TextBox report;
        private readonly ToolStripStatusLabel status;
        private readonly List<string> packFiles = new List<string>();
        private readonly List<ListViewItem> items = new List<ListViewItem>();
        private readonly List<PackItem> pack = new List<PackItem>();
        private TextBox xeniaBox;
        private ListView packList;
        private TextBox packName;
        private TextBox packOffer;
        private Label packInfo;
        private Button addToPack;
        private Button removeFromPack;
        private Button newPack;
        private Button packTrial;
        private Button buildXenia;
        private Button buildXbox;
        private Button removeXenia;
        private string packTrialFolder;
        private bool busy;

        public CarPortFm3Form()
            : this(PortMode.Import)
        {
        }

        public CarPortFm3Form(PortMode mode)
        {
            this.mode = mode;
            this.Text = (mode == PortMode.Dlc) ? "Create DLC Package" : "Import Car From Another Forza";
            this.Size = new Size(1360, 900);
            this.MinimumSize = new Size(1100, 720);
            this.StartPosition = FormStartPosition.CenterParent;
            this.Font = ForzaTheme.Regular;
            try
            {
                this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (ArgumentException)
            {
            }

            // Brand bar, as in the FM4 window.
            MenuStrip header = new MenuStrip();
            ToolStripLabel forza = new ToolStripLabel("FORZA");
            forza.Font = new Font("Segoe UI", 11f, FontStyle.Bold | FontStyle.Italic);
            forza.ForeColor = ForzaTheme.Red;
            forza.Margin = new Padding(6, 1, 0, 2);
            ToolStripLabel studio = new ToolStripLabel("STUDIO EVO");
            studio.Font = new Font("Segoe UI Light", 11f, FontStyle.Italic);
            studio.Margin = new Padding(0, 1, 18, 2);
            ToolStripLabel title = new ToolStripLabel((mode == PortMode.Dlc) ? "CREATE A DLC PACKAGE FOR FORZA MOTORSPORT 3" : "IMPORT A CAR FROM FORZA MOTORSPORT 4 INTO FORZA MOTORSPORT 3");
            title.Font = ForzaTheme.Bold;
            title.ForeColor = ForzaTheme.TextDim;
            header.Items.Add(forza);
            header.Items.Add(studio);
            header.Items.Add(title);

            StatusStrip statusStrip = new StatusStrip();
            this.status = new ToolStripStatusLabel("Choose the two game folders and load the cars.");
            this.status.Spring = true;
            this.status.TextAlign = ContentAlignment.MiddleLeft;
            statusStrip.Items.Add(this.status);

            GameBanner banner = new GameBanner("FORZA MOTORSPORT 3", (mode == PortMode.Dlc) ? "DLC PACKAGE" : "FROM  FORZA MOTORSPORT 4", Color.FromArgb(214, 170, 40));
            banner.Dock = DockStyle.Top;

            // Game folders: the start values come from the settings.
            Panel folders = new Panel();
            folders.Dock = DockStyle.Top;
            folders.Height = 74;
            folders.Width = this.ClientSize.Width;   // the rows are placed and anchored from this width (a Panel starts 200 wide)
            this.sourceBox = this.FolderRow(folders, "Forza Motorsport 4 (source, DLC):", 10, DefaultSource(), true);
            this.targetBox = this.FolderRow(folders, "Forza Motorsport 3:", 42, DefaultTarget(mode), false);
            this.load = new Button();
            this.load.Text = "Load Cars";
            this.load.Size = new Size(110, 56);
            this.load.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            this.load.Location = new Point(folders.Width - 122, 9);
            this.load.Click += delegate { this.LoadCars(); };
            folders.Controls.Add(this.load);

            // Cars (left): search, list, then the selected car's options.
            Panel filter = new Panel();
            filter.Dock = DockStyle.Top;
            filter.Height = 32;
            Label find = new Label();
            find.Text = "Search:";
            find.Location = new Point(8, 9);
            find.AutoSize = true;
            this.search = new TextBox();
            this.search.Location = new Point(62, 7);
            this.search.Width = 260;
            this.search.TextChanged += delegate { this.Filter(); };
            this.showInFm3 = new CheckBox();
            this.showInFm3.Text = "Also show cars FM3 already has (for tests)";
            this.showInFm3.Location = new Point(338, 7);
            this.showInFm3.AutoSize = true;
            this.showInFm3.CheckedChanged += delegate { this.Filter(); };
            filter.Controls.Add(find);
            filter.Controls.Add(this.search);
            this.includeDlc = new CheckBox();
            this.includeDlc.Text = "Include the game's DLC";
            this.includeDlc.Checked = AppSettings.Get("PortFm3.IncludeDlc", "0") == "1";
            this.includeDlc.Location = new Point(640, 7);
            this.includeDlc.AutoSize = true;
            this.includeDlc.CheckedChanged += delegate
            {
                AppSettings.Set("PortFm3.IncludeDlc", this.includeDlc.Checked ? "1" : "0");
                if (this.items.Count > 0 && !this.busy)
                {
                    this.LoadCars();    // the DLC are read with the cars
                }
            };
            filter.Controls.Add(this.showInFm3);
            filter.Controls.Add(this.includeDlc);

            this.cars = new ListView();
            this.cars.Dock = DockStyle.Fill;
            this.cars.View = View.Details;
            this.cars.FullRowSelect = true;
            this.cars.MultiSelect = false;
            this.cars.HideSelection = false;
            this.cars.Columns.Add("Id", 56);
            this.cars.Columns.Add("Car", 240);
            this.cars.Columns.Add("Year", 50);
            this.cars.Columns.Add("Media name", 190);
            this.cars.Columns.Add("DLC pack", 220);
            this.cars.Columns.Add("In FM3", 220);
            this.cars.SelectedIndexChanged += delegate { this.UpdateButtons(); this.ShowUpgrades(); };
            StyleList(this.cars);

            Panel options = new Panel();
            options.Dock = DockStyle.Fill;
            Label donorLabel = new Label();
            donorLabel.Text = "FM3 template car, for what FM4's car lacks (your choice):";
            donorLabel.Location = new Point(8, 8);
            donorLabel.AutoSize = true;
            this.donors = new ComboBox();
            this.donors.DropDownStyle = ComboBoxStyle.DropDownList;
            this.donors.Location = new Point(8, 28);
            this.donors.Width = 460;
            this.donors.MaxDropDownItems = 24;
            this.donors.SelectedIndexChanged += delegate { this.UpdateButtons(); };
            this.trial = new Button();
            this.trial.Text = "Trial Run";
            this.trial.Size = new Size(110, 28);
            this.trial.Location = new Point(8, 62);
            this.trial.Click += delegate { this.Port(false); };
            this.install = new Button();
            this.install.Text = "Install In FM3...";
            this.install.Size = new Size(130, 28);
            this.install.Location = new Point(124, 62);
            this.install.Click += delegate { this.Port(true); };
            options.Controls.Add(donorLabel);
            options.Controls.Add(this.donors);
            if (mode == PortMode.Import)
            {
                options.Controls.Add(this.trial);
                options.Controls.Add(this.install);
            }
            Panel selectedCar = new Panel();
            selectedCar.Dock = DockStyle.Bottom;
            selectedCar.Height = (mode == PortMode.Import) ? 124 : 84;
            selectedCar.Controls.Add(options);
            selectedCar.Controls.Add(CarPortForm.SectionHeader("Selected car"));

            Panel carsArea = new Panel();
            carsArea.Dock = DockStyle.Fill;
            carsArea.Controls.Add(this.cars);
            carsArea.Controls.Add(selectedCar);
            carsArea.Controls.Add(filter);
            carsArea.Controls.Add(CarPortForm.SectionHeader("Cars"));

            // Report under both areas, as a tab like the FM4 window's.
            this.report = new TextBox();
            this.report.Dock = DockStyle.Fill;
            this.report.Multiline = true;
            this.report.ReadOnly = true;
            this.report.ScrollBars = ScrollBars.Both;
            this.report.WordWrap = false;
            this.report.Font = new Font("Consolas", 9f);
            TabControl bottomTabs = new TabControl();
            TabPage reportPage = new TabPage("Report");
            reportPage.Controls.Add(this.report);
            bottomTabs.TabPages.Add(reportPage);
            bottomTabs.TabPages.Add(this.BuildReviewPage());
            bottomTabs.TabPages.Add(this.BuildUpgradesPage());

            SplitContainer top = new SplitContainer();
            top.Dock = DockStyle.Fill;
            top.FixedPanel = FixedPanel.Panel2;
            top.Panel1.Controls.Add(carsArea);
            if (mode == PortMode.Dlc)
            {
                Panel transfer = this.BuildTransfer();
                top.Panel2.Controls.Add(this.BuildPackPanel());
                top.Panel2.Controls.Add(transfer);
            }
            else
            {
                top.Panel2Collapsed = true;     // no pack here
            }
            SplitContainer body = new SplitContainer();
            body.Dock = DockStyle.Fill;
            body.Orientation = Orientation.Horizontal;
            body.FixedPanel = FixedPanel.Panel2;
            body.Panel1.Controls.Add(top);
            body.Panel2.Controls.Add(CarPortForm.TabHost(bottomTabs));
            this.Load += delegate
            {
                body.SplitterDistance = Math.Max(200, body.Height - 230);
                if (mode == PortMode.Dlc)
                {
                    top.SplitterDistance = Math.Max(400, top.Width - 380 - 112);
                }
            };

            this.Controls.Add(body);
            this.Controls.Add(folders);
            this.Controls.Add(statusStrip);
            this.Controls.Add(banner);
            this.Controls.Add(header);
            this.MainMenuStrip = header;
            this.BackColor = ForzaTheme.Background;
            this.ForeColor = ForzaTheme.Text;
            ForzaTheme.Apply(this);
            ForzaTheme.GreenItems(this.donors);
            this.UpdateButtons();
        }

        // ----- the settings give the start values -----

        // The FM4 folder: the last one used here, else the port target of Tools > Settings.
        private static string DefaultSource()
        {
            string last = AppSettings.Get("PortFm3.Source", "");
            if (last.Length > 0 && Directory.Exists(last))
            {
                return last;
            }
            return GameInfo.ConfiguredFolder(ForzaGame.Motorsport4) ?? "";
        }

        // The FM3 folder: the last one used for this window, else Tools > Settings > Folder.FM3; an import goes to its test copy
        // (<folder>_DEV) when there is one, never to the clean game.
        private static string DefaultTarget(PortMode mode)
        {
            string key = (mode == PortMode.Dlc) ? "PortFm3.DlcTarget" : "PortFm3.Target";
            string last = AppSettings.Get(key, "");
            if (last.Length > 0 && Directory.Exists(last))
            {
                return last;
            }
            string configured = GameInfo.ConfiguredFolder(ForzaGame.Motorsport3);
            if (configured == null)
            {
                return "";
            }
            string dev = configured.TrimEnd('\\') + "_DEV";
            return (mode == PortMode.Import && Directory.Exists(dev)) ? dev : configured;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (Directory.Exists(this.sourceBox.Text) && Directory.Exists(this.targetBox.Text))
            {
                this.LoadCars();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (this.busy)
            {
                e.Cancel = true;
            }
            base.OnFormClosing(e);
        }

        // A folder row as in the FM4 window: caption, box, Folder... (and Package... for the source).
        private TextBox FolderRow(Panel parent, string caption, int y, string value, bool packages)
        {
            Label label = new Label();
            label.Text = caption;
            label.Location = new Point(10, y + 4);
            label.AutoSize = true;
            TextBox box = new TextBox();
            box.Location = new Point(215, y);
            box.Width = parent.Width - 215 - 290;
            box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            box.Text = value;
            Button browse = new Button();
            browse.Text = "Folder...";
            browse.Size = new Size(72, 26);
            browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            browse.Location = new Point(parent.Width - 282, y - 2);
            browse.Click += delegate
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.SelectedPath = box.Text;
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        box.Text = dialog.SelectedPath;
                    }
                }
            };
            parent.Controls.Add(label);
            parent.Controls.Add(box);
            parent.Controls.Add(browse);
            if (packages)
            {
                Button package = new Button();
                package.Text = "Package...";
                package.Size = new Size(72, 26);
                package.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                package.Location = new Point(parent.Width - 206, y - 2);
                new ToolTip().SetToolTip(package, "FM4 DLC package files (Xbox 360): their cars are listed with the game's. Press again to add more.");
                package.Click += delegate { this.ChoosePackages(); };
                parent.Controls.Add(package);
            }
            return box;
        }

        private void ChoosePackages()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "FM4 DLC packages (Xbox 360 package files)";
                dialog.Multiselect = true;
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                foreach (string file in dialog.FileNames)
                {
                    if (!this.packFiles.Contains(file))
                    {
                        this.packFiles.Add(file);
                    }
                }
            }
            this.status.Text = this.packFiles.Count + " FM4 DLC package(s) added: Load Cars lists their cars with the game's.";
            if (Directory.Exists(this.sourceBox.Text) && Directory.Exists(this.targetBox.Text))
            {
                this.LoadCars();
            }
        }

        // Lists drawn like the FM4 window's: upper-case headers over a red rule, alternating dark rows, the selection in Forza red.
        private static void StyleList(ListView list)
        {
            list.OwnerDraw = true;
            list.Resize += delegate { FillLastColumn(list); };
            list.DrawColumnHeader += delegate(object sender, DrawListViewColumnHeaderEventArgs e)
            {
                using (Brush brush = new SolidBrush(ForzaTheme.Background))
                {
                    e.Graphics.FillRectangle(brush, e.Bounds);
                }
                Rectangle text = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.Header.Text.ToUpperInvariant(), ForzaTheme.Bold, text, ForzaTheme.TextDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                using (Pen pen = new Pen(ForzaTheme.Red))
                {
                    e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                }
            };
            list.DrawItem += delegate(object sender, DrawListViewItemEventArgs e) { };
            list.DrawSubItem += delegate(object sender, DrawListViewSubItemEventArgs e)
            {
                bool selected = e.Item.Selected;
                Color back = selected ? ForzaTheme.Selection : ((e.ItemIndex % 2 == 0) ? ForzaTheme.Surface : Color.FromArgb(31, 31, 34));
                using (Brush brush = new SolidBrush(back))
                {
                    e.Graphics.FillRectangle(brush, e.Bounds);
                }
                Rectangle text = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height);
                bool dim = e.Item.ForeColor == ForzaTheme.TextDim;
                Color fore = selected ? Color.White : ((e.ColumnIndex == 0 || dim) ? ForzaTheme.TextDim : ForzaTheme.Text);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, ForzaTheme.Regular, text, fore, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
        }

        private static void FillLastColumn(ListView list)
        {
            if (list.Columns.Count == 0 || !list.IsHandleCreated || list.Disposing || list.IsDisposed || list.FindForm() == null || list.FindForm().Disposing)
            {
                return;
            }
            int others = 0;
            for (int i = 0; i < list.Columns.Count - 1; i++)
            {
                others += list.Columns[i].Width;
            }
            list.Columns[list.Columns.Count - 1].Width = Math.Max(60, list.ClientSize.Width - others);
        }

        private void LoadCars()
        {
            string source = this.sourceBox.Text.Trim();
            string target = this.targetBox.Text.Trim();
            if (GameInfo.Detect(source) != ForzaGame.Motorsport4 || GameInfo.Detect(target) != ForzaGame.Motorsport3)
            {
                MessageBox.Show(this, "The source must be a Forza Motorsport 4 folder and the target a Forza Motorsport 3 folder.\n\nFound: "
                    + (GameInfo.Name(GameInfo.Detect(source)) ?? "no Forza game") + " and " + (GameInfo.Name(GameInfo.Detect(target)) ?? "no Forza game") + ".", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            AppSettings.Set("PortFm3.Source", source);
            AppSettings.Set((this.mode == PortMode.Dlc) ? "PortFm3.DlcTarget" : "PortFm3.Target", target);
            List<string> files = new List<string>(this.packFiles);
            bool includeNow = this.includeDlc.Checked;
            this.status.Text = "Reading the databases...";
            this.Background(delegate
            {
                StringBuilder notes = new StringBuilder();
                List<SourceCar> list = new List<SourceCar>();
                List<string[]> donorList = new List<string[]>();
                HashSet<string> inFm3 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Dictionary<long, string> engines3 = new Dictionary<long, string>();
                HashSet<long> drive3 = new HashSet<long>();
                using (SqliteDatabase to = SqliteDatabase.FromBytes(File.ReadAllBytes(Db(target))))
                {
                    StringTable names3 = StringTableOrNull(target);
                    foreach (object[] row in to.Query("SELECT EngineID, MediaName FROM Data_Engine").Rows)
                    {
                        engines3[Convert.ToInt64(row[0], CultureInfo.InvariantCulture)] = Convert.ToString(row[1], CultureInfo.InvariantCulture);
                    }
                    foreach (object[] row in to.Query("SELECT DrivetrainID FROM Data_Drivetrain").Rows)
                    {
                        drive3.Add(Convert.ToInt64(row[0], CultureInfo.InvariantCulture));
                    }
                    foreach (object[] row in to.Query("SELECT MediaName, DisplayName FROM Data_Car ORDER BY MediaName").Rows)
                    {
                        string media = row[0] as string;
                        if (media == null)
                        {
                            continue;
                        }
                        inFm3.Add(media);
                        if (GamePaths.FindMedia(target, "cars", media) != null)
                        {
                            string name = (names3 != null) ? names3.Lookup("Data_Car", "DisplayName", row[1] as string) : null;
                            donorList.Add(new[] { media, string.IsNullOrEmpty(name) ? media : name });
                        }
                    }
                }
                this.ReadCars(source, null, "", list, inFm3, notes);
                // The packages chosen with Package..., then the FM4 DLC installed in Xenia (as the FM4 window lists its source game's DLC).
                List<DlcSource> packs = new List<DlcSource>();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string file in files)
                {
                    try
                    {
                        seen.Add(Path.GetFullPath(file));
                        packs.Add(DlcSource.OpenPackage(file, true));
                    }
                    catch (Exception e)
                    {
                        notes.AppendLine(Path.GetFileName(file) + ": left out, " + e.Message);
                    }
                }
                string content = includeNow ? InstalledDlc.ContentFolder(ForzaGame.Motorsport4) : null;
                if (content != null)
                {
                    uint title = InstalledDlc.TitleId(ForzaGame.Motorsport4);
                    foreach (string file in Directory.GetFiles(content))
                    {
                        try
                        {
                            if (seen.Add(Path.GetFullPath(file)) && StfsReader.IsPackage(file))
                            {
                                DlcSource installed = DlcSource.OpenPackage(file, true);
                                if (installed.TitleId == title)
                                {
                                    packs.Add(installed);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            notes.AppendLine(Path.GetFileName(file) + ": left out, " + e.Message);
                        }
                    }
                    foreach (string folder in Directory.GetDirectories(content))
                    {
                        packs.AddRange(DlcSource.Find(folder));
                    }
                }
                foreach (DlcSource pack in packs)
                {
                    string label = !string.IsNullOrEmpty(pack.Title) ? pack.Title : Path.GetFileName(pack.Folder);
                    try
                    {
                        if (pack.Problem != null || pack.Game != ForzaGame.Motorsport4)
                        {
                            notes.AppendLine(label + ": left out, " + (pack.Problem ?? "not an FM4 package"));
                            continue;
                        }
                        string root = pack.Stage(notes);
                        this.ReadCars(root, root, label, list, inFm3, notes);
                    }
                    catch (Exception e)
                    {
                        notes.AppendLine(label + ": left out, " + e.Message);
                    }
                }
                return delegate
                {
                    this.fm3Engines = engines3;
                    this.fm3Drivetrains = drive3;
                    this.items.Clear();
                    foreach (SourceCar car in list)
                    {
                        ListViewItem item = new ListViewItem(car.Id.ToString(CultureInfo.InvariantCulture));
                        item.Tag = car;
                        item.SubItems.Add(car.Name);
                        item.SubItems.Add(car.Year.ToString(CultureInfo.InvariantCulture));
                        item.SubItems.Add(car.MediaName);
                        item.SubItems.Add(car.Dlc);
                        item.SubItems.Add(car.InFm3 ? "yes (ported under another name)" : "");
                        if (car.InFm3)
                        {
                            item.ForeColor = ForzaTheme.TextDim;
                        }
                        this.items.Add(item);
                    }
                    this.donors.Items.Clear();
                    int select = -1;
                    foreach (string[] donor in donorList)
                    {
                        int at = this.donors.Items.Add(donor[1] + "  (" + donor[0] + ")");
                        this.donors.Tag = donorList;
                        if (string.Equals(donor[0], "TOY_SupraRZ_98", StringComparison.OrdinalIgnoreCase))
                        {
                            select = at;
                        }
                    }
                    this.donors.Tag = donorList;
                    if (select >= 0)
                    {
                        this.donors.SelectedIndex = select;
                    }
                    this.Filter();
                    if (this.mode == PortMode.Dlc && this.packOffer.Text.Trim().Length == 0)
                    {
                        try
                        {
                            this.packOffer.Text = DlcPackage.FreeOfferFm3(target, this.XeniaContent()).ToString(CultureInfo.InvariantCulture);
                        }
                        catch (Exception)
                        {
                        }
                    }
                    this.status.Text = string.Format(CultureInfo.InvariantCulture, "{0} FM4 cars, {1} FM3 template cars. Choose a car.", list.Count, donorList.Count);
                    if (notes.Length > 0)
                    {
                        this.report.Text = "DLC packages:\r\n" + notes.ToString();
                    }
                };
            });
        }

        // The cars of one FM4 folder or staged pack: those FM3 does not have under that name come first as new, the others are shown on request.
        private void ReadCars(string root, string packRoot, string pack, List<SourceCar> into, HashSet<string> inFm3, StringBuilder notes)
        {
            string db = (packRoot == null) ? Db(root) : null;
            byte[] data = null;
            if (packRoot != null)
            {
                data = DlcSource.IsStage(root) ? ReadStageMerge(root) : null;
            }
            else
            {
                data = File.ReadAllBytes(db);
            }
            if (data == null)
            {
                notes.AppendLine(pack + ": no database with cars");
                return;
            }
            StringTable names = StringTableOrNull(root);
            HashSet<string> listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SourceCar already in into)
            {
                listed.Add(already.MediaName);
            }
            using (SqliteDatabase source = SqliteDatabase.FromBytes(data))
            {
                if (!DbIds.HasTable(source, "Data_Car"))
                {
                    return;
                }
                foreach (object[] row in source.Query("SELECT Id, MediaName, DisplayName, Year FROM Data_Car ORDER BY Id").Rows)
                {
                    if (!(row[1] is string))
                    {
                        continue;
                    }
                    if (!listed.Add((string) row[1]))
                    {
                        continue;   // the game's own row, or the same car in another package: listed once
                    }
                    SourceCar car = new SourceCar();
                    car.Id = Convert.ToInt64(row[0], CultureInfo.InvariantCulture);
                    car.MediaName = (string) row[1];
                    string name = (names != null) ? names.Lookup("Data_Car", "DisplayName", row[2] as string) : null;
                    car.Name = string.IsNullOrEmpty(name) ? car.MediaName : name;
                    car.Year = (row[3] == null) ? 0 : Convert.ToInt64(row[3], CultureInfo.InvariantCulture);
                    car.Root = packRoot;
                    car.Dlc = pack;
                    car.InFm3 = inFm3.Contains(car.MediaName);
                    into.Add(car);
                }
            }
        }

        // A staged pack keeps its merge database under Media\db\patch (or the stage's own db): the first one with a Data_Car table.
        private static byte[] ReadStageMerge(string root)
        {
            string db = Db(root);
            if (File.Exists(db))
            {
                return File.ReadAllBytes(db);
            }
            return null;
        }

        private static string Db(string root)
        {
            return Path.Combine(Path.Combine(Path.Combine(root, "Media"), "db"), "gamedb.slt");
        }

        private static StringTable StringTableOrNull(string root)
        {
            try
            {
                return StringTable.Load(root, "EN");
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void Filter()
        {
            string text = this.search.Text.Trim();
            List<ListViewItem> shown = new List<ListViewItem>();
            foreach (ListViewItem item in this.items)
            {
                SourceCar car = (SourceCar) item.Tag;
                if (car.InFm3 && !this.showInFm3.Checked)
                {
                    continue;
                }
                if (text.Length > 0 && item.SubItems[1].Text.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0 && car.MediaName.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                shown.Add(item);
            }
            this.cars.BeginUpdate();
            this.cars.Items.Clear();
            this.cars.Items.AddRange(shown.ToArray());
            this.cars.EndUpdate();
            this.UpdateButtons();
        }


        // ----- Review: what the last trial run or install found worth a look -----

        private TabPage BuildReviewPage()
        {
            TabPage page = new TabPage("Review");
            Label title = new Label();
            title.Dock = DockStyle.Top;
            title.Height = 24;
            title.Font = ForzaTheme.Bold;
            title.Padding = new Padding(6, 5, 0, 0);
            title.Text = "What the port changed or could not do (engines and drivetrains FM3 lacks, tyres, sound files...). Fixes to choose are not there for FM3 yet.";
            this.reviewList = new ListView();
            this.reviewList.Dock = DockStyle.Fill;
            this.reviewList.View = View.Details;
            this.reviewList.FullRowSelect = true;
            this.reviewList.MultiSelect = false;
            this.reviewList.HideSelection = false;
            this.reviewList.Columns.Add("Car", 190);
            this.reviewList.Columns.Add("Where", 180);
            this.reviewList.Columns.Add("What", 700);
            StyleList(this.reviewList);
            page.Controls.Add(this.reviewList);
            page.Controls.Add(title);
            return page;
        }

        // The lines of a port report that say something was changed, replaced, left out or missing.
        private static List<string[]> ReviewRows(string car, string report)
        {
            List<string[]> rows = new List<string[]>();
            string[] marks = { "WARNING", "donor", "left out", "silent", "not in FM3", "could not", "skipped", "approximate", "dropped", "missing", "stays", "converted" };
            foreach (string raw in report.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }
                bool hit = false;
                foreach (string mark in marks)
                {
                    hit = hit || line.IndexOf(mark, StringComparison.OrdinalIgnoreCase) >= 0;
                }
                if (!hit)
                {
                    continue;
                }
                int colon = line.IndexOf(':');
                string where = (colon > 0 && colon < 32) ? line.Substring(0, colon) : "port";
                rows.Add(new string[] { car, where, (colon > 0 && colon < 32) ? line.Substring(colon + 1).Trim() : line });
            }
            return rows;
        }

        private void FillReview(List<string[]> rows)
        {
            if (this.reviewList == null)
            {
                return;
            }
            this.reviewList.BeginUpdate();
            this.reviewList.Items.Clear();
            foreach (string[] row in rows)
            {
                ListViewItem item = new ListViewItem(row);
                if (row[2].IndexOf("WARNING", StringComparison.OrdinalIgnoreCase) >= 0 || row[2].IndexOf("silent", StringComparison.OrdinalIgnoreCase) >= 0 || row[2].IndexOf("missing", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    item.ForeColor = ForzaTheme.Yellow;
                }
                this.reviewList.Items.Add(item);
            }
            this.reviewList.EndUpdate();
        }

        // ----- Upgrades: the selected car's engines and drivetrains as FM4 has them, and where they come from in FM3 -----

        private TabPage BuildUpgradesPage()
        {
            TabPage page = new TabPage("Upgrades");
            this.upgradesTitle = new Label();
            this.upgradesTitle.Dock = DockStyle.Top;
            this.upgradesTitle.Height = 24;
            this.upgradesTitle.Font = ForzaTheme.Bold;
            this.upgradesTitle.Padding = new Padding(6, 5, 0, 0);
            this.upgradesTitle.Text = "Select a car.";
            this.upgradesList = new ListView();
            this.upgradesList.Dock = DockStyle.Fill;
            this.upgradesList.View = View.Details;
            this.upgradesList.FullRowSelect = true;
            this.upgradesList.MultiSelect = false;
            this.upgradesList.HideSelection = false;
            this.upgradesList.Columns.Add("Part", 190);
            this.upgradesList.Columns.Add("Level", 60);
            this.upgradesList.Columns.Add("FM4 item", 260);
            this.upgradesList.Columns.Add("In FM3", 600);
            StyleList(this.upgradesList);
            page.Controls.Add(this.upgradesList);
            page.Controls.Add(this.upgradesTitle);
            return page;
        }

        private void ShowUpgrades()
        {
            if (this.upgradesList == null)
            {
                return;
            }
            this.upgradesList.Items.Clear();
            if (this.cars.SelectedItems.Count != 1)
            {
                this.upgradesTitle.Text = "Select a car.";
                return;
            }
            SourceCar car = (SourceCar) this.cars.SelectedItems[0].Tag;
            this.upgradesTitle.Text = car.Name + ": the upgrades FM4 has for it and what the port does with them in FM3 (FM3 has 8 engine levels, FM4 4: the car keeps FM4's)";
            try
            {
                string path = Db(car.Root ?? this.sourceBox.Text.Trim());
                using (SqliteDatabase db = SqliteDatabase.FromBytes(File.ReadAllBytes(path)))
                {
                    foreach (object[] row in db.Query("SELECT u.Level, u.EngineID, e.MediaName, u.IsStock FROM List_UpgradeEngine u LEFT JOIN Data_Engine e ON e.EngineID = u.EngineID WHERE u.Ordinal = ? ORDER BY u.Level", car.Id).Rows)
                    {
                        long id = Convert.ToInt64(row[1], CultureInfo.InvariantCulture);
                        string media = row[2] as string;
                        string present;
                        string status;
                        if (this.fm3Engines.TryGetValue(id, out present))
                        {
                            status = (media != null && string.Equals(present, media, StringComparison.OrdinalIgnoreCase)) ? "FM3's own engine" : "FM3 has engine " + id + " as " + present + ": the template car's engine is used";
                        }
                        else
                        {
                            status = "not in FM3: copied from FM4 (parts, torque curves, sound tuning)";
                        }
                        this.AddUpgrade("Engine", Convert.ToString(row[0], CultureInfo.InvariantCulture), (media ?? "engine " + id) + (Convert.ToInt64(row[3], CultureInfo.InvariantCulture) == 1 ? " (stock)" : ""), status);
                    }
                    foreach (object[] row in db.Query("SELECT Level, DrivetrainID FROM List_UpgradeDrivetrain WHERE Ordinal = ? ORDER BY Level", car.Id).Rows)
                    {
                        long id = Convert.ToInt64(row[1], CultureInfo.InvariantCulture);
                        this.AddUpgrade("Drivetrain", Convert.ToString(row[0], CultureInfo.InvariantCulture), "drivetrain " + id, this.fm3Drivetrains.Contains(id) ? "FM3's own drivetrain" : "not in FM3: copied from FM4");
                    }
                    string[][] others =
                    {
                        new string[] { "List_UpgradeBrakes", "Brakes", "rows copied with FM3's columns" },
                        new string[] { "List_UpgradeTireCompound", "Tire compound", "rows copied; FM4's drag tyres are left out (FM3 has none)" },
                        new string[] { "List_UpgradeSpringDamper", "Springs and dampers", "rows copied; rates divided by the class ratio (FM3 handling)" },
                        new string[] { "List_UpgradeAntiSwayFront", "Anti-roll bar, front", "rows copied; rates converted" },
                        new string[] { "List_UpgradeAntiSwayRear", "Anti-roll bar, rear", "rows copied; rates converted" },
                        new string[] { "List_UpgradeRimSizeFront", "Rim size, front", "rows copied" },
                        new string[] { "List_UpgradeRimSizeRear", "Rim size, rear", "rows copied" },
                        new string[] { "List_UpgradeRearWing", "Rear wing", "rows copied" },
                        new string[] { "List_UpgradeCarBodyWeight", "Weight reduction", "rows copied" }
                    };
                    foreach (string[] other in others)
                    {
                        try
                        {
                            long count = Convert.ToInt64(db.Scalar("SELECT count(*) FROM " + other[0] + " WHERE Ordinal = ?", car.Id), CultureInfo.InvariantCulture);
                            if (count > 0)
                            {
                                this.AddUpgrade(other[1], count + " level(s)", "", other[2]);
                            }
                        }
                        catch (Exception)
                        {
                            // a pack's database holds only the tables it has rows for
                        }
                    }
                }
            }
            catch (Exception e)
            {
                this.upgradesTitle.Text = car.Name + ": the upgrades could not be read (" + e.Message + ")";
            }
        }

        private void AddUpgrade(string part, string level, string item, string fm3)
        {
            this.upgradesList.Items.Add(new ListViewItem(new string[] { part, level, item, fm3 }));
        }

        private void UpdateButtons()
        {
            bool ready = !this.busy && this.cars.SelectedItems.Count == 1 && this.donors.SelectedIndex >= 0;
            this.trial.Enabled = ready;
            this.install.Enabled = ready;
            this.load.Enabled = !this.busy;
            if (this.mode == PortMode.Dlc && this.packList != null && this.removeXenia != null)
            {
                string content = this.XeniaContent();
                bool named = this.packName.Text.Trim().Length > 0 && this.Offer() > 0;
                this.addToPack.Enabled = ready;
                this.removeFromPack.Enabled = !this.busy && this.packList.SelectedItems.Count == 1;
                this.newPack.Enabled = !this.busy && this.pack.Count > 0;
                this.packTrial.Enabled = !this.busy && this.pack.Count > 0;
                this.buildXenia.Enabled = !this.busy && this.packTrialFolder != null && content != null && named;
                this.buildXbox.Enabled = !this.busy && this.packTrialFolder != null && named;
                this.removeXenia.Enabled = !this.busy && content != null && this.Offer() > 0 && Directory.Exists(DlcPackage.PackagePathFm3(content, this.Offer()));
                this.packInfo.Text = (this.pack.Count == 0) ? "Select a car on the left, choose its template car, then Add (or double-click the car)."
                    : (this.packTrialFolder == null) ? this.pack.Count + " car(s): Trial Pack, then Build DLC Package."
                    : "Trial done: Build DLC Package for Xenia, or the Xbox 360 package.";
            }
        }

        #region DLC pack

        // Add / remove between the two lists, as in the FM4 window.
        private Panel BuildTransfer()
        {
            Panel transfer = new Panel();
            transfer.Dock = DockStyle.Left;
            transfer.Width = 112;
            this.addToPack = new Button();
            this.addToPack.Text = "Add  →";
            this.addToPack.Size = new Size(96, 30);
            this.addToPack.Click += delegate { this.AddToPack(); };
            new ToolTip().SetToolTip(this.addToPack, "Put the selected car, with its template car, into the pack (or double-click the car)");
            this.removeFromPack = new Button();
            this.removeFromPack.Text = "←  Remove";
            this.removeFromPack.Size = new Size(96, 30);
            this.removeFromPack.Click += delegate { this.RemoveFromPack(); };
            transfer.Controls.Add(this.addToPack);
            transfer.Controls.Add(this.removeFromPack);
            transfer.Layout += delegate
            {
                int middle = transfer.ClientSize.Height / 2;
                this.addToPack.Location = new Point(8, middle - 36);
                this.removeFromPack.Location = new Point(8, middle + 6);
            };
            this.cars.DoubleClick += delegate
            {
                if (this.addToPack.Enabled)
                {
                    this.AddToPack();
                }
            };
            return transfer;
        }

        // The pack (right): name, offer, the cars, then the Xenia folder and the actions from the bottom, as in the FM4 window.
        private Panel BuildPackPanel()
        {
            Panel packPanel = new Panel();
            packPanel.Dock = DockStyle.Fill;
            Label nameLabel = new Label();
            nameLabel.Text = "Name:";
            nameLabel.Location = new Point(8, 39);
            nameLabel.AutoSize = true;
            this.packName = new TextBox();
            this.packName.Location = new Point(58, 37);
            this.packName.Width = 300;
            this.packName.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            this.packName.Text = AppSettings.Get("PortFm3.PackName", "ForzaStudio EVO pack");
            this.packName.TextChanged += delegate { AppSettings.Set("PortFm3.PackName", this.packName.Text); this.UpdateButtons(); };
            Label offerLabel = new Label();
            offerLabel.Text = "Offer id:";
            offerLabel.Location = new Point(8, 69);
            offerLabel.AutoSize = true;
            this.packOffer = new TextBox();
            this.packOffer.Location = new Point(58, 66);
            this.packOffer.Width = 70;
            this.packOffer.MaxLength = 5;
            this.packOffer.Text = AppSettings.Get("PortFm3.PackOffer", "");
            this.packOffer.TextChanged += delegate { AppSettings.Set("PortFm3.PackOffer", this.packOffer.Text.Trim()); this.UpdateButtons(); };
            new ToolTip().SetToolTip(this.packOffer, "The package's offer number (1-65535). The game's last is 99; a pack with the number of an installed one is refused.");
            Button suggest = new Button();
            suggest.Text = "Free";
            suggest.Location = new Point(134, 65);
            suggest.Size = new Size(46, 24);
            new ToolTip().SetToolTip(suggest, "Put the first number the game's database and Xenia's packages do not use");
            suggest.Click += delegate { this.SuggestOffer(); };
            this.packList = new ListView();
            this.packList.Location = new Point(8, 100);
            this.packList.Size = new Size(350, 150);
            this.packList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.packList.View = View.Details;
            this.packList.FullRowSelect = true;
            this.packList.MultiSelect = false;
            this.packList.HideSelection = false;
            this.packList.Columns.Add("Car", 140);
            this.packList.Columns.Add("Media name", 120);
            this.packList.Columns.Add("Template", 100);
            this.packList.SelectedIndexChanged += delegate { this.UpdateButtons(); };
            StyleList(this.packList);

            this.newPack = PackButton(packPanel, "New Pack", 8, 0, 100);
            this.newPack.Click += delegate { this.NewPack(); };
            Label xeniaLabel = new Label();
            xeniaLabel.Text = "Xenia folder (with xenia_canary.exe):";
            xeniaLabel.AutoSize = true;
            this.xeniaBox = new TextBox();
            this.xeniaBox.Width = 312;
            this.xeniaBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.xeniaBox.Text = AppSettings.Get(SettingsForm.XeniaKey, "");
            this.xeniaBox.TextChanged += delegate { AppSettings.Set(SettingsForm.XeniaKey, this.xeniaBox.Text.Trim()); this.UpdateButtons(); };
            Button xeniaBrowse = new Button();
            xeniaBrowse.Text = "...";
            xeniaBrowse.Size = new Size(32, 24);
            xeniaBrowse.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            xeniaBrowse.Click += delegate
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "Xenia's folder (the one with xenia_canary.exe and its config)";
                    dialog.ShowNewFolderButton = false;
                    if (Directory.Exists(this.xeniaBox.Text))
                    {
                        dialog.SelectedPath = this.xeniaBox.Text;
                    }
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        this.xeniaBox.Text = dialog.SelectedPath;
                    }
                }
            };
            this.packTrial = PackButton(packPanel, "Trial Pack", 8, 0, 100);
            this.packTrial.Click += delegate { this.RunPackTrial(); };
            this.buildXenia = PackButton(packPanel, "Build DLC Package", 114, 0, 130);
            this.buildXenia.Click += delegate { this.BuildForXenia(); };
            this.removeXenia = PackButton(packPanel, "Remove Pack", 250, 0, 108);
            this.removeXenia.Click += delegate { this.RemoveFromXenia(); };
            this.buildXbox = PackButton(packPanel, "Build for Xbox 360...", 8, 0, 238);
            this.buildXbox.Click += delegate { this.BuildForXbox(); };
            this.packInfo = new Label();
            this.packInfo.Size = new Size(350, 48);
            this.packInfo.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.packInfo.ForeColor = SystemColors.GrayText;
            packPanel.Controls.Add(this.packInfo);
            packPanel.Controls.Add(xeniaLabel);
            packPanel.Controls.Add(this.xeniaBox);
            packPanel.Controls.Add(xeniaBrowse);
            packPanel.Controls.Add(nameLabel);
            packPanel.Controls.Add(this.packName);
            packPanel.Controls.Add(offerLabel);
            packPanel.Controls.Add(this.packOffer);
            packPanel.Controls.Add(suggest);
            packPanel.Controls.Add(this.packList);
            packPanel.Controls.Add(CarPortForm.SectionHeader("DLC pack (Xenia / Xbox 360)"));
            packPanel.Layout += delegate
            {
                int h = packPanel.ClientSize.Height;
                int w = packPanel.ClientSize.Width;
                this.packInfo.Bounds = new Rectangle(8, h - 52, w - 16, 48);
                this.buildXbox.Top = h - 84;
                this.packTrial.Top = this.buildXenia.Top = this.removeXenia.Top = h - 116;
                this.xeniaBox.Bounds = new Rectangle(8, h - 148, w - 54, this.xeniaBox.Height);
                xeniaBrowse.Location = new Point(w - 40, h - 150);
                xeniaLabel.Location = new Point(8, h - 168);
                this.newPack.Top = h - 204;
                this.packName.Width = w - 66;
                this.packList.Bounds = new Rectangle(8, this.packList.Top, w - 16, Math.Max(40, h - 212 - this.packList.Top));
            };
            return packPanel;
        }

        private static Button PackButton(Panel parent, string text, int x, int y, int width)
        {
            Button button = new Button();
            button.Text = text;
            button.Size = new Size(width, 26);
            button.Location = new Point(x, y);
            button.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            parent.Controls.Add(button);
            return button;
        }

        // Free: the first offer above the game's and the installed packs'.
        private void SuggestOffer()
        {
            string target = this.targetBox.Text.Trim();
            if (!Directory.Exists(target))
            {
                return;
            }
            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                this.packOffer.Text = DlcPackage.FreeOfferFm3(target, this.XeniaContent()).ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception e)
            {
                this.status.Text = "No free offer found: " + e.Message;
            }
            finally
            {
                this.Cursor = previous;
            }
        }

        // New Pack: the list and the trial start over.
        private void NewPack()
        {
            this.pack.Clear();
            this.PackChanged();
            this.report.Clear();
        }

        private string XeniaContent()
        {
            if (this.xeniaBox == null)
            {
                return null;
            }
            string xenia = this.xeniaBox.Text.Trim();
            if (xenia.Length == 0 || !Directory.Exists(xenia))
            {
                return null;
            }
            try
            {
                return DlcPackage.XeniaContent(xenia, DlcPackage.TitleIdFm3);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private int Offer()
        {
            int offer;
            return int.TryParse(this.packOffer.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out offer) && offer > 0 && offer < 0xFFFF ? offer : 0;
        }

        private void AddToPack()
        {
            if (this.cars.SelectedItems.Count != 1 || this.SelectedDonor() == null)
            {
                return;
            }
            SourceCar car = (SourceCar) this.cars.SelectedItems[0].Tag;
            if (this.pack.Exists(delegate(PackItem item) { return string.Equals(item.Car.MediaName, car.MediaName, StringComparison.OrdinalIgnoreCase); }))
            {
                this.status.Text = car.MediaName + " is in the pack already.";
                return;
            }
            PackItem added = new PackItem();
            added.Car = car;
            added.Donor = this.SelectedDonor();
            this.pack.Add(added);
            this.PackChanged();
        }

        private void RemoveFromPack()
        {
            if (this.packList.SelectedItems.Count != 1)
            {
                return;
            }
            this.pack.Remove((PackItem) this.packList.SelectedItems[0].Tag);
            this.PackChanged();
        }

        // The pack's cars changed: the trial is out of date.
        private void PackChanged()
        {
            this.packTrialFolder = null;
            this.packList.Items.Clear();
            foreach (PackItem item in this.pack)
            {
                ListViewItem row = new ListViewItem(item.Car.Name);
                row.Tag = item;
                row.SubItems.Add(item.Car.MediaName);
                row.SubItems.Add(item.Donor);
                this.packList.Items.Add(row);
            }
            this.status.Text = this.pack.Count + " car(s) in the pack. Trial Pack next.";
            this.UpdateButtons();
        }

        private void RunPackTrial()
        {
            string source = this.sourceBox.Text.Trim();
            string target = this.targetBox.Text.Trim();
            List<PackItem> items = new List<PackItem>(this.pack);
            if (items.Count == 0)
            {
                return;
            }
            string folder = Path.Combine(Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "PackFm3"), "pack_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);
            this.packTrialFolder = null;
            this.report.Text = string.Format("Pack trial: {0} car(s) -> {1}\r\n", items.Count, folder);
            this.Background(delegate
            {
                StringBuilder text = new StringBuilder();
                string failed = null;
                Exception error = null;
                List<string[]> review = new List<string[]>();
                foreach (PackItem item in items)
                {
                    CarPortToFm3 port = new CarPortToFm3();
                    port.SourceRoot = source;
                    port.TargetRoot = target;
                    port.OutputRoot = folder;   // the cars of the pack go one on top of the other: the next reads the previous one's database
                    port.MediaName = item.Car.MediaName;
                    port.DonorMediaName = item.Donor;
                    text.AppendLine().AppendLine("=== " + item.Car.Name + " (" + item.Car.MediaName + "), template " + item.Donor);
                    try
                    {
                        if (item.Car.Root != null)
                        {
                            DlcSource.Materialize(item.Car.Root, item.Car.MediaName);
                            port.PackRoot = item.Car.Root;
                        }
                        port.Run();
                    }
                    catch (Exception e)
                    {
                        error = e;
                        failed = item.Car.MediaName;
                    }
                    text.Append(port.Report.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n"));
                    review.AddRange(ReviewRows(item.Car.Name, port.Report.ToString()));
                    if (error != null)
                    {
                        break;
                    }
                }
                return delegate
                {
                    this.report.Text += text.ToString();
                    this.FillReview(review);
                    if (error != null)
                    {
                        this.report.Text += "\r\nFAILED (" + failed + "): " + error.Message + "\r\n";
                        this.status.Text = failed + ": the trial failed, see the report. Change its template or take it out of the pack.";
                    }
                    else
                    {
                        this.report.Text += "\r\npack trial done; files in " + folder + "\r\n";
                        this.packTrialFolder = folder;
                        this.status.Text = "Trial done: name the pack, then Build For Xenia or Xbox 360 Package.";
                    }
                    this.report.SelectionStart = this.report.TextLength;
                    this.report.ScrollToCaret();
                };
            });
        }

        private void BuildForXenia()
        {
            string content = this.XeniaContent();
            string trial = this.packTrialFolder;
            int offer = this.Offer();
            string name = this.packName.Text.Trim();
            string target = this.targetBox.Text.Trim();
            if (content == null || trial == null || offer == 0 || name.Length == 0)
            {
                return;
            }
            if (XeniaRunning())
            {
                MessageBox.Show(this, "Xenia is running. Close it first.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            AppSettings.Set(SettingsForm.XeniaKey, this.xeniaBox.Text.Trim());
            this.report.Text = string.Format("Building the pack \"{0}\" (offer {1}) in {2}\r\n", name, offer, content);
            this.Background(delegate
            {
                StringBuilder text = new StringBuilder();
                string error = null;
                try
                {
                    DlcPackage.BuildFolderFm3(target, trial, content, offer, name, text);
                }
                catch (Exception e)
                {
                    error = e.Message;
                }
                return delegate
                {
                    this.report.Text += text.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n") + (error != null ? "\r\nFAILED: " + error + "\r\n" : "\r\nDone. Start FM3 in Xenia.\r\n");
                    this.status.Text = (error != null) ? "The pack failed, see the report." : "Pack " + name + " built for Xenia (offer " + offer + ").";
                };
            });
        }

        private void BuildForXbox()
        {
            string trial = this.packTrialFolder;
            int offer = this.Offer();
            string name = this.packName.Text.Trim();
            string target = this.targetBox.Text.Trim();
            if (trial == null || offer == 0 || name.Length == 0)
            {
                return;
            }
            string output;
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Folder for the Xbox 360 package file (saved directly in it)";
                string last = AppSettings.Get("CarPort.XboxOut", "");
                if (Directory.Exists(last))
                {
                    dialog.SelectedPath = last;
                }
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                output = dialog.SelectedPath;
            }
            AppSettings.Set("CarPort.XboxOut", output);
            string installed = this.XeniaContent();   // the strings of the FM3 packs installed in Xenia, if its folder is set
            this.report.Text = string.Format("Building the Xbox 360 package \"{0}\" (offer {1}) into {2}\r\n", name, offer, output);
            this.Background(delegate
            {
                StringBuilder text = new StringBuilder();
                string error = null;
                string work = Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "XboxFm3_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
                try
                {
                    string folder = DlcPackage.BuildFolderFm3(target, trial, Path.Combine(work, "00000002"), installed, offer, name, text);
                    DlcPackage.BuildConsolePackage(folder, output, name, null, text, DlcPackage.TitleIdFm3, "Forza Motorsport 3");
                }
                catch (Exception e)
                {
                    error = e.Message;
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(work))
                        {
                            Directory.Delete(work, true);
                        }
                    }
                    catch (IOException)
                    {
                    }
                }
                return delegate
                {
                    this.report.Text += text.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n") + (error != null ? "\r\nFAILED: " + error + "\r\n" : "\r\nDone.\r\n");
                    this.status.Text = (error != null) ? "The package failed, see the report." : "Xbox 360 package built in " + output;
                };
            });
        }

        private void RemoveFromXenia()
        {
            string content = this.XeniaContent();
            int offer = this.Offer();
            if (content == null || offer == 0)
            {
                return;
            }
            if (XeniaRunning())
            {
                MessageBox.Show(this, "Xenia is running. Close it first.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string package = DlcPackage.PackagePathFm3(content, offer);
            if (MessageBox.Show(this, "Delete the pack folder\n" + package + "\n(and its Xenia header)?", this.Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            {
                return;
            }
            DlcPackage.RemoveFm3(content, offer);
            this.status.Text = "Pack " + offer + " removed from Xenia.";
            this.UpdateButtons();
        }

        #endregion

        private string SelectedDonor()
        {
            List<string[]> list = this.donors.Tag as List<string[]>;
            return (list != null && this.donors.SelectedIndex >= 0) ? list[this.donors.SelectedIndex][0] : null;
        }

        private void Port(bool installing)
        {
            if (this.cars.SelectedItems.Count != 1 || this.SelectedDonor() == null)
            {
                return;
            }
            SourceCar car = (SourceCar) this.cars.SelectedItems[0].Tag;
            string target = this.targetBox.Text.Trim();
            string donor = this.SelectedDonor();
            if (installing)
            {
                if (target.TrimEnd('\\').EndsWith("\\FM3_Extracted", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(this, "FM3_Extracted is the clean game and is never written: use the test copy FM3_Extracted_DEV.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (XeniaRunning())
                {
                    MessageBox.Show(this, "Xenia is running. Close it first: the game files are replaced while the install writes them.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (MessageBox.Show(this, "Install " + car.MediaName + " into\n" + target + "\n\n"
                    + (BackupPolicy.Enabled ? "Every file it replaces is saved to bak\\ first." : "Backups are off ([Backup] Enabled in ForzaStudio.ini): replaced files are NOT saved."),
                    this.Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                {
                    return;
                }
            }
            string source = this.sourceBox.Text.Trim();
            string folder = Path.Combine(Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "CarPortFm3"), car.MediaName + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);
            this.report.Text = string.Format("{0} {1} -> {2}, donor {3}...\r\n", installing ? "Installing" : "Trial run:", car.MediaName, target, donor);
            this.Background(delegate
            {
                CarPortToFm3 port = new CarPortToFm3();
                port.SourceRoot = source;
                port.TargetRoot = target;
                port.OutputRoot = folder;
                port.MediaName = car.MediaName;
                port.DonorMediaName = donor;
                string result;
                List<string[]> review = new List<string[]>();
                try
                {
                    if (car.Root != null)
                    {
                        DlcSource.Materialize(car.Root, car.MediaName);
                        port.PackRoot = car.Root;
                    }
                    port.Run();
                    result = port.Report.ToString();
                    review.AddRange(ReviewRows(car.Name, port.Report.ToString()));
                    if (installing)
                    {
                        int copied = CopyWithBackup(Path.Combine(folder, "Media"), Path.Combine(target, "Media"), car.MediaName);
                        result += "\r\nInstalled: " + copied + " file(s) copied into " + target + ".";
                    }
                    else
                    {
                        result += "\r\nTrial run only: the files are in " + folder;
                    }
                }
                catch (Exception e)
                {
                    review.Clear();
                    review.AddRange(ReviewRows(car.Name, port.Report.ToString()));
                    result = port.Report.ToString() + "\r\nFAILED: " + e.Message + "\r\n" + e.StackTrace;
                }
                return delegate
                {
                    this.report.Text = result.Replace("\r\n", "\n").Replace("\n", "\r\n");
                    this.FillReview(review);
                    this.status.Text = result.IndexOf("FAILED:", StringComparison.Ordinal) >= 0 ? car.MediaName + ": failed, see the report." : (installing ? car.MediaName + " installed in FM3." : car.MediaName + ": trial run OK.");
                };
            });
        }

        private static bool XeniaRunning()
        {
            foreach (Process process in Process.GetProcesses())
            {
                if (process.ProcessName.StartsWith("xenia", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        // Copies the ported tree over the game's, saving each file it replaces to bak\<stamp>_bak_<name> first.
        private static int CopyWithBackup(string from, string to, string car)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string backup = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bak");
            int count = 0;
            foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(from.Length).TrimStart('\\');
                string destination = Path.Combine(to, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                if (File.Exists(destination) && BackupPolicy.Enabled)
                {
                    Directory.CreateDirectory(backup);
                    string copy = Path.Combine(backup, stamp + "_bak_" + car + "_" + relative.Replace('\\', '_'));
                    File.Copy(destination, copy, true);
                }
                File.Copy(file, destination, true);
                count++;
            }
            return count;
        }

        private void Background(Func<MethodInvoker> work)
        {
            this.busy = true;
            this.UseWaitCursor = true;
            this.UpdateButtons();
            Thread thread = new Thread(delegate()
            {
                MethodInvoker done;
                try
                {
                    done = work();
                }
                catch (Exception e)
                {
                    string message = e.Message;
                    done = delegate { this.status.Text = "Failed: " + message; this.report.Text = message; };
                }
                this.BeginInvoke((MethodInvoker) delegate
                {
                    this.busy = false;
                    this.UseWaitCursor = false;
                    done();
                    this.UpdateButtons();
                });
            });
            thread.IsBackground = true;
            thread.Start();
        }
    }
}
