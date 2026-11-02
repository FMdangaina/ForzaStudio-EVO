namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.RegularExpressions;

    // A car's shadersettings.xml: per-car values for the parameters of its shader techniques.
    // <ShaderSettings><technique><Param value="..."/>...</technique>...</ShaderSettings>
    // The element name is the technique: a main or lod0 model's material name as it is, a cockpit
    // model's material with "cockpit_" in front (FM4 MP4-12C: cockpit_bump_leather2 sets the cockpit's
    // bump_leather2). Measured on the 317 FM4 cars (fm4dev\Traffic\ss_fm4):
    //  - 61 have no shadersettings.xml: every material then runs with its shader's defaults;
    //  - 41 list techniques none of their models use (frontwindow, detail_glass_red...): harmless;
    //  - which parameters each technique gets in FM4: Fm4Parameters below (names only).
    // So a material without an entry is normal; an entry follows its material when that is renamed.
    public class ShaderSettings
    {
        public class Parameter
        {
            public string Name;
            public string Xml;     // the whole element, e.g. <GlassColor r="0.6" g="0.6" b="0.6" a="0.0"/>
        }

        public class Entry
        {
            public string Name;
            public List<Parameter> Parameters = new List<Parameter>();

            public Entry Copy(string name)
            {
                Entry copy = new Entry();
                copy.Name = name;
                foreach (Parameter p in this.Parameters)
                {
                    copy.Parameters.Add(new Parameter { Name = p.Name, Xml = p.Xml });
                }
                return copy;
            }
        }

        public List<Entry> Entries = new List<Entry>();
        private byte[] preamble = new byte[0];
        // Text after </ShaderSettings>, written back as it is: FM4's DOD_126_ViperGTSR_03 has a
        // lights_glass element there (outside the root, so probably ignored by the game).
        private string trailer = "";

        public static ShaderSettings Parse(byte[] data)
        {
            ShaderSettings settings = new ShaderSettings();
            int skip = 0;
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            {
                settings.preamble = new byte[] { 0xEF, 0xBB, 0xBF };
                skip = 3;
            }
            settings.ParseText(Encoding.UTF8.GetString(data, skip, data.Length - skip));
            return settings;
        }

        public static ShaderSettings Parse(string text)
        {
            ShaderSettings settings = new ShaderSettings();
            settings.ParseText(text);
            return settings;
        }

        private void ParseText(string text)
        {
            Match root = Regex.Match(text, "<ShaderSettings>(.*)</ShaderSettings>\\s*", RegexOptions.Singleline);
            if (root.Success && root.Index + root.Length < text.Length)
            {
                this.trailer = text.Substring(root.Index + root.Length);
            }
            foreach (Match section in Regex.Matches(root.Success ? root.Groups[1].Value : text, "<(\\w+)>(.*?)</\\1>", RegexOptions.Singleline))
            {
                Entry entry = new Entry();
                entry.Name = section.Groups[1].Value;
                foreach (Match value in Regex.Matches(section.Groups[2].Value, "<(\\w+)\\b[^>]*/>"))
                {
                    entry.Parameters.Add(new Parameter { Name = value.Groups[1].Value, Xml = value.Value });
                }
                this.Entries.Add(entry);
            }
        }

        // The layout the port writes (and FM4 files use): two-space indent, CRLF.
        public byte[] ToBytes()
        {
            StringBuilder xml = new StringBuilder("<ShaderSettings>\r\n");
            foreach (Entry entry in this.Entries)
            {
                xml.Append("  <" + entry.Name + ">\r\n");
                foreach (Parameter p in entry.Parameters)
                {
                    xml.Append("    " + p.Xml + "\r\n");
                }
                xml.Append("  </" + entry.Name + ">\r\n");
            }
            xml.Append("</ShaderSettings>\r\n");
            if (this.trailer.Length > 0)
            {
                xml.Append("\r\n").Append(this.trailer);
            }
            byte[] text = Encoding.UTF8.GetBytes(xml.ToString());
            byte[] data = new byte[this.preamble.Length + text.Length];
            Buffer.BlockCopy(this.preamble, 0, data, 0, this.preamble.Length);
            Buffer.BlockCopy(text, 0, data, this.preamble.Length, text.Length);
            return data;
        }

        public Entry Find(string name)
        {
            return this.Entries.Find(delegate(Entry e) { return e.Name == name; });
        }

        // The technique a material of the given model file runs with.
        public static string TechniqueName(string material, string carbinName)
        {
            return carbinName.EndsWith("_cockpit.carbin", StringComparison.OrdinalIgnoreCase) ? "cockpit_" + material : material;
        }

        // The material (subsection) names of a model of any of the four games: FM2 through the viewer's reader, FM3 as
        // "study" files, FM4 / Horizon as they are.
        public static List<string> MaterialNames(byte[] data, string name)
        {
            List<string> names = new List<string>();
            if (CarModelFolder.IsMotorsport2(data))
            {
                using (ForzaCarBin bin = new ForzaCarBin(name, data))
                {
                    foreach (ForzaCarSection section in bin.Sections)
                    {
                        foreach (ForzaCarSubSection sub in section.SubSections)
                        {
                            names.Add(sub.Name);
                        }
                    }
                }
                return names;
            }
            CarbinModel model = CarbinModel.IsMotorsport3File(data) ? new CarbinModel(data, true) : new CarbinModel(data);
            foreach (CarbinModel.Section section in model.Sections)
            {
                foreach (CarbinModel.SubSection sub in section.SubSections)
                {
                    names.Add(sub.Name);
                }
            }
            return names;
        }

        // Technique -> model files using it, over every .carbin of the car folder (main, lod0, cockpit,
        // brakes). Files that cannot be read are skipped.
        public static SortedDictionary<string, List<string>> Techniques(ForzaZip zip, string folder)
        {
            SortedDictionary<string, List<string>> used = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (ForzaZip.Entry entry in zip.Entries)
            {
                if (!entry.Name.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase) || !entry.Name.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
                    || entry.Name.IndexOf('/', folder.Length) >= 0)
                {
                    continue;
                }
                List<string> materials;
                try
                {
                    materials = MaterialNames(zip.Read(entry), entry.Name);
                }
                catch (Exception)
                {
                    continue;
                }
                string file = entry.Name.Substring(entry.Name.LastIndexOf('/') + 1);
                {
                    foreach (string material in materials)
                    {
                        string technique = TechniqueName(material, entry.Name);
                        List<string> files;
                        if (!used.TryGetValue(technique, out files))
                        {
                            files = new List<string>();
                            used[technique] = files;
                        }
                        if (!files.Contains(file))
                        {
                            files.Add(file);
                        }
                    }
                }
            }
            return used;
        }

        // After a material rename in the models: the entry of 'from' goes to 'to' (both technique names).
        // 'to' keeps its own entry if it has one. If 'from' is still used somewhere its entry is copied,
        // otherwise renamed. Returns what was done, or null when there was nothing to do.
        public string FollowRename(string from, string to, bool fromStillUsed)
        {
            Entry source = this.Find(from);
            if (source == null || from == to)
            {
                return null;
            }
            if (this.Find(to) != null)
            {
                return string.Format("{0} keeps its own settings ({1} has others)", to, from);
            }
            if (fromStillUsed)
            {
                this.Entries.Insert(this.Entries.IndexOf(source) + 1, source.Copy(to));
                return string.Format("{0} settings copied to {1} ({0} is still used)", from, to);
            }
            source.Name = to;
            return string.Format("{0} -> {1}", from, to);
        }

        // The attributes of a parameter element in order: ("value", "0.8") or r, g, b, a.
        public static List<string[]> Attributes(string xml)
        {
            List<string[]> attributes = new List<string[]>();
            foreach (Match m in Regex.Matches(xml, "(\\w+)=\"([^\"]*)\""))
            {
                attributes.Add(new string[] { m.Groups[1].Value, m.Groups[2].Value });
            }
            // Colours always come back as r g b a, whatever order the file wrote them in (some ports have a b g r);
            // saving an edited parameter then writes them in the FM4 order. Other attributes keep their order.
            string order = "rgba";
            List<string[]> sorted = new List<string[]>();
            for (int rank = 0; rank < 4; rank++)
            {
                string[] found = attributes.Find(delegate(string[] a) { return a[0] == order[rank].ToString(); });
                if (found != null)
                {
                    sorted.Add(found);
                }
            }
            foreach (string[] a in attributes)
            {
                if (!sorted.Contains(a))
                {
                    sorted.Add(a);
                }
            }
            return sorted;
        }

        public static string BuildXml(string name, List<string[]> attributes)
        {
            StringBuilder xml = new StringBuilder("<" + name);
            foreach (string[] a in attributes)
            {
                xml.Append(" " + a[0] + "=\"" + a[1] + "\"");
            }
            return xml.Append("/>").ToString();
        }

        // The attribute names a new parameter gets, as on FM4 cars: colours r g b a, the rest one value.
        // Samplers (LightsSampler, DamageLightsSampler) are not offered.
        public static string[] AttributeNames(string parameter)
        {
            return parameter.Contains("Color") ? new string[] { "r", "g", "b", "a" } : new string[] { "value" };
        }

        public static bool IsSampler(string parameter)
        {
            return parameter.EndsWith("Sampler", StringComparison.Ordinal);
        }

        public enum Problem
        {
            None,
            UnusedEntry,        // no model uses the technique: harmless (41 FM4 cars have some)
            UnknownParameter,   // no FM4 car sets this parameter at all
            UnusualParameter,   // FM4 sets it, but never on this technique
            NoEntry,            // a technique the models use without settings: shader defaults
        }

        public static Problem Check(Entry entry, IDictionary<string, List<string>> used)
        {
            return used.ContainsKey(entry.Name) ? Problem.None : Problem.UnusedEntry;
        }

        // 'known' = the names the game's own shader files hold (ShaderTextures.ParameterNames), null/empty when
        // they could not be read: then the survey list is used. Only a name no shader of the game knows is an
        // error; one the survey never saw on this technique is just unusual (the game's shaders accept it).
        public static Problem Check(string technique, Parameter parameter, ICollection<string> known)
        {
            return Check(ShaderProfile.Fm4, technique, parameter, known);
        }

        // The same for the cars of another game: a name its own cars set (ShaderProfiles) is never an error and the
        // "unusual" test uses the parameters its cars set on the technique.
        public static Problem Check(ShaderProfile profile, string technique, Parameter parameter, ICollection<string> known)
        {
            // A name some stock FM4 car sets (CarPort.ShaderParameters) is never an error, even where the shader
            // files do not list it (CockpitMetalPaintScale, CubeMultiplier, a typo in VOL_C30): the game ships it.
            bool exists = CarPort.IsShaderParameter(parameter.Name) || ShaderProfiles.IsParameter(profile, parameter.Name)
                || (known != null && (known.Contains(parameter.Name) || parameter.Name.StartsWith("D3DRS_", StringComparison.Ordinal)));
            if (!exists)
            {
                return Problem.UnknownParameter;
            }
            string[] seen = ShaderProfiles.ParametersOf(profile, technique);
            return (seen != null && Array.IndexOf(seen, parameter.Name) < 0) ? Problem.UnusualParameter : Problem.None;
        }

        // The parameters FM4 cars set on a technique, null when no FM4 car lists it.
        public static string[] Fm4Parameters(string technique)
        {
            string list;
            return Fm4Table.TryGetValue(technique, out list) ? list.Split(' ') : null;
        }

        // Every technique FM4 cars' shadersettings.xml list, with the parameters they set on it (names
        // only: 256 cars with the file, fm4dev\Traffic\tools\sssurvey + ssgen).
        private static readonly Dictionary<string, string> Fm4Table = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "badge", "FresnelIndex FresnelScalar PaintScale SpecularAmount SpecularPower UseDiffuseInsteadOfSH" },
            { "badge2", "FresnelIndex FresnelScalar PaintScale" },
            { "black_glass", "FresnelIndex FresnelScalar" },
            { "blur_rim", "SpecularColor" },
            { "body", "SpecularAmount2 SpecularAmount3 SpecularCubeScalar" },
            { "body2", "SpecularAmount2 SpecularAmount3 SpecularCubeScalar" },
            { "body_2", "MinShadowValue" },
            { "body_3", "FresnelScalar" },
            { "caliper", "PaintScale SHLowerScale ShadowScale" },
            { "carbon_fiber", "CarbonFiberDiffuseColor CarbonFiberDiffuseColor2 CarbonFiberScale CarbonFiberSpecularColor2 CarbonFiberSpecularPower2 FresnelIndex FresnelScalar" },
            { "chrome", "ChromeDiffuseScale ChromeMaxDiffuseShadowAmt ChromeMaxReflectionShadowAmt ChromeUseSHLerp CubeScale MinDiffuse MinShadowValue UseDiffuseInsteadOfSH" },
            { "chrome_2", "ChromeDiffuseScale ChromeUseSHLerp CubeScale UseDiffuseInsteadOfSH" },
            { "chrome_3", "ChromeDiffuseScale ChromeUseSHLerp CubeScale MinDiffuse UseDiffuseInsteadOfSH" },
            { "chrome_rim", "ChromeDiffuseScale CubeScale" },
            { "cockpit_badge", "FresnelIndex FresnelScalar MinCockpitShadow PaintScale" },
            { "cockpit_bump_leather", "CockpitBumpBias CockpitBumpScale CockpitNormalScale CockpitPaintScale CockpitSpecularAmount CockpitSpecularAmount2 CockpitSpecularPower CockpitSpecularPower2 MinCockpitShadow MinCockpitSpecular MinShadowValue SHLowerScale" },
            { "cockpit_bump_leather2", "CockpitBumpBias CockpitBumpScale CockpitNormalScale CockpitPaintScale CockpitSpecularAmount CockpitSpecularAmount2 CockpitSpecularPower CockpitSpecularPower2 MinCockpitShadow SHLowerScale" },
            { "cockpit_bump_leather3", "CockpitBumpScale CockpitNormalScale CockpitPaintScale CockpitSpecularAmount2 CockpitSpecularPower CockpitSpecularPower2 MinCockpitSpecular" },
            { "cockpit_bump_plastic", "CockpitBumpBias CockpitBumpScale CockpitNormalScale CockpitPaintScale CockpitSpecularAmount CockpitSpecularAmount2 CockpitSpecularPower CockpitSpecularPower2 MinCockpitShadow MinCockpitSpecular MinShadowValue SHLowerScale SHTransition SHTransitionWidth SHUpperScale" },
            { "cockpit_bump_plastic2", "CockpitBumpBias CockpitBumpScale CockpitNormalScale CockpitPaintScale CockpitSpecularAmount CockpitSpecularAmount2 CockpitSpecularPower CockpitSpecularPower2 MinCockpitShadow" },
            { "cockpit_bump_plastic3", "CockpitBumpScale CockpitNormalScale CockpitPaintScale CockpitSpecularAmount2" },
            { "cockpit_bump_stitching", "CockpitBumpScale CockpitNormalScale CockpitPaintScale CockpitSpecularAmount CockpitSpecularAmount2 CockpitSpecularPower2 MinCockpitShadow SHLowerScale StitchColor" },
            { "cockpit_carbon_fiber", "CarbonFiberDiffuseColor CarbonFiberDiffuseColor2 CarbonFiberMipBias CarbonFiberScale CarbonFiberSpecularColor2 CarbonFiberSpecularPower2 FresnelIndex FresnelScalar MinCockpitShadow MinShadowValue UseDiffuseInsteadOfSH" },
            { "cockpit_carbonfiber", "CarbonFiberDiffuseColor CarbonFiberDiffuseColor2 CarbonFiberSpecularColor2" },
            { "cockpit_chrome", "ChromeAmbientScale ChromeDiffuseScale CockpitSpecularAmount CockpitSpecularPower CubeScale MinShadowValue SHLowerScale SHUpperScale" },
            { "cockpit_cloth", "MinCockpitShadow PaintScale SHLowerScale" },
            { "cockpit_gauge_emissive", "CockpitPlasticPaintScale GaugeAmbientAmt GaugeEmissiveScale" },
            { "cockpit_gauge_needle", "GaugeEmissiveScale MinDiffuse UseDiffuseInsteadOfSH" },
            { "cockpit_glass", "GlassColor GlassMaxOpacity" },
            { "cockpit_headliner", "CockpitHeadlinerColor MinCockpitShadow" },
            { "cockpit_leather", "CockpitSpecularAmount CockpitSpecularAmount2 CockpitSpecularPower CockpitSpecularPower2 MinCockpitShadow PaintScale" },
            { "cockpit_metal", "CockpitMetalPaintScale CockpitSpecularAmount CockpitSpecularAmount2 CockpitSpecularPower CockpitSpecularPower2 CubeScale DynamicEnvLightmapDampner FresnelIndex FresnelScalar MinCockpitShadow MinCockpitSpecular MinShadowValue PaintScale SHLowerScale SHUpperScale" },
            { "cockpit_metal2", "CockpitSpecularPower CubeScale PaintScale" },
            { "cockpit_mottled", "CockpitPlasticPaintScale CockpitSpecularAmount CockpitSpecularAmount2 CockpitSpecularPower CockpitSpecularPower2 MinCockpitShadow MinCockpitSpecular SHLowerScale SHUpperScale" },
            { "cockpit_plastic", "CockpitPaintScale CockpitPalsticPaintScale CockpitPlasticPaintScale CockpitSpecularAmount CockpitSpecularAmount2 CockpitSpecularPower CockpitSpecularPower2 MinCockpitShadow MinCockpitSpecular MinShadowValue PaintScale SHLowerScale SHTransition SHTransitionWidth SHUpperScale" },
            { "cockpit_reflector", "CockpitSpecularAmount CockpitSpecularAmount2 FresnelIndex FresnelScalar MinCockpitShadow PaintScale" },
            { "cockpit_solid", "PaintScale" },
            { "cockpit_suede", "DiffuseColorMultiply MinCockpitShadow MottleDark MottleLight MottleTile SpecularColor" },
            { "cockpit_wood", "FresnelIndex FresnelScalar MinCockpitShadow PaintScale SHLowerScale" },
            { "detail_glass_amber", "CubeScale Direct FresnelMax FresnelMin FresnelScale Glancing GlassColor GlassMaxOpacity" },
            { "detail_glass_clear", "CubeScale Direct FresnelBiasA FresnelPower FresnelScale Glancing GlassColor GlassMaxOpacity" },
            { "detail_glass_red", "CubeScale DarkValues Direct FresnelBiasA FresnelMax FresnelMin FresnelScale Glancing GlassColor GlassMaxOpacity LightValues UV1Tile1" },
            { "detail_glass_simple", "CubeScale GlassColor" },
            { "emblem", "CubeScale FresnelIndex FresnelScalar FresnelScale MinDiffuse PaintColor PaintScale SpecularAmount SpecularPower UseDiffuseInsteadOfSH" },
            { "frame", "PristinePaintScale SpecularAmount SpecularColor SpecularPower" },
            { "frontwindow", "GlassColor" },
            { "glass", "FresnelIndex FresnelScalar GlassColor GlassMaxOpacity SpecularAmount" },
            { "grille1", "GrilleColor GrilleScale" },
            { "grille1_alpha", "GrilleColor GrilleScale" },
            { "grille2", "GrilleColor GrilleScale" },
            { "grille2_alpha", "GrilleColor GrilleScale UseDiffuseInsteadOfSH" },
            { "head_light", "CubeScale FresnelIndex FresnelScalar GlassMaxOpacity PaintScale" },
            { "head_light2", "PaintScale" },
            { "headlight", "CubeScale PaintScale" },
            { "headlight_paint", "D3DRS_DepthBias" },
            { "interior", "PristinePaintScale" },
            { "lights_glass", "FresnelIndex FresnelScalar GlassColor GlassMaxOpacity PaintScale SpecularAmount" },
            { "matte_colors", "DirtCylinderHeight DirtOffsetU DirtOffsetV DirtSpecularKillAmount PaintColor PaintScale SHLowerScale SHTransition SHTransitionWidth SHUpperScale SpecularAmount SpecularColor SpecularPower UseDiffuseInsteadOfSH" },
            { "misc", "FresnelIndex FresnelScalar MinShadowValue PaintScale SpecularAmount SpecularColor SpecularPower UseDiffuseInsteadOfSH" },
            { "plastic2", "MinShadowValue PaintScale PristinePaintScale SpecularAmount SpecularColor SpecularPower UseDiffuseInsteadOfSH" },
            { "red_glass", "FresnelIndex FresnelScalar GlassMaxOpacity" },
            { "reflector", "CubeScale FresnelIndex FresnelScalar GlassColor GlassMaxOpacity PaintScale SpecularAmount SpecularPower" },
            { "reverse_light", "AmbientColor CubeScale D3DRS_DepthBias D3DRS_SlopeScaleDepthBias DynamicEnvLightmapDampner FresnelIndex FresnelScalar FresnelScale GlassMaxOpacity LightsSampler PaintScale ReverseLightColor SpecularAmount SpecularPower StaticEnvLightmapDampner" },
            { "reverse_light_LOD0", "ReverseLightColor" },
            { "reverse_light_lod0", "AmbientColor CubeScale D3DRS_DepthBias D3DRS_SlopeScaleDepthBias DynamicEnvLightmapDampner FresnelIndex FresnelScalar GlassMaxOpacity LightsSampler PaintScale ReverseLightColor SpecularAmount SpecularPower StaticEnvLightmapDampner" },
            { "rotor", "PaintScale ShadowScale SpecularAmount SpecularPower" },
            { "rubber_trim", "UseDiffuseInsteadOfSH" },
            { "solid", "PaintScale" },
            { "tail_light", "AmbientColor BrakeColor CubeMultiplier CubeScale D3DRS_DepthBias D3DRS_SlopeScaleDepthBias DamageLightsSampler DirtCylinderHeight DirtOffsetU DirtOffsetV DirtSpecularKillAmount DynamicEnvLightmapDampner FresnelIndex FresnelMax FresnelMin FresnelPower FresnelScalar FresnelScale GlassMaxOpacity LightsSampler PaintScale SpecularAmount SpecularColor SpecularPower StaticEnvLightmapDampner" },
            { "tail_light2", "BrakeColor PaintScale" },
            { "tail_light_2", "BrakeColor" },
            { "tail_light_LOD0", "BrakeColor CubeScale PaintScale" },
            { "tail_light_lod0", "AmbientColor BrakeColor CubeScale D3DRS_DepthBias D3DRS_SlopeScaleDepthBias DamageLightsSampler DynamicEnvLightmapDampner FresnelIndex FresnelScalar FresnelScale GlassMaxOpacity LightsSampler PaintScale SpecularAmount SpecularPower StaticEnvLightmapDampner" },
            { "texture_reflector", "GlassColor" },
            { "textured_reflector", "AmbientColor CubeScale FresnelIndex FresnelScalar FresnelScale GlassMaxOpacity PaintScale SpecularAmount SpecularPower" },
            { "weatherstrip", "PaintScale PristinePaintScale SpecularAmount SpecularPower" },
            { "window", "FresnelIndex" },
        };
    }
}
