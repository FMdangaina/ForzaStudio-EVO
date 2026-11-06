namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;

    // What writes a scene file for the model export (FbxBuilder, GltfBuilder). Parent 0 = scene root.
    // 'texture' (may be null) is the picture the material shows, for looking at the UVs in the 3D editor;
    // the import ignores it. A .glb holds the pictures, the other files write them to a "textures" folder next to them.
    public interface IModelSceneBuilder
    {
        long AddNull(string name, long parent);
        long AddMesh(string name, long parent, string materialName, ObjMesh.Material mesh, ExportTexture texture);
        void Save(string path);
    }

    // A texture for the export: a file name (unique per picture, .png) and the PNG bytes.
    public class ExportTexture
    {
        public string Name;
        public byte[] Png;

        public string Stem
        {
            get { return Path.GetFileNameWithoutExtension(this.Name); }
        }

        public static string TexturesFolder(string scenePath)
        {
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(scenePath)), "textures");
        }
    }

    // A node of a scene file read back (FBX or glTF), the same for both formats. Materials is the
    // reader's own list (cleaning a name there changes what Read gives). Read is null for a node without a mesh.
    public class ModelSceneNode
    {
        public string Name;
        public ModelSceneNode Parent;
        public List<ModelSceneNode> Children = new List<ModelSceneNode>();
        public List<string> Materials = new List<string>();
        public Func<string, List<ObjMesh.Material>> Read;

        public bool HasMesh
        {
            get { return this.Read != null; }
        }
    }

    // The car's model as one FBX or glTF scene with the model's own (carbin) hierarchy:
    //   <car>                       (Null)
    //     main | lod0 | cockpit     (Null: the model file)
    //       LOD1 ... LOD5 | LOD0    (Null)
    //         <part>                (Null)
    //           <part>__<material>  (Mesh: the part's triangles with that material)
    // Edit it in 3ds Max, Blender...; the import takes every mesh below a kind and LOD node, applies the
    // transforms of its ancestors, and hands the parts to the same import as the OBJ folders (CarModelFolder).
    // Only those minimal nodes are needed: a LOD the file has no node for is left alone, a part missing
    // from a LOD the file does have is left alone too (dropped there only when the import is asked to), a new
    // part is a new group (or a mesh) under a LOD node. The export can write chosen model files / LODs only.
    public static class CarModelFbx
    {
        private static readonly Regex LodNode = new Regex(@"^lod(\d+)$", RegexOptions.IgnoreCase);
        private static readonly Regex CopySuffix = new Regex(@"(\.\d{3}|_\d{3}|\s\(\d+\))$");
        private const string Separator = "__";

        // 'Name.001' (Blender) and 'Name_001' (Max) copies are the same name.
        public static string StripCopy(string name)
        {
            return CopySuffix.Replace(name, "");
        }

        // A name the car uses is kept as is (parts can end in _001); any other loses its copy suffix.
        private static string Clean(string name, Dictionary<string, bool> known)
        {
            return known.ContainsKey(name) ? name : StripCopy(name);
        }

        private static string KindOf(string name)
        {
            switch (StripCopy(name).ToLowerInvariant())
            {
                case "main": return CarModelFolder.Main;
                case "lod0": return CarModelFolder.Lod0;
                case "cockpit": return CarModelFolder.Cockpit;
            }
            return null;
        }

        #region Export

        // A model file at one LOD: what the export dialog offers.
        public class Level
        {
            public string Kind;
            public uint Lod;
            public int Parts;

            public string Key
            {
                get { return LevelKey(this.Kind, this.Lod); }
            }

            public string Describe()
            {
                return string.Format("{0} file, LOD {1} ({2} parts)", CarModelFolder.KindName(this.Kind), this.Lod, this.Parts);
            }
        }

        public static string LevelKey(string kind, uint lod)
        {
            return kind + "|" + lod.ToString(CultureInfo.InvariantCulture);
        }

        // Every model file / LOD the car has, main, lod0 and cockpit in that order.
        public static List<Level> Levels(Dictionary<string, CarbinModel> models)
        {
            List<Level> levels = new List<Level>();
            foreach (string kind in new string[] { CarModelFolder.Main, CarModelFolder.Lod0, CarModelFolder.Cockpit })
            {
                CarbinModel model;
                if (!models.TryGetValue(kind, out model))
                {
                    continue;
                }
                SortedDictionary<uint, int> counts = new SortedDictionary<uint, int>();
                foreach (CarbinModel.Section section in model.Sections)
                {
                    List<uint> seen = new List<uint>();
                    foreach (CarbinModel.SubSection sub in section.SubSections)
                    {
                        if (!seen.Contains(sub.Lod))
                        {
                            seen.Add(sub.Lod);
                            int count;
                            counts.TryGetValue(sub.Lod, out count);
                            counts[sub.Lod] = count + 1;
                        }
                    }
                }
                foreach (KeyValuePair<uint, int> pair in counts)
                {
                    Level level = new Level();
                    level.Kind = kind;
                    level.Lod = pair.Key;
                    level.Parts = pair.Value;
                    levels.Add(level);
                }
            }
            return levels;
        }

        // True for .gltf / .glb paths (glTF 2.0), false for FBX.
        public static bool IsGltf(string path)
        {
            string extension = Path.GetExtension(path);
            return string.Equals(extension, ".glb", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".gltf", StringComparison.OrdinalIgnoreCase);
        }

        public static string FormatName(string path)
        {
            return IsGltf(path) ? "glTF" : "FBX";
        }

        // kind -> .carbin name / parsed model. 'wanted' lists the LevelKeys to write (null = all).
        // The file format follows the extension (.fbx, .glb, .gltf); the scene is the same.
        // Returns the number of mesh nodes written.
        public static int Export(Dictionary<string, string> names, Dictionary<string, CarbinModel> models, string path, string source, List<string> wanted)
        {
            return Export(names, models, path, source, wanted, null);
        }

        // 'textures' (null = none): material, model kind and LOD -> the texture the material shows, or null.
        public static int Export(Dictionary<string, string> names, Dictionary<string, CarbinModel> models, string path, string source, List<string> wanted,
            Func<string, string, uint, ExportTexture> textures)
        {
            string car = CarModelFolder.FolderName(names.ContainsKey(CarModelFolder.Main) ? names[CarModelFolder.Main] : First(names));
            IModelSceneBuilder builder = IsGltf(path)
                ? (IModelSceneBuilder) new GltfBuilder("ForzaStudio EVO: " + source)
                : new FbxBuilder("ForzaStudio EVO: " + source);
            long root = builder.AddNull(car, 0);
            int count = 0;
            foreach (string kind in new string[] { CarModelFolder.Main, CarModelFolder.Lod0, CarModelFolder.Cockpit })
            {
                CarbinModel model;
                if (!models.TryGetValue(kind, out model))
                {
                    continue;
                }
                SortedDictionary<uint, long> lodNodes = new SortedDictionary<uint, long>();
                foreach (CarbinModel.Section section in model.Sections)
                {
                    foreach (CarbinModel.SubSection sub in section.SubSections)
                    {
                        if (Wanted(wanted, kind, sub.Lod))
                        {
                            lodNodes[sub.Lod] = 0;
                        }
                    }
                }
                if (lodNodes.Count == 0)
                {
                    continue;
                }
                long kindNode = builder.AddNull(CarModelFolder.KindName(kind), root);
                List<uint> lods = new List<uint>(lodNodes.Keys);
                foreach (uint lod in lods)
                {
                    lodNodes[lod] = builder.AddNull("LOD" + lod.ToString(CultureInfo.InvariantCulture), kindNode);
                }
                foreach (CarbinModel.Section section in model.Sections)
                {
                    List<uint> partLods = new List<uint>();
                    foreach (CarbinModel.SubSection sub in section.SubSections)
                    {
                        if (!partLods.Contains(sub.Lod))
                        {
                            partLods.Add(sub.Lod);
                        }
                    }
                    foreach (uint lod in partLods)
                    {
                        if (!lodNodes.ContainsKey(lod))
                        {
                            continue;
                        }
                        long part = builder.AddNull(section.Name, lodNodes[lod]);
                        foreach (ObjMesh.Material material in ObjMesh.FromLod(section, lod).Materials)
                        {
                            ExportTexture texture = (textures != null) ? textures(material.Name, kind, lod) : null;
                            builder.AddMesh(section.Name + Separator + material.Name, part, material.Name, material, texture);
                            count++;
                        }
                    }
                }
            }
            builder.Save(path);
            return count;
        }

        private static bool Wanted(List<string> wanted, string kind, uint lod)
        {
            return wanted == null || wanted.Contains(LevelKey(kind, lod));
        }

        private static string First(Dictionary<string, string> names)
        {
            foreach (string name in names.Values)
            {
                return name;
            }
            return "car";
        }

        #endregion

        #region Import

        private class Found
        {
            public string Kind;
            public uint Lod;
            public string Part;
            public ObjMesh Mesh = new ObjMesh();
        }

        // Reads the FBX or glTF file and lays it out as OBJ model folders in a new temporary folder: the car's own
        // export, with the OBJs of every part the file changed, added or dropped replaced. The result is
        // imported like any OBJ folder (CarModelFolder.Prepare / Apply). 'folders' maps kind -> folder;
        // 'root' is the temporary folder to delete afterwards. Notes are the findings of the node check.
        // The nodes of an FBX or glTF file (by extension), in file order.
        public static List<ModelSceneNode> LoadScene(string path)
        {
            List<ModelSceneNode> result = new List<ModelSceneNode>();
            if (IsGltf(path))
            {
                GltfScene gltf = GltfScene.Load(path);
                Dictionary<GltfScene.Node, ModelSceneNode> map = new Dictionary<GltfScene.Node, ModelSceneNode>();
                foreach (GltfScene.Node node in gltf.Nodes)
                {
                    ModelSceneNode item = new ModelSceneNode();
                    item.Name = node.Name;
                    item.Materials = node.Materials;
                    item.Read = gltf.HasMesh(node) ? GltfReader(gltf, node) : null;
                    map[node] = item;
                    result.Add(item);
                }
                Link(map, delegate(GltfScene.Node n) { return n.Parent; });
                return result;
            }
            FbxScene fbx = FbxScene.Load(File.ReadAllBytes(path));
            Dictionary<FbxScene.Model, ModelSceneNode> models = new Dictionary<FbxScene.Model, ModelSceneNode>();
            foreach (FbxScene.Model model in fbx.ById.Values)
            {
                ModelSceneNode item = new ModelSceneNode();
                item.Name = model.Name;
                item.Materials = model.Materials;
                item.Read = fbx.HasMesh(model) ? FbxReader(fbx, model) : null;
                models[model] = item;
                result.Add(item);
            }
            Link(models, delegate(FbxScene.Model m) { return m.Parent; });
            return result;
        }

        // One method per delegate so each one keeps its own node (no shared loop variable).
        private static Func<string, List<ObjMesh.Material>> GltfReader(GltfScene scene, GltfScene.Node node)
        {
            return delegate(string fallback) { return scene.ReadMesh(node, fallback); };
        }

        private static Func<string, List<ObjMesh.Material>> FbxReader(FbxScene scene, FbxScene.Model model)
        {
            return delegate(string fallback) { return scene.ReadMesh(model, fallback); };
        }

        private static void Link<T>(Dictionary<T, ModelSceneNode> map, Func<T, T> parentOf) where T : class
        {
            foreach (KeyValuePair<T, ModelSceneNode> pair in map)
            {
                T parent = parentOf(pair.Key);
                ModelSceneNode owner;
                if (parent != null && map.TryGetValue(parent, out owner))
                {
                    pair.Value.Parent = owner;
                    owner.Children.Add(pair.Value);
                }
            }
        }

        public static void PrepareFolders(string scenePath, Dictionary<string, string> names, Dictionary<string, CarbinModel> models,
            bool dropMissing, out Dictionary<string, string> folders, out string root, List<string> notes, List<string> errors)
        {
            string format = FormatName(scenePath);
            root = Path.Combine(Path.GetTempPath(), "ForzaStudio_" + format.ToLowerInvariant() + "_" + Guid.NewGuid().ToString("N"));
            folders = new Dictionary<string, string>();
            List<ModelSceneNode> scene = LoadScene(scenePath);

            Dictionary<string, bool> carMaterials = new Dictionary<string, bool>(StringComparer.Ordinal);
            Dictionary<string, bool> knownNames = new Dictionary<string, bool>(StringComparer.Ordinal);   // parts and materials of the car
            foreach (CarbinModel model in models.Values)
            {
                foreach (CarbinModel.Section section in model.Sections)
                {
                    knownNames[section.Name] = true;
                    foreach (CarbinModel.SubSection sub in section.SubSections)
                    {
                        carMaterials[sub.Name] = true;
                        knownNames[sub.Name] = true;
                    }
                }
            }

            // Every mesh under a kind node and a LOD node, grouped by kind, LOD and part.
            Dictionary<string, Found> found = new Dictionary<string, Found>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, bool> lodsSeen = new Dictionary<string, bool>();
            List<string> outside = new List<string>();
            int meshes = 0;
            foreach (ModelSceneNode node in scene)
            {
                if (node.Children.Count > 0 && !node.HasMesh)
                {
                    // Pure group; only its meshes count. Remember the LOD levels the file has.
                    Match lodMatch = LodNode.Match(StripCopy(node.Name));
                    if (lodMatch.Success)
                    {
                        string kindOfLod = KindAbove(node);
                        if (kindOfLod != null)
                        {
                            lodsSeen[kindOfLod + "|" + uint.Parse(lodMatch.Groups[1].Value, CultureInfo.InvariantCulture)] = true;
                        }
                    }
                }
                if (!node.HasMesh)
                {
                    continue;
                }
                meshes++;
                string kind = null;
                uint lod = 0;
                ModelSceneNode partNode = null;
                for (ModelSceneNode walk = node; walk != null; walk = walk.Parent)
                {
                    Match match = LodNode.Match(StripCopy(walk.Name));
                    if (match.Success && walk.Parent != null && KindOf(walk.Parent.Name) != null && partNode != null)
                    {
                        lod = uint.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                        kind = KindOf(walk.Parent.Name);
                        break;
                    }
                    partNode = walk;
                }
                if (kind == null)
                {
                    outside.Add(node.Name);
                    continue;
                }
                lodsSeen[kind + "|" + lod] = true;

                // The part is the node right below the LOD node; a mesh straight below it is a part of its own.
                string partName = partNode.Name;
                if (partNode == node)
                {
                    int cut = partName.IndexOf(Separator, StringComparison.Ordinal);
                    if (cut > 0)
                    {
                        partName = partName.Substring(0, cut);
                    }
                }
                partName = Clean(partName, knownNames);
                string fallback = null;
                int at = node.Name.IndexOf(Separator, StringComparison.Ordinal);
                if (at >= 0)
                {
                    fallback = Clean(node.Name.Substring(at + Separator.Length), knownNames);
                }
                // A mesh's materials: the ones the file names, unless they are not the car's and the node name has one that is.
                for (int i = 0; i < node.Materials.Count; i++)
                {
                    node.Materials[i] = Clean(node.Materials[i], knownNames);
                    if (!carMaterials.ContainsKey(node.Materials[i]) && fallback != null && carMaterials.ContainsKey(fallback) && node.Materials.Count == 1)
                    {
                        node.Materials[i] = fallback;
                    }
                }
                string key = kind + "|" + lod + "|" + partName;
                Found target;
                if (!found.TryGetValue(key, out target))
                {
                    target = new Found();
                    target.Kind = kind;
                    target.Lod = lod;
                    target.Part = partName;
                    found[key] = target;
                }
                foreach (ObjMesh.Material material in node.Read(fallback ?? partName))
                {
                    ObjMesh.Material into = null;
                    foreach (ObjMesh.Material have in target.Mesh.Materials)
                    {
                        if (have.Name == material.Name)
                        {
                            into = have;
                        }
                    }
                    if (into == null)
                    {
                        target.Mesh.Materials.Add(material);
                    }
                    else
                    {
                        into.Triangles.AddRange(material.Triangles);
                    }
                }
            }
            if (outside.Count > 0)
            {
                notes.Add(string.Format("{0} mesh nodes are not below a main / lod0 / cockpit node and a LODn node, ignored: {1}", outside.Count, Short(outside)));
            }
            if (found.Count == 0)
            {
                errors.Add("No mesh found under a model file node (main, lod0, cockpit) and a LOD node (LOD0, LOD1...). Export the model to " + format + " first and edit that scene.");
                return;
            }

            // The car's own export as the base.
            foreach (KeyValuePair<string, CarbinModel> pair in models)
            {
                string folder = Path.Combine(root, CarModelFolder.FolderName(names[pair.Key]));
                CarModelFolder.Export(pair.Value, names[pair.Key], folder, format + " import");
                folders[pair.Key] = folder;
            }

            int same = 0;
            int changed = 0;
            int added = 0;
            int dropped = 0;
            int absent = 0;
            List<string> droppedNames = new List<string>();
            foreach (KeyValuePair<string, CarbinModel> pair in models)
            {
                string kind = pair.Key;
                string folder = folders[kind];
                // Parts the model has at the LODs the file has.
                Dictionary<string, bool> known = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                foreach (CarbinModel.Section section in pair.Value.Sections)
                {
                    known[section.Name] = true;
                    List<uint> lods = new List<uint>();
                    foreach (CarbinModel.SubSection sub in section.SubSections)
                    {
                        if (!lods.Contains(sub.Lod))
                        {
                            lods.Add(sub.Lod);
                        }
                    }
                    foreach (uint lod in lods)
                    {
                        if (!lodsSeen.ContainsKey(kind + "|" + lod))
                        {
                            continue;
                        }
                        string obj = ObjPath(folder, lod, section.Name);
                        Found mine;
                        if (!found.TryGetValue(kind + "|" + lod + "|" + section.Name, out mine))
                        {
                            // A scene with only some parts is the normal way to work; dropping is asked for.
                            if (dropMissing)
                            {
                                File.Delete(obj);
                                dropped++;
                                droppedNames.Add(CarModelFolder.KindName(kind) + " LOD" + lod + " " + section.Name);
                            }
                            else
                            {
                                absent++;
                            }
                            continue;
                        }
                        if (ObjMesh.Same(ObjMesh.FromLod(section, lod), mine.Mesh, 2e-5f, 2e-5f, 1e-3f))
                        {
                            same++;
                        }
                        else
                        {
                            mine.Mesh.Save(obj, section.Name, "ForzaStudio EVO: " + section.Name + " from " + format, CarModelFolder.MaterialsName);
                            changed++;
                        }
                    }
                }
                // Parts only the file has.
                foreach (Found item in found.Values)
                {
                    if (item.Kind != kind || known.ContainsKey(item.Part))
                    {
                        continue;
                    }
                    string directory = Path.Combine(folder, "lod" + item.Lod.ToString(CultureInfo.InvariantCulture));
                    Directory.CreateDirectory(directory);
                    item.Mesh.Save(Path.Combine(directory, item.Part + ".obj"), item.Part, "ForzaStudio EVO: " + item.Part + " from " + format, CarModelFolder.MaterialsName);
                    added++;
                }
            }
            notes.Add(string.Format(format + ": {0} mesh nodes, {1} part LODs unchanged, {2} changed, {3} new, {4} dropped, {5} not in the file (left as they are).", meshes, same, changed, added, dropped, absent));
            if (droppedNames.Count > 0)
            {
                notes.Add("Dropped: " + Short(droppedNames));
            }
        }

        private static string KindAbove(ModelSceneNode node)
        {
            return (node.Parent != null) ? KindOf(node.Parent.Name) : null;
        }

        private static string ObjPath(string folder, uint lod, string part)
        {
            return Path.Combine(Path.Combine(folder, "lod" + lod.ToString(CultureInfo.InvariantCulture)), part + ".obj");
        }

        private static string Short(List<string> items)
        {
            int shown = Math.Min(8, items.Count);
            return string.Join(", ", items.GetRange(0, shown).ToArray()) + ((items.Count > shown) ? string.Format(" ... {0} more", items.Count - shown) : "");
        }

        #endregion
    }
}
