// Behaviour tests for the 0.8 outpost-seeding rules: tier -> allowance, and terrain -> archetype.
//
// Both rule tables are pure — no Find, no Unity — so this suite is plain arithmetic and branching
// over the same numbers the seeding pass feeds them at runtime. What is under test is the mapping,
// which is the part that can actually be wrong.
using System;
using RegionsAndSocieties.Integration;
using RegionsAndSocieties.Sizing;

namespace OutpostRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("outpost allowance ladder (Tier 1 = 2, +1 per tier)");
            Check("a homestead anchors no outposts", OutpostAllowanceRules.OutpostAllowance(SettlementTier.Homestead) == 0);
            Check("Village allows 2", OutpostAllowanceRules.OutpostAllowance(SettlementTier.Hamlet) == 2);
            Check("Town allows 3", OutpostAllowanceRules.OutpostAllowance(SettlementTier.Village) == 3);
            Check("City allows 4", OutpostAllowanceRules.OutpostAllowance(SettlementTier.Town) == 4);
            Check("Metropolis allows 5", OutpostAllowanceRules.OutpostAllowance(SettlementTier.City) == 5);
            Check("each tier allows exactly one more than the last",
                OutpostAllowanceRules.OutpostAllowance(SettlementTier.Village) - OutpostAllowanceRules.OutpostAllowance(SettlementTier.Hamlet) == 1
                && OutpostAllowanceRules.OutpostAllowance(SettlementTier.Town) - OutpostAllowanceRules.OutpostAllowance(SettlementTier.Village) == 1
                && OutpostAllowanceRules.OutpostAllowance(SettlementTier.City) - OutpostAllowanceRules.OutpostAllowance(SettlementTier.Town) == 1);

            Section("remaining allowance never goes negative");
            Check("an empty Village territory has room for 2", OutpostAllowanceRules.RemainingAllowance(SettlementTier.Hamlet, 0) == 2);
            Check("a Village with one outpost has room for 1", OutpostAllowanceRules.RemainingAllowance(SettlementTier.Hamlet, 1) == 1);
            Check("a Village at its allowance has no room", OutpostAllowanceRules.RemainingAllowance(SettlementTier.Hamlet, 2) == 0);
            Check("a territory over its allowance takes no more (never negative)", OutpostAllowanceRules.RemainingAllowance(SettlementTier.Hamlet, 5) == 0);

            Section("allowance flows from a tier classified out of population");
            // On the district scale (#30) a ~2,000-person settlement holds a village's population, so
            // it classifies as a Village and its territory allows three outposts.
            SettlementTier villageTier = SettlementSizeEvaluator.Classify(WorldObjectKind.Settlement, 2000);
            Check("a ~2,000-pop settlement is a Village", villageTier == SettlementTier.Village);
            Check("...and a Village territory allows 3 outposts", OutpostAllowanceRules.OutpostAllowance(villageTier) == 3);

            Section("archetype follows the dominant terrain signal");
            Check("mountainous ground is a mine",
                OutpostArchetypeRules.Choose(Features(hilliness: 3)) == OutpostArchetype.Mining);
            Check("large hills are a mine",
                OutpostArchetypeRules.Choose(Features(hilliness: 2)) == OutpostArchetype.Mining);
            Check("mineral-rich flat ground is a mine",
                OutpostArchetypeRules.Choose(Features(mineralsFraction: 0.8f)) == OutpostArchetype.Mining);
            Check("forest is a logging camp",
                OutpostArchetypeRules.Choose(Features(treeDensity: 0.6f)) == OutpostArchetype.Logging);
            Check("fertile flat land is a farm",
                OutpostArchetypeRules.Choose(Features(plantDensity: 0.7f, hilliness: 0)) == OutpostArchetype.Farming);
            Check("game-rich open land is a hunting camp",
                OutpostArchetypeRules.Choose(Features(animalDensity: 0.7f)) == OutpostArchetype.Hunting);
            Check("barren, featureless ground falls back to an encampment",
                OutpostArchetypeRules.Choose(Features()) == OutpostArchetype.Encampment);

            Section("archetype priority: rock beats vegetation");
            Check("a forested mountain is mined, not logged",
                OutpostArchetypeRules.Choose(Features(hilliness: 3, treeDensity: 0.9f)) == OutpostArchetype.Mining);
            Check("fertile hills that are not steep still farm",
                OutpostArchetypeRules.Choose(Features(plantDensity: 0.7f, hilliness: 1)) == OutpostArchetype.Farming);
            Check("fertile land on steep ground is mined, not farmed",
                OutpostArchetypeRules.Choose(Features(plantDensity: 0.9f, hilliness: 2)) == OutpostArchetype.Mining);

            Section("position- and faction-aware archetype (#18 weighted scorer)");
            // No anchor context (anchorTier None) degrades to the terrain-only chain — the tests above.
            Check("no anchor context degrades to terrain only",
                OutpostArchetypeRules.Choose(Features(hilliness: 3)) == OutpostArchetype.Mining);

            // A fertile tile at a capital's core: a tribal capital farms it; an industrial capital makes
            // it a civic post instead.
            Check("tribal capital core, fertile -> Farming",
                OutpostArchetypeRules.Choose(Features(plantDensity: 0.7f, hilliness: 0, distanceToAnchor: 0.1f, anchorTier: SettlementTier.Village, techLevel: 2)) == OutpostArchetype.Farming);
            Check("industrial capital core, fertile -> a civic post, not a farm",
                OutpostArchetypeRules.Choose(Features(plantDensity: 0.7f, hilliness: 0, distanceToAnchor: 0.1f, anchorTier: SettlementTier.Town, techLevel: 4)) != OutpostArchetype.Farming);

            // The periphery is for extraction and defence, not civic work.
            Check("industrial frontier mountains -> Mining",
                OutpostArchetypeRules.Choose(Features(hilliness: 3, plantDensity: 0.3f, distanceToAnchor: 0.9f, anchorTier: SettlementTier.Town, techLevel: 4)) == OutpostArchetype.Mining);

            // Faction gate: a tribe cannot field the industrial-tech posts.
            Check("tribal frontier mountains -> Mining (never an industrial post)",
                OutpostArchetypeRules.Choose(Features(hilliness: 3, plantDensity: 0.3f, distanceToAnchor: 0.9f, anchorTier: SettlementTier.Village, techLevel: 2)) == OutpostArchetype.Mining);

            // Raiders read the land differently: salvage in the interior, fortlets on the frontier, never civic.
            Check("pirate frontier -> Defensive",
                OutpostArchetypeRules.Choose(Features(plantDensity: 0.3f, distanceToAnchor: 0.9f, anchorTier: SettlementTier.Town, techLevel: 4, permanentEnemy: true)) == OutpostArchetype.Defensive);
            Check("pirate core -> Scavenging, not a civic post",
                OutpostArchetypeRules.Choose(Features(plantDensity: 0.7f, hilliness: 0, distanceToAnchor: 0.1f, anchorTier: SettlementTier.Town, techLevel: 4, permanentEnemy: true)) == OutpostArchetype.Scavenging);

            Section("tier pyramid — triangular thresholds, and a homestead costs one settlement");
            Check("T0 (homestead) needs 1 settlement, not zero", TierPyramidRules.TerritoriesForTier(0) == 1);
            Check("T1 (hamlet) needs 3", TierPyramidRules.TerritoriesForTier(1) == 3);
            Check("T2 (village) needs 6", TierPyramidRules.TerritoriesForTier(2) == 6);
            Check("T3 (town) needs 10", TierPyramidRules.TerritoriesForTier(3) == 10);
            Check("T4 (city, the capital tier) needs 15", TierPyramidRules.TerritoriesForTier(4) == 15);
            Check("a negative rung costs nothing rather than going negative", TierPyramidRules.TerritoriesForTier(-2) == 0);

            Section("max capital tier a settlement count affords");
            Check("1 settlement → a homestead capital", TierPyramidRules.MaxCapitalTier(1) == 0);
            Check("2 → still a homestead (a hamlet needs 3)", TierPyramidRules.MaxCapitalTier(2) == 0);
            Check("3 → T1 hamlet", TierPyramidRules.MaxCapitalTier(3) == 1);
            Check("5 → still T1", TierPyramidRules.MaxCapitalTier(5) == 1);
            Check("6 → T2 village", TierPyramidRules.MaxCapitalTier(6) == 2);
            Check("9 → still T2", TierPyramidRules.MaxCapitalTier(9) == 2);
            Check("10 → T3 town", TierPyramidRules.MaxCapitalTier(10) == 3);
            Check("14 → still T3 (a city needs 15)", TierPyramidRules.MaxCapitalTier(14) == 3);
            Check("15 → T4 city", TierPyramidRules.MaxCapitalTier(15) == 4);
            Check("100 → capped at T4", TierPyramidRules.MaxCapitalTier(100) == 4);

            Section("tier counts — bottom-heavy staircase, each tier one wider");
            Check("N=15 is the exact 5-4-3-2-1 pyramid", CountsAre(TierPyramidRules.TierCounts(15), 5, 4, 3, 2, 1));
            Check("N=3 → 2 homesteads under 1 hamlet", CountsAre(TierPyramidRules.TierCounts(3), 2, 1, 0, 0, 0));
            Check("N=1 → a lone homestead", CountsAre(TierPyramidRules.TierCounts(1), 1, 0, 0, 0, 0));
            Check("N=16 → the extra widens the base, not a second apex", CountsAre(TierPyramidRules.TierCounts(16), 6, 4, 3, 2, 1));
            Check("counts always sum to N", Sum(TierPyramidRules.TierCounts(23)) == 23 && Sum(TierPyramidRules.TierCounts(7)) == 7);
            Check("every tier is at least one wider than the one above", ValidPyramid(TierPyramidRules.TierCounts(23)) && ValidPyramid(TierPyramidRules.TierCounts(15)) && ValidPyramid(TierPyramidRules.TierCounts(2)));

            Section("tier by protection rank — the capital is rank 0");
            var p15 = TierPyramidRules.TierCounts(15);
            Check("most protected is the City capital", TierPyramidRules.TierForRank(0, p15) == SettlementTier.City);
            Check("ranks 1-2 are Towns", TierPyramidRules.TierForRank(1, p15) == SettlementTier.Town && TierPyramidRules.TierForRank(2, p15) == SettlementTier.Town);
            Check("ranks 3-5 are Villages", TierPyramidRules.TierForRank(3, p15) == SettlementTier.Village && TierPyramidRules.TierForRank(5, p15) == SettlementTier.Village);
            Check("ranks 6-9 are Hamlets", TierPyramidRules.TierForRank(6, p15) == SettlementTier.Hamlet && TierPyramidRules.TierForRank(9, p15) == SettlementTier.Hamlet);
            Check("ranks 10-14 are Homesteads", TierPyramidRules.TierForRank(10, p15) == SettlementTier.Homestead && TierPyramidRules.TierForRank(14, p15) == SettlementTier.Homestead);
            Check("a rank past the last settlement falls back to homestead too", TierPyramidRules.TierForRank(15, p15) == SettlementTier.Homestead);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL OUTPOST-RULE TESTS PASSED" : failures + " OUTPOST-RULE TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static bool CountsAre(int[] c, int t0, int t1, int t2, int t3, int t4)
        {
            return c[0] == t0 && c[1] == t1 && c[2] == t2 && c[3] == t3 && c[4] == t4;
        }

        private static int Sum(int[] c)
        {
            int s = 0;
            for (int i = 0; i < c.Length; i++) s += c[i];
            return s;
        }

        private static bool ValidPyramid(int[] c)
        {
            for (int t = 0; t < TierPyramidRules.MaxTier; t++)
            {
                if (c[t + 1] > 0 && c[t] < c[t + 1] + 1) return false;
            }
            return true;
        }

        private static TileFeatures Features(int hilliness = 0, float plantDensity = 0f, float treeDensity = 0f,
            float animalDensity = 0f, float mineralsFraction = 0f, bool coastal = false,
            float distanceToAnchor = 0f, SettlementTier anchorTier = SettlementTier.Homestead,
            int techLevel = 4, bool permanentEnemy = false)
        {
            return new TileFeatures
            {
                hilliness = hilliness,
                plantDensity = plantDensity,
                treeDensity = treeDensity,
                animalDensity = animalDensity,
                mineralsFraction = mineralsFraction,
                coastal = coastal,
                distanceToAnchor = distanceToAnchor,
                anchorTier = anchorTier,
                techLevel = techLevel,
                permanentEnemy = permanentEnemy
            };
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
