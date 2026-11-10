using System;
using System.Collections.Generic;

namespace ForzaStudio
{
    // Attach (as in 3ds Max): one mesh (subsection) joins another of the same LOD, in the same part or in another one.
    // The target keeps its name, material and place in the draw lists; the attached mesh's triangles are added to its
    // strip list (restart between), its vertices copied into the target part with their UVs re-encoded to one range
    // that covers both (MergeSection does the vertex work). The attached mesh is removed, and its part too if it was
    // the last mesh there.
    public partial class CarbinModel
    {
        // The index count the joined mesh would have (both lists plus the restart between them).
        public int AttachedIndexCount(int targetSection, int targetSub, int sourceSection, int sourceSub)
        {
            return this.Sections[targetSection].SubSections[targetSub].Indices.Count + this.Sections[sourceSection].SubSections[sourceSub].Indices.Count + 1;
        }

        // True when FM4 would leave holes in the joined mesh (see LongMeshes): every write would cut it again by height.
        public bool AttachTooLong(int targetSection, int targetSub, int sourceSection, int sourceSub)
        {
            bool fm4 = !this.motorsport3 && !this.IsHorizon && (this.Type == 2 || this.Type == 3);
            return fm4 && this.AttachedIndexCount(targetSection, targetSub, sourceSection, sourceSub) > MaxGameSubSectionIndices;
        }

        // Duplicate of one mesh in its own part (Clone in 3ds Max): same material, LOD, UV range and triangles, using the
        // same vertices; its draw-list entries go right after the original's. The copy can then be moved (drag onto a
        // part), edited or renamed on its own: those edits touch only its index list, and a move copies its vertices.
        // Returns the copy's subsection index.
        public int DuplicateSubSection(int sectionIndex, int subIndex)
        {
            if (sectionIndex < 0 || sectionIndex >= this.Sections.Count || subIndex < 0 || subIndex >= this.Sections[sectionIndex].SubSections.Count)
            {
                throw new ArgumentException("The mesh to duplicate is not in the model.");
            }
            Section section = this.Sections[sectionIndex];
            SubSection original = section.SubSections[subIndex];
            if (original.Stripped != null)
            {
                throw new NotSupportedException("This mesh layout cannot be duplicated.");
            }
            SubSection copy = original.Copy();
            section.SubSections.Add(copy);
            int added = section.SubSections.Count - 1;
            this.InsertDrawListEntryAfter((uint) ((sectionIndex << 16) | subIndex), (uint) ((sectionIndex << 16) | added));
            this.CheckDrawLists();
            return added;
        }

        public string AttachSubSection(int targetSection, int targetSub, int sourceSection, int sourceSub)
        {
            if (targetSection < 0 || targetSection >= this.Sections.Count || sourceSection < 0 || sourceSection >= this.Sections.Count
                || targetSub < 0 || targetSub >= this.Sections[targetSection].SubSections.Count
                || sourceSub < 0 || sourceSub >= this.Sections[sourceSection].SubSections.Count)
            {
                throw new ArgumentException("The meshes to attach are not in the model.");
            }
            if (targetSection == sourceSection && targetSub == sourceSub)
            {
                throw new ArgumentException("A mesh cannot be attached to itself.");
            }
            Section target = this.Sections[targetSection];
            Section source = this.Sections[sourceSection];
            SubSection keep = target.SubSections[targetSub];
            SubSection moving = source.SubSections[sourceSub];
            if (keep.Lod != moving.Lod)
            {
                throw new ArgumentException(string.Format("The meshes are in different LODs ({0} and {1}): only meshes of the same LOD can be joined.", keep.Lod, moving.Lod));
            }
            if (keep.Stripped != null || moving.Stripped != null)
            {
                throw new NotSupportedException("This mesh layout cannot be joined.");
            }
            string keepName = keep.Name;
            string movingName = moving.Name;
            string sourceName = source.Name;
            int before = keep.Indices.Count;
            int added = moving.Indices.Count;

            // A material name no other mesh of the target part uses, so MergeSection joins exactly these two.
            string temporary = "__attach";
            while (target.SubSections.Exists(delegate(SubSection s) { return s.Name == temporary; }))
            {
                temporary += "_";
            }
            // The attached mesh alone in a copy of its part, then out of its own part.
            string copy = "__attach_part";
            while (this.Find(copy) != null)
            {
                copy += "_";
            }
            this.DuplicateSection(source.Name, copy);
            Section copied = this.Find(copy);
            List<int> others = new List<int>();
            for (int k = 0; k < copied.SubSections.Count; k++)
            {
                if (k != sourceSub)
                {
                    others.Add(k);
                }
            }
            this.RemoveSubSectionsAt(this.Sections.IndexOf(copied), others);
            copied.SubSections[0].Name = temporary;
            this.RemoveSubSectionsAt(this.Sections.IndexOf(source), new List<int> { sourceSub });

            keep.Name = temporary;
            try
            {
                this.MergeSection(copy, target.Name);
            }
            finally
            {
                keep.Name = keepName;
            }
            string report = string.Format("{0}.{1} attached to {2}.{3} (LOD {4}): {5} + {6} indices", sourceName, movingName, target.Name, keepName, keep.Lod, before, added);
            if (!this.Sections.Contains(source))
            {
                report += "; " + sourceName + " had no other mesh and was removed";
            }
            return report;
        }
    }
}
