using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>
    /// Builds a region's <see cref="CohortState"/> roster (DEMOGRAPHIC_MODEL §1, the cohort container's
    /// foundation). The roster is <b>discovered at runtime</b> from the faction's available xenotypes
    /// (reusing <see cref="FactionDemographicProfile"/>), and each cohort's intrinsic Stock — lifespan,
    /// drug dependency, fragility, heritability — is <b>read from the xenotype's own genes</b>, so a modded
    /// xenotype gets sensible values with zero knowledge of it. Kept to the top-N cohorts plus an "other"
    /// bucket per the performance guardrail; most regions are one or two xenotypes.
    ///
    /// <para>Game-coupled (reads defs/genes), so it is held to its shape by the type-check and exercised by
    /// a debug action rather than the pure unit suites. Degrades cleanly: no Biotech → one Baseliner cohort,
    /// which is exactly the old region-level model.</para>
    /// </summary>
    public static class CohortFactory
    {
        /// <summary>How many named cohorts a region tracks before the rest fall into "other".</summary>
        public const int DefaultTopN = 4;

        // intrinsic lifespans by longevity gene (years). Deathless/ageless read as effectively immortal so
        // the old-age hazard vanishes; a longevity gene stretches it; everyone else is the human ~80.
        public const float BaselineLifespan = 80f, LongevityLifespan = 160f, ImmortalLifespan = 2000f;
        public const float DependencyBurden = 0.6f;   // a chemical-dependency caste carries this drug burden
        public const float BaseFragility = 0.02f, FrailFragility = 0.12f;

        /// <summary>The intrinsic lifespan a xenotype's genes imply.</summary>
        public static float LifespanOf(XenotypeDef xeno)
        {
            if (xeno?.genes == null) return BaselineLifespan;
            float best = BaselineLifespan;
            for (int i = 0; i < xeno.genes.Count; i++)
            {
                string n = xeno.genes[i]?.defName;
                if (n == null) continue;
                if (n.IndexOf("Ageless", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Deathless", StringComparison.OrdinalIgnoreCase) >= 0)
                    return ImmortalLifespan;
                if (n.IndexOf("Longevity", StringComparison.OrdinalIgnoreCase) >= 0)
                    best = LongevityLifespan;
            }
            return best;
        }

        /// <summary>The genetic drug burden a xenotype carries: a chemical-dependency gene (Hussar, Waster…)
        /// coerces its labour and drains its wealth.</summary>
        public static float DrugBurdenOf(XenotypeDef xeno)
        {
            if (xeno?.genes == null) return 0f;
            for (int i = 0; i < xeno.genes.Count; i++)
            {
                Type cls = xeno.genes[i]?.geneClass;
                if (cls != null && cls.Name.IndexOf("ChemicalDependency", StringComparison.Ordinal) >= 0) return DependencyBurden;
            }
            return 0f;
        }

        /// <summary>Genetic fragility (raises infant mortality), from frailty-style genes.</summary>
        public static float FragilityOf(XenotypeDef xeno)
        {
            if (xeno?.genes == null) return BaseFragility;
            for (int i = 0; i < xeno.genes.Count; i++)
            {
                string n = xeno.genes[i]?.defName;
                if (n == null) continue;
                if (n.IndexOf("Frail", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Fragile", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Delicate", StringComparison.OrdinalIgnoreCase) >= 0)
                    return FrailFragility;
            }
            return BaseFragility;
        }

        /// <summary>Whether a xenotype's germline breeds true (§8). Implanted xenotypes (Sanguophage) are
        /// not inheritable, so their children are baseliner. Reads <c>XenotypeDef.inheritable</c>.</summary>
        public static bool IsHeritable(XenotypeDef xeno) => xeno == null || xeno.inheritable;

        /// <summary>Whether this cohort is the plain-human baseliner (the "other"/fallback cohort).</summary>
        public static bool IsBaseliner(XenotypeDef xeno) => xeno == null || xeno.defName == "Baseliner";

        /// <summary>Build one cohort's intrinsic state from its xenotype and its share of the region.</summary>
        public static CohortState BuildCohort(XenotypeDef xeno, float share, float regionPopulation)
        {
            var c = new CohortState
            {
                lifespan = LifespanOf(xeno),
                fragility = FragilityOf(xeno),
                drugBurden = DrugBurdenOf(xeno),
                heritable = IsHeritable(xeno),
                isBaseliner = IsBaseliner(xeno),
                fertility = 0.45f,
                baseInit = 0.1f,
                basePreference = 0.1f,
                share = share < 0f ? 0f : share,
            };
            c.pop = regionPopulation * c.share;
            return c;
        }

        /// <summary>
        /// The region's cohort roster: the faction's top-N xenotypes as named cohorts (each paired with its
        /// <see cref="XenotypeDef"/> identity) plus an "other" bucket for the tail, populated to the region's
        /// headcount. One Baseliner cohort when Biotech is off or the faction has no roster — the
        /// graceful-degradation case (= the old region-level model).
        /// </summary>
        public static List<RegionCohort> BuildRoster(Faction faction, float regionPopulation, int topN = DefaultTopN)
        {
            var roster = new List<RegionCohort>();
            FactionDemographicProfile profile = faction != null ? FactionDemographicProfile.Build(faction) : FactionDemographicProfile.Empty;

            // normalise the weights into shares
            float total = 0f;
            if (profile?.raceWeights != null) for (int i = 0; i < profile.raceWeights.Length; i++) total += profile.raceWeights[i];

            if (profile?.races == null || profile.races.Length == 0 || total <= 0f)
            {
                roster.Add(new RegionCohort(null, BuildCohort(null, 1f, regionPopulation)));   // single Baseliner cohort
                return roster;
            }

            // sort race indices by weight, descending, keeping the top-N as named cohorts.
            var order = new List<int>();
            for (int i = 0; i < profile.races.Length; i++) order.Add(i);
            order.Sort((a, b) => profile.raceWeights[b].CompareTo(profile.raceWeights[a]));

            float otherShare = 0f;
            int named = 0;
            for (int k = 0; k < order.Count; k++)
            {
                int idx = order[k];
                float share = profile.raceWeights[idx] / total;
                if (named < topN)
                {
                    roster.Add(new RegionCohort(profile.races[idx], BuildCohort(profile.races[idx], share, regionPopulation)));
                    named++;
                }
                else otherShare += share;
            }
            if (otherShare > 0f)
                roster.Add(new RegionCohort(null, BuildCohort(null, otherShare, regionPopulation)));   // the tail, as baseliner
            return roster;
        }
    }
}
