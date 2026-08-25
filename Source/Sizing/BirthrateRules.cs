using System;

namespace RegionsAndSocieties.Sizing
{
    /// <summary>
    /// The factor inputs a settlement's net growth is built from (#6). Plain data with neutral
    /// defaults, so the game side fills in only what it can measure and a factor whose source is absent
    /// (a DLC not installed, a system not present) simply contributes nothing. Every field is
    /// independent and <b>additive</b>: the net rate is a sum of fertility and negative-mortality terms.
    /// </summary>
    public struct GrowthInputs
    {
        /// <summary>Share of the population that is fertile-age women (from the region age structure),
        /// 0..1. Drives the fertility term. Defaults to a typical <see cref="BirthrateRules.DefaultFertileFraction"/> via the helper.</summary>
        public float FertileFraction;

        /// <summary>Faction tech level as an int (RimWorld <c>TechLevel</c>: 2 Neolithic .. 5 Spacer).
        /// Drives the mortality term — medicine lowers the death rate, which is what actually powers the
        /// demographic transition.</summary>
        public int TechLevel;

        /// <summary>Development / wealth of the region, 0..1 (from the socioeconomic tier). Higher wealth
        /// suppresses fertility — the late-transition decline. 0 if unknown.</summary>
        public float WealthLevel;

        /// <summary>Ideology natalist bias as a direct additive rate (+ pro-fertility, − anti). 0 when
        /// Ideology is absent or the ideoligion is fertility-neutral.</summary>
        public float IdeologyBias;

        /// <summary>Xenotype reproduction bias as a direct additive rate (e.g. sanguophages that do not
        /// breed contribute negative; fast-breeding xenotypes positive). 0 without Biotech / baseliner.</summary>
        public float XenotypeBias;

        /// <summary>Food adequacy, 1 = fully fed, &lt;1 = shortfall (from the region resource model, #7).
        /// A shortfall adds famine mortality. Defaults to fed (1).</summary>
        public float FoodBalance;

        /// <summary>Recent annual fractional loss from war/raids (from <c>DemographicHooks</c>), added as
        /// mortality. 0 when nothing is fighting.</summary>
        public float WarLossRate;
    }

    /// <summary>
    /// Birthrate-informed growth of a settlement's modeled population (#6), as an <b>additive factor
    /// model</b>. Net annual growth is a sum of independent terms — fertility (age structure), minus
    /// mortality (tech/medicine, famine, war), minus the wealth-driven fertility decline, plus cultural
    /// (ideology) and biological (xenotype) biases — each of which is neutral when its data is missing,
    /// so the model degrades gracefully (no DLC ⇒ that factor is simply absent). The net rate then
    /// drives a logistic drift toward the settlement's target (⅔ of cap): fast mid-range, tapering as it
    /// fills, and — because fertility and mortality are separate terms — reproducing the real
    /// demographic-transition hump (pre-industrial net ≈ 0, industrializing peaks, post-industrial
    /// falls, below-replacement declines).
    ///
    /// <para>Pure — plain numbers in and out, no game state, no Unity — so it is unit-tested without a
    /// game and the same call drives both the live tick and the debug fast-forward. For the player it
    /// only informs R&amp;T's model; it never adds or removes real colonists (that gate lives in the
    /// caller).</para>
    /// </summary>
    public static class BirthrateRules
    {
        /// <summary>A modeled settlement holds at least this population, so logistic growth can start
        /// from a fresh (near-zero) settlement instead of being stuck at zero.</summary>
        public const float SeedFloor = 1f;

        /// <summary>Typical fertile-age-women share when the age structure is unknown.</summary>
        public const float DefaultFertileFraction = 0.25f;

        // Tuning constants (annual rates). Rates are scaled well ABOVE real-world demography for game
        // pacing (#6): a settlement should visibly fill within a playthrough, so a healthy town grows
        // ~10-15%/yr (doubling every ~5-7 years), not the real ~1-2%. The fertility/mortality SPLIT is
        // kept so the transition SHAPE still reads — industrializing grows fastest, wealth softens it —
        // and famine/war can still push a settlement into decline; the ends stay modestly positive
        // rather than the realistic near-zero, because a stagnant settlement reads as broken in game.
        private const float BirthsPerFertileWomanYear = 0.68f;   // fertility scale (~17%/yr at a 0.25 fertile share)
        private const float WealthFertilityPenaltyMax = 0.080f;  // full-wealth fertility suppression
        private const float FamineMortalityMax = 0.180f;         // total starvation death rate
        private const float NetRateFloor = -0.12f;               // clamp for a collapsing population
        private const float NetRateCeil = 0.25f;                 // clamp for a boom

        // --- individual additive factors (each pure and independently testable) ---

        /// <summary>Fertility term: births per capita per year, proportional to the fertile-age-women
        /// share. A society with more fertile women (a young pyramid) has a higher birth rate.</summary>
        public static float FertilityRate(float fertileFraction)
        {
            if (fertileFraction < 0f) fertileFraction = 0f;
            return fertileFraction * BirthsPerFertileWomanYear;
        }

        /// <summary>Mortality term: deaths per capita per year by tech level. Medicine lowers it — the
        /// falling death rate that drives the demographic transition. Pre-industrial societies die
        /// nearly as fast as they are born.</summary>
        public static float MortalityRate(int techLevel)
        {
            switch (techLevel)
            {
                case 2: return 0.100f;   // Neolithic
                case 3: return 0.075f;   // Medieval
                case 4: return 0.030f;   // Industrial
                case 5: return 0.025f;   // Spacer
                case 6:
                case 7: return 0.020f;   // Ultra / Archotech
                default: return 0.050f;
            }
        }

        /// <summary>Wealth-driven fertility decline: richer, more developed regions choose fewer
        /// children (the late-transition fall). 0 at subsistence, up to the penalty max at full wealth.</summary>
        public static float WealthFertilityPenalty(float wealthLevel)
        {
            if (wealthLevel <= 0f) return 0f;
            if (wealthLevel > 1f) wealthLevel = 1f;
            return wealthLevel * WealthFertilityPenaltyMax;
        }

        /// <summary>Famine mortality from a food shortfall: 0 when fed (balance ≥ 1), rising to the max
        /// as food runs out (balance → 0). This is the region-can't-feed-its-people pressure (#6/#7).</summary>
        public static float FamineMortality(float foodBalance)
        {
            if (foodBalance >= 1f) return 0f;
            if (foodBalance < 0f) foodBalance = 0f;
            return (1f - foodBalance) * FamineMortalityMax;
        }

        /// <summary>
        /// Net annual growth rate = fertility − mortality, plus the additive biases and minus the
        /// pressures, clamped to a sane band. A missing factor arrives neutral, so it drops out of the
        /// sum. May be negative (a shrinking, below-replacement or starving population).
        /// </summary>
        public static float NetAnnualRate(GrowthInputs g)
        {
            float fertility = FertilityRate(g.FertileFraction) - WealthFertilityPenalty(g.WealthLevel) + g.IdeologyBias + g.XenotypeBias;
            if (fertility < 0f) fertility = 0f;   // biases cannot drive births below zero

            float mortality = MortalityRate(g.TechLevel) + FamineMortality(g.FoodBalance) + Math.Max(0f, g.WarLossRate);

            float net = fertility - mortality;
            if (net < NetRateFloor) net = NetRateFloor;
            if (net > NetRateCeil) net = NetRateCeil;
            return net;
        }

        /// <summary>Convenience net rate from a tech level alone (age unknown, subsistence, fed, no
        /// DLC factors) — the simplest caller / a fallback. Uses the default fertile fraction.</summary>
        public static float AnnualGrowthRate(int techLevel)
        {
            return NetAnnualRate(new GrowthInputs
            {
                FertileFraction = DefaultFertileFraction,
                TechLevel = techLevel,
                FoodBalance = 1f,
            });
        }

        /// <summary>
        /// Advance a modeled population one step over <paramref name="yearsElapsed"/> years at
        /// <paramref name="annualRate"/> (which may be negative), clamped to <c>[0, <paramref name="max"/>]</c>.
        /// A non-negative rate grows logistically toward <paramref name="carryingCapacity"/> without
        /// overshooting it (and shrinks toward it from above — a dropped tier); a negative rate is an
        /// intrinsic decline that decays the population toward zero. Never leaves <c>[0, max]</c>.
        /// </summary>
        public static float GrowStep(float current, int carryingCapacity, int max, float annualRate, float yearsElapsed)
        {
            if (max < 0) max = 0;
            float p = current < 0f ? 0f : current;

            if (carryingCapacity <= 0 || yearsElapsed <= 0f || annualRate == 0f)
            {
                return Clamp(p, 0f, max);
            }

            float k = carryingCapacity;

            if (annualRate > 0f)
            {
                if (p < SeedFloor) p = SeedFloor;   // seed so a near-zero settlement can begin growing
                float dP = annualRate * p * (1f - p / k) * yearsElapsed;
                float next = p + dP;
                // Do not overshoot the capacity in one step, in either direction — settles, no oscillation.
                if (p <= k) { if (next > k) next = k; }
                else { if (next < k) next = k; }
                return Clamp(next, 0f, max);
            }
            else
            {
                // Intrinsic decline (below-replacement / starving): exponential decay toward zero.
                float next = p + annualRate * p * yearsElapsed;
                return Clamp(next, 0f, max);
            }
        }

        private static float Clamp(float v, float lo, float hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
