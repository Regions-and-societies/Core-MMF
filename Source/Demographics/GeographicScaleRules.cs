using RegionsAndSocieties.Sizing;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>
    /// Geographic scale → density, carrying capacity, food self-sufficiency (DEMOGRAPHIC_MODEL §6). Gives a
    /// region a physical size from its tile count at the <b>district-model scale</b> (a world tile is
    /// <see cref="WorldScaleRules.TileAreaKm2"/> = 23.4 km², #67), then how many people that land can feed:
    /// area × arable fraction (by biome fertility) × yield per arable km² (rises with education). Carrying
    /// capacity lifts the local food by road/trade imports; food self-sufficiency reads 0.5 at break-even.
    ///
    /// <para>At this scale food is rarely the binding constraint — regions sit well below carrying capacity,
    /// so population growth is governed by birth/migration rates rather than starvation (validated in the sim).
    /// Pure and deterministic; the tile area is read from <see cref="WorldScaleRules"/>, never hardcoded.</para>
    /// </summary>
    public static class GeographicScaleRules
    {
        // --- arable land ----------------------------------------------------
        public const float ArableBase = 0.05f;          // a poor biome is ~5% arable
        public const float ArablePerFertility = 0.35f;  // a rich biome climbs toward ~40%
        public const float ArableFloor = 0.02f, ArableCap = 0.45f;

        /// <summary>The fraction of a region that is arable, from biome fertility (0..1).</summary>
        public static float ArableFraction(float biomeFertility)
            => Clamp(ArableBase + ArablePerFertility * biomeFertility, ArableFloor, ArableCap);

        // --- yield ----------------------------------------------------------
        public const float SubsistenceYield = 50f;      // people one arable km² feeds by hand
        public const float IndustrialYieldBonus = 450f; // education lifts it toward ~500

        /// <summary>People one km² of arable land can feed per year, from the skilled-education share
        /// (secondary + undergrad + postgrad, 0..1): subsistence farming feeds far fewer than industrial.</summary>
        public static float PeoplePerArableKm2(float skilledEduShare)
            => SubsistenceYield + IndustrialYieldBonus * Clamp01(skilledEduShare);

        // --- area / capacity ------------------------------------------------
        /// <summary>A region's area in km², at the district-model tile scale.</summary>
        public static float RegionAreaKm2(int tiles)
            => (tiles < 1 ? 1 : tiles) * WorldScaleRules.TileAreaKm2;

        /// <summary>How many people the region's own land can feed: area × arable × yield.</summary>
        public static float FoodCapacity(int tiles, float biomeFertility, float skilledEduShare)
            => RegionAreaKm2(tiles) * ArableFraction(biomeFertility) * PeoplePerArableKm2(skilledEduShare);

        public const float ImportMax = 0.8f;    // roads + a trade/services sector let a region import food
        public const float CarryingFloor = 50f; // even barren land supports a minimum

        /// <summary>Carrying capacity: local food lifted by road/trade imports, floored. Roads and the
        /// services-sector share are 0..1.</summary>
        public static float CarryingCapacity(float foodCapacity, float roads, float sectorServices)
        {
            float importMult = 1f + ImportMax * Clamp01(roads) * Clamp01(sectorServices);
            float k = foodCapacity * importMult;
            return k < CarryingFloor ? CarryingFloor : k;
        }

        /// <summary>Food self-sufficiency, 0..1, where <b>0.5 is break-even</b> (population equals food
        /// capacity); below 0.5 the region is malnourished, at 1 it produces a surplus of 2× its people.</summary>
        public static float FoodSelfSufficiency(float foodCapacity, float population)
        {
            float pop = population < 1f ? 1f : population;
            return Clamp01(foodCapacity / pop / 2f);
        }

        /// <summary>Population density, people per km².</summary>
        public static float DensityPerKm2(float population, int tiles)
        {
            float area = RegionAreaKm2(tiles);
            return area <= 0f ? 0f : population / area;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
