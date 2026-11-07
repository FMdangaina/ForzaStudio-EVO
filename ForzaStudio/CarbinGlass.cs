using System;
using System.Collections.Generic;
using System.Globalization;

namespace ForzaStudio
{
    // GlassParticles (default.xex: CCarPartData.GlassParticles, Vertices vec4 + Indices u16) = the mesh the glass
    // shatters into. Measured on the 20 FM4 DLC cars (fm4dev\CarbinCheck glass2, 2026-11-07):
    //   - only in the main file, next to LOD1, on the side / rear / light glass (glassR, glassLF, glassLTL, glassLHL...);
    //     the windscreen glassF never has any (FM3 and FH neither: it cracks, it does not shatter);
    //   - vertices in model space, w = 1, lying on the glass (1-6 cm from the LOD1 surface, which is coarser);
    //   - triangles in pairs (0 1 2, 2 3 0) of small quads, edge median 5-7 cm, one connected mesh;
    //   - FM3, FH1, FH2 and FM4 share the same pattern for the same car (an artist's mesh, not computed by the game).
    // A part whose geometry is replaced loses its pattern; this rebuilds one from the new LOD1 triangles: every
    // triangle split into a grid of about ParticleEdge, vertices shared, the source winding kept.
    public partial class CarbinModel
    {
        public const double ParticleEdge = 0.06;

        // Rebuilds the section's glass particles from its LOD1 (main file) geometry. Returns the vertex count, 0 when
        // the section has no LOD1 triangles.
        public static int BuildGlassParticles(Section section)
        {
            double[] positions = Positions(section, section.Lod);
            List<int[]> triangles = new List<int[]>();
            foreach (SubSection sub in section.SubSections)
            {
                if (sub.Lod == 1)
                {
                    triangles.AddRange(StripTriangles(sub));
                }
            }
            if (triangles.Count == 0 || positions.Length == 0)
            {
                section.SetGlassParticles(new float[0], new int[0]);
                return 0;
            }
            double edge = ParticleEdge;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                List<float> vertices = new List<float>();
                List<int> indices = new List<int>();
                if (Subdivide(positions, triangles, edge, vertices, indices))
                {
                    section.SetGlassParticles(vertices.ToArray(), indices);
                    return vertices.Count / 4;
                }
                edge *= 1.5;   // too many vertices for the 16-bit indices: bigger pieces
            }
            section.SetGlassParticles(new float[0], new int[0]);
            return 0;
        }

        private static bool Subdivide(double[] positions, List<int[]> triangles, double edge, List<float> vertices, List<int> indices)
        {
            Dictionary<string, int> shared = new Dictionary<string, int>();
            foreach (int[] t in triangles)
            {
                double[] a = Point(positions, t[0]);
                double[] b = Point(positions, t[1]);
                double[] c = Point(positions, t[2]);
                double longest = Math.Max(Distance(a, b), Math.Max(Distance(b, c), Distance(c, a)));
                if (longest < 1e-6)
                {
                    continue;
                }
                int n = Math.Max(1, Math.Min(64, (int) Math.Ceiling(longest / edge)));
                int[,] grid = new int[n + 1, n + 1];
                for (int i = 0; i <= n; i++)
                {
                    for (int j = 0; i + j <= n; j++)
                    {
                        double u = i / (double) n;
                        double v = j / (double) n;
                        double[] p =
                        {
                            a[0] + ((b[0] - a[0]) * u) + ((c[0] - a[0]) * v),
                            a[1] + ((b[1] - a[1]) * u) + ((c[1] - a[1]) * v),
                            a[2] + ((b[2] - a[2]) * u) + ((c[2] - a[2]) * v),
                        };
                        string key = string.Format(CultureInfo.InvariantCulture, "{0:F4},{1:F4},{2:F4}", p[0], p[1], p[2]);
                        int index;
                        if (!shared.TryGetValue(key, out index))
                        {
                            index = vertices.Count / 4;
                            if (index > 65534)
                            {
                                return false;
                            }
                            shared[key] = index;
                            vertices.Add((float) p[0]);
                            vertices.Add((float) p[1]);
                            vertices.Add((float) p[2]);
                            vertices.Add(1f);
                        }
                        grid[i, j] = index;
                    }
                }
                for (int i = 0; i < n; i++)
                {
                    for (int j = 0; i + j < n; j++)
                    {
                        AddTriangle(indices, grid[i, j], grid[i + 1, j], grid[i, j + 1]);
                        if (i + j + 1 < n)
                        {
                            AddTriangle(indices, grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]);
                        }
                    }
                }
            }
            return indices.Count > 0;
        }

        private static void AddTriangle(List<int> indices, int a, int b, int c)
        {
            if (a != b && b != c && c != a)
            {
                indices.Add(a);
                indices.Add(b);
                indices.Add(c);
            }
        }

        private static double[] Point(double[] positions, int index)
        {
            return new double[] { positions[index * 3], positions[(index * 3) + 1], positions[(index * 3) + 2] };
        }

        private static double Distance(double[] p, double[] q)
        {
            double x = p[0] - q[0];
            double y = p[1] - q[1];
            double z = p[2] - q[2];
            return Math.Sqrt((x * x) + (y * y) + (z * z));
        }
    }
}
