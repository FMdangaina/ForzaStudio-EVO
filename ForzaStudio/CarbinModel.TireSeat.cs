using System;
using System.Collections.Generic;

namespace ForzaStudio
{
    // The tire seat ring FM3 needs on a wheel's LOD 0 (user 2026-11-22: FM2-born wheels in FM3 showed bare rims, no tire).
    // FM3 has no tire mesh in LOD 0: it draws its generic tire (tirea / TireB / TireC) and, unlike FM4, whose wheel header holds
    // the 12 tire measures, it can only take them from the geometry: the LOD 0 'wheel_black' mesh. On FM3's own wheels that mesh
    // is always a ring 2-4 mm wide on the inner side (x from the LOD 0 minimum) at the LOD 0 rim's largest radius, ~160 vertices
    // (E:\DEV\fm4dev\FHtoFM3\wblack.exe, 25 wheels). FM2 has no LOD 0; the LOD 0 rebuilt from LOD 1 has no wheel_black.
    // Here, on the FM4-form wheel before it becomes FM3's, a wheel_black LOD 0 mesh is made of the LOD 0 vertices already on that
    // ring (inner side, largest radius): a copy of a LOD 0 mesh (its material slot, UV range and flags) renamed wheel_black, with
    // triangles between neighbouring ring vertices by angle. No vertex is added or moved.
    public partial class CarbinModel
    {
        private const double TireSeatWidth = 0.004;      // FM3's rings: 0.002-0.004 wide along the axis
        private const double TireSeatRadiusSlack = 0.004;

        // Returns a report line, or null when the wheel has its wheel_black LOD 0 mesh already (or nothing to build it on).
        public string AddTireSeatRing()
        {
            if (this.Sections.Count == 0)
            {
                return null;
            }
            int sectionIndex = 0;
            Section section = this.Sections[sectionIndex];
            int template = -1;
            for (int k = 0; k < section.SubSections.Count; k++)
            {
                SubSection sub = section.SubSections[k];
                if (sub.Lod != 0)
                {
                    continue;
                }
                if (string.Equals(sub.Name, "wheel_black", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                if (template < 0 || string.Equals(sub.Name, "inner_rim", StringComparison.OrdinalIgnoreCase))
                {
                    template = k;
                }
            }
            if (template < 0 || section.Lod0 == null || section.Lod0.Count == 0)
            {
                return null;
            }
            // FM4 form (the port, before conversion) or FM3 itself (a built pack's wheel, mended in place)
            double[] p = this.motorsport3 ? Motorsport3Positions(section, section.Lod0) : Positions(section, section.Lod0);
            int count = p.Length / 3;
            // The LOD 0 vertices the meshes use: the ring is measured on them only.
            HashSet<int> used = new HashSet<int>();
            foreach (SubSection sub in section.SubSections)
            {
                if (sub.Lod != 0)
                {
                    continue;
                }
                foreach (int index in sub.Indices)
                {
                    if (!IsRestart(sub, index) && index < count)
                    {
                        used.Add(index);
                    }
                }
            }
            double xMin = double.MaxValue;
            double rMax = 0;
            foreach (int i in used)
            {
                xMin = Math.Min(xMin, p[i * 3]);
                rMax = Math.Max(rMax, Radius(p, i));
            }
            // The ring: vertices at the largest radius near the inner side. Widened step by step if too few.
            List<int> ring = new List<int>();
            for (double width = TireSeatWidth; width <= 0.05 && ring.Count < 8; width *= 2)
            {
                ring.Clear();
                foreach (int i in used)
                {
                    if (p[i * 3] <= xMin + width && Radius(p, i) >= rMax - TireSeatRadiusSlack)
                    {
                        ring.Add(i);
                    }
                }
            }
            if (ring.Count < 3)
            {
                return string.Format("tire seat: no LOD 0 vertices at the rim's edge (radius {0:0.000}): wheel_black not made, FM3 may draw no tire", rMax);
            }
            ring.Sort(delegate(int a, int b) { return Angle(p, a).CompareTo(Angle(p, b)); });
            int copyIndex = this.DuplicateSubSection(sectionIndex, template);
            SubSection copy = section.SubSections[copyIndex];
            copy.Name = "wheel_black";
            int restart = Restart(copy);
            List<int> indices = new List<int>();
            // A fan of thin triangles round the ring: each neighbour pair with the next vertex, one strip each.
            for (int i = 0; i < ring.Count; i++)
            {
                if (indices.Count > 0)
                {
                    indices.Add(restart);
                }
                indices.Add(ring[i]);
                indices.Add(ring[(i + 1) % ring.Count]);
                indices.Add(ring[(i + 2) % ring.Count]);
            }
            copy.Indices.Clear();
            copy.Indices.AddRange(indices);
            double ringMin = double.MaxValue;
            double ringMax = double.MinValue;
            foreach (int i in ring)
            {
                ringMin = Math.Min(ringMin, p[i * 3]);
                ringMax = Math.Max(ringMax, p[i * 3]);
            }
            return string.Format("tire seat: wheel_black LOD 0 made (FM3 fits its tire on it) from {0} rim vertices, x {1:0.000}..{2:0.000}, radius {3:0.000}, on the slot of {4}",
                ring.Count, ringMin, ringMax, rMax, section.SubSections[template].Name);
        }

        // The same LOD 0 sizing as the FM4 -> FM3 conversion (SizeWheelLod0ForMotorsport3), on a wheel already in FM3 form (a
        // built pack mended in place): LOD 0 scaled about the axis to 'radius' when outside FM3's band; both buffers written again
        // (FM3 positions hang on the bounds' centre, which may grow). Returns the report line, or null when nothing changed.
        public string SizeMotorsport3WheelLod0(double radius)
        {
            if (!this.motorsport3 || this.Sections.Count == 0)
            {
                throw new InvalidOperationException("Not an FM3 wheel model.");
            }
            Section section = this.Sections[0];
            double[] lod = Motorsport3Positions(section, section.Lod);
            double[] lod0 = Motorsport3Positions(section, section.Lod0);
            this.Motorsport3WheelRadius = radius;
            string line = this.SizeWheelLod0ForMotorsport3(section, lod0);
            if (line == null)
            {
                return null;
            }
            if (OutsideBounds(section.Bounds, lod0))
            {
                GrowBounds(section, lod, lod0);
            }
            Motorsport3Encode(section, section.Lod, lod);
            Motorsport3Encode(section, section.Lod0, lod0);
            return line;
        }

        private static double Radius(double[] p, int i)
        {
            return Math.Sqrt((p[(i * 3) + 1] * p[(i * 3) + 1]) + (p[(i * 3) + 2] * p[(i * 3) + 2]));
        }

        private static double Angle(double[] p, int i)
        {
            return Math.Atan2(p[(i * 3) + 2], p[(i * 3) + 1]);
        }
    }
}
