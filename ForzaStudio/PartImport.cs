using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;

namespace ForzaStudio
{
    // Copy Part From Another Car, without UI (ST-03): parts of a source car's model files go into the same files of the
    // target car. Everything is computed first and checked; Result is filled only when every file built and passed the
    // checks, so an import that stops half way changes nothing (the caller writes Result or nothing).
    // Files are keyed like ArchiveEditorForm.CarModelFiles: "" (main), "_lod0", "_cockpit".
    public partial class PartImport
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
        // FM3 TARGET (Target holds this FM3 car's own files; Source the part's files in FM4 form, PartSource.ForMotorsport3).
        // This car's file is opened in FM3's own layout, so its parts are written back byte-identical; the copied part is
        // placed on the FM4 form of the file (TargetAsMotorsport4, computed when null) and put in FM3's layout by
        // CarbinModel.ConvertPartToMotorsport3. SourceNative: the source car's FM3 files as they are, when it is an FM3 car
        // too: the part is then copied as it is (not moved), or with only its positions rewritten (moved / LOD 1 used up
        // close): UVs, normals, damage words stay the FM3 bytes (user rule: UV1 / UV2 never touched).
        public Dictionary<string, byte[]> TargetAsMotorsport4;
        public Dictionary<string, byte[]> SourceNative;

        public Dictionary<string, byte[]> Result;     // changed target files (null until Run succeeds)
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
            if (this.Target.ContainsKey("") && CarbinFormat.Identify(this.Target[""]).Family == CarbinFamily.Motorsport2)
            {
                this.RunMotorsport2();     // an FM2 car: its own writer (PartImport.Motorsport2.cs)
                return;
            }
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
            // FM3 car: placed on the FM4 form of its files, written in its own layout (see TargetAsMotorsport4)
            bool fm3 = CarbinModel.IsMotorsport3File(this.Target[refKey]);
            Dictionary<string, byte[]> placing = this.Target;
            if (fm3)
            {
                if (this.TargetAsMotorsport4 == null)
                {
                    this.TargetAsMotorsport4 = PartSource.Motorsport3AsMotorsport4(this.Target);
                }
                placing = this.TargetAsMotorsport4;
            }
            // the reference files: the parts must exist in the source one and are placed by it (main, or the chosen file)
            CarbinModel targetMain = Open(placing[refKey], "this car's " + FileLabel(refKey) + " model (it must be editable)");
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
                    foreach (CarbinModel.Section section in OpenTarget(bytes, FileLabel(key)).Sections)
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
                        model = OpenTarget(targetBytes, FileLabel(key));
                    }
                    if (step.Replaced != null && model.Find(step.Replaced) != null)
                    {
                        model.RemoveSection(step.Replaced);
                        this.Log.Add(string.Format("{0}: part {1} removed", FileLabel(key), step.Replaced));
                    }
                    bool moveHere = this.Moves[i] != Matrix.Identity && (step.MoveIn == null || step.MoveIn.Contains(key));
                    string report = model.IsMotorsport3
                        ? this.ImportIntoMotorsport3(model, sourceModel, step, moveHere ? this.Moves[i] : Matrix.Identity, (this.OnlyFile != null) ? sourceKey : key)
                        : model.ImportSection(sourceModel, step.Part, step.NewName, false);
                    foreach (string line in report.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        this.Log.Add(FileLabel(key) + ": " + line);
                    }
                    if (this.Moves[i] != Matrix.Identity)
                    {
                        if (moveHere)
                        {
                            if (!model.IsMotorsport3)
                            {
                                model.TransformSection(step.NewName, this.Moves[i]);   // FM3: moved in FM4 form, before its conversion
                            }
                            this.Log.Add(string.Format("{0}: {1} moved (LODs {2})", FileLabel(key), step.NewName, LodText(Lods(from))));
                        }
                        else
                        {
                            this.Log.Add(string.Format("{0}: {1} kept at the source position (LODs {2}), as chosen", FileLabel(key), step.NewName, LodText(Lods(from))));
                        }
                    }
                    AddLods(sourceLods, step.NewName, from);
                    // the materials as they arrived in this file (an FM3 car: after FM3's material names)
                    foreach (CarbinModel.SubSection sub in model.Find(step.NewName).SubSections)
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
                CarbinModel check = OpenTarget(bytes, FileLabel(key) + " after the import");
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

        // This car's file: an FM3 file in its own layout (CarbinModel writes it back byte-identical), else as Open.
        private static CarbinModel OpenTarget(byte[] bytes, string what)
        {
            try
            {
                return CarbinModel.IsMotorsport3File(bytes) ? new CarbinModel(bytes, true) : new CarbinModel(bytes);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(what + ": " + exception.Message, exception);
            }
        }

        // Materials FM4 added that FM3 draws with another name: the FM4 -> FM3 port's list (CarPortToFm3.MaterialNames).
        private static readonly string[][] Motorsport3MaterialNames = { new string[] { "lights_glass", "detail_glass_clear" } };

        // One part into an FM3 car's file (model, FM3 layout). sourceModel: the source file in FM4 form, in this file's
        // LOD layout (SourceModel); move: already limited to this file (identity = not moved here).
        //  - from an FM3 car, same kind of file, not moved: the FM3 section as it is (every byte);
        //  - otherwise the FM4 form is moved, put in FM3's layout (ConvertPartToMotorsport3, the port's step) and, from an
        //    FM3 car, every vertex gets its FM3 bytes back except the position (KeepNativeVertexBytes).
        private string ImportIntoMotorsport3(CarbinModel model, CarbinModel sourceModel, Step step, Matrix move, string nativeKey)
        {
            CarbinModel native = null;
            byte[] nativeBytes;
            if (this.SourceNative != null && this.SourceNative.TryGetValue(nativeKey, out nativeBytes) && CarbinModel.IsMotorsport3File(nativeBytes))
            {
                native = new CarbinModel(nativeBytes, true);
            }
            CarbinModel.Section nativePart = (native != null) ? native.Find(step.Part) : null;
            bool sameLayout = nativePart != null && native.DrawLists.Count == model.DrawLists.Count;
            if (sameLayout && move == Matrix.Identity && CarVertices(nativePart))
            {
                return model.ImportSection(native, step.Part, step.NewName, false) + "\r\n" + step.NewName + ": FM3 part copied as it is (every byte of the source's FM3 section)";
            }
            CarbinModel carrier = new CarbinModel(sourceModel.ToBytes());
            if (move != Matrix.Identity)
            {
                carrier.TransformSection(step.Part, move);
            }
            carrier.ConvertPartToMotorsport3(step.Part, step.NewName);
            CarbinModel.Section part = carrier.Find(step.Part);
            StringBuilder notes = new StringBuilder();
            List<string> renamed = new List<string>();
            foreach (CarbinModel.SubSection sub in part.SubSections)
            {
                foreach (string[] pair in Motorsport3MaterialNames)
                {
                    if (sub.Name == pair[0])
                    {
                        sub.Name = pair[1];
                        if (!renamed.Contains(pair[0] + " -> " + pair[1]))
                        {
                            renamed.Add(pair[0] + " -> " + pair[1]);
                        }
                    }
                }
            }
            if (renamed.Count > 0)
            {
                notes.AppendFormat("\r\n{0}: FM3 material names: {1}", step.NewName, string.Join(", ", renamed.ToArray()));
            }
            if (nativePart != null)
            {
                string failed = KeepNativeVertexBytes(part, nativePart, sameLayout);
                if (failed == null)
                {
                    notes.AppendFormat("\r\n{0}: FM3 vertex data kept from the source's FM3 file, positions {1}", step.NewName, (move != Matrix.Identity) ? "rewritten (moved)" : "rewritten (LOD 1 used up close)");
                }
                else
                {
                    this.Warnings.Add(step.NewName + ": the FM3 vertex bytes could not be matched (" + failed + "): the part went through FM4 form, UVs with FM3's half precision");
                }
            }
            else
            {
                notes.AppendFormat("\r\n{0}: put in FM3's layout (FM4 -> FM3 port conversion)", step.NewName);
            }
            return model.ImportSection(carrier, step.Part, step.NewName, false) + notes.ToString();
        }

        // FM3 car-part vertices (40 bytes) in every buffer of the section.
        private static bool CarVertices(CarbinModel.Section section)
        {
            return (section.Lod.Count == 0 || section.Lod.Stride == 40) && (section.Lod0.Count == 0 || section.Lod0.Stride == 40);
        }

        // The part after ConvertPartToMotorsport3 (positions re-encoded, UVs as halves from FM4's 16-bit UVs) gets the FM3
        // source's own bytes after the position back in every vertex: UV1 / UV2, normal, the 4 S16 values, damage word,
        // the word FM4 dropped and the frame axis (a move is a translation + even scale: normals do not change). A brake
        // file's 16-byte vertices give their UV1 only. Vertex order: the same buffers when the layout is the same; up close
        // (main LOD 1 as LOD 0) the converted Lod0 = the native Lod vertices LOD 1 draws, in order (CloseUpFromLod1 keeps
        // the order). Checked: every vertex a material draws must have the same UV1 within FM3's half precision, else
        // nothing is changed and the reason is returned.
        private static string KeepNativeVertexBytes(CarbinModel.Section part, CarbinModel.Section native, bool sameLayout)
        {
            List<object[]> pairs = new List<object[]>();
            if (sameLayout)
            {
                pairs.Add(new object[] { part.Lod, native.Lod, null, false });
                pairs.Add(new object[] { part.Lod0, native.Lod0, null, true });
            }
            else
            {
                if (part.Lod.Count != 0)
                {
                    return "LOD layout";
                }
                List<int> lod1 = new List<int>();
                bool[] used = new bool[native.Lod.Count];
                foreach (CarbinModel.SubSection sub in native.SubSections)
                {
                    if (sub.Lod == 1)
                    {
                        foreach (int index in sub.Indices)
                        {
                            if (index >= 0 && index < used.Length)
                            {
                                used[index] = true;
                            }
                        }
                    }
                }
                for (int i = 0; i < used.Length; i++)
                {
                    if (used[i])
                    {
                        lod1.Add(i);
                    }
                }
                pairs.Add(new object[] { part.Lod0, native.Lod, lod1.ToArray(), true });
            }
            foreach (object[] pair in pairs)
            {
                CarbinModel.VertexBuffer to = (CarbinModel.VertexBuffer) pair[0];
                CarbinModel.VertexBuffer from = (CarbinModel.VertexBuffer) pair[1];
                int[] map = (int[]) pair[2];
                if (to.Count != ((map != null) ? map.Length : from.Count))
                {
                    return string.Format("{0} vertices, source {1}", to.Count, (map != null) ? map.Length : from.Count);
                }
                if (to.Count > 0 && (to.Stride < 12 || from.Stride < 12))
                {
                    return "vertex size " + from.Stride;
                }
                bool[] drawn = new bool[to.Count];
                foreach (CarbinModel.SubSection sub in part.SubSections)
                {
                    if ((sub.Lod == 0) == (bool) pair[3])
                    {
                        foreach (int index in sub.Indices)
                        {
                            if (index >= 0 && index < drawn.Length)
                            {
                                drawn[index] = true;
                            }
                        }
                    }
                }
                for (int i = 0; i < to.Count; i++)
                {
                    int n = (map != null) ? map[i] : i;
                    if (drawn[i] && (!SameUv(to.Data, (i * to.Stride) + 8, from.Data, (n * from.Stride) + 8) || !SameUv(to.Data, (i * to.Stride) + 10, from.Data, (n * from.Stride) + 10)))
                    {
                        return "vertex " + i + " has another UV";
                    }
                }
            }
            foreach (object[] pair in pairs)
            {
                CarbinModel.VertexBuffer to = (CarbinModel.VertexBuffer) pair[0];
                CarbinModel.VertexBuffer from = (CarbinModel.VertexBuffer) pair[1];
                int[] map = (int[]) pair[2];
                int end = (to.Stride == from.Stride) ? to.Stride : 12;
                for (int i = 0; i < to.Count; i++)
                {
                    int n = (map != null) ? map[i] : i;
                    Buffer.BlockCopy(from.Data, (n * from.Stride) + 8, to.Data, (i * to.Stride) + 8, end - 8);
                }
            }
            return null;
        }

        // Two FM3 half UV values, the converted one within the 16-bit + half rounding of the FM4 form.
        private static bool SameUv(byte[] a, int ao, byte[] b, int bo)
        {
            if (a[ao] == b[bo] && a[ao + 1] == b[bo + 1])
            {
                return true;
            }
            float x = Half(a, ao);
            float y = Half(b, bo);
            return Math.Abs(x - y) <= 0.01f + (0.002f * Math.Max(Math.Abs(x), Math.Abs(y)));
        }

        private static float Half(byte[] data, int offset)
        {
            int h = (data[offset] << 8) | data[offset + 1];
            int exponent = (h >> 10) & 0x1F;
            int mantissa = h & 0x3FF;
            double value = (exponent == 0) ? mantissa * Math.Pow(2, -24) : ((exponent == 31) ? double.MaxValue : (1 + (mantissa / 1024.0)) * Math.Pow(2, exponent - 15));
            return (float) (((h & 0x8000) != 0) ? -value : value);
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
