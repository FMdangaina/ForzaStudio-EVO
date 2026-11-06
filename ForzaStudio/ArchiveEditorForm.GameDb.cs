namespace ForzaStudio
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Drawing;
    using System.Globalization;
    using System.Text;
    using System.Windows.Forms;

    // FM4 game database (media\db\gamedb.slt): cars with their engines, body and upgrades, tracks,
    // events and, for anything else, the raw tables. Edits go to the in-memory database at once and
    // are written to the file like any other change (File > Save, with a backup).
    public partial class ArchiveEditorForm
    {
        private enum DbSection
        {
            Cars,
            Wheels,
            Tracks,
            Events,
            Engines,
            Makes,
            Countries,
            PartMakers,
            Tables
        }

        private Panel pnlGameDb;
        private ToolStripLabel lblDbReference;
        private ToolStripButton btnDbOpenPath;
        private ToolStripButton btnDbDlc;
        // The DLC packages installed for a game, read once per session (turning the DLC button on again rescans).
        private static readonly Dictionary<ForzaGame, List<InstalledDlc>> dlcCache = new Dictionary<ForzaGame, List<InstalledDlc>>();
        private List<InstalledDlc> dlcMerged;       // the packages merged into the database shown, or null
        private string focusedGamePath;
        private ToolStripLabel lblDbLanguage;
        private ToolStripDropDownButton cmbDbLanguage;
        private readonly Dictionary<string, StringTable> stringTables = new Dictionary<string, StringTable>(StringComparer.OrdinalIgnoreCase);
        private ToolStripButton[] btnDbSections;
        private ToolStripTextBox txtDbSearch;
        private ToolStripDropDownButton btnDbChanges;
        private ListView lvDb;
        private Panel pnlDbContent;
        private List<ListViewItem> dbItems = new List<ListViewItem>();
        private DbSection dbSection = DbSection.Cars;
        private bool dbFilling;
        // Each section keeps its own search, each table its own row filter (user 2026-10-20: switching
        // between two tables to compare them lost the filter).
        private readonly Dictionary<DbSection, string> dbSearches = new Dictionary<DbSection, string>();
        private readonly Dictionary<string, string> tableFilters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private bool dbSearchRestoring;

        private TabControl tabCar;
        private PropertyGrid pgCar;
        private PropertyGrid pgEngine;
        private PropertyGrid pgBody;
        private ToolStripComboBox cmbEngine;
        private ToolStripLabel lblEngineUsage;
        private RelatedPanel relUpgrades;
        private RelatedPanel relColors;
        private RelatedPanel relOther;
        private TabControl tabTrack;
        private PropertyGrid pgTrack;
        private RelatedPanel relTrack;
        private TabControl tabEvent;
        private PropertyGrid pgEvent;
        private RelatedPanel relEvent;
        private TabControl tabEngineList;
        private PropertyGrid pgEngineList;
        private RelatedPanel relEngineList;
        private TabControl tabMake;
        private PropertyGrid pgMake;
        private RelatedPanel relMake;
        private TabControl tabCountry;
        private PropertyGrid pgCountry;
        private RelatedPanel relCountry;
        private TabControl tabPartMaker;
        private PropertyGrid pgPartMaker;
        private RelatedPanel relPartMaker;
        private ListHeader hdrMake;
        private ListHeader hdrCountry;
        private ListHeader hdrPartMaker;
        private RecordGrid gridTable;
        private Label lblDbHint;

        private GameDatabase gameDb;
        private ForzaZip.Entry gameDbEntry;
        private byte[] gameDbBytes;
        private long dbCarId = -1;
        private bool dbSplitSized;

        private void BuildGameDbView()
        {
            this.pnlGameDb = new Panel();
            this.pnlGameDb.Dock = DockStyle.Fill;

            ToolStrip bar = new ToolStrip();
            bar.GripStyle = ToolStripGripStyle.Hidden;
            string[] names = { "Cars", "Wheels", "Tracks", "Events", "Engines", "Makes", "Countries", "Manufacturers", "Tables" };
            this.btnDbSections = new ToolStripButton[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                DbSection section = (DbSection) i;
                // Groups: the game's main content | the raw tables.
                if (section == DbSection.Engines || section == DbSection.Tables)
                {
                    bar.Items.Add(new ToolStripSeparator());
                }
                this.btnDbSections[i] = new ToolStripButton(names[i], null, delegate { this.SelectDbSection(section); });
                bar.Items.Add(this.btnDbSections[i]);
            }
            bar.Items.Add(new ToolStripSeparator());
            bar.Items.Add(new ToolStripLabel("Search:"));
            this.txtDbSearch = new ToolStripTextBox();
            this.txtDbSearch.Width = 180;
            this.txtDbSearch.TextChanged += delegate
            {
                if (this.dbSearchRestoring)
                {
                    return;
                }
                this.dbSearches[this.dbSection] = this.txtDbSearch.Text;
                this.FilterDbList();
            };
            bar.Items.Add(this.txtDbSearch);
            this.btnDbChanges = new ToolStripDropDownButton("No changes");
            this.btnDbChanges.Alignment = ToolStripItemAlignment.Right;
            bar.Items.Add(this.btnDbChanges);
            bar.Items.Add(new ToolStripSeparator());
            this.btnDbOpenPath = new ToolStripButton("Open Linked File", null, delegate { this.OpenGamePath(this.focusedGamePath); });
            this.btnDbOpenPath.Enabled = false;
            bar.Items.Add(this.btnDbOpenPath);
            this.btnDbDlc = new ToolStripButton("DLC", null, delegate { this.ToggleDbDlc(); });
            this.btnDbDlc.CheckOnClick = true;
            this.btnDbDlc.Checked = AppSettings.Get("Db.Dlc", "1") != "0";
            this.btnDbDlc.ToolTipText = "Shows the DLC packages installed for this game (Xenia content folder, Tools > Settings): their cars and tracks join the lists. The view is read-only; turn it off to edit the game's own database.";
            bar.Items.Add(this.btnDbDlc);
            ToolStripButton check = new ToolStripButton("Check References", null, delegate { this.CheckDbReferences(); });
            check.ToolTipText = "Checks that every id column points at an existing row (cars, engines, bodies, upgrades, tracks, events...)";
            bar.Items.Add(check);
            this.lblDbLanguage = new ToolStripLabel("Text:");
            bar.Items.Add(this.lblDbLanguage);
            this.cmbDbLanguage = new ToolStripDropDownButton("EN");
            this.cmbDbLanguage.Font = ForzaTheme.Bold;
            this.cmbDbLanguage.ToolTipText = "Language of the game's text (GAME:\\Media\\StringTables)";
            bar.Items.Add(this.cmbDbLanguage);
            this.lblDbReference = new ToolStripLabel();
            this.lblDbReference.Alignment = ToolStripItemAlignment.Right;
            this.lblDbReference.ForeColor = ForzaTheme.TextDim;
            bar.Items.Add(this.lblDbReference);

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.FixedPanel = FixedPanel.Panel1;
            split.SizeChanged += delegate
            {
                // Set once the panel has a real size (earlier it throws).
                if (!this.dbSplitSized && split.Width > 600)
                {
                    this.dbSplitSized = true;
                    split.SplitterDistance = 450;
                }
            };

            this.lvDb = new ListView();
            this.lvDb.Dock = DockStyle.Fill;
            this.lvDb.View = View.Details;
            this.lvDb.FullRowSelect = true;
            this.lvDb.HideSelection = false;
            this.lvDb.MultiSelect = false;
            this.lvDb.BorderStyle = BorderStyle.None;
            this.lvDb.SelectedIndexChanged += delegate
            {
                if (!this.dbFilling && this.lvDb.SelectedItems.Count == 1)
                {
                    this.ShowDbItem(this.lvDb.SelectedItems[0]);
                }
            };
            this.lvDb.ColumnClick += delegate(object sender, ColumnClickEventArgs e)
            {
                DbListSorter sorter = this.lvDb.ListViewItemSorter as DbListSorter;
                bool descending = sorter != null && sorter.Column == e.Column && !sorter.Descending;
                this.lvDb.ListViewItemSorter = new DbListSorter(e.Column, descending);
            };
            this.lvDb.OwnerDraw = true;
            this.lvDb.DrawColumnHeader += new DrawListViewColumnHeaderEventHandler(this.lvDb_DrawColumnHeader);
            this.lvDb.DrawItem += delegate(object sender, DrawListViewItemEventArgs e) { };
            this.lvDb.DrawSubItem += new DrawListViewSubItemEventHandler(this.lvDb_DrawSubItem);
            split.Panel1.Controls.Add(this.lvDb);

            this.pnlDbContent = new Panel();
            this.pnlDbContent.Dock = DockStyle.Fill;
            split.Panel2.Controls.Add(this.pnlDbContent);

            // Cars
            this.tabCar = new TabControl();
            this.tabCar.Dock = DockStyle.Fill;
            this.pgCar = this.NewPropertyGrid();
            this.AddTab(this.tabCar, "Car", this.pgCar);
            Panel engine = new Panel();
            engine.Dock = DockStyle.Fill;
            this.pgEngine = this.NewPropertyGrid();
            ToolStrip engineBar = new ToolStrip();
            engineBar.GripStyle = ToolStripGripStyle.Hidden;
            engineBar.Items.Add(new ToolStripLabel("Engine:"));
            this.cmbEngine = new ToolStripComboBox();
            this.cmbEngine.DropDownStyle = ComboBoxStyle.DropDownList;
            this.cmbEngine.AutoSize = false;
            this.cmbEngine.Width = 420;
            this.cmbEngine.SelectedIndexChanged += delegate { this.ShowEngine(); };
            engineBar.Items.Add(this.cmbEngine);
            engineBar.Items.Add(new ToolStripButton("Add Engine Swap...", null, delegate { this.AddEngineSwap(); }));
            this.lblEngineUsage = new ToolStripLabel();
            engineBar.Items.Add(this.lblEngineUsage);
            engine.Controls.Add(this.pgEngine);
            engine.Controls.Add(engineBar);
            this.AddTab(this.tabCar, "Engine", engine);
            this.AddTab(this.tabCar, "Power", this.BuildPowerTab());
            this.AddTab(this.tabCar, "Transmission", this.BuildTransmissionTab());
            this.pgBody = this.NewPropertyGrid();
            this.AddTab(this.tabCar, "Body", this.pgBody);
            this.relUpgrades = this.NewRelatedPanel();
            this.AddTab(this.tabCar, "Upgrades", this.relUpgrades);
            this.relColors = this.NewRelatedPanel();
            this.AddTab(this.tabCar, "Colors", this.relColors);
            this.relOther = this.NewRelatedPanel();
            this.AddTab(this.tabCar, "Other", this.relOther);

            this.BuildWheelView(bar);

            // Tracks
            this.tabTrack = new TabControl();
            this.tabTrack.Dock = DockStyle.Fill;
            this.pgTrack = this.NewPropertyGrid();
            this.AddTab(this.tabTrack, "Track", this.pgTrack);
            this.relTrack = this.NewRelatedPanel();
            this.AddTab(this.tabTrack, "Races, grid and AI", this.relTrack);

            // Events
            this.tabEvent = new TabControl();
            this.tabEvent.Dock = DockStyle.Fill;
            this.pgEvent = this.NewPropertyGrid();
            this.AddTab(this.tabEvent, "Event", this.pgEvent);
            this.relEvent = this.NewRelatedPanel();
            this.AddTab(this.tabEvent, "Races and participants", this.relEvent);

            // Engines: the whole Data_Engine list, not only the engines of one car
            this.tabEngineList = new TabControl();
            this.tabEngineList.Dock = DockStyle.Fill;
            this.pgEngineList = this.NewPropertyGrid();
            this.AddTab(this.tabEngineList, "Engine", this.pgEngineList);
            this.relEngineList = this.NewRelatedPanel();
            this.AddTab(this.tabEngineList, "Cars and curves", this.relEngineList);

            this.tabMake = this.NewListTabs(out this.pgMake, out this.relMake, "Make", "Cars and parts");
            this.tabCountry = this.NewListTabs(out this.pgCountry, out this.relCountry, "Country", "Makes and cars");
            this.tabPartMaker = this.NewListTabs(out this.pgPartMaker, out this.relPartMaker, "Manufacturer", "Parts");
            this.hdrMake = new ListHeader(2);
            this.hdrCountry = new ListHeader(1);
            this.hdrPartMaker = new ListHeader(1);

            // Tables
            this.gridTable = new RecordGrid(true);
            this.gridTable.Dock = DockStyle.Fill;
            this.gridTable.Changed += new EventHandler(this.OnGameDbEdited);
            this.gridTable.GamePathActivated += new GamePathHandler(this.OpenGamePath);
            this.gridTable.GamePathFocused += new GamePathHandler(this.SetFocusedGamePath);
            this.gridTable.Jump += new DbJumpHandler(this.JumpInDb);

            this.lblDbHint = new Label();
            this.lblDbHint.Dock = DockStyle.Fill;
            this.lblDbHint.TextAlign = ContentAlignment.MiddleCenter;
            this.lblDbHint.ForeColor = SystemColors.GrayText;
            this.lblDbHint.Text = "Select an item in the list.";

            // The car header (thumbnail, logo, model) stays above all the car tabs.
            this.pnlDbContent.Controls.Add(TabHost(this.tabCar, this.BuildCarHeader()));
            this.pnlDbContent.Controls.Add(TabHost(this.tabWheel, this.wheelHeader));
            this.BuildTrackAndEventHeaders();
            this.pnlDbContent.Controls.Add(TabHost(this.tabTrack, this.trackHeader));
            this.pnlDbContent.Controls.Add(TabHost(this.tabEvent, this.eventHeader));
            this.pnlDbContent.Controls.Add(TabHost(this.tabEngineList, null));
            this.pnlDbContent.Controls.Add(TabHost(this.tabMake, this.hdrMake.Panel));
            this.pnlDbContent.Controls.Add(TabHost(this.tabCountry, this.hdrCountry.Panel));
            this.pnlDbContent.Controls.Add(TabHost(this.tabPartMaker, this.hdrPartMaker.Panel));
            this.pnlDbContent.Controls.Add(this.gridTable);
            this.pnlDbContent.Controls.Add(this.lblDbHint);

            this.pnlGameDb.Controls.Add(split);
            this.pnlGameDb.Controls.Add(bar);
        }

        private PropertyGrid NewPropertyGrid()
        {
            PropertyGrid grid = new PropertyGrid();
            grid.Dock = DockStyle.Fill;
            grid.PropertySort = PropertySort.Categorized;
            grid.ToolbarVisible = false;
            grid.HelpVisible = true;
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem open = new ToolStripMenuItem("Open Linked File", null, delegate { this.OpenGamePath(SelectedGridValue(grid)); });
            menu.Items.Add(open);
            menu.Opening += delegate(object sender, CancelEventArgs e)
            {
                open.Visible = GamePaths.IsGamePath(SelectedGridValue(grid));
                DbRecord record = grid.SelectedObject as DbRecord;
                string column = (grid.SelectedGridItem != null && grid.SelectedGridItem.PropertyDescriptor != null) ? grid.SelectedGridItem.PropertyDescriptor.Name : null;
                DbJumpMenu.Fill(menu, this.gameDb, (record == null) ? null : record.Table.Name, (record == null) ? null : record.Values, column, new DbJumpHandler(this.JumpInDb));
                e.Cancel = !DbJumpMenu.HasVisibleItems(menu);
            };
            menu.Renderer = ForzaTheme.Renderer;
            grid.ContextMenuStrip = menu;
            grid.SelectedGridItemChanged += delegate { this.SetFocusedGamePath(SelectedGridValue(grid)); };
            grid.Enter += delegate { this.SetFocusedGamePath(SelectedGridValue(grid)); };
            // Double-click on a GAME:\ value opens the file (the grid view is an inner control).
            grid.ControlAdded += delegate(object sender, ControlEventArgs e) { HookDoubleClick(e.Control, grid); };
            foreach (Control child in grid.Controls)
            {
                this.HookDoubleClick(child, grid);
            }
            return grid;
        }

        private void HookDoubleClick(Control control, PropertyGrid grid)
        {
            control.MouseDoubleClick += delegate
            {
                string value = SelectedGridValue(grid);
                if (GamePaths.IsGamePath(value))
                {
                    this.OpenGamePath(value);
                }
            };
        }

        // The field being looked at, when it holds a GAME:\ path: the toolbar button opens that file.
        private void SetFocusedGamePath(string value)
        {
            this.focusedGamePath = GamePaths.IsGamePath(value) ? value : null;
            this.btnDbOpenPath.Enabled = this.focusedGamePath != null;
            if (this.focusedGamePath == null)
            {
                this.btnDbOpenPath.ToolTipText = "Select a field that holds a GAME:\\ path";
                return;
            }
            ForzaZip.Entry own = this.OwnEntry(this.focusedGamePath);
            if (own != null)
            {
                string inZip = "in this DLC (" + own.Name + ") - double-click, Enter or this button shows it";
                this.btnDbOpenPath.ToolTipText = this.focusedGamePath + "\n" + inZip;
                this.SetStatus(GamePaths.RelativePath(this.focusedGamePath) + " - " + inZip);
                return;
            }
            GamePaths.GameFile file = GamePaths.Locate(this.GameRoot, this.focusedGamePath);
            string state = (file == null) ? "not found" : !CanView(file.Name) && (file.InZip || File.Exists(file.FilePath)) ? "no viewer for this type"
                : (file.InZip ? "in " + Path.GetFileName(file.ZipPath) + " - " : "") + "double-click, Enter or this button opens it";
            this.btnDbOpenPath.ToolTipText = this.focusedGamePath + "\n" + state;
            this.SetStatus(GamePaths.RelativePath(this.focusedGamePath) + " - " + state);
        }

        private static string SelectedGridValue(PropertyGrid grid)
        {
            return (grid.SelectedGridItem == null) ? null : grid.SelectedGridItem.Value as string;
        }

        // A TabControl under the game-style tab strip.
        // An optional header, the tab strip, then the pages. The TabControl is a little larger than the
        // host so its own light border is cut off.
        private static Control TabHost(TabControl tabs, Control header)
        {
            Panel host = new Panel();
            host.Dock = DockStyle.Fill;
            ForzaTabStrip strip = new ForzaTabStrip(tabs);
            strip.Dock = DockStyle.None;
            tabs.Dock = DockStyle.None;
            if (header != null)
            {
                header.Dock = DockStyle.None;
                host.Controls.Add(header);
            }
            host.Resize += delegate
            {
                int top = 0;
                if (header != null)
                {
                    header.Bounds = new Rectangle(0, 0, host.Width, header.Height);
                    top = header.Height;
                }
                strip.Bounds = new Rectangle(0, top, host.Width, strip.Height);
                tabs.Bounds = new Rectangle(-4, top + strip.Height - 4, host.Width + 8, host.Height - top - strip.Height + 8);
            };
            host.Controls.Add(tabs);
            host.Controls.Add(strip);
            strip.BringToFront();
            return host;
        }

        private Dictionary<string, Color> dbClassColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

        private void lvDb_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
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
        }

        // Rows on the dark surface, the selection in Forza red, the class as the game's badge.
        private void lvDb_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            bool selected = e.Item.Selected;
            Color back = selected ? ForzaTheme.Selection : ((e.ItemIndex % 2 == 0) ? ForzaTheme.Surface : Color.FromArgb(31, 31, 34));
            using (Brush brush = new SolidBrush(back))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }
            string header = this.lvDb.Columns[e.ColumnIndex].Text;
            string value = e.SubItem.Text;
            if (header == "Class" && value.Length > 0)
            {
                string letter = value;
                string pi = "";
                int space = value.IndexOf(' ');
                if (space > 0)
                {
                    letter = value.Substring(0, space);
                    pi = value.Substring(space + 1);
                }
                Color color;
                if (!this.dbClassColors.TryGetValue(letter, out color))
                {
                    color = ForzaTheme.Line;
                }
                ForzaTheme.DrawClassBadge(e.Graphics, e.Bounds, letter, pi, color);
                return;
            }
            Rectangle text = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height);
            Color fore = selected ? Color.White : ((e.ColumnIndex == 0) ? ForzaTheme.TextDim : ForzaTheme.Text);
            TextRenderer.DrawText(e.Graphics, value, ForzaTheme.Regular, text, fore, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private RelatedPanel NewRelatedPanel()
        {
            RelatedPanel panel = new RelatedPanel();
            panel.Changed += new EventHandler(this.OnGameDbEdited);
            panel.GamePathActivated += new GamePathHandler(this.OpenGamePath);
            panel.GamePathFocused += new GamePathHandler(this.SetFocusedGamePath);
            panel.Jump += new DbJumpHandler(this.JumpInDb);
            return panel;
        }

        // Sections that show a table's rows with their own page.
        private static DbSection? SectionOf(string table)
        {
            switch (table)
            {
                case "Data_Car":
                    return DbSection.Cars;
                case "List_Wheels":
                    return DbSection.Wheels;
                case "Tracks":
                    return DbSection.Tracks;
                case "Events":
                    return DbSection.Events;
                case "Data_Engine":
                    return DbSection.Engines;
                case "List_CarMake":
                    return DbSection.Makes;
                case "List_Country":
                    return DbSection.Countries;
                case "List_PartManufacturer":
                    return DbSection.PartMakers;
            }
            return null;
        }

        // Go to rows of another table (Go to / Used by). A car, wheel, track or event opens in its
        // own section; anything else in Tables, filtered. Back returns here.
        private void JumpInDb(string table, string where, string key, object value)
        {
            if (this.gameDb == null || !this.gameDb.HasTable(table))
            {
                return;
            }
            Bookmark start = this.CaptureBookmark();
            DbSection? section = SectionOf(table);
            GameDatabase.Table info = this.gameDb.GetTable(table);
            bool byId = section != null && key != null && value is long && info.Columns.Count > 0 && string.Equals(info.Columns[0].Name, key, StringComparison.OrdinalIgnoreCase);
            if (byId)
            {
                this.SelectDbItem(section.Value, (long) value);
            }
            else
            {
                this.SelectDbSection(DbSection.Tables);
                this.ClearDbSearch();
                foreach (ListViewItem item in this.lvDb.Items)
                {
                    if (string.Equals(item.Tag as string, table, StringComparison.OrdinalIgnoreCase))
                    {
                        this.dbFilling = true;
                        item.Selected = true;
                        item.Focused = true;
                        item.EnsureVisible();
                        this.dbFilling = false;
                        break;
                    }
                }
                this.ShowTableRows(table, where);
                this.ShowDbContent(this.gridTable);
                this.gridTable.FocusGrid();
            }
            this.PushBookmark(start);
            this.SetStatus(string.Format("{0} WHERE {1}   (Back returns)", table, where));
        }

        // Every id column of the database checked against the table it points to.
        private void CheckDbReferences()
        {
            if (this.gameDb == null)
            {
                return;
            }
            int count;
            List<GameDatabase.Orphans> found;
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                found = this.gameDb.CheckReferences(out count);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            using (Form dialog = new Form())
            {
                dialog.Text = string.Format("References: {0} relations checked, {1} with dangling ids", count, found.Count);
                dialog.Size = new Size(1000, 520);
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.ShowInTaskbar = false;
                dialog.MinimizeBox = false;
                ListView list = new ListView();
                list.Dock = DockStyle.Fill;
                list.View = View.Details;
                list.FullRowSelect = true;
                list.Columns.Add("Column", 300);
                list.Columns.Add("Points to", 220);
                list.Columns.Add("Rows", 50);
                list.Columns.Add("Missing ids", 220);
                list.Columns.Add("In the original FM4", 190);
                bool fm4 = this.gameDb.IsFm4Layout;
                foreach (GameDatabase.Orphans orphans in found)
                {
                    GameDbSchema.Relation relation = orphans.Relation;
                    ListViewItem item = new ListViewItem(relation.Table + "." + relation.Column);
                    item.SubItems.Add(relation.Target + "." + relation.TargetColumn);
                    item.SubItems.Add(orphans.Rows.ToString(CultureInfo.InvariantCulture));
                    item.SubItems.Add(orphans.Values);
                    item.SubItems.Add(relation.Strict ? "never dangles: check this" : "also dangles" + (relation.Note != null ? " (" + relation.Note + ")" : ""));
                    item.ForeColor = (relation.Strict && fm4) ? Color.FromArgb(255, 120, 100) : ForzaTheme.TextDim;
                    item.Tag = orphans;
                    list.Items.Add(item);
                }
                Label hint = new Label();
                hint.Dock = DockStyle.Bottom;
                hint.Height = 40;
                hint.Padding = new Padding(6);
                hint.ForeColor = ForzaTheme.TextDim;
                hint.Text = found.Count == 0 ? "Every id points at an existing row."
                    : fm4 ? "Red: these columns have no dangling id in the original FM4 database, so an edit made them. Double-click a line to see its rows."
                    : "Not an FM4 database (FM3, FH1, FH2): the game itself keeps rows of cars and tracks it does not have, so nothing is marked red. Double-click a line to see its rows.";
                GameDatabase.Orphans picked = null;
                list.DoubleClick += delegate
                {
                    if (list.SelectedItems.Count == 1)
                    {
                        picked = (GameDatabase.Orphans) list.SelectedItems[0].Tag;
                        dialog.Close();
                    }
                };
                dialog.Controls.Add(list);
                dialog.Controls.Add(hint);
                ForzaTheme.Apply(dialog);
                dialog.ShowDialog(this);
                if (picked != null)
                {
                    this.JumpInDb(picked.Relation.Table, picked.Where, null, null);
                }
            }
        }

        private void AddTab(TabControl tabs, string title, Control content)
        {
            TabPage page = new TabPage(title);
            content.Dock = DockStyle.Fill;
            page.Controls.Add(content);
            tabs.TabPages.Add(page);
        }

        private void ShowGameDb(ForzaZip.Entry entry, byte[] data)
        {
            bool same = this.gameDb != null && entry == this.gameDbEntry && (ReferenceEquals(data, this.gameDbBytes) || (!entry.IsModified && this.gameDb.Changes.Count == 0));
            if (!same)
            {
                GameDatabase opened = new GameDatabase(data);
                this.CloseGameDb();
                this.gameDb = opened;
                this.gameDbEntry = entry;
                this.gameDbBytes = data;
                this.LoadReferenceDb(entry);
                this.LoadDbStrings();
                this.dbClassColors = this.gameDb.ClassColors();
                this.UpdateDbChanges();
                this.SelectDbSection(this.dbSection);
                this.StartDlcMerge(entry, opened);
            }
            this.ShowView(this.pnlGameDb);
        }

        #region Installed DLC

        // The game's own gamedb.slt (not a patch) of a known game: its installed DLC packages are read in the
        // background and merged into the view when done (InstalledDlc).
        private void StartDlcMerge(ForzaZip.Entry entry, GameDatabase opened)
        {
            this.dlcMerged = null;
            this.btnDbDlc.Text = "DLC";
            if (!this.btnDbDlc.Checked || entry.IsModified || this.GameRoot == null)
            {
                return;
            }
            string name = (entry.FullPath ?? entry.Name).Replace('\\', '/');
            if (!name.EndsWith("/db/gamedb.slt", StringComparison.OrdinalIgnoreCase) || name.IndexOf("/patch/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return;
            }
            ForzaGame game = GameInfo.Detect(this.GameRoot);
            string folder = (game == ForzaGame.Unknown) ? null : InstalledDlc.ContentFolder(game);
            if (folder == null)
            {
                return;
            }
            List<InstalledDlc> cached;
            if (dlcCache.TryGetValue(game, out cached))
            {
                this.FinishDlcMerge(opened, cached);
                return;
            }
            this.SetStatus("Reading the DLC packages installed for " + GameInfo.Name(game) + "...");
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<InstalledDlc> list = null;
                try
                {
                    list = InstalledDlc.Scan(game, folder, delegate(string package)
                    {
                        this.PostToForm(delegate { this.SetStatus("Reading DLC package " + package + "..."); });
                    });
                }
                catch (Exception)
                {
                }
                if (list != null)
                {
                    this.PostToForm(delegate
                    {
                        dlcCache[game] = list;
                        this.FinishDlcMerge(opened, list);
                    });
                }
            });
        }

        private void PostToForm(MethodInvoker action)
        {
            try
            {
                this.BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
                // the window is closed
            }
        }

        // Merges the packages into the database the user is looking at, if it is still the one scanned for and has no edits.
        private void FinishDlcMerge(GameDatabase opened, List<InstalledDlc> list)
        {
            if (this.gameDb != opened || opened.Changes.Count != 0 || this.gameDbEntry == null || this.gameDbEntry.IsModified)
            {
                return;
            }
            StringBuilder report = new StringBuilder();
            int merged = InstalledDlc.Apply(opened, list, report);
            this.btnDbDlc.ToolTipText = "Installed DLC packages (the view is read-only, turn the button off to edit):\n" + report.ToString().TrimEnd();
            if (merged == 0)
            {
                this.SetStatus("No installed DLC package with database rows was found for this game.");
                return;
            }
            this.dlcMerged = list;
            this.btnDbDlc.Text = "DLC (" + merged + ")";
            this.ApplyStrings(this.cmbDbLanguage.Visible ? this.cmbDbLanguage.Text : null, false);
            DbView view = this.CaptureDbView();
            this.SelectDbSection(this.dbSection);
            this.RestoreDbView(view);
            this.SetStatus(string.Format(CultureInfo.InvariantCulture, "{0} DLC package{1} merged into the view: {2} cars, {3} tracks (read-only; turn the DLC button off to edit the game's database).",
                merged, (merged == 1) ? "" : "s", opened.DlcCars.Count, opened.DlcTracks.Count));
        }

        private void ToggleDbDlc()
        {
            AppSettings.Set("Db.Dlc", this.btnDbDlc.Checked ? "1" : "0");
            if (this.btnDbDlc.Checked)
            {
                dlcCache.Clear();
            }
            if (this.gameDb == null || this.gameDbEntry == null)
            {
                return;
            }
            if (this.gameDb.Changes.Count > 0)
            {
                this.btnDbDlc.Checked = !this.btnDbDlc.Checked;
                AppSettings.Set("Db.Dlc", this.btnDbDlc.Checked ? "1" : "0");
                MessageBox.Show(this, "The database has pending changes. Save them (File > Save) before switching the DLC view.", "DLC", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            // The same file again, from the bytes the view was opened with.
            ForzaZip.Entry entry = this.gameDbEntry;
            byte[] data = this.gameDbBytes;
            this.gameDbEntry = null;
            this.ShowGameDb(entry, data);
        }

        #endregion

        // The languages found in GAME:\Media\StringTables; the last one picked (IT first) is used.
        private void LoadDbStrings()
        {
            List<string> languages = StringTable.Languages(this.GameRoot);
            this.cmbDbLanguage.DropDownItems.Clear();
            foreach (string language in languages)
            {
                string picked = language;
                this.cmbDbLanguage.DropDownItems.Add(new ToolStripMenuItem(language, null, delegate
                {
                    if (this.gameDb != null)
                    {
                        AppSettings.Set("Language", picked);
                        this.ShowLanguage(picked);
                        this.ApplyStrings(picked, true);
                    }
                }));
            }
            this.cmbDbLanguage.Visible = languages.Count > 0;
            this.lblDbLanguage.Visible = languages.Count > 0;
            string chosen = null;
            if (languages.Count > 0)
            {
                int index = languages.IndexOf(AppSettings.Get("Language", "EN").ToUpperInvariant());
                if (index < 0)
                {
                    index = Math.Max(0, languages.IndexOf("EN"));
                }
                chosen = languages[index];
                this.ShowLanguage(chosen);
            }
            this.ApplyStrings(chosen, false);
        }

        private void ShowLanguage(string language)
        {
            this.cmbDbLanguage.Text = language;
            foreach (ToolStripItem item in this.cmbDbLanguage.DropDownItems)
            {
                ((ToolStripMenuItem) item).Checked = item.Text == language;
            }
        }

        private void ApplyStrings(string language, bool refresh)
        {
            StringTable table = null;
            if (language != null)
            {
                string key = this.GameRoot + "|" + language + ((this.dlcMerged != null) ? "|dlc" : "");
                if (!this.stringTables.TryGetValue(key, out table))
                {
                    try
                    {
                        table = StringTable.Load(this.GameRoot, language);
                        if (this.dlcMerged != null)
                        {
                            InstalledDlc.ApplyStrings(table, this.dlcMerged, language);
                        }
                    }
                    catch (Exception)
                    {
                        table = null;
                    }
                    this.stringTables[key] = table;
                }
            }
            this.gameDb.Strings = table;
            if (refresh)
            {
                // Same place, new language.
                DbView view = this.CaptureDbView();
                this.SelectDbSection(this.dbSection);
                this.RestoreDbView(view);
            }
        }

        // A partial database (DLC patch) gets its names from the nearest full gamedb.slt above it:
        // <folder>\gamedb.slt or <folder>\Media\db\gamedb.slt.
        private void LoadReferenceDb(ForzaZip.Entry entry)
        {
            if (entry.FullPath == null || (this.gameDb.HasTable("List_CarMake") && this.gameDb.HasTable("CarClasses") && this.gameDb.HasTable("Tracks")))
            {
                return;
            }
            string self = Path.GetFullPath(entry.FullPath);
            string folder = Path.GetDirectoryName(self);
            while (!string.IsNullOrEmpty(folder))
            {
                foreach (string candidate in new string[] { Path.Combine(folder, "gamedb.slt"), Path.Combine(Path.Combine(Path.Combine(folder, "Media"), "db"), "gamedb.slt") })
                {
                    if (!File.Exists(candidate) || string.Equals(Path.GetFullPath(candidate), self, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    try
                    {
                        ForzaZip.Entry open = this.EntryForFile(candidate);
                        GameDatabase reference = new GameDatabase((open != null) ? this.zip.Read(open) : File.ReadAllBytes(candidate));
                        if (!reference.HasTable("Data_Car"))
                        {
                            reference.Dispose();
                            continue;
                        }
                        this.gameDb.Reference = reference;
                        string shown = (this.GameRoot != null && candidate.StartsWith(this.GameRoot, StringComparison.OrdinalIgnoreCase)) ? candidate.Substring(this.GameRoot.Length).TrimStart('\\') : candidate;
                        this.lblDbReference.Text = "Names from " + shown;
                        this.lblDbReference.ToolTipText = "This database is partial (a DLC patch): names of makes, classes, tracks and other cars come from\n" + candidate;
                        return;
                    }
                    catch (Exception)
                    {
                    }
                }
                folder = Path.GetDirectoryName(folder);
            }
        }

        private void CloseGameDb()
        {
            if (this.gameDb != null)
            {
                if (this.gameDb.Reference != null)
                {
                    this.gameDb.Reference.Dispose();
                }
                this.gameDb.Dispose();
                this.gameDb = null;
            }
            this.lblDbReference.Text = "";
            this.gameDbEntry = null;
            this.gameDbBytes = null;
            this.dbCarId = -1;
            this.pgCar.SelectedObject = null;
            this.pgEngine.SelectedObject = null;
            this.pgBody.SelectedObject = null;
            this.pgTrack.SelectedObject = null;
            this.pgEvent.SelectedObject = null;
        }

        // Every edit: the database file becomes the pending content of its entry (saved with File > Save).
        private void OnGameDbEdited(object sender, EventArgs e)
        {
            if (this.gameDb == null || this.zip == null || this.gameDb.ReadOnly)
            {
                return;
            }
            this.gameDbBytes = this.gameDb.ToBytes();
            this.zip.Replace(this.gameDbEntry, this.gameDbBytes);
            TreeNode node = this.FindEntryNode(this.gameDbEntry);
            if (node != null)
            {
                this.RefreshNode(node);
            }
            this.UpdateDbChanges();
            if (this.gameDb.Changes.Count > 0)
            {
                this.SetStatus(this.gameDb.Changes[this.gameDb.Changes.Count - 1].ToString() + "   (File > Save writes " + EntryFileName(this.gameDbEntry.Name) + ")");
            }
            DbRecord record = sender as DbRecord;
            if (record != null && record.Table.Name == "Data_Car")
            {
                this.RefreshCarItem(Convert.ToInt64(record.Values[0]));
            }
        }

        private static string EntryFileName(string name)
        {
            return name.Substring(name.LastIndexOf('/') + 1);
        }

        private void UpdateDbChanges()
        {
            this.btnDbChanges.DropDownItems.Clear();
            int count = (this.gameDb == null) ? 0 : this.gameDb.Changes.Count;
            this.btnDbChanges.Text = (count == 0) ? "No changes" : string.Format("{0} change{1}", count, count == 1 ? "" : "s");
            if (count == 0)
            {
                return;
            }
            for (int i = count - 1; i >= Math.Max(0, count - 40); i--)
            {
                ToolStripMenuItem item = new ToolStripMenuItem(this.gameDb.Changes[i].ToString());
                item.Enabled = false;
                this.btnDbChanges.DropDownItems.Add(item);
            }
            if (count > 40)
            {
                ToolStripMenuItem more = new ToolStripMenuItem(string.Format("... {0} older", count - 40));
                more.Enabled = false;
                this.btnDbChanges.DropDownItems.Add(more);
            }
            this.btnDbChanges.DropDownItems.Add(new ToolStripSeparator());
            this.btnDbChanges.DropDownItems.Add(new ToolStripMenuItem("Copy List", null, delegate
            {
                StringBuilder text = new StringBuilder();
                foreach (GameDatabase.Change change in this.gameDb.Changes)
                {
                    text.AppendLine(change.ToString());
                }
                Clipboard.SetText(text.ToString());
            }));
        }

        #region List

        // The table each section needs; a DLC patch database may have only some of them.
        private static readonly string[] SectionTables = { "Data_Car", "List_Wheels", "Tracks", "Events", "Data_Engine", "List_CarMake", "List_Country", "List_PartManufacturer", null };

        private bool HasSection(DbSection section)
        {
            string table = ((int) section < SectionTables.Length) ? SectionTables[(int) section] : null;
            return table == null || this.gameDb.HasTable(table);
        }

        private void SelectDbSection(DbSection section)
        {
            if (this.gameDb != null)
            {
                while (!this.HasSection(section))
                {
                    section = (DbSection) ((int) section + 1);
                }
            }
            this.dbSection = section;
            this.btnWheelFilter.Visible = section == DbSection.Wheels;
            for (int i = 0; i < this.btnDbSections.Length; i++)
            {
                this.btnDbSections[i].Checked = i == (int) section;
                this.btnDbSections[i].Enabled = this.gameDb == null || this.HasSection((DbSection) i);
            }
            if (this.gameDb == null)
            {
                return;
            }
            this.lvDb.ListViewItemSorter = null;
            this.lvDb.Columns.Clear();
            this.dbItems.Clear();
            switch (section)
            {
                case DbSection.Cars:
                    this.AddDbColumns(new string[] { "Id", "Make", "Model", "Media name", "Year", "Class" }, new int[] { 40, 70, 110, 125, 38, 55 });
                    this.AddDlcColumn(this.gameDb.DlcCars);
                    foreach (object[] row in this.gameDb.Sql.Query(CarListSql).Rows)
                    {
                        this.dbItems.Add(this.CarItem(row));
                    }
                    this.dbItems.Sort(delegate(ListViewItem a, ListViewItem b)
                    {
                        int make = string.Compare(a.SubItems[1].Text, b.SubItems[1].Text, StringComparison.OrdinalIgnoreCase);
                        return (make != 0) ? make : string.Compare(a.SubItems[2].Text, b.SubItems[2].Text, StringComparison.OrdinalIgnoreCase);
                    });
                    break;

                case DbSection.Wheels:
                    this.AddDbColumns(new string[] { "Id", "Type", "Maker", "Name", "Media name", "Mass", "Price" }, new int[] { 40, 85, 85, 125, 115, 45, 50 });
                    this.FillWheelList();
                    break;

                case DbSection.Tracks:
                    this.AddDbColumns(new string[] { "Id", "Track", "Layout", "Media name", "Length (m)", "Reverse" }, new int[] { 40, 120, 110, 85, 60, 50 });
                    this.AddDlcColumn(this.gameDb.DlcTracks);
                    foreach (object[] row in this.gameDb.Sql.Query("SELECT id, MediaName, Length, IsReverse, ShortDisplayName, DisplayName FROM Tracks ORDER BY MediaName, id").Rows)
                    {
                        string track = this.gameDb.Text("Tracks", "ShortDisplayName", row[4]) ?? GameDatabase.FormatValue(row[1]);
                        this.dbItems.Add(NewItem(row[0], track, this.gameDb.Text("Tracks", "DisplayName", row[5]) ?? "", row[1], row[2], YesNoText(row[3]), DlcName(this.gameDb.DlcTracks, row[0])));
                    }
                    break;

                case DbSection.Events:
                    this.AddDbColumns(new string[] { "Id", "Event", "Level", "Races", "Class", "Tracks" }, new int[] { 40, 150, 40, 40, 40, 200 });
                    Dictionary<long, List<string>> tracks = new Dictionary<long, List<string>>();
                    if (this.gameDb.HasTable("Races"))
                    {
                        foreach (object[] row in this.gameDb.Sql.Query("SELECT EventId, TrackId FROM Races ORDER BY EventId, RaceNumber").Rows)
                        {
                            if (!(row[0] is long))
                            {
                                continue;
                            }
                            if (!tracks.ContainsKey((long) row[0]))
                            {
                                tracks[(long) row[0]] = new List<string>();
                            }
                            tracks[(long) row[0]].Add(this.gameDb.LookupName("track.short", row[1]) ?? GameDatabase.FormatValue(row[1]));
                        }
                    }
                    foreach (object[] row in this.gameDb.Sql.Query("SELECT Id, Level, NumberOfRaces, TargetClass, Name FROM Events ORDER BY Id").Rows)
                    {
                        List<string> names;
                        string list = (row[0] is long && tracks.TryGetValue((long) row[0], out names)) ? string.Join(", ", names.ToArray()) : "";
                        this.dbItems.Add(NewItem(row[0], this.gameDb.Text("Events", "Name", row[4]) ?? "", row[1], row[2], this.gameDb.LookupName("class.short", row[3]) ?? "", list));
                    }
                    break;

                case DbSection.Engines:
                    this.AddDbColumns(new string[] { "Id", "Engine", "Media name", "Config", "Cars" }, new int[] { 45, 200, 110, 90, 45 });
                    Dictionary<long, int> users = new Dictionary<long, int>();
                    if (this.gameDb.HasTable("List_UpgradeEngine"))
                    {
                        foreach (object[] row in this.gameDb.Sql.Query("SELECT EngineID, COUNT(DISTINCT Ordinal) FROM List_UpgradeEngine GROUP BY EngineID").Rows)
                        {
                            if (row[0] is long && row[1] is long)
                            {
                                users[(long) row[0]] = (int) (long) row[1];
                            }
                        }
                    }
                    bool hasConfig = this.gameDb.HasTable("List_EngineConfig");
                    string engineSql = hasConfig
                        ? "SELECT e.EngineID, e.EngineName, e.MediaName, c.EngineConfig FROM Data_Engine e LEFT JOIN List_EngineConfig c ON c.ConfigID = e.ConfigID ORDER BY e.EngineID"
                        : "SELECT EngineID, EngineName, MediaName, NULL FROM Data_Engine ORDER BY EngineID";
                    foreach (object[] row in this.gameDb.Sql.Query(engineSql).Rows)
                    {
                        int count;
                        string engineName = this.gameDb.Text("Data_Engine", "EngineName", row[1]) ?? GameDatabase.FormatValue(row[1]);
                        this.dbItems.Add(NewItem(row[0], engineName, row[2], row[3], (row[0] is long && users.TryGetValue((long) row[0], out count)) ? count : 0));
                    }
                    break;

                case DbSection.Makes:
                    this.AddDbColumns(new string[] { "Id", "Make", "Code", "Country", "Cars" }, new int[] { 45, 170, 110, 110, 45 });
                    Dictionary<long, int> makeCars = this.CountBy("Data_Car", "MakeID");
                    foreach (object[] row in this.gameDb.Sql.Query("SELECT m.ID, m.DisplayName, m.ManufacturerCode, c.Country FROM List_CarMake m LEFT JOIN List_Country c ON c.CountryID = m.CountryID ORDER BY m.DisplayName").Rows)
                    {
                        int cars;
                        string makeName = this.gameDb.Text("List_CarMake", "DisplayName", row[1]) ?? GameDatabase.FormatValue(row[1]);
                        string makeCountry = this.gameDb.Text("List_Country", "Country", row[3]) ?? GameDatabase.FormatValue(row[3]);
                        this.dbItems.Add(NewItem(row[0], makeName, row[2], makeCountry, (row[0] is long && makeCars.TryGetValue((long) row[0], out cars)) ? cars : 0));
                    }
                    break;

                case DbSection.Countries:
                    this.AddDbColumns(new string[] { "Id", "Country", "Region", "Makes" }, new int[] { 45, 170, 70, 55 });
                    Dictionary<long, int> countryMakes = this.CountBy("List_CarMake", "CountryID");
                    foreach (object[] row in this.gameDb.Sql.Query("SELECT CountryID, Country, RegionID FROM List_Country ORDER BY CountryID").Rows)
                    {
                        int makes;
                        string countryName = this.gameDb.Text("List_Country", "Country", row[1]) ?? GameDatabase.FormatValue(row[1]);
                        this.dbItems.Add(NewItem(row[0], countryName, row[2], (row[0] is long && countryMakes.TryGetValue((long) row[0], out makes)) ? makes : 0));
                    }
                    break;

                case DbSection.PartMakers:
                    this.AddDbColumns(new string[] { "Id", "Manufacturer", "Logo" }, new int[] { 45, 200, 200 });
                    foreach (object[] row in this.gameDb.Sql.Query("SELECT Id, PartManufacturer, LogoPath FROM List_PartManufacturer ORDER BY PartManufacturer").Rows)
                    {
                        this.dbItems.Add(NewItem(row[0], this.gameDb.Text("List_PartManufacturer", "PartManufacturer", row[1]) ?? GameDatabase.FormatValue(row[1]), row[2]));
                    }
                    break;


                case DbSection.Tables:
                    this.AddDbColumns(new string[] { "Table", "Rows" }, new int[] { 260, 70 });
                    foreach (string name in this.gameDb.TableNames)
                    {
                        ListViewItem item = NewItem(name, this.gameDb.Count(name, null));
                        item.Tag = name;
                        this.dbItems.Add(item);
                    }
                    break;
            }
            this.ShowDbContent(null);
            string search;
            this.dbSearchRestoring = true;
            this.txtDbSearch.Text = this.dbSearches.TryGetValue(section, out search) ? search : "";
            this.dbSearchRestoring = false;
            this.FilterDbList();
        }

        // Clears the search of the section shown (a jump to an item that has to be in the list).
        private void ClearDbSearch()
        {
            this.txtDbSearch.Text = "";
        }

        // A table's rows with the given filter; the table shown before keeps its own for when it comes back.
        private void ShowTableRows(string table, string filter)
        {
            if (this.gridTable.TableName != null)
            {
                this.tableFilters[this.gridTable.TableName] = this.gridTable.Filter ?? "";
            }
            this.gridTable.ShowRows(this.gameDb, table, filter);
        }

        // A part maker's LogoPath is only the decal's name (decal364): the picture is
        // GAME:\Media\UI\Textures\UI4\Logos\Car_Parts_Logos\decal364.xds (about 240 of 569 exist in FM4).
        private const string PartLogoSql = "'GAME:\\Media\\UI\\Textures\\UI4\\Logos\\Car_Parts_Logos\\' || LogoPath || '.xds'";

        private const string CarListSql = "SELECT Id, MakeID, MediaName, Year, ClassID, PerformanceIndex, DisplayName FROM Data_Car";

        // Make name, class letter and the PI number shown in game (PerformanceIndex 0.45 = B 550).
        private ListViewItem CarItem(object[] row)
        {
            string make = this.gameDb.LookupName("make.short", row[1]) ?? GameDatabase.FormatValue(row[1]);
            string pi = (row[5] is double || row[5] is long) ? " " + Math.Round((Convert.ToDouble(row[5]) * 1000) + 100).ToString(CultureInfo.InvariantCulture) : "";
            string model = this.gameDb.Text("Data_Car", "DisplayName", row[6]) ?? "";
            return NewItem(row[0], make, model, row[2], row[3], (this.gameDb.LookupName("class.short", row[4]) ?? "") + pi, DlcName(this.gameDb.DlcCars, row[0]));
        }

        // The "DLC" column of the cars and tracks lists, only when a package added rows.
        private void AddDlcColumn(Dictionary<long, string> marks)
        {
            if (marks.Count > 0)
            {
                this.lvDb.Columns.Add("DLC", 130);
            }
        }

        private static string DlcName(Dictionary<long, string> marks, object id)
        {
            string pack;
            return (id is long && marks.TryGetValue((long) id, out pack)) ? pack : "";
        }

        private void RefreshCarItem(long id)
        {
            SqliteDatabase.Result result = this.gameDb.Sql.Query(CarListSql + " WHERE Id = ?", id);
            if (result.Rows.Count == 0)
            {
                return;
            }
            ListViewItem updated = this.CarItem(result.Rows[0]);
            foreach (ListViewItem item in this.dbItems)
            {
                if (item.Text == updated.Text)
                {
                    for (int i = 1; i < updated.SubItems.Count; i++)
                    {
                        item.SubItems[i].Text = updated.SubItems[i].Text;
                    }
                }
            }
        }

        private static string YesNoText(object value)
        {
            return (value is long) ? (((long) value) != 0 ? "yes" : "") : "";
        }

        private void AddDbColumns(string[] titles, int[] widths)
        {
            for (int i = 0; i < titles.Length; i++)
            {
                this.lvDb.Columns.Add(titles[i], widths[i]);
            }
        }

        private static ListViewItem NewItem(params object[] values)
        {
            ListViewItem item = new ListViewItem(GameDatabase.FormatValue(values[0]));
            for (int i = 1; i < values.Length; i++)
            {
                item.SubItems.Add((values[i] == null) ? "" : GameDatabase.FormatValue(values[i]));
            }
            return item;
        }

        private void FilterDbList()
        {
            string find = this.txtDbSearch.Text.Trim();
            this.dbFilling = true;
            this.lvDb.BeginUpdate();
            this.lvDb.Items.Clear();
            List<ListViewItem> shown = new List<ListViewItem>();
            foreach (ListViewItem item in this.dbItems)
            {
                bool match = find.Length == 0;
                foreach (ListViewItem.ListViewSubItem sub in item.SubItems)
                {
                    match = match || sub.Text.IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0;
                }
                // Wheels: All, Stock or Aftermarket.
                if (match && this.dbSection == DbSection.Wheels && this.wheelFilter.Length > 0 && item.SubItems[1].Text != this.wheelFilter)
                {
                    match = false;
                }
                if (match)
                {
                    shown.Add(item);
                }
            }
            this.lvDb.Items.AddRange(shown.ToArray());
            this.lvDb.EndUpdate();
            this.dbFilling = false;
        }

        private class DbListSorter : IComparer
        {
            public readonly int Column;
            public readonly bool Descending;

            public DbListSorter(int column, bool descending)
            {
                this.Column = column;
                this.Descending = descending;
            }

            public int Compare(object x, object y)
            {
                string a = ((ListViewItem) x).SubItems[this.Column].Text;
                string b = ((ListViewItem) y).SubItems[this.Column].Text;
                double na;
                double nb;
                int result = (double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out na) && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out nb))
                    ? na.CompareTo(nb) : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                return this.Descending ? -result : result;
            }
        }

        #endregion

        #region Content

        private void ShowDbContent(Control content)
        {
            foreach (Control control in this.pnlDbContent.Controls)
            {
                Control shown = content ?? this.lblDbHint;
                control.Visible = control == shown || control.Contains(shown);
            }
        }

        private void ShowDbItem(ListViewItem item)
        {
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                switch (this.dbSection)
                {
                    case DbSection.Cars:
                        this.ShowCar(long.Parse(item.Text, CultureInfo.InvariantCulture));
                        break;

                    case DbSection.Wheels:
                        this.ShowWheel(long.Parse(item.Text, CultureInfo.InvariantCulture));
                        break;

                    case DbSection.Tracks:
                        this.ShowTrack(long.Parse(item.Text, CultureInfo.InvariantCulture));
                        break;

                    case DbSection.Events:
                        this.ShowEvent(long.Parse(item.Text, CultureInfo.InvariantCulture));
                        break;

                    case DbSection.Engines:
                        this.ShowEngineListItem(long.Parse(item.Text, CultureInfo.InvariantCulture));
                        break;

                    case DbSection.Makes:
                        long makeId = long.Parse(item.Text, CultureInfo.InvariantCulture);
                        this.ShowListItem(this.tabMake, this.pgMake, this.relMake, this.hdrMake, "List_CarMake", makeId, this.gameDb.MakeRelated(makeId), "DisplayName", "IconPath", "IconPathLight");
                        break;

                    case DbSection.Countries:
                        long countryId = long.Parse(item.Text, CultureInfo.InvariantCulture);
                        this.ShowListItem(this.tabCountry, this.pgCountry, this.relCountry, this.hdrCountry, "List_Country", countryId, this.gameDb.CountryRelated(countryId), "Country", "IconPath");
                        break;

                    case DbSection.PartMakers:
                        long partMakerId = long.Parse(item.Text, CultureInfo.InvariantCulture);
                        this.ShowListItem(this.tabPartMaker, this.pgPartMaker, this.relPartMaker, this.hdrPartMaker, "List_PartManufacturer", partMakerId, this.gameDb.PartMakerRelated(partMakerId), "PartManufacturer", PartLogoSql);
                        break;

                    case DbSection.Tables:
                        string filter;
                        this.ShowTableRows((string) item.Tag, this.tableFilters.TryGetValue((string) item.Tag, out filter) ? filter : null);
                        this.ShowDbContent(this.gridTable);
                        break;
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Game Database", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        // The row of a table by its id (first column).
        private DbRecord Record(string table, long id)
        {
            if (!this.gameDb.HasTable(table))
            {
                return null;
            }
            long rowId = this.gameDb.RowIdOf(table, id);
            if (rowId < 0 && this.gameDb.GetTable(table).PrimaryKey == null)
            {
                return null;
            }
            DbRecord record = new DbRecord(this.gameDb, table, rowId);
            if (!record.Exists)
            {
                return null;
            }
            record.Changed += new EventHandler(this.OnGameDbEdited);
            return record;
        }

        private void ShowCar(long id)
        {
            this.dbCarId = id;
            this.pgCar.SelectedObject = this.Record("Data_Car", id);
            this.UpdateCarHeader(id);

            // The car's engines: stock one first, then the swaps.
            this.cmbEngine.Items.Clear();
            foreach (long[] engine in this.gameDb.CarEngineIds(id))
            {
                string name = this.gameDb.LookupName("engine", engine[0]) ?? "?";
                this.cmbEngine.Items.Add(new EngineChoice(engine[0], string.Format("{0} - {1}{2}", engine[0], name, (engine[1] != 0) ? "  (stock)" : "  (swap)")));
            }
            if (this.cmbEngine.Items.Count > 0)
            {
                this.cmbEngine.SelectedIndex = 0;
            }
            else
            {
                this.pgEngine.SelectedObject = null;
                this.lblEngineUsage.Text = "No engine rows for this car.";
            }

            this.ShowCarTransmissions(id);

            long body = this.gameDb.CarBodyId(id);
            this.pgBody.SelectedObject = (body >= 0) ? this.Record("Data_CarBody", body) : null;
            this.relUpgrades.ShowItems(this.gameDb, this.gameDb.CarUpgrades(id));
            this.relColors.ShowItems(this.gameDb, this.gameDb.CarColors(id));
            this.relOther.ShowItems(this.gameDb, this.gameDb.CarOther(id));
            this.ShowDbContent(this.tabCar);
        }

        private class EngineChoice
        {
            public readonly long Id;
            private readonly string text;

            public EngineChoice(long id, string text)
            {
                this.Id = id;
                this.text = text;
            }

            public override string ToString()
            {
                return this.text;
            }
        }

        // Engines are shared between cars: say so, since an edit changes all of them.
        private void ShowEngine()
        {
            EngineChoice choice = this.cmbEngine.SelectedItem as EngineChoice;
            if (choice == null || this.gameDb == null)
            {
                return;
            }
            this.pgEngine.SelectedObject = this.Record("Data_Engine", choice.Id);
            this.ShowPower(choice.Id);
            List<string> names = this.gameDb.EngineUsers(choice.Id);
            if (names.Count <= 1)
            {
                this.lblEngineUsage.Text = (this.pgEngine.SelectedObject == null) ? "Engine not found in Data_Engine." : "Used only by this car.";
                this.lblEngineUsage.ForeColor = ForzaTheme.Text;
                return;
            }
            this.lblEngineUsage.Text = string.Format("Shared by {0} cars - edits change all of them", names.Count);
            this.lblEngineUsage.ToolTipText = string.Join("\n", names.ToArray());
            this.lblEngineUsage.ForeColor = ForzaTheme.Red;
        }

        #endregion

        #region Game files

        private PictureBox picCarThumb;
        private PictureBox picCarLogo;
        private Label lblCarFiles;
        private Button btnCarFiles;
        private Button btnCarDelete;
        private ToolTip carHeaderTip = new ToolTip();

        // Thumbnail and make logo (from their GAME:\ paths) and the way to the car's model files.
        private Control BuildCarHeader()
        {
            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 86;
            this.picCarThumb = new PictureBox();
            this.picCarThumb.Location = new Point(4, 4);
            this.picCarThumb.Size = new Size(156, 78);
            this.picCarThumb.SizeMode = PictureBoxSizeMode.Zoom;
            this.picCarThumb.BorderStyle = BorderStyle.FixedSingle;
            this.picCarLogo = new PictureBox();
            this.picCarLogo.Location = new Point(166, 4);
            this.picCarLogo.Size = new Size(78, 78);
            this.picCarLogo.SizeMode = PictureBoxSizeMode.Zoom;
            this.lblCarFiles = new Label();
            this.lblCarFiles.Location = new Point(252, 6);
            this.lblCarFiles.Size = new Size(300, 44);
            this.lblCarFiles.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            this.lblCarFiles.AutoEllipsis = true;
            this.btnCarFiles = new Button();
            this.btnCarFiles.Text = "Open Car Model";
            this.btnCarFiles.Location = new Point(252, 54);
            this.btnCarFiles.AutoSize = true;
            this.btnCarFiles.Click += delegate { this.OpenCarFiles(); };
            header.Controls.Add(this.picCarThumb);
            header.Controls.Add(this.picCarLogo);
            header.Controls.Add(this.lblCarFiles);
            header.Controls.Add(this.btnCarFiles);
            this.btnCarWheel = new Button();
            this.btnCarWheel.Text = "Stock Wheel";
            this.btnCarWheel.AutoSize = true;
            this.btnCarWheel.Click += delegate { this.JumpToWheel(); };
            header.Controls.Add(this.btnCarWheel);
            this.btnCarDelete = new Button();
            this.btnCarDelete.Text = "Delete Car...";
            this.btnCarDelete.AutoSize = true;
            this.btnCarDelete.Enabled = false;
            this.btnCarDelete.Click += delegate { this.DeleteCar(); };
            this.carHeaderTip.SetToolTip(this.btnCarDelete, "Takes a car that was added to this game (ported or duplicated) out of it: rows, strings and files. Cars of FM4 itself cannot be deleted.");
            header.Controls.Add(this.btnCarDelete);
            header.Layout += delegate
            {
                this.btnCarWheel.Location = new Point(this.btnCarFiles.Right + 6, this.btnCarFiles.Top);
                this.btnCarDelete.Location = new Point(this.btnCarWheel.Right + 6, this.btnCarFiles.Top);
            };
            header.Resize += delegate { this.lblCarFiles.Width = Math.Max(100, header.Width - this.lblCarFiles.Left - 6); };
            return header;
        }

        private void UpdateCarHeader(long id)
        {
            SqliteDatabase.Result result = this.gameDb.Sql.Query("SELECT Thumbnail, MakeID, MediaName FROM Data_Car WHERE Id = ?", id);
            object[] row = (result.Rows.Count > 0) ? result.Rows[0] : new object[3];
            row[1] = this.gameDb.LookupName("make.icon", row[1]);
            string root = this.GameRoot;
            object wheel = this.gameDb.Sql.Scalar("SELECT StockWheelID FROM Data_Car WHERE Id = ?", id);
            this.btnCarWheel.Enabled = wheel is long && this.gameDb.HasTable("List_Wheels");
            this.btnCarDelete.Enabled = false;
            SetPicture(this.picCarThumb, this.LoadGameImage(row[0] as string));
            SetPicture(this.picCarLogo, this.LoadGameImage(row[1] as string));
            string media = row[2] as string;
            ForzaZip.Entry own = this.OwnMedia("cars", media);
            if (own != null)
            {
                // A DLC (pack zip or development folder) carries its cars under its own media\cars: the database links to them.
                this.lblCarFiles.Text = string.Format("Car model: {0} (in this DLC)\nGAME:\\ = {1}", own.Name, root ?? "game folder not found");
                this.carHeaderTip.SetToolTip(this.lblCarFiles, this.lblCarFiles.Text);
                this.btnCarFiles.Enabled = true;
                return;
            }
            if (root == null)
            {
                this.lblCarFiles.Text = "Game folder not found. Open the game's base folder (the one that contains Media), or a folder inside it, to see the thumbnail, the logo and the car's model files.";
                this.btnCarFiles.Enabled = false;
                return;
            }
            string car = GamePaths.FindCar(root, media);
            this.lblCarFiles.Text = string.Format("Car model: {1}\nGAME:\\ = {0}", root,
                (car != null) ? car.Substring(root.Length).TrimStart('\\') : "Media\\Cars\\" + media + " not found (.zip or folder)");
            this.carHeaderTip.SetToolTip(this.lblCarFiles, this.lblCarFiles.Text);
            this.btnCarFiles.Enabled = car != null;
            this.btnCarDelete.Enabled = car != null && id > GameIdLimits.Max(ForzaGame.Motorsport4, "Data_Car", "Id");
        }

        private static void SetPicture(PictureBox box, Bitmap image)
        {
            Image old = box.Image;
            box.Image = image;
            if (old != null)
            {
                old.Dispose();
            }
        }

        // GAME: is the folder that contains Media, found from what is open.
        private string GameRoot
        {
            get
            {
                if (this.zip == null)
                {
                    return null;
                }
                // Inside a DLC package the work folder has only the pack's files: the shared textures, shaders and
                // camera presets are the source game's (Tools > Settings > Folders); the pack's database is MergeEntry.
                if (this.package != null && this.InPackage(this.zip.FilePath))
                {
                    ForzaGame source = GameInfo.FromTitleId(this.package.TitleId);
                    // FM4 is the port target (Tools > Port Car); the other games have their own Folders setting.
                    string game = (source == ForzaGame.Motorsport4) ? AppSettings.Get("CarPort.Target", "") : GameInfo.ConfiguredFolder(source);
                    if (!string.IsNullOrEmpty(game) && Directory.Exists(Path.Combine(game, "Media")))
                    {
                        return game;
                    }
                }
                // A DLC development folder (DlcFolderRoot) has only its own files: its base is FM4, the port target.
                if (this.DlcFolderRoot != null)
                {
                    string game = AppSettings.Get("CarPort.Target", "");
                    if (!string.IsNullOrEmpty(game) && Directory.Exists(Path.Combine(game, "Media")))
                    {
                        return game;
                    }
                }
                return GamePaths.FindRoot(this.zip.FilePath);
            }
        }

        // The merge database (db\patch\*_merge.slt, the highest number) of the pack zip open in a DLC package, or null.
        private ForzaZip.Entry MergeEntry()
        {
            if (this.package == null || this.zip == null || !this.InPackage(this.zip.FilePath))
            {
                return null;
            }
            ForzaZip.Entry found = null;
            foreach (ForzaZip.Entry entry in this.zip.Entries)
            {
                string name = (entry.FullPath ?? entry.Name).Replace('\\', '/');
                if (!entry.IsDirectory && name.IndexOf("media/db/patch/", StringComparison.OrdinalIgnoreCase) >= 0 && name.EndsWith("_merge.slt", StringComparison.OrdinalIgnoreCase))
                {
                    found = entry;
                }
            }
            return found;
        }

        private static string RelativeToFolder(string folder, string path)
        {
            string prefix = folder.EndsWith("\\") ? folder : folder + "\\";
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? path.Substring(prefix.Length).Replace('\\', '/') : null;
        }

        // The entry of a file on disk in the open folder (so pending edits are seen), or null.
        private ForzaZip.Entry EntryForFile(string path)
        {
            if (this.zip == null || !this.zip.IsFolder)
            {
                return null;
            }
            string relative = RelativeToFolder(this.zip.FilePath, path);
            return (relative == null) ? null : this.zip.Find(relative);
        }

        // A DLC pack zip (Media\DLCZips\36_pri_65.zip) holds the files its database names under its own media\
        // folder, mounted by the game at GAME:\Media: the open zip's entry for a GAME:\ path (or the .xds built
        // from a .tga/.dds/.png), else null. Only for a zip: an open folder is found through GameRoot.
        private ForzaZip.Entry OwnEntry(string gamePath)
        {
            if (!GamePaths.IsGamePath(gamePath))
            {
                return null;
            }
            string relative = this.OwnName(GamePaths.RelativePath(gamePath));
            if (relative == null)
            {
                return null;
            }
            ForzaZip.Entry entry = this.zip.Find(relative);
            string extension = Path.GetExtension(relative).ToLowerInvariant();
            if (entry == null && (extension == ".tga" || extension == ".dds" || extension == ".png"))
            {
                entry = this.zip.Find(Path.ChangeExtension(relative, ".xds"));
            }
            return (entry == null || entry.IsDirectory || entry.IsDeleted) ? null : entry;
        }

        // A DLC development folder: loose files laid out like a pack zip (Media\cars, Media\UI...) with the pack's
        // database in Media\db\patch\*_merge.slt and no gamedb.slt of its own. Its base is the main game
        // (GameRoot), its own files are looked up first. Null when the open folder is not one.
        private string DlcFolderRoot
        {
            get
            {
                if (this.zip == null || !this.zip.IsFolder || this.InPackage(this.zip.FilePath))
                {
                    return null;
                }
                string root = GamePaths.FindRoot(this.zip.FilePath);
                if (root == null)
                {
                    return null;
                }
                string db = Path.Combine(Path.Combine(root, "Media"), "db");
                string patch = Path.Combine(db, "patch");
                if (File.Exists(Path.Combine(db, "gamedb.slt")) || !Directory.Exists(patch) || Directory.GetFiles(patch, "*_merge.slt").Length == 0)
                {
                    return null;
                }
                return root;
            }
        }

        // A path below the DLC's root ("Media\cars\...") as the name of the open zip's entry, or null: the same
        // name in a pack zip, the name below the open folder in a DLC development folder.
        private string OwnName(string relative)
        {
            if (this.zip == null)
            {
                return null;
            }
            if (!this.zip.IsFolder)
            {
                return relative.Replace('\\', '/');
            }
            string dlc = this.DlcFolderRoot;
            return (dlc == null) ? null : RelativeToFolder(this.zip.FilePath, Path.Combine(dlc, relative.Replace('/', '\\')));
        }

        // media/<folder>/<name> of the DLC open (a pack zip or a DLC development folder): its main .carbin, else its
        // first .carbin, else its first file; or media/<folder>/<name>.zip. Null when the DLC does not carry it.
        private ForzaZip.Entry OwnMedia(string folder, string name)
        {
            string parent = string.IsNullOrEmpty(name) ? null : this.OwnName("media/" + folder);
            if (parent == null)
            {
                return null;
            }
            string prefix = parent + "/" + name + "/";
            ForzaZip.Entry found = this.zip.Find(prefix + name + ".carbin") ?? this.zip.Find(parent + "/" + name + ".zip");
            ForzaZip.Entry first = null;
            foreach (ForzaZip.Entry entry in this.zip.Entries)
            {
                if (found != null)
                {
                    break;
                }
                if (entry.IsDirectory || entry.IsDeleted || !entry.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (entry.Name.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase))
                {
                    found = entry;
                }
                else if (first == null)
                {
                    first = entry;
                }
            }
            return found ?? first;
        }

        // Shows an entry of the open zip (Back returns to where the jump started).
        private void OpenOwnEntry(ForzaZip.Entry entry)
        {
            string name = entry.Name;
            this.NavigateFrom(delegate { this.SelectEntry(name); });
        }

        private Bitmap LoadGameImage(string gamePath)
        {
            ForzaZip.Entry own = this.OwnEntry(gamePath);
            if (own != null)
            {
                try
                {
                    return ImageFromData(this.zip.Read(own), own.Name);
                }
                catch (Exception)
                {
                }
            }
            GamePaths.GameFile file = GamePaths.Locate(this.GameRoot, gamePath);
            if (file == null || (!file.InZip && !File.Exists(file.FilePath)))
            {
                return null;
            }
            try
            {
                // A loose file open in the tree is read from there, so pending edits show.
                ForzaZip.Entry entry = file.InZip ? null : this.EntryForFile(file.FilePath);
                byte[] data = (entry != null) ? this.zip.Read(entry) : GamePaths.Read(file);
                return ImageFromData(data, file.Name);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Bitmap ImageFromData(byte[] data, string name)
        {
            string extension = Path.GetExtension(name).ToLowerInvariant();
            if (extension == ".xds")
            {
                return new XdsTexture(data).GetLevelBitmap(0);
            }
            if (extension == ".xdc")
            {
                return new XdsTexture(XdcFile.Unpack(data)).GetLevelBitmap(0);
            }
            if (extension == ".tga")
            {
                return new TgaImage(data).ToBitmap();
            }
            using (MemoryStream stream = new MemoryStream(data))
            using (Image image = Image.FromStream(stream))
            {
                return new Bitmap(image);
            }
        }

        private void OpenGamePath(string gamePath)
        {
            if (!GamePaths.IsGamePath(gamePath))
            {
                return;
            }
            ForzaZip.Entry own = this.OwnEntry(gamePath);
            if (own != null)
            {
                // A file of the DLC pack zip open: shown in its tree.
                this.OpenOwnEntry(own);
                return;
            }
            string root = this.GameRoot;
            if (root == null)
            {
                MessageBox.Show(this, "To follow GAME:\\ paths, open the game's base folder (the one that contains Media) with File > Open Folder, or any folder inside it.",
                    "Game Files", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            GamePaths.GameFile file = GamePaths.Locate(root, gamePath);
            if (file == null)
            {
                MessageBox.Show(this, "Not found (as a file or inside a game zip):\n" + Path.Combine(root, GamePaths.RelativePath(gamePath)), "Game Files", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            if ((file.InZip || File.Exists(file.FilePath)) && !CanView(file.Name))
            {
                MessageBox.Show(this, string.Format("There is no viewer for {0} files yet. The file is:\n{1}", Path.GetExtension(file.Name), file), "Game Files", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (file.InZip)
            {
                // Inside a mounted zip: open the zip at that file (Back returns here).
                this.NavigateFrom(delegate
                {
                    this.OpenArchive(file.ZipPath);
                    if (this.zip != null && string.Equals(this.zip.FilePath, file.ZipPath, StringComparison.OrdinalIgnoreCase))
                    {
                        this.SelectEntry(file.EntryName);
                    }
                });
                return;
            }
            string path = file.FilePath;
            this.NavigateFrom(delegate
            {
                if (IsArchive(path))
                {
                    this.OpenArchive(path);
                }
                else
                {
                    this.OpenGameFile(path, root);
                }
            });
        }

        // Files the editor shows (the others are only listed).
        private static bool CanView(string path)
        {
            string name = path.ToLowerInvariant();
            foreach (string extension in new string[] { ".xds", ".xdc", ".tga", ".carbin", ".rmb.bin", ".xml", ".ini", ".cfg", ".txt", ".html", ".htm", ".slt", ".zip", ".fbf", ".bgf", ".bsg" })
            {
                if (name.EndsWith(extension))
                {
                    return true;
                }
            }
            return false;
        }

        // Shows a game file (or a car folder) in the tree, opening the whole game folder if needed.
        private void OpenGameFile(string path, string root)
        {
            if (this.zip != null && this.zip.IsFolder && this.SelectGameFile(this.zip.FilePath, path))
            {
                return;
            }
            if (MessageBox.Show(this, path + "\n\nis outside the open folder. Open the game folder\n" + root + "\nto browse it?", "Game Files", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            this.OpenFolder(root);
            if (this.zip != null && this.zip.IsFolder)
            {
                this.SelectGameFile(this.zip.FilePath, path);
            }
        }

        private bool SelectGameFile(string folder, string path)
        {
            string relative = RelativeToFolder(folder, path);
            if (relative == null)
            {
                return false;
            }
            ForzaZip.Entry entry = this.zip.Find(relative);
            if (entry == null && Directory.Exists(path))
            {
                // A car folder: its main model, else its first model, else its first file.
                string prefix = relative.TrimEnd('/') + "/";
                entry = this.zip.Find(prefix + Path.GetFileName(path) + ".carbin");
                foreach (ForzaZip.Entry candidate in this.zip.Entries)
                {
                    if (entry == null && candidate.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && candidate.Name.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase))
                    {
                        entry = candidate;
                    }
                }
                foreach (ForzaZip.Entry candidate in this.zip.Entries)
                {
                    if (entry == null && candidate.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        entry = candidate;
                    }
                }
            }
            if (entry == null)
            {
                return false;
            }
            this.SelectEntry(entry.Name);
            return true;
        }

        // Takes a car that was added to the game (ported, or made by Duplicate Car) out of the open game folder again
        // (CarUninstall): the plan first, in a window, then the user confirms; every file is backed up first.
        private void DeleteCar()
        {
            string root = this.GameRoot;
            string media = (this.gameDb == null || this.dbCarId < 0) ? null : this.gameDb.Sql.Scalar("SELECT MediaName FROM Data_Car WHERE Id = ?", this.dbCarId) as string;
            if (root == null || media == null)
            {
                return;
            }
            if (this.zip != null && this.zip.IsModified)
            {
                MessageBox.Show(this, "This window has changes not saved yet. Save or discard them first: deleting the car rewrites the game's database and string tables.", "Delete Car", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            CarUninstall uninstall = new CarUninstall();
            uninstall.TargetRoot = root;
            uninstall.MediaName = media;
            List<string> plan;
            try
            {
                plan = uninstall.Plan();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Delete Car", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string intro = "Delete " + media + " from\n" + root + "\n\nThis is what goes (only what belongs to this car and no other car uses). Every file is backed up first to\n" + uninstall.BackupFolder;
            if (!ConfirmText("Delete Car", intro, string.Join("\r\n", plan.ToArray())))
            {
                return;
            }
            if (CarPortForm.XeniaRunning())
            {
                MessageBox.Show(this, "Close Xenia first: it keeps the game's files open.", "Delete Car", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                uninstall.Report = new StringBuilder();
                uninstall.Run();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Delete Car", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            MessageBox.Show(this, media + " was deleted.", "Delete Car", MessageBoxButtons.OK, MessageBoxIcon.Information);
            this.ReloadAfterGameChange(root);
        }

        // A window with a text (a plan) to read and OK / Cancel; true on OK.
        private bool ConfirmText(string title, string intro, string text)
        {
            using (Form form = new Form())
            {
                form.Text = title;
                form.ClientSize = new Size(760, 520);
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.Sizable;
                form.MinimizeBox = false;
                form.ShowInTaskbar = false;
                Label label = new Label();
                label.Dock = DockStyle.Top;
                label.Height = 96;
                label.Padding = new Padding(8);
                label.Text = intro;
                TextBox box = new TextBox();
                box.Multiline = true;
                box.ReadOnly = true;
                box.ScrollBars = ScrollBars.Both;
                box.WordWrap = false;
                box.Dock = DockStyle.Fill;
                box.Font = new Font(FontFamily.GenericMonospace, 8.5f);
                box.Text = text;
                box.Select(0, 0);
                // A flow panel from the right: no coordinates, so the buttons stay in view whatever the window's size.
                FlowLayoutPanel buttons = new FlowLayoutPanel();
                buttons.Dock = DockStyle.Bottom;
                buttons.Height = 48;
                buttons.FlowDirection = FlowDirection.RightToLeft;
                buttons.Padding = new Padding(8, 8, 8, 0);
                Button cancel = new Button();
                cancel.Text = "Cancel";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.Size = new Size(90, 28);
                Button ok = new Button();
                ok.Text = "Delete";
                ok.DialogResult = DialogResult.OK;
                ok.Size = new Size(90, 28);
                buttons.Controls.Add(cancel);
                buttons.Controls.Add(ok);
                form.Controls.Add(box);
                form.Controls.Add(buttons);
                form.Controls.Add(label);
                form.CancelButton = cancel;
                ForzaTheme.Apply(form);
                return form.ShowDialog(this) == DialogResult.OK;
            }
        }

        private void OpenCarFiles()
        {
            string root = this.GameRoot;
            string media = (this.gameDb == null) ? null : this.gameDb.Sql.Scalar("SELECT MediaName FROM Data_Car WHERE Id = ?", this.dbCarId) as string;
            ForzaZip.Entry own = this.OwnMedia("cars", media);
            if (own != null)
            {
                this.OpenOwnEntry(own);
                return;
            }
            string car = GamePaths.FindCar(root, media);
            if (car == null)
            {
                return;
            }
            this.NavigateFrom(delegate
            {
                if (File.Exists(car))
                {
                    // A zip replaces the folder in the tree; Back returns to this car.
                    this.OpenArchive(car);
                }
                else
                {
                    this.OpenGameFile(car, root);
                }
            });
        }

        #endregion
    }
}
