namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;

    // Face-level edits of one mesh (subsection = one material in one LOD): delete triangles, or detach
    // them into a part of their own. The faces are the ones StripTriangles lists (their numbers are the
    // positions in that list). The mesh's LOD is the only one touched: the other LODs of the part keep
    // their triangles (the main file's LODs are separate subsections over a shared vertex buffer).
    public partial class CarbinModel
    {
        // Triangles written as 3-index strips ended by a restart: the winding stays as StripTriangles gave it.
        // A copy of vertex v at the end of the buffer (and of its damage word), for a face that must stop
        // sharing it. Returns the new vertex's index.
        internal static int CopyVertexToEnd(Section section, bool lod0, int v)
        {
            VertexBuffer buffer = lod0 ? section.Lod0 : section.Lod;
            Table damage = lod0 ? section.DamageLod0 : section.DamageLod;
            bool hasDamage = damage.Count == buffer.Count && damage.Stride > 0;
            int index = buffer.Count;
            byte[] data = new byte[(index + 1) * buffer.Stride];
            Buffer.BlockCopy(buffer.Data, 0, data, 0, index * buffer.Stride);
            Buffer.BlockCopy(buffer.Data, v * buffer.Stride, data, index * buffer.Stride, buffer.Stride);
            buffer.Data = data;
            buffer.Count = index + 1;
            if (hasDamage)
            {
                byte[] words = new byte[(index + 1) * damage.Stride];
                Buffer.BlockCopy(damage.Data, 0, words, 0, index * damage.Stride);
                Buffer.BlockCopy(damage.Data, v * damage.Stride, words, index * damage.Stride, damage.Stride);
                damage.Data = words;
                damage.Count = index + 1;
            }
            return index;
        }

        internal static List<int> TriangleStrips(SubSection sub, List<int[]> triangles, Predicate<int> keep)
        {
            int restart = (sub.IndexSize == 2) ? 0xFFFF : 0x00FFFFFF;
            List<int> indices = new List<int>();
            for (int i = 0; i < triangles.Count; i++)
            {
                if (!keep(i))
                {
                    continue;
                }
                if (indices.Count > 0)
                {
                    indices.Add(restart);
                }
                indices.AddRange(triangles[i]);
            }
            return indices;
        }

        // Drops the vertices no subsection uses any more and refits the bounds.
        private static void CompactSection(Section section)
        {
            double[] lod = Compact(section, section.Lod, section.DamageLod, false, Positions(section, section.Lod));
            double[] lod0 = Compact(section, section.Lod0, section.DamageLod0, true, Positions(section, section.Lod0));
            // FM3 (its own layout): the box stays. Every vertex left was inside it already, so its bytes stay as the game
            // has them; a refitted box rewrote every position half of the part (up to 1.6 mm off on a "body", mesht).
            if (!IsMotorsport3Layout(section, section.Lod) && !IsMotorsport3Layout(section, section.Lod0))
            {
                Reencode(section, lod, lod0);
            }
            CheckIndexRange(section);
        }

        // Deletes the faces; the vertices only they used go. A mesh left with no face is removed, and a
        // part left with no mesh too. Returns a short report.
        public string DeleteFaces(int sectionIndex, int subIndex, IEnumerable<int> faces)
        {
            Section section = this.Sections[sectionIndex];
            SubSection sub = section.SubSections[subIndex];
            string name = section.Name + " / " + sub.Name + " (LOD " + sub.Lod + ")";
            List<int[]> triangles = StripTriangles(sub);
            Set<int> gone = new Set<int>(faces);
            int left = triangles.Count - gone.Count;
            if (gone.Count == 0)
            {
                return "";
            }
            if (left <= 0)
            {
                this.RemoveSubSectionsAt(sectionIndex, new List<int> { subIndex });
                return name + ": every face deleted, the mesh is removed";
            }
            sub.Indices = TriangleStrips(sub, triangles, delegate(int i) { return !gone.Contains(i); });
            CompactSection(section);
            this.CheckDrawLists();
            return string.Format("{0}: {1} faces deleted, {2} left", name, gone.Count, left);
        }

        // Makes the faces a mesh of their own in the same part: a copy of the mesh (same material and LOD)
        // that draws only those faces, placed in the draw lists right after the mesh it comes from. The
        // vertices stay shared; the UV mapper copies them when it edits one side only.
        public string DetachFaces(int sectionIndex, int subIndex, IEnumerable<int> faces)
        {
            Section section = this.Sections[sectionIndex];
            SubSection sub = section.SubSections[subIndex];
            string from = section.Name + " / " + sub.Name + " (LOD " + sub.Lod + ")";
            List<int[]> triangles = StripTriangles(sub);
            Set<int> moving = new Set<int>(faces);
            if (moving.Count == 0)
            {
                return "";
            }
            if (moving.Count >= triangles.Count)
            {
                return from + ": every face is selected, nothing to detach from";
            }
            SubSection piece = sub.Copy();
            piece.Indices = TriangleStrips(piece, triangles, delegate(int i) { return moving.Contains(i); });
            sub.Indices = TriangleStrips(sub, triangles, delegate(int i) { return !moving.Contains(i); });
            int at = section.SubSections.Count;
            section.SubSections.Add(piece);
            for (int l = 0; l < this.DrawLists.Count; l++)
            {
                List<uint> list = new List<uint>();
                foreach (uint entry in this.DrawLists[l])
                {
                    list.Add(entry);
                    if ((int) (entry >> 16) == sectionIndex && (int) (entry & 0xFFFF) == subIndex)
                    {
                        list.Add((uint) ((sectionIndex << 16) | at));
                    }
                }
                this.DrawLists[l] = list.ToArray();
            }
            this.CheckDrawLists();
            return string.Format("{0}: {1} faces detached into their own mesh, {2} left", from, moving.Count, triangles.Count - moving.Count);
        }
    }
}
