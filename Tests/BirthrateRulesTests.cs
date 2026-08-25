// Behaviour tests for the settlement birthrate-growth core (#6): additive factor model (fertility,
// mortality, wealth, famine, ideology/xenotype biases), the demographic-transition hump, and the
// logistic GrowStep. Pure, so this runs without a game.
using System;
using RegionsAndSocieties.Sizing;

namespace BirthrateRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("factor terms");
            Check("fertility rises with the fertile-age share", BirthrateRules.FertilityRate(0.30f) > BirthrateRules.FertilityRate(0.15f));
            Check("no fertile women -> no births", BirthrateRules.FertilityRate(0f) == 0f);
            Check("medicine lowers mortality (neolithic dies faster than industrial)", BirthrateRules.MortalityRate(2) > BirthrateRules.MortalityRate(4));
            Check("wealth suppresses fertility (0 at subsistence, positive when rich)",
                BirthrateRules.WealthFertilityPenalty(0f) == 0f && BirthrateRules.WealthFertilityPenalty(1f) > 0f);
            Check("famine only when food is short", BirthrateRules.FamineMortality(1f) == 0f && BirthrateRules.FamineMortality(0.2f) > 0f);

            Section("transition shape at game pace");
            // Realistic fertile-age-women shares from the region age structure: younger pre-industrial
            // pyramids ~0.14, aging post-industrial ~0.09.
            float neo = BirthrateRules.NetAnnualRate(Inputs(2, 0.14f, 0f));            // pre-industrial, poor, young
            float industrializing = BirthrateRules.NetAnnualRate(Inputs(4, 0.12f, 0.3f)); // industrial, developing
            float richSpacer = BirthrateRules.NetAnnualRate(Inputs(5, 0.09f, 1.0f));   // wealthy post-industrial, aging
            Check($"a healthy developing town grows at a game pace ~10-15% (got {industrializing:0.000})", Between(industrializing, 0.09f, 0.16f));
            Check("pre-industrial still grows (not stagnant)", neo > 0.03f);
            Check("wealthy post-industrial falls below industrializing (the hump holds)", richSpacer < industrializing);
            Check("but the ends stay positive at game pace (no stagnant settlements)", richSpacer > 0f);

            Section("factors are additive and degrade gracefully");
            var baseIn = Inputs(4, 0.12f, 0.3f);
            float baseNet = BirthrateRules.NetAnnualRate(baseIn);
            // A missing DLC factor arrives as 0 and drops out — same result as not passing it.
            var withNeutral = baseIn; withNeutral.IdeologyBias = 0f; withNeutral.XenotypeBias = 0f;
            Check("neutral (absent) ideology/xenotype leave the rate unchanged", BirthrateRules.NetAnnualRate(withNeutral) == baseNet);
            var natalist = baseIn; natalist.IdeologyBias = 0.01f;
            Check("a natalist ideology raises the rate", BirthrateRules.NetAnnualRate(natalist) > baseNet);
            var barren = baseIn; barren.XenotypeBias = -0.02f;
            Check("a non-breeding xenotype lowers the rate", BirthrateRules.NetAnnualRate(barren) < baseNet);
            var starving = baseIn; starving.FoodBalance = 0.3f;
            Check("a food shortfall lowers the rate (famine mortality)", BirthrateRules.NetAnnualRate(starving) < baseNet);
            var atWar = baseIn; atWar.WarLossRate = 0.03f;
            Check("war losses lower the rate", BirthrateRules.NetAnnualRate(atWar) < baseNet);
            // Famine/war can still overwhelm growth and push a settlement into decline.
            float besieged = BirthrateRules.NetAnnualRate(Inputs(5, 0.09f, 1.0f, 0.2f, 0.02f));
            Check("heavy famine + war pushes a settlement below zero (it shrinks)", besieged < 0f);
            Check("net rate is clamped to a sane band",
                Between(BirthrateRules.NetAnnualRate(Inputs(4, 5f, 0f)), 0.20f, 0.25f)
                && Between(BirthrateRules.NetAnnualRate(Inputs(2, 0f, 1f, 0.1f, 0.5f)), -0.12f, -0.10f));

            Section("one growth step");
            float r = 0.03f;
            float s = BirthrateRules.GrowStep(10f, 100, 150, r, 1f);
            Check("a settlement below target grows", s > 10f);
            Check("...but does not overshoot the target in one step", s <= 100f);
            Check("zero elapsed time -> no change", BirthrateRules.GrowStep(10f, 100, 150, r, 0f) == 10f);
            Check("higher rate grows more", BirthrateRules.GrowStep(10f, 100, 150, 0.05f, 1f) > BirthrateRules.GrowStep(10f, 100, 150, 0.01f, 1f));
            float dMid = BirthrateRules.GrowStep(50f, 100, 150, r, 1f) - 50f;
            float dHigh = BirthrateRules.GrowStep(95f, 100, 150, r, 1f) - 95f;
            Check("growth tapers as it fills (mid > near-full)", dMid > dHigh);
            Check("a near-zero settlement seeds and grows", BirthrateRules.GrowStep(0f, 100, 150, r, 1f) > 0f);

            Section("clamping, decline, and over-capacity");
            Check("never exceeds max", BirthrateRules.GrowStep(200f, 100, 150, r, 1f) <= 150f);
            Check("never goes negative", BirthrateRules.GrowStep(-5f, 100, 150, r, 1f) >= 0f);
            Check("over target (positive rate) shrinks toward the target", Between(BirthrateRules.GrowStep(120f, 100, 150, r, 1f), 100f, 120f));
            // Negative net rate = intrinsic decline toward zero, even below the capacity.
            float decl = BirthrateRules.GrowStep(80f, 100, 150, -0.02f, 1f);
            Check("a negative rate declines the population", decl < 80f);
            Check("...toward zero, not toward the capacity", decl < 100f);

            Section("converges to the target over many steps");
            float p = 5f;
            for (int i = 0; i < 800; i++) p = BirthrateRules.GrowStep(p, 100, 150, 0.05f, 0.5f);
            Check($"approaches the target after a long run (got {p:0.0})", Close(p, 100f, 1.5f));

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL BIRTHRATE TESTS PASSED" : failures + " BIRTHRATE TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static GrowthInputs Inputs(int tech, float fertile, float wealth, float food = 1f, float war = 0f)
        {
            return new GrowthInputs { TechLevel = tech, FertileFraction = fertile, WealthLevel = wealth, FoodBalance = food, WarLossRate = war };
        }

        private static bool Between(float v, float lo, float hi) => v >= lo && v <= hi;
        private static bool Close(float a, float b, float tol) => Math.Abs(a - b) <= tol;
        private static void Section(string s) { Console.WriteLine(); Console.WriteLine("-- " + s); }
        private static void Check(string label, bool ok) { if (!ok) failures++; Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label); }
    }
}
