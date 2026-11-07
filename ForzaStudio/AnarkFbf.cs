using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ForzaStudio
{
    // Anark digital dash scene (.fbf, DigitalGauge\Dash_<car>.fbf). Big-endian.
    //   u32 version (FM3 0x3EE, FM4 0x3EF, FH2/FH1 0x3F0), u32 record count,
    //   records: u32 type, u32 id, u32 length + name, then a body that depends on the type,
    //   then the mesh table: u32 mesh count, u32 total A, u32 data bytes, the vertex buffer descriptors,
    //   the index buffer descriptors, and the data (vertices and u16 indices of every mesh).
    // Groups come after their children and have no body. A model's body points into the mesh table.
    // The positions of the objects are not in this file (see the .bgf).
    // Bytes not understood yet are kept as they are, so a file is written back byte for byte.
    public class AnarkFbf
    {
        public const uint VersionFM3 = 0x3EE;
        public const uint VersionFM4 = 0x3EF;
        public const uint VersionFH2 = 0x3F0;

        public enum RecordType
        {
            Group = 0,
            Text = 1,
            Camera = 2,
            Layer = 3,
            Model = 4,
            Material = 5,
            Light = 6,
            Image = 7,
        }

        public class Record
        {
            public RecordType Type;
            public uint Id;
            public string Name;
            public byte[] Body;          // fixed-size bodies (camera, layer, model, material, light)

            // Text: 6 bytes, u16 length + default text, u32 length + tag (mostly empty, "NLOC" seen), 10 bytes,
            //   u16 length + font, 34 bytes (FH2: 38, FM3: 33, all 154 FM3 dashes; FM4's byte 33 is 0 on every file).
            public byte[] TextHead;
            public string Text;
            public string TextTag;
            public byte[] TextMiddle;
            public string Font;
            public byte[] TextTail;

            // Image: u32 owner material id, u32 length + path, 116 bytes.
            public uint Owner;
            public string Path;

            public Record Copy()
            {
                Record r = (Record) this.MemberwiseClone();
                r.Body = Dup(this.Body);
                r.TextHead = Dup(this.TextHead);
                r.TextMiddle = Dup(this.TextMiddle);
                r.TextTail = Dup(this.TextTail);
                return r;
            }

            private static byte[] Dup(byte[] b)
            {
                return (b == null) ? null : (byte[]) b.Clone();
            }

            public override string ToString()
            {
                return this.Type + " " + this.Id + " " + this.Name;
            }
        }

        // Vertex buffer descriptor (96 bytes): u32 vertex count, u32 index buffer count, 0, 0, float min[4], float max[4],
        //   1, 1, 0, 0, 0, 0xFFFF0000, u32 data offset | 3, u32 0x10000000 | vertex bytes | 2,
        //   (32-byte vertices on all 431 files: float3 position, float3 normal, float2 uv)
        //   u32 offset of its first index buffer descriptor in that table, 0, 0, 0.
        // Index buffer descriptor (36 bytes): u32 index count, 0x20000002, 1, 0, 0, 0, 0xFFFF0000, u32 data offset, u32 bytes.
        public class Mesh
        {
            public byte[] VertexDesc;
            public List<byte[]> IndexDescs = new List<byte[]>();

            public int VertexCount { get { return (int) ReadU32(this.VertexDesc, 0); } }
            public int IndexBufferCount { get { return (int) ReadU32(this.VertexDesc, 4); } }
            public int VertexOffset { get { return (int) (ReadU32(this.VertexDesc, 18 * 4) & ~3u); } }
            public int VertexBytes { get { return (int) (ReadU32(this.VertexDesc, 19 * 4) & 0x0FFFFFFC); } }
            public int IndexDescOffset { get { return (int) ReadU32(this.VertexDesc, 20 * 4); } }
            public int VertexStride { get { return (this.VertexCount > 0) ? this.VertexBytes / this.VertexCount : 0; } }

            public static int IndexCount(byte[] desc) { return (int) ReadU32(desc, 0); }
            public static int IndexOffset(byte[] desc) { return (int) ReadU32(desc, 7 * 4); }
            public static int IndexBytes(byte[] desc) { return (int) ReadU32(desc, 8 * 4); }
        }

        private const int IndexDescSize = 36;
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        public uint Version;
        public List<Record> Records = new List<Record>();
        public List<Mesh> Meshes = new List<Mesh>();
        public byte[] MeshData;          // vertices and indices of every mesh, at the descriptors' offsets

        public bool IsFH2 { get { return this.Version == VersionFH2; } }
        public bool IsFM3 { get { return this.Version == VersionFM3; } }

        private static int BodySize(RecordType type)
        {
            switch (type)
            {
                case RecordType.Group: return 0;
                case RecordType.Camera: return 64;
                case RecordType.Layer: return 24;
                case RecordType.Model: return 8;
                case RecordType.Material: return 104;
                case RecordType.Light: return 104;
            }
            return -1;
        }

        public static AnarkFbf Read(byte[] data)
        {
            AnarkFbf fbf = new AnarkFbf();
            Reader r = new Reader(data);
            fbf.Version = r.U32();
            if (fbf.Version != VersionFM3 && fbf.Version != VersionFM4 && fbf.Version != VersionFH2)
            {
                throw new InvalidDataException(string.Format("not an Anark .fbf (version 0x{0:X})", fbf.Version));
            }
            int count = (int) r.U32();
            for (int n = 0; n < count; n++)
            {
                Record rec = new Record();
                int at = r.Position;
                uint type = r.U32();
                rec.Type = (RecordType) type;
                rec.Id = r.U32();
                rec.Name = r.String32();
                switch (rec.Type)
                {
                    case RecordType.Text:
                        rec.TextHead = r.Bytes(6);
                        rec.Text = r.String16();
                        rec.TextTag = r.String32();
                        rec.TextMiddle = r.Bytes(10);
                        rec.Font = r.String16();
                        rec.TextTail = r.Bytes(fbf.IsFH2 ? 38 : fbf.IsFM3 ? 33 : 34);
                        break;
                    case RecordType.Image:
                        rec.Owner = r.U32();
                        rec.Path = r.String32();
                        rec.Body = r.Bytes(116);
                        break;
                    default:
                        int size = BodySize(rec.Type);
                        if (size < 0)
                        {
                            throw new InvalidDataException(string.Format("record {0} at 0x{1:X}: unknown type {2}", n, at, type));
                        }
                        rec.Body = r.Bytes(size);
                        break;
                }
                fbf.Records.Add(rec);
            }
            int meshes = (int) r.U32();
            int descBytes = (int) r.U32();
            int dataBytes = (int) r.U32();
            for (int n = 0; n < meshes; n++)
            {
                Mesh mesh = new Mesh();
                mesh.VertexDesc = r.Bytes(96);
                fbf.Meshes.Add(mesh);
            }
            int descOffset = 0;
            foreach (Mesh mesh in fbf.Meshes)
            {
                if (mesh.IndexDescOffset != descOffset)
                {
                    throw new InvalidDataException(string.Format("mesh index descriptors at {0}, expected {1}", mesh.IndexDescOffset, descOffset));
                }
                for (int k = 0; k < mesh.IndexBufferCount; k++)
                {
                    byte[] desc = r.Bytes(IndexDescSize);
                    if (Mesh.IndexOffset(desc) + Mesh.IndexBytes(desc) > dataBytes)
                    {
                        throw new InvalidDataException(string.Format("mesh indices at {0}+{1} past the {2} data bytes", Mesh.IndexOffset(desc), Mesh.IndexBytes(desc), dataBytes));
                    }
                    mesh.IndexDescs.Add(desc);
                    descOffset += IndexDescSize;
                }
            }
            if (descOffset != descBytes)
            {
                throw new InvalidDataException(string.Format("{0} bytes of index descriptors, header says {1}", descOffset, descBytes));
            }
            fbf.MeshData = r.Bytes(dataBytes);
            if (r.Position != data.Length)
            {
                throw new InvalidDataException(string.Format("{0} bytes left after the mesh table", data.Length - r.Position));
            }
            return fbf;
        }

        public byte[] Write()
        {
            MemoryStream s = new MemoryStream();
            WriteU32(s, this.Version);
            WriteU32(s, (uint) this.Records.Count);
            foreach (Record rec in this.Records)
            {
                WriteU32(s, (uint) rec.Type);
                WriteU32(s, rec.Id);
                WriteString32(s, rec.Name);
                switch (rec.Type)
                {
                    case RecordType.Text:
                        s.Write(rec.TextHead, 0, rec.TextHead.Length);
                        WriteString16(s, rec.Text);
                        WriteString32(s, rec.TextTag);
                        s.Write(rec.TextMiddle, 0, rec.TextMiddle.Length);
                        WriteString16(s, rec.Font);
                        s.Write(rec.TextTail, 0, rec.TextTail.Length);
                        break;
                    case RecordType.Image:
                        WriteU32(s, rec.Owner);
                        WriteString32(s, rec.Path);
                        s.Write(rec.Body, 0, rec.Body.Length);
                        break;
                    default:
                        s.Write(rec.Body, 0, rec.Body.Length);
                        break;
                }
            }
            int descBytes = 0;
            foreach (Mesh mesh in this.Meshes)
            {
                descBytes += mesh.IndexDescs.Count * IndexDescSize;
            }
            WriteU32(s, (uint) this.Meshes.Count);
            WriteU32(s, (uint) descBytes);
            WriteU32(s, (uint) this.MeshData.Length);
            foreach (Mesh mesh in this.Meshes)
            {
                s.Write(mesh.VertexDesc, 0, mesh.VertexDesc.Length);
            }
            foreach (Mesh mesh in this.Meshes)
            {
                foreach (byte[] desc in mesh.IndexDescs)
                {
                    s.Write(desc, 0, desc.Length);
                }
            }
            s.Write(this.MeshData, 0, this.MeshData.Length);
            return s.ToArray();
        }

        // Links checked on all 431 dashes (FM4 234, FH1 94, FH2 103, 2026-11-07): a model's body u32 0 is its mesh, one
        // mesh per model in the order of the model records; a material's body u32 at 0x44 is the id of its model; a
        // model has as many materials as its mesh has index buffers (the game sizes the model's material list from the
        // mesh and reads that many: a copied model whose materials still named the original crashed FM4 on load).
        public const int MaterialModelOffset = 0x44;

        public static int MeshOf(Record model)
        {
            return (int) ReadU32(model.Body, 0);
        }

        public static uint ModelOf(Record material)
        {
            return ReadU32(material.Body, MaterialModelOffset);
        }

        public static void SetModelOf(Record material, uint modelId)
        {
            WriteU32(material.Body, MaterialModelOffset, modelId);
        }

        // A new mesh at the end of the table with the vertices and indices of mesh 'index' of 'src' (this file or
        // another), its data appended; returns its index. RepackMeshes puts it in its place.
        public int CloneMesh(AnarkFbf src, int index)
        {
            Mesh from = src.Meshes[index];
            Mesh copy = new Mesh();
            copy.VertexDesc = (byte[]) from.VertexDesc.Clone();
            // the bytes after the last piece stay at the end (RepackMeshes keeps them there)
            int used = this.UsedEnd();
            MemoryStream data = new MemoryStream();
            data.Write(this.MeshData, 0, used);
            int at = Align4(data);
            data.Write(src.MeshData, from.VertexOffset, from.VertexBytes);
            WriteU32(copy.VertexDesc, 18 * 4, (uint) at | (ReadU32(from.VertexDesc, 18 * 4) & 3u));
            foreach (byte[] desc in from.IndexDescs)
            {
                byte[] d = (byte[]) desc.Clone();
                at = Align4(data);
                data.Write(src.MeshData, Mesh.IndexOffset(desc), Mesh.IndexBytes(desc));
                WriteU32(d, 7 * 4, (uint) at);
                copy.IndexDescs.Add(d);
            }
            data.Write(this.MeshData, used, this.MeshData.Length - used);
            this.MeshData = data.ToArray();
            this.Meshes.Add(copy);
            return this.Meshes.Count - 1;
        }

        // The mesh table the way the game's files have it (431/431): mesh k belongs to the k-th model record, the data
        // of the meshes in that order (vertices, then each index buffer), each piece at the next 4-byte step. Meshes no
        // model uses are dropped; a mesh two models share is given to each. Bytes after the last piece are kept (one
        // FM4 file has 2). An unchanged file comes back byte for byte.
        public void RepackMeshes()
        {
            int oldEnd = this.UsedEnd();
            List<Mesh> meshes = new List<Mesh>();
            MemoryStream data = new MemoryStream();
            int descOffset = 0;
            int lastEnd = -1;   // old end of the piece written last: the padding after it is copied from there
            foreach (Record rec in this.Records)
            {
                if (rec.Type != RecordType.Model)
                {
                    continue;
                }
                int index = MeshOf(rec);
                if (index < 0 || index >= this.Meshes.Count)
                {
                    throw new InvalidDataException(string.Format("model {0} ({1}) uses mesh {2} of {3}", rec.Id, rec.Name, index, this.Meshes.Count));
                }
                Mesh from = this.Meshes[index];
                Mesh m = new Mesh();
                m.VertexDesc = (byte[]) from.VertexDesc.Clone();
                int at = this.Piece(data, from.VertexOffset, from.VertexBytes, ref lastEnd);
                WriteU32(m.VertexDesc, 18 * 4, (uint) at | (ReadU32(from.VertexDesc, 18 * 4) & 3u));
                WriteU32(m.VertexDesc, 20 * 4, (uint) descOffset);
                foreach (byte[] desc in from.IndexDescs)
                {
                    byte[] d = (byte[]) desc.Clone();
                    at = this.Piece(data, Mesh.IndexOffset(desc), Mesh.IndexBytes(desc), ref lastEnd);
                    WriteU32(d, 7 * 4, (uint) at);
                    m.IndexDescs.Add(d);
                    descOffset += IndexDescSize;
                }
                WriteU32(rec.Body, 0, (uint) meshes.Count);
                meshes.Add(m);
            }
            int trailing = Math.Max(0, this.MeshData.Length - oldEnd);
            data.Write(this.MeshData, this.MeshData.Length - trailing, trailing);
            this.Meshes = meshes;
            this.MeshData = data.ToArray();
        }

        // End of the last vertex or index piece in the data.
        private int UsedEnd()
        {
            int end = 0;
            foreach (Mesh m in this.Meshes)
            {
                end = Math.Max(end, m.VertexOffset + m.VertexBytes);
                foreach (byte[] d in m.IndexDescs)
                {
                    end = Math.Max(end, Mesh.IndexOffset(d) + Mesh.IndexBytes(d));
                }
            }
            return end;
        }

        // Pads to the next 4-byte step with the bytes that followed the previous piece in the old data (not always 0:
        // three Ferrari 458 scenes have other bytes there), then writes the piece; returns where it starts.
        private int Piece(MemoryStream s, int offset, int bytes, ref int lastEnd)
        {
            for (int n = 0; (s.Length & 3) != 0; n++)
            {
                int old = lastEnd + n;
                s.WriteByte((lastEnd >= 0 && old < this.MeshData.Length) ? this.MeshData[old] : (byte) 0);
            }
            int at = (int) s.Length;
            s.Write(this.MeshData, offset, bytes);
            lastEnd = offset + bytes;
            return at;
        }

        private static int Align4(MemoryStream s)
        {
            while ((s.Length & 3) != 0)
            {
                s.WriteByte(0);
            }
            return (int) s.Length;
        }

        internal static void WriteU32(byte[] data, int offset, uint v)
        {
            data[offset] = (byte) (v >> 24);
            data[offset + 1] = (byte) (v >> 16);
            data[offset + 2] = (byte) (v >> 8);
            data[offset + 3] = (byte) v;
        }

        public Record Find(uint id)
        {
            foreach (Record rec in this.Records)
            {
                if (rec.Id == id)
                {
                    return rec;
                }
            }
            return null;
        }

        internal static uint ReadU32(byte[] data, int offset)
        {
            return (uint) ((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }

        internal static float ReadFloat(byte[] data, int offset)
        {
            return BitConverter.ToSingle(BitConverter.GetBytes(ReadU32(data, offset)), 0);
        }

        internal static void WriteFloat(byte[] data, int offset, float value)
        {
            uint v = BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);
            data[offset] = (byte) (v >> 24);
            data[offset + 1] = (byte) (v >> 16);
            data[offset + 2] = (byte) (v >> 8);
            data[offset + 3] = (byte) v;
        }

        private static void WriteU32(Stream s, uint v)
        {
            s.WriteByte((byte) (v >> 24));
            s.WriteByte((byte) (v >> 16));
            s.WriteByte((byte) (v >> 8));
            s.WriteByte((byte) v);
        }

        private static void WriteString32(Stream s, string text)
        {
            byte[] b = Latin1.GetBytes(text);
            WriteU32(s, (uint) b.Length);
            s.Write(b, 0, b.Length);
        }

        private static void WriteString16(Stream s, string text)
        {
            byte[] b = Latin1.GetBytes(text);
            s.WriteByte((byte) (b.Length >> 8));
            s.WriteByte((byte) b.Length);
            s.Write(b, 0, b.Length);
        }

        private class Reader
        {
            private readonly byte[] data;
            public int Position;

            public Reader(byte[] data)
            {
                this.data = data;
            }

            private void Need(int n)
            {
                if (n < 0 || this.Position + n > this.data.Length)
                {
                    throw new InvalidDataException(string.Format("unexpected end of file at 0x{0:X} (+{1})", this.Position, n));
                }
            }

            public uint U32()
            {
                this.Need(4);
                uint v = ReadU32(this.data, this.Position);
                this.Position += 4;
                return v;
            }

            public byte[] Bytes(int n)
            {
                this.Need(n);
                byte[] b = new byte[n];
                Buffer.BlockCopy(this.data, this.Position, b, 0, n);
                this.Position += n;
                return b;
            }

            public string String32()
            {
                int n = (int) this.U32();
                if (n > 4096)
                {
                    throw new InvalidDataException(string.Format("string length {0} at 0x{1:X}", n, this.Position - 4));
                }
                return Latin1.GetString(this.Bytes(n));
            }

            public string String16()
            {
                this.Need(2);
                int n = (this.data[this.Position] << 8) | this.data[this.Position + 1];
                this.Position += 2;
                return Latin1.GetString(this.Bytes(n));
            }
        }
    }
}
