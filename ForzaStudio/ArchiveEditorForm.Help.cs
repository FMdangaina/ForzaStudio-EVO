using System;
using System.Drawing;
using System.Windows.Forms;

namespace ForzaStudio
{
    // F1 / the "?" button of the viewer: the list of the viewport commands (ViewportHelp.cs). Non-modal, it stays
    // open while working; the group of the side tab in use goes first.
    public partial class ArchiveEditorForm
    {
        private ViewportHelpForm helpForm;
        private bool helpFollowsTab;

        private ToolStripButton BuildHelpButton()
        {
            ToolStripButton button = new ToolStripButton("?");
            button.Font = ForzaTheme.Bold;
            button.AutoToolTip = false;
            button.ToolTipText = "Viewport commands: mouse, keys, tools  (F1)";
            button.Click += delegate { this.ToggleViewportHelp(); };
            return button;
        }

        private bool HandleHelpKey(Keys keyData)
        {
            if (keyData != Keys.F1)
            {
                return false;
            }
            this.ToggleViewportHelp();
            return true;
        }

        private void ToggleViewportHelp()
        {
            if (this.helpForm != null && !this.helpForm.IsDisposed)
            {
                this.helpForm.Close();
                return;
            }
            this.helpForm = new ViewportHelpForm();
            this.helpForm.FormClosed += delegate { this.helpForm = null; };
            this.helpForm.SetTab(this.CurrentSideTabName());
            if (!this.helpFollowsTab && this.tabSide != null)
            {
                this.helpFollowsTab = true;
                this.tabSide.SelectedIndexChanged += delegate
                {
                    if (this.helpForm != null && !this.helpForm.IsDisposed)
                    {
                        this.helpForm.SetTab(this.CurrentSideTabName());
                    }
                };
            }
            // At the top right corner of the viewer (of the content area when no model is shown), inside the screen.
            Control area = (this.pnlModel != null && this.pnlModel.Visible) ? (Control) this.pnlViewport : this.pnlContent;
            Point corner = area.PointToScreen(new Point(area.ClientSize.Width, 0));
            Rectangle screen = Screen.FromControl(this).WorkingArea;
            int x = Math.Max(screen.Left, Math.Min(corner.X - this.helpForm.Width - 8, screen.Right - this.helpForm.Width));
            int y = Math.Max(screen.Top, Math.Min(corner.Y + 8, screen.Bottom - this.helpForm.Height));
            this.helpForm.Location = new Point(x, y);
            this.helpForm.Show(this);
        }

        private string CurrentSideTabName()
        {
            return (this.tabSide != null && this.tabSide.SelectedTab != null) ? this.tabSide.SelectedTab.Text.Trim() : null;
        }
    }
}
