// Behaviour tests for the population-dynamics tuning math (#36): migration distance falloff, the
// movable floor, the colony ceiling, and bounded accretion. Pure.
using System;
using RegionsAndSocieties.Integration;

namespace PopulationDynamicsRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("distance falloff");
            Check("pull is full at the colony", Close(PopulationDynamicsRules.DistanceFalloff(0), 1f));
            Check("pull decays with hops",
                PopulationDynamicsRules.DistanceFalloff(1) > PopulationDynamicsRules.DistanceFalloff(2)
                && PopulationDynamicsRules.DistanceFalloff(2) > PopulationDynamicsRules.DistanceFalloff(5));
            Check("pull stays in (0,1]",
                PopulationDynamicsRules.DistanceFalloff(3) > 0f && PopulationDynamicsRules.DistanceFalloff(3) <= 1f);
            Check("a lower retention decays faster",
                PopulationDynamicsRules.DistanceFalloff(3, 0.3f) < PopulationDynamicsRules.DistanceFalloff(3, 0.7f));

            Section("movable floor");
            Check("population above the floor is movable", Close(PopulationDynamicsRules.Movable(100f, 5f), 95f));
            Check("nothing below the floor moves", Close(PopulationDynamicsRules.Movable(3f, 5f), 0f));

            Section("colony ceiling");
            Check("an empty colony has room", PopulationDynamicsRules.ColonyRoom(1000f, 0f, 4f) > 0f);
            Check("room shrinks as the colony fills",
                PopulationDynamicsRules.ColonyRoom(1000f, 1000f, 4f) < PopulationDynamicsRules.ColonyRoom(1000f, 0f, 4f));
            Check("a full colony has no room", Close(PopulationDynamicsRules.ColonyRoom(1000f, 3000f, 4f), 0f));
            Check("an over-full colony never reads negative", PopulationDynamicsRules.ColonyRoom(1000f, 9999f, 4f) >= 0f);

            Section("bounded accretion");
            Check("a neighbour with room takes the full step", Close(PopulationDynamicsRules.AccretionInto(20f, 100f, 100f, 1.5f), 20f));
            Check("accretion is clipped to the remaining cap room",
                Close(PopulationDynamicsRules.AccretionInto(40f, 140f, 100f, 1.5f), 10f));   // cap 150, room 10
            Check("a capped-out neighbour accretes nothing", Close(PopulationDynamicsRules.AccretionInto(20f, 150f, 100f, 1.5f), 0f));
            Check("a zero step accretes nothing", Close(PopulationDynamicsRules.AccretionInto(0f, 10f, 100f, 1.5f), 0f));

            Section("composition: migration is conserving in spirit (falloff never amplifies)");
            float movable = PopulationDynamicsRules.Movable(200f, 5f);
            float pull = 0.05f * movable * PopulationDynamicsRules.DistanceFalloff(2);
            Check("a distant region moves less than its flat share", pull < 0.05f * movable);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL POP-DYNAMICS TESTS PASSED" : failures + " POP-DYNAMICS TEST(S) FAILED");
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
