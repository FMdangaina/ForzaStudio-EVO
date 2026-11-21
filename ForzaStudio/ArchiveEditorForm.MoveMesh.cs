namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Windows.Forms;

    // Drag and drop in the model tree: a mesh (material node) dropped on a part moves into that part,
    // dropped on a LOD node it goes into a new part named by the user. FH traffic models merge several
    // pieces in one part (all lights in taillightR, bumpers in body): this regroups them by hand.
    // Only the shown file changes; the edit stays pending until Save, like the other part edits.
    public partial class ArchiveEditorForm
    {
        private TreeNode dropHighlight;

        // Empty parts: tree nodes only (a carbin part without meshes is not written), shown in yellow
        // until a mesh is dropped on them, which makes them real. Kept for the shown file across reloads.
        private static readonly object EmptyPartTag = new object();
        private readonly List<string[]> emptyParts = new List<string[]>();   // { LOD node text, part name }
        private string emptyPartsEntry;

        private static bool IsLodNode(TreeNode node)
        {
            return node != null && node.Parent != null && node.Parent.Parent == null && !(node.Tag is ForzaMesh);
        }

        private static bool IsEmptyPart(TreeNode node)
        {
            return node != null && node.Tag == EmptyPartTag;
        }

        // Called while the model tree is built: re-adds the empty parts of this file, dropping those that
        // became real parts (a mesh was moved in) and forgetting them when another file is shown.
        private void AddEmptyParts(string entryName, TreeNode root)
        {
            if (entryName != this.emptyPartsEntry)
            {
                this.emptyParts.Clear();
                this.emptyPartsEntry = entryName;
            }
            for (int i = this.emptyParts.Count - 1; i >= 0; i--)
            {
                if (FindPart(root, this.emptyParts[i][1]))
                {
                    this.emptyParts.RemoveAt(i);
                }
            }
            foreach (string[] empty in this.emptyParts)
            {
                TreeNode lod = GetChild(root.Nodes, empty[0]);
                TreeNode part = new TreeNode(empty[1]);
                part.Tag = EmptyPartTag;
                part.ForeColor = ForzaTheme.Yellow;
                part.ToolTipText = "Empty part: drag a mesh here. Not written to the file while it stays empty.";
                lod.Nodes.Add(part);
            }
        }

        private static bool FindPart(TreeNode root, string name)
        {
            foreach (TreeNode lod in root.Nodes)
            {
                foreach (TreeNode part in lod.Nodes)
                {
                    if (part.Text == name && !IsEmptyPart(part))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void NewEmptyPart(TreeNode lodNode)
        {
            string name = this.AskNewPartName(string.Format("New empty part in {0}, named:", lodNode.Text));
            if (name == null)
            {
                return;
            }
            this.emptyParts.Add(new string[] { lodNode.Text, name });
            this.ReloadModelKeepingView();
            this.SetStatus(name + ": empty part (yellow). Drag meshes onto it; while it is empty it is not written to the file.");
        }

        private void RemoveEmptyPart(TreeNode node)
        {
            this.emptyParts.RemoveAll(delegate(string[] e) { return e[1] == node.Text; });
            node.Remove();
            this.SetStatus(node.Text + ": empty part removed.");
        }

        private void HookMeshDragDrop()
        {
            this.tvModel.AllowDrop = true;
            this.tvModel.ItemDrag += delegate(object sender, ItemDragEventArgs e)
            {
                TreeNode node = e.Item as TreeNode;
                ForzaMesh mesh = (node != null) ? node.Tag as ForzaMesh : null;
                if (mesh != null && mesh.SubIndex >= 0 && this.SelectedSectionName != null)
                {
                    this.tvModel.SelectedNode = node;
                    this.tvModel.DoDragDrop(node, DragDropEffects.Move);
                }
            };
            this.tvModel.DragOver += delegate(object sender, DragEventArgs e)
            {
                TreeNode target = this.tvModel.GetNodeAt(this.tvModel.PointToClient(new Point(e.X, e.Y)));
                TreeNode dragged = e.Data.GetData(typeof(TreeNode)) as TreeNode;
                bool ok = dragged != null && this.MeshDropTarget(dragged, target) != null;
                e.Effect = ok ? DragDropEffects.Move : DragDropEffects.None;
                this.HighlightDrop(ok ? target : null);
            };
            this.tvModel.DragLeave += delegate { this.HighlightDrop(null); };
            this.tvModel.DragDrop += delegate(object sender, DragEventArgs e)
            {
                this.HighlightDrop(null);
                TreeNode target = this.tvModel.GetNodeAt(this.tvModel.PointToClient(new Point(e.X, e.Y)));
                TreeNode dragged = e.Data.GetData(typeof(TreeNode)) as TreeNode;
                string part = (dragged != null) ? this.MeshDropTarget(dragged, target) : null;
                if (part != null)
                {
                    // Run after the drag ends, so a dialog can open.
                    this.BeginInvoke((MethodInvoker) delegate { this.MoveMesh(dragged, part); });
                }
            };
        }

        // The part a mesh node goes to when dropped on 'target': a part node (or one of its meshes)
        // other than its own, or "" for a LOD node (a new part). Null when it cannot be dropped there.
        private string MeshDropTarget(TreeNode dragged, TreeNode target)
        {
            if (target == null || !(dragged.Tag is ForzaMesh) || dragged.Parent == null)
            {
                return null;
            }
            if (target.Tag is ForzaMesh)
            {
                target = target.Parent;
            }
            if (target == null || target.Parent == null)
            {
                return null;                                   // the file node
            }
            if (target.Parent.Parent == null)
            {
                return "";                                     // a LOD node: new part
            }
            return (target.Text != dragged.Parent.Text) ? target.Text : null;
        }

        private void HighlightDrop(TreeNode node)
        {
            if (this.dropHighlight == node)
            {
                return;
            }
            if (this.dropHighlight != null)
            {
                this.dropHighlight.BackColor = Color.Empty;
                this.dropHighlight.ForeColor = Color.Empty;
            }
            this.dropHighlight = node;
            if (node != null)
            {
                node.BackColor = SystemColors.Highlight;
                node.ForeColor = SystemColors.HighlightText;
            }
        }

        private void MoveMesh(TreeNode meshNode, string part)
        {
            ForzaMesh mesh = (ForzaMesh) meshNode.Tag;
            string section = meshNode.Parent.Text;
            string material = meshNode.Text;
            if (part.Length == 0)
            {
                part = this.AskNewPartName(string.Format("Move {0} of {1} into a new part named:", material, section));
                if (part == null)
                {
                    return;
                }
            }
            string report;
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                ForzaZip.Entry entry = this.currentEntry;
                byte[] data = this.zip.Read(entry);
                Fm2CarbinModel fm2 = Fm2Model(data);
                if (fm2 != null)
                {
                    // FM2 (2026-11-21): its own writer, the shared buffers laid out again
                    int p, k;
                    if (!fm2.LocateMesh(section, material, TreeLod(meshNode), mesh.SectionIndex, mesh.SubIndex, out p, out k))
                    {
                        throw new InvalidOperationException("The mesh could not be found in the file: nothing moved.");
                    }
                    report = fm2.MoveMesh(p, k, part, -1);
                    this.zip.Replace(entry, fm2.ToBytes());
                    this.RefreshEntryNode(entry);
                    this.editSection = null;
                    this.ReloadModelKeepingView();
                    this.SetStatus(report + " (pending: Save to write the archive)");
                    return;
                }
                CarbinModel model = EditableModel(data);   // FM3 in its own layout (fm3t\mm3: vertex bytes kept)
                // The tree reads the file in the same order as CarbinModel; checked by name anyway.
                if (mesh.SectionIndex >= model.Sections.Count || model.Sections[mesh.SectionIndex].Name != section
                    || mesh.SubIndex >= model.Sections[mesh.SectionIndex].SubSections.Count
                    || model.Sections[mesh.SectionIndex].SubSections[mesh.SubIndex].Name != material)
                {
                    throw new InvalidOperationException("The mesh could not be found in the file: nothing moved.");
                }
                report = model.MoveSubSection(mesh.SectionIndex, mesh.SubIndex, part);
                this.zip.Replace(entry, model.ToBytes());
                this.RefreshEntryNode(entry);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Move Mesh", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            this.editSection = null;
            this.ReloadModelKeepingView();
            this.SetStatus(report + " (pending: Save to write the archive)");
        }

        // The name of the new part: the user's own (no name is made up), unique in this file.
        private string AskNewPartName(string prompt)
        {
            Panel content = new Panel();
            content.Size = new Size(360, 56);
            Label label = new Label();
            label.Text = prompt;
            label.AutoSize = true;
            label.Location = new Point(0, 4);
            TextBox name = new TextBox();
            name.Width = 220;
            name.Location = new Point(0, 26);
            content.Controls.AddRange(new Control[] { label, name });
            if (!this.ShowOptions("New Part", content))
            {
                return null;
            }
            string text = name.Text.Trim();
            if (text.Length == 0 || text.IndexOfAny(new char[] { ' ', '/', '\\', '<', '>' }) >= 0)
            {
                MessageBox.Show(this, "Give the new part a name without spaces.", "Move Mesh", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }
            foreach (TreeNode lod in this.tvModel.Nodes[0].Nodes)
            {
                foreach (TreeNode existing in lod.Nodes)
                {
                    if (existing.Text == text)
                    {
                        MessageBox.Show(this, "The model already has a part named " + text + ": drop meshes on that part instead.", "New Part", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return null;
                    }
                }
            }
            return text;
        }
    }
}
