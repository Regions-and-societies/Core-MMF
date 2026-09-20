// Integration tests for the per-cohort year step (#58): the six pure modules composed into one annual
// advance. Checks the pieces wire together and the emergent shapes the sim shows fall out. Pure.
using System;
using RegionsAndSocieties.Demographics;

namespace CohortYearRulesTests
{
    public static class Program
    {
        private static int failures;

        private static RegionStage ComfortableRegion()
        {
            var r = new RegionStage
            {
                wealth = 0.7f, urbanisation = 0.5f, employmentRate = 0.9f, conflict = 0f, pollution = 0.05f,
                roads = 0.5f, biomeFertility = 0.6f, slaveryStance = 0f, ideoTolerance = 0.6f, natalism = 0.3f,
                ageWorking = 0.6f, crime = 0.05f,
                education = new float[] { 0.10f, 0.30f, 0.35f, 0.18f, 0.07f },
                sectorServices = 0.4f, sectorManufacturing = 0.3f, sectorPublic = 0.1f, sectorMilitary = 0.05f,
                tiles = 200, population = 12000f,
            };
            CohortYearRules.PrepareRegion(r);
            return r;
        }

        private static RegionStage HarshSlaveRegion()
        {
            var r = new RegionStage
            {
                wealth = 0.5f, urbanisation = 0.4f, employmentRate = 0.7f, conflict = 0.2f, pollution = 0.2f,
                roads = 0.3f, biomeFertility = 0.4f, slaveryStance = 1f, ideoTolerance = -0.7f, natalism = 0.4f,
                ageWorking = 0.6f, crime = 0.2f,
                education = new float[] { 0.20f, 0.40f, 0.25f, 0.10f, 0.05f },
                sectorServices = 0.2f, sectorManufacturing = 0.3f, sectorPublic = 0.1f, sectorMilitary = 0.15f,
                tiles = 150, population = 8000f,
            };
            CohortYearRules.PrepareRegion(r);
            return r;
        }

        // a rough one-year net growth signal (the container turns these into real pop moves via ReproductionRules)
        private static float NetSignal(CohortState x) => x.birthRate - x.mortalityHazard + x.migNet;

        public static int Main()
        {
            Section("the step populates a coherent profile");
            var r = ComfortableRegion();
            var acc = new CohortState { lifespan = 80f, fertility = 0.45f, heritable = true, isBaseliner = true, baseInit = 0.2f, basePreference = 0.2f, pop = 6000f, share = 0.5f };
            CohortYearRules.Step(acc, r);
            Check("education distribution sums to 1", Close(Sum(acc.education), 1f));
            Check("strata sums to 1", Close(acc.strataElite + acc.strataMiddle + acc.strataUnderclass, 1f));
            Check("age structure sums to 1", Close(acc.ageChild + acc.ageWorking + acc.ageElder, 1f));
            Check("life expectancy is set", acc.lifeExpectancy >= 15);
            Check("an income source was assigned", Enum.IsDefined(typeof(IncomeSource), acc.incomeSource));
            Check("contentment in range", acc.contentment >= 0f && acc.contentment <= 1f);

            Section("an accepted, well-off cohort thrives");
            Check("high standing in a tolerant region", acc.standing > 0.5f);
            Check("births are near full (not standing-suppressed)", acc.birthMult > 0.9f);
            Check("dies of old age in comfort", acc.leadingCause == DeathCause.OldAge);
            Check("net signal is non-negative", NetSignal(acc) >= 0f);

            Section("a hated, drug-dependent, enslaved cohort declines");
            var r2 = HarshSlaveRegion();
            var doomed = new CohortState { lifespan = 80f, fertility = 0.45f, drugBurden = 0.7f, heritable = true, isBaseliner = false, baseInit = -0.6f, basePreference = -0.6f, pop = 500f, share = 0.06f };
            CohortYearRules.Step(doomed, r2);
            Check("standing is deeply negative", doomed.standing < -0.5f);
            Check("births are crushed by low standing", doomed.birthMult < 0.3f);
            Check("it is enslaved", doomed.slaveShare > 0f);
            Check("education is capped by slavery", doomed.education[3] + doomed.education[4] < 0.15f);
            Check("crime is high", doomed.crime > 0.4f);
            Check("it is trying to flee (negative net migration)", doomed.migNet < 0f);
            Check("its net signal is worse than the thriving cohort", NetSignal(doomed) < NetSignal(acc));

            Section("familiarity drift: a common cohort grows more accepted");
            var common = new CohortState { baseInit = 0.1f, basePreference = 0.1f, share = 0.7f, heritable = true, pop = 8000f };
            float p0 = common.basePreference;
            for (int y = 0; y < 30; y++) CohortYearRules.Step(common, r);
            Check("a common cohort's preference drifts upward over years", common.basePreference > p0);

            Section("shifted education pyramid");
            float[] baseE = { 0.2f, 0.4f, 0.25f, 0.1f, 0.05f };
            float[] hi = new float[5], lo = new float[5];
            CohortYearRules.ShiftedPyramid(baseE, 0.9f, hi);   // high access -> tilt to higher tiers
            CohortYearRules.ShiftedPyramid(baseE, 0.1f, lo);   // low access -> tilt to lower tiers
            Check("high access lifts the top tiers", hi[4] > lo[4] && hi[3] > lo[3]);
            Check("low access swells the bottom tiers", lo[0] > hi[0]);
            Check("both still sum to 1", Close(Sum(hi), 1f) && Close(Sum(lo), 1f));

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL COHORT YEAR TESTS PASSED" : failures + " COHORT YEAR TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static float Sum(float[] a) { float s = 0f; for (int i = 0; i < a.Length; i++) s += a[i]; return s; }
        private static bool Close(float a, float b) => Math.Abs(a - b) < 0.0025f;

        private static void Section(string name)
        {
            Console.WriteLine();
            Console.WriteLine("-- " + name);
        }

        private static void Check(string label, bool ok)
        {
            if (!ok) failures++;
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label);
        }
    }
}
