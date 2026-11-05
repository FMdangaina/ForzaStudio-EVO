# Task status - chat of 2026-10-27 (test queue of TODO.md), state at the end of the day

> **Closed, kept for history** (2026-11-05). Everything below has been committed and pushed since (tag v0.0.1);
> open points live in `TODO.md`.

The full lists are in `TODO.md`; this file is only this chat's state. Everything below is committed, nothing is pushed (the user pushes, D-4).

## Commits of the day (oldest first)
- `cd8361a` Engine sound and numbers, ratings at import (D-14), make logos (D-13), Tools > Car Performance From Game (D-15).
- `0effa50` Fixes from the tests: DB writes of two tools, source Id clash, FH1 / FH2 crash damage +1.
- `b5e44cf` Port: DSC engines get FM4-like blow-off slots in the engine tuning (other chat).
- `a5406b6` **D-18** donors from installed DLC packs (`CarPort.ResolveDonor`, `[DLC: pack]` in the donor list), FM2 sources refuse a DLC donor, tolerant Xenia path (`DlcPackage.XeniaContent`).
- `61959e1` WIP digital dash editor, **suspended** (Add > Gauge crashed, Add > Texts from another car did not work; cause unknown, TODO 12.6 - 12.9).
- `2cb4591` Event poster `EventBkgd<Id>` in the archive creators (other chat).
- `7045a97` **D-16** tire zips by the CAR's name (`BuildTireZips`), **D-19** Tires option Normal / Offroad (H1), donor chosen by the user (no suggestion).
- `24b6715` Offroad tire = the H1 model as it is, no scaling.
- `0613fb7` **D-2** a missing light make logo shows the dark one.
- `6f2ffd4` Donor list rereads the packs when Xenia's content folder changes; D-8 run on the real folders.
- `ed1150b`, `c58adf5`, `3d77280`, `24ccbf5` TODO updates.

## Closed today (by the user)
T-ED1 - T-ED9, T-G11, T-G14, T-G15, 8.1, D-2, D-8, D-16, D-18, 3.9. T-G7 and T-G10 postponed by the user. Dash editor suspended.

## Still to see
- T-G8 (13.1): menu song `blowmeaway22` created, test with a modified file holding only that song; second song, mono / 48 kHz.
- T-G12 / B1: CLOSED by the user (BRZ port deforms well with the damage scaler +1).
- D-17 / the Raptor grip (suspended): FH1 / FH2 ports have more grip than FM4's own car; the tire zips are now by car name, so look again.
- 3.5 missing sounds across generations (classify cases, mapping table); the donor engine sound read from a DLC pack (`ReadTuning`) was never reached by a trial.
- Waiting for the user's .carbin: offroad tires, small tires, generic brake discs for "standard" cars (FM2 / FH1 / FH2 traffic).
- Other open points: 2.3.2 own engine from scratch, 3.2 gr2 BE / LE, 6.2 driver position, 5.3 FM1 / FM2 close-up, 8.2 make extras, 10.4 tyre width, 1.3 / 1.4 checks, 16.1 docs.

## Harness of this chat (outside the repo)
`E:\DEV\fm4dev\Ratings` (pr, dump, cmpcar, zipdiff) and `E:\DEV\fm4dev\Donor` (pr2 with TIRES env, packs, d8, ze / zx / tb tools; `out*` trial folders). Build with VS 2022 MSBuild, Forza Studio and Xenia closed before any write to `FM4_Extracted_DEV`.

## Notes
- The Xenia folder in Settings may be any level of the path (`content\0000000000000000` as the user typed it works).
- FM4 has one pack in Xenia now (P3_TEST); the official packs were taken out, the donor list follows what is in the folder.
