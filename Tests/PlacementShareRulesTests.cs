// Behaviour tests for the placement-share model (#47): range→share migration, normalised fractions,
// the per-faction estimate, and largest-remainder apportionment. Pure, no game.
using System;
using RegionsAndSocieties.Placement;

namespace PlacementShareRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("baseCountRange → share weight migration = midpoint");
            Check("civil 5..15 -> 10", Eq(PlacementShareRules.MigrateRangeToShareWeight(5, 15), 10f));
            Check("hostile 3..8 -> 5.5", Eq(PlacementShareRules.MigrateRangeToShareWeight(3, 8), 5.5f));
            Check("proportions preserved (civil > hostile)",
                PlacementShareRules.MigrateRangeToShareWeight(5, 15) > PlacementShareRules.MigrateRangeToShareWeight(3, 8));
            Check("degenerate negative -> 0", Eq(PlacementShareRules.MigrateRangeToShareWeight(-4, -2), 0f));

            Section("normalised fraction = weight / total");
            Check("10 of 40 -> 0.25", Eq(PlacementShareRules.NormalizedFraction(10f, 40f), 0.25f));
            Check("zero total -> 0", Eq(PlacementShareRules.NormalizedFraction(10f, 0f), 0f));
            Check("zero weight -> 0", Eq(PlacementShareRules.NormalizedFraction(0f, 40f), 0f));

            Section("placed total = expected regions × claimed-land fraction");
            Check("448 × 0.5 -> 224", PlacementShareRules.PlacedTotal(448, 0.5f) == 224);
            Check("fraction clamped to 1", PlacementShareRules.PlacedTotal(100, 2f) == 100);
            Check("negative fraction -> 0", PlacementShareRules.PlacedTotal(100, -1f) == 0);
            Check("zero regions -> 0", PlacementShareRules.PlacedTotal(0, 0.5f) == 0);

            Section("per-faction estimate = fraction × placed total");
            // share 40 of 100 total, 200 placed -> 80.
            Check("40% of 200 placed -> 80", PlacementShareRules.EstimatedFactionCount(40f, 100f, 200) == 80);
            Check("zero placed -> 0", PlacementShareRules.EstimatedFactionCount(40f, 100f, 0) == 0);
            Check("zero total -> 0", PlacementShareRules.EstimatedFactionCount(40f, 0f, 200) == 0);

            Section("largest-remainder apportionment sums exactly and honours share");
            int[] a = PlacementShareRules.Apportion(new float[] { 40f, 40f, 20f }, 100);
            Check("40/40/20 of 100 -> 40,40,20", a[0] == 40 && a[1] == 40 && a[2] == 20);
            Check("apportionment sums to total", Sum(a) == 100);

            int[] b = PlacementShareRules.Apportion(new float[] { 1f, 1f, 1f }, 10);
            Check("three equal shares of 10 sum to 10", Sum(b) == 10);
            Check("equal shares within one of each other", Max(b) - Min(b) <= 1);

            int[] c = PlacementShareRules.Apportion(new float[] { 40f, 0f, 60f }, 50);
            Check("zero-weight faction gets nothing", c[1] == 0);
            Check("nonzero split sums to total", Sum(c) == 50 && c[0] > 0 && c[2] > 0);

            int[] d = PlacementShareRules.Apportion(new float[] { 1f, 1f }, 0);
            Check("zero total -> all zero", Sum(d) == 0);
            int[] e = PlacementShareRules.Apportion(new float[] { 0f, 0f }, 10);
            Check("all-zero weights -> all zero (no false placement)", Sum(e) == 0);

            // The acceptance shape: share 40% receives ≈ 40% of placed, ±1.
            int[] f = PlacementShareRules.Apportion(new float[] { 40f, 35f, 25f }, 137);
            Check("share 40% -> ~40% of 137 (55) ±1", Math.Abs(f[0] - 55) <= 1);
            Check("acceptance apportionment sums exactly", Sum(f) == 137);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL PLACEMENT-SHARE TESTS PASSED" : failures + " PLACEMENT-SHARE TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static bool Eq(float a, float b) => Math.Abs(a - b) < 0.0001f;
        private static int Sum(int[] a) { int s = 0; foreach (int x in a) s += x; return s; }
        private static int Max(int[] a) { int m = int.MinValue; foreach (int x in a) if (x > m) m = x; return m; }
        private static int Min(int[] a) { int m = int.MaxValue; foreach (int x in a) if (x < m) m = x; return m; }

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
