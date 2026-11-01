# URGENT (2026-10-31)

State after the string-key / make clash work. Commits: 595172d (Data_Car.str keys), d0fc578 (makes, title rules, logos), 10071cc (combo type-to-search).

## Done
- Packs built one after the other reused the same `Data_Car.str` keys (FFFB/FFFC...): the newest pack hid the names of the others (many cars shown as Evora S). `DlcPackage.BuildStrings` now moves a clashing key to a free one and `RemapStrings` rewrites the pack's rows.
- Make strings clashed too (Eagle over Hennessey, Viper over Lincoln): the `IDS_...` names carry the source game's ids, so a clash is now judged by the TEXT (`DlcPackage.SameString`).
- A make the game or an installed pack already has under the same ManufacturerCode is not added twice (`DlcPackage.ReuseMakes`; Viper 107 -> 104).
- Title rules (`BadgesAndTitles\<offer>.xml`) follow the make and unlock ids of the pack's final database (`MakeRuleIds` / `RuleBlock`).
- Make logos missing from the game and from the installed packs are carried by the pack (`ShipLogos`).
- Repair of existing packs: `DlcFixer.RekeyStrings(content)` and `DlcFixer.RepairMakes(content, gameRoot)`. The 4 packs of 2026-10-31 in Xenia's content folder (`4D530910\00000002`: GTX1 / Range Rover / Eagle Speedster / Evora S) repaired and checked: 21/21 car names right, Hennessey and Lincoln intact, Viper SRT10 ACR-X under make 104. Originals in `E:\DEV\fm4dev\PackStr\bak`, `bak2`, `bak3`; harness `E:\DEV\fm4dev\PackStr` (`r.exe` strings, `r2.exe` makes, `v.exe` check, `k.exe` make strings).
- Combo boxes: typing in any list picks the best entry (`ForzaTheme.TypeToSearch`). Not seen on screen yet.

## To do, in this order
1. **In-game check** of the 4 repaired packs (car names, make tiles: Viper / Lincoln / Eagle / Hennessey, Viper SRT10 under Viper).
2. **Rebuild a pack with the new code** (e.g. the Viper SRT10) and read the build report: "make N is 104 in ...", no second Viper make, no `43.xml`-style rule for it, logos line. ReuseMakes, ShipLogos, MakeRuleIds and RuleBlock only compile and ran as repair, never in a real build.
3. **Validate DLC for Installation** does not call `RekeyStrings` / `RepairMakes`: packs made before, or by others, with key clashes or a doubled make are not fixed there. Call both after `fixer.Relocate` in `DlcInstallForm` (user rule: the wizard must fix everything fixable).
4. **CarPort.StringKey** still picks keys against the game table alone (and reuses a key with the same `IDS_` name). It works because the pack build corrects it, but the port should look at the installed packs' strings too.
5. Other packs built in a row may have the same clash: run `RekeyStrings` / `RepairMakes` on them, lowest priority first (list the packs with `m.exe` / `k.exe`).
6. Eagle logo: the files exist in the FM4 base (all logo folders but `Manufacturer_Logos_SM_wtbg`, which FH1 lacks too). Unclear what the user saw missing: ask which screen.
7. Port log still shows `database warning: ... is not above FM4's largest` for ids kept from the source (engine 1057, drivetrain 1005, aero 16004xx, wheels 11160...): the pack build moves them (`RelocateKeys`) but the port does not; decide whether the port should take max+1 itself.
8. Combo search: combos created after `ForzaTheme.Apply` and the main form may not get it; check on screen.

## Update 2026-11-01
- 1 done by the user (in game, OK).
- 3 DONE (uncommitted, tested headless on the unrepaired originals of 2026-10-31, `E:\DEV\fm4dev\DlcInstall\inst.exe`): `DlcInstallForm.Check` now runs `Relocate`, `RepairMakes(world)`, `RekeyStrings(content)`, `MoveStrings`. Two bugs found by the test and fixed: `DlcFixer.OpenMerge` and `LoadStrings` reloaded from the layer and threw away what the step before had changed in memory (Relocate's ids were lost; RekeyStrings's keys were lost, MoveStrings then dropped the Data_Car table with 8 of 12 names). Both are now "load once". `DbIntegrity.CheckMerge`: a package already installed that shares rows with the game is added with INSERT OR IGNORE (before, one duplicate row aborted it and hid its makes: false "make 104 does not exist"). Results: Eagle pack OK, Evora S pack (Viper 107 -> 104, title rule 43.xml removed) CHECK ok.
- 4 CODED, not tested in a port: `CarPort.PortStrings` reads the installed packs' keys (`StringLayer.Collect`), a key an installed pack holds is not free, a new key = highest key in the game table + installed packs + this port + 1 (below FFFF; from FFFE down only when that is used up). To check: port a car, read the "string key ... used" report line.
- 5 not a bug: FM4's Data_Engine max is 1098 (GameIdLimits.json, base game + DLC); the ids kept from FH1 (1057...) are inside FM4's range, so the warning is right. Not changed (user: not interested).
- 2 waits for the user's Viper + Eagle ports.

## Update 2026-11-01 (evening), user tests 1-6
- 1, 2 (fresh Viper + Eagle pack), 3, 6 OK. 4 (wizard on the old Evora S pack F98B put back) broke names: the pack still shipped copies of List_CarMake.str / List_BadgeAndTitleUnlocks.str (they replace the tables of the packs below: Eagle lost its name, Lincoln would read Viper via key B08D).
- Fixed (uncommitted): `DlcFixer.DropRedundantTables(content, gameRoot)`, called by `DlcInstallForm.Check` after `MoveStrings`: a shipped table whose strings the pack's database no longer uses (or that equal game + lower packs, "N/A" placeholders ignored) is not shipped. Headless on the original F98B with VIPER_EAGLE + Viper pack installed: only Benchmark.str left, CHECK ok.
- Eagle twice (FM4 has Eagle id 87 EAG, USA; FH1's is EAE with country 9 = UK): `CarPort.PortMake` now reuses a FM4 make with the same English name (`TargetMakeByName`). NOT tested in a port. The VIPER_EAGLE pack already installed still has make 106 (EAE, UK, 1 car): rebuild it with the new code, or write a repair that folds it into 87.
- Two Viper SRT10 ACR-X under Viper = the old Evora pack carries its own copy (DOD_ViperSRT10ACRX_12_2), not a bug.
- CORRECTION (user, same evening): VIPER_EAGLE is the correct, final pack (its own Eagle make 106 is wanted). The damage came only from running the install wizard on the old Evora S pack (F98B), which kept its stale List_CarMake.str copy. The port's "same name" make reuse (`TargetMakeByName`) was NOT asked for and is REMOVED again; only `DropRedundantTables` stays.
