# Porting notes & gotchas — Core-MMF ⇄ Core-RP2

Regions and Societies ships as **two editions** from near-identical source:

- **Core-MMF** — the base edition, built against **Map Mode Framework** (About `<name>` ends
  "(Map Mode Framework)").
- **Core-RP2** — the **Realistic Planets 2** fork (About `<name>` ends "(Realistic Planets 2)").

`packageId` and all C# namespaces are **identical** across editions — only About.xml `<name>`,
the framework dependency, and a few framework-touch points differ. Most source files are
**byte-identical**; changes are typically authored once and applied to the other edition with
`git am` (that is how #55's registration hook landed in both repos from one patch).

This doc collects everything that is *not* obvious from the code, so a future port is mechanical.
Add to it whenever you hit a fork-specific surprise.

## Framework touch points (the parts that actually differ)

- **RP2 removed `MapModeUI.DoDrawSettingsExpanded`.** The border-toggle Harmony patch (#81) must
  `Prepare()`-skip itself when the method is absent, or `PatchAll` throws and sinks the whole mod
  ctor (including the VOE/Empire/provider registration below it). Under RP2 the startup log line
  `DoDrawSettingsExpanded=false` is EXPECTED, not a bug. The mod-settings "Draw region borders on
  the world map" checkbox exists precisely so the toggle is reachable under frameworks that lack
  that panel.
- **RP2 changes sea level / planet type**, which changes the land/water split of a generated
  world. See "Region-count estimate" below — the pre-gen estimate is the only thing affected;
  once a world exists everything reads the real grid.
- **RP2 sea-level / planet-type fields are read REFLECTIVELY** (field names discovered at impl
  time), the same pattern as the `Page_CreateWorldParams` coverage read in the placement dialog.
  Never hard-reference an RP2 type from Core — the mod must build and run against Map Mode
  Framework alone.

## Region-count estimate (calibration — 2026-09-08 campaign, vanilla / Map Mode Framework)

Measured with the dev quicktest coverage override (`devQuicktestCoverage`), one seed per size,
`targetRegionSize=150`, via the worldgen `CALIB:` log line:

| coverage | total tiles | land frac (this seed) | land regions | avg region tiles |
|----------|-------------|-----------------------|--------------|------------------|
| 5%       | 3,787       | 62%                   | 13–15        | 158              |
| 10%      | 14,613      | 25%                   | 23           | 162              |
| 30%      | 119,904     | 41%                   | 225–239      | 217              |
| 50%      | 295,732     | 55%                   | 823          | 197              |
| 100%     | 590,492     | 67%                   | 1,789        | 221              |

Gotchas baked into `PlacementEstimates`:

- **`totalTiles` is DETERMINISTIC per coverage** (5% gave 3787 on two different seeds) but wildly
  non-linear. The old `100000 × coverage` was off by up to ~6× at high coverage; replaced by the
  measured anchor curve in `EstimateTotalTiles(coverage)` (linear interpolation).
- **Land fraction swings 25%→67% by SEED** — unknowable before generation. The pre-gen estimate
  uses a mean (`TypicalLandFraction = 0.5`) and the dialog labels the number "rough — varies with
  sea level" rather than a tight band. **RP2's sea level shifts this mean further**, so the RP2
  edition should re-measure land fraction (and ideally the tile curve) and, if it differs enough,
  read RP2's sea-level setting reflectively to pick a better pre-gen land fraction.
- **Average region size GROWS with world size** (158 tiles at 5% → 221 at 100%), because the
  partition/merge lets regions run larger on big worlds and sparse biomes scale up. So
  `regions ≈ landTiles / targetSize` slightly over-counts big worlds; it roughly cancels the higher
  land fraction there, so the net estimate is within ~10–20% at 50%+.
- Once a world EXISTS the dialog counts real land tiles from the grid and reports the exact region
  count — no estimate. Only the pre-gen (world-creation screen) path uses the curve above.
- **`#54` RP2 acceptance matrix is still deferred** — re-run this table under RP2 at 30% + 100%,
  accept ±15% on the region estimate, and bake RP2's land fraction / tile curve if it diverges.

## RP2 sea-level / planet-size handling (implemented 2026-09-08, #54 tail)

`RealisticPlanetsProbe` (`Source/Integration`) reads RP2's world-shaping settings by reflection —
RP2 is never hard-referenced, so the file is inert on the base edition and lives in BOTH editions.
Confirmed field paths (decompiled from `Realistic_Planets_2.dll`, then read live via
`inspect_csharp_field`):

- **Planet size** = `Planets.Core.Planets_GameComponent.subcount` (static `int`, the "Planet Scale"
  slider, range 5..11, default 10 — it sets `PlanetLayerSettingsDefOf.Surface.settings.subdivisions`,
  i.e. the icosahedron subdivision level → tile count). NOT vanilla coverage. `PlacementEstimates.
  EstimateTotalTilesRP2(subcount, coverage)` = vanilla curve × **3.0^(subcount − 10)**. CALIBRATED
  2026-09-08 (seed biomemix, 30% coverage): subcount 5/8/10 = 488 / 13,323 / 119,904 — a clean ×3.0
  per step (119904/9 = 13323 exactly; subcount 10 = the vanilla 30% anchor). The tile count grows
  EXPONENTIALLY with Planet Scale — an earlier (subcount/10)^2 guess was ~60× off at subcount 5. To
  RE-MEASURE: set `devQuicktestSubcount` in the RP2 mod-settings XML, -quicktest, read the `CALIB:` line.
- **Sea level** = `Planets.Core.Planets_GameComponent.seaLevel` (static enum `Planets.WorldGen.SeaLevel`
  = {Low, SlightlyLow, Normal, SlightlyHigh, High}, read as its 0..4 ordinal; Normal = 2). Higher = more
  ocean = less land. `PlacementEstimates.LandFractionForSeaLevel(ordinal)` = **{0.62, 0.56, 0.50, 0.37,
  0.23}**, CALIBRATED 2026-09-08 from a controlled biomemix sweep (Low 72.5% / Normal 58.6% / High 27.2%
  at subcount 10, 30%): Normal anchored to the cross-seed mean 0.50 (two Normal seeds: 43.3%, 58.6%), the
  measured spread applied, "slightly" levels interpolated. Land fraction still swings widely by seed, so
  it stays the "rough" pre-gen number; more seeds per level would refine it. RE-MEASURE via
  `devQuicktestSeaLevel` in the RP2 XML, same `CALIB:` channel. (Both dev overrides are RP2-only, off in
  normal play.)

Both are read only in the placement dialog's pre-gen branch (`Dialog_FactionPlacementSettings` ~L90).
Rainfall + axial tilt were intentionally left out (they are already vanilla/MMF sliders, not RP2-
unique) — a possible 0.5.0+ refinement, not part of this.

## About.xml is XML — escape "&" in the description (learned the hard way, 2026-09-08)

The `<description>` is parsed as XML, so a raw `&` (e.g. a changelog header "Map & Placement") throws
`error parsing EntityName` and RimWorld loads the mod with DEFAULT metadata — effectively dropping it.
The C# build and the release deploy never parse About.xml, so it ships undetected (it did, in v0.4.0,
on BOTH editions). Use `&amp;`. `&` inside an XML comment is fine (not entity-parsed). Steam
`steam_description.txt` (BBCode) and `Learning/*.md` (Markdown) are NOT XML — raw `&` is fine there.
Validate after editing About.xml: PowerShell `[xml](Get-Content <file> -Raw)` throws on a malformed one.

## Placement value model (#47)

- Each faction's stored `placementShare` is read per the global `placementValueMode`: **Percent**
  (default) = a share of the total LAND regions, normalised across present factions and filled to
  the `claimedLandAreaPercent` density knob; **Count** = a literal region target.
- `PlacementShareRules.DistributeRegions` is the single arithmetic the dialog, the pie chart and
  worldgen all call — keep it that way across editions so what the player previews is what generates.
- The whole-planet-absolute percent basis was **dropped from the UI**; `PlacementPercentBasis`
  survives only to keep `DistributeRegions` general and its tests covering both apportionment paths.

## Clustering + kin (#46/#57) — the cluster field is MODE-DEPENDENT

- **Count mode**: the cluster field is the **size** of the largest contiguous body (regions).
- **Percent mode**: the cluster field is the **number of clusters** the faction breaks into.
- `ClusteringRules.EffectiveBodyCap(mode, field, plannedRegions)` resolves both to a body-size cap
  the placement ranking consumes; `SubFactionRules.PlannedKinCount(mode, field, regions)` is the
  matching dialog readout.
- **Kin = one faction per contiguous body** (the owner's model), NOT the old ≤3 geographic
  sections. `MaxSections`/`SectionCount`/`AssignSections` still exist in `SubFactionRules` but are
  no longer used by worldgen — `SectionLabels` IS still used (it names N bodies, numbering the
  middle ones). If you delete the dead section helpers, update the tests that still exercise them.
- **Faction-budget crowding**: one-faction-per-body is capped only by
  `MaxWorldFactionsAfterSplit` (40). A very scattered faction processed early can consume most of
  the budget, starving later factions' kin. Acceptable per design; revisit if it bites.

## Test harness

- Suites compile FILE SUBSETS, not the whole assembly. A pure rule that references a type in
  another file needs that file added to its suite in `Tests/run-tests.sh`. The value-mode enums
  live in their own `Source/Placement/PlacementValueMode.cs` precisely so the `placementshare`,
  `clustering` and `subfaction` suites can each compile them.
- `IExposable`/`IntRange` stubs live in `RimWorldStubs.cs` (base), not `RimWorldStubsExt.cs`.
- Impure files (dialogs, patches, WorldComponents) are signature-typechecked, not run.

## Dev / validation environment

- **`Mods/Core` is RimSynapse Core, NOT this mod.** R&S loads from `Mods/Core-MMF`. The repo is
  symlinked in, and `dotnet build` writes to `Assemblies/` which IS the loaded binary; the deploy
  tool also copies a clean build to the Steam Mods folder.
- **The dev modlist drifts** (Psychology/others auto-add → the launch comes up 7 mods, or 4 with
  R&S missing). Re-run `configure_active_mods` with the exact 6-mod list and verify
  "Game initialized 6 mod(s)" every launch:
  `Ludeon.RimWorld, brrainz.harmony, nozome.mapmodeframework, rimsynapse.core, archdukejim.rimagentic, regionsandsocieties.core`.
- **Quicktest overrides** (dev-only, `FactionPlacementSettings`): `devQuicktestCoverage` (>0
  overrides planet coverage; vanilla quicktest fixes 30%) and `devQuicktestSeed` (pins the world
  seed). Set them in `C:/RimWorldDevData/Config/Mod_Core-MMF_RegionsAndSocietiesMod.xml`.
- **Worldgen is slow at scale**: ~1.6s at 5%, ~32s at 30%, ~160s at 50%, ~**10 min** at 100%
  (~590k tiles). Give a long `readyTimeoutSec` and read the `CALIB:` / "World generation completed"
  lines from Player.log rather than waiting on the bridge, which times out during gen.
- A harmless startup "Native Crash Reporting / UNKNOWN … Managed Stacktrace:" with an EMPTY stack
  fires during Harmony `PatchAll` and self-recovers (all patches still apply) — not a real crash.
- The R&S `rt_*` MCP tools (partition audit, placement probe, share report) were **not reachable
  via `execute_game_tool` this session**; the worldgen log is the reliable data channel. Debug
  actions are the durable headless trigger when the reflection tools are unavailable.
