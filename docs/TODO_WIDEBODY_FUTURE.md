# TODO WIDEBODY (FUTURE)

Widebody in FM4 requirements

Credit: these notes come from Aiden-Piercee (https://github.com/Aiden-Piercee).

## Database (new rows, can go in a DLC execute.sql)

1. **List_UpgradeCarBody** - a Level 1 row for the car (Id/CarBodyID = carId*1000+1). This is what makes the "Wide Body Kit" tile show under Conversion > Body Kit
2. **Data_CarBody** - a row for that new CarBodyID. Copy the stock one and widen ModelFrontTrackOuter / ModelRearTrackOuter so the wheels move out
3. The CarBodyID-keyed kit tables for the new body: FrontBumper, RearBumper, SideSkirt, Hood, TireWidthFront, TireWidthRear, Weight, ChassisStiffness. Copy them from stock, otherwise those pages break
4. **List_PartsStrings** - new strings that hide the stock pieces and show the new ones, e.g. `"Hooda,-body,bodyc"` or `"-bumperFa,bumperFc"`
5. Point the widebody's kit rows (we use the hood row for the body swap) at those strings. Only leaving one row per slot locks the kit like FH3 does

## Model

6. New pieces added to the car's carbin (main + _lod0), e.g. bodyc / bumperFc / wingc. Name can be anything, it just has to match the parts string
7. Ship the edited carbins as an override zip mounted on `game:\Media\Cars\<CAR>\` (zipmount.xml), higher priority than the original

## Not needed

- No code changes
- No UpgradeTypes row for level 1, the tile already exists in the base game (only levels 2+ need new Upgrades rows, e.g. for spacers)
- No new file names, the car still loads `<car>.carbin` / `_lod0` as normal

## Gotchas

- "body" is always shown, so it has to be hidden with `-body` in a parts string
- Without step 6/7 you still get the wider track, just no visual change (that's how wheel spacers work)
