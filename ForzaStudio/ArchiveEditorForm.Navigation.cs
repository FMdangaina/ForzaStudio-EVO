namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.IO;
    using System.Windows.Forms;

    // Back: every jump to another file (a GAME:\ link, Open Car Model, a zip opened from a folder)
    // remembers where it started, down to the database row, tab, list and cell, and Back returns there.
    public partial class ArchiveEditorForm
    {
        private const int MaxBookmarks = 50;

        private class DbView
        {
            public DbSection Section;
            public string Search;
            public string ItemKey;
            public int Tab = -1;
            public string Related;
            public Point Cell = new Point(-1, -1);
            public string Property;
            public string TableFilter;
        }

        private class Bookmark
        {
            public string SourcePath;
            public bool IsFolder;
            public string EntryName;
            public DbView Db;
            public bool BaseDb;     // the base game's database shown over a package's zip (ShowBaseDatabase)

            public override string ToString()
            {
                string name = this.BaseDb ? BaseDbName : (this.EntryName != null) ? this.EntryName.Substring(this.EntryName.LastIndexOf('/') + 1) : Path.GetFileName(this.SourcePath.TrimEnd('\\'));
                if (this.Db != null && this.Db.ItemKey != null)
                {
                    name += string.Format(" ({0} #{1})", this.Db.Section, this.Db.ItemKey);
                }
                return name;
            }
        }

        private readonly List<Bookmark> bookmarks = new List<Bookmark>();
        private ToolStripMenuItem mnuBackBar;

        // Runs a jump and, if it really went somewhere else, remembers where it started.
        private void NavigateFrom(MethodInvoker jump)
        {
            Bookmark start = this.CaptureBookmark();
            jump();
            if (start == null)
            {
                return;
            }
            bool moved = this.zip == null || !string.Equals(this.zip.FilePath, start.SourcePath, StringComparison.OrdinalIgnoreCase)
                || this.currentEntry == null || this.currentEntry.Name != start.EntryName;
            if (moved)
            {
                this.bookmarks.Add(start);
                if (this.bookmarks.Count > MaxBookmarks)
                {
                    this.bookmarks.RemoveAt(0);
                }
                this.UpdateBackMenu();
            }
        }

        // A jump inside the same file (for example from a car to its wheel in the database).
        private void PushBookmark(Bookmark bookmark)
        {
            if (bookmark == null)
            {
                return;
            }
            this.bookmarks.Add(bookmark);
            if (this.bookmarks.Count > MaxBookmarks)
            {
                this.bookmarks.RemoveAt(0);
            }
            this.UpdateBackMenu();
        }

        private Bookmark CaptureBookmark()
        {
            if (this.zip == null)
            {
                return null;
            }
            Bookmark bookmark = new Bookmark();
            bookmark.SourcePath = this.zip.FilePath;
            bookmark.IsFolder = this.zip.IsFolder;
            bookmark.EntryName = (this.currentEntry != null) ? this.currentEntry.Name : null;
            if (this.gameDb != null && this.currentEntry != null && this.currentEntry == this.gameDbEntry && this.pnlGameDb.Visible)
            {
                bookmark.Db = this.CaptureDbView();
            }
            else if (this.gameDb != null && this.baseDbEntry != null && this.gameDbEntry == this.baseDbEntry && this.pnlGameDb.Visible)
            {
                bookmark.BaseDb = true;
                bookmark.Db = this.CaptureDbView();
            }
            return bookmark;
        }

        private void GoBack()
        {
            if (this.bookmarks.Count == 0)
            {
                // Nothing remembered: from a zip, back to the folder it was opened from.
                if (this.lastFolder != null && (this.zip == null || !this.zip.IsFolder))
                {
                    this.OpenFolder(this.lastFolder);
                }
                return;
            }
            Bookmark bookmark = this.bookmarks[this.bookmarks.Count - 1];
            this.bookmarks.RemoveAt(this.bookmarks.Count - 1);
            this.RestoreBookmark(bookmark);
            this.UpdateBackMenu();
        }

        private void RestoreBookmark(Bookmark bookmark)
        {
            if (this.zip == null || !string.Equals(this.zip.FilePath, bookmark.SourcePath, StringComparison.OrdinalIgnoreCase))
            {
                if (bookmark.IsFolder ? !Directory.Exists(bookmark.SourcePath) : !File.Exists(bookmark.SourcePath))
                {
                    MessageBox.Show(this, bookmark.SourcePath + "\n\nis not there any more.", "Back", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    return;
                }
                if (bookmark.IsFolder)
                {
                    this.OpenFolder(bookmark.SourcePath);
                }
                else
                {
                    this.OpenArchive(bookmark.SourcePath);
                }
                if (this.zip == null || !string.Equals(this.zip.FilePath, bookmark.SourcePath, StringComparison.OrdinalIgnoreCase))
                {
                    // Cancelled (unsaved changes kept): remember it again.
                    this.bookmarks.Add(bookmark);
                    return;
                }
            }
            if (bookmark.BaseDb)
            {
                this.ShowBaseDatabase();
                if (this.gameDb != null && this.gameDbEntry == this.baseDbEntry && bookmark.Db != null)
                {
                    this.RestoreDbView(bookmark.Db);
                }
                this.SetStatus("Back to " + bookmark);
                return;
            }
            if (bookmark.EntryName != null && (this.currentEntry == null || this.currentEntry.Name != bookmark.EntryName))
            {
                this.SelectEntry(bookmark.EntryName);
            }
            if (bookmark.Db != null && this.gameDb != null && this.currentEntry != null && this.currentEntry == this.gameDbEntry)
            {
                this.RestoreDbView(bookmark.Db);
            }
            this.SetStatus("Back to " + bookmark);
        }

        private void UpdateBackMenu()
        {
            bool canGoBack = this.bookmarks.Count > 0 || (this.lastFolder != null && (this.zip == null || !this.zip.IsFolder));
            this.mnuBackBar.Enabled = canGoBack;
            string target = (this.bookmarks.Count > 0) ? this.bookmarks[this.bookmarks.Count - 1].ToString() : (canGoBack ? Path.GetFileName(this.lastFolder.TrimEnd('\\')) + "\\" : null);
            this.mnuBackBar.Text = (target != null) ? "◀ Back to " + target : "◀ Back";
            this.mnuBackBar.ToolTipText = "Ctrl+B or Alt+Left";
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Alt | Keys.Left) || keyData == (Keys.Control | Keys.B))
            {
                this.GoBack();
                return true;
            }
            if (this.HandleViewKey(keyData))
            {
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        #region Database view state

        private TabControl ActiveDbTabs
        {
            get
            {
                switch (this.dbSection)
                {
                    case DbSection.Cars:
                        return this.tabCar;
                    case DbSection.Wheels:
                        return this.tabWheel;
                    case DbSection.Tracks:
                        return this.tabTrack;
                    case DbSection.Events:
                        return this.tabEvent;
                    case DbSection.Engines:
                        return this.tabEngineList;
                    case DbSection.Makes:
                        return this.tabMake;
                    case DbSection.Countries:
                        return this.tabCountry;
                    case DbSection.PartMakers:
                        return this.tabPartMaker;
                }
                return null;
            }
        }

        private static PropertyGrid FindPropertyGrid(Control control)
        {
            if (control is PropertyGrid)
            {
                return (PropertyGrid) control;
            }
            foreach (Control child in control.Controls)
            {
                if (child is PropertyGrid)
                {
                    return (PropertyGrid) child;
                }
            }
            return null;
        }

        private DbView CaptureDbView()
        {
            DbView view = new DbView();
            view.Section = this.dbSection;
            view.Search = this.txtDbSearch.Text;
            if (this.lvDb.SelectedItems.Count == 1)
            {
                view.ItemKey = this.lvDb.SelectedItems[0].Text;
            }
            TabControl tabs = this.ActiveDbTabs;
            if (tabs != null && tabs.SelectedTab != null && tabs.SelectedTab.Controls.Count > 0)
            {
                view.Tab = tabs.SelectedIndex;
                Control content = tabs.SelectedTab.Controls[0];
                RelatedPanel related = content as RelatedPanel;
                if (related != null)
                {
                    view.Related = related.SelectedLabel;
                    view.Cell = related.Grid.CellPosition;
                }
                PropertyGrid grid = FindPropertyGrid(content);
                if (grid != null && grid.SelectedGridItem != null)
                {
                    view.Property = grid.SelectedGridItem.Label;
                }
            }
            else if (tabs == null)
            {
                view.TableFilter = this.gridTable.Filter;
                view.Cell = this.gridTable.CellPosition;
            }
            return view;
        }

        private void RestoreDbView(DbView view)
        {
            if (this.dbSection != view.Section)
            {
                this.SelectDbSection(view.Section);
            }
            if (this.txtDbSearch.Text != (view.Search ?? ""))
            {
                this.txtDbSearch.Text = view.Search ?? "";
            }
            if (view.ItemKey != null)
            {
                foreach (ListViewItem item in this.lvDb.Items)
                {
                    if (item.Text == view.ItemKey)
                    {
                        item.Selected = true;
                        item.Focused = true;
                        item.EnsureVisible();
                        break;
                    }
                }
            }
            TabControl tabs = this.ActiveDbTabs;
            if (tabs != null)
            {
                if (view.Tab >= 0 && view.Tab < tabs.TabCount)
                {
                    tabs.SelectedIndex = view.Tab;
                }
                Control content = (tabs.SelectedTab != null && tabs.SelectedTab.Controls.Count > 0) ? tabs.SelectedTab.Controls[0] : null;
                RelatedPanel related = content as RelatedPanel;
                if (related != null)
                {
                    if (view.Related != null && related.SelectedLabel != view.Related)
                    {
                        related.SelectedLabel = view.Related;
                    }
                    related.Grid.CellPosition = view.Cell;
                    related.Grid.FocusGrid();
                }
                PropertyGrid grid = (content == null) ? null : FindPropertyGrid(content);
                if (grid != null && view.Property != null)
                {
                    SelectProperty(grid, view.Property);
                    grid.Focus();
                }
            }
            else
            {
                if (this.gridTable.TableName != null && this.gridTable.Filter != view.TableFilter)
                {
                    this.gridTable.ShowRows(this.gameDb, this.gridTable.TableName, view.TableFilter);
                }
                this.gridTable.CellPosition = view.Cell;
                this.gridTable.FocusGrid();
            }
        }

        private static void SelectProperty(PropertyGrid grid, string label)
        {
            GridItem root = grid.SelectedGridItem;
            if (root == null)
            {
                return;
            }
            while (root.Parent != null)
            {
                root = root.Parent;
            }
            GridItem found = FindGridItem(root, label);
            if (found != null)
            {
                grid.SelectedGridItem = found;
            }
        }

        private static GridItem FindGridItem(GridItem item, string label)
        {
            if (item.GridItemType == GridItemType.Property && item.Label == label)
            {
                return item;
            }
            foreach (GridItem child in item.GridItems)
            {
                GridItem found = FindGridItem(child, label);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        #endregion
    }
}
