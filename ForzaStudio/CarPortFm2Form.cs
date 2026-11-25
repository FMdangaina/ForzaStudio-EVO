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
    internal sealed class CarPortFm2Form : Form
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
        private readonly Label materialsTitle;
        private readonly DataGridView materials;
        private readonly List<ListViewItem> items = new List<ListViewItem>();
        private readonly Dictionary<string, Trial> trials = new Dictionary<string, Trial>(StringComparer.OrdinalIgnoreCase);
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
            this.sourceBox = this.FolderRow(folders, "Source game (FM4/FM3/FH1/FH2):", 10, DefaultSource());
            this.targetBox = this.FolderRow(folders, "Forza Motorsport 2:", 42, DefaultTarget());
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
            filter.Controls.Add(find);
            filter.Controls.Add(this.search);
            filter.Controls.Add(this.includeDlc);

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
            this.cars.SelectedIndexChanged += delegate { this.ShowTrial(); this.UpdateButtons(); };
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
            this.tabs.TabPages.Add(reportPage);
            this.tabs.TabPages.Add(this.variantsPage);
            this.tabs.TabPages.Add(this.materialsPage);

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

        private TextBox FolderRow(Panel parent, string caption, int y, string value)
        {
            Label label = new Label();
            label.Text = caption;
            label.Location = new Point(10, y + 4);
            label.AutoSize = true;
            TextBox box = new TextBox();
            box.Location = new Point(215, y);
            box.Width = parent.Width - 215 - 214;
            box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            box.Text = value;
            Button browse = new Button();
            browse.Text = "Folder...";
            browse.Size = new Size(72, 26);
            browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            browse.Location = new Point(parent.Width - 206, y - 2);
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
            this.status.Text = "Reading the databases...";
            this.Background(delegate
            {
                StringBuilder notes = new StringBuilder();
                List<SourceCar> list = new List<SourceCar>();
                List<string[]> templateCars = new List<string[]>();
                HashSet<string> inFm2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (SqliteDatabase to = SqliteDatabase.FromBytes(File.ReadAllBytes(CarPortFm2Chain.FindDatabase(target))))
                {
                    StringTable names2 = StringTableOrNull(target);
                    foreach (object[] row in to.Query("SELECT MediaName, DisplayName FROM Data_Car ORDER BY MediaName").Rows)
                    {
                        string media = row[0] as string;
                        if (media == null)
                        {
                            continue;
                        }
                        inFm2.Add(media);
                        if (GamePaths.FindMedia(target, "cars", media) != null)
                        {
                            string name = (names2 != null) ? names2.Lookup("Data_Car", "DisplayName", row[1] as string) : null;
                            templateCars.Add(new[] { media, string.IsNullOrEmpty(name) ? media : name });
                        }
                    }
                }
                ReadCars(source, CarPortFm2Chain.FindDatabase(source), null, null, "", list, inFm2);
                string content = dlcToo ? InstalledDlc.ContentFolder(game) : null;
                if (content != null)
                {
                    List<DlcSource> packs = new List<DlcSource>();
                    uint title = InstalledDlc.TitleId(game);
                    foreach (string file in Directory.GetFiles(content))
                    {
                        try
                        {
                            if (StfsReader.IsPackage(file))
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
                            ReadCars(root, CarPortFm2Chain.FindDatabase(root), root, pack, label, list, inFm2);
                        }
                        catch (Exception e)
                        {
                            notes.AppendLine(label + ": left out, " + e.Message);
                        }
                    }
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
                        if (car.Traffic)
                        {
                            item.ForeColor = ForzaTheme.Yellow;
                        }
                        this.items.Add(item);
                    }
                    this.templateList = templateCars;
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
                    this.status.Text = string.Format(CultureInfo.InvariantCulture, "{0} {1} cars FM2 does not have, {2} FM2 template cars. Choose a car and its template, then Trial Run.", list.Count, GameInfo.Name(game), templateCars.Count);
                    if (notes.Length > 0)
                    {
                        this.report.Text = "DLC packages:\r\n" + notes.ToString();
                    }
                };
            });
        }

        // The cars of the source folder or of one staged pack that FM2 does not have under that name.
        private static void ReadCars(string root, string database, string packRoot, DlcSource pack, string label, List<SourceCar> into, HashSet<string> inFm2)
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
                foreach (object[] row in source.Query("SELECT Id, MediaName, DisplayName, Year, " + (hasShort ? "ModelShort" : "NULL") + " FROM Data_Car ORDER BY Id").Rows)
                {
                    string media = row[1] as string;
                    if (media == null || inFm2.Contains(media) || (own != null && !own.Contains(media)))
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
            this.load.Enabled = !this.busy;
            this.trial.Enabled = ready;
            this.install.Enabled = ready && current != null && CarPortFm2Chain.Fits(current.Plan, current.Skip, out seeThrough, out opaque);
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
                try
                {
                    CarDuplicate copy = new CarDuplicate();
                    copy.TargetRoot = target;
                    copy.SourceMediaName = template;
                    copy.NewMediaName = car.MediaName;
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
                text.AppendLine("Install then converts the database rows and builds the engine sound from the source game's.");
                return delegate
                {
                    this.trials[car.MediaName] = made;
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
            if (MessageBox.Show(this, "Install " + car.Name + " in Forza Motorsport 2?\n\nFolder: " + target + "\nTemplate car: " + template + soundLine
                + "\n\nThe game's database, zip list and sound files are changed and the car's files are added." + left, this.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
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
                    chain.NewMediaName = car.MediaName;
                    chain.DisplayName = car.Name;
                    chain.ModelShort = car.Short;
                    chain.Year = car.Year;
                    chain.SkipParts.AddRange(skip);
                    chain.CarMaterials = carMaterials;
                    chain.WheelMaterials = wheelMaterials;
                    chain.UseSoundOf = useSoundOf;
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
                return delegate
                {
                    this.report.Text = text.ToString();
                    this.tabs.SelectedIndex = 0;
                    if (failed != null)
                    {
                        this.status.Text = "Install failed: " + failed.Message;
                        return;
                    }
                    // the car is an FM2 car now: out of the list
                    foreach (ListViewItem item in this.items.ToArray())
                    {
                        if (item.Tag == car)
                        {
                            this.items.Remove(item);
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
