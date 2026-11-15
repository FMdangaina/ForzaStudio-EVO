namespace ForzaStudio
{
    using System;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Windows.Forms;

    // The gearbox of a duplicated car (Duplicate Car, TODO D-10): how many forward gears, the ratio of each, the
    // reverse and the final drive. Starts from the source car's stock gearbox; Result is the GearboxSpec that
    // CarDuplicate hands to CarGearbox.Clone.
    internal sealed class GearboxDialog : Form
    {
        private readonly NumericUpDown count;
        private readonly NumericUpDown finalDrive;
        private readonly NumericUpDown reverse;
        private readonly NumericUpDown[] gears = new NumericUpDown[8];
        private readonly Label[] overall = new Label[8];

        public GearboxSpec Result;

        // database: the game's database with the source's package rows; null = the game's
        public GearboxDialog(string target, long carId, GearboxSpec initial, byte[] database = null)
        {
            this.AutoScaleMode = AutoScaleMode.None;
            this.Text = "Gearbox of the copy";
            this.ClientSize = new Size(520, 430);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;

            // FM2 has no drivetrain table: the gearbox is the car's own List_UpgradeTransmission rows, 7 forward gears at most.
            bool fm2 = GameInfo.Detect(target) == ForzaGame.Motorsport2;
            int most = fm2 ? 7 : 8;
            GearboxSpec start = initial;
            if (start == null)
            {
                using (SqliteDatabase db = SqliteDatabase.FromBytes(database ?? File.ReadAllBytes(CarMake.DatabasePath(target))))
                {
                    if (fm2)
                    {
                        start = CarGearbox.ReadFm2(db, carId);
                    }
                    else
                    {
                        long drivetrain = CarGearbox.StockDrivetrain(db, carId);
                        start = (drivetrain < 0) ? null : CarGearbox.Read(db, drivetrain);
                    }
                }
            }
            if (start == null)
            {
                throw new InvalidOperationException("The car has no stock gearbox.");
            }
            if (fm2)
            {
                this.Text = "Gearbox of the copy (FM2: the copy's own transmission levels, 7 gears at most)";
            }

            Label hint = new Label();
            hint.SetBounds(12, 8, 496, 50);
            hint.Text = "A copy of the source car's drivetrain with the gearbox below (stock level; the upgrade levels keep their shape). Choose the number of forward gears, then a ratio for each: 1st gear is the highest number. The last column is the overall ratio (gear x final drive).";
            this.Controls.Add(hint);

            this.count = this.Number(12, 74, 0, 1, most, 0, "Forward gears");
            this.finalDrive = this.Number(180, 74, 3, 0.5, 9.999, 3, "Final drive");
            this.reverse = this.Number(348, 74, 3, 0.5, 9.999, 3, "Reverse (as a positive number)");

            for (int i = 0; i < 8; i++)
            {
                this.gears[i] = this.Number(12, 140 + (i * 30), 3, 0.1, 9.999, 3, null);
                Label name = new Label();
                name.Text = "Gear " + (i + 1);
                name.SetBounds(130, 143 + (i * 30), 60, 20);
                this.Controls.Add(name);
                this.overall[i] = new Label();
                this.overall[i].SetBounds(200, 143 + (i * 30), 120, 20);
                this.Controls.Add(this.overall[i]);
                this.gears[i].ValueChanged += delegate { this.Refresh2(); };
            }
            this.finalDrive.ValueChanged += delegate { this.Refresh2(); };
            this.count.ValueChanged += delegate { this.Refresh2(); };

            this.count.Value = Math.Max(1, Math.Min(most, start.Forward.Length));
            this.finalDrive.Value = (decimal) start.FinalDrive;
            this.reverse.Value = (decimal) Math.Abs(start.Reverse);
            for (int i = 0; i < 8; i++)
            {
                this.gears[i].Value = (i < start.Forward.Length) ? (decimal) start.Forward[i] : (decimal) (start.Forward[start.Forward.Length - 1] * Math.Pow(0.85, i - start.Forward.Length + 1));
            }
            this.Refresh2();

            Button ok = new Button();
            ok.Text = "OK";
            ok.SetBounds(336, 390, 80, 28);
            ok.Click += delegate { this.Accept(); };
            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(428, 390, 80, 28);
            this.Controls.Add(ok);
            this.Controls.Add(cancel);
            this.CancelButton = cancel;
            this.AcceptButton = ok;
            ForzaTheme.Apply(this);
        }

        private NumericUpDown Number(int x, int y, int decimals, double minimum, double maximum, int places, string caption)
        {
            if (caption != null)
            {
                Label label = new Label();
                label.Text = caption;
                label.SetBounds(x, y - 18, 160, 18);
                this.Controls.Add(label);
            }
            NumericUpDown number = new NumericUpDown();
            number.DecimalPlaces = places;
            number.Minimum = (decimal) minimum;
            number.Maximum = (decimal) maximum;
            number.Increment = (places == 0) ? 1 : 0.01m;
            number.SetBounds(x, y, 100, 24);
            this.Controls.Add(number);
            return number;
        }

        // Only the gears that exist can be edited; the overall ratio shows next to each.
        private void Refresh2()
        {
            int n = (int) this.count.Value;
            for (int i = 0; i < 8; i++)
            {
                this.gears[i].Enabled = i < n;
                this.overall[i].Text = (i < n) ? ((double) this.gears[i].Value * (double) this.finalDrive.Value).ToString("0.00", CultureInfo.InvariantCulture) : "";
            }
        }

        private void Accept()
        {
            int n = (int) this.count.Value;
            GearboxSpec spec = new GearboxSpec();
            spec.Forward = new double[n];
            for (int i = 0; i < n; i++)
            {
                spec.Forward[i] = (double) this.gears[i].Value;
                if (i > 0 && spec.Forward[i] >= spec.Forward[i - 1])
                {
                    MessageBox.Show(this, "Gear " + (i + 1) + " must be lower than gear " + i + " (1st gear is the highest ratio).", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            spec.FinalDrive = (double) this.finalDrive.Value;
            spec.Reverse = -(double) this.reverse.Value;
            this.Result = spec;
            this.DialogResult = DialogResult.OK;
        }
    }
}
