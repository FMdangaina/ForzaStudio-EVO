namespace ForzaStudio
{
    using System;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.Windows.Forms;

    // The look of Forza Motorsport 4's menus: near-black surfaces, light grey text, Forza red for the
    // accent and the thin horizontal rules, slanted shapes for headers and tabs.
    internal static class ForzaTheme
    {
        public static readonly Color Background = Color.FromArgb(18, 18, 19);
        public static readonly Color Surface = Color.FromArgb(27, 27, 29);
        public static readonly Color Raised = Color.FromArgb(38, 38, 41);
        public static readonly Color Line = Color.FromArgb(54, 54, 58);
        public static readonly Color Text = Color.FromArgb(232, 232, 232);
        public static readonly Color TextDim = Color.FromArgb(150, 150, 154);
        public static readonly Color Red = Color.FromArgb(226, 38, 30);
        public static readonly Color RedDark = Color.FromArgb(120, 24, 20);
        public static readonly Color Selection = Color.FromArgb(150, 28, 22);
        public static readonly Color Yellow = Color.FromArgb(240, 200, 60);
        public static readonly Color Green = Color.FromArgb(96, 204, 112);   // recommended entries (IRecommended)

        public static readonly Font Regular = new Font("Segoe UI", 9f);
        public static readonly Font Bold = new Font("Segoe UI Semibold", 9f);
        public static readonly Font Heading = new Font("Segoe UI Semibold", 9.75f);

        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr window, string subApp, string idList);

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        // Colors every control below root (call again after adding controls).
        public static void Apply(Control root)
        {
            Style(root);
            DarkChrome(root);
            foreach (Control child in root.Controls)
            {
                Apply(child);
            }
        }

        // Windows' own dark parts (Windows 10 1809 and later): title bar, scroll bars, combo boxes.
        private static void DarkChrome(Control control)
        {
            string theme = null;
            if (control is ComboBox)
            {
                theme = "DarkMode_CFD";
            }
            else if (control is TreeView || control is ListView || control is ListBox || control is TextBoxBase || control is ScrollBar || control is DataGridView
                || (control.Parent != null && (control.Parent is PropertyGrid || control.Parent.Parent is PropertyGrid)))
            {
                theme = "DarkMode_Explorer";
            }
            if (control is Form)
            {
                if (control.IsHandleCreated)
                {
                    DarkTitleBar((Form) control);
                }
                else
                {
                    control.HandleCreated += delegate { DarkTitleBar((Form) control); };
                }
            }
            if (theme == null)
            {
                return;
            }
            if (control.IsHandleCreated)
            {
                SetTheme(control.Handle, theme);
            }
            else
            {
                control.HandleCreated += delegate { SetTheme(control.Handle, theme); };
            }
        }

        private static void SetTheme(IntPtr handle, string theme)
        {
            try
            {
                SetWindowTheme(handle, theme, null);
            }
            catch (Exception)
            {
                // Older Windows: the light parts stay.
            }
        }

        private static void DarkTitleBar(Form form)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(form.Handle, 20, ref on, 4) != 0)
                {
                    DwmSetWindowAttribute(form.Handle, 19, ref on, 4);
                }
            }
            catch (Exception)
            {
            }
        }

        // Selected tree nodes in Forza red instead of the system blue.
        private static void DrawTreeNode(object sender, DrawTreeNodeEventArgs e)
        {
            TreeView tree = (TreeView) sender;
            if (e.Bounds.Width == 0 || e.Bounds.Height == 0)
            {
                return;
            }
            Font font = e.Node.NodeFont ?? tree.Font;
            Size size = TextRenderer.MeasureText(e.Graphics, e.Node.Text, font);
            Rectangle bounds = new Rectangle(e.Bounds.X, e.Bounds.Y, size.Width + 4, e.Bounds.Height);
            bool selected = (e.State & TreeNodeStates.Selected) != 0;
            using (Brush brush = new SolidBrush(selected ? Selection : tree.BackColor))
            {
                e.Graphics.FillRectangle(brush, bounds);
            }
            Color fore = selected ? Color.White : (e.Node.ForeColor.IsEmpty ? tree.ForeColor : e.Node.ForeColor);
            TextRenderer.DrawText(e.Graphics, e.Node.Text, font, bounds, fore, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private static void DrawListItem(object sender, DrawItemEventArgs e)
        {
            ListBox list = (ListBox) sender;
            if (e.Index < 0)
            {
                return;
            }
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (Brush brush = new SolidBrush(selected ? Selection : list.BackColor))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }
            Rectangle text = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 4, e.Bounds.Height);
            object item = list.Items[e.Index];
            TextRenderer.DrawText(e.Graphics, item.ToString(), list.Font, text, IsRecommended(item) ? Green : (selected ? Color.White : list.ForeColor), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        public static void DrawListHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (Brush brush = new SolidBrush(Background))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }
            Rectangle text = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.Header.Text.ToUpperInvariant(), Bold, text, TextDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            using (Pen pen = new Pen(Red))
            {
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }
        }

        // The last column reaches the right edge, so no undrawn (light) header is left beside it.
        private static void StretchLastColumn(ListView list)
        {
            if (list.View != View.Details || list.Columns.Count == 0 || !list.IsHandleCreated || list.IsDisposed)
            {
                return;
            }
            int others = 0;
            for (int i = 0; i < list.Columns.Count - 1; i++)
            {
                others += list.Columns[i].Width;
            }
            int width = list.ClientSize.Width - others;
            ColumnHeader last = list.Columns[list.Columns.Count - 1];
            if (width > 40 && last.Width != width)
            {
                last.Width = width;
            }
        }

        public delegate bool ItemTest(object item);

        public static bool IsRecommended(object item)
        {
            IRecommended entry = item as IRecommended;
            return entry != null && entry.Recommended;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ComboBox, object> searching = new System.Runtime.CompilerServices.ConditionalWeakTable<ComboBox, object>();

        // Typing in a list (DropDownList) picks the best entry while the user writes, in place of the first-letter jump:
        // an entry that is the text, then one that starts with it, then one with a word that starts with it, then one that
        // holds it (or all its words). A tip under the box shows what was typed; it starts again after two seconds.
        public static void TypeToSearch(ComboBox combo)
        {
            object mark;
            if (combo.DropDownStyle != ComboBoxStyle.DropDownList || searching.TryGetValue(combo, out mark))
            {
                return;
            }
            searching.Add(combo, new object());
            System.Text.StringBuilder typed = new System.Text.StringBuilder();
            DateTime last = DateTime.MinValue;
            ToolTip tip = new ToolTip();
            combo.Disposed += delegate { tip.Dispose(); };
            combo.KeyPress += delegate(object sender, KeyPressEventArgs e)
            {
                if (e.KeyChar == (char) 27 || e.KeyChar == '\r' || e.KeyChar == '\t')
                {
                    typed.Length = 0;
                    tip.Hide(combo);
                    return;
                }
                if (e.KeyChar == '\b')
                {
                    if (typed.Length > 0)
                    {
                        typed.Length--;
                    }
                }
                else if (char.IsControl(e.KeyChar) || (e.KeyChar == ' ' && typed.Length == 0))
                {
                    return;
                }
                else
                {
                    if ((DateTime.Now - last).TotalMilliseconds > 2000)
                    {
                        typed.Length = 0;
                    }
                    typed.Append(e.KeyChar);
                }
                last = DateTime.Now;
                e.Handled = true;
                int best = BestMatch(combo, typed.ToString());
                if (best >= 0 && best != combo.SelectedIndex)
                {
                    combo.SelectedIndex = best;
                }
                tip.Show(typed.Length == 0 ? "search" : "search: " + typed + (best < 0 ? "  (no match)" : ""), combo, 0, combo.Height + 2, 2000);
            };
        }

        private static int BestMatch(ComboBox combo, string query)
        {
            query = query.Trim().ToLowerInvariant();
            if (query.Length == 0)
            {
                return -1;
            }
            string[] terms = query.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int best = -1;
            int bestScore = int.MaxValue;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                string text = combo.GetItemText(combo.Items[i]).ToLowerInvariant();
                int score;
                if (text == query)
                {
                    score = 0;
                }
                else if (text.StartsWith(query, StringComparison.Ordinal))
                {
                    score = 1;
                }
                else if (WordStart(text, query))
                {
                    score = 2;
                }
                else if (text.IndexOf(query, StringComparison.Ordinal) >= 0)
                {
                    score = 3;
                }
                else
                {
                    bool all = terms.Length > 1;
                    foreach (string term in terms)
                    {
                        all = all && text.IndexOf(term, StringComparison.Ordinal) >= 0;
                    }
                    if (!all)
                    {
                        continue;
                    }
                    score = 4;
                }
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        private static bool WordStart(string text, string query)
        {
            for (int at = text.IndexOf(query, StringComparison.Ordinal); at >= 0; at = text.IndexOf(query, at + 1, StringComparison.Ordinal))
            {
                if (at == 0 || !char.IsLetterOrDigit(text[at - 1]))
                {
                    return true;
                }
            }
            return false;
        }

        // A list whose recommended entries are written in green (in place of a ★ mark, user 2026-10-20).
        public static void GreenItems(ComboBox combo)
        {
            GreenItems(combo, IsRecommended);
        }

        public static void GreenItems(ComboBox combo, ItemTest green)
        {
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                if (e.Index < 0)
                {
                    return;
                }
                object item = combo.Items[e.Index];
                bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
                using (Brush brush = new SolidBrush(selected ? Selection : combo.BackColor))
                {
                    e.Graphics.FillRectangle(brush, e.Bounds);
                }
                Rectangle text = new Rectangle(e.Bounds.X + 2, e.Bounds.Y, e.Bounds.Width - 2, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, combo.GetItemText(item), combo.Font, text, green(item) ? Green : (selected ? Color.White : combo.ForeColor),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
        }

        private static void Style(Control control)
        {
            if (control.Tag as string == "keep-colors")
            {
                return;
            }
            if (control is ToolStrip)
            {
                ToolStrip strip = (ToolStrip) control;
                strip.Renderer = Renderer;
                strip.BackColor = (strip is MenuStrip) ? Background : Surface;
                strip.ForeColor = Text;
                foreach (ToolStripItem item in strip.Items)
                {
                    StyleItem(item);
                }
                return;
            }
            if (control is DataGridView)
            {
                StyleGrid((DataGridView) control);
                return;
            }
            if (control is PropertyGrid)
            {
                StylePropertyGrid((PropertyGrid) control);
                return;
            }
            if (control is TreeView)
            {
                TreeView tree = (TreeView) control;
                tree.BackColor = Surface;
                tree.ForeColor = Text;
                tree.LineColor = Line;
                tree.BorderStyle = BorderStyle.None;
                if (tree.DrawMode == TreeViewDrawMode.Normal)
                {
                    tree.DrawMode = TreeViewDrawMode.OwnerDrawText;
                    tree.DrawNode += new DrawTreeNodeEventHandler(DrawTreeNode);
                }
                return;
            }
            if (control is ListView)
            {
                ListView list = (ListView) control;
                list.BackColor = Surface;
                list.ForeColor = Text;
                list.BorderStyle = BorderStyle.None;
                if (!list.OwnerDraw)
                {
                    // Windows draws the column headers light even in dark mode: they are drawn here
                    // (upper case, red rule, as the database list), the rows stay Windows' own.
                    list.OwnerDraw = true;
                    list.DrawColumnHeader += new DrawListViewColumnHeaderEventHandler(DrawListHeader);
                    list.DrawItem += delegate(object sender, DrawListViewItemEventArgs e) { e.DrawDefault = true; };
                    list.DrawSubItem += delegate(object sender, DrawListViewSubItemEventArgs e) { e.DrawDefault = true; };
                    list.Resize += delegate { StretchLastColumn(list); };
                    list.ColumnWidthChanged += delegate(object sender, ColumnWidthChangedEventArgs e)
                    {
                        if (e.ColumnIndex < list.Columns.Count - 1)
                        {
                            StretchLastColumn(list);
                        }
                    };
                }
                return;
            }
            if (control is ListBox)
            {
                ListBox list = (ListBox) control;
                list.BackColor = Surface;
                list.ForeColor = Text;
                list.BorderStyle = BorderStyle.None;
                if (list.DrawMode == DrawMode.Normal)
                {
                    list.DrawMode = DrawMode.OwnerDrawFixed;
                    list.ItemHeight = 18;
                    list.DrawItem += new DrawItemEventHandler(DrawListItem);
                }
                return;
            }
            if (control is TextBoxBase)
            {
                control.BackColor = Surface;
                control.ForeColor = Text;
                ((TextBoxBase) control).BorderStyle = BorderStyle.None;
                return;
            }
            if (control is ComboBox)
            {
                ComboBox combo = (ComboBox) control;
                combo.FlatStyle = FlatStyle.Flat;
                combo.BackColor = Raised;
                combo.ForeColor = Text;
                TypeToSearch(combo);
                return;
            }
            if (control is Button)
            {
                Button button = (Button) control;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Line;
                button.FlatAppearance.MouseOverBackColor = RedDark;
                button.FlatAppearance.MouseDownBackColor = Red;
                button.BackColor = Raised;
                button.ForeColor = Text;
                return;
            }
            if (control is CheckBox || control is Label)
            {
                if (control.ForeColor == SystemColors.GrayText || control.ForeColor == SystemColors.ControlDarkDark)
                {
                    control.ForeColor = TextDim;
                }
                else if (control.ForeColor == SystemColors.ControlText)
                {
                    control.ForeColor = Text;
                }
                control.BackColor = Color.Transparent;
                return;
            }
            if (control is TabPage || control is Panel || control is SplitContainer || control is SplitterPanel || control is TabControl || control is FlowLayoutPanel || control is Form)
            {
                control.BackColor = Background;
                control.ForeColor = Text;
            }
        }

        private static void StyleItem(ToolStripItem item)
        {
            item.ForeColor = Text;
            if (item is ToolStripTextBox)
            {
                ToolStripTextBox box = (ToolStripTextBox) item;
                box.BackColor = Raised;
                box.ForeColor = Text;
                box.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (item is ToolStripComboBox)
            {
                ToolStripComboBox combo = (ToolStripComboBox) item;
                combo.FlatStyle = FlatStyle.Flat;
                combo.BackColor = Raised;
                combo.ForeColor = Text;
                combo.ComboBox.BackColor = Raised;
                combo.ComboBox.ForeColor = Text;
                TypeToSearch(combo.ComboBox);
                DarkChrome(combo.ComboBox);
            }
            ToolStripDropDownItem dropDown = item as ToolStripDropDownItem;
            if (dropDown != null)
            {
                dropDown.DropDown.Renderer = Renderer;
                dropDown.DropDown.BackColor = Surface;
                foreach (ToolStripItem child in dropDown.DropDownItems)
                {
                    StyleItem(child);
                }
            }
        }

        public static void StyleGrid(DataGridView grid)
        {
            grid.EnableHeadersVisualStyles = false;
            grid.BackgroundColor = Background;
            grid.GridColor = Line;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.DefaultCellStyle.BackColor = Surface;
            grid.DefaultCellStyle.ForeColor = Text;
            grid.DefaultCellStyle.SelectionBackColor = Selection;
            grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(31, 31, 34);
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Background;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextDim;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Background;
            grid.ColumnHeadersDefaultCellStyle.Font = Bold;
            grid.RowHeadersDefaultCellStyle.BackColor = Background;
            grid.RowHeadersDefaultCellStyle.ForeColor = TextDim;
            grid.RowHeadersDefaultCellStyle.SelectionBackColor = Red;
            grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        }

        public static void StylePropertyGrid(PropertyGrid grid)
        {
            grid.BackColor = Background;
            grid.ViewBackColor = Surface;
            grid.ViewForeColor = Text;
            grid.LineColor = Background;
            grid.CategoryForeColor = Red;
            grid.HelpBackColor = Background;
            grid.HelpForeColor = TextDim;
            grid.CommandsBackColor = Background;
            grid.CommandsForeColor = Text;
        }

        // A parallelogram leaning right, the shape of the game's tabs and class badges.
        public static GraphicsPath Slanted(Rectangle bounds, int slant)
        {
            GraphicsPath path = new GraphicsPath();
            path.AddPolygon(new Point[]
            {
                new Point(bounds.Left + slant, bounds.Top), new Point(bounds.Right, bounds.Top),
                new Point(bounds.Right - slant, bounds.Bottom), new Point(bounds.Left, bounds.Bottom)
            });
            return path;
        }

        // A class badge like the game's: colored part with the letter, slanted edge, white part with the PI.
        public static void DrawClassBadge(Graphics graphics, Rectangle bounds, string letter, string pi, Color color)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle badge = new Rectangle(bounds.X + 2, bounds.Y + 2, Math.Min(bounds.Width - 4, 64), bounds.Height - 4);
            int split = badge.X + 22;
            using (GraphicsPath white = new GraphicsPath())
            {
                white.AddRectangle(badge);
                using (Brush brush = new SolidBrush(Color.FromArgb(232, 232, 232)))
                {
                    graphics.FillPath(brush, white);
                }
            }
            using (GraphicsPath colored = new GraphicsPath())
            {
                colored.AddPolygon(new Point[] { new Point(badge.Left, badge.Top), new Point(split + 4, badge.Top), new Point(split - 2, badge.Bottom), new Point(badge.Left, badge.Bottom) });
                using (Brush brush = new SolidBrush(color))
                {
                    graphics.FillPath(brush, colored);
                }
            }
            TextRenderer.DrawText(graphics, letter, Bold, new Rectangle(badge.Left, badge.Top, split - badge.Left, badge.Height), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(graphics, pi, Regular, new Rectangle(split, badge.Top, badge.Right - split, badge.Height), Color.FromArgb(40, 40, 40), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            graphics.SmoothingMode = SmoothingMode.Default;
        }

        public static readonly ToolStripRenderer Renderer = new ForzaRenderer();

        private class ForzaColors : ProfessionalColorTable
        {
            public override Color MenuStripGradientBegin { get { return Background; } }
            public override Color MenuStripGradientEnd { get { return Background; } }
            public override Color ToolStripGradientBegin { get { return Surface; } }
            public override Color ToolStripGradientMiddle { get { return Surface; } }
            public override Color ToolStripGradientEnd { get { return Surface; } }
            public override Color ToolStripBorder { get { return Surface; } }
            public override Color ToolStripDropDownBackground { get { return Surface; } }
            public override Color ImageMarginGradientBegin { get { return Surface; } }
            public override Color ImageMarginGradientMiddle { get { return Surface; } }
            public override Color ImageMarginGradientEnd { get { return Surface; } }
            public override Color MenuBorder { get { return Line; } }
            public override Color MenuItemBorder { get { return Red; } }
            public override Color MenuItemSelected { get { return RedDark; } }
            public override Color MenuItemSelectedGradientBegin { get { return RedDark; } }
            public override Color MenuItemSelectedGradientEnd { get { return RedDark; } }
            public override Color MenuItemPressedGradientBegin { get { return Raised; } }
            public override Color MenuItemPressedGradientMiddle { get { return Raised; } }
            public override Color MenuItemPressedGradientEnd { get { return Raised; } }
            public override Color ButtonSelectedHighlight { get { return RedDark; } }
            public override Color ButtonSelectedGradientBegin { get { return RedDark; } }
            public override Color ButtonSelectedGradientMiddle { get { return RedDark; } }
            public override Color ButtonSelectedGradientEnd { get { return RedDark; } }
            public override Color ButtonSelectedBorder { get { return Red; } }
            public override Color ButtonPressedGradientBegin { get { return Red; } }
            public override Color ButtonPressedGradientMiddle { get { return Red; } }
            public override Color ButtonPressedGradientEnd { get { return Red; } }
            public override Color ButtonPressedBorder { get { return Red; } }
            public override Color ButtonCheckedGradientBegin { get { return Selection; } }
            public override Color ButtonCheckedGradientMiddle { get { return Selection; } }
            public override Color ButtonCheckedGradientEnd { get { return Selection; } }
            public override Color ButtonCheckedHighlight { get { return Selection; } }
            public override Color CheckBackground { get { return Selection; } }
            public override Color SeparatorDark { get { return Line; } }
            public override Color SeparatorLight { get { return Background; } }
            public override Color StatusStripGradientBegin { get { return Background; } }
            public override Color StatusStripGradientEnd { get { return Background; } }
            public override Color GripDark { get { return Line; } }
            public override Color GripLight { get { return Background; } }
        }

        private class ForzaRenderer : ToolStripProfessionalRenderer
        {
            public ForzaRenderer()
                : base(new ForzaColors())
            {
                this.RoundedEdges = false;
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? ((e.Item.ForeColor == Red) ? Red : Text) : Color.FromArgb(95, 95, 98);
                base.OnRenderItemText(e);
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = TextDim;
                base.OnRenderArrow(e);
            }

            // The thin red rule under the menu bar, as on the game's screens.
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                if (e.ToolStrip is MenuStrip)
                {
                    using (Pen pen = new Pen(Red, 2f))
                    {
                        e.Graphics.DrawLine(pen, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
                    }
                    return;
                }
                if (e.ToolStrip is StatusStrip)
                {
                    using (Pen pen = new Pen(Line))
                    {
                        e.Graphics.DrawLine(pen, 0, 0, e.ToolStrip.Width, 0);
                    }
                    return;
                }
                if (!(e.ToolStrip is ToolStripDropDown))
                {
                    return;
                }
                base.OnRenderToolStripBorder(e);
            }
        }
    }

    // A list entry the tool recommends (most similar donor, suggested material, parameter FM4 sets):
    // drawn in ForzaTheme.Green.
    internal interface IRecommended
    {
        bool Recommended { get; }
    }

    // Tabs drawn like the game's menu headers: upper-case labels on slanted plates, the selected one
    // in Forza red. It drives a TabControl whose own tab row is hidden.
    // Vertical: a rail on the right edge of a side panel, the labels read top to bottom, so any
    // number of sections fits (the model view's PART / TEXTURE / CAMERA / COLLISION, user 2026-10-20).
    internal class ForzaTabStrip : Control
    {
        private readonly TabControl tabs;
        private readonly bool vertical;
        private int hover = -1;

        public ForzaTabStrip(TabControl tabs)
            : this(tabs, false)
        {
        }

        public ForzaTabStrip(TabControl tabs, bool vertical)
        {
            this.tabs = tabs;
            this.vertical = vertical;
            this.Dock = vertical ? DockStyle.Right : DockStyle.Top;
            if (vertical)
            {
                this.Width = 28;
            }
            else
            {
                this.Height = 30;
            }
            this.DoubleBuffered = true;
            this.BackColor = ForzaTheme.Background;
            this.Cursor = Cursors.Hand;
            tabs.Appearance = TabAppearance.FlatButtons;
            tabs.ItemSize = new Size(0, 1);
            tabs.SizeMode = TabSizeMode.Fixed;
            tabs.SelectedIndexChanged += delegate { this.Invalidate(); };
            tabs.ControlAdded += delegate { this.Invalidate(); };
            tabs.ControlRemoved += delegate { this.Invalidate(); };
        }

        private Rectangle TabBounds(int index, Graphics graphics)
        {
            if (this.vertical)
            {
                int y = 6;
                for (int i = 0; i < this.tabs.TabCount; i++)
                {
                    int height = TextRenderer.MeasureText(graphics, this.tabs.TabPages[i].Text.ToUpperInvariant(), ForzaTheme.Bold).Width + 24;
                    if (i == index)
                    {
                        return new Rectangle(4, y, this.Width - 6, height);
                    }
                    y += height + 3;
                }
                return Rectangle.Empty;
            }
            int x = 6;
            for (int i = 0; i < this.tabs.TabCount; i++)
            {
                int width = TextRenderer.MeasureText(graphics, this.tabs.TabPages[i].Text.ToUpperInvariant(), ForzaTheme.Bold).Width + 30;
                if (i == index)
                {
                    return new Rectangle(x, 4, width, this.Height - 9);
                }
                x += width - 6;
            }
            return Rectangle.Empty;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(this.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (this.vertical)
            {
                this.PaintVertical(e.Graphics);
                return;
            }
            for (int i = 0; i < this.tabs.TabCount; i++)
            {
                Rectangle bounds = this.TabBounds(i, e.Graphics);
                bool selected = i == this.tabs.SelectedIndex;
                Color fill = selected ? ForzaTheme.Red : (i == this.hover ? ForzaTheme.Raised : ForzaTheme.Surface);
                using (GraphicsPath path = ForzaTheme.Slanted(bounds, 8))
                using (Brush brush = new SolidBrush(fill))
                {
                    e.Graphics.FillPath(brush, path);
                }
                TextRenderer.DrawText(e.Graphics, this.tabs.TabPages[i].Text.ToUpperInvariant(), ForzaTheme.Bold, bounds, selected ? Color.White : ForzaTheme.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            using (Pen pen = new Pen(ForzaTheme.Red))
            {
                e.Graphics.DrawLine(pen, 0, this.Height - 2, this.Width, this.Height - 2);
            }
        }

        // Plates down the rail, the text turned 90 degrees; the red rule on the panel side.
        private void PaintVertical(Graphics graphics)
        {
            using (Pen pen = new Pen(ForzaTheme.Red))
            {
                graphics.DrawLine(pen, 1, 0, 1, this.Height);
            }
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                format.FormatFlags = StringFormatFlags.NoWrap;
                for (int i = 0; i < this.tabs.TabCount; i++)
                {
                    Rectangle bounds = this.TabBounds(i, graphics);
                    bool selected = i == this.tabs.SelectedIndex;
                    Color fill = selected ? ForzaTheme.Red : (i == this.hover ? ForzaTheme.Raised : ForzaTheme.Surface);
                    using (Brush brush = new SolidBrush(fill))
                    {
                        graphics.FillRectangle(brush, bounds);
                    }
                    GraphicsState state = graphics.Save();
                    graphics.TranslateTransform(bounds.X + (bounds.Width / 2f), bounds.Y + (bounds.Height / 2f));
                    graphics.RotateTransform(90);
                    using (Brush text = new SolidBrush(selected ? Color.White : ForzaTheme.TextDim))
                    {
                        graphics.DrawString(this.tabs.TabPages[i].Text.ToUpperInvariant(), ForzaTheme.Bold, text, new RectangleF(-bounds.Height / 2f, -bounds.Width / 2f, bounds.Height, bounds.Width), format);
                    }
                    graphics.Restore(state);
                }
            }
        }

        private int HitTest(Point point)
        {
            using (Graphics graphics = this.CreateGraphics())
            {
                for (int i = 0; i < this.tabs.TabCount; i++)
                {
                    if (this.TabBounds(i, graphics).Contains(point))
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int index = this.HitTest(e.Location);
            if (index >= 0)
            {
                this.tabs.SelectedIndex = index;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = this.HitTest(e.Location);
            if (index != this.hover)
            {
                this.hover = index;
                this.Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            this.hover = -1;
            this.Invalidate();
        }
    }
}
