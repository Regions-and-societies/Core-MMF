using System;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using RegionsAndSocieties.Demographics;

namespace RegionsAndSocieties.Patches
{
    /// <summary>
    /// #28: after a pawn is generated, hand its home-region demographics to any consumer that wants to make
    /// the pawn inherit them (education → skills, SES → gear). Core reports; the consumer decides. A hard
    /// no-op when nothing is listening — the fast-out runs before any work, so the base game and a
    /// no-consumer install are untouched and pay nothing. Core never modifies the pawn itself here.
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) })]
    public static class Patch_PawnGenerator_GeneratePawn
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __result, PawnGenerationRequest request)
        {
            // Fast-out: nothing to do unless a consumer is listening AND societies are modelled.
            if (!PawnDemographicHooks.HasConsumer || __result == null) return;
            if (!RegionsAndSocietiesMod.SocietiesEnabled) return;

            try
            {
                Faction faction = request.Faction ?? __result.Faction;
                int homeTile = ResolveHomeTile(request);
                RegionDemographics demo = ResolveDemographics(homeTile, faction);
                if (demo == null || demo.settledTiles <= 0) return;   // no region to inherit from (e.g. worldgen, unsettled)

                PawnDemographicHooks.Fire(new PawnDemographicContext
                {
                    pawn = __result,
                    faction = faction,
                    homeTile = homeTile,
                    demographics = demo,
                    isPlayerPawn = faction != null && faction.IsPlayer,
                });
            }
            catch (Exception e)
            {
                Log.Error("[RegionsAndSocieties] Pawn-demographics hook (#28) threw; pawn generation continues: " + e);
            }
        }

        /// <summary>The surface world tile a pawn was generated for, or -1 when it has none (then the
        /// faction-wide demographics stand in).</summary>
        private static int ResolveHomeTile(PawnGenerationRequest request)
        {
            PlanetTile t = request.Tile;
            WorldGrid grid = Find.WorldGrid;
            if (grid != null && t.Valid && t.tileId >= 0 && t.tileId < grid.TilesCount && t.Layer.IsRootSurface)
                return t.tileId;
            return -1;
        }

        /// <summary>The home-region demographics: the region at the generation tile, else the faction's
        /// whole-territory aggregate, else none.</summary>
        private static RegionDemographics ResolveDemographics(int homeTile, Faction faction)
        {
            if (homeTile >= 0)
            {
                var mgr = Find.World?.GetComponent<SynapseRegionManager>();
                GeographicProvince prov = mgr?.GetProvinceForTile(homeTile);
                if (prov != null && prov.provinceType == ProvinceType.Land)
                {
                    RegionDemographics d = RegionDemographicsUtility.ForRegion(prov);
                    if (d.settledTiles > 0) return d;
                }
            }
            return faction != null ? RegionDemographicsUtility.ForFaction(faction) : null;
        }
    }
}
