namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Text;

    // Every string key used in each .str table of FM4, base game and every DLC package, all languages
    // (docs/FM4/GameStringKeys.json, built by tools/FM4Audit/strings.cs; embedded in the exe). A new key of
    // a table is never one of these, so a string added to the game cannot be blanked or replaced by a
    // package (the audit found 909 shared keys empty in one package and filled in another).
    public static class GameStringKeys
    {
        private const string ResourceName = "GameStringKeys.json";

        private static Dictionary<string, Dictionary<int, bool>> tables;

        public static bool Used(string table, int key)
        {
            Dictionary<int, bool> keys;
            return Load().TryGetValue(table, out keys) && keys.ContainsKey(key);
        }

        // The highest key that no FM4 package uses and no given file holds (FFFF ends the key list).
        public static int Free(string table, IEnumerable<StrFile> files)
        {
            for (int candidate = 0xFFFE; candidate > 0; candidate--)
            {
                bool used = Used(table, candidate);
                foreach (StrFile file in files)
                {
                    used = used || file.Contains(candidate);
                }
                if (!used)
                {
                    return candidate;
                }
            }
            throw new InvalidOperationException("The string table " + table + " has no free key.");
        }

        private static Dictionary<string, Dictionary<int, bool>> Load()
        {
            if (tables == null)
            {
                Dictionary<string, Dictionary<int, bool>> found = new Dictionary<string, Dictionary<int, bool>>(StringComparer.OrdinalIgnoreCase);
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
                {
                    if (stream == null)
                    {
                        throw new InvalidOperationException("GameStringKeys.json is not embedded in the program.");
                    }
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        Dictionary<string, object> root = GameIdLimits.Parse(reader.ReadToEnd()) as Dictionary<string, object>;
                        Dictionary<string, object> game = (root != null && root.ContainsKey("FM4")) ? root["FM4"] as Dictionary<string, object> : null;
                        if (game == null)
                        {
                            throw new InvalidOperationException("GameStringKeys.json has no FM4 section.");
                        }
                        foreach (KeyValuePair<string, object> table in game)
                        {
                            Dictionary<int, bool> keys = new Dictionary<int, bool>();
                            foreach (object key in (List<object>) table.Value)
                            {
                                keys[(int) (long) key] = true;
                            }
                            found[table.Key] = keys;
                        }
                    }
                }
                tables = found;
            }
            return tables;
        }
    }
}
