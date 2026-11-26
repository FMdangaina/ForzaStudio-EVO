namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Windows.Forms;

    // MAKE of the FM2 window (B5 of docs/PORT_PARITY.md, user 2026-11-27), as the FM4 window's: the selected car's make
    // (default the source's: FM2's own, or added to FM2 by the port when FM2 lacks it, Fm2LookupPort.PrepareMake) or
    // any make FM2 has; "+" makes a new one (NewMakeForm in its FM2 mode, written by Fm2LookupPort.AddMake: the row, its
    // strings in every language, its logo). The choice goes to the trial's dry run, the install and the port record.
    internal sealed partial class CarPortFm2Form
    {
        private ComboBox makes;
        private Button newMake;
        private List<object[]> fm2MakeList = new List<object[]>();          // FM2's makes: ID, name
        private Dictionary<long, string> sourceMakeNames = new Dictionary<long, string>();
        private bool makesFilling;

        private sealed class MakeItem
        {
            public long Id;         // -1: the source's
            public string Text;

            public override string ToString()
            {
                return this.Text;
            }
        }

        private void BuildMakeRow(Panel options)
        {
            Label label = new Label();
            label.Text = "Make:";
            label.Location = new Point(480, 31);
            label.AutoSize = true;
            this.makes = new ComboBox();
            this.makes.DropDownStyle = ComboBoxStyle.DropDownList;
            this.makes.Location = new Point(524, 28);
            this.makes.Width = 280;
            this.makes.MaxDropDownItems = 24;
            this.makes.SelectedIndexChanged += delegate { this.MakeChosen(); };
            new ToolTip().SetToolTip(this.makes, "The make the car goes under in FM2: the source's (added to FM2 when it lacks it), or any make FM2 has");
            this.newMake = new Button();
            this.newMake.Text = "+";
            this.newMake.Location = new Point(810, 27);
            this.newMake.Size = new Size(30, 24);
            new ToolTip().SetToolTip(this.newMake, "New make: create a car make in the FM2 folder (name, code, country, logo, profile)");
            this.newMake.Click += delegate { this.NewMake(); };
            options.Controls.Add(label);
            options.Controls.Add(this.makes);
            options.Controls.Add(this.newMake);
        }

        private string Fm2MakeName(long id)
        {
            object[] make = this.fm2MakeList.Find(delegate(object[] m) { return Convert.ToInt64(m[0], CultureInfo.InvariantCulture) == id; });
            return (make != null) ? Convert.ToString(make[1], CultureInfo.InvariantCulture) : null;
        }

        // The selected car's Make choice.
        private void ShowMake()
        {
            SourceCar car = this.SelectedCar;
            this.makesFilling = true;
            this.makes.Items.Clear();
            this.makes.Enabled = car != null && !this.busy;
            this.newMake.Enabled = !this.busy && !string.IsNullOrEmpty(this.loadedTarget);
            if (car != null)
            {
                string sourceName;
                this.sourceMakeNames.TryGetValue(car.SourceMake, out sourceName);
                string fm2Name = this.Fm2MakeName(car.SourceMake);
                MakeItem own = new MakeItem();
                own.Id = -1;
                own.Text = "the source's: " + (fm2Name ?? sourceName ?? "make " + car.SourceMake) + " (" + car.SourceMake + ((fm2Name == null) ? ", added to FM2)" : ")");
                this.makes.Items.Add(own);
                int selected = 0;
                foreach (object[] make in this.fm2MakeList)
                {
                    MakeItem item = new MakeItem();
                    item.Id = Convert.ToInt64(make[0], CultureInfo.InvariantCulture);
                    item.Text = make[1] + " (" + item.Id + ")";
                    if (item.Id == car.MakeId)
                    {
                        selected = this.makes.Items.Count;
                    }
                    this.makes.Items.Add(item);
                }
                this.makes.SelectedIndex = selected;
            }
            this.makesFilling = false;
        }

        private void MakeChosen()
        {
            SourceCar car = this.SelectedCar;
            MakeItem item = this.makes.SelectedItem as MakeItem;
            if (this.makesFilling || car == null || item == null)
            {
                return;
            }
            // the source's own make chosen by its FM2 id is the same as the default
            car.MakeId = (item.Id == car.SourceMake) ? -1 : item.Id;
            if (this.CurrentTrial != null)
            {
                this.status.Text = car.Name + ": make " + item.Text + ". Trial Run again to see the database with it (Install uses it as it is).";
            }
        }

        // "+": a make created in the FM2 folder now (backed up first), then chosen for the selected car.
        private void NewMake()
        {
            string target = this.loadedTarget;
            if (this.busy || string.IsNullOrEmpty(target) || !File.Exists(CarPortFm2Chain.FindDatabase(target)))
            {
                return;
            }
            CarMakeSpec spec;
            using (NewMakeForm form = new NewMakeForm(target, InstalledDlc.ContentFolder(ForzaGame.Motorsport2), new List<long>(), true, false, true))
            {
                if (form.ShowDialog(this) != DialogResult.OK || !form.InGame)
                {
                    return;
                }
                spec = form.Spec;
            }
            if (!XeniaGuard.Confirm(this, this.Text, "the Forza Motorsport 2 files"))
            {
                return;
            }
            SourceCar car = this.SelectedCar;
            this.status.Text = "Creating the make " + spec.Name + "...";
            this.Background(delegate
            {
                List<string> log = new List<string>();
                Exception failed = null;
                try
                {
                    string dbPath = CarPortFm2Chain.FindDatabase(target);
                    string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                    string backup = BackupPolicy.Enabled ? Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bak"), "fm2make_" + stamp) : null;
                    byte[] bytes = File.ReadAllBytes(dbPath);
                    using (SqliteDatabase db = SqliteDatabase.FromBytes(bytes))
                    {
                        Fm2LookupPort lookups = new Fm2LookupPort();
                        lookups.TargetRoot = target;
                        lookups.BackupFolder = backup;
                        lookups.Begin(null, db);
                        lookups.AddMake(spec);
                        if (backup != null)
                        {
                            Directory.CreateDirectory(backup);
                            File.WriteAllBytes(Path.Combine(backup, "gamedb.slt"), bytes);
                        }
                        lookups.Commit();
                        File.WriteAllBytes(dbPath, db.ToBytes());
                        log.AddRange(lookups.Log);
                        log.Add("written: " + dbPath + ((backup != null) ? " (backup in " + backup + ")" : ""));
                    }
                }
                catch (Exception e)
                {
                    failed = e;
                }
                return delegate
                {
                    StringBuilder text = new StringBuilder();
                    text.AppendLine(((failed != null) ? "NEW MAKE FAILED: " : "NEW MAKE: ") + spec.Name + " (" + spec.Code + ", id " + spec.Id + ") in " + target);
                    if (failed != null)
                    {
                        text.AppendLine(failed.Message);
                    }
                    foreach (string line in log)
                    {
                        text.AppendLine(line);
                    }
                    this.report.Text = text.ToString();
                    this.tabs.SelectedIndex = 0;
                    if (failed != null)
                    {
                        this.status.Text = "The make was not created: " + failed.Message;
                        return;
                    }
                    this.fm2MakeList.Add(new object[] { spec.Id, spec.Name });
                    if (car != null)
                    {
                        car.MakeId = spec.Id;
                    }
                    this.ShowMake();
                    this.status.Text = "Make " + spec.Name + " (" + spec.Id + ") created in FM2" + ((car != null) ? " and chosen for " + car.Name : "") + ". A make with no car is not listed in game.";
                };
            });
        }
    }
}
