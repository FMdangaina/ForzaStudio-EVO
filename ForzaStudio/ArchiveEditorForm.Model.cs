namespace ForzaStudio
{
    using Microsoft.Xna.Framework;
    using Microsoft.Xna.Framework.Graphics;
    using Microsoft.Xna.Framework.Input;
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Windows.Forms;

    public partial class ArchiveEditorForm
    {
        private Panel pnlModel;
        private SplitContainer splitModel;
        private TreeView tvModel;
        private Panel pnlViewport;
        private ToolStripButton btnWireframe;
        private ToolStripButton btnTextures;
        private ToolStripComboBox cmbCameraSpeed;
        private TrackBar trkFov;
        private ToolStripControlHost fovHost;
        private bool syncingFov;
        private ToolStripLabel lblCameraFov;
        private ToolStripLabel lblModelInfo;
        private Timer modelTimer;
        private RenderScene scene;
        private System.Drawing.Point lastMouse;

        private void BuildModelView()
        {
            this.pnlModel = new Panel();
            this.pnlModel.Dock = DockStyle.Fill;

            SplitContainer modelSplit = this.splitModel = new SplitContainer();
            modelSplit.Dock = DockStyle.Fill;
            modelSplit.FixedPanel = FixedPanel.Panel1;
            modelSplit.SplitterDistance = 260;

            this.tvModel = new TreeView();
            this.tvModel.Dock = DockStyle.Fill;
            this.tvModel.CheckBoxes = true;
            this.tvModel.HideSelection = false;
            this.tvModel.AfterCheck += new TreeViewEventHandler(this.tvModel_AfterCheck);
            modelSplit.Panel1.Controls.Add(this.tvModel);
            this.AddStockOnlyButton(modelSplit.Panel1);

            ToolStrip toolbar = new ToolStrip();
            toolbar.GripStyle = ToolStripGripStyle.Hidden;
            this.btnWireframe = new ToolStripButton("Wireframe");
            this.btnWireframe.CheckOnClick = true;
            this.btnWireframe.CheckedChanged += delegate
            {
                if (this.scene != null)
                {
                    this.scene.ShowWireframe = this.btnWireframe.Checked;
                    this.scene.Update(true);
                }
            };
            // Textures on (the game's own, or the ones picked in the TEXTURE tab) or off (plain grey).
            // At start as chosen in Tools > Settings > VIEWER.
            bool texturesOn = AppSettings.Get("Viewer.Textures", "1") != "0";
            this.btnTextures = new ToolStripButton(texturesOn ? "Textures: on" : "Textures: off");
            this.btnTextures.CheckOnClick = true;
            this.btnTextures.Checked = texturesOn;
            this.btnTextures.CheckedChanged += delegate { this.TexturesSwitched(this.btnTextures.Checked); };
            this.cmbCameraSpeed = new ToolStripComboBox();
            this.cmbCameraSpeed.DropDownStyle = ComboBoxStyle.DropDownList;
            this.cmbCameraSpeed.Items.Add(new ComboBoxItem<float>(0.25f, "Camera: slow"));
            this.cmbCameraSpeed.Items.Add(new ComboBoxItem<float>(1f, "Camera: normal"));
            this.cmbCameraSpeed.Items.Add(new ComboBoxItem<float>(4f, "Camera: fast"));
            this.cmbCameraSpeed.SelectedIndex = 1;
            // The field of view (vertical, degrees) is set here only: the wheel dollies the camera. The game's
            // own values: photo mode 45, bumper 53, chase 54, cockpit 62, hood 66 (CameraSettings.ini).
            this.trkFov = new TrackBar();
            this.trkFov.Minimum = 10;
            this.trkFov.Maximum = 90;
            this.trkFov.Value = 45;
            this.trkFov.TickFrequency = 10;
            this.trkFov.AutoSize = false;
            this.trkFov.Size = new Size(150, 24);
            this.trkFov.ValueChanged += delegate
            {
                if (this.scene != null && !this.syncingFov)
                {
                    this.scene.Camera.FieldOfViewDegrees = this.trkFov.Value;
                    this.scene.Update(true);
                }
            };
            this.fovHost = new ToolStripControlHost(this.trkFov);
            this.fovHost.ToolTipText = "Field of view (perspective only): photo mode 45, bumper 53, chase 54, cockpit 62, hood 66. The wheel moves the camera, it does not change this.";
            this.lblCameraFov = new ToolStripLabel();
            this.lblCameraFov.ForeColor = SystemColors.GrayText;
            toolbar.Items.Add(this.btnWireframe);
            toolbar.Items.Add(this.btnTextures);
            toolbar.Items.Add(this.cmbCameraSpeed);
            toolbar.Items.Add(new ToolStripLabel("FOV"));
            toolbar.Items.Add(this.fovHost);
            toolbar.Items.Add(this.lblCameraFov);
            toolbar.Items.Add(this.BuildGoToCameraButton());
            this.lblModelInfo = new ToolStripLabel();
            this.lblModelInfo.Alignment = ToolStripItemAlignment.Right;
            toolbar.Items.Add(this.lblModelInfo);
            ToolStripLabel warning = this.BuildMaterialWarning();
            warning.Alignment = ToolStripItemAlignment.Right;
            toolbar.Items.Add(warning);

            this.pnlViewport = new Panel();
            this.pnlViewport.Dock = DockStyle.Fill;
            this.pnlViewport.BackColor = System.Drawing.Color.FromArgb(75, 75, 70);
            this.pnlViewport.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                this.pnlViewport.Focus();
                this.lastMouse = e.Location;
                this.clickDown = e.Location;
                if (e.Button == MouseButtons.Left && this.MeshEditActive)
                {
                    this.MeshEditMouseDown(e);
                }
            };
            this.pnlViewport.MouseUp += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && this.MeshEditActive)
                {
                    this.MeshEditMouseUp(e);
                }
                else if (e.Button == MouseButtons.Right && Math.Abs(e.X - this.clickDown.X) <= 3 && Math.Abs(e.Y - this.clickDown.Y) <= 3)
                {
                    this.ViewportRightClick(e.Location);
                }
                else if (e.Button == MouseButtons.Left && Math.Abs(e.X - this.clickDown.X) <= 3 && Math.Abs(e.Y - this.clickDown.Y) <= 3)
                {
                    this.SelectMeshAt(e.Location, (Control.ModifierKeys & System.Windows.Forms.Keys.Control) != 0, false);
                }
            };
            this.pnlViewport.MouseMove += new MouseEventHandler(this.pnlViewport_MouseMove);
            this.pnlViewport.MouseEnter += delegate { this.ViewportPointerEntered(); };
            this.pnlViewport.MouseWheel += delegate(object sender, MouseEventArgs e)
            {
                if (this.scene != null)
                {
                    if (this.scene.Camera.Orthographic)
                    {
                        this.scene.Camera.ApplyZoomForce((e.Delta < 0) ? 0.02f : -0.02f);
                    }
                    else
                    {
                        // Perspective: the wheel moves the camera toward / away from the model.
                        this.scene.Camera.Dolly(e.Delta > 0 ? 1 : -1, this.OrbitPivot());
                        this.scene.Update(true);
                    }
                }
            };
            this.pnlViewport.Resize += delegate
            {
                if (this.scene != null && this.pnlViewport.Width > 0 && this.pnlViewport.Height > 0)
                {
                    this.scene.ResetGraphicsDevice();
                }
            };
            this.pnlViewport.Paint += delegate
            {
                if (this.scene != null)
                {
                    this.scene.Update(true);
                }
            };

            Label help = new Label();
            help.Dock = DockStyle.Bottom;
            help.Height = 18;
            help.ForeColor = SystemColors.GrayText;
            help.Text = "  Middle drag: move the view   Alt + middle drag: turn the model   Wheel: zoom   W A S D / Q E: move   Arrows: look   1-6: front, back, right, left, top, bottom";

            modelSplit.Panel2.Controls.Add(this.pnlViewport);
            modelSplit.Panel2.Controls.Add(this.BuildMeshInfoPanel());
            modelSplit.Panel2.Controls.Add(help);
            modelSplit.Panel2.Controls.Add(this.BuildToolsStrip());
            modelSplit.Panel2.Controls.Add(toolbar);
            this.pnlModel.Controls.Add(modelSplit);

            this.modelTimer = new Timer();
            this.modelTimer.Interval = 15;
            this.modelTimer.Tick += new EventHandler(this.modelTimer_Tick);
        }

        private void ShowModel(string name, byte[] data)
        {
            this.ClearModel();
            this.tvModel.BeginUpdate();
            bool updating = true;
            try
            {
                this.ShowModelCore(name, data, ref updating);
            }
            finally
            {
                // A file that fails to parse must not leave the tree frozen for the next model.
                if (updating)
                {
                    this.tvModel.EndUpdate();
                }
            }
        }

        private void ShowModelCore(string name, byte[] data, ref bool updating)
        {
            string title = Path.GetFileName(name);
            TreeNode root = new TreeNode(title);
            if (name.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase))
            {
                using (ForzaCarBin bin = new ForzaCarBin(name, data))
                {
                    for (int s = 0; s < bin.Sections.Length; s++)
                    {
                        ForzaCarSection section = bin.Sections[s];
                        for (int k = 0; k < section.SubSections.Length; k++)
                        {
                            ForzaCarSubSection sub = section.SubSections[k];
                            TreeNode lod = GetChild(root.Nodes, "LOD" + sub.Lod);
                            TreeNode part = GetChild(lod.Nodes, section.Name);
                            TreeNode mesh = new TreeNode(sub.Name);
                            ForzaMesh forzaMesh = new ForzaMesh(section.Name + "_" + sub.Name, sub.Name, sub.Indices, sub.Vertices);
                            forzaMesh.SectionIndex = s;
                            forzaMesh.SubIndex = k;
                            forzaMesh.Technique = ShaderSettings.TechniqueName(sub.Name, name);
                            mesh.Tag = forzaMesh;
                            part.Nodes.Add(mesh);
                        }
                    }
                }
                this.AddEmptyParts(name, root);
            }
            else
            {
                using (ForzaTrackBin bin = new ForzaTrackBin(name, data))
                {
                    foreach (ForzaTrackSection section in bin.Sections)
                    {
                        TreeNode type = GetChild(root.Nodes, section.Type);
                        TreeNode part = GetChild(type.Nodes, section.Name);
                        foreach (ForzaTrackSubSection sub in section.SubSections)
                        {
                            TreeNode mesh = new TreeNode(sub.Name);
                            mesh.Tag = new ForzaMesh(title + "_" + section.Name + "_" + sub.Name, sub.Name, sub.Indices, sub.Vertices);
                            part.Nodes.Add(mesh);
                        }
                    }
                }
            }
            this.tvModel.Nodes.Add(root);
            this.LoadDamageInfo(data);
            this.MarkUnknownMaterials(data, root);
            this.ApplyShaderSettingsToMeshes();
            root.Expand();
            updating = false;
            this.tvModel.EndUpdate();

            // Show the most detailed LOD by default.
            TreeNode first = (root.Nodes.Count > 0) ? root.Nodes[0] : null;
            foreach (TreeNode node in root.Nodes)
            {
                if (node.Text == "LOD0")
                {
                    first = node;
                }
            }
            if (first != null)
            {
                first.Checked = true;
                first.Expand();
            }

            this.ShowView(this.pnlModel);
            if (this.scene == null)
            {
                // Only possible once the view is visible and laid out.
                this.splitModel.SplitterDistance = Math.Min(260, Math.Max(this.splitModel.Width / 3, 50));
                this.scene = new RenderScene(ref this.tvModel, ref this.pnlViewport);
                this.scene.ShowWireframe = this.btnWireframe.Checked;
                this.scene.Camera.FieldOfViewDegrees = this.trkFov.Value;
            }
            this.UpdateModelInfo();
            this.OnModelLoaded();
            this.LoadCockpitCamera();
            this.LoadCollision();
            this.LoadExhaust();
            this.LoadDriver();
            this.modelTimer.Start();
        }

        private static TreeNode GetChild(TreeNodeCollection nodes, string name)
        {
            TreeNode node = nodes[name];
            if (node == null)
            {
                node = nodes.Add(name, name);
            }
            return node;
        }

        private void ClearModel()
        {
            this.modelTimer.Stop();
            this.MeshEditReset();
            foreach (ForzaMesh mesh in GetMeshes(this.tvModel.Nodes, false))
            {
                mesh.Dispose();
            }
            this.tvModel.Nodes.Clear();
        }

        private static List<ForzaMesh> GetMeshes(TreeNodeCollection nodes, bool checkedOnly)
        {
            List<ForzaMesh> list = new List<ForzaMesh>();
            foreach (TreeNode node in nodes)
            {
                ForzaMesh mesh = node.Tag as ForzaMesh;
                if (mesh != null && (!checkedOnly || node.Checked))
                {
                    list.Add(mesh);
                }
                list.AddRange(GetMeshes(node.Nodes, checkedOnly));
            }
            return list;
        }

        private void tvModel_AfterCheck(object sender, TreeViewEventArgs e)
        {
            this.tvModel.AfterCheck -= new TreeViewEventHandler(this.tvModel_AfterCheck);
            SetChecked(e.Node.Nodes, e.Node.Checked);
            this.tvModel.AfterCheck += new TreeViewEventHandler(this.tvModel_AfterCheck);
            this.UpdateModelInfo();
            if (this.scene != null)
            {
                this.scene.Update(true);
            }
        }

        private static void SetChecked(TreeNodeCollection nodes, bool isChecked)
        {
            foreach (TreeNode node in nodes)
            {
                node.Checked = isChecked;
                SetChecked(node.Nodes, isChecked);
            }
        }

        private void UpdateModelInfo()
        {
            int vertices = 0;
            int faces = 0;
            foreach (ForzaMesh mesh in GetMeshes(this.tvModel.Nodes, true))
            {
                vertices += mesh.VertexCount;
                faces += mesh.FaceCount;
            }
            this.lblModelInfo.Text = string.Format("{0:N0} vertices, {1:N0} faces", vertices, faces);
            this.MeshEditShownChanged();
        }

        private void pnlViewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (this.scene != null && e.Button == MouseButtons.None && this.MeshEditActive)
            {
                this.MeshEditHover(e);
            }
            if (this.scene == null || e.Button == MouseButtons.None)
            {
                return;
            }
            float dx = e.X - this.lastMouse.X;
            float dy = e.Y - this.lastMouse.Y;
            this.lastMouse = e.Location;
            bool editing = this.MeshEditActive;
            if (editing && e.Button == MouseButtons.Left)
            {
                // Selecting faces: the drag draws a box (MeshEditMouseUp picks).
                this.MeshEditMouseMove(e);
                return;
            }
            // As in 3ds Max: the middle button moves the view (the point looked at stays on the model),
            // Alt + middle turns it around the model; the wheel zooms. Left and right do nothing here.
            if (e.Button == MouseButtons.Middle)
            {
                Vector3 pivot = this.OrbitPivot();
                if ((Control.ModifierKeys & System.Windows.Forms.Keys.Alt) != 0)
                {
                    this.scene.Camera.Orbit(dx * 0.01f, -dy * 0.01f, pivot);
                }
                else
                {
                    this.scene.Camera.PanPixels(dx, dy, Vector3.Distance(this.scene.Camera.EyePosition, pivot));
                }
                this.scene.Update(true);
            }
        }

        private void modelTimer_Tick(object sender, EventArgs e)
        {
            if (this.scene == null || !this.pnlModel.Visible)
            {
                return;
            }
            try
            {
                Camera camera = this.scene.Camera;
                camera.SpeedScale = ((ComboBoxItem<float>) this.cmbCameraSpeed.SelectedItem).Value;
                if (this.pnlViewport.Focused && Form.ActiveForm == this)
                {
                    KeyboardState keys = Keyboard.GetState();
                    if (keys.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.W)) camera.ApplyForce(camera.ForwardDirection);
                    if (keys.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.S)) camera.ApplyForce(-camera.ForwardDirection);
                    if (keys.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.A)) camera.ApplyForce(-camera.HorizontalDirection);
                    if (keys.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.D)) camera.ApplyForce(camera.HorizontalDirection);
                    if (keys.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Q)) camera.ApplyForce(new Vector3(0f, -1f, 0f));
                    if (keys.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.E)) camera.ApplyForce(new Vector3(0f, 1f, 0f));
                    if (keys.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Up)) camera.ApplyLookForce(new Vector2(0f, 0.1f));
                    if (keys.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Down)) camera.ApplyLookForce(new Vector2(0f, -0.1f));
                    if (keys.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Left)) camera.ApplyLookForce(new Vector2(-0.1f, 0f));
                    if (keys.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Right)) camera.ApplyLookForce(new Vector2(0.1f, 0f));
                }
                this.scene.Update(false);
                // The wheel zooms by changing the FOV; show where it is.
                this.SyncViewButtons();
                string fov = camera.Orthographic ? string.Format("{0:0.##} m high", camera.OrthoHeight) : string.Format("{0:0.#}\u00B0", camera.FieldOfViewDegrees);
                if (this.lblCameraFov.Text != fov)
                {
                    this.lblCameraFov.Text = fov;
                }
            }
            catch (DeviceLostException)
            {
                this.scene.ResetGraphicsDevice();
            }
        }

        private void ExportObj()
        {
            List<ForzaMesh> meshes = GetMeshes(this.tvModel.Nodes, true);
            if (meshes.Count == 0)
            {
                MessageBox.Show(this, "Check at least one part in the model tree.", "Export OBJ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "Wavefront OBJ (*.obj)|*.obj";
                dialog.FileName = Path.GetFileNameWithoutExtension(this.currentEntry.Name) + ".obj";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                CultureInfo inv = CultureInfo.InvariantCulture;
                string mtlName = Path.GetFileNameWithoutExtension(dialog.FileName) + ".mtl";
                Dictionary<string, bool> materials = new Dictionary<string, bool>();
                using (StreamWriter obj = new StreamWriter(dialog.FileName))
                {
                    obj.WriteLine("# Exported by ForzaStudio EVO from " + this.currentEntry.Name);
                    obj.WriteLine("mtllib " + mtlName);
                    int baseIndex = 1;
                    foreach (ForzaMesh mesh in meshes)
                    {
                        materials[mesh.MaterialName] = true;
                        obj.WriteLine("g " + mesh.Name);
                        foreach (ForzaVertex v in mesh.Vertices)
                        {
                            obj.WriteLine(string.Format(inv, "v {0} {1} {2}", v.position.X, v.position.Y, v.position.Z));
                        }
                        foreach (ForzaVertex v in mesh.Vertices)
                        {
                            obj.WriteLine(string.Format(inv, "vt {0} {1}", v.texture0.X, v.texture0.Y));
                        }
                        foreach (ForzaVertex v in mesh.Vertices)
                        {
                            obj.WriteLine(string.Format(inv, "vn {0} {1} {2}", v.normal.X, v.normal.Y, v.normal.Z));
                        }
                        obj.WriteLine("usemtl " + mesh.MaterialName);
                        for (int i = 0; i + 2 < mesh.Indices.Length; i += 3)
                        {
                            int a = mesh.Indices[i] + baseIndex;
                            int b = mesh.Indices[i + 1] + baseIndex;
                            int c = mesh.Indices[i + 2] + baseIndex;
                            obj.WriteLine("f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}", a, b, c);
                        }
                        baseIndex += mesh.Vertices.Length;
                    }
                }
                using (StreamWriter mtl = new StreamWriter(Path.Combine(Path.GetDirectoryName(dialog.FileName), mtlName)))
                {
                    foreach (string material in materials.Keys)
                    {
                        Vector3 color = Utilities.GetRandomColor(material.GetHashCode()).ToVector3();
                        mtl.WriteLine("newmtl " + material);
                        mtl.WriteLine(string.Format(inv, "Kd {0} {1} {2}", color.X, color.Y, color.Z));
                    }
                }
                this.SetStatus(string.Format("Exported {0} parts to {1}", meshes.Count, dialog.FileName));
            }
        }
    }
}
