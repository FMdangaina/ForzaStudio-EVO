using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Net;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;


namespace ForzaStudio
{
    public partial class AboutForm : Form
    {
        int move;
        int x;
        int y;

        public AboutForm()
        {
            InitializeComponent();
            this.label1.Text = ArchiveEditorForm.AppTitle;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            this.Hide();
            this.Close();
        }

        private void label3_Click(object sender, EventArgs e)
        {
            Process.Start("http://codeescape.com");
        }

        private void AboutForm_MouseDown(object sender, MouseEventArgs e)
        {
            move = 1;
            x = e.X;
            y = e.Y;
        }

        private void AboutForm_MouseUp(object sender, MouseEventArgs e)
        {
            move = 0;
        }

        private void AboutForm_MouseMove(object sender, MouseEventArgs e)
        {
            if (move == 1)
            {
                this.SetDesktopLocation(MousePosition.X - x, MousePosition.Y - y);
            }
        }
    }
}
