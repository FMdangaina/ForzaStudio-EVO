namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Windows.Forms;

    // Replace the thumbnail open in the texture view with one of the Xenia profile (XeniaThumbnails).
    // Tools > Set Generated Thumbnail does it for the cars of a whole DLC (SetThumbnailsForm).
    public partial class ArchiveEditorForm
    {
        private void ReplaceWithXeniaThumbnail()
        {
            if (this.texture == null || this.currentEntry == null)
            {
                return;
            }
            byte[] xds;
            string name;
            using (XeniaThumbnailPicker picker = new XeniaThumbnailPicker(768, 288, "A8R8G8B8"))
            {
                if (picker.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                xds = picker.Selected;
                name = picker.SelectedName;
            }
            try
            {
                int level = this.cmbMipLevel.SelectedIndex;
                if (this.xdcSource != null)
                {
                    this.xdcSource = XdcFile.Pack(xds, this.xdcSource);
                    this.ReplaceCurrent(this.xdcSource);
                }
                else
                {
                    this.ReplaceCurrent(xds);
                }
                this.ShowTexture(xds);
                this.cmbMipLevel.SelectedIndex = Math.Min(level, this.cmbMipLevel.Items.Count - 1);
                this.SetStatus(string.Format("Replaced {0} with the Xenia thumbnail {1} - save the archive to keep it", this.currentEntry.Name, name));
            }
            catch (Exception exception)
            {
                this.SetStatus("Replace failed");
                MessageBox.Show(this, exception.Message, "Xenia Thumbnail", MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
        }

        private sealed class XeniaThumbnailPicker : Form
        {
            private const int PreviewWidth = 256;
            private const int PreviewHeight = 96;

            private readonly int width;
            private readonly int height;
            private readonly string codec;
            private readonly ListView list;
            private readonly ImageList images;
            private readonly Label folderLabel;
            private readonly Button ok;
            private readonly List<XeniaThumbnails.Item> items = new List<XeniaThumbnails.Item>();

            public byte[] Selected { get; private set; }
            public string SelectedName { get; private set; }

            public XeniaThumbnailPicker(int width, int height, string codec)
            {
                this.width = width;
                this.height = height;
                this.codec = codec;
                this.AutoScaleMode = AutoScaleMode.None;
                this.Text = "Xenia Thumbnail";
                this.ClientSize = new Size(620, 460);
                this.StartPosition = FormStartPosition.CenterParent;
                this.MinimizeBox = false;
                this.MaximizeBox = false;
                this.ShowInTaskbar = false;
                this.FormBorderStyle = FormBorderStyle.FixedDialog;

                this.folderLabel = new Label();
                this.folderLabel.AutoEllipsis = true;
                this.folderLabel.SetBounds(10, 12, 500, 20);
                Button browse = new Button();
                browse.Text = "Folder...";
                browse.SetBounds(520, 8, 90, 26);
                browse.Click += delegate { this.BrowseFolder(); };

                this.images = new ImageList();
                this.images.ColorDepth = ColorDepth.Depth32Bit;
                this.images.ImageSize = new Size(PreviewWidth, PreviewHeight);
                this.list = new ListView();
                this.list.View = View.LargeIcon;
                this.list.LargeImageList = this.images;
                this.list.MultiSelect = false;
                this.list.HideSelection = false;
                this.list.SetBounds(10, 40, 600, 370);
                this.list.SelectedIndexChanged += delegate { this.ok.Enabled = this.list.SelectedIndices.Count == 1; };
                this.list.DoubleClick += delegate { this.Accept(); };

                this.ok = new Button();
                this.ok.Text = "Use this thumbnail";
                this.ok.SetBounds(380, 420, 140, 28);
                this.ok.Enabled = false;
                this.ok.Click += delegate { this.Accept(); };
                Button cancel = new Button();
                cancel.Text = "Cancel";
                cancel.SetBounds(530, 420, 80, 28);
                cancel.DialogResult = DialogResult.Cancel;
                this.CancelButton = cancel;

                this.Controls.AddRange(new Control[] { this.folderLabel, browse, this.list, this.ok, cancel });
                this.Font = ForzaTheme.Regular;
                this.BackColor = ForzaTheme.Background;
                this.ForeColor = ForzaTheme.Text;
                ForzaTheme.Apply(this);
                this.Shown += delegate { this.LoadFolder(XeniaThumbnails.FindFolder(this.width == 512 ? "4D53084D" : "4D530910")); };
            }

            private void BrowseFolder()
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "The ForzaProfile\\Thumbnails folder of the Xenia profile (it holds Thumbnail_N.xdc).";
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        XeniaThumbnails.Remember(dialog.SelectedPath, this.width == 512 ? "4D53084D" : "4D530910");
                        this.LoadFolder(dialog.SelectedPath);
                    }
                }
            }

            private void LoadFolder(string folder)
            {
                this.list.Items.Clear();
                this.images.Images.Clear();
                this.items.Clear();
                this.ok.Enabled = false;
                if (folder == null || !System.IO.Directory.Exists(folder))
                {
                    this.folderLabel.Text = "Thumbnails folder not found: choose it with Folder...";
                    return;
                }
                this.folderLabel.Text = folder;
                Cursor.Current = Cursors.WaitCursor;
                try
                {
                    foreach (XeniaThumbnails.Item item in XeniaThumbnails.Load(folder, this.width, this.height, this.codec))
                    {
                        this.images.Images.Add(XeniaThumbnails.Preview(item.Texture, PreviewWidth, PreviewHeight));
                        this.items.Add(item);
                        ListViewItem row = new ListViewItem(item.Name + "  " + item.Time.ToString("dd/MM HH:mm"));
                        row.ImageIndex = this.items.Count - 1;
                        this.list.Items.Add(row);
                    }
                }
                finally
                {
                    Cursor.Current = Cursors.Default;
                }
                if (this.list.Items.Count == 0)
                {
                    this.folderLabel.Text = folder + " - no " + this.width + " x " + this.height + " thumbnails";
                }
            }

            private void Accept()
            {
                if (this.list.SelectedIndices.Count != 1)
                {
                    return;
                }
                XeniaThumbnails.Item item = this.items[this.list.SelectedItems[0].ImageIndex];
                this.Selected = item.Xds;
                this.SelectedName = item.Name;
                this.DialogResult = DialogResult.OK;
            }
        }
    }
}
