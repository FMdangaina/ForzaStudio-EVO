namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Windows.Forms;

    // Game LOD preview: the viewport shows what the game draws at one LOD, across the car's model files.
    // LOD 0 = the _lod0 file's LOD 0 + the _cockpit file (the close-up car); LOD n = LOD n of the main file.
    // The file open in the tree shows only that LOD; the other files are drawn beside it, read-only
    // (RenderScene.ExtraRoots): they are not in the tree, so no tool selects or edits them.
    public partial class ArchiveEditorForm
    {
        private ToolStripComboBox cmbGameLod;
        private readonly List<ForzaMesh> gameLodMeshes = new List<ForzaMesh>();
        private readonly Dictionary<ForzaMesh, ShaderTextures.Kind> gameLodKinds = new Dictionary<ForzaMesh, ShaderTextures.Kind>();
        private int gameLodFaces;
        private string gameLodNote = "";
        private bool settingGameLod;

        private ToolStripComboBox BuildGameLodCombo()
        {
            this.cmbGameLod = new ToolStripComboBox();
            this.cmbGameLod.DropDownStyle = ComboBoxStyle.DropDownList;
            this.cmbGameLod.Items.Add("View: this file");
            this.cmbGameLod.Items.Add("Game LOD0 (lod0 + cockpit)");
            for (int lod = 1; lod <= 5; lod++)
            {
                this.cmbGameLod.Items.Add("Game LOD" + lod + " (main)");
            }
            this.cmbGameLod.SelectedIndex = 0;
            this.cmbGameLod.ToolTipText = "Shows one LOD as the game draws it, with the car's other model files beside this one (read-only).\n"
                + "LOD0 = the _lod0 file + the _cockpit file; LOD1-5 = the main file. Only this file's parts can be picked and edited.";
            this.cmbGameLod.SelectedIndexChanged += delegate
            {
                if (!this.settingGameLod)
                {
                    this.ShowGameLod(this.cmbGameLod.SelectedIndex - 1);
                }
            };
            return this.cmbGameLod;
        }

        // A new model was loaded: back to "this file".
        private void ResetGameLod()
        {
            this.ClearGameLod();
            if (this.cmbGameLod != null && this.cmbGameLod.SelectedIndex != 0)
            {
                this.settingGameLod = true;
                this.cmbGameLod.SelectedIndex = 0;
                this.settingGameLod = false;
            }
        }

        private void ClearGameLod()
        {
            if (this.scene != null)
            {
                this.scene.ExtraRoots.Clear();
            }
            foreach (ForzaMesh mesh in this.gameLodMeshes)
            {
                this.meshTextures.Remove(mesh);
                this.autoTextureSampler.Remove(mesh);
                mesh.Dispose();
            }
            this.gameLodMeshes.Clear();
            this.gameLodKinds.Clear();
            this.gameLodFaces = 0;
            this.gameLodNote = "";
        }

        // lod -1: only this file, as before (LOD 0 or the first LOD checked).
        private void ShowGameLod(int lod)
        {
            this.ClearGameLod();
            if (this.currentEntry == null || this.zip == null || this.tvModel.Nodes.Count == 0
                || !this.currentEntry.Name.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            TreeNode root = this.tvModel.Nodes[0];
            if (lod < 0)
            {
                if (!this.CheckOnlyLod(root, "LOD0", true) && root.Nodes.Count > 0)
                {
                    this.CheckOnlyLod(root, root.Nodes[0].Text, true);
                }
            }
            else
            {
                Dictionary<string, ForzaZip.Entry> files = this.CarModelFiles(this.zip, this.ModelFolder);
                // What the game draws at this LOD: suffix of the file -> its kind for textures.xml.
                List<KeyValuePair<string, ShaderTextures.Kind>> wanted = new List<KeyValuePair<string, ShaderTextures.Kind>>();
                if (lod == 0 && files.ContainsKey("_lod0"))
                {
                    wanted.Add(new KeyValuePair<string, ShaderTextures.Kind>("_lod0", ShaderTextures.Kind.Lod0));
                    if (files.ContainsKey("_cockpit"))
                    {
                        wanted.Add(new KeyValuePair<string, ShaderTextures.Kind>("_cockpit", ShaderTextures.Kind.Cockpit));
                    }
                }
                else if (files.ContainsKey(""))
                {
                    // LOD 1-5, or a car with one model file (FM2): the main file.
                    wanted.Add(new KeyValuePair<string, ShaderTextures.Kind>("", (lod == 0) ? ShaderTextures.Kind.Lod0 : ShaderTextures.Kind.Main));
                }
                bool thisFileShown = false;
                List<string> shown = new List<string>();
                foreach (KeyValuePair<string, ShaderTextures.Kind> file in wanted)
                {
                    ForzaZip.Entry entry = files[file.Key];
                    string fileName = entry.Name.Substring(entry.Name.LastIndexOf('/') + 1);
                    if (string.Equals(entry.Name, this.currentEntry.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        thisFileShown = this.CheckOnlyLod(root, "LOD" + lod, false);
                        shown.Add(fileName + " (tree)");
                        continue;
                    }
                    int faces = this.AddGameLodFile(entry, lod, file.Value);
                    shown.Add(faces < 0 ? fileName + " (unreadable)" : string.Format("{0} ({1:N0} faces)", fileName, faces));
                }
                if (!thisFileShown)
                {
                    // This file has no part in that LOD: its own parts are hidden, the others are drawn alone.
                    this.CheckOnlyLod(root, null, false);
                }
                this.gameLodNote = (shown.Count == 0) ? "  |  no model file of the car has LOD" + lod : "  |  game LOD" + lod + ": " + string.Join(" + ", shown.ToArray());
                Dictionary<ForzaMesh, int> lods = this.MeshLods();
                foreach (ForzaMesh mesh in this.gameLodMeshes)
                {
                    this.SetAutoTexture(mesh, lods);
                }
                this.ApplyTextures();
            }
            this.UpdateModelInfo();
            if (this.scene != null)
            {
                this.scene.Update(true);
            }
        }

        // Checks the LOD node named lodName (and everything below it) and unchecks the others; false when
        // there is none. expand: also open that node.
        private bool CheckOnlyLod(TreeNode root, string lodName, bool expand)
        {
            bool found = false;
            foreach (TreeNode node in root.Nodes)
            {
                bool on = node.Text == lodName;
                found |= on;
                node.Checked = on;   // AfterCheck spreads it to the parts and meshes
                if (on && expand)
                {
                    node.Expand();
                }
            }
            return found;
        }

        // Reads one LOD of another model file into a detached tree drawn by the scene. Faces drawn, -1 if unreadable.
        private int AddGameLodFile(ForzaZip.Entry entry, int lod, ShaderTextures.Kind kind)
        {
            byte[] data = this.zip.Read(entry);
            ShaderSettings settings = null;
            ForzaZip.Entry settingsEntry = this.zip.Find(this.ModelFolder + "shadersettings.xml");
            if (settingsEntry != null)
            {
                try
                {
                    settings = ShaderSettings.Parse(this.zip.Read(settingsEntry));
                }
                catch (Exception)
                {
                    settings = null;
                }
            }
            TreeNode root = new TreeNode(Path.GetFileName(entry.Name));
            int faces = 0;
            try
            {
                using (ForzaCarBin bin = new ForzaCarBin(entry.Name, data))
                {
                    for (int s = 0; s < bin.Sections.Length; s++)
                    {
                        ForzaCarSection section = bin.Sections[s];
                        for (int k = 0; k < section.SubSections.Length; k++)
                        {
                            ForzaCarSubSection sub = section.SubSections[k];
                            if (sub.Lod != lod)
                            {
                                continue;
                            }
                            ForzaMesh mesh = new ForzaMesh(section.Name + "_" + sub.Name, sub.Name, sub.Indices, sub.Vertices);
                            mesh.SectionIndex = s;
                            mesh.SubIndex = k;
                            mesh.Technique = ShaderSettings.TechniqueName(sub.Name, entry.Name);
                            mesh.Settings = ShaderValues(settings, mesh.Technique ?? mesh.MaterialName);
                            TreeNode node = new TreeNode(sub.Name);
                            node.Tag = mesh;
                            node.Checked = true;
                            root.Nodes.Add(node);
                            this.gameLodMeshes.Add(mesh);
                            this.gameLodKinds[mesh] = kind;
                            faces += mesh.FaceCount;
                        }
                    }
                }
            }
            catch (Exception)
            {
                return -1;
            }
            this.gameLodFaces += faces;
            if (this.scene != null)
            {
                this.scene.ExtraRoots.Add(root);
            }
            return faces;
        }
    }
}
