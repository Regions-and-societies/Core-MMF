// Behaviour tests for territory clustering (#46): the 1/3/5/7/9+ stops, faction-kind defaults,
// within-cap-first ranking with overflow only as a fallback, body tracking with merges, and the
// ascending seeding key. Pure, no game.
using System;
using System.Collections.Generic;
using RegionsAndSocieties.Placement;

namespace ClusteringRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            const int neolithic = 2, industrial = 4, spacer = 5;

            Section("stops and snapping");
            Check("9 is unbounded", ClusteringRules.IsUnbounded(9) && ClusteringRules.IsUnbounded(12) && !ClusteringRules.IsUnbounded(7));
            Check("unset (0) snaps to unbounded", ClusteringRules.Snap(0) == ClusteringRules.Unbounded);
            Check("stops snap to themselves", ClusteringRules.Snap(1) == 1 && ClusteringRules.Snap(3) == 3 && ClusteringRules.Snap(5) == 5 && ClusteringRules.Snap(7) == 7 && ClusteringRules.Snap(9) == 9);
            Check("even values snap to a neighbour stop", ClusteringRules.Snap(2) == 1 || ClusteringRules.Snap(2) == 3);
            Check("4 -> 3 or 5", ClusteringRules.Snap(4) == 3 || ClusteringRules.Snap(4) == 5);
            Check("above 9 -> 9", ClusteringRules.Snap(15) == 9);
            Check("labels", ClusteringRules.Label(3) == "3" && ClusteringRules.Label(9) == "9+ (no limit)");

            Section("faction kinds and their defaults (owner's table)");
            Check("pirate gang -> Pirate", ClusteringRules.ClassifyKind("Pirate", "pirate gang", spacer, true, true) == FactionKind.Pirate);
            Check("waster pirates -> Pirate", ClusteringRules.ClassifyKind("PirateWaster", "waster pirates", industrial, true, true) == FactionKind.Pirate);
            Check("Empire -> Empire", ClusteringRules.ClassifyKind("Empire", "empire", spacer, false, false) == FactionKind.Empire);
            Check("gentle tribe -> Tribe", ClusteringRules.ClassifyKind("TribeCivil", "gentle tribe", neolithic, false, false) == FactionKind.Tribe);
            Check("fierce tribe -> Tribe", ClusteringRules.ClassifyKind("TribeSavage", "savage tribe", neolithic, true, true) == FactionKind.Tribe);
            Check("rough outlander union -> RoughUnion", ClusteringRules.ClassifyKind("OutlanderRough", "rough outlander union", industrial, false, false) == FactionKind.RoughUnion);
            Check("rough pig union -> RoughUnion", ClusteringRules.ClassifyKind("OutlanderRoughPig", "rough pig union", industrial, false, true) == FactionKind.RoughUnion);
            Check("modded hostile industrial (not permanent enemy) -> RoughUnion", ClusteringRules.ClassifyKind("VFE_Warlords", "warlords", industrial, false, true) == FactionKind.RoughUnion);
            Check("civil union -> Other", ClusteringRules.ClassifyKind("OutlanderCivil", "civil outlander union", industrial, false, false) == FactionKind.Other);
            Check("traders guild -> Other", ClusteringRules.ClassifyKind("TradersGuild", "traders guild", spacer, false, false) == FactionKind.Other);
            Check("null names survive", ClusteringRules.ClassifyKind(null, null, spacer, false, false) == FactionKind.Other);
            Check("pirates 3", ClusteringRules.DefaultClusterSize(FactionKind.Pirate) == 3);
            Check("Empire 3", ClusteringRules.DefaultClusterSize(FactionKind.Empire) == 3);
            Check("tribes 5", ClusteringRules.DefaultClusterSize(FactionKind.Tribe) == 5);
            Check("rough unions 7", ClusteringRules.DefaultClusterSize(FactionKind.RoughUnion) == 7);
            Check("everyone else no limit", ClusteringRules.DefaultClusterSize(FactionKind.Other) == ClusteringRules.Unbounded);
            Check("no default uses stop 1", ClusteringRules.DefaultClusterSize(FactionKind.Pirate) > 1 && ClusteringRules.DefaultClusterSize(FactionKind.Tribe) > 1);

            Section("cap: merged size and within-cap");
            Check("alone -> 1", ClusteringRules.MergedSize(new int[0]) == 1 && ClusteringRules.MergedSize(null) == 1);
            Check("touching one body of 2 -> 3", ClusteringRules.MergedSize(new[] { 2 }) == 3);
            Check("bridging two bodies of 2 -> 5", ClusteringRules.MergedSize(new[] { 2, 2 }) == 5);
            Check("3 within cap 3", ClusteringRules.WithinCap(3, 3));
            Check("4 overflows cap 3", !ClusteringRules.WithinCap(4, 3));
            Check("cap 1: only a lone province is within", ClusteringRules.WithinCap(1, 1) && !ClusteringRules.WithinCap(2, 1));
            Check("9+ never overflows", ClusteringRules.WithinCap(40, 9));
            Check("rank: within-cap first", ClusteringRules.CapRank(true) < ClusteringRules.CapRank(false));

            Section("ranking: within-cap beats any score; overflow only when nothing else is left");
            // Candidates as (withinCap, score); the placement sort is (CapRank asc, score desc).
            var cands = new List<(bool within, float score, string name)>
            {
                (false, 0.95f, "rich-but-overflows"),
                (true, 0.40f, "poor-detached"),
                (true, 0.55f, "ok-detached"),
                (false, 0.80f, "good-but-overflows"),
            };
            cands.Sort((a, b) => {
                int r = ClusteringRules.CapRank(a.within).CompareTo(ClusteringRules.CapRank(b.within));
                return r != 0 ? r : b.score.CompareTo(a.score);
            });
            Check("best within-cap candidate wins over a richer overflow", cands[0].name == "ok-detached");
            Check("second is the other within-cap one", cands[1].name == "poor-detached");
            Check("overflow candidates trail, best first", cands[2].name == "rich-but-overflows" && cands[3].name == "good-but-overflows");
            var onlyOverflow = new List<(bool within, float score, string name)> { (false, 0.2f, "a"), (false, 0.7f, "b") };
            onlyOverflow.Sort((a, b) => {
                int r = ClusteringRules.CapRank(a.within).CompareTo(ClusteringRules.CapRank(b.within));
                return r != 0 ? r : b.score.CompareTo(a.score);
            });
            Check("with no within-cap option the best overflow is taken (fill in)", onlyOverflow[0].name == "b");

            Section("bodies: connected components under land adjacency, merged through a bridge");
            var bodies = new TerritoryBodies();
            Check("empty: 0 bodies", bodies.Count == 0 && bodies.Largest == 0);
            bodies.Add(1, new[] { 2, 3 });          // nothing held yet -> new body
            Check("first province is its own body", bodies.Count == 1 && bodies.Largest == 1);
            bodies.Add(10, new[] { 11 });           // detached -> second body
            Check("detached province starts a second body", bodies.Count == 2);
            Check("candidate touching body{1} would make 2", bodies.MergedSizeIfAdded(new[] { 1, 99 }) == 2);
            Check("candidate touching nothing would be 1", bodies.MergedSizeIfAdded(new[] { 50 }) == 1);
            Check("candidate bridging both bodies would make 3", bodies.MergedSizeIfAdded(new[] { 1, 10 }) == 3);
            bodies.Add(5, new[] { 1, 10 });         // bridge
            Check("bridge merges the two bodies", bodies.Count == 1 && bodies.Largest == 3 && bodies.ProvinceCount == 3);
            bodies.Add(5, new[] { 1 });
            Check("re-adding is a no-op", bodies.ProvinceCount == 3);
            bodies.Add(20, null);
            Check("null neighbours -> new body", bodies.Count == 2 && bodies.Contains(20));
            // Order independence: build the same shape in another order.
            var other = new TerritoryBodies();
            other.Add(5, new[] { 1, 10 });
            other.Add(10, new[] { 5, 11 });
            other.Add(1, new[] { 5, 2, 3 });
            Check("same bodies whatever the add order", other.Count == 1 && other.Largest == 3);

            Section("seeding order: ascending cluster size");
            var order = new List<int> { 9, 5, 3, 7, 0 };
            order.Sort((a, b) => ClusteringRules.SeedingKey(a).CompareTo(ClusteringRules.SeedingKey(b)));
            Check("3s, then 5s, 7s, then the unbounded (incl. unset)", order[0] == 3 && order[1] == 5 && order[2] == 7 && ClusteringRules.SeedingKey(order[3]) == 9 && ClusteringRules.SeedingKey(order[4]) == 9);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL CLUSTERING TESTS PASSED" : failures + " CLUSTERING TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static void Section(string s) { Console.WriteLine(); Console.WriteLine("-- " + s); }
        private static void Check(string label, bool ok) { if (!ok) failures++; Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label); }
    }
}
