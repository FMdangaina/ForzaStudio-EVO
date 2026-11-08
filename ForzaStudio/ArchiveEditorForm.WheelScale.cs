namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Windows.Forms;

    // Wheel archives (Media\wheels\<name>.zip): does the rim follow the database rim size (WheelScale)?
    // Checked when the archive opens; a wheel modelled at its true size can be made scalable
    // (Model > Make Rim Scalable), as the FM2 port does. The change is pending until Save.
    public partial class ArchiveEditorForm
    {
        // The wheel models of the open archive with their status (tire zips TireA/B/C are not rims).
        private List<KeyValuePair<ForzaZip.Entry, WheelScale.Status>> WheelModels()
        {
            List<KeyValuePair<ForzaZip.Entry, WheelScale.Status>> found = new List<KeyValuePair<ForzaZip.Entry, WheelScale.Status>>();
            if (this.zip == null || this.zip.IsFolder || Path.GetFileName(this.zip.FilePath ?? "").StartsWith("Tire", StringComparison.OrdinalIgnoreCase))
            {
                return found;
            }
            foreach (ForzaZip.Entry entry in this.zip.Entries)
            {
                if (!entry.Name.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                try
                {
                    WheelScale.Status status = WheelScale.Check(new CarbinModel(this.zip.Read(entry)));
                    if (status.IsWheel)
                    {
                        found.Add(new KeyValuePair<ForzaZip.Entry, WheelScale.Status>(entry, status));
                    }
                }
                catch (Exception)
                {
                    // Not a model this check reads: Check Model Format reports it.
                }
            }
            return found;
        }

        // After an archive opens: a wheel that is not scalable is reported and the conversion offered.
        // Only a wheel archive (its folder is "wheels"): a car zip that holds wheel models is not asked.
        private void CheckWheelScaleOnOpen()
        {
            if (this.zip == null || this.zip.IsFolder
                || !string.Equals(Path.GetFileName(Path.GetDirectoryName(this.zip.FilePath ?? "") ?? ""), "wheels", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            List<KeyValuePair<ForzaZip.Entry, WheelScale.Status>> wheels = this.WheelModels();
            if (wheels.Count == 0)
            {
                return;
            }
            StringBuilder fixedSize = new StringBuilder();
            foreach (KeyValuePair<ForzaZip.Entry, WheelScale.Status> wheel in wheels)
            {
                if (!wheel.Value.Scalable)
                {
                    fixedSize.AppendLine(wheel.Key.Name + ": " + wheel.Value);
                }
            }
            if (fixedSize.Length == 0)
            {
                this.SetStatus(string.Format("Opened {0}: rim scalable ({1})", this.SourceName, wheels[0].Value));
                return;
            }
            this.SetStatus("Opened " + this.SourceName + ": rim NOT scalable (Model > Make Rim Scalable)");
            if (MessageBox.Show(this, fixedSize.ToString() + "\nThe game draws this rim at its modelled size whatever rim size the database gives the car.\n\n"
                + "Make it scalable now? LOD 0 becomes the 15\" reference of FM4 with a morph table, as the FM2 port does; LODs 1-5 are not changed. "
                + "The change is pending until you save.", "Wheel Rim Size", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                this.MakeRimScalable(false);
            }
        }

        // Model > Make Rim Scalable: every wheel model of the archive that is not scalable yet.
        private void MakeRimScalable(bool ask)
        {
            List<KeyValuePair<ForzaZip.Entry, WheelScale.Status>> wheels = this.WheelModels();
            if (wheels.Count == 0)
            {
                MessageBox.Show(this, "Open a wheel archive (Media\\wheels\\<name>.zip) first.", "Make Rim Scalable", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            List<ForzaZip.Entry> todo = new List<ForzaZip.Entry>();
            StringBuilder text = new StringBuilder();
            foreach (KeyValuePair<ForzaZip.Entry, WheelScale.Status> wheel in wheels)
            {
                if (wheel.Value.Scalable)
                {
                    text.AppendLine(wheel.Key.Name + ": already " + wheel.Value);
                }
                else
                {
                    todo.Add(wheel.Key);
                }
            }
            if (todo.Count == 0)
            {
                MessageBox.Show(this, text.ToString(), "Make Rim Scalable", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (ask && MessageBox.Show(this, "LOD 0 of " + todo.Count + " wheel model(s) becomes the 15\" reference of FM4 with a morph table, so the game draws the rim at the car's database rim size. "
                + "LODs 1-5 are not changed. Pending until you save.\n\nContinue?", "Make Rim Scalable", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                foreach (ForzaZip.Entry entry in todo)
                {
                    CarbinModel model = new CarbinModel(this.zip.Read(entry));
                    string line = WheelScale.MakeScalable(model);
                    byte[] data = model.ToBytes();
                    WheelScale.Status after = WheelScale.Check(new CarbinModel(data));
                    if (!after.Scalable)
                    {
                        throw new InvalidOperationException(entry.Name + ": still not scalable after the conversion (" + after + ")");
                    }
                    this.zip.Replace(entry, data);
                    this.RefreshEntryNode(entry);
                    text.AppendLine(entry.Name + ": " + line);
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Make Rim Scalable", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            if (this.currentEntry != null && todo.Contains(this.currentEntry))
            {
                this.editSection = null;
                this.ReloadModelKeepingView();
            }
            this.UpdateTitle();
            this.SetStatus("Rim made scalable (pending: Save to write the archive)");
            this.ShowReport("Make Rim Scalable", text.ToString());
        }
    }
}
