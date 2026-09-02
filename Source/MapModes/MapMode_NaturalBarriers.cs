using System.Collections.Generic;
using MapModeFramework;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// A debug overlay (#20) that paints ONLY the world's natural barriers, each type its own colour —
    /// impassable rock, open water, high-ground ridges, swamp, and biome edges — and leaves open,
    /// same-biome land unshaded. It shows the terrain's own division of the map directly, so the barriers
    /// a region partition should follow are visible. This is the tuning surface for combining the
    /// square-cell fill with barrier-following into a better region algorithm.
    ///
    /// <para>Materials are a FIXED small palette pre-built on the main thread in <see cref="DoPreRegenerate"/>,
    /// so the worker-thread mesh build (<c>GetMaterial</c>) only ever reads them — Unity forbids creating a
    /// material off the main thread.</para>
    /// </summary>
    [StaticConstructorOnStartup]
    public class MapMode_NaturalBarriers : MapMode
    {
        private enum Barrier { None = 0, Impassable, Water, Mountain, LargeHill, Swamp, BiomeEdge, SmallHill }

        // One colour per barrier type; None is unshaded. Strong hard walls read dark/solid, soft edges faint.
        private static readonly Color[] Colors =
        {
            Color.clear,                             // None
            new Color(0.10f, 0.10f, 0.12f, 0.85f),   // Impassable — near-black rock / sea ice
            new Color(0.20f, 0.42f, 0.75f, 0.75f),   // Water — blue
            new Color(0.45f, 0.30f, 0.20f, 0.78f),   // Mountain — dark brown ridge
            new Color(0.62f, 0.46f, 0.30f, 0.62f),   // Large hill — medium brown
            new Color(0.28f, 0.42f, 0.28f, 0.70f),   // Swamp — murky green
            new Color(0.90f, 0.58f, 0.18f, 0.58f),   // Biome edge — orange
            new Color(0.72f, 0.64f, 0.50f, 0.42f),   // Small hill — faint tan (weak barrier)
        };
        private static Material[] mats;

        public MapMode_NaturalBarriers() { }
        public MapMode_NaturalBarriers(MapModeDef def) : base(def) { }

        public override WorldLayer_MapMode WorldLayer => WorldLayer_MapMode_Terrain.Instance;

        public override void DoPreRegenerate()
        {
            base.DoPreRegenerate();
            if (mats != null) return;
            mats = new Material[Colors.Length];
            for (int i = 0; i < Colors.Length; i++)
                mats[i] = i == 0 ? BaseContent.ClearMat : MakeMat(Colors[i]);
        }

        private static Material MakeMat(Color c)
        {
            Material m = (ShaderDatabase.MetaOverlay != null && BaseContent.WhiteTex != null)
                ? MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.MetaOverlay, c, 3510)
                : SolidColorMaterials.SimpleSolidColorMaterial(c);
            return m ?? BaseContent.WhiteMat;
        }

        public override Material GetMaterial(int tile)
        {
            if (mats == null) return BaseContent.ClearMat;   // pre-build hasn't run; never create off-thread
            return mats[(int)BarrierOf(tile)];
        }

        public override string GetTileLabel(int tile)
        {
            Barrier b = BarrierOf(tile);
            return b == Barrier.None ? null : b.ToString();
        }

        public override string GetTooltip(int tile)
        {
            Barrier b = BarrierOf(tile);
            return b == Barrier.None ? "Open land (no natural barrier)" : "Natural barrier: " + b;
        }

        /// <summary>The strongest natural barrier a tile represents, hard walls first. Reads tile terrain
        /// only — no material creation — so it is safe from the worker thread.</summary>
        private static Barrier BarrierOf(int tile)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tile < 0 || tile >= grid.TilesCount) return Barrier.None;
            Tile t = grid[tile];
            if (t == null) return Barrier.None;

            BiomeDef biome = t.PrimaryBiome;
            if (t.hilliness == Hilliness.Impassable || (biome != null && (biome.impassable || biome.defName == "SeaIce")))
                return Barrier.Impassable;
            if (t.WaterCovered) return Barrier.Water;
            if (t.hilliness == Hilliness.Mountainous) return Barrier.Mountain;
            if (t.hilliness == Hilliness.LargeHills) return Barrier.LargeHill;

            bool swamp = t.swampiness > 0.1f
                || (biome != null && (biome.defName.Contains("Swamp") || biome.defName.Contains("Marsh")));
            if (swamp) return Barrier.Swamp;

            if (IsBiomeEdge(grid, tile, biome)) return Barrier.BiomeEdge;
            if (t.hilliness == Hilliness.SmallHills) return Barrier.SmallHill;
            return Barrier.None;
        }

        /// <summary>True when a land neighbour sits in a different biome — a soft, walkable border the
        /// partition may want to snap to.</summary>
        private static bool IsBiomeEdge(WorldGrid grid, int tile, BiomeDef biome)
        {
            if (biome == null) return false;
            var neighbours = new List<PlanetTile>();
            grid.GetTileNeighbors(tile, neighbours);
            for (int i = 0; i < neighbours.Count; i++)
            {
                int n = neighbours[i].tileId;
                if (n < 0 || n >= grid.TilesCount) continue;
                Tile nt = grid[n];
                if (nt != null && !nt.WaterCovered && nt.PrimaryBiome != null && nt.PrimaryBiome != biome) return true;
            }
            return false;
        }
    }
}
