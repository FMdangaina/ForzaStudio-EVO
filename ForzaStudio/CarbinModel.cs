namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    // Structural model of a car .carbin (main, lod0 and cockpit files) for edits that change sizes:
    // moving or merging subsections between sections and removing sections. Every block the tool
    // does not understand is kept as raw bytes, so an untouched model writes back byte for byte.
    //   type 2  FM4: 32-byte vertices (8 trailing bytes).
    //   type 5  FH2 (and FH1): the same layout with 28-byte vertices (4 trailing bytes), a flag word
    //           (and an ID) after each buffer's count and stride, a longer header (extra 0x20-byte
    //           records) and one more word before each index list.
    //   FM3     cars and brakes type 1, wheels type 2 (see IsMotorsport3): read for study only, through
    //           CarbinModel(data, true); the editing code assumes the FM4/FH vertex encoding.
    //
    // Header draw lists: after the fixed header come 2 x N lists (N = LOD count of the file, two
    // render passes). Each entry is (section index << 16) | subsection index, so every structural
    // change must renumber them.
    public partial class CarbinModel
    {
        // FH buffers and tables carry a word after count and stride: 0 when empty, else 1 followed by a
        // 32-bit ID that is unique in the car and is no checksum of the data (see BufferId).
        public class VertexBuffer
        {
            public int Count;
            public int Stride;
            public byte[] Extra = new byte[0];   // FH: 00000000, or 00000001 + ID
            public byte[] Data = new byte[0];
            internal byte[] ExtraFor;            // the Data the read ID belongs to
        }

        public class Table
        {
            public int Count;
            public int Stride;
            public byte[] Extra = new byte[0];   // FH: as for vertex buffers
            public byte[] Data = new byte[0];
            internal byte[] ExtraFor;
        }

        // The game names of the raw blocks below (CCarMesh, CCarMaterialData, UnpackingData...): CarbinModel.Names.cs.
        public partial class SubSection
        {
            public byte[] Pre5;                  // FM3: 9 bytes, and no Uv or Fixed36
            public float[] Uv = new float[8];    // offset/scale for U, V, U2, V2
            public byte[] Fixed36;
            public string Name;
            public uint Lod;
            public uint IndexType;
            public byte[] Fixed56;
            public uint Three;                           // 3 (FM4), 4 (FH)
            public uint IndexWord;                       // FH only: the word before the index list
            public byte[] Stripped;                      // FH only, IndexWord 2: 8 bytes and no indices (stripped_*.carbin)
            public int IndexSize;
            public List<int> Indices = new List<int>();  // raw values, restart markers included
            public byte[] Tail;

            public SubSection Copy()
            {
                SubSection copy = (SubSection) this.MemberwiseClone();
                copy.Pre5 = (byte[]) this.Pre5.Clone();
                copy.Uv = (float[]) this.Uv.Clone();
                copy.Fixed36 = (byte[]) this.Fixed36.Clone();
                copy.Fixed56 = (byte[]) this.Fixed56.Clone();
                copy.Tail = (byte[]) this.Tail.Clone();
                copy.Stripped = (this.Stripped != null) ? (byte[]) this.Stripped.Clone() : null;
                copy.Indices = new List<int>(this.Indices);
                return copy;
            }
        }

        // The game names of the raw blocks below (CCarSubModel, GlassParticles, MorphTargets...): CarbinModel.Names.cs.
        public partial class Section
        {
            public byte[] Marker = new byte[0];
            public float[] Bounds = new float[9];        // origin, min, max (min/max relative to origin)
            public byte[] Unk1c;
            public uint ACount;
            public byte[] A;
            public byte[] AfterA;
            public uint BCount;
            public byte[] B;
            public byte[] Pre;                           // FM3: none
            public uint VbTest1;
            public uint VbTest2;
            public string Name;
            public byte[] PostName;
            public VertexBuffer Lod = new VertexBuffer();
            public byte[] AfterLod;
            public List<SubSection> SubSections = new List<SubSection>();
            public byte[] AfterSubs;
            public VertexBuffer Lod0 = new VertexBuffer();
            // FM3 sections end with Lod0: Post9 and Mid are empty and the damage tables are not written.
            public byte[] Post9;
            public Table DamageLod = new Table();   // 4 bytes per Lod vertex, only on deformable sections
            public byte[] Mid;
            public Table DamageLod0 = new Table();  // 4 bytes per Lod0 vertex, only on deformable sections

            public bool IsDeformable
            {
                get { return this.Post9.Length > 4 && this.Post9[4] != 0; }
            }
        }

        public uint Type;

        // FH files (car models type 5, and FH wheels): 28-byte vertices and the FH block sizes.
        public bool IsHorizon
        {
            get { return this.horizon; }
        }

        private bool horizon;

        // FM3 files (verified on all 3810 car and wheel carbins and the 120 Media\Brakes files of DVD1):
        //  - cars (main, lod0, cockpit) and brakes are type 1, wheels type 2; the word at 4 is 0xD on
        //    brakes (FM4 0x10, FH 0x11). Header as in the legacy ForzaCarBin reader: 8 bytes, 0x100 more
        //    (type 1) or 20 (type 2) unless that word is 0xD, 8, N records of 140 bytes, 0x1AC;
        //  - sections: marker 2, no Pre word before VbTest1/VbTest2, nothing after Lod0 (no Post9,
        //    damage tables or Mid);
        //  - subsections: 9 bytes (2, a byte, 2) instead of 5 + the 8 UV floats + 36;
        //  - vertices are half floats: xyz a unit direction, w the radius, position = origin + xyz * w
        //    (no bounds scale); 40 bytes on car parts, 16 on brakes, 44 on wheels.
        public bool IsMotorsport3
        {
            get { return this.motorsport3; }
        }

        private bool motorsport3;
        private bool allowMotorsport3;

        // FH (type 5) -> FM4 (type 2), measured on the Supra, which both games have:
        //  - header: FH has N 0x20-byte records after 0x154 (no FM4 counterpart, dropped) and format
        //    word 0x11 where FM4 has 0x10; the rest is the same layout (FM4 files also hold leftover
        //    memory in their first bytes, which the game ignores);
        //  - per section the version words PostName, AfterSubs, Post9[5..8] and Mid are 3 in FH, 2 in
        //    FM4; per subsection Three is 4 in FH, 3 in FM4, and FH's extra index word goes;
        //  - buffers lose the FH flag/ID; 28-byte vertices grow to 32: the 4 trailing SNORM8 values
        //    become the FM4 SNORM16 ones (same meaning, see WriteTail).
        //  - wheels (type 3) and calipers/rotors (type 1) keep their header but for the format word
        //    (FH 0x11, FM4 0x10: at 0x48 on wheels, at 4 on brakes; the only header difference on the
        //    Supra's brakes and on all 120 Media\brakes files of both games). A file without sections
        //    (brakes\Circle01, drum_null_caliper) reads as FM4, so the word is fixed first.
        public void ConvertHorizonToMotorsport4()
        {
            if (this.Type == 1 || this.Type == 3)
            {
                int word = (this.Type == 3) ? 0x48 : 4;
                if (ReadU32(this.Head, word) == 0x11)
                {
                    this.Head[word + 3] = 0x10;
                }
            }
            if (!this.IsHorizon)
            {
                return;
            }
            if (this.Type == 1 || this.Type == 3)
            {
                this.ConvertSectionsToMotorsport4();
                return;
            }
            int extra = (int) ReadU32(this.Head, 0x154);
            int fmStart = 0x158 + (extra * 0x20);
            using (MemoryStream head = new MemoryStream())
            {
                head.Write(this.Head, 0, 0x154);
                head.Write(this.Head, fmStart, this.Head.Length - fmStart);
                byte[] converted = head.ToArray();
                converted[0] = 0;
                converted[1] = 0;
                converted[2] = 0;
                converted[3] = 2;
                if (ReadU32(converted, 0x154) == 0x11)
                {
                    converted[0x157] = 0x10;
                }
                this.Head = converted;
            }
            this.ConvertSectionsToMotorsport4();
            this.Type = 2;
        }

        private void ConvertSectionsToMotorsport4()
        {
            foreach (Section section in this.Sections)
            {
                // Unk1c starts with a float that is 1.0 in every FM4 section (and in FH2 cars but late
                // ones: the P1 has 0.1 on its lod0 and cockpit parts, which FM4 did not draw).
                section.Unk1c[0] = 0x3F;
                section.Unk1c[1] = 0x80;
                section.Unk1c[2] = 0;
                section.Unk1c[3] = 0;
                foreach (byte[] word in new byte[][] { section.PostName, section.AfterSubs, section.Mid })
                {
                    if (ReadU32(word, 0) == 3)
                    {
                        word[3] = 2;
                    }
                }
                if (section.Post9.Length == 9 && section.Post9[8] == 3)
                {
                    section.Post9[8] = 2;
                }
                foreach (SubSection sub in section.SubSections)
                {
                    if (sub.Stripped != null)
                    {
                        throw new NotSupportedException(section.Name + ": stripped FH subsections (no index list) have no FM4 form.");
                    }
                    if (sub.Three == 4)
                    {
                        sub.Three = 3;
                    }
                    sub.IndexWord = 0;
                }
                section.Lod = WidenVertices(section.Lod);
                section.Lod0 = WidenVertices(section.Lod0);
                ConvertDamageWords(section);
                section.DamageLod.Extra = new byte[0];
                section.DamageLod0.Extra = new byte[0];
            }
            this.horizon = false;
        }

        // EXPERIMENT (ROADMAP B1, tests T2 / T3, Port.ini [Damage]): the FH per-vertex damage word is a signed
        // 11:11:10 vector of length ~90 (a magnitude); FM4's is a unit direction, signed normalised 10:11:11
        // (x / 511, y / 1023, z / 1023). Words = copy leaves the FH words as they are (what the port always
        // did); Words = normalize writes normalize(FH) in the FM4 format, the sign of the sections listed in
        // FlipSections reversed. A zero FH word stays zero. Tables = both | main | lod0 chooses which of the two
        // tables (main carbin LOD, lod0) is converted. Only the converted tables change.
        private static void ConvertDamageWords(Section section)
        {
            if (PortSettings.Get("Damage", "Words", "donor").ToLowerInvariant() != "normalize")
            {
                return;
            }
            string tables = PortSettings.Get("Damage", "Tables", "both").ToLowerInvariant();
            bool flip = false;
            foreach (string name in PortSettings.Get("Damage", "FlipSections", "").Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                flip = flip || string.Equals(name, section.Name, StringComparison.OrdinalIgnoreCase) || name == "*";
            }
            if (tables != "lod0")
            {
                NormalizeDamage(section.DamageLod, flip);
            }
            if (tables != "main")
            {
                NormalizeDamage(section.DamageLod0, flip);
            }
        }

        // FH 11:11:10 words -> unit direction in the FM4 10:11:11 format (see ConvertDamageWords).
        public static void NormalizeDamage(Table table, bool flip)
        {
            if (table.Stride != 4 || table.Data.Length < 4)
            {
                return;
            }
            for (int i = 0; i + 4 <= table.Data.Length; i += 4)
            {
                uint word = ReadU32(table.Data, i);
                double x = SignExtend(word >> 21, 11);
                double y = SignExtend(word >> 10, 11);
                double z = SignExtend(word, 10);
                double length = Math.Sqrt((x * x) + (y * y) + (z * z));
                if (length < 0.5)
                {
                    continue;
                }
                double sign = flip ? -1.0 : 1.0;
                uint px = (uint) (int) Math.Round(sign * x / length * 511) & 0x3FF;
                uint py = (uint) (int) Math.Round(sign * y / length * 1023) & 0x7FF;
                uint pz = (uint) (int) Math.Round(sign * z / length * 1023) & 0x7FF;
                uint packed = (px << 22) | (py << 11) | pz;
                table.Data[i] = (byte) (packed >> 24);
                table.Data[i + 1] = (byte) (packed >> 16);
                table.Data[i + 2] = (byte) (packed >> 8);
                table.Data[i + 3] = (byte) packed;
            }
        }

        // EXPERIMENT (ROADMAP B1, test T4, Port.ini [Damage] Words = donor): this model is in FM4 form; each
        // deformable section takes the damage word of every vertex from the nearest vertex of the same section
        // in 'reference', the same car's FM4 file (FH and FM4 positions are identical). The reference decides how
        // FM4 deforms the car: if the port still does not deform with these words, the words are not the cause.
        // Returns a line for the report.
        public bool DamageByShape;     // set on a reference of ANOTHER car (BorrowDamageWords): vertices match by place in the section box, not by position

        public string BorrowDamageWords(CarbinModel reference)
        {
            return this.BorrowDamageWords(reference, null);
        }

        // only: the one section to do, or null for every deformable section.
        public string BorrowDamageWords(CarbinModel reference, string only)
        {
            int vertices = 0;
            int sections = 0;
            double worst = 0;
            List<string> missing = new List<string>();
            foreach (Section section in this.Sections)
            {
                Section from = reference.Find(section.Name);
                if (!section.IsDeformable || (only != null && section.Name != only))
                {
                    continue;
                }
                if (from == null || !from.IsDeformable)
                {
                    missing.Add(section.Name);
                    continue;
                }
                bool any = false;
                for (int pass = 0; pass < 2; pass++)
                {
                    VertexBuffer buffer = (pass == 0) ? section.Lod : section.Lod0;
                    VertexBuffer other = (pass == 0) ? from.Lod : from.Lod0;
                    Table table = (pass == 0) ? section.DamageLod : section.DamageLod0;
                    Table otherTable = (pass == 0) ? from.DamageLod : from.DamageLod0;
                    if (buffer.Count == 0 || other.Count == 0 || table.Stride != 4 || otherTable.Stride != 4 || table.Count != buffer.Count || otherTable.Count != other.Count)
                    {
                        continue;
                    }
                    double[] mine = Positions(section, buffer);
                    double[] theirs = Positions(from, other);
                    if (reference.DamageByShape)
                    {
                        // Another car: place in the section's box (0..1 on each axis), so a longer or wider part still maps onto the same corner.
                        BoxCoordinates(section, mine);
                        BoxCoordinates(from, theirs);
                    }
                    int[] order = new int[other.Count];
                    for (int i = 0; i < order.Length; i++)
                    {
                        order[i] = i;
                    }
                    Array.Sort(order, delegate(int a, int b) { return theirs[a * 3].CompareTo(theirs[b * 3]); });
                    for (int v = 0; v < buffer.Count; v++)
                    {
                        double x = mine[v * 3];
                        double y = mine[(v * 3) + 1];
                        double z = mine[(v * 3) + 2];
                        int lo = 0;
                        int hi = order.Length;
                        while (lo < hi)
                        {
                            int mid = (lo + hi) / 2;
                            if (theirs[order[mid] * 3] < x)
                            {
                                lo = mid + 1;
                            }
                            else
                            {
                                hi = mid;
                            }
                        }
                        int best = -1;
                        double bestDistance = double.MaxValue;
                        for (int step = 0; step < 2; step++)
                        {
                            for (int i = (step == 0) ? lo : lo - 1; i >= 0 && i < order.Length; i += (step == 0) ? 1 : -1)
                            {
                                double dx = theirs[order[i] * 3] - x;
                                if (dx * dx >= bestDistance)
                                {
                                    break;
                                }
                                double dy = theirs[(order[i] * 3) + 1] - y;
                                double dz = theirs[(order[i] * 3) + 2] - z;
                                double distance = (dx * dx) + (dy * dy) + (dz * dz);
                                if (distance < bestDistance)
                                {
                                    bestDistance = distance;
                                    best = order[i];
                                }
                            }
                        }
                        if (best >= 0)
                        {
                            Buffer.BlockCopy(otherTable.Data, best * 4, table.Data, v * 4, 4);
                            vertices++;
                            worst = Math.Max(worst, Math.Sqrt(bestDistance));
                            any = true;
                        }
                    }
                }
                sections += any ? 1 : 0;
            }
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "damage words from the FM4 file (Port.ini Damage Words = donor){3}: {0} vertices in {1} deformable sections, farthest match {2:0.0000}{4}{5}", vertices, sections, worst,
                reference.DamageByShape ? " of another car, matched by place in the section box" : "", reference.DamageByShape ? " (box units)" : " m",
                (missing.Count > 0) ? "; sections the FM4 file lacks, source words kept: " + string.Join(", ", missing.ToArray()) : "");
        }

        // Positions (3 per vertex) -> 0..1 on each axis of the section's own box.
        private static void BoxCoordinates(Section section, double[] positions)
        {
            for (int c = 0; c < 3; c++)
            {
                double lo = section.Bounds[c] + section.Bounds[3 + c];
                double size = section.Bounds[6 + c] - section.Bounds[3 + c];
                for (int i = c; i < positions.Length; i += 3)
                {
                    positions[i] = (size > 1e-6) ? (positions[i] - lo) / size : 0.5;
                }
            }
        }

        private static int SignExtend(uint value, int bits)
        {
            int v = (int) (value & ((1u << bits) - 1));
            return (v >= (1 << (bits - 1))) ? v - (1 << bits) : v;
        }

        // 28-byte FH vertices -> 32-byte FM4 vertices.
        private static VertexBuffer WidenVertices(VertexBuffer buffer)
        {
            VertexBuffer wide = new VertexBuffer();
            wide.Count = buffer.Count;
            wide.Stride = (buffer.Count > 0) ? 0x20 : buffer.Stride;
            if (buffer.Count == 0 || buffer.Stride != 0x1c)
            {
                wide.Stride = (buffer.Count > 0) ? buffer.Stride : 0;
                wide.Data = buffer.Data;
                return wide;
            }
            wide.Data = new byte[buffer.Count * 0x20];
            for (int i = 0; i < buffer.Count; i++)
            {
                Buffer.BlockCopy(buffer.Data, i * 0x1c, wide.Data, i * 0x20, 24);
                for (int k = 0; k < 4; k++)
                {
                    sbyte value = (sbyte) buffer.Data[(i * 0x1c) + 24 + k];
                    WriteS16(wide.Data, (i * 0x20) + 24 + (k * 2), Clamp((int) Math.Round(Math.Max(-1.0, value / 127.0) * 32767)));
                }
            }
            return wide;
        }

        private static uint ReadU32(byte[] data, int offset)
        {
            return (uint) ((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }

        // Bytes per vertex of the file: 32 (FM4) or 28 (FH).
        public int VertexStride
        {
            get { return this.IsHorizon ? 0x1c : 0x20; }
        }
        public byte[] Head;                         // everything before the draw lists
        public List<uint[]> DrawLists = new List<uint[]>();
        public byte[] BeforeSections;
        public List<Section> Sections = new List<Section>();
        public byte[] Trailer;

        private byte[] data;
        private int position;

        public CarbinModel(byte[] file) : this(file, false)
        {
        }

        // allowMotorsport3: read FM3 files too (study only); otherwise they are refused.
        public CarbinModel(byte[] file, bool allowMotorsport3)
        {
            this.allowMotorsport3 = allowMotorsport3;
            this.data = file;
            this.Parse();
            this.data = null;
        }

        public Section Find(string name)
        {
            foreach (Section section in this.Sections)
            {
                if (section.Name == name)
                {
                    return section;
                }
            }
            return null;
        }

        #region Reading

        private uint U32()
        {
            uint v = (uint) ((this.data[this.position] << 24) | (this.data[this.position + 1] << 16) | (this.data[this.position + 2] << 8) | this.data[this.position + 3]);
            this.position += 4;
            return v;
        }

        private float F32()
        {
            byte[] b = { this.data[this.position + 3], this.data[this.position + 2], this.data[this.position + 1], this.data[this.position] };
            this.position += 4;
            return BitConverter.ToSingle(b, 0);
        }

        private byte[] Bytes(int length)
        {
            byte[] result = new byte[length];
            Buffer.BlockCopy(this.data, this.position, result, 0, length);
            this.position += length;
            return result;
        }

        private VertexBuffer ReadBuffer()
        {
            VertexBuffer buffer = new VertexBuffer();
            buffer.Count = (int) this.U32();
            buffer.Stride = (int) this.U32();
            buffer.Extra = this.ReadExtra();
            buffer.Data = this.Bytes(IsStripped(buffer.Extra) ? 0 : buffer.Count * buffer.Stride);
            buffer.ExtraFor = buffer.Data;
            return buffer;
        }

        private Table ReadTable()
        {
            Table table = new Table();
            table.Count = (int) this.U32();
            table.Stride = (int) this.U32();
            table.Extra = this.ReadExtra();
            table.Data = this.Bytes(IsStripped(table.Extra) ? 0 : table.Count * table.Stride);
            table.ExtraFor = table.Data;
            return table;
        }

        // FH stripped_*.carbin: flag 3 and the ID, and no data (count and stride kept).
        private static bool IsStripped(byte[] extra)
        {
            return extra.Length == 8 && extra[0] == 0 && extra[1] == 0 && extra[2] == 0 && extra[3] == 3;
        }

        // FH: the flag word, and the ID when the flag is set.
        private byte[] ReadExtra()
        {
            if (!this.IsHorizon)
            {
                return new byte[0];
            }
            int start = this.position;
            uint flag = this.U32();
            this.position = start;
            return this.Bytes((flag != 0) ? 8 : 4);
        }

        private void Parse()
        {
            this.position = 0;
            this.Type = this.U32();
            if (IsMotorsport3File(this.data))
            {
                if (!this.allowMotorsport3)
                {
                    throw new NotSupportedException("Forza Motorsport 3 .carbin: it can be read for study, but editing FM3 models is not supported.");
                }
                this.motorsport3 = true;
                this.ParseFrom(Motorsport3HeadLength(this.data));
                return;
            }
            if (this.Type == 1 || this.Type == 3)
            {
                // Wheels (type 3, 920-byte header) and calipers/rotors (type 1, 0x354): a fixed header
                // in both games; FM4 and FH differ only in the section layout, so the FM4 reading is
                // tried first and FH when it does not end on the trailer.
                int headLength = 4 + ((this.Type == 3) ? 920 : 0x354);
                try
                {
                    this.horizon = false;
                    this.ParseFrom(headLength);
                    if (this.Trailer.Length == 24)
                    {
                        return;
                    }
                }
                catch (Exception)
                {
                }
                this.horizon = true;
                this.ParseFrom(headLength);
                return;
            }
            if (this.Type != 2 && this.Type != 5)
            {
                throw new NotSupportedException(string.Format("Structural editing of .carbin type {0} is not supported (car models: FM4 type 2, FH type 5; wheels: type 3; brakes: type 1).", this.Type));
            }
            this.horizon = this.Type == 5;
            if (this.IsHorizon)
            {
                // FH: N records of 0x20 bytes and 12 more bytes before the FM4-style record table.
                this.position += 0x150;
                uint extra = this.U32();
                this.position += ((int) extra * 0x20) + 12;
            }
            else
            {
                this.position += 0x15c;
            }
            uint records = this.U32();
            this.position += 4 + ((int) records * 140) + 0x340;
            this.ParseFrom(this.position);
        }

        // FM3: type 1 with 0xD at 4 (brakes) or a large word there (a float on main files, 0x390000 or
        // 0x3A0000 on lod0/cockpit; FM4/FH have 0x10/0x11, FM2 files small values such as 3, 4, 0x2C9), or
        // type 2 without the FM4 word 0x10 at 0x154 and not FM2 (0x2CA at 0x104).
        public static bool IsMotorsport3File(byte[] file)
        {
            if (file.Length < 0x158)
            {
                return false;
            }
            uint type = ReadU32(file, 0);
            uint format = ReadU32(file, 4);
            if (format == 0x2CA || ReadU32(file, 0x104) == 0x2CA)
            {
                return false;
            }
            return (type == 1 && (format == 0xD || format >= 0x10000)) || (type == 2 && ReadU32(file, 0x154) != 0x10);
        }

        private static int Motorsport3HeadLength(byte[] file)
        {
            int position = 8;
            if (ReadU32(file, 4) != 0xD)
            {
                position += (ReadU32(file, 0) == 1) ? 0x100 : 20;
            }
            position += 8;
            return position + 4 + ((int) ReadU32(file, position) * 140) + 0x1AC;
        }

        // Everything after the header (draw lists, sections, trailer).
        private void ParseFrom(int headLength)
        {
            this.DrawLists.Clear();
            this.Sections.Clear();
            this.position = 0;
            this.Head = this.Bytes(headLength);
            uint lists = this.U32() * 2;
            for (int i = 0; i < lists; i++)
            {
                uint[] list = new uint[this.U32()];
                for (int k = 0; k < list.Length; k++)
                {
                    list[k] = this.U32();
                }
                this.DrawLists.Add(list);
            }
            this.BeforeSections = this.Bytes(4);
            uint count = this.U32();
            for (int s = 0; s < count; s++)
            {
                this.Sections.Add(this.ReadSection());
            }
            this.Trailer = this.Bytes(this.data.Length - this.position);
        }

        private Section ReadSection()
        {
            Section section = new Section();
            int start = this.position;
            uint marker = this.U32();
            if (marker == 0x2c9 || marker == 2 || marker == 5)
            {
                this.position = start;
                section.Marker = this.Bytes(4);
            }
            else
            {
                this.position = start;
            }
            for (int i = 0; i < 9; i++)
            {
                section.Bounds[i] = this.F32();
            }
            section.Unk1c = this.Bytes(0x1c);
            section.ACount = this.U32();
            section.A = this.Bytes((int) section.ACount * 16);
            section.AfterA = this.Bytes(4);
            section.BCount = this.U32();
            section.B = this.Bytes((int) section.BCount * 2);
            section.Pre = this.Bytes(this.motorsport3 ? 0 : 4);
            section.VbTest1 = this.U32();
            section.VbTest2 = this.U32();
            int nameLength = this.data[this.position++];
            section.Name = Encoding.ASCII.GetString(this.data, this.position, nameLength);
            this.position += nameLength;
            section.PostName = this.Bytes(4);
            section.Lod = this.ReadBuffer();
            section.AfterLod = this.Bytes(4);
            uint subs = this.U32();
            for (int k = 0; k < subs; k++)
            {
                SubSection sub = new SubSection();
                sub.Pre5 = this.Bytes(this.motorsport3 ? 9 : 5);
                if (!this.motorsport3)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        sub.Uv[i] = this.F32();
                    }
                }
                sub.Fixed36 = this.Bytes(this.motorsport3 ? 0 : 0x24);
                int subNameLength = (int) this.U32();
                sub.Name = Encoding.ASCII.GetString(this.data, this.position, subNameLength);
                this.position += subNameLength;
                sub.Lod = this.U32();
                sub.IndexType = this.U32();
                sub.Fixed56 = this.Bytes(56);
                sub.Three = this.U32();
                int indexCount = 0;
                if (this.IsHorizon)
                {
                    sub.IndexWord = this.U32();
                    if (sub.IndexWord == 2)
                    {
                        sub.Stripped = this.Bytes(8);
                    }
                }
                if (sub.Stripped == null)
                {
                    indexCount = (int) this.U32();
                    sub.IndexSize = (int) this.U32();
                }
                for (int i = 0; i < indexCount; i++)
                {
                    if (sub.IndexSize == 2)
                    {
                        sub.Indices.Add((this.data[this.position] << 8) | this.data[this.position + 1]);
                        this.position += 2;
                    }
                    else
                    {
                        sub.Indices.Add((int) this.U32());
                    }
                }
                sub.Tail = this.Bytes(4);
                section.SubSections.Add(sub);
            }
            section.AfterSubs = this.Bytes(4);
            section.Lod0 = this.ReadBuffer();
            if (this.motorsport3)
            {
                section.Post9 = new byte[0];
                section.Mid = new byte[0];
                return section;
            }
            section.Post9 = this.Bytes(9);
            section.DamageLod = this.ReadTable();
            section.Mid = this.Bytes(4);
            section.DamageLod0 = this.ReadTable();
            return section;
        }

        #endregion

        #region Writing

        public byte[] ToBytes()
        {
            this.SplitLongSubSections();      // the end of a very long index list = holes in game (CarbinModel.LongMeshes)
            this.FixTransparentPartFlags();   // see-through material in a part with flag 0 = not drawn in game
            this.FixDrawListPasses();         // glass in the opaque pass (or the reverse) = not drawn in game
            using (MemoryStream output = new MemoryStream())
            {
                Write(output, this.Head);
                WriteU32(output, (uint) (this.DrawLists.Count / 2));
                foreach (uint[] list in this.DrawLists)
                {
                    WriteU32(output, (uint) list.Length);
                    foreach (uint entry in list)
                    {
                        WriteU32(output, entry);
                    }
                }
                Write(output, this.BeforeSections);
                WriteU32(output, (uint) this.Sections.Count);
                foreach (Section section in this.Sections)
                {
                    WriteSection(this, output, section);
                }
                Write(output, this.Trailer);
                return output.ToArray();
            }
        }

        private static void WriteSection(CarbinModel model, Stream output, Section section)
        {
            Write(output, section.Marker);
            foreach (float value in section.Bounds)
            {
                WriteF32(output, value);
            }
            Write(output, section.Unk1c);
            WriteU32(output, section.ACount);
            Write(output, section.A);
            Write(output, section.AfterA);
            WriteU32(output, section.BCount);
            Write(output, section.B);
            Write(output, section.Pre);
            WriteU32(output, section.VbTest1);
            WriteU32(output, section.VbTest2);
            byte[] name = Encoding.ASCII.GetBytes(section.Name);
            output.WriteByte((byte) name.Length);
            Write(output, name);
            Write(output, section.PostName);
            WriteBuffer(model, output, section.Lod, section.Name + "/lod");
            Write(output, section.AfterLod);
            WriteU32(output, (uint) section.SubSections.Count);
            foreach (SubSection sub in section.SubSections)
            {
                Write(output, sub.Pre5);
                if (!model.IsMotorsport3)
                {
                    foreach (float value in sub.Uv)
                    {
                        WriteF32(output, value);
                    }
                }
                Write(output, sub.Fixed36);
                byte[] subName = Encoding.ASCII.GetBytes(sub.Name);
                WriteU32(output, (uint) subName.Length);
                Write(output, subName);
                WriteU32(output, sub.Lod);
                WriteU32(output, sub.IndexType);
                Write(output, sub.Fixed56);
                WriteU32(output, sub.Three);
                if (model.IsHorizon)
                {
                    WriteU32(output, sub.IndexWord);
                }
                if (sub.Stripped != null)
                {
                    Write(output, sub.Stripped);
                }
                else
                {
                    WriteU32(output, (uint) sub.Indices.Count);
                    WriteU32(output, (uint) sub.IndexSize);
                }
                foreach (int index in sub.Indices)
                {
                    if (sub.IndexSize == 2)
                    {
                        output.WriteByte((byte) (index >> 8));
                        output.WriteByte((byte) index);
                    }
                    else
                    {
                        WriteU32(output, (uint) index);
                    }
                }
                Write(output, sub.Tail);
            }
            Write(output, section.AfterSubs);
            WriteBuffer(model, output, section.Lod0, section.Name + "/lod0");
            if (model.IsMotorsport3)
            {
                return;
            }
            Write(output, section.Post9);
            WriteTable(model, output, section.DamageLod, section.Name + "/damage");
            Write(output, section.Mid);
            WriteTable(model, output, section.DamageLod0, section.Name + "/damage0");
        }

        private static void WriteBuffer(CarbinModel model, Stream output, VertexBuffer buffer, string owner)
        {
            WriteU32(output, (uint) buffer.Count);
            WriteU32(output, (uint) buffer.Stride);
            if (model.IsHorizon)
            {
                buffer.Extra = BufferExtra(buffer.Extra, buffer.ExtraFor, buffer.Data, buffer.Count, owner);
                buffer.ExtraFor = buffer.Data;
            }
            Write(output, buffer.Extra);
            Write(output, buffer.Data);
        }

        private static void WriteTable(CarbinModel model, Stream output, Table table, string owner)
        {
            WriteU32(output, (uint) table.Count);
            WriteU32(output, (uint) table.Stride);
            if (model.IsHorizon)
            {
                table.Extra = BufferExtra(table.Extra, table.ExtraFor, table.Data, table.Count, owner);
                table.ExtraFor = table.Data;
            }
            Write(output, table.Extra);
            Write(output, table.Data);
        }

        // FH: flag 0 for an empty buffer; else flag 1 and the ID, kept while the buffer holds the data
        // it was read with (edits in place keep it too), a new one for a rebuilt or copied buffer: no
        // two buffers of a car share an ID in the game's files. The new ID is a hash of the owner's
        // name and the data, so the same edit writes the same bytes.
        private static byte[] BufferExtra(byte[] extra, byte[] extraFor, byte[] data, int count, string owner)
        {
            if (count == 0)
            {
                return new byte[4];
            }
            if (IsStripped(extra) && data.Length == 0)
            {
                return extra;
            }
            if (extra.Length == 8 && extra[3] == 1 && object.ReferenceEquals(extraFor, data))
            {
                return extra;
            }
            uint hash = 2166136261;
            foreach (byte b in Encoding.ASCII.GetBytes(owner))
            {
                hash = (hash ^ b) * 16777619;
            }
            foreach (byte b in data)
            {
                hash = (hash ^ b) * 16777619;
            }
            return new byte[] { 0, 0, 0, 1, (byte) (hash >> 24), (byte) (hash >> 16), (byte) (hash >> 8), (byte) hash };
        }

        private static void Write(Stream output, byte[] bytes)
        {
            output.Write(bytes, 0, bytes.Length);
        }

        private static void WriteU32(Stream output, uint value)
        {
            output.WriteByte((byte) (value >> 24));
            output.WriteByte((byte) (value >> 16));
            output.WriteByte((byte) (value >> 8));
            output.WriteByte((byte) value);
        }

        private static void WriteF32(Stream output, float value)
        {
            byte[] b = BitConverter.GetBytes(value);
            output.WriteByte(b[3]);
            output.WriteByte(b[2]);
            output.WriteByte(b[1]);
            output.WriteByte(b[0]);
        }

        #endregion

        #region Vertices

        private static short S16(byte[] d, int o)
        {
            return (short) ((d[o] << 8) | d[o + 1]);
        }

        private static void WriteS16(byte[] d, int o, int value)
        {
            d[o] = (byte) (value >> 8);
            d[o + 1] = (byte) value;
        }

        private static double Scale(float[] bounds)
        {
            double sum = 0;
            for (int c = 0; c < 3; c++)
            {
                double half = (bounds[6 + c] - bounds[3 + c]) / 2;
                sum += half * half;
            }
            return Math.Sqrt(sum);
        }

        // Model-space positions of a buffer, 3 doubles per vertex.
        public static double[] Positions(Section section, VertexBuffer buffer)
        {
            double scale = Scale(section.Bounds);
            double[] positions = new double[buffer.Count * 3];
            for (int i = 0; i < buffer.Count; i++)
            {
                int o = i * buffer.Stride;
                double w = S16(buffer.Data, o + 6) / 32767.0;
                for (int c = 0; c < 3; c++)
                {
                    double center = (section.Bounds[3 + c] + section.Bounds[6 + c]) / 2;
                    positions[(i * 3) + c] = section.Bounds[c] + center + ((S16(buffer.Data, o + (c * 2)) / 32767.0) * w * scale);
                }
            }
            return positions;
        }

        private static void Encode(Section section, VertexBuffer buffer, double[] positions)
        {
            double scale = Scale(section.Bounds);
            for (int i = 0; i < buffer.Count; i++)
            {
                double[] q = new double[3];
                double length = 0;
                for (int c = 0; c < 3; c++)
                {
                    double center = (section.Bounds[3 + c] + section.Bounds[6 + c]) / 2;
                    q[c] = (positions[(i * 3) + c] - section.Bounds[c] - center) / scale;
                    length += q[c] * q[c];
                }
                length = Math.Sqrt(length);
                int o = i * buffer.Stride;
                if (length < 1e-12)
                {
                    WriteS16(buffer.Data, o, 32767);
                    WriteS16(buffer.Data, o + 2, 0);
                    WriteS16(buffer.Data, o + 4, 0);
                    WriteS16(buffer.Data, o + 6, 0);
                    continue;
                }
                for (int c = 0; c < 3; c++)
                {
                    WriteS16(buffer.Data, o + (c * 2), Clamp((int) Math.Round((q[c] / length) * 32767)));
                }
                WriteS16(buffer.Data, o + 6, Clamp((int) Math.Round(Math.Min(length, 1.0) * 32767)));
            }
        }

        private static int Clamp(int value)
        {
            return Math.Max(-32767, Math.Min(32767, value));
        }

        // The game's convention (all 24430 original sections): the origin is the center of the vertices'
        // AABB and the bounds are symmetric (min = -max). Sets origin and bounds that way for the given
        // positions and re-encodes every vertex, then updates the centroid.
        private static void Reencode(Section section, double[] lod, double[] lod0)
        {
            double[] min = { double.MaxValue, double.MaxValue, double.MaxValue };
            double[] max = { double.MinValue, double.MinValue, double.MinValue };
            foreach (double[] values in new double[][] { lod, lod0 })
            {
                for (int i = 0; i < values.Length; i++)
                {
                    min[i % 3] = Math.Min(min[i % 3], values[i]);
                    max[i % 3] = Math.Max(max[i % 3], values[i]);
                }
            }
            if (min[0] > max[0])
            {
                return;
            }
            float[] origin;
            float[] half;
            SymmetricBounds(min, max, out origin, out half);
            for (int c = 0; c < 3; c++)
            {
                section.Bounds[c] = origin[c];
                section.Bounds[3 + c] = -half[c];
                section.Bounds[6 + c] = half[c];
            }
            Encode(section, section.Lod, lod);
            Encode(section, section.Lod0, lod0);
            UpdateCentroid(section);
        }

        // Origin = AABB center as float; half extents rounded outwards so the float bounds contain every
        // vertex.
        public static void SymmetricBounds(double[] min, double[] max, out float[] origin, out float[] half)
        {
            origin = new float[3];
            half = new float[3];
            for (int c = 0; c < 3; c++)
            {
                origin[c] = (float) ((min[c] + max[c]) / 2);
                double extent = Math.Max(max[c] - origin[c], origin[c] - min[c]);
                float h = (float) extent;
                if (h < extent)
                {
                    h = (float) (extent + (Math.Abs(extent) * 1e-7) + 1e-9);
                }
                half[c] = h;
            }
        }

        // Unk1c floats 1..3 are the mean vertex position relative to the origin (lod0 buffer when the
        // section has one, else the LOD buffer): verified on all 24430 original sections.
        private static void UpdateCentroid(Section section)
        {
            VertexBuffer buffer = (section.Lod0.Count > 0) ? section.Lod0 : section.Lod;
            if (buffer.Count == 0)
            {
                return;
            }
            double[] positions = Positions(section, buffer);
            for (int c = 0; c < 3; c++)
            {
                double sum = 0;
                for (int i = 0; i < buffer.Count; i++)
                {
                    sum += positions[(i * 3) + c];
                }
                byte[] b = BitConverter.GetBytes((float) ((sum / buffer.Count) - section.Bounds[c]));
                section.Unk1c[4 + (c * 4)] = b[3];
                section.Unk1c[5 + (c * 4)] = b[2];
                section.Unk1c[6 + (c * 4)] = b[1];
                section.Unk1c[7 + (c * 4)] = b[0];
            }
        }

        #endregion

        #region Structural edits

        internal static bool IsRestart(SubSection sub, int index)
        {
            // Restart markers: 0xFFFF (16-bit) and 0x00FFFFFF (32-bit) in every original file.
            return (sub.IndexSize == 2) ? index == 0xFFFF : (index < 0 || index >= 0xFFFFFF);
        }

        private static int Restart(SubSection sub)
        {
            if (sub.IndexSize == 2)
            {
                return 0xFFFF;
            }
            foreach (int index in sub.Indices)
            {
                if (IsRestart(sub, index))
                {
                    return index;
                }
            }
            return 0x00FFFFFF;
        }

        private static VertexBuffer BufferOf(Section section, SubSection sub)
        {
            return (sub.Lod == 0) ? section.Lod0 : section.Lod;
        }

        private static Table DamageOf(Section section, VertexBuffer buffer)
        {
            return (buffer == section.Lod0) ? section.DamageLod0 : section.DamageLod;
        }

        private static float[] UvRange(SubSection sub, VertexBuffer buffer, int channel, List<int> vertices)
        {
            float lo = float.MaxValue;
            float hi = float.MinValue;
            foreach (int v in vertices)
            {
                int raw = (buffer.Data[(v * buffer.Stride) + 8 + (channel * 2)] << 8) | buffer.Data[(v * buffer.Stride) + 9 + (channel * 2)];
                float value = ((raw / 65535f) * sub.Uv[(channel * 2) + 1]) + sub.Uv[channel * 2];
                lo = Math.Min(lo, value);
                hi = Math.Max(hi, value);
            }
            return new float[] { lo, hi };
        }

        private static List<int> UsedVertices(SubSection sub)
        {
            Dictionary<int, bool> seen = new Dictionary<int, bool>();
            List<int> result = new List<int>();
            foreach (int index in sub.Indices)
            {
                if (!IsRestart(sub, index) && !seen.ContainsKey(index))
                {
                    seen[index] = true;
                    result.Add(index);
                }
            }
            return result;
        }

        // Moves every subsection of 'sourceName' into 'targetName' and removes the source section.
        // A source subsection whose name matches a target subsection of the same LOD is merged into it
        // (strips joined with a restart, UV ranges widened to cover both); the others are appended.
        // Damage words for the new vertices come from the source when it is deformable, otherwise from
        // the nearest target vertex. Returns a short report.
        public string MergeSection(string sourceName, string targetName)
        {
            Section source = this.Find(sourceName);
            Section target = this.Find(targetName);
            if (source == null || target == null || source == target)
            {
                throw new ArgumentException("Source and target must be two different existing sections.");
            }
            StringBuilder report = new StringBuilder();
            int sourceIndex = this.Sections.IndexOf(source);
            int targetIndex = this.Sections.IndexOf(target);

            // Positions in model space, before anything moves.
            double[] targetLod = Positions(target, target.Lod);
            double[] targetLod0 = Positions(target, target.Lod0);
            double[] sourceLod = Positions(source, source.Lod);
            double[] sourceLod0 = Positions(source, source.Lod0);
            List<double> newLod = new List<double>(targetLod);
            List<double> newLod0 = new List<double>(targetLod0);
            MemoryStream lodData = new MemoryStream();
            MemoryStream lod0Data = new MemoryStream();
            lodData.Write(target.Lod.Data, 0, target.Lod.Data.Length);
            lod0Data.Write(target.Lod0.Data, 0, target.Lod0.Data.Length);
            MemoryStream lodDamage = new MemoryStream();
            MemoryStream lod0Damage = new MemoryStream();
            lodDamage.Write(target.DamageLod.Data, 0, target.DamageLod.Data.Length);
            lod0Damage.Write(target.DamageLod0.Data, 0, target.DamageLod0.Data.Length);
            int lodCount = target.Lod.Count;
            int lod0Count = target.Lod0.Count;

            // Appends a copy of a source (or target) vertex; uvFrom/uvTo re-encode its UVs between
            // subsection parameter sets. Returns the new index in the target buffer.
            AppendVertex append = delegate(Section owner, VertexBuffer buffer, double[] ownerPositions, int vertex, SubSection uvFrom, float[] uvTo)
            {
                bool isLod0 = buffer == owner.Lod0;
                int stride = buffer.Stride;
                byte[] copy = new byte[stride];
                Buffer.BlockCopy(buffer.Data, vertex * stride, copy, 0, stride);
                if (uvFrom != null)
                {
                    for (int channel = 0; channel < 4; channel++)
                    {
                        float scale = uvFrom.Uv[(channel * 2) + 1];
                        float newScale = uvTo[(channel * 2) + 1];
                        if (newScale == 0)
                        {
                            continue;
                        }
                        int raw = (copy[8 + (channel * 2)] << 8) | copy[9 + (channel * 2)];
                        float value = ((raw / 65535f) * scale) + uvFrom.Uv[channel * 2];
                        int encoded = (int) Math.Round(((value - uvTo[channel * 2]) / newScale) * 65535);
                        encoded = Math.Max(0, Math.Min(65535, encoded));
                        copy[8 + (channel * 2)] = (byte) (encoded >> 8);
                        copy[9 + (channel * 2)] = (byte) encoded;
                    }
                }
                double x = ownerPositions[vertex * 3];
                double y = ownerPositions[(vertex * 3) + 1];
                double z = ownerPositions[(vertex * 3) + 2];
                Table ownerDamage = DamageOf(owner, buffer);
                byte[] word = new byte[4];
                if (ownerDamage.Count == buffer.Count && ownerDamage.Stride == 4)
                {
                    Buffer.BlockCopy(ownerDamage.Data, vertex * 4, word, 0, 4);
                }
                else
                {
                    // Rigid source: borrow the word of the nearest original target vertex.
                    Table targetDamage = isLod0 ? target.DamageLod0 : target.DamageLod;
                    double[] reference = isLod0 ? targetLod0 : targetLod;
                    int best = -1;
                    double bestDistance = double.MaxValue;
                    for (int i = 0; i < reference.Length / 3; i++)
                    {
                        double dx = reference[i * 3] - x;
                        double dy = reference[(i * 3) + 1] - y;
                        double dz = reference[(i * 3) + 2] - z;
                        double distance = (dx * dx) + (dy * dy) + (dz * dz);
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            best = i;
                        }
                    }
                    if (best >= 0 && targetDamage.Stride == 4)
                    {
                        Buffer.BlockCopy(targetDamage.Data, best * 4, word, 0, 4);
                    }
                }
                if (isLod0)
                {
                    lod0Data.Write(copy, 0, stride);
                    lod0Damage.Write(word, 0, 4);
                    newLod0.Add(x);
                    newLod0.Add(y);
                    newLod0.Add(z);
                    return lod0Count++;
                }
                lodData.Write(copy, 0, stride);
                lodDamage.Write(word, 0, 4);
                newLod.Add(x);
                newLod.Add(y);
                newLod.Add(z);
                return lodCount++;
            };

            // Shared copies of source vertices for appended subsections (one copy per vertex and buffer).
            Dictionary<int, int> sharedLod = new Dictionary<int, int>();
            Dictionary<int, int> sharedLod0 = new Dictionary<int, int>();
            Dictionary<int, int> newIndexOfSource = new Dictionary<int, int>();   // source sub -> target sub (-1 = merged)
            int originalTargetSubs = target.SubSections.Count;

            for (int k = 0; k < source.SubSections.Count; k++)
            {
                SubSection sub = source.SubSections[k];
                VertexBuffer buffer = BufferOf(source, sub);
                double[] sourcePositions = (buffer == source.Lod0) ? sourceLod0 : sourceLod;
                SubSection match = null;
                for (int t = 0; t < originalTargetSubs; t++)
                {
                    SubSection candidate = target.SubSections[t];
                    if (candidate.Name == sub.Name && candidate.Lod == sub.Lod)
                    {
                        match = candidate;
                        break;
                    }
                }

                if (match == null)
                {
                    Dictionary<int, int> shared = (buffer == source.Lod0) ? sharedLod0 : sharedLod;
                    SubSection moved = sub.Copy();
                    moved.IndexSize = (sub.Lod == 0) ? IndexSizeOf(target, true, sub.IndexSize) : IndexSizeOf(target, false, sub.IndexSize);
                    int restart = Restart(moved);
                    moved.Indices.Clear();
                    foreach (int index in sub.Indices)
                    {
                        if (IsRestart(sub, index))
                        {
                            moved.Indices.Add(restart);
                            continue;
                        }
                        int mapped;
                        if (!shared.TryGetValue(index, out mapped))
                        {
                            mapped = append(source, buffer, sourcePositions, index, null, null);
                            shared[index] = mapped;
                        }
                        moved.Indices.Add(mapped);
                    }
                    target.SubSections.Add(moved);
                    newIndexOfSource[k] = target.SubSections.Count - 1;
                    report.AppendFormat("moved {0} (LOD {1}): {2} indices\r\n", sub.Name, sub.Lod, sub.Indices.Count);
                    continue;
                }

                // Merge: one UV parameter set must cover both parts. Widen it if needed, then give the
                // merged subsection its own vertex copies so other subsections keep their UVs.
                VertexBuffer targetBuffer = BufferOf(target, match);
                double[] targetPositions = (targetBuffer == target.Lod0) ? targetLod0 : targetLod;
                List<int> targetUsed = UsedVertices(match);
                List<int> sourceUsed = UsedVertices(sub);
                float[] uv = (float[]) match.Uv.Clone();
                bool widened = false;
                for (int channel = 0; channel < 4; channel++)
                {
                    if (match.Uv[(channel * 2) + 1] == 0 && sub.Uv[(channel * 2) + 1] == 0)
                    {
                        continue;
                    }
                    float[] a = UvRange(match, targetBuffer, channel, targetUsed);
                    float[] b = UvRange(sub, buffer, channel, sourceUsed);
                    float lo = Math.Min(a[0], b[0]);
                    float hi = Math.Max(a[1], b[1]);
                    float offset = match.Uv[channel * 2];
                    float scale = match.Uv[(channel * 2) + 1];
                    if (lo < offset || hi > offset + scale)
                    {
                        uv[channel * 2] = Math.Min(lo, offset);
                        uv[(channel * 2) + 1] = Math.Max(hi, offset + scale) - uv[channel * 2];
                        widened = true;
                    }
                }
                List<int> merged = new List<int>();
                int mergedRestart = Restart(match);
                Dictionary<int, int> ownTarget = new Dictionary<int, int>();
                foreach (int index in match.Indices)
                {
                    if (IsRestart(match, index))
                    {
                        merged.Add(mergedRestart);
                        continue;
                    }
                    if (!widened)
                    {
                        merged.Add(index);
                        continue;
                    }
                    int mapped;
                    if (!ownTarget.TryGetValue(index, out mapped))
                    {
                        mapped = append(target, targetBuffer, targetPositions, index, match, uv);
                        ownTarget[index] = mapped;
                    }
                    merged.Add(mapped);
                }
                merged.Add(mergedRestart);
                Dictionary<int, int> ownSource = new Dictionary<int, int>();
                foreach (int index in sub.Indices)
                {
                    if (IsRestart(sub, index))
                    {
                        merged.Add(mergedRestart);
                        continue;
                    }
                    int mapped;
                    if (!ownSource.TryGetValue(index, out mapped))
                    {
                        mapped = append(source, buffer, sourcePositions, index, sub, uv);
                        ownSource[index] = mapped;
                    }
                    merged.Add(mapped);
                }
                match.Indices = merged;
                match.Uv = uv;
                newIndexOfSource[k] = -1;
                report.AppendFormat("merged {0} (LOD {1}) into {2}.{0}: {3} + {4} indices{5}\r\n", sub.Name, sub.Lod, target.Name, match.Indices.Count - sub.Indices.Count - 1, sub.Indices.Count, widened ? ", UV range widened" : "");
            }

            // New buffers and damage tables.
            target.Lod.Data = lodData.ToArray();
            target.Lod.Count = lodCount;
            target.Lod0.Data = lod0Data.ToArray();
            target.Lod0.Count = lod0Count;
            if (target.Lod.Count > 0 && target.Lod.Stride == 0)
            {
                target.Lod.Stride = source.Lod.Stride;
            }
            if (target.Lod0.Count > 0 && target.Lod0.Stride == 0)
            {
                target.Lod0.Stride = source.Lod0.Stride;
            }
            // A deformable section carries one damage word per vertex of each non-empty buffer.
            if (target.IsDeformable)
            {
                SetDamage(target.DamageLod, lodCount, lodDamage);
                SetDamage(target.DamageLod0, lod0Count, lod0Damage);
            }
            CheckIndexRange(target);
            Reencode(target, newLod.ToArray(), newLod0.ToArray());

            // Draw lists: the source's entries become the target's new subsections (merged ones drop
            // out), and sections after the removed one shift down by one.
            int dropped = 0;
            for (int l = 0; l < this.DrawLists.Count; l++)
            {
                List<uint> list = new List<uint>();
                foreach (uint entry in this.DrawLists[l])
                {
                    int s = (int) (entry >> 16);
                    int k = (int) (entry & 0xFFFF);
                    if (s == sourceIndex)
                    {
                        int mapped;
                        if (!newIndexOfSource.TryGetValue(k, out mapped) || mapped < 0)
                        {
                            dropped++;
                            continue;
                        }
                        s = targetIndex;
                        k = mapped;
                    }
                    if (s > sourceIndex)
                    {
                        s--;
                    }
                    list.Add((uint) ((s << 16) | k));
                }
                this.DrawLists[l] = list.ToArray();
            }
            this.Sections.Remove(source);
            this.CheckDrawLists();
            report.AppendFormat("{0} removed; {1}: LOD vertices {2}, LOD0 vertices {3}; draw list entries dropped: {4}\r\n", source.Name, target.Name, target.Lod.Count, target.Lod0.Count, dropped);
            return report.ToString();
        }

        // Removes every subsection named 'subName' (all LODs) from a section, drops the vertices nobody
        // uses any more and refits the bounds. A section left without subsections is removed.
        // Returns the number of subsections removed.
        public int RemoveSubSections(string sectionName, string subName)
        {
            Section section = this.Find(sectionName);
            if (section == null)
            {
                return 0;
            }
            List<int> removed = new List<int>();
            for (int k = 0; k < section.SubSections.Count; k++)
            {
                if (section.SubSections[k].Name == subName)
                {
                    removed.Add(k);
                }
            }
            return this.RemoveSubSectionsAt(this.Sections.IndexOf(section), removed);
        }

        // Moves one mesh (subsection 'subIndex' of section 'sectionIndex') into the part 'targetName',
        // which is created when it does not exist (with the source part's settings: damage, flags).
        // The mesh takes only its own vertices; the source part is compacted, and removed if it was
        // its last mesh. Into an existing part a mesh of the same material and LOD is joined to it
        // (MergeSection), as FM4 keeps one subsection per material and LOD. Returns a short report.
        public string MoveSubSection(int sectionIndex, int subIndex, string targetName)
        {
            if (sectionIndex < 0 || sectionIndex >= this.Sections.Count || subIndex < 0 || subIndex >= this.Sections[sectionIndex].SubSections.Count)
            {
                throw new ArgumentException("The mesh to move is not in the model.");
            }
            Section source = this.Sections[sectionIndex];
            Section target = this.Find(targetName);
            if (target == source)
            {
                throw new ArgumentException("The mesh is already in " + targetName + ".");
            }
            SubSection moving = source.SubSections[subIndex];
            string copyName = targetName;
            if (target != null)
            {
                copyName = "__moving";
                while (this.Find(copyName) != null)
                {
                    copyName += "_";
                }
            }
            this.DuplicateSection(source.Name, copyName);
            List<int> others = new List<int>();
            for (int k = 0; k < source.SubSections.Count; k++)
            {
                if (k != subIndex)
                {
                    others.Add(k);
                }
            }
            this.RemoveSubSectionsAt(this.Sections.IndexOf(this.Find(copyName)), others);
            this.RemoveSubSectionsAt(sectionIndex, new List<int> { subIndex });
            string report = string.Format("{0} (LOD {1}) moved from {2} to {3}", moving.Name, moving.Lod, source.Name, targetName);
            if (target != null)
            {
                this.MergeSection(copyName, targetName);
            }
            else
            {
                report += " (new part)";
            }
            if (!this.Sections.Contains(source))
            {
                report += "; " + source.Name + " had no other mesh and was removed";
            }
            return report;
        }

        // Splits a part triangle by triangle, every LOD: 'target' names the part each triangle belongs to
        // from its centroid (model space). Each named part gets a copy of the section holding only its
        // triangles (each written as a 3-index strip ended by a restart, so the winding stays), joined
        // into that part when it exists (MergeSection: one subsection per material and LOD), the part
        // itself when the name is its own. The original goes. Returns one line per resulting part, or ""
        // when every triangle already belongs to the part.
        public delegate string PartOfPoint(double x, double y, double z);

        public string SplitSection(string name, PartOfPoint target)
        {
            Section section = this.Find(name);
            if (section == null)
            {
                return "";
            }
            double[] lodPositions = Positions(section, section.Lod);
            double[] lod0Positions = Positions(section, section.Lod0);
            // Per subsection, per triangle: the target name.
            List<string> names = new List<string>();
            List<List<int[]>> triangles = new List<List<int[]>>();
            List<List<string>> owners = new List<List<string>>();
            foreach (SubSection sub in section.SubSections)
            {
                double[] p = (sub.Lod == 0) ? lod0Positions : lodPositions;
                List<int[]> faces = StripTriangles(sub);
                List<string> owner = new List<string>();
                foreach (int[] f in faces)
                {
                    double x = (p[f[0] * 3] + p[f[1] * 3] + p[f[2] * 3]) / 3;
                    double y = (p[(f[0] * 3) + 1] + p[(f[1] * 3) + 1] + p[(f[2] * 3) + 1]) / 3;
                    double z = (p[(f[0] * 3) + 2] + p[(f[1] * 3) + 2] + p[(f[2] * 3) + 2]) / 3;
                    string t = target(x, y, z) ?? name;
                    owner.Add(t);
                    if (!names.Contains(t))
                    {
                        names.Add(t);
                    }
                }
                triangles.Add(faces);
                owners.Add(owner);
            }
            if (names.Count == 0 || (names.Count == 1 && string.Equals(names[0], name, StringComparison.OrdinalIgnoreCase)))
            {
                return "";
            }

            StringBuilder report = new StringBuilder();
            List<string> copies = new List<string>();
            Dictionary<string, string> targetOf = new Dictionary<string, string>();
            foreach (string t in names)
            {
                string copy = "__split_" + t;
                while (this.Find(copy) != null)
                {
                    copy += "_";
                }
                this.DuplicateSection(name, copy);
                Section part = this.Find(copy);
                List<int> empty = new List<int>();
                int count = 0;
                for (int k = 0; k < part.SubSections.Count; k++)
                {
                    SubSection sub = part.SubSections[k];
                    int restart = (sub.IndexSize == 2) ? 0xFFFF : 0x00FFFFFF;
                    List<int> indices = new List<int>();
                    for (int i = 0; i < triangles[k].Count; i++)
                    {
                        if (owners[k][i] != t)
                        {
                            continue;
                        }
                        if (indices.Count > 0)
                        {
                            indices.Add(restart);
                        }
                        indices.AddRange(triangles[k][i]);
                        count++;
                    }
                    sub.Indices = indices;
                    if (indices.Count == 0)
                    {
                        empty.Add(k);
                    }
                }
                int index = this.Sections.IndexOf(part);
                if (empty.Count > 0)
                {
                    this.RemoveSubSectionsAt(index, empty);
                }
                else
                {
                    double[] lod = Compact(part, part.Lod, part.DamageLod, false, Positions(part, part.Lod));
                    double[] lod0 = Compact(part, part.Lod0, part.DamageLod0, true, Positions(part, part.Lod0));
                    Reencode(part, lod, lod0);
                    CheckIndexRange(part);
                }
                if (this.Find(copy) != null)
                {
                    copies.Add(copy);
                    targetOf[copy] = t;
                    report.AppendFormat("{0} -> {1}: {2} triangles\r\n", name, t, count);
                }
            }
            this.RemoveSection(name);
            foreach (string copy in copies)
            {
                string t = targetOf[copy];
                if (this.Find(t) == null)
                {
                    this.Find(copy).Name = t;
                }
                else
                {
                    this.MergeSection(copy, t);
                }
            }
            this.CheckDrawLists();
            return report.ToString();
        }

        // The box (min x, y, z, max x, y, z; model space) of the vertices a subsection draws.
        public double[] SubSectionBox(Section section, SubSection sub)
        {
            VertexBuffer buffer = (sub.Lod == 0) ? section.Lod0 : section.Lod;
            double[] p = Positions(section, buffer);
            double[] box = { double.MaxValue, double.MaxValue, double.MaxValue, double.MinValue, double.MinValue, double.MinValue };
            foreach (int index in sub.Indices)
            {
                if (index < 0 || index >= buffer.Count || IsRestart(sub, index))
                {
                    continue;
                }
                for (int c = 0; c < 3; c++)
                {
                    box[c] = Math.Min(box[c], p[(index * 3) + c]);
                    box[3 + c] = Math.Max(box[3 + c], p[(index * 3) + c]);
                }
            }
            return box;
        }

        // Removes the subsections at these indices of a section (see RemoveSubSectionsAt).
        public int RemoveSubSections(string sectionName, List<int> indices)
        {
            Section section = this.Find(sectionName);
            return (section == null) ? 0 : this.RemoveSubSectionsAt(this.Sections.IndexOf(section), new List<int>(indices));
        }

        // Removes the given subsections of a section, drops the vertices nobody uses any more and refits
        // the bounds. A section left without subsections is removed. Returns the number removed.
        private int RemoveSubSectionsAt(int sectionIndex, List<int> removed)
        {
            Section section = this.Sections[sectionIndex];
            if (removed.Count == 0)
            {
                return 0;
            }
            if (removed.Count == section.SubSections.Count)
            {
                this.RemoveSection(section.Name);
                return removed.Count;
            }
            removed.Sort();
            double[] lod = Positions(section, section.Lod);
            double[] lod0 = Positions(section, section.Lod0);
            for (int i = removed.Count - 1; i >= 0; i--)
            {
                section.SubSections.RemoveAt(removed[i]);
            }

            // Draw lists: drop the removed subsections, renumber the later ones of this section.
            for (int l = 0; l < this.DrawLists.Count; l++)
            {
                List<uint> list = new List<uint>();
                foreach (uint entry in this.DrawLists[l])
                {
                    int s = (int) (entry >> 16);
                    int k = (int) (entry & 0xFFFF);
                    if (s == sectionIndex)
                    {
                        if (removed.Contains(k))
                        {
                            continue;
                        }
                        int shift = 0;
                        foreach (int gone in removed)
                        {
                            if (gone < k)
                            {
                                shift++;
                            }
                        }
                        k -= shift;
                    }
                    list.Add((uint) ((s << 16) | k));
                }
                this.DrawLists[l] = list.ToArray();
            }

            lod = Compact(section, section.Lod, section.DamageLod, false, lod);
            lod0 = Compact(section, section.Lod0, section.DamageLod0, true, lod0);
            Reencode(section, lod, lod0);
            CheckIndexRange(section);
            this.CheckDrawLists();
            return removed.Count;
        }

        // A close-up model file (_lod0 or _cockpit kind) made from this main model's LOD 1, for source cars
        // whose own close-up file is a placeholder (FH traffic models: 10-17 parts of 17 vertices) or
        // missing. FM4 layout (BMW_X5M_11, FER_F40_87): one LOD, 0, whose two draw lists match the main
        // file's LOD 1 lists (list 0 and list N of [pass A LOD 1..N, pass B LOD 1..N]); vertices in the
        // Lod0 buffer with their damage words in DamageLod0, the Lod buffer empty; 32-bit indices; the
        // section fields as in the main file but the geometry ones (bounds, centroid). The header differs
        // between main and close-up files (record count), so it comes from a file of that kind ('head').
        // Only the sections 'keep' accepts and that have LOD 1 subsections go in.
        public CarbinModel CloseUpFromLod1(byte[] head, Predicate<string> keep)
        {
            CarbinModel model = new CarbinModel(this.ToBytes());
            int lods = model.DrawLists.Count / 2;
            if (lods < 1)
            {
                throw new InvalidDataException("The model has no draw lists.");
            }
            foreach (Section section in new List<Section>(model.Sections))
            {
                bool lod1 = false;
                foreach (SubSection sub in section.SubSections)
                {
                    lod1 |= sub.Lod == 1;
                }
                if (!lod1 || !keep(section.Name))
                {
                    model.RemoveSection(section.Name);
                }
            }
            if (model.Sections.Count == 0)
            {
                throw new InvalidDataException("No part to put in the close-up model.");
            }

            Dictionary<uint, uint> renumber = new Dictionary<uint, uint>();
            for (int s = 0; s < model.Sections.Count; s++)
            {
                Section section = model.Sections[s];
                List<SubSection> kept = new List<SubSection>();
                for (int k = 0; k < section.SubSections.Count; k++)
                {
                    SubSection sub = section.SubSections[k];
                    if (sub.Lod != 1)
                    {
                        continue;
                    }
                    renumber[(uint) ((s << 16) | k)] = (uint) ((s << 16) | kept.Count);
                    sub.Lod = 0;
                    ConvertIndices(sub, 4);
                    kept.Add(sub);
                }
                section.SubSections = kept;
                VertexBuffer buffer = section.Lod0;
                section.Lod0 = section.Lod;
                section.Lod = buffer;
                Table damage = section.DamageLod0;
                section.DamageLod0 = section.DamageLod;
                section.DamageLod = damage;
            }
            List<uint[]> lists = new List<uint[]>();
            foreach (uint[] old in new uint[][] { model.DrawLists[0], model.DrawLists[lods] })
            {
                List<uint> list = new List<uint>();
                foreach (uint entry in old)
                {
                    uint moved;
                    if (renumber.TryGetValue(entry, out moved))
                    {
                        list.Add(moved);
                    }
                }
                lists.Add(list.ToArray());
            }
            model.DrawLists = lists;
            model.Head = (byte[]) head.Clone();

            // Vertices only the LODs 2-5 used go; bounds and centroid follow the kept geometry.
            foreach (Section section in model.Sections)
            {
                double[] lod0 = Compact(section, section.Lod0, section.DamageLod0, true, Positions(section, section.Lod0));
                Reencode(section, new double[0], lod0);
                CheckIndexRange(section);
            }
            model.CheckDrawLists();
            return model;
        }

        // Draw lists of a model holding every LOD in one file: list n of each pass draws LOD n, 6 per pass
        // (LOD 0-5) on all 420 FM4 car wheels. Four FH wheels (FOR_F350SuperDuty_08, NIS_Leaf_11,
        // TOY_RAV4Sport_04, VOL_XC70_12, the same in FH1 and FH2) have LOD 1-4 only, 4 lists per pass
        // starting with LOD 1: padded with empty lists to that layout.
        private const int LodsPerPass = 6;

        private void PadLodDrawLists()
        {
            int perPass = this.DrawLists.Count / 2;
            int first = int.MaxValue;
            foreach (Section section in this.Sections)
            {
                foreach (SubSection sub in section.SubSections)
                {
                    first = Math.Min(first, (int) sub.Lod);
                }
            }
            if (perPass < 1 || this.DrawLists.Count % 2 != 0 || first == int.MaxValue || first + perPass > LodsPerPass || (first == 0 && perPass == LodsPerPass))
            {
                return;
            }
            List<uint[]> lists = new List<uint[]>();
            for (int pass = 0; pass < 2; pass++)
            {
                for (int lod = 0; lod < LodsPerPass; lod++)
                {
                    int l = lod - first;
                    lists.Add((l >= 0 && l < perPass) ? this.DrawLists[(pass * perPass) + l] : new uint[0]);
                }
            }
            this.DrawLists = lists;
        }

        // For a model holding every LOD in one file (wheels, type 3): LOD 0 rebuilt from LOD 1, for FH traffic
        // wheels whose LOD 0 is a 3-vertex placeholder. The Lod0 buffer becomes a copy of the Lod buffer
        // with its damage words, the LOD 0 subsections copies of the LOD 1 ones that 'keep' accepts (by
        // material; FM4 wheels draw no tire in LOD 0, 441 of 442) and the LOD 0 draw lists (list 0 of
        // each pass) the LOD 1 lists' entries moved to the copies. False when there is no LOD 1.
        public bool Lod0FromLod1(Predicate<string> keep)
        {
            this.PadLodDrawLists();
            int perPass = this.DrawLists.Count / 2;
            if (perPass < 2)
            {
                return false;
            }
            Dictionary<uint, uint> renumber = new Dictionary<uint, uint>();
            Dictionary<uint, uint> copies = new Dictionary<uint, uint>();
            List<int> untouched = new List<int>();
            bool any = false;
            for (int s = 0; s < this.Sections.Count; s++)
            {
                Section section = this.Sections[s];
                List<SubSection> subs = new List<SubSection>();
                List<int> lod1 = new List<int>();
                for (int k = 0; k < section.SubSections.Count; k++)
                {
                    SubSection sub = section.SubSections[k];
                    if (sub.Lod == 0)
                    {
                        continue;
                    }
                    renumber[(uint) ((s << 16) | k)] = (uint) ((s << 16) | subs.Count);
                    subs.Add(sub);
                    if (sub.Lod == 1 && keep(sub.Name))
                    {
                        lod1.Add(k);
                    }
                }
                if (lod1.Count == 0)
                {
                    // Left as it is, its own LOD 0 included.
                    for (int k = 0; k < section.SubSections.Count; k++)
                    {
                        renumber[(uint) ((s << 16) | k)] = (uint) ((s << 16) | k);
                    }
                    untouched.Add(s);
                    continue;
                }
                foreach (int k in lod1)
                {
                    SubSection copy = section.SubSections[k].Copy();
                    copy.Lod = 0;
                    ConvertIndices(copy, 4);
                    copies[(uint) ((s << 16) | k)] = (uint) ((s << 16) | subs.Count);
                    subs.Add(copy);
                }
                section.SubSections = subs;
                section.Lod0 = new VertexBuffer { Count = section.Lod.Count, Stride = section.Lod.Stride, Extra = (byte[]) section.Lod.Extra.Clone(), Data = (byte[]) section.Lod.Data.Clone() };
                section.DamageLod0 = new Table { Count = section.DamageLod.Count, Stride = section.DamageLod.Stride, Extra = (byte[]) section.DamageLod.Extra.Clone(), Data = (byte[]) section.DamageLod.Data.Clone() };
                any = true;
            }
            if (!any)
            {
                return false;
            }
            for (int l = 0; l < this.DrawLists.Count; l++)
            {
                List<uint> list = new List<uint>();
                if (l % perPass == 0)
                {
                    // The kept LOD 0 of untouched sections, then the copies in LOD 1 order.
                    foreach (uint entry in this.DrawLists[l])
                    {
                        if (untouched.Contains((int) (entry >> 16)))
                        {
                            list.Add(entry);
                        }
                    }
                    foreach (uint entry in this.DrawLists[l + 1])
                    {
                        uint copy;
                        if (copies.TryGetValue(entry, out copy))
                        {
                            list.Add(copy);
                        }
                    }
                }
                else
                {
                    foreach (uint entry in this.DrawLists[l])
                    {
                        list.Add(renumber[entry]);
                    }
                }
                this.DrawLists[l] = list.ToArray();
            }
            for (int s = 0; s < this.Sections.Count; s++)
            {
                if (untouched.Contains(s))
                {
                    continue;
                }
                Section section = this.Sections[s];
                double[] lod0 = Compact(section, section.Lod0, section.DamageLod0, true, Positions(section, section.Lod0));
                Reencode(section, Positions(section, section.Lod), lod0);
                CheckIndexRange(section);
            }
            this.CheckDrawLists();
            this.SetWheelLodRange();
            return true;
        }

        // Wheel head (type 3) 0x15C / 0x160: first and last LOD of the file, BE words (0 and 5 on all 420
        // FM4 car wheels, 0/1 on the 4 SLOD wheels, 0/0 on the 18 tyres). A wheel whose source starts at
        // LOD 1 (FH RAV4, XC70, F350) keeps 1 there after LOD 0 is rebuilt, and the game draws no rim
        // where it uses LOD 0 (showroom): set from the subsections.
        private void SetWheelLodRange()
        {
            if (this.Type != 3 || this.Head.Length < 0x164)
            {
                return;
            }
            uint first = uint.MaxValue;
            uint last = 0;
            foreach (Section section in this.Sections)
            {
                foreach (SubSection sub in section.SubSections)
                {
                    first = Math.Min(first, sub.Lod);
                    last = Math.Max(last, sub.Lod);
                }
            }
            if (first == uint.MaxValue)
            {
                return;
            }
            for (int k = 0; k < 4; k++)
            {
                this.Head[0x15C + k] = (byte) (first >> (24 - (k * 8)));
                this.Head[0x160 + k] = (byte) (last >> (24 - (k * 8)));
            }
        }

        // Removes a whole section; later sections shift down by one in the draw lists.
        public void RemoveSection(string name)
        {
            Section section = this.Find(name);
            if (section == null)
            {
                return;
            }
            int index = this.Sections.IndexOf(section);
            for (int l = 0; l < this.DrawLists.Count; l++)
            {
                List<uint> list = new List<uint>();
                foreach (uint entry in this.DrawLists[l])
                {
                    int s = (int) (entry >> 16);
                    if (s == index)
                    {
                        continue;
                    }
                    if (s > index)
                    {
                        s--;
                    }
                    list.Add((uint) ((s << 16) | (entry & 0xFFFF)));
                }
                this.DrawLists[l] = list.ToArray();
            }
            this.Sections.RemoveAt(index);
            this.CheckDrawLists();
        }

        // Keeps only the vertices of 'buffer' that a subsection still uses (with their damage words)
        // and remaps the indices. Returns the kept vertices' positions.
        private static double[] Compact(Section section, VertexBuffer buffer, Table damage, bool lod0, double[] positions)
        {
            if (buffer.Count == 0)
            {
                return positions;
            }
            bool[] used = new bool[buffer.Count];
            foreach (SubSection sub in section.SubSections)
            {
                if ((sub.Lod == 0) != lod0)
                {
                    continue;
                }
                foreach (int index in sub.Indices)
                {
                    if (!IsRestart(sub, index))
                    {
                        used[index] = true;
                    }
                }
            }
            int[] map = new int[buffer.Count];
            int count = 0;
            for (int i = 0; i < buffer.Count; i++)
            {
                map[i] = used[i] ? count++ : -1;
            }
            if (count == buffer.Count)
            {
                return positions;
            }
            // Damage entries: 4 bytes per vertex on car models, 16 on wheels.
            bool hasDamage = damage.Count == buffer.Count && damage.Stride > 0;
            byte[] data = new byte[count * buffer.Stride];
            byte[] words = new byte[count * damage.Stride];
            double[] kept = new double[count * 3];
            for (int i = 0; i < buffer.Count; i++)
            {
                if (map[i] < 0)
                {
                    continue;
                }
                Buffer.BlockCopy(buffer.Data, i * buffer.Stride, data, map[i] * buffer.Stride, buffer.Stride);
                if (hasDamage)
                {
                    Buffer.BlockCopy(damage.Data, i * damage.Stride, words, map[i] * damage.Stride, damage.Stride);
                }
                kept[map[i] * 3] = positions[i * 3];
                kept[(map[i] * 3) + 1] = positions[(i * 3) + 1];
                kept[(map[i] * 3) + 2] = positions[(i * 3) + 2];
            }
            foreach (SubSection sub in section.SubSections)
            {
                if ((sub.Lod == 0) != lod0)
                {
                    continue;
                }
                for (int k = 0; k < sub.Indices.Count; k++)
                {
                    if (!IsRestart(sub, sub.Indices[k]))
                    {
                        sub.Indices[k] = map[sub.Indices[k]];
                    }
                }
            }
            buffer.Data = data;
            buffer.Count = count;
            if (count == 0)
            {
                buffer.Stride = 0;
            }
            if (hasDamage)
            {
                damage.Count = count;
                damage.Data = words;
                if (count == 0)
                {
                    damage.Stride = 0;
                }
            }
            return kept;
        }

        #region OBJ geometry

        // Triangles of a strip subsection, front faces with normal = (b - a) x (c - a) (the winding that
        // matches the stored normals on every original part); degenerate ones skipped.
        public static List<int[]> StripTriangles(SubSection sub)
        {
            List<int[]> faces = new List<int[]>();
            List<int> strip = new List<int>();
            List<int> all = new List<int>(sub.Indices);
            all.Add(-1);
            foreach (int index in all)
            {
                if (index >= 0 && !IsRestart(sub, index))
                {
                    strip.Add(index);
                    continue;
                }
                for (int i = 0; i + 2 < strip.Count; i++)
                {
                    int a = strip[i];
                    int b = strip[i + 1];
                    int c = strip[i + 2];
                    if (a == b || b == c || a == c)
                    {
                        continue;
                    }
                    faces.Add((i % 2 == 0) ? new int[] { a, b, c } : new int[] { b, a, c });
                }
                strip.Clear();
            }
            return faces;
        }

        public static Microsoft.Xna.Framework.Vector2 VertexUv(VertexBuffer buffer, int v, SubSection sub, int set)
        {
            int o = (v * buffer.Stride) + 8 + (set * 4);
            float u = ((((buffer.Data[o] << 8) | buffer.Data[o + 1]) / 65535f) * sub.Uv[(set * 4) + 1]) + sub.Uv[set * 4];
            float w = ((((buffer.Data[o + 2] << 8) | buffer.Data[o + 3]) / 65535f) * sub.Uv[(set * 4) + 3]) + sub.Uv[(set * 4) + 2];
            return new Microsoft.Xna.Framework.Vector2(u, w);
        }

        private static Microsoft.Xna.Framework.Quaternion Frame(VertexBuffer buffer, int v)
        {
            int o = (v * buffer.Stride) + 16;
            Microsoft.Xna.Framework.Quaternion q = new Microsoft.Xna.Framework.Quaternion(S16(buffer.Data, o) / 32767f, S16(buffer.Data, o + 2) / 32767f, S16(buffer.Data, o + 4) / 32767f, S16(buffer.Data, o + 6) / 32767f);
            if (q.LengthSquared() > 1e-8f)
            {
                q.Normalize();
            }
            return q;
        }

        public static Microsoft.Xna.Framework.Vector3 VertexNormal(VertexBuffer buffer, int v)
        {
            return Microsoft.Xna.Framework.Vector3.Transform(Microsoft.Xna.Framework.Vector3.UnitX, Frame(buffer, v));
        }

        // Replaces the geometry of a part with an OBJ mesh, in every LOD the part has in this file (the
        // main file's LODs share one vertex buffer). 'materialMap' maps OBJ material -> car material.
        // fitUv: OBJ UVs are normalized per material and placed in this car's region of the material
        // (part from another car); otherwise they are used as they are (same car). Per vertex, the
        // unknown trailing bytes, UV2 and the damage word come from the nearest original vertex of the
        // part. Returns a report.
        public string ReplaceGeometry(string sectionName, ObjMesh mesh, Dictionary<string, string> materialMap, bool fitUv)
        {
            Section section = this.Find(sectionName);
            if (section == null || section.SubSections.Count == 0)
            {
                return null;
            }
            Dictionary<uint, ObjMesh> meshes = new Dictionary<uint, ObjMesh>();
            foreach (SubSection sub in section.SubSections)
            {
                meshes[sub.Lod] = mesh;
            }
            return this.RebuildLods(sectionName, meshes, materialMap, fitUv);
        }

        // Working state of a part rebuild: the part's original vertices (reference for tangent frames,
        // trailing bytes, UV2 and damage words) and the new vertex buffer being filled.
        private class Rebuild
        {
            public double[] Reference;
            public byte[] ReferenceData;
            public byte[] ReferenceDamage;
            public int ReferenceCount;
            public float[] ReferenceUv;
            public float[] ReferenceUv2;        // absolute UV2 of the original vertices, NaN when unused
            public int Stride;                  // 32 (FM4) or 28 (FH)
            public PointTree Tree;
            public VertexBuffer Target;
            public int DamageStride;            // per-vertex table entry: 4 (car damage words), 16 (wheels), 0 = no table
            public bool Lod0;
            public SubSection Template;
            public int Restart;
            public List<byte[]> Vertices = new List<byte[]>();
            public List<double> Positions = new List<double>();
            public List<byte[]> Words = new List<byte[]>();
            public StringBuilder Report = new StringBuilder();
        }

        // Rebuilds a part LOD by LOD. meshes[lod] is the new geometry of that LOD (the same ObjMesh
        // object for several LODs shares its vertices between them), null keeps that LOD as it is, and
        // LODs of the part missing from the dictionary are dropped. A LOD the part did not have is added
        // to the draw list that draws its materials at that LOD. OBJ material names are car materials
        // unless 'materialMap' (OBJ -> car, null = same name) says otherwise. Returns a report.
        public string RebuildLods(string sectionName, Dictionary<uint, ObjMesh> meshes, Dictionary<string, string> materialMap, bool fitUv)
        {
            Section section = this.Find(sectionName);
            if (section == null)
            {
                throw new ArgumentException("No part " + sectionName + ".");
            }
            if (meshes.Count == 0)
            {
                throw new ArgumentException(sectionName + ": no LOD to build.");
            }
            // LOD 0 lives in the Lod0 buffer, LODs 1+ in the Lod buffer: cars keep them in different
            // files, wheels in the same one. One pass per buffer; the other buffer's subsections stay.
            Dictionary<uint, ObjMesh> lod0Meshes = new Dictionary<uint, ObjMesh>();
            Dictionary<uint, ObjMesh> lodMeshes = new Dictionary<uint, ObjMesh>();
            foreach (KeyValuePair<uint, ObjMesh> pair in meshes)
            {
                ((pair.Key == 0) ? lod0Meshes : lodMeshes)[pair.Key] = pair.Value;
            }
            string report = "";
            if (lod0Meshes.Count > 0)
            {
                report += this.RebuildBuffer(section, lod0Meshes, true, materialMap, fitUv);
            }
            if (lodMeshes.Count > 0)
            {
                report += ((report.Length > 0) ? "\r\n" : "") + this.RebuildBuffer(section, lodMeshes, false, materialMap, fitUv);
            }
            return report;
        }

        // RebuildLods on one vertex buffer (Lod0 for LOD 0, Lod for LODs 1+).
        private string RebuildBuffer(Section section, Dictionary<uint, ObjMesh> meshes, bool useLod0, Dictionary<string, string> materialMap, bool fitUv)
        {
            string sectionName = section.Name;
            int sectionIndex = this.Sections.IndexOf(section);
            List<uint> lods = new List<uint>(meshes.Keys);
            lods.Sort();

            Rebuild work = new Rebuild();
            work.Lod0 = useLod0;
            work.Target = useLod0 ? section.Lod0 : section.Lod;
            work.Stride = this.VertexStride;
            if (work.Target.Count > 0 && work.Target.Stride != work.Stride)
            {
                throw new NotSupportedException(string.Format("{0}: {1}-byte vertices, expected {2}.", sectionName, work.Target.Stride, work.Stride));
            }
            Table damage = useLod0 ? section.DamageLod0 : section.DamageLod;
            // The per-vertex table follows the vertices: damage words on deformable car parts, 16 bytes
            // per vertex on wheels (left stale, it no longer matched the rebuilt vertices).
            work.DamageStride = (damage.Count > 0 && damage.Count == work.Target.Count && damage.Stride > 0) ? damage.Stride : 0;
            work.Reference = Positions(section, work.Target);
            work.ReferenceData = work.Target.Data;
            work.ReferenceDamage = damage.Data;
            work.ReferenceCount = work.Target.Count;
            work.Tree = new PointTree(work.Reference, work.ReferenceCount);
            // Absolute UVs of the original vertices (each subsection has its own UV range), NaN when unused.
            work.ReferenceUv = new float[work.ReferenceCount * 2];
            work.ReferenceUv2 = new float[work.ReferenceCount * 2];
            for (int i = 0; i < work.ReferenceUv.Length; i++)
            {
                work.ReferenceUv[i] = float.NaN;
                work.ReferenceUv2[i] = float.NaN;
            }
            foreach (SubSection sub in section.SubSections)
            {
                if ((sub.Lod == 0) != useLod0)
                {
                    continue;
                }
                foreach (int index in sub.Indices)
                {
                    if (!IsRestart(sub, index) && index < work.ReferenceCount)
                    {
                        Microsoft.Xna.Framework.Vector2 uv = VertexUv(work.Target, index, sub, 0);
                        work.ReferenceUv[index * 2] = uv.X;
                        work.ReferenceUv[(index * 2) + 1] = uv.Y;
                        Microsoft.Xna.Framework.Vector2 uv2 = VertexUv(work.Target, index, sub, 1);
                        work.ReferenceUv2[index * 2] = uv2.X;
                        work.ReferenceUv2[(index * 2) + 1] = uv2.Y;
                    }
                }
            }

            // Which draw list each (material, LOD) goes to: where the part had it, else where the model
            // draws that material at that LOD, else the busiest list of the LOD. Kept subsections stay
            // in their own list.
            Dictionary<string, int> listOf = new Dictionary<string, int>();
            Dictionary<int, uint> lodOfList = new Dictionary<int, uint>();
            Dictionary<SubSection, int> ownList = new Dictionary<SubSection, int>();
            for (int l = 0; l < this.DrawLists.Count; l++)
            {
                foreach (uint entry in this.DrawLists[l])
                {
                    SubSection sub = this.Sections[(int) (entry >> 16)].SubSections[(int) (entry & 0xFFFF)];
                    lodOfList[l] = sub.Lod;
                    string key = sub.Name + "@" + sub.Lod;
                    bool own = (int) (entry >> 16) == sectionIndex;
                    if (own)
                    {
                        ownList[sub] = l;
                    }
                    if (own || !listOf.ContainsKey(key))
                    {
                        listOf[key] = l;
                    }
                }
            }

            work.Template = null;
            foreach (SubSection sub in section.SubSections)
            {
                if ((sub.Lod == 0) == useLod0)
                {
                    work.Template = sub.Copy();
                    break;
                }
            }
            if (work.Template == null)
            {
                work.Template = this.AnySubSection(useLod0);
            }
            if (work.Template == null)
            {
                throw new InvalidDataException("No subsection to use as a template in this file.");
            }
            int indexSize = IndexSizeOfFile(this, useLod0);
            if (indexSize != 0 && indexSize != work.Template.IndexSize)
            {
                ConvertIndices(work.Template, indexSize);
            }
            work.Restart = (work.Template.IndexSize == 2) ? 0xFFFF : 0x00FFFFFF;

            List<SubSection> newSubs = new List<SubSection>();
            List<KeyValuePair<int, SubSection>> placed = new List<KeyValuePair<int, SubSection>>();
            Dictionary<ObjMesh, List<SubSection>> built = new Dictionary<ObjMesh, List<SubSection>>();
            Dictionary<int, int> keptVertex = new Dictionary<int, int>();
            bool rebuilt = false;
            // Subsections of the other buffer (wheels): kept as they are, LOD 0 ones before the others.
            List<SubSection> otherSubs = new List<SubSection>();
            foreach (SubSection sub in section.SubSections)
            {
                if ((sub.Lod == 0) != useLod0)
                {
                    otherSubs.Add(sub);
                    int list;
                    if (ownList.TryGetValue(sub, out list))
                    {
                        placed.Add(new KeyValuePair<int, SubSection>(list, sub));
                    }
                }
            }
            if (!useLod0)
            {
                newSubs.AddRange(otherSubs);
            }
            foreach (uint lod in lods)
            {
                ObjMesh mesh = meshes[lod];
                if (mesh == null)
                {
                    // LOD kept: its subsections as they are, over copies of the vertices they use.
                    foreach (SubSection sub in section.SubSections)
                    {
                        if (sub.Lod != lod)
                        {
                            continue;
                        }
                        SubSection copy = sub.Copy();
                        for (int i = 0; i < copy.Indices.Count; i++)
                        {
                            int index = copy.Indices[i];
                            if (IsRestart(copy, index))
                            {
                                continue;
                            }
                            int mapped;
                            if (!keptVertex.TryGetValue(index, out mapped))
                            {
                                mapped = work.Vertices.Count;
                                keptVertex[index] = mapped;
                                byte[] vertex = new byte[work.Stride];
                                Buffer.BlockCopy(work.ReferenceData, index * work.Stride, vertex, 0, work.Stride);
                                work.Vertices.Add(vertex);
                                work.Positions.Add(work.Reference[index * 3]);
                                work.Positions.Add(work.Reference[(index * 3) + 1]);
                                work.Positions.Add(work.Reference[(index * 3) + 2]);
                                byte[] word = new byte[work.DamageStride];
                                if (work.DamageStride > 0)
                                {
                                    Buffer.BlockCopy(work.ReferenceDamage, index * work.DamageStride, word, 0, work.DamageStride);
                                }
                                work.Words.Add(word);
                            }
                            copy.Indices[i] = mapped;
                        }
                        newSubs.Add(copy);
                        placed.Add(new KeyValuePair<int, SubSection>(ownList[sub], copy));
                    }
                    continue;
                }
                rebuilt = true;
                List<SubSection> subs;
                if (!built.TryGetValue(mesh, out subs))
                {
                    subs = this.AppendMesh(work, mesh, materialMap, fitUv);
                    built[mesh] = subs;
                }
                foreach (SubSection sub in subs)
                {
                    SubSection copy = sub.Copy();
                    copy.Lod = lod;
                    int list;
                    if (!listOf.TryGetValue(copy.Name + "@" + lod, out list))
                    {
                        list = -1;
                        foreach (KeyValuePair<int, uint> pair in lodOfList)
                        {
                            if (pair.Value == lod && (list < 0 || this.DrawLists[pair.Key].Length > this.DrawLists[list].Length))
                            {
                                list = pair.Key;
                            }
                        }
                    }
                    if (list < 0)
                    {
                        throw new InvalidDataException("No draw list for LOD " + lod + ".");
                    }
                    newSubs.Add(copy);
                    placed.Add(new KeyValuePair<int, SubSection>(list, copy));
                }
            }
            if (newSubs.Count == otherSubs.Count)
            {
                throw new InvalidDataException(sectionName + ": the OBJs have no usable triangles.");
            }
            if (useLod0)
            {
                newSubs.AddRange(otherSubs);
            }
            // Positions of the other buffer, re-encoded with the new bounds (decoded before they change).
            double[] other = Positions(section, useLod0 ? section.Lod : section.Lod0);
            if (work.Template.IndexSize == 2 && work.Vertices.Count >= 0xFFFF)
            {
                throw new InvalidDataException(string.Format("{0}: {1:N0} vertices do not fit the 16-bit indices of this file (max 65,534).", sectionName, work.Vertices.Count));
            }
            section.SubSections.Clear();
            section.SubSections.AddRange(newSubs);

            // Vertex buffer and per-vertex table.
            byte[] data = new byte[work.Vertices.Count * work.Stride];
            byte[] damageData = new byte[work.Vertices.Count * work.DamageStride];
            for (int i = 0; i < work.Vertices.Count; i++)
            {
                Buffer.BlockCopy(work.Vertices[i], 0, data, i * work.Stride, work.Stride);
                Buffer.BlockCopy(work.Words[i], 0, damageData, i * work.DamageStride, work.DamageStride);
            }
            work.Target.Data = data;
            work.Target.Count = work.Vertices.Count;
            work.Target.Stride = work.Stride;
            if (work.DamageStride > 0)
            {
                damage.Count = work.Vertices.Count;
                damage.Data = damageData;
            }
            // GlassParticles (A/B: the mesh the glass shatters into, only next to LOD1 in main files) follow the old
            // shape: rebuilt from the new LOD1 triangles (CarbinGlass.cs) once the buffer above is in place.
            if (rebuilt && !useLod0 && (section.ACount > 0 || section.BCount > 0))
            {
                int particles = BuildGlassParticles(section);
                work.Report.AppendFormat("{0}: glass particles rebuilt from the new shape ({1} vertices, {2} triangles)\r\n", sectionName, particles, section.GlassIndexCount / 3);
            }

            // Draw lists: old entries of the part out, new ones in.
            for (int l = 0; l < this.DrawLists.Count; l++)
            {
                List<uint> list = new List<uint>();
                foreach (uint entry in this.DrawLists[l])
                {
                    if ((int) (entry >> 16) != sectionIndex)
                    {
                        list.Add(entry);
                    }
                }
                for (int k = 0; k < section.SubSections.Count; k++)
                {
                    foreach (KeyValuePair<int, SubSection> pair in placed)
                    {
                        if (pair.Key == l && pair.Value == section.SubSections[k])
                        {
                            list.Add((uint) ((sectionIndex << 16) | k));
                        }
                    }
                }
                this.DrawLists[l] = list.ToArray();
            }

            double[] all = work.Positions.ToArray();
            Reencode(section, useLod0 ? other : all, useLod0 ? all : other);
            CheckIndexRange(section);
            this.CheckDrawLists();
            work.Report.AppendFormat("{0}: {1} vertices, LODs {2}", sectionName, work.Vertices.Count, string.Join(",", lods.ConvertAll<string>(delegate(uint x) { return x.ToString(); }).ToArray()));
            return work.Report.ToString();
        }

        // A subsection of any part drawn from the given buffer kind (template for a part with none).
        private SubSection AnySubSection(bool lod0)
        {
            foreach (Section section in this.Sections)
            {
                foreach (SubSection sub in section.SubSections)
                {
                    if ((sub.Lod == 0) == lod0)
                    {
                        return sub.Copy();
                    }
                }
            }
            return null;
        }

        // Appends an OBJ mesh's vertices to the rebuild and returns one subsection per material (LOD
        // still unset). Vertices: one per distinct (material, position, uv, normal).
        private List<SubSection> AppendMesh(Rebuild work, ObjMesh mesh, Dictionary<string, string> materialMap, bool fitUvAll)
        {
            List<SubSection> result = new List<SubSection>();
            foreach (ObjMesh.Material material in mesh.Materials)
            {
                bool fitUv = fitUvAll && !material.Exact;
                string carMaterial = material.Name;
                if (materialMap != null && (!materialMap.TryGetValue(material.Name, out carMaterial) || string.IsNullOrEmpty(carMaterial)))
                {
                    work.Report.AppendFormat("OBJ material {0} skipped (no car material chosen)\r\n", material.Name);
                    continue;
                }
                SubSection sub = work.Template.Copy();
                sub.Name = carMaterial;
                sub.Indices = new List<int>();
                SubSection own = this.FindMaterial(carMaterial, work.Lod0);
                float[] uvParams = (own != null) ? (float[]) own.Uv.Clone() : (float[]) work.Template.Uv.Clone();

                // UV range of this material in the OBJ (Forza convention: v = 1 - OBJ v).
                float[] lo = { float.MaxValue, float.MaxValue };
                float[] hi = { float.MinValue, float.MinValue };
                foreach (ObjMesh.Corner[] triangle in material.Triangles)
                {
                    foreach (ObjMesh.Corner corner in triangle)
                    {
                        float u = corner.Uv.X;
                        float w = 1f - corner.Uv.Y;
                        lo[0] = Math.Min(lo[0], u);
                        hi[0] = Math.Max(hi[0], u);
                        lo[1] = Math.Min(lo[1], w);
                        hi[1] = Math.Max(hi[1], w);
                    }
                }
                if (!fitUv)
                {
                    // Region = the OBJ's own range.
                    for (int c = 0; c < 2; c++)
                    {
                        uvParams[c * 2] = lo[c];
                        uvParams[(c * 2) + 1] = Math.Max(hi[c] - lo[c], 1e-6f);
                    }
                }

                // Per-corner tangents from the UV gradients, accumulated per vertex.
                Dictionary<string, int> unique = new Dictionary<string, int>();
                List<Microsoft.Xna.Framework.Vector3> normalSum = new List<Microsoft.Xna.Framework.Vector3>();
                List<Microsoft.Xna.Framework.Vector3> tangentSum = new List<Microsoft.Xna.Framework.Vector3>();
                List<Microsoft.Xna.Framework.Vector3> bitangentSum = new List<Microsoft.Xna.Framework.Vector3>();
                List<int[]> faces = new List<int[]>();
                List<ObjMesh.Corner> cornersOf = new List<ObjMesh.Corner>();
                int flipped = 0;
                int kept = 0;
                foreach (ObjMesh.Corner[] triangle in material.Triangles)
                {
                    Microsoft.Xna.Framework.Vector3 face = Microsoft.Xna.Framework.Vector3.Cross(triangle[1].Position - triangle[0].Position, triangle[2].Position - triangle[0].Position);
                    if (face.LengthSquared() < 1e-16f)
                    {
                        continue;
                    }
                    ObjMesh.Corner[] t = triangle;
                    if (triangle[0].HasNormal && triangle[1].HasNormal && triangle[2].HasNormal
                        && Microsoft.Xna.Framework.Vector3.Dot(face, triangle[0].Normal + triangle[1].Normal + triangle[2].Normal) < 0)
                    {
                        // Winding disagrees with the normals: the game's front face is (b-a)x(c-a).
                        t = new ObjMesh.Corner[] { triangle[1], triangle[0], triangle[2] };
                        face = -face;
                        flipped++;
                    }
                    int[] ids = new int[3];
                    for (int k = 0; k < 3; k++)
                    {
                        ObjMesh.Corner corner = t[k];
                        string key = string.Format(CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R}|{3:R},{4:R}|{5:R},{6:R},{7:R}", corner.Position.X, corner.Position.Y, corner.Position.Z, corner.Uv.X, corner.Uv.Y, corner.Normal.X, corner.Normal.Y, corner.Normal.Z);
                        int id;
                        if (!unique.TryGetValue(key, out id))
                        {
                            id = cornersOf.Count;
                            unique[key] = id;
                            cornersOf.Add(corner);
                            normalSum.Add(Microsoft.Xna.Framework.Vector3.Zero);
                            tangentSum.Add(Microsoft.Xna.Framework.Vector3.Zero);
                            bitangentSum.Add(Microsoft.Xna.Framework.Vector3.Zero);
                        }
                        ids[k] = id;
                    }
                    // UV gradient tangent frame of the triangle.
                    Microsoft.Xna.Framework.Vector3 e1 = t[1].Position - t[0].Position;
                    Microsoft.Xna.Framework.Vector3 e2 = t[2].Position - t[0].Position;
                    float du1 = t[1].Uv.X - t[0].Uv.X;
                    float dv1 = (1f - t[1].Uv.Y) - (1f - t[0].Uv.Y);
                    float du2 = t[2].Uv.X - t[0].Uv.X;
                    float dv2 = (1f - t[2].Uv.Y) - (1f - t[0].Uv.Y);
                    float det = (du1 * dv2) - (du2 * dv1);
                    Microsoft.Xna.Framework.Vector3 tangent = Microsoft.Xna.Framework.Vector3.Zero;
                    Microsoft.Xna.Framework.Vector3 bitangent = Microsoft.Xna.Framework.Vector3.Zero;
                    if (Math.Abs(det) > 1e-12f)
                    {
                        tangent = ((e1 * dv2) - (e2 * dv1)) / det;
                        bitangent = ((e2 * du1) - (e1 * du2)) / det;
                    }
                    foreach (int id in ids)
                    {
                        normalSum[id] += face;
                        tangentSum[id] += tangent;
                        bitangentSum[id] += bitangent;
                    }
                    faces.Add(ids);
                }
                if (faces.Count == 0)
                {
                    continue;
                }
                int baseIndex = work.Vertices.Count;
                List<float[]> uv2 = new List<float[]>();
                for (int id = 0; id < cornersOf.Count; id++)
                {
                    ObjMesh.Corner corner = cornersOf[id];
                    Microsoft.Xna.Framework.Vector3 n = corner.HasNormal ? corner.Normal : Microsoft.Xna.Framework.Vector3.Normalize(normalSum[id]);
                    int nearest = work.Tree.Nearest(corner.Position.X, corner.Position.Y, corner.Position.Z);
                    byte[] vertex = new byte[work.Stride];
                    if (nearest >= 0)
                    {
                        Buffer.BlockCopy(work.ReferenceData, nearest * work.Stride, vertex, 0, work.Stride);
                    }
                    // UV1 in the chosen region.
                    float u = corner.Uv.X;
                    float w = 1f - corner.Uv.Y;
                    float[] range = { hi[0] - lo[0], hi[1] - lo[1] };
                    float[] value = { u, w };
                    for (int c = 0; c < 2; c++)
                    {
                        float normalized = fitUv ? ((range[c] > 1e-9f) ? (value[c] - lo[c]) / range[c] : 0.5f) : (value[c] - uvParams[c * 2]) / uvParams[(c * 2) + 1];
                        int raw = Math.Max(0, Math.Min(65535, (int) Math.Round(normalized * 65535)));
                        vertex[8 + (c * 2)] = (byte) (raw >> 8);
                        vertex[9 + (c * 2)] = (byte) raw;
                    }
                    // A vertex the part already had (same place, normal and UV: the untouched areas of
                    // an edited part) keeps the game's own tangent frame, copied above.
                    int same = SameVertex(work.Tree, work.ReferenceUv, work.Target, corner.Position, n, u, w);
                    if (same >= 0)
                    {
                        // Frame and the 8 trailing bytes as they were.
                        Buffer.BlockCopy(work.ReferenceData, (same * work.Stride) + 16, vertex, 16, work.Stride - 16);
                        nearest = same;
                        kept++;
                    }
                    else
                    {
                        WriteFrame(vertex, n, tangentSum[id], bitangentSum[id]);
                        WriteTail(vertex, n, work.Stride);
                    }
                    work.Vertices.Add(vertex);
                    work.Positions.Add(corner.Position.X);
                    work.Positions.Add(corner.Position.Y);
                    work.Positions.Add(corner.Position.Z);
                    // Per-vertex table entry of the same vertex, else of the nearest one.
                    byte[] word = new byte[work.DamageStride];
                    if (work.DamageStride > 0 && nearest >= 0)
                    {
                        Buffer.BlockCopy(work.ReferenceDamage, nearest * work.DamageStride, word, 0, work.DamageStride);
                    }
                    work.Words.Add(word);
                    uv2.Add((nearest >= 0) ? new float[] { work.ReferenceUv2[nearest * 2], work.ReferenceUv2[(nearest * 2) + 1] } : new float[] { float.NaN, float.NaN });
                }
                EncodeUv2(work, baseIndex, uv2, uvParams);
                foreach (int[] face in faces)
                {
                    if (sub.Indices.Count > 0)
                    {
                        sub.Indices.Add(work.Restart);
                    }
                    sub.Indices.Add(face[0] + baseIndex);
                    sub.Indices.Add(face[1] + baseIndex);
                    sub.Indices.Add(face[2] + baseIndex);
                }
                sub.Uv = uvParams;
                result.Add(sub);
                work.Report.AppendFormat("{0} -> {1}: {2} triangles, {3} vertices ({4} unchanged, game tangent frame kept){5}\r\n", material.Name, carMaterial, faces.Count, cornersOf.Count, kept, (flipped > 0) ? string.Format(" ({0} triangles turned to match the normals)", flipped) : "");
            }
            return result;
        }

        // UV2 of new vertices (bytes 12-15): the absolute UV2 of the vertex they came from, encoded in
        // the subsection's own UV2 range. The raw bytes belong to the source subsection's range, and
        // the same material has a different range at each LOD (wheels). A constant UV2 keeps scale 0,
        // as the game's files store it.
        private static void EncodeUv2(Rebuild work, int baseIndex, List<float[]> uv2, float[] uvParams)
        {
            for (int c = 0; c < 2; c++)
            {
                float lo = float.MaxValue;
                float hi = float.MinValue;
                foreach (float[] value in uv2)
                {
                    if (!float.IsNaN(value[c]))
                    {
                        lo = Math.Min(lo, value[c]);
                        hi = Math.Max(hi, value[c]);
                    }
                }
                if (lo > hi)
                {
                    continue;   // no source UV2: raw bytes and range as copied
                }
                float range = hi - lo;
                uvParams[4 + (c * 2)] = lo;
                uvParams[5 + (c * 2)] = (range > 1e-9f) ? range : 0f;
                for (int i = 0; i < uv2.Count; i++)
                {
                    float value = float.IsNaN(uv2[i][c]) ? lo : uv2[i][c];
                    int raw = (range > 1e-9f) ? Math.Max(0, Math.Min(65535, (int) Math.Round(((value - lo) / range) * 65535))) : 0;
                    byte[] vertex = work.Vertices[baseIndex + i];
                    vertex[12 + (c * 2)] = (byte) (raw >> 8);
                    vertex[13 + (c * 2)] = (byte) raw;
                }
            }
        }

        // The 8 trailing vertex bytes: four SNORM16, t0 a level (high on open surfaces, low in
        // recesses) and (t1, t2, t3) a vector along the normal, world = (-t3, -t1, t2), of length
        // ~1.24 t0 - 0.07 (fitted on 278702 vertices of two cars; looks like baked ambient light,
        // exact recipe unknown). Copying them from the nearest old vertex made blotches on new shapes;
        // a new vertex gets the level of an open panel facing its way (0.83 facing up or sideways,
        // down to 0.50 facing the ground: 75th percentile of the two cars' exterior panels by normal)
        // and its own normal, which shades smoothly and as bright as the bodywork around it.
        // FH (28-byte vertices) stores the same four values as SNORM8: on an FH2 car |v| ~ 1.19 t0 -
        // 0.02 and open panels reach 0.83 facing up, ~0.60 facing down (120884 vertices).
        private static void WriteTail(byte[] vertex, Microsoft.Xna.Framework.Vector3 n, int stride)
        {
            Microsoft.Xna.Framework.Vector3 d = Microsoft.Xna.Framework.Vector3.Normalize(n);
            float facing = Math.Max(0f, Math.Min(1f, (d.Y + 1f) / 1.25f));
            if (stride == 0x1c)
            {
                float fhLevel = 0.60f + (0.23f * facing);
                float fhLength = Math.Max(0f, Math.Min(1f, (1.19f * fhLevel) - 0.02f));
                vertex[24] = (byte) (sbyte) Math.Max(-127, Math.Min(127, (int) Math.Round(fhLevel * 127)));
                vertex[25] = (byte) (sbyte) Math.Max(-127, Math.Min(127, (int) Math.Round(-d.Y * fhLength * 127)));
                vertex[26] = (byte) (sbyte) Math.Max(-127, Math.Min(127, (int) Math.Round(d.Z * fhLength * 127)));
                vertex[27] = (byte) (sbyte) Math.Max(-127, Math.Min(127, (int) Math.Round(-d.X * fhLength * 127)));
                return;
            }
            float level = 0.50f + (0.33f * facing);
            float length = Math.Max(0f, Math.Min(1f, (1.24f * level) - 0.07f));
            WriteS16(vertex, 24, Clamp((int) Math.Round(level * 32767)));
            WriteS16(vertex, 26, Clamp((int) Math.Round(-d.Y * length * 32767)));
            WriteS16(vertex, 28, Clamp((int) Math.Round(d.Z * length * 32767)));
            WriteS16(vertex, 30, Clamp((int) Math.Round(-d.X * length * 32767)));
        }

        // The original vertex within 0.1 mm, 1 degree and 1e-4 in UV of the new one, or -1. Several
        // originals share a place on hard edges and UV seams, so all of them are tried (lowest index
        // first).
        private static int SameVertex(PointTree tree, float[] referenceUv, VertexBuffer buffer, Microsoft.Xna.Framework.Vector3 p, Microsoft.Xna.Framework.Vector3 n, float u, float v)
        {
            Microsoft.Xna.Framework.Vector3 wanted = Microsoft.Xna.Framework.Vector3.Normalize(n);
            List<int> near = tree.Within(p.X, p.Y, p.Z, 1e-8);
            near.Sort();
            foreach (int index in near)
            {
                bool uv = Math.Abs(referenceUv[index * 2] - u) < 1e-4f && Math.Abs(referenceUv[(index * 2) + 1] - v) < 1e-4f;
                if (uv && Microsoft.Xna.Framework.Vector3.Dot(VertexNormal(buffer, index), wanted) > 0.99985f)
                {
                    return index;
                }
            }
            return -1;
        }

        // Static k-d tree over a vertex list (3 doubles per point): nearest point and points within a
        // radius, so rebuilding a 50,000-vertex part does not compare every pair.
        private sealed class PointTree
        {
            private readonly double[] points;
            private readonly int[] order;
            private readonly byte[] axis;

            public PointTree(double[] points, int count)
            {
                this.points = points;
                this.order = new int[count];
                this.axis = new byte[count];
                for (int i = 0; i < count; i++)
                {
                    this.order[i] = i;
                }
                this.Build(0, count, new double[count]);
            }

            private void Build(int lo, int hi, double[] keys)
            {
                if (hi - lo <= 1)
                {
                    return;
                }
                // Split on the widest axis of the segment, at the median.
                int split = 0;
                double widest = -1;
                for (int c = 0; c < 3; c++)
                {
                    double min = double.MaxValue;
                    double max = double.MinValue;
                    for (int i = lo; i < hi; i++)
                    {
                        double value = this.points[(this.order[i] * 3) + c];
                        min = Math.Min(min, value);
                        max = Math.Max(max, value);
                    }
                    if (max - min > widest)
                    {
                        widest = max - min;
                        split = c;
                    }
                }
                for (int i = lo; i < hi; i++)
                {
                    keys[i] = this.points[(this.order[i] * 3) + split];
                }
                Array.Sort(keys, this.order, lo, hi - lo);
                int mid = (lo + hi) / 2;
                this.axis[mid] = (byte) split;
                this.Build(lo, mid, keys);
                this.Build(mid + 1, hi, keys);
            }

            private double Distance(int point, double x, double y, double z)
            {
                double dx = this.points[point * 3] - x;
                double dy = this.points[(point * 3) + 1] - y;
                double dz = this.points[(point * 3) + 2] - z;
                return (dx * dx) + (dy * dy) + (dz * dz);
            }

            // Nearest point (lowest index on ties), -1 when empty.
            public int Nearest(double x, double y, double z)
            {
                int best = -1;
                double bestDistance = double.MaxValue;
                this.Nearest(0, this.order.Length, x, y, z, ref best, ref bestDistance);
                return best;
            }

            private void Nearest(int lo, int hi, double x, double y, double z, ref int best, ref double bestDistance)
            {
                if (lo >= hi)
                {
                    return;
                }
                int mid = (lo + hi) / 2;
                int point = this.order[mid];
                double distance = this.Distance(point, x, y, z);
                if (distance < bestDistance || (distance == bestDistance && point < best))
                {
                    bestDistance = distance;
                    best = point;
                }
                if (hi - lo == 1)
                {
                    return;
                }
                int c = this.axis[mid];
                double diff = ((c == 0) ? x : ((c == 1) ? y : z)) - this.points[(point * 3) + c];
                if (diff < 0)
                {
                    this.Nearest(lo, mid, x, y, z, ref best, ref bestDistance);
                    if (diff * diff <= bestDistance)
                    {
                        this.Nearest(mid + 1, hi, x, y, z, ref best, ref bestDistance);
                    }
                }
                else
                {
                    this.Nearest(mid + 1, hi, x, y, z, ref best, ref bestDistance);
                    if (diff * diff <= bestDistance)
                    {
                        this.Nearest(lo, mid, x, y, z, ref best, ref bestDistance);
                    }
                }
            }

            // Points within sqrt(radiusSquared).
            public List<int> Within(double x, double y, double z, double radiusSquared)
            {
                List<int> found = new List<int>();
                this.Within(0, this.order.Length, x, y, z, radiusSquared, found);
                return found;
            }

            private void Within(int lo, int hi, double x, double y, double z, double radiusSquared, List<int> found)
            {
                if (lo >= hi)
                {
                    return;
                }
                int mid = (lo + hi) / 2;
                int point = this.order[mid];
                if (this.Distance(point, x, y, z) <= radiusSquared)
                {
                    found.Add(point);
                }
                if (hi - lo == 1)
                {
                    return;
                }
                int c = this.axis[mid];
                double diff = ((c == 0) ? x : ((c == 1) ? y : z)) - this.points[(point * 3) + c];
                if (diff <= 0 || diff * diff <= radiusSquared)
                {
                    this.Within(lo, mid, x, y, z, radiusSquared, found);
                }
                if (diff >= 0 || diff * diff <= radiusSquared)
                {
                    this.Within(mid + 1, hi, x, y, z, radiusSquared, found);
                }
            }
        }

        // Tangent frame quaternion as in the game's models (measured on 221742 vertices of two cars):
        // X = normal, -Z = the direction the texture v grows (always), Y = Z x X (so +-dP/du, by UV
        // handedness). The frame carries no handedness; the sign of w is free (q and -q are the
        // same rotation) and kept >= 0.
        private static void WriteFrame(byte[] vertex, Microsoft.Xna.Framework.Vector3 n, Microsoft.Xna.Framework.Vector3 tangent, Microsoft.Xna.Framework.Vector3 bitangent)
        {
            if (n.LengthSquared() < 1e-12f)
            {
                n = Microsoft.Xna.Framework.Vector3.UnitY;
            }
            n.Normalize();
            // dP/dv in the normal's plane; without UVs (untextured materials such as plastic2, where
            // the game's own frames follow no rule) Z leans on the car's length axis, else on X.
            Microsoft.Xna.Framework.Vector3 v = bitangent - (n * Microsoft.Xna.Framework.Vector3.Dot(n, bitangent));
            if (v.LengthSquared() < 1e-12f)
            {
                Microsoft.Xna.Framework.Vector3 axis = (Math.Abs(n.Z) < 0.9f) ? Microsoft.Xna.Framework.Vector3.UnitZ : Microsoft.Xna.Framework.Vector3.UnitX;
                v = -(axis - (n * Microsoft.Xna.Framework.Vector3.Dot(n, axis)));
            }
            v.Normalize();
            Microsoft.Xna.Framework.Vector3 z = -v;
            Microsoft.Xna.Framework.Vector3 y = Microsoft.Xna.Framework.Vector3.Cross(z, n);
            Microsoft.Xna.Framework.Matrix m = Microsoft.Xna.Framework.Matrix.Identity;
            m.M11 = n.X;
            m.M12 = n.Y;
            m.M13 = n.Z;
            m.M21 = y.X;
            m.M22 = y.Y;
            m.M23 = y.Z;
            m.M31 = z.X;
            m.M32 = z.Y;
            m.M33 = z.Z;
            Microsoft.Xna.Framework.Quaternion q = Microsoft.Xna.Framework.Quaternion.CreateFromRotationMatrix(m);
            q.Normalize();
            if (q.W < 0)
            {
                q = Microsoft.Xna.Framework.Quaternion.Negate(q);
            }
            WriteS16(vertex, 16, Clamp((int) Math.Round(q.X * 32767)));
            WriteS16(vertex, 18, Clamp((int) Math.Round(q.Y * 32767)));
            WriteS16(vertex, 20, Clamp((int) Math.Round(q.Z * 32767)));
            WriteS16(vertex, 22, Clamp((int) Math.Round(q.W * 32767)));
        }

        #endregion

        #region Import from another model

        private static Section CopySection(Section s)
        {
            Section copy = new Section();
            copy.Marker = (byte[]) s.Marker.Clone();
            copy.Bounds = (float[]) s.Bounds.Clone();
            copy.Unk1c = (byte[]) s.Unk1c.Clone();
            copy.ACount = s.ACount;
            copy.A = (byte[]) s.A.Clone();
            copy.AfterA = (byte[]) s.AfterA.Clone();
            copy.BCount = s.BCount;
            copy.B = (byte[]) s.B.Clone();
            copy.Pre = (byte[]) s.Pre.Clone();
            copy.VbTest1 = s.VbTest1;
            copy.VbTest2 = s.VbTest2;
            copy.Name = s.Name;
            copy.PostName = (byte[]) s.PostName.Clone();
            copy.Lod = CopyBuffer(s.Lod);
            copy.AfterLod = (byte[]) s.AfterLod.Clone();
            foreach (SubSection sub in s.SubSections)
            {
                copy.SubSections.Add(sub.Copy());
            }
            copy.AfterSubs = (byte[]) s.AfterSubs.Clone();
            copy.Lod0 = CopyBuffer(s.Lod0);
            copy.Post9 = (byte[]) s.Post9.Clone();
            copy.DamageLod = CopyTable(s.DamageLod);
            copy.Mid = (byte[]) s.Mid.Clone();
            copy.DamageLod0 = CopyTable(s.DamageLod0);
            return copy;
        }

        private static VertexBuffer CopyBuffer(VertexBuffer b)
        {
            VertexBuffer copy = new VertexBuffer();
            copy.Count = b.Count;
            copy.Stride = b.Stride;
            copy.Extra = (byte[]) b.Extra.Clone();
            copy.Data = (byte[]) b.Data.Clone();
            return copy;
        }

        private static Table CopyTable(Table t)
        {
            Table copy = new Table();
            copy.Count = t.Count;
            copy.Stride = t.Stride;
            copy.Data = (byte[]) t.Data.Clone();
            return copy;
        }

        // Copies a section of another model of the same kind (main / lod0 / cockpit) into this one,
        // with its vertices, tangent frames, damage data and all LODs, under 'newName'. Its draw-list
        // entries go to the same lists (same LOD and render pass) as in the source. When
        // 'useOwnMaterialRegions' is set, each imported material takes the UV region this model already
        // uses for a material of the same name: UVs index the car's own texture sheets, so the source
        // car's regions would sample unrelated pixels here. Returns a short report.
        public string ImportSection(CarbinModel source, string sourceName, string newName, bool useOwnMaterialRegions)
        {
            Section from = source.Find(sourceName);
            if (from == null)
            {
                throw new ArgumentException("The source model has no part " + sourceName + ".");
            }
            if (this.Find(newName) != null)
            {
                throw new ArgumentException("This model already has a part named " + newName + ".");
            }
            if (source.DrawLists.Count != this.DrawLists.Count)
            {
                throw new NotSupportedException(string.Format("The models have different LOD layouts ({0} and {1} draw lists): import between files of the same kind (main, lod0, cockpit).", source.DrawLists.Count / 2, this.DrawLists.Count / 2));
            }
            Section copy = CopySection(from);
            copy.Name = newName;
            foreach (SubSection sub in copy.SubSections)
            {
                int expected = IndexSizeOfFile(this, sub.Lod == 0);
                if (expected != 0 && expected != sub.IndexSize)
                {
                    ConvertIndices(sub, expected);
                }
            }
            StringBuilder report = new StringBuilder();
            if (useOwnMaterialRegions)
            {
                foreach (SubSection sub in copy.SubSections)
                {
                    SubSection own = this.FindMaterial(sub.Name, sub.Lod == 0);
                    if (own != null)
                    {
                        sub.Uv = (float[]) own.Uv.Clone();
                    }
                    else
                    {
                        report.AppendFormat("material {0} is not used by this car: it keeps the source UV region\r\n", sub.Name);
                    }
                }
            }
            CheckIndexRange(copy);
            int sourceIndex = source.Sections.IndexOf(from);
            this.Sections.Add(copy);
            int index = this.Sections.Count - 1;
            for (int l = 0; l < source.DrawLists.Count; l++)
            {
                List<uint> list = new List<uint>(this.DrawLists[l]);
                foreach (uint entry in source.DrawLists[l])
                {
                    if ((int) (entry >> 16) == sourceIndex)
                    {
                        list.Add((uint) ((index << 16) | (entry & 0xFFFF)));
                    }
                }
                this.DrawLists[l] = list.ToArray();
            }
            this.CheckDrawLists();
            report.AppendFormat("{0} imported as {1}: {2} materials, {3} + {4} vertices", sourceName, newName, copy.SubSections.Count, copy.Lod.Count, copy.Lod0.Count);
            return report.ToString();
        }

        // A copy of one of this model's parts under a new name (same materials, damage data, LODs and
        // draw lists): the template for a part added from an OBJ.
        public void DuplicateSection(string name, string newName)
        {
            this.ImportSection(this, name, newName, false);
        }

        // A subsection of this model using the material, preferring the given buffer kind.
        private SubSection FindMaterial(string name, bool lod0)
        {
            SubSection fallback = null;
            foreach (Section section in this.Sections)
            {
                foreach (SubSection sub in section.SubSections)
                {
                    if (sub.Name != name)
                    {
                        continue;
                    }
                    if ((sub.Lod == 0) == lod0)
                    {
                        return sub;
                    }
                    if (fallback == null)
                    {
                        fallback = sub;
                    }
                }
            }
            return fallback;
        }

        private static int IndexSizeOfFile(CarbinModel model, bool lod0)
        {
            foreach (Section section in model.Sections)
            {
                foreach (SubSection sub in section.SubSections)
                {
                    if ((sub.Lod == 0) == lod0)
                    {
                        return sub.IndexSize;
                    }
                }
            }
            return 0;
        }

        private static void ConvertIndices(SubSection sub, int size)
        {
            int restart = (size == 2) ? 0xFFFF : 0x00FFFFFF;
            for (int i = 0; i < sub.Indices.Count; i++)
            {
                if (IsRestart(sub, sub.Indices[i]))
                {
                    sub.Indices[i] = restart;
                }
            }
            sub.IndexSize = size;
        }

        // Moves / rotates / scales a whole section (positions and tangent frames), then refits bounds.
        public void TransformSection(string name, Microsoft.Xna.Framework.Matrix m)
        {
            Section section = this.Find(name);
            if (section == null)
            {
                return;
            }
            Microsoft.Xna.Framework.Quaternion rotation;
            Microsoft.Xna.Framework.Matrix normalMatrix;
            FrameTransform(m, out rotation, out normalMatrix);
            double[][] all = new double[2][];
            VertexBuffer[] buffers = { section.Lod, section.Lod0 };
            for (int b = 0; b < 2; b++)
            {
                VertexBuffer buffer = buffers[b];
                double[] positions = Positions(section, buffer);
                for (int i = 0; i < buffer.Count; i++)
                {
                    Microsoft.Xna.Framework.Vector3 p = new Microsoft.Xna.Framework.Vector3((float) positions[i * 3], (float) positions[(i * 3) + 1], (float) positions[(i * 3) + 2]);
                    p = Microsoft.Xna.Framework.Vector3.Transform(p, m);
                    positions[i * 3] = p.X;
                    positions[(i * 3) + 1] = p.Y;
                    positions[(i * 3) + 2] = p.Z;
                    TransformFrame(buffer.Data, (i * buffer.Stride) + 16, rotation, normalMatrix);
                }
                all[b] = positions;
            }
            Reencode(section, all[0], all[1]);
        }

        // Mirrors a whole section across the plane x = 'plane' (left part made from the right one):
        // positions, tangent frames (MirrorFrame) and the strips' winding, so the faces still point out.
        public void MirrorSection(string name, double plane)
        {
            Section section = this.Find(name);
            if (section == null)
            {
                return;
            }
            double[][] all = new double[2][];
            VertexBuffer[] buffers = { section.Lod, section.Lod0 };
            for (int b = 0; b < 2; b++)
            {
                VertexBuffer buffer = buffers[b];
                double[] positions = Positions(section, buffer);
                for (int i = 0; i < buffer.Count; i++)
                {
                    positions[i * 3] = (2 * plane) - positions[i * 3];
                    MirrorFrame(buffer.Data, (i * buffer.Stride) + 16);
                }
                all[b] = positions;
            }
            Reencode(section, all[0], all[1]);
            // A strip's triangles alternate winding (StripTriangles): repeating each strip's first index
            // makes its first triangle degenerate and shifts every other one to the opposite winding.
            foreach (SubSection sub in section.SubSections)
            {
                List<int> flipped = new List<int>();
                bool start = true;
                foreach (int index in sub.Indices)
                {
                    if (IsRestart(sub, index))
                    {
                        start = true;
                        flipped.Add(index);
                        continue;
                    }
                    if (start)
                    {
                        flipped.Add(index);
                        start = false;
                    }
                    flipped.Add(index);
                }
                sub.Indices = flipped;
            }
        }

        // Tangent frame at 'offset' mirrored in X: the normal (X axis image) and the Y axis image are
        // mirrored, the Z axis image mirrored and negated so the frame stays a rotation; the sign of w
        // (the handedness, see TransformFrame) flips, so the bitangent the shader rebuilds is the mirror.
        private static void MirrorFrame(byte[] data, int offset)
        {
            Microsoft.Xna.Framework.Quaternion q = new Microsoft.Xna.Framework.Quaternion(
                S16(data, offset) / 32767f, S16(data, offset + 2) / 32767f, S16(data, offset + 4) / 32767f, S16(data, offset + 6) / 32767f);
            if (q.LengthSquared() < 1e-6f)
            {
                return;
            }
            bool negativeW = q.W < 0;
            q.Normalize();
            Microsoft.Xna.Framework.Vector3 x = Microsoft.Xna.Framework.Vector3.Transform(Microsoft.Xna.Framework.Vector3.UnitX, q);
            Microsoft.Xna.Framework.Vector3 y = Microsoft.Xna.Framework.Vector3.Transform(Microsoft.Xna.Framework.Vector3.UnitY, q);
            Microsoft.Xna.Framework.Vector3 z = Microsoft.Xna.Framework.Vector3.Transform(Microsoft.Xna.Framework.Vector3.UnitZ, q);
            Microsoft.Xna.Framework.Matrix m = Microsoft.Xna.Framework.Matrix.Identity;
            m.M11 = -x.X; m.M12 = x.Y; m.M13 = x.Z;
            m.M21 = -y.X; m.M22 = y.Y; m.M23 = y.Z;
            m.M31 = z.X; m.M32 = -z.Y; m.M33 = -z.Z;
            q = Microsoft.Xna.Framework.Quaternion.CreateFromRotationMatrix(m);
            q.Normalize();
            if ((q.W < 0) == negativeW)
            {
                q = Microsoft.Xna.Framework.Quaternion.Negate(q);
            }
            WriteS16(data, offset, Clamp((int) Math.Round(q.X * 32767)));
            WriteS16(data, offset + 2, Clamp((int) Math.Round(q.Y * 32767)));
            WriteS16(data, offset + 4, Clamp((int) Math.Round(q.Z * 32767)));
            WriteS16(data, offset + 6, Clamp((int) Math.Round(q.W * 32767)));
        }

        #endregion

        #region Transforms

        // Rotation and normal matrix of an affine transform (XNA row vectors: p' = p * m).
        public static void FrameTransform(Microsoft.Xna.Framework.Matrix m, out Microsoft.Xna.Framework.Quaternion rotation, out Microsoft.Xna.Framework.Matrix normalMatrix)
        {
            Microsoft.Xna.Framework.Matrix linear = m;
            linear.Translation = Microsoft.Xna.Framework.Vector3.Zero;
            normalMatrix = Microsoft.Xna.Framework.Matrix.Transpose(Microsoft.Xna.Framework.Matrix.Invert(linear));
            Microsoft.Xna.Framework.Vector3 scale;
            Microsoft.Xna.Framework.Vector3 translation;
            if (!linear.Decompose(out scale, out rotation, out translation))
            {
                rotation = Microsoft.Xna.Framework.Quaternion.Identity;
            }
            if (scale.X * scale.Y * scale.Z < 0)
            {
                throw new NotSupportedException("Mirroring (negative scale) would flip the triangle winding.");
            }
        }

        // Tangent frame at 'offset': 4 x SNORM16 quaternion; the normal is its matrix's first row (X axis
        // image). The rotation is applied after the original frame; a remaining normal difference
        // (non-uniform scale) is closed by the shortest arc. The sign of w (likely the bitangent
        // handedness) is kept.
        public static void TransformFrame(byte[] data, int offset, Microsoft.Xna.Framework.Quaternion rotation, Microsoft.Xna.Framework.Matrix normalMatrix)
        {
            Microsoft.Xna.Framework.Quaternion q = new Microsoft.Xna.Framework.Quaternion(
                S16(data, offset) / 32767f, S16(data, offset + 2) / 32767f, S16(data, offset + 4) / 32767f, S16(data, offset + 6) / 32767f);
            if (q.LengthSquared() < 1e-6f)
            {
                return;
            }
            bool negativeW = q.W < 0;
            q.Normalize();
            Microsoft.Xna.Framework.Vector3 normal = Microsoft.Xna.Framework.Vector3.Transform(Microsoft.Xna.Framework.Vector3.UnitX, q);
            Microsoft.Xna.Framework.Vector3 wanted = Microsoft.Xna.Framework.Vector3.TransformNormal(normal, normalMatrix);
            q = Microsoft.Xna.Framework.Quaternion.Concatenate(q, rotation);
            if (wanted.LengthSquared() > 1e-12f)
            {
                wanted.Normalize();
                Microsoft.Xna.Framework.Vector3 current = Microsoft.Xna.Framework.Vector3.Transform(Microsoft.Xna.Framework.Vector3.UnitX, q);
                float dot = Microsoft.Xna.Framework.Vector3.Dot(current, wanted);
                Microsoft.Xna.Framework.Vector3 axis = Microsoft.Xna.Framework.Vector3.Cross(current, wanted);
                if (dot < 0.999999f && axis.LengthSquared() > 1e-12f)
                {
                    axis.Normalize();
                    float angle = (float) Math.Acos(Math.Max(-1f, Math.Min(1f, dot)));
                    q = Microsoft.Xna.Framework.Quaternion.Concatenate(q, Microsoft.Xna.Framework.Quaternion.CreateFromAxisAngle(axis, angle));
                }
            }
            q.Normalize();
            if ((q.W < 0) != negativeW)
            {
                q = Microsoft.Xna.Framework.Quaternion.Negate(q);
            }
            WriteS16(data, offset, Clamp((int) Math.Round(q.X * 32767)));
            WriteS16(data, offset + 2, Clamp((int) Math.Round(q.Y * 32767)));
            WriteS16(data, offset + 4, Clamp((int) Math.Round(q.Z * 32767)));
            WriteS16(data, offset + 6, Clamp((int) Math.Round(q.W * 32767)));
        }

        // AABB center and half size of the vertices used by the named subsections (all LODs), from the
        // lod0 buffer when they use it, else the LOD buffer. False if the material is not in the section.
        public static bool MaterialBounds(Section section, string subName, out double[] center, out double[] half)
        {
            center = new double[3];
            half = new double[3];
            foreach (bool lod0 in new bool[] { true, false })
            {
                VertexBuffer buffer = lod0 ? section.Lod0 : section.Lod;
                double[] positions = null;
                double[] min = { double.MaxValue, double.MaxValue, double.MaxValue };
                double[] max = { double.MinValue, double.MinValue, double.MinValue };
                foreach (SubSection sub in section.SubSections)
                {
                    if (sub.Name != subName || (sub.Lod == 0) != lod0)
                    {
                        continue;
                    }
                    if (positions == null)
                    {
                        positions = Positions(section, buffer);
                    }
                    foreach (int index in sub.Indices)
                    {
                        if (IsRestart(sub, index))
                        {
                            continue;
                        }
                        for (int c = 0; c < 3; c++)
                        {
                            min[c] = Math.Min(min[c], positions[(index * 3) + c]);
                            max[c] = Math.Max(max[c], positions[(index * 3) + c]);
                        }
                    }
                }
                if (positions != null && min[0] <= max[0])
                {
                    for (int c = 0; c < 3; c++)
                    {
                        center[c] = (min[c] + max[c]) / 2;
                        half[c] = (max[c] - min[c]) / 2;
                    }
                    return true;
                }
            }
            return false;
        }

        // Transforms only the vertices of the named subsections (one material of a part, all LODs).
        // Vertices they share with other subsections are duplicated first, so the other materials stay
        // where they are. Bounds and centroid are refitted. Returns the number of vertices moved.
        public int TransformSubSections(string sectionName, string subName, Microsoft.Xna.Framework.Matrix m)
        {
            Section section = this.Find(sectionName);
            if (section == null)
            {
                return 0;
            }
            Microsoft.Xna.Framework.Quaternion rotation;
            Microsoft.Xna.Framework.Matrix normalMatrix;
            FrameTransform(m, out rotation, out normalMatrix);
            double[] lod = Positions(section, section.Lod);
            double[] lod0 = Positions(section, section.Lod0);
            int moved = 0;
            lod = TransformMaterial(section, section.Lod, section.DamageLod, false, lod, subName, m, rotation, normalMatrix, ref moved);
            lod0 = TransformMaterial(section, section.Lod0, section.DamageLod0, true, lod0, subName, m, rotation, normalMatrix, ref moved);
            if (moved > 0)
            {
                Reencode(section, lod, lod0);
                CheckIndexRange(section);
            }
            return moved;
        }

        private static double[] TransformMaterial(Section section, VertexBuffer buffer, Table damage, bool lod0, double[] positions, string subName,
            Microsoft.Xna.Framework.Matrix m, Microsoft.Xna.Framework.Quaternion rotation, Microsoft.Xna.Framework.Matrix normalMatrix, ref int moved)
        {
            if (buffer.Count == 0)
            {
                return positions;
            }
            bool[] mine = new bool[buffer.Count];
            bool[] others = new bool[buffer.Count];
            foreach (SubSection sub in section.SubSections)
            {
                if ((sub.Lod == 0) != lod0)
                {
                    continue;
                }
                bool[] marks = (sub.Name == subName) ? mine : others;
                foreach (int index in sub.Indices)
                {
                    if (!IsRestart(sub, index))
                    {
                        marks[index] = true;
                    }
                }
            }

            // Shared vertices: the material gets its own copies (appended, damage word copied).
            bool hasDamage = damage.Count == buffer.Count && damage.Stride == 4;
            Dictionary<int, int> copies = new Dictionary<int, int>();
            List<double> newPositions = new List<double>(positions);
            MemoryStream data = new MemoryStream();
            data.Write(buffer.Data, 0, buffer.Data.Length);
            MemoryStream words = new MemoryStream();
            words.Write(damage.Data, 0, damage.Data.Length);
            int count = buffer.Count;
            for (int i = 0; i < buffer.Count; i++)
            {
                if (mine[i] && others[i])
                {
                    copies[i] = count++;
                    data.Write(buffer.Data, i * buffer.Stride, buffer.Stride);
                    if (hasDamage)
                    {
                        words.Write(damage.Data, i * 4, 4);
                    }
                    newPositions.Add(positions[i * 3]);
                    newPositions.Add(positions[(i * 3) + 1]);
                    newPositions.Add(positions[(i * 3) + 2]);
                }
            }
            if (copies.Count > 0)
            {
                foreach (SubSection sub in section.SubSections)
                {
                    if ((sub.Lod == 0) != lod0 || sub.Name != subName)
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

            // Which vertices move: the material's own ones (originals not shared, plus the copies).
            bool[] move = new bool[count];
            for (int i = 0; i < mine.Length; i++)
            {
                move[i] = mine[i] && !others[i];
            }
            foreach (int copy in copies.Values)
            {
                move[copy] = true;
            }
            double[] result = newPositions.ToArray();
            for (int i = 0; i < count; i++)
            {
                if (!move[i])
                {
                    continue;
                }
                Microsoft.Xna.Framework.Vector3 p = new Microsoft.Xna.Framework.Vector3((float) result[i * 3], (float) result[(i * 3) + 1], (float) result[(i * 3) + 2]);
                p = Microsoft.Xna.Framework.Vector3.Transform(p, m);
                result[i * 3] = p.X;
                result[(i * 3) + 1] = p.Y;
                result[(i * 3) + 2] = p.Z;
                TransformFrame(buffer.Data, (i * buffer.Stride) + 16, rotation, normalMatrix);
                moved++;
            }
            return result;
        }

        #endregion

        private delegate int AppendVertex(Section owner, VertexBuffer buffer, double[] ownerPositions, int vertex, SubSection uvFrom, float[] uvTo);

        private static void SetDamage(Table table, int vertices, MemoryStream words)
        {
            if (vertices == 0)
            {
                return;
            }
            table.Count = vertices;
            table.Stride = 4;
            table.Data = words.ToArray();
            if (table.Data.Length != vertices * 4)
            {
                throw new InvalidDataException("Damage table does not match the vertex count.");
            }
        }

        // Index width used by the target's subsections for that buffer (files use one width for all).
        private static int IndexSizeOf(Section target, bool lod0, int fallback)
        {
            foreach (SubSection sub in target.SubSections)
            {
                if ((sub.Lod == 0) == lod0)
                {
                    return sub.IndexSize;
                }
            }
            return fallback;
        }

        private static void CheckIndexRange(Section section)
        {
            foreach (SubSection sub in section.SubSections)
            {
                VertexBuffer buffer = BufferOf(section, sub);
                foreach (int index in sub.Indices)
                {
                    if (IsRestart(sub, index))
                    {
                        continue;
                    }
                    if (index < 0 || index >= buffer.Count)
                    {
                        throw new InvalidDataException(string.Format("{0}.{1}: index {2} outside the vertex buffer ({3}).", section.Name, sub.Name, index, buffer.Count));
                    }
                }
                if (sub.IndexSize == 2 && buffer.Count >= 0xFFFF)
                {
                    throw new InvalidDataException(string.Format("{0}: {1} vertices do not fit 16-bit indices.", section.Name, buffer.Count));
                }
            }
        }

        // Every subsection must be drawn exactly once, and every entry must point at a subsection.
        public void CheckDrawLists()
        {
            Dictionary<uint, int> seen = new Dictionary<uint, int>();
            foreach (uint[] list in this.DrawLists)
            {
                foreach (uint entry in list)
                {
                    int s = (int) (entry >> 16);
                    int k = (int) (entry & 0xFFFF);
                    if (s >= this.Sections.Count || k >= this.Sections[s].SubSections.Count)
                    {
                        throw new InvalidDataException(string.Format("Draw list entry {0:X8} points outside the model.", entry));
                    }
                    int count;
                    seen.TryGetValue(entry, out count);
                    seen[entry] = count + 1;
                }
            }
            for (int s = 0; s < this.Sections.Count; s++)
            {
                for (int k = 0; k < this.Sections[s].SubSections.Count; k++)
                {
                    int count;
                    seen.TryGetValue((uint) ((s << 16) | k), out count);
                    if (count != 1)
                    {
                        throw new InvalidDataException(string.Format("{0}.{1} appears {2} times in the draw lists.", this.Sections[s].Name, this.Sections[s].SubSections[k].Name, count));
                    }
                }
            }
        }

        #endregion
    }
}
