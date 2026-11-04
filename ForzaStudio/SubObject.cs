namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.Windows.Forms;

    // The sub-object levels of 3ds Max's Editable Mesh: what a click or a region selects.
    public enum SubLevel
    {
        Vertex,
        Edge,
        Face,
        Polygon,
        Element,
    }

    // The level in use (the same in the viewport's MESH tab and in the UV mapper), its tool bar buttons and icons, and the
    // groupings of a triangle list the levels need. A polygon = triangles joined by an edge and lying in the same plane
    // (within PlanarDegrees: the quads and flat n-gons the model was built from; a carbin only stores triangles); an
    // element = triangles joined by a shared point, again and again (a piece of the mesh).
    public static class SubObject
    {
        public const double PlanarDegrees = 2.0;

        private static SubLevel current = SubLevel.Face;
        public static event EventHandler LevelChanged;

        public static SubLevel CurrentLevel
        {
            get { return current; }
            set
            {
                if (current != value)
                {
                    current = value;
                    if (LevelChanged != null)
                    {
                        LevelChanged(null, EventArgs.Empty);
                    }
                }
            }
        }

        // Face, Polygon and Element select whole triangles.
        public static bool SelectsFaces(SubLevel level)
        {
            return level == SubLevel.Face || level == SubLevel.Polygon || level == SubLevel.Element;
        }

        public static string Name(SubLevel level)
        {
            switch (level)
            {
                case SubLevel.Vertex: return "Vertex";
                case SubLevel.Edge: return "Edge";
                case SubLevel.Polygon: return "Polygon";
                case SubLevel.Element: return "Element";
                default: return "Face";
            }
        }

        private static string Hint(SubLevel level)
        {
            switch (level)
            {
                case SubLevel.Vertex: return "Vertex: select points (a click takes the point nearest the pointer on the face under it)";
                case SubLevel.Edge: return "Edge: select edges (a click takes the edge nearest the pointer on the face under it)";
                case SubLevel.Polygon: return "Polygon: select flat polygons (triangles joined by an edge in the same plane)";
                case SubLevel.Element: return "Element: select whole pieces (triangles joined by their points)";
                default: return "Face: select triangles";
            }
        }

        // The five buttons, one checked: the level in use. keys: the hints name the keys 1-5.
        public static ToolStripButton[] CreateButtons(bool keys)
        {
            SubLevel[] levels = { SubLevel.Vertex, SubLevel.Edge, SubLevel.Face, SubLevel.Polygon, SubLevel.Element };
            ToolStripButton[] buttons = new ToolStripButton[levels.Length];
            for (int i = 0; i < levels.Length; i++)
            {
                SubLevel which = levels[i];
                ToolStripButton button = new ToolStripButton(Icon(which));
                button.DisplayStyle = ToolStripItemDisplayStyle.Image;
                button.ImageScaling = ToolStripItemImageScaling.None;
                button.ToolTipText = Hint(which) + (keys ? "  (" + (i + 1) + ")" : "");
                button.AutoToolTip = false;
                button.Click += delegate { CurrentLevel = which; };
                buttons[i] = button;
            }
            EventHandler refresh = delegate
            {
                for (int i = 0; i < levels.Length; i++)
                {
                    buttons[i].Checked = levels[i] == current;
                }
            };
            refresh(null, EventArgs.Empty);
            LevelChanged += refresh;
            buttons[0].Disposed += delegate { LevelChanged -= refresh; };
            return buttons;
        }

        private static readonly Color Teal = Color.FromArgb(70, 200, 200);

        // 22 x 22 icons in the style of 3ds Max's: four dots, a triangle outline, a filled triangle, a filled square, a cube.
        public static Bitmap Icon(SubLevel level)
        {
            Bitmap bitmap = new Bitmap(22, 22);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (Pen pen = new Pen(Teal, 1.6f))
            using (Brush fill = new SolidBrush(Teal))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                switch (level)
                {
                    case SubLevel.Vertex:
                        foreach (PointF p in new PointF[] { new PointF(11, 5), new PointF(5, 11), new PointF(17, 11), new PointF(11, 17) })
                        {
                            g.FillRectangle(fill, p.X - 2, p.Y - 2, 4, 4);
                        }
                        break;
                    case SubLevel.Edge:
                        g.DrawPolygon(pen, new PointF[] { new PointF(4, 11), new PointF(18, 4), new PointF(18, 18) });
                        break;
                    case SubLevel.Face:
                        g.FillPolygon(fill, new PointF[] { new PointF(4, 11), new PointF(18, 4), new PointF(18, 18) });
                        break;
                    case SubLevel.Polygon:
                        g.FillRectangle(fill, 5, 5, 12, 12);
                        break;
                    default:
                        PointF top = new PointF(11, 3), left = new PointF(4, 7), right = new PointF(18, 7), middle = new PointF(11, 11);
                        PointF bottomLeft = new PointF(4, 15), bottomRight = new PointF(18, 15), bottom = new PointF(11, 19);
                        using (Brush light = new SolidBrush(Color.FromArgb(120, 225, 225)))
                        using (Brush dark = new SolidBrush(Color.FromArgb(40, 150, 150)))
                        {
                            g.FillPolygon(light, new PointF[] { top, right, middle, left });
                            g.FillPolygon(fill, new PointF[] { left, middle, bottom, bottomLeft });
                            g.FillPolygon(dark, new PointF[] { middle, right, bottomRight, bottom });
                        }
                        break;
                }
            }
            return bitmap;
        }

        // ---- groupings ----

        // Points at the same place get one id (UV seams split a point into several vertices).
        public static int[] Weld(double[] positions)
        {
            int count = positions.Length / 3;
            int[] group = new int[count];
            Dictionary<string, int> ids = new Dictionary<string, int>();
            for (int v = 0; v < count; v++)
            {
                string key = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}|{1}|{2}", Math.Round(positions[v * 3], 4), Math.Round(positions[(v * 3) + 1], 4), Math.Round(positions[(v * 3) + 2], 4));
                int id;
                if (!ids.TryGetValue(key, out id))
                {
                    id = ids.Count;
                    ids[key] = id;
                }
                group[v] = id;
            }
            return group;
        }

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        private static void Union(int[] parent, int a, int b)
        {
            a = Find(parent, a);
            b = Find(parent, b);
            if (a != b)
            {
                parent[b] = a;
            }
        }

        private static int[] Roots(int[] parent)
        {
            for (int i = 0; i < parent.Length; i++)
            {
                parent[i] = Find(parent, i);
            }
            return parent;
        }

        private static int Id(int[] group, int v)
        {
            return (group == null) ? v : group[v];
        }

        // Element id of every triangle: triangles sharing a point (by group, or by vertex when group is null) are one.
        public static int[] Elements(List<int[]> triangles, int[] group)
        {
            int[] parent = new int[triangles.Count];
            Dictionary<int, int> first = new Dictionary<int, int>();
            for (int f = 0; f < triangles.Count; f++)
            {
                parent[f] = f;
            }
            for (int f = 0; f < triangles.Count; f++)
            {
                foreach (int v in triangles[f])
                {
                    int id = Id(group, v), other;
                    if (first.TryGetValue(id, out other))
                    {
                        Union(parent, other, f);
                    }
                    else
                    {
                        first[id] = f;
                    }
                }
            }
            return Roots(parent);
        }

        // Polygon id of every triangle: triangles sharing an edge whose normals are within PlanarDegrees are one.
        public static int[] Polygons(List<int[]> triangles, int[] group, double[] positions)
        {
            int[] parent = new int[triangles.Count];
            double[][] normals = new double[triangles.Count][];
            Dictionary<long, int> edgeFace = new Dictionary<long, int>();
            double limit = Math.Cos(PlanarDegrees * Math.PI / 180.0);
            for (int f = 0; f < triangles.Count; f++)
            {
                parent[f] = f;
                normals[f] = Normal(positions, triangles[f]);
            }
            for (int f = 0; f < triangles.Count; f++)
            {
                int[] t = triangles[f];
                for (int e = 0; e < 3; e++)
                {
                    long key = EdgeKey(Id(group, t[e]), Id(group, t[(e + 1) % 3]));
                    int other;
                    if (edgeFace.TryGetValue(key, out other))
                    {
                        double[] a = normals[f], b = normals[other];
                        if (a != null && b != null && ((a[0] * b[0]) + (a[1] * b[1]) + (a[2] * b[2])) >= limit)
                        {
                            Union(parent, other, f);
                        }
                    }
                    else
                    {
                        edgeFace[key] = f;
                    }
                }
            }
            return Roots(parent);
        }

        public static long EdgeKey(int a, int b)
        {
            return (a < b) ? (((long) a << 32) | (uint) b) : (((long) b << 32) | (uint) a);
        }

        private static double[] Normal(double[] p, int[] t)
        {
            double ax = p[t[1] * 3] - p[t[0] * 3], ay = p[(t[1] * 3) + 1] - p[(t[0] * 3) + 1], az = p[(t[1] * 3) + 2] - p[(t[0] * 3) + 2];
            double bx = p[t[2] * 3] - p[t[0] * 3], by = p[(t[2] * 3) + 1] - p[(t[0] * 3) + 1], bz = p[(t[2] * 3) + 2] - p[(t[0] * 3) + 2];
            double nx = (ay * bz) - (az * by), ny = (az * bx) - (ax * bz), nz = (ax * by) - (ay * bx);
            double length = Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
            return (length < 1e-12) ? null : new double[] { nx / length, ny / length, nz / length };
        }
    }
}
