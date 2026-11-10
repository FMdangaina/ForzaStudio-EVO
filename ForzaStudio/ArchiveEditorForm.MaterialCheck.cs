namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Windows.Forms;

    // FM4 models whose materials no FM4 car uses (an FH2 port: fullbeam, hidhead, ...) render them
    // black in game. They are shown in red in the model tree and can be renamed to an FM4 material.
    public partial class ArchiveEditorForm
    {
        private ToolStripLabel lblMaterialWarning;
        private int modelType;                                   // carbin type of the shown model, 0 if not checked
        private string materialFile = "";                        // file name and kind of the shown model, for Fm4MaterialUse
        private bool materialWheels;
        private ShaderProfile shaderProfile = ShaderProfile.Unknown;   // game of the shown model (ShaderProfiles.Detect)
        private readonly List<string> unknownMaterials = new List<string>();

        private ToolStripLabel BuildMaterialWarning()
        {
            this.lblMaterialWarning = new ToolStripLabel();
            this.lblMaterialWarning.ForeColor = ForzaTheme.Red;
            this.lblMaterialWarning.Visible = false;
            // A link: a click selects the first one, ready for Rename Material.
            this.lblMaterialWarning.IsLink = true;
            this.lblMaterialWarning.LinkColor = ForzaTheme.Red;
            this.lblMaterialWarning.ActiveLinkColor = ForzaTheme.Red;
            this.lblMaterialWarning.VisitedLinkColor = ForzaTheme.Red;
            this.lblMaterialWarning.LinkBehavior = LinkBehavior.HoverUnderline;
            this.lblMaterialWarning.Click += delegate { this.SelectUnknownMaterial(); };
            return this.lblMaterialWarning;
        }

        // Colors the mesh nodes (and their part and LOD) whose material no FM4 car uses. Only FM4-format
        // files are checked: in an FH or FM3 file every game-specific name would show.
        private void MarkUnknownMaterials(byte[] data, TreeNode root)
        {
            this.unknownMaterials.Clear();
            this.modelType = 0;
            this.lblMaterialWarning.Visible = false;
            // The game of the model decides the lists: FM4 as before, the others against what their own cars use
            // (ShaderProfiles, docs\SHADERS_OTHER_GAMES.md). A file that cannot be read is not checked.
            this.shaderProfile = ShaderProfiles.Detect(data);
            if (this.shaderProfile == ShaderProfile.Unknown && this.GameRoot != null)
            {
                this.shaderProfile = ShaderProfiles.ForGame(GameInfo.Detect(this.GameRoot));   // 4 of the 411 FM2 wheel files
            }
            string game = ShaderProfiles.Tag(this.shaderProfile);
            if (this.shaderProfile == ShaderProfile.Unknown)
            {
                return;
            }
            bool lodZero = false;
            bool lodOther = false;
            if (this.shaderProfile != ShaderProfile.Fm4)
            {
                foreach (TreeNode lodNode in root.Nodes)
                {
                    if (lodNode.Text == "LOD0") { lodZero = true; } else { lodOther = true; }
                }
            }
            else
            {
                CarbinModel model;
                try
                {
                    model = new CarbinModel(data);
                }
                catch (Exception)
                {
                    return;
                }
                this.modelType = (int) model.Type;
                // A file holding LOD 0 and the others together is a wheel set (cars keep LOD 0 in _lod0 / _cockpit files);
                // the check uses the FM4 list of its kind and LOD (Fm4MaterialUse).
                foreach (CarbinModel.Section section in model.Sections)
                {
                    foreach (CarbinModel.SubSection sub in section.SubSections)
                    {
                        if (sub.Lod == 0) { lodZero = true; } else { lodOther = true; }
                    }
                }
            }
            this.materialFile = root.Text;
            this.materialWheels = lodZero && lodOther;
            this.MarkUnknownMaterials(root.Nodes);
            if (this.unknownMaterials.Count > 0)
            {
                this.lblMaterialWarning.Text = string.Format("{0} material{1} not in {2}{3}", this.unknownMaterials.Count, (this.unknownMaterials.Count == 1) ? "" : "s", game,
                    (this.shaderProfile == ShaderProfile.Fm2 || this.shaderProfile == ShaderProfile.Fm3) ? " (this game's models cannot be edited)" : ": click to fix");
                this.lblMaterialWarning.ToolTipText = "No " + game + " car uses " + string.Join(", ", this.unknownMaterials.ToArray())
                    + ": they may render black in game.\nRed in the tree. Click here to select the first one, then Rename Material in the Part tab.";
                this.lblMaterialWarning.Visible = true;
            }
        }

        private void MarkUnknownMaterials(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Tag is ForzaMesh)
                {
                    bool used = this.IsUsedByFm4(node);
                    ((ForzaMesh) node.Tag).Unused = !used;   // magenta in the viewport, as the game's black
                    if (!used)
                    {
                        if (!this.unknownMaterials.Contains(node.Text))
                        {
                            this.unknownMaterials.Add(node.Text);
                        }
                        for (TreeNode marked = node; marked != null; marked = marked.Parent)
                        {
                            marked.ForeColor = ForzaTheme.Red;
                        }
                    }
                    continue;
                }
                this.MarkUnknownMaterials(node.Nodes);
            }
        }

        // True when FM4 uses the mesh's material in a model of this kind and LOD (the tree is file > LODn > part > material).
        private bool IsUsedByFm4(TreeNode mesh)
        {
            if (this.modelType == 1 && this.shaderProfile == ShaderProfile.Fm4)
            {
                return CarPort.IsFm4Material(mesh.Text, 1);
            }
            uint lod = 0;
            TreeNode lodNode = (mesh.Parent != null) ? mesh.Parent.Parent : null;
            if (lodNode != null && lodNode.Text.StartsWith("LOD"))
            {
                uint.TryParse(lodNode.Text.Substring(3), out lod);
            }
            return ShaderProfiles.IsUsed(this.shaderProfile, Fm4MaterialUse.ListName(this.materialWheels, this.materialFile, lod), mesh.Text);
        }

        // Selecting a node lights up its meshes in the viewport (a part, a LOD or one material); the file node lights none.
        // The tree's selection moved (a click, or a Ctrl click that adds to the meshes already picked): the
        // selected meshes are the node's own, or all of them when Ctrl is held.
        private void TreeSelected()
        {
            if (!this.keepMeshSelection)
            {
                TreeNode selected = this.tvModel.SelectedNode;
                List<ForzaMesh> own = new List<ForzaMesh>();
                if (selected != null && selected.Parent != null)
                {
                    if (selected.Tag is ForzaMesh)
                    {
                        own.Add((ForzaMesh) selected.Tag);
                    }
                    else
                    {
                        own.AddRange(GetMeshes(selected.Nodes, false));
                    }
                }
                if ((Control.ModifierKeys & System.Windows.Forms.Keys.Control) != 0 && this.meshSelection.Count > 0)
                {
                    bool allIn = own.Count > 0;
                    foreach (ForzaMesh mesh in own)
                    {
                        allIn &= this.meshSelection.Contains(mesh);
                    }
                    foreach (ForzaMesh mesh in own)
                    {
                        if (allIn)
                        {
                            this.meshSelection.Remove(mesh);
                        }
                        else
                        {
                            this.meshSelection.Add(mesh);
                        }
                    }
                }
                else
                {
                    this.meshSelection.Clear();
                    foreach (ForzaMesh mesh in own)
                    {
                        this.meshSelection.Add(mesh);
                    }
                }
            }
            this.UpdateMeshPanel();
            this.HighlightSelection();
        }

        // Selected meshes light up in the viewport (no glow while the UV mapper is open).
        private void HighlightSelection()
        {
            foreach (ForzaMesh mesh in GetMeshes(this.tvModel.Nodes, false))
            {
                mesh.Selected = false;
            }
            if (this.uvMapper == null)
            {
                foreach (ForzaMesh mesh in this.meshSelection)
                {
                    mesh.Selected = true;
                }
            }
            if (this.scene != null)
            {
                this.scene.Update(true);
            }
        }

        private bool IsUnknownMaterial(string material)
        {
            return this.unknownMaterials.Contains(material);
        }

        private void SelectUnknownMaterial()
        {
            TreeNode found = FindUnknownMaterial(this.tvModel.Nodes, this.unknownMaterials);
            if (found != null)
            {
                found.EnsureVisible();
                this.tvModel.SelectedNode = found;
                this.tvModel.Focus();
            }
        }

        private static TreeNode FindUnknownMaterial(TreeNodeCollection nodes, List<string> names)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Tag is ForzaMesh && names.Contains(node.Text))
                {
                    return node;
                }
                TreeNode found = FindUnknownMaterial(node.Nodes, names);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        // Renames the selected material (subsection name) to an FM4 one, in the selected part or in every
        // part, in this file or every model file of the car, under each file's own variant (CarPort.LodVariant:
        // tail_light_lod0 in lod0 and cockpit). shadersettings.xml follows when it has the old name only.
        private void RenameSelectedMaterial()
        {
            string section = this.SelectedSectionName;
            string material = this.SelectedMaterialName;
            if (section == null || material == null)
            {
                return;
            }
            ForzaMesh selected = this.tvModel.SelectedNode.Tag as ForzaMesh;
            int sharing = 0;   // meshes of the selected part (all LODs) with this material
            foreach (TreeNode lodNode in this.tvModel.SelectedNode.Parent.Parent.Parent.Nodes)
            {
                foreach (TreeNode partNode in lodNode.Nodes)
                {
                    if (partNode.Text != section)
                    {
                        continue;
                    }
                    foreach (TreeNode meshNode in partNode.Nodes)
                    {
                        sharing += (meshNode.Tag is ForzaMesh && meshNode.Text == material) ? 1 : 0;
                    }
                }
            }
            Panel content = new Panel();
            Label label = new Label();
            label.Text = string.Format("Rename the material {0} of {1} to:", material, section);
            label.AutoSize = true;
            label.Location = new Point(0, 4);
            // Green: what FM4 cars give this part (with the number of cars), then the most similar shaders; then the common FM4 materials, then
            // ~ the unusual ones (an FM4 shader no or few FM4 cars use: for something different on purpose).
            List<MaterialSuggestions.Entry> choices = MaterialSuggestions.For(material, this.currentEntry.Name, this.modelType, new List<string>(new string[] { section }));
            ComboBox name = new ComboBox();
            name.DropDownStyle = ComboBoxStyle.DropDown;
            name.Width = 420;
            name.MaxDropDownItems = 20;
            ForzaTheme.GreenItems(name);
            name.Location = new Point(0, 26);
            AutoCompleteStringCollection names = new AutoCompleteStringCollection();
            foreach (MaterialSuggestions.Entry choice in choices)
            {
                name.Items.Add(choice);
                names.Add(choice.Name);
            }
            name.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            name.AutoCompleteSource = AutoCompleteSource.CustomSource;
            name.AutoCompleteCustomSource = names;
            bool recommended = choices.Count > 0 && choices[0].Group == MaterialSuggestions.Group.Recommended;
            if (recommended)
            {
                name.SelectedIndex = 0;
            }
            // One scope, picked with a radio button. The default is the mesh selected in the tree and nothing else
            // (user, 2026-11-09: with nothing ticked the old dialog renamed every mesh of the part in every LOD).
            RadioButton onlyThis = new RadioButton();
            onlyThis.Text = "Only this mesh (the one selected in the tree)";
            onlyThis.AutoSize = true;
            onlyThis.Location = new Point(0, 60);
            onlyThis.Enabled = selected != null && selected.SubIndex >= 0;
            RadioButton wholePart = new RadioButton();
            wholePart.Text = string.Format("Every mesh of {0} with {1}, all LODs ({2})", section, material, sharing);
            wholePart.AutoSize = true;
            wholePart.Location = new Point(0, 84);
            RadioButton everyPart = new RadioButton();
            everyPart.Text = string.Format("Every part with {0}", material);
            everyPart.AutoSize = true;
            everyPart.Location = new Point(0, 108);
            onlyThis.Checked = onlyThis.Enabled;
            wholePart.Checked = !onlyThis.Enabled;
            CheckBox everyFile = new CheckBox();
            everyFile.Text = "Also in the other model files of the car (main, lod0, cockpit)";
            everyFile.Checked = false;
            everyFile.AutoSize = true;
            everyFile.Location = new Point(18, 132);
            everyFile.Enabled = !onlyThis.Checked;
            onlyThis.CheckedChanged += delegate
            {
                everyFile.Enabled = !onlyThis.Checked;
                if (onlyThis.Checked)
                {
                    everyFile.Checked = false;
                }
            };
            Label note = new Label();
            note.Text = "green = recommended   ~ unusual: an FM4 shader few or no FM4 cars use\n\"shader N\": how close the FM4 shader is to the source game's (0-100).";
            note.ForeColor = SystemColors.GrayText;
            note.AutoSize = true;
            note.Location = new Point(0, 162);
            content.Size = new Size(440, 196);
            content.Controls.AddRange(new Control[] { label, name, onlyThis, wholePart, everyPart, everyFile, note });
            if (!this.ShowOptions("Rename Material", content))
            {
                return;
            }
            string newName = MaterialSuggestions.NameOf((name.SelectedItem != null && name.Text == name.SelectedItem.ToString()) ? name.SelectedItem : name.Text);
            if (newName.Length == 0 || newName.IndexOfAny(new char[] { ' ', '/', '\\', '<', '>' }) >= 0 || newName == material)
            {
                return;
            }
            this.RenameMaterial(section, material, selected, onlyThis.Checked ? this.tvModel.SelectedNode : null, newName, everyPart.Checked, everyFile.Checked);
        }

        // In-place rename of a mesh node in the tree (F2): this mesh only, as the dialog's default; the
        // dialog (right click > Rename Material) keeps the FM4 suggestions and the wider scopes.
        private void RenameMeshInPlace(TreeNode node, string newName)
        {
            ForzaMesh selected = node.Tag as ForzaMesh;
            if (selected == null || node.Parent == null || selected.SubIndex < 0)
            {
                return;
            }
            string material = node.Text;
            if (newName.Length == 0 || newName == material)
            {
                return;
            }
            if (newName.IndexOfAny(new char[] { ' ', '/', '\\', '<', '>' }) >= 0)
            {
                MessageBox.Show(this, "Give the material a name without spaces, slashes or < >.", "Rename Material", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            this.RenameMaterial(node.Parent.Text, material, selected, node, newName, false, false);
        }

        // onlyNode: rename that mesh only (its node, for the tree to keep its tick); null: every mesh with the
        // material in the part (everyPart: in every part), in this file or (everyFile) every model file.
        private void RenameMaterial(string section, string material, ForzaMesh selected, TreeNode onlyNode, string newName, bool everyPart, bool everyFile)
        {
            bool onlyThis = onlyNode != null;
            List<string> report = new List<string>();
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                List<string[]> renamed = new List<string[]>();   // technique from -> to, for shadersettings.xml
                foreach (ForzaZip.Entry entry in this.PartEditTargets(everyFile && !onlyThis))
                {
                    CarbinModel model;
                    try
                    {
                        model = new CarbinModel(this.zip.Read(entry));
                    }
                    catch (NotSupportedException)
                    {
                        continue;
                    }
                    string to = CarPort.LodVariant(newName, entry.Name);
                    int count = 0;
                    if (onlyThis)
                    {
                        // The tree reads the file in the same order as CarbinModel; checked by name anyway.
                        CarbinModel.SubSection sub = (selected.SectionIndex < model.Sections.Count && selected.SubIndex < model.Sections[selected.SectionIndex].SubSections.Count)
                            ? model.Sections[selected.SectionIndex].SubSections[selected.SubIndex] : null;
                        if (sub == null || model.Sections[selected.SectionIndex].Name != section || sub.Name != material)
                        {
                            throw new InvalidDataException("The selected mesh could not be found in the file: nothing renamed.");
                        }
                        sub.Name = to;
                        count = 1;
                    }
                    foreach (CarbinModel.Section part in model.Sections)
                    {
                        if (onlyThis || (!everyPart && part.Name != section))
                        {
                            continue;
                        }
                        foreach (CarbinModel.SubSection sub in part.SubSections)
                        {
                            if (sub.Name == material)
                            {
                                sub.Name = to;
                                count++;
                            }
                        }
                    }
                    if (count == 0)
                    {
                        continue;
                    }
                    this.zip.Replace(entry, model.ToBytes());
                    this.RefreshEntryNode(entry);
                    string file = entry.Name.Substring(entry.Name.LastIndexOf('/') + 1);
                    report.Add(string.Format("{0}: {1} -> {2} ({3})", file, material, to, count));
                    string[] pair = new string[] { ShaderSettings.TechniqueName(material, entry.Name), ShaderSettings.TechniqueName(to, entry.Name) };
                    if (!renamed.Exists(delegate(string[] p) { return p[0] == pair[0] && p[1] == pair[1]; }))
                    {
                        renamed.Add(pair);
                    }
                }
                this.FollowShaderSettings(renamed, report);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Rename Material", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            if (onlyNode != null && report.Count > 0)
            {
                // The tree keeps ticks and selection by path: the node takes its new name first.
                onlyNode.Text = CarPort.LodVariant(newName, this.currentEntry.Name);
            }
            this.editSection = null;
            this.ReloadModelKeepingView();
            this.SetStatus((report.Count > 0) ? string.Join("; ", report.ToArray()) + " (pending: Save to write the archive)" : "Nothing to rename.");
        }

        // After materials were renamed in the car's models (technique pairs from -> to): the car's
        // shadersettings.xml follows (ShaderSettings.FollowRename). The models are read again, so a
        // technique still used elsewhere (other part, other file, one mesh renamed) keeps its entry and
        // the new name gets a copy; otherwise the entry is renamed. Before, the entry was always renamed,
        // which left the meshes still using the old name without their settings.
        private void FollowShaderSettings(List<string[]> renamed, List<string> report)
        {
            ForzaZip.Entry entry = this.zip.Find(this.ModelFolder + "shadersettings.xml");
            if (entry == null || renamed.Count == 0)
            {
                return;
            }
            byte[] original = this.zip.Read(entry);
            ShaderSettings settings = ShaderSettings.Parse(original);
            string before = Convert.ToBase64String(settings.ToBytes());
            SortedDictionary<string, List<string>> used = ShaderSettings.Techniques(this.zip, this.ModelFolder);
            foreach (string[] pair in renamed)
            {
                string done = settings.FollowRename(pair[0], pair[1], used.ContainsKey(pair[0]));
                if (done != null)
                {
                    report.Add("shadersettings.xml: " + done);
                }
            }
            if (Convert.ToBase64String(settings.ToBytes()) != before)
            {
                this.zip.Replace(entry, settings.ToBytes());
                this.RefreshEntryNode(entry);
            }
        }
    }
}
