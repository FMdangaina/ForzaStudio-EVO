namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Windows.Forms;

    // Undo / Redo (user 2026-11-16: "tasto indietro e ripristina"), Ctrl+Z / Ctrl+Y (also Ctrl+Shift+Z) and two
    // buttons on the top bar (no drop-down menu).
    // Two levels: while a part's move / rotate / scale fields are still a preview, Undo / Redo step through the field
    // values; otherwise they step through the archive's history (ForzaZip.History: every pending change of every
    // file, one step per user action, closed when the application goes idle). Nothing is written to disk: Save does.
    public partial class ArchiveEditorForm
    {
        private ToolStripMenuItem mnuUndoBar;
        private ToolStripMenuItem mnuRedoBar;
        private ForzaZip hookedZip;

        // Field values (move xyz, rotate xyz, scale xyz) before each change of the part in edit.
        private readonly List<decimal[]> fieldUndo = new List<decimal[]>();
        private readonly List<decimal[]> fieldRedo = new List<decimal[]>();
        private decimal[] fieldCurrent;
        private NumericUpDown lastFieldControl;
        private DateTime lastFieldChange;

        private void AddUndoRedoBar()
        {
            this.mnuUndoBar = new ToolStripMenuItem("↶ Undo", null, delegate { this.UndoEdit(); });
            this.mnuRedoBar = new ToolStripMenuItem("Redo ↷", null, delegate { this.RedoEdit(); });
            this.mnuUndoBar.ToolTipText = "Undo (Ctrl+Z)";
            this.mnuRedoBar.ToolTipText = "Redo (Ctrl+Y)";
            this.mnuUndoBar.Enabled = false;
            this.mnuRedoBar.Enabled = false;
            this.menu.Items.Add(this.mnuUndoBar);
            this.menu.Items.Add(this.mnuRedoBar);
            Application.Idle += this.OnIdleHistory;
            this.FormClosed += delegate { Application.Idle -= this.OnIdleHistory; };
        }

        // The user's action is over: the archive changes it made become one undo step (named after the status text).
        private void OnIdleHistory(object sender, EventArgs e)
        {
            if (this.hookedZip != this.zip)
            {
                if (this.hookedZip != null)
                {
                    this.hookedZip.BeforeRead -= this.OnZipBeforeRead;
                }
                this.hookedZip = this.zip;
                if (this.zip != null)
                {
                    this.zip.BeforeRead += this.OnZipBeforeRead;
                }
            }
            if (this.zip != null && !this.committingPartEdit)
            {
                this.zip.CloseStep(this.lblStatus.Text);
            }
            this.UpdateUndoButtons();
        }

        private void UpdateUndoButtons()
        {
            if (this.mnuUndoBar == null)
            {
                return;
            }
            bool fields = this.editSection != null && (this.fieldUndo.Count > 0 || this.HasPendingPartEdit);
            bool undo = fields || (this.zip != null && this.zip.CanUndo);
            bool redo = this.fieldRedo.Count > 0 || (!this.HasPendingPartEditSafe && this.zip != null && this.zip.CanRedo);
            if (this.mnuUndoBar.Enabled != undo)
            {
                this.mnuUndoBar.Enabled = undo;
            }
            if (this.mnuRedoBar.Enabled != redo)
            {
                this.mnuRedoBar.Enabled = redo;
            }
            string undoTip = fields ? "Undo the last move / rotate / scale value (Ctrl+Z)"
                : (this.zip != null && this.zip.UndoLabel != null) ? "Undo: " + this.zip.UndoLabel + " (Ctrl+Z)" : "Undo (Ctrl+Z)";
            string redoTip = (this.fieldRedo.Count > 0) ? "Redo the move / rotate / scale value (Ctrl+Y)"
                : (this.zip != null && this.zip.RedoLabel != null) ? "Redo: " + this.zip.RedoLabel + " (Ctrl+Y)" : "Redo (Ctrl+Y)";
            if (this.mnuUndoBar.ToolTipText != undoTip)
            {
                this.mnuUndoBar.ToolTipText = undoTip;
            }
            if (this.mnuRedoBar.ToolTipText != redoTip)
            {
                this.mnuRedoBar.ToolTipText = redoTip;
            }
        }

        private bool HasPendingPartEditSafe
        {
            get { return this.numMove != null && this.editSection != null && this.HasPendingPartEdit; }
        }

        // Something is about to read one of the files a part move in preview changes: the move is written first.
        private void OnZipBeforeRead(ForzaZip.Entry entry)
        {
            if (this.committingPartEdit || !this.HasPendingPartEditSafe || this.currentEntry == null)
            {
                return;
            }
            if (this.PartEditTargets().Contains(entry))
            {
                this.CommitPartEdit(PartReload.Deferred);
            }
        }

        // Called from ProcessCmdKey (Navigation.cs).
        private bool HandleUndoKey(Keys keyData)
        {
            bool undo = keyData == (Keys.Control | Keys.Z);
            bool redo = keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z);
            if ((undo || redo) && !this.TextEditorHasFocus())
            {
                if (undo)
                {
                    this.UndoEdit();
                }
                else
                {
                    this.RedoEdit();
                }
                return true;
            }
            return false;
        }

        // A text editor (XML, SQL, a tree label being renamed) keeps its own Ctrl+Z.
        private bool TextEditorHasFocus()
        {
            Control control = this.ActiveControl;
            while (control is ContainerControl && ((ContainerControl) control).ActiveControl != null)
            {
                control = ((ContainerControl) control).ActiveControl;
            }
            if (control is TreeView && this.tvModel != null && control == this.tvModel && this.tvModel.LabelEdit && IsEditingLabel(this.tvModel))
            {
                return true;
            }
            TextBoxBase text = control as TextBoxBase;
            if (text == null)
            {
                return false;
            }
            // The number fields' own edit box (single line, inside a NumericUpDown) uses the editor's Undo.
            return !(text.Parent is UpDownBase);
        }

        private static bool IsEditingLabel(TreeView tree)
        {
            // TVM_GETEDITCONTROL: a label edit box exists while a node is being renamed.
            return SendMessage(tree.Handle, 0x110F, IntPtr.Zero, IntPtr.Zero) != IntPtr.Zero;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        // ---- field history (the part in edit, not yet written) ----

        private decimal[] FieldSnapshot()
        {
            decimal[] values = new decimal[9];
            for (int c = 0; c < 3; c++)
            {
                values[c] = this.numMove[c].Value;
                values[3 + c] = this.numRotate[c].Value;
                values[6 + c] = this.numScale[c].Value;
            }
            return values;
        }

        private void SetFields(decimal[] values)
        {
            this.fillingPartEdit = true;
            for (int c = 0; c < 3; c++)
            {
                this.numMove[c].Value = Clamp(this.numMove[c], values[c]);
                this.numRotate[c].Value = Clamp(this.numRotate[c], values[3 + c]);
                this.numScale[c].Value = Clamp(this.numScale[c], values[6 + c]);
            }
            this.fillingPartEdit = false;
            this.fieldCurrent = this.FieldSnapshot();
        }

        private static decimal Clamp(NumericUpDown number, decimal value)
        {
            return Math.Max(number.Minimum, Math.Min(number.Maximum, value));
        }

        // One undo step per field edit: changes of the same box less than a second apart (holding an arrow, the wheel)
        // are one step.
        private void RecordFieldChange(NumericUpDown changed)
        {
            DateTime now = DateTime.Now;
            bool sameRun = changed == this.lastFieldControl && (now - this.lastFieldChange).TotalMilliseconds < 1000;
            this.lastFieldControl = changed;
            this.lastFieldChange = now;
            if (!sameRun)
            {
                this.PushFieldUndo();
            }
        }

        private void PushFieldUndo()
        {
            decimal[] before = this.fieldCurrent ?? new decimal[] { 0, 0, 0, 0, 0, 0, 100, 100, 100 };
            this.fieldUndo.Add(before);
            this.fieldRedo.Clear();
        }

        private void ClearFieldHistory()
        {
            this.fieldUndo.Clear();
            this.fieldRedo.Clear();
            this.lastFieldControl = null;
            this.fieldCurrent = (this.numMove != null) ? this.FieldSnapshot() : null;
        }

        // ---- Undo / Redo ----

        private void UndoEdit()
        {
            if (this.editSection != null && this.fieldUndo.Count > 0)
            {
                this.fieldRedo.Add(this.FieldSnapshot());
                decimal[] values = this.fieldUndo[this.fieldUndo.Count - 1];
                this.fieldUndo.RemoveAt(this.fieldUndo.Count - 1);
                this.lastFieldControl = null;
                this.SetFields(values);
                this.UpdatePartPreview();
                this.SetStatus("Undo: " + this.editSection + " move / rotate / scale value");
                this.UpdateUndoButtons();
                return;
            }
            if (this.zip == null)
            {
                return;
            }
            // A move still in preview without field history (should not happen): written first, then undone.
            this.CommitPartEdit(PartReload.None);
            ForzaZip.HistoryStep step = this.zip.Undo();
            if (step == null)
            {
                this.SetStatus("Nothing to undo.");
                return;
            }
            this.AfterHistoryStep(step, "Undo: " + (step.Label ?? "change"));
        }

        private void RedoEdit()
        {
            if (this.editSection != null && this.fieldRedo.Count > 0)
            {
                this.fieldUndo.Add(this.FieldSnapshot());
                decimal[] values = this.fieldRedo[this.fieldRedo.Count - 1];
                this.fieldRedo.RemoveAt(this.fieldRedo.Count - 1);
                this.lastFieldControl = null;
                this.SetFields(values);
                this.UpdatePartPreview();
                this.SetStatus("Redo: " + this.editSection + " move / rotate / scale value");
                this.UpdateUndoButtons();
                return;
            }
            if (this.zip == null || this.HasPendingPartEditSafe)
            {
                return;
            }
            ForzaZip.HistoryStep step = this.zip.Redo();
            if (step == null)
            {
                this.SetStatus("Nothing to redo.");
                return;
            }
            this.AfterHistoryStep(step, "Redo: " + (step.Label ?? "change"));
        }

        // The tree and the view follow the files the step changed.
        private void AfterHistoryStep(ForzaZip.HistoryStep step, string status)
        {
            this.ClearPreviewCache();
            List<ForzaZip.Entry> touched = ForzaZip.Touched(step);
            bool structural = false;
            foreach (ForzaZip.Entry entry in touched)
            {
                bool inArchive = this.zip.Entries.Contains(entry);
                if (!inArchive || this.FindEntryNode(entry) == null)
                {
                    structural = true;
                }
            }
            if (structural)
            {
                if (this.zip.IsFolder)
                {
                    this.PopulateFolderTree();
                }
                else
                {
                    this.PopulateTree();
                }
            }
            else
            {
                foreach (ForzaZip.Entry entry in touched)
                {
                    this.RefreshEntryNode(entry);
                }
            }
            if (this.currentEntry != null && !this.zip.Entries.Contains(this.currentEntry))
            {
                this.currentEntry = null;
                this.ShowView(this.lblEmpty);
            }
            else if (this.currentEntry != null && touched.Contains(this.currentEntry))
            {
                if (this.currentEntry.Name.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase) && this.scene != null)
                {
                    this.editSection = null;
                    this.ClearFieldHistory();
                    this.ReloadModelKeepingView();
                }
                else
                {
                    this.ShowEntry(this.currentEntry);
                }
            }
            this.UpdateTitle();
            this.SetStatus(status + string.Format("   ({0} more to undo, {1} to redo)", this.zip.UndoCount, this.zip.RedoCount));
            this.UpdateUndoButtons();
        }
    }
}
