using System;
using System.Collections.Generic;

namespace ForzaStudio
{
    // Simple reading of the game's car shaders for the viewport (not a translation: the fxobj files are compiled
    // Xbox 360 microcode). A material is a technique name; the classes below come from what its pixel shaders
    // declare (constants such as PaintColor / GlassColor / ChromeDiffuseScale, samplers such as LightsSampler),
    // see ShaderLookTable for the result on every FM4 / FH1 / FH2 / FM3 technique and Look() for the BasicEffect values.
    public enum ShaderKind
    {
        Generic,    // textured, nothing special
        Paint,      // car paint: body colour + metallic highlight
        Chrome,     // mirror metal: dark diffuse, strong white highlight
        Metal,      // brushed / anodized / bump metal
        Glass,      // transparent, tinted
        Light,      // lamp lens: texture + a bit of glow
        Emissive,   // gauges, LEDs, lit parts
        Carbon,     // carbon fiber
        Rubber,     // black rubber, plastic trim, undercarriage
        Soft,       // cockpit leather, cloth, suede, wood, plastic: matte
        Ghost,      // fading placeholder (hidden car parts)
        Pass,       // engine passes (shadow, depth, debug): nothing to see
        Missing     // no such technique in FM4: magenta
    }

    public struct ShaderLookValues
    {
        public ShaderKind Kind;
        public Vector3Lite Diffuse;   // multiplied with the texture / base grey
        public Vector3Lite Emissive;
        public Vector3Lite Specular;
        public float SpecularPower;
        public float Alpha;
        public bool UseTexture;       // the diffuse comes from the texture (else the base grey / the paint colour)
        public bool HasPaintColor;    // shadersettings.xml gives the paint colour itself (matte_colors, emblem): used instead of the body colour
        public Vector3Lite PaintColor;
        public float UvScale;         // texture repeat set by the car (CarbonFiberScale, MottleTile, UV1Tile1); 0 = as the mesh maps it
        public bool GlassTinted;      // shadersettings.xml gives GlassColor: it tints the glass (and a texture drawn on it)
        public bool OpacitySet;       // shadersettings.xml gives GlassMaxOpacity
        public const float TexturedGlassAlpha = 0.6f;   // glass that has a texture (lamp lenses) shows it fairly opaque: the texture alpha is not applied
    }

    // Plain float triple so the table and the harnesses do not need XNA.
    public struct Vector3Lite
    {
        public float X, Y, Z;
        public Vector3Lite(float x, float y, float z) { this.X = x; this.Y = y; this.Z = z; }
        public Vector3Lite(float v) { this.X = v; this.Y = v; this.Z = v; }
    }

    public static partial class ShaderLook
    {
        // Techniques whose class needs more than the name: read from the pixel shaders of FM4 / FH1 / FH2 / FM3
        // (the 36 of 391 where the shader constants and the name rules disagree).
        private static readonly Dictionary<string, ShaderKind> Known = new Dictionary<string, ShaderKind>(StringComparer.OrdinalIgnoreCase)
        {
            // Only `body` takes the car's colour in the viewport (user 2026-10-31): misc, engine, cloth... share its
            // shader but are drawn with their texture (nodamage), so they are Generic here.
            { "badge2", ShaderKind.Generic }, { "brinl", ShaderKind.Light }, { "brinr", ShaderKind.Light }, { "brkreverse", ShaderKind.Light },
            { "cloth", ShaderKind.Generic }, { "cloth2", ShaderKind.Generic }, { "engine", ShaderKind.Generic }, { "engine_embossed_CF", ShaderKind.Carbon },
            { "hidhead", ShaderKind.Light }, { "lights_chrome", ShaderKind.Light }, { "lights_metal", ShaderKind.Light }, { "lights_plastic", ShaderKind.Light },
            { "lights_gls_hidhead", ShaderKind.Glass }, { "lights_gls_indicator_left", ShaderKind.Glass }, { "lights_gls_indicator_right", ShaderKind.Glass },
            { "lights_gls_noemit", ShaderKind.Glass }, { "lights_gls_oldhead", ShaderKind.Glass }, { "lights_gls_reverse_light", ShaderKind.Glass },
            { "lights_gls_tail_light", ShaderKind.Glass }, { "lights_gls_taillight2S", ShaderKind.Glass }, { "lights_gls_taillightst", ShaderKind.Glass },
            { "lindtailside", ShaderKind.Light }, { "linred", ShaderKind.Light }, { "liveryBadge", ShaderKind.Generic }, { "misc", ShaderKind.Generic },
            { "mottled", ShaderKind.Generic }, { "oldhead", ShaderKind.Light }, { "race_kit_red", ShaderKind.Generic }, { "rindtailside", ShaderKind.Light },
            { "rinred", ShaderKind.Light }, { "slinlorange", ShaderKind.Light }, { "slinrorange", ShaderKind.Light }, { "slorange", ShaderKind.Light },
            { "stripe_badge", ShaderKind.Generic }, { "wheel_black", ShaderKind.Rubber }, { "xenonhead", ShaderKind.Light }
        };

        private static Dictionary<string, bool> MakeSet(string[] names)
        {
            Dictionary<string, bool> set = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (string name in names) set[name] = true;
            return set;
        }

        // Class of a material (= technique) name as the carbin gives it. Whether FM4 really uses the material is not
        // decided here: the compiled techniques are no test (cockpit files of FM4 cars use names such as `window` or
        // `plastic2` that have no cockpit_ technique, and lights_amber has one but renders black); the viewport
        // paints a mesh magenta from ForzaMesh.Unused, set from the lists of what FM4 cars use (Fm4MaterialUse).
        public static ShaderKind For(string technique)
        {
            ShaderKind kind;
            if (string.IsNullOrEmpty(technique)) return ShaderKind.Generic;
            if (Known.TryGetValue(technique, out kind)) return kind;
            return Classify(technique, null);
        }

        // Class from the technique name and what its pixel shaders declare (constants + samplers, names only).
        public static ShaderKind Classify(string technique, ICollection<string> features)
        {
            string n = (technique ?? "").ToLowerInvariant();
            if (n.StartsWith("ghost") || n.StartsWith("cockpit_ghost") || Has(features, "GhostColor") || Has(features, "GhostInteriorColor")) return ShaderKind.Ghost;
            if (n == "zonly" || n == "missing" || (n.Contains("shadow") && !n.Contains("unshadowed")) || n.Contains("smask") || n.StartsWith("debug") || n.Contains("missingbrake")
                || n == "stencil" || n == "stencilraw" || n == "drawlines" || n == "wireframe" || n == "default" || n == "scaling_text") return ShaderKind.Pass;
            // lights_* are the light-aware variants of the usual materials (lights_metal = metal ...); only amber / red are lamps themselves
            if (n.StartsWith("lights_") && n != "lights_amber" && n != "lights_red" && !n.StartsWith("lights_gls_") && n != "lights_glass") return Classify(n.Substring(7), features);
            if (n.Contains("plate")) return ShaderKind.Generic;
            if (n.Contains("glass") || n.Contains("window") || n.Contains("windshield") || Has(features, "GlassColor") || Has(features, "S:DamageGlassSampler")) return ShaderKind.Glass;
            if (n.Contains("gauge") || n.Contains("emissive") || n.Contains("_led") || n.Contains("glow") || Has(features, "S:CockpitGaugeEmissiveSampler")) return ShaderKind.Emissive;
            if (n.Contains("light") || n.Contains("lamp") || n.Contains("fullbeam") || n.Contains("drl") || n.Contains("indicator") || n.Contains("fog") || n.Contains("reflector") || n.Contains("lens")
                || Has(features, "S:LightsSampler") || Has(features, "S:HeadlightSampler") || Has(features, "BrakeColor")) return ShaderKind.Light;
            if (n.Contains("chrome") || n.Contains("mirror") || Has(features, "ChromeDiffuseScale")) return ShaderKind.Chrome;
            if (n.Contains("carbon") || Any(features, "CarbonFiber")) return ShaderKind.Carbon;
            if (n.Contains("rubber") || n.Contains("tire") || n.Contains("tyre") || n.Contains("weatherstrip") || n == "black" || n.Contains("undercarriage") || n.Contains("bottom") || n.Contains("cage") || Has(features, "BlackColor")) return ShaderKind.Rubber;
            // the full body pixel shader declares the two-tone / stripe paint constants; misc, engine, cloth ... only share its metal part
            if (n.Contains("paint") || n.Contains("body") || (Has(features, "PaintColor") && Has(features, "MetalColor") && Has(features, "TwoToneColor"))) return ShaderKind.Paint;
            if (n.Contains("metal") || n.Contains("alumin") || n.Contains("steel") || n.Contains("rim") || n.Contains("caliper") || n.Contains("rotor") || n.Contains("badge") || n.Contains("emblem") || n.Contains("grille")) return ShaderKind.Metal;
            if (n.StartsWith("cockpit_") || Has(features, "S:CockpitInteriorHiSampler") || Any(features, "Cockpit")) return ShaderKind.Soft;
            return ShaderKind.Generic;
        }

        private static bool Has(ICollection<string> features, string f)
        {
            return features != null && features.Contains(f);
        }

        private static bool Any(ICollection<string> features, string part)
        {
            if (features == null) return false;
            foreach (string f in features) if (f.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // How much of the environment picture a class mirrors (0 = none), used by the viewport's EnvReflection pass.
        public static float Reflectivity(ShaderKind kind)
        {
            switch (kind)
            {
                case ShaderKind.Paint: return 0.35f;
                case ShaderKind.Chrome: return 0.9f;
                case ShaderKind.Metal: return 0.6f;
                case ShaderKind.Glass: return 0.5f;
                case ShaderKind.Light: return 0.3f;
                case ShaderKind.Carbon: return 0.3f;
                case ShaderKind.Generic: return 0.1f;
                case ShaderKind.Rubber: return 0.05f;
                case ShaderKind.Soft: return 0.03f;
                default: return 0f;
            }
        }

        // The values of a car's shadersettings.xml entry for the mesh's technique (parameter -> r g b a or value)
        // laid over the class look, so a red GlassColor or a red lamp colour shows in the viewport as in game.
        // Colours above 1 (FH lamps use 2.0) are clamped; a parameter the class has no use for is ignored.
        public static ShaderLookValues Apply(ShaderLookValues look, IDictionary<string, float[]> p)
        {
            if (p == null || p.Count == 0) return look;
            float[] v;
            if (p.TryGetValue("GlassColor", out v) && v.Length >= 3 && look.Kind == ShaderKind.Glass)
            {
                look.Diffuse = Clamp3(v, 0.6f);
                look.GlassTinted = true;
                look.Specular = new Vector3Lite(0.6f);
            }
            if (p.TryGetValue("GlassMaxOpacity", out v) && look.Kind == ShaderKind.Glass)
            {
                look.Alpha = Math.Max(0.1f, Math.Min(1f, v[0]));
                look.OpacitySet = true;
            }
            if ((p.TryGetValue("PaintColor", out v) || p.TryGetValue("CarbonFiberDiffuseColor", out v) || p.TryGetValue("GrilleColor", out v) || p.TryGetValue("CockpitHeadlinerColor", out v)) && v.Length >= 3)
            {
                if (look.Kind == ShaderKind.Paint || p.ContainsKey("PaintColor"))
                {
                    look.HasPaintColor = true;
                    look.PaintColor = Clamp3(v, 1f);
                }
                else
                {
                    look.Diffuse = Mul(look.Diffuse, Clamp3(v, 1f));
                }
            }
            if (p.TryGetValue("DiffuseColorMultiply", out v) && v.Length >= 3) look.Diffuse = Mul(look.Diffuse, Clamp3(v, 1f));
            foreach (string key in new string[] { "PaintScale", "CockpitPaintScale", "CockpitPlasticPaintScale", "CockpitMetalPaintScale", "PristinePaintScale" })
            {
                if (p.TryGetValue(key, out v) && look.Kind != ShaderKind.Glass && look.Kind != ShaderKind.Chrome)
                {
                    look.Diffuse = Mul(look.Diffuse, new Vector3Lite(Math.Max(0.1f, Math.Min(1.5f, v[0]))));
                    break;
                }
            }
            if (p.TryGetValue("ChromeDiffuseScale", out v)) look.Diffuse = new Vector3Lite(Math.Max(0f, Math.Min(1f, 0.25f * v[0])));
            float amount = 1f;
            bool hasSpecular = false;
            foreach (string key in new string[] { "SpecularAmount", "CockpitSpecularAmount" })
            {
                if (p.TryGetValue(key, out v)) { amount = Math.Max(0f, Math.Min(1.5f, v[0])); hasSpecular = true; break; }
            }
            if ((p.TryGetValue("SpecularColor", out v) || p.TryGetValue("CockpitSpecularColor", out v)) && v.Length >= 3)
            {
                look.Specular = Mul(Clamp3(v, 1f), new Vector3Lite(amount));
            }
            else if (hasSpecular)
            {
                look.Specular = new Vector3Lite(Math.Min(1f, amount));
            }
            foreach (string key in new string[] { "SpecularPower", "CockpitSpecularPower" })
            {
                if (p.TryGetValue(key, out v)) { look.SpecularPower = Math.Max(1f, Math.Min(200f, v[0])); break; }
            }
            // Texture repeat: the car's value multiplies the mesh UVs (only a sane number is taken).
            foreach (string key in new string[] { "CarbonFiberScale", "MottleTile", "UV1Tile1" })
            {
                if (p.TryGetValue(key, out v) && v[0] > 0.01f && v[0] < 200f)
                {
                    look.UvScale = v[0];
                    break;
                }
            }
            // Self-lit gauges: GaugeEmissiveScale is the glow, GaugeAmbientAmt a bit of base light.
            float glow = 0f;
            // FM3 files spell it "GuageEmissiveScale" on some cars
            if (p.TryGetValue("GaugeEmissiveScale", out v) || p.TryGetValue("GuageEmissiveScale", out v)) glow = Math.Max(glow, 0.45f * Math.Min(1f, v[0]));
            if (p.TryGetValue("GaugeAmbientAmt", out v)) glow = Math.Max(glow, 0.3f * Math.Min(1f, v[0]));
            if (glow > 0f)
            {
                look.Emissive = new Vector3Lite(Math.Max(look.Emissive.X, glow), Math.Max(look.Emissive.Y, glow), Math.Max(look.Emissive.Z, glow));
            }
            if (look.Kind == ShaderKind.Light)
            {
                // FM4 names first, then the Horizon lamp colours (FH1 / FH2 shadersettings.xml; values up to 2.0, clamped)
                foreach (string key in new string[] { "ReverseLightColor", "BrakeColor", "TailLightColor", "XenonHeadlightColor", "HidHeadlightColor", "OldHeadlightColor", "FullbeamColor",
                    "DRLWhiteColor", "DRLOrangeColor", "DRLRedColor", "FogWhiteColor", "FogRedColor", "IndicatorLightColor", "IndicatorRedLightColor", "SideLightsOrangeColor", "NumberPlateLightColor" })
                {
                    if (p.TryGetValue(key, out v) && v.Length >= 3)
                    {
                        look.Emissive = Clamp3(v, 0.45f);
                        look.Diffuse = Mul(look.Diffuse, Clamp3(v, 1f));
                        break;
                    }
                }
            }
            return look;
        }

        private static Vector3Lite Clamp3(float[] v, float scale)
        {
            return new Vector3Lite(Math.Max(0f, Math.Min(1f, v[0])) * scale, Math.Max(0f, Math.Min(1f, v[1])) * scale, Math.Max(0f, Math.Min(1f, v[2])) * scale);
        }

        private static Vector3Lite Mul(Vector3Lite a, Vector3Lite b)
        {
            return new Vector3Lite(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
        }

        // BasicEffect-style values for a class. `paint` is the body colour (null = the viewer's base grey).
        public static ShaderLookValues Look(ShaderKind kind)
        {
            ShaderLookValues v = new ShaderLookValues { Kind = kind, Diffuse = new Vector3Lite(1f), Emissive = new Vector3Lite(0f), Specular = new Vector3Lite(0.3f), SpecularPower = 20f, Alpha = 1f, UseTexture = true };
            switch (kind)
            {
                case ShaderKind.Paint:    v.Specular = new Vector3Lite(0.7f); v.SpecularPower = 60f; v.UseTexture = false; break;
                case ShaderKind.Chrome:   v.Diffuse = new Vector3Lite(0.25f); v.Specular = new Vector3Lite(1f); v.SpecularPower = 90f; v.UseTexture = false; break;
                case ShaderKind.Metal:    v.Diffuse = new Vector3Lite(0.65f); v.Specular = new Vector3Lite(0.8f); v.SpecularPower = 40f; break;
                case ShaderKind.Glass:    v.Diffuse = new Vector3Lite(0.15f); v.Specular = new Vector3Lite(1f); v.SpecularPower = 120f; v.Alpha = 0.28f; v.UseTexture = false; break;
                case ShaderKind.Light:    v.Emissive = new Vector3Lite(0.22f); v.Specular = new Vector3Lite(0.9f); v.SpecularPower = 80f; break;
                case ShaderKind.Emissive: v.Emissive = new Vector3Lite(0.9f); v.Specular = new Vector3Lite(0.1f); break;
                case ShaderKind.Carbon:   v.Diffuse = new Vector3Lite(0.55f); v.Specular = new Vector3Lite(0.55f); v.SpecularPower = 35f; break;
                case ShaderKind.Rubber:   v.Diffuse = new Vector3Lite(0.12f); v.Specular = new Vector3Lite(0.08f); v.SpecularPower = 8f; v.UseTexture = false; break;
                case ShaderKind.Soft:     v.Specular = new Vector3Lite(0.1f); v.SpecularPower = 8f; break;
                case ShaderKind.Ghost:    v.Alpha = 0.12f; v.Specular = new Vector3Lite(0f); v.UseTexture = false; break;
                case ShaderKind.Pass:     v.Alpha = 0f; break;
                case ShaderKind.Missing:  v.Diffuse = new Vector3Lite(0f); v.Emissive = new Vector3Lite(1f, 0f, 1f); v.Specular = new Vector3Lite(0f); v.UseTexture = false; break;
            }
            return v;
        }
    }
}
