using System;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>Which cohort a pairing's children join (DEMOGRAPHIC_MODEL §8). The game layer maps these to
    /// actual <c>XenotypeDef</c>s; the rule stays pure by naming the outcome abstractly.</summary>
    public enum ChildOutcome
    {
        ParentA,    // the child breeds true to parent A's germline
        ParentB,    // ... or parent B's
        Baseliner,  // neither germline carries (implanted xenotypes) — a baseliner child
        Hybrid,     // two different germlines cross — a hybrid cohort
    }

    /// <summary>
    /// Reproduction &amp; inheritance, RimWorld-accurate (DEMOGRAPHIC_MODEL §8). Germline xenotypes breed true;
    /// <b>xenogene/implanted xenotypes (e.g. Sanguophage) are not inherited</b> — their children are baseliner;
    /// a cross-germline pairing yields a <b>Hybrid</b>. Births are split between within-group (endogamy, which
    /// rises with xenophobia so groups stay separate) and cross-group (exogamy, partnered by population share).
    /// A logistic term caps births at the land's carrying capacity.
    ///
    /// <para>This is the pure rate/inheritance math — <see cref="ChildOutcome"/> is resolved to real defs, and
    /// births are assigned across cohorts, by the (game-coupled) cohort container. Ported from the sim, whose
    /// 100-yr run shows sanguophages fading toward 0, hybrids emerging, and accepted majorities consolidating.</para>
    /// </summary>
    public static class ReproductionRules
    {
        public const float EndogamyBase = 0.55f;
        public const float EndogamyXenophobiaSwing = 0.4f;
        public const float EndogamyFloor = 0.35f, EndogamyCap = 0.95f;

        /// <summary>The share of births that stay within a cohort, from ideological tolerance (-1 hostile .. 1
        /// tolerant): a xenophobic region keeps groups separate (toward the cap), a tolerant one mixes more.</summary>
        public static float Endogamy(float ideoTolerance)
        {
            float tol01 = (Clamp(ideoTolerance, -1f, 1f) + 1f) / 2f;
            return Clamp(EndogamyBase + EndogamyXenophobiaSwing * (1f - tol01), EndogamyFloor, EndogamyCap);
        }

        public const float LogisticFloor = -0.5f;

        /// <summary>The birth-capacity term: 1 with room to spare, 0 at carrying capacity, negative (down to a
        /// floor) when overpopulated. Multiplies gross births.</summary>
        public static float LogisticFactor(float totalPopulation, float carryingCapacity)
        {
            if (carryingCapacity <= 0f) return LogisticFloor;
            return Clamp(1f - totalPopulation / carryingCapacity, LogisticFloor, 1f);
        }

        /// <summary>Gross live births for a cohort in a year: headcount × birth rate × infant survival ×
        /// the logistic room (never negative — an over-capacity region simply has no births, not anti-births).</summary>
        public static float Births(float population, float birthRate, float infantMortalityPer1000, float logisticFactor)
        {
            float survive = 1f - Clamp(infantMortalityPer1000, 0f, 1000f) / 1000f;
            float room = logisticFactor < 0f ? 0f : logisticFactor;
            float b = population * birthRate * survive * room;
            return b < 0f ? 0f : b;
        }

        /// <summary>Deaths for a cohort in a year: headcount × its total mortality hazard.</summary>
        public static float Deaths(float population, float mortalityHazard) => population * (mortalityHazard < 0f ? 0f : mortalityHazard);

        /// <summary>Net migration for a cohort in a year: headcount × net migration rate (may be negative).</summary>
        public static float NetMigration(float population, float netMigrationRate) => population * netMigrationRate;

        /// <summary>
        /// The children of a <b>cross-group</b> pairing of two different germlines/xenotypes (§8):
        /// two implanted xenotypes → baseliner; two heritable germlines → the non-baseliner one if the other
        /// is baseliner, else a hybrid; exactly one heritable → that one breeds true.
        /// </summary>
        public static ChildOutcome ChildOfPairing(bool aHeritable, bool aIsBaseliner, bool bHeritable, bool bIsBaseliner)
        {
            if (!aHeritable && !bHeritable) return ChildOutcome.Baseliner;
            if (aHeritable && bHeritable)
            {
                if (aIsBaseliner) return ChildOutcome.ParentB;   // baseliner × X → X
                if (bIsBaseliner) return ChildOutcome.ParentA;
                return ChildOutcome.Hybrid;                      // two distinct germlines
            }
            return aHeritable ? ChildOutcome.ParentA : ChildOutcome.ParentB;
        }

        /// <summary>The children of a <b>within-group</b> pairing (§8): a heritable germline breeds true; an
        /// implanted xenotype does not, so its within-group children are baseliner (this is why sanguophages
        /// fade toward zero — they cannot reproduce themselves).</summary>
        public static ChildOutcome ChildOfSameGroup(bool heritable) => heritable ? ChildOutcome.ParentA : ChildOutcome.Baseliner;

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
