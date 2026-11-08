namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Drawing;
    using System.Text;
    using System.Globalization;
    using System.IO;
    using System.Windows.Forms;

    // Tools > Change Car Sound: the user picks a car and the car whose engine sound it should get. The engine sound is
    // Data_Engine.MediaName (the name of the engine tuning, <MediaName>_ET.xml, with its harmonic tunings and sound
    // banks), so changing it is one value of the car's stock engine, as the Sound box of Duplicate Car does. The donor
    // list can be filtered by cylinders, layout and aspiration (naturally aspirated, turbo, DSC / CSC supercharger) and
    // follows the car's own engine by default, so the first cars shown are the similar ones. Works on the database open in
    // the database view: the game's gamedb.slt, or a DLC package's merge database (the game's gamedb.slt is then its reference).
    internal sealed class ChangeSoundForm : Form
    {
        private sealed class CarRow
        {
            public long Id;
            public string Name;
            public string Media;       // the car's MediaName
            public long EngineId;
            public string Sound;
            public string Layout;
            public long Cylinders;
            public long Aspiration;
            public string AspirationName;
            public bool Own;           // the engine row is in the database that is edited (not in the reference one)
            public long Placement;     // Data_Car.EnginePlacementID: 1 front, 2 mid, 3 rear

            public int Group
            {
                get { return AspirationGroup(this.Aspiration); }
            }
        }

        private static readonly string[] Groups = { "Any aspiration", "Naturally aspirated", "Turbo (single, twin, quad)", "Supercharger DSC", "Supercharger CSC" };

        private GameDatabase db;
        private GameDatabase reference;            // the game's own gamedb.slt when the open database is a partial one (a DLC pack's)
        private readonly string gameRoot;          // the FM4 folder of the ini (the reference database, the strings, the sounds)
        private bool standalone;                   // opens its own source (game folder, package, DLC folder) and saves it
        private readonly List<CarRow> cars = new List<CarRow>();
        private readonly TextBox sourceBox = new TextBox();
        private readonly ComboBox part = new ComboBox();
        private readonly ComboBox revs = new ComboBox();
        private readonly Button listenCurrent = new Button();
        private readonly Button listenNew = new Button();
        private string openRoot;                   // the opened FM4 folder (standalone) or the editor's
        private string packRoot;                   // the DLC folder / the work folder of a package
        private StfsReader package;                // set when the source is a package file
        private string dbFile;                     // the file the database is read from and written to ...
        private string dbEntry;                    // ... or the zip entry of it in dbFile (a pack's merge database)
        private readonly ListView targets = new ListView();
        private readonly ListView donors = new ListView();
        private readonly TextBox targetFilter = new TextBox();
        private readonly TextBox donorFilter = new TextBox();
        private readonly ComboBox layout = new ComboBox();
        private readonly ComboBox cylinders = new ComboBox();
        private readonly ComboBox aspiration = new ComboBox();
        private readonly CheckBox follow = new CheckBox();
        private readonly Label info = new Label();
        private readonly Button change = new Button();
        private readonly CheckBox convert = new CheckBox();
        private readonly NumericUpDown convertTo = new NumericUpDown();
        private readonly ComboBox placement = new ComboBox();     // 0 keep the CMT, 1 front, 2 mid, 3 rear (= EnginePlacementID)
        private bool filling;

        public event EventHandler Changed;

        // In the editor: the database open in its database view; packFolder = the work folder of the open DLC package, or null.
        public ChangeSoundForm(GameDatabase db, string gameRoot, string openRoot, string packFolder)
        {
            this.db = db;
            this.gameRoot = gameRoot;
            this.openRoot = openRoot;
            this.packRoot = packFolder;
            this.reference = db.Reference ?? OpenGameDb(gameRoot, db);
            this.LoadCars();
            this.Build();
        }

        // Alone (Tools > Change Car Sound without anything open): the source is chosen here and saved here.
        public ChangeSoundForm(string gameRoot)
        {
            this.standalone = true;
            this.gameRoot = gameRoot;
            this.Build();
        }

        private static string WorkRoot
        {
            get { return Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "DlcSound"); }
        }

        private void Build()
        {
            this.FormClosed += delegate
            {
                EnginePreview.Stop();
                this.DisposeReference();
            };
            this.AutoScaleMode = AutoScaleMode.None;
            this.Text = "Change car sound";
            this.ClientSize = new Size(1000, 684);
            this.MinimumSize = new Size(800, 480);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;

            Label left = new Label();
            left.Text = "1. The car to change:";
            left.SetBounds(12, 10, 300, 20);
            this.targetFilter.SetBounds(12, 34, 470, 24);
            this.SetupList(this.targets, 12, 64, 470, 420);
            this.targetFilter.TextChanged += delegate { this.FillTargets(); };
            this.targets.SelectedIndexChanged += delegate { this.OnTargetChanged(); };

            Label right = new Label();
            right.Text = "2. Take the engine sound of:";
            right.SetBounds(500, 10, 300, 20);
            this.donorFilter.SetBounds(500, 34, 200, 24);
            this.layout.SetBounds(708, 34, 90, 24);
            this.cylinders.SetBounds(804, 34, 90, 24);
            this.aspiration.SetBounds(500, 64, 200, 24);
            this.follow.Text = "Same engine as the car";
            this.follow.Checked = true;
            this.follow.SetBounds(708, 64, 200, 24);
            foreach (ComboBox box in new ComboBox[] { this.layout, this.cylinders, this.aspiration })
            {
                box.DropDownStyle = ComboBoxStyle.DropDownList;
                box.MaxDropDownItems = 24;
            }
            this.FillFilters();
            this.SetupList(this.donors, 500, 94, 488, 390);
            this.donorFilter.TextChanged += delegate { this.FillDonors(); };
            this.layout.SelectedIndexChanged += delegate { this.OnFilterChosen(); };
            this.cylinders.SelectedIndexChanged += delegate { this.OnFilterChosen(); };
            this.aspiration.SelectedIndexChanged += delegate { this.OnFilterChosen(); };
            this.follow.CheckedChanged += delegate { if (this.follow.Checked) { this.OnTargetChanged(); } };
            this.donors.SelectedIndexChanged += delegate { this.OnDonorChanged(); };

            Label listen = new Label();
            listen.Text = "Listen:";
            listen.SetBounds(12, 496, 50, 20);
            listen.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            this.part.SetBounds(64, 492, 130, 24);
            this.part.DropDownStyle = ComboBoxStyle.DropDownList;
            this.part.Items.AddRange(new object[] { "Exhaust", "Intake", "Engine ambience" });
            this.part.SelectedIndex = 0;
            this.revs.SetBounds(200, 492, 100, 24);
            this.revs.DropDownStyle = ComboBoxStyle.DropDownList;
            this.revs.Items.AddRange(new object[] { "Low revs", "Mid revs", "High revs" });
            this.revs.SelectedIndex = 1;
            this.listenCurrent.Text = "Current sound";
            this.listenCurrent.SetBounds(310, 490, 110, 28);
            this.listenCurrent.Click += delegate { this.Listen(this.SelectedTarget()); };
            this.listenNew.Text = "New sound";
            this.listenNew.SetBounds(426, 490, 110, 28);
            this.listenNew.Click += delegate { this.Listen(this.SelectedDonor()); };
            Button stop = new Button();
            stop.Text = "Stop";
            stop.SetBounds(542, 490, 70, 28);
            stop.Click += delegate { EnginePreview.Stop(); };
            foreach (Control control in new Control[] { listen, this.part, this.revs, this.listenCurrent, this.listenNew, stop })
            {
                control.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            }

            this.info.SetBounds(12, 526, 976, 64);
            this.info.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

            // Options that write sound files into the pack (SoundConvert): a sound with another cylinder count, the engine moved.
            this.convert.Text = "Convert to";
            this.convert.SetBounds(12, 600, 86, 24);
            this.convertTo.SetBounds(100, 600, 48, 24);
            this.convertTo.Minimum = 1;
            this.convertTo.Maximum = 16;
            this.convertTo.Value = 4;
            Label cylinderWord = new Label();
            cylinderWord.Text = "cylinders";
            cylinderWord.SetBounds(152, 603, 70, 20);
            Label engineWord = new Label();
            engineWord.Text = "Engine:";
            engineWord.SetBounds(228, 603, 52, 20);
            this.placement.SetBounds(282, 600, 160, 24);
            this.placement.DropDownStyle = ComboBoxStyle.DropDownList;
            this.placement.Items.AddRange(new object[] { "keep the car's CMT", "front", "mid", "rear" });
            this.placement.SelectedIndex = 0;
            this.convert.CheckedChanged += delegate { this.UpdateInfo(); };
            this.convertTo.ValueChanged += delegate { this.UpdateInfo(); };
            this.placement.SelectedIndexChanged += delegate { this.UpdateInfo(); };
            foreach (Control control in new Control[] { this.convert, this.convertTo, cylinderWord, engineWord, this.placement })
            {
                control.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            }

            this.change.Text = "Change sound";
            this.change.SetBounds(792, 596, 110, 32);
            this.change.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            this.change.Click += delegate { this.Apply(); };
            Button check = new Button();
            check.Text = "Check sounds";
            check.SetBounds(672, 596, 112, 32);
            check.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            check.Click += delegate { this.CheckSounds(); };
            Button details = new Button();
            details.Text = "Sound details";
            details.SetBounds(552, 596, 112, 32);
            details.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            details.Click += delegate { this.ShowDetails(); };
            Button close = new Button();
            close.Text = "Close";
            close.DialogResult = DialogResult.Cancel;
            close.SetBounds(910, 596, 78, 32);
            close.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            this.CancelButton = close;

            this.targets.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom;
            this.donors.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            this.sourceBox.Text = this.standalone ? "" : "The database open in the editor";
            Panel body = new Panel();
            body.Size = new Size(1000, 640);
            body.Dock = DockStyle.Fill;
            body.Controls.AddRange(new Control[] { left, this.targetFilter, this.targets, right, this.donorFilter, this.layout, this.cylinders, this.aspiration, this.follow, this.donors,
                listen, this.part, this.revs, this.listenCurrent, this.listenNew, stop, this.info, this.convert, this.convertTo, cylinderWord, engineWord, this.placement, details, check, this.change, close });
            this.Controls.Add(body);
            {
                Panel bar = new Panel();
                bar.Size = new Size(1000, 44);
                bar.Dock = DockStyle.Top;
                Label from = new Label();
                from.Text = "From:";
                from.SetBounds(12, 14, 40, 20);
                this.sourceBox.ReadOnly = true;
                this.sourceBox.SetBounds(54, 10, 470, 24);
                this.sourceBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                Button game = new Button();
                game.Text = "FM4 game folder...";
                game.SetBounds(534, 8, 150, 28);
                game.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                game.Click += delegate { this.OpenGameFolder(); };
                Button file = new Button();
                file.Text = "DLC package...";
                file.SetBounds(690, 8, 150, 28);
                file.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                file.Click += delegate { this.OpenPackageFile(); };
                Button folder = new Button();
                folder.Text = "DLC folder...";
                folder.SetBounds(846, 8, 142, 28);
                folder.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                folder.Click += delegate { this.OpenDlcFolder(); };
                bar.Controls.AddRange(new Control[] { from, this.sourceBox, game, file, folder });
                this.Controls.Add(bar);
            }
            ForzaTheme.Apply(this);
            this.FillTargets();
            this.FillDonors();
            this.UpdateInfo();
        }

        // The game's gamedb.slt of the FM4 folder set in the ini, for a partial database (a DLC pack's merge database: it has
        // its own cars but not the lists, nor the game's engines to take the sound of). Null when db is the full one.
        private static GameDatabase OpenGameDb(string gameRoot, GameDatabase db)
        {
            if (db.HasTable("List_EngineConfig") || string.IsNullOrEmpty(gameRoot))
            {
                return null;
            }
            string path = System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(gameRoot, "Media"), "db"), "gamedb.slt");
            try
            {
                if (!System.IO.File.Exists(path))
                {
                    return null;
                }
                GameDatabase game = new GameDatabase(System.IO.File.ReadAllBytes(path));
                game.Strings = db.Strings;
                return game.HasTable("Data_Car") ? game : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void DisposeReference()
        {
            if (this.reference != null && (this.db == null || this.reference != this.db.Reference))
            {
                this.reference.Dispose();
            }
            this.reference = null;
        }

        private static bool XeniaRunning()
        {
            foreach (string name in new string[] { "xenia_canary", "xenia" })
            {
                if (Process.GetProcessesByName(name).Length > 0)
                {
                    return true;
                }
            }
            return false;
        }

        private void OpenGameFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "The FM4 game folder (the one that contains Media): the Xenia test copy FM4_Extracted_DEV.";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                this.Open(delegate
                {
                    string folder = Path.GetFullPath(dialog.SelectedPath).TrimEnd('\\');
                    string file = Path.Combine(Path.Combine(Path.Combine(folder, "Media"), "db"), "gamedb.slt");
                    if (!File.Exists(file))
                    {
                        throw new FileNotFoundException("Media\\db\\gamedb.slt not found: choose the FM4 game folder (the one that contains Media).");
                    }
                    if (folder.EndsWith("\\FM4_Extracted", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("FM4_Extracted is the clean game and is never written: use the test copy FM4_Extracted_DEV.");
                    }
                    this.package = null;
                    this.packRoot = null;
                    this.openRoot = folder;
                    this.dbFile = file;
                    this.dbEntry = null;
                    GameDatabase game = new GameDatabase(File.ReadAllBytes(file));
                    this.SetSource(folder, game, folder);
                });
            }
        }

        private void OpenPackageFile()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "DLC package";
                dialog.Filter = "Xbox 360 package (CON, LIVE, PIRS)|*.*";
                string installed = InstalledDlc.ContentFolder(ForzaGame.Motorsport4);
                if (installed != null)
                {
                    dialog.InitialDirectory = installed;
                }
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                this.Open(delegate
                {
                    StfsReader opened = StfsReader.Open(dialog.FileName);
                    string work = Path.Combine(WorkRoot, Path.GetFileName(dialog.FileName));
                    if (Directory.Exists(work))
                    {
                        Directory.Delete(work, true);
                    }
                    try
                    {
                        opened.Extract(work);
                    }
                    catch (Exception)
                    {
                        if (Directory.Exists(work))
                        {
                            Directory.Delete(work, true);
                        }
                        throw;
                    }
                    this.LoadPack(work, dialog.FileName);
                    this.package = opened;
                });
            }
        }

        private void OpenDlcFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "The DLC folder (it holds Media\\DLCZips, or the loose db\\patch\\*_merge.slt).";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                this.Open(delegate
                {
                    this.LoadPack(dialog.SelectedPath, dialog.SelectedPath);
                    this.package = null;
                });
            }
        }

        private void Open(MethodInvoker load)
        {
            string was = this.sourceBox.Text;
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                EnginePreview.Stop();
                load();
                this.standalone = true;
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                this.sourceBox.Text = was;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        // The merge database of a DLC folder with the highest number: loose (db\patch\<n>_merge.slt) or an entry of a pack zip.
        private void LoadPack(string folder, string shown)
        {
            string bestFile = null, bestEntry = null, bestName = null;
            foreach (string path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (path.EndsWith("_merge.slt", StringComparison.OrdinalIgnoreCase))
                {
                    string name = Path.GetFileName(path);
                    if (bestName == null || string.Compare(name, bestName, StringComparison.OrdinalIgnoreCase) > 0)
                    {
                        bestFile = path;
                        bestEntry = null;
                        bestName = name;
                    }
                }
                else if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(path).IndexOf("StringTables", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    foreach (ForzaZip.Entry entry in new ForzaZip(path).Entries)
                    {
                        string name = Path.GetFileName(entry.Name.Replace('\\', '/'));
                        if (entry.Name.EndsWith("_merge.slt", StringComparison.OrdinalIgnoreCase) && (bestName == null || string.Compare(name, bestName, StringComparison.OrdinalIgnoreCase) > 0))
                        {
                            bestFile = path;
                            bestEntry = entry.Name;
                            bestName = name;
                        }
                    }
                }
            }
            if (bestFile == null)
            {
                throw new InvalidOperationException("No merge database (db\\patch\\*_merge.slt) in this DLC: it adds no cars.");
            }
            ForzaZip mergeZip = (bestEntry == null) ? null : new ForzaZip(bestFile);
            byte[] data = (mergeZip == null) ? File.ReadAllBytes(bestFile) : mergeZip.Read(mergeZip.Find(bestEntry));
            GameDatabase merge = new GameDatabase(data);
            if (!merge.HasTable("Data_Car") || !merge.HasTable("Data_Engine"))
            {
                throw new InvalidOperationException("The database of this DLC (" + bestName + ") has no cars with an engine of their own.");
            }
            this.packRoot = folder;
            this.openRoot = this.gameRoot;
            this.dbFile = bestFile;
            this.dbEntry = bestEntry;
            this.SetSource(shown, merge, this.gameRoot);
        }

        // The database is the one to edit; the strings are the game's (the pack's on top), the sounds' folders as set.
        private void SetSource(string shown, GameDatabase database, string stringsRoot)
        {
            this.DisposeReference();
            this.db = database;
            if (this.packRoot != null && Directory.Exists(this.packRoot))
            {
                StringTable table = null;
                try
                {
                    table = string.IsNullOrEmpty(stringsRoot) ? null : StringTable.Load(stringsRoot, "EN");
                }
                catch (Exception)
                {
                }
                foreach (string path in Directory.GetFiles(this.packRoot, "StringTables*.zip", SearchOption.AllDirectories))
                {
                    table = table ?? new StringTable();
                    ForzaZip strings = new ForzaZip(path);
                    foreach (ForzaZip.Entry entry in strings.Entries)
                    {
                        string name = entry.Name.Replace('\\', '/');
                        if (name.StartsWith("EN/", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".str", StringComparison.OrdinalIgnoreCase))
                        {
                            table.Overlay(Path.GetFileNameWithoutExtension(name), strings.Read(entry));
                        }
                    }
                }
                this.db.Strings = table;
            }
            else
            {
                try
                {
                    this.db.Strings = StringTable.Load(stringsRoot, "EN");
                }
                catch (Exception)
                {
                }
            }
            this.reference = OpenGameDb(this.gameRoot, this.db);
            this.sourceBox.Text = shown;
            this.cars.Clear();
            this.LoadCars();
            this.layout.Items.Clear();
            this.cylinders.Items.Clear();
            this.aspiration.Items.Clear();
            this.FillFilters();
            this.targetFilter.Text = "";
            this.donorFilter.Text = "";
            this.FillTargets();
            this.FillDonors();
            this.UpdateInfo();
        }

        private void Listen(CarRow car)
        {
            if (car == null)
            {
                return;
            }
            string[] partNames = EnginePreview.Parts;
            double[] positions = { 0.1, 0.5, 0.9 };
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                EnginePreview preview = new EnginePreview(this.packRoot, this.openRoot, this.gameRoot);
                string played = preview.Play(car.Sound, partNames[Math.Max(0, this.part.SelectedIndex)], positions[Math.Max(0, this.revs.SelectedIndex)]);
                this.info.Text = car.Name + " (" + car.Sound + "): " + played + ". Stop ends the loop.";
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        private static void Backup(string path)
        {
            if (!BackupPolicy.Enabled)
            {
                return;
            }
            string folder = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "bak");
            Directory.CreateDirectory(folder);
            File.Copy(path, Path.Combine(folder, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + "_bak_" + Path.GetFileName(path)), true);
        }

        // Standalone: the database written back to where it was read (the game's file, a pack's zip) and the package packed again.
        private string SaveSource()
        {
            byte[] bytes = this.db.ToBytes();
            Backup(this.dbFile);
            if (this.dbEntry == null)
            {
                File.WriteAllBytes(this.dbFile, bytes);
            }
            else
            {
                ForzaZip zip = new ForzaZip(this.dbFile);
                zip.Replace(zip.Find(this.dbEntry), bytes);
                zip.Save(this.dbFile);
            }
            return this.Repack(Path.GetFileName(this.dbFile) + " saved");
        }

        // The package file again from its work folder when the source is a package (done = what was written before, for the message).
        private string Repack(string done)
        {
            if (this.package == null)
            {
                return done;
            }
            string target = this.package.Path;
            string temp = target + ".new";
            try
            {
                Backup(target);
                StfsPackage.Rewrite(this.packRoot, temp, this.package.Header);
                StfsReader check = StfsReader.Open(temp);
                File.Delete(target);
                File.Move(temp, target);
                check.Path = target;
                this.package = check;
            }
            catch (Exception)
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
                throw;
            }
            return done + ", package " + Path.GetFileName(target) + " written";
        }

        // What the selected car plays now and what the picked sound plays: tuning, harmonic tunings, banks (and where each is
        // read from), samples, and the other cars that share each bank, from the audio index of the game, its installed
        // packages and the open pack.
        private void ShowDetails()
        {
            CarRow current = this.SelectedTarget();
            CarRow chosen = this.SelectedDonor();
            if (current == null && chosen == null)
            {
                MessageBox.Show(this, "Pick a car on the left, or a sound on the right.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string text;
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                string root = !string.IsNullOrEmpty(this.gameRoot) ? this.gameRoot : this.openRoot;
                SoundIndex index = SoundIndex.Get(ForzaGame.Motorsport4, root, this.packRoot, null);
                StringBuilder builder = new StringBuilder();
                builder.AppendLine(index.Summary());
                builder.AppendLine();
                if (current != null)
                {
                    builder.AppendLine("CAR TO CHANGE: " + current.Name);
                    builder.AppendLine(index.Describe(current.Sound));
                }
                if (chosen != null && (current == null || !string.Equals(chosen.Sound, current.Sound, StringComparison.OrdinalIgnoreCase)))
                {
                    builder.AppendLine("SOUND TO TAKE: " + chosen.Name);
                    builder.AppendLine(index.Describe(chosen.Sound));
                }
                text = builder.ToString();
            }
            catch (Exception exception)
            {
                text = "The audio could not be indexed: " + exception.Message;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            using (Form dialog = new Form())
            {
                dialog.Text = "Sound details";
                dialog.ClientSize = new Size(1000, 520);
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.ShowInTaskbar = false;
                TextBox box = new TextBox();
                box.Multiline = true;
                box.ReadOnly = true;
                box.ScrollBars = ScrollBars.Both;
                box.WordWrap = false;
                box.Font = new Font(FontFamily.GenericMonospace, 9f);
                box.SetBounds(10, 10, 980, 460);
                box.Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
                Button close = new Button();
                close.Text = "Close";
                close.SetBounds(910, 478, 80, 32);
                close.DialogResult = DialogResult.Cancel;
                dialog.Controls.AddRange(new Control[] { box, close });
                dialog.CancelButton = close;
                ForzaTheme.Apply(dialog);
                box.Select(0, 0);
                dialog.ShowDialog(this);
            }
        }

        private EnginePreview Preview()
        {
            return new EnginePreview(this.packRoot, this.openRoot, this.gameRoot);
        }

        // The car whose model tuning stands in for a car's own: one with the same engine sound that has a model tuning, else the donor picked.
        private CarRow ModelSource(CarRow car, EnginePreview preview)
        {
            CarRow pick = this.SelectedDonor();
            foreach (CarRow other in this.cars)
            {
                if (other != car && other.Media != null && string.Equals(other.Sound, car.Sound, StringComparison.OrdinalIgnoreCase) && preview.FindModel(other.Media) != null)
                {
                    return other;
                }
            }
            return (pick != null && pick.Media != null && preview.FindModel(pick.Media) != null) ? pick : null;
        }

        // Check sounds: every car of the source (its stock engine's tuning, harmonic tunings and banks, and the car's model tuning),
        // listed; Fix adds the missing model tunings (copied from a car with the same engine sound, the car's name in them).
        private void CheckSounds()
        {
            if (this.db == null)
            {
                return;
            }
            EnginePreview preview = this.Preview();
            List<string> lines = new List<string>();
            List<CarRow> fixable = new List<CarRow>();
            int problems = 0;
            Cursor.Current = Cursors.WaitCursor;
            Dictionary<string, bool> engines = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (CarRow car in this.cars)
            {
                if (!car.Own)
                {
                    continue;
                }
                List<string> found = new List<string>();
                if (!engines.ContainsKey(car.Sound))
                {
                    engines[car.Sound] = true;
                    foreach (string text in preview.Diagnose(car.Sound))
                    {
                        found.Add(text);
                    }
                }
                if (preview.FindModel(car.Media) == null)
                {
                    CarRow from = this.ModelSource(car, preview);
                    found.Add("car model tuning " + car.Media + "_CMT.xml is missing: the car has no sound model (silent)" + ((from != null) ? "; Fix copies " + from.Media + "_CMT.xml" : "; no car with this sound has one to copy: pick the car to copy from in the right list"));
                    if (from != null)
                    {
                        fixable.Add(car);
                    }
                }
                foreach (string text in found)
                {
                    lines.Add(car.Media + ": " + text);
                    problems++;
                }
            }
            Cursor.Current = Cursors.Default;
            if (problems == 0)
            {
                MessageBox.Show(this, "Every car of " + this.sourceBox.Text + " has its sound files.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            bool canFix = fixable.Count > 0 && (this.standalone || this.packRoot != null);
            using (Form dialog = new Form())
            {
                dialog.Text = "Check sounds: " + problems + " problem(s)";
                dialog.ClientSize = new Size(900, 420);
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.ShowInTaskbar = false;
                TextBox box = new TextBox();
                box.Multiline = true;
                box.ReadOnly = true;
                box.ScrollBars = ScrollBars.Both;
                box.WordWrap = false;
                box.SetBounds(10, 10, 880, 360);
                box.Text = string.Join("\r\n", lines.ToArray()) + (canFix || fixable.Count == 0 ? "" : "\r\n\r\nOpen the pack with DLC package... / DLC folder... here to fix it (this window saves what it fixes).");
                Button fix = new Button();
                fix.Text = "Fix " + fixable.Count + " model tuning(s)";
                fix.SetBounds(640, 378, 160, 32);
                fix.Enabled = canFix;
                fix.DialogResult = DialogResult.OK;
                Button close = new Button();
                close.Text = "Close";
                close.SetBounds(810, 378, 80, 32);
                close.DialogResult = DialogResult.Cancel;
                dialog.Controls.AddRange(new Control[] { box, fix, close });
                dialog.CancelButton = close;
                ForzaTheme.Apply(dialog);
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
            }
            this.FixModels(fixable, preview);
        }

        // The game mounts the pack's own zip: the sound files go beside the pack's database, in its Media\audio\Cars folder.
        // Returns the zip; audioPrefix = the entry path of that folder ("...Media/audio/Cars/").
        private string PackZip(out string audioPrefix)
        {
            string zipPath = (this.dbEntry != null) ? this.dbFile : null;
            if (zipPath == null)
            {
                foreach (string file in Directory.GetFiles(this.packRoot, "*_pri_*.zip", SearchOption.AllDirectories))
                {
                    if (Path.GetFileName(file).IndexOf("StringTables", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        zipPath = file;
                        break;
                    }
                }
            }
            if (zipPath == null)
            {
                throw new InvalidOperationException("The DLC has no pack zip (Media\\DLCZips\\<offer>_pri_<n>.zip) to put the files in.");
            }
            int media = (this.dbEntry != null) ? this.dbEntry.IndexOf("Media/", StringComparison.OrdinalIgnoreCase) : -1;
            audioPrefix = (media > 0 ? this.dbEntry.Substring(0, media) : "") + "Media/audio/Cars/";
            return zipPath;
        }

        private static void Put(ForzaZip zip, string entryName, byte[] data)
        {
            ForzaZip.Entry existing = zip.Find(entryName);
            if (existing != null)
            {
                zip.Replace(existing, data);
            }
            else
            {
                zip.Add(entryName, data);
            }
        }

        private void FixModels(List<CarRow> fixable, EnginePreview preview)
        {
            if (XeniaRunning())
            {
                MessageBox.Show(this, "Close Xenia first: it keeps the game and DLC files in use and reads them when it starts.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                string zipPath;
                string prefix = "";
                string folder = "";
                if (this.packRoot != null)
                {
                    zipPath = this.PackZip(out prefix);
                    prefix += "CarModelTuning/";
                }
                else
                {
                    zipPath = Path.Combine(Path.Combine(Path.Combine(Path.Combine(this.openRoot, "Media"), "audio"), "cars"), "CarModelTuning.zip");
                    folder = this.openRoot;
                }
                ForzaZip zip = new ForzaZip(zipPath);
                Encoding latin = Encoding.GetEncoding(28591);
                foreach (CarRow car in fixable)
                {
                    CarRow from = this.ModelSource(car, preview);
                    if (from == null)
                    {
                        continue;
                    }
                    string text = latin.GetString(preview.FindModel(from.Media));
                    byte[] data = latin.GetBytes(System.Text.RegularExpressions.Regex.Replace(text, System.Text.RegularExpressions.Regex.Escape(from.Media), car.Media, System.Text.RegularExpressions.RegexOptions.IgnoreCase));
                    string entryName = prefix + car.Media + "_CMT.xml";
                    ForzaZip.Entry existing = zip.Find(entryName);
                    if (existing != null)
                    {
                        zip.Replace(existing, data);
                    }
                    else
                    {
                        zip.Add(entryName, data);
                    }
                }
                Backup(zipPath);
                zip.Save(zipPath);
                if (folder.Length > 0)
                {
                    // A game zip changed: its line in zipmanifest.xml gets the new central directory position.
                    string manifest = ZipManifest.PathFor(folder);
                    string gamePath = ZipManifest.GamePath(folder, Path.GetFullPath(zipPath));
                    if (gamePath != null && ZipManifest.Lists(manifest, gamePath))
                    {
                        Backup(manifest);
                        ZipManifest.Update(manifest, gamePath, zipPath);
                    }
                }
                string done = fixable.Count + " model tuning(s) added to " + Path.GetFileName(zipPath);
                this.info.Text = this.Repack(done) + "." + (this.standalone ? "" : " The editor has the pack open: close its zip (Back), then File > Rebuild DLC Package to pack it.");
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        private void SetupList(ListView list, int x, int y, int width, int height)
        {
            list.SetBounds(x, y, width, height);
            list.View = View.Details;
            list.FullRowSelect = true;
            list.MultiSelect = false;
            list.HideSelection = false;
            list.Columns.Add("Car", 230);
            list.Columns.Add("Sound", 130);
            list.Columns.Add("Layout", 55);
            list.Columns.Add("Cyl", 40);
            list.Columns.Add("Aspiration", 110);
        }

        private static int AspirationGroup(long id)
        {
            return (id <= 1) ? 1 : (id <= 4) ? 2 : (id == 5) ? 3 : (id == 6) ? 4 : 0;
        }

        private static long Number(object value)
        {
            return (value == null || value == DBNull.Value) ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        private static string Str(object value)
        {
            return (value == null || value == DBNull.Value) ? "" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        // Every car with its stock engine: this database's cars first, then the reference database's (the game's own,
        // when this one is a DLC pack's merge database). A car in both is the pack's.
        private void LoadCars()
        {
            Dictionary<long, string> layouts = new Dictionary<long, string>();
            Dictionary<long, long> cylinderCounts = new Dictionary<long, long>();
            Dictionary<long, string> aspirations = new Dictionary<long, string>();
            Dictionary<long, bool> seen = new Dictionary<long, bool>();
            foreach (GameDatabase source in new GameDatabase[] { this.db, this.reference })
            {
                if (source == null)
                {
                    continue;
                }
                if (source.HasTable("List_EngineConfig"))
                {
                    foreach (object[] row in source.Sql.Query("SELECT ConfigID, EngineConfig FROM List_EngineConfig").Rows)
                    {
                        layouts[Number(row[0])] = Str(row[1]);
                    }
                }
                if (source.HasTable("List_Cylinders"))
                {
                    foreach (object[] row in source.Sql.Query("SELECT CylinderID, Number FROM List_Cylinders").Rows)
                    {
                        cylinderCounts[Number(row[0])] = Number(row[1]);
                    }
                }
                if (source.HasTable("List_Aspiration"))
                {
                    foreach (object[] row in source.Sql.Query("SELECT AspirationID, Aspiration FROM List_Aspiration").Rows)
                    {
                        aspirations[Number(row[0])] = Str(row[1]);
                    }
                }
            }
            foreach (GameDatabase source in new GameDatabase[] { this.db, this.reference })
            {
                if (source == null || !source.HasTable("Data_Car") || !source.HasTable("Data_Engine") || !source.HasTable("List_UpgradeEngine"))
                {
                    continue;
                }
                SqliteDatabase.Result result = source.Sql.Query(
                    "SELECT c.Id, c.MediaName, e.EngineID, e.MediaName, e.ConfigID, e.CylinderID, e.AspirationID_Stock, c.EnginePlacementID FROM Data_Car c "
                    + "JOIN List_UpgradeEngine u ON u.Ordinal = c.Id AND u.IsStock = 1 JOIN Data_Engine e ON e.EngineID = u.EngineID ORDER BY c.Id");
                foreach (object[] row in result.Rows)
                {
                    long id = Number(row[0]);
                    if (seen.ContainsKey(id) || Str(row[3]).Length == 0)
                    {
                        continue;
                    }
                    seen[id] = true;
                    CarRow car = new CarRow();
                    car.Id = id;
                    car.Name = source.LookupName("car", id) ?? Str(row[1]);
                    car.Media = Str(row[1]);
                    car.EngineId = Number(row[2]);
                    car.Sound = Str(row[3]);
                    long config = Number(row[4]);
                    long cylinder = Number(row[5]);
                    car.Layout = layouts.ContainsKey(config) ? layouts[config] : config.ToString(CultureInfo.InvariantCulture);
                    car.Cylinders = cylinderCounts.ContainsKey(cylinder) ? cylinderCounts[cylinder] : 0;
                    car.Aspiration = Number(row[6]);
                    car.AspirationName = aspirations.ContainsKey(car.Aspiration) ? aspirations[car.Aspiration] : car.Aspiration.ToString(CultureInfo.InvariantCulture);
                    car.Own = (source == this.db);
                    car.Placement = Number(row[7]);
                    this.cars.Add(car);
                }
            }
            this.cars.Sort(delegate(CarRow a, CarRow b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
        }

        private void FillFilters()
        {
            this.filling = true;
            SortedDictionary<string, bool> layouts = new SortedDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            SortedDictionary<long, bool> counts = new SortedDictionary<long, bool>();
            foreach (CarRow car in this.cars)
            {
                layouts[car.Layout] = true;
                counts[car.Cylinders] = true;
            }
            this.layout.Items.Add("Any layout");
            foreach (string name in layouts.Keys)
            {
                this.layout.Items.Add(name);
            }
            this.cylinders.Items.Add("Any cyl.");
            foreach (long count in counts.Keys)
            {
                this.cylinders.Items.Add(count.ToString(CultureInfo.InvariantCulture));
            }
            this.aspiration.Items.AddRange(Groups);
            this.layout.SelectedIndex = 0;
            this.cylinders.SelectedIndex = 0;
            this.aspiration.SelectedIndex = 0;
            this.filling = false;
        }

        private static ListViewItem Item(CarRow car)
        {
            ListViewItem item = new ListViewItem(car.Name);
            item.SubItems.Add(car.Sound);
            item.SubItems.Add(car.Layout);
            item.SubItems.Add(car.Cylinders.ToString(CultureInfo.InvariantCulture));
            item.SubItems.Add(car.AspirationName);
            item.Tag = car;
            return item;
        }

        private static bool Contains(CarRow car, string text)
        {
            return text.Length == 0 || car.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 || car.Sound.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private CarRow SelectedTarget()
        {
            return (this.targets.SelectedItems.Count == 0) ? null : (CarRow) this.targets.SelectedItems[0].Tag;
        }

        private CarRow SelectedDonor()
        {
            return (this.donors.SelectedItems.Count == 0) ? null : (CarRow) this.donors.SelectedItems[0].Tag;
        }

        private void FillTargets()
        {
            CarRow keep = this.SelectedTarget();
            string text = this.targetFilter.Text.Trim();
            this.targets.BeginUpdate();
            this.targets.Items.Clear();
            foreach (CarRow car in this.cars)
            {
                if (car.Own && Contains(car, text))
                {
                    ListViewItem item = Item(car);
                    this.targets.Items.Add(item);
                    if (keep != null && keep.Id == car.Id)
                    {
                        item.Selected = true;
                    }
                }
            }
            this.targets.EndUpdate();
        }

        private void FillDonors()
        {
            CarRow keep = this.SelectedDonor();
            CarRow target = this.SelectedTarget();
            string text = this.donorFilter.Text.Trim();
            string layoutName = (this.layout.SelectedIndex > 0) ? (string) this.layout.SelectedItem : null;
            long count = (this.cylinders.SelectedIndex > 0) ? long.Parse((string) this.cylinders.SelectedItem, CultureInfo.InvariantCulture) : -1;
            int group = this.aspiration.SelectedIndex;
            this.donors.BeginUpdate();
            this.donors.Items.Clear();
            foreach (CarRow car in this.cars)
            {
                if ((target != null && car.Id == target.Id) || !Contains(car, text)
                    || (layoutName != null && !string.Equals(car.Layout, layoutName, StringComparison.OrdinalIgnoreCase))
                    || (count >= 0 && car.Cylinders != count)
                    || (group > 0 && car.Group != group))
                {
                    continue;
                }
                ListViewItem item = Item(car);
                this.donors.Items.Add(item);
                if (keep != null && keep.Id == car.Id)
                {
                    item.Selected = true;
                }
            }
            this.donors.EndUpdate();
            this.UpdateInfo();
        }

        // The options that write files need the pack opened in this window (the editor would overwrite its zip).
        private bool CanWriteFiles
        {
            get { return this.standalone && this.packRoot != null && this.db != null; }
        }

        // Proposed: convert when the picked sound has another cylinder count than the car's engine.
        private void OnDonorChanged()
        {
            CarRow target = this.SelectedTarget();
            CarRow donor = this.SelectedDonor();
            if (target != null && donor != null && target.Cylinders > 0)
            {
                this.convertTo.Value = Math.Min(this.convertTo.Maximum, Math.Max(this.convertTo.Minimum, target.Cylinders));
                this.convert.Checked = this.CanWriteFiles && donor.Cylinders != target.Cylinders;
            }
            this.UpdateInfo();
        }

        // Proposed engine position: the car's Data_Car.EnginePlacementID when its CMT puts the engine on the other side of
        // the cabin (FH1 595: rear in the DB, front in the CMT); else the CMT is kept. The user can pick any.
        private void ProposePlacement(CarRow target)
        {
            int propose = 0;
            if (target != null && this.CanWriteFiles && target.Placement >= 1 && target.Placement <= 3)
            {
                try
                {
                    byte[] cmt = this.Preview().FindModel(target.Media);
                    if (cmt != null && SoundConvert.EngineInFront(Encoding.GetEncoding(28591).GetString(cmt)) != (target.Placement == 1))
                    {
                        propose = (int) target.Placement;
                    }
                }
                catch (Exception)
                {
                }
            }
            this.placement.SelectedIndex = propose;
        }

        // The filters take the engine of the selected car (Same engine as the car).
        private void OnTargetChanged()
        {
            CarRow target = this.SelectedTarget();
            this.ProposePlacement(target);
            if (target != null && this.follow.Checked)
            {
                this.filling = true;
                this.layout.SelectedItem = target.Layout;
                this.cylinders.SelectedItem = target.Cylinders.ToString(CultureInfo.InvariantCulture);
                this.aspiration.SelectedIndex = Math.Max(0, target.Group);
                this.filling = false;
            }
            this.FillDonors();
        }

        // A filter chosen by hand stops the following.
        private void OnFilterChosen()
        {
            if (this.filling)
            {
                return;
            }
            this.follow.Checked = false;
            this.FillDonors();
        }

        private void UpdateInfo()
        {
            CarRow target = this.SelectedTarget();
            CarRow donor = this.SelectedDonor();
            bool files = this.CanWriteFiles;
            this.convert.Enabled = files;
            this.convertTo.Enabled = files && this.convert.Checked;
            this.placement.Enabled = files;
            this.change.Enabled = target != null && (donor != null || (files && this.placement.SelectedIndex > 0));
            this.listenCurrent.Enabled = target != null;
            this.listenNew.Enabled = donor != null;
            if (this.db == null)
            {
                this.info.Text = "Choose what to change: the FM4 game folder (Xenia's test copy), a DLC package (an installed one is in Xenia's content folder) or a DLC folder.";
                return;
            }
            if (target == null)
            {
                this.info.Text = string.Format(CultureInfo.InvariantCulture, "{0} cars. Pick the car whose sound you want to change; the right list then shows the cars with a similar engine (cylinders, layout, aspiration).", this.cars.Count);
                return;
            }
            string text = string.Format(CultureInfo.InvariantCulture, "{0}: sound {1}, {2} {3} cyl, {4}.", target.Name, target.Sound, target.Layout, target.Cylinders, target.AspirationName);
            if (!target.Own)
            {
                text += "\nThis car's engine is in the game's own database, not in the one open here: it cannot be changed from here.";
            }
            else if (donor != null)
            {
                text += string.Format(CultureInfo.InvariantCulture, "\nIt will play the sound {0} of {1}. {2}", donor.Sound, donor.Name, string.Equals(donor.Sound, target.Sound, StringComparison.OrdinalIgnoreCase) ? "(it is the same sound already)" : "");
                if (files && this.convert.Checked)
                {
                    text += string.Format(CultureInfo.InvariantCulture, " Turned into a {0}-cylinder sound ({1}_ET.xml + <bank>_{0}cyl_HT.xml in the pack, same banks).", this.convertTo.Value, target.Media);
                }
            }
            if (files && this.placement.SelectedIndex > 0)
            {
                bool back = this.placement.SelectedIndex > 1;
                text += string.Format(CultureInfo.InvariantCulture, "\nEngine {0} (car's DB: {1}): {2}_CMT.xml intake z = exhaust z x {3}, engine z = exhaust z x {4}, listener pans / volumes = FM4 {5}-engine medians.",
                    this.placement.SelectedItem, (target.Placement >= 1 && target.Placement <= 3) ? (string) this.placement.Items[(int) target.Placement] : "?", target.Media,
                    back ? SoundConvert.IntakeRatio : SoundConvert.FrontIntakeRatio, back ? SoundConvert.AmbientRatio : SoundConvert.FrontAmbientRatio, back ? "mid" : "front");
            }
            else if (!files && this.db != null)
            {
                text += "\nCylinder conversion and engine position write sound files: open the pack here (DLC package... / DLC folder...).";
            }
            this.info.Text = text;
        }

        private void Apply()
        {
            CarRow target = this.SelectedTarget();
            CarRow donor = this.SelectedDonor();
            bool files = this.CanWriteFiles;
            bool convertOn = files && this.convert.Checked && donor != null;
            int place = files ? this.placement.SelectedIndex : 0;
            if (target == null || (donor == null && place == 0) || this.filling)
            {
                return;
            }
            if (convertOn || place > 0)
            {
                this.ApplyWithFiles(target, donor, convertOn, place);
                return;
            }
            if (this.standalone && XeniaRunning())
            {
                MessageBox.Show(this, "Close Xenia first: it keeps the game and DLC files in use and reads them when it starts.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (this.db.ReadOnly)
            {
                MessageBox.Show(this, "This view holds the game plus its installed DLC packages and is read-only. Turn the DLC button off in the toolbar first.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!target.Own)
            {
                MessageBox.Show(this, "The engine of this car is in the game's own database. Open that database (Media\\db\\gamedb.slt) to change it.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            List<string> users = this.db.EngineUsers(target.EngineId);
            if (users.Count > 1
                && MessageBox.Show(this, "The engine " + target.Sound + " is also the stock engine of " + (users.Count - 1) + " other car(s): " + string.Join(", ", users.GetRange(0, Math.Min(6, users.Count)).ToArray()) + (users.Count > 6 ? ", ..." : "") + ".\nTheir sound changes too. Continue?", this.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            List<string> soundProblems = this.Preview().Diagnose(donor.Sound);
            if (soundProblems.Count > 0
                && MessageBox.Show(this, "The sound " + donor.Sound + " has a problem:\n\n" + string.Join("\n", soundProblems.ToArray()) + "\n\nChange it anyway?", this.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            try
            {
                long rowId = this.db.RowIdOf("Data_Engine", target.EngineId);
                if (rowId < 0)
                {
                    throw new InvalidOperationException("The engine " + target.EngineId + " has no row in this database.");
                }
                Dictionary<string, object> values = new Dictionary<string, object>();
                values["MediaName"] = donor.Sound;
                this.db.UpdateColumns("Data_Engine", rowId, values, "Change sound: " + target.Name + " " + target.Sound + " -> " + donor.Sound + " (" + donor.Name + ")");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string saved = null;
            if (this.standalone)
            {
                try
                {
                    Cursor.Current = Cursors.WaitCursor;
                    saved = this.SaveSource();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "The change is made but not saved:\n\n" + ex.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                finally
                {
                    Cursor.Current = Cursors.Default;
                }
            }
            string previous = target.Sound;
            foreach (CarRow car in this.cars)
            {
                if (car.Own && car.EngineId == target.EngineId)
                {
                    car.Sound = donor.Sound;
                }
            }
            this.FillTargets();
            this.FillDonors();
            if (this.Changed != null)
            {
                this.Changed(this, EventArgs.Empty);
            }
            this.info.Text = target.Name + ": sound " + previous + " -> " + donor.Sound + ((saved != null) ? " (" + saved + ")." : ". Save the database afterwards (File > Save).");
        }

        // Change sound with the options that write files into the pack zip (only with the pack opened here, CanWriteFiles):
        // a donor sound turned into the car's cylinder count (new <car>_ET.xml + <bank>_<n>cyl_HT.xml, same banks; the
        // engine's MediaName = the car's), and / or the car's CMT for the engine position picked (1 front, 2 mid, 3 rear). See SoundConvert.
        private void ApplyWithFiles(CarRow target, CarRow donor, bool convertOn, int place)
        {
            if (XeniaRunning())
            {
                MessageBox.Show(this, "Close Xenia first: it keeps the game and DLC files in use and reads them when it starts.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!target.Own)
            {
                MessageBox.Show(this, "The engine of this car is in the game's own database, not in this pack.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Encoding latin = Encoding.GetEncoding(28591);
            EnginePreview preview = this.Preview();
            string newSound = (donor != null) ? donor.Sound : target.Sound;
            List<string> log = new List<string>();
            Dictionary<string, byte[]> put = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);   // entry under Media/audio/Cars/ -> data
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                if (donor != null)
                {
                    List<string> soundProblems = preview.Diagnose(donor.Sound);
                    if (soundProblems.Count > 0
                        && MessageBox.Show(this, "The sound " + donor.Sound + " has a problem:\n\n" + string.Join("\n", soundProblems.ToArray()) + "\n\nChange it anyway?", this.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    {
                        return;
                    }
                }
                if (convertOn)
                {
                    byte[] tuning = preview.FindTuning(donor.Sound);
                    if (tuning == null)
                    {
                        throw new FileNotFoundException("The engine tuning " + donor.Sound + "_ET.xml was not found in the game or the pack.");
                    }
                    SoundConvert.Result result = SoundConvert.Cylinders(tuning, preview.FindHarmonic, (int) donor.Cylinders, (int) this.convertTo.Value);
                    log.AddRange(result.Log);
                    if (result.Harmonics.Count > 0)
                    {
                        newSound = target.Media;
                        put["Engines/enginetuning/" + target.Media + "_ET.xml"] = latin.GetBytes(result.Tuning);
                        foreach (KeyValuePair<string, string> harmonic in result.Harmonics)
                        {
                            put["Engines/harmonictuning/" + harmonic.Key] = latin.GetBytes(harmonic.Value);
                        }
                        log.Add(target.Media + "_ET.xml = " + donor.Sound + "_ET.xml pointing to them");
                    }
                }
                if (place > 0)
                {
                    byte[] cmt = preview.FindModel(target.Media);
                    if (cmt == null)
                    {
                        throw new FileNotFoundException("The car has no model tuning " + target.Media + "_CMT.xml: add it first with Check sounds > Fix.");
                    }
                    put["CarModelTuning/" + target.Media + "_CMT.xml"] = latin.GetBytes(SoundConvert.Place(latin.GetString(cmt), place >= 2, log));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            bool dbChange = !string.Equals(newSound, target.Sound, StringComparison.OrdinalIgnoreCase);
            if (dbChange)
            {
                List<string> users = this.db.EngineUsers(target.EngineId);
                if (users.Count > 1
                    && MessageBox.Show(this, "The engine " + target.Sound + " is also the stock engine of " + (users.Count - 1) + " other car(s): " + string.Join(", ", users.GetRange(0, Math.Min(6, users.Count)).ToArray()) + (users.Count > 6 ? ", ..." : "") + ".\nTheir sound changes too. Continue?", this.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return;
                }
            }
            string saved;
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                string prefix;
                string zipPath = this.PackZip(out prefix);
                ForzaZip zip = new ForzaZip(zipPath);
                foreach (KeyValuePair<string, byte[]> file in put)
                {
                    Put(zip, prefix + file.Key, file.Value);
                }
                Backup(zipPath);
                zip.Save(zipPath);
                if (dbChange)
                {
                    long rowId = this.db.RowIdOf("Data_Engine", target.EngineId);
                    if (rowId < 0)
                    {
                        throw new InvalidOperationException("The engine " + target.EngineId + " has no row in this database.");
                    }
                    Dictionary<string, object> values = new Dictionary<string, object>();
                    values["MediaName"] = newSound;
                    this.db.UpdateColumns("Data_Engine", rowId, values, "Change sound: " + target.Name + " " + target.Sound + " -> " + newSound + ((donor != null) ? " (" + donor.Name + ")" : ""));
                    saved = this.SaveSource();
                }
                else
                {
                    saved = this.Repack(put.Count + " file(s) written to " + Path.GetFileName(zipPath));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The change is not complete:\n\n" + ex.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            string previous = target.Sound;
            foreach (CarRow car in this.cars)
            {
                if (car.Own && car.EngineId == target.EngineId)
                {
                    car.Sound = newSound;
                }
            }
            this.FillTargets();
            this.FillDonors();
            if (this.Changed != null)
            {
                this.Changed(this, EventArgs.Empty);
            }
            string done = target.Name + ": sound " + previous + " -> " + newSound + " (" + saved + ").";
            this.info.Text = done;
            MessageBox.Show(this, done + "\n\n" + string.Join("\n", log.ToArray()), this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
