namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Xml;

    // physicsdefinition.bin of a Forza Horizon 1 / 2 car zip (FH only; E:\DEV\fm4dev\ToFH\PHYSICSDEFINITION.md, 392/392 files
    // of both games parsed with no byte left over, 2026-11-26). Big endian:
    //   u32 1, u32 13, u32 1, 30 floats (f00 = 0.01, f01-f09 = the solid hull's inertia tensor, f10-f18 = its exact inverse,
    //   f19-f29 not decoded), u32 shape count, shapes { u32 kind; kind 0 = sphere: u16 flags + x, y, z, radius;
    //   kind 2 = convex hull: u32 size + blob }, trailer u32 1, u16 0, u32 0xFFFFFFFF.
    // The spheres are in the frame of physics/maxdata.xml (X / Y identical where both have them) with the same Flags
    // (2 = body, 1 = contact point). FH's own collision: 44 FH1 cars carry a 4-sphere placeholder maxdata.xml but their
    // full 26-42 spheres here. User rule 2026-11-26: maxdata.xml not valid -> the spheres come from this file (FM2, FM3, FM4).
    public static class FhPhysicsDefinition
    {
        public const string EntryName = "physicsdefinition.bin";

        // Fewer than this many spheres = a placeholder (FH's 4 spheres at the wheel corners; FM4 cars have 26-39, FM2's 20).
        public const int PlaceholderSpheres = 10;

        public struct Sphere
        {
            public int Flags;
            public double X, Y, Z, Radius;
        }

        // The spheres in file order; null when the file is not in the layout above (nothing is guessed).
        public static List<Sphere> ReadSpheres(byte[] data)
        {
            if (data == null || data.Length < 0x88 + 10 || U32(data, 0) != 1 || U32(data, 4) != 13 || U32(data, 8) != 1)
            {
                return null;
            }
            List<Sphere> spheres = new List<Sphere>();
            int p = 0x84;
            uint count = U32(data, p);
            p += 4;
            for (uint i = 0; i < count; i++)
            {
                if (p + 8 > data.Length)
                {
                    return null;
                }
                uint kind = U32(data, p);
                p += 4;
                if (kind == 0)
                {
                    if (p + 18 > data.Length)
                    {
                        return null;
                    }
                    Sphere sphere = new Sphere { Flags = (data[p] << 8) | data[p + 1], X = F32(data, p + 2), Y = F32(data, p + 6), Z = F32(data, p + 10), Radius = F32(data, p + 14) };
                    if (!(sphere.Radius > 0) || double.IsInfinity(sphere.Radius) || double.IsNaN(sphere.X) || double.IsNaN(sphere.Y) || double.IsNaN(sphere.Z))
                    {
                        return null;
                    }
                    spheres.Add(sphere);
                    p += 18;
                }
                else if (kind == 2)
                {
                    long size = U32(data, p);
                    if (p + 4 + size > data.Length)
                    {
                        return null;
                    }
                    p += 4 + (int) size;
                }
                else
                {
                    return null;
                }
            }
            if (data.Length - p != 10 || U32(data, p) != 1 || U32(data, p + 6) != 0xFFFFFFFF)
            {
                return null;
            }
            return spheres;
        }

        // maxdata.xml's CollSpheres not valid: missing, a placeholder (fewer than PlaceholderSpheres), or a sphere whose
        // radius / position is not a number (or radius <= 0).
        public static bool IsInvalid(XmlElement spheres)
        {
            if (spheres == null)
            {
                return true;
            }
            int count = 0;
            foreach (XmlNode node in spheres.ChildNodes)
            {
                XmlElement sphere = node as XmlElement;
                if (sphere == null)
                {
                    continue;
                }
                double radius, x, y, z;
                if (!Number(sphere, "Radius", out radius) || !(radius > 0) || double.IsInfinity(radius)
                    || !Number(sphere, "PosX", out x) || !Number(sphere, "PosY", out y) || !Number(sphere, "PosZ", out z))
                {
                    return true;
                }
                count++;
            }
            return count < PlaceholderSpheres;
        }

        // Replaces the children of CollSpheres with the .bin's spheres (Sphere0.., FM4's attribute order). Returns false and
        // changes nothing when the .bin is missing, not readable or itself has fewer than PlaceholderSpheres spheres.
        public static bool ApplySpheres(XmlDocument document, XmlElement spheres, byte[] data, out int count)
        {
            count = 0;
            List<Sphere> list = ReadSpheres(data);
            if (spheres == null || list == null || list.Count < PlaceholderSpheres)
            {
                return false;
            }
            spheres.RemoveAll();
            foreach (string attribute in new string[] { "PosOffsetX", "PosOffsetY", "PosOffsetZ" })
            {
                spheres.SetAttribute(attribute, "0.000000");
            }
            spheres.SetAttribute("PosScale", "1.000000");
            spheres.SetAttribute("RadiusScale", "1.000000");
            for (int i = 0; i < list.Count; i++)
            {
                XmlElement sphere = document.CreateElement("Sphere" + i);
                sphere.SetAttribute("Radius", list[i].Radius.ToString("0.000000", CultureInfo.InvariantCulture));
                sphere.SetAttribute("PosX", list[i].X.ToString("0.000000", CultureInfo.InvariantCulture));
                sphere.SetAttribute("PosY", list[i].Y.ToString("0.000000", CultureInfo.InvariantCulture));
                sphere.SetAttribute("PosZ", list[i].Z.ToString("0.000000", CultureInfo.InvariantCulture));
                sphere.SetAttribute("Flags", list[i].Flags.ToString(CultureInfo.InvariantCulture));
                spheres.AppendChild(sphere);
            }
            count = list.Count;
            return true;
        }

        private static bool Number(XmlElement element, string name, out double value)
        {
            return double.TryParse(element.GetAttribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value);
        }

        private static uint U32(byte[] d, int o)
        {
            return (uint) ((d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3]);
        }

        private static double F32(byte[] d, int o)
        {
            return BitConverter.ToSingle(new byte[] { d[o + 3], d[o + 2], d[o + 1], d[o] }, 0);
        }
    }
}
