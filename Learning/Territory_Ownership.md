# Territory Ownership

Who holds a province is a **score**, not a flag. This page explains how that score is built and what the resulting states mean.

---

## The three states

- **Held** — a faction has cleared the ownership threshold and no rival is close behind it.
- **Contested** — two or more factions have cleared the threshold and their scores are within the contest margin of each other. The province shows as contested rather than belonging to the leader.
- **Unclaimed** — nobody cleared the threshold. This is a real outcome, not a gap in the model: a province with a lone trading post in the corner is genuinely not anybody's territory.

Every province also carries an **unclaimed share** — the portion of it no faction has accounted for. In a sparsely settled region that number should be substantial, and the province map mode shows it.

---

## What contributes to a faction's score

Several independent components, each a share of the whole rather than a flat bonus:

- **Primary holdings** — settlements, plus military installations at reduced weight. A faction with two of the four settlements in a province takes half of this component, not all of it.
- **Perimeter coverage** — how much of the province edge sits nearest to that faction's holdings. Territory is about reach, not just presence.
- **External perimeter** — a bonus to whichever faction dominates the province's outward-facing edge.
- **Secondary holdings** — outposts, plus camps at reduced weight, with a bonus to whoever has most.
- **Demographics** — **contributes nothing in 0.7.** See below.

Because each component is a share, the totals across all factions in a province cannot exceed the whole, and what is left over is the unclaimed share.

---

## Demographics is switched off in 0.7

This component is meant to express what proportion of a region's people are a given faction's. It did not do that.

Underneath the real path sat a fallback that awarded the component's **full weight** for simply owning a settlement in the province — which the primary-holdings component already measures. The same fact was counted twice, the second time under a name implying something entirely different, and the fallback fired on most installs.

It now contributes zero, and the fallback is removed rather than left dormant. In 0.8 it returns properly, reading a real regional distribution of belief.

The practical effect in 0.7 is that ownership is scored only on things this release actually models, and some provinces that would previously have read as held now read as unclaimed or contested. That is the intended correction.

---

## A known limitation

**A faction with a single settlement and no rivals can still hold an entire province.** The perimeter components award their full value to whoever is the only object present — being unopposed reads to the model as having reach.

This is being fixed. The intended model is that a settlement is a **strong claim** and an outpost a **weak** one, and that a faction which has not invested in a region should not fully hold it just because nobody contested it. Until that lands, expect sparse worlds to look more fully claimed than they should.

---

## Why this is one calculation

Placement, expansion and the world inspect pane all read the **same** ownership answer. That is deliberate: when three systems each compute "who owns this" separately, they eventually disagree, and the player sees a tile refused for belonging to a faction the inspect pane says does not hold it.
