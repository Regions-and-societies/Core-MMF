// Behaviour tests for the deterministic dwelling core: how a population resolves into homes, their
// occupancy, and the land each takes, driven by how urban a place is. Scaled to the district model
// (#30) and reading the shared SettlementTier ladder. Pure, so it runs without a game.
using System;
using RegionsAndSocieties.Demographics;
using RegionsAndSocieties.Sizing;

namespace ResidenceRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("urbanization rises from rural to city");
            Check("rural floor is 0", Close(ResidenceRules.Urbanization(ResidenceRules.RuralPopulation), 0f));
            Check("below the floor is 0", Close(ResidenceRules.Urbanization(30), 0f));
            Check("city ceiling is 1", Close(ResidenceRules.Urbanization(ResidenceRules.CityPopulation), 1f));
            Check("above the ceiling is 1", Close(ResidenceRules.Urbanization(99999), 1f));
            Check("midpoint is between", ResidenceRules.Urbanization(3000) > 0.05f && ResidenceRules.Urbanization(3000) < 0.95f);
            Check("monotonic in population", ResidenceRules.Urbanization(700) < ResidenceRules.Urbanization(3700));

            Section("the endpoints are the district scale (#30)");
            Check("fully rural at a homestead (100)", ResidenceRules.RuralPopulation == 100);
            Check("fully urban at a city (6,100)", ResidenceRules.CityPopulation == 6100);

            Section("occupancy falls as it urbanises (extended -> nuclear)");
            Check("rural occupancy is the extended family", Close(ResidenceRules.Occupancy(0f), ResidenceRules.RuralOccupancy));
            Check("urban occupancy is nuclear", Close(ResidenceRules.Occupancy(1f), ResidenceRules.UrbanOccupancy));
            Check("occupancy is monotonically lower when more urban", ResidenceRules.Occupancy(0.2f) > ResidenceRules.Occupancy(0.8f));
            Check("rural holds more per home than urban", ResidenceRules.RuralOccupancy > ResidenceRules.UrbanOccupancy);

            Section("dwellings derive from population and occupancy");
            var country = ResidenceRules.For(700);    // a hamlet
            var city = ResidenceRules.For(6100);       // a city
            Check("empty population has no dwellings", ResidenceRules.For(0).dwellings == 0);
            Check("a person always has at least one home", ResidenceRules.For(1).dwellings >= 1);
            Check("country reads as a hamlet", country.tier == SettlementTier.Hamlet);
            Check("city reads as a city", city.tier == SettlementTier.City);
            Check("city: many small homes", city.dwellings >= 1000);
            Check("city has more homes per person than country",
                (city.dwellings / (float)city.population) > (country.dwellings / (float)country.population));
            Check("dwellings x occupancy reconstructs population (country)", Math.Abs(country.dwellings * country.occupancy - country.population) <= country.occupancy);
            Check("dwellings x occupancy reconstructs population (city)", Math.Abs(city.dwellings * city.occupancy - city.population) <= city.occupancy);

            Section("land shrinks and packs in as it urbanises");
            Check("rural dwelling has a wide plot", Close(ResidenceRules.LandPerDwelling(0f), ResidenceRules.RuralLandPerDwelling));
            Check("urban dwelling has a tight lot", Close(ResidenceRules.LandPerDwelling(1f), ResidenceRules.UrbanLandPerDwelling));
            Check("dwelling land shrinks with urbanization", ResidenceRules.LandPerDwelling(0.2f) > ResidenceRules.LandPerDwelling(0.8f));
            Check("fewer tiles per pawn in the city", country.landPerPawn > city.landPerPawn);

            Section("tier reads off the shared ladder (no duplicate residence ladder)");
            Check("a homestead-sized place is a Homestead", ResidenceRules.For(100).tier == SettlementTier.Homestead);
            Check("a village-sized place is a Village", ResidenceRules.For(1900).tier == SettlementTier.Village);
            Check("a town-sized place is a Town", ResidenceRules.For(3700).tier == SettlementTier.Town);
            Check("tier rises with population", (int)ResidenceRules.For(700).tier < (int)ResidenceRules.For(3700).tier);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL RESIDENCE TESTS PASSED" : failures + " RESIDENCE TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static bool Close(float a, float b) => Math.Abs(a - b) < 0.0005f;

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
