namespace ForzaStudio
{
    using System;
    using System.IO;

    // FM4 -> FM3 textures: the inverse of CarPort.ConvertMotorsport3Texture (FM3 sRGB linear-flagged DXT -> FM4 gamma-flagged AS_16).
    // FM4 colour textures hold the colours re-encoded for the Xbox 360 gamma curve and carry the gamma flag; FM3 holds sRGB values
    // with no flag. The curve is undone on the DXT colour endpoints of every level (alpha and indices untouched), the flag and the
    // AS_16 format go. FM4's DXN normal maps become DXT1 as FM3 has them (red = x, green = y, blue = z, measured on leather2_nrm).
    public static class Fm3Textures
    {
        public static byte[] FromMotorsport4(byte[] data, string name)
        {
            string file = Path.GetFileNameWithoutExtension(name).ToLowerInvariant();
            if (data.Length < XdsTexture.HeaderSize)
            {
                return data;
            }
            XdsTexture texture = new XdsTexture(data);
            if (texture.Codec == DxtFormat.Dxn)
            {
                return NormalMapToDxt1(texture);
            }
            if (!texture.IsGamma || file.Contains("_nrm") || file.Contains("normal"))
            {
                return data;
            }
            DxtFormat codec = texture.Codec;
            for (int level = 0; level < texture.LevelCount; level++)
            {
                byte[] blocks = texture.ReadLevelBlocks(level);
                if (codec == DxtFormat.A8R8G8B8)
                {
                    for (int i = 0; i < blocks.Length; i += 4)
                    {
                        blocks[i] = Gamma360ToSrgb[blocks[i]];
                        blocks[i + 1] = Gamma360ToSrgb[blocks[i + 1]];
                        blocks[i + 2] = Gamma360ToSrgb[blocks[i + 2]];
                    }
                }
                else if (codec == DxtFormat.Dxt1 || codec == DxtFormat.Dxt3 || codec == DxtFormat.Dxt5)
                {
                    int size = texture.BytesPerBlock;
                    for (int offset = size - 8; offset < blocks.Length; offset += size)
                    {
                        ConvertColorEndpoints(blocks, offset, codec == DxtFormat.Dxt1);
                    }
                }
                texture.WriteLevelBlocks(level, blocks);
            }
            byte[] result = texture.ToBytes();
            result[0x1F] = (byte) (result[0x1F] & 0x03);   // no gamma sign
            int format = result[0x23] & 0x3F;
            if (format == 0x33 || format == 0x35)
            {
                result[0x23] = (byte) ((result[0x23] & 0xC0) | (format - 0x21));   // AS_16 DXT1 / DXT4_5 -> plain
            }
            return result;
        }

        // DXN (x in red, y in green) -> DXT1 with z rebuilt in blue, level by level.
        private static byte[] NormalMapToDxt1(XdsTexture source)
        {
            XdsTexture result = XdsTexture.Create(source.Width, source.Height, DxtFormat.Dxt1, false, source.LevelCount);
            for (int level = 0; level < source.LevelCount; level++)
            {
                XdsTexture.Level info = source.GetLevel(level);
                byte[] pixels = source.DecodeLevel(level);
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    double x = ((pixels[i + 2] / 255.0) * 2) - 1;
                    double y = ((pixels[i + 1] / 255.0) * 2) - 1;
                    double z = Math.Sqrt(Math.Max(0, 1 - (x * x) - (y * y)));
                    pixels[i] = (byte) Math.Max(0, Math.Min(255, Math.Round(((z * 0.5) + 0.5) * 255)));
                    pixels[i + 3] = 255;
                }
                result.WriteLevelBlocks(level, EncodeDxt1(pixels, info.Width, info.Height));
            }
            byte[] dxt1 = result.ToBytes();
            int dxtFormat = dxt1[0x23] & 0x3F;
            if (dxtFormat == 0x33)
            {
                dxt1[0x23] = (byte) ((dxt1[0x23] & 0xC0) | 0x12);   // Create made the gamma form
            }
            dxt1[0x1F] = (byte) (dxt1[0x1F] & 0x03);
            return dxt1;
        }

        // Bounding-box DXT1 encoder (BGRA in, blocks out in row order, little endian): enough for a smooth normal map.
        private static byte[] EncodeDxt1(byte[] bgra, int width, int height)
        {
            int bw = Math.Max(1, (width + 3) / 4);
            int bh = Math.Max(1, (height + 3) / 4);
            byte[] blocks = new byte[bw * bh * 8];
            for (int by = 0; by < bh; by++)
            {
                for (int bx = 0; bx < bw; bx++)
                {
                    int[,] px = new int[16, 3];
                    int[] lo = { 255, 255, 255 };
                    int[] hi = { 0, 0, 0 };
                    for (int k = 0; k < 16; k++)
                    {
                        int x = Math.Min(width - 1, (bx * 4) + (k & 3));
                        int y = Math.Min(height - 1, (by * 4) + (k >> 2));
                        int o = ((y * width) + x) * 4;
                        px[k, 0] = bgra[o + 2];
                        px[k, 1] = bgra[o + 1];
                        px[k, 2] = bgra[o];
                        for (int c = 0; c < 3; c++)
                        {
                            lo[c] = Math.Min(lo[c], px[k, c]);
                            hi[c] = Math.Max(hi[c], px[k, c]);
                        }
                    }
                    int c0 = Pack565(hi);
                    int c1 = Pack565(lo);
                    if (c0 == c1)
                    {
                        if (c0 < 0xFFFF) { c0++; } else { c1--; }
                    }
                    int[][] palette = new int[4][];
                    palette[0] = Unpack565(c0);
                    palette[1] = Unpack565(c1);
                    palette[2] = new int[] { ((2 * palette[0][0]) + palette[1][0]) / 3, ((2 * palette[0][1]) + palette[1][1]) / 3, ((2 * palette[0][2]) + palette[1][2]) / 3 };
                    palette[3] = new int[] { (palette[0][0] + (2 * palette[1][0])) / 3, (palette[0][1] + (2 * palette[1][1])) / 3, (palette[0][2] + (2 * palette[1][2])) / 3 };
                    uint indices = 0;
                    for (int k = 0; k < 16; k++)
                    {
                        int best = 0;
                        int bestDistance = int.MaxValue;
                        for (int p = 0; p < 4; p++)
                        {
                            int d = 0;
                            for (int c = 0; c < 3; c++)
                            {
                                int diff = px[k, c] - palette[p][c];
                                d += diff * diff;
                            }
                            if (d < bestDistance)
                            {
                                bestDistance = d;
                                best = p;
                            }
                        }
                        indices |= (uint) best << (k * 2);
                    }
                    int offset = ((by * bw) + bx) * 8;
                    blocks[offset] = (byte) c0;
                    blocks[offset + 1] = (byte) (c0 >> 8);
                    blocks[offset + 2] = (byte) c1;
                    blocks[offset + 3] = (byte) (c1 >> 8);
                    blocks[offset + 4] = (byte) indices;
                    blocks[offset + 5] = (byte) (indices >> 8);
                    blocks[offset + 6] = (byte) (indices >> 16);
                    blocks[offset + 7] = (byte) (indices >> 24);
                }
            }
            return blocks;
        }

        private static int Pack565(int[] rgb)
        {
            return (((rgb[0] * 31 + 127) / 255) << 11) | (((rgb[1] * 63 + 127) / 255) << 5) | ((rgb[2] * 31 + 127) / 255);
        }

        private static int[] Unpack565(int c)
        {
            int r = (c >> 11) & 0x1F;
            int g = (c >> 5) & 0x3F;
            int b = c & 0x1F;
            return new int[] { (r << 3) | (r >> 2), (g << 2) | (g >> 4), (b << 3) | (b >> 2) };
        }

        // The two 565 endpoints of a DXT colour block through Gamma360ToSrgb; same block rules as CarPort.ConvertColorEndpoints.
        private static void ConvertColorEndpoints(byte[] blocks, int offset, bool dxt1)
        {
            int c0 = blocks[offset] | (blocks[offset + 1] << 8);
            int c1 = blocks[offset + 2] | (blocks[offset + 3] << 8);
            uint indices = (uint) (blocks[offset + 4] | (blocks[offset + 5] << 8) | (blocks[offset + 6] << 16) | (blocks[offset + 7] << 24));
            if ((!dxt1 || c0 > c1) && ConvertSmoothBlock(blocks, offset, c0, c1, indices))
            {
                return;
            }
            int n0 = ConvertEndpoint(c0);
            int n1 = ConvertEndpoint(c1);
            if (dxt1)
            {
                bool four = c0 > c1;
                if (four && n0 == n1)
                {
                    indices = 0;
                }
                else if ((four && n0 < n1) || (!four && n0 > n1))
                {
                    int swap = n0;
                    n0 = n1;
                    n1 = swap;
                    uint swapped = 0;
                    for (int i = 0; i < 16; i++)
                    {
                        uint index = (indices >> (i * 2)) & 3;
                        index = four ? (index ^ 1) : ((index < 2) ? (index ^ 1) : index);
                        swapped |= index << (i * 2);
                    }
                    indices = swapped;
                }
            }
            blocks[offset] = (byte) n0;
            blocks[offset + 1] = (byte) (n0 >> 8);
            blocks[offset + 2] = (byte) n1;
            blocks[offset + 3] = (byte) (n1 >> 8);
            blocks[offset + 4] = (byte) indices;
            blocks[offset + 5] = (byte) (indices >> 8);
            blocks[offset + 6] = (byte) (indices >> 16);
            blocks[offset + 7] = (byte) (indices >> 24);
        }

        // Low-contrast blocks (dark plastic, cloth, flat swatches): moving each 565 endpoint alone to the nearest code gives every
        // block of a flat dark area the same 5/6-bit rounding error, a purple cast (R and B on 8 steps, G on 4). The block is
        // decoded, its 16 colours go through the curve, and the endpoint pair whose palette fits them best is searched among the
        // few codes around them, with a penalty on hue so the pair that stays neutral wins. Blocks with more contrast keep the
        // endpoint mapping (their rounding is lost in the contrast).
        private const int SmoothRange = 12;
        private static readonly System.Collections.Generic.Dictionary<int, int[]> FlatBlocks = new System.Collections.Generic.Dictionary<int, int[]>();

        private static bool ConvertSmoothBlock(byte[] blocks, int offset, int c0, int c1, uint indices)
        {
            int[][] old = Palette(c0, c1);
            int[,] target = new int[16, 3];
            int[] lo = { 255, 255, 255 };
            int[] hi = { 0, 0, 0 };
            for (int k = 0; k < 16; k++)
            {
                int[] color = old[(indices >> (k * 2)) & 3];
                for (int c = 0; c < 3; c++)
                {
                    int value = Gamma360ToSrgb[color[c]];
                    target[k, c] = value;
                    lo[c] = Math.Min(lo[c], value);
                    hi[c] = Math.Max(hi[c], value);
                }
            }
            for (int c = 0; c < 3; c++)
            {
                if (hi[c] - lo[c] > SmoothRange)
                {
                    return false;
                }
            }
            bool flat = lo[0] == hi[0] && lo[1] == hi[1] && lo[2] == hi[2];
            int key = lo[0] | (lo[1] << 8) | (lo[2] << 16);
            int[] best;
            if (flat && FlatBlocks.TryGetValue(key, out best))
            {
                WriteColorBlock(blocks, offset, best[0], best[1], (uint) best[2]);
                return true;
            }
            int[] reds = Near(5, lo[0], hi[0], 3);
            int[] greens = Near(6, lo[1], hi[1], 4);
            int[] blues = Near(5, lo[2], hi[2], 3);
            int[] codes = new int[reds.Length * greens.Length * blues.Length];
            int count = 0;
            foreach (int r in reds)
            {
                foreach (int g in greens)
                {
                    foreach (int b in blues)
                    {
                        codes[count++] = (r << 11) | (g << 5) | b;
                    }
                }
            }
            long bestError = long.MaxValue;
            best = new int[] { c0, c1, (int) indices };
            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j <= i; j++)
                {
                    int a = Math.Max(codes[i], codes[j]);
                    int b = Math.Min(codes[i], codes[j]);
                    int[][] palette = Palette(a, b);
                    long total = 0;
                    uint chosen = 0;
                    for (int k = 0; k < 16 && total < bestError; k++)
                    {
                        long pixelBest = long.MaxValue;
                        int pick = 0;
                        for (int p = 0; p < 4; p++)
                        {
                            long dr = target[k, 0] - palette[p][0];
                            long dg = target[k, 1] - palette[p][1];
                            long db = target[k, 2] - palette[p][2];
                            long error = (dr * dr) + (dg * dg) + (db * db) + (4 * (((dr - dg) * (dr - dg)) + ((db - dg) * (db - dg))));
                            if (error < pixelBest)
                            {
                                pixelBest = error;
                                pick = p;
                            }
                        }
                        total += pixelBest;
                        chosen |= (uint) pick << (k * 2);
                    }
                    if (total < bestError)
                    {
                        bestError = total;
                        best = new int[] { a, b, (int) chosen };
                    }
                }
            }
            if (best[0] == best[1])
            {
                best[2] = 0;   // DXT1 reads c0 == c1 as the 3-colour form: index 0 is the one that always means c0
            }
            if (flat)
            {
                FlatBlocks[key] = best;
            }
            WriteColorBlock(blocks, offset, best[0], best[1], (uint) best[2]);
            return true;
        }

        private static int[][] Palette(int c0, int c1)
        {
            int[] a = Unpack565(c0);
            int[] b = Unpack565(c1);
            return new int[][]
            {
                a,
                b,
                new int[] { ((2 * a[0]) + b[0]) / 3, ((2 * a[1]) + b[1]) / 3, ((2 * a[2]) + b[2]) / 3 },
                new int[] { (a[0] + (2 * b[0])) / 3, (a[1] + (2 * b[1])) / 3, (a[2] + (2 * b[2])) / 3 }
            };
        }

        // The codes of that width whose 8-bit value is nearest to the span of a channel.
        private static int[] Near(int bits, int low, int high, int take)
        {
            int count = 1 << bits;
            double middle = (low + high) / 2.0;
            System.Collections.Generic.List<int> codes = new System.Collections.Generic.List<int>();
            for (int code = 0; code < count; code++)
            {
                codes.Add(code);
            }
            codes.Sort(delegate(int x, int y)
            {
                double dx = Math.Abs(((x << (8 - bits)) | (x >> ((2 * bits) - 8))) - middle);
                double dy = Math.Abs(((y << (8 - bits)) | (y >> ((2 * bits) - 8))) - middle);
                return dx.CompareTo(dy);
            });
            return codes.GetRange(0, take).ToArray();
        }

        private static void WriteColorBlock(byte[] blocks, int offset, int c0, int c1, uint indices)
        {
            blocks[offset] = (byte) c0;
            blocks[offset + 1] = (byte) (c0 >> 8);
            blocks[offset + 2] = (byte) c1;
            blocks[offset + 3] = (byte) (c1 >> 8);
            blocks[offset + 4] = (byte) indices;
            blocks[offset + 5] = (byte) (indices >> 8);
            blocks[offset + 6] = (byte) (indices >> 16);
            blocks[offset + 7] = (byte) (indices >> 24);
        }

        private static int ConvertEndpoint(int c)
        {
            return (Endpoint5[(c >> 11) & 0x1F] << 11) | (Endpoint6[(c >> 5) & 0x3F] << 5) | Endpoint5[c & 0x1F];
        }

        // Xbox 360 gamma byte -> sRGB byte: the inverse of CarPort's SrgbToGamma360 (PWL curve 4l | 64+2(l-16) | 96+(l-32) | 192+(l-128)63/127),
        // then linear to sRGB.
        private static readonly byte[] Gamma360ToSrgb = BuildGamma360ToSrgb();
        private static readonly int[] Endpoint5 = BuildEndpointMap(5);
        private static readonly int[] Endpoint6 = BuildEndpointMap(6);

        private static byte[] BuildGamma360ToSrgb()
        {
            byte[] map = new byte[256];
            for (int g = 0; g < 256; g++)
            {
                double l = (g < 64) ? g / 4.0 : (g < 96) ? 16 + ((g - 64) / 2.0) : (g < 192) ? 32 + (g - 96) : 128 + ((g - 192) * 127.0 / 63.0);
                double c = Math.Min(1, l / 255.0);
                double s = (c <= 0.0031308) ? c * 12.92 : (1.055 * Math.Pow(c, 1 / 2.4)) - 0.055;
                map[g] = (byte) Math.Max(0, Math.Min(255, (int) Math.Round(s * 255)));
            }
            return map;
        }

        private static int[] BuildEndpointMap(int bits)
        {
            int count = 1 << bits;
            int[] map = new int[count];
            for (int code = 0; code < count; code++)
            {
                int target = Gamma360ToSrgb[(code << (8 - bits)) | (code >> ((2 * bits) - 8))];
                int best = 0;
                for (int other = 1; other < count; other++)
                {
                    int expanded = (other << (8 - bits)) | (other >> ((2 * bits) - 8));
                    int bestExpanded = (best << (8 - bits)) | (best >> ((2 * bits) - 8));
                    if (Math.Abs(expanded - target) < Math.Abs(bestExpanded - target))
                    {
                        best = other;
                    }
                }
                map[code] = best;
            }
            return map;
        }
    }
}
