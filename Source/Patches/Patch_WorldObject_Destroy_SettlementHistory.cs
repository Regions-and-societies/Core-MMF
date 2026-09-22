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
    /// there settle into the region.
    ///
    /// <para><b>Base (Phase 1)</b>: record a permanent, faint settlement-history <i>legacy</i> pressure source
    /// and let the site be removed as normal. Non-invasive: only READS the object's state and only fires when
    /// the timer actually ran out — a site the player cleared is removed with time still on the clock and is
    /// skipped.</para>
    ///
    /// <para><b>Persistent (Phase 2, opt-in via <see cref="SynapseRegionManager.EffectivePersistentOutpostHistory"/>)</b>:
    /// instead of the faint legacy, STOP the site's timer and keep it — it stays as a timerless world object
    /// that projects its full make-up into the region (through <see cref="RegionDemographicsUtility"/>) until
    /// it is cleared or captured. We skip the removal (prefix returns false) only when there is a
    /// <see cref="TimeoutComp"/> we can actually stop; a site whose expiry we cannot turn off (an
    /// IExpirable-only or quest-cleanup case) falls back to the faint legacy so no site is ever leaked.</para>
    /// </summary>
    [HarmonyPatch(typeof(WorldObject), nameof(WorldObject.Destroy))]
    public static class Patch_WorldObject_Destroy_SettlementHistory
    {
        /// <summary>Returns false to SKIP the vanilla removal (the site is kept, timerless), true to let it
        /// proceed. Any error falls through to normal removal.</summary>
        [HarmonyPrefix]
        public static bool Prefix(WorldObject __instance)
        {
            try { return OnRemoved(__instance); }
            catch (Exception e)
            {
                Log.Error("[RegionsAndSocieties] settlement-history hook (#35) threw; removal continues: " + e);
                return true;
            }
        }

        /// <summary>True to allow removal, false to keep the site (persistent mode kept its timer stopped).</summary>
        private static bool OnRemoved(WorldObject o)
        {
            if (o == null || o.Faction == null) return true;
            if (Current.ProgramState != ProgramState.Playing || Find.World == null) return true;
            if (!RegionsAndSocietiesMod.SocietiesEnabled) return true;

            // Only persistent-outpost-like objects — not settlements (they don't expire), not untimed things.
            WorldObjectKind kind = WorldObjectClassifier.Classify(o);
            if (kind != WorldObjectKind.Outpost && kind != WorldObjectKind.Camp) return true;

            PlanetTile pt = o.Tile;
            if (!RegionDemographicsUtility.IsSurfaceSampleTile(pt)) return true;

            if (!ExpiredNaturally(o)) return true;   // the player cleared it (time still on the clock) — no legacy

            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            GeographicProvince prov = mgr?.GetProvinceForTile(pt.tileId);
            if (prov == null || prov.provinceType != ProvinceType.Land) return true;
            if (!RegionDemographicsUtility.IsOwnOrNeutralTerritory(prov, o.Faction)) return true;   // #35: own or neutral land only

            // Phase 2: keep the site as a live, timerless bonus instead of a faint legacy — but only when we
            // can actually stop its timer, so a quest that will clean the site up regardless is never fought.
            if (mgr.EffectivePersistentOutpostHistory)
            {
                TimeoutComp timeout = o.GetComponent<TimeoutComp>();
                if (timeout != null)
                {
                    timeout.StopTimeout();   // "leave the quest with no timer" — it persists as a world object
                    RegionDemographicsUtility.InvalidateCache();   // it is now a live pressure source
                    Log.Message($"[RegionsAndSocieties] #35: '{o.LabelCap}' ({kind}) expired in friendly/neutral territory and is KEPT (persistent mode) as a live demographic source at tile {pt.tileId}.");
                    return false;   // skip removal — the site stays
                }
                // no stoppable timer: fall through to the faint legacy so nothing is leaked
            }

            int population = SettlementHistoryRules.LegacyPopulation(0f);
            mgr.AddSettlementHistory(pt.tileId, o.Faction.GetUniqueLoadID(), population);
            Log.Message($"[RegionsAndSocieties] #35: '{o.LabelCap}' ({kind}) expired in friendly/neutral territory — left a settlement-history legacy of {population} at tile {pt.tileId}.");
            return true;
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
    }
}
