namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Microsoft.Xna.Framework;

    // Import Model from an OBJ folder on FM2 cars (user 2026-11-21: "import obj su fm2 e fm3 non è abilitato").
    // One part rebuilt in FM2's own format from the OBJ of each LOD (CarModelFolder.ExportLegacy wrote them: positions in
    // model space, vt = (u, 1 - v) in the units of FM2's UV words, one group per material):
    //  - a LOD with no OBJ to rebuild and not dropped keeps its meshes and vertices as they are;
    //  - an OBJ corner at the place, with the normal and the UV of a vertex the part had keeps that vertex, every byte
    //    (the untouched areas of an edited part stay the game's);
    //  - a new vertex: position (4 halves against the part's origin, which stays), UV words, normal; the damage word
    //    and the word at +24 from the nearest vertex the part had (a new part: of its template); the 16 baked light
    //    values computed on the car (Fm2CarbinModelLighting.cs);
    //  - triangles wound as this car's own triangles are against their normals.
    public partial class Fm2CarbinModel
    {
        private struct ObjVertexKey : IEquatable<ObjVertexKey>
        {
            public Vector3 P, N;
            public ushort U, V;

            public bool Equals(ObjVertexKey other)
            {
                return this.P == other.P && this.N == other.N && this.U == other.U && this.V == other.V;
            }

            public override bool Equals(object other)
            {
                return other is ObjVertexKey && this.Equals((ObjVertexKey) other);
            }

            public override int GetHashCode()
            {
                return this.P.GetHashCode() ^ (this.N.GetHashCode() * 31) ^ (this.U << 16) ^ this.V;
            }
        }

        // True when the game's triangles (as MeshTriangles lists them) turn against their vertex normals: counted on
        // this car's LOD 1 meshes.
        private bool WindingAgainstNormals()
        {
            long with = 0, against = 0;
            foreach (Part part in this.Parts)
            {
                byte[] data = this.PartVertices(part);
                double[] positions = this.Positions(part);
                int count = part.VertexCount;
                foreach (Mesh mesh in part.Meshes)
                {
                    if (mesh.Lod != 1)
                    {
                        continue;
                    }
                    foreach (int[] f in MeshTriangles(mesh, count))
                    {
                        Vector3 a = At(positions, f[0]), b = At(positions, f[1]), c = At(positions, f[2]);
                        Vector3 n = CarbinModel.ReadPacked111110(data, (f[0] * VertexStride) + VertexNormal) + CarbinModel.ReadPacked111110(data, (f[1] * VertexStride) + VertexNormal)
                            + CarbinModel.ReadPacked111110(data, (f[2] * VertexStride) + VertexNormal);
                        float d = Vector3.Dot(Vector3.Cross(b - a, c - a), n);
                        if (d > 0)
                        {
                            with++;
                        }
                        else if (d < 0)
                        {
                            against++;
                        }
                    }
                }
            }
            return against > with;
        }

        private static long ObjCell(double x, double y, double z, int dx, int dy, int dz)
        {
            const double cell = 0.001;
            long a = (long) Math.Floor(x / cell) + dx + 500000, b = (long) Math.Floor(y / cell) + dy + 500000, c = (long) Math.Floor(z / cell) + dz + 500000;
            return (a * 1000000L * 1000000L) + (b * 1000000L) + c;
        }

        // The nearest of the points to p: the cells around p first, every point when they are empty. -1: no points.
        private static int NearestOf(double[] points, int count, Dictionary<long, List<int>> grid, Vector3 p)
        {
            int best = -1;
            double bestDistance = double.MaxValue;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        List<int> list;
                        if (!grid.TryGetValue(ObjCell(p.X, p.Y, p.Z, dx, dy, dz), out list))
                        {
                            continue;
                        }
                        foreach (int i in list)
                        {
                            double ex = points[i * 3] - p.X, ey = points[(i * 3) + 1] - p.Y, ez = points[(i * 3) + 2] - p.Z;
                            double d = (ex * ex) + (ey * ey) + (ez * ez);
                            if (d < bestDistance)
                            {
                                bestDistance = d;
                                best = i;
                            }
                        }
                    }
                }
            }
            if (best >= 0)
            {
                return best;
            }
            for (int i = 0; i < count; i++)
            {
                double ex = points[i * 3] - p.X, ey = points[(i * 3) + 1] - p.Y, ez = points[(i * 3) + 2] - p.Z;
                double d = (ex * ex) + (ey * ey) + (ez * ez);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }
            return best;
        }

        // The part 'name' (new when the model has none: then 'template' gives its settings) from the OBJ meshes of the LODs
        // in 'rebuild'; the LODs in 'drop' go; the other LODs stay. Returns a report.
        public string RebuildFromObj(string name, string template, IDictionary<uint, ObjMesh> rebuild, ICollection<uint> drop)
        {
            Part old = this.Find(name);
            Part like = old ?? this.Find(template);
            if (like == null)
            {
                throw new InvalidOperationException(name + ": no part to take the settings from.");
            }
            foreach (uint lod in rebuild.Keys)
            {
                if (lod < 1 || lod > 5)
                {
                    throw new InvalidOperationException(name + ": Forza Motorsport 2 cars have LODs 1 to 5 (lod" + lod + " cannot be imported).");
                }
            }
            if (like.Meshes.Count == 0)
            {
                throw new InvalidOperationException(like.Name + " has no mesh to use as a template.");
            }
            byte[] likeData = this.PartVertices(like);
            double[] likePositions = this.Positions(like);
            int likeCount = likeData.Length / VertexStride;
            bool against = this.WindingAgainstNormals();

            Part part = CopyPart(like);
            part.Name = (old != null) ? old.Name : name;
            part.Meshes = new List<Mesh>();
            if (old == null)
            {
                part.GlassVertices = new byte[0];
                part.GlassIndices = new byte[0];
            }
            double[] origin = { part.Bounds[0], part.Bounds[1], part.Bounds[2] };
            MemoryStream data = new MemoryStream();
            List<bool> kept = new List<bool>();
            int count = 0;
            int keptCount = 0;

            // the LODs that stay: their meshes, with the vertices they draw
            Dictionary<int, int> carried = new Dictionary<int, int>();
            if (old != null)
            {
                foreach (Mesh mesh in old.Meshes)
                {
                    if (rebuild.ContainsKey(mesh.Lod) || (drop != null && drop.Contains(mesh.Lod)))
                    {
                        continue;
                    }
                    Mesh copy = CopyMesh(mesh);
                    for (int i = 0; i < copy.Indices.Count; i++)
                    {
                        int v = copy.Indices[i];
                        if (v < 0 || v >= likeCount)
                        {
                            continue;       // a strip restart
                        }
                        int to;
                        if (!carried.TryGetValue(v, out to))
                        {
                            to = count++;
                            carried[v] = to;
                            data.Write(likeData, v * VertexStride, VertexStride);
                            kept.Add(true);
                        }
                        copy.Indices[i] = to;
                    }
                    part.Meshes.Add(copy);
                }
            }

            // the vertices the part had, by place (same-vertex and nearest lookups)
            Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
            for (int i = 0; i < likeCount; i++)
            {
                long key = ObjCell(likePositions[i * 3], likePositions[(i * 3) + 1], likePositions[(i * 3) + 2], 0, 0, 0);
                List<int> list;
                if (!grid.TryGetValue(key, out list))
                {
                    list = new List<int>();
                    grid[key] = list;
                }
                list.Add(i);
            }

            Dictionary<ObjVertexKey, int> made = new Dictionary<ObjVertexKey, int>();
            int triangles = 0, flipped = 0, fresh = 0;
            List<uint> lods = new List<uint>(rebuild.Keys);
            lods.Sort();
            foreach (uint lod in lods)
            {
                ObjMesh mesh = rebuild[lod];
                // corners without a normal: the sum of the normals of the faces at that place
                Dictionary<Vector3, Vector3> faceNormals = new Dictionary<Vector3, Vector3>();
                foreach (ObjMesh.Material material in mesh.Materials)
                {
                    foreach (ObjMesh.Corner[] t in material.Triangles)
                    {
                        Vector3 n = Vector3.Cross(t[1].Position - t[0].Position, t[2].Position - t[0].Position);
                        foreach (ObjMesh.Corner corner in t)
                        {
                            Vector3 sum;
                            faceNormals.TryGetValue(corner.Position, out sum);
                            faceNormals[corner.Position] = sum + n;
                        }
                    }
                }
                foreach (ObjMesh.Material material in mesh.Materials)
                {
                    if (material.Triangles.Count == 0)
                    {
                        continue;
                    }
                    Mesh pattern = null;
                    foreach (Mesh candidate in like.Meshes)
                    {
                        if (candidate.MaterialName == material.Name && (pattern == null || candidate.Lod == lod))
                        {
                            pattern = candidate;
                        }
                    }
                    Mesh target = CopyMesh(pattern ?? like.Meshes[0]);
                    target.MaterialName = material.Name;
                    target.Lod = lod;
                    target.Primitive = 6;
                    target.IndexSize = 2;
                    target.Indices = new List<int>();
                    foreach (ObjMesh.Corner[] t in material.Triangles)
                    {
                        int[] ids = new int[3];
                        Vector3 normalSum = Vector3.Zero;
                        for (int c = 0; c < 3; c++)
                        {
                            ObjMesh.Corner corner = t[c];
                            Vector3 n = corner.HasNormal ? corner.Normal : faceNormals[corner.Position];
                            n = (n.LengthSquared() > 1e-20f) ? Vector3.Normalize(n) : Vector3.UnitY;
                            normalSum += n;
                            float u = corner.HasUv ? corner.Uv.X : 0f, w = corner.HasUv ? 1f - corner.Uv.Y : 0f;
                            // the words reach from -25 (word 0) to +24.9992 (word 65535)
                            if (u < -25.0004f || w < -25.0004f || u > UvLimit + 0.0004 || w > UvLimit + 0.0004)
                            {
                                throw new InvalidOperationException(string.Format("{0}, LOD {1}, {2}: Forza Motorsport 2 stores UVs within +-25 and this OBJ reaches {3:0.#}.",
                                    name, lod, material.Name, Math.Max(Math.Abs(u), Math.Abs(w))));
                            }
                            ObjVertexKey key = new ObjVertexKey();
                            key.P = corner.Position;
                            key.N = n;
                            key.U = ForzaVertex.Fm2UvWord(0.0 - u);
                            key.V = ForzaVertex.Fm2UvWord(0.0 - w);
                            int id;
                            if (!made.TryGetValue(key, out id))
                            {
                                id = count++;
                                made[key] = id;
                                byte[] vertex = new byte[VertexStride];
                                // the vertex the part had here (place within 0.1 mm, normal within 1 degree, the same UV words)
                                int same = -1;
                                for (int dx = -1; dx <= 1 && same < 0; dx++)
                                {
                                    for (int dy = -1; dy <= 1 && same < 0; dy++)
                                    {
                                        for (int dz = -1; dz <= 1 && same < 0; dz++)
                                        {
                                            List<int> list;
                                            if (!grid.TryGetValue(ObjCell(key.P.X, key.P.Y, key.P.Z, dx, dy, dz), out list))
                                            {
                                                continue;
                                            }
                                            foreach (int i in list)
                                            {
                                                double ex = likePositions[i * 3] - key.P.X, ey = likePositions[(i * 3) + 1] - key.P.Y, ez = likePositions[(i * 3) + 2] - key.P.Z;
                                                if ((ex * ex) + (ey * ey) + (ez * ez) > 1e-8)
                                                {
                                                    continue;
                                                }
                                                int o = i * VertexStride;
                                                if (((likeData[o + VertexUv0] << 8) | likeData[o + VertexUv0 + 1]) != key.U || ((likeData[o + VertexUv0 + 2] << 8) | likeData[o + VertexUv0 + 3]) != key.V)
                                                {
                                                    continue;
                                                }
                                                Vector3 theirs = CarbinModel.ReadPacked111110(likeData, o + VertexNormal);
                                                if (theirs.LengthSquared() > 1e-12f && Vector3.Dot(Vector3.Normalize(theirs), n) > 0.99985f)
                                                {
                                                    same = i;
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                }
                                if (same >= 0 && old != null)
                                {
                                    Buffer.BlockCopy(likeData, same * VertexStride, vertex, 0, VertexStride);
                                    kept.Add(true);
                                    keptCount++;
                                }
                                else
                                {
                                    int nearest = NearestOf(likePositions, likeCount, grid, key.P);
                                    if (nearest >= 0)
                                    {
                                        Buffer.BlockCopy(likeData, nearest * VertexStride, vertex, 0, VertexStride);
                                    }
                                    double[] d = { key.P.X - origin[0], key.P.Y - origin[1], key.P.Z - origin[2] };
                                    double length = Math.Sqrt((d[0] * d[0]) + (d[1] * d[1]) + (d[2] * d[2]));
                                    for (int axis = 0; axis < 3; axis++)
                                    {
                                        CarbinModel.WriteHalfBig(vertex, axis * 2, (length > 1e-12) ? d[axis] / length : 0);
                                    }
                                    CarbinModel.WriteHalfBig(vertex, 6, length);
                                    vertex[VertexUv0] = (byte) (key.U >> 8);
                                    vertex[VertexUv0 + 1] = (byte) key.U;
                                    vertex[VertexUv0 + 2] = (byte) (key.V >> 8);
                                    vertex[VertexUv0 + 3] = (byte) key.V;
                                    Array.Clear(vertex, VertexUv1, 4);
                                    CarbinModel.WritePacked111110Big(vertex, VertexNormal, n);
                                    kept.Add(false);
                                    fresh++;
                                }
                                data.Write(vertex, 0, VertexStride);
                            }
                            ids[c] = id;
                        }
                        if (ids[0] == ids[1] || ids[1] == ids[2] || ids[0] == ids[2])
                        {
                            continue;
                        }
                        // front face as the normals say, then wound as the game winds its own
                        bool front = Vector3.Dot(Vector3.Cross(t[1].Position - t[0].Position, t[2].Position - t[0].Position), normalSum) >= 0;
                        if (!front)
                        {
                            flipped++;
                        }
                        if (front == against)
                        {
                            int swap = ids[0];
                            ids[0] = ids[1];
                            ids[1] = swap;
                        }
                        if (target.Indices.Count > 0)
                        {
                            target.Indices.Add(0xFFFF);
                        }
                        target.Indices.AddRange(ids);
                        triangles++;
                    }
                    if (target.Indices.Count > 0)
                    {
                        part.Meshes.Add(target);
                    }
                }
            }
            if (part.Meshes.Count == 0)
            {
                if (old != null)
                {
                    this.RemovePart(old.Name);
                    return name + ": removed (no LOD left)";
                }
                return name + ": nothing to build";
            }
            if (count >= 0xFFFF)
            {
                throw new InvalidOperationException(string.Format("{0}: {1} vertices; one FM2 part holds at most 65534.", name, count));
            }
            byte[] local = data.ToArray();
            // the box: the origin stays, the half sizes hold every vertex (min = - max)
            double[] positions = LocalPositions(part, local);
            float[] half = new float[3];
            for (int i = 0; i < count; i++)
            {
                for (int axis = 0; axis < 3; axis++)
                {
                    half[axis] = Math.Max(half[axis], (float) Math.Abs(positions[(i * 3) + axis] - origin[axis]));
                }
            }
            for (int axis = 0; axis < 3; axis++)
            {
                part.Bounds[3 + axis] = 0f - half[axis];
                part.Bounds[6 + axis] = half[axis];
            }
            // baked light: computed for the part in its place, then the kept vertices get the game's values back
            string light = "";
            if (fresh > 0)
            {
                byte[] before = (byte[]) local.Clone();
                string fitted;
                int lit = this.ComputeLighting(part, local, (old != null) ? old.Name : null, out fitted);
                int computed = 0;
                for (int i = 0; i < count; i++)
                {
                    if (kept[i])
                    {
                        Buffer.BlockCopy(before, (i * VertexStride) + VertexLighting, local, (i * VertexStride) + VertexLighting, 32);
                    }
                    else
                    {
                        computed++;
                    }
                }
                light = string.Format("; baked light computed on the {0} new vertices ({1})", Math.Min(computed, lit), fitted);
            }
            if (old != null)
            {
                this.ReplacePart(old.Name, part, local);
            }
            else
            {
                this.AddPart(part, local);
            }
            List<string> lodText = new List<string>();
            foreach (uint lod in lods)
            {
                lodText.Add(lod.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            return string.Format("{0}{1}: LOD {2} rebuilt, {3} triangles, {4} vertices ({5} unchanged, kept as the game has them; {6} new){7}{8}",
                (old == null) ? "NEW " : "", part.Name, string.Join(",", lodText.ToArray()), triangles, count, keptCount + carried.Count, fresh,
                (flipped > 0) ? "; " + flipped + " faces turned to face their normals" : "", light);
        }
    }
}
