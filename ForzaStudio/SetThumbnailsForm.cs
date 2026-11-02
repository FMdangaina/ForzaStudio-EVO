namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Windows.Forms;

    // Tools > Set Generated Thumbnail: the cars of a DLC on the left (an Xbox 360 package or a Xenia DLC
    // folder, or the cars of an FM4 game folder, whose thumbnails are in MediaITEXTURES.ZIP), the thumbnails the game saved in the Xenia profile on the right (XeniaThumbnails). A
    // thumbnail is assigned to a car (button or double-click); Save writes them into the DLC's
    // Thumbnail_<id>.xds files, loose or inside a zip of the DLC, and packs a package again with its own
    // header (StfsPackage.Rewrite; the original is kept in bak\). Meant for the end: the DLC is built first,
    // tried in the game, and the thumbnails come once it works.
    // For a game folder the list is the game database's cars (newest id first, with a filter) and only the
    // cars given a thumbnail are written; Textures.zip is backed up and its zipmanifest line updated.
    public sealed class SetThumbnailsForm : Form
    {
        private const int CarWidth = 128;
        private const int CarHeight = 48;
        private const int PickWidth = 256;
        private const int PickHeight = 96;
        private static readonly Regex ThumbnailName = new Regex(@"(^|[\\/])Thumbnail_(\d+)\.xds$", RegexOptions.IgnoreCase);

        private sealed class Car
        {
            public int Id;
            public string Media;
            public string File;         // the loose file, or the zip holding the entry
            public string Entry;        // the entry in the zip; null for a loose file
            public XdsTexture Current;      // a game folder reads them a few at a time (about 500 textures)
            public bool Tried;
            public XeniaThumbnails.Item Assigned;
            public string DbFile;       // DLC: the merge database holding the car's row (null = no row, the car is a thumbnail file only)
            public string DbEntry;      // the entry of the merge database in the zip DbFile; null = DbFile is the loose database
            public string DbThumb;     // DLC: the row's Thumbnail column
            public string Wanted        // the column the row must have: the file is named by the car's id
            {
                get { return "game:\\Media\\UI\\Textures\\Thumbnails\\Thumbnail_" + this.Id.ToString(CultureInfo.InvariantCulture) + ".xds"; }
            }
        }

        private Timer reader;
        private int readNext;
        private readonly Dictionary<string, ForzaZip> readZips = new Dictionary<string, ForzaZip>(StringComparer.OrdinalIgnoreCase);
        private readonly TextBox dlcBox;
        private readonly ListView carList;
        private readonly ImageList carImages;
        private readonly Label xeniaLabel;
        private readonly ListView pickList;
        private readonly ImageList pickImages;
        private readonly Button assign;
        private readonly Button clear;
        private readonly Button save;
        private readonly Label status;
        private readonly TextBox filterBox;
        private readonly Label carsLabel;
        private readonly List<Car> cars = new List<Car>();
        private readonly List<XeniaThumbnails.Item> picks = new List<XeniaThumbnails.Item>();

        private string root;                // the DLC folder (the work folder of a package)
        private StfsReader package;         // set when the DLC is a package file
        private string gameRoot;           // set when the cars come from an FM4 game folder

        // The game of the DLC or folder being edited: FM4 thumbnails are 768 x 288, FM3's 512 x 256 (same A8R8G8B8 xds, same .xdc), each in its own profile folder.
        private ForzaGame thumbGame = ForzaGame.Motorsport4;

        private int ThumbWidth
        {
            get { return (this.thumbGame == ForzaGame.Motorsport3) ? 512 : 768; }
        }

        private int ThumbHeight
        {
            get { return (this.thumbGame == ForzaGame.Motorsport3) ? 256 : 288; }
        }

        private string ThumbTitle
        {
            get { return (this.thumbGame == ForzaGame.Motorsport3) ? "4D53084D" : "4D530910"; }
        }

        private void SetGame(ForzaGame game)
        {
            if ((game != ForzaGame.Motorsport3 && game != ForzaGame.Motorsport4) || game == this.thumbGame)
            {
                return;
            }
            this.thumbGame = game;
            this.LoadXenia(XeniaThumbnails.FindFolder(this.ThumbTitle));
        }


        public SetThumbnailsForm()
        {
            this.AutoScaleMode = AutoScaleMode.None;
            this.Text = "Set Generated Thumbnail";
            this.ClientSize = new Size(1040, 640);
            this.MinimumSize = new Size(900, 500);
            this.StartPosition = FormStartPosition.CenterParent;
            this.ShowInTaskbar = false;

            Label dlcLabel = new Label();
            dlcLabel.Text = "From:";
            dlcLabel.SetBounds(10, 14, 36, 20);
            this.dlcBox = new TextBox();
            this.dlcBox.ReadOnly = true;
            this.dlcBox.SetBounds(48, 10, 540, 24);
            this.dlcBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Button file = new Button();
            file.Text = "Package file...";
            file.SetBounds(600, 8, 110, 28);
            file.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            file.Click += delegate { this.OpenPackageFile(); };
            Button folder = new Button();
            folder.Text = "DLC folder...";
            folder.SetBounds(720, 8, 120, 28);
            folder.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            folder.Click += delegate { this.OpenDlcFolder(); };

            this.carImages = new ImageList();
            this.carImages.ColorDepth = ColorDepth.Depth32Bit;
            this.carImages.ImageSize = new Size(CarWidth, CarHeight);
            this.carList = new ListView();
            this.carList.View = View.Details;
            this.carList.FullRowSelect = true;
            this.carList.MultiSelect = false;
            this.carList.HideSelection = false;
            this.carList.SmallImageList = this.carImages;
            this.carList.Columns.Add("Car", 270);
            this.carList.Columns.Add("Id", 50);
            this.carList.Columns.Add("Thumbnail", 120);
            this.carList.SetBounds(10, 82, 500, 504);
            this.carList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            this.carList.SelectedIndexChanged += delegate { this.UpdateButtons(); };
            this.carList.DoubleClick += delegate { this.Assign(); };

            this.assign = new Button();
            this.assign.Text = "Assign";
            this.assign.SetBounds(520, 250, 90, 30);
            this.assign.Anchor = AnchorStyles.Left;
            this.assign.Click += delegate { this.Assign(); };
            this.clear = new Button();
            this.clear.Text = "Clear";
            this.clear.SetBounds(520, 290, 90, 30);
            this.clear.Anchor = AnchorStyles.Left;
            this.clear.Click += delegate { this.ClearAssigned(); };

            this.xeniaLabel = new Label();
            this.xeniaLabel.AutoEllipsis = true;
            this.xeniaLabel.SetBounds(620, 54, 310, 20);
            Button xeniaFolder = new Button();
            xeniaFolder.Text = "Folder...";
            xeniaFolder.SetBounds(940, 48, 90, 28);
            xeniaFolder.Click += delegate { this.BrowseXenia(); };
            xeniaFolder.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            this.pickImages = new ImageList();
            this.pickImages.ColorDepth = ColorDepth.Depth32Bit;
            this.pickImages.ImageSize = new Size(PickWidth, PickHeight);
            this.pickList = new ListView();
            this.pickList.View = View.LargeIcon;
            this.pickList.LargeImageList = this.pickImages;
            this.pickList.MultiSelect = false;
            this.pickList.HideSelection = false;
            this.pickList.SetBounds(620, 82, 410, 504);
            this.pickList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.pickList.SelectedIndexChanged += delegate { this.UpdateButtons(); };
            this.pickList.DoubleClick += delegate { this.Assign(); };

            this.carsLabel = new Label();
            this.carsLabel.Text = "CARS";
            this.carsLabel.Font = ForzaTheme.Bold;
            this.carsLabel.ForeColor = ForzaTheme.TextDim;
            this.carsLabel.SetBounds(10, 54, 280, 20);
            this.filterBox = new TextBox();
            this.filterBox.SetBounds(300, 50, 210, 24);
            this.filterBox.TextChanged += delegate { this.RebuildList(); };
            Button game = new Button();
            game.Text = "Game folder...";
            game.SetBounds(850, 8, 180, 28);
            game.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            game.Click += delegate { this.OpenGameFolder(); };

            this.status = new Label();
            this.status.AutoEllipsis = true;
            this.status.SetBounds(10, 604, 700, 24);
            this.status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.save = new Button();
            this.save.Text = "Save";
            this.save.SetBounds(730, 598, 170, 32);
            this.save.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            this.save.Click += delegate { this.SaveThumbnails(); };
            Button close = new Button();
            close.Text = "Close";
            close.SetBounds(920, 598, 110, 32);
            close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            close.Click += delegate { this.Close(); };

            // The main window's look (as in the port window): brand bar on top, dark surfaces.
            Panel body = new Panel();
            body.Size = this.ClientSize;
            body.Dock = DockStyle.Fill;
            body.Controls.AddRange(new Control[] { this.carsLabel, this.filterBox, dlcLabel, this.dlcBox, file, folder, game, this.carList, this.assign, this.clear,
                this.xeniaLabel, xeniaFolder, this.pickList, this.status, this.save, close });
            MenuStrip header = new MenuStrip();
            ToolStripLabel forza = new ToolStripLabel("FORZA");
            forza.Font = new Font("Segoe UI", 11f, FontStyle.Bold | FontStyle.Italic);
            forza.ForeColor = ForzaTheme.Red;
            forza.Margin = new Padding(6, 1, 0, 2);
            ToolStripLabel studio = new ToolStripLabel("STUDIO EVO");
            studio.Font = new Font("Segoe UI Light", 11f, FontStyle.Italic);
            studio.Margin = new Padding(0, 1, 18, 2);
            ToolStripLabel title = new ToolStripLabel("SET GENERATED THUMBNAIL");
            title.Font = ForzaTheme.Bold;
            title.ForeColor = ForzaTheme.TextDim;
            header.Items.Add(forza);
            header.Items.Add(studio);
            header.Items.Add(title);
            this.Font = ForzaTheme.Regular;
            try
            {
                this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (ArgumentException)
            {
            }
            this.Controls.Add(body);
            this.Controls.Add(header);
            this.MainMenuStrip = header;
            this.BackColor = ForzaTheme.Background;
            this.ForeColor = ForzaTheme.Text;
            ForzaTheme.Apply(this);
            this.Shown += delegate { this.LoadXenia(XeniaThumbnails.FindFolder()); };
            this.UpdateButtons();
        }

        // The package's work folder is left to the temp folder; a new open replaces it.
        private static string WorkRoot
        {
            get { return Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "DlcThumbs"); }
        }

        private void OpenPackageFile()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "DLC package";
                dialog.Filter = "Xbox 360 package (CON, LIVE, PIRS)|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                try
                {
                    Cursor.Current = Cursors.WaitCursor;
                    this.status.Text = "Extracting " + Path.GetFileName(dialog.FileName) + "...";
                    this.status.Refresh();
                    StfsReader opened = StfsReader.Open(dialog.FileName);
                    this.SetGame(GameInfo.FromTitleId(opened.TitleId));
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
                    this.package = opened;
                    this.dlcBox.Text = dialog.FileName;
                    this.LoadCars(work);
                }
                catch (Exception exception)
                {
                    MessageBox.Show(this, exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Hand);
                }
                finally
                {
                    Cursor.Current = Cursors.Default;
                }
            }
        }

        private void OpenDlcFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "The DLC folder in Xenia's content folder (it holds Media\\DLCZips).";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                this.package = null;
                this.dlcBox.Text = dialog.SelectedPath;
                this.SetGame(dialog.SelectedPath.IndexOf("4D53084D", StringComparison.OrdinalIgnoreCase) >= 0 ? ForzaGame.Motorsport3 : ForzaGame.Motorsport4);
                try
                {
                    Cursor.Current = Cursors.WaitCursor;
                    this.LoadCars(dialog.SelectedPath);
                }
                catch (Exception exception)
                {
                    MessageBox.Show(this, exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Hand);
                }
                finally
                {
                    Cursor.Current = Cursors.Default;
                }
            }
        }

        // The cars are the Thumbnail_<id>.xds files of the DLC, loose or in a zip; their names come from the
        // DLC's merge database (db\patch\*_merge.slt) when it is loose.
        private void LoadCars(string folder)
        {
            this.gameRoot = null;
            this.root = folder;
            this.cars.Clear();
            this.carList.Items.Clear();
            this.carImages.Images.Clear();
            Dictionary<int, string> names = new Dictionary<int, string>();
            List<Car> rows = new List<Car>();       // the cars of the merge database(s), with or without a picture
            // The merge database is loose, or an entry of a zip of the pack (Media/db/patch/<n>_merge.slt).
            foreach (string path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (path.EndsWith("_merge.slt", StringComparison.OrdinalIgnoreCase))
                {
                    this.ReadMerge(path, null, File.ReadAllBytes(path), names, rows);
                }
                else if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(path).IndexOf("StringTables", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    ForzaZip zip = new ForzaZip(path);
                    foreach (ForzaZip.Entry entry in zip.Entries)
                    {
                        if (entry.Name.EndsWith("_merge.slt", StringComparison.OrdinalIgnoreCase))
                        {
                            this.ReadMerge(path, entry.Name, zip.Read(entry), names, rows);
                        }
                    }
                }
            }
            // Every picture of the DLC by file name, then each database row takes the one its Thumbnail column names
            // (Thumbnail_431.xds under the car 8040 is that car's); what no row claims stays a car of its own.
            Dictionary<string, Car> found = new Dictionary<string, Car>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                Match match = ThumbnailName.Match(path);
                if (match.Success)
                {
                    Car car = this.NewCar(int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), path, null, File.ReadAllBytes(path));
                    found[Path.GetFileName(path)] = car;
                }
                else if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(path).IndexOf("StringTables", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    ForzaZip zip = new ForzaZip(path);
                    foreach (ForzaZip.Entry entry in zip.Entries)
                    {
                        match = ThumbnailName.Match(entry.Name);
                        if (match.Success)
                        {
                            found[Path.GetFileName(entry.Name)] = this.NewCar(int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), path, entry.Name, zip.Read(entry));
                        }
                    }
                }
            }
            foreach (Car dbCar in rows)
            {
                Car picture = null;
                string file = null;
                if (dbCar.DbThumb != null)
                {
                    file = dbCar.DbThumb.Replace('/', '\\');
                    file = file.Substring(file.LastIndexOf('\\') + 1);
                    found.TryGetValue(file, out picture);
                }
                if (picture != null)
                {
                    dbCar.File = picture.File;
                    dbCar.Entry = picture.Entry;
                    dbCar.Current = picture.Current;
                    found.Remove(file);
                }
                dbCar.Tried = true;
                this.cars.Add(dbCar);
            }
            foreach (Car car in found.Values)
            {
                string media;
                names.TryGetValue(car.Id, out media);
                car.Media = media;
                this.cars.Add(car);
            }
            this.carsLabel.Text = "CARS IN THE DLC";
            this.RebuildList();
            this.status.Text = this.cars.Count == 0 ? "No Thumbnail_<id>.xds in this DLC." : string.Format("{0} cars. Pick a car, then a thumbnail, and Assign.", this.cars.Count);
        }

        // The cars of an FM4 game folder: Data_Car of its database, each with the thumbnail file its Thumbnail
        // column points at (in Media\UI\Textures.zip). The textures are not read until one is assigned.
        private void OpenGameFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "The FM4 or FM3 game folder (the one that contains Media): the test copies FM4_Extracted_DEV, FM3_Extracted_DEV.";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                try
                {
                    Cursor.Current = Cursors.WaitCursor;
                    this.LoadGameCars(dialog.SelectedPath);
                }
                catch (Exception exception)
                {
                    MessageBox.Show(this, exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Hand);
                }
                finally
                {
                    Cursor.Current = Cursors.Default;
                }
            }
        }

        private void LoadGameCars(string folder)
        {
            string db = Path.Combine(Path.Combine(Path.Combine(folder, "Media"), "db"), "gamedb.slt");
            if (!File.Exists(db))
            {
                throw new FileNotFoundException("Media\\db\\gamedb.slt not found: choose the FM4 or FM3 game folder (the one that contains Media).");
            }
            string clean = Path.GetFullPath(folder).TrimEnd('\\');
            if (clean.EndsWith("\\FM4_Extracted", StringComparison.OrdinalIgnoreCase) || clean.EndsWith("\\FM3_Extracted", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(Path.GetFileName(clean) + " is the clean game and is never written: use the test copy " + Path.GetFileName(clean) + "_DEV.");
            }
            this.SetGame(GameInfo.Detect(folder));
            this.package = null;
            this.gameRoot = folder;
            this.root = folder;
            this.dlcBox.Text = folder;
            this.cars.Clear();
            this.readZips.Clear();
            int missing = 0;
            using (SqliteDatabase game = SqliteDatabase.FromBytes(File.ReadAllBytes(db)))
            {
                foreach (object[] row in game.Query("SELECT Id, MediaName, Thumbnail FROM Data_Car").Rows)
                {
                    GamePaths.GameFile file = GamePaths.Locate(folder, Convert.ToString(row[2], CultureInfo.InvariantCulture));
                    if (file == null)
                    {
                        missing++;
                        continue;
                    }
                    Car car = new Car();
                    car.Id = Convert.ToInt32(row[0], CultureInfo.InvariantCulture);
                    car.Media = Convert.ToString(row[1], CultureInfo.InvariantCulture);
                    car.File = file.InZip ? file.ZipPath : file.FilePath;
                    car.Entry = file.InZip ? file.EntryName : null;
                    this.cars.Add(car);
                }
            }
            this.carsLabel.Text = "CARS IN THE GAME";
            this.RebuildList();
            this.status.Text = string.Format("{0} cars{1}. Pick a car and a thumbnail, Assign.", this.cars.Count,
                (missing > 0) ? " (" + missing + " without a thumbnail file left out)" : "");
        }

        // The list again from 'cars' through the filter (name or id); a game folder shows the newest ids first,
        // where the ported cars are.
        private void RebuildList()
        {
            string filter = this.filterBox.Text.Trim();
            List<Car> shown = this.cars.FindAll(delegate(Car c)
            {
                return filter.Length == 0 || (c.Media ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                    || c.Id.ToString(CultureInfo.InvariantCulture).IndexOf(filter, StringComparison.Ordinal) >= 0;
            });
            bool newestFirst = this.gameRoot != null;
            shown.Sort(delegate(Car a, Car b) { return newestFirst ? b.Id.CompareTo(a.Id) : a.Id.CompareTo(b.Id); });
            this.carList.BeginUpdate();
            this.carList.Items.Clear();
            this.carImages.Images.Clear();
            foreach (Car car in shown)
            {
                ListViewItem item = new ListViewItem(car.Media ?? ("Car " + car.Id));
                item.SubItems.Add(car.Id.ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add("");
                item.Tag = car;
                this.ShowRow(item);
                this.carList.Items.Add(item);
            }
            this.carList.EndUpdate();
            this.UpdateButtons();
            this.StartReading();
        }

        // Game folder: the current thumbnails of the rows are read a few per tick so the window stays alive.
        private void StartReading()
        {
            if (this.reader == null)
            {
                this.reader = new Timer();
                this.reader.Interval = 15;
                this.reader.Tick += delegate { this.ReadSome(); };
                this.FormClosed += delegate { this.reader.Stop(); };
            }
            this.readNext = 0;
            this.reader.Enabled = this.gameRoot != null;
        }

        private void ReadSome()
        {
            int done = 0;
            while (this.readNext < this.carList.Items.Count && done < 6)
            {
                ListViewItem row = this.carList.Items[this.readNext++];
                Car car = (Car) row.Tag;
                if (car.Current != null || car.Tried)
                {
                    continue;
                }
                car.Tried = true;
                done++;
                try
                {
                    byte[] data;
                    if (car.Entry == null)
                    {
                        data = File.ReadAllBytes(car.File);
                    }
                    else
                    {
                        ForzaZip zip;
                        if (!this.readZips.TryGetValue(car.File, out zip))
                        {
                            zip = new ForzaZip(car.File);
                            this.readZips[car.File] = zip;
                        }
                        data = zip.Read(zip.Find(car.Entry));
                    }
                    car.Current = new XdsTexture(data);
                    this.ShowRow(row);
                }
                catch (Exception)
                {
                }
            }
            if (this.readNext >= this.carList.Items.Count)
            {
                this.reader.Stop();
            }
        }

        // The row's picture and state: the assigned thumbnail, else the current one when it was read.
        private void ShowRow(ListViewItem row)
        {
            Car car = (Car) row.Tag;
            XdsTexture shown = (car.Assigned != null) ? car.Assigned.Texture : car.Current;
            if (shown != null)
            {
                this.carImages.Images.Add(XeniaThumbnails.Preview(shown, CarWidth, CarHeight));
                row.ImageIndex = this.carImages.Images.Count - 1;
            }
            else
            {
                row.ImageIndex = -1;
            }
            row.SubItems[2].Text = (car.Assigned != null) ? car.Assigned.Name : (car.Current != null ? "current" : "");
        }

        private void ReadMerge(string file, string entry, byte[] data, Dictionary<int, string> names, List<Car> rows)
        {
            using (SqliteDatabase db = SqliteDatabase.FromBytes(data))
            {
                foreach (object[] row in db.Query("SELECT Id, MediaName, Thumbnail FROM Data_Car").Rows)
                {
                    Car dbCar = new Car();
                    dbCar.Id = Convert.ToInt32(row[0], CultureInfo.InvariantCulture);
                    dbCar.Media = Convert.ToString(row[1], CultureInfo.InvariantCulture);
                    dbCar.DbFile = file;
                    dbCar.DbEntry = entry;
                    dbCar.DbThumb = (row[2] is string) ? (string) row[2] : null;
                    names[dbCar.Id] = dbCar.Media;
                    rows.Add(dbCar);
                }
            }
        }

        private Car NewCar(int id, string file, string entry, byte[] data)
        {
            Car car = new Car();
            car.Id = id;
            car.File = file;
            car.Entry = entry;
            car.Current = new XdsTexture(data);
            return car;
        }

        private void BrowseXenia()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "The ForzaProfile\\Thumbnails folder of the Xenia profile (it holds Thumbnail_N.xdc).";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    XeniaThumbnails.Remember(dialog.SelectedPath, this.ThumbTitle);
                    this.LoadXenia(dialog.SelectedPath);
                }
            }
        }

        private void LoadXenia(string folder)
        {
            this.pickList.Items.Clear();
            this.pickImages.Images.Clear();
            this.picks.Clear();
            if (folder == null || !Directory.Exists(folder))
            {
                this.xeniaLabel.Text = "Thumbnails folder not found: choose it with Folder...";
                return;
            }
            this.xeniaLabel.Text = folder;
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                foreach (XeniaThumbnails.Item item in XeniaThumbnails.Load(folder, this.ThumbWidth, this.ThumbHeight, "A8R8G8B8"))
                {
                    this.pickImages.Images.Add(XeniaThumbnails.Preview(item.Texture, PickWidth, PickHeight));
                    this.picks.Add(item);
                    this.pickList.Items.Add(new ListViewItem(item.Name + "  " + item.Time.ToString("dd/MM HH:mm"), this.picks.Count - 1));
                }
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            this.UpdateButtons();
        }

        private Car SelectedCar
        {
            get { return (this.carList.SelectedItems.Count == 1) ? (Car) this.carList.SelectedItems[0].Tag : null; }
        }

        private void UpdateButtons()
        {
            Car car = this.SelectedCar;
            this.assign.Enabled = car != null && this.pickList.SelectedItems.Count == 1;
            this.clear.Enabled = car != null && car.Assigned != null;
            int count = this.cars.FindAll(delegate(Car c) { return c.Assigned != null; }).Count;
            this.save.Enabled = count > 0;
            this.save.Text = count > 0 ? "Save " + count + ((count == 1) ? " thumbnail" : " thumbnails") : "Save";
        }

        private void Assign()
        {
            Car car = this.SelectedCar;
            if (car == null || this.pickList.SelectedItems.Count != 1)
            {
                return;
            }
            ListViewItem row = this.carList.SelectedItems[0];
            car.Assigned = this.picks[this.pickList.SelectedItems[0].ImageIndex];
            this.ShowRow(row);
            this.carList.Invalidate();
            // On to the next car, to go down the list.
            int next = row.Index + 1;
            if (next < this.carList.Items.Count)
            {
                this.carList.Items[next].Selected = true;
                this.carList.Items[next].EnsureVisible();
            }
            this.UpdateButtons();
        }

        private void ClearAssigned()
        {
            Car car = this.SelectedCar;
            if (car == null || car.Assigned == null)
            {
                return;
            }
            ListViewItem row = this.carList.SelectedItems[0];
            car.Assigned = null;
            this.ShowRow(row);
            this.carList.Invalidate();
            this.UpdateButtons();
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

        private void SaveThumbnails()
        {
            if (this.root == null)
            {
                return;
            }
            if (XeniaRunning())
            {
                MessageBox.Show(this, "Close Xenia first: it keeps the DLC and game files in use and reads them when it starts.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                this.status.Text = "Writing the thumbnails...";
                this.status.Refresh();
                int written = 0;
                List<string> changedDbs = new List<string>();
                // The zips are opened once each, the loose files written straight.
                Dictionary<string, ForzaZip> zips = new Dictionary<string, ForzaZip>(StringComparer.OrdinalIgnoreCase);
                foreach (Car car in this.cars)
                {
                    if (car.Assigned == null)
                    {
                        continue;
                    }
                    string wantedName = "Thumbnail_" + car.Id.ToString(CultureInfo.InvariantCulture) + ".xds";
                    if (car.DbFile != null && (car.File == null || !string.Equals(Path.GetFileName(car.Entry ?? car.File), wantedName, StringComparison.OrdinalIgnoreCase)))
                    {
                        // No picture yet, or one named after another car (Thumbnail_431 under car 8040): the car's own
                        // Thumbnail_<id>.xds, loose in the DLC, and the database row points at it.
                        if (car.DbEntry != null)
                        {
                            // The game mounts the pack's zip (Media\DLCZips\<n>_pri_65.zip) and nothing outside it: the picture goes in the
                            // zip of the car's database row, beside it.
                            ForzaZip packZip;
                            if (!zips.TryGetValue(car.DbFile, out packZip))
                            {
                                packZip = new ForzaZip(car.DbFile);
                                zips[car.DbFile] = packZip;
                            }
                            int media = car.DbEntry.IndexOf("Media/", StringComparison.OrdinalIgnoreCase);
                            string entryName = (media > 0 ? car.DbEntry.Substring(0, media) : "") + "Media/UI/Textures/Thumbnails/" + wantedName;
                            ForzaZip.Entry existing = packZip.Find(entryName);
                            if (existing != null)
                            {
                                packZip.Replace(existing, car.Assigned.Xds);
                            }
                            else
                            {
                                packZip.Add(entryName, car.Assigned.Xds);
                            }
                            car.File = car.DbFile;
                            car.Entry = entryName;
                        }
                        else
                        {
                            string folder = Path.Combine(Path.Combine(Path.Combine(Path.Combine(this.root, "Media"), "UI"), "Textures"), "Thumbnails");
                            Directory.CreateDirectory(folder);
                            car.File = Path.Combine(folder, wantedName);
                            car.Entry = null;
                            File.WriteAllBytes(car.File, car.Assigned.Xds);
                        }
                        string dbKey = car.DbFile + "|" + car.DbEntry;
                        if (!changedDbs.Contains(dbKey))
                        {
                            changedDbs.Add(dbKey);
                        }
                        car.DbThumb = null;
                    }
                    else if (car.Entry == null)
                    {
                        File.WriteAllBytes(car.File, car.Assigned.Xds);
                    }
                    else
                    {
                        ForzaZip zip;
                        if (!zips.TryGetValue(car.File, out zip))
                        {
                            zip = new ForzaZip(car.File);
                            zips[car.File] = zip;
                        }
                        zip.Replace(zip.Find(car.Entry), car.Assigned.Xds);
                    }
                    written++;
                }
                // The merge databases whose rows now point at the car's own file (loose, or an entry of a zip saved below).
                foreach (string key in changedDbs)
                {
                    string[] part = key.Split('|');
                    string slt = part[0], entryName = (part[1].Length > 0) ? part[1] : null;
                    ForzaZip dbZip = null;
                    ForzaZip.Entry dbEntry = null;
                    byte[] bytes;
                    if (entryName == null)
                    {
                        bytes = File.ReadAllBytes(slt);
                    }
                    else
                    {
                        if (!zips.TryGetValue(slt, out dbZip))
                        {
                            dbZip = new ForzaZip(slt);
                            zips[slt] = dbZip;
                        }
                        dbEntry = dbZip.Find(entryName);
                        bytes = dbZip.Read(dbEntry);
                    }
                    using (SqliteDatabase db = SqliteDatabase.FromBytes(bytes))
                    {
                        foreach (Car car in this.cars)
                        {
                            if (car.DbFile == slt && car.DbEntry == entryName && car.Assigned != null && car.DbThumb == null)
                            {
                                db.Execute("UPDATE Data_Car SET Thumbnail = ? WHERE Id = ?", car.Wanted, car.Id);
                                car.DbThumb = car.Wanted;
                            }
                        }
                        if (entryName == null)
                        {
                            File.WriteAllBytes(slt, db.ToBytes());
                        }
                        else
                        {
                            dbZip.Replace(dbEntry, db.ToBytes());
                        }
                    }
                }
                string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
                string manifest = (this.gameRoot != null) ? ZipManifest.PathFor(this.gameRoot) : null;
                bool manifestBackedUp = false;
                foreach (KeyValuePair<string, ForzaZip> zip in zips)
                {
                    if (this.gameRoot != null)
                    {
                        Backup(zip.Key, stamp);
                    }
                    zip.Value.Save(zip.Key);
                    // A game zip changed: its line in zipmanifest.xml gets the new central directory position.
                    string gamePath = (this.gameRoot != null) ? ZipManifest.GamePath(this.gameRoot, Path.GetFullPath(zip.Key)) : null;
                    if (gamePath != null && ZipManifest.Lists(manifest, gamePath))
                    {
                        if (!manifestBackedUp)
                        {
                            Backup(manifest, stamp);
                            manifestBackedUp = true;
                        }
                        ZipManifest.Update(manifest, gamePath, zip.Key);
                    }
                }
                string packed = "";
                if (this.package != null)
                {
                    packed = this.Pack();
                }
                foreach (Car car in this.cars)
                {
                    if (car.Assigned != null)
                    {
                        car.Current = car.Assigned.Texture;
                        car.Assigned = null;
                    }
                }
                foreach (ListViewItem row in this.carList.Items)
                {
                    Car car = (Car) row.Tag;
                    this.ShowRow(row);
                }
                this.carList.Invalidate();
                this.status.Text = string.Format("{0} thumbnails saved{1}.", written, packed);
                this.UpdateButtons();
            }
            catch (Exception exception)
            {
                this.status.Text = "Save failed";
                MessageBox.Show(this, exception.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        // A copy in bak\ next to the exe, as the editor does before it overwrites a game file.
        private static void Backup(string path, string stamp)
        {
            if (!BackupPolicy.Enabled)
            {
                return;
            }
            string folder = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "bak");
            Directory.CreateDirectory(folder);
            File.Copy(path, Path.Combine(folder, stamp + "_bak_" + Path.GetFileName(path)), true);
        }

        // The package file again from the work folder, with its own header; the original is kept in bak\.
        private string Pack()
        {
            string target = this.package.Path;
            string temp = target + ".new";
            try
            {
                string backup = null;
                if (BackupPolicy.Enabled)
                {
                    string backups = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "bak");
                    Directory.CreateDirectory(backups);
                    backup = Path.Combine(backups, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + "_bak_" + Path.GetFileName(target));
                    File.Copy(target, backup, true);
                }
                StfsPackage.Rewrite(this.root, temp, this.package.Header);
                StfsReader check = StfsReader.Open(temp);
                File.Delete(target);
                File.Move(temp, target);
                check.Path = target;
                this.package = check;
                return ", package " + Path.GetFileName(target) + " written" + ((backup != null) ? " (original in bak\\" + Path.GetFileName(backup) + ")" : " (no backup: Backup is off in the ini)");
            }
            catch (Exception)
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
                throw;
            }
        }
    }
}
