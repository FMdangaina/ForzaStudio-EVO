namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.Windows.Forms;

    // Gives every mesh in the model tree the values its car's shadersettings.xml has for the mesh's technique,
    // so the viewport draws glass tints, lamp colours, paint scale, specular... as the game does
    // (ShaderLook.Apply). Run when a model is shown and after every Shader Settings edit.
    public partial class ArchiveEditorForm
    {
        private string pendingShaderTechnique;   // the technique Shader Settings selects when it opens

        // Model tree > Shader Settings of "material": opens the car's shadersettings.xml on that material's technique.
        private void OpenShaderSettingsOfSelection()
        {
            ForzaMesh mesh = (this.tvModel.SelectedNode != null) ? this.tvModel.SelectedNode.Tag as ForzaMesh : null;
            ForzaZip.Entry entry = (this.zip == null) ? null : this.zip.Find(this.ModelFolder + "shadersettings.xml");
            if (mesh == null || entry == null)
            {
                MessageBox.Show(this, (entry == null) ? "This car has no shadersettings.xml: every material runs with its shader's defaults." : "Select a material first.",
                    "Shader Settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            this.pendingShaderTechnique = mesh.Technique ?? mesh.MaterialName;
            this.SelectEntry(entry.Name);
        }

        private readonly Dictionary<string, string> sphereFiles = new Dictionary<string, string>(StringComparer.Ordinal);   // technique -> texture file picked for the preview sphere

        // The sphere's texture: the file picked for the technique, else (when its look draws a texture) the one the
        // viewport draws on a mesh of that technique.
        private void UpdateSphereTexture(string technique, ShaderLookValues look)
        {
            Bitmap image = null;
            string file;
            if (this.sphereFiles.TryGetValue(technique, out file))
            {
                image = this.LoadPreview(file, true);
            }
            else if (look.UseTexture || look.Kind == ShaderKind.Glass)
            {
                ForzaMesh mesh = FindMeshOfTechnique(this.tvModel.Nodes, technique);
                string source;
                if (mesh != null && this.meshTextures.TryGetValue(mesh, out source))
                {
                    image = this.LoadPreview(source, true);
                }
            }
            this.sphereShader.SetTexture(image);
        }

        private static ForzaMesh FindMeshOfTechnique(TreeNodeCollection nodes, string technique)
        {
            foreach (TreeNode node in nodes)
            {
                ForzaMesh mesh = node.Tag as ForzaMesh;
                if (mesh != null && (mesh.Technique ?? mesh.MaterialName) == technique)
                {
                    return mesh;
                }
                ForzaMesh found = FindMeshOfTechnique(node.Nodes, technique);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private void PickSphereTexture()
        {
            string technique = this.SelectedTechnique;
            if (technique == null)
            {
                return;
            }
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Texture for the preview sphere";
                dialog.Filter = "Images (*.xds;*.tga;*.png;*.jpg;*.bmp)|*.xds;*.tga;*.png;*.jpg;*.bmp|All files (*.*)|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                this.sphereFiles[technique] = dialog.FileName;
            }
            this.FillShaderParameters();
        }

        private void AutoSphereTexture()
        {
            string technique = this.SelectedTechnique;
            if (technique != null && this.sphereFiles.Remove(technique))
            {
                this.FillShaderParameters();
            }
        }

        // The sphere in a window of its own, big enough to read the texture.
        private void ShowBigSphere()
        {
            if (!this.sphereShader.HasLook)
            {
                return;
            }
            using (Form form = new Form())
            {
                form.Text = "Shader preview - " + this.SelectedTechnique;
                form.StartPosition = FormStartPosition.CenterParent;
                form.ClientSize = new Size(640, 640);
                form.BackColor = ForzaTheme.Surface;
                ShaderViewport big = new ShaderViewport();
                big.Dock = DockStyle.Fill;
                big.SetLook(this.sphereShader.Look);
                big.SetTexture(this.sphereShader.Texture);

                // Shape and (optionally) one mesh of the model: the mesh is drawn with this technique's look, with its own texture when it has one.
                ComboBox shapes = new ComboBox();
                shapes.DropDownStyle = ComboBoxStyle.DropDownList;
                shapes.Items.AddRange(new object[] { "Sphere", "Cube", "Plane", "Cylinder" });
                shapes.SelectedIndex = 0;
                shapes.Width = 110;
                ComboBox meshes = new ComboBox();
                meshes.DropDownStyle = ComboBoxStyle.DropDownList;
                meshes.Width = 360;
                meshes.DropDownWidth = 520;
                List<ForzaMesh> list = new List<ForzaMesh>();
                meshes.Items.Add("(shape)");
                CollectMeshes(this.tvModel.Nodes, list, meshes);
                meshes.SelectedIndex = 0;
                shapes.SelectedIndexChanged += delegate
                {
                    meshes.SelectedIndex = 0;
                    big.SetShape((PreviewShape) shapes.SelectedIndex);
                };
                meshes.SelectedIndexChanged += delegate
                {
                    if (meshes.SelectedIndex <= 0)
                    {
                        big.SetMesh(null);
                        big.SetTexture(this.sphereShader.Texture);
                        return;
                    }
                    ForzaMesh mesh = list[meshes.SelectedIndex - 1];
                    string source;
                    Bitmap own = this.meshTextures.TryGetValue(mesh, out source) ? this.LoadPreview(source, true) : null;
                    big.SetTexture(own ?? this.sphereShader.Texture);
                    big.SetMesh(mesh);
                };
                FlowLayoutPanel bar = new FlowLayoutPanel();
                bar.Dock = DockStyle.Top;
                bar.AutoSize = true;
                bar.Controls.AddRange(new Control[] { shapes, meshes });
                form.Controls.Add(big);
                form.Controls.Add(bar);
                form.ShowDialog(this);
            }
        }

        // Every mesh of the model tree, in tree order: "node > material" in the combo, the mesh in the list (same index + 1).
        private static void CollectMeshes(TreeNodeCollection nodes, List<ForzaMesh> list, ComboBox combo)
        {
            foreach (TreeNode node in nodes)
            {
                ForzaMesh mesh = node.Tag as ForzaMesh;
                if (mesh != null && mesh.VertexCount > 0)
                {
                    list.Add(mesh);
                    combo.Items.Add(node.Text + "  [" + (mesh.Technique ?? mesh.MaterialName) + "]");
                }
                CollectMeshes(node.Nodes, list, combo);
            }
        }

        private void ApplyShaderSettingsToMeshes()
        {
            ShaderSettings settings = null;
            ForzaZip.Entry entry = (this.zip == null) ? null : this.zip.Find(this.ModelFolder + "shadersettings.xml");
            if (entry != null)
            {
                try
                {
                    settings = ShaderSettings.Parse(this.zip.Read(entry));
                }
                catch (Exception)
                {
                    settings = null;
                }
            }
            this.ApplyShaderSettingsToMeshes(this.tvModel.Nodes, settings);
        }

        private void ApplyShaderSettingsToMeshes(TreeNodeCollection nodes, ShaderSettings settings)
        {
            foreach (TreeNode node in nodes)
            {
                ForzaMesh mesh = node.Tag as ForzaMesh;
                if (mesh != null)
                {
                    mesh.Settings = ShaderValues(settings, mesh.Technique ?? mesh.MaterialName);
                }
                this.ApplyShaderSettingsToMeshes(node.Nodes, settings);
            }
        }

        // parameter -> its numbers (r g b a, or the one value) for a technique; null when it has no entry.
        private static Dictionary<string, float[]> ShaderValues(ShaderSettings settings, string technique)
        {
            ShaderSettings.Entry entry = (settings == null || technique == null) ? null : settings.Find(technique);
            if (entry == null)
            {
                return null;
            }
            Dictionary<string, float[]> values = new Dictionary<string, float[]>(StringComparer.Ordinal);
            foreach (ShaderSettings.Parameter p in entry.Parameters)
            {
                List<float> numbers = new List<float>();
                foreach (string[] a in ShaderSettings.Attributes(p.Xml))
                {
                    float number;
                    if (float.TryParse(a[1], NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                    {
                        numbers.Add(number);
                    }
                }
                if (numbers.Count > 0)
                {
                    values[p.Name] = numbers.ToArray();
                }
            }
            return values;
        }
    }
}
