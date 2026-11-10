namespace ForzaStudio
{
    using Microsoft.Xna.Framework;
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Windows.Forms;

    // Part edits on FM4 car models: move / rotate / scale a section with a live preview, and merge a
    // section into another. Every model file of the car (main, lod0, cockpit) that has the part gets
    // the same edit, so the LODs stay aligned. Edits are pending changes of the archive, like any other.
    public partial class ArchiveEditorForm
    {
        private Label lblEditPart;
        private NumericUpDown[] numMove;
        private NumericUpDown[] numRotate;
        private NumericUpDown[] numScale;
        private NumericUpDown[] numPivot;
        private CheckBox chkUniformScale;
        private ComboBox cmbPivot;
        private CheckBox chkAllModelFiles;
        private ComboBox cmbMergeTarget;
        private Button btnApplyEdit;
        private Button btnMergeInto;
        private Button btnFocusPart;
        private CheckBox chkSoloPart;
        private Button btnDeletePart;
        private Button btnDuplicatePart;
        private Button btnRenameMaterial;   // enabled when a material (mesh node) is selected
        private Button btnRenamePart;
        private bool fillingPartEdit;
        private string editSection;
        private string editMaterial;   // null: the whole part
        private Vector3 editCenter;
        private Vector3 editHalfSize;

        private const string PickPartText = "Pick a part (not a LOD) in the tree to change it.";

        private void AddPartEditControls(FlowLayoutPanel side)
        {
            this.lblEditPart = SideNote(PickPartText);

            side.Controls.Add(SideHeading("Move, rotate, scale", false));
            side.Controls.Add(this.lblEditPart);
            side.Controls.Add(SideCaption("Move (m)"));
            side.Controls.Add(this.NumberRow(out this.numMove, -20, 20, 3, 0.005m, 0));
            side.Controls.Add(SideCaption("Rotate (deg)"));
            side.Controls.Add(this.NumberRow(out this.numRotate, -360, 360, 1, 1, 0));
            side.Controls.Add(SideCaption("Scale (%)"));
            side.Controls.Add(this.NumberRow(out this.numScale, 1, 1000, 1, 1, 100));
            this.chkUniformScale = new CheckBox();
            this.chkUniformScale.Text = "Same scale on all axes";
            this.chkUniformScale.Size = new Size(SideWidth, 22);
            side.Controls.Add(this.chkUniformScale);

            side.Controls.Add(SideCaption("Pivot"));
            this.cmbPivot = new ComboBox();
            this.cmbPivot.DropDownStyle = ComboBoxStyle.DropDownList;
            this.cmbPivot.Width = SideWidth;
            this.cmbPivot.Items.AddRange(new object[] { "Part center", "Model origin (0, 0, 0)", "Custom point" });
            this.cmbPivot.SelectedIndex = 0;
            this.cmbPivot.SelectedIndexChanged += delegate
            {
                foreach (NumericUpDown number in this.numPivot)
                {
                    number.Enabled = this.cmbPivot.SelectedIndex == 2;
                }
                this.UpdatePartPreview();
            };
            side.Controls.Add(this.cmbPivot);
            side.Controls.Add(this.NumberRow(out this.numPivot, -20, 20, 3, 0.01m, 0));
            foreach (NumericUpDown number in this.numPivot)
            {
                number.Enabled = false;
            }

            this.chkAllModelFiles = new CheckBox();
            this.chkAllModelFiles.Text = "Also in the car's other model files (main, lod0, cockpit)";
            this.chkAllModelFiles.Checked = true;
            this.chkAllModelFiles.Size = new Size(SideWidth, 36);
            side.Controls.Add(this.chkAllModelFiles);

            this.btnApplyEdit = new Button();
            this.btnApplyEdit.Text = "Apply";
            this.btnApplyEdit.Click += delegate { this.ApplyPartEdit(); };
            Button reset = new Button();
            reset.Text = "Reset";
            reset.Click += delegate { this.ResetPartEdit(); };
            side.Controls.Add(SideButtonRow(this.btnApplyEdit, reset));

            side.Controls.Add(SideHeading("View", false));
            this.btnFocusPart = SideWideButton("Focus On Part", delegate { this.FocusPart(); });
            side.Controls.Add(this.btnFocusPart);
            this.chkSoloPart = new CheckBox();
            this.chkSoloPart.Text = "Show only this part";
            this.chkSoloPart.Size = new Size(SideWidth, 22);
            this.chkSoloPart.CheckedChanged += delegate { this.UpdatePartPreview(); this.UpdateModelInfo(); };
            side.Controls.Add(this.chkSoloPart);

            side.Controls.Add(SideHeading("Part", false));
            this.btnDuplicatePart = new Button();
            this.btnDuplicatePart.Text = "Duplicate...";
            this.btnDuplicatePart.Click += delegate { this.DuplicateSelectedPart(); };
            this.btnDeletePart = new Button();
            this.btnDeletePart.Text = "Delete...";
            this.btnDeletePart.Click += delegate { this.DeleteSelectedNode(); };
            side.Controls.Add(SideButtonRow(this.btnDuplicatePart, this.btnDeletePart));
            this.btnRenamePart = SideWideButton("Rename Part (F2)", delegate { this.BeginRenamePart(); });
            side.Controls.Add(this.btnRenamePart);
            this.btnRenameMaterial = SideWideButton("Rename Material...", delegate { this.RenameSelectedMaterial(); });
            side.Controls.Add(this.btnRenameMaterial);

            side.Controls.Add(SideCaption("Merge this part into"));
            this.cmbMergeTarget = new ComboBox();
            this.cmbMergeTarget.DropDownStyle = ComboBoxStyle.DropDownList;
            this.cmbMergeTarget.Width = SideWidth;
            side.Controls.Add(this.cmbMergeTarget);
            this.btnMergeInto = SideWideButton("Merge...", delegate { this.MergeSelectedPart(); });
            side.Controls.Add(this.btnMergeInto);
            side.Controls.Add(SideNote("Materials with the same name are joined, the others are moved; the merged part is removed."));
            side.Controls.Add(SideNote("Drag a material in the tree onto a part to move that mesh there, onto a LOD for a new part (this file only)."));
            side.Controls.Add(SideNote("Join two meshes (even with the same name, in one part): right-click a mesh in the tree > Attach, pick the other (this file only)."));
            this.AddPartImportControls(side);

            this.tvModel.AfterSelect += delegate { this.OnPartSelected(); };
            this.BuildModelTreeMenu();
            this.HookMeshDragDrop();
            this.EnablePartEdit(false);
        }

        // Right-click menu and Delete key on the model tree.
        private void BuildModelTreeMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Renderer = ForzaTheme.Renderer;
            ToolStripMenuItem delete = new ToolStripMenuItem("Delete...", null, delegate { this.DeleteSelectedNode(); });
            ToolStripMenuItem focus = new ToolStripMenuItem("Focus On Part", null, delegate { this.FocusPart(); });
            ToolStripMenuItem duplicate = new ToolStripMenuItem("Duplicate Part...", null, delegate { this.DuplicateSelectedPart(); });
            ToolStripMenuItem renamePart = new ToolStripMenuItem("Rename Part", null, delegate { this.BeginRenamePart(); });
            renamePart.ShortcutKeyDisplayString = "F2";
            ToolStripMenuItem renameMesh = new ToolStripMenuItem("Rename", null, delegate { this.BeginRenameNode(); });
            renameMesh.ShortcutKeyDisplayString = "F2";
            ToolStripMenuItem rename = new ToolStripMenuItem("Rename Material...", null, delegate { this.RenameSelectedMaterial(); });
            ToolStripMenuItem shader = new ToolStripMenuItem("Shader Settings...", null, delegate { this.OpenShaderSettingsOfSelection(); });
            ToolStripMenuItem attach = new ToolStripMenuItem("Attach...", null, delegate { this.AttachToSelectedMesh(); });
            ToolStripMenuItem duplicateMesh = new ToolStripMenuItem("Duplicate Mesh", null, delegate { this.DuplicateSelectedMesh(); });
            ToolStripSeparator partSeparator = new ToolStripSeparator();
            ToolStripMenuItem newPart = new ToolStripMenuItem("New Empty Part...", null, delegate { this.NewEmptyPart(this.tvModel.SelectedNode); });
            menu.Items.Add(newPart);
            menu.Items.Add(focus);
            menu.Items.Add(duplicate);
            menu.Items.Add(renamePart);
            menu.Items.Add(renameMesh);
            menu.Items.Add(rename);
            menu.Items.Add(shader);
            menu.Items.Add(duplicateMesh);
            menu.Items.Add(attach);
            menu.Items.Add(partSeparator);
            menu.Items.Add(delete);
            menu.Opening += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                string section;
                string material;
                // The node under the pointer first: the tree selects a right-clicked node only after the menu
                // has asked what is selected, so the menu used to work on the node selected before.
                TreeNode hit = this.tvModel.GetNodeAt(this.tvModel.PointToClient(Cursor.Position));
                if (hit != null && hit != this.tvModel.SelectedNode)
                {
                    this.tvModel.SelectedNode = hit;
                }
                // LOD node: only New Empty Part; empty part: only its removal.
                TreeNode node = this.tvModel.SelectedNode;
                bool lodNode = IsLodNode(node) && this.SelectedSectionName == null && this.currentEntry != null
                    && this.currentEntry.Name.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase);
                bool empty = IsEmptyPart(node);
                newPart.Visible = lodNode;
                focus.Visible = duplicate.Visible = partSeparator.Visible = !lodNode && !empty;
                delete.Visible = !lodNode;
                renamePart.Visible = empty;
                if (empty)
                {
                    renamePart.Text = string.Format("Rename Part \"{0}\"", node.Text);
                }
                renameMesh.Visible = false;
                attach.Visible = duplicateMesh.Visible = false;
                if (lodNode || empty)
                {
                    rename.Visible = false;
                    shader.Visible = false;
                    delete.Text = string.Format("Remove Empty Part \"{0}\"", (node != null) ? node.Text : "");
                    return;
                }
                if (!this.DeletableNode(out section, out material))
                {
                    // Only parts and their materials have commands (LOD and model nodes have none).
                    e.Cancel = true;
                    return;
                }
                // Part edits (move, focus) need the PART tab's reader; delete, duplicate and rename do not.
                focus.Enabled = this.editSection == section;
                renamePart.Visible = true;
                renamePart.Text = string.Format("Rename Part \"{0}\"", section);
                renamePart.ShortcutKeyDisplayString = (material == null) ? "F2" : "";
                renameMesh.Visible = material != null && this.IsRenamableMesh(node);
                renameMesh.Text = string.Format("Rename \"{0}\" (this mesh only)", material);
                rename.Visible = material != null;
                shader.Visible = material != null;
                // (Visible reads false while the menu is not shown yet: set from a bool, never read back.)
                bool meshNode = material != null && IsAttachableMesh(node);
                attach.Visible = meshNode;
                attach.Text = string.Format("Attach to \"{0}\" of {1}...", material, section);
                duplicateMesh.Visible = meshNode;
                duplicateMesh.Text = string.Format("Duplicate \"{0}\" (this mesh only)", material);
                if (shader.Visible)
                {
                    shader.Text = string.Format("Shader Settings of \"{0}\"...", material);
                }
                if (rename.Visible)
                {
                    rename.Text = string.Format("Rename Material \"{0}\"...", material);
                    rename.ForeColor = this.IsUnknownMaterial(material) ? ForzaTheme.Red : delete.ForeColor;
                }
                delete.Text = (material != null) ? string.Format("Delete Material \"{0}\" from {1}...", material, section) : string.Format("Delete Part \"{0}\"...", section);
                duplicate.Text = string.Format("Duplicate Part \"{0}\"...", section);
            };
            this.tvModel.ContextMenuStrip = menu;
            this.HookPartLabelEdit();
            this.tvModel.NodeMouseClick += delegate(object sender, TreeNodeMouseClickEventArgs e)
            {
                if (e.Button == MouseButtons.Right)
                {
                    this.tvModel.SelectedNode = e.Node;
                }
            };
            this.tvModel.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Delete)
                {
                    e.Handled = true;
                    this.DeleteSelectedNode();
                }
                else if (e.KeyCode == Keys.F2)
                {
                    // F2: the part or the mesh (material) node is renamed in place.
                    e.Handled = true;
                    this.BeginRenameNode();
                }
            };
        }

        // A mesh node is one material of a part; a part node is the whole section.
        private bool DeletableNode(out string section, out string material)
        {
            // Not tied to the PART tab (editSection): it can fail to read a file the edits below handle fine,
            // and then the tree had no right-click menu and Delete did nothing.
            section = this.SelectedSectionName;
            material = null;
            if (section == null || IsEmptyPart(this.tvModel.SelectedNode))
            {
                return false;
            }
            TreeNode node = this.tvModel.SelectedNode;
            if (node.Tag is ForzaMesh)
            {
                material = node.Text;
            }
            return true;
        }

        private void DeleteSelectedNode()
        {
            if (IsEmptyPart(this.tvModel.SelectedNode))
            {
                this.RemoveEmptyPart(this.tvModel.SelectedNode);
                return;
            }
            string section;
            string material;
            if (!this.DeletableNode(out section, out material))
            {
                return;
            }
            string scope = this.chkAllModelFiles.Checked ? "every model file of the car" : "this file";
            string question = (material != null)
                ? string.Format("Delete the material \"{0}\" of {1}, in all LODs of {2}?\n\nIts triangles and the vertices only they use are removed. If it is the last material of {1}, the whole part goes.", material, section, scope)
                : string.Format("Delete the part \"{0}\" with all its materials, in {1}?", section, scope);
            question += "\nThe change stays pending until you save (Archive > Revert Selected File undoes it).";
            if (MessageBox.Show(this, question, "Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            List<string> report = new List<string>();
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                foreach (ForzaZip.Entry entry in this.PartEditTargets())
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
                    if (model.Find(section) == null)
                    {
                        continue;
                    }
                    string file = entry.Name.Substring(entry.Name.LastIndexOf('/') + 1);
                    if (material != null)
                    {
                        int removed = model.RemoveSubSections(section, material);
                        if (removed == 0)
                        {
                            continue;
                        }
                        report.Add(string.Format("{0}: {1} removed ({2} LODs){3}", file, material, removed, (model.Find(section) == null) ? ", part now empty and removed" : ""));
                    }
                    else
                    {
                        model.RemoveSection(section);
                        report.Add(file + ": part removed");
                    }
                    this.zip.Replace(entry, model.ToBytes());
                    this.RefreshEntryNode(entry);
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Delete", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            this.editSection = null;
            this.ReloadModelKeepingView();
            this.SetStatus((report.Count > 0) ? string.Join("; ", report.ToArray()) : "Nothing to delete.");
        }

        // A copy of the whole part (all materials, LODs, damage data) under a new name, in the same place:
        // e.g. bumperRa -> bumperRb, then edit, move or replace the copy from an OBJ.
        private void DuplicateSelectedPart()
        {
            string section = this.SelectedSectionName;
            if (section == null || IsEmptyPart(this.tvModel.SelectedNode))
            {
                return;
            }
            Panel content = new Panel();
            content.Size = new Size(360, 56);
            Label label = new Label();
            label.Text = "Name of the copy of " + section + ":";
            label.AutoSize = true;
            label.Location = new System.Drawing.Point(0, 4);
            TextBox name = new TextBox();
            name.Width = 220;
            name.BorderStyle = BorderStyle.FixedSingle;
            name.Location = new System.Drawing.Point(0, 26);
            name.Text = this.SuggestPartName(section);
            content.Controls.AddRange(new Control[] { label, name });
            if (!this.ShowOptions("Duplicate " + section, content))
            {
                return;
            }
            string newName = name.Text.Trim();
            if (newName.Length == 0 || newName.IndexOfAny(new char[] { ' ', '/', '\\' }) >= 0)
            {
                MessageBox.Show(this, "Give the copy a name without spaces.", "Duplicate", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (newName == section)
            {
                return;
            }
            bool replace = this.PartExists(newName);
            if (replace && MessageBox.Show(this, string.Format("This car already has a part named {0}.\n\nReplace it with a copy of {1}? (Pending until you save; Archive > Revert Selected File undoes it.)", newName, section),
                "Duplicate", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            List<string> report = new List<string>();
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                foreach (ForzaZip.Entry entry in this.PartEditTargets(true))
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
                    if (model.Find(section) == null)
                    {
                        continue;
                    }
                    if (replace && model.Find(newName) != null)
                    {
                        model.RemoveSection(newName);
                    }
                    model.DuplicateSection(section, newName);
                    this.zip.Replace(entry, model.ToBytes());
                    this.RefreshEntryNode(entry);
                    report.Add(entry.Name.Substring(entry.Name.LastIndexOf('/') + 1));
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Duplicate", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            this.editSection = null;
            this.ReloadModelKeepingView();
            this.SetStatus(string.Format("{0} duplicated as {1} in {2} (pending: Save to write the archive)", section, newName, string.Join(", ", report.ToArray())));
        }

        // F2, the tree menu or the PART tab button: the part's name becomes editable in the tree, as in
        // Explorer (on a material node, its part). Enter renames, Esc leaves it. The tree edits labels only
        // when asked here, so a slow second click on a node does not start an edit by mistake.
        private void BeginRenamePart()
        {
            TreeNode node = this.tvModel.SelectedNode;
            if (node != null && node.Tag is ForzaMesh)
            {
                node = node.Parent;
            }
            if (node == null || !IsPartNode(node))
            {
                return;
            }
            this.tvModel.SelectedNode = node;
            if (!IsEmptyPart(node) && this.SelectedSectionName != node.Text)
            {
                return;
            }
            this.tvModel.Focus();
            this.tvModel.LabelEdit = true;
            node.BeginEdit();
        }

        // F2 on any node: a part as above, a mesh (one material of a part) renamed in place too, this mesh
        // only (right click > Rename Material has the FM4 suggestions and the wider scopes).
        private void BeginRenameNode()
        {
            TreeNode node = this.tvModel.SelectedNode;
            if (IsRenamableMesh(node))
            {
                this.tvModel.Focus();
                this.tvModel.LabelEdit = true;
                node.BeginEdit();
                return;
            }
            this.BeginRenamePart();
        }

        // model > LOD > part (an empty part too); not a mesh, a LOD or the model.
        private static bool IsPartNode(TreeNode node)
        {
            return node != null && !(node.Tag is ForzaMesh) && IsLodNode(node.Parent);
        }

        // A mesh node of a carbin's tree (not of the game LOD preview, not a track model).
        private bool IsRenamableMesh(TreeNode node)
        {
            ForzaMesh mesh = (node != null) ? node.Tag as ForzaMesh : null;
            return mesh != null && mesh.SubIndex >= 0 && IsPartNode(node.Parent) && this.currentEntry != null
                && this.currentEntry.Name.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase);
        }

        private void HookPartLabelEdit()
        {
            this.tvModel.BeforeLabelEdit += delegate(object sender, NodeLabelEditEventArgs e)
            {
                if (!IsPartNode(e.Node) && !this.IsRenamableMesh(e.Node))
                {
                    e.CancelEdit = true;
                    this.tvModel.LabelEdit = false;
                }
            };
            this.tvModel.AfterLabelEdit += delegate(object sender, NodeLabelEditEventArgs e)
            {
                this.tvModel.LabelEdit = false;
                // The tree is rebuilt from the file after the rename; nothing to keep from the edit box.
                e.CancelEdit = true;
                if (e.Label == null)
                {
                    return;   // Esc
                }
                string from = e.Node.Text;
                string to = e.Label.Trim();
                TreeNode edited = e.Node;
                if (edited.Tag is ForzaMesh)
                {
                    this.BeginInvoke((MethodInvoker) delegate { this.RenameMeshInPlace(edited, to); });
                    return;
                }
                bool empty = IsEmptyPart(e.Node);
                // After the edit box is gone: the rename rebuilds the tree.
                this.BeginInvoke((MethodInvoker) delegate { this.RenamePart(from, to, empty); });
            };
        }

        // Renames a part (section name: all its materials and LODs) in every model file of the car (main,
        // lod0, cockpit share the part names), as Duplicate adds a part to all of them. The name is a plain
        // length-prefixed string in the carbin, nothing else in the file refers to it. An empty part (not in
        // the file yet) is renamed in the tree only.
        private void RenamePart(string section, string newName, bool empty)
        {
            if (newName == section || newName.Length == 0)
            {
                return;
            }
            bool ascii = true;
            foreach (char c in newName)
            {
                ascii = ascii && c > ' ' && c < 127;
            }
            if (!ascii || newName.Length > 255 || newName.IndexOfAny(new char[] { '/', '\\' }) >= 0)
            {
                MessageBox.Show(this, "Give the part a name without spaces, slashes or accented letters (at most 255 characters).", "Rename Part", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (this.PartExists(newName) || (this.emptyParts.Exists(delegate(string[] p) { return p[1] == newName; })))
            {
                MessageBox.Show(this, string.Format("This car already has a part named {0}.\n\nTo join the two parts use Merge in the Part tab.", newName), "Rename Part", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (empty)
            {
                foreach (string[] part in this.emptyParts)
                {
                    if (part[1] == section)
                    {
                        part[1] = newName;
                    }
                }
                RenamePartNodes(this.tvModel.Nodes, section, newName);
                this.ReloadModelKeepingView();
                this.SetStatus(string.Format("Empty part {0} renamed {1}", section, newName));
                return;
            }
            List<string> report = new List<string>();
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                foreach (ForzaZip.Entry entry in this.PartEditTargets(true))
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
                    CarbinModel.Section part = model.Find(section);
                    if (part == null)
                    {
                        continue;
                    }
                    part.Name = newName;
                    this.zip.Replace(entry, model.ToBytes());
                    this.RefreshEntryNode(entry);
                    report.Add(entry.Name.Substring(entry.Name.LastIndexOf('/') + 1));
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Rename Part", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            // The tree keeps its ticks, open nodes and selection by path: rename the nodes first.
            RenamePartNodes(this.tvModel.Nodes, section, newName);
            this.editSection = null;
            this.ReloadModelKeepingView();
            this.SetStatus((report.Count > 0)
                ? string.Format("{0} renamed {1} in {2} (pending: Save to write the archive)", section, newName, string.Join(", ", report.ToArray()))
                : "Nothing to rename.");
        }

        // Part nodes sit under the LOD nodes (model > LOD > part).
        private static void RenamePartNodes(TreeNodeCollection roots, string from, string to)
        {
            foreach (TreeNode root in roots)
            {
                foreach (TreeNode lod in root.Nodes)
                {
                    foreach (TreeNode part in lod.Nodes)
                    {
                        if (part.Text == from && !(part.Tag is ForzaMesh))
                        {
                            part.Text = to;
                            part.Name = to;
                        }
                    }
                }
            }
        }

        private static Control AxisHeader(string text)
        {
            Label title = SideLabel(text);
            title.Size = new Size(228, 20);
            return title;
        }

        // Three number boxes, each after its axis letter in the viewport's axis color.
        private FlowLayoutPanel NumberRow(out NumericUpDown[] numbers, decimal min, decimal max, int decimals, decimal step, decimal value)
        {
            FlowLayoutPanel row = new FlowLayoutPanel();
            row.Size = new Size(230, 26);
            row.Margin = new Padding(0);
            numbers = new NumericUpDown[3];
            string[] axes = { "X", "Y", "Z" };
            System.Drawing.Color[] colors = { System.Drawing.Color.FromArgb(235, 70, 70), System.Drawing.Color.FromArgb(90, 210, 90), System.Drawing.Color.FromArgb(80, 130, 255) };
            for (int c = 0; c < 3; c++)
            {
                Label axis = new Label();
                axis.Text = axes[c];
                axis.AutoSize = false;
                axis.Size = new Size(13, 22);
                axis.TextAlign = ContentAlignment.MiddleLeft;
                axis.Margin = new Padding(0);
                axis.ForeColor = colors[c];
                row.Controls.Add(axis);
                NumericUpDown number = new NumericUpDown();
                number.Width = 58;
                number.Minimum = min;
                number.Maximum = max;
                number.DecimalPlaces = decimals;
                number.Increment = step;
                number.Value = value;
                number.BorderStyle = BorderStyle.FixedSingle;
                number.Margin = new Padding(0, 0, 5, 0);
                number.ValueChanged += new EventHandler(this.PartEditValueChanged);
                numbers[c] = number;
                row.Controls.Add(number);
            }
            return row;
        }

        private void PartEditValueChanged(object sender, EventArgs e)
        {
            if (this.fillingPartEdit)
            {
                return;
            }
            NumericUpDown changed = (NumericUpDown) sender;
            if (this.chkUniformScale.Checked && Array.IndexOf(this.numScale, changed) >= 0)
            {
                this.fillingPartEdit = true;
                foreach (NumericUpDown number in this.numScale)
                {
                    number.Value = changed.Value;
                }
                this.fillingPartEdit = false;
            }
            this.UpdatePartPreview();
        }

        private void EnablePartEdit(bool enabled)
        {
            foreach (NumericUpDown[] row in new NumericUpDown[][] { this.numMove, this.numRotate, this.numScale })
            {
                foreach (NumericUpDown number in row)
                {
                    number.Enabled = enabled;
                }
            }
            this.cmbPivot.Enabled = enabled;
            this.btnApplyEdit.Enabled = enabled;
            this.btnFocusPart.Enabled = enabled;
            this.btnDeletePart.Enabled = enabled;
            this.btnDuplicatePart.Enabled = enabled;
            this.btnRenamePart.Enabled = enabled;
            this.btnRenameMaterial.Enabled = enabled && this.SelectedMaterialName != null;
            this.cmbMergeTarget.Enabled = enabled;
            this.btnMergeInto.Enabled = enabled && this.cmbMergeTarget.Items.Count > 0;
        }

        // The section of the selected tree node: a part node (LOD > part) or a mesh node below it.
        private string SelectedSectionName
        {
            get
            {
                TreeNode node = this.tvModel.SelectedNode;
                if (node == null || this.currentEntry == null || !this.currentEntry.Name.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                if (node.Tag is ForzaMesh)
                {
                    node = node.Parent;
                }
                if (node == null || node.Parent == null || node.Parent.Parent == null)
                {
                    return null;
                }
                return node.Text;
            }
        }

        // Material (subsection name) of the selected mesh node, null when a whole part is selected.
        private string SelectedMaterialName
        {
            get
            {
                TreeNode node = this.tvModel.SelectedNode;
                return (node != null && node.Tag is ForzaMesh && this.SelectedSectionName != null) ? node.Text : null;
            }
        }

        private void OnPartSelected()
        {
            string section = this.SelectedSectionName;
            string material = this.SelectedMaterialName;
            if (section == this.editSection && material == this.editMaterial && section != null)
            {
                return;
            }
            this.ClearPartPreview();
            this.editSection = null;
            this.editMaterial = null;
            this.ResetPartValues();
            this.cmbMergeTarget.Items.Clear();
            if (section == null)
            {
                this.lblEditPart.Text = PickPartText;
                this.EnablePartEdit(false);
                return;
            }
            if (IsEmptyPart(this.tvModel.SelectedNode))
            {
                this.lblEditPart.Text = section + ": empty part (yellow). Drag meshes onto it in the tree; while it is empty it is not written to the file.";
                this.EnablePartEdit(false);
                return;
            }
            try
            {
                CarbinFile carbin = this.SelectionCarbin();
                CarbinFile.Section found = carbin.FindSection(section);
                if (found == null)
                {
                    throw new InvalidOperationException("Part not found in the file.");
                }
                this.editSection = section;
                this.editCenter = new Vector3((float) found.Origin[0], (float) found.Origin[1], (float) found.Origin[2]);
                this.editHalfSize = new Vector3((float) found.Max[0], (float) found.Max[1], (float) found.Max[2]);
                if (material != null)
                {
                    // One material of the part: its own center is the default pivot.
                    CarbinModel model = this.SelectionModel();
                    double[] center;
                    double[] half;
                    if (CarbinModel.MaterialBounds(model.Find(section), material, out center, out half))
                    {
                        this.editMaterial = material;
                        this.editCenter = new Vector3((float) center[0], (float) center[1], (float) center[2]);
                        this.editHalfSize = new Vector3((float) half[0], (float) half[1], (float) half[2]);
                    }
                }
                List<string> names = new List<string>();
                foreach (CarbinFile.Section other in carbin.Sections)
                {
                    if (other.Name != section && !names.Contains(other.Name))
                    {
                        names.Add(other.Name);
                    }
                }
                names.Sort(StringComparer.OrdinalIgnoreCase);
                this.cmbMergeTarget.Items.AddRange(names.ToArray());
                if (this.cmbMergeTarget.Items.Count > 0)
                {
                    this.cmbMergeTarget.SelectedIndex = 0;
                }
                this.lblEditPart.Text = (this.editMaterial != null)
                    ? string.Format("Material: {0} of {1} (all LODs)\nCenter: {2:0.000}, {3:0.000}, {4:0.000}", this.editMaterial, section, this.editCenter.X, this.editCenter.Y, this.editCenter.Z)
                    : string.Format("Part: {0}\nCenter: {1:0.000}, {2:0.000}, {3:0.000}", section, this.editCenter.X, this.editCenter.Y, this.editCenter.Z);
                this.fillingPartEdit = true;
                for (int c = 0; c < 3; c++)
                {
                    this.numPivot[c].Value = (decimal) Math.Max(-20, Math.Min(20, (c == 0) ? this.editCenter.X : ((c == 1) ? this.editCenter.Y : this.editCenter.Z)));
                }
                this.fillingPartEdit = false;
                this.EnablePartEdit(true);
                this.UpdatePartPreview();
            }
            catch (Exception exception)
            {
                // Track models and older games use other layouts: no part edits there.
                this.lblEditPart.Text = "Part edits not available: " + exception.Message;
                this.EnablePartEdit(false);
            }
        }

        // The shown file read once per version for the tree selection (read-only use): a click on a node used
        // to unzip and parse the whole carbin twice. A pending edit (new Replacement) or another file reads again.
        private ForzaZip.Entry selectionEntry;
        private byte[] selectionVersion;
        private CarbinFile selectionCarbin;
        private CarbinModel selectionModel;

        private bool SelectionCacheValid()
        {
            if (this.selectionEntry != this.currentEntry || this.selectionVersion != this.currentEntry.Replacement)
            {
                this.selectionEntry = this.currentEntry;
                this.selectionVersion = this.currentEntry.Replacement;
                this.selectionCarbin = null;
                this.selectionModel = null;
                return false;
            }
            return true;
        }

        private CarbinFile SelectionCarbin()
        {
            this.SelectionCacheValid();
            if (this.selectionCarbin == null)
            {
                this.selectionCarbin = new CarbinFile(this.zip.Read(this.currentEntry));
            }
            return this.selectionCarbin;
        }

        private CarbinModel SelectionModel()
        {
            this.SelectionCacheValid();
            if (this.selectionModel == null)
            {
                this.selectionModel = new CarbinModel(this.zip.Read(this.currentEntry));
            }
            return this.selectionModel;
        }

        // Points the camera at the part (with the pending edit applied), from the current direction.
        private void FocusPart()
        {
            if (this.scene == null || this.editSection == null)
            {
                return;
            }
            // Seen from outside the car: from the side of the body the part is on (the model is centered
            // on the origin), slightly from above.
            Vector3 target = Vector3.Transform(this.editCenter, this.HasPendingPartEdit ? this.PartEditMatrix : Matrix.Identity);
            float size = Math.Max(this.editHalfSize.Length(), 0.15f);
            Vector3 outward = new Vector3(target.X, 0f, target.Z);
            if (outward.LengthSquared() < 0.01f)
            {
                outward = new Vector3(1f, 0f, 1f);
            }
            outward.Normalize();
            Vector3 direction = Vector3.Normalize(outward + new Vector3(0f, 0.45f, 0f));
            float distance = Math.Max(size * 3f, 0.8f);
            this.scene.Camera.LookFrom(target + (direction * distance), target);
            this.scene.Update(true);
        }

        private void ResetPartValues()
        {
            this.fillingPartEdit = true;
            for (int c = 0; c < 3; c++)
            {
                this.numMove[c].Value = 0;
                this.numRotate[c].Value = 0;
                this.numScale[c].Value = 100;
            }
            this.fillingPartEdit = false;
        }

        private void ResetPartEdit()
        {
            this.ResetPartValues();
            this.UpdatePartPreview();
        }

        private Vector3 EditPivot
        {
            get
            {
                switch (this.cmbPivot.SelectedIndex)
                {
                    case 1:
                        return Vector3.Zero;
                    case 2:
                        return new Vector3((float) this.numPivot[0].Value, (float) this.numPivot[1].Value, (float) this.numPivot[2].Value);
                }
                return this.editCenter;
            }
        }

        // p' = pivot + R(S(p - pivot)) + move, rotations applied X then Y then Z.
        private Matrix PartEditMatrix
        {
            get
            {
                Vector3 pivot = this.EditPivot;
                Matrix scale = Matrix.CreateScale((float) this.numScale[0].Value / 100f, (float) this.numScale[1].Value / 100f, (float) this.numScale[2].Value / 100f);
                Matrix rotate = Matrix.CreateRotationX(MathHelper.ToRadians((float) this.numRotate[0].Value))
                    * Matrix.CreateRotationY(MathHelper.ToRadians((float) this.numRotate[1].Value))
                    * Matrix.CreateRotationZ(MathHelper.ToRadians((float) this.numRotate[2].Value));
                Vector3 move = new Vector3((float) this.numMove[0].Value, (float) this.numMove[1].Value, (float) this.numMove[2].Value);
                return Matrix.CreateTranslation(-pivot) * scale * rotate * Matrix.CreateTranslation(pivot + move);
            }
        }

        private bool HasPendingPartEdit
        {
            get
            {
                for (int c = 0; c < 3; c++)
                {
                    if (this.numMove[c].Value != 0 || this.numRotate[c].Value != 0 || this.numScale[c].Value != 100)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        private List<ForzaMesh> SectionMeshes(string section)
        {
            List<ForzaMesh> meshes = new List<ForzaMesh>();
            foreach (TreeNode root in this.tvModel.Nodes)
            {
                foreach (TreeNode lod in root.Nodes)
                {
                    TreeNode part = lod.Nodes[section];
                    if (part != null)
                    {
                        meshes.AddRange(GetMeshes(part.Nodes, false));
                    }
                }
            }
            return meshes;
        }

        private void UpdatePartPreview()
        {
            if (this.editSection == null)
            {
                return;
            }
            bool pending = this.HasPendingPartEdit;
            Matrix matrix = pending ? this.PartEditMatrix : Matrix.Identity;
            List<ForzaMesh> part = this.SectionMeshes(this.editSection);
            if (this.editMaterial != null)
            {
                part = part.FindAll(delegate(ForzaMesh mesh) { return mesh.MaterialName == this.editMaterial; });
            }
            foreach (ForzaMesh mesh in GetMeshes(this.tvModel.Nodes, false))
            {
                mesh.Hidden = this.chkSoloPart.Checked && !part.Contains(mesh);
            }
            foreach (ForzaMesh mesh in part)
            {
                mesh.PreviewTransform = matrix;
                mesh.Highlight = pending;
            }
            if (this.scene != null)
            {
                this.scene.Update(true);
            }
        }

        private void ClearPartPreview()
        {
            foreach (ForzaMesh mesh in GetMeshes(this.tvModel.Nodes, false))
            {
                mesh.PreviewTransform = Matrix.Identity;
                mesh.Highlight = false;
                mesh.Hidden = false;
            }
        }

        // The current model file plus, when asked, the other type-2 .carbin files next to it.
        private List<ForzaZip.Entry> PartEditTargets()
        {
            return this.PartEditTargets(this.chkAllModelFiles.Checked);
        }

        // A new part must exist in every model file (main for far LODs, lod0 up close), so adding one
        // always passes allFiles.
        private List<ForzaZip.Entry> PartEditTargets(bool allFiles)
        {
            List<ForzaZip.Entry> targets = new List<ForzaZip.Entry>();
            targets.Add(this.currentEntry);
            if (!allFiles)
            {
                return targets;
            }
            string folder = this.ModelFolder;
            foreach (ForzaZip.Entry entry in this.zip.Entries)
            {
                if (entry == this.currentEntry || !entry.Name.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
                    || entry.Name.IndexOf('/', folder.Length) >= 0 || !entry.Name.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                targets.Add(entry);
            }
            return targets;
        }

        private void ApplyPartEdit()
        {
            if (this.editSection == null || !this.HasPendingPartEdit)
            {
                return;
            }
            Matrix matrix = this.PartEditMatrix;
            List<string> done = new List<string>();
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                foreach (ForzaZip.Entry entry in this.PartEditTargets())
                {
                    byte[] edited;
                    try
                    {
                        if (this.editMaterial != null)
                        {
                            // One material: vertices it shares with other materials get their own copies.
                            CarbinModel model = new CarbinModel(this.zip.Read(entry));
                            if (model.TransformSubSections(this.editSection, this.editMaterial, matrix) == 0)
                            {
                                continue;
                            }
                            edited = model.ToBytes();
                        }
                        else
                        {
                            CarbinFile carbin = new CarbinFile(this.zip.Read(entry));
                            CarbinFile.Section section = carbin.FindSection(this.editSection);
                            if (section == null)
                            {
                                continue;
                            }
                            carbin.TransformSection(section, matrix);
                            edited = carbin.ToBytes();
                        }
                    }
                    catch (NotSupportedException)
                    {
                        continue;   // a layout without part edits
                    }
                    this.zip.Replace(entry, edited);
                    this.RefreshEntryNode(entry);
                    done.Add(entry.Name.Substring(entry.Name.LastIndexOf('/') + 1));
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Edit Part", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            string what = (this.editMaterial != null) ? this.editSection + "." + this.editMaterial : this.editSection;
            this.ReloadModelKeepingView();
            this.SetStatus(string.Format("{0}: edit applied to {1} (pending: Save to write the archive)", what, string.Join(", ", done.ToArray())));
        }

        private void MergeSelectedPart()
        {
            string source = this.editSection;
            string target = this.cmbMergeTarget.SelectedItem as string;
            if (source == null || target == null)
            {
                return;
            }
            string question = string.Format("Merge \"{0}\" into \"{1}\"?\n\nMaterials with the same name are joined, the others are moved into {1}, and {0} is removed from {2}.\nThe change stays pending until you save (Archive > Revert Selected File undoes it).",
                source, target, this.chkAllModelFiles.Checked ? "every model file of the car" : "this file");
            if (MessageBox.Show(this, question, "Merge Parts", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            List<string> report = new List<string>();
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                foreach (ForzaZip.Entry entry in this.PartEditTargets())
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
                    string file = entry.Name.Substring(entry.Name.LastIndexOf('/') + 1);
                    bool hasSource = model.Find(source) != null;
                    bool hasTarget = model.Find(target) != null;
                    if (!hasSource)
                    {
                        continue;
                    }
                    if (!hasTarget)
                    {
                        report.Add(file + ": has " + source + " but no " + target + ", left unchanged");
                        continue;
                    }
                    model.MergeSection(source, target);
                    this.zip.Replace(entry, model.ToBytes());
                    this.RefreshEntryNode(entry);
                    report.Add(file + ": merged");
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Merge Parts", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            this.editSection = null;
            this.ReloadModelKeepingView();
            this.SetStatus(string.Format("{0} -> {1}: {2}", source, target, string.Join("; ", report.ToArray())));
        }

        private void RefreshEntryNode(ForzaZip.Entry entry)
        {
            TreeNode node = this.FindEntryNode(entry);
            if (node != null)
            {
                this.RefreshNode(node);
            }
        }

        // Reloads the shown model from the (edited) archive data, keeping the camera, the checked and
        // expanded parts and the selection.
        private void ReloadModelKeepingView()
        {
            List<string> checkedPaths = new List<string>();
            List<string> expandedPaths = new List<string>();
            CollectViewState(this.tvModel.Nodes, checkedPaths, expandedPaths);
            string selected = (this.tvModel.SelectedNode != null) ? this.tvModel.SelectedNode.FullPath : null;
            string section = this.editSection;

            this.editSection = null;
            this.ShowModel(this.currentEntry.Name, this.zip.Read(this.currentEntry));

            this.tvModel.AfterCheck -= new TreeViewEventHandler(this.tvModel_AfterCheck);
            this.tvModel.BeginUpdate();
            RestoreViewState(this.tvModel.Nodes, checkedPaths, expandedPaths);
            this.tvModel.EndUpdate();
            this.tvModel.AfterCheck += new TreeViewEventHandler(this.tvModel_AfterCheck);
            TreeNode select = (selected != null) ? FindByPath(this.tvModel.Nodes, selected) : null;
            if (select != null)
            {
                this.tvModel.SelectedNode = select;
            }
            this.UpdateModelInfo();
            if (this.scene != null)
            {
                this.scene.Update(true);
            }
        }

        private static void CollectViewState(TreeNodeCollection nodes, List<string> checkedPaths, List<string> expandedPaths)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Checked)
                {
                    checkedPaths.Add(node.FullPath);
                }
                if (node.IsExpanded)
                {
                    expandedPaths.Add(node.FullPath);
                }
                CollectViewState(node.Nodes, checkedPaths, expandedPaths);
            }
        }

        private static void RestoreViewState(TreeNodeCollection nodes, List<string> checkedPaths, List<string> expandedPaths)
        {
            foreach (TreeNode node in nodes)
            {
                node.Checked = checkedPaths.Contains(node.FullPath);
                if (expandedPaths.Contains(node.FullPath))
                {
                    node.Expand();
                }
                RestoreViewState(node.Nodes, checkedPaths, expandedPaths);
            }
        }

        private static TreeNode FindByPath(TreeNodeCollection nodes, string path)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.FullPath == path)
                {
                    return node;
                }
                TreeNode found = FindByPath(node.Nodes, path);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }
}
