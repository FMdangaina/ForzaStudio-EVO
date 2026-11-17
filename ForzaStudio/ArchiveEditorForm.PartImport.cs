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
            side.Controls.Add(SideWideButton("Fill Missing LOD0 Parts From LOD1...", delegate { this.FillLod0FromLod1(); }));
            side.Controls.Add(SideButtonRow(PartButton("Export OBJ...", delegate { this.ExportPartObj(); }), PartButton("Import OBJ...", delegate { this.ImportObjIntoPart(); })));
            // The whole model, by format (was the Model menu, then a button of the viewer's bar: the user wanted it
            // in PART, 2026-11-17).
            side.Controls.Add(SideHeading("Import / export model", false));
            side.Controls.Add(SideCaption("OBJ folder"));
            side.Controls.Add(SideButtonRow(PartButton("Export...", delegate { this.ExportModelFolder(); }), PartButton("Import...", delegate { this.ImportModelFolder(); })));
            side.Controls.Add(SideCaption("FBX"));
            side.Controls.Add(SideButtonRow(PartButton("Export...", delegate { this.ExportModelFbx(false); }), PartButton("Import...", delegate { this.ImportModelFbx(false); })));
            side.Controls.Add(SideCaption("glTF"));
            side.Controls.Add(SideButtonRow(PartButton("Export...", delegate { this.ExportModelFbx(true); }), PartButton("Import...", delegate { this.ImportModelFbx(true); })));
            side.Controls.Add(SideWideButton("Export Checked Parts to OBJ...", delegate { this.ExportObj(); }));
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

        // The name a copied part gets: its own when free here, else the next free variant as SuggestPartName gives it
        // (exhausta -> exhaustb, else name_2, name_3...), against the names of all this car's model files.
        private static string FreePartName(string name, ICollection<string> taken)
        {
            if (!taken.Contains(name))
            {
                return name;
            }
            char last = name[name.Length - 1];
            if (last >= 'a' && last < 'z')
            {
                for (char c = (char) (last + 1); c <= 'z'; c++)
                {
                    string candidate = name.Substring(0, name.Length - 1) + c;
                    if (!taken.Contains(candidate))
                    {
                        return candidate;
                    }
                }
            }
            for (int i = 2; ; i++)
            {
                string candidate = name + "_" + i;
                if (!taken.Contains(candidate))
                {
                    return candidate;
                }
            }
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
                dialog.AcceptLabel = delegate(int n) { return "Use this car"; };
                return (dialog.ShowDialog(this) == DialogResult.OK) ? (string) dialog.SelectedKey : null;
            }
        }

        // The car to take a part from: first the editor's list (PickKnownCar: this pack, the game folders, the installed
        // packages); its "(another file...)" row opens a file dialog for a car zip, a pack zip with several cars
        // (media/cars/<car>/, e.g. FM4 disc 2's 0002_pri_65.zip), an Xbox 360 DLC package (the car is read from it to a
        // cached copy, the package is never written) or an uncompressed car folder. label: for the window and status.
        private bool PickPartSource(out ForzaZip source, out string sourceFolder, out string label)
        {
            source = null;
            sourceFolder = "";
            label = null;
            string own = this.ModelFolder.TrimEnd('/');
            own = own.Substring(own.LastIndexOf('/') + 1);
            List<string> packCars = PackZipCars(this.zip);
            packCars.RemoveAll(delegate(string c) { return c.Equals(own, StringComparison.OrdinalIgnoreCase); });
            // The editor's own list of the cars it knows (games, installed packages, this pack); a file only on request.
            CarSource known = this.PickKnownCar("Copy Part From Another Car", packCars);
            if (known == null)
            {
                return false;
            }
            try
            {
                switch (known.Kind)
                {
                    case CarSourceKind.ThisPack:
                        source = this.zip;
                        sourceFolder = "media/cars/" + known.Media + "/";
                        label = known.Media;
                        return true;
                    case CarSourceKind.GameZip:
                        source = new ForzaZip(known.Path);
                        label = known.Media + " (" + known.Game + ")";
                        return true;
                    case CarSourceKind.Package:
                        Cursor.Current = Cursors.WaitCursor;
                        this.SetStatus("Reading " + known.Media + " from the package " + known.PackageTitle + "...");
                        string carZip = PackageCarZip(null, known.Path, known.Media);
                        if (carZip == null)
                        {
                            MessageBox.Show(this, known.Media + " was not found in the package " + known.PackageTitle + ".", "Import Part", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return false;
                        }
                        source = new ForzaZip(carZip);
                        label = known.Media + " (" + known.Game + ", " + known.PackageTitle + ")";
                        return true;
                }
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
            ForzaZip source;
            string sourceFolder;
            string sourceLabel;
            if (!this.PickPartSource(out source, out sourceFolder, out sourceLabel))
            {
                return;
            }
            Dictionary<string, ForzaZip.Entry> sourceFiles = this.CarModelFiles(source, sourceFolder);
            Dictionary<string, ForzaZip.Entry> targetFiles = this.CarModelFiles(this.zip, this.ModelFolder);
            // the model file open in the viewer: the copy goes ONLY into it (user 2026-11-17: "mi sta bene la regola
            // import solo su lod aperto"); main when no _lod0 / _cockpit file is open
            string fileKey = "";
            foreach (KeyValuePair<string, ForzaZip.Entry> pair in targetFiles)
            {
                if (pair.Value == this.currentEntry)
                {
                    fileKey = pair.Key;
                }
            }
            if (!sourceFiles.ContainsKey("") || !targetFiles.ContainsKey(""))
            {
                MessageBox.Show(this, "Both cars need their main model (.carbin next to the _lod0.carbin).", "Import Part", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            // the source file is the user's choice (user 2026-11-17: "mi fai scegliere il file main, lod0 o cockpit, per
            // assurdo anche freni e dischi"; an FM2 car has only its main model)
            string sourceKey = this.PickPartSourceFile(source, sourceFolder, sourceFiles, fileKey, sourceLabel);
            if (sourceKey == null)
            {
                return;
            }
            Dictionary<string, ForzaZip.Entry> chosenFiles = new Dictionary<string, ForzaZip.Entry>();
            chosenFiles[""] = sourceFiles[""];          // the game check and the conversion read the main model
            chosenFiles[sourceKey] = sourceFiles[sourceKey];
            sourceFiles = chosenFiles;
            CarbinModel sourceMain;
            CarbinModel targetMain;
            // The source car's model files in this car's game form (ST-03, PartSource): an FM4 car as it is, an FM3
            // or FH1 / FH2 car converted in memory as the ports convert it; an FM2 car rebuilt on this car's main model as
            // the FM2 port rebuilds it on its donor.
            Dictionary<string, byte[]> sourceData = new Dictionary<string, byte[]>();
            List<string> sourceNotes = new List<string>();
            Dictionary<string, List<string>> sourceUnknown = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                foreach (KeyValuePair<string, ForzaZip.Entry> pair in sourceFiles)
                {
                    sourceData[pair.Key] = source.Read(pair.Value);
                }
                byte[] targetMainData = this.zip.Read(targetFiles[""]);
                CarbinFamily sourceGame = CarbinFormat.Identify(sourceData[""]).Family;
                CarbinFamily targetGame = CarbinFormat.Identify(targetMainData).Family;
                if (sourceGame != targetGame && targetGame != CarbinFamily.Motorsport4)
                {
                    throw new NotSupportedException(string.Format("Copying a part from a {0} car into a {1} car is not supported yet (today: FM4, FM3, FH1 and FH2 into FM4).", PartSource.GameName(sourceGame), PartSource.GameName(targetGame)));
                }
                if (sourceGame != targetGame)
                {
                    // this car's model files: an FH part takes its damage words from them (PartSource)
                    Dictionary<string, byte[]> targetData = new Dictionary<string, byte[]>();
                    foreach (KeyValuePair<string, ForzaZip.Entry> pair in targetFiles)
                    {
                        targetData[pair.Key] = (pair.Key.Length == 0) ? targetMainData : this.zip.Read(pair.Value);
                    }
                    sourceData = PartSource.ForMotorsport4(sourceData, sourceNotes, sourceUnknown, targetData);
                    sourceLabel += " (" + PartSource.GameName(sourceGame) + ")";
                }
                // the list and the preview show the chosen source file; the copy goes into the open file only
                sourceMain = new CarbinModel(sourceData[sourceKey]);
                targetMain = new CarbinModel((fileKey.Length == 0) ? targetMainData : this.zip.Read(targetFiles[fileKey]));
                if (sourceMain.DrawLists.Count / 2 == 1 && targetMain.DrawLists.Count / 2 > 1)
                {
                    throw new NotSupportedException(string.Format("The source's {0} model has LOD 0 only, and this car's main model needs LODs 1-5. Open this car's lod0 or cockpit model to copy into it, or choose the source's main model.", PartImport.FileLabel(sourceKey)));
                }
                sourceLabel += " - " + PartImport.FileLabel(sourceKey) + " model, into this car's " + PartImport.FileLabel(fileKey);
                // FH1 / FH2 cars into an FH car: refused, for now (user 2026-11-16). Into FM4 they arrive converted above.
                if (sourceMain.IsHorizon)
                {
                    throw new NotSupportedException("Forza Horizon .carbin: it can be read for study, but copying parts from FH models is not supported.");
                }
            }
            catch (Exception exception)
            {
                Cursor.Current = Cursors.Default;
                MessageBox.Show(this, exception.Message, "Import Part", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            // every part name of this car's model files: a copied part is added under a free name (never replaces by itself)
            HashSet<string> taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, ForzaZip.Entry> pair in targetFiles)
            {
                try
                {
                    foreach (CarbinModel.Section section in ((pair.Key == fileKey) ? targetMain : new CarbinModel(this.zip.Read(pair.Value))).Sections)
                    {
                        taken.Add(section.Name);
                    }
                }
                catch (NotSupportedException)
                {
                }
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
            extra.Size = new Size(620, 126);
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
            // user 2026-11-16: by default nothing is replaced; replacing is the user's choice in this box
            replace.SelectedIndex = 0;
            Label nameLabel = new Label();
            nameLabel.Text = "Name in this car:";
            nameLabel.AutoSize = true;
            nameLabel.Location = new System.Drawing.Point(0, 36);
            TextBox name = new TextBox();
            name.Location = new System.Drawing.Point(130, 33);
            name.Width = 200;
            name.BorderStyle = BorderStyle.FixedSingle;
            name.Text = "";
            string currentPart = null;     // the highlighted row: its free name is proposed while nothing is replaced
            replace.SelectedIndexChanged += delegate
            {
                if (replace.SelectedIndex > 0)
                {
                    name.Text = (string) replace.SelectedItem;
                }
                else if (currentPart != null)
                {
                    name.Text = FreePartName(currentPart, taken);
                }
            };
            CheckBox align = new CheckBox();
            align.Text = "Place it at the replaced part's center";
            align.Checked = true;
            align.AutoSize = true;
            align.Location = new System.Drawing.Point(0, 62);
            // no "Use this car's texture regions" box any more: a copied part's UV1 / UV2 are never touched (user rule
            // 2026-11-17, PartImport)
            CheckBox fit = new CheckBox();
            fit.Text = "Also scale it to the replaced part's size (same proportions)";
            fit.Checked = false;
            fit.AutoSize = true;
            fit.Location = new System.Drawing.Point(0, 84);
            Label multiNote = new Label();
            multiNote.Text = "Several parts (tick them): each is added as a new part, under its name or the next free one.";
            multiNote.AutoSize = true;
            multiNote.Location = new System.Drawing.Point(0, 106);
            extra.Size = new Size(620, 126);
            extra.Controls.AddRange(new Control[] { replaceLabel, replace, nameLabel, name, align, fit, multiNote });

            // part (source) -> its name in this car and the part it replaces (null = added)
            List<string[]> plan = new List<string[]>();
            bool alignChecked;
            bool fitChecked;
            using (PickDialog pick = new PickDialog("Import Part From " + sourceLabel, new string[] { "Part", "Materials", "Damage" }, new int[] { 160, 260, 90 }, rows, extra))
            {
                pick.MultiSelect = true;
                pick.AcceptLabel = delegate(int n) { return (n == 1) ? "Copy part" : "Copy " + n + " parts"; };
                // the source car in a small viewport, the highlighted / ticked parts selected (orange); no textures
                // (user 2026-11-16: see the part being picked, "usa tecnica viewport", "non visualizzare texture")
                PartPreviewControl preview = new PartPreviewControl();
                try
                {
                    string mainName = sourceFiles[sourceKey].Name;
                    ForzaZip.Entry settingsEntry = source.Find(mainName.Substring(0, mainName.LastIndexOf('/') + 1) + "shadersettings.xml");
                    ShaderSettings settings = null;
                    try
                    {
                        settings = (settingsEntry != null) ? ShaderSettings.Parse(source.Read(settingsEntry)) : null;
                    }
                    catch (Exception)
                    {
                        settings = null;   // the default look of each material
                    }
                    preview.LoadModel(mainName, sourceData[sourceKey], delegate(string technique) { return ShaderValues(settings, technique); });
                }
                catch (Exception)
                {
                    // no picture: the list works as before
                }
                pick.DoubleClickAccepts = false;     // only the Copy button copies (a stray double-click did)
                pick.AddPreview(preview, 900);
                pick.SelectionChanged += delegate
                {
                    preview.ShowParts(pick.MarkedKeys.ConvertAll(delegate(object key) { return (string) key; }));
                };
                pick.SelectionChanged += delegate
                {
                    bool one = pick.SelectedCount <= 1;
                    replace.Enabled = one;
                    name.Enabled = one;
                    List<object> marked = pick.MarkedKeys;
                    string part = (marked.Count > 0) ? (string) marked[marked.Count - 1] : null;
                    if (one && part != null && part != currentPart)
                    {
                        currentPart = part;
                        if (replace.SelectedIndex == 0)
                        {
                            // exhausta here already: exhaustb (if free); not here: its own name
                            name.Text = FreePartName(part, taken);
                        }
                    }
                };
                if (pick.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                if (pick.SelectedKeys.Count == 1)
                {
                    string part = (string) pick.SelectedKey;
                    string newName = name.Text.Trim();
                    bool replacing = replace.SelectedIndex > 0;
                    if (newName.Length == 0)
                    {
                        newName = replacing ? (string) replace.SelectedItem : FreePartName(part, taken);
                    }
                    if (!replacing && taken.Contains(newName))
                    {
                        MessageBox.Show(this, "This car already has a part named " + newName + ". Give the new part a free name, or choose it in \"Replace in this car\".", "Import Part", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    plan.Add(new string[] { part, newName, replacing ? (string) replace.SelectedItem : null });
                }
                else
                {
                    // several: each added as a new part, its own name or the next free one (none replaced)
                    foreach (object key in pick.SelectedKeys)
                    {
                        string part = (string) key;
                        string newName = FreePartName(part, taken);
                        taken.Add(newName);
                        plan.Add(new string[] { part, newName, null });
                    }
                }
                alignChecked = align.Checked;
                fitChecked = fit.Checked;
            }
            this.RunPartImport(sourceData, sourceNotes, sourceUnknown, targetFiles, sourceLabel, plan, alignChecked, fitChecked, fileKey, sourceKey);
        }

        // The source car's model file to copy from: main, lod0, cockpit and its brake files (calipers, rotors), the one
        // of the open file's kind proposed. Keys: "" / "_lod0" / "_cockpit" as CarModelFiles, a brake file "_<rest>"
        // ("_caliperlf_lod0"). Adds the brake entries to 'files'. One file only (FM2): taken without asking. Null = cancel.
        private string PickPartSourceFile(ForzaZip source, string folder, Dictionary<string, ForzaZip.Entry> files, string openKey, string sourceLabel)
        {
            string mainName = files[""].Name;
            if (CarbinFormat.Identify(source.Read(files[""])).Family == CarbinFamily.Motorsport2)
            {
                return "";      // an FM2 car: one .carbin (LODs 1-5), its parts are rebuilt from it (PartSource)
            }
            string carBase = mainName.Substring(0, mainName.Length - ".carbin".Length);
            foreach (ForzaZip.Entry entry in source.Entries)
            {
                string name = entry.Name;
                string lower = name.ToLowerInvariant();
                if (!lower.StartsWith(folder.ToLowerInvariant()) || !lower.EndsWith(".carbin") || name.IndexOf('/', folder.Length) >= 0
                    || lower.Substring(folder.Length).StartsWith("stripped_") || files.ContainsValue(entry))
                {
                    continue;
                }
                string rest = name.StartsWith(carBase, StringComparison.OrdinalIgnoreCase) ? name.Substring(carBase.Length) : "_" + name.Substring(folder.Length);
                string key = rest.Substring(0, rest.Length - ".carbin".Length);
                if (!files.ContainsKey(key))
                {
                    files[key] = entry;
                }
            }
            if (files.Count == 1)
            {
                return "";
            }
            List<object[]> rows = new List<object[]>();
            Dictionary<string, string> keyOf = new Dictionary<string, string>();
            foreach (string key in new string[] { "", "_lod0", "_cockpit" })
            {
                if (files.ContainsKey(key))
                {
                    keyOf[PartImport.FileLabel(key)] = key;
                    rows.Add(new object[] { PartImport.FileLabel(key), files[key].Name.Substring(folder.Length), (key.Length == 0) ? "LODs 1-5" : "LOD 0 (close up)" });
                }
            }
            List<string> others = new List<string>(files.Keys);
            others.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string key in others)
            {
                if (key != "" && key != "_lod0" && key != "_cockpit")
                {
                    keyOf[PartImport.FileLabel(key)] = key;
                    rows.Add(new object[] { PartImport.FileLabel(key), files[key].Name.Substring(folder.Length), "brake" });
                }
            }
            using (PickDialog pick = new PickDialog("Copy From Which File Of " + sourceLabel + " (into this car's " + PartImport.FileLabel(openKey) + " model)", new string[] { "File", "Name", "Kind" }, new int[] { 140, 300, 120 }, rows, null))
            {
                pick.Preselect = files.ContainsKey(openKey) ? PartImport.FileLabel(openKey) : PartImport.FileLabel("");
                if (pick.ShowDialog(this) != DialogResult.OK)
                {
                    return null;
                }
                return keyOf[(string) pick.SelectedKey];
            }
        }

        // Builds the whole import first (PartImport: all files, checks), asks on which LODs a moved part is moved,
        // shows the log as a preview, and only then puts the files in the archive (pending until Save).
        private void RunPartImport(Dictionary<string, byte[]> sourceData, List<string> sourceNotes, Dictionary<string, List<string>> sourceUnknown, Dictionary<string, ForzaZip.Entry> targetFiles, string sourceLabel, List<string[]> plan, bool alignChecked, bool fitChecked, string onlyFile, string sourceFile)
        {
            PartImport import = new PartImport();
            import.SourceFile = sourceFile;     // the source car's chosen file (may differ in kind from onlyFile)
            import.OnlyFile = onlyFile;     // the open file's kind only (null = every file, as before)
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                foreach (KeyValuePair<string, byte[]> pair in sourceData)
                {
                    import.Source[pair.Key] = pair.Value;     // already in this car's game form (PartSource)
                }
                foreach (KeyValuePair<string, ForzaZip.Entry> pair in targetFiles)
                {
                    import.Target[pair.Key] = this.zip.Read(pair.Value);
                }
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            bool anyMove = false;
            foreach (string[] step in plan)
            {
                PartImport.Step s = new PartImport.Step();
                s.Part = step[0];
                s.NewName = step[1];
                s.Replaced = step[2];
                s.Align = alignChecked;
                s.Fit = fitChecked;
                import.Steps.Add(s);
                anyMove |= s.Replaced != null && (alignChecked || fitChecked);
            }

            // User rule (ST-03): moving / scaling is never applied to every LOD silently - ask which files.
            // One file chosen (the open one): the move applies to that file, the one the user is working on.
            if (anyMove && onlyFile != null)
            {
                foreach (PartImport.Step s in import.Steps)
                {
                    s.MoveIn = new List<string> { onlyFile };
                }
            }
            else if (anyMove)
            {
                List<string> moveIn = this.AskMoveLods(import, sourceLabel);
                if (moveIn == null)
                {
                    return;
                }
                foreach (PartImport.Step s in import.Steps)
                {
                    s.MoveIn = moveIn;
                }
            }

            try
            {
                Cursor.Current = Cursors.WaitCursor;
                import.Run();
            }
            catch (Exception exception)
            {
                Cursor.Current = Cursors.Default;
                MessageBox.Show(this, exception.Message + "\n\nNothing was changed.", "Import Part", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }

            // A part from another game: a material of it that no FM4 car uses is said (it may render black).
            foreach (string warning in PartSource.UnknownMaterialWarnings(import.Source, import.Steps, sourceUnknown))
            {
                import.Warnings.Add(warning);
            }
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            text.AppendFormat("Import from {0}\r\n\r\n", sourceLabel);
            foreach (string line in sourceNotes)
            {
                text.AppendLine(line);
            }
            foreach (string line in import.Log)
            {
                text.AppendLine(line);
            }
            if (import.Warnings.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("WARNINGS:");
                foreach (string line in import.Warnings)
                {
                    text.AppendLine("  " + line);
                }
            }
            text.AppendLine();
            text.AppendLine("Files that change: " + string.Join(", ", new List<string>(import.Result.Keys).ConvertAll(PartImport.FileLabel).ToArray()));
            // The preview stays (user 2026-11-16: "anteprima andava bene"; the whole flow will be reworked, UI_WORKFLOW C).
            if (!this.ConfirmReport("Import Part - preview", text.ToString(), "Apply"))
            {
                this.SetStatus("Import Part cancelled: nothing changed");
                return;
            }

            List<string> report = new List<string>();
            foreach (KeyValuePair<string, byte[]> pair in import.Result)
            {
                ForzaZip.Entry entry = targetFiles[pair.Key];
                this.zip.Replace(entry, pair.Value);
                this.RefreshEntryNode(entry);
                report.Add(entry.Name.Substring(entry.Name.LastIndexOf('/') + 1));
            }
            try
            {
                File.AppendAllText(Path.Combine(System.Windows.Forms.Application.StartupPath, "PartImport.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " " + (this.zip.FilePath ?? "") + "\r\n" + text + "\r\n");
            }
            catch (Exception)
            {
                // the log is a convenience: the import stays
            }
            this.editSection = null;
            this.ReloadModelKeepingView();
            List<string> done = new List<string>();
            foreach (string[] step in plan)
            {
                done.Add((step[0] == step[1]) ? step[0] : step[0] + " -> " + step[1]);
            }
            this.SetStatus(string.Format("{0} from {1} in {2}{3}   (Ctrl+Z: undo; details in PartImport.log)", string.Join(", ", done.ToArray()), sourceLabel, string.Join(", ", report.ToArray()),
                (import.Warnings.Count > 0) ? "   " + import.Warnings.Count + " WARNING(S): " + import.Warnings[0] : ""));
        }

        // Parts the main model draws (LOD 1-5) that their close-up model lacks: up close they vanish (many cars ported
        // from FM2). The chosen ones get a copy of their LOD 1 as LOD 0 (Lod0Fill): cockpit parts (hood, interior,
        // mirrors...) in _cockpit, the others in _lod0, as in FM4 (user 2026-11-15: hoods go to the cockpit model).
        private void FillLod0FromLod1()
        {
            const string title = "Parts Missing in LOD0";
            Dictionary<string, ForzaZip.Entry> files = this.CarModelFiles(this.zip, this.ModelFolder);
            if (!files.ContainsKey("") || (!files.ContainsKey("_lod0") && !files.ContainsKey("_cockpit")))
            {
                MessageBox.Show(this, "Open an FM4 car with its main model and its _lod0 / _cockpit models (e.g. toy_suprarz_98.carbin + toy_suprarz_98_lod0.carbin).", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Dictionary<string, byte[]> data = new Dictionary<string, byte[]>();
            foreach (KeyValuePair<string, ForzaZip.Entry> pair in files)
            {
                data[pair.Key] = this.zip.Read(pair.Value);
            }
            List<Lod0Fill.Candidate> missing;
            try
            {
                missing = Lod0Fill.Missing(data);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            if (missing.Count == 0)
            {
                MessageBox.Show(this, "Every part of the main model is also in its close-up model (lod0, or cockpit for cockpit parts): nothing to copy.", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Panel panel = new Panel();
            Label intro = new Label();
            intro.Text = "These parts are in the main model (LOD 1-5) but not in their close-up model, so they vanish up close.\n"
                + "Ticked parts get a copy of their LOD 1 as LOD 0 (same position, materials and UVs): cockpit parts in the cockpit model, the others in lod0.";
            intro.AutoSize = true;
            intro.Location = new System.Drawing.Point(0, 0);
            CheckedListBox list = new CheckedListBox();
            list.CheckOnClick = true;
            list.Location = new System.Drawing.Point(0, 40);
            list.Size = new Size(640, Math.Min(22 * missing.Count + 8, 360));
            list.IntegralHeight = false;
            List<Lod0Fill.Candidate> rows = new List<Lod0Fill.Candidate>();
            foreach (Lod0Fill.Candidate c in missing)
            {
                List<string> lods = c.MainLods.ConvertAll(delegate(uint l) { return l.ToString(); });
                string note = !c.HasLod1 ? "  - no LOD 1: cannot be copied" : "";
                list.Items.Add(string.Format("{0}  -> {1}   (LOD {2}; {3}){4}", c.Part, PartImport.FileLabel(c.Target), string.Join(",", lods.ToArray()), string.Join(", ", c.Materials.ToArray()), note), c.HasLod1);
                rows.Add(c);
            }
            list.ItemCheck += delegate(object sender, ItemCheckEventArgs e)
            {
                if (!rows[e.Index].HasLod1)
                {
                    e.NewValue = CheckState.Unchecked;
                }
            };
            Button all = new Button();
            all.Text = "Tick all";
            all.Size = new Size(90, 26);
            all.Location = new System.Drawing.Point(0, list.Bottom + 6);
            all.Click += delegate { for (int i = 0; i < list.Items.Count; i++) { list.SetItemChecked(i, rows[i].HasLod1); } };
            Button none = new Button();
            none.Text = "Untick all";
            none.Size = new Size(90, 26);
            none.Location = new System.Drawing.Point(96, list.Bottom + 6);
            none.Click += delegate { for (int i = 0; i < list.Items.Count; i++) { list.SetItemChecked(i, false); } };
            panel.Controls.AddRange(new Control[] { intro, list, all, none });
            panel.Size = new Size(640, all.Bottom + 4);
            if (!this.ShowOptions(title, panel))
            {
                return;
            }
            Lod0Fill fill = new Lod0Fill();
            fill.Files = data;
            for (int i = 0; i < rows.Count; i++)
            {
                if (list.GetItemChecked(i) && rows[i].HasLod1)
                {
                    fill.Parts.Add(rows[i].Part);
                }
            }
            if (fill.Parts.Count == 0)
            {
                this.SetStatus(title + ": no part ticked, nothing changed");
                return;
            }
            try
            {
                fill.Run();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message + "\n\nNothing was changed.", title, MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }

            System.Text.StringBuilder text = new System.Text.StringBuilder();
            foreach (string line in fill.Log)
            {
                text.AppendLine(line);
            }
            if (fill.Warnings.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("WARNINGS");
                foreach (string line in fill.Warnings)
                {
                    text.AppendLine("  " + line);
                }
            }
            List<string> changed = new List<string>();
            foreach (string key in fill.Result.Keys)
            {
                changed.Add(files[key].Name.Substring(files[key].Name.LastIndexOf('/') + 1));
            }
            string fileName = string.Join(", ", changed.ToArray());
            text.AppendLine();
            text.AppendLine("Files that change: " + fileName + " (the main model is not touched)");
            if (!this.ConfirmReport(title + " - preview", text.ToString(), "Apply"))
            {
                this.SetStatus(title + " cancelled: nothing changed");
                return;
            }
            foreach (KeyValuePair<string, byte[]> pair in fill.Result)
            {
                this.zip.Replace(files[pair.Key], pair.Value);
                this.RefreshEntryNode(files[pair.Key]);
            }
            try
            {
                File.AppendAllText(Path.Combine(System.Windows.Forms.Application.StartupPath, "PartImport.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " " + (this.zip.FilePath ?? "") + " LOD1 -> LOD0\r\n" + text + "\r\n");
            }
            catch (Exception)
            {
                // the log is a convenience: the change stays
            }
            this.editSection = null;
            this.ReloadModelKeepingView();
            this.SetStatus(string.Format("{0} copied from LOD 1 into {1}{2}   (Ctrl+Z: undo; details in PartImport.log)", string.Join(", ", fill.Parts.ToArray()), fileName,
                (fill.Warnings.Count > 0) ? "   " + fill.Warnings.Count + " WARNING(S): " + fill.Warnings[0] : ""));
        }

        // Which model files get the move / scale. Every file is listed with the LODs the copied parts draw there.
        private List<string> AskMoveLods(PartImport import, string sourceLabel)
        {
            Dictionary<string, List<uint>> lods = new Dictionary<string, List<uint>>();
            foreach (PartImport.Step step in import.Steps)
            {
                foreach (KeyValuePair<string, List<uint>> pair in PartImport.PartLods(import.Source, step.Part))
                {
                    if (!import.Target.ContainsKey(pair.Key))
                    {
                        continue;
                    }
                    List<uint> list;
                    if (!lods.TryGetValue(pair.Key, out list))
                    {
                        list = new List<uint>();
                        lods[pair.Key] = list;
                    }
                    foreach (uint lod in pair.Value)
                    {
                        if (!list.Contains(lod))
                        {
                            list.Add(lod);
                        }
                    }
                    list.Sort();
                }
            }
            Panel panel = new Panel();
            Label intro = new Label();
            intro.Text = "The copied part is moved to the replaced part's position" + (import.Steps[0].Fit ? " and scaled" : "") + ".\nApply the move on which LODs? (unticked = kept where it is in " + sourceLabel + ")";
            intro.AutoSize = true;
            intro.Location = new System.Drawing.Point(0, 0);
            panel.Controls.Add(intro);
            Dictionary<string, CheckBox> boxes = new Dictionary<string, CheckBox>();
            int y = 44;
            foreach (string key in new string[] { "", "_lod0", "_cockpit" })
            {
                List<uint> list;
                if (!lods.TryGetValue(key, out list))
                {
                    continue;
                }
                CheckBox box = new CheckBox();
                box.Text = string.Format("{0} model: LOD {1}", PartImport.FileLabel(key), PartImport.LodText(list));
                box.Checked = true;
                box.AutoSize = true;
                box.Location = new System.Drawing.Point(0, y);
                panel.Controls.Add(box);
                boxes[key] = box;
                y += 24;
            }
            panel.Size = new Size(520, y + 4);
            if (!this.ShowOptions("Import Part - move on which LODs?", panel))
            {
                return null;
            }
            List<string> chosen = new List<string>();
            foreach (KeyValuePair<string, CheckBox> pair in boxes)
            {
                if (pair.Value.Checked)
                {
                    chosen.Add(pair.Key);
                }
            }
            return chosen;
        }

        private bool ConfirmReport(string title, string text, string accept)
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
                Panel bottom = new Panel();
                // Full width BEFORE the anchored buttons go in: a panel born 200 wide moved them off screen
                // when it docked (the user saw no Apply button, 2026-11-15).
                bottom.Size = new Size(form.ClientSize.Width, 44);
                bottom.Dock = DockStyle.Bottom;
                Button ok = new Button();
                ok.Text = accept;
                ok.Size = new Size(84, 28);
                ok.Location = new System.Drawing.Point(form.ClientSize.Width - 186, 8);
                ok.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                ok.DialogResult = DialogResult.OK;
                Button cancel = new Button();
                cancel.Text = "Cancel";
                cancel.Size = new Size(84, 28);
                cancel.Location = new System.Drawing.Point(form.ClientSize.Width - 96, 8);
                cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                cancel.DialogResult = DialogResult.Cancel;
                bottom.Controls.Add(ok);
                bottom.Controls.Add(cancel);
                form.Controls.Add(box);
                form.Controls.Add(bottom);
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                form.BackColor = ForzaTheme.Background;
                ForzaTheme.Apply(form);
                return form.ShowDialog(this) == DialogResult.OK;
            }
        }
    }
}
