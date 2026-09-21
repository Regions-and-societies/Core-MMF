// Behaviour tests for colony -> regional influence (#81): the player's treatment of a xenotype becomes
// an acceptance signal that relaxes into the region and spreads to neighbours. Pure.
using System;
using RegionsAndSocieties.Demographics;

namespace ColonyInfluenceRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("treatment signal");
            Check("all-free reads fully welcoming", Close(ColonyInfluenceRules.TreatmentSignal(4, 0), 1f));
            Check("all-enslaved reads fully hostile", Close(ColonyInfluenceRules.TreatmentSignal(0, 4), -1f));
            Check("none of that xenotype = no signal", Close(ColonyInfluenceRules.TreatmentSignal(0, 0), 0f));
            Check("a mix reads in between", Close(ColonyInfluenceRules.TreatmentSignal(3, 1), 0.5f));
            Check("freeing tips it positive vs enslaving",
                ColonyInfluenceRules.TreatmentSignal(3, 1) > ColonyInfluenceRules.TreatmentSignal(1, 3));

            Section("relaxation toward a target");
            Check("a neutral region moves toward a welcoming example",
                ColonyInfluenceRules.Relax(0f, 1f, 0.15f) > 0f && ColonyInfluenceRules.Relax(0f, 1f, 0.15f) < 1f);
            Check("relaxation is bounded", ColonyInfluenceRules.Relax(0.95f, 5f, 1f) <= ColonyInfluenceRules.MaxAcceptance + 1e-4f);
            Check("a reached target holds", Close(ColonyInfluenceRules.Relax(1f, 1f, 0.15f), 1f));

            Section("convergence: repeated acceptance builds up, enslavement tears down");
            float accept = 0f;
            for (int y = 0; y < 40; y++) accept = ColonyInfluenceRules.Relax(accept, 1f, ColonyInfluenceRules.SelfRelaxRate);
            Check("decades of freeing a xenotype make the region welcoming", accept > 0.9f);
            float hostile = 0f;
            for (int y = 0; y < 40; y++) hostile = ColonyInfluenceRules.Relax(hostile, -1f, ColonyInfluenceRules.SelfRelaxRate);
            Check("decades of enslaving make it hostile", hostile < -0.9f);

            Section("spread: a neighbour lags the source (slower rate)");
            float source = 1f, neighbour = 0f;
            for (int y = 0; y < 10; y++) neighbour = ColonyInfluenceRules.Relax(neighbour, source, ColonyInfluenceRules.SpreadRate);
            Check("the neighbour has begun to adopt it", neighbour > 0f);
            Check("but still trails the source after a decade", neighbour < source);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL COLONY-INFLUENCE TESTS PASSED" : failures + " COLONY-INFLUENCE TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static bool Close(float a, float b) => Math.Abs(a - b) < 0.0025f;
        private static void Section(string name) { Console.WriteLine(); Console.WriteLine("-- " + name); }
        private static void Check(string label, bool ok)
        {
            if (!ok) failures++;
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label);
        }
    }
}
