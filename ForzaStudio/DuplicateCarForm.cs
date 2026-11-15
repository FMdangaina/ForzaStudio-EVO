namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Windows.Forms;

    // Tools > Duplicate Car (ROADMAP 18.3 / 18.4): a five-step wizard around CarDuplicate.
    //   1 Car      the car to copy and the new MediaName
    //   2 Names    DisplayName, ModelShort, year, make
    //   3 Data     the main Data_Car numbers, editable (the copy keeps the source's wheel and paint colours)
    //   4 Engine   shared with the source (as before) or an engine of its own, a copy of the source's that can be
    //              changed (CarEngine, ROADMAP 2.3.2)
    //   5 Summary  the plan (nothing written), then Create
    internal sealed class DuplicateCarForm : Form
    {
        private sealed class CarItem
        {
            public string MediaName;
            public string Text;
            public string PackageFile;      // the installed package the car's files come from (disc 2, DLC); null = the game folder

            public override string ToString()
            {
                return this.Text;
            }
        }

        private sealed class MakeItem
        {
            public long Id;
            public string Text;

            public override string ToString()
            {
                return this.Text;
            }
        }

        // The Data_Car columns offered for a copy, with what they are (a reference id is checked by the database rules).
        // Not offered: the ids and names the wizard sets itself, the engine's Sim* numbers (the engine page), the steering and
        // traction tuning.
        private static readonly string[][] DataFields =
        {
            new string[] { "HasLivery", "1 = the car has the livery editor" },
            new string[] { "HasRaceLivery", "1 = it has a race livery" },
            new string[] { "IsUnicorn", "1 = marked as a unicorn (rare) car" },
            new string[] { "IsArcade", "1 = in the arcade car list" },
            new string[] { "IsDrivable", "0 = showroom only" },
            new string[] { "HasCarExperience", "1 = has an Autovista" },
            new string[] { "IsCarExperienceUnlocked", "1 = its Autovista is unlocked" },
            new string[] { "IsPurchased", "1 = already owned" },
            new string[] { "WingMask", "wing mask of the livery" },
            new string[] { "BaseCost", "price in credits" },
            new string[] { "BaseRarity", "rarity" },
            new string[] { "ClassID", "CarClasses.Id (D, C, B, A, S, R...)" },
            new string[] { "CarClassModifier", "class modifier" },
            new string[] { "PerformanceIndex", "PI as 0-1 (shown x 1000 + 100)" },
            new string[] { "CarTypeID", "List_CarType: 1 production, 2 tuned, 3 race" },
            new string[] { "FamilyModelID", "List_FamilyModel.Id (the Club events)" },
            new string[] { "FamilyBodyID", "List_FamilyBody.Id (body type)" },
            new string[] { "FamilySpecialID", "List_FamilySpecial" },
            new string[] { "EnginePlacementID", "List_EnginePlacement" },
            new string[] { "MaterialTypeID", "List_MaterialType" },
            new string[] { "DriveTypeID", "List_DriveType: 1 FWD, 2 RWD, 3 AWD" },
            new string[] { "RegionID", "List_Region: 1 Asia, 2 Europe, 3 America" },
            new string[] { "CountryID", "List_Country" },
            new string[] { "CurbWeight", "kg" },
            new string[] { "WeightDistribution", "front share, %" },
            new string[] { "NumGears", "gears" },
            new string[] { "Displacement", "cc (the engine page sets it for an own engine)" },
            new string[] { "FrontTireWidthMM", "mm" },
            new string[] { "FrontTireAspect", "profile, %" },
            new string[] { "FrontWheelDiameterIN", "inches" },
            new string[] { "RearTireWidthMM", "mm" },
            new string[] { "RearTireAspect", "profile, %" },
            new string[] { "RearWheelDiameterIN", "inches" },
            new string[] { "TireBrandID", "List_TireBrand" },
            new string[] { "StockWheelID", "List_Wheels.ID (the stock wheel)" },
            new string[] { "FrontStockRideHeight", "m" },
            new string[] { "RearStockRideHeight", "m" },
            new string[] { "CaliperAngleFront", "brake caliper angle" },
            new string[] { "CaliperAngleRear", "brake caliper angle" },
            new string[] { "CaliperRGBFront", "caliper colour (RGB number)" },
            new string[] { "CaliperRGBRear", "caliper colour (RGB number)" },
            new string[] { "GaugeID", "List_DigitalGauges.GaugeID" },
            new string[] { "SteerMaxAngle", "steering lock" },
            new string[] { "SpeedLimiterID", "speed limiter" },
            new string[] { "TopSpeed-mph", "shown in the car's stats" },
            new string[] { "Time:0-60-sec", "shown in the car's stats" },
            new string[] { "Time:0-100-sec", "shown in the car's stats" },
            new string[] { "QuarterMileTime-sec", "shown in the car's stats" },
            new string[] { "QuarterMileSpeed-mph", "shown in the car's stats" },
            new string[] { "HandlingRating", "rating bar" },
            new string[] { "SpeedRating", "rating bar" },
            new string[] { "AccelerationRating", "rating bar" },
            new string[] { "BrakingRating", "rating bar" },
            new string[] { "LaunchRating", "rating bar" },
            new string[] { "GameTorqueScale", "game torque scale" },
            new string[] { "GameDragScale", "game drag scale" },
            new string[] { "BodyAeroLongitudinalDrag", "aero" },
            new string[] { "BodyAeroVerticalDrag", "aero" },
            new string[] { "BodyAeroForwardDownforceFront", "aero" },
            new string[] { "BodyAeroForwardDownforceRear", "aero" },
        };

        private readonly string target;
        private string xeniaContent;
        private readonly Panel[] pages = new Panel[5];
        private readonly ComboBox car;
        private readonly TextBox newName;
        private readonly NumericUpDown carId;               // the copy's car Id: the next free one, or one the user chooses
        private Dictionary<long, string> packCars = new Dictionary<long, string>();     // car Ids of the installed packs
        private readonly Dictionary<long, string> gameCars = new Dictionary<long, string>();   // car Ids of the game database
        private readonly Label problem;
        private readonly RadioButton toGame;
        private readonly RadioButton toPack;
        private readonly RadioButton toFile;                // the pack as an STFS package file (Xbox 360; Xenia reads it too)
        private string packageOutput;                       // the folder the package file goes in, asked when the copy is created
        private readonly TextBox packName;
        private readonly NumericUpDown packOffer;
        private readonly TextBox displayName;
        private readonly TextBox modelShort;
        private readonly NumericUpDown year;
        private readonly ComboBox make;
        private readonly DataGridView grid;
        private readonly CheckBox showAll;
        private readonly RadioButton engineShared;
        private readonly RadioButton engineOwn;
        private readonly Button engineEdit;
        private readonly Label engineSummary;
        private EngineSpec engineSpec;
        private readonly CheckBox gearboxOwn;
        private readonly Button gearboxEdit;
        private readonly Label gearboxSummary;
        private GearboxSpec gearboxSpec;
        private readonly TextBox summary;
        private readonly Button back;
        private readonly Button next;
        private readonly Button cancel;
        private readonly Dictionary<string, object> original = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, object> carRow = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private int page;
        private List<string> taken = new List<string>();
        private StringTable names;

        public string CreatedName;
        private bool replacePack;

        // The content folder of the packs (null until Xenia's folder is known) and the next free offer.
        private void UseXeniaContent(string content)
        {
            this.xeniaContent = content;
            this.packCars = (content == null) ? new Dictionary<long, string>() : DbIds.PackCarIds(DbIds.PackDatabases(content));
            this.SuggestCarId();
            if (content == null)
            {
                return;
            }
            try
            {
                ForzaGame packGame = GameInfo.Detect(this.target);
                int free = (packGame == ForzaGame.Motorsport3) ? DlcPackage.FreeOfferFm3(this.target, content) : (packGame == ForzaGame.Motorsport2) ? DlcPackage.FreeOfferFm2(content) : DlcPackage.FreeOffer(this.target, content);
                this.packOffer.Value = Math.Max(this.packOffer.Minimum, Math.Min(this.packOffer.Maximum, free));
            }
            catch (Exception)
            {
                this.packOffer.Value = 91;
            }
        }

        // The first free car Id (above every car of FM4 and its DLC, free in the game and the installed packs) in the Id box.
        private void SuggestCarId()
        {
            if (this.carId == null)
            {
                return;
            }
            ForzaGame game = GameInfo.Detect(this.target);
            long id = (game == ForzaGame.Motorsport2) ? DbIds.Fm2FirstFreeCarId : (game == ForzaGame.Motorsport3) ? GameIdLimits.MaxCar(game) + 1 : DbIds.InstallFirstCar;
            while (this.gameCars.ContainsKey(id) || this.packCars.ContainsKey(id) || (game == ForzaGame.Motorsport2 && !DbIds.Fm2UsableCarId(id)))
            {
                id = (game == ForzaGame.Motorsport2) ? DbIds.Fm2SkipDlcIds(id + 1) : id + 1;
            }
            this.carId.Value = Math.Max(this.carId.Minimum, Math.Min(this.carId.Maximum, id));
        }

        // Xenia's folder was not set in Settings: ask for it here and remember it.
        private bool AskXeniaFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Xenia's folder (the one with xenia.exe or xenia_canary.exe): the DLC pack is written to its content folder.";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return false;
                }
                try
                {
                    this.UseXeniaContent(DlcPackage.XeniaContent(dialog.SelectedPath, InstalledDlc.TitleId(GameInfo.Detect(this.target)).ToString("X8", CultureInfo.InvariantCulture)));
                }
                catch (IOException exception)
                {
                    MessageBox.Show(this, exception.Message, "Duplicate Car", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    return false;
                }
                AppSettings.Set(SettingsForm.XeniaKey, dialog.SelectedPath);
                return true;
            }
        }

        public DuplicateCarForm(string target, string xeniaContent)
        {
            this.target = target;
            this.xeniaContent = xeniaContent;
            this.AutoScaleMode = AutoScaleMode.None;
            this.Text = "Duplicate Car";
            this.ClientSize = new Size(600, 420);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;

            try
            {
                this.names = StringTable.Load(target, "EN");
            }
            catch (Exception)
            {
            }
            List<CarItem> cars = new List<CarItem>();
            List<MakeItem> makes = new List<MakeItem>();
            using (SqliteDatabase db = SqliteDatabase.FromBytes(File.ReadAllBytes(CarMake.DatabasePath(target))))
            {
                foreach (object[] row in db.Query("SELECT MediaName, DisplayName, ModelShort, Year FROM Data_Car ORDER BY MediaName").Rows)
                {
                    CarItem item = new CarItem();
                    item.MediaName = (string) row[0];
                    string shown = this.Lookup("Data_Car", "ModelShort", row[2] as string);
                    item.Text = item.MediaName + (string.IsNullOrEmpty(shown) ? "" : "   " + shown);
                    cars.Add(item);
                    this.taken.Add(item.MediaName);
                }
                foreach (object[] row in db.Query("SELECT Id, MediaName FROM Data_Car").Rows)
                {
                    this.gameCars[Convert.ToInt64(row[0], CultureInfo.InvariantCulture)] = row[1] as string;
                }
                if (DbIds.HasTable(db, "Data_CarBody"))
                {
                    foreach (object[] row in db.Query("SELECT Id FROM Data_CarBody").Rows)
                    {
                        long body = Convert.ToInt64(row[0], CultureInfo.InvariantCulture);
                        if (body % 1000 == 0 && !this.gameCars.ContainsKey(body / 1000))
                        {
                            this.gameCars[body / 1000] = "a car body row";
                        }
                    }
                }
                foreach (object[] row in db.Query("SELECT ID, DisplayName FROM List_CarMake ORDER BY ID").Rows)
                {
                    MakeItem item = new MakeItem();
                    item.Id = Convert.ToInt64(row[0], CultureInfo.InvariantCulture);
                    string shown = this.Lookup("List_CarMake", "DisplayName", row[1] as string);
                    item.Text = string.IsNullOrEmpty(shown) ? "make " + item.Id : shown;
                    makes.Add(item);
                }
            }
            makes.Sort(delegate(MakeItem a, MakeItem b) { return string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase); });
            this.AddPackageCars(cars);

            for (int i = 0; i < this.pages.Length; i++)
            {
                this.pages[i] = new Panel();
                this.pages[i].SetBounds(0, 0, 600, 370);
                this.Controls.Add(this.pages[i]);
            }

            // 1 Car
            this.AddLabel(0, "The car to copy and the name of the copy. The copy gets the next free car Id (above every car of FM4 and its DLC), or the one you write, and its own strings.", 12, 12, 576, 36);
            this.AddLabel(0, "Car:", 12, 64, 90, 20);
            this.car = new ComboBox();
            this.car.DropDownStyle = ComboBoxStyle.DropDownList;
            this.car.SetBounds(106, 61, 480, 24);
            this.car.MaxDropDownItems = 20;
            this.car.Items.AddRange(cars.ToArray());
            this.car.SelectedIndexChanged += delegate { this.CarChanged(); };
            this.pages[0].Controls.Add(this.car);
            this.AddLabel(0, "New MediaName:", 12, 100, 94, 20);
            this.newName = new TextBox();
            this.newName.SetBounds(106, 97, 240, 24);
            this.newName.MaxLength = 40;
            this.newName.TextChanged += delegate { this.Recheck(); };
            new ToolTip().SetToolTip(this.newName, "The car's file name in the game (like ACU_NSX_97): letters, digits and underscores; the zip is Media\\cars\\<name>.zip");
            this.pages[0].Controls.Add(this.newName);
            this.AddLabel(0, "Car Id:", 356, 100, 50, 20);
            this.carId = new NumericUpDown();
            this.carId.Minimum = 1;
            this.carId.Maximum = DbIds.MaxChosenCarId;
            this.carId.SetBounds(408, 97, 90, 24);
            this.carId.ValueChanged += delegate { this.Recheck(); };
            this.carId.KeyUp += delegate { this.Recheck(); };
            new ToolTip().SetToolTip(this.carId, "The copy's Data_Car.Id (its rows take Id * 1000 + n). It must be above every car of FM4 and its DLC and free in the game and in the installed packs.");
            this.pages[0].Controls.Add(this.carId);
            Button nextFree = new Button();
            nextFree.Text = "Next free";
            nextFree.SetBounds(504, 96, 82, 26);
            nextFree.Click += delegate { this.SuggestCarId(); };
            new ToolTip().SetToolTip(nextFree, "Back to the first free car Id.");
            this.pages[0].Controls.Add(nextFree);
            this.problem =this.AddLabel(0, "", 12, 130, 576, 40);

            // Where the copy goes: the game folder, or a DLC pack in Xenia's content folder (the game stays as it is).
            this.AddLabel(0, "Put the copy:", 12, 184, 94, 20);
            this.toGame = new RadioButton();
            this.toGame.Text = "in the game folder (installed)";
            this.toGame.SetBounds(106, 182, 300, 24);
            this.toGame.Checked = true;
            this.toPack = new RadioButton();
            this.toPack.Text = "in a DLC pack folder (Xenia)";
            this.toPack.SetBounds(106, 208, 220, 24);
            this.toFile = new RadioButton();
            this.toFile.Text = "as a DLC package file (STFS)";
            this.toFile.SetBounds(332, 208, 250, 24);
            new ToolTip().SetToolTip(this.toFile, "The pack as one Xbox 360 package file (the same STFS file the port window builds), saved in a folder you choose when the copy is created: for a modded console, or to put in Xenia's content folder. The game folder is not changed.");
            this.AddLabel(0, "Pack name:", 124, 240, 80, 20);
            this.packName = new TextBox();
            this.packName.SetBounds(208, 237, 260, 24);
            this.AddLabel(0, "Offer:", 124, 270, 80, 20);
            this.packOffer = new NumericUpDown();
            this.packOffer.Minimum = 1;
            this.packOffer.Maximum = 99999;
            this.packOffer.SetBounds(208, 267, 80, 24);
            new ToolTip().SetToolTip(this.toPack, "The pack is written to Xenia's content folder (Xenia's folder is set in Settings; if it is not, it is asked when this is chosen).");
            this.UseXeniaContent(xeniaContent);
            this.packName.Enabled = false;
            this.packOffer.Enabled = false;
            EventHandler packChanged = delegate
            {
                if (this.toPack.Checked && this.xeniaContent == null && !this.AskXeniaFolder())
                {
                    this.toGame.Checked = true;
                    return;
                }
                bool inPack = this.toPack.Checked || this.toFile.Checked;
                this.packName.Enabled = inPack;
                this.packOffer.Enabled = inPack;
                if (inPack && this.packName.Text.Length == 0)
                {
                    this.packName.Text = this.displayName.Text.Length > 0 ? this.displayName.Text : this.newName.Text.Trim();
                }
                this.Recheck();
            };
            this.pages[0].Controls.Add(this.toGame);
            this.pages[0].Controls.Add(this.toPack);
            this.pages[0].Controls.Add(this.toFile);
            this.pages[0].Controls.Add(this.packName);
            this.pages[0].Controls.Add(this.packOffer);
            this.toPack.CheckedChanged += packChanged;
            this.toFile.CheckedChanged += packChanged;

            // 2 Names
            this.AddLabel(1, "The names the game shows, the same in every language (change a language later in the string tables).", 12, 12, 576, 36);
            this.AddLabel(1, "Display name:", 12, 64, 90, 20);
            this.displayName = new TextBox();
            this.displayName.SetBounds(106, 61, 300, 24);
            this.pages[1].Controls.Add(this.displayName);
            this.AddLabel(1, "Model name:", 12, 98, 90, 20);
            this.modelShort = new TextBox();
            this.modelShort.SetBounds(106, 95, 300, 24);
            this.pages[1].Controls.Add(this.modelShort);
            this.AddLabel(1, "Year:", 12, 132, 90, 20);
            this.year = new NumericUpDown();
            this.year.Minimum = 1900;
            this.year.Maximum = 2100;
            this.year.Value = 2000;
            this.year.SetBounds(106, 129, 80, 24);
            this.pages[1].Controls.Add(this.year);
            this.AddLabel(1, "Make:", 12, 166, 90, 20);
            this.make = new ComboBox();
            this.make.DropDownStyle = ComboBoxStyle.DropDownList;
            this.make.SetBounds(106, 163, 300, 24);
            this.make.MaxDropDownItems = 20;
            this.make.Items.AddRange(makes.ToArray());
            this.pages[1].Controls.Add(this.make);

            // 3 Data
            this.AddLabel(2, "The values that usually change for a copy; everything else is copied from the source. Paint colours and upgrades are copied with the car; the drivetrain and wheel stay shared with the source, the engine too unless the next page gives the copy its own.", 12, 12, 576, 48);
            this.grid = new DataGridView();
            this.grid.SetBounds(12, 66, 576, 262);
            this.grid.AllowUserToAddRows = false;
            this.grid.AllowUserToDeleteRows = false;
            this.grid.RowHeadersVisible = false;
            this.grid.Columns.Add("column", "Column");
            this.grid.Columns.Add("value", "Value");
            this.grid.Columns.Add("source", "Source car");
            this.grid.Columns.Add("note", "What it is");
            this.grid.Columns[0].ReadOnly = true;
            this.grid.Columns[2].ReadOnly = true;
            this.grid.Columns[3].ReadOnly = true;
            this.grid.Columns[0].Width = 175;
            this.grid.Columns[1].Width = 90;
            this.grid.Columns[2].Width = 90;
            this.grid.Columns[3].Width = 200;
            this.pages[2].Controls.Add(this.grid);
            this.showAll = new CheckBox();
            this.showAll.Text = "Show every value of the car (stats, aero, tyres, calipers...)";
            this.showAll.SetBounds(12, 334, 576, 22);
            this.showAll.CheckedChanged += delegate { this.ShowDataRows(); };
            this.pages[2].Controls.Add(this.showAll);

            // 4 Engine
            this.AddLabel(3, "The engine of the copy. Shared: the copy uses the source's engine, so any later change to that engine changes both. Its own: a copy of the source's engine, every upgrade level and torque curve included, that you design here: name, sound, turbo boost, idle and redline rpm, torque and the shape of the torque curve.", 12, 8, 576, 64);
            this.engineShared = new RadioButton();
            this.engineShared.Text = "Shared with the source car (the engine is not copied)";
            this.engineShared.Checked = true;
            this.engineShared.SetBounds(12, 82, 560, 22);
            this.engineOwn = new RadioButton();
            this.engineOwn.Text = "An engine of its own";
            this.engineOwn.SetBounds(12, 108, 300, 22);
            this.engineEdit = new Button();
            this.engineEdit.Text = "Design the engine...";
            this.engineEdit.SetBounds(316, 105, 160, 28);
            this.engineEdit.Enabled = false;
            this.engineEdit.Click += delegate { this.EditEngine(); };
            this.engineSummary = new Label();
            this.engineSummary.SetBounds(32, 140, 556, 80);
            this.pages[3].Controls.Add(this.engineShared);
            this.pages[3].Controls.Add(this.engineOwn);
            this.pages[3].Controls.Add(this.engineEdit);
            this.pages[3].Controls.Add(this.engineSummary);
            this.engineOwn.CheckedChanged += delegate
            {
                this.engineEdit.Enabled = this.engineOwn.Checked;
                if (this.engineOwn.Checked && this.engineSpec == null)
                {
                    this.EditEngine();
                }
            };

            // The gearbox sits on the same page: number of gears and a ratio for each.
            this.gearboxOwn = new CheckBox();
            this.gearboxOwn.Text = "A gearbox of its own (number of gears, ratios, final drive; the drivetrain is copied, the source keeps its own)";
            this.gearboxOwn.SetBounds(12, 232, 576, 22);
            this.gearboxEdit = new Button();
            this.gearboxEdit.Text = "Design the gearbox...";
            this.gearboxEdit.SetBounds(32, 258, 160, 28);
            this.gearboxEdit.Enabled = false;
            this.gearboxEdit.Click += delegate { this.EditGearbox(); };
            this.gearboxSummary = new Label();
            this.gearboxSummary.SetBounds(32, 292, 556, 50);
            this.pages[3].Controls.Add(this.gearboxOwn);
            this.pages[3].Controls.Add(this.gearboxEdit);
            this.pages[3].Controls.Add(this.gearboxSummary);
            this.gearboxOwn.CheckedChanged += delegate
            {
                this.gearboxEdit.Enabled = this.gearboxOwn.Checked;
                if (this.gearboxOwn.Checked && this.gearboxSpec == null)
                {
                    this.EditGearbox();
                }
            };

            // 5 Summary
            this.AddLabel(4, "What will be done (nothing is written until Create). Every file is backed up first (bak folder).", 12, 12, 576, 20);
            this.summary = new TextBox();
            this.summary.Multiline = true;
            this.summary.ReadOnly = true;
            this.summary.ScrollBars = ScrollBars.Vertical;
            this.summary.SetBounds(12, 40, 576, 316);
            this.pages[4].Controls.Add(this.summary);

            this.back = new Button();
            this.back.Text = "< Back";
            this.back.SetBounds(336, 382, 80, 28);
            this.back.Click += delegate { this.Go(this.page - 1); };
            this.next = new Button();
            this.next.Text = "Next >";
            this.next.SetBounds(424, 382, 80, 28);
            this.next.Click += delegate { this.NextClicked(); };
            this.cancel = new Button();
            this.cancel.Text = "Cancel";
            this.cancel.DialogResult = DialogResult.Cancel;
            this.cancel.SetBounds(512, 382, 80, 28);
            this.Controls.Add(this.back);
            this.Controls.Add(this.next);
            this.Controls.Add(this.cancel);
            this.CancelButton = this.cancel;

            // FM2 / FM3 (ST-02): engine and gearbox of its own (FM3 = FM4's tables; FM2 CarEngine.CloneFm2 / CarGearbox.ApplyFm2);
            // the DLC pack destinations: FM4 and FM3 (DlcPackage.Fm3), FM2 not yet.
            ForzaGame game = GameInfo.Detect(target);
            if (game == ForzaGame.Motorsport2 || game == ForzaGame.Motorsport3)
            {
                string tag = GameInfo.Tag(game);
                this.Text = "Duplicate Car (" + tag + ")";
            }

            ForzaTheme.Apply(this);
            if (this.car.Items.Count > 0)
            {
                this.car.SelectedIndex = 0;
            }
            this.Go(0);
        }

        private string Lookup(string table, string column, string value)
        {
            return (this.names == null || value == null) ? null : this.names.Lookup(table, column, value);
        }

        private Label AddLabel(int pageIndex, string text, int x, int y, int width, int height)
        {
            Label label = new Label();
            label.Text = text;
            label.SetBounds(x, y, width, height);
            this.pages[pageIndex].Controls.Add(label);
            return label;
        }

        private CarItem SelectedCar
        {
            get { return this.car.SelectedItem as CarItem; }
        }

        // The columns a copy usually needs changed; the rest of DataFields shows with "Show every value", and any row the
        // user already changed stays visible.
        private static readonly string[] BasicFields =
        {
            "BaseCost", "CurbWeight", "WeightDistribution", "DriveTypeID", "EnginePlacementID", "CarTypeID", "CountryID", "RegionID",
            "IsDrivable", "IsArcade", "IsUnicorn", "HasLivery", "StockWheelID",
        };

        private void ShowDataRows()
        {
            this.grid.CurrentCell = null;
            foreach (DataGridViewRow row in this.grid.Rows)
            {
                string column = Convert.ToString(row.Cells[0].Value, CultureInfo.InvariantCulture);
                bool changed = !string.Equals(Convert.ToString(row.Cells[1].Value, CultureInfo.InvariantCulture), Convert.ToString(row.Cells[2].Value, CultureInfo.InvariantCulture), StringComparison.Ordinal);
                row.Visible = this.showAll.Checked || changed || Array.IndexOf(BasicFields, column) >= 0;
            }
        }

        // The cars of the installed packages (Xenia's content folder of the game) whose files are not in the game folder: disc 2 cars
        // (their rows are the game's) get the package; DLC cars (rows in the package's merge) are added, marked with the package's name.
        private void AddPackageCars(List<CarItem> cars)
        {
            if (string.IsNullOrEmpty(this.xeniaContent) || !Directory.Exists(this.xeniaContent))
            {
                return;
            }
            ForzaGame game = GameInfo.Detect(this.target);
            uint title = InstalledDlc.TitleId(game);
            string carsFolder = Path.Combine(Path.Combine(this.target, "Media"), "cars");
            HashSet<string> inGame = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(carsFolder))
            {
                foreach (string zip in Directory.GetFiles(carsFolder, "*.zip"))
                {
                    inGame.Add(Path.GetFileNameWithoutExtension(zip));
                }
            }
            Dictionary<string, CarItem> byName = new Dictionary<string, CarItem>(StringComparer.OrdinalIgnoreCase);
            foreach (CarItem item in cars)
            {
                byName[item.MediaName] = item;
            }
            List<CarItem> added = new List<CarItem>();
            foreach (string entry in Directory.GetFileSystemEntries(this.xeniaContent))
            {
                try
                {
                    bool folder = Directory.Exists(entry);
                    if ((!folder && (!StfsReader.IsPackage(entry) || ModPack.IsModPack(entry))))
                    {
                        continue;   // a mod pack replaces a car, it is not where the car comes from
                    }
                    DlcSource source = folder ? DlcSource.Open(entry) : DlcSource.OpenPackage(entry, true);
                    if (source == null || source.Problem != null || (!folder && source.TitleId != title))
                    {
                        continue;
                    }
                    string name = string.IsNullOrEmpty(source.Title) ? Path.GetFileName(entry) : source.Title;
                    foreach (string car in source.CarFolders())
                    {
                        if (inGame.Contains(car))
                        {
                            continue;
                        }
                        CarItem item;
                        if (byName.TryGetValue(car, out item))
                        {
                            if (item.PackageFile == null)
                            {
                                item.PackageFile = entry;
                                item.Text += "   [" + name + "]";
                            }
                            continue;
                        }
                        item = new CarItem();
                        item.MediaName = car;
                        item.PackageFile = entry;
                        item.Text = car + "   [" + name + "]";
                        byName[car] = item;
                        added.Add(item);
                        this.taken.Add(car);
                    }
                }
                catch (Exception)
                {
                    // a package that can't be read gives no source car
                }
            }
            cars.AddRange(added);
            cars.Sort(delegate(CarItem a, CarItem b) { return string.Compare(a.MediaName, b.MediaName, StringComparison.OrdinalIgnoreCase); });
        }

        private readonly Dictionary<string, byte[]> packageDatabases = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, StringTable> packageNames = new Dictionary<string, StringTable>(StringComparer.OrdinalIgnoreCase);

        // A name of the source car: its package's strings first, then the game's.
        private string SourceName(CarItem item, string column)
        {
            StringTable own;
            string text = (item.PackageFile != null && this.packageNames.TryGetValue(item.PackageFile, out own)) ? own.Lookup("Data_Car", column, this.carRow[column] as string) : null;
            return text ?? this.Lookup("Data_Car", column, this.carRow[column] as string) ?? "";
        }

        // The database the source car's rows are in: the game's, with its package's merge for a package car.
        private byte[] SourceDatabase(CarItem item)
        {
            if (item == null || item.PackageFile == null)
            {
                return File.ReadAllBytes(CarMake.DatabasePath(this.target));
            }
            byte[] bytes;
            if (!this.packageDatabases.TryGetValue(item.PackageFile, out bytes))
            {
                DlcSource source = CarDuplicate.OpenSourcePackage(item.PackageFile, GameInfo.Detect(this.target));
                bytes = CarDuplicate.PackageReference(this.target, source);
                this.packageDatabases[item.PackageFile] = bytes;
                // A DLC car's names are only in its package's string tables (the game's lack its keys).
                StringTable own = new StringTable();
                try
                {
                    foreach (KeyValuePair<string, byte[]> file in source.StringFiles("EN"))
                    {
                        own.Overlay(file.Key, file.Value);
                    }
                }
                catch (Exception)
                {
                }
                this.packageNames[item.PackageFile] = own;
            }
            return bytes;
        }

        // The source car's current values into the name, year, make and data pages.
        private void CarChanged()
        {
            CarItem item = this.SelectedCar;
            if (item == null)
            {
                return;
            }
            using (SqliteDatabase db = SqliteDatabase.FromBytes(this.SourceDatabase(item)))
            {
                SqliteDatabase.Result row = db.Query("SELECT * FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", item.MediaName);
                this.carRow.Clear();
                for (int i = 0; i < row.Columns.Length; i++)
                {
                    this.carRow[row.Columns[i]] = row.Rows[0][i];
                }
            }
            this.newName.Text = this.FreeName(item.MediaName);
            this.displayName.Text = this.SourceName(item, "DisplayName");
            this.modelShort.Text = this.SourceName(item, "ModelShort");
            this.year.Value = Math.Max(this.year.Minimum, Math.Min(this.year.Maximum, Convert.ToDecimal(this.carRow["Year"], CultureInfo.InvariantCulture)));
            long makeId = Convert.ToInt64(this.carRow["MakeID"], CultureInfo.InvariantCulture);
            for (int i = 0; i < this.make.Items.Count; i++)
            {
                if (((MakeItem) this.make.Items[i]).Id == makeId)
                {
                    this.make.SelectedIndex = i;
                }
            }
            this.grid.Rows.Clear();
            this.engineSpec = null;
            this.engineSummary.Text = "";
            this.engineShared.Checked = true;
            this.gearboxSpec = null;
            this.gearboxSummary.Text = "";
            this.gearboxOwn.Checked = false;
            foreach (string[] field in DataFields)
            {
                object value;
                if (this.carRow.TryGetValue(field[0], out value) && value != null && !(value is DBNull))
                {
                    this.grid.Rows.Add(field[0], Convert.ToString(value, CultureInfo.InvariantCulture), Convert.ToString(value, CultureInfo.InvariantCulture), field[1]);
                }
            }
            this.ShowDataRows();
            this.Recheck();
        }


        // The engine dialog on the source car's stock engine; cancelled with no engine yet: back to shared.
        private void EditEngine()
        {
            if (this.SelectedCar == null)
            {
                return;
            }
            try
            {
                using (EngineDialog dialog = new EngineDialog(this.target, this.names, Convert.ToInt64(this.carRow["Id"], CultureInfo.InvariantCulture), this.engineSpec, this.SourceDatabase(this.SelectedCar)))
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        this.engineSpec = dialog.Result;
                        this.engineSummary.Text = "Engine of its own: " + EngineDialog.Describe(this.engineSpec);
                    }
                    else if (this.engineSpec == null)
                    {
                        this.engineShared.Checked = true;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                this.engineShared.Checked = true;
            }
        }

        // The gearbox dialog on the source car's stock gearbox; cancelled with no gearbox yet: back to shared.
        private void EditGearbox()
        {
            if (this.SelectedCar == null)
            {
                return;
            }
            try
            {
                using (GearboxDialog dialog = new GearboxDialog(this.target, Convert.ToInt64(this.carRow["Id"], CultureInfo.InvariantCulture), this.gearboxSpec, this.SourceDatabase(this.SelectedCar)))
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        this.gearboxSpec = dialog.Result;
                        this.gearboxSummary.Text = "Gearbox of its own: " + CarGearbox.Describe(this.gearboxSpec);
                    }
                    else if (this.gearboxSpec == null)
                    {
                        this.gearboxOwn.Checked = false;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                this.gearboxOwn.Checked = false;
            }
        }

        private string FreeName(string source)
        {
            for (int i = 2; i < 1000; i++)
            {
                string candidate = source + "_" + i.ToString(CultureInfo.InvariantCulture);
                if (candidate.Length <= 40 && !this.taken.Exists(delegate(string t) { return string.Equals(t, candidate, StringComparison.OrdinalIgnoreCase); }))
                {
                    return candidate;
                }
            }
            return source;
        }

        private bool Recheck()
        {
            string text = this.newName.Text.Trim();
            string message = null;
            if (this.SelectedCar == null)
            {
                message = "Choose the car to copy.";
            }
            else if (!CarDuplicate.IsValidName(text))
            {
                message = "The MediaName must be 3-40 letters, digits or underscores.";
            }
            else if (this.taken.Exists(delegate(string t) { return string.Equals(t, text, StringComparison.OrdinalIgnoreCase); }))
            {
                message = text + " is already a car of this game.";
            }
            else if ((this.toPack.Checked || this.toFile.Checked) && this.PackHasCar(text))
            {
                message = text + " is already a car of an installed DLC pack.";
            }
            else if ((this.toPack.Checked || this.toFile.Checked) && this.packName.Text.Trim().Length == 0)
            {
                message = "Give the pack a name.";
            }
            else
            {
                message = this.CarIdProblem();
            }
            this.problem.ForeColor = ForzaTheme.Yellow;
            this.problem.Text = message ?? "";
            return message == null;
        }

        // The Id written in the box (its text: Value changes only when the box loses focus), or -1.
        private long ChosenCarId()
        {
            long id;
            return long.TryParse(this.carId.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id) ? id : -1;
        }

        // Why the Id in the box cannot be used, or null (the full check, car columns included, runs again on Create).
        private string CarIdProblem()
        {
            long id = this.ChosenCarId();
            if (id <= 0)
            {
                return "Write the car Id (a whole number).";
            }
            string name;
            if (this.gameCars.TryGetValue(id, out name))
            {
                return string.Format(CultureInfo.InvariantCulture, "Car Id {0} is already used in the game database by {1}.", id, name);
            }
            return DbIds.ChosenCarIdProblem(null, id, this.packCars, GameInfo.Detect(this.target));
        }

        // A pack in Xenia's content folder already holds a car of this name.
        private bool PackHasCar(string mediaName)
        {
            if (this.xeniaContent == null)
            {
                return false;
            }
            foreach (string merge in DbIds.PackDatabases(this.xeniaContent))
            {
                try
                {
                    using (SqliteDatabase db = SqliteDatabase.FromBytes(File.ReadAllBytes(merge)))
                    {
                        if (DbIds.HasTable(db, "Data_Car") && db.Scalar("SELECT Id FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", mediaName) != null)
                        {
                            return true;
                        }
                    }
                }
                catch (Exception)
                {
                }
            }
            return false;
        }

        private void Go(int index)
        {
            this.page = Math.Max(0, Math.Min(this.pages.Length - 1, index));
            for (int i = 0; i < this.pages.Length; i++)
            {
                this.pages[i].Visible = i == this.page;
            }
            this.back.Enabled = this.page > 0;
            this.next.Text = (this.page == this.pages.Length - 1) ? "Create" : "Next >";
            if (this.page == this.pages.Length - 1)
            {
                this.ShowPlan();
            }
        }

        private void NextClicked()
        {
            if (this.page == 0 && !this.Recheck())
            {
                return;
            }
            if (this.page < this.pages.Length - 1)
            {
                this.Go(this.page + 1);
                return;
            }
            this.Create();
        }

        private CarDuplicate Build()
        {
            CarDuplicate duplicate = new CarDuplicate();
            duplicate.TargetRoot = this.target;
            duplicate.XeniaContent = this.xeniaContent;
            if (this.toPack.Checked || this.toFile.Checked)
            {
                duplicate.PackContent = this.toPack.Checked ? this.xeniaContent : null;
                if (this.toFile.Checked)
                {
                    duplicate.ConsoleOutput = this.packageOutput ?? "(the folder you choose when the copy is created)";
                    duplicate.Thumbnail = CarPortForm.PackThumbnail(this.Icon);
                }
                duplicate.PackOffer = (int) this.packOffer.Value;
                duplicate.PackName = this.packName.Text.Trim();
                duplicate.ReplacePack = this.replacePack;
            }
            duplicate.SourceMediaName = this.SelectedCar.MediaName;
            duplicate.SourcePackage = this.SelectedCar.PackageFile;   // a disc 2 / DLC car: taken out of its package (CarDuplicate.Package.cs)
            if (this.engineOwn.Checked)
            {
                if (this.engineSpec == null)
                {
                    throw new InvalidOperationException("Design the engine first (page 4), or choose the shared engine.");
                }
                duplicate.Engine = this.engineSpec;
            }
            if (this.gearboxOwn.Checked)
            {
                if (this.gearboxSpec == null)
                {
                    throw new InvalidOperationException("Design the gearbox first (page 4), or untick the gearbox of its own.");
                }
                duplicate.Gearbox = this.gearboxSpec;
            }
            duplicate.NewMediaName = this.newName.Text.Trim();
            duplicate.ChosenId = this.ChosenCarId();
            duplicate.DisplayName = this.displayName.Text.Trim();
            duplicate.ModelShort = this.modelShort.Text.Trim();
            duplicate.Year = (long) this.year.Value;
            MakeItem makeItem = this.make.SelectedItem as MakeItem;
            if (makeItem != null && makeItem.Id != Convert.ToInt64(this.carRow["MakeID"], CultureInfo.InvariantCulture))
            {
                duplicate.MakeId = makeItem.Id;
            }
            foreach (DataGridViewRow row in this.grid.Rows)
            {
                string column = (string) row.Cells[0].Value;
                string value = Convert.ToString(row.Cells[1].Value, CultureInfo.InvariantCulture);
                if (value == Convert.ToString(row.Cells[2].Value, CultureInfo.InvariantCulture))
                {
                    continue;
                }
                object old = this.carRow[column];
                if (old is long)
                {
                    long whole;
                    if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out whole))
                    {
                        throw new InvalidOperationException(column + ": " + value + " is not a whole number.");
                    }
                    duplicate.Data[column] = whole;
                }
                else
                {
                    double real;
                    if (!double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out real))
                    {
                        throw new InvalidOperationException(column + ": " + value + " is not a number.");
                    }
                    duplicate.Data[column] = real;
                }
            }
            return duplicate;
        }

        private void ShowPlan()
        {
            try
            {
                CarDuplicate duplicate = this.Build();
                this.summary.Text = string.Join("\r\n", duplicate.Plan().ToArray());
                this.next.Enabled = true;
            }
            catch (Exception ex)
            {
                this.summary.Text = "Cannot duplicate: " + ex.Message;
                this.next.Enabled = false;
            }
        }

        private void Create()
        {
            string question;
            this.replacePack = false;
            if (this.toFile.Checked)
            {
                // The package file goes straight into the folder the user picks (as the port window's Xbox 360 build does).
                // The folder is remembered per game and must not be another game's content folder: the copy is a pack of the
                // OPEN game (FM2/FM3/FM4), and before 2026-11-15 one FM4 key served all three (an FM3 pack landed in 4D530910).
                ForzaGame packGame = GameInfo.Detect(this.target);
                string title = InstalledDlc.TitleId(packGame).ToString("X8", CultureInfo.InvariantCulture);
                string key = (packGame == ForzaGame.Motorsport4) ? "CarPort.XboxOut" : "CarPort.XboxOut." + GameInfo.Tag(packGame);
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "Folder for the " + GameInfo.Name(packGame) + " DLC package file (saved directly in it; Xenia: ...\\" + title + "\\00000002)";
                    string start = DlcPackage.PackageOutputStart(key, title);
                    if (start != null)
                    {
                        dialog.SelectedPath = start;
                    }
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }
                    string foreign = DlcPackage.ForeignTitleFolder(dialog.SelectedPath, title);
                    if (foreign != null)
                    {
                        MessageBox.Show(this, dialog.SelectedPath + " is the content folder of another game (title " + foreign + ", " + (GameInfo.Name(GameInfo.FromTitleId(uint.Parse(foreign, NumberStyles.HexNumber, CultureInfo.InvariantCulture))) ?? "unknown game") + ").\n\nThis copy is a " + GameInfo.Name(packGame) + " pack (title " + title + "): choose its own folder.", "Duplicate Car", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                        return;
                    }
                    this.packageOutput = dialog.SelectedPath;
                }
                AppSettings.Set(key, this.packageOutput);
                question = "Build the DLC package file \"" + this.packName.Text.Trim() + "\" (offer " + (int) this.packOffer.Value + ") with the copy " + this.newName.Text.Trim() + " in " + this.packageOutput + " now? The game folder is not changed.";
            }
            else if (this.toPack.Checked)
            {
                int offer = (int) this.packOffer.Value;
                if (Directory.Exists((GameInfo.Detect(this.target) == ForzaGame.Motorsport3) ? DlcPackage.PackagePathFm3(this.xeniaContent, offer) : (GameInfo.Detect(this.target) == ForzaGame.Motorsport2) ? DlcPackage.PackagePathFm2(this.xeniaContent, offer) : DlcPackage.PackagePath(this.xeniaContent, offer)))
                {
                    if (MessageBox.Show(this, "A package with offer " + offer + " is already in Xenia's content folder (it may be another pack). Replace it with this one?", this.Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                    {
                        return;
                    }
                    this.replacePack = true;
                }
                question = "Build the DLC pack \"" + this.packName.Text.Trim() + "\" (offer " + offer + ") with the copy " + this.newName.Text.Trim() + " in Xenia's content folder now? The game folder is not changed.";
            }
            else
            {
                question = "Write the copy " + this.newName.Text.Trim() + " into the game folder now? The database, the string tables, Textures.zip and zipmanifest.xml are backed up first.";
            }
            if (MessageBox.Show(this, question, this.Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            {
                return;
            }
            try
            {
                CarDuplicate duplicate = this.Build();
                duplicate.Run();
                this.CreatedName = duplicate.NewMediaName;
                this.summary.Text = duplicate.Report.ToString().Replace("\n", "\r\n");
                this.next.Enabled = false;
                this.back.Enabled = false;
                this.cancel.Text = "Close";
                MessageBox.Show(this, duplicate.NewMediaName + " created (car Id " + duplicate.NewId + ")." + ((duplicate.PackageFile != null) ? "\n\nDLC package file: " + duplicate.PackageFile + "\nOn the console it goes in Hdd1:\\Content\\0000000000000000\\" + InstalledDlc.TitleId(GameInfo.Detect(this.target)).ToString("X8", CultureInfo.InvariantCulture) + "\\00000002\\ (FTP)." : ""),this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                if (duplicate.PackageFile != null)
                {
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + duplicate.PackageFile + "\"");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
