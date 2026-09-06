# Regions & Societies — demographic model (LOCKED design of record, 0.4.0 keystone)

Status: **topology locked 2026-09-06.** This is the canonical spec the C# build follows. It consolidates the
design iterations and the calibration simulator (`Design/sim/`). Weights here are first-pass, to be calibrated
against the simulator and in-game dry runs; the **structure** is fixed.

Design law: **Core simulates; compatibility patches decide gameplay.** Everything degrades cleanly without DLC
or consumer mods. Nothing here rewrites the player's colony pawns.

---

## 1. The pivot: xenotype cohorts are the unit; the region is an aggregate
A region holds a set of **xenotype cohorts**. Every person-level axis is tracked **per cohort**; the region-level
numbers shown on overlays and the region panel are **population-weighted aggregates** over the cohorts.

- **Roster is discovered at runtime.** A region's cohorts = `PawnGenerator.XenotypesAvailableFor(faction)` (already
  reflected in `RegionDemographicsUtility`). Modded xenotypes are included automatically; xenotypes from an absent
  DLC are absent automatically. **No Biotech → the def database holds only Baseliner → one cohort → the model is
  mathematically identical to the old region-level model.** Degrading is free, not a special case.
- **Per-xenotype constants are read from the xenotype's own genes**, so a modded xenotype gets sensible values with
  zero knowledge of it: lifespan ← Longevity/Ageless genes; genetic drug dependency ← ChemicalDependency gene;
  fragility ← frailty genes; **heritability ← `XenotypeDef.inheritable`**.
- **Performance guardrail:** track the **top N cohorts (N≈4) + an "other" bucket** per region. Most regions are one
  or two xenotypes. N is a tunable constant.

### Per-cohort axes (each cohort carries its own)
population, age structure, sex ratio, gender identity, education distribution, stratification (Elite/Middle/Underclass),
SES, wealth (as income/cost/assets — §5), employment, healthcare access + health vitals (§4), crime, contentment,
housing, substance use, dependency ratio, ideology, standing, drug burden, birth/migration, fight/flight.

### Region-level shared "stage" (one value, all cohorts read it)
biome fertility, geographic area + food capacity (§6), roads, conflict, sector mix, faction rigidity, slavery stance,
pollution, development level + sanitation (§7). These are properties of the place/economy, not of a people.

---

## 2. The influence graph (per cohort)
Each mutable factor is a node. Its **target** = seeded baseline + Σ(weight × source level) through an optional
curve (Linear | Saturating | Threshold) and lag; its **current** value chases the target with **hysteresis**:

```
gap    = target − current
moving = moving ? |gap| > releaseBand : |gap| > deadband     (harder to start than to stop)
v      = inertia·v + velocity·gap
step   = moving ? clamp(v, ±maxStepPerYear) : 0
current += step
```
Distributions push the named tier and take proportionally from the rest, staying normalised. The graph steps once
per **demographic year**. Each cohort runs its own instance, reading its own factors, the shared region stage, and
cross-cohort terms (surrounding ideology → standing; a dominant group → a minority's suppression).

### Factor taxonomy
- **Stock** (immutable per person): population, xenotype, sex, age. No inbound edges; moved only by Flows.
- **Flow** (rates on stocks): birth, mortality, net migration, combat losses. **Birth is the only door from ideology
  into genes/age** (the one-way valve).
- **Fast mutable** (velocity ~0.4–0.6): employment, sector, urbanisation, wealth, SES, crime, contentment, housing.
- **Slow mutable** (velocity ~0.04–0.08, high inertia, wide deadband): education, ideology, stratification. Ideology
  is self-reinforcing via surrounding share.
- **Context** (source only, no level): biome, faction character, faction pressure, residual ghost, roads, conflict,
  outposts, player-colony pull, pollution, resource pools. Patches add their own with `PatchOperationAdd`.
- **Derived** (no state; recomputed each step): Balance, DrugBurdenAgg, MigrationAgg, food self-sufficiency,
  inequality, dependency ratio, the health vitals, and the region aggregates.

### Two graph primitives beyond plain stress
- **Ceiling** (`mode=Ceiling`): bounds a cumulative tier share and spills the excess down. Used for the slavery
  education cap (§3).
- **Suppress** (`mode=Suppress`): multiplies a cohort's own inbound edges by a factor from a source. Used for
  ideological xenotype standing damping births/education (§3).

Lives as an in-game, patchable, **live-reloadable** XML document: `Defs/DemographicFactorDefs/…` (loader + reload
debug action). Template today under `Design/DemographicFactorDefs/`.

---

## 3. Stratification & the three suppression mechanisms (distinct, compounding)
Class structure is state, not a scalar: `Strata` = {Elite, Middle, Underclass} per cohort, slow. `Balance` =
Middle × skilled-education share is the growth-capacity read the economy consumes. Three **distinct** mechanisms
shape it, never generalised into one:

1. **Genetic drug dependency** (chemical-dependency gene, e.g. Hussar/Waster): income diverted to the drug (wealth
   drain), and existential — coerces employment, resists out-migration, drives fight-not-flight.
2. **Ideological xenotype standing** (from surrounding ideology's preferred/disliked xenotypes; contextual, so
   moving relieves it): a disliked cohort has its own edges **suppressed**, sharpest on birth (down to ~a floor),
   also education/employment. This is "repression" = related factors don't apply as heavily for that group.
3. **Slavery** (binary abhorrent/accepted, from the ideology precept — the game makes no finer distinction): drives
   only the **education Ceiling** (enslaved share held at Primary). Per cohort: `slaveShare` = f(slavery stance,
   standing, drug burden); the region reports both "% of a xenotype enslaved" and "who the slaves are".

Fight-vs-flight is **derived, emergent** (never an archetype constant): fight ← drug dependency + Public/Military
sector + standing + Elite; flight ← −standing + wealth − drug + youth + conflict. On a conquest shock the split of
who dies vs flees is computed from these, so adding an industry marker changes the outcome.

---

## 4. Health = real vital statistics (not an index)
There is no opaque "health %". A cohort's **healthcare access** (region medicine × the cohort's income/standing —
the elite get better care) plus food and environment produce three real indicators from **one mortality
decomposition** — mortality is a sum of cause-specific annual hazards:

- **Life expectancy** = genetic lifespan pulled toward by access, cut by exogenous hazards.
- **Infant mortality** = the age-0 slice (medicine, nutrition, genetic fragility).
- **Leading cause of death** = argmax of the hazards: old age (scales inverse to genetic lifespan, so a
  near-immortal never dies of it), malnutrition, disease (cut by sanitation §7, raised by pollution), violence
  (the cohort's crime), war (conflict), addiction (drug burden + substance use §8), xenophobia (low standing).

Mortality and effective births (× (1 − infant mortality)) feed the population stock. Feeds #28 pawn-gen: a pawn's
home region carries a story — what its people die of.

---

## 5. Wealth = income (from a source) − cost of living → assets, per cohort (silver)
Not one scalar. Three quantities in silver:
- **Income** flows from the **economic sector** the cohort's labour sits in (the seam to `EconomicSectorDef` — what a
  region produces is what its people earn), scaled by education (laborTier match) and employment; slaves earn little.
- **Cost of living** = subsistence + an **expectations treadmill that rises with regional prosperity** (RimWorld's
  wealth-expectations mechanic) + education taste + a **role premium** (royalty/clergy/civil-servants — keyed to the
  Royalty title / Public sector / Elite strata, NOT raw standing) + the drug-dependency cost. No debt (RimWorld has
  none).
- **Assets** = accumulated income − cost, floored at 0. Net worth; compounds.

Emergent: **relative poverty** (a low earner in a prosperous region can't afford the lifestyle and dissaves to 0) and
**widening inequality** (positive-net cohorts compound while zero-net never start). SES tiers classify from assets +
net; region wealth is the aggregate.

---

## 6. Geographic scale → density, carrying capacity, food self-sufficiency
The mod reads `planetCoverage` and `grid.TilesCount` but only counts tiles. This model gives tiles a physical size,
**decoupled**: total tiles come from the real world; **per-tile scale is a tunable constant, default ≈4 km²/tile**
(≈2 km across — the scale at which the game's ~450-per-tile cap can feed itself). RimWorld's "planet" framing is not
physically self-consistent, so we pick a playable scale rather than Earth-sized (absurd density) or the rendered map
(too small).
- Region area = tiles × km²/tile. **Food capacity** = area × arable-fraction(biome fertility) × people-per-arable-km²
  (rises with education). Replaces the constant carrying-capacity **K** for #36; population saturates at what the land
  (plus road/trade imports) can feed. Answers #54 region-size and gives **population density** for free.

---

## 7. The added societal indicators (grounded in OECD Better Life Index + Social Progress Index)
- **Contentment / mood** (life satisfaction — the headline quality-of-life read): per cohort from lifestyle
  affordability + health + housing + freedom + standing − pollution − crime. Gates crime and out-migration.
- **Housing / shelter**: per cohort from income + region development level; slave-floored. A wealth sink / construction
  demand.
- **Pollution / environmental quality**: region context (Biotech pollution etc.); raises disease, lowers contentment.
- **Sanitation / water**: a region **development level 0–5** (from urbanisation + wealth) sets treatment **reach**
  (L0/1 untreated well → L2 settlement core → L3 radius 1 → L4 radius 3 → L5 whole region); **wealth = purity**;
  sanitation = coverage × purity, cutting the disease hazard. Inferred from development + wealth — no dependency on
  Dubs Hygiene, though that mod motivated it.
- **Substance use** (non-genetic addiction): per cohort from low contentment + unemployment + drug availability;
  **feeds crime (fund the habit) AND the addiction death hazard** — the link between crime and health.
- **Dependency ratio**: children + elders per working-age adult (drives economic productivity). From age structure.
- **Folded** (not separate axes): recreation → contentment; personal freedom → 1 − slavery − repression;
  inclusiveness → standing. **Skipped** (no real RimWorld mechanic): civic engagement, social connection, info access.

---

## 8. Reproduction & inheritance (RimWorld-accurate)
Children inherit a **blend of both parents' germline genes** (not a dominant-parent pick). Germline xenotypes breed
true; **xenogene/implanted xenotypes (e.g. Sanguophage) are NOT inherited** — their children are baseliner/hybrid.
A cross-germline pairing yields a **Hybrid** cohort. Read `XenotypeDef.inheritable` for which is which (covers modded
xenotypes). Births are assigned to child cohorts by a mating model: **endogamy** (rises with xenophobia → groups stay
separate) vs **exogamy** (partner by population share). Hybrids carry a standing penalty ("stress the children") when
the ideology disapproves of mixing. **Per-faction xenotype acceptance drifts over time** (familiarity: a common
xenotype grows more accepted → reproduces more).

Validated in sim over 100 yr: sanguophages fade toward 0 (can't breed true), hybrids emerge, a hated + drug-dependent
+ enslaved cohort goes extinct, and accepted majorities consolidate.

---

## 9. The hooks (the seams to consumers)
- **Inbound — `SettlementActor` (the one real code hook):** a settlement (player colony / outpost / NPC base) presents
  {headcount, wealth, education, sectorOutput, ideo, xenotypes}; Core folds it into the containing region's factor
  **targets**, population-weighted by the settlement's share. The player colony is read live (in Core); outposts/NPC
  bases are the same interface driven by a patch (#35). The colony's own tile reads its real values; neighbours get a
  distance-falloff pull. **Player-colony representative population scales by difficulty** (hardest = 1:1, each easier
  step +≥1, since the rendered map is small and the world-tile cap ~450). Widens #36; #35 gets its mechanism.
- **Outbound — pawn-generation (#28):** a public endpoint hands a consumer a tile/faction's cohort profile so
  generated pawns inherit their home region's demographics (education → skills, SES → gear). No-op without a consumer;
  never rewrites colony pawns.
- **Economy extensibility — `EconomicSectorRegistry`** (mirrors #55 `FactionPlacementDefaults`): CPs register sectors +
  gates; the `EconomicSectorDef` tree is XML-patchable with `MayRequire`. Rimatomics/Rimfeller etc. = thin CPs, not
  hardcoded. Sector vocabulary + wiring lands in 0.4.0; **pricing/production/consumption flows are 0.5.0** (#7/#31).

Symmetry: #28 is sim→game (region shapes pawns); `SettlementActor` is game→sim (colony/outposts shape region). Together
they close the loop; the inbound hook is the single place concrete player-owned state touches the abstract model.

---

## 10. Conquest / dynamic reshaping
A conquest shock **swaps the region's owning-faction Context** (rigidity, slavery stance, tolerance) to the
conqueror's — it can be a bigger impact, not just relaxation. The elite takes a hit split into died-vs-fled by the
derived fight/flight. A liberal conqueror regrows a middle over decades; a harsher one hollows it further. A later
**nation/region policy hook** adds explicit levers (e.g. capping slaves per free citizen); for now the ideology swap
is the whole mechanism. Driven by the demographic-stress API as a Context input, not a bespoke override.

---

## 11. C# build plan (the feature branch moves Design/ → Defs/ with these)
1. `DemographicFactorDef` + `EconomicSectorDef` loaders with validation (Stock rejects influences; sources/tiers
   resolve; distributions normalise; Ceiling only on distributions; Derived forms a DAG); reload debug action.
2. Pure `InfluenceGraphRules` step (target/hysteresis/velocity/clamp/distribution push/Ceiling/Suppress) + tests.
3. The **cohort** container (top-N + other), per-cohort state scribed; region aggregation.
4. Mechanism modules (each pure + tested): vitals decomposition (§4), wealth income/cost/assets (§5), geographic
   scale (§6), reproduction/inheritance (§8), the added indicators (§7).
5. `SettlementActor` inbound hook + player-colony reader; `EconomicSectorRegistry`; widen #35/#36.
6. Gate everything behind the Societies switch (#53); Developers_Guide for patch authors.
7. Regression guard: all weights zero reproduces the 0.3.2 baseline; no-Biotech = single cohort = old model.

Calibration reference: `Design/sim/` (Node). Known open calibration: cross-indicator balance pass; age is still the
one region-level axis (push per-cohort with the dependency ratio); role-premium keys off role not raw standing.
