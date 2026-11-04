namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Windows.Forms;

    // The engine of a duplicated car (Duplicate Car, ROADMAP 2.3.2): name, sound, layout, cylinders, aspiration,
    // boost, compression, mass, displacement, idle and redline rpm, torque, and the shape of the torque curve,
    // drawn with the mouse on the power graph. Starts from the source car's stock engine; Result is the
    // EngineSpec (only what differs) that CarDuplicate hands to CarEngine.Clone.
    internal sealed class EngineDialog : Form
    {
        private sealed class Choice
        {
            public long Id;
            public string Media;
            public string Text;
            public string Code;     // layout letter / cylinder count, for the engine name

            public override string ToString()
            {
                return this.Text;
            }
        }

        private readonly StringTable names;
        private readonly TextBox name;
        private readonly ComboBox sound;
        private readonly ComboBox layout;
        private readonly ComboBox cylinders;
        private readonly ComboBox aspiration;
        private readonly NumericUpDown boost;
        private readonly NumericUpDown compression;
        private readonly NumericUpDown mass;
        private readonly NumericUpDown displacement;
        private readonly NumericUpDown idle;
        private readonly NumericUpDown redline;
        private readonly string target;
        private readonly NumericUpDown turboVolume;
        private readonly ComboBox bov;
        private readonly CheckBox diesel;
        private readonly CheckBox carbureted;
        private readonly CheckBox rotary;
        private readonly PowerGraph graph;
        private readonly DataGridView table;
        private bool refreshing;
        private EnginePower current = new EnginePower();   // the curve the graph and the table show
        private readonly Dictionary<string, object> source = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private double[] stockBase = new double[0];     // the source's stock curve in base N.m (its scale times its values), one per 100 rpm from 0
        private double[] shape = new double[0];         // the curve being designed, normalised to stockScale, one value per 100 rpm
        private Label pointsNote;
        private double stockScale;                      // List_TorqueCurve.TorqueScale of the stock curve
        private double stockFactor = 1;                // what the game multiplies the stock curve by: the stock parts' TorqueScale (CarEngine.StockMultipliers)
        private double stockBoost = 1;                 // ... and the stock turbo's MaxScale (counts only for a boosted engine)
        private double stockRedline;
        private double stockDisplacement;
        private double stockIdle;
        private bool filling;
        private bool nameAuto = true;                  // the name follows layout, cylinders and aspiration until it is typed over
        private string nameTail = "";                  // what follows the engine code in the source name (the car)

        public EngineSpec Result;

        // initial = what was designed before (the wizard reopens the dialog with it), or null for the source's stock engine.
        public EngineDialog(string target, StringTable names, long carId, EngineSpec initial)
        {
            this.names = names;
            this.target = target;
            this.AutoScaleMode = AutoScaleMode.None;
            this.Text = "Engine of the copy";
            this.ClientSize = new Size(780, 740);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;

            List<Choice> layouts = new List<Choice>();
            List<Choice> cylinderChoices = new List<Choice>();
            List<Choice> aspirations = new List<Choice>();
            List<Choice> sounds = new List<Choice>();
            using (SqliteDatabase db = SqliteDatabase.FromBytes(File.ReadAllBytes(CarMake.DatabasePath(target))))
            {
                foreach (object[] row in db.Query("SELECT ConfigID, EngineConfig, DisplayName FROM List_EngineConfig ORDER BY ConfigID").Rows)
                {
                    layouts.Add(this.Coded(this.Item(row[0], this.Lookup("List_EngineConfig", "DisplayName", row[2] as string) ?? (row[1] as string), null), row[1]));
                }
                foreach (object[] row in db.Query("SELECT CylinderID, Number, Description, Rotors FROM List_Cylinders ORDER BY CylinderID").Rows)
                {
                    string shown = this.Lookup("List_Cylinders", "Description", row[2] as string);
                    bool rotors = Convert.ToInt64(row[3], CultureInfo.InvariantCulture) > 0;
                    cylinderChoices.Add(this.Coded(this.Item(row[0], string.IsNullOrEmpty(shown) ? (Convert.ToString(row[1], CultureInfo.InvariantCulture) + (rotors ? " rotors" : " cylinders")) : shown, null), row[1]));
                }
                foreach (object[] row in db.Query("SELECT AspirationID, Aspiration, DisplayName FROM List_Aspiration ORDER BY AspirationID").Rows)
                {
                    aspirations.Add(this.Item(row[0], this.Lookup("List_Aspiration", "DisplayName", row[2] as string) ?? (row[1] as string), null));
                }
                foreach (object[] row in db.Query("SELECT EngineID, MediaName, EngineName FROM Data_Engine ORDER BY EngineName").Rows)
                {
                    sounds.Add(this.Item(row[0], Convert.ToString(row[2], CultureInfo.InvariantCulture).Replace("   ", " ") + "   [" + row[1] + "]", row[1] as string));
                }
                this.ReadSource(db, carId);
            }

            Label hint = new Label();
            hint.SetBounds(12, 8, 756, 34);
            hint.Text = "A copy of the source car's stock engine with the values below. The upgrade levels follow the displacement, the redline and the torque; drag on the graph to draw the torque curve.";
            this.Controls.Add(hint);
            this.name = new TextBox();
            this.Field(0, "Name:", 0, this.name, 420);
            this.sound = new ComboBox();
            this.sound.DropDownStyle = ComboBoxStyle.DropDownList;
            this.sound.MaxDropDownItems = 24;
            Choice own = new Choice();
            own.Text = "(the source engine's sound)";
            this.sound.Items.Add(own);
            this.sound.Items.AddRange(sounds.ToArray());
            this.sound.SelectedIndex = 0;
            this.Field(1, "Sound:", 0, this.sound, 560);
            this.layout = this.Combo(2, "Layout:", 0, layouts);
            this.cylinders = this.Combo(2, "Cylinders:", 1, cylinderChoices);
            this.aspiration = this.Combo(3, "Aspiration:", 0, aspirations);
            this.boost = this.Number(3, "Turbo boost (bar):", 1, 0, 6, 2, 0.05m);
            this.compression = this.Number(4, "Compression:", 0, 4, 20, 1, 0.1m);
            this.mass = this.Number(4, "Mass (kg):", 1, 20, 1500, 0, 5m);
            this.displacement = this.Number(5, "Displacement (cc):", 0, 0, 20000, 0, 50m);
            this.idle = this.Number(6, "Idle (rpm):", 0, 100, 3000, 0, 50m);
            this.redline = this.Number(6, "Redline (rpm):", 1, 1000, 20000, 0, 100m);
            this.diesel = new CheckBox();
            this.diesel.Text = "Diesel";
            this.diesel.SetBounds(124, 12 + (7 * 30), 90, 22);
            this.carbureted = new CheckBox();
            this.carbureted.Text = "Carburettor";
            this.carbureted.SetBounds(224, 12 + (7 * 30), 110, 22);
            this.rotary = new CheckBox();
            this.rotary.Text = "Rotary";
            this.rotary.SetBounds(344, 12 + (7 * 30), 90, 22);
            this.Controls.Add(this.diesel);
            this.Controls.Add(this.carbureted);
            this.Controls.Add(this.rotary);

            // The turbo's sound: the engine tuning file's TurboVolume (the game has one whistle, four loops in Turbos\turbos.fsb) and the
            // blow-off sample (turbos.fev: L1_BlowOff_1..3, L2_BlowOff_1..3, L3_BlowOff_1..5), used for a turbocharged engine.
            this.turboVolume = this.Number(8, "Turbo volume:", 0, 0, 1.5m, 2, 0.05m);
            this.bov = new ComboBox();
            this.bov.DropDownStyle = ComboBoxStyle.DropDownList;
            Choice bovOwn = new Choice();
            bovOwn.Text = "(the sound engine's own)";
            this.bov.Items.Add(bovOwn);
            int[] bovCounts = { 3, 3, 5 };
            for (int level = 1; level <= 3; level++)
            {
                for (int choice = 1; choice <= bovCounts[level - 1]; choice++)
                {
                    Choice sample = new Choice();
                    sample.Id = (level * 10) + choice;
                    sample.Text = "L" + level + "  blow-off " + choice;
                    this.bov.Items.Add(sample);
                }
            }
            this.bov.SelectedIndex = 0;
            this.Field(8, "BOV sound:", 1, this.bov, 150);
            this.turboVolume.Value = 0;
            this.PreviewButton("Whistle", 240, 70, delegate { return "turbo1_loop7"; });
            this.PreviewButton("HI", 660, 48, delegate { return this.BovSample("HI"); });
            this.PreviewButton("MED", 712, 56, delegate { return this.BovSample("MED"); });
            Label turboNote = new Label();
            turboNote.SetBounds(124, 12 + (8 * 30) + 26, 600, 16);
            turboNote.Text = "Turbo volume 0 = the sound engine's own (0.5 if it has none). FM4's turbo engines use 0.2 - 0.9. Turbocharged engines only.";
            this.Controls.Add(turboNote);

            this.graph = new PowerGraph();
            this.graph.Editable = true;
            this.graph.SetBounds(12, 298, 506, 360);
            this.graph.CurveEdited += delegate
            {
                double[] edited = this.graph.EditedCurve;
                if (edited != null && edited.Length == this.shape.Length)
                {
                    this.shape = (double[]) edited.Clone();
                }
                this.Rebuild();
            };
            this.Controls.Add(this.graph);

            // The same curve as numbers, one row per point: the torque column can be typed in (or a column pasted).
            this.table = new DataGridView();
            this.table.SetBounds(526, 298, 242, 360);
            this.table.AllowUserToAddRows = false;
            this.table.AllowUserToDeleteRows = false;
            this.table.AllowUserToResizeRows = false;
            this.table.RowHeadersVisible = false;
            this.table.Columns.Add("rpm", "rpm");
            this.table.Columns.Add("torque", "N.m");
            this.table.Columns.Add("power", "CV");
            this.table.Columns[0].ReadOnly = true;
            this.table.Columns[2].ReadOnly = true;
            foreach (DataGridViewColumn column in this.table.Columns)
            {
                column.Width = 68;
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
            this.table.CellValueChanged += delegate(object sender, DataGridViewCellEventArgs e) { this.TableEdited(e.RowIndex, e.ColumnIndex); };
            this.Controls.Add(this.table);
            Button reset = new Button();
            reset.Text = "Reset curve";
            reset.SetBounds(12, 668, 110, 28);
            reset.Click += delegate
            {
                this.shape = this.StockCurve(this.Points());
                this.Rebuild();
            };
            this.Controls.Add(reset);
            Button generate = new Button();
            generate.Text = "Generate curve...";
            generate.SetBounds(12, 700, 110, 28);
            generate.Click += delegate { this.GenerateCurve(); };
            this.Controls.Add(generate);
            Label note = new Label();
            note.SetBounds(130, 664, 380, 40);
            note.Text = "Drag on the graph, or type the N.m of a row of the table: one row per 100 rpm, as FM4 stores it (the number of values follows the redline + 300 rpm diesel / 500 petrol).. Rows past the redline are grey. Idle is the stall rpm; the sound is the FM4 engine you pick.";
            this.Controls.Add(note);
            this.pointsNote = new Label();
            this.pointsNote.SetBounds(130, 706, 380, 28);
            this.Controls.Add(this.pointsNote);
            Button ok = new Button();
            ok.Text = "OK";
            ok.SetBounds(596, 686, 80, 28);
            ok.Click += delegate { this.Accept(); };
            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(688, 686, 80, 28);
            this.Controls.Add(ok);
            this.Controls.Add(cancel);
            this.CancelButton = cancel;
            this.AcceptButton = ok;

            this.Fill();
            if (initial != null)
            {
                this.LoadSpec(initial);
            }
            this.redline.ValueChanged += delegate { this.Rebuild(); };
            this.aspiration.SelectedIndexChanged += delegate { this.Rebuild(); this.SuggestName(); this.TurboControls(); };
            this.TurboControls();
            this.layout.SelectedIndexChanged += delegate { this.SuggestName(); };
            this.cylinders.SelectedIndexChanged += delegate { this.SuggestName(); };
            this.name.TextChanged += delegate { if (!this.filling) { this.nameAuto = false; } };
            this.boost.ValueChanged += delegate { this.Rebuild(); };
            this.diesel.CheckedChanged += delegate { this.Rebuild(); this.SuggestName(); };
            ForzaTheme.Apply(this);
        }

        private delegate string SamplePrefix();

        // A small play button of the turbo sound row: it listens to the sample of turbos.fsb whose name starts with what the
        // callback gives (null = nothing chosen). The game folder is the one the engine is made in.
        private void PreviewButton(string text, int x, int width, SamplePrefix prefix)
        {
            Button play = new Button();
            play.Text = "▶ " + text;
            play.SetBounds(x, 12 + (8 * 30) - 1, width, 26);
            play.Click += delegate
            {
                string wanted = prefix();
                if (wanted == null)
                {
                    MessageBox.Show(this, "Pick a BOV sound first (the sound engine's own is not a single sample).", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                try
                {
                    TurboPreview.Play(this.target, wanted);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            this.Controls.Add(play);
        }

        // "BlowOff_L<level>_<choice>_<HI|MED>" of the BOV chosen, or null.
        private string BovSample(string loudness)
        {
            Choice sample = this.bov.SelectedItem as Choice;
            return (sample == null || sample.Id <= 0) ? null : "BlowOff_L" + (sample.Id / 10) + "_" + (sample.Id % 10) + "_" + loudness;
        }

        // The turbo sound controls are for a turbocharged engine (single, twin, quad).
        private void TurboControls()
        {
            Choice picked = this.aspiration.SelectedItem as Choice;
            bool turbo = picked != null && picked.Id >= 2 && picked.Id <= 4;
            this.turboVolume.Enabled = turbo;
            this.bov.Enabled = turbo;
        }

        private string Lookup(string table, string column, string value)
        {
            return (this.names == null || value == null) ? null : this.names.Lookup(table, column, value);
        }

        // The engine name in FM4's form: layout letter + cylinders + aspiration (V6, I4T, V12TT, V8DSC), a dash, the car.
        // It follows the three choices until the name is typed over; the user can then write what they like.
        private void SuggestName()
        {
            Choice layoutChoice = this.layout.SelectedItem as Choice;
            Choice cylinderChoice = this.cylinders.SelectedItem as Choice;
            Choice aspirationChoice = this.aspiration.SelectedItem as Choice;
            if (!this.nameAuto || this.filling || layoutChoice == null || cylinderChoice == null || aspirationChoice == null)
            {
                return;
            }
            string layoutCode = string.Equals(layoutChoice.Code, "Rotary", StringComparison.OrdinalIgnoreCase) ? "R" : layoutChoice.Code;
            string suffix = (aspirationChoice.Id == 2) ? "T" : (aspirationChoice.Id == 3) ? "TT" : (aspirationChoice.Id == 4) ? "QT" : (aspirationChoice.Id == 5) ? "DSC" : (aspirationChoice.Id == 6) ? "CSC" : "";
            bool was = this.filling;
            this.filling = true;
            this.name.Text = layoutCode + cylinderChoice.Code + suffix + "   -   " + this.nameTail;
            this.filling = was;
        }

        private Choice Coded(Choice choice, object code)
        {
            choice.Code = Convert.ToString(code, CultureInfo.InvariantCulture);
            return choice;
        }

        private Choice Item(object id, string text, string media)
        {
            Choice item = new Choice();
            item.Id = Convert.ToInt64(id, CultureInfo.InvariantCulture);
            item.Media = media;
            item.Text = (media == null ? item.Id + "  " : "") + text;
            return item;
        }

        private void Field(int row, string text, int column, Control control, int width)
        {
            Label label = new Label();
            label.Text = text;
            label.SetBounds(12 + (column * 380), 15 + (row * 30), 108, 20);
            control.SetBounds(124 + (column * 380), 12 + (row * 30), width, 24);
            this.Controls.Add(label);
            this.Controls.Add(control);
        }

        private ComboBox Combo(int row, string text, int column, List<Choice> items)
        {
            ComboBox combo = new ComboBox();
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.Items.AddRange(items.ToArray());
            this.Field(row, text, column, combo, 230);
            return combo;
        }

        private NumericUpDown Number(int row, string text, int column, decimal min, decimal max, int decimals, decimal step)
        {
            NumericUpDown number = new NumericUpDown();
            number.Minimum = min;
            number.Maximum = max;
            number.DecimalPlaces = decimals;
            number.Increment = step;
            this.Field(row, text, column, number, 110);
            return number;
        }

        private static void Select(ComboBox combo, long id)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                Choice item = combo.Items[i] as Choice;
                if (item != null && item.Id == id && item.Media == null)
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            combo.SelectedIndex = -1;
        }

        private static void Set(NumericUpDown number, double value)
        {
            number.Value = Math.Max(number.Minimum, Math.Min(number.Maximum, (decimal) value));
        }

        private static double Real(object value)
        {
            return (value == null || value is DBNull) ? 0 : Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        // The source car's stock engine, its stock displacement, redline and torque curve.
        private void ReadSource(SqliteDatabase db, long carId)
        {
            long engine = CarEngine.StockEngine(db, carId);
            if (engine < 0)
            {
                throw new InvalidOperationException("The car has no stock engine.");
            }
            CarEngine.StockMultipliers(db, engine, out this.stockFactor, out this.stockBoost);
            SqliteDatabase.Result row = db.Query("SELECT * FROM Data_Engine WHERE EngineID = ?", engine);
            for (int i = 0; i < row.Columns.Length; i++)
            {
                this.source[row.Columns[i]] = row.Rows[0][i];
            }
            if (DbIds.HasTable(db, "List_UpgradeEngineDisplacement"))
            {
                this.stockDisplacement = Real(db.Scalar("SELECT Disp FROM List_UpgradeEngineDisplacement WHERE EngineID = ? AND IsStock = 1 ORDER BY Level LIMIT 1", engine));
            }
            if (DbIds.HasTable(db, "List_UpgradeEngineCamshaft"))
            {
                SqliteDatabase.Result cam = db.Query("SELECT RedlineRPM, TorqueCurveMaxRPM, StallRPM, TorqueCurveFullThrottleID FROM List_UpgradeEngineCamshaft WHERE EngineID = ? AND IsStock = 1 ORDER BY Level LIMIT 1", engine);
                if (cam.Rows.Count > 0)
                {
                    this.stockRedline = Real(cam.Rows[0][0]);
                    this.stockIdle = Real(cam.Rows[0][2]);
                    double maxRpm = Real(cam.Rows[0][1]);
                    SqliteDatabase.Result curve = db.Query("SELECT * FROM List_TorqueCurve WHERE TorqueCurveID = ?", Convert.ToInt64(cam.Rows[0][3], CultureInfo.InvariantCulture));
                    if (curve.Rows.Count > 0)
                    {
                        // The listed values end with FM4's marker (-2.63) at the curve's max rpm: it is not part of the curve.
                        object[] values = curve.Rows[0];
                        this.stockScale = Real(values[1]);
                        int listed = Math.Min((int) Real(values[2]), values.Length - 4);
                        int real = Math.Max(1, listed - 1);
                        double step = (listed > 1 && maxRpm > 0) ? maxRpm / (listed - 1) : 100.0;
                        int grid = (int) Math.Floor((real - 1) * step / 100.0) + 1;
                        this.stockBase = new double[grid];
                        for (int i = 0; i < grid; i++)
                        {
                            double p = i * 100.0 / step;
                            int j = Math.Min(real - 1, (int) p);
                            double next = Real(values[3 + Math.Min(real - 1, j + 1)]);
                            this.stockBase[i] = (Real(values[3 + j]) + ((next - Real(values[3 + j])) * (p - j))) * this.stockScale;
                        }
                    }
                }
            }
        }

        private void Fill()
        {
            this.filling = true;
            this.name.Text = Convert.ToString(this.source["EngineName"], CultureInfo.InvariantCulture);
            int dash = this.name.Text.IndexOf('-');
            this.nameTail = (dash >= 0) ? this.name.Text.Substring(dash + 1).Trim() : this.name.Text.Trim();
            Select(this.layout, Convert.ToInt64(this.source["ConfigID"], CultureInfo.InvariantCulture));
            Select(this.cylinders, Convert.ToInt64(this.source["CylinderID"], CultureInfo.InvariantCulture));
            Select(this.aspiration, Convert.ToInt64(this.source["AspirationID_Stock"], CultureInfo.InvariantCulture));
            Set(this.boost, Real(this.source["StockBoost-bar"]));
            Set(this.compression, Real(this.source["Compression"]));
            Set(this.mass, Real(this.source["EngineMass-kg"]));
            Set(this.displacement, this.stockDisplacement);
            Set(this.idle, this.stockIdle);
            Set(this.redline, this.stockRedline);
            this.diesel.Checked = Convert.ToInt64(this.source["Diesel"], CultureInfo.InvariantCulture) != 0;
            this.carbureted.Checked = Convert.ToInt64(this.source["Carbureted"], CultureInfo.InvariantCulture) != 0;
            this.rotary.Checked = Convert.ToInt64(this.source["Rotary"], CultureInfo.InvariantCulture) != 0;
            this.shape = this.StockCurve(this.Points());
            this.filling = false;
            this.Rebuild();
        }

        // The numbers shown are what the game shows for the car (its torque and power screen = the stock curve times the stock parts'
        // scale and, for a boosted engine, the turbo's MaxScale), so a torque typed or pasted here is the torque seen in game.
        private double Multiplier()
        {
            Choice picked = this.aspiration.SelectedItem as Choice;
            return this.stockFactor * ((picked != null) ? CarEngine.BoostGain(picked.Id, (double) this.boost.Value) : 1.0);
        }

        // The controls as an earlier Accept left them: the source's values with the spec's changes on top.
        private void LoadSpec(EngineSpec spec)
        {
            this.filling = true;
            if (spec.Name != null)
            {
                this.name.Text = spec.Name;
                this.nameAuto = false;
            }
            object value;
            if (spec.Data.TryGetValue("MediaName", out value))
            {
                for (int i = 0; i < this.sound.Items.Count; i++)
                {
                    Choice item = this.sound.Items[i] as Choice;
                    if (item != null && item.Media != null && item.Media == Convert.ToString(value, CultureInfo.InvariantCulture))
                    {
                        this.sound.SelectedIndex = i;
                    }
                }
            }
            if (spec.Data.TryGetValue("ConfigID", out value))
            {
                Select(this.layout, Convert.ToInt64(value, CultureInfo.InvariantCulture));
            }
            if (spec.Data.TryGetValue("CylinderID", out value))
            {
                Select(this.cylinders, Convert.ToInt64(value, CultureInfo.InvariantCulture));
            }
            if (spec.Data.TryGetValue("AspirationID_Stock", out value))
            {
                Select(this.aspiration, Convert.ToInt64(value, CultureInfo.InvariantCulture));
            }
            if (spec.Data.TryGetValue("StockBoost-bar", out value))
            {
                Set(this.boost, Real(value));
            }
            if (spec.Data.TryGetValue("Compression", out value))
            {
                Set(this.compression, Real(value));
            }
            if (spec.Data.TryGetValue("EngineMass-kg", out value))
            {
                Set(this.mass, Real(value));
            }
            if (spec.Data.TryGetValue("Diesel", out value))
            {
                this.diesel.Checked = Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0;
            }
            if (spec.Data.TryGetValue("Carbureted", out value))
            {
                this.carbureted.Checked = Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0;
            }
            if (spec.Data.TryGetValue("Rotary", out value))
            {
                this.rotary.Checked = Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0;
            }
            if (spec.DisplacementCc.HasValue)
            {
                Set(this.displacement, spec.DisplacementCc.Value);
            }
            if (spec.IdleRpm.HasValue)
            {
                Set(this.idle, spec.IdleRpm.Value);
            }
            if (spec.RedlineRpm.HasValue)
            {
                Set(this.redline, spec.RedlineRpm.Value);
            }
            if (spec.TurboVolume.HasValue)
            {
                Set(this.turboVolume, spec.TurboVolume.Value);
            }
            if (spec.BovLevel > 0)
            {
                for (int i = 1; i < this.bov.Items.Count; i++)
                {
                    Choice sample = this.bov.Items[i] as Choice;
                    if (sample != null && sample.Id == (spec.BovLevel * 10) + spec.BovChoice)
                    {
                        this.bov.SelectedIndex = i;
                    }
                }
            }
            if (spec.Curve != null && this.stockScale > 0)
            {
                this.shape = new double[spec.Curve.Length];
                for (int i = 0; i < this.shape.Length; i++)
                {
                    this.shape[i] = spec.Curve[i] / this.stockScale;
                }
            }
            this.filling = false;
            this.Rebuild();
        }

        // The curve is one point per 100 rpm from 0 up to the redline + a margin (300 rpm diesel, 500 petrol), as in FM4's
        // List_TorqueCurve: the number of values follows the redline.
        private double Margin()
        {
            return this.diesel.Checked ? 300.0 : 500.0;
        }

        private int Points()
        {
            return (int) Math.Ceiling(((double) this.redline.Value + this.Margin()) / 100.0) + 1;
        }

        // The source's stock curve (normalised to its scale) at a point of the 100 rpm grid; its last value goes on past its end.
        private double StockShapeAt(int index)
        {
            if (this.stockBase.Length == 0 || this.stockScale <= 0)
            {
                return 0;
            }
            return this.stockBase[Math.Min(index, this.stockBase.Length - 1)] / this.stockScale;
        }

        private double[] StockCurve(int points)
        {
            double[] curve = new double[points];
            for (int i = 0; i < points; i++)
            {
                curve[i] = this.StockShapeAt(i);
            }
            return curve;
        }

        // A new redline changes the number of points: a shorter curve is cut, a longer one goes on with the source's shape.
        private void FitCurve()
        {
            int points = this.Points();
            if (points == this.shape.Length)
            {
                return;
            }
            double[] fitted = new double[points];
            int last = this.shape.Length - 1;
            for (int i = 0; i < points; i++)
            {
                if (i <= last)
                {
                    fitted[i] = this.shape[i];
                }
                else
                {
                    double a = this.StockShapeAt(last);
                    fitted[i] = (last >= 0 && a > 1e-6) ? this.shape[last] * this.StockShapeAt(i) / a : 0;
                }
            }
            this.shape = fitted;
        }

        // The graph from the curve shape and the redline.
        private void Rebuild()
        {
            if (this.filling)
            {
                return;
            }
            this.FitCurve();
            EnginePower power = new EnginePower();
            int count = this.shape.Length;
            if (count >= 2)
            {
                power.CurveScale = this.stockScale * this.Multiplier();
                power.Redline = (double) this.redline.Value;
                power.Rpm = new double[count];
                power.Normalized = (double[]) this.shape.Clone();
                power.BaseTorque = new double[count];
                power.Torque = new double[count];
                for (int i = 0; i < count; i++)
                {
                    power.Rpm[i] = i * 100.0;
                    power.BaseTorque[i] = this.shape[i] * power.CurveScale;
                    power.Torque[i] = power.BaseTorque[i];
                }
            }
            this.graph.Power = power;
            this.RefreshTable(power);
            this.pointsNote.Text = string.Format(CultureInfo.InvariantCulture, "{0} torque values, 0 - {1} rpm, 100 rpm apart (FM4's end marker is added)", count, (count - 1) * 100);
        }

        // The table: one row per point of the curve.
        private void RefreshTable(EnginePower power)
        {
            this.current = power;
            this.refreshing = true;
            int rows = power.Rpm.Length;
            while (this.table.Rows.Count < rows)
            {
                this.table.Rows.Add();
            }
            while (this.table.Rows.Count > rows)
            {
                this.table.Rows.RemoveAt(this.table.Rows.Count - 1);
            }
            for (int k = 0; k < rows; k++)
            {
                double rpm = power.Rpm[k];
                double torque = power.Torque[k];
                this.table.Rows[k].Cells[0].Value = rpm.ToString("0", CultureInfo.InvariantCulture);
                this.table.Rows[k].Cells[1].Value = torque.ToString("0.0", CultureInfo.InvariantCulture);
                this.table.Rows[k].Cells[2].Value = EnginePower.Cv(EnginePower.Kilowatts(torque, rpm)).ToString("0", CultureInfo.InvariantCulture);
                this.table.Rows[k].DefaultCellStyle.ForeColor = (power.Redline > 0 && rpm > power.Redline) ? ForzaTheme.TextDim : ForzaTheme.Text;
            }
            this.refreshing = false;
        }

        // The factor between the torque shown and the curve's normalised value.
        private double ScaleNow()
        {
            return this.stockScale * this.Multiplier();
        }

        // A torque typed in the table (N.m at that rpm).
        private void TableEdited(int row, int column)
        {
            if (this.refreshing || column != 1 || row < 0 || row >= this.shape.Length || this.ScaleNow() <= 0)
            {
                return;
            }
            double value;
            string text = Convert.ToString(this.table.Rows[row].Cells[1].Value, CultureInfo.InvariantCulture) ?? "";
            if (double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                this.shape[row] = Math.Max(0, value) / this.ScaleNow();
            }
            this.Rebuild();
        }

        // "Generate curve": peak torque and power with their rpm and the rev limiter (the redline) give the whole torque
        // curve (CurveGenerateDialog.Torque), one value per 100 rpm.
        private void GenerateCurve()
        {
            if (this.current.Rpm.Length < 2)
            {
                return;
            }
            double bestTorque = 0, torqueRpm = 0, bestPower = 0, powerRpm = 0;
            for (int i = 0; i < this.current.Rpm.Length; i++)
            {
                double rpm = this.current.Rpm[i];
                if (this.current.Redline > 0 && rpm > this.current.Redline)
                {
                    break;
                }
                double kw = EnginePower.Kilowatts(this.current.Torque[i], rpm);
                if (this.current.Torque[i] > bestTorque) { bestTorque = this.current.Torque[i]; torqueRpm = rpm; }
                if (kw > bestPower) { bestPower = kw; powerRpm = rpm; }
            }
            double idle = (double) this.idle.Value;
            double rangeFrom = torqueRpm, rangeTo = torqueRpm;     // where the torque is within 1% of its peak
            for (int i = 0; i < this.current.Rpm.Length; i++)
            {
                if (this.current.Rpm[i] <= this.current.Redline && this.current.Torque[i] >= 0.99 * bestTorque)
                {
                    rangeFrom = Math.Min(rangeFrom, this.current.Rpm[i]);
                    rangeTo = Math.Max(rangeTo, this.current.Rpm[i]);
                }
            }
            using (CurveGenerateDialog dialog = new CurveGenerateDialog(idle, Math.Round(bestTorque), Math.Round(rangeFrom / 100) * 100, Math.Round(rangeTo / 100) * 100, Math.Round(bestPower), Math.Round(powerRpm / 100) * 100, (double) this.redline.Value))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                decimal limit = Math.Max(this.redline.Minimum, Math.Min(this.redline.Maximum, (decimal) dialog.Limiter));
                if (limit != this.redline.Value)
                {
                    this.redline.Value = limit;       // rebuilds the graph and the table at the new length
                }
                double scale = this.ScaleNow();
                if (scale <= 0)
                {
                    return;
                }
                for (int k = 0; k < this.shape.Length; k++)
                {
                    this.shape[k] = Math.Max(0, CurveGenerateDialog.Torque(k * 100.0, idle, dialog.PeakTorque, dialog.TorqueRpm, dialog.TorqueRpmTo, dialog.PeakPowerKw, dialog.PowerRpm, (double) limit)) / scale;
                }
                this.Rebuild();
            }
        }

        private void Accept()
        {
            EngineSpec spec = new EngineSpec();
            string text = this.name.Text.Trim();
            if (text.Length == 0)
            {
                MessageBox.Show(this, "Give the engine a name.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (text != Convert.ToString(this.source["EngineName"], CultureInfo.InvariantCulture))
            {
                spec.Name = text;
            }
            Choice chosenAspiration = this.aspiration.SelectedItem as Choice;
            if (chosenAspiration != null && chosenAspiration.Id >= 2 && chosenAspiration.Id <= 6 && this.boost.Value <= 0)
            {
                MessageBox.Show(this, "A turbo or supercharged engine needs a boost above 0 bar.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (chosenAspiration != null && chosenAspiration.Id >= 2 && chosenAspiration.Id <= 6 && this.boost.Value > 2.04m
                && MessageBox.Show(this, "FM4's boost goes from 0.48 to 2.04 bar; " + this.boost.Value.ToString(CultureInfo.InvariantCulture) + " bar is outside what the torque and power numbers were fitted on, so the shown values may differ from the game's. Continue?", this.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            Choice picked = this.sound.SelectedItem as Choice;
            if (picked != null && picked.Media != null && picked.Media != Convert.ToString(this.source["MediaName"], CultureInfo.InvariantCulture))
            {
                List<string> soundProblems = new EnginePreview(null, this.target).Diagnose(picked.Media);
                if (soundProblems.Count > 0
                    && MessageBox.Show(this, "The sound " + picked.Media + " has a problem:\n\n" + string.Join("\n", soundProblems.ToArray()) + "\n\nUse it anyway?", this.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return;
                }
                spec.Data["MediaName"] = picked.Media;
            }
            this.Id(spec, "ConfigID", this.layout);
            this.Id(spec, "CylinderID", this.cylinders);
            this.Id(spec, "AspirationID_Stock", this.aspiration);
            this.Real(spec, "StockBoost-bar", this.boost);
            if (chosenAspiration != null && chosenAspiration.Id >= 2 && chosenAspiration.Id <= 4)
            {
                if (this.turboVolume.Value > 0)
                {
                    spec.TurboVolume = (double) this.turboVolume.Value;
                }
                Choice sample = this.bov.SelectedItem as Choice;
                if (sample != null && sample.Id > 0)
                {
                    spec.BovLevel = (int) (sample.Id / 10);
                    spec.BovChoice = (int) (sample.Id % 10);
                }
            }
            this.Real(spec, "Compression", this.compression);
            this.Real(spec, "EngineMass-kg", this.mass);
            this.Flag(spec, "Diesel", this.diesel);
            this.Flag(spec, "Carbureted", this.carbureted);
            this.Flag(spec, "Rotary", this.rotary);
            if (this.stockDisplacement > 0 && this.displacement.Value > 0 && (double) this.displacement.Value != Math.Round(this.stockDisplacement))
            {
                spec.DisplacementCc = (double) this.displacement.Value;
            }
            if (this.stockRedline > 0 && (double) this.redline.Value != Math.Round(this.stockRedline))
            {
                spec.RedlineRpm = (double) this.redline.Value;
            }
            if (this.stockIdle > 0 && (double) this.idle.Value != Math.Round(this.stockIdle))
            {
                spec.IdleRpm = (double) this.idle.Value;
            }
            // The curve is always built from this: base N.m (before the parts and the boost), one value per 100 rpm.
            spec.Curve = new double[this.shape.Length];
            for (int i = 0; i < this.shape.Length; i++)
            {
                spec.Curve[i] = Math.Max(0, this.shape[i]) * this.stockScale;
            }
            this.Result = spec;
            this.DialogResult = DialogResult.OK;
        }

        private void Id(EngineSpec spec, string column, ComboBox combo)
        {
            Choice item = combo.SelectedItem as Choice;
            if (item != null && item.Id != Convert.ToInt64(this.source[column], CultureInfo.InvariantCulture))
            {
                spec.Data[column] = item.Id;
            }
        }

        private void Real(EngineSpec spec, string column, NumericUpDown number)
        {
            if (Math.Abs((double) number.Value - Real(this.source[column])) > Math.Pow(10, -number.DecimalPlaces) / 2)
            {
                spec.Data[column] = (double) number.Value;
            }
        }

        private void Flag(EngineSpec spec, string column, CheckBox box)
        {
            if ((Convert.ToInt64(this.source[column], CultureInfo.InvariantCulture) != 0) != box.Checked)
            {
                spec.Data[column] = box.Checked ? 1L : 0L;
            }
        }

        // One line for the wizard page.
        public static string Describe(EngineSpec spec)
        {
            List<string> parts = new List<string>();
            if (spec.Name != null)
            {
                parts.Add("\"" + spec.Name + "\"");
            }
            object sound;
            if (spec.Data.TryGetValue("MediaName", out sound))
            {
                parts.Add("sound of " + sound);
            }
            if (spec.DisplacementCc.HasValue)
            {
                parts.Add(spec.DisplacementCc.Value.ToString("0", CultureInfo.InvariantCulture) + " cc");
            }
            if (spec.RedlineRpm.HasValue)
            {
                parts.Add("redline " + spec.RedlineRpm.Value.ToString("0", CultureInfo.InvariantCulture));
            }
            if (spec.IdleRpm.HasValue)
            {
                parts.Add("idle " + spec.IdleRpm.Value.ToString("0", CultureInfo.InvariantCulture));
            }
            if (spec.Curve != null)
            {
                parts.Add("own torque curve of " + spec.Curve.Length + " values");
            }
            parts.Add(spec.Data.Count + " other values changed");
            return string.Join(", ", parts.ToArray());
        }
    }
}
