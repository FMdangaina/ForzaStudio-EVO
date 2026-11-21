namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Microsoft.Xna.Framework;

    // MESH tab on FM2 models (user 2026-11-21: "non posso editare mesh direttamente, non mi fa selezionare nulla"): the
    // faces of a mesh as the view picks them, Delete Faces, Detach Faces and the move / rotate / scale of picked vertices,
    // with the meaning they have on FM4 models (CarbinMeshEdit.cs, CarbinModel.MoveVertices.cs):
    //  - only the LOD of the mesh changes: a vertex another LOD of the part also draws is copied first;
    //  - every vertex byte stays but what the edit changes: the position of a moved vertex (4 halves, written against the
    //    part's origin as it is, so the other vertices keep their bytes) and its normal when the edit turns it.
    // The part's box keeps its origin and grows to hold the moved vertices (min = - max, as every FM2 part).
    public partial class Fm2CarbinModel
    {
        // The triangles of mesh k of part p (indices into the part's vertices), in the order the mesh editor numbers them.
        public List<int[]> MeshFaces(int p, int k)
        {
            Part part = this.Parts[p];
            return MeshTriangles(part.Meshes[k], part.VertexCount);
        }

        private static void SetFaces(Mesh mesh, List<int[]> faces, Predicate<int> keep)
        {
            List<int> indices = new List<int>();
            int restart = RestartOf(mesh);
            for (int f = 0; f < faces.Count; f++)
            {
                if (!keep(f))
                {
                    continue;
                }
                if (mesh.Primitive != 4 && indices.Count > 0)
                {
                    indices.Add(restart);
                }
                indices.AddRange(faces[f]);
            }
            mesh.Indices = indices;
        }

        // Deletes the faces; the vertices only they used go. A mesh left with no face is removed, and a part left with no
        // mesh too. Returns a short report.
        public string DeleteFaces(int p, int k, IEnumerable<int> faces)
        {
            string report = "";
            this.Atomic(delegate
            {
                Part part = this.Parts[p];
                Mesh mesh = part.Meshes[k];
                string name = part.Name + " / " + mesh.MaterialName + " (LOD " + mesh.Lod + ")";
                List<int[]> triangles = MeshTriangles(mesh, part.VertexCount);
                HashSet<int> gone = new HashSet<int>();
                foreach (int f in faces)
                {
                    if (f >= 0 && f < triangles.Count)
                    {
                        gone.Add(f);
                    }
                }
                if (gone.Count == 0)
                {
                    return;
                }
                int left = triangles.Count - gone.Count;
                if (left <= 0)
                {
                    this.RemoveMeshesAt(part, new List<int> { k });
                    report = name + ": every face deleted, the mesh is removed";
                    return;
                }
                SetFaces(mesh, triangles, delegate(int f) { return !gone.Contains(f); });
                // the vertices no mesh of the part draws any more go
                Dictionary<Part, byte[]> locals = this.Locals();
                byte[] local = locals[part];
                int count = local.Length / VertexStride;
                bool[] used = new bool[count];
                foreach (Mesh other in part.Meshes)
                {
                    foreach (int i in other.Indices)
                    {
                        if (i >= 0 && i < count)
                        {
                            used[i] = true;
                        }
                    }
                }
                int[] renumber = new int[count];
                MemoryStream compact = new MemoryStream();
                int next = 0;
                for (int i = 0; i < count; i++)
                {
                    renumber[i] = used[i] ? next++ : -1;
                    if (used[i])
                    {
                        compact.Write(local, i * VertexStride, VertexStride);
                    }
                }
                foreach (Mesh other in part.Meshes)
                {
                    for (int n = 0; n < other.Indices.Count; n++)
                    {
                        int i = other.Indices[n];
                        if (i >= 0 && i < count)
                        {
                            other.Indices[n] = renumber[i];
                        }
                    }
                }
                locals[part] = compact.ToArray();
                this.Rebuild(locals);
                report = string.Format("{0}: {1} faces deleted, {2} left", name, gone.Count, left);
            });
            return report;
        }

        // Makes the faces a mesh of their own in the same part: a copy of the mesh (same material and LOD) that draws only
        // those faces. The vertices stay shared; the UV mapper copies them when it edits one side only.
        public string DetachFaces(int p, int k, IEnumerable<int> faces)
        {
            string report = "";
            this.Atomic(delegate
            {
                Part part = this.Parts[p];
                Mesh mesh = part.Meshes[k];
                string from = part.Name + " / " + mesh.MaterialName + " (LOD " + mesh.Lod + ")";
                List<int[]> triangles = MeshTriangles(mesh, part.VertexCount);
                HashSet<int> moving = new HashSet<int>();
                foreach (int f in faces)
                {
                    if (f >= 0 && f < triangles.Count)
                    {
                        moving.Add(f);
                    }
                }
                if (moving.Count == 0)
                {
                    return;
                }
                if (moving.Count >= triangles.Count)
                {
                    report = from + ": every face is selected, nothing to detach from";
                    return;
                }
                Mesh piece = CopyMesh(mesh);
                SetFaces(piece, triangles, delegate(int f) { return moving.Contains(f); });
                SetFaces(mesh, triangles, delegate(int f) { return !moving.Contains(f); });
                part.Meshes.Add(piece);
                this.AddDrawEntry(p, part.Meshes.Count - 1);
                report = string.Format("{0}: {1} faces detached into their own mesh, {2} left", from, moving.Count, triangles.Count - moving.Count);
            });
            return report;
        }

        // Moves / rotates / scales the given vertices of mesh k of part p (indices as MeshFaces gives them). Returns the
        // number of vertices moved.
        public int TransformVertices(int p, int k, ICollection<int> vertices, Matrix m)
        {
            int moved = 0;
            if (p < 0 || p >= this.Parts.Count || k < 0 || k >= this.Parts[p].Meshes.Count || vertices.Count == 0)
            {
                return 0;
            }
            this.Atomic(delegate
            {
                Part part = this.Parts[p];
                uint lod = part.Meshes[k].Lod;
                Dictionary<Part, byte[]> locals = this.Locals();
                byte[] local = locals[part];
                int count = local.Length / VertexStride;
                // which vertices the other LODs of the part draw
                bool[] otherLod = new bool[count];
                foreach (Mesh mesh in part.Meshes)
                {
                    if (mesh.Lod == lod)
                    {
                        continue;
                    }
                    foreach (int i in mesh.Indices)
                    {
                        if (i >= 0 && i < count)
                        {
                            otherLod[i] = true;
                        }
                    }
                }
                // those get a copy for this LOD
                Dictionary<int, int> copies = new Dictionary<int, int>();
                MemoryStream grown = new MemoryStream();
                grown.Write(local, 0, local.Length);
                int total = count;
                foreach (int v in vertices)
                {
                    if (v < 0 || v >= count || !otherLod[v] || copies.ContainsKey(v))
                    {
                        continue;
                    }
                    copies[v] = total++;
                    grown.Write(local, v * VertexStride, VertexStride);
                }
                if (total >= 0xFFFF)
                {
                    throw new InvalidOperationException(part.Name + ": more than 65534 vertices in one FM2 part with the copies this LOD needs.");
                }
                if (copies.Count > 0)
                {
                    local = grown.ToArray();
                    foreach (Mesh mesh in part.Meshes)
                    {
                        if (mesh.Lod != lod)
                        {
                            continue;
                        }
                        for (int n = 0; n < mesh.Indices.Count; n++)
                        {
                            int copy;
                            if (copies.TryGetValue(mesh.Indices[n], out copy))
                            {
                                mesh.Indices[n] = copy;
                            }
                        }
                    }
                }
                Quaternion rotation;
                Matrix normalMatrix;
                CarbinModel.FrameTransform(m, out rotation, out normalMatrix);
                bool turn = !CarbinModel.KeepsDirectionsOf(rotation, normalMatrix);
                double[] positions = LocalPositions(part, local);
                HashSet<int> done = new HashSet<int>();
                double[] half = { part.Bounds[6], part.Bounds[7], part.Bounds[8] };
                foreach (int v in vertices)
                {
                    if (v < 0 || v >= count)
                    {
                        continue;
                    }
                    int copy;
                    int i = copies.TryGetValue(v, out copy) ? copy : v;
                    if (!done.Add(i))
                    {
                        continue;
                    }
                    Vector3 to = Vector3.Transform(new Vector3((float) positions[i * 3], (float) positions[(i * 3) + 1], (float) positions[(i * 3) + 2]), m);
                    double[] d = { to.X - part.Bounds[0], to.Y - part.Bounds[1], to.Z - part.Bounds[2] };
                    double length = Math.Sqrt((d[0] * d[0]) + (d[1] * d[1]) + (d[2] * d[2]));
                    int o = i * VertexStride;
                    for (int c = 0; c < 3; c++)
                    {
                        CarbinModel.WriteHalfBig(local, o + (c * 2), (length > 1e-12) ? d[c] / length : 0);
                        half[c] = Math.Max(half[c], Math.Abs(d[c]));
                    }
                    CarbinModel.WriteHalfBig(local, o + 6, length);
                    if (turn)
                    {
                        TurnNormal(local, i, normalMatrix);
                    }
                    moved++;
                }
                for (int c = 0; c < 3; c++)
                {
                    part.Bounds[3 + c] = (float) (0 - half[c]);
                    part.Bounds[6 + c] = (float) half[c];
                }
                locals[part] = local;
                this.Rebuild(locals);
            });
            return moved;
        }
    }
}
