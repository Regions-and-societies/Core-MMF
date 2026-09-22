using System;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using RegionsAndSocieties.Demographics;
using RegionsAndSocieties.Integration;

namespace RegionsAndSocieties.Patches
{
    /// <summary>
    /// #35: when a timed site (a raider camp, a quest outpost — anything with a timeout) the player chose NOT
    /// to clear finally EXPIRES on its own, in its own faction's or neutral territory, the people who were
    /// there settle into the region. Record it as a permanent settlement-history pressure source. Non-invasive:
    /// a prefix on removal that only READS the object's state and only fires when the timer actually ran out —
    /// a site the player cleared is removed with time still on the clock and is skipped. Never touches the
    /// vanilla timeout mechanic itself.
    /// </summary>
    [HarmonyPatch(typeof(WorldObject), nameof(WorldObject.Destroy))]
    public static class Patch_WorldObject_Destroy_SettlementHistory
    {
        [HarmonyPrefix]
        public static void Prefix(WorldObject __instance)
        {
            try { OnRemoved(__instance); }
            catch (Exception e) { Log.Error("[RegionsAndSocieties] settlement-history hook (#35) threw; removal continues: " + e); }
        }

        private static void OnRemoved(WorldObject o)
        {
            if (o == null || o.Faction == null) return;
            if (Current.ProgramState != ProgramState.Playing || Find.World == null) return;
            if (!RegionsAndSocietiesMod.SocietiesEnabled) return;

            // Only persistent-outpost-like objects — not settlements (they don't expire), not untimed things.
            WorldObjectKind kind = WorldObjectClassifier.Classify(o);
            if (kind != WorldObjectKind.Outpost && kind != WorldObjectKind.Camp) return;

            PlanetTile pt = o.Tile;
            if (!RegionDemographicsUtility.IsSurfaceSampleTile(pt)) return;

            if (!ExpiredNaturally(o)) return;   // the player cleared it (time still on the clock) — no legacy

            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            GeographicProvince prov = mgr?.GetProvinceForTile(pt.tileId);
            if (prov == null || prov.provinceType != ProvinceType.Land) return;
            if (!IsOwnOrNeutralTerritory(prov, o.Faction)) return;   // #35: only in its own or neutral land

            int population = SettlementHistoryRules.LegacyPopulation(0f);
            mgr.AddSettlementHistory(pt.tileId, o.Faction.GetUniqueLoadID(), population);
            Log.Message($"[RegionsAndSocieties] #35: '{o.LabelCap}' ({kind}) expired in friendly/neutral territory — left a settlement-history legacy of {population} at tile {pt.tileId}.");
        }

        /// <summary>True only when the site's own timer ran out (it was ignored), not when the player cleared
        /// it before expiry. Reads the vanilla timeout state without altering it.</summary>
        private static bool ExpiredNaturally(WorldObject o)
        {
            TimeoutComp timeout = o.GetComponent<TimeoutComp>();
            if (timeout != null && timeout.Active) return timeout.TicksLeft <= 0;
            if (o is IExpirableWorldObject exp)
            {
                int at = exp.ExpireAtTicks;
                return at >= 0 && Find.TickManager != null && Find.TickManager.TicksGame >= at;
            }
            return false;   // no timer we understand — do not treat an untimed removal as an expiry
        }

        private static bool IsOwnOrNeutralTerritory(GeographicProvince prov, Faction faction)
        {
            var owners = prov.owningFactionIds;
            if (owners == null || owners.Count == 0) return true;   // neutral / unclaimed
            return owners.Contains(faction.GetUniqueLoadID());       // its own territory
        }
    }
}
