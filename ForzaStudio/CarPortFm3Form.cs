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

    // Tools > Import Car From Another Forza > For FM3 (source folder = FM4), and Tools > Create DLC Package > For FM3 (CarPortToFm3, DlcPackage.Fm3).
    // The same screen as the FM4 window (CarPortForm): brand bar and game banner, the two game folders, the cars, the selected car's
    // template, the pack on the right (Dlc), the report under both. The folders start from Tools > Settings (FM4 = the port target,
    // FM3 = Folder.FM3, Xenia) and the last ones used. Own class because CarPortForm is built around FM4 as the target.
    internal sealed partial class CarPortFm3Form : Form
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
            public bool Traffic;        // FH traffic car: placeholder close-up models, rebuilt by step 1 (shown yellow)
        }

        // Dlc mode: cars go into a pack list, then Trial Pack, Build DLC Package / Build for Xbox 360.
        private sealed class PackItem
        {
            public SourceCar Car;
            public string Donor;
            public CarChoices Choices;  // arcade, make, sound, brakes chosen when the car was added
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
        private Dictionary<string, Dictionary<string, object>> packTrialFixes;   // the fixes each car of the pack trial was made with
        private bool busy;
        private Panel fixPanel;
        private Label fixTitle;
        private DataGridView fixGrid;
        private TabPage reviewPage;
        private string fixCar;
        private bool showingFixes;
        private readonly Dictionary<string, Dictionary<string, object>> fixes = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);       // car -> issue key -> chosen value
        private readonly Dictionary<string, List<CarPort.Issue>> issuesOf = new Dictionary<string, List<CarPort.Issue>>(StringComparer.OrdinalIgnoreCase);   // car -> what its last run found
        private readonly Dictionary<string, DlcSource> donorPacks = new Dictionary<string, DlcSource>(StringComparer.OrdinalIgnoreCase);   // template car -> its FM3 package (cars not in the folder)
        private ForzaGame sourceGame = ForzaGame.Motorsport4;   // of the cars listed: FM4 straight, FH1 / FH2 through their FM4 form (CarPortViaFm4)

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
            ToolStripLabel title = new ToolStripLabel((mode == PortMode.Dlc) ? "CREATE A DLC PACKAGE FOR FORZA MOTORSPORT 3" : "IMPORT A CAR FROM FORZA MOTORSPORT 4 OR FORZA HORIZON INTO FORZA MOTORSPORT 3");
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

            GameBanner banner = new GameBanner("FORZA MOTORSPORT 3", (mode == PortMode.Dlc) ? "DLC PACKAGE" : "FROM  FM4 / FM2 / FH1 / FH2", Color.FromArgb(214, 170, 40));
            banner.Dock = DockStyle.Top;

            // Game folders: the start values come from the settings.
            Panel folders = new Panel();
            folders.Dock = DockStyle.Top;
            folders.Height = 74;
            folders.Width = this.ClientSize.Width;   // the rows are placed and anchored from this width (a Panel starts 200 wide)
            this.sourceBox = this.FolderRow(folders, "Source game (FM4/FM2/FH1/FH2):", 10, DefaultSource(), true);
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
            this.cars.SelectedIndexChanged += delegate { this.UpdateButtons(); this.ShowUpgrades(); this.ShowSelectedFixes(); };
            StyleList(this.cars);

            Panel options = new Panel();
            options.Dock = DockStyle.Fill;
            Label donorLabel = new Label();
            donorLabel.Text = "FM3 template car, for what the car lacks in FM3 (your choice; game and DLC cars):";
            donorLabel.Location = new Point(8, 8);
            donorLabel.AutoSize = true;
            this.donors = new ComboBox();
            this.donors.DropDownStyle = ComboBoxStyle.DropDownList;
            this.donors.Location = new Point(8, 28);
            this.donors.Width = 460;
            this.donors.MaxDropDownItems = 24;
            this.donors.SelectedIndexChanged += delegate { this.UpdateButtons(); };
            this.BuildChoices(options);     // arcade, make (+), engine sound, brakes: as the FM4 window
            this.trial = new Button();
            this.trial.Text = "Trial Run";
            this.trial.Size = new Size(110, 28);
            this.trial.Location = new Point(8, 154);
            this.trial.Click += delegate { this.Port(false); };
            this.install = new Button();
            this.install.Text = "Install In FM3...";
            this.install.Size = new Size(130, 28);
            this.install.Location = new Point(124, 154);
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
            selectedCar.Height = (mode == PortMode.Import) ? 216 : 182;
            selectedCar.Controls.Add(options);
            selectedCar.Controls.Add(CarPortForm.SectionHeader("Selected car"));

            Panel carsArea = new Panel();
            carsArea.Dock = DockStyle.Fill;
            carsArea.Controls.Add(this.cars);
            carsArea.Controls.Add(selectedCar);
            carsArea.Controls.Add(filter);
            Control carsHeader = CarPortForm.SectionHeader("Cars");
            // The traffic colour of the car list at the right of the header, as the FM4 window draws it (user 2026-11-22).
            carsHeader.Paint += delegate(object sender, PaintEventArgs e)
            {
                const string name = "Traffic car (close-up rebuilt)";
                Size size = TextRenderer.MeasureText(name, ForzaTheme.Regular);
                int x = carsHeader.Width - 8 - size.Width;
                TextRenderer.DrawText(e.Graphics, name, ForzaTheme.Regular, new Point(x, 7), ForzaTheme.Yellow);
                using (Brush dot = new SolidBrush(ForzaTheme.Yellow))
                {
                    e.Graphics.FillEllipse(dot, x - 14, 11, 9, 9);
                }
            };
            carsHeader.Resize += delegate { carsHeader.Invalidate(); };
            carsArea.Controls.Add(carsHeader);

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
                dialog.Title = "DLC packages of the source game, FM4 or FM2 (Xbox 360 package files)";
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
            this.status.Text = this.packFiles.Count + " DLC package(s) added: Load Cars lists their cars with the source game's.";
            if (Directory.Exists(this.sourceBox.Text) && Directory.Exists(this.targetBox.Text))
            {
                this.LoadCars();
            }
        }

        // Lists drawn like the FM4 window's: upper-case headers over a red rule, alternating dark rows, the selection in Forza red.
        internal static void StyleList(ListView list)
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
                bool yellow = e.Item.ForeColor == ForzaTheme.Yellow;   // traffic cars; review warnings
                Color fore = selected ? Color.White : ((e.ColumnIndex == 0 || dim) ? ForzaTheme.TextDim : yellow ? ForzaTheme.Yellow : ForzaTheme.Text);
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
            ForzaGame game = GameInfo.Detect(source);
            // FH1, FH2 and the FM2 game folder go through their FM4 form (CarPortViaFm4); 'horizon' names that route
            bool horizon = CarPortViaFm4.GoesThroughFm4(source);
            if (horizon && GameInfo.ConfiguredFolder(ForzaGame.Motorsport4) == null)
            {
                MessageBox.Show(this, "A " + GameInfo.Name(game) + " car reaches Forza Motorsport 3 through its Forza Motorsport 4 form.\n\nSet the Forza Motorsport 4 folder in Tools > Settings > Folders (it is only read).", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if ((game != ForzaGame.Motorsport4 && !horizon) || GameInfo.Detect(target) != ForzaGame.Motorsport3)
            {
                MessageBox.Show(this, "The source must be a Forza Motorsport 4, Forza Motorsport 2, Forza Horizon or Forza Horizon 2 folder and the target a Forza Motorsport 3 folder.\n\nFound: "
                    + (GameInfo.Name(GameInfo.Detect(source)) ?? "no Forza game") + " and " + (GameInfo.Name(GameInfo.Detect(target)) ?? "no Forza game") + ".", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            AppSettings.Set("PortFm3.Source", source);
            AppSettings.Set((this.mode == PortMode.Dlc) ? "PortFm3.DlcTarget" : "PortFm3.Target", target);
            // The source game's DLC packages: FM4's with an FM4 source, FM2's with an FM2 source (2026-11-22, through the FM4
            // form as the FM2 game's cars); a Horizon source lists the game's own cars only.
            bool fm2 = game == ForzaGame.Motorsport2;
            ForzaGame packGame = fm2 ? ForzaGame.Motorsport2 : ForzaGame.Motorsport4;
            bool packsToo = !horizon || fm2;
            List<string> files = packsToo ? new List<string>(this.packFiles) : new List<string>();
            bool includeNow = this.includeDlc.Checked && packsToo;
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
                // Engine sound choices: the game folder's cars (their tuning files are in the folder's sound zips, not in packages)
                List<string[]> soundList = new List<string[]>(donorList);
                List<CarPort.Choice> makeList = CarPortForm.Makes(target);
                // Template cars also from the FM3 packages installed in Xenia (second disc and DLC), as the FM4 window does
                // (user 2026-11-21). The package is only read; it is laid out when such a car is used (DonorRootOf).
                Dictionary<string, DlcSource> packOf = new Dictionary<string, DlcSource>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string content3 = InstalledDlc.ContentFolder(ForzaGame.Motorsport3);
                    if (content3 != null)
                    {
                        HashSet<string> listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (string[] donor in donorList)
                        {
                            listed.Add(donor[0]);
                        }
                        foreach (InstalledDlc installed in InstalledDlc.Scan(ForzaGame.Motorsport3, content3, null))
                        {
                            if (installed.Source == null || installed.Source.Problem != null || installed.Source.Game != ForzaGame.Motorsport3)
                            {
                                continue;
                            }
                            string packTitle = !string.IsNullOrEmpty(installed.Source.Title) ? installed.Source.Title : Path.GetFileName(installed.Source.Folder);
                            foreach (string media in installed.Source.Cars())
                            {
                                if (!listed.Add(media))
                                {
                                    continue;   // in the folder, or in a package already listed (the Ultimate packs repeat cars)
                                }
                                DlcCatalog.Pack official = DlcCatalog.PackOf(ForzaGame.Motorsport3, media);
                                string name = media;
                                if (official != null)
                                {
                                    foreach (DlcCatalog.CarRow row in official.Cars)
                                    {
                                        if (string.Equals(row.MediaName, media, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(row.Name))
                                        {
                                            name = row.Name;
                                        }
                                    }
                                }
                                donorList.Add(new[] { media, name + "  [DLC: " + packTitle + "]" });
                                packOf[media] = installed.Source;
                            }
                        }
                        donorList.Sort(delegate(string[] x, string[] y) { return string.Compare(x[0], y[0], StringComparison.OrdinalIgnoreCase); });
                    }
                }
                catch (Exception e)
                {
                    notes.AppendLine("FM3 packages in Xenia: not read, " + e.Message);
                }
                lock (this.donorPacks)
                {
                    this.donorPacks.Clear();
                    foreach (KeyValuePair<string, DlcSource> pair in packOf)
                    {
                        this.donorPacks[pair.Key] = pair.Value;
                    }
                }
                // FM3's own cars also live in the packages of its second disc and DLC (not in the folder's database)
                foreach (DlcCatalog.Pack official in DlcCatalog.All)
                {
                    if (official.Game == ForzaGame.Motorsport3)
                    {
                        foreach (DlcCatalog.CarRow row in official.Cars)
                        {
                            inFm3.Add(row.MediaName);
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
                string content = includeNow ? InstalledDlc.ContentFolder(packGame) : null;
                if (content != null)
                {
                    uint title = InstalledDlc.TitleId(packGame);
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
                        if (pack.Problem != null || pack.Game != packGame)
                        {
                            notes.AppendLine(label + ": left out, " + (pack.Problem ?? "not an " + GameInfo.Tag(packGame) + " package"));
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
                    this.sourceGame = game;
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
                        else if (car.Traffic)
                        {
                            item.ForeColor = ForzaTheme.Yellow;
                        }
                        this.items.Add(item);
                    }
                    this.donors.Items.Clear();
                    foreach (string[] donor in donorList)
                    {
                        this.donors.Items.Add(donor[1] + "  (" + donor[0] + ")");
                    }
                    this.donors.Tag = donorList;
                    this.FillChoices(makeList, soundList, horizon);
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
                    this.status.Text = string.Format(CultureInfo.InvariantCulture, "{0} {2} cars, {1} FM3 template cars. Choose a car.{3}", list.Count, donorList.Count, GameInfo.Name(game),
                        horizon ? " A " + GameInfo.Name(game) + " car goes through its FM4 form (the FM4 folder of Tools > Settings is read, not written)." : "");
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
                // the FM2 game folder: only the cars with a zip in Media\cars (step 1 lays the car out from it), as the FM4 window lists them
                bool needsZip = packRoot == null && GameInfo.Detect(root) == ForzaGame.Motorsport2;
                // an FM2 package's stage holds the whole FM2 database (the game's + the package's): only the package's own cars
                Dictionary<string, KeyValuePair<bool, string>> own = (packRoot != null && DlcSource.IsFm2Raw(root)) ? DlcSource.LazyCars(root) : null;
                // FH traffic cars (FH1 / FH2 folder or a staged FH package), yellow as in the FM4 window
                bool horizonSource = CarPortViaFm4.IsHorizon(root);
                Dictionary<string, KeyValuePair<bool, string>> lazy = horizonSource ? DlcSource.LazyCars(root) : null;
                int noFiles = 0;
                foreach (object[] row in source.Query("SELECT Id, MediaName, DisplayName, Year FROM Data_Car ORDER BY Id").Rows)
                {
                    if (!(row[1] is string))
                    {
                        continue;
                    }
                    if (needsZip && GamePaths.FindMedia(root, "cars", (string) row[1]) == null)
                    {
                        noFiles++;
                        continue;
                    }
                    if (own != null && own.Count > 0 && !own.ContainsKey((string) row[1]))
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
                    car.Traffic = horizonSource && IsTraffic(root, car.MediaName, lazy);
                    into.Add(car);
                }
                if (noFiles > 0)
                {
                    notes.AppendLine(noFiles + " cars of the source database have no car zip in its folder: left out");
                }
            }
        }

        // A car with placeholder close-up models (CarPort.HasPlaceholderCloseUp on its zip, or the package index of a lazy stage).
        private static bool IsTraffic(string root, string media, Dictionary<string, KeyValuePair<bool, string>> lazy)
        {
            try
            {
                string zip = GamePaths.FindMedia(root, "cars", media);
                if (zip != null && File.Exists(zip))
                {
                    return CarPort.HasPlaceholderCloseUp(zip, media);
                }
                KeyValuePair<bool, string> listed;
                return lazy != null && lazy.TryGetValue(media, out listed) && listed.Key;
            }
            catch (Exception)
            {
                return false;
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
            title.Text = "What the port into FM3 changed or could not do (engines and drivetrains FM3 lacks, tyres, sound files...).";

            // What the car has that is not regular for FM3 (2026-11-21: materials no FM3 car uses, from CarPortToFm3's review;
            // for a Horizon car also the database values of its FM4 form), each with a fix to choose, as in the FM4 window.
            // The fixes are kept per car and used by the next trial run, the install and the pack trial.
            this.fixPanel = new Panel();
            this.fixPanel.Dock = DockStyle.Top;
            this.fixPanel.Height = 200;
            this.fixPanel.Visible = false;
            this.fixTitle = new Label();
            this.fixTitle.Dock = DockStyle.Top;
            this.fixTitle.Height = 24;
            this.fixTitle.Font = ForzaTheme.Bold;
            this.fixTitle.Padding = new Padding(6, 5, 0, 0);
            this.fixGrid = new DataGridView();
            this.fixGrid.Dock = DockStyle.Fill;
            this.fixGrid.AllowUserToAddRows = false;
            this.fixGrid.AllowUserToDeleteRows = false;
            this.fixGrid.RowHeadersVisible = false;
            this.fixGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            this.fixGrid.EditMode = DataGridViewEditMode.EditOnEnter;
            this.fixGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            DataGridViewTextBoxColumn fixWhere = new DataGridViewTextBoxColumn();
            fixWhere.HeaderText = "Where";
            fixWhere.ReadOnly = true;
            fixWhere.FillWeight = 25;
            DataGridViewTextBoxColumn fixProblem = new DataGridViewTextBoxColumn();
            fixProblem.HeaderText = "Problem";
            fixProblem.ReadOnly = true;
            fixProblem.FillWeight = 45;
            DataGridViewComboBoxColumn fixChoice = new DataGridViewComboBoxColumn();
            fixChoice.HeaderText = "Fix (used by the next trial run and the install)";
            fixChoice.FillWeight = 30;
            fixChoice.FlatStyle = FlatStyle.Flat;
            this.fixGrid.Columns.Add(fixWhere);
            this.fixGrid.Columns.Add(fixProblem);
            this.fixGrid.Columns.Add(fixChoice);
            this.fixGrid.CellValueChanged += delegate
            {
                this.StoreFixes();
                this.CheckPackTrialFixes();
            };
            this.fixGrid.CurrentCellDirtyStateChanged += delegate
            {
                if (this.fixGrid.IsCurrentCellDirty)
                {
                    this.fixGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            this.fixGrid.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };
            // Recommended fixes in green, in the cell and in its list.
            this.fixGrid.CellFormatting += delegate(object sender, DataGridViewCellFormattingEventArgs e)
            {
                if (e.ColumnIndex == 2 && e.RowIndex >= 0 && RecommendedFix(this.fixGrid.Rows[e.RowIndex], e.Value))
                {
                    e.CellStyle.ForeColor = ForzaTheme.Green;
                    e.CellStyle.SelectionForeColor = ForzaTheme.Green;
                }
            };
            this.fixGrid.EditingControlShowing += delegate(object sender, DataGridViewEditingControlShowingEventArgs e)
            {
                ComboBox combo = e.Control as ComboBox;
                if (combo != null && combo.Tag as string != "green")
                {
                    combo.Tag = "green";   // the grid reuses its editing control
                    ForzaTheme.GreenItems(combo, delegate(object item) { return this.fixGrid.CurrentRow != null && RecommendedFix(this.fixGrid.CurrentRow, item); });
                }
            };
            this.fixPanel.Controls.Add(this.fixGrid);
            this.fixPanel.Controls.Add(this.fixTitle);
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
            page.Controls.Add(this.fixPanel);   // docked first: above the title and the list
            // The panel is shown while the Report tab is in front (after a trial): the grid laid its Fill columns out hidden and
            // drew only a 200-pixel corner, the rest showing old pixels of the report (user screenshot 2026-11-22). Laid out and
            // drawn again whenever it can be seen.
            EventHandler redraw = delegate
            {
                if (this.fixPanel.Visible && page.Visible)
                {
                    this.fixGrid.PerformLayout();
                    this.fixGrid.Invalidate(true);
                    this.fixGrid.Update();
                }
            };
            this.fixPanel.VisibleChanged += redraw;
            this.fixPanel.SizeChanged += redraw;
            page.Enter += redraw;
            page.VisibleChanged += redraw;
            this.fixGrid.SizeChanged += delegate { this.fixGrid.Invalidate(true); };
            this.reviewPage = page;
            return page;
        }

        private const string LeaveAsIs = "(leave as is)";

        // The issues of the last run of this Horizon car (null: none known), each row preset to the fix chosen before, else
        // to the one the FM4 port suggests itself. The same grid and rules as the FM4 window's review (CarPortForm.ShowReview).
        private void ShowFixes(string mediaName, List<CarPort.Issue> issues)
        {
            if (this.fixGrid == null)
            {
                return;
            }
            this.showingFixes = true;
            this.fixCar = (issues == null) ? null : mediaName;
            this.fixGrid.Rows.Clear();
            int count = (issues == null) ? 0 : issues.Count;
            if (count > 0)
            {
                Dictionary<string, object> chosen;
                if (!this.fixes.TryGetValue(mediaName, out chosen))
                {
                    chosen = new Dictionary<string, object>();
                    this.fixes[mediaName] = chosen;
                }
                foreach (CarPort.Issue issue in issues)
                {
                    int index = this.fixGrid.Rows.Add();
                    DataGridViewRow row = this.fixGrid.Rows[index];
                    row.Tag = issue;
                    row.Cells[0].Value = issue.Where;
                    row.Cells[1].Value = issue.Problem;
                    DataGridViewComboBoxCell cell = (DataGridViewComboBoxCell) row.Cells[2];
                    cell.Items.Add(LeaveAsIs);
                    string selected = LeaveAsIs;
                    foreach (CarPort.Choice choice in issue.Choices)
                    {
                        cell.Items.Add(choice.Label);
                        object value;
                        if ((chosen.TryGetValue(issue.Key, out value) && object.Equals(Convert.ToString(value, CultureInfo.InvariantCulture), Convert.ToString(choice.Value, CultureInfo.InvariantCulture)))
                            || (!chosen.ContainsKey(issue.Key) && issue.Suggested != null && object.Equals(issue.Suggested, choice.Value)))
                        {
                            selected = choice.Label;
                        }
                    }
                    cell.Value = selected;
                }
                this.fixTitle.Text = string.Format("{0}: {1} to fix for FM3 (a material no FM3 car uses renders black). Choose a fix, then Trial Run or Install again.", mediaName, count);
            }
            this.fixPanel.Visible = count > 0;
            if (this.reviewPage != null)
            {
                this.reviewPage.Text = (count > 0) ? "Review (" + count + " to fix)" : "Review";
            }
            this.showingFixes = false;
            this.StoreFixes();
        }

        // The fix grid follows the car selected in the list: what its last run (alone or in the pack trial) found.
        private void ShowSelectedFixes()
        {
            this.ShowFixesOf((this.cars.SelectedItems.Count == 1) ? ((SourceCar) this.cars.SelectedItems[0].Tag).MediaName : null);
        }

        // A pack row selected (user 2026-11-21: the review could only be reached through the car list, so a pack car hidden
        // by the search box could not be fixed): the grid shows that car.
        private void ShowPackFixes()
        {
            if (this.packList != null && this.packList.SelectedItems.Count == 1)
            {
                this.ShowFixesOf(((PackItem) this.packList.SelectedItems[0].Tag).Car.MediaName);
            }
        }

        private void ShowFixesOf(string mediaName)
        {
            this.StoreFixes();
            if (mediaName == null)
            {
                this.ShowFixes(null, null);
                return;
            }
            List<CarPort.Issue> known;
            this.issuesOf.TryGetValue(mediaName, out known);
            this.ShowFixes(mediaName, known);
        }

        // After a pack trial: the selected pack car, else the first pack car with something to fix.
        private void ShowPackTrialFixes()
        {
            if (this.packList.SelectedItems.Count == 1)
            {
                this.ShowPackFixes();
                return;
            }
            foreach (PackItem item in this.pack)
            {
                List<CarPort.Issue> known;
                if (this.issuesOf.TryGetValue(item.Car.MediaName, out known) && known != null && known.Count > 0)
                {
                    this.ShowFixesOf(item.Car.MediaName);
                    return;
                }
            }
            this.ShowSelectedFixes();
        }

        // Whether a label of a fix row is one of its recommended choices.
        private static bool RecommendedFix(DataGridViewRow row, object label)
        {
            CarPort.Issue issue = row.Tag as CarPort.Issue;
            string text = label as string;
            return issue != null && text != null && issue.Choices.Exists(delegate(CarPort.Choice c) { return c.Recommended && c.Label == text; });
        }

        // The fixes picked in the grid, kept for the car on show.
        private void StoreFixes()
        {
            if (this.fixCar == null || this.showingFixes)
            {
                return;
            }
            Dictionary<string, object> chosen;
            if (!this.fixes.TryGetValue(this.fixCar, out chosen))
            {
                chosen = new Dictionary<string, object>();
                this.fixes[this.fixCar] = chosen;
            }
            foreach (DataGridViewRow row in this.fixGrid.Rows)
            {
                CarPort.Issue issue = row.Tag as CarPort.Issue;
                string text = row.Cells[2].Value as string;
                if (issue == null || text == null)
                {
                    continue;
                }
                chosen.Remove(issue.Key);
                foreach (CarPort.Choice choice in issue.Choices)
                {
                    if (choice.Label == text)
                    {
                        chosen[issue.Key] = choice.Value;
                    }
                }
            }
        }

        // A fix changed by the user in the grid for a car of the pack, after the pack trial: Build would pack the trial made
        // with the old fixes (the "458 R8" pack, 2026-11-21: materials chosen after the trial never reached it). The trial
        // is then out of date: Build goes off until Trial Pack runs again (user 2026-11-21). The grid's own presets do not count.
        private void CheckPackTrialFixes()
        {
            if (this.showingFixes || this.fixCar == null || this.packTrialFolder == null || this.packTrialFixes == null)
            {
                return;
            }
            Dictionary<string, object> used;
            if (!this.packTrialFixes.TryGetValue(this.fixCar, out used))
            {
                return;     // not a car of the trial
            }
            if (SameFixes(used, this.FixesOf(this.fixCar)))
            {
                return;
            }
            this.packTrialFolder = null;
            this.packTrialFixes = null;
            this.UpdateButtons();
            this.status.Text = "Fixes changed: Trial Pack again.";
        }

        private static bool SameFixes(Dictionary<string, object> a, Dictionary<string, object> b)
        {
            int countA = (a == null) ? 0 : a.Count;
            int countB = (b == null) ? 0 : b.Count;
            if (countA != countB)
            {
                return false;
            }
            if (countA == 0)
            {
                return true;
            }
            foreach (KeyValuePair<string, object> pair in a)
            {
                object other;
                if (!b.TryGetValue(pair.Key, out other)
                    || Convert.ToString(pair.Value, CultureInfo.InvariantCulture) != Convert.ToString(other, CultureInfo.InvariantCulture))
                {
                    return false;
                }
            }
            return true;
        }

        // The fixes chosen for a car so far (a copy), or null.
        private Dictionary<string, object> FixesOf(string mediaName)
        {
            Dictionary<string, object> chosen;
            return (this.fixes.TryGetValue(mediaName, out chosen) && chosen.Count > 0) ? new Dictionary<string, object>(chosen) : null;
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
            this.upgradesTitle.Text = (this.sourceGame == ForzaGame.Motorsport4)
                ? car.Name + ": the upgrades FM4 has for it and what the port does with them in FM3 (FM3 has 8 engine levels, FM4 4: the car keeps FM4's)"
                : car.Name + ": the upgrades " + GameInfo.Name(this.sourceGame) + " has for it (the car passes through its FM4 form first: the trial report lists what each of the two steps does)";
            if (this.sourceGame == ForzaGame.Motorsport2)
            {
                // FM2's upgrade tables have another layout; its car gets the upgrades of its FM4 form (step 1, Fm2Source)
                this.upgradesTitle.Text = car.Name + ": the upgrades come with the car's FM4 form (step 1 builds them from the FM2 car and its FM4 template): the trial report lists what each of the two steps does";
                return;
            }
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
            this.packList.SelectedIndexChanged += delegate { this.UpdateButtons(); this.ShowPackFixes(); };
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
            added.Choices = this.CurrentChoices();
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
            ForzaGame game = this.sourceGame;
            List<PackItem> items = new List<PackItem>(this.pack);
            if (items.Count == 0)
            {
                return;
            }
            string folder = Path.Combine(Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "PackFm3"), "pack_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);
            this.packTrialFolder = null;
            this.report.Text = string.Format("Pack trial: {0} car(s) -> {1}\r\n", items.Count, folder);
            // the review's choices of each car so far (Horizon cars), and what this trial finds
            this.StoreFixes();
            Dictionary<string, Dictionary<string, object>> packFixes = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
            foreach (PackItem item in items)
            {
                packFixes[item.Car.MediaName] = this.FixesOf(item.Car.MediaName);
            }
            Dictionary<string, List<CarPort.Issue>> foundInPack = new Dictionary<string, List<CarPort.Issue>>(StringComparer.OrdinalIgnoreCase);
            List<CarMakeSpec> newMakes = this.MakesToWrite(false, null);   // the pack's new makes: into the trial, and with it into the pack
            this.Background(delegate
            {
                StringBuilder text = new StringBuilder();
                string failed = null;
                Exception error = null;
                List<string[]> review = new List<string[]>();
                try
                {
                    WriteMakes(newMakes, target, folder, text);
                }
                catch (Exception e)
                {
                    error = e;
                    failed = "new make";
                    items = new List<PackItem>();   // no car runs without its make
                }
                // FH1 / FH2 / FM2 cars: their FM4 forms go one on top of the other too, in one folder for the whole trial (as the FM4
                // window's pack trial), so each takes car / engine / drivetrain / wheel ids of its own.
                string fm4Stage = Path.Combine(Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "CarPortViaFm4"), "pack_" + Path.GetFileName(folder));
                try
                {
                    foreach (PackItem item in items)
                    {
                        text.AppendLine().AppendLine("=== " + item.Car.Name + " (" + item.Car.MediaName + "), template " + item.Donor);
                        // the cars of the pack go one on top of the other in 'folder': the next reads the previous one's database
                        List<CarPort.Issue> carIssues;
                        string carReview;
                        string carReport = RunCar(game, source, target, folder, item.Car, item.Donor, this.DonorRootOf(item.Donor), packFixes[item.Car.MediaName], item.Choices, fm4Stage, out carIssues, out carReview, out error);
                        foundInPack[item.Car.MediaName] = carIssues;
                        if (error != null)
                        {
                            failed = item.Car.MediaName;
                        }
                        text.Append(carReport.Replace("\r\n", "\n").Replace("\n", "\r\n"));
                        review.AddRange(ReviewRows(item.Car.Name, carReview));
                        if (error != null)
                        {
                            break;
                        }
                    }
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(fm4Stage))
                        {
                            Directory.Delete(fm4Stage, true);
                        }
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
                return delegate
                {
                    this.report.Text += text.ToString();
                    this.FillReview(review);
                    foreach (KeyValuePair<string, List<CarPort.Issue>> pair in foundInPack)
                    {
                        this.issuesOf[pair.Key] = pair.Value;
                    }
                    this.ShowPackTrialFixes();
                    if (error != null)
                    {
                        this.report.Text += "\r\nFAILED (" + failed + "): " + error.Message + "\r\n";
                        this.status.Text = failed + ": the trial failed, see the report. Change its template or take it out of the pack.";
                    }
                    else
                    {
                        this.report.Text += "\r\npack trial done; files in " + folder + "\r\n";
                        this.packTrialFolder = folder;
                        this.packTrialFixes = packFixes;   // what this trial was made with (CheckPackTrialFixes)
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
            if (!XeniaGuard.Confirm(this, this.Text, "FM3's DLC packages"))
            {
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
                string start = DlcPackage.PackageOutputStart("CarPort.XboxOut.FM3", DlcPackage.TitleIdFm3);   // never another game's folder
                if (start != null)
                {
                    dialog.SelectedPath = start;
                }
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                output = dialog.SelectedPath;
            }
            string foreign = DlcPackage.ForeignTitleFolder(output, DlcPackage.TitleIdFm3);
            if (foreign != null)
            {
                MessageBox.Show(this, output + " is the content folder of another game (title " + foreign + ").\n\nThis is a Forza Motorsport 3 package (title " + DlcPackage.TitleIdFm3 + "): choose its own folder.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            AppSettings.Set("CarPort.XboxOut.FM3", output);
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
            if (!XeniaGuard.Confirm(this, this.Text, "FM3's DLC packages"))
            {
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
                if (!XeniaGuard.Confirm(this, this.Text, "FM3's game files"))
                {
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
            ForzaGame game = this.sourceGame;
            string folder = Path.Combine(Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "CarPortFm3"), car.MediaName + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);
            this.report.Text = string.Format("{0} {1} -> {2}, donor {3}...\r\n", installing ? "Installing" : "Trial run:", car.MediaName, target, donor);
            this.StoreFixes();
            Dictionary<string, object> carFixes = this.FixesOf(car.MediaName);   // the review's choices so far (Horizon cars)
            CarChoices choices = this.CurrentChoices();
            // New makes of this window: all of them into a trial; into the game only the one the car goes under (as the FM4 window)
            List<CarMakeSpec> newMakes = this.MakesToWrite(installing, choices.MakeId);
            this.Background(delegate
            {
                string result;
                List<string[]> review = new List<string[]>();
                string portReport = "";
                List<CarPort.Issue> found = null;
                string reviewText = "";
                try
                {
                    Exception stopped;
                    StringBuilder makeText = new StringBuilder();
                    try
                    {
                        WriteMakes(newMakes, target, folder, makeText);
                    }
                    finally
                    {
                        portReport = makeText.ToString();
                    }
                    portReport += RunCar(game, source, target, folder, car, donor, this.DonorRootOf(donor), carFixes, choices, null, out found, out reviewText, out stopped);
                    if (stopped != null)
                    {
                        throw new InvalidOperationException(stopped.Message, stopped);   // its own stack trace is shown below
                    }
                    result = portReport;
                    review.AddRange(ReviewRows(car.Name, reviewText));
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
                    review.AddRange(ReviewRows(car.Name, (reviewText.Length > 0) ? reviewText : portReport));
                    result = portReport + "\r\nFAILED: " + e.Message + "\r\n" + (e.InnerException ?? e).StackTrace;
                }
                return delegate
                {
                    this.report.Text = result.Replace("\r\n", "\n").Replace("\n", "\r\n");
                    this.FillReview(review);
                    this.issuesOf[car.MediaName] = found;
                    this.ShowFixes(car.MediaName, found);
                    bool failed = result.IndexOf("FAILED:", StringComparison.Ordinal) >= 0;
                    if (installing && !failed)
                    {
                        this.MakesInstalled(newMakes);   // now in the game's database: no longer written into trials
                    }
                    this.status.Text = failed ? car.MediaName + ": failed, see the report." : (installing ? car.MediaName + " installed in FM3." : car.MediaName + ": trial run OK.");
                };
            });
        }

        // The template car's package laid out as a game folder (DlcSource.Stage, kept under %TEMP%, and its zip written),
        // or null for a car of the FM3 folder. Called off the UI thread: laying a package out takes a while the first time.
        private string DonorRootOf(string donor)
        {
            DlcSource pack;
            lock (this.donorPacks)
            {
                if (donor == null || !this.donorPacks.TryGetValue(donor, out pack))
                {
                    return null;
                }
            }
            string root = pack.Stage(new StringBuilder());
            DlcSource.Materialize(root, donor);
            return root;
        }

        // One car into 'folder' (the FM3 result, never a game folder): an FM4 car (or a car of an FM4 pack) straight through
        // CarPortToFm3, a Horizon car through its FM4 form (CarPortViaFm4). Returns the report; 'error' is what stopped it.
        private static string RunCar(ForzaGame game, string source, string target, string folder, SourceCar car, string donor, string donorRoot, Dictionary<string, object> fixes, CarChoices choices, string sharedStage, out List<CarPort.Issue> issues, out string reviewText, out Exception error)
        {
            error = null;
            issues = null;
            reviewText = "";
            if (game == ForzaGame.Horizon1 || game == ForzaGame.Horizon2 || game == ForzaGame.Motorsport2)
            {
                CarPortViaFm4 chain = new CarPortViaFm4();
                chain.SourceRoot = car.Root ?? source;   // a car of an FM2 DLC package: its stage (CarPort materializes the car)
                chain.Fm4Root = GameInfo.ConfiguredFolder(ForzaGame.Motorsport4);
                chain.TargetRoot = target;
                chain.OutputRoot = folder;
                chain.MediaName = car.MediaName;
                chain.DonorMediaName = donor;
                chain.DonorRoot = donorRoot;
                chain.XeniaContent = InstalledDlc.ContentFolder(ForzaGame.Motorsport3);
                chain.Fixes = fixes;
                chain.SharedStage = sharedStage;   // a pack trial: one FM4-form folder for all its cars (ids of their own)
                if (choices != null)
                {
                    chain.Arcade = choices.Arcade;
                    chain.MakeId = choices.MakeId;
                    chain.SoundFrom = choices.Sound;
                    chain.Brakes = choices.Brakes;
                }
                try
                {
                    chain.Run();
                }
                catch (Exception e)
                {
                    error = e;
                }
                issues = chain.Issues;
                reviewText = chain.SecondReport;   // the review list: what the FM3 step did (the FM4 form's issues are in the grid)
                return chain.Report.ToString();
            }
            CarPortToFm3 port = new CarPortToFm3();
            port.SourceRoot = source;
            port.TargetRoot = target;
            port.OutputRoot = folder;
            port.MediaName = car.MediaName;
            port.DonorMediaName = donor;
            port.DonorRoot = donorRoot;
            port.XeniaContent = InstalledDlc.ContentFolder(ForzaGame.Motorsport3);
            port.Fixes = fixes;     // the review's material choices (FM3 names)
            if (choices != null)
            {
                port.Arcade = choices.Arcade;
                port.MakeId = choices.MakeId;
                port.SoundDonorMediaName = choices.Sound;   // (an FM4 car keeps its own brakes and caliper angles)
            }
            try
            {
                if (car.Root != null)
                {
                    DlcSource.Materialize(car.Root, car.MediaName);
                    port.PackRoot = car.Root;
                }
                port.Run();
            }
            catch (Exception e)
            {
                error = e;
            }
            issues = port.Issues;
            reviewText = port.Report.ToString();
            return reviewText;
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
