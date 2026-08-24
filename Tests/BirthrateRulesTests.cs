// Behaviour tests for the settlement birthrate-growth core (#6): tech-informed rate + logistic step
// toward the target, clamped and non-oscillating. Pure, so this runs without a game.
using System;
using RegionsAndSocieties.Sizing;

namespace BirthrateRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("annual growth rate by tech level");
            Check("neolithic grows faster than industrial", BirthrateRules.AnnualGrowthRate(2) > BirthrateRules.AnnualGrowthRate(4));
            Check("industrial grows faster than spacer", BirthrateRules.AnnualGrowthRate(4) > BirthrateRules.AnnualGrowthRate(5));
            Check("all rates positive", BirthrateRules.AnnualGrowthRate(2) > 0f && BirthrateRules.AnnualGrowthRate(5) > 0f);
            Check("unknown tech gets a sane default", BirthrateRules.AnnualGrowthRate(99) > 0f);

            Section("one growth step");
            float r = BirthrateRules.AnnualGrowthRate(4);
            float s = BirthrateRules.GrowStep(10f, 100, 150, r, 1f);
            Check("a settlement below target grows", s > 10f);
            Check("...but does not overshoot the target in one step", s <= 100f);
            Check("zero elapsed time -> no change", BirthrateRules.GrowStep(10f, 100, 150, r, 0f) == 10f);
            Check("higher rate grows more in the same step",
                BirthrateRules.GrowStep(10f, 100, 150, 0.05f, 1f) > BirthrateRules.GrowStep(10f, 100, 150, 0.01f, 1f));
            // Logistic shape: growth increment is larger mid-range than near the target.
            float dMid = BirthrateRules.GrowStep(50f, 100, 150, r, 1f) - 50f;
            float dHigh = BirthrateRules.GrowStep(95f, 100, 150, r, 1f) - 95f;
            Check("growth tapers as it fills (mid > near-full)", dMid > dHigh);

            Section("clamping and bounds");
            Check("never exceeds max", BirthrateRules.GrowStep(200f, 100, 150, r, 1f) <= 150f);
            Check("never goes negative", BirthrateRules.GrowStep(-5f, 100, 150, r, 1f) >= 0f);
            Check("zero capacity -> clamped, no growth", BirthrateRules.GrowStep(10f, 0, 150, r, 1f) == 10f);
            // A seed (near zero) can start growing rather than being stuck at zero.
            Check("a near-zero settlement seeds and grows", BirthrateRules.GrowStep(0f, 100, 150, r, 1f) > 0f);

            Section("shrinks when over target");
            float over = BirthrateRules.GrowStep(120f, 100, 150, r, 1f);
            Check("a settlement above target shrinks toward it", over < 120f && over >= 100f);
            Check("...and does not overshoot the target downward", over >= 100f);

            Section("converges to the target over many steps");
            // Enough years for the logistic to fill: at 5%/yr, 5 -> 100 needs a couple of centuries.
            float p = 5f;
            for (int i = 0; i < 800; i++) p = BirthrateRules.GrowStep(p, 100, 150, 0.05f, 0.5f);
            Check($"approaches the target after long run (got {p:0.0})", Close(p, 100f, 1.5f));

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL BIRTHRATE TESTS PASSED" : failures + " BIRTHRATE TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static bool Close(float a, float b, float tol) => Math.Abs(a - b) <= tol;
        private static void Section(string s) { Console.WriteLine(); Console.WriteLine("-- " + s); }
        private static void Check(string label, bool ok) { if (!ok) failures++; Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label); }
    }
}
