namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Drawing;
    using System.IO;
    using System.Text;
    using System.Windows.Forms;

    // Main window: the whole zip (or game folder) on the left, a viewer/editor for the selected entry
    // on the right. Edits are kept in memory (marked with *) until the archive is saved.
    public partial class ArchiveEditorForm : Form
    {
        private const string PendingNodeName = "\0pending";

        private ForzaZip zip;
        private ForzaZip.Entry currentEntry;
        private string lastFolder;

        // Folder mode: the tree is filled one folder at a time, when it is expanded.
        private Dictionary<string, List<string>> folderDirs;
        private Dictionary<string, List<ForzaZip.Entry>> folderFiles;

        private MenuStrip menu;
        private ToolStripMenuItem mnuSave;
        private ToolStripMenuItem mnuSaveAs;
        private ToolStripMenuItem mnuClose;
        private StatusStrip status;
        private ToolStripStatusLabel lblStatus;
        private SplitContainer split;
        private TreeView tvArchive;
        private ContextMenuStrip ctxArchive;
        private Panel pnlContent;
        private Label lblEmpty;
        private TextBox txtContent;
        private Panel pnlInfo;
        private Label lblInfo;

        // The name shown in titles and dialogs (the exe is ForzaStudioEVO.exe).
        public const string AppName = "ForzaStudio EVO";

        // Name + version for titles: the version is AssemblyInformationalVersion (AssemblyInfo.cs), the only place to change it.
        public static readonly string AppTitle = AppName + " " + Application.ProductVersion;

        public ArchiveEditorForm()
        {
            this.Text = AppTitle;
            this.Size = new Size(1280, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.WindowState = FormWindowState.Maximized;
            this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            this.KeyPreview = true;

            this.BuildMenu();

            this.status = new StatusStrip();
            this.lblStatus = new ToolStripStatusLabel("Open a Forza .zip archive or a game folder (File menu).");
            this.lblStatus.Spring = true;
            this.lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            this.status.Items.Add(this.lblStatus);

            this.split = new SplitContainer();
            this.split.Dock = DockStyle.Fill;
            this.split.SplitterDistance = 280;
            this.split.FixedPanel = FixedPanel.Panel1;

            this.tvArchive = new TreeView();
            this.tvArchive.Dock = DockStyle.Fill;
            this.tvArchive.HideSelection = false;
            this.tvArchive.BeforeSelect += delegate(object sender, TreeViewCancelEventArgs e)
            {
                if (e.Node.Tag is ForzaZip.Entry && e.Node.Tag != this.currentEntry && !this.ConfirmXmlEdits())
                {
                    e.Cancel = true;
                }
            };
            this.tvArchive.AfterSelect += new TreeViewEventHandler(this.tvArchive_AfterSelect);
            this.tvArchive.BeforeExpand += delegate(object sender, TreeViewCancelEventArgs e) { this.LoadFolderNode(e.Node); };
            this.tvArchive.NodeMouseDoubleClick += delegate(object sender, TreeNodeMouseClickEventArgs e)
            {
                if (e.Node == this.tvArchive.SelectedNode)
                {
                    this.OpenSelectedArchive();
                }
            };
            this.tvArchive.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && this.CanOpenSelectedArchive)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    this.OpenSelectedArchive();
                }
                else if (e.KeyCode == Keys.Delete && e.Modifiers == Keys.None && this.CanDeleteSelection)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    this.DeleteSelected();
                }
            };
            this.tvArchive.NodeMouseClick += delegate(object sender, TreeNodeMouseClickEventArgs e)
            {
                if (e.Button == MouseButtons.Right)
                {
                    this.tvArchive.SelectedNode = e.Node;
                }
            };
            this.BuildArchiveMenu();
            this.split.Panel1.Controls.Add(this.tvArchive);

            this.pnlContent = new Panel();
            this.pnlContent.Dock = DockStyle.Fill;
            this.split.Panel2.Controls.Add(this.pnlContent);

            this.lblEmpty = new Label();
            this.lblEmpty.Dock = DockStyle.Fill;
            this.lblEmpty.TextAlign = ContentAlignment.MiddleCenter;
            this.lblEmpty.ForeColor = SystemColors.GrayText;
            this.lblEmpty.Text = "Select a file in the tree.";

            this.txtContent = new TextBox();
            this.txtContent.Dock = DockStyle.Fill;
            this.txtContent.Multiline = true;
            this.txtContent.ReadOnly = true;
            this.txtContent.ScrollBars = ScrollBars.Both;
            this.txtContent.WordWrap = false;
            this.txtContent.Font = new Font("Consolas", 9.75f);
            this.txtContent.BackColor = ForzaTheme.Surface;

            this.pnlInfo = new Panel();
            this.pnlInfo.Dock = DockStyle.Fill;
            this.lblInfo = new Label();
            this.lblInfo.Dock = DockStyle.Fill;
            this.lblInfo.TextAlign = ContentAlignment.MiddleCenter;
            this.pnlInfo.Controls.Add(this.lblInfo);

            this.BuildModelView();
            this.BuildTextureView();
            this.BuildXmlView();
            this.BuildShaderView();
            this.BuildGameDbView();
            this.BuildDashView();

            this.pnlContent.Controls.Add(this.lblEmpty);
            this.pnlContent.Controls.Add(this.txtContent);
            this.pnlContent.Controls.Add(this.pnlXml);
            this.pnlContent.Controls.Add(this.pnlShader);
            this.pnlContent.Controls.Add(this.pnlGameDb);
            this.pnlContent.Controls.Add(this.pnlInfo);
            this.pnlContent.Controls.Add(this.pnlModel);
            this.pnlContent.Controls.Add(this.pnlTexture);
            this.pnlContent.Controls.Add(this.pnlDash);
            this.ShowView(this.lblEmpty);

            this.BuildMusicControls();

            this.Controls.Add(this.split);
            this.Controls.Add(this.status);
            this.Controls.Add(this.menu);
            this.MainMenuStrip = this.menu;
            this.UpdateTitle();

            // The look of the game's menus.
            this.BackColor = ForzaTheme.Background;
            this.ForeColor = ForzaTheme.Text;
            this.Font = ForzaTheme.Regular;
            ForzaTheme.Apply(this);
            this.tvArchive.ContextMenuStrip.Renderer = ForzaTheme.Renderer;
        }

        // "FORZA STUDIO" at the left of the menu bar, Forza red and white like the game's title.
        private void AddBrand()
        {
            ToolStripLabel forza = new ToolStripLabel("FORZA");
            forza.Font = new Font("Segoe UI", 11f, FontStyle.Bold | FontStyle.Italic);
            forza.ForeColor = ForzaTheme.Red;
            forza.Margin = new Padding(6, 1, 0, 2);
            ToolStripLabel studio = new ToolStripLabel("STUDIO EVO");
            studio.Font = new Font("Segoe UI Light", 11f, FontStyle.Italic);
            studio.Margin = new Padding(0, 1, 18, 2);
            this.menu.Items.Insert(0, studio);
            this.menu.Items.Insert(0, forza);
        }

        #region Menus

        private void BuildMenu()
        {
            this.menu = new MenuStrip();

            ToolStripMenuItem file = new ToolStripMenuItem("&File");
            file.DropDownItems.Add(new ToolStripMenuItem("&Open Archive...", null, delegate { this.OpenArchiveDialog(); }, Keys.Control | Keys.O));
            file.DropDownItems.Add(new ToolStripMenuItem("Open &Folder...", null, delegate { this.OpenFolderDialog(); }, Keys.Control | Keys.Shift | Keys.O));
            file.DropDownItems.Add(new ToolStripMenuItem("Open DLC &Package...", null, delegate { this.OpenPackageDialog(); }));
            file.DropDownItems.Add(new ToolStripSeparator());
            this.mnuSave = new ToolStripMenuItem("&Save", null, delegate { this.SaveArchive(false); }, Keys.Control | Keys.S);
            this.mnuSaveAs = new ToolStripMenuItem("Save &As...", null, delegate { this.SaveArchive(true); });
            this.mnuClose = new ToolStripMenuItem("&Close", null, delegate { this.CloseArchive(); });
            file.DropDownItems.Add(this.mnuSave);
            file.DropDownItems.Add(this.mnuSaveAs);
            file.DropDownItems.Add(this.mnuClose);
            file.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem rebuild = new ToolStripMenuItem("&Rebuild DLC Package", null, delegate { this.RebuildPackageCommand(); });
            file.DropDownItems.Add(rebuild);
            ToolStripMenuItem deletePackageCar = new ToolStripMenuItem("Delete Car from DLC Package...", null, delegate { this.DeletePackageCar(); });
            file.DropDownItems.Add(deletePackageCar);
            file.DropDownOpening += delegate { rebuild.Enabled = this.package != null; deletePackageCar.Enabled = this.package != null; };
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, delegate { this.Close(); }));

            // The archive tree's file commands, also on its right-click menu.
            ToolStripMenuItem archive = new ToolStripMenuItem("&Archive");
            ToolStripMenuItem openSelected = new ToolStripMenuItem("&Open Selected Archive", null, delegate { this.OpenSelectedArchive(); });
            archive.DropDownItems.Add(openSelected);
            archive.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem addFile = new ToolStripMenuItem("&Add File...", null, delegate { this.AddFileToArchive(); }, Keys.Control | Keys.Shift | Keys.A);
            archive.DropDownItems.Add(addFile);
            ToolStripMenuItem newFolder = new ToolStripMenuItem("New &Folder...", null, delegate { this.NewArchiveFolder(); });
            archive.DropDownItems.Add(newFolder);
            ToolStripMenuItem copyDash = new ToolStripMenuItem("Copy &Dash From Another Car...", null, delegate { this.CopyDashFromCar(); });
            archive.DropDownItems.Add(copyDash);
            ToolStripMenuItem extract =new ToolStripMenuItem("&Extract Selected File...", null, delegate { this.ExtractSelected(); });
            ToolStripMenuItem replace = new ToolStripMenuItem("&Replace Selected File...", null, delegate { this.ReplaceSelected(); });
            ToolStripMenuItem revert = new ToolStripMenuItem("Re&vert Selected File", null, delegate { this.RevertSelected(); });
            archive.DropDownItems.Add(extract);
            archive.DropDownItems.Add(replace);
            archive.DropDownItems.Add(revert);
            ToolStripMenuItem deleteItem = new ToolStripMenuItem("&Delete Selected File or Folder...", null, delegate { this.DeleteSelected(); });
            deleteItem.ShortcutKeyDisplayString = "Del (in the tree)";
            ToolStripMenuItem undoDelete = new ToolStripMenuItem("&Undo Delete", null, delegate { this.UndoDeleteSelected(); });
            archive.DropDownItems.Add(deleteItem);
            archive.DropDownItems.Add(undoDelete);
            archive.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem extractAll = new ToolStripMenuItem("Extract A&ll...", null, delegate { this.ExtractAll(); });
            archive.DropDownItems.Add(extractAll);
            archive.DropDownOpening += delegate
            {
                ForzaZip.Entry entry = (this.zip != null) ? this.SelectedEntry : null;
                openSelected.Enabled = this.CanOpenSelectedArchive;
                addFile.Enabled = this.zip != null;
                newFolder.Enabled = this.zip != null;
                copyDash.Enabled = this.zip != null;
                extract.Enabled = entry != null;
                replace.Enabled = entry != null;
                revert.Enabled = entry != null && entry.IsModified;
                revert.Text = (entry != null && entry.IsAdded) ? "Remo&ve Added File" : "Re&vert Selected File";
                deleteItem.Enabled = this.CanDeleteSelection;
                undoDelete.Enabled = this.CanUndoDeleteSelection;
                extractAll.Enabled = this.zip != null;
            };

            ToolStripMenuItem model = new ToolStripMenuItem("&Model");
            ToolStripMenuItem exportChecked = new ToolStripMenuItem("Export &Checked Parts to OBJ...", null, delegate { this.ExportObj(); });
            model.DropDownItems.Add(exportChecked);
            model.DropDownItems.Add(new ToolStripSeparator());
            model.DropDownItems.Add(new ToolStripMenuItem("&Export Model to OBJ Folder...", null, delegate { this.ExportModelFolder(); }));
            model.DropDownItems.Add(new ToolStripMenuItem("&Import Model from OBJ Folder...", null, delegate { this.ImportModelFolder(); }));
            model.DropDownItems.Add(new ToolStripMenuItem("Export Model to &FBX...", null, delegate { this.ExportModelFbx(false); }));
            model.DropDownItems.Add(new ToolStripMenuItem("Import Model from F&BX...", null, delegate { this.ImportModelFbx(false); }));
            model.DropDownItems.Add(new ToolStripMenuItem("Export Model to &glTF...", null, delegate { this.ExportModelFbx(true); }));
            model.DropDownItems.Add(new ToolStripMenuItem("Import Model from gl&TF...", null, delegate { this.ImportModelFbx(true); }));
            model.DropDownItems.Add(new ToolStripSeparator());
            model.DropDownItems.Add(new ToolStripMenuItem("Make &Rim Scalable...", null, delegate { this.MakeRimScalable(true); }));
            model.DropDownItems.Add(new ToolStripMenuItem("Export &UV Map to TGA...", null, delegate { this.ExportUvMap(); }));
            model.DropDownOpening += delegate { exportChecked.Enabled = this.pnlModel.Visible && this.scene != null; };

            ToolStripMenuItem texture = new ToolStripMenuItem("Te&xture");
            texture.DropDownItems.Add(new ToolStripMenuItem("&New Texture from Image...", null, delegate { this.NewXdsFromImage(); }));
            texture.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem exportImage = new ToolStripMenuItem("&Export Image...", null, delegate { this.ExportTexturePng(); });
            ToolStripMenuItem importImage = new ToolStripMenuItem("&Import Image...", null, delegate { this.ImportTextureImage(); });
            texture.DropDownItems.Add(exportImage);
            texture.DropDownItems.Add(importImage);
            texture.DropDownOpening += delegate
            {
                bool shown = this.pnlTexture.Visible && (this.texture != null || this.tga != null);
                exportImage.Enabled = shown;
                importImage.Enabled = shown;
            };

            ToolStripMenuItem tools = new ToolStripMenuItem("&Tools");
            // Only the target game is chosen here; the source game is read from the folder chosen in the window
            // (user 2026-11-08). Targets without a conversion + window yet are greyed (PortPairs).
            ToolStripMenuItem importCar = new ToolStripMenuItem("&Import Car From Another Forza");
            ToolStripMenuItem createDlc = new ToolStripMenuItem("Create &DLC Package");
            foreach (string target in PortPairs.Games)
            {
                string t = target;
                ToolStripMenuItem into = new ToolStripMenuItem("For " + t + "...", null, delegate { this.ShowCarPort(PortMode.Import, t, null); });
                into.Enabled = PortPairs.CanImportInto(t);
                importCar.DropDownItems.Add(into);
                ToolStripMenuItem pack = new ToolStripMenuItem("For " + t + "...", null, delegate { this.ShowCarPort(PortMode.Dlc, t, null); });
                pack.Enabled = PortPairs.CanCreateDlc(t);
                createDlc.DropDownItems.Add(pack);
            }
            tools.DropDownItems.Add(importCar);
            tools.DropDownItems.Add(createDlc);
            tools.DropDownItems.Add(new ToolStripMenuItem("Duplicate &Car...", null, delegate { this.ShowDuplicateCar(); }));
            tools.DropDownItems.Add(new ToolStripMenuItem("Database &SQL...", null, delegate { this.ShowSqlConsole(); }));
            tools.DropDownItems.Add(new ToolStripMenuItem("Car &Performance From Game...", null, delegate { this.ShowPerformanceFromGame(); }));
            tools.DropDownItems.Add(new ToolStripMenuItem("Change Car &Sound...", null, delegate { this.ShowChangeSound(); }));
            tools.DropDownItems.Add(new ToolStripMenuItem("Check &Model Format...", null, delegate { this.ShowModelFormatCheck(); }));
            tools.DropDownItems.Add(new ToolStripMenuItem("Chec&k and Validate DLC...", null, delegate { using (DlcValidatorForm form = new DlcValidatorForm()) { form.ShowDialog(this); } }));
            tools.DropDownItems.Add(new ToolStripMenuItem("Validate DLC for &Installation...", null, delegate { using (DlcInstallForm form = new DlcInstallForm()) { form.ShowDialog(this); } }));
            tools.DropDownItems.Add(new ToolStripMenuItem("Set Generated &Thumbnail...", null, delegate { using (SetThumbnailsForm form = new SetThumbnailsForm()) { form.ShowDialog(this); } }));
            tools.DropDownItems.Add(new ToolStripSeparator());
            tools.DropDownItems.Add(new ToolStripMenuItem("&Settings...", null, delegate { this.ShowSettings(); }));
            tools.DropDownItems.Add(new ToolStripSeparator());
            // The original Forza Studio 4.6 window, kept as a tribute: Alt+T, F.
            tools.DropDownItems.Add(new ToolStripMenuItem("Pay Respects (&F)", null, delegate { new ForzaStudioForm().Show(); }));

            ToolStripMenuItem help = new ToolStripMenuItem("&Help");
            help.DropDownItems.Add(new ToolStripMenuItem("&About...", null, delegate { new AboutForm().ShowDialog(this); }));

            this.menu.Items.Add(file);
            this.menu.Items.Add(archive);
            this.menu.Items.Add(model);
            this.menu.Items.Add(texture);
            this.menu.Items.Add(tools);
            this.menu.Items.Add(help);
            this.mnuBackBar = new ToolStripMenuItem("◀ Back", null, delegate { this.GoBack(); });
            this.menu.Items.Add(this.mnuBackBar);
            this.AddBrand();
        }

        // Tools > Settings: folders, Port.ini, viewer; the music menu follows the new choices.
        private void ShowSettings()
        {
            using (SettingsForm settings = new SettingsForm())
            {
                if (settings.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                this.mnuMusicShuffle.Checked = settings.MusicShuffle;
                this.mnuMusicAutoPlay.Checked = settings.MusicAutoPlay;
                this.SetStatus("Settings saved.");
            }
        }

        private void BuildArchiveMenu()
        {
            this.ctxArchive = new ContextMenuStrip();
            ToolStripMenuItem extract = new ToolStripMenuItem("Extract File...", null, delegate { this.ExtractSelected(); });
            ToolStripMenuItem replace = new ToolStripMenuItem("Replace File...", null, delegate { this.ReplaceSelected(); });
            ToolStripMenuItem revert = new ToolStripMenuItem("Revert File", null, delegate { this.RevertSelected(); });
            ToolStripMenuItem add = new ToolStripMenuItem("Add File Here...", null, delegate { this.AddFileToArchive(); });
            this.ctxArchive.Items.Add(extract);
            this.ctxArchive.Items.Add(replace);
            this.ctxArchive.Items.Add(revert);
            ToolStripMenuItem delete = new ToolStripMenuItem("Delete...", null, delegate { this.DeleteSelected(); });
            ToolStripMenuItem undoDelete = new ToolStripMenuItem("Undo Delete", null, delegate { this.UndoDeleteSelected(); });
            this.ctxArchive.Items.Add(delete);
            this.ctxArchive.Items.Add(undoDelete);
            this.ctxArchive.Items.Add(new ToolStripSeparator());
            this.ctxArchive.Items.Add(add);
            this.ctxArchive.Items.Add(new ToolStripMenuItem("New Folder Here...", null, delegate { this.NewArchiveFolder(); }));
            this.ctxArchive.Opening += delegate(object sender, CancelEventArgs e)
            {
                if (this.zip == null)
                {
                    e.Cancel = true;
                    return;
                }
                ForzaZip.Entry entry = this.SelectedEntry;
                extract.Enabled = entry != null;
                replace.Enabled = entry != null;
                revert.Enabled = entry != null && entry.IsModified;
                revert.Text = (entry != null && entry.IsAdded) ? "Remove Added File" : "Revert File";
                delete.Enabled = this.CanDeleteSelection;
                delete.Text = (entry != null) ? "Delete File..." : "Delete Folder...";
                undoDelete.Enabled = this.CanUndoDeleteSelection;
            };
            this.tvArchive.ContextMenuStrip = this.ctxArchive;
        }

        #endregion

        // The port window works on the FM4 folder open here, if any (its target box locked), and an
        // install or uninstall there reloads this window, so the change shows at once.
        private void ShowCarPort(PortMode mode, string targetGame, string sourceGame)
        {
            if (targetGame == "FM3")
            {
                new CarPortFm3Form(mode).Show(this);
                return;
            }
            string root = (this.zip == null) ? null : GamePaths.FindRoot(this.zip.FilePath);
            bool fm4 = root != null && GameInfo.Detect(root) == ForzaGame.Motorsport4;
            CarPortForm port = new CarPortForm(fm4 ? root : null, mode);
            if (mode == PortMode.Import)
            {
                port.Text = "Import Car From Another Forza > " + targetGame;
            }
            if (fm4)
            {
                port.MainWindowUnsaved = delegate { return this.zip != null && this.zip.IsModified; };
                port.GameChanged += delegate { this.ReloadAfterGameChange(root); };
            }
            port.Show(this);
        }

        // Tools > Database SQL: on the gamedb.slt open in the database view.
        private void ShowSqlConsole()
        {
            if (this.gameDb == null)
            {
                MessageBox.Show(this, "Open the game database (Media\\db\\gamedb.slt) first.", "Database SQL", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (SqlConsoleForm form = new SqlConsoleForm(this.gameDb))
            {
                form.Changed += delegate
                {
                    this.OnGameDbEdited(null, EventArgs.Empty);
                    this.SelectDbSection(this.dbSection);
                };
                form.ShowDialog(this);
            }
        }

        // Tools > Car Performance From Game: the database open in the database view (the game's, or a DLC package's merge database).
        private void ShowPerformanceFromGame()
        {
            if (this.gameDb == null || !this.gameDb.HasTable("Data_Car"))
            {
                MessageBox.Show(this, "Open a database with the car first (Media\\db\\gamedb.slt, or the pack's database).", "Car performance from the game", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (PerformanceFromGameForm form = new PerformanceFromGameForm(this.gameDb, 0))
            {
                form.Changed += delegate
                {
                    this.OnGameDbEdited(null, EventArgs.Empty);
                    this.SelectDbSection(this.dbSection);
                };
                form.ShowDialog(this);
            }
        }

        // Tools > Change Car Sound: the database open in the database view (gamedb.slt, or a DLC pack's merge database).
        private void ShowChangeSound()
        {
            if (this.gameDb == null || !this.gameDb.HasTable("Data_Car") || !this.gameDb.HasTable("Data_Engine"))
            {
                // Nothing suitable open: the window opens its own source (game folder, DLC package or folder) and saves it.
                using (ChangeSoundForm alone = new ChangeSoundForm(AppSettings.Get("CarPort.Target", "")))
                {
                    alone.ShowDialog(this);
                }
                return;
            }
            using (ChangeSoundForm form = new ChangeSoundForm(this.gameDb, AppSettings.Get("CarPort.Target", ""), this.GameRoot, this.packageFolder))
            {
                form.Changed += delegate
                {
                    this.OnGameDbEdited(null, EventArgs.Empty);
                    this.SelectDbSection(this.dbSection);
                };
                form.ShowDialog(this);
            }
        }

        // Tools > Duplicate Car: the FM4 folder of what is open here (or the port window's target folder).
        private void ShowDuplicateCar()
        {
            string root = (this.zip == null) ? null : GamePaths.FindRoot(this.zip.FilePath);
            if (root == null || GameInfo.Detect(root) != ForzaGame.Motorsport4)
            {
                string saved = AppSettings.Get("CarPort.Target", "");
                root = (saved.Length > 0 && GameInfo.Detect(saved) == ForzaGame.Motorsport4) ? saved : null;
            }
            if (root == null)
            {
                MessageBox.Show(this, "Open a file of the Forza Motorsport 4 folder first (the copy is made in that folder).", "Duplicate Car", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string xenia = AppSettings.Get(SettingsForm.XeniaKey, "");
            string content = null;
            try
            {
                content = (xenia.Length > 0 && Directory.Exists(xenia)) ? DlcPackage.XeniaContent(xenia) : null;
            }
            catch (IOException)
            {
            }
            using (DuplicateCarForm form = new DuplicateCarForm(root, content))
            {
                form.ShowDialog(this);
                if (form.CreatedName != null)
                {
                    this.ReloadAfterGameChange(root);
                }
            }
        }

        // What is open here, read again from disk after the port changed the game folder; the same
        // file stays selected. Only when it still belongs to that game folder.
        private void ReloadAfterGameChange(string root)
        {
            if (this.zip == null || this.zip.IsModified || !string.Equals(GamePaths.FindRoot(this.zip.FilePath), root, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            string path = this.zip.FilePath;
            bool folder = this.zip.IsFolder;
            ForzaZip.Entry selected = this.SelectedEntry;
            string name = (selected == null) ? null : selected.Name;
            if (folder ? !Directory.Exists(path) : !File.Exists(path))
            {
                this.CloseArchiveCore();
                this.UpdateTitle();
                this.SetStatus(path + " is no longer in the game folder (uninstalled): closed.");
                return;
            }
            if (folder)
            {
                this.OpenFolder(path);
            }
            else
            {
                this.OpenArchive(path);
            }
            if (name != null && this.zip != null && this.zip.Find(name) != null)
            {
                this.SelectEntry(name);
            }
            this.SetStatus("Reloaded " + path + " after the port changed the game folder.");
        }

        #region Archive

        private ForzaZip.Entry SelectedEntry
        {
            get { return (this.tvArchive.SelectedNode == null) ? null : (this.tvArchive.SelectedNode.Tag as ForzaZip.Entry); }
        }

        // A .zip selected in a game folder (double-click, Enter or Archive > Open Selected Archive).
        private bool CanOpenSelectedArchive
        {
            get
            {
                ForzaZip.Entry entry = this.SelectedEntry;
                return this.zip != null && this.zip.IsFolder && entry != null && IsArchive(entry.Name);
            }
        }

        private void OpenSelectedArchive()
        {
            if (this.CanOpenSelectedArchive)
            {
                ForzaZip.Entry entry = this.SelectedEntry;
                this.NavigateFrom(delegate { this.OpenArchive(entry.FullPath); });
            }
        }

        public void OpenArchive(string path)
        {
            // An Xbox 360 package (the DLC file as the console stores it, no extension).
            if (!IsArchive(path) && File.Exists(path) && StfsReader.IsPackage(path))
            {
                this.OpenPackage(path);
                return;
            }
            // A loose game file (media\db\gamedb.slt): open its folder and select it.
            if (!IsArchive(path) && File.Exists(path) && Path.GetExtension(path).Equals(".slt", StringComparison.OrdinalIgnoreCase))
            {
                this.OpenFolder(Path.GetDirectoryName(Path.GetFullPath(path)));
                if (this.zip != null && this.zip.IsFolder)
                {
                    this.SelectEntry(Path.GetFileName(path));
                }
                return;
            }
            if (!this.ConfirmDiscard())
            {
                return;
            }
            // Any other loose file (a single .xds...): on its own.
            if (!IsArchive(path) && File.Exists(path))
            {
                this.OpenSingleFile(path);
                return;
            }
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                ForzaZip opened = new ForzaZip(path);
                this.CloseArchiveCore();
                this.zip = opened;
                this.PopulateTree();
                this.SetStatus(string.Format("Opened {0} ({1} files)", Path.GetFileName(path), this.zip.Entries.Count));
                Cursor.Current = Cursors.Default;
                this.CheckWheelScaleOnOpen();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Open Archive", MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
                this.UpdateTitle();
            }
        }

        private void OpenArchiveDialog()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "Forza archive, game database or texture (*.zip;*.slt;*.xds)|*.zip;*.slt;*.xds|All files (*.*)|*.*";
                if (ShowRemembering(dialog, this, "OpenArchive"))
                {
                    this.OpenArchive(dialog.FileName);
                }
            }
        }

        // A folder of uncompressed game files (the disc without zips). Edits are saved file by file.
        public void OpenFolder(string path)
        {
            if (path == null || !this.ConfirmDiscard())
            {
                return;
            }
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                this.SetStatus("Reading " + path + "...");
                ForzaZip opened = ForzaZip.OpenFolder(path);
                this.CloseArchiveCore();
                this.zip = opened;
                this.lastFolder = opened.FilePath;
                if (!this.InPackage(opened.FilePath))
                {
                    AppSettings.Set("OpenFolder", opened.FilePath);
                }
                this.PopulateTree();
                this.SetStatus(string.Format("Opened folder {0} ({1} files)", opened.FilePath, this.zip.Entries.Count));
                this.StartMusicFor(opened.FilePath);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Open Folder", MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
                this.UpdateTitle();
            }
        }

        // File dialogs start where the same kind of file was last picked (settings.txt, key Dir.<key>).
        private static bool ShowRemembering(FileDialog dialog, IWin32Window owner, string key)
        {
            string directory = AppSettings.Get("Dir." + key, null);
            if (directory != null && Directory.Exists(directory))
            {
                dialog.InitialDirectory = directory;
            }
            if (dialog.ShowDialog(owner) != DialogResult.OK)
            {
                return false;
            }
            AppSettings.Set("Dir." + key, Path.GetDirectoryName(dialog.FileName));
            return true;
        }

        private void OpenFolderDialog()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select a game folder (for example a car folder or the folder that contains them):";
                dialog.ShowNewFolderButton = false;
                string start = (this.lastFolder != null && Directory.Exists(this.lastFolder)) ? this.lastFolder : AppSettings.Get("OpenFolder", null);
                if (start != null && Directory.Exists(start))
                {
                    dialog.SelectedPath = start;
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    this.OpenFolder(dialog.SelectedPath);
                }
            }
        }

        private string SourceName
        {
            get
            {
                string name = Path.GetFileName(this.zip.FilePath);
                return (name.Length > 0) ? name : this.zip.FilePath;
            }
        }

        private static bool IsArchive(string name)
        {
            return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        }

        private void PopulateTree()
        {
            if (this.zip.IsFolder)
            {
                this.PopulateFolderTree();
                return;
            }
            this.tvArchive.BeginUpdate();
            this.tvArchive.Nodes.Clear();
            TreeNode root = new TreeNode(Path.GetFileName(this.zip.FilePath));
            List<ForzaZip.Entry> entries = new List<ForzaZip.Entry>(this.zip.Entries);
            entries.Sort(delegate(ForzaZip.Entry a, ForzaZip.Entry b)
            {
                int depth = a.Name.Split('/').Length.CompareTo(b.Name.Split('/').Length);
                return (depth != 0) ? -depth : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            foreach (ForzaZip.Entry entry in entries)
            {
                if (entry.IsDirectory)
                {
                    continue;
                }
                string[] parts = entry.Name.Split('/');
                TreeNodeCollection nodes = root.Nodes;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    TreeNode folder = nodes[parts[i]];
                    if (folder == null)
                    {
                        folder = nodes.Add(parts[i], parts[i]);
                    }
                    nodes = folder.Nodes;
                }
                TreeNode node = new TreeNode();
                node.Tag = entry;
                this.SetNodeText(node, entry);
                node.ToolTipText = string.Format("{0} bytes", entry.UncompressedSize);
                nodes.Add(node);
            }
            if (this.virtualFoldersOf == this.zip)
            {
                foreach (string virtualFolder in this.virtualFolders)
                {
                    TreeNodeCollection nodes = root.Nodes;
                    foreach (string part in virtualFolder.Trim('/').Split('/'))
                    {
                        TreeNode folder = nodes[part] ?? nodes.Add(part, part);
                        nodes = folder.Nodes;
                    }
                }
            }
            this.tvArchive.Nodes.Add(root);
            root.Expand();
            this.tvArchive.EndUpdate();
        }

        // Folder nodes carry their relative path ("" or "a/b/") as Tag and get a placeholder child
        // until they are expanded.
        private void PopulateFolderTree()
        {
            this.folderDirs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            this.folderFiles = new Dictionary<string, List<ForzaZip.Entry>>(StringComparer.OrdinalIgnoreCase);
            this.folderDirs[""] = new List<string>();
            this.folderFiles[""] = new List<ForzaZip.Entry>();
            foreach (ForzaZip.Entry entry in this.zip.Entries)
            {
                int slash = entry.Name.LastIndexOf('/');
                string dir = (slash < 0) ? "" : entry.Name.Substring(0, slash + 1);
                this.AddFolder(dir);
                this.folderFiles[dir].Add(entry);
            }
            this.AddEmptyFolders();
            this.tvArchive.BeginUpdate();
            this.tvArchive.Nodes.Clear();
            bool packageRoot = this.package != null && string.Equals(this.zip.FilePath.TrimEnd('\\'), this.packageFolder, StringComparison.OrdinalIgnoreCase);
            TreeNode root = new TreeNode(packageRoot ? this.PackageRootLabel : this.SourceName);
            root.Tag = "";
            root.ToolTipText = packageRoot ? this.package.Path + "\nwork folder: " + this.zip.FilePath : this.zip.FilePath;
            this.FillFolderNode(root);
            this.tvArchive.Nodes.Add(root);
            root.Expand();
            this.tvArchive.EndUpdate();
        }

        private void AddFolder(string dir)
        {
            if (this.folderDirs.ContainsKey(dir))
            {
                return;
            }
            this.folderDirs[dir] = new List<string>();
            this.folderFiles[dir] = new List<ForzaZip.Entry>();
            int slash = dir.LastIndexOf('/', dir.Length - 2);
            string parent = (slash < 0) ? "" : dir.Substring(0, slash + 1);
            this.AddFolder(parent);
            this.folderDirs[parent].Add(dir);
        }

        private void FillFolderNode(TreeNode node)
        {
            string dir = (string) node.Tag;
            node.Nodes.Clear();
            List<string> dirs = this.folderDirs[dir];
            dirs.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string sub in dirs)
            {
                TreeNode child = new TreeNode(sub.Substring(dir.Length).TrimEnd('/'));
                child.Tag = sub;
                child.Nodes.Add(PendingNodeName, "...");
                node.Nodes.Add(child);
            }
            foreach (ForzaZip.Entry entry in this.folderFiles[dir])
            {
                TreeNode child = new TreeNode();
                child.Tag = entry;
                child.ToolTipText = string.Format("{0:N0} bytes", entry.UncompressedSize);
                this.SetNodeText(child, entry);
                node.Nodes.Add(child);
            }
        }

        private void LoadFolderNode(TreeNode node)
        {
            if (node.Nodes.Count == 1 && node.Nodes[0].Name == PendingNodeName)
            {
                this.FillFolderNode(node);
            }
        }

        // Finds the tree node of an entry, loading the folders on its path in folder mode.
        private TreeNode FindEntryNode(ForzaZip.Entry entry)
        {
            if (entry == null)
            {
                return null;
            }
            if (this.zip.IsFolder && this.tvArchive.Nodes.Count > 0)
            {
                TreeNode node = this.tvArchive.Nodes[0];
                string[] parts = entry.Name.Split('/');
                string dir = "";
                for (int i = 0; i < parts.Length - 1 && node != null; i++)
                {
                    dir += parts[i] + "/";
                    this.LoadFolderNode(node);
                    TreeNode next = null;
                    foreach (TreeNode child in node.Nodes)
                    {
                        if (string.Equals(child.Tag as string, dir, StringComparison.OrdinalIgnoreCase))
                        {
                            next = child;
                            break;
                        }
                    }
                    node = next;
                }
                if (node == null)
                {
                    return null;
                }
                this.LoadFolderNode(node);
                foreach (TreeNode child in node.Nodes)
                {
                    if (child.Tag == entry)
                    {
                        return child;
                    }
                }
                return null;
            }
            return FindNode(this.tvArchive.Nodes, entry);
        }

        private void RefreshNode(TreeNode node)
        {
            ForzaZip.Entry entry = node.Tag as ForzaZip.Entry;
            if (entry == null)
            {
                return;
            }
            this.SetNodeText(node, entry);
            this.UpdateTitle();
        }

        private void SetNodeText(TreeNode node, ForzaZip.Entry entry)
        {
            string name = entry.Name.Substring(entry.Name.LastIndexOf('/') + 1);
            if (entry.IsDeleted)
            {
                node.Text = name + " (deleted)";
                node.NodeFont = new Font(this.tvArchive.Font, FontStyle.Strikeout);
                node.ForeColor = Color.Firebrick;
                return;
            }
            node.ForeColor = Color.Empty;
            node.Text = entry.IsAdded ? (name + " +") : (entry.IsModified ? (name + " *") : name);
            node.NodeFont = entry.IsModified ? new Font(this.tvArchive.Font, FontStyle.Bold) : null;
        }

        private bool ConfirmDiscard()
        {
            if (!this.ConfirmXmlEdits())
            {
                return false;
            }
            if (this.zip == null || !this.zip.IsModified)
            {
                return true;
            }
            DialogResult result = MessageBox.Show(this, "Save changes to " + this.SourceName + "?", AppName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (result == DialogResult.Cancel)
            {
                return false;
            }
            return (result == DialogResult.No) || this.SaveArchive(false);
        }

        private void CloseArchive()
        {
            if (this.ConfirmDiscard())
            {
                this.CloseArchiveCore();
                this.UpdateTitle();
            }
        }

        private void CloseArchiveCore()
        {
            this.ClearPreviewCache();
            this.currentEntry = null;
            this.ClearModel();
            this.ClearTexture();
            this.tvArchive.Nodes.Clear();
            this.zip = null;
            this.CloseGameDb();
            this.xmlEntry = null;
            this.xmlDirty = false;
            this.folderDirs = null;
            this.folderFiles = null;
            this.ShowView(this.lblEmpty);
        }

        // Backups go to a "bak" folder next to the exe, so they travel with the tool:
        // bak\<date>_bak_<file name>. Each save keeps the version it overwrites.
        private static string BackupFolder
        {
            get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "bak"); }
        }

        private static string BackupStamp()
        {
            return DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        }

        // null when [Backup] Enabled is off in ForzaStudio.ini.
        private static string BackupFile(string source, string name, string stamp)
        {
            if (!BackupPolicy.Enabled)
            {
                return null;
            }
            Directory.CreateDirectory(BackupFolder);
            string target = Path.Combine(BackupFolder, stamp + "_bak_" + name);
            File.Copy(source, target, true);
            return target;
        }

        // A game zip that was rewritten: its line in GAME:\Media\zipmanifest.xml gets the new central
        // directory position (the manifest is backed up first). False if the game does not list it.
        private bool UpdateZipManifest(string zipPath, string stamp)
        {
            string root = GamePaths.FindRoot(zipPath);
            string manifest = ZipManifest.PathFor(root);
            string gamePath = (root == null) ? null : ZipManifest.GamePath(root, Path.GetFullPath(zipPath));
            if (gamePath == null || !ZipManifest.Lists(manifest, gamePath))
            {
                return false;
            }
            BackupFile(manifest, "zipmanifest.xml", stamp);
            return ZipManifest.Update(manifest, gamePath, zipPath);
        }

        private bool SaveFolder()
        {
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                List<ForzaZip.Entry> modified = this.zip.ModifiedEntries;
                if (modified.Count == 0)
                {
                    this.SetStatus("Nothing to save.");
                    return true;
                }
                this.SetStatus(string.Format("Saving {0} file(s)...", modified.Count));
                string stamp = BackupStamp();
                foreach (ForzaZip.Entry entry in modified)
                {
                    // A deleted file goes to the Recycle Bin, not to bak\.
                    if (!entry.IsDeleted && File.Exists(entry.FullPath))
                    {
                        BackupFile(entry.FullPath, this.SourceName + "_" + entry.Name.Replace('/', '_'), stamp);
                    }
                }
                List<ForzaZip.Entry> saved = this.zip.SaveFolder();
                List<ForzaZip.Entry> removed = this.RemoveDeletedFiles(stamp);
                this.packagePending |= (saved.Count > 0 || removed.Count > 0) && this.InPackage(this.zip.FilePath);
                if (removed.Count > 0)
                {
                    this.currentEntry = null;
                    this.PopulateTree();
                }
                int manifests = 0;
                foreach (ForzaZip.Entry entry in saved)
                {
                    TreeNode node = this.FindEntryNode(entry);
                    if (node != null)
                    {
                        this.SetNodeText(node, entry);
                    }
                    if (IsArchive(entry.Name) && this.UpdateZipManifest(entry.FullPath, stamp))
                    {
                        manifests++;
                    }
                }
                this.SetStatus(string.Format("Saved {0} file(s){1}{2}{3}", saved.Count, (saved.Count > 0 && BackupPolicy.Enabled) ? ", previous versions in " + BackupFolder : "",
                    (removed.Count > 0) ? ", " + removed.Count + " deleted" : "", (manifests > 0) ? ", zipmanifest.xml updated" : ""));
                return true;
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Save", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return false;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
                this.UpdateTitle();
            }
        }

        // Inside an open DLC package, a save also packs the package again.
        private bool SaveArchive(bool saveAs)
        {
            if (!this.SaveArchiveCore(saveAs))
            {
                return false;
            }
            return !this.packagePending || this.zip == null || !this.InPackage(this.zip.FilePath) || this.RebuildPackage();
        }

        private bool SaveArchiveCore(bool saveAs)
        {
            if (this.zip == null)
            {
                return false;
            }
            if (!this.ApplyXml(false))
            {
                return false;
            }
            if (this.zip.IsFolder)
            {
                return this.SaveFolder();
            }
            if (!saveAs && !this.zip.IsModified)
            {
                this.SetStatus("Nothing to save.");
                return true;
            }
            string path = this.zip.FilePath;
            if (saveAs)
            {
                using (SaveFileDialog dialog = new SaveFileDialog())
                {
                    dialog.Filter = "Forza archive (*.zip)|*.zip";
                    dialog.FileName = Path.GetFileName(path);
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                    {
                        return false;
                    }
                    path = dialog.FileName;
                }
            }
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                this.SetStatus("Saving " + Path.GetFileName(path) + "...");
                string backup = null;
                string stamp = BackupStamp();
                if (File.Exists(path))
                {
                    backup = BackupFile(path, Path.GetFileName(path), stamp);
                }
                string selected = (this.currentEntry != null) ? this.currentEntry.Name : null;
                this.zip.Save(path);
                this.packagePending |= this.InPackage(path);
                bool manifest = this.UpdateZipManifest(path, stamp);
                this.PopulateTree();
                if (selected != null)
                {
                    this.SelectEntry(selected);
                }
                this.SetStatus(string.Format("Saved {0}{1}{2}", Path.GetFileName(path), (backup != null) ? " (previous version: bak\\" + Path.GetFileName(backup) + ")" : "",
                    manifest ? ", zipmanifest.xml updated" : ""));
                return true;
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Save Archive", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return false;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
                this.UpdateTitle();
            }
        }

        private void SelectEntry(string name)
        {
            TreeNode node = this.FindEntryNode(this.zip.Find(name));
            if (node != null)
            {
                this.tvArchive.SelectedNode = node;
            }
        }

        private static TreeNode FindNode(TreeNodeCollection nodes, object tag)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Tag == tag)
                {
                    return node;
                }
                TreeNode found = FindNode(node.Nodes, tag);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        // Stores new data for the current entry and refreshes its tree node.
        private void ReplaceCurrent(byte[] data)
        {
            this.ClearPreviewCache();
            this.zip.Replace(this.currentEntry, data);
            TreeNode node = this.FindEntryNode(this.currentEntry);
            if (node != null)
            {
                this.RefreshNode(node);
            }
        }

        private void ExtractSelected()
        {
            ForzaZip.Entry entry = this.SelectedEntry;
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.FileName = entry.Name.Substring(entry.Name.LastIndexOf('/') + 1);
                dialog.Filter = "All files (*.*)|*.*";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    File.WriteAllBytes(dialog.FileName, this.zip.Read(entry));
                    this.SetStatus("Extracted " + dialog.FileName);
                }
            }
        }

        private void ReplaceSelected()
        {
            ForzaZip.Entry entry = this.SelectedEntry;
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Replace " + entry.Name;
                dialog.Filter = "All files (*.*)|*.*";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    this.ClearPreviewCache();
                    this.zip.Replace(entry, File.ReadAllBytes(dialog.FileName));
                    this.RefreshNode(this.tvArchive.SelectedNode);
                    this.ShowEntry(entry);
                }
            }
        }

        private void RevertSelected()
        {
            ForzaZip.Entry entry = this.SelectedEntry;
            this.ClearPreviewCache();
            bool added = entry.IsAdded;
            this.zip.Revert(entry);
            if (added)
            {
                // Not saved yet: it just goes away.
                this.currentEntry = null;
                this.PopulateTree();
                this.ShowView(this.lblEmpty);
                this.UpdateTitle();
                this.SetStatus(entry.Name + " removed (it was never saved).");
                return;
            }
            this.RefreshNode(this.tvArchive.SelectedNode);
            this.ShowEntry(entry);
        }

        private void ExtractAll()
        {
            if (this.zip == null)
            {
                return;
            }
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Extract all files to:";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                try
                {
                    Cursor.Current = Cursors.WaitCursor;
                    foreach (ForzaZip.Entry entry in this.zip.Entries)
                    {
                        if (entry.IsDirectory)
                        {
                            continue;
                        }
                        string path = Path.Combine(dialog.SelectedPath, entry.Name.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        File.WriteAllBytes(path, this.zip.Read(entry));
                    }
                    this.SetStatus("Extracted " + this.zip.Entries.Count + " files to " + dialog.SelectedPath);
                }
                catch (Exception exception)
                {
                    MessageBox.Show(this, exception.Message, "Extract All", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                }
                finally
                {
                    Cursor.Current = Cursors.Default;
                }
            }
        }

        #endregion

        #region Content views

        private void tvArchive_AfterSelect(object sender, TreeViewEventArgs e)
        {
            ForzaZip.Entry entry = e.Node.Tag as ForzaZip.Entry;
            if (entry != null && entry != this.currentEntry)
            {
                this.ShowEntry(entry);
            }
        }

        private void ShowEntry(ForzaZip.Entry entry)
        {
            this.currentEntry = entry;
            string extension = Path.GetExtension(entry.Name).ToLowerInvariant();
            if (this.zip.IsFolder && IsArchive(entry.Name))
            {
                this.ShowInfo(entry, null);
                this.SetStatus(entry.Name + " - double-click to open the archive");
                return;
            }
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                byte[] data = this.zip.Read(entry);
                if (extension == ".carbin" || entry.Name.EndsWith(".rmb.bin", StringComparison.OrdinalIgnoreCase))
                {
                    this.ShowModel(entry.Name, data);
                }
                else if (extension == ".xds")
                {
                    this.xdcSource = null;
                    this.ShowTexture(data);
                }
                else if (extension == ".xdc")
                {
                    this.xdcSource = data;
                    this.ShowTexture(XdcFile.Unpack(data));
                }
                else if (extension == ".tga")
                {
                    this.ShowTga(data);
                }
                else if (extension == ".xml" && IsShaderSettings(entry) && !(this.shaderAsXml && entry == this.shaderEntry))
                {
                    this.ShowShaderSettings(entry, data);
                }
                else if (extension == ".xml")
                {
                    this.ShowXml(entry, data);
                }
                else if (extension == ".fbf" || extension == ".bgf" || extension == ".bsg")
                {
                    this.ShowDash(entry, data);
                }
                else if (extension == ".slt")
                {
                    this.ShowGameDb(entry, data);
                }
                else if (extension == ".ini" || extension == ".cfg" || extension == ".txt")
                {
                    this.ShowTextFile(entry, data, false);
                }
                else if (data.Length >= 4 && data[0] == 'c' && data[1] == 'm' && data[2] == 's' && data[3] == 's')
                {
                    this.ShowInfo(entry, "this is Forza profile save data (cmss header): it is signed and encrypted, so it cannot be edited here.");
                }
                else if (IsText(extension))
                {
                    Encoding encoding;
                    byte[] preamble;
                    this.txtContent.Text = DecodeText(data, out encoding, out preamble).Replace("\r\n", "\n").Replace("\n", "\r\n");
                    this.ShowView(this.txtContent);
                }
                else
                {
                    this.ShowInfo(entry, null);
                }
                this.SetStatus(string.Format("{0} - {1:N0} bytes{2}", entry.Name, data.Length, entry.IsModified ? " (modified)" : ""));
            }
            catch (Exception exception)
            {
                this.ShowInfo(entry, exception.Message);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        private static bool IsText(string extension)
        {
            switch (extension)
            {
                case ".xml":
                case ".txt":
                case ".html":
                case ".htm":
                case ".rdf":
                case ".ini":
                case ".cfg":
                    return true;
            }
            return false;
        }

        private void ShowInfo(ForzaZip.Entry entry, string error)
        {
            string hint = (this.zip.IsFolder && IsArchive(entry.Name))
                ? "Double-click the archive in the tree, or press Enter, to open it (Back returns here)."
                : "Right click the file in the tree to extract or replace it.";
            this.lblInfo.Text = string.Format("{0}\n\n{1:N0} bytes{2}\n\n{3}",
                entry.Name, entry.UncompressedSize, (error != null) ? ("\n\nCannot open this file:\n" + error) : "", hint);
            this.ShowView(this.pnlInfo);
        }

        private void ShowView(Control view)
        {
            foreach (Control control in this.pnlContent.Controls)
            {
                control.Visible = control == view;
            }
            if (view != this.pnlModel)
            {
                this.modelTimer.Stop();
            }
        }

        #endregion

        private void SetStatus(string text)
        {
            this.lblStatus.Text = text;
            this.status.Refresh();
        }

        private void UpdateTitle()
        {
            bool open = this.zip != null;
            this.mnuSave.Enabled = open;
            this.mnuSaveAs.Enabled = open && !this.zip.IsFolder;
            this.mnuClose.Enabled = open;
            this.UpdateBackMenu();
            this.Text = open ? string.Format("{0}{1}{2} - {3}", this.zip.IsFolder ? this.zip.FilePath.TrimEnd('\\') : Path.GetFileName(this.zip.FilePath), this.zip.IsFolder ? "\\" : "", this.zip.IsModified ? " *" : "", AppTitle) : AppTitle;
            if (open && this.package != null && this.InPackage(this.zip.FilePath))
            {
                // The work folder's path means nothing to the user: name the package instead.
                string inside = this.zip.FilePath.TrimEnd('\\').Substring(this.packageFolder.Length).TrimStart('\\');
                this.Text = string.Format("{0}{1}{2}{3} - {4}", this.PackageRootLabel, (inside.Length > 0) ? " \\ " + inside : "", this.zip.IsModified ? " *" : "",
                    this.packagePending ? " (package not written)" : "", AppTitle);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!this.ConfirmDiscard() || !this.ConfirmPackageRebuild())
            {
                e.Cancel = true;
                return;
            }
            this.modelTimer.Stop();
            base.OnFormClosing(e);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            this.split.SplitterDistance = 280;
        }

        // Command line: ForzaStudioEVO.exe archive.zip|folder [entry to select]
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && (File.Exists(args[1]) || Directory.Exists(args[1])))
            {
                if (Directory.Exists(args[1]))
                {
                    this.OpenFolder(args[1]);
                }
                else
                {
                    this.OpenArchive(args[1]);
                }
                if (args.Length > 2 && this.zip != null && this.zip.Find(args[2]) != null)
                {
                    this.SelectEntry(args[2]);
                }
            }
        }
    }
}
