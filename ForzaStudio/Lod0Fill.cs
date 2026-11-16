using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace ForzaStudio
{
    // Parts drawn in LOD 1-5 (main .carbin) but missing from their close-up model, without UI. Many cars ported from
    // FM2 have such parts: far away they are there, up close they vanish. The close-up model of a part follows FM4:
    // cockpit parts (hood, interior, mirrors, seats, front glass...: CarPort.CockpitPart) go in _cockpit, every
    // other part in _lod0 (user 2026-11-15: hoods from LOD 1 into the cockpit model).
    // The copy is the main file's LOD 1 of the part made a LOD 0 part the way the port builds a whole close-up
    // model (CarbinModel.CloseUpFromLod1: LOD 1 subsections only, 32-bit strips, vertices in the Lod0 buffer with
    // their damage words, the LOD 1 draw-list entries of both passes), then imported into the existing close-up
    // model. UVs stay those of LOD 1 (the port's close-ups use them as they are).
    // Reusable for any part that arrives with LOD 1-5 only (future FM2 part imports): put it in the main model,
    // then run this on it. Result is filled only when every new file was read back and passed every check.
    public class Lod0Fill
    {
        public class Candidate
        {
            public string Part;
            public string Target;         // "_lod0" or "_cockpit": the close-up file it belongs to
            public List<uint> MainLods = new List<uint>();
            public List<string> Materials = new List<string>();
            public bool HasLod1;
        }

        // Model files keyed like ArchiveEditorForm.CarModelFiles: "" (main), "_lod0", "_cockpit".
        public Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>();
        public List<string> Parts = new List<string>();

        public Dictionary<string, byte[]> Result;
        public readonly List<string> Log = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        public static string TargetOf(string part)
        {
            return CarPort.CockpitPart(part) ? "_cockpit" : "_lod0";
        }

        // Parts of the main model missing from their close-up model (by name). A part whose close-up file the car
        // does not have is not listed.
        public static List<Candidate> Missing(Dictionary<string, byte[]> files)
        {
            CarbinModel main = new CarbinModel(files[""]);
            Dictionary<string, CarbinModel> closeUps = new Dictionary<string, CarbinModel>();
            foreach (string key in new string[] { "_lod0", "_cockpit" })
            {
                if (files.ContainsKey(key) && files[key] != null)
                {
                    closeUps[key] = new CarbinModel(files[key]);
                }
            }
            List<Candidate> list = new List<Candidate>();
            foreach (CarbinModel.Section section in main.Sections)
            {
                string target = TargetOf(section.Name);
                CarbinModel closeUp;
                if (!closeUps.TryGetValue(target, out closeUp) || closeUp.Find(section.Name) != null)
                {
                    continue;
                }
                Candidate c = new Candidate();
                c.Part = section.Name;
                c.Target = target;
                foreach (CarbinModel.SubSection sub in section.SubSections)
                {
                    if (!c.MainLods.Contains(sub.Lod))
                    {
                        c.MainLods.Add(sub.Lod);
                    }
                    if (sub.Lod == 1)
                    {
                        c.HasLod1 = true;
                        if (!c.Materials.Contains(sub.Name))
                        {
                            c.Materials.Add(sub.Name);
                        }
                    }
                }
                if (c.MainLods.Count == 0)
                {
                    continue;    // a part with no geometry at all
                }
                c.MainLods.Sort();
                list.Add(c);
            }
            return list;
        }

        public void Run()
        {
            this.Result = null;
            if (!this.Files.ContainsKey("") || this.Files[""] == null)
            {
                throw new InvalidOperationException("The car needs its main model.");
            }
            if (this.Parts.Count == 0)
            {
                throw new InvalidOperationException("No part chosen.");
            }
            string mainHash = Hash(this.Files[""]);
            CarbinModel main = new CarbinModel(this.Files[""]);
            Dictionary<string, byte[]> result = new Dictionary<string, byte[]>();

            foreach (string key in new string[] { "_lod0", "_cockpit" })
            {
                List<string> parts = this.Parts.FindAll(delegate(string p) { return TargetOf(p) == key; });
                if (parts.Count == 0)
                {
                    continue;
                }
                if (!this.Files.ContainsKey(key) || this.Files[key] == null)
                {
                    throw new InvalidOperationException("The car has no " + PartImport.FileLabel(key) + " model for " + string.Join(", ", parts.ToArray()) + ".");
                }
                result[key] = this.FillFile(main, key, parts);
            }
            if (Hash(this.Files[""]) != mainHash)
            {
                throw new InvalidOperationException("Internal error: the main model bytes changed.");
            }
            this.Result = result;
        }

        private byte[] FillFile(CarbinModel main, string key, List<string> parts)
        {
            string label = PartImport.FileLabel(key);
            string fileName = "x" + key + ".carbin";
            bool cockpit = key == "_cockpit";
            CarbinModel target = new CarbinModel(this.Files[key]);
            List<string> before = new List<string>();
            List<string> ownMaterials = new List<string>();
            foreach (CarbinModel.Section section in target.Sections)
            {
                before.Add(section.Name);
                foreach (CarbinModel.SubSection sub in section.SubSections)
                {
                    if (!ownMaterials.Contains(sub.Name))
                    {
                        ownMaterials.Add(sub.Name);
                    }
                }
            }

            Dictionary<string, int> expectedSubs = new Dictionary<string, int>();
            foreach (string part in parts)
            {
                CarbinModel.Section section = main.Find(part);
                if (section == null)
                {
                    throw new InvalidOperationException("The main model has no part " + part + ".");
                }
                if (target.Find(part) != null)
                {
                    throw new InvalidOperationException("The " + label + " model already has " + part + ".");
                }
                int subs = 0;
                foreach (CarbinModel.SubSection sub in section.SubSections)
                {
                    subs += (sub.Lod == 1) ? 1 : 0;
                }
                if (subs == 0)
                {
                    throw new InvalidOperationException(part + " has no LOD 1 in the main model.");
                }
                expectedSubs[part] = subs;
            }

            CarbinModel closeUp = main.CloseUpFromLod1(target.Head, delegate(string name) { return parts.Contains(name); });
            Dictionary<string, int> expectedVertices = new Dictionary<string, int>();
            foreach (CarbinModel.Section section in closeUp.Sections)
            {
                List<string> renamed = new List<string>();
                foreach (CarbinModel.SubSection sub in section.SubSections)
                {
                    string name = sub.Name;
                    // FM4 light names in close-up files: tail_light_lod0 (lod0 + cockpit), reverse_light_lod0 (lod0).
                    sub.Name = CarPort.LodVariant(sub.Name, fileName);
                    // As the port does: a LOD 1 tire in LOD 0 draws the separate FM4 tire and renders black ->
                    // inner_rim (CarPort.RenameLod0Tire); the LOD 1 interior has no cockpit_ technique -> leather.
                    if (sub.Name == "tire")
                    {
                        sub.Name = "inner_rim";
                    }
                    if (cockpit && string.Equals(sub.Name, "interior", StringComparison.OrdinalIgnoreCase))
                    {
                        sub.Name = "leather";
                    }
                    if (sub.Name != name)
                    {
                        renamed.Add(name + " -> " + sub.Name);
                    }
                }
                expectedVertices[section.Name] = section.Lod0.Count;
                string report = target.ImportSection(closeUp, section.Name, section.Name, false);
                this.Log.Add(label + ": " + report.Replace("\r\n", "; ").TrimEnd(' ', ';') + " (from LOD 1 of the main model)");
                if (renamed.Count > 0)
                {
                    this.Log.Add("  " + section.Name + " materials renamed for LOD 0: " + string.Join(", ", renamed.ToArray()));
                }
            }
            byte[] bytes = target.ToBytes();

            // Checks on the bytes that would be written.
            CarbinModel check = new CarbinModel(bytes);
            check.CheckDrawLists();
            List<string> names = new List<string>();
            foreach (CarbinModel.Section section in check.Sections)
            {
                if (names.Contains(section.Name))
                {
                    throw new InvalidOperationException("Check failed: two parts named " + section.Name + " in the " + label + " model.");
                }
                names.Add(section.Name);
            }
            foreach (string name in before)
            {
                if (!names.Contains(name))
                {
                    throw new InvalidOperationException("Check failed: " + name + " is gone from the " + label + " model.");
                }
            }
            foreach (string part in parts)
            {
                CarbinModel.Section section = check.Find(part);
                if (section == null)
                {
                    throw new InvalidOperationException("Check failed: " + part + " is not in the new " + label + " model.");
                }
                if (section.SubSections.Count != expectedSubs[part])
                {
                    throw new InvalidOperationException(string.Format("Check failed: {0} has {1} materials, LOD 1 has {2}.", part, section.SubSections.Count, expectedSubs[part]));
                }
                int vertices;
                if (!expectedVertices.TryGetValue(part, out vertices) || section.Lod0.Count != vertices || section.Lod0.Count == 0)
                {
                    throw new InvalidOperationException("Check failed: " + part + " vertex count differs from its LOD 1.");
                }
                foreach (CarbinModel.SubSection sub in section.SubSections)
                {
                    if (sub.Lod != 0)
                    {
                        throw new InvalidOperationException("Check failed: " + part + " has a LOD " + sub.Lod + " subsection in the " + label + " model.");
                    }
                    string warning = "material " + sub.Name + " was not used by the " + label + " model before";
                    if (!ownMaterials.Contains(sub.Name) && !this.Warnings.Contains(warning))
                    {
                        this.Warnings.Add(warning);
                    }
                }
            }
            this.Log.Add(string.Format("{0} model: {1} parts before, {2} after", label, before.Count, check.Sections.Count));
            return bytes;
        }

        private static string Hash(byte[] data)
        {
            using (SHA1 sha = SHA1.Create())
            {
                return Convert.ToBase64String(sha.ComputeHash(data));
            }
        }
    }
}
