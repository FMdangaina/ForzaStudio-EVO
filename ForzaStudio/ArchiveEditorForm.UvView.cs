namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.Drawing.Imaging;
    using System.IO;
    using System.Windows.Forms;

    // UV layout of the selected part(s), in the TEXTURE tab: the triangle edges of the UV set in use
    // (UV1 / UV2) drawn over the 0..1 square, with the preview texture faintly behind when there is one.
    // The small preview is redrawn when the selection, the UV set or the texture changes; "Open in
    // window" gives the same drawing in a resizable window with zoom and PNG export at any size (UvForm).
    public partial class ArchiveEditorForm
    {
        private const int UvViewSize = SideWidth;
        private const int UvViewHeight = 300;          // taller than wide: the side column is narrow (docs/UI_WORKFLOW.md D)
        private const int UvMaxTriangles = 120000;   // more than this in one selection: every n-th triangle only
        private PictureBox picUv;
        private Label lblUvInfo;

        private void AddUvViewControls(FlowLayoutPanel column)
        {
            this.picUv = new PictureBox();
            this.picUv.Size = new Size(UvViewSize, UvViewHeight);
            this.picUv.SizeMode = PictureBoxSizeMode.Normal;
            this.picUv.BackColor = System.Drawing.Color.FromArgb(24, 24, 24);
            this.picUv.Margin = new Padding(0, 2, 0, 2);
            this.picUv.Cursor = Cursors.Hand;
            this.picUv.DoubleClick += delegate { this.OpenUvWindow(); };
            this.textureTips.SetToolTip(this.picUv, "Framed on the selection's UVs (grey square = the texture, 0 to 1). Double-click to open it larger.");
            this.lblUvInfo = SideNote("");
            this.lblUvInfo.Margin = new Padding(0, 0, 0, 2);
            column.Controls.Add(SideHeading("UV layout", false));
            column.Controls.Add(SideCaption("UV set"));
            column.Controls.Add(this.cmbMeshUV);
            column.Controls.Add(this.picUv);
            column.Controls.Add(this.lblUvInfo);
            column.Controls.Add(SideWideButton("Open In Window...", delegate { this.OpenUvWindow(); }));
            column.Controls.Add(SideWideButton("Export UV Map to TGA...", delegate { this.ExportUvMap(); }));
        }

        private void OpenUvWindow()
        {
            List<ForzaMesh> meshes = this.SelectedMeshes;
            if (meshes.Count == 0)
            {
                MessageBox.Show(this, "Select a part in the tree.", "UV Layout", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            // own copy of the texture: the preview cache may dispose its bitmaps while the window is open
            Bitmap texture = null;
            string source;
            if (this.meshTextures.TryGetValue(meshes[0], out source) && source != null)
            {
                Bitmap cached = this.LoadPreview(source, true);
                if (cached != null)
                {
                    texture = new Bitmap(cached);
                }
            }
            string title = (this.tvModel.SelectedNode != null) ? this.tvModel.SelectedNode.Text : "";
            UvForm form = new UvForm(title, new List<ForzaMesh>(meshes), meshes[0].UseSecondUV, texture, Path.GetFileNameWithoutExtension(this.currentEntry.Name));
            form.Show(this);
        }

        private void RefreshUvView()
        {
            if (this.picUv == null)
            {
                return;
            }
            List<ForzaMesh> meshes = this.SelectedMeshes;
            Bitmap texture = null;
            string source;
            if (meshes.Count > 0 && this.btnTextures.Checked && this.meshTextures.TryGetValue(meshes[0], out source) && source != null)
            {
                texture = this.LoadPreview(source, true);
            }
            bool second = meshes.Count > 0 && meshes[0].UseSecondUV;
            int step;
            Bitmap image = UvForm.Render(meshes, second, UvViewSize, UvViewHeight, 6, UvForm.Frame(meshes, second), texture, 1f, false, out step);
            int triangles = 0;
            foreach (ForzaMesh mesh in meshes)
            {
                triangles += mesh.FaceCount;
            }
            this.lblUvInfo.Text = (meshes.Count == 0) ? "Select a part in the tree."
                : string.Format("{0}, {1} part{2}, {3:N0} faces{4}",
                    second ? "UV2" : "UV1", meshes.Count, meshes.Count == 1 ? "" : "s", triangles, step > 1 ? " (every " + step + "th drawn)" : "");
            Image old = this.picUv.Image;
            this.picUv.Image = image;
            if (old != null)
            {
                old.Dispose();
            }
        }
    }

    // UV layout window: the selected parts' UV set over the 0..1 square, zoom with the box or Ctrl+wheel,
    // scroll when bigger than the window, export to PNG at the size typed (the zoom is only the view).
    public class UvForm : Form
    {
        private const int Margin = 6;
        private readonly List<ForzaMesh> meshes;
        private readonly Bitmap texture;
        private readonly string baseName;
        private PictureBox picture;
        private Panel scroller;
        private ComboBox cmbSet;
        private CheckBox chkTexture;
        private CheckBox chkTransparent;
        private NumericUpDown numZoom;
        private NumericUpDown numExport;
        private NumericUpDown numLine;
        private Label lblStatus;

        public UvForm(string title, List<ForzaMesh> meshes, bool secondUV, Bitmap texture, string baseName)
        {
            this.meshes = meshes;
            this.texture = texture;
            this.baseName = baseName;
            this.Text = "UV Layout - " + title;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Size = new Size(900, 760);
            this.KeyPreview = true;

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Top;
            bar.AutoSize = true;
            bar.Padding = new Padding(6, 4, 6, 4);

            this.cmbSet = new ComboBox();
            this.cmbSet.DropDownStyle = ComboBoxStyle.DropDownList;
            this.cmbSet.Items.AddRange(new object[] { "UV1", "UV2" });
            this.cmbSet.SelectedIndex = secondUV ? 1 : 0;
            this.cmbSet.Width = 60;
            this.cmbSet.SelectedIndexChanged += delegate { this.Redraw(); };

            this.chkTexture = new CheckBox();
            this.chkTexture.Text = "Texture";
            this.chkTexture.AutoSize = true;
            this.chkTexture.Checked = texture != null;
            this.chkTexture.Enabled = texture != null;
            this.chkTexture.CheckedChanged += delegate { this.Redraw(); };

            this.numLine = Number(1, 1, 8);
            this.numLine.DecimalPlaces = 0;
            this.numLine.ValueChanged += delegate { this.Redraw(); };

            this.numZoom = Number(600, 100, 8000);
            this.numZoom.Increment = 100;
            this.numZoom.ValueChanged += delegate { this.Redraw(); };

            this.numExport = Number(2048, 64, 16384);
            this.numExport.Increment = 256;

            this.chkTransparent = new CheckBox();
            this.chkTransparent.Text = "Transparent background";
            this.chkTransparent.AutoSize = true;

            Button export = new Button();
            export.Text = "Export PNG...";
            export.AutoSize = true;
            export.Click += delegate { this.Export(); };

            bar.Controls.Add(Caption("UV set"));
            bar.Controls.Add(this.cmbSet);
            bar.Controls.Add(this.chkTexture);
            bar.Controls.Add(Caption("Line"));
            bar.Controls.Add(this.numLine);
            bar.Controls.Add(Caption("View size (px)"));
            bar.Controls.Add(this.numZoom);
            bar.Controls.Add(Caption("Export size (px)"));
            bar.Controls.Add(this.numExport);
            bar.Controls.Add(this.chkTransparent);
            bar.Controls.Add(export);

            this.lblStatus = new Label();
            this.lblStatus.Dock = DockStyle.Bottom;
            this.lblStatus.Height = 20;
            this.lblStatus.ForeColor = SystemColors.GrayText;

            this.picture = new PictureBox();
            this.picture.SizeMode = PictureBoxSizeMode.Normal;
            this.scroller = new Panel();
            this.scroller.Dock = DockStyle.Fill;
            this.scroller.AutoScroll = true;
            this.scroller.BackColor = System.Drawing.Color.FromArgb(40, 40, 40);
            this.scroller.Controls.Add(this.picture);
            this.scroller.MouseWheel += this.OnWheel;
            this.picture.MouseWheel += this.OnWheel;
            this.picture.MouseEnter += delegate { this.picture.Focus(); };

            this.Controls.Add(this.scroller);
            this.Controls.Add(bar);
            this.Controls.Add(this.lblStatus);
            ForzaTheme.Apply(this);
            this.Load += delegate { this.FitView(); this.Redraw(); };
            this.FormClosed += delegate
            {
                if (this.picture.Image != null)
                {
                    this.picture.Image.Dispose();
                }
                if (this.texture != null)
                {
                    this.texture.Dispose();
                }
            };
        }

        private static Label Caption(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Margin = new Padding(8, 6, 2, 0);
            return label;
        }

        private static NumericUpDown Number(int value, int min, int max)
        {
            NumericUpDown box = new NumericUpDown();
            box.Minimum = min;
            box.Maximum = max;
            box.Value = value;
            box.Width = 70;
            return box;
        }

        // The view starts as large as the window allows while staying square.
        private void FitView()
        {
            int side = Math.Max(Math.Min(this.scroller.ClientSize.Width, this.scroller.ClientSize.Height) - 4, 100);
            this.numZoom.Value = Math.Min(this.numZoom.Maximum, Math.Max(this.numZoom.Minimum, side));
        }

        private void OnWheel(object sender, MouseEventArgs e)
        {
            if ((Control.ModifierKeys & Keys.Control) == 0)
            {
                return;
            }
            decimal value = this.numZoom.Value * (e.Delta > 0 ? 1.15m : 1m / 1.15m);
            this.numZoom.Value = Math.Min(this.numZoom.Maximum, Math.Max(this.numZoom.Minimum, Math.Round(value)));
            ((HandledMouseEventArgs) e).Handled = true;
        }

        private bool Second
        {
            get { return this.cmbSet.SelectedIndex == 1; }
        }

        private Bitmap Make(int size, bool transparent)
        {
            int step;
            Bitmap image = Render(this.meshes, this.Second, size, Margin, this.chkTexture.Checked ? this.texture : null, (float) this.numLine.Value, transparent, out step);
            int faces = 0;
            foreach (ForzaMesh mesh in this.meshes)
            {
                faces += mesh.FaceCount;
            }
            this.lblStatus.Text = string.Format("  {0}, {1} part{2}, {3:N0} faces{4}   Ctrl+wheel: zoom", this.Second ? "UV2" : "UV1", this.meshes.Count, this.meshes.Count == 1 ? "" : "s", faces,
                step > 1 ? ", every " + step + "th drawn" : "");
            return image;
        }

        private void Redraw()
        {
            if (this.picture == null || this.lblStatus == null)
            {
                return;
            }
            Bitmap image = this.Make((int) this.numZoom.Value, false);
            Image old = this.picture.Image;
            this.picture.Image = image;
            this.picture.Size = image.Size;
            if (old != null)
            {
                old.Dispose();
            }
        }

        private void Export()
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "PNG image (*.png)|*.png";
                dialog.FileName = this.baseName + "_" + (this.Second ? "uv2" : "uv1") + ".png";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                try
                {
                    using (Bitmap image = this.Make((int) this.numExport.Value, this.chkTransparent.Checked))
                    {
                        image.Save(dialog.FileName, ImageFormat.Png);
                    }
                    this.Redraw();
                    this.lblStatus.Text = "  Saved " + dialog.FileName;
                }
                catch (Exception exception)
                {
                    MessageBox.Show(this, exception.Message, "Export UV Layout", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // size x size bitmap: the 0..1 square inside a margin, texture faint behind, triangle edges on top.
        // step > 1 when the selection was thinned to stay fast.
        public static Bitmap Render(List<ForzaMesh> meshes, bool second, int size, int margin, Bitmap texture, float lineWidth, bool transparent, out int step)
        {
            return Render(meshes, second, size, size, margin, new RectangleF(0f, 0f, 1f, 1f), texture, lineWidth, transparent, out step);
        }

        // The UV region the selection's triangles use (u from X, v from Y, bottom-up as parsed), with a small border;
        // the whole 0..1 square when there is nothing to frame. Read through the indices: meshes of one section
        // may share a vertex buffer, and only this selection's triangles count.
        public static RectangleF Frame(List<ForzaMesh> meshes, bool second)
        {
            float minU = float.MaxValue, minV = float.MaxValue, maxU = float.MinValue, maxV = float.MinValue;
            foreach (ForzaMesh mesh in meshes)
            {
                if (mesh.Indices == null || mesh.Vertices == null)
                {
                    continue;
                }
                foreach (int index in mesh.Indices)
                {
                    if (index < 0 || index >= mesh.Vertices.Length)
                    {
                        continue;
                    }
                    Microsoft.Xna.Framework.Vector2 uv = second ? mesh.Vertices[index].texture1 : mesh.Vertices[index].texture0;
                    if (float.IsNaN(uv.X) || float.IsNaN(uv.Y) || Math.Abs(uv.X) >= 100000f || Math.Abs(uv.Y) >= 100000f)
                    {
                        continue;
                    }
                    minU = Math.Min(minU, uv.X);
                    maxU = Math.Max(maxU, uv.X);
                    minV = Math.Min(minV, uv.Y);
                    maxV = Math.Max(maxV, uv.Y);
                }
            }
            if (minU > maxU)
            {
                return new RectangleF(0f, 0f, 1f, 1f);
            }
            float width = Math.Max(maxU - minU, 0.01f), height = Math.Max(maxV - minV, 0.01f);
            float border = Math.Max(width, height) * 0.04f;
            float centreU = (minU + maxU) / 2f, centreV = (minV + maxV) / 2f;
            return new RectangleF(centreU - width / 2f - border, centreV - height / 2f - border, width + 2f * border, height + 2f * border);
        }

        // width x height bitmap showing the UV region view (u from view.X, v from view.Y, bottom-up), scaled the same
        // on both axes and centred inside the margin: the 0..1 square (where the texture is) drawn where it falls.
        public static Bitmap Render(List<ForzaMesh> meshes, bool second, int width, int height, int margin, RectangleF view, Bitmap texture, float lineWidth, bool transparent, out int step)
        {
            Bitmap image = new Bitmap(width, height);
            float scale = Math.Min((width - 2 * margin) / view.Width, (height - 2 * margin) / view.Height);
            float originX = (width - view.Width * scale) / 2f - view.X * scale;         // x = originX + u * scale
            float originY = (height + view.Height * scale) / 2f + view.Y * scale;       // y = originY - v * scale
            RectangleF unit = new RectangleF(originX, originY - scale, scale, scale);    // the 0..1 square
            Rectangle square = Rectangle.Round(unit);
            int triangles = 0;
            foreach (ForzaMesh mesh in meshes)
            {
                triangles += mesh.FaceCount;
            }
            step = Math.Max(1, (triangles + 120000 - 1) / 120000);
            using (Graphics g = Graphics.FromImage(image))
            {
                g.Clear(transparent ? System.Drawing.Color.Transparent : System.Drawing.Color.FromArgb(24, 24, 24));
                if (texture != null)
                {
                    try
                    {
                        using (ImageAttributes faint = new ImageAttributes())
                        {
                            faint.SetColorMatrix(new ColorMatrix(new float[][]
                            {
                                new float[] { 1, 0, 0, 0, 0 }, new float[] { 0, 1, 0, 0, 0 }, new float[] { 0, 0, 1, 0, 0 },
                                new float[] { 0, 0, 0, 0.45f, 0 }, new float[] { 0, 0, 0, 0, 1 }
                            }));
                            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                            g.DrawImage(texture, square, 0, 0, texture.Width, texture.Height, GraphicsUnit.Pixel, faint);
                        }
                    }
                    catch (Exception)
                    {
                        // a bitmap disposed under us: no background this time
                    }
                }
                using (Pen border = new Pen(System.Drawing.Color.FromArgb(120, 120, 120)))
                {
                    g.DrawRectangle(border, unit.X, unit.Y, unit.Width, unit.Height);
                }
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen pen = new Pen(System.Drawing.Color.FromArgb(transparent ? 255 : 200, 255, 70, 60), lineWidth))
                {
                    PointF[] tri = new PointF[4];
                    foreach (ForzaMesh mesh in meshes)
                    {
                        if (mesh.Indices == null || mesh.Vertices == null)
                        {
                            continue;
                        }
                        for (int i = 0; i + 2 < mesh.Indices.Length; i += 3 * step)
                        {
                            bool valid = true;
                            for (int k = 0; k < 3 && valid; k++)
                            {
                                int index = mesh.Indices[i + k];
                                if (index < 0 || index >= mesh.Vertices.Length)
                                {
                                    valid = false;
                                    break;
                                }
                                Microsoft.Xna.Framework.Vector2 uv = second ? mesh.Vertices[index].texture1 : mesh.Vertices[index].texture0;
                                // parsed UVs are bottom-up (OBJ convention), the square is drawn top-down
                                tri[k] = new PointF(originX + uv.X * scale, originY - uv.Y * scale);
                            }
                            tri[3] = tri[0];
                            if (valid && IsFinite(tri))
                            {
                                g.DrawLines(pen, tri);
                            }
                        }
                    }
                }
            }
            return image;
        }

        private static bool IsFinite(PointF[] points)
        {
            foreach (PointF p in points)
            {
                if (float.IsNaN(p.X) || float.IsNaN(p.Y) || Math.Abs(p.X) > 1e5f || Math.Abs(p.Y) > 1e5f)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
