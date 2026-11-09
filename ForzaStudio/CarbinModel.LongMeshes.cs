using System;
using System.Collections.Generic;
using System.IO;

namespace ForzaStudio
{
    // FM4 does not draw the end of a very long subsection (CCarMesh) index list: the last strips are left out,
    // so small isolated patches of the part are holes in game (the editor and the source game draw them).
    // Found 2026-11-09 on the FH2 Corvette C7 port: lod0 body/body has 91660 indices and its last strips
    // (right door at mid height, right sill) were the holes. The largest subsection of FM4's own cars has
    // 78657 indices (toy_suprarz_98_lod0 bumperRb/plastic2), the next 63935.
    //
    // A longer one is cut by HEIGHT into horizontal bands (user 2026-11-09: a defined scheme, not faces at random),
    // as FM4's 240SX does with body (top) and body_2 (the lower body): each triangle goes to the band of its centre,
    // the bands hold about the same number of triangles, and the strips are broken at the band limits with their
    // winding kept. `body` keeps its name on the top band and the lower ones become body_2, body_3 (FM4 materials
    // with the same shader; a name the part already uses at that LOD is skipped); other materials keep their name.
    // The new subsections go at the end of the part and right after the original in the same draw lists.
    // A car's shadersettings.xml entry follows (body -> body_2) through ForzaZip / FollowSplitOnDisk.
    public partial class CarbinModel
    {
        public const int MaxGameSubSectionIndices = 78657;
        public const int SplitPieceIndices = 60000;

        // The names a split part of these materials takes, band by band from the top.
        private static readonly string[][] SplitNames = { new string[] { "body", "body_2", "body_3" } };

        // Returns "part.material: n -> pieces" for each subsection cut. FM4 car and wheel models only;
        // game files are never above the limit, so they are left byte-identical.
        public List<string> SplitLongSubSections()
        {
            List<string[]> renamed;
            return this.SplitLongSubSections(out renamed);
        }

        // renamed: material pairs (original, new name of a lower band) for the shadersettings.xml entries.
        public List<string> SplitLongSubSections(out List<string[]> renamed)
        {
            List<string> report = new List<string>();
            renamed = new List<string[]>();
            if (this.motorsport3 || this.IsHorizon || (this.Type != 2 && this.Type != 3))
            {
                return report;
            }
            for (int s = 0; s < this.Sections.Count; s++)
            {
                Section section = this.Sections[s];
                int original = section.SubSections.Count;
                for (int k = 0; k < original; k++)
                {
                    SubSection sub = section.SubSections[k];
                    if (sub.Stripped != null || sub.Indices.Count <= MaxGameSubSectionIndices)
                    {
                        continue;
                    }
                    double[] positions = Positions(section, (sub.Lod == 0) ? section.Lod0 : section.Lod);
                    List<List<int>> bands = null;
                    double[] limits = null;
                    for (int count = 2; count <= 12; count++)
                    {
                        bands = CutByHeight(sub, positions, count, out limits);
                        if (bands.TrueForAll(delegate(List<int> band) { return band.Count <= SplitPieceIndices; }))
                        {
                            break;
                        }
                    }
                    bands.RemoveAll(delegate(List<int> band) { return band.Count == 0; });
                    if (bands.Count < 2)
                    {
                        continue;
                    }
                    List<string> names = this.BandNames(section, sub, bands.Count);
                    int before = sub.Indices.Count;
                    sub.Indices = bands[0];
                    uint previous = (uint) ((s << 16) | k);
                    for (int b = 1; b < bands.Count; b++)
                    {
                        SubSection piece = sub.Copy();
                        piece.Indices = bands[b];
                        piece.Name = names[b];
                        section.SubSections.Add(piece);
                        uint added = (uint) ((s << 16) | (section.SubSections.Count - 1));
                        this.InsertDrawListEntryAfter(previous, added);
                        previous = added;
                        if (piece.Name != sub.Name && !renamed.Exists(delegate(string[] p) { return p[0] == sub.Name && p[1] == piece.Name; }))
                        {
                            renamed.Add(new string[] { sub.Name, piece.Name });
                        }
                    }
                    List<string> heights = new List<string>();
                    foreach (double limit in limits)
                    {
                        heights.Add(limit.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    report.Add(string.Format("{0}.{1} (LOD {2}): {3} indices -> {4} (cut at height {5} m)", section.Name, sub.Name, sub.Lod, before,
                        string.Join(" + ", names.GetRange(0, bands.Count).ToArray()), string.Join(", ", heights.ToArray())));
                }
            }
            return report;
        }

        // Band b's material: body / body_2 / body_3 for `body` (names the part already has at this LOD skipped),
        // the original name for every other material and when the list runs out.
        private List<string> BandNames(Section section, SubSection sub, int count)
        {
            List<string> names = new List<string>();
            names.Add(sub.Name);
            List<string> free = new List<string>();
            foreach (string[] list in SplitNames)
            {
                if (list[0] != sub.Name)
                {
                    continue;
                }
                for (int i = 1; i < list.Length; i++)
                {
                    string candidate = list[i];
                    if (!section.SubSections.Exists(delegate(SubSection other) { return other.Lod == sub.Lod && other.Name == candidate; }))
                    {
                        free.Add(candidate);
                    }
                }
            }
            for (int b = 1; b < count; b++)
            {
                names.Add((b - 1 < free.Count) ? free[b - 1] : sub.Name);
            }
            return names;
        }

        // The subsection's triangles in 'count' horizontal bands, top first, each with about the same number of
        // triangles (by the height of their centre). Each band is a strip list: runs of consecutive triangles of
        // one strip that fall in the band, a run starting at an odd triangle getting its first index twice so the
        // triangles keep their winding; runs joined by the file's restart marker. limits: the cut heights.
        private static List<List<int>> CutByHeight(SubSection sub, double[] positions, int count, out double[] limits)
        {
            int restart = Restart(sub);
            List<List<int>> strips = new List<List<int>>();
            List<int> current = new List<int>();
            foreach (int index in sub.Indices)
            {
                if (IsRestart(sub, index))
                {
                    if (current.Count > 0)
                    {
                        strips.Add(current);
                        current = new List<int>();
                    }
                    continue;
                }
                current.Add(index);
            }
            if (current.Count > 0)
            {
                strips.Add(current);
            }

            // Height of each real triangle's centre, for the band limits.
            List<double> heights = new List<double>();
            foreach (List<int> strip in strips)
            {
                for (int i = 0; i + 2 < strip.Count; i++)
                {
                    if (!IsDegenerate(strip, i))
                    {
                        heights.Add(CentreHeight(positions, strip, i));
                    }
                }
            }
            heights.Sort();
            limits = new double[count - 1];
            for (int b = 1; b < count; b++)
            {
                // band 0 is the top: the limits go down from the top
                limits[b - 1] = (heights.Count == 0) ? 0 : heights[Math.Max(0, Math.Min(heights.Count - 1, (heights.Count * (count - b)) / count))];
            }

            List<List<int>> bands = new List<List<int>>();
            for (int b = 0; b < count; b++)
            {
                bands.Add(new List<int>());
            }
            foreach (List<int> strip in strips)
            {
                int runStart = -1;
                int runBand = -1;
                int lastBand = -1;
                for (int i = 0; i + 2 < strip.Count; i++)
                {
                    // a degenerate triangle (a strip join) stays with the run it is in
                    int band = IsDegenerate(strip, i) ? lastBand : BandOf(CentreHeight(positions, strip, i), limits);
                    if (band < 0)
                    {
                        continue;   // degenerate triangles before the first real one
                    }
                    if (runStart < 0)
                    {
                        runStart = i;
                        runBand = band;
                    }
                    else if (band != runBand)
                    {
                        EmitRun(bands[runBand], strip, runStart, i - 1, restart);
                        runStart = i;
                        runBand = band;
                    }
                    lastBand = band;
                }
                if (runStart >= 0)
                {
                    EmitRun(bands[runBand], strip, runStart, strip.Count - 3, restart);
                }
            }
            return bands;
        }

        private static bool IsDegenerate(List<int> strip, int i)
        {
            return strip[i] == strip[i + 1] || strip[i + 1] == strip[i + 2] || strip[i] == strip[i + 2];
        }

        private static double CentreHeight(double[] positions, List<int> strip, int i)
        {
            return (positions[(strip[i] * 3) + 1] + positions[(strip[i + 1] * 3) + 1] + positions[(strip[i + 2] * 3) + 1]) / 3.0;
        }

        // 0 = top band: above the first limit.
        private static int BandOf(double height, double[] limits)
        {
            int band = 0;
            while (band < limits.Length && height < limits[band])
            {
                band++;
            }
            return band;
        }

        // Triangles first..last of a strip (triangle i = indices i, i+1, i+2) as a new strip of the band.
        private static void EmitRun(List<int> band, List<int> strip, int first, int last, int restart)
        {
            bool real = false;
            for (int i = first; i <= last && !real; i++)
            {
                real = !IsDegenerate(strip, i);
            }
            if (!real)
            {
                return;
            }
            if (band.Count > 0)
            {
                band.Add(restart);
            }
            if (first % 2 == 1)
            {
                band.Add(strip[first]);   // keeps the parity: the triangles after it wind as in the original strip
            }
            for (int i = first; i <= last + 2; i++)
            {
                band.Add(strip[i]);
            }
        }

        // Puts 'added' right after every occurrence of 'existing' in the draw lists.
        private void InsertDrawListEntryAfter(uint existing, uint added)
        {
            for (int l = 0; l < this.DrawLists.Count; l++)
            {
                List<uint> list = new List<uint>(this.DrawLists[l]);
                int at = list.IndexOf(existing);
                if (at < 0)
                {
                    continue;
                }
                list.Insert(at + 1, added);
                this.DrawLists[l] = list.ToArray();
            }
        }

        // shadersettings.xml after a split renamed lower bands (material pairs from SplitLongSubSections): each new
        // name gets a copy of its original's entry when the file has one and the new name has none, so the split
        // part looks the same (FM4's own R10 has <body> and uses body_2 without an entry: only split pieces get it).
        // Null when nothing changes.
        public static byte[] FollowSplit(byte[] shaderSettings, string carbinName, List<string[]> renamed)
        {
            if (shaderSettings == null || renamed == null || renamed.Count == 0)
            {
                return null;
            }
            ShaderSettings settings;
            try
            {
                settings = ShaderSettings.Parse(shaderSettings);
            }
            catch (Exception)
            {
                return null;
            }
            bool changed = false;
            foreach (string[] pair in renamed)
            {
                ShaderSettings.Entry from = settings.Find(ShaderSettings.TechniqueName(pair[0], carbinName));
                string to = ShaderSettings.TechniqueName(pair[1], carbinName);
                if (from != null && settings.Find(to) == null)
                {
                    settings.Entries.Insert(settings.Entries.IndexOf(from) + 1, from.Copy(to));
                    changed = true;
                }
            }
            return changed ? settings.ToBytes() : null;
        }

        // ForGame for a file about to be written straight to disk (port trial and game folders, DLC loose files,
        // DLC Fixer): also brings the shadersettings.xml next to it along with a split.
        public static byte[] ForGameOnDisk(string path, byte[] data)
        {
            List<string[]> renamed;
            data = ForGame(path, data, out renamed);
            FollowSplitOnDisk(path, renamed);
            return data;
        }

        // The same for a .carbin written straight to disk: the shadersettings.xml next to it.
        public static void FollowSplitOnDisk(string carbinPath, List<string[]> renamed)
        {
            if (renamed == null || renamed.Count == 0)
            {
                return;
            }
            string path = Path.Combine(Path.GetDirectoryName(carbinPath), "shadersettings.xml");
            if (!File.Exists(path))
            {
                return;
            }
            byte[] data = FollowSplit(File.ReadAllBytes(path), Path.GetFileName(carbinPath), renamed);
            if (data != null)
            {
                File.WriteAllBytes(path, data);
            }
        }
    }
}
