namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Windows.Forms;

    // The selected car's choices of the FM3 windows (Import and Create DLC Package), as the FM4 window's (user 2026-11-22: FM3's car
    // tables are FM4's, the FM3 windows lacked them): Also in arcade mode, Make with "+" (a new make: CarMake, FM3 layout), Engine
    // sound (an FM3 car's tuning files, else the car's own converted), FRONT / REAR brakes with the caliper angles. No Tires: FM3 has
    // no tyres per car (docs/PORT_FH_TO_FM3.md 1). The brakes are for a car without brakes of its own (FM2, traffic), which only the
    // routes through the FM4 form meet: step 1 writes the generic model and its angle (degrees in the box, radians in the row) together.
    // An FM4 car keeps its own brakes and angles (the boxes are off).
    // A new make for the pack is kept here (pendingMakes) and written into the trial folder before the cars, so the pack build finds
    // its row, strings and logos in the difference to the game; an install writes the one the car goes under.
    internal sealed partial class CarPortFm3Form
    {
        private sealed class CarChoices
        {
            public bool Arcade = true;
            public long? MakeId;                // FM3 make; null = the car's own
            public string Sound;                // FM3 car whose sound files the car takes; null = its own
            public CarPort.BrakeChoice Brakes;  // pistons for a car without brakes, angles; null = the car's own
        }

        private CheckBox arcade;
        private ComboBox makes;
        private ComboBox sounds;
        private Button newMakeButton;
        private ComboBox frontCaliper;
        private ComboBox rearCaliper;
        private TextBox frontAngle;
        private TextBox rearAngle;
        private readonly List<CarMakeSpec> pendingMakes = new List<CarMakeSpec>();

        // The controls under the template car, at the FM4 window's places.
        private void BuildChoices(Panel options)
        {
            this.arcade = new CheckBox();
            this.arcade.Text = "Also in arcade mode";
            new ToolTip().SetToolTip(this.arcade, "Sets Data_Car.IsArcade, so the car shows in FM3's arcade car list too");
            this.arcade.Checked = true;
            this.arcade.Location = new Point(484, 30);
            this.arcade.AutoSize = true;
            options.Controls.Add(this.arcade);

            Label makeLabel = new Label();
            makeLabel.Text = "Make:";
            makeLabel.Location = new Point(8, 63);
            makeLabel.AutoSize = true;
            this.makes = new ComboBox();
            this.makes.DropDownStyle = ComboBoxStyle.DropDownList;
            this.makes.Location = new Point(52, 60);
            this.makes.Width = 150;
            this.makes.DropDownWidth = 260;
            this.makes.MaxDropDownItems = 24;
            new ToolTip().SetToolTip(this.makes, "The FM3 make the car goes under; the source's own make by default (added to FM3 when FM3 lacks it)");
            this.newMakeButton = new Button();
            this.newMakeButton.Text = "+";
            this.newMakeButton.Location = new Point(206, 59);
            this.newMakeButton.Size = new Size(30, 24);
            new ToolTip().SetToolTip(this.newMakeButton, "New make: create a car make FM3 lacks, in the game's database or in the DLC pack");
            this.newMakeButton.Click += delegate { this.NewMake(); };
            Label soundLabel = new Label();
            soundLabel.Text = "Engine sound:";
            soundLabel.Location = new Point(244, 63);
            soundLabel.AutoSize = true;
            this.sounds = new ComboBox();
            this.sounds.DropDownStyle = ComboBoxStyle.DropDownList;
            this.sounds.Location = new Point(332, 60);
            this.sounds.Width = 260;
            this.sounds.MaxDropDownItems = 24;
            new ToolTip().SetToolTip(this.sounds, "The FM3 car whose engine sound files (_ET and _CMT) the car takes; by default its own sound, converted for FM3");
            options.Controls.Add(makeLabel);
            options.Controls.Add(this.makes);
            options.Controls.Add(this.newMakeButton);
            options.Controls.Add(soundLabel);
            options.Controls.Add(this.sounds);

            Label brakeLabel = new Label();
            brakeLabel.Text = "FRONT brakes (only if the car has none):";
            brakeLabel.Location = new Point(8, 99);
            brakeLabel.AutoSize = true;
            this.frontCaliper = CarPortForm.BrakeCombo(options, 268, 96, new string[] { "from the car", "2 pistons", "4 pistons", "6 pistons" });
            Label frontAngleLabel = new Label();
            frontAngleLabel.Text = "caliper angle, degrees:";
            frontAngleLabel.Location = new Point(344, 99);
            frontAngleLabel.AutoSize = true;
            this.frontAngle = CarPortForm.BrakeAngle(options, 480, 96, "");
            Label rearLabel = new Label();
            rearLabel.Text = "REAR brakes (only if the car has none):";
            rearLabel.Location = new Point(8, 127);
            rearLabel.AutoSize = true;
            this.rearCaliper = CarPortForm.BrakeCombo(options, 268, 124, new string[] { "from the car", "2 pistons", "4 pistons", "6 pistons" });
            Label rearAngleLabel = new Label();
            rearAngleLabel.Text = "caliper angle, degrees:";
            rearAngleLabel.Location = new Point(344, 127);
            rearAngleLabel.AutoSize = true;
            this.rearAngle = CarPortForm.BrakeAngle(options, 480, 124, "");
            options.Controls.Add(brakeLabel);
            options.Controls.Add(frontAngleLabel);
            options.Controls.Add(rearLabel);
            options.Controls.Add(rearAngleLabel);
        }

        // After Load Cars: FM3's makes (and the pack's new ones) and the folder's cars for the sound. throughFm4: FH1 / FH2 / FM2,
        // whose cars may have no brakes (the pistons choice); an FM4 car has its own.
        private void FillChoices(List<CarPort.Choice> makeList, List<string[]> soundCars, bool throughFm4)
        {
            this.makes.Items.Clear();
            this.makes.Items.AddRange(makeList.ToArray());
            this.AddPendingMakeChoices();
            this.makes.SelectedIndex = 0;
            List<string[]> sorted = new List<string[]>(soundCars);
            sorted.Sort(delegate(string[] a, string[] b) { return string.Compare(a[1], b[1], StringComparison.OrdinalIgnoreCase); });
            this.sounds.Items.Clear();
            this.sounds.Items.Add(new CarPort.Choice { Value = null, Label = "its own" });
            foreach (string[] car in sorted)
            {
                this.sounds.Items.Add(new CarPort.Choice { Value = car[0], Label = car[1] + "  (" + car[0] + ")" });
            }
            this.sounds.SelectedIndex = 0;
            foreach (ComboBox pistons in new ComboBox[] { this.frontCaliper, this.rearCaliper })
            {
                if (!throughFm4)
                {
                    pistons.SelectedIndex = 0;
                }
                pistons.Enabled = throughFm4;
            }
            foreach (TextBox angle in new TextBox[] { this.frontAngle, this.rearAngle })
            {
                if (!throughFm4)
                {
                    angle.Text = "";
                }
                angle.Enabled = throughFm4;   // the angle is written with the generic model (the FM4-form step); an FM4 car keeps its own
            }
            string why = throughFm4 ? "Generic brakes for a car with none of its own (FM2, traffic cars); 'from the car' = its own"
                : "An FM4 car has brakes of its own: they are kept, with their caliper angles";
            new ToolTip().SetToolTip(this.frontCaliper, why);
            new ToolTip().SetToolTip(this.rearCaliper, why);
        }

        private CarChoices CurrentChoices()
        {
            CarChoices choices = new CarChoices();
            choices.Arcade = this.arcade.Checked;
            CarPort.Choice make = this.makes.SelectedItem as CarPort.Choice;
            if (make != null && make.Value != null)
            {
                choices.MakeId = Convert.ToInt64(make.Value, CultureInfo.InvariantCulture);
            }
            CarPort.Choice sound = this.sounds.SelectedItem as CarPort.Choice;
            if (sound != null && sound.Value != null)
            {
                choices.Sound = (string) sound.Value;
            }
            CarPort.BrakeChoice brakes = new CarPort.BrakeChoice();
            brakes.FrontPistons = (this.frontCaliper.Enabled && this.frontCaliper.SelectedIndex > 0) ? this.frontCaliper.SelectedIndex * 2 : 0;
            brakes.RearPistons = (this.rearCaliper.Enabled && this.rearCaliper.SelectedIndex > 0) ? this.rearCaliper.SelectedIndex * 2 : 0;
            double angle;
            brakes.FrontAngle = (this.frontAngle.Enabled && double.TryParse(this.frontAngle.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out angle)) ? (double?) angle : null;
            brakes.RearAngle = (this.rearAngle.Enabled && double.TryParse(this.rearAngle.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out angle)) ? (double?) angle : null;
            choices.Brakes = brakes;
            return choices;
        }

        #region New make

        private void NewMake()
        {
            string target = this.targetBox.Text.Trim();
            if (this.busy || !File.Exists(CarMake.DatabasePath(target)) || GameInfo.Detect(target) != ForzaGame.Motorsport3)
            {
                return;
            }
            List<long> taken = new List<long>();
            foreach (CarMakeSpec pending in this.pendingMakes)
            {
                taken.Add(pending.Id);
            }
            NewMakeForm form;
            try
            {
                form = new NewMakeForm(target, this.XeniaContent(), taken, this.mode == PortMode.Import, this.mode == PortMode.Dlc);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            using (form)
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                CarMakeSpec spec = form.Spec;
                if (!form.InGame)
                {
                    this.pendingMakes.Add(spec);
                    this.packTrialFolder = null;
                    this.packTrialFixes = null;
                    this.AddPendingMakeChoice(spec, true);
                    this.UpdateButtons();
                    this.report.Text = string.Format(CultureInfo.InvariantCulture, "Make {0} \"{1}\" ({2}) added to the pack: it is written into the next trial, and with it into the pack. The game folder is untouched.\r\n", spec.Id, spec.Name, spec.Code);
                    this.status.Text = "New make added to the pack: choose it for a car, then Trial Pack.";
                    return;
                }
                if (target.TrimEnd('\\').EndsWith("\\FM3_Extracted", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(this, "FM3_Extracted is the clean game and is never written: use the test copy FM3_Extracted_DEV.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!XeniaGuard.Confirm(this, this.Text, "FM3's game files (the make is written into them)"))
                {
                    return;
                }
                this.report.Text = string.Format(CultureInfo.InvariantCulture, "Creating make {0} \"{1}\" in {2}...\r\n", spec.Id, spec.Name, target);
                this.Background(delegate
                {
                    StringBuilder text = new StringBuilder();
                    Exception error = null;
                    try
                    {
                        CarMake.Create(spec, target, null, Path.Combine(Application.StartupPath, "texconv.exe"), text);
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    List<CarPort.Choice> list = CarPortForm.Makes(target);
                    return delegate
                    {
                        this.report.Text += text.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
                        if (error != null)
                        {
                            this.report.Text += "\r\nFAILED: " + error.Message + "\r\n";
                            this.status.Text = "The make was not created (see the report).";
                            return;
                        }
                        this.RefreshMakeChoices(list, spec.Id);
                        this.status.Text = string.Format("Make \"{0}\" is in the FM3 folder. Put a car under it with the Make choice.", spec.Name);
                    };
                });
            }
        }

        private void AddPendingMakeChoice(CarMakeSpec spec, bool select)
        {
            CarPort.Choice choice = new CarPort.Choice { Value = spec.Id, Label = spec.Name + "  (" + spec.Code + ")  - new, for the pack", Recommended = true };
            this.makes.Items.Add(choice);
            if (select)
            {
                this.makes.SelectedItem = choice;
            }
        }

        private void AddPendingMakeChoices()
        {
            foreach (CarMakeSpec spec in this.pendingMakes)
            {
                this.AddPendingMakeChoice(spec, false);
            }
        }

        private void RefreshMakeChoices(List<CarPort.Choice> list, long selectId)
        {
            this.makes.Items.Clear();
            this.makes.Items.AddRange(list.ToArray());
            this.AddPendingMakeChoices();
            this.makes.SelectedIndex = 0;
            foreach (object item in this.makes.Items)
            {
                CarPort.Choice choice = item as CarPort.Choice;
                if (choice != null && choice.Value is long && (long) choice.Value == selectId)
                {
                    this.makes.SelectedItem = item;
                }
            }
        }

        // The new makes a run writes into its folder first: all of them for a trial, for an install only the one the car goes under.
        private List<CarMakeSpec> MakesToWrite(bool installing, long? makeId)
        {
            List<CarMakeSpec> list = new List<CarMakeSpec>();
            foreach (CarMakeSpec spec in this.pendingMakes)
            {
                if (!installing || (makeId.HasValue && spec.Id == makeId.Value))
                {
                    list.Add(spec);
                }
            }
            return list;
        }

        // An install put these makes into the game: they are no longer pending.
        private void MakesInstalled(List<CarMakeSpec> installed)
        {
            foreach (CarMakeSpec spec in installed)
            {
                this.pendingMakes.Remove(spec);
            }
        }

        private static void WriteMakes(List<CarMakeSpec> specs, string target, string folder, StringBuilder text)
        {
            string texconv = Path.Combine(Application.StartupPath, "texconv.exe");
            foreach (CarMakeSpec spec in specs)
            {
                text.AppendLine().AppendLine("=== make " + spec.Name + " (" + spec.Code + ")");
                CarMake.Create(spec, target, folder, texconv, text);
            }
        }

        #endregion
    }
}
