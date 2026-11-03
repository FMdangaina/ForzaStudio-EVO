using System;
using System.Collections.Generic;

namespace ForzaStudio
{
    // Font names of a digital dash for FM4. A dash names its fonts in the .bgf (property "font", every text element)
    // and keeps the same names as the default in the .fbf text records; the game looks the name up in
    // Media\UI\Fonts.zip\fontmap.xml. FM4's fontmap knows the DG1..DG5 families (as dg1_arial bold, dg2_lcd bold,
    // dg3_moire bold, dg4_pf_ronda_seven, dg5_quartzms) and the UI fonts A, B, D, E, but NOT the names FH1/FH2
    // dashes use (horizon_dg1.., horizon_a..): FM4 then falls back on a generic font. FM3's eurostile and
    // franklin gothic have no entry either.
    // Rule: a name FM4's own dashes use stays; the others become the FM4 name of the same font family.
    // Only names that exist in FM4's dashes are written (no invented names).
    public static class DashFonts
    {
        // The 15 font names of the 234 FM4 dashes (2026-10-31), lower case.
        private static readonly Set<string> Fm4Names = new Set<string>(new string[]
        {
            "a_endurancepro", "a_swiss_721_bold_condensed bold", "arial", "arial bold", "b_swiss_721_medium medium",
            "d_endurancepro-condsemibold", "d_swiss_721_roman_ condensed roman", "d_swiss_721_roman_condensed roman",
            "dg1_arial bold", "dg2_lcd bold", "dg3_moire bold", "dg4_pf_ronda_seven", "dg5_quartzms",
            "e_endurancepro-cond", "lcd bold",
        }, StringComparer.OrdinalIgnoreCase);

        private const string Dg1 = "dg1_arial bold";
        private const string A = "a_swiss_721_bold_condensed bold";

        // Source font (lower case) -> FM4 name. FH1/FH2 point horizon_dgN at the same dgN files FM4 has
        // (checked on both fontmap.xml); horizon_c = A in FH1 and FH2's own C (FM4 has no C), so A.
        private static readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "horizon_dg1", Dg1 },
            { "horizon_dg2", "dg2_lcd bold" },
            { "horizon_dg3", "dg3_moire bold" },
            { "horizon_dg4", "dg4_pf_ronda_seven" },
            { "horizon_dg5", "dg5_quartzms" },
            { "horizon_dg6", Dg1 },
            { "horizon_a", A },
            { "horizon_b", "b_swiss_721_medium medium" },
            { "horizon_c", A },
            { "horizon_d", "d_swiss_721_roman_condensed roman" },
            { "horizon_e", "e_endurancepro-cond" },
            { "b_endurancepro bold", "b_swiss_721_medium medium" },
            { "lcd light", "lcd bold" },
            { "franklin gothic demi cond", A },
            { "eurostile bold", Dg1 },
        };

        // The FM4 name for a font, or the same name when FM4 knows it or there is no rule (then Unknown says so).
        public static string ToFm4(string font)
        {
            if (string.IsNullOrEmpty(font) || Fm4Names.Contains(font))
            {
                return font;
            }
            string mapped;
            return Map.TryGetValue(font, out mapped) ? mapped : font;
        }

        public static bool Unknown(string font)
        {
            return !string.IsNullOrEmpty(font) && !Fm4Names.Contains(font) && !Map.ContainsKey(font);
        }

        // FM3 (target of the FM4 -> FM3 port). The 12 font names of the 154 FM3 dashes plus the names its fontmap.xml resolves by
        // prefix and FM4 dashes also use: arial, d_endurancepro-condsemibold (D_Endurance), e_endurancepro-cond (E_Endurance).
        private static readonly Set<string> Fm3Names = new Set<string>(new string[]
        {
            "a_endurancepro", "arial bold", "b_endurancepro bold", "dg1_arial bold", "dg2_lcd bold", "dg3_moire bold",
            "dg4_pf_ronda_seven", "dg5_quartzms", "eurostile bold", "franklin gothic demi cond", "lcd bold", "lcd light",
            "arial", "d_endurancepro-condsemibold", "e_endurancepro-cond",
        }, StringComparer.OrdinalIgnoreCase);

        // FM4 name -> the FM3 name of the same family (FM3 maps A_Endurance, B_Endurance, D_Endurance, E_Endurance to its UI fonts;
        // FM4's Swiss 721 names have no entry there).
        private static readonly Dictionary<string, string> MapFm3 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "a_swiss_721_bold_condensed bold", "a_endurancepro" },
            { "b_swiss_721_medium medium", "b_endurancepro bold" },
            { "d_swiss_721_roman_condensed roman", "d_endurancepro-condsemibold" },
            { "d_swiss_721_roman_ condensed roman", "d_endurancepro-condsemibold" },
        };

        public static string ToFm3(string font)
        {
            if (string.IsNullOrEmpty(font) || Fm3Names.Contains(font))
            {
                return font;
            }
            string mapped;
            return MapFm3.TryGetValue(font, out mapped) ? mapped : font;
        }

        public static bool UnknownFm3(string font)
        {
            return !string.IsNullOrEmpty(font) && !Fm3Names.Contains(font) && !MapFm3.ContainsKey(font);
        }

        // .fbf: the default fonts of the text records. Returns how many were changed.
        public static int Fix(AnarkFbf fbf, List<string> unknown)
        {
            return Fix(fbf, unknown, false);
        }

        public static int Fix(AnarkFbf fbf, List<string> unknown, bool toFm3)
        {
            int changed = 0;
            foreach (AnarkFbf.Record record in fbf.Records)
            {
                if (record.Type != AnarkFbf.RecordType.Text)
                {
                    continue;
                }
                string font = toFm3 ? ToFm3(record.Font) : ToFm4(record.Font);
                if (font != record.Font)
                {
                    record.Font = font;
                    changed++;
                }
                else if ((toFm3 ? UnknownFm3(record.Font) : Unknown(record.Font)) && unknown != null && !unknown.Contains(record.Font))
                {
                    unknown.Add(record.Font);
                }
            }
            return changed;
        }

        // .bgf: the strings the "font" properties point at (each string is used by one property only). The bytes
        // come back unchanged when nothing needs a new name.
        public static byte[] Fix(byte[] bgfData, out int changed, List<string> unknown)
        {
            return Fix(bgfData, out changed, unknown, false);
        }

        public static byte[] Fix(byte[] bgfData, out int changed, List<string> unknown, bool toFm3)
        {
            changed = 0;
            AnarkBgf bgf = AnarkBgf.Read(bgfData);
            foreach (AnarkBgf.Element element in bgf.Elements)
            {
                AnarkBgf.Property p = element.Find("font");
                string font = bgf.GetString(p);
                if (font == null)
                {
                    continue;
                }
                string fm4 = toFm3 ? ToFm3(font) : ToFm4(font);
                if (fm4 != font)
                {
                    bgf.Strings[(int) p.Value] = fm4;
                    changed++;
                }
                else if ((toFm3 ? UnknownFm3(font) : Unknown(font)) && unknown != null && !unknown.Contains(font))
                {
                    unknown.Add(font);
                }
            }
            return (changed == 0) ? bgfData : bgf.Write();
        }
    }
}
