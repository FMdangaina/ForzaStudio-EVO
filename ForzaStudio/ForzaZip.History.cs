using System;
using System.Collections.Generic;

namespace ForzaStudio
{
    // Undo / redo of the pending changes of an archive (user 2026-11-16: "tasto indietro e ripristina").
    // Every change of an entry (Replace, Add, MarkDeleted, Undelete, Revert, Rename, Remove, the shadersettings
    // follow-up of a long-mesh split) records the entry's state before it into the open step; CloseStep (the
    // editor calls it when the user's action is over: Application.Idle) closes the step with each entry's state
    // after it. Undo puts the "before" states back, Redo the "after" ones. Save clears the history: the saved
    // archive is the new starting point.
    public partial class ForzaZip
    {
        internal class EntryState
        {
            public byte[] Replacement;
            public bool Deleted;
            public bool IsNew;
            public bool InList;
            public int Index;
            public string Name;
            public byte[] LocalHeader;
            public byte[] CentralRecord;
        }

        public class HistoryStep
        {
            public string Label;
            internal readonly List<Entry> Entries = new List<Entry>();
            internal readonly List<EntryState> Before = new List<EntryState>();
            internal readonly List<EntryState> After = new List<EntryState>();
        }

        // Raised at the start of every Read: the editor uses it to write a part move / rotate / scale that is still only
        // a preview into the file before anything reads that file (so no "Apply" is needed).
        public event Action<Entry> BeforeRead;

        private const int HistoryLimit = 100;
        private readonly List<HistoryStep> undo = new List<HistoryStep>();
        private readonly List<HistoryStep> redo = new List<HistoryStep>();
        private HistoryStep open;
        // Each entry's state at the end of the last step (a copy): Read hands out the live replacement array, and a
        // caller that changes it in place before Replace would otherwise change the "before" state too.
        private readonly Dictionary<Entry, EntryState> known = new Dictionary<Entry, EntryState>();
        private bool restoring;

        public bool CanUndo { get { return this.undo.Count > 0 || this.open != null; } }
        public bool CanRedo { get { return this.redo.Count > 0 && this.open == null; } }
        public string UndoLabel { get { return (this.undo.Count > 0) ? this.undo[this.undo.Count - 1].Label : null; } }
        public string RedoLabel { get { return (this.redo.Count > 0) ? this.redo[this.redo.Count - 1].Label : null; } }
        public int UndoCount { get { return this.undo.Count; } }
        public int RedoCount { get { return this.redo.Count; } }

        private EntryState Capture(Entry entry)
        {
            EntryState state = new EntryState();
            // Copied: a caller may change the array it got from Read in place before replacing it.
            state.Replacement = (entry.Replacement != null) ? (byte[]) entry.Replacement.Clone() : null;
            state.Deleted = entry.deleted;
            state.IsNew = entry.IsNew;
            state.Index = this.Entries.IndexOf(entry);
            state.InList = state.Index >= 0;
            state.Name = entry.Name;
            state.LocalHeader = entry.LocalHeader;
            state.CentralRecord = entry.CentralRecord;
            return state;
        }

        // Called before an entry changes. 'notYetInList' for a new entry: its state before is "absent".
        private void Record(Entry entry, bool notYetInList)
        {
            if (this.restoring)
            {
                return;
            }
            if (this.open == null)
            {
                this.open = new HistoryStep();
            }
            if (this.open.Entries.Contains(entry))
            {
                return;
            }
            EntryState before = this.Capture(entry);
            EntryState last;
            if (this.known.TryGetValue(entry, out last))
            {
                before.Replacement = last.Replacement;   // never changed: Restore and Capture copy
            }
            if (notYetInList)
            {
                before.InList = false;
                before.Index = -1;
            }
            this.open.Entries.Add(entry);
            this.open.Before.Add(before);
        }

        private void Record(Entry entry)
        {
            this.Record(entry, false);
        }

        // Ends the user's action: the changes since the last call become one undo step. False when nothing changed.
        public bool CloseStep(string label)
        {
            if (this.open == null)
            {
                return false;
            }
            HistoryStep step = this.open;
            this.open = null;
            bool changed = false;
            foreach (Entry entry in step.Entries)
            {
                EntryState after = this.Capture(entry);
                step.After.Add(after);
                this.known[entry] = after;
            }
            for (int i = 0; i < step.Entries.Count && !changed; i++)
            {
                changed = !Same(step.Before[i], step.After[i]);
            }
            if (!changed)
            {
                return false;
            }
            step.Label = label;
            this.undo.Add(step);
            if (this.undo.Count > HistoryLimit)
            {
                this.undo.RemoveAt(0);
            }
            this.redo.Clear();
            return true;
        }

        private static bool Same(EntryState a, EntryState b)
        {
            if (a.Deleted != b.Deleted || a.InList != b.InList || a.IsNew != b.IsNew || a.Name != b.Name)
            {
                return false;
            }
            if (a.Replacement == null || b.Replacement == null)
            {
                return a.Replacement == b.Replacement;
            }
            if (a.Replacement.Length != b.Replacement.Length)
            {
                return false;
            }
            for (int i = 0; i < a.Replacement.Length; i++)
            {
                if (a.Replacement[i] != b.Replacement[i])
                {
                    return false;
                }
            }
            return true;
        }

        // Takes the last step back; returns it (null when there is none). The entries it touched are in Touched.
        public HistoryStep Undo()
        {
            this.CloseStep("(change)");
            if (this.undo.Count == 0)
            {
                return null;
            }
            HistoryStep step = this.undo[this.undo.Count - 1];
            this.undo.RemoveAt(this.undo.Count - 1);
            this.Restore(step, step.Before);
            this.redo.Add(step);
            return step;
        }

        public HistoryStep Redo()
        {
            if (this.open != null || this.redo.Count == 0)
            {
                return null;
            }
            HistoryStep step = this.redo[this.redo.Count - 1];
            this.redo.RemoveAt(this.redo.Count - 1);
            this.Restore(step, step.After);
            this.undo.Add(step);
            return step;
        }

        public static List<Entry> Touched(HistoryStep step)
        {
            return new List<Entry>(step.Entries);
        }

        private void Restore(HistoryStep step, List<EntryState> states)
        {
            this.restoring = true;
            try
            {
                // Removals first, then insertions in index order, so the saved indices stay right.
                for (int i = 0; i < step.Entries.Count; i++)
                {
                    if (!states[i].InList)
                    {
                        this.Entries.Remove(step.Entries[i]);
                    }
                }
                List<int> order = new List<int>();
                for (int i = 0; i < step.Entries.Count; i++)
                {
                    order.Add(i);
                }
                order.Sort(delegate(int a, int b) { return states[a].Index.CompareTo(states[b].Index); });
                foreach (int i in order)
                {
                    Entry entry = step.Entries[i];
                    EntryState state = states[i];
                    entry.Replacement = (state.Replacement != null) ? (byte[]) state.Replacement.Clone() : null;
                    entry.deleted = state.Deleted;
                    entry.IsNew = state.IsNew;
                    entry.Name = state.Name;
                    entry.LocalHeader = state.LocalHeader;
                    entry.CentralRecord = state.CentralRecord;
                    this.known[entry] = state;
                    if (state.InList && !this.Entries.Contains(entry))
                    {
                        this.Entries.Insert(Math.Max(0, Math.Min(state.Index, this.Entries.Count)), entry);
                    }
                }
            }
            finally
            {
                this.restoring = false;
            }
        }

        // The saved archive is the new starting point.
        public void ClearHistory()
        {
            this.undo.Clear();
            this.known.Clear();
            this.redo.Clear();
            this.open = null;
        }
    }
}
