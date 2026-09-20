using System;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>
    /// The added societal indicators (DEMOGRAPHIC_MODEL §7), grounded in the OECD Better Life / Social
    /// Progress indices: contentment (life satisfaction), housing, sanitation, substance use, freedom,
    /// affordability, development level and the dependency ratio. Each is a small closed form over the
    /// cohort's and region's state; ported from the calibration sim.
    ///
    /// <para>Pure and deterministic. Standing arrives as -1..1 where a raw standing is used (Freedom), and
    /// as 0..1 where the sim already normalised it (Contentment). Sanitation is inferred from development +
    /// wealth — no dependency on any hygiene mod, though one motivated it.</para>
    /// </summary>
    public static class IndicatorsRules
    {
        /// <summary>Region development level, 0–5, from urbanisation and wealth — a settlement-development
        /// tier that sets water-treatment reach and housing quality.</summary>
        public static float DevelopmentLevel(float urbanisation, float wealth)
            => Clamp01(0.4f * urbanisation + 0.6f * wealth) * 5f;

        /// <summary>Water-treatment coverage from the development level: untreated below L1, half at L3,
        /// whole-region at L5.</summary>
        public static float SanitationCoverage(float developmentLevel)
            => Clamp01((developmentLevel - 1f) / 4f);

        /// <summary>Sanitation = treated coverage × purity (wealth) — the share of the disease hazard it
        /// suppresses.</summary>
        public static float Sanitation(float developmentLevel, float wealth)
            => SanitationCoverage(developmentLevel) * Clamp01(wealth);

        /// <summary>A cohort's housing/shelter, 0..1, from its income and the region's development level,
        /// floored by slavery. Income is silver/day.</summary>
        public static float Housing(float income, float developmentLevel, float slaveShare)
            => Clamp01(0.2f + 0.45f * Clamp01(income / 8f) + 0.35f * (developmentLevel / 5f)) * (1f - 0.5f * Clamp01(slaveShare));

        /// <summary>Personal freedom (SPI, folded), 0..1: cut by the enslaved share and by low standing.
        /// Standing is -1..1.</summary>
        public static float Freedom(float slaveShare, float standing)
            => Clamp01(1f - Clamp01(slaveShare) - 0.5f * Math.Max(0f, -standing));

        /// <summary>Can the cohort afford the local lifestyle: 0.5 at break-even, rising with a net surplus
        /// relative to its cost of living.</summary>
        public static float Affordability(float netDaily, float costLiving)
            => Clamp01(0.5f + netDaily / (costLiving < 1f ? 1f : costLiving));

        /// <summary>Contentment / mood (life satisfaction — the headline quality-of-life read): lifestyle
        /// affordability + health + housing + freedom + standing, less pollution and crime. Standing is 0..1.</summary>
        public static float Contentment(float affordability, float healthcareAccess, float housing, float freedom,
            float standing01, float pollution, float crime)
            => Clamp01(0.12f + 0.25f * affordability + 0.20f * healthcareAccess + 0.15f * housing
                     + 0.15f * freedom + 0.10f * standing01 - 0.20f * pollution - 0.15f * crime);

        /// <summary>How easily drugs are had, 0..1: easier in richer, more urban/services regions.</summary>
        public static float DrugAvailability(float wealth, float sectorServices)
            => Clamp01(0.15f + 0.4f * wealth + 0.3f * sectorServices);

        public const float SubstanceUseCap = 0.85f;

        /// <summary>Non-genetic substance use, 0..cap: driven by discontent, unemployment and availability,
        /// damped by freedom. Feeds both crime (funding the habit) and the addiction death hazard.</summary>
        public static float SubstanceUse(float contentment, float employmentRate, float drugAvailability, float freedom)
            => Clamp(0.05f + 0.45f * (1f - contentment) + 0.2f * (1f - employmentRate) + 0.25f * drugAvailability - 0.2f * freedom, 0f, SubstanceUseCap);

        /// <summary>The cohort's approximate age structure — child / working-age / elder shares — from its
        /// birth rate (the young) and life expectancy (the old). A coarse pyramid, enough to aggregate a
        /// region's age profile from its cohorts (#58). Shares sum to 1.</summary>
        public static void AgeStructure(float birthRate, int lifeExpectancy, out float child, out float working, out float elder)
        {
            child = Clamp(birthRate * 6f, 0.10f, 0.45f);
            elder = Clamp((lifeExpectancy - 40f) / 220f, 0.02f, 0.28f);
            working = 1f - child - elder;
            if (working < 0f) working = 0f;
            float sum = child + working + elder;
            if (sum > 0f) { child /= sum; working /= sum; elder /= sum; }
        }

        /// <summary>Dependency ratio: children + elders per working-age adult, from the age structure.
        /// Higher = fewer workers carrying more dependents.</summary>
        public static float DependencyRatio(float birthRate, int lifeExpectancy)
        {
            AgeStructure(birthRate, lifeExpectancy, out float child, out float working, out float elder);
            return (child + elder) / (working < 0.2f ? 0.2f : working);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
