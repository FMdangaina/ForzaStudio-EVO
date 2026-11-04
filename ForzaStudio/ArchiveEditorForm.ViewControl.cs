namespace ForzaStudio
{
    using Microsoft.Xna.Framework;
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Windows.Forms;

    // The viewport's view controls: standard views on the numeric keypad (1 bottom, 2 left, 3 right,
    // 4 front, 5 back, 6 top), an orthographic projection (the views switch to it; the toolbar button
    // goes back and forth), and the rule about who owns the keyboard: the keypad drives the view while
    // the pointer is over the viewport or the viewport has the focus, and never types into a box.
    public partial class ArchiveEditorForm
    {
        // Keys 1-6 (keypad and digit row), the order of a 3D editor: the principal views first, the bottom last.
        private static readonly Camera.StandardView[] KeyViews =
        {
            Camera.StandardView.Front, Camera.StandardView.Back, Camera.StandardView.Right,
            Camera.StandardView.Left, Camera.StandardView.Top, Camera.StandardView.Bottom
        };

        private ToolStripButton btnOrtho;
        private bool syncingOrtho;

        private ToolStrip toolsStrip;
        private ToolStripButton btnToolDelete, btnToolDetach, btnToolLinked, btnToolNone, btnToolUv;

        private static ToolStripButton ToolButton(Bitmap icon, string hint, EventHandler click)
        {
            ToolStripButton button = new ToolStripButton(icon);
            button.DisplayStyle = ToolStripItemDisplayStyle.Image;
            button.ImageScaling = ToolStripItemImageScaling.None;
            button.ToolTipText = hint;
            button.AutoToolTip = false;
            button.Click += click;
            return button;
        }

        // Keys of the mesh tools, while the MESH tab is open and the viewer has the focus (W A S D Q E move
        // the camera, so the letters here are others).
        private bool HandleToolKey(Keys keyData)
        {
            if (!this.MeshEditActive || !this.pnlViewport.Focused || this.FocusIsTextEntry())
            {
                return false;
            }
            if (this.MeshEditRegionKey(keyData))
            {
                return true;
            }
            switch (keyData)
            {
                case Keys.Delete: this.DeleteSelectedFaces(); return true;
                case Keys.L: this.SelectLinkedFaces(); return true;
                case Keys.P: this.DetachSelectedFaces(); return true;
                case Keys.U: this.OpenUvMapper(); return true;
                case Keys.Control | Keys.A: this.SelectAllFaces(); return true;
                case Keys.Control | Keys.Shift | Keys.A: this.ClearFaceSelection(); return true;
            }
            return false;
        }

        // The tool bar under the viewport's: the six views, the projection, and the mesh / UV tools, each
        // with an icon and, as its hint, what it does and its key.
        private ToolStrip BuildToolsStrip()
        {
            ToolStrip strip = new ToolStrip();
            strip.GripStyle = ToolStripGripStyle.Hidden;
            strip.ShowItemToolTips = true;
            strip.ImageScalingSize = new Size(22, 22);
            string[] names = { "Front", "Back", "Right", "Left", "Top", "Bottom" };       // as KeyViews, and ToolIcons.View's faces
            for (int i = 0; i < KeyViews.Length; i++)
            {
                Camera.StandardView view = KeyViews[i];
                strip.Items.Add(ToolButton(ToolIcons.View(i), names[i] + " view, orthographic  (" + (i + 1).ToString() + ")", delegate { this.ShowStandardView(view); }));
            }
            this.btnOrtho = new ToolStripButton(ToolIcons.Orthographic());
            this.btnOrtho.DisplayStyle = ToolStripItemDisplayStyle.Image;
            this.btnOrtho.ImageScaling = ToolStripItemImageScaling.None;
            this.btnOrtho.AutoToolTip = false;
            this.btnOrtho.CheckOnClick = true;
            this.btnOrtho.ToolTipText = "Orthographic projection on / off: no perspective, to line things up. The standard views switch it on.";
            this.btnOrtho.CheckedChanged += delegate
            {
                if (this.syncingOrtho || this.scene == null)
                {
                    return;
                }
                Vector3 min, max;
                Vector3 focus = this.ModelBounds(out min, out max) ? (min + max) / 2f : Vector3.Zero;
                // Half the model's depth along the view: the perspective is matched at the near side of the
                // model (not its middle), so the car does not jump closer when the projection changes.
                Vector3 size = max - min, look = this.scene.Camera.ForwardDirection;
                float half = (Math.Abs(look.X) * size.X + Math.Abs(look.Y) * size.Y + Math.Abs(look.Z) * size.Z) / 2f;
                this.scene.Camera.SetOrthographic(this.btnOrtho.Checked, focus, (min.X > max.X) ? 0f : half);
                this.scene.Update(true);
            };

            strip.Items.Add(this.btnOrtho);
            strip.Items.Add(new ToolStripSeparator());
            foreach (ToolStripButton level in SubObject.CreateButtons(false))
            {
                strip.Items.Add(level);
            }
            strip.Items.Add(SelectionRegion.CreateButton());
            strip.Items.Add(ToolButton(ToolIcons.Select(true), "Select all the faces shown (MESH tab)  (Ctrl+A)", delegate { this.SelectAllFaces(); }));
            this.btnToolNone = ToolButton(ToolIcons.Select(false), "Select no face  (Ctrl+Shift+A)", delegate { this.ClearFaceSelection(); });
            this.btnToolLinked = ToolButton(ToolIcons.Linked(), "Select the pieces the selected faces belong to  (L)", delegate { this.SelectLinkedFaces(); });
            this.btnToolDelete = ToolButton(ToolIcons.Delete(), "Delete the selected faces  (Delete)", delegate { this.DeleteSelectedFaces(); });
            this.btnToolDetach = ToolButton(ToolIcons.Detach(), "Detach the selected faces into a mesh of their own, in the same part  (P)", delegate { this.DetachSelectedFaces(); });
            this.btnToolUv = ToolButton(ToolIcons.Uv(), "UV Mapper: edit the UV of the selected faces, or of the part selected in the tree  (U)", delegate { this.OpenUvMapper(); });
            strip.Items.Add(this.btnToolNone);
            strip.Items.Add(this.btnToolLinked);
            strip.Items.Add(this.btnToolDelete);
            strip.Items.Add(this.btnToolDetach);
            strip.Items.Add(new ToolStripSeparator());
            strip.Items.Add(this.btnToolUv);
            this.toolsStrip = strip;
            this.UpdateFaceSelectionLabel();
            return strip;
        }

        // Bounds of the parts checked in the tree, model axes. False when nothing is shown.
        private bool ModelBounds(out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.MaxValue);
            max = new Vector3(float.MinValue);
            bool any = false;
            foreach (ForzaMesh mesh in GetMeshes(this.tvModel.Nodes, true))
            {
                if (mesh.Hidden || mesh.Vertices == null)
                {
                    continue;
                }
                foreach (ForzaVertex vertex in mesh.Vertices)
                {
                    min = Vector3.Min(min, vertex.position);
                    max = Vector3.Max(max, vertex.position);
                    any = true;
                }
            }
            return any;
        }

        private void ShowStandardView(Camera.StandardView view)
        {
            if (this.scene == null)
            {
                return;
            }
            Vector3 min, max;
            Vector3 centre = Vector3.Zero;
            float height = 5.5f;
            if (this.ModelBounds(out min, out max))
            {
                centre = (min + max) / 2f;
                Vector3 size = max - min;
                float aspect = Math.Max(0.5f, (float) this.pnlViewport.Width / Math.Max(1, this.pnlViewport.Height));
                // The extents across the screen in this view; 15% margin.
                float across, up;
                switch (view)
                {
                    case Camera.StandardView.Left:
                    case Camera.StandardView.Right: across = size.Z; up = size.Y; break;
                    case Camera.StandardView.Front:
                    case Camera.StandardView.Back: across = size.X; up = size.Y; break;
                    default: across = size.X; up = size.Z; break;
                }
                height = Math.Max(up, across / aspect) * 1.15f;
            }
            this.scene.Camera.SetStandardView(view, centre, height);
            this.scene.Update(true);
            this.SetStatus(string.Format("{0} view (orthographic). Wheel zooms, drag moves the view.", view));
        }

        // Keeps the toolbar button and the label in step with the camera (it can leave orthographic
        // by itself: looking through a game camera is a perspective view).
        private void SyncViewButtons()
        {
            if (this.scene == null || this.btnOrtho == null)
            {
                return;
            }
            bool ortho = this.scene.Camera.Orthographic;
            if (this.trkFov != null)
            {
                // The slider follows the camera (a game camera view sets its own FOV); it is for perspective only.
                int degrees = (int) Math.Round(this.scene.Camera.FieldOfViewDegrees);
                degrees = Math.Max(this.trkFov.Minimum, Math.Min(this.trkFov.Maximum, degrees));
                if (this.trkFov.Value != degrees)
                {
                    this.syncingFov = true;
                    this.trkFov.Value = degrees;
                    this.syncingFov = false;
                }
                this.trkFov.Enabled = !ortho;
            }
            if (this.btnOrtho.Checked != ortho)
            {
                this.syncingOrtho = true;
                this.btnOrtho.Checked = ortho;
                this.syncingOrtho = false;
            }
        }

        // The control that has the keyboard takes text: a box, a list being typed into, a text editor.
        private bool FocusIsTextEntry()
        {
            Control focused = this.ActiveControl;
            while (focused is ContainerControl && ((ContainerControl) focused).ActiveControl != null)
            {
                focused = ((ContainerControl) focused).ActiveControl;
            }
            return focused is TextBoxBase || focused is NumericUpDown || focused is RichTextBox
                || (focused is ComboBox && ((ComboBox) focused).DropDownStyle != ComboBoxStyle.DropDownList)
                || (focused != null && focused.GetType().Name.IndexOf("TextBox", StringComparison.Ordinal) >= 0);
        }

        // The point the model turns around: the middle of what is shown (0, 0, 0 when nothing is).
        private Vector3 OrbitPivot()
        {
            Vector3 min, max;
            return this.ModelBounds(out min, out max) ? (min + max) / 2f : Vector3.Zero;
        }

        private bool PointerOverViewport()
        {
            if (this.pnlViewport == null || !this.pnlViewport.IsHandleCreated || !this.pnlViewport.Visible)
            {
                return false;
            }
            return this.pnlViewport.ClientRectangle.Contains(this.pnlViewport.PointToClient(Cursor.Position));
        }

        // Numpad 1-6 as views, only when the viewer is the one being looked at: the pointer is over
        // it or it has the focus. Consumed, so the digit is never typed into a box.
        private bool HandleViewKey(Keys keyData)
        {
            if (this.HandleToolKey(keyData))
            {
                return true;
            }
            bool pad = keyData >= Keys.NumPad1 && keyData <= Keys.NumPad6;
            bool row = keyData >= Keys.D1 && keyData <= Keys.D6;
            if ((!pad && !row) || this.pnlModel == null || !this.pnlModel.Visible || this.scene == null)
            {
                return false;
            }
            // The keypad works while the pointer is over the viewer; the digit row only while the
            // viewer has the focus (a box being typed into keeps its digits).
            if (pad ? (!this.pnlViewport.Focused && !this.PointerOverViewport()) : (!this.pnlViewport.Focused || this.FocusIsTextEntry()))
            {
                return false;
            }
            this.ShowStandardView(KeyViews[pad ? keyData - Keys.NumPad1 : keyData - Keys.D1]);
            return true;
        }

        // The viewport takes the focus (for W A S D) when the pointer comes over it, unless a box is
        // being typed into: moving the mouse must not pull the keyboard out of a spin box.
        private void ViewportPointerEntered()
        {
            if (!this.FocusIsTextEntry())
            {
                this.pnlViewport.Focus();
            }
        }
    }
}
