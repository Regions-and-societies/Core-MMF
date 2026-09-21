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
        public static float LifespanOf(XenotypeDef xeno) => LifespanFromGenes(xeno?.genes);

        /// <summary>The genetic drug burden a xenotype carries: a chemical-dependency gene (Hussar, Waster…)
        /// coerces its labour and drains its wealth.</summary>
        public static float DrugBurdenOf(XenotypeDef xeno) => DrugBurdenFromGenes(xeno?.genes);

        /// <summary>Genetic fragility (raises infant mortality), from frailty-style genes.</summary>
        public static float FragilityOf(XenotypeDef xeno) => FragilityFromGenes(xeno?.genes);

        // The intrinsic-reading core works on a raw gene list, so it serves both a XenotypeDef's genes and a
        // CustomXenotype's genes (player-editor / xenogerm xenotypes) identically — the same keyword/geneClass
        // detection, zero knowledge of any specific xenotype required.

        public static float LifespanFromGenes(List<GeneDef> genes)
        {
            if (genes == null) return BaselineLifespan;
            float best = BaselineLifespan;
            for (int i = 0; i < genes.Count; i++)
            {
                string n = genes[i]?.defName;
                if (n == null) continue;
                if (n.IndexOf("Ageless", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Deathless", StringComparison.OrdinalIgnoreCase) >= 0)
                    return ImmortalLifespan;
                if (n.IndexOf("Longevity", StringComparison.OrdinalIgnoreCase) >= 0)
                    best = LongevityLifespan;
            }
            return best;
        }

        public static float DrugBurdenFromGenes(List<GeneDef> genes)
        {
            if (genes == null) return 0f;
            for (int i = 0; i < genes.Count; i++)
            {
                Type cls = genes[i]?.geneClass;
                if (cls != null && cls.Name.IndexOf("ChemicalDependency", StringComparison.Ordinal) >= 0) return DependencyBurden;
            }
            return 0f;
        }

        public static float FragilityFromGenes(List<GeneDef> genes)
        {
            if (genes == null) return BaseFragility;
            for (int i = 0; i < genes.Count; i++)
            {
                string n = genes[i]?.defName;
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
            => BuildCohortFromGenes(xeno?.genes, IsHeritable(xeno), IsBaseliner(xeno), share, regionPopulation);

        /// <summary>Build one cohort's intrinsic state from a raw gene list — the shared core, so a def-less
        /// xenotype (a player custom xenotype read off a colonist) is built exactly like a def-backed one.</summary>
        public static CohortState BuildCohortFromGenes(List<GeneDef> genes, bool heritable, bool isBaseliner, float share, float regionPopulation)
        {
            var c = new CohortState
            {
                lifespan = LifespanFromGenes(genes),
                fragility = FragilityFromGenes(genes),
                drugBurden = DrugBurdenFromGenes(genes),
                heritable = heritable,
                isBaseliner = isBaseliner,
                fertility = 0.45f,
                baseInit = 0.1f,
                basePreference = 0.1f,
                share = share < 0f ? 0f : share,
            };
            c.pop = regionPopulation * c.share;
            return c;
        }

        /// <summary>
        /// The cohort roster for the PLAYER'S region, read from the ACTUAL colony (#58 combine-the-lists): the
        /// player's people are whatever xenotypes their colonists are — including player-made <b>custom
        /// xenotypes</b> (the in-game editor) that have no <see cref="XenotypeDef"/> and so never appear in any
        /// faction table. Each distinct xenotype among the free colonists becomes a cohort whose share is its
        /// head-count fraction, scaled to the region's modelled population; a custom xenotype keeps its own
        /// name as identity (displayed as itself, not folded into Baseliner) and reads its intrinsics from its
        /// genes like any other. Returns empty when there are no colonists, so the caller falls back to the
        /// faction roster.
        /// </summary>
        public static List<RegionCohort> BuildRosterFromColony(float regionPopulation)
        {
            var roster = new List<RegionCohort>();
            List<Pawn> colonists;
            try { colonists = PawnsFinder.AllMaps_FreeColonists; } catch { colonists = null; }
            if (colonists == null || colonists.Count == 0) return roster;

            var count = new Dictionary<string, int>();
            var genesOf = new Dictionary<string, List<GeneDef>>();
            var heritableOf = new Dictionary<string, bool>();
            var baselinerOf = new Dictionary<string, bool>();
            int total = 0;

            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn_GeneTracker g = colonists[i]?.genes;
                string id; List<GeneDef> genes; bool heritable, baseliner;
                XenotypeDef xd = g?.Xenotype;
                if (xd != null && !IsBaseliner(xd))
                {
                    // A def-backed xenotype (vanilla or modded).
                    id = xd.defName; genes = xd.genes; heritable = xd.inheritable; baseliner = false;
                }
                else
                {
                    // Baseliner base xenotype — but it may be a player CUSTOM (unique) xenotype. Prefer the
                    // CustomXenotype the tracker surfaces; otherwise a non-baseliner xenotypeName is itself the
                    // signal (an editor-applied custom sets it), so a def-less xenotype is caught either way.
                    CustomXenotype cx = g?.CustomXenotype;
                    string cname = cx?.name
                        ?? (g != null && !string.IsNullOrEmpty(g.xenotypeName) && g.xenotypeName != "Baseliner" ? g.xenotypeName : null);
                    if (!string.IsNullOrEmpty(cname))
                    {
                        id = "custom:" + cname;
                        genes = cx?.genes ?? EndogeneDefs(g);
                        heritable = cx?.inheritable ?? true;
                        baseliner = false;
                    }
                    else { id = ""; genes = xd?.genes; heritable = true; baseliner = true; }   // "" → baseliner bucket
                }
                count.TryGetValue(id, out int n); count[id] = n + 1; total++;
                if (!genesOf.ContainsKey(id)) { genesOf[id] = genes; heritableOf[id] = heritable; baselinerOf[id] = baseliner; }
            }
            if (total == 0) return roster;

            foreach (var kv in count)
            {
                float share = (float)kv.Value / total;   // head-count fraction of the colony
                bool baseliner = baselinerOf[kv.Key];
                // Store the identity the overlay will read: "" for baseliner, the defName for a def-backed
                // xenotype, or the custom xenotype's own name (marker stripped) so it surfaces by name.
                string identity = baseliner ? "" : (kv.Key.StartsWith("custom:") ? kv.Key.Substring("custom:".Length) : kv.Key);
                roster.Add(new RegionCohort(identity,
                    BuildCohortFromGenes(genesOf[kv.Key], heritableOf[kv.Key], baseliner, share, regionPopulation)));
            }
            return roster;
        }

        /// <summary>The gene defs a pawn actually carries as endogenes — the intrinsic gene set of a custom
        /// (unique) xenotype when no <see cref="CustomXenotype"/> object is available to read from.</summary>
        private static List<GeneDef> EndogeneDefs(Pawn_GeneTracker g)
        {
            var list = new List<GeneDef>();
            List<Gene> endo = g?.Endogenes;
            if (endo != null)
                for (int i = 0; i < endo.Count; i++)
                    if (endo[i]?.def != null) list.Add(endo[i].def);
            return list;
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
                roster.Add(new RegionCohort("", BuildCohort(null, 1f, regionPopulation)));   // single Baseliner cohort
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
                    XenotypeDef xeno = profile.races[idx];
                    roster.Add(new RegionCohort(xeno?.defName ?? "", BuildCohort(xeno, share, regionPopulation)));
                    named++;
                }
                else otherShare += share;
            }
            if (otherShare > 0f)
                roster.Add(new RegionCohort("", BuildCohort(null, otherShare, regionPopulation)));   // the tail, as baseliner
            return roster;
        }
    }
}
