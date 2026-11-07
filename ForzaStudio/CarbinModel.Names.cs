using System;
using System.Collections.Generic;

namespace ForzaStudio
{
    // The game's names for the blocks CarbinModel reads as raw bytes (traced in default.xex, 2026-11-07,
    // docs/CARBIN_FROM_XEX.md). A carbin is a serialized object tree: every object starts with a u32 version
    // read from the file, then its fields. The raw fields stay as they are (byte-identical round trip, FH and
    // FM3 variants); these properties read and write the same bytes under the game's names, for code that
    // checks or builds a carbin.
    //
    // Section (CCarSubModel -> CCarPartData) and subsection (CCarMesh) as stored, FM4 car models:
    //   Marker                    CCarPartData version (5; FM3 2)
    //   Bounds                    origin, min, max
    //   Unk1c                     float the game clamps (1.0), mean vertex position (3 floats), float 0 / 0.2,
    //                             GlassParticles version 1, Vertices container version 1
    //   ACount + A                GlassParticles.Vertices (vec4)
    //   AfterA                    Indices container version 1
    //   BCount + B                GlassParticles.Indices (u16 triangles)
    //   Pre[0]                    bool (1 on glass, lexan, most headlights)
    //   Pre[1..3] + VbTest1[0]    u32 0
    //   VbTest1[1..3] + VbTest2[0] TSubModel version 1
    //   VbTest2[1..3] + name byte u32 name length (the name is a u32-length string)
    //   PostName                  VertexBuffer version 2 (FH 3)
    //   AfterLod                  Mesh container version 1, then the mesh count
    //   AfterSubs                 VertexBuffer_LOD0 version 2 (FH 3)
    //   Post9                     MorphTargets version 1, HasMorphs bool, Morph version 2 (FH 3)
    //   DamageLod                 Morph (the morph target of the Lod buffer)
    //   Mid                       Morph_LOD0 version 2 (FH 3)
    //   DamageLod0                Morph_LOD0
    // Subsection:
    //   Pre5                      CCarMaterialData version 3, RemoveFromShadow bool
    //   Uv                        CCarMaterialData vec4 0..1: UV offset / scale (U, V, U2, V2)
    //   Fixed36[0..31]            CCarMaterialData vec4 2..3 (unused, zero)
    //   Fixed36[32..35]           CCarMeshBase version 2
    //   Name, Lod, IndexType      Name, LOD, PrimitiveType (6 strip, 4 list)
    //   Fixed56[0..3]             MaterialIndex
    //   Fixed56[4..55]            UnpackingData: version 1, PositionOffset, PositionScale, UVOffsetScale (vec4)
    //   Three                     IndexBuffer version 3 (FH 4), then count and index size
    //   Tail                      meshFlags
    public partial class CarbinModel
    {
        public const int PrimitiveTriangleList = 4;
        public const int PrimitiveTriangleStrip = 6;

        // ReadF32(byte[], int) is in CarbinModelMotorsport3.cs.
        private static void WriteF32(byte[] data, int offset, float value)
        {
            WriteBigEndian(data, offset, BitConverter.ToUInt32(BitConverter.GetBytes(value), 0));
        }

        private static float[] ReadVec4(byte[] data, int offset)
        {
            return new float[] { ReadF32(data, offset), ReadF32(data, offset + 4), ReadF32(data, offset + 8), ReadF32(data, offset + 12) };
        }

        private static void WriteVec4(byte[] data, int offset, float[] value)
        {
            for (int i = 0; i < 4; i++)
            {
                WriteF32(data, offset + (i * 4), (value != null && i < value.Length) ? value[i] : 0f);
            }
        }

        private static uint WordAt(byte[] data, int offset)
        {
            return (data != null && data.Length >= offset + 4) ? ReadU32(data, offset) : 0;
        }

        public partial class SubSection
        {
            public uint MaterialDataVersion
            {
                get { return WordAt(this.Pre5, 0); }
            }

            public bool RemoveFromShadow
            {
                get { return this.Pre5 != null && this.Pre5.Length == 5 && this.Pre5[4] != 0; }
                set
                {
                    if (this.Pre5 != null && this.Pre5.Length == 5)
                    {
                        this.Pre5[4] = (byte) (value ? 1 : 0);
                    }
                }
            }

            public uint MeshBaseVersion
            {
                get { return WordAt(this.Fixed36, 32); }
            }

            public uint PrimitiveType
            {
                get { return this.IndexType; }
                set { this.IndexType = value; }
            }

            public uint MaterialIndex
            {
                get { return WordAt(this.Fixed56, 0); }
                set { WriteBigEndian(this.Fixed56, 0, value); }
            }

            public uint UnpackingDataVersion
            {
                get { return WordAt(this.Fixed56, 4); }
            }

            // Vertex position = PositionOffset + stored SHORT4N value x PositionScale (CPU decoder 828EDEB8).
            public float[] PositionOffset
            {
                get { return ReadVec4(this.Fixed56, 8); }
                set { WriteVec4(this.Fixed56, 8, value); }
            }

            public float[] PositionScale
            {
                get { return ReadVec4(this.Fixed56, 24); }
                set { WriteVec4(this.Fixed56, 24, value); }
            }

            public float[] UnpackingUvOffsetScale
            {
                get { return ReadVec4(this.Fixed56, 40); }
                set { WriteVec4(this.Fixed56, 40, value); }
            }

            public uint IndexBufferVersion
            {
                get { return this.Three; }
            }

            public uint MeshFlags
            {
                get { return WordAt(this.Tail, 0); }
            }
        }

        public partial class Section
        {
            public uint PartDataVersion
            {
                get { return WordAt(this.Marker, 0); }
            }

            // Unk1c float 0: clamped to a minimum by the game (8287FC30), 1.0 on every FM4 section.
            public float ClampedFloat
            {
                get { return (this.Unk1c != null && this.Unk1c.Length >= 4) ? ReadF32(this.Unk1c, 0) : 0f; }
            }

            // Unk1c floats 1..3, relative to the origin.
            public float[] MeanPosition
            {
                get
                {
                    if (this.Unk1c == null || this.Unk1c.Length < 16)
                    {
                        return new float[3];
                    }
                    return new float[] { ReadF32(this.Unk1c, 4), ReadF32(this.Unk1c, 8), ReadF32(this.Unk1c, 12) };
                }
            }

            // Unk1c float 4: 0, or 0.2 on parts that can fall off (mirrors, wings, bumperRa, exhausta) - hypothesis.
            public float DetachFloat
            {
                get { return (this.Unk1c != null && this.Unk1c.Length >= 20) ? ReadF32(this.Unk1c, 16) : 0f; }
            }

            public uint GlassParticlesVersion
            {
                get { return WordAt(this.Unk1c, 20); }
            }

            public uint GlassVerticesVersion
            {
                get { return WordAt(this.Unk1c, 24); }
            }

            public uint GlassIndicesVersion
            {
                get { return WordAt(this.AfterA, 0); }
            }

            public int GlassVertexCount
            {
                get { return (int) this.ACount; }
            }

            public int GlassIndexCount
            {
                get { return (int) this.BCount; }
            }

            // GlassParticles.Vertices: x, y, z, w per vertex.
            public float[] GlassVertices
            {
                get
                {
                    float[] result = new float[this.ACount * 4];
                    for (int i = 0; i < result.Length; i++)
                    {
                        result[i] = ReadF32(this.A, i * 4);
                    }
                    return result;
                }
            }

            public int[] GlassIndices
            {
                get
                {
                    int[] result = new int[this.BCount];
                    for (int i = 0; i < result.Length; i++)
                    {
                        result[i] = (this.B[i * 2] << 8) | this.B[(i * 2) + 1];
                    }
                    return result;
                }
            }

            // vertices: 4 floats each (x, y, z, w); indices: triangles (count % 3 = 0), each below 65536.
            public void SetGlassParticles(float[] vertices, IList<int> indices)
            {
                int count = (vertices == null) ? 0 : vertices.Length / 4;
                byte[] a = new byte[count * 16];
                for (int i = 0; i < count * 4; i++)
                {
                    WriteF32(a, i * 4, vertices[i]);
                }
                int n = (indices == null) ? 0 : indices.Count;
                byte[] b = new byte[n * 2];
                for (int i = 0; i < n; i++)
                {
                    b[i * 2] = (byte) (indices[i] >> 8);
                    b[(i * 2) + 1] = (byte) indices[i];
                }
                this.A = a;
                this.ACount = (uint) count;
                this.B = b;
                this.BCount = (uint) n;
            }

            // Pre[0]: 1 on all glass / lexan, most headlights, some body parts (hypothesis: "has glass").
            public bool PartFlag
            {
                get { return this.Pre != null && this.Pre.Length == 4 && this.Pre[0] != 0; }
            }

            public uint SubModelVersion
            {
                get { return this.Pre != null && this.Pre.Length == 4 ? (((this.VbTest1 & 0xFFFFFF) << 8) | (this.VbTest2 >> 24)) : 0; }
            }

            public uint VertexBufferVersion
            {
                get { return WordAt(this.PostName, 0); }
            }

            public uint MeshContainerVersion
            {
                get { return WordAt(this.AfterLod, 0); }
            }

            public uint VertexBufferLod0Version
            {
                get { return WordAt(this.AfterSubs, 0); }
            }

            public uint MorphTargetsVersion
            {
                get { return WordAt(this.Post9, 0); }
            }

            // MorphTargets.HasMorphs: the section deforms (same as IsDeformable).
            public bool HasMorphs
            {
                get { return this.IsDeformable; }
            }

            public uint MorphVersion
            {
                get { return WordAt(this.Post9, 5); }
            }

            public uint MorphLod0Version
            {
                get { return WordAt(this.Mid, 0); }
            }

            // The morph targets: one 10_11_11 SNORM direction per vertex (zero or unit length).
            public Table Morph
            {
                get { return this.DamageLod; }
            }

            public Table MorphLod0
            {
                get { return this.DamageLod0; }
            }
        }

        // Header of an FM4 car model (type 2), offsets inside Head. Null when the walk does not end where the
        // draw lists start (another file kind, or a damaged header).
        public sealed class HeaderLayout
        {
            public const int ModelDataVersion = 0x000;      // CCarModelData version 2
            public const int Matrices = 0x004;              // 4 x 4x4 matrix (no names in the code)
            public const int Vectors = 0x104;               // 5 x vec4 (no names)
            public const int BaseDataVersion = 0x154;       // CBaseCarModelData version 0x10
            public const int ShaderVersion = 0x158;         // 16
            public const int EmittersContainer = 0x15C;     // version 1, count
            public const int Emitters = 0x164;              // count x CCarEmitterData (140 bytes)
            public const int EmitterSize = 140;
            public const int DriverVectorCount = 16;
            public const int GaugesSize = CarGauges.Count * CarGauges.RecordSize;

            public int EmitterCount;
            public int DriverControls;       // CDriverControls version 3, then 16 vec4
            public int StartLod;             // StartLOD, EndLOD (u32 each)
            public int Gauges;               // 15 x (pivot vec4, axis vec4, MaxRotation)
            public int Floats;               // 2 floats (about -0.34 / 0.675 on every car)
            public int Word;                 // u32, 1
            public int BoneMappingTable;     // version 1, count 0
            public int Last8;                // 8 bytes not traced yet
            public int End;

            public int DriverVector(int index)
            {
                return this.DriverControls + 4 + (index * 16);
            }

            public static HeaderLayout Walk(byte[] head)
            {
                if (head == null || head.Length < Emitters || ReadU32(head, 0) != 2 || ReadU32(head, BaseDataVersion) != 0x10)
                {
                    return null;
                }
                HeaderLayout layout = new HeaderLayout();
                layout.EmitterCount = (int) ReadU32(head, EmittersContainer + 4);
                if (layout.EmitterCount < 0 || layout.EmitterCount > 64)
                {
                    return null;
                }
                layout.DriverControls = Emitters + (layout.EmitterCount * EmitterSize);
                layout.StartLod = layout.DriverControls + 4 + (DriverVectorCount * 16);
                layout.Gauges = layout.StartLod + 8;
                layout.Floats = layout.Gauges + GaugesSize;
                layout.Word = layout.Floats + 8;
                layout.BoneMappingTable = layout.Word + 4;
                layout.Last8 = layout.BoneMappingTable + 8;
                layout.End = layout.Last8 + 8;
                return (layout.End == head.Length) ? layout : null;
            }
        }

        // CDriverControls version 3 vectors in file order (names from default.xex; the order matches the
        // measured layout: grips at vectors 6..11, wheel pivot / axis at 12 / 13, shifter at 15).
        public static readonly string[] DriverControlNames =
        {
            "SteeringWheelPosition", "SteeringWheelAxis", "LeftHandAnchor", "RightHandAnchor", "ButtAnchor", "ShifterAnchor",
            "RightHandOuter", "LeftHandOuter", "RightHandInner", "LeftHandInner", "RightHandCenter", "LeftHandCenter",
            "SteeringPivotLOD0", "SteeringWheelAxisLOD0", "ButtAnchorPositionLOD0", "ShifterAnchorPositionLOD0",
        };
    }
}
