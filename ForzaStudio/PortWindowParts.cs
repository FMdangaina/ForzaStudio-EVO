// Parts the FM3 and FM2 port windows share with the FM4 window (CarPortForm), so the three look and work alike
// (user 2026-11-28: for 0.0.8 the FM2 and FM3 windows must match FM4's in size and functions; docs/PORT_PARITY.md).
namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Windows.Forms;

    internal static class PortWindowParts
    {
        // The window's size, as the FM4 window's: most of the screen (at 1360 x 900 the car list's columns and the pack's
        // scrolled sideways, user 2026-11-13), never below the minimum, never above the screen's working area.
        public static void SizeToScreen(Form form)
        {
            form.MinimumSize = new Size(1100, 720);
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            form.Size = new Size(Math.Max(1100, Math.Min(1900, work.Width - 40)), Math.Max(720, Math.Min(1080, work.Height - 40)));
            form.StartPosition = FormStartPosition.CenterScreen;
        }

        // The window's title: "Import Car From Another Forza: FH1 > FM3" / "Create DLC Package: FH1 > FM3" once the source
        // game is known (sourceTag null before the cars load).
        public static string Title(PortMode mode, string sourceTag, ForzaGame target)
        {
            string what = (mode == PortMode.Dlc) ? "Create DLC Package" : "Import Car From Another Forza";
            return what + ": " + (string.IsNullOrEmpty(sourceTag) ? "" : sourceTag + " > ") + GameInfo.Tag(target);
        }

        private sealed class SourceGame
        {
            public string Folder;
            public string Caption;      // "Forza Motorsport 4"

            public override string ToString()
            {
                return this.Folder;     // what the box's text becomes when it is picked
            }
        }

        // The source row, as the FM4 window's: "Source game:", a list of the games set in Tools > Settings (any game folder
        // can be typed or browsed too), Folder... and Package.... The target game is left out of the list.
        // changed: the text changed (the info line follows); packages: the Package... button (the window's own way).
        public static ComboBox SourceRow(Form owner, Panel parent, int y, string value, ForzaGame target, EventHandler changed, EventHandler packages, string packagesTip)
        {
            Label label = new Label();
            label.Text = "Source game:";
            label.Location = new Point(10, y + 4);
            label.AutoSize = true;
            ComboBox box = new ComboBox();
            box.DropDownStyle = ComboBoxStyle.DropDown;
            box.DrawMode = DrawMode.OwnerDrawFixed;
            box.Location = new Point(215, y);
            box.Width = parent.Width - 215 - 282;
            box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            box.Text = value;
            box.DropDown += delegate { FillSourceGames(box, target); };
            box.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                e.DrawBackground();
                if (e.Index < 0)
                {
                    return;
                }
                SourceGame game = box.Items[e.Index] as SourceGame;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                Rectangle area = e.Bounds;
                if (game == null)
                {
                    TextRenderer.DrawText(e.Graphics, box.Items[e.Index].ToString(), box.Font, area, ForzaTheme.TextDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                    return;
                }
                TextRenderer.DrawText(e.Graphics, game.Caption, ForzaTheme.Bold, new Rectangle(area.X + 2, area.Y, 190, area.Height), selected ? Color.White : ForzaTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(e.Graphics, game.Folder, box.Font, new Rectangle(area.X + 196, area.Y, area.Width - 198, area.Height), selected ? Color.White : ForzaTheme.TextDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis);
            };
            if (changed != null)
            {
                box.TextChanged += changed;
            }
            FillSourceGames(box, target);

            Button browse = new Button();
            browse.Text = "Folder...";
            browse.Size = new Size(72, 24);
            browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            browse.Location = new Point(parent.Width - 276, y - 2);
            new ToolTip().SetToolTip(browse, "Another game folder (with default.xex and Media)");
            browse.Click += delegate
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "The source game folder (with default.xex and Media)";
                    dialog.ShowNewFolderButton = false;
                    if (Directory.Exists(box.Text))
                    {
                        dialog.SelectedPath = box.Text;
                    }
                    if (dialog.ShowDialog(owner) == DialogResult.OK)
                    {
                        box.Text = dialog.SelectedPath;
                    }
                }
            };
            Button file = new Button();
            file.Text = "Package...";
            file.Size = new Size(72, 24);
            file.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            file.Location = new Point(parent.Width - 200, y - 2);
            new ToolTip().SetToolTip(file, packagesTip);
            file.Click += packages;
            parent.Controls.Add(label);
            parent.Controls.Add(box);
            parent.Controls.Add(browse);
            parent.Controls.Add(file);
            return box;
        }

        // The list of the source box: FM4 and the source games of Tools > Settings > Folders, without the target game
        // (read again each time the list opens).
        public static void FillSourceGames(ComboBox box, ForzaGame target)
        {
            string text = box.Text;
            box.Items.Clear();
            string fm4 = GameInfo.ConfiguredFolder(ForzaGame.Motorsport4);
            if (target != ForzaGame.Motorsport4 && !string.IsNullOrEmpty(fm4))
            {
                box.Items.Add(new SourceGame { Folder = fm4, Caption = GameInfo.Name(ForzaGame.Motorsport4) });
            }
            foreach (string[] source in SettingsForm.SourceFolders)
            {
                string folder = AppSettings.Get(source[0], "");
                string caption = source[1].TrimEnd(':');
                if (folder.Length > 0 && !string.Equals(caption, GameInfo.Name(target), StringComparison.OrdinalIgnoreCase))
                {
                    box.Items.Add(new SourceGame { Folder = folder, Caption = caption });
                }
            }
            if (box.Items.Count == 0)
            {
                box.Items.Add("No source games yet: set them in Tools > Settings > Folders");
            }
            box.Text = text;
        }

        // The line under the source, as the FM4 window's: the game, its DLC in Xenia (headers only, DlcCatalog), and what
        // is missing and where it is set. problem: something is missing (drawn yellow). packFiles: the package files added
        // with Package... (listed with the game's cars).
        public static string DescribeSource(string source, bool withDlc, ForzaGame target, int packFiles, out bool problem)
        {
            problem = true;
            string extra = (packFiles > 0) ? string.Format(CultureInfo.InvariantCulture, "  + {0} package file(s) (Package...).", packFiles) : "";
            if (source.Length == 0)
            {
                return "Choose the source game in the list (the games set in Tools > Settings > Folders), or Folder... / Package...";
            }
            if (!Directory.Exists(source))
            {
                return "Folder not found.";
            }
            ForzaGame game;
            try
            {
                game = GameInfo.Detect(source);
            }
            catch (Exception)
            {
                game = ForzaGame.Unknown;
            }
            if (game == ForzaGame.Unknown)
            {
                return "Not a game folder (no default.xex / Media of a Forza this window reads).";
            }
            if (game == target)
            {
                return GameInfo.Name(game) + ": the same game as the target, not a source.";
            }
            string text = GameInfo.Name(game) + " (game).";
            if (!withDlc)
            {
                problem = false;
                return text + "  DLC: not included (box below)." + extra;
            }
            if (AppSettings.Get(SettingsForm.XeniaKey, "").Length == 0)
            {
                return text + "  DLC: the Xenia folder is not set (Tools > Settings > Folders > Xenia)." + extra;
            }
            string content = InstalledDlc.ContentFolder(game);
            if (content == null)
            {
                problem = false;
                return text + "  DLC: none installed in Xenia for this game." + extra;
            }
            int packs = 0;
            int known = 0;
            int cars = 0;
            uint title = InstalledDlc.TitleId(game);
            Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.GetFiles(content))
            {
                DlcCatalog.Header header = DlcCatalog.ReadHeader(file);
                if (header == null || header.TitleId != title || seen.ContainsKey(header.ContentId))
                {
                    continue;
                }
                seen[header.ContentId] = true;
                packs++;
                DlcCatalog.Pack pack = DlcCatalog.Find(header);
                if (pack != null)
                {
                    known++;
                    cars += pack.Cars.Count;
                }
            }
            problem = false;
            string dlc = string.Format(CultureInfo.InvariantCulture, "  DLC in Xenia: {0} packages", packs);
            if (known > 0)
            {
                dlc += string.Format(CultureInfo.InvariantCulture, " ({0} cars in the {1} known ones{2})", cars, known, (known < packs) ? string.Format(CultureInfo.InvariantCulture, ", {0} read when loading", packs - known) : "");
            }
            return text + dlc + "." + extra;
        }

        // The info label under the source box, placed as the FM4 window's.
        public static Label SourceInfo(Panel parent)
        {
            Label info = new Label();
            info.Location = new Point(215, 36);
            info.Size = new Size(parent.Width - 215 - 130, 18);
            info.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            info.AutoEllipsis = true;
            info.ForeColor = ForzaTheme.TextDim;
            parent.Controls.Add(info);
            return info;
        }

        // The colours of the car list at the right of the "Cars" header, as the FM4 window draws them (user 2026-11-08).
        public static void Legend(Control header, string[] names, Color[] colors)
        {
            header.Paint += delegate(object sender, PaintEventArgs e)
            {
                int x = header.Width - 8;
                for (int i = names.Length - 1; i >= 0; i--)
                {
                    Size size = TextRenderer.MeasureText(names[i], ForzaTheme.Regular);
                    x -= size.Width;
                    TextRenderer.DrawText(e.Graphics, names[i], ForzaTheme.Regular, new Point(x, 7), colors[i]);
                    x -= 14;
                    using (Brush dot = new SolidBrush(colors[i]))
                    {
                        e.Graphics.FillEllipse(dot, x, 11, 9, 9);
                    }
                    x -= 14;
                }
            };
            header.Resize += delegate { header.Invalidate(); };
        }

        // The class badge colours of the car list (letter -> colour): the source game's CarClasses, then the target's for
        // the letters it lacks. Empty when neither database has the table (FM2).
        public static Dictionary<string, Color> ClassColors(string source, string target)
        {
            Dictionary<string, Color> colors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in new string[] { source, target })
            {
                try
                {
                    string db = (root == null) ? null : Path.Combine(Path.Combine(Path.Combine(root, "Media"), "db"), "gamedb.slt");
                    if (db == null || !File.Exists(db))
                    {
                        continue;
                    }
                    using (GameDatabase game = new GameDatabase(File.ReadAllBytes(db)))
                    {
                        foreach (KeyValuePair<string, Color> one in game.ClassColors())
                        {
                            if (!colors.ContainsKey(one.Key))
                            {
                                colors[one.Key] = one.Value;
                            }
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                }
                catch (IOException)
                {
                }
            }
            return colors;
        }

        // The Xenia line of a pack panel, as the FM4 window's: the folder of Tools > Settings (set only there) and whether it
        // is there. Returns the text; problem: not set or not found (drawn yellow).
        public static string XeniaInfo(out bool problem)
        {
            string xenia = AppSettings.Get(SettingsForm.XeniaKey, "").Trim();
            problem = true;
            if (xenia.Length == 0)
            {
                return "not set: Settings... > Folders > Xenia";
            }
            if (!Directory.Exists(xenia))
            {
                return xenia + "  (folder not found)";
            }
            problem = false;
            return xenia;
        }

        // Tools > Settings on its Folders tab; true when the user pressed OK.
        public static bool OpenFolderSettings(Form owner)
        {
            using (SettingsForm settings = new SettingsForm("Folders"))
            {
                return settings.ShowDialog(owner) == DialogResult.OK;
            }
        }
    }
}
