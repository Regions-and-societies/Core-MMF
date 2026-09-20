// Behaviour tests for reproduction & inheritance (#58 §8): endogamy from tolerance, the logistic birth cap,
// and the germline inheritance rules (implanted -> baseliner, cross-germline -> hybrid). Ported. Pure.
using System;
using RegionsAndSocieties.Demographics;

namespace ReproductionRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("endogamy rises with xenophobia");
            Check("a tolerant region mixes more (low endogamy)", ReproductionRules.Endogamy(1f) < 0.6f);
            Check("a xenophobic region stays separate (high endogamy)", ReproductionRules.Endogamy(-1f) > 0.9f);
            Check("more hostile => more endogamy", ReproductionRules.Endogamy(-1f) > ReproductionRules.Endogamy(1f));
            Check("clamped within band", ReproductionRules.Endogamy(-5f) <= 0.95f + 1e-6f && ReproductionRules.Endogamy(5f) >= 0.35f - 1e-6f);

            Section("logistic birth cap");
            Check("an empty region has full room (~1)", ReproductionRules.LogisticFactor(10f, 10000f) > 0.99f);
            Check("at carrying capacity there is no room (0)", Math.Abs(ReproductionRules.LogisticFactor(1000f, 1000f)) < 1e-6f);
            Check("overpopulation goes negative, floored", ReproductionRules.LogisticFactor(3000f, 1000f) == -0.5f);

            Section("births / deaths / migration");
            Check("births scale with population and rate",
                ReproductionRules.Births(1000f, 0.04f, 20f, 1f) > ReproductionRules.Births(500f, 0.04f, 20f, 1f));
            Check("infant mortality cuts live births",
                ReproductionRules.Births(1000f, 0.04f, 300f, 1f) < ReproductionRules.Births(1000f, 0.04f, 0f, 1f));
            Check("an over-capacity region has no births (not anti-births)",
                ReproductionRules.Births(1000f, 0.04f, 20f, -0.5f) == 0f);
            Check("deaths scale with the mortality hazard", ReproductionRules.Deaths(1000f, 0.03f) == 30f);
            Check("net migration can be negative", ReproductionRules.NetMigration(1000f, -0.02f) < 0f);

            Section("within-group inheritance");
            Check("a germline cohort breeds true", ReproductionRules.ChildOfSameGroup(true) == ChildOutcome.ParentA);
            Check("an implanted xenotype does NOT (sanguophage -> baseliner)", ReproductionRules.ChildOfSameGroup(false) == ChildOutcome.Baseliner);

            Section("cross-group inheritance (the germline rules)");
            Check("two implanted xenotypes -> baseliner",
                ReproductionRules.ChildOfPairing(false, false, false, false) == ChildOutcome.Baseliner);
            Check("baseliner x germline -> the germline",
                ReproductionRules.ChildOfPairing(true, true, true, false) == ChildOutcome.ParentB);
            Check("germline x baseliner -> the germline",
                ReproductionRules.ChildOfPairing(true, false, true, true) == ChildOutcome.ParentA);
            Check("two distinct germlines -> hybrid",
                ReproductionRules.ChildOfPairing(true, false, true, false) == ChildOutcome.Hybrid);
            Check("one heritable, one implanted -> the heritable one",
                ReproductionRules.ChildOfPairing(true, false, false, false) == ChildOutcome.ParentA);
            Check("implanted x heritable -> the heritable one",
                ReproductionRules.ChildOfPairing(false, false, true, false) == ChildOutcome.ParentB);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL REPRODUCTION TESTS PASSED" : failures + " REPRODUCTION TEST(S) FAILED");
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
