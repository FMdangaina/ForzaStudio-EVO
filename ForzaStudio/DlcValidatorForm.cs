namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.IO;
    using System.Text;
    using System.Windows.Forms;

    // Tools > Check and Validate DLC: pick a package installed in Xenia's content folder (any game), or browse for a
    // package file or an extracted folder, and run DlcValidator on it. Nothing is written.
    internal sealed class DlcValidatorForm : Form
    {
        private sealed class Item
        {
            public string Path;
            public string Text;

            public override string ToString()
            {
                return this.Text;
            }
        }

        private readonly ListBox list = new ListBox();
        private readonly TextBox output = new TextBox();
        private readonly CheckBox deep = new CheckBox();
        private readonly Label status = new Label();
        private readonly Button run = new Button();
        private readonly Button fix = new Button();
        private string lastPath;
        private DlcValidator.Report lastReport;

        public DlcValidatorForm()
        {
            this.AutoScaleMode = AutoScaleMode.None;
            this.Text = "Check and validate DLC";
            this.ClientSize = new Size(1000, 640);
            this.MinimumSize = new Size(760, 480);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;

            Label head = new Label();
            head.Text = "Packages installed (Xenia content folder), Ctrl/Shift for several:";
            head.SetBounds(12, 10, 460, 20);
            this.list.SetBounds(12, 34, 400, 520);
            this.list.SelectionMode = SelectionMode.MultiExtended;
            this.list.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom;
            this.FillList();

            Button browseFile = new Button();
            browseFile.Text = "Package file...";
            browseFile.SetBounds(12, 562, 120, 28);
            browseFile.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            browseFile.Click += delegate { this.BrowseFile(); };
            Button browseFolder = new Button();
            browseFolder.Text = "Folder...";
            browseFolder.SetBounds(140, 562, 100, 28);
            browseFolder.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            browseFolder.Click += delegate { this.BrowseFolder(); };

            this.deep.Text = "Deep check (unpack every file, slower)";
            this.deep.SetBounds(12, 598, 300, 24);
            this.deep.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;

            this.run.Text = "Validate";
            this.run.SetBounds(248, 562, 80, 28);
            this.run.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            this.run.Click += delegate { this.ValidateSelected(); };
            this.fix.Text = "Fix...";
            this.fix.Enabled = false;
            this.fix.SetBounds(336, 562, 76, 28);
            this.fix.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            this.fix.Click += delegate { this.FixLast(); };

            this.output.Multiline = true;
            this.output.ReadOnly = true;
            this.output.ScrollBars = ScrollBars.Both;
            this.output.WordWrap = false;
            this.output.Font = new Font("Consolas", 9f);
            this.output.SetBounds(424, 34, 564, 556);
            this.output.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            this.status.SetBounds(424, 598, 480, 24);
            this.status.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            Button close = new Button();
            close.Text = "Close";
            close.DialogResult = DialogResult.Cancel;
            close.SetBounds(910, 596, 78, 28);
            close.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            this.CancelButton = close;
            this.Controls.AddRange(new Control[] { head, this.list, browseFile, browseFolder, this.run, this.fix, this.deep, this.output, this.status, close });
            ForzaTheme.Apply(this);
        }

        private void FillList()
        {
            foreach (ForzaGame game in new ForzaGame[] { ForzaGame.Motorsport4, ForzaGame.Horizon1, ForzaGame.Horizon2, ForzaGame.Motorsport3 })
            {
                string folder = InstalledDlc.ContentFolder(game);
                if (folder == null)
                {
                    continue;
                }
                List<string> paths = new List<string>(Directory.GetFileSystemEntries(folder));
                paths.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string path in paths)
                {
                    bool isFolder = Directory.Exists(path);
                    if (!isFolder && !StfsReader.IsPackage(path))
                    {
                        continue;
                    }
                    Item item = new Item();
                    item.Path = path;
                    item.Text = GameInfo.Tag(game) + "  " + Path.GetFileName(path) + (isFolder ? "  (folder)" : "");
                    this.list.Items.Add(item);
                }
            }
            if (this.list.Items.Count == 0)
            {
                this.status.Text = "No installed package found: set the Xenia folder in the port window, or browse.";
            }
        }

        private void BrowseFile()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "DLC package (STFS file)";
                dialog.Filter = "All files|*";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    this.Run(new string[] { dialog.FileName });
                }
            }
        }

        private void BrowseFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "An extracted DLC package (the folder with Media\\DLCZips)";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    this.Run(new string[] { dialog.SelectedPath });
                }
            }
        }

        private void ValidateSelected()
        {
            List<string> paths = new List<string>();
            foreach (object selected in this.list.SelectedItems)
            {
                paths.Add(((Item) selected).Path);
            }
            if (paths.Count == 0)
            {
                this.status.Text = "Select a package in the list, or use Package file / Folder.";
                return;
            }
            this.Run(paths.ToArray());
        }

        private void Run(string[] paths)
        {
            StringBuilder text = new StringBuilder();
            int errors = 0;
            DlcValidator.Report last = null;
            int warnings = 0;
            this.run.Enabled = false;
            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                foreach (string path in paths)
                {
                    this.status.Text = "Checking " + Path.GetFileName(path) + "...";
                    this.status.Refresh();
                    DlcValidator.Report report = DlcValidator.Validate(path, this.deep.Checked);
                    last = report;
                    errors += report.Count(DlcValidator.Error);
                    warnings += report.Count(DlcValidator.Warning);
                    text.AppendLine(report.Text());
                }
            }
            finally
            {
                this.Cursor = previous;
                this.run.Enabled = true;
            }
            this.output.Text = text.ToString();
            this.status.Text = string.Format("{0} package(s): {1} error(s), {2} warning(s).", paths.Length, errors, warnings);
            this.lastPath = (paths.Length == 1) ? paths[0] : null;
            this.lastReport = (paths.Length == 1) ? last : null;
            this.fix.Enabled = this.lastPath != null && this.lastReport != null && this.lastReport.Game != ForzaGame.Unknown;
        }

        // Fix...: what can be changed in the package just checked, ticked off by the user, then checked again.
        private void FixLast()
        {
            if (this.lastPath == null)
            {
                return;
            }
            if (!XeniaGuard.Confirm(this, "Fix DLC package", "this DLC package"))
            {
                return;
            }
            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            DlcFixer fixer = null;
            try
            {
                this.status.Text = "Looking for what can be fixed...";
                this.status.Refresh();
                fixer = new DlcFixer(this.lastPath);
                fixer.Analyze(this.lastReport);
            }
            catch (Exception e)
            {
                this.Cursor = previous;
                if (fixer != null)
                {
                    fixer.Dispose();
                }
                MessageBox.Show(this, "The package cannot be analysed: " + e.Message, "Fix DLC package", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            this.Cursor = previous;
            try
            {
                List<DlcFixer.Item> chosen;
                using (DlcFixForm form = new DlcFixForm(fixer))
                {
                    if (form.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }
                    chosen = form.Chosen;
                }
                if (chosen.Count == 0)
                {
                    return;
                }
                this.Cursor = Cursors.WaitCursor;
                this.status.Text = "Fixing...";
                this.status.Refresh();
                string backup = fixer.Apply(chosen);
                this.Cursor = previous;
                string log = fixer.Log.ToString();
                this.Run(new string[] { this.lastPath });
                this.output.Text = "FIX DONE" + ((backup != null) ? " (original kept: " + backup + ")" : " (no backup: Backup is off in the ini)") + "\r\n" + log.Replace("\n", "\r\n") + "\r\nNew check:\r\n" + this.output.Text;
            }
            catch (Exception e)
            {
                this.Cursor = previous;
                MessageBox.Show(this, "The fix failed, the package is as it was (or its backup is in bak\\): " + e.Message, "Fix DLC package", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.Cursor = previous;
                fixer.Dispose();
            }
        }
    }
}
