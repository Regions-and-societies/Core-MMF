// Behaviour tests for the district model (#69): how a settlement occupies its tile, and how the
// player's own colony is reconciled with the tile's simulated population. Pure, no game.
using System;
using RegionsAndSocieties.Sizing;

namespace DistrictRulesTests
{
    public static class Program
    {
        private static int failures;
        private const int Default = 250;

        public static int Main()
        {
            Section("ring geometry — the centered hexagonal numbers");
            Check("ring 0 is the rendered map alone", DistrictRules.DistrictsInRings(0) == 1);
            Check("ring 1 adds the six neighbours", DistrictRules.DistrictsInRings(1) == 7);
            Check("ring 2 is 19", DistrictRules.DistrictsInRings(2) == 19);
            Check("ring 3 is 37", DistrictRules.DistrictsInRings(3) == 61 - 24);
            Check("ring 4 is 61", DistrictRules.DistrictsInRings(4) == 61);
            Check("negative rings degrade to the single district", DistrictRules.DistrictsInRings(-2) == 1);
            Check("rings for 1 district is 0", DistrictRules.RingsForDistricts(1) == 0);
            Check("rings for 7 is 1", DistrictRules.RingsForDistricts(7) == 1);
            Check("a count between rings rounds up", DistrictRules.RingsForDistricts(8) == 2 && DistrictRules.RingsForDistricts(20) == 3);
            Check("inverse round-trips on exact counts", DistrictRules.RingsForDistricts(DistrictRules.DistrictsInRings(3)) == 3);

            Section("tier to districts");
            Check("Homestead 1", DistrictRules.DistrictsForTier(SettlementTier.Homestead) == 1);
            Check("Village 7", DistrictRules.DistrictsForTier(SettlementTier.Hamlet) == 7);
            Check("Town 19", DistrictRules.DistrictsForTier(SettlementTier.Village) == 19);
            Check("City 37", DistrictRules.DistrictsForTier(SettlementTier.Town) == 37);
            Check("Metropolis 61", DistrictRules.DistrictsForTier(SettlementTier.City) == 61);

            Section("people per district — the measured playtest density");
            // 1,600 people/km2 x 0.0625 km2 = 100 on a default map.
            Check("100 per district at 250x250", Near(DistrictRules.PeoplePerDistrict(250), 100f, 0.5f));
            Check("400 per district at 500x500", Near(DistrictRules.PeoplePerDistrict(500), 400f, 1f));
            Check("rescales with map size, so a bigger map means fewer, fuller districts",
                DistrictRules.PeoplePerDistrict(500) > DistrictRules.PeoplePerDistrict(250));
            Check("a nonsense map size yields nothing", DistrictRules.PeoplePerDistrict(0) == 0f);

            Section("tier populations");
            Check("Homestead ~100", Near(DistrictRules.PopulationForTier(SettlementTier.Homestead, Default), 100, 2));
            Check("Village ~700", Near(DistrictRules.PopulationForTier(SettlementTier.Hamlet, Default), 700, 5));
            Check("Town ~1,900", Near(DistrictRules.PopulationForTier(SettlementTier.Village, Default), 1900, 10));
            Check("City ~3,700", Near(DistrictRules.PopulationForTier(SettlementTier.Town, Default), 3700, 20));
            Check("Metropolis ~6,100", Near(DistrictRules.PopulationForTier(SettlementTier.City, Default), 6100, 30));

            Section("population back to tier and districts");
            Check("50 people is a homestead", DistrictRules.TierForPopulation(50, Default) == SettlementTier.Homestead);
            Check("700 is a village", DistrictRules.TierForPopulation(700, Default) == SettlementTier.Hamlet);
            Check("2,000 is a town", DistrictRules.TierForPopulation(2000, Default) == SettlementTier.Village);
            Check("4,000 is a city", DistrictRules.TierForPopulation(4000, Default) == SettlementTier.Town);
            Check("50,000 tops out at metropolis", DistrictRules.TierForPopulation(50000, Default) == SettlementTier.City);
            Check("tier is monotonic in population",
                (int)DistrictRules.TierForPopulation(300, Default) <= (int)DistrictRules.TierForPopulation(3000, Default));
            Check("250 people needs 3 districts", DistrictRules.DistrictsForPopulation(250, Default) == 3);
            Check("any population at all occupies a district", DistrictRules.DistrictsForPopulation(1, Default) == 1);
            Check("nobody occupies nothing", DistrictRules.DistrictsForPopulation(0, Default) == 0);
            Check("the ring cap bounds it", DistrictRules.DistrictsForPopulation(999999, Default) == 61);
            Check("a wider cap allows more", DistrictRules.DistrictsForPopulation(999999, Default, 6) == 127);

            Section("the tile as a whole — settled cluster plus hinterland");
            Check("a metropolis uses about a sixth of its tile",
                Near(DistrictRules.SettledShareOfTile(61, Default), 0.163f, 0.005f));
            Check("a homestead uses almost none", DistrictRules.SettledShareOfTile(1, Default) < 0.005f);
            Check("every tier leaves most of the tile as hinterland", DistrictRules.SettledShareOfTile(61, Default) < 0.5f);
            Check("share never exceeds the whole tile", DistrictRules.SettledShareOfTile(100000, Default) == 1f);
            Check("hinterland is a quarter again", DistrictRules.HinterlandPopulation(1000) == 250);
            Check("nobody settled means nobody outside", DistrictRules.HinterlandPopulation(0) == 0);
            Check("tile total is settled plus hinterland", DistrictRules.TilePopulation(1000) == 1250);
            // The share must keep the open country sane at both ends of the ladder.
            Check("a homestead's countryside is empty (~1/km2)",
                DistrictRules.HinterlandDensityPerKm2(100, 1, Default) < 2f);
            Check("a metropolis's countryside is farmed (~78/km2)",
                Near(DistrictRules.HinterlandDensityPerKm2(6100, 61, Default), 78f, 5f));

            Section("the player's tier comes from development, not head count");
            Check("an undeveloped map is a homestead", DistrictRules.TierFromDevelopment(0.01f, 1f) == SettlementTier.Homestead);
            Check("a quarter-developed rich map is a town", DistrictRules.TierFromDevelopment(0.25f, 1.5f) == SettlementTier.Village);
            Check("wealth promotes a given footprint",
                (int)DistrictRules.TierFromDevelopment(0.3f, 2.5f) > (int)DistrictRules.TierFromDevelopment(0.3f, 1f));
            Check("a fully built rich map is a metropolis", DistrictRules.TierFromDevelopment(1f, 1.5f) == SettlementTier.City);
            Check("nothing built is a homestead whatever the wealth", DistrictRules.TierFromDevelopment(0f, 10f) == SettlementTier.Homestead);
            Check("absent wealth data is treated as ordinary, not as zero",
                DistrictRules.TierFromDevelopment(0.5f, 0f) == DistrictRules.TierFromDevelopment(0.5f, 1f));

            Section("the player's count is authoritative and never penalised");
            // 40 colonists at Town: 40 on the rendered map + 18 surrounding districts x 100, + hinterland.
            int town40 = DistrictRules.PlayerTilePopulation(40, SettlementTier.Village, Default);
            Check("40 colonists in a town tile reads ~2,300", Near(town40, 2300, 30));
            Check("the colonists are added, never replaced by a modelled number",
                DistrictRules.PlayerTilePopulation(40, SettlementTier.Homestead, Default) >= 40);
            Check("a lone homestead tile is the colony plus its own countryside",
                DistrictRules.PlayerTilePopulation(40, SettlementTier.Homestead, Default) == DistrictRules.TilePopulation(40));
            Check("promotion adds suburbs rather than demanding more colonists",
                DistrictRules.PlayerTilePopulation(40, SettlementTier.Town, Default) > town40);
            Check("more colonists always means more people, at a fixed tier",
                DistrictRules.PlayerTilePopulation(80, SettlementTier.Village, Default) > town40);
            Check("an empty colony still has its surrounding districts",
                DistrictRules.PlayerTilePopulation(0, SettlementTier.Village, Default) > 0);
            Check("a negative count is treated as none", DistrictRules.PlayerTilePopulation(-5, SettlementTier.Village, Default)
                == DistrictRules.PlayerTilePopulation(0, SettlementTier.Village, Default));
            Check("the rendered share is small but stated", DistrictRules.RenderedShare(40, SettlementTier.Village, Default) > 0f
                && DistrictRules.RenderedShare(40, SettlementTier.Village, Default) < 0.05f);
            // The reconciliation the whole model exists for: a developed colony is close to one district.
            Check("a fully built map is within ~2.5x of one district's modelled population",
                DistrictRules.PeoplePerDistrict(Default) / 40f < 3f);

            Section("built radius — what the pressure falloff (#70) scales from");
            Check("one district is a radius of ~0.56 districts", Near(DistrictRules.BuiltRadiusDistricts(1), 0.564f, 0.01f));
            Check("ring k gives about k + 0.5", Near(DistrictRules.BuiltRadiusDistricts(19), 2.46f, 0.05f)
                && Near(DistrictRules.BuiltRadiusDistricts(61), 4.41f, 0.05f));
            Check("no districts, no radius", DistrictRules.BuiltRadiusDistricts(0) == 0f);
            // Radius must go as the square root of population, not linearly: that is the #70 fix.
            float r100 = DistrictRules.BuiltRadiusTiles(100, Default);
            float r400 = DistrictRules.BuiltRadiusTiles(400, Default);
            Check("quadrupling population doubles the radius", Near(r400 / r100, 2f, 0.06f));
            Check("a metropolis is still only a fraction of a tile across",
                DistrictRules.BuiltRadiusTiles(6100, Default) < 0.3f);
            Check("radius grows with population", DistrictRules.BuiltRadiusTiles(6100, Default) > r100);
            Check("nobody has no radius", DistrictRules.BuiltRadiusTiles(0, Default) == 0f);
            Check("an absurd ring count saturates instead of overflowing negative", DistrictRules.DistrictsInRings(1000000) > 0);
            Check("uncapped districts exceed the display cap for a huge population",
                DistrictRules.DistrictsForPopulationUncapped(999999, Default) > DistrictRules.DistrictsForPopulation(999999, Default));

            Section("demographic influence radius (#70) — the fix the old formula needed");
            // The old rule was reach = population x knob, in TILES: a 150-person settlement projected
            // pressure 150 tiles, so the reach-based culling could never fire. Reach must go as sqrt(P).
            float infl = DistrictRules.DefaultInfluenceMultiplier;
            float rVillage = DistrictRules.InfluenceRadiusTiles(700, Default, infl);
            float rTown = DistrictRules.InfluenceRadiusTiles(1900, Default, infl);
            float rCity = DistrictRules.InfluenceRadiusTiles(3700, Default, infl);
            float rMetro = DistrictRules.InfluenceRadiusTiles(6100, Default, infl);
            Check("a village reaches ~2 tiles", Near(rVillage, 2.0f, 0.3f));
            Check("a town reaches ~3.3 tiles", Near(rTown, 3.3f, 0.4f));
            Check("a metropolis reaches ~5.9 tiles", Near(rMetro, 5.9f, 0.6f));
            Check("reach is monotonic in population", rVillage < rTown && rTown < rCity && rCity < rMetro);
            // The property that matters: quadrupling population doubles the radius, it does not quadruple it.
            Check("quadrupling population doubles the reach",
                Near(DistrictRules.InfluenceRadiusTiles(4000, Default, infl) / DistrictRules.InfluenceRadiusTiles(1000, Default, infl), 2f, 0.12f));
            Check("a metropolis is nowhere near the old 150-tile reach", rMetro < 10f);
            Check("even a huge city stays bounded", DistrictRules.InfluenceRadiusTiles(100000, Default, infl) < 30f);

            Check("a hamlet still colours its surroundings, via the floor",
                Near(DistrictRules.InfluenceRadiusTiles(20, Default, infl), DistrictRules.MinInfluenceTiles, 0.001f));
            Check("the floor never exceeds a real settlement's reach", rTown > DistrictRules.MinInfluenceTiles);
            Check("nobody has no reach at all", DistrictRules.InfluenceRadiusTiles(0, Default, infl) == 0f);

            Check("a higher multiplier carries further",
                DistrictRules.InfluenceRadiusTiles(6100, Default, 52f) > rMetro);
            Check("a nonsense multiplier falls back to the default rather than collapsing",
                DistrictRules.InfluenceRadiusTiles(6100, Default, 0f) == rMetro
                && DistrictRules.InfluenceRadiusTiles(6100, Default, -5f) == rMetro);
            Check("the convenience overload matches the explicit default",
                DistrictRules.InfluenceRadiusTiles(6100, Default) == rMetro);
            // The same head count is the same ground whatever the map size - a district is just a
            // different slice of it - so reach is independent of map size bar district rounding.
            Check("reach is the same ground whatever the map size",
                Near(DistrictRules.InfluenceRadiusTiles(6100, 500, infl), rMetro, rMetro * 0.05f));

            // Why the reach fix is also the performance fix: a source only touches the tiles inside its
            // reach, so the hex neighbourhood it must paint is small and bounded.
            Check("a metropolis paints ~100 tiles, not ~70,000",
                (3f * rMetro * (rMetro + 1f) + 1f) < 200f);

            Section("occupancy — the nominal figure is a target, not a ceiling");
            // PopulationForTier is every district built to the measured density, on ordinary ground,
            // at exactly 100% occupancy. Real places sit either side of it.
            int nominalCity = DistrictRules.PopulationForTier(SettlementTier.City, Default);
            Check("at nominal land and occupancy, it is the nominal figure",
                DistrictRules.PopulationAt(SettlementTier.City, Default, 1f, 1f) == nominalCity);
            Check("half the ground buildable, half the people",
                Near(DistrictRules.PopulationAt(SettlementTier.City, Default, 0.5f, 1f), nominalCity / 2, 2));
            Check("crowding multiplies on top of land",
                Near(DistrictRules.PopulationAt(SettlementTier.City, Default, 0.5f, 1.5f), (int)(nominalCity * 0.75f), 3));
            Check("unbuildable ground holds nobody", DistrictRules.PopulationAt(SettlementTier.City, Default, 0f, 1.5f) == 0);
            Check("empty of people holds nobody", DistrictRules.PopulationAt(SettlementTier.City, Default, 1f, 0f) == 0);

            // The crowding ceiling is the growth model's own constant, not a second opinion.
            Check("the ceiling is the birthrate stagnation ratio",
                DistrictRules.MaxOccupancy == BirthrateRules.BirthStagnationRatio);
            Check("a city tops out ~50% above nominal",
                Near(DistrictRules.MaxPopulationForTier(SettlementTier.City, Default), (int)(nominalCity * 1.5f), 3));
            Check("occupancy cannot be pushed past the ceiling",
                DistrictRules.PopulationAt(SettlementTier.City, Default, 1f, 99f)
                == DistrictRules.MaxPopulationForTier(SettlementTier.City, Default));
            Check("buildable fraction cannot exceed whole ground",
                DistrictRules.PopulationAt(SettlementTier.City, Default, 5f, 1f) == nominalCity);

            Section("occupancy read back, and named");
            Check("nominal population reads as 100% occupancy",
                Near(DistrictRules.OccupancyOf(nominalCity, SettlementTier.City, Default, 1f), 1f, 0.01f));
            Check("half the people on half the ground is still full",
                Near(DistrictRules.OccupancyOf(nominalCity / 2, SettlementTier.City, Default, 0.5f), 1f, 0.02f));
            Check("nobody is zero, not a divide by zero",
                DistrictRules.OccupancyOf(0, SettlementTier.City, Default, 1f) == 0f);
            Check("unbuildable ground reports zero rather than infinity",
                DistrictRules.OccupancyOf(500, SettlementTier.City, Default, 0f) == 0f);

            Check("an almost empty district is a ruin candidate",
                DistrictRules.OccupancyBand(0.05f) == DistrictOccupancy.Ruined);
            Check("thinly settled is sparse", DistrictRules.OccupancyBand(0.4f) == DistrictOccupancy.Sparse);
            Check("comfortable is nominal", DistrictRules.OccupancyBand(0.95f) == DistrictOccupancy.Nominal);
            Check("a little over is crowded", DistrictRules.OccupancyBand(1.1f) == DistrictOccupancy.Crowded);
            Check("well over is overcrowded", DistrictRules.OccupancyBand(1.45f) == DistrictOccupancy.Overcrowded);
            Check("the bands run in order across the whole range",
                (int)DistrictRules.OccupancyBand(0f) < (int)DistrictRules.OccupancyBand(0.4f)
                && (int)DistrictRules.OccupancyBand(0.4f) < (int)DistrictRules.OccupancyBand(1f)
                && (int)DistrictRules.OccupancyBand(1f) < (int)DistrictRules.OccupancyBand(1.2f)
                && (int)DistrictRules.OccupancyBand(1.2f) < (int)DistrictRules.OccupancyBand(1.5f));

            Console.WriteLine();
            if (failures == 0) { Console.WriteLine("ALL DISTRICT TESTS PASSED"); return 0; }
            Console.WriteLine(failures + " DISTRICT TEST(S) FAILED");
            return 1;
        }

        private static bool Near(float a, float b, float tolerance)
        {
            return Math.Abs(a - b) <= tolerance;
        }

        private static bool Near(int a, int b, int tolerance)
        {
            return Math.Abs(a - b) <= tolerance;
        }

        private static void Section(string name)
        {
            Console.WriteLine();
            Console.WriteLine("-- " + name);
        }

        private static void Check(string what, bool ok)
        {
            if (!ok) failures++;
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what);
        }
    }
}
