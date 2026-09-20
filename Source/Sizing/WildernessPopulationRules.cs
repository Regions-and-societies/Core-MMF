namespace RegionsAndSocieties.Sizing
{
    /// <summary>
    /// The seeded, biome-weighted base population of a wilderness tile (#79) — the <b>Frontier</b>
    /// floor that carries most of the world's people, underneath the settlement peaks. Pure and
    /// deterministic: the same (seed, tile, habitability) always yields the same number, so the whole
    /// surface is a formula of the world seed and the land and is never stored
    /// (see <c>Design/WILDERNESS_POPULATION.md</c>).
    ///
    /// <para><b>Not a set of pressure sources.</b> This is sampled O(1) per tile as a continuous floor,
    /// so a populated countryside costs the demographic field nothing beyond the tile loop it already
    /// runs — rejecting the ~49,000-discrete-sources design that would have sunk #61. The target is a
    /// biome-scaled mean of ~30 people per 23.4 km² tile (~1.3/km²), which puts a 30%-coverage world
    /// near 1.5M with ~85-90% of it rural. The density is a tuning endpoint, pinned against behaviour
    /// in <c>Design/sim</c>, not a hidden constant.</para>
    ///
    /// <para>The habitability input is <see cref="RegionsAndSocieties.Placement.BiomeHabitabilityRules.Habitability"/>
    /// (settlement weight × toil × health): 0 for land vanilla never settles — ocean, ice, impassable —
    /// which therefore holds no one, ~1 for a temperate biome on open ground, and higher for lush land.</para>
    /// </summary>
    public static class WildernessPopulationRules
    {
        /// <summary>People on a tile of reference habitability, before jitter — the #79 target mean.</summary>
        public const float TargetMeanPerTile = 30f;

        /// <summary>The habitability a reference tile has: a temperate biome on open ground, which
        /// <c>BiomeHabitabilityRules.Habitability</c> scores near 1. Good land rises above the mean,
        /// marginal land falls below it, uninhabitable land holds no one.</summary>
        public const float ReferenceHabitability = 1f;

        /// <summary>How sharply good land out-populates poor land. Above 1 makes deserts thin and river
        /// valleys full; 1 would be linear in habitability.</summary>
        public const float Steepness = 1.3f;

        /// <summary>The best land holds at most this multiple of the reference density, so a single lush
        /// wilderness tile cannot rival a settlement (a homestead is 100).</summary>
        public const float MaxMultiple = 4f;

        /// <summary>Per-tile jitter half-range: a tile's density varies ±30% from its biome mean, so
        /// neighbouring wilderness tiles of one biome are not identical.</summary>
        public const float JitterSpread = 0.3f;

        /// <summary>People a wilderness tile of the given habitability supports, before the per-tile
        /// jitter. Zero for uninhabitable land (habitability ≤ 0: ocean, ice, impassable).</summary>
        public static float PeoplePerTile(float habitability)
        {
            if (habitability <= 0f) return 0f;
            float reference = ReferenceHabitability <= 0f ? 1f : ReferenceHabitability;
            float mult = (float)System.Math.Pow(habitability / reference, Steepness);
            if (mult > MaxMultiple) mult = MaxMultiple;
            return TargetMeanPerTile * mult;
        }

        /// <summary>A deterministic per-tile jitter in [1 − JitterSpread, 1 + JitterSpread], a pure hash
        /// of (seed, tile) so the surface is identical on reload and independent of world-object churn.</summary>
        public static float Jitter(int worldSeed, int tileId)
        {
            uint h = Hash(worldSeed, tileId);
            float f = (h & 0xFFFFFFu) / (float)0x1000000;   // 0..1
            return (1f - JitterSpread) + f * (2f * JitterSpread);
        }

        /// <summary>The base population of a wilderness tile: biome-weighted density × per-tile jitter,
        /// rounded. Deterministic from the world seed — the whole surface reconstructs from it.</summary>
        public static int PopulationForTile(int worldSeed, int tileId, float habitability)
        {
            float bp = PeoplePerTile(habitability);
            if (bp <= 0f) return 0;
            int p = (int)System.Math.Round(bp * Jitter(worldSeed, tileId), System.MidpointRounding.AwayFromZero);
            return p < 0 ? 0 : p;
        }

        // A small avalanching integer hash (finaliser-style), so a one-tile change of seed or id gives an
        // unrelated jitter. Pure — no game types, no UnityEngine.Random state to save or restore.
        private static uint Hash(int worldSeed, int tileId)
        {
            unchecked
            {
                uint h = (uint)worldSeed * 2654435761u + (uint)tileId * 40503u + 0x9E3779B9u;
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
                return h;
            }
        }
    }
}
