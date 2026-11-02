namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Windows.Forms;

    // Tools > Validate DLC for Installation: pick a DLC package and Xenia's content folder; the package is compared with the game
    // and with every other package in that folder, the ids / names / files / strings that clash are changed in a copy (DlcRelocate,
    // DlcFixer.Relocate / MoveStrings), and Install writes the result. Only the package to install is ever changed; the original
    // goes to bak\ when the package is replaced in place.
    internal sealed class DlcInstallForm : Form
    {
        private readonly TextBox package = new TextBox();
        private readonly TextBox content = new TextBox();
        private readonly Label game = new Label();
        private readonly TextBox report = new TextBox();
        private readonly Button check = new Button();
        private readonly Button install = new Button();
        private readonly Label status = new Label();
        private DlcWorld world;
        private DlcFixer fixer;

        public DlcInstallForm()
        {
            this.AutoScaleMode = AutoScaleMode.None;
            this.Text = "Validate DLC for Installation";
            this.ClientSize = new Size(960, 620);
            this.MinimumSize = new Size(760, 480);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;

            Label head = new Label();
            head.Text = "The package is checked against the game and the other packages of the folder. Ids, car names, thumbnails, offer and strings that clash are changed in the package to install, nothing else is touched.";
            head.SetBounds(12, 10, 936, 34);

            Label packageLabel = new Label();
            packageLabel.Text = "DLC package:";
            packageLabel.SetBounds(12, 52, 110, 20);
            this.package.SetBounds(128, 50, 700, 24);
            this.package.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Button packageBrowse = new Button();
            packageBrowse.Text = "Browse...";
            packageBrowse.SetBounds(836, 48, 112, 28);
            packageBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            packageBrowse.Click += delegate { this.BrowsePackage(); };

            Label contentLabel = new Label();
            contentLabel.Text = "Xenia content:";
            contentLabel.SetBounds(12, 86, 110, 20);
            this.content.SetBounds(128, 84, 700, 24);
            this.content.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            string xenia = AppSettings.Get(SettingsForm.XeniaKey, "");
            this.content.Text = (xenia.Length > 0 && Directory.Exists(xenia)) ? (DlcPackage.XeniaContent(xenia) ?? "") : "";
            Button contentBrowse = new Button();
            contentBrowse.Text = "Browse...";
            contentBrowse.SetBounds(836, 82, 112, 28);
            contentBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            contentBrowse.Click += delegate { this.BrowseContent(); };

            this.game.Text = "The game folder is the package's own game (FM4: the port target, FM3: Tools > Settings > Folders).";
            this.game.SetBounds(128, 116, 820, 20);

            this.check.Text = "Check";
            this.check.SetBounds(128, 144, 110, 30);
            this.check.Click += delegate { this.Check(); };
            this.install.Text = "Install";
            this.install.SetBounds(246, 144, 110, 30);
            this.install.Enabled = false;
            this.install.Click += delegate { this.Install(); };
            this.status.SetBounds(366, 150, 580, 20);
            this.status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            this.report.Multiline = true;
            this.report.ReadOnly = true;
            this.report.ScrollBars = ScrollBars.Both;
            this.report.WordWrap = false;
            this.report.Font = new Font("Consolas", 9f);
            this.report.SetBounds(12, 184, 936, 424);
            this.report.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

            this.Controls.AddRange(new Control[] { head, packageLabel, this.package, packageBrowse, contentLabel, this.content, contentBrowse, this.game, this.check, this.install, this.status, this.report });
            ForzaTheme.Apply(this);
            this.FormClosed += delegate { this.Release(); };
        }

        private void Release()
        {
            if (this.fixer != null)
            {
                this.fixer.Dispose();
                this.fixer = null;
            }
            if (this.world != null)
            {
                this.world.Dispose();
                this.world = null;
            }
            this.install.Enabled = false;
        }

        private void BrowsePackage()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "DLC package to install (Xbox 360 STFS file)";
                dialog.Filter = "All files|*.*";
                if (this.content.Text.Length > 0 && Directory.Exists(this.content.Text))
                {
                    dialog.InitialDirectory = this.content.Text;
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    this.package.Text = dialog.FileName;
                    this.Release();
                }
            }
        }

        private void BrowseContent()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Xenia's content folder of Forza 4 (...\\4D530910\\00000002)";
                if (this.content.Text.Length > 0 && Directory.Exists(this.content.Text))
                {
                    dialog.SelectedPath = this.content.Text;
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    this.content.Text = dialog.SelectedPath;
                    this.Release();
                }
            }
        }

        private void Check()
        {
            this.Release();
            string path = this.package.Text.Trim();
            string folder = this.content.Text.Trim();
            if (!File.Exists(path) || !StfsReader.IsPackage(path))
            {
                MessageBox.Show(this, "Choose a DLC package file (an Xbox 360 STFS package).", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ForzaGame packageGame = GameInfo.FromTitleId(StfsReader.Open(path).TitleId);
            if (packageGame != ForzaGame.Motorsport4 && packageGame != ForzaGame.Motorsport3)
            {
                MessageBox.Show(this, "This package is not for Forza Motorsport 4 or 3.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string baseFolder = GameInfo.ConfiguredFolder(packageGame);
            if (baseFolder == null)
            {
                MessageBox.Show(this, "Set the " + GameInfo.Name(packageGame) + " folder in Tools > Settings > Folders first: the game's database is what the package is checked against.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            this.game.Text = GameInfo.Name(packageGame) + " folder: " + baseFolder;
            // The folder of the packages already installed is the package's own game's: FM4's content folder would be the wrong "world" for an FM3 pack.
            string title = (packageGame == ForzaGame.Motorsport3) ? "4D53084D" : "4D530910";
            if (folder.Length == 0 || folder.IndexOf(title, StringComparison.OrdinalIgnoreCase) < 0)
            {
                string xenia = AppSettings.Get(SettingsForm.XeniaKey, "");
                string own = (xenia.Length > 0 && Directory.Exists(xenia)) ? DlcPackage.XeniaContent(xenia, title) : null;
                if (own != null)
                {
                    folder = own;
                    this.content.Text = own;
                }
            }
            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            this.status.Text = "Reading the game and the installed packages...";
            this.Refresh();
            StringBuilder text = new StringBuilder();
            try
            {
                this.world = DlcWorld.Load(folder, baseFolder, path);
                text.AppendLine("Compared with the game and " + (this.world.Packages.Count) + " installed package(s):");
                foreach (string pack in this.world.Packages)
                {
                    text.AppendLine("  " + pack);
                }
                foreach (string skipped in this.world.Skipped)
                {
                    text.AppendLine("  not read: " + skipped);
                }
                text.AppendLine();
                this.status.Text = "Extracting and checking the package...";
                this.Refresh();
                this.fixer = new DlcFixer(path);
                this.fixer.Relocate(this.world);
                this.fixer.RepairMakes(this.world);
                this.fixer.RekeyStrings(folder);
                this.fixer.MoveStrings(this.world);
                this.fixer.DropRedundantTables(folder, baseFolder);
                text.Append(this.fixer.Log.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n"));
                DbIntegrity.Result merged = DbIntegrity.CheckMerge(this.world.GameDb, this.world.MergeFiles, this.fixer.MergeBytes(), packageGame);
                text.AppendLine();
                text.AppendLine(merged.Ok ? "The database of the changed package merges into the game and the installed packages: references and rules OK." : "THE DATABASE STILL HAS PROBLEMS:");
                foreach (string error in merged.Errors)
                {
                    text.AppendLine("  ERROR " + error);
                }
                foreach (string warning in merged.Warnings)
                {
                    text.AppendLine("  warning " + warning);
                }
                this.install.Enabled = merged.Ok;
                this.status.Text = merged.Ok ? "Checked. Install writes the changed package." : "Checked: errors remain, not installable.";
            }
            catch (Exception e)
            {
                text.AppendLine("ERROR " + e.Message);
                this.status.Text = "Failed.";
                this.Release();
            }
            finally
            {
                this.Cursor = previous;
            }
            this.report.Text = text.ToString();
        }

        private void Install()
        {
            string path = this.package.Text.Trim();
            string folder = this.content.Text.Trim();
            if (this.fixer == null || !Directory.Exists(folder))
            {
                return;
            }
            foreach (Process process in Process.GetProcesses())
            {
                if (process.ProcessName.StartsWith("xenia", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(this, "Xenia is running. Close it first, then press Install again.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            string full = Path.GetFullPath(path);
            bool inside = full.StartsWith(Path.GetFullPath(folder).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
            string output = inside ? full : Path.Combine(folder, Path.GetFileName(full));
            if (!inside && File.Exists(output))
            {
                MessageBox.Show(this, "The content folder already has a file called " + Path.GetFileName(output) + ". Rename the package first.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string question = inside
                ? "The package " + Path.GetFileName(full) + " will be replaced by the changed one." + (BackupPolicy.Enabled ? " The original is kept in bak\\." : " Backups are off in the settings: the original is NOT kept.") + "\n\nContinue?"
                : "The changed package will be written to\n" + output + "\n\nThe original file is not changed. Continue?";
            if (MessageBox.Show(this, question, this.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                if (inside && BackupPolicy.Enabled)
                {
                    string bak = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bak");
                    Directory.CreateDirectory(bak);
                    string copy = Path.Combine(bak, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + "_bak_" + Path.GetFileName(full));
                    File.Copy(full, copy, true);
                    this.report.AppendText("original kept: " + copy + "\r\n");
                }
                this.fixer.SaveTo(output);
                this.report.AppendText("\r\nInstalled: " + output + "\r\n");
                this.status.Text = "Installed.";
                this.Release();
            }
            catch (Exception e)
            {
                this.report.AppendText("\r\nERROR " + e.Message + "\r\n");
                this.status.Text = "Install failed.";
            }
            finally
            {
                this.Cursor = previous;
            }
        }
    }
}
