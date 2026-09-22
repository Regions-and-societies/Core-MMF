// Behaviour tests for the settlement-history legacy sizing (#35). Pure.
using System;
using RegionsAndSocieties.Demographics;

namespace SettlementHistoryRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("legacy population from site points");
            Check("a tiny (0-point) site still leaves a homestead's worth",
                SettlementHistoryRules.LegacyPopulation(0f) == SettlementHistoryRules.BaseLegacyPopulation);
            Check("a bigger site leaves more", SettlementHistoryRules.LegacyPopulation(1000f) > SettlementHistoryRules.LegacyPopulation(100f));
            Check("it is capped below a real settlement",
                SettlementHistoryRules.LegacyPopulation(99999f) <= SettlementHistoryRules.MaxLegacyPopulation);
            Check("negative points are treated as zero",
                SettlementHistoryRules.LegacyPopulation(-50f) == SettlementHistoryRules.BaseLegacyPopulation);
            Check("even a large single site stays below a village-scale settlement",
                SettlementHistoryRules.MaxLegacyPopulation < 700);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL SETTLEMENT HISTORY TESTS PASSED" : failures + " SETTLEMENT HISTORY TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static void Section(string name) { Console.WriteLine(); Console.WriteLine("-- " + name); }
        private static void Check(string label, bool ok)
        {
            if (!ok) failures++;
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label);
        }
    }
}
