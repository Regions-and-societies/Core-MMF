// Behaviour tests for social stratification & balance (#29): the shape of the education distribution,
// growth capacity from the skilled middle, polarisation, and bottom-heavy decapitation. Pure.
using System;
using RegionsAndSocieties.Demographics;

namespace StratificationRulesTests
{
    public static class Program
    {
        private static int failures;
        private const int Ill = 0, Pri = 1, Sec = 2, Und = 3, Post = 4;

        // a balanced society (broad skilled middle) and a polarised one (elite over a mass of illiterates)
        private static float[] Balanced() => new float[] { 0.08f, 0.22f, 0.38f, 0.24f, 0.08f };
        private static float[] Polarised() => new float[] { 0.55f, 0.15f, 0.08f, 0.07f, 0.15f };

        public static int Main()
        {
            Section("skilled middle & growth capacity");
            Check("balanced has a bigger skilled middle than polarised",
                StratificationRules.SkilledMiddleShare(Balanced()) > StratificationRules.SkilledMiddleShare(Polarised()));
            Check("a balanced society grows at (near) full capacity", StratificationRules.GrowthCapacity(Balanced()) > 0.95f);
            Check("a polarised society is throttled", StratificationRules.GrowthCapacity(Polarised()) < 0.7f);
            Check("capacity never drops below the floor",
                StratificationRules.GrowthCapacity(new float[] { 1f, 0f, 0f, 0f, 0f }) >= StratificationRules.MinGrowthCapacity - 1e-4f);
            Check("capacity never exceeds 1", StratificationRules.GrowthCapacity(new float[] { 0f, 0f, 0.5f, 0.5f, 0f }) <= 1f + 1e-4f);

            Section("balance index");
            Check("balanced reads high", StratificationRules.BalanceIndex(Balanced()) > 0.8f);
            Check("polarised reads low", StratificationRules.BalanceIndex(Polarised()) < 0.4f);

            Section("polarise: stratification hollows the middle into a bimodal shape");
            float[] baseD = Balanced();
            float[] mild = new float[5], hard = new float[5];
            StratificationRules.Polarize(baseD, 0.3f, mild);
            StratificationRules.Polarize(baseD, 1.0f, hard);
            Check("the skilled middle shrinks as stratification rises",
                StratificationRules.SkilledMiddleShare(hard) < StratificationRules.SkilledMiddleShare(mild)
                && StratificationRules.SkilledMiddleShare(mild) < StratificationRules.SkilledMiddleShare(baseD));
            Check("the underclass swells", hard[Ill] > baseD[Ill]);
            Check("a thin elite grows too (bimodal)", hard[Post] > baseD[Post]);
            Check("still a distribution (sums to 1)", Close(Sum(hard), 1f) && Close(Sum(mild), 1f));
            Check("stratification therefore throttles growth",
                StratificationRules.GrowthCapacity(hard) < StratificationRules.GrowthCapacity(baseD));
            Check("zero stratification leaves the shape unchanged",
                Close(StratificationRules.SkilledMiddleShare(Zeroed(baseD)), StratificationRules.SkilledMiddleShare(baseD)));

            Section("decapitation: bottom-heavy shift (elite flight)");
            float[] bh = new float[5];
            StratificationRules.ShiftBottomHeavy(Balanced(), 1f, bh);
            Check("the top strata collapse", bh[Post] < Balanced()[Post] && bh[Und] < Balanced()[Und]);
            Check("the bottom swells", bh[Ill] > Balanced()[Ill]);
            Check("a decapitated region is a worse economy", StratificationRules.GrowthCapacity(bh) < StratificationRules.GrowthCapacity(Balanced()));
            Check("still a distribution", Close(Sum(bh), 1f));

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL STRATIFICATION TESTS PASSED" : failures + " STRATIFICATION TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static float[] Zeroed(float[] d) { var o = new float[5]; StratificationRules.Polarize(d, 0f, o); return o; }
        private static float Sum(float[] a) { float s = 0f; for (int i = 0; i < a.Length; i++) s += a[i]; return s; }
        private static bool Close(float a, float b) => Math.Abs(a - b) < 0.0025f;
        private static void Section(string name) { Console.WriteLine(); Console.WriteLine("-- " + name); }
        private static void Check(string label, bool ok)
        {
            if (!ok) failures++;
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label);
        }
    }
}
