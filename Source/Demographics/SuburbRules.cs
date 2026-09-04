using System;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>
    /// How a settlement's people spread over its footprint (0.3.0). A settlement is not a point: its
    /// head count lives on its own tile AND in the rings around it — suburbs, farms, outlying homes —
    /// thinning outward. The density field, the per-tile "Pawn dwellings" label and the residence
    /// layer all read that spread, so a 200-person city no longer sits as a lone 200 in a sea of zeros.
    ///
    /// <para>Pure: the world is handed in as the count of ELIGIBLE tiles in each ring (habitable land in
    /// the settlement's own region, as the caller decides); the rule only splits the number. The total
    /// is conserved exactly — whatever the rings cannot take stays on the centre tile — so region
    /// totals are unchanged by the spread.</para>
    /// </summary>
    public static class SuburbRules
    {
        /// <summary>The settlement tile keeps at least this share of its people; the rest is the suburbs' budget.</summary>
        public const float CoreShare = 0.5f;

        /// <summary>Per-tile weight of each ring relative to the ring inside it: ring r weighs RingDecay^(r-1).</summary>
        public const float RingDecay = 0.5f;

        /// <summary>
        /// Split <paramref name="population"/> between the centre tile and its rings.
        /// <paramref name="eligibleTilesPerRing"/>[i] is the number of eligible tiles in ring i+1 (ring 1 =
        /// the six neighbours); null or empty means no suburbs. Returns the per-tile amount for each ring in
        /// <paramref name="perTileByRing"/> (same length as the input, 0 where a ring has no eligible tile)
        /// and the amount that stays on the centre tile. Centre + Σ(ring share × ring count) == population.
        /// </summary>
        public static float Distribute(float population, int[] eligibleTilesPerRing, out float[] perTileByRing)
        {
            int rings = eligibleTilesPerRing?.Length ?? 0;
            perTileByRing = new float[rings];
            if (population <= 0f) return 0f;
            if (rings == 0) return population;

            // The per-tile share is sized against a FULL hex footprint (ring r has 6r tiles), each tile
            // in ring r weighing RingDecay^(r-1). A ring with fewer eligible tiles than that — coast,
            // mountains, a region border — simply places fewer shares, and the difference stays on the
            // centre. So a coastal city's suburbs are the same size as an inland one's; there are just
            // fewer of them, and the town itself is denser for it.
            float fullUnits = 0f;
            for (int r = 0; r < rings; r++)
            {
                fullUnits += FullRingTiles(r + 1) * (float)Math.Pow(RingDecay, r);
            }
            if (fullUnits <= 0f) return population;

            float budget = population * (1f - CoreShare);
            float perUnit = budget / fullUnits;
            float placed = 0f;
            for (int r = 0; r < rings; r++)
            {
                int n = eligibleTilesPerRing[r];
                if (n <= 0) { perTileByRing[r] = 0f; continue; }
                float share = perUnit * (float)Math.Pow(RingDecay, r);
                perTileByRing[r] = share;
                placed += share * n;
            }

            return population - placed;
        }

        /// <summary>Tiles in hex ring <paramref name="ring"/> (1 = the six neighbours) on an unbroken grid.</summary>
        public static int FullRingTiles(int ring)
        {
            return ring <= 0 ? 1 : 6 * ring;
        }
    }
}
