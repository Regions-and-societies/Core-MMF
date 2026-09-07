// Behaviour tests for the shore-proportional inland-lake split (#48): shore counts, largest-remainder
// quotas, and a capacity-bounded flood whose region shares match the quotas exactly. Pure, no game — the
// BFS runs on a hand-built line/patch adjacency, the same code the hex world drives.
using System;
using System.Collections.Generic;
using System.Linq;
using RegionsAndSocieties.Partition;

namespace LakeSplitRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("shore counts and dominance");
            var shore = LakeSplitRules.ShoreCounts(
                new List<int> { 0, 1, 8 },
                new Dictionary<int, List<int>> {
                    { 0, new List<int> { 1 } }, { 1, new List<int> { 1 } }, { 8, new List<int> { 3 } } });
            Check("region 1 borders 2 tiles", shore[1] == 2);
            Check("region 3 borders 1 tile", shore[3] == 1);
            Check("dominant is region 1 (most shore)", LakeSplitRules.DominantRegion(shore) == 1);
            var tie = new Dictionary<int, int> { { 5, 3 }, { 2, 3 } };
            Check("dominance tie -> lowest id", LakeSplitRules.DominantRegion(tie) == 2);

            Section("quotas: largest remainder, sum exact");
            var q = LakeSplitRules.Quotas(9, new Dictionary<int, int> { { 1, 2 }, { 3, 1 } });
            Check("9 tiles, shore 2:1 -> 6 and 3", q[1] == 6 && q[3] == 3);
            Check("quotas sum to the lake size", q.Values.Sum() == 9);
            var q2 = LakeSplitRules.Quotas(10, new Dictionary<int, int> { { 1, 1 }, { 2, 1 }, { 3, 1 } });
            Check("10 tiles over 3 equal shores sums to 10", q2.Values.Sum() == 10);
            Check("10/3 split is 4-3-3 by remainder", q2[1] == 4 && q2[2] == 3 && q2[3] == 3);
            // floor-at-1: a long lake with a sliver shore still yields the sliver region a tile.
            var q3 = LakeSplitRules.Quotas(100, new Dictionary<int, int> { { 1, 99 }, { 2, 1 } });
            Check("sliver shore floored to >=1", q3[2] >= 1 && q3.Values.Sum() == 100);

            Section("split: shares match quotas (no leftovers)");
            // A 12-tile line lake; region 1 seeds tile 0, region 2 tile 6, region 3 tile 11.
            var line12 = Line(12);
            var labels12 = new Dictionary<int, List<int>> {
                { 0, new List<int> { 1 } }, { 6, new List<int> { 2 } }, { 11, new List<int> { 3 } } };
            var a12 = LakeSplitRules.Split(Enumerable.Range(0, 12).ToList(), line12, labels12);
            Check("every lake tile assigned", a12.Count == 12);
            Check("even 3-way share -> 4/4/4", Count(a12, 1) == 4 && Count(a12, 2) == 4 && Count(a12, 3) == 4);
            Check("each region's slice is contiguous on the line", Contiguous(a12));

            // Proportional: region 1 borders tiles 0 AND 1 (shore 2), region 3 tile 8 (shore 1).
            var line9 = Line(9);
            var labels9 = new Dictionary<int, List<int>> {
                { 0, new List<int> { 1 } }, { 1, new List<int> { 1 } }, { 8, new List<int> { 3 } } };
            var a9 = LakeSplitRules.Split(Enumerable.Range(0, 9).ToList(), line9, labels9);
            var q9 = LakeSplitRules.Quotas(9, LakeSplitRules.ShoreCounts(Enumerable.Range(0, 9).ToList(), labels9));
            Check("proportional shares match the quotas exactly",
                Count(a9, 1) == q9[1] && Count(a9, 3) == q9[3]);
            Check("dominant (region 1) got the larger half (6 vs 3)", Count(a9, 1) == 6 && Count(a9, 3) == 3);

            Section("pond wholly inside one region");
            var pond = Line(5);
            var pondLabels = new Dictionary<int, List<int>> { { 0, new List<int> { 7 } } };
            var ap = LakeSplitRules.Split(Enumerable.Range(0, 5).ToList(), pond, pondLabels);
            Check("single-shore pond -> all tiles to that region", ap.Count == 5 && ap.Values.All(v => v == 7));

            Section("no shore at all -> empty (caller keeps as water)");
            var none = LakeSplitRules.Split(Enumerable.Range(0, 3).ToList(), Line(3),
                new Dictionary<int, List<int>>());
            Check("no shore labels -> no assignment", none.Count == 0);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL LAKE-SPLIT TESTS PASSED" : failures + " LAKE-SPLIT TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        // A line of n tiles: tile i adjacent to i-1 and i+1.
        private static Dictionary<int, List<int>> Line(int n)
        {
            var adj = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                var l = new List<int>();
                if (i > 0) l.Add(i - 1);
                if (i < n - 1) l.Add(i + 1);
                adj[i] = l;
            }
            return adj;
        }

        private static int Count(Dictionary<int, int> assign, int region)
        {
            int c = 0;
            foreach (var v in assign.Values) if (v == region) c++;
            return c;
        }

        // On a line, each region's tiles should form one unbroken run.
        private static bool Contiguous(Dictionary<int, int> assign)
        {
            var byTile = assign.OrderBy(k => k.Key).Select(k => k.Value).ToList();
            var seen = new HashSet<int>();
            int prev = -999;
            foreach (int r in byTile)
            {
                if (r != prev)
                {
                    if (!seen.Add(r)) return false;   // region reappears after a gap
                    prev = r;
                }
            }
            return true;
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
