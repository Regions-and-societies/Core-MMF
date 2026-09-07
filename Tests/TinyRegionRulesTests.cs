// Behaviour tests for the drop-tiny-regions terminal rule (#51): the 6-tile cap, the settlement
// never-orphan guard, and the drop-anyway rule for a holdingless speck. Pure, so this runs without a game.
using System;
using RegionsAndSocieties.Placement;

namespace TinyRegionRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("cap: only 1-6 tile regions are candidates");
            Check("0 tiles (empty) -> Keep", TinyRegionRules.Resolve(0, false, true) == TinyRegionAction.Keep);
            Check("7 tiles -> Keep (above the cap)", TinyRegionRules.Resolve(7, false, false) == TinyRegionAction.Keep);
            Check("6 tiles at the cap -> Drop", TinyRegionRules.Resolve(6, false, true) == TinyRegionAction.Drop);
            Check("a 200-tile region is never touched", TinyRegionRules.Resolve(200, false, false) == TinyRegionAction.Keep);

            Section("holdingless speck: drop regardless of neighbours");
            Check("1-tile island, no land in reach -> Drop", TinyRegionRules.Resolve(1, false, false) == TinyRegionAction.Drop);
            Check("3-tile crag with a land neighbour -> Drop", TinyRegionRules.Resolve(3, false, true) == TinyRegionAction.Drop);

            Section("never orphan a settlement/outpost");
            Check("tiny region with a holding and a land neighbour -> Fold", TinyRegionRules.Resolve(2, true, true) == TinyRegionAction.Fold);
            Check("tiny region with a holding but no land neighbour -> Keep", TinyRegionRules.Resolve(2, true, false) == TinyRegionAction.Keep);
            Check("a settlement region is never Dropped", TinyRegionRules.Resolve(4, true, true) != TinyRegionAction.Drop
                && TinyRegionRules.Resolve(4, true, false) != TinyRegionAction.Drop);

            Section("ShouldDrop wrapper mirrors Resolve==Drop");
            Check("holdingless tiny -> ShouldDrop true", TinyRegionRules.ShouldDrop(5, false, false));
            Check("settlement tiny -> ShouldDrop false", !TinyRegionRules.ShouldDrop(5, true, true));
            Check("above cap -> ShouldDrop false", !TinyRegionRules.ShouldDrop(9, false, false));
            Check("cap constant is 6", TinyRegionRules.TinyRegionMaxTiles == 6);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL TINY-REGION TESTS PASSED" : failures + " TINY-REGION TEST(S) FAILED");
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
