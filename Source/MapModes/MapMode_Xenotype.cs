using System.Collections.Generic;
using MapModeFramework;
using RegionsAndSocieties.Demographics;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// The regional xenotype overlay (0.2.0, #12): every settled region is tinted by its dominant
    /// xenotype, with the tint strengthening as that xenotype's share grows — a monoculture reads
    /// solid, a mixed region reads pale. Colours are derived deterministically from the xenotype's
    /// defName, so mod-added castes flow through automatically with their own stable colour and no
    /// hardcoded list. With Biotech off there are no xenotypes to show — every pawn is Baseliner — so
    /// the map is left unshaded and the tooltip says so rather than painting a flat map as if it were
    /// data. Reads the one shared aggregate (<see cref="RegionDemographicsUtility.ForRegion"/>).
    /// </summary>
    [StaticConstructorOnStartup]
    public class MapMode_Xenotype : MapMode
    {
        // One material per (xenotype, dominance band). Built lazily — the set of xenotypes in a world is
        // small and stable, so this caps at a handful of materials each.
        private static readonly Dictionary<XenotypeDef, Material[]> xenotypeMats = new Dictionary<XenotypeDef, Material[]>();

        // Alpha per dominance band: a barely-dominant region is faint, a near-monoculture is solid.
        private static readonly float[] BandAlpha = new float[] { 0.35f, 0.52f, 0.70f };
        // Upper bounds (inclusive) of the first two dominant-share bands; above the last is band 2.
        private static readonly float[] BandUpperShare = new float[] { 0.45f, 0.70f };

        public static void CacheData()
        {
            PopulationDensityUtility.EnsureCache();
        }

        public override WorldLayer_MapMode WorldLayer => WorldLayer_MapMode_Terrain.Instance;
        public override bool CanToggleWater => false;

        public override void DoPreRegenerate()
        {
            base.DoPreRegenerate();
            CacheData();
        }

        public MapMode_Xenotype() { }
        public MapMode_Xenotype(MapModeDef def) : base(def) { }

        // A stable hue from the xenotype's defName (FNV-1a), so each caste keeps one recognisable colour
        // within and across sessions without any saved state or hardcoded table.
        private static Color BaseColorFor(XenotypeDef xenotype)
        {
            uint h = 2166136261u;
            string name = xenotype.defName ?? "";
            for (int i = 0; i < name.Length; i++) { h ^= name[i]; h *= 16777619u; }
            float hue = (h % 3600u) / 3600f;                 // 0..1 around the wheel
            float sat = 0.55f + ((h >> 12) % 100u) / 400f;   // 0.55..0.80
            float val = 0.80f + ((h >> 20) % 100u) / 500f;   // 0.80..1.00
            return Color.HSVToRGB(hue, Mathf.Clamp01(sat), Mathf.Clamp01(val));
        }

        private static int BandForShare(float share)
        {
            for (int i = 0; i < BandUpperShare.Length; i++)
                if (share <= BandUpperShare[i]) return i;
            return BandAlpha.Length - 1;
        }

        private static Material MaterialFor(XenotypeDef xenotype, float share)
        {
            if (!xenotypeMats.TryGetValue(xenotype, out Material[] bands))
            {
                bands = new Material[BandAlpha.Length];
                Color rgb = BaseColorFor(xenotype);
                for (int b = 0; b < BandAlpha.Length; b++)
                {
                    Color color = new Color(rgb.r, rgb.g, rgb.b, BandAlpha[b]);
                    Material mat = null;
                    if (ShaderDatabase.MetaOverlay != null && BaseContent.WhiteTex != null)
                    {
                        mat = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.MetaOverlay, color, 3510);
                    }
                    if (mat == null) mat = SolidColorMaterials.SimpleSolidColorMaterial(color);
                    if (mat == null) mat = BaseContent.WhiteMat;
                    bands[b] = mat;
                }
                xenotypeMats[xenotype] = bands;
            }
            return bands[BandForShare(share)];
        }

        private static RegionDemographics DemoForTile(int tile)
        {
            if (Find.World == null || Find.WorldGrid == null || tile < 0 || tile >= Find.WorldGrid.TilesCount) return null;
            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            GeographicProvince province = mgr?.GetProvinceForTile(tile);
            if (province == null || province.provinceType != ProvinceType.Land) return null;
            RegionDemographics demo = RegionDemographicsUtility.ForRegion(province);
            return demo.settledTiles > 0 ? demo : null;
        }

        public override Material GetMaterial(int tile)
        {
            if (Find.WorldGrid == null || tile >= Find.WorldGrid.TilesCount) return BaseContent.ClearMat;
            if (Find.WorldGrid[tile].WaterCovered) return BaseContent.ClearMat;

            RegionDemographics demo = DemoForTile(tile);
            if (demo == null || !demo.biotechActive) return BaseContent.ClearMat;   // no xenotypes to show

            XenotypeDef dominant = RegionDemographicsUtility.DominantXenotype(demo, out float share);
            if (dominant == null) return BaseContent.ClearMat;
            return MaterialFor(dominant, share);
        }

        public override string GetTileLabel(int tile)
        {
            RegionDemographics demo = DemoForTile(tile);
            if (demo == null || !demo.biotechActive) return null;
            XenotypeDef dominant = RegionDemographicsUtility.DominantXenotype(demo, out float share);
            return dominant != null ? $"{dominant.LabelCap} {Mathf.RoundToInt(share * 100f)}%" : null;
        }

        public override string GetTooltip(int tile)
        {
            if (Find.World == null) return null;
            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            GeographicProvince province = mgr?.GetProvinceForTile(tile);
            if (province == null || province.provinceType != ProvinceType.Land) return null;
            return RegionDemographicsUtility.XenotypeSummary(province);
        }
    }
}
