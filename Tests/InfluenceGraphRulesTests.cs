// Behaviour tests for the pure influence-graph step (#58 keystone): curves, scalar hysteresis + inertia,
// distribution chase + normalize, and the cumulative-share ceiling. Ported from Design/sim/graph.js. Pure.
using System;
using RegionsAndSocieties.Demographics;

namespace InfluenceGraphRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("curves");
            Check("linear passes through", Close(InfluenceGraphRules.Curve(InfluenceCurve.Linear, 0.7f), 0.7f));
            Check("saturating diminishes past ~0.5", InfluenceGraphRules.Curve(InfluenceCurve.Saturating, 1f) < 1f
                && Close(InfluenceGraphRules.Curve(InfluenceCurve.Saturating, 1f), 0.5f));
            Check("saturating leaves the sign of a negative alone", InfluenceGraphRules.Curve(InfluenceCurve.Saturating, -0.3f) == -0.3f);
            Check("threshold is zero below the cut", InfluenceGraphRules.Curve(InfluenceCurve.Threshold, 0.3f, 0.5f) == 0f);
            Check("threshold passes above the cut", Close(InfluenceGraphRules.Curve(InfluenceCurve.Threshold, 0.8f, 0.5f), 0.8f));

            Section("scalar step: converges toward the target");
            var d = new FactorDynamics(0.5f, 0.2f, 0.02f, 0.01f, 0.10f);
            var st = new HysteresisState();
            float v = 0.2f;
            float prevGap = 1f;
            for (int i = 0; i < 200; i++)
            {
                v = InfluenceGraphRules.StepScalar(v, 0.8f, d, ref st);
                float gap = Math.Abs(0.8f - v);
                Check2(gap <= prevGap + 1e-4f, "gap never grows while chasing");
                prevGap = gap;
            }
            Check("reaches the target", Math.Abs(v - 0.8f) < 0.02f);

            Section("scalar step: clamps, hysteresis, bounds");
            var st2 = new HysteresisState();
            float step0 = InfluenceGraphRules.StepScalar(0f, 1f, d, ref st2);
            Check("one step is capped at maxStep", step0 <= 0.10f + 1e-6f);
            Check("stays within [0,1] chasing a high target", step0 >= 0f && step0 <= 1f);
            var st3 = new HysteresisState();
            float tiny = InfluenceGraphRules.StepScalar(0.5f, 0.505f, d, ref st3);   // gap 0.005 < deadband 0.02
            Check("a gap inside the deadband does not start motion", tiny == 0.5f && !st3.moving);
            // once moving, it keeps moving down into the releaseBand (harder to start than to stop)
            var st4 = new HysteresisState { moving = true, velocity = 0f };
            float creep = InfluenceGraphRules.StepScalar(0.5f, 0.515f, d, ref st4);   // gap 0.015: > releaseBand, < deadband
            Check("an already-moving factor keeps moving inside the deadband", creep != 0.5f && st4.moving);

            Section("distribution step: chases and stays normalised");
            float[] cur = { 0.6f, 0.3f, 0.1f };
            float[] tgt = { 0.2f, 0.3f, 0.5f };
            float[] vel = new float[3];
            bool moving = false;
            for (int i = 0; i < 400; i++) InfluenceGraphRules.StepDistribution(cur, tgt, d, vel, ref moving);
            Check("distribution sums to 1", Close(cur[0] + cur[1] + cur[2], 1f));
            Check("it moved toward the target (tier 3 rose)", cur[2] > 0.4f);
            Check("and away from the shrinking tier (tier 1 fell)", cur[0] < 0.35f);

            Section("cumulative-share ceiling (slavery education cap, §3)");
            float[] edu = { 0.2f, 0.3f, 0.3f, 0.2f };   // [Primary, Secondary, Undergrad, Postgrad]
            // hold the share at/above Secondary (index 1) to 0.4, spilling the rest into Primary (index 0).
            InfluenceGraphRules.ApplyCeiling(edu, 1, 0.4f, 0);
            float above = edu[1] + edu[2] + edu[3];
            Check("share above the cap is held to the bound", Close(above, 0.4f));
            Check("the excess spilled into the floor tier", edu[0] > 0.2f);
            Check("the distribution still sums to 1", Close(edu[0] + edu[1] + edu[2] + edu[3], 1f));
            // already within bound => untouched
            float[] edu2 = { 0.8f, 0.1f, 0.05f, 0.05f };
            InfluenceGraphRules.ApplyCeiling(edu2, 1, 0.4f, 0);
            Check("a distribution already under the cap is left alone", Close(edu2[0], 0.8f));

            Section("normalize");
            float[] raw = { 2f, 2f, 4f };
            InfluenceGraphRules.Normalize(raw);
            Check("normalizes to shares", Close(raw[0], 0.25f) && Close(raw[2], 0.5f));
            float[] zero = { 0f, 0f };
            InfluenceGraphRules.Normalize(zero);
            Check("a zero distribution is left as-is", zero[0] == 0f && zero[1] == 0f);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL INFLUENCE GRAPH TESTS PASSED" : failures + " INFLUENCE GRAPH TEST(S) FAILED");
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

        // A per-iteration invariant that should not spam a line every loop; only records failures.
        private static void Check2(bool ok, string label)
        {
            if (!ok) { failures++; Console.WriteLine("  FAIL  " + label); }
        }
    }
}
