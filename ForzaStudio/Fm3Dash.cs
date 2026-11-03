namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;

    // FM4 digital dash -> FM3 (the inverse of CarPort.ConvertMotorsport3Dash). .bgf and .bsg are the same format in both games; the
    // .fbf differs by its version (0x3EE), 33 bytes at the end of a text record (FM4 has a 34th, always 0) and the default text, which
    // FM3 keeps equal to the .bgf textstring (303 of 303 text records of the 154 FM3 dashes) where FM4 empties it. Fonts and image
    // paths are the caller's rules (DashFonts.ToFm3, FM3's Media\UI\Textures.zip). Checked on the 59 dashes both games have: with
    // images and fonts as FM3's own, 35 come out byte-identical, the others were re-authored in FM4 (other record sets).
    public static class Fm3Dash
    {
        // Property hashes of the 234 FM4 dashes that none of the 154 FM3 dashes uses (2026-11-03, tools FM3_Dash\cen2): the six-dash
        // animation behaviour (Animation name + 15 parameters), the "Overlays group" parameter of the 23 dashes with a 95/96-parameter
        // contract, the tach memory pair and the image UV offset properties. FM3 may not know them: such a dash is not ported unless asked.
        private static readonly uint[] Fm4Only =
        {
            0xABC40DE5, 0x995FA2AC, 0x9D02C3D7, 0x9D8CA968, 0x988547FB, 0x98D4A326, 0x9DC6F0F7, 0x9D487501, 0x9CD6E2A6, 0x9BC166E6,
            0x9EDB24E7, 0x99F4E2E8, 0x9D0EA0E9, 0x98285EEA, 0x9B421CEB, 0x9E5BDAEC, 0xAAC190DD, 0x1D2E3A61, 0x19D05AB4, 0x19D15AF3,
            0x181409F5, 0x0E562203, 0x0BF92B21, 0xA523363B, 0xAA63A72E
        };

        // The FM4-only properties a .bgf uses, as names or hex hashes (empty: the dash only uses what FM3 dashes use).
        public static List<string> Fm4OnlyProperties(AnarkBgf bgf)
        {
            List<string> found = new List<string>();
            foreach (AnarkBgf.Element element in bgf.Elements)
            {
                foreach (AnarkBgf.Property property in element.Properties)
                {
                    if (Array.IndexOf(Fm4Only, property.Hash) >= 0)
                    {
                        string name = property.Name;
                        string label = string.IsNullOrEmpty(name) ? property.Hash.ToString("X8") : name;
                        if (!found.Contains(label))
                        {
                            found.Add(label);
                        }
                    }
                }
            }
            return found;
        }

        // The .fbf in FM3 form. imagePath maps a record's image path to FM3's (null = leave it); fonts go through DashFonts.ToFm3.
        public static byte[] ToMotorsport3(byte[] fbfData, AnarkBgf bgf, Converter<string, string> imagePath, List<string> unknownFonts)
        {
            AnarkFbf fbf = AnarkFbf.Read(fbfData);
            if (fbf.IsFM3)
            {
                return fbfData;
            }
            Dictionary<uint, string> texts = new Dictionary<uint, string>();
            foreach (AnarkBgf.Element element in bgf.Elements)
            {
                AnarkBgf.Property property = element.Find("textstring");
                string text = (property == null) ? null : bgf.GetString(property);
                if (text != null)
                {
                    texts[element.Id] = text;
                }
            }
            fbf.Version = AnarkFbf.VersionFM3;
            foreach (AnarkFbf.Record record in fbf.Records)
            {
                if (record.Type == AnarkFbf.RecordType.Text)
                {
                    byte[] tail = new byte[33];
                    Array.Copy(record.TextTail, 0, tail, 0, 33);
                    record.TextTail = tail;
                    string text;
                    record.Text = texts.TryGetValue(record.Id, out text) ? text : "";
                }
                else if (record.Type == AnarkFbf.RecordType.Image && imagePath != null)
                {
                    string path = imagePath(record.Path);
                    if (path != null)
                    {
                        record.Path = path;
                    }
                }
            }
            DashFonts.Fix(fbf, unknownFonts, true);
            return fbf.Write();
        }
    }
}
