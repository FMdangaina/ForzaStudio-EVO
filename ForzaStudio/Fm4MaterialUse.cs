using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ForzaStudio
{
    // The materials FM4 itself uses, per kind of model file and LOD (docs\FM4\Fm4MaterialUse.txt, measured on the base
    // game + every installed DLC, SLOD cars of the Autovista pack left out because they have their own special
    // materials). A material is valid in a list when one FM4 carbin of that list has it. The compiled shaders
    // are not the test: FM4 has techniques (lights_amber, Bentley_paint) that no normal car uses and that render black.
    public static class Fm4MaterialUse
    {
        private static Dictionary<string, Dictionary<string, bool>> lists;

        private static void Load()
        {
            if (lists != null)
            {
                return;
            }
            Dictionary<string, Dictionary<string, bool>> found = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Fm4MaterialUse.txt"))
                {
                    if (stream != null)
                    {
                        using (StreamReader reader = new StreamReader(stream))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                string[] part = line.Split('\t');
                                if (line.StartsWith("#") || part.Length != 2)
                                {
                                    continue;
                                }
                                Dictionary<string, bool> names;
                                if (!found.TryGetValue(part[0], out names))
                                {
                                    names = new Dictionary<string, bool>(StringComparer.Ordinal);
                                    found[part[0]] = names;
                                }
                                names[part[1]] = true;
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            lists = found;
        }

        // The list a subsection belongs to: wheels (one file, LOD 0-5), brakes, cockpit, or the car's LOD 0 / LOD 1-5.
        public static string ListName(bool wheels, string fileName, uint lod)
        {
            string file = (fileName ?? "").ToLowerInvariant();
            if (wheels)
            {
                return "wheels_LOD" + Math.Min(lod, 5u);
            }
            if (file.Contains("caliper") || file.Contains("rotor"))
            {
                return "brake_LOD0";
            }
            if (file.EndsWith("_cockpit.carbin"))
            {
                return "car_cockpit";
            }
            return (lod == 0) ? "car_LOD0" : "car_LOD1-5";
        }

        // ---- what FM4 cars give each part (docs\FM4\Fm4PartMaterials.txt: list, part, material, cars) ----

        private static Dictionary<string, Dictionary<string, int>> partUse;   // "list|part" -> material -> cars

        private static void LoadParts()
        {
            if (partUse != null)
            {
                return;
            }
            Dictionary<string, Dictionary<string, int>> found = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Fm4PartMaterials.txt"))
                {
                    if (stream != null)
                    {
                        using (StreamReader reader = new StreamReader(stream))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                string[] part = line.Split('\t');
                                int cars;
                                if (line.StartsWith("#") || part.Length != 4 || !int.TryParse(part[3], out cars))
                                {
                                    continue;
                                }
                                Dictionary<string, int> materials;
                                if (!found.TryGetValue(part[0] + "|" + part[1], out materials))
                                {
                                    materials = new Dictionary<string, int>(StringComparer.Ordinal);
                                    found[part[0] + "|" + part[1]] = materials;
                                }
                                materials[part[2]] = cars;
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            partUse = found;
        }

        // The materials FM4 car models give a part in this list, most used first, with the number of cars. The lists
        // are the car lists of ListName (car_LOD0, car_LOD1-5, car_cockpit).
        public static List<KeyValuePair<string, int>> PartMaterials(string list, string part)
        {
            LoadParts();
            List<KeyValuePair<string, int>> result = new List<KeyValuePair<string, int>>();
            Dictionary<string, int> materials;
            if (partUse.TryGetValue(list + "|" + part, out materials))
            {
                result.AddRange(materials);
                result.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b)
                {
                    int c = b.Value.CompareTo(a.Value);
                    return (c != 0) ? c : string.CompareOrdinal(a.Key, b.Key);
                });
            }
            return result;
        }

        // True when the list is known (the file is missing from a build without the resource: nothing is flagged).
        public static bool Has(string list)
        {
            Load();
            return lists.ContainsKey(list);
        }

        public static bool IsUsed(string list, string material)
        {
            Load();
            Dictionary<string, bool> names;
            return !lists.TryGetValue(list, out names) || names.ContainsKey(material);
        }

        // A subsection of a car model (main, lod0 or cockpit file) of this LOD: the converter does not know which of
        // lod0 / cockpit it is writing, so LOD 0 is valid in either list.
        public static bool UsedByCarModel(uint lod, string material)
        {
            if (lod == 0)
            {
                return IsUsed("car_LOD0", material) || IsUsed("car_cockpit", material);
            }
            return IsUsed("car_LOD1-5", material);
        }

        // True when some FM4 model of any list uses the material.
        public static bool UsedAnywhere(string material)
        {
            Load();
            if (lists.Count == 0)
            {
                return true;
            }
            foreach (Dictionary<string, bool> names in lists.Values)
            {
                if (names.ContainsKey(material))
                {
                    return true;
                }
            }
            return false;
        }

        // Every material of the lists that hold models of cars (not wheels, not brakes), sorted.
        public static string[] CarMaterials()
        {
            Load();
            Dictionary<string, bool> all = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, Dictionary<string, bool>> list in lists)
            {
                if (list.Key.StartsWith("car_"))
                {
                    foreach (string name in list.Value.Keys)
                    {
                        all[name] = true;
                    }
                }
            }
            string[] names2 = new string[all.Count];
            all.Keys.CopyTo(names2, 0);
            Array.Sort(names2, StringComparer.OrdinalIgnoreCase);
            return names2;
        }
    }
}
