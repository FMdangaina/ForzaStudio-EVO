namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Windows.Forms;

    // Model view side tab SHADER: the shadersettings.xml entry of the selected part's material (technique), edited
    // next to the 3D view. Every change goes into the archive at once (pending until File > Save) and the view
    // redraws with it (ShaderLook.Apply), so a glass tint, a lamp colour, a specular value... is seen on the car while
    // it is set. A car without shadersettings.xml gets one when the first parameter is added. The settings belong to
    // the technique: every part with the same material changes. The Shader Settings view of the file stays as it is
    // (Open In Shader Settings goes there).
    public partial class ArchiveEditorForm
    {
        private TabPage tabSideShader;
        private Label lblShdInfo;
        private ComboBox cmbShdTechnique;
        private ListView lvShdParams;
        private FlowLayoutPanel pnlShdValues;
        private ComboBox cmbShdAdd;
        private Button btnShdAdd;
        private Button btnShdRemove;
        private Button btnShdRemoveAll;
        private ComboBox cmbShdCar;
        private Button btnShdCopy;
        private Button btnShdMaterial;
        private bool fillingShd;
        private readonly Dictionary<string, string> shdCarPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private void AddShaderTabControls(FlowLayoutPanel side)
        {
            this.tabSideShader = (TabPage) side.Parent;

            this.lblShdInfo = SideNote("Select a part in the tree.");
            this.lblShdInfo.ForeColor = this.ForeColor;

            this.cmbShdTechnique = new ComboBox();
            this.cmbShdTechnique.DropDownStyle = ComboBoxStyle.DropDownList;
            this.cmbShdTechnique.Width = SideWidth;
            this.cmbShdTechnique.SelectedIndexChanged += delegate
            {
                if (!this.fillingShd)
                {
                    this.FillShaderTabParameters(null);
                }
            };

            this.lvShdParams = new ListView();
            this.lvShdParams.View = View.Details;
            this.lvShdParams.FullRowSelect = true;
            this.lvShdParams.HideSelection = false;
            this.lvShdParams.MultiSelect = false;
            this.lvShdParams.ShowItemToolTips = true;
            this.lvShdParams.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            this.lvShdParams.Size = new Size(SideWidth, 150);
            this.lvShdParams.Columns.Add("Parameter", 110);
            this.lvShdParams.Columns.Add("Value", SideWidth - 114);
            this.lvShdParams.SelectedIndexChanged += delegate
            {
                if (!this.fillingShd)
                {
                    this.BuildShaderTabValueEditor();
                    this.EnableShaderTabButtons();
                }
            };

            this.pnlShdValues = new FlowLayoutPanel();
            this.pnlShdValues.FlowDirection = FlowDirection.TopDown;
            this.pnlShdValues.WrapContents = false;
            this.pnlShdValues.AutoSize = true;
            this.pnlShdValues.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.pnlShdValues.MinimumSize = new Size(SideWidth, 10);
            this.pnlShdValues.Margin = new Padding(0, 4, 0, 4);

            this.cmbShdAdd = new ComboBox();
            this.cmbShdAdd.DropDownStyle = ComboBoxStyle.DropDownList;
            this.cmbShdAdd.Width = SideWidth;
            this.cmbShdAdd.MaxDropDownItems = 24;
            this.cmbShdAdd.DropDownWidth = 260;
            ForzaTheme.GreenItems(this.cmbShdAdd);
            this.btnShdAdd = new Button();
            this.btnShdAdd.Text = "Add";
            this.btnShdAdd.Click += delegate { this.AddShaderTabParameter(); };
            this.btnShdRemove = new Button();
            this.btnShdRemove.Text = "Remove";
            this.btnShdRemove.Click += delegate { this.RemoveShaderTabParameter(); };
            this.btnShdRemoveAll = new Button();
            this.btnShdRemoveAll.Text = "Remove All";
            this.btnShdRemoveAll.Click += delegate { this.RemoveShaderTabEntry(); };

            this.cmbShdCar = new ComboBox();
            this.cmbShdCar.DropDownStyle = ComboBoxStyle.DropDownList;
            this.cmbShdCar.Width = SideWidth;
            this.cmbShdCar.MaxDropDownItems = 24;
            this.cmbShdCar.DropDownWidth = 300;
            this.btnShdCopy = SideWideButton("Copy Missing From Car", delegate { this.CopyShaderTabFromCar(); });

            this.btnShdMaterial = SideWideButton("Change Material...", delegate { this.RenameSelectedMaterial(); this.FillShaderTab(); });
            Button open = SideWideButton("Open In Shader Settings", delegate { this.OpenShaderSettingsOfSelection(); });

            side.Controls.Add(SideHeading("Material", true));
            side.Controls.Add(this.lblShdInfo);
            side.Controls.Add(this.cmbShdTechnique);
            side.Controls.Add(this.btnShdMaterial);
            side.Controls.Add(SideHeading("Settings", false));
            side.Controls.Add(this.lvShdParams);
            side.Controls.Add(this.pnlShdValues);
            side.Controls.Add(SideCaption("Add parameter (green = the game's cars set it)"));
            side.Controls.Add(this.cmbShdAdd);
            side.Controls.Add(SideButtonRow(this.btnShdAdd, this.btnShdRemove, this.btnShdRemoveAll));
            side.Controls.Add(SideHeading("From another car", false));
            side.Controls.Add(this.cmbShdCar);
            side.Controls.Add(this.btnShdCopy);
            side.Controls.Add(open);
            side.Controls.Add(SideNote("Changes are drawn in the view at once and stored in the archive (pending until File > Save). Settings belong to the material: every part with it changes. Without settings a material runs with its shader's defaults."));

            this.tvModel.AfterSelect += delegate { this.FillShaderTab(); };
            this.tabSide.SelectedIndexChanged += delegate
            {
                if (this.tabSide.SelectedTab == this.tabSideShader)
                {
                    this.FillShaderTabCars();
                    this.FillShaderTab();
                }
            };
        }

        private bool ShaderTabOpen
        {
            get { return this.tabSide != null && this.tabSide.SelectedTab == this.tabSideShader && this.pnlModel.Visible; }
        }

        private ForzaZip.Entry ShaderTabEntry
        {
            get { return (this.zip == null) ? null : this.zip.Find(this.ModelFolder + "shadersettings.xml"); }
        }

        // The car's settings as they are in the archive now (also after edits in the Shader Settings view); empty when it has none.
        private ShaderSettings ReadShaderTabSettings()
        {
            ForzaZip.Entry entry = this.ShaderTabEntry;
            if (entry == null)
            {
                return new ShaderSettings();
            }
            try
            {
                return ShaderSettings.Parse(this.zip.Read(entry));
            }
            catch (Exception exception)
            {
                this.SetStatus("shadersettings.xml cannot be read: " + exception.Message);
                return null;
            }
        }

        private string ShaderTabTechnique
        {
            get { return this.cmbShdTechnique.SelectedItem as string; }
        }

        // The materials of the selected parts (one, or every one of a part / LOD node).
        private void FillShaderTab()
        {
            if (!this.ShaderTabOpen)
            {
                return;
            }
            string keep = this.ShaderTabTechnique;
            List<string> techniques = new List<string>();
            foreach (ForzaMesh mesh in this.SelectedMeshes)
            {
                string technique = mesh.Technique ?? mesh.MaterialName;
                if (!string.IsNullOrEmpty(technique) && !techniques.Contains(technique))
                {
                    techniques.Add(technique);
                }
            }
            techniques.Sort(StringComparer.OrdinalIgnoreCase);
            this.fillingShd = true;
            this.cmbShdTechnique.Items.Clear();
            foreach (string technique in techniques)
            {
                this.cmbShdTechnique.Items.Add(technique);
            }
            int index = (keep != null) ? techniques.IndexOf(keep) : -1;
            this.cmbShdTechnique.SelectedIndex = (techniques.Count == 0) ? -1 : Math.Max(0, index);
            this.cmbShdTechnique.Visible = techniques.Count > 1;
            this.fillingShd = false;
            this.btnShdMaterial.Enabled = this.tvModel.SelectedNode != null && this.tvModel.SelectedNode.Tag is ForzaMesh;
            this.FillShaderTabParameters(null);
        }

        private void FillShaderTabParameters(string select)
        {
            string technique = this.ShaderTabTechnique;
            ShaderSettings settings = (technique != null) ? this.ReadShaderTabSettings() : null;
            ShaderSettings.Entry entry = (settings != null) ? settings.Find(technique) : null;
            if (select == null && this.lvShdParams.SelectedItems.Count > 0)
            {
                select = ((ShaderSettings.Parameter) this.lvShdParams.SelectedItems[0].Tag).Name;
            }
            this.fillingShd = true;
            this.lvShdParams.BeginUpdate();
            this.lvShdParams.Items.Clear();
            this.cmbShdAdd.Items.Clear();
            ICollection<string> known = ShaderTextures.For(this.GameRoot).ParameterNames;
            if (technique == null)
            {
                this.lblShdInfo.Text = "Select a part (or a part / LOD node) in the tree.";
            }
            else
            {
                int parts = 0;
                foreach (ForzaMesh mesh in GetMeshes(this.tvModel.Nodes, false))
                {
                    parts += ((mesh.Technique ?? mesh.MaterialName) == technique) ? 1 : 0;
                }
                string[] usual = ShaderProfiles.ParametersOf(this.SettingsProfile, technique);
                this.lblShdInfo.Text = technique + "\n" + parts + " part(s) of this model use it. "
                    + ((settings == null) ? "shadersettings.xml cannot be read."
                        : (this.ShaderTabEntry == null) ? "The car has no shadersettings.xml (made when a parameter is added)."
                        : (entry == null) ? "No settings: shader defaults." : "")
                    + "\n" + this.SettingsGame + " cars set: " + ((usual != null && usual.Length > 0) ? string.Join(", ", usual) : "nothing");
                if (entry != null)
                {
                    foreach (ShaderSettings.Parameter p in entry.Parameters)
                    {
                        ShaderSettings.Problem problem = ShaderSettings.Check(this.SettingsProfile, technique, p, known);
                        ListViewItem item = new ListViewItem(new string[] { p.Name, ShaderValueText(p) });
                        item.Tag = p;
                        if (problem == ShaderSettings.Problem.UnknownParameter)
                        {
                            item.ForeColor = ForzaTheme.Red;
                            item.ToolTipText = "ERROR: no shader of the game has this parameter";
                        }
                        else if (problem == ShaderSettings.Problem.UnusualParameter)
                        {
                            item.ForeColor = ForzaTheme.Yellow;
                            item.ToolTipText = "Unusual: no " + this.SettingsGame + " car sets it on " + technique + " (not an error)";
                        }
                        this.lvShdParams.Items.Add(item);
                        if (p.Name == select)
                        {
                            item.Selected = true;
                        }
                    }
                }
                // Add list: the parameters the game's cars set on this technique first (green), then the others the shaders know.
                if (settings != null)
                {
                    List<string> names = new List<string>(CarPort.ShaderParameterNames);
                    foreach (string name in known ?? new string[0])
                    {
                        if (name.Length > 0 && char.IsUpper(name[0]) && !names.Contains(name))
                        {
                            names.Add(name);
                        }
                    }
                    names.Sort(StringComparer.Ordinal);
                    foreach (bool starred in new bool[] { true, false })
                    {
                        foreach (string name in names)
                        {
                            bool seen = usual != null && Array.IndexOf(usual, name) >= 0;
                            if (seen != starred || ShaderSettings.IsSampler(name) || (entry != null && entry.Parameters.Exists(delegate(ShaderSettings.Parameter x) { return x.Name == name; })))
                            {
                                continue;
                            }
                            this.cmbShdAdd.Items.Add(new ParameterChoice(name, starred));
                        }
                    }
                    if (this.cmbShdAdd.Items.Count > 0)
                    {
                        this.cmbShdAdd.SelectedIndex = 0;
                    }
                }
            }
            this.lvShdParams.EndUpdate();
            this.fillingShd = false;
            this.BuildShaderTabValueEditor();
            this.EnableShaderTabButtons();
        }

        private void EnableShaderTabButtons()
        {
            bool technique = this.ShaderTabTechnique != null;
            bool any = this.lvShdParams.Items.Count > 0;
            this.cmbShdAdd.Enabled = technique && this.cmbShdAdd.Items.Count > 0;
            this.btnShdAdd.Enabled = this.cmbShdAdd.Enabled;
            this.btnShdRemove.Enabled = this.lvShdParams.SelectedItems.Count > 0;
            this.btnShdRemoveAll.Enabled = any;
            this.cmbShdCar.Enabled = technique && this.cmbShdCar.Items.Count > 0;
            this.btnShdCopy.Enabled = this.cmbShdCar.Enabled && this.cmbShdCar.SelectedIndex >= 0;
        }

        // Editor of the selected parameter: per value a slider (colours 0..1, other values 0..2, wider
        // values in the number box) and a number box, plus the colour picker for colours.
        private void BuildShaderTabValueEditor()
        {
            Control[] olds = new Control[this.pnlShdValues.Controls.Count];
            this.pnlShdValues.Controls.CopyTo(olds, 0);
            foreach (Control old in olds)
            {
                this.pnlShdValues.Controls.Remove(old);
                old.Dispose();
            }
            if (this.lvShdParams.SelectedItems.Count == 0)
            {
                return;
            }
            ShaderSettings.Parameter p = (ShaderSettings.Parameter) this.lvShdParams.SelectedItems[0].Tag;
            List<string[]> attributes = ShaderSettings.Attributes(p.Xml);
            if (attributes.Count == 0)
            {
                this.pnlShdValues.Controls.Add(SideNote(p.Name + " has no values to edit here: use Open In Shader Settings > Edit as XML."));
                return;
            }
            bool colour = attributes.Count >= 3 && attributes[0][0] == "r" && attributes[1][0] == "g" && attributes[2][0] == "b";
            NumericUpDown[] boxes = new NumericUpDown[attributes.Count];
            TrackBar[] sliders = new TrackBar[attributes.Count];
            bool loading = true;
            bool syncing = false;
            Panel swatch = null;
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
                if (swatch != null)
                {
                    swatch.BackColor = SwatchColor(boxes);
                }
                this.StoreShaderTabValue(p.Name, ShaderSettings.BuildXml(p.Name, attributes));
            };
            for (int i = 0; i < attributes.Count; i++)
            {
                int at = i;
                FlowLayoutPanel row = new FlowLayoutPanel();
                row.Size = new Size(SideWidth + 4, 30);
                row.Margin = new Padding(0);
                Label caption = new Label();
                caption.Text = attributes[i][0];
                caption.AutoSize = false;
                caption.Size = new Size(38, 24);
                caption.TextAlign = ContentAlignment.MiddleLeft;
                caption.Margin = new Padding(0, 3, 2, 0);
                NumericUpDown box = new NumericUpDown();
                box.DecimalPlaces = 3;
                box.Increment = 0.01m;
                box.Minimum = -100000m;
                box.Maximum = 100000m;
                box.Width = 70;
                box.Margin = new Padding(0, 3, 2, 0);
                decimal number;
                decimal.TryParse(attributes[i][1], NumberStyles.Float, CultureInfo.InvariantCulture, out number);
                box.Value = Math.Max(box.Minimum, Math.Min(box.Maximum, number));
                decimal top = colour ? 1m : 2m;
                TrackBar slider = new TrackBar();
                slider.Minimum = 0;
                slider.Maximum = 1000;
                slider.TickStyle = TickStyle.None;
                slider.AutoSize = false;
                slider.Size = new Size(SideWidth - 38 - 70 - 6, 26);
                slider.Margin = new Padding(0, 2, 0, 0);
                slider.Value = (int) Math.Round(1000m * Math.Max(0m, Math.Min(top, box.Value)) / top);
                slider.ValueChanged += delegate
                {
                    if (!syncing)
                    {
                        syncing = true;
                        boxes[at].Value = Math.Round(top * slider.Value / 1000m, 3);
                        syncing = false;
                    }
                };
                box.ValueChanged += delegate
                {
                    if (!syncing)
                    {
                        syncing = true;
                        sliders[at].Value = (int) Math.Round(1000m * Math.Max(0m, Math.Min(top, boxes[at].Value)) / top);
                        syncing = false;
                    }
                    store();
                };
                boxes[i] = box;
                sliders[i] = slider;
                row.Controls.Add(caption);
                row.Controls.Add(box);
                row.Controls.Add(slider);
                this.pnlShdValues.Controls.Add(row);
            }
            if (colour)
            {
                swatch = new Panel();
                swatch.Size = new Size(SideWidth, 22);
                swatch.Margin = new Padding(0, 4, 0, 2);
                swatch.Cursor = Cursors.Hand;
                swatch.BorderStyle = BorderStyle.FixedSingle;
                swatch.BackColor = SwatchColor(boxes);
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
                this.pnlShdValues.Controls.Add(swatch);
                this.pnlShdValues.Controls.Add(SideNote("Click the colour to pick it."));
            }
            loading = false;
        }

        // Writes the archive's shadersettings.xml (made when the car has none), then the view redraws with it.
        private void WriteShaderTabSettings(ShaderSettings settings)
        {
            ForzaZip.Entry entry = this.ShaderTabEntry;
            if (entry != null)
            {
                this.zip.Replace(entry, settings.ToBytes());
                this.RefreshEntryNode(entry);
            }
            else
            {
                this.zip.Add(this.ModelFolder + "shadersettings.xml", settings.ToBytes());
                this.PopulateTree();
                this.UpdateTitle();
            }
            this.ApplyShaderSettingsToMeshes();
            if (this.scene != null)
            {
                this.scene.Update(true);
            }
        }

        // A value moved in the editor: only that parameter's line changes (the list is not rebuilt, so the slider keeps the mouse).
        private void StoreShaderTabValue(string name, string xml)
        {
            string technique = this.ShaderTabTechnique;
            ShaderSettings settings = (technique != null) ? this.ReadShaderTabSettings() : null;
            ShaderSettings.Entry entry = (settings != null) ? settings.Find(technique) : null;
            ShaderSettings.Parameter p = (entry != null) ? entry.Parameters.Find(delegate(ShaderSettings.Parameter x) { return x.Name == name; }) : null;
            if (p == null)
            {
                return;
            }
            p.Xml = xml;
            this.WriteShaderTabSettings(settings);
            foreach (ListViewItem item in this.lvShdParams.Items)
            {
                ShaderSettings.Parameter shown = (ShaderSettings.Parameter) item.Tag;
                if (shown.Name == name)
                {
                    shown.Xml = xml;
                    item.SubItems[1].Text = ShaderValueText(shown);
                }
            }
            this.SetStatus(technique + ": " + name + " changed (pending: Save to write the archive)");
        }

        // Adds the chosen parameter with every value 0 (nothing is made up), selected so its sliders are under the list.
        private void AddShaderTabParameter()
        {
            string technique = this.ShaderTabTechnique;
            ShaderSettings settings = (technique != null && this.cmbShdAdd.SelectedItem != null) ? this.ReadShaderTabSettings() : null;
            if (settings == null)
            {
                return;
            }
            string name = this.cmbShdAdd.SelectedItem.ToString();
            List<string[]> attributes = new List<string[]>();
            foreach (string a in ShaderSettings.AttributeNames(name))
            {
                attributes.Add(new string[] { a, "0" });
            }
            ShaderSettings.Entry entry = settings.Find(technique);
            if (entry == null)
            {
                entry = new ShaderSettings.Entry();
                entry.Name = technique;
                settings.Entries.Add(entry);
            }
            entry.Parameters.Add(new ShaderSettings.Parameter { Name = name, Xml = ShaderSettings.BuildXml(name, attributes) });
            this.WriteShaderTabSettings(settings);
            this.FillShaderTabParameters(name);
            this.SetStatus(technique + ": " + name + " added, set its value under the list (pending: Save to write the archive)");
        }

        private void RemoveShaderTabParameter()
        {
            string technique = this.ShaderTabTechnique;
            if (technique == null || this.lvShdParams.SelectedItems.Count == 0)
            {
                return;
            }
            string name = ((ShaderSettings.Parameter) this.lvShdParams.SelectedItems[0].Tag).Name;
            ShaderSettings settings = this.ReadShaderTabSettings();
            ShaderSettings.Entry entry = (settings != null) ? settings.Find(technique) : null;
            if (entry == null)
            {
                return;
            }
            entry.Parameters.RemoveAll(delegate(ShaderSettings.Parameter x) { return x.Name == name; });
            if (entry.Parameters.Count == 0)
            {
                settings.Entries.Remove(entry);   // no empty elements
            }
            this.WriteShaderTabSettings(settings);
            this.FillShaderTabParameters(null);
            this.SetStatus(technique + ": " + name + " removed (pending: Save to write the archive)");
        }

        private void RemoveShaderTabEntry()
        {
            string technique = this.ShaderTabTechnique;
            ShaderSettings settings = (technique != null) ? this.ReadShaderTabSettings() : null;
            ShaderSettings.Entry entry = (settings != null) ? settings.Find(technique) : null;
            if (entry == null)
            {
                return;
            }
            if (MessageBox.Show(this, "Remove all settings of " + technique + "? It then runs with its shader's defaults.", "Shader", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            settings.Entries.Remove(entry);
            this.WriteShaderTabSettings(settings);
            this.FillShaderTabParameters(null);
            this.SetStatus(technique + ": settings removed (pending: Save to write the archive)");
        }

        // The cars of Media\cars of the game folder the archive is in (the source "Copy Missing From Car" reads).
        private void FillShaderTabCars()
        {
            string root = this.GameRoot;
            string cars = (root == null) ? null : Path.Combine(Path.Combine(root, "Media"), "cars");
            if (cars == null || !Directory.Exists(cars) || this.shdCarPaths.Count > 0)
            {
                return;
            }
            foreach (string path in Directory.GetFiles(cars, "*.zip"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (!name.Equals("Shared", StringComparison.OrdinalIgnoreCase))
                {
                    this.shdCarPaths[name] = path;
                }
            }
            List<string> names = new List<string>(this.shdCarPaths.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            this.cmbShdCar.Items.Clear();
            this.cmbShdCar.Items.AddRange(names.ToArray());
            if (this.cmbShdCar.Items.Count > 0)
            {
                this.cmbShdCar.SelectedIndex = 0;
            }
        }

        // The chosen car's entry of the same technique: the parameters this car does not set yet are copied (its own stay).
        private void CopyShaderTabFromCar()
        {
            string technique = this.ShaderTabTechnique;
            string car = this.cmbShdCar.SelectedItem as string;
            string path;
            if (technique == null || car == null || !this.shdCarPaths.TryGetValue(car, out path))
            {
                return;
            }
            ShaderSettings.Entry from;
            try
            {
                ForzaZip other = GamePaths.OpenZip(path);
                ForzaZip.Entry file = other.Entries.Find(delegate(ForzaZip.Entry e) { return IsShaderSettings(e); });
                from = (file == null) ? null : ShaderSettings.Parse(other.Read(file)).Find(technique);
            }
            catch (Exception exception)
            {
                this.SetStatus(car + ": " + exception.Message);
                return;
            }
            if (from == null || from.Parameters.Count == 0)
            {
                this.SetStatus(car + " has no settings for " + technique + ": nothing copied.");
                return;
            }
            ShaderSettings settings = this.ReadShaderTabSettings();
            if (settings == null)
            {
                return;
            }
            ShaderSettings.Entry entry = settings.Find(technique);
            if (entry == null)
            {
                entry = new ShaderSettings.Entry();
                entry.Name = technique;
                settings.Entries.Add(entry);
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
            if (added == 0)
            {
                this.SetStatus(technique + ": " + car + " sets nothing this car does not set already.");
                return;
            }
            this.WriteShaderTabSettings(settings);
            this.FillShaderTabParameters(null);
            this.SetStatus(string.Format("{0}: {1} parameter(s) copied from {2} (pending: Save to write the archive)", technique, added, car));
        }
    }
}
