# Game database rules (integrity)

The integrity of `gamedb.slt` comes first. Every writer (the SQL console, Duplicate Car, the car port, a DLC pack's merge database) answers to the same rules, in `DbIntegrity.cs`: it compares the database before and after what it did, and the change is refused when a rule breaks.

## The rules

1. **Keys.** A new row's key is above the largest key FM4 and its DLC use for that column (one file per game, `docs/FM4/IdMax.json` and the other games, made by `tools/IdMax`: every table of the database, its key and the largest Id of each id column in the base game and its official DLC). A key is in a table once. The base game has `PRIMARY KEY` on 170 tables (SQLite refuses a second row); the 28 tables without one, and every merge database, rely on this rule. A column the table already repeats (`StartGridPositions.id`, `Combo_Colors.Ordinal`...) is not a key and is left alone. `ContentOffers.OfferId` is built from the pack's offer number and is exempt from the limit.
2. **References.** `GameDbSchema` holds the 340 relations of the database (the database has no `REFERENCES`). A *strict* relation has no dangling id in the original FM4, so a change must not give it dangling rows: a new row points at rows that exist, a row that others point at is not removed, renumbered or replaced. A *loose* relation already dangles in FM4 (`Combo_Engines.Ordinal` for cut cars...) and is only reported.
3. **Cars.** A `Data_Car` row that is added or changed has `ModelId = Id`, its `Data_CarBody` row (`Id * 1000`) and a `MediaName` no other car has.
4. **`Ordinal`.** In the car tables (`List_Upgrade*`, `CarPartPositions`, `Combo_Colors`, `EventParticipants`...) a car's Id is the `Ordinal` column (`CarId` / `CarID` in a few). `Ordinal` = `Data_Car.Id` of an existing car, always: it is never a counter, so never take `MAX(Ordinal) + 1`. Rule 2 enforces it.
5. **`car * 1000 + n` keys.** In a car table where every other car's rows have key / 1000 = `Ordinal`, the car's rows must too (`DbIds.CheckCar`, measured on the database itself). FM4 DVD1, 100%: `CarPartPositions`, `Combo_Colors`, `List_AntiSwayPhysics`, `List_SpringDamperPhysics` and the 11 `List_Upgrade*` tables. Tables numbered by a counter (`EventParticipants`, `Combo_Engines`, `Combo_Colors_Exceptions`, `ContentOffersMapping`, `Rewards_DriverLevelv3`) are left alone. Two official DLC cars break it in `Combo_Colors` (MAS_GranTurismoMC_10, AUD_2_R15_11), so the rule only binds what Forza Studio writes.

## Where each writer applies them

| Writer | What it does | Blocks on |
|---|---|---|
| SQL console (`SqlGuard`) | one statement runs on a copy first; the plan per table (rows added, removed, changed, the keys) is shown and the user confirms; then it is written inside a transaction (`BEGIN` / `COMMIT`, `ROLLBACK` on error) | every rule |
| Duplicate Car (`CarDuplicate`) | the copy's rows are inserted in one transaction; `DbIds.CheckCar` and `DbIntegrity.Compare` run on the result before any file is written; the plan lists the rows per table | every rule |
| Duplicate Car into a DLC pack | the same on a staged copy of the game; then the pack's merge database is added to a copy of the game database (after the other installed packs' merges) and the rules run on that | every rule; a failure removes the pack |
| Car port (`CarPort`) | `DbIds.CheckCar` as before, plus `DbIntegrity.Compare` against the database as the run started | a key twice, a broken car; an id that points at nothing and a key at or below FM4's largest are written in the log as warnings (the rows come from another game and keep their Ids) |

## Diagnostics (Tools > Database SQL > Diagnostics)

Ready-made SELECTs, they change nothing: rows per table; keys twice (tables with no declared key); dangling ids (strict relations: every row is an edit's; and every relation); cars with `ModelId <> Id`; cars without `Data_CarBody`; `MediaName` twice; cars above FM4's last official car; the rows of one car in every table; the rows with a key above FM4's largest (what was added). On the original FM4 DVD1 database: no strict dangling id, 33 loose ones, 0 rows above the limits.
