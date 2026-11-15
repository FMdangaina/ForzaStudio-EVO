namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;

    // The FM4 materials offered in place of an FH1 / FH2 / FM3 material FM4 has no shader for, in three
    // groups: recommended (the port table, a rule on the name, the most similar FM4 shaders), the common
    // FM4 materials, and the unusual ones (every other technique of FM4's car shader library: they have a
    // shader, but no or few FM4 cars use them, for the user who wants something different on purpose).
    // The similarity comes from the compiled car shaders of the four games (fm4dev\Shaders\tools\similarity,
    // 2026-10-18): 60 x the shared pixel shader constants and samplers (weighted: engine globals count less)
    // + 20 x the shared vertex shader constants + 20 x the pixel shader size ratio. It compares what a shader
    // reads, not what it computes: a hint, not a promise of the same look.
    public static class MaterialSuggestions
    {
        public enum Group
        {
            Recommended,
            Common,
            Unusual,
        }

        // Recommended entries are drawn in green (ForzaTheme.GreenItems), unusual ones marked "~".
        public class Entry : IRecommended
        {
            public string Name;
            public Group Group;
            public string Note;       // why it is offered: "port table", "shader 74", ...

            public bool Recommended
            {
                get { return this.Group == Group.Recommended; }
            }

            public override string ToString()
            {
                string mark = (this.Group == Group.Unusual) ? "~ " : "";
                return mark + this.Name + (string.IsNullOrEmpty(this.Note) ? "" : "   (" + this.Note + ")");
            }
        }

        // The choices for a material of a model file, recommended first. Brake models (type 1) get the FM4
        // brake materials only: the shader table covers car models.
        public static List<Entry> For(string material, string fileName, int type)
        {
            return For(material, fileName, type, null);
        }

        // parts: the sections (parts) the material sits on; the materials FM4 cars give those parts are offered first, with their number of cars.
        public static List<Entry> For(string material, string fileName, int type, List<string> parts)
        {
            List<Entry> entries = new List<Entry>();
            if (type == 1)
            {
                foreach (string name in CarPort.Fm4Materials(1))
                {
                    Add(entries, name, Group.Common, null, fileName);
                }
                return entries;
            }
            List<string> similar = new List<string>();   // most similar first, as in the table
            Dictionary<string, int> scores = Scores(material, similar);
            // no hand-made table or name rule (user 2026-10-25): what FM4 cars give the part(s), by count, then the similar shaders
            AddPartUse(entries, material, fileName, parts);
            int shown = 0;
            foreach (string name in similar)
            {
                if (shown == 3 || scores[name] < 50)
                {
                    break;
                }
                if (Add(entries, name, Group.Recommended, "shader " + scores[name], fileName))
                {
                    shown++;
                }
            }
            foreach (string name in Sorted(CarPort.Fm4Materials(2), scores))
            {
                Add(entries, name, Group.Common, Score(scores, name), fileName);
            }
            foreach (string name in Sorted(Candidates.Split(' '), scores))
            {
                string score = Score(scores, name);
                string rare = CarPort.IsFm4Material(name, 2) ? "few FM4 cars" : "no FM4 car";
                Add(entries, name, Group.Unusual, (score != null) ? score + ", " + rare : rare, fileName);
            }
            return entries;
        }

        // The choices for a material of an FM2 / FM3 model (ST-01): first what the game's own cars give the part(s) in this
        // kind of file and LOD (green, with the number of cars), then every other material the game's cars use there, most
        // used first. Only the game's own names: FM2 and FM3 have no shader library the editor can read for other choices.
        // FM4 and FH models keep For(...).
        public static List<Entry> ForGame(ShaderProfile game, string material, string fileName, string list, List<string> parts)
        {
            List<Entry> entries = new List<Entry>();
            string tag = ShaderProfiles.Tag(game);
            Dictionary<string, int> total = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string part in parts ?? new List<string>())
            {
                foreach (KeyValuePair<string, int> use in ShaderProfiles.PartMaterials(game, list, part))
                {
                    int cars;
                    total.TryGetValue(use.Key, out cars);
                    total[use.Key] = cars + use.Value;
                }
            }
            List<KeyValuePair<string, int>> order = new List<KeyValuePair<string, int>>(total);
            order.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b)
            {
                int c = b.Value.CompareTo(a.Value);
                return (c != 0) ? c : string.CompareOrdinal(a.Key, b.Key);
            });
            string on = string.Join(" ", (parts ?? new List<string>()).ToArray());
            foreach (KeyValuePair<string, int> use in order)
            {
                if (use.Key != material)
                {
                    Add(entries, use.Key, Group.Recommended, use.Value + " " + tag + " cars on " + on, fileName);
                }
            }
            foreach (KeyValuePair<string, int> use in ShaderProfiles.PartMaterials(game, list, null))
            {
                if (use.Key != material)
                {
                    Add(entries, use.Key, Group.Common, use.Value + " " + tag + " cars", fileName);
                }
            }
            return entries;
        }

        // The materials FM4 car models give the part(s) in this kind of file (Fm4MaterialUse.PartMaterials), most used
        // first, as green suggestions; nothing is chosen for the user.
        private static void AddPartUse(List<Entry> entries, string material, string fileName, List<string> parts)
        {
            if (parts == null || parts.Count == 0)
            {
                return;
            }
            string file = (fileName ?? "").ToLowerInvariant();
            string list = file.EndsWith("_cockpit.carbin") ? "car_cockpit" : (file.EndsWith("_lod0.carbin") ? "car_LOD0" : "car_LOD1-5");
            Dictionary<string, int> total = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string part in parts)
            {
                foreach (KeyValuePair<string, int> use in Fm4MaterialUse.PartMaterials(list, part))
                {
                    int cars;
                    total.TryGetValue(use.Key, out cars);
                    total[use.Key] = cars + use.Value;
                }
            }
            List<KeyValuePair<string, int>> order = new List<KeyValuePair<string, int>>(total);
            order.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b)
            {
                int c = b.Value.CompareTo(a.Value);
                return (c != 0) ? c : string.CompareOrdinal(a.Key, b.Key);
            });
            int shown = 0;
            foreach (KeyValuePair<string, int> use in order)
            {
                if (shown == 8)
                {
                    break;
                }
                if (use.Key != material && Add(entries, use.Key, Group.Recommended, use.Value + " FM4 models on " + string.Join(" ", parts.ToArray()), fileName))
                {
                    shown++;
                }
            }
        }

        // The name a choice stands for: an Entry's name, or the first word of a typed or listed text.
        public static string NameOf(object choice)
        {
            Entry entry = choice as Entry;
            if (entry != null)
            {
                return entry.Name;
            }
            string text = Convert.ToString(choice).Trim().TrimStart('★', '~').Trim();
            int space = text.IndexOf(' ');
            return (space < 0) ? text : text.Substring(0, space);
        }

        // The preselected choice: the port table, then the name rule, then the most similar shader; null if none.
        public static string Best(string material, string fileName, int type)
        {
            List<Entry> entries = For(material, fileName, type);
            return (entries.Count > 0 && entries[0].Group == Group.Recommended) ? entries[0].Name : null;
        }

        // Adds the file's own variant of a name (tail_light_lod0 in lod0 and cockpit) unless it is there already.
        private static bool Add(List<Entry> entries, string name, Group group, string note, string fileName)
        {
            if (name == null)
            {
                return false;
            }
            name = CarPort.LodVariant(name, fileName);
            foreach (Entry entry in entries)
            {
                if (entry.Name == name)
                {
                    return false;
                }
            }
            Entry added = new Entry();
            added.Name = name;
            added.Group = group;
            added.Note = note;
            entries.Add(added);
            return true;
        }

        private static string Score(Dictionary<string, int> scores, string name)
        {
            int score;
            return scores.TryGetValue(name, out score) ? "shader " + score : null;
        }

        // Most similar first, then by name (a total order: the unstable sort cannot mix it up).
        private static List<string> Sorted(string[] names, Dictionary<string, int> scores)
        {
            List<string> list = new List<string>(names);
            list.Sort(delegate(string a, string b)
            {
                int sa, sb;
                scores.TryGetValue(a, out sa);
                scores.TryGetValue(b, out sb);
                return (sa != sb) ? sb.CompareTo(sa) : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        private static Dictionary<string, int> Scores(string material, List<string> order)
        {
            Dictionary<string, int> scores = new Dictionary<string, int>();
            string list;
            if (material.StartsWith("cockpit_", StringComparison.Ordinal))
            {
                material = material.Substring("cockpit_".Length);
            }
            if (Similar.TryGetValue(material, out list))
            {
                foreach (string pair in list.Split(' '))
                {
                    int colon = pair.IndexOf(':');
                    scores[pair.Substring(0, colon)] = int.Parse(pair.Substring(colon + 1));
                    order.Add(pair.Substring(0, colon));
                }
            }
            return scores;
        }

        // FM4 materials for the FH light and trim names, from what the name says the part is.
        private static string ByName(string material)
        {
            string name = material.ToLowerInvariant();
            if (name.Contains("gls"))
            {
                return "lights_glass";   // the glass over a light, lights_gls_reverse_light included
            }
            if (name.Contains("reverse"))
            {
                return "reverse_light";
            }
            if (name.Contains("orange"))
            {
                return "detail_glass_amber";   // as the port table's indicators and drlinlorange
            }
            if (Regex.IsMatch(name, "^(fullbeam|hidhead|oldhead|xenonhead|fogwhite|drl.*white.*)$"))
            {
                return "head_light";
            }
            if (Regex.IsMatch(name, "^(taillight.*|.*tailside|brin[lr]|[lr]inred|fogred|drl.*red)$"))
            {
                return "tail_light";
            }
            if (name.StartsWith("window_"))
            {
                return "window";
            }
            if (name.StartsWith("carbon_fiber_"))
            {
                return "carbon_fiber";
            }
            return null;
        }

        // True when FM4's car shader library has the material (it renders, even if no FM4 car uses it).
        public static bool HasShader(string material)
        {
            return Array.IndexOf(Candidates.Split(' '), material) >= 0;
        }

        // The first suggestion for a material, with why: what FM4 cars give the part(s) it sits on, else the most similar
        // shader (score 50 and up). Only shown, never applied (the port decides nothing). Null when there is none.
        public static string BestFor(string material, string fileName, List<string> sections, out string basis)
        {
            foreach (Entry entry in For(material, fileName, 2, sections))
            {
                if (entry.Group == Group.Recommended)
                {
                    basis = entry.Note;
                    return entry.Name;
                }
                break;
            }
            basis = null;
            return null;
        }

        // Every material name of FM4's car shader library (shaders_v16), cockpit_ taken off (a cockpit model
        // names leather, the game draws cockpit_leather), render passes and debug views left out.
        private const string Candidates =
            "badge badge2 badge_chrome black black_glass black_glass_breakable blur_lip blur_lip_V2 blur_rim blur_rim_V2 body body_2 body_3 " +
            "bottom bottom_2 brake_badge bumper_frame bump_leather bump_leather2 bump_leather3 bump_plastic bump_plastic2 bump_plastic3 " +
            "bump_stitching bump_stitching2 cage caliper carbon_fiber carbon_fiber2 carbon_fiber3 carbon_fiber_aniso carbon_fiber_no_env " +
            "chrome chrome_2 chrome_3 chrome_blur_lip chrome_blur_lip_V2 chrome_blur_rim chrome_blur_rim_V2 chrome_bump chrome_rim chrome_rim_V2 " +
            "cloth cloth2 defrost_lines detail_glass_amber detail_glass_clear detail_glass_red digital_gauge dullplastic emblem embossed_plastic " +
            "embossed_plastic2 engine frame frontwindow frontwindow_2 gauge gauge_emissive gauge_emissive_unshadowed gauge_needle glass glass_2 " +
            "grille1 grille1_alpha grille2 grille2_alpha headlight_paint headliner head_light head_light2 hub inner_rim inner_rim_V2 inside_window " +
            "interior interior_banner leather lights_body lights_chrome lights_glass liveryBadge matte matte_colors matte_colors2 metal metal2 " +
            "mirrorLeft mirrorMiddle mirrorRight misc mottled outer_rim plastic plastic2 race_kit_red red_glass reflector reverse_light " +
            "reverse_light_lod0 rim rim_bump_metal rim_bump_metal2 rim_V2 rotor rubber rubber_trim rubber_trim_2 scaling_text sidewall " +
            "smooth_metal solid stitching stripe_badge suede tail_light tail_light2 tail_light_lod0 tail_light_on textured_reflector tire " +
            "tire_V2 tread undercarriage unicolor_carbon_fiber weatherstrip wheel_black wheel_emblem wheel_emblem_V2 white_brake window " +
            "window_2 wood";

        // FH1 / FH2 / FM3 materials FM4 has no shader for -> the 8 most similar FM4 materials with their score
        // (names with the same shaders count once: tail_light stands for tail_light2, _on and _lod0).
        private static readonly Dictionary<string, string> Similar = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "body_traffic", "blur_lip:48 inner_rim:48 rim:48 wheel_emblem:48 blur_lip_V2:47 inner_rim_V2:47 rim_V2:47 wheel_emblem_V2:47" },
            { "brinl", "tail_light:74 reflector:72 head_light:66 reverse_light:66 white_brake:63 lights_glass:62 head_light2:57 textured_reflector:56" },
            { "brinr", "tail_light:74 reflector:72 head_light:66 reverse_light:66 white_brake:63 lights_glass:62 head_light2:57 textured_reflector:56" },
            { "brkreverse", "tail_light:75 reflector:72 reverse_light:71 head_light:66 white_brake:63 lights_glass:62 head_light2:57 textured_reflector:57" },
            { "carbon_fiber_2", "carbon_fiber:56 unicolor_carbon_fiber:51 embossed_plastic:42 carbon_fiber_aniso:41 carbon_fiber_no_env:40 matte:40 rubber_trim:40 mirrorLeft:37" },
            { "carbon_fiber_3", "carbon_fiber:56 unicolor_carbon_fiber:51 embossed_plastic:42 carbon_fiber_aniso:41 carbon_fiber_no_env:40 matte:40 rubber_trim:40 mirrorLeft:37" },
            { "drlbrakeorange", "reflector:72 tail_light:70 head_light:66 reverse_light:66 white_brake:63 lights_glass:62 head_light2:57 textured_reflector:56" },
            { "drlbrakered", "reflector:72 tail_light:70 head_light:66 reverse_light:66 white_brake:63 lights_glass:62 head_light2:57 textured_reflector:56" },
            { "drlinlorange", "reflector:72 tail_light:70 head_light:66 reverse_light:66 white_brake:63 lights_glass:62 head_light2:57 textured_reflector:56" },
            { "drlinlred", "reflector:72 tail_light:70 head_light:66 reverse_light:66 white_brake:63 lights_glass:62 head_light2:57 textured_reflector:56" },
            { "drlinlwhite", "reflector:72 tail_light:70 head_light:66 reverse_light:66 white_brake:63 lights_glass:62 head_light2:57 textured_reflector:56" },
            { "drlinrorange", "reflector:72 tail_light:70 head_light:66 reverse_light:66 white_brake:63 lights_glass:62 head_light2:57 textured_reflector:56" },
            { "drlinrred", "reflector:72 tail_light:70 head_light:66 reverse_light:66 white_brake:63 lights_glass:62 head_light2:57 textured_reflector:56" },
            { "drlinrwhite", "reflector:72 tail_light:70 head_light:66 reverse_light:66 white_brake:63 lights_glass:62 head_light2:57 textured_reflector:56" },
            { "drlorange", "reflector:74 tail_light:72 head_light:68 reverse_light:68 white_brake:65 lights_glass:64 head_light2:59 textured_reflector:58" },
            { "drlred", "reflector:74 tail_light:72 head_light:68 reverse_light:68 white_brake:65 lights_glass:64 head_light2:59 textured_reflector:58" },
            { "drlwhite", "reflector:74 tail_light:72 head_light:68 reverse_light:68 white_brake:65 lights_glass:64 head_light2:59 textured_reflector:58" },
            { "drlwhiteswitch", "reflector:74 tail_light:72 head_light:68 reverse_light:68 lights_glass:64 white_brake:64 head_light2:58 textured_reflector:58" },
            { "fogred", "reflector:74 tail_light:72 head_light:68 reverse_light:68 white_brake:65 lights_glass:64 head_light2:59 textured_reflector:58" },
            { "fogwhite", "reflector:74 tail_light:72 head_light:68 reverse_light:68 white_brake:65 lights_glass:64 head_light2:59 textured_reflector:58" },
            { "fullbeam", "reflector:74 tail_light:72 head_light:68 reverse_light:68 white_brake:65 lights_glass:64 head_light2:59 textured_reflector:58" },
            { "gauge_emissive_alpha", "gauge_emissive:68 plastic:65 mottled:63 gauge_emissive_unshadowed:58 gauge_needle:58 rubber:57 solid:57 metal:50" },
            { "hidhead", "reflector:74 tail_light:72 head_light:68 reverse_light:68 white_brake:65 lights_glass:64 head_light2:59 textured_reflector:58" },
            { "indicator_left", "reflector:74 tail_light:72 head_light:68 reverse_light:68 white_brake:65 lights_glass:64 head_light2:59 textured_reflector:58" },
            { "indicator_right", "reflector:74 tail_light:72 head_light:68 reverse_light:68 white_brake:65 lights_glass:64 head_light2:59 textured_reflector:58" },
            { "lights_gls_hidhead", "reflector:65 tail_light:61 lights_glass:58 reverse_light:57 head_light:55 white_brake:54 inner_rim:51 blur_lip:50" },
            { "lights_gls_indicator_left", "reflector:65 tail_light:61 lights_glass:58 reverse_light:57 head_light:55 white_brake:54 inner_rim:51 blur_lip:50" },
            { "lights_gls_indicator_right", "reflector:65 tail_light:61 lights_glass:58 reverse_light:57 head_light:55 white_brake:54 inner_rim:51 blur_lip:50" },
            { "lights_gls_noemit", "reflector:68 tail_light:63 lights_glass:62 reverse_light:60 head_light:58 white_brake:56 inner_rim:52 blur_lip:51" },
            { "lights_gls_oldhead", "reflector:65 tail_light:61 lights_glass:58 reverse_light:57 head_light:55 white_brake:54 inner_rim:51 blur_lip:50" },
            { "lights_gls_reverse_light", "reflector:65 tail_light:61 reverse_light:60 lights_glass:58 head_light:55 white_brake:54 inner_rim:51 blur_lip:50" },
            { "lights_gls_tail_light", "reflector:65 tail_light:64 lights_glass:58 reverse_light:57 head_light:55 white_brake:54 inner_rim:51 blur_lip:50" },
            { "lights_gls_taillight2S", "reflector:62 tail_light:61 lights_glass:55 reverse_light:55 head_light:52 white_brake:52 blur_lip:50 inner_rim:50" },
            { "lights_gls_taillightst", "reflector:65 tail_light:61 lights_glass:58 reverse_light:57 head_light:55 white_brake:54 inner_rim:51 blur_lip:50" },
            { "lindtailside", "tail_light:70 reflector:67 reverse_light:63 head_light:61 white_brake:59 lights_glass:57 head_light2:53 textured_reflector:53" },
            { "linred", "reflector:74 tail_light:72 head_light:68 reverse_light:68 white_brake:65 lights_glass:64 head_light2:59 textured_reflector:58" },
            { "numberplate", "interior:44 tail_light:44 defrost_lines:43 matte:43 blur_lip:42 inner_rim:42 reverse_light:42 rubber_trim:42" },
            { "numplate", "reflector:74 tail_light:72 head_light:68 reverse_light:68 white_brake:65 lights_glass:64 head_light2:59 textured_reflector:58" },
            { "oldhead", "reflector:74 tail_light:72 head_light:68 reverse_light:68 white_brake:65 lights_glass:64 head_light2:59 textured_reflector:58" },
            { "rindtailside", "tail_light:70 reflector:67 reverse_light:63 head_light:61 white_brake:59 lights_glass:57 head_light2:53 textured_reflector:53" },
            { "rinred", "reflector:74 tail_light:72 head_light:68 reverse_light:68 white_brake:65 lights_glass:64 head_light2:59 textured_reflector:58" },
            { "slinlorange", "reflector:72 tail_light:70 head_light:66 reverse_light:66 white_brake:63 lights_glass:62 head_light2:57 textured_reflector:56" },
            { "slinrorange", "reflector:72 tail_light:70 head_light:66 reverse_light:66 white_brake:63 lights_glass:62 head_light2:57 textured_reflector:56" },
            { "slorange", "reflector:74 tail_light:72 head_light:68 reverse_light:68 white_brake:65 lights_glass:64 head_light2:59 textured_reflector:58" },
            { "taillight2S", "tail_light:73 reflector:70 reverse_light:65 head_light:64 white_brake:62 lights_glass:61 head_light2:56 textured_reflector:55" },
            { "taillightst", "reflector:74 tail_light:72 head_light:68 reverse_light:68 white_brake:65 lights_glass:64 head_light2:59 textured_reflector:58" },
            { "window_traffic", "glass:57 window:57 black_glass_breakable:56 glass_2:54 lights_glass:52 black_glass:51 head_light:48 head_light2:46" },
            { "window_traffic_dark", "glass:57 window:57 black_glass_breakable:56 glass_2:54 lights_glass:52 black_glass:51 head_light:48 head_light2:46" },
            { "xenonhead", "reflector:74 tail_light:72 head_light:68 reverse_light:68 white_brake:65 lights_glass:64 head_light2:59 textured_reflector:58" },
            { "z_gauge_emissive_alpha", "gauge_emissive:68 plastic:65 mottled:63 gauge_emissive_unshadowed:58 gauge_needle:58 rubber:57 solid:57 metal:50" },
        };
    }
}
