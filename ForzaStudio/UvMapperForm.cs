namespace ForzaStudio
{
    using Microsoft.Xna.Framework;
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.Drawing.Imaging;
    using System.Windows.Forms;

    public delegate Bitmap TextureLoader();
    public delegate void TextureShown(Bitmap shown);

    // A texture the UV mapper can show behind the UV square: a name and how to read it.
    public sealed class UvTexture
    {
        public string Name;
        public TextureLoader Load;

        public UvTexture(string name, TextureLoader load)
        {
            this.Name = name;
            this.Load = load;
        }
    }

    // The UV mapper window: the UV square with the faces being edited, vertices to select (click, box, or a
    // whole piece with Linked), and Move / Rotate / Scale by dragging. Every step is written into the
    // vertices and shown in the viewport at once (the 'edited' callback). Apply keeps the result, Cancel
    // leaves the model as it was.
    public sealed class UvMapperForm : Form
    {
        private enum Tool { Select, Move, Rotate, Scale }

        private readonly UvSession session;
        private readonly MethodInvoker edited;
        private readonly Canvas canvas;
        private readonly ToolStripLabel lblInfo;
        private readonly ToolStripButton[] toolButtons = new ToolStripButton[4];
        private EventHandler levelChanged;

        public UvMapperForm(UvSession session, string title, List<UvTexture> textures, int initialTexture, MethodInvoker edited)
        {
            this.session = session;
            this.edited = edited;
            this.Text = "UV Mapper - " + title + " - UV" + (session.Set + 1).ToString();
            this.Size = new Size(940, 720);
            this.StartPosition = FormStartPosition.Manual;
            this.KeyPreview = true;
            this.Font = ForzaTheme.Regular;
            this.BackColor = ForzaTheme.Background;

            this.canvas = new Canvas(session, null, this);
            this.canvas.Dock = DockStyle.Fill;

            ToolStrip bar = new ToolStrip();
            bar.GripStyle = ToolStripGripStyle.Hidden;
            bar.ShowItemToolTips = true;
            bar.ImageScalingSize = new Size(22, 22);

            // The four tools: one is on at a time (Q W E R).
            string[] hints =
            {
                "Select: click a point, or drag a box (Shift adds, Ctrl toggles)  (Q)",
                "Move the selected points: drag  (W)",
                "Rotate the selected points around their centre: drag  (E)",
                "Scale the selected points from their centre: drag  (R)"
            };
            Bitmap[] icons = { ToolIcons.UvSelect(), ToolIcons.UvMove(), ToolIcons.UvRotate(), ToolIcons.UvScale() };
            for (int i = 0; i < 4; i++)
            {
                Tool which = (Tool) i;
                ToolStripButton tool = Button(icons[i], hints[i], delegate { this.SetTool(which); });
                tool.CheckOnClick = false;
                this.toolButtons[i] = tool;
                bar.Items.Add(tool);
            }
            this.canvas.CurrentTool = Tool.Select;
            this.toolButtons[(int) Tool.Select].Checked = true;
            bar.Items.Add(new ToolStripSeparator());
            bar.Items.Add(SelectionRegion.CreateButton());
            foreach (ToolStripButton level in SubObject.CreateButtons(true))
            {
                bar.Items.Add(level);
            }
            this.levelChanged = delegate { this.SetTool(Tool.Select); this.canvas.Invalidate(); };
            SubObject.LevelChanged += this.levelChanged;
            this.FormClosed += delegate { SubObject.LevelChanged -= this.levelChanged; };
            bar.Items.Add(Button(ToolIcons.Select(true), "Select all the points  (A)", delegate { this.canvas.SelectAll(); }));
            bar.Items.Add(Button(ToolIcons.Select(false), "Select no point  (Ctrl+D)", delegate { this.canvas.SelectNone(); }));
            bar.Items.Add(Button(ToolIcons.Linked(), "Select the piece (island) the selected points belong to  (L)", delegate { this.canvas.SelectLinked(); }));
            bar.Items.Add(new ToolStripSeparator());
            bar.Items.Add(Button(ToolIcons.UvFlip(true), "Flip the selection left-right  (H)", delegate { this.canvas.Flip(true); }));
            bar.Items.Add(Button(ToolIcons.UvFlip(false), "Flip the selection top-bottom  (V)", delegate { this.canvas.Flip(false); }));
            bar.Items.Add(Button(ToolIcons.UvRotate90(), "Turn the selection by 90 degrees  (T)", delegate { this.canvas.Rotate90(); }));
            bar.Items.Add(Button(ToolIcons.UvFit(), "Fit the selection into the 0-1 square, same scale on both axes  (Shift+F)", delegate { this.canvas.FitSelection(); }));
            bar.Items.Add(new ToolStripSeparator());
            bar.Items.Add(Button(ToolIcons.UvUndo(), "Undo the last change  (Ctrl+Z)", delegate { this.canvas.Undo(); }));
            bar.Items.Add(Button(ToolIcons.UvReset(), "Reset every point to how it was when the mapper opened  (Ctrl+Shift+R)", delegate { this.canvas.ResetAll(); }));
            bar.Items.Add(Button(ToolIcons.UvView(), "Fit the view to the 0-1 square  (F)", delegate { this.canvas.FitView(); }));
            this.lblInfo = new ToolStripLabel();
            this.lblInfo.Alignment = ToolStripItemAlignment.Right;
            this.lblInfo.ForeColor = ForzaTheme.TextDim;
            bar.Items.Add(this.lblInfo);
            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 44;
            Button apply = new Button();
            apply.Text = "Apply";
            apply.Size = new Size(96, 28);
            apply.Location = new System.Drawing.Point(8, 8);
            apply.Click += delegate { this.DialogResult = DialogResult.OK; this.Close(); };
            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.Size = new Size(96, 28);
            cancel.Location = new System.Drawing.Point(112, 8);
            cancel.Click += delegate { this.DialogResult = DialogResult.Cancel; this.Close(); };
            Label hint = new Label();
            hint.Text = "Wheel: zoom   Middle drag: move the view   Shift / Ctrl: add / toggle to the selection";
            hint.ForeColor = ForzaTheme.TextDim;
            hint.AutoSize = true;
            hint.Location = new System.Drawing.Point(224, 14);
            bottom.Controls.Add(apply);
            bottom.Controls.Add(cancel);
            bottom.Controls.Add(hint);

            this.Controls.Add(this.canvas);
            this.Controls.Add(this.BuildTextureBar(textures, initialTexture));
            this.Controls.Add(bar);
            this.Controls.Add(bottom);
            ForzaTheme.Apply(this);
            this.canvas.BackColor = System.Drawing.Color.FromArgb(24, 24, 26);
            this.UpdateInfo();
        }

        private static ToolStripButton Button(Bitmap icon, string hint, EventHandler click)
        {
            ToolStripButton button = new ToolStripButton(icon);
            button.DisplayStyle = ToolStripItemDisplayStyle.Image;
            button.ImageScaling = ToolStripItemImageScaling.None;
            button.AutoToolTip = false;
            button.ToolTipText = hint;
            button.Click += click;
            return button;
        }
        // ---- the texture behind the UV square ----

        private const string NoTexture = "(no texture)";
        private const string GreyChecker = "Check: grey and white";
        private const string ColourChecker = "Check: colour grid";
        private const string FromFile = "From file...";

        // Told whenever the picture behind the square changes (null: none), so the viewport can show it too.
        public TextureShown TextureChanged;

        private List<UvTexture> textures;
        private Bitmap rawTexture;            // as read, with its alpha
        private ToolStripComboBox cmbTexture;
        private ToolStripComboBox cmbChannel;
        private bool fillingTextures;

        private ToolStrip BuildTextureBar(List<UvTexture> choices, int initial)
        {
            this.textures = choices;
            ToolStrip bar = new ToolStrip();
            bar.GripStyle = ToolStripGripStyle.Hidden;
            bar.ShowItemToolTips = true;
            ToolStripLabel label = new ToolStripLabel("Texture");
            this.cmbTexture = new ToolStripComboBox();
            this.cmbTexture.DropDownStyle = ComboBoxStyle.DropDownList;
            this.cmbTexture.Width = 260;
            this.cmbTexture.ToolTipText = "The picture behind the UV square: the car's own textures, a check pattern (grey and white, or a colour grid with cell names), or a file";
            this.cmbTexture.Items.Add(NoTexture);
            this.cmbTexture.Items.Add(GreyChecker);
            this.cmbTexture.Items.Add(ColourChecker);
            foreach (UvTexture texture in choices)
            {
                this.cmbTexture.Items.Add(texture.Name);
            }
            this.cmbTexture.Items.Add(FromFile);
            this.cmbChannel = new ToolStripComboBox();
            this.cmbChannel.DropDownStyle = ComboBoxStyle.DropDownList;
            this.cmbChannel.Width = 130;
            this.cmbChannel.ToolTipText = "Which channels of the texture to show: colour only, alpha only (as grey), or both (the colour with its transparency)";
            this.cmbChannel.Items.AddRange(new object[] { "Colour only", "Alpha only", "Colour + alpha" });
            this.cmbChannel.SelectedIndex = 0;
            this.fillingTextures = true;
            this.cmbTexture.SelectedIndex = (initial >= 0 && initial < choices.Count) ? 3 + initial : 0;
            this.fillingTextures = false;
            this.cmbTexture.SelectedIndexChanged += delegate { this.TextureChosen(); };
            this.cmbChannel.SelectedIndexChanged += delegate { this.ShowTexture(); };
            bar.Items.Add(label);
            bar.Items.Add(this.cmbTexture);
            bar.Items.Add(new ToolStripSeparator());
            bar.Items.Add(new ToolStripLabel("Channels"));
            bar.Items.Add(this.cmbChannel);
            this.LoadChosenTexture();
            return bar;
        }

        private void TextureChosen()
        {
            if (this.fillingTextures)
            {
                return;
            }
            if ((string) this.cmbTexture.SelectedItem == FromFile)
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Filter = "Textures (*.xds;*.tga;*.png;*.jpg;*.bmp)|*.xds;*.tga;*.png;*.jpg;*.bmp|All files (*.*)|*.*";
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                    {
                        this.fillingTextures = true;
                        this.cmbTexture.SelectedIndex = 0;
                        this.fillingTextures = false;
                        this.LoadChosenTexture();
                        return;
                    }
                    try
                    {
                        this.SetRaw(ReadFile(dialog.FileName));
                    }
                    catch (Exception exception)
                    {
                        MessageBox.Show(this, exception.Message, "UV Mapper", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    }
                }
                return;
            }
            this.LoadChosenTexture();
        }

        private static Bitmap ReadFile(string path)
        {
            string lower = path.ToLowerInvariant();
            if (lower.EndsWith(".xds"))
            {
                return XdsTexture.FromFile(path).GetLevelBitmap(0);
            }
            if (lower.EndsWith(".tga"))
            {
                return TgaImage.FromFile(path).ToBitmap();
            }
            using (Image file = Image.FromFile(path))
            {
                return new Bitmap(file);
            }
        }

        private void LoadChosenTexture()
        {
            int index = this.cmbTexture.SelectedIndex;
            Bitmap raw = null;
            try
            {
                if (index == 1)
                {
                    raw = GreyCheck();
                }
                else if (index == 2)
                {
                    raw = ColourCheck();
                }
                else if (index >= 3 && index - 3 < this.textures.Count)
                {
                    raw = this.textures[index - 3].Load();
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, "The texture could not be read: " + exception.Message, "UV Mapper", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
            this.SetRaw(raw);
        }

        private void SetRaw(Bitmap raw)
        {
            Bitmap old = this.rawTexture;
            this.rawTexture = raw;
            this.ShowTexture();
            if (old != null)
            {
                old.Dispose();
            }
        }

        // The channels asked for, as the picture drawn behind the square.
        private void ShowTexture()
        {
            if (this.canvas == null || this.cmbChannel == null)
            {
                return;
            }
            Bitmap shown = null;
            if (this.rawTexture != null)
            {
                shown = ForChannels(this.rawTexture, this.cmbChannel.SelectedIndex);
            }
            this.canvas.SetTexture(shown);
            if (this.TextureChanged != null)
            {
                this.TextureChanged(this.canvas.CurrentTexture);
            }
        }

        private static Bitmap ForChannels(Bitmap source, int mode)
        {
            Bitmap result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            System.Drawing.Rectangle area = new System.Drawing.Rectangle(0, 0, source.Width, source.Height);
            BitmapData from = source.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData to = result.LockBits(area, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            byte[] row = new byte[source.Width * 4];
            for (int y = 0; y < source.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(new IntPtr(from.Scan0.ToInt64() + ((long) y * from.Stride)), row, 0, row.Length);
                for (int i = 0; i < row.Length; i += 4)
                {
                    if (mode == 0)
                    {
                        row[i + 3] = 255;
                    }
                    else if (mode == 1)
                    {
                        byte a = row[i + 3];
                        row[i] = a;
                        row[i + 1] = a;
                        row[i + 2] = a;
                        row[i + 3] = 255;
                    }
                }
                System.Runtime.InteropServices.Marshal.Copy(row, 0, new IntPtr(to.Scan0.ToInt64() + ((long) y * to.Stride)), row.Length);
            }
            source.UnlockBits(from);
            result.UnlockBits(to);
            return result;
        }

        // The classic grey / white check: 16 x 16 squares.
        private static Bitmap GreyCheck()
        {
            Bitmap bitmap = new Bitmap(512, 512, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                for (int y = 0; y < 16; y++)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        using (Brush brush = new SolidBrush(((x + y) % 2 == 0) ? System.Drawing.Color.FromArgb(245, 245, 245) : System.Drawing.Color.FromArgb(150, 150, 150)))
                        {
                            g.FillRectangle(brush, x * 32, y * 32, 32, 32);
                        }
                    }
                }
            }
            return bitmap;
        }

        // The colour test grid of UV tools: 8 x 8 cells of changing colour, each named (A1 ... H8), so a
        // stretched or turned mapping can be read at a glance.
        private static Bitmap ColourCheck()
        {
            Bitmap bitmap = new Bitmap(512, 512, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (Font font = new Font("Segoe UI", 12f, FontStyle.Bold))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        float hue = ((x * 8) + y) * 360f / 64f;
                        bool even = (x + y) % 2 == 0;
                        System.Drawing.Color colour = FromHsv(hue, even ? 0.55f : 0.8f, even ? 0.95f : 0.75f);
                        using (Brush brush = new SolidBrush(colour))
                        {
                            g.FillRectangle(brush, x * 64, y * 64, 64, 64);
                        }
                        g.DrawString(((char) ('A' + x)).ToString() + (y + 1).ToString(), font, Brushes.Black, (x * 64) + 4, (y * 64) + 4);
                    }
                }
                using (Pen pen = new Pen(System.Drawing.Color.FromArgb(90, 0, 0, 0)))
                {
                    for (int i = 0; i <= 8; i++)
                    {
                        g.DrawLine(pen, i * 64, 0, i * 64, 512);
                        g.DrawLine(pen, 0, i * 64, 512, i * 64);
                    }
                }
            }
            return bitmap;
        }

        private static System.Drawing.Color FromHsv(float hue, float saturation, float value)
        {
            int sector = (int) (hue / 60f) % 6;
            float f = (hue / 60f) - (float) Math.Floor(hue / 60f);
            float p = value * (1f - saturation), q = value * (1f - (f * saturation)), t = value * (1f - ((1f - f) * saturation));
            float r, g, b;
            switch (sector)
            {
                case 0: r = value; g = t; b = p; break;
                case 1: r = q; g = value; b = p; break;
                case 2: r = p; g = value; b = t; break;
                case 3: r = p; g = q; b = value; break;
                case 4: r = t; g = p; b = value; break;
                default: r = value; g = p; b = q; break;
            }
            return System.Drawing.Color.FromArgb((int) (r * 255), (int) (g * 255), (int) (b * 255));
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            this.canvas.FitView();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!this.canvas.ProcessShortcut(keyData))
            {
                return base.ProcessCmdKey(ref msg, keyData);
            }
            return true;
        }

        private void ToggleFaceMode()
        {
            SubObject.CurrentLevel = SubObject.SelectsFaces(SubObject.CurrentLevel) ? SubLevel.Vertex : SubLevel.Face;
            this.SetTool(Tool.Select);
        }

        private void SetTool(Tool tool)
        {
            this.canvas.CurrentTool = tool;
            for (int i = 0; i < this.toolButtons.Length; i++)
            {
                this.toolButtons[i].Checked = i == (int) tool;
            }
        }

        private void Changed()
        {
            this.session.Commit();
            if (this.edited != null)
            {
                this.edited();
            }
            this.UpdateInfo();
        }

        private void UpdateInfo()
        {
            Vector2 min, max;
            string selection = "nothing selected";
            if (this.session.Bounds(this.canvas.Selection, out min, out max))
            {
                selection = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:N0} selected, U {1:0.000}..{2:0.000}, V {3:0.000}..{4:0.000}", this.canvas.Selection.Count, min.X, max.X, min.Y, max.Y);
            }
            this.lblInfo.Text = string.Format("{0} mesh{1}, {2:N0} points - {3}", this.session.Parts.Count, this.session.Parts.Count == 1 ? "" : "es", this.session.EditVertexCount, selection);
        }

        // ---- the drawing surface ----

        private sealed class Canvas : Control
        {
            private readonly UvSession session;
            private Bitmap texture;
            private readonly UvMapperForm owner;
            private float zoom = 400f;
            private PointF origin = new PointF(40f, 40f);          // where UV (0, 0) is on screen
            public readonly Set<long> Selection = new Set<long>();
            public Tool CurrentTool = Tool.Move;
            private readonly List<List<float[][]>> undo = new List<List<float[][]>>();
            private readonly SelectionRegion region = new SelectionRegion();
            private bool dragging, panning, boxing;
            private System.Drawing.Point dragStart, dragLast;
            private Vector2 pivot;

            public Canvas(UvSession session, Bitmap texture, UvMapperForm owner)
            {
                this.session = session;
                this.texture = texture;
                this.owner = owner;
                this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                this.TabStop = true;
            }

            public Bitmap CurrentTexture { get { return this.texture; } }

            public void SetTexture(Bitmap shown)
            {
                Bitmap old = this.texture;
                this.texture = shown;
                this.Invalidate();
                if (old != null)
                {
                    old.Dispose();
                }
            }

            private PointF ToScreen(float u, float v)
            {
                return new PointF(this.origin.X + (u * this.zoom), this.origin.Y + (v * this.zoom));
            }

            private Vector2 ToUv(System.Drawing.Point p)
            {
                return new Vector2((p.X - this.origin.X) / this.zoom, (p.Y - this.origin.Y) / this.zoom);
            }

            public void FitView()
            {
                float side = Math.Max(50, Math.Min(this.ClientSize.Width, this.ClientSize.Height)) - 60;
                this.zoom = side;
                this.origin = new PointF((this.ClientSize.Width - side) / 2f, (this.ClientSize.Height - side) / 2f);
                this.Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(this.BackColor);
                PointF a = this.ToScreen(0f, 0f), b = this.ToScreen(1f, 1f);
                RectangleF square = new RectangleF(a.X, a.Y, b.X - a.X, b.Y - a.Y);
                g.FillRectangle(new SolidBrush(System.Drawing.Color.FromArgb(34, 34, 38)), square);
                if (this.texture != null)
                {
                    ColorMatrix matrix = new ColorMatrix();
                    matrix.Matrix33 = 0.85f;
                    ImageAttributes attributes = new ImageAttributes();
                    attributes.SetColorMatrix(matrix);
                    g.DrawImage(this.texture, System.Drawing.Rectangle.Round(square), 0, 0, this.texture.Width, this.texture.Height, GraphicsUnit.Pixel, attributes);
                }
                using (Pen grid = new Pen(System.Drawing.Color.FromArgb(48, 255, 255, 255)))
                {
                    for (int i = 1; i < 10; i++)
                    {
                        PointF p = this.ToScreen(i / 10f, i / 10f);
                        g.DrawLine(grid, p.X, a.Y, p.X, b.Y);
                        g.DrawLine(grid, a.X, p.Y, b.X, p.Y);
                    }
                }
                using (Pen border = new Pen(ForzaTheme.Red))
                {
                    g.DrawRectangle(border, square.X, square.Y, square.Width, square.Height);
                }
                foreach (UvSession.Part part in this.session.Parts)
                {
                    bool[] editing = new bool[part.Faces.Count];
                    foreach (int f in part.EditFaces)
                    {
                        editing[f] = true;
                    }
                    this.DrawFaces(g, part, editing, false, System.Drawing.Color.FromArgb(70, 70, 76));
                    this.DrawFaces(g, part, editing, true, System.Drawing.Color.FromArgb(120, 210, 140));
                    this.FillSelectedFaces(g, part, this.session.Parts.IndexOf(part));
                }
                int partCount = this.session.Parts.Count;
                bool dots = this.session.EditVertexCount <= 30000;
                for (int p = 0; p < partCount; p++)
                {
                    UvSession.Part part = this.session.Parts[p];
                    foreach (int v in part.EditVerts)
                    {
                        bool selected = this.Selection.Contains(UvSession.Key(p, v));
                        if (!selected && !dots)
                        {
                            continue;
                        }
                        PointF s = this.ToScreen(part.U[v], part.V[v]);
                        if (selected)
                        {
                            g.FillRectangle(Brushes.Red, s.X - 3, s.Y - 3, 6, 6);
                        }
                        else
                        {
                            g.FillRectangle(Brushes.SteelBlue, s.X - 1.5f, s.Y - 1.5f, 3, 3);
                        }
                    }
                }
                if (this.boxing && this.region.Active && (this.region.Mode == RegionMode.Fence || !this.region.WasClick))
                {
                    using (Pen pen = new Pen(System.Drawing.Color.White))
                    {
                        pen.DashStyle = DashStyle.Dash;
                        PointF[] outline = this.region.Outline();
                        if (outline.Length > 1)
                        {
                            g.DrawLines(pen, outline);
                        }
                    }
                }
            }

            // The faces whose three points are all selected get a translucent fill.
            private void FillSelectedFaces(Graphics g, UvSession.Part part, int partIndex)
            {
                if (this.Selection.Count == 0)
                {
                    return;
                }
                using (GraphicsPath path = new GraphicsPath())
                using (Brush fill = new SolidBrush(System.Drawing.Color.FromArgb(90, 240, 30, 30)))
                {
                    foreach (int f in part.EditFaces)
                    {
                        int[] t = part.Faces[f];
                        if (this.Selection.Contains(UvSession.Key(partIndex, t[0])) && this.Selection.Contains(UvSession.Key(partIndex, t[1])) && this.Selection.Contains(UvSession.Key(partIndex, t[2])))
                        {
                            path.StartFigure();
                            path.AddPolygon(new PointF[] { this.ToScreen(part.U[t[0]], part.V[t[0]]), this.ToScreen(part.U[t[1]], part.V[t[1]]), this.ToScreen(part.U[t[2]], part.V[t[2]]) });
                        }
                    }
                    if (path.PointCount > 0)
                    {
                        g.FillPath(fill, path);
                    }
                }
            }

            private void DrawFaces(Graphics g, UvSession.Part part, bool[] editing, bool edit, System.Drawing.Color color)
            {
                using (GraphicsPath path = new GraphicsPath())
                using (Pen pen = new Pen(color))
                {
                    int count = 0;
                    for (int f = 0; f < part.Faces.Count; f++)
                    {
                        if (editing[f] != edit)
                        {
                            continue;
                        }
                        int[] t = part.Faces[f];
                        path.StartFigure();
                        path.AddPolygon(new PointF[] { this.ToScreen(part.U[t[0]], part.V[t[0]]), this.ToScreen(part.U[t[1]], part.V[t[1]]), this.ToScreen(part.U[t[2]], part.V[t[2]]) });
                        count++;
                    }
                    if (count > 0)
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }

            // ---- mouse ----

            private bool NearestVertex(System.Drawing.Point p, out long key)
            {
                key = -1;
                float best = 10f * 10f;
                for (int i = 0; i < this.session.Parts.Count; i++)
                {
                    UvSession.Part part = this.session.Parts[i];
                    foreach (int v in part.EditVerts)
                    {
                        PointF s = this.ToScreen(part.U[v], part.V[v]);
                        float d = ((s.X - p.X) * (s.X - p.X)) + ((s.Y - p.Y) * (s.Y - p.Y));
                        if (d < best)
                        {
                            best = d;
                            key = UvSession.Key(i, v);
                        }
                    }
                }
                return key >= 0;
            }

            // Face, Polygon, Element: Select picks whole faces (the 3 points of each) instead of single points.
            public bool FaceMode { get { return SubObject.SelectsFaces(SubObject.CurrentLevel); } }

            private static float Side(PointF p, PointF a, PointF b)
            {
                return ((p.X - b.X) * (a.Y - b.Y)) - ((a.X - b.X) * (p.Y - b.Y));
            }

            private static bool InTriangle(PointF p, PointF a, PointF b, PointF c)
            {
                float d1 = Side(p, a, b), d2 = Side(p, b, c), d3 = Side(p, c, a);
                bool negative = d1 < 0 || d2 < 0 || d3 < 0, positive = d1 > 0 || d2 > 0 || d3 > 0;
                return !(negative && positive);
            }

            // A region takes the faces whose centre is inside it, a click the face under the pointer.
            // Ctrl on a face that is already selected deselects its points (the others are added by the caller).
            private void HitFaces(bool box, System.Drawing.Point click, bool toggle, List<long> hit, List<int[]> faces)
            {
                PointF pointer = new PointF(click.X, click.Y);
                for (int i = 0; i < this.session.Parts.Count; i++)
                {
                    UvSession.Part part = this.session.Parts[i];
                    for (int f = part.Faces.Count - 1; f >= 0; f--)
                    {
                        if (!part.IsEditFace(f))
                        {
                            continue;
                        }
                        int[] t = part.Faces[f];
                        PointF a = this.ToScreen(part.U[t[0]], part.V[t[0]]), b = this.ToScreen(part.U[t[1]], part.V[t[1]]), c = this.ToScreen(part.U[t[2]], part.V[t[2]]);
                        bool inside = box ? this.region.Contains(new PointF((a.X + b.X + c.X) / 3f, (a.Y + b.Y + c.Y) / 3f)) : InTriangle(pointer, a, b, c);
                        if (!inside)
                        {
                            continue;
                        }
                        if (faces != null)
                        {
                            faces.Add(new int[] { i, f });
                        }
                        long k0 = UvSession.Key(i, t[0]), k1 = UvSession.Key(i, t[1]), k2 = UvSession.Key(i, t[2]);
                        if (toggle && this.Selection.Contains(k0) && this.Selection.Contains(k1) && this.Selection.Contains(k2))
                        {
                            this.Selection.Remove(k0);
                            this.Selection.Remove(k1);
                            this.Selection.Remove(k2);
                        }
                        else
                        {
                            hit.Add(k0);
                            hit.Add(k1);
                            hit.Add(k2);
                        }
                        if (!box)
                        {
                            return;
                        }
                    }
                }
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                this.Focus();
                this.dragStart = e.Location;
                this.dragLast = e.Location;
                if (e.Button == MouseButtons.Middle)
                {
                    this.panning = true;
                    return;
                }
                if (e.Button != MouseButtons.Left)
                {
                    return;
                }
                if (this.CurrentTool == Tool.Select)
                {
                    if (this.region.Press(e.Location, e.Clicks))
                    {
                        this.FinishRegion(e.Location);      // a fence closed by this click
                        return;
                    }
                    this.boxing = this.region.Active;
                    this.Invalidate();
                }
                else if (this.Selection.Count > 0)
                {
                    this.undo.Add(this.session.Snapshot());
                    this.dragging = true;
                    Vector2 min, max;
                    this.session.Bounds(this.Selection, out min, out max);
                    this.pivot = (min + max) / 2f;
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                if (this.panning)
                {
                    this.origin = new PointF(this.origin.X + (e.X - this.dragLast.X), this.origin.Y + (e.Y - this.dragLast.Y));
                    this.dragLast = e.Location;
                    this.Invalidate();
                    return;
                }
                if (this.boxing)
                {
                    this.dragLast = e.Location;
                    this.region.Move(e.Location);
                    this.Invalidate();
                    return;
                }
                if (!this.dragging)
                {
                    return;
                }
                Vector2 pivotScreen = new Vector2(this.ToScreen(this.pivot.X, this.pivot.Y).X, this.ToScreen(this.pivot.X, this.pivot.Y).Y);
                if (this.CurrentTool == Tool.Move)
                {
                    Vector2 delta = new Vector2((e.X - this.dragLast.X) / this.zoom, (e.Y - this.dragLast.Y) / this.zoom);
                    this.session.Transform(this.Selection, this.pivot, 1f, 0f, 0f, 1f, delta);
                    this.pivot += delta;
                }
                else if (this.CurrentTool == Tool.Rotate)
                {
                    float before = (float) Math.Atan2(this.dragLast.Y - pivotScreen.Y, this.dragLast.X - pivotScreen.X);
                    float after = (float) Math.Atan2(e.Y - pivotScreen.Y, e.X - pivotScreen.X);
                    float angle = after - before, cos = (float) Math.Cos(angle), sin = (float) Math.Sin(angle);
                    this.session.Transform(this.Selection, this.pivot, cos, -sin, sin, cos, Vector2.Zero);
                }
                else if (this.CurrentTool == Tool.Scale)
                {
                    float before = (float) Math.Sqrt(Math.Pow(this.dragLast.X - pivotScreen.X, 2) + Math.Pow(this.dragLast.Y - pivotScreen.Y, 2));
                    float after = (float) Math.Sqrt(Math.Pow(e.X - pivotScreen.X, 2) + Math.Pow(e.Y - pivotScreen.Y, 2));
                    if (before > 4f && after > 4f)
                    {
                        float k = after / before;
                        this.session.Transform(this.Selection, this.pivot, k, 0f, 0f, k, Vector2.Zero);
                    }
                }
                this.dragLast = e.Location;
                this.owner.Changed();
                this.Invalidate();
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                this.panning = false;
                this.dragging = false;
                if (this.boxing && e.Button == MouseButtons.Left)
                {
                    if (!this.region.Release(e.Location))
                    {
                        return;         // a fence goes on with the next click
                    }
                    this.FinishRegion(e.Location);
                }
            }

            // Polygon / Element: the faces hit grow to their whole flat polygon (3D plane, SubObject.Polygons) or element (the
            // UV island: triangles joined by their points). Ctrl on a polygon / element that is all selected deselects it.
            private void HitGrown(bool box, System.Drawing.Point click, bool toggle, List<long> hit)
            {
                List<int[]> faces = new List<int[]>();
                this.HitFaces(box, click, false, new List<long>(), faces);
                Dictionary<int, Dictionary<int, bool>> wanted = new Dictionary<int, Dictionary<int, bool>>();
                foreach (int[] pair in faces)
                {
                    int[] ids = this.GroupIds(pair[0]);
                    Dictionary<int, bool> set;
                    if (!wanted.TryGetValue(pair[0], out set))
                    {
                        set = new Dictionary<int, bool>();
                        wanted[pair[0]] = set;
                    }
                    set[ids[pair[1]]] = true;
                }
                foreach (KeyValuePair<int, Dictionary<int, bool>> part in wanted)
                {
                    UvSession.Part p = this.session.Parts[part.Key];
                    int[] ids = this.GroupIds(part.Key);
                    List<long> keys = new List<long>();
                    foreach (int f in p.EditFaces)
                    {
                        if (part.Value.ContainsKey(ids[f]))
                        {
                            foreach (int v in p.Faces[f])
                            {
                                keys.Add(UvSession.Key(part.Key, v));
                            }
                        }
                    }
                    bool all = keys.Count > 0 && keys.TrueForAll(delegate(long k) { return this.Selection.Contains(k); });
                    if (toggle && all)
                    {
                        foreach (long k in keys)
                        {
                            this.Selection.Remove(k);
                        }
                    }
                    else
                    {
                        hit.AddRange(keys);
                    }
                }
            }

            private readonly Dictionary<int, int[]> polygonIds = new Dictionary<int, int[]>();
            private readonly Dictionary<int, int[]> elementIds = new Dictionary<int, int[]>();

            // Polygon or element id of each face of a part (by the level in use), made once.
            private int[] GroupIds(int part)
            {
                bool polygon = SubObject.CurrentLevel == SubLevel.Polygon;
                Dictionary<int, int[]> cache = polygon ? this.polygonIds : this.elementIds;
                int[] ids;
                if (!cache.TryGetValue(part, out ids))
                {
                    UvSession.Part p = this.session.Parts[part];
                    ids = polygon ? SubObject.Polygons(p.Faces, null, p.Positions) : SubObject.Elements(p.Faces, null);
                    cache[part] = ids;
                }
                return ids;
            }

            private static float SegmentDistance(PointF p, PointF a, PointF b)
            {
                float dx = b.X - a.X, dy = b.Y - a.Y, length = (dx * dx) + (dy * dy);
                float t = (length <= 0f) ? 0f : Math.Max(0f, Math.Min(1f, (((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / length));
                float x = a.X + (t * dx) - p.X, y = a.Y + (t * dy) - p.Y;
                return (float) Math.Sqrt((x * x) + (y * y));
            }

            // Edge: a click takes the edge nearest the pointer (within 8 pixels), a region the edges with both ends inside;
            // an edge selects its two points.
            private void HitEdges(bool box, System.Drawing.Point click, List<long> hit)
            {
                PointF pointer = new PointF(click.X, click.Y);
                float best = 8f;
                long bestA = -1, bestB = -1;
                for (int i = 0; i < this.session.Parts.Count; i++)
                {
                    UvSession.Part part = this.session.Parts[i];
                    foreach (int f in part.EditFaces)
                    {
                        int[] t = part.Faces[f];
                        for (int e = 0; e < 3; e++)
                        {
                            int va = t[e], vb = t[(e + 1) % 3];
                            PointF a = this.ToScreen(part.U[va], part.V[va]), b = this.ToScreen(part.U[vb], part.V[vb]);
                            if (box)
                            {
                                if (this.region.Contains(a) && this.region.Contains(b))
                                {
                                    hit.Add(UvSession.Key(i, va));
                                    hit.Add(UvSession.Key(i, vb));
                                }
                                continue;
                            }
                            float d = SegmentDistance(pointer, a, b);
                            if (d < best)
                            {
                                best = d;
                                bestA = UvSession.Key(i, va);
                                bestB = UvSession.Key(i, vb);
                            }
                        }
                    }
                }
                if (!box && bestA >= 0)
                {
                    hit.Add(bestA);
                    hit.Add(bestB);
                }
            }

            // The region is complete: the points (or faces) inside it, or the one under the pointer for a click, join the selection.
            private void FinishRegion(System.Drawing.Point at)
            {
                this.boxing = false;
                this.dragLast = at;
                bool fence = this.region.Mode == RegionMode.Fence;
                bool click = this.region.WasClick;
                bool add = (Control.ModifierKeys & Keys.Shift) != 0, toggle = (Control.ModifierKeys & Keys.Control) != 0;
                if (!add && !toggle)
                {
                    this.Selection.Clear();
                }
                bool box = fence || !click;
                List<long> hit = new List<long>();
                if (this.FaceMode)
                {
                    if (SubObject.CurrentLevel == SubLevel.Face)
                    {
                        this.HitFaces(box, at, toggle, hit, null);
                    }
                    else
                    {
                        this.HitGrown(box, at, toggle, hit);
                    }
                }
                else if (SubObject.CurrentLevel == SubLevel.Edge)
                {
                    this.HitEdges(box, at, hit);
                }
                else if (box)
                {
                    for (int i = 0; i < this.session.Parts.Count; i++)
                    {
                        UvSession.Part part = this.session.Parts[i];
                        foreach (int v in part.EditVerts)
                        {
                            if (this.region.Contains(this.ToScreen(part.U[v], part.V[v])))
                            {
                                hit.Add(UvSession.Key(i, v));
                            }
                        }
                    }
                }
                else
                {
                    long key;
                    if (this.NearestVertex(at, out key))
                    {
                        hit.Add(key);
                    }
                }
                this.region.Finish();
                foreach (long key in hit)
                {
                    if (toggle && !this.FaceMode && this.Selection.Contains(key))
                    {
                        this.Selection.Remove(key);
                    }
                    else
                    {
                        this.Selection.Add(key);
                    }
                }
                this.owner.UpdateInfo();
                this.Invalidate();
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                Vector2 before = this.ToUv(e.Location);
                this.zoom = Math.Max(20f, Math.Min(20000f, this.zoom * (e.Delta > 0 ? 1.15f : 1f / 1.15f)));
                this.origin = new PointF(e.X - (before.X * this.zoom), e.Y - (before.Y * this.zoom));
                this.Invalidate();
            }

            // ---- actions ----

            // Escape drops a region being drawn.
            private bool CancelRegion()
            {
                if (!this.region.Active)
                {
                    return false;
                }
                this.region.Cancel();
                this.boxing = false;
                this.Invalidate();
                return true;
            }

            // Enter closes a fence with the corners clicked so far.
            private bool CloseFence()
            {
                if (!this.region.Active || this.region.Mode != RegionMode.Fence)
                {
                    return false;
                }
                this.FinishRegion(this.dragLast);
                return true;
            }

            public bool ProcessShortcut(Keys keyData)
            {
                switch (keyData)
                {
                    case Keys.Escape: return this.CancelRegion();
                    case Keys.Enter: return this.CloseFence();
                    case Keys.Q: this.owner.SetTool(Tool.Select); return true;
                    case Keys.W: this.owner.SetTool(Tool.Move); return true;
                    case Keys.E: this.owner.SetTool(Tool.Rotate); return true;
                    case Keys.R: this.owner.SetTool(Tool.Scale); return true;
                    case Keys.S: this.owner.ToggleFaceMode(); return true;
                    case Keys.D1: SubObject.CurrentLevel = SubLevel.Vertex; return true;
                    case Keys.D2: SubObject.CurrentLevel = SubLevel.Edge; return true;
                    case Keys.D3: SubObject.CurrentLevel = SubLevel.Face; return true;
                    case Keys.D4: SubObject.CurrentLevel = SubLevel.Polygon; return true;
                    case Keys.D5: SubObject.CurrentLevel = SubLevel.Element; return true;
                    case Keys.A: this.SelectAll(); return true;
                    case Keys.L: this.SelectLinked(); return true;
                    case Keys.F: this.FitView(); return true;
                    case Keys.H: this.Flip(true); return true;
                    case Keys.V: this.Flip(false); return true;
                    case Keys.T: this.Rotate90(); return true;
                    case Keys.Shift | Keys.F: this.FitSelection(); return true;
                    case Keys.Control | Keys.D: this.SelectNone(); return true;
                    case Keys.Control | Keys.Shift | Keys.R: this.ResetAll(); return true;
                    case Keys.Control | Keys.Z: this.Undo(); return true;
                }
                return false;
            }

            public void SelectAll()
            {
                this.Selection.Clear();
                for (int i = 0; i < this.session.Parts.Count; i++)
                {
                    foreach (int v in this.session.Parts[i].EditVerts)
                    {
                        this.Selection.Add(UvSession.Key(i, v));
                    }
                }
                this.owner.UpdateInfo();
                this.Invalidate();
            }

            public void SelectNone()
            {
                this.Selection.Clear();
                this.owner.UpdateInfo();
                this.Invalidate();
            }

            public void SelectLinked()
            {
                if (this.Selection.Count == 0)
                {
                    return;
                }
                foreach (long key in this.session.Island(new List<long>(this.Selection)))
                {
                    this.Selection.Add(key);
                }
                this.owner.UpdateInfo();
                this.Invalidate();
            }

            private bool Begin()
            {
                if (this.Selection.Count == 0)
                {
                    return false;
                }
                this.undo.Add(this.session.Snapshot());
                return true;
            }

            private Vector2 Centre()
            {
                Vector2 min, max;
                this.session.Bounds(this.Selection, out min, out max);
                return (min + max) / 2f;
            }

            public void Flip(bool u)
            {
                if (!this.Begin())
                {
                    return;
                }
                this.session.Transform(this.Selection, this.Centre(), u ? -1f : 1f, 0f, 0f, u ? 1f : -1f, Vector2.Zero);
                this.owner.Changed();
                this.Invalidate();
            }

            public void Rotate90()
            {
                if (!this.Begin())
                {
                    return;
                }
                this.session.Transform(this.Selection, this.Centre(), 0f, -1f, 1f, 0f, Vector2.Zero);
                this.owner.Changed();
                this.Invalidate();
            }

            // The selection into the 0..1 square, the same scale on both axes, centred.
            public void FitSelection()
            {
                Vector2 min, max;
                if (!this.session.Bounds(this.Selection, out min, out max) || !this.Begin())
                {
                    return;
                }
                float extent = Math.Max(Math.Max(max.X - min.X, max.Y - min.Y), 1e-6f);
                float k = 1f / extent;
                Vector2 centre = (min + max) / 2f;
                this.session.Transform(this.Selection, centre, k, 0f, 0f, k, new Vector2(0.5f, 0.5f) - centre);
                this.owner.Changed();
                this.Invalidate();
            }

            public void Undo()
            {
                if (this.undo.Count == 0)
                {
                    return;
                }
                this.session.Restore(this.undo[this.undo.Count - 1]);
                this.undo.RemoveAt(this.undo.Count - 1);
                this.owner.Changed();
                this.Invalidate();
            }

            public void ResetAll()
            {
                this.undo.Add(this.session.Snapshot());
                this.session.Reset();
                this.owner.Changed();
                this.Invalidate();
            }
        }
    }
}
