# Regions and Territories Overview

This mod adds a world-map layer with three jobs: divide the planet into **provinces**, decide **who holds** each one, and govern **where new world objects may stand**.

---

## Geographic provinces

At world generation the planet is divided into contiguous geographic provinces, built from terrain rather than drawn arbitrarily — biome, elevation, rivers and coastline all shape where one region ends and the next begins. A generated world typically produces somewhere between 250 and 400 provinces, varying with planet size and seed.

A province is the unit everything else in this mod reasons about. It holds its tiles, the world objects standing on them, its resource pools, and the ownership picture described below.

---

## Who holds a region

Ownership is not a flag. Each faction present in a province is **scored**, and a faction is treated as holding territory there once it clears the ownership threshold. Two factions can both clear it, in which case the province is **contested** rather than owned.

Scores come from several independent components — the settlements and military installations present, how much of the province perimeter each faction sits nearest to, and the outposts and camps supporting them. A province where nobody clears the threshold stays **unclaimed**, and unclaimed is a real state rather than a rounding artefact.

See [Territory Ownership](Territory_Ownership) for the detail.

---

## Placement governance

One evaluator decides whether a world object may stand on a given tile, and every placement path routes through it — settling, outpost building, and worldgen placement all ask the same question so they cannot disagree about the same tile.

The rules cover:

- **Buffer distance** between permanent holdings, so settlements do not stack on top of each other.
- **Foreign territory**, so you are not quietly founding inside somebody else's claim.
- **Supply range**, so holdings stay within reach of what supports them.
- **Sequential expansion**, so territory grows outward rather than appearing in disconnected patches.

When a tile is refused, the world inspect pane tells you **why**. A refusal without a reason is a bug; please report one if you see it.

Camps are deliberately exempt from the separation rule — an expeditionary camp pitched beside a settlement is the point of a camp, not a mistake.

---

## Residency

Pawns generated into dwellings within a province are marked as **residents** of it. When RimSynapse Core is installed, this mod registers a residency provider with it, so other RimSynapse mods can ask "does this pawn live here?" and get an authoritative answer without depending on this mod directly.

With Core absent, residency still works internally; nothing is registered and nothing breaks.

---

## What this mod does not do

The boundary matters, because it is what keeps the suite from turning into one large mod:

- **This mod says what a world object is, and where it may stand.**
- **RimSynapse - Factions says what the faction holding it extracts, taxes, defends, and looks like from outside** — regional economy, settlement sizing, military reach and published standing all live there.

If you want the faction simulation, install Factions as well, and load it *after* this mod.
