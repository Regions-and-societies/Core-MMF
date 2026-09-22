// Behaviour tests for per-settlement demographic variation (#74): deterministic, bounded perturbation of a
// faction baseline so two settlements of the same faction differ measurably. Pure.
using System;
using RegionsAndSocieties.Demographics;

namespace SettlementCompositionRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("deviation is deterministic and bounded");
            Check("same seed+salt -> same deviation",
                SettlementCompositionRules.Deviation(42, 3, 0.35f) == SettlementCompositionRules.Deviation(42, 3, 0.35f));
            Check("deviation stays within [1-spread, 1+spread]",
                SettlementCompositionRules.Deviation(42, 3, 0.35f) >= 0.65f - 1e-4f && SettlementCompositionRules.Deviation(42, 3, 0.35f) <= 1.35f + 1e-4f);
            Check("different settlements differ",
                SettlementCompositionRules.Deviation(1, 3, 0.35f) != SettlementCompositionRules.Deviation(2, 3, 0.35f));

            Section("weight perturbation shifts the mix but preserves the total");
            float[] baseW = { 0.70f, 0.20f, 0.07f, 0.03f };
            float[] a = SettlementCompositionRules.PerturbWeights(baseW, 1001);
            float[] b = SettlementCompositionRules.PerturbWeights(baseW, 2002);
            Check("the total is preserved", Close(Sum(a), Sum(baseW)) && Close(Sum(b), Sum(baseW)));
            Check("the input is untouched", Close(baseW[0], 0.70f));
            Check("all weights stay non-negative", a[0] >= 0f && a[3] >= 0f);
            Check("two settlements of the same faction get MEASURABLY different mixes", MaxDiff(a, b) > 0.02f);
            Check("it is deterministic (same seed -> same mix)",
                Close(a[0], SettlementCompositionRules.PerturbWeights(baseW, 1001)[0]));
            Check("a dominant race stays roughly dominant (bounded, not scrambled)", a[0] > a[1] && a[0] > a[2]);

            Section("wealth perturbation");
            int w1 = SettlementCompositionRules.PerturbWealth(500, 1001);
            int w2 = SettlementCompositionRules.PerturbWealth(500, 2002);
            Check("wealth stays within ±spread", w1 >= 400 && w1 <= 600 && w2 >= 400 && w2 <= 600);
            Check("two settlements differ in wealth", w1 != w2);
            Check("wealth is deterministic", w1 == SettlementCompositionRules.PerturbWealth(500, 1001));

            Section("edge cases");
            Check("null weights -> empty", SettlementCompositionRules.PerturbWeights(null, 1).Length == 0);
            Check("zero wealth stays zero", SettlementCompositionRules.PerturbWealth(0, 5) == 0);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL SETTLEMENT COMPOSITION TESTS PASSED" : failures + " SETTLEMENT COMPOSITION TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static float Sum(float[] a) { float s = 0f; for (int i = 0; i < a.Length; i++) s += a[i]; return s; }
        private static float MaxDiff(float[] a, float[] b) { float m = 0f; for (int i = 0; i < a.Length; i++) { float d = Math.Abs(a[i] - b[i]); if (d > m) m = d; } return m; }
        private static bool Close(float a, float b) => Math.Abs(a - b) < 0.0025f;
        private static void Section(string name) { Console.WriteLine(); Console.WriteLine("-- " + name); }
        private static void Check(string label, bool ok)
        {
            if (!ok) failures++;
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label);
        }
    }
}
