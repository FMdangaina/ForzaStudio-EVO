namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Windows.Forms;

    // Mesh helpers for Attach (pick mode: ArchiveEditorForm.PickMode.cs) and Duplicate Mesh.
    // Only the shown file changes; the edit stays pending until Save.
    public partial class ArchiveEditorForm
    {
        private static bool IsAttachableMesh(TreeNode node)
        {
            ForzaMesh mesh = (node != null) ? node.Tag as ForzaMesh : null;
            return mesh != null && mesh.SubIndex >= 0 && node.Parent != null && node.Parent.Parent != null;
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
