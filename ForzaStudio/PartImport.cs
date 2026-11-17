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
        // NO UV region remap (user rule 2026-11-17: "UV1 e UV2 non devono essere toccati"). Until then the copy took,
        // by default, the UV offset / scale of this car's material of the same name ("Use this car's texture regions"),
        // which squeezed the part's UV1 / UV2 into another layout. A copied part keeps its UVs exactly; harness pi3
        // checks every vertex.
        // The one model file of THIS car the parts go into: the file open in the viewer ("" main, "_lod0", "_cockpit").
        // User 2026-11-17: "mi sta bene la regola import solo su lod aperto". The replaced part and the placement are
        // that file's. null = every file that has the part, placed by the main model (the behaviour before 2026-11-17).
        public string OnlyFile;
        // The source car's file the parts come from, chosen by the user (user 2026-11-17: "mi fai scegliere il file
        // main, lod0 o cockpit, per assurdo anche freni e dischi"): any key of Source. null = the same kind as OnlyFile.
        // Main -> lod0 / cockpit: the part's LOD 1 becomes its close-up geometry (CarbinModel.CloseUpFromLod1, as
        // Lod0Fill); one-LOD files (lod0, cockpit, brakes) -> lod0 / cockpit: as they are; one-LOD -> main: refused
        // (the main model needs LODs 1-5).
        public string SourceFile;

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
            string refKey = this.OnlyFile ?? "";
            string sourceKey = (this.OnlyFile != null) ? (this.SourceFile ?? this.OnlyFile) : "";
            if ((this.OnlyFile == null && !this.Source.ContainsKey("")) || !this.Target.ContainsKey(""))
            {
                throw new InvalidOperationException("Both cars need their main model (.carbin next to the _lod0.carbin).");
            }
            if (!this.Source.ContainsKey(sourceKey))
            {
                throw new InvalidOperationException("The source car has no " + FileLabel(sourceKey) + " model.");
            }
            if (!this.Target.ContainsKey(refKey))
            {
                throw new InvalidOperationException("This car has no " + FileLabel(refKey) + " model to copy into.");
            }
            if (this.Steps.Count == 0)
            {
                throw new InvalidOperationException("No part chosen.");
            }
            Dictionary<string, string> sourceHash = Hashes(this.Source);
            // the reference files: the parts must exist in the source one and are placed by it (main, or the chosen file)
            CarbinModel targetMain = Open(this.Target[refKey], "this car's " + FileLabel(refKey) + " model (it must be editable)");
            CarbinModel sourceMain = this.SourceModel(sourceKey, refKey, targetMain);

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
                    throw new InvalidOperationException("The source car has no part " + step.Part + " in its " + FileLabel(sourceKey) + " model"
                        + ((sourceKey.Length == 0 && refKey.Length > 0) ? " with a LOD 1 to use up close." : "."));
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
                if ((this.OnlyFile != null && key != this.OnlyFile) || !this.Target.TryGetValue(key, out targetBytes))
                {
                    continue;
                }
                CarbinModel sourceModel;
                if (this.OnlyFile != null)
                {
                    sourceModel = sourceMain;     // the chosen source file, in this file's LOD layout (SourceModel)
                }
                else
                {
                    byte[] sourceBytes;
                    if (!this.Source.TryGetValue(key, out sourceBytes))
                    {
                        this.Warnings.Add(string.Format("{0}: the source car has no such file, nothing copied there", FileLabel(key)));
                        continue;
                    }
                    sourceModel = Open(sourceBytes, "source " + FileLabel(key));
                }
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
                    string report = model.ImportSection(sourceModel, step.Part, step.NewName, false);
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
                this.Warnings.Add("material " + material + " is not used by this car: this car has no texture laid out for it (the part's UVs are kept as they are)");
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

        // The chosen source file in the LOD layout of this car's target file (see SourceFile).
        private CarbinModel SourceModel(string sourceKey, string targetKey, CarbinModel target)
        {
            CarbinModel source = Open(this.Source[sourceKey], "source " + FileLabel(sourceKey) + " model");
            int sourceLods = source.DrawLists.Count / 2;
            int targetLods = target.DrawLists.Count / 2;
            if (sourceLods == targetLods)
            {
                return source;      // same layout: main -> main, lod0 / cockpit / brake -> lod0 / cockpit
            }
            if (targetLods == 1 && sourceLods > 1)
            {
                // a main-model part up close: its LOD 1 as LOD 0, materials named as in a close-up file (as Lod0Fill)
                List<string> parts = new List<string>();
                foreach (Step step in this.Steps)
                {
                    parts.Add(step.Part);
                }
                CarbinModel closeUp = source.CloseUpFromLod1(target.Head, delegate(string name) { return parts.Contains(name); });
                foreach (CarbinModel.Section section in closeUp.Sections)
                {
                    List<string> renamed = Lod0Fill.CloseUpMaterials(section, (targetKey == "_cockpit") ? "_cockpit" : "_lod0");
                    this.Log.Add(string.Format("{0}: LOD 1 of the source's main model used as its {1} geometry{2}", section.Name, FileLabel(targetKey),
                        (renamed.Count > 0) ? " (materials " + string.Join(", ", renamed.ToArray()) + ")" : ""));
                }
                return closeUp;
            }
            throw new InvalidOperationException(string.Format("A part of the source's {0} model has LOD 0 only; this car's {1} model needs LODs 1 to {2}. Open this car's lod0 or cockpit model, or copy from the source's main model.",
                FileLabel(sourceKey), FileLabel(targetKey), targetLods));
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
