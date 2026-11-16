using System;
using System.Collections.Generic;
using System.Globalization;

namespace ForzaStudio
{
    // Upgrade part makers (ST-03, user 2026-11-15: FM3 Cayenne front bumper "Edo Competition" without logo in FM4).
    // Upgrade rows have ManufacturerID -> List_PartManufacturer (Id, PartManufacturer "_&<string>", LogoPath "decalNNN");
    // the game shows UI\Textures.zip UI4/Logos/Car_Parts_Logos[_SM]/<LogoPath>.xds. FM3 and FM4 share the rows, ids
    // and names, but FM4 dropped 216 of FM3's 437 logos (EDO decal122 among them).
    // Logo format (all 437 FM3 / 221 FM4 files, 2026-11-15): A8R8G8B8, 1 level. FM3 linear (fetch 02, format 0x86),
    // FM4 gamma (FE, 0xB2). Big: FM3 220x184, FM4 220x123; small: 132x68 in both (68 FM4 ones are 132x80).
    // Same-size logos converted with CarPort.ConvertMotorsport3Texture match FM4's: header equal, mean error 1.9 (143/143).
    public static class PartLogos
    {
        public static readonly string[] Folders = { "Car_Parts_Logos", "Car_Parts_Logos_SM" };

        public static string EntryName(string folder, string logo)
        {
            return "UI4/Logos/" + folder + "/" + logo + ".xds";
        }

        // Part makers used by a car's upgrade rows: tables with a ManufacturerID column, rows found through the car's
        // own keys (Ordinal, its car body, engines, drivetrains, motors).
        public static List<long> CarManufacturers(SqliteDatabase db, long carId)
        {
            string car = carId.ToString(CultureInfo.InvariantCulture);
            Dictionary<string, string> through = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            through["CarBodyID"] = "SELECT CarBodyID FROM List_UpgradeCarBody WHERE Ordinal = " + car;
            through["EngineID"] = "SELECT EngineID FROM List_UpgradeEngine WHERE Ordinal = " + car;
            through["DrivetrainID"] = "SELECT DrivetrainID FROM List_UpgradeDrivetrain WHERE Ordinal = " + car;
            through["MotorID"] = "SELECT MotorID FROM List_UpgradeMotor WHERE Ordinal = " + car;
            List<long> ids = new List<long>();
            foreach (object[] row in db.Query("SELECT name FROM sqlite_master WHERE type = 'table' AND name LIKE 'List_Upgrade%'").Rows)
            {
                string table = (string) row[0];
                if (table.Equals("List_UpgradeDrivetrain", StringComparison.OrdinalIgnoreCase))
                {
                    continue;   // its ManufacturerId is the car make (List_CarMake, GameDbSchema), not a part maker
                }
                List<string> columns = new List<string>();
                foreach (object[] column in db.Query("PRAGMA table_info(" + SqliteDatabase.Quote(table) + ")").Rows)
                {
                    columns.Add((string) column[1]);
                }
                string maker = columns.Find(delegate(string c) { return c.Equals("ManufacturerID", StringComparison.OrdinalIgnoreCase); });
                if (maker == null)
                {
                    continue;
                }
                string where = null;
                if (columns.Exists(delegate(string c) { return c.Equals("Ordinal", StringComparison.OrdinalIgnoreCase); }))
                {
                    where = "Ordinal = " + car;
                }
                else
                {
                    foreach (KeyValuePair<string, string> pair in through)
                    {
                        string key = columns.Find(delegate(string c) { return c.Equals(pair.Key, StringComparison.OrdinalIgnoreCase); });
                        if (key != null)
                        {
                            where = SqliteDatabase.Quote(key) + " IN (" + pair.Value + ")";
                            break;
                        }
                    }
                }
                if (where == null)
                {
                    continue;
                }
                foreach (object[] r in db.Query("SELECT DISTINCT " + SqliteDatabase.Quote(maker) + " FROM " + SqliteDatabase.Quote(table) + " WHERE " + where).Rows)
                {
                    if (r[0] == null || r[0] is DBNull)
                    {
                        continue;
                    }
                    long id = Convert.ToInt64(r[0], CultureInfo.InvariantCulture);
                    if (id > 0 && !ids.Contains(id))
                    {
                        ids.Add(id);
                    }
                }
            }
            ids.Sort();
            return ids;
        }

        // An FM3 logo in FM4 form, at the size FM4 uses in that folder (width x height of 'reference'): gamma like
        // CarPort.ConvertMotorsport3Texture; when the size differs, the drawn part (pixels that differ from the corner
        // colour) is kept at its size and centred, or scaled down evenly when it does not fit (never stretched).
        public static byte[] Fm3ToFm4(byte[] fm3, string entryName, int width, int height, out string note)
        {
            byte[] converted = CarPort.ConvertMotorsport3Texture(fm3, entryName);
            XdsTexture texture = new XdsTexture(converted);
            if (texture.Codec != DxtFormat.A8R8G8B8)
            {
                note = "kept as converted (" + texture.Codec + ")";
                return converted;
            }
            if (texture.Width == width && texture.Height == height)
            {
                if ((converted[0x23] & 0x3F) == 0x06)
                {
                    converted[0x23] = (byte) ((converted[0x23] & 0xC0) | 0x32);   // 8_8_8_8 -> 8_8_8_8_AS_16_16_16_16 (FM4's 0xB2)
                }
                note = string.Format(CultureInfo.InvariantCulture, "{0}x{1}", width, height);
                return converted;
            }
            int w0 = texture.Width;
            int h0 = texture.Height;
            byte[] pixels = texture.ReadLevelBlocks(0);
            // Drawn area: pixels unlike the top-left corner (the background).
            int minX = w0, minY = h0, maxX = -1, maxY = -1;
            for (int y = 0; y < h0; y++)
            {
                for (int x = 0; x < w0; x++)
                {
                    int i = ((y * w0) + x) * 4;
                    int d = Math.Abs(pixels[i] - pixels[0]) + Math.Abs(pixels[i + 1] - pixels[1]) + Math.Abs(pixels[i + 2] - pixels[2]) + Math.Abs(pixels[i + 3] - pixels[3]);
                    if (d > 24)
                    {
                        minX = Math.Min(minX, x);
                        maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y);
                        maxY = Math.Max(maxY, y);
                    }
                }
            }
            if (maxX < 0)
            {
                minX = 0;
                minY = 0;
                maxX = w0 - 1;
                maxY = h0 - 1;
            }
            double cw = maxX - minX + 1;
            double ch = maxY - minY + 1;
            double margin = 0.9;   // leave a border like the game's logos
            double scale = Math.Min(1.0, Math.Min((width * margin) / cw, (height * margin) / ch));
            double cx0 = (minX + maxX + 1) / 2.0;
            double cy0 = (minY + maxY + 1) / 2.0;
            byte[] result = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double sx = (((x + 0.5) - (width / 2.0)) / scale) + cx0 - 0.5;
                    double sy = (((y + 0.5) - (height / 2.0)) / scale) + cy0 - 0.5;
                    int xa = (int) Math.Floor(sx);
                    int ya = (int) Math.Floor(sy);
                    double fx = sx - xa;
                    double fy = sy - ya;
                    for (int c = 0; c < 4; c++)
                    {
                        double v = 0;
                        for (int j = 0; j < 2; j++)
                        {
                            for (int i = 0; i < 2; i++)
                            {
                                int xx = xa + i;
                                int yy = ya + j;
                                double weight = ((i == 0) ? 1 - fx : fx) * ((j == 0) ? 1 - fy : fy);
                                v += weight * ((xx < 0 || yy < 0 || xx >= w0 || yy >= h0) ? pixels[c] : pixels[(((yy * w0) + xx) * 4) + c]);
                            }
                        }
                        result[(((y * width) + x) * 4) + c] = (byte) Math.Round(v);
                    }
                }
            }
            XdsTexture made = XdsTexture.Create(width, height, DxtFormat.A8R8G8B8, true, 1);
            made.WriteLevelBlocks(0, result);
            note = string.Format(CultureInfo.InvariantCulture, "{0}x{1} -> {2}x{3}{4}", w0, h0, width, height, (scale < 1.0) ? string.Format(CultureInfo.InvariantCulture, " (scaled {0:0.00})", scale) : " (centred, same size)");
            return made.ToBytes();
        }
    }
}
