using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Microsoft.Xna.Framework;

namespace ForzaStudio
{
    // Copy Part From Another Car, without UI (ST-03): parts of a source car's model files go into the same files of the
    // target car. Everything is computed first and checked; Result is filled only when every file built and passed the
    // checks, so an import that stops half way changes nothing (the caller writes Result or nothing).
    // Files are keyed like ArchiveEditorForm.CarModelFiles: "" (main), "_lod0", "_cockpit".
    public class PartImport
    {
        public class Step
        {
            public string Part;          // in the source
            public string NewName;       // in the target
            public string Replaced;      // target part removed first (null = added)
            public bool Align = true;    // at the replaced part's center
            public bool Fit;             // also scaled (same proportions) to the replaced part's size
            public List<string> MoveIn;  // files whose copy is moved / scaled ("" main, "_lod0", "_cockpit"); null = all.
                                         // The operator is ASKED (user rule ST-03): a part may sit right in one LOD file only.
        }

        public Dictionary<string, byte[]> Source = new Dictionary<string, byte[]>();
        public Dictionary<string, byte[]> Target = new Dictionary<string, byte[]>();
        public List<Step> Steps = new List<Step>();
        public bool UseOwnRegions = true;

        public Dictionary<string, byte[]> Result;      // changed target files (null until Run succeeds)
        public List<string> Log = new List<string>();  // copied resources and changed references
        public List<string> Warnings = new List<string>();
        public List<Matrix> Moves = new List<Matrix>();

        private static readonly string[] Order = { "", "_lod0", "_cockpit" };

        public static string FileLabel(string key)
        {
            return (key.Length == 0) ? "main" : key.TrimStart('_');
        }

        public void Run()
        {
            this.Result = null;
            this.Log.Clear();
            this.Warnings.Clear();
            this.Moves.Clear();
            if (!this.Source.ContainsKey("") || !this.Target.ContainsKey(""))
            {
                throw new InvalidOperationException("Both cars need their main model (.carbin next to the _lod0.carbin).");
            }
            if (this.Steps.Count == 0)
            {
                throw new InvalidOperationException("No part chosen.");
            }
            Dictionary<string, string> sourceHash = Hashes(this.Source);
            CarbinModel sourceMain = Open(this.Source[""], "source main model");
            CarbinModel targetMain = Open(this.Target[""], "this car's main model (it must be editable)");

            // Plan checks: the parts exist, names stay unique.
            List<string> targetNames = new List<string>();
            foreach (CarbinModel.Section section in targetMain.Sections)
            {
                targetNames.Add(section.Name);
            }
            List<string> newNames = new List<string>();
            foreach (Step step in this.Steps)
            {
                if (sourceMain.Find(step.Part) == null)
                {
                    throw new InvalidOperationException("The source car has no part " + step.Part + " in its main model.");
                }
                if (string.IsNullOrEmpty(step.NewName))
                {
                    step.NewName = step.Part;
                }
                if (step.Replaced != null && !targetNames.Contains(step.Replaced))
                {
                    throw new InvalidOperationException("This car has no part " + step.Replaced + " to replace.");
                }
                if (step.NewName != step.Replaced && targetNames.Contains(step.NewName))
                {
                    throw new InvalidOperationException("This car already has a part named " + step.NewName + ": replace it or use another name.");
                }
                if (newNames.Contains(step.NewName))
                {
                    throw new InvalidOperationException("Two parts would be named " + step.NewName + " in this car.");
                }
                newNames.Add(step.NewName);
            }

            // One placement per part for every file (main model bounds).
            foreach (Step step in this.Steps)
            {
                Matrix move = Matrix.Identity;
                CarbinModel.Section from = sourceMain.Find(step.Part);
                CarbinModel.Section to = (step.Replaced != null) ? targetMain.Find(step.Replaced) : null;
                if (to != null && (step.Align || step.Fit))
                {
                    Vector3 fromCenter = Center(from);
                    Vector3 toCenter = Center(to);
                    float scale = 1f;
                    if (step.Fit)
                    {
                        float fromSize = Longest(from);
                        float toSize = Longest(to);
                        if (fromSize > 1e-6f && toSize > 1e-6f)
                        {
                            scale = toSize / fromSize;
                        }
                    }
                    move = Matrix.CreateTranslation(-fromCenter) * Matrix.CreateScale(scale) * Matrix.CreateTranslation(toCenter);
                    this.Log.Add(string.Format("{0}: moved {1:0.000} {2:0.000} {3:0.000} m (center of {4}){5}", step.NewName, toCenter.X - fromCenter.X, toCenter.Y - fromCenter.Y, toCenter.Z - fromCenter.Z, step.Replaced, (scale != 1f) ? string.Format(", scale {0:0.000}", scale) : ""));
                }
                this.Moves.Add(move);
            }

            // Materials this car already uses (every file): others keep the source shader / region.
            List<string> carMaterials = new List<string>();
            foreach (string key in Order)
            {
                byte[] bytes;
                if (this.Target.TryGetValue(key, out bytes))
                {
                    foreach (CarbinModel.Section section in Open(bytes, FileLabel(key)).Sections)
                    {
                        foreach (CarbinModel.SubSection sub in section.SubSections)
                        {
                            if (!carMaterials.Contains(sub.Name))
                            {
                                carMaterials.Add(sub.Name);
                            }
                        }
                    }
                }
            }

            Dictionary<string, byte[]> result = new Dictionary<string, byte[]>();
            Dictionary<string, List<uint>> sourceLods = new Dictionary<string, List<uint>>();
            Dictionary<string, List<uint>> resultLods = new Dictionary<string, List<uint>>();
            List<string> missingMaterials = new List<string>();
            foreach (string key in Order)
            {
                byte[] targetBytes;
                if (!this.Target.TryGetValue(key, out targetBytes))
                {
                    continue;
                }
                byte[] sourceBytes;
                if (!this.Source.TryGetValue(key, out sourceBytes))
                {
                    this.Warnings.Add(string.Format("{0}: the source car has no such file, nothing copied there", FileLabel(key)));
                    continue;
                }
                CarbinModel sourceModel = Open(sourceBytes, "source " + FileLabel(key));
                CarbinModel model = null;
                for (int i = 0; i < this.Steps.Count; i++)
                {
                    Step step = this.Steps[i];
                    CarbinModel.Section from = sourceModel.Find(step.Part);
                    if (from == null)
                    {
                        continue;
                    }
                    if (model == null)
                    {
                        model = Open(targetBytes, FileLabel(key));
                    }
                    if (step.Replaced != null && model.Find(step.Replaced) != null)
                    {
                        model.RemoveSection(step.Replaced);
                        this.Log.Add(string.Format("{0}: part {1} removed", FileLabel(key), step.Replaced));
                    }
                    string report = model.ImportSection(sourceModel, step.Part, step.NewName, this.UseOwnRegions);
                    foreach (string line in report.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        this.Log.Add(FileLabel(key) + ": " + line);
                    }
                    if (this.Moves[i] != Matrix.Identity)
                    {
                        if (step.MoveIn == null || step.MoveIn.Contains(key))
                        {
                            model.TransformSection(step.NewName, this.Moves[i]);
                            this.Log.Add(string.Format("{0}: {1} moved (LODs {2})", FileLabel(key), step.NewName, LodText(Lods(from))));
                        }
                        else
                        {
                            this.Log.Add(string.Format("{0}: {1} kept at the source position (LODs {2}), as chosen", FileLabel(key), step.NewName, LodText(Lods(from))));
                        }
                    }
                    AddLods(sourceLods, step.NewName, from);
                    foreach (CarbinModel.SubSection sub in from.SubSections)
                    {
                        if (!carMaterials.Contains(sub.Name) && !missingMaterials.Contains(sub.Name))
                        {
                            missingMaterials.Add(sub.Name);
                        }
                    }
                }
                if (model == null)
                {
                    continue;
                }
                byte[] bytes = model.ToBytes();

                // Checks on the written file, read back.
                CarbinModel check = Open(bytes, FileLabel(key) + " after the import");
                check.CheckDrawLists();
                List<string> seen = new List<string>();
                foreach (CarbinModel.Section section in check.Sections)
                {
                    if (seen.Contains(section.Name))
                    {
                        throw new InvalidOperationException(FileLabel(key) + ": two parts named " + section.Name + " after the import.");
                    }
                    seen.Add(section.Name);
                }
                foreach (Step step in this.Steps)
                {
                    CarbinModel.Section from = sourceModel.Find(step.Part);
                    if (from == null)
                    {
                        continue;
                    }
                    CarbinModel.Section got = check.Find(step.NewName);
                    if (got == null)
                    {
                        throw new InvalidOperationException(FileLabel(key) + ": " + step.NewName + " missing after the import.");
                    }
                    if (got.SubSections.Count != from.SubSections.Count || got.Lod.Count != from.Lod.Count || got.Lod0.Count != from.Lod0.Count)
                    {
                        throw new InvalidOperationException(string.Format("{0}: {1} differs from the source part ({2}/{3}/{4} materials/vertices, source {5}/{6}/{7}).", FileLabel(key), step.NewName, got.SubSections.Count, got.Lod.Count, got.Lod0.Count, from.SubSections.Count, from.Lod.Count, from.Lod0.Count));
                    }
                    if (step.Replaced != null && step.Replaced != step.NewName && check.Find(step.Replaced) != null)
                    {
                        throw new InvalidOperationException(FileLabel(key) + ": " + step.Replaced + " still there after the import.");
                    }
                    AddLods(resultLods, step.NewName, got);
                }
                result[key] = bytes;
            }

            // LOD coverage: every LOD the source part draws, over all files, is drawn in this car too.
            foreach (Step step in this.Steps)
            {
                List<uint> want;
                List<uint> got;
                sourceLods.TryGetValue(step.NewName, out want);
                resultLods.TryGetValue(step.NewName, out got);
                want = want ?? new List<uint>();
                got = got ?? new List<uint>();
                foreach (uint lod in want)
                {
                    if (!got.Contains(lod))
                    {
                        throw new InvalidOperationException(string.Format("{0}: LOD{1} of the source part is missing in this car.", step.NewName, lod));
                    }
                }
                this.Log.Add(string.Format("{0}: LODs {1}{2}", step.NewName, LodText(got), (got.Count < 6) ? " (the source part has only these)" : ""));
            }
            foreach (string material in missingMaterials)
            {
                this.Warnings.Add("material " + material + " is not used by this car: the part keeps the source car's region of it (texture may look wrong)");
            }
            if (result.Count == 0)
            {
                throw new InvalidOperationException("Nothing to copy: the source files lack these parts.");
            }
            Dictionary<string, string> after = Hashes(this.Source);
            foreach (KeyValuePair<string, string> pair in sourceHash)
            {
                if (after[pair.Key] != pair.Value)
                {
                    throw new InvalidOperationException("Internal error: the source " + FileLabel(pair.Key) + " changed.");
                }
            }
            this.Result = result;
        }

        private static CarbinModel Open(byte[] bytes, string what)
        {
            try
            {
                return new CarbinModel(bytes);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(what + ": " + exception.Message, exception);
            }
        }

        private static Vector3 Center(CarbinModel.Section s)
        {
            return new Vector3(s.Bounds[0] + ((s.Bounds[3] + s.Bounds[6]) / 2f), s.Bounds[1] + ((s.Bounds[4] + s.Bounds[7]) / 2f), s.Bounds[2] + ((s.Bounds[5] + s.Bounds[8]) / 2f));
        }

        private static float Longest(CarbinModel.Section s)
        {
            return Math.Max(s.Bounds[6] - s.Bounds[3], Math.Max(s.Bounds[7] - s.Bounds[4], s.Bounds[8] - s.Bounds[5]));
        }

        private static void AddLods(Dictionary<string, List<uint>> map, string name, CarbinModel.Section section)
        {
            List<uint> list;
            if (!map.TryGetValue(name, out list))
            {
                list = new List<uint>();
                map[name] = list;
            }
            foreach (CarbinModel.SubSection sub in section.SubSections)
            {
                if (!list.Contains(sub.Lod))
                {
                    list.Add(sub.Lod);
                }
            }
            list.Sort();
        }

        public static List<uint> Lods(CarbinModel.Section section)
        {
            Dictionary<string, List<uint>> one = new Dictionary<string, List<uint>>();
            AddLods(one, "", section);
            return one[""];
        }

        // LODs a part draws in each model file of a car (for the "move on which LODs" question).
        public static Dictionary<string, List<uint>> PartLods(Dictionary<string, byte[]> files, string part)
        {
            Dictionary<string, List<uint>> map = new Dictionary<string, List<uint>>();
            foreach (string key in Order)
            {
                byte[] bytes;
                if (files.TryGetValue(key, out bytes))
                {
                    CarbinModel.Section section = Open(bytes, FileLabel(key)).Find(part);
                    if (section != null)
                    {
                        map[key] = Lods(section);
                    }
                }
            }
            return map;
        }

        public static string LodText(List<uint> lods)
        {
            List<string> parts = new List<string>();
            foreach (uint lod in lods)
            {
                parts.Add(lod.ToString());
            }
            return string.Join(",", parts.ToArray());
        }

        private static Dictionary<string, string> Hashes(Dictionary<string, byte[]> files)
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            using (SHA1 sha = SHA1.Create())
            {
                foreach (KeyValuePair<string, byte[]> pair in files)
                {
                    map[pair.Key] = Convert.ToBase64String(sha.ComputeHash(pair.Value));
                }
            }
            return map;
        }
    }
}
