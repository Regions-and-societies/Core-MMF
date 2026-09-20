using System;

namespace RegionsAndSocieties.Sizing
{
    /// <summary>
    /// Every threshold and per-tier effect in one table, following the precedent
    /// <c>PlacementRules</c> set in Epic 2: 0.8 is Logic Externalization, so the numbers live in a
    /// single object that can be moved into defs or settings without hunting them down.
    /// </summary>
    public static class SettlementSizeRules
    {
        // -- population thresholds --------------------------------------------
        //
        // The tier populations are the district model's own figures (#30/#77): a settlement reads as
        // a given tier once it holds that tier's comfortable district population —
        // 700 (Hamlet), 1,900 (Village), 3,700 (Town), 6,100 (City); below 700 it is a Homestead.
        // DistrictRules is the single source of truth for these, so this table can never drift from
        // the scale drawn on the world map. See DistrictRules.PopulationForTier / TierForPopulation.

        /// <summary>
        /// Residents per dwelling, so a dwelling count can stand in for an unknown population.
        /// Mirrors <c>GeographicProvince.totalDwellings</c>, which is population / 2.
        /// </summary>
        public const int ResidentsPerDwelling = 2;

        /// <summary>The comfortable population at which a settlement reads as this tier — the tier's
        /// district population (0 for Homestead, the floor).</summary>
        public static int MinPopulationFor(SettlementTier tier)
        {
            return (int)tier <= 0 ? 0 : DistrictRules.PopulationForTier(tier);
        }

        // -- tier effects -----------------------------------------------------

        /// <summary>
        /// How many residents this tier can support. A settlement at capacity is the signal for
        /// Epic 3 that further growth has to come from tiering up rather than from more people.
        ///
        /// <c>Homestead</c> returns 0, meaning "no tier-imposed cap". Callers must read a non-positive
        /// capacity as unlimited rather than as room for nobody.
        /// </summary>
        public static int PopulationCapacity(SettlementTier tier)
        {
            // A tier's own crowded ceiling (comfortable population × 1.5), from the district model.
            // Homestead returns 0 — "no tier-imposed cap" — as the contract above requires.
            return (int)tier <= 0 ? 0 : DistrictRules.MaxPopulationForTier(tier);
        }

        /// <summary>
        /// Production multiplier for the tier.
        ///
        /// Deliberately sublinear in population: a metropolis holds roughly seven times a village's
        /// headcount but produces a little over twice as much. Big settlements are better, not
        /// runaway — otherwise the optimal play is one enormous capital and nothing else, which is
        /// the opposite of a mod about regions.
        ///
        /// <c>Homestead</c> returns 1, not 0. A holding with no tier — a camp, or anything at all when
        /// tiers are switched off — must be left alone by this multiplier, and a neutral 1 is the
        /// only value that does that. Returning 0 here would silently zero the economy of every
        /// untiered holding in the world, which is a far worse failure than a missing bonus.
        /// </summary>
        public static float ProductionScale(SettlementTier tier)
        {
            switch (tier)
            {
                case SettlementTier.Hamlet: return 1.00f;
                case SettlementTier.Village: return 1.35f;
                case SettlementTier.Town: return 1.75f;
                case SettlementTier.City: return 2.80f;
                default: return 1f;
            }
        }

        /// <summary>
        /// How far this settlement's claim reaches, in tiles. Feeds territory footprint: larger
        /// tiers claim more of the region around them.
        /// </summary>
        public static int TerritoryFootprint(SettlementTier tier)
        {
            switch (tier)
            {
                case SettlementTier.Hamlet: return 1;
                case SettlementTier.Village: return 1;
                case SettlementTier.Town: return 2;
                case SettlementTier.City: return 4;
                default: return 0;
            }
        }
    }
}
