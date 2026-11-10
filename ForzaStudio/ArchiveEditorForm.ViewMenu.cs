namespace ForzaStudio
{
    using Microsoft.Xna.Framework;
    using System;
    using System.Collections.Generic;
    using System.Windows.Forms;

    // Right click in the viewport (a click, not a drag): a menu as in 3ds Max for what is selected (one node of the
    // model tree, or the meshes picked with Ctrl): show only it, hide it, hide everything else, show everything
    // again, freeze, focus on it. Showing and hiding is the tree's check boxes, so the tree always says what is
    // drawn. "Show Only" remembers what was shown and "End Show Only" brings it back.
    public partial class ArchiveEditorForm
    {
        private ContextMenuStrip viewMenu;
        private ToolStripMenuItem mnuViewShowOnly, mnuViewEndOnly, mnuViewHide, mnuViewHideOthers, mnuViewFocus, mnuViewFreeze;
        private List<TreeNode> shownBeforeIsolate;

        // The meshes selected: one node of the tree (a mesh, a part, a LOD), or several picked with Ctrl.
        private readonly Set<ForzaMesh> meshSelection = new Set<ForzaMesh>();
        private bool keepMeshSelection;     // the tree's selection is being moved by a Ctrl click in the viewport

        // The tree nodes the menu works on: those of the Ctrl-picked meshes, else the selected node.
        private List<TreeNode> SelectionNodes()
        {
            List<TreeNode> nodes = new List<TreeNode>();
            if (this.meshSelection.Count > 1)
            {
                List<TreeNode> all = new List<TreeNode>();
                AllNodes(this.tvModel.Nodes, all);
                foreach (TreeNode node in all)
                {
                    ForzaMesh mesh = node.Tag as ForzaMesh;
                    if (mesh != null && this.meshSelection.Contains(mesh))
                    {
                        nodes.Add(node);
                    }
                }
            }
            else if (this.tvModel.SelectedNode != null)
            {
                nodes.Add(this.tvModel.SelectedNode);
            }
            return nodes;
        }

        private void ShowViewMenu(System.Drawing.Point at)
        {
            if (this.viewMenu == null)
            {
                this.viewMenu = new ContextMenuStrip();
                this.mnuViewShowOnly = new ToolStripMenuItem("Show Only This", null, delegate { this.ShowOnlySelected(); });
                this.mnuViewEndOnly = new ToolStripMenuItem("End Show Only (back to what was shown)", null, delegate { this.EndShowOnly(); });
                this.mnuViewHide = new ToolStripMenuItem("Hide Selection", null, delegate { this.HideSelected(false); });
                this.mnuViewHideOthers = new ToolStripMenuItem("Hide Unselected", null, delegate { this.HideSelected(true); });
                ToolStripMenuItem unhide = new ToolStripMenuItem("Unhide All", null, delegate { this.UnhideAll(); });
                this.mnuViewFocus = new ToolStripMenuItem("Focus On Selected", null, delegate { this.FocusOnSelected(); });
                ToolStripMenuItem unfreeze = new ToolStripMenuItem("Unfreeze All", null, delegate { this.FreezeSelected(false); });
                this.mnuViewFreeze = new ToolStripMenuItem("Freeze Selection", null, delegate { this.FreezeSelected(true); });
                ToolStripMenuItem unhideByName = new ToolStripMenuItem("Unhide by Name...", null, delegate { this.UnhideByName(); });
                this.viewMenu.Items.Add(this.mnuViewShowOnly);
                this.viewMenu.Items.Add(this.mnuViewEndOnly);
                this.viewMenu.Items.Add(new ToolStripSeparator());
                this.viewMenu.Items.Add(unfreeze);
                this.viewMenu.Items.Add(this.mnuViewFreeze);
                this.viewMenu.Items.Add(unhideByName);
                this.viewMenu.Items.Add(unhide);
                this.viewMenu.Items.Add(this.mnuViewHideOthers);
                this.viewMenu.Items.Add(this.mnuViewHide);
                this.viewMenu.Items.Add(new ToolStripSeparator());
                this.viewMenu.Items.Add(this.mnuViewFocus);
                this.AddCarColorMenu(this.viewMenu);
                ForzaTheme.Apply(this.viewMenu);
            }
            bool selected = this.SelectionNodes().Count > 0;
            this.mnuViewShowOnly.Enabled = selected;
            this.mnuViewHide.Enabled = selected;
            this.mnuViewFreeze.Enabled = selected;
            this.mnuViewHideOthers.Enabled = selected;
            this.mnuViewFocus.Enabled = selected;
            this.mnuViewEndOnly.Visible = this.shownBeforeIsolate != null;
            this.viewMenu.Show(this.pnlViewport, at);
        }

        private static void AllNodes(TreeNodeCollection nodes, List<TreeNode> into)
        {
            foreach (TreeNode node in nodes)
            {
                into.Add(node);
                AllNodes(node.Nodes, into);
            }
        }

        // Check boxes changed together, without the tree's own handler running for each.
        private void SetSubtreeChecked(TreeNode node, bool value)
        {
            this.tvModel.AfterCheck -= new TreeViewEventHandler(this.tvModel_AfterCheck);
            node.Checked = value;
            SetChecked(node.Nodes, value);
            this.tvModel.AfterCheck += new TreeViewEventHandler(this.tvModel_AfterCheck);
        }

        private void ShownChanged()
        {
            // The parts and LODs follow their meshes: hiding everything leaves no tick behind.
            this.tvModel.AfterCheck -= new TreeViewEventHandler(this.tvModel_AfterCheck);
            this.tvModel.BeginUpdate();
            try
            {
                SyncParentChecks(this.tvModel.Nodes);
            }
            finally
            {
                this.tvModel.EndUpdate();
                this.tvModel.AfterCheck += new TreeViewEventHandler(this.tvModel_AfterCheck);
            }
            this.UpdateModelInfo();
            if (this.scene != null)
            {
                this.scene.Update(true);
            }
        }

        private void ShowOnlySelected()
        {
            List<TreeNode> keep = this.SelectionNodes();
            if (keep.Count == 0)
            {
                return;
            }
            List<TreeNode> all = new List<TreeNode>();
            AllNodes(this.tvModel.Nodes, all);
            if (this.shownBeforeIsolate == null)
            {
                this.shownBeforeIsolate = all.FindAll(delegate(TreeNode node) { return node.Checked; });
            }
            this.tvModel.BeginUpdate();
            foreach (TreeNode node in this.tvModel.Nodes)
            {
                this.SetSubtreeChecked(node, false);
            }
            foreach (TreeNode node in keep)
            {
                this.SetSubtreeChecked(node, true);
            }
            this.tvModel.EndUpdate();
            this.ShownChanged();
            this.SetStatus("Showing only " + (keep.Count == 1 ? keep[0].Text : keep.Count + " meshes") + " (right click > End Show Only to go back)");
        }

        private void EndShowOnly()
        {
            if (this.shownBeforeIsolate == null)
            {
                return;
            }
            this.tvModel.BeginUpdate();
            List<TreeNode> all = new List<TreeNode>();
            AllNodes(this.tvModel.Nodes, all);
            this.tvModel.AfterCheck -= new TreeViewEventHandler(this.tvModel_AfterCheck);
            foreach (TreeNode node in all)
            {
                node.Checked = this.shownBeforeIsolate.Contains(node);
            }
            this.tvModel.AfterCheck += new TreeViewEventHandler(this.tvModel_AfterCheck);
            this.tvModel.EndUpdate();
            this.shownBeforeIsolate = null;
            this.ShownChanged();
        }

        // others: hide everything but the selection.
        private void HideSelected(bool others)
        {
            List<TreeNode> keep = this.SelectionNodes();
            if (keep.Count == 0)
            {
                return;
            }
            this.tvModel.BeginUpdate();
            if (!others)
            {
                foreach (TreeNode node in keep)
                {
                    this.SetSubtreeChecked(node, false);
                }
            }
            else
            {
                List<TreeNode> inside = new List<TreeNode>();
                foreach (TreeNode node in keep)
                {
                    inside.Add(node);
                    AllNodes(node.Nodes, inside);
                }
                List<TreeNode> all = new List<TreeNode>();
                AllNodes(this.tvModel.Nodes, all);
                this.tvModel.AfterCheck -= new TreeViewEventHandler(this.tvModel_AfterCheck);
                foreach (TreeNode node in all)
                {
                    if (!inside.Contains(node) && node.Parent != null && node.Nodes.Count == 0)
                    {
                        node.Checked = false;
                    }
                }
                this.tvModel.AfterCheck += new TreeViewEventHandler(this.tvModel_AfterCheck);
            }
            this.tvModel.EndUpdate();
            this.shownBeforeIsolate = null;
            this.ShownChanged();
        }

        // Everything of the LODs that are shown (a LOD with a part checked); the first LOD when none is.
        private void UnhideAll()
        {
            if (this.tvModel.Nodes.Count == 0)
            {
                return;
            }
            this.tvModel.BeginUpdate();
            foreach (TreeNode file in this.tvModel.Nodes)
            {
                bool any = false;
                foreach (TreeNode lod in file.Nodes)
                {
                    List<TreeNode> below = new List<TreeNode>();
                    AllNodes(lod.Nodes, below);
                    if (lod.Checked || below.Exists(delegate(TreeNode node) { return node.Checked; }))
                    {
                        this.SetSubtreeChecked(lod, true);
                        any = true;
                    }
                }
                if (!any && file.Nodes.Count > 0)
                {
                    this.SetSubtreeChecked(file.Nodes[0], true);
                }
            }
            this.tvModel.EndUpdate();
            this.shownBeforeIsolate = null;
            this.ShownChanged();
        }

        // The meshes under some nodes (a mesh node counts itself).
        private static List<ForzaMesh> MeshesOf(List<TreeNode> nodes)
        {
            List<ForzaMesh> meshes = new List<ForzaMesh>();
            foreach (TreeNode node in nodes)
            {
                ForzaMesh own = node.Tag as ForzaMesh;
                if (own != null)
                {
                    meshes.Add(own);
                }
                meshes.AddRange(GetMeshes(node.Nodes, false));
            }
            return meshes;
        }

        // Freeze: the meshes stay drawn, dark grey, and a click in the viewport does not pick them.
        private void FreezeSelected(bool freeze)
        {
            if (freeze)
            {
                foreach (ForzaMesh mesh in MeshesOf(this.SelectionNodes()))
                {
                    mesh.Frozen = true;
                }
            }
            else
            {
                foreach (ForzaMesh mesh in GetMeshes(this.tvModel.Nodes, false))
                {
                    mesh.Frozen = false;
                }
            }
            if (this.scene != null)
            {
                this.scene.Update(true);
            }
        }

        // A list of the hidden meshes (unchecked in the tree) to pick from.
        private void UnhideByName()
        {
            List<TreeNode> all = new List<TreeNode>();
            AllNodes(this.tvModel.Nodes, all);
            List<TreeNode> hidden = all.FindAll(delegate(TreeNode node) { return node.Tag is ForzaMesh && !node.Checked; });
            if (hidden.Count == 0)
            {
                this.SetStatus("Nothing is hidden.");
                return;
            }
            Panel content = new Panel();
            content.Size = new System.Drawing.Size(420, 300);
            CheckedListBox list = new CheckedListBox();
            list.CheckOnClick = true;
            list.Dock = DockStyle.Fill;
            foreach (TreeNode node in hidden)
            {
                string path = node.FullPath;
                int first = path.IndexOf('\\');
                list.Items.Add(first >= 0 ? path.Substring(first + 1) : path);
            }
            content.Controls.Add(list);
            if (!this.ShowOptions("Unhide by Name", content))
            {
                return;
            }
            this.tvModel.AfterCheck -= new TreeViewEventHandler(this.tvModel_AfterCheck);
            for (int i = 0; i < hidden.Count; i++)
            {
                if (list.GetItemChecked(i))
                {
                    hidden[i].Checked = true;
                }
            }
            this.tvModel.AfterCheck += new TreeViewEventHandler(this.tvModel_AfterCheck);
            this.shownBeforeIsolate = null;
            this.ShownChanged();
        }

        private void FocusOnSelected()
        {
            if (this.scene == null)
            {
                return;
            }
            Vector3 min = new Vector3(float.MaxValue), max = new Vector3(float.MinValue);
            bool any = false;
            foreach (ForzaMesh mesh in MeshesOf(this.SelectionNodes()))
            {
                if (mesh.Vertices == null)
                {
                    continue;
                }
                foreach (ForzaVertex vertex in mesh.Vertices)
                {
                    // stray game vertices (about 1.7e38) would send the camera out of reach: see ModelBounds
                    if (!PlausibleCoordinate(vertex.position.X) || !PlausibleCoordinate(vertex.position.Y) || !PlausibleCoordinate(vertex.position.Z))
                    {
                        continue;
                    }
                    min = Vector3.Min(min, vertex.position);
                    max = Vector3.Max(max, vertex.position);
                    any = true;
                }
            }
            if (!any)
            {
                return;
            }
            this.scene.Camera.FocusOn((min + max) / 2f, (max - min).Length() / 2f);
            this.scene.Update(true);
        }

        // A right click (not a drag): a mesh under the pointer that is not selected becomes the selection, then the menu.
        private void ViewportRightClick(System.Drawing.Point at)
        {
            this.SelectMeshAt(at, false, true);
            this.ShowViewMenu(at);
        }
    }
}
