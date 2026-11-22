/*
    This is applicate to all source code of all project
    
    Forza Studio: Forza Motorsport resource extraction tool.

    Copyright (C) 2012 Mike Davis (http://codeescape.com/)

    Permission is hereby granted, free of charge, to any person 
    obtaining a copy of this software and associated documentation
    files (the "Software"), to deal in the Software without restriction,
    including without limitation the rights to use, copy, modify, merge,
    publish, distribute, sublicense, and/or sell copies of the Software,
    and to permit persons to whom the Software is furnished to do so,
    subject to the following conditions:

    The above copyright notice and this permission notice shall be included 
    in all copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
    OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
    THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
    FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
    DEALINGS IN THE SOFTWARE.
*/

namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    public class ForzaCarBin : ForzaResource
    {
        public static int bodyIBoffset;
        public static int bodyVertexCount;
        public static uint bodyVertexSize;
        public static ForzaVertex[] bodyVertices;
        public static bool f2ibend;
        public static int glassIBoffset;
        public static int glassVertexCount;
        public static uint glassVertexSize;
        public static ForzaVertex[] glassVertices;
        public ForzaCarSection[] Sections;
        public EndianStream Stream;
        public string tempName;
        public uint TypeId;

        public ForzaCarBin(string filePath) : base(filePath, (string) null)
        {
            this.Initialize();
        }

        public ForzaCarBin(string archivePath, string filePath) : base(filePath, archivePath)
        {
            this.Initialize();
        }

        public ForzaCarBin(string filePath, byte[] data) : base(filePath, data)
        {
            this.Initialize();
        }

        public override void Dispose()
        {
            if (this.Stream != null)
            {
                this.Stream.Dispose();
            }
            this.Sections = null;
        }

        ~ForzaCarBin()
        {
            this.Dispose();
        }

        private ForzaVersion GetVersion(System.IO.Stream stream)
        {
            long position = stream.Position;
            ForzaVersion unknown = ForzaVersion.Unknown;
            if (this.Stream.Length > 0x200L)
            {
                this.Stream.Position = 0L;
                uint num2 = this.Stream.ReadUInt32();
                uint num3 = this.Stream.ReadUInt32();
                stream.Seek(0x48L, SeekOrigin.Begin);
                uint num4 = this.Stream.ReadUInt32();
                stream.Seek(0x70L, SeekOrigin.Begin);
                uint num5 = this.Stream.ReadUInt32();
                stream.Seek(260L, SeekOrigin.Begin);
                uint num6 = this.Stream.ReadUInt32();
                stream.Seek(340L, SeekOrigin.Begin);
                uint num7 = this.Stream.ReadUInt32();
                // FM2 ModelSettings version 714 (0x2CA), or 713 on one wheel (por_550_56, same layout: Fm2CarbinModel).
                if (((num2 == 1) && (num3 == 0x2ca || num3 == 0x2c9)) || ((num2 == 2) && (num6 == 0x2ca || num6 == 0x2c9)))
                {
                    unknown = ForzaVersion.Two;
                }
                else if ((((num2 == 1) && (num3 == 0x10)) || ((num2 == 2) && (num7 == 0x10))) || ((num2 == 3) && (num4 == 0x10)))
                {
                    unknown = ForzaVersion.Four;
                }
                else if (((num2 == 1) && (num3 == 0x11)) || ((num2 == 5) || ((num2 == 3) && (num4 == 0x11))))
                {
                    unknown = ForzaVersion.Horizon;
                }
                else if (((num2 == 2) && ((num3 == 0) || (num3 == 1))) || ((num2 == 1) && (num5 == 0)))
                {
                    unknown = ForzaVersion.Three;
                }
            }
            // The word at 0x70 tested above lies in an FM3 body's wheel lighting: it is 0 on FM3's own files but not on every
            // ported one (FM2 -> FM3 cockpits, pack FM2_TEST 2026-11-22: "file not supported" in the viewport). The model
            // settings version (13 = FM3) after the header is the sure sign.
            if (unknown == ForzaVersion.Unknown && CarbinFormat.Identify(this.GetData()).Family == CarbinFamily.Motorsport3)
            {
                unknown = ForzaVersion.Three;
            }
            stream.Position = position;
            return unknown;
        }

        public void Initialize()
        {
            this.Stream = new EndianStream(base.GetDataStream(), EndianType.BigEndian);
            glassIBoffset = 0;
            bodyIBoffset = 0;
            switch ((base.Version = this.GetVersion(this.Stream)))
            {
                case ForzaVersion.Two:
                    this.TypeId = this.Stream.ReadUInt32();
                    uint settings = this.Stream.ReadUInt32();
                    if (settings != 0x2ca && settings != 0x2c9)
                    {
                        if (this.TypeId != 2)
                        {
                            if (this.TypeId == 1)
                            {
                                this.Stream.Position += 20L;
                            }
                            break;
                        }
                        this.Stream.Position += 0x100L;
                    }
                    break;

                case ForzaVersion.Three:
                {
                    this.TypeId = this.Stream.ReadUInt32();
                    if (this.Stream.ReadUInt32() != 13)
                    {
                        if (this.TypeId != 1)
                        {
                            if (this.TypeId == 2)
                            {
                                this.Stream.Position += 20L;
                            }
                        }
                        else
                        {
                            this.Stream.Position += 0x100L;
                        }
                    }
                    this.Stream.Position += 8L;
                    this.Stream.Seek((long) (this.Stream.ReadUInt32() * 140), SeekOrigin.Current);
                    this.Stream.Position += 0x1acL;
                    uint num11 = this.Stream.ReadUInt32() * 2;
                    for (int m = 0; m < num11; m++)
                    {
                        this.Stream.Seek((long) (this.Stream.ReadUInt32() * 4), SeekOrigin.Current);
                    }
                    this.Stream.Position += 4L;
                    this.Sections = new ForzaCarSection[this.Stream.ReadUInt32()];
                    for (int n = 0; n < this.Sections.Length; n++)
                    {
                        this.Sections[n] = new ForzaCarSection(this);
                    }
                    goto Label_0AF0;
                }
                case ForzaVersion.Four:
                {
                    this.TypeId = this.Stream.ReadUInt32();
                    if (this.TypeId != 1)
                    {
                        if (this.TypeId == 2)
                        {
                            this.Stream.Position += 0x15cL;
                            uint num14 = this.Stream.ReadUInt32();
                            this.Stream.Position += 4L;
                            this.Stream.Position += num14 * 140;
                            this.Stream.Position += 0x340L;
                            uint num15 = this.Stream.ReadUInt32() * 2;
                            for (int num16 = 0; num16 < num15; num16++)
                            {
                                this.Stream.Seek((long) (this.Stream.ReadUInt32() * 4), SeekOrigin.Current);
                            }
                            this.Stream.Position += 4L;
                        }
                        else if (this.TypeId == 3)
                        {
                            this.Stream.Position += 920L;
                            uint num17 = this.Stream.ReadUInt32() * 2;
                            for (int num18 = 0; num18 < num17; num18++)
                            {
                                this.Stream.Seek((long) (this.Stream.ReadUInt32() * 4), SeekOrigin.Current);
                            }
                            this.Stream.Position += 4L;
                        }
                    }
                    else
                    {
                        this.Stream.Position += 860L;
                        this.Stream.Seek((long) (this.Stream.ReadUInt32() * 4), SeekOrigin.Current);
                        this.Stream.Position += 8L;
                    }
                    uint num19 = this.Stream.ReadUInt32();
                    List<ForzaCarSection> list = new List<ForzaCarSection>();
                    for (int num20 = 0; num20 < num19; num20++)
                    {
                        try
                        {
                            list.Add(new ForzaCarSection(this));
                        }
                        catch (Exception)
                        {
                        }
                    }
                    this.Sections = new ForzaCarSection[list.Count];
                    for (int num21 = 0; num21 < this.Sections.Length; num21++)
                    {
                        this.Sections[num21] = list[num21];
                    }
                    // A header that declares no sections is a valid empty model (null caliper/drum).
                    if (this.Sections.Length == 0 && num19 != 0)
                    {
                        throw new Exception("analyze this!");
                    }
                    string name = base.Name;
                    goto Label_0AF0;
                }
                case ForzaVersion.Horizon:
                {
                    this.TypeId = this.Stream.ReadUInt32();
                    if (this.TypeId != 1)
                    {
                        if (this.TypeId == 5)
                        {
                            this.Stream.Position += 0x150L;
                            this.Stream.Seek((long) (this.Stream.ReadUInt32() * 0x20), SeekOrigin.Current);
                            this.Stream.Position += 12L;
                            uint num22 = this.Stream.ReadUInt32();
                            this.Stream.Position += 4L;
                            this.Stream.Position += num22 * 140;
                            this.Stream.Position += 0x340L;
                            uint num23 = this.Stream.ReadUInt32() * 2;
                            for (int num24 = 0; num24 < num23; num24++)
                            {
                                this.Stream.Seek((long) (this.Stream.ReadUInt32() * 4), SeekOrigin.Current);
                            }
                            this.Stream.Position += 4L;
                        }
                        else if (this.TypeId == 3)
                        {
                            this.Stream.Position += 920L;
                            uint num25 = this.Stream.ReadUInt32() * 2;
                            for (int num26 = 0; num26 < num25; num26++)
                            {
                                this.Stream.Seek((long) (this.Stream.ReadUInt32() * 4), SeekOrigin.Current);
                            }
                            this.Stream.Position += 4L;
                        }
                    }
                    else
                    {
                        this.Stream.Position += 860L;
                        this.Stream.Seek((long) (this.Stream.ReadUInt32() * 4), SeekOrigin.Current);
                        this.Stream.Position += 8L;
                    }
                    uint num27 = this.Stream.ReadUInt32();
                    List<ForzaCarSection> list2 = new List<ForzaCarSection>();
                    for (int num28 = 0; num28 < num27; num28++)
                    {
                        try
                        {
                            list2.Add(new ForzaCarSection(this));
                        }
                        catch (Exception)
                        {
                        }
                    }
                    this.Sections = new ForzaCarSection[list2.Count];
                    for (int num29 = 0; num29 < this.Sections.Length; num29++)
                    {
                        this.Sections[num29] = list2[num29];
                    }
                    if (this.Sections.Length == 0)
                    {
                        throw new Exception("analyze this!");
                    }
                    string text2 = base.Name;
                    goto Label_0AF0;
                }
                default:
                    throw new NotSupportedException();
            }
            this.Stream.Position += 4L;
            this.Stream.Seek((long) (this.Stream.ReadUInt32() * 140), SeekOrigin.Current);
            this.Stream.Position += 0x6cL;
            uint num = this.Stream.ReadUInt32() * 2;
            for (int i = 0; i < num; i++)
            {
                this.Stream.Seek((long) (this.Stream.ReadUInt32() * 4), SeekOrigin.Current);
            }
            this.Stream.Position += 4L;
            this.Sections = new ForzaCarSection[this.Stream.ReadUInt32()];
            long position = this.Stream.Position;
            for (int j = 0; j < this.Sections.Length; j++)
            {
                uint num5 = this.Stream.ReadUInt32();
                if (((num5 != 0x2c9) && (num5 != 2)) && (num5 != 5))
                {
                    this.Stream.Position -= 4L;
                }
                this.Stream.Position += 0x24L;
                this.Stream.Position += 0x1cL;
                this.Stream.Seek((long) (this.Stream.ReadUInt32() * 0x10), SeekOrigin.Current);
                this.Stream.Position += 4L;
                this.Stream.Seek((long) (this.Stream.ReadUInt32() * 2), SeekOrigin.Current);
                this.Stream.Position += 12L;
                this.tempName = this.Stream.ReadASCII(this.Stream.ReadByte());
                this.Stream.Position += 4L;
                this.Stream.Position += 4L;
                this.Stream.Position += 4L;
                this.Stream.Position += 4L;
                this.Stream.Position += 4L;
                uint num6 = this.Stream.ReadUInt32();
                for (int num7 = 0; num7 < num6; num7++)
                {
                    this.Stream.Position += 8L;
                    this.tempName = this.Stream.ReadASCII(this.Stream.ReadInt32());
                    this.Stream.Position += 4L;
                    this.Stream.Position += 4L;
                    this.Stream.Position += 4L;
                    this.Stream.Seek((long) (this.Stream.ReadUInt32() * this.Stream.ReadUInt32()), SeekOrigin.Current);
                }
            }
            this.Stream.Position += 8L;
            glassVertexCount = this.Stream.ReadInt32();
            glassVertexSize = this.Stream.ReadUInt32();
            this.Stream.Position += 4L;
            if (glassVertexCount > 0)
            {
                glassVertices = new ForzaVertex[glassVertexCount];
                for (int num8 = 0; num8 < glassVertexCount; num8++)
                {
                    glassVertices[num8] = new ForzaVertex(ForzaVersion.Two, ForzaVertexType.Car, this.Stream, glassVertexSize);
                }
            }
            this.Stream.Position += 12L;
            bodyVertexCount = this.Stream.ReadInt32();
            bodyVertexSize = this.Stream.ReadUInt32();
            this.Stream.Position += 4L;
            if (bodyVertexCount > 0)
            {
                bodyVertices = new ForzaVertex[bodyVertexCount];
                for (int num9 = 0; num9 < bodyVertexCount; num9++)
                {
                    bodyVertices[num9] = new ForzaVertex(ForzaVersion.Two, ForzaVertexType.Car, this.Stream, bodyVertexSize);
                }
            }
            this.Stream.Position = position;
            for (int k = 0; k < this.Sections.Length; k++)
            {
                this.Sections[k] = new ForzaCarSection(this);
                if (ForzaCarSection.glassOffset > glassIBoffset)
                {
                    glassIBoffset = ForzaCarSection.glassOffset;
                }
                if (ForzaCarSection.bodyOffset > bodyIBoffset)
                {
                    bodyIBoffset = ForzaCarSection.bodyOffset;
                }
            }
        Label_0AF0:
            this.Stream.Dispose();
        }
    }
}

