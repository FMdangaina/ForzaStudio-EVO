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
    using Microsoft.Xna.Framework;
    using System;
    using System.IO;

    public class ForzaCarSection
    {
        public static int bodyOffset;
        public static int glassOffset;
        public static int IBoffset;
        public ForzaVertex[] lod0Vertices;
        public int lodVertexCount;
        public uint lodVertexSize;
        public ForzaVertex[] lodVertices;
        public string Name;
        public ForzaCarBin Parent;
        public int sectionMax;
        public EndianStream Stream;
        public ForzaCarSubSection[] SubSections;
        public uint vbTest1;
        public uint vbTest2;
        public int vertexCount;
        public uint vertexSize;

        public ForzaCarSection(ForzaCarBin car)
        {
            this.Parent = car;
            this.Stream = car.Stream;
            if ((this.Stream.Position + 100L) > this.Stream.Length)
            {
                throw new Exception();
            }
            uint num = this.Stream.ReadUInt32();
            if (((num != 0x2c9) && (num != 2)) && (num != 5))
            {
                this.Stream.Position -= 4L;
            }
            Vector3 vector = new Vector3(this.Stream.ReadSingle(), this.Stream.ReadSingle(), this.Stream.ReadSingle());
            Vector3 targetMin = new Vector3(this.Stream.ReadSingle(), this.Stream.ReadSingle(), this.Stream.ReadSingle());
            Vector3 targetMax = new Vector3(this.Stream.ReadSingle(), this.Stream.ReadSingle(), this.Stream.ReadSingle());
            this.Stream.Position += 0x1cL;
            this.Stream.Seek((long) (this.Stream.ReadUInt32() * 0x10), SeekOrigin.Current);
            this.Stream.Position += 4L;
            this.Stream.Seek((long) (this.Stream.ReadUInt32() * 2), SeekOrigin.Current);
            if (car.Version == ForzaVersion.Three)
            {
                this.Stream.Position += 8L;
            }
            else if (((car.Version == ForzaVersion.Two) || (car.Version == ForzaVersion.Four)) || (car.Version == ForzaVersion.Horizon))
            {
                this.Stream.Position += 4L;
                this.vbTest1 = this.Stream.ReadUInt32();
                this.vbTest2 = this.Stream.ReadUInt32();
            }
            this.Name = this.Stream.ReadASCII(this.Stream.ReadByte());
            this.Stream.Position += 4L;
            if (car.Version == ForzaVersion.Two)
            {
                this.Stream.Position += 4L;
            }
            this.lodVertexCount = this.Stream.ReadInt32();
            this.lodVertexSize = this.Stream.ReadUInt32();
            if (this.lodVertexSize == 0x1c)
            {
                this.Stream.Position += 8L;
            }
            if (this.lodVertexCount > 0)
            {
                this.lodVertices = new ForzaVertex[this.lodVertexCount];
                for (int j = 0; j < this.lodVertexCount; j++)
                {
                    this.lodVertices[j] = new ForzaVertex(car.Version, ForzaVertexType.Car, this.Stream, this.lodVertexSize);
                }
            }
            this.Stream.Position += 4L;
            if ((car.Version == ForzaVersion.Horizon) && (this.lodVertexSize != 0x1c))
            {
                this.Stream.Position += 4L;
            }
            IBoffset = 0;
            if ((car.Version == ForzaVersion.Two) && (this.vbTest1 == this.vbTest2))
            {
                IBoffset = ForzaCarBin.glassIBoffset;
            }
            else if ((car.Version == ForzaVersion.Two) && (this.vbTest1 == 0))
            {
                IBoffset = ForzaCarBin.bodyIBoffset;
            }
            uint num3 = this.Stream.ReadUInt32();
            this.SubSections = new ForzaCarSubSection[num3];
            this.sectionMax = 0;
            for (int i = 0; i < num3; i++)
            {
                this.SubSections[i] = new ForzaCarSubSection(this);
                if (ForzaCarSubSection.subSImax > this.sectionMax)
                {
                    this.sectionMax = ForzaCarSubSection.subSImax;
                }
            }
            glassOffset = 0;
            bodyOffset = 0;
            if ((car.Version == ForzaVersion.Two) && (this.vbTest1 == this.vbTest2))
            {
                this.lodVertexCount = ForzaCarBin.glassVertexCount;
                this.lodVertexSize = ForzaCarBin.glassVertexSize;
                this.lodVertices = ForzaCarBin.glassVertices;
                glassOffset = this.sectionMax + 1;
            }
            else if ((car.Version == ForzaVersion.Two) && (this.vbTest1 == 0))
            {
                this.lodVertexCount = ForzaCarBin.bodyVertexCount;
                this.lodVertexSize = ForzaCarBin.bodyVertexSize;
                this.lodVertices = ForzaCarBin.bodyVertices;
                bodyOffset = this.sectionMax + 1;
            }
            else
            {
                this.Stream.Position += 4L;
                this.vertexCount = this.Stream.ReadInt32();
                this.vertexSize = this.Stream.ReadUInt32();
                if (this.vertexSize == 0x1c)
                {
                    this.Stream.Position += 8L;
                }
                if (this.vertexCount > 0)
                {
                    this.lod0Vertices = new ForzaVertex[this.vertexCount];
                    for (int k = 0; k < this.vertexCount; k++)
                    {
                        this.lod0Vertices[k] = new ForzaVertex(car.Version, ForzaVertexType.Car, this.Stream, this.vertexSize);
                    }
                }
            }
            if (car.Version == ForzaVersion.Four)
            {
                this.Stream.Position += 9L;
                this.Stream.Seek((long) (this.Stream.ReadUInt32() * this.Stream.ReadUInt32()), SeekOrigin.Current);
                this.Stream.Position += 4L;
                this.Stream.Seek((long) (this.Stream.ReadUInt32() * this.Stream.ReadUInt32()), SeekOrigin.Current);
            }
            else if (car.Version == ForzaVersion.Horizon)
            {
                this.Stream.Position += 9L;
                if (this.lodVertexSize == 0x1c)
                {
                    this.Stream.Position += 4L;
                }
                this.Stream.Seek((long) (this.Stream.ReadUInt32() * this.Stream.ReadUInt32()), SeekOrigin.Current);
                this.Stream.Position += 8L;
                this.Stream.Seek((long) (this.Stream.ReadUInt32() * this.Stream.ReadUInt32()), SeekOrigin.Current);
                this.Stream.Position += 8L;
            }
            ForzaStudio.BoundingBox box = ForzaStudio.BoundingBox.FromVertices(this.lod0Vertices);
            ForzaStudio.BoundingBox box2 = ForzaStudio.BoundingBox.FromVertices(this.lodVertices);
            foreach (ForzaCarSubSection section in this.SubSections)
            {
                section.Vertices = Utilities.GenerateVertices((section.Lod == 0) ? this.lod0Vertices : this.lodVertices, ref section.Indices);
                for (int m = 0; m < section.Vertices.Length; m++)
                {
                    if (((targetMin.Length() > 0f) && (targetMax.Length() > 0f)) && (car.Version != ForzaVersion.Two))
                    {
                        if (section.Lod == 0)
                        {
                            section.Vertices[m].position = Utilities.CalculateBoundTargetValue(section.Vertices[m].position, box.Min, box.Max, targetMin, targetMax);
                        }
                        else
                        {
                            section.Vertices[m].position = Utilities.CalculateBoundTargetValue(section.Vertices[m].position, box2.Min, box2.Max, targetMin, targetMax);
                        }
                    }
                    section.Vertices[m].position += vector;
                    if (car.Version == ForzaVersion.Two)
                    {
                        // ForzaVertex.Fm2Uv values (offset-binary word / 1310.72) in the viewer's convention; the same
                        // numbers the old half * 12800 path gave below 1.5625, the right ones above it.
                        section.Vertices[m].texture0.X = 0f - section.Vertices[m].texture0.X;
                        section.Vertices[m].texture0.Y = 1f + section.Vertices[m].texture0.Y;
                        section.Vertices[m].texture1.X = 0f - section.Vertices[m].texture1.X;
                        section.Vertices[m].texture1.Y = 1f + section.Vertices[m].texture1.Y;
                    }
                    else
                    {
                        section.Vertices[m].texture0.X = (section.Vertices[m].texture0.X * section.XUVScale) + section.XUVOffset;
                        section.Vertices[m].texture0.Y = 1f - ((section.Vertices[m].texture0.Y * section.YUVScale) + section.YUVOffset);
                        section.Vertices[m].texture1.X = (section.Vertices[m].texture1.X * section.XUV2Scale) + section.XUV2Offset;
                        section.Vertices[m].texture1.Y = 1f - ((section.Vertices[m].texture1.Y * section.YUV2Scale) + section.YUV2Offset);
                    }
                }
                // A few FM2 meshes are triangle lists (primitive 4: 5 meshes in 5 cars, ST-01 2026-11-13), not strips: read as
                // strips they gave wrong faces and, in for_mustanggt_05 (skirtLd, LOD4), an IndexOutOfRange that kept the
                // car from opening. A list is already what the viewer draws.
                if (section.IndexType != IndexType.TriList)
                {
                    section.Indices = Utilities.GenerateTriangleList(section.Indices, section.FaceCount);
                }
            }
        }
    }
}

