using System;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>The cause a cohort most dies of — the argmax of the mortality hazards (DEMOGRAPHIC_MODEL §4).</summary>
    public enum DeathCause { OldAge, Malnutrition, Disease, Violence, War, Addiction, Xenophobia }

    /// <summary>The seven cause-specific annual mortality hazards a cohort faces (DEMOGRAPHIC_MODEL §4).
    /// Mortality is their sum; the leading cause is the largest.</summary>
    public struct CauseHazards
    {
        public float oldAge, malnutrition, disease, violence, war, addiction, xenophobia;

        /// <summary>Everything except old age — the hazards that cut a life short of its genetic span.</summary>
        public float Exogenous => malnutrition + disease + violence + war + addiction + xenophobia;
        /// <summary>Total annual mortality hazard.</summary>
        public float Total => oldAge + Exogenous;
    }

    /// <summary>The vital statistics that fall out of the mortality decomposition (DEMOGRAPHIC_MODEL §4).</summary>
    public struct VitalStatistics
    {
        public int lifeExpectancy;          // years
        public int infantMortalityPer1000;  // deaths per 1000 live births
        public DeathCause leadingCause;
        public float mortalityHazard;        // annual, the hazard sum
        public CauseHazards hazards;
    }

    /// <summary>
    /// Health as <b>real vital statistics, not an opaque index</b> (DEMOGRAPHIC_MODEL §4). A cohort's
    /// healthcare access (region medicine × its own income — the elite get better care), plus food and
    /// environment, produce three grounded indicators from <b>one mortality decomposition</b>: mortality is
    /// the sum of cause-specific annual hazards, and life expectancy, infant mortality and the leading cause
    /// of death all read off that same sum.
    ///
    /// <para>Old age scales inverse to the cohort's genetic lifespan, so a near-immortal never dies of it;
    /// disease is cut by sanitation and raised by pollution/urbanisation; addiction folds genetic dependency
    /// and non-genetic use. Ported from the calibration sim so its constants carry over. Pure — standing
    /// arrives as -1..1 (a hated cohort, negative, faces a xenophobia hazard).</para>
    /// </summary>
    public static class VitalsRules
    {
        /// <summary>Region medicine, 0..1: wealth buys it, education and a services sector staff it.</summary>
        public static float RegionMedicine(float wealth, float eduUndergradShare, float sectorServices)
        {
            return Clamp01(0.25f + 0.75f * wealth + 0.4f * eduUndergradShare + 0.3f * sectorServices);
        }

        /// <summary>A cohort's healthcare access: region medicine lifted by its own income — the elite get
        /// better care. Income is silver/day.</summary>
        public static float HealthcareAccess(float regionMedicine, float income)
        {
            return Clamp01(regionMedicine * (0.8f + 0.25f * Clamp01(income / 6f)));
        }

        /// <summary>
        /// The seven cause-specific annual hazards. <paramref name="env"/> is healthcare access,
        /// <paramref name="foodSelfSuff"/> is 0..1 with 0.5 = break-even (below it, malnutrition bites),
        /// <paramref name="standing"/> is -1..1.
        /// </summary>
        public static CauseHazards Hazards(float env, float lifespan, float foodSelfSuff, float urbanisation,
            float sanitation, float pollution, float crime, float conflict, float drugBurden, float substanceUse, float standing)
        {
            if (lifespan <= 0f) lifespan = 80f;
            float hunger = Clamp(0.5f - foodSelfSuff, 0f, 0.5f) / 0.5f;
            return new CauseHazards
            {
                oldAge = 0.011f * (80f / lifespan),                        // inverse to genetic lifespan
                malnutrition = 0.10f * hunger,
                disease = 0.032f * (1f - env) * (1f + 0.5f * urbanisation) * (1f - 0.6f * sanitation) * (1f + 0.8f * pollution),
                violence = 0.05f * crime,
                war = 0.06f * conflict,
                addiction = 0.06f * (drugBurden + 0.5f * substanceUse) * (1f - env),
                xenophobia = 0.06f * Math.Max(0f, -standing),
            };
        }

        /// <summary>Genetic lifespan pulled toward by healthcare access, then cut by the exogenous hazards.
        /// Floored at 15 years.</summary>
        public static int LifeExpectancy(float lifespan, float env, float exogenousHazard)
        {
            if (lifespan <= 0f) lifespan = 80f;
            float healthy = lifespan * (0.55f + 0.45f * env);
            float cut = 1f - Clamp(exogenousHazard * 4f, 0f, 0.78f);
            int le = (int)Math.Round(healthy * cut, MidpointRounding.AwayFromZero);
            return le < 15 ? 15 : le;
        }

        /// <summary>Infant mortality per 1000 live births: the age-0 slice of medicine, nutrition and the
        /// cohort's genetic fragility. <paramref name="foodSelfSuff"/> is 0..1 (0.5 = break-even).</summary>
        public static int InfantMortalityPer1000(float env, float foodSelfSuff, float fragility)
        {
            float hunger = Clamp(0.5f - foodSelfSuff, 0f, 0.5f) / 0.5f;
            float rate = Clamp(0.02f + 0.25f * (1f - env) + 0.30f * hunger + fragility, 0.004f, 0.45f);
            return (int)Math.Round(1000f * rate, MidpointRounding.AwayFromZero);
        }

        /// <summary>The cause a cohort most dies of: the largest hazard (ties resolve in enum order).</summary>
        public static DeathCause LeadingCause(CauseHazards h)
        {
            DeathCause cause = DeathCause.OldAge;
            float best = h.oldAge;
            Consider(ref cause, ref best, DeathCause.Malnutrition, h.malnutrition);
            Consider(ref cause, ref best, DeathCause.Disease, h.disease);
            Consider(ref cause, ref best, DeathCause.Violence, h.violence);
            Consider(ref cause, ref best, DeathCause.War, h.war);
            Consider(ref cause, ref best, DeathCause.Addiction, h.addiction);
            Consider(ref cause, ref best, DeathCause.Xenophobia, h.xenophobia);
            return cause;
        }

        private static void Consider(ref DeathCause cause, ref float best, DeathCause c, float v)
        {
            if (v > best) { best = v; cause = c; }
        }

        /// <summary>The full vital picture from one hazard decomposition.</summary>
        public static VitalStatistics Compute(float env, float lifespan, float foodSelfSuff, float urbanisation,
            float sanitation, float pollution, float crime, float conflict, float drugBurden, float substanceUse,
            float standing, float fragility)
        {
            CauseHazards h = Hazards(env, lifespan, foodSelfSuff, urbanisation, sanitation, pollution, crime, conflict, drugBurden, substanceUse, standing);
            return new VitalStatistics
            {
                hazards = h,
                mortalityHazard = h.Total,
                lifeExpectancy = LifeExpectancy(lifespan, env, h.Exogenous),
                infantMortalityPer1000 = InfantMortalityPer1000(env, foodSelfSuff, fragility),
                leadingCause = LeadingCause(h),
            };
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
