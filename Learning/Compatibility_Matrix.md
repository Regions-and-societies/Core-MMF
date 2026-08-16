# Compatibility Matrix

This mod patches world generation, tile ownership, roads, ruins placement and map-mode overlays — all surfaces other world mods also touch. This page records what has actually been run together, and what the results were.

Everything below was observed with all listed mods loaded simultaneously, not inferred.

---

## Verified compatible

**Empire Refactored** (`Matathias.Empire`) — the primary integration target.
- Settlement population is read through the adapter and feeds population density and tiers.
- Production and reward figures are extended rather than replaced.
- Empire settlements are player-founded; this mod does not generate them.

**Vanilla Outposts Expanded** (`vanillaexpanded.outposts`)
- Outposts are recognised and count toward territorial claims.
- Outposts are player-founded from a caravan. Neither this mod nor VOE places them at world generation, so a freshly generated world has none — that is correct, not a fault.

**Vanilla Expanded Framework** (`OskarPotocki.VanillaFactionsExpanded.Core`)
- Contributes exactly one world object of its own, a moving base, which is classified as a caravan. A base that moves cannot hold a province stably.
- Note that VOE now ships inside this framework, so its outpost types arrive from VEF's assembly while belonging to the VOE profile.

**World Domination 2.0** (`TSA.WorldDominationExperimental`)
- Outposts and travelling parties are recognised. Travellers are classified as caravans — an in-flight raid is not a territory-holding settlement.
- Settlement grade is encoded in def names rather than a numeric field, so no level ladder is read for this mod.

**Map Mode Framework** (`nozome.mapmodeframework`) — a hard dependency, not merely compatible. Overlays do not draw without it.

---

## Load order

**The old RimSynapse - Factions load-order rule no longer applies**: Factions bound the former assembly name and does not bind this mod.

This is an ordering constraint, not an incompatibility. Reversed, the Factions assembly cannot resolve this one and **every type in it silently disappears** — its patches never bind, its worldgen step never runs, and nothing in the log says the mod is dead beyond a handful of "could not find a type named" lines. The mod appears installed and does nothing.

RimWorld obeys the order written in your mod list. A declared dependency is advisory; it does not reorder anything for you. If you sort your mod list alphabetically, check this pair afterwards.

RimSynapse Core reports this at startup if it happens, above the resulting wall of errors.

No ordering constraint has been observed against Empire, VOE, VEF, World Domination or Map Mode Framework.

---

## Known limits

- **A new colony is required.** See [Save Compatibility](Save_Compatibility).
- **The demographic component of ownership contributes nothing in 0.7.** It is wired but returns zero, and returns properly in 0.8 reading real regional demographics.
- **Broader modlist coverage is not yet characterised.** The mods above are what has been run and verified together. Other world mods are not known to conflict — they are simply untested, which is a different statement.

---

## Reporting a conflict

Useful reports include the full mod list in load order, the `Player.log` from the run, and whether the problem survives moving this mod earlier or later in the list. Ordering problems and genuine incompatibilities look identical from the outside, and that one detail separates them.
