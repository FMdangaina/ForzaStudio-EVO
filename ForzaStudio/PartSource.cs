using System;
using System.Collections.Generic;
using System.Text;

namespace ForzaStudio
{
    // ST-03: the model files of a Copy Part SOURCE car in the form of the target game, so that PartImport (which
    // works between files of one game) can take parts from another game's car.
    // Scope set by the user 2026-11-17: parts FROM FH1 / FH2 into every Motorsport, and between the Motorsports
    // (2 <-> 3, 2 <-> 4, 3 <-> 4). Done so far: FM3 -> FM4, FH1 / FH2 -> FM4. The others are refused with a message
    // until they are built.
    //
    // FM3 -> FM4: each file goes through CarPort.ConvertModel, the conversion the FM3 -> FM4 car port uses (header,
    // sections, 40-byte vertices, FM4 material names, _lod0 light variants in the _lod0 file), so a copied part is
    // what the same part would be in the ported car. Nothing of the source car is written.
    public static class PartSource
    {
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
            if (family != CarbinFamily.Motorsport3 && family != CarbinFamily.Horizon)
            {
                // FM2: next ST-03 step (the message is the one the Copy Part dialog always gave)
                throw new NotSupportedException(GameName(family) + " .carbin: it can be read for study, but copying parts from "
                    + (family == CarbinFamily.Motorsport2 ? "FM2" : "these") + " models is not supported.");
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
