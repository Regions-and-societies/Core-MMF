# Colony → Regional Influence (design note, #58 follow-on)

**Status:** BUILT + validated in-game 2026-09-21 (#81, on the #33/#36/#81 cluster branch). The mechanic
below is implemented in `ColonyInfluenceRules` (pure), `CohortState.acceptanceOffset` →
`CohortYearRules.Step`, `GeographicProvince.xenotypeAcceptance` (scribed) and
`SynapseRegionManager.UpdateColonyInfluence` (colony example → self-relax → neighbour diffusion). What
remains from this note is the **infographic panel** (still TODO — see below). Builds on the #58 cohort
engine (`Design/DEMOGRAPHIC_MODEL.md`) and the colony-read seeding landed with #58.

Validation: a free "Testling" colonist over 30 years → home region acceptance 0.76, that cohort's standing
maxed and its share grew 25%→39%, acceptance radiating to neighbours decaying with distance (0.37/0.24/0.21/0.17).

## The idea (thematic)

The player's colony is not just *counted* in its region — it is an **example the planet notices**.
What the player *does* with xenotypes bends the demographic trends of the surrounding region, and
from there outward:

- Accept a Hussar as a **free** colony member → you demonstrate "Hussars can live free here." That
  xenotype's *standing* in the region rises → over the following years more Hussars **migrate in**.
- Hold that xenotype as **slaves**, or never accept them at all → the region stays **wary** → that
  xenotype avoids the area.

This is the thematic answer to *"why does one tiny map tile move a whole 23.4 km² hex?"* — the tile is
the player's, and the player's choices are a visible signal that shifts what the region finds normal.
**The infographic must state this** (see below).

## Why the engine already supports it

The per-cohort influence graph (DEMOGRAPHIC_MODEL §1–§8) already has every lever; this feature is a
*coupling*, not a new simulation:

- `standing = clamp(basePreference + region.ideoTolerance)`; **familiarity drift** already nudges a
  cohort's `basePreference` toward acceptance the more present it is.
- **Migration** already flows in with positive standing (`inMig ∝ region.wealth · (standing>0 ? 1 : 0.3)`)
  and out with low standing / high `flight`.
- `slaveShare` already derives from `region.slaveryStance` and the cohort's standing.

So the region already *attracts* what it accepts and *repels* what it degrades. What is missing is the
**player colony as an input** to the region stage, and the **outward spread**.

## What to build

1. **Read the colony's treatment, per xenotype.** For each xenotype present among the player's pawns:
   a *free* member (colonist/free) is a **positive** acceptance signal; a *slave* is a **negative** one
   (and a per-xenotype `slaveryStance` push); absence is neutral. Strength scales with how prominent the
   treatment is (a freed Hussar leader reads louder than one field hand — tuning TBD).
2. **Inject it into the player region's stage.** Bias that region's per-cohort acceptance
   (`baseInit` / a new per-xenotype acceptance offset) and `ideoTolerance` from the colony signal, on top
   of the colony-composition seed already landed. The existing yearly step then does the attraction/drift.
3. **Extrapolate to neighbours (the "one tile moves the hex" part).** Diffuse the shifted acceptance to
   adjacent land regions at **decaying strength**, reusing `ProvinceAdjacency` and the neighbour-similarity
   already computed (`RegionDemographicsUtility.AverageNeighborSimilarity`). Multiple hops = planet-scale
   drift over long time, bounded so a single colony cannot flip a continent overnight.
4. **Make it gradual and bounded.** Years, not seasons; capped so a tiny colony nudges rather than
   dominates. A colony is small; its *signal* is what carries, not its headcount.

## Infographic requirement

Add a panel/callout to the 0.5.0 population infographic series explaining the tile→region extrapolation:
*"Your colony is one tile in a 23.4 km² region, but your choices are an example the region copies — accept
a xenotype as free and more arrive; enslave them and they stay away."* This is the narrative justification
for why the player's single map affects regional demographics. See
[[roadmap-0-5-0-teaser]] / `About/release-media/0.5.0/`.

## Open questions (confirm before building)

- **Signal definition:** free/slave the primary axis? Also factor social integration (relations, roles)?
- **Strength & speed:** how fast should acceptance shift, and what is the cap per colony?
- **Spread:** how many adjacency hops, and how fast does it decay? Planet-scale eventually, or region + immediate neighbours only?
- **Symmetry:** does *rejecting*/enslaving actively push a xenotype's standing **down** region-wide, or only fail to raise it?
- **Sequencing:** land the #58 engine first and build this as its own feature, or fold into #58 before landing?

## Relationship to landed work

`feature/issue-58` already: aggregates region demographics from evolving cohorts; seeds the player's region
from the actual colony (composition, incl. custom xenotypes). This note adds the *dynamic* layer — the
colony's **treatment** of xenotypes as a force on regional (then planetary) trends.
