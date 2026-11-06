namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.IO;
    using System.Text;
    using System.Windows.Forms;

    // The whole car model as a work folder of OBJs (see CarModelFolder): export every part of the
    // main, lod0 and cockpit files, edit / add / delete OBJs in an editor, import the folder back.
    public partial class ArchiveEditorForm
    {
        // The car's type-2 model files, parsed, by kind; null with a message when there are none.
        private Dictionary<string, CarbinModel> LoadCarModels(string action, out Dictionary<string, ForzaZip.Entry> files)
        {
            files = (this.zip == null) ? new Dictionary<string, ForzaZip.Entry>() : this.CarModelFiles(this.zip, this.ModelFolder);
            if (files.Count == 0)
            {
                MessageBox.Show(this, "Open a car's model (its .carbin) first.", action, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }
            Dictionary<string, CarbinModel> models = new Dictionary<string, CarbinModel>();
            foreach (KeyValuePair<string, ForzaZip.Entry> pair in files)
            {
                byte[] data = this.zip.Read(pair.Value);
                if (CarModelFolder.IsMotorsport2(data))
                {
                    MessageBox.Show(this, "Forza Motorsport 2 models can be exported to a folder, but importing them back is not available yet.", action, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return null;
                }
                models[pair.Key] = new CarbinModel(data);
            }
            return models;
        }

        private static string CarbinName(ForzaZip.Entry entry)
        {
            return entry.Name.Substring(entry.Name.LastIndexOf('/') + 1);
        }

        private void ExportModelFolder()
        {
            // Every model file of the car; FM2 ones go through the viewer's reader.
            Dictionary<string, ForzaZip.Entry> files = (this.zip == null) ? new Dictionary<string, ForzaZip.Entry>() : this.CarModelFiles(this.zip, this.ModelFolder);
            if (files.Count == 0)
            {
                MessageBox.Show(this, "Open a car's model (its .carbin) first.", "Export Model", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string parent;
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Where to export: one folder per .carbin is made here (e.g. toy_supra_98\\lod1\\part.obj, toy_supra_98_lod0\\lod0\\part.obj):";
                string start = AppSettings.Get("Dir.ModelFolder", null);
                if (start != null && Directory.Exists(start))
                {
                    dialog.SelectedPath = start;
                }
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                parent = dialog.SelectedPath;
                AppSettings.Set("Dir.ModelFolder", parent);
            }
            List<string> existing = new List<string>();
            foreach (ForzaZip.Entry entry in files.Values)
            {
                string folder = Path.Combine(parent, CarModelFolder.FolderName(CarbinName(entry)));
                if (Directory.Exists(folder))
                {
                    existing.Add(Path.GetFileName(folder));
                }
            }
            if (existing.Count > 0 && MessageBox.Show(this, string.Join(", ", existing.ToArray()) + " already exist in " + parent + ".\n\nOverwrite them? The OBJs in their lodN folders are replaced by a fresh export (other files are left alone).",
                "Export Model", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                int count = 0;
                List<string> made = new List<string>();
                foreach (string kind in new string[] { CarModelFolder.Main, CarModelFolder.Lod0, CarModelFolder.Cockpit })
                {
                    ForzaZip.Entry entry;
                    if (!files.TryGetValue(kind, out entry))
                    {
                        continue;
                    }
                    string folder = Path.Combine(parent, CarModelFolder.FolderName(CarbinName(entry)));
                    byte[] data = this.zip.Read(entry);
                    count += CarModelFolder.IsMotorsport2(data)
                        ? CarModelFolder.ExportLegacy(data, CarbinName(entry), folder, this.SourceName)
                        : CarModelFolder.Export(new CarbinModel(data), CarbinName(entry), folder, this.SourceName);
                    made.Add(Path.GetFileName(folder));
                }
                this.SetStatus(string.Format("{0:N0} OBJs exported to {1}: {2} (edit them, then Model > Import Model from OBJ Folder)", count, parent, string.Join(", ", made.ToArray())));
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Export Model", MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        // The .carbin folders to import from the folder picked: that folder itself when it is one
        // (e.g. toy_supra_98_lod0), else the car's .carbin folders inside it.
        private static Dictionary<string, string> ModelFoldersIn(string picked, Dictionary<string, ForzaZip.Entry> files)
        {
            Dictionary<string, string> folders = new Dictionary<string, string>();
            foreach (KeyValuePair<string, ForzaZip.Entry> pair in files)
            {
                string name = CarModelFolder.FolderName(CarbinName(pair.Value));
                if (string.Equals(Path.GetFileName(picked.TrimEnd('\\')), name, StringComparison.OrdinalIgnoreCase))
                {
                    folders.Clear();
                    folders[pair.Key] = picked;
                    return folders;
                }
                string inside = Path.Combine(picked, name);
                if (Directory.Exists(inside))
                {
                    folders[pair.Key] = inside;
                }
            }
            return folders;
        }

        private void ImportModelFolder()
        {
            Dictionary<string, ForzaZip.Entry> files;
            Dictionary<string, CarbinModel> models;
            try
            {
                models = this.LoadCarModels("Import Model", out files);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Import Model", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            if (models == null)
            {
                return;
            }
            List<string> expected = new List<string>();
            foreach (ForzaZip.Entry entry in files.Values)
            {
                expected.Add(CarModelFolder.FolderName(CarbinName(entry)));
            }
            Dictionary<string, string> folders;
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "The folder you exported to (with " + string.Join(", ", expected.ToArray()) + "), or one of those folders to import only that .carbin:";
                dialog.ShowNewFolderButton = false;
                string start = AppSettings.Get("Dir.ModelFolder", null);
                if (start != null && Directory.Exists(start))
                {
                    dialog.SelectedPath = start;
                }
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                folders = ModelFoldersIn(dialog.SelectedPath, files);
                if (folders.Count == 0)
                {
                    MessageBox.Show(this, "No model folder of this car in " + dialog.SelectedPath + ".\n\nExpected: " + string.Join(", ", expected.ToArray()) + " (made by Model > Export Model to OBJ Folder).",
                        "Import Model", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                // Remembered: the folder that holds the .carbin folders.
                bool single = false;
                foreach (string folder in folders.Values)
                {
                    single |= string.Equals(folder, dialog.SelectedPath, StringComparison.OrdinalIgnoreCase);
                }
                AppSettings.Set("Dir.ModelFolder", single ? Path.GetDirectoryName(dialog.SelectedPath) : dialog.SelectedPath);
            }

            this.ApplyModelImport(files, models, folders, null, "OBJ");
        }

        // The whole car as one FBX or glTF scene with its carbin hierarchy (see CarModelFbx).
        private void ExportModelFbx(bool gltf)
        {
            string title = gltf ? "Export Model to glTF" : "Export Model to FBX";
            Dictionary<string, CarbinModel> models;
            Dictionary<string, ForzaZip.Entry> files;
            try
            {
                models = this.LoadCarModels(title, out files);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            if (models == null)
            {
                return;
            }
            List<string> wanted = this.ChooseFbxLevels(CarModelFbx.Levels(models), title, gltf ? "glTF" : "FBX");
            if (wanted == null)
            {
                return;
            }
            string path;
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = title;
                dialog.Filter = gltf ? "glTF binary (*.glb)|*.glb|glTF + bin (*.gltf)|*.gltf" : "FBX (*.fbx)|*.fbx";
                dialog.FileName = CarModelFolder.FolderName(CarbinName(files.ContainsKey(CarModelFolder.Main) ? files[CarModelFolder.Main] : First(files)))
                    + ((wanted.Count == 1) ? FbxSuffix(wanted[0]) : "") + (gltf ? ".glb" : ".fbx");
                string start = AppSettings.Get("Dir.ModelFolder", null);
                if (start != null && Directory.Exists(start))
                {
                    dialog.InitialDirectory = start;
                }
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                path = dialog.FileName;
                AppSettings.Set("Dir.ModelFolder", Path.GetDirectoryName(path));
            }
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                Dictionary<string, string> names = new Dictionary<string, string>();
                foreach (KeyValuePair<string, ForzaZip.Entry> pair in files)
                {
                    names[pair.Key] = CarbinName(pair.Value);
                }
                List<string> pictures = new List<string>();
                int count = CarModelFbx.Export(names, models, path, this.SourceName, wanted, this.ExportTextureFinder(pictures));
                string where = (pictures.Count == 0) ? "" : CarModelFbx.IsGltf(path) && path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)
                    ? string.Format(", {0} textures inside", pictures.Count) : string.Format(", {0} textures in {1}", pictures.Count, ExportTexture.TexturesFolder(path));
                this.SetStatus(string.Format("{0:N0} meshes exported to {1}{2} (edit it, then Model > " + (gltf ? "Import Model from glTF" : "Import Model from FBX") + ")", count, path, where));
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        // The texture each material shows in the viewer, for the FBX / glTF export (to look at the UVs in a
        // 3D editor): the one picked by hand in the mesh panel, else the one the game draws it with (the
        // viewer's automatic textures, ShaderTextures). Each picture is decoded once, written opaque as PNG
        // (alpha holds masks in FM4 textures and would hide the colour). 'written' collects the picture names.
        private Func<string, string, uint, ExportTexture> ExportTextureFinder(List<string> written)
        {
            ShaderTextures table = ShaderTextures.For(this.GameRoot);
            Dictionary<string, ExportTexture> bySource = new Dictionary<string, ExportTexture>(StringComparer.OrdinalIgnoreCase);
            return delegate(string material, string kind, uint lod)
            {
                string source = this.ExportTextureSource(table, material, kind, lod);
                if (source == null)
                {
                    return null;
                }
                ExportTexture texture;
                if (bySource.TryGetValue(source, out texture))
                {
                    return texture;
                }
                texture = null;
                try
                {
                    using (Bitmap image = this.LoadRawTexture(source))
                    {
                        texture = new ExportTexture();
                        string file = source.StartsWith(SharedPrefix) ? "shared_" + Path.GetFileName(source.Substring(SharedPrefix.Length))
                            : Path.GetFileName(source.StartsWith(ArchivePrefix) ? source.Substring(ArchivePrefix.Length) : source);
                        texture.Name = Path.GetFileNameWithoutExtension(file) + ".png";
                        texture.Png = OpaquePng(image);
                        written.Add(texture.Name);
                    }
                }
                catch (Exception)
                {
                    texture = null;     // a picture that cannot be read: the material keeps its colour
                }
                bySource[source] = texture;
                return texture;
            };
        }

        private string ExportTextureSource(ShaderTextures table, string material, string kind, uint lod)
        {
            string chosen;
            if (this.materialTextures.TryGetValue(material, out chosen) && chosen.Length > 0 && this.SourceExists(chosen))
            {
                return chosen;
            }
            if (table.Problem != null)
            {
                return null;
            }
            ShaderTextures.Kind where = (kind == CarModelFolder.Cockpit) ? ShaderTextures.Kind.Cockpit
                : (kind == CarModelFolder.Lod0 || lod == 0) ? ShaderTextures.Kind.Lod0 : ShaderTextures.Kind.Main;
            foreach (ShaderTextures.Texture texture in table.ColourTextures(material, where))
            {
                // As SetAutoTexture: a shared texture this game lacks may be carried in the car's own zip.
                string source = (texture.Where == ShaderTextures.Place.Shared) ? SharedPrefix + texture.File
                    : (texture.Where == ShaderTextures.Place.Car) ? ArchivePrefix + texture.File : null;
                if (texture.Where == ShaderTextures.Place.Shared && !this.SourceExists(source))
                {
                    source = ArchivePrefix + texture.File;
                }
                if (source != null && this.SourceExists(source))
                {
                    return source;
                }
            }
            return null;
        }

        private static byte[] OpaquePng(Bitmap image)
        {
            using (Bitmap copy = new Bitmap(image.Width, image.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(copy))
                {
                    graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    graphics.DrawImage(image, 0, 0, image.Width, image.Height);
                }
                Rectangle all = new Rectangle(0, 0, copy.Width, copy.Height);
                System.Drawing.Imaging.BitmapData data = copy.LockBits(all, System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    byte[] pixels = new byte[data.Stride * copy.Height];
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                    for (int i = 3; i < pixels.Length; i += 4)
                    {
                        pixels[i] = 255;
                    }
                    System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
                }
                finally
                {
                    copy.UnlockBits(data);
                }
                using (MemoryStream png = new MemoryStream())
                {
                    copy.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                    return png.ToArray();
                }
            }
        }

        // "|3" -> "_main_lod3", "_lod0|0" -> "_lod0_lod0": a file name for a one-level export.
        private static string FbxSuffix(string key)
        {
            string[] parts = key.Split('|');
            return "_" + ((parts[0].Length == 0) ? "main" : parts[0].Substring(1)) + "_lod" + parts[1];
        }

        // Which model files and LODs to put in the FBX (a small scene is quicker to open and edit).
        // Null when cancelled. The lod0 file's LOD 0 (the close-up body) is preselected.
        private List<string> ChooseFbxLevels(List<CarModelFbx.Level> levels, string title, string format)
        {
            Panel panel = new Panel();
            panel.Size = new Size(430, 40 + (Math.Min(levels.Count, 12) * 20) + 40);
            Label header = new Label();
            header.Text = "Model files and LODs to put in the " + format + ":";
            header.AutoSize = true;
            header.Location = new Point(0, 2);
            panel.Controls.Add(header);
            CheckedListBox list = new CheckedListBox();
            list.CheckOnClick = true;
            list.IntegralHeight = false;
            list.Location = new Point(0, 24);
            list.Size = new Size(430, (Math.Min(levels.Count, 12) * 20) + 12);
            bool any = false;
            foreach (CarModelFbx.Level level in levels)
            {
                bool first = level.Kind == CarModelFolder.Lod0 && level.Lod == 0;
                any |= first;
                list.Items.Add(level.Describe(), first);
            }
            if (!any && list.Items.Count > 0)
            {
                list.SetItemChecked(0, true);
            }
            panel.Controls.Add(list);
            Button all = new Button();
            all.Text = "All";
            all.Size = new Size(70, 26);
            all.Location = new Point(0, list.Bottom + 6);
            all.Click += delegate { for (int i = 0; i < list.Items.Count; i++) { list.SetItemChecked(i, true); } };
            Button none = new Button();
            none.Text = "None";
            none.Size = new Size(70, 26);
            none.Location = new Point(76, list.Bottom + 6);
            none.Click += delegate { for (int i = 0; i < list.Items.Count; i++) { list.SetItemChecked(i, false); } };
            panel.Controls.Add(all);
            panel.Controls.Add(none);
            panel.Height = none.Bottom + 4;
            while (true)
            {
                if (!this.ShowOptions(title, panel))
                {
                    return null;
                }
                List<string> wanted = new List<string>();
                for (int i = 0; i < levels.Count; i++)
                {
                    if (list.GetItemChecked(i))
                    {
                        wanted.Add(levels[i].Key);
                    }
                }
                if (wanted.Count > 0)
                {
                    return wanted;
                }
                MessageBox.Show(this, "Tick at least one model file / LOD.", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private static ForzaZip.Entry First(Dictionary<string, ForzaZip.Entry> files)
        {
            foreach (ForzaZip.Entry entry in files.Values)
            {
                return entry;
            }
            return null;
        }

        private void ImportModelFbx(bool gltf)
        {
            string title = gltf ? "Import Model from glTF" : "Import Model from FBX";
            Dictionary<string, ForzaZip.Entry> files;
            Dictionary<string, CarbinModel> models;
            try
            {
                models = this.LoadCarModels(title, out files);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            if (models == null)
            {
                return;
            }
            string path;
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = title;
                dialog.Filter = gltf ? "glTF (*.glb, *.gltf)|*.glb;*.gltf" : "FBX (*.fbx)|*.fbx";
                string start = AppSettings.Get("Dir.ModelFolder", null);
                if (start != null && Directory.Exists(start))
                {
                    dialog.InitialDirectory = start;
                }
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                path = dialog.FileName;
                AppSettings.Set("Dir.ModelFolder", Path.GetDirectoryName(path));
            }
            Panel options = new Panel();
            options.Size = new Size(470, 70);
            Label what = new Label();
            what.Text = "Only the parts the file has are compared with the model; the others are left as they are.";
            what.AutoSize = false;
            what.Size = new Size(470, 22);
            what.Location = new Point(0, 2);
            CheckBox drop = new CheckBox();
            drop.Text = "Remove the parts the file does not have (at the LODs it has)";
            drop.AutoSize = true;
            drop.Location = new Point(0, 32);
            options.Controls.Add(what);
            options.Controls.Add(drop);
            if (!this.ShowOptions(title, options))
            {
                return;
            }
            Dictionary<string, string> names = new Dictionary<string, string>();
            foreach (KeyValuePair<string, ForzaZip.Entry> pair in files)
            {
                names[pair.Key] = CarbinName(pair.Value);
            }
            string root = null;
            try
            {
                Dictionary<string, string> folders;
                List<string> notes = new List<string>();
                List<string> errors = new List<string>();
                try
                {
                    Cursor.Current = Cursors.WaitCursor;
                    CarModelFbx.PrepareFolders(path, names, models, drop.Checked, out folders, out root, notes, errors);
                }
                catch (Exception exception)
                {
                    MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Hand);
                    return;
                }
                finally
                {
                    Cursor.Current = Cursors.Default;
                }
                if (errors.Count > 0)
                {
                    MessageBox.Show(this, string.Join("\n", errors.ToArray()), title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                this.ApplyModelImport(files, models, folders, notes, CarModelFbx.FormatName(path));
            }
            finally
            {
                if (root != null && Directory.Exists(root))
                {
                    try
                    {
                        Directory.Delete(root, true);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }

        // The import of OBJ folders (the user's, or the ones made from an FBX): plan, confirmation, apply.
        // 'notes' are findings of the FBX check, shown with the plan.
        private void ApplyModelImport(Dictionary<string, ForzaZip.Entry> files, Dictionary<string, CarbinModel> models, Dictionary<string, string> folders, List<string> notes, string source)
        {
            CarModelFolder.Plan plan = this.PrepareModelPlan(folders, models, false);
            if (plan == null)
            {
                return;
            }
            if (notes != null)
            {
                plan.Warnings.InsertRange(0, notes);
            }
            if (plan.IsEmpty)
            {
                // Unchanged OBJs can still be rebuilt: fresh precalc lighting and UV data.
                if (MessageBox.Show(this, "Every " + source + " part matches the model." + ((notes != null) ? "\n\n" + string.Join("\n", notes.ToArray()) : "") + ((plan.Ignored.Count > 0) ? "\n\nIgnored: " + string.Join(", ", plan.Ignored.ToArray()) : "")
                    + "\n\nRebuild every OBJ anyway (recomputes precalc lighting and UV data)?", "Import Model", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    return;
                }
                plan = this.PrepareModelPlan(folders, models, true);
                if (plan == null || plan.IsEmpty)
                {
                    return;
                }
            }
            bool rebuildAll;
            if (!this.ConfirmModelPlan(plan, out rebuildAll))
            {
                return;
            }
            if (rebuildAll)
            {
                // Same plan with the unchanged OBJs too; the template choices are kept.
                CarModelFolder.Plan all = this.PrepareModelPlan(folders, models, true);
                if (all == null)
                {
                    return;
                }
                foreach (CarModelFolder.PartPlan part in all.Parts)
                {
                    foreach (CarModelFolder.PartPlan chosen in plan.Parts)
                    {
                        if (part.IsNew && part.Kind == chosen.Kind && part.Part == chosen.Part)
                        {
                            part.Template = chosen.Template;
                        }
                    }
                }
                plan = all;
            }

            StringBuilder report = new StringBuilder();
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                List<string> changed = CarModelFolder.Apply(plan, models, report);
                foreach (string kind in changed)
                {
                    this.zip.Replace(files[kind], models[kind].ToBytes());
                    this.RefreshEntryNode(files[kind]);
                }
            }
            catch (Exception exception)
            {
                // Nothing was replaced unless every model was rebuilt: the archive is unchanged.
                MessageBox.Show(this, exception.Message + "\n\nThe archive was not changed.", "Import Model", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            this.editSection = null;
            this.ReloadModelKeepingView();
            this.SetStatus(string.Format("Model imported: {0} part changes (pending: Save to write the archive)", plan.Parts.Count));
            this.ShowReport("Import Model", report.ToString());
        }

        // The import plan, or null after showing its errors.
        private CarModelFolder.Plan PrepareModelPlan(Dictionary<string, string> folders, Dictionary<string, CarbinModel> models, bool rebuildAll)
        {
            CarModelFolder.Plan plan;
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                plan = CarModelFolder.Prepare(folders, models, rebuildAll);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Import Model", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return null;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            if (plan.Errors.Count > 0)
            {
                MessageBox.Show(this, "Nothing imported:\n\n" + string.Join("\n", plan.Errors.GetRange(0, Math.Min(20, plan.Errors.Count)).ToArray())
                    + ((plan.Errors.Count > 20) ? string.Format("\n... {0} more", plan.Errors.Count - 20) : ""), "Import Model", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            return plan;
        }

        // The plan in words, with a template choice for each new part. False when cancelled.
        // rebuildAll: the user asked to rebuild the unchanged OBJs too.
        private bool ConfirmModelPlan(CarModelFolder.Plan plan, out bool rebuildAll)
        {
            rebuildAll = false;
            List<CarModelFolder.PartPlan> added = new List<CarModelFolder.PartPlan>();
            StringBuilder text = new StringBuilder();
            foreach (CarModelFolder.PartPlan part in plan.Parts)
            {
                text.AppendLine(part.Describe());
                if (part.IsNew)
                {
                    added.Add(part);
                }
            }
            foreach (string warning in plan.Warnings)
            {
                text.AppendLine();
                text.AppendLine(warning);
            }
            if (plan.Ignored.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("Ignored: " + string.Join(", ", plan.Ignored.ToArray()));
            }

            Panel panel = new Panel();
            int rows = Math.Min(added.Count, 10);
            int listHeight = (added.Count > 0) ? 28 + (rows * 28) : 0;
            panel.Size = new Size(640, 300 + listHeight + 28);
            CheckBox all = new CheckBox();
            all.Text = "Also rebuild the unchanged OBJs (recomputes precalc lighting and UV data)";
            all.AutoSize = true;
            all.Location = new Point(0, 300 + listHeight + 4);
            panel.Controls.Add(all);
            TextBox summary = new TextBox();
            summary.Multiline = true;
            summary.ReadOnly = true;
            summary.ScrollBars = ScrollBars.Both;
            summary.WordWrap = false;
            summary.Font = new Font(FontFamily.GenericMonospace, 9f);
            summary.Size = new Size(640, 290);
            summary.Text = text.ToString();
            panel.Controls.Add(summary);
            Dictionary<CarModelFolder.PartPlan, ComboBox> templates = new Dictionary<CarModelFolder.PartPlan, ComboBox>();
            if (added.Count > 0)
            {
                Panel list = new Panel();
                list.Location = new Point(0, 300);
                list.Size = new Size(640, 28 + (rows * 28));
                list.AutoScroll = added.Count > rows;
                Label header = new Label();
                header.Text = "New parts take materials, damage data and draw order from a template part (nearest one preselected):";
                header.AutoSize = true;
                header.Location = new Point(0, 4);
                list.Controls.Add(header);
                int y = 28;
                foreach (CarModelFolder.PartPlan part in added)
                {
                    Label name = new Label();
                    name.Text = ((part.Kind == CarModelFolder.Main) ? "main" : part.Kind.Substring(1)) + " " + part.Part;
                    name.AutoSize = false;
                    name.Size = new Size(260, 22);
                    name.TextAlign = ContentAlignment.MiddleLeft;
                    name.Location = new Point(0, y);
                    ComboBox combo = new ComboBox();
                    combo.DropDownStyle = ComboBoxStyle.DropDownList;
                    combo.Width = 300;
                    combo.Location = new Point(266, y);
                    combo.Items.AddRange(part.Templates.ToArray());
                    combo.SelectedItem = part.Template;
                    list.Controls.Add(name);
                    list.Controls.Add(combo);
                    templates[part] = combo;
                    y += 28;
                }
                panel.Controls.Add(list);
            }
            if (!this.ShowOptions(string.Format("Import Model ({0} changes)", plan.Parts.Count), panel))
            {
                return false;
            }
            rebuildAll = all.Checked;
            foreach (KeyValuePair<CarModelFolder.PartPlan, ComboBox> pair in templates)
            {
                if (pair.Value.SelectedItem != null)
                {
                    pair.Key.Template = (string) pair.Value.SelectedItem;
                }
            }
            return true;
        }

        // A read-only text window (import reports).
        private void ShowReport(string title, string text)
        {
            using (Form form = new Form())
            {
                form.Text = title;
                form.StartPosition = FormStartPosition.CenterParent;
                form.ShowInTaskbar = false;
                form.MinimizeBox = false;
                form.Font = ForzaTheme.Regular;
                form.ClientSize = new Size(760, 480);
                TextBox box = new TextBox();
                box.Multiline = true;
                box.ReadOnly = true;
                box.ScrollBars = ScrollBars.Both;
                box.WordWrap = false;
                box.Font = new Font(FontFamily.GenericMonospace, 9f);
                box.Dock = DockStyle.Fill;
                box.Text = text;
                form.Controls.Add(box);
                form.BackColor = ForzaTheme.Background;
                ForzaTheme.Apply(form);
                form.ShowDialog(this);
            }
        }
    }
}
