using System;
using Verse;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>
    /// A permanent demographic legacy left by a timed site the player let EXPIRE rather than clear (#35). When
    /// a raider camp, a quest site or any timed outpost times out in its own faction's territory or on neutral
    /// land — the player chose not to invade — the people who were there settle into the region. It becomes
    /// part of the region's history: a persistent pressure source, at the site's tile, carrying the site
    /// faction's make-up (via the #74 per-settlement machinery), that feeds the demographic field forever.
    ///
    /// <para>Scribed on <see cref="SynapseRegionManager"/> so a save keeps how the map's history has
    /// accumulated, and deterministic (a fixed tile + faction + population), so the pressure field stays
    /// stable and <see cref="RegionDemographicsUtility.VerifyCulling"/> keeps reporting zero mismatches.</para>
    /// </summary>
    public class SettlementHistorySource : IExposable
    {
        public int tile = -1;              // where the site was
        public string factionId;           // whose people they were (resolved to a Faction for the make-up)
        public int population;             // the permanent headcount this legacy contributes

        public SettlementHistorySource() { }
        public SettlementHistorySource(int tile, string factionId, int population)
        {
            this.tile = tile; this.factionId = factionId; this.population = population;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref tile, "tile", -1);
            Scribe_Values.Look(ref factionId, "faction");
            Scribe_Values.Look(ref population, "pop", 0);
        }
    }
}
