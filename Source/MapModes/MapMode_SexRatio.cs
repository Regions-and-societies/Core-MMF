using MapModeFramework;
using RegionsAndSocieties.Demographics;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// The regional sex-ratio overlay (0.2.0, #11): every settled region is shaded by how its sex
    /// balance departs from the ~50/50 baseline — blue where men outnumber women, magenta where women
    /// outnumber men, a faint neutral where it is even. The baseline is deterministic and genuinely
    /// near-even, so a mostly-neutral map is honest data, not a blank; the colour appears where a
    /// mod-driven skew is in force (a draft in progress, a war's generational scar — see
    /// <see cref="DemographicHooks"/>). Reads the one shared aggregate
    /// (<see cref="RegionDemographicsUtility.ForRegion"/>); it paints, it does not compute.
    /// </summary>
    [StaticConstructorOnStartup]
    public class MapMode_SexRatio : MapMode
    {
        private static Material[] ratioMats = null;

        // Five bands across the female fraction, male-heavy to female-heavy, centred on an even split.
        private static readonly Color[] BandBase = new Color[]
        {
            new Color(0.20f, 0.45f, 0.85f, 0.60f),   // 0: strongly male
            new Color(0.45f, 0.65f, 0.90f, 0.50f),   // 1: male-leaning
            new Color(0.60f, 0.60f, 0.62f, 0.30f),   // 2: even — faint neutral
            new Color(0.85f, 0.55f, 0.80f, 0.50f),   // 3: female-leaning
            new Color(0.80f, 0.20f, 0.60f, 0.60f)    // 4: strongly female
        };

        // Upper bounds (inclusive) of the first four female-fraction bands; above the last is band 4.
        private static readonly float[] BandUpper = new float[] { 0.40f, 0.47f, 0.53f, 0.60f };

        public static void InitializeMaterials()
        {
            if (ratioMats != null) return;
            ratioMats = new Material[BandBase.Length];
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
                ratioMats[i] = mat;
            }
        }

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

        public MapMode_SexRatio() { }
        public MapMode_SexRatio(MapModeDef def) : base(def) { }

        // Returns the region's female fraction, or a negative sentinel when the tile has no settled
        // demographic data (unsettled land, water, off-map) so the caller leaves it unshaded.
        private static float FemaleFractionForTile(int tile)
        {
            if (Find.World == null || Find.WorldGrid == null || tile < 0 || tile >= Find.WorldGrid.TilesCount) return -1f;
            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            GeographicProvince province = mgr?.GetProvinceForTile(tile);
            if (province == null || province.provinceType != ProvinceType.Land) return -1f;
            RegionDemographics demo = RegionDemographicsUtility.ForRegion(province);
            return demo.settledTiles > 0 ? demo.femaleFraction : -1f;
        }

        private static int BandFor(float femaleFraction)
        {
            for (int i = 0; i < BandUpper.Length; i++)
                if (femaleFraction <= BandUpper[i]) return i;
            return BandBase.Length - 1;
        }

        public override Material GetMaterial(int tile)
        {
            if (Find.WorldGrid == null || tile >= Find.WorldGrid.TilesCount) return BaseContent.ClearMat;
            if (Find.WorldGrid[tile].WaterCovered) return BaseContent.ClearMat;

            float frac = FemaleFractionForTile(tile);
            if (frac < 0f) return BaseContent.ClearMat;   // unsettled — leave the terrain unshaded

            if (ratioMats == null) return BaseContent.ClearMat;
            return ratioMats[BandFor(frac)];
        }

        public override string GetTileLabel(int tile)
        {
            float frac = FemaleFractionForTile(tile);
            if (frac < 0f) return null;
            return Mathf.RoundToInt(frac * 100f) + "%";   // percent female
        }

        public override string GetTooltip(int tile)
        {
            if (Find.World == null) return null;
            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            GeographicProvince province = mgr?.GetProvinceForTile(tile);
            if (province == null || province.provinceType != ProvinceType.Land) return null;
            return RegionDemographicsUtility.SexRatioSummary(province);
        }
    }
}
