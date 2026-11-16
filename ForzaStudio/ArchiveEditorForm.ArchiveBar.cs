using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ForzaStudio
{
    // Step E.2 of docs/UI_WORKFLOW.md: the Archive / Model / Texture menus are gone, their commands are where they
    // are used. Archive: a strip of buttons over the file tree (and the tree's right-click menu, as before).
    // Model: the "Import / export model" section of the PART tab. Make Rim Scalable: a button of the PART tab, shown for a
    // wheel model. Export UV Map: in the TEXTURE tab. Texture Export / Import Image: already on the texture page.
    public partial class ArchiveEditorForm
    {
        private ToolStrip archiveStrip;
        private ToolStripButton barAddFile, barNewFolder, barExtract, barReplace, barRevert, barDelete, barUndoDelete, barExtractAll;
        private Button btnRimScalable;
        private ForzaZip.Entry rimCheckedEntry;

        private static ToolStripButton BarButton(string text, string hint, EventHandler click)
        {
            ToolStripButton button = new ToolStripButton(text);
            button.DisplayStyle = ToolStripItemDisplayStyle.Text;
            button.AutoToolTip = false;
            button.ToolTipText = hint;
            button.Margin = new Padding(2, 1, 2, 2);
            button.Click += click;
            return button;
        }

        // A second bar under the top one, as wide as the window (user 2026-11-17: "sdoppia la barra sopra"; the strip
        // and then the grid over the file tree were rejected as cramped). The file commands in three groups: add,
        // the selected file, delete / extract everything. Added to the form by the constructor, right under the menu.
        private void BuildArchiveStrip()
        {
            ToolStrip strip = new ToolStrip();
            strip.GripStyle = ToolStripGripStyle.Hidden;
            strip.Dock = DockStyle.Top;
            strip.ShowItemToolTips = true;
            strip.Renderer = ForzaTheme.Renderer;
            strip.Padding = new Padding(6, 1, 6, 1);
            ToolStripLabel caption = new ToolStripLabel("FILES");
            caption.Font = ForzaTheme.Bold;
            caption.ForeColor = ForzaTheme.TextDim;
            caption.Margin = new Padding(2, 1, 10, 2);
            this.barAddFile = BarButton("Add File", "Add a file to the archive, in the selected folder  (Ctrl+Shift+A with the file tree active)", delegate { this.AddFileToArchive(); });
            this.barNewFolder = BarButton("New Folder", "New folder in the archive", delegate { this.NewArchiveFolder(); });
            this.barExtract = BarButton("Extract", "Extract the selected file", delegate { this.ExtractSelected(); });
            this.barReplace = BarButton("Replace", "Replace the selected file with one from disk", delegate { this.ReplaceSelected(); });
            this.barRevert = BarButton("Revert", "Revert the selected file to the saved one (an added file: remove it)", delegate { this.RevertSelected(); });
            this.barDelete = BarButton("Delete", "Delete the selected file or folder  (Del)", delegate { this.DeleteSelected(); });
            this.barUndoDelete = BarButton("Undo Delete", "Bring back the selected deleted file or folder", delegate { this.UndoDeleteSelected(); });
            this.barExtractAll = BarButton("Extract All", "Extract every file of the archive to a folder", delegate { this.ExtractAll(); });
            strip.Items.AddRange(new ToolStripItem[]
            {
                caption, this.barAddFile, this.barNewFolder, new ToolStripSeparator(),
                this.barExtract, this.barReplace, this.barRevert, new ToolStripSeparator(),
                this.barDelete, this.barUndoDelete, new ToolStripSeparator(), this.barExtractAll
            });
            strip.MouseEnter += delegate { this.UpdateArchiveBar(); };
            this.tvArchive.AfterSelect += delegate { this.UpdateArchiveBar(); };
            this.archiveStrip = strip;
            this.UpdateArchiveBar();
        }

        // What applies to the selection is enabled (as the Archive menu did when it opened). Called on a selection
        // change, with the title (every change of the archive) and when the pointer comes over the strip.
        private void UpdateArchiveBar()
        {
            if (this.archiveStrip == null)
            {
                return;
            }
            bool open = this.zip != null;
            ForzaZip.Entry entry = open ? this.SelectedEntry : null;
            this.barAddFile.Enabled = open;
            this.barNewFolder.Enabled = open;
            this.barExtract.Enabled = entry != null;
            this.barReplace.Enabled = entry != null;
            this.barRevert.Enabled = entry != null && entry.IsModified;
            this.barRevert.Text = (entry != null && entry.IsAdded) ? "Remove" : "Revert";
            this.barDelete.Enabled = open && this.CanDeleteSelection;
            this.barUndoDelete.Enabled = open && this.CanUndoDeleteSelection;
            this.barExtractAll.Enabled = open;
            this.UpdateRimButton();
        }

        // Ctrl+Shift+A adds a file only with the file tree active: in the viewer the same key belongs to the MESH tab
        // (select none).
        private bool HandleArchiveKey(Keys keyData)
        {
            if (keyData != (Keys.Control | Keys.Shift | Keys.A) || this.zip == null || this.tvArchive == null || !this.tvArchive.Focused)
            {
                return false;
            }
            this.AddFileToArchive();
            return true;
        }

        // PART tab, right under the header line: only with a wheel model open. Pressed by the user only: the rim is
        // never made scalable, nor the question asked, automatically (USER RULE 2026-11-17).
        private Button BuildRimScalableButton()
        {
            this.btnRimScalable = SideWideButton("Make Rim Scalable", delegate { this.MakeRimScalable(false); });
            this.btnRimScalable.Visible = false;
            this.partTips.SetToolTip(this.btnRimScalable, "Wheel models of this archive that are not scalable: LOD 0 becomes the 15\" reference of FM4 with a morph table, so the game draws the rim at the car's database rim size. LODs 1-5 are not changed. Pending until Save; Ctrl+Z undoes it.");
            return this.btnRimScalable;
        }

        // One check per model shown: is it a wheel (WheelScale.Check)? Tire zips (TireA/B/C) are not rims.
        private void UpdateRimButton()
        {
            if (this.btnRimScalable == null || this.rimCheckedEntry == this.currentEntry)
            {
                return;
            }
            this.rimCheckedEntry = this.currentEntry;
            bool wheel = false;
            try
            {
                if (this.zip != null && !this.zip.IsFolder && this.currentEntry != null
                    && this.currentEntry.Name.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase)
                    && !Path.GetFileName(this.zip.FilePath ?? "").StartsWith("Tire", StringComparison.OrdinalIgnoreCase))
                {
                    wheel = WheelScale.Check(new CarbinModel(this.zip.Read(this.currentEntry))).IsWheel;
                }
            }
            catch (Exception)
            {
                // not a model this check reads: no button
            }
            this.btnRimScalable.Visible = wheel;
        }
    }
}
