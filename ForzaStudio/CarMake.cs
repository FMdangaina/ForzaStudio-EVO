namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.Drawing.Imaging;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;

    // What a new car make is: the List_CarMake row, its two strings (DisplayName, Profile) in every
    // language and its logo in the six logo folders of UI\Textures.zip, the same three things the port
    // adds for a make FM4 lacks (CarPort.PortMake). Where it is written:
    //   local   outputRoot null: the game folder itself (files backed up to bak\ first);
    //   DLC     outputRoot = the pack's trial folder: the files go there as copies, a file already
    //           there (an earlier car or make of the pack) is read instead of the game's, and the
    //           pack build (DlcPackage.BuildFolder) takes the new row, the string tables and the
    //           logo entries from the difference to the game, as it does for a ported make.
    // A make with no car is not listed in game: a car goes under it by the port's Make choice.
    // A country FM4 lacks, added with the make: the List_Country row, its name (List_Country.str, every
    // language) and its flag in UI\Textures.zip (UI4/Logos/Country_Flags/L_FLA_<base>_flag.xds).
    public class CarCountrySpec
    {
        public long Id;                 // 0: the next free one (CarMake.FreeCountryId)
        public string Name;             // as the game shows it, e.g. SWEDEN; every language gets it
        public string Base;             // the flag's name part: L_FLA_<base>_flag
        public long RegionId;           // List_Region.Id
        public string Flag;             // an image file (png, jpg, bmp, tga), stretched to the flags' size
    }

    public class CarMakeSpec
    {
        public CarCountrySpec NewCountry;   // set: CountryId is the new country's Id (Create adds it first)
        public long Id;                 // 0: the next free one (CarMake.FreeId)
        public string Code;             // List_CarMake.ManufacturerCode, e.g. ACU
        public string Base;             // List_CarMake.IconPathBase, the logo's name part: L_MAN_<base>_Logo
        public long CountryId;          // List_Country.CountryID
        public string Name;             // DisplayName, English; the other languages get it too
        public string Profile;          // Profile, English; the other languages get it too
        public string Logo;             // an image file (png, jpg, bmp, tga, dds), fitted in each folder's size
        public string TemplateBase;     // an existing make whose logos give each folder's size and format
        public Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);       // language -> name
        public Dictionary<string, string> Profiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);    // language -> profile
    }

    public static class CarMake
    {
        // Logo folders of UI\Textures.zip the game builds from IconPathBase (L_MAN_<base>_Logo.xds).
        public static readonly string[] LogoFolders = { "Manufacturer_Logos", "Manufacturer_Logos_I", "Manufacturer_Logos_LG", "Manufacturer_Logos_SM", "Manufacturer_Logos_SM_wtbg", "Manufacturer_Logos_kinect" };

        // FM3 (user 2026-11-22: the "+" of the FM3 port windows): the same tables and string tables, but three logo folders
        // (53 makes each), no IconPathBase column, IconPath under Manufacturer_Logos (FM4: _I), and no titles. Its livery
        // decals are FM3 carbins, which MakeDecals does not build: none added.
        public static readonly string[] LogoFoldersFm3 = { "Manufacturer_Logos", "Manufacturer_Logos_I", "Manufacturer_Logos_SM" };

        private static bool IsFm3(string root)
        {
            return GameInfo.Detect(root) == ForzaGame.Motorsport3;
        }

        private const string Logos = @"GAME:\Media\UI\Textures\UI4\Logos\";

        public static string DatabasePath(string root)
        {
            return Path.Combine(Path.Combine(Path.Combine(root, "Media"), "db"), "gamedb.slt");
        }

        // The next make Id: after the game's largest and the largest of any DLC package in Xenia's
        // content folder (so two packs with a make each do not share an Id).
        public static long FreeId(SqliteDatabase db, string xeniaContent)
        {
            long id = DbIds.Next(db, "List_CarMake", "ID");
            foreach (string pack in DbIds.PackDatabases(xeniaContent))
            {
                try
                {
                    using (SqliteDatabase merge = SqliteDatabase.FromBytes(File.ReadAllBytes(pack)))
                    {
                        object max = merge.Scalar("SELECT max(ID) FROM List_CarMake");
                        if (max != null && max != DBNull.Value)
                        {
                            id = Math.Max(id, Convert.ToInt64(max, CultureInfo.InvariantCulture) + 1);
                        }
                    }
                }
                catch (Exception)
                {
                    // a package without the table, or unreadable: no make Id of its own
                }
            }
            return id;
        }

        private const string Flags = @"GAME:\Media\UI\Textures\UI4\Logos\Country_Flags\";

        // The next country Id: after the game's largest and the largest of any DLC package in Xenia's
        // content folder, like FreeId.
        public static long FreeCountryId(SqliteDatabase db, string xeniaContent)
        {
            long id = DbIds.Next(db, "List_Country", "CountryID");
            foreach (string pack in DbIds.PackDatabases(xeniaContent))
            {
                try
                {
                    using (SqliteDatabase merge = SqliteDatabase.FromBytes(File.ReadAllBytes(pack)))
                    {
                        object max = merge.Scalar("SELECT max(CountryID) FROM List_Country");
                        if (max != null && max != DBNull.Value)
                        {
                            id = Math.Max(id, Convert.ToInt64(max, CultureInfo.InvariantCulture) + 1);
                        }
                    }
                }
                catch (Exception)
                {
                    // a package without the table, or unreadable: no country Id of its own
                }
            }
            return id;
        }

        // The checks before anything is written; the first problem as a message, else null.
        public static string Check(CarMakeSpec spec, string targetRoot, string outputRoot)
        {
            if (string.IsNullOrEmpty(spec.Name) || spec.Name.Trim().Length == 0)
            {
                return "The make needs a name.";
            }
            if (spec.Code == null || !Regex.IsMatch(spec.Code, "^[A-Z0-9]{2,4}$"))
            {
                return "The code is 2-4 capital letters or digits (the game's are 3 letters: ACU, AST, POR).";
            }
            if (spec.Base == null || !Regex.IsMatch(spec.Base, "^[A-Za-z0-9]{2,40}$"))
            {
                return "The logo name is letters and digits only (L_MAN_<name>_Logo).";
            }
            if (string.IsNullOrEmpty(spec.Logo) || !File.Exists(spec.Logo))
            {
                return "Choose the logo image.";
            }
            string database = In(DatabasePath(targetRoot), targetRoot, outputRoot);
            using (SqliteDatabase db = SqliteDatabase.FromBytes(File.ReadAllBytes(database)))
            {
                if (spec.Id > 0 && Convert.ToInt64(db.Scalar("SELECT count(*) FROM List_CarMake WHERE ID = ?", spec.Id)) > 0)
                {
                    return "The game already has a make with Id " + spec.Id + ".";
                }
                if (Convert.ToInt64(db.Scalar("SELECT count(*) FROM List_CarMake WHERE upper(ManufacturerCode) = ?", spec.Code)) > 0)
                {
                    return "The game already has a make with the code " + spec.Code + ".";
                }
                bool baseColumn = DbIds.Schema(db)["List_CarMake"].Contains("IconPathBase");
                if (baseColumn
                    ? Convert.ToInt64(db.Scalar("SELECT count(*) FROM List_CarMake WHERE lower(IconPathBase) = ?", spec.Base.ToLowerInvariant())) > 0
                    : Convert.ToInt64(db.Scalar("SELECT count(*) FROM List_CarMake WHERE lower(IconPath) LIKE ?", "%l_man_" + spec.Base.ToLowerInvariant() + "_logo.%")) > 0)
                {
                    return "The game already has a make with the logo name " + spec.Base + ".";
                }
                CarCountrySpec country = spec.NewCountry;
                if (country == null)
                {
                    if (Convert.ToInt64(db.Scalar("SELECT count(*) FROM List_Country WHERE CountryID = ?", spec.CountryId)) == 0)
                    {
                        return "The country " + spec.CountryId + " is not in List_Country.";
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(country.Name) || country.Name.Trim().Length == 0)
                    {
                        return "The new country needs a name.";
                    }
                    if (country.Base == null || !Regex.IsMatch(country.Base, "^[A-Za-z0-9]{2,40}$"))
                    {
                        return "The flag name is letters and digits only (L_FLA_<name>_flag).";
                    }
                    if (string.IsNullOrEmpty(country.Flag) || !File.Exists(country.Flag))
                    {
                        return "Choose the flag image.";
                    }
                    if (country.Id > 0 && Convert.ToInt64(db.Scalar("SELECT count(*) FROM List_Country WHERE CountryID = ?", country.Id)) > 0)
                    {
                        return "The game already has a country with Id " + country.Id + ".";
                    }
                    if (Convert.ToInt64(db.Scalar("SELECT count(*) FROM List_Country WHERE lower(IconPath) LIKE ?", "%l_fla_" + country.Base.ToLowerInvariant() + "_flag.%")) > 0)
                    {
                        return "The game already has a country with the flag name " + country.Base + ".";
                    }
                    if (Convert.ToInt64(db.Scalar("SELECT count(*) FROM List_Region WHERE Id = ?", country.RegionId)) == 0)
                    {
                        return "The region " + country.RegionId + " is not in List_Region.";
                    }
                }
            }
            return null;
        }

        public static long Create(CarMakeSpec spec, string targetRoot, string outputRoot, string texconv, StringBuilder report)
        {
            string problem = Check(spec, targetRoot, outputRoot);
            if (problem != null)
            {
                throw new InvalidOperationException(problem);
            }
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string media = Path.Combine(targetRoot, "Media");

            // Everything is prepared in memory first; the files are written at the end.
            string textures = Path.Combine(Path.Combine(media, "UI"), "Textures.zip");
            ForzaZip logos = new ForzaZip(In(textures, targetRoot, outputRoot));
            bool fm3 = IsFm3(targetRoot);
            Dictionary<string, byte[]> logoFiles = BuildLogos(spec, logos, texconv, report, fm3 ? LogoFoldersFm3 : LogoFolders);

            string database = DatabasePath(targetRoot);
            SqliteDatabase db = SqliteDatabase.FromBytes(File.ReadAllBytes(In(database, targetRoot, outputRoot)));
            long id;
            string titles;          // the make's Drive_ rule (MakeTitles), null when the database has no titles
            ForzaZip liveryZip = null;  // Livery.zip with the make's decal added (MakeDecals), null when none
            Dictionary<string, ForzaZip> stringZips = new Dictionary<string, ForzaZip>(StringComparer.OrdinalIgnoreCase);
            try
            {
                id = (spec.Id > 0) ? spec.Id : FreeId(db, null);
                string[] strings = AddStrings(spec, db, targetRoot, outputRoot, stringZips, id, report);
                long countryId = spec.CountryId;
                if (spec.NewCountry != null)
                {
                    CarCountrySpec country = spec.NewCountry;
                    countryId = (country.Id > 0) ? country.Id : FreeCountryId(db, null);
                    string name = AddCountryName(country, db, stringZips, countryId, report);
                    byte[] flag = BuildFlag(country, logos, texconv, report);
                    logoFiles["UI4/Logos/Country_Flags/L_FLA_" + country.Base + "_flag.xds"] = flag;
                    db.Execute("INSERT INTO List_Country (CountryID, Country, RegionID, IconPath) VALUES (?, ?, ?, ?)",
                        countryId, name, country.RegionId, Flags + "L_FLA_" + country.Base + "_flag.tga");
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "List_Country: row {0} ({1}), region {2}, Country {3}", countryId, country.Name, country.RegionId, name));
                }
                if (fm3)
                {
                    db.Execute("INSERT INTO List_CarMake (ID, DisplayName, ManufacturerCode, CountryID, IconPath, IconPathLight, Profile) VALUES (?, ?, ?, ?, ?, ?, ?)",
                        id, strings[0], spec.Code, countryId,
                        Logos + @"Manufacturer_Logos\L_MAN_" + spec.Base + "_Logo.tga",
                        Logos + @"Manufacturer_Logos_SM\L_MAN_" + spec.Base + "_Logo.tga",
                        strings[1]);
                }
                else
                {
                    db.Execute("INSERT INTO List_CarMake (ID, DisplayName, ManufacturerCode, CountryID, IconPath, IconPathLight, IconPathBase, Profile) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                        id, strings[0], spec.Code, countryId,
                        Logos + @"Manufacturer_Logos_I\L_MAN_" + spec.Base + "_Logo.tga",
                        Logos + @"Manufacturer_Logos_SM\L_MAN_" + spec.Base + "_Logo.tga",
                        spec.Base, strings[1]);
                }
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "List_CarMake: row {0} ({1}, {2}), country {3}, DisplayName {4}, Profile {5}", id, spec.Code, spec.Name, spec.CountryId, strings[0], strings[1]));
                titles = MakeTitles.Add(db, id, spec.Name, spec.Base, stringZips, report);
                string livery = MakeDecals.LiveryPath(targetRoot);
                if (fm3)
                {
                    report.AppendLine("  decals: FM3's livery decals are FM3 models, not built here: none added");
                }
                else if (File.Exists(livery) && MakeDecals.Supported(db))
                {
                    liveryZip = new ForzaZip(In(livery, targetRoot, outputRoot));
                    ForzaZip.Entry model = liveryZip.Find(MakeDecals.TemplateEntry);
                    if (model == null)
                    {
                        throw new InvalidOperationException("Livery.zip has no " + MakeDecals.TemplateEntry + " to build the decal on.");
                    }
                    byte[] carbin;
                    using (Image image = LoadImage(spec.Logo))
                    {
                        carbin = MakeDecals.BuildCarbin(image, liveryZip.Read(model));
                    }
                    liveryZip.Add(MakeDecals.Add(db, id, spec.Name, liveryZip, stringZips, report), carbin);
                }
                else
                {
                    report.AppendLine("  decals: no Livery.zip or no decal tables, none added");
                }

                foreach (KeyValuePair<string, byte[]> file in logoFiles)
                {
                    ForzaZip.Entry entry = logos.Find(file.Key);
                    if (entry != null)
                    {
                        logos.Replace(entry, file.Value);
                    }
                    else
                    {
                        logos.Add(file.Key, file.Value);
                    }
                }

                // The writes: the database, the string zips, Textures.zip, then their manifest lines.
                List<string> written = new List<string>();
                string databaseOut = Out(database, targetRoot, outputRoot, stamp);
                File.WriteAllBytes(databaseOut, db.ToBytes());
                foreach (KeyValuePair<string, ForzaZip> zip in stringZips)
                {
                    zip.Value.Save(Out(zip.Key, targetRoot, outputRoot, stamp));
                    written.Add(zip.Key);
                }
                logos.Save(Out(textures, targetRoot, outputRoot, stamp));
                written.Add(textures);
                if (liveryZip != null)
                {
                    liveryZip.Save(Out(MakeDecals.LiveryPath(targetRoot), targetRoot, outputRoot, stamp));
                    written.Add(MakeDecals.LiveryPath(targetRoot));
                }

                if (titles != null)
                {
                    WriteTitleRule(titles, spec.Base, targetRoot, outputRoot, stamp, report);
                }

                string manifest = ZipManifest.PathFor(targetRoot);
                string manifestOut = Out(manifest, targetRoot, outputRoot, stamp);
                foreach (string path in written)
                {
                    if (!ZipManifest.Update(manifestOut, ZipManifest.GamePath(targetRoot, path), Out(path, targetRoot, outputRoot, stamp)))
                    {
                        report.AppendLine("  WARNING zipmanifest.xml does not list " + path);
                    }
                }
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "zipmanifest.xml: {0} lines updated", written.Count));
            }
            finally
            {
                db.Dispose();
            }
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "make {0} \"{1}\" created {2}", id, spec.Name, (outputRoot == null) ? "in the game" : "in the DLC pack trial folder " + outputRoot));
            if (liveryZip == null)
            {
                report.AppendLine("  no Livery_DecalsSortOrder rows: the make has no decals in the livery editor");
            }
            if (titles == null)
            {
                report.AppendLine("  no titles: the database has no badge and title table");
            }
            report.AppendLine("  the make shows in game once a car is under it (the port's Make choice)");
            return id;
        }

        // The make's Drive_ rule: in the game, added to BadgeAndTitleRules.xml (the game reads that file only;
        // backed up first); in a DLC trial folder, a file of its own (<base>.xml) that the pack build joins
        // into the pack's <offer>.xml.
        private static void WriteTitleRule(string block, string logoBase, string targetRoot, string outputRoot, string stamp, StringBuilder report)
        {
            if (outputRoot == null)
            {
                string rules = MakeTitles.GameRules(targetRoot);
                string text = File.ReadAllText(rules);
                if (text.IndexOf("id=\"Drive_" + logoBase + "\"", StringComparison.Ordinal) >= 0)
                {
                    report.AppendLine("  titles: " + MakeTitles.RulesFile + " has Drive_" + logoBase + " already");
                    return;
                }
                File.WriteAllText(Out(rules, targetRoot, null, stamp), MakeTitles.AddRule(text, block), new UTF8Encoding(false));
                report.AppendLine("  titles: Drive_" + logoBase + " added to " + MakeTitles.RulesFile);
                return;
            }
            string own = Path.Combine(MakeTitles.RulesFolder(outputRoot), logoBase + ".xml");
            Directory.CreateDirectory(Path.GetDirectoryName(own));
            File.WriteAllText(own, MakeTitles.Document(new string[] { block }), new UTF8Encoding(false));
            report.AppendLine("  titles: Drive_" + logoBase + " in the trial folder's " + logoBase + ".xml (the pack's rules file)");
        }

        // DisplayName and Profile of the make in List_CarMake.str of every language: two keys free in all
        // of them (from FFFE down, as the port does) and names IDS_DisplayName_<n> / IDS_Profile_<n>.
        // The table id (high 16 bits of the "_&" value) is the one the other makes have. Returns the
        // two "_&<id>" values for the row.
        private static string[] AddStrings(CarMakeSpec spec, SqliteDatabase db, string targetRoot, string outputRoot, Dictionary<string, ForzaZip> zips, long id, StringBuilder report)
        {
            string folder = Path.Combine(Path.Combine(targetRoot, "Media"), "StringTables");
            Dictionary<string, StrFile> files = new Dictionary<string, StrFile>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in Directory.GetFiles(folder, "*.zip"))
            {
                ForzaZip zip = new ForzaZip(In(path, targetRoot, outputRoot));
                ForzaZip.Entry entry = zip.Find("List_CarMake.str");
                if (entry == null)
                {
                    continue;
                }
                zips[path] = zip;
                files[path] = new StrFile(zip.Read(entry));
            }
            if (files.Count == 0)
            {
                throw new InvalidOperationException("No string table has List_CarMake.str.");
            }
            object other = db.Scalar("SELECT DisplayName FROM List_CarMake WHERE DisplayName LIKE '\\_&%' ESCAPE '\\' LIMIT 1");
            uint value;
            if (!(other is string) || !uint.TryParse(((string) other).Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out value))
            {
                throw new InvalidOperationException("List_CarMake has no string id to model the new one on.");
            }
            uint high = value & 0xFFFF0000u;
            int[] keys = new int[2];
            int found = 0;
            for (int candidate = 0xFFFE; candidate > 0 && found < 2; candidate--)
            {
                bool used = false;
                foreach (StrFile file in files.Values)
                {
                    used = used || file.Contains(candidate);
                }
                if (!used)
                {
                    keys[found++] = candidate;
                }
            }
            if (found < 2)
            {
                throw new InvalidOperationException("The string tables have no free keys.");
            }
            int displayNumber = NextNumber(files, "IDS_DisplayName_");
            int profileNumber = NextNumber(files, "IDS_Profile_");
            foreach (KeyValuePair<string, StrFile> pair in files)
            {
                string language = Path.GetFileNameWithoutExtension(pair.Key);
                string name;
                string profile;
                if (!spec.Names.TryGetValue(language, out name) || string.IsNullOrEmpty(name))
                {
                    name = spec.Name;
                }
                if (!spec.Profiles.TryGetValue(language, out profile) || string.IsNullOrEmpty(profile))
                {
                    profile = spec.Profile ?? string.Empty;
                }
                pair.Value.Set(keys[0], name, "IDS_DisplayName_" + displayNumber.ToString(CultureInfo.InvariantCulture));
                pair.Value.Set(keys[1], profile, "IDS_Profile_" + profileNumber.ToString(CultureInfo.InvariantCulture));
                ForzaZip zip = zips[pair.Key];
                zip.Replace(zip.Find("List_CarMake.str"), pair.Value.ToBytes());
            }
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "strings: keys {0:X4} (DisplayName) and {1:X4} (Profile) in List_CarMake.str of {2} languages", keys[0], keys[1], files.Count));
            return new string[] { "_&" + (high | (uint) keys[0]).ToString(CultureInfo.InvariantCulture), "_&" + (high | (uint) keys[1]).ToString(CultureInfo.InvariantCulture) };
        }

        // The country's name in List_Country.str of every language (the zips AddStrings opened): one key
        // free in all of them (from FFFE down) with the table id the other countries have, named
        // IDS_Country_<n>. Returns the "_&<id>" value for the row.
        private static string AddCountryName(CarCountrySpec country, SqliteDatabase db, Dictionary<string, ForzaZip> zips, long id, StringBuilder report)
        {
            Dictionary<string, StrFile> files = new Dictionary<string, StrFile>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, ForzaZip> pair in zips)
            {
                ForzaZip.Entry entry = pair.Value.Find("List_Country.str");
                if (entry != null)
                {
                    files[pair.Key] = new StrFile(pair.Value.Read(entry));
                }
            }
            if (files.Count == 0)
            {
                throw new InvalidOperationException("No string table has List_Country.str.");
            }
            object other = db.Scalar("SELECT Country FROM List_Country WHERE Country LIKE '\\_&%' ESCAPE '\\' LIMIT 1");
            uint value;
            if (!(other is string) || !uint.TryParse(((string) other).Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out value))
            {
                throw new InvalidOperationException("List_Country has no string id to model the new one on.");
            }
            uint high = value & 0xFFFF0000u;
            int key = 0;
            for (int candidate = 0xFFFE; candidate > 0 && key == 0; candidate--)
            {
                bool used = false;
                foreach (StrFile file in files.Values)
                {
                    used = used || file.Contains(candidate);
                }
                if (!used)
                {
                    key = candidate;
                }
            }
            if (key == 0)
            {
                throw new InvalidOperationException("List_Country.str has no free key.");
            }
            int number = NextNumber(files, "IDS_Country_");
            foreach (KeyValuePair<string, StrFile> pair in files)
            {
                pair.Value.Set(key, country.Name.Trim(), "IDS_Country_" + number.ToString(CultureInfo.InvariantCulture));
                ForzaZip zip = zips[pair.Key];
                zip.Replace(zip.Find("List_Country.str"), pair.Value.ToBytes());
            }
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "strings: key {0:X4} in List_Country.str of {1} languages", key, files.Count));
            return "_&" + (high | (uint) key).ToString(CultureInfo.InvariantCulture);
        }

        // L_FLA_<base>_flag.xds: a texture laid out as an existing flag (size, format, gamma, mips) holding
        // the image stretched over all of it.
        private static byte[] BuildFlag(CarCountrySpec country, ForzaZip textures, string texconv, StringBuilder report)
        {
            string name = "UI4/Logos/Country_Flags/L_FLA_" + country.Base + "_flag.xds";
            if (textures.Find(name) != null)
            {
                throw new InvalidOperationException("Textures.zip already has " + name + ".");
            }
            ForzaZip.Entry model = textures.Find("UI4/Logos/Country_Flags/L_FLA_Italy_flag.xds");
            if (model == null)
            {
                throw new InvalidOperationException("Textures.zip has no flag to model the new one on.");
            }
            XdsTexture like = new XdsTexture(textures.Read(model));
            XdsTexture flag = XdsTexture.Create(like.Width, like.Height, like.Codec, like.IsGamma, like.LevelCount);
            string temp = Path.Combine(Path.GetTempPath(), "ForzaStudio_flag_" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                using (Image image = LoadImage(country.Flag))
                using (Bitmap stretched = new Bitmap(like.Width, like.Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(stretched))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(image, 0, 0, like.Width, like.Height);
                    }
                    stretched.Save(temp, ImageFormat.Png);
                }
                flag.ImportImage(temp, texconv);
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "flag {0}: {1}x{2} {3}, {4} levels, as the Italian one", name, like.Width, like.Height, like.FormatName, like.LevelCount));
            return flag.ToBytes();
        }

        // The number after the highest "<prefix><n>" name in any language.
        private static int NextNumber(Dictionary<string, StrFile> files, string prefix)
        {
            int max = 0;
            foreach (StrFile file in files.Values)
            {
                foreach (StrFile.Entry entry in file.Entries)
                {
                    int n;
                    if (entry.Name != null && entry.Name.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(entry.Name.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out n))
                    {
                        max = Math.Max(max, n);
                    }
                }
            }
            return max + 1;
        }

        // L_MAN_<base>_Logo.xds for each logo folder: a texture laid out as the template make's logo in
        // that folder (size, format, gamma, mips) holding the image fitted in its size.
        private static Dictionary<string, byte[]> BuildLogos(CarMakeSpec spec, ForzaZip textures, string texconv, StringBuilder report, string[] folders)
        {
            string template = string.IsNullOrEmpty(spec.TemplateBase) ? "Acura" : spec.TemplateBase;
            Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using (Image image = LoadImage(spec.Logo))
            {
                foreach (string folder in folders)
                {
                    string name = "UI4/Logos/" + folder + "/L_MAN_" + spec.Base + "_Logo.xds";
                    if (textures.Find(name) != null)
                    {
                        throw new InvalidOperationException("Textures.zip already has " + name + ".");
                    }
                    ForzaZip.Entry model = textures.Find("UI4/Logos/" + folder + "/L_MAN_" + template + "_Logo.xds");
                    if (model == null)
                    {
                        throw new InvalidOperationException("The template make " + template + " has no logo in " + folder + ".");
                    }
                    XdsTexture like = new XdsTexture(textures.Read(model));
                    XdsTexture logo = XdsTexture.Create(like.Width, like.Height, like.Codec, like.IsGamma, like.LevelCount);
                    string temp = Path.Combine(Path.GetTempPath(), "ForzaStudio_logo_" + Guid.NewGuid().ToString("N") + ".png");
                    try
                    {
                        using (Bitmap fitted = Fit(image, like.Width, like.Height))
                        {
                            fitted.Save(temp, ImageFormat.Png);
                        }
                        logo.ImportImage(temp, texconv);
                    }
                    finally
                    {
                        if (File.Exists(temp))
                        {
                            File.Delete(temp);
                        }
                    }
                    files[name] = logo.ToBytes();
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "logo {0}: {1}x{2} {3}, {4} levels, as {5}'s", folder, like.Width, like.Height, like.FormatName, like.LevelCount, template));
                }
            }
            return files;
        }

        internal static Image LoadImage(string path)
        {
            using (Image loaded = Image.FromFile(path))
            {
                return new Bitmap(loaded);
            }
        }

        // The image scaled to fit the size (aspect kept, a small margin) on a transparent canvas.
        internal static Bitmap Fit(Image image, int width, int height)
        {
            Bitmap canvas = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                double scale = Math.Min((width * 0.92) / image.Width, (height * 0.92) / image.Height);
                int w = Math.Max(1, (int) Math.Round(image.Width * scale));
                int h = Math.Max(1, (int) Math.Round(image.Height * scale));
                g.DrawImage(image, (width - w) / 2, (height - h) / 2, w, h);
            }
            return canvas;
        }

        // A game file where it is read: its copy under the trial folder when there is one.
        private static string In(string path, string targetRoot, string outputRoot)
        {
            if (outputRoot == null)
            {
                return path;
            }
            string copy = Path.Combine(outputRoot, Relative(path, targetRoot));
            return File.Exists(copy) ? copy : path;
        }

        // Where it is written: itself (backed up to bak\), or its place under the trial folder.
        private static string Out(string path, string targetRoot, string outputRoot, string stamp)
        {
            if (outputRoot == null)
            {
                if (File.Exists(path) && BackupPolicy.Enabled)
                {
                    string bak = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bak");
                    Directory.CreateDirectory(bak);
                    string copy = Path.Combine(bak, stamp + "_make_" + Path.GetFileName(path));
                    if (!File.Exists(copy))
                    {
                        File.Copy(path, copy);
                    }
                }
                return path;
            }
            string output = Path.Combine(outputRoot, Relative(path, targetRoot));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            if (!File.Exists(output) && File.Exists(path) && path.EndsWith("zipmanifest.xml", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(path, output);
            }
            return output;
        }

        private static string Relative(string path, string root)
        {
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path.Substring(root.TrimEnd('\\').Length + 1) : path;
        }
    }
}
