# Wilderness population — a seeded base-population surface

Written 2026-09-19. A 0.5.0 design decision that reorders the milestone: it is a **prerequisite
for #58** (the demographic influence graph) and everything dynamic that hangs off it.

---

## 1. The problem

The living demographic model needs people to live *on the land*, not only in named settlements.
Today they don't:

- **Hinterland is tied to settlements.** `DistrictRules.HinterlandPopulation = settledPopulation ×
  0.25`. A tile with no settlement carries **zero** modelled people — the wilderness between
  settlements is empty. Population exists only as a 25% skirt around settled tiles.
- **Settlements themselves are still tiny.** NPC settlements seed at the pre-district scale
  (`SettlementSizeRules` / `StaticNpcPopulationEstimate`: ~40–169 people each). That is the ~30×
  undershoot #30 exists to fix.

Net at 30% coverage (~197 settlements): the whole modelled world holds on the order of **~20,000
people**. A demographic field, migration and dynamic shifts have almost nothing to move around. A
living world has to have the **countryside carry most of the population** — which, for a
pre-industrial world, is historically true: ~80–90% of people live rurally, not in named towns.

So the model is currently the wrong way round: ~100% settlement, ~0% countryside. We need to invert
that **before** building the demographic graph on top of it.

## 2. The decision — a seeded procedural surface, not discrete sources

The wilderness population is a **seeded, procedurally generated base-population surface**, baked from
the world seed and the land itself, sampled O(1) per tile. It is **not** a set of world objects and
**not** a set of discrete pressure sources.

> **Rejected: per-tile pressure sources.** Giving every land tile a source would mean ~49,000
> sources at 30% coverage (vs ~197 settlements today) — a ~250× explosion that #61 could not
> survive, and the source-culling by reach (#20/#70) would become meaningless because every tile
> would be a source of its own. The countryside must be a **smooth background field underneath** the
> settlement sources, not more sources.

The surface is:

- **Deterministic from the world seed** — the same seed always produces the same countryside, so it
  saves as nothing more than the seed and is identical on reload.
- **Land-driven** — weighted by biome habitability, so people cluster on good land (river valleys,
  temperate forest) and thin out in desert, ice and mountain, *before any settlement exists*.
- **Closed-form per tile** — like `DistrictRules`, a district/tile population is computed, never
  instantiated. Sampling a tile is a hash + a couple of multiplies.

### There is already a precedent to build on

`PopulationDensityUtility.RefreshCache` step 1 (the #62 "natural pockets") is *already* a seeded
per-tile population field: `UnityEngine.Random.InitState(i * 377 + 99)`, ~6% of habitable tiles roll
a pocket of `Random(2,8) × (plantDensity + forageability + 0.2)`, capped at 5 / 12 / 6 by landmark
and danger. It is deterministic, O(tiles), allocation-light, and biome-weighted — exactly the shape
we want. It is simply **tuned tiny and wired only to the heatmap**.

The wilderness surface is a **scale-up and repurposing of that mechanism**: raise the target so the
countryside reaches the density we need, weight it by `BiomeHabitabilityRules` rather than raw
`plantDensity`, and expose it to the demographic model as a first-class per-tile base population —
not just a heatmap smear.

## 3. How much population — the arithmetic

Geography is fixed by the scale work: a land tile is **23.4 km²**. Land-tile counts (from the
port.md calibration; seed-dependent):

| Coverage | Land tiles | Land area |
|---|---|---|
| 30% (working baseline) | ~49,000 | ~1.15M km² |
| 50% | ~163,000 | ~3.8M km² |
| 100% | ~396,000 | ~9.3M km² |

World population is then **≈ mean rural density × land area** (settlements are a small addition on
top). Candidate densities:

| Density | Per tile | **World @30%** | @50% | @100% | Reads as |
|---|---|---|---|---|---|
| 0.5 /km² | ~12 | ~0.6M | ~1.9M | ~4.6M | very sparse frontier |
| **1 /km²** | ~23 | **~1.2M** | ~3.8M | ~9.3M | today's homestead-hinterland floor |
| **2 /km²** | ~47 | **~2.3M** | ~7.6M | ~18.5M | sparse agrarian |
| 5 /km² | ~117 | ~5.7M | ~19M | ~46M | light medieval rural |

**Proposed target: a mean of ~1–2 /km², biome-scaled — ~1.5–2.5M at 30% coverage.** Rationale:

- **1 /km² is already blessed** as the density the model treats as acceptable for a homestead's own
  hinterland (`DistrictRules.HinterlandDensityPerKm2` ≈ 1/km²). Using it as a *land-wide floor* is
  the smallest defensible step, not a new invention.
- **Stay ≤ ~2 /km² so settlements remain the peaks.** At 5/km² a wilderness tile (~117 people)
  out-populates a homestead settlement (100 settled), and named places stop reading as population
  centres — the same rounding-error trap the district model was built to escape, at the other end.
- **The mean is biome-scaled, not flat.** Desert/ice wilderness sits near 0; temperate forest and
  river valleys sit above the mean. The habitability weight (`BiomeHabitabilityRules`) is what makes
  the countryside interesting on its own.

The density is a **tuning target, not a hard number** — §6 pins it down in the sim against the
behaviour it has to produce, then in-game against the border-blend and migration targets.

## 4. How settlements and the surface compose

The countryside is the floor; settlements are concentrations on top. Two rules keep them honest:

1. **No double-counting.** A settled tile's population is its settlement (`DistrictRules`) — the
   wilderness surface does not *add* to a settled tile, it is what the tile would hold with no
   settlement. `HinterlandPopulation` (the settlement's own farm skirt) is a separate, settlement-
   owned quantity and stays; the wilderness surface fills the tiles the skirt never reaches.
2. **Settlements draw from their surroundings.** A settlement's presence should depress or absorb the
   raw wilderness figure locally (people are *in* the town, not also in the fields it stands on),
   rather than stack on top of it. Exact reconciliation is a §6 tuning question.

The demographic pressure field then has two layers: **discrete settlement sources** (culled by
reach, as now) over a **continuous biome-weighted countryside floor** (sampled, never a source).

## 5. Where it lives in the code

Keep the repo's pure-rules convention (pure in, scalars out, unit-tested against the doubles):

- **New:** `Source/Sizing/WildernessPopulationRules.cs` (proposed) — pure: `(seed, tileHash,
  BiomeTraits, habitability) → base population`. Deterministic, biome-weighted, normalised to the
  target mean density. Unit-tested like `DistrictRules`.
- **Reuse:** `BiomeHabitabilityRules` / `BiomeTraits` (`Source/Placement/`) for the land weighting;
  `WorldScaleRules` for tile area; the seeded-hash pattern from `PopulationDensityUtility` step 1.
- **Wire:** `PopulationDensityUtility` gains the surface as its baseline layer (replacing/absorbing
  the tiny natural-pocket step), and the demographic field (`RegionDemographicsUtility` /
  `SynapseRegionManager`) reads it as the countryside floor beneath settlement sources.

## 6. Open questions / tuning plan

Prototype in `Design/sim` before committing constants:

- **Mean density** (§3): 1 vs 2 /km², validated against whether migration and shifts are legible
  without demoting settlements.
- **Biome spread**: how steeply habitability maps to density (desert floor, temperate peak).
- **Settlement reconciliation** (§4.2): how a settlement depresses/absorbs its local wilderness so
  the two layers don't double-count.
- **Field cost** (#61): confirm the surface is genuinely O(tiles) sampled and does not reintroduce
  the precompute cost — it should be *cheaper* than discrete sources, not another 22 s.
- **Determinism**: seed → surface must be stable across reload and independent of world-object
  churn (unlike the current cache, which refreshes on every settlement change).

## 7. Sequencing

This precedes #58. Order:

1. **This** — the wilderness surface (rules + sim tuning + wire-in), so the world carries people.
2. **#58** — the demographic influence graph, now with a populated substrate to model.
3. The rest of the demographic issues (#29, #33/#34, …) on top.

#30's threshold retune (settlement scale) and this (countryside scale) are the two halves of "make
the world's population real"; they should be tuned with an eye on each other so settlements stay the
peaks above the countryside floor.
