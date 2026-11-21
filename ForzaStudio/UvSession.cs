namespace ForzaStudio
{
    using Microsoft.Xna.Framework;
    using System;
    using System.Collections.Generic;

    // The UV mapping of some meshes (material + LOD) of a carbin being edited: the UVs of one channel
    // (UV1 or UV2) as floats, moved, rotated, scaled by the UV mapper, and written back into the vertices
    // in the file's own encoding:
    //  - FM4 / FH: 16-bit, inside the mesh's own offset / scale, widened when the new UVs do not fit;
    //  - FM3 (model opened in its own layout): the UV itself as halves, no range per mesh;
    //  - FM2 (Fm2CarbinModel): one channel, offset-binary words, +-25 (Fm2CarbinModelUv.cs).
    // UVs here are as the game samples them: V grows downward (the OBJ's vt is 1 - V).
    // A vertex is shared by every face that uses it, so editing some faces of a mesh first gives them
    // copies of the vertices the other faces still use ("keep separately").
    public sealed class UvSession
    {
        public enum Start { Current, OtherChannel, World, View }

        // One mesh of the file being edited, whatever its game.
        internal interface ISource
        {
            string Label { get; }

            // The UV channels its vertices hold (1 or 2).
            int Sets { get; }

            // The vertices its triangles index (the whole buffer they point into).
            int VertexCount { get; }

            List<int[]> Triangles();

            double[] Positions();

            Vector2 Uv(int v, int set);

            Vector3 Normal(int v);

            // Faces being edited that touch a vertex some other face uses (in this mesh or another mesh of the same
            // vertices) get a copy of it: part.Faces and the mesh's indices are rewritten when any copy was made.
            void Split(Part part, bool[] editing);

            // part.U / part.V of channel 'set' into the vertices.
            void Write(int set, Part part);
        }

        public sealed class Part
        {
            public int Section, Sub;
            internal ISource Source;
            public List<int[]> Faces;
            public List<int> EditFaces;
            public int[] EditVerts;
            public int[] UsedVerts;
            public float[] U, V, U0, V0;
            public double[] Positions;
            public string Label;
            private bool[] editFlags;

            // True when face f is one of the faces being edited (EditFaces is fixed once the session is built).
            public bool IsEditFace(int f)
            {
                if (this.editFlags == null)
                {
                    this.editFlags = new bool[this.Faces.Count];
                    foreach (int e in this.EditFaces)
                    {
                        this.editFlags[e] = true;
                    }
                }
                return this.editFlags[f];
            }
        }

        public readonly int Set;
        public readonly List<Part> Parts = new List<Part>();
        private Func<byte[]> toBytes;

        private UvSession(int set)
        {
            this.Set = set;
        }

        // The file with the mapping as Commit last wrote it.
        public byte[] ToBytes()
        {
            return this.toBytes();
        }

        // FM4 / FH, or FM3 opened in its own layout. 'meshes': section / mesh numbers with the faces to edit
        // (null = every face of the mesh).
        public static UvSession Create(CarbinModel model, List<int[]> meshes, List<List<int>> faces, int set, bool splitShared)
        {
            UvSession session = new UvSession(set);
            session.toBytes = delegate { return model.ToBytes(); };
            List<ISource> sources = new List<ISource>();
            foreach (int[] m in meshes)
            {
                CarbinModel.Section s = model.Sections[m[0]];
                sources.Add(new CarbinSource(s, s.SubSections[m[1]]));
            }
            session.Build(sources, meshes, faces, splitShared);
            return session;
        }

        // FM2. 'meshes': the viewer's section / mesh numbers; 'located': the part / mesh numbers of each in the model.
        public static UvSession Create(Fm2CarbinModel model, List<int[]> meshes, List<int[]> located, List<List<int>> faces, int set, bool splitShared)
        {
            UvSession session = new UvSession(set);
            Fm2CarbinModel.UvWork work = model.BeginUv();
            session.toBytes = delegate { return work.ToBytes(); };
            List<ISource> sources = new List<ISource>();
            foreach (int[] m in located)
            {
                Fm2CarbinModel.Part part = model.Parts[m[0]];
                sources.Add(new Motorsport2Source(work, part, part.Meshes[m[1]]));
            }
            session.Build(sources, meshes, faces, splitShared);
            return session;
        }

        private void Build(List<ISource> sources, List<int[]> meshes, List<List<int>> faces, bool splitShared)
        {
            for (int m = 0; m < meshes.Count; m++)
            {
                ISource source = sources[m];
                if (this.Set >= source.Sets)
                {
                    throw new InvalidOperationException(source.Label + ": these vertices hold UV1 only.");
                }
                Part part = new Part();
                part.Section = meshes[m][0];
                part.Sub = meshes[m][1];
                part.Source = source;
                part.Label = source.Label;
                part.Faces = source.Triangles();
                part.EditFaces = new List<int>();
                bool[] editing = new bool[part.Faces.Count];
                if (faces[m] == null)
                {
                    for (int f = 0; f < part.Faces.Count; f++)
                    {
                        editing[f] = true;
                    }
                }
                else
                {
                    foreach (int f in faces[m])
                    {
                        if (f >= 0 && f < part.Faces.Count)
                        {
                            editing[f] = true;
                        }
                    }
                }
                for (int f = 0; f < part.Faces.Count; f++)
                {
                    if (editing[f])
                    {
                        part.EditFaces.Add(f);
                    }
                }
                if (splitShared)
                {
                    source.Split(part, editing);
                }
                part.Positions = source.Positions();
                Dictionary<int, bool> used = new Dictionary<int, bool>(), edit = new Dictionary<int, bool>();
                for (int f = 0; f < part.Faces.Count; f++)
                {
                    foreach (int v in part.Faces[f])
                    {
                        used[v] = true;
                        if (editing[f])
                        {
                            edit[v] = true;
                        }
                    }
                }
                part.UsedVerts = new int[used.Count];
                used.Keys.CopyTo(part.UsedVerts, 0);
                part.EditVerts = new int[edit.Count];
                edit.Keys.CopyTo(part.EditVerts, 0);
                int count = source.VertexCount;
                part.U = new float[count];
                part.V = new float[count];
                foreach (int v in part.UsedVerts)
                {
                    Vector2 uv = source.Uv(v, this.Set);
                    part.U[v] = uv.X;
                    part.V[v] = uv.Y;
                }
                part.U0 = (float[]) part.U.Clone();
                part.V0 = (float[]) part.V.Clone();
                this.Parts.Add(part);
            }
            // Meshes of one part share its vertices, and a later mesh's split can have added vertices after an
            // earlier one was read: every mesh sees them as they ended up.
            foreach (Part part in this.Parts)
            {
                int count = part.Source.VertexCount;
                part.Positions = part.Source.Positions();
                part.U = Grow(part.U, count);
                part.V = Grow(part.V, count);
                part.U0 = Grow(part.U0, count);
                part.V0 = Grow(part.V0, count);
            }
        }

        private static float[] Grow(float[] values, int count)
        {
            if (values.Length >= count)
            {
                return values;
            }
            float[] grown = new float[count];
            Array.Copy(values, grown, values.Length);
            return grown;
        }

        // The vertices of the faces being edited that other faces use too, each replaced in those faces by a copy
        // ('others': how many other faces use each vertex; 'copyVertex': appends a copy, returns its number).
        // Returns true when any copy was made (part.Faces then holds the new numbers).
        private static bool SplitFaces(Part part, int[] others, Func<int, int> copyVertex)
        {
            Dictionary<int, int> copies = new Dictionary<int, int>();
            foreach (int f in part.EditFaces)
            {
                int[] face = part.Faces[f];
                for (int c = 0; c < 3; c++)
                {
                    int v = face[c];
                    if (v >= others.Length || others[v] == 0)
                    {
                        continue;
                    }
                    int copy;
                    if (!copies.TryGetValue(v, out copy))
                    {
                        copy = copyVertex(v);
                        copies[v] = copy;
                    }
                    face[c] = copy;
                }
            }
            return copies.Count > 0;
        }

        // FM4 / FH meshes, and FM3 ones in their own layout (CarbinModel.LayoutUv...).
        private sealed class CarbinSource : ISource
        {
            private readonly CarbinModel.Section s;
            private readonly CarbinModel.SubSection sub;
            private readonly bool lod0;

            public CarbinSource(CarbinModel.Section s, CarbinModel.SubSection sub)
            {
                this.s = s;
                this.sub = sub;
                this.lod0 = sub.Lod == 0;
            }

            private CarbinModel.VertexBuffer Buffer
            {
                get { return this.lod0 ? this.s.Lod0 : this.s.Lod; }
            }

            public string Label
            {
                get { return this.s.Name + " / " + this.sub.Name; }
            }

            public int Sets
            {
                get { return CarbinModel.LayoutUvSets(this.s, this.Buffer); }
            }

            public int VertexCount
            {
                get { return this.Buffer.Count; }
            }

            public List<int[]> Triangles()
            {
                return CarbinModel.StripTriangles(this.sub);
            }

            public double[] Positions()
            {
                return CarbinModel.Positions(this.s, this.Buffer);
            }

            public Vector2 Uv(int v, int set)
            {
                return CarbinModel.LayoutUv(this.s, this.Buffer, v, this.sub, set);
            }

            public Vector3 Normal(int v)
            {
                return CarbinModel.LayoutNormal(this.s, this.Buffer, v);
            }

            public void Split(Part part, bool[] editing)
            {
                int[] others = new int[this.Buffer.Count];
                foreach (CarbinModel.SubSection other in this.s.SubSections)
                {
                    if ((other.Lod == 0) != this.lod0)
                    {
                        continue;
                    }
                    if (other == this.sub)
                    {
                        for (int f = 0; f < part.Faces.Count; f++)
                        {
                            if (!editing[f])
                            {
                                foreach (int v in part.Faces[f])
                                {
                                    others[v]++;
                                }
                            }
                        }
                    }
                    else
                    {
                        foreach (int[] face in CarbinModel.StripTriangles(other))
                        {
                            foreach (int v in face)
                            {
                                others[v]++;
                            }
                        }
                    }
                }
                if (SplitFaces(part, others, delegate(int v) { return CarbinModel.CopyVertexToEnd(this.s, this.lod0, v); }))
                {
                    this.sub.Indices = CarbinModel.TriangleStrips(this.sub, part.Faces, delegate(int i) { return true; });
                }
            }

            public void Write(int set, Part part)
            {
                CarbinModel.VertexBuffer buffer = this.Buffer;
                int o = set * 4;
                float[] range = this.sub.Uv;
                if (CarbinModel.IsMotorsport3Buffer(this.s, buffer))
                {
                    // FM3 vertices hold the UV itself as halves (no range per mesh): only the vertices of the edited
                    // faces are written, the others keep their bytes.
                    foreach (int v in part.EditVerts)
                    {
                        CarbinModel.WriteLayoutUv(this.s, buffer, v, this.sub, set, part.U[v], part.V[v]);
                    }
                    return;
                }
                // A mesh keeps its offset / scale when the UVs fit in it; otherwise the range is widened to the UVs' own
                // min / max.
                float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
                foreach (int v in part.UsedVerts)
                {
                    minU = Math.Min(minU, part.U[v]);
                    maxU = Math.Max(maxU, part.U[v]);
                    minV = Math.Min(minV, part.V[v]);
                    maxV = Math.Max(maxV, part.V[v]);
                }
                const float slack = 1e-5f;
                if (minU < range[o] - slack || maxU > range[o] + range[o + 1] + slack || range[o + 1] < 1e-6f)
                {
                    range[o] = minU;
                    range[o + 1] = Math.Max(maxU - minU, 1e-4f);
                }
                if (minV < range[o + 2] - slack || maxV > range[o + 2] + range[o + 3] + slack || range[o + 3] < 1e-6f)
                {
                    range[o + 2] = minV;
                    range[o + 3] = Math.Max(maxV - minV, 1e-4f);
                }
                byte[] data = buffer.Data;
                int stride = buffer.Stride;
                foreach (int v in part.UsedVerts)
                {
                    int u16 = Raw((part.U[v] - range[o]) / range[o + 1]);
                    int v16 = Raw((part.V[v] - range[o + 2]) / range[o + 3]);
                    int at = (v * stride) + 8 + (set * 4);
                    data[at] = (byte) (u16 >> 8);
                    data[at + 1] = (byte) u16;
                    data[at + 2] = (byte) (v16 >> 8);
                    data[at + 3] = (byte) v16;
                }
            }

            private static int Raw(float unit)
            {
                return Math.Max(0, Math.Min(65535, (int) Math.Round(unit * 65535f)));
            }
        }

        // FM2 meshes: the part's own vertices (every mesh and LOD of the part indexes them), one UV channel.
        private sealed class Motorsport2Source : ISource
        {
            private readonly Fm2CarbinModel.UvWork work;
            private readonly Fm2CarbinModel.Part part;
            private readonly Fm2CarbinModel.Mesh mesh;

            public Motorsport2Source(Fm2CarbinModel.UvWork work, Fm2CarbinModel.Part part, Fm2CarbinModel.Mesh mesh)
            {
                this.work = work;
                this.part = part;
                this.mesh = mesh;
            }

            public string Label
            {
                get { return this.part.Name + " / " + this.mesh.MaterialName; }
            }

            public int Sets
            {
                get { return 1; }
            }

            public int VertexCount
            {
                get { return this.work.VertexCount(this.part); }
            }

            public List<int[]> Triangles()
            {
                return this.work.Triangles(this.part, this.mesh);
            }

            public double[] Positions()
            {
                return this.work.Positions(this.part);
            }

            public Vector2 Uv(int v, int set)
            {
                return (set == 0) ? this.work.Uv(this.part, v) : Vector2.Zero;
            }

            public Vector3 Normal(int v)
            {
                return this.work.Normal(this.part, v);
            }

            public void Split(Part part, bool[] editing)
            {
                int[] others = new int[this.VertexCount];
                foreach (Fm2CarbinModel.Mesh other in this.part.Meshes)
                {
                    if (other == this.mesh)
                    {
                        for (int f = 0; f < part.Faces.Count; f++)
                        {
                            if (!editing[f])
                            {
                                foreach (int v in part.Faces[f])
                                {
                                    others[v]++;
                                }
                            }
                        }
                    }
                    else
                    {
                        foreach (int[] face in this.work.Triangles(this.part, other))
                        {
                            foreach (int v in face)
                            {
                                others[v]++;
                            }
                        }
                    }
                }
                if (SplitFaces(part, others, delegate(int v) { return this.work.CopyVertex(this.part, v); }))
                {
                    this.work.SetTriangles(this.mesh, part.Faces);
                }
            }

            public void Write(int set, Part part)
            {
                foreach (int v in part.EditVerts)
                {
                    this.work.WriteUv(this.part, v, part.U[v], part.V[v]);
                }
            }
        }

        public int EditVertexCount
        {
            get
            {
                int n = 0;
                foreach (Part part in this.Parts)
                {
                    n += part.EditVerts.Length;
                }
                return n;
            }
        }

        // ---- first mapping ----

        public void MapFromOtherChannel()
        {
            foreach (Part part in this.Parts)
            {
                if (part.Source.Sets < 2)
                {
                    throw new InvalidOperationException(part.Label + ": these vertices hold one UV channel only, there is no other channel to copy.");
                }
                foreach (int v in part.EditVerts)
                {
                    Vector2 uv = part.Source.Uv(v, 1 - this.Set);
                    part.U[v] = uv.X;
                    part.V[v] = uv.Y;
                }
            }
        }

        // axis 0: seen from the top, 1: from the front, 2: from the side (model axes, Y up, front -Z).
        public void MapFromWorld(int axis)
        {
            this.Fit(delegate(Part part, int v, out float a, out float b)
            {
                float x = (float) part.Positions[v * 3], y = (float) part.Positions[(v * 3) + 1], z = (float) part.Positions[(v * 3) + 2];
                switch (axis)
                {
                    case 0: a = x; b = z; break;
                    case 1: a = -x; b = -y; break;
                    default: a = -z; b = -y; break;
                }
            });
        }

        // The vertices as the viewport shows them now: screen position, in square pixels.
        public void MapFromView(Matrix viewProjection, float width, float height)
        {
            this.Fit(delegate(Part part, int v, out float a, out float b)
            {
                Vector4 clip = Vector4.Transform(new Vector4((float) part.Positions[v * 3], (float) part.Positions[(v * 3) + 1], (float) part.Positions[(v * 3) + 2], 1f), viewProjection);
                float w = (Math.Abs(clip.W) < 1e-6f) ? 1e-6f : clip.W;
                a = ((clip.X / w) + 1f) * 0.5f * width;
                b = (1f - (clip.Y / w)) * 0.5f * height;
            });
        }

        private delegate void Plane(Part part, int v, out float a, out float b);

        // Puts the projected points in the 0..1 square, the same scale on both axes, centred.
        private void Fit(Plane plane)
        {
            float minA = float.MaxValue, minB = float.MaxValue, maxA = float.MinValue, maxB = float.MinValue;
            List<float[]> points = new List<float[]>();
            foreach (Part part in this.Parts)
            {
                float[] pa = new float[part.EditVerts.Length], pb = new float[part.EditVerts.Length];
                for (int i = 0; i < part.EditVerts.Length; i++)
                {
                    plane(part, part.EditVerts[i], out pa[i], out pb[i]);
                    minA = Math.Min(minA, pa[i]);
                    maxA = Math.Max(maxA, pa[i]);
                    minB = Math.Min(minB, pb[i]);
                    maxB = Math.Max(maxB, pb[i]);
                }
                points.Add(pa);
                points.Add(pb);
            }
            float extent = Math.Max(Math.Max(maxA - minA, maxB - minB), 1e-6f);
            float scale = 1f / extent;
            float offsetA = (1f - ((maxA - minA) * scale)) / 2f, offsetB = (1f - ((maxB - minB) * scale)) / 2f;
            for (int p = 0; p < this.Parts.Count; p++)
            {
                Part part = this.Parts[p];
                for (int i = 0; i < part.EditVerts.Length; i++)
                {
                    part.U[part.EditVerts[i]] = ((points[p * 2][i] - minA) * scale) + offsetA;
                    part.V[part.EditVerts[i]] = ((points[(p * 2) + 1][i] - minB) * scale) + offsetB;
                }
            }
        }

        // ---- tools on a selection (keys: part number << 32 | vertex) ----

        public static long Key(int part, int vertex)
        {
            return ((long) part << 32) | (uint) vertex;
        }

        public static void Unkey(long key, out int part, out int vertex)
        {
            part = (int) (key >> 32);
            vertex = (int) (key & 0xFFFFFFFF);
        }

        public bool Bounds(IEnumerable<long> selection, out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.MaxValue);
            max = new Vector2(float.MinValue);
            bool any = false;
            foreach (long key in selection)
            {
                int p, v;
                Unkey(key, out p, out v);
                min = Vector2.Min(min, new Vector2(this.Parts[p].U[v], this.Parts[p].V[v]));
                max = Vector2.Max(max, new Vector2(this.Parts[p].U[v], this.Parts[p].V[v]));
                any = true;
            }
            return any;
        }

        // new = centre + M * (old - centre) + move; M is the 2x2 matrix [a b; c d] on (u, v).
        public void Transform(IEnumerable<long> selection, Vector2 centre, float a, float b, float c, float d, Vector2 move)
        {
            foreach (long key in selection)
            {
                int p, v;
                Unkey(key, out p, out v);
                Part part = this.Parts[p];
                float du = part.U[v] - centre.X, dv = part.V[v] - centre.Y;
                part.U[v] = centre.X + (a * du) + (b * dv) + move.X;
                part.V[v] = centre.Y + (c * du) + (d * dv) + move.Y;
            }
        }

        public void Reset()
        {
            foreach (Part part in this.Parts)
            {
                part.U = (float[]) part.U0.Clone();
                part.V = (float[]) part.V0.Clone();
            }
        }

        public List<float[][]> Snapshot()
        {
            List<float[][]> copy = new List<float[][]>();
            foreach (Part part in this.Parts)
            {
                copy.Add(new float[][] { (float[]) part.U.Clone(), (float[]) part.V.Clone() });
            }
            return copy;
        }

        public void Restore(List<float[][]> snapshot)
        {
            for (int i = 0; i < this.Parts.Count; i++)
            {
                this.Parts[i].U = (float[]) snapshot[i][0].Clone();
                this.Parts[i].V = (float[]) snapshot[i][1].Clone();
            }
        }

        // Faces of the edited part that touch the vertex, grown into the piece (island) they make: every
        // edited vertex reachable through faces that share a vertex.
        public List<long> Island(IEnumerable<long> seeds)
        {
            Dictionary<long, bool> found = new Dictionary<long, bool>();
            Queue<long> queue = new Queue<long>();
            foreach (long key in seeds)
            {
                if (!found.ContainsKey(key))
                {
                    found[key] = true;
                    queue.Enqueue(key);
                }
            }
            List<Dictionary<int, List<int>>> facesAt = new List<Dictionary<int, List<int>>>();
            foreach (Part part in this.Parts)
            {
                Dictionary<int, List<int>> map = new Dictionary<int, List<int>>();
                foreach (int f in part.EditFaces)
                {
                    foreach (int v in part.Faces[f])
                    {
                        List<int> list;
                        if (!map.TryGetValue(v, out list))
                        {
                            list = new List<int>();
                            map[v] = list;
                        }
                        list.Add(f);
                    }
                }
                facesAt.Add(map);
            }
            while (queue.Count > 0)
            {
                int p, v;
                Unkey(queue.Dequeue(), out p, out v);
                List<int> list;
                if (!facesAt[p].TryGetValue(v, out list))
                {
                    continue;
                }
                foreach (int f in list)
                {
                    foreach (int w in this.Parts[p].Faces[f])
                    {
                        long key = Key(p, w);
                        if (!found.ContainsKey(key))
                        {
                            found[key] = true;
                            queue.Enqueue(key);
                        }
                    }
                }
            }
            return new List<long>(found.Keys);
        }

        // ---- writing back ----

        // Encodes the floats into the vertices of every mesh, in the file's own encoding (see ISource.Write of each game).
        public void Commit()
        {
            foreach (Part part in this.Parts)
            {
                this.Commit(part);
            }
        }

        public void Commit(Part part)
        {
            part.Source.Write(this.Set, part);
        }

        // ---- the viewer ----

        // The mesh as the viewer draws it, rebuilt from the file's own vertices (so the vertex numbers are
        // the file's, including the copies a split made).
        public static void BuildPreview(Part part, ForzaMesh mesh, int set)
        {
            ISource source = part.Source;
            int count = source.VertexCount;
            bool two = source.Sets > 1;
            ForzaVertex[] vertices = new ForzaVertex[count];
            for (int v = 0; v < count; v++)
            {
                vertices[v].position = new Vector3((float) part.Positions[v * 3], (float) part.Positions[(v * 3) + 1], (float) part.Positions[(v * 3) + 2]);
                Vector3 normal = source.Normal(v);
                if (normal.LengthSquared() > 1e-12f)
                {
                    normal.Normalize();
                }
                vertices[v].normal = normal;
                vertices[v].color = Microsoft.Xna.Framework.Graphics.Color.White;
                Vector2 uv1 = source.Uv(v, 0), uv2 = two ? source.Uv(v, 1) : Vector2.Zero;
                vertices[v].texture0 = new Vector2(uv1.X, 1f - uv1.Y);
                vertices[v].texture1 = new Vector2(uv2.X, 1f - uv2.Y);
            }
            int[] indices = new int[part.Faces.Count * 3];
            for (int f = 0; f < part.Faces.Count; f++)
            {
                indices[f * 3] = part.Faces[f][0];
                indices[(f * 3) + 1] = part.Faces[f][1];
                indices[(f * 3) + 2] = part.Faces[f][2];
            }
            mesh.Vertices = vertices;
            mesh.Indices = indices;
            mesh.UseSecondUV = set == 1;
            mesh.Invalidate();
        }

        // After a change: the edited channel's coordinates into the viewer's vertices.
        public void UpdatePreview(Part part, ForzaMesh mesh)
        {
            ForzaVertex[] vertices = mesh.Vertices;
            foreach (int v in part.UsedVerts)
            {
                Vector2 uv = new Vector2(part.U[v], 1f - part.V[v]);
                if (this.Set == 0)
                {
                    vertices[v].texture0 = uv;
                }
                else
                {
                    vertices[v].texture1 = uv;
                }
            }
            mesh.Invalidate();
        }
    }
}
