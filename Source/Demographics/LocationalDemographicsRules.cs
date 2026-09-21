using System;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>
    /// Location-based demographics (#33): a region is not one flat value — its characteristics shift with
    /// where people actually live. Toward population centres people skew younger, better educated,
    /// wealthier and more employed; the rural fringe trends the opposite way. This turns the region's
    /// (population-weighted, #58) aggregate into a per-tile gradient keyed to local dwelling density.
    ///
    /// <para><b>Mean-preserving.</b> The per-tile skew is built so the population-weighted mean of the tile
    /// values equals the region aggregate exactly (in the unclamped range): a tile's factor is
    /// <c>1 + spread·(urbanity − meanUrbanity)</c>, and Σ&#160;pop·(urbanity − meanUrbanity) = 0 by the
    /// definition of the weighted mean. So the region overlay stays the honest summary of its tiles while
    /// each tile reads its own local value — the two never drift apart.</para>
    ///
    /// <para>Pure and dependency-free: the caller supplies each tile's local population and the region's
    /// densest-tile population and population-weighted mean urbanity (both cheap to cache per region).</para>
    /// </summary>
    public static class LocationalDemographicsRules
    {
        // Default spreads = the fraction an axis swings from the region mean at the density extremes. First
        // pass, tunable. Wealth swings most with urbanisation, employment least; age is applied negatively
        // (cities skew younger).
        public const float DefaultWealthSpread = 0.60f;
        public const float DefaultEducationSpread = 0.35f;
        public const float DefaultEmploymentSpread = 0.15f;
        public const float DefaultAgeSpread = 0.20f;

        // A tile's density factor is clamped to this band so an extreme outlier tile can't produce a
        // negative or absurd value; this only bites well past the normal spread, so ordinary regions keep
        // the exact mean-preserving property.
        public const float MinFactor = 0.30f;
        public const float MaxFactor = 2.50f;

        /// <summary>
        /// A tile's urbanity, 0 (most rural in its region) to 1 (the region's densest tile), on a log scale
        /// so density spanning orders of magnitude reads as a smooth gradient rather than one bright dot.
        /// Any monotonic mapping is valid as long as the region's mean urbanity is taken over the SAME
        /// mapping (the caller does this), which is what preserves the weighted mean.
        /// </summary>
        public static float Urbanity(float tilePopulation, float regionMaxTilePopulation)
        {
            if (regionMaxTilePopulation <= 0f) return 0f;
            float t = tilePopulation < 0f ? 0f : tilePopulation;
            float num = (float)Math.Log(1.0 + t);
            float den = (float)Math.Log(1.0 + regionMaxTilePopulation);
            if (den <= 0f) return 0f;
            float u = num / den;
            return u < 0f ? 0f : (u > 1f ? 1f : u);
        }

        /// <summary>
        /// The mean-preserving multiplier for a rises-with-density axis at this tile: 1 at the region's mean
        /// urbanity, above 1 in denser-than-average locations and below 1 in sparser ones. Pass a NEGATIVE
        /// spread for an axis that falls with density (age). Clamped to [<see cref="MinFactor"/>,
        /// <see cref="MaxFactor"/>].
        /// </summary>
        public static float DensityFactor(float urbanity, float meanUrbanity, float spread)
        {
            float f = 1f + spread * (urbanity - meanUrbanity);
            return f < MinFactor ? MinFactor : (f > MaxFactor ? MaxFactor : f);
        }

        /// <summary>Local wealth at a tile: the region's median wealth scaled up toward its cities.</summary>
        public static int LocalWealth(int regionWealth, float urbanity, float meanUrbanity, float spread = DefaultWealthSpread)
            => (int)Math.Round(regionWealth * DensityFactor(urbanity, meanUrbanity, spread));

        /// <summary>Local education index (0–100), higher in denser locations, clamped to range.</summary>
        public static int LocalEducationIndex(int regionIndex, float urbanity, float meanUrbanity, float spread = DefaultEducationSpread)
            => Clamp0to100((int)Math.Round(regionIndex * DensityFactor(urbanity, meanUrbanity, spread)));

        /// <summary>Local employment rate (0–100), higher in denser locations, clamped to range.</summary>
        public static int LocalEmploymentRate(int regionRate, float urbanity, float meanUrbanity, float spread = DefaultEmploymentSpread)
            => Clamp0to100((int)Math.Round(regionRate * DensityFactor(urbanity, meanUrbanity, spread)));

        /// <summary>Local median age: LOWER in denser locations (cities skew young), so the spread is applied
        /// negatively. Floored at a sane minimum so a boomtown never reads as an infant.</summary>
        public static int LocalMedianAge(int regionMedianAge, float urbanity, float meanUrbanity, float spread = DefaultAgeSpread)
        {
            int a = (int)Math.Round(regionMedianAge * DensityFactor(urbanity, meanUrbanity, -spread));
            return a < 5 ? 5 : a;
        }

        private static int Clamp0to100(int v) => v < 0 ? 0 : (v > 100 ? 100 : v);
    }
}
