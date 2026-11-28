namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Microsoft.Xna.Framework;

    // Edits of an FM2 car model (ST-03, user 2026-11-18): parts added / replaced (Copy Part), removed, duplicated, moved /
    // rotated / scaled (whole part or one material). Everything else of the file stays as Fm2CarbinModel read it.
    //
    // A part's vertices are a range of one of the two shared buffers (Fm2CarbinModel header). An edit takes every part's
    // vertex bytes out (Locals), changes the parts it is about, and lays both buffers out again in part order (Rebuild,
    // the same layout RepackBuffers writes: with nothing changed, the file itself). Mesh indices count from the part's
    // first vertex, so they never need renumbering when other parts move in the buffer.
    //
    // Measured on the 310 FM2 car bodies (E:\DEV\fm4dev\ST03\fm2t, study.txt / uvmap / com):
    //  - bounds: origin = the box centre, min = -max (52 of 52 Supra parts); position = origin + xyz * w (4 halves);
    //  - glass shard vertices are in car space (641 of 641 inside the part box): they move with the part;
    //  - the centre of mass is not in car space (8 of 1091 parts with mass inside the box): it stays as it is;
    //  - UV: the FM2 -> FM4 path gives FM4 UV = - FM2 value, U and V (exact on 1081 vertices); FM2 holds +-25 only
    //    (offset-binary word, ForzaVertex.Fm2Uv: before 2026-11-21 written without the offset = mirrored = black in game);
    //  - the 16 baked lighting values: the nearest vertex of another part is closer than a random one (1.45 vs 2.16).
    public partial class Fm2CarbinModel
    {
        private const double UvLimit = 32767 / 1310.72;     // the largest FM2 UV value (offset-binary word, +-25)

        // FM4 carbon fibre UVs = FM2's x 250 (the weave's tiling is in FM4's UVs, in FM2's shader).
        public const float CarbonFibreScale = 250f;

        public static bool IsCarbonFibre(string material)
        {
            return material != null && material.StartsWith("carbon_fiber", StringComparison.OrdinalIgnoreCase);
        }

        // ---- vertices per part ----

        public byte[] PartVertices(Part part)
        {
            int count = part.VertexCount;
            byte[] local = new byte[count * VertexStride];
            if (count > 0)
            {
                Buffer.BlockCopy(this.BufferOf(part).Data, part.FirstVertex * VertexStride, local, 0, local.Length);
            }
            return local;
        }

        private Dictionary<Part, byte[]> Locals()
        {
            Dictionary<Part, byte[]> locals = new Dictionary<Part, byte[]>();
            foreach (Part part in this.Parts)
            {
                locals[part] = this.PartVertices(part);
            }
            return locals;
        }

        // Both shared buffers laid out again from each part's own vertex bytes, in part order, in the buffer of its flag.
        private void Rebuild(Dictionary<Part, byte[]> locals)
        {
            MemoryStream[] data = new MemoryStream[] { new MemoryStream(), new MemoryStream() };   // 0 see-through, 1 opaque
            int[] cursor = new int[2];
            foreach (Part part in this.Parts)
            {
                byte[] local;
                if (!locals.TryGetValue(part, out local) || part.Meshes.Count == 0)
                {
                    local = new byte[0];
                }
                int target = (part.HasTransparentMeshes != 0) ? 0 : 1;
                int count = local.Length / VertexStride;
                if (cursor[target] + count > 0x10000)
                {
                    throw new InvalidOperationException(string.Format("FM2 holds at most 65536 vertices in its {0} buffer: with {1} ({2} vertices) this car would need {3}.",
                        (target == 0) ? "see-through" : "opaque", part.Name, count, cursor[target] + count));
                }
                data[target].Write(local, 0, local.Length);
                part.FirstVertex = cursor[target];
                part.LastVertex = cursor[target] + count - 1;
                cursor[target] += count;
            }
            this.TransparentVertices.Data = data[0].ToArray();
            this.TransparentVertices.Count = cursor[0];
            this.OpaqueVertices.Data = data[1].ToArray();
            this.OpaqueVertices.Count = cursor[1];
        }

        // ---- parts ----

        private static bool SeeThroughPart(Part part)
        {
            foreach (Mesh mesh in part.Meshes)
            {
                if (IsSeeThrough(mesh.MaterialName))
                {
                    return true;
                }
            }
            return false;
        }

        private void AddDrawEntries(int index)
        {
            Part part = this.Parts[index];
            for (int k = 0; k < part.Meshes.Count; k++)
            {
                Mesh mesh = part.Meshes[k];
                if (mesh.Lod < 1 || mesh.Lod > this.LodCount)
                {
                    throw new InvalidOperationException(string.Format("{0}.{1}: LOD {2}, this FM2 model has LODs 1-{3}.", part.Name, mesh.MaterialName, mesh.Lod, this.LodCount));
                }
                int list = (IsSeeThrough(mesh.MaterialName) ? 0 : this.LodCount) + (int) mesh.Lod - 1;
                this.DrawLists[list].Add((uint) ((index << 16) | k));
            }
        }

        // shift: entries of later parts move down one (the part is removed); else they stay (the part is replaced).
        private void RemoveDrawEntries(int index, bool shift)
        {
            foreach (List<uint> list in this.DrawLists)
            {
                List<uint> kept = new List<uint>();
                foreach (uint entry in list)
                {
                    int p = (int) (entry >> 16);
                    if (p == index)
                    {
                        continue;
                    }
                    kept.Add((shift && p > index) ? (uint) (((p - 1) << 16) | (int) (entry & 0xFFFF)) : entry);
                }
                list.Clear();
                list.AddRange(kept);
            }
        }

        private int IndexOf(string name)
        {
            Part part = this.Find(name);
            if (part == null)
            {
                throw new ArgumentException("The model has no part " + name + ".");
            }
            return this.Parts.IndexOf(part);
        }

        // Every edit is all or nothing: when it fails half way (a LOD this model lacks, a full shared buffer...) the model is
        // put back as it was (every field from the bytes written before the edit), then the error goes on.
        private void Atomic(Action edit)
        {
            byte[] before = this.ToBytes();
            try
            {
                edit();
            }
            catch (Exception)
            {
                Fm2CarbinModel again = new Fm2CarbinModel(before);
                foreach (System.Reflection.FieldInfo field in typeof(Fm2CarbinModel).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                {
                    field.SetValue(this, field.GetValue(again));
                }
                throw;
            }
        }

        public void RemovePart(string name)
        {
            this.Atomic(delegate { this.RemovePartCore(name); });
        }

        public int RemoveMeshes(string name, string material)
        {
            int removed = 0;
            this.Atomic(delegate { removed = this.RemoveMeshesCore(name, material); });
            return removed;
        }

        public void TransformPart(string name, Matrix m)
        {
            this.Atomic(delegate { this.TransformPartCore(name, m); });
        }

        public int TransformMesh(string name, string material, Matrix m)
        {
            int moved = 0;
            this.Atomic(delegate { moved = this.TransformMeshCore(name, material, m); });
            return moved;
        }

        // A new part at the end of the list (its vertices: 'vertices', 60 bytes each, numbered as its mesh indices).
        public void AddPart(Part part, byte[] vertices)
        {
            if (this.Find(part.Name) != null)
            {
                throw new ArgumentException("This model already has a part named " + part.Name + ".");
            }
            this.Atomic(delegate
            {
                Dictionary<Part, byte[]> locals = this.Locals();
                part.HasTransparentMeshes = (byte) (SeeThroughPart(part) ? 1 : 0);
                this.Parts.Add(part);
                locals[part] = vertices;
                this.AddDrawEntries(this.Parts.Count - 1);
                this.Rebuild(locals);
            });
        }

        // 'part' takes the place (index: draw order, emitters' parent) of the part named 'replaced'.
        public void ReplacePart(string replaced, Part part, byte[] vertices)
        {
            int index = this.IndexOf(replaced);
            if (!string.Equals(part.Name, replaced, StringComparison.OrdinalIgnoreCase) && this.Find(part.Name) != null)
            {
                throw new ArgumentException("This model already has a part named " + part.Name + ".");
            }
            this.Atomic(delegate
            {
                Dictionary<Part, byte[]> locals = this.Locals();
                locals.Remove(this.Parts[index]);
                part.HasTransparentMeshes = (byte) (SeeThroughPart(part) ? 1 : 0);
                this.Parts[index] = part;
                locals[part] = vertices;
                this.RemoveDrawEntries(index, false);
                this.AddDrawEntries(index);
                this.Rebuild(locals);
            });
        }

        // Emitters (exhaust tips) parented to the removed part get no parent (-1); later parts' numbers move down.
        private void RemovePartCore(string name)
        {
            int index = this.IndexOf(name);
            Dictionary<Part, byte[]> locals = this.Locals();
            locals.Remove(this.Parts[index]);
            this.RemoveDrawEntries(index, true);
            foreach (byte[] emitter in this.Emitters)
            {
                int parent = ReadS32(emitter, 8);
                if (parent == index)
                {
                    WriteS32(emitter, 8, -1);
                }
                else if (parent > index)
                {
                    WriteS32(emitter, 8, parent - 1);
                }
            }
            this.Parts.RemoveAt(index);
            this.Rebuild(locals);
        }

        public void DuplicatePart(string name, string newName)
        {
            Part from = this.Find(name);
            if (from == null)
            {
                throw new ArgumentException("The model has no part " + name + ".");
            }
            Part copy = CopyPart(from);
            copy.Name = newName;
            this.AddPart(copy, this.PartVertices(from));
        }

        public void RenamePart(string name, string newName)
        {
            Part part = this.Find(name);
            if (part == null)
            {
                throw new ArgumentException("The model has no part " + name + ".");
            }
            if (!string.Equals(name, newName, StringComparison.OrdinalIgnoreCase) && this.Find(newName) != null)
            {
                throw new ArgumentException("This model already has a part named " + newName + ".");
            }
            part.Name = newName;
        }

        // Removes the meshes of one material from a part (the part itself when nothing is left); vertices no mesh draws
        // any more go (FM2: the highest index is the part's last vertex). Returns the meshes removed.
        private int RemoveMeshesCore(string name, string material)
        {
            Part part = this.Find(name);
            if (part == null)
            {
                return 0;
            }
            List<int> gone = new List<int>();
            for (int k = 0; k < part.Meshes.Count; k++)
            {
                if (string.Equals(part.Meshes[k].MaterialName, material, StringComparison.Ordinal))
                {
                    gone.Add(k);
                }
            }
            return this.RemoveMeshesAt(part, gone);
        }

        // Removes the meshes 'gone' (mesh numbers, ascending) of a part, the part itself when nothing is left; vertices no
        // mesh draws any more go. Returns the meshes removed.
        private int RemoveMeshesAt(Part part, List<int> gone)
        {
            int index = this.Parts.IndexOf(part);
            if (gone.Count == 0)
            {
                return 0;
            }
            if (gone.Count == part.Meshes.Count)
            {
                this.RemovePartCore(part.Name);
                return gone.Count;
            }
            Dictionary<Part, byte[]> locals = this.Locals();
            foreach (List<uint> list in this.DrawLists)
            {
                List<uint> kept = new List<uint>();
                foreach (uint entry in list)
                {
                    int p = (int) (entry >> 16);
                    int k = (int) (entry & 0xFFFF);
                    if (p != index)
                    {
                        kept.Add(entry);
                        continue;
                    }
                    if (gone.Contains(k))
                    {
                        continue;
                    }
                    int below = 0;
                    foreach (int g in gone)
                    {
                        below += (g < k) ? 1 : 0;
                    }
                    kept.Add((uint) ((p << 16) | (k - below)));
                }
                list.Clear();
                list.AddRange(kept);
            }
            for (int g = gone.Count - 1; g >= 0; g--)
            {
                part.Meshes.RemoveAt(gone[g]);
            }
            byte[] local = locals[part];
            int count = local.Length / VertexStride;
            bool[] used = new bool[count];
            foreach (Mesh mesh in part.Meshes)
            {
                foreach (int i in mesh.Indices)
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
            foreach (Mesh mesh in part.Meshes)
            {
                for (int k = 0; k < mesh.Indices.Count; k++)
                {
                    int i = mesh.Indices[k];
                    if (i >= 0 && i < count)
                    {
                        mesh.Indices[k] = renumber[i];
                    }
                }
            }
            locals[part] = compact.ToArray();
            part.HasTransparentMeshes = (byte) (SeeThroughPart(part) ? 1 : 0);
            this.Rebuild(locals);
            return gone.Count;
        }

        // ---- meshes between parts (tree drag, Attach, Merge Part, Duplicate Mesh; user 2026-11-21) ----

        // The mesh the tree shows (part / mesh number in file order, checked by name, else found by part name, material and
        // LOD). False when the file has no such mesh.
        public bool LocateMesh(string partName, string material, int lod, int partHint, int meshHint, out int partIndex, out int meshIndex)
        {
            partIndex = -1;
            meshIndex = -1;
            if (partHint >= 0 && partHint < this.Parts.Count && this.Parts[partHint].Name == partName && meshHint >= 0
                && meshHint < this.Parts[partHint].Meshes.Count && this.Parts[partHint].Meshes[meshHint].MaterialName == material
                && (lod < 0 || this.Parts[partHint].Meshes[meshHint].Lod == lod))
            {
                partIndex = partHint;
                meshIndex = meshHint;
                return true;
            }
            Part part = this.Find(partName);
            if (part == null)
            {
                return false;
            }
            for (int k = 0; k < part.Meshes.Count; k++)
            {
                if (part.Meshes[k].MaterialName == material && (lod < 0 || part.Meshes[k].Lod == lod))
                {
                    partIndex = this.Parts.IndexOf(part);
                    meshIndex = k;
                    return true;
                }
            }
            return false;
        }

        // Mesh k of part p goes into the part 'target' (made with p's settings, without glass shards, when the model has
        // none of that name). into >= 0: joined into that mesh of the target, which keeps its name and material (Attach);
        // else joined into the target's mesh of the same material and LOD when there is one, else a mesh of its own. The
        // moved vertices keep every byte but the position (re-encoded in the target's bounds); the source part loses the
        // vertices nothing draws any more and goes when it was its last mesh. Returns a short report.
        public string MoveMesh(int p, int k, string target, int into)
        {
            string report = null;
            this.Atomic(delegate { report = this.MoveMeshCore(p, k, target, into); });
            return report;
        }

        // The source part's meshes all go into the target (MoveMesh, same material and LOD joined), glass shards too; the
        // source part goes. Without a target part the source is renamed.
        public string MergeParts(string source, string target)
        {
            string report = null;
            this.Atomic(delegate
            {
                Part from = this.Find(source);
                if (from == null)
                {
                    throw new ArgumentException("The model has no part " + source + ".");
                }
                Part to = this.Find(target);
                if (to == null)
                {
                    from.Name = target;
                    report = "renamed (no " + target + " there)";
                    return;
                }
                if (from == to)
                {
                    throw new ArgumentException("A part cannot be merged into itself.");
                }
                // all at once: the target's positions are encoded once (one move per mesh would round them every time)
                Dictionary<Part, byte[]> locals = this.Locals();
                byte[] toLocal = locals[to];
                byte[] fromLocal = locals[from];
                int offset = toLocal.Length / VertexStride;
                int fromCount = fromLocal.Length / VertexStride;
                if (offset + fromCount >= 0xFFFF)
                {
                    throw new InvalidOperationException(target + ": more than 65534 vertices in one FM2 part after the merge.");
                }
                byte[] combined = new byte[toLocal.Length + fromLocal.Length];
                Buffer.BlockCopy(toLocal, 0, combined, 0, toLocal.Length);
                Buffer.BlockCopy(fromLocal, 0, combined, toLocal.Length, fromLocal.Length);
                List<double> positions = new List<double>(LocalPositions(to, toLocal));
                positions.AddRange(LocalPositions(from, fromLocal));
                EncodePositions(to, combined, positions.ToArray());
                locals[to] = combined;
                int toIndex = this.Parts.IndexOf(to);
                int joinedCount = 0;
                foreach (Mesh mesh in from.Meshes)
                {
                    List<int> moved = new List<int>(mesh.Indices.Count);
                    foreach (int i in mesh.Indices)
                    {
                        moved.Add((i >= 0 && i < fromCount) ? i + offset : RestartOf(mesh));
                    }
                    Mesh joined = null;
                    foreach (Mesh m in to.Meshes)
                    {
                        if (m.MaterialName == mesh.MaterialName && m.Lod == mesh.Lod && m.Primitive == mesh.Primitive && m.IndexSize == mesh.IndexSize)
                        {
                            joined = m;
                            break;
                        }
                    }
                    if (joined != null)
                    {
                        if (joined.Primitive == 6 && joined.Indices.Count > 0)
                        {
                            joined.Indices.Add(RestartOf(joined));
                        }
                        joined.Indices.AddRange(moved);
                        joinedCount++;
                    }
                    else
                    {
                        Mesh copy = CopyMesh(mesh);
                        copy.Indices = moved;
                        to.Meshes.Add(copy);
                        this.AddDrawEntry(toIndex, to.Meshes.Count - 1);
                    }
                }
                AppendGlass(to, from);
                to.HasTransparentMeshes = (byte) (SeeThroughPart(to) ? 1 : 0);
                this.Rebuild(locals);
                int meshes = from.Meshes.Count;
                this.RemovePartCore(source);
                report = string.Format("merged ({0} meshes, {1} joined to meshes of the same material and LOD)", meshes, joinedCount);
            });
            return report;
        }

        // A copy of mesh k of part p in the same part (same material and LOD, its own copy of the vertices).
        public void DuplicateMesh(int p, int k)
        {
            this.Atomic(delegate
            {
                Part part = this.Parts[p];
                Mesh mesh = part.Meshes[k];
                Dictionary<Part, byte[]> locals = this.Locals();
                byte[] local = locals[part];
                int count = local.Length / VertexStride;
                Dictionary<int, int> map = new Dictionary<int, int>();
                MemoryStream added = new MemoryStream();
                added.Write(local, 0, local.Length);
                Mesh copy = CopyMesh(mesh);
                copy.Indices.Clear();
                foreach (int i in mesh.Indices)
                {
                    if (i < 0 || i >= count)
                    {
                        copy.Indices.Add(i);
                        continue;
                    }
                    int n;
                    if (!map.TryGetValue(i, out n))
                    {
                        n = count + map.Count;
                        map[i] = n;
                        added.Write(local, i * VertexStride, VertexStride);
                    }
                    copy.Indices.Add(n);
                }
                if (count + map.Count >= 0xFFFF)
                {
                    throw new InvalidOperationException(part.Name + ": more than 65534 vertices in one FM2 part after the copy.");
                }
                part.Meshes.Add(copy);
                locals[part] = added.ToArray();
                this.AddDrawEntry(p, part.Meshes.Count - 1);
                this.Rebuild(locals);
            });
        }

        private string MoveMeshCore(int p, int k, string targetName, int into)
        {
            Part source = this.Parts[p];
            Mesh mesh = source.Meshes[k];
            Part target = this.Find(targetName);
            if (target == source && into < 0)
            {
                throw new ArgumentException(mesh.MaterialName + " is already in " + source.Name + ".");
            }
            if (target == source && into == k)
            {
                throw new ArgumentException("A mesh cannot be attached to itself.");
            }
            Dictionary<Part, byte[]> locals = this.Locals();
            byte[] sourceLocal = locals[source];
            int sourceCount = sourceLocal.Length / VertexStride;
            bool created = target == null;
            if (created)
            {
                target = CopyPart(source);
                target.Name = targetName;
                target.Meshes.Clear();
                target.GlassVertices = new byte[0];
                target.GlassIndices = new byte[0];
                locals[target] = new byte[0];
            }

            // the mesh's indices in the target's numbering (same part: unchanged), its vertices copied in first-use order
            List<int> moved = new List<int>(mesh.Indices.Count);
            if (target == source)
            {
                moved.AddRange(mesh.Indices);
            }
            else
            {
                byte[] targetLocal = locals[target];
                int offset = targetLocal.Length / VertexStride;
                double[] sourcePositions = LocalPositions(source, sourceLocal);
                List<double> positions = new List<double>(LocalPositions(target, targetLocal));
                MemoryStream bytes = new MemoryStream();
                bytes.Write(targetLocal, 0, targetLocal.Length);
                Dictionary<int, int> map = new Dictionary<int, int>();
                int targetRestart = RestartOf((into >= 0) ? target.Meshes[into] : mesh);
                foreach (int i in mesh.Indices)
                {
                    if (i < 0 || i >= sourceCount)
                    {
                        moved.Add(targetRestart);
                        continue;
                    }
                    int n;
                    if (!map.TryGetValue(i, out n))
                    {
                        n = offset + map.Count;
                        map[i] = n;
                        bytes.Write(sourceLocal, i * VertexStride, VertexStride);
                        for (int c = 0; c < 3; c++)
                        {
                            positions.Add(sourcePositions[(i * 3) + c]);
                        }
                    }
                    moved.Add(n);
                }
                if (offset + map.Count >= 0xFFFF)
                {
                    throw new InvalidOperationException(target.Name + ": more than 65534 vertices in one FM2 part after the move.");
                }
                byte[] combined = bytes.ToArray();
                EncodePositions(target, combined, positions.ToArray());
                locals[target] = combined;
            }

            Mesh joined = null;
            if (into >= 0)
            {
                joined = target.Meshes[into];
                if (joined.Primitive != mesh.Primitive)
                {
                    throw new InvalidOperationException(string.Format("{0} and {1} are drawn in different ways (strip / list): nothing attached.", mesh.MaterialName, joined.MaterialName));
                }
            }
            else
            {
                foreach (Mesh m in target.Meshes)
                {
                    if (m.MaterialName == mesh.MaterialName && m.Lod == mesh.Lod && m.Primitive == mesh.Primitive)
                    {
                        joined = m;
                        break;
                    }
                }
            }
            string report;
            if (joined != null)
            {
                int restart = RestartOf(joined);
                if (joined.Primitive == 6 && joined.Indices.Count > 0)
                {
                    joined.Indices.Add(restart);
                }
                foreach (int i in moved)
                {
                    joined.Indices.Add((i == RestartOf(mesh) || i == RestartOf(joined)) ? restart : i);
                }
                report = string.Format("{0} joined to {1} of {2}", mesh.MaterialName, joined.MaterialName, target.Name);
            }
            else
            {
                Mesh copy = CopyMesh(mesh);
                copy.Indices = moved;
                target.Meshes.Add(copy);
                report = string.Format("{0} moved into {1}{2}", mesh.MaterialName, target.Name, created ? " (new part)" : "");
            }
            target.HasTransparentMeshes = (byte) (SeeThroughPart(target) ? 1 : 0);
            if (created)
            {
                this.Parts.Add(target);
                this.AddDrawEntries(this.Parts.Count - 1);
            }
            else if (joined == null)
            {
                this.AddDrawEntry(this.Parts.IndexOf(target), target.Meshes.Count - 1);
            }
            this.Rebuild(locals);
            bool last = source.Meshes.Count == 1;
            this.RemoveMeshesAt(source, new List<int> { k });
            return report + (last ? "; " + source.Name + " now empty and removed" : "");
        }

        private static int RestartOf(Mesh mesh)
        {
            return (mesh.IndexSize == 2) ? 0xFFFF : 0x00FFFFFF;
        }

        private void AddDrawEntry(int partIndex, int meshIndex)
        {
            Mesh mesh = this.Parts[partIndex].Meshes[meshIndex];
            if (mesh.Lod < 1 || mesh.Lod > this.LodCount)
            {
                throw new InvalidOperationException(string.Format("{0}.{1}: LOD {2}, this FM2 model has LODs 1-{3}.", this.Parts[partIndex].Name, mesh.MaterialName, mesh.Lod, this.LodCount));
            }
            int list = (IsSeeThrough(mesh.MaterialName) ? 0 : this.LodCount) + (int) mesh.Lod - 1;
            this.DrawLists[list].Add((uint) ((partIndex << 16) | meshIndex));
        }

        // The source's glass shards after the target's (16-byte vertices; u16 big-endian indices moved by the target's count).
        private static void AppendGlass(Part target, Part source)
        {
            if (source.GlassVertices.Length == 0)
            {
                return;
            }
            int offset = target.GlassVertices.Length / 16;
            byte[] vertices = new byte[target.GlassVertices.Length + source.GlassVertices.Length];
            Buffer.BlockCopy(target.GlassVertices, 0, vertices, 0, target.GlassVertices.Length);
            Buffer.BlockCopy(source.GlassVertices, 0, vertices, target.GlassVertices.Length, source.GlassVertices.Length);
            byte[] indices = new byte[target.GlassIndices.Length + source.GlassIndices.Length];
            Buffer.BlockCopy(target.GlassIndices, 0, indices, 0, target.GlassIndices.Length);
            for (int i = 0; i + 1 < source.GlassIndices.Length; i += 2)
            {
                int value = ((source.GlassIndices[i] << 8) | source.GlassIndices[i + 1]) + offset;
                if (value > 0xFFFF)
                {
                    throw new InvalidOperationException(target.Name + ": too many glass shard vertices after the merge.");
                }
                indices[target.GlassIndices.Length + i] = (byte) (value >> 8);
                indices[target.GlassIndices.Length + i + 1] = (byte) value;
            }
            target.GlassVertices = vertices;
            target.GlassIndices = indices;
        }

        private static Mesh CopyMesh(Mesh mesh)
        {
            Mesh m = new Mesh();
            m.MaterialVersion = mesh.MaterialVersion;
            m.Version = mesh.Version;
            m.MaterialName = mesh.MaterialName;
            m.Primitive = mesh.Primitive;
            m.Lod = mesh.Lod;
            m.IndexBufferVersion = mesh.IndexBufferVersion;
            m.IndexSize = mesh.IndexSize;
            m.Indices = new List<int>(mesh.Indices);
            return m;
        }

        // A deep copy of a part (its vertex range is not copied: PartVertices of the source).
        public static Part CopyPart(Part from)
        {
            Part copy = new Part();
            copy.PhysicsVersion = from.PhysicsVersion;
            copy.Bounds = (float[]) from.Bounds.Clone();
            copy.Physics = (byte[]) from.Physics.Clone();
            copy.GlassVersion = from.GlassVersion;
            copy.GlassVertexListVersion = from.GlassVertexListVersion;
            copy.GlassVertices = (byte[]) from.GlassVertices.Clone();
            copy.GlassIndexListVersion = from.GlassIndexListVersion;
            copy.GlassIndices = (byte[]) from.GlassIndices.Clone();
            copy.HasTransparentMeshes = from.HasTransparentMeshes;
            copy.Version = from.Version;
            copy.Name = from.Name;
            copy.Vertices = new VertexBuffer();
            copy.Vertices.Version = from.Vertices.Version;
            copy.Vertices.DataVersion = from.Vertices.DataVersion;
            copy.Vertices.Stride = from.Vertices.Stride;
            copy.MeshListVersion = from.MeshListVersion;
            foreach (Mesh mesh in from.Meshes)
            {
                Mesh m = new Mesh();
                m.MaterialVersion = mesh.MaterialVersion;
                m.Version = mesh.Version;
                m.MaterialName = mesh.MaterialName;
                m.Primitive = mesh.Primitive;
                m.Lod = mesh.Lod;
                m.IndexBufferVersion = mesh.IndexBufferVersion;
                m.IndexSize = mesh.IndexSize;
                m.Indices = new List<int>(mesh.Indices);
                copy.Meshes.Add(m);
            }
            return copy;
        }

        // ---- moving ----

        // Positions decoded from part-local vertex bytes (origin + xyz * w).
        private static double[] LocalPositions(Part part, byte[] local)
        {
            int count = local.Length / VertexStride;
            double[] positions = new double[count * 3];
            for (int i = 0; i < count; i++)
            {
                int o = i * VertexStride;
                double w = Half(local, o + 6);
                for (int c = 0; c < 3; c++)
                {
                    positions[(i * 3) + c] = part.Bounds[c] + (Half(local, o + (c * 2)) * w);
                }
            }
            return positions;
        }

        // New bounds (origin = box centre, min = -max, as every FM2 part) and the positions written as 4 halves.
        private static void EncodePositions(Part part, byte[] local, double[] positions)
        {
            int count = local.Length / VertexStride;
            if (count == 0)
            {
                return;
            }
            double[] min = { double.MaxValue, double.MaxValue, double.MaxValue };
            double[] max = { double.MinValue, double.MinValue, double.MinValue };
            for (int i = 0; i < positions.Length; i++)
            {
                min[i % 3] = Math.Min(min[i % 3], positions[i]);
                max[i % 3] = Math.Max(max[i % 3], positions[i]);
            }
            float[] origin;
            float[] half;
            CarbinModel.SymmetricBounds(min, max, out origin, out half);
            for (int c = 0; c < 3; c++)
            {
                part.Bounds[c] = origin[c];
                part.Bounds[3 + c] = -half[c];
                part.Bounds[6 + c] = half[c];
            }
            for (int i = 0; i < count; i++)
            {
                int o = i * VertexStride;
                double[] d = new double[3];
                double length = 0;
                for (int c = 0; c < 3; c++)
                {
                    d[c] = positions[(i * 3) + c] - origin[c];
                    length += d[c] * d[c];
                }
                length = Math.Sqrt(length);
                for (int c = 0; c < 3; c++)
                {
                    CarbinModel.WriteHalfBig(local, o + (c * 2), (length > 1e-12) ? d[c] / length : 0);
                }
                CarbinModel.WriteHalfBig(local, o + 6, length);
            }
        }

        private static void TurnNormal(byte[] local, int vertex, Matrix normalMatrix)
        {
            int at = (vertex * VertexStride) + VertexNormal;
            Vector3 normal = CarbinModel.ReadPacked111110(local, at);
            if (normal.LengthSquared() < 1e-6f)
            {
                return;
            }
            Vector3 turned = Vector3.TransformNormal(Vector3.Normalize(normal), normalMatrix);
            if (turned.LengthSquared() > 1e-12f)
            {
                CarbinModel.WritePacked111110Big(local, at, Vector3.Normalize(turned));
            }
        }

        // Moves / rotates / scales a whole part: positions, normals (only when the edit turns them), glass shards (car
        // space). UV, damage word, the unknown word, baked lighting and the centre of mass stay as they are.
        private void TransformPartCore(string name, Matrix m)
        {
            Part part = this.Find(name);
            if (part == null)
            {
                return;
            }
            Quaternion rotation;
            Matrix normalMatrix;
            CarbinModel.FrameTransform(m, out rotation, out normalMatrix);
            bool turn = !CarbinModel.KeepsDirectionsOf(rotation, normalMatrix);
            Dictionary<Part, byte[]> locals = this.Locals();
            byte[] local = locals[part];
            double[] positions = LocalPositions(part, local);
            for (int i = 0; i < positions.Length / 3; i++)
            {
                Vector3 p = Vector3.Transform(new Vector3((float) positions[i * 3], (float) positions[(i * 3) + 1], (float) positions[(i * 3) + 2]), m);
                positions[i * 3] = p.X;
                positions[(i * 3) + 1] = p.Y;
                positions[(i * 3) + 2] = p.Z;
                if (turn)
                {
                    TurnNormal(local, i, normalMatrix);
                }
            }
            EncodePositions(part, local, positions);
            for (int g = 0; g + 16 <= part.GlassVertices.Length; g += 16)
            {
                Vector3 p = Vector3.Transform(new Vector3(ReadF32(part.GlassVertices, g), ReadF32(part.GlassVertices, g + 4), ReadF32(part.GlassVertices, g + 8)), m);
                WriteF32(part.GlassVertices, g, p.X);
                WriteF32(part.GlassVertices, g + 4, p.Y);
                WriteF32(part.GlassVertices, g + 8, p.Z);
            }
            this.Rebuild(locals);
        }

        // One material of a part: its vertices shared with other materials of the part get their own copies (appended to
        // the part's range), then only its vertices move. Returns the vertices moved.
        private int TransformMeshCore(string name, string material, Matrix m)
        {
            Part part = this.Find(name);
            if (part == null)
            {
                return 0;
            }
            Quaternion rotation;
            Matrix normalMatrix;
            CarbinModel.FrameTransform(m, out rotation, out normalMatrix);
            bool turn = !CarbinModel.KeepsDirectionsOf(rotation, normalMatrix);
            Dictionary<Part, byte[]> locals = this.Locals();
            byte[] local = locals[part];
            int count = local.Length / VertexStride;
            bool[] mine = new bool[count];
            bool[] others = new bool[count];
            foreach (Mesh mesh in part.Meshes)
            {
                bool[] marks = string.Equals(mesh.MaterialName, material, StringComparison.Ordinal) ? mine : others;
                foreach (int index in mesh.Indices)
                {
                    if (index >= 0 && index < count)
                    {
                        marks[index] = true;
                    }
                }
            }
            Dictionary<int, int> copies = new Dictionary<int, int>();
            MemoryStream grown = new MemoryStream();
            grown.Write(local, 0, local.Length);
            int total = count;
            for (int i = 0; i < count; i++)
            {
                if (mine[i] && others[i])
                {
                    copies[i] = total++;
                    grown.Write(local, i * VertexStride, VertexStride);
                }
            }
            if (total >= 0xFFFF)
            {
                throw new InvalidOperationException(part.Name + ": more than 65534 vertices in one FM2 part after the move.");
            }
            foreach (Mesh mesh in part.Meshes)
            {
                if (!string.Equals(mesh.MaterialName, material, StringComparison.Ordinal))
                {
                    continue;
                }
                for (int k = 0; k < mesh.Indices.Count; k++)
                {
                    int copy;
                    if (copies.TryGetValue(mesh.Indices[k], out copy))
                    {
                        mesh.Indices[k] = copy;
                    }
                }
            }
            local = grown.ToArray();
            double[] positions = LocalPositions(part, local);
            int moved = 0;
            for (int i = 0; i < total; i++)
            {
                bool move = (i < count) ? (mine[i] && !others[i]) : true;
                if (!move)
                {
                    continue;
                }
                Vector3 p = Vector3.Transform(new Vector3((float) positions[i * 3], (float) positions[(i * 3) + 1], (float) positions[(i * 3) + 2]), m);
                positions[i * 3] = p.X;
                positions[(i * 3) + 1] = p.Y;
                positions[(i * 3) + 2] = p.Z;
                if (turn)
                {
                    TurnNormal(local, i, normalMatrix);
                }
                moved++;
            }
            if (moved == 0)
            {
                return 0;
            }
            EncodePositions(part, local, positions);
            locals[part] = local;
            this.Rebuild(locals);
            return moved;
        }

        // Centre and half size of the vertices a material of the part uses (car space). False when it has none.
        public bool MeshBounds(string name, string material, out Vector3 center, out Vector3 half)
        {
            center = Vector3.Zero;
            half = Vector3.Zero;
            Part part = this.Find(name);
            if (part == null)
            {
                return false;
            }
            double[] positions = this.Positions(part);
            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);
            bool any = false;
            foreach (Mesh mesh in part.Meshes)
            {
                if (!string.Equals(mesh.MaterialName, material, StringComparison.Ordinal))
                {
                    continue;
                }
                foreach (int index in mesh.Indices)
                {
                    if (index < 0 || index >= positions.Length / 3)
                    {
                        continue;
                    }
                    Vector3 p = new Vector3((float) positions[index * 3], (float) positions[(index * 3) + 1], (float) positions[(index * 3) + 2]);
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                    any = true;
                }
            }
            if (any)
            {
                center = (min + max) / 2;
                half = (max - min) / 2;
            }
            return any;
        }

        // ---- a part from an FM4-form model (Copy Part into an FM2 car) ----

        // Part 'name' of an FM4-form main model (LODs 1-5: its Lod buffer) as an FM2 part named 'newName', for this car:
        //  - positions as 4 halves around the new bounds; UV0 = - FM4 UV1 (the FM2 -> FM4 rule, reversed), UV1 0 as in every
        //    FM2 file; normal from the FM4 tangent frame; damage word = the FM4 damage table's (same meaning), 000003FF on
        //    parts that do not deform;
        //  - borrowDamage (FH sources, whose words are not FM4 words): the damage word of this car's nearest vertex;
        //  - the 16 baked lighting values: this car's nearest vertex (another part), preferring one that faces the same
        //    way (the donor rule of the FM2 port, reversed);
        //  - the word at +24 (change of the normal of the deformed vertex): 0, "the normal does not change", as the FM3
        //    writer does. FM4 / FH have no such word and a donor vertex's belongs to another shape (2026-11-27);
        //  - UV0 outside FM2's +-25 (user 2026-11-18, choice b): the part's U and / or V moved by a whole number so its
        //    range fits; a tiled texture looks the same. A range wider than 50 cannot be written: refused. 'notes' says
        //    which shift was used.
        // Physics: zero mass, no glass shards (the caller copies the replaced part's when it replaces one).
        public Part FromMotorsport4(CarbinModel source, string name, string newName, bool borrowDamage, List<string> notes, out byte[] vertices)
        {
            return this.FromMotorsport4(source, name, newName, borrowDamage, notes, out vertices, null);
        }

        // preferPart: this car's part whose vertices give the baked lighting first (the part being replaced, else the part
        // of the source's name when this car has one); null: the whole car.
        public Part FromMotorsport4(CarbinModel source, string name, string newName, bool borrowDamage, List<string> notes, out byte[] vertices, string preferPart)
        {
            return this.FromMotorsport4(source, name, newName, borrowDamage, notes, out vertices, preferPart, null, false);
        }

        // replacedPart: the part the converted one takes the place of (null: added); computeLighting: baked lighting computed
        // by rays against this car (else the donor's values).
        public Part FromMotorsport4(CarbinModel source, string name, string newName, bool borrowDamage, List<string> notes, out byte[] vertices, string preferPart, string replacedPart, bool computeLighting)
        {
            CarbinModel.Section section = source.Find(name);
            if (section == null)
            {
                throw new ArgumentException("The source model has no part " + name + ".");
            }
            CarbinModel.VertexBuffer buffer = section.Lod;
            // vertices the LOD 1-5 meshes use, in order; the others are left out
            bool[] used = new bool[buffer.Count];
            CarbinModel.SubSection[] owner = new CarbinModel.SubSection[buffer.Count];
            List<CarbinModel.SubSection> subs = new List<CarbinModel.SubSection>();
            foreach (CarbinModel.SubSection sub in section.SubSections)
            {
                if (sub.Lod == 0)
                {
                    continue;
                }
                subs.Add(sub);
                foreach (int index in sub.Indices)
                {
                    if (index >= 0 && index < buffer.Count)
                    {
                        used[index] = true;
                        if (owner[index] == null)
                        {
                            owner[index] = sub;
                        }
                    }
                }
            }
            if (subs.Count == 0)
            {
                throw new InvalidOperationException(name + ": no LOD 1-5 meshes (an FM2 car has only LODs 1-5).");
            }
            int[] map = new int[buffer.Count];
            int count = 0;
            for (int i = 0; i < buffer.Count; i++)
            {
                map[i] = used[i] ? count++ : -1;
            }
            if (count >= 0xFFFF)
            {
                throw new InvalidOperationException(string.Format("{0}: {1} vertices, an FM2 part holds at most 65534.", name, count));
            }
            double[] all = CarbinModel.Positions(section, buffer);
            double[] positions = new double[count * 3];
            float[][] uv = new float[count][];
            float[] uvMin = { float.MaxValue, float.MaxValue };
            float[] uvMax = { float.MinValue, float.MinValue };
            for (int i = 0; i < buffer.Count; i++)
            {
                if (map[i] < 0)
                {
                    continue;
                }
                int n = map[i];
                positions[n * 3] = all[i * 3];
                positions[(n * 3) + 1] = all[(i * 3) + 1];
                positions[(n * 3) + 2] = all[(i * 3) + 2];
                Vector2 t = CarbinModel.VertexUv(buffer, i, owner[i], 0);
                // carbon fibre: FM4 puts the weave's tiling in the UVs, 250 times FM2's (same parts of the 60 cars both games
                // have: span ratio 250.06 U / 249.99 V over 113 meshes, every other material 1.00; fm2t\uvscale.txt)
                float scale = IsCarbonFibre(owner[i].Name) ? CarbonFibreScale : 1f;
                uv[n] = new float[] { -t.X / scale, -t.Y / scale };
                for (int c = 0; c < 2; c++)
                {
                    uvMin[c] = Math.Min(uvMin[c], uv[n][c]);
                    uvMax[c] = Math.Max(uvMax[c], uv[n][c]);
                }
            }
            // Whole-number UV shift (user choice b), per island: vertices joined by the strips' triangles. One island never
            // gets two shifts, so no vertex is split; an island wider than 50 cannot be written. A part inside +-25 as a
            // whole keeps its UVs exactly.
            // A piece wider than that (a long strip of tiled texture: race wings, race bumpers) gets a shift per triangle,
            // in steps of 20 so neighbours mostly share one; its meshes are written one triangle per strip (same winding)
            // and a vertex on the border of two shifts is written twice. Only a single triangle wider than 50 is refused.
            int[] islandOf = null;
            Dictionary<int, int[]> islandShift = new Dictionary<int, int[]>();
            HashSet<int> wide = new HashSet<int>();
            if (uvMin[0] < -UvLimit || uvMax[0] > UvLimit || uvMin[1] < -UvLimit || uvMax[1] > UvLimit)
            {
                islandOf = Islands(subs, map, count, buffer.Count);
                Dictionary<int, float[]> range = new Dictionary<int, float[]>();
                for (int n = 0; n < count; n++)
                {
                    float[] r;
                    if (!range.TryGetValue(islandOf[n], out r))
                    {
                        r = new float[] { float.MaxValue, float.MaxValue, float.MinValue, float.MinValue };
                        range[islandOf[n]] = r;
                    }
                    for (int c = 0; c < 2; c++)
                    {
                        r[c] = Math.Min(r[c], uv[n][c]);
                        r[2 + c] = Math.Max(r[2 + c], uv[n][c]);
                    }
                }
                foreach (KeyValuePair<int, float[]> pair in range)
                {
                    int[] s = new int[2];
                    bool fits = true;
                    for (int c = 0; c < 2; c++)
                    {
                        float lo = pair.Value[c], hi = pair.Value[2 + c];
                        if (lo >= -UvLimit && hi <= UvLimit)
                        {
                            continue;
                        }
                        s[c] = (int) Math.Round((lo + hi) / 2);
                        fits &= lo - s[c] >= -UvLimit && hi - s[c] <= UvLimit;
                    }
                    if (fits)
                    {
                        islandShift[pair.Key] = s;
                    }
                    else
                    {
                        wide.Add(pair.Key);
                    }
                }
            }

            // The FM2 vertices: (kept vertex, U shift, V shift). Every kept vertex outside the wide pieces first, in order
            // (with nothing wide: the kept vertices themselves, in order).
            List<int> outVertex = new List<int>();
            List<int[]> outShift = new List<int[]>();
            Dictionary<long, int> outIndex = new Dictionary<long, int>();
            Func<int, int[], int> output = delegate(int n, int[] s)
            {
                long key = ((long) n << 32) | ((long) (s[0] & 0xFFFF) << 16) | (long) (s[1] & 0xFFFF);
                int at;
                if (!outIndex.TryGetValue(key, out at))
                {
                    at = outVertex.Count;
                    outIndex[key] = at;
                    outVertex.Add(n);
                    outShift.Add(s);
                }
                return at;
            };
            int[] none = new int[2];
            Func<int, int[]> shiftOf = delegate(int n)
            {
                int[] s;
                return (islandOf != null && islandShift.TryGetValue(islandOf[n], out s)) ? s : none;
            };
            for (int n = 0; n < count; n++)
            {
                if (islandOf == null || !wide.Contains(islandOf[n]))
                {
                    output(n, shiftOf(n));
                }
            }
            List<List<int>> meshIndices = new List<List<int>>();
            int splitTriangles = 0;
            int squeezedTriangles = 0;
            foreach (CarbinModel.SubSection sub in subs)
            {
                List<int> indices = new List<int>();
                bool rewrite = false;
                if (wide.Count > 0)
                {
                    foreach (int index in sub.Indices)
                    {
                        rewrite |= index >= 0 && index < buffer.Count && map[index] >= 0 && wide.Contains(islandOf[map[index]]);
                    }
                }
                if (!rewrite)
                {
                    foreach (int index in sub.Indices)
                    {
                        indices.Add((index >= 0 && index < buffer.Count && map[index] >= 0) ? output(map[index], shiftOf(map[index])) : 0xFFFF);
                    }
                }
                else
                {
                    // the strip's triangles in their winding (odd ones swap the first two corners), one strip each
                    List<int> run = new List<int>();
                    List<int> strip = new List<int>(sub.Indices);
                    strip.Add(-1);
                    foreach (int index in strip)
                    {
                        int n = (index >= 0 && index < buffer.Count) ? map[index] : -1;
                        if (n >= 0)
                        {
                            run.Add(n);
                            continue;
                        }
                        for (int k = 2; k < run.Count; k++)
                        {
                            int a = run[k - 2], b = run[k - 1], c = run[k];
                            if (a == b || b == c || a == c)
                            {
                                continue;
                            }
                            if ((k % 2) == 1)
                            {
                                int t = a;
                                a = b;
                                b = t;
                            }
                            int[] corners = { a, b, c };
                            int[] s = null;
                            if (wide.Contains(islandOf[a]) || wide.Contains(islandOf[b]) || wide.Contains(islandOf[c]))
                            {
                                s = TriangleShift(uv, corners, name, ref squeezedTriangles);
                                splitTriangles++;
                            }
                            if (indices.Count > 0)
                            {
                                indices.Add(0xFFFF);
                            }
                            foreach (int corner in corners)
                            {
                                indices.Add(output(corner, (s != null && wide.Contains(islandOf[corner])) ? s : shiftOf(corner)));
                            }
                        }
                        run.Clear();
                    }
                }
                meshIndices.Add(indices);
            }
            // only vertices a mesh draws (FM2: the highest index is the part's last vertex, Check)
            bool[] referenced = new bool[outVertex.Count];
            foreach (List<int> indices in meshIndices)
            {
                foreach (int index in indices)
                {
                    if (index != 0xFFFF)
                    {
                        referenced[index] = true;
                    }
                }
            }
            int[] renumber = new int[outVertex.Count];
            List<int> keptVertex = new List<int>();
            List<int[]> keptShift = new List<int[]>();
            for (int v = 0; v < outVertex.Count; v++)
            {
                renumber[v] = referenced[v] ? keptVertex.Count : -1;
                if (referenced[v])
                {
                    keptVertex.Add(outVertex[v]);
                    keptShift.Add(outShift[v]);
                }
            }
            if (keptVertex.Count != outVertex.Count)
            {
                foreach (List<int> indices in meshIndices)
                {
                    for (int k = 0; k < indices.Count; k++)
                    {
                        if (indices[k] != 0xFFFF)
                        {
                            indices[k] = renumber[indices[k]];
                        }
                    }
                }
                outVertex = keptVertex;
                outShift = keptShift;
            }
            if (outVertex.Count >= 0xFFFF)
            {
                throw new InvalidOperationException(string.Format("{0}: {1} vertices after fitting its UVs, an FM2 part holds at most 65534.", name, outVertex.Count));
            }
            int shiftedPieces = 0;
            foreach (int[] s in islandShift.Values)
            {
                shiftedPieces += (s[0] != 0 || s[1] != 0) ? 1 : 0;
            }
            if (notes != null && (shiftedPieces > 0 || splitTriangles > 0))
            {
                notes.Add(string.Format("{0}: UVs moved by whole numbers to fit FM2's +-25 ({1} connected piece{2}{3}); a tiled texture looks the same", newName, shiftedPieces, (shiftedPieces == 1) ? "" : "s",
                    (splitTriangles > 0) ? string.Format(", {0} triangles of wider pieces one by one, {1} vertices written twice", splitTriangles, outVertex.Count - count) : ""));
            }
            if (notes != null && squeezedTriangles > 0)
            {
                notes.Add(string.Format("{0}: {1} triangles of a tiled texture wider than FM2's 50: centred, their UVs held at +-25 (fewer repeats on those triangles only)", newName, squeezedTriangles));
            }

            int[] bufferIndex = new int[count];
            for (int i = 0; i < buffer.Count; i++)
            {
                if (map[i] >= 0)
                {
                    bufferIndex[map[i]] = i;
                }
            }
            bool deformable = section.DamageLod.Count == buffer.Count && section.DamageLod.Stride == 4 && section.IsDeformable;
            Donor donor = new Donor(this, preferPart);
            int[] nearest = new int[count];
            byte[][] tail = new byte[count][];     // bytes 24..59 per source vertex (split copies share them)
            for (int n = 0; n < count; n++)
            {
                nearest[n] = -2;
            }
            vertices = new byte[outVertex.Count * VertexStride];
            double[] outPositions = new double[outVertex.Count * 3];
            for (int v = 0; v < outVertex.Count; v++)
            {
                int n = outVertex[v];
                int i = bufferIndex[n];
                int o = v * VertexStride;
                for (int c = 0; c < 3; c++)
                {
                    outPositions[(v * 3) + c] = positions[(n * 3) + c];
                }
                for (int c = 0; c < 2; c++)
                {
                    ushort raw = ForzaVertex.Fm2UvWord(uv[n][c] - outShift[v][c]);
                    vertices[o + VertexUv0 + (c * 2)] = (byte) (raw >> 8);
                    vertices[o + VertexUv0 + (c * 2) + 1] = (byte) raw;
                }
                Vector3 normal = CarbinModel.VertexNormal(buffer, i);
                CarbinModel.WritePacked111110Big(vertices, o + VertexNormal, (normal.LengthSquared() > 1e-12f) ? Vector3.Normalize(normal) : Vector3.UnitY);
                if (nearest[n] == -2)
                {
                    tail[n] = new byte[VertexStride];
                    nearest[n] = donor.Fill(new Vector3((float) positions[n * 3], (float) positions[(n * 3) + 1], (float) positions[(n * 3) + 2]), normal,
                        IsSeeThrough(owner[i].Name), tail[n], 0);
                }
                int near = nearest[n];
                if (near >= 0)
                {
                    Buffer.BlockCopy(tail[n], VertexLighting, vertices, o + VertexLighting, VertexStride - VertexLighting);
                }
                // bytes 24..27 (normal change) stay 0: see the comment of FromMotorsport4
                if (borrowDamage && near >= 0)
                {
                    Buffer.BlockCopy(donor.Data(near), donor.Offset(near) + VertexDamage, vertices, o + VertexDamage, 4);
                }
                else if (deformable)
                {
                    Buffer.BlockCopy(section.DamageLod.Data, i * 4, vertices, o + VertexDamage, 4);
                }
                else
                {
                    vertices[o + VertexDamage + 2] = 0x03;
                    vertices[o + VertexDamage + 3] = 0xFF;
                }
            }

            Part part = new Part();
            Part template = (this.Parts.Count > 0) ? this.Parts[0] : null;
            if (template != null)
            {
                part.PhysicsVersion = template.PhysicsVersion;
                part.GlassVersion = template.GlassVersion;
                part.GlassVertexListVersion = template.GlassVertexListVersion;
                part.GlassIndexListVersion = template.GlassIndexListVersion;
                part.Version = template.Version;
                part.Vertices.Version = template.Vertices.Version;
                part.Vertices.DataVersion = template.Vertices.DataVersion;
                part.Vertices.Stride = template.Vertices.Stride;
                part.MeshListVersion = template.MeshListVersion;
            }
            part.Name = newName;
            for (int k = 0; k < subs.Count; k++)
            {
                Mesh mesh = new Mesh();
                Mesh like = (template != null && template.Meshes.Count > 0) ? template.Meshes[0] : null;
                if (like != null)
                {
                    mesh.MaterialVersion = like.MaterialVersion;
                    mesh.Version = like.Version;
                    mesh.IndexBufferVersion = like.IndexBufferVersion;
                }
                mesh.MaterialName = subs[k].Name;
                mesh.Primitive = 6;
                mesh.Lod = subs[k].Lod;
                mesh.IndexSize = 2;
                mesh.Indices = meshIndices[k];
                part.Meshes.Add(mesh);
            }
            EncodePositions(part, vertices, outPositions);
            if (computeLighting)
            {
                // 2026-11-21: baked lighting computed by rays (Fm2CarbinModelLighting.cs); the donor's values stay only where
                // nothing opaque draws the vertex
                string fitted;
                int lit = this.ComputeLighting(part, vertices, replacedPart, out fitted);
                if (notes != null)
                {
                    notes.Add(string.Format("{0}: baked lighting computed on {1} vertices ({2})", newName, lit, fitted));
                }
            }
            return part;
        }

        // A whole-number shift that puts one triangle's UVs inside +-25: a step of 40 when it fits (neighbours then
        // share it), else the nearest whole number. A triangle wider than 50 (2026-11-23, FM4 458 Italia: grille1 of LOD
        // 3-5, a tiled grille repeated up to 62 times on one triangle) is centred and counted in 'squeezed': the UV writer
        // (ForzaVertex.Fm2UvWord) holds its corners at +-25, so only that triangle shows fewer repeats.
        private static int[] TriangleShift(float[][] uv, int[] corners, string name, ref int squeezed)
        {
            int[] s = new int[2];
            for (int c = 0; c < 2; c++)
            {
                float lo = Math.Min(uv[corners[0]][c], Math.Min(uv[corners[1]][c], uv[corners[2]][c]));
                float hi = Math.Max(uv[corners[0]][c], Math.Max(uv[corners[1]][c], uv[corners[2]][c]));
                float middle = (lo + hi) / 2;
                int coarse = (int) (40 * Math.Round(middle / 40));
                int fine = (int) Math.Round(middle);
                if (lo - coarse >= -UvLimit && hi - coarse <= UvLimit)
                {
                    s[c] = coarse;
                }
                else if (lo - fine >= -UvLimit && hi - fine <= UvLimit)
                {
                    s[c] = fine;
                }
                else
                {
                    s[c] = fine;
                    squeezed++;
                }
            }
            return s;
        }

        // Island number per kept vertex: vertices joined by a strip's consecutive indices (its triangles) share one.
        private static int[] Islands(List<CarbinModel.SubSection> subs, int[] map, int count, int bufferCount)
        {
            int[] parent = new int[count];
            for (int n = 0; n < count; n++)
            {
                parent[n] = n;
            }
            Func<int, int> root = null;
            root = delegate(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            };
            // real triangles only: strips join separate pieces with degenerate triangles (two equal corners)
            foreach (CarbinModel.SubSection sub in subs)
            {
                int a = -1, b = -1;
                foreach (int index in sub.Indices)
                {
                    int c = (index >= 0 && index < bufferCount) ? map[index] : -1;
                    if (c < 0)
                    {
                        a = -1;     // a restart ends the strip
                        b = -1;
                        continue;
                    }
                    if (a >= 0 && b >= 0 && a != b && b != c && a != c)
                    {
                        parent[root(a)] = root(b);
                        parent[root(c)] = root(b);
                    }
                    a = b;
                    b = c;
                }
            }
            int[] island = new int[count];
            for (int n = 0; n < count; n++)
            {
                island[n] = root(n);
            }
            return island;
        }

        // This car's vertices on a 10 cm grid, for the nearest-vertex rule.
        private class Donor
        {
            // 2026-11-21 (user: blotchy shading on a converted bumper): the nearest vertex of the whole car was often another
            // upgrade variant lying in the same place (bumperFa..Fi, Frace) or a coarser LOD, each with its own baked lighting
            // (12.8 % of a same-car bumper's vertices came out darker by > 0.15 in band 0, fm2t\lit.exe). Now: only vertices
            // LOD 1 draws; vertices with all-zero lighting (glass, interior, seats) only for see-through materials; the part
            // being replaced (or the part of the source's name) first, its 4 nearest facing the same way within 10 cm averaged
            // (baked spherical harmonics add up linearly), the exact vertex when one lies within 1 mm.
            private readonly Fm2CarbinModel model;
            private readonly List<int[]> vertices = new List<int[]>();    // part, vertex
            private readonly List<Vector3> positions = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<bool> lit = new List<bool>();
            private readonly List<int> preferred = new List<int>();
            private readonly Dictionary<long, List<int>> gridLit = new Dictionary<long, List<int>>();
            private readonly Dictionary<long, List<int>> gridAll = new Dictionary<long, List<int>>();
            private const float Cell = 0.1f;
            private const float PreferredReach = 0.10f;

            public Donor(Fm2CarbinModel model, string preferPart)
            {
                this.model = model;
                for (int p = 0; p < model.Parts.Count; p++)
                {
                    Part part = model.Parts[p];
                    bool prefer = preferPart != null && string.Equals(part.Name, preferPart, StringComparison.OrdinalIgnoreCase);
                    int count = part.VertexCount;
                    bool[] lod1 = new bool[count];
                    foreach (Mesh mesh in part.Meshes)
                    {
                        if (mesh.Lod != 1)
                        {
                            continue;
                        }
                        foreach (int i in mesh.Indices)
                        {
                            if (i >= 0 && i < count)
                            {
                                lod1[i] = true;
                            }
                        }
                    }
                    double[] pos = model.Positions(part);
                    byte[] data = model.BufferOf(part).Data;
                    for (int v = 0; v < count; v++)
                    {
                        if (!lod1[v])
                        {
                            continue;
                        }
                        int at0 = model.VertexOffset(part, v);
                        bool hasLight = false;
                        for (int k = 0; k < 32 && !hasLight; k++)
                        {
                            hasLight = data[at0 + VertexLighting + k] != 0;
                        }
                        Vector3 at = new Vector3((float) pos[v * 3], (float) pos[(v * 3) + 1], (float) pos[(v * 3) + 2]);
                        int id = this.positions.Count;
                        this.vertices.Add(new int[] { p, v });
                        this.positions.Add(at);
                        this.normals.Add(CarbinModel.ReadPacked111110(data, at0 + VertexNormal));
                        this.lit.Add(hasLight);
                        if (prefer)
                        {
                            this.preferred.Add(id);
                        }
                        long key = Key(at);
                        AddTo(this.gridAll, key, id);
                        if (hasLight)
                        {
                            AddTo(this.gridLit, key, id);
                        }
                    }
                }
            }

            private static void AddTo(Dictionary<long, List<int>> grid, long key, int id)
            {
                List<int> cell;
                if (!grid.TryGetValue(key, out cell))
                {
                    cell = new List<int>();
                    grid[key] = cell;
                }
                cell.Add(id);
            }

            // Bytes 24..59 (the normal-change word and the 16 lighting values) for a vertex at 'at' facing 'normal' into
            // 'dest' (FromMotorsport4 keeps only the lighting). Returns the donor vertex (its damage word is used for FH),
            // -1 when the car has none.
            public int Fill(Vector3 at, Vector3 normal, bool seeThrough, byte[] dest, int offset)
            {
                // the preferred part: nearest facing the same way within reach
                List<KeyValuePair<float, int>> near = new List<KeyValuePair<float, int>>();
                foreach (int id in this.preferred)
                {
                    if (!seeThrough && !this.lit[id])
                    {
                        continue;
                    }
                    float d = (this.positions[id] - at).LengthSquared();
                    if (d <= PreferredReach * PreferredReach && Vector3.Dot(this.normals[id], normal) > 0.5f)
                    {
                        near.Add(new KeyValuePair<float, int>(d, id));
                    }
                }
                int best;
                if (near.Count > 0)
                {
                    near.Sort(delegate(KeyValuePair<float, int> x, KeyValuePair<float, int> y) { return x.Key.CompareTo(y.Key); });
                    best = near[0].Value;
                    if (near.Count > 4)
                    {
                        near.RemoveRange(4, near.Count - 4);
                    }
                }
                else
                {
                    best = this.Nearest(at, normal, seeThrough ? this.gridAll : this.gridLit);
                    if (best < 0 && !seeThrough)
                    {
                        best = this.Nearest(at, normal, this.gridAll);
                    }
                    if (best < 0)
                    {
                        return -1;
                    }
                    near.Add(new KeyValuePair<float, int>((this.positions[best] - at).LengthSquared(), best));
                }
                Buffer.BlockCopy(this.Data(best), this.Offset(best) + VertexUnknown, dest, offset + VertexUnknown, VertexStride - VertexUnknown);
                if (near.Count > 1 && near[0].Key > 1e-6f)
                {
                    double[] sum = new double[16];
                    double weights = 0;
                    foreach (KeyValuePair<float, int> pair in near)
                    {
                        double w = 1.0 / (Math.Sqrt(pair.Key) + 0.005);
                        byte[] data = this.Data(pair.Value);
                        int o = this.Offset(pair.Value) + VertexLighting;
                        for (int k = 0; k < 16; k++)
                        {
                            sum[k] += w * (short) ((data[o + (k * 2)] << 8) | data[o + (k * 2) + 1]);
                        }
                        weights += w;
                    }
                    for (int k = 0; k < 16; k++)
                    {
                        int value = (int) Math.Round(sum[k] / weights);
                        value = Math.Max(-32768, Math.Min(32767, value));
                        dest[offset + VertexLighting + (k * 2)] = (byte) (value >> 8);
                        dest[offset + VertexLighting + (k * 2) + 1] = (byte) value;
                    }
                }
                return best;
            }

            private static long Key(Vector3 p)
            {
                return Key((int) Math.Floor(p.X / Cell), (int) Math.Floor(p.Y / Cell), (int) Math.Floor(p.Z / Cell));
            }

            private static long Key(int x, int y, int z)
            {
                return (((long) (x + 0x8000)) << 32) | (((long) (y + 0x8000)) << 16) | (long) (z + 0x8000);
            }

            // The nearest vertex facing the same way (normals within 90 degrees) within a growing search; the nearest of
            // any facing when none does. -1 when the car has no vertices.
            private int Nearest(Vector3 at, Vector3 normal, Dictionary<long, List<int>> grid)
            {
                if (grid.Count == 0)
                {
                    return -1;
                }
                int x = (int) Math.Floor(at.X / Cell), y = (int) Math.Floor(at.Y / Cell), z = (int) Math.Floor(at.Z / Cell);
                int best = -1, bestAny = -1;
                float bd = float.MaxValue, bdAny = float.MaxValue;
                int firstRing = -1;
                for (int r = 0; r <= 40; r++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        for (int dy = -r; dy <= r; dy++)
                        {
                            for (int dz = -r; dz <= r; dz++)
                            {
                                if (Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz))) != r)
                                {
                                    continue;
                                }
                                List<int> cell;
                                if (!grid.TryGetValue(Key(x + dx, y + dy, z + dz), out cell))
                                {
                                    continue;
                                }
                                foreach (int id in cell)
                                {
                                    float d = (this.positions[id] - at).LengthSquared();
                                    if (d < bdAny)
                                    {
                                        bdAny = d;
                                        bestAny = id;
                                    }
                                    if (d < bd && Vector3.Dot(this.normals[id], normal) > 0)
                                    {
                                        bd = d;
                                        best = id;
                                    }
                                }
                            }
                        }
                    }
                    // a cell ring r away is at least (r - 1) cells far: stop once the best found is closer than that; with no
                    // vertex facing the same way 3 rings after the first vertex at all, the nearest of any facing
                    float reach = Math.Max(0, r - 1) * Cell;
                    if (best >= 0 && bd <= reach * reach)
                    {
                        return best;
                    }
                    if (bestAny >= 0 && firstRing < 0)
                    {
                        firstRing = r;
                    }
                    if (best < 0 && firstRing >= 0 && r >= firstRing + 3)
                    {
                        return bestAny;
                    }
                }
                return (best >= 0) ? best : bestAny;
            }

            public byte[] Data(int id)
            {
                return this.model.BufferOf(this.model.Parts[this.vertices[id][0]]).Data;
            }

            public int Offset(int id)
            {
                return this.model.VertexOffset(this.model.Parts[this.vertices[id][0]], this.vertices[id][1]);
            }
        }

        // ---- big-endian helpers ----

        private static int ReadS32(byte[] d, int o)
        {
            return (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];
        }

        private static void WriteS32(byte[] d, int o, int value)
        {
            d[o] = (byte) (value >> 24);
            d[o + 1] = (byte) (value >> 16);
            d[o + 2] = (byte) (value >> 8);
            d[o + 3] = (byte) value;
        }

        private static float ReadF32(byte[] d, int o)
        {
            return BitConverter.ToSingle(new byte[] { d[o + 3], d[o + 2], d[o + 1], d[o] }, 0);
        }

        private static void WriteF32(byte[] d, int o, float value)
        {
            byte[] b = BitConverter.GetBytes(value);
            d[o] = b[3];
            d[o + 1] = b[2];
            d[o + 2] = b[1];
            d[o + 3] = b[0];
        }
    }
}
