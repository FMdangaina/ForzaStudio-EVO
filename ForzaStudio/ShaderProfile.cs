using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ForzaStudio
{
    // Which game's shaders a model is drawn with. FM4 has its own tables (ShaderLookNames, ShaderSettings.Fm4Table,
    // Fm4MaterialUse); the other games read what their own cars use from the embedded lists below
    // (docs\<FM2|FM3|HZN>\MaterialUse.txt and SsParams.txt, made by fm4dev\Shaders\games\gamescan.exe,
    // see docs\SHADERS_OTHER_GAMES.md). FH1 and FH2 share one profile: same shaders, same technique list.
    public enum ShaderProfile
    {
        Unknown,
        Fm2,
        Fm3,
        Fm4,
        Horizon
    }

    public static class ShaderProfiles
    {
        // The game a .carbin comes from, by its layout (every car and wheel file of the four games is recognised):
        // FM2 type 2 / marker 0x2CA, FM3 half-float vertices, Horizon 28-byte vertices, else FM4. Unknown when the
        // file cannot be read; the caller then asks the user (ForShaderGame).
        public static ShaderProfile Detect(byte[] carbin)
        {
            if (carbin == null || carbin.Length < 0x108)
            {
                return ShaderProfile.Unknown;
            }
            try
            {
                if (CarModelFolder.IsMotorsport2(carbin))
                {
                    return ShaderProfile.Fm2;
                }
                if (CarbinModel.IsMotorsport3File(carbin))
                {
                    return ShaderProfile.Fm3;
                }
                return new CarbinModel(carbin).IsHorizon ? ShaderProfile.Horizon : ShaderProfile.Fm4;
            }
            catch (Exception)
            {
                return ShaderProfile.Unknown;
            }
        }

        // The game folder the open archive is in, when it holds one of the four games (a carbin that cannot be read).
        public static ShaderProfile ForGame(ForzaGame game)
        {
            switch (game)
            {
                case ForzaGame.Motorsport2: return ShaderProfile.Fm2;
                case ForzaGame.Motorsport3: return ShaderProfile.Fm3;
                case ForzaGame.Motorsport4: return ShaderProfile.Fm4;
                case ForzaGame.Horizon1:
                case ForzaGame.Horizon2: return ShaderProfile.Horizon;
                default: return ShaderProfile.Unknown;
            }
        }

        public static string Tag(ShaderProfile profile)
        {
            switch (profile)
            {
                case ShaderProfile.Fm2: return "FM2";
                case ShaderProfile.Fm3: return "FM3";
                case ShaderProfile.Fm4: return "FM4";
                case ShaderProfile.Horizon: return "FH";
                default: return "";
            }
        }

        public static string Name(ShaderProfile profile)
        {
            switch (profile)
            {
                case ShaderProfile.Fm2: return "Forza Motorsport 2";
                case ShaderProfile.Fm3: return "Forza Motorsport 3";
                case ShaderProfile.Fm4: return "Forza Motorsport 4";
                case ShaderProfile.Horizon: return "Forza Horizon";
                default: return "";
            }
        }

        // ---- the lists of a game other than FM4 ----

        private static readonly Dictionary<ShaderProfile, Dictionary<string, Dictionary<string, bool>>> uses = new Dictionary<ShaderProfile, Dictionary<string, Dictionary<string, bool>>>();
        private static readonly Dictionary<ShaderProfile, Dictionary<string, Dictionary<string, bool>>> parameters = new Dictionary<ShaderProfile, Dictionary<string, Dictionary<string, bool>>>();

        // Rows <key><TAB><value> of the resource, grouped by key (a value of the second column is split on spaces for
        // the parameter lists).
        private static Dictionary<string, Dictionary<string, bool>> Read(string resource, bool split)
        {
            Dictionary<string, Dictionary<string, bool>> found = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
                {
                    if (stream == null)
                    {
                        return found;
                    }
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
                            Dictionary<string, bool> values;
                            if (!found.TryGetValue(part[0], out values))
                            {
                                values = new Dictionary<string, bool>(StringComparer.Ordinal);
                                found[part[0]] = values;
                            }
                            foreach (string value in split ? part[1].Split(' ') : new string[] { part[1] })
                            {
                                if (value.Length > 0)
                                {
                                    values[value] = true;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            return found;
        }

        private static Dictionary<string, Dictionary<string, bool>> Uses(ShaderProfile profile)
        {
            Dictionary<string, Dictionary<string, bool>> lists;
            if (!uses.TryGetValue(profile, out lists))
            {
                lists = Read("MaterialUse_" + Tag(profile) + ".txt", false);
                uses[profile] = lists;
            }
            return lists;
        }

        private static Dictionary<string, Dictionary<string, bool>> Parameters(ShaderProfile profile)
        {
            Dictionary<string, Dictionary<string, bool>> table;
            if (!parameters.TryGetValue(profile, out table))
            {
                table = Read("SsParams_" + Tag(profile) + ".txt", true);
                parameters[profile] = table;
            }
            return table;
        }

        // True when the game's list for this kind of file and LOD (Fm4MaterialUse.ListName) has the material, or when
        // the game has no such list (nothing is flagged without data).
        public static bool IsUsed(ShaderProfile profile, string list, string material)
        {
            if (profile == ShaderProfile.Fm4)
            {
                return Fm4MaterialUse.IsUsed(list, material);
            }
            Dictionary<string, bool> names;
            return !Uses(profile).TryGetValue(list, out names) || names.ContainsKey(material);
        }

        // The parameters the game's own cars set on a technique, null when none lists it (FM4: ShaderSettings.Fm4Parameters).
        public static string[] ParametersOf(ShaderProfile profile, string technique)
        {
            if (profile == ShaderProfile.Fm4 || profile == ShaderProfile.Unknown)
            {
                return ShaderSettings.Fm4Parameters(technique);
            }
            Dictionary<string, bool> names;
            if (!Parameters(profile).TryGetValue(technique, out names))
            {
                return null;
            }
            string[] list = new string[names.Count];
            names.Keys.CopyTo(list, 0);
            Array.Sort(list, StringComparer.Ordinal);
            return list;
        }

        // True when some car of the game sets the parameter on some technique (so it is never an error there, typos
        // such as FM3's CokpitBumpScale included: the game ships them).
        public static bool IsParameter(ShaderProfile profile, string name)
        {
            if (profile == ShaderProfile.Fm4 || profile == ShaderProfile.Unknown)
            {
                return false;
            }
            foreach (Dictionary<string, bool> names in Parameters(profile).Values)
            {
                if (names.ContainsKey(name))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
