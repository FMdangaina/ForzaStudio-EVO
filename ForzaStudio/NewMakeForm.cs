namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Windows.Forms;

    // New car make (CarMake): its name, code, country, profile text and logo image. Two ways out:
    //   Create in the game   the make goes into the FM4 folder now (local database and files);
    //   Add to the pack      the make is kept by the port window and written into the trial folder of
    //                        every pack trial and single-car trial, so the DLC pack carries it.
    // Spec holds the result; InGame tells which button was used.
    internal sealed class NewMakeForm : Form
    {
        private sealed class Country
        {
            public long Id;
            public string Name;

            public override string ToString()
            {
                return this.Name;
            }
        }

        private sealed class Region
        {
            public long Id;
            public string Name;

            public override string ToString()
            {
                return this.Name;
            }
        }

        private const long NewCountryId = -1;   // the combo's "New country..." item

        private readonly string target;
        private readonly SqliteDatabase fm2Db;     // FM2 (the FM2 port window): its rules (Fm2LookupPort), no new country
        private readonly long firstId;
        private readonly long firstCountryId;
        private readonly List<Region> regions = new List<Region>();
        private CarCountrySpec newCountry;
        private int lastCountryIndex;
        private bool changingCountry;
        private readonly TextBox name;
        private readonly TextBox code;
        private readonly TextBox logoName;
        private readonly ComboBox country;
        private readonly TextBox id;
        private readonly TextBox profile;
        private readonly TextBox logoFile;
        private readonly PictureBox preview;
        private readonly Label problem;
        private bool logoNameEdited;

        public CarMakeSpec Spec;
        public bool InGame;
        private static string gameTag = "FM4";   // the target game's tag, for the texts (also of the New Country box)

        // existingIds: makes already pending in the port window; xeniaContent: for the Id of a DLC make.
        public NewMakeForm(string target, string xeniaContent, List<long> pendingIds, bool canCreateInGame, bool canAddToPack)
            : this(target, xeniaContent, pendingIds, canCreateInGame, canAddToPack, false)
        {
        }

        // fm2: a make for Forza Motorsport 2 (Fm2LookupPort): the id above every Forza's makes, FM2's countries only (its
        // country rows carry a flag symbol of its font, none can be added), FM2's checks, one logo folder.
        public NewMakeForm(string target, string xeniaContent, List<long> pendingIds, bool canCreateInGame, bool canAddToPack, bool fm2)
        {
            this.target = target;
            if (fm2)
            {
                this.fm2Db = SqliteDatabase.FromBytes(File.ReadAllBytes(CarPortFm2Chain.FindDatabase(target)));
                this.FormClosed += delegate { this.fm2Db.Dispose(); };
            }
            this.AutoScaleMode = AutoScaleMode.None;
            this.Text = "New Car Make";
            gameTag = GameInfo.Tag(GameInfo.Detect(target));   // the wording names the target game (FM4 or FM3)
            this.ClientSize = new Size(560, 470);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;

            long free;
            List<Country> countries = new List<Country>();
            long freeCountry;
            using (SqliteDatabase db = SqliteDatabase.FromBytes(File.ReadAllBytes(CarMake.DatabasePath(target))))
            {
                free = fm2 ? Fm2LookupPort.FreeMakeId(db) : CarMake.FreeId(db, xeniaContent);
                StringTable names = null;
                try
                {
                    names = StringTable.Load(target, "EN");
                }
                catch (Exception)
                {
                }
                foreach (object[] row in db.Query("SELECT CountryID, Country FROM List_Country ORDER BY CountryID").Rows)
                {
                    string text = (names != null) ? names.Lookup("List_Country", "Country", row[1] as string) : null;
                    Country item = new Country();
                    item.Id = Convert.ToInt64(row[0], CultureInfo.InvariantCulture);
                    item.Name = string.IsNullOrEmpty(text) ? "country " + item.Id : text;
                    countries.Add(item);
                }
                freeCountry = CarMake.FreeCountryId(db, xeniaContent);
                foreach (object[] row in db.Query("SELECT Id, DisplayName FROM List_Region ORDER BY Id").Rows)
                {
                    string text = (names != null) ? names.Lookup("List_Region", "DisplayName", row[1] as string) : null;
                    Region region = new Region();
                    region.Id = Convert.ToInt64(row[0], CultureInfo.InvariantCulture);
                    region.Name = string.IsNullOrEmpty(text) ? "region " + region.Id : text;
                    this.regions.Add(region);
                }
            }
            this.firstCountryId = freeCountry;
            foreach (long taken in pendingIds)
            {
                free = Math.Max(free, taken + 1);
            }
            this.firstId = free;
            countries.Sort(delegate(Country a, Country b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });

            int y = 14;
            this.name = this.AddRow("Name:", y, 360, "The make's name as the game shows it (every language gets it; change a language later in the string tables)");
            this.name.TextChanged += delegate { this.NameChanged(); };
            y += 32;
            this.code = this.AddRow("Code:", y, 70, "ManufacturerCode, 2-4 capital letters or digits (the game's: ACU, AST, POR)");
            this.code.CharacterCasing = CharacterCasing.Upper;
            this.code.MaxLength = 4;
            this.code.TextChanged += delegate { this.Recheck(); };
            Label idLabel = new Label();
            idLabel.Text = "Id:";
            idLabel.SetBounds(240, y + 3, 24, 20);
            this.id = new TextBox();
            this.id.SetBounds(268, y, 70, 24);
            this.id.Text = this.firstId.ToString(CultureInfo.InvariantCulture);
            this.id.TextChanged += delegate { this.Recheck(); };
            new ToolTip().SetToolTip(this.id, "List_CarMake.ID; the next free one, also past the makes of the DLC packs in Xenia's content folder");
            this.Controls.Add(idLabel);
            this.Controls.Add(this.id);
            y += 32;
            this.logoName = this.AddRow("Logo name:", y, 160, fm2 ? "The logo file is L_MAN_<name>_Logo in FM2's one logo folder of UI\\Textures.zip (L_Map/Manufacturer_Logos)"
                : "The logo files are L_MAN_<name>_Logo in the " + ((gameTag == "FM3") ? "three" : "six") + " logo folders of UI\\Textures.zip");
            this.logoName.TextChanged += delegate { this.logoNameEdited = this.logoName.Focused || this.logoNameEdited; this.Recheck(); };
            y += 32;
            Label countryLabel = new Label();
            countryLabel.Text = "Country:";
            countryLabel.SetBounds(12, y + 3, 80, 20);
            this.country = new ComboBox();
            this.country.DropDownStyle = ComboBoxStyle.DropDownList;
            this.country.SetBounds(96, y, 200, 24);
            this.country.MaxDropDownItems = 20;
            this.country.Items.AddRange(countries.ToArray());
            if (!fm2)
            {
                Country newItem = new Country();
                newItem.Id = NewCountryId;
                newItem.Name = "New country...";
                this.country.Items.Add(newItem);
            }
            if (this.country.Items.Count > 0)
            {
                this.country.SelectedIndex = 0;
            }
            this.lastCountryIndex = this.country.SelectedIndex;
            this.country.SelectedIndexChanged += delegate { this.CountryChanged(); };
            new ToolTip().SetToolTip(this.country, "The make's country. New country...: a country " + gameTag + " lacks, with its flag and name (added to the pack with the make)");
            this.Controls.Add(countryLabel);
            this.Controls.Add(this.country);
            y += 32;
            Label profileLabel = new Label();
            profileLabel.Text = "Profile:";
            profileLabel.SetBounds(12, y + 3, 80, 20);
            this.profile = new TextBox();
            this.profile.Multiline = true;
            this.profile.ScrollBars = ScrollBars.Vertical;
            this.profile.SetBounds(96, y, 452, 110);
            new ToolTip().SetToolTip(this.profile, "The text of the make's page (Profile). May stay empty.");
            this.Controls.Add(profileLabel);
            this.Controls.Add(this.profile);
            y += 120;

            Label logoLabel = new Label();
            logoLabel.Text = "Logo:";
            logoLabel.SetBounds(12, y + 3, 80, 20);
            this.logoFile = new TextBox();
            this.logoFile.ReadOnly = true;
            this.logoFile.SetBounds(96, y, 360, 24);
            Button browse = new Button();
            browse.Text = "Image...";
            browse.SetBounds(464, y - 1, 84, 26);
            browse.Click += delegate { this.PickLogo(); };
            this.Controls.Add(logoLabel);
            this.Controls.Add(this.logoFile);
            this.Controls.Add(browse);
            y += 32;
            this.preview = new PictureBox();
            this.preview.SetBounds(96, y, 200, 90);
            this.preview.SizeMode = PictureBoxSizeMode.Zoom;
            this.preview.BackColor = Color.FromArgb(60, 60, 64);
            this.Controls.Add(this.preview);
            Label hint = new Label();
            hint.Text = "A PNG with transparency is best. It is fitted into each logo folder's size, taken from Acura's logos.";
            hint.SetBounds(304, y, 244, 90);
            hint.ForeColor = ForzaTheme.TextDim;
            this.Controls.Add(hint);
            y += 100;

            this.problem = new Label();
            this.problem.SetBounds(12, y, 536, 20);
            this.problem.ForeColor = ForzaTheme.Yellow;
            this.Controls.Add(this.problem);
            y += 26;

            Button game = new Button();
            game.Text = "Create in the game";
            game.SetBounds(12, y, 150, 30);
            game.Visible = canCreateInGame;
            game.Click += delegate { this.Accept(true); };
            new ToolTip().SetToolTip(game, "Writes the make into the " + NewMakeForm.gameTag + " folder's database, string tables and Textures.zip now (files backed up to bak\\)");
            Button pack = new Button();
            pack.Text = "Add to the DLC pack";
            pack.SetBounds(canCreateInGame ? 170 : 12, y, 150, 30);
            pack.Visible = canAddToPack;
            pack.Click += delegate { this.Accept(false); };
            new ToolTip().SetToolTip(pack, "Kept in the port window and written into every trial and the DLC pack, so the pack carries the make; the game folder stays untouched");
            Button close = new Button();
            close.Text = "Cancel";
            close.SetBounds(468, y, 80, 30);
            close.DialogResult = DialogResult.Cancel;
            this.Controls.Add(game);
            this.Controls.Add(pack);
            this.Controls.Add(close);
            this.CancelButton = close;
            ForzaTheme.Apply(this);
            this.Recheck();
        }

        private TextBox AddRow(string caption, int y, int width, string tip)
        {
            Label label = new Label();
            label.Text = caption;
            label.SetBounds(12, y + 3, 80, 20);
            TextBox box = new TextBox();
            box.SetBounds(96, y, width, 24);
            new ToolTip().SetToolTip(box, tip);
            this.Controls.Add(label);
            this.Controls.Add(box);
            return box;
        }

        // The logo name follows the name (letters and digits) until the user edits it.
        private void NameChanged()
        {
            if (!this.logoNameEdited)
            {
                this.logoName.Text = Regex.Replace(this.name.Text, "[^A-Za-z0-9]", string.Empty);
                this.logoNameEdited = false;
            }
            this.Recheck();
        }

        private void PickLogo()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Logo image";
                dialog.Filter = "Images (*.png;*.jpg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                try
                {
                    using (Image image = Image.FromFile(dialog.FileName))
                    {
                        this.preview.Image = new Bitmap(image);
                    }
                    this.logoFile.Text = dialog.FileName;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Not an image: " + ex.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            this.Recheck();
        }

        private CarMakeSpec Build()
        {
            CarMakeSpec spec = new CarMakeSpec();
            long value;
            spec.Id = long.TryParse(this.id.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value) ? value : 0;
            spec.Code = this.code.Text.Trim().ToUpperInvariant();
            spec.Base = this.logoName.Text.Trim();
            spec.Name = this.name.Text.Trim();
            spec.Profile = this.profile.Text.Trim();
            spec.Logo = this.logoFile.Text;
            Country selected = this.country.SelectedItem as Country;
            spec.CountryId = (selected != null) ? selected.Id : 0;
            if (selected != null && selected.Id == NewCountryId)
            {
                spec.NewCountry = this.newCountry;
                spec.CountryId = (this.newCountry != null) ? this.newCountry.Id : 0;
            }
            return spec;
        }

        // "New country...": asks for the country's name, region and flag. Cancelling goes back to the
        // country picked before; a country made earlier is edited.
        private void CountryChanged()
        {
            if (this.changingCountry)
            {
                return;
            }
            Country selected = this.country.SelectedItem as Country;
            if (selected == null || selected.Id != NewCountryId)
            {
                this.lastCountryIndex = this.country.SelectedIndex;
                this.Recheck();
                return;
            }
            this.changingCountry = true;
            using (NewCountryForm form = new NewCountryForm(this.regions, this.newCountry, this.firstCountryId))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    this.newCountry = form.Spec;
                    selected.Name = "New: " + this.newCountry.Name;
                    this.country.Items[this.country.SelectedIndex] = selected;
                    this.lastCountryIndex = this.country.SelectedIndex;
                }
                else if (this.newCountry == null)
                {
                    this.country.SelectedIndex = this.lastCountryIndex;
                }
            }
            this.changingCountry = false;
            this.Recheck();
        }

        // Name, region and flag of a country FM4 lacks.
        private sealed class NewCountryForm : Form
        {
            private readonly TextBox countryName;
            private readonly TextBox flagName;
            private readonly ComboBox region;
            private readonly TextBox flagFile;
            private readonly PictureBox preview;
            private readonly Label problem;
            private readonly long id;
            private bool flagNameEdited;

            public CarCountrySpec Spec;

            public NewCountryForm(List<Region> regions, CarCountrySpec current, long id)
            {
                this.id = (current != null && current.Id > 0) ? current.Id : id;
                this.AutoScaleMode = AutoScaleMode.None;
                this.Text = "New Country";
                this.ClientSize = new Size(420, 300);
                this.FormBorderStyle = FormBorderStyle.FixedDialog;
                this.MaximizeBox = false;
                this.MinimizeBox = false;
                this.ShowInTaskbar = false;
                this.StartPosition = FormStartPosition.CenterParent;

                this.countryName = this.AddRow("Name:", 14, 260, "The name as the game shows it, e.g. SWEDEN (every language gets it)");
                this.countryName.TextChanged += delegate { this.NameChanged(); };
                this.flagName = this.AddRow("Flag name:", 46, 160, "The flag file is L_FLA_<name>_flag in UI\\Textures.zip (letters and digits only)");
                this.flagName.TextChanged += delegate { this.flagNameEdited = this.flagName.Focused || this.flagNameEdited; this.Recheck(); };
                Label regionLabel = new Label();
                regionLabel.Text = "Region:";
                regionLabel.SetBounds(12, 81, 80, 20);
                this.region = new ComboBox();
                this.region.DropDownStyle = ComboBoxStyle.DropDownList;
                this.region.SetBounds(96, 78, 200, 24);
                this.region.Items.AddRange(regions.ToArray());
                this.Controls.Add(regionLabel);
                this.Controls.Add(this.region);
                Label flagLabel = new Label();
                flagLabel.Text = "Flag:";
                flagLabel.SetBounds(12, 115, 80, 20);
                this.flagFile = new TextBox();
                this.flagFile.ReadOnly = true;
                this.flagFile.SetBounds(96, 110, 230, 24);
                Button browse = new Button();
                browse.Text = "Image...";
                browse.SetBounds(334, 109, 76, 26);
                browse.Click += delegate { this.PickFlag(); };
                this.Controls.Add(flagLabel);
                this.Controls.Add(this.flagFile);
                this.Controls.Add(browse);
                this.preview = new PictureBox();
                this.preview.SetBounds(96, 142, 160, 80);
                this.preview.SizeMode = PictureBoxSizeMode.Zoom;
                this.preview.BackColor = Color.FromArgb(60, 60, 64);
                this.Controls.Add(this.preview);
                Label hint = new Label();
                hint.Text = "The picture is stretched over the whole flag texture, like " + gameTag + "'s.";
                hint.SetBounds(264, 142, 146, 80);
                hint.ForeColor = ForzaTheme.TextDim;
                this.Controls.Add(hint);
                this.problem = new Label();
                this.problem.SetBounds(12, 232, 396, 20);
                this.problem.ForeColor = ForzaTheme.Yellow;
                this.Controls.Add(this.problem);
                Button ok = new Button();
                ok.Text = "OK";
                ok.SetBounds(12, 260, 90, 30);
                ok.Click += delegate { this.Accept(); };
                Button cancel = new Button();
                cancel.Text = "Cancel";
                cancel.SetBounds(320, 260, 90, 30);
                cancel.DialogResult = DialogResult.Cancel;
                this.Controls.Add(ok);
                this.Controls.Add(cancel);
                this.CancelButton = cancel;
                ForzaTheme.Apply(this);

                if (this.region.Items.Count > 0)
                {
                    this.region.SelectedIndex = 0;
                }
                if (current != null)
                {
                    this.countryName.Text = current.Name;
                    this.flagName.Text = current.Base;
                    this.flagNameEdited = true;
                    foreach (Region item in regions)
                    {
                        if (item.Id == current.RegionId)
                        {
                            this.region.SelectedItem = item;
                        }
                    }
                    this.SetFlag(current.Flag);
                }
                this.Recheck();
            }

            private TextBox AddRow(string caption, int y, int width, string tip)
            {
                Label label = new Label();
                label.Text = caption;
                label.SetBounds(12, y + 3, 80, 20);
                TextBox box = new TextBox();
                box.SetBounds(96, y, width, 24);
                new ToolTip().SetToolTip(box, tip);
                this.Controls.Add(label);
                this.Controls.Add(box);
                return box;
            }

            private void NameChanged()
            {
                if (!this.flagNameEdited)
                {
                    this.flagName.Text = Regex.Replace(this.countryName.Text, "[^A-Za-z0-9]", string.Empty).ToLowerInvariant();
                    this.flagNameEdited = false;
                }
                this.Recheck();
            }

            private void PickFlag()
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = "Flag image";
                    dialog.Filter = "Images (*.png;*.jpg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*";
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        this.SetFlag(dialog.FileName);
                    }
                }
                this.Recheck();
            }

            private void SetFlag(string path)
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return;
                }
                try
                {
                    using (Image image = Image.FromFile(path))
                    {
                        this.preview.Image = new Bitmap(image);
                    }
                    this.flagFile.Text = path;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Not an image: " + ex.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            private CarCountrySpec Build()
            {
                CarCountrySpec spec = new CarCountrySpec();
                spec.Id = this.id;
                spec.Name = this.countryName.Text.Trim();
                spec.Base = this.flagName.Text.Trim();
                spec.Flag = this.flagFile.Text;
                Region selected = this.region.SelectedItem as Region;
                spec.RegionId = (selected != null) ? selected.Id : 0;
                return spec;
            }

            private bool Recheck()
            {
                if (this.problem == null)
                {
                    return false;
                }
                CarCountrySpec spec = this.Build();
                string text = null;
                if (spec.Name.Length == 0)
                {
                    text = "The country needs a name.";
                }
                else if (!Regex.IsMatch(spec.Base, "^[A-Za-z0-9]{2,40}$"))
                {
                    text = "The flag name is letters and digits only.";
                }
                else if (spec.RegionId == 0)
                {
                    text = "Choose the region.";
                }
                else if (spec.Flag.Length == 0)
                {
                    text = "Choose the flag image.";
                }
                this.problem.Text = text ?? string.Empty;
                return text == null;
            }

            private void Accept()
            {
                if (!this.Recheck())
                {
                    return;
                }
                this.Spec = this.Build();
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }

        private bool Recheck()
        {
            if (this.problem == null)
            {
                return false;
            }
            CarMakeSpec spec = this.Build();
            string text = null;
            if (spec.Id <= 0)
            {
                text = "The Id is a number.";
            }
            else if (this.name.Text.Trim().Length > 0 || this.code.Text.Length > 0 || this.logoFile.Text.Length > 0)
            {
                try
                {
                    text = (this.fm2Db != null) ? Fm2LookupPort.MakeSpecProblem(spec, this.fm2Db, this.target) : CarMake.Check(spec, this.target, null);
                }
                catch (Exception ex)
                {
                    text = ex.Message;
                }
            }
            else
            {
                text = "Fill in the make.";
            }
            this.problem.Text = text ?? string.Empty;
            return text == null;
        }

        private void Accept(bool inGame)
        {
            if (!this.Recheck())
            {
                return;
            }
            this.Spec = this.Build();
            this.InGame = inGame;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
