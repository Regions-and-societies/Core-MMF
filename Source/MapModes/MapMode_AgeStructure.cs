using MapModeFramework;
using RegionsAndSocieties.Demographics;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// The regional age-structure overlay (0.2.0, #10): every settled region is shaded by its median
    /// age, from youthful green through mature yellow to elderly red, so the age model reads at a
    /// glance the way the dwellings heatmap reads population. The structure itself is deterministic and
    /// region-level (<see cref="RegionDemographicsUtility.ForRegion"/>), so this mode only paints the
    /// one shared aggregate — it computes nothing of its own. Unsettled wilderness and water are left
    /// unshaded; hovering a tile shows the region's full age breakdown.
    /// </summary>
    [StaticConstructorOnStartup]
    public class MapMode_AgeStructure : MapMode
    {
        private static Material[] ageMats = null;

        // Five median-age bands, young to old. Tuned so a typical industrial society (~mid-30s) sits in
        // the middle yellow and only genuinely young/old structures reach the ends.
        private static readonly Color[] BandBase = new Color[]
        {
            new Color(0.30f, 0.70f, 0.35f, 0.55f),   // 0: green — youthful (< 25)
            new Color(0.62f, 0.78f, 0.25f, 0.55f),   // 1: yellow-green — young adult
            new Color(0.92f, 0.85f, 0.15f, 0.58f),   // 2: yellow — mature (mid-30s)
            new Color(0.95f, 0.55f, 0.12f, 0.62f),   // 3: orange — aging
            new Color(0.85f, 0.20f, 0.35f, 0.66f)    // 4: red — elderly / long-lived
        };

        // Upper bounds (inclusive) of the first four bands, in years; anything above the last is band 4.
        private static readonly int[] BandUpperAge = new int[] { 24, 34, 44, 59 };

        public static void InitializeMaterials()
        {
            if (ageMats != null) return;
            ageMats = new Material[BandBase.Length];
            for (int i = 0; i < BandBase.Length; i++)
            {
                Color color = BandBase[i];
                Material mat = null;
                if (ShaderDatabase.MetaOverlay != null && BaseContent.WhiteTex != null)
                {
                    mat = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.MetaOverlay, color, 3510);
                }
                if (mat == null) mat = SolidColorMaterials.SimpleSolidColorMaterial(color);
                if (mat == null) mat = BaseContent.WhiteMat;
                ageMats[i] = mat;
            }
        }

        public static void CacheData()
        {
            InitializeMaterials();
            PopulationDensityUtility.EnsureCache();   // ForRegion keys off the same population cache version
        }

        public override WorldLayer_MapMode WorldLayer => WorldLayer_MapMode_Terrain.Instance;
        public override bool CanToggleWater => false;

        public override void DoPreRegenerate()
        {
            base.DoPreRegenerate();
            CacheData();
        }

        public MapMode_AgeStructure() { }
        public MapMode_AgeStructure(MapModeDef def) : base(def) { }

        private static int MedianAgeForTile(int tile)
        {
            if (Find.World == null || Find.WorldGrid == null || tile < 0 || tile >= Find.WorldGrid.TilesCount) return 0;
            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            GeographicProvince province = mgr?.GetProvinceForTile(tile);
            if (province == null || province.provinceType != ProvinceType.Land) return 0;
            RegionDemographics demo = RegionDemographicsUtility.ForRegion(province);
            return demo.settledTiles > 0 ? demo.medianAge : 0;
        }

        private static int BandFor(int medianAge)
        {
            for (int i = 0; i < BandUpperAge.Length; i++)
                if (medianAge <= BandUpperAge[i]) return i;
            return BandBase.Length - 1;
        }

        public override Material GetMaterial(int tile)
        {
            if (Find.WorldGrid == null || tile >= Find.WorldGrid.TilesCount) return BaseContent.ClearMat;
            if (Find.WorldGrid[tile].WaterCovered) return BaseContent.ClearMat;

            int medianAge = MedianAgeForTile(tile);
            if (medianAge <= 0) return BaseContent.ClearMat;   // unsettled — leave the terrain unshaded

            if (ageMats == null) return BaseContent.ClearMat;
            return ageMats[BandFor(medianAge)];
        }

        public override string GetTileLabel(int tile)
        {
            int medianAge = MedianAgeForTile(tile);
            return medianAge > 0 ? medianAge.ToString() : null;
        }

        public override string GetTooltip(int tile)
        {
            if (Find.World == null) return null;
            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            GeographicProvince province = mgr?.GetProvinceForTile(tile);
            if (province == null || province.provinceType != ProvinceType.Land) return null;
            return RegionDemographicsUtility.AgeStructureSummary(province);
        }
    }
}
