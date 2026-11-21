namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Windows.Forms;

    // Attach and Merge as pick modes, like 3ds Max (user 2026-11-16): select a mesh (Attach) or a part (Merge), press
    // the button, then click the destination in the model tree or on the model. The SELECTED one goes INTO the clicked
    // one and disappears ("seleziono chrome di exhaustbx, attach, clicco chrome di exhaustb: chrome sparisce da
    // exhaustbx"); a part left without meshes goes too. After one join the mode goes off (user, same day: "quando finisco
    // di attaccare spengo tool"); Esc, a right-click or the button stop it before. No dialogs: every join is one Undo step.
    // Attach: meshes of the same LOD, the shown file only. Merge: whole parts, every model file when "Also in the car's
    // other model files" is ticked; a destination missing from a file (e.g. a new empty part) takes the part's name there.
    public partial class ArchiveEditorForm
    {
        private enum PickMode { None, Attach, Merge }

        private PickMode pickMode;
        private string pickSourcePath;   // tree path of the mesh / part to move; null = the next click chooses it
        private Button btnAttachPick;
        private Button btnMergePick;
        private bool reloadingView;      // the tree is being rebuilt: its selections are not the user's clicks
        private bool suppressTreeMenu;   // a right-click ended a pick mode: no context menu for it

        private void AddPickButtons(FlowLayoutPanel side)
        {
            this.btnAttachPick = new Button();
            this.btnAttachPick.Text = AttachLabel;
            this.btnAttachPick.Click += delegate { this.StartPick(PickMode.Attach); };
            this.btnMergePick = new Button();
            this.btnMergePick.Text = MergeLabel;
            this.btnMergePick.Click += delegate { this.StartPick(PickMode.Merge); };
            side.Controls.Add(SideButtonRow(this.btnAttachPick, this.btnMergePick));
            this.partTips.SetToolTip(this.btnAttachPick, "Select a mesh, Alt+A, click the mesh it goes into (same LOD). The selected one disappears into the clicked one; then the tool goes off (Esc or right-click: stop before).");
            this.partTips.SetToolTip(this.btnMergePick, "Select a part, Alt+M, click the part it goes into (a new empty part works too). The selected one disappears into the clicked one; then the tool goes off (Esc or right-click: stop before).");
            this.tvModel.BeforeSelect += this.OnPickBeforeSelect;
            this.tvModel.MouseMove += delegate(object sender, MouseEventArgs e) { this.PickHover(this.tvModel.GetNodeAt(e.Location)); };
            this.tvModel.MouseLeave += delegate { this.PickHover(null); };
        }

        // The destination under the pointer while a pick mode waits for it: shown green in the tree and named in the
        // strip as TARGET (user 2026-11-16: "indichiamo target e source").
        private TreeNode pickHoverNode;
        private static readonly System.Drawing.Color PickTargetColor = System.Drawing.Color.FromArgb(30, 110, 50);

        private void PickHover(TreeNode node)
        {
            TreeNode target = null;
            if (this.pickMode != PickMode.None && this.pickSourcePath != null && node != null && !this.reloadingView)
            {
                TreeNode source = this.FindByPickKey(this.pickSourcePath);
                if (source != null && this.IsPickTarget(node, source))
                {
                    target = this.PickNode(node);
                }
            }
            if (target == this.pickHoverNode)
            {
                return;
            }
            this.ClearPickHover();
            this.pickHoverNode = target;
            if (target != null)
            {
                target.BackColor = PickTargetColor;
                target.ForeColor = System.Drawing.Color.White;
            }
            if (this.pickMode != PickMode.None)
            {
                this.UpdatePickUi(null);
            }
        }

        private void ClearPickHover()
        {
            if (this.pickHoverNode != null && this.pickHoverNode.TreeView != null)
            {
                this.pickHoverNode.BackColor = System.Drawing.Color.Empty;
                this.pickHoverNode.ForeColor = System.Drawing.Color.Empty;
            }
            this.pickHoverNode = null;
        }

        private const string AttachLabel = "Attach (Alt+A)";
        private const string MergeLabel = "Merge (Alt+M)";
        private Label pickBanner;

        // The red strip over the viewport while Attach / Merge is on: says what the next click does, where the eyes
        // already are (the status bar alone was missed).
        private Control BuildPickBanner()
        {
            this.pickBanner = new Label();
            this.pickBanner.Dock = DockStyle.Top;
            this.pickBanner.Height = 20;     // a thin strip (user 2026-11-16: the 30 px one was too much)
            this.pickBanner.BackColor = ForzaTheme.Red;
            this.pickBanner.ForeColor = System.Drawing.Color.White;
            this.pickBanner.Font = new System.Drawing.Font(ForzaTheme.Regular, System.Drawing.FontStyle.Bold);
            this.pickBanner.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.pickBanner.Padding = new Padding(8, 0, 0, 0);
            this.pickBanner.Visible = false;
            this.pickBanner.Cursor = Cursors.Hand;
            this.pickBanner.Click += delegate { this.ExitPickMode("stopped"); };
            return this.pickBanner;
        }

        private void StartPick(PickMode mode)
        {
            if (this.pickMode == mode)
            {
                this.ExitPickMode("stopped");
                return;
            }
            if (this.currentEntry == null || !this.currentEntry.Name.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase))
            {
                this.SetStatus("Attach / Merge: open a car model (.carbin) first.");
                return;
            }
            this.CommitPartEdit(PartReload.Now);   // a move still in preview is written first
            this.pickMode = mode;
            TreeNode node = this.tvModel.SelectedNode;
            this.pickSourcePath = this.IsPickSource(node) ? PickKey(PickNode(node)) : null;
            this.UpdatePickUi(null);
        }

        private void ExitPickMode(string why)
        {
            if (this.pickMode == PickMode.None)
            {
                return;
            }
            string name = (this.pickMode == PickMode.Attach) ? "Attach" : "Merge Part";
            this.pickMode = PickMode.None;
            this.pickSourcePath = null;
            this.UpdatePickUi(null);
            if (why != null)
            {
                this.SetStatus(name + " " + why + ".");
            }
        }

        // The node a pick uses: the mesh node for Attach, the part node (also from one of its meshes) for Merge.
        private TreeNode PickNode(TreeNode node)
        {
            if (this.pickMode == PickMode.Merge && node != null && node.Tag is ForzaMesh && node.Parent != null)
            {
                return node.Parent;
            }
            return node;
        }

        private bool IsPickSource(TreeNode node)
        {
            if (node == null)
            {
                return false;
            }
            if (this.pickMode == PickMode.Attach)
            {
                return IsAttachableMesh(node);
            }
            TreeNode part = this.PickNode(node);
            return IsPartNode(part) && !IsEmptyPart(part);
        }

        private bool IsPickTarget(TreeNode node, TreeNode source)
        {
            if (node == null || source == null)
            {
                return false;
            }
            if (this.pickMode == PickMode.Attach)
            {
                return IsAttachableMesh(node) && node != source && node.Parent.Parent == source.Parent.Parent;
            }
            TreeNode part = this.PickNode(node);
            return part != null && part != source && (IsPartNode(part) || IsEmptyPart(part)) && part.Text != source.Text;
        }

        private void UpdatePickUi(string done)
        {
            if (this.btnAttachPick == null)
            {
                return;
            }
            bool attach = this.pickMode == PickMode.Attach;
            bool merge = this.pickMode == PickMode.Merge;
            this.btnAttachPick.BackColor = attach ? ForzaTheme.Selection : ForzaTheme.Raised;
            this.btnMergePick.BackColor = merge ? ForzaTheme.Selection : ForzaTheme.Raised;
            this.btnAttachPick.Text = attach ? "Attach: ON" : AttachLabel;
            this.btnMergePick.Text = merge ? "Merge: ON" : MergeLabel;
            if (this.pickMode == PickMode.None)
            {
                this.ClearPickHover();
                if (this.pickBanner != null)
                {
                    this.pickBanner.Visible = false;
                }
                return;
            }
            // SOURCE = what moves (the selected one), TARGET = what it goes into (the one under the pointer, green).
            string what = attach ? "mesh" : "part";
            string sourceText, targetText;
            if (this.pickSourcePath == null)
            {
                sourceText = "click the " + what + " to move";
                targetText = "then the " + what + " it goes into";
            }
            else
            {
                TreeNode source = this.FindByPickKey(this.pickSourcePath);
                sourceText = (source == null) ? this.pickSourcePath.Split('\n')[0] : (attach ? source.Parent.Text + " / " + source.Text : source.Text);
                TreeNode hover = this.pickHoverNode;
                targetText = (hover != null) ? (attach ? hover.Parent.Text + " / " + hover.Text : hover.Text) + "  (click)"
                    : "click the " + what + " it goes into" + (attach ? " (same LOD)" : "");
            }
            string text = string.Format("{0}   SOURCE: {1}   →   TARGET: {2}      Esc: stop", attach ? "ATTACH" : "MERGE", sourceText, targetText);
            this.SetStatus((done != null) ? done + "   |   " + text : text);
            if (this.pickBanner != null)
            {
                this.pickBanner.Text = (done != null) ? done + "   >   " + text : text;
                this.pickBanner.BackColor = ForzaTheme.Red;   // again: the theme pass may have recoloured it
                this.pickBanner.ForeColor = System.Drawing.Color.White;
                this.pickBanner.Visible = true;
            }
        }

        // In a pick mode a click does not select: it chooses the one to move, then its destination.
        private void OnPickBeforeSelect(object sender, TreeViewCancelEventArgs e)
        {
            if (this.pickMode == PickMode.None || this.reloadingView || e.Node == null)
            {
                return;
            }
            if (this.pickSourcePath == null)
            {
                if (this.IsPickSource(e.Node))
                {
                    this.pickSourcePath = PickKey(this.PickNode(e.Node));   // selected normally, then the destination
                    this.UpdatePickUi(null);
                }
                return;
            }
            TreeNode source = this.FindByPickKey(this.pickSourcePath);
            if (source == null)
            {
                this.pickSourcePath = null;
                this.UpdatePickUi(null);
                return;
            }
            TreeNode picked = this.PickNode(e.Node);
            if (picked == source)
            {
                return;
            }
            e.Cancel = true;
            if (!this.IsPickTarget(e.Node, source))
            {
                this.UpdatePickUi((this.pickMode == PickMode.Attach) ? "Not a mesh of the same LOD" : "Not another part");
                return;
            }
            string sourcePath = this.pickSourcePath;
            string targetPath = PickKey(picked);
            this.BeginInvoke(new MethodInvoker(delegate { this.DoPick(sourcePath, targetPath); }));
        }

        private void DoPick(string sourcePath, string targetPath)
        {
            TreeNode source = this.FindByPickKey(sourcePath);
            TreeNode target = this.FindByPickKey(targetPath);
            if (source == null || target == null)
            {
                return;
            }
            string report = (this.pickMode == PickMode.Attach) ? this.AttachMeshes(source, target) : this.MergeParts(source.Text, target.Text);
            if (report == null)
            {
                return;   // refused (message shown): the mode and the selection stay
            }
            this.zip.CloseStep(report);
            // One join, then the tool goes off (user 2026-11-16: "quando finisco di attaccare spengo tool").
            this.pickMode = PickMode.None;
            this.pickSourcePath = null;
            this.editSection = null;
            this.ReloadModelKeepingView();
            this.UpdatePickUi(null);
            this.SetStatus(report + "   (Ctrl+Z: undo)");
        }

        // A tree node by path AND rank among same-named siblings: a part can hold two meshes called "chrome", and the
        // path alone always found the first (attaching it into itself failed, user 2026-11-16).
        private static string PickKey(TreeNode node)
        {
            TreeNodeCollection siblings = (node.Parent != null) ? node.Parent.Nodes : node.TreeView.Nodes;
            int rank = 0;
            foreach (TreeNode sibling in siblings)
            {
                if (sibling == node)
                {
                    break;
                }
                if (sibling.Text == node.Text)
                {
                    rank++;
                }
            }
            return node.FullPath + "\n" + rank;
        }

        private TreeNode FindByPickKey(string key)
        {
            if (key == null)
            {
                return null;
            }
            int cut = key.LastIndexOf('\n');
            string path = key.Substring(0, cut);
            int rank = int.Parse(key.Substring(cut + 1));
            TreeNode first = FindByPath(this.tvModel.Nodes, path);
            if (first == null)
            {
                return null;
            }
            TreeNodeCollection siblings = (first.Parent != null) ? first.Parent.Nodes : this.tvModel.Nodes;
            foreach (TreeNode sibling in siblings)
            {
                if (sibling.Text == first.Text && rank-- == 0)
                {
                    return sibling;
                }
            }
            return null;
        }

        // The selected mesh (source) joins the clicked one (target), which keeps its name and material; the source goes,
        // and its part too when it was its last mesh (CarbinModel.AttachSubSection). Null when refused.
        private string AttachMeshes(TreeNode sourceNode, TreeNode targetNode)
        {
            ForzaMesh target = (ForzaMesh) targetNode.Tag;
            ForzaMesh source = (ForzaMesh) sourceNode.Tag;
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                ForzaZip.Entry entry = this.currentEntry;
                byte[] data = this.zip.Read(entry);
                Fm2CarbinModel fm2 = Fm2Model(data);
                if (fm2 != null)
                {
                    // FM2 (2026-11-21): its own writer; the source mesh joins the clicked one
                    int tp, tk, sp, sk;
                    LocateFm2Mesh(fm2, target, targetNode, out tp, out tk);
                    LocateFm2Mesh(fm2, source, sourceNode, out sp, out sk);
                    string joined = fm2.MoveMesh(sp, sk, fm2.Parts[tp].Name, tk);
                    this.zip.Replace(entry, fm2.ToBytes());
                    this.RefreshEntryNode(entry);
                    return string.Format("{0} / {1} attached into {2} / {3} ({4})", sourceNode.Parent.Text, sourceNode.Text, targetNode.Parent.Text, targetNode.Text, joined);
                }
                CarbinModel model = EditableModel(data);   // FM3 in its own layout
                CheckMeshInModel(model, target, targetNode);
                CheckMeshInModel(model, source, sourceNode);
                if (model.AttachTooLong(target.SectionIndex, target.SubIndex, source.SectionIndex, source.SubIndex))
                {
                    MessageBox.Show(this, string.Format("Together they would have {0:N0} indices: FM4 does not draw a mesh longer than {1:N0} to the end (holes in game), so saving would cut it again by height.\n\nNothing attached.",
                        model.AttachedIndexCount(target.SectionIndex, target.SubIndex, source.SectionIndex, source.SubIndex), CarbinModel.MaxGameSubSectionIndices),
                        "Attach", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return null;
                }
                string report = model.AttachSubSection(target.SectionIndex, target.SubIndex, source.SectionIndex, source.SubIndex);
                this.zip.Replace(entry, model.ToBytes());
                this.RefreshEntryNode(entry);
                return string.Format("{0} / {1} attached into {2} / {3} ({4})", sourceNode.Parent.Text, sourceNode.Text, targetNode.Parent.Text, targetNode.Text, report.Replace("\r\n", "; ").TrimEnd(' ', ';'));
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Attach", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return null;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        // The selected part (source) joins the clicked one (target) in the shown file or in every model file: materials of
        // the same name are joined, the others moved, the source goes. In a file without the target (a new empty part, a
        // part only in some files) the source takes the target's name. Null when nothing changed.
        private string MergeParts(string source, string target)
        {
            List<string> report = new List<string>();
            int changed = 0;
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                foreach (ForzaZip.Entry entry in this.PartEditTargets())
                {
                    byte[] data = this.zip.Read(entry);
                    Fm2CarbinModel fm2 = Fm2Model(data);
                    if (fm2 != null)
                    {
                        // FM2 (2026-11-21): its own writer
                        if (fm2.Find(source) == null)
                        {
                            continue;
                        }
                        string done = fm2.MergeParts(source, target);
                        report.Add(entry.Name.Substring(entry.Name.LastIndexOf('/') + 1) + ": " + done);
                        this.zip.Replace(entry, fm2.ToBytes());
                        this.RefreshEntryNode(entry);
                        changed++;
                        continue;
                    }
                    CarbinModel model;
                    try
                    {
                        model = EditableModel(data);   // FM3 in its own layout
                    }
                    catch (NotSupportedException)
                    {
                        continue;
                    }
                    string file = entry.Name.Substring(entry.Name.LastIndexOf('/') + 1);
                    CarbinModel.Section from = model.Find(source);
                    if (from == null)
                    {
                        continue;
                    }
                    if (model.Find(target) != null)
                    {
                        model.MergeSection(source, target);
                        report.Add(file + ": merged");
                    }
                    else
                    {
                        from.Name = target;
                        report.Add(file + ": renamed (no " + target + " there)");
                    }
                    this.zip.Replace(entry, model.ToBytes());
                    this.RefreshEntryNode(entry);
                    changed++;
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Merge Part", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return null;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            if (changed == 0)
            {
                MessageBox.Show(this, "No model file has " + source + ": nothing merged.", "Merge Part", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }
            return string.Format("{0} merged into {1}: {2}", source, target, string.Join(", ", report.ToArray()));
        }

        // Esc ends a pick mode (called from ProcessCmdKey).
        private bool HandlePickKey(Keys keyData)
        {
            if (keyData == Keys.Escape && this.pickMode != PickMode.None)
            {
                this.ExitPickMode("stopped");
                return true;
            }
            return this.HandlePartKey(keyData);
        }

        // 3ds Max-like part keys (docs/UI_WORKFLOW.md step A), only while a car model is shown. Ctrl / Alt keys work
        // from anywhere but a text box; the plain keys (Del, F2, Z) only with the model tree or the viewport focused,
        // so typing in a box or the tree's type-ahead on other pages is untouched.
        private bool HandlePartKey(Keys keyData)
        {
            // Only with the PART tab open (user 2026-11-16: "alt-a ed alt-m non devono funzionare sempre"); on the MESH
            // tab Delete belongs to the faces.
            if (this.pnlModel == null || !this.pnlModel.Visible || this.currentEntry == null
                || this.tabSide == null || this.tabSide.SelectedTab != this.tabSidePart
                || !this.currentEntry.Name.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase) || this.FocusIsTextEntry()
                || (this.tvModel.LabelEdit && IsEditingLabel(this.tvModel)))
            {
                return false;
            }
            switch (keyData)
            {
                case Keys.Alt | Keys.A:
                    this.StartPick(PickMode.Attach);
                    return true;
                case Keys.Alt | Keys.M:
                    this.StartPick(PickMode.Merge);
                    return true;
                case Keys.Control | Keys.D:
                    this.DuplicateSelectedPart();
                    return true;
                case Keys.Alt | Keys.Q:
                    this.chkSoloPart.Checked = !this.chkSoloPart.Checked;
                    return true;
            }
            bool modelFocused = this.tvModel.Focused || this.pnlViewport.Focused;
            if (!modelFocused)
            {
                return false;
            }
            switch (keyData)
            {
                case Keys.Delete:
                    this.DeleteSelectedNode();
                    return true;
                case Keys.F2:
                    this.BeginRenameNode();
                    return true;
                case Keys.Z:
                    this.FocusPart();
                    return true;
            }
            return false;
        }
    }
}
