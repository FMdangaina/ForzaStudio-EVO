namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;

    // Every column of the FM4 gamedb.slt that holds the id of a row in another table. The database
    // declares no foreign keys: these were found by value containment on the original FM4 database
    // and checked one by one (FH2 has all of FM4's tables, so they hold there too).
    // Strict relations have no dangling value in the original game; loose ones already point at
    // missing rows there (cut cars and tracks), so the game tolerates them.
    public static class GameDbSchema
    {
        public sealed class Relation
        {
            public readonly string Table;
            public readonly string Column;   // "A,B" for a key of two columns
            public readonly string Target;
            public readonly string TargetColumn;
            public readonly bool Strict;
            public readonly string Note;

            public Relation(string table, string column, string target, string targetColumn, bool strict, string note)
            {
                this.Table = table;
                this.Column = column;
                this.Target = target;
                this.TargetColumn = targetColumn;
                this.Strict = strict;
                this.Note = note;
            }

            public bool IsComposite
            {
                get { return this.Column.IndexOf(',') >= 0; }
            }

            public string[] Columns
            {
                get { return this.Column.Split(','); }
            }

            public string[] TargetColumns
            {
                get { return this.TargetColumn.Split(','); }
            }

            public override string ToString()
            {
                return string.Format("{0}.{1} -> {2}.{3}", this.Table, this.Column, this.Target, this.TargetColumn);
            }
        }

        public static readonly List<Relation> Relations = new List<Relation>();

        // Values that mean "none" in id columns.
        public static bool IsNone(long value)
        {
            return value == 0 || value == -1;
        }

        public static Relation Find(string table, string column)
        {
            foreach (Relation relation in Relations)
            {
                if (string.Equals(relation.Table, table, StringComparison.OrdinalIgnoreCase) && string.Equals(relation.Column, column, StringComparison.OrdinalIgnoreCase))
                {
                    return relation;
                }
            }
            return null;
        }

        // Relations whose target is this table.
        public static List<Relation> Into(string table)
        {
            return Relations.FindAll(delegate(Relation r) { return string.Equals(r.Target, table, StringComparison.OrdinalIgnoreCase); });
        }

        // Relations from this table.
        public static List<Relation> From(string table)
        {
            return Relations.FindAll(delegate(Relation r) { return string.Equals(r.Table, table, StringComparison.OrdinalIgnoreCase); });
        }

        // ---- per game (ST-02, 2026-11-14) ----
        // FM4, FH1 and FH2 use the map above (measured on FM4; FH2 has all of FM4's tables). FM2 and FM3 have their own,
        // measured the same way on their stock databases (fm4dev\ST02\relmap.exe, reviewed): docs\<FM2|FM3>\DbRelations.tsv
        // (FM2 163 relations, FM3 219), and the keys of their tables that declare none, docs\<FM2|FM3>\DbKeys.tsv (FM2: the
        // counters of the physics, aero, torque curve, price and engine tables; FM3: 4 tables).

        private static readonly Dictionary<ForzaGame, List<Relation>> gameRelations = new Dictionary<ForzaGame, List<Relation>>();
        private static readonly Dictionary<ForzaGame, Dictionary<string, string>> gameKeys = new Dictionary<ForzaGame, Dictionary<string, string>>();

        private static string Tag(ForzaGame game)
        {
            return (game == ForzaGame.Motorsport2) ? "FM2" : (game == ForzaGame.Motorsport3) ? "FM3" : null;
        }

        private static List<string[]> ReadResource(string name)
        {
            List<string[]> rows = new List<string[]>();
            using (System.IO.Stream stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("The program has no " + name + " (database map of the game).");
                }
                using (System.IO.StreamReader reader = new System.IO.StreamReader(stream))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length > 0 && !line.StartsWith("#"))
                        {
                            rows.Add(line.Split('\t'));
                        }
                    }
                }
            }
            return rows;
        }

        // The relations of a game's database.
        public static List<Relation> For(ForzaGame game)
        {
            string tag = Tag(game);
            if (tag == null)
            {
                return Relations;
            }
            lock (gameRelations)
            {
                List<Relation> list;
                if (!gameRelations.TryGetValue(game, out list))
                {
                    list = new List<Relation>();
                    foreach (string[] row in ReadResource("DbRelations_" + tag + ".tsv"))
                    {
                        if (row.Length >= 5)
                        {
                            list.Add(new Relation(row[0], row[1], row[2], row[3], row[4] == "1", (row.Length > 5) ? row[5] : null));
                        }
                    }
                    gameRelations[game] = list;
                }
                return list;
            }
        }

        public static List<Relation> Into(ForzaGame game, string table)
        {
            return For(game).FindAll(delegate(Relation r) { return string.Equals(r.Target, table, StringComparison.OrdinalIgnoreCase); });
        }

        public static List<Relation> From(ForzaGame game, string table)
        {
            return For(game).FindAll(delegate(Relation r) { return string.Equals(r.Table, table, StringComparison.OrdinalIgnoreCase); });
        }

        // The key column of a table that declares none, for FM2 / FM3 (DbKeys); null for the other games or tables.
        public static string LogicalKey(ForzaGame game, string table)
        {
            string tag = Tag(game);
            if (tag == null)
            {
                return null;
            }
            lock (gameKeys)
            {
                Dictionary<string, string> keys;
                if (!gameKeys.TryGetValue(game, out keys))
                {
                    keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string[] row in ReadResource("DbKeys_" + tag + ".tsv"))
                    {
                        if (row.Length >= 2)
                        {
                            keys[row[0]] = row[1];
                        }
                    }
                    gameKeys[game] = keys;
                }
                string key;
                return keys.TryGetValue(table, out key) ? key : null;
            }
        }

        private static void R(string table, string column, string target, string targetColumn)
        {
            Relations.Add(new Relation(table, column, target, targetColumn, true, null));
        }

        // A relation that already dangles in the original game.
        private static void L(string table, string column, string target, string targetColumn, string note)
        {
            Relations.Add(new Relation(table, column, target, targetColumn, false, note));
        }

        static GameDbSchema()
        {
            // Cars: in the other tables a car's Id is called Ordinal (or CarId).
            foreach (string table in new string[] {
                "CarPartPositions", "Combo_Colors", "Combo_Colors_Exceptions", "List_AntiSwayPhysics", "List_SpringDamperPhysics",
                "List_UpgradeAntiSwayFront", "List_UpgradeAntiSwayRear", "List_UpgradeBrakes", "List_UpgradeCarBody", "List_UpgradeDrivetrain",
                "List_UpgradeEngine", "List_UpgradeMotor", "List_UpgradeRearWing", "List_UpgradeSpringDamper", "List_UpgradeTireCompound",
                "EventParticipants", "KinectSplitscreenCars", "CarInvalidDefaultParts" })
            {
                R(table, "Ordinal", "Data_Car", "Id");
            }
            L("List_UpgradeRimSizeFront", "Ordinal", "Data_Car", "Id", "rows of 2 cut cars");
            L("List_UpgradeRimSizeRear", "Ordinal", "Data_Car", "Id", "rows of 2 cut cars");
            L("StripeColors", "Ordinal", "Data_Car", "Id", "rows of cut cars");
            L("Combo_Engines", "Ordinal", "Data_Car", "Id", "old table, not used: List_UpgradeEngine holds the engines");
            R("CameraOverrides", "CarId", "Data_Car", "Id");
            R("CarExceptions", "CarID", "Data_Car", "Id");
            R("CarListFilters", "CarID", "Data_Car", "Id");
            R("CarTrackOffsets", "Id", "Data_Car", "Id");
            L("Demo_CarList", "Id", "Data_Car", "Id", "4 cut cars");
            L("HasCarExperience", "CarId", "Data_Car", "Id", "one cut car");
            L("TireMod_FrictionScale", "Ordinal", "Data_Car", "Id", "one cut car");
            R("IECars", "CarId", "Data_Car", "Id");
            L("ArcadeCarList", "CarId", "Data_Car", "Id", "rows of cut cars");
            L("EventShowroomChallenges", "CarID", "Data_Car", "Id", null);
            R("Rewards_DriverLevelv3", "RewardCarId", "Data_Car", "Id");
            R("Rewards_ShowroomUnlocks", "RewardCarId", "Data_Car", "Id");
            R("ContentOffersMapping", "ContentId", "Data_Car", "Id");
            L("NewProfile_Career_Garage", "CarId", "Data_Car", "Id", "profile table, empty on the disc");
            L("NewProfile_CarExperienceUnlocks", "CarID", "Data_Car", "Id", "profile table");
            L("NewProfile_CarExperienceRevealsSeen", "CarID", "Data_Car", "Id", "profile table");
            L("NewProfile_CarPointsOfInterestProgress", "CarOrdinal", "Data_Car", "Id", "profile table");

            // Data_Car's own lists.
            R("Data_Car", "MakeID", "List_CarMake", "ID");
            R("Data_Car", "ClassID", "CarClasses", "Id");
            R("Data_Car", "CarTypeID", "List_CarType", "Id");
            R("Data_Car", "FamilyModelID", "List_FamilyModel", "Id");
            R("Data_Car", "FamilyBodyID", "List_FamilyBody", "Id");
            R("Data_Car", "FamilySpecialID", "List_FamilySpecial", "FamilySpecialID");
            R("Data_Car", "EnginePlacementID", "List_EnginePlacement", "ID");
            R("Data_Car", "MaterialTypeID", "List_MaterialType", "MaterialTypeID");
            R("Data_Car", "PowertrainID", "Powertrains", "Id");
            R("Data_Car", "DriveTypeID", "List_DriveType", "ID");
            R("Data_Car", "EngineConfigID", "List_EngineConfig", "ConfigID");
            R("Data_Car", "CylinderID", "List_Cylinders", "CylinderID");
            R("Data_Car", "AspirationTypeId", "List_Aspiration", "AspirationID");
            R("Data_Car", "AutoSteerOverrideID", "AutoSteerOverrides", "Id");
            R("Data_Car", "StockWheelID", "List_Wheels", "ID");
            R("Data_Car", "RegionID", "List_Region", "Id");
            R("Data_Car", "CountryID", "List_Country", "CountryID");
            L("Data_Car", "AIPlayerID", "AIPlayers", "Id", "0 for every car");

            // Engines, motors, drivetrains, bodies.
            R("Data_Engine", "ConfigID", "List_EngineConfig", "ConfigID");
            R("Data_Engine", "CylinderID", "List_Cylinders", "CylinderID");
            R("Data_Engine", "VariableTimingID", "List_VariableTiming", "VariableTimingID");
            R("Data_Engine", "AspirationID_Stock", "List_Aspiration", "AspirationID");
            R("Data_Motor", "ConfigID", "List_EngineConfig", "ConfigID");
            R("Data_Motor", "TorqueCurveFullThrottleID", "List_TorqueCurve", "TorqueCurveID");
            R("Data_Drivetrain", "DrivetypeID", "List_DriveType", "ID");
            R("Powertrains", "DrivetrainId", "List_DriveType", "ID");
            L("Powertrains", "EnginePlacementId", "List_EnginePlacement", "ID", "row 7 uses 4");
            R("List_UpgradeEngine", "EngineID", "Data_Engine", "EngineID");
            L("List_UpgradeEngine", "ManufacturerID", "List_CarMake", "ID", "the car's make; one swap uses 417");
            R("List_UpgradeDrivetrain", "DrivetrainID", "Data_Drivetrain", "DrivetrainID");
            R("List_UpgradeDrivetrain", "PowertrainId", "Powertrains", "Id");
            R("List_UpgradeDrivetrain", "ManufacturerId", "List_CarMake", "ID");
            R("List_UpgradeCarBody", "CarBodyID", "Data_CarBody", "Id");
            R("List_UpgradeMotor", "MotorID", "Data_Motor", "MotorID");
            R("List_UpgradeMotorParts", "MotorID", "Data_Motor", "MotorID");
            foreach (string part in new string[] { "CSC", "Camshaft", "DSC", "Displacement", "Exhaust", "Flywheel", "FuelSystem", "Ignition", "Intake",
                "Intercooler", "Manifold", "OilCooling", "PistonsCompression", "RestrictorPlate", "TurboQuad", "TurboSingle", "TurboTwin", "Valves" })
            {
                R("List_UpgradeEngine" + part, "EngineID", "Data_Engine", "EngineID");
            }
            foreach (string part in new string[] { "Clutch", "Differential", "Driveline", "Transmission" })
            {
                R("List_UpgradeDrivetrain" + part, "DrivetrainID", "Data_Drivetrain", "DrivetrainID");
            }
            foreach (string part in new string[] { "ChassisStiffness", "FrontBumper", "Hood", "RearBumper", "SideSkirt", "Weight" })
            {
                R("List_UpgradeCarBody" + part, "CarBodyID", "Data_CarBody", "Id");
            }
            L("List_UpgradeCarBodyTireWidthFront", "CarBodyId", "Data_CarBody", "Id", "rows of 11 cut cars");
            L("List_UpgradeCarBodyTireWidthRear", "CarBodyId", "Data_CarBody", "Id", "rows of 11 cut cars");

            // Part makers (shown in the upgrade shop) and the part names that are also the model's section names.
            foreach (string table in new string[] {
                "List_UpgradeAntiSwayFront", "List_UpgradeAntiSwayRear", "List_UpgradeBrakes", "List_UpgradeCarBody", "List_UpgradeCarBodyChassisStiffness",
                "List_UpgradeCarBodyFrontBumper", "List_UpgradeCarBodyHood", "List_UpgradeCarBodyRearBumper", "List_UpgradeCarBodySideSkirt",
                "List_UpgradeCarBodyWeight", "List_UpgradeDrivetrainClutch", "List_UpgradeDrivetrainDifferential", "List_UpgradeDrivetrainDriveline",
                "List_UpgradeDrivetrainTransmission", "List_UpgradeEngineCSC", "List_UpgradeEngineCamshaft", "List_UpgradeEngineDSC",
                "List_UpgradeEngineDisplacement", "List_UpgradeEngineExhaust", "List_UpgradeEngineFlywheel", "List_UpgradeEngineFuelSystem",
                "List_UpgradeEngineIgnition", "List_UpgradeEngineIntake", "List_UpgradeEngineIntercooler", "List_UpgradeEngineManifold",
                "List_UpgradeEngineOilCooling", "List_UpgradeEnginePistonsCompression", "List_UpgradeEngineRestrictorPlate", "List_UpgradeEngineTurboQuad",
                "List_UpgradeEngineTurboSingle", "List_UpgradeEngineTurboTwin", "List_UpgradeEngineValves", "List_UpgradeMotor", "List_UpgradeMotorParts",
                "List_UpgradeRearWing", "List_UpgradeSpringDamper", "List_UpgradeTireCompound" })
            {
                R(table, "ManufacturerID", "List_PartManufacturer", "Id");
            }
            L("List_PartAttribute", "ManufacturerID", "List_PartManufacturer", "Id", "old table, not used");
            R("List_Wheels", "PartManufacturerID", "List_PartManufacturer", "Id");
            R("Combo_TireBrandCompound", "TireBrandId", "List_PartManufacturer", "Id");
            foreach (string table in new string[] {
                "List_UpgradeCarBodyChassisStiffness", "List_UpgradeCarBodyFrontBumper", "List_UpgradeCarBodyHood", "List_UpgradeCarBodyRearBumper",
                "List_UpgradeCarBodySideSkirt", "List_UpgradeCarBodyWeight", "List_UpgradeEngineCSC", "List_UpgradeEngineDSC", "List_UpgradeEngineExhaust",
                "List_UpgradeEngineFuelSystem", "List_UpgradeEngineIgnition", "List_UpgradeEngineIntake", "List_UpgradeEngineIntercooler", "List_UpgradeRearWing" })
            {
                R(table, "PartsStringID", "List_PartsStrings", "Id");
            }

            // Physics rows of the upgrades (Id = car*1000+n like the upgrade row, rear springs +100).
            R("List_UpgradeAntiSwayFront", "AntiSwayPhysicsID", "List_AntiSwayPhysics", "AntiSwayPhysicsID");
            R("List_UpgradeAntiSwayRear", "AntiSwayPhysicsID", "List_AntiSwayPhysics", "AntiSwayPhysicsID");
            R("List_UpgradeSpringDamper", "FrontSpringDamperPhysicsID", "List_SpringDamperPhysics", "SpringDamperPhysicsID");
            R("List_UpgradeSpringDamper", "RearSpringDamperPhysicsID", "List_SpringDamperPhysics", "SpringDamperPhysicsID");
            R("List_UpgradeSpringDamper", "SuspensionDamageModDefID", "List_DamageModDef", "DamageModDefID");
            R("List_UpgradeSpringDamper", "TiresDamageModDefID", "List_DamageModDef", "DamageModDefID");
            R("List_SpringDamperPhysics", "SuspensionPhysicsTypeID", "List_SuspensionPhysicsType", "SuspensionPhysicsTypeID");
            R("List_UpgradeCarBodyFrontBumper", "AeroPhysicsID", "List_AeroPhysics", "AeroPhysicsID");
            R("List_UpgradeRearWing", "AeroPhysicsID", "List_AeroPhysics", "AeroPhysicsID");
            R("List_UpgradeEngineCamshaft", "TorqueCurveFullThrottleID", "List_TorqueCurve", "TorqueCurveID");
            R("List_UpgradeBrakes", "FrontBrakeTypeID", "List_BrakeType", "BrakeTypeID");
            R("List_UpgradeBrakes", "RearBrakeTypeID", "List_BrakeType", "BrakeTypeID");
            R("List_UpgradeBrakes", "DamageModDefID", "List_DamageModDef", "DamageModDefID");
            R("List_UpgradeTireCompound", "TireCompoundID", "List_TireCompound", "TireCompoundID");
            R("Combo_TireBrandCompound", "TireCompoundId", "List_TireCompound", "TireCompoundID");
            foreach (string curve in new string[] { "FrictionMultiCurveLateralID", "FrictionMultiCurveLongitudinalID" })
            {
                R("List_TireCompound", curve, "List_TireFrictionMultiCurve", "FrictionMultiCurveID");
            }
            foreach (string curve in new string[] { "TempAffectFriction", "TempAffectWear", "PressureAffectFriction", "PressureAffectWear", "StartPressureAffectPeakSASR",
                "CamberAffectLatFriction", "CamberAffectLongFriction", "WearAffectFriction", "SpeedAffectFriction", "NormSlipAngleAffectAlignTorque",
                "NormSlipRatioAffectAlignTorque", "LoadAffectAlignTorque" })
            {
                R("List_TireCompound", "AffectCurve" + curve + "ID", "List_TireAffectCurve", "AffectCurveID");
            }
            R("List_TireFrictionMultiCurve", "TireFrictionCurveID0", "List_TireFrictionCurve", "FrictionCurveID");
            R("List_TireFrictionMultiCurve", "TireFrictionCurveID1", "List_TireFrictionCurve", "FrictionCurveID");

            // Colors and liveries.
            R("Combo_Colors", "ColorClassId", "List_ColorClass", "ColorClassId");
            L("Combo_Colors_Exceptions", "Ordinal,Sequence", "Combo_Colors", "Ordinal,Sequence", "one exception has no color");
            L("StripeColors", "Ordinal,Sequence", "Combo_Colors", "Ordinal,Sequence", "rows of cut cars");
            R("AIPlayerColors", "SpecialColorId", "List_SpecialColors", "ID");
            R("Livery_DecalsSortOrder", "MakeID", "List_CarMake", "ID");
            R("Livery_DecalsSortOrder", "Livery_DecalID", "Livery_Decals", "ID");
            L("Livery_VinylsDecals", "Category", "Livery_Categories", "ID", "category 20 is missing");

            // Makes, countries, regions.
            R("List_CarMake", "CountryID", "List_Country", "CountryID");
            R("List_Country", "RegionID", "List_Region", "Id");
            R("Environments", "CountryId", "List_Country", "CountryID");
            R("AIPlayers", "CountryId", "List_Country", "CountryID");

            // Wheels.
            R("WheelNormOffsets", "Id", "List_Wheels", "ID");
            R("WheelExceptions", "WheelID", "List_Wheels", "ID");

            // Tracks: per-layout data also exists for cut layouts.
            R("Tracks", "EnvironmentId", "Environments", "Id");
            R("Tracks", "DefaultStartGridPositionsId", "StartGridPositions", "id");
            L("Tracks", "FlyingStartPosId", "FlyingStartPositions", "id", "16 tracks point at missing rows");
            L("Tracks", "MPPitProfile", "PittingProfiles", "id", "one track uses 200");
            L("EnvironmentTimeOfDay", "EnvironmentId", "Environments", "Id", null);
            R("Races", "TrackId", "Tracks", "id");
            R("HopperRaces", "TrackId", "Tracks", "id");
            L("KinectSplitscreenTracks", "TrackId", "Tracks", "id", null);
            L("SplitPoints", "TrackId", "Tracks", "id", "cut layouts");
            L("GraffitiPoints", "TrackId", "Tracks", "id", "cut layouts");
            L("TurnSections", "TrackId", "Tracks", "id", "cut layouts");
            L("TrackPittingZones", "Tracks_id", "Tracks", "id", "cut layouts");
            L("TrackStartingMerges", "Tracks_id", "Tracks", "id", "cut layouts");
            L("TrackSpecificAI", "Tracks_id", "Tracks", "id", null);
            L("TrackSpecificAI", "AIDiff_id", "AIDiff", "id", "uses 3");
            L("NewProfile_Career_TrackUsage", "TrackId", "Tracks", "id", "profile table");

            // Events, races and AI.
            R("Races", "EventId", "Events", "Id");
            R("RacesSpecial", "RaceId", "Races", "Id");
            R("EventParticipants", "EventID", "Events", "Id");
            R("EventRestrictions", "EventID", "Events", "Id");
            L("EventShowroomChallenges", "EventID", "Events", "Id", null);
            R("Events", "CareerTypeId", "CareerEventTypes", "id");
            R("Events", "PittingProfileID", "PittingProfiles", "id");
            R("Events", "TargetClass", "CarClasses", "Id");
            R("Events", "BonusId", "EventBonuses", "Id");
            R("Events", "Level", "PlayerLevelDef", "Id");
            foreach (string level in new string[] { "Easy", "Med", "Hard", "Pro" })
            {
                R("Events", "AISkill" + level, "AISkills", "id");
                R("Events", "AITemperament" + level, "AITemperaments", "id");
            }
            R("EventRandomBonuses", "BonusId", "EventBonuses", "Id");
            R("Rewards_DriverLevelv3", "DriverLevel", "PlayerLevelDef", "Id");
            L("NewProfile_Career_EventResults", "EventId", "Events", "Id", "profile table");
            L("NewProfile_Career_RaceResults", "RaceId", "Races", "Id", "profile table");
            R("AISkills", "Rubberbands_id", "AIRubberbands", "id");
            R("AILineChoices", "AISkills_id", "AISkills", "id");
            R("AIRubberbandModeChoices", "AIRubberbands_id", "AIRubberbands", "id");
            R("MPAIDifficultyMap", "AISkill", "AISkills", "id");
            R("MPAIDifficultyMap", "AITemperament", "AITemperaments", "id");
            L("AIPlayerColors", "PlayerId", "AIPlayers", "Id", "colors of removed drivers");

            // Upgrade shop, options, content.
            R("UpgradeTypes", "AreaId", "UpgradeAreas", "id");
            R("Upgrades", "TypeId", "UpgradeTypes", "id");
            foreach (string table in new string[] { "UpgradeWizardParts", "UpgradeWizardPartsRemoval", "UpgradeWizardPowerRemoval", "NewProfile_Career_PurchasedParts" })
            {
                R(table, "PartEnum", "UpgradeTypes", "PartEnum");
            }
            L("List_Aspiration", "KeyPartEnum", "UpgradeTypes", "PartEnum", "-1 for none");
            R("CarInvalidDefaultParts", "PartEnumValue", "UpgradeTypes", "PartEnum");
            R("ContentOffersMapping", "OfferId", "ContentOffers", "OfferId");
            R("GameOptionCategoryMap", "GroupId", "GameOptionGroups", "Id");
            L("GameOptionCategoryMap", "CatId", "GameOptionCategories", "Id", null);
            L("GameOptionCategoryMap", "OptionId", "GameOptions", "Id", null);
            L("GameOptionMap", "OptionId", "GameOptions", "Id", null);
            R("GameOptionMap", "ValueId", "GameOptionValues", "Id");
            R("VolatileHostOptions", "OptionId", "GameOptions", "Id");
            R("PerformanceBudget", "CameraType", "PerformanceBudgetCameras", "CameraType");

            // FM3 only (checked on the FM3 database; FM4 dropped these tables and columns).
            R("Data_Car", "GaugeID", "List_DigitalGauges", "GaugeID");
            L("List_TorqueCurve", "EngineID", "Data_Engine", "EngineID", "FM3: 16 curves of cut engines");
            R("List_UpgradeEngineCamshaft", "TorqueCurveZeroThrottleID", "List_TorqueCurve", "TorqueCurveID");
            R("AIPlayers", "FavoriteColorClassId", "List_ColorClass", "ColorClassId");
            R("Rewards_DriverLevelv3", "InitialCar", "Data_Car", "Id");
            R("Rewards_CarPartDiscounts", "CarMakeId", "List_CarMake", "ID");
            R("Rewards_CarPartDiscounts", "BrandId", "List_PartManufacturer", "Id");
            R("Rewards_CarPartDiscounts", "PartEnumId", "UpgradeTypes", "PartEnum");
            R("AES_Tip_Overrides", "EventId", "Events", "Id");
            R("ChampSeries", "EventId", "Events", "Id");
            R("Events", "GameTypeId", "GameTypes", "id");
            R("Events", "ScoringID", "EventScoring", "ScoringID");
            R("GameOptionGroupMap", "GroupId", "GameOptionGroups", "Id");
            R("GameOptionGroupMap", "CatId", "GameOptionCategories", "Id");
            L("NewProfile_DLC_CarsSeen", "CarID", "Data_Car", "Id", "profile table");

            // FH1 and FH2 (checked on their databases).
            R("Data_Car", "TireBrandID", "List_TireBrand", "TireBrandID");
            R("EventParticipants", "AIPlayerID", "AIPlayers", "Id");
            R("Events", "PlayerCarId", "Data_Car", "Id");
            R("Events", "HubId", "EventHubs", "Id");
            R("Events", "TimeOfDayDescriptionID", "TimeOfDayDescriptions", "Id");
            R("Events", "CampaignId", "Campaigns", "Id");
            foreach (string level in new string[] { "Easy", "Med", "Hard", "Pro" })
            {
                L("Events", "AIRubberband" + level, "AIRubberbands", "id", "FH1 uses 14");
            }
            R("Races", "RaceModeId", "RaceModes", "id");
            L("CarPIOverrides", "CarID", "Data_Car", "Id", "rows of cars not in the game");
            R("CarUpgradeExceptions", "CarID", "Data_Car", "Id");
            R("EventHubInitialEvents", "HubId", "EventHubs", "Id");
            R("EventHubInitialEvents", "EventId", "Events", "Id");
            R("EventRecommendedCars", "EventID", "Events", "Id");
            R("EventRecommendedCars", "CarID", "Data_Car", "Id");
            L("EventUIColors", "EventId", "Events", "Id", "colors of cut events");
            R("Event_Music", "EventId", "Events", "Id");
            foreach (string table in new string[] { "E3Drivers", "FirstTimeCareerDrivers", "FreeRoamDrivers" })
            {
                L(table, "AIPlayerId", "AIPlayers", "Id", "FH2: removed drivers");
                R(table, "CarId", "Data_Car", "Id");
            }
            R("Rewards_EventPrizes", "EventId", "Events", "Id");
            R("Rewards_EventPrizes", "RewardCarId", "Data_Car", "Id");
            L("Rewards_EventUnlock", "EventId", "Events", "Id", "FH1: one cut event");
            R("Rewards_EventUnlock", "UnlockEventId", "Events", "Id");
            L("Rewards_LoyaltyCars", "RewardCarId", "Data_Car", "Id", "FH2: 3 cars not in the game");
            L("Rewards_Wristband", "RewardCarId", "Data_Car", "Id", "FH2: 2 cars not in the game");
            R("WristbandScoring", "WristbandLevel", "CareerWristbandLevels", "Id");
            R("Data_Car_Buckets", "CarId", "Data_Car", "Id");
            R("Data_Car_Buckets", "CarBucket", "CarBuckets", "Id");
            L("CarBuckets", "ParentBucketId", "CarBuckets", "Id", "FH2 uses 41");
            R("Campaigns", "CarBucket", "CarBuckets", "Id");
            R("Campaigns", "RoadTripLocationId", "RoadTripLocations", "Id");
            R("CampaignEvents", "EventId", "Events", "Id");
            R("CampaignEvents", "CampaignId", "Campaigns", "Id");
            R("CampaignEvents", "RoadTripLocation", "RoadTripLocations", "Id");
            R("Anthem360OnlineEvents", "TrackId", "Tracks", "id");
            R("Anthem360OnlineEvents", "RoadTripLocationId", "RoadTripLocations", "Id");
            R("Anthem360RaceModeMapper", "RaceModeId", "RaceModes", "id");
            R("DestinationEventSets", "DestinationId", "RoadTripLocations", "Id");
            R("DestinationEventSets", "RoadTripId", "RoadTrips", "Id");
            R("DestinationEvents", "DestinationEventSetId", "DestinationEventSets", "Id");
            R("DestinationEvents", "EventId", "Events", "Id");
            R("DestinationEvents", "RoadTripEventType", "RoadTripEventTypes", "Id");
            R("DestinationEvents", "SponsorshipDealID", "SponsorshipDeals", "Id");
            R("DestinationEvents", "PlayerCarId", "Data_Car", "Id");
            R("DestinationFMVCutscenes", "RoadTripLocationId", "RoadTripLocations", "Id");
            R("MultiStageSeriesEvents", "EventId", "Events", "Id");
            R("MultiStageSeriesRaces", "MultiStageSeriesEventId", "MultiStageSeriesEvents", "Id");
            R("MultiStageSeriesRaces", "EventId", "Events", "Id");
            R("Rewards_EventCars", "EventId", "Events", "Id");
            R("Rewards_EventCars", "CarId", "Data_Car", "Id");
            R("BarnfindCars", "Ordinal", "Data_Car", "Id");
            R("ToggleActive", "Ordinal", "Data_Car", "Id");
            L("CarChallenges", "CarId", "Data_Car", "Id", "FH2: 11 cars not in the game");
            L("CarChallengeTitles", "CarId", "Data_Car", "Id", "FH2: 11 cars not in the game");
            L("OptionalRallyUpgrades", "Ordinal", "Data_Car", "Id", "FH2: one car not in the game");
            R("OptionalRallyUpgrades", "PartEnum", "UpgradeTypes", "PartEnum");
            L("NewProfile_RoadTripDestinationVisits", "RoadTripLocationID", "RoadTripLocations", "Id", "profile table");
        }
    }
}
