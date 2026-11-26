namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Windows.Forms;

    // Import car from another Forza, into FM2 (CarPortFm2Chain: Duplicate Car of an FM2 template, CarPortToFm2,
    // CarDataToFm2, Fm2SoundPort). The same screen as the FM3 window (CarPortFm3Form): brand bar and game banner, the two
    // game folders, the cars, the selected car's template, the report. Its own part is the Variants tab: FM2 holds 65536
    // vertices in each of a car's two shared buffers, and when a car does not fit the user ticks the upgrade variants to
    // port (user 2026-11-22); Install stays off while a buffer is over.
    internal sealed partial class CarPortFm2Form : Form
    {
        private sealed class SourceCar
        {
            public long Id;
            public string MediaName;
            public string Name;
            public string Short;
            public long Year;
            public string Root;         // the staged DLC pack the car comes from, or null for the game folder itself
            public DlcSource Pack;
            public string Dlc;          // the pack's name, or ""
            public bool Traffic;        // FH traffic car (placeholder close-up models): shown yellow, as in the FM3 / FM4 windows
            // FM2 has the car already ("Also show cars FM2 already has"):
            public bool Fm2Own;         // the MediaName is one of FM2's own cars: ported beside it under TargetMedia, never over it
            public string TargetMedia;  // the car's MediaName in FM2 (the source's, or the renamed copy's)
            public long Fm2Id;          // FM2 has a ported car under TargetMedia: its id (Install ports again over it); else 0
            public Fm2PortRecord Record;    // that car's port record (template and choices of the first port), or null
            public bool RecordApplied;  // the record's template / sound were put into the window once
            public long SourceMake = -1;    // the source's Data_Car.MakeID
            public long MakeId = -1;        // the Make chosen (FM2 List_CarMake.ID); -1: the source's (added to FM2 when it lacks it)

            public bool InFm2 { get { return this.Fm2Own || this.Fm2Id > 0; } }

            public string Fm2Text
            {
                get
                {
                    string ported = (this.Fm2Id > 0) ? "ported" + (this.Fm2Own ? " as " + this.TargetMedia : "") + ", car " + this.Fm2Id.ToString(CultureInfo.InvariantCulture)
                        + ((this.Record == null) ? ", choices not recorded" : "") : null;
                    return this.Fm2Own ? "FM2 car" + ((ported != null) ? "; " + ported : ", a copy goes in as " + this.TargetMedia) : (ported ?? "");
                }
            }
        }

        // What the last trial run of a car found: its parts against one template.
        private sealed class Trial
        {
            public string Template;
            public List<Fm2CarbinModel.PlanPart> Plan;
            public List<Fm2CarbinModel.PlanPart> Wheel;     // the stock wheel's parts; null: the wheel is not ported
            public HashSet<string> Skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // the user's choices in the Materials grid: source material -> FM2 material (no entry: left as it is)
            public Dictionary<string, string> CarMaterials = new Dictionary<string, string>(StringComparer.Ordinal);
            public Dictionary<string, string> WheelMaterials = new Dictionary<string, string>(StringComparer.Ordinal);
            public List<ReviewRow> Review = new List<ReviewRow>();     // the database dry run's lines worth a look
            // Upgrades tab: the engine swaps (the source's, FM2 engines added, a drive conversion) and the dry run's table
            public List<SwapItem> Swaps = new List<SwapItem>();
            public long StockDrive;                 // the source car's drive type (1 FWD, 2 RWD, 3 AWD)
            public List<string[]> Upgrades = new List<string[]>();
            public bool SwapsChanged;               // changed after the dry run: Trial Run again to see the database with them
        }

        // One engine swap of the Upgrades tab. A source swap left out stays listed, unticked; FM2 engines and the drive
        // conversion are removed with Remove.
        private sealed class SwapItem
        {
            public CarDataToFm2.Fm2Swap Swap;
            public bool Included = true;
            public string Engine;       // shown name

            public SwapItem Copy()
            {
                SwapItem copy = new SwapItem();
                copy.Swap = CarDataToFm2.Fm2Swap.Parse(this.Swap.ToString());
                copy.Included = this.Included;
                copy.Engine = this.Engine;
                return copy;
            }
        }

        // The swaps the trial, the install and the record use.
        private static List<CarDataToFm2.Fm2Swap> ChosenSwaps(Trial trial)
        {
            List<CarDataToFm2.Fm2Swap> swaps = new List<CarDataToFm2.Fm2Swap>();
            foreach (SwapItem item in trial.Swaps)
            {
                if (item.Included)
                {
                    swaps.Add(item.Swap);
                }
            }
            return swaps;
        }

        // One row of the Review tab: what the port could not do, did by itself, adds to FM2, or what the user chose.
        private enum ReviewKind { Info, Fixed, Added, Warning, Error }

        private sealed class ReviewRow
        {
            public string Where;
            public string What;
            public ReviewKind Kind;
            public MaterialRow Material;    // a material FM2 does not use: Fix = the same choice as the Materials tab
            public bool Variant;            // a variant left out: double-click opens the Variants tab

            public ReviewRow(string where, string what, ReviewKind kind)
            {
                this.Where = where;
                this.What = what;
                this.Kind = kind;
            }
        }

        // One row of the Materials grid: a source material FM2's own cars (or wheels) do not use.
        private sealed class MaterialRow
        {
            public bool Wheel;
            public string Material;
            public List<string> Parts = new List<string>();
        }

        private const string LeaveAsIs = "(leave as is)";

        private static List<MaterialRow> UnknownMaterials(Trial trial)
        {
            List<MaterialRow> rows = new List<MaterialRow>();
            for (int pass = 0; pass < 2; pass++)
            {
                List<Fm2CarbinModel.PlanPart> parts = (pass == 0) ? trial.Plan : trial.Wheel;
                if (parts == null)
                {
                    continue;
                }
                foreach (Fm2CarbinModel.PlanPart part in parts)
                {
                    foreach (KeyValuePair<string, List<uint>> material in part.Materials)
                    {
                        bool used = true;
                        foreach (uint lod in material.Value)
                        {
                            used &= CarPortToFm2.Fm2Uses(pass == 1, lod, material.Key);
                        }
                        if (used)
                        {
                            continue;
                        }
                        MaterialRow row = rows.Find(delegate(MaterialRow r) { return r.Wheel == (pass == 1) && r.Material == material.Key; });
                        if (row == null)
                        {
                            row = new MaterialRow();
                            row.Wheel = pass == 1;
                            row.Material = material.Key;
                            rows.Add(row);
                        }
                        row.Parts.Add(part.Name);
                    }
                }
            }
            rows.Sort(delegate(MaterialRow a, MaterialRow b)
            {
                return (a.Wheel != b.Wheel) ? (a.Wheel ? 1 : -1) : string.Compare(a.Material, b.Material, StringComparison.OrdinalIgnoreCase);
            });
            return rows;
        }

        private const int BufferSize = 65536;

        private readonly TextBox sourceBox;
        private readonly TextBox targetBox;
        private readonly Button load;
        private readonly TextBox search;
        private readonly CheckBox includeDlc;
        private readonly CheckBox showInFm2;
        private readonly ListView cars;
        private readonly ComboBox templates;
        private readonly RadioButton portSound;
        private readonly RadioButton useSound;
        private readonly ComboBox soundCars;    // the FM2 cars (templateList); "Use sound": the one whose engine sound the car gets
        private bool soundPicked;               // the user chose a sound car: a template change no longer moves it
        private readonly Button trial;
        private readonly Button install;
        private readonly TextBox report;
        private readonly ToolStripStatusLabel status;
        private readonly TabControl tabs;
        private readonly TabPage variantsPage;
        private readonly Label opaqueCount;
        private readonly Label seeThroughCount;
        private readonly Label variantsTitle;
        private readonly DataGridView variants;
        private readonly TabPage materialsPage;
        private TabPage upgradesPage;
        private Label upgradesTitle;
        private ListView swapList;
        private ComboBox swapLevel;
        private ComboBox fm2Engines;
        private ComboBox addLevel;
        private Button addEngine;
        private Button removeSwap;
        private Label driveStock;
        private ComboBox driveChoice;
        private ComboBox driveLevel;
        private ListView upgradeTable;
        private bool upgradesFilling;
        private List<object[]> fm2EngineList = new List<object[]>();     // FM2's own engines: EngineID, MediaName, DriveTypeID
        private readonly TabPage reviewPage;
        private readonly Label reviewTitle;
        private readonly DataGridView review;
        private readonly Dictionary<string, List<ReviewRow>> installReviews = new Dictionary<string, List<ReviewRow>>(StringComparer.OrdinalIgnoreCase);
        private bool reviewFilling;
        private readonly Label materialsTitle;
        private readonly DataGridView materials;
        private readonly List<ListViewItem> items = new List<ListViewItem>();
        private readonly Dictionary<string, Trial> trials = new Dictionary<string, Trial>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> packFiles = new List<string>();     // DLC package files chosen with Package... (this window only)
        private List<string[]> templateList = new List<string[]>();
        private ForzaGame sourceGame = ForzaGame.Unknown;
        private string loadedSource;
        private string loadedTarget;
        private bool busy;
        private bool filling;

        public CarPortFm2Form()
        {
            this.Text = "Import Car From Another Forza > FM2";
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

            MenuStrip header = new MenuStrip();
            ToolStripLabel forza = new ToolStripLabel("FORZA");
            forza.Font = new Font("Segoe UI", 11f, FontStyle.Bold | FontStyle.Italic);
            forza.ForeColor = ForzaTheme.Red;
            forza.Margin = new Padding(6, 1, 0, 2);
            ToolStripLabel studio = new ToolStripLabel("STUDIO EVO");
            studio.Font = new Font("Segoe UI Light", 11f, FontStyle.Italic);
            studio.Margin = new Padding(0, 1, 18, 2);
            ToolStripLabel title = new ToolStripLabel("IMPORT A CAR FROM FORZA MOTORSPORT 4, FORZA MOTORSPORT 3 OR FORZA HORIZON INTO FORZA MOTORSPORT 2");
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

            GameBanner banner = new GameBanner("FORZA MOTORSPORT 2", "FROM  FM4 / FM3 / FH1 / FH2", Color.FromArgb(200, 70, 60));
            banner.Dock = DockStyle.Top;

            Panel folders = new Panel();
            folders.Dock = DockStyle.Top;
            folders.Height = 74;
            folders.Width = this.ClientSize.Width;
            this.sourceBox = this.FolderRow(folders, "Source game (FM4/FM3/FH1/FH2):", 10, DefaultSource(), true);
            this.targetBox = this.FolderRow(folders, "Forza Motorsport 2:", 42, DefaultTarget(), false);
            this.load = new Button();
            this.load.Text = "Load Cars";
            this.load.Size = new Size(110, 56);
            this.load.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            this.load.Location = new Point(folders.Width - 122, 9);
            this.load.Click += delegate { this.LoadCars(); };
            folders.Controls.Add(this.load);

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
            this.includeDlc = new CheckBox();
            this.includeDlc.Text = "Include the game's DLC";
            this.includeDlc.Checked = AppSettings.Get("PortFm2.IncludeDlc", "0") == "1";
            this.includeDlc.Location = new Point(338, 7);
            this.includeDlc.AutoSize = true;
            this.includeDlc.CheckedChanged += delegate
            {
                AppSettings.Set("PortFm2.IncludeDlc", this.includeDlc.Checked ? "1" : "0");
                if (this.items.Count > 0 && !this.busy)
                {
                    this.LoadCars();
                }
            };
            // the cars FM2 already has: the ported ones to port again over (choices of the first port read back), FM2's own
            // ones to port beside them under another name (as the FM3 / FM4 windows)
            this.showInFm2 = new CheckBox();
            this.showInFm2.Text = "Also show cars FM2 already has";
            this.showInFm2.Location = new Point(538, 7);
            this.showInFm2.AutoSize = true;
            this.showInFm2.CheckedChanged += delegate { this.Filter(); };
            filter.Controls.Add(find);
            filter.Controls.Add(this.search);
            filter.Controls.Add(this.includeDlc);
            filter.Controls.Add(this.showInFm2);

            this.cars = new ListView();
            this.cars.Dock = DockStyle.Fill;
            this.cars.View = View.Details;
            this.cars.FullRowSelect = true;
            this.cars.MultiSelect = false;
            this.cars.HideSelection = false;
            this.cars.Columns.Add("Id", 56);
            this.cars.Columns.Add("Car", 260);
            this.cars.Columns.Add("Year", 50);
            this.cars.Columns.Add("Media name", 200);
            this.cars.Columns.Add("DLC pack", 220);
            this.cars.Columns.Add("In FM2", 260);
            this.cars.SelectedIndexChanged += delegate { this.ApplyRecord(); this.ShowMake(); this.ShowTrial(); this.UpdateButtons(); };
            CarPortFm3Form.StyleList(this.cars);

            Panel options = new Panel();
            options.Dock = DockStyle.Fill;
            Label templateLabel = new Label();
            templateLabel.Text = "FM2 template car: the new car starts as a copy of it, then everything of it is replaced (your choice):";
            templateLabel.Location = new Point(8, 8);
            templateLabel.AutoSize = true;
            this.templates = new ComboBox();
            this.templates.DropDownStyle = ComboBoxStyle.DropDownList;
            this.templates.Location = new Point(8, 28);
            this.templates.Width = 460;
            this.templates.MaxDropDownItems = 24;
            this.templates.SelectedIndexChanged += delegate
            {
                // "Use sound" starts on the template car until the user picks another
                if (!this.soundPicked && this.templates.SelectedIndex >= 0 && this.templates.SelectedIndex < this.soundCars.Items.Count)
                {
                    this.soundCars.SelectedIndex = this.templates.SelectedIndex;
                    this.soundPicked = false;
                }
                this.ShowTrial();
                this.UpdateButtons();
            };
            // Engine sound (user 2026-11-25: "port sound / use sound"): the source's, built into FM2 banks, or an FM2 car's
            Label soundLabel = new Label();
            soundLabel.Text = "Engine sound:";
            soundLabel.Location = new Point(8, 66);
            soundLabel.AutoSize = true;
            this.portSound = new RadioButton();
            this.portSound.Text = "Port sound (the source car's)";
            this.portSound.Location = new Point(104, 63);
            this.portSound.AutoSize = true;
            this.portSound.Checked = true;
            this.useSound = new RadioButton();
            this.useSound.Text = "Use sound of the FM2 car:";
            this.useSound.Location = new Point(300, 63);
            this.useSound.AutoSize = true;
            this.soundCars = new ComboBox();
            this.soundCars.DropDownStyle = ComboBoxStyle.DropDownList;
            this.soundCars.Location = new Point(480, 62);
            this.soundCars.Width = 360;
            this.soundCars.MaxDropDownItems = 24;
            this.soundCars.Enabled = false;
            this.soundCars.SelectionChangeCommitted += delegate { this.soundPicked = true; };
            this.useSound.CheckedChanged += delegate { this.soundCars.Enabled = this.useSound.Checked; };
            this.trial = new Button();
            this.trial.Text = "Trial Run";
            this.trial.Size = new Size(110, 28);
            this.trial.Location = new Point(8, 94);
            this.trial.Click += delegate { this.RunTrial(); };
            this.install = new Button();
            this.install.Text = "Install In FM2...";
            this.install.Size = new Size(130, 28);
            this.install.Location = new Point(124, 94);
            this.install.Click += delegate { this.Install(); };
            options.Controls.Add(templateLabel);
            options.Controls.Add(this.templates);
            this.BuildMakeRow(options);
            options.Controls.Add(soundLabel);
            options.Controls.Add(this.portSound);
            options.Controls.Add(this.useSound);
            options.Controls.Add(this.soundCars);
            options.Controls.Add(this.trial);
            options.Controls.Add(this.install);
            Panel selectedCar = new Panel();
            selectedCar.Dock = DockStyle.Bottom;
            selectedCar.Height = 162;
            selectedCar.Controls.Add(options);
            selectedCar.Controls.Add(CarPortForm.SectionHeader("Selected car"));

            Panel carsArea = new Panel();
            carsArea.Dock = DockStyle.Fill;
            carsArea.Controls.Add(this.cars);
            carsArea.Controls.Add(selectedCar);
            carsArea.Controls.Add(filter);
            Control carsHeader = CarPortForm.SectionHeader("Cars");
            // the traffic colour of the car list at the right of the header, as the FM3 window draws it (user 2026-11-25)
            carsHeader.Paint += delegate(object sender, PaintEventArgs e)
            {
                const string name = "Traffic car";
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

            this.report = new TextBox();
            this.report.Dock = DockStyle.Fill;
            this.report.Multiline = true;
            this.report.ReadOnly = true;
            this.report.ScrollBars = ScrollBars.Both;
            this.report.WordWrap = false;
            this.report.Font = new Font("Consolas", 9f);
            this.tabs = new TabControl();
            TabPage reportPage = new TabPage("Report");
            reportPage.Controls.Add(this.report);

            // Variants: the two counters, then one row per upgrade variant of the car.
            this.variantsPage = new TabPage("Variants");
            Panel counters = new Panel();
            counters.Dock = DockStyle.Top;
            counters.Height = 52;
            this.variantsTitle = new Label();
            this.variantsTitle.Location = new Point(6, 5);
            this.variantsTitle.AutoSize = true;
            this.variantsTitle.Font = ForzaTheme.Bold;
            this.variantsTitle.Text = "Run a trial on a car: its upgrade variants are listed here.";
            this.opaqueCount = new Label();
            this.opaqueCount.Location = new Point(6, 28);
            this.opaqueCount.AutoSize = true;
            this.opaqueCount.Font = ForzaTheme.Bold;
            this.seeThroughCount = new Label();
            this.seeThroughCount.Location = new Point(330, 28);
            this.seeThroughCount.AutoSize = true;
            this.seeThroughCount.Font = ForzaTheme.Bold;
            counters.Controls.Add(this.variantsTitle);
            counters.Controls.Add(this.opaqueCount);
            counters.Controls.Add(this.seeThroughCount);
            this.variants = new DataGridView();
            this.variants.Dock = DockStyle.Fill;
            this.variants.AllowUserToAddRows = false;
            this.variants.AllowUserToDeleteRows = false;
            this.variants.AllowUserToResizeRows = false;
            this.variants.RowHeadersVisible = false;
            this.variants.SelectionMode = DataGridViewSelectionMode.CellSelect;
            this.variants.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            DataGridViewCheckBoxColumn portColumn = new DataGridViewCheckBoxColumn();
            portColumn.HeaderText = "Port";
            portColumn.FillWeight = 10;
            DataGridViewTextBoxColumn partColumn = new DataGridViewTextBoxColumn();
            partColumn.HeaderText = "Upgrade variant (part)";
            partColumn.ReadOnly = true;
            partColumn.FillWeight = 40;
            DataGridViewTextBoxColumn vertexColumn = new DataGridViewTextBoxColumn();
            vertexColumn.HeaderText = "Vertices";
            vertexColumn.ReadOnly = true;
            vertexColumn.FillWeight = 20;
            DataGridViewTextBoxColumn bufferColumn = new DataGridViewTextBoxColumn();
            bufferColumn.HeaderText = "Buffer";
            bufferColumn.ReadOnly = true;
            bufferColumn.FillWeight = 30;
            this.variants.Columns.Add(portColumn);
            this.variants.Columns.Add(partColumn);
            this.variants.Columns.Add(vertexColumn);
            this.variants.Columns.Add(bufferColumn);
            this.variants.CurrentCellDirtyStateChanged += delegate
            {
                if (this.variants.IsCurrentCellDirty)
                {
                    this.variants.CommitEdit(DataGridViewDataErrorContexts.Commit);     // a tick counts at once
                }
            };
            this.variants.CellValueChanged += delegate { this.VariantsChanged(); };
            this.variantsPage.Controls.Add(this.variants);
            this.variantsPage.Controls.Add(counters);

            // Materials: one row per source material FM2's own cars do not use; the fix is the user's choice, never the tool's.
            this.materialsPage = new TabPage("Materials");
            this.materialsTitle = new Label();
            this.materialsTitle.Dock = DockStyle.Top;
            this.materialsTitle.Height = 26;
            this.materialsTitle.Font = ForzaTheme.Bold;
            this.materialsTitle.Padding = new Padding(6, 6, 0, 0);
            this.materials = new DataGridView();
            this.materials.Dock = DockStyle.Fill;
            this.materials.AllowUserToAddRows = false;
            this.materials.AllowUserToDeleteRows = false;
            this.materials.AllowUserToResizeRows = false;
            this.materials.RowHeadersVisible = false;
            this.materials.SelectionMode = DataGridViewSelectionMode.CellSelect;
            this.materials.EditMode = DataGridViewEditMode.EditOnEnter;
            this.materials.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            DataGridViewTextBoxColumn whereColumn = new DataGridViewTextBoxColumn();
            whereColumn.HeaderText = "Where";
            whereColumn.ReadOnly = true;
            whereColumn.FillWeight = 8;
            DataGridViewTextBoxColumn materialColumn = new DataGridViewTextBoxColumn();
            materialColumn.HeaderText = "Material FM2 does not use";
            materialColumn.ReadOnly = true;
            materialColumn.FillWeight = 20;
            DataGridViewTextBoxColumn partsColumn = new DataGridViewTextBoxColumn();
            partsColumn.HeaderText = "Parts that use it";
            partsColumn.ReadOnly = true;
            partsColumn.FillWeight = 50;
            DataGridViewComboBoxColumn choiceColumn = new DataGridViewComboBoxColumn();
            choiceColumn.HeaderText = "FM2 material (your choice)";
            choiceColumn.FillWeight = 22;
            choiceColumn.FlatStyle = FlatStyle.Flat;
            this.materials.Columns.Add(whereColumn);
            this.materials.Columns.Add(materialColumn);
            this.materials.Columns.Add(partsColumn);
            this.materials.Columns.Add(choiceColumn);
            this.materials.CurrentCellDirtyStateChanged += delegate
            {
                if (this.materials.IsCurrentCellDirty)
                {
                    this.materials.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            this.materials.CellValueChanged += delegate { this.MaterialsChanged(); };
            this.materials.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };
            this.materialsPage.Controls.Add(this.materials);
            this.materialsPage.Controls.Add(this.materialsTitle);
            // a grid first shown after its rows were set lays its Fill columns out late (as the FM3 window's fix grid)
            this.materialsPage.VisibleChanged += delegate { this.materials.PerformLayout(); this.materials.Invalidate(true); };
            this.reviewPage = this.BuildReviewPage(out this.reviewTitle, out this.review);
            this.tabs.TabPages.Add(reportPage);
            this.tabs.TabPages.Add(this.reviewPage);
            this.tabs.TabPages.Add(this.variantsPage);
            this.tabs.TabPages.Add(this.materialsPage);
            this.tabs.TabPages.Add(this.BuildUpgradesPage());

            SplitContainer body = new SplitContainer();
            body.Dock = DockStyle.Fill;
            body.Orientation = Orientation.Horizontal;
            body.FixedPanel = FixedPanel.Panel2;
            body.Panel1.Controls.Add(carsArea);
            body.Panel2.Controls.Add(CarPortForm.TabHost(this.tabs));
            this.Load += delegate { body.SplitterDistance = Math.Max(200, body.Height - 280); };

            this.Controls.Add(body);
            this.Controls.Add(folders);
            this.Controls.Add(statusStrip);
            this.Controls.Add(banner);
            this.Controls.Add(header);
            this.MainMenuStrip = header;
            this.BackColor = ForzaTheme.Background;
            this.ForeColor = ForzaTheme.Text;
            ForzaTheme.Apply(this);
            this.ShowTrial();
            this.UpdateButtons();
        }

        // The source folder: the last one used here, else the FM4 folder of the settings.
        private static string DefaultSource()
        {
            string last = AppSettings.Get("PortFm2.Source", "");
            if (last.Length > 0 && Directory.Exists(last))
            {
                return last;
            }
            return GameInfo.ConfiguredFolder(ForzaGame.Motorsport4) ?? "";
        }

        // The FM2 folder: the last one used here, else the settings' FM2 folder, its test copy (<folder>_DEV) when there is one.
        private static string DefaultTarget()
        {
            string last = AppSettings.Get("PortFm2.Target", "");
            if (last.Length > 0 && Directory.Exists(last))
            {
                return last;
            }
            string configured = GameInfo.ConfiguredFolder(ForzaGame.Motorsport2);
            if (configured == null)
            {
                return "";
            }
            string dev = configured.TrimEnd('\\') + "_DEV";
            return Directory.Exists(dev) ? dev : configured;
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

        // A folder row as in the FM3 / FM4 windows: caption, box, Folder... (and Package... for the source).
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
            if (packages)
            {
                Button package = new Button();
                package.Text = "Package...";
                package.Size = new Size(72, 26);
                package.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                package.Location = new Point(parent.Width - 206, y - 2);
                new ToolTip().SetToolTip(package, "DLC package files of the source game (Xbox 360), installed in Xenia or not: their cars are listed with the game's. Press again to add more.");
                package.Click += delegate { this.ChoosePackages(); };
                parent.Controls.Add(package);
            }
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
            return box;
        }

        // Package... (as the FM3 / FM4 windows): single package files of the source game, kept while the window is open.
        private void ChoosePackages()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "DLC packages of the source game (Xbox 360 package files)";
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
            if (Directory.Exists(this.sourceBox.Text.Trim()) && Directory.Exists(this.targetBox.Text.Trim()) && !this.busy)
            {
                this.LoadCars();
            }
        }

        private static bool IsSource(ForzaGame game)
        {
            return game == ForzaGame.Motorsport4 || game == ForzaGame.Motorsport3 || game == ForzaGame.Horizon1 || game == ForzaGame.Horizon2;
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

        private void LoadCars()
        {
            string source = this.sourceBox.Text.Trim();
            string target = this.targetBox.Text.Trim();
            ForzaGame game = GameInfo.Detect(source);
            if (!IsSource(game) || GameInfo.Detect(target) != ForzaGame.Motorsport2)
            {
                MessageBox.Show(this, "The source must be a Forza Motorsport 4, Forza Motorsport 3, Forza Horizon or Forza Horizon 2 folder and the target a Forza Motorsport 2 folder.\n\nFound: "
                    + (GameInfo.Name(game) ?? "no Forza game") + " and " + (GameInfo.Name(GameInfo.Detect(target)) ?? "no Forza game") + ".", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            AppSettings.Set("PortFm2.Source", source);
            AppSettings.Set("PortFm2.Target", target);
            // the source folder gives its game's packages their base database: FM4's expansions (offers 1-4, e.g. the
            // 2006-Current Model Year pack with the M5 2009) have no database of their own (user 2026-11-25: M5 E60 not
            // listed), as the FM4 port window does
            GameInfo.UseSourceFolder(game, source);
            bool dlcToo = this.includeDlc.Checked;
            List<string> files = new List<string>(this.packFiles);
            this.status.Text = "Reading the databases...";
            this.Background(delegate
            {
                StringBuilder notes = new StringBuilder();
                List<SourceCar> list = new List<SourceCar>();
                List<string[]> templateCars = new List<string[]>();
                Dictionary<string, long> inFm2 = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                List<object[]> engines = new List<object[]>();
                List<object[]> fm2Makes = new List<object[]>();
                Dictionary<long, string> sourceMakes = new Dictionary<long, string>();
                using (SqliteDatabase to = SqliteDatabase.FromBytes(File.ReadAllBytes(CarPortFm2Chain.FindDatabase(target))))
                {
                    StringTable names2 = StringTableOrNull(target);
                    foreach (object[] row in to.Query("SELECT MediaName, DisplayName, Id FROM Data_Car ORDER BY MediaName").Rows)
                    {
                        string media = row[0] as string;
                        if (media == null)
                        {
                            continue;
                        }
                        inFm2[media] = Convert.ToInt64(row[2], CultureInfo.InvariantCulture);
                        if (GamePaths.FindMedia(target, "cars", media) != null)
                        {
                            string name = (names2 != null) ? names2.Lookup("Data_Car", "DisplayName", row[1] as string) : null;
                            templateCars.Add(new[] { media, string.IsNullOrEmpty(name) ? media : name });
                        }
                    }
                    // the engines the Upgrades tab can add as swaps: FM2's own (not the ports' own engines)
                    foreach (object[] row in to.Query("SELECT EngineID, MediaName, DriveTypeID FROM Data_Engine WHERE EngineID <= ? ORDER BY MediaName", GameIdLimits.Max(ForzaGame.Motorsport2, "Data_Engine", "EngineID")).Rows)
                    {
                        engines.Add(row);
                    }
                    // the makes the Make choice offers: FM2's (with their names)
                    foreach (object[] row in to.Query("SELECT ID, DisplayName, ManufacturerCode FROM List_CarMake ORDER BY ID").Rows)
                    {
                        string name = (names2 != null) ? names2.Lookup("List_CarMake", "DisplayName", row[1] as string) : null;
                        fm2Makes.Add(new object[] { Convert.ToInt64(row[0], CultureInfo.InvariantCulture), string.IsNullOrEmpty(name) ? Convert.ToString(row[2], CultureInfo.InvariantCulture) : name });
                    }
                }
                // the source's makes, for the default of the Make choice ("the source's: RUF, added to FM2")
                using (SqliteDatabase from = SqliteDatabase.FromBytes(File.ReadAllBytes(CarPortFm2Chain.FindDatabase(source))))
                {
                    StringTable names1 = StringTableOrNull(source);
                    foreach (object[] row in from.Query("SELECT ID, DisplayName, ManufacturerCode FROM List_CarMake").Rows)
                    {
                        string name = (names1 != null) ? names1.Lookup("List_CarMake", "DisplayName", row[1] as string) : null;
                        sourceMakes[Convert.ToInt64(row[0], CultureInfo.InvariantCulture)] = string.IsNullOrEmpty(name) ? Convert.ToString(row[2], CultureInfo.InvariantCulture) : name;
                    }
                }
                ReadCars(source, CarPortFm2Chain.FindDatabase(source), null, null, "", list);
                // the packages chosen with Package..., then the game's DLC installed in Xenia (Include the game's DLC)
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
                string content = dlcToo ? InstalledDlc.ContentFolder(game) : null;
                if (content != null)
                {
                    uint title = InstalledDlc.TitleId(game);
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
                        if (pack.Problem != null || pack.Game != game)
                        {
                            notes.AppendLine(label + ": left out, " + (pack.Problem ?? "not a " + GameInfo.Name(game) + " package"));
                            continue;
                        }
                        string root = pack.Stage(notes);
                        ReadCars(root, CarPortFm2Chain.FindDatabase(root), root, pack, label, list);
                    }
                    catch (Exception e)
                    {
                        notes.AppendLine(label + ": left out, " + e.Message);
                    }
                }
                // what FM2 has of each car: one of its own (a copy goes beside it under another name) or a port (ported again
                // over it, with the choices its port record gives back)
                long fm2Top = GameIdLimits.MaxCarBase(ForzaGame.Motorsport2);    // FM2's own cars (617); its official DLC ids reach 1024 but are no rows of the game's database, ports take 659-1000
                int newCars = 0;
                foreach (SourceCar car in list)
                {
                    long id;
                    car.Fm2Own = inFm2.TryGetValue(car.MediaName, out id) && id <= fm2Top;
                    car.TargetMedia = car.Fm2Own ? RenamedPort(car.MediaName, game) : car.MediaName;
                    if (inFm2.TryGetValue(car.TargetMedia, out id) && id > fm2Top)
                    {
                        car.Fm2Id = id;
                        car.Record = Fm2PortRecord.Read(GamePaths.FindCar(target, car.TargetMedia));
                    }
                    newCars += car.InFm2 ? 0 : 1;
                }
                return delegate
                {
                    this.sourceGame = game;
                    this.loadedSource = source;
                    this.loadedTarget = target;
                    this.trials.Clear();
                    this.items.Clear();
                    foreach (SourceCar car in list)
                    {
                        ListViewItem item = new ListViewItem(car.Id.ToString(CultureInfo.InvariantCulture));
                        item.Tag = car;
                        item.SubItems.Add(car.Name);
                        item.SubItems.Add(car.Year.ToString(CultureInfo.InvariantCulture));
                        item.SubItems.Add(car.MediaName);
                        item.SubItems.Add(car.Dlc);
                        item.SubItems.Add(car.Fm2Text);
                        if (car.InFm2)
                        {
                            item.ForeColor = ForzaTheme.TextDim;
                        }
                        else if (car.Traffic)
                        {
                            item.ForeColor = ForzaTheme.Yellow;
                        }
                        this.items.Add(item);
                    }
                    this.templateList = templateCars;
                    this.FillFm2Engines(engines);
                    this.fm2MakeList = fm2Makes;
                    this.sourceMakeNames = sourceMakes;
                    this.ShowMake();
                    this.templates.Items.Clear();
                    this.soundCars.Items.Clear();
                    this.soundPicked = false;
                    foreach (string[] one in templateCars)
                    {
                        this.templates.Items.Add(one[1] + "  (" + one[0] + ")");
                        this.soundCars.Items.Add(one[1] + "  (" + one[0] + ")");
                    }
                    this.Filter();
                    this.ShowTrial();
                    this.status.Text = string.Format(CultureInfo.InvariantCulture, "{0} {1} cars FM2 does not have ({3} it has: Also show cars FM2 already has), {2} FM2 template cars. Choose a car and its template, then Trial Run.", newCars, GameInfo.Name(game), templateCars.Count, list.Count - newCars);
                    if (notes.Length > 0)
                    {
                        this.report.Text = "DLC packages:\r\n" + notes.ToString();
                    }
                };
            });
        }

        // The cars of the source folder or of one staged pack (FM2's own ones and the ported ones too: LoadCars marks them).
        private static void ReadCars(string root, string database, string packRoot, DlcSource pack, string label, List<SourceCar> into)
        {
            StringTable names = StringTableOrNull(root);
            HashSet<string> own = (pack != null) ? new HashSet<string>(pack.Cars(), StringComparer.OrdinalIgnoreCase) : null;
            HashSet<string> listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // FH traffic cars (FH1 / FH2 folder or a staged FH package): yellow, found as the FM3 window finds them
            bool horizon = CarPortViaFm4.IsHorizon(root);
            Dictionary<string, KeyValuePair<bool, string>> lazy = horizon ? DlcSource.LazyCars(root) : null;
            foreach (SourceCar already in into)
            {
                listed.Add(already.MediaName);
            }
            using (SqliteDatabase source = SqliteDatabase.FromBytes(File.ReadAllBytes(database)))
            {
                if (!DbIds.HasTable(source, "Data_Car"))
                {
                    return;
                }
                bool hasShort = false;
                foreach (string column in source.Query("SELECT * FROM Data_Car LIMIT 0").Columns)
                {
                    hasShort |= string.Equals(column, "ModelShort", StringComparison.OrdinalIgnoreCase);
                }
                foreach (object[] row in source.Query("SELECT Id, MediaName, DisplayName, Year, " + (hasShort ? "ModelShort" : "NULL") + ", MakeID FROM Data_Car ORDER BY Id").Rows)
                {
                    string media = row[1] as string;
                    if (media == null || (own != null && !own.Contains(media)))
                    {
                        continue;
                    }
                    if (packRoot == null && GamePaths.FindMedia(root, "cars", media) == null)
                    {
                        continue;   // a row without the car's files (the game's DLC cars, read from their packages)
                    }
                    if (!listed.Add(media))
                    {
                        continue;
                    }
                    SourceCar car = new SourceCar();
                    car.Id = Convert.ToInt64(row[0], CultureInfo.InvariantCulture);
                    car.MediaName = media;
                    string name = (names != null) ? names.Lookup("Data_Car", "DisplayName", row[2] as string) : null;
                    car.Name = string.IsNullOrEmpty(name) ? media : name;
                    string shortName = (names != null) ? names.Lookup("Data_Car", "ModelShort", row[4] as string) : null;
                    car.Short = string.IsNullOrEmpty(shortName) ? car.Name : shortName;
                    car.Year = (row[3] == null || row[3] is DBNull) ? 0 : Convert.ToInt64(row[3], CultureInfo.InvariantCulture);
                    car.Root = packRoot;
                    car.Pack = pack;
                    car.Dlc = label;
                    car.Traffic = horizon && CarPortFm3Form.IsTraffic(root, media, lazy);
                    car.SourceMake = (row[5] == null || row[5] is DBNull) ? -1 : Convert.ToInt64(row[5], CultureInfo.InvariantCulture);
                    into.Add(car);
                }
            }
        }

        private void Filter()
        {
            string text = this.search.Text.Trim();
            List<ListViewItem> shown = new List<ListViewItem>();
            foreach (ListViewItem item in this.items)
            {
                SourceCar car = (SourceCar) item.Tag;
                if (text.Length > 0 && car.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0 && car.MediaName.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                if (car.InFm2 && !this.showInFm2.Checked)
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

        private SourceCar SelectedCar
        {
            get { return (this.cars.SelectedItems.Count == 1) ? (SourceCar) this.cars.SelectedItems[0].Tag : null; }
        }

        private string SelectedTemplate
        {
            get { return (this.templates.SelectedIndex >= 0 && this.templates.SelectedIndex < this.templateList.Count) ? this.templateList[this.templates.SelectedIndex][0] : null; }
        }

        // The selected car's trial, when it was run against the template now chosen.
        private Trial CurrentTrial
        {
            get
            {
                SourceCar car = this.SelectedCar;
                Trial found;
                return (car != null && this.trials.TryGetValue(car.MediaName, out found) && string.Equals(found.Template, this.SelectedTemplate, StringComparison.OrdinalIgnoreCase)) ? found : null;
            }
        }

        private void UpdateButtons()
        {
            bool ready = !this.busy && this.SelectedCar != null && this.SelectedTemplate != null;
            Trial current = this.CurrentTrial;
            int seeThrough, opaque;
            SourceCar car = this.SelectedCar;
            // a port again never takes the car itself as its template (its model is the port's, not an FM2 car's)
            ready &= car == null || car.Fm2Id == 0 || !string.Equals(this.SelectedTemplate, car.TargetMedia, StringComparison.OrdinalIgnoreCase);
            this.load.Enabled = !this.busy;
            this.trial.Enabled = ready;
            this.install.Enabled = ready && current != null && CarPortFm2Chain.Fits(current.Plan, current.Skip, out seeThrough, out opaque);
            this.install.Text = (car != null && car.Fm2Id > 0) ? "Port Again Over Car " + car.Fm2Id.ToString(CultureInfo.InvariantCulture) + "..."
                : (car != null && car.Fm2Own) ? "Install In FM2 As " + car.TargetMedia + "..." : "Install In FM2...";
        }

        // The MediaName of a copy of one of FM2's own cars: the source's + _FM4 / _FM3 / _FH (up to 40 characters), as
        // the FM4 window names a car FM4 already has.
        private static string RenamedPort(string media, ForzaGame game)
        {
            string tag = (game == ForzaGame.Motorsport4) ? "_FM4" : (game == ForzaGame.Motorsport3) ? "_FM3" : "_FH";
            return ((media.Length + tag.Length > 40) ? media.Substring(0, 40 - tag.Length) : media) + tag;
        }

        // The first time a car ported before is chosen: the template and the engine sound of its port record go into the
        // window (the variants and materials follow in the trial run). The user can change them after.
        private void ApplyRecord()
        {
            SourceCar car = this.SelectedCar;
            if (car == null || car.Record == null || car.RecordApplied)
            {
                return;
            }
            car.RecordApplied = true;
            if (car.Record.MakeId > 0)
            {
                car.MakeId = car.Record.MakeId;      // the Make chosen in the first port
                this.ShowMake();
            }
            int template = this.templateList.FindIndex(delegate(string[] one) { return string.Equals(one[0], car.Record.Template, StringComparison.OrdinalIgnoreCase); });
            if (template >= 0)
            {
                this.templates.SelectedIndex = template;
            }
            if (!string.IsNullOrEmpty(car.Record.UseSoundOf))
            {
                int sound = this.templateList.FindIndex(delegate(string[] one) { return string.Equals(one[0], car.Record.UseSoundOf, StringComparison.OrdinalIgnoreCase); });
                if (sound >= 0)
                {
                    this.useSound.Checked = true;
                    this.soundCars.SelectedIndex = sound;
                    this.soundPicked = true;
                }
            }
            else
            {
                this.portSound.Checked = true;
            }
            this.status.Text = car.Name + ": ported before on the template " + car.Record.Template + "; Trial Run gives back the variants and materials you chose then.";
        }

        // ----- Review: what the trial run (with the database dry run) or the install found worth a look -----

        private TabPage BuildReviewPage(out Label title, out DataGridView grid)
        {
            TabPage page = new TabPage("Review");
            title = new Label();
            title.Dock = DockStyle.Top;
            title.Height = 26;
            title.Font = ForzaTheme.Bold;
            title.Padding = new Padding(6, 6, 0, 0);
            title.Text = "Run a trial on a car: what the port into FM2 cannot do, does by itself or adds to FM2 is listed here.";
            DataGridView view = new DataGridView();
            grid = view;
            view.Dock = DockStyle.Fill;
            view.AllowUserToAddRows = false;
            view.AllowUserToDeleteRows = false;
            view.AllowUserToResizeRows = false;
            view.RowHeadersVisible = false;
            view.SelectionMode = DataGridViewSelectionMode.CellSelect;
            view.EditMode = DataGridViewEditMode.EditOnEnter;
            view.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            DataGridViewTextBoxColumn car = new DataGridViewTextBoxColumn();
            car.HeaderText = "Car";
            car.ReadOnly = true;
            car.FillWeight = 14;
            DataGridViewTextBoxColumn where = new DataGridViewTextBoxColumn();
            where.HeaderText = "Where";
            where.ReadOnly = true;
            where.FillWeight = 12;
            DataGridViewTextBoxColumn what = new DataGridViewTextBoxColumn();
            what.HeaderText = "What";
            what.ReadOnly = true;
            what.FillWeight = 56;
            DataGridViewComboBoxColumn fix = new DataGridViewComboBoxColumn();
            fix.HeaderText = "Fix (your choice)";
            fix.FillWeight = 18;
            fix.FlatStyle = FlatStyle.Flat;
            view.Columns.Add(car);
            view.Columns.Add(where);
            view.Columns.Add(what);
            view.Columns.Add(fix);
            view.CurrentCellDirtyStateChanged += delegate
            {
                if (view.IsCurrentCellDirty)
                {
                    view.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            view.CellValueChanged += delegate(object sender, DataGridViewCellEventArgs e) { this.ReviewFixChanged(e.RowIndex); };
            view.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };
            // the material choices FM2's own cars give the same parts are green, as in the FM3 / FM4 windows
            view.CellFormatting += delegate(object sender, DataGridViewCellFormattingEventArgs e)
            {
                if (e.ColumnIndex == 3 && e.RowIndex >= 0 && Recommended(view.Rows[e.RowIndex], e.Value as string))
                {
                    e.CellStyle.ForeColor = ForzaTheme.Green;
                    e.CellStyle.SelectionForeColor = ForzaTheme.Green;
                }
            };
            view.EditingControlShowing += delegate(object sender, DataGridViewEditingControlShowingEventArgs e)
            {
                ComboBox combo = e.Control as ComboBox;
                if (combo != null && combo.Tag as string != "green")
                {
                    combo.Tag = "green";   // the grid reuses its editing control
                    ForzaTheme.GreenItems(combo, delegate(object item) { return view.CurrentRow != null && Recommended(view.CurrentRow, item as string); });
                }
            };
            view.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                ReviewRow row = (e.RowIndex >= 0) ? view.Rows[e.RowIndex].Tag as ReviewRow : null;
                if (row != null && row.Variant)
                {
                    this.tabs.SelectedTab = this.variantsPage;
                }
                else if (row != null && row.Material != null && e.ColumnIndex != 3)
                {
                    this.tabs.SelectedTab = this.materialsPage;
                }
            };
            page.Controls.Add(view);
            page.Controls.Add(title);
            page.VisibleChanged += delegate { view.PerformLayout(); view.Invalidate(true); };
            return page;
        }

        // The materials FM2's own cars give the parts a material sits on (MaterialSuggestions.ForGame, green).
        private static List<string> RecommendedMaterials(MaterialRow row)
        {
            List<string> names = new List<string>();
            string list = Fm4MaterialUse.ListName(row.Wheel, row.Wheel ? "wheel.carbin" : "car.carbin", 1);
            foreach (MaterialSuggestions.Entry entry in MaterialSuggestions.ForGame(ShaderProfile.Fm2, row.Material, "car.carbin", list, row.Parts))
            {
                if (entry.Group == MaterialSuggestions.Group.Recommended)
                {
                    names.Add(entry.Name);
                }
            }
            return names;
        }

        private static bool Recommended(DataGridViewRow gridRow, string choice)
        {
            List<string> green = gridRow.Cells[3].Tag as List<string>;
            return green != null && choice != null && green.Contains(choice);
        }

        // The port's log lines worth a look, sorted out by what they say (CarPortToFm2, CarDataToFm2, Fm2LookupPort, the chain).
        private static List<ReviewRow> ReviewFromLog(IEnumerable<string> lines)
        {
            List<ReviewRow> rows = new List<ReviewRow>();
            foreach (string raw in lines)
            {
                string line = (raw ?? "").Trim();
                if (line.Length == 0)
                {
                    continue;
                }
                string where = line.StartsWith("car model:", StringComparison.Ordinal) ? "car model"
                    : line.StartsWith("wheel model:", StringComparison.Ordinal) ? "wheel model"
                    : line.StartsWith("MAXData.xml:", StringComparison.Ordinal) ? "collision"
                    : line.StartsWith("shader settings:", StringComparison.Ordinal) ? "shader settings"
                    : line.StartsWith("car texture", StringComparison.Ordinal) || line.StartsWith("wheel texture", StringComparison.Ordinal) ? "textures"
                    : line.StartsWith("LOOKUP", StringComparison.Ordinal) || line.IndexOf("FM2 lacked it", StringComparison.Ordinal) >= 0 ? "added to FM2"
                    : line.StartsWith("not ported:", StringComparison.Ordinal) ? "database"
                    : line.StartsWith("record:", StringComparison.Ordinal) ? "port record"
                    : line.StartsWith("== step", StringComparison.Ordinal) ? "install" : "";
                string what = line;
                foreach (string prefix in new[] { "car model: ", "wheel model: ", "MAXData.xml: ", "LOOKUP ", "not ported: ", "record: " })
                {
                    if (what.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        what = what.Substring(prefix.Length);
                        break;
                    }
                }
                if (line.IndexOf("FAILED", StringComparison.Ordinal) >= 0 || line.IndexOf("REFUSED", StringComparison.Ordinal) >= 0 || line.StartsWith("ERROR", StringComparison.Ordinal))
                {
                    rows.Add(new ReviewRow(where, what, ReviewKind.Error));
                }
                else if (line.StartsWith("not ported:", StringComparison.Ordinal) || line.IndexOf("the FM2 car's stays", StringComparison.Ordinal) >= 0 || line.IndexOf("NOT written", StringComparison.Ordinal) >= 0)
                {
                    rows.Add(new ReviewRow(where, what, ReviewKind.Warning));
                }
                else if (line.IndexOf("FM2 lacked it", StringComparison.Ordinal) >= 0)
                {
                    rows.Add(new ReviewRow(where, what, ReviewKind.Added));
                }
                else if (line.IndexOf("FIXED AUTOMATICALLY", StringComparison.Ordinal) >= 0 || line.IndexOf("number plate removed", StringComparison.Ordinal) >= 0
                    || line.IndexOf("UVs moved", StringComparison.Ordinal) >= 0)
                {
                    rows.Add(new ReviewRow(where, what, ReviewKind.Fixed));
                }
                else if (line.IndexOf("left out", StringComparison.Ordinal) >= 0 && where != "" || line.IndexOf("removed:", StringComparison.Ordinal) >= 0)
                {
                    rows.Add(new ReviewRow(where, what, ReviewKind.Info));
                }
            }
            return rows;
        }

        // The selected car's review: its trial's (materials and variants to choose, the database dry run) or its install's.
        private void ShowReview()
        {
            SourceCar car = this.SelectedCar;
            Trial current = this.CurrentTrial;
            this.reviewFilling = true;
            this.review.Rows.Clear();
            List<ReviewRow> rows = new List<ReviewRow>();
            List<ReviewRow> installed;
            if (current != null)
            {
                foreach (MaterialRow material in UnknownMaterials(current))
                {
                    string chosen;
                    bool has = (material.Wheel ? current.WheelMaterials : current.CarMaterials).TryGetValue(material.Material, out chosen);
                    ReviewRow row = new ReviewRow(material.Wheel ? "wheel material" : "car material", material.Material + " (no FM2 car uses it; on " + string.Join(", ", material.Parts.ToArray()) + ")"
                        + (has ? "" : ": left as it is, it may draw black in game"), has ? ReviewKind.Info : ReviewKind.Warning);
                    row.Material = material;
                    rows.Add(row);
                }
                foreach (Fm2CarbinModel.PlanPart part in current.Plan)
                {
                    if (part.Variant && current.Skip.Contains(part.Name))
                    {
                        ReviewRow row = new ReviewRow("variant", part.Name + " left out (your choice, " + part.Vertices.ToString("N0", CultureInfo.InvariantCulture) + " vertices); double-click: Variants", ReviewKind.Info);
                        row.Variant = true;
                        rows.Add(row);
                    }
                }
                rows.AddRange(current.Review);
                if (car != null && this.installReviews.TryGetValue(car.MediaName, out installed))
                {
                    // a failed install after this trial: its errors stay on top until the next trial
                    rows.AddRange(installed.FindAll(delegate(ReviewRow r) { return r.Kind == ReviewKind.Error; }));
                }
            }
            else if (car != null && this.installReviews.TryGetValue(car.MediaName, out installed))
            {
                rows.AddRange(installed);
            }
            rows.Sort(delegate(ReviewRow a, ReviewRow b) { return ((int) b.Kind).CompareTo((int) a.Kind); });
            int warnings = 0;
            foreach (ReviewRow row in rows)
            {
                int index = this.review.Rows.Add(car.Name, row.Where, row.What);
                DataGridViewRow gridRow = this.review.Rows[index];
                gridRow.Tag = row;
                DataGridViewComboBoxCell cell = (DataGridViewComboBoxCell) gridRow.Cells[3];
                if (row.Material != null)
                {
                    cell.Items.Add(LeaveAsIs);
                    foreach (string choice in CarPortToFm2.Fm2Materials(row.Material.Wheel))
                    {
                        cell.Items.Add(choice);
                    }
                    string chosen;
                    cell.Value = ((row.Material.Wheel ? current.WheelMaterials : current.CarMaterials).TryGetValue(row.Material.Material, out chosen) && cell.Items.Contains(chosen)) ? chosen : LeaveAsIs;
                    cell.Tag = RecommendedMaterials(row.Material);
                }
                else
                {
                    cell.ReadOnly = true;
                    cell.DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing;
                }
                Color color = (row.Kind == ReviewKind.Error) ? ForzaTheme.Red : (row.Kind == ReviewKind.Warning) ? ForzaTheme.Yellow
                    : (row.Kind == ReviewKind.Added) ? ForzaTheme.Blue : (row.Kind == ReviewKind.Fixed) ? ForzaTheme.Green : ForzaTheme.Text;
                for (int c = 0; c < 3; c++)
                {
                    gridRow.Cells[c].Style.ForeColor = color;
                }
                warnings += (row.Kind == ReviewKind.Error || row.Kind == ReviewKind.Warning) ? 1 : 0;
            }
            this.reviewTitle.Text = (car == null) ? "Run a trial on a car: what the port into FM2 cannot do, does by itself or adds to FM2 is listed here."
                : (rows.Count == 0) ? car.Name + ": " + ((current != null || this.installReviews.ContainsKey(car.MediaName)) ? "nothing to look at." : "run a trial with the template chosen.")
                : car.Name + ": " + rows.Count + " line(s), " + warnings + " to look at (yellow / red). Blue: added to FM2; green: fixed by the port. Fix: the same choice as the Materials tab.";
            this.reviewPage.Text = (warnings > 0) ? "Review (" + warnings + ")" : "Review";
            this.reviewFilling = false;
        }

        // A Fix chosen in the Review: the same choice in the Materials grid (which keeps it with the trial and the counters).
        private void ReviewFixChanged(int index)
        {
            if (this.reviewFilling || index < 0 || index >= this.review.Rows.Count)
            {
                return;
            }
            ReviewRow row = this.review.Rows[index].Tag as ReviewRow;
            string chosen = this.review.Rows[index].Cells[3].Value as string;
            if (row == null || row.Material == null || chosen == null)
            {
                return;
            }
            foreach (DataGridViewRow gridRow in this.materials.Rows)
            {
                MaterialRow material = gridRow.Tag as MaterialRow;
                if (material != null && material.Wheel == row.Material.Wheel && material.Material == row.Material.Material)
                {
                    gridRow.Cells[3].Value = chosen;     // CellValueChanged: MaterialsChanged, then the review again
                }
            }
        }

        // The database step on a copy of FM2's database in memory (nothing written): the rows FM2 cannot take, the makes,
        // part makers and cylinders it would get, before Install (user 2026-11-26). A new car is converted over its
        // template's rows, a port again over its own.
        private static List<string> DryRunData(string sourceRoot, string sourceBase, string sourceMedia, string target, string onCar, List<Fm2CarbinModel.PlanPart> plan, ICollection<string> skip, List<CarDataToFm2.Fm2Swap> swaps, List<string[]> upgrades, long makeOverride)
        {
            List<string> lines = new List<string>();
            using (SqliteDatabase db = SqliteDatabase.FromBytes(File.ReadAllBytes(CarPortFm2Chain.FindDatabase(target))))
            using (SqliteDatabase source = SqliteDatabase.FromBytes(File.ReadAllBytes(CarPortFm2Chain.FindDatabase(sourceRoot))))
            {
                object id = db.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", onCar);
                object sourceId = source.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", sourceMedia);
                string zip = GamePaths.FindCar(target, onCar);
                if (id == null || sourceId == null || zip == null)
                {
                    lines.Add("database dry run: not done (" + ((id == null || zip == null) ? "FM2 has no car " + onCar : "the source has no row of " + sourceMedia) + ")");
                    return lines;
                }
                ForzaZip carZip = new ForzaZip(zip);
                Fm2CarbinModel model = null;
                foreach (ForzaZip.Entry entry in carZip.Entries)
                {
                    if (entry.Name.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase))
                    {
                        model = new Fm2CarbinModel(carZip.Read(entry));
                        break;
                    }
                }
                HashSet<string> parts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Fm2CarbinModel.PlanPart part in plan)
                {
                    if (!part.Variant || skip == null || !skip.Contains(part.Name))
                    {
                        parts.Add(part.Name);
                    }
                }
                Fm2LookupPort lookups = new Fm2LookupPort();
                lookups.SourceRoot = sourceRoot;
                lookups.SourceFallbackRoot = sourceBase;
                lookups.TargetRoot = target;
                lookups.BackupFolder = null;
                lookups.Begin(source, db);
                CarDataToFm2 data = new CarDataToFm2();
                try
                {
                    if (makeOverride <= 0)
                    {
                        lookups.PrepareMake(Fm2LookupPort.SourceMake(source, sourceMedia));   // a chosen Make: the source's is not added
                    }
                    data.IdResolver = lookups.Resolve;
                    data.Target = db;
                    data.Source = source;
                    data.Car = Convert.ToInt64(id, CultureInfo.InvariantCulture);
                    data.SourceCar = Convert.ToInt64(sourceId, CultureInfo.InvariantCulture);
                    data.ModelParts = parts;
                    data.Model = model;
                    data.Swaps = swaps;
                    data.MakeOverride = makeOverride;
                    data.Run();
                }
                catch (Exception e)
                {
                    lines.Add("database dry run FAILED: " + e.Message);
                }
                lines.AddRange(data.Log);
                lines.AddRange(lookups.Log);
                if (upgrades != null)
                {
                    UpgradeTable(db, source, data.Car, data.SourceCar, upgrades);
                }
            }
            return lines;
        }

        // The Upgrades tab's table: per upgrade table and level, the source's row and FM2's (after the dry run), with a note.
        // The car's tables by Ordinal, the engine part tables by the stock engine of each game.
        private static void UpgradeTable(SqliteDatabase db, SqliteDatabase source, long car, long sourceCar, List<string[]> into)
        {
            long engine = CarEngine.StockEngine(db, car);
            long sourceEngine = CarEngine.StockEngine(source, sourceCar);
            List<string> tables = new List<string>();
            foreach (object[] r in db.Query("SELECT name FROM sqlite_master WHERE type = 'table' AND name LIKE 'List_Upgrade%' ORDER BY name").Rows)
            {
                tables.Add(Convert.ToString(r[0], CultureInfo.InvariantCulture));
            }
            foreach (string table in tables)
            {
                List<string> columns = new List<string>(db.Query("SELECT * FROM " + SqliteDatabase.Quote(table) + " LIMIT 0").Columns);
                string key = columns.Contains("Ordinal") ? "Ordinal" : columns.Contains("EngineID") ? "EngineID" : null;
                if (key == null || !columns.Contains("Level"))
                {
                    continue;
                }
                long fm2Key = (key == "Ordinal") ? car : engine;
                long sourceKey = (key == "Ordinal") ? sourceCar : sourceEngine;
                // the source's rows: the tables the port converts this one from (FM4's front / rear anti-roll bars, drivetrain
                // parts, camshaft...), else the source's table of the same name (List_UpgradeEngine: the swaps)
                List<Dictionary<string, object>> sourceRows = CarDataToFm2.SourceUpgradeRows(source, table, sourceCar);
                bool hasSource = sourceRows != null || DbIds.HasTable(source, table);
                SortedDictionary<long, string> fm2 = UpgradeLevels(db, table, key, fm2Key, columns, true);
                SortedDictionary<long, string> src;
                if (sourceRows != null)
                {
                    src = LevelsOf(source, sourceRows);
                }
                else
                {
                    List<string> sourceColumns = hasSource ? new List<string>(source.Query("SELECT * FROM " + SqliteDatabase.Quote(table) + " LIMIT 0").Columns) : new List<string>();
                    src = (hasSource && sourceColumns.Contains(key) && sourceColumns.Contains("Level")) ? UpgradeLevels(source, table, key, sourceKey, sourceColumns, false) : new SortedDictionary<long, string>();
                }
                // the games number some parts differently (FM2's rim size and tyre width start at level 1): the source's levels
                // are lined up on FM2's by the two stock levels
                long fm2Stock = columns.Contains("IsStock") ? StockLevel(db.Scalar("SELECT Level FROM " + SqliteDatabase.Quote(table) + " WHERE " + key + " = ? AND IsStock = 1 ORDER BY Level LIMIT 1", fm2Key)) : long.MinValue;
                long sourceStock = long.MinValue;
                if (sourceRows != null)
                {
                    Dictionary<string, object> stockRow = sourceRows.Find(delegate(Dictionary<string, object> r) { object s; return r.TryGetValue("IsStock", out s) && Convert.ToString(s, CultureInfo.InvariantCulture) == "1"; });
                    sourceStock = (stockRow != null) ? StockLevel(stockRow["Level"]) : long.MinValue;
                }
                else if (hasSource && table == "List_UpgradeEngine")
                {
                    sourceStock = StockLevel(source.Scalar("SELECT Level FROM List_UpgradeEngine WHERE Ordinal = ? AND IsStock = 1 LIMIT 1", sourceCar));
                }
                long offset = (fm2Stock != long.MinValue && sourceStock != long.MinValue) ? fm2Stock - sourceStock : 0;
                SortedDictionary<long, string> lined = new SortedDictionary<long, string>();
                foreach (KeyValuePair<long, string> level in src)
                {
                    lined[level.Key + offset] = level.Value;
                }
                SortedSet<long> levels = new SortedSet<long>(fm2.Keys);
                levels.UnionWith(lined.Keys);
                string part = table.Substring("List_Upgrade".Length);
                foreach (long level in levels)
                {
                    string a, b;
                    bool inSource = lined.TryGetValue(level, out a);
                    bool inFm2 = fm2.TryGetValue(level, out b);
                    string note = !inFm2 ? "not ported (FM2 has no such level / part)" : !inSource ? (hasSource ? "FM2's (the source has none)" : "FM2 only table") : "";
                    if (table == "List_UpgradeEngine" && level != fm2Stock)
                    {
                        note = inFm2 ? "engine swap" + (inSource ? "" : " (yours)") : note;
                    }
                    string name = (level == fm2Stock) ? "stock" : "L" + level.ToString(CultureInfo.InvariantCulture);
                    if (offset != 0 && inSource)
                    {
                        name += " (source L" + (level - offset).ToString(CultureInfo.InvariantCulture) + ")";
                    }
                    into.Add(new[] { part, name, inSource ? a : "-", inFm2 ? b : "-", note });
                }
            }
        }

        private static long StockLevel(object level)
        {
            return (level == null || level is DBNull) ? long.MinValue : Convert.ToInt64(level, CultureInfo.InvariantCulture);
        }

        // level -> "maker N, price P, mass M" of source rows already read (front / rear sides: both, " / ")
        private static SortedDictionary<long, string> LevelsOf(SqliteDatabase db, List<Dictionary<string, object>> rows)
        {
            SortedDictionary<long, string> levels = new SortedDictionary<long, string>();
            foreach (Dictionary<string, object> row in rows)
            {
                object level;
                if (!row.TryGetValue("Level", out level) || level == null || level is DBNull)
                {
                    continue;
                }
                List<string> parts = new List<string>();
                object value;
                if ((row.TryGetValue("ManufacturerID", out value) || row.TryGetValue("ManufacturerId", out value)) && value != null && !(value is DBNull) && Convert.ToString(value, CultureInfo.InvariantCulture) != "0")
                {
                    parts.Add("maker " + value);
                }
                if (row.TryGetValue("Price", out value) && value != null && !(value is DBNull))
                {
                    parts.Add("price " + Convert.ToString(value, CultureInfo.InvariantCulture));
                }
                if (row.TryGetValue("MassDiff", out value) && value != null && !(value is DBNull) && Convert.ToDouble(value, CultureInfo.InvariantCulture) != 0)
                {
                    parts.Add("mass " + Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("0.#", CultureInfo.InvariantCulture));
                }
                long key = Convert.ToInt64(level, CultureInfo.InvariantCulture);
                string text = (parts.Count > 0) ? string.Join(", ", parts.ToArray()) : "yes";
                string had;
                levels[key] = levels.TryGetValue(key, out had) ? (had == text ? had : had + " / " + text) : text;
            }
            return levels;
        }

        // level -> "maker N, price P, mass M" (FM2: the maker's name) for one car / engine of an upgrade table
        private static SortedDictionary<long, string> UpgradeLevels(SqliteDatabase db, string table, string key, long value, List<string> columns, bool fm2)
        {
            SortedDictionary<long, string> levels = new SortedDictionary<long, string>();
            string maker = columns.Contains("ManufacturerID") ? "ManufacturerID" : columns.Contains("ManufacturerId") ? "ManufacturerId" : null;
            string sql = "SELECT Level, " + (maker ?? "NULL") + ", " + (columns.Contains("Price") ? "Price" : "NULL") + ", " + (columns.Contains("MassDiff") ? "MassDiff" : "NULL")
                + (columns.Contains("EngineID") && key != "EngineID" ? ", EngineID" : ", NULL") + " FROM " + SqliteDatabase.Quote(table) + " WHERE " + key + " = ? ORDER BY Level";
            foreach (object[] r in db.Query(sql, value).Rows)
            {
                long level = Convert.ToInt64(r[0], CultureInfo.InvariantCulture);
                List<string> parts = new List<string>();
                if (r[4] != null && !(r[4] is DBNull))
                {
                    parts.Add("engine " + r[4] + " " + db.Scalar("SELECT MediaName FROM Data_Engine WHERE EngineID = ?", r[4]));
                }
                if (r[1] != null && !(r[1] is DBNull) && Convert.ToString(r[1], CultureInfo.InvariantCulture) != "0")
                {
                    object name = (fm2 && table != "List_UpgradeEngine") ? db.Scalar("SELECT PartManufacturer FROM List_PartManufacturer WHERE Id = ?", r[1]) : null;
                    parts.Add("maker " + r[1] + ((name != null) ? " " + name : ""));
                }
                if (r[2] != null && !(r[2] is DBNull))
                {
                    parts.Add("price " + Convert.ToString(r[2], CultureInfo.InvariantCulture));
                }
                if (r[3] != null && !(r[3] is DBNull) && Convert.ToDouble(r[3], CultureInfo.InvariantCulture) != 0)
                {
                    parts.Add("mass " + Convert.ToDouble(r[3], CultureInfo.InvariantCulture).ToString("0.#", CultureInfo.InvariantCulture));
                }
                string text = (parts.Count > 0) ? string.Join(", ", parts.ToArray()) : "yes";
                string had;
                levels[level] = levels.TryGetValue(level, out had) ? had + " / " + text : text;
            }
            return levels;
        }

        // ----- Variants -----

        // The grid and the counters of the selected car's trial (empty without one).
        private void ShowTrial()
        {
            Trial current = this.CurrentTrial;
            this.filling = true;
            this.variants.Rows.Clear();
            if (current != null)
            {
                foreach (Fm2CarbinModel.PlanPart part in current.Plan)
                {
                    if (part.Variant)
                    {
                        int index = this.variants.Rows.Add(!current.Skip.Contains(part.Name), part.Name, part.Vertices.ToString("N0", CultureInfo.InvariantCulture), part.SeeThrough ? "see-through" : "opaque");
                        this.variants.Rows[index].Tag = part;
                    }
                }
            }
            this.materials.Rows.Clear();
            SourceCar car = this.SelectedCar;
            int rows = 0;
            if (current != null)
            {
                List<string> carChoices = CarPortToFm2.Fm2Materials(false);
                List<string> wheelChoices = CarPortToFm2.Fm2Materials(true);
                foreach (MaterialRow row in UnknownMaterials(current))
                {
                    int index = this.materials.Rows.Add(row.Wheel ? "wheel" : "car", row.Material, string.Join(", ", row.Parts.ToArray()));
                    DataGridViewComboBoxCell cell = (DataGridViewComboBoxCell) this.materials.Rows[index].Cells[3];
                    cell.Items.Add(LeaveAsIs);
                    foreach (string choice in row.Wheel ? wheelChoices : carChoices)
                    {
                        cell.Items.Add(choice);
                    }
                    string chosen;
                    cell.Value = ((row.Wheel ? current.WheelMaterials : current.CarMaterials).TryGetValue(row.Material, out chosen) && cell.Items.Contains(chosen)) ? chosen : LeaveAsIs;
                    this.materials.Rows[index].Tag = row;
                    rows++;
                }
            }
            this.materialsTitle.Text = (current == null) ? ((car == null) ? "Run a trial on a car: the materials FM2 does not use are listed here." : car.Name + ": run a trial with the template chosen to list the materials FM2 does not use.")
                : (rows == 0) ? car.Name + ": FM2's own cars use every material of this car. Nothing to choose."
                : car.Name + ": " + rows + " material" + ((rows == 1) ? "" : "s") + " no FM2 car uses (they may draw black in game). Choose an FM2 material, or leave it: your choice.";
            this.filling = false;
            this.ShowCounters();
            this.ShowReview();
            this.ShowUpgrades();
        }

        // A choice in the Materials grid: kept with the car's trial. A see-through material moves its parts to the other
        // buffer, so the counters and the Variants grid follow at once.
        private void MaterialsChanged()
        {
            Trial current = this.CurrentTrial;
            if (this.filling || current == null)
            {
                return;
            }
            current.CarMaterials.Clear();
            current.WheelMaterials.Clear();
            foreach (DataGridViewRow gridRow in this.materials.Rows)
            {
                MaterialRow row = gridRow.Tag as MaterialRow;
                string chosen = gridRow.Cells[3].Value as string;
                if (row != null && !string.IsNullOrEmpty(chosen) && chosen != LeaveAsIs)
                {
                    (row.Wheel ? current.WheelMaterials : current.CarMaterials)[row.Material] = chosen;
                }
            }
            foreach (Fm2CarbinModel.PlanPart part in current.Plan)
            {
                part.Choose(current.CarMaterials);
            }
            this.filling = true;
            foreach (DataGridViewRow gridRow in this.variants.Rows)
            {
                Fm2CarbinModel.PlanPart part = gridRow.Tag as Fm2CarbinModel.PlanPart;
                if (part != null)
                {
                    gridRow.Cells[3].Value = part.SeeThrough ? "see-through" : "opaque";
                }
            }
            this.filling = false;
            this.ShowCounters();
            this.UpdateButtons();
            // after the grid event that got here (a Review fix lands in this grid from the Review's own event)
            this.BeginInvoke((MethodInvoker) this.ShowReview);
        }

        private void VariantsChanged()
        {
            Trial current = this.CurrentTrial;
            if (this.filling || current == null)
            {
                return;
            }
            current.Skip.Clear();
            foreach (DataGridViewRow row in this.variants.Rows)
            {
                Fm2CarbinModel.PlanPart part = row.Tag as Fm2CarbinModel.PlanPart;
                if (part != null && !Convert.ToBoolean(row.Cells[0].Value, CultureInfo.InvariantCulture))
                {
                    current.Skip.Add(part.Name);
                }
            }
            this.ShowCounters();
            this.UpdateButtons();
        }

        private void ShowCounters()
        {
            Trial current = this.CurrentTrial;
            SourceCar car = this.SelectedCar;
            if (current == null)
            {
                this.variantsTitle.Text = (car == null) ? "Run a trial on a car: its upgrade variants are listed here." : car.Name + ": run a trial with the template chosen to list its upgrade variants.";
                this.opaqueCount.Text = "";
                this.seeThroughCount.Text = "";
                return;
            }
            int seeThrough, opaque;
            bool fits = CarPortFm2Chain.Fits(current.Plan, current.Skip, out seeThrough, out opaque);
            int rows = this.variants.Rows.Count;
            this.variantsTitle.Text = car.Name + ((rows == 0) ? ": no upgrade variants, every part is ported."
                : fits ? ": tick the upgrade variants to port (" + (rows - current.Skip.Count) + " of " + rows + "). The stock parts always go."
                : ": too many vertices for FM2. Untick variants until both counters are within 65,536.");
            this.opaqueCount.Text = string.Format(CultureInfo.InvariantCulture, "Opaque buffer: {0:N0} / {1:N0}", opaque, BufferSize);
            this.opaqueCount.ForeColor = (opaque > BufferSize) ? ForzaTheme.Red : ForzaTheme.Green;
            this.seeThroughCount.Text = string.Format(CultureInfo.InvariantCulture, "See-through buffer: {0:N0} / {1:N0}", seeThrough, BufferSize);
            this.seeThroughCount.ForeColor = (seeThrough > BufferSize) ? ForzaTheme.Red : ForzaTheme.Green;
        }

        // ----- Trial run and install -----

        // The folder the car is read from: the game folder, or its DLC package laid out with the merged database.
        private static string SourceRootOf(SourceCar car, string source, List<string> log)
        {
            return (car.Pack == null) ? source : CarPortFm2Chain.StageDlc(car.Pack, car.Root, source, car.MediaName, log);
        }

        private void RunTrial()
        {
            SourceCar car = this.SelectedCar;
            string template = this.SelectedTemplate;
            string source = this.loadedSource;
            string target = this.loadedTarget;
            if (car == null || template == null)
            {
                return;
            }
            // the material choices made for this car before are kept and used by this run
            Dictionary<string, string> carMaterials = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, string> wheelMaterials = new Dictionary<string, string>(StringComparer.Ordinal);
            Trial before;
            if (this.trials.TryGetValue(car.MediaName, out before))
            {
                foreach (KeyValuePair<string, string> pair in before.CarMaterials)
                {
                    carMaterials[pair.Key] = pair.Value;
                }
                foreach (KeyValuePair<string, string> pair in before.WheelMaterials)
                {
                    wheelMaterials[pair.Key] = pair.Value;
                }
            }
            // a car ported before, first trial on its port's template: the choices of that port come back
            // (a re-port keeps the original choices, user 2026-11-26)
            Fm2PortRecord record = (before == null && car.Record != null && string.Equals(car.Record.Template, template, StringComparison.OrdinalIgnoreCase)) ? car.Record : null;
            if (record != null)
            {
                foreach (KeyValuePair<string, string> pair in record.CarMaterials)
                {
                    carMaterials[pair.Key] = pair.Value;
                }
                foreach (KeyValuePair<string, string> pair in record.WheelMaterials)
                {
                    wheelMaterials[pair.Key] = pair.Value;
                }
            }
            this.status.Text = "Trial run of " + car.Name + "...";
            this.Background(delegate
            {
                List<string> log = new List<string>();
                CarPortToFm2 port = new CarPortToFm2();
                port.SourceRoot = SourceRootOf(car, source, log);
                port.SourceMediaName = car.MediaName;
                port.TargetRoot = target;
                port.TemplateMediaName = template;
                port.CarMaterials = carMaterials;
                port.WheelMaterials = wheelMaterials;
                List<Fm2CarbinModel.PlanPart> plan = port.Plan();
                List<Fm2CarbinModel.PlanPart> wheelPlan = null;
                try
                {
                    wheelPlan = port.PlanWheel();
                    if (wheelPlan == null)
                    {
                        log.Add("Wheel: the stock wheel of the source or of the template is not in the folders, the FM2 wheel would stay.");
                    }
                }
                catch (Exception e)
                {
                    log.Add("Wheel: not read, " + e.Message);
                }

                // the copy of the template, as Duplicate Car plans it (nothing written)
                List<string> copyPlan = new List<string>();
                if (car.Fm2Id > 0)
                {
                    copyPlan.Add("none: Install ports again over " + car.TargetMedia + ", car " + car.Fm2Id.ToString(CultureInfo.InvariantCulture)
                        + " of FM2 (car, upgrade and engine ids kept; the model is built again on " + template + ")");
                    if (car.Record == null)
                    {
                        copyPlan.Add("the car has no port record (ported before records were kept): the variants and materials of its first port are not known, choose them again");
                    }
                }
                else
                try
                {
                    CarDuplicate copy = new CarDuplicate();
                    copy.TargetRoot = target;
                    copy.SourceMediaName = template;
                    copy.NewMediaName = car.TargetMedia;
                    copy.XeniaContent = InstalledDlc.ContentFolder(ForzaGame.Motorsport2);
                    copy.DisplayName = car.Name;
                    copy.ModelShort = car.Short;
                    copy.Year = car.Year;
                    copyPlan.AddRange(copy.Plan());
                }
                catch (Exception e)
                {
                    copyPlan.Add("REFUSED: " + e.Message);
                }

                // every variant starts ticked: which ones to leave out is the user's choice (user 2026-11-22), never the tool's
                Trial made = new Trial();
                made.Template = template;
                made.Plan = plan;
                made.Wheel = wheelPlan;
                made.CarMaterials = carMaterials;
                made.WheelMaterials = wheelMaterials;
                if (record != null)
                {
                    foreach (Fm2CarbinModel.PlanPart part in plan)
                    {
                        if (part.Variant && record.Skip.Contains(part.Name))
                        {
                            made.Skip.Add(part.Name);
                        }
                    }
                    log.Add("Choices of the first port (its record): " + ((made.Skip.Count > 0) ? "variants left out " + string.Join(", ", new List<string>(made.Skip).ToArray()) : "no variant left out")
                        + "; " + (record.CarMaterials.Count + record.WheelMaterials.Count) + " material choice(s).");
                }
                // the database step in memory, nothing written: what FM2 cannot take and what it gets, shown before Install
                // the engine swaps (Upgrades tab): the trial before's, the record's or the source's
                try
                {
                    using (SqliteDatabase sourceDb = SqliteDatabase.FromBytes(File.ReadAllBytes(CarPortFm2Chain.FindDatabase(port.SourceRoot))))
                    {
                        object sourceId = sourceDb.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", car.MediaName);
                        if (sourceId != null)
                        {
                            made.Swaps = StartSwaps(sourceDb, Convert.ToInt64(sourceId, CultureInfo.InvariantCulture), before, (before == null) ? car.Record : null, template, out made.StockDrive);
                        }
                    }
                }
                catch (Exception e)
                {
                    log.Add("Engine swaps: not read, " + e.Message);
                }
                List<string> dryRun;
                try
                {
                    dryRun = DryRunData(port.SourceRoot, (car.Pack != null) ? source : null, car.MediaName, target, (car.Fm2Id > 0) ? car.TargetMedia : template, plan, made.Skip, ChosenSwaps(made), made.Upgrades, car.MakeId);
                }
                catch (Exception e)
                {
                    dryRun = new List<string>(new[] { "database dry run FAILED: " + e.Message });
                }
                made.Review = ReviewFromLog(dryRun);
                List<MaterialRow> unknown = UnknownMaterials(made);
                int[] stock = new int[2];
                int stockParts = 0, variantParts = 0;
                foreach (Fm2CarbinModel.PlanPart part in plan)
                {
                    stock[part.SeeThrough ? 0 : 1] += part.Variant ? 0 : part.Vertices;
                    stockParts += part.Variant ? 0 : 1;
                    variantParts += part.Variant ? 1 : 0;
                }
                bool stockFits = stock[0] <= BufferSize && stock[1] <= BufferSize;

                StringBuilder text = new StringBuilder();
                text.AppendLine("TRIAL RUN, nothing written: " + car.Name + " (" + car.MediaName + ") into FM2 on the template " + template);
                text.AppendLine();
                foreach (string line in log)
                {
                    text.AppendLine(line);
                }
                text.AppendLine(string.Format(CultureInfo.InvariantCulture, "Model: {0} stock parts, {1} upgrade variants. FM2 holds 65,536 vertices in each of the two buffers.", stockParts, variantParts));
                int allSee, allOpaque;
                bool allFit = CarPortFm2Chain.Fits(plan, null, out allSee, out allOpaque);
                text.AppendLine(string.Format(CultureInfo.InvariantCulture, "  every part: opaque {0:N0}, see-through {1:N0}", allOpaque, allSee));
                text.AppendLine(string.Format(CultureInfo.InvariantCulture, "  stock parts alone: opaque {0:N0}, see-through {1:N0}", stock[1], stock[0]));
                if (!stockFits)
                {
                    text.AppendLine("  THE STOCK PARTS ALONE DO NOT FIT: this car cannot be ported into FM2 as it is.");
                }
                else if (!allFit)
                {
                    text.AppendLine("  NOT EVERYTHING FITS: untick upgrade variants in the Variants tab until both counters are within 65,536.");
                }
                else
                {
                    text.AppendLine("  everything fits.");
                }
                text.AppendLine();
                text.AppendLine("Parts (vertices, buffer):");
                foreach (Fm2CarbinModel.PlanPart part in plan)
                {
                    text.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0,-22} {1,7:N0}  {2}{3}", part.Name, part.Vertices, part.SeeThrough ? "see-through" : "opaque", part.Variant ? "  (variant)" : ""));
                }
                text.AppendLine();
                if (unknown.Count == 0)
                {
                    text.AppendLine("Materials: FM2's own cars use every material of this car.");
                }
                else
                {
                    text.AppendLine("Materials no FM2 car uses (" + unknown.Count + "; they may draw black in game): choose in the Materials tab, or leave them.");
                    foreach (MaterialRow row in unknown)
                    {
                        string chosen;
                        bool has = (row.Wheel ? wheelMaterials : carMaterials).TryGetValue(row.Material, out chosen);
                        text.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0,-5} {1,-24} {2}   ({3})", row.Wheel ? "wheel" : "car", row.Material, has ? "-> " + chosen : "left as it is", string.Join(", ", row.Parts.ToArray())));
                    }
                }
                text.AppendLine();
                text.AppendLine("The copy of the FM2 template car:");
                foreach (string line in copyPlan)
                {
                    text.AppendLine("  " + line);
                }
                text.AppendLine();
                text.AppendLine("Database, dry run on a copy in memory (nothing written; Install does it again for real):");
                foreach (string line in dryRun)
                {
                    text.AppendLine("  " + line);
                }
                text.AppendLine();
                text.AppendLine("Install then converts the database rows and builds the engine sound from the source game's. See the Review tab.");
                return delegate
                {
                    this.trials[car.MediaName] = made;
                    this.installReviews.Remove(car.MediaName);
                    this.report.Text = text.ToString();
                    this.ShowTrial();
                    string materialNote = (unknown.Count > 0) ? " " + unknown.Count + " material" + ((unknown.Count == 1) ? "" : "s") + " no FM2 car uses: see the Materials tab." : "";
                    if (!allFit)
                    {
                        this.tabs.SelectedTab = this.variantsPage;
                    }
                    else if (unknown.Count > 0)
                    {
                        this.tabs.SelectedTab = this.materialsPage;
                    }
                    this.status.Text = !stockFits ? "Trial done: the stock parts alone do not fit FM2."
                        : !allFit ? "Trial done: not every variant fits. Untick variants in the Variants tab until the counters are green, then Install In FM2." + materialNote
                        : "Trial done: everything fits. Install In FM2 when ready." + materialNote;
                };
            });
        }

        private void Install()
        {
            SourceCar car = this.SelectedCar;
            string template = this.SelectedTemplate;
            Trial current = this.CurrentTrial;
            string source = this.loadedSource;
            string target = this.loadedTarget;
            if (car == null || template == null || current == null)
            {
                return;
            }
            string left = (current.Skip.Count > 0) ? "\n\nUpgrade variants left out: " + string.Join(", ", new List<string>(current.Skip).ToArray()) + "." : "";
            string useSoundOf = null;
            if (this.useSound.Checked)
            {
                if (this.soundCars.SelectedIndex < 0 || this.soundCars.SelectedIndex >= this.templateList.Count)
                {
                    MessageBox.Show(this, "Choose the FM2 car whose engine sound the car gets.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                useSoundOf = this.templateList[this.soundCars.SelectedIndex][0];
            }
            string soundLine = "\nEngine sound: " + ((useSoundOf == null) ? "ported from the source car" : "the FM2 sound of " + useSoundOf);
            soundLine += "\nMake: " + this.makes.Text;
            bool again = car.Fm2Id > 0;
            string question = again
                ? "Port " + car.Name + " again over FM2's car " + car.Fm2Id.ToString(CultureInfo.InvariantCulture) + " (" + car.TargetMedia + ")?\n\nFolder: " + target + "\nTemplate car: " + template + soundLine
                    + "\n\nThe car's model, wheel, textures, database rows and sound are written again; its car, upgrade and engine ids stay."
                    + "\n\nIf you own this car in an FM2 garage, the saved car can crash the game: sell it and buy it again."
                : "Install " + car.Name + " in Forza Motorsport 2" + (car.Fm2Own ? " as " + car.TargetMedia + " (FM2 has its own " + car.MediaName + ", never written over)" : "") + "?\n\nFolder: " + target + "\nTemplate car: " + template + soundLine
                    + "\n\nThe game's database, zip list and sound files are changed and the car's files are added.";
            if (MessageBox.Show(this, question + left, this.Text, MessageBoxButtons.YesNo, again ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            if (!XeniaGuard.Confirm(this, this.Text, "the Forza Motorsport 2 files"))
            {
                return;
            }
            List<string> skip = new List<string>(current.Skip);
            Dictionary<string, string> carMaterials = new Dictionary<string, string>(current.CarMaterials, StringComparer.Ordinal);
            Dictionary<string, string> wheelMaterials = new Dictionary<string, string>(current.WheelMaterials, StringComparer.Ordinal);
            List<CarDataToFm2.Fm2Swap> swaps = ChosenSwaps(current);
            long makeId = car.MakeId;
            this.status.Text = "Installing " + car.Name + "...";
            this.Background(delegate
            {
                CarPortFm2Chain chain = new CarPortFm2Chain();
                Exception failed = null;
                try
                {
                    chain.SourceRoot = SourceRootOf(car, source, chain.Log);
                    chain.SourceBaseRoot = (car.Pack != null) ? source : null;
                    chain.SourceMediaName = car.MediaName;
                    chain.TargetRoot = target;
                    chain.TemplateMediaName = template;
                    chain.NewMediaName = car.TargetMedia;
                    chain.Again = again;
                    chain.SourceGameName = GameInfo.Name(this.sourceGame) ?? "";
                    chain.SourcePack = car.Dlc ?? "";
                    chain.DisplayName = car.Name;
                    chain.ModelShort = car.Short;
                    chain.Year = car.Year;
                    chain.SkipParts.AddRange(skip);
                    chain.CarMaterials = carMaterials;
                    chain.WheelMaterials = wheelMaterials;
                    chain.UseSoundOf = useSoundOf;
                    chain.Swaps = swaps;
                    chain.MakeId = makeId;
                    chain.Progress = delegate(string step)
                    {
                        this.BeginInvoke((MethodInvoker) delegate { this.status.Text = car.Name + ": " + step; });
                    };
                    chain.Run();
                }
                catch (Exception e)
                {
                    failed = e;
                }
                StringBuilder text = new StringBuilder();
                text.AppendLine(((failed != null) ? "INSTALL FAILED: " : "INSTALLED: ") + car.Name + " (" + car.MediaName + ") in " + target);
                if (failed != null)
                {
                    text.AppendLine(failed.Message);
                    text.AppendLine("The steps marked done below were written; the others were not.");
                }
                text.AppendLine();
                foreach (string line in chain.Log)
                {
                    text.AppendLine(line);
                }
                Fm2PortRecord written = (failed == null) ? Fm2PortRecord.Read(GamePaths.FindCar(target, car.TargetMedia)) : null;
                List<ReviewRow> installReview = ReviewFromLog(chain.Log);
                if (failed != null)
                {
                    installReview.Insert(0, new ReviewRow("install", "FAILED: " + failed.Message, ReviewKind.Error));
                }
                return delegate
                {
                    this.report.Text = text.ToString();
                    this.tabs.SelectedIndex = 0;
                    this.installReviews[car.MediaName] = installReview;
                    if (failed != null)
                    {
                        this.ShowReview();
                    }
                    if (failed != null)
                    {
                        this.status.Text = "Install failed: " + failed.Message;
                        return;
                    }
                    // the car is in FM2 now: a ported car (shown with "Also show cars FM2 already has", ported again from there)
                    car.Fm2Id = chain.CarId;
                    car.Record = written;
                    car.RecordApplied = true;
                    foreach (ListViewItem item in this.items)
                    {
                        if (item.Tag == car)
                        {
                            item.SubItems[5].Text = car.Fm2Text;
                            item.ForeColor = ForzaTheme.TextDim;
                        }
                    }
                    this.trials.Remove(car.MediaName);
                    this.Filter();
                    this.ShowTrial();
                    this.status.Text = car.Name + " installed in FM2 as car " + chain.CarId + (chain.SoundDone ? "." : ", but the engine sound step failed (see the report).");
                };
            });
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
