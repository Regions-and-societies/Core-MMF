// Behaviour tests for sub-faction splitting (#57): the eligibility gate, section count, deterministic
// geographic grouping, and the latitude/longitude direction labels. Pure, no game.
using System;
using System.Collections.Generic;
using RegionsAndSocieties.Placement;

namespace SubFactionRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            const int neolithic = 2, industrial = 4, spacer = 5, ultra = 6;
            int tribeCap = ClusteringRules.DefaultClusterSize(FactionKind.Tribe);       // 5
            int roughCap = ClusteringRules.DefaultClusterSize(FactionKind.RoughUnion);  // 7
            int pirateCap = ClusteringRules.DefaultClusterSize(FactionKind.Pirate);     // 3
            int otherCap = ClusteringRules.DefaultClusterSize(FactionKind.Other);       // 9 (unbounded)

            Section("who splits: capped AND below spacer tech, two or more bodies");
            Check("a scattered tribe splits", SubFactionRules.ShouldSplit(neolithic, tribeCap, 3));
            Check("a scattered rough union splits", SubFactionRules.ShouldSplit(industrial, roughCap, 2));
            Check("a single-body tribe does not", !SubFactionRules.ShouldSplit(neolithic, tribeCap, 1));
            Check("pirates (spacer tech) do not, even scattered", !SubFactionRules.ShouldSplit(spacer, pirateCap, 5));
            Check("the Empire (ultra tech) does not", !SubFactionRules.ShouldSplit(ultra, 3, 4));
            Check("an unbounded faction does not", !SubFactionRules.ShouldSplit(industrial, otherCap, 4));
            Check("a low-tech unbounded faction still does not (no finite cap)", !SubFactionRules.ShouldSplit(neolithic, ClusteringRules.Unbounded, 4));

            Section("section count: at most 2-3, never more than bodies");
            Check("2 bodies -> 2 sections", SubFactionRules.SectionCount(2, SubFactionRules.MaxSections) == 2);
            Check("3 bodies -> 3", SubFactionRules.SectionCount(3, SubFactionRules.MaxSections) == 3);
            Check("7 bodies -> capped at 3", SubFactionRules.SectionCount(7, SubFactionRules.MaxSections) == 3);
            Check("1 body -> 1", SubFactionRules.SectionCount(1, SubFactionRules.MaxSections) == 1);
            Check("MaxSections is 3", SubFactionRules.MaxSections == 3);

            Section("grouping: two far clusters land in two sections");
            // Four bodies: two near the north pole (+Y), two near the south (-Y).
            var ns = new List<GeoPoint>
            {
                new GeoPoint(0.1, 0.9, 0.0), new GeoPoint(-0.1, 0.92, 0.05),   // north pair
                new GeoPoint(0.05, -0.9, 0.0), new GeoPoint(-0.05, -0.93, 0.1), // south pair
            };
            var a2 = SubFactionRules.AssignSections(ns, 2);
            Check("the two north bodies share a section", a2[0] == a2[1]);
            Check("the two south bodies share a section", a2[2] == a2[3]);
            Check("north and south are different sections", a2[0] != a2[2]);
            Check("deterministic: same input, same assignment", Same(a2, SubFactionRules.AssignSections(ns, 2)));
            Check("one section requested -> everything in 0", All0(SubFactionRules.AssignSections(ns, 1)));

            Section("grouping: three clusters -> three sections");
            var three = new List<GeoPoint>
            {
                new GeoPoint(0.0, 0.95, 0.0), new GeoPoint(0.05, 0.93, 0.02),  // north
                new GeoPoint(0.9, -0.2, 0.0), new GeoPoint(0.92, -0.25, 0.0),  // east-ish
                new GeoPoint(-0.9, -0.2, 0.0), new GeoPoint(-0.93, -0.15, 0.0),// west-ish
            };
            var a3 = SubFactionRules.AssignSections(three, 3);
            Check("north pair together", a3[0] == a3[1]);
            Check("east pair together", a3[2] == a3[3]);
            Check("west pair together", a3[4] == a3[5]);
            Check("three distinct sections", a3[0] != a3[2] && a3[0] != a3[4] && a3[2] != a3[4]);

            Section("labels: a north-south split reads South / North");
            // Section 0 is the northern group, section 1 the southern (index order deliberately reversed).
            var nsCentroids = new List<GeoPoint> { new GeoPoint(0, 0.9, 0), new GeoPoint(0, -0.9, 0) };
            var nsLabels = SubFactionRules.SectionLabels(nsCentroids);
            Check("northern section labelled North", nsLabels[0] == "North");
            Check("southern section labelled South", nsLabels[1] == "South");

            Section("labels: an east-west split reads West / East");
            var ewCentroids = new List<GeoPoint> { new GeoPoint(0.9, 0.0, 0), new GeoPoint(-0.9, 0.0, 0) };
            var ewLabels = SubFactionRules.SectionLabels(ewCentroids);
            Check("eastern (high X) section labelled East", ewLabels[0] == "East");
            Check("western (low X) section labelled West", ewLabels[1] == "West");

            Section("labels: three north-south sections gain a Central");
            var three3 = new List<GeoPoint> { new GeoPoint(0, 0.9, 0), new GeoPoint(0, 0.0, 0), new GeoPoint(0, -0.9, 0) };
            var l3 = SubFactionRules.SectionLabels(three3);
            Check("top is North", l3[0] == "North");
            Check("middle is Central", l3[1] == "Central");
            Check("bottom is South", l3[2] == "South");
            Check("all labels distinct", l3[0] != l3[1] && l3[1] != l3[2] && l3[0] != l3[2]);
            Check("goodwill constant is friendly kin, not merged", SubFactionRules.LooseKinGoodwill > 0 && SubFactionRules.LooseKinGoodwill < 100);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL SUB-FACTION TESTS PASSED" : failures + " SUB-FACTION TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static bool Same(int[] a, int[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
        private static bool All0(int[] a) { foreach (int v in a) if (v != 0) return false; return true; }
        private static void Section(string s) { Console.WriteLine(); Console.WriteLine("-- " + s); }
        private static void Check(string label, bool ok) { if (!ok) failures++; Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label); }
    }
}
