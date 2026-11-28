namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Windows.Forms;

    // DLC mode of the FM2 port window (docs/PORT_PARITY.md C, user 2026-11-27: "port first, then into the pack"):
    //   Add ->            the selected car, after its Trial Run, goes into the pack with every choice of the window (template,
    //                     variants, materials, sound, swaps, make), kept as an Fm2PortRecord;
    //   Trial Pack        stage = the FM2 files the port reads and writes, copied under %TEMP%\ForzaStudio\PackFm2\pack_<time>
    //                     (CarDuplicate.Stage per template + Textures.zip and the sound zips); the pack's new makes, then every
    //                     car through CarPortFm2Chain into the stage, car ids from First car id on. The FM2 folder is only read;
    //   Build DLC Package DlcPackage.BuildFolderFm2 (the stage against the FM2 folder: merge, strings, the files the port added,
    //                     its sound too), the merge's integrity check, the layer zipped (FM2 reads only zips), names pack;
    //   Remove Pack       DLC<offer> out of Xenia's FM2 content folder.
    // The pack is remembered between sessions (ForzaStudio.ini, PortFm2.Pack*); new makes of the pack are not (made again).
    internal sealed partial class CarPortFm2Form
    {
        private sealed class PackItem
        {
            public SourceCar Car;
            public Fm2PortRecord Choice;    // template, variants left out, materials, sound, swaps, make
            public long CarId;              // given by the last Trial Pack; 0: none yet
        }

        private const char PackLineSeparator = '|';

        private readonly List<PackItem> pack = new List<PackItem>();
        private readonly List<CarMakeSpec> pendingMakes = new List<CarMakeSpec>();     // DLC mode's new makes ("+"), for the pack only
        private Button intoDlc;
        private ListView packList;
        private TextBox packName;
        private TextBox packOffer;
        private TextBox packFirstCar;
        private TextBox xeniaBox;       // not shown: holds the Xenia folder of Tools > Settings for XeniaContent()
        private Label xeniaInfo;        // the Xenia folder shown in the pack panel (set in Settings only)
        private Button removeFromPack;
        private Button newPack;
        private Button packTrial;
        private Button buildPack;
        private Button removePack;
        private Button addToPackage;
        private Button buildXbox;
        private Label packInfo;
        private string packTrialFolder;     // the stage of the last Trial Pack that went through; null: Trial Pack first

        // Add / remove between the two lists, as in the FM4 / FM3 windows (2026-11-28; was an "Into DLC ->" button under the
        // selected car and a "Remove" over the pack list). Add needs the car's trial run: an FM2 pack car carries its choices.
        private Panel BuildTransfer()
        {
            Panel transfer = new Panel();
            transfer.Dock = DockStyle.Left;
            transfer.Width = 112;
            this.intoDlc = new Button();
            this.intoDlc.Text = "Add  \u2192";
            this.intoDlc.Size = new Size(96, 30);
            new ToolTip().SetToolTip(this.intoDlc, "Put the car into the pack with the choices of its trial run (template, variants, materials, sound, swaps, make). Trial Run the car first.");
            this.intoDlc.Click += delegate { this.IntoDlc(); };
            this.removeFromPack = new Button();
            this.removeFromPack.Text = "\u2190  Remove";
            this.removeFromPack.Size = new Size(96, 30);
            new ToolTip().SetToolTip(this.removeFromPack, "Take the selected car out of the pack");
            this.removeFromPack.Click += delegate { this.RemoveFromPack(); };
            transfer.Controls.Add(this.intoDlc);
            transfer.Controls.Add(this.removeFromPack);
            transfer.Layout += delegate
            {
                int middle = transfer.ClientSize.Height / 2;
                this.intoDlc.Location = new Point(8, middle - 36);
                this.removeFromPack.Location = new Point(8, middle + 6);
            };
            this.cars.DoubleClick += delegate
            {
                if (this.intoDlc.Enabled)
                {
                    this.IntoDlc();
                }
            };
            return transfer;
        }

        // The pack (right): name, offer, first car id, the cars, then the Xenia folder and the actions from the bottom, laid out
        // as in the FM4 / FM3 windows.
        private Panel BuildPackPanel()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;

            Panel fields = panel;
            Label nameLabel = new Label();
            nameLabel.Text = "Name:";
            nameLabel.Location = new Point(8, 39);
            nameLabel.AutoSize = true;
            this.packName = new TextBox();
            this.packName.Location = new Point(58, 37);
            this.packName.Width = 300;
            this.packName.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            this.packName.Text = AppSettings.Get("PortFm2.PackName", "ForzaStudio EVO pack");
            this.packName.TextChanged += delegate { AppSettings.Set("PortFm2.PackName", this.packName.Text); this.UpdateButtons(); };

            Label offerLabel = new Label();
            offerLabel.Text = "Offer id:";
            offerLabel.Location = new Point(8, 69);
            offerLabel.AutoSize = true;
            this.packOffer = new TextBox();
            this.packOffer.Location = new Point(58, 66);
            this.packOffer.Width = 70;
            this.packOffer.MaxLength = 5;
            this.packOffer.Text = AppSettings.Get("PortFm2.PackOffer", "");
            this.packOffer.TextChanged += delegate { AppSettings.Set("PortFm2.PackOffer", this.packOffer.Text.Trim()); this.UpdateButtons(); };
            new ToolTip().SetToolTip(this.packOffer, "The package's number: the folder DLC<offer> in Xenia's FM2 content folder (FM2's own packs use 1-7)");
            Button freeOffer = new Button();
            freeOffer.Text = "Free";
            freeOffer.Location = new Point(134, 65);
            freeOffer.Size = new Size(46, 24);
            new ToolTip().SetToolTip(freeOffer, "Put the first number no FM2 package in Xenia uses");
            freeOffer.Click += delegate { this.packOffer.Text = DlcPackage.FreeOfferFm2(this.XeniaContent()).ToString(CultureInfo.InvariantCulture); };

            Label firstLabel = new Label();
            firstLabel.Text = "First car id:";
            firstLabel.Location = new Point(190, 69);
            firstLabel.AutoSize = true;
            this.packFirstCar = new TextBox();
            this.packFirstCar.Location = new Point(262, 66);
            this.packFirstCar.Width = 50;
            this.packFirstCar.MaxLength = 4;
            this.packFirstCar.Text = AppSettings.Get("PortFm2.PackFirstCar", "");
            this.packFirstCar.TextChanged += delegate
            {
                AppSettings.Set("PortFm2.PackFirstCar", this.packFirstCar.Text.Trim());
                this.PackChanged(false);
            };
            new ToolTip().SetToolTip(this.packFirstCar, "The pack's first car id (" + DbIds.Fm2FirstFreeCarId + "-" + DbIds.Fm2LastFreeCarId
                + "): the cars take the next free ones from it, never one of the FM2 folder or of another installed pack");
            Button freeFirst = new Button();
            freeFirst.Text = "Free";
            freeFirst.Location = new Point(316, 65);
            freeFirst.Size = new Size(46, 24);
            new ToolTip().SetToolTip(freeFirst, "Put the first car id neither the FM2 folder nor an installed pack uses");
            freeFirst.Click += delegate { this.SuggestFirstCar(); };

            Label xeniaLabel = new Label();
            // Xenia is set in one place only, Tools > Settings > Folders (user 2026-11-08): here it is shown, with a way there,
            // as in the FM4 window (2026-11-28; was a box to type in with a "..." button).
            xeniaLabel.Text = "Xenia (DLC packs):";
            xeniaLabel.AutoSize = true;
            this.xeniaBox = new TextBox();      // not shown: holds the folder for XeniaContent()
            this.xeniaBox.Visible = false;
            this.xeniaBox.Text = AppSettings.Get(SettingsForm.XeniaKey, "");
            this.xeniaInfo = new Label();
            this.xeniaInfo.AutoEllipsis = true;
            this.xeniaInfo.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Button xeniaBrowse = new Button();
            xeniaBrowse.Text = "Settings...";
            xeniaBrowse.Size = new Size(80, 24);
            xeniaBrowse.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            new ToolTip().SetToolTip(xeniaBrowse, "Tools > Settings > Folders: the game folders and Xenia (the pack goes into Xenia's FM2 content folder)");
            xeniaBrowse.Click += delegate
            {
                if (PortWindowParts.OpenFolderSettings(this))
                {
                    this.xeniaBox.Text = AppSettings.Get(SettingsForm.XeniaKey, "");
                    PortWindowParts.FillSourceGames(this.sourceBox, ForzaGame.Motorsport2);
                    this.UpdateXeniaInfo();
                    this.UpdateSourceInfo();
                    this.UpdatePackInfo();
                    this.UpdateButtons();
                }
            };

            this.newPack = new Button();
            this.newPack.Text = "New Pack";
            this.newPack.Size = new Size(100, 26);
            this.newPack.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            new ToolTip().SetToolTip(this.newPack, "Empty the pack (nothing in Xenia changes)");
            this.newPack.Click += delegate { this.NewPack(); };
            fields.Controls.Add(nameLabel);
            fields.Controls.Add(this.packName);
            fields.Controls.Add(offerLabel);
            fields.Controls.Add(this.packOffer);
            fields.Controls.Add(freeOffer);
            fields.Controls.Add(firstLabel);
            fields.Controls.Add(this.packFirstCar);
            fields.Controls.Add(freeFirst);
            fields.Controls.Add(xeniaLabel);
            fields.Controls.Add(this.xeniaBox);
            fields.Controls.Add(this.xeniaInfo);
            fields.Controls.Add(xeniaBrowse);
            fields.Controls.Add(this.newPack);
            this.UpdateXeniaInfo();

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
            this.packList.Columns.Add("Template", 110);
            this.packList.Columns.Add("Id", 44);
            this.packList.SelectedIndexChanged += delegate { this.UpdateButtons(); };
            this.packList.DoubleClick += delegate { this.SelectPackCar(); };
            CarPortFm3Form.StyleList(this.packList);

            // Rows from the bottom, as the FM4 window: hint, actions, Xenia folder, New Pack; the list takes the rest.
            Panel bottom = panel;
            this.packInfo = new Label();
            this.packInfo.Size = new Size(350, 48);
            this.packInfo.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.packInfo.ForeColor = ForzaTheme.TextDim;
            this.packTrial = new Button();
            this.packTrial.Text = "Trial Pack";
            this.packTrial.Location = new Point(8, 0);
            this.packTrial.Size = new Size(100, 26);
            this.packTrial.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            new ToolTip().SetToolTip(this.packTrial, "Port every pack car into a temporary copy of the FM2 files (the FM2 folder is only read): report and Review");
            this.packTrial.Click += delegate { this.RunPackTrial(); };
            this.buildPack = new Button();
            this.buildPack.Text = "Build DLC Package";
            this.buildPack.Location = new Point(114, 0);
            this.buildPack.Size = new Size(130, 26);
            this.buildPack.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            new ToolTip().SetToolTip(this.buildPack, "Write DLC<offer> into Xenia's FM2 content folder from the last Trial Pack");
            this.buildPack.Click += delegate { this.BuildPack(); };
            this.removePack = new Button();
            this.removePack.Text = "Remove Pack";
            this.removePack.Location = new Point(250, 0);
            this.removePack.Size = new Size(108, 26);
            this.removePack.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            new ToolTip().SetToolTip(this.removePack, "Take DLC<offer> out of Xenia's FM2 content folder");
            this.removePack.Click += delegate { this.RemovePackFromXenia(); };
            this.addToPackage = new Button();
            this.addToPackage.Text = "Add To Package...";
            this.addToPackage.Location = new Point(250, 0);
            this.addToPackage.Size = new Size(120, 26);
            this.addToPackage.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            new ToolTip().SetToolTip(this.addToPackage, "Put the cars of the last Trial Pack into an FM2 package that is already in Xenia (a DLC<n> folder or a package file): it keeps its offer, name and cars");
            this.addToPackage.Click += delegate { this.AddToPackage(); };
            this.buildXbox = new Button();
            this.buildXbox.Text = "Build for Xbox 360...";
            this.buildXbox.Location = new Point(8, 0);
            this.buildXbox.Size = new Size(238, 26);
            this.buildXbox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            new ToolTip().SetToolTip(this.buildXbox, "Write the pack as an Xbox 360 package file (STFS) into a folder you choose, from the last Trial Pack");
            this.buildXbox.Click += delegate { this.BuildXbox(); };
            bottom.Controls.Add(this.packInfo);
            bottom.Controls.Add(this.packTrial);
            bottom.Controls.Add(this.buildPack);
            bottom.Controls.Add(this.removePack);
            bottom.Controls.Add(this.addToPackage);
            bottom.Controls.Add(this.buildXbox);

            panel.Controls.Add(this.packList);
            panel.Controls.Add(CarPortForm.SectionHeader("DLC pack (Xenia / Xbox 360)"));
            panel.Layout += delegate
            {
                int h = panel.ClientSize.Height;
                int w = panel.ClientSize.Width;
                this.packInfo.Bounds = new Rectangle(8, h - 52, w - 16, 48);
                this.buildXbox.Top = this.addToPackage.Top = h - 84;
                this.packTrial.Top = this.buildPack.Top = this.removePack.Top = h - 116;
                this.xeniaInfo.Bounds = new Rectangle(8, h - 146, w - 104, 20);
                xeniaBrowse.Location = new Point(w - 88, h - 150);
                xeniaLabel.Location = new Point(8, h - 168);
                this.newPack.Location = new Point(8, h - 204);
                this.packName.Width = w - 66;
                this.packList.Bounds = new Rectangle(8, this.packList.Top, w - 16, Math.Max(40, h - 212 - this.packList.Top));
            };
            this.UpdatePackInfo();
            return panel;
        }

        // The Xenia line of the pack panel: the folder of Settings and whether it is there (as the FM4 window).
        private void UpdateXeniaInfo()
        {
            bool problem;
            this.xeniaInfo.Text = PortWindowParts.XeniaInfo(out problem);
            this.xeniaInfo.ForeColor = problem ? ForzaTheme.Yellow : ForzaTheme.TextDim;
        }

        private void UpdatePackButtons(bool trialDone)
        {
            if (this.packList == null)
            {
                return;
            }
            string content = this.XeniaContent();
            int offer = this.Offer();
            bool named = this.packName.Text.Trim().Length > 0 && offer > 0;
            this.intoDlc.Enabled = trialDone;
            this.removeFromPack.Enabled = !this.busy && this.packList.SelectedItems.Count == 1;
            this.newPack.Enabled = !this.busy && this.pack.Count > 0;
            this.packTrial.Enabled = !this.busy && this.pack.Count > 0 && this.loadedTarget != null;
            this.buildPack.Enabled = !this.busy && this.packTrialFolder != null && content != null && named;
            this.addToPackage.Enabled = !this.busy && this.packTrialFolder != null && content != null;
            this.buildXbox.Enabled = !this.busy && this.packTrialFolder != null && named;
            this.removePack.Enabled = !this.busy && content != null && offer > 0 && Directory.Exists(DlcPackage.PackagePathFm2(content, offer));
        }

        private void UpdatePackInfo()
        {
            if (this.packInfo == null)
            {
                return;
            }
            string content = this.XeniaContent();
            this.packInfo.Text = (this.pack.Count == 0) ? "Select a car on the left, Trial Run it, then Add (or double-click the car): it goes here with its choices."
                : (this.packTrialFolder == null) ? this.pack.Count + " car(s). Trial Pack ports them into a copy of the FM2 files (the FM2 folder is only read)."
                : (content == null) ? this.pack.Count + " car(s), trial done. Set Xenia's folder to build the package."
                : this.pack.Count + " car(s), trial done: Build DLC Package writes DLC" + this.packOffer.Text.Trim() + " into " + content + ".";
        }

        // Xenia's FM2 content folder (made when the pack is built); null without a Xenia folder.
        private string XeniaContent()
        {
            string xenia = (this.xeniaBox == null) ? "" : this.xeniaBox.Text.Trim();
            if (xenia.Length == 0 || !Directory.Exists(xenia))
            {
                return null;
            }
            try
            {
                return DlcPackage.XeniaContent(xenia, DlcPackage.TitleIdFm2);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private int Offer()
        {
            int offer;
            return (int.TryParse(this.packOffer.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out offer) && offer >= 1 && offer <= 65534) ? offer : 0;
        }

        private long FirstCar()
        {
            long id;
            return (long.TryParse(this.packFirstCar.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out id) && DbIds.Fm2FreeWindow(id)) ? id : 0;
        }

        // The installed packs' merges but this pack's own (its car ids are given again by the trial).
        private static List<string> OtherPacks(string content, int offer)
        {
            List<string> others = DbIds.PackDatabases(content);
            if (content != null && offer > 0)
            {
                string own = DlcPackage.PackagePathFm2(content, offer).TrimEnd('\\') + "\\";
                others.RemoveAll(delegate(string path) { return path.StartsWith(own, StringComparison.OrdinalIgnoreCase); });
            }
            return others;
        }

        // The first car id from 'from' on that neither the database nor an installed pack uses (0: none left up to 1000).
        private static long FreeCarId(SqliteDatabase db, long from, Dictionary<long, string> packCars)
        {
            for (long id = Math.Max(from, DbIds.Fm2FirstFreeCarId); id <= DbIds.Fm2LastFreeCarId; id++)
            {
                if (DbIds.ChosenCarIdProblem(db, id, packCars, ForzaGame.Motorsport2) == null)
                {
                    return id;
                }
            }
            return 0;
        }

        private void SuggestFirstCar()
        {
            if (this.loadedTarget == null)
            {
                return;
            }
            try
            {
                using (SqliteDatabase db = SqliteDatabase.FromBytes(File.ReadAllBytes(CarPortFm2Chain.FindDatabase(this.loadedTarget))))
                {
                    long id = FreeCarId(db, DbIds.Fm2FirstFreeCarId, DbIds.PackCarIds(OtherPacks(this.XeniaContent(), this.Offer())));
                    if (id == 0)
                    {
                        this.status.Text = "No free car id is left in " + DbIds.Fm2FirstFreeCarId + "-" + DbIds.Fm2LastFreeCarId + ".";
                        return;
                    }
                    this.packFirstCar.Text = id.ToString(CultureInfo.InvariantCulture);
                }
            }
            catch (Exception e)
            {
                this.status.Text = "The free car id was not found: " + e.Message;
            }
        }

        // A change of the pack's cars or of the First car id: the last trial no longer holds.
        private void PackChanged(bool cars)
        {
            this.packTrialFolder = null;
            if (cars)
            {
                this.SavePack();
            }
            this.RefreshPackList();
            this.UpdateButtons();
        }

        private void RefreshPackList()
        {
            if (this.packList == null)
            {
                return;
            }
            PackItem selected = (this.packList.SelectedItems.Count == 1) ? (PackItem) this.packList.SelectedItems[0].Tag : null;
            this.packList.BeginUpdate();
            this.packList.Items.Clear();
            foreach (PackItem item in this.pack)
            {
                ListViewItem row = new ListViewItem(item.Car.Name);
                row.Tag = item;
                row.SubItems.Add(item.Car.TargetMedia);
                row.SubItems.Add(item.Choice.Template);
                row.SubItems.Add((item.CarId > 0 && this.packTrialFolder != null) ? item.CarId.ToString(CultureInfo.InvariantCulture) : "");
                row.Selected = item == selected;
                this.packList.Items.Add(row);
            }
            this.packList.EndUpdate();
            this.UpdatePackInfo();
        }

        // Add -> (IntoDlc): the selected car with the choices of its trial run.
        private void IntoDlc()
        {
            SourceCar car = this.SelectedCar;
            string template = this.SelectedTemplate;
            Trial current = this.CurrentTrial;
            if (car == null || template == null || current == null)
            {
                return;
            }
            if (car.Fm2Id > 0)
            {
                MessageBox.Show(this, car.Name + " is in the FM2 folder already (" + car.TargetMedia + ", car " + car.Fm2Id.ToString(CultureInfo.InvariantCulture)
                    + "): a pack car needs a name the game does not have. Uninstall it in the Import window first.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
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
            Fm2PortRecord choice = new Fm2PortRecord();
            choice.SourceGame = GameInfo.Name(this.sourceGame) ?? "";
            choice.SourceMediaName = car.MediaName;
            choice.SourcePack = car.Dlc ?? "";
            choice.Template = template;
            choice.Skip.AddRange(current.Skip);
            foreach (KeyValuePair<string, string> pair in current.CarMaterials)
            {
                choice.CarMaterials[pair.Key] = pair.Value;
            }
            foreach (KeyValuePair<string, string> pair in current.WheelMaterials)
            {
                choice.WheelMaterials[pair.Key] = pair.Value;
            }
            choice.UseSoundOf = useSoundOf;
            choice.Swaps = ChosenSwaps(current);
            choice.MakeId = car.MakeId;
            PackItem item = this.pack.Find(delegate(PackItem p) { return string.Equals(p.Car.TargetMedia, car.TargetMedia, StringComparison.OrdinalIgnoreCase); });
            bool again = item != null;
            if (item == null)
            {
                item = new PackItem();
                item.Car = car;
                this.pack.Add(item);
            }
            item.Choice = choice;
            this.PackChanged(true);
            this.status.Text = car.Name + (again ? ": its choices in the pack replaced" : " is in the pack") + " (template " + template + (current.Skip.Count > 0 ? ", " + current.Skip.Count + " variant(s) left out" : "")
                + "). Trial Pack when the pack is complete.";
        }

        private void RemoveFromPack()
        {
            if (this.packList.SelectedItems.Count != 1)
            {
                return;
            }
            this.pack.Remove((PackItem) this.packList.SelectedItems[0].Tag);
            this.PackChanged(true);
        }

        private void NewPack()
        {
            if (this.pack.Count > 0 && MessageBox.Show(this, "Empty the pack (" + this.pack.Count + " car(s))? Nothing in Xenia changes.", this.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            this.pack.Clear();
            this.PackChanged(true);
        }

        // Double click on a pack car: the car chosen in the list on the left.
        private void SelectPackCar()
        {
            if (this.packList.SelectedItems.Count != 1)
            {
                return;
            }
            PackItem item = (PackItem) this.packList.SelectedItems[0].Tag;
            foreach (ListViewItem row in this.cars.Items)
            {
                if (row.Tag == item.Car)
                {
                    row.Selected = true;
                    row.EnsureVisible();
                    return;
                }
            }
            this.status.Text = item.Car.Name + " is not shown in the car list (search, or Also show cars FM2 already has).";
        }

        private void AddPendingMake(CarMakeSpec spec)
        {
            this.pendingMakes.Add(spec);
            this.fm2MakeList.Add(new object[] { spec.Id, spec.Name });
            SourceCar car = this.SelectedCar;
            if (car != null)
            {
                car.MakeId = spec.Id;
            }
            this.ShowMake();
            this.status.Text = "Make " + spec.Name + " (" + spec.Id + ") kept for the pack" + ((car != null) ? " and chosen for " + car.Name : "")
                + ": Trial Pack writes it into the pack, never into the FM2 folder. Trial Run again to see the database with it.";
        }

        private CarMakeSpec PendingMake(long id)
        {
            return (id > 0) ? this.pendingMakes.Find(delegate(CarMakeSpec spec) { return spec.Id == id; }) : null;
        }

        private void SavePack()
        {
            AppSettings.Set("PortFm2.PackSource", this.loadedSource ?? "");
            int old;
            int.TryParse(AppSettings.Get("PortFm2.PackCars", "0"), NumberStyles.None, CultureInfo.InvariantCulture, out old);
            for (int i = 0; i < Math.Max(old, this.pack.Count); i++)
            {
                string key = "PortFm2.PackCar" + i.ToString(CultureInfo.InvariantCulture);
                if (i >= this.pack.Count)
                {
                    AppSettings.Set(key, "");
                    continue;
                }
                List<string> lines = new List<string>();
                foreach (string line in this.pack[i].Choice.ToText().Replace("\r\n", "\n").Split('\n'))
                {
                    if (line.Length > 0 && !line.StartsWith("written=", StringComparison.Ordinal))
                    {
                        lines.Add(line);
                    }
                }
                AppSettings.Set(key, string.Join(PackLineSeparator.ToString(), lines.ToArray()));
            }
            AppSettings.Set("PortFm2.PackCars", this.pack.Count.ToString(CultureInfo.InvariantCulture));
        }

        // After Load Cars: the pack of this session (its cars found again in the new list), else the one saved for this source.
        private void RestorePack(List<SourceCar> list, string source)
        {
            StringBuilder notes = new StringBuilder();
            foreach (CarMakeSpec spec in this.pendingMakes)
            {
                this.fm2MakeList.Add(new object[] { spec.Id, spec.Name });
            }
            // every change of the pack is saved at once, so the saved pack is this session's too; another source folder shows
            // an empty pack and leaves the saved one as it is
            List<Fm2PortRecord> choices = new List<Fm2PortRecord>();
            if (string.Equals(AppSettings.Get("PortFm2.PackSource", ""), source, StringComparison.OrdinalIgnoreCase))
            {
                int count;
                int.TryParse(AppSettings.Get("PortFm2.PackCars", "0"), NumberStyles.None, CultureInfo.InvariantCulture, out count);
                for (int i = 0; i < count; i++)
                {
                    string saved = AppSettings.Get("PortFm2.PackCar" + i.ToString(CultureInfo.InvariantCulture), "");
                    Fm2PortRecord record = (saved.Length > 0) ? Fm2PortRecord.Parse(saved.Replace(PackLineSeparator.ToString(), "\r\n")) : null;
                    if (record != null)
                    {
                        choices.Add(record);
                    }
                }
            }
            this.pack.Clear();
            foreach (Fm2PortRecord choice in choices)
            {
                SourceCar car = list.Find(delegate(SourceCar c)
                {
                    return string.Equals(c.MediaName, choice.SourceMediaName, StringComparison.OrdinalIgnoreCase) && string.Equals(c.Dlc ?? "", choice.SourcePack ?? "", StringComparison.OrdinalIgnoreCase);
                });
                if (car == null)
                {
                    notes.AppendLine("pack: " + choice.SourceMediaName + (choice.SourcePack.Length > 0 ? " (" + choice.SourcePack + ")" : "") + " is not in the source any more, left out");
                    continue;
                }
                if (car.Fm2Id > 0)
                {
                    notes.AppendLine("pack: " + car.Name + " is in the FM2 folder now (car " + car.Fm2Id + "), left out");
                    continue;
                }
                if (choice.MakeId > 0 && !this.fm2MakeList.Exists(delegate(object[] m) { return Convert.ToInt64(m[0], CultureInfo.InvariantCulture) == choice.MakeId; }))
                {
                    notes.AppendLine("pack: " + car.Name + "'s make " + choice.MakeId + " was a new make of an earlier session: the source's make again (create it with + and choose it)");
                    choice.MakeId = -1;
                }
                car.MakeId = choice.MakeId;
                PackItem item = new PackItem();
                item.Car = car;
                item.Choice = choice;
                this.pack.Add(item);
            }
            if (choices.Count > 0)
            {
                this.status.Text = this.pack.Count + " car(s) in the pack (kept from before). " + this.status.Text;
            }
            if (notes.Length > 0)
            {
                this.report.AppendText("\r\n" + notes.ToString());
            }
            this.packTrialFolder = null;
            if (this.pack.Count != choices.Count)
            {
                this.SavePack();    // the cars left out (see the report) are out of the saved pack too
            }
            this.RefreshPackList();
            this.UpdateButtons();
        }

        // The FM2 files the port reads and writes, copied under the stage at the same places.
        private static void BuildStage(string target, string stage, IEnumerable<string> templates, List<string> log)
        {
            foreach (string template in templates)
            {
                CarDuplicate copy = new CarDuplicate();
                copy.TargetRoot = target;
                copy.SourceMediaName = template;
                copy.Stage(stage);      // database, zip list, string tables, the template's car / wheel / tyre zips, thumbnail zip, sound zips
            }
            string media = Path.Combine(target, "Media");
            string audio = Path.Combine(Path.Combine(media, "Audio"), "Cars");
            // new makes' / part makers' logos, the port's HarmonicTunings (Fm2LookupPort, Fm2SoundPort)
            foreach (string file in new string[] { Path.Combine(Path.Combine(media, "UI"), "Textures.zip"), Path.Combine(Path.Combine(audio, "Engines"), "HarmonicTuning.zip"),
                Path.Combine(Path.Combine(audio, "Engines"), "DSPTuning.zip"), Path.Combine(audio, "EmitterData.zip") })
            {
                if (!File.Exists(file))
                {
                    continue;
                }
                string copy = Path.Combine(stage, file.Substring(target.TrimEnd('\\').Length + 1));
                if (!File.Exists(copy))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(copy));
                    File.Copy(file, copy, true);
                }
            }
            foreach (string lod in new string[] { "LOD1", "LOD2" })
            {
                Directory.CreateDirectory(Path.Combine(Path.Combine(Path.Combine(Path.Combine(Path.Combine(stage, "Media"), "Audio"), "Cars"), "Engines"), Path.Combine("Soundbanks", lod)));
            }
            log.Add("stage: " + stage + " (the FM2 files the port reads and writes; the FM2 folder is only read)");
        }

        private void RunPackTrial()
        {
            string source = this.loadedSource;
            string target = this.loadedTarget;
            string content = this.XeniaContent();
            int offer = this.Offer();
            if (this.pack.Count == 0 || target == null)
            {
                return;
            }
            long first = this.FirstCar();
            if (first == 0)
            {
                MessageBox.Show(this, "Put the pack's first car id (" + DbIds.Fm2FirstFreeCarId + "-" + DbIds.Fm2LastFreeCarId + "), or Free.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (content != null && offer > 0 && Directory.Exists(DlcPackage.PackagePathFm2(content, offer)))
            {
                MessageBox.Show(this, "DLC" + offer + " is in Xenia's FM2 content folder already. Remove Pack first, or choose another offer id (Free).", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            List<PackItem> items = new List<PackItem>(this.pack);
            List<CarMakeSpec> makes = new List<CarMakeSpec>();
            foreach (PackItem item in items)
            {
                CarMakeSpec spec = this.PendingMake(item.Choice.MakeId);
                if (spec != null && !makes.Contains(spec))
                {
                    makes.Add(spec);
                }
            }
            string folder = Path.Combine(Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "PackFm2"), "pack_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture));
            ForzaGame game = this.sourceGame;
            this.packTrialFolder = null;
            this.status.Text = "Trial of the pack (" + items.Count + " car(s))...";
            this.Background(delegate
            {
                // only the last stage is kept: each one holds a copy of FM2's UI\Textures.zip
                string parent = Path.GetDirectoryName(folder);
                if (Directory.Exists(parent))
                {
                    foreach (string old in Directory.GetDirectories(parent, "pack_*"))
                    {
                        try
                        {
                            Directory.Delete(old, true);
                        }
                        catch (IOException)
                        {
                        }
                        catch (UnauthorizedAccessException)
                        {
                        }
                    }
                }
                StringBuilder text = new StringBuilder();
                Dictionary<string, List<ReviewRow>> reviews = new Dictionary<string, List<ReviewRow>>(StringComparer.OrdinalIgnoreCase);
                Dictionary<PackItem, long> ids = new Dictionary<PackItem, long>();
                string failed = null;
                List<string> log = new List<string>();
                try
                {
                    List<string> templates = new List<string>();
                    foreach (PackItem item in items)
                    {
                        if (!templates.Exists(delegate(string t) { return string.Equals(t, item.Choice.Template, StringComparison.OrdinalIgnoreCase); }))
                        {
                            templates.Add(item.Choice.Template);
                        }
                    }
                    BuildStage(target, folder, templates, log);
                    foreach (CarMakeSpec spec in makes)
                    {
                        WriteMake(folder, spec, null, log);
                    }
                }
                catch (Exception e)
                {
                    failed = "the stage: " + e.Message;
                }
                foreach (string line in log)
                {
                    text.AppendLine(line);
                }
                Dictionary<long, string> packCars = (failed == null) ? DbIds.PackCarIds(OtherPacks(content, offer)) : null;
                long next = first;
                foreach (PackItem item in items)
                {
                    if (failed != null)
                    {
                        break;
                    }
                    SourceCar car = item.Car;
                    CarPortFm2Chain chain = new CarPortFm2Chain();
                    try
                    {
                        long id;
                        using (SqliteDatabase db = SqliteDatabase.FromBytes(File.ReadAllBytes(CarPortFm2Chain.FindDatabase(folder))))
                        {
                            id = FreeCarId(db, next, packCars);
                        }
                        if (id == 0)
                        {
                            throw new InvalidOperationException("no free car id left from " + next + " up to " + DbIds.Fm2LastFreeCarId + ".");
                        }
                        next = id + 1;
                        chain.SourceRoot = SourceRootOf(car, source, chain.Log);
                        chain.SourceBaseRoot = (car.Pack != null) ? source : null;
                        chain.SourceMediaName = car.MediaName;
                        chain.TargetRoot = folder;
                        chain.PackStage = true;
                        chain.PerfRoot = target;
                        chain.ChosenId = id;
                        chain.TemplateMediaName = item.Choice.Template;
                        chain.NewMediaName = car.TargetMedia;
                        chain.SourceGameName = GameInfo.Name(game) ?? "";
                        chain.SourcePack = car.Dlc ?? "";
                        chain.DisplayName = car.Name;
                        chain.ModelShort = car.Short;
                        chain.Year = car.Year;
                        chain.SkipParts.AddRange(item.Choice.Skip);
                        chain.CarMaterials = new Dictionary<string, string>(item.Choice.CarMaterials, StringComparer.Ordinal);
                        chain.WheelMaterials = new Dictionary<string, string>(item.Choice.WheelMaterials, StringComparer.Ordinal);
                        chain.UseSoundOf = item.Choice.UseSoundOf;
                        chain.Swaps = item.Choice.Swaps;
                        chain.MakeId = item.Choice.MakeId;
                        chain.Progress = delegate(string step)
                        {
                            this.BeginInvoke((MethodInvoker) delegate { this.status.Text = "Trial Pack, " + car.Name + ": " + step; });
                        };
                        chain.Run();
                        ids[item] = chain.CarId;
                    }
                    catch (Exception e)
                    {
                        failed = car.Name + ": " + e.Message;
                    }
                    text.AppendLine();
                    text.AppendLine("== " + car.Name + " (" + car.MediaName + " -> " + car.TargetMedia + ")" + (ids.ContainsKey(item) ? ", car " + ids[item] : ", FAILED"));
                    foreach (string line in chain.Log)
                    {
                        text.AppendLine(line);
                    }
                    List<ReviewRow> review = ReviewFromLog(chain.Log);
                    if (!ids.ContainsKey(item))
                    {
                        review.Insert(0, new ReviewRow("pack", "FAILED: " + failed, ReviewKind.Error));
                    }
                    reviews[car.MediaName] = review;
                }
                return delegate
                {
                    foreach (KeyValuePair<string, List<ReviewRow>> pair in reviews)
                    {
                        this.installReviews[pair.Key] = pair.Value;
                    }
                    foreach (PackItem item in items)
                    {
                        long id;
                        item.CarId = ids.TryGetValue(item, out id) ? id : 0;
                    }
                    this.report.Text = ((failed != null) ? "TRIAL PACK FAILED (" + failed + "): nothing can be built until it goes through.\r\n\r\n" : "TRIAL PACK: " + items.Count + " car(s) ported into the stage.\r\n\r\n") + text.ToString();
                    this.tabs.SelectedIndex = 0;
                    this.packTrialFolder = (failed == null) ? folder : null;
                    this.RefreshPackList();
                    this.ShowReview();
                    this.status.Text = (failed != null) ? "Trial Pack failed: " + failed : "Trial done: name the pack, then Build DLC Package. The Review tab shows each car's lines (choose it in the list).";
                };
            });
        }

        private void BuildPack()
        {
            string target = this.loadedTarget;
            string trial = this.packTrialFolder;
            string content = this.XeniaContent();
            int offer = this.Offer();
            string name = this.packName.Text.Trim();
            if (target == null || trial == null || content == null || offer <= 0 || name.Length == 0)
            {
                return;
            }
            if (Directory.Exists(DlcPackage.PackagePathFm2(content, offer)))
            {
                MessageBox.Show(this, "DLC" + offer + " is in Xenia's FM2 content folder already: Remove Pack first (then Trial Pack again), or choose another offer id.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!XeniaGuard.Confirm(this, this.Text, "FM2's DLC packages"))
            {
                return;
            }
            this.status.Text = "Building DLC" + offer + "...";
            this.Background(delegate
            {
                StringBuilder text = new StringBuilder();
                text.AppendLine("Building the pack \"" + name + "\" (offer " + offer + ") from " + trial + " into " + content + ":");
                string failed = null;
                Directory.CreateDirectory(content);
                List<string> others = DbIds.PackDatabases(content);
                try
                {
                    string package = DlcPackage.BuildFolderFm2(target, trial, content, content, offer, name, text);
                    string merge = Path.Combine(Path.Combine(Path.Combine(Path.Combine(Path.Combine(Path.Combine(package, "Media"), "DLCZips"), DlcPackage.LayerFm2(offer)), "media"), "db"), "patch");
                    DbIntegrity.Result merged = DbIntegrity.CheckMerge(File.ReadAllBytes(CarPortFm2Chain.FindDatabase(target)), others,
                        File.ReadAllBytes(Path.Combine(merge, offer.ToString(CultureInfo.InvariantCulture) + "0000_merge.slt")), ForzaGame.Motorsport2);
                    if (!merged.Ok)
                    {
                        throw new InvalidOperationException("the pack's database would break the game's:\r\n  " + string.Join("\r\n  ", merged.Errors.ToArray()));
                    }
                    text.AppendLine("pack database check: merged into the game's database the rules hold");
                    foreach (string line in merged.Warnings)
                    {
                        text.AppendLine("  warning: " + line);
                    }
                    DlcPackage.ZipLayerFm2(package, offer, text);
                    DlcPackage.RefreshNamesPack(ForzaGame.Motorsport2, target, content, text);
                }
                catch (Exception e)
                {
                    failed = e.Message;
                    try
                    {
                        DlcPackage.RemoveFm2(content, offer);     // nothing half-built is left in Xenia
                    }
                    catch (IOException)
                    {
                    }
                }
                return delegate
                {
                    this.report.Text = ((failed != null) ? "BUILD FAILED: " + failed + "\r\nNothing was left in Xenia.\r\n\r\n" : "") + text.ToString();
                    this.tabs.SelectedIndex = 0;
                    this.status.Text = (failed != null) ? "Build failed: " + failed : "DLC" + offer + " built. Start FM2 in Xenia (with its title update): the cars are in the dealer's DLC.";
                    this.UpdatePackInfo();
                };
            });
        }

        // The package an Add To Package goes into: the game's folder packs and package files in Xenia's content folder.
        internal static string PickPackage(IWin32Window owner, ForzaGame game, string content, string title)
        {
            List<KeyValuePair<string, string>> packages = DlcPackageAdd.Packages(game, content);
            if (packages.Count == 0)
            {
                MessageBox.Show(owner, "Xenia's " + GameInfo.Tag(game) + " content folder has no package to add to:\n" + content, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }
            List<object[]> rows = new List<object[]>();
            foreach (KeyValuePair<string, string> package in packages)
            {
                rows.Add(new object[] { package.Key, Path.GetFileName(package.Key), package.Value });
            }
            using (PickDialog dialog = new PickDialog(title + " - choose the package", new string[] { "Path", "Package", "What" }, new int[] { 0, 260, 300 }, rows, null))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK || dialog.SelectedKey == null)
                {
                    return null;
                }
                return Convert.ToString(dialog.SelectedKey, CultureInfo.InvariantCulture);
            }
        }

        // Add To Package (as the FM4 window's): the last Trial Pack's cars into a package already in Xenia.
        private void AddToPackage()
        {
            string target = this.loadedTarget;
            string trial = this.packTrialFolder;
            string content = this.XeniaContent();
            if (target == null || trial == null || content == null)
            {
                return;
            }
            string package = PickPackage(this, ForzaGame.Motorsport2, content, this.Text);
            if (package == null)
            {
                return;
            }
            if (MessageBox.Show(this, "Add the " + this.pack.Count + " car(s) of the last Trial Pack to\n" + package + "?\n\nThe package keeps its offer and its cars; the new cars' rows, strings and files are added. "
                + (BackupPolicy.Enabled ? "The original is kept in bak\\ first." : "Backups are off: the original is not kept."), this.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            if (!XeniaGuard.Confirm(this, this.Text, "FM2's DLC packages"))
            {
                return;
            }
            string name = File.Exists(package) ? null : this.packName.Text.Trim();
            this.status.Text = "Adding the cars to " + Path.GetFileName(package) + "...";
            this.Background(delegate
            {
                StringBuilder text = new StringBuilder();
                text.AppendLine("Add To Package: " + package);
                string failed = null;
                try
                {
                    DlcPackageAdd.AddFm(ForzaGame.Motorsport2, package, target, trial, content, name, text);
                    DlcPackage.RefreshNamesPack(ForzaGame.Motorsport2, target, content, text);
                }
                catch (Exception e)
                {
                    failed = e.Message;
                }
                return delegate
                {
                    this.report.Text = ((failed != null) ? "ADD TO PACKAGE FAILED: " + failed + "\r\nThe package was not changed.\r\n\r\n" : "") + text.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
                    this.tabs.SelectedIndex = 0;
                    this.status.Text = (failed != null) ? "Add To Package failed: " + failed : "The cars are in " + Path.GetFileName(package) + ". Start FM2 in Xenia.";
                    this.UpdatePackInfo();
                };
            });
        }

        // Build for Xbox 360 (as the FM3 / FM4 windows): the pack as an STFS package file in a folder the user chooses.
        private void BuildXbox()
        {
            string target = this.loadedTarget;
            string trial = this.packTrialFolder;
            int offer = this.Offer();
            string name = this.packName.Text.Trim();
            if (target == null || trial == null || offer <= 0 || name.Length == 0)
            {
                return;
            }
            string output;
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Folder for the Xbox 360 package file (saved directly in it)";
                string start = DlcPackage.PackageOutputStart("CarPort.XboxOut.FM2", DlcPackage.TitleIdFm2);   // never another game's folder
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
            string foreign = DlcPackage.ForeignTitleFolder(output, DlcPackage.TitleIdFm2);
            if (foreign != null)
            {
                MessageBox.Show(this, output + " is the content folder of another game (title " + foreign + ").\n\nThis is a Forza Motorsport 2 package (title " + DlcPackage.TitleIdFm2 + "): choose its own folder.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            AppSettings.Set("CarPort.XboxOut.FM2", output);
            string installed = this.XeniaContent();   // the strings and ids of the FM2 packs installed in Xenia, if its folder is set
            this.status.Text = "Building the Xbox 360 package...";
            this.Background(delegate
            {
                StringBuilder text = new StringBuilder();
                text.AppendLine(string.Format("Building the Xbox 360 package \"{0}\" (offer {1}) into {2}", name, offer, output));
                string failed = null;
                string work = Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "XboxFm2_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
                try
                {
                    List<string> others = DbIds.PackDatabases(installed);
                    string folder = DlcPackage.BuildFolderFm2(target, trial, Path.Combine(work, "00000002"), installed, offer, name, text);
                    string merge = Path.Combine(Path.Combine(Path.Combine(Path.Combine(Path.Combine(Path.Combine(folder, "Media"), "DLCZips"), DlcPackage.LayerFm2(offer)), "media"), "db"), "patch");
                    DbIntegrity.Result merged = DbIntegrity.CheckMerge(File.ReadAllBytes(CarPortFm2Chain.FindDatabase(target)), others,
                        File.ReadAllBytes(Path.Combine(merge, offer.ToString(CultureInfo.InvariantCulture) + "0000_merge.slt")), ForzaGame.Motorsport2);
                    if (!merged.Ok)
                    {
                        throw new InvalidOperationException("the pack's database would break the game's:\r\n  " + string.Join("\r\n  ", merged.Errors.ToArray()));
                    }
                    text.AppendLine("pack database check: merged into the game's database the rules hold");
                    DlcPackage.BuildConsolePackage(folder, output, name, null, text, DlcPackage.TitleIdFm2, "Forza Motorsport 2");   // the layer zipped deflate
                }
                catch (Exception e)
                {
                    failed = e.Message;
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
                    this.report.Text = text.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n") + ((failed != null) ? "\r\nFAILED: " + failed + "\r\n" : "\r\nDone.\r\n");
                    this.tabs.SelectedIndex = 0;
                    this.status.Text = (failed != null) ? "The package failed, see the report." : "Xbox 360 package built in " + output;
                };
            });
        }

        private void RemovePackFromXenia()
        {
            string content = this.XeniaContent();
            int offer = this.Offer();
            string target = this.loadedTarget;
            if (content == null || offer <= 0 || !Directory.Exists(DlcPackage.PackagePathFm2(content, offer)))
            {
                return;
            }
            if (MessageBox.Show(this, "Remove DLC" + offer + " from Xenia's FM2 content folder?\n\n" + DlcPackage.PackagePathFm2(content, offer)
                + "\n\nIf you own one of its cars in an FM2 garage, the saved car can crash the game.", this.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            if (!XeniaGuard.Confirm(this, this.Text, "FM2's DLC packages"))
            {
                return;
            }
            this.Background(delegate
            {
                StringBuilder text = new StringBuilder();
                string failed = null;
                try
                {
                    DlcPackage.RemoveFm2(content, offer);
                    text.AppendLine("DLC" + offer + " removed from " + content);
                    if (target != null)
                    {
                        DlcPackage.RefreshNamesPack(ForzaGame.Motorsport2, target, content, text);
                    }
                }
                catch (Exception e)
                {
                    failed = e.Message;
                }
                return delegate
                {
                    this.report.Text = ((failed != null) ? "REMOVE FAILED: " + failed + "\r\n\r\n" : "") + text.ToString();
                    this.tabs.SelectedIndex = 0;
                    this.status.Text = (failed != null) ? "Remove failed: " + failed : "DLC" + offer + " removed from Xenia.";
                    this.UpdatePackInfo();
                };
            });
        }
    }
}
