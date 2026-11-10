namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.Windows.Forms;

    // The ways to drag a selection, as in 3ds Max's Selection Region flyout.
    public enum RegionMode
    {
        Rectangle,
        Circle,
        Fence,
        Lasso,
        Paint,
    }

    // A selection region drawn on the screen with the mouse, shared by the viewport's MESH tab and the UV mapper (screen
    // pixels in both). Rectangle and Circle: press, drag, release (the circle is centred on the press). Lasso: press, drag
    // a free line, release (closed). Fence: each click adds a corner, a double click or a click on the first corner closes it,
    // Escape cancels. Paint: drag a brush over what to select. A press and release without movement is a plain click
    // (the caller picks the item under the pointer).
    public sealed class SelectionRegion
    {
        // The mode the tool bars show, the same in the viewport and in the UV mapper.
        private static RegionMode current = RegionMode.Rectangle;
        public static event EventHandler ModeChanged;
        public const int BrushRadius = 14;

        public static RegionMode CurrentMode
        {
            get { return current; }
            set
            {
                if (current != value)
                {
                    current = value;
                    if (ModeChanged != null)
                    {
                        ModeChanged(null, EventArgs.Empty);
                    }
                }
            }
        }

        public static string Name(RegionMode mode)
        {
            switch (mode)
            {
                case RegionMode.Circle: return "Circular selection region";
                case RegionMode.Fence: return "Fence selection region (click the corners, double click to close)";
                case RegionMode.Lasso: return "Lasso selection region";
                case RegionMode.Paint: return "Paint selection region";
                default: return "Rectangular selection region";
            }
        }

        private readonly List<PointF> points = new List<PointF>();
        private RegionMode mode;
        private PointF start, now;
        private bool active, moved;

        // True from the first press until the region is finished or cancelled (a fence stays active between clicks).
        public bool Active { get { return this.active; } }
        public RegionMode Mode { get { return this.mode; } }
        // The press and release did not move: a click.
        public bool WasClick { get { return !this.moved && this.mode != RegionMode.Fence; } }

        public void Cancel()
        {
            this.active = false;
            this.points.Clear();
        }

        // A mouse press. Returns true when it closes a fence (the caller then reads the region and calls Finish).
        public bool Press(Point at, int clicks)
        {
            PointF p = new PointF(at.X, at.Y);
            if (!this.active)
            {
                this.mode = current;
                this.active = true;
                this.moved = false;
                this.start = p;
                this.now = p;
                this.points.Clear();
                this.points.Add(p);
                return false;
            }
            if (this.mode == RegionMode.Fence)
            {
                bool close = clicks >= 2 || (this.points.Count >= 3 && Distance(p, this.points[0]) <= 8f);
                if (!close)
                {
                    this.points.Add(p);
                    this.now = p;
                }
                return close && this.points.Count >= 3;
            }
            return false;
        }

        public void Move(Point at)
        {
            PointF p = new PointF(at.X, at.Y);
            this.now = p;
            if (!this.moved && Distance(p, this.start) > 3f)
            {
                this.moved = true;
            }
            if ((this.mode == RegionMode.Lasso || this.mode == RegionMode.Paint) && this.moved)
            {
                if (this.points.Count == 0 || Distance(p, this.points[this.points.Count - 1]) >= 2f)
                {
                    this.points.Add(p);
                }
            }
        }

        // The button is released. Returns true when the region is complete (every mode but a fence).
        public bool Release(Point at)
        {
            this.Move(at);
            if (this.mode == RegionMode.Fence)
            {
                return false;
            }
            this.active = false;
            return true;
        }

        // A closed fence has been read: it is no longer being drawn.
        public void Finish()
        {
            this.active = false;
        }

        private static float Distance(PointF a, PointF b)
        {
            return (float) Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));
        }

        // Is the point inside the region as drawn?
        public bool Contains(PointF p)
        {
            switch (this.mode)
            {
                case RegionMode.Rectangle:
                    return p.X >= Math.Min(this.start.X, this.now.X) && p.X <= Math.Max(this.start.X, this.now.X)
                        && p.Y >= Math.Min(this.start.Y, this.now.Y) && p.Y <= Math.Max(this.start.Y, this.now.Y);
                case RegionMode.Circle:
                    return Distance(p, this.start) <= Distance(this.now, this.start);
                case RegionMode.Paint:
                    return this.NearStroke(p);
                default:
                    return InPolygon(p, this.points);
            }
        }

        private bool NearStroke(PointF p)
        {
            if (this.points.Count == 1)
            {
                return Distance(p, this.points[0]) <= BrushRadius;
            }
            for (int i = 1; i < this.points.Count; i++)
            {
                if (SegmentDistance(p, this.points[i - 1], this.points[i]) <= BrushRadius)
                {
                    return true;
                }
            }
            return false;
        }

        private static float SegmentDistance(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float length = (dx * dx) + (dy * dy);
            float t = (length <= 0f) ? 0f : Math.Max(0f, Math.Min(1f, (((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / length));
            return Distance(p, new PointF(a.X + (t * dx), a.Y + (t * dy)));
        }

        private static bool InPolygon(PointF p, List<PointF> polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                PointF a = polygon[i], b = polygon[j];
                if (((a.Y > p.Y) != (b.Y > p.Y)) && (p.X < ((b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y)) + a.X))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        // The line to draw while the region is made, in screen pixels (a polyline: the first point is repeated at the end
        // of a closed shape). A fence ends at the pointer.
        public PointF[] Outline()
        {
            List<PointF> line = new List<PointF>();
            switch (this.mode)
            {
                case RegionMode.Rectangle:
                    line.Add(this.start);
                    line.Add(new PointF(this.now.X, this.start.Y));
                    line.Add(this.now);
                    line.Add(new PointF(this.start.X, this.now.Y));
                    line.Add(this.start);
                    break;
                case RegionMode.Circle:
                    float radius = Distance(this.now, this.start);
                    for (int i = 0; i <= 48; i++)
                    {
                        double angle = i * Math.PI * 2.0 / 48.0;
                        line.Add(new PointF(this.start.X + (float) (Math.Cos(angle) * radius), this.start.Y + (float) (Math.Sin(angle) * radius)));
                    }
                    break;
                case RegionMode.Fence:
                    line.AddRange(this.points);
                    line.Add(this.now);
                    break;
                case RegionMode.Lasso:
                    line.AddRange(this.points);
                    if (this.points.Count > 0)
                    {
                        line.Add(this.points[0]);
                    }
                    break;
                default:
                    line.AddRange(this.points);
                    break;
            }
            return line.ToArray();
        }

        // ---- the flyout of the tool bars ----

        // A drop-down button with the five modes; its image is the mode in use. Used by the viewport and the UV mapper.
        public static ToolStripDropDownButton CreateButton()
        {
            ToolStripDropDownButton button = new ToolStripDropDownButton();
            button.DisplayStyle = ToolStripItemDisplayStyle.Image;
            button.ImageScaling = ToolStripItemImageScaling.None;
            Dictionary<RegionMode, ToolStripMenuItem> items = new Dictionary<RegionMode, ToolStripMenuItem>();
            foreach (RegionMode mode in new RegionMode[] { RegionMode.Rectangle, RegionMode.Circle, RegionMode.Fence, RegionMode.Lasso, RegionMode.Paint })
            {
                RegionMode which = mode;
                ToolStripMenuItem item = new ToolStripMenuItem(Name(mode).Replace(" (click the corners, double click to close)", ""), Icon(mode));
                item.ImageScaling = ToolStripItemImageScaling.None;
                item.Click += delegate { CurrentMode = which; };
                items[mode] = item;
                button.DropDownItems.Add(item);
            }
            EventHandler refresh = delegate
            {
                button.Image = Icon(current);
                button.ToolTipText = Name(current) + ": drag the way chosen in the list (Ctrl adds, Alt removes)";
                foreach (KeyValuePair<RegionMode, ToolStripMenuItem> pair in items)
                {
                    pair.Value.Checked = pair.Key == current;
                }
            };
            refresh(null, EventArgs.Empty);
            ModeChanged += refresh;
            button.Disposed += delegate { ModeChanged -= refresh; };
            return button;
        }

        private static readonly Color Line = Color.FromArgb(205, 205, 210);

        // The 22 x 22 icons: a dashed rectangle, circle, fence polygon with its corners, lasso with its tail, brush circle.
        public static Bitmap Icon(RegionMode mode)
        {
            Bitmap bitmap = new Bitmap(22, 22);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (Pen pen = new Pen(Line, 1.4f))
            using (Brush red = new SolidBrush(Color.FromArgb(226, 38, 30)))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                pen.DashStyle = DashStyle.Dash;
                switch (mode)
                {
                    case RegionMode.Rectangle:
                        g.DrawRectangle(pen, 3, 5, 15, 12);
                        break;
                    case RegionMode.Circle:
                        g.DrawEllipse(pen, 3, 3, 16, 16);
                        break;
                    case RegionMode.Fence:
                        PointF[] corners = { new PointF(4, 6), new PointF(17, 4), new PointF(18, 15), new PointF(8, 18) };
                        g.DrawPolygon(pen, corners);
                        foreach (PointF c in corners)
                        {
                            g.FillRectangle(red, c.X - 2, c.Y - 2, 4, 4);
                        }
                        break;
                    case RegionMode.Lasso:
                        pen.DashStyle = DashStyle.Solid;
                        g.DrawCurve(pen, new PointF[] { new PointF(4, 9), new PointF(9, 3), new PointF(17, 5), new PointF(18, 11), new PointF(10, 15), new PointF(5, 12) }, 0.5f);
                        g.FillEllipse(red, 9, 15, 4, 4);
                        break;
                    default:
                        pen.DashStyle = DashStyle.Solid;
                        g.DrawEllipse(pen, 7, 7, 10, 10);
                        g.DrawCurve(pen, new PointF[] { new PointF(2, 18), new PointF(6, 14), new PointF(8, 9) }, 0.4f);
                        g.FillEllipse(red, 10, 10, 4, 4);
                        break;
                }
            }
            return bitmap;
        }
    }
}
