# Save Compatibility

**Short version: start a new colony.**

---

## Why a new colony

Provinces are built during **world generation**. A world that was generated without this mod has no province data, and there is no way to reconstruct it faithfully afterwards — the terrain-driven division that produces regions happens once, as the planet is made.

Adding this mod to an existing save therefore gives you a world with no regions, which means no territory, no ownership, and no meaningful overlays.

---

## Compatibility mode

Rather than refusing to load such a save, the mod **adopts** it.

When a save contains no province data, strict territorial ownership stands down: placement rules that depend on regions stop applying, so you are not suddenly unable to settle tiles that were legal yesterday. You are told when this happens rather than left to guess.

A save that **does** contain provinces keeps strict rules, unchanged.

Compatibility mode is a safety net, not a supported way to play. You get the mod loaded without breaking your colony; you do not get the features, because the data they need was never generated.

---

## Updating between versions

Within a major version, saves carry over. Across the 0.6 to 0.7 boundary they do not — 0.7 changed how ownership is computed and what is stored on a province.

If you are mid-colony on 0.6 and want to stay there, finish that colony before updating. There is no migration, and none is planned; the effort is better spent on the world layer than on translating a model that changed underneath it.

---

## What to expect after updating an in-progress 0.7 world

Ownership figures shift when the scoring changes between releases. In 0.7 specifically, the demographic component was switched off, so some provinces that previously read as **held** now read as **unclaimed** or **contested**.

That is the intended correction rather than lost data — the old figure was awarding a fifth of the score for something already counted elsewhere. Any system reading ownership, including Empire production and tithe figures, moves with it.
