using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ForzaStudio
{
    // The list of the model viewer's commands (docs/UI_WORKFLOW.md, help of the viewport, user 2026-11-16:
    // "dovremmo avere un help dei comandi del viewport editor e del viewport in generale").
    // ONE table: when a key or a mouse action changes in the code, change it here too. The handlers are
    // HandlePickKey / HandlePartKey (PickMode.cs), HandleGizmoKey (MoveGizmo.cs), HandleViewKey / HandleToolKey
    // (ViewControl.cs), HandleUndoKey (UndoRedo.cs), the pnlViewport mouse events (Model.cs, MeshEdit.cs).
    internal static class ViewportHelp
    {
        internal sealed class Group
        {
            public readonly string Title;
            public readonly string Tab;         // side tab the group belongs to (shown first there), or null
            public readonly string[] Rows;      // pairs: keys, what they do

            public Group(string title, string tab, params string[] rows)
            {
                this.Title = title;
                this.Tab = tab;
                this.Rows = rows;
            }
        }

        public static readonly Group[] Groups =
        {
            new Group("VIEW (everywhere)", null,
                "Mouse wheel", "Zoom: the camera moves toward / away from the model (orthographic: the view gets smaller / bigger)",
                "Middle button drag", "Move the view",
                "Alt + middle drag", "Turn around the model",
                "1 - 6  (or keypad)", "Front, Back, Right, Left, Top, Bottom view, orthographic. The keypad works with the pointer over the view",
                "Keyboard", "Never moves the camera"),
            new Group("SELECTION IN THE VIEW", null,
                "Click", "Select one material of a part",
                "Ctrl + click", "Add a material to the selection",
                "Double-click", "Select the whole part",
                "Right-click", "Select and open the view menu",
                "Tree: part node", "The whole part (moves all of it)",
                "Tree: material node", "Only that material"),
            new Group("TOOLS (3ds Max)", null,
                "Q", "Select",
                "W", "Move",
                "E", "Rotate",
                "R", "Scale",
                "Drag a handle", "Move / rotate / scale the selection. Pending until Save"),
            new Group("PART TAB", "Part",
                "Alt + A", "Attach: click the part that goes INTO the selected one",
                "Alt + M", "Merge",
                "Esc  /  right-click", "Stop Attach / Merge",
                "Ctrl + D", "Duplicate the selected part",
                "Alt + Q", "Show only this part (on / off)",
                "Z", "Frame the selected part",
                "F2", "Rename the part or material",
                "Del", "Delete the selected node"),
            new Group("MESH TAB", "Mesh",
                "Click  /  drag", "Select at the level picked in the tab (vertex, edge, face...), with the region shape picked there",
                "Ctrl or Shift + click", "Add to the selection",
                "Alt + click", "Remove from the selection",
                "Ctrl + A", "Select all",
                "Ctrl + Shift + A", "Select none",
                "L", "Select linked faces",
                "Del", "Delete the selected faces",
                "P", "Detach the selected faces",
                "U", "UV Mapper",
                "Enter", "Close a fence region",
                "Esc", "Cancel the region being drawn",
                "W / E / R", "Handles on the selected vertices, edges, faces. Free edits change only the open LOD"),
            new Group("ALWAYS", null,
                "Ctrl + O", "Open an archive",
                "Ctrl + Shift + O", "Open a folder",
                "Ctrl + S", "Save",
                "Ctrl + Z", "Undo",
                "Ctrl + Y  /  Ctrl + Shift + Z", "Redo",
                "Alt + Left  /  Ctrl + B", "Back to the previous page",
                "F1", "This list (again to close it)"),
            new Group("FILE TREE", null,
                "Ctrl + Shift + A", "Add a file to the archive (only with the file tree active: in the view it is Select none of the MESH tab)",
                "Del", "Delete the selected file or folder",
                "Enter  /  double-click", "Open the selected archive (a zip inside the folder)",
                "Right-click", "The file commands, as the FILES bar"),
            new Group("WHERE THE OLD MENUS WENT", null,
                "Archive", "The FILES bar under the top bar; Copy Dash is in Tools",
                "Model", "PART tab, section IMPORT / EXPORT MODEL; Make Rim Scalable also in the PART tab (wheel models); Export UV Map in the TEXTURE tab",
                "Texture", "Export / Import Image on the texture page; New Texture in Tools"),
            // What is not obvious at first sight (user 2026-11-17: "documenta su f1 ... se ci sono cose strane").
            new Group("GOOD TO KNOW", null,
                "Red headings", "A click on a red heading of the side bar closes or opens its section; the state is remembered. IMPORT / EXPORT PART and IMPORT / EXPORT MODEL start closed",
                "File tree", "Hidden while a model is shown: the Files button over the model tree brings it back",
                "FILES bar", "Its buttons work on the file selected in the file tree; the grey ones do not apply to it. Revert becomes Remove on a file just added",
                "DLC button", "In the top bar only with a DLC package open, or a game / DLC car open as a copy (mod pack)",
                "Tools, game buttons", "A grey game has no conversion or tool yet",
                "Make Rim Scalable", "A button of the PART tab, only with a wheel model open. It is never asked automatically; it runs at once, pending until Save, Ctrl+Z undoes it",
                "Nothing is written", "Moves, copies and edits stay pending until Save; Ctrl+Z undoes them, so most actions ask no confirmation",
                "Copy Part", "Never replaces a part unless you pick one in \"Replace in this car\"; the copy gets the next free name",
                "Copy Part file", "After the car you pick its file (main, lod0, cockpit, even a caliper or rotor); the part goes ONLY into the file you have open. Main into lod0 / cockpit uses the part's LOD 1; a lod0 / cockpit / brake part cannot go into the main model (it needs LODs 1-5)",
                "Copy Part UVs", "UV1 and UV2 of a copied part are never changed: it keeps its own layout on this car's textures",
                "Copy Part from FH", "FH parts take their crash deformation from this car's file, never their own; FH stripped meshes are left out",
                "Copy Part from FM2", "An FM2 car has one file (no file question): its parts are rebuilt on this car's main model as the FM2 port does, so crash deformation and baked light come from this car. Into lod0 / cockpit the part's LOD 1 is used",
                "Handles on MESH", "Vertex / edge / face moves change only the open LOD; part moves (PART tab) can also go to lod0 and cockpit",
                "UV box (TEXTURE)", "Framed on the selection's UVs. Select one material for a close view: a part with several materials spreads over their textures",
                "Ctrl + Shift + A", "Two meanings: Add File with the file tree active, Select none in the MESH tab with the view active",
                "1 - 6", "Standard views only with the view active (the digit row) or the pointer over it (keypad)"),
        };
    }

    // Non-modal list of the commands; it does not take the focus, so the keys keep going to the viewer.
    internal sealed class ViewportHelpForm : Form
    {
        private const int Pad = 12, KeyWidth = 170, Gap = 10;
        private readonly Panel list;
        private string tab;

        public ViewportHelpForm()
        {
            this.Text = "Commands and help";
            this.FormBorderStyle = FormBorderStyle.SizableToolWindow;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Size = new Size(520, 640);
            this.MinimumSize = new Size(380, 240);
            this.KeyPreview = true;
            this.list = new DoubleBufferedPanel();
            this.list.Dock = DockStyle.Fill;
            this.list.AutoScroll = true;
            this.list.Paint += delegate(object sender, PaintEventArgs e) { this.Draw(e.Graphics, true); };
            this.list.Resize += delegate { this.Relayout(); };
            this.list.MouseEnter += delegate { if (this.ContainsFocus) { this.list.Focus(); } };
            this.Controls.Add(this.list);
            ForzaTheme.Apply(this);
            this.BackColor = ForzaTheme.Background;
            this.list.BackColor = ForzaTheme.Background;
            this.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.F1)
                {
                    this.Close();
                    e.Handled = true;
                }
            };
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        // The group of the side tab open in the editor goes first, its title in red.
        public void SetTab(string sideTab)
        {
            if (string.Equals(this.tab, sideTab, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            this.tab = sideTab;
            this.list.AutoScrollPosition = Point.Empty;
            this.Relayout();
        }

        private IEnumerable<ViewportHelp.Group> Ordered()
        {
            foreach (ViewportHelp.Group group in ViewportHelp.Groups)
            {
                if (this.IsCurrent(group))
                {
                    yield return group;
                }
            }
            foreach (ViewportHelp.Group group in ViewportHelp.Groups)
            {
                if (!this.IsCurrent(group))
                {
                    yield return group;
                }
            }
        }

        private bool IsCurrent(ViewportHelp.Group group)
        {
            return group.Tab != null && string.Equals(group.Tab, this.tab, StringComparison.OrdinalIgnoreCase);
        }

        private void Relayout()
        {
            using (Graphics graphics = this.list.CreateGraphics())
            {
                int height = this.Draw(graphics, false);
                this.list.AutoScrollMinSize = new Size(0, height);
            }
            this.list.Invalidate();
        }

        // Lays out (and, with paint, draws) the groups; returns the total height.
        private int Draw(Graphics graphics, bool paint)
        {
            int width = Math.Max(200, this.list.ClientSize.Width - 2 * Pad);
            int textWidth = Math.Max(80, width - KeyWidth - Gap);
            int y = Pad + (paint ? this.list.AutoScrollPosition.Y : 0);
            int top = y;
            const TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top;
            foreach (ViewportHelp.Group group in this.Ordered())
            {
                bool current = this.IsCurrent(group);
                string title = current ? group.Title + "  (this tab)" : group.Title;
                int titleHeight = TextRenderer.MeasureText(graphics, title, ForzaTheme.Heading).Height;
                if (paint)
                {
                    TextRenderer.DrawText(graphics, title, ForzaTheme.Heading, new Point(Pad, y), current ? ForzaTheme.Red : ForzaTheme.Text, flags);
                    using (Pen pen = new Pen(ForzaTheme.Line))
                    {
                        graphics.DrawLine(pen, Pad, y + titleHeight + 2, Pad + width, y + titleHeight + 2);
                    }
                }
                y += titleHeight + 8;
                for (int i = 0; i + 1 < group.Rows.Length; i += 2)
                {
                    Size keySize = TextRenderer.MeasureText(graphics, group.Rows[i], ForzaTheme.Bold, new Size(KeyWidth, int.MaxValue), flags);
                    Size textSize = TextRenderer.MeasureText(graphics, group.Rows[i + 1], ForzaTheme.Regular, new Size(textWidth, int.MaxValue), flags);
                    if (paint)
                    {
                        TextRenderer.DrawText(graphics, group.Rows[i], ForzaTheme.Bold, new Rectangle(Pad, y, KeyWidth, keySize.Height), ForzaTheme.Yellow, flags);
                        TextRenderer.DrawText(graphics, group.Rows[i + 1], ForzaTheme.Regular, new Rectangle(Pad + KeyWidth + Gap, y, textWidth, textSize.Height), ForzaTheme.Text, flags);
                    }
                    y += Math.Max(keySize.Height, textSize.Height) + 4;
                }
                y += 14;
            }
            return y - top + Pad;
        }

        private sealed class DoubleBufferedPanel : Panel
        {
            public DoubleBufferedPanel()
            {
                this.DoubleBuffered = true;
                this.ResizeRedraw = true;
            }
        }
    }
}
