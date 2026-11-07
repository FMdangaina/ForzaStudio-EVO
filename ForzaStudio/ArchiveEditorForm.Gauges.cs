namespace ForzaStudio
{
    using Microsoft.Xna.Framework;
    using Microsoft.Xna.Framework.Graphics;
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.Text;
    using System.Windows.Forms;

    // The analogue gauges of a car's _cockpit.carbin (CarGauges): per gauge the pivot, axis and maximum turn of the
    // model header and the tick marks of carattribs.xml. While the GAUGES tab is open the needle the game turns
    // (<name>_min) is turned in the view by the slider, exactly as the game does (about the pivot, by
    // position x MaxRotation), so it can be checked against the artist's _mid / _max needles; Fit From Poses
    // computes pivot, axis and turn from the three needles. Apply writes the cockpit model and carattribs.xml into
    // the car file, pending until File > Save.
    public partial class ArchiveEditorForm
    {
        private sealed class GaugeState
        {
            public CarGauges.Gauge Value;
            public CarGauges.Gauge Original;
            public List<PointF2> Ticks = new List<PointF2>();
            public string OriginalTicks = "";
            public bool InCarAttributes;
            public double[] Min, Mid, Max;          // needle vertex positions (model axes), null when the part is missing
            public string MinSection;

            public bool HasNeedle
            {
                get { return this.Min != null; }
            }

            public bool HeaderChanged
            {
                get
                {
                    return this.Value.Pivot != this.Original.Pivot || this.Value.Axis != this.Original.Axis || this.Value.MaxRotation != this.Original.MaxRotation;
                }
            }

            public bool TicksChanged
            {
                get { return TicksText(this.Ticks) != this.OriginalTicks; }
            }

            public bool Changed
            {
                get { return this.HeaderChanged || this.TicksChanged; }
            }
        }

        private TabPage tabSideGauges;
        private bool gaugesTabWanted = false;
        private Label lblGauInfo;
        private ListBox lstGauges;
        private TrackBar trkGauNeedle;
        private Label lblGauReading;
        private NumericUpDown[] numGauPivot;
        private NumericUpDown[] numGauAxis;
        private NumericUpDown numGauMax;
        private Label lblGauMax;
        private TextBox txtGauTicks;
        private Label lblGauProblems;
        private CheckBox chkGauHidePoses;
        private CheckBox chkGauShow;
        private Button btnGauFit;
        private Button btnGauUndo;
        private Button btnGauApply;
        private Button btnGauAddTick;
        private NumericUpDown numGauTickValue;
        private CheckBox chkGauKmh;
        private CheckBox chkGauFitAxis;
        private FlowLayoutPanel gauTickRow;

        private ForzaZip.Entry gauEntry;
        private List<GaugeState> gauItems;
        private bool fillingGau;
        private bool gauPreviewOn;

        private void AddGaugeControls(FlowLayoutPanel side)
        {
            this.lblGauInfo = SideNote("Open a car's _cockpit model.");
            this.lblGauInfo.ForeColor = this.ForeColor;

            this.lstGauges = new ListBox();
            this.lstGauges.Size = new Size(SideWidth, 110);
            this.lstGauges.IntegralHeight = false;
            this.lstGauges.SelectedIndexChanged += delegate
            {
                if (!this.fillingGau)
                {
                    this.FillGaugeControls();
                    this.UpdateViewOverlay();
                }
            };

            this.trkGauNeedle = new TrackBar();
            this.trkGauNeedle.Minimum = 0;
            this.trkGauNeedle.Maximum = 1000;
            this.trkGauNeedle.TickFrequency = 100;
            this.trkGauNeedle.SmallChange = 10;
            this.trkGauNeedle.LargeChange = 100;
            this.trkGauNeedle.Size = new Size(SideWidth, 32);
            this.trkGauNeedle.ValueChanged += delegate { this.UpdateGaugeReading(); this.UpdateViewOverlay(); };
            this.lblGauReading = SideNote("");
            this.lblGauReading.ForeColor = this.ForeColor;

            FlowLayoutPanel pivot = this.NumberRow(out this.numGauPivot, -5, 5, 4, 0.001m, 0);
            FlowLayoutPanel axis = this.NumberRow(out this.numGauAxis, -1, 1, 4, 0.005m, 0);
            foreach (NumericUpDown[] row in new NumericUpDown[][] { this.numGauPivot, this.numGauAxis })
            {
                foreach (NumericUpDown number in row)
                {
                    number.ValueChanged -= new EventHandler(this.PartEditValueChanged);
                    number.ValueChanged += delegate { this.GaugeValueChanged(); };
                }
            }
            this.numGauMax = new NumericUpDown();
            this.numGauMax.Minimum = -7;
            this.numGauMax.Maximum = 7;
            this.numGauMax.DecimalPlaces = 4;
            this.numGauMax.Increment = 0.01m;
            this.numGauMax.Width = 90;
            this.numGauMax.ValueChanged += delegate { this.GaugeValueChanged(); };
            this.lblGauMax = new Label();
            this.lblGauMax.AutoSize = false;
            this.lblGauMax.Size = new Size(SideWidth - 96, 22);
            this.lblGauMax.TextAlign = ContentAlignment.MiddleLeft;
            FlowLayoutPanel maxRow = new FlowLayoutPanel();
            maxRow.Size = new Size(SideWidth, 28);
            maxRow.Margin = new Padding(0);
            maxRow.Controls.Add(this.numGauMax);
            maxRow.Controls.Add(this.lblGauMax);

            this.txtGauTicks = new TextBox();
            this.txtGauTicks.Multiline = true;
            this.txtGauTicks.ScrollBars = ScrollBars.Vertical;
            this.txtGauTicks.Size = new Size(SideWidth, 110);
            this.txtGauTicks.Font = new Font(FontFamily.GenericMonospace, 8.25f);
            this.txtGauTicks.TextChanged += delegate { this.GaugeTicksChanged(); };

            // Tick at the needle: move the needle onto a number printed on the dial, type that number, add.
            this.numGauTickValue = new NumericUpDown();
            this.numGauTickValue.Minimum = -100000;
            this.numGauTickValue.Maximum = 100000;
            this.numGauTickValue.DecimalPlaces = 2;
            this.numGauTickValue.Width = 90;
            this.chkGauKmh = new CheckBox();
            this.chkGauKmh.Text = "km/h";
            this.chkGauKmh.Checked = true;
            this.chkGauKmh.AutoSize = true;
            Button addTick = new Button();
            addTick.Text = "Add Tick at Needle";
            addTick.Size = new Size(SideWidth, 28);
            addTick.Click += delegate { this.AddTickAtNeedle(); };
            FlowLayoutPanel tickRow = new FlowLayoutPanel();
            tickRow.Size = new Size(SideWidth, 28);
            tickRow.Margin = new Padding(0);
            tickRow.Controls.Add(this.numGauTickValue);
            tickRow.Controls.Add(this.chkGauKmh);
            this.gauTickRow = tickRow;
            this.btnGauAddTick = addTick;

            this.lblGauProblems = SideNote("");
            this.lblGauProblems.ForeColor = ForzaTheme.Red;

            this.chkGauHidePoses = new CheckBox();
            this.chkGauHidePoses.Text = "Only the turning needle (as in game)";
            this.chkGauHidePoses.Checked = true;
            this.chkGauHidePoses.Size = new Size(SideWidth, 22);
            this.chkGauHidePoses.CheckedChanged += delegate { this.UpdateViewOverlay(); };
            this.chkGauShow = new CheckBox();
            this.chkGauShow.Text = "Show pivot, axis and scale in the view";
            this.chkGauShow.Checked = true;
            this.chkGauShow.Size = new Size(SideWidth, 22);
            this.chkGauShow.CheckedChanged += delegate { this.UpdateViewOverlay(); };

            this.btnGauFit = new Button();
            this.btnGauFit.Text = "Fit From Poses";
            this.btnGauFit.Click += delegate { this.FitGauge(); };
            this.btnGauUndo = new Button();
            this.btnGauUndo.Text = "Undo Gauge";
            this.btnGauUndo.Click += delegate { this.UndoGauge(); };
            this.btnGauApply = SideWideButton("Apply to Model", delegate { this.ApplyGauges(); });

            side.Controls.Add(SideHeading("Gauges", true));
            side.Controls.Add(this.lblGauInfo);
            side.Controls.Add(this.lstGauges);
            side.Controls.Add(SideCaption("Needle position (0 = _min, 1 = _max)"));
            side.Controls.Add(this.trkGauNeedle);
            side.Controls.Add(this.lblGauReading);
            side.Controls.Add(this.chkGauHidePoses);
            side.Controls.Add(this.chkGauShow);
            side.Controls.Add(SideHeading("Model header", false));
            side.Controls.Add(SideCaption("Pivot (m, model axes)"));
            side.Controls.Add(pivot);
            side.Controls.Add(SideCaption("Axis (model axes; header stores -X, -Y, Z)"));
            side.Controls.Add(axis);
            side.Controls.Add(SideCaption("Max rotation (radians)"));
            side.Controls.Add(maxRow);
            side.Controls.Add(SideButtonRow(this.btnGauFit, this.btnGauUndo));
            this.chkGauFitAxis = new CheckBox();
            this.chkGauFitAxis.Text = "Fit also the axis (needles' plane)";
            this.chkGauFitAxis.Size = new Size(SideWidth, 22);
            side.Controls.Add(this.chkGauFitAxis);
            side.Controls.Add(SideHeading("Tick marks (carattribs)", false));
            side.Controls.Add(SideCaption("One per line: needle 0..1, reading"));
            side.Controls.Add(this.txtGauTicks);
            side.Controls.Add(SideCaption("Reading at the needle (speed: km/h when ticked)"));
            side.Controls.Add(this.gauTickRow);
            side.Controls.Add(this.btnGauAddTick);
            side.Controls.Add(this.lblGauProblems);
            side.Controls.Add(SideHeading("Write", false));
            side.Controls.Add(this.btnGauApply);
            side.Controls.Add(SideNote("The game turns the <name>_min needle about the pivot by position x max rotation (right hand rule about the axis); _mid and _max are the artist's needles at half and full scale, so at 1 the turning needle must cover _max. Readings: speed in m/s (x 3.6 = km/h), tach in rpm; no tick marks = the reading is used as it is (fuel). Fit From Poses computes pivot, axis and max rotation from the three needles. Apply writes the cockpit model and carattribs.xml, pending until File > Save."));
            this.EnableGaugeEdit();
        }

        private void SetGaugesTabVisible(bool visible)
        {
            this.gaugesTabWanted = visible;
            this.ArrangeCarTabs();
        }

        private GaugeState SelectedGauge
        {
            get
            {
                int index = (this.lstGauges == null) ? -1 : this.lstGauges.SelectedIndex;
                return (this.gauItems != null && index >= 0 && index < this.gauItems.Count) ? this.gauItems[index] : null;
            }
        }

        private bool GaugesChanged
        {
            get { return this.gauItems != null && this.gauItems.Exists(delegate(GaugeState g) { return g.Changed; }); }
        }

        private void EnableGaugeEdit()
        {
            GaugeState g = this.SelectedGauge;
            bool on = g != null;
            foreach (NumericUpDown n in this.numGauPivot)
            {
                n.Enabled = on;
            }
            foreach (NumericUpDown n in this.numGauAxis)
            {
                n.Enabled = on;
            }
            this.numGauMax.Enabled = on;
            this.txtGauTicks.Enabled = on;
            this.trkGauNeedle.Enabled = on;
            this.btnGauFit.Enabled = on && g.HasNeedle && g.Mid != null && g.Max != null;
            this.btnGauAddTick.Enabled = on;
            this.numGauTickValue.Enabled = on;
            this.chkGauKmh.Visible = on && g.Value.Index == 0;
            this.btnGauUndo.Enabled = on && g.Changed;
            this.btnGauApply.Enabled = this.GaugesChanged;
        }

        // Called with every model shown: a car's _cockpit.carbin whose header has the gauge table.
        private void LoadGauges()
        {
            this.ClearGaugePreview();
            this.gauEntry = null;
            this.gauItems = null;
            this.lblGauInfo.Text = "";
            this.lblGauInfo.ForeColor = this.ForeColor;
            string name = (this.currentEntry != null) ? this.currentEntry.Name.ToLowerInvariant() : "";
            if (name.EndsWith("_cockpit.carbin"))
            {
                try
                {
                    CarbinModel model = new CarbinModel(this.zip.Read(this.currentEntry));
                    CarGauges.Gauge[] header = CarGauges.Read(model.Head);
                    if (header != null)
                    {
                        string attributes = null;
                        ForzaZip.Entry entry = this.zip.Find("carattribs.xml");
                        if (entry != null)
                        {
                            attributes = Encoding.UTF8.GetString(this.zip.Read(entry));
                        }
                        List<GaugeState> items = new List<GaugeState>();
                        foreach (CarGauges.Gauge gauge in header)
                        {
                            GaugeState state = new GaugeState();
                            state.Value = gauge;
                            state.Original = gauge.Copy();
                            if (attributes != null)
                            {
                                state.Ticks = CarGauges.ReadTicks(attributes, gauge.XmlName, out state.InCarAttributes);
                            }
                            state.OriginalTicks = TicksText(state.Ticks);
                            foreach (CarbinModel.Section section in model.Sections)
                            {
                                string lower = section.Name.ToLowerInvariant();
                                if (!lower.StartsWith(gauge.SectionPrefix))
                                {
                                    continue;
                                }
                                CarbinModel.VertexBuffer buffer = (section.Lod0.Count > 0) ? section.Lod0 : section.Lod;
                                double[] positions = CarbinModel.Positions(section, buffer);
                                string pose = lower.Substring(gauge.SectionPrefix.Length);
                                if (pose.StartsWith("min") && state.Min == null)
                                {
                                    state.Min = positions;
                                    state.MinSection = section.Name;
                                }
                                else if (pose.StartsWith("mid") && state.Mid == null)
                                {
                                    state.Mid = positions;
                                }
                                else if (pose.StartsWith("max") && state.Max == null)
                                {
                                    state.Max = positions;
                                }
                            }
                            // Listed when the car uses it: a needle, header data or tick marks.
                            if (state.HasNeedle || !gauge.IsDefault || state.Ticks.Count > 0)
                            {
                                items.Add(state);
                            }
                        }
                        this.gauEntry = this.currentEntry;
                        this.gauItems = items;
                    }
                }
                catch (Exception exception)
                {
                    this.lblGauInfo.Text = "Gauges not read: " + exception.Message;
                }
            }
            this.SetGaugesTabVisible(this.gauItems != null);
            this.FillGaugeList(0);
        }

        private static string TicksText(List<PointF2> ticks)
        {
            StringBuilder text = new StringBuilder();
            foreach (PointF2 tick in ticks)
            {
                text.AppendFormat(CultureInfo.InvariantCulture, "{0:0.######} {1:0.######}\r\n", tick.X, tick.Y);
            }
            return text.ToString();
        }

        private static string GaugeText(GaugeState g)
        {
            List<string> notes = new List<string>();
            notes.Add(g.HasNeedle ? "needle" : "no needle");
            notes.Add(g.Ticks.Count + " ticks");
            if (CarGauges.Check(g.Value, g.Ticks, g.HasNeedle).Count > 0)
            {
                notes.Add("!");
            }
            return (g.Changed ? "* " : "") + g.Value.XmlName + " (" + string.Join(", ", notes.ToArray()) + ")";
        }

        private void FillGaugeList(int select)
        {
            this.fillingGau = true;
            this.lstGauges.Items.Clear();
            if (this.gauItems != null)
            {
                foreach (GaugeState g in this.gauItems)
                {
                    this.lstGauges.Items.Add(GaugeText(g));
                }
                if (this.lstGauges.Items.Count > 0)
                {
                    this.lstGauges.SelectedIndex = Math.Max(0, Math.Min(select, this.lstGauges.Items.Count - 1));
                }
                int problems = this.gauItems.FindAll(delegate(GaugeState g) { return CarGauges.Check(g.Value, g.Ticks, g.HasNeedle).Count > 0; }).Count;
                int changed = this.gauItems.FindAll(delegate(GaugeState g) { return g.Changed; }).Count;
                this.lblGauInfo.Text = this.gauItems.Count + " gauges" + (problems > 0 ? ", " + problems + " with problems (!)" : "") + (changed > 0 ? "\n" + changed + " edited, not applied" : "");
                this.lblGauInfo.ForeColor = (changed > 0 || problems > 0) ? ForzaTheme.Red : this.ForeColor;
            }
            this.fillingGau = false;
            this.FillGaugeControls();
            this.UpdateViewOverlay();
        }

        private void RefreshGaugeLine()
        {
            int index = this.lstGauges.SelectedIndex;
            GaugeState g = this.SelectedGauge;
            if (g == null)
            {
                return;
            }
            this.fillingGau = true;
            this.lstGauges.Items[index] = GaugeText(g);
            this.fillingGau = false;
        }

        private void FillGaugeControls()
        {
            GaugeState g = this.SelectedGauge;
            this.fillingGau = true;
            Vector3 p = (g != null) ? g.Value.Pivot : Vector3.Zero;
            Vector3 a = (g != null) ? g.Value.Axis : Vector3.Zero;
            float[] pv = { p.X, p.Y, p.Z }, av = { a.X, a.Y, a.Z };
            for (int i = 0; i < 3; i++)
            {
                this.numGauPivot[i].Value = Clamp(this.numGauPivot[i], pv[i]);
                this.numGauAxis[i].Value = Clamp(this.numGauAxis[i], av[i]);
            }
            this.numGauMax.Value = (g != null) ? Clamp(this.numGauMax, g.Value.MaxRotation) : 0;
            this.txtGauTicks.Text = (g != null) ? TicksText(g.Ticks) : "";
            this.fillingGau = false;
            this.UpdateGaugeLabels();
            this.EnableGaugeEdit();
        }

        private static decimal Clamp(NumericUpDown number, float value)
        {
            return Math.Max(number.Minimum, Math.Min(number.Maximum, (decimal) Math.Round(value, number.DecimalPlaces)));
        }

        private void UpdateGaugeLabels()
        {
            GaugeState g = this.SelectedGauge;
            this.lblGauMax.Text = (g != null) ? string.Format(CultureInfo.InvariantCulture, "= {0:0.0} degrees", MathHelper.ToDegrees(g.Value.MaxRotation)) : "";
            List<string> problems = (g != null) ? CarGauges.Check(g.Value, g.Ticks, g.HasNeedle) : new List<string>();
            if (g != null && g.HasNeedle && g.Max != null && g.Max.Length == g.Min.Length)
            {
                double error = CarGauges.PoseError(g.Min, g.Max, g.Value.Pivot, g.Value.Axis, g.Value.MaxRotation) * 1000;
                if (!double.IsNaN(error) && error > 5)
                {
                    problems.Add(string.Format(CultureInfo.InvariantCulture, "at 1 the needle is {0:0.0} mm from _max (FM4 cars: under 2 mm): pivot, axis or max rotation off - try Fit From Poses", error));
                }
            }
            if (g != null && !g.HasNeedle)
            {
                problems.Add("no " + g.Value.SectionPrefix + "min part in this model: nothing turns");
            }
            this.lblGauProblems.Text = string.Join("\n", problems.ToArray());
            this.UpdateGaugeReading();
        }

        private void UpdateGaugeReading()
        {
            GaugeState g = this.SelectedGauge;
            if (g == null)
            {
                this.lblGauReading.Text = "";
                return;
            }
            float t = this.trkGauNeedle.Value / 1000f;
            float reading = CarGauges.ValueAt(g.Ticks, t);
            string unit = "";
            if (g.Ticks.Count >= 2)
            {
                if (g.Value.Index == 0)
                {
                    unit = string.Format(CultureInfo.InvariantCulture, " m/s = {0:0} km/h = {1:0} mph", reading * 3.6f, reading * 2.23694f);
                }
                else if (g.Value.Index == 1)
                {
                    unit = " rpm";
                }
            }
            this.lblGauReading.Text = string.Format(CultureInfo.InvariantCulture, "Needle {0:0.000}: reading {1:0.###}{2}{3}", t, reading, unit, g.Ticks.Count == 0 ? " (no tick marks: the reading itself)" : "");
        }

        private void GaugeValueChanged()
        {
            GaugeState g = this.SelectedGauge;
            if (this.fillingGau || g == null)
            {
                return;
            }
            g.Value.Pivot = new Vector3((float) this.numGauPivot[0].Value, (float) this.numGauPivot[1].Value, (float) this.numGauPivot[2].Value);
            g.Value.Axis = new Vector3((float) this.numGauAxis[0].Value, (float) this.numGauAxis[1].Value, (float) this.numGauAxis[2].Value);
            g.Value.MaxRotation = (float) this.numGauMax.Value;
            // The boxes round to 4 decimals: a value not typed keeps its full precision.
            KeepGauge(ref g.Value.Pivot, g.Original.Pivot, this.numGauPivot);
            KeepGauge(ref g.Value.Axis, g.Original.Axis, this.numGauAxis);
            if ((decimal) Math.Round(g.Original.MaxRotation, 4) == this.numGauMax.Value)
            {
                g.Value.MaxRotation = g.Original.MaxRotation;
            }
            this.GaugeEdited();
        }

        private static void KeepGauge(ref Vector3 value, Vector3 original, NumericUpDown[] numbers)
        {
            float[] v = { value.X, value.Y, value.Z }, o = { original.X, original.Y, original.Z };
            for (int i = 0; i < 3; i++)
            {
                if ((decimal) Math.Round(o[i], 4) == numbers[i].Value)
                {
                    v[i] = o[i];
                }
            }
            value = new Vector3(v[0], v[1], v[2]);
        }

        private void GaugeTicksChanged()
        {
            GaugeState g = this.SelectedGauge;
            if (this.fillingGau || g == null)
            {
                return;
            }
            List<PointF2> ticks = new List<PointF2>();
            foreach (string line in this.txtGauTicks.Lines)
            {
                string[] parts = line.Trim().Split(new char[] { ' ', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries);
                float x, y;
                if (parts.Length >= 2 && float.TryParse(parts[0].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                    && float.TryParse(parts[1].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out y))
                {
                    ticks.Add(new PointF2(x, y));
                }
            }
            g.Ticks = ticks;
            this.GaugeEdited();
        }

        private void GaugeEdited()
        {
            this.RefreshGaugeLine();
            this.UpdateGaugeLabels();
            this.EnableGaugeEdit();
            this.btnGauApply.Enabled = this.GaugesChanged;
            this.UpdateViewOverlay();
        }

        // A tick mark at the needle's position with the typed reading (speed typed in km/h is stored in m/s);
        // a mark at the same position is replaced, the list stays sorted.
        private void AddTickAtNeedle()
        {
            GaugeState g = this.SelectedGauge;
            if (g == null)
            {
                return;
            }
            float t = this.trkGauNeedle.Value / 1000f;
            float reading = (float) this.numGauTickValue.Value;
            if (g.Value.Index == 0 && this.chkGauKmh.Checked)
            {
                reading /= 3.6f;
            }
            List<PointF2> ticks = new List<PointF2>(g.Ticks);
            ticks.RemoveAll(delegate(PointF2 tick) { return Math.Abs(tick.X - t) < 0.0005f; });
            ticks.Add(new PointF2(t, reading));
            ticks.Sort(delegate(PointF2 x, PointF2 y) { return x.X.CompareTo(y.X); });
            g.Ticks = ticks;
            this.fillingGau = true;
            this.txtGauTicks.Text = TicksText(g.Ticks);
            this.fillingGau = false;
            this.GaugeEdited();
        }

        private void FitGauge()
        {
            GaugeState g = this.SelectedGauge;
            if (g == null || g.Min == null || g.Mid == null || g.Max == null)
            {
                return;
            }
            Vector3 pivot, axis;
            float angle;
            double error;
            // The header's axis stays when there is one (the game's); only a needle without header data gets the
            // axis of the needles' plane.
            Vector3 keep = (g.Value.IsDefault || this.chkGauFitAxis.Checked) ? Vector3.Zero : g.Value.Axis;
            if (!CarGauges.Fit(g.Min, g.Mid, g.Max, keep, out pivot, out axis, out angle, out error))
            {
                MessageBox.Show(this, "The three needles do not give a turn (different vertices, or they do not move).", "Gauges", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            double before = CarGauges.PoseError(g.Min, g.Max, g.Value.Pivot, g.Value.Axis, g.Value.MaxRotation);
            g.Value.Pivot = pivot;
            g.Value.Axis = axis;
            g.Value.MaxRotation = angle;
            this.FillGaugeControls();
            this.GaugeEdited();
            this.SetStatus(string.Format(CultureInfo.InvariantCulture, "{0}: fitted from {1}min / mid / max: pivot {2:0.0000}, {3:0.0000}, {4:0.0000}, axis {5:0.000}, {6:0.000}, {7:0.000}, max {8:0.0000} rad; needle at 1 is {9:0.0} mm from _max (was {10:0.0} mm). Apply to keep it.",
                g.Value.XmlName, g.Value.SectionPrefix, pivot.X, pivot.Y, pivot.Z, axis.X, axis.Y, axis.Z, angle, error * 1000, before * 1000));
        }

        private void UndoGauge()
        {
            GaugeState g = this.SelectedGauge;
            if (g == null)
            {
                return;
            }
            g.Value = g.Original.Copy();
            g.Ticks = new List<PointF2>();
            bool present;
            ForzaZip.Entry entry = this.zip.Find("carattribs.xml");
            if (entry != null)
            {
                g.Ticks = CarGauges.ReadTicks(Encoding.UTF8.GetString(this.zip.Read(entry)), g.Value.XmlName, out present);
            }
            this.FillGaugeControls();
            this.GaugeEdited();
        }

        private void ApplyGauges()
        {
            if (this.gauEntry == null || this.gauItems == null || !this.GaugesChanged)
            {
                return;
            }
            try
            {
                List<string> done = new List<string>();
                if (this.gauItems.Exists(delegate(GaugeState g) { return g.HeaderChanged; }))
                {
                    CarbinModel model = new CarbinModel(this.zip.Read(this.gauEntry));
                    foreach (GaugeState g in this.gauItems)
                    {
                        if (g.HeaderChanged)
                        {
                            CarGauges.Write(model.Head, g.Value);
                            done.Add(g.Value.XmlName + " header");
                        }
                    }
                    this.zip.Replace(this.gauEntry, model.ToBytes());
                    this.RefreshEntryNode(this.gauEntry);
                }
                if (this.gauItems.Exists(delegate(GaugeState g) { return g.TicksChanged; }))
                {
                    ForzaZip.Entry entry = this.zip.Find("carattribs.xml");
                    if (entry == null)
                    {
                        throw new InvalidOperationException("The car file has no carattribs.xml: the tick marks were not written.");
                    }
                    string text = Encoding.UTF8.GetString(this.zip.Read(entry));
                    foreach (GaugeState g in this.gauItems)
                    {
                        if (g.TicksChanged)
                        {
                            text = CarGauges.WriteTicks(text, g.Value.XmlName, g.Ticks);
                            done.Add(g.Value.XmlName + " " + g.Ticks.Count + " ticks");
                        }
                    }
                    this.zip.Replace(entry, Encoding.UTF8.GetBytes(text));
                    this.RefreshEntryNode(entry);
                }
                foreach (GaugeState g in this.gauItems)
                {
                    g.Original = g.Value.Copy();
                    g.OriginalTicks = TicksText(g.Ticks);
                }
                this.FillGaugeList(Math.Max(0, this.lstGauges.SelectedIndex));
                this.SetStatus("Gauges written: " + string.Join(", ", done.ToArray()) + " (pending: Save to write the archive)");
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Gauges", MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
        }

        // The needle of the selected gauge turned to the slider position; the other needles at 0. Only while the
        // GAUGES tab is open (called by UpdateViewOverlay, which redraws).
        private void UpdateGaugePreview(bool open)
        {
            if (!open || this.gauItems == null)
            {
                this.ClearGaugePreview();
                return;
            }
            GaugeState selected = this.SelectedGauge;
            float t = this.trkGauNeedle.Value / 1000f;
            foreach (GaugeState g in this.gauItems)
            {
                if (!g.HasNeedle)
                {
                    continue;
                }
                Matrix turn = (g == selected) ? g.Value.NeedleTransform(t) : Matrix.Identity;
                foreach (ForzaMesh mesh in this.SectionMeshes(g.MinSection))
                {
                    mesh.PreviewTransform = turn;
                    mesh.Highlight = g == selected;
                }
                foreach (string pose in new string[] { "mid", "max" })
                {
                    foreach (TreeNode root in this.tvModel.Nodes)
                    {
                        foreach (TreeNode lod in root.Nodes)
                        {
                            foreach (TreeNode part in lod.Nodes)
                            {
                                if (part.Name.StartsWith(g.Value.SectionPrefix + pose, StringComparison.OrdinalIgnoreCase))
                                {
                                    foreach (ForzaMesh mesh in GetMeshes(part.Nodes, false))
                                    {
                                        mesh.Hidden = this.chkGauHidePoses.Checked;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            this.gauPreviewOn = true;
        }

        private void ClearGaugePreview()
        {
            if (!this.gauPreviewOn)
            {
                return;
            }
            this.gauPreviewOn = false;
            if (this.gauItems == null)
            {
                return;
            }
            foreach (GaugeState g in this.gauItems)
            {
                if (!g.HasNeedle)
                {
                    continue;
                }
                foreach (TreeNode root in this.tvModel.Nodes)
                {
                    foreach (TreeNode lod in root.Nodes)
                    {
                        foreach (TreeNode part in lod.Nodes)
                        {
                            if (part.Name.StartsWith(g.Value.SectionPrefix, StringComparison.OrdinalIgnoreCase))
                            {
                                foreach (ForzaMesh mesh in GetMeshes(part.Nodes, false))
                                {
                                    mesh.PreviewTransform = Matrix.Identity;
                                    mesh.Highlight = false;
                                    mesh.Hidden = false;
                                }
                            }
                        }
                    }
                }
            }
        }

        // Pivot (yellow cross), axis (cyan line), the swept scale (orange arc at the needle tip's radius, a mark
        // per tick), the needle at the slider (yellow) and at the ends (grey).
        private List<VertexPositionColor> GaugeOverlayLines()
        {
            GaugeState g = this.SelectedGauge;
            if (g == null || !this.chkGauShow.Checked || g.Value.Axis.LengthSquared() < 1e-10f)
            {
                return null;
            }
            List<VertexPositionColor> lines = new List<VertexPositionColor>();
            Microsoft.Xna.Framework.Graphics.Color yellow = new Microsoft.Xna.Framework.Graphics.Color(255, 230, 40, 255);
            Microsoft.Xna.Framework.Graphics.Color cyan = new Microsoft.Xna.Framework.Graphics.Color(60, 220, 255, 255);
            Microsoft.Xna.Framework.Graphics.Color orange = new Microsoft.Xna.Framework.Graphics.Color(255, 150, 40, 255);
            Microsoft.Xna.Framework.Graphics.Color grey = new Microsoft.Xna.Framework.Graphics.Color(170, 170, 170, 255);
            Vector3 pivot = g.Value.Pivot;
            Vector3 axis = Vector3.Normalize(g.Value.Axis);
            AddCross(lines, pivot, 0.006f, yellow);
            lines.Add(new VertexPositionColor(pivot, cyan));
            lines.Add(new VertexPositionColor(pivot + (axis * 0.04f), cyan));
            // The needle's tip: its vertex farthest from the axis (the vertex itself, so lines and arc stay on the needle).
            Vector3 tip = Vector3.Zero;
            float best = 0f;
            if (g.Min != null)
            {
                for (int i = 0; i < g.Min.Length / 3; i++)
                {
                    Vector3 v = new Vector3((float) g.Min[i * 3], (float) g.Min[(i * 3) + 1], (float) g.Min[(i * 3) + 2]) - pivot;
                    Vector3 flat = v - (axis * Vector3.Dot(v, axis));
                    if (flat.LengthSquared() > best)
                    {
                        best = flat.LengthSquared();
                        tip = v;
                    }
                }
            }
            if (best < 1e-8f)
            {
                return lines;
            }
            const int steps = 48;
            for (int s = 0; s < steps; s++)
            {
                Vector3 a = Vector3.Transform(pivot + tip, g.Value.NeedleTransform(s / (float) steps));
                Vector3 b = Vector3.Transform(pivot + tip, g.Value.NeedleTransform((s + 1) / (float) steps));
                lines.Add(new VertexPositionColor(a, orange));
                lines.Add(new VertexPositionColor(b, orange));
            }
            foreach (PointF2 tick in g.Ticks)
            {
                Matrix turn = g.Value.NeedleTransform(tick.X);
                lines.Add(new VertexPositionColor(Vector3.Transform(pivot + (tip * 1.08f), turn), orange));
                lines.Add(new VertexPositionColor(Vector3.Transform(pivot + (tip * 1.25f), turn), orange));
            }
            // The limits: the needle at 0, half and full scale (where _min, _mid, _max should be).
            foreach (float end in new float[] { 0f, 0.5f, 1f })
            {
                lines.Add(new VertexPositionColor(pivot, grey));
                lines.Add(new VertexPositionColor(Vector3.Transform(pivot + tip, g.Value.NeedleTransform(end)), grey));
            }
            lines.Add(new VertexPositionColor(pivot, yellow));
            lines.Add(new VertexPositionColor(Vector3.Transform(pivot + (tip * 1.1f), g.Value.NeedleTransform(this.trkGauNeedle.Value / 1000f)), yellow));
            return lines;
        }
    }
}
