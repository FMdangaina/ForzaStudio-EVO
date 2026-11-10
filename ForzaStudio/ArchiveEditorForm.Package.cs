namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Windows.Forms;

    // Open DLC Package (ROADMAP topic 3 l): an Xbox 360 marketplace package (STFS LIVE / CON / PIRS, the
    // file the console keeps, named by its content id) is extracted to a work folder under
    // %TEMP%\ForzaStudio\DlcEdit\<file name> and shown like a game folder: its zips open with a
    // double-click (Back returns), every file can be viewed, replaced, added and edited. Each Save writes
    // the work folder and packs it again into the same package file with its own header
    // (StfsPackage.Rewrite); the original file is kept once in bak\ the first time.
    public partial class ArchiveEditorForm
    {
        private StfsReader package;
        private string packageFolder;
        private bool packageBackedUp;
        private bool packagePending;        // the work folder changed since the package was last written
        private bool packageOriginal;       // an original package (DlcCatalog): read-only, its cars change through mod packs

        private const string OriginalPackageNote = "This is an original DLC package (in the official DLC list): it is never written.\n\n"
            + "To change one of its cars: File > Base Game Database (or the car in the game's database), Open Car Model. "
            + "The car opens as a copy and Save writes a small mod pack in Xenia that replaces the car's files; the original stays as it is.";

        private static string PackageWorkRoot
        {
            get { return Path.Combine(Path.Combine(Path.GetTempPath(), "ForzaStudio"), "DlcEdit"); }
        }

        // True for the package's work folder and everything in it.
        private bool InPackage(string path)
        {
            if (this.packageFolder == null || path == null)
            {
                return false;
            }
            string full = Path.GetFullPath(path).TrimEnd('\\');
            return string.Equals(full, this.packageFolder, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(this.packageFolder + "\\", StringComparison.OrdinalIgnoreCase);
        }

        private void OpenPackageDialog()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Open DLC Package";
                dialog.Filter = "Xbox 360 package (CON, LIVE, PIRS)|*.*";
                if (ShowRemembering(dialog, this, "OpenPackage"))
                {
                    this.OpenPackage(dialog.FileName);
                }
            }
        }

        public void OpenPackage(string path)
        {
            if (!this.ConfirmDiscard() || !this.ConfirmPackageRebuild())
            {
                return;
            }
            string folder = Path.Combine(PackageWorkRoot, Path.GetFileName(path));
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                StfsReader opened = StfsReader.Open(path);
                this.SetStatus("Extracting " + Path.GetFileName(path) + "...");
                this.CloseArchiveCore();
                this.package = null;
                this.packageFolder = null;
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
                try
                {
                    opened.Extract(folder);
                }
                catch (Exception)
                {
                    if (Directory.Exists(folder))
                    {
                        Directory.Delete(folder, true);
                    }
                    throw;
                }
                this.package = opened;
                this.packageFolder = Path.GetFullPath(folder).TrimEnd('\\');
                this.packageBackedUp = false;
                this.packagePending = false;
                this.packageOriginal = IsOriginalPackage(path);
                this.bookmarks.Clear();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Open DLC Package", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
                this.UpdateTitle();
            }
            this.OpenFolder(this.packageFolder);
            if (this.zip != null)
            {
                this.SetStatus(string.Format("Opened DLC package {0} \"{1}\" ({2}, title {3:X8}, {4} files). {5}",
                    Path.GetFileName(this.package.Path), this.package.DisplayName, this.package.Magic.Trim(), this.package.TitleId, this.zip.Entries.Count,
                    this.packageOriginal ? "ORIGINAL package: read-only; File > Base Game Database > Open Car Model opens a car as a copy and Save writes a mod pack."
                        : "Double-click a zip to open it; Save packs the package again."));
            }
        }

        // The tree's root while the package's work folder is open.
        private string PackageRootLabel
        {
            get
            {
                return string.Format("{0} ({1})", (this.package.DisplayName.Length > 0) ? this.package.DisplayName : "DLC package", Path.GetFileName(this.package.Path));
            }
        }

        // Writes the package again from the work folder (after a save inside it).
        private bool RebuildPackage()
        {
            if (this.package == null)
            {
                return false;
            }
            if (this.packageOriginal)
            {
                this.packagePending = false;
                MessageBox.Show(this, OriginalPackageNote, "Rebuild DLC Package", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            string target = this.package.Path;
            string temp = target + ".new";
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                this.SetStatus("Packing " + Path.GetFileName(target) + "...");
                string backup = null;
                // The original once: not again at every opening when bak\ already holds a copy of this package
                // (FM4 disc 2 packages are 580-850 MB each).
                if (!this.packageBackedUp && File.Exists(target) && !PackageBackupExists(target))
                {
                    backup = BackupFile(target, Path.GetFileName(target), BackupStamp());
                }
                this.packageBackedUp = true;
                string contentId = StfsPackage.Rewrite(this.packageFolder, temp, this.package.Header);
                StfsReader check = StfsReader.Open(temp);
                if (File.Exists(target))
                {
                    File.Delete(target);
                }
                File.Move(temp, target);
                check.Path = target;
                this.package = check;
                this.packagePending = false;
                this.SetStatus(string.Format("DLC package {0} written ({1:N0} bytes, content id {2}){3}. The signature is not renewed: for Xenia and modded (RGH/JTAG) consoles.",
                    Path.GetFileName(target), new FileInfo(target).Length, contentId.Substring(0, 40),
                    (backup != null) ? ", original in bak\\" + Path.GetFileName(backup) : ""));
                return true;
            }
            catch (Exception exception)
            {
                if (File.Exists(temp))
                {
                    try
                    {
                        File.Delete(temp);
                    }
                    catch (IOException)
                    {
                    }
                }
                MessageBox.Show(this, "The files are saved in the work folder\n" + this.packageFolder + "\nbut the package was not written:\n\n" + exception.Message +
                    "\n\nFix it and save again (File > Rebuild DLC Package).", "Rebuild DLC Package", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return false;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
                this.UpdateTitle();
            }
        }

        private static bool PackageBackupExists(string package)
        {
            try
            {
                return Directory.Exists(BackupFolder) && Directory.GetFiles(BackupFolder, "*_bak_" + Path.GetFileName(package)).Length > 0;
            }
            catch (IOException)
            {
                return false;
            }
        }

        // File > Rebuild DLC Package: saves what is open, then packs.
        private void RebuildPackageCommand()
        {
            if (this.package == null)
            {
                return;
            }
            if (this.zip != null && this.InPackage(this.zip.FilePath) && (this.zip.IsModified || this.xmlDirty))
            {
                // Save packs it too.
                this.SaveArchive(false);
                return;
            }
            this.RebuildPackage();
        }

        // File > Delete Car from DLC Package: picks one of the pack's cars, shows what goes (DlcCarDelete), then edits the
        // pack's zips in the work folder and packs the package again.
        private void DeletePackageCar()
        {
            if (this.package == null)
            {
                return;
            }
            if (this.packageOriginal)
            {
                MessageBox.Show(this, OriginalPackageNote, "Delete Car from DLC Package", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (this.zip != null && (this.zip.IsModified || this.xmlDirty))
            {
                MessageBox.Show(this, "This window has changes not saved yet. Save or discard them first: deleting a car rewrites the pack's zips.", "Delete Car from DLC Package", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            List<string> cars;
            try
            {
                cars = DlcCarDelete.Cars(this.packageFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Delete Car from DLC Package", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (cars.Count == 0)
            {
                MessageBox.Show(this, "The package's merge database has no car.", "Delete Car from DLC Package", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            List<object[]> rows = new List<object[]>();
            foreach (string car in cars)
            {
                rows.Add(new object[] { car });
            }
            string media;
            using (PickDialog dialog = new PickDialog("Delete Car from DLC Package", new string[] { "Media name" }, new int[] { 380 }, rows, null))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                media = (string) dialog.SelectedKey;
            }
            DlcCarDelete delete = new DlcCarDelete();
            delete.PackageFolder = this.packageFolder;
            delete.MediaName = media;
            List<string> plan;
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                plan = delete.Plan();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Delete Car from DLC Package", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            string intro = "Delete " + media + " from the DLC package\n" + Path.GetFileName(this.package.Path) + "\n\nThis is what goes (only what belongs to this car and no other car of the package uses). The package is written again afterwards.";
            if (!ConfirmText("Delete Car from DLC Package", intro, string.Join("\r\n", plan.ToArray())))
            {
                return;
            }
            if (CarPortForm.XeniaRunning())
            {
                MessageBox.Show(this, "Close Xenia first: it keeps the package open.", "Delete Car from DLC Package", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                this.CloseArchiveCore();
                delete.Run();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Delete Car from DLC Package", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                this.OpenFolder(this.packageFolder);
                return;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            this.packagePending = true;
            bool written = this.RebuildPackage();
            this.OpenFolder(this.packageFolder);
            if (written)
            {
                this.SetStatus(media + " deleted from the DLC package and the package written.");
            }
        }

        // Before leaving a package whose last rebuild failed.
        private bool ConfirmPackageRebuild()
        {
            if (this.package == null || !this.packagePending)
            {
                return true;
            }
            DialogResult result = MessageBox.Show(this, "The DLC package " + Path.GetFileName(this.package.Path) + " was not written after the last changes. Write it now?",
                AppName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (result == DialogResult.Cancel)
            {
                return false;
            }
            return result == DialogResult.No || this.RebuildPackage();
        }
    }
}
