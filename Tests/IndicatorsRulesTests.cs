// Behaviour tests for the added societal indicators (#58 §7): development, sanitation, housing, freedom,
// affordability, contentment, substance use, dependency ratio. Ported from Design/sim/graph.js. Pure.
using System;
using RegionsAndSocieties.Demographics;

namespace IndicatorsRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("development level and sanitation");
            Check("dev level is 0..5", IndicatorsRules.DevelopmentLevel(1f, 1f) <= 5f && IndicatorsRules.DevelopmentLevel(0f, 0f) >= 0f);
            Check("wealth + urbanisation raise development", IndicatorsRules.DevelopmentLevel(0.8f, 0.8f) > IndicatorsRules.DevelopmentLevel(0.2f, 0.2f));
            Check("a hamlet (L1) has no treatment coverage", Close(IndicatorsRules.SanitationCoverage(1f), 0f));
            Check("a developed region (L5) has full coverage", Close(IndicatorsRules.SanitationCoverage(5f), 1f));
            Check("sanitation is coverage x purity", Close(IndicatorsRules.Sanitation(5f, 0.5f), 0.5f));
            Check("a poor developed region purifies less", IndicatorsRules.Sanitation(5f, 0.3f) < IndicatorsRules.Sanitation(5f, 0.9f));

            Section("housing");
            Check("income and development lift housing", IndicatorsRules.Housing(10f, 4f, 0f) > IndicatorsRules.Housing(1f, 1f, 0f));
            Check("slavery floors housing", IndicatorsRules.Housing(10f, 4f, 0.8f) < IndicatorsRules.Housing(10f, 4f, 0f));

            Section("freedom");
            Check("a free, accepted cohort is fully free", Close(IndicatorsRules.Freedom(0f, 0.5f), 1f));
            Check("slavery cuts freedom", IndicatorsRules.Freedom(0.5f, 0.5f) < 1f);
            Check("a hated cohort is less free", IndicatorsRules.Freedom(0f, -0.8f) < 1f);

            Section("affordability and contentment");
            Check("break-even reads 0.5", Close(IndicatorsRules.Affordability(0f, 10f), 0.5f));
            Check("a surplus improves affordability", IndicatorsRules.Affordability(5f, 10f) > 0.5f);
            float good = IndicatorsRules.Contentment(0.9f, 0.9f, 0.8f, 1f, 0.9f, 0f, 0f);
            float bad = IndicatorsRules.Contentment(0.2f, 0.3f, 0.2f, 0.3f, 0.2f, 0.6f, 0.6f);
            Check("a comfortable cohort is content", good > 0.7f);
            Check("a poor, oppressed, polluted cohort is not", bad < good);

            Section("substance use");
            Check("drugs are easier to get in rich urban regions",
                IndicatorsRules.DrugAvailability(0.9f, 0.6f) > IndicatorsRules.DrugAvailability(0.1f, 0f));
            Check("discontent + unemployment raise substance use",
                IndicatorsRules.SubstanceUse(0.2f, 0.4f, 0.6f, 0.5f) > IndicatorsRules.SubstanceUse(0.9f, 1f, 0.2f, 1f));
            Check("substance use is capped", IndicatorsRules.SubstanceUse(0f, 0f, 1f, 0f) <= 0.85f + 1e-6f);
            Check("freedom damps substance use",
                IndicatorsRules.SubstanceUse(0.5f, 0.8f, 0.5f, 1f) < IndicatorsRules.SubstanceUse(0.5f, 0.8f, 0.5f, 0f));

            Section("age structure");
            IndicatorsRules.AgeStructure(0.03f, 70, out float ch, out float wk, out float el);
            Check("age shares sum to 1", Close(ch + wk + el, 1f));
            Check("working-age is the majority", wk > ch && wk > el);
            IndicatorsRules.AgeStructure(0.06f, 70, out float ch2, out float _, out float _);
            Check("a higher birth rate means more children", ch2 > ch);
            IndicatorsRules.AgeStructure(0.03f, 95, out float _, out float _, out float el2);
            Check("a longer life means more elders", el2 > el);

            Section("dependency ratio");
            Check("high birth rate raises dependency",
                IndicatorsRules.DependencyRatio(0.06f, 70) > IndicatorsRules.DependencyRatio(0.01f, 70));
            Check("longer life raises dependency (more elders)",
                IndicatorsRules.DependencyRatio(0.03f, 90) > IndicatorsRules.DependencyRatio(0.03f, 45));
            Check("dependency ratio is positive", IndicatorsRules.DependencyRatio(0.03f, 70) > 0f);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL INDICATOR TESTS PASSED" : failures + " INDICATOR TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

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
