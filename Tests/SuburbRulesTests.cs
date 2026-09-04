// Behaviour tests for the suburb-spread rule (0.3.0): a settlement's people are split between its
// own tile and the rings around it, thinning outward, with the total conserved exactly. Pure, no game.
using System;
using RegionsAndSocieties.Demographics;

namespace SuburbRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("conservation");
            float centre = SuburbRules.Distribute(200f, new[] { 6, 12 }, out float[] rings);
            float total = centre + rings[0] * 6 + rings[1] * 12;
            Check($"a 200-person city with full rings still totals 200 (got {total:0.###})", Close(total, 200f, 0.001f));
            Check("the centre keeps at least half", centre >= 100f - 0.001f);
            Check($"ring 1 holds more per tile than ring 2 ({rings[0]:0.#} vs {rings[1]:0.#})", rings[0] > rings[1] && rings[1] > 0f);
            Check("ring 2 tiles weigh half of ring 1 tiles", Close(rings[1], rings[0] * SuburbRules.RingDecay, 0.001f));
            Check($"suburb tiles are visible, not rounding dust (ring 1 = {rings[0]:0.#})", rings[0] >= 5f);

            Section("partial rings: what the rings cannot take stays on the centre");
            float c2 = SuburbRules.Distribute(200f, new[] { 3, 0 }, out float[] r2);
            Check("total conserved with only three eligible neighbours", Close(c2 + r2[0] * 3, 200f, 0.001f));
            Check("an empty ring gets nothing", r2[1] == 0f);
            Check("the centre absorbs the rest (more than with full rings)", c2 > centre);
            float c3 = SuburbRules.Distribute(200f, new[] { 0, 0 }, out float[] r3);
            Check("no eligible tile at all: everything stays on the centre", c3 == 200f && r3[0] == 0f && r3[1] == 0f);

            Section("no suburbs");
            float c4 = SuburbRules.Distribute(30f, null, out float[] r4);
            Check("null rings (radius 0, e.g. an outpost) keep the whole count on the tile", c4 == 30f && r4.Length == 0);
            float c5 = SuburbRules.Distribute(30f, new int[0], out _);
            Check("empty rings likewise", c5 == 30f);
            float c6 = SuburbRules.Distribute(0f, new[] { 6 }, out float[] r6);
            Check("zero population places nothing anywhere", c6 == 0f && r6[0] == 0f);

            Section("village vs metropolis footprint");
            float v = SuburbRules.Distribute(30f, new[] { 6 }, out float[] vr);
            Check($"a village of 30 keeps 15 and puts 2.5 on each neighbour (centre {v:0.#}, ring {vr[0]:0.##})", Close(v, 15f, 0.01f) && Close(vr[0], 2.5f, 0.01f));
            float m = SuburbRules.Distribute(300f, new[] { 6, 12, 18, 24 }, out float[] mr);
            Check("a metropolis of 300 thins over four rings, each half the last", mr[0] > mr[1] && mr[1] > mr[2] && mr[2] > mr[3] && mr[3] > 0f);
            Check("metropolis total conserved", Close(m + mr[0] * 6 + mr[1] * 12 + mr[2] * 18 + mr[3] * 24, 300f, 0.001f));

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL SUBURB TESTS PASSED" : failures + " SUBURB TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static bool Close(float a, float b, float tol) => Math.Abs(a - b) <= tol;
        private static void Section(string s) { Console.WriteLine(); Console.WriteLine("-- " + s); }
        private static void Check(string label, bool ok) { if (!ok) failures++; Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label); }
    }
}
