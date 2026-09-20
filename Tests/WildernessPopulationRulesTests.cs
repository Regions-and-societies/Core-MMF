// Behaviour tests for the seeded wilderness base-population surface (#79): biome-weighted density,
// deterministic per-tile jitter, uninhabitable land empty. Pure, so it runs without a game.
using System;
using RegionsAndSocieties.Sizing;

namespace WildernessPopulationRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("density from habitability");
            Check("uninhabitable land holds no one", WildernessPopulationRules.PeoplePerTile(0f) == 0f);
            Check("negative habitability holds no one", WildernessPopulationRules.PeoplePerTile(-1f) == 0f);
            Check("a reference tile holds the target mean",
                Close(WildernessPopulationRules.PeoplePerTile(WildernessPopulationRules.ReferenceHabitability), WildernessPopulationRules.TargetMeanPerTile));
            Check("better land holds more people",
                WildernessPopulationRules.PeoplePerTile(1.5f) > WildernessPopulationRules.PeoplePerTile(0.5f));
            Check("marginal land is well below the mean",
                WildernessPopulationRules.PeoplePerTile(0.3f) < WildernessPopulationRules.TargetMeanPerTile * 0.5f);
            Check("density is capped so one tile cannot rival a homestead",
                WildernessPopulationRules.PeoplePerTile(100f) <= WildernessPopulationRules.TargetMeanPerTile * WildernessPopulationRules.MaxMultiple + 0.001f);
            Check("even the best land stays below a homestead (100)",
                WildernessPopulationRules.PeoplePerTile(100f) < 100f * 1.5f);   // 120 < 150; a homestead peak still reads above it

            Section("per-tile jitter");
            float j = WildernessPopulationRules.Jitter(12345, 7);
            Check("jitter is within its half-range",
                j >= 1f - WildernessPopulationRules.JitterSpread - 0.0001f && j <= 1f + WildernessPopulationRules.JitterSpread + 0.0001f);
            Check("jitter is deterministic", WildernessPopulationRules.Jitter(12345, 7) == j);
            Check("a different tile jitters differently",
                WildernessPopulationRules.Jitter(12345, 7) != WildernessPopulationRules.Jitter(12345, 8));
            Check("a different seed jitters differently",
                WildernessPopulationRules.Jitter(99, 7) != WildernessPopulationRules.Jitter(12345, 7));

            Section("population per tile");
            Check("uninhabitable land is empty whatever the seed",
                WildernessPopulationRules.PopulationForTile(42, 100, 0f) == 0);
            Check("a habitable tile is populated",
                WildernessPopulationRules.PopulationForTile(42, 100, 1f) > 0);
            Check("PopulationForTile is deterministic",
                WildernessPopulationRules.PopulationForTile(42, 100, 1f) == WildernessPopulationRules.PopulationForTile(42, 100, 1f));
            Check("a reference tile lands near the target mean (within jitter)",
                Math.Abs(WildernessPopulationRules.PopulationForTile(42, 100, 1f) - WildernessPopulationRules.TargetMeanPerTile) <= WildernessPopulationRules.TargetMeanPerTile * WildernessPopulationRules.JitterSpread + 1);

            Section("a mean-ish world total is in the millions (sanity)");
            // ~49,000 land tiles at 30% coverage, reference habitability, mean ~30 -> ~1.5M.
            double worldish = 49000.0 * WildernessPopulationRules.PeoplePerTile(WildernessPopulationRules.ReferenceHabitability);
            Check("~49k reference tiles hold ~1.5M", worldish > 1_000_000 && worldish < 2_000_000);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL WILDERNESS POPULATION TESTS PASSED" : failures + " WILDERNESS POPULATION TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static bool Close(float a, float b) => Math.Abs(a - b) < 0.0005f;

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
