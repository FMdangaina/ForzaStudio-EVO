namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.IO;
    using System.Windows.Forms;

    // File > Mod Packs (user 2026-11-10): lists the mod packs (ModPack) installed in Xenia's FM4 content folder and
    // uninstalls the ones picked. Uninstall = the mod pack file is deleted (the original car comes back in game) and the
    // editor's copies of that car in %TEMP%\ForzaStudio\PackageCars go too, so the next Open Car Model reads the original.
    public partial class ArchiveEditorForm
    {
        private void ShowModPacks()
        {
            string content = InstalledDlc.ContentFolder(ForzaGame.Motorsport4);
            if (content == null)
            {
                MessageBox.Show(this, "The Xenia content folder of FM4 is not set (Tools > Settings > Folders).", "Mod Packs", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (Form form = new Form())
            {
                form.Text = "Mod Packs (FM4)";
                form.StartPosition = FormStartPosition.CenterParent;
                form.Size = new Size(860, 420);
                form.MinimumSize = new Size(560, 260);
                form.ShowInTaskbar = false;
                form.MinimizeBox = false;

                Label info = new Label();
                info.Dock = DockStyle.Top;
                info.Height = 40;
                info.Padding = new Padding(8, 6, 8, 0);
                info.Text = "Mod packs made by Save of a game or DLC car, in " + content + ".\r\nUninstall deletes the mod pack: the original car comes back in game. The original files were never changed.";

                ListView list = new ListView();
                list.Dock = DockStyle.Fill;
                list.View = View.Details;
                list.FullRowSelect = true;
                list.HideSelection = false;
                list.MultiSelect = true;
                list.Columns.Add("Car", 230);
                list.Columns.Add("Offer", 55);
                list.Columns.Add("Standalone", 75);
                list.Columns.Add("Written", 130);
                list.Columns.Add("Replaces", 200);
                list.Columns.Add("File", 140);

                FlowLayoutPanel buttons = new FlowLayoutPanel();
                buttons.Dock = DockStyle.Bottom;
                buttons.Height = 40;
                buttons.FlowDirection = FlowDirection.RightToLeft;
                buttons.Padding = new Padding(6);
                Button close = new Button();
                close.Text = "Close";
                close.Width = 90;
                close.DialogResult = DialogResult.Cancel;
                Button uninstall = new Button();
                uninstall.Text = "Uninstall";
                uninstall.Width = 110;
                uninstall.Enabled = false;
                buttons.Controls.Add(close);
                buttons.Controls.Add(uninstall);
                form.CancelButton = close;

                MethodInvoker fill = delegate
                {
                    list.BeginUpdate();
                    list.Items.Clear();
                    foreach (KeyValuePair<string, Dictionary<string, string>> pack in ModPack.List(content))
                    {
                        ListViewItem item = new ListViewItem(pack.Value["car"]);
                        item.SubItems.Add(Value(pack.Value, "offer"));
                        item.SubItems.Add(Value(pack.Value, "standalone") == "1" ? "yes" : "no");
                        item.SubItems.Add(Value(pack.Value, "written"));
                        item.SubItems.Add(Value(pack.Value, "source"));
                        item.SubItems.Add(Path.GetFileName(pack.Key));
                        item.Tag = pack.Key;
                        list.Items.Add(item);
                    }
                    list.EndUpdate();
                    uninstall.Enabled = list.SelectedItems.Count > 0;
                };
                list.SelectedIndexChanged += delegate { uninstall.Enabled = list.SelectedItems.Count > 0; };
                uninstall.Click += delegate
                {
                    if (this.UninstallModPacks(form, list.SelectedItems))
                    {
                        fill();
                    }
                };

                form.Controls.Add(list);
                form.Controls.Add(buttons);
                form.Controls.Add(info);
                ForzaTheme.Apply(form);
                fill();
                if (list.Items.Count == 0)
                {
                    info.Text += "\r\nNo mod pack is installed.";
                }
                form.ShowDialog(this);
            }
        }

        private static string Value(Dictionary<string, string> marker, string key)
        {
            string value;
            return marker.TryGetValue(key, out value) ? value : "";
        }

        private bool UninstallModPacks(IWin32Window owner, ListView.SelectedListViewItemCollection selected)
        {
            if (CarPortForm.XeniaRunning())
            {
                MessageBox.Show(owner, "Xenia is open and keeps the packages in use. Close Xenia, then Uninstall again.", "Mod Packs", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            List<string> files = new List<string>();
            List<string> cars = new List<string>();
            foreach (ListViewItem item in selected)
            {
                files.Add((string) item.Tag);
                cars.Add(item.Text);
            }
            // the car open in the editor must be closed first: its copy is deleted
            string open = (this.zip != null) ? this.zip.FilePath : null;
            foreach (string car in cars)
            {
                foreach (string copy in ModCarCopies(car))
                {
                    if (open != null && string.Equals(Path.GetFullPath(copy), SafeFullPath(open), StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show(owner, car + " is open in the editor. Close it first (File > Close), then Uninstall.", "Mod Packs", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return false;
                    }
                }
            }
            if (MessageBox.Show(owner, "Uninstall the mod pack of:\r\n\r\n" + string.Join("\r\n", cars.ToArray()) + "\r\n\r\nThe original car comes back in game. Your edits are lost (the mod pack is not backed up).",
                "Mod Packs", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            {
                return false;
            }
            List<string> failed = new List<string>();
            for (int i = 0; i < files.Count; i++)
            {
                try
                {
                    File.Delete(files[i]);
                    foreach (string copy in ModCarCopies(cars[i]))
                    {
                        modCars.Remove(Path.GetFullPath(copy));
                        File.Delete(copy);
                    }
                }
                catch (Exception exception)
                {
                    failed.Add(cars[i] + ": " + exception.Message);
                }
            }
            if (failed.Count > 0)
            {
                MessageBox.Show(owner, "Not uninstalled:\r\n\r\n" + string.Join("\r\n", failed.ToArray()), "Mod Packs", MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
            this.SetStatus(string.Format("Mod packs uninstalled: {0} of {1}. The original cars come back in game.", files.Count - failed.Count, files.Count));
            return true;
        }

        // The editor's copies of a car (Open Car Model of a game or package car) under %TEMP%\ForzaStudio\PackageCars.
        private static List<string> ModCarCopies(string media)
        {
            List<string> copies = new List<string>();
            if (!Directory.Exists(PackageCarsRoot))
            {
                return copies;
            }
            foreach (string folder in Directory.GetDirectories(PackageCarsRoot))
            {
                string copy = Path.Combine(folder, media + ".zip");
                if (File.Exists(copy))
                {
                    copies.Add(copy);
                }
            }
            return copies;
        }

        private static string SafeFullPath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (ArgumentException)
            {
                return path;   // a mounted path (stfs:...)
            }
        }
    }
}
