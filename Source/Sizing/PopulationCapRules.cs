using System;

namespace RegionsAndSocieties.Sizing
{
    /// <summary>
    /// How large a settlement may grow, and the size it drifts toward, as a function of its tier
    /// (0.8; rescaled onto the district model in #30). The cap <b>is the district model's own crowded
    /// ceiling</b>: <see cref="DistrictRules.MaxPopulationForTier"/> — the tier's comfortable
    /// population (100 / 700 / 1,900 / 3,700 / 6,100) times the birth-stagnation ratio (1.5) — scaled
    /// by a player multiplier (now a knob around 1.0) and a tech-level factor. At the default ×1 with
    /// an industrial faction (factor 1): a hamlet caps at 1,050, a city at 9,150.
    ///
    /// <para>The desired size is two-thirds of the cap. Because the district ceiling is 1.5× the
    /// comfortable population, two-thirds of it lands exactly back on that comfortable figure — so a
    /// settlement drifts toward its nominal district population (a city toward 6,100) and crowds up to
    /// 1.5× before births stop. The modeled population steps toward the target with a dead-band so it
    /// settles rather than oscillates (hysteresis). Pure: it works on plain numbers, so the tech
    /// factor and the current population arrive as arguments and the whole thing is testable without a
    /// game. For the player this only informs R&amp;T's model — it never adds or removes real colonists.</para>
    /// </summary>
    public static class PopulationCapRules
    {
        /// <summary>
        /// Default cap multiplier: the district ceiling × this. Player-tunable via a mod-menu slider.
        /// 1.0 (#30; was 30 on the old sub-district scale): the real scale now lives in
        /// <see cref="DistrictRules"/>, so the multiplier is a plain "denser/sparser world" knob around
        /// 1 rather than the number that sets the scale.
        /// </summary>
        public const float DefaultMultiplier = 1f;

        /// <summary>
        /// The population a settlement starts modelling from.
        ///
        /// <para><b>A non-positive <paramref name="targetCapacity"/> means "no tier-imposed cap", not
        /// "room for nobody"</b> — the contract <see cref="MaxPopulation"/> documents and every other
        /// caller honours. An untiered settlement (which is every settlement when the settlement-tier
        /// feature is off, its default) therefore seeds from <paramref name="fallbackEstimate"/>. Reading
        /// that zero as an empty settlement is what left every NPC settlement with no population at all,
        /// and with it no demographics anywhere on the planet (#71).</para>
        /// </summary>
        public static float SeedPopulation(int targetCapacity, int fallbackEstimate, float seedFraction, float seedFloor)
        {
            float floor = seedFloor < 0f ? 0f : seedFloor;
            if (targetCapacity <= 0)
            {
                float f = fallbackEstimate > 0 ? fallbackEstimate : floor;
                return f < floor ? floor : f;
            }
            float fraction = seedFraction > 0f ? seedFraction : TargetFraction;
            float seed = targetCapacity * fraction;
            return seed < floor ? floor : seed;
        }

        /// <summary>The desired size is this fraction of the cap; the population drifts toward it.</summary>
        public const float TargetFraction = 2f / 3f;

        /// <summary>
        /// Maximum population a settlement of this tier may hold:
        /// <c>DistrictRules.MaxPopulationForTier(tier) × multiplier × techFactor</c>, rounded, never
        /// negative. A tierless holding caps at 0.
        /// </summary>
        public static int MaxPopulation(SettlementTier tier, float multiplier, float techFactor)
        {
            // Homestead (rung 0) is the "no tier-imposed cap" sentinel (#71): a homestead — and every
            // holding when settlement tiers are off, which is the default — must return 0 here so
            // SeedPopulation reads it as "no cap, use the fallback estimate" rather than "room for
            // nobody", which is what emptied the whole demographic field before #71.
            if ((int)tier <= 0 || multiplier <= 0f || techFactor <= 0f) return 0;
            // The scale lives in DistrictRules (the crowded ceiling = comfortable population × 1.5),
            // not here; the multiplier is a player knob around 1 (#30). Hamlet 1,050 .. City 9,150.
            return Mathf_RoundToInt(DistrictRules.MaxPopulationForTier(tier) * multiplier * techFactor);
        }

        /// <summary>The size a settlement of this tier drifts toward: two-thirds of its cap.</summary>
        public static int TargetPopulation(SettlementTier tier, float multiplier, float techFactor)
        {
            return Mathf_RoundToInt(MaxPopulation(tier, multiplier, techFactor) * TargetFraction);
        }

        /// <summary>
        /// One hysteresis step of the modeled population toward <paramref name="target"/>: no change
        /// while within <paramref name="deadBand"/> of the target (so it settles instead of jittering),
        /// otherwise move toward it by at most <paramref name="maxStep"/>. Works in both directions —
        /// a settlement over its target shrinks toward it, one under it grows.
        /// </summary>
        public static int StepToward(int current, int target, int maxStep, int deadBand)
        {
            int diff = target - current;
            if (Math.Abs(diff) <= Math.Max(0, deadBand)) return current;
            int step = Math.Min(Math.Max(1, maxStep), Math.Abs(diff));
            return current + Math.Sign(diff) * step;
        }

        // Local rounding so this file stays free of UnityEngine and compiles in the pure sandbox.
        private static int Mathf_RoundToInt(float v)
        {
            return (int)Math.Round(v, MidpointRounding.AwayFromZero);
        }
    }
}
