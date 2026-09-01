using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties.UI
{
    /// <summary>
    /// Draws a small silhouette of a province's actual shape (#26) inside a UI rect — the region's tiles
    /// projected from the globe onto the tangent plane at its centroid, normalised to fit. Gives the
    /// region panel a recognisable "this is the shape of the place" mini-map. Perimeter tiles (from the
    /// province topology aggregate) are drawn a shade darker so the outline reads. Pure projection +
    /// drawing; caches nothing, cheap enough for a per-frame panel since a province is a few hundred tiles.
    /// </summary>
    public static class RegionOutlineDrawer
    {
        public static void Draw(Rect rect, GeographicProvince province, Color fill)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.10f, 0.11f, 0.13f, 0.9f));
            Widgets.DrawBox(rect);

            WorldGrid grid = Find.WorldGrid;
            if (grid == null || province?.tiles == null || province.tiles.Count == 0) return;

            // Project every tile to a local 2D plane at the province centroid.
            Vector3 center = Vector3.zero;
            List<int> tiles = province.tiles;
            for (int i = 0; i < tiles.Count; i++) center += grid.GetTileCenter(tiles[i]);
            center /= tiles.Count;
            if (center.sqrMagnitude < 1e-6f) return;

            Vector3 n = center.normalized;
            // Any two axes tangent to the sphere at the centroid.
            Vector3 u = Vector3.Cross(n, Vector3.up);
            if (u.sqrMagnitude < 1e-4f) u = Vector3.Cross(n, Vector3.forward);
            u.Normalize();
            Vector3 v = Vector3.Cross(n, u).normalized;

            int count = tiles.Count;
            var px = new float[count];
            var py = new float[count];
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                Vector3 d = grid.GetTileCenter(tiles[i]) - center;
                float x = Vector3.Dot(d, u);
                float y = Vector3.Dot(d, v);
                px[i] = x; py[i] = y;
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
            }

            float spanX = Mathf.Max(1e-4f, maxX - minX);
            float spanY = Mathf.Max(1e-4f, maxY - minY);
            const float pad = 6f;
            float scale = Mathf.Min((rect.width - 2f * pad) / spanX, (rect.height - 2f * pad) / spanY);

            // Cell size from the tile spacing (a neighbour step), so the silhouette fills without gaps.
            float cell = Mathf.Max(2f, EstimateTileStep(grid, tiles[0]) * scale);
            float offX = rect.x + (rect.width - spanX * scale) / 2f;
            float offY = rect.y + (rect.height - spanY * scale) / 2f;

            var perimeter = province.perimeterTiles != null ? new HashSet<int>(province.perimeterTiles) : null;
            Color edge = new Color(fill.r * 0.55f, fill.g * 0.55f, fill.b * 0.55f, 1f);

            for (int i = 0; i < count; i++)
            {
                float sx = offX + (px[i] - minX) * scale;
                float sy = offY + (maxY - py[i]) * scale;   // flip: world +v up -> screen down
                var c = perimeter != null && perimeter.Contains(tiles[i]) ? edge : fill;
                Widgets.DrawBoxSolid(new Rect(sx - cell / 2f, sy - cell / 2f, cell, cell), c);
            }
        }

        /// <summary>The screen-independent distance to a tile's first neighbour — the cell pitch to draw at.</summary>
        private static float EstimateTileStep(WorldGrid grid, int tile)
        {
            var nb = new List<PlanetTile>();
            grid.GetTileNeighbors(tile, nb);
            if (nb.Count == 0) return 1f;
            return Mathf.Max(1e-4f, (grid.GetTileCenter(tile) - grid.GetTileCenter(nb[0].tileId)).magnitude);
        }
    }
}
