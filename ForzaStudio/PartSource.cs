using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ForzaStudio
{
    // ST-03: the model files of a Copy Part SOURCE car in the form of the target game, so that PartImport (which
    // works between files of one game) can take parts from another game's car.
    // Scope set by the user 2026-11-17: parts FROM FH1 / FH2 into every Motorsport, and between the Motorsports
    // (2 <-> 3, 2 <-> 4, 3 <-> 4). Done so far: FM3 -> FM4, FH1 / FH2 -> FM4, FM2 -> FM4, and every one of them plus
    // FM4 -> FM3 (below). FM2 targets are refused with a message until they are built.
    //
    // FM3 -> FM4: each file goes through CarPort.ConvertModel, the conversion the FM3 -> FM4 car port uses (header,
    // sections, 40-byte vertices, FM4 material names, _lod0 light variants in the _lod0 file), so a copied part is
    // what the same part would be in the ported car. Nothing of the source car is written.
    //
    // FM3 TARGET (user 2026-11-17, approved plan): the FM3 car's file is edited in its own layout (CarbinModel reads and
    // writes FM3 byte-identical), so its own parts never change. The copied part is prepared in FM4 form here
    // (ForMotorsport3), placed by PartImport on the FM4 form of this car's file (Motorsport3AsMotorsport4, memory
    // only), then put in FM3's layout by CarbinModel.ConvertPartToMotorsport3 (the FM4 -> FM3 port's section step).
    // From an FM3 car the FM3 bytes of every vertex are kept (PartImport.SourceNative).
    public static class PartSource
    {
        // An FM3 model file in FM4 form, the plain format conversion only (CarbinModel.ConvertMotorsport3ToMotorsport4):
        // part and material names stay FM3's, vertices in the same order. Memory only; never written to a game file.
        public static byte[] Motorsport3AsMotorsport4(byte[] data)
        {
            CarbinModel model = new CarbinModel(data, true);
            if (!model.IsMotorsport3)
            {
                return data;
            }
            model.ConvertMotorsport3ToMotorsport4();
            return model.ToBytes();
        }

        public static Dictionary<string, byte[]> Motorsport3AsMotorsport4(Dictionary<string, byte[]> files)
        {
            Dictionary<string, byte[]> converted = new Dictionary<string, byte[]>();
            foreach (KeyValuePair<string, byte[]> pair in files)
            {
                converted[pair.Key] = Motorsport3AsMotorsport4(pair.Value);
            }
            return converted;
        }

        // The source car's model files for an FM2 target (PartImport.Motorsport2.cs): an FM2 car as it is (its parts are
        // copied in FM2 form), an FM4 car as it is, an FM3 car in FM4 form with its names (plain conversion), an FH car
        // through the FH -> FM4 conversion (stripped meshes left out; its damage words are not used: SourceIsHorizon).
        public static Dictionary<string, byte[]> ForMotorsport2(Dictionary<string, byte[]> files, List<string> notes, Dictionary<string, List<string>> unknown)
        {
            byte[] main;
            if (!files.TryGetValue("", out main))
            {
                throw new InvalidOperationException("The source car has no main model.");
            }
            CarbinFamily family = CarbinFormat.Identify(main).Family;
            switch (family)
            {
                case CarbinFamily.Motorsport2:
                    if (notes != null)
                    {
                        notes.Add("FM2 -> FM2: the part's FM2 vertices are kept byte for byte; only the positions are rewritten when it is moved");
                    }
                    return files;
                case CarbinFamily.Motorsport4:
                    if (notes != null)
                    {
                        notes.Add("FM4 -> FM2: 60-byte FM2 vertices (UV0 = - UV1, normal, damage word as FM4 has it; baked lighting from the replaced part, else this car's nearest LOD 1 vertex)");
                    }
                    return files;
                case CarbinFamily.Motorsport3:
                    if (notes != null)
                    {
                        notes.Add("FM3 -> FM2: read in FM4 form, then FM2 vertices (damage word as FM3 has it; baked lighting from the replaced part, else this car's nearest LOD 1 vertex)");
                    }
                    return Motorsport3AsMotorsport4(files);
                case CarbinFamily.Horizon:
                    if (notes != null)
                    {
                        notes.Add("FH -> FM2: FH -> FM4 conversion, then FM2 vertices; damage words and baked lighting from the replaced part, else this car's nearest LOD 1 vertex (FH words are never used)");
                    }
                    return ForMotorsport4(files, null, unknown, null);
            }
            throw new NotSupportedException(GameName(family) + " .carbin: copying parts from these models is not supported.");
        }

        // The source car's model files for an FM3 target, in FM4 form (the form PartImport places and converts):
        //  - FM3 car: the plain conversion (names kept: they are FM3's already; PartImport puts the FM3 vertex bytes back);
        //  - FM4 car: as it is;
        //  - FH1 / FH2 / FM2 car: as for an FM4 target (ForMotorsport4), the damage words / FM2 rebuild taken from
        //    'targetAsMotorsport4' = this FM3 car's files in FM4 form (Motorsport3AsMotorsport4).
        public static Dictionary<string, byte[]> ForMotorsport3(Dictionary<string, byte[]> files, List<string> notes, Dictionary<string, List<string>> unknown, Dictionary<string, byte[]> targetAsMotorsport4)
        {
            byte[] main;
            if (!files.TryGetValue("", out main))
            {
                throw new InvalidOperationException("The source car has no main model.");
            }
            CarbinFamily family = CarbinFormat.Identify(main).Family;
            if (family == CarbinFamily.Motorsport4)
            {
                if (notes != null)
                {
                    notes.Add("source part converted to FM3's layout as the FM4 -> FM3 port converts it (UVs become FM3 halves)");
                }
                return files;
            }
            if (family == CarbinFamily.Motorsport3)
            {
                if (notes != null)
                {
                    notes.Add("FM3 -> FM3: the part's FM3 vertex data (UVs, normals, damage words) is kept byte for byte; only the positions are rewritten when it is moved");
                }
                return Motorsport3AsMotorsport4(files);
            }
            Dictionary<string, byte[]> result = ForMotorsport4(files, notes, unknown, targetAsMotorsport4);
            if (notes != null)
            {
                notes.Add("then put in FM3's layout as the FM4 -> FM3 port does (UVs become FM3 halves)");
            }
            return result;
        }

        public static string GameName(CarbinFamily family)
        {
            switch (family)
            {
                case CarbinFamily.Motorsport2: return "Forza Motorsport 2";
                case CarbinFamily.Motorsport3: return "Forza Motorsport 3";
                case CarbinFamily.Motorsport4: return "Forza Motorsport 4";
                case CarbinFamily.Horizon: return "Forza Horizon";
            }
            return "unknown game";
        }

        // files: keyed like ArchiveEditorForm.CarModelFiles ("" main, "_lod0", "_cockpit"). Returns the same keys
        // in FM4 form (the same arrays when the car is FM4 already).
        // notes: what the conversion did, for the import report. unknown: material no FM4 car uses -> its parts.
        public static Dictionary<string, byte[]> ForMotorsport4(Dictionary<string, byte[]> files, List<string> notes, Dictionary<string, List<string>> unknown)
        {
            return ForMotorsport4(files, notes, unknown, null);
        }

        // target: the TARGET car's model files (same keys), FM4. An FH source takes its per-vertex damage words from
        // the target's file of the same kind (BorrowDamageWords, matched by place in the section box: another car),
        // as the FH -> FM4 port takes them from its donor (user 2026-11-17: "prende sempre da donor i punti").
        // FH words are never converted (they made the GTX1 explode, 2026-11-10).
        public static Dictionary<string, byte[]> ForMotorsport4(Dictionary<string, byte[]> files, List<string> notes, Dictionary<string, List<string>> unknown, Dictionary<string, byte[]> target)
        {
            byte[] main;
            if (!files.TryGetValue("", out main))
            {
                throw new InvalidOperationException("The source car has no main model.");
            }
            CarbinFamily family = CarbinFormat.Identify(main).Family;
            if (family == CarbinFamily.Motorsport4)
            {
                return files;
            }
            if (family == CarbinFamily.Motorsport2)
            {
                return FromMotorsport2(files, notes, unknown, target);
            }
            if (family != CarbinFamily.Motorsport3 && family != CarbinFamily.Horizon)
            {
                throw new NotSupportedException(GameName(family) + " .carbin: copying parts from these models is not supported.");
            }
            Dictionary<string, byte[]> converted = new Dictionary<string, byte[]>();
            foreach (KeyValuePair<string, byte[]> pair in files)
            {
                StringBuilder report = new StringBuilder();
                byte[] data = pair.Value;
                CarbinModel words = null;
                if (family == CarbinFamily.Horizon)
                {
                    // stripped FH meshes (no index list) are not parts to copy (user 2026-11-17): left out, silently
                    data = WithoutStripped(data);
                    byte[] own;
                    if (target != null && target.TryGetValue(pair.Key, out own))
                    {
                        words = new CarbinModel(own);
                        words.DamageByShape = true;
                    }
                }
                // lod0 = the _lod0 file only, as the port calls it (the cockpit file keeps the plain light names)
                converted[pair.Key] = CarPort.ConvertModel(data, pair.Key == "_lod0", report, null, null, unknown, words);
                if (notes != null)
                {
                    notes.Add(string.Format("source {0} model converted from {1} to FM4 form ({2:N0} -> {3:N0} bytes)", PartImport.FileLabel(pair.Key), GameName(family), pair.Value.Length, converted[pair.Key].Length));
                    if (family == CarbinFamily.Horizon)
                    {
                        notes.Add(words != null ? "  damage words: from this car's " + PartImport.FileLabel(pair.Key) + " model (FH words are never used)"
                            : "  damage words: this car has no " + PartImport.FileLabel(pair.Key) + " model, the FH words stay (the part may deform badly)");
                    }
                    foreach (string line in report.ToString().Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        // the unknown-material lines of the whole car are not this import's business: the copied
                        // parts' ones become warnings (unknown)
                        if (line.TrimStart().StartsWith("materials:", StringComparison.Ordinal))
                        {
                            notes.Add("  " + line.Trim());
                        }
                    }
                }
            }
            return converted;
        }

        // FM2 -> FM4 (ST-03, 2026-11-17): the FM2 port's own model step (Fm2Source.Stage). The one FM2 .carbin (LODs 1-5)
        // is exported to OBJ (CarModelFolder.ExportLegacy) and rebuilt onto a copy of THIS car's main model as donor
        // (CarModelFolder.Prepare / Apply, any material allowed): tangent frames, baked light and damage words from the
        // nearest vertex of this car (the donor rule of the FH parts), UV1 as the OBJ has it (no fit; FM2 has no UV2, it
        // is always 0). Only the FM2 parts are kept, then CarPort.ConvertModel maps the material names as the port does.
        // Returns the main model only: an FM2 car has no _lod0 / _cockpit; Copy Part takes LOD 1 for those (PartImport).
        private static Dictionary<string, byte[]> FromMotorsport2(Dictionary<string, byte[]> files, List<string> notes, Dictionary<string, List<string>> unknown, Dictionary<string, byte[]> target)
        {
            foreach (string key in files.Keys)
            {
                if (key.Length > 0)
                {
                    throw new NotSupportedException("An FM2 car: only its main model can be copied from.");
                }
            }
            byte[] own;
            if (target == null || !target.TryGetValue("", out own))
            {
                throw new InvalidOperationException("Copying from an FM2 car needs this car's main model (the parts are rebuilt on it).");
            }
            string work = Path.Combine(Path.GetTempPath(), "ForzaStudioEVO_fm2part_" + Guid.NewGuid().ToString("N"));
            try
            {
                string objDir = Path.Combine(work, "fm2");
                int objs = CarModelFolder.ExportLegacy(files[""], "fm2.carbin", objDir, "FM2");
                List<string> fm2Parts = new List<string>();
                foreach (string lod in Directory.GetDirectories(objDir))
                {
                    foreach (string file in Directory.GetFiles(lod, "*.obj"))
                    {
                        string part = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                        if (!fm2Parts.Contains(part))
                        {
                            fm2Parts.Add(part);
                        }
                    }
                }
                Dictionary<string, CarbinModel> models = new Dictionary<string, CarbinModel>();
                models[CarModelFolder.Main] = new CarbinModel(own);
                Dictionary<string, string> folders = new Dictionary<string, string>();
                folders[CarModelFolder.Main] = objDir;
                CarModelFolder.Plan plan = CarModelFolder.Prepare(folders, models, true, true);
                if (plan.Errors.Count > 0)
                {
                    throw new InvalidOperationException("The FM2 model does not fit: " + string.Join("; ", plan.Errors.ToArray()));
                }
                StringBuilder build = new StringBuilder();
                CarModelFolder.Apply(plan, models, build);
                CarbinModel main = models[CarModelFolder.Main];
                // 2026-11-21: baked light = the FM2 vertices' own values (CarbinModelLighting.cs), not an estimate
                string light;
                try
                {
                    light = main.LightingFromMotorsport2(new Fm2CarbinModel(files[""]));
                }
                catch (Exception exception)
                {
                    light = "baked light: the FM2 values could not be read (" + exception.Message + "), the open-panel estimate stays";
                }
                foreach (CarbinModel.Section section in new List<CarbinModel.Section>(main.Sections))
                {
                    if (!fm2Parts.Contains(section.Name.ToLowerInvariant()))
                    {
                        main.RemoveSection(section.Name);      // this car's own parts: not part of the source
                    }
                }
                StringBuilder report = new StringBuilder();
                byte[] converted = CarPort.ConvertModel(main.ToBytes(), false, report, null, null, unknown);
                if (notes != null)
                {
                    notes.Add(string.Format("source main model rebuilt from Forza Motorsport 2 ({0} OBJ files, {1} parts) on this car's main model, as the FM2 port does", objs, fm2Parts.Count));
                    notes.Add("  damage words: from the nearest vertex of this car (FM2 has none of FM4's); UV1 as in FM2, UV2 0 as in FM2");
                    notes.Add("  " + light);
                    foreach (string line in report.ToString().Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (line.TrimStart().StartsWith("materials:", StringComparison.Ordinal))
                        {
                            notes.Add("  " + line.Trim());
                        }
                    }
                }
                Dictionary<string, byte[]> result = new Dictionary<string, byte[]>();
                result[""] = converted;
                return result;
            }
            finally
            {
                try
                {
                    Directory.Delete(work, true);
                }
                catch (Exception)
                {
                    // a leftover temp folder only (TempCleanup)
                }
            }
        }

        // The FH model without its stripped subsections (no index list, no FM4 form); a part left with none goes.
        // The same bytes when there are none.
        private static byte[] WithoutStripped(byte[] data)
        {
            CarbinModel model = new CarbinModel(data, true);
            bool any = false;
            foreach (CarbinModel.Section section in new List<CarbinModel.Section>(model.Sections))
            {
                List<int> stripped = new List<int>();
                for (int k = 0; k < section.SubSections.Count; k++)
                {
                    if (section.SubSections[k].Stripped != null)
                    {
                        stripped.Add(k);
                    }
                }
                if (stripped.Count > 0)
                {
                    model.RemoveSubSections(section.Name, stripped);
                    any = true;
                }
            }
            return any ? model.ToBytes() : data;
        }

        // One line per copied part that carries a material no FM4 car uses (unknown, from ForMotorsport4): such a
        // material may render black in the game. The fix is the user's (Rename Material in the tree): no name is invented.
        public static List<string> UnknownMaterialWarnings(Dictionary<string, byte[]> source, List<PartImport.Step> steps, Dictionary<string, List<string>> unknown)
        {
            List<string> warnings = new List<string>();
            if (unknown == null || unknown.Count == 0)
            {
                return warnings;
            }
            foreach (PartImport.Step step in steps)
            {
                List<string> materials = new List<string>();
                foreach (KeyValuePair<string, List<string>> pair in unknown)
                {
                    if (pair.Value.Contains(step.Part))
                    {
                        materials.Add(pair.Key);
                    }
                }
                if (materials.Count > 0)
                {
                    materials.Sort(StringComparer.OrdinalIgnoreCase);
                    warnings.Add(string.Format("{0}: no FM4 car uses the material{1} {2}: it may render black (Rename Material in the tree)", step.NewName ?? step.Part, materials.Count == 1 ? "" : "s", string.Join(", ", materials.ToArray())));
                }
            }
            return warnings;
        }
    }
}
