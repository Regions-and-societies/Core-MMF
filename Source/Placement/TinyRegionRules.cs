namespace RegionsAndSocieties.Placement
{
    /// <summary>The terminal fate of a land region too small to serve (#51). A region of 1–6 tiles carries
    /// no useful region benefits and nothing the demographic model can model, so once every merge/absorb
    /// pass has had its chance it is removed rather than kept — with one exception: a region that anchors a
    /// permanent holding (settlement/outpost) is never orphaned.</summary>
    public enum TinyRegionAction
    {
        /// <summary>Leave the region as-is (not tiny, or a settlement region with nowhere to fold).</summary>
        Keep,
        /// <summary>Unassign the tiles (tileToProvinceId = -1) and remove the region — the same state
        /// impassable holes already use.</summary>
        Drop,
        /// <summary>Fold the tiles into the largest land neighbour rather than dropping — used only to keep a
        /// settlement/outpost from being orphaned.</summary>
        Fold,
    }

    /// <summary>
    /// The drop-tiny-regions terminal rule (#51). Pure by design like the rest of the Placement layer:
    /// counts and flags in, an action out, no game state — so worldgen's final pass and any save-migration
    /// path share exactly one decision, and it is testable without a game.
    ///
    /// <para>Order of the merge/split passes still wins: this only decides the fate of what survived them.
    /// With #49 in place a small island within reach of land has already joined the mainland before this
    /// runs, so what reaches here is genuinely unplaceable.</para>
    /// </summary>
    public static class TinyRegionRules
    {
        /// <summary>Land regions of this size or smaller are dropped (or folded when they hold a settlement).
        /// A fixed value, not a setting: below this the demographic model has nothing to model, and a slider
        /// would only let a player mint regions the rest of the mod cannot serve.</summary>
        public const int TinyRegionMaxTiles = 6;

        /// <summary>
        /// What to do with a land region of <paramref name="tileCount"/> tiles. Above the cap → Keep. At or
        /// below it: a region holding a settlement/outpost folds into its largest land neighbour when it has
        /// one, and is otherwise kept (never orphan a holding); a region with no holding is dropped, whether
        /// or not it has a land neighbour (a small island within reach of land was already merged by #49, so
        /// one still standing here has no useful home).
        /// </summary>
        public static TinyRegionAction Resolve(int tileCount, bool hasSettlement, bool hasLandNeighbour)
        {
            if (tileCount <= 0 || tileCount > TinyRegionMaxTiles) return TinyRegionAction.Keep;
            if (hasSettlement) return hasLandNeighbour ? TinyRegionAction.Fold : TinyRegionAction.Keep;
            return TinyRegionAction.Drop;
        }

        /// <summary>The acceptance-named predicate: true when the region should become unassigned tiles.
        /// A thin wrapper over <see cref="Resolve"/>.</summary>
        public static bool ShouldDrop(int tileCount, bool hasSettlement, bool hasLandNeighbour)
        {
            return Resolve(tileCount, hasSettlement, hasLandNeighbour) == TinyRegionAction.Drop;
        }
    }
}
