namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Imaging;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;

    // The lookup rows a car ported into FM2 needs and FM2 lacks, added as the FM3 port adds a make (CarPortToFm3.PortMake).
    // User 2026-11-26: "sì, aggiungi le marche come FM3" and "sì a entrambi" (cylinders by meaning, part makers by name).
    // Harnesses: E:\DEV\fm4dev\PortToFM2\shtest (makecmp, ctry, cyl, pm, makeport).
    //
    // MAKE (List_CarMake): make ids are the same in every Forza (same id = same ManufacturerCode, no exception); FM2 has 1-53,
    //   so a Hummer (85) or a RUF (91) went under the template's make. Row on the source's id; DisplayName / Profile keys in
    //   List_CarMake.str of every language; IconPath = IconPathLight = ...\L_Map\Manufacturer_Logos\L_MAN_<base>_Logo.tga and
    //   the logo in UI\Textures.zip. FM2's country rows carry a flag Symbol (a character of its font): a make whose country
    //   FM2 lacks (Holden, Joss: Australia) is refused before anything is written.
    // CYLINDERS (List_Cylinders): the ids mean other things in FM2 and FM4 (FM4 10 = electric, 11 = inline 3, 12 = four
    //   rotors; FM2 10 = four rotors, no 11 / 12): the 787B (four rotors) became a V8, the 107 (three cylinders) a four.
    //   Matched by Number + Rotors; a kind FM2 lacks gets a row (next id) with its Description in List_Cylinders.str.
    // PART MAKERS (List_PartManufacturer, the ManufacturerID of upgrade rows): same id = same name on 224 ids, another name on
    //   55 (FM4 5 Vortech, FM2 5 Amuse), and FM2 lacks 290 of FM4's. Matched by name (FM2 keeps the name as text, FM4 / FH as
    //   a string key); a maker FM2 lacks (ABT, URAS) gets a row (next id), its name, and its logo as the next decal<n> of
    //   L_Map\Car_Parts_Logos_256x128 (FM2's naming).
    // Logos: laid out as one of FM2's of the same folder (size, format, gamma, levels), from the source's (FM4 / FH undone to
    // FM3's linear values first, as the car textures), the transparent margins cut off.
    // Prepare / Resolve put rows into the database being converted; Commit writes the string and texture zips after the
    // database check passed.
    internal sealed class Fm2LookupPort
    {
        public string SourceRoot;           // the source game folder, or a staged DLC package
        public string SourceFallbackRoot;   // a staged package: its base game (strings, logos); else null
        public string TargetRoot;           // the FM2 folder
        public string BackupFolder;         // copies of the zips before they change (null: none)
        public List<string> Log = new List<string>();

        private const string MakeLogos = "L_Map/Manufacturer_Logos/";
        private const string PartLogos = "L_Map/Car_Parts_Logos_256x128/";

        private SqliteDatabase source;
        private SqliteDatabase target;
        private readonly Dictionary<string, ForzaZip> stringZips = new Dictionary<string, ForzaZip>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, StrFile>> strFiles = new Dictionary<string, Dictionary<string, StrFile>>(StringComparer.OrdinalIgnoreCase);
        private ForzaZip textures;
        private bool texturesChanged;
        private readonly Dictionary<long, long> cylinders = new Dictionary<long, long>();
        private readonly Dictionary<long, long> partMakers = new Dictionary<long, long>();

        // ---- before anything is written ----

        public static long SourceMake(SqliteDatabase source, string mediaName)
        {
            object make = source.Scalar("SELECT MakeID FROM Data_Car WHERE MediaName = ? COLLATE NOCASE", mediaName);
            return (make == null || make is DBNull) ? -1 : Convert.ToInt64(make, CultureInfo.InvariantCulture);
        }

        // Why the car's make cannot go into FM2, or null (FM2 has it, or it can be added).
        public static string MakeProblem(SqliteDatabase source, SqliteDatabase target, long make)
        {
            if (make < 0 || Count(target, "SELECT COUNT(*) FROM List_CarMake WHERE ID = ?", make) > 0)
            {
                return null;
            }
            SqliteDatabase.Result row = source.Query("SELECT ManufacturerCode, CountryID FROM List_CarMake WHERE ID = ?", make);
            if (row.Rows.Count == 0)
            {
                return "make " + make + " is in neither FM2 nor the source's database";
            }
            if (Count(target, "SELECT COUNT(*) FROM List_Country WHERE CountryID = ?", row.Rows[0][1]) == 0)
            {
                return string.Format(CultureInfo.InvariantCulture, "the make {0} (id {1}) is from country {2}, which FM2 lacks: FM2's country rows carry a flag symbol of its font and there is none to give it",
                    row.Rows[0][0], make, row.Rows[0][1]);
            }
            return null;
        }

        // ---- the database being converted ----

        public void Begin(SqliteDatabase sourceDb, SqliteDatabase targetDb)
        {
            this.source = sourceDb;
            this.target = targetDb;
        }

        // Adds the make's row when FM2 lacks it (so Data_Car.MakeID stays the source's). False: FM2 has it.
        public bool PrepareMake(long make)
        {
            if (make < 0 || Count(this.target, "SELECT COUNT(*) FROM List_CarMake WHERE ID = ?", make) > 0)
            {
                return false;
            }
            string problem = MakeProblem(this.source, this.target, make);
            if (problem != null)
            {
                throw new InvalidOperationException("The car's make cannot go into FM2: " + problem + ".");
            }
            object[] row = this.source.Query("SELECT DisplayName, ManufacturerCode, CountryID, IconPath, IconPathLight, Profile FROM List_CarMake WHERE ID = ?", make).Rows[0];
            string code = Convert.ToString(row[1], CultureInfo.InvariantCulture);
            uint high = High(this.target, "SELECT DisplayName FROM List_CarMake WHERE DisplayName LIKE '\\_&%' ESCAPE '\\' LIMIT 1");
            string name = this.AddString("List_CarMake.str", Convert.ToString(row[0], CultureInfo.InvariantCulture), code, high, "IDS_DisplayName_", null);
            string profile = this.AddString("List_CarMake.str", Convert.ToString(row[5], CultureInfo.InvariantCulture), code, high, "IDS_Profile_", name);
            string logoName = Path.GetFileNameWithoutExtension(Convert.ToString(row[3], CultureInfo.InvariantCulture).Replace('/', '\\'));
            if (string.IsNullOrEmpty(logoName))
            {
                logoName = "L_MAN_" + code + "_Logo";
            }
            string icon = @"GAME:\Media\UI\Textures\L_Map\Manufacturer_Logos\" + logoName + ".tga";
            this.target.Execute("INSERT INTO List_CarMake (ID, DisplayName, ManufacturerCode, CountryID, IconPath, IconPathLight, Profile) VALUES (?, ?, ?, ?, ?, ?, ?)",
                make, name, code, row[2], icon, icon, profile);
            this.Log.Add(string.Format(CultureInfo.InvariantCulture, "make {0}: FM2 lacked it, List_CarMake row added on the source's id {1} (country {2}), name / profile keys {3} / {4}", code, make, row[2], name, profile));
            this.Logo(Convert.ToString(row[3], CultureInfo.InvariantCulture), MakeLogos, logoName, "make " + code);
            return true;
        }

        // CarDataToFm2.IdResolver: the FM2 id of a source id of a lookup this class handles, or null (the conversion's rules).
        public object Resolve(string table, string column, string level, object sourceValue)
        {
            long id;
            if (sourceValue == null || sourceValue is DBNull || !long.TryParse(Convert.ToString(sourceValue, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
            {
                return null;
            }
            if (string.Equals(column, "CylinderID", StringComparison.OrdinalIgnoreCase))
            {
                return this.Cylinder(id);
            }
            // the car's make: the same id in every Forza; a make PrepareMake added is used by no FM2 car yet, so the
            // conversion's "id no FM2 row uses" check would keep the template's
            if (string.Equals(table, "Data_Car", StringComparison.OrdinalIgnoreCase) && string.Equals(column, "MakeID", StringComparison.OrdinalIgnoreCase))
            {
                return (Count(this.target, "SELECT COUNT(*) FROM List_CarMake WHERE ID = ?", id) > 0) ? (object) id : null;
            }
            if (string.Equals(column, "ManufacturerID", StringComparison.OrdinalIgnoreCase) && table.StartsWith("List_Upgrade", StringComparison.OrdinalIgnoreCase) && level != "stock" && id > 0)
            {
                return this.PartMaker(id);
            }
            return null;
        }

        private object Cylinder(long id)
        {
            long found;
            if (this.cylinders.TryGetValue(id, out found))
            {
                return found;
            }
            SqliteDatabase.Result row = this.source.Query("SELECT Number, Rotors, Description FROM List_Cylinders WHERE CylinderID = ?", id);
            if (row.Rows.Count == 0)
            {
                return null;
            }
            object number = row.Rows[0][0], rotors = row.Rows[0][1];
            object same = this.target.Scalar("SELECT CylinderID FROM List_Cylinders WHERE Number = ? AND Rotors = ? ORDER BY CylinderID LIMIT 1", number, rotors);
            if (same != null && !(same is DBNull))
            {
                found = Convert.ToInt64(same, CultureInfo.InvariantCulture);
                this.Log.Add(string.Format(CultureInfo.InvariantCulture, "cylinders: source id {0} ({1} cylinders, {2} rotors) = FM2's {3}", id, number, rotors, found));
            }
            else
            {
                found = Math.Max(Convert.ToInt64(this.target.Scalar("SELECT ifnull(MAX(CylinderID), 0) FROM List_Cylinders"), CultureInfo.InvariantCulture), PackMax("List_Cylinders", "CylinderID")) + 1;   // also above the installed packs'
                uint high = High(this.target, "SELECT Description FROM List_Cylinders WHERE Description LIKE '\\_&%' ESCAPE '\\' LIMIT 1");
                string text = Convert.ToString(number, CultureInfo.InvariantCulture) + (Convert.ToInt64(rotors, CultureInfo.InvariantCulture) != 0 ? " rotors" : " cylinders");
                string description = this.AddString("List_Cylinders.str", Convert.ToString(row.Rows[0][2], CultureInfo.InvariantCulture), text, high, "IDS_Description_", null);
                this.target.Execute("INSERT INTO List_Cylinders (CylinderID, Number, Description, Rotors) VALUES (?, ?, ?, ?)", found, number, description, rotors);
                this.Log.Add(string.Format(CultureInfo.InvariantCulture, "cylinders: FM2 lacked {0} cylinders / {1} rotors (source id {2}): List_Cylinders row {3} added, description key {4}", number, rotors, id, found, description));
            }
            this.cylinders[id] = found;
            return found;
        }

        private object PartMaker(long id)
        {
            long found;
            if (this.partMakers.TryGetValue(id, out found))
            {
                return found;
            }
            SqliteDatabase.Result row = this.source.Query("SELECT PartManufacturer, LogoPath FROM List_PartManufacturer WHERE Id = ?", id);
            if (row.Rows.Count == 0)
            {
                return null;
            }
            string value = Convert.ToString(row.Rows[0][0], CultureInfo.InvariantCulture);
            StrFile.Entry entry = this.SourceString("EN", "List_PartManufacturer.str", value);
            string name = (entry != null) ? entry.Text : (value.StartsWith("_&", StringComparison.Ordinal) ? null : value);
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            name = name.Trim();
            // the game's own race-part brand: FM2 "Forza" (35, on 206 of its race bumpers), FH2 "Forza", FM4 "Forza
            // Motorsport 4" for the same id 492 (shtest\pm.cs, pm2.cs)
            if (name.StartsWith("Forza ", StringComparison.OrdinalIgnoreCase) && this.target.Scalar("SELECT Id FROM List_PartManufacturer WHERE PartManufacturer = 'Forza'") != null)
            {
                name = "Forza";
            }
            // by name, a country FM2 adds in brackets left aside ("Chargespeed (Japan)" = FM4's "Chargespeed", the same id 21)
            object same = null;
            foreach (object[] fm2 in this.target.Query("SELECT Id, PartManufacturer FROM List_PartManufacturer ORDER BY Id").Rows)
            {
                if (string.Equals(BareName(Convert.ToString(fm2[1], CultureInfo.InvariantCulture)), BareName(name), StringComparison.OrdinalIgnoreCase))
                {
                    same = fm2[0];
                    break;
                }
            }
            if (same != null && !(same is DBNull))
            {
                found = Convert.ToInt64(same, CultureInfo.InvariantCulture);
                if (found != id)
                {
                    this.Log.Add(string.Format(CultureInfo.InvariantCulture, "part maker {0}: source id {1} = FM2's {2} (by name)", name, id, found));
                }
            }
            else
            {
                found = Math.Max(Convert.ToInt64(this.target.Scalar("SELECT ifnull(MAX(Id), 0) FROM List_PartManufacturer"), CultureInfo.InvariantCulture), PackMax("List_PartManufacturer", "Id")) + 1;   // also above the installed packs'
                string decal = "decal" + this.NextDecal().ToString("000", CultureInfo.InvariantCulture);
                string logoPath = @"GAME:\Media\UI\Textures\L_Map\Car_Parts_Logos_256x128\" + decal + ".TGA";
                this.target.Execute("INSERT INTO List_PartManufacturer (Id, PartManufacturer, LogoPath) VALUES (?, ?, ?)", found, name, logoPath);
                this.Log.Add(string.Format(CultureInfo.InvariantCulture, "part maker {0}: FM2 lacked it, List_PartManufacturer row {1} added (source id {2}), logo {3}", name, found, id, decal));
                string sourceLogo = Convert.ToString(row.Rows[0][1], CultureInfo.InvariantCulture);
                if (sourceLogo.IndexOf('\\') < 0 && sourceLogo.IndexOf('/') < 0)
                {
                    sourceLogo = @"GAME:\Media\UI\Textures\UI4\Logos\Car_Parts_Logos\" + sourceLogo + ".tga";
                }
                this.Logo(sourceLogo, PartLogos, decal, "part maker " + name);
            }
            this.partMakers[id] = found;
            return found;
        }

        // A part maker's name without a trailing "(...)" (FM2 adds the country to some: "Garage Valley (Japan)").
        private static string BareName(string name)
        {
            string text = (name ?? "").Trim();
            if (text.EndsWith(")", StringComparison.Ordinal) && text.LastIndexOf(" (", StringComparison.Ordinal) > 0)
            {
                text = text.Substring(0, text.LastIndexOf(" (", StringComparison.Ordinal)).Trim();
            }
            return text;
        }

        // The number after FM2's highest decal<n> (its part maker rows and logos).
        private int NextDecal()
        {
            int top = 0;
            foreach (object[] row in this.target.Query("SELECT LogoPath FROM List_PartManufacturer").Rows)
            {
                top = Math.Max(top, DecalNumber(Convert.ToString(row[0], CultureInfo.InvariantCulture)));
            }
            ForzaZip zip = this.Textures();
            if (zip != null)
            {
                foreach (ForzaZip.Entry entry in zip.Entries)
                {
                    if (entry.Name.Replace('\\', '/').StartsWith(PartLogos, StringComparison.OrdinalIgnoreCase))
                    {
                        top = Math.Max(top, DecalNumber(entry.Name));
                    }
                }
            }
            top = Math.Max(top, (int) PackMax("List_PartManufacturer", "LogoPath"));     // a pack's logo file of that name would be the other maker's
            return top + 1;
        }

        private static int DecalNumber(string path)
        {
            string file = Path.GetFileNameWithoutExtension(path.Replace('/', '\\'));
            int n;
            return (file.StartsWith("decal", StringComparison.OrdinalIgnoreCase) && int.TryParse(file.Substring(5), NumberStyles.None, CultureInfo.InvariantCulture, out n)) ? n : 0;
        }

        // ---- strings ----

        // A new key of 'table' (the source's when free in every language, else from FFFE down) holding the source's text of
        // that language (else English, else 'fallback'); returns the "_&n" value.
        private string AddString(string table, string sourceValue, string fallback, uint high, string namePrefix, string notThis)
        {
            Dictionary<string, StrFile> files = this.StrFiles(table);
            uint stringId;
            int key = (sourceValue.StartsWith("_&", StringComparison.Ordinal) && uint.TryParse(sourceValue.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out stringId)) ? (int) (stringId & 0xFFFF) : -1;
            if (key < 0 || Used(files, key) || Key(high, key) == notThis)
            {
                key = 0xFFFE;
                while (key > 0 && (Used(files, key) || Key(high, key) == notThis))
                {
                    key--;
                }
            }
            foreach (KeyValuePair<string, StrFile> language in files)
            {
                StrFile.Entry text = this.SourceString(language.Key, table, sourceValue);
                if (text != null)
                {
                    language.Value.Set(key, text.Text, text.Name);
                }
                else
                {
                    language.Value.Set(key, fallback, namePrefix + key.ToString(CultureInfo.InvariantCulture));
                }
            }
            return Key(high, key);
        }

        private Dictionary<string, StrFile> StrFiles(string table)
        {
            Dictionary<string, StrFile> files;
            if (this.strFiles.TryGetValue(table, out files))
            {
                return files;
            }
            files = new Dictionary<string, StrFile>(StringComparer.OrdinalIgnoreCase);
            string folder = Path.Combine(Path.Combine(this.TargetRoot, "Media"), "StringTables");
            foreach (string zip in Directory.GetFiles(folder, "*.zip"))
            {
                string language = Path.GetFileNameWithoutExtension(zip);
                ForzaZip archive;
                if (!this.stringZips.TryGetValue(language, out archive))
                {
                    archive = new ForzaZip(zip);
                    this.stringZips[language] = archive;
                }
                ForzaZip.Entry entry = archive.Find(table);
                if (entry != null)
                {
                    files[language] = new StrFile(archive.Read(entry));
                }
            }
            this.strFiles[table] = files;
            return files;
        }

        // The source's text of a "_&n" value in a language's table (the package first, then its game); English when the
        // language is missing.
        private StrFile.Entry SourceString(string language, string table, string value)
        {
            uint stringId;
            if (string.IsNullOrEmpty(value) || !value.StartsWith("_&", StringComparison.Ordinal) || !uint.TryParse(value.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out stringId))
            {
                return null;
            }
            int key = (int) (stringId & 0xFFFF);
            foreach (string lang in new string[] { language, "EN" })
            {
                foreach (string root in new string[] { this.SourceRoot, this.SourceFallbackRoot })
                {
                    string zip = (root == null) ? null : Path.Combine(Path.Combine(Path.Combine(root, "Media"), "StringTables"), lang + ".zip");
                    if (zip == null || !File.Exists(zip))
                    {
                        continue;
                    }
                    ForzaZip archive = new ForzaZip(zip);
                    ForzaZip.Entry entry = archive.Find(table);
                    if (entry == null)
                    {
                        continue;
                    }
                    foreach (StrFile.Entry text in new StrFile(archive.Read(entry)).Entries)
                    {
                        if (text.Key == key)
                        {
                            return text;
                        }
                    }
                }
            }
            return null;
        }

        // ---- logos ----

        private ForzaZip Textures()
        {
            if (this.textures == null)
            {
                string path = Path.Combine(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "UI"), "Textures.zip");
                if (File.Exists(path))
                {
                    this.textures = new ForzaZip(path);
                }
            }
            return this.textures;
        }

        private void Logo(string sourceIcon, string folder, string logoName, string what)
        {
            ForzaZip zip = this.Textures();
            if (zip == null)
            {
                this.Log.Add(what + ": FM2 has no UI\\Textures.zip, no logo");
                return;
            }
            GamePaths.GameFile from = null;
            ForzaGame sourceGame = ForzaGame.Unknown;
            foreach (string root in new string[] { this.SourceRoot, this.SourceFallbackRoot })
            {
                if (root == null)
                {
                    continue;
                }
                if (from == null)
                {
                    from = GamePaths.Locate(root, sourceIcon);
                }
                if (sourceGame == ForzaGame.Unknown)
                {
                    sourceGame = GameInfo.Detect(root);
                }
            }
            if (from == null)
            {
                this.Log.Add(what + ": the source's logo " + sourceIcon + " was not found, no logo (shown without it)");
                return;
            }
            byte[] data = GamePaths.Read(from);
            if (sourceGame != ForzaGame.Motorsport3)
            {
                data = Fm3Textures.FromMotorsport4(data, logoName);
            }
            using (Bitmap whole = new XdsTexture(data).GetLevelBitmap(0))
            {
                this.PutLogo(whole, folder, logoName, what, "the source's " + Path.GetFileName(from.InZip ? from.EntryName : from.FilePath));
            }
        }

        // A logo picture into FM2's folder, laid out as the folder's first logo (size, format, levels), margins cut.
        private void PutLogo(Bitmap whole, string folder, string logoName, string what, string fromText)
        {
            ForzaZip zip = this.Textures();
            ForzaZip.Entry like = null;
            foreach (ForzaZip.Entry entry in zip.Entries)
            {
                if (!entry.IsDirectory && entry.Name.Replace('\\', '/').StartsWith(folder, StringComparison.OrdinalIgnoreCase) && entry.Name.EndsWith(".xds", StringComparison.OrdinalIgnoreCase))
                {
                    like = entry;
                    break;
                }
            }
            if (like == null)
            {
                this.Log.Add(what + ": FM2 has no logo in " + folder + " to copy the layout of, no logo (shown without it)");
                return;
            }
            XdsTexture layout = new XdsTexture(zip.Read(like));
            XdsTexture logo = XdsTexture.Create(layout.Width, layout.Height, layout.Codec, layout.IsGamma, layout.LevelCount);
            string temp = Path.Combine(Path.GetTempPath(), "ForzaStudio_fm2logo_" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                // FM2's logos fill their frame; the source's have wide transparent margins: cut them off first
                using (Bitmap picture = Trim(whole))
                using (Bitmap fitted = CarMake.Fit(picture, layout.Width, layout.Height))
                {
                    fitted.Save(temp, ImageFormat.Png);
                }
                logo.ImportImage(temp, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "texconv.exe"));
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            string name = folder + logoName + ".xds";
            ForzaZip.Entry existing = zip.Find(name);
            if (existing != null)
            {
                zip.Replace(existing, logo.ToBytes());
            }
            else
            {
                zip.Add(name, logo.ToBytes());
            }
            this.texturesChanged = true;
            this.Log.Add(string.Format(CultureInfo.InvariantCulture, "{0}: logo {1} {2}x{3} {4}, {5} levels, laid out as FM2's {6}, from {7}",
                what, name, layout.Width, layout.Height, layout.FormatName, layout.LevelCount, Path.GetFileName(like.Name), fromText));
        }

        // ---- a make made by the user (New make in the FM2 port window) ----

        // The id a new make gets: above every make id of every Forza (GameIdLimits) and of FM2's database, so the rule
        // "same make, same id in every Forza" (the port's lookups) never meets another make under that id.
        public static long FreeMakeId(SqliteDatabase fm2)
        {
            long id = 53;
            foreach (ForzaGame game in new[] { ForzaGame.Motorsport2, ForzaGame.Motorsport3, ForzaGame.Motorsport4, ForzaGame.Horizon1, ForzaGame.Horizon2 })
            {
                id = Math.Max(id, GameIdLimits.Max(game, "List_CarMake", "ID"));
            }
            object max = fm2.Scalar("SELECT max(ID) FROM List_CarMake");
            if (max != null && !(max is DBNull))
            {
                id = Math.Max(id, Convert.ToInt64(max, CultureInfo.InvariantCulture));
            }
            id = Math.Max(id, PackMax("List_CarMake", "ID"));
            return id + 1;
        }

        // The largest value of a column in the merges of the FM2 packs installed in Xenia (0: none, or no Xenia folder): an id
        // the port adds must not be one another pack added already (two packs, each with a new part maker, both took 290:
        // the second could not be built nor added, 2026-11-27).
        private static long PackMax(string table, string column)
        {
            long top = 0;
            foreach (string merge in DbIds.PackDatabases(InstalledDlc.ContentFolder(ForzaGame.Motorsport2)))
            {
                try
                {
                    using (SqliteDatabase db = SqliteDatabase.FromBytes(File.ReadAllBytes(merge)))
                    {
                        if (!DbIds.HasTable(db, table) || Array.IndexOf(db.Query("SELECT * FROM " + SqliteDatabase.Quote(table) + " LIMIT 0").Columns, column) < 0)
                        {
                            continue;
                        }
                        if (column == "LogoPath")
                        {
                            foreach (object[] row in db.Query("SELECT LogoPath FROM " + SqliteDatabase.Quote(table)).Rows)
                            {
                                top = Math.Max(top, DecalNumber(Convert.ToString(row[0], CultureInfo.InvariantCulture)));
                            }
                            continue;
                        }
                        object max = db.Scalar("SELECT max(" + SqliteDatabase.Quote(column) + ") FROM " + SqliteDatabase.Quote(table));
                        if (max != null && !(max is DBNull))
                        {
                            top = Math.Max(top, Convert.ToInt64(max, CultureInfo.InvariantCulture));
                        }
                    }
                }
                catch (Exception)
                {
                    // a merge that does not open: its ids are not known
                }
            }
            return top;
        }

        // Why a make made in the window cannot go into FM2, or null.
        public static string MakeSpecProblem(CarMakeSpec spec, SqliteDatabase fm2, string fm2Root)
        {
            if (spec.Id <= 53)
            {
                return "The Id must be above FM2's makes (1-53).";
            }
            if (Count(fm2, "SELECT COUNT(*) FROM List_CarMake WHERE ID = ?", spec.Id) > 0)
            {
                return "FM2 has a make with Id " + spec.Id + ".";
            }
            if (string.IsNullOrEmpty(spec.Name))
            {
                return "Give the make a name.";
            }
            if (!Regex.IsMatch(spec.Code ?? "", "^[A-Z0-9]{2,4}$"))
            {
                return "The code is 2-4 capital letters or digits.";
            }
            if (Count(fm2, "SELECT COUNT(*) FROM List_CarMake WHERE ManufacturerCode = ? COLLATE NOCASE", spec.Code) > 0)
            {
                return "FM2 has a make with the code " + spec.Code + ".";
            }
            if (spec.NewCountry != null)
            {
                return "FM2 takes no new country: its country rows carry a flag symbol of its font.";
            }
            if (Count(fm2, "SELECT COUNT(*) FROM List_Country WHERE CountryID = ?", spec.CountryId) == 0)
            {
                return "Choose one of FM2's countries.";
            }
            if (!Regex.IsMatch(spec.Base ?? "", "^[A-Za-z0-9]+$"))
            {
                return "The logo name is letters and digits.";
            }
            string textures = Path.Combine(Path.Combine(Path.Combine(fm2Root, "Media"), "UI"), "Textures.zip");
            if (File.Exists(textures) && new ForzaZip(textures).Find(MakeLogos + "L_MAN_" + spec.Base + "_Logo.xds") != null)
            {
                return "FM2 has a logo L_MAN_" + spec.Base + "_Logo already: choose another logo name.";
            }
            if (string.IsNullOrEmpty(spec.Logo) || !File.Exists(spec.Logo))
            {
                return "Choose the logo image.";
            }
            return null;
        }

        // The make's row, its name and profile in every language (the same text in each) and its logo, as a ported make
        // gets them (PrepareMake). Begin first; Commit writes the string tables and Textures.zip, the caller the database.
        public void AddMake(CarMakeSpec spec)
        {
            string problem = MakeSpecProblem(spec, this.target, this.TargetRoot);
            if (problem != null)
            {
                throw new InvalidOperationException(problem);
            }
            uint high = High(this.target, "SELECT DisplayName FROM List_CarMake WHERE DisplayName LIKE '\\_&%' ESCAPE '\\' LIMIT 1");
            string name = this.AddString("List_CarMake.str", "", spec.Name, high, "IDS_DisplayName_", null);
            string profile = this.AddString("List_CarMake.str", "", spec.Profile ?? "", high, "IDS_Profile_", name);
            string logoName = "L_MAN_" + spec.Base + "_Logo";
            string icon = @"GAME:\Media\UI\Textures\L_Map\Manufacturer_Logos\" + logoName + ".tga";
            this.target.Execute("INSERT INTO List_CarMake (ID, DisplayName, ManufacturerCode, CountryID, IconPath, IconPathLight, Profile) VALUES (?, ?, ?, ?, ?, ?, ?)",
                spec.Id, name, spec.Code, spec.CountryId, icon, icon, profile);
            this.Log.Add(string.Format(CultureInfo.InvariantCulture, "make {0}: new make {1}, List_CarMake row {2} (country {3}), name / profile keys {4} / {5}", spec.Code, spec.Name, spec.Id, spec.CountryId, name, profile));
            if (this.Textures() == null)
            {
                this.Log.Add("make " + spec.Code + ": FM2 has no UI\\Textures.zip, no logo");
                return;
            }
            using (Image image = Image.FromFile(spec.Logo))
            using (Bitmap whole = new Bitmap(image))
            {
                this.PutLogo(whole, MakeLogos, logoName, "make " + spec.Code, Path.GetFileName(spec.Logo));
            }
        }

        // The picture cut to its visible pixels (alpha above 8); the whole picture when nothing is visible.
        private static Bitmap Trim(Bitmap image)
        {
            int left = image.Width, top = image.Height, right = -1, bottom = -1;
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    if (image.GetPixel(x, y).A > 8)
                    {
                        left = Math.Min(left, x);
                        right = Math.Max(right, x);
                        top = Math.Min(top, y);
                        bottom = Math.Max(bottom, y);
                    }
                }
            }
            if (right < 0)
            {
                return new Bitmap(image);
            }
            return image.Clone(new Rectangle(left, top, right - left + 1, bottom - top + 1), PixelFormat.Format32bppArgb);
        }

        // ---- after the database check ----

        // Writes the changed string and texture zips (backed up first) and their zipmanifest.xml lines.
        public void Commit()
        {
            Dictionary<string, ForzaZip> changed = new Dictionary<string, ForzaZip>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, Dictionary<string, StrFile>> table in this.strFiles)
            {
                foreach (KeyValuePair<string, StrFile> language in table.Value)
                {
                    ForzaZip zip = this.stringZips[language.Key];
                    zip.Replace(zip.Find(table.Key), language.Value.ToBytes());
                    changed[Path.Combine(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "StringTables"), language.Key + ".zip")] = zip;
                }
            }
            if (this.texturesChanged)
            {
                changed[Path.Combine(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "UI"), "Textures.zip")] = this.textures;
            }
            if (changed.Count == 0)
            {
                return;
            }
            Dictionary<string, byte[]> bytes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, ForzaZip> file in changed)
            {
                bytes[file.Key] = ArchiveBytes(file.Value);
            }
            string manifest = ZipManifest.PathFor(this.TargetRoot);
            int updated = 0;
            foreach (KeyValuePair<string, byte[]> file in bytes)
            {
                if (this.BackupFolder != null && File.Exists(file.Key))
                {
                    string copy = Path.Combine(Path.Combine(this.BackupFolder, "lookups"), Path.GetFileName(Path.GetDirectoryName(file.Key)) + "_" + Path.GetFileName(file.Key));
                    Directory.CreateDirectory(Path.GetDirectoryName(copy));
                    if (!File.Exists(copy))
                    {
                        File.Copy(file.Key, copy);
                    }
                }
                File.WriteAllBytes(file.Key, file.Value);
                if (File.Exists(manifest) && ZipManifest.Update(manifest, ZipManifest.GamePath(this.TargetRoot, file.Key), file.Key))
                {
                    updated++;
                }
            }
            this.Log.Add(string.Format(CultureInfo.InvariantCulture, "lookups: {0} zip(s) written, {1} zipmanifest.xml line(s) updated", bytes.Count, updated));
            this.strFiles.Clear();
            this.stringZips.Clear();
            this.textures = null;
            this.texturesChanged = false;
        }

        // ---- helpers ----

        private static long Count(SqliteDatabase db, string sql, object arg)
        {
            return Convert.ToInt64(db.Scalar(sql, arg), CultureInfo.InvariantCulture);
        }

        private static uint High(SqliteDatabase db, string sql)
        {
            object other = db.Scalar(sql);
            uint value;
            return (other is string && uint.TryParse(((string) other).Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out value)) ? value & 0xFFFF0000u : 0;
        }

        private static string Key(uint high, int key)
        {
            return "_&" + (high | (uint) key).ToString(CultureInfo.InvariantCulture);
        }

        private static bool Used(Dictionary<string, StrFile> files, int key)
        {
            foreach (StrFile file in files.Values)
            {
                if (file.Contains(key))
                {
                    return true;
                }
            }
            return false;
        }

        private static byte[] ArchiveBytes(ForzaZip archive)
        {
            string temp = Path.Combine(Path.GetTempPath(), "ForzaStudio_fm2lookup_" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                archive.Save(temp);
                return File.ReadAllBytes(temp);
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
        }
    }
}
