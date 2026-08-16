# World Object Integration

This mod has to reason about settlements, outposts, camps and military installations that belong to **other mods**, without hardcoding which mods exist. That is what the integration layer does.

---

## The problem it solves

Before this layer, territory rules matched on type names with string comparisons scattered through the code. Every new world mod meant another special case, and a mod nobody had special-cased was invisible to the territory system.

Now there is one classifier. Every world object is resolved to a **kind** — settlement, outpost, camp, military installation, caravan, site, ignored, or unknown — and the rules only ever ask about kinds.

---

## Adapter profiles

Each supported mod is described by a **profile** rather than by code: its package ID, the types it contributes, which members hold population and level, and a priority. Profiles are data, so adding support for a mod is a description rather than a new branch.

Profiles ship for Empire Refactored, Vanilla Outposts Expanded, Vanilla Expanded Framework and World Domination. A vanilla adapter handles the base game.

If a profile's mod is not installed, that profile sits quietly and classifies nothing.

---

## Why the profiles are checked against the real game

A profile names foreign types and members as **strings**, and a wrong name costs nothing at runtime: the adapter simply returns its default and the caller reads a plausible zero. Nothing throws, nothing logs, and the number looks reasonable.

That is not hypothetical. **Three of the four shipped profiles were wrong** before this was checked:

- The Empire profile named three population members, none of which exist. Every Empire settlement reported a population of zero, always. What Empire actually publishes is a worker count.
- The Vanilla Expanded Framework profile named types from an assembly that had been **renamed** — VFECore became VEF — so that adapter had been completely inert for as long as it had existed, and nothing said so.
- The World Domination profile was written before the mod was installed and was wrong in every particular: namespace, both marker types, the author, and its dependencies.

There is now a test that reflects against the **live assemblies** on every run and fails if a profile names something that is not there. A profile whose mod is active but whose markers resolve to nothing is treated as an error, not as "not installed".

The lesson generalises: in a layer designed to fail silently, correctness has to be asserted, because nothing else will notice.

---

## Rules are matched carefully

A profile's type rules are matched in priority order and the first non-unknown answer wins. Rules are **not** scoped to the mod that declared them, which means an overly broad rule could claim another mod's objects.

This was real. The VEF profile once carried four broad substring rules — matching anything containing "Settlement", "Camp", "Outpost" or "Base". Because it sorts before World Domination, it would have classified two of that mod's **travelling parties** as territory-holding settlements. The only reason it never happened is that VEF's markers were stale for an unrelated reason.

Profiles now use narrow, exact rules wherever possible, and a test asserts that every world object is classified by its own mod's profile. Legitimate cross-assembly cases — VOE's types arriving from VEF's assembly — are recorded as data rather than allowed to weaken the check.

---

## Adding support for another mod

The short version: describe the mod rather than special-casing it.

- Give the profile the mod's package ID and the types it actually contributes.
- **Read the real type and member names off the loaded assembly.** Do not write them from documentation or expectation — that is how three of four profiles shipped wrong.
- Prefer exact type matches over substring matches. A substring rule is global and will eventually claim something it should not.
- Set a priority that does not place broad rules ahead of more specific ones.
- Declare only population and level members that genuinely exist. Declaring none is a truthful statement that the mod publishes no headcount; declaring names that do not resolve is not.
