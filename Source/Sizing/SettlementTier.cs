using System;

namespace RegionsAndSocieties.Sizing
{
    /// <summary>
    /// How large a population centre is, independent of which mod created it.
    ///
    /// The tier is the unit production scaling, territory footprint, outpost allowance, and the UI
    /// all read, so it has to mean the same thing for a vanilla settlement, an Empire colony, and a
    /// VOE outpost. It is derived — never stored on the world object — so it cannot go stale.
    ///
    /// Lives in Regions and Territories, not Factions: the tier governs territory (outpost allowance,
    /// ownership, footprint), which is this mod's domain, and R&amp;T must not depend on Factions.
    /// Factions consumes this for taxation, standing and production.
    /// </summary>
    public enum SettlementTier
    {
        /// <summary>The smallest rung: a lone homestead, one district of people. Also what an
        /// unranked holding reads as, and what everything reads as while the settlement-tier feature
        /// is switched off. Carries no tier-imposed population cap.</summary>
        Homestead = 0,
        Village = 1,       // T1
        Town = 2,          // T2
        City = 3,          // T3
        Metropolis = 4     // T4 — the capital tier; a faction needs 10 settlements to afford one
    }

    public static class SettlementTierExtensions
    {
        public static string Label(this SettlementTier tier)
        {
            switch (tier)
            {
                case SettlementTier.Village: return "village";
                case SettlementTier.Town: return "town";
                case SettlementTier.City: return "city";
                case SettlementTier.Metropolis: return "metropolis";
                default: return "homestead";
            }
        }

        /// <summary>Capitalised for the inspect pane, where it starts a line.</summary>
        public static string LabelCapitalized(this SettlementTier tier)
        {
            switch (tier)
            {
                case SettlementTier.Village: return "Village";
                case SettlementTier.Town: return "Town";
                case SettlementTier.City: return "City";
                case SettlementTier.Metropolis: return "Metropolis";
                default: return "Homestead";
            }
        }

        public static bool IsAtLeast(this SettlementTier tier, SettlementTier other)
        {
            return (int)tier >= (int)other;
        }

        /// <summary>The larger of two tiers. Used when several sources disagree about a settlement.</summary>
        public static SettlementTier Max(this SettlementTier a, SettlementTier b)
        {
            return (int)a >= (int)b ? a : b;
        }
    }
}
