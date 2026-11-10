namespace ForzaStudio
{
    using System;
    using System.Drawing;
    using System.Runtime.InteropServices;
    using System.Windows.Forms;

    // A TreeView with check boxes that keeps its ticks and Checked in step. The Windows tree control
    // toggles the box on a double click by itself, without AfterCheck: the box then shows a tick that
    // Checked (what the viewport draws) does not have. A double click on a box is dropped here; its first
    // click already toggled the box through the normal path.
    public class ModelTreeView : TreeView
    {
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int TVM_HITTEST = 0x1111;
        private const int TVHT_ONITEMSTATEICON = 0x0040;

        [StructLayout(LayoutKind.Sequential)]
        private struct TVHITTESTINFO
        {
            public int X;
            public int Y;
            public int Flags;
            public IntPtr Item;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref TVHITTESTINFO lParam);

        public ModelTreeView()
        {
            // Less flicker when many boxes change at once.
            this.DoubleBuffered = true;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_LBUTTONDBLCLK && this.CheckBoxes)
            {
                TVHITTESTINFO hit = new TVHITTESTINFO();
                long lParam = m.LParam.ToInt64();
                hit.X = (short) (lParam & 0xFFFF);
                hit.Y = (short) ((lParam >> 16) & 0xFFFF);
                SendMessage(this.Handle, TVM_HITTEST, IntPtr.Zero, ref hit);
                if ((hit.Flags & TVHT_ONITEMSTATEICON) != 0)
                {
                    m.Result = IntPtr.Zero;
                    return;
                }
            }
            base.WndProc(ref m);
        }
    }
}
