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
        // Colours come from the shared PopulationOverlayPalette (#30/#79): clear for the Frontier
        // countryside and empty land alike, and a violet→yellow ramp for the settled zone — ramped here by
        // DWELLINGS per tile (sublinear in people, since urban homes hold fewer each) up to the peak.
        private static Material[] rampMats;

        public MapMode_Residence() { }
        public MapMode_Residence(MapModeDef def) : base(def) { }

        public override WorldLayer_MapMode WorldLayer => WorldLayer_MapMode_Terrain.Instance;
        public override bool CanToggleWater => false;

        private static Material MakeMat(Color color)
        {
            Material m = (ShaderDatabase.MetaOverlay != null && BaseContent.WhiteTex != null)
                ? MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.MetaOverlay, color, 3510)
                : SolidColorMaterials.SimpleSolidColorMaterial(color);
            return m ?? BaseContent.WhiteMat;
        }

        public override void DoPreRegenerate()
        {
            base.DoPreRegenerate();
            PopulationDensityUtility.EnsureCache();
            if (rampMats != null) return;
            Color[] ramp = PopulationOverlayPalette.Ramp;
            rampMats = new Material[ramp.Length];
            for (int i = 0; i < ramp.Length; i++) rampMats[i] = MakeMat(ramp[i]);
        }

        public override Material GetMaterial(int tile)
        {
            if (rampMats == null || Find.WorldGrid == null || tile < 0 || tile >= Find.WorldGrid.TilesCount)
                return BaseContent.ClearMat;
            Tile t = Find.WorldGrid[tile];
            if (t == null || t.WaterCovered) return BaseContent.ClearMat;

            // Ramp by dwellings per tile: white for empty land, clear for the Frontier countryside, then
            // up the ramp to the densest tile's dwelling count (the peak).
            int pop = PopulationDensityUtility.GetPopulationAtTile(tile);
            int dwellings = ResidenceRules.For(pop).dwellings;
            int peakDwellings = ResidenceRules.For(PopulationDensityUtility.MaxTilePopulation()).dwellings;
            int band = PopulationOverlayPalette.Band(dwellings, PopulationOverlayPalette.FrontierCeilingDwellings, peakDwellings);

            if (band < 0) return BaseContent.ClearMat;   // clear — countryside or empty land
            if (band >= rampMats.Length) band = rampMats.Length - 1;
            return rampMats[band];
        }

        public override string GetTileLabel(int tile)
        {
            // #79: only settlement-scale tiles get a dwelling-count label; the Frontier countryside
            // (populated everywhere now) stays unlabelled, matching where the ramp colours the map.
            int pop = PopulationDensityUtility.GetSourcePopulationAtTile(tile);
            if (pop < PopulationOverlayPalette.FrontierCeiling) return null;
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
