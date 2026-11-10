using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ForzaStudio
{
    // Forza Motorsport 2 .carbin: reads the whole file into named fields and writes it back byte for byte
    // (785 of the game's 789 carbins: 310 car bodies, 68 brakes, 407 wheels). The base of an FM2 model writer.
    //
    // FM2 is the oldest form of the object tree the later games use (CarbinFormat.cs, docs/CARBIN_ALL_GAMES.md):
    //
    //   header        u32 1 (brake, wheel) or 2 (car body, followed by the wheel lighting: 4 x 16 floats)
    //   ModelSettings version 713 / 714, emitter list (140-byte records: exhaust tips), driver anchors (6 vec4)
    //   Model         version 4
    //     DrawOrder   version 4, LOD count, then per LOD the transparent lists and per LOD the opaque lists
    //                 (u16 part, u16 mesh)
    //     Parts       each: physics (bounds, mass, centre of mass, detach damage), glass shards, the part's
    //                 vertex range in a shared buffer, its meshes (material name, LOD, strip indices)
    //     two shared vertex buffers: the first for the parts with HasTransparentMeshes = 1, the second for
    //                 the others. A part has no vertices of its own (its own buffer is always empty).
    //
    // A part uses the vertices FirstVertex..LastVertex of its buffer and its mesh indices count from
    // FirstVertex (highest index = LastVertex - FirstVertex on all 11057 parts; the ranges follow each other
    // in part order). Vertices are 60 bytes (Vertex* constants below).
    //
    // Not read: three wheels in a later form (ModelSettings 3 / 4: 5zigen_hyper_5zr_02, enkei_rs5,
    // mht_crossover_wheel), brakes\Circle01 (junk), the decal carbins of Livery.zip (another kind of file).
    public class Fm2CarbinModel
    {
        public const int VertexStride = 60;

        // 60-byte vertex, offsets:
        public const int VertexPosition = 0;        // 4 halves: x, y, z, w; position = part origin + xyz * w
        public const int VertexUv0 = 8;             // 2 x u16: sign bit + magnitude / 1310.72
        public const int VertexUv1 = 12;            // always 0
        public const int VertexNormal = 16;         // 11 / 11 / 10 signed bits, x low
        public const int VertexDamage = 20;         // damage direction, the FM4 morph word (docs/CARBIN_ALL_GAMES.md section 5)
        public const int VertexUnknown = 24;        // u32, 0 on 30 % of the vertices
        public const int VertexLighting = 28;       // 16 SNORM16: baked spherical harmonics, bands 0-3 (1 + 3 + 5 + 7)

        public const uint BufferMarker = 0x000DECAF;    // before and after the data of a filled vertex buffer

        public class VertexBuffer
        {
            public uint Version = 2;
            public uint DataVersion = 1;
            public int Count;
            public int Stride = VertexStride;
            public byte[] Data = new byte[0];
        }

        public class Mesh
        {
            public uint MaterialVersion = 1;
            public uint Version = 3;
            public string MaterialName = "";
            public uint Primitive = 6;          // 6 strip (0xFFFF restarts), 4 list
            public uint Lod;                    // 1..5
            public uint IndexBufferVersion = 3;
            public int IndexSize = 2;
            public List<int> Indices = new List<int>();     // relative to the part's FirstVertex
        }

        public class Part
        {
            public uint PhysicsVersion = 713;
            public float[] Bounds = new float[9];       // origin, min, max (min / max relative to the origin)
            public byte[] Physics = new byte[20];       // Mass, CenterOfMass (3 floats), DetachDamage
            public uint GlassVersion = 1;
            public uint GlassVertexListVersion = 1;
            public byte[] GlassVertices = new byte[0];  // vec4 each: the shattered-glass mesh
            public uint GlassIndexListVersion = 1;
            public byte[] GlassIndices = new byte[0];   // u16 each
            public int FirstVertex;
            public int LastVertex;
            public byte HasTransparentMeshes;           // 1: first shared buffer, 0: second
            public uint Version = 1;
            public string Name = "";
            public VertexBuffer Vertices = new VertexBuffer();   // empty in every FM2 file
            public uint MeshListVersion = 1;
            public List<Mesh> Meshes = new List<Mesh>();

            public int VertexCount
            {
                get { return (this.Meshes.Count == 0) ? 0 : this.LastVertex - this.FirstVertex + 1; }
            }
        }

        public uint HeaderVersion;                      // 1 brake or wheel, 2 car body
        public byte[] WheelLighting = new byte[0];      // car body: 4 x 16 floats
        public uint SettingsVersion = 714;
        public uint EmitterListVersion = 1;
        public List<byte[]> Emitters = new List<byte[]>();   // 140 bytes: version 2, type, parent part (-1 none), 2 matrices
        public uint DriverVersion = 1;
        public byte[] DriverAnchors = new byte[96];     // 6 vec4
        public uint ModelVersion = 4;
        public uint DrawOrderVersion = 4;
        public int LodCount;
        public List<List<uint>> DrawLists = new List<List<uint>>();   // LodCount transparent lists, then LodCount opaque; entry = part << 16 | mesh
        public uint PartListVersion = 1;
        public List<Part> Parts = new List<Part>();
        public VertexBuffer TransparentVertices = new VertexBuffer();
        public VertexBuffer OpaqueVertices = new VertexBuffer();

        private byte[] data;
        private int position;

        // True for the files this class reads (CarbinFormat tells FM2 from the other games).
        public static bool CanRead(byte[] file)
        {
            CarbinFormat format = CarbinFormat.Identify(file);
            return format.Family == CarbinFamily.Motorsport2 && (format.SettingsVersion == 713 || format.SettingsVersion == 714);
        }

        public Fm2CarbinModel()
        {
        }

        public Fm2CarbinModel(byte[] file)
        {
            if (!CanRead(file))
            {
                throw new NotSupportedException("Not a Forza Motorsport 2 model (" + CarbinFormat.Identify(file) + ").");
            }
            this.data = file;
            this.position = 0;
            this.HeaderVersion = this.U32();
            if (this.HeaderVersion == 2)
            {
                this.WheelLighting = this.Bytes(256);
            }
            this.SettingsVersion = this.U32();
            this.EmitterListVersion = this.U32();
            int emitters = this.Count(64, "emitters");
            for (int i = 0; i < emitters; i++)
            {
                this.Emitters.Add(this.Bytes(140));
            }
            this.DriverVersion = this.U32();
            if (this.DriverVersion != 1)
            {
                throw new InvalidDataException("FM2 carbin: driver anchors version " + this.DriverVersion + ".");
            }
            this.DriverAnchors = this.Bytes(96);
            this.ModelVersion = this.U32();
            this.DrawOrderVersion = this.U32();
            this.LodCount = this.Count(16, "LODs");
            for (int i = 0; i < this.LodCount * 2; i++)
            {
                int entries = this.Count(100000, "draw entries");
                List<uint> list = new List<uint>(entries);
                for (int k = 0; k < entries; k++)
                {
                    list.Add(this.U32());
                }
                this.DrawLists.Add(list);
            }
            this.PartListVersion = this.U32();
            int parts = this.Count(4096, "parts");
            for (int i = 0; i < parts; i++)
            {
                this.Parts.Add(this.ReadPart());
            }
            this.TransparentVertices = this.ReadBuffer();
            this.OpaqueVertices = this.ReadBuffer();
            if (this.position != file.Length)
            {
                throw new InvalidDataException("FM2 carbin: " + (file.Length - this.position) + " bytes left after the model.");
            }
            this.data = null;
        }

        private Part ReadPart()
        {
            Part part = new Part();
            part.PhysicsVersion = this.U32();
            for (int i = 0; i < 9; i++)
            {
                part.Bounds[i] = BitConverter.ToSingle(BitConverter.GetBytes(this.U32()), 0);
            }
            part.Physics = this.Bytes(20);
            part.GlassVersion = this.U32();
            part.GlassVertexListVersion = this.U32();
            part.GlassVertices = this.Bytes(this.Count(1000000, "glass vertices") * 16);
            part.GlassIndexListVersion = this.U32();
            part.GlassIndices = this.Bytes(this.Count(1000000, "glass indices") * 2);
            part.FirstVertex = this.U16();
            part.LastVertex = this.U16();
            part.HasTransparentMeshes = this.Bytes(1)[0];
            part.Version = this.U32();
            part.Name = this.Text();
            part.Vertices = this.ReadBuffer();
            part.MeshListVersion = this.U32();
            int meshes = this.Count(4096, "meshes");
            for (int i = 0; i < meshes; i++)
            {
                Mesh mesh = new Mesh();
                mesh.MaterialVersion = this.U32();
                mesh.Version = this.U32();
                mesh.MaterialName = this.Text();
                mesh.Primitive = this.U32();
                mesh.Lod = this.U32();
                mesh.IndexBufferVersion = this.U32();
                int count = this.Count(int.MaxValue / 4, "indices");
                mesh.IndexSize = (int) this.U32();
                if (mesh.IndexSize != 2 && mesh.IndexSize != 4)
                {
                    throw new InvalidDataException("FM2 carbin: index size " + mesh.IndexSize + ".");
                }
                mesh.Indices = new List<int>(count);
                for (int k = 0; k < count; k++)
                {
                    mesh.Indices.Add((mesh.IndexSize == 2) ? this.U16() : (int) this.U32());
                }
                part.Meshes.Add(mesh);
            }
            return part;
        }

        private VertexBuffer ReadBuffer()
        {
            VertexBuffer buffer = new VertexBuffer();
            buffer.Version = this.U32();
            buffer.DataVersion = this.U32();
            buffer.Count = this.Count(int.MaxValue / 64, "vertices");
            buffer.Stride = (int) this.U32();
            if (buffer.Count > 0)
            {
                this.Marker();
                buffer.Data = this.Bytes(buffer.Count * buffer.Stride);
                this.Marker();
            }
            return buffer;
        }

        private void Marker()
        {
            uint marker = this.U32();
            if (marker != BufferMarker)
            {
                throw new InvalidDataException("FM2 carbin: vertex buffer marker " + marker.ToString("X8") + " at 0x" + (this.position - 4).ToString("X") + ".");
            }
        }

        private uint U32()
        {
            if (this.position + 4 > this.data.Length)
            {
                throw new InvalidDataException("FM2 carbin: the file ends at 0x" + this.position.ToString("X") + ".");
            }
            uint value = (uint) ((this.data[this.position] << 24) | (this.data[this.position + 1] << 16) | (this.data[this.position + 2] << 8) | this.data[this.position + 3]);
            this.position += 4;
            return value;
        }

        private int U16()
        {
            if (this.position + 2 > this.data.Length)
            {
                throw new InvalidDataException("FM2 carbin: the file ends at 0x" + this.position.ToString("X") + ".");
            }
            int value = (this.data[this.position] << 8) | this.data[this.position + 1];
            this.position += 2;
            return value;
        }

        private int Count(int max, string what)
        {
            uint value = this.U32();
            if (value > (uint) max)
            {
                throw new InvalidDataException("FM2 carbin: " + value + " " + what + " at 0x" + (this.position - 4).ToString("X") + ".");
            }
            return (int) value;
        }

        private byte[] Bytes(int count)
        {
            if (count < 0 || this.position + count > this.data.Length)
            {
                throw new InvalidDataException("FM2 carbin: " + count + " bytes wanted at 0x" + this.position.ToString("X") + ", past the end of the file.");
            }
            byte[] bytes = new byte[count];
            Buffer.BlockCopy(this.data, this.position, bytes, 0, count);
            this.position += count;
            return bytes;
        }

        private string Text()
        {
            return Encoding.ASCII.GetString(this.Bytes(this.Count(4096, "name characters")));
        }

        public byte[] ToBytes()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                Put(stream, this.HeaderVersion);
                stream.Write(this.WheelLighting, 0, this.WheelLighting.Length);
                Put(stream, this.SettingsVersion);
                Put(stream, this.EmitterListVersion);
                Put(stream, (uint) this.Emitters.Count);
                foreach (byte[] emitter in this.Emitters)
                {
                    stream.Write(emitter, 0, emitter.Length);
                }
                Put(stream, this.DriverVersion);
                stream.Write(this.DriverAnchors, 0, this.DriverAnchors.Length);
                Put(stream, this.ModelVersion);
                Put(stream, this.DrawOrderVersion);
                Put(stream, (uint) this.LodCount);
                foreach (List<uint> list in this.DrawLists)
                {
                    Put(stream, (uint) list.Count);
                    foreach (uint entry in list)
                    {
                        Put(stream, entry);
                    }
                }
                Put(stream, this.PartListVersion);
                Put(stream, (uint) this.Parts.Count);
                foreach (Part part in this.Parts)
                {
                    Put(stream, part.PhysicsVersion);
                    foreach (float value in part.Bounds)
                    {
                        Put(stream, BitConverter.ToUInt32(BitConverter.GetBytes(value), 0));
                    }
                    stream.Write(part.Physics, 0, part.Physics.Length);
                    Put(stream, part.GlassVersion);
                    Put(stream, part.GlassVertexListVersion);
                    Put(stream, (uint) (part.GlassVertices.Length / 16));
                    stream.Write(part.GlassVertices, 0, part.GlassVertices.Length);
                    Put(stream, part.GlassIndexListVersion);
                    Put(stream, (uint) (part.GlassIndices.Length / 2));
                    stream.Write(part.GlassIndices, 0, part.GlassIndices.Length);
                    Put16(stream, part.FirstVertex);
                    Put16(stream, part.LastVertex);
                    stream.WriteByte(part.HasTransparentMeshes);
                    Put(stream, part.Version);
                    PutText(stream, part.Name);
                    PutBuffer(stream, part.Vertices);
                    Put(stream, part.MeshListVersion);
                    Put(stream, (uint) part.Meshes.Count);
                    foreach (Mesh mesh in part.Meshes)
                    {
                        Put(stream, mesh.MaterialVersion);
                        Put(stream, mesh.Version);
                        PutText(stream, mesh.MaterialName);
                        Put(stream, mesh.Primitive);
                        Put(stream, mesh.Lod);
                        Put(stream, mesh.IndexBufferVersion);
                        Put(stream, (uint) mesh.Indices.Count);
                        Put(stream, (uint) mesh.IndexSize);
                        foreach (int index in mesh.Indices)
                        {
                            if (mesh.IndexSize == 2)
                            {
                                Put16(stream, index);
                            }
                            else
                            {
                                Put(stream, (uint) index);
                            }
                        }
                    }
                }
                PutBuffer(stream, this.TransparentVertices);
                PutBuffer(stream, this.OpaqueVertices);
                return stream.ToArray();
            }
        }

        private static void Put(Stream stream, uint value)
        {
            stream.WriteByte((byte) (value >> 24));
            stream.WriteByte((byte) (value >> 16));
            stream.WriteByte((byte) (value >> 8));
            stream.WriteByte((byte) value);
        }

        private static void Put16(Stream stream, int value)
        {
            stream.WriteByte((byte) (value >> 8));
            stream.WriteByte((byte) value);
        }

        private static void PutText(Stream stream, string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            Put(stream, (uint) bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void PutBuffer(Stream stream, VertexBuffer buffer)
        {
            Put(stream, buffer.Version);
            Put(stream, buffer.DataVersion);
            Put(stream, (uint) buffer.Count);
            Put(stream, (uint) buffer.Stride);
            if (buffer.Count > 0)
            {
                Put(stream, BufferMarker);
                stream.Write(buffer.Data, 0, buffer.Data.Length);
                Put(stream, BufferMarker);
            }
        }

        // ---- reading the geometry ----

        public Part Find(string name)
        {
            return this.Parts.Find(delegate(Part p) { return string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase); });
        }

        // The shared buffer a part's vertices are in.
        public VertexBuffer BufferOf(Part part)
        {
            return (part.HasTransparentMeshes != 0) ? this.TransparentVertices : this.OpaqueVertices;
        }

        // Offset in BufferOf(part).Data of the part's vertex number 'vertex' (the number its mesh indices use).
        public int VertexOffset(Part part, int vertex)
        {
            return (part.FirstVertex + vertex) * this.BufferOf(part).Stride;
        }

        // x, y, z per vertex of the part, in car space (metres).
        public double[] Positions(Part part)
        {
            byte[] d = this.BufferOf(part).Data;
            int count = part.VertexCount;
            double[] positions = new double[count * 3];
            for (int i = 0; i < count; i++)
            {
                int o = this.VertexOffset(part, i) + VertexPosition;
                double w = Half(d, o + 6);
                for (int c = 0; c < 3; c++)
                {
                    positions[(i * 3) + c] = part.Bounds[c] + (Half(d, o + (c * 2)) * w);
                }
            }
            return positions;
        }

        // The damage direction word of a vertex of the part (X bits 0-10, Y 11-21, Z 22-31, signed).
        public uint Damage(Part part, int vertex)
        {
            byte[] d = this.BufferOf(part).Data;
            int o = this.VertexOffset(part, vertex) + VertexDamage;
            return (uint) ((d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3]);
        }

        private static double Half(byte[] d, int o)
        {
            int h = (d[o] << 8) | d[o + 1];
            int sign = (h >> 15) & 1;
            int exponent = (h >> 10) & 31;
            int mantissa = h & 1023;
            double value = (exponent == 0) ? mantissa / 1024.0 * Math.Pow(2, -14) : (1 + (mantissa / 1024.0)) * Math.Pow(2, exponent - 15);
            return (sign == 1) ? -value : value;
        }

        // What does not hold together in the model: vertex ranges, mesh indices, draw lists, buffer sizes.
        // Empty on every file of the game; a writer has to keep it empty.
        public List<string> Check()
        {
            List<string> problems = new List<string>();
            int[] next = new int[2];
            for (int p = 0; p < this.Parts.Count; p++)
            {
                Part part = this.Parts[p];
                int buffer = (part.HasTransparentMeshes != 0) ? 0 : 1;
                if (part.HasTransparentMeshes > 1)
                {
                    problems.Add(part.Name + ": HasTransparentMeshes is " + part.HasTransparentMeshes + " (0 or 1).");
                }
                if (part.Vertices.Count != 0)
                {
                    problems.Add(part.Name + ": the part's own vertex buffer is not empty.");
                }
                int highest = -1;
                foreach (Mesh mesh in part.Meshes)
                {
                    int restart = (mesh.IndexSize == 2) ? 0xFFFF : 0x00FFFFFF;
                    foreach (int index in mesh.Indices)
                    {
                        if (index != restart && index > highest)
                        {
                            highest = index;
                        }
                    }
                }
                if (highest < 0)
                {
                    continue;
                }
                if (part.FirstVertex != next[buffer])
                {
                    problems.Add(part.Name + ": FirstVertex " + part.FirstVertex + ", the previous part of the buffer ends at " + (next[buffer] - 1) + ".");
                }
                if (highest != part.LastVertex - part.FirstVertex)
                {
                    problems.Add(part.Name + ": highest mesh index " + highest + ", vertex range " + part.FirstVertex + ".." + part.LastVertex + ".");
                }
                next[buffer] = part.LastVertex + 1;
            }
            if (this.TransparentVertices.Count != next[0])
            {
                problems.Add("First shared buffer: " + this.TransparentVertices.Count + " vertices, the parts use " + next[0] + ".");
            }
            if (this.OpaqueVertices.Count != next[1])
            {
                problems.Add("Second shared buffer: " + this.OpaqueVertices.Count + " vertices, the parts use " + next[1] + ".");
            }
            if (this.DrawLists.Count != this.LodCount * 2)
            {
                problems.Add("Draw lists: " + this.DrawLists.Count + ", expected " + (this.LodCount * 2) + ".");
            }
            foreach (List<uint> list in this.DrawLists)
            {
                foreach (uint entry in list)
                {
                    int partIndex = (int) (entry >> 16);
                    int meshIndex = (int) (entry & 0xFFFF);
                    if (partIndex >= this.Parts.Count || meshIndex >= this.Parts[partIndex].Meshes.Count)
                    {
                        problems.Add("Draw list entry " + entry.ToString("X8") + " points at no mesh.");
                    }
                }
            }
            return problems;
        }
    }
}
