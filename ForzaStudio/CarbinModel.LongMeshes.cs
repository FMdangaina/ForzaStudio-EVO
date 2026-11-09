using System;
using System.Collections.Generic;

namespace ForzaStudio
{
    // FM4 does not draw the end of a very long subsection (CCarMesh) index list: the last strips are left out,
    // so small isolated patches of the part are holes in game (the editor and the source game draw them).
    // Found 2026-11-09 on the FH2 Corvette C7 port: lod0 body/body has 91660 indices and its last strips
    // (right door at mid height, right sill) were the holes. The largest subsection of FM4's own cars has
    // 78657 indices (toy_suprarz_98_lod0 bumperRb/plastic2), the next 63935.
    // A longer one is cut at its strip restarts into subsections of the same material, each at most
    // SplitPieceIndices long; the new pieces go at the end of the part and right after the original in the
    // same draw lists (a material twice in one part and LOD: GT6 wheel packs do it and draw fine).
    public partial class CarbinModel
    {
        public const int MaxGameSubSectionIndices = 78657;
        public const int SplitPieceIndices = 60000;

        // Returns "part.material: n -> pieces" for each subsection cut. FM4 car and wheel models only;
        // game files are never above the limit, so they are left byte-identical.
        public List<string> SplitLongSubSections()
        {
            List<string> report = new List<string>();
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
                    List<List<int>> pieces = CutAtRestarts(sub);
                    if (pieces.Count < 2)
                    {
                        continue;
                    }
                    int before = sub.Indices.Count;
                    sub.Indices = pieces[0];
                    uint entry = (uint) ((s << 16) | k);
                    uint previous = entry;
                    for (int p = 1; p < pieces.Count; p++)
                    {
                        SubSection piece = sub.Copy();
                        piece.Indices = pieces[p];
                        section.SubSections.Add(piece);
                        uint added = (uint) ((s << 16) | (section.SubSections.Count - 1));
                        this.InsertDrawListEntryAfter(previous, added);
                        previous = added;
                    }
                    report.Add(string.Format("{0}.{1} (LOD {2}): {3} indices -> {4} subsections", section.Name, sub.Name, sub.Lod, before, pieces.Count));
                }
            }
            return report;
        }

        // The index list cut between strips into pieces of at most SplitPieceIndices (a single longer strip
        // stays whole in its own piece); strips inside a piece are joined by the file's restart marker.
        private static List<List<int>> CutAtRestarts(SubSection sub)
        {
            int restart = Restart(sub);
            List<List<int>> strips = new List<List<int>>();
            List<int> strip = new List<int>();
            foreach (int index in sub.Indices)
            {
                if (IsRestart(sub, index))
                {
                    if (strip.Count > 0)
                    {
                        strips.Add(strip);
                        strip = new List<int>();
                    }
                    continue;
                }
                strip.Add(index);
            }
            if (strip.Count > 0)
            {
                strips.Add(strip);
            }
            List<List<int>> pieces = new List<List<int>>();
            List<int> piece = new List<int>();
            foreach (List<int> one in strips)
            {
                if (piece.Count > 0 && piece.Count + 1 + one.Count > SplitPieceIndices)
                {
                    pieces.Add(piece);
                    piece = new List<int>();
                }
                if (piece.Count > 0)
                {
                    piece.Add(restart);
                }
                piece.AddRange(one);
            }
            if (piece.Count > 0)
            {
                pieces.Add(piece);
            }
            return pieces;
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
    }
}
