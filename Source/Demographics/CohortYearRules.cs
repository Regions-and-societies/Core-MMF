using System;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>The shared region "stage" every cohort reads (DEMOGRAPHIC_MODEL §1) — properties of the
    /// place and economy, not of a people. Levels are 0..1 unless noted. Call
    /// <see cref="CohortYearRules.PrepareRegion"/> once per region per year to fill the derived fields.</summary>
    public sealed class RegionStage
    {
        // stepped region factors (the region-level influence graph feeds these; here they are inputs)
        public float wealth, urbanisation, employmentRate, conflict, pollution, roads, biomeFertility;
        public float slaveryStance;   // 0..1
        public float ideoTolerance;   // -1..1
        public float natalism;        // 0..1
        public float ageWorking;      // working-age share of the population
        public float crime;           // region crime aggregate from LAST year (feeds contentment)

        public float[] education = new float[5];   // [Illiterate, Primary, Secondary, Undergrad, Postgrad]
        public float sectorServices, sectorManufacturing, sectorPublic, sectorMilitary;

        public int tiles = 1;
        public float population = 1f;

        // derived by PrepareRegion
        public float developmentLevel, sanitation, skilledEduShare, foodSelfSufficiency;
    }

    /// <summary>One xenotype cohort's state (DEMOGRAPHIC_MODEL §1): its intrinsic Stock (from genes), the
    /// slow-carried values (standing preference, assets), and every derived per-year output. The C# cohort
    /// container (step 3) scribes this and drives births/deaths through <see cref="ReproductionRules"/>.</summary>
    public sealed class CohortState
    {
        // --- intrinsic (Stock: from the xenotype's genes) ---
        public float lifespan = 80f, fragility = 0.02f, fertility = 0.45f, drugBurden = 0f;
        public bool heritable = true, isBaseliner = false, isHybrid = false;
        public float baseInit = 0.1f;        // the cohort's initial ideological preference

        // --- slow-carried between years ---
        public float basePreference = 0.1f;  // drifts toward familiarity
        public float pop, share, assets;

        // --- external influence, set before Step each year (#81) ---
        // A per-xenotype acceptance offset the region applies to this cohort's standing: the player's
        // example (accepting a xenotype as free vs enslaving it) raises or lowers how welcome that xenotype
        // is, spreading region to region. Transient (recomputed each year from the region's acceptance map),
        // so it is not scribed. Zero = no influence.
        public float acceptanceOffset;

        // --- derived each year (outputs) ---
        public float standing, eduIndex, slaveShare, wealthLevel;
        public float[] education = new float[5];
        public float strataElite, strataMiddle, strataUnderclass;
        public IncomeSource incomeSource;
        public float income, costLiving, netDaily;
        public float healthEnv, housing, freedom, contentment, substanceUse, crime;
        public float birthRate, birthMult, fight, flight, migNet;
        public int lifeExpectancy, infantMortalityPer1000;
        public DeathCause leadingCause;
        public float mortalityHazard, dependency, femaleFraction, genderDiverse;
        public float ageChild, ageWorking, ageElder;   // the cohort's age pyramid (shares, sum 1)
    }

    /// <summary>
    /// One demographic year for a single cohort (DEMOGRAPHIC_MODEL §1–§8): composes the six pure mechanism
    /// modules — <see cref="InfluenceGraphRules"/> (the slavery education ceiling), <see cref="WealthRules"/>,
    /// <see cref="VitalsRules"/>, <see cref="GeographicScaleRules"/>, <see cref="IndicatorsRules"/> and (at the
    /// container level) <see cref="ReproductionRules"/> — with the sim's inline social formulas (standing
    /// drift, the education pyramid, stratification, crime, the birth target, fight/flight, migration).
    ///
    /// <para>Pure and deterministic: given a cohort and its region stage it recomputes the cohort's whole
    /// profile for the year. This is the spine the (game-coupled) cohort container wraps — it holds no game
    /// types, so it unit-tests, and the emergent shapes the sim shows fall out of it.</para>
    /// </summary>
    public static class CohortYearRules
    {
        // education tier indices
        private const int Illiterate = 0, Primary = 1, Secondary = 2, Undergrad = 3, Postgrad = 4;

        /// <summary>Fill a region stage's derived fields (development, sanitation, skilled-education share,
        /// food self-sufficiency). Call once per region per year before stepping its cohorts.</summary>
        public static void PrepareRegion(RegionStage r)
        {
            r.developmentLevel = IndicatorsRules.DevelopmentLevel(r.urbanisation, r.wealth);
            r.sanitation = IndicatorsRules.Sanitation(r.developmentLevel, r.wealth);
            r.skilledEduShare = r.education[Secondary] + r.education[Undergrad] + r.education[Postgrad];
            float food = GeographicScaleRules.FoodCapacity(r.tiles, r.biomeFertility, r.skilledEduShare);
            r.foodSelfSufficiency = GeographicScaleRules.FoodSelfSufficiency(food, r.population);
        }

        /// <summary>Advance one cohort one demographic year against its (already-prepared) region stage.</summary>
        public static void Step(CohortState x, RegionStage r)
        {
            // --- standing: familiarity drift, then contextual lift from the surrounding ideology ---
            float familiar = x.baseInit + 0.5f * Clamp(x.share * 2f - 0.2f, -0.3f, 0.5f);
            x.basePreference += 0.05f * (familiar - x.basePreference);
            // #81: the region's acceptance of THIS xenotype (from the player's example, spread region to
            // region) shifts its standing — an accepted caste is more welcome (more births, freer, attracts
            // migrants), a degraded one less.
            x.standing = Clamp(x.basePreference + r.ideoTolerance + x.acceptanceOffset, -1f, 1f);
            float standFac = (x.standing + 1f) / 2f;

            // --- socioeconomics: education index, slave share, stratified wealth level, slave floor ---
            float eduAtt = r.education[Primary] * 0.25f + r.education[Secondary] * 0.5f
                         + r.education[Undergrad] * 0.75f + r.education[Postgrad] * 1f;
            x.eduIndex = Clamp01(eduAtt * (0.55f + 0.45f * standFac));
            x.slaveShare = r.slaveryStance * Clamp(0.05f + 0.60f * Math.Max(0f, -x.standing) + 0.25f * x.drugBurden, 0f, 0.9f);
            x.wealthLevel = Clamp01(r.wealth * (0.45f + 0.55f * standFac) * (1f - 0.55f * x.drugBurden));
            if (x.slaveShare > 0f)
            {
                x.wealthLevel *= 1f - 0.6f * x.slaveShare;
                x.eduIndex *= 1f - 0.5f * x.slaveShare;
            }

            // --- wealth decomposed: income (from a sector) − cost of living → assets (§5) ---
            WealthProfile w = WealthRules.Compute(x.eduIndex, standFac, x.drugBurden, x.slaveShare, r.wealth, r.employmentRate, x.assets);
            x.incomeSource = w.source; x.income = w.income; x.costLiving = w.costLiving; x.netDaily = w.netDaily; x.assets = w.assets;

            // --- education distribution per cohort, then the slavery ceiling (§3) ---
            float access = Clamp01(0.5f + 0.5f * x.standing - 0.7f * x.slaveShare - 0.25f * x.drugBurden);
            ShiftedPyramid(r.education, access, x.education);
            if (x.slaveShare > 0f)
                InfluenceGraphRules.ApplyCeiling(x.education, Secondary, 1f - 0.9f * x.slaveShare, Primary);

            // --- stratification (Elite / Middle / Underclass) ---
            float elite = Clamp(0.04f + 0.28f * Math.Max(0f, x.standing) + 0.20f * x.eduIndex, 0f, 0.6f);
            float under = Clamp(0.18f + 0.50f * Math.Max(0f, -x.standing) + 0.40f * x.slaveShare + 0.25f * x.drugBurden, 0f, 0.85f);
            float middle = Math.Max(0.05f, 1f - elite - under);
            float st = elite + middle + under;
            x.strataElite = elite / st; x.strataMiddle = middle / st; x.strataUnderclass = under / st;

            // --- sex & gender ---
            x.femaleFraction = Clamp(0.50f + 0.15f * r.conflict, 0.30f, 0.70f);
            x.genderDiverse = Clamp(0.05f * Clamp01(0.5f + 0.5f * r.ideoTolerance), 0f, 0.10f);

            // --- health access, housing, freedom, contentment, substance use (§4/§7) ---
            float regionMed = VitalsRules.RegionMedicine(r.wealth, r.education[Undergrad], r.sectorServices);
            x.healthEnv = VitalsRules.HealthcareAccess(regionMed, x.income);
            x.housing = IndicatorsRules.Housing(x.income, r.developmentLevel, x.slaveShare);
            x.freedom = IndicatorsRules.Freedom(x.slaveShare, x.standing);
            float afford = IndicatorsRules.Affordability(x.netDaily, x.costLiving);
            x.contentment = IndicatorsRules.Contentment(afford, x.healthEnv, x.housing, x.freedom, standFac, r.pollution, r.crime);
            float availability = IndicatorsRules.DrugAvailability(r.wealth, r.sectorServices);
            x.substanceUse = IndicatorsRules.SubstanceUse(x.contentment, r.employmentRate, availability, x.freedom);

            // --- crime: fed by underclass, unemployment, drugs, discontent, low standing (§7) ---
            x.crime = Clamp01(0.02f + 0.35f * x.strataUnderclass + 0.25f * (1f - r.employmentRate) + 0.25f * x.drugBurden
                + 0.30f * x.substanceUse + 0.20f * (1f - x.contentment) + 0.30f * Math.Max(0f, -x.standing)
                - 0.30f * x.eduIndex + 0.30f * r.conflict);

            // --- vital statistics: one mortality decomposition (§4) ---
            VitalStatistics v = VitalsRules.Compute(x.healthEnv, x.lifespan, r.foodSelfSufficiency, r.urbanisation,
                r.sanitation, r.pollution, x.crime, r.conflict, x.drugBurden, x.substanceUse, x.standing, x.fragility);
            x.lifeExpectancy = v.lifeExpectancy; x.infantMortalityPer1000 = v.infantMortalityPer1000;
            x.leadingCause = v.leadingCause; x.mortalityHazard = v.mortalityHazard;

            // --- birth rate: age + natalism + fertility − prosperity/education/conflict, standing-suppressed ---
            x.birthMult = Clamp(1f + 0.95f * x.standing, 0.05f, 1f);
            float birth = 0.02f + 0.40f * r.ageWorking + 0.30f * r.natalism + 0.30f * x.fertility
                - 0.20f * InfluenceGraphRules.Curve(InfluenceCurve.Saturating, r.wealth)
                - 0.20f * r.education[Undergrad] - 0.15f * r.conflict + 0.15f * x.strataUnderclass;
            x.birthRate = Clamp(birth, 0f, 0.06f) * x.birthMult;

            // --- fight / flight (derived, emergent) and net migration ---
            x.fight = Clamp(0.6f * x.drugBurden + 0.5f * r.sectorPublic + 0.7f * r.sectorMilitary + 0.3f * x.standing + 0.3f * x.strataElite, 0f, 1.5f);
            x.flight = Clamp(-0.5f * x.standing + 0.3f * r.wealth - 0.5f * x.drugBurden + 0.2f * r.ageWorking + 0.4f * r.conflict + 0.3f * x.crime, 0f, 1.5f);
            float outMig = 0.03f * x.flight * (1f - x.drugBurden);
            float inMig = 0.02f * r.wealth * (x.standing > 0f ? 1f : 0.3f);
            x.migNet = inMig - outMig;

            // --- age structure + dependency ratio (§7) ---
            IndicatorsRules.AgeStructure(x.birthRate, x.lifeExpectancy, out x.ageChild, out x.ageWorking, out x.ageElder);
            x.dependency = IndicatorsRules.DependencyRatio(x.birthRate, x.lifeExpectancy);
        }

        /// <summary>Tilt a base education pyramid up (access &gt; 0.5) or down toward the higher tiers,
        /// renormalised into <paramref name="output"/>. Ported from the sim's shiftedPyramid.</summary>
        public static void ShiftedPyramid(float[] baseDist, float access, float[] output)
        {
            float k = (access - 0.5f) * 4f;   // -2..+2
            float s = 0f;
            for (int i = 0; i < output.Length; i++)
            {
                float b = i < baseDist.Length ? baseDist[i] : 0.01f;
                if (b < 1e-4f) b = 1e-4f;
                float wgt = b * (float)Math.Exp(k * (i - 2) / 2f);
                output[i] = wgt; s += wgt;
            }
            if (s > 0f) for (int i = 0; i < output.Length; i++) output[i] /= s;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
