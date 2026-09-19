using System;
using RegionsAndSocieties.Sizing;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>How a settled population resolves into <b>dwellings</b>. A tile's population is a head
    /// count of people; this derives the dwellings that hold them, their occupancy, and the land each
    /// takes, from a single driver — how urban the place is. Rural land is a homestead of an extended
    /// family on a wide plot; a city is nuclear households packed onto small lots. So as a tile urbanises,
    /// occupancy falls (7 → ~1.8), dwelling count rises for the same population, and land per pawn shrinks.
    ///
    /// <para>Scaled to the district model (#30): fully rural at a homestead's population, fully urban at a
    /// city's, both read from <see cref="DistrictRules"/> so this can never drift from the world scale.
    /// The tier a place reads as is <see cref="DistrictRules.TierForPopulation"/> — <b>one</b> ladder for
    /// the whole world, not a duplicate residence ladder (the old <c>ResidenceTier</c> was dropped in #30
    /// because urbanization is a pure function of population, so it only re-labelled the size axis).</para>
    ///
    /// <para>Pure and deterministic (no game types), so it unit-tests against the hand-written doubles.
    /// Every number below is a tunable endpoint, not a hidden constant.</para></summary>
    public static class ResidenceRules
    {
        /// <summary>People per dwelling in a fully rural place — an extended family under one roof.</summary>
        public const float RuralOccupancy = 7f;
        /// <summary>People per dwelling in a fully urban place — nuclear households and singles.</summary>
        public const float UrbanOccupancy = 1.8f;

        /// <summary>At or below this population a tile reads as fully rural (urbanization 0): a homestead —
        /// the smallest nucleated place — and the dispersed frontier below it. The district homestead
        /// population (100), so the curve is anchored to the world scale rather than an invented number.</summary>
        public static int RuralPopulation => DistrictRules.PopulationForTier(SettlementTier.Homestead);
        /// <summary>At or above this population a tile reads as fully urban (urbanization 1): a city
        /// (6,100 in the district model).</summary>
        public static int CityPopulation => DistrictRules.PopulationForTier(SettlementTier.City);

        /// <summary>Land a dwelling occupies in a fully rural place (relative units — a sprawling plot).</summary>
        public const float RuralLandPerDwelling = 1f;
        /// <summary>Land a dwelling occupies in a fully urban place (relative units — a tight lot).</summary>
        public const float UrbanLandPerDwelling = 0.12f;

        /// <summary>How urban a place is, 0 (rural) to 1 (city), from the population concentrated on it.
        /// Smoothstepped between the rural and city population endpoints so the transition eases at both
        /// ends rather than snapping.</summary>
        public static float Urbanization(int population)
        {
            int rural = RuralPopulation, city = CityPopulation;
            if (city <= rural) return population >= city ? 1f : 0f;
            float t = (population - rural) / (float)(city - rural);
            t = Clamp01(t);
            return t * t * (3f - 2f * t);   // smoothstep
        }

        /// <summary>Average people per dwelling at an urbanization level.</summary>
        public static float Occupancy(float urbanization) => Lerp(RuralOccupancy, UrbanOccupancy, Clamp01(urbanization));

        /// <summary>Relative land a single dwelling occupies at an urbanization level.</summary>
        public static float LandPerDwelling(float urbanization) => Lerp(RuralLandPerDwelling, UrbanLandPerDwelling, Clamp01(urbanization));

        /// <summary>The full dwelling picture for a population: how many homes, how full, how much land,
        /// and the settlement tier it reads as (the shared ladder, via <see cref="DistrictRules"/>).</summary>
        public static ResidenceProfile For(int population)
        {
            if (population <= 0)
                return new ResidenceProfile { population = 0, urbanization = 0f, occupancy = RuralOccupancy, dwellings = 0, landPerDwelling = RuralLandPerDwelling, landPerPawn = RuralLandPerDwelling / RuralOccupancy, tier = SettlementTier.Homestead };

            float u = Urbanization(population);
            float occ = Occupancy(u);
            int dwellings = Math.Max(1, (int)Math.Round(population / occ, MidpointRounding.AwayFromZero));
            float landDwelling = LandPerDwelling(u);
            return new ResidenceProfile
            {
                population = population,
                urbanization = u,
                occupancy = occ,
                dwellings = dwellings,
                landPerDwelling = landDwelling,
                landPerPawn = landDwelling / occ,
                tier = DistrictRules.TierForPopulation(population),
            };
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }

    /// <summary>The derived dwelling picture for a population.</summary>
    public struct ResidenceProfile
    {
        public int population;          // people (the input head count)
        public float urbanization;      // 0 rural .. 1 city
        public int dwellings;           // dwellings that hold them
        public float occupancy;         // average people per dwelling
        public float landPerDwelling;   // relative land one dwelling occupies
        public float landPerPawn;       // relative personal land per person
        public SettlementTier tier;     // the settlement tier this reads as (the shared ladder)
    }
}
