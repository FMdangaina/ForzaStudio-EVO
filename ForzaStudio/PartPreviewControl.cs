namespace ForzaStudio
{
    using Microsoft.Xna.Framework;
    using Microsoft.Xna.Framework.Graphics;
    using System;
    using System.Collections.Generic;
    using System.Windows.Forms;

    // Copy Part From Another Car: the source car in a small viewport next to the part list, drawn like the editor's
    // viewport (RenderScene: materials, lights; no textures, user 2026-11-16), with the highlighted and ticked parts
    // shown as selected (orange). Mouse as in the viewport: left drag or Alt + middle drag turns, middle drag moves,
    // the wheel zooms.
    public sealed class PartPreviewControl : Panel
    {
        private readonly TreeView models = new TreeView();     // never shown: the scene draws its ticked meshes
        private readonly Dictionary<string, List<ForzaMesh>> partMeshes = new Dictionary<string, List<ForzaMesh>>(StringComparer.Ordinal);
        private RenderScene scene;
        private Vector3 center;
        private float radius = 1f;
        private System.Drawing.Point lastMouse;
        private readonly Label note = new Label();
        private const string Help = "  Drag: turn   Middle drag: move   Wheel: zoom   I: part only   F: frame";

        // "Part only" (user 2026-11-17): the other parts get ForzaMesh.Hidden, so the scene simply skips them (no
        // reload, fewer draws), and the camera frames the chosen parts. Kept while the dialog is open.
        private readonly Dictionary<string, Microsoft.Xna.Framework.BoundingBox> partBounds = new Dictionary<string, Microsoft.Xna.Framework.BoundingBox>(StringComparer.Ordinal);
        private readonly HashSet<string> wanted = new HashSet<string>(StringComparer.Ordinal);
        private readonly CheckBox partOnlyBox = new CheckBox();
        private readonly Button frameButton = new Button();
        private Vector3 carCenter;
        private float carRadius = 1f;

        public PartPreviewControl()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            this.BackColor = System.Drawing.Color.FromArgb(75, 75, 70);
            this.note.Dock = DockStyle.Bottom;
            this.note.Height = 18;
            this.note.ForeColor = System.Drawing.Color.Gainsboro;
            this.note.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
            this.note.Text = Help;
            this.Controls.Add(this.note);

            System.Drawing.Color bar = System.Drawing.Color.FromArgb(45, 45, 48);
            this.partOnlyBox.Appearance = Appearance.Button;
            this.partOnlyBox.Text = "Part only";
            this.partOnlyBox.FlatStyle = FlatStyle.Flat;
            this.partOnlyBox.FlatAppearance.CheckedBackColor = System.Drawing.Color.FromArgb(200, 110, 30);
            this.partOnlyBox.BackColor = bar;
            this.partOnlyBox.ForeColor = System.Drawing.Color.Gainsboro;
            this.partOnlyBox.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.partOnlyBox.SetBounds(6, 6, 78, 24);
            this.partOnlyBox.TabStop = false;
            this.partOnlyBox.CheckedChanged += (s, e) => this.ApplyPartOnly(true);
            this.frameButton.Text = "Frame";
            this.frameButton.FlatStyle = FlatStyle.Flat;
            this.frameButton.BackColor = bar;
            this.frameButton.ForeColor = System.Drawing.Color.Gainsboro;
            this.frameButton.SetBounds(88, 6, 60, 24);
            this.frameButton.TabStop = false;
            this.frameButton.Click += (s, e) => { this.FrameView(); this.Redraw(); };
            ToolTip tips = new ToolTip();
            tips.SetToolTip(this.partOnlyBox, "Show only the chosen part, framed (I)");
            tips.SetToolTip(this.frameButton, "Frame the chosen part, or the whole car (F)");
            this.Controls.Add(this.partOnlyBox);
            this.Controls.Add(this.frameButton);
        }

        // The car's main model, every part at the lowest (most detailed) LOD it has in that file. settingsFor gives a
        // technique's shadersettings values (the viewport's material look); may be null.
        public void LoadModel(string name, byte[] data, Func<string, Dictionary<string, float[]>> settingsFor)
        {
            this.DisposeMeshes();
            TreeNode root = new TreeNode("car");
            Vector3 min = new Vector3(float.MaxValue), max = new Vector3(float.MinValue);
            using (ForzaCarBin bin = new ForzaCarBin(name, data))
            {
                for (int s = 0; s < bin.Sections.Length; s++)
                {
                    ForzaCarSection section = bin.Sections[s];
                    int best = int.MaxValue;
                    foreach (ForzaCarSubSection sub in section.SubSections)
                    {
                        best = Math.Min(best, sub.Lod);
                    }
                    for (int k = 0; k < section.SubSections.Length; k++)
                    {
                        ForzaCarSubSection sub = section.SubSections[k];
                        if (sub.Lod != best || sub.Indices == null || sub.Vertices == null)
                        {
                            continue;
                        }
                        ForzaMesh mesh = new ForzaMesh(section.Name + "_" + sub.Name, sub.Name, sub.Indices, sub.Vertices);
                        mesh.SectionIndex = s;
                        mesh.SubIndex = k;
                        mesh.Technique = ShaderSettings.TechniqueName(sub.Name, name);
                        mesh.Settings = (settingsFor != null) ? settingsFor(mesh.Technique ?? mesh.MaterialName) : null;
                        TreeNode node = new TreeNode(sub.Name);
                        node.Tag = mesh;
                        node.Checked = true;
                        root.Nodes.Add(node);
                        List<ForzaMesh> list;
                        if (!this.partMeshes.TryGetValue(section.Name, out list))
                        {
                            list = new List<ForzaMesh>();
                            this.partMeshes[section.Name] = list;
                        }
                        list.Add(mesh);
                        Microsoft.Xna.Framework.BoundingBox box;
                        if (!this.partBounds.TryGetValue(section.Name, out box))
                        {
                            box = new Microsoft.Xna.Framework.BoundingBox(new Vector3(float.MaxValue), new Vector3(float.MinValue));
                        }
                        foreach (ForzaVertex v in sub.Vertices)
                        {
                            box.Min = Vector3.Min(box.Min, v.position);
                            box.Max = Vector3.Max(box.Max, v.position);
                        }
                        this.partBounds[section.Name] = box;
                        foreach (ForzaVertex v in sub.Vertices)
                        {
                            min = Vector3.Min(min, v.position);
                            max = Vector3.Max(max, v.position);
                        }
                    }
                }
            }
            this.models.Nodes.Clear();
            this.models.Nodes.Add(root);
            if (this.partMeshes.Count > 0)
            {
                this.center = (min + max) * 0.5f;
                this.radius = Math.Max(0.01f, (max - min).Length() * 0.5f);
            }
            this.carCenter = this.center;
            this.carRadius = this.radius;
            this.FocusCamera();
            this.ApplyPartOnly(true);
        }

        // The parts shown as selected (orange); the others as the viewport draws them.
        public void ShowParts(IEnumerable<string> names)
        {
            HashSet<string> wanted = this.wanted;
            wanted.Clear();
            wanted.UnionWith(names);
            List<string> missing = new List<string>();
            foreach (string name in wanted)
            {
                if (!this.partMeshes.ContainsKey(name))
                {
                    missing.Add(name);
                }
            }
            foreach (KeyValuePair<string, List<ForzaMesh>> pair in this.partMeshes)
            {
                foreach (ForzaMesh mesh in pair.Value)
                {
                    mesh.Selected = wanted.Contains(pair.Key);
                }
            }
            this.note.Text = (missing.Count > 0) ? "  Not in the main model (lod0 / cockpit only?): " + string.Join(", ", missing.ToArray()) : Help;
            // the framing follows the list only in "Part only"; the whole car view is left where the user put it
            this.ApplyPartOnly(this.partOnlyBox.Checked);
        }

        // Hides every part but the chosen ones while "Part only" is on (nothing chosen = whole car), then frames.
        private void ApplyPartOnly(bool frame)
        {
            bool only = this.partOnlyBox.Checked && this.HasChosenMesh();
            foreach (KeyValuePair<string, List<ForzaMesh>> pair in this.partMeshes)
            {
                bool hide = only && !this.wanted.Contains(pair.Key);
                foreach (ForzaMesh mesh in pair.Value)
                {
                    mesh.Hidden = hide;
                }
            }
            if (frame)
            {
                this.FrameView();
            }
            this.Redraw();
        }

        private bool HasChosenMesh()
        {
            foreach (string name in this.wanted)
            {
                if (this.partMeshes.ContainsKey(name))
                {
                    return true;
                }
            }
            return false;
        }

        // "Part only" with a chosen part: frame the chosen parts; otherwise the whole car. The view direction is kept.
        private void FrameView()
        {
            this.center = this.carCenter;
            this.radius = this.carRadius;
            if (this.partOnlyBox.Checked)
            {
                Vector3 min = new Vector3(float.MaxValue), max = new Vector3(float.MinValue);
                bool any = false;
                foreach (string name in this.wanted)
                {
                    Microsoft.Xna.Framework.BoundingBox box;
                    if (this.partBounds.TryGetValue(name, out box) && box.Min.X <= box.Max.X)
                    {
                        min = Vector3.Min(min, box.Min);
                        max = Vector3.Max(max, box.Max);
                        any = true;
                    }
                }
                if (any)
                {
                    this.center = (min + max) * 0.5f;
                    this.radius = Math.Max(0.02f, (max - min).Length() * 0.5f);
                }
            }
            if (this.scene != null && this.partMeshes.Count > 0)
            {
                this.scene.Camera.FocusOn(this.center, this.radius);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Modifiers != Keys.None)
            {
                return;
            }
            if (e.KeyCode == Keys.I)
            {
                this.partOnlyBox.Checked = !this.partOnlyBox.Checked;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F)
            {
                this.FrameView();
                this.Redraw();
                e.Handled = true;
            }
        }

        private void EnsureScene()
        {
            if (this.scene != null || !this.IsHandleCreated || !this.Visible || this.Width <= 0 || this.Height <= 0)
            {
                return;
            }
            TreeView tree = this.models;
            Panel panel = this;
            this.scene = new RenderScene(ref tree, ref panel);
            this.scene.ShowWireframe = false;
            this.FocusCamera();
        }

        private void FocusCamera()
        {
            if (this.scene != null && this.partMeshes.Count > 0)
            {
                this.scene.Camera.FocusOn(this.center, this.radius);
                // three-quarter view, a little from above
                this.scene.Camera.Orbit(MathHelper.ToRadians(-35f), MathHelper.ToRadians(-20f), this.center);
            }
        }

        private void Redraw()
        {
            this.EnsureScene();
            if (this.scene == null)
            {
                return;
            }
            try
            {
                this.scene.Update(true);
            }
            catch (DeviceLostException)
            {
                this.scene.ResetGraphicsDevice();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            this.Redraw();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // the scene paints the whole panel
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (this.scene != null && this.Width > 0 && this.Height > 0)
            {
                this.scene.ResetGraphicsDevice();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            this.Focus();
            this.lastMouse = e.Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (this.scene == null || e.Button == MouseButtons.None)
            {
                return;
            }
            float dx = e.X - this.lastMouse.X, dy = e.Y - this.lastMouse.Y;
            this.lastMouse = e.Location;
            bool turn = e.Button == MouseButtons.Left || (e.Button == MouseButtons.Middle && (Control.ModifierKeys & Keys.Alt) != 0);
            if (turn)
            {
                this.scene.Camera.Orbit(dx * 0.01f, -dy * 0.01f, this.center);
            }
            else if (e.Button == MouseButtons.Middle)
            {
                this.scene.Camera.PanPixels(dx, dy, Vector3.Distance(this.scene.Camera.EyePosition, this.center));
            }
            else
            {
                return;
            }
            this.Redraw();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (this.scene != null)
            {
                this.scene.Camera.Dolly((e.Delta > 0) ? 1 : -1, this.center);
                this.Redraw();
            }
        }

        private void DisposeMeshes()
        {
            this.partBounds.Clear();
            foreach (List<ForzaMesh> list in this.partMeshes.Values)
            {
                foreach (ForzaMesh mesh in list)
                {
                    mesh.Dispose();
                }
            }
            this.partMeshes.Clear();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (this.scene != null)
                {
                    this.scene.Dispose();
                    this.scene = null;
                }
                this.DisposeMeshes();
                this.models.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
