// Behaviour tests for the per-cohort wealth decomposition (#58 §5): income by sector, the cost-of-living
// treadmill, assets, and the emergent relative-poverty / widening-inequality shapes. Ported from the sim. Pure.
using System;
using RegionsAndSocieties.Demographics;

namespace WealthRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("income source by education and standing");
            float wm;
            Check("the educated go to services", WealthRules.SourceFor(0.8f, 0.7f, 0f, out wm) == IncomeSource.Services && wm > 1.4f);
            Check("mid-education is skilled trades", WealthRules.SourceFor(0.45f, 0.7f, 0f, out wm) == IncomeSource.Manufacturing);
            Check("the drug-dependent fall to extraction", WealthRules.SourceFor(0.3f, 0.7f, 0.5f, out wm) == IncomeSource.Extraction && wm < 0.8f);
            Check("the low-standing fall to extraction", WealthRules.SourceFor(0.3f, 0.2f, 0f, out wm) == IncomeSource.Extraction);
            Check("everyone else farms", WealthRules.SourceFor(0.3f, 0.7f, 0f, out wm) == IncomeSource.Agriculture);

            Section("income responds to the cohort's lot");
            float wmA, wmB;
            WealthRules.SourceFor(0.8f, 0.7f, 0f, out wmA);
            WealthRules.SourceFor(0.2f, 0.7f, 0f, out wmB);
            Check("more education earns more", WealthRules.Income(0.8f, wmA, 1f, 0.7f, 0f) > WealthRules.Income(0.2f, wmB, 1f, 0.7f, 0f));
            Check("unemployment cuts income", WealthRules.Income(0.5f, 1f, 0.4f, 0.7f, 0f) < WealthRules.Income(0.5f, 1f, 1f, 0.7f, 0f));
            Check("slavery docks income", WealthRules.Income(0.5f, 1f, 1f, 0.7f, 0.8f) < WealthRules.Income(0.5f, 1f, 1f, 0.7f, 0f));

            Section("cost of living rises with regional prosperity (the treadmill)");
            Check("a richer region costs more to live in",
                WealthRules.CostOfLiving(0.9f, 0.5f, 0.5f, 0f) > WealthRules.CostOfLiving(0.2f, 0.5f, 0.5f, 0f));
            Check("high-standing roles carry a premium",
                WealthRules.CostOfLiving(0.6f, 0.5f, 0.9f, 0f) > WealthRules.CostOfLiving(0.6f, 0.5f, 0.5f, 0f));
            Check("a drug dependency adds to the cost",
                WealthRules.CostOfLiving(0.5f, 0.5f, 0.5f, 0.6f) > WealthRules.CostOfLiving(0.5f, 0.5f, 0.5f, 0f));

            Section("assets accrue and never go into debt");
            Check("a positive net banks", WealthRules.AccrueAssets(100f, 2f) > 100f);
            Check("assets floor at zero (no debt)", WealthRules.AccrueAssets(10f, -50f) == 0f);
            Check("a prior debt is treated as zero", WealthRules.AccrueAssets(-999f, 1f) == 30f);

            Section("emergent: relative poverty in a prosperous region");
            // A low earner (uneducated, low standing, unemployed-ish) in a rich region can't afford the lifestyle.
            var poor = WealthRules.Compute(0.15f, 0.25f, 0f, 0f, 0.9f, 0.6f, 0f);
            Check("the poor cohort dissaves (net negative)", poor.netDaily < 0f);
            Check("and stays at zero assets", poor.assets == 0f);
            var rich = WealthRules.Compute(0.85f, 0.8f, 0f, 0f, 0.9f, 1f, 0f);
            Check("a high earner in the same region banks a surplus", rich.netDaily > 0f && rich.assets > 0f);

            Section("emergent: widening inequality compounds over years");
            float richAssets = 0f, poorAssets = 0f;
            for (int y = 0; y < 20; y++)
            {
                richAssets = WealthRules.Compute(0.85f, 0.8f, 0f, 0f, 0.9f, 1f, richAssets).assets;
                poorAssets = WealthRules.Compute(0.15f, 0.25f, 0f, 0f, 0.9f, 0.6f, poorAssets).assets;
            }
            Check("the rich cohort compounds", richAssets > 0f);
            Check("the poor cohort never starts", poorAssets == 0f);
            Check("the gap is real", richAssets > poorAssets);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL WEALTH TESTS PASSED" : failures + " WEALTH TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

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
