namespace ForzaStudio
{
    using Microsoft.Xna.Framework;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.RegularExpressions;

    // A car's model as work folders laid out like the model tree: one folder per .carbin, one
    // subfolder per LOD, one OBJ per part (a group per material, usemtl = car material):
    //   toy_supraveilside03_98\lod1\glassRTL.obj        main .carbin, LOD 1..5
    //   toy_supraveilside03_98_lod0\lod0\glassRTL.obj   _lod0.carbin
    //   toy_supraveilside03_98_cockpit\lod0\seatL.obj   _cockpit.carbin
    // Each .carbin folder also holds model.txt (what was exported, a hash per OBJ). Importing rebuilds
    // only what changed: an edited OBJ rebuilds that part at that LOD, a new OBJ name adds a part
    // (template: the nearest part of the same file) and a deleted OBJ drops that LOD of the part
    // (the part goes when none is left).
    public static class CarModelFolder
    {
        public const string ManifestName = "model.txt";
        public const string MaterialsName = "materials.mtl";

        // Model file kinds as CarModelFiles names them.
        public const string Main = "";
        public const string Lod0 = "_lod0";
        public const string Cockpit = "_cockpit";

        private static readonly Regex LodFolder = new Regex(@"^lod(\d+)$", RegexOptions.IgnoreCase);

        public static string KindName(string kind)
        {
            return (kind == Main) ? "main" : kind.Substring(1);
        }

        // "toy_supra_98_lod0.carbin" -> "toy_supra_98_lod0": the folder of that model file.
        public static string FolderName(string carbinName)
        {
            return Path.GetFileNameWithoutExtension(carbinName);
        }

        private static string Hash(string path)
        {
            using (MD5 md5 = MD5.Create())
            {
                return BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            }
        }

        // OBJs of a .carbin folder: lodN\part.obj -> (LOD, part, path). Other OBJs go to 'ignored'.
        private static List<string[]> Scan(string folder, List<string> ignored)
        {
            List<string[]> found = new List<string[]>();
            foreach (string directory in Directory.GetDirectories(folder))
            {
                Match match = LodFolder.Match(Path.GetFileName(directory));
                if (!match.Success)
                {
                    continue;
                }
                foreach (string path in Directory.GetFiles(directory, "*.obj"))
                {
                    found.Add(new string[] { match.Groups[1].Value, Path.GetFileNameWithoutExtension(path), path });
                }
            }
            foreach (string path in Directory.GetFiles(folder, "*.obj"))
            {
                ignored.Add(Path.GetFileName(path) + " (outside the lodN folders)");
            }
            return found;
        }

        private static string Relative(uint lod, string part)
        {
            return "lod" + lod.ToString(CultureInfo.InvariantCulture) + "/" + part + ".obj";
        }

        #region Export

        // Writes one model file into its folder (created if needed). OBJs of an earlier export in the
        // lodN folders are deleted first, so the folder always matches its manifest. Returns the
        // number of OBJs written.
        public static int Export(CarbinModel model, string carbinName, string folder, string source)
        {
            Directory.CreateDirectory(folder);
            foreach (string directory in Directory.GetDirectories(folder))
            {
                if (LodFolder.IsMatch(Path.GetFileName(directory)))
                {
                    foreach (string old in Directory.GetFiles(directory, "*.obj"))
                    {
                        File.Delete(old);
                    }
                }
            }
            StringBuilder manifest = new StringBuilder();
            manifest.AppendLine("# ForzaStudio EVO model folder of " + carbinName + ", exported from " + source);
            manifest.AppendLine("# lodN\\<part>.obj = the part at that LOD, one group per material (usemtl = car material).");
            manifest.AppendLine("# Edit, add or delete OBJs, then Model > Import Model from OBJ Folder: only changed OBJs are rebuilt.");
            manifest.AppendLine("# A new OBJ name adds a part; deleting a part's OBJ drops it at that LOD. Do not edit the lines below.");
            manifest.AppendLine("file " + carbinName);
            Dictionary<uint, List<string>> materials = new Dictionary<uint, List<string>>();
            int count = 0;
            foreach (CarbinModel.Section section in model.Sections)
            {
                List<uint> lods = new List<uint>();
                foreach (CarbinModel.SubSection sub in section.SubSections)
                {
                    if (!lods.Contains(sub.Lod))
                    {
                        lods.Add(sub.Lod);
                    }
                    if (!materials.ContainsKey(sub.Lod))
                    {
                        materials[sub.Lod] = new List<string>();
                    }
                    if (!materials[sub.Lod].Contains(sub.Name))
                    {
                        materials[sub.Lod].Add(sub.Name);
                    }
                }
                lods.Sort();
                foreach (uint lod in lods)
                {
                    string directory = Path.Combine(folder, "lod" + lod.ToString(CultureInfo.InvariantCulture));
                    Directory.CreateDirectory(directory);
                    string path = Path.Combine(directory, section.Name + ".obj");
                    ObjMesh.ExportLod(section, lod, path, "ForzaStudio EVO: " + section.Name + " from " + carbinName, MaterialsName);
                    manifest.AppendLine("obj " + Hash(path) + " " + Relative(lod, section.Name));
                    count++;
                }
            }
            foreach (KeyValuePair<uint, List<string>> pair in materials)
            {
                pair.Value.Sort(StringComparer.OrdinalIgnoreCase);
                ObjMesh.WriteMtl(Path.Combine(Path.Combine(folder, "lod" + pair.Key.ToString(CultureInfo.InvariantCulture)), MaterialsName), pair.Value);
            }
            File.WriteAllText(Path.Combine(folder, ManifestName), manifest.ToString(), new UTF8Encoding(false));
            return count;
        }

        // FM2 .carbin: type 2 with the FM2 marker 0x2CA (at 4, or at 0x104 after the type-2 prefix).
        // Its parts share two vertex buffers, so the structural model does not read it; the export
        // goes through the viewer's reader (ForzaCarBin), the import is not available yet.
        public static bool IsMotorsport2(byte[] carbin)
        {
            if (carbin.Length < 0x108)
            {
                return false;
            }
            uint type = U32(carbin, 0);
            return (type == 1 && U32(carbin, 4) == 0x2ca) || (type == 2 && U32(carbin, 0x104) == 0x2ca);
        }

        private static uint U32(byte[] data, int offset)
        {
            return (uint) ((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }

        // The same folder layout from the viewer's reader (FM2: LODs 1 to 5 in the one .carbin).
        // Positions, UVs (OBJ convention) and normals as the viewer shows them.
        public static int ExportLegacy(byte[] carbin, string carbinName, string folder, string source)
        {
            Directory.CreateDirectory(folder);
            foreach (string directory in Directory.GetDirectories(folder))
            {
                if (LodFolder.IsMatch(Path.GetFileName(directory)))
                {
                    foreach (string old in Directory.GetFiles(directory, "*.obj"))
                    {
                        File.Delete(old);
                    }
                }
            }
            CultureInfo inv = CultureInfo.InvariantCulture;
            StringBuilder manifest = new StringBuilder();
            manifest.AppendLine("# ForzaStudio EVO model folder of " + carbinName + " (Forza Motorsport 2), exported from " + source);
            manifest.AppendLine("# lodN\\<part>.obj = the part at that LOD, one group per material (usemtl = car material).");
            manifest.AppendLine("# FM2 models can be exported; importing them back is not available yet.");
            manifest.AppendLine("file " + carbinName);
            Dictionary<int, List<string>> materials = new Dictionary<int, List<string>>();
            int count = 0;
            using (ForzaCarBin model = new ForzaCarBin(carbinName, carbin))
            {
                foreach (ForzaCarSection section in model.Sections)
                {
                    SortedDictionary<int, List<ForzaCarSubSection>> lods = new SortedDictionary<int, List<ForzaCarSubSection>>();
                    foreach (ForzaCarSubSection sub in section.SubSections)
                    {
                        if (sub.Vertices == null || sub.Indices == null || sub.FaceCount == 0 || !HasUsableTriangle(sub))
                        {
                            continue;
                        }
                        if (!lods.ContainsKey(sub.Lod))
                        {
                            lods[sub.Lod] = new List<ForzaCarSubSection>();
                        }
                        lods[sub.Lod].Add(sub);
                        if (!materials.ContainsKey(sub.Lod))
                        {
                            materials[sub.Lod] = new List<string>();
                        }
                        if (!materials[sub.Lod].Contains(sub.Name))
                        {
                            materials[sub.Lod].Add(sub.Name);
                        }
                    }
                    foreach (KeyValuePair<int, List<ForzaCarSubSection>> lod in lods)
                    {
                        string directory = Path.Combine(folder, "lod" + lod.Key.ToString(inv));
                        Directory.CreateDirectory(directory);
                        string path = Path.Combine(directory, section.Name + ".obj");
                        using (StreamWriter obj = new StreamWriter(path, false, new UTF8Encoding(false)))
                        {
                            obj.WriteLine("# ForzaStudio EVO: " + section.Name + " from " + carbinName);
                            obj.WriteLine("# part " + section.Name + ", LOD " + lod.Key + "; units: meters, model space; one group per material (usemtl = car material)");
                            obj.WriteLine("mtllib " + MaterialsName);
                            int baseIndex = 1;
                            foreach (ForzaCarSubSection sub in lod.Value)
                            {
                                obj.WriteLine("g " + section.Name + "_" + sub.Name);
                                obj.WriteLine("usemtl " + sub.Name);
                                foreach (ForzaVertex v in sub.Vertices)
                                {
                                    // stray vertices (no face uses them, see below) written at the origin
                                    Vector3 p = PlausiblePosition(v.position) ? v.position : Vector3.Zero;
                                    obj.WriteLine(string.Format(inv, "v {0:R} {1:R} {2:R}", p.X, p.Y, p.Z));
                                }
                                foreach (ForzaVertex v in sub.Vertices)
                                {
                                    obj.WriteLine(string.Format(inv, "vt {0:R} {1:R}", v.texture0.X, v.texture0.Y));
                                }
                                foreach (ForzaVertex v in sub.Vertices)
                                {
                                    Vector3 n = (v.normal.LengthSquared() > 1e-12f) ? Vector3.Normalize(v.normal) : Vector3.UnitY;
                                    obj.WriteLine(string.Format(inv, "vn {0:R} {1:R} {2:R}", n.X, n.Y, n.Z));
                                }
                                for (int i = 0; i + 2 < sub.Indices.Length; i += 3)
                                {
                                    int a = sub.Indices[i];
                                    int b = sub.Indices[i + 1];
                                    int c = sub.Indices[i + 2];
                                    if (!UsableTriangle(sub, a, b, c))
                                    {
                                        continue;
                                    }
                                    // Front face as the stored normals say (the strip winding alternates).
                                    Vector3 face = Vector3.Cross(sub.Vertices[b].position - sub.Vertices[a].position, sub.Vertices[c].position - sub.Vertices[a].position);
                                    if (Vector3.Dot(face, sub.Vertices[a].normal + sub.Vertices[b].normal + sub.Vertices[c].normal) < 0)
                                    {
                                        int swap = a;
                                        a = b;
                                        b = swap;
                                    }
                                    obj.WriteLine("f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}", a + baseIndex, b + baseIndex, c + baseIndex);
                                }
                                baseIndex += sub.Vertices.Length;
                            }
                        }
                        manifest.AppendLine("obj " + Hash(path) + " " + Relative((uint) lod.Key, section.Name));
                        count++;
                    }
                }
            }
            foreach (KeyValuePair<int, List<string>> pair in materials)
            {
                pair.Value.Sort(StringComparer.OrdinalIgnoreCase);
                ObjMesh.WriteMtl(Path.Combine(Path.Combine(folder, "lod" + pair.Key.ToString(inv)), MaterialsName), pair.Value);
            }
            File.WriteAllText(Path.Combine(folder, ManifestName), manifest.ToString(), new UTF8Encoding(false));
            return count;
        }

        // Some game files hold placeholder geometry with vertices at about -1.7e38 (FM2 Lingenfelter "winga": one
        // quad, 4 equal vertices; also FM2 288 GTO, FTO, Cupra GT): no real surface. Their triangles are left out,
        // and a part left with none is not exported, so the port handles it as a part the car does not have.
        public static bool PlausiblePosition(Vector3 p)
        {
            return !float.IsNaN(p.X) && !float.IsNaN(p.Y) && !float.IsNaN(p.Z)
                && Math.Abs(p.X) < 100000f && Math.Abs(p.Y) < 100000f && Math.Abs(p.Z) < 100000f;
        }

        private static bool UsableTriangle(ForzaCarSubSection sub, int a, int b, int c)
        {
            int n = sub.Vertices.Length;
            if (a == b || b == c || a == c || a < 0 || b < 0 || c < 0 || a >= n || b >= n || c >= n)
            {
                return false;
            }
            return PlausiblePosition(sub.Vertices[a].position) && PlausiblePosition(sub.Vertices[b].position) && PlausiblePosition(sub.Vertices[c].position);
        }

        private static bool HasUsableTriangle(ForzaCarSubSection sub)
        {
            for (int i = 0; i + 2 < sub.Indices.Length; i += 3)
            {
                if (UsableTriangle(sub, sub.Indices[i], sub.Indices[i + 1], sub.Indices[i + 2]))
                {
                    return true;
                }
            }
            return false;
        }

        #endregion

        #region Import plan

        // What an import does to one part of one model file.
        public class PartPlan
        {
            public string Kind;
            public string Part;
            public bool IsNew;
            public bool Remove;
            public string Template;                     // new parts: the part copied for materials, damage data and draw lists
            public List<string> Templates = new List<string>();   // the choices (parts of the same file)
            public string BorrowedFrom;                 // new part with no OBJ in this file: geometry of the other file, at every LOD of the template
            public SortedDictionary<uint, string> Rebuild = new SortedDictionary<uint, string>();   // LOD -> OBJ
            public List<uint> Keep = new List<uint>();
            public List<uint> Drop = new List<uint>();

            public string Describe()
            {
                string file = KindName(this.Kind);
                if (this.Remove)
                {
                    return string.Format("{0} {1}: remove (its OBJs were deleted)", file, this.Part);
                }
                List<string> parts = new List<string>();
                if (this.BorrowedFrom != null)
                {
                    parts.Add("built at every LOD of its template");
                }
                else if (this.Rebuild.Count > 0)
                {
                    parts.Add("rebuild LOD " + Join(this.Rebuild.Keys));
                }
                if (this.Drop.Count > 0)
                {
                    parts.Add("drop LOD " + Join(this.Drop));
                }
                if (this.Keep.Count > 0 && !this.IsNew)
                {
                    parts.Add("keep LOD " + Join(this.Keep));
                }
                string what = string.Join(", ", parts.ToArray());
                if (this.IsNew)
                {
                    return string.Format("{0} {1}: NEW part, {2}{3}", file, this.Part, what, (this.BorrowedFrom != null) ? " (geometry from " + this.BorrowedFrom + ")" : "");
                }
                return string.Format("{0} {1}: {2}", file, this.Part, what);
            }
        }

        public class Plan
        {
            public Dictionary<string, string> Folders;          // kind -> .carbin folder imported
            public List<string> WithoutManifest = new List<string>();
            public List<PartPlan> Parts = new List<PartPlan>();
            public List<string> Ignored = new List<string>();     // OBJs outside the lodN folders
            public List<string> Errors = new List<string>();      // problems that block the import
            public List<string> Warnings = new List<string>();
            public Dictionary<string, ObjMesh> Meshes = new Dictionary<string, ObjMesh>(StringComparer.OrdinalIgnoreCase);   // changed OBJs, read and checked

            public bool IsEmpty
            {
                get { return this.Parts.Count == 0; }
            }
        }

        private static string Join(IEnumerable<uint> values)
        {
            List<string> text = new List<string>();
            foreach (uint value in values)
            {
                text.Add(value.ToString(CultureInfo.InvariantCulture));
            }
            return string.Join(",", text.ToArray());
        }

        // Compares each .carbin folder with its model (and the manifest of its export, when there is
        // one). Without a manifest every OBJ counts as changed and nothing is dropped. rebuildAll:
        // unchanged OBJs are rebuilt too (fresh precalc lighting and UV data).
        public static Plan Prepare(Dictionary<string, string> folders, Dictionary<string, CarbinModel> models, bool rebuildAll)
        {
            return Prepare(folders, models, rebuildAll, false);
        }

        // allowAnyMaterial: a model imported from another game (FM2) may use materials the donor model has none of; they
        // are checked later (CarPort material review), the OBJ names are the game's own.
        public static Plan Prepare(Dictionary<string, string> folders, Dictionary<string, CarbinModel> models, bool rebuildAll, bool allowAnyMaterial)
        {
            Plan plan = new Plan();
            plan.Folders = folders;

            // Car materials (any model file): an OBJ group must use one of them.
            Dictionary<string, bool> carMaterials = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (CarbinModel model in models.Values)
            {
                foreach (CarbinModel.Section section in model.Sections)
                {
                    foreach (CarbinModel.SubSection sub in section.SubSections)
                    {
                        carMaterials[sub.Name] = true;
                    }
                }
            }

            Dictionary<string, PartPlan> added = new Dictionary<string, PartPlan>(StringComparer.OrdinalIgnoreCase);
            foreach (string kind in new string[] { Main, Lod0, Cockpit })
            {
                string folder;
                CarbinModel model;
                if (!folders.TryGetValue(kind, out folder) || !models.TryGetValue(kind, out model))
                {
                    continue;
                }
                string file = KindName(kind);

                // Exported OBJs and their hashes.
                Dictionary<string, string> exported = null;
                string manifestPath = Path.Combine(folder, ManifestName);
                if (File.Exists(manifestPath))
                {
                    exported = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string line in File.ReadAllLines(manifestPath))
                    {
                        string[] fields = line.Split(new char[] { ' ' }, 3);
                        if (fields.Length == 3 && fields[0] == "obj")
                        {
                            exported[fields[2].Trim()] = fields[1];
                        }
                    }
                }
                else
                {
                    plan.WithoutManifest.Add(Path.GetFileName(folder));
                }

                // OBJs present, by part (spelled as the model spells it when it has it) and LOD.
                // The main file holds LOD 0 too when the model has no _lod0 file (wheels) or already
                // has that LOD.
                bool mainWithLod0 = kind == Main && (!models.ContainsKey(Lod0) || HasLod(model, 0));
                Dictionary<string, SortedDictionary<uint, string>> present = new Dictionary<string, SortedDictionary<uint, string>>(StringComparer.OrdinalIgnoreCase);
                foreach (string[] found in Scan(folder, plan.Ignored))
                {
                    uint lod = uint.Parse(found[0], CultureInfo.InvariantCulture);
                    if ((kind == Main) ? (lod == 0 && !mainWithLod0) : (lod != 0))
                    {
                        plan.Errors.Add(string.Format("{0}\\lod{1}\\{2}.obj: the {0} file has {3}", Path.GetFileName(folder), lod, found[1], (kind == Main) ? "LODs 1 and up (LOD 0 is the _lod0 file)" : "only LOD 0"));
                        continue;
                    }
                    CarbinModel.Section existing = FindIgnoreCase(model, found[1]);
                    string part = (existing != null) ? existing.Name : found[1];
                    if (!present.ContainsKey(part))
                    {
                        present[part] = new SortedDictionary<uint, string>();
                    }
                    present[part][lod] = found[2];
                }

                // Parts the model has.
                foreach (CarbinModel.Section section in model.Sections)
                {
                    SortedDictionary<uint, string> mine;
                    present.TryGetValue(section.Name, out mine);
                    PartPlan part = new PartPlan();
                    part.Kind = kind;
                    part.Part = section.Name;
                    List<uint> lods = new List<uint>();
                    foreach (CarbinModel.SubSection sub in section.SubSections)
                    {
                        if (!lods.Contains(sub.Lod))
                        {
                            lods.Add(sub.Lod);
                        }
                    }
                    if (mine != null)
                    {
                        foreach (uint lod in mine.Keys)
                        {
                            if (!lods.Contains(lod))
                            {
                                lods.Add(lod);
                            }
                        }
                    }
                    lods.Sort();
                    foreach (uint lod in lods)
                    {
                        string path = null;
                        if (mine != null)
                        {
                            mine.TryGetValue(lod, out path);
                        }
                        string hash = null;
                        bool wasExported = exported != null && exported.TryGetValue(Relative(lod, section.Name), out hash);
                        if (path == null)
                        {
                            if (wasExported)
                            {
                                part.Drop.Add(lod);
                            }
                            else
                            {
                                part.Keep.Add(lod);
                            }
                        }
                        else if (!rebuildAll && wasExported && hash == Hash(path))
                        {
                            part.Keep.Add(lod);
                        }
                        else
                        {
                            part.Rebuild[lod] = path;
                        }
                    }
                    if (part.Rebuild.Count == 0 && part.Drop.Count == 0)
                    {
                        continue;
                    }
                    part.Remove = part.Rebuild.Count == 0 && part.Keep.Count == 0;
                    plan.Parts.Add(part);
                }

                // New parts.
                foreach (KeyValuePair<string, SortedDictionary<uint, string>> pair in present)
                {
                    if (model.Find(pair.Key) != null)
                    {
                        continue;
                    }
                    PartPlan part = new PartPlan();
                    part.Kind = kind;
                    part.Part = pair.Key;
                    part.IsNew = true;
                    foreach (KeyValuePair<uint, string> lod in pair.Value)
                    {
                        part.Rebuild[lod.Key] = lod.Value;
                    }
                    plan.Parts.Add(part);
                    added[kind + "|" + pair.Key] = part;
                }
            }

            // A new part must be in the main file (far LODs) and in the lod0 file (up close): when the
            // OBJs cover only one of them, the other uses the same geometry at every LOD of its template.
            List<PartPlan> borrowed = new List<PartPlan>();
            foreach (PartPlan part in added.Values)
            {
                if (part.Kind == Cockpit)
                {
                    continue;
                }
                string other = (part.Kind == Main) ? Lod0 : Main;
                if (!models.ContainsKey(other) || FindIgnoreCase(models[other], part.Part) != null || added.ContainsKey(other + "|" + part.Part))
                {
                    continue;
                }
                PartPlan copy = new PartPlan();
                copy.Kind = other;
                copy.Part = part.Part;
                copy.IsNew = true;
                uint lod = FirstKey(part.Rebuild);
                copy.BorrowedFrom = (part.Kind == Main) ? "main\\lod" + lod : "lod0\\lod0";
                copy.Rebuild[uint.MaxValue] = part.Rebuild[lod];   // LODs set from the template on import
                borrowed.Add(copy);
            }
            plan.Parts.AddRange(borrowed);

            // Read and check every OBJ to rebuild: faces, and car materials for every group.
            foreach (PartPlan part in plan.Parts)
            {
                foreach (string path in part.Rebuild.Values)
                {
                    if (plan.Meshes.ContainsKey(path))
                    {
                        continue;
                    }
                    string shown = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path))) + "\\" + Path.GetFileName(Path.GetDirectoryName(path)) + "\\" + Path.GetFileName(path);
                    ObjMesh mesh;
                    try
                    {
                        mesh = ObjMesh.Load(path);
                    }
                    catch (Exception exception)
                    {
                        plan.Errors.Add(shown + ": " + exception.Message);
                        continue;
                    }
                    if (mesh.TriangleCount == 0)
                    {
                        plan.Errors.Add(shown + ": no faces (delete the OBJ to remove the part at this LOD)");
                        continue;
                    }
                    foreach (ObjMesh.Material material in mesh.Materials)
                    {
                        if (!allowAnyMaterial && !carMaterials.ContainsKey(material.Name))
                        {
                            plan.Errors.Add(string.Format("{0}: material \"{1}\" is not one of the car's (usemtl names are case-sensitive)", shown, material.Name));
                        }
                    }
                    plan.Meshes[path] = mesh;
                }
            }

            // Templates: the existing part of the same file nearest to the new geometry.
            foreach (PartPlan part in plan.Parts)
            {
                if (!part.IsNew)
                {
                    continue;
                }
                ObjMesh mesh;
                plan.Meshes.TryGetValue(part.Rebuild[FirstKey(part.Rebuild)], out mesh);
                Vector3 center = (mesh != null) ? Center(mesh) : Vector3.Zero;
                double best = double.MaxValue;
                foreach (CarbinModel.Section section in models[part.Kind].Sections)
                {
                    if (section.SubSections.Count == 0)
                    {
                        continue;
                    }
                    part.Templates.Add(section.Name);
                    double dx = section.Bounds[0] - center.X;
                    double dy = section.Bounds[1] - center.Y;
                    double dz = section.Bounds[2] - center.Z;
                    double distance = (dx * dx) + (dy * dy) + (dz * dz);
                    if (distance < best)
                    {
                        best = distance;
                        part.Template = section.Name;
                    }
                }
                part.Templates.Sort(StringComparer.OrdinalIgnoreCase);
                if (part.Template == null)
                {
                    plan.Errors.Add(part.Part + ": no part in the " + KindName(part.Kind) + " file to use as a template");
                }
            }

            if (plan.WithoutManifest.Count > 0)
            {
                plan.Warnings.Add("No " + ManifestName + " in " + string.Join(", ", plan.WithoutManifest.ToArray()) + ": every OBJ there is rebuilt and nothing is dropped.");
            }
            return plan;
        }

        private static uint FirstKey(SortedDictionary<uint, string> map)
        {
            foreach (uint key in map.Keys)
            {
                return key;
            }
            return 0;
        }

        private static bool HasLod(CarbinModel model, uint lod)
        {
            foreach (CarbinModel.Section section in model.Sections)
            {
                foreach (CarbinModel.SubSection sub in section.SubSections)
                {
                    if (sub.Lod == lod)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static CarbinModel.Section FindIgnoreCase(CarbinModel model, string name)
        {
            foreach (CarbinModel.Section section in model.Sections)
            {
                if (string.Equals(section.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return section;
                }
            }
            return null;
        }

        private static Vector3 Center(ObjMesh mesh)
        {
            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);
            foreach (ObjMesh.Material material in mesh.Materials)
            {
                foreach (ObjMesh.Corner[] triangle in material.Triangles)
                {
                    foreach (ObjMesh.Corner corner in triangle)
                    {
                        min = Vector3.Min(min, corner.Position);
                        max = Vector3.Max(max, corner.Position);
                    }
                }
            }
            return (min.X > max.X) ? Vector3.Zero : (min + max) / 2;
        }

        #endregion

        #region Import

        // Applies the plan to the models; returns the kinds of the models changed and fills the report.
        public static List<string> Apply(Plan plan, Dictionary<string, CarbinModel> models, StringBuilder report)
        {
            List<string> changed = new List<string>();
            // New parts first: their templates are copied before any part is rebuilt or removed.
            List<PartPlan> order = new List<PartPlan>();
            foreach (PartPlan part in plan.Parts)
            {
                if (part.IsNew)
                {
                    order.Add(part);
                }
            }
            foreach (PartPlan part in plan.Parts)
            {
                if (!part.IsNew)
                {
                    order.Add(part);
                }
            }
            foreach (PartPlan part in order)
            {
                CarbinModel model = models[part.Kind];
                string file = KindName(part.Kind);
                if (part.Remove)
                {
                    model.RemoveSection(part.Part);
                    report.AppendLine(file + " " + part.Part + ": removed");
                }
                else
                {
                    Dictionary<uint, ObjMesh> meshes = new Dictionary<uint, ObjMesh>();
                    if (part.IsNew)
                    {
                        model.DuplicateSection(part.Template, part.Part);
                    }
                    if (part.Rebuild.ContainsKey(uint.MaxValue))
                    {
                        // Borrowed geometry: one mesh at every LOD of the template.
                        ObjMesh mesh = plan.Meshes[part.Rebuild[uint.MaxValue]];
                        foreach (CarbinModel.SubSection sub in model.Find(part.Part).SubSections)
                        {
                            meshes[sub.Lod] = mesh;
                        }
                    }
                    else
                    {
                        foreach (KeyValuePair<uint, string> pair in part.Rebuild)
                        {
                            meshes[pair.Key] = plan.Meshes[pair.Value];
                        }
                        foreach (uint lod in part.Keep)
                        {
                            meshes[lod] = null;
                        }
                    }
                    if (meshes.Count == 0)
                    {
                        model.RemoveSection(part.Part);
                        report.AppendLine(file + " " + part.Part + ": removed (no LOD left)");
                    }
                    else
                    {
                        string done = model.RebuildLods(part.Part, meshes, null, false);
                        report.AppendLine(file + " " + (part.IsNew ? "NEW " : "") + done.Replace("\r\n", "\r\n    "));
                    }
                }
                if (!changed.Contains(part.Kind))
                {
                    changed.Add(part.Kind);
                }
            }
            return changed;
        }

        #endregion
    }
}
