# Map Modes

This mod ships three world-map overlays, drawn through the **Map Mode Framework**. That framework is a hard dependency — without it the overlays do not draw at all.

Switch between them with the map mode selector on the world view.

---

## Geographic Provinces

Draws the province boundaries themselves: the contiguous regions the planet was divided into at world generation.

The tooltip reports what the region contains — its tiles, the factions scoring in it, and its **unclaimed share**. If you want to understand why a tile was refused for settlement, this is the overlay to check first.

Useful when: you want to see the shape of the world's regions, or work out which province a tile belongs to.

---

## Faction Territory

Colour-codes provinces by the faction holding them, using each faction's own colour.

**Contested provinces are shown as contested**, listing every faction with a claim, rather than being handed to whichever is marginally ahead. A province nobody holds is drawn as unclaimed.

Useful when: you want to see the political shape of the planet, find a frontier, or understand where your own claim ends.

---

## Population Density

A gradient showing where people actually are, propagated outward from settlements through biomes, terrain and roads rather than drawn as flat circles. Mountains and water slow the spread; roads carry it further.

This is the layer that feeds population-derived figures elsewhere in the suite, so if a settlement tier or a density-based number looks wrong, this overlay is where to check the input.

Useful when: choosing where to settle, or working out why one region feels busier than another.

---

## If an overlay is missing or blank

- **Map Mode Framework not installed.** Nothing will draw. It is a requirement, not an optional integration.
- **A blank or uniform overlay on a freshly generated world** usually means worldgen did not complete this mod's steps — check `Player.log` for errors during world generation.
- **Territory looking more fully claimed than expected** is a known limitation of 0.7 rather than a display fault; see [Territory Ownership](Territory_Ownership).
