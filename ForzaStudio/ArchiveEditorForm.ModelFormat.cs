namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Windows.Forms;

    // Tools > Check Model Format: every .carbin of the open archive against the FM4 format as the game reads it
    // (CarbinValidator, traced in default.xex). Read-only; pending edits are checked as they are in the archive.
    public partial class ArchiveEditorForm
    {
        private void ShowModelFormatCheck()
        {
            if (this.zip == null)
            {
                MessageBox.Show(this, "Open a car, wheel or brake archive first.", "Check Model Format", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            StringBuilder text = new StringBuilder();
            int models = 0;
            int errors = 0;
            int warnings = 0;
            foreach (ForzaZip.Entry entry in this.zip.Entries)
            {
                if (!entry.Name.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                CarbinModel model;
                try
                {
                    model = new CarbinModel(this.zip.Read(entry));
                }
                catch (Exception e)
                {
                    text.AppendLine(entry.Name + ": not checked (" + e.Message + ")");
                    continue;
                }
                if (model.IsHorizon)
                {
                    text.AppendLine(entry.Name + ": FH format, not checked (FM4 models only)");
                    continue;
                }
                models++;
                List<CarbinValidator.Finding> found = CarbinValidator.Check(model, entry.Name);
                if (found.Count == 0)
                {
                    text.AppendLine(entry.Name + ": OK");
                    continue;
                }
                text.AppendLine(entry.Name + ":");
                foreach (CarbinValidator.Finding finding in found)
                {
                    text.AppendLine("    " + finding);
                    errors += finding.Error ? 1 : 0;
                    warnings += finding.Error ? 0 : 1;
                }
            }
            string head = string.Format("{0} models checked: {1} errors, {2} warnings.\r\n"
                + "Checked: object versions, header layout (emitters, driver controls, LODs, gauges), glass particles, "
                + "morph tables against HasMorphs and the vertex counts, morph words, mesh fields and indices. "
                + "Values that every official FM4 model has; a warning is a difference, not always a fault.\r\n\r\n", models, errors, warnings);
            this.ShowReport("Check Model Format - " + System.IO.Path.GetFileName(this.zip.FilePath ?? ""), head + text.ToString());
        }
    }
}
