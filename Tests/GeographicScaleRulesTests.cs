// Behaviour tests for the geographic-scale layer (#58 §6): area/food/carrying-capacity/self-sufficiency at
// the district tile scale (WorldScaleRules.TileAreaKm2). Ported from Design/sim/graph.js. Pure.
using System;
using RegionsAndSocieties.Demographics;
using RegionsAndSocieties.Sizing;

namespace GeographicScaleRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("arable fraction by biome fertility");
            Check("a poor biome is ~5% arable", Close(GeographicScaleRules.ArableFraction(0f), 0.05f));
            Check("a rich biome is ~40% arable", Close(GeographicScaleRules.ArableFraction(1f), 0.40f));
            Check("it never exceeds the cap", GeographicScaleRules.ArableFraction(5f) <= 0.45f + 1e-6f);
            Check("richer land is more arable", GeographicScaleRules.ArableFraction(0.8f) > GeographicScaleRules.ArableFraction(0.2f));

            Section("yield per arable km rises with education");
            Check("subsistence feeds ~50/arable-km2", Close(GeographicScaleRules.PeoplePerArableKm2(0f), 50f));
            Check("industrial feeds ~500/arable-km2", Close(GeographicScaleRules.PeoplePerArableKm2(1f), 500f));

            Section("region area is the district tile scale");
            Check("area = tiles x TileAreaKm2", Close(GeographicScaleRules.RegionAreaKm2(10), 10f * WorldScaleRules.TileAreaKm2));
            Check("a tile is ~23.4 km2 (district model)", WorldScaleRules.TileAreaKm2 > 20f && WorldScaleRules.TileAreaKm2 < 27f);
            Check("a zero-tile region floors at one tile", GeographicScaleRules.RegionAreaKm2(0) == WorldScaleRules.TileAreaKm2);

            Section("food capacity scales with land, fertility, education");
            float baseCap = GeographicScaleRules.FoodCapacity(100, 0.5f, 0.3f);
            Check("more land feeds more", GeographicScaleRules.FoodCapacity(200, 0.5f, 0.3f) > baseCap);
            Check("richer land feeds more", GeographicScaleRules.FoodCapacity(100, 0.9f, 0.3f) > baseCap);
            Check("more education feeds more", GeographicScaleRules.FoodCapacity(100, 0.5f, 0.9f) > baseCap);

            Section("carrying capacity: floor and trade imports");
            Check("barren land still supports the floor", GeographicScaleRules.CarryingCapacity(0f, 0f, 0f) == 50f);
            Check("roads + a trade sector import food",
                GeographicScaleRules.CarryingCapacity(1000f, 1f, 1f) > GeographicScaleRules.CarryingCapacity(1000f, 0f, 0f));
            Check("no roads, no import lift", Close(GeographicScaleRules.CarryingCapacity(1000f, 0f, 1f), 1000f));

            Section("food self-sufficiency (0.5 = break-even)");
            Check("population equal to capacity reads break-even", Close(GeographicScaleRules.FoodSelfSufficiency(1000f, 1000f), 0.5f));
            Check("twice the capacity of the population reads self-sufficient (1)",
                Close(GeographicScaleRules.FoodSelfSufficiency(2000f, 1000f), 1f));
            Check("a starving region reads low", GeographicScaleRules.FoodSelfSufficiency(100f, 5000f) < 0.1f);

            Section("district-scale sanity: a real region feeds itself");
            // ~200-tile region, temperate, modestly educated, ~12k people (matching the in-game validation).
            float cap = GeographicScaleRules.FoodCapacity(200, 0.5f, 0.3f);
            Check("capacity comfortably exceeds a 12k population", cap > 12000f);
            Check("so it reads fully self-sufficient", Close(GeographicScaleRules.FoodSelfSufficiency(cap, 12000f), 1f));
            Check("density is a sane figure", GeographicScaleRules.DensityPerKm2(12000f, 200) > 0f && GeographicScaleRules.DensityPerKm2(12000f, 200) < 10f);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL GEOGRAPHIC SCALE TESTS PASSED" : failures + " GEOGRAPHIC SCALE TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static bool Close(float a, float b) => Math.Abs(a - b) < 0.01f * Math.Max(1f, Math.Abs(b));

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
