namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Xml;

    // Which texture files a car material draws with, the way the game finds them (verified on 10 FM4 and
    // 10 FH1 traffic cars, 0 missing; fm4dev\Shaders\FINDINGS.md "Textures per material"):
    //  - the material is a shader technique (cockpit model: "cockpit_" + name first);
    //  - the technique's pixel shaders read samplers, named in their D3D constant tables (register set s)
    //    inside GAME:\Media\Shaders\Cars\*.fxobj (race library + single-material files);
    //  - GAME:\Media\cars\Shared.zip\textures.xml maps each sampler to a file in the car's folder
    //    (%CARFOLDER%), in Shared.zip (%SHAREDCARFOLDER%) or the wheel's folder, by loadFlags: LOD15 =
    //    the main model (LODs 1-5), LOD0 = the close-up files, Cockpit, SuperLOD = showroom textures;
    //  - baseSampler and damageSampler are built in: nodamage / damage, _LOD0 for the close-up files.
    // One table per game folder, read once.
    public sealed class ShaderTextures
    {
        public enum Kind
        {
            Main,       // LODs 1-5 of the main .carbin
            Lod0,       // LOD 0 (_lod0.carbin, a wheel's LOD 0)
            Cockpit,    // _cockpit.carbin
        }

        public enum Place
        {
            Car,
            Shared,
            Wheel,
        }

        public sealed class Texture
        {
            public string Sampler;
            public string File;
            public Place Where;
        }

        private sealed class Definition
        {
            public string File;
            public string Path;
            public string Flags;
        }

        // The samplers whose texture is the colour the viewer shows, best first. The others are normal,
        // occlusion, dirt and damage maps, drawn over the colour by the game. A material's own texture
        // (undercarriage, bumper_frame, grilles... most in Shared.zip) comes before the car's atlas
        // (baseSampler = nodamage), which those techniques also read (user 2026-10-20: undercarriage and
        // bumper_frame showed nodamage). NumplateSampler comes after it: FH1 `body` reads both.
        private static readonly string[] ColourSamplers =
        {
            "UndercarriageSampler", "BumperFrameSampler",
            "Grille1Sampler", "Grille2Sampler", "Grille1DiffuseSampler", "Grille2DiffuseSampler", "Grille3DiffuseSampler",
            "CarbonFiberDiffuseSampler", "CockpitWoodSampler", "EngineDiffuseSampler",
            "BrakeDiffuseSampler", "WheelDiffuseSampler", "Badge2Sampler", "ExteriorMetalSampler", "GaugesPlasticSuperSampler",
            "ClothSuperSampler", "Cloth2SuperSampler", "Cloth3SuperSampler", "HeadlinerSuperSampler", "Headliner2SuperSampler",
            "MottledSampler", "Mottled2Sampler", "Mottled3Sampler",
            "CockpitInteriorHiSampler", "LightsSampler", "baseSampler", "NumplateSampler", "DamageGlassSampler",
        };

        private static readonly Dictionary<string, ShaderTextures> cache = new Dictionary<string, ShaderTextures>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, List<string>> techniques = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> constants = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Definition>> definitions = new Dictionary<string, List<Definition>>(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> parameterNames = new HashSet<string>(StringComparer.Ordinal);

        // Every parameter / sampler / technique name in the game's car shader files, plus every constant their
        // shaders declare: a name shadersettings.xml can set. Empty when the shaders could not be read.
        public ICollection<string> ParameterNames
        {
            get
            {
                HashSet<string> all = new HashSet<string>(this.parameterNames, StringComparer.Ordinal);
                foreach (List<string> list in this.constants.Values)
                {
                    all.UnionWith(list);
                }
                return all;
            }
        }

        public string SharedZipPath { get; private set; }

        public string Problem { get; private set; }

        // The table of a game folder (the one with Media), or one whose Problem says what is missing.
        public static ShaderTextures For(string gameRoot)
        {
            string key = gameRoot ?? "";
            ShaderTextures table;
            if (!cache.TryGetValue(key, out table))
            {
                table = new ShaderTextures(gameRoot);
                cache[key] = table;
            }
            return table;
        }

        private ShaderTextures(string root)
        {
            if (root == null)
            {
                this.Problem = "the model is not inside a game folder (one with Media)";
                return;
            }
            string media = Path.Combine(root, "Media");
            string shaders = Path.Combine(Path.Combine(media, "Shaders"), "Cars");
            this.SharedZipPath = Path.Combine(Path.Combine(media, "cars"), "Shared.zip");
            try
            {
                // FM2 and FM3: loose Media\cars\Shared folder, no textures.xml (the table is in default.xex).
                bool motorsport3 = !File.Exists(this.SharedZipPath) && Directory.Exists(Path.Combine(Path.Combine(media, "cars"), "Shared"));
                // FM2 has no Media\Shaders either: its car effect is inside default.xex.
                if (motorsport3 && !Directory.Exists(shaders))
                {
                    this.AddMotorsport2();
                    return;
                }
                if (!Directory.Exists(shaders) || (!File.Exists(this.SharedZipPath) && !motorsport3))
                {
                    this.Problem = "no Media\\Shaders\\Cars or Media\\cars\\Shared.zip in " + root;
                    return;
                }
                string[] files = Directory.GetFiles(shaders, "*.fxobj");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                // The race library first, then the single-material files; the other libraries (16 cars,
                // split screen, UI, SLOD, debug) repeat the same techniques.
                foreach (string file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                    if (name.Contains("_debug") || name.Contains("16car") || name.Contains("slod") || name.Contains("splitscreen") || name.Contains("_ui_"))
                    {
                        continue;
                    }
                    try
                    {
                        EffectReader reader = new EffectReader(File.ReadAllBytes(file));
                        reader.Read(this.techniques, this.constants);
                        reader.ReadNames(this.parameterNames);
                    }
                    catch (Exception)
                    {
                        // A file the reader does not understand only loses its techniques.
                    }
                }
                if (motorsport3)
                {
                    this.AddMotorsport3Definitions();
                    return;
                }
                ForzaZip shared = GamePaths.OpenZip(this.SharedZipPath);
                ForzaZip.Entry entry = shared.Find("textures.xml");
                if (entry == null)
                {
                    this.Problem = "Shared.zip has no textures.xml";
                    return;
                }
                string text = Encoding.UTF8.GetString(shared.Read(entry));
                XmlDocument document = new XmlDocument();
                // The game's file has bare '&' in loadFlags ("Cockpit & SuperLOD").
                document.LoadXml(text.Substring(Math.Max(0, text.IndexOf('<'))).Replace("&", "&amp;"));
                foreach (XmlNode node in document.DocumentElement.ChildNodes)
                {
                    XmlElement sampler = node as XmlElement;
                    if (sampler == null)
                    {
                        continue;
                    }
                    List<Definition> list = new List<Definition>();
                    foreach (XmlNode child in sampler.ChildNodes)
                    {
                        XmlElement texture = child as XmlElement;
                        if (texture != null)
                        {
                            list.Add(new Definition { File = texture.GetAttribute("value"), Path = texture.GetAttribute("searchPath"), Flags = texture.GetAttribute("loadFlags") });
                        }
                    }
                    this.definitions[sampler.Name] = list;
                }
            }
            catch (Exception exception)
            {
                this.Problem = exception.Message;
            }
        }

        // FM3 has no textures.xml: its car loader (default.xex 82643B18) names the file of every sampler
        // itself. Car = the car's folder, %SHAREDCARFOLDER% = game:\media\cars\shared. baseSampler and
        // damageSampler are nodamage / damage (+ _LOD0) as in the other games (Resolve).
        private void AddMotorsport3Definitions()
        {
            const string Shared = "%SHAREDCARFOLDER%";
            this.Define("LightsSampler", "lights.xds", "", "LOD15", "lights_LOD0.xds", "", "LOD0");
            this.Define("DamageLightsSampler", "damagelights.xds", "", "LOD15", "damagelights_lod0.xds", "", "LOD0");
            this.Define("HeadlightSampler", "headlight.xpr", Shared, "");
            this.Define("Badge2Sampler", "badge2.xds", "", "");
            this.Define("CockpitInteriorHiSampler", "Interior_LOD0.xds", "", "");
            this.Define("CockpitWoodSampler", "wood.xds", "", "");
            this.Define("CockpitPlasticNormalSampler", "plastic_NRM.xds", Shared, "");
            this.Define("CockpitPlastic2NormalSampler", "plastic2_NRM.xds", Shared, "");
            this.Define("CockpitPlastic3NormalSampler", "plastic3_NRM.xds", Shared, "");
            this.Define("CockpitLeatherNormalSampler", "leather_NRM.xds", Shared, "");
            this.Define("CockpitLeather2NormalSampler", "leather2_NRM.xds", Shared, "");
            this.Define("CockpitLeather3NormalSampler", "leather3_NRM.xds", Shared, "");
            this.Define("CockpitStitchingNormalSampler", "stitches_NRM.xds", Shared, "");
            this.Define("CockpitStaticCubeMapSampler", "staticCube.xpr", Shared, "");
            this.Define("BumperFrameSampler", "bumper_frame.xds", Shared, "");
            this.Define("CarbonFiberSampler", "carbonFiberNormal.xds", Shared, "");
            this.Define("DirtMaskSampler", "dirt_mask.xds", Shared, "");
            this.Define("DirtSampler", "dirt_pattern.xds", Shared, "");
            this.Define("DamageGlassSampler", "glass_texture.xds", Shared, "");
            this.Define("Grille1Sampler", "grille1_alpha.xds", Shared, "");
            this.Define("Grille2Sampler", "grille2_alpha.xds", Shared, "");
            this.Define("UndercarriageSampler", "undercarriage.xds", Shared, "");
        }

        // FM2 keeps everything in default.xex: the car effect (the D3DX effect at file offset 0x66F50 of the
        // decrypted image, read with EffectReader: technique -> samplers below, the `_packed` twins and the
        // debug / shadow techniques left out) and the file of every sampler (car loader 825658A8). One
        // model file per car and no _LOD0 textures.
        private const string Motorsport2Techniques =
            "badge=baseSampler,dirtMaskSampler;badge2=badge2Sampler,dirtMaskSampler;badge_chrome=dirtMaskSampler;black=;" +
            "black_glass=dirtMaskSampler;blur_lip=baseSampler,damageSampler,dirtMaskSampler;" +
            "blur_rim=baseSampler,blurRimSampler,blurRimSampler2,damageSampler,dirtMaskSampler;" +
            "body=baseSampler,damageSampler,dirtMaskSampler,dirtSampler;body_2=baseSampler,damageSampler,dirtMaskSampler,dirtSampler;" +
            "bumper_frame=bumperFrameSampler,dirtMaskSampler;caliper=baseSampler;carbon_fiber=carbonFiberSampler,dirtMaskSampler;" +
            "chrome=dirtMaskSampler;chrome_2=dirtMaskSampler;chrome_blur_lip=dirtMaskSampler;" +
            "chrome_blur_rim=blurRimSampler,blurRimSampler2,dirtMaskSampler;chrome_rim=dirtMaskSampler;cloth=;cloth2=;" +
            "detail_glass_amber=damageLightsSampler,dirtMaskSampler,lightsSampler;" +
            "detail_glass_clear=damageLightsSampler,dirtMaskSampler,lightsSampler;" +
            "detail_glass_red=damageLightsSampler,dirtMaskSampler,lightsSampler;" +
            "dullplastic=baseSampler,damageSampler,dirtMaskSampler;emblem=baseSampler,dirtMaskSampler;" +
            "frame=baseSampler,damageSampler,dirtMaskSampler,dirtSampler;glass=damageGlassSampler,dirtMaskSampler;" +
            "glass_2=damageGlassSampler,dirtMaskSampler;grille1=grille1Sampler;grille2=grille2Sampler;" +
            "head_light=dirtMaskSampler,lightsSampler;head_light2=baseSampler,dirtMaskSampler;hub=baseSampler;" +
            "inner_rim=baseSampler,damageSampler,dirtMaskSampler;interior=baseSampler,damageSampler,dirtMaskSampler,dirtSampler;" +
            "matte=baseSampler,damageSampler,dirtMaskSampler;matte_colors=baseSampler,damageSampler,dirtMaskSampler;" +
            "misc=baseSampler,damageSampler,dirtMaskSampler,dirtSampler;missing=baseSampler,damageSampler,dirtMaskSampler,dirtSampler;" +
            "plastic=baseSampler,damageSampler,dirtMaskSampler,dirtSampler;plastic2=baseSampler,damageSampler,dirtMaskSampler,dirtSampler;" +
            "race_kit_red=baseSampler,damageSampler,dirtMaskSampler,dirtSampler;red_glass=damageGlassSampler,dirtMaskSampler;" +
            "reflector=damageLightsSampler,dirtMaskSampler,lightsSampler;reverse_light=dirtMaskSampler,lightsSampler;" +
            "rim=baseSampler,damageSampler,dirtMaskSampler;rotor=baseSampler;" +
            "rubber_trim=baseSampler,damageSampler,dirtMaskSampler,dirtSampler;rubber_trim_2=baseSampler,damageSampler,dirtMaskSampler,dirtSampler;" +
            "tail_light=dirtMaskSampler,lightsSampler;tail_light_on=dirtMaskSampler,lightsSampler;" +
            "textured_reflector=damageLightsSampler,dirtMaskSampler,lightsSampler;tire=dirtMaskSampler,tireSampler;" +
            "weatherstrip=baseSampler,damageSampler,dirtMaskSampler,dirtSampler;wheel_emblem=baseSampler,damageSampler,dirtMaskSampler;" +
            "white_brake=baseSampler,dirtMaskSampler;window=damageGlassSampler,dirtMaskSampler;window_2=damageGlassSampler,dirtMaskSampler";

        private bool singleModel;      // FM2: no _LOD0 files

        private void AddMotorsport2()
        {
            const string Shared = "%SHAREDCARFOLDER%";
            this.singleModel = true;
            foreach (string item in Motorsport2Techniques.Split(';'))
            {
                int equals = item.IndexOf('=');
                this.techniques[item.Substring(0, equals)] = new List<string>(item.Substring(equals + 1).Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            }
            this.Define("LightsSampler", "lights.xds", "", "");
            this.Define("DamageLightsSampler", "damagelights.xds", "", "");
            this.Define("Badge2Sampler", "badge2.xds", "", "");
            this.Define("BumperFrameSampler", "bumper_frame.xds", Shared, "");
            this.Define("CarbonFiberSampler", "carbonFiberNormal.xds", Shared, "");
            this.Define("DirtMaskSampler", "dirt_mask.xds", Shared, "");
            this.Define("DirtSampler", "dirt_pattern.xds", Shared, "");
            this.Define("DamageGlassSampler", "glass_texture.xds", Shared, "");
            this.Define("Grille1Sampler", "grille1.xds", Shared, "");
            this.Define("Grille2Sampler", "grille2.xds", Shared, "");
        }

        // A sampler's files as (file, searchPath, loadFlags) triples.
        private void Define(string sampler, params string[] entries)
        {
            List<Definition> list = new List<Definition>();
            for (int index = 0; index + 2 < entries.Length; index += 3)
            {
                list.Add(new Definition { File = entries[index], Path = entries[index + 1], Flags = entries[index + 2] });
            }
            this.definitions[sampler] = list;
        }

        // The technique a material draws with in a model file, or null when the game has none (the
        // `missing` technique: black).
        public string TechniqueOf(string material, Kind kind)
        {
            if (kind == Kind.Cockpit && this.techniques.ContainsKey("cockpit_" + material))
            {
                return "cockpit_" + material;
            }
            return this.techniques.ContainsKey(material) ? material : null;
        }

        // The colour texture of a material, best guess first (several when a choice depends on what
        // the car holds): every colour sampler of its technique, mapped to files.
        public List<Texture> ColourTextures(string material, Kind kind)
        {
            List<Texture> result = new List<Texture>();
            string technique = this.TechniqueOf(material, kind);
            // Cockpit file: the interior atlas (CockpitInteriorHiSampler -> Interior_LOD0.xds) is what
            // its parts are painted with. The `interior` material (FH traffic cockpits, a few FM4 ones)
            // takes the body technique (nodamage) but sits on that atlas; and many cockpit techniques
            // (plastic, headliner, mottled, grille...) have only showroom (_s_LOD0) textures, which the
            // cars do not carry (user 2026-10-20: interiors showed no texture). The atlas comes first
            // for `interior` and last, as a fallback, for the others.
            Texture atlas = (kind == Kind.Cockpit) ? this.Resolve("CockpitInteriorHiSampler", kind) : null;
            if (atlas != null && material.StartsWith("interior", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(atlas);
            }
            bool wantsColour = false;      // the technique reads a colour texture at all (black, chrome: none)
            if (technique != null)
            {
                List<string> samplers = this.techniques[technique];
                foreach (string sampler in ColourSamplers)
                {
                    // FM3 spells them lightsSampler, undercarriageSampler...
                    if (samplers.Exists(delegate(string name) { return string.Equals(name, sampler, StringComparison.OrdinalIgnoreCase); }))
                    {
                        wantsColour = true;
                        Texture texture = this.Resolve(sampler, kind);
                        if (texture != null)
                        {
                            result.Add(texture);
                        }
                    }
                }
            }
            if (atlas != null && wantsColour && !result.Exists(delegate(Texture t) { return t.File == atlas.File; }))
            {
                result.Add(new Texture { Sampler = "CockpitInteriorHiSampler, fallback", File = atlas.File, Where = atlas.Where });
            }
            return result;
        }

        // Every texture file a material's shader reads (colour, normal, dirt... all its samplers that
        // map to a file), for the port: what a car needs from its game.
        public List<Texture> AllTextures(string material, Kind kind)
        {
            List<Texture> result = new List<Texture>();
            foreach (string sampler in this.SamplersOf(this.TechniqueOf(material, kind)))
            {
                Texture texture = this.Resolve(sampler, kind);
                if (texture != null)
                {
                    result.Add(texture);
                }
            }
            return result;
        }

        public List<string> SamplersOf(string technique)
        {
            List<string> samplers;
            return (technique != null && this.techniques.TryGetValue(technique, out samplers)) ? samplers : new List<string>();
        }

        // Every constant (float, int, bool, sampler) the vertex and pixel shaders of a technique declare in
        // their D3D constant tables, engine ones included; null when the game has no such technique. These are
        // the names a car's shadersettings.xml can really set on it.
        public ICollection<string> ConstantsOf(string technique)
        {
            List<string> constants;
            return (technique != null && this.constants.TryGetValue(technique, out constants)) ? constants : null;
        }

        private Texture Resolve(string sampler, Kind kind)
        {
            string suffix = (kind == Kind.Main || this.singleModel) ? "" : "_LOD0";
            if (sampler == "baseSampler" || sampler == "damageSampler")
            {
                return new Texture { Sampler = sampler, File = (sampler == "baseSampler" ? "nodamage" : "damage") + suffix + ".xds", Where = Place.Car };
            }
            List<Definition> list;
            if (!this.definitions.TryGetValue(sampler, out list) || list.Count == 0)
            {
                return null;
            }
            // The entry for this file; else the first one (a showroom-only texture is still the right
            // picture for the viewer).
            Definition chosen = list[0];
            foreach (Definition definition in list)
            {
                if (Fits(definition.Flags, kind))
                {
                    chosen = definition;
                    break;
                }
            }
            Place where = (chosen.Path == "%SHAREDCARFOLDER%") ? Place.Shared : (chosen.Path == "%WHEELFOLDER%") ? Place.Wheel : Place.Car;
            return new Texture { Sampler = sampler, File = chosen.File, Where = where };
        }

        // loadFlags: terms joined by '&', '!' = not.
        private static bool Fits(string flags, Kind kind)
        {
            if (string.IsNullOrEmpty(flags))
            {
                return true;
            }
            foreach (string raw in flags.Split('&'))
            {
                string term = raw.Trim();
                bool not = term.StartsWith("!");
                if (not)
                {
                    term = term.Substring(1).Trim();
                }
                bool value;
                switch (term)
                {
                    case "LOD15":
                        value = kind == Kind.Main;
                        break;
                    case "LOD0":
                        value = kind != Kind.Main;
                        break;
                    case "Cockpit":
                        value = kind == Kind.Cockpit;
                        break;
                    case "SuperLOD":
                        value = false;
                        break;
                    default:
                        value = true;       // InGameTextures, CarCommon
                        break;
                }
                if (value == not)
                {
                    return false;
                }
            }
            return true;
        }

        // An Xbox 360 D3DX9 effect (.fxobj, big-endian): technique -> passes -> pixel shader -> its
        // texture samplers (fm4dev\Shaders\tools\fxtech.cs, fxsamplers.cs). Engine samplers
        // (environment, track lightmap, shadows, mirror, cube maps) are left out.
        private sealed class EffectReader
        {
            private readonly byte[] d;
            private readonly List<int> vertexShaders = new List<int>();
            private readonly List<int> pixelShaders = new List<int>();
            private readonly List<List<string>> pixelSamplers = new List<List<string>>();
            private readonly List<List<string>> vertexConstants = new List<List<string>>();
            private readonly List<List<string>> pixelConstants = new List<List<string>>();
            private int nameBase;
            private int blockBase;

            public EffectReader(byte[] data)
            {
                this.d = data;
            }

            private uint U32(int o)
            {
                return (uint) ((this.d[o] << 24) | (this.d[o + 1] << 16) | (this.d[o + 2] << 8) | this.d[o + 3]);
            }

            private int U16(int o)
            {
                return (this.d[o] << 8) | this.d[o + 1];
            }

            private string Str(int o)
            {
                int e = o;
                while (e < this.d.Length && this.d[e] != 0)
                {
                    e++;
                }
                return Encoding.ASCII.GetString(this.d, o, e - o);
            }

            // A name reference: a printable, null-terminated string of 1-64 bytes right after a 0.
            private string Name(uint reference)
            {
                long p = reference + (long) this.nameBase;
                if (p <= 0 || p >= this.d.Length || this.d[p - 1] != 0)
                {
                    return null;
                }
                long e = p;
                while (e < this.d.Length && this.d[e] != 0)
                {
                    if (this.d[e] < 0x21 || this.d[e] > 0x7E || e - p > 64)
                    {
                        return null;
                    }
                    e++;
                }
                return (e - p < 1 || e >= this.d.Length) ? null : Encoding.ASCII.GetString(this.d, (int) p, (int) (e - p));
            }

            private static bool Engine(string name)
            {
                string l = name.ToLowerInvariant();
                return l.StartsWith("env") || l.Contains("tracklightmap") || l.Contains("shadow") || l.Contains("csm") || l.StartsWith("mirror") || l.Contains("cubemap") || l.Contains("clouds");
            }

            // Every constant name of the D3D constant table at 'table' (any register set).
            private List<string> AllConstants(int table)
            {
                List<string> names = new List<string>();
                int count = (int) this.U32(table + 12), info = table + (int) this.U32(table + 16);
                for (int i = 0; i < count; i++)
                {
                    int e = info + (i * 20);
                    if (e + 20 > this.d.Length)
                    {
                        break;
                    }
                    names.Add(this.Str(table + (int) this.U32(e)));
                }
                return names;
            }

            // The effect's name table: parameters and samplers, then the technique names from `body` on. Walks back
            // from `body` over identifier-like names (a library file only; single-material files add theirs below).
            public void ReadNames(HashSet<string> into)
            {
                string s = Encoding.GetEncoding(28591).GetString(this.d);
                int p = s.LastIndexOf("\0body\0", StringComparison.Ordinal);
                while (p > 0)
                {
                    int q = s.LastIndexOf('\0', p - 1);
                    if (q < 0 || p - q - 1 < 1 || p - q - 1 > 64)
                    {
                        break;
                    }
                    string name = s.Substring(q + 1, p - q - 1);
                    if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$"))
                    {
                        break;
                    }
                    into.Add(name);
                    p = q;
                }
            }

            public void Read(Dictionary<string, List<string>> techniques, Dictionary<string, List<string>> constants)
            {
                int marker = -1;
                for (int o = 0x10; o < 0x4000 && o + 4 <= this.d.Length; o += 4)
                {
                    if (this.U32(o) == 0xA3D70141)
                    {
                        marker = o;
                        break;
                    }
                }
                if (marker < 0)
                {
                    return;
                }
                this.nameBase = marker + 0x0C;
                this.blockBase = marker + 0x1C;
                for (int o = 0; o + 0x28 < this.d.Length; o += 4)
                {
                    uint magic = this.U32(o);
                    if (magic != 0x102A1101 && magic != 0x102A1100)
                    {
                        continue;
                    }
                    int table = o + (int) this.U32(o + 0x10) + 4;
                    if (table < 0 || table + 20 > this.d.Length || (this.U32(table + 8) >> 16) < 0xFFFE)
                    {
                        continue;
                    }
                    if (magic == 0x102A1101)
                    {
                        this.vertexShaders.Add(o);
                        this.vertexConstants.Add(this.AllConstants(table));
                        continue;
                    }
                    this.pixelShaders.Add(o);
                    this.pixelConstants.Add(this.AllConstants(table));
                    List<string> names = new List<string>();
                    int count = (int) this.U32(table + 12), info = table + (int) this.U32(table + 16);
                    for (int i = 0; i < count; i++)
                    {
                        int e = info + (i * 20);
                        if (e + 20 > this.d.Length)
                        {
                            break;
                        }
                        if ((this.U16(e + 4) & 3) != 3)
                        {
                            continue;
                        }
                        string sampler = this.Str(table + (int) this.U32(e));
                        if (!Engine(sampler))
                        {
                            names.Add(sampler);
                        }
                    }
                    this.pixelSamplers.Add(names);
                }
                int stop = (this.vertexShaders.Count > 0) ? this.vertexShaders[0] : this.d.Length;
                // Technique record (5 dwords): name ref, pass count, ?, ?, ref to the first pass record;
                // pass record (5 dwords): name ref, 0, pass block ref, state ref, state ref.
                for (int o = marker; o + 20 <= stop; o += 4)
                {
                    string name = this.Name(this.U32(o));
                    if (name == null)
                    {
                        continue;
                    }
                    int passes = (int) this.U32(o + 4);
                    int first = (int) this.U32(o + 16) + this.nameBase;
                    if (passes < 1 || passes > 8 || first < marker || first + (20 * passes) > stop)
                    {
                        continue;
                    }
                    List<string> samplers = new List<string>();
                    List<string> names = new List<string>();
                    bool ok = true;
                    for (int k = 0; k < passes; k++)
                    {
                        int record = first + (k * 20);
                        int vertex = -1;
                        int shader = (this.Name(this.U32(record)) != null && this.U32(record + 4) == 0) ? this.PassPixelShader((int) this.U32(record + 8), out vertex) : -2;
                        if (shader == -2)
                        {
                            ok = false;
                            break;
                        }
                        if (vertex >= 0)
                        {
                            AddNew(names, this.vertexConstants[vertex]);
                        }
                        if (shader >= 0)
                        {
                            AddNew(names, this.pixelConstants[shader]);
                            foreach (string sampler in this.pixelSamplers[shader])
                            {
                                if (!samplers.Contains(sampler))
                                {
                                    samplers.Add(sampler);
                                }
                            }
                        }
                    }
                    if (!ok)
                    {
                        continue;
                    }
                    if (!techniques.ContainsKey(name))
                    {
                        techniques[name] = samplers;
                        constants[name] = names;
                    }
                    o += 16;
                }
            }

            // The pixel shader index of a pass block: -1 none, -2 not a pass block. The block ends (within
            // 0x100 bytes) with FFFFFFFF, VS ref, PS ref; refs point at a blob's +0 to +15.
            private int PassPixelShader(int block, out int vertex)
            {
                vertex = -1;
                int b = block + this.blockBase;
                if (block == 0 || b < 0 || b + 0x100 > this.d.Length)
                {
                    return -2;
                }
                for (int o = b; o < b + 0x100; o += 4)
                {
                    if (this.U32(o) != 0xFFFFFFFF)
                    {
                        continue;
                    }
                    vertex = Find(this.vertexShaders, (int) this.U32(o + 4) + this.blockBase);
                    int pixel = Find(this.pixelShaders, (int) this.U32(o + 8) + this.blockBase);
                    if (vertex < 0 && pixel < 0)
                    {
                        continue;
                    }
                    return pixel;
                }
                return -2;
            }

            private static void AddNew(List<string> into, List<string> from)
            {
                foreach (string name in from)
                {
                    if (!into.Contains(name))
                    {
                        into.Add(name);
                    }
                }
            }

            private static int Find(List<int> starts, int address)
            {
                for (int i = 0; i < starts.Count; i++)
                {
                    if (address >= starts[i] && address < starts[i] + 16)
                    {
                        return i;
                    }
                }
                return -1;
            }
        }
    }
}
