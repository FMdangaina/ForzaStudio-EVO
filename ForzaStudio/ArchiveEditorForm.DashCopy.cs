using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ForzaStudio
{
    public partial class ArchiveEditorForm
    {
        // Archive > Copy Dash From Another Car: the whole digital dash (dash_<car>.fbf/.bgf/.bsg and the pictures it
        // names, digitalgauge/<name>.xds) of another FM4-format car zip into the open car zip. The pictures in the .fbf
        // are addressed GAME:\MEDIA\CARS\<CAR>\DIGITALGAUGE\<NAME>.TGA, so the car name in those paths becomes this
        // car's; the pictures are copied under their own names (kept when this car already has one).
        private void CopyDashFromCar()
        {
            if (this.zip == null)
            {
                return;
            }
            string sourcePath;
            using (OpenFileDialog open = new OpenFileDialog())
            {
                open.Title = "Car zip whose digital dash to copy";
                open.Filter = "Car zip|*.zip|All files|*.*";
                if (!string.IsNullOrEmpty(this.zip.FilePath))
                {
                    string folder = this.zip.IsFolder ? Path.GetDirectoryName(this.zip.FilePath.TrimEnd('\\')) : Path.GetDirectoryName(this.zip.FilePath);
                    if (folder != null && Directory.Exists(folder))
                    {
                        open.InitialDirectory = folder;
                    }
                }
                if (open.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                sourcePath = open.FileName;
            }
            ForzaZip other;
            try
            {
                other = IsArchive(sourcePath) ? new ForzaZip(sourcePath) : ForzaZip.OpenFile(sourcePath);
            }
            catch (Exception ex)
            {
                this.SetStatus("Dash: " + ex.Message);
                return;
            }
            ForzaZip.Entry bgfEntry = null;
            foreach (ForzaZip.Entry e in other.Entries)
            {
                if (e.Name.EndsWith(".bgf", StringComparison.OrdinalIgnoreCase) && e.Name.IndexOf("dash_", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    bgfEntry = e;
                    break;
                }
            }
            if (bgfEntry == null)
            {
                this.SetStatus("Dash: " + Path.GetFileName(sourcePath) + " has no digital dash.");
                return;
            }
            string stem = bgfEntry.Name.Substring(0, bgfEntry.Name.Length - 4);
            ForzaZip.Entry fbfEntry = other.Find(stem + ".fbf"), bsgEntry = other.Find(stem + ".bsg");
            if (fbfEntry == null || bsgEntry == null)
            {
                this.SetStatus("Dash: the dash of " + Path.GetFileName(sourcePath) + " lacks its .fbf or .bsg.");
                return;
            }
            string sourceName = Path.GetFileName(stem).Substring("dash_".Length);

            string guess = this.zip.IsFolder ? Path.GetFileName(this.zip.FilePath.TrimEnd('\\')) : Path.GetFileNameWithoutExtension(this.zip.FilePath);
            string target;
            using (Form dialog = new Form())
            {
                dialog.Text = "Copy dash from " + sourceName;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MinimizeBox = dialog.MaximizeBox = false;
                dialog.ClientSize = new Size(432, 150);
                Label l1 = new Label();
                l1.Text = "Car name:";
                l1.SetBounds(12, 15, 70, 18);
                TextBox name = new TextBox();
                name.Text = guess;
                name.SetBounds(86, 12, 334, 22);
                Label note = new Label();
                note.Text = "The dash becomes dash_<name>.fbf/.bgf/.bsg in digitalgauge/ and its pictures are addressed to this car. Use the car's MediaName (the zip name).";
                note.SetBounds(12, 44, 408, 46);
                Button ok = new Button();
                ok.Text = "Copy";
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(246, 110, 84, 26);
                Button cancel = new Button();
                cancel.Text = "Cancel";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds(336, 110, 84, 26);
                dialog.Controls.AddRange(new Control[] { l1, name, note, ok, cancel });
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;
                ForzaTheme.Apply(dialog);
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                target = name.Text.Trim();
            }
            if (target.Length == 0 || target.IndexOfAny(new char[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|', ' ' }) >= 0)
            {
                this.SetStatus("Dash: give the car name without spaces or path characters.");
                return;
            }

            string prefix = "digitalgauge/dash_" + target.ToLowerInvariant();
            List<string> existing = new List<string>();
            foreach (string ext in new string[] { ".fbf", ".bgf", ".bsg" })
            {
                if (this.zip.Find(prefix + ext) != null)
                {
                    existing.Add(prefix + ext);
                }
            }
            if (existing.Count > 0 && MessageBox.Show(this, "This car already has a dash (" + string.Join(", ", existing.ToArray()) + ").\n\nReplace it?",
                "Copy Dash", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            AnarkFbf fbf;
            try
            {
                fbf = AnarkFbf.Read(other.Read(fbfEntry));
            }
            catch (Exception ex)
            {
                this.SetStatus("Dash: " + ex.Message);
                return;
            }
            if (fbf.IsFM3)
            {
                this.SetStatus("Dash: that is an FM3 dash; port the car instead (the port converts it).");
                return;
            }
            List<string> images = new List<string>();
            foreach (AnarkFbf.Record record in fbf.Records)
            {
                if (record.Type != AnarkFbf.RecordType.Image || record.Path == null)
                {
                    continue;
                }
                string path = record.Path.Replace('/', '\\');
                int at = path.ToUpperInvariant().IndexOf(@"\DIGITALGAUGE\", StringComparison.Ordinal);
                if (!path.StartsWith(@"GAME:\MEDIA\CARS\", StringComparison.OrdinalIgnoreCase) || at < 0)
                {
                    continue;
                }
                string image = Path.GetFileNameWithoutExtension(path);
                if (!images.Contains(image.ToLowerInvariant()))
                {
                    images.Add(image.ToLowerInvariant());
                }
                record.Path = @"GAME:\MEDIA\CARS\" + target.ToUpperInvariant() + path.Substring(at);
            }
            byte[] fbfData = fbf.Write();
            byte[] bgfData = other.Read(bgfEntry), bsgData = other.Read(bsgEntry);

            List<string> copied = new List<string>(), kept = new List<string>(), missing = new List<string>();
            try
            {
                this.PutDashFile(prefix + ".fbf", fbfData);
                this.PutDashFile(prefix + ".bgf", bgfData);
                this.PutDashFile(prefix + ".bsg", bsgData);
                foreach (string image in images)
                {
                    string entryName = "digitalgauge/" + image + ".xds";
                    ForzaZip.Entry picture = other.Find(entryName);
                    if (picture == null)
                    {
                        missing.Add(image);
                    }
                    else if (this.zip.Find(entryName) != null)
                    {
                        kept.Add(image);
                    }
                    else
                    {
                        this.zip.Add(entryName, other.Read(picture));
                        copied.Add(image);
                    }
                }
            }
            catch (Exception ex)
            {
                this.SetStatus("Dash copy failed: " + ex.Message);
                MessageBox.Show(this, ex.Message, "Copy Dash", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }

            // The source car's name left in the files (the .fbf names were rewritten; the .bgf and .bsg are copied as they are).
            List<string> leftovers = new List<string>();
            foreach (KeyValuePair<string, byte[]> file in new KeyValuePair<string, byte[]>[] {
                new KeyValuePair<string, byte[]>(".fbf", fbfData), new KeyValuePair<string, byte[]>(".bgf", bgfData), new KeyValuePair<string, byte[]>(".bsg", bsgData) })
            {
                if (ContainsAscii(file.Value, sourceName))
                {
                    leftovers.Add(file.Key);
                }
            }

            this.ClearPreviewCache();
            this.currentEntry = null;
            this.PopulateTree();
            this.SelectEntry(prefix + ".fbf");
            this.UpdateTitle();
            StringBuilder report = new StringBuilder();
            report.AppendFormat("Dash {0} -> {1}: .fbf/.bgf/.bsg, {2} pictures copied", sourceName, target, copied.Count);
            if (kept.Count > 0)
            {
                report.AppendFormat(", {0} already here (kept)", kept.Count);
            }
            if (missing.Count > 0)
            {
                report.AppendFormat(", NOT in the source: {0}", string.Join(", ", missing.ToArray()));
            }
            if (leftovers.Count > 0)
            {
                report.AppendFormat("; the name {0} is still inside {1}", sourceName, string.Join(", ", leftovers.ToArray()));
            }
            report.Append(" - save to write it");
            this.SetStatus(report.ToString());
        }

        private void PutDashFile(string name, byte[] data)
        {
            ForzaZip.Entry entry = this.zip.Find(name);
            if (entry != null)
            {
                this.zip.Replace(entry, data);
            }
            else
            {
                this.zip.Add(name, data);
            }
        }

        private static bool ContainsAscii(byte[] data, string text)
        {
            byte[] needle = Encoding.ASCII.GetBytes(text.ToLowerInvariant());
            for (int i = 0; i + needle.Length <= data.Length; i++)
            {
                int j = 0;
                while (j < needle.Length && ((data[i + j] >= (byte)'A' && data[i + j] <= (byte)'Z') ? data[i + j] + 32 : data[i + j]) == needle[j])
                {
                    j++;
                }
                if (j == needle.Length)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
