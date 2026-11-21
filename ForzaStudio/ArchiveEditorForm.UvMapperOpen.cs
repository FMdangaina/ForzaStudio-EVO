namespace ForzaStudio
{
    using Microsoft.Xna.Framework;
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.IO;
    using System.Windows.Forms;

    // MESH tab > UV Mapper...: asks which UV channel to edit and how to start (keep, copy the other channel,
    // project from the world axes or from the viewport), then opens the UV mapper window. The viewport shows
    // every change at once (the meshes are redrawn from the working copy); Apply writes the model file
    // (pending until Save), Cancel puts the viewport back.
    public partial class ArchiveEditorForm
    {
        private UvMapperForm uvMapper;
        private UvSession uvSession;
        private ForzaZip.Entry uvEntry;
        private bool uvDiscard;
        private readonly List<ForzaMesh> uvMeshes = new List<ForzaMesh>();

        private void OpenUvMapper()
        {
            if (this.uvMapper != null)
            {
                this.uvMapper.Activate();
                return;
            }
            if (this.scene == null || this.currentEntry == null || !this.currentEntry.Name.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "Open a car model (.carbin).", "UV Mapper", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            // What to edit: the selected faces, else the meshes selected in the tree.
            List<int[]> meshes = new List<int[]>();
            List<List<int>> faces = new List<List<int>>();
            if (this.faceSelection.Count > 0 && this.EnsurePickData())
            {
                foreach (KeyValuePair<long, List<int>> pair in this.SelectedFacesPerMesh())
                {
                    int s, k, f;
                    FaceKeyParts(pair.Key, out s, out k, out f);
                    meshes.Add(new int[] { s, k });
                    faces.Add(pair.Value);
                }
            }
            else
            {
                foreach (ForzaMesh mesh in this.SelectedMeshes)
                {
                    if (mesh.SectionIndex < 0 || mesh.SubIndex < 0)
                    {
                        continue;
                    }
                    bool known = false;
                    foreach (int[] m in meshes)
                    {
                        known |= m[0] == mesh.SectionIndex && m[1] == mesh.SubIndex;
                    }
                    if (!known)
                    {
                        meshes.Add(new int[] { mesh.SectionIndex, mesh.SubIndex });
                        faces.Add(null);
                    }
                }
            }
            // The meshes shown, to tick the ones to map: what is selected starts ticked.
            List<ForzaMesh> candidates = new List<ForzaMesh>();
            foreach (ForzaMesh mesh in GetMeshes(this.tvModel.Nodes, true))
            {
                if (!mesh.Hidden && mesh.SectionIndex >= 0 && mesh.SubIndex >= 0)
                {
                    candidates.Add(mesh);
                }
            }
            if (candidates.Count == 0)
            {
                MessageBox.Show(this, "Show a model (check its parts in the tree) first.", "UV Mapper", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // ---- how to start ----
            Panel content = new Panel();
            content.Size = new Size(380, 560);
            Label meshLabel = new Label();
            meshLabel.Text = "Meshes to map:";
            meshLabel.AutoSize = true;
            meshLabel.Location = new System.Drawing.Point(0, 270);
            ComboBox material = new ComboBox();
            material.DropDownStyle = ComboBoxStyle.DropDownList;
            material.Width = 170;
            material.Location = new System.Drawing.Point(100, 266);
            material.Items.Add("(tick all of a material)");
            List<string> materials = new List<string>();
            foreach (ForzaMesh mesh in candidates)
            {
                if (!materials.Contains(mesh.MaterialName))
                {
                    materials.Add(mesh.MaterialName);
                }
            }
            materials.Sort();
            foreach (string name in materials)
            {
                material.Items.Add(name);
            }
            material.SelectedIndex = 0;
            CheckedListBox meshList = new CheckedListBox();
            meshList.CheckOnClick = true;
            meshList.Size = new Size(380, 220);
            meshList.Location = new System.Drawing.Point(0, 296);
            foreach (ForzaMesh mesh in candidates)
            {
                bool ticked = false;
                foreach (int[] m in meshes)
                {
                    ticked |= m[0] == mesh.SectionIndex && m[1] == mesh.SubIndex;
                }
                meshList.Items.Add(mesh.Name, ticked);
            }
            material.SelectedIndexChanged += delegate
            {
                if (material.SelectedIndex <= 0)
                {
                    return;
                }
                for (int i = 0; i < candidates.Count; i++)
                {
                    meshList.SetItemChecked(i, candidates[i].MaterialName == (string) material.SelectedItem);
                }
            };
            Button tickAll = new Button();
            tickAll.Text = "All";
            tickAll.Size = new Size(48, 24);
            tickAll.Location = new System.Drawing.Point(280, 264);
            tickAll.Click += delegate { for (int i = 0; i < candidates.Count; i++) { meshList.SetItemChecked(i, true); } };
            Button tickNone = new Button();
            tickNone.Text = "None";
            tickNone.Size = new Size(48, 24);
            tickNone.Location = new System.Drawing.Point(332, 264);
            tickNone.Click += delegate { for (int i = 0; i < candidates.Count; i++) { meshList.SetItemChecked(i, false); } };
            Label faceNote = new Label();
            faceNote.Text = (this.faceSelection.Count > 0) ? "Selected faces are mapped on their meshes; the other ticked meshes whole." : "Ticked meshes are mapped whole.";
            faceNote.ForeColor = ForzaTheme.TextDim;
            faceNote.Size = new Size(380, 32);
            faceNote.Location = new System.Drawing.Point(0, 520);
            Label channelLabel = new Label();
            channelLabel.Text = "Channel to edit:";
            channelLabel.AutoSize = true;
            channelLabel.Location = new System.Drawing.Point(0, 6);
            ComboBox channel = new ComboBox();
            channel.DropDownStyle = ComboBoxStyle.DropDownList;
            channel.Items.AddRange(new object[] { "UV1", "UV2" });
            channel.SelectedIndex = (this.cmbMeshUV != null && this.cmbMeshUV.SelectedIndex == 1) ? 1 : 0;
            channel.Width = 80;
            channel.Location = new System.Drawing.Point(110, 2);
            Label startLabel = new Label();
            startLabel.Text = "New mapping created:";
            startLabel.AutoSize = true;
            startLabel.Location = new System.Drawing.Point(0, 38);
            RadioButton keep = new RadioButton();
            keep.Text = "Keep the current mapping of this channel";
            keep.AutoSize = true;
            keep.Checked = true;
            keep.Location = new System.Drawing.Point(12, 62);
            RadioButton other = new RadioButton();
            other.Text = "From the other channel";
            other.AutoSize = true;
            other.Location = new System.Drawing.Point(12, 88);
            RadioButton world = new RadioButton();
            world.Text = "From the world, seen from:";
            world.AutoSize = true;
            world.Location = new System.Drawing.Point(12, 114);
            ComboBox axis = new ComboBox();
            axis.DropDownStyle = ComboBoxStyle.DropDownList;
            axis.Items.AddRange(new object[] { "the top", "the front", "the side" });
            axis.SelectedIndex = 0;
            axis.Width = 90;
            axis.Location = new System.Drawing.Point(200, 111);
            RadioButton view = new RadioButton();
            view.Text = "From the viewport (the view as it is now)";
            view.AutoSize = true;
            view.Location = new System.Drawing.Point(12, 140);
            CheckBox split = new CheckBox();
            split.Text = "Faces that share points with others keep the others' mapping (the points are copied)";
            split.Checked = true;
            split.Size = new Size(370, 40);
            split.Location = new System.Drawing.Point(0, 176);
            Label note = new Label();
            note.Text = "Without the copy, moving a point also moves it for the faces that are not selected.";
            note.ForeColor = ForzaTheme.TextDim;
            note.Size = new Size(370, 36);
            note.Location = new System.Drawing.Point(0, 222);
            content.Controls.AddRange(new Control[] { channelLabel, channel, startLabel, keep, other, world, axis, view, split, note, meshLabel, material, tickAll, tickNone, meshList, faceNote });
            if (!this.ShowOptions("UV Mapper", content))
            {
                return;
            }
            // The ticked meshes: the faces selected on a mesh, else the whole mesh.
            List<int[]> chosen = new List<int[]>();
            List<List<int>> chosenFaces = new List<List<int>>();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!meshList.GetItemChecked(i))
                {
                    continue;
                }
                List<int> own = null;
                for (int m = 0; m < meshes.Count; m++)
                {
                    if (meshes[m][0] == candidates[i].SectionIndex && meshes[m][1] == candidates[i].SubIndex)
                    {
                        own = faces[m];
                    }
                }
                chosen.Add(new int[] { candidates[i].SectionIndex, candidates[i].SubIndex });
                chosenFaces.Add(own);
            }
            if (chosen.Count == 0)
            {
                MessageBox.Show(this, "Tick at least one mesh.", "UV Mapper", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            meshes = chosen;
            faces = chosenFaces;
            int set = channel.SelectedIndex;
            Matrix viewProjection = this.scene.Camera.View * this.scene.Camera.Projection;
            float width = this.pnlViewport.ClientSize.Width, height = this.pnlViewport.ClientSize.Height;

            UvSession session;
            try
            {
                byte[] data = this.zip.Read(this.currentEntry);
                Fm2CarbinModel fm2 = Fm2Model(data);
                if (fm2 != null)
                {
                    // FM2 (2026-11-21): its own model; each mesh found in it by the tree's part, material and LOD
                    List<int[]> located = new List<int[]>();
                    foreach (int[] m in meshes)
                    {
                        TreeNode node = FindMeshNode(this.tvModel.Nodes, m[0], m[1]);
                        if (node == null || node.Parent == null)
                        {
                            throw new InvalidOperationException("A mesh to map is not in the tree any more: nothing changed.");
                        }
                        int p, k;
                        LocateFm2Mesh(fm2, (ForzaMesh) node.Tag, node, out p, out k);
                        located.Add(new int[] { p, k });
                    }
                    session = UvSession.Create(fm2, meshes, located, faces, set, split.Checked);
                }
                else
                {
                    CarbinModel model = EditableModel(data);   // FM3 in its own layout (its vertex bytes stay but the UVs mapped)
                    if (model.IsMotorsport3)
                    {
                        foreach (int[] m in meshes)
                        {
                            TreeNode node = FindMeshNode(this.tvModel.Nodes, m[0], m[1]);
                            if (node != null && node.Parent != null)
                            {
                                CheckMeshInModel(model, (ForzaMesh) node.Tag, node);
                            }
                        }
                    }
                    session = UvSession.Create(model, meshes, faces, set, split.Checked);
                }
                if (other.Checked)
                {
                    session.MapFromOtherChannel();
                }
                else if (world.Checked)
                {
                    session.MapFromWorld(axis.SelectedIndex);
                }
                else if (view.Checked)
                {
                    session.MapFromView(viewProjection, width, height);
                }
                session.Commit();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "UV Mapper", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }

            // The viewport shows the working copy, with the channel being edited.
            this.uvMeshes.Clear();
            List<ForzaMesh> shown = GetMeshes(this.tvModel.Nodes, false);
            foreach (UvSession.Part part in session.Parts)
            {
                ForzaMesh target = null;
                foreach (ForzaMesh mesh in shown)
                {
                    if (mesh.SectionIndex == part.Section && mesh.SubIndex == part.Sub)
                    {
                        target = mesh;
                    }
                }
                this.uvMeshes.Add(target);
                if (target != null)
                {
                    try
                    {
                        UvSession.BuildPreview(part, target, set);
                    }
                    catch (Exception exception)
                    {
                        this.ReloadModelKeepingView();
                        this.uvMeshes.Clear();
                        MessageBox.Show(this, "The viewport could not show the mapping: " + exception.Message, "UV Mapper", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                        return;
                    }
                    target.Selected = false;
                    target.Highlight = false;
                }
            }
            this.scene.Update(true);

            // The textures the mapper can show: the one the viewport uses for the first mesh first, then the
            // car's own, read raw (with their alpha).
            List<UvTexture> choices = new List<UvTexture>();
            int initial = -1;
            string source;
            ForzaMesh first = this.uvMeshes.Count > 0 ? this.uvMeshes[0] : null;
            if (first != null && this.meshTextures.TryGetValue(first, out source) && source != null)
            {
                string viewportSource = source;
                initial = 0;
                choices.Add(new UvTexture("(as in the viewport)", delegate { return this.LoadRawTexture(viewportSource); }));
            }
            string folder = this.ModelFolder;
            foreach (ForzaZip.Entry entry in this.zip.Entries)
            {
                if (entry.Name.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
                    && (entry.Name.EndsWith(".xds", StringComparison.OrdinalIgnoreCase) || entry.Name.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)))
                {
                    string own = ArchivePrefix + entry.Name.Substring(folder.Length);
                    choices.Add(new UvTexture(entry.Name.Substring(folder.Length), delegate { return this.LoadRawTexture(own); }));
                }
            }            this.uvSession = session;
            this.uvEntry = this.currentEntry;
            this.uvDiscard = false;
            string title = (session.Parts.Count == 1) ? session.Parts[0].Label : session.Parts.Count + " meshes";
            this.uvMapper = new UvMapperForm(session, title, choices, initial, delegate { this.UvMapperChanged(); });
            this.uvMapper.Icon = this.Icon;
            this.uvMapper.TextureChanged = delegate(Bitmap picture) { this.UvTextureToViewport(picture); };
            this.uvMapper.Location = new System.Drawing.Point(this.Left + 60, this.Top + 90);
            this.uvMapper.FormClosed += delegate(object sender, FormClosedEventArgs e) { this.UvMapperClosed(((Form) sender).DialogResult == DialogResult.OK); };
            this.uvMapper.Show(this);
        }

        // A texture as the file holds it (alpha kept; the viewport's alpha-mask option does not apply).
        private Bitmap LoadRawTexture(string source)
        {
            if (source.StartsWith(ArchivePrefix))
            {
                ForzaZip.Entry entry = this.FindArchiveSource(source);
                byte[] data = this.zip.Read(entry);
                return entry.Name.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ? new TgaImage(data).ToBitmap() : new XdsTexture(data).GetLevelBitmap(0);
            }
            if (source.StartsWith(SharedPrefix))
            {
                ForzaZip shared = this.SharedZip;
                return new XdsTexture(shared.Read(shared.Find(source.Substring(SharedPrefix.Length)))).GetLevelBitmap(0);
            }
            if (source.EndsWith(".xds", StringComparison.OrdinalIgnoreCase))
            {
                return XdsTexture.FromFile(source).GetLevelBitmap(0);
            }
            if (source.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
            {
                return TgaImage.FromFile(source).ToBitmap();
            }
            using (Image file = Image.FromFile(source))
            {
                return new Bitmap(file);
            }
        }

        // The texture picked in the mapper is drawn on the edited meshes in the viewport (own copies: the
        // mapper disposes its pictures); the originals come back when the mapper closes (the model reloads).
        private readonly List<Bitmap> uvViewTextures = new List<Bitmap>();

        private void UvTextureToViewport(Bitmap shown)
        {
            if (this.scene == null)
            {
                return;
            }
            Bitmap copy = (shown == null) ? null : new Bitmap(shown);
            this.scene.ReleaseGpuTextures();
            foreach (ForzaMesh mesh in this.uvMeshes)
            {
                if (mesh != null)
                {
                    this.scene.SetMeshTexture(mesh, copy);
                    mesh.ForceTexture = copy != null;
                }
            }
            this.DisposeUvViewTextures();
            if (copy != null)
            {
                this.uvViewTextures.Add(copy);
            }
            this.scene.Update(true);
        }

        private void DisposeUvViewTextures()
        {
            foreach (Bitmap old in this.uvViewTextures)
            {
                old.Dispose();
            }
            this.uvViewTextures.Clear();
        }

        private void UvMapperChanged()
        {
            if (this.uvSession == null)
            {
                return;
            }
            for (int i = 0; i < this.uvSession.Parts.Count; i++)
            {
                if (this.uvMeshes[i] != null)
                {
                    this.uvSession.UpdatePreview(this.uvSession.Parts[i], this.uvMeshes[i]);
                }
            }
            this.scene.Update(true);
        }

        private void UvMapperClosed(bool apply)
        {
            UvSession session = this.uvSession;
            ForzaZip.Entry entry = this.uvEntry;
            bool discard = this.uvDiscard;
            this.uvMapper = null;
            this.uvSession = null;
            this.uvMeshes.Clear();
            if (this.scene != null)
            {
                this.scene.ClearTextures();
                this.scene.ReleaseGpuTextures();
            }
            this.DisposeUvViewTextures();
            if (discard)
            {
                return;
            }
            if (apply && session != null && entry == this.currentEntry)
            {
                try
                {
                    session.Commit();
                    this.zip.Replace(entry, session.ToBytes());
                    this.RefreshEntryNode(entry);
                    this.ReloadModelKeepingView();
                    this.SetStatus(string.Format("UV{0} of {1} mapped (pending: Save to write the archive)", session.Set + 1, session.Parts.Count == 1 ? session.Parts[0].Label : session.Parts.Count + " meshes"));
                    return;
                }
                catch (Exception exception)
                {
                    MessageBox.Show(this, exception.Message, "UV Mapper", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                }
            }
            this.ReloadModelKeepingView();
        }

        // The tree node of the mesh the viewer numbers (section, mesh); null when the tree does not show it.
        private static TreeNode FindMeshNode(TreeNodeCollection nodes, int section, int sub)
        {
            foreach (TreeNode node in nodes)
            {
                ForzaMesh mesh = node.Tag as ForzaMesh;
                if (mesh != null && mesh.SectionIndex == section && mesh.SubIndex == sub)
                {
                    return node;
                }
                TreeNode below = FindMeshNode(node.Nodes, section, sub);
                if (below != null)
                {
                    return below;
                }
            }
            return null;
        }

        // Another model was opened: the mapper's meshes are gone, so it closes without writing anything.
        private void CloseUvMapperForModelChange()
        {
            if (this.uvMapper != null)
            {
                this.uvDiscard = true;
                this.uvMapper.Close();
            }
        }
    }
}
