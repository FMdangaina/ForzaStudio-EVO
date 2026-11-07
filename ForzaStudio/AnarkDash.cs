using System;
using System.Collections.Generic;
using System.Drawing;

namespace ForzaStudio
{
    // A digital dash: the .fbf scene and the .bgf graph of one Dash_<car>, joined by element id, with the
    // initial state of every node (the values in the graph; slides and animations are not applied yet).
    // Coordinates: orthographic camera on -Z, X right, Y up, rotations in radians (left-handed: a positive
    // rotation.z turns clockwise on screen). Node transform as in UI Composer: position + R * S * (p - pivot).
    public class AnarkDash
    {
        public class Node
        {
            public int Index;                      // in Bgf.Elements
            public AnarkBgf.Element Element;
            public AnarkFbf.Record Record;         // null for behaviours
            public AnarkBsg.Node Base;             // base transform (.bsg), null for materials, images, behaviours
            public Node Parent;
            public List<Node> Children = new List<Node>();

            // world transform: screen = (A*x + C*y + X, B*x + D*y + Y), depth Z
            public float A = 1, B, C, D = 1, X, Y, Z;
            public float Opacity = 1;               // 0..1, including the parents

            public string Name
            {
                get
                {
                    if (this.Element.Type == AnarkBgf.ElementType.Behaviour)
                    {
                        return "Behaviour";
                    }
                    return (this.Record != null) ? this.Record.Name : "#" + this.Element.Id;
                }
            }

            public PointF Transform(float x, float y)
            {
                return new PointF(this.A * x + this.C * y + this.X, this.B * x + this.D * y + this.Y);
            }
        }

        public enum ItemKind
        {
            Rectangle,
            Text,
        }

        // Something drawn: a model's rectangle (with its material and texture) or a text.
        public class Item
        {
            public ItemKind Kind;
            public Node Node;
            public PointF[] Corners;               // world: bottom-left, bottom-right, top-right, top-left
            public RectangleF LocalRect;           // rectangles: the mesh bounds in the node's own space (y up)
            public Color Color = Color.White;      // material diffuse or text colour, with the opacity in alpha
            public string Texture;                 // GAME:\ path of the image, or null
            public string Text;
            public string Font;
            public float Size;
            public int HorzAlign;                  // 0 left, 1 center, 2 right
            public int VertAlign;                  // 0 top, 1 middle, 2 bottom
        }

        private static readonly float[] DefaultValues = new float[] { 0, 0, 0, 0, 0, 0, 1, 1, 1, 0, 0, 0, -50, -50, 0, 50, 50, 0, 1 };

        public AnarkFbf Fbf;
        public AnarkBgf Bgf;
        public AnarkBsg Bsg;                       // may be null
        public List<Node> Nodes = new List<Node>();
        public Node Root;
        public List<Item> Items = new List<Item>();   // in drawing order (far to near)

        public AnarkDash(AnarkFbf fbf, AnarkBgf bgf, AnarkBsg bsg)
        {
            this.Fbf = fbf;
            this.Bgf = bgf;
            this.Bsg = bsg;
            this.Build();
        }

        // The node tree from the three files (again after elements were added).
        private void Build()
        {
            AnarkBgf bgf = this.Bgf;
            this.Nodes.Clear();
            this.Root = null;
            for (int i = 0; i < bgf.Elements.Count; i++)
            {
                Node node = new Node();
                node.Index = i;
                node.Element = bgf.Elements[i];
                if (node.Element.Type != AnarkBgf.ElementType.Behaviour)
                {
                    node.Record = this.Fbf.Find(node.Element.Id);
                    node.Base = (this.Bsg != null) ? this.Bsg.Find(node.Element.Id) : null;
                }
                this.Nodes.Add(node);
            }
            foreach (Node node in this.Nodes)
            {
                int parent = node.Element.Parent;
                if (parent >= 0 && parent < this.Nodes.Count && parent != node.Index)
                {
                    node.Parent = this.Nodes[parent];
                    node.Parent.Children.Add(node);
                }
                else if (this.Root == null)
                {
                    this.Root = node;
                }
            }
            this.Update();
        }

        // Recomputes the transforms and the drawn items (after a property changed).
        public void Update()
        {
            foreach (Node node in this.Nodes)
            {
                if (node.Parent == null)
                {
                    this.Place(node, null);
                }
            }
            this.Items.Clear();
            foreach (Node node in this.Nodes)
            {
                Item item = this.MakeItem(node);
                if (item != null)
                {
                    this.Items.Add(item);
                }
            }
            // far to near; the graph order breaks ties (the sort is made stable by the index)
            this.Items.Sort(delegate(Item p, Item q)
            {
                int c = q.Node.Z.CompareTo(p.Node.Z);
                return (c != 0) ? c : p.Node.Index.CompareTo(q.Node.Index);
            });
        }

        private void Place(Node node, Node parent)
        {
            AnarkBgf.Element e = node.Element;
            bool spatial = e.Type == AnarkBgf.ElementType.Node || e.Type == AnarkBgf.ElementType.Text || e.Type == AnarkBgf.ElementType.Component;
            float a = 1, b = 0, c = 0, d = 1, x = 0, y = 0, z = 0, opacity = 1;
            if (spatial)
            {
                float[] v = (node.Base != null) ? node.Base.Values : DefaultValues;
                float sx = e.GetFloat("scale.x", v[AnarkBsg.Node.Scale]), sy = e.GetFloat("scale.y", v[AnarkBsg.Node.Scale + 1]);
                float rx = e.GetFloat("rotation.x", v[AnarkBsg.Node.Rotation]), ry = e.GetFloat("rotation.y", v[AnarkBsg.Node.Rotation + 1]);
                float rz = e.GetFloat("rotation.z", v[AnarkBsg.Node.Rotation + 2]);
                float px = e.GetFloat("pivot.x", v[AnarkBsg.Node.Pivot]), py = e.GetFloat("pivot.y", v[AnarkBsg.Node.Pivot + 1]);
                // flat objects seen along Z: a rotation about X or Y only shortens (or mirrors) the other axis
                sx *= (float) Math.Cos(ry);
                sy *= (float) Math.Cos(rx);
                float cos = (float) Math.Cos(rz), sin = (float) Math.Sin(rz);
                // clockwise rotation in a Y-up frame: x' = cos*x + sin*y, y' = -sin*x + cos*y
                a = cos * sx;
                b = -sin * sx;
                c = sin * sy;
                d = cos * sy;
                x = e.GetFloat("position.x", v[AnarkBsg.Node.Position]) - (a * px + c * py);
                y = e.GetFloat("position.y", v[AnarkBsg.Node.Position + 1]) - (b * px + d * py);
                z = e.GetFloat("position.z", v[AnarkBsg.Node.Position + 2]);
                opacity = e.GetFloat("opacity", v[AnarkBsg.Node.Opacity] * 100) / 100f;
            }
            if (parent != null)
            {
                node.A = parent.A * a + parent.C * b;
                node.B = parent.B * a + parent.D * b;
                node.C = parent.A * c + parent.C * d;
                node.D = parent.B * c + parent.D * d;
                node.X = parent.A * x + parent.C * y + parent.X;
                node.Y = parent.B * x + parent.D * y + parent.Y;
                node.Z = parent.Z + z;
                node.Opacity = parent.Opacity * opacity;
            }
            else
            {
                node.A = a;
                node.B = b;
                node.C = c;
                node.D = d;
                node.X = x;
                node.Y = y;
                node.Z = z;
                node.Opacity = opacity;
            }
            foreach (Node child in node.Children)
            {
                this.Place(child, node);
            }
        }

        private Item MakeItem(Node node)
        {
            if (node.Record == null)
            {
                return null;
            }
            AnarkBgf.Element e = node.Element;
            if (node.Record.Type == AnarkFbf.RecordType.Model)
            {
                int meshIndex = (int) AnarkFbf.ReadU32(node.Record.Body, 0);
                float minX = -50, minY = -50, maxX = 50, maxY = 50;
                if (meshIndex >= 0 && meshIndex < this.Fbf.Meshes.Count)
                {
                    byte[] desc = this.Fbf.Meshes[meshIndex].VertexDesc;
                    minX = AnarkFbf.ReadFloat(desc, 16);
                    minY = AnarkFbf.ReadFloat(desc, 20);
                    maxX = AnarkFbf.ReadFloat(desc, 32);
                    maxY = AnarkFbf.ReadFloat(desc, 36);
                }
                Item item = new Item();
                item.Kind = ItemKind.Rectangle;
                item.Node = node;
                item.Corners = new PointF[] { node.Transform(minX, minY), node.Transform(maxX, minY), node.Transform(maxX, maxY), node.Transform(minX, maxY) };
                item.LocalRect = RectangleF.FromLTRB(minX, minY, maxX, maxY);
                float opacity = node.Opacity;
                Color color = Color.White;
                Node material = MaterialOf(node);
                if (material != null)
                {
                    float[] rgb = this.MaterialDiffuse(material);
                    opacity *= material.Element.GetFloat("opacity", 100) / 100;
                    color = Color.FromArgb(255, Clamp(rgb[0] / 255), Clamp(rgb[1] / 255), Clamp(rgb[2] / 255));
                    Node image = ImageOf(material);
                    if (image != null)
                    {
                        item.Texture = image.Record.Path;
                    }
                }
                item.Color = Color.FromArgb(Clamp(opacity), color);
                return item;
            }
            if (node.Record.Type == AnarkFbf.RecordType.Text)
            {
                Item item = new Item();
                item.Kind = ItemKind.Text;
                item.Node = node;
                item.Text = this.Bgf.GetString(e.Find("textstring"));
                if (string.IsNullOrEmpty(item.Text))
                {
                    item.Text = string.IsNullOrEmpty(node.Record.Text) ? node.Record.Name : node.Record.Text;
                }
                item.Font = this.Bgf.GetString(e.Find("font")) ?? node.Record.Font;
                item.Size = e.GetFloat("size", 24);
                item.HorzAlign = e.GetInt("horzalign", 0);
                item.VertAlign = e.GetInt("vertalign", 1);
                float r = e.GetFloat("textcolor.r", 255), g = e.GetFloat("textcolor.g", 255), bl = e.GetFloat("textcolor.b", 255);
                item.Color = Color.FromArgb(Clamp(node.Opacity), Clamp(r / 255), Clamp(g / 255), Clamp(bl / 255));
                item.Corners = new PointF[] { node.Transform(0, 0) };
                return item;
            }
            return null;
        }

        private static int Clamp(float unit)
        {
            int v = (int) Math.Round(unit * 255);
            return (v < 0) ? 0 : (v > 255) ? 255 : v;
        }

        // The material under a model, and the image under a material.
        public static Node MaterialOf(Node model)
        {
            foreach (Node child in model.Children)
            {
                if (child.Record != null && child.Record.Type == AnarkFbf.RecordType.Material)
                {
                    return child;
                }
            }
            return null;
        }

        public static Node ImageOf(Node material)
        {
            foreach (Node child in material.Children)
            {
                if (child.Record != null && child.Record.Type == AnarkFbf.RecordType.Image)
                {
                    return child;
                }
            }
            return null;
        }

        // .fbf material: float diffuse r, g, b, a (0..1); the graph may override diffuse (0..255). Returns 0..255.
        private float[] MaterialDiffuse(Node material)
        {
            byte[] m = material.Record.Body;
            float[] rgb = new float[3];
            for (int k = 0; k < 3; k++)
            {
                rgb[k] = material.Element.GetFloat(DiffuseNames[k], AnarkFbf.ReadFloat(m, k * 4) * 255);
            }
            return rgb;
        }

        // ---- Editing ----
        // Editing a value changes only values already in the files: no property, string or record is added, so every
        // count in the .bgf header (strings at 0x3C, properties at 0x2C/0x78, components 0x28/0x74, behaviours 0x30/0x7C)
        // and the order of the graph properties stay as they are. Duplicate / Remove (above) are the exceptions and
        // keep the counts themselves.
        // A value is written where the game reads it: the graph property when the element has one, else the base
        // (.bsg node, .fbf material/text/image record). Every .bgf string belongs to one property only (all 431 files),
        // so a text or font is changed in place.

        public const string SourceGraph = "bgf", SourceBase = "bsg", SourceScene = "fbf";

        private static readonly string[] DiffuseNames = new string[] { "diffuse.r", "diffuse.g", "diffuse.b" };
        private static readonly string[] TextColorNames = new string[] { "textcolor.r", "textcolor.g", "textcolor.b" };

        public bool FbfChanged, BgfChanged, BsgChanged;

        public bool Changed { get { return this.FbfChanged || this.BgfChanged || this.BsgChanged; } }

        // Index in AnarkBsg.Node.Values of a transform property, or -1.
        private static int BaseIndex(string name)
        {
            if (name == "opacity")
            {
                return AnarkBsg.Node.Opacity;
            }
            int dot = name.IndexOf('.');
            if (dot < 0 || dot != name.Length - 2)
            {
                return -1;
            }
            int axis = name[dot + 1] - 'x';
            if (axis < 0 || axis > 2)
            {
                return -1;
            }
            switch (name.Substring(0, dot))
            {
                case "position": return AnarkBsg.Node.Position + axis;
                case "rotation": return AnarkBsg.Node.Rotation + axis;
                case "scale": return AnarkBsg.Node.Scale + axis;
                case "pivot": return AnarkBsg.Node.Pivot + axis;
            }
            return -1;
        }

        private static bool IsSpatial(Node node)
        {
            AnarkBgf.ElementType t = node.Element.Type;
            return t == AnarkBgf.ElementType.Node || t == AnarkBgf.ElementType.Text || t == AnarkBgf.ElementType.Component;
        }

        // Where a transform property (position/rotation/scale/pivot .x/.y/.z, opacity) of a node lives: SourceGraph,
        // SourceBase, or null when it cannot be edited without adding a property.
        public string TransformSource(Node node, string name)
        {
            if (!IsSpatial(node) || BaseIndex(name) < 0)
            {
                return null;
            }
            AnarkBgf.Property p = node.Element.Find(name);
            if (p != null)
            {
                return (p.Kind == AnarkBgf.ValueKind.Float) ? SourceGraph : null;
            }
            return (node.Base != null) ? SourceBase : null;
        }

        // Value as the dash shows it: graph units (radians, opacity 0..100).
        public float GetTransform(Node node, string name)
        {
            int k = BaseIndex(name);
            if (k < 0)
            {
                throw new ArgumentException("not a transform property: " + name);
            }
            float[] v = (node.Base != null) ? node.Base.Values : DefaultValues;
            float fallback = (k == AnarkBsg.Node.Opacity) ? v[k] * 100 : v[k];
            return node.Element.GetFloat(name, fallback);
        }

        public bool SetTransform(Node node, string name, float value)
        {
            if (!this.WriteTransform(node, name, value))
            {
                return false;
            }
            this.Update();
            return true;
        }

        // The write alone, for edits that change several values before one Update.
        private bool WriteTransform(Node node, string name, float value)
        {
            string source = this.TransformSource(node, name);
            if (source == SourceGraph)
            {
                node.Element.Find(name).Float = value;
                this.BgfChanged = true;
            }
            else if (source == SourceBase)
            {
                int k = BaseIndex(name);
                node.Base.Values[k] = (k == AnarkBsg.Node.Opacity) ? value / 100 : value;
                this.BsgChanged = true;
            }
            else
            {
                return false;
            }
            return true;
        }

        // ---- Mouse editing (move and resize on the preview) ----

        // A drag in progress. Every Apply works from the values the drag started with, so nothing drifts.
        public class Drag
        {
            internal Node Node;
            internal bool Resize;
            internal float Hx, Hy;                  // handle position in the mesh rectangle: 0, 0.5 or 1
            internal PointF StartParent;            // cursor in the parent's space when the drag began
            internal float PosX, PosY, ScaleX, ScaleY, Rot, PivotX, PivotY;
            internal RectangleF Local;
            internal PointF Fixed;                  // resize: the opposite handle in the parent's space
            public string What { get { return this.Resize ? "size" : "position"; } }
        }

        // Why the last BeginMove / BeginResize returned null.
        public string Blocked;

        // Cursor (world) to the space of the node's parent, where position and pivot live.
        private static PointF ToParent(Node node, PointF world)
        {
            Node p = node.Parent;
            if (p == null)
            {
                return world;
            }
            float det = p.A * p.D - p.B * p.C;
            if (Math.Abs(det) < 1e-9f)
            {
                return PointF.Empty;
            }
            float dx = world.X - p.X, dy = world.Y - p.Y;
            return new PointF((p.D * dx - p.C * dy) / det, (-p.B * dx + p.A * dy) / det);
        }

        public Drag BeginMove(Node node, PointF world)
        {
            this.Blocked = null;
            if (!IsSpatial(node))
            {
                return null;
            }
            if (this.TransformSource(node, "position.x") == null || this.TransformSource(node, "position.y") == null)
            {
                this.Blocked = node.Name + " has no editable position";
                return null;
            }
            Drag d = new Drag();
            d.Node = node;
            d.StartParent = ToParent(node, world);
            d.PosX = this.GetTransform(node, "position.x");
            d.PosY = this.GetTransform(node, "position.y");
            return d;
        }

        // Resize by a handle of the mesh rectangle: scale x / y and position change so that the opposite handle stays.
        public Drag BeginResize(Node node, float hx, float hy)
        {
            this.Blocked = null;
            Item item = this.ItemOf(node);
            if (item == null || item.Kind != ItemKind.Rectangle)
            {
                return null;
            }
            string[] needed = new string[] { "position.x", "position.y", "scale.x", "scale.y" };
            foreach (string name in needed)
            {
                if (this.TransformSource(node, name) == null)
                {
                    this.Blocked = node.Name + " has no editable " + name;
                    return null;
                }
            }
            if (Math.Abs(this.GetTransform(node, "rotation.x")) > 1e-4f || Math.Abs(this.GetTransform(node, "rotation.y")) > 1e-4f)
            {
                this.Blocked = node.Name + " is turned around X or Y, set its scale in the property list";
                return null;
            }
            Drag d = new Drag();
            d.Node = node;
            d.Resize = true;
            d.Hx = hx;
            d.Hy = hy;
            d.Local = item.LocalRect;
            d.PosX = this.GetTransform(node, "position.x");
            d.PosY = this.GetTransform(node, "position.y");
            d.ScaleX = this.GetTransform(node, "scale.x");
            d.ScaleY = this.GetTransform(node, "scale.y");
            d.Rot = this.GetTransform(node, "rotation.z");
            d.PivotX = this.GetTransform(node, "pivot.x");
            d.PivotY = this.GetTransform(node, "pivot.y");
            PointF fixedLocal = new PointF(d.Local.Left + (1 - hx) * d.Local.Width, d.Local.Top + (1 - hy) * d.Local.Height);
            d.Fixed = ParentPoint(d, d.ScaleX, d.ScaleY, fixedLocal);
            return d;
        }

        // A point of the node's own space in the parent's space: position + R * S * (q - pivot).
        private static PointF ParentPoint(Drag d, float sx, float sy, PointF q)
        {
            double cos = Math.Cos(d.Rot), sin = Math.Sin(d.Rot);
            float ux = (q.X - d.PivotX) * sx, uy = (q.Y - d.PivotY) * sy;
            return new PointF((float) (d.PosX + cos * ux + sin * uy), (float) (d.PosY - sin * ux + cos * uy));
        }

        // ---- Duplicate, add, delete ----

        // The nearest component (the scene or a gauge) at or above a node: the owner of its slide entries.
        public static Node OwnerOf(Node node)
        {
            for (Node n = node; n != null; n = n.Parent)
            {
                if (n.Element.Type == AnarkBgf.ElementType.Component)
                {
                    return n;
                }
            }
            return null;
        }

        // Where new elements go. All 431 dashes of the game list their elements depth first, and the slides, animation
        // tracks and behaviours of the .bgf count elements by index, so nothing is inserted in the middle: new elements are
        // appended as children of a group on the path to the last element (the shallowest one that belongs to the scene
        // itself: the backdrop of the dash; else the layer), which keeps the order depth first. Each is listed in the
        // slides of the component that owns that group (the scene). Null when the dash has neither.
        public Node EndParent()
        {
            List<Node> path = new List<Node>();
            for (Node n = this.Nodes[this.Nodes.Count - 1]; n != null; n = n.Parent)
            {
                path.Insert(0, n);
            }
            foreach (Node n in path)
            {
                if (n.Record != null && n.Record.Type == AnarkFbf.RecordType.Group && n.Element.Type == AnarkBgf.ElementType.Node && OwnerOf(n) == this.Root)
                {
                    return n;
                }
            }
            foreach (Node n in path)
            {
                if (n.Record != null && n.Record.Type == AnarkFbf.RecordType.Layer && OwnerOf(n) == this.Root)
                {
                    return n;                       // no backdrop group at the end: the layer itself
                }
            }
            return null;
        }

        // The layer on the path to the last element: gauges (components) are its children.
        public Node EndLayer()
        {
            for (Node n = this.Nodes[this.Nodes.Count - 1]; n != null; n = n.Parent)
            {
                if (n.Record != null && n.Record.Type == AnarkFbf.RecordType.Layer && OwnerOf(n) == this.Root)
                {
                    return n;
                }
            }
            return null;
        }

        // Why a gauge cannot be copied, or null. A gauge is a component with its elements and its own slides; one with a
        // behaviour (bars: PROGRESS_BAR), animation tracks or events is tied to indexes the copy would not have.
        public string WhyNotCopyableGauge(Node gauge)
        {
            if (this.Bgf.Slides == null)
            {
                return "the slides of this dash are not understood";
            }
            List<int> subtree = this.Subtree(gauge);
            foreach (int i in subtree)
            {
                if (this.Nodes[i].Element.Type == AnarkBgf.ElementType.Behaviour)
                {
                    return gauge.Name + " has a behaviour (a bar or an animation): it cannot be copied";
                }
            }
            if (this.Bgf.IndexMentioned(gauge.Index, gauge.Index + subtree.Count))
            {
                return gauge.Name + " is used by an animation or an event of the game: it cannot be copied";
            }
            List<AnarkBgf.Slide> slides = this.Bgf.SlidesOf(gauge.Index);
            if (slides.Count == 0)
            {
                return gauge.Name + " has no slides";
            }
            foreach (AnarkBgf.Slide slide in slides)
            {
                if (slide.Entries.Count != subtree.Count)
                {
                    return gauge.Name + " has slides that do not list all its elements: it cannot be copied";
                }
                foreach (AnarkBgf.SlideEntry e in slide.Entries)
                {
                    if (e.ItemCount > 0 || e.TrackCount > 0)
                    {
                        return gauge.Name + " uses animation tracks in its slides: it cannot be copied";
                    }
                }
            }
            return null;
        }

        // The gauges of this dash that can be copied (the library of this dash): Gauge_Gear, Gauge_Clock...
        public List<Node> GaugeTemplates()
        {
            List<Node> list = new List<Node>();
            foreach (Node n in this.Nodes)
            {
                if (n.Element.Type == AnarkBgf.ElementType.Component && n != this.Root && n.Record != null && this.WhyNotCopyableGauge(n) == null)
                {
                    list.Add(n);
                }
            }
            return list;
        }

        // The first text inside a node.
        public Node FirstText(Node root)
        {
            foreach (int i in this.Subtree(root))
            {
                if (this.Nodes[i].Record != null && this.Nodes[i].Record.Type == AnarkFbf.RecordType.Text)
                {
                    return this.Nodes[i];
                }
            }
            return null;
        }

        // After Import: every data channel the other dash pointed at one of the copied elements is pointed at its copy here
        // (rpm, speed, gear, lap time...), unless this dash already feeds that value to something (it keeps it).
        // Returns one line per channel for the status.
        public List<string> BindImported(AnarkDash src, Dictionary<int, int> map)
        {
            List<string> lines = new List<string>();
            foreach (Node b in src.Nodes)
            {
                if (b.Element.Type != AnarkBgf.ElementType.Behaviour || b.Element.Parent != src.Root.Index)
                {
                    continue;
                }
                foreach (AnarkBgf.Property p in b.Element.Properties)
                {
                    if (p.Kind != AnarkBgf.ValueKind.String || !p.IsBehaviourParameter)
                    {
                        continue;
                    }
                    string target = src.Bgf.GetString(p);
                    foreach (KeyValuePair<int, int> pair in map)
                    {
                        if (src.Nodes[pair.Key].Record == null || src.PathOf(src.Nodes[pair.Key]) != target)
                        {
                            continue;
                        }
                        bool done;
                        string label = p.Name ?? p.NameHash.ToString("X7");
                        string before = this.BindChannel((p.Hash & 0x07FFFFFF).ToString("X8"), this.Nodes[pair.Value], out done, true);
                        lines.Add(done ? label + " -> " + this.Nodes[pair.Value].Name
                            : (before == null) ? label + ": this dash has no such channel" : label + ": kept on " + before.Substring(before.LastIndexOf('.') + 1));
                    }
                }
            }
            return lines;
        }

        // Points a data channel (a "... target" parameter of the scene's contract) at an element and switches it on.
        // Returns the path it pointed at before, or null when there is no such parameter.
        public string BindChannel(string hash, Node target, out bool done)
        {
            return this.BindChannel(hash, target, out done, false);
        }

        public string BindChannel(string hash, Node target, out bool done, bool onlyIfFree)
        {
            done = false;
            uint h = Convert.ToUInt32(hash, 16);
            foreach (Node n in this.Nodes)
            {
                if (n.Element.Type == AnarkBgf.ElementType.Behaviour && n.Element.Parent == this.Root.Index)
                {
                    foreach (AnarkBgf.Property p in n.Element.Properties)
                    {
                        if ((p.Hash & 0x07FFFFFF) == h && p.Kind == AnarkBgf.ValueKind.String)
                        {
                            string before = this.Bgf.GetString(p);
                            if (onlyIfFree && before != null && before.StartsWith("parent."))
                            {
                                return before;
                            }
                            done = this.SetTarget(n, p, this.PathOf(target));
                            return before;
                        }
                    }
                }
            }
            return null;
        }

        private static bool SameLinear(Node p, Node q)
        {
            return Math.Abs(p.A - q.A) < 1e-4f && Math.Abs(p.B - q.B) < 1e-4f && Math.Abs(p.C - q.C) < 1e-4f && Math.Abs(p.D - q.D) < 1e-4f;
        }

        // The indexes of a node and everything under it (depth first order: they follow each other).
        public List<int> Subtree(Node root)
        {
            List<Node> elements = this.Nodes;
            bool[] inside = new bool[elements.Count];
            inside[root.Index] = true;
            List<int> list = new List<int>();
            list.Add(root.Index);
            for (int i = root.Index + 1; i < elements.Count; i++)
            {
                int p = elements[i].Element.Parent;
                if (p >= 0 && p < inside.Length && inside[p])
                {
                    inside[i] = true;
                    list.Add(i);
                }
            }
            return list;
        }

        // Copies a node (a model, a text or a group) with everything under it:
        //   .fbf  the records, with new ids, right after the original's record (groups after their children);
        //   .bgf  the elements and their properties, INSERTED right after the original under the same parent (the whole
        //         tail is renumbered: AnarkBgf.InsertElements; gauges and imports are appended, see EndParent), strings get
        //         their own copy, the header counts are updated, every new element is listed in the slides of its component
        //         with the values the slides give the original, and its animation tracks are copied (AnarkBgf.CopyTracks);
        //   .bsg  the base nodes, appended, with new ids; materials and images also go in its table of them.
        // The meshes and textures are shared. The copy keeps its place on the screen. name = the copy's name (the .bgf
        // element hash is built from it); sameNames keeps every name as it is, so whatever the game does by name may
        // act on the copy too. Returns the new node, or null (Blocked says why). The node tree is rebuilt: older Node
        // objects are stale.
        // Model / material / mesh links the game needs (see AnarkFbf.MaterialModelOffset): every material names its model
        // (its parent element), every model has its own mesh in record order. Copies made before 2026-11-07 broke them
        // (copied materials named the original model, copied models shared its mesh) and FM4 crashed loading the car.
        // Also the owner written in each slide header (AnarkBgf.Slide.OwnerMatchesEntries). Returns how many links are
        // wrong; 0 on every game file (431/431).
        public int LinkProblems()
        {
            return this.ModelLinkProblems() + this.Bgf.SlideOwnerProblems();
        }

        private int ModelLinkProblems()
        {
            int wrong = 0;
            int mesh = 0;
            foreach (AnarkFbf.Record r in this.Fbf.Records)
            {
                if (r.Type == AnarkFbf.RecordType.Model && AnarkFbf.MeshOf(r) != mesh++)
                {
                    wrong++;
                }
            }
            if (mesh != this.Fbf.Meshes.Count)
            {
                wrong++;
            }
            foreach (AnarkBgf.Element e in this.Bgf.Elements)
            {
                AnarkFbf.Record r = (e.Type == AnarkBgf.ElementType.Material && e.Parent >= 0) ? this.Fbf.Find(e.Id) : null;
                if (r != null && r.Type == AnarkFbf.RecordType.Material && AnarkFbf.ModelOf(r) != this.Bgf.Elements[e.Parent].Id)
                {
                    wrong++;
                }
            }
            return wrong;
        }

        // Puts the links back: each material names its parent element, each model gets its own mesh in record order
        // (a shared mesh is copied), each slide names its owner. Returns the number of wrong links found (0 = nothing changed).
        public int RepairLinks()
        {
            int slides = this.Bgf.SlideOwnerProblems();
            if (slides > 0)
            {
                foreach (AnarkBgf.Slide slide in this.Bgf.Slides)
                {
                    slide.SetOwnerFromEntries();
                }
                this.BgfChanged = true;
            }
            int wrong = this.ModelLinkProblems();
            if (wrong == 0)
            {
                return slides;
            }
            foreach (AnarkBgf.Element e in this.Bgf.Elements)
            {
                AnarkFbf.Record r = (e.Type == AnarkBgf.ElementType.Material && e.Parent >= 0) ? this.Fbf.Find(e.Id) : null;
                if (r != null && r.Type == AnarkFbf.RecordType.Material)
                {
                    AnarkFbf.SetModelOf(r, this.Bgf.Elements[e.Parent].Id);
                }
            }
            this.Fbf.RepackMeshes();
            this.FbfChanged = true;
            this.Build();
            return wrong + slides;
        }

        public Node Duplicate(Node root, string name, bool sameNames)
        {
            return this.Import(this, root, name, sameNames);
        }

        // Old index in the other dash -> index of the copy in this one, of the last Import.
        public Dictionary<int, int> LastImport = new Dictionary<int, int>();

        // True when the node, or anything under it, is an image or a material (it needs a texture the car zip may lack).
        private static bool UsesImage(AnarkDash dash, Node node)
        {
            foreach (int i in dash.Subtree(node))
            {
                AnarkFbf.Record r = dash.Nodes[i].Record;
                if (r != null && (r.Type == AnarkFbf.RecordType.Image || r.Type == AnarkFbf.RecordType.Material))
                {
                    return true;
                }
            }
            return false;
        }

        private bool NameTaken(string name)
        {
            foreach (AnarkFbf.Record r in this.Fbf.Records)
            {
                if (r.Name == name)
                {
                    return true;
                }
            }
            return false;
        }

        // Duplicate (src == this) or Import: the same copy, but the node comes from ANOTHER dash (src). A group or a text
        // only, never a gauge; whatever under it uses an image or a material is left out (the texture is in the other
        // car's zip). Names keep the source's (a number is added when this dash already has one: name == null).
        public Node Import(AnarkDash src, Node root, string name, bool sameNames)
        {
            bool cross = src != this;
            this.Blocked = null;
            if (cross)
            {
                sameNames = true;
                if (root.Element.Type == AnarkBgf.ElementType.Component)
                {
                    this.Blocked = "a gauge of another dash cannot be imported (its behaviour and animations are tied to that dash)";
                    return null;
                }
            }
            AnarkBgf.ElementType type = root.Element.Type;
            bool isComponent = type == AnarkBgf.ElementType.Component;
            if (root.Record == null || (type != AnarkBgf.ElementType.Node && type != AnarkBgf.ElementType.Text && !isComponent))
            {
                this.Blocked = root.Name + " cannot be duplicated alone (select a model, a text, a group or a gauge)";
                return null;
            }
            if (isComponent && root == this.Root)
            {
                this.Blocked = "the scene cannot be duplicated";
                return null;
            }
            if (string.IsNullOrEmpty(name) || !IsLatin1(name))
            {
                this.Blocked = "the name of the copy must be plain text";
                return null;
            }
            if (this.Bgf.Slides == null)
            {
                this.Blocked = "the slides of this dash are not understood, nothing can be added to it";
                return null;
            }
            // A copy of a model, text or group of this dash goes right after the original, under the same parent: a real
            // insertion, every index of the .bgf tail is renumbered (2026-11-05). Gauges and imports still go at the end.
            bool inPlace = !cross && !isComponent && root.Parent != null && this.Bgf.TailEditable;
            Node parent = inPlace ? root.Parent : isComponent ? this.EndLayer() : this.EndParent();
            if (parent == null)
            {
                this.Blocked = "this dash has no " + (isComponent ? "layer" : "group at its end") + " to put a copy in";
                return null;
            }
            Node owner = OwnerOf(parent);
            if (!inPlace && !cross && root.Parent != null && !SameLinear(root.Parent, parent))
            {
                this.Blocked = root.Name + " sits in a part of the dash that is scaled or turned differently from where copies go (" + parent.Name + "): the copy would change size";
                return null;
            }

            List<AnarkBgf.Element> elements = this.Bgf.Elements;
            List<AnarkBgf.Element> srcEl = src.Bgf.Elements;
            bool[] inside = new bool[srcEl.Count];
            int skipped = 0;
            foreach (int i in src.Subtree(root))
            {
                AnarkBgf.ElementType t = srcEl[i].Type;
                if ((t == AnarkBgf.ElementType.Component && i != root.Index) || t == AnarkBgf.ElementType.Behaviour)
                {
                    this.Blocked = root.Name + " contains a behaviour or another gauge of the game: copy its parts instead";
                    return null;
                }
                if (cross && i != root.Index && (!inside[srcEl[i].Parent] || UsesImage(src, src.Nodes[i])))
                {
                    skipped++;
                    continue;
                }
                inside[i] = true;
            }
            if (cross && UsesImage(src, root) && src.FirstText(root) == null)
            {
                this.Blocked = root.Name + " is a picture (needs a texture of the other car)";
                return null;
            }
            if (cross && (src.Bsg == null) != (this.Bsg == null))
            {
                this.Blocked = "one of the two dashes has no .bsg";
                return null;
            }
            if (isComponent)
            {
                // a gauge with animations, behaviours or events cannot be copied: they count elements by index
                string why = this.WhyNotCopyableGauge(root);
                if (why != null)
                {
                    this.Blocked = why;
                    return null;
                }
            }

            // where the copy stays: the original's place on the screen
            PointF place = (!cross && root.Parent != null) ? root.Parent.Transform(this.GetTransform(root, "position.x"), this.GetTransform(root, "position.y")) : PointF.Empty;
            float placeZ = root.Z;

            uint nextId = 0;
            foreach (AnarkFbf.Record r in this.Fbf.Records)
            {
                nextId = Math.Max(nextId, r.Id);
            }
            foreach (AnarkBgf.Element e in elements)
            {
                nextId = Math.Max(nextId, e.Id);
            }
            if (this.Bsg != null)
            {
                foreach (AnarkBsg.Node n in this.Bsg.Nodes)
                {
                    nextId = Math.Max(nextId, n.Id);
                }
            }

            string suffix = sameNames ? "" : "_" + name;
            Dictionary<int, int> newIndex = new Dictionary<int, int>();
            Dictionary<uint, uint> newId = new Dictionary<uint, uint>();
            List<KeyValuePair<int, AnarkFbf.Record>> records = new List<KeyValuePair<int, AnarkFbf.Record>>();
            int originalCount = srcEl.Count;
            int at = inPlace ? root.Index + this.Subtree(root).Count : elements.Count;
            List<AnarkBgf.Element> added = new List<AnarkBgf.Element>();
            List<string> used = new List<string>();
            bool meshesChanged = false;
            for (int i = 0; i < originalCount; i++)
            {
                if (!inside[i])
                {
                    continue;
                }
                AnarkBgf.Element old = srcEl[i];
                AnarkBgf.Element copy = old.Copy();
                newIndex[i] = at + added.Count;
                copy.Parent = (i == root.Index) ? parent.Index : newIndex[old.Parent];
                copy.Id = ++nextId;
                newId[old.Id] = copy.Id;
                AnarkFbf.Record rec = src.Fbf.Find(old.Id);
                if (rec != null)
                {
                    AnarkFbf.Record recCopy = rec.Copy();
                    recCopy.Id = copy.Id;
                    recCopy.Name = sameNames ? rec.Name : (i == root.Index) ? name : rec.Name + suffix;
                    if (cross)
                    {
                        // the names of the other dash, a number after them when this dash (or this import) has one already
                        string unique = rec.Name;
                        for (int n = 2; this.NameTaken(unique) || used.Contains(unique); n++)
                        {
                            unique = rec.Name + "_" + n;
                        }
                        used.Add(unique);
                        recCopy.Name = unique;
                    }
                    uint owned;
                    if (recCopy.Type == AnarkFbf.RecordType.Image && newId.TryGetValue(recCopy.Owner, out owned))
                    {
                        recCopy.Owner = owned;
                    }
                    // a copied model gets its own mesh and its copied materials name it (the game reads a model's
                    // materials through them: left on the original, FM4 crashed loading the Brera, 2026-11-07)
                    if (recCopy.Type == AnarkFbf.RecordType.Material && newId.TryGetValue(AnarkFbf.ModelOf(recCopy), out owned))
                    {
                        AnarkFbf.SetModelOf(recCopy, owned);
                    }
                    if (recCopy.Type == AnarkFbf.RecordType.Model)
                    {
                        AnarkFbf.WriteU32(recCopy.Body, 0, (uint) this.Fbf.CloneMesh(src.Fbf, AnarkFbf.MeshOf(rec)));
                        meshesChanged = true;
                    }
                    // An element's hash is the full sdbm hash of its name for 80% of the elements of the FM4 dashes (12368 of
                    // 15609; images, some groups and models have a hash from another name, the editor's): such a copy gets the
                    // hash of its own name, the others keep the original's (as with the same names). The old form (top 5 bits
                    // of the original + 27 bits of the new name) matched neither.
                    if (old.Hash == AnarkBgf.HashFull(rec.Name))
                    {
                        copy.Hash = AnarkBgf.HashFull(recCopy.Name);
                    }
                    records.Add(new KeyValuePair<int, AnarkFbf.Record>(src.Fbf.Records.IndexOf(rec), recCopy));
                }
                foreach (AnarkBgf.Property p in copy.Properties)
                {
                    if (p.Kind == AnarkBgf.ValueKind.String && p.Value < src.Bgf.Strings.Count)
                    {
                        string text = src.Bgf.Strings[(int) p.Value];
                        p.Value = (uint) this.Bgf.Strings.Count;
                        this.Bgf.Strings.Add(text);
                    }
                }
                added.Add(copy);
            }
            if (this.Bgf.TailEditable)
            {
                this.Bgf.InsertElements(at, added);
            }
            else
            {
                elements.AddRange(added);       // at == the end: nothing to renumber
            }
            // owner, the originals and the parent sit before 'at': their indexes did not move
            for (int i = 0; i < originalCount; i++)
            {
                if (inside[i] && (!isComponent || i == root.Index))
                {
                    this.Bgf.AddToSlides(owner.Index, newIndex[i], cross ? -1 : i);
                    if (!cross)
                    {
                        this.Bgf.CopyTracks(i, newIndex[i]);
                    }
                }
            }
            this.LastImport = newIndex;
            if (isComponent)
            {
                // the gauge's own slides: the same entries for the new elements
                foreach (AnarkBgf.Slide slide in this.Bgf.SlidesOf(root.Index))
                {
                    AnarkBgf.Slide copySlide = new AnarkBgf.Slide();
                    Array.Copy(slide.Name, copySlide.Name, slide.Name.Length);
                    copySlide.Duration = slide.Duration;
                    foreach (AnarkBgf.SlideEntry entry in slide.Entries)
                    {
                        int mapped;
                        if (newIndex.TryGetValue((int) entry.Index, out mapped))
                        {
                            AnarkBgf.SlideEntry e2 = new AnarkBgf.SlideEntry();
                            e2.Index = (uint) mapped;
                            e2.Flag = entry.Flag;
                            e2.Data = (byte[]) entry.Data.Clone();
                            copySlide.Entries.Add(e2);
                        }
                    }
                    this.Bgf.Slides.Add(copySlide);
                }
            }
            if (records.Count > 0)
            {
                // before the record of the group they go in, in the order the originals have (groups after their children)
                records.Sort(delegate(KeyValuePair<int, AnarkFbf.Record> p, KeyValuePair<int, AnarkFbf.Record> q) { return p.Key.CompareTo(q.Key); });
                List<AnarkFbf.Record> ordered = new List<AnarkFbf.Record>();
                foreach (KeyValuePair<int, AnarkFbf.Record> pair in records)
                {
                    ordered.Add(pair.Value);
                }
                // in place: right after the original's record (it follows its children, the parent's record comes later)
                int recordAt = inPlace ? this.Fbf.Records.IndexOf(root.Record) + 1
                    : this.Fbf.Records.IndexOf((parent.Record.Type == AnarkFbf.RecordType.Group) ? parent.Record : this.Root.Record);
                this.Fbf.Records.InsertRange((recordAt >= 0) ? recordAt : this.Fbf.Records.Count, ordered);
                if (meshesChanged)
                {
                    this.Fbf.RepackMeshes();
                }
            }

            if (this.Bsg != null)
            {
                // base nodes, appended; the parent of a copy is the base node of the copy of its parent, and for the
                // first copy that of the group it goes in (or of the nearest ancestor that has one)
                Dictionary<uint, AnarkBsg.Node> copies = new Dictionary<uint, AnarkBsg.Node>();
                for (int i = 0; i < originalCount; i++)
                {
                    if (!inside[i])
                    {
                        continue;
                    }
                    uint id = srcEl[i].Id;
                    AnarkFbf.Record oldRecord = src.Fbf.Find(id);
                    if (oldRecord != null && (oldRecord.Type == AnarkFbf.RecordType.Material || oldRecord.Type == AnarkFbf.RecordType.Image))
                    {
                        this.Bsg.AddToTable(newId[id], (byte) (oldRecord.Type == AnarkFbf.RecordType.Material ? 6 : 7));
                        continue;
                    }
                    AnarkBsg.Node b = src.Bsg.Find(id);
                    if (b == null)
                    {
                        continue;
                    }
                    AnarkBsg.Node bc = b.Copy();
                    bc.Id = newId[b.Id];
                    AnarkBsg.Node bparent = null;
                    if (i != root.Index)
                    {
                        for (int p = srcEl[i].Parent; p >= 0 && bparent == null; p = srcEl[p].Parent)
                        {
                            AnarkBsg.Node pb = src.Bsg.Find(srcEl[p].Id);
                            if (pb != null)
                            {
                                copies.TryGetValue(pb.Id, out bparent);
                            }
                        }
                    }
                    else
                    {
                        for (int p = parent.Index; p >= 0 && bparent == null; p = this.Nodes[p].Element.Parent)
                        {
                            bparent = this.Bsg.Find(elements[p].Id);
                        }
                    }
                    bc.Parent = (bparent != null) ? this.Bsg.Nodes.IndexOf(bparent) : -1;
                    copies[b.Id] = bc;
                    this.Bsg.Nodes.Add(bc);
                }
            }

            this.Bgf.UpdateHeader();
            this.FbfChanged = this.BgfChanged = true;
            this.BsgChanged = this.BsgChanged || this.Bsg != null;
            this.Build();
            Node result = this.Nodes[newIndex[root.Index]];
            if (!cross)
            {
                this.PlaceAt(result, place, placeZ);
            }
            return result;
        }

        // Moves a node so that its position point is at a point of the screen (world) and its depth is z; false when
        // it has no editable position.
        public bool PlaceAt(Node node, PointF world, float z)
        {
            if (!IsSpatial(node) || this.TransformSource(node, "position.x") == null || this.TransformSource(node, "position.y") == null)
            {
                return false;
            }
            PointF local = ToParent(node, world);
            this.WriteTransform(node, "position.x", local.X);
            this.WriteTransform(node, "position.y", local.Y);
            if (node.Parent != null)
            {
                this.WriteTransform(node, "position.z", z - node.Parent.Z);
            }
            this.Update();
            return true;
        }

        // ---- Add (a copy of something this dash already has) ----

        // The element to copy for a new "text", "rectangle" (one flat colour) or "image" (a texture): the first the dash
        // has of that kind. Null when it has none.
        public Node Template(string kind)
        {
            foreach (Node n in this.Nodes)
            {
                if (n.Record == null)
                {
                    continue;
                }
                if (kind == "text")
                {
                    if (n.Record.Type == AnarkFbf.RecordType.Text && n.Element.Find("textstring") != null && n.Element.Find("font") != null
                        && n.Element.Find("size") != null && this.ColorSource(n) != null)
                    {
                        return n;
                    }
                    continue;
                }
                if (n.Record.Type != AnarkFbf.RecordType.Model || n.Children.Count != 1)
                {
                    continue;
                }
                Node material = MaterialOf(n);
                if (material == null || this.ColorSource(n) == null)
                {
                    continue;
                }
                Node image = ImageOf(material);
                if (kind == "rectangle" && image == null && material.Children.Count == 0)
                {
                    return n;
                }
                if (kind == "image" && image != null && material.Children.Count == 1)
                {
                    return n;
                }
            }
            return null;
        }

        // ---- Delete ----

        // Why a node cannot be removed for good, or null when it can. The game counts elements by index, so only the
        // last elements of the dash can go (the ones added, in practice); anything else can be hidden.
        public string WhyNotRemovable(Node node)
        {
            if (node.Element.Type == AnarkBgf.ElementType.Behaviour || node.Parent == null)
            {
                return node.Name + " is a behaviour or the scene of the game";
            }
            if (this.Bgf.Slides == null)
            {
                return "the slides of this dash are not understood";
            }
            List<int> subtree = this.Subtree(node);
            if (this.Bgf.TailEditable)
            {
                // every reference of the tail is known (2026-11-05): anything goes, with its behaviours, animation tracks,
                // events and slides; the rest is renumbered
                return null;
            }
            foreach (int i in subtree)
            {
                if ((this.Nodes[i].Element.Type == AnarkBgf.ElementType.Component && i != node.Index) || this.Nodes[i].Element.Type == AnarkBgf.ElementType.Behaviour)
                {
                    return node.Name + " holds a behaviour or another gauge of the game";
                }
            }
            if (node.Index + subtree.Count != this.Nodes.Count)
            {
                return node.Name + " is not one of the last elements of the dash, and the game numbers the elements (it can be hidden instead)";
            }
            if (this.Bgf.IndexMentioned(node.Index))
            {
                return node.Name + " may be used by an animation or a behaviour of the game (it can be hidden instead)";
            }
            return null;
        }

        // Removes a node with everything under it (see WhyNotRemovable). The node tree is rebuilt.
        public bool Remove(Node node)
        {
            this.Blocked = this.WhyNotRemovable(node);
            if (this.Blocked != null)
            {
                return false;
            }
            int first = node.Index;
            // data channels that point inside what goes are set free
            List<string> gone = new List<string>();
            foreach (int i in this.Subtree(node))
            {
                gone.Add(this.PathOf(this.Nodes[i]));
            }
            foreach (Node b in this.Nodes)
            {
                if (b.Element.Type == AnarkBgf.ElementType.Behaviour && b.Element.Parent == this.Root.Index)
                {
                    foreach (AnarkBgf.Property p in b.Element.Properties)
                    {
                        string target = (p.Kind == AnarkBgf.ValueKind.String && p.IsBehaviourParameter) ? this.Bgf.GetString(p) : null;
                        if (target != null && gone.Contains(target))
                        {
                            this.SetTarget(b, p, "Scene.UIContract");
                        }
                    }
                }
            }
            List<AnarkBgf.Element> elements = this.Bgf.Elements;
            int count = this.Subtree(node).Count;
            Dictionary<uint, bool> ids = new Dictionary<uint, bool>();
            List<int> strings = new List<int>();
            for (int i = first; i < first + count; i++)
            {
                if (elements[i].Type != AnarkBgf.ElementType.Behaviour)
                {
                    ids[elements[i].Id] = true;     // behaviours have id 0 and nothing in the .fbf / .bsg
                }
                foreach (AnarkBgf.Property p in elements[i].Properties)
                {
                    if (p.Kind == AnarkBgf.ValueKind.String && p.Value < this.Bgf.Strings.Count)
                    {
                        strings.Add((int) p.Value);
                    }
                }
            }
            bool modelGone = this.Fbf.Records.Exists(delegate(AnarkFbf.Record r) { return ids.ContainsKey(r.Id) && r.Type == AnarkFbf.RecordType.Model; });
            this.Fbf.Records.RemoveAll(delegate(AnarkFbf.Record r) { return ids.ContainsKey(r.Id); });
            if (modelGone)
            {
                this.Fbf.RepackMeshes();    // its mesh goes too, the next models' meshes move up (one mesh per model, in order)
            }
            if (this.Bsg != null)
            {
                List<AnarkBsg.Node> old = new List<AnarkBsg.Node>(this.Bsg.Nodes);
                this.Bsg.Nodes.RemoveAll(delegate(AnarkBsg.Node b) { return ids.ContainsKey(b.Id); });
                foreach (AnarkBsg.Node b in this.Bsg.Nodes)
                {
                    if (b.Parent >= 0 && b.Parent < old.Count)
                    {
                        b.Parent = this.Bsg.Nodes.IndexOf(old[b.Parent]);
                    }
                }
                foreach (uint id in ids.Keys)
                {
                    this.Bsg.RemoveFromTable(id);
                }
                this.BsgChanged = true;
            }
            if (this.Bgf.TailEditable)
            {
                this.Bgf.RemoveElements(first, count);
            }
            else
            {
                elements.RemoveRange(first, elements.Count - first);
                this.Bgf.RemoveFromSlides(first);
            }
            // only the last strings can go (the others keep their numbers), and only when nothing left uses them
            Dictionary<int, bool> usedStrings = new Dictionary<int, bool>();
            foreach (AnarkBgf.Element e in elements)
            {
                foreach (AnarkBgf.Property p in e.Properties)
                {
                    if (p.Kind == AnarkBgf.ValueKind.String)
                    {
                        usedStrings[(int) p.Value] = true;
                    }
                }
            }
            if (this.Bgf.Declared != null)
            {
                foreach (AnarkBgf.DeclaredEvents de in this.Bgf.Declared)
                {
                    foreach (int s in de.Strings)
                    {
                        usedStrings[s] = true;
                    }
                }
            }
            int last = this.Bgf.Strings.Count - 1;
            while (last >= 0 && strings.Contains(last) && !usedStrings.ContainsKey(last))
            {
                this.Bgf.Strings.RemoveAt(last);
                last--;
            }
            this.Bgf.UpdateHeader();
            this.FbfChanged = this.BgfChanged = true;
            this.Build();
            return true;
        }


        public bool MoveBy(Node node, float dx, float dy)
        {
            Drag d = this.BeginMove(node, PointF.Empty);
            if (d == null)
            {
                return false;
            }
            this.ApplyDrag(d, new PointF(dx, dy), false);
            return true;
        }

        // Moves a node by (dx, dy) in its parent's space.
        public bool Nudge(Node node, float dx, float dy)
        {
            if (!IsSpatial(node) || this.TransformSource(node, "position.x") == null || this.TransformSource(node, "position.y") == null)
            {
                return false;
            }
            this.WriteTransform(node, "position.x", this.GetTransform(node, "position.x") + dx);
            this.WriteTransform(node, "position.y", this.GetTransform(node, "position.y") + dy);
            this.Update();
            return true;
        }

        public Item ItemOf(Node node)
        {
            foreach (Item item in this.Items)
            {
                if (item.Node == node)
                {
                    return item;
                }
            }
            return null;
        }

        // Follows the cursor (world). keepRatio: corner handles keep the shape.
        public void ApplyDrag(Drag d, PointF world, bool keepRatio)
        {
            PointF t = ToParent(d.Node, world);
            if (!d.Resize)
            {
                this.WriteTransform(d.Node, "position.x", d.PosX + t.X - d.StartParent.X);
                this.WriteTransform(d.Node, "position.y", d.PosY + t.Y - d.StartParent.Y);
                this.Update();
                return;
            }
            double cos = Math.Cos(d.Rot), sin = Math.Sin(d.Rot);
            float dx = t.X - d.Fixed.X, dy = t.Y - d.Fixed.Y;
            // back into the node's axes (undo the rotation)
            float vx = (float) (cos * dx - sin * dy), vy = (float) (sin * dx + cos * dy);
            float sx = d.ScaleX, sy = d.ScaleY;
            float hw = (d.Hx - (1 - d.Hx)) * d.Local.Width, hh = (d.Hy - (1 - d.Hy)) * d.Local.Height;
            if (d.Hx != 0.5f && Math.Abs(hw) > 1e-6f)
            {
                sx = SafeScale(vx / hw);
            }
            if (d.Hy != 0.5f && Math.Abs(hh) > 1e-6f)
            {
                sy = SafeScale(vy / hh);
            }
            if (keepRatio && d.Hx != 0.5f && d.Hy != 0.5f)
            {
                float fx = sx / d.ScaleX, fy = sy / d.ScaleY;
                float f = (Math.Abs(fx - 1) > Math.Abs(fy - 1)) ? fx : fy;
                sx = SafeScale(d.ScaleX * f);
                sy = SafeScale(d.ScaleY * f);
            }
            PointF fixedLocal = new PointF(d.Local.Left + (1 - d.Hx) * d.Local.Width, d.Local.Top + (1 - d.Hy) * d.Local.Height);
            // position that keeps the fixed handle where it was: F - R * S' * (q - pivot)
            float ux = (fixedLocal.X - d.PivotX) * sx, uy = (fixedLocal.Y - d.PivotY) * sy;
            this.WriteTransform(d.Node, "scale.x", sx);
            this.WriteTransform(d.Node, "scale.y", sy);
            this.WriteTransform(d.Node, "position.x", (float) (d.Fixed.X - (cos * ux + sin * uy)));
            this.WriteTransform(d.Node, "position.y", (float) (d.Fixed.Y - (-sin * ux + cos * uy)));
            this.Update();
        }

        private static float SafeScale(float s)
        {
            if (float.IsNaN(s) || float.IsInfinity(s))
            {
                return 1;
            }
            return (Math.Abs(s) < 0.001f) ? (s < 0 ? -0.001f : 0.001f) : s;
        }

        // ---- Any other graph property ----

        public bool SetFloat(Node node, AnarkBgf.Property p, float value)
        {
            if (p.Kind != AnarkBgf.ValueKind.Float || float.IsNaN(value) || float.IsInfinity(value))
            {
                return false;
            }
            p.Float = value;
            this.BgfChanged = true;
            this.Update();
            return true;
        }

        // Int, bool (0 / 1) and enum-like properties: the raw value.
        public bool SetRaw(Node node, AnarkBgf.Property p, uint value)
        {
            if (p.Kind == AnarkBgf.ValueKind.Float || p.Kind == AnarkBgf.ValueKind.String)
            {
                return false;
            }
            p.Value = value;
            this.BgfChanged = true;
            this.Update();
            return true;
        }

        // A string property in place (one string belongs to one property only).
        public bool SetStringProperty(Node node, AnarkBgf.Property p, string value)
        {
            if (p.Kind != AnarkBgf.ValueKind.String || value == null || !IsLatin1(value) || p.Value >= this.Bgf.Strings.Count)
            {
                return false;
            }
            this.Bgf.Strings[(int) p.Value] = value;
            this.BgfChanged = true;
            this.Update();
            return true;
        }

        // The path the data contract uses for an element: "parent." + the names from the scene's child down to it.
        public string PathOf(Node node)
        {
            string path = node.Name;
            for (Node n = node.Parent; n != null && n != this.Root; n = n.Parent)
            {
                path = n.Name + "." + path;
            }
            return "parent." + path;
        }

        // The paths of the elements a contract parameter can point at (texts, models, groups, gauges).
        public List<string> Paths()
        {
            List<string> list = new List<string>();
            list.Add("Scene.UIContract");
            foreach (Node n in this.Nodes)
            {
                if (n != this.Root && n.Record != null && n.Record.Type != AnarkFbf.RecordType.Material && n.Record.Type != AnarkFbf.RecordType.Image
                    && n.Record.Type != AnarkFbf.RecordType.Light && n.Record.Type != AnarkFbf.RecordType.Camera)
                {
                    list.Add(this.PathOf(n));
                }
            }
            return list;
        }

        // A contract target: the path, and the "on" switch just before it follows (1 for a path, 0 for none).
        public bool SetTarget(Node node, AnarkBgf.Property p, string path)
        {
            if (!this.SetStringProperty(node, p, path))
            {
                return false;
            }
            int i = node.Element.Properties.IndexOf(p);
            if (i > 0)
            {
                AnarkBgf.Property on = node.Element.Properties[i - 1];
                if (on.IsBehaviourParameter && on.Kind == AnarkBgf.ValueKind.Bool)
                {
                    on.Value = (path.Length > 0 && path != "Scene.UIContract" && path != "this") ? 1u : 0u;
                }
            }
            return true;
        }

        // Colour of a text (graph textcolor) or of a model (its material: graph diffuse, else the .fbf material).
        public string ColorSource(Node node)
        {
            if (node.Record == null)
            {
                return null;
            }
            if (node.Record.Type == AnarkFbf.RecordType.Text)
            {
                foreach (string name in TextColorNames)
                {
                    AnarkBgf.Property p = node.Element.Find(name);
                    if (p == null || p.Kind != AnarkBgf.ValueKind.Float)
                    {
                        return null;
                    }
                }
                return SourceGraph;
            }
            if (node.Record.Type == AnarkFbf.RecordType.Model)
            {
                Node material = MaterialOf(node);
                if (material == null)
                {
                    return null;
                }
                bool graph = false, scene = false;
                foreach (string name in DiffuseNames)
                {
                    AnarkBgf.Property p = material.Element.Find(name);
                    if (p != null && p.Kind != AnarkBgf.ValueKind.Float)
                    {
                        return null;
                    }
                    graph |= p != null;
                    scene |= p == null;
                }
                return (graph && scene) ? SourceGraph + "+" + SourceScene : graph ? SourceGraph : SourceScene;
            }
            return null;
        }

        public Color GetColor(Node node)
        {
            if (node.Record != null && node.Record.Type == AnarkFbf.RecordType.Text)
            {
                AnarkBgf.Element e = node.Element;
                return Color.FromArgb(Clamp(e.GetFloat("textcolor.r", 255) / 255), Clamp(e.GetFloat("textcolor.g", 255) / 255), Clamp(e.GetFloat("textcolor.b", 255) / 255));
            }
            Node material = (node.Record != null && node.Record.Type == AnarkFbf.RecordType.Model) ? MaterialOf(node) : null;
            if (material == null)
            {
                return Color.White;
            }
            float[] rgb = this.MaterialDiffuse(material);
            return Color.FromArgb(Clamp(rgb[0] / 255), Clamp(rgb[1] / 255), Clamp(rgb[2] / 255));
        }

        public bool SetColor(Node node, Color color)
        {
            if (this.ColorSource(node) == null)
            {
                return false;
            }
            float[] rgb = new float[] { color.R, color.G, color.B };
            if (node.Record.Type == AnarkFbf.RecordType.Text)
            {
                for (int k = 0; k < 3; k++)
                {
                    node.Element.Find(TextColorNames[k]).Float = rgb[k];
                }
                this.BgfChanged = true;
            }
            else
            {
                Node material = MaterialOf(node);
                for (int k = 0; k < 3; k++)
                {
                    AnarkBgf.Property p = material.Element.Find(DiffuseNames[k]);
                    if (p != null)
                    {
                        p.Float = rgb[k];
                        this.BgfChanged = true;
                    }
                    else
                    {
                        AnarkFbf.WriteFloat(material.Record.Body, k * 4, rgb[k] / 255);
                        this.FbfChanged = true;
                    }
                }
            }
            this.Update();
            return true;
        }

        // Text and font of a text node: the graph strings (every text element has them).
        public bool SetText(Node node, string text)
        {
            return this.SetString(node, "textstring", text);
        }

        public bool SetFont(Node node, string font)
        {
            return this.SetString(node, "font", font);
        }

        private bool SetString(Node node, string name, string value)
        {
            if (node.Record == null || node.Record.Type != AnarkFbf.RecordType.Text || value == null || !IsLatin1(value))
            {
                return false;
            }
            AnarkBgf.Property p = node.Element.Find(name);
            if (p == null || p.Kind != AnarkBgf.ValueKind.String || p.Value >= this.Bgf.Strings.Count)
            {
                return false;
            }
            this.Bgf.Strings[(int) p.Value] = value;
            this.BgfChanged = true;
            this.Update();
            return true;
        }

        private static bool IsLatin1(string s)
        {
            foreach (char c in s)
            {
                if (c > 0xFF)
                {
                    return false;
                }
            }
            return true;
        }

        // Texture of a model: the GAME:\ path in the .fbf image record under its material.
        public Node TextureNode(Node node)
        {
            if (node.Record == null)
            {
                return null;
            }
            if (node.Record.Type == AnarkFbf.RecordType.Image)
            {
                return node;
            }
            if (node.Record.Type == AnarkFbf.RecordType.Model)
            {
                Node material = MaterialOf(node);
                return (material != null) ? ImageOf(material) : null;
            }
            if (node.Record.Type == AnarkFbf.RecordType.Material)
            {
                return ImageOf(node);
            }
            return null;
        }

        public bool SetTexture(Node node, string gamePath)
        {
            Node image = this.TextureNode(node);
            if (image == null || string.IsNullOrEmpty(gamePath) || !IsLatin1(gamePath))
            {
                return false;
            }
            image.Record.Path = gamePath;
            this.FbfChanged = true;
            this.Update();
            return true;
        }

        // The image file of a GAME:\MEDIA\CARS\<car>\DIGITALGAUGE\<name>.TGA path inside a car zip: digitalgauge/<name>.xds
        public static string TextureEntry(string gamePath, out string car)
        {
            car = null;
            string[] parts = gamePath.Replace('/', '\\').Split('\\');
            int cars = Array.FindIndex(parts, delegate(string p) { return string.Equals(p, "CARS", StringComparison.OrdinalIgnoreCase); });
            if (cars < 0 || cars + 2 >= parts.Length)
            {
                return null;
            }
            car = parts[cars + 1];
            string rest = string.Join("/", parts, cars + 2, parts.Length - cars - 2);
            int dot = rest.LastIndexOf('.');
            return ((dot > 0) ? rest.Substring(0, dot) : rest) + ".xds";
        }
    }
}
