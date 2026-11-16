using System;
using System.Drawing;
using System.Windows.Forms;

namespace ForzaStudio
{
    // Step E.1 of docs/UI_WORKFLOW.md: the File / Tools / Help menus become a bar of buttons at the top. Open (with
    // its kinds), Save, Save As, DLC (only when it applies), Tools (a panel of buttons), and on the right "?" and
    // Pay Respects. The keys are handled here (HandleTopBarKey), not by menu shortcuts.
    public partial class ArchiveEditorForm
    {
        private ToolStripMenuItem mnuDlcBar;
        private ToolStripMenuItem mnuDlcCarDb;
        private ToolStripMenuItem mnuDlcRebuild;
        private ToolStripMenuItem mnuDlcDeleteCar;
        private ToolStripMenuItem mnuDlcBaseDb;
        private ToolStripDropDown toolsPanel;

        // Brand, then these, then the menus still to move (Archive / Model / Texture, step E.2), then Back / Undo / Redo.
        private void AddTopBar()
        {
            ToolStripMenuItem open = new ToolStripMenuItem("Open ▾");
            open.ToolTipText = "Open an archive (Ctrl+O), a folder (Ctrl+Shift+O) or a DLC package";
            ToolStripMenuItem openArchive = new ToolStripMenuItem("Archive...", null, delegate { this.OpenArchiveDialog(); });
            openArchive.ShortcutKeyDisplayString = "Ctrl+O";
            ToolStripMenuItem openFolder = new ToolStripMenuItem("Folder...", null, delegate { this.OpenFolderDialog(); });
            openFolder.ShortcutKeyDisplayString = "Ctrl+Shift+O";
            open.DropDownItems.Add(openArchive);
            open.DropDownItems.Add(openFolder);
            open.DropDownItems.Add(new ToolStripMenuItem("DLC Package...", null, delegate { this.OpenPackageDialog(); }));
            open.DropDownItems.Add(new ToolStripSeparator());
            this.mnuClose = new ToolStripMenuItem("Close", null, delegate { this.CloseArchive(); });
            open.DropDownItems.Add(this.mnuClose);

            this.mnuSave = new ToolStripMenuItem("Save", null, delegate { this.SaveArchive(false); });
            this.mnuSave.ToolTipText = "Save  (Ctrl+S)";
            this.mnuSaveAs = new ToolStripMenuItem("Save As...", null, delegate { this.SaveArchive(true); });

            // The DLC commands: shown only with a DLC package open, or a game / DLC car open as a copy (mod pack).
            this.mnuDlcBar = new ToolStripMenuItem("DLC ▾");
            this.mnuDlcRebuild = new ToolStripMenuItem("Rebuild DLC Package", null, delegate { this.RebuildPackageCommand(); });
            this.mnuDlcDeleteCar = new ToolStripMenuItem("Delete Car from DLC Package...", null, delegate { this.DeletePackageCar(); });
            this.mnuDlcBaseDb = new ToolStripMenuItem("Base Game Database (disc 2 packs)", null, delegate { this.ShowBaseDatabase(); });
            this.mnuDlcBaseDb.ToolTipText = "For a package with cars but no database of its own (FM4 disc 2, FM3 second disc): the base game's gamedb.slt, read-only, linked to the pack's cars.";
            this.mnuDlcCarDb = new ToolStripMenuItem("Database of This Car", null, delegate { this.ShowModCarDatabase(); });
            this.mnuDlcCarDb.ToolTipText = "For a game or original DLC car open as a copy (its Save writes a mod pack): its database rows, edited here and saved into the mod pack with the car. The game's gamedb.slt is never written.";
            this.mnuDlcBar.DropDownItems.Add(this.mnuDlcRebuild);
            this.mnuDlcBar.DropDownItems.Add(this.mnuDlcDeleteCar);
            this.mnuDlcBar.DropDownItems.Add(this.mnuDlcBaseDb);
            this.mnuDlcBar.DropDownItems.Add(this.mnuDlcCarDb);
            this.mnuDlcBar.Visible = false;

            ToolStripMenuItem tools = new ToolStripMenuItem("Tools ▾");
            tools.Click += delegate { this.ShowToolsPanel(tools); };

            ToolStripMenuItem help = new ToolStripMenuItem("?");
            help.Alignment = ToolStripItemAlignment.Right;
            help.ToolTipText = "Help";
            ToolStripMenuItem commands = new ToolStripMenuItem("Viewport Commands", null, delegate { this.ToggleViewportHelp(); });
            commands.ShortcutKeyDisplayString = "F1";
            help.DropDownItems.Add(commands);
            help.DropDownItems.Add(new ToolStripMenuItem("About...", null, delegate { new AboutForm().ShowDialog(this); }));
            help.DropDownOpening += delegate { commands.Enabled = this.pnlModel != null && this.pnlModel.Visible; };

            // The original Forza Studio 4.6 window, kept as a tribute (untouched).
            ToolStripMenuItem respects = new ToolStripMenuItem("Pay Respects", null, delegate { new ForzaStudioForm().Show(); });
            respects.Alignment = ToolStripItemAlignment.Right;
            respects.ToolTipText = "The original Forza Studio 4.6 window";

            this.menu.Items.Add(open);
            this.menu.Items.Add(this.mnuSave);
            this.menu.Items.Add(this.mnuSaveAs);
            this.menu.Items.Add(this.mnuDlcBar);
            this.menu.Items.Add(tools);
            this.menu.Items.Add(new ToolStripSeparator());
            this.menu.Items.Add(respects);
            this.menu.Items.Add(help);
        }

        // Called with the title (an archive / package opened, saved or closed).
        private void UpdateDlcBar()
        {
            if (this.mnuDlcBar == null)
            {
                return;
            }
            bool package = this.package != null;
            bool carDb = this.zip != null && ModCarOf(this.zip.FilePath) != null;
            this.mnuDlcRebuild.Visible = package;
            this.mnuDlcDeleteCar.Visible = package;
            this.mnuDlcBaseDb.Visible = package;
            this.mnuDlcCarDb.Visible = carDb;
            this.mnuDlcBar.Visible = package || carDb;
        }

        private bool HandleTopBarKey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.O:
                    this.OpenArchiveDialog();
                    return true;
                case Keys.Control | Keys.Shift | Keys.O:
                    this.OpenFolderDialog();
                    return true;
                case Keys.Control | Keys.S:
                    if (this.mnuSave != null && this.mnuSave.Enabled)
                    {
                        this.SaveArchive(false);
                    }
                    return true;
            }
            return false;
        }

        // Tools: a panel of buttons under the Tools button, in groups. One click runs the tool and closes the panel.
        private void ShowToolsPanel(ToolStripItem anchor)
        {
            if (this.toolsPanel == null)
            {
                this.toolsPanel = this.BuildToolsPanel();
            }
            Rectangle bounds = anchor.Bounds;
            this.toolsPanel.Show(this.menu, new Point(bounds.Left, bounds.Bottom));
        }

        private ToolStripDropDown BuildToolsPanel()
        {
            const int Width = 300;
            ToolStripDropDown drop = new ToolStripDropDown();
            drop.Padding = new Padding(1);
            FlowLayoutPanel column = new FlowLayoutPanel();
            column.FlowDirection = FlowDirection.TopDown;
            column.WrapContents = false;
            column.AutoSize = true;
            column.Padding = new Padding(10, 4, 10, 10);

            Action<string> heading = delegate(string text)
            {
                Label label = new Label();
                label.Text = text.ToUpperInvariant();
                label.AutoSize = false;
                label.Size = new Size(Width, 22);
                label.TextAlign = ContentAlignment.BottomLeft;
                label.Font = new Font(ForzaTheme.Regular, FontStyle.Bold);
                label.ForeColor = ForzaTheme.Red;
                label.Margin = new Padding(0, column.Controls.Count == 0 ? 2 : 12, 0, 3);
                column.Controls.Add(label);
            };
            Func<string, int, EventHandler, Button> button = delegate(string text, int width, EventHandler click)
            {
                Button b = new Button();
                b.Text = text;
                b.Size = new Size(width, 28);
                b.Margin = new Padding(0, 2, 4, 2);
                b.TextAlign = ContentAlignment.MiddleLeft;
                b.Click += delegate
                {
                    drop.Close();
                    click(b, EventArgs.Empty);
                };
                return b;
            };
            Action<string, EventHandler> wide = delegate(string text, EventHandler click) { column.Controls.Add(button(text, Width, click)); };
            // "Import car into  FM4  FM3 ...": only the target game is chosen here; the source game is read from the
            // folder chosen in the window (user 2026-11-08). Targets without a conversion yet are greyed (PortPairs).
            Action<string, PortMode> perGame = delegate(string caption, PortMode mode)
            {
                Label label = new Label();
                label.Text = caption;
                label.AutoSize = false;
                label.Size = new Size(Width, 18);
                label.TextAlign = ContentAlignment.BottomLeft;
                label.Margin = new Padding(0, 4, 0, 1);
                column.Controls.Add(label);
                FlowLayoutPanel row = new FlowLayoutPanel();
                row.FlowDirection = FlowDirection.LeftToRight;
                row.WrapContents = true;
                row.AutoSize = true;
                row.MaximumSize = new Size(Width + 4, 0);
                row.Margin = new Padding(0);
                foreach (string target in PortPairs.Games)
                {
                    string t = target;
                    Button b = button(t, 56, delegate { this.ShowCarPort(mode, t, null); });
                    b.TextAlign = ContentAlignment.MiddleCenter;
                    b.Enabled = (mode == PortMode.Import) ? PortPairs.CanImportInto(t) : PortPairs.CanCreateDlc(t);
                    row.Controls.Add(b);
                }
                column.Controls.Add(row);
            };

            heading("Cars");
            perGame("Import car from another Forza, into:", PortMode.Import);
            perGame("Create DLC package for:", PortMode.Dlc);
            wide("Duplicate Car...", delegate { this.ShowDuplicateCar(); });
            wide("Change Car Sound...", delegate { this.ShowChangeSound(); });
            wide("Car Performance From Game...", delegate { this.ShowPerformanceFromGame(); });
            heading("Database");
            wide("Database SQL...", delegate { this.ShowSqlConsole(); });
            heading("Checks");
            wide("Check Model Format...", delegate { this.ShowModelFormatCheck(); });
            wide("Check and Validate DLC...", delegate { using (DlcValidatorForm form = new DlcValidatorForm()) { form.ShowDialog(this); } });
            wide("Validate DLC for Installation...", delegate { using (DlcInstallForm form = new DlcInstallForm()) { form.ShowDialog(this); } });
            wide("Set Generated Thumbnail...", delegate { using (SetThumbnailsForm form = new SetThumbnailsForm()) { form.ShowDialog(this); } });
            heading("Mod packs and settings");
            wide("Mod Packs (uninstall)...", delegate { this.ShowModPacks(); });
            wide("Settings...", delegate { this.ShowSettings(); });

            ForzaTheme.Apply(column);
            column.BackColor = ForzaTheme.Surface;
            drop.BackColor = ForzaTheme.Line;
            ToolStripControlHost host = new ToolStripControlHost(column);
            host.Margin = Padding.Empty;
            host.Padding = Padding.Empty;
            host.AutoSize = true;
            drop.Items.Add(host);
            return drop;
        }
    }
}
