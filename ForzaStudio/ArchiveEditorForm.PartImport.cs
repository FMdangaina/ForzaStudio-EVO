namespace ForzaStudio
{
    using Microsoft.Xna.Framework;
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.IO;
    using System.Windows.Forms;

    // Parts between cars and editors: export a part to OBJ, replace a part's geometry from an OBJ, and
    // copy a part from another car's model (lossless: all LODs, tangent frames and damage data).
    public partial class ArchiveEditorForm
    {
        private void AddPartImportControls(FlowLayoutPanel side)
        {
            side.Controls.Add(SideHeading("Import / export part", false));
            side.Controls.Add(SideWideButton("Copy Part From Another Car...", delegate { this.ImportPartFromCar(); }));
            side.Controls.Add(SideButtonRow(PartButton("Export OBJ...", delegate { this.ExportPartObj(); }), PartButton("Import OBJ...", delegate { this.ImportObjIntoPart(); })));
            side.Controls.Add(SideNote("The whole model: Model > Export Model to OBJ Folder."));
        }

        // The part OBJ export/import works on: the one in edit, else the part selected in the tree (OBJ
        // work needs only its name). Says why when there is none instead of doing nothing.
        private string PartForObj(string action)
        {
            string section = this.editSection ?? this.SelectedSectionName;
            if (section == null)
            {
                string reason = (this.currentEntry == null || !this.currentEntry.Name.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase))
                    ? "Open the car's model (e.g. toy_suprarz_98.carbin) and select a part in the model tree."
                    : "Select a part or one of its materials in the model tree.\n\n" + this.lblEditPart.Text;
                MessageBox.Show(this, reason, action, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return section;
        }

        private static Button PartButton(string text, EventHandler click)
        {
            Button button = new Button();
            button.Text = text;
            button.Size = new Size(220, 28);
            button.Click += click;
            return button;
        }

        // Model files of the car shown: "" (main), "_lod0", "_cockpit" -> entry.
        private Dictionary<string, ForzaZip.Entry> CarModelFiles(ForzaZip archive, string folder)
        {
            Dictionary<string, ForzaZip.Entry> files = new Dictionary<string, ForzaZip.Entry>();
            // The car is the "<base>.carbin" + "<base>_lod0.carbin" pair; calipers and rotors
            // ("<base>_rotorlf_lod0.carbin", type 1) have no main file and are skipped.
            string mainBase = null;
            foreach (ForzaZip.Entry entry in archive.Entries)
            {
                string name = entry.Name.ToLowerInvariant();
                if (name.StartsWith(folder.ToLowerInvariant()) && name.EndsWith("_lod0.carbin") && name.IndexOf('/', folder.Length) < 0)
                {
                    string candidate = entry.Name.Substring(0, entry.Name.Length - "_lod0.carbin".Length);
                    if (archive.Find(candidate + ".carbin") != null && (mainBase == null || candidate.Length < mainBase.Length))
                    {
                        mainBase = candidate;
                    }
                }
            }
            if (mainBase == null)
            {
                // FM2: one .carbin per car, no _lod0 / _cockpit files.
                ForzaZip.Entry single = null;
                foreach (ForzaZip.Entry entry in archive.Entries)
                {
                    string name = entry.Name.ToLowerInvariant();
                    if (name.StartsWith(folder.ToLowerInvariant()) && name.EndsWith(".carbin") && name.IndexOf('/', folder.Length) < 0
                        && !name.Substring(folder.Length).StartsWith("stripped_") && name.IndexOf("_caliper") < 0 && name.IndexOf("_rotor") < 0)
                    {
                        if (single != null)
                        {
                            return files;
                        }
                        single = entry;
                    }
                }
                if (single != null)
                {
                    files[""] = single;
                }
                return files;
            }
            foreach (string suffix in new string[] { "", "_lod0", "_cockpit" })
            {
                ForzaZip.Entry entry = archive.Find(mainBase + suffix + ".carbin");
                if (entry != null)
                {
                    files[suffix] = entry;
                }
            }
            return files;
        }

        private void ExportPartObj()
        {
            string part = this.PartForObj("Export Part");
            if (part == null)
            {
                return;
            }
            // A material node selected: only that material of its part.
            string material = this.SelectedMaterialName;
            // The most detailed version: the lod0 file when it has the part.
            ForzaZip.Entry source = this.currentEntry;
            Dictionary<string, ForzaZip.Entry> files = this.CarModelFiles(this.zip, this.ModelFolder);
            CarbinModel model = null;
            ForzaZip.Entry lod0;
            try
            {
                if (files.TryGetValue("_lod0", out lod0))
                {
                    CarbinModel candidate = new CarbinModel(this.zip.Read(lod0));
                    if (candidate.Find(part) != null)
                    {
                        model = candidate;
                        source = lod0;
                    }
                }
                if (model == null)
                {
                    model = new CarbinModel(this.zip.Read(this.currentEntry));
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Export Part", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "Wavefront OBJ (*.obj)|*.obj";
                dialog.FileName = Path.GetFileNameWithoutExtension(source.Name) + "_" + part + ((material != null) ? "_" + material : "") + ".obj";
                if (!ShowRemembering(dialog, this, "Obj"))
                {
                    return;
                }
                try
                {
                    string label = (material != null) ? part + "/" + material : part;
                    int triangles = ObjMesh.Export(model, part, dialog.FileName, "ForzaStudio EVO: " + label + " from " + source.Name, material);
                    this.SetStatus(string.Format("{0}: {1:N0} triangles exported from {2} to {3}", label, triangles, source.Name, dialog.FileName));
                }
                catch (Exception exception)
                {
                    MessageBox.Show(this, exception.Message, "Export Part", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                }
            }
        }

        // Every material name this car's model uses, sorted.
        private List<string> CarMaterials()
        {
            List<string> names = new List<string>();
            foreach (ForzaZip.Entry entry in this.PartEditTargets())
            {
                try
                {
                    CarbinModel model = new CarbinModel(this.zip.Read(entry));
                    foreach (CarbinModel.Section section in model.Sections)
                    {
                        foreach (CarbinModel.SubSection sub in section.SubSections)
                        {
                            if (!names.Contains(sub.Name))
                            {
                                names.Add(sub.Name);
                            }
                        }
                    }
                }
                catch (NotSupportedException)
                {
                }
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        private void ImportObjIntoPart()
        {
            string part;
            string material;
            string emptyName;
            if (!this.ImportTarget(out part, out material, out emptyName))
            {
                this.PartForObj("Import OBJ");       // says what to select
                return;
            }
            using (OpenFileDialog open = new OpenFileDialog())
            {
                open.Filter = "Wavefront OBJ (*.obj)|*.obj";
                open.Title = "Geometry for " + (emptyName ?? ((material != null) ? part + "/" + material : part ?? "a new part"));
                if (!ShowRemembering(open, this, "Obj"))
                {
                    return;
                }
                this.ImportObjFile(open.FileName);
            }
        }

        // What the Import OBJ button works on, from the selected tree node: a material node (part =
        // its parent, material = the node), a part, an empty part (emptyName: the OBJ becomes it) or a
        // LOD node (new part only, part and material null).
        private bool ImportTarget(out string part, out string material, out string emptyName)
        {
            part = null;
            material = null;
            emptyName = null;
            TreeNode node = this.tvModel.SelectedNode;
            if (node == null || this.currentEntry == null || !this.currentEntry.Name.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (IsEmptyPart(node))
            {
                emptyName = node.Text;
                return true;
            }
            part = this.editSection ?? this.SelectedSectionName;
            if (part != null)
            {
                material = this.SelectedMaterialName;
                return true;
            }
            return IsLodNode(node);
        }

        private void ImportObjFile(string path)
        {
            string section;
            string material;
            string emptyName;
            if (!this.ImportTarget(out section, out material, out emptyName))
            {
                return;
            }
            ObjMesh mesh;
            try
            {
                mesh = ObjMesh.Load(path);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, "Cannot read the OBJ: " + exception.Message, "Import OBJ", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            if (mesh.TriangleCount == 0)
            {
                MessageBox.Show(this, "The OBJ has no faces.", "Import OBJ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Material mapping and UV mode.
            List<string> carMaterials = this.CarMaterials();
            Dictionary<string, ComboBox> choices = new Dictionary<string, ComboBox>();
            // The old parts of the car, to copy the settings of a new part from.
            List<string> partNames = new List<string>();
            try
            {
                foreach (CarbinModel.Section existing in new CarbinModel(this.zip.Read(this.currentEntry)).Sections)
                {
                    partNames.Add(existing.Name);
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Import OBJ", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            if (partNames.Count == 0)
            {
                return;
            }
            partNames.Sort(StringComparer.OrdinalIgnoreCase);
            bool existingPart = section != null;
            bool materialScope = existingPart && material != null;

            Panel extra = new Panel();
            extra.Size = new Size(560, 140 + (mesh.Materials.Count * 28));
            int y = 0;
            RadioButton replaceTarget = new RadioButton();
            RadioButton addToPart = new RadioButton();
            if (existingPart)
            {
                replaceTarget.Text = materialScope ? "Replace material " + material + " of part " + section : "Replace part " + section;
                replaceTarget.AutoSize = true;
                replaceTarget.Checked = true;
                replaceTarget.Location = new System.Drawing.Point(0, y);
                extra.Controls.Add(replaceTarget);
                y += 26;
                addToPart.Text = "Add the OBJ's materials to part " + section + " (keeps everything it has)";
                addToPart.AutoSize = true;
                addToPart.Location = new System.Drawing.Point(0, y);
                extra.Controls.Add(addToPart);
                y += 26;
            }
            RadioButton addTarget = new RadioButton();
            addTarget.Text = "Add as a new part";
            addTarget.AutoSize = true;
            addTarget.Checked = !existingPart;
            addTarget.Location = new System.Drawing.Point(0, y);
            extra.Controls.Add(addTarget);
            y += 28;
            Label templateLabel = new Label();
            templateLabel.Text = "Copy settings of";
            templateLabel.AutoSize = true;
            templateLabel.Location = new System.Drawing.Point(20, y + 4);
            ComboBox template = new ComboBox();
            template.DropDownStyle = ComboBoxStyle.DropDownList;
            template.Width = 170;
            template.Location = new System.Drawing.Point(130, y);
            template.Items.AddRange(partNames.ToArray());
            template.SelectedIndex = Math.Max(0, partNames.IndexOf(section ?? ""));
            Label nameLabel = new Label();
            nameLabel.Text = "named";
            nameLabel.AutoSize = true;
            nameLabel.Location = new System.Drawing.Point(316, y + 4);
            TextBox newName = new TextBox();
            newName.Width = 180;
            newName.BorderStyle = BorderStyle.FixedSingle;
            newName.Location = new System.Drawing.Point(370, y + 1);
            newName.Text = emptyName ?? this.SuggestPartName((string) template.SelectedItem);
            newName.ReadOnly = emptyName != null;
            EventHandler enable = delegate
            {
                template.Enabled = nameLabel.Enabled = templateLabel.Enabled = newName.Enabled = addTarget.Checked;
            };
            addTarget.CheckedChanged += enable;
            enable(null, EventArgs.Empty);
            extra.Controls.AddRange(new Control[] { templateLabel, template, nameLabel, newName });
            y += 40;
            Label header = new Label();
            header.Text = string.Format("{0:N0} triangles. Car material for each OBJ material:", mesh.TriangleCount);
            header.AutoSize = true;
            header.Location = new System.Drawing.Point(0, y);
            extra.Controls.Add(header);
            y += 24;
            foreach (ObjMesh.Material objMaterial in mesh.Materials)
            {
                Label name = new Label();
                name.Text = string.Format("{0} ({1})", objMaterial.Name, objMaterial.Triangles.Count);
                name.AutoSize = false;
                name.Size = new Size(220, 22);
                name.TextAlign = ContentAlignment.MiddleLeft;
                name.Location = new System.Drawing.Point(0, y);
                // Editable: a name the car does not have yet adds a new material node to the part.
                ComboBox combo = new ComboBox();
                combo.DropDownStyle = ComboBoxStyle.DropDown;
                combo.Width = 300;
                combo.Location = new System.Drawing.Point(226, y);
                combo.Items.Add("(skip)");
                combo.Items.AddRange(carMaterials.ToArray());
                int index = carMaterials.IndexOf(objMaterial.Name);
                // Same name, else (one material selected in the tree) that material, else skipped.
                combo.SelectedIndex = (index >= 0) ? index + 1 : (materialScope ? carMaterials.IndexOf(material) + 1 : 0);
                extra.Controls.Add(name);
                extra.Controls.Add(combo);
                choices[objMaterial.Name] = combo;
                y += 28;
            }
            CheckBox fit = new CheckBox();
            fit.Text = "Fit UVs into this car's material regions (part from another car)";
            fit.AutoSize = true;
            fit.Location = new System.Drawing.Point(0, y + 4);
            extra.Controls.Add(fit);

            if (!this.ShowOptions("Import OBJ" + (existingPart ? " Into " + section : ""), extra))
            {
                return;
            }
            bool newPart = addTarget.Checked;
            string basis = newPart ? (string) template.SelectedItem : section;       // the part whose settings are used
            string target = newPart ? newName.Text.Trim() : section;
            if (target.Length == 0 || target.IndexOfAny(new char[] { ' ', '/', '\\', '<', '>' }) >= 0)
            {
                MessageBox.Show(this, "Give the new part a name without spaces.", "Import OBJ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (newPart && this.PartExists(target) && MessageBox.Show(this, string.Format("This car already has a part named {0}.\n\nReplace it with the OBJ ({1} as template)?", target, basis),
                "Import OBJ", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            Dictionary<string, string> map = new Dictionary<string, string>();
            foreach (KeyValuePair<string, ComboBox> pair in choices)
            {
                string text = pair.Value.Text.Trim();
                map[pair.Key] = (text.Length == 0 || text == "(skip)") ? null : text;
            }
            bool replaceMaterial = materialScope && !newPart && replaceTarget.Checked;
            // One material (or the added materials) changes inside the part: the part's own LODs are
            // rebuilt one by one, the other materials as they are.
            bool merge = existingPart && !newPart && (replaceMaterial || addToPart.Checked);

            List<string> report = new List<string>();
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                foreach (ForzaZip.Entry entry in this.PartEditTargets(newPart || this.chkAllModelFiles.Checked))
                {
                    CarbinModel model;
                    try
                    {
                        model = new CarbinModel(this.zip.Read(entry));
                    }
                    catch (NotSupportedException)
                    {
                        continue;
                    }
                    if (model.Find(basis) == null)
                    {
                        continue;
                    }
                    if (newPart)
                    {
                        if (model.Find(target) != null)
                        {
                            model.RemoveSection(target);
                        }
                        model.DuplicateSection(basis, target);
                    }
                    string done;
                    if (merge)
                    {
                        done = this.MergeObjIntoPart(model, section, replaceMaterial ? material : null, mesh, map, fit.Checked);
                    }
                    else
                    {
                        done = model.ReplaceGeometry(target, mesh, map, fit.Checked);
                    }
                    if (done == null)
                    {
                        continue;
                    }
                    this.zip.Replace(entry, model.ToBytes());
                    this.RefreshEntryNode(entry);
                    report.Add(entry.Name.Substring(entry.Name.LastIndexOf('/') + 1));
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Import OBJ", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            this.editSection = null;
            this.ReloadModelKeepingView();
            string what = newPart ? string.Format("{0}: added from OBJ ({1} as template)", target, basis)
                : replaceMaterial ? string.Format("{0}/{1}: material replaced from OBJ", target, material)
                : merge ? string.Format("{0}: materials added from OBJ", target)
                : target + ": geometry replaced from OBJ";
            this.SetStatus(string.Format("{0} in {1} (pending: Save to write the archive)", what, (report.Count > 0) ? string.Join(", ", report.ToArray()) : "no file (nothing to change)"));
        }

        // Puts an OBJ into an existing part of the model without touching its other materials: each LOD
        // is rebuilt from its own triangles, minus 'replace' (a material name, null = keep all) plus the
        // OBJ's triangles under the chosen car materials. When replacing, only the LODs that had that
        // material take the OBJ. Returns null when no LOD changes (this file lacks the material).
        private string MergeObjIntoPart(CarbinModel model, string partName, string replace, ObjMesh obj, Dictionary<string, string> map, bool fitUv)
        {
            CarbinModel.Section part = model.Find(partName);
            if (part == null)
            {
                return null;
            }
            List<uint> lods = new List<uint>();
            foreach (CarbinModel.SubSection sub in part.SubSections)
            {
                if (!lods.Contains(sub.Lod))
                {
                    lods.Add(sub.Lod);
                }
            }
            Dictionary<uint, ObjMesh> meshes = new Dictionary<uint, ObjMesh>();
            bool changed = false;
            foreach (uint lod in lods)
            {
                ObjMesh current = ObjMesh.FromLod(part, lod);
                bool had = false;
                if (replace != null)
                {
                    had = current.Materials.RemoveAll(delegate(ObjMesh.Material m) { return m.Name == replace; }) > 0;
                    if (!had)
                    {
                        meshes[lod] = null;          // this LOD does not draw the material: kept as it is
                        continue;
                    }
                }
                Dictionary<string, ObjMesh.Material> addedInto = new Dictionary<string, ObjMesh.Material>();
                foreach (ObjMesh.Material added in obj.Materials)
                {
                    string carMaterial;
                    if (!map.TryGetValue(added.Name, out carMaterial) || string.IsNullOrEmpty(carMaterial))
                    {
                        continue;
                    }
                    // A group of its own even when the part already draws that material: the part's
                    // triangles keep their UVs (Exact), only the OBJ's are fitted when asked.
                    ObjMesh.Material into = null;
                    if (!addedInto.TryGetValue(carMaterial, out into))
                    {
                        into = new ObjMesh.Material();
                        into.Name = carMaterial;
                        current.Materials.Add(into);
                        addedInto[carMaterial] = into;
                    }
                    into.Triangles.AddRange(added.Triangles);
                }
                meshes[lod] = current;
                changed = true;
            }
            if (!changed)
            {
                return null;
            }
            return model.RebuildLods(partName, meshes, null, fitUv);
        }

        // Whether any model file the edit touches already has a part with this name.
        private bool PartExists(string name)
        {
            foreach (ForzaZip.Entry entry in this.PartEditTargets(true))
            {
                try
                {
                    if (new CarbinModel(this.zip.Read(entry)).Find(name) != null)
                    {
                        return true;
                    }
                }
                catch (NotSupportedException)
                {
                }
            }
            return false;
        }

        // A free name for a sibling part: exhausta -> exhaustb (next variant letter), else name_2, name_3...
        private string SuggestPartName(string name)
        {
            char last = name[name.Length - 1];
            if (last >= 'a' && last < 'z')
            {
                for (char c = (char) (last + 1); c <= 'z'; c++)
                {
                    string candidate = name.Substring(0, name.Length - 1) + c;
                    if (!this.PartExists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            for (int i = 2; ; i++)
            {
                string candidate = name + "_" + i;
                if (!this.PartExists(candidate))
                {
                    return candidate;
                }
            }
        }

        // OK / Cancel window around an options panel.
        private bool ShowOptions(string title, Control content)
        {
            using (Form form = new Form())
            {
                form.Text = title;
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ShowInTaskbar = false;
                form.Font = ForzaTheme.Regular;
                form.ClientSize = new Size(content.Width + 24, content.Height + 60);
                content.Location = new System.Drawing.Point(12, 12);
                form.Controls.Add(content);
                Button ok = new Button();
                ok.Text = "OK";
                ok.Size = new Size(84, 28);
                ok.Location = new System.Drawing.Point(form.ClientSize.Width - 186, form.ClientSize.Height - 40);
                ok.DialogResult = DialogResult.OK;
                Button cancel = new Button();
                cancel.Text = "Cancel";
                cancel.Size = new Size(84, 28);
                cancel.Location = new System.Drawing.Point(form.ClientSize.Width - 96, form.ClientSize.Height - 40);
                cancel.DialogResult = DialogResult.Cancel;
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                form.BackColor = ForzaTheme.Background;
                ForzaTheme.Apply(form);
                bool accepted = form.ShowDialog(this) == DialogResult.OK;
                // Detached so the caller can still read its fields after the window is disposed.
                form.Controls.Remove(content);
                return accepted;
            }
        }

        // Picks one car out of several (a pack zip, a DLC package); null = cancelled.
        private string PickSourceCar(string title, List<string> cars, string extraRow)
        {
            List<object[]> rows = new List<object[]>();
            if (extraRow != null)
            {
                rows.Add(new object[] { extraRow });
            }
            cars.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string car in cars)
            {
                rows.Add(new object[] { car });
            }
            using (PickDialog dialog = new PickDialog(title, new string[] { "Car" }, new int[] { 420 }, rows, null))
            {
                return (dialog.ShowDialog(this) == DialogResult.OK) ? (string) dialog.SelectedKey : null;
            }
        }

        // The car to take a part from: another car of the pack zip being edited, a car zip, a pack zip with several cars
        // (media/cars/<car>/, e.g. FM4 disc 2's 0002_pri_65.zip), an Xbox 360 DLC package (the car is read from it to a
        // cached copy, the package is never written) or an uncompressed car folder. label: for the window and status.
        private bool PickPartSource(out ForzaZip source, out string sourceFolder, out string label)
        {
            source = null;
            sourceFolder = "";
            label = null;
            const string OtherFile = "(another file: car zip, pack zip or DLC package...)";
            string own = this.ModelFolder.TrimEnd('/');
            own = own.Substring(own.LastIndexOf('/') + 1);
            List<string> packCars = PackZipCars(this.zip);
            packCars.RemoveAll(delegate(string c) { return c.Equals(own, StringComparison.OrdinalIgnoreCase); });
            if (packCars.Count > 0)
            {
                string car = this.PickSourceCar("Copy Part From: another car of this pack, or a file", packCars, OtherFile);
                if (car == null)
                {
                    return false;
                }
                if (car != OtherFile)
                {
                    source = this.zip;
                    sourceFolder = "media/cars/" + car + "/";
                    label = car;
                    return true;
                }
            }
            string file;
            using (OpenFileDialog open = new OpenFileDialog())
            {
                open.Filter = "Forza car (*.zip; *_lod0.carbin)|*.zip;*_lod0.carbin|Xbox 360 DLC package (no extension)|*.*";
                open.Title = "Car to take a part from (car zip, pack zip, DLC package, or the _lod0.carbin in an uncompressed car folder)";
                if (!ShowRemembering(open, this, "SourceCar"))
                {
                    return false;
                }
                file = open.FileName;
            }
            try
            {
                if (file.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase))
                {
                    source = ForzaZip.OpenFolder(Path.GetDirectoryName(file));
                    label = Path.GetFileName(Path.GetDirectoryName(file));
                    return true;
                }
                if (StfsReader.IsPackage(file))
                {
                    Cursor.Current = Cursors.WaitCursor;
                    DlcSource package = DlcSource.OpenPackage(file, true);
                    List<string> cars = (package == null) ? new List<string>() : package.CarFolders();
                    if (cars.Count == 0)
                    {
                        MessageBox.Show(this, "No car in this package" + ((package != null && package.Problem != null) ? ": " + package.Problem : "."), "Import Part", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return false;
                    }
                    string title = string.IsNullOrEmpty(package.Title) ? Path.GetFileName(file) : package.Title;
                    string car = (cars.Count == 1) ? cars[0] : this.PickSourceCar("Copy Part From: " + title, cars, null);
                    if (car == null)
                    {
                        return false;
                    }
                    Cursor.Current = Cursors.WaitCursor;
                    this.SetStatus("Reading " + car + " from the package " + title + "...");
                    string zipPath = PackageCarZip(package, file, car);
                    if (zipPath == null)
                    {
                        MessageBox.Show(this, car + " was not found in the package " + title + ".", "Import Part", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return false;
                    }
                    source = new ForzaZip(zipPath);
                    label = car + " (" + title + ")";
                    return true;
                }
                source = new ForzaZip(file);
                label = Path.GetFileName(file);
                if (this.CarModelFiles(source, "").ContainsKey(""))
                {
                    return true;
                }
                List<string> zipCars = PackZipCars(source);
                if (zipCars.Count == 0)
                {
                    return true;   // says below that the main model is missing
                }
                string picked = (zipCars.Count == 1) ? zipCars[0] : this.PickSourceCar("Copy Part From: " + Path.GetFileName(file), zipCars, null);
                if (picked == null)
                {
                    return false;
                }
                sourceFolder = "media/cars/" + picked + "/";
                label = picked + " (" + Path.GetFileName(file) + ")";
                return true;
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Import Part", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return false;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        private void ImportPartFromCar()
        {
            string selected = this.editSection;
            ForzaZip source;
            string sourceFolder;
            string sourceLabel;
            if (!this.PickPartSource(out source, out sourceFolder, out sourceLabel))
            {
                return;
            }
            Dictionary<string, ForzaZip.Entry> sourceFiles = this.CarModelFiles(source, sourceFolder);
            Dictionary<string, ForzaZip.Entry> targetFiles = this.CarModelFiles(this.zip, this.ModelFolder);
            if (!sourceFiles.ContainsKey("") || !targetFiles.ContainsKey(""))
            {
                MessageBox.Show(this, "Both cars need their main model (.carbin next to the _lod0.carbin).", "Import Part", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            CarbinModel sourceMain;
            CarbinModel targetMain;
            try
            {
                sourceMain = new CarbinModel(source.Read(sourceFiles[""]));
                targetMain = new CarbinModel(this.zip.Read(targetFiles[""]));
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Import Part", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }

            List<object[]> rows = new List<object[]>();
            foreach (CarbinModel.Section section in sourceMain.Sections)
            {
                List<string> materials = new List<string>();
                foreach (CarbinModel.SubSection sub in section.SubSections)
                {
                    if (!materials.Contains(sub.Name))
                    {
                        materials.Add(sub.Name);
                    }
                }
                rows.Add(new object[] { section.Name, string.Join(", ", materials.ToArray()), section.IsDeformable ? "deformable" : "rigid" });
            }

            // Options under the part list.
            Panel extra = new Panel();
            extra.Size = new Size(520, 104);
            Label replaceLabel = new Label();
            replaceLabel.Text = "Replace in this car:";
            replaceLabel.AutoSize = true;
            replaceLabel.Location = new System.Drawing.Point(0, 6);
            ComboBox replace = new ComboBox();
            replace.DropDownStyle = ComboBoxStyle.DropDownList;
            replace.Location = new System.Drawing.Point(130, 2);
            replace.Width = 200;
            replace.Items.Add("(nothing: add as a new part)");
            List<string> targetNames = new List<string>();
            foreach (CarbinModel.Section section in targetMain.Sections)
            {
                targetNames.Add(section.Name);
            }
            targetNames.Sort(StringComparer.OrdinalIgnoreCase);
            replace.Items.AddRange(targetNames.ToArray());
            replace.SelectedIndex = (selected != null && targetNames.Contains(selected)) ? targetNames.IndexOf(selected) + 1 : 0;
            Label nameLabel = new Label();
            nameLabel.Text = "Name in this car:";
            nameLabel.AutoSize = true;
            nameLabel.Location = new System.Drawing.Point(0, 36);
            TextBox name = new TextBox();
            name.Location = new System.Drawing.Point(130, 33);
            name.Width = 200;
            name.BorderStyle = BorderStyle.FixedSingle;
            name.Text = (replace.SelectedIndex > 0) ? (string) replace.SelectedItem : "";
            replace.SelectedIndexChanged += delegate
            {
                if (replace.SelectedIndex > 0)
                {
                    name.Text = (string) replace.SelectedItem;
                }
            };
            CheckBox align = new CheckBox();
            align.Text = "Place it at the replaced part's center";
            align.Checked = true;
            align.AutoSize = true;
            align.Location = new System.Drawing.Point(0, 62);
            CheckBox regions = new CheckBox();
            regions.Text = "Use this car's texture regions for its materials";
            regions.Checked = true;
            regions.AutoSize = true;
            regions.Location = new System.Drawing.Point(0, 84);
            extra.Controls.AddRange(new Control[] { replaceLabel, replace, nameLabel, name, align, regions });

            string part;
            string newName;
            string replaced;
            bool alignChecked;
            bool regionsChecked;
            using (PickDialog pick = new PickDialog("Import Part From " + sourceLabel, new string[] { "Part", "Materials", "Damage" }, new int[] { 160, 260, 90 }, rows, extra))
            {
                if (pick.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                part = (string) pick.SelectedKey;
                newName = name.Text.Trim();
                replaced = (replace.SelectedIndex > 0) ? (string) replace.SelectedItem : null;
                alignChecked = align.Checked;
                regionsChecked = regions.Checked;
            }
            if (newName.Length == 0)
            {
                newName = part;
            }
            if (newName != replaced && targetNames.Contains(newName))
            {
                MessageBox.Show(this, "This car already has a part named " + newName + ": choose it in \"Replace\" or use another name.", "Import Part", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // One placement for every file: source part center -> replaced part center (main files).
            Matrix move = Matrix.Identity;
            CarbinModel.Section from = sourceMain.Find(part);
            CarbinModel.Section to = (replaced != null) ? targetMain.Find(replaced) : null;
            if (alignChecked && to != null)
            {
                move = Matrix.CreateTranslation(new Vector3(to.Bounds[0] - from.Bounds[0], to.Bounds[1] - from.Bounds[1], to.Bounds[2] - from.Bounds[2]));
            }

            List<string> report = new List<string>();
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                foreach (KeyValuePair<string, ForzaZip.Entry> pair in targetFiles)
                {
                    ForzaZip.Entry sourceEntry;
                    if (!sourceFiles.TryGetValue(pair.Key, out sourceEntry))
                    {
                        continue;
                    }
                    CarbinModel sourceModel = new CarbinModel(source.Read(sourceEntry));
                    if (sourceModel.Find(part) == null)
                    {
                        continue;
                    }
                    CarbinModel model = new CarbinModel(this.zip.Read(pair.Value));
                    if (replaced != null && model.Find(replaced) != null)
                    {
                        model.RemoveSection(replaced);
                    }
                    model.ImportSection(sourceModel, part, newName, regionsChecked);
                    if (move != Matrix.Identity)
                    {
                        model.TransformSection(newName, move);
                    }
                    this.zip.Replace(pair.Value, model.ToBytes());
                    this.RefreshEntryNode(pair.Value);
                    report.Add(pair.Value.Name.Substring(pair.Value.Name.LastIndexOf('/') + 1));
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Import Part", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            this.editSection = null;
            this.ReloadModelKeepingView();
            this.SetStatus(string.Format("{0} from {1} -> {2} in {3} (pending: Save to write the archive)", part, sourceLabel, newName, string.Join(", ", report.ToArray())));
        }
    }
}
