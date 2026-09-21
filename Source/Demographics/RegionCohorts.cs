using System;
using System.Collections.Generic;
using Verse;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>A cohort's identity (its xenotype, by defName) paired with its evolving <see cref="CohortState"/>.
    /// The defName is empty for the baseliner and hybrid cohorts; <c>state.isBaseliner</c>/<c>state.isHybrid</c>
    /// distinguish them. Identity is a string, not an <c>XenotypeDef</c>, so the scribed container stays free of
    /// game defs — the game-coupled layer (<see cref="CohortFactory"/>, pawn generation) maps defName ↔ def.</summary>
    public class RegionCohort : IExposable
    {
        public string xenoDefName;           // "" for baseliner / hybrid / "other"
        public CohortState state = new CohortState();

        public RegionCohort() { }
        public RegionCohort(string xenoDefName, CohortState state) { this.xenoDefName = xenoDefName ?? ""; this.state = state; }

        public bool IsXeno => !string.IsNullOrEmpty(xenoDefName);

        public void ExposeData()
        {
            Scribe_Values.Look(ref xenoDefName, "xeno", "");
            if (state == null) state = new CohortState();
            // Persist the intrinsic Stock + the slow-carried values only; the derived per-year outputs
            // (income, vitals, education/strata distributions, indicators) are recomputed each AdvanceYear.
            Scribe_Values.Look(ref state.lifespan, "lifespan", 80f);
            Scribe_Values.Look(ref state.fragility, "fragility", 0.02f);
            Scribe_Values.Look(ref state.fertility, "fertility", 0.45f);
            Scribe_Values.Look(ref state.drugBurden, "drugBurden", 0f);
            Scribe_Values.Look(ref state.heritable, "heritable", true);
            Scribe_Values.Look(ref state.isBaseliner, "isBaseliner", false);
            Scribe_Values.Look(ref state.isHybrid, "isHybrid", false);
            Scribe_Values.Look(ref state.baseInit, "baseInit", 0.1f);
            Scribe_Values.Look(ref state.basePreference, "basePreference", 0.1f);
            Scribe_Values.Look(ref state.pop, "pop", 0f);
            Scribe_Values.Look(ref state.share, "share", 0f);
            Scribe_Values.Look(ref state.assets, "assets", 0f);
        }
    }

    /// <summary>
    /// The scribed per-region cohort container (DEMOGRAPHIC_MODEL §1, #58 step 3) — the <b>source of truth</b>
    /// for a region's people. The caller seeds it with a roster (built by <see cref="CohortFactory"/>), then
    /// advances it one demographic year at a time: every cohort's factors step (<see cref="CohortYearRules"/>),
    /// then populations move by births, deaths and migration with germline inheritance
    /// (<see cref="ReproductionRules"/>). Region aggregates are read back off these cohorts.
    ///
    /// <para>Pure of game defs (identity is a defName string), so it scribes cleanly and the step/inheritance
    /// math it drives is unit-tested; only the roster building (which reads genes) is game-coupled and lives in
    /// <see cref="CohortFactory"/>. Bounded by the top-N + "other" roster, so a save stays small.</para>
    /// </summary>
    public class RegionCohorts : IExposable
    {
        public List<RegionCohort> cohorts = new List<RegionCohort>();
        public bool seeded;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref cohorts, "cohorts", LookMode.Deep);
            Scribe_Values.Look(ref seeded, "seeded", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && cohorts == null) cohorts = new List<RegionCohort>();
        }

        public float TotalPopulation
        {
            get { float p = 0f; if (cohorts != null) for (int i = 0; i < cohorts.Count; i++) p += cohorts[i].state.pop; return p; }
        }

        /// <summary>Cold start: adopt a pre-built roster (from <see cref="CohortFactory.BuildRoster"/>).</summary>
        public void SetRoster(List<RegionCohort> roster)
        {
            cohorts = roster ?? new List<RegionCohort>();
            seeded = true;
        }

        /// <summary>Advance every cohort one demographic year against the region stage, then move populations.
        /// <paramref name="acceptance"/> (#81, optional) is the region's per-xenotype acceptance the player's
        /// example has built up; it shifts each matching cohort's standing before its year steps.</summary>
        public void AdvanceYear(RegionStage stage, Dictionary<string, float> acceptance = null)
        {
            if (cohorts == null || cohorts.Count == 0) return;
            CohortYearRules.PrepareRegion(stage);

            float total = TotalPopulation; if (total <= 0f) total = 1f;
            for (int i = 0; i < cohorts.Count; i++)
            {
                CohortState s = cohorts[i].state;
                s.share = s.pop / total;
                s.acceptanceOffset = (acceptance != null && acceptance.TryGetValue(cohorts[i].xenoDefName, out float a)) ? a : 0f;
                CohortYearRules.Step(s, stage);
            }
            MoveStocks(stage);
        }

        // Births, deaths, migration + germline inheritance (ported from the sim's moveStocks, using the pure
        // ReproductionRules). Births are assigned to child cohorts by endogamy/exogamy; new baseliner/hybrid
        // cohorts spawn as needed; vanished cohorts are pruned; shares are recomputed.
        private void MoveStocks(RegionStage stage)
        {
            float food = GeographicScaleRules.FoodCapacity(stage.tiles, stage.biomeFertility, stage.skilledEduShare);
            float K = GeographicScaleRules.CarryingCapacity(food, stage.roads, stage.sectorServices);
            float total = TotalPopulation;
            float logistic = ReproductionRules.LogisticFactor(total, K);
            float endogamy = ReproductionRules.Endogamy(stage.ideoTolerance);
            float totPop = total <= 0f ? 1f : total;

            var bornXeno = new Dictionary<string, float>();
            float bornBaseliner = 0f, bornHybrid = 0f;

            void AddToParent(RegionCohort p, float n)
            {
                if (n <= 0f) return;
                if (p.state.isHybrid) bornHybrid += n;
                else if (!p.IsXeno || p.state.isBaseliner) bornBaseliner += n;
                else { bornXeno.TryGetValue(p.xenoDefName, out float v); bornXeno[p.xenoDefName] = v + n; }
            }
            void AddOutcome(ChildOutcome oc, RegionCohort a, RegionCohort b, float n)
            {
                if (n <= 0f) return;
                switch (oc)
                {
                    case ChildOutcome.ParentA: AddToParent(a, n); break;
                    case ChildOutcome.ParentB: AddToParent(b, n); break;
                    case ChildOutcome.Baseliner: bornBaseliner += n; break;
                    case ChildOutcome.Hybrid: bornHybrid += n; break;
                }
            }

            for (int i = 0; i < cohorts.Count; i++)
            {
                CohortState s = cohorts[i].state;
                float B = ReproductionRules.Births(s.pop, s.birthRate, s.infantMortalityPer1000, logistic);
                if (B <= 0f) continue;
                RegionCohort x = cohorts[i];
                AddOutcome(ReproductionRules.ChildOfSameGroup(s.heritable), x, x, B * endogamy);
                float exo = B * (1f - endogamy);
                if (exo <= 0f) continue;
                for (int j = 0; j < cohorts.Count; j++)
                {
                    if (j == i) continue;
                    RegionCohort y = cohorts[j];
                    if (y.state.pop <= 0f) continue;
                    float n = exo * (y.state.pop / totPop);
                    if (n <= 0f) continue;
                    AddOutcome(ReproductionRules.ChildOfPairing(s.heritable, s.isBaseliner, y.state.heritable, y.state.isBaseliner), x, y, n);
                }
            }

            for (int i = 0; i < cohorts.Count; i++)
            {
                CohortState s = cohorts[i].state;
                float deaths = ReproductionRules.Deaths(s.pop, s.mortalityHazard);
                float mig = ReproductionRules.NetMigration(s.pop, s.migNet);
                s.pop = Math.Max(0f, s.pop - deaths + mig);
            }

            foreach (var kv in bornXeno) FindOrCreateXeno(kv.Key).state.pop += kv.Value;
            if (bornBaseliner > 0f) FindOrCreateSpecial(false).state.pop += bornBaseliner;
            if (bornHybrid > 0f) FindOrCreateSpecial(true).state.pop += bornHybrid;

            cohorts.RemoveAll(c => c.state.pop <= 0.5f && c.state.share <= 0.001f);
            float p = TotalPopulation;
            for (int i = 0; i < cohorts.Count; i++) cohorts[i].state.share = p > 0f ? cohorts[i].state.pop / p : 0f;
        }

        private RegionCohort FindOrCreateXeno(string xenoDefName)
        {
            for (int i = 0; i < cohorts.Count; i++)
                if (cohorts[i].xenoDefName == xenoDefName && !cohorts[i].state.isHybrid) return cohorts[i];
            // A xenotype cohort that has died out and is being reintroduced by exogamy: revive it as a plain
            // heritable germline (its gene-read intrinsics are lost, but the identity/lineage continues).
            var c = new RegionCohort(xenoDefName, new CohortState { heritable = true, isBaseliner = false });
            cohorts.Add(c);
            return c;
        }

        private RegionCohort FindOrCreateSpecial(bool hybrid)
        {
            for (int i = 0; i < cohorts.Count; i++)
            {
                CohortState s = cohorts[i].state;
                if (hybrid ? s.isHybrid : (s.isBaseliner && !s.isHybrid && !cohorts[i].IsXeno)) return cohorts[i];
            }
            var st = new CohortState { heritable = true, isBaseliner = !hybrid, isHybrid = hybrid };
            if (hybrid) { st.baseInit = -0.15f; st.basePreference = -0.15f; }   // hybrids carry a mild standing penalty (§8)
            var c = new RegionCohort("", st);
            cohorts.Add(c);
            return c;
        }
    }
}
