namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;

    // The PART tab's Export OBJ / Import OBJ of one part on Forza Motorsport 2 and 3 model files (2026-11-21; FM4 / FH
    // files keep the code in ArchiveEditorForm.PartImport.cs). Same choices as on FM4: replace the part's geometry,
    // replace one material, add materials to the part, or make a new part with another part as template.
    //  - FM3: the edit runs on the file in FM4 form (PartSource.Motorsport3AsMotorsport4), the baked light of the new
    //    vertices is computed on the car (CarbinModel.BeginLighting / FinishLighting), then the part goes back into the
    //    FM3 file in FM3's layout (CarModelFolder.ToMotorsport3); every other part keeps every byte.
    //  - FM2: the part is built in FM2's own format (Fm2CarbinModel.RebuildFromObj): corners that did not change keep the
    //    game's vertex, new ones get computed baked light. "Fit UVs" has no FM2 form (FM2 keeps no material regions)
    //    and is left out with a line in the report.
    public static class PartObjImport
    {
        public class Request
        {
            public string Section;              // the existing part worked on (null with NewPart)
            public string Basis;                // the part whose settings are used (the template of a new part)
            public string Target;               // the part written
            public bool NewPart;
            public bool Merge;                  // replace one material / add materials, the rest of the part stays
            public string ReplaceMaterial;      // with Merge: the material replaced (null = only add)
            public bool FitUv;
            public ObjMesh Mesh;
            public Dictionary<string, string> Map;   // OBJ material -> car material (null / empty = skipped)
        }

        public static bool Handles(byte[] data)
        {
            return CarbinModel.IsMotorsport3File(data) || CarModelFolder.IsMotorsport2(data);
        }

        // The file with the import done, or null when this file has nothing to change (no such part / material).
        public static byte[] Run(byte[] data, Request request, List<string> notes)
        {
            return CarModelFolder.IsMotorsport2(data) ? RunMotorsport2(data, request, notes) : RunMotorsport3(data, request, notes);
        }

        // Part and material names of an FM2 / FM3 / FM4 model file (the lists of the import window).
        public static CarbinModel Names(byte[] data)
        {
            if (CarModelFolder.IsMotorsport2(data))
            {
                return CarModelFolder.PlanModel(new Fm2CarbinModel(data));
            }
            return CarbinModel.IsMotorsport3File(data) ? new CarbinModel(data, true) : new CarbinModel(data);
        }

        private static List<uint> Lods(CarbinModel.Section part)
        {
            List<uint> lods = new List<uint>();
            foreach (CarbinModel.SubSection sub in part.SubSections)
            {
                if (!lods.Contains(sub.Lod))
                {
                    lods.Add(sub.Lod);
                }
            }
            return lods;
        }

        // The part's LODs with 'replace' taken out and the OBJ's triangles added under the chosen car materials; a LOD
        // that does not draw the replaced material stays as it is (null). sameGroup: the added triangles join the
        // part's group of that material (FM2), else they are a group of their own (FM4 form: only theirs are UV-fitted).
        private static Dictionary<uint, ObjMesh> Merged(IEnumerable<uint> lods, Func<uint, ObjMesh> current, Request request, bool sameGroup, out bool changed)
        {
            Dictionary<uint, ObjMesh> meshes = new Dictionary<uint, ObjMesh>();
            changed = false;
            foreach (uint lod in lods)
            {
                ObjMesh mesh = current(lod);
                if (request.ReplaceMaterial != null)
                {
                    string gone = request.ReplaceMaterial;
                    if (mesh.Materials.RemoveAll(delegate(ObjMesh.Material m) { return m.Name == gone; }) == 0)
                    {
                        meshes[lod] = null;
                        continue;
                    }
                }
                Dictionary<string, ObjMesh.Material> addedInto = new Dictionary<string, ObjMesh.Material>();
                foreach (ObjMesh.Material added in request.Mesh.Materials)
                {
                    string carMaterial;
                    if (!request.Map.TryGetValue(added.Name, out carMaterial) || string.IsNullOrEmpty(carMaterial))
                    {
                        continue;
                    }
                    ObjMesh.Material into = null;
                    if (!addedInto.TryGetValue(carMaterial, out into))
                    {
                        if (sameGroup)
                        {
                            foreach (ObjMesh.Material have in mesh.Materials)
                            {
                                if (have.Name == carMaterial)
                                {
                                    into = have;
                                }
                            }
                        }
                        if (into == null)
                        {
                            into = new ObjMesh.Material();
                            into.Name = carMaterial;
                            mesh.Materials.Add(into);
                        }
                        addedInto[carMaterial] = into;
                    }
                    into.Triangles.AddRange(added.Triangles);
                }
                meshes[lod] = mesh;
                changed = true;
            }
            return meshes;
        }

        #region FM3

        private static byte[] RunMotorsport3(byte[] data, Request request, List<string> notes)
        {
            CarbinModel model = new CarbinModel(PartSource.Motorsport3AsMotorsport4(data));
            if (model.Find(request.Basis) == null)
            {
                return null;
            }
            CarbinModel.LightingState light = null;
            try
            {
                light = model.BeginLighting();
            }
            catch (Exception exception)
            {
                notes.Add("baked light not computed (" + exception.Message + "); new vertices get the open-panel estimate");
            }
            if (request.NewPart)
            {
                if (model.Find(request.Target) != null)
                {
                    model.RemoveSection(request.Target);
                }
                model.DuplicateSection(request.Basis, request.Target);
            }
            string done;
            if (request.Merge)
            {
                CarbinModel.Section part = model.Find(request.Section);
                bool changed;
                Dictionary<uint, ObjMesh> meshes = Merged(Lods(part), delegate(uint lod) { return ObjMesh.FromLod(part, lod); }, request, false, out changed);
                done = changed ? model.RebuildLods(request.Section, meshes, null, request.FitUv) : null;
            }
            else
            {
                done = model.ReplaceGeometry(request.Target, request.Mesh, request.Map, request.FitUv);
            }
            if (done == null)
            {
                return null;
            }
            notes.Add(done);
            if (light != null)
            {
                try
                {
                    string line = model.FinishLighting(light);
                    if (line != null)
                    {
                        notes.Add(line);
                    }
                }
                catch (Exception exception)
                {
                    notes.Add("baked light not computed (" + exception.Message + "); new vertices keep the open-panel estimate");
                }
            }
            CarModelFolder.Plan plan = new CarModelFolder.Plan();
            CarModelFolder.PartPlan step = new CarModelFolder.PartPlan();
            step.Kind = CarModelFolder.Main;
            step.Part = request.Target;
            plan.Parts.Add(step);
            StringBuilder report = new StringBuilder();
            byte[] result = CarModelFolder.ToMotorsport3(plan, CarModelFolder.Main, model, data, report);
            notes.Add(report.ToString().Trim());
            return result;
        }

        #endregion

        #region FM2

        // The OBJ under the car's material names: skipped materials out, materials sent to the same name joined.
        private static ObjMesh Mapped(ObjMesh mesh, Dictionary<string, string> map)
        {
            ObjMesh result = new ObjMesh();
            foreach (ObjMesh.Material material in mesh.Materials)
            {
                string carMaterial;
                if (!map.TryGetValue(material.Name, out carMaterial) || string.IsNullOrEmpty(carMaterial))
                {
                    continue;
                }
                ObjMesh.Material into = null;
                foreach (ObjMesh.Material have in result.Materials)
                {
                    if (have.Name == carMaterial)
                    {
                        into = have;
                    }
                }
                if (into == null)
                {
                    into = new ObjMesh.Material();
                    into.Name = carMaterial;
                    result.Materials.Add(into);
                }
                into.Triangles.AddRange(material.Triangles);
            }
            return result;
        }

        // One part of an FM2 file at one LOD, as Export Model to OBJ Folder writes it (null: nothing to draw there).
        // 'folder' is a temporary folder the caller deletes.
        public static ObjMesh Motorsport2Lod(byte[] data, string folder, string part, uint lod)
        {
            if (!Directory.Exists(folder) || Directory.GetDirectories(folder).Length == 0)
            {
                CarModelFolder.ExportLegacy(data, "model.carbin", folder, "part");
            }
            string obj = Path.Combine(Path.Combine(folder, "lod" + lod.ToString(System.Globalization.CultureInfo.InvariantCulture)), part + ".obj");
            return File.Exists(obj) ? ObjMesh.Load(obj) : null;
        }

        public static string TempFolder()
        {
            return Path.Combine(Path.GetTempPath(), "ForzaStudio_fm2part_" + Guid.NewGuid().ToString("N"));
        }

        public static void DropFolder(string folder)
        {
            try
            {
                if (folder != null && Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
            catch (IOException)
            {
            }
        }

        private static byte[] RunMotorsport2(byte[] data, Request request, List<string> notes)
        {
            Fm2CarbinModel model = new Fm2CarbinModel(data);
            Fm2CarbinModel.Part basis = model.Find(request.Basis);
            if (basis == null)
            {
                return null;
            }
            List<uint> lods = new List<uint>();
            foreach (Fm2CarbinModel.Mesh mesh in basis.Meshes)
            {
                if (!lods.Contains(mesh.Lod))
                {
                    lods.Add(mesh.Lod);
                }
            }
            lods.Sort();
            Dictionary<uint, ObjMesh> rebuild = new Dictionary<uint, ObjMesh>();
            if (request.Merge)
            {
                string folder = TempFolder();
                try
                {
                    bool changed;
                    Dictionary<uint, ObjMesh> meshes = Merged(lods, delegate(uint lod) { return Motorsport2Lod(data, folder, request.Section, lod) ?? new ObjMesh(); }, request, true, out changed);
                    if (!changed)
                    {
                        return null;
                    }
                    foreach (KeyValuePair<uint, ObjMesh> pair in meshes)
                    {
                        if (pair.Value != null && pair.Value.TriangleCount > 0)
                        {
                            rebuild[pair.Key] = pair.Value;
                        }
                    }
                }
                finally
                {
                    DropFolder(folder);
                }
            }
            else
            {
                ObjMesh mapped = Mapped(request.Mesh, request.Map);
                if (mapped.TriangleCount == 0)
                {
                    return null;
                }
                foreach (uint lod in lods)
                {
                    rebuild[lod] = mapped;
                }
                if (request.NewPart && model.Find(request.Target) != null)
                {
                    model.RemovePart(request.Target);
                }
            }
            if (rebuild.Count == 0)
            {
                return null;
            }
            notes.Add(model.RebuildFromObj(request.Target, request.NewPart ? request.Basis : null, rebuild, new List<uint>()));
            if (request.FitUv)
            {
                notes.Add("\"Fit UVs\" left out: Forza Motorsport 2 keeps no material regions, the OBJ's UVs are used as they are");
            }
            return model.ToBytes();
        }

        #endregion
    }
}
