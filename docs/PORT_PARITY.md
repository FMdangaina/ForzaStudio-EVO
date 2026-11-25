# Port windows: what each game's window has (2026-11-26)

User 2026-11-26: "Create DLC package for: FM2" must be enabled and built. Before the DLC: "mancano parecchie cose,
qualcuna su fm3 e tutte su fm2. FACCIAMO UN LAVORO COMPLETO."

Three windows: FM4 (`CarPortForm`, `.Makes`, `.Upgrades`), FM3 (`CarPortFm3Form`, `.Choices`) and FM2
(`CarPortFm2Form`). FM4 and FM3 each have two modes, Import and DLC (`PortMode`). FM2 has Import only, and
`PortPairs.CanCreateDlc("FM2")` is false, so the button is greyed.

## 1. What each window has

| Feature | FM4 | FM3 | FM2 |
|---|---|---|---|
| Source: game folder, **Package...** (single package files) | ✅ combo + info line | ✅ | folder only |
| Cars: search, include the game's DLC | ✅ | ✅ | ✅ |
| **Also show cars the target already has** | ✅ (count of hidden) | ✅ "In FM3" column | ❌ |
| Traffic cars in yellow | ✅ | ✅ | ✅ (2026-11-25) |
| Source **PI** column | ✅ | ❌ | ❌ |
| Template car | ✅ ([DLC] marked) | ✅ | ✅ |
| **Arcade** | ✅ | ✅ | always (user rule) |
| **Make** + **New make** | ✅ | ✅ | ❌ (`MakeID` copied; `CarMake` has no FM2) |
| Engine sound: the source's / a target car's | ✅ | ✅ | ✅ (2026-11-25) |
| Tires (Normal / Offroad / Small / Offroad Big) | ✅ | ❌ | n/a (FM2 shared tire texture) |
| Front / rear brakes + caliper angle | ✅ | ✅ | n/a (FM2: no per-car brakes) |
| Trial Run, Install | ✅ | ✅ | ✅ |
| **Open Trial Folder** | ✅ | ❌ | n/a (the FM2 trial writes no files) |
| **Uninstall...** (take a ported car out of the folder) | ✅ | ❌ | ❌ |
| Tab Report | ✅ | ✅ | ✅ |
| Tab **Review** (what the port changed or could not do, **Fix** choices) | ✅ | ✅ | ❌ (Report lines only) |
| Tab **Upgrades** | ✅ editable: swaps add / remove, drive type | read only (part, level, item, in FM3) | ❌ |
| Tabs Variants, Materials (buffer counters, material choices) | — | — | ✅ (FM2 only) |
| **DLC mode**: pack list Add / Remove, New pack, Name, Offer + Free | ✅ | ✅ | ❌ |
| DLC: **First car id** | ✅ | ❌ | ❌ |
| DLC: Trial Pack, Build for Xenia, Remove Pack | ✅ | ✅ | ❌ (`DlcPackage.BuildFolderFm2` exists, ST-02) |
| DLC: **Add to existing package** (`DlcPackageAdd`) | ✅ | ❌ | ❌ |
| DLC: Build for Xbox 360 | ✅ | ✅ | ❌ |
| DLC: the pack is **remembered** between sessions (`RestorePack`) | ✅ | ❌ | ❌ |

## 2. FM2 data still the template's (rule: nothing of the template may stay)

From `PORT_TO_FM2.md` section 5:
- the car zip's `ShaderSettings.xml`;
- `List_Wheels.IconPath` (the template rim's picture);
- ids an FM2 lookup lacks (FM3 `FamilyModelID` 26) keep the template's;
- the rim size upgrade on a converted wheel: not studied yet.

## 3. Plan (order proposed to the user)

Each UI step is described to the user and gets a yes before it is built (`describe-ux-before-building`).

**A. FM2 data** (no UI):
1. `ShaderSettings.xml` from the source game's car (or FM2's typical when the source has none).
2. A make FM2 lacks: list which source makes FM2 lacks, then decide (FM2 make rows or `CarMake` for FM2).
3. `IconPath` and lookup ids: what FM2 has for them.

**B. FM2 import window**:
1. **Also show cars FM2 already has** + port again over an installed car (same id, upgrade ids kept, as `data2` does).
2. **Package...** source.
3. **Review** tab: the "not ported" lines, the materials and variants left out, the template's left items, with Fix where
   the port has a choice.
4. **Upgrades** tab: each part and level, the source item and what FM2 got (read only, as FM3's).
5. **Make** + New make for FM2.
6. **Uninstall...**.
7. Source PI column.

**C. FM2 DLC mode** ("Create DLC package for: FM2"), port first, then into the pack (user 2026-11-26):
1. The same window in DLC mode: after the car's trial, **Into DLC →** puts it in the pack with all its choices
   (template, variants, materials, sound).
2. Pack panel: list with Remove, New pack, Name, Offer + Free, First car id (659-1000), Trial Pack, **Build DLC Package**
   (Xenia's FM2 content folder, `DLC<offer>`), Remove Pack, the pack remembered between sessions.
3. `BuildFolderFm2` extended with HarmonicTuning and `Soundbanks\LOD1/LOD2` (DLCBeta's layout) for "Port sound".
4. Later: Add to existing package, Xbox 360.

**D. FM3 window** (what FM4 has and FM3 lacks): Uninstall, First car id, the pack remembered, Add to existing package,
Upgrades editable (swaps, drive type), source PI column.
