namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    // FM2's StockGarage (found 2026-11-14, ST-02): a table of the database with one row per car the game shows (Id, names,
    // thumbnail, make, class, ratings, stock upgrade ids...). FM2 lists its cars from it: a car with every Data_Car row but no
    // StockGarage row is not in the game (TOY_SupraRZ_98_ST02). The official DLC fill it from their LicenseMasks\*.sql with
    // the query below (March Car Pack, 00000002.sql), copied as it is; on the base game it rebuilds 303 of 303 rows
    // identical. The 7 base cars without a row (Camaro 35th, 360 Challenge...) are unlock or special cars: a copy gets a row
    // only when its source has one.
    public static class Fm2StockGarage
    {
        public const string Table = "StockGarage";

        private const string SelectBody =
            "SELECT Data_Car.Id AS Id, Data_Car.DisplayName AS DisplayName, Data_Car.Thumbnail AS Thumbnail, Data_Car.MediaName AS MediaName, " +
            "Data_Car.Year AS Year, Data_Car.MakeID AS MakeID, Data_Car.EnginePlacementID AS EnginePlacementID_Alias, Data_Car.ClassID AS ClassID, " +
            "Data_Car.PerformanceIndex AS PerformanceIndex, Powertrains.DrivetrainId AS DrivetrainID_Alias, List_Country.RegionID AS RegionID, " +
            "List_Country.CountryID AS CountryID, Data_Car.Id AS ModelID, Data_Car.CurbWeight AS Weight, Data_Car.ModelShort AS ModelName, " +
            "List_CarMake.DisplayName AS MakeName, List_CarMake.IconPath AS MakeIcon, List_CarMake.IconPathLight AS MakeIconLight, " +
            "Powertrains.IconPath AS PowertrainIcon, Data_Car.SimPeakPower AS Power, Data_Car.IsDLC AS IsDLC, Data_Car.IsUnicorn AS IsUnicorn, " +
            "Data_Car.FamilyModelID AS FamilyModelID, Data_Car.FamilyBodyID AS FamilyBodyID, Data_Car.CarTypeID AS CarTypeID, " +
            "Data_Engine.CylinderID AS CylinderID, List_UpgradeBrakes.Id AS Brakes, List_UpgradeSpringDamper.Id AS SpringDamper, " +
            "List_UpgradeFrontBumper.Id AS FrontBumper, List_UpgradeRearWing.Id AS RearWing, List_UpgradeRearBumper.Id AS RearBumper, " +
            "List_UpgradeSideSkirt.Id AS SideSkirt, List_UpgradeHood.Id AS Hood, List_UpgradeEngineExhaust.Id AS Exhaust, " +
            "Data_Car.SimPeakAngVel AS SimPeakAngVel, Data_Car.SimPeakTorqueAngVel AS SimPeakTorqueAngVel, Data_Car.SimRedlineAngVel AS SimRedlineAngVel, " +
            "Data_Engine.AspirationID_Stock AS AspirationID_Stock, Data_Car.SpeedRating AS SpeedRating, Data_Car.AccelerationRating AS AccelerationRating, " +
            "Data_Car.BrakingRating AS BrakingRating, Data_Car.HandlingRating AS HandlingRating, Data_Car.BaseRarity AS BaseRarity, " +
            "Data_Car.SimPeakPower AS SimPeakPower, Data_Car.SimPeakTorque AS SimPeakTorque, Data_Car.CurbWeight AS CurbWeight, " +
            "Data_Car.[FrontWeight-pct] AS WeightDistribution, List_Country.Country AS Origin, BaseCost AS BaseCost, Data_Car.StockGears AS StockGears, " +
            "[StockTire-FrontWidth-mm] AS [StockTire-FrontWidth-mm], [StockTire-FrontAspect-pct] AS [StockTire-FrontAspect-pct], " +
            "[StockTire-FrontWheel-in] AS [StockTire-FrontWheel-in], [StockTire-RearWidth-mm] AS [StockTire-RearWidth-mm], " +
            "[StockTire-RearAspect-pct] AS [StockTire-RearAspect-pct], [StockTire-RearWheel-in] AS [StockTire-RearWheel-in] " +
            "FROM Data_Car " +
            "LEFT JOIN List_UpgradeEngine ON List_UpgradeEngine.Ordinal=Data_Car.Id " +
            "LEFT JOIN Data_Engine ON List_UpgradeEngine.EngineID=Data_Engine.EngineID " +
            "LEFT JOIN Powertrains ON Data_Engine.DriveTypeID=Powertrains.DrivetrainId AND Data_Car.EnginePlacementID=Powertrains.EnginePlacementID " +
            "LEFT JOIN List_CarMake ON Data_Car.MakeID=List_CarMake.Id " +
            "LEFT JOIN List_Country ON List_CarMake.CountryID=List_Country.CountryID " +
            "LEFT JOIN List_Region ON List_Region.Id=List_Country.RegionID " +
            "LEFT JOIN List_UpgradeBrakes ON Data_Car.Id=List_UpgradeBrakes.Ordinal AND List_UpgradeBrakes.IsStock=1 " +
            "LEFT JOIN List_UpgradeSpringDamper ON Data_Car.Id=List_UpgradeSpringDamper.Ordinal AND List_UpgradeSpringDamper.IsStock=1 " +
            "LEFT JOIN List_UpgradeFrontBumper ON Data_Car.Id=List_UpgradeFrontBumper.Ordinal AND List_UpgradeFrontBumper.IsStock=1 " +
            "LEFT JOIN List_UpgradeRearWing ON Data_Car.Id=List_UpgradeRearWing.Ordinal AND List_UpgradeRearWing.IsStock=1 " +
            "LEFT JOIN List_UpgradeRearBumper ON Data_Car.Id=List_UpgradeRearBumper.Ordinal AND List_UpgradeRearBumper.IsStock=1 " +
            "LEFT JOIN List_UpgradeSideSkirt ON Data_Car.Id=List_UpgradeSideSkirt.Ordinal AND List_UpgradeSideSkirt.IsStock=1 " +
            "LEFT JOIN List_UpgradeHood ON Data_Car.Id=List_UpgradeHood.Ordinal AND List_UpgradeHood.IsStock=1 " +
            "LEFT JOIN List_UpgradeEngineExhaust ON Data_Engine.EngineID=List_UpgradeEngineExhaust.EngineId AND List_UpgradeEngineExhaust.IsStock=1 " +
            "";

        private const string Select = SelectBody + "WHERE List_UpgradeEngine.IsStock=1 AND Data_Car.Id = ?";

        // The statement of an FM2 pack's LicenseMasks\00000001.sql for its cars, as the official packs write it: the bought cars
        // that are not listed yet go into StockGarage.
        public static string LicenseInsert(List<long> cars)
        {
            List<string> ids = cars.ConvertAll<string>(delegate(long c) { return "Data_Car.Id=" + c.ToString(CultureInfo.InvariantCulture); });
            List<string> own = cars.ConvertAll<string>(delegate(long c) { return "Id=" + c.ToString(CultureInfo.InvariantCulture); });
            return "INSERT INTO " + Table + " \r\n" + SelectBody + "\r\nWHERE\tList_UpgradeEngine.IsStock=1 AND \r\n\t\tData_Car.IsPurchased=1 AND \r\n\t\t(" + string.Join(" OR ", ids.ToArray())
                + ")\r\nEXCEPT\r\nSELECT * FROM " + Table + " WHERE (" + string.Join(" OR ", own.ToArray()) + ");\r\n";
        }

        // The licence mask lines that open each pack car in Arcade (ArcadeCarList Level 0), once. The merge cannot carry
        // ArcadeCarList (FM2 hung on loading with it, DLC8 Mustang 2026-11-14), so the mask adds it when the pack is read. User
        // rule: FM2 added cars ALWAYS in Arcade. Hand test on DLC9 (BMW 1M 679, Add To Package, was missing from Arcade):
        // the line made it show, no hang (in game 2026-11-28).
        public static string ArcadeInsert(List<long> cars)
        {
            StringBuilder sql = new StringBuilder();
            foreach (long car in cars)
            {
                string id = car.ToString(CultureInfo.InvariantCulture);
                sql.Append("INSERT INTO ArcadeCarList (CarId, Level) SELECT " + id + ", 0 WHERE NOT EXISTS (SELECT 1 FROM ArcadeCarList WHERE CarId=" + id + ");\r\n");
            }
            return sql.ToString();
        }

        // FM2's Arcade lists the cars of NewProfile_Arcade_Garage (235 base cars) plus the ones Rewards_Arcade unlocks; the
        // official DLC never add theirs. User rule 2026-11-14: a car the editor duplicates or adds to FM2 is ALWAYS enabled in
        // Arcade, whatever its source.
        public const string ArcadeTable = "NewProfile_Arcade_Garage";

        // The source's dealer rows (Rewards_DriverLevel TypeId 3: RegionID, UnlockLevel) for the copy; returns the rows added.
        public static int CopyDealerLevels(SqliteDatabase db, long source, long car)
        {
            if (!Has(db, "Rewards_DriverLevel"))
            {
                return 0;
            }
            db.Execute("DELETE FROM Rewards_DriverLevel WHERE CarId = ? AND TypeId = 3", car);
            return db.Execute("INSERT INTO Rewards_DriverLevel (CarId, RegionID, UnlockLevel, TypeId) SELECT ?, RegionID, UnlockLevel, TypeId FROM Rewards_DriverLevel WHERE CarId = ? AND TypeId = 3", car, source);
        }

        // A copy whose source has no dealer rows (a disc 2 / DLC car: their packs carry none) gets one per region at driver level
        // 0, like most base cars (user 2026-11-15: a car in the game folder must not stay locked). Returns the rows added.
        public static int DefaultDealerLevels(SqliteDatabase db, long car)
        {
            if (!Has(db, "Rewards_DriverLevel") || Convert.ToInt64(db.Scalar("SELECT count(*) FROM Rewards_DriverLevel WHERE CarId = ? AND TypeId = 3", car), CultureInfo.InvariantCulture) > 0)
            {
                return 0;
            }
            return db.Execute("INSERT INTO Rewards_DriverLevel (CarId, RegionID, UnlockLevel, TypeId) SELECT DISTINCT ?, RegionID, 0, 3 FROM Rewards_DriverLevel WHERE TypeId = 3 ORDER BY RegionID", car);
        }

        // The car's ArcadeCarList row (Level 0 = open, 99 = locked) when it has none (a disc 2 / DLC source). Returns true when added.
        public static bool EnsureArcadeList(SqliteDatabase db, long car)
        {
            if (!Has(db, "ArcadeCarList") || Convert.ToInt64(db.Scalar("SELECT count(*) FROM ArcadeCarList WHERE CarId = ?", car), CultureInfo.InvariantCulture) > 0)
            {
                return false;
            }
            db.Execute("INSERT INTO ArcadeCarList (CarId, Level) VALUES (?, 0)", car);
            return true;
        }

        // Adds the car to the Arcade garage (once). Returns true when a row was added.
        public static bool EnableArcade(SqliteDatabase db, long car)
        {
            if (!Has(db, ArcadeTable) || Convert.ToInt64(db.Scalar("SELECT count(*) FROM " + ArcadeTable + " WHERE CarId = ?", car), CultureInfo.InvariantCulture) > 0)
            {
                return false;
            }
            db.Execute("INSERT INTO " + ArcadeTable + " (CarId) VALUES (?)", car);
            return true;
        }

        public static bool Has(SqliteDatabase db)
        {
            return Has(db, Table);
        }

        private static bool Has(SqliteDatabase db, string table)
        {
            return Convert.ToInt64(db.Scalar("SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = ?", table), CultureInfo.InvariantCulture) > 0;
        }

        public static bool HasRow(SqliteDatabase db, long car)
        {
            return Has(db) && Convert.ToInt64(db.Scalar("SELECT count(*) FROM " + Table + " WHERE Id = ?", car), CultureInfo.InvariantCulture) > 0;
        }

        // The car's row as the official DLC build it (replacing one it has). Returns the rows written (0 when the query finds no
        // stock engine row for the car).
        public static int Write(SqliteDatabase db, long car)
        {
            db.Execute("DELETE FROM " + Table + " WHERE Id = ?", car);
            int rows = db.Query(Select, car).Rows.Count;
            if (rows != 1)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "StockGarage: the official query finds {0} rows for car {1} (one stock List_UpgradeEngine row expected).", rows, car));
            }
            db.Execute("INSERT INTO " + Table + " " + Select, car);
            return rows;
        }
    }
}
