namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Xna.Framework;

    // UV Mapper on FM2 models (user 2026-11-21: "permettimi di modificare uv su fm2 fm3").
    // FM2 vertices hold one UV channel (UV0 at +8: 2 offset-binary words, FM4 UV = (raw - 32768) / 1310.72, +-25; the words
    // at +12 are always 0), with no range per mesh. The mapper works on each part's own vertex bytes (as every FM2 edit) and
    // lays the two shared buffers out again when the model is written. Only the UV words of the edited vertices change;
    // a vertex another mesh still uses is copied first ("keep separately"), every byte the same.
    public partial class Fm2CarbinModel
    {
        public sealed class UvWork
        {
            private readonly Fm2CarbinModel model;
            private readonly Dictionary<Part, byte[]> locals;

            internal UvWork(Fm2CarbinModel model)
            {
                this.model = model;
                this.locals = model.Locals();
            }

            public int VertexCount(Part part)
            {
                return this.locals[part].Length / VertexStride;
            }

            // Car space, as the viewport shows them.
            public double[] Positions(Part part)
            {
                return LocalPositions(part, this.locals[part]);
            }

            // In FM4 units (the texture is sampled at u, v; v grows downward).
            public Vector2 Uv(Part part, int v)
            {
                byte[] local = this.locals[part];
                int o = (v * VertexStride) + VertexUv0;
                ushort a = (ushort) ((local[o] << 8) | local[o + 1]), b = (ushort) ((local[o + 2] << 8) | local[o + 3]);
                return new Vector2(0f - ForzaVertex.Fm2Uv(a), 0f - ForzaVertex.Fm2Uv(b));
            }

            public Vector3 Normal(Part part, int v)
            {
                return CarbinModel.ReadPacked111110(this.locals[part], (v * VertexStride) + VertexNormal);
            }

            // The mesh's triangles (indices into the part's vertices), winding as the game draws it.
            public List<int[]> Triangles(Part part, Mesh mesh)
            {
                return MeshTriangles(mesh, this.VertexCount(part));
            }

            // A copy of vertex v at the end of the part's vertices (every byte); returns its number.
            public int CopyVertex(Part part, int v)
            {
                byte[] local = this.locals[part];
                int count = local.Length / VertexStride;
                if (count + 1 >= 0xFFFF)
                {
                    throw new InvalidOperationException(part.Name + ": more than 65534 vertices in one FM2 part with the copies the mapping needs.");
                }
                byte[] grown = new byte[local.Length + VertexStride];
                Buffer.BlockCopy(local, 0, grown, 0, local.Length);
                Buffer.BlockCopy(local, v * VertexStride, grown, local.Length, VertexStride);
                this.locals[part] = grown;
                return count;
            }

            // The mesh drawn from these triangles: a list stays a list, a strip gets one 3-index strip per triangle (the
            // winding stays as Triangles gave it).
            public void SetTriangles(Mesh mesh, List<int[]> faces)
            {
                List<int> indices = new List<int>();
                int restart = RestartOf(mesh);
                foreach (int[] face in faces)
                {
                    if (mesh.Primitive != 4 && indices.Count > 0)
                    {
                        indices.Add(restart);
                    }
                    indices.AddRange(face);
                }
                mesh.Indices = indices;
            }

            public void WriteUv(Part part, int v, float u, float w)
            {
                // the words reach from -25 (word 0) to +24.9992 (word 65535)
                if (float.IsNaN(u) || float.IsNaN(w) || u < -25.0004f || w < -25.0004f || u > UvLimit + 0.0004 || w > UvLimit + 0.0004)
                {
                    throw new InvalidOperationException(string.Format(
                        "{0}: Forza Motorsport 2 stores UVs within +-25 and this mapping reaches {1:0.#}. Move or scale it closer to 0.",
                        part.Name, Math.Max(Math.Abs(u), Math.Abs(w))));
                }
                byte[] local = this.locals[part];
                int o = (v * VertexStride) + VertexUv0;
                ushort a = ForzaVertex.Fm2UvWord(0.0 - u), b = ForzaVertex.Fm2UvWord(0.0 - w);
                local[o] = (byte) (a >> 8);
                local[o + 1] = (byte) a;
                local[o + 2] = (byte) (b >> 8);
                local[o + 3] = (byte) b;
            }

            // The model with the working vertices in its shared buffers.
            public byte[] ToBytes()
            {
                this.model.Rebuild(this.locals);
                return this.model.ToBytes();
            }
        }

        public UvWork BeginUv()
        {
            return new UvWork(this);
        }
    }
}
