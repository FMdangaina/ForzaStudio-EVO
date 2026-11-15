namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Windows.Forms;

    // New makes (CarMake) from the port window: the "+" next to the Make choice. A make created in the
    // game is in the FM4 database at once. A make added to the pack is kept here (pendingMakes) and
    // written into the trial folder before the cars of every trial, so the pack build finds the new row,
    // strings and logos in the difference to the game; an install in the FM4 folder writes it there
    // first when the car goes under it.
    internal partial class CarPortForm
    {
        private readonly List<CarMakeSpec> pendingMakes = new List<CarMakeSpec>();
        private Button newMakeButton;

        private Button BuildNewMakeButton()
        {
            this.newMakeButton = new Button();
            this.newMakeButton.Text = "+";
            this.newMakeButton.Location = new Point(206, 59);
            this.newMakeButton.Size = new Size(30, 24);
            new ToolTip().SetToolTip(this.newMakeButton, "New make: create a car make FM4 lacks, in the game's database or in the DLC pack");
            this.newMakeButton.Click += delegate { this.NewMake(); };
            return this.newMakeButton;
        }

        private void NewMake()
        {
            string target = this.targetBox.Text.Trim();
            if (this.busy || !File.Exists(CarMake.DatabasePath(target)))
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
                    this.AddPendingMakeChoice(spec, true);
                    this.UpdateButtons();
                    this.bottomTabs.SelectedIndex = 0;
                    this.report.Text = string.Format("Make {0} \"{1}\" ({2}) added to the pack: it is written into the next trial, and with it into the pack. The game folder is untouched.\r\n", spec.Id, spec.Name, spec.Code);
                    return;
                }
                if (!XeniaGuard.Confirm(this, this.Text, "the game's files (the make is written into them)"))
                {
                    return;
                }
                if (this.MainWindowBusy())
                {
                    return;
                }
                this.bottomTabs.SelectedIndex = 0;
                this.report.Text = string.Format("Creating make {0} \"{1}\" in {2}...\r\n", spec.Id, spec.Name, target);
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
                    return delegate
                    {
                        this.report.Text += text.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
                        if (error != null)
                        {
                            this.report.Text += "\r\nFAILED: " + error.Message + "\r\n";
                            this.details.Text = "The make was not created (see the report).";
                            return;
                        }
                        this.OnGameChanged();
                        this.RefreshMakeChoices(spec.Id);
                        this.details.Text = string.Format("Make \"{0}\" is in the FM4 folder. Put a car under it with the Make choice.", spec.Name);
                    };
                });
            }
        }

        private static string MakeLabel(CarMakeSpec spec)
        {
            return spec.Name + "  (" + spec.Code + ")  - new, for the pack";
        }

        private void AddPendingMakeChoice(CarMakeSpec spec, bool select)
        {
            CarPort.Choice choice = new CarPort.Choice { Value = spec.Id, Label = MakeLabel(spec), Recommended = true };
            this.makes.Items.Add(choice);
            if (select)
            {
                this.makes.SelectedItem = choice;
            }
        }

        // After the source/target is read again: the pack's pending makes go back in the list.
        private void AddPendingMakeChoices()
        {
            foreach (CarMakeSpec spec in this.pendingMakes)
            {
                this.AddPendingMakeChoice(spec, false);
            }
        }

        // The list read again from the FM4 database (a make was created in the game); selectId is chosen.
        private void RefreshMakeChoices(long selectId)
        {
            List<CarPort.Choice> list = Makes(this.targetBox.Text.Trim());
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

        // The pending makes the folder does not have yet, written into it; text is the report.
        private static void WriteMakes(List<CarMakeSpec> specs, string target, string trialFolder, StringBuilder text)
        {
            string texconv = Path.Combine(Application.StartupPath, "texconv.exe");
            foreach (CarMakeSpec spec in specs)
            {
                text.AppendLine().AppendLine("=== make " + spec.Name + " (" + spec.Code + ")");
                CarMake.Create(spec, target, trialFolder, texconv, text);
            }
        }

        // The pending make a car installed under makeId goes under (null: none), taken out of the pack's
        // pending list once the install wrote it into the game.
        private CarMakeSpec PendingMake(long? makeId)
        {
            if (!makeId.HasValue)
            {
                return null;
            }
            foreach (CarMakeSpec spec in this.pendingMakes)
            {
                if (spec.Id == makeId.Value)
                {
                    return spec;
                }
            }
            return null;
        }
    }
}
