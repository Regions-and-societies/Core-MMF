# World-Object Integration — detected-mods framework (design)

Status: **framework only — none of the capability reporting below is implemented.**
Captured from the 0.4.0 placement-dialog work so the shape is not lost.

## Where it shows

The new-game **Geographic Placement Settings** dialog (advanced view) carries a card titled
**"World Objects to Add During World Generation:"**. Its bottom section is a *detected compatibility
mods* table. Today the table can only report **which** integrations are active (from
`WorldObjectAdapterRegistry`), not **what each contributes**, so with no compatibility patch installed
the dialog renders a dimmed **worked example** of the intended shape and the note:

> Nothing to post here without a compatibility patch. With one installed it would look like:

and, under the example rows:

> *Framework example — not yet implemented.*

## The intended table

One row per detected compatibility patch; one column per class of world object the patch contributes to
world generation. The example columns are illustrative, not final:

| Mod       | Economy | Military bases | Roads |
|-----------|:-------:|:--------------:|:-----:|
| VOE-CP    |   Yes   |       No       |  Yes  |
| Empire-CP |   Yes   |      Yes       |  Yes  |
| VFE-CP    |   No    |      Yes       |  No   |

(The three example rows above are fabricated placeholders — they do **not** reflect what any patch
actually registers. They exist only to show a reader what a populated table would look like.)

## What it would take to make it real

Each compatibility patch already registers an `IWorldObjectAdapter` (read side), an `IHoldingCreator`
(create side), and, as of #18, an `ISeedingPolicy` (how many / where). To populate the capability
columns a patch would additionally need to **declare which world-object classes it seeds** — e.g. an
enum-flags or a small descriptor the patch hands to a registry, which the dialog then reads to fill the
grid. That declaration API does not exist yet; adding it is the follow-up this design records.

Suggested shape (not built):

- A `WorldObjectContribution` descriptor: `{ string ModId; string DisplayName; ContributionKind Kinds; }`
  where `ContributionKind` is `[Flags]` over `Economy | MilitaryBases | Roads | Outposts | Camps | …`.
- A `WorldObjectContributionRegistry` (mirror of the existing registries) each patch registers into from
  its `Mod` constructor.
- The dialog table iterates the registry instead of the hard-coded example rows; the example rows are
  shown only when the registry is empty.

## Rendering notes (current placeholder)

- `Dialog_FactionPlacementSettings.DrawDetectedModsTable` draws the header + example. Real integrations,
  when present, list the mod name in green with `—` in every capability column (present-but-unreported).
- Columns and the worked example are intentionally dim (`GameFont.Tiny`, grey) to read as a mock-up, not
  live data.
