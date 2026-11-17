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
    using Microsoft.Xna.Framework.Graphics;
    using Microsoft.Xna.Framework.Graphics.PackedVector;
    using System;
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Sequential)]
    public struct ForzaVertex
    {
        public Vector3 position;
        public Vector2 texture0;
        public Vector2 texture1;
        public Vector3 normal;
        public Color color;
        public static VertexElement[] VertexElements;
        public static int SizeInBytes;
        public ForzaVertex(ForzaVersion version, ForzaVertexType type, EndianStream stream, uint size)
        {
            float num;
            float num2;
            float num3;
            this.position = new Vector3();
            this.texture0 = new Vector2();
            this.texture1 = new Vector2();
            this.normal = new Vector3();
            this.color = Color.DarkRed;
            switch (type)
            {
                case ForzaVertexType.Car:
                    float num4;
                    switch (version)
                    {
                        case ForzaVersion.Two:
                            // 64: one wheel (por_550_56, ModelSettings 713); position, UVs and normal sit where the 60-byte
                            // vertex has them (checked 2026-11-13), the extra 4 bytes are skipped with the rest.
                            if (size != 60 && size != 64)
                            {
                                throw new NotSupportedException();
                            }
                            num = this.ConvertHalfToFloat(stream.ReadUInt16());
                            num2 = this.ConvertHalfToFloat(stream.ReadUInt16());
                            num3 = this.ConvertHalfToFloat(stream.ReadUInt16());
                            num4 = this.ConvertHalfToFloat(stream.ReadUInt16());
                            this.position = new Vector3(num * num4, num2 * num4, num3 * num4);
                            // FM2 UVs are sign + magnitude / 1310.72 (docs/CARBIN_ALL_GAMES.md), raw values: ForzaCarSection
                            // turns them into the viewer's u = -value, v = 1 + value. Until 2026-11-17 they were read as half
                            // floats and "fixed" with * or / 12800, exact only below 1.5625 in size: larger UVs (grilles,
                            // rubber trim, lights, tiled glass) came out as garbage up to millions, and in the FM2 port and
                            // Copy Part they squeezed the whole part's UV range in FM4's 16-bit encoding.
                            this.texture0 = new Vector2(Fm2Uv(stream.ReadUInt16()), Fm2Uv(stream.ReadUInt16()));
                            this.texture1 = new Vector2(Fm2Uv(stream.ReadUInt16()), Fm2Uv(stream.ReadUInt16()));
                            this.normal = this.ToN3(stream.ReadUInt32());
                            stream.Position += 4L;
                            stream.Position += 4L;
                            stream.Position += 0x20L + (size - 60);
                            return;

                        case ForzaVersion.Three:
                            switch (size)
                            {
                                case 0x10:
                                    num = this.ConvertHalfToFloat(stream.ReadUInt16());
                                    num2 = this.ConvertHalfToFloat(stream.ReadUInt16());
                                    num3 = this.ConvertHalfToFloat(stream.ReadUInt16());
                                    num4 = this.ConvertHalfToFloat(stream.ReadUInt16());
                                    this.position = new Vector3(num * num4, num2 * num4, num3 * num4);
                                    this.texture0 = new Vector2(this.ConvertHalfToFloat(stream.ReadUInt16()), this.ConvertHalfToFloat(stream.ReadUInt16()));
                                    this.normal = this.ToN3(stream.ReadUInt32());
                                    return;

                                case 40:
                                    num = this.ConvertHalfToFloat(stream.ReadUInt16());
                                    num2 = this.ConvertHalfToFloat(stream.ReadUInt16());
                                    num3 = this.ConvertHalfToFloat(stream.ReadUInt16());
                                    num4 = this.ConvertHalfToFloat(stream.ReadUInt16());
                                    this.position = new Vector3(num * num4, num2 * num4, num3 * num4);
                                    this.texture0 = new Vector2(this.ConvertHalfToFloat(stream.ReadUInt16()), this.ConvertHalfToFloat(stream.ReadUInt16()));
                                    this.texture1 = new Vector2(this.ConvertHalfToFloat(stream.ReadUInt16()), this.ConvertHalfToFloat(stream.ReadUInt16()));
                                    this.normal = this.ToN3(stream.ReadUInt32());
                                    stream.Position += 0x10L;
                                    this.ToN3(stream.ReadUInt32());
                                    return;

                                case 0x2c:
                                    num = this.ConvertHalfToFloat(stream.ReadUInt16());
                                    num2 = this.ConvertHalfToFloat(stream.ReadUInt16());
                                    num3 = this.ConvertHalfToFloat(stream.ReadUInt16());
                                    num4 = this.ConvertHalfToFloat(stream.ReadUInt16());
                                    this.position = new Vector3(num * num4, num2 * num4, num3 * num4);
                                    this.texture0 = new Vector2(this.ConvertHalfToFloat(stream.ReadUInt16()), this.ConvertHalfToFloat(stream.ReadUInt16()));
                                    this.normal = this.ToN3(stream.ReadUInt32());
                                    stream.Position += 0x10L;
                                    this.ToN3(stream.ReadUInt32());
                                    stream.Position += 8L;
                                    return;
                            }
                            throw new NotSupportedException();

                        case ForzaVersion.Four:
                        {
                            if (size != 0x20)
                            {
                                throw new NotSupportedException();
                            }
                            num = this.ShortN(stream.ReadInt16());
                            num2 = this.ShortN(stream.ReadInt16());
                            num3 = this.ShortN(stream.ReadInt16());
                            num4 = this.ShortN(stream.ReadInt16());
                            this.position = new Vector3(num * num4, num2 * num4, num3 * num4);
                            this.texture0 = new Vector2(this.UShortN(stream.ReadUInt16()), this.UShortN(stream.ReadUInt16()));
                            this.texture1 = new Vector2(this.UShortN(stream.ReadUInt16()), this.UShortN(stream.ReadUInt16()));
                            Matrix matrix = Matrix.CreateFromQuaternion(new Quaternion(this.ShortN(stream.ReadInt16()), this.ShortN(stream.ReadInt16()), this.ShortN(stream.ReadInt16()), this.ShortN(stream.ReadInt16())));
                            this.normal = new Vector3(matrix.M11, matrix.M12, matrix.M13);
                            new Vector3(matrix.M21, matrix.M22, matrix.M23);
                            stream.Position += 4L;
                            stream.Position += 4L;
                            return;
                        }
                        case ForzaVersion.Horizon:
                        {
                            if (size != 0x1c)
                            {
                                throw new NotSupportedException();
                            }
                            num = this.ShortN(stream.ReadInt16());
                            num2 = this.ShortN(stream.ReadInt16());
                            num3 = this.ShortN(stream.ReadInt16());
                            num4 = this.ShortN(stream.ReadInt16());
                            this.position = new Vector3(num * num4, num2 * num4, num3 * num4);
                            this.texture0 = new Vector2(this.UShortN(stream.ReadUInt16()), this.UShortN(stream.ReadUInt16()));
                            this.texture1 = new Vector2(this.UShortN(stream.ReadUInt16()), this.UShortN(stream.ReadUInt16()));
                            Matrix matrix2 = Matrix.CreateFromQuaternion(new Quaternion(this.ShortN(stream.ReadInt16()), this.ShortN(stream.ReadInt16()), this.ShortN(stream.ReadInt16()), this.ShortN(stream.ReadInt16())));
                            this.normal = new Vector3(matrix2.M11, matrix2.M12, matrix2.M13);
                            new Vector3(matrix2.M21, matrix2.M22, matrix2.M23);
                            stream.Position += 4L;
                            return;
                        }
                    }
                    throw new NotSupportedException();

                case ForzaVertexType.Track:
                {
                    uint num9 = size;
                    if (num9 > 0x18)
                    {
                        switch (num9)
                        {
                            case 0x1c:
                                num = stream.ReadSingle();
                                num2 = stream.ReadSingle();
                                num3 = stream.ReadSingle();
                                this.position = new Vector3(num, num2, num3);
                                this.normal = this.GetNormalized101010(stream.ReadUInt32());
                                this.texture0 = new Vector2(this.UShortN(stream.ReadUInt16()), this.UShortN(stream.ReadUInt16()));
                                stream.Position += 8L;
                                return;

                            case 0x20:
                                num = stream.ReadSingle();
                                num2 = stream.ReadSingle();
                                num3 = stream.ReadSingle();
                                this.position = new Vector3(num, num2, num3);
                                this.normal = this.GetNormalized101010(stream.ReadUInt32());
                                this.texture0 = new Vector2(this.UShortN(stream.ReadUInt16()), this.UShortN(stream.ReadUInt16()));
                                this.texture1 = new Vector2(this.UShortN(stream.ReadUInt16()), this.UShortN(stream.ReadUInt16()));
                                stream.Position += 8L;
                                return;

                            case 0x24:
                                num = stream.ReadSingle();
                                num2 = stream.ReadSingle();
                                num3 = stream.ReadSingle();
                                this.position = new Vector3(num, num2, num3);
                                this.normal = this.GetNormalized101010(stream.ReadUInt32());
                                stream.Position += 4L;
                                this.texture0 = new Vector2(this.UShortN(stream.ReadUInt16()), this.UShortN(stream.ReadUInt16()));
                                this.texture1 = new Vector2(this.UShortN(stream.ReadUInt16()), this.UShortN(stream.ReadUInt16()));
                                stream.Position += 8L;
                                return;

                            case 40:
                                num = stream.ReadSingle();
                                num2 = stream.ReadSingle();
                                num3 = stream.ReadSingle();
                                this.position = new Vector3(num, num2, num3);
                                this.normal = this.GetNormalized101010(stream.ReadUInt32());
                                this.texture0 = new Vector2(this.UShortN(stream.ReadUInt16()), this.UShortN(stream.ReadUInt16()));
                                this.texture1 = new Vector2(this.UShortN(stream.ReadUInt16()), this.UShortN(stream.ReadUInt16()));
                                stream.Position += 0x10L;
                                return;
                        }
                        break;
                    }
                    if (num9 > 0x10)
                    {
                        switch (num9)
                        {
                            case 20:
                                num = stream.ReadSingle();
                                num2 = stream.ReadSingle();
                                num3 = stream.ReadSingle();
                                this.position = new Vector3(num, num2, num3);
                                this.normal = this.GetNormalized101010(stream.ReadUInt32());
                                this.texture0 = new Vector2(this.UShortN(stream.ReadUInt16()), this.UShortN(stream.ReadUInt16()));
                                return;

                            case 0x18:
                                num = stream.ReadSingle();
                                num2 = stream.ReadSingle();
                                num3 = stream.ReadSingle();
                                this.position = new Vector3(num, num2, num3);
                                this.normal = this.GetNormalized101010(stream.ReadUInt32());
                                this.texture0 = new Vector2(this.UShortN(stream.ReadUInt16()), this.UShortN(stream.ReadUInt16()));
                                this.texture1 = new Vector2(this.UShortN(stream.ReadUInt16()), this.UShortN(stream.ReadUInt16()));
                                return;
                        }
                        break;
                    }
                    switch (num9)
                    {
                        case 12:
                            num = stream.ReadSingle();
                            num2 = stream.ReadSingle();
                            num3 = stream.ReadSingle();
                            this.position = new Vector3(num, num2, num3);
                            return;

                        case 0x10:
                            num = stream.ReadSingle();
                            num2 = stream.ReadSingle();
                            num3 = stream.ReadSingle();
                            this.position = new Vector3(num, num2, num3);
                            this.normal = this.GetNormalized101010(stream.ReadUInt32());
                            return;
                    }
                    break;
                }
                default:
                    throw new NotSupportedException();
            }
            throw new NotSupportedException();
        }

        private float ShortN(short value)
        {
            return (((float) value) / 32767f);
        }

        private float UShortN(ushort value)
        {
            return (((float) value) / 65535f);
        }

        private Vector3 GetNormalized101010(uint packedValue)
        {
            Normalized101010 normalized = new Normalized101010 {
                PackedValue = packedValue
            };
            return normalized.ToVector3();
        }

        private unsafe float ConvertHalfToFloat(uint value)
        {
            uint num = 0;
            uint num2 = 0;
            num = value & 0x3ff;
            if ((value & 0x7c00) != 0)
            {
                num2 = (value >> 10) & 0x1f;
            }
            else if (num != 0)
            {
                num2 = 1;
                do
                {
                    num2--;
                    num = num << 1;
                }
                while ((num & 0x400) == 0);
                num &= 0x3ff;
            }
            else
            {
                num2 = 0xffffff90;
            }
            uint num3 = ((uint) (((value & 0x8000) << 0x10) | ((num2 + 0x70) << 0x17))) | (num << 13);
            return *(((float*) &num3));
        }

        // FM2 UV value: bit 15 sign, bits 0-14 a SIGNED 15-bit magnitude, / 1310.72 (2^24 / 12800). The magnitude's own
        // sign (2026-11-17): 0x7FEA is -22, not +32746; read unsigned, such vertices sat near +-25 next to neighbours near 0
        // and smeared carbon fibre on the FM2 Sparco Evo's hood and trunk. Over the 310 FM2 cars (23.9M triangles,
        // fm4dev\ST03\fm3\fm2span.exe) the signed read leaves no value above 12.5 (288764 before) and cuts triangles
        // with a UV span above 2 from 64804 to 39954 (Sparco Evo 427 -> 0). Values below 12.5 are unchanged.
        public static float Fm2Uv(ushort raw)
        {
            int magnitude = raw & 0x7FFF;
            if (magnitude >= 0x4000)
            {
                magnitude -= 0x8000;
            }
            float value = magnitude / 1310.72f;
            return ((raw & 0x8000) != 0) ? -value : value;
        }

        private Vector3 ToN3(uint u)
        {
            Vector3 zero = Vector3.Zero;
            uint[] numArray3 = new uint[2];
            numArray3[1] = 0xfffff800;
            uint[] numArray = numArray3;
            uint[] numArray4 = new uint[2];
            numArray4[1] = 0xfffffc00;
            uint[] numArray2 = numArray4;
            uint num = u & 0x7ff;
            zero.X = ((float) ((short) (num | numArray[num >> 10]))) / 1023f;
            num = (u >> 11) & 0x7ff;
            zero.Y = ((float) ((short) (num | numArray[num >> 10]))) / 1023f;
            num = (u >> 0x16) & 0x3ff;
            zero.Z = ((float) ((short) (num | numArray2[num >> 9]))) / 511f;
            return zero;
        }

        static ForzaVertex()
        {
            VertexElements = new VertexElement[] { new VertexElement(0, 0, VertexElementFormat.Vector3, VertexElementMethod.Default, VertexElementUsage.Position, 0), new VertexElement(0, 12, VertexElementFormat.Vector2, VertexElementMethod.Default, VertexElementUsage.TextureCoordinate, 0), new VertexElement(0, 20, VertexElementFormat.Vector2, VertexElementMethod.Default, VertexElementUsage.TextureCoordinate, 1), new VertexElement(0, 0x1c, VertexElementFormat.Vector3, VertexElementMethod.Default, VertexElementUsage.Normal, 0), new VertexElement(0, 40, VertexElementFormat.Color, VertexElementMethod.Default, VertexElementUsage.Color, 0) };
            SizeInBytes = 0x2c;
        }
    }
}

