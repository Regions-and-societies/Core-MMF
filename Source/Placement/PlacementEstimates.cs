using System;

namespace RegionsAndSocieties.Placement
{
    /// <summary>
    /// One shared estimate of how many land regions a world will have, from the target region size (#54).
    /// The header of the placement settings and #47's per-faction "share % → ≈ N regions" read the SAME
    /// number here, so they never disagree.
    ///
    /// <para>The border-first partition cuts each biome container into ~ceil(size / target) honeycomb
    /// cells, so the region count is ≈ landTiles / targetSize, scaled by a measured <c>fill</c> factor that
    /// absorbs the two things the raw ratio misses: biome-size weights (sparse biomes make fewer, larger
    /// regions) and the 0.4.0 post-passes (lakes shared out, islands absorbed, tiny regions dropped). Pure
    /// and unit-tested; the impure side supplies the real land-tile count (from the grid when a world
    /// exists, else an estimate from planet size × land fraction).</para>
    /// </summary>
    public static class PlacementEstimates
    {
        /// <summary>Measured region-count fill: actual regions / (landTiles / targetSize). ~1.0 on the
        /// mixed biomemix test world (441 regions vs 67149/150 = 448). A desert- or ice-heavy world runs
        /// lower (bigger sparse regions); the band below carries that spread. Re-measure across the RP2
        /// matrix (#54 acceptance) and bake the median here.</summary>
        public const float DefaultFill = 1.0f;

        /// <summary>± band on the estimate, the #54 acceptance tolerance and the biome-mix spread.</summary>
        public const float FillSpread = 0.15f;

        /// <summary>A rough land fraction (land tiles / all tiles) for the PRE-generation estimate only,
        /// when no grid exists to count. Measured ~0.56 on biomemix; real worlds vary widely with sea
        /// level, so this is only used before a world is generated and is shown to the player as an
        /// assumption. Once the world exists the dialog counts real tiles (and then the region count is
        /// known exactly, no estimate needed).</summary>
        public const float TypicalLandFraction = 0.5f;

        /// <summary>Expected land-region count for a world of <paramref name="landTiles"/> land tiles cut at
        /// <paramref name="targetSize"/> tiles per region, scaled by <paramref name="fill"/>. Rounded, never
        /// negative.</summary>
        public static int ExpectedRegionCount(int landTiles, int targetSize, float fill)
        {
            if (landTiles <= 0 || targetSize <= 0) return 0;
            if (fill <= 0f) fill = DefaultFill;
            int n = (int)Math.Round((double)landTiles / targetSize * fill);
            return n < 0 ? 0 : n;
        }

        /// <summary>The estimate at the default fill.</summary>
        public static int ExpectedRegionCount(int landTiles, int targetSize)
        {
            return ExpectedRegionCount(landTiles, targetSize, DefaultFill);
        }

        /// <summary>Low end of the ± band.</summary>
        public static int ExpectedRegionCountLow(int landTiles, int targetSize)
        {
            return ExpectedRegionCount(landTiles, targetSize, DefaultFill * (1f - FillSpread));
        }

        /// <summary>High end of the ± band.</summary>
        public static int ExpectedRegionCountHigh(int landTiles, int targetSize)
        {
            return ExpectedRegionCount(landTiles, targetSize, DefaultFill * (1f + FillSpread));
        }

        /// <summary>Land tiles implied by a planet's total tile count and a land fraction — the PRE-gen
        /// fallback when no grid exists to count directly.</summary>
        public static int EstimateLandTiles(int totalTiles, float landFraction)
        {
            if (totalTiles <= 0) return 0;
            if (landFraction <= 0f) landFraction = TypicalLandFraction;
            if (landFraction > 1f) landFraction = 1f;
            return (int)Math.Round(totalTiles * (double)landFraction);
        }
    }
}
