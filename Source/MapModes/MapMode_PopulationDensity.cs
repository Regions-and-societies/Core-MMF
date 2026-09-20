using MapModeFramework;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    [StaticConstructorOnStartup]
    public class MapMode_PopulationDensity : MapMode
    {
        private static Material[] densityMats = null;
        private static Material emptyMat = null;   // white — habitable land with no people (#79)

        // Colours come from the shared PopulationOverlayPalette (#30/#79): white for empty land, clear for
        // the Frontier countryside, and a violet→yellow magma ramp for the settled zone, each ramp colour
        // darkened by elevation. The old fixed 5-band table lived here; it moved to the palette so the
        // population and dwellings overlays cannot drift apart on what "empty/countryside/peak" look like.

        private static Material MakeMat(Color color)
        {
            Material m = null;
            if (ShaderDatabase.MetaOverlay != null && BaseContent.WhiteTex != null)
                m = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.MetaOverlay, color, 3510);
            if (m == null) m = SolidColorMaterials.SimpleSolidColorMaterial(color);
            return m ?? BaseContent.WhiteMat;
        }

        public static void InitializeMaterials()
        {
            if (densityMats != null) return;

            emptyMat = MakeMat(PopulationOverlayPalette.Empty);

            // ramp segments * 4 elevation bands.
            Color[] ramp = PopulationOverlayPalette.Ramp;
            densityMats = new Material[ramp.Length * 4];
            for (int seg = 0; seg < ramp.Length; seg++)
            {
                Color baseColor = ramp[seg];
                for (int band = 0; band < 4; band++)
                {
                    // Darken toward the mountains and lift alpha a little, so terrain still reads through.
                    float dim = 1f - 0.16f * band;
                    Color color = new Color(baseColor.r * dim, baseColor.g * dim, baseColor.b * dim,
                        Mathf.Min(0.9f, baseColor.a + 0.05f * band));
                    densityMats[seg * 4 + band] = MakeMat(color);
                }
            }
        }

        // The density model lives in PopulationDensityUtility. This map mode used to carry a
        // near-verbatim clone of the whole thing (baseline, propagation, step multiplier), which
        // meant the heatmap and the inspect pane could disagree and every fix had to be made twice
        // (#62). It now reads the one shared cache; CacheData just makes sure it is warm.
        public static void CacheData()
        {
            InitializeMaterials();
            PopulationDensityUtility.EnsureCache();
        }

        public override WorldLayer_MapMode WorldLayer => WorldLayer_MapMode_Terrain.Instance;
        public override bool CanToggleWater => false;

        public override void DoPreRegenerate()
        {
            base.DoPreRegenerate();
            CacheData();
        }

        public MapMode_PopulationDensity() { }
        public MapMode_PopulationDensity(MapModeDef def) : base(def) { }

        public override Material GetMaterial(int tile)
        {
            if (Find.WorldGrid == null || tile >= Find.WorldGrid.TilesCount)
            {
                return BaseContent.ClearMat;
            }

            Tile tileData = Find.WorldGrid[tile];
            if (tileData.WaterCovered)
            {
                return BaseContent.ClearMat;
            }

            // Colour by the smeared influence field so the heatmap still fades outward from cities.
            // #79: the whole map now has a Frontier baseline, so the palette reads empty land as white,
            // the countryside as clear, and only settlement concentrations up the ramp to the peak.
            int pop = PopulationDensityUtility.GetPopulationAtTile(tile);
            int band = PopulationOverlayPalette.Band(pop, PopulationOverlayPalette.FrontierCeiling,
                PopulationDensityUtility.MaxTilePopulation());

            if (band == -2) return emptyMat ?? BaseContent.WhiteMat;   // white — nobody could live here
            if (band == -1) return BaseContent.ClearMat;               // clear — Frontier countryside

            // Darken the ramp colour toward the mountains so terrain still reads through.
            float elevation = tileData.elevation;
            int elevationBand = 0;
            if (elevation >= 2200f) elevationBand = 3;
            else if (elevation >= 1200f) elevationBand = 2;
            else if (elevation >= 600f) elevationBand = 1;

            int index = band * 4 + elevationBand;
            if (densityMats == null || index < 0 || index >= densityMats.Length)
            {
                return BaseContent.ClearMat;
            }

            return densityMats[index];
        }

        public override string GetTileLabel(int tile)
        {
            // Label with the dwellings actually on the tile, not the smeared field (#55).
            int pop = PopulationDensityUtility.GetSourcePopulationAtTile(tile);
            return pop > 0 ? pop.ToString() : null;
        }

        public override string GetTooltip(int tile)
        {
            // Every habitable tile answers, zero included, with its neighbours around it and the
            // hovered tile bolded in the middle — so an empty stretch of map reads as "0 here",
            // not as the tooltip being broken. Ocean and ice still show nothing.
            return PopulationDensityUtility.GetDwellingsDisplay(tile);
        }
    }
}
