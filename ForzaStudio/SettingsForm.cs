namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Windows.Forms;

    // Tools > Settings (ROADMAP 12.3, user 2026-10-20), three game-style tabs:
    //   FOLDERS  the FM4 folder installs go to, the source games, Xenia (settings.txt: the port window
    //            starts from them);
    //   PORT     the values of Port.ini (car and row Ids, PI lines of DLC packages, drag tyres);
    //   UPGRADES Upgrades.ini section by section, each key with the file's note (user 2026-10-20);
    //            both files can be set back to their defaults (a .bak copy first);
    //   VIEWER   textures at start, the language of the database's text, the music.
    // Nothing is written until OK.
    internal sealed class SettingsForm : Form
    {
        // settings.txt keys of the folders (CarPort.* are the port window's own).
        public const string Fm4Key = "CarPort.Target";
        public const string XeniaKey = "CarPort.Xenia";
        public static readonly string[][] SourceFolders =
        {
            new string[] { "Folder.FH1", "Forza Horizon:" },
            new string[] { "Folder.FH2", "Forza Horizon 2:" },
            new string[] { "Folder.FM3", "Forza Motorsport 3:" },
            new string[] { "Folder.FM2", "Forza Motorsport 2:" },
        };

        // The FM4 string table languages (Media\StringTables\<language>.zip).
        private static readonly string[] Languages = { "EN", "GB", "IT", "DE", "FR", "ES", "MX", "BR", "NL", "PL", "CZ", "HU", "RU", "JP", "KO", "CHT" };

        // Port.ini values shown in the PORT tab: section, key, caption.
        private static readonly string[][] PortValues =
        {
            new string[] { "PI", "FH1.Slope", "FH1 DLC PI line: slope" },
            new string[] { "PI", "FH1.Offset", "FH1 DLC PI line: offset" },
            new string[] { "PI", "FM3.Slope", "FM3 DLC PI line: slope" },
            new string[] { "PI", "FM3.Offset", "FM3 DLC PI line: offset" },
            new string[] { "Tires", "DragLevel", "Drag tyres: upgrade level" },
            new string[] { "Tires", "DragPrice", "Drag tyres: price (Cr)" },
        };

        private readonly Dictionary<string, TextBox> folders = new Dictionary<string, TextBox>();
        private readonly Dictionary<string, TextBox> portBoxes = new Dictionary<string, TextBox>();
        private CheckBox dragTires;
        private ComboBox damageWords;
        private CheckBox textures;
        private ComboBox language;
        private CheckBox musicAutoPlay;
        private CheckBox musicShuffle;
        private Color bodyColor;
        private Button bodySwatch;
        private IniEditorPanel allPanel;

        public SettingsForm()
            : this(null)
        {
        }

        // tab: "Folders", "Port", "Upgrades" or "Viewer" to open on (the port window opens Upgrades).
        public SettingsForm(string tab)
        {
            this.Text = "Settings - ForzaStudio EVO";
            this.ClientSize = new Size(900, 640);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ShowInTaskbar = false;
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.Font = ForzaTheme.Regular;

            MenuStrip header = new MenuStrip();
            ToolStripLabel forza = new ToolStripLabel("FORZA");
            forza.Font = new Font("Segoe UI", 11f, FontStyle.Bold | FontStyle.Italic);
            forza.ForeColor = ForzaTheme.Red;
            forza.Margin = new Padding(6, 1, 0, 2);
            ToolStripLabel studio = new ToolStripLabel("STUDIO EVO");
            studio.Font = new Font("Segoe UI Light", 11f, FontStyle.Italic);
            studio.Margin = new Padding(0, 1, 18, 2);
            ToolStripLabel title = new ToolStripLabel("SETTINGS");
            title.Font = ForzaTheme.Bold;
            title.ForeColor = ForzaTheme.TextDim;
            header.Items.Add(forza);
            header.Items.Add(studio);
            header.Items.Add(title);

            TabControl tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.TabPages.Add(this.FoldersPage());
            tabs.TabPages.Add(this.PortPage());
            tabs.TabPages.Add(this.UpgradesPage());
            tabs.TabPages.Add(this.ViewerPage());
            tabs.TabPages.Add(this.AllPage());
            foreach (TabPage page in tabs.TabPages)
            {
                if (string.Equals(page.Text, tab, StringComparison.OrdinalIgnoreCase))
                {
                    tabs.SelectedTab = page;
                }
            }
            ForzaTabStrip strip = new ForzaTabStrip(tabs);

            Panel buttons = new Panel();
            buttons.Dock = DockStyle.Bottom;
            buttons.Height = 44;
            Button ok = new Button();
            ok.Text = "OK";
            ok.Size = new Size(90, 28);
            ok.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            ok.Location = new Point(buttons.Width - 196, 8);
            ok.Click += delegate
            {
                if (this.Save())
                {
                    this.DialogResult = DialogResult.OK;
                }
            };
            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.Size = new Size(90, 28);
            cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cancel.Location = new Point(buttons.Width - 100, 8);
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            this.CancelButton = cancel;

            this.Controls.Add(tabs);
            this.Controls.Add(strip);
            this.Controls.Add(buttons);
            this.Controls.Add(header);
            this.MainMenuStrip = header;
            ForzaTheme.Apply(this);
        }

        #region Folders

        private TabPage FoldersPage()
        {
            TabPage page = new TabPage("Folders");
            page.AutoScroll = true;
            int y = 14;
            Label note = Note(page, "Game folders hold default.xex and Media. Installs go only to the FM4 folder below (keep a clean copy apart).", y);
            y += note.Height + 12;
            y = this.FolderRow(page, Fm4Key, "Forza Motorsport 4 (installs):", y, true);
            foreach (string[] source in SourceFolders)
            {
                y = this.FolderRow(page, source[0], source[1], y, true);
            }
            y = this.FolderRow(page, XeniaKey, "Xenia (DLC packs):", y, false);
            Note(page, "Set them once here. The port window lists these source games; a game's DLC are read from Xenia; the DLC without a database of their own (FM2, FM3 second disc) take it from the source game chosen there.", y + 4);
            return page;
        }

        private int FolderRow(TabPage page, string key, string caption, int y, bool game)
        {
            Label label = new Label();
            label.Text = caption;
            label.AutoSize = true;
            label.Location = new Point(12, y + 4);
            TextBox box = new TextBox();
            box.Text = AppSettings.Get(key, "");
            box.Location = new Point(210, y);
            box.Width = 570;
            Button browse = new Button();
            browse.Text = "Folder...";
            browse.Size = new Size(76, 24);
            browse.Location = new Point(790, y - 2);
            browse.Click += delegate
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = caption;
                    dialog.ShowNewFolderButton = false;
                    if (Directory.Exists(box.Text))
                    {
                        dialog.SelectedPath = box.Text;
                    }
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        box.Text = dialog.SelectedPath;
                    }
                }
            };
            Label found = new Label();
            found.AutoSize = true;
            found.ForeColor = ForzaTheme.TextDim;
            found.Location = new Point(210, y + 27);
            EventHandler check = delegate { found.Text = Describe(box.Text.Trim(), game); };
            box.TextChanged += check;
            check(box, EventArgs.Empty);
            page.Controls.Add(label);
            page.Controls.Add(box);
            page.Controls.Add(browse);
            page.Controls.Add(found);
            this.folders[key] = box;
            return y + 54;
        }

        // The Xenia folder: the DLC packages its content folder holds per game, or that there is no content folder.
        private static string DescribeXenia(string folder)
        {
            List<string> found = new List<string>();
            foreach (ForzaGame game in new ForzaGame[] { ForzaGame.Motorsport2, ForzaGame.Motorsport3, ForzaGame.Motorsport4, ForzaGame.Horizon1, ForzaGame.Horizon2 })
            {
                try
                {
                    string content = DlcPackage.XeniaContent(folder, InstalledDlc.TitleId(game).ToString("X8", CultureInfo.InvariantCulture));
                    if (Directory.Exists(content))
                    {
                        int packages = 0;
                        foreach (string file in Directory.GetFiles(content))
                        {
                            DlcCatalog.Header header = DlcCatalog.ReadHeader(file);
                            if (header != null && header.TitleId == InstalledDlc.TitleId(game))
                            {
                                packages++;
                            }
                        }
                        found.Add(GameInfo.Tag(game) + " " + packages);
                    }
                }
                catch (IOException)
                {
                }
            }
            return (found.Count == 0)
                ? "no DLC content found here: choose the folder of xenia_canary.exe (or its content folder)"
                : "DLC packages found: " + string.Join(", ", found.ToArray());
        }

        // What a folder box points at: the game found there, or why not.
        private static string Describe(string folder, bool game)
        {
            if (folder.Length == 0)
            {
                return "not set";
            }
            if (!Directory.Exists(folder))
            {
                return "folder not found";
            }
            if (!game)
            {
                return DescribeXenia(folder);
            }
            try
            {
                ForzaGame detected = GameInfo.Detect(folder);
                return (detected == ForzaGame.Unknown) ? "no Forza game found here" : GameInfo.Tag(detected) + " found";
            }
            catch (Exception)
            {
                return "no Forza game found here";
            }
        }

        #endregion

        #region Port

        private TabPage PortPage()
        {
            TabPage page = new TabPage("Port");
            page.AutoScroll = true;
            int y = 14;
            Label note = Note(page, PortSettings.FileName + " next to the exe. Empty box = the default.", y);
            y += note.Height + 10;
            foreach (string[] value in PortValues)
            {
                Label label = new Label();
                label.Text = value[2] + ":";
                label.AutoSize = true;
                label.Location = new Point(12, y + 3);
                TextBox box = new TextBox();
                box.Text = PortSettings.Get(value[0], value[1], "");
                box.Location = new Point(270, y);
                box.Width = 140;
                page.Controls.Add(label);
                page.Controls.Add(box);
                this.portBoxes[value[0] + "." + value[1]] = box;
                y += 28;
            }
            this.dragTires = new CheckBox();
            this.dragTires.Text = "Add FM4's drag tyres to a ported car without them";
            this.dragTires.AutoSize = true;
            this.dragTires.Checked = PortSettings.GetBool("Tires", "DragTires", true);
            this.dragTires.Location = new Point(12, y + 2);
            page.Controls.Add(this.dragTires);
            y += 36;

            // [Damage]: where the crash deformation words of a car ported from FH1 / FH2 come from.
            Label damageTitle = new Label();
            damageTitle.Text = "Crash damage of cars ported from FH1 / FH2";
            damageTitle.Font = new Font(this.Font, FontStyle.Bold);
            damageTitle.AutoSize = true;
            damageTitle.Location = new Point(12, y);
            page.Controls.Add(damageTitle);
            y += 24;
            this.damageWords = new ComboBox();
            this.damageWords.DropDownStyle = ComboBoxStyle.DropDownList;
            this.damageWords.Items.AddRange(DamageWordChoices);
            this.damageWords.Width = 400;
            y = LabelledRow(page, "Damage words:", this.damageWords, y);
            this.LoadDamage();
            Label damageNote = Note(page, "The deformation of every part comes from an FM4 car; a part that car lacks takes the words of its nearest part. FH's own words are never used (they made a car explode in game).", y);
            y += damageNote.Height + 14;

            Button reset = DefaultsButton(PortSettings.FileName, delegate { this.ResetIni(PortSettings.IniPath, PortSettings.DefaultIni, true); });
            reset.Location = new Point(12, y);
            page.Controls.Add(reset);
            return page;
        }

        // Port.ini [Damage] Words values, shown as "value - meaning".
        private static readonly object[] DamageWordChoices =
        {
            "donor - an FM4 car's words (the same car if FM4 has it, else the donor)",
            "donorcar - always the donor car's words",
        };

        private static int LabelledRow(TabPage page, string caption, Control control, int y)
        {
            Label label = new Label();
            label.Text = caption;
            label.AutoSize = true;
            label.Location = new Point(12, y + 3);
            control.Location = new Point(270, y);
            page.Controls.Add(label);
            page.Controls.Add(control);
            return y + 28;
        }

        private static string ChoiceValue(ComboBox box)
        {
            string text = (box.SelectedItem as string) ?? "";
            int dash = text.IndexOf(" - ", StringComparison.Ordinal);
            return (dash > 0) ? text.Substring(0, dash) : text;
        }

        private static void SelectChoice(ComboBox box, string value)
        {
            for (int i = 0; i < box.Items.Count; i++)
            {
                string item = (string) box.Items[i];
                if (string.Equals(item, value, StringComparison.OrdinalIgnoreCase) || item.StartsWith(value + " - ", StringComparison.OrdinalIgnoreCase))
                {
                    box.SelectedIndex = i;
                    return;
                }
            }
            box.SelectedIndex = 0;
        }

        private void LoadDamage()
        {
            SelectChoice(this.damageWords, PortSettings.Get("Damage", "Words", "donor").Trim());
        }

        private static Button DefaultsButton(string file, EventHandler click)
        {
            Button button = new Button();
            button.Text = "Restore " + file + " Defaults";
            button.AutoSize = true;
            button.Height = 28;
            button.Click += click;
            return button;
        }

        private void ResetIni(string path, string defaults, bool port)
        {
            if (MessageBox.Show(this, "Put the " + (port ? "port" : "upgrade") + " sections of " + Path.GetFileName(path) + " back to their default values? Your changes in them are lost (the other sections stay; a copy of the file is kept in bak\\).",
                this.Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            {
                return;
            }
            try
            {
                AppIni.Ensure();
                if (File.Exists(path))
                {
                    string bak = Path.Combine(Path.GetDirectoryName(path), "bak");
                    Directory.CreateDirectory(bak);
                    File.Copy(path, Path.Combine(bak, Path.GetFileName(path) + ".bak"), true);
                }
                IniFile.ResetSections(path, defaults);
            }
            catch (IOException exception)
            {
                MessageBox.Show(this, "Cannot write " + path + ": " + exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            catch (UnauthorizedAccessException exception)
            {
                MessageBox.Show(this, "Cannot write " + path + ": " + exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (port)
            {
                foreach (string[] value in PortValues)
                {
                    this.portBoxes[value[0] + "." + value[1]].Text = PortSettings.Get(value[0], value[1], "");
                }
                this.dragTires.Checked = PortSettings.GetBool("Tires", "DragTires", true);
                this.LoadDamage();
            }
            else
            {
                this.LoadUpgrades();
            }
            this.allPanel.Reload();
        }

        #endregion

        #region Upgrades

        // Upgrades.ini as a list of sections and a grid of its keys: the value is edited in place, the
        // note is the file's comment above the key. Changes are written on OK (comments kept).
        private ListBox upgradeSections;
        private DataGridView upgradeGrid;
        private Label upgradeComment;
        private List<IniFile.Section> upgradeLayout = new List<IniFile.Section>();
        private readonly Dictionary<string, string> upgradeEdits = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // "section|key" -> value
        private bool upgradeFilling;

        private TabPage UpgradesPage()
        {
            TabPage page = new TabPage("Upgrades");
            page.Padding = new Padding(8);
            Label note = new Label();
            note.Text = CarUpgrades.FileName + " next to the exe: the generic upgrades of ported cars (Street / Sport / Race levels, aspiration, drive type conversions). Edit the Value column; the note is the file's own.";
            note.ForeColor = SystemColors.GrayText;
            note.Dock = DockStyle.Top;
            note.Height = 36;
            this.upgradeComment = new Label();
            this.upgradeComment.Dock = DockStyle.Top;
            this.upgradeComment.Height = 40;
            this.upgradeComment.ForeColor = ForzaTheme.Text;
            this.upgradeSections = new ListBox();
            this.upgradeSections.Dock = DockStyle.Left;
            this.upgradeSections.Width = 230;
            this.upgradeSections.IntegralHeight = false;
            this.upgradeSections.SelectedIndexChanged += delegate { this.ShowUpgradeSection(); };
            Panel gap = new Panel();
            gap.Dock = DockStyle.Left;
            gap.Width = 8;
            this.upgradeGrid = new DataGridView();
            this.upgradeGrid.Dock = DockStyle.Fill;
            this.upgradeGrid.AllowUserToAddRows = false;
            this.upgradeGrid.AllowUserToDeleteRows = false;
            this.upgradeGrid.AllowUserToResizeRows = false;
            this.upgradeGrid.RowHeadersVisible = false;
            this.upgradeGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            this.upgradeGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            this.upgradeGrid.Columns.Add("Key", "Key");
            this.upgradeGrid.Columns.Add("Value", "Value");
            this.upgradeGrid.Columns.Add("Note", "Note");
            this.upgradeGrid.Columns[0].ReadOnly = true;
            this.upgradeGrid.Columns[0].Width = 190;
            this.upgradeGrid.Columns[1].Width = 180;
            this.upgradeGrid.Columns[2].ReadOnly = true;
            this.upgradeGrid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            this.upgradeGrid.Columns[2].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            this.upgradeGrid.Columns[2].DefaultCellStyle.ForeColor = ForzaTheme.TextDim;
            this.upgradeGrid.EditMode = DataGridViewEditMode.EditOnEnter;
            // Only the value shows the selection: the key and note are read-only text.
            foreach (int readOnly in new int[] { 0, 2 })
            {
                DataGridViewCellStyle style = this.upgradeGrid.Columns[readOnly].DefaultCellStyle;
                style.SelectionBackColor = ForzaTheme.Surface;
                style.SelectionForeColor = (readOnly == 2) ? ForzaTheme.TextDim : ForzaTheme.Text;
            }
            foreach (DataGridViewColumn column in this.upgradeGrid.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
            }
            // A grid picks its first cell when it is shown: nothing selected until the user clicks.
            this.upgradeGrid.VisibleChanged += delegate
            {
                if (this.upgradeGrid.Visible)
                {
                    this.upgradeGrid.CurrentCell = null;
                    this.upgradeGrid.ClearSelection();
                }
            };
            this.upgradeGrid.CellValueChanged += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (this.upgradeFilling || e.RowIndex < 0 || e.ColumnIndex != 1 || this.upgradeSections.SelectedItem == null)
                {
                    return;
                }
                DataGridViewRow row = this.upgradeGrid.Rows[e.RowIndex];
                this.upgradeEdits[(string) this.upgradeSections.SelectedItem + "|" + (string) row.Cells[0].Value] = Convert.ToString(row.Cells[1].Value, CultureInfo.InvariantCulture).Trim();
            };
            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 40;
            Button reset = DefaultsButton(CarUpgrades.FileName, delegate { this.ResetIni(CarUpgrades.IniPath, CarUpgrades.DefaultIni, false); });
            reset.Location = new Point(0, 8);
            bottom.Controls.Add(reset);
            page.Controls.Add(this.upgradeGrid);
            page.Controls.Add(gap);
            page.Controls.Add(this.upgradeSections);
            page.Controls.Add(this.upgradeComment);
            page.Controls.Add(note);
            page.Controls.Add(bottom);
            this.LoadUpgrades();
            return page;
        }

        // The file (written with the defaults when missing), its sections in the list; edits not saved yet are dropped.
        private void LoadUpgrades()
        {
            string path = CarUpgrades.IniPath;
            if (!File.Exists(path))
            {
                IniFile.WriteDefaults(path, CarUpgrades.DefaultIni);
            }
            this.upgradeLayout = IniFile.Layout(File.Exists(path) ? File.ReadAllText(path) : CarUpgrades.DefaultIni);
            // the file also holds the port and [App] sections: only the upgrade ones are listed here
            Dictionary<string, Dictionary<string, string>> own = CarUpgrades.ParseIni(CarUpgrades.DefaultIni);
            this.upgradeLayout.RemoveAll(delegate(IniFile.Section s) { return !own.ContainsKey(s.Name); });
            this.upgradeEdits.Clear();
            string selected = this.upgradeSections.SelectedItem as string;
            this.upgradeSections.Items.Clear();
            foreach (IniFile.Section section in this.upgradeLayout)
            {
                this.upgradeSections.Items.Add(section.Name);
            }
            if (this.upgradeSections.Items.Count > 0)
            {
                int index = (selected != null) ? this.upgradeSections.Items.IndexOf(selected) : 0;
                this.upgradeSections.SelectedIndex = Math.Max(0, index);
            }
        }

        private void ShowUpgradeSection()
        {
            this.upgradeGrid.EndEdit();
            this.upgradeFilling = true;
            this.upgradeGrid.Rows.Clear();
            IniFile.Section section = this.upgradeLayout.Find(delegate(IniFile.Section s) { return s.Name == this.upgradeSections.SelectedItem as string; });
            this.upgradeComment.Text = (section != null) ? section.Comment : "";
            if (section != null)
            {
                foreach (string[] entry in section.Entries)
                {
                    string edited;
                    string value = this.upgradeEdits.TryGetValue(section.Name + "|" + entry[0], out edited) ? edited : entry[1];
                    this.upgradeGrid.Rows.Add(entry[0], value, entry[2]);
                }
            }
            this.upgradeGrid.CurrentCell = null;
            this.upgradeGrid.ClearSelection();
            this.upgradeFilling = false;
        }

        #endregion

        // Every section and key of ForzaStudio.ini, the port flags and the [App] values included.
        private TabPage AllPage()
        {
            TabPage page = new TabPage("All");
            page.Padding = new Padding(8);
            Label note = new Label();
            note.Text = AppIni.FileName + " next to the exe, whole: edit any value here. The other tabs are shortcuts to the same file; a value changed in two places keeps the last one saved (this tab last).";
            note.ForeColor = SystemColors.GrayText;
            note.Dock = DockStyle.Top;
            note.Height = 36;
            this.allPanel = new IniEditorPanel();
            page.Controls.Add(this.allPanel);
            page.Controls.Add(note);
            return page;
        }

        #region Viewer

        private TabPage ViewerPage()
        {
            TabPage page = new TabPage("Viewer");
            int y = 14;
            this.textures = new CheckBox();
            this.textures.Text = "Textures on when the program starts (model view toolbar: Textures on / off)";
            this.textures.AutoSize = true;
            this.textures.Checked = AppSettings.Get("Viewer.Textures", "1") != "0";
            this.textures.Location = new Point(12, y);
            page.Controls.Add(this.textures);
            y += 36;
            Label label = new Label();
            label.Text = "Language of the game's text in the database view:";
            label.AutoSize = true;
            label.Location = new Point(12, y + 3);
            this.language = new ComboBox();
            this.language.DropDownStyle = ComboBoxStyle.DropDownList;
            this.language.Items.AddRange(Languages);
            string current = AppSettings.Get("Language", "EN").ToUpperInvariant();
            if (!this.language.Items.Contains(current))
            {
                this.language.Items.Add(current);
            }
            this.language.SelectedItem = current;
            this.language.Location = new Point(320, y);
            this.language.Width = 80;
            page.Controls.Add(label);
            page.Controls.Add(this.language);
            y += 40;
            Label colourLabel = new Label();
            colourLabel.Text = "Body colour of the car in the model viewport (paint materials):";
            colourLabel.AutoSize = true;
            colourLabel.Location = new Point(12, y + 5);
            int[] rgb = AppSettings.GetRgb("Viewer.BodyColor", "8C8C8C");
            this.bodyColor = Color.FromArgb(rgb[0], rgb[1], rgb[2]);
            this.bodySwatch = new Button();
            this.bodySwatch.Location = new Point(420, y);
            this.bodySwatch.Size = new Size(80, 26);
            this.bodySwatch.FlatStyle = FlatStyle.Flat;
            this.bodySwatch.BackColor = this.bodyColor;
            this.bodySwatch.Text = "";
            this.bodySwatch.Click += delegate
            {
                using (ColorDialog dialog = new ColorDialog())
                {
                    dialog.Color = this.bodyColor;
                    dialog.FullOpen = true;
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        this.bodyColor = dialog.Color;
                        this.bodySwatch.BackColor = this.bodyColor;
                    }
                }
            };
            page.Controls.Add(colourLabel);
            page.Controls.Add(this.bodySwatch);
            y += 40;
            this.musicAutoPlay = new CheckBox();
            this.musicAutoPlay.Text = "Music: play when a game folder opens";
            this.musicAutoPlay.AutoSize = true;
            this.musicAutoPlay.Checked = AppSettings.Get("MusicAutoPlay", "1") != "0";
            this.musicAutoPlay.Location = new Point(12, y);
            page.Controls.Add(this.musicAutoPlay);
            y += 28;
            this.musicShuffle = new CheckBox();
            this.musicShuffle.Text = "Music: shuffle";
            this.musicShuffle.AutoSize = true;
            this.musicShuffle.Checked = AppSettings.Get("MusicShuffle", "0") == "1";
            this.musicShuffle.Location = new Point(12, y);
            page.Controls.Add(this.musicShuffle);
            return page;
        }

        public bool TexturesOn
        {
            get { return this.textures.Checked; }
        }

        public bool MusicShuffle
        {
            get { return this.musicShuffle.Checked; }
        }

        public bool MusicAutoPlay
        {
            get { return this.musicAutoPlay.Checked; }
        }

        #endregion

        private static Label Note(TabPage page, string text, int y)
        {
            Label note = new Label();
            note.Text = text;
            note.ForeColor = SystemColors.GrayText;
            note.Location = new Point(12, y);
            note.Size = new Size(850, 32);
            page.Controls.Add(note);
            return note;
        }

        // Checks the numbers first, then writes settings.txt and Port.ini (only the values changed).
        private bool Save()
        {
            foreach (string[] value in PortValues)
            {
                string text = this.portBoxes[value[0] + "." + value[1]].Text.Trim();
                double number;
                if (text.Length > 0 && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                {
                    MessageBox.Show(this, value[2] + ": \"" + text + "\" is not a number (use a dot for decimals).", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }
            foreach (KeyValuePair<string, TextBox> folder in this.folders)
            {
                if (AppSettings.Get(folder.Key, "") != folder.Value.Text.Trim())
                {
                    AppSettings.Set(folder.Key, folder.Value.Text.Trim());
                }
            }
            try
            {
                foreach (string[] value in PortValues)
                {
                    string text = this.portBoxes[value[0] + "." + value[1]].Text.Trim();
                    if (text != PortSettings.Get(value[0], value[1], ""))
                    {
                        IniFile.Set(PortSettings.IniPath, PortSettings.DefaultIni, value[0], value[1], text);
                    }
                }
                if (this.dragTires.Checked != PortSettings.GetBool("Tires", "DragTires", true))
                {
                    IniFile.Set(PortSettings.IniPath, PortSettings.DefaultIni, "Tires", "DragTires", this.dragTires.Checked ? "on" : "off");
                }
                string[][] damage =
                {
                    new string[] { "Words", ChoiceValue(this.damageWords), "donor" },
                };
                foreach (string[] value in damage)
                {
                    if (!string.Equals(value[1], PortSettings.Get("Damage", value[0], value[2]).Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        IniFile.Set(PortSettings.IniPath, PortSettings.DefaultIni, "Damage", value[0], value[1]);
                    }
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, "Cannot write " + PortSettings.IniPath + ": " + exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            this.upgradeGrid.EndEdit();
            try
            {
                foreach (KeyValuePair<string, string> edit in this.upgradeEdits)
                {
                    int bar = edit.Key.IndexOf('|');
                    IniFile.Section section = this.upgradeLayout.Find(delegate(IniFile.Section s) { return s.Name == edit.Key.Substring(0, bar); });
                    string[] entry = (section == null) ? null : section.Entries.Find(delegate(string[] e) { return e[0] == edit.Key.Substring(bar + 1); });
                    if (entry != null && entry[1] != edit.Value)
                    {
                        IniFile.Set(CarUpgrades.IniPath, CarUpgrades.DefaultIni, edit.Key.Substring(0, bar), edit.Key.Substring(bar + 1), edit.Value);
                    }
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, "Cannot write " + CarUpgrades.IniPath + ": " + exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            AppSettings.Set("Viewer.Textures", this.textures.Checked ? "1" : "0");
            AppSettings.Set("Language", (string) this.language.SelectedItem);
            AppSettings.Set("MusicAutoPlay", this.musicAutoPlay.Checked ? "1" : "0");
            AppSettings.Set("MusicShuffle", this.musicShuffle.Checked ? "1" : "0");
            AppSettings.Set("Viewer.BodyColor", string.Format(CultureInfo.InvariantCulture, "{0:X2}{1:X2}{2:X2}", this.bodyColor.R, this.bodyColor.G, this.bodyColor.B));
            try
            {
                this.allPanel.Apply();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, "Cannot write " + AppIni.Path + ": " + exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }
    }
}
