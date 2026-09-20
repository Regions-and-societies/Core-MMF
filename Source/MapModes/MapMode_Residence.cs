using MapModeFramework;
using RegionsAndSocieties.Demographics;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// Shades each populated tile by how its people are housed — the settlement tier its population reads
    /// as (<see cref="ResidenceRules"/> → <see cref="Sizing.DistrictRules.TierForPopulation"/>). A rural
    /// homestead (an extended family on wide land) reads green; a city (nuclear households packed onto
    /// small lots) reads red; hamlet, village and town sit between. The label shows the number of
    /// dwellings, so the overlay reads population as HOMES, not head count.
    ///
    /// <para>Materials — one per tier — are pre-built on the main thread in <see cref="DoPreRegenerate"/>,
    /// so the worker-thread mesh build only reads them.</para>
    /// </summary>
    [StaticConstructorOnStartup]
    public class MapMode_Residence : MapMode
    {
        // One colour per settlement tier, rural to urban: land-rich green -> packed-in red. Indexed by
        // (int)SettlementTier, so Homestead=0 .. City=4.
        private static readonly Color[] TierBase =
        {
            new Color(0.35f, 0.62f, 0.30f, 0.55f),   // Homestead — rural, land-rich
            new Color(0.55f, 0.69f, 0.26f, 0.57f),   // Hamlet
            new Color(0.74f, 0.68f, 0.21f, 0.59f),   // Village
            new Color(0.88f, 0.54f, 0.18f, 0.63f),   // Town
            new Color(0.84f, 0.24f, 0.28f, 0.68f),   // City — dense, small lots
        };
        private static Material[] tierMats;

        public MapMode_Residence() { }
        public MapMode_Residence(MapModeDef def) : base(def) { }

        public override WorldLayer_MapMode WorldLayer => WorldLayer_MapMode_Terrain.Instance;
        public override bool CanToggleWater => false;

        public override void DoPreRegenerate()
        {
            base.DoPreRegenerate();
            PopulationDensityUtility.EnsureCache();
            if (tierMats != null) return;
            tierMats = new Material[TierBase.Length];
            for (int i = 0; i < TierBase.Length; i++)
            {
                Color c = TierBase[i];
                Material m = (ShaderDatabase.MetaOverlay != null && BaseContent.WhiteTex != null)
                    ? MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.MetaOverlay, c, 3510)
                    : SolidColorMaterials.SimpleSolidColorMaterial(c);
                tierMats[i] = m ?? BaseContent.WhiteMat;
            }
        }

        public override Material GetMaterial(int tile)
        {
            if (tierMats == null || Find.WorldGrid == null || tile < 0 || tile >= Find.WorldGrid.TilesCount)
                return BaseContent.ClearMat;
            Tile t = Find.WorldGrid[tile];
            if (t == null || t.WaterCovered) return BaseContent.ClearMat;
            int pop = PopulationDensityUtility.GetPopulationAtTile(tile);
            if (pop <= 0) return BaseContent.ClearMat;
            int band = (int)Sizing.DistrictRules.TierForPopulation(pop);
            if (band < 0) band = 0;
            if (band >= tierMats.Length) band = tierMats.Length - 1;
            return tierMats[band];
        }

        public override string GetTileLabel(int tile)
        {
            int pop = PopulationDensityUtility.GetSourcePopulationAtTile(tile);
            if (pop <= 0) return null;
            return ResidenceRules.For(pop).dwellings.ToString();
        }

        public override string GetTooltip(int tile)
        {
            if (Find.WorldGrid == null || tile < 0 || tile >= Find.WorldGrid.TilesCount) return null;
            Tile t = Find.WorldGrid[tile];
            if (t == null || t.WaterCovered) return null;
            int pop = PopulationDensityUtility.GetPopulationAtTile(tile);
            if (pop <= 0) return "No residents here";
            ResidenceProfile r = ResidenceRules.For(pop);
            return $"{r.tier}\n{r.dwellings} dwellings · {r.occupancy:0.0} per home\n{pop} people · land/person {r.landPerPawn:0.00}";
        }
    }
}
