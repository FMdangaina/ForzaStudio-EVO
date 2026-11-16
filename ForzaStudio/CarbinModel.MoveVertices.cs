using System.Collections.Generic;

namespace ForzaStudio
{
    // Move / rotate / scale of a sub-object selection (vertices, edges, faces) made in the MESH tab, with the view's
    // handles (docs/UI_WORKFLOW.md step B). Only the LOD the mesh is in changes (user 2026-11-16: "questi movimenti
    // liberi si applicano solo sul lod che abbiamo aperto"):
    // - a vertex the subsections of the SAME LOD share (a seam between two materials of the part) moves for all of
    //   them, so the seam stays closed;
    // - a vertex another LOD of the part also uses (LOD 1-5 share one vertex buffer) is copied first, and the
    //   subsections of this LOD use the copy: the other LODs stay as they are.
    // Bounds and centroid are refitted as for a part move.
    public partial class CarbinModel
    {
        // vertices: indices into the subsection's vertex buffer (as StripTriangles gives them). Returns the number of
        // vertices moved.
        public int TransformVertices(int sectionIndex, int subIndex, ICollection<int> vertices, Microsoft.Xna.Framework.Matrix m)
        {
            if (sectionIndex < 0 || sectionIndex >= this.Sections.Count)
            {
                return 0;
            }
            Section section = this.Sections[sectionIndex];
            if (subIndex < 0 || subIndex >= section.SubSections.Count || vertices.Count == 0)
            {
                return 0;
            }
            SubSection target = section.SubSections[subIndex];
            VertexBuffer buffer = BufferOf(section, target);
            Table damage = DamageOf(section, buffer);
            if (buffer.Count == 0)
            {
                return 0;
            }
            bool lod0 = buffer == section.Lod0;
            double[] lod = Positions(section, section.Lod);
            double[] lod0Positions = Positions(section, section.Lod0);
            double[] positions = lod0 ? lod0Positions : lod;

            // which vertices the other LODs of this buffer use
            bool[] otherLod = new bool[buffer.Count];
            foreach (SubSection sub in section.SubSections)
            {
                if (BufferOf(section, sub) != buffer || sub.Lod == target.Lod)
                {
                    continue;
                }
                foreach (int index in sub.Indices)
                {
                    if (!IsRestart(sub, index) && index >= 0 && index < buffer.Count)
                    {
                        otherLod[index] = true;
                    }
                }
            }

            // the shared ones get a copy for this LOD
            bool hasDamage = damage.Count == buffer.Count && damage.Stride == 4;
            Dictionary<int, int> copies = new Dictionary<int, int>();
            List<double> newPositions = new List<double>(positions);
            System.IO.MemoryStream data = new System.IO.MemoryStream();
            data.Write(buffer.Data, 0, buffer.Data.Length);
            System.IO.MemoryStream words = new System.IO.MemoryStream();
            words.Write(damage.Data, 0, damage.Data.Length);
            int count = buffer.Count;
            foreach (int v in vertices)
            {
                if (v < 0 || v >= buffer.Count || !otherLod[v] || copies.ContainsKey(v))
                {
                    continue;
                }
                copies[v] = count++;
                data.Write(buffer.Data, v * buffer.Stride, buffer.Stride);
                if (hasDamage)
                {
                    words.Write(damage.Data, v * 4, 4);
                }
                newPositions.Add(positions[v * 3]);
                newPositions.Add(positions[(v * 3) + 1]);
                newPositions.Add(positions[(v * 3) + 2]);
            }
            if (copies.Count > 0)
            {
                foreach (SubSection sub in section.SubSections)
                {
                    if (BufferOf(section, sub) != buffer || sub.Lod != target.Lod)
                    {
                        continue;
                    }
                    for (int k = 0; k < sub.Indices.Count; k++)
                    {
                        int copy;
                        if (!IsRestart(sub, sub.Indices[k]) && copies.TryGetValue(sub.Indices[k], out copy))
                        {
                            sub.Indices[k] = copy;
                        }
                    }
                }
                buffer.Data = data.ToArray();
                buffer.Count = count;
                if (hasDamage)
                {
                    damage.Data = words.ToArray();
                    damage.Count = count;
                }
            }

            Microsoft.Xna.Framework.Quaternion rotation;
            Microsoft.Xna.Framework.Matrix normalMatrix;
            FrameTransform(m, out rotation, out normalMatrix);
            double[] result = newPositions.ToArray();
            int moved = 0;
            foreach (int original in vertices)
            {
                if (original < 0 || original >= positions.Length / 3)
                {
                    continue;
                }
                int copy;
                int i = copies.TryGetValue(original, out copy) ? copy : original;
                Microsoft.Xna.Framework.Vector3 p = new Microsoft.Xna.Framework.Vector3((float) result[i * 3], (float) result[(i * 3) + 1], (float) result[(i * 3) + 2]);
                p = Microsoft.Xna.Framework.Vector3.Transform(p, m);
                result[i * 3] = p.X;
                result[(i * 3) + 1] = p.Y;
                result[(i * 3) + 2] = p.Z;
                TransformFrame(buffer.Data, (i * buffer.Stride) + 16, rotation, normalMatrix);
                moved++;
            }
            if (moved > 0)
            {
                Reencode(section, lod0 ? lod : result, lod0 ? result : lod0Positions);
                CheckIndexRange(section);
            }
            return moved;
        }
    }
}
