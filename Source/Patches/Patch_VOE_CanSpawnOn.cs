using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RegionsAndSocieties.Patches
{
    /// <summary>
    /// Postfix on Vanilla Outposts Expanded's <c>Outposts.Utils.CanSpawnOnWithExt</c>, applied
    /// reflectively from <c>TryPatchVOE</c>. TEMPORARY HOME: this is VOE knowledge in core and moves
    /// to Regions-and-societies/VOE-CP with the rest of the VOE integration (Core-MMF#3, extraction
    /// order: VOE last). Parked here so the Empire extraction could take the rest of its old file.
    ///
    /// <para>The third parameter must be named <c>ps</c>, not <c>pawns</c>. Harmony binds injected
    /// parameters <b>by name</b> against the original method's signature, and VOE calls it
    /// <c>ps</c> — the mismatch made this patch fail to attach with
    /// <c>Parameter "pawns" not found in method ... CanSpawnOnWithExt</c>, which is only visible
    /// with VOE actually installed. Renaming it here is the fix; do not "tidy" it back.</para>
    /// </summary>
    public static class Patch_VOE_CanSpawnOn
    {
        public static void CanSpawnOnWithExt_Postfix(object ext, PlanetTile tileIdx, System.Collections.Generic.IEnumerable<Pawn> ps, ref string __result)
        {
            if (!string.IsNullOrEmpty(__result)) return;

            if (!OutpostPlacementUtility.CanPlaceOutpostAt(tileIdx.tileId, Faction.OfPlayer, out string reason))
            {
                __result = reason;
            }
        }
    }
}
