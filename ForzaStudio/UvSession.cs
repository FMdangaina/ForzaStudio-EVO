namespace ForzaStudio
{
    using Microsoft.Xna.Framework;
    using System;
    using System.Collections.Generic;

    // The UV mapping of some meshes (material + LOD) of a carbin being edited: the UVs of one channel
    // (UV1 or UV2) as floats, moved, rotated, scaled by the UV mapper, and written back into the vertices
    // (16-bit, inside the mesh's own offset / scale, widened when the new UVs do not fit).
    // UVs here are as the game samples them: V grows downward (the OBJ's vt is 1 - V).
    // A vertex is shared by every face that uses it, so editing some faces of a mesh first gives them
    // copies of the vertices the other faces still use ("keep separately").
    public sealed class UvSession
    {
        public enum Start { Current, OtherChannel, World, View }

        public sealed class Part
        {
            public int Section, Sub;
            public CarbinModel.Section S;
            public CarbinModel.SubSection Sb;
            public CarbinModel.VertexBuffer Buffer;
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

        public readonly CarbinModel Model;
        public readonly int Set;
        public readonly List<Part> Parts = new List<Part>();

        private UvSession(CarbinModel model, int set)
        {
            this.Model = model;
            this.Set = set;
        }

        // 'meshes': section / mesh numbers with the faces to edit (null = every face of the mesh).
        public static UvSession Create(CarbinModel model, List<int[]> meshes, List<List<int>> faces, int set, bool splitShared)
        {
            UvSession session = new UvSession(model, set);
            for (int m = 0; m < meshes.Count; m++)
            {
                CarbinModel.Section s = model.Sections[meshes[m][0]];
                CarbinModel.SubSection sub = s.SubSections[meshes[m][1]];
                bool lod0 = sub.Lod == 0;
                Part part = new Part();
                part.Section = meshes[m][0];
                part.Sub = meshes[m][1];
                part.S = s;
                part.Sb = sub;
                part.Label = s.Name + " / " + sub.Name;
                part.Faces = CarbinModel.StripTriangles(sub);
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
                    SplitShared(s, sub, part, editing, lod0);
                }
                part.Buffer = lod0 ? s.Lod0 : s.Lod;
                part.Positions = CarbinModel.Positions(s, part.Buffer);
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
                int count = part.Buffer.Count;
                part.U = new float[count];
                part.V = new float[count];
                foreach (int v in part.UsedVerts)
                {
                    Vector2 uv = CarbinModel.VertexUv(part.Buffer, v, sub, set);
                    part.U[v] = uv.X;
                    part.V[v] = uv.Y;
                }
                part.U0 = (float[]) part.U.Clone();
                part.V0 = (float[]) part.V.Clone();
                session.Parts.Add(part);
            }
            // Meshes of one part share its vertex buffer, and a later mesh's split can have added vertices after an
            // earlier one was read: every mesh sees the buffer as it ended up.
            foreach (Part part in session.Parts)
            {
                int count = part.Buffer.Count;
                part.Positions = CarbinModel.Positions(part.S, part.Buffer);
                part.U = Grow(part.U, count);
                part.V = Grow(part.V, count);
                part.U0 = Grow(part.U0, count);
                part.V0 = Grow(part.V0, count);
            }
            return session;
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

        // Faces being edited that touch a vertex some other face uses (in this mesh or another mesh of the
        // same buffer) get a copy of it; the mesh's indices are rewritten when any copy was made.
        private static void SplitShared(CarbinModel.Section s, CarbinModel.SubSection sub, Part part, bool[] editing, bool lod0)
        {
            CarbinModel.VertexBuffer buffer = lod0 ? s.Lod0 : s.Lod;
            int[] others = new int[buffer.Count];
            foreach (CarbinModel.SubSection other in s.SubSections)
            {
                if ((other.Lod == 0) != lod0)
                {
                    continue;
                }
                if (other == sub)
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
            Dictionary<int, int> copies = new Dictionary<int, int>();
            foreach (int f in part.EditFaces)
            {
                int[] face = part.Faces[f];
                for (int c = 0; c < 3; c++)
                {
                    int v = face[c];
                    if (others[v] == 0)
                    {
                        continue;
                    }
                    int copy;
                    if (!copies.TryGetValue(v, out copy))
                    {
                        copy = CarbinModel.CopyVertexToEnd(s, lod0, v);
                        copies[v] = copy;
                    }
                    face[c] = copy;
                }
            }
            if (copies.Count > 0)
            {
                sub.Indices = CarbinModel.TriangleStrips(sub, part.Faces, delegate(int i) { return true; });
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
                foreach (int v in part.EditVerts)
                {
                    Vector2 uv = CarbinModel.VertexUv(part.Buffer, v, part.Sb, 1 - this.Set);
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

        // Encodes the floats into the vertices of every mesh. A mesh keeps its offset / scale when the UVs
        // fit in it; otherwise the range is widened to the UVs' own min / max.
        public void Commit()
        {
            foreach (Part part in this.Parts)
            {
                this.Commit(part);
            }
        }

        public void Commit(Part part)
        {
            int o = this.Set * 4;
            float[] range = part.Sb.Uv;
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
            byte[] data = part.Buffer.Data;
            int stride = part.Buffer.Stride;
            foreach (int v in part.UsedVerts)
            {
                int u16 = Raw((part.U[v] - range[o]) / range[o + 1]);
                int v16 = Raw((part.V[v] - range[o + 2]) / range[o + 3]);
                int at = (v * stride) + 8 + (this.Set * 4);
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

        // ---- the viewer ----

        // The mesh as the viewer draws it, rebuilt from the carbin's own vertices (so the vertex numbers are
        // the buffer's, including the copies a split made).
        public static void BuildPreview(Part part, ForzaMesh mesh, int set)
        {
            int count = part.Buffer.Count;
            ForzaVertex[] vertices = new ForzaVertex[count];
            for (int v = 0; v < count; v++)
            {
                vertices[v].position = new Vector3((float) part.Positions[v * 3], (float) part.Positions[(v * 3) + 1], (float) part.Positions[(v * 3) + 2]);
                Vector3 normal = CarbinModel.VertexNormal(part.Buffer, v);
                if (normal.LengthSquared() > 1e-12f)
                {
                    normal.Normalize();
                }
                vertices[v].normal = normal;
                vertices[v].color = Microsoft.Xna.Framework.Graphics.Color.White;
                Vector2 uv1 = CarbinModel.VertexUv(part.Buffer, v, part.Sb, 0), uv2 = CarbinModel.VertexUv(part.Buffer, v, part.Sb, 1);
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
