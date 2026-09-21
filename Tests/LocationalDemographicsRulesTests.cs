// Behaviour tests for location-based demographics (#33): a region's aggregate becomes a per-tile
// gradient keyed to local density, built so the population-weighted mean of the tiles equals the
// region aggregate. Pure.
using System;
using RegionsAndSocieties.Demographics;

namespace LocationalDemographicsRulesTests
{
    public static class Program
    {
        private static int failures;

        // a small region: a dense city tile, a town, and a spread of rural tiles
        private static readonly float[] Pops = { 6000f, 1900f, 700f, 100f, 30f, 30f, 10f, 0f };

        public static int Main()
        {
            float maxPop = Max(Pops);

            Section("urbanity");
            Check("empty tile is fully rural", Close(LocationalDemographicsRules.Urbanity(0f, maxPop), 0f));
            Check("the densest tile is fully urban", Close(LocationalDemographicsRules.Urbanity(maxPop, maxPop), 1f));
            Check("urbanity rises with population",
                LocationalDemographicsRules.Urbanity(1900f, maxPop) > LocationalDemographicsRules.Urbanity(100f, maxPop));
            Check("urbanity is bounded 0..1",
                LocationalDemographicsRules.Urbanity(999999f, maxPop) <= 1f && LocationalDemographicsRules.Urbanity(-5f, maxPop) >= 0f);
            Check("no max population -> everything rural", Close(LocationalDemographicsRules.Urbanity(500f, 0f), 0f));

            float meanU = MeanUrbanity(Pops, maxPop);

            Section("density factor");
            Check("a tile at the mean urbanity is neutral", Close(LocationalDemographicsRules.DensityFactor(meanU, meanU, 0.6f), 1f));
            Check("denser than average reads above 1", LocationalDemographicsRules.DensityFactor(1f, meanU, 0.6f) > 1f);
            Check("sparser than average reads below 1", LocationalDemographicsRules.DensityFactor(0f, meanU, 0.6f) < 1f);
            Check("a negative spread inverts the direction", LocationalDemographicsRules.DensityFactor(1f, meanU, -0.6f) < 1f);
            Check("the factor is clamped", LocationalDemographicsRules.DensityFactor(1f, 0f, 100f) <= LocationalDemographicsRules.MaxFactor + 1e-4f);

            Section("directionality: cities are richer, better-schooled, younger");
            float uCity = LocationalDemographicsRules.Urbanity(6000f, maxPop);
            float uRural = LocationalDemographicsRules.Urbanity(30f, maxPop);
            Check("city wealth > rural wealth",
                LocationalDemographicsRules.LocalWealth(350, uCity, meanU) > LocationalDemographicsRules.LocalWealth(350, uRural, meanU));
            Check("city education > rural education",
                LocationalDemographicsRules.LocalEducationIndex(50, uCity, meanU) > LocationalDemographicsRules.LocalEducationIndex(50, uRural, meanU));
            Check("city employment > rural employment",
                LocationalDemographicsRules.LocalEmploymentRate(66, uCity, meanU) >= LocationalDemographicsRules.LocalEmploymentRate(66, uRural, meanU));
            Check("city median age < rural median age",
                LocationalDemographicsRules.LocalMedianAge(34, uCity, meanU) < LocationalDemographicsRules.LocalMedianAge(34, uRural, meanU));

            Section("mean-preserving: population-weighted tiles reproduce the region aggregate");
            Check("weighted wealth ~ aggregate", Close(WeightedWealth(350, meanU, maxPop), 350f, 350f * 0.02f));
            Check("weighted education ~ aggregate", Close(WeightedEdu(50, meanU, maxPop), 50f, 1.5f));
            Check("weighted employment ~ aggregate", Close(WeightedEmp(66, meanU, maxPop), 66f, 1.5f));

            Section("edge: a region with one populated tile");
            float[] one = { 500f };
            float m1 = Max(one), mu1 = MeanUrbanity(one, m1);
            Check("the sole tile reads the aggregate", Close(LocationalDemographicsRules.LocalWealth(350, LocationalDemographicsRules.Urbanity(500f, m1), mu1), 350f, 1f));

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL LOCATIONAL TESTS PASSED" : failures + " LOCATIONAL TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static float MeanUrbanity(float[] pops, float maxPop)
        {
            float wsum = 0f, psum = 0f;
            for (int i = 0; i < pops.Length; i++)
            {
                float u = LocationalDemographicsRules.Urbanity(pops[i], maxPop);
                wsum += pops[i] * u; psum += pops[i];
            }
            return psum > 0f ? wsum / psum : 0f;
        }

        private static float WeightedWealth(int agg, float meanU, float maxPop)
        {
            float acc = 0f, psum = 0f;
            for (int i = 0; i < Pops.Length; i++)
            {
                float u = LocationalDemographicsRules.Urbanity(Pops[i], maxPop);
                acc += Pops[i] * LocationalDemographicsRules.LocalWealth(agg, u, meanU);
                psum += Pops[i];
            }
            return psum > 0f ? acc / psum : 0f;
        }

        private static float WeightedEdu(int agg, float meanU, float maxPop)
        {
            float acc = 0f, psum = 0f;
            for (int i = 0; i < Pops.Length; i++)
            {
                float u = LocationalDemographicsRules.Urbanity(Pops[i], maxPop);
                acc += Pops[i] * LocationalDemographicsRules.LocalEducationIndex(agg, u, meanU);
                psum += Pops[i];
            }
            return psum > 0f ? acc / psum : 0f;
        }

        private static float WeightedEmp(int agg, float meanU, float maxPop)
        {
            float acc = 0f, psum = 0f;
            for (int i = 0; i < Pops.Length; i++)
            {
                float u = LocationalDemographicsRules.Urbanity(Pops[i], maxPop);
                acc += Pops[i] * LocationalDemographicsRules.LocalEmploymentRate(agg, u, meanU);
                psum += Pops[i];
            }
            return psum > 0f ? acc / psum : 0f;
        }

        private static float Max(float[] a) { float m = 0f; for (int i = 0; i < a.Length; i++) if (a[i] > m) m = a[i]; return m; }
        private static bool Close(float a, float b) => Math.Abs(a - b) < 0.0025f;
        private static bool Close(float a, float b, float tol) => Math.Abs(a - b) <= tol;

        private static void Section(string name) { Console.WriteLine(); Console.WriteLine("-- " + name); }
        private static void Check(string label, bool ok)
        {
            if (!ok) failures++;
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label);
        }
    }
}
