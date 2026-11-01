namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Windows.Forms;

    // Shader Settings view of a car's shadersettings.xml: on the left the techniques the car's models
    // use (and the entries no model uses), on the right the parameters of the selected one. Red: an
    // error (a parameter FM4 does not know, or one no FM4 car sets on that technique, such as
    // BrakeColor on lights_glass); yellow: an entry no model uses (harmless, 41 FM4 cars have some).
    // A technique without an entry runs with its shader's defaults (61 FM4 cars have no file at all).
    // Every edit is stored in the archive at once (pending until Save); "Edit as XML" opens the text.
    // "Copy missing from" reads the entries of this file or of another car (Media\cars of the same game
    // folder, or any car zip by Browse...), shows the chosen entry's values and copies what is missing.
    public partial class ArchiveEditorForm
    {
        private Panel pnlShader;
        private ListView lvTechniques;
        private ListView lvParameters;
        private ToolStripLabel lblShaderSummary;
        private Label lblShaderTechnique;
        private ShaderViewport sphereShader;
        private ComboBox cmbAddParameter;
        private ComboBox cmbCopyFrom;
        private ComboBox cmbCopyCar;
        private Label lblCopyPreview;
        private Button btnAddParameter;
        private FlowLayoutPanel pnlValueEditor;
        private Button btnRemoveParameter;
        private Button btnCopyFrom;
        private Button btnRemoveEntry;
        private ToolStripButton btnXmlShaderView;

        private ForzaZip.Entry shaderEntry;
        private ShaderSettings shaderData;
        private SortedDictionary<string, List<string>> shaderUsed;
        private ICollection<string> shaderKnown;     // every parameter name the game's shader files hold (null = unreadable)
        private bool shaderAsXml;          // "Edit as XML" chosen for the shown file
        private ShaderSettings copySource;    // the settings "Copy missing from" reads (this file or another car)
        private string copySourceName;        // that car's name, null = this file
        private Dictionary<string, string> copyCarPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private bool fillingCopyCars;

        private const string CopyThisFile = "(this file)";
        private const string CopyBrowse = "Browse...";

        private static bool IsShaderSettings(ForzaZip.Entry entry)
        {
            return entry.Name.EndsWith("shadersettings.xml", StringComparison.OrdinalIgnoreCase);
        }

        private void BuildShaderView()
        {
            this.pnlShader = new Panel();
            this.pnlShader.Dock = DockStyle.Fill;

            ToolStrip bar = new ToolStrip();
            bar.GripStyle = ToolStripGripStyle.Hidden;
            bar.Items.Add(new ToolStripButton("Edit as XML", null, delegate
            {
                this.shaderAsXml = true;
                this.ShowXml(this.shaderEntry, this.zip.Read(this.shaderEntry));
            }));
            bar.Items.Add(new ToolStripButton("Remove Unused Entries", null, delegate { this.RemoveUnusedShaderEntries(); }));
            this.lblShaderSummary = new ToolStripLabel();
            this.lblShaderSummary.Alignment = ToolStripItemAlignment.Right;
            bar.Items.Add(this.lblShaderSummary);

            // Techniques on the left at a fixed width (it keeps it when the window grows), everything else on the right.
            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.FixedPanel = FixedPanel.Panel1;
            // The sizes can only be set once the control has its real width (a new SplitContainer is 150 px wide:
            // setting them now throws and the program does not start).
            bool splitSet = false;
            split.SizeChanged += delegate
            {
                if (!splitSet && split.Width > 800)
                {
                    splitSet = true;
                    try
                    {
                        split.Panel1MinSize = 240;
                        split.Panel2MinSize = 420;
                        split.SplitterDistance = 360;
                    }
                    catch (ArgumentException)
                    {
                    }
                }
            };

            this.lvTechniques = new ListView();
            this.lvTechniques.Dock = DockStyle.Fill;
            this.lvTechniques.View = View.Details;
            this.lvTechniques.FullRowSelect = true;
            this.lvTechniques.HideSelection = false;
            this.lvTechniques.MultiSelect = false;
            this.lvTechniques.Columns.Add("Technique", 190);
            this.lvTechniques.Columns.Add("Used in", 130);
            this.lvTechniques.ShowItemToolTips = true;
            this.lvTechniques.SelectedIndexChanged += delegate { this.FillShaderParameters(); };
            split.Panel1.Controls.Add(this.lvTechniques);

            // Right side, top to bottom: title + sphere, parameter list, the value editor, add / remove, copy.
            TableLayoutPanel right = new TableLayoutPanel();
            right.Dock = DockStyle.Fill;
            right.ColumnCount = 1;
            right.RowCount = 5;
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 200f));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Row 0: title and the FM4 list on the left, the preview sphere and its buttons on the right.
            this.lblShaderTechnique = new Label();
            this.lblShaderTechnique.Dock = DockStyle.Fill;
            this.lblShaderTechnique.Padding = new Padding(6, 6, 6, 6);
            this.sphereShader = new ShaderViewport();
            this.sphereShader.Dock = DockStyle.Fill;
            this.sphereShader.DoubleClick += delegate { this.ShowBigSphere(); };
            Button sphereFile = new Button();
            sphereFile.Text = "Texture...";
            sphereFile.AutoSize = true;
            sphereFile.Click += delegate { this.PickSphereTexture(); };
            Button sphereAuto = new Button();
            sphereAuto.Text = "Auto";
            sphereAuto.AutoSize = true;
            sphereAuto.Click += delegate { this.AutoSphereTexture(); };
            Button sphereBig = new Button();
            sphereBig.Text = "Bigger";
            sphereBig.AutoSize = true;
            sphereBig.Click += delegate { this.ShowBigSphere(); };
            FlowLayoutPanel sphereButtons = new FlowLayoutPanel();
            sphereButtons.Dock = DockStyle.Bottom;
            sphereButtons.AutoSize = true;
            sphereButtons.Controls.AddRange(new Control[] { sphereFile, sphereAuto, sphereBig });
            Panel sphereBox = new Panel();
            sphereBox.Dock = DockStyle.Right;
            sphereBox.Width = 230;
            sphereBox.Controls.Add(this.sphereShader);
            sphereBox.Controls.Add(sphereButtons);
            Panel head = new Panel();
            head.Dock = DockStyle.Fill;
            head.Controls.Add(this.lblShaderTechnique);
            head.Controls.Add(sphereBox);
            right.Controls.Add(head, 0, 0);

            // Row 1: the parameters of the technique.
            this.lvParameters = new ListView();
            this.lvParameters.Dock = DockStyle.Fill;
            this.lvParameters.View = View.Details;
            this.lvParameters.FullRowSelect = true;
            this.lvParameters.HideSelection = false;
            this.lvParameters.MultiSelect = false;
            this.lvParameters.Columns.Add("Parameter", 200);
            this.lvParameters.Columns.Add("Value", 230);
            this.lvParameters.Columns.Add("Check", 260);
            this.lvParameters.SelectedIndexChanged += delegate { this.EnableShaderButtons(); this.BuildValueEditor(); };
            right.Controls.Add(this.lvParameters, 0, 1);

            // Row 2: editor of the selected parameter (number boxes, colour swatch + picker).
            this.pnlValueEditor = new FlowLayoutPanel();
            this.pnlValueEditor.Dock = DockStyle.Fill;
            this.pnlValueEditor.AutoSize = true;
            this.pnlValueEditor.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.pnlValueEditor.MinimumSize = new Size(0, 44);
            this.pnlValueEditor.Padding = new Padding(4, 6, 4, 6);
            right.Controls.Add(this.pnlValueEditor, 0, 2);

            // Row 3: add / remove.
            FlowLayoutPanel edit = new FlowLayoutPanel();
            edit.Dock = DockStyle.Fill;
            edit.AutoSize = true;
            edit.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            edit.Padding = new Padding(4, 2, 4, 2);
            this.cmbAddParameter = new ComboBox();
            this.cmbAddParameter.DropDownStyle = ComboBoxStyle.DropDownList;
            this.cmbAddParameter.Width = 220;
            ForzaTheme.GreenItems(this.cmbAddParameter);
            this.cmbAddParameter.MaxDropDownItems = 24;
            this.btnAddParameter = new Button();
            this.btnAddParameter.Text = "Add Parameter";
            this.btnAddParameter.AutoSize = true;
            this.btnAddParameter.Click += delegate { this.AddShaderParameter(); };
            this.btnRemoveParameter = new Button();
            this.btnRemoveParameter.Text = "Remove Parameter";
            this.btnRemoveParameter.AutoSize = true;
            this.btnRemoveParameter.Click += delegate { this.RemoveShaderParameter(); };
            this.btnRemoveEntry = new Button();
            this.btnRemoveEntry.Text = "Remove All Settings";
            this.btnRemoveEntry.AutoSize = true;
            this.btnRemoveEntry.Click += delegate { this.RemoveShaderEntry(); };
            Label addLabel = new Label();
            addLabel.Text = "Add (green = the game's own cars set it):";
            addLabel.AutoSize = true;
            addLabel.Margin = new Padding(3, 7, 3, 3);
            edit.Controls.AddRange(new Control[] { addLabel, this.cmbAddParameter, this.btnAddParameter, this.btnRemoveParameter, this.btnRemoveEntry });
            right.Controls.Add(edit, 0, 3);

            // Row 4: copy missing values from another entry or car.
            FlowLayoutPanel copy = new FlowLayoutPanel();
            copy.Dock = DockStyle.Fill;
            copy.AutoSize = true;
            copy.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            copy.Padding = new Padding(4, 2, 4, 6);
            this.cmbCopyCar = new ComboBox();
            this.cmbCopyCar.DropDownStyle = ComboBoxStyle.DropDownList;
            this.cmbCopyCar.Width = 200;
            this.cmbCopyCar.MaxDropDownItems = 24;
            this.cmbCopyCar.SelectedIndexChanged += delegate { this.CopyCarChanged(); };
            this.cmbCopyFrom = new ComboBox();
            this.cmbCopyFrom.DropDownStyle = ComboBoxStyle.DropDownList;
            this.cmbCopyFrom.Width = 200;
            this.cmbCopyFrom.MaxDropDownItems = 24;
            this.cmbCopyFrom.SelectedIndexChanged += delegate { this.ShowCopyPreview(); };
            this.btnCopyFrom = new Button();
            this.btnCopyFrom.Text = "Copy Missing";
            this.btnCopyFrom.AutoSize = true;
            this.btnCopyFrom.Click += delegate { this.CopyShaderEntry(); };
            this.lblCopyPreview = new Label();
            this.lblCopyPreview.AutoSize = true;
            this.lblCopyPreview.MaximumSize = new Size(640, 0);
            this.lblCopyPreview.ForeColor = ForzaTheme.TextDim;
            this.lblCopyPreview.Margin = new Padding(3, 4, 3, 3);
            Label copyLabel = new Label();
            copyLabel.Text = "Copy from car / entry:";
            copyLabel.AutoSize = true;
            copyLabel.Margin = new Padding(3, 7, 3, 3);
            copy.Controls.AddRange(new Control[] { copyLabel, this.cmbCopyCar, this.cmbCopyFrom, this.btnCopyFrom, this.lblCopyPreview });
            right.Controls.Add(copy, 0, 4);

            split.Panel2.Controls.Add(right);

            this.pnlShader.Controls.Add(split);
            this.pnlShader.Controls.Add(bar);
        }

        // Editor of the selected parameter: one number box per attribute (colours: r g b a) and, for colours, a swatch
        // that opens the same picker as the car colours. Every change is stored at once (pending until Save).
        private void BuildValueEditor()
        {
            Control[] olds = new Control[this.pnlValueEditor.Controls.Count];
            this.pnlValueEditor.Controls.CopyTo(olds, 0);
            foreach (Control old in olds)
            {
                this.pnlValueEditor.Controls.Remove(old);
                old.Dispose();
            }
            if (this.lvParameters.SelectedItems.Count == 0)
            {
                return;
            }
            ShaderSettings.Parameter p = (ShaderSettings.Parameter) this.lvParameters.SelectedItems[0].Tag;
            List<string[]> attributes = ShaderSettings.Attributes(p.Xml);
            if (attributes.Count == 0)
            {
                Label none = new Label();
                none.Text = p.Name + " has no values to edit here: use Edit as XML.";
                none.AutoSize = true;
                this.pnlValueEditor.Controls.Add(none);
                return;
            }
            Label title = new Label();
            title.Text = p.Name;
            title.Font = ForzaTheme.Bold;
            title.AutoSize = true;
            title.Margin = new Padding(3, 6, 12, 3);
            this.pnlValueEditor.Controls.Add(title);
            bool colour = attributes.Count >= 3 && attributes[0][0] == "r" && attributes[1][0] == "g" && attributes[2][0] == "b";
            NumericUpDown[] boxes = new NumericUpDown[attributes.Count];
            Panel swatch = null;
            bool loading = true;
            for (int i = 0; i < attributes.Count; i++)
            {
                Label caption = new Label();
                caption.Text = attributes[i][0];
                caption.AutoSize = true;
                caption.Margin = new Padding(8, 6, 2, 3);
                NumericUpDown box = new NumericUpDown();
                box.DecimalPlaces = 3;
                box.Increment = colour ? 0.05m : 0.1m;
                box.Minimum = -100000m;
                box.Maximum = 100000m;
                box.Width = 78;
                box.BackColor = ForzaTheme.Raised;
                box.ForeColor = ForzaTheme.Text;
                decimal number;
                decimal.TryParse(attributes[i][1], NumberStyles.Float, CultureInfo.InvariantCulture, out number);
                box.Value = Math.Max(box.Minimum, Math.Min(box.Maximum, number));
                boxes[i] = box;
                this.pnlValueEditor.Controls.Add(caption);
                this.pnlValueEditor.Controls.Add(box);
            }
            if (colour)
            {
                swatch = new Panel();
                swatch.Size = new Size(46, 24);
                swatch.Margin = new Padding(14, 3, 3, 3);
                swatch.Cursor = Cursors.Hand;
                swatch.BorderStyle = BorderStyle.FixedSingle;
                this.pnlValueEditor.Controls.Add(swatch);
            }
            Action refresh = delegate
            {
                if (swatch != null)
                {
                    swatch.BackColor = SwatchColor(boxes);
                }
            };
            Action store = delegate
            {
                if (loading)
                {
                    return;
                }
                for (int i = 0; i < attributes.Count; i++)
                {
                    attributes[i][1] = boxes[i].Value.ToString("0.######", CultureInfo.InvariantCulture);
                }
                p.Xml = ShaderSettings.BuildXml(p.Name, attributes);
                this.StoreShaderValueLive(p);
                refresh();
            };
            foreach (NumericUpDown box in boxes)
            {
                box.ValueChanged += delegate { store(); };
            }
            if (swatch != null)
            {
                swatch.MouseClick += delegate
                {
                    Rectangle near = swatch.RectangleToScreen(swatch.ClientRectangle);
                    ForzaColorPicker.Popup(this, this.RectangleToClient(near), SwatchColor(boxes), delegate(Color picked)
                    {
                        boxes[0].Value = Math.Round((decimal) picked.R / 255m, 3);
                        boxes[1].Value = Math.Round((decimal) picked.G / 255m, 3);
                        boxes[2].Value = Math.Round((decimal) picked.B / 255m, 3);
                    });
                };
                refresh();
            }
            loading = false;
        }

        // The colour a r g b triple shows as (values above 1, as FH lamps use, are drawn at full).
        private static Color SwatchColor(NumericUpDown[] boxes)
        {
            Func<decimal, int> channel = delegate(decimal v) { return (int) Math.Round(255 * Math.Max(0m, Math.Min(1m, v))); };
            return Color.FromArgb(channel(boxes[0].Value), channel(boxes[1].Value), channel(boxes[2].Value));
        }

        // A value edited in the box: stored in the archive, the viewport and the sphere follow, the list row is renamed.
        private void StoreShaderValueLive(ShaderSettings.Parameter p)
        {
            this.zip.Replace(this.shaderEntry, this.shaderData.ToBytes());
            this.RefreshEntryNode(this.shaderEntry);
            this.ApplyShaderSettingsToMeshes();
            string technique = this.SelectedTechnique;
            if (technique != null)
            {
                this.UpdateShaderSphere(technique);
            }
            foreach (ListViewItem item in this.lvParameters.Items)
            {
                if (item.Tag == p)
                {
                    item.SubItems[1].Text = ShaderValueText(p);
                }
            }
            this.SetStatus((technique ?? "") + ": " + p.Name + " changed (pending: Save to write the archive)");
        }

        // "r 0.50  g 0.50  b 0.50  a 0.00" for colours, the number for a single value.
        private static string ShaderValueText(ShaderSettings.Parameter p)
        {
            List<string> values = new List<string>();
            foreach (string[] a in ShaderSettings.Attributes(p.Xml))
            {
                double number;
                string text = double.TryParse(a[1], NumberStyles.Float, CultureInfo.InvariantCulture, out number) ? number.ToString("0.###", CultureInfo.InvariantCulture) : a[1];
                values.Add((a[0] == "value") ? text : a[0] + " " + text);
            }
            return string.Join("   ", values.ToArray());
        }

        private void UpdateShaderSphere(string technique)
        {
            ShaderLookValues preview = ShaderLook.Look(ShaderLook.For(technique));
            if (preview.Kind == ShaderKind.Pass) { preview = ShaderLook.Look(ShaderKind.Generic); }
            ShaderLookValues applied = ShaderLook.Apply(preview, ShaderValues(this.shaderData, technique));
            this.sphereShader.SetLook(applied);
            this.UpdateSphereTexture(technique, applied);
        }

        // A label in front of a list in the button rows, the same width on both rows so the lists line up.
        private static Label ShaderButtonLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.Width = 150;
            label.TextAlign = ContentAlignment.MiddleRight;
            label.Margin = new Padding(3, 4, 3, 3);
            return label;
        }

        private void ShowShaderSettings(ForzaZip.Entry entry, byte[] data)
        {
            this.shaderEntry = entry;
            this.shaderData = ShaderSettings.Parse(data);
            string folder = entry.Name.Substring(0, entry.Name.LastIndexOf('/') + 1);
            this.shaderUsed = ShaderSettings.Techniques(this.zip, folder);
            this.shaderKnown = ShaderTextures.For(this.GameRoot).ParameterNames;
            this.FillCopyCars();
            this.FillShaderTechniques(this.pendingShaderTechnique);
            this.pendingShaderTechnique = null;
            this.ShowView(this.pnlShader);
        }

        private string SelectedTechnique
        {
            get { return (this.lvTechniques.SelectedItems.Count > 0) ? (string) this.lvTechniques.SelectedItems[0].Tag : null; }
        }

        // A name with the cockpit_ prefix, a leading z_ (Horizon draw-order marker), underscores and case dropped:
        // cockpit_gauge_emissive_alpha and cockpit_z_gauge_emissive_alpha compare equal.
        private static string LookalikeKey(string technique)
        {
            string key = technique.ToLowerInvariant();
            if (key.StartsWith("cockpit_"))
            {
                key = key.Substring(8);
            }
            if (key.StartsWith("z_"))
            {
                key = key.Substring(2);
            }
            return key.Replace("_", "");
        }

        // For an entry no model uses: the technique a model does use that looks like it (a typo or a missing z_ in the
        // car's file, e.g. the Alfa 8C's cockpit_gauge_emissive_alpha against cockpit_z_gauge_emissive_alpha), as a tooltip line.
        private string LookalikeHint(string technique)
        {
            string key = LookalikeKey(technique);
            List<string> twins = new List<string>();
            foreach (string used in this.shaderUsed.Keys)
            {
                if (used != technique && LookalikeKey(used) == key && this.shaderData.Find(used) == null)
                {
                    twins.Add(used);
                }
            }
            return (twins.Count == 0) ? "" : "\nA model uses " + string.Join(", ", twins.ToArray()) + ", which has no settings: the entry may have been meant for it (misspelt name).";
        }

        // M main, L lod0, C cockpit, B brakes.
        private string UsedIn(string technique)
        {
            List<string> files;
            if (!this.shaderUsed.TryGetValue(technique, out files))
            {
                return "not used";
            }
            string letters = "";
            foreach (string file in files)
            {
                string kind = file.EndsWith("_lod0.carbin", StringComparison.OrdinalIgnoreCase) && (file.Contains("caliper") || file.Contains("rotor")) ? "B"
                    : file.EndsWith("_lod0.carbin", StringComparison.OrdinalIgnoreCase) ? "L"
                    : file.EndsWith("_cockpit.carbin", StringComparison.OrdinalIgnoreCase) ? "C" : "M";
                if (!letters.Contains(kind))
                {
                    letters += kind;
                }
            }
            // Spelled out for the lists: Main = the car model (LODs 1-5), LOD0 = the close-up file, Cockpit, Brakes.
            List<string> words = new List<string>();
            foreach (char letter in letters)
            {
                words.Add(letter == 'M' ? "Main" : letter == 'L' ? "LOD0" : letter == 'C' ? "Cockpit" : "Brakes");
            }
            return string.Join(", ", words.ToArray());
        }

        // The game whose cars' settings are the reference: the shown model's (ShaderProfiles.Detect), else the game
        // folder the archive is in, else FM4.
        private ShaderProfile SettingsProfile
        {
            get
            {
                if (this.shaderProfile != ShaderProfile.Unknown)
                {
                    return this.shaderProfile;
                }
                ShaderProfile fromFolder = (this.GameRoot != null) ? ShaderProfiles.ForGame(GameInfo.Detect(this.GameRoot)) : ShaderProfile.Unknown;
                return (fromFolder != ShaderProfile.Unknown) ? fromFolder : ShaderProfile.Fm4;
            }
        }

        private string SettingsGame
        {
            get { return ShaderProfiles.Tag(this.SettingsProfile); }
        }

        private int ErrorsOf(ShaderSettings.Entry entry)
        {
            int errors = 0;
            foreach (ShaderSettings.Parameter p in entry.Parameters)
            {
                errors += (ShaderSettings.Check(this.SettingsProfile, entry.Name, p, this.shaderKnown) == ShaderSettings.Problem.UnknownParameter) ? 1 : 0;
            }
            return errors;
        }

        private void FillShaderTechniques(string select)
        {
            select = select ?? this.SelectedTechnique;
            this.lvTechniques.BeginUpdate();
            this.lvTechniques.Items.Clear();
            this.lvTechniques.Groups.Clear();
            ListViewGroup withSettings = new ListViewGroup("With settings");
            ListViewGroup defaults = new ListViewGroup("No settings (shader defaults)");
            this.lvTechniques.Groups.Add(withSettings);
            this.lvTechniques.Groups.Add(defaults);
            int errors = 0;
            int unused = 0;
            foreach (ShaderSettings.Entry entry in this.shaderData.Entries)
            {
                ListViewItem item = new ListViewItem(new string[] { entry.Name, this.UsedIn(entry.Name) }, withSettings);
                item.Tag = entry.Name;
                int bad = this.ErrorsOf(entry);
                if (bad > 0)
                {
                    errors += bad;
                    item.ForeColor = ForzaTheme.Red;
                    item.ToolTipText = bad + " parameter(s) " + this.SettingsGame + " does not use on " + entry.Name;
                }
                else if (!this.shaderUsed.ContainsKey(entry.Name))
                {
                    unused++;
                    item.ForeColor = ForzaTheme.Yellow;
                    item.ToolTipText = "No model of the car uses " + entry.Name + ": the game ignores it (harmless)." + this.LookalikeHint(entry.Name);
                }
                this.lvTechniques.Items.Add(item);
            }
            int noSettings = 0;
            foreach (KeyValuePair<string, List<string>> used in this.shaderUsed)
            {
                if (this.shaderData.Find(used.Key) != null)
                {
                    continue;
                }
                noSettings++;
                ListViewItem item = new ListViewItem(new string[] { used.Key, this.UsedIn(used.Key) }, defaults);
                item.Tag = used.Key;
                item.ForeColor = ForzaTheme.TextDim;
                item.ToolTipText = string.Join(", ", used.Value.ToArray());
                this.lvTechniques.Items.Add(item);
            }
            this.lvTechniques.EndUpdate();
            this.lblShaderSummary.Text = string.Format("{0} with settings, {1} without (defaults), {2} unused, {3} error{4}",
                this.shaderData.Entries.Count, noSettings, unused, errors, (errors == 1) ? "" : "s");
            this.lblShaderSummary.ForeColor = (errors > 0) ? ForzaTheme.Red : ForzaTheme.Text;
            foreach (ListViewItem item in this.lvTechniques.Items)
            {
                if ((string) item.Tag == select)
                {
                    item.Selected = true;
                    item.EnsureVisible();
                }
            }
            this.FillShaderParameters();
        }

        private void FillShaderParameters()
        {
            string technique = this.SelectedTechnique;
            this.lvParameters.BeginUpdate();
            this.lvParameters.Items.Clear();
            this.cmbAddParameter.Items.Clear();
            this.cmbCopyFrom.Items.Clear();
            ShaderSettings.Entry entry = (technique != null) ? this.shaderData.Find(technique) : null;
            if (technique == null)
            {
                this.lblShaderTechnique.Text = "Pick a technique on the left.";
                this.sphereShader.Clear();
            }
            else
            {
                string[] fm4 = ShaderProfiles.ParametersOf(this.SettingsProfile, technique);
                this.lblShaderTechnique.Text = string.Format("{0}  -  used in: {1}{2}\n{4} cars set on it: {3}", technique, this.UsedIn(technique),
                    (entry == null) ? "  -  no settings: shader defaults" : "",
                    (fm4 != null) ? string.Join(", ", fm4) : "nothing (no " + this.SettingsGame + " car lists it)", this.SettingsGame);
                this.UpdateShaderSphere(technique);
                if (entry != null)
                {
                    foreach (ShaderSettings.Parameter p in entry.Parameters)
                    {
                        ShaderSettings.Problem problem = ShaderSettings.Check(this.SettingsProfile, technique, p, this.shaderKnown);
                        string note = (problem == ShaderSettings.Problem.UnknownParameter) ? "ERROR: no shader of the game has this parameter"
                            : (problem == ShaderSettings.Problem.UnusualParameter) ? "unusual: no " + this.SettingsGame + " car sets it on " + technique + " (not an error)" : "";
                        ListViewItem item = new ListViewItem(new string[] { p.Name, ShaderValueText(p), note });
                        item.Tag = p;
                        if (problem == ShaderSettings.Problem.UnknownParameter)
                        {
                            item.ForeColor = ForzaTheme.Red;
                        }
                        else if (problem == ShaderSettings.Problem.UnusualParameter)
                        {
                            item.ForeColor = ForzaTheme.Yellow;
                        }
                        this.lvParameters.Items.Add(item);
                    }
                }
                // Add: green = FM4 cars set it on this technique, first; then the other FM4 parameters.
                List<string> names = new List<string>(CarPort.ShaderParameterNames);
                foreach (string known in this.shaderKnown ?? new string[0])
                {
                    if (known.Length > 0 && char.IsUpper(known[0]) && !names.Contains(known))
                    {
                        names.Add(known);
                    }
                }
                names.Sort(StringComparer.Ordinal);
                foreach (bool starred in new bool[] { true, false })
                {
                    foreach (string name in names)
                    {
                        bool seen = fm4 != null && Array.IndexOf(fm4, name) >= 0;
                        if (seen != starred || ShaderSettings.IsSampler(name) || (entry != null && entry.Parameters.Exists(delegate(ShaderSettings.Parameter x) { return x.Name == name; })))
                        {
                            continue;
                        }
                        this.cmbAddParameter.Items.Add(new ParameterChoice(name, starred));
                    }
                }
                if (this.cmbAddParameter.Items.Count > 0)
                {
                    this.cmbAddParameter.SelectedIndex = 0;
                }
            }
            this.lvParameters.EndUpdate();
            this.FillCopyFrom();
        }

        // The cars "Copy missing from" can read: this file, the cars of Media\cars of the game folder the
        // open file is in, cars picked before with Browse..., and Browse... itself.
        private void FillCopyCars()
        {
            string keep = (this.cmbCopyCar.SelectedItem as string) ?? CopyThisFile;
            string root = this.GameRoot;
            string cars = (root == null) ? null : Path.Combine(Path.Combine(root, "Media"), "cars");
            if (cars != null && Directory.Exists(cars))
            {
                foreach (string path in Directory.GetFiles(cars, "*.zip"))
                {
                    this.copyCarPaths[Path.GetFileNameWithoutExtension(path)] = path;
                }
            }
            List<string> names = new List<string>(this.copyCarPaths.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            this.fillingCopyCars = true;
            this.cmbCopyCar.Items.Clear();
            this.cmbCopyCar.Items.Add(CopyThisFile);
            foreach (string name in names)
            {
                this.cmbCopyCar.Items.Add(name);
            }
            this.cmbCopyCar.Items.Add(CopyBrowse);
            int index = this.cmbCopyCar.Items.IndexOf(keep);
            this.cmbCopyCar.SelectedIndex = (index >= 0 && keep != CopyBrowse) ? index : 0;
            this.fillingCopyCars = false;
            this.LoadCopySource();
        }

        private void CopyCarChanged()
        {
            if (this.fillingCopyCars)
            {
                return;
            }
            if ((this.cmbCopyCar.SelectedItem as string) == CopyBrowse)
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = "Car to read shadersettings.xml from";
                    dialog.Filter = "Car zip (*.zip)|*.zip|All files (*.*)|*.*";
                    string picked = null;
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        picked = Path.GetFileNameWithoutExtension(dialog.FileName);
                        this.copyCarPaths[picked] = dialog.FileName;
                    }
                    this.fillingCopyCars = true;
                    this.cmbCopyCar.SelectedItem = CopyThisFile;
                    this.fillingCopyCars = false;
                    this.FillCopyCars();
                    if (picked != null)
                    {
                        this.cmbCopyCar.SelectedItem = picked;
                    }
                    else
                    {
                        this.FillCopyFrom();
                    }
                }
                return;
            }
            this.LoadCopySource();
            this.FillCopyFrom();
        }

        // Reads the chosen car's shadersettings.xml (any folder in the zip), or uses this file.
        private void LoadCopySource()
        {
            string car = this.cmbCopyCar.SelectedItem as string;
            this.copySource = null;
            this.copySourceName = null;
            if (car == null || car == CopyThisFile || !this.copyCarPaths.ContainsKey(car))
            {
                return;
            }
            this.copySourceName = car;
            try
            {
                ForzaZip other = GamePaths.OpenZip(this.copyCarPaths[car]);
                ForzaZip.Entry entry = other.Entries.Find(delegate(ForzaZip.Entry e) { return IsShaderSettings(e); });
                this.copySource = (entry == null) ? new ShaderSettings() : ShaderSettings.Parse(other.Read(entry));
            }
            catch (Exception exception)
            {
                this.copySource = new ShaderSettings();
                this.SetStatus(car + ": " + exception.Message);
            }
        }

        // The entries with settings of the chosen source; the selected technique's own name first.
        private void FillCopyFrom()
        {
            string technique = this.SelectedTechnique;
            ShaderSettings source = this.copySource ?? this.shaderData;
            this.cmbCopyFrom.Items.Clear();
            if (technique != null)
            {
                List<string> names = new List<string>();
                foreach (ShaderSettings.Entry other in source.Entries)
                {
                    if (other.Parameters.Count > 0 && (this.copySource != null || other.Name != technique))
                    {
                        names.Add(other.Name);
                    }
                }
                names.Sort(StringComparer.OrdinalIgnoreCase);
                if (names.Remove(technique))
                {
                    names.Insert(0, technique);
                }
                foreach (string name in names)
                {
                    this.cmbCopyFrom.Items.Add(name);
                }
                if (this.cmbCopyFrom.Items.Count > 0)
                {
                    this.cmbCopyFrom.SelectedIndex = 0;
                }
            }
            this.ShowCopyPreview();
            this.EnableShaderButtons();
        }

        // The values of the entry about to be copied, so another car's settings can be read before copying.
        private void ShowCopyPreview()
        {
            ShaderSettings source = this.copySource ?? this.shaderData;
            string where = (this.copySourceName != null) ? this.copySourceName : "this file";
            ShaderSettings.Entry from = (this.cmbCopyFrom.SelectedItem != null) ? source.Find((string) this.cmbCopyFrom.SelectedItem) : null;
            if (this.SelectedTechnique == null)
            {
                this.lblCopyPreview.Text = "";
            }
            else if (from == null)
            {
                this.lblCopyPreview.Text = (this.copySourceName != null && source.Entries.Count == 0)
                    ? where + " has no shadersettings.xml (shader defaults)." : "Nothing to copy from " + where + ".";
            }
            else
            {
                List<string> lines = new List<string>();
                foreach (ShaderSettings.Parameter p in from.Parameters)
                {
                    List<string> values = new List<string>();
                    foreach (string[] a in ShaderSettings.Attributes(p.Xml))
                    {
                        values.Add((a[0] == "value") ? a[1] : a[0] + " " + a[1]);
                    }
                    lines.Add(p.Name + " " + string.Join(" ", values.ToArray()));
                }
                this.lblCopyPreview.Text = from.Name + " in " + where + ": " + string.Join(";  ", lines.ToArray());
            }
        }

        private void EnableShaderButtons()
        {
            bool technique = this.SelectedTechnique != null;
            bool entry = technique && this.shaderData.Find(this.SelectedTechnique) != null;
            bool parameter = this.lvParameters.SelectedItems.Count > 0;
            this.cmbAddParameter.Enabled = technique;
            this.btnAddParameter.Enabled = technique && this.cmbAddParameter.Items.Count > 0;
            this.btnRemoveParameter.Enabled = parameter;
            this.cmbCopyFrom.Enabled = technique && this.cmbCopyFrom.Items.Count > 0;
            this.btnCopyFrom.Enabled = this.cmbCopyFrom.Enabled;
            this.btnRemoveEntry.Enabled = entry;
        }

        // Stores the edited settings in the archive (pending until Save) and refreshes the lists.
        private void StoreShaderSettings(string status)
        {
            this.zip.Replace(this.shaderEntry, this.shaderData.ToBytes());
            this.RefreshEntryNode(this.shaderEntry);
            this.ApplyShaderSettingsToMeshes();
            this.FillShaderTechniques(null);
            this.SetStatus(status + " (pending: Save to write the archive)");
        }

        // An entry of the add list: green when FM4 cars set the parameter on this technique.
        private class ParameterChoice : IRecommended
        {
            private readonly string name;
            private readonly bool fm4;

            public ParameterChoice(string name, bool fm4)
            {
                this.name = name;
                this.fm4 = fm4;
            }

            public bool Recommended
            {
                get { return this.fm4; }
            }

            public override string ToString()
            {
                return this.name;
            }
        }

        // Adds the chosen parameter with every value 0 (nothing is made up) and selects it: its values are set
        // in the editor under the list.
        private void AddShaderParameter()
        {
            string technique = this.SelectedTechnique;
            if (technique == null || this.cmbAddParameter.SelectedItem == null)
            {
                return;
            }
            string name = this.cmbAddParameter.SelectedItem.ToString();
            List<string[]> attributes = new List<string[]>();
            foreach (string a in ShaderSettings.AttributeNames(name))
            {
                attributes.Add(new string[] { a, "0" });
            }
            ShaderSettings.Entry entry = this.shaderData.Find(technique);
            if (entry == null)
            {
                entry = new ShaderSettings.Entry();
                entry.Name = technique;
                this.shaderData.Entries.Add(entry);
            }
            entry.Parameters.Add(new ShaderSettings.Parameter { Name = name, Xml = ShaderSettings.BuildXml(name, attributes) });
            this.StoreShaderSettings(technique + ": " + name + " added, set its value below");
            foreach (ListViewItem item in this.lvParameters.Items)
            {
                if (((ShaderSettings.Parameter) item.Tag).Name == name)
                {
                    item.Selected = true;
                    item.EnsureVisible();
                }
            }
        }

        private void RemoveShaderParameter()
        {
            string technique = this.SelectedTechnique;
            ShaderSettings.Entry entry = (technique != null) ? this.shaderData.Find(technique) : null;
            if (entry == null || this.lvParameters.SelectedItems.Count == 0)
            {
                return;
            }
            ShaderSettings.Parameter p = (ShaderSettings.Parameter) this.lvParameters.SelectedItems[0].Tag;
            entry.Parameters.Remove(p);
            if (entry.Parameters.Count == 0)
            {
                this.shaderData.Entries.Remove(entry);   // no empty elements
            }
            this.StoreShaderSettings(technique + ": " + p.Name + " removed");
        }

        // The parameters of another entry that this technique does not set yet (its own stay).
        private void CopyShaderEntry()
        {
            string technique = this.SelectedTechnique;
            ShaderSettings source = this.copySource ?? this.shaderData;
            ShaderSettings.Entry from = (this.cmbCopyFrom.SelectedItem != null) ? source.Find((string) this.cmbCopyFrom.SelectedItem) : null;
            if (technique == null || from == null || (this.copySource == null && from.Name == technique))
            {
                return;
            }
            ShaderSettings.Entry entry = this.shaderData.Find(technique);
            if (entry == null)
            {
                entry = new ShaderSettings.Entry();
                entry.Name = technique;
                this.shaderData.Entries.Add(entry);
            }
            int added = 0;
            foreach (ShaderSettings.Parameter p in from.Parameters)
            {
                if (!entry.Parameters.Exists(delegate(ShaderSettings.Parameter x) { return x.Name == p.Name; }))
                {
                    entry.Parameters.Add(new ShaderSettings.Parameter { Name = p.Name, Xml = p.Xml });
                    added++;
                }
            }
            if (entry.Parameters.Count == 0)
            {
                this.shaderData.Entries.Remove(entry);
            }
            this.StoreShaderSettings(string.Format("{0}: {1} parameter(s) copied from {2}{3}", technique, added, from.Name,
                (this.copySourceName != null) ? " of " + this.copySourceName : ""));
        }

        private void RemoveShaderEntry()
        {
            string technique = this.SelectedTechnique;
            ShaderSettings.Entry entry = (technique != null) ? this.shaderData.Find(technique) : null;
            if (entry == null)
            {
                return;
            }
            if (MessageBox.Show(this, "Remove all settings of " + technique + "? It then runs with its shader's defaults.", "Shader Settings", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            this.shaderData.Entries.Remove(entry);
            this.StoreShaderSettings(technique + ": settings removed");
        }

        private void RemoveUnusedShaderEntries()
        {
            List<string> removed = new List<string>();
            foreach (ShaderSettings.Entry entry in new List<ShaderSettings.Entry>(this.shaderData.Entries))
            {
                if (!this.shaderUsed.ContainsKey(entry.Name))
                {
                    this.shaderData.Entries.Remove(entry);
                    removed.Add(entry.Name);
                }
            }
            if (removed.Count == 0)
            {
                this.SetStatus("No unused entries.");
                return;
            }
            this.StoreShaderSettings("Removed unused: " + string.Join(", ", removed.ToArray()));
        }
    }
}
