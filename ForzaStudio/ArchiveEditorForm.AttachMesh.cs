namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Windows.Forms;

    // Attach (3ds Max): right-click a mesh in the model tree, Attach..., pick another mesh of the same LOD (same part or
    // another one): the picked mesh joins the right-clicked one, which keeps its name and material (CarbinModel.Attach).
    // Only the shown file changes; the edit stays pending until Save, like Move Mesh.
    public partial class ArchiveEditorForm
    {
        private sealed class AttachChoice
        {
            public TreeNode Node;
            public string Text;

            public override string ToString()
            {
                return this.Text;
            }
        }

        private static bool IsAttachableMesh(TreeNode node)
        {
            ForzaMesh mesh = (node != null) ? node.Tag as ForzaMesh : null;
            return mesh != null && mesh.SubIndex >= 0 && node.Parent != null && node.Parent.Parent != null;
        }

        private void AttachToSelectedMesh()
        {
            TreeNode targetNode = this.tvModel.SelectedNode;
            if (!IsAttachableMesh(targetNode) || this.currentEntry == null)
            {
                return;
            }
            ForzaMesh target = (ForzaMesh) targetNode.Tag;
            TreeNode lodNode = targetNode.Parent.Parent;

            // The other meshes of the same LOD node, every part.
            ListBox list = new ListBox();
            list.Size = new Size(420, 260);
            list.Location = new Point(0, 26);
            list.IntegralHeight = false;
            foreach (TreeNode part in lodNode.Nodes)
            {
                foreach (TreeNode node in part.Nodes)
                {
                    if (node == targetNode || !IsAttachableMesh(node))
                    {
                        continue;
                    }
                    AttachChoice choice = new AttachChoice();
                    choice.Node = node;
                    choice.Text = part.Text + " / " + node.Text + ((part == targetNode.Parent) ? "   (same part)" : "");
                    list.Items.Add(choice);
                }
            }
            if (list.Items.Count == 0)
            {
                MessageBox.Show(this, "There is no other mesh in this LOD to attach.", "Attach Mesh", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            list.SelectedIndex = 0;
            Panel content = new Panel();
            content.Size = new Size(420, 290);
            Label label = new Label();
            label.Text = string.Format("Attach to {0} / {1} (it keeps its name):", targetNode.Parent.Text, targetNode.Text);
            label.AutoSize = true;
            label.Location = new Point(0, 4);
            content.Controls.AddRange(new Control[] { label, list });
            list.DoubleClick += delegate
            {
                Form form = list.FindForm();
                if (form != null && list.SelectedItem != null)
                {
                    form.DialogResult = DialogResult.OK;
                }
            };
            if (!this.ShowOptions("Attach Mesh", content) || list.SelectedItem == null)
            {
                return;
            }
            TreeNode sourceNode = ((AttachChoice) list.SelectedItem).Node;
            ForzaMesh source = (ForzaMesh) sourceNode.Tag;

            string report;
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                ForzaZip.Entry entry = this.currentEntry;
                CarbinModel model = new CarbinModel(this.zip.Read(entry));
                CheckMeshInModel(model, target, targetNode);
                CheckMeshInModel(model, source, sourceNode);
                if (model.AttachTooLong(target.SectionIndex, target.SubIndex, source.SectionIndex, source.SubIndex))
                {
                    MessageBox.Show(this, string.Format("Together they would have {0:N0} indices: FM4 does not draw a mesh longer than {1:N0} to the end (holes in game), so saving would cut it again by height.\n\nNothing attached.",
                        model.AttachedIndexCount(target.SectionIndex, target.SubIndex, source.SectionIndex, source.SubIndex), CarbinModel.MaxGameSubSectionIndices),
                        "Attach Mesh", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                report = model.AttachSubSection(target.SectionIndex, target.SubIndex, source.SectionIndex, source.SubIndex);
                this.zip.Replace(entry, model.ToBytes());
                this.RefreshEntryNode(entry);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Attach Mesh", MessageBoxButtons.OK, MessageBoxIcon.Hand);
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

        // Duplicate of the right-clicked mesh in its own part (same material); only the shown file, pending until Save.
        private void DuplicateSelectedMesh()
        {
            TreeNode node = this.tvModel.SelectedNode;
            if (!IsAttachableMesh(node) || this.currentEntry == null)
            {
                return;
            }
            ForzaMesh mesh = (ForzaMesh) node.Tag;
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                ForzaZip.Entry entry = this.currentEntry;
                CarbinModel model = new CarbinModel(this.zip.Read(entry));
                CheckMeshInModel(model, mesh, node);
                model.DuplicateSubSection(mesh.SectionIndex, mesh.SubIndex);
                this.zip.Replace(entry, model.ToBytes());
                this.RefreshEntryNode(entry);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Duplicate Mesh", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            this.editSection = null;
            this.ReloadModelKeepingView();
            this.SetStatus(string.Format("{0} / {1}: duplicated in the same part (pending: Save to write the archive)", node.Parent.Text, node.Text));
        }

        // The tree reads the file in the same order as CarbinModel; checked by name anyway.
        private static void CheckMeshInModel(CarbinModel model, ForzaMesh mesh, TreeNode node)
        {
            if (mesh.SectionIndex >= model.Sections.Count || model.Sections[mesh.SectionIndex].Name != node.Parent.Text
                || mesh.SubIndex >= model.Sections[mesh.SectionIndex].SubSections.Count
                || model.Sections[mesh.SectionIndex].SubSections[mesh.SubIndex].Name != node.Text)
            {
                throw new InvalidOperationException("The mesh " + node.Parent.Text + " / " + node.Text + " could not be found in the file: nothing attached.");
            }
        }
    }
}
