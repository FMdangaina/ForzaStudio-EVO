namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.IO;
    using System.Windows.Forms;

    // Digital dash view (Anark .fbf + .bgf + .bsg of DigitalGauge\Dash_<car>): element tree, 2D preview, property inspector.
    // Selecting any of the three files shows the whole dash. The inspector (a PropertyGrid, see DashInspector) lists the
    // values of the selected element with the editor that fits each: drop-down lists, check boxes, numbers, text, colour.
    // On the preview, a drag moves the element and the handles resize it. AnarkDash writes each value where it lives,
    // nothing is added; every edit makes the changed files pending entries of the archive, written with File > Save.
    public partial class ArchiveEditorForm
    {
        private Panel pnlDash;
        private SplitContainer splitDash;
        private TreeView tvDash;
        private AnarkDashCanvas dashCanvas;
        private PropertyGrid pgDash;
        private ToolStripLabel lblDashInfo;
        private bool dashSelecting;
        private ForzaZip.Entry dashFbfEntry, dashBgfEntry, dashBsgEntry;

        // name, caption, factor (shown = stored * factor: rotation in degrees), minimum, maximum
        private static readonly object[][] DashTransforms = new object[][]
        {
            new object[] { "position.x", "Position X", 1f, -100000f, 100000f },
            new object[] { "position.y", "Position Y", 1f, -100000f, 100000f },
            new object[] { "position.z", "Position Z", 1f, -100000f, 100000f },
            new object[] { "rotation.z", "Rotation (deg)", (float) (180 / Math.PI), -100000f, 100000f },
            new object[] { "scale.x", "Scale X", 1f, -10000f, 10000f },
            new object[] { "scale.y", "Scale Y", 1f, -10000f, 10000f },
            new object[] { "pivot.x", "Pivot X", 1f, -100000f, 100000f },
            new object[] { "pivot.y", "Pivot Y", 1f, -100000f, 100000f },
            new object[] { "opacity", "Opacity (0-100)", 1f, 0f, 100f },
        };

        // Graph properties shown in the first two groups of the inspector, not again in the last one.
        private static readonly string[] DashHandledNames = new string[]
        {
            "position.x", "position.y", "position.z", "rotation.z", "scale.x", "scale.y", "pivot.x", "pivot.y", "opacity",
            "textstring", "font", "textcolor.r", "textcolor.g", "textcolor.b",
        };

        // Names of the values of the integer properties that are choices.
        private static string[] DashEnumLabels(string name)
        {
            switch (name)
            {
                case "horzalign": return new string[] { "Left", "Center", "Right" };
                case "vertalign": return new string[] { "Top", "Middle", "Bottom" };
            }
            return null;
        }

        private void BuildDashView()
        {
            this.pnlDash = new Panel();
            this.pnlDash.Dock = DockStyle.Fill;

            SplitContainer outer = this.splitDash = new SplitContainer();
            outer.Dock = DockStyle.Fill;
            outer.FixedPanel = FixedPanel.Panel1;

            this.tvDash = new TreeView();
            this.tvDash.Dock = DockStyle.Fill;
            this.tvDash.HideSelection = false;
            this.tvDash.AfterSelect += delegate(object sender, TreeViewEventArgs e)
            {
                if (this.dashSelecting)
                {
                    return;
                }
                this.dashCanvas.Selected = (e.Node != null) ? e.Node.Tag as AnarkDash.Node : null;
                this.dashCanvas.Invalidate();
                this.ShowDashProperties(this.dashCanvas.Selected);
            };
            outer.Panel1.Controls.Add(this.tvDash);

            SplitContainer inner = new SplitContainer();
            inner.Dock = DockStyle.Fill;
            inner.FixedPanel = FixedPanel.Panel2;

            this.dashCanvas = new AnarkDashCanvas();
            this.dashCanvas.Dock = DockStyle.Fill;
            this.dashCanvas.SelectionChanged += delegate { this.SelectDashNode(this.dashCanvas.Selected); };
            this.dashCanvas.DuplicateRequested += delegate { this.DuplicateDashSelection(); };
            this.dashCanvas.DeleteRequested += delegate { this.DeleteDashSelection(); };
            this.dashCanvas.Dragging +=delegate { this.pgDash.Refresh(); };
            this.dashCanvas.Edited += delegate(object sender, DashEditEventArgs e)
            {
                this.DashEdited(true, e.What, true);
            };
            this.dashCanvas.EditBlocked += delegate(object sender, DashEditEventArgs e) { this.SetStatus("Dash: " + e.What + "."); };

            this.pgDash = new PropertyGrid();
            this.pgDash.Dock = DockStyle.Fill;
            this.pgDash.PropertySort = PropertySort.Categorized;
            this.pgDash.ToolbarVisible = false;
            this.pgDash.HelpVisible = true;

            ToolStrip bar = new ToolStrip();
            bar.GripStyle = ToolStripGripStyle.Hidden;
            bar.Items.Add(new ToolStripButton("Fit (double-click)", null, delegate { this.dashCanvas.Fit(); }));
            ToolStripButton outlines = new ToolStripButton("Outlines");
            outlines.CheckOnClick = true;
            outlines.CheckedChanged += delegate
            {
                this.dashCanvas.ShowOutlines = outlines.Checked;
                this.dashCanvas.Invalidate();
            };
            bar.Items.Add(outlines);
            ToolStripDropDownButton align = new ToolStripDropDownButton("Align (select several)");
            string[][] aligns = new string[][]
            {
                new string[] { "Left", "left" }, new string[] { "Center horizontally", "hcenter" }, new string[] { "Right", "right" },
                new string[] { "Top", "top" }, new string[] { "Middle", "vcenter" }, new string[] { "Bottom", "bottom" },
                new string[] { "Spread horizontally (3+)", "hspread" }, new string[] { "Spread vertically (3+)", "vspread" },
            };
            foreach (string[] a in aligns)
            {
                string mode = a[1];
                align.DropDownItems.Add(new ToolStripMenuItem(a[0], null, delegate { this.dashCanvas.Align(mode); }));
            }
            bar.Items.Add(align);
            bar.Items.Add(new ToolStripButton("Duplicate (Ctrl+D)", null, delegate { this.DuplicateDashSelection(); }));
            ToolStripDropDownButton add = new ToolStripDropDownButton("Add");
            add.DropDownItems.Add(new ToolStripMenuItem("Gauge (a number or text with its own tree)...", null, delegate { this.AddDashGauge(); }));
            add.DropDownItems.Add(new ToolStripMenuItem("Texts from another car's dash...", null, delegate { this.ImportDashFromCar(); }));
            bar.Items.Add(add);
            bar.Items.Add(new ToolStripButton("Delete (Del)", null, delegate { this.DeleteDashSelection(); }));
            bar.Items.Add(new ToolStripButton("Structure...", null, delegate { this.ShowDashReport(); }));
            this.lblDashInfo = new ToolStripLabel();
            this.lblDashInfo.Alignment = ToolStripItemAlignment.Right;
            bar.Items.Add(this.lblDashInfo);

            inner.Panel1.Controls.Add(this.dashCanvas);
            inner.Panel2.Controls.Add(this.pgDash);
            outer.Panel2.Controls.Add(inner);
            this.pnlDash.Controls.Add(outer);
            this.pnlDash.Controls.Add(bar);
            this.pnlDash.Resize += delegate
            {
                if (inner.Width > 500)
                {
                    inner.SplitterDistance = inner.Width - 340;
                }
            };
        }

        private void ShowDash(ForzaZip.Entry entry, byte[] data)
        {
            string stem = entry.Name.Substring(0, entry.Name.Length - Path.GetExtension(entry.Name).Length);
            ForzaZip.Entry fbfEntry = this.zip.Find(stem + ".fbf"), bgfEntry = this.zip.Find(stem + ".bgf"), bsgEntry = this.zip.Find(stem + ".bsg");
            if (fbfEntry == null || bgfEntry == null)
            {
                this.ShowInfo(entry, "the dash needs both " + Path.GetFileName(stem) + ".fbf and .bgf next to each other.");
                return;
            }
            AnarkFbf fbf = AnarkFbf.Read(fbfEntry == entry ? data : this.zip.Read(fbfEntry));
            AnarkBgf bgf = AnarkBgf.Read(bgfEntry == entry ? data : this.zip.Read(bgfEntry));
            AnarkBsg bsg = (bsgEntry != null) ? AnarkBsg.Read(bsgEntry == entry ? data : this.zip.Read(bsgEntry)) : null;
            AnarkDash dash = new AnarkDash(fbf, bgf, bsg);
            // a dash saved by an older build (copied models before 2026-11-07) crashes FM4 on load: repaired before the
            // view is built, written to the archive below and saved with Save like any other dash edit
            int repaired = 0;
            string repairError = null;
            try
            {
                repaired = dash.RepairLinks();
            }
            catch (InvalidDataException ex)
            {
                repairError = ex.Message;
            }
            this.dashFbfEntry = fbfEntry;
            this.dashBgfEntry = bgfEntry;
            this.dashBsgEntry = bsgEntry;

            this.ShowView(this.pnlDash);
            this.splitDash.SplitterDistance = Math.Min(260, Math.Max(this.splitDash.Width / 3, 50));
            this.dashCanvas.SetDash(dash, new AnarkDashCanvas.TextureLoader(this.LoadDashTexture));

            this.FillDashTree(dash);
            this.ShowDashProperties(null);

            int missing = 0;
            foreach (AnarkDash.Item item in dash.Items)
            {
                if (item.Texture != null && this.dashCanvas.GetTexture(item.Texture) == null)
                {
                    missing++;
                }
            }
            this.lblDashInfo.Text = string.Format("FBF 0x{0:X} ({1})   {2} elements, {3} meshes{4}{5}",
                fbf.Version, fbf.IsFH2 ? "FH2" : "FM4", bgf.Elements.Count, fbf.Meshes.Count,
                (bsg == null) ? ", no .bsg" : "", (missing > 0) ? string.Format(", {0} textures missing", missing) : "");

            if (repaired > 0)
            {
                this.DashEdited(true, repaired + " model/material/slide link(s) of an older edit repaired (FM4 would crash on this dash)", false);
            }
            else if (repairError != null)
            {
                this.SetStatus("Dash: model/material/slide links cannot be checked (" + repairError + ").");
            }
        }

        private void FillDashTree(AnarkDash dash)
        {
            this.dashSelecting = true;
            this.tvDash.BeginUpdate();
            this.tvDash.Nodes.Clear();
            if (dash.Root != null)
            {
                TreeNode root = this.AddDashNode(this.tvDash.Nodes, dash.Root);
                root.Expand();
                foreach (TreeNode child in root.Nodes)
                {
                    child.Expand();
                }
            }
            this.tvDash.EndUpdate();
            this.dashSelecting = false;
        }

        // Ctrl+D / Duplicate: copies the selected elements with everything under them (see AnarkDash.Duplicate).
        private void DuplicateDashSelection()
        {
            AnarkDash dash = this.dashCanvas.Dash;
            List<AnarkDash.Node> nodes = (dash != null) ? this.dashCanvas.TopSelection() : new List<AnarkDash.Node>();
            if (nodes.Count == 0)
            {
                this.SetStatus("Dash: select what to duplicate.");
                return;
            }
            string name;
            bool sameNames;
            if (!this.AskDashCopy(nodes, out name, out sameNames))
            {
                return;
            }
            // by element id: a copy is inserted right after its original, so the indexes of the next originals move
            List<uint> originals = new List<uint>();
            foreach (AnarkDash.Node n in nodes)
            {
                originals.Add(n.Element.Id);
            }
            List<uint> created = new List<uint>();
            bool texts = false;
            foreach (uint id in originals)
            {
                AnarkDash.Node original = ById(dash, id);
                AnarkDash.Node copy = (original != null) ? dash.Duplicate(original, (nodes.Count == 1) ? name : original.Name + name, sameNames) : null;
                if (copy == null)
                {
                    this.SetStatus("Dash: " + dash.Blocked + ".");
                    continue;
                }
                created.Add(copy.Element.Id);
                texts = texts || dash.FirstText(copy) != null;
            }
            if (created.Count == 0)
            {
                return;
            }
            foreach (uint id in created)
            {
                dash.Nudge(ById(dash, id), 20, -20);        // beside the original; stays on it when the position is not editable
            }
            this.FillDashTree(dash);
            this.dashCanvas.Selection.Clear();
            foreach (uint id in created)
            {
                this.dashCanvas.Selection.Add(ById(dash, id));
            }
            this.dashCanvas.Invalidate();
            this.SelectDashNode(this.dashCanvas.Selected);
            // the game feeds a value (gear, clock...) to one text only, and a text it does not feed is not shown in game
            this.DashEdited(true, ((created.Count == 1) ? "duplicate" : created.Count + " duplicates")
                + (texts ? " (its texts are not fed by the game, so they will not show in game: a value can be moved to a new text with Add > Gauge)" : ""), false);
        }

        private static AnarkDash.Node ById(AnarkDash dash, uint id)
        {
            foreach (AnarkDash.Node n in dash.Nodes)
            {
                if (n.Element.Id == id)
                {
                    return n;
                }
            }
            return null;
        }

        private bool AskDashCopy(List<AnarkDash.Node> nodes, out string name, out bool sameNames)
        {
            bool single = nodes.Count == 1;
            using (Form dialog = new Form())
            {
                dialog.Text = single ? "Duplicate " + nodes[0].Name : "Duplicate " + nodes.Count + " elements";
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MinimizeBox = dialog.MaximizeBox = false;
                dialog.ClientSize = new Size(420, 190);
                Label caption = new Label();
                caption.Text = single ? "Name of the copy:" : "Text added to each name:";
                caption.SetBounds(12, 12, 396, 18);
                TextBox box = new TextBox();
                box.Text = single ? nodes[0].Name + "_2" : "_2";
                box.SetBounds(12, 34, 396, 22);
                CheckBox same = new CheckBox();
                same.Text = "Keep the original names inside (the game may animate or feed the copy like the original)";
                same.SetBounds(12, 66, 396, 36);
                same.CheckedChanged += delegate { box.Enabled = !same.Checked; };
                Label note = new Label();
                note.Text = "The copy shares meshes and textures, goes in the last group of the dash (the backdrop) and sits 20 units off the original. The game feeds only its own gauges: a copy of a gear or speed text stays still.";
                note.SetBounds(12, 104, 396, 44);
                Button ok = new Button();
                ok.Text = "Duplicate";
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(236, 156, 84, 26);
                Button cancel = new Button();
                cancel.Text = "Cancel";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds(326, 156, 84, 26);
                dialog.Controls.AddRange(new Control[] { caption, box, same, note, ok, cancel });
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;
                ForzaTheme.Apply(dialog);
                bool accepted = dialog.ShowDialog(this) == DialogResult.OK && (same.Checked || box.Text.Trim().Length > 0);
                name = box.Text.Trim();
                sameNames = same.Checked;
                return accepted;
            }
        }

        // Structure...: every element, record, base node, property and tail table of the three files, as text.
        private void ShowDashReport()
        {
            AnarkDash dash = this.dashCanvas.Dash;
            if (dash == null)
            {
                return;
            }
            string text = DashReport.Build(dash);
            using (Form dialog = new Form())
            {
                dialog.Text = "Dash structure (" + dash.Nodes.Count + " elements)";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.Size = new Size(1100, 760);
                TextBox box = new TextBox();
                box.Multiline = true;
                box.ReadOnly = true;
                box.ScrollBars = ScrollBars.Both;
                box.WordWrap = false;
                box.Font = new Font("Consolas", 9f);
                box.Dock = DockStyle.Fill;
                box.Text = text;
                Button save = new Button();
                save.Text = "Save as .txt...";
                save.Dock = DockStyle.Bottom;
                save.Click += delegate
                {
                    using (SaveFileDialog sfd = new SaveFileDialog())
                    {
                        sfd.Filter = "Text|*.txt";
                        sfd.FileName = "dash_structure.txt";
                        if (sfd.ShowDialog(dialog) == DialogResult.OK)
                        {
                            File.WriteAllText(sfd.FileName, text, System.Text.Encoding.UTF8);
                        }
                    }
                };
                dialog.Controls.Add(box);
                dialog.Controls.Add(save);
                ForzaTheme.Apply(dialog);
                dialog.ShowDialog(this);
            }
        }

        // Del / Delete: an element added to the dash (or any of its last elements the game does not use) is removed from
        // the three files; any other is offered to be hidden (opacity 0), because the game numbers the elements.
        private void DeleteDashSelection()
        {
            AnarkDash dash = this.dashCanvas.Dash;
            List<AnarkDash.Node> nodes = (dash != null) ? this.dashCanvas.TopSelection() : new List<AnarkDash.Node>();
            if (nodes.Count == 0)
            {
                this.SetStatus("Dash: select what to delete.");
                return;
            }
            List<int> indexes = new List<int>();
            foreach (AnarkDash.Node n in nodes)
            {
                indexes.Add(n.Index);
            }
            indexes.Sort();
            indexes.Reverse();                  // the last first: removing one does not move the others
            int removed = 0;
            List<int> hide = new List<int>();
            List<string> reasons = new List<string>();
            foreach (int index in indexes)
            {
                AnarkDash.Node node = dash.Nodes[index];
                string why = dash.WhyNotRemovable(node);
                if (why == null)
                {
                    dash.Remove(node);
                    removed++;
                }
                else
                {
                    hide.Add(index);
                    reasons.Add(why);
                }
            }
            int hidden = 0;
            if (hide.Count > 0)
            {
                string list = reasons[0] + ((hide.Count > 1) ? " (and " + (hide.Count - 1) + " more)" : "");
                if (MessageBox.Show(this, "Cannot be deleted: " + list + ".\r\n\r\nHide " + ((hide.Count == 1) ? "it" : "them") + " instead (opacity 0)?",
                    "Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    foreach (int index in hide)
                    {
                        if (dash.SetTransform(dash.Nodes[index], "opacity", 0))
                        {
                            hidden++;
                        }
                    }
                }
            }
            if (removed == 0 && hidden == 0)
            {
                return;
            }
            this.FillDashTree(dash);
            this.dashCanvas.Selection.Clear();
            this.dashCanvas.Invalidate();
            this.SelectDashNode(null);
            this.DashEdited(true, ((removed > 0) ? removed + " deleted" : "") + ((removed > 0 && hidden > 0) ? ", " : "") + ((hidden > 0) ? hidden + " hidden" : ""), false);
        }

        // Data the game can feed into a text: contract channel (see AnarkBgf), shown name.
        private static readonly string[][] DashFeeds = new string[][]
        {
            new string[] { "001BB29C", "Engine rpm (tach)" }, new string[] { "054BD82F", "Turbo boost pressure" }, new string[] { "064405FF", "Gear" },
            new string[] { "01F1A7EB", "Speed" }, new string[] { "04B6EF78", "Fuel" }, new string[] { "030765FF", "Odometer" }, new string[] { "015CE8CE", "Trip" },
            new string[] { "07C92224", "Clock" }, new string[] { "06FE717C", "Oil pressure" }, new string[] { "03DFBB0D", "Oil temperature" },
            new string[] { "013DA2F2", "Water temperature" }, new string[] { "06CD5CEA", "Outside temperature" },
            new string[] { "00B74AB7", "Current lap time" }, new string[] { "03DC8BD8", "Last lap time" }, new string[] { "04FB9F66", "Best lap time" },
            new string[] { "01446A9B", "Race position" }, new string[] { "042EF57D", "Lap number" }, new string[] { "0720D35B", "Total laps" },
        };

        // Add > From another car: a group or a text (with the texts under it) taken from the dash of another car, like the
        // TEXT group of the Corvette C5R (rpm, speed, gear, lap time). Whatever under it needs a picture is left out. The
        // channels the other dash fed to these texts are fed to the copies here, unless this dash already uses that value.
        private void ImportDashFromCar()
        {
            AnarkDash dash = this.dashCanvas.Dash;
            if (dash == null)
            {
                return;
            }
            AnarkDash src;
            string carName;
            using (OpenFileDialog open = new OpenFileDialog())
            {
                open.Title = "Car zip (or its folder file) whose dash to take from";
                open.Filter = "Car zip|*.zip|All files|*.*";
                if (this.zip != null && !string.IsNullOrEmpty(this.zip.FilePath) && !this.zip.IsFolder)
                {
                    open.InitialDirectory = Path.GetDirectoryName(this.zip.FilePath);
                }
                if (open.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                ForzaZip other;
                try
                {
                    other = IsArchive(open.FileName) ? new ForzaZip(open.FileName) : ForzaZip.OpenFile(open.FileName);
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
                    this.SetStatus("Dash: " + Path.GetFileName(open.FileName) + " has no digital dash.");
                    return;
                }
                string stem = bgfEntry.Name.Substring(0, bgfEntry.Name.Length - 4);
                ForzaZip.Entry fbfEntry = other.Find(stem + ".fbf"), bsgEntry = other.Find(stem + ".bsg");
                if (fbfEntry == null)
                {
                    this.SetStatus("Dash: the other dash has no .fbf.");
                    return;
                }
                src = new AnarkDash(AnarkFbf.Read(other.Read(fbfEntry)), AnarkBgf.Read(other.Read(bgfEntry)), (bsgEntry != null) ? AnarkBsg.Read(other.Read(bsgEntry)) : null);
                carName = Path.GetFileNameWithoutExtension(open.FileName);
            }
            List<AnarkDash.Node> usable = new List<AnarkDash.Node>();
            foreach (AnarkDash.Node n in src.Nodes)
            {
                if (n != src.Root && n.Record != null && AnarkDash.OwnerOf(n) == src.Root && n.Element.Type != AnarkBgf.ElementType.Component
                    && (n.Record.Type == AnarkFbf.RecordType.Group || n.Record.Type == AnarkFbf.RecordType.Text) && src.FirstText(n) != null)
                {
                    usable.Add(n);
                }
            }
            if (usable.Count == 0)
            {
                this.SetStatus("Dash: the dash of " + carName + " has no group or text to take.");
                return;
            }
            using (Form dialog = new Form())
            {
                dialog.Text = "Add from " + carName;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MinimizeBox = dialog.MaximizeBox = false;
                dialog.ClientSize = new Size(432, 190);
                Label l1 = new Label();
                l1.Text = "Take:";
                l1.SetBounds(12, 15, 60, 18);
                ComboBox from = new ComboBox();
                from.DropDownStyle = ComboBoxStyle.DropDownList;
                foreach (AnarkDash.Node t in usable)
                {
                    from.Items.Add(src.PathOf(t).Replace("parent.Layer.", "") + "  (" + src.Subtree(t).Count + " elements)");
                }
                from.SelectedIndex = 0;
                from.SetBounds(76, 12, 344, 22);
                CheckBox feed = new CheckBox();
                feed.Text = "Feed the copies with the same game values as there (rpm, speed, gear, lap time...)";
                feed.Checked = true;
                feed.SetBounds(12, 46, 408, 22);
                Label note = new Label();
                note.Text = "Texts, groups and their place on the screen are copied; pictures under the group are left out. A value this dash already feeds to something else stays where it is. The font must be one this dash can show.";
                note.SetBounds(12, 76, 408, 60);
                Button ok = new Button();
                ok.Text = "Add";
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(246, 150, 84, 26);
                Button cancel = new Button();
                cancel.Text = "Cancel";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds(336, 150, 84, 26);
                dialog.Controls.AddRange(new Control[] { l1, from, feed, note, ok, cancel });
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;
                ForzaTheme.Apply(dialog);
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                AnarkDash.Node pick = usable[from.SelectedIndex];
                AnarkDash.Node copy = dash.Import(src, pick, pick.Name, true);
                if (copy == null)
                {
                    this.SetStatus("Dash: " + dash.Blocked + ".");
                    return;
                }
                string fed = "";
                if (feed.Checked)
                {
                    List<string> lines = dash.BindImported(src, dash.LastImport);
                    fed = (lines.Count > 0) ? "; " + string.Join(", ", lines.ToArray()) : "; nothing fed";
                }
                this.FillDashTree(dash);
                this.dashCanvas.Selection.Clear();
                this.dashCanvas.Selection.Add(copy);
                this.dashCanvas.Invalidate();
                this.SelectDashNode(copy);
                this.DashEdited(true, "added " + copy.Name + " from " + carName, false);
                this.SetStatus("Dash: added " + copy.Name + " from " + carName + fed + ".");
            }
        }

        // Add > Gauge: a whole gauge (component, group and text, with its slides) copied from one of the gauges this dash
        // already has, like Gauge_Gear, in the scene. It can be fed with a game value (rpm, boost, speed...).
        private void AddDashGauge()
        {
            AnarkDash dash = this.dashCanvas.Dash;
            if (dash == null)
            {
                return;
            }
            List<AnarkDash.Node> templates = dash.GaugeTemplates();
            List<AnarkDash.Node> usable = new List<AnarkDash.Node>();
            foreach (AnarkDash.Node t in templates)
            {
                if (dash.FirstText(t) != null)
                {
                    usable.Add(t);
                }
            }
            if (usable.Count == 0)
            {
                this.SetStatus("Dash: this dash has no text gauge (like Gauge_Gear or Gauge_Clock) to copy.");
                return;
            }
            using (Form dialog = new Form())
            {
                dialog.Text = "Add gauge";
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MinimizeBox = dialog.MaximizeBox = false;
                dialog.ClientSize = new Size(432, 236);
                Label l1 = new Label();
                l1.Text = "Copy of:";
                l1.SetBounds(12, 15, 96, 18);
                ComboBox from = new ComboBox();
                from.DropDownStyle = ComboBoxStyle.DropDownList;
                foreach (AnarkDash.Node t in usable)
                {
                    from.Items.Add(t.Name + "  (" + (dash.Subtree(t).Count) + " elements)");
                }
                from.SelectedIndex = 0;
                from.SetBounds(112, 12, 308, 22);
                Label l2 = new Label();
                l2.Text = "Name:";
                l2.SetBounds(12, 45, 96, 18);
                TextBox nameBox = new TextBox();
                nameBox.Text = "Gauge_New";
                nameBox.SetBounds(112, 42, 308, 22);
                Label l3 = new Label();
                l3.Text = "Text shown:";
                l3.SetBounds(12, 75, 96, 18);
                TextBox textBox = new TextBox();
                textBox.Text = "0";
                textBox.SetBounds(112, 72, 308, 22);
                Label l4 = new Label();
                l4.Text = "Fed by the game with:";
                l4.SetBounds(12, 105, 98, 30);
                ComboBox feed = new ComboBox();
                feed.DropDownStyle = ComboBoxStyle.DropDownList;
                feed.Items.Add("(nothing: stays as typed)");
                foreach (string[] f in DashFeeds)
                {
                    feed.Items.Add(f[1]);
                }
                feed.SelectedIndex = 0;
                feed.SetBounds(112, 102, 308, 22);
                Label note = new Label();
                note.Text = "The new gauge gets its own tree (gauge > group > text) and slides, like the original. A value can feed only one text: choosing one takes it from the text that had it. Move it on the preview afterwards.";
                note.SetBounds(12, 140, 408, 50);
                Button ok = new Button();
                ok.Text = "Add";
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(246, 200, 84, 26);
                Button cancel = new Button();
                cancel.Text = "Cancel";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds(336, 200, 84, 26);
                dialog.Controls.AddRange(new Control[] { l1, from, l2, nameBox, l3, textBox, l4, feed, note, ok, cancel });
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;
                ForzaTheme.Apply(dialog);
                if (dialog.ShowDialog(this) != DialogResult.OK || nameBox.Text.Trim().Length == 0)
                {
                    return;
                }
                AnarkDash.Node gauge = dash.Duplicate(usable[from.SelectedIndex], nameBox.Text.Trim(), false);
                if (gauge == null)
                {
                    this.SetStatus("Dash: " + dash.Blocked + ".");
                    return;
                }
                AnarkDash.Node text = dash.FirstText(gauge);
                dash.SetText(text, textBox.Text);
                string fed = "";
                if (feed.SelectedIndex > 0)
                {
                    bool done;
                    string before = dash.BindChannel(DashFeeds[feed.SelectedIndex - 1][0], text, out done);
                    fed = done ? "; fed with " + DashFeeds[feed.SelectedIndex - 1][1] + ((before != null && before.StartsWith("parent.")) ? " (taken from " + before.Substring(before.LastIndexOf('.') + 1) + ")" : "") : "; the value could not be set";
                }
                this.FillDashTree(dash);
                this.dashCanvas.Selection.Clear();
                this.dashCanvas.Selection.Add(gauge);
                this.dashCanvas.Invalidate();
                this.SelectDashNode(gauge);
                this.DashEdited(true, "new gauge" + fed, false);
            }
        }

        // Add > Text / Rectangle / Image (no longer in the menu): a copy of the first element of that kind the dash has (see AnarkDash.Template),
        // with the text, font, size, color or texture asked here, put in the middle of the dash. Only what the game
        // already has can be used: the fonts the dash's texts use, the textures in this car's digitalgauge folder.
        private void AddDashElement(string kind)
        {
            AnarkDash dash = this.dashCanvas.Dash;
            if (dash == null)
            {
                return;
            }
            AnarkDash.Node template = dash.Template(kind);
            if (template == null)
            {
                this.SetStatus("Dash: this dash has no " + ((kind == "text") ? "text" : (kind == "rectangle") ? "flat-color rectangle" : "textured rectangle") + " to base a new one on.");
                return;
            }
            AnarkDash.Node added = null;
            string name = "New_" + kind;
            for (int n = 2; dash.Fbf.Records.Exists(delegate(AnarkFbf.Record r) { return r.Name == name; }); n++)
            {
                name = "New_" + kind + n;
            }
            using (Form dialog = new Form())
            {
                dialog.Text = "Add " + kind;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MinimizeBox = dialog.MaximizeBox = false;
                List<Control> controls = new List<Control>();
                int y = 12;
                TextBox nameBox = new TextBox();
                TextBox textBox = null;
                ComboBox fontBox = null, textureBox = null;
                NumericUpDown sizeBox = null, widthBox = null, heightBox = null;
                TextBox colorBox = new TextBox();
                Label l = new Label();
                l.Text = "Name:";
                l.SetBounds(12, y + 3, 90, 18);
                nameBox.Text = name;
                nameBox.SetBounds(110, y, 290, 22);
                controls.Add(l);
                controls.Add(nameBox);
                y += 30;
                if (kind == "text")
                {
                    l = new Label();
                    l.Text = "Text:";
                    l.SetBounds(12, y + 3, 90, 18);
                    textBox = new TextBox();
                    textBox.Text = "Text";
                    textBox.SetBounds(110, y, 290, 22);
                    controls.Add(l);
                    controls.Add(textBox);
                    y += 30;
                    l = new Label();
                    l.Text = "Font:";
                    l.SetBounds(12, y + 3, 90, 18);
                    fontBox = new ComboBox();
                    fontBox.DropDownStyle = ComboBoxStyle.DropDownList;
                    foreach (string font in this.DashFonts())
                    {
                        fontBox.Items.Add(font);
                    }
                    string current = this.DashString(template, "font");
                    fontBox.SelectedIndex = Math.Max(0, fontBox.Items.IndexOf(current));
                    fontBox.SetBounds(110, y, 290, 22);
                    controls.Add(l);
                    controls.Add(fontBox);
                    y += 30;
                    l = new Label();
                    l.Text = "Size:";
                    l.SetBounds(12, y + 3, 90, 18);
                    sizeBox = new NumericUpDown();
                    sizeBox.Minimum = 1;
                    sizeBox.Maximum = 1000;
                    sizeBox.Value = (decimal) Math.Max(1, Math.Min(1000, template.Element.GetFloat("size", 40)));
                    sizeBox.SetBounds(110, y, 90, 22);
                    controls.Add(l);
                    controls.Add(sizeBox);
                    y += 30;
                }
                else if (kind == "rectangle")
                {
                    l = new Label();
                    l.Text = "Width x height:";
                    l.SetBounds(12, y + 3, 96, 18);
                    widthBox = new NumericUpDown();
                    heightBox = new NumericUpDown();
                    widthBox.Minimum = heightBox.Minimum = 1;
                    widthBox.Maximum = heightBox.Maximum = 10000;
                    widthBox.Value = heightBox.Value = 100;
                    widthBox.SetBounds(110, y, 90, 22);
                    heightBox.SetBounds(210, y, 90, 22);
                    controls.Add(l);
                    controls.Add(widthBox);
                    controls.Add(heightBox);
                    y += 30;
                }
                else
                {
                    string current = AnarkDash.ImageOf(AnarkDash.MaterialOf(template)).Record.Path;
                    l = new Label();
                    l.Text = "Texture:";
                    l.SetBounds(12, y + 3, 90, 18);
                    textureBox = new ComboBox();
                    textureBox.DropDownStyle = ComboBoxStyle.DropDownList;
                    foreach (string path in this.DashTextures(current))
                    {
                        textureBox.Items.Add(path);
                    }
                    textureBox.SelectedIndex = Math.Max(0, textureBox.Items.IndexOf(current));
                    textureBox.SetBounds(110, y, 290, 22);
                    controls.Add(l);
                    controls.Add(textureBox);
                    y += 30;
                }
                if (kind != "image")
                {
                    l = new Label();
                    l.Text = "Color (#RRGGBB):";
                    l.SetBounds(12, y + 3, 96, 18);
                    colorBox.Text = GameColor.Hex(dash.GetColor(template));
                    colorBox.SetBounds(110, y, 90, 22);
                    controls.Add(l);
                    controls.Add(colorBox);
                    y += 30;
                }
                Label note = new Label();
                note.Text = "A copy of " + template.Name + ", put in the middle of the dash and in the last group (the backdrop). The game does not feed it: it stays as set here. Move and resize it on the preview afterwards.";
                note.SetBounds(12, y + 4, 388, 44);
                controls.Add(note);
                y += 54;
                Button ok = new Button();
                ok.Text = "Add";
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(226, y, 84, 26);
                Button cancel = new Button();
                cancel.Text = "Cancel";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds(316, y, 84, 26);
                controls.Add(ok);
                controls.Add(cancel);
                dialog.ClientSize = new Size(412, y + 38);
                dialog.Controls.AddRange(controls.ToArray());
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;
                ForzaTheme.Apply(dialog);
                Color color = Color.White;
                if (dialog.ShowDialog(this) != DialogResult.OK || nameBox.Text.Trim().Length == 0)
                {
                    return;
                }
                if (kind != "image" && !DashColorEditor.TryParse(colorBox.Text, out color))
                {
                    this.SetStatus("Dash: the color must look like #RRGGBB.");
                    return;
                }
                AnarkDash.Node copy = dash.Duplicate(template, nameBox.Text.Trim(), false);
                if (copy == null)
                {
                    this.SetStatus("Dash: " + dash.Blocked + ".");
                    return;
                }
                added = copy;
                dash.PlaceAt(copy, PointF.Empty, copy.Z);
                if (kind == "text")
                {
                    dash.SetText(copy, textBox.Text);
                    dash.SetFont(copy, (string) fontBox.SelectedItem);
                    dash.SetFloat(copy, copy.Element.Find("size"), (float) sizeBox.Value);
                    dash.SetColor(copy, color);
                }
                else if (kind == "rectangle")
                {
                    dash.SetTransform(copy, "scale.x", (float) widthBox.Value / 100f);
                    dash.SetTransform(copy, "scale.y", (float) heightBox.Value / 100f);
                    dash.SetColor(copy, color);
                }
                else
                {
                    dash.SetTexture(copy, (string) textureBox.SelectedItem);
                }
            }
            this.FillDashTree(dash);
            this.dashCanvas.Selection.Clear();
            this.dashCanvas.Selection.Add(added);
            this.dashCanvas.Invalidate();
            this.SelectDashNode(added);
            this.DashEdited(true, "new " + kind, false);
        }

        private TreeNode AddDashNode(TreeNodeCollection parent, AnarkDash.Node node)
        {
            string type = (node.Record != null) ? node.Record.Type.ToString() : node.Element.Type.ToString();
            TreeNode tree = new TreeNode(string.Format("{0}  [{1}]", node.Name, type));
            tree.Tag = node;
            if (node.Element.Type == AnarkBgf.ElementType.Behaviour)
            {
                tree.ForeColor = SystemColors.GrayText;
            }
            parent.Add(tree);
            foreach (AnarkDash.Node child in node.Children)
            {
                this.AddDashNode(tree.Nodes, child);
            }
            return tree;
        }

        private void SelectDashNode(AnarkDash.Node node)
        {
            this.dashSelecting = true;
            try
            {
                this.tvDash.SelectedNode = (node != null) ? FindDashTreeNode(this.tvDash.Nodes, node) : null;
            }
            finally
            {
                this.dashSelecting = false;
            }
            this.ShowDashProperties(node);
        }

        private static TreeNode FindDashTreeNode(TreeNodeCollection nodes, AnarkDash.Node node)
        {
            foreach (TreeNode tree in nodes)
            {
                if (tree.Tag == node)
                {
                    return tree;
                }
                TreeNode found = FindDashTreeNode(tree.Nodes, node);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        // Texture of a dash image: digitalgauge\<name>.xds of this zip, or of the car the path names
        // (a dash taken from another car loads that car's textures, see anark notes).
        private Bitmap LoadDashTexture(string gamePath)
        {
            string car;
            string name = AnarkDash.TextureEntry(gamePath, out car);
            if (name == null)
            {
                return null;
            }
            ForzaZip source = this.zip;
            ForzaZip.Entry texture = this.zip.Find(name);
            if (texture == null && car != null && !this.zip.IsFolder)
            {
                string other = Path.Combine(Path.GetDirectoryName(this.zip.FilePath), car + ".zip");
                if (File.Exists(other))
                {
                    source = new ForzaZip(other);
                    texture = source.Find(name);
                }
            }
            return (texture != null) ? new XdsTexture(source.Read(texture)).GetLevelBitmap(0) : null;
        }

        // Fills the inspector for a node: only the values the element has; a value that cannot be changed without
        // adding something to the files is shown read only.
        private void ShowDashProperties(AnarkDash.Node node)
        {
            if (node == null)
            {
                this.pgDash.SelectedObject = null;
                return;
            }
            if (this.dashCanvas.Selection.Count > 1)
            {
                this.ShowDashMulti(this.dashCanvas.Selection);
                return;
            }
            AnarkDash dash = this.dashCanvas.Dash;
            AnarkFbf.Record rec = node.Record;
            DashInspector view = new DashInspector(node.Name);

            view.Add("Name", DashInspector.CategoryElement, "Name of the element in the .fbf.", typeof(string), delegate { return node.Name; }, null);
            view.Add("Type", DashInspector.CategoryElement, "Kind of element.", typeof(string),
                delegate { return (rec != null) ? rec.Type.ToString() : node.Element.Type.ToString(); }, null);
            view.Add("Id", DashInspector.CategoryElement, "Record id: joins the .bgf, .bsg and .fbf.", typeof(string), delegate { return node.Element.Id.ToString(); }, null);

            bool spatial = node.Element.Type == AnarkBgf.ElementType.Node || node.Element.Type == AnarkBgf.ElementType.Text || node.Element.Type == AnarkBgf.ElementType.Component;
            if (spatial)
            {
                foreach (object[] t in DashTransforms)
                {
                    string name = (string) t[0];
                    float factor = (float) t[2], min = (float) t[3], max = (float) t[4];
                    string source = dash.TransformSource(node, name);
                    string description = (source != null)
                        ? "Stored in the ." + source + ". Drag on the preview to change position and size."
                        : "This element has no such value, so it cannot be changed without adding one to the files.";
                    view.Add((string) t[1], DashInspector.CategoryTransform, description, typeof(float),
                        delegate { return dash.GetTransform(node, name) * factor; },
                        (source == null) ? null : (DashSet) delegate(object value)
                        {
                            float shown = Math.Max(min, Math.Min(max, Convert.ToSingle(value)));
                            return this.DashApply(dash.SetTransform(node, name, shown / factor), name);
                        });
                }
            }

            bool isText = rec != null && rec.Type == AnarkFbf.RecordType.Text;
            if (rec != null && (isText || rec.Type == AnarkFbf.RecordType.Model))
            {
                string source = dash.ColorSource(node);
                view.Add("Color", DashInspector.CategoryAppearance, (source != null) ? "Stored in the ." + source + ". Click the arrow for the color picker, or type #RRGGBB." : "No editable color on this element.",
                    typeof(string), delegate { return GameColor.Hex(dash.GetColor(node)); },
                    (source == null) ? null : (DashSet) delegate(object value)
                    {
                        Color color;
                        return DashColorEditor.TryParse(value as string, out color) && this.DashApply(dash.SetColor(node, color), "color");
                    }).Color();
            }
            if (isText)
            {
                string textValue = this.DashString(node, "textstring");
                view.Add("Text", DashInspector.CategoryAppearance, "The text shown (a Latin-1 string stored in the .bgf).", typeof(string),
                    delegate { return this.DashString(node, "textstring") ?? rec.Text; },
                    (textValue == null) ? null : (DashSet) delegate(object value)
                    {
                        string text = (string) value ?? "";
                        return text == this.DashString(node, "textstring") || this.DashApply(dash.SetText(node, text), "text");
                    });
                string fontValue = this.DashString(node, "font");
                view.Add("Font", DashInspector.CategoryAppearance, "Font of the text: pick one used by this dash, or type a font the game has.", typeof(string),
                    delegate { return this.DashString(node, "font") ?? rec.Font; },
                    (fontValue == null) ? null : (DashSet) delegate(object value)
                    {
                        string font = (string) value ?? "";
                        return font == this.DashString(node, "font") || this.DashApply(dash.SetFont(node, font), "font");
                    }).Choices(delegate { return this.DashFonts().ToArray(); });
            }
            AnarkDash.Node image = dash.TextureNode(node);
            if (image != null)
            {
                view.Add("Texture", DashInspector.CategoryAppearance, "GAME:\\ path of the image (.fbf). The list holds the images of this dash and the .xds of its digitalgauge folder.", typeof(string),
                    delegate { return image.Record.Path; },
                    delegate(object value)
                    {
                        string path = (string) value ?? "";
                        return path == image.Record.Path || this.DashApply(dash.SetTexture(node, path), "texture");
                    }).Choices(delegate { return this.DashTextures(image.Record.Path).ToArray(); });
            }

            foreach (AnarkBgf.Property p in node.Element.Properties)
            {
                AnarkBgf.Property property = p;
                string name = p.Name;
                if (name != null && Array.IndexOf(DashHandledNames, name) >= 0)
                {
                    continue;
                }
                string caption = name ?? ("hash " + p.NameHash.ToString("X7"));
                string description = (p.IsBehaviourParameter ? "Behaviour parameter" : "Graph property") + " (" + p.Kind + "), stored in the .bgf.";
                string[] labels = (p.Kind == AnarkBgf.ValueKind.Int && name != null) ? DashEnumLabels(name) : null;
                switch (p.Kind)
                {
                    case AnarkBgf.ValueKind.Float:
                        view.Add(caption, DashInspector.CategoryGraph, description, typeof(float), delegate { return property.Float; },
                            delegate(object value) { return this.DashApply(dash.SetFloat(node, property, Convert.ToSingle(value)), caption); });
                        break;
                    case AnarkBgf.ValueKind.Bool:
                        view.Add(caption, DashInspector.CategoryGraph, description, typeof(bool), delegate { return property.Value != 0; },
                            delegate(object value) { return this.DashApply(dash.SetRaw(node, property, ((bool) value) ? 1u : 0u), caption); });
                        break;
                    case AnarkBgf.ValueKind.String:
                        if (p.IsBehaviourParameter && caption.EndsWith("target") || caption.Contains("target ("))
                        {
                            view.Add(caption, DashInspector.CategoryGraph, "Data contract: the element that receives this value from the game, as a path (pick one, or Scene.UIContract for none). Setting a path switches the 'on' value before it on.", typeof(string),
                                delegate { return dash.Bgf.GetString(property) ?? ""; },
                                delegate(object value) { return this.DashApply(dash.SetTarget(node, property, (string) value ?? ""), caption); })
                                .Choices(delegate { return dash.Paths().ToArray(); });
                            break;
                        }
                        view.Add(caption, DashInspector.CategoryGraph, description, typeof(string), delegate { return dash.Bgf.GetString(property) ?? ""; },
                            delegate(object value) { return this.DashApply(dash.SetStringProperty(node, property, (string) value), caption); });
                        break;
                    default:
                        if (labels != null)
                        {
                            view.Add(caption, DashInspector.CategoryGraph, description, typeof(string),
                                delegate { return (property.Value < labels.Length) ? labels[property.Value] : ((int) property.Value).ToString(); },
                                delegate(object value)
                                {
                                    int index = Array.IndexOf(labels, (string) value);
                                    return index >= 0 && this.DashApply(dash.SetRaw(node, property, (uint) index), caption);
                                }).Choices(labels, true);
                        }
                        else
                        {
                            view.Add(caption, DashInspector.CategoryGraph, description, typeof(int), delegate { return (int) property.Value; },
                                delegate(object value) { return this.DashApply(dash.SetRaw(node, property, (uint) (int) value), caption); });
                        }
                        break;
                }
            }
            this.pgDash.SelectedObject = view;
        }

        // Several elements selected: the values they share. The inspector shows the main (last) element's value;
        // a change is written to every selected element that has that value.
        private void ShowDashMulti(List<AnarkDash.Node> nodes)
        {
            AnarkDash dash = this.dashCanvas.Dash;
            AnarkDash.Node main = nodes[nodes.Count - 1];
            DashInspector view = new DashInspector(nodes.Count + " elements");
            view.Add("Selected", DashInspector.CategoryElement, "Edits below apply to all of them (Ctrl+click adds or removes one).", typeof(string),
                delegate { return nodes.Count + " elements"; }, null);

            foreach (object[] t in DashTransforms)
            {
                string name = (string) t[0];
                float factor = (float) t[2], min = (float) t[3], max = (float) t[4];
                AnarkDash.Node shown = null;
                int count = 0;
                foreach (AnarkDash.Node n in nodes)
                {
                    if (dash.TransformSource(n, name) != null)
                    {
                        count++;
                        if (shown == null || n == main)
                        {
                            shown = n;
                        }
                    }
                }
                if (count == 0)
                {
                    continue;
                }
                AnarkDash.Node reference = shown;
                view.Add((string) t[1], DashInspector.CategoryTransform,
                    string.Format("Shown for {0}; a change is written to the {1} selected elements that have it (position: aligns them).", reference.Name, count),
                    typeof(float), delegate { return dash.GetTransform(reference, name) * factor; },
                    delegate(object value)
                    {
                        float stored = Math.Max(min, Math.Min(max, Convert.ToSingle(value))) / factor;
                        bool any = false;
                        foreach (AnarkDash.Node n in nodes)
                        {
                            any |= dash.SetTransform(n, name, stored);
                        }
                        return this.DashApply(any, name);
                    });
            }

            List<AnarkDash.Node> colored = nodes.FindAll(delegate(AnarkDash.Node n) { return dash.ColorSource(n) != null; });
            if (colored.Count > 0)
            {
                AnarkDash.Node reference = colored.Contains(main) ? main : colored[0];
                view.Add("Color", DashInspector.CategoryAppearance, "Written to the " + colored.Count + " selected elements that have an editable color.", typeof(string),
                    delegate { return GameColor.Hex(dash.GetColor(reference)); },
                    delegate(object value)
                    {
                        Color color;
                        if (!DashColorEditor.TryParse(value as string, out color))
                        {
                            return false;
                        }
                        bool any = false;
                        foreach (AnarkDash.Node n in colored)
                        {
                            any |= dash.SetColor(n, color);
                        }
                        return this.DashApply(any, "color");
                    }).Color();
            }

            foreach (string name in new string[] { "horzalign", "vertalign" })
            {
                string[] labels = DashEnumLabels(name);
                List<AnarkBgf.Property> found = new List<AnarkBgf.Property>();
                AnarkBgf.Property mainProperty = null;
                foreach (AnarkDash.Node n in nodes)
                {
                    AnarkBgf.Property p = n.Element.Find(name);
                    if (p != null && p.Kind == AnarkBgf.ValueKind.Int)
                    {
                        found.Add(p);
                        if (n == main)
                        {
                            mainProperty = p;
                        }
                    }
                }
                if (found.Count == 0)
                {
                    continue;
                }
                AnarkBgf.Property reference = mainProperty ?? found[0];
                view.Add(name, DashInspector.CategoryGraph, "Written to the " + found.Count + " selected elements that have it.", typeof(string),
                    delegate { return (reference.Value < labels.Length) ? labels[reference.Value] : ((int) reference.Value).ToString(); },
                    delegate(object value)
                    {
                        int index = Array.IndexOf(labels, (string) value);
                        if (index < 0)
                        {
                            return false;
                        }
                        bool any = false;
                        foreach (AnarkBgf.Property p in found)
                        {
                            any |= dash.SetRaw(null, p, (uint) index);
                        }
                        return this.DashApply(any, name);
                    }).Choices(labels, true);
            }
            this.pgDash.SelectedObject = view;
        }

        private bool DashApply(bool done, string what)
        {
            this.DashEdited(done, what, false);
            return done;
        }

        // The graph string of a text node, or null when it has none.
        private string DashString(AnarkDash.Node node, string name)
        {
            AnarkBgf.Property p = node.Element.Find(name);
            return (p != null && p.Kind == AnarkBgf.ValueKind.String) ? this.dashCanvas.Dash.Bgf.GetString(p) : null;
        }

        // Fonts used by the texts of this dash (other names must be fonts the game has).
        private List<string> DashFonts()
        {
            List<string> fonts = new List<string>();
            foreach (AnarkDash.Node n in this.dashCanvas.Dash.Nodes)
            {
                if (n.Record != null && n.Record.Type == AnarkFbf.RecordType.Text)
                {
                    string font = this.DashString(n, "font");
                    if (!string.IsNullOrEmpty(font) && !fonts.Contains(font))
                    {
                        fonts.Add(font);
                    }
                }
            }
            fonts.Sort(StringComparer.OrdinalIgnoreCase);
            return fonts;
        }

        // Images of this dash, plus every digitalgauge\<name>.xds of the zip under the folder of the current path.
        private List<string> DashTextures(string current)
        {
            List<string> paths = new List<string>();
            foreach (AnarkDash.Node n in this.dashCanvas.Dash.Nodes)
            {
                if (n.Record != null && n.Record.Type == AnarkFbf.RecordType.Image && !paths.Contains(n.Record.Path))
                {
                    paths.Add(n.Record.Path);
                }
            }
            int slash = current.LastIndexOf('\\');
            if (slash > 0)
            {
                string folder = current.Substring(0, slash + 1);
                foreach (ForzaZip.Entry e in this.zip.Entries)
                {
                    string name = e.Name.Replace('\\', '/');
                    if (name.StartsWith("digitalgauge/", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".xds", StringComparison.OrdinalIgnoreCase)
                        && name.IndexOf('/', "digitalgauge/".Length) < 0)
                    {
                        string stem = name.Substring("digitalgauge/".Length, name.Length - "digitalgauge/".Length - 4);
                        string path = folder + stem.ToUpperInvariant() + ".TGA";
                        if (!paths.Exists(delegate(string p) { return string.Equals(p, path, StringComparison.OrdinalIgnoreCase); }))
                        {
                            paths.Add(path);
                        }
                    }
                }
            }
            paths.Sort(StringComparer.OrdinalIgnoreCase);
            return paths;
        }


        // After an edit: preview, and the changed files as pending entries of the archive. refresh: the inspector
        // rereads its values (the edit came from the preview, not from the inspector).
        private void DashEdited(bool done, string what, bool refresh)
        {
            if (!done)
            {
                this.SetStatus("Dash: " + what + " cannot be changed here.");
                return;
            }
            AnarkDash dash = this.dashCanvas.Dash;
            List<string> files = new List<string>();
            if (dash.FbfChanged)
            {
                this.zip.Replace(this.dashFbfEntry, dash.Fbf.Write());
                this.RefreshDashEntry(this.dashFbfEntry, files);
            }
            if (dash.BgfChanged)
            {
                this.zip.Replace(this.dashBgfEntry, dash.Bgf.Write());
                this.RefreshDashEntry(this.dashBgfEntry, files);
            }
            if (dash.BsgChanged && this.dashBsgEntry != null)
            {
                this.zip.Replace(this.dashBsgEntry, dash.Bsg.Write());
                this.RefreshDashEntry(this.dashBsgEntry, files);
            }
            this.dashCanvas.Invalidate();
            if (refresh)
            {
                this.pgDash.Refresh();
            }
            AnarkDash.Node node = this.dashCanvas.Selected;
            int count = this.dashCanvas.Selection.Count;
            this.SetStatus(string.Format("Dash: {0} of {1} changed in {2} (pending: Save to write the archive)",
                what, (count > 1) ? count + " elements" : (node != null) ? node.Name : "the dash", string.Join(", ", files.ToArray())));
        }

        private void RefreshDashEntry(ForzaZip.Entry entry, List<string> files)
        {
            files.Add(EntryFileName(entry.Name));
            TreeNode node = this.FindEntryNode(entry);
            if (node != null)
            {
                this.RefreshNode(node);
            }
            else
            {
                this.UpdateTitle();
            }
        }
    }
}
