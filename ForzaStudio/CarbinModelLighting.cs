namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading.Tasks;
    using Microsoft.Xna.Framework;

    // Baked light of FM4 / FM3 car vertices without a donor (user 2026-11-21: "voglio arrivare ad avere meno roba possibile
    // calcolata dal donor"). Study: E:\DEV\fm4dev\ST03\light34 (sh34, cmp24), docs/ST03_PART_IMPORT.md.
    // The 4 SNORM16 values (FM4: at +24 of the 32-byte vertex; FM3: at +20 of the 40-byte one) are the first 4 values
    // (bands 0-1) of the precomputed-radiance-transfer vector FM2 stores in 16 (Fm2CarbinModelLighting.cs): the same D3DX
    // signs and axes, stored = LightScale x 2T (an open vertex facing up: 0.82; FM2: 0.562).
    //  - A part that comes from FM2 (port, Copy Part): each vertex takes the values of the FM2 vertex at the same place,
    //    scaled (LightingFromMotorsport2): the source game's own data.
    //  - New geometry with no source values (OBJ import): computed on this car with the FM2 model, light sent back by
    //    the car and a black ground rectangle, both fitted on the car's own vertices before the import
    //    (BeginLighting / FinishLighting). The vertices the import left as they were keep the game's values.
    // Before, every new vertex got the level of an open panel facing its way (WriteTail): no shade in recesses.
    public partial class CarbinModel
    {
        // stored = LightScale x (2 x T): FM4 open vertices facing up 0.816-0.820, FM3 0.810, FM2 0.5615.
        internal const double LightScale = 1.457;

        // What BeginLighting keeps for FinishLighting.
        public sealed class LightingState
        {
            internal Fm2CarbinModel.LightModel Model;
            internal Dictionary<string, HashSet<string>> Kept = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        }

        // Where a vertex of this buffer holds the 4 values; -1: none (FH 28-byte vertices hold SNORM8, FM3 brakes none).
        private static int LightOffset(Section section, VertexBuffer buffer)
        {
            if (buffer == null || buffer.Count == 0)
            {
                return -1;
            }
            if (IsMotorsport3Layout(section, buffer))
            {
                return (buffer.Stride == 40) ? 20 : -1;
            }
            return (buffer.Stride == 32) ? 24 : -1;
        }

        // The vertex's values in 2 x T units, as 16 (the model's size; 4..15 stay 0).
        private static double[] ReadLight(VertexBuffer buffer, int v, int offset)
        {
            double[] l = new double[16];
            if (offset >= 0)
            {
                int o = (v * buffer.Stride) + offset;
                for (int k = 0; k < 4; k++)
                {
                    l[k] = (short) ((buffer.Data[o + (k * 2)] << 8) | buffer.Data[o + (k * 2) + 1]) / 32767.0 / LightScale;
                }
            }
            return l;
        }

        private static void WriteLight(VertexBuffer buffer, int v, int offset, double[] value)
        {
            int o = (v * buffer.Stride) + offset;
            for (int k = 0; k < 4; k++)
            {
                int s = (int) Math.Round(Math.Max(-1, Math.Min(1, value[k] * LightScale)) * 32767);
                buffer.Data[o + (k * 2)] = (byte) (s >> 8);
                buffer.Data[o + (k * 2) + 1] = (byte) s;
            }
        }

        // The LOD whose triangles hide the light: 1 in a main model, 0 in a lod0 / cockpit file.
        private uint LightSceneLod()
        {
            foreach (Section section in this.Sections)
            {
                foreach (SubSection sub in section.SubSections)
                {
                    if (sub.Lod == 1)
                    {
                        return 1;
                    }
                }
            }
            return 0;
        }

        private static bool LightSkipped(Section section)
        {
            // the main model holds one wheel at the origin (the game puts it at the four corners)
            return string.Equals(section.Name, "wheel", StringComparison.OrdinalIgnoreCase);
        }

        // The parts the game shows by default: alone in their group of upgrade variants, or the group's "a" part (else its
        // first part); never the wheel.
        private bool[] LightStock()
        {
            bool[] stock = new bool[this.Sections.Count];
            for (int p = 0; p < this.Sections.Count; p++)
            {
                if (LightSkipped(this.Sections[p]))
                {
                    continue;
                }
                string group = Fm2CarbinModel.VariantGroup(this.Sections[p].Name);
                int size = 0, first = -1, a = -1;
                for (int q = 0; q < this.Sections.Count; q++)
                {
                    if (Fm2CarbinModel.VariantGroup(this.Sections[q].Name) != group)
                    {
                        continue;
                    }
                    size++;
                    if (first < 0)
                    {
                        first = q;
                    }
                    if (this.Sections[q].Name == group + "a")
                    {
                        a = q;
                    }
                }
                stock[p] = size == 1 || p == ((a >= 0) ? a : first);
            }
            return stock;
        }

        private static Vector3 LightAt(double[] positions, int i)
        {
            return new Vector3((float) positions[i * 3], (float) positions[(i * 3) + 1], (float) positions[(i * 3) + 2]);
        }

        private static double[][] SectionLight(Section section, VertexBuffer buffer)
        {
            int offset = LightOffset(section, buffer);
            double[][] values = new double[buffer.Count][];
            for (int v = 0; v < buffer.Count; v++)
            {
                values[v] = ReadLight(buffer, v, offset);
            }
            return values;
        }

        // The section's opaque triangles of the scene LOD; returns the vertices they use.
        private static bool[] AddLightTriangles(Fm2CarbinModel.LightScene scene, Section section, uint lod, double[] positions, double[][] values)
        {
            int count = values.Length;
            bool[] used = new bool[count];
            foreach (SubSection sub in section.SubSections)
            {
                if (sub.Lod != lod || IsTransparentMaterial(sub.Name))
                {
                    continue;
                }
                foreach (int[] f in StripTriangles(sub))
                {
                    if (f[0] < 0 || f[1] < 0 || f[2] < 0 || f[0] >= count || f[1] >= count || f[2] >= count)
                    {
                        continue;
                    }
                    if (scene != null)
                    {
                        scene.Add(LightAt(positions, f[0]), LightAt(positions, f[1]), LightAt(positions, f[2]), values[f[0]], values[f[1]], values[f[2]]);
                    }
                    used[f[0]] = used[f[1]] = used[f[2]] = true;
                }
            }
            return used;
        }

        // The car as the game shows it: its stock parts; 'standIn' (not null) in the place of its group of variants, with
        // the values given (the part being computed).
        private Fm2CarbinModel.LightScene BuildLightScene(uint lod, Section standIn, double[] standInPositions, double[][] standInValues)
        {
            Fm2CarbinModel.LightScene scene = new Fm2CarbinModel.LightScene();
            bool[] stock = this.LightStock();
            string slot = (standIn != null) ? Fm2CarbinModel.VariantGroup(standIn.Name) : null;
            for (int p = 0; p < this.Sections.Count; p++)
            {
                Section section = this.Sections[p];
                if (section == standIn || !stock[p] || (slot != null && Fm2CarbinModel.VariantGroup(section.Name) == slot))
                {
                    continue;
                }
                VertexBuffer buffer = (lod == 0) ? section.Lod0 : section.Lod;
                if (buffer == null || buffer.Count == 0)
                {
                    continue;
                }
                AddLightTriangles(scene, section, lod, Positions(section, buffer), SectionLight(section, buffer));
            }
            if (standIn != null)
            {
                AddLightTriangles(scene, standIn, lod, standInPositions, standInValues);
            }
            return scene;
        }

        private static string LightKey(VertexBuffer buffer, int v)
        {
            // the tangent frame and what follows it: an import copies these bytes on the vertices it keeps
            return Convert.ToBase64String(buffer.Data, (v * buffer.Stride) + 16, buffer.Stride - 16);
        }

        // Before an OBJ import: rho and the ground of this car fitted on its own stored values, and which vertices each
        // part has now (those the import keeps are left alone by FinishLighting).
        public LightingState BeginLighting()
        {
            Fm2CarbinModel.LightSetup();
            LightingState state = new LightingState();
            uint lod = this.LightSceneLod();
            Fm2CarbinModel.LightScene scene = this.BuildLightScene(lod, null, null, null);
            List<Vector3> at = new List<Vector3>(), normals = new List<Vector3>();
            List<double[]> stored = new List<double[]>();
            bool[] stock = this.LightStock();
            for (int p = 0; p < this.Sections.Count; p++)
            {
                Section section = this.Sections[p];
                VertexBuffer buffer = (lod == 0) ? section.Lod0 : section.Lod;
                int offset = LightOffset(section, buffer);
                if (offset >= 0)
                {
                    HashSet<string> keys = new HashSet<string>();
                    for (int v = 0; v < buffer.Count; v++)
                    {
                        keys.Add(LightKey(buffer, v));
                    }
                    state.Kept[section.Name] = keys;
                }
                if (!stock[p] || offset < 0)
                {
                    continue;
                }
                double[] positions = Positions(section, buffer);
                double[][] values = SectionLight(section, buffer);
                bool[] used = AddLightTriangles(null, section, lod, positions, values);
                for (int v = 0; v < buffer.Count; v++)
                {
                    if (!used[v])
                    {
                        continue;
                    }
                    double[] l = values[v];
                    Vector3 n = LayoutNormal(section, buffer, v);
                    Vector3 position = LightAt(positions, v);
                    if ((l[0] == 0 && l[1] == 0 && l[2] == 0 && l[3] == 0) || n.LengthSquared() < 0.25f || !Fm2CarbinModel.LightScene.Plausible(position))
                    {
                        continue;
                    }
                    at.Add(position);
                    normals.Add(Vector3.Normalize(n));
                    stored.Add(l);
                }
            }
            state.Model = Fm2CarbinModel.FitLightModel(scene, at, normals, stored, 4);
            return state;
        }

        // After the import: the vertices it made (not among those BeginLighting saw in the part) get computed values.
        // Returns a line for the report, null when nothing was new.
        public string FinishLighting(LightingState state)
        {
            uint lod = this.LightSceneLod();
            int total = 0, parts = 0;
            foreach (Section section in this.Sections)
            {
                if (LightSkipped(section))
                {
                    continue;
                }
                VertexBuffer buffer = (lod == 0) ? section.Lod0 : section.Lod;
                int offset = LightOffset(section, buffer);
                if (offset < 0)
                {
                    continue;
                }
                HashSet<string> kept;
                state.Kept.TryGetValue(section.Name, out kept);
                bool[] pending = new bool[buffer.Count];
                bool any = false;
                for (int v = 0; v < buffer.Count; v++)
                {
                    pending[v] = kept == null || !kept.Contains(LightKey(buffer, v));
                    any |= pending[v];
                }
                if (!any)
                {
                    continue;
                }
                int done = this.ComputeSectionLight(section, buffer, offset, pending, lod, state.Model);
                if (done > 0)
                {
                    total += done;
                    parts++;
                }
            }
            if (total == 0)
            {
                return null;
            }
            return string.Format(CultureInfo.InvariantCulture, "baked light computed on {0} new vertices of {1} part{2} (light sent back {3:0.00}, ground {4})",
                total, parts, (parts == 1) ? "" : "s", state.Model.Rho, (state.Model.GroundY < -10) ? "none" : string.Format(CultureInfo.InvariantCulture, "at {0:0.00} m", state.Model.GroundY));
        }

        // The model of Fm2CarbinModel.ComputeLighting on one part of this car: rays of each pending vertex against the
        // stock parts (this part in the place of its group), light sent back from the stored values, the part's own
        // computed values iterated from zero.
        private int ComputeSectionLight(Section section, VertexBuffer buffer, int offset, bool[] pending, uint lod, Fm2CarbinModel.LightModel model)
        {
            int count = buffer.Count;
            double[] positions = Positions(section, buffer);
            double[][] values = SectionLight(section, buffer);
            bool[] compute = new bool[count];
            Vector3[] normals = new Vector3[count];
            int written = 0;
            for (int v = 0; v < count; v++)
            {
                if (!pending[v])
                {
                    continue;
                }
                Vector3 n = LayoutNormal(section, buffer, v);
                if (n.LengthSquared() < 0.25f || !Fm2CarbinModel.LightScene.Plausible(LightAt(positions, v)))
                {
                    continue;
                }
                normals[v] = Vector3.Normalize(n);
                compute[v] = true;
                Array.Clear(values[v], 0, 16);
                written++;
            }
            if (written == 0)
            {
                return 0;
            }
            Fm2CarbinModel.LightScene scene = this.BuildLightScene(lod, section, positions, values);
            Fm2CarbinModel.LightRays[] rays = new Fm2CarbinModel.LightRays[count];
            Parallel.For(0, count, v =>
            {
                if (compute[v])
                {
                    rays[v] = Fm2CarbinModel.TraceLight(scene, LightAt(positions, v), normals[v], true);
                }
            });
            for (int pass = 0; pass < Fm2CarbinModel.LightPasses; pass++)
            {
                double[][] next = new double[count][];
                Parallel.For(0, count, v =>
                {
                    if (compute[v])
                    {
                        next[v] = Fm2CarbinModel.LightValue(scene, rays[v], model);
                    }
                });
                for (int v = 0; v < count; v++)
                {
                    if (compute[v])
                    {
                        Array.Copy(next[v], values[v], 16);     // the scene's triangles read these arrays
                    }
                }
            }
            for (int v = 0; v < count; v++)
            {
                if (compute[v])
                {
                    WriteLight(buffer, v, offset, values[v]);
                }
            }
            return written;
        }

        // A model rebuilt from an FM2 car's geometry (the FM2 port, Copy Part from FM2): every vertex of a part FM2 has
        // takes the first 4 of the 16 values of the FM2 vertex at the same place (within 2 mm, the one facing most the same
        // way), x LightScale. FM2 vertices without light (glass, interior: all 16 values 0) and vertices with no FM2
        // vertex there keep what they have. Returns a line for the report.
        public string LightingFromMotorsport2(Fm2CarbinModel source)
        {
            int moved = 0, unlit = 0, missed = 0, parts = 0;
            const double cell = 0.002;
            foreach (Section section in this.Sections)
            {
                Fm2CarbinModel.Part part = null;
                foreach (Fm2CarbinModel.Part candidate in source.Parts)
                {
                    if (string.Equals(candidate.Name, section.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        part = candidate;
                        break;
                    }
                }
                VertexBuffer buffer = section.Lod;
                int offset = LightOffset(section, buffer);
                if (part == null || offset < 0 || part.VertexCount == 0)
                {
                    continue;
                }
                parts++;
                double[] theirs = source.Positions(part);
                byte[] data = source.PartVertices(part);
                int n = Math.Min(theirs.Length / 3, data.Length / Fm2CarbinModel.VertexStride);
                Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
                for (int i = 0; i < n; i++)
                {
                    long key = LightCell(theirs[i * 3], theirs[(i * 3) + 1], theirs[(i * 3) + 2], cell, 0, 0, 0);
                    List<int> list;
                    if (!grid.TryGetValue(key, out list))
                    {
                        list = new List<int>();
                        grid[key] = list;
                    }
                    list.Add(i);
                }
                double[] mine = Positions(section, buffer);
                for (int v = 0; v < buffer.Count; v++)
                {
                    Vector3 normal = LayoutNormal(section, buffer, v);
                    if (normal.LengthSquared() > 1e-12f)
                    {
                        normal.Normalize();
                    }
                    int best = -1;
                    float bestDot = 0.5f;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dz = -1; dz <= 1; dz++)
                            {
                                List<int> list;
                                if (!grid.TryGetValue(LightCell(mine[v * 3], mine[(v * 3) + 1], mine[(v * 3) + 2], cell, dx, dy, dz), out list))
                                {
                                    continue;
                                }
                                foreach (int i in list)
                                {
                                    double d = 0;
                                    for (int c = 0; c < 3; c++)
                                    {
                                        double e = theirs[(i * 3) + c] - mine[(v * 3) + c];
                                        d += e * e;
                                    }
                                    if (d > 4e-6)
                                    {
                                        continue;
                                    }
                                    Vector3 theirNormal = ReadPacked111110(data, (i * Fm2CarbinModel.VertexStride) + Fm2CarbinModel.VertexNormal);
                                    if (theirNormal.LengthSquared() < 1e-12f)
                                    {
                                        continue;
                                    }
                                    float dot = Vector3.Dot(Vector3.Normalize(theirNormal), normal);
                                    if (dot > bestDot)
                                    {
                                        bestDot = dot;
                                        best = i;
                                    }
                                }
                            }
                        }
                    }
                    if (best < 0)
                    {
                        missed++;
                        continue;
                    }
                    int from = (best * Fm2CarbinModel.VertexStride) + Fm2CarbinModel.VertexLighting;
                    bool lit = false;
                    for (int k = 0; k < 32; k++)
                    {
                        lit |= data[from + k] != 0;
                    }
                    if (!lit)
                    {
                        unlit++;
                        continue;
                    }
                    double[] value = new double[4];
                    for (int k = 0; k < 4; k++)
                    {
                        value[k] = (short) ((data[from + (k * 2)] << 8) | data[from + (k * 2) + 1]) / 32767.0;     // FM2: 2 x T
                    }
                    WriteLight(buffer, v, offset, value);
                    moved++;
                }
            }
            return string.Format(CultureInfo.InvariantCulture,
                "baked light: the FM2 vertices' own values (first 4 of 16, x {0:0.###}) on {1} vertices of {2} parts; {3} without light in FM2 and {4} with no FM2 vertex in the same place keep the open-panel estimate",
                LightScale, moved, parts, unlit, missed);
        }

        private static long LightCell(double x, double y, double z, double cell, int dx, int dy, int dz)
        {
            long a = (long) Math.Floor(x / cell) + dx + 500000, b = (long) Math.Floor(y / cell) + dy + 500000, c = (long) Math.Floor(z / cell) + dz + 500000;
            return (a * 1000000L * 1000000L) + (b * 1000000L) + c;
        }
    }
}
