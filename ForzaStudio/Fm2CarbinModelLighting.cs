namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading.Tasks;
    using Microsoft.Xna.Framework;

    // FM2 baked lighting COMPUTED for a part converted from another game (ST-03, user 2026-11-21: "non sarebbe possibile
    // calcolare questi dati di lightning invece di prenderli dal donor?"; an FH1 Abflug bumper stayed blotchy with the donor).
    //
    // What the 16 SNORM16 values at +28 are (study E:\DEV\fm4dev\ST03\fm2t\shb.cs .. shb4.cs, gfit.cs, lcmp.cs, lvar.cs):
    // a precomputed radiance transfer vector, stored = 2 x T, T_j = (1 / pi) x integral of the light reaching the vertex from
    // direction w x max(0, n.w) x Y_j(w), Y = the D3DX real spherical harmonics, bands 0-3 (signs (-1)^m: -y, z, -x, xy, -yz...)
    // in car space. The game shades with the dot product of these 16 values and the lighting of the track.
    // On vertices nothing hides, 2 x T computed here equals the stored values within 0.003 (band 0 = 0.562 facing up).
    // Where the car hides the sky the stored values are much brighter than plain occlusion: the baker let the surfaces send
    // light back. Model, per ray (256, Fibonacci sphere) against the car's LOD 1 opaque triangles (stock parts only: FM2 cars
    // hold every upgrade variant in place, the "a" part of each group stands; the converted part in place of the replaced one):
    //   - the ray meets the car: it brings rho x the STORED value of the surface it meets (interpolated over the triangle;
    //     the converted part's own values are iterated from zero, 6 passes);
    //   - the ray goes down onto the ground, a black rectangle under the car: nothing;
    //   - else: the sky (cosine x Y_j).
    // rho and the ground (height, the 4 edges) differ a little per car, so they are fitted on the car's own stock vertices
    // before the part is computed (1500 vertices; typical rho 0.85-0.95, ground 0.15-0.25 m under the origin, edges 0.1-0.5 m
    // beyond the car's box).
    // Measured as the shading the game evaluates (sky and sun lights, lcmp.cs) on the FM4 Supra bumperFb put in the FM2
    // Supra against the game's own bumperFb: mean error 0.045-0.08 of full brightness (the linear map of features used
    // before: 0.11-0.16, and darker on the upper half). Tried and dropped because they made it worse: correcting with the
    // replaced part's own values nearby (the per-vertex residual is noise, it does not carry over), smoothing.
    public partial class Fm2CarbinModel
    {
        private const int LightDirs = 256;
        private const float LightCell = 0.15f;
        private const float LightBias = 0.004f;             // ray origin: 4 mm off the surface
        internal const int LightPasses = 6;
        private const int LightCalibrationVertices = 1500;
        private static readonly double[] LightRhoChoices = { 0.6, 0.65, 0.7, 0.75, 0.8, 0.85, 0.9, 0.95, 1.0 };
        private static readonly float[] LightGroundChoices = { 0.1f, 0.05f, 0f, -0.05f, -0.1f, -0.15f, -0.2f, -0.25f, -0.3f, -0.35f, -0.4f, -0.5f, -0.6f, -0.8f, -1.2f, -50f };
        private static readonly float[] LightMarginChoices = { -0.3f, -0.15f, 0f, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.75f, 1f, 1.5f, 2.5f, 5f, 1000f };
        private static Vector3[] lightDirs;
        private static double[][] lightSh;

        internal struct LightHit
        {
            public int Triangle;
            public float T;
            public float U;
            public float V;
        }

        // What differs per car: the share of light the car's surfaces send back and the black ground under it.
        internal sealed class LightModel
        {
            public double Rho = 0.85;
            public float GroundY = -0.2f;
            public float MinX = -1000, MaxX = 1000, MinZ = -1000, MaxZ = 1000;
            public int Samples;
            public double Error;

            public bool GroundStops(Vector3 o, Vector3 d)
            {
                if (d.Y > -1e-6f || o.Y <= this.GroundY)
                {
                    return false;
                }
                float t = (this.GroundY - o.Y) / d.Y;
                float x = o.X + (d.X * t), z = o.Z + (d.Z * t);
                return x > this.MinX && x < this.MaxX && z > this.MinZ && z < this.MaxZ;
            }
        }

        // The rays of one vertex: what they bring whatever the model (Direct: the sky above), the triangles they meet
        // (Hits, or their stored light summed in Back when the hits are not kept) and the free rays going down.
        internal sealed class LightRays
        {
            public Vector3 Origin;
            public double[] Direct = new double[16];
            public double[] Back;
            public LightHit[] Hits;
            public double[] HitWeight;
            public int[] Down;
            public float[] DownCos;
        }

        internal sealed class LightScene
        {
            public readonly List<Vector3[]> Triangles = new List<Vector3[]>();
            public readonly List<double[][]> Values = new List<double[][]>();      // per triangle: its 3 vertices' 16 values
            public readonly Dictionary<long, List<int>> Grid = new Dictionary<long, List<int>>();
            public Vector3 Min = new Vector3(float.MaxValue), Max = new Vector3(float.MinValue);

            public void Add(Vector3 a, Vector3 b, Vector3 c, double[] la, double[] lb, double[] lc)
            {
                if (!Plausible(a) || !Plausible(b) || !Plausible(c))
                {
                    return;     // stray vertices at ~ -1.7e38 in a few game files (fer_gto_84, mit_fto_98...)
                }
                int id = this.Triangles.Count;
                this.Triangles.Add(new Vector3[] { a, b, c });
                this.Values.Add(new double[][] { la, lb, lc });
                Vector3 min = Vector3.Min(a, Vector3.Min(b, c)), max = Vector3.Max(a, Vector3.Max(b, c));
                this.Min = Vector3.Min(this.Min, min);
                this.Max = Vector3.Max(this.Max, max);
                for (int x = (int) Math.Floor(min.X / LightCell); x <= (int) Math.Floor(max.X / LightCell); x++)
                {
                    for (int y = (int) Math.Floor(min.Y / LightCell); y <= (int) Math.Floor(max.Y / LightCell); y++)
                    {
                        for (int z = (int) Math.Floor(min.Z / LightCell); z <= (int) Math.Floor(max.Z / LightCell); z++)
                        {
                            long key = LightKey(x, y, z);
                            List<int> cell;
                            if (!this.Grid.TryGetValue(key, out cell))
                            {
                                cell = new List<int>();
                                this.Grid[key] = cell;
                            }
                            cell.Add(id);
                        }
                    }
                }
            }

            public static bool Plausible(Vector3 p)
            {
                return Math.Abs(p.X) < 20 && Math.Abs(p.Y) < 20 && Math.Abs(p.Z) < 20;
            }

            // The nearest triangle the ray meets within 6 m, either face (false: none).
            public bool Nearest(Vector3 o, Vector3 d, out LightHit hit)
            {
                hit = new LightHit();
                hit.Triangle = -1;
                hit.T = float.MaxValue;
                if (!Plausible(o))
                {
                    return false;
                }
                int x = (int) Math.Floor(o.X / LightCell), y = (int) Math.Floor(o.Y / LightCell), z = (int) Math.Floor(o.Z / LightCell);
                int sx = (d.X > 0) ? 1 : -1, sy = (d.Y > 0) ? 1 : -1, sz = (d.Z > 0) ? 1 : -1;
                float tx = Next(o.X, d.X, x), ty = Next(o.Y, d.Y, y), tz = Next(o.Z, d.Z, z);
                float dx = (Math.Abs(d.X) > 1e-9f) ? LightCell / Math.Abs(d.X) : float.MaxValue;
                float dy = (Math.Abs(d.Y) > 1e-9f) ? LightCell / Math.Abs(d.Y) : float.MaxValue;
                float dz = (Math.Abs(d.Z) > 1e-9f) ? LightCell / Math.Abs(d.Z) : float.MaxValue;
                // a ray crosses at most ~3 x 6 m / cell cells; the cap guards against float trouble on odd input
                for (int steps = 0; steps < 200; steps++)
                {
                    float exit = Math.Min(tx, Math.Min(ty, tz));
                    List<int> cell;
                    if (this.Grid.TryGetValue(LightKey(x, y, z), out cell))
                    {
                        foreach (int i in cell)
                        {
                            float t, u, v;
                            if (RayHit(o, d, this.Triangles[i], out t, out u, out v) && t < hit.T)
                            {
                                hit.Triangle = i;
                                hit.T = t;
                                hit.U = u;
                                hit.V = v;
                            }
                        }
                    }
                    if (hit.T <= exit + 1e-4f || exit > 6f)
                    {
                        break;
                    }
                    if (tx <= ty && tx <= tz)
                    {
                        tx += dx;
                        x += sx;
                    }
                    else if (ty <= tz)
                    {
                        ty += dy;
                        y += sy;
                    }
                    else
                    {
                        tz += dz;
                        z += sz;
                    }
                }
                return hit.Triangle >= 0;
            }

            // Adds weight x the 16 values at the hit point (interpolated between the triangle's vertices).
            public void Gather(LightHit hit, double weight, double[] into)
            {
                double[][] l = this.Values[hit.Triangle];
                double a = (1 - hit.U - hit.V) * weight, b = hit.U * weight, c = hit.V * weight;
                for (int j = 0; j < 16; j++)
                {
                    into[j] += (a * l[0][j]) + (b * l[1][j]) + (c * l[2][j]);
                }
            }

            private static float Next(float p, float d, int c)
            {
                return (d > 1e-9f) ? (((c + 1) * LightCell) - p) / d : ((d < -1e-9f) ? ((c * LightCell) - p) / d : float.MaxValue);
            }

            private static bool RayHit(Vector3 o, Vector3 d, Vector3[] tri, out float t, out float u, out float v)
            {
                t = u = v = 0;
                Vector3 e1 = tri[1] - tri[0], e2 = tri[2] - tri[0];
                Vector3 p = Vector3.Cross(d, e2);
                float det = Vector3.Dot(e1, p);
                if (Math.Abs(det) < 1e-12f)
                {
                    return false;
                }
                float inv = 1 / det;
                Vector3 s = o - tri[0];
                u = Vector3.Dot(s, p) * inv;
                if (u < 0 || u > 1)
                {
                    return false;
                }
                Vector3 q = Vector3.Cross(s, e1);
                v = Vector3.Dot(d, q) * inv;
                if (v < 0 || u + v > 1)
                {
                    return false;
                }
                t = Vector3.Dot(e2, q) * inv;
                return t > 0.002f && t < 6f;
            }
        }

        private static long LightKey(int x, int y, int z)
        {
            return (((long) (x + 4096)) << 26) | (((long) (y + 4096)) << 13) | (long) (z + 4096);
        }

        // 256 directions (Fibonacci sphere) and the D3DX real spherical harmonics (bands 0-3) in each.
        internal static void LightSetup()
        {
            if (lightDirs != null)
            {
                return;
            }
            Vector3[] dirs = new Vector3[LightDirs];
            double[][] sh = new double[LightDirs][];
            double golden = Math.PI * (3 - Math.Sqrt(5));
            for (int k = 0; k < LightDirs; k++)
            {
                double h = 1 - ((k + 0.5) * 2.0 / LightDirs), r = Math.Sqrt(1 - (h * h)), th = golden * k;
                dirs[k] = new Vector3((float) (Math.Cos(th) * r), (float) h, (float) (Math.Sin(th) * r));
                double x = dirs[k].X, y = dirs[k].Y, z = dirs[k].Z;
                sh[k] = new double[]
                {
                    0.282095, -0.488603 * y, 0.488603 * z, -0.488603 * x,
                    1.092548 * x * y, -1.092548 * y * z, 0.315392 * ((3 * z * z) - 1), -1.092548 * x * z, 0.546274 * ((x * x) - (y * y)),
                    -0.590044 * y * ((3 * x * x) - (y * y)), 2.890611 * x * y * z, -0.457046 * y * ((5 * z * z) - 1), 0.373176 * z * ((5 * z * z) - 3),
                    -0.457046 * x * ((5 * z * z) - 1), 1.445306 * z * ((x * x) - (y * y)), -0.590044 * x * ((x * x) - (3 * y * y)),
                };
            }
            lightSh = sh;
            lightDirs = dirs;
        }

        // The group of upgrade variants a part belongs to: its name without the last lowercase letter (or "race").
        internal static string VariantGroup(string name)
        {
            if (name.EndsWith("race", StringComparison.Ordinal) && name.Length > 4)
            {
                return name.Substring(0, name.Length - 4);
            }
            return (name.Length > 1 && char.IsLower(name[name.Length - 1])) ? name.Substring(0, name.Length - 1) : name;
        }

        // The parts the game shows by default: alone in their group, or the group's "a" part (else its first part).
        private bool[] StockParts()
        {
            bool[] stock = new bool[this.Parts.Count];
            for (int p = 0; p < this.Parts.Count; p++)
            {
                string group = VariantGroup(this.Parts[p].Name);
                int size = 0, first = -1, a = -1;
                for (int q = 0; q < this.Parts.Count; q++)
                {
                    if (VariantGroup(this.Parts[q].Name) != group)
                    {
                        continue;
                    }
                    size++;
                    if (first < 0)
                    {
                        first = q;
                    }
                    if (this.Parts[q].Name == group + "a")
                    {
                        a = q;
                    }
                }
                stock[p] = size == 1 || p == ((a >= 0) ? a : (this.noStockWithoutA ? -1 : first));
            }
            return stock;
        }

        // A car ported from FM4 / FH can hold upgrade variants of a group without its "a" part (FM4's R33: skirtLb, c, e, f
        // and no skirtLa, the stock sill is in the body). The game then shows none of them by default, so none stands in the
        // light scene: with the first variant standing, the body's sill was baked as if a skirt covered it (dark blotches
        // in game, user 2026-11-22). Set by ReplaceWholeModel only; the stock files keep the rule above.
        private bool noStockWithoutA;

        // LOD 1 opaque triangles of a part (positions: car space; values: the 16 lighting values of each vertex).
        private static void AddPartTriangles(LightScene scene, Part part, double[] positions, double[][] values)
        {
            int count = positions.Length / 3;
            foreach (Mesh mesh in part.Meshes)
            {
                if (mesh.Lod != 1 || IsSeeThrough(mesh.MaterialName))
                {
                    continue;
                }
                foreach (int[] f in MeshTriangles(mesh, count))
                {
                    scene.Add(At(positions, f[0]), At(positions, f[1]), At(positions, f[2]), values[f[0]], values[f[1]], values[f[2]]);
                }
            }
        }

        private static Vector3 At(double[] positions, int i)
        {
            return new Vector3((float) positions[i * 3], (float) positions[(i * 3) + 1], (float) positions[(i * 3) + 2]);
        }

        // Triangles of a mesh (strip with restarts, or list), winding as the game draws it.
        private static List<int[]> MeshTriangles(Mesh mesh, int count)
        {
            List<int[]> faces = new List<int[]>();
            int restart = (mesh.IndexSize == 2) ? 0xFFFF : 0x00FFFFFF;
            List<int> ix = mesh.Indices;
            if (mesh.Primitive == 4)
            {
                for (int i = 0; i + 2 < ix.Count; i += 3)
                {
                    AddFace(faces, ix[i], ix[i + 1], ix[i + 2], count);
                }
                return faces;
            }
            int start = 0;
            for (int i = 0; i < ix.Count; i++)
            {
                if (ix[i] == restart)
                {
                    start = i + 1;
                    continue;
                }
                if (i - start >= 2)
                {
                    if (((i - start) & 1) == 0)
                    {
                        AddFace(faces, ix[i - 2], ix[i - 1], ix[i], count);
                    }
                    else
                    {
                        AddFace(faces, ix[i - 1], ix[i - 2], ix[i], count);
                    }
                }
            }
            return faces;
        }

        private static void AddFace(List<int[]> faces, int a, int b, int c, int count)
        {
            if (a != b && b != c && a != c && a >= 0 && b >= 0 && c >= 0 && a < count && b < count && c < count)
            {
                faces.Add(new int[] { a, b, c });
            }
        }

        // The 16 stored values of every vertex of a part of this car.
        private double[][] PartLighting(Part part)
        {
            byte[] data = this.PartVertices(part);
            int count = data.Length / VertexStride;
            double[][] values = new double[count][];
            for (int v = 0; v < count; v++)
            {
                values[v] = ReadLighting(data, v * VertexStride);
            }
            return values;
        }

        // The car as the game shows it: its stock parts, 'standIn' added; 'slotGroup' (not null): every part of that variant
        // group leaves (standIn takes its place); 'skip': a part that leaves too (the replaced one).
        private LightScene BuildLightScene(Part standIn, double[] standInPositions, double[][] standInValues, string slotGroup, string skip)
        {
            LightScene scene = new LightScene();
            bool[] stock = this.StockParts();
            for (int p = 0; p < this.Parts.Count; p++)
            {
                Part part = this.Parts[p];
                if (part == standIn || !stock[p])
                {
                    continue;
                }
                if (skip != null && string.Equals(part.Name, skip, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (slotGroup != null && VariantGroup(part.Name) == slotGroup)
                {
                    continue;
                }
                AddPartTriangles(scene, part, this.Positions(part), this.PartLighting(part));
            }
            if (standIn != null)
            {
                AddPartTriangles(scene, standIn, standInPositions, standInValues);
            }
            return scene;
        }

        // The rays of a vertex against the scene. keepHits: the triangles met are kept (their values may change: the
        // part being computed); else their light is summed in Back at once (the stored values of a finished car).
        internal static LightRays TraceLight(LightScene scene, Vector3 position, Vector3 normal, bool keepHits)
        {
            LightRays rays = new LightRays();
            rays.Origin = position + (normal * LightBias);
            List<LightHit> hits = keepHits ? new List<LightHit>() : null;
            List<double> weights = keepHits ? new List<double>() : null;
            List<int> down = new List<int>();
            List<float> downCos = new List<float>();
            if (!keepHits)
            {
                rays.Back = new double[16];
            }
            for (int k = 0; k < LightDirs; k++)
            {
                Vector3 d = lightDirs[k];
                float cos = Vector3.Dot(normal, d);
                if (cos <= 0)
                {
                    continue;
                }
                LightHit hit;
                if (scene.Nearest(rays.Origin, d, out hit))
                {
                    // light sent back: (1 / pi) x cos x the solid angle of a ray, on stored (2 x T) values
                    double w = cos * 4.0 / LightDirs;
                    if (keepHits)
                    {
                        hits.Add(hit);
                        weights.Add(w);
                    }
                    else
                    {
                        scene.Gather(hit, w, rays.Back);
                    }
                    continue;
                }
                if (d.Y < -1e-6f)
                {
                    down.Add(k);
                    downCos.Add(cos);
                    continue;
                }
                AddSky(rays.Direct, k, cos);
            }
            if (keepHits)
            {
                rays.Hits = hits.ToArray();
                rays.HitWeight = weights.ToArray();
            }
            rays.Down = down.ToArray();
            rays.DownCos = downCos.ToArray();
            return rays;
        }

        // A free ray's share of the sky: 2 x (1 / pi) x cos x the solid angle of a ray x Y_j.
        private static void AddSky(double[] into, int dir, float cos)
        {
            double w = cos * 8.0 / LightDirs;
            double[] y = lightSh[dir];
            for (int j = 0; j < 16; j++)
            {
                into[j] += w * y[j];
            }
        }

        // The 16 values of a vertex from its rays, under a model.
        internal static double[] LightValue(LightScene scene, LightRays rays, LightModel model)
        {
            double[] s = new double[16];
            for (int j = 0; j < 16; j++)
            {
                s[j] = rays.Direct[j] + ((rays.Back != null) ? model.Rho * rays.Back[j] : 0);
            }
            if (rays.Hits != null)
            {
                for (int h = 0; h < rays.Hits.Length; h++)
                {
                    scene.Gather(rays.Hits[h], model.Rho * rays.HitWeight[h], s);
                }
            }
            for (int q = 0; q < rays.Down.Length; q++)
            {
                if (!model.GroundStops(rays.Origin, lightDirs[rays.Down[q]]))
                {
                    AddSky(s, rays.Down[q], rays.DownCos[q]);
                }
            }
            return s;
        }

        private static LightModel MakeLightModel(LightScene scene, double[] p)
        {
            LightModel model = new LightModel();
            model.Rho = p[0];
            model.GroundY = (float) p[1];
            model.MinX = scene.Min.X - (float) p[2];
            model.MaxX = scene.Max.X + (float) p[3];
            model.MinZ = scene.Min.Z - (float) p[4];
            model.MaxZ = scene.Max.Z + (float) p[5];
            return model;
        }

        // rho and the ground of this car's baker, fitted on the stock parts' own stored values (the car as it is now):
        // first the ground height and one margin for the 4 edges (whole grid), then rho, the height and each edge in turn.
        private LightModel CalibrateLighting()
        {
            LightSetup();
            LightScene scene = this.BuildLightScene(null, null, null, null, null);
            List<Vector3> at = new List<Vector3>(), normals = new List<Vector3>();
            List<double[]> stored = new List<double[]>();
            bool[] stock = this.StockParts();
            for (int p = 0; p < this.Parts.Count; p++)
            {
                if (!stock[p])
                {
                    continue;
                }
                Part part = this.Parts[p];
                double[] positions = this.Positions(part);
                byte[] data = this.PartVertices(part);
                int count = Math.Min(positions.Length / 3, data.Length / VertexStride);
                bool[] used = new bool[count];
                foreach (Mesh mesh in part.Meshes)
                {
                    if (mesh.Lod != 1 || IsSeeThrough(mesh.MaterialName))
                    {
                        continue;
                    }
                    foreach (int[] f in MeshTriangles(mesh, count))
                    {
                        used[f[0]] = used[f[1]] = used[f[2]] = true;
                    }
                }
                for (int v = 0; v < count; v++)
                {
                    if (!used[v])
                    {
                        continue;
                    }
                    double[] l = ReadLighting(data, v * VertexStride);
                    bool lit = false;
                    for (int j = 0; j < 16; j++)
                    {
                        lit |= l[j] != 0;
                    }
                    Vector3 n = CarbinModel.ReadPacked111110(data, (v * VertexStride) + VertexNormal);
                    Vector3 position = At(positions, v);
                    if (!lit || n.LengthSquared() < 0.25f || !LightScene.Plausible(position))
                    {
                        continue;
                    }
                    at.Add(position);
                    normals.Add(Vector3.Normalize(n));
                    stored.Add(l);
                }
            }
            return FitLightModel(scene, at, normals, stored, 16);
        }

        // rho and the ground that fit best the stored values (in 2 x T units) of these vertices of the scene; shared with
        // the FM3 / FM4 models (CarbinModelLighting.cs), whose 4 values are the first 4 of the same vector.
        internal static LightModel FitLightModel(LightScene scene, List<Vector3> at, List<Vector3> normals, List<double[]> stored, int values)
        {
            LightSetup();
            double[] best = { 0.85, -0.2, 0.25, 0.25, 0.25, 0.25 };
            if (at.Count < 200)
            {
                return MakeLightModel(scene, best);     // too little to fit on: the usual values
            }
            // every step-th vertex: spread over the whole car, the same choice on every run
            int step = Math.Max(1, at.Count / LightCalibrationVertices);
            List<int> pick = new List<int>();
            for (int i = 0; i < at.Count; i += step)
            {
                pick.Add(i);
            }
            int n2 = pick.Count;
            LightRays[] rays = new LightRays[n2];
            Parallel.For(0, n2, i => { rays[i] = TraceLight(scene, at[pick[i]], normals[pick[i]], false); });
            Func<double[], double> error = delegate(double[] p)
            {
                LightModel model = MakeLightModel(scene, p);
                double[] e = new double[n2];
                Parallel.For(0, n2, i =>
                {
                    double[] s = LightValue(scene, rays[i], model);
                    double[] l = stored[pick[i]];
                    double t = 0;
                    for (int j = 0; j < values; j++)
                    {
                        t += Math.Abs(s[j] - l[j]);
                    }
                    e[i] = t;
                });
                double sum = 0;
                for (int i = 0; i < n2; i++)
                {
                    sum += e[i];
                }
                return sum / n2 / values;
            };
            double bestError = error(best);
            foreach (float y in LightGroundChoices)
            {
                foreach (float margin in LightMarginChoices)
                {
                    double[] p = { best[0], y, margin, margin, margin, margin };
                    double e = error(p);
                    if (e < bestError - 1e-7)
                    {
                        bestError = e;
                        best = p;
                    }
                }
            }
            for (int sweep = 0; sweep < 3; sweep++)
            {
                bool moved = false;
                for (int prm = 0; prm < 6; prm++)
                {
                    int choices = (prm == 0) ? LightRhoChoices.Length : ((prm == 1) ? LightGroundChoices.Length : LightMarginChoices.Length);
                    for (int c = 0; c < choices; c++)
                    {
                        double[] p = (double[]) best.Clone();
                        p[prm] = (prm == 0) ? LightRhoChoices[c] : ((prm == 1) ? LightGroundChoices[c] : LightMarginChoices[c]);
                        double e = error(p);
                        if (e < bestError - 1e-7)
                        {
                            bestError = e;
                            best = p;
                            moved = true;
                        }
                    }
                }
                if (!moved)
                {
                    break;
                }
            }
            LightModel fitted = MakeLightModel(scene, best);
            fitted.Samples = n2;
            fitted.Error = bestError;
            return fitted;
        }

        private static double[] ReadLighting(byte[] data, int vertexOffset)
        {
            double[] l = new double[16];
            for (int k = 0; k < 16; k++)
            {
                l[k] = (short) ((data[vertexOffset + VertexLighting + (k * 2)] << 8) | data[vertexOffset + VertexLighting + (k * 2) + 1]) / 32767.0;
            }
            return l;
        }

        // Writes computed lighting into the converted part's vertices ('vertices': its local 60-byte vertices, positions as
        // EncodePositions wrote them for 'part'), for every vertex an opaque mesh draws; the others keep the values they
        // have (the donor's). 'replaced': the part the converted one takes the place of (null: added). Returns the vertices
        // written; 'fitted': what was fitted on this car (for the notes).
        private int ComputeLighting(Part part, byte[] vertices, string replaced, out string fitted)
        {
            LightSetup();
            LightModel model = this.CalibrateLighting();
            fitted = string.Format(CultureInfo.InvariantCulture, "light sent back {0:0.00}, ground {1}", model.Rho, (model.GroundY < -10) ? "none" : string.Format(CultureInfo.InvariantCulture, "at {0:0.00} m", model.GroundY));
            return this.ComputeLighting(part, vertices, replaced, model);
        }

        // The same with a baker given by the caller (a whole car ported into this model has no stored values to fit on:
        // Fm2CarbinModelPort.cs fits on the template before it is replaced).
        private int ComputeLighting(Part part, byte[] vertices, string replaced, LightModel model)
        {
            LightSetup();
            int count = vertices.Length / VertexStride;
            double[] positions = LocalPositions(part, vertices);
            bool[] opaque = new bool[count];
            foreach (Mesh mesh in part.Meshes)
            {
                if (IsSeeThrough(mesh.MaterialName))
                {
                    continue;
                }
                foreach (int i in mesh.Indices)
                {
                    if (i >= 0 && i < count)
                    {
                        opaque[i] = true;
                    }
                }
            }
            bool[] compute = new bool[count];
            Vector3[] normals = new Vector3[count];
            double[][] values = new double[count][];
            int written = 0;
            for (int v = 0; v < count; v++)
            {
                int o = v * VertexStride;
                values[v] = ReadLighting(vertices, o);      // kept where nothing is computed
                Vector3 n = CarbinModel.ReadPacked111110(vertices, o + VertexNormal);
                // a stray vertex or one without a normal keeps the donor's values
                if (!opaque[v] || n.LengthSquared() < 0.25f || !LightScene.Plausible(At(positions, v)))
                {
                    continue;
                }
                normals[v] = Vector3.Normalize(n);
                compute[v] = true;
                Array.Clear(values[v], 0, 16);              // its own light sent back starts from nothing
                written++;
            }
            // replacing: the converted part takes the replaced part's group slot; added: it stands beside the stock parts
            LightScene scene = this.BuildLightScene(part, positions, values, (replaced != null) ? VariantGroup(replaced) : null, replaced);
            LightRays[] rays = new LightRays[count];
            Parallel.For(0, count, v =>
            {
                if (compute[v])
                {
                    rays[v] = TraceLight(scene, At(positions, v), normals[v], true);
                }
            });
            for (int pass = 0; pass < LightPasses; pass++)
            {
                double[][] next = new double[count][];
                Parallel.For(0, count, v =>
                {
                    if (compute[v])
                    {
                        next[v] = LightValue(scene, rays[v], model);
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
                if (!compute[v])
                {
                    continue;
                }
                int o = v * VertexStride;
                for (int k = 0; k < 16; k++)
                {
                    int s = (int) Math.Round(Math.Max(-1, Math.Min(1, values[v][k])) * 32767);
                    vertices[o + VertexLighting + (k * 2)] = (byte) (s >> 8);
                    vertices[o + VertexLighting + (k * 2) + 1] = (byte) s;
                }
            }
            return written;
        }
    }
}
