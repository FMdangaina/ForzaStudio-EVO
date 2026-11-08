namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Imaging;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Windows.Forms;

    // Mesh information and preview texture assignment. The .carbin only names the material
    // (shader technique) of each part; the textures it samples come from the game's shaders and
    // Media\cars\Shared.zip\textures.xml (ShaderTextures): each material gets its colour texture by
    // itself ("(auto)"), from the car's files or Shared.zip. A texture picked by hand wins, by default
    // for every part sharing the material, for this session only. The toolbar's Textures button
    // switches them all off (plain grey) and on.
    public partial class ArchiveEditorForm
    {
        private const string ArchivePrefix = "zip:";
        private const string SharedPrefix = "shared:";        // an entry of the game's Media\cars\Shared.zip
        private const string AutoItem = "(auto)";
        private Dictionary<ForzaMesh, string> autoTextureSampler = new Dictionary<ForzaMesh, string>();

        // The toolbar's Textures button.
        private void TexturesSwitched(bool on)
        {
            this.btnTextures.Text = on ? "Textures: on" : "Textures: off";
            this.ApplyTextures();
        }
        private static readonly System.Drawing.Color BaseColor = System.Drawing.Color.FromArgb(150, 150, 150);

        private Label lblMeshInfo;
        private ComboBox cmbMeshTexture;
        private ComboBox cmbMeshUV;
        private CheckBox chkSameMaterial;
        private CheckBox chkAlphaMask;
        private bool updatingMeshPanel;

        // Material name -> texture source ("zip:<entry>" or a file path) and UV set, kept across models.
        private Dictionary<string, string> materialTextures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, bool> materialSecondUV = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<ForzaMesh, string> meshTextures = new Dictionary<ForzaMesh, string>();
        private Dictionary<string, Bitmap> previewCache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        // Model view side panel: game-style tabs PART / TEXTURE / CAMERA / COLLISION, each a column of
        // controls SideWidth wide. CAMERA and COLLISION are shown only for car models (SetCameraTabVisible,
        // SetCollisionTabVisible); their lines are drawn in the view only while their tab is open.
        private const int SideWidth = 236;
        private TabControl tabSide;
        private TabPage tabSideCamera;
        private TabPage tabSideCollision;
        private TabPage tabSideExhaust;
        private TabPage tabSideDriver;
        private bool cameraTabWanted = true;
        private bool collisionTabWanted = true;
        private bool exhaustTabWanted = false;
        private bool driverTabWanted = false;

        private Panel BuildMeshInfoPanel()
        {
            Panel host = new Panel();
            host.Dock = DockStyle.Right;
            host.Width = 296;
            this.tabSide = new TabControl();
            FlowLayoutPanel part = this.AddSideTab("Part");
            FlowLayoutPanel textures = this.AddSideTab("Texture");
            // SHADER next to TEXTURE (ArchiveEditorForm.ShaderTab.cs). Added in place: TabPages.Insert does
            // nothing before the control has its handle, so the pages are only ever added in order.
            FlowLayoutPanel shader = this.AddSideTab("Shader");
            FlowLayoutPanel camera = this.AddSideTab("Camera");
            this.tabSideCamera = (TabPage) camera.Parent;
            FlowLayoutPanel collision = this.AddSideTab("Collision");
            this.tabSideCollision = (TabPage) collision.Parent;
            FlowLayoutPanel exhaust = this.AddSideTab("Exhaust");
            this.tabSideExhaust = (TabPage) exhaust.Parent;
            FlowLayoutPanel driver = this.AddSideTab("Driver");
            this.tabSideDriver = (TabPage) driver.Parent;
            FlowLayoutPanel meshEdit = this.AddSideTab("Mesh");
            this.tabSideMesh = (TabPage) meshEdit.Parent;
            FlowLayoutPanel gauges = this.AddSideTab("Gauges");
            this.tabSideGauges = (TabPage) gauges.Parent;
            this.tabSide.SelectedIndexChanged += delegate { this.MeshWireframeFollowTab(); this.UpdateViewOverlay(); };
            host.Controls.Add(SideRailHost(this.tabSide));

            this.lblMeshInfo = SideNote("Select a part in the tree.");
            this.lblMeshInfo.ForeColor = this.ForeColor;

            this.cmbMeshTexture = new ComboBox();
            this.cmbMeshTexture.DropDownStyle = ComboBoxStyle.DropDownList;
            this.cmbMeshTexture.Width = SideWidth;
            this.cmbMeshTexture.SelectedIndexChanged += delegate
            {
                if (!this.updatingMeshPanel && this.cmbMeshTexture.SelectedIndex >= 0)
                {
                    string item = (string) this.cmbMeshTexture.SelectedItem;
                    this.AssignTexture(item == AutoItem ? null : item.StartsWith(SharedPrefix) ? item : ArchivePrefix + item);
                }
            };

            Button browse = SideWideButton("Texture From File...", delegate { this.AssignTextureFromFile(); });

            this.cmbMeshUV = new ComboBox();
            this.cmbMeshUV.DropDownStyle = ComboBoxStyle.DropDownList;
            this.cmbMeshUV.Width = SideWidth;
            this.cmbMeshUV.Items.AddRange(new object[] { "UV1", "UV2" });
            this.cmbMeshUV.SelectedIndex = 0;
            this.cmbMeshUV.SelectedIndexChanged += delegate
            {
                if (!this.updatingMeshPanel)
                {
                    this.SetSecondUV(this.cmbMeshUV.SelectedIndex == 1);
                }
            };

            this.chkSameMaterial = new CheckBox();
            this.chkSameMaterial.Text = "Apply to all parts with this material";
            this.chkSameMaterial.Checked = true;
            this.chkSameMaterial.Size = new Size(SideWidth, 22);

            this.chkAlphaMask = new CheckBox();
            this.chkAlphaMask.Text = "Alpha as decal mask over base color";
            this.chkAlphaMask.Checked = false;
            this.chkAlphaMask.Size = new Size(SideWidth, 22);
            this.chkAlphaMask.CheckedChanged += delegate
            {
                this.ClearPreviewCache();
                this.ApplyTextures();
            };

            part.Controls.Add(SideHeading("Selected", true));
            part.Controls.Add(this.lblMeshInfo);
            this.AddPartEditControls(part);

            textures.Controls.Add(SideHeading("Preview texture", true));
            textures.Controls.Add(this.cmbMeshTexture);
            textures.Controls.Add(browse);
            textures.Controls.Add(SideCaption("UV set"));
            textures.Controls.Add(this.cmbMeshUV);
            textures.Controls.Add(this.chkSameMaterial);
            textures.Controls.Add(this.chkAlphaMask);
            this.AddUvViewControls(textures);
            textures.Controls.Add(SideNote("(auto): the texture the game draws the material with (its shader's samplers and Shared.zip\\textures.xml), from the car's files or Shared.zip. Pick another to override it; choices are remembered per material and only change the preview."));

            // Kept in the tabs until a model loads, so the theme reaches it (LoadCockpitCamera hides it).
            this.AddCameraControls(camera);
            this.AddCollisionControls(collision);
            this.AddExhaustControls(exhaust);
            this.AddDriverControls(driver);
            this.AddMeshEditControls(meshEdit);
            this.AddGaugeControls(gauges);
            this.AddShaderTabControls(shader);

            this.tvModel.AfterSelect += delegate { this.TreeSelected(); };
            return host;
        }

        // The side tabs with their labels on a vertical rail at the right edge: a horizontal strip ran
        // out of room at four tabs (COLLISION cut, user 2026-10-20). The TabControl is a little larger
        // than its area so its own light border is cut off, as in TabHost.
        private static Control SideRailHost(TabControl tabs)
        {
            Panel host = new Panel();
            host.Dock = DockStyle.Fill;
            ForzaTabStrip rail = new ForzaTabStrip(tabs, true);
            Panel pages = new Panel();
            pages.Dock = DockStyle.Fill;
            tabs.Dock = DockStyle.None;
            pages.Controls.Add(tabs);
            pages.Resize += delegate { tabs.Bounds = new Rectangle(-4, -4, pages.Width + 8, pages.Height + 8); };
            host.Controls.Add(pages);
            host.Controls.Add(rail);
            return host;
        }

        private FlowLayoutPanel AddSideTab(string title)
        {
            FlowLayoutPanel column = new FlowLayoutPanel();
            column.FlowDirection = FlowDirection.TopDown;
            column.WrapContents = false;
            column.AutoScroll = true;
            column.Padding = new Padding(10, 4, 0, 10);
            this.AddTab(this.tabSide, title, column);
            return column;
        }

        private void SetCameraTabVisible(bool visible)
        {
            this.cameraTabWanted = visible;
            this.ArrangeCarTabs();
        }

        private void SetCollisionTabVisible(bool visible)
        {
            this.collisionTabWanted = visible;
            this.ArrangeCarTabs();
        }

        private void SetExhaustTabVisible(bool visible)
        {
            this.exhaustTabWanted = visible;
            this.ArrangeCarTabs();
        }

        private void SetDriverTabVisible(bool visible)
        {
            this.driverTabWanted = visible;
            this.ArrangeCarTabs();
        }

        // CAMERA, COLLISION, EXHAUST, DRIVER in that order whichever comes and goes; the open tab stays open when still there.
        private void ArrangeCarTabs()
        {
            bool cameraShown = this.tabSide.TabPages.Contains(this.tabSideCamera);
            bool collisionShown = this.tabSide.TabPages.Contains(this.tabSideCollision);
            bool exhaustShown = this.tabSide.TabPages.Contains(this.tabSideExhaust);
            bool driverShown = this.tabSide.TabPages.Contains(this.tabSideDriver);
            bool gaugesShown = this.tabSide.TabPages.Contains(this.tabSideGauges);
            if (cameraShown == this.cameraTabWanted && collisionShown == this.collisionTabWanted && exhaustShown == this.exhaustTabWanted && driverShown == this.driverTabWanted
                && gaugesShown == this.gaugesTabWanted)
            {
                return;
            }
            TabPage selected = this.tabSide.SelectedTab;
            this.tabSide.TabPages.Remove(this.tabSideCamera);
            this.tabSide.TabPages.Remove(this.tabSideCollision);
            this.tabSide.TabPages.Remove(this.tabSideExhaust);
            this.tabSide.TabPages.Remove(this.tabSideDriver);
            this.tabSide.TabPages.Remove(this.tabSideGauges);
            if (this.cameraTabWanted)
            {
                this.tabSide.TabPages.Add(this.tabSideCamera);
            }
            if (this.collisionTabWanted)
            {
                this.tabSide.TabPages.Add(this.tabSideCollision);
            }
            if (this.exhaustTabWanted)
            {
                this.tabSide.TabPages.Add(this.tabSideExhaust);
            }
            if (this.driverTabWanted)
            {
                this.tabSide.TabPages.Add(this.tabSideDriver);
            }
            if (this.gaugesTabWanted)
            {
                this.tabSide.TabPages.Add(this.tabSideGauges);
            }
            if (selected != null && this.tabSide.TabPages.Contains(selected))
            {
                this.tabSide.SelectedTab = selected;
            }
            this.tabSide.Parent.Invalidate(true);
        }

        // The view's line overlay belongs to the open side tab: the cockpit camera on CAMERA, the
        // collision spheres on COLLISION, nothing on the others.
        private void UpdateViewOverlay()
        {
            if (this.scene == null)
            {
                return;
            }
            TabPage open = this.tabSide.SelectedTab;
            List<Microsoft.Xna.Framework.Graphics.VertexPositionColor> lines = (open == this.tabSideCamera) ? this.CameraOverlayLines()
                : (open == this.tabSideCollision) ? this.CollisionOverlayLines()
                : (open == this.tabSideExhaust) ? this.ExhaustOverlayLines()
                : (open == this.tabSideDriver) ? this.DriverOverlayLines()
                : (open == this.tabSideGauges) ? this.GaugeOverlayLines()
                : (open == this.tabSideMesh) ? this.MeshEditOverlayLines() : null;
            this.UpdateGaugePreview(open == this.tabSideGauges);
            this.scene.Overlay = (lines != null && lines.Count >= 2) ? lines.ToArray() : null;
            this.scene.OverlayFaces = (open == this.tabSideMesh) ? this.MeshEditFaceFill() : null;
            this.scene.Update(true);
        }

        // Side panel pieces: a red section heading, a small caption over a control, a grey note that
        // grows with its text, a full-width button and a row of equal buttons.
        private static Label SideHeading(string text, bool first)
        {
            Label label = new Label();
            label.Text = text.ToUpperInvariant();
            label.AutoSize = false;
            label.Size = new Size(SideWidth, 22);
            label.TextAlign = ContentAlignment.BottomLeft;
            label.Font = new Font(ForzaTheme.Regular, FontStyle.Bold);
            label.ForeColor = ForzaTheme.Red;
            label.Margin = new Padding(0, first ? 4 : 16, 0, 4);
            return label;
        }

        private static Label SideCaption(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.Size = new Size(SideWidth, 20);
            label.TextAlign = ContentAlignment.BottomLeft;
            label.Margin = new Padding(0, 4, 0, 1);
            return label;
        }

        private static Label SideNote(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.MaximumSize = new Size(SideWidth, 0);
            label.ForeColor = SystemColors.GrayText;
            label.Margin = new Padding(0, 4, 0, 4);
            return label;
        }

        private static Button SideWideButton(string text, EventHandler click)
        {
            Button button = new Button();
            button.Text = text;
            button.Size = new Size(SideWidth, 28);
            button.Margin = new Padding(0, 3, 0, 3);
            button.Click += click;
            return button;
        }

        private static FlowLayoutPanel SideButtonRow(params Button[] buttons)
        {
            FlowLayoutPanel row = new FlowLayoutPanel();
            row.Size = new Size(SideWidth + 4, 34);
            row.Margin = new Padding(0);
            int width = (SideWidth - 4 * (buttons.Length - 1)) / buttons.Length;
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].Size = new Size(width, 28);
                buttons[i].Margin = new Padding(0, 3, i < buttons.Length - 1 ? 4 : 0, 3);
                row.Controls.Add(buttons[i]);
            }
            return row;
        }

        private List<ForzaMesh> SelectedMeshes
        {
            get
            {
                List<ForzaMesh> meshes = new List<ForzaMesh>();
                if (this.meshSelection.Count > 0)
                {
                    meshes.AddRange(this.meshSelection);
                    return meshes;
                }
                TreeNode node = this.tvModel.SelectedNode;
                if (node != null)
                {
                    ForzaMesh mesh = node.Tag as ForzaMesh;
                    if (mesh != null)
                    {
                        meshes.Add(mesh);
                    }
                    else
                    {
                        meshes.AddRange(GetMeshes(node.Nodes, false));
                    }
                }
                return meshes;
            }
        }

        // Called when a model has been loaded: refresh the texture list and reapply remembered materials.
        private void OnModelLoaded()
        {
            this.updatingMeshPanel = true;
            this.cmbMeshTexture.Items.Clear();
            this.cmbMeshTexture.Items.Add(AutoItem);
            // Textures next to the model and below it: the whole zip, or the car's folder in folder mode.
            string folder = this.ModelFolder;
            foreach (ForzaZip.Entry entry in this.zip.Entries)
            {
                if (!entry.Name.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (entry.Name.EndsWith(".xds", StringComparison.OrdinalIgnoreCase) || entry.Name.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
                {
                    this.cmbMeshTexture.Items.Add(entry.Name.Substring(folder.Length));
                }
            }
            // Then the common textures of the game (Media\cars\Shared.zip), listed as "shared:name".
            ForzaZip shared = this.SharedZip;
            if (shared != null)
            {
                List<string> common = new List<string>();
                foreach (ForzaZip.Entry entry in shared.Entries)
                {
                    if (entry.Name.EndsWith(".xds", StringComparison.OrdinalIgnoreCase))
                    {
                        common.Add(SharedPrefix + entry.Name);
                    }
                }
                common.Sort(StringComparer.OrdinalIgnoreCase);
                this.cmbMeshTexture.Items.AddRange(common.ToArray());
            }
            this.updatingMeshPanel = false;

            this.meshTextures.Clear();
            this.autoTextureSampler.Clear();
            Dictionary<ForzaMesh, int> lods = this.MeshLods();
            int auto = 0, missing = 0;
            foreach (ForzaMesh mesh in GetMeshes(this.tvModel.Nodes, false))
            {
                string source;
                if (this.materialTextures.TryGetValue(mesh.MaterialName, out source))
                {
                    if (source.Length > 0 && this.SourceExists(source))
                    {
                        this.meshTextures[mesh] = source;
                    }
                }
                else if (this.SetAutoTexture(mesh, lods))
                {
                    auto++;
                }
                else
                {
                    missing++;
                }
                bool second;
                mesh.UseSecondUV = this.materialSecondUV.TryGetValue(mesh.MaterialName, out second) && second;
            }
            this.ApplyTextures();
            this.UpdateMeshPanel();
            ShaderTextures table = ShaderTextures.For(this.GameRoot);
            if (table.Problem != null)
            {
                this.SetStatus("Automatic textures not available: " + table.Problem);
            }
            else if (auto + missing > 0)
            {
                this.SetStatus(string.Format("Automatic textures: {0} parts textured, {1} without a texture the game would use", auto, missing));
            }
        }

        // The LOD of every mesh, from the tree (LODn > part > mesh).
        private Dictionary<ForzaMesh, int> MeshLods()
        {
            Dictionary<ForzaMesh, int> lods = new Dictionary<ForzaMesh, int>();
            foreach (TreeNode root in this.tvModel.Nodes)
            {
                foreach (TreeNode lod in root.Nodes)
                {
                    int level;
                    if (!lod.Text.StartsWith("LOD") || !int.TryParse(lod.Text.Substring(3), out level))
                    {
                        continue;
                    }
                    foreach (ForzaMesh mesh in GetMeshes(lod.Nodes, false))
                    {
                        lods[mesh] = level;
                    }
                }
            }
            return lods;
        }

        // The model file's kind for textures.xml: the cockpit file, a LOD 0 (the _lod0 file, a wheel's
        // LOD 0), or LODs 1-5 of the main file.
        private ShaderTextures.Kind TextureKind(ForzaMesh mesh, Dictionary<ForzaMesh, int> lods)
        {
            string name = (this.currentEntry != null) ? this.currentEntry.Name.ToLowerInvariant() : "";
            if (name.EndsWith("_cockpit.carbin"))
            {
                return ShaderTextures.Kind.Cockpit;
            }
            int lod;
            return (name.EndsWith("_lod0.carbin") || (lods.TryGetValue(mesh, out lod) && lod == 0)) ? ShaderTextures.Kind.Lod0 : ShaderTextures.Kind.Main;
        }

        // The texture the game draws the mesh's material with, when the car or Shared.zip has it.
        private bool SetAutoTexture(ForzaMesh mesh, Dictionary<ForzaMesh, int> lods)
        {
            ShaderTextures table = ShaderTextures.For(this.GameRoot);
            if (table.Problem != null)
            {
                return false;
            }
            foreach (ShaderTextures.Texture texture in table.ColourTextures(mesh.MaterialName, this.TextureKind(mesh, lods)))
            {
                string source = (texture.Where == ShaderTextures.Place.Shared) ? SharedPrefix + texture.File
                    : (texture.Where == ShaderTextures.Place.Car) ? ArchivePrefix + texture.File : null;
                // A shared texture this game lacks, carried by a ported car in its own zip (CarPort).
                if (texture.Where == ShaderTextures.Place.Shared && !this.SourceExists(source))
                {
                    source = ArchivePrefix + texture.File;
                }
                if (source != null && this.SourceExists(source) && this.LoadPreview(source, true) != null)
                {
                    this.meshTextures[mesh] = source;
                    this.autoTextureSampler[mesh] = texture.Sampler;
                    return true;
                }
            }
            return false;
        }

        private void UpdateMeshPanel()
        {
            List<ForzaMesh> meshes = this.SelectedMeshes;
            this.updatingMeshPanel = true;
            try
            {
                if (meshes.Count == 0)
                {
                    this.lblMeshInfo.Text = "Select a part in the tree.";
                    this.cmbMeshTexture.SelectedIndex = -1;
                    return;
                }
                Dictionary<string, int> materials = new Dictionary<string, int>();
                int vertices = 0;
                int faces = 0;
                foreach (ForzaMesh mesh in meshes)
                {
                    vertices += mesh.VertexCount;
                    faces += mesh.FaceCount;
                    int count;
                    materials.TryGetValue(mesh.MaterialName, out count);
                    materials[mesh.MaterialName] = count + 1;
                }
                ForzaMesh first = meshes[0];
                string texture;
                this.meshTextures.TryGetValue(first, out texture);
                TreeNode node = this.tvModel.SelectedNode;
                if (node.Tag is ForzaMesh)
                {
                    string part = (node.Parent != null) ? node.Parent.Text : "";
                    string lod = (node.Parent != null && node.Parent.Parent != null) ? node.Parent.Parent.Text : "";
                    string sampler;
                    string shown = DisplaySource(texture) + (this.autoTextureSampler.TryGetValue(first, out sampler) ? " (auto, " + sampler + ")" : "");
                    this.lblMeshInfo.Text = string.Format("Part: {0}\nLOD: {1}\nMaterial: {2}\nDamage: {7}\n\nVertices: {3:N0}\nFaces: {4:N0}\nParts with this material: {5}\n\nTexture: {6}{8}",
                        part, lod, first.MaterialName + (this.IsUnknownMaterial(first.MaterialName) ? " (no FM4 car uses it: may render black)" : ""), vertices, faces, this.CountMaterial(first.MaterialName), shown, this.DescribeDamage(part),
                        this.DescribeShader(first));
                }
                else
                {
                    List<string> names = new List<string>(materials.Keys);
                    names.Sort();
                    int total = names.Count;
                    if (names.Count > 12)
                    {
                        int more = names.Count - 12;
                        names.RemoveRange(12, more);
                        names.Add(string.Format("and {0} more", more));
                    }
                    this.lblMeshInfo.Text = string.Format("{0}\n{1} parts, {2:N0} vertices, {3:N0} faces\n{6}\n\nMaterials ({4}):\n{5}",
                        this.tvModel.SelectedNode.Text, meshes.Count, vertices, faces, total, string.Join(", ", names.ToArray()), this.DescribeDamage(node));
                }
                string entry = (texture != null && texture.StartsWith(ArchivePrefix)) ? texture.Substring(ArchivePrefix.Length)
                    : (texture != null && texture.StartsWith(SharedPrefix)) ? texture : null;
                int index = (this.autoTextureSampler.ContainsKey(first) || texture == null) ? 0
                    : ((entry != null) ? this.cmbMeshTexture.Items.IndexOf(entry) : -1);
                this.cmbMeshTexture.SelectedIndex = index;
                this.cmbMeshUV.SelectedIndex = first.UseSecondUV ? 1 : 0;
            }
            finally
            {
                this.updatingMeshPanel = false;
                this.RefreshUvView();
            }
        }

        // Section name -> has damage deformation data, for the model being shown (null if unknown).
        private Dictionary<string, bool> sectionDeformable;

        private void LoadDamageInfo(byte[] data)
        {
            this.sectionDeformable = null;
            try
            {
                CarbinFile carbin = new CarbinFile(data);
                this.sectionDeformable = new Dictionary<string, bool>();
                foreach (CarbinFile.Section section in carbin.Sections)
                {
                    this.sectionDeformable[section.Name] = section.IsDeformable;
                }
            }
            catch (Exception)
            {
            }
        }

        private string DescribeDamage(string section)
        {
            bool deformable;
            if (this.sectionDeformable == null || !this.sectionDeformable.TryGetValue(section, out deformable))
            {
                return "unknown";
            }
            return deformable ? "deformable (has damage data)" : "rigid";
        }

        // Group nodes: how many of the parts below are deformable.
        private string DescribeDamage(TreeNode node)
        {
            if (this.sectionDeformable == null)
            {
                return "";
            }
            if (this.sectionDeformable.ContainsKey(node.Text) && node.Parent != null && node.Parent.Parent != null)
            {
                return "Damage: " + this.DescribeDamage(node.Text);
            }
            Dictionary<string, bool> parts = new Dictionary<string, bool>();
            this.CollectParts(node, parts);
            int deformable = 0;
            foreach (bool value in parts.Values)
            {
                if (value)
                {
                    deformable++;
                }
            }
            return string.Format("Deformable parts: {0} of {1}", deformable, parts.Count);
        }

        private void CollectParts(TreeNode node, Dictionary<string, bool> parts)
        {
            foreach (TreeNode child in node.Nodes)
            {
                bool deformable;
                if (child.Tag is ForzaMesh && this.sectionDeformable.TryGetValue(node.Text, out deformable))
                {
                    parts[node.Text] = deformable;
                }
                this.CollectParts(child, parts);
            }
        }

        private int CountMaterial(string material)
        {
            int count = 0;
            foreach (ForzaMesh mesh in GetMeshes(this.tvModel.Nodes, false))
            {
                if (mesh.MaterialName == material)
                {
                    count++;
                }
            }
            return count;
        }

        private static string DisplaySource(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return "(none)";
            }
            return source.StartsWith(ArchivePrefix) ? source.Substring(ArchivePrefix.Length)
                : source.StartsWith(SharedPrefix) ? source.Substring(SharedPrefix.Length) + " (Shared.zip)"
                : Path.GetFileName(source) + " (folder)";
        }

        // The game's technique for the mesh's material and the textures its shader reads.
        private string DescribeShader(ForzaMesh mesh)
        {
            ShaderTextures table = ShaderTextures.For(this.GameRoot);
            if (table.Problem != null)
            {
                return "";
            }
            string technique = table.TechniqueOf(mesh.MaterialName, this.TextureKind(mesh, this.MeshLods()));
            if (technique == null)
            {
                return "\nShader: none in this game (drawn as `missing`)";
            }
            List<string> samplers = table.SamplersOf(technique);
            return "\nShader: " + technique + (samplers.Count > 0 ? "\nSamplers: " + string.Join(", ", samplers.ToArray()) : "");
        }

        // Targets are the selected parts, widened to every part sharing their materials when requested.
        private List<ForzaMesh> TargetMeshes(out List<string> materials)
        {
            List<ForzaMesh> selected = this.SelectedMeshes;
            materials = new List<string>();
            foreach (ForzaMesh mesh in selected)
            {
                if (!materials.Contains(mesh.MaterialName))
                {
                    materials.Add(mesh.MaterialName);
                }
            }
            if (!this.chkSameMaterial.Checked)
            {
                return selected;
            }
            List<ForzaMesh> targets = new List<ForzaMesh>();
            foreach (ForzaMesh mesh in GetMeshes(this.tvModel.Nodes, false))
            {
                if (materials.Contains(mesh.MaterialName))
                {
                    targets.Add(mesh);
                }
            }
            return targets;
        }

        // source: a texture, or null = back to the automatic one.
        private void AssignTexture(string source)
        {
            List<string> materials;
            List<ForzaMesh> targets = this.TargetMeshes(out materials);
            if (targets.Count == 0)
            {
                return;
            }
            if (!string.IsNullOrEmpty(source) && this.LoadPreview(source) == null)
            {
                return;
            }
            Dictionary<ForzaMesh, int> lods = (source == null) ? this.MeshLods() : null;
            foreach (ForzaMesh mesh in targets)
            {
                this.meshTextures.Remove(mesh);
                this.autoTextureSampler.Remove(mesh);
                if (source == null)
                {
                    this.SetAutoTexture(mesh, lods);
                }
                else if (source.Length > 0)
                {
                    this.meshTextures[mesh] = source;
                }
            }
            if (this.chkSameMaterial.Checked)
            {
                foreach (string material in materials)
                {
                    if (source == null)
                    {
                        this.materialTextures.Remove(material);
                    }
                    else
                    {
                        this.materialTextures[material] = source;
                    }
                }
            }
            this.ApplyTextures();
            this.UpdateMeshPanel();
            this.SetStatus(string.Format("{0} -> {1} ({2} parts)", string.Join(", ", materials.ToArray()), (source == null) ? "automatic texture" : DisplaySource(source), targets.Count));
        }

        private void AssignTextureFromFile()
        {
            if (this.SelectedMeshes.Count == 0)
            {
                return;
            }
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "Textures (*.xds;*.tga)|*.xds;*.tga|Images (*.png;*.tga;*.bmp;*.jpg)|*.png;*.tga;*.bmp;*.jpg";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    this.AssignTexture(dialog.FileName);
                }
            }
        }

        private void SetSecondUV(bool second)
        {
            List<string> materials;
            foreach (ForzaMesh mesh in this.TargetMeshes(out materials))
            {
                mesh.UseSecondUV = second;
            }
            if (this.chkSameMaterial.Checked)
            {
                foreach (string material in materials)
                {
                    this.materialSecondUV[material] = second;
                }
            }
            this.RefreshUvView();
            if (this.scene != null)
            {
                this.scene.Update(true);
            }
        }

        // Folder of the model being shown ("" for a zip root); archive texture sources are relative to it,
        // so the same assignments work in the zip and in the uncompressed car folder.
        private string ModelFolder
        {
            get { return (this.currentEntry == null) ? "" : this.currentEntry.Name.Substring(0, this.currentEntry.Name.LastIndexOf('/') + 1); }
        }

        private ForzaZip.Entry FindArchiveSource(string source)
        {
            return (this.zip == null) ? null : this.zip.Find(this.ModelFolder + source.Substring(ArchivePrefix.Length));
        }

        private bool SourceExists(string source)
        {
            if (source.StartsWith(ArchivePrefix))
            {
                return this.FindArchiveSource(source) != null;
            }
            if (source.StartsWith(SharedPrefix))
            {
                ForzaZip shared = this.SharedZip;
                return shared != null && shared.Find(source.Substring(SharedPrefix.Length)) != null;
            }
            return File.Exists(source);
        }

        // The game folder's Media\cars\Shared.zip, or null.
        private ForzaZip SharedZip
        {
            get
            {
                string path = ShaderTextures.For(this.GameRoot).SharedZipPath;
                try
                {
                    return (path != null && File.Exists(path)) ? GamePaths.OpenZip(path) : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        private void ApplyTextures()
        {
            if (this.scene == null)
            {
                return;
            }
            this.scene.ClearTextures();
            if (this.btnTextures.Checked)
            {
                foreach (KeyValuePair<ForzaMesh, string> pair in this.meshTextures)
                {
                    this.scene.SetMeshTexture(pair.Key, this.LoadPreview(pair.Value));
                }
            }
            this.scene.Update(true);
            this.RefreshUvView();
        }

        private void ClearPreviewCache()
        {
            if (this.scene != null)
            {
                this.scene.ClearTextures();
                this.scene.ReleaseGpuTextures();
            }
            foreach (Bitmap bitmap in this.previewCache.Values)
            {
                bitmap.Dispose();
            }
            this.previewCache.Clear();
        }

        private Bitmap LoadPreview(string source)
        {
            return this.LoadPreview(source, false);
        }

        // Decodes a texture source into the bitmap shown on the model (null if it cannot be read; quiet:
        // no message, for the automatic textures).
        private Bitmap LoadPreview(string source, bool quiet)
        {
            Bitmap preview;
            string key = source.StartsWith(ArchivePrefix) ? (ArchivePrefix + this.ModelFolder + source.Substring(ArchivePrefix.Length))
                : source.StartsWith(SharedPrefix) ? (SharedPrefix + ShaderTextures.For(this.GameRoot).SharedZipPath + "|" + source.Substring(SharedPrefix.Length))
                : source;
            if (this.previewCache.TryGetValue(key, out preview))
            {
                return preview;
            }
            try
            {
                Bitmap image;
                if (source.StartsWith(ArchivePrefix))
                {
                    ForzaZip.Entry entry = this.FindArchiveSource(source);
                    byte[] data = this.zip.Read(entry);
                    image = entry.Name.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ? new TgaImage(data).ToBitmap() : new XdsTexture(data).GetLevelBitmap(0);
                }
                else if (source.StartsWith(SharedPrefix))
                {
                    ForzaZip shared = this.SharedZip;
                    image = new XdsTexture(shared.Read(shared.Find(source.Substring(SharedPrefix.Length)))).GetLevelBitmap(0);
                }
                else if (source.EndsWith(".xds", StringComparison.OrdinalIgnoreCase))
                {
                    image = XdsTexture.FromFile(source).GetLevelBitmap(0);
                }
                else if (source.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
                {
                    image = TgaImage.FromFile(source).ToBitmap();
                }
                else
                {
                    using (Image file = Image.FromFile(source))
                    {
                        image = new Bitmap(file);
                    }
                }
                preview = this.chkAlphaMask.Checked ? CompositeOver(image, BaseColor) : image;
                if (preview != image)
                {
                    image.Dispose();
                }
                this.previewCache[key] = preview;
                return preview;
            }
            catch (Exception exception)
            {
                if (!quiet)
                {
                    MessageBox.Show(this, "Cannot load " + DisplaySource(source) + ":\n" + exception.Message, "Preview Texture", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                }
                return null;
            }
        }

        // Forza car textures use alpha to blend decals over the paint: preview them over a flat base color.
        private static Bitmap CompositeOver(Bitmap image, System.Drawing.Color baseColor)
        {
            Bitmap result = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
            Rectangle rect = new Rectangle(0, 0, image.Width, image.Height);
            BitmapData src = image.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData dst = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            byte[] row = new byte[image.Width * 4];
            for (int y = 0; y < image.Height; y++)
            {
                Marshal.Copy(new IntPtr(src.Scan0.ToInt64() + ((long) y * src.Stride)), row, 0, row.Length);
                for (int i = 0; i < row.Length; i += 4)
                {
                    int a = row[i + 3];
                    row[i] = (byte) (((row[i] * a) + (baseColor.B * (255 - a))) / 255);
                    row[i + 1] = (byte) (((row[i + 1] * a) + (baseColor.G * (255 - a))) / 255);
                    row[i + 2] = (byte) (((row[i + 2] * a) + (baseColor.R * (255 - a))) / 255);
                    row[i + 3] = 255;
                }
                Marshal.Copy(row, 0, new IntPtr(dst.Scan0.ToInt64() + ((long) y * dst.Stride)), row.Length);
            }
            image.UnlockBits(src);
            result.UnlockBits(dst);
            return result;
        }
    }
}
