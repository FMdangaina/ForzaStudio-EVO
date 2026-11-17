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
        private Button btnResetEdit;
        private Button btnFocusPart;
        private CheckBox chkSoloPart;
        private Button btnDeletePart;
        private Button btnDuplicatePart;
        private Button btnRenameMaterial;   // enabled when a material (mesh node) is selected
        private Button btnRenamePart;
        private readonly ToolTip partTips = new ToolTip();   // PART tab explanations (no notes in the panel)
        private bool fillingPartEdit;
        private string editSection;
        private string editMaterial;   // null: the whole part
        private Vector3 editCenter;
        private Vector3 editHalfSize;

        private const string PickPartText = "Pick a part (not a LOD) in the tree to change it.";

        private Label lblPartHeader;
        private const string NothingSelectedText = "Nothing selected";

        // "LOD1  >  bumperRo  >  black": the selected node's place in the model tree.
        private void UpdatePartHeader()
        {
            if (this.lblPartHeader == null)
            {
                return;
            }
            List<string> names = new List<string>();
            for (TreeNode node = this.tvModel.SelectedNode; node != null && node.Parent != null; node = node.Parent)
            {
                names.Insert(0, node.Text);
            }
            string text = (names.Count == 0) ? NothingSelectedText : string.Join("  ›  ", names.ToArray());
            if (this.lblPartHeader.Text != text)
            {
                this.lblPartHeader.Text = text;
                this.partTips.SetToolTip(this.lblPartHeader, text);
            }
        }

        // The red headings of a side tab open and close their section with a click (docs/UI_WORKFLOW.md A.3). A
        // section = the controls up to the next heading. Open or closed is kept in ForzaStudio.ini ([Editor]
        // Section<NAME>); closedAtFirst names the ones that start closed.
        private static void MakeCollapsible(FlowLayoutPanel side, params string[] closedAtFirst)
        {
            MakeCollapsibleTab("", side, closedAtFirst);
        }

        private static readonly System.Reflection.MethodInfo ControlGetState =
            typeof(Control).GetMethod("GetState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // The control's own Visible flag: Control.Visible is also false while a parent is hidden (a tab not shown yet).
        private static bool OwnVisible(Control control)
        {
            try
            {
                return (ControlGetState != null) ? (bool) ControlGetState.Invoke(control, new object[] { 2 }) : control.Visible;
            }
            catch (Exception)
            {
                return control.Visible;
            }
        }

        // tab: prefix of the ini key, so that equal headings of different tabs ("Write") keep their own state
        // (empty for PART and TEXTURE, whose keys came first). Every side tab is collapsible (user 2026-11-17:
        // "nella barra laterale tutte le voci vorrei fossero minimizzabili"). Closing hides only the controls that
        // were visible and opening shows those again: what a tab hides by itself stays hidden.
        private static void MakeCollapsibleTab(string tab, FlowLayoutPanel side, params string[] closedAtFirst)
        {
            foreach (Control control in side.Controls)
            {
                Label heading = control as Label;
                if (heading == null || heading.Name != SideHeadingName)
                {
                    continue;
                }
                string title = heading.Text;
                string key = "Editor.Section" + tab + title.Replace(" ", "").Replace(",", "").Replace("/", "").Replace("(", "").Replace(")", "");
                bool closed = Array.Exists(closedAtFirst, delegate(string name) { return string.Equals(name, title, StringComparison.OrdinalIgnoreCase); });
                bool open = AppSettings.Get(key, closed ? "0" : "1") != "0";
                heading.Cursor = Cursors.Hand;
                Label label = heading;
                List<Control> hidden = new List<Control>();
                EventHandler apply = delegate
                {
                    label.Text = (open ? "- " : "+ ") + title;
                    side.SuspendLayout();
                    if (open)
                    {
                        foreach (Control other in hidden)
                        {
                            other.Visible = true;
                        }
                        hidden.Clear();
                    }
                    else
                    {
                        bool inside = false;
                        foreach (Control other in side.Controls)
                        {
                            if (other == label)
                            {
                                inside = true;
                            }
                            else if (other.Name == SideHeadingName)
                            {
                                inside = false;
                            }
                            else if (inside && OwnVisible(other))
                            {
                                other.Visible = false;
                                hidden.Add(other);
                            }
                        }
                    }
                    side.ResumeLayout();
                };
                heading.Click += delegate
                {
                    open = !open;
                    AppSettings.Set(key, open ? "1" : "0");
                    apply(label, EventArgs.Empty);
                };
                apply(label, EventArgs.Empty);
            }
        }

        private void AddPartEditControls(FlowLayoutPanel side)
        {
            this.lblEditPart = SideNote(PickPartText);

            // Actions first, so they are on screen without scrolling (docs/UI_WORKFLOW.md step A); the
            // explanations are tooltips, the keys are on the buttons.
            // What is selected, always the first line of the tab (docs/UI_WORKFLOW.md A.3).
            this.lblPartHeader = new Label();
            this.lblPartHeader.AutoSize = false;
            this.lblPartHeader.Size = new Size(SideWidth, 24);
            this.lblPartHeader.TextAlign = ContentAlignment.MiddleLeft;
            this.lblPartHeader.Font = new Font(ForzaTheme.Regular, FontStyle.Bold);
            this.lblPartHeader.AutoEllipsis = true;
            this.lblPartHeader.Margin = new Padding(0, 4, 0, 0);
            this.lblPartHeader.Text = NothingSelectedText;
            side.Controls.Add(this.lblPartHeader);
            side.Controls.Add(SideHeading("Part", true));
            this.AddPickButtons(side);
            this.btnDuplicatePart = new Button();
            this.btnDuplicatePart.Text = "Duplicate (Ctrl+D)";
            this.btnDuplicatePart.Click += delegate { this.DuplicateSelectedPart(); };
            this.btnDeletePart = new Button();
            this.btnDeletePart.Text = "Delete (Del)";
            this.btnDeletePart.Click += delegate { this.DeleteSelectedNode(); };
            side.Controls.Add(SideButtonRow(this.btnDuplicatePart, this.btnDeletePart));
            this.btnRenamePart = new Button();
            this.btnRenamePart.Text = "Rename (F2)";
            this.btnRenamePart.Click += delegate { this.BeginRenamePart(); };
            this.btnRenameMaterial = new Button();
            this.btnRenameMaterial.Text = "Rename Material";
            this.btnRenameMaterial.Click += delegate { this.RenameSelectedMaterial(); };
            side.Controls.Add(SideButtonRow(this.btnRenamePart, this.btnRenameMaterial));
            this.btnFocusPart = new Button();
            this.btnFocusPart.Text = "Focus (Z)";
            this.btnFocusPart.Click += delegate { this.FocusPart(); };
            this.chkSoloPart = new CheckBox();
            this.chkSoloPart.Text = "Solo (Alt+Q)";
            this.chkSoloPart.Size = new Size(SideWidth / 2, 28);
            this.chkSoloPart.Margin = new Padding(8, 3, 0, 3);
            this.chkSoloPart.CheckedChanged += delegate { this.UpdatePartPreview(); this.UpdateModelInfo(); };
            FlowLayoutPanel viewRow = SideButtonRow(this.btnFocusPart);
            this.btnFocusPart.Width = SideWidth / 2 - 4;
            viewRow.Controls.Add(this.chkSoloPart);
            side.Controls.Add(viewRow);
            this.partTips.SetToolTip(this.btnDeletePart, "Deletes the part (or the selected material) in every model file when \"Also in lod0 and cockpit\" is ticked. No question: Ctrl+Z brings it back.");
            this.partTips.SetToolTip(this.btnDuplicatePart, "Copies the part under the next free name (exhausta -> exhaustb); type a new name right away in the tree.");
            this.partTips.SetToolTip(this.btnRenameMaterial, "Rename the selected material, with the FM4 material suggestions.");
            this.partTips.SetToolTip(this.chkSoloPart, "Show only this part.");
            this.partTips.SetToolTip(this.tvModel, "Drag a material onto a part to move that mesh there, onto a LOD for a new part (this file only).");

            side.Controls.Add(SideHeading("Move, rotate, scale", false));
            side.Controls.Add(this.lblEditPart);
            side.Controls.Add(SideCaption("Move (m)"));
            // Mouse wheel = 0.001 per notch on move, rotate and scale (user 2026-11-17); arrows keep their step.
            side.Controls.Add(this.NumberRow(out this.numMove, -20, 20, 3, 0.005m, 0, 0.001m));
            side.Controls.Add(SideCaption("Rotate (deg)"));
            side.Controls.Add(this.NumberRow(out this.numRotate, -360, 360, 3, 1, 0, 0.001m));
            side.Controls.Add(SideCaption("Scale (%)"));
            side.Controls.Add(this.NumberRow(out this.numScale, 1, 1000, 3, 1, 100, 0.001m));
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
            this.chkAllModelFiles.Text = "Also in lod0 and cockpit";
            this.chkAllModelFiles.Checked = true;
            this.chkAllModelFiles.Size = new Size(SideWidth - 70, 28);
            this.chkAllModelFiles.Margin = new Padding(0, 3, 0, 3);
            this.partTips.SetToolTip(this.chkAllModelFiles, "Move, delete, duplicate and merge act on every model file of the car (main, lod0, cockpit) that has the part, so the LODs stay aligned.");
            this.btnResetEdit = new Button();
            this.btnResetEdit.Text = "Reset";
            this.btnResetEdit.Size = new Size(66, 28);
            this.btnResetEdit.Margin = new Padding(0, 3, 0, 3);
            this.btnResetEdit.Click += delegate { this.ResetPartEdit(); };
            this.partTips.SetToolTip(this.btnResetEdit, "Back to the part as it was before this move. No Apply: a change is kept until Save; Ctrl+Z / Ctrl+Y go back and forth.");
            FlowLayoutPanel filesRow = new FlowLayoutPanel();
            filesRow.Size = new Size(SideWidth + 4, 34);
            filesRow.Margin = new Padding(0);
            filesRow.Controls.Add(this.chkAllModelFiles);
            filesRow.Controls.Add(this.btnResetEdit);
            side.Controls.Add(filesRow);

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
            ToolStripMenuItem delete = new ToolStripMenuItem("Delete", null, delegate { this.DeleteSelectedNode(); });
            ToolStripMenuItem focus = new ToolStripMenuItem("Focus On Part", null, delegate { this.FocusPart(); });
            ToolStripMenuItem duplicate = new ToolStripMenuItem("Duplicate Part", null, delegate { this.DuplicateSelectedPart(); });
            ToolStripMenuItem renamePart = new ToolStripMenuItem("Rename Part", null, delegate { this.BeginRenamePart(); });
            renamePart.ShortcutKeyDisplayString = "F2";
            ToolStripMenuItem renameMesh = new ToolStripMenuItem("Rename", null, delegate { this.BeginRenameNode(); });
            renameMesh.ShortcutKeyDisplayString = "F2";
            ToolStripMenuItem rename = new ToolStripMenuItem("Rename Material...", null, delegate { this.RenameSelectedMaterial(); });
            ToolStripMenuItem shader = new ToolStripMenuItem("Shader Settings...", null, delegate { this.OpenShaderSettingsOfSelection(); });
            ToolStripMenuItem attach = new ToolStripMenuItem("Attach", null, delegate { this.StartPick(PickMode.Attach); });
            ToolStripMenuItem mergePart = new ToolStripMenuItem("Merge Part", null, delegate { this.StartPick(PickMode.Merge); });
            ToolStripMenuItem duplicateMesh = new ToolStripMenuItem("Duplicate Mesh", null, delegate { this.DuplicateSelectedMesh(); });
            // Display only: the keys themselves are handled in HandlePartKey (ProcessCmdKey).
            delete.ShortcutKeyDisplayString = "Del";
            focus.ShortcutKeyDisplayString = "Z";
            duplicate.ShortcutKeyDisplayString = "Ctrl+D";
            attach.ShortcutKeyDisplayString = "Alt+A";
            mergePart.ShortcutKeyDisplayString = "Alt+M";
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
            menu.Items.Add(mergePart);
            menu.Items.Add(partSeparator);
            menu.Items.Add(delete);
            menu.Opening += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                // A right-click ends Attach / Merge (3ds Max): no menu for that click.
                if (this.suppressTreeMenu || this.pickMode != PickMode.None)
                {
                    this.suppressTreeMenu = false;
                    this.ExitPickMode("stopped");
                    e.Cancel = true;
                    return;
                }
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
                attach.Visible = duplicateMesh.Visible = mergePart.Visible = false;
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
                attach.Text = string.Format("Attach \"{0}\" of {1} (then click the mesh it goes into)", material, section);
                mergePart.Visible = true;
                mergePart.Text = string.Format("Merge Part \"{0}\" (then click the part it goes into)", section);
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
                delete.Text = (material != null) ? string.Format("Delete Material \"{0}\" from {1}", material, section) : string.Format("Delete Part \"{0}\"", section);
                duplicate.Text = string.Format("Duplicate Part \"{0}\"", section);
            };
            this.tvModel.ContextMenuStrip = menu;
            this.HookPartLabelEdit();
            this.tvModel.NodeMouseClick += delegate(object sender, TreeNodeMouseClickEventArgs e)
            {
                if (e.Button == MouseButtons.Right)
                {
                    if (this.pickMode != PickMode.None)
                    {
                        // ends the pick mode (the menu's Opening does it); the click is not a pick
                        this.suppressTreeMenu = true;
                        return;
                    }
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
            // No confirmation (user 2026-11-16, docs/UI_WORKFLOW.md step A): Ctrl+Z brings it back.
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
            this.SetStatus((report.Count > 0) ? string.Join("; ", report.ToArray()) + "   (Ctrl+Z: undo)" : "Nothing to delete.");
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
            // No name window (docs/UI_WORKFLOW.md step A): the copy takes the next free variant name (exhausta ->
            // exhaustb, as the car's own parts) and its name opens for editing in the tree; Enter / Esc keeps it.
            string newName = this.SuggestPartName(section);
            TreeNode partNode = this.tvModel.SelectedNode;
            if (partNode != null && partNode.Tag is ForzaMesh)
            {
                partNode = partNode.Parent;
            }
            string newPath = (partNode != null && partNode.Parent != null) ? partNode.Parent.FullPath + this.tvModel.PathSeparator + newName : null;
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
            this.SetStatus(string.Format("{0} duplicated as {1} in {2}   (type a new name now, Enter keeps it; Ctrl+Z: undo)", section, newName, string.Join(", ", report.ToArray())));
            TreeNode copy = (newPath != null) ? FindByPath(this.tvModel.Nodes, newPath) : null;
            if (copy != null)
            {
                this.tvModel.SelectedNode = copy;
                this.BeginRenamePart();
            }
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
            return this.NumberRow(out numbers, min, max, decimals, step, value, 0);
        }

        // wheelStep > 0: one wheel notch changes the value by exactly wheelStep (WinForms would apply
        // Increment once per scroll line, usually 3 times per notch).
        private FlowLayoutPanel NumberRow(out NumericUpDown[] numbers, decimal min, decimal max, int decimals, decimal step, decimal value, decimal wheelStep)
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
                if (wheelStep > 0)
                {
                    number.MouseWheel += delegate(object sender, MouseEventArgs e)
                    {
                        HandledMouseEventArgs handled = e as HandledMouseEventArgs;
                        if (handled != null)
                        {
                            handled.Handled = true;
                        }
                        NumericUpDown box = (NumericUpDown) sender;
                        if (!box.Enabled || e.Delta == 0)
                        {
                            return;
                        }
                        decimal next = box.Value + (e.Delta > 0 ? wheelStep : -wheelStep);
                        box.Value = Math.Max(box.Minimum, Math.Min(box.Maximum, next));
                    };
                }
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
            this.RecordFieldChange(changed);
            if (this.chkUniformScale.Checked && Array.IndexOf(this.numScale, changed) >= 0)
            {
                this.fillingPartEdit = true;
                foreach (NumericUpDown number in this.numScale)
                {
                    number.Value = changed.Value;
                }
                this.fillingPartEdit = false;
            }
            this.fieldCurrent = this.FieldSnapshot();
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
            this.btnResetEdit.Enabled = enabled;
            this.btnFocusPart.Enabled = enabled;
            this.btnDeletePart.Enabled = enabled;
            this.btnDuplicatePart.Enabled = enabled;
            this.btnRenamePart.Enabled = enabled;
            this.btnRenameMaterial.Enabled = enabled && this.SelectedMaterialName != null;
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
            this.UpdatePartHeader();
            string section = this.SelectedSectionName;
            string material = this.SelectedMaterialName;
            if (section == this.editSection && material == this.editMaterial && section != null)
            {
                return;
            }
            // Another part picked: the move of the previous one is written (memory, pending until Save).
            this.CommitPartEdit(PartReload.Deferred);
            this.ClearPartPreview();
            this.editSection = null;
            this.editMaterial = null;
            this.ResetPartValues();
            this.ClearFieldHistory();
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
                // FM2 / FM3: no geometry edits, but material names can be changed (MaterialSlots, ST-01).
                if (material != null && (this.shaderProfile == ShaderProfile.Fm2 || this.shaderProfile == ShaderProfile.Fm3))
                {
                    this.lblEditPart.Text = "Material: " + material + " of " + section + "\n" + ShaderProfiles.Name(this.shaderProfile) + " model: only Rename Material is available here.";
                    this.btnRenameMaterial.Enabled = true;
                }
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
            if (this.HasPendingPartEdit)
            {
                this.PushFieldUndo();
            }
            this.ResetPartValues();
            this.fieldCurrent = this.FieldSnapshot();
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
            // The part's meshes are looked up once per selection, not at every value change (a dragged number box
            // or handle asks many times a second); Solo walks the whole tree only when it or the part changes.
            string key = this.editSection + "\n" + this.editMaterial;
            bool solo = this.chkSoloPart.Checked;
            if (this.previewPart == null || this.previewPartKey != key || this.previewSolo != solo)
            {
                List<ForzaMesh> part = this.SectionMeshes(this.editSection);
                if (this.editMaterial != null)
                {
                    part = part.FindAll(delegate(ForzaMesh mesh) { return mesh.MaterialName == this.editMaterial; });
                }
                Dictionary<ForzaMesh, bool> inPart = new Dictionary<ForzaMesh, bool>();
                foreach (ForzaMesh mesh in part)
                {
                    inPart[mesh] = true;
                }
                foreach (ForzaMesh mesh in GetMeshes(this.tvModel.Nodes, false))
                {
                    mesh.Hidden = solo && !inPart.ContainsKey(mesh);
                }
                this.previewPart = part;
                this.previewPartKey = key;
                this.previewSolo = solo;
            }
            foreach (ForzaMesh mesh in this.previewPart)
            {
                mesh.PreviewTransform = matrix;
                mesh.Highlight = pending;
            }
            this.viewRedrawWanted = true;   // drawn by the view's timer: one redraw per tick
        }

        private List<ForzaMesh> previewPart;    // the meshes UpdatePartPreview moves; null = look them up again
        private string previewPartKey;
        private bool previewSolo;
        private bool viewRedrawWanted;

        private void ClearPartPreview()
        {
            this.previewPart = null;
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

        // How the model view follows a move written by CommitPartEdit: Now (reload at once), Deferred (after the running
        // action: when the write happens inside another action's read, whose tree nodes must stay valid), None (the
        // caller shows another file anyway).
        private enum PartReload { Now, Deferred, None }

        private bool committingPartEdit;
        private bool partReloadQueued;

        // No "Apply" (user 2026-11-16): the move / rotate / scale stays a live preview and is written into the archive's
        // pending data (memory, until Save) by itself when another part or file is picked, when anything reads one of
        // the files it changes, before Undo / Redo and before Save. Each write is one undo step.
        private bool CommitPartEdit(PartReload reload)
        {
            if (this.committingPartEdit || this.editSection == null || this.zip == null || this.currentEntry == null || !this.HasPendingPartEdit)
            {
                return false;
            }
            Matrix matrix = this.PartEditMatrix;
            List<string> done = new List<string>();
            this.committingPartEdit = true;
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
                return false;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
                this.committingPartEdit = false;
            }
            string what = (this.editMaterial != null) ? this.editSection + "." + this.editMaterial : this.editSection;
            string text = string.Format("{0}: moved / rotated / scaled in {1} (pending: Save to write the archive; Ctrl+Z undoes)", what, string.Join(", ", done.ToArray()));
            this.zip.CloseStep(what + " moved / rotated / scaled");
            // The geometry now holds the move: the fields start again from 0 / 100. The meshes keep their preview
            // transform until the view reloads, so the part does not jump meanwhile.
            this.fillingPartEdit = true;
            this.ResetPartValues();
            this.fillingPartEdit = false;
            this.ClearFieldHistory();
            switch (reload)
            {
                case PartReload.Now:
                    this.ReloadModelKeepingView();
                    break;
                case PartReload.Deferred:
                    if (!this.partReloadQueued)
                    {
                        this.partReloadQueued = true;
                        ForzaZip.Entry shown = this.currentEntry;
                        this.BeginInvoke(new MethodInvoker(delegate
                        {
                            this.partReloadQueued = false;
                            if (this.currentEntry == shown && this.currentEntry != null)
                            {
                                this.ReloadModelKeepingView();
                            }
                        }));
                    }
                    break;
            }
            this.SetStatus(text);
            return true;
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
            bool wasReloading = this.reloadingView;
            this.reloadingView = true;   // the selection put back below is not a pick (Attach / Merge mode)
            try
            {
                this.ReloadModelCore(checkedPaths, expandedPaths, selected);
            }
            finally
            {
                this.reloadingView = wasReloading;
            }
        }

        private void ReloadModelCore(List<string> checkedPaths, List<string> expandedPaths, string selected)
        {
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
