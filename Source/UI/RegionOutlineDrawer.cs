using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties.UI
{
    /// <summary>
    /// Draws a small silhouette of a province's actual shape (#26) inside a UI rect. Each tile is
    /// projected through the SAME world camera the planet is drawn with
    /// (<see cref="GenWorldUI.WorldToUIPosition"/>), so the silhouette matches the region's on-screen
    /// orientation exactly — same rotation, same handedness — rather than an arbitrary tangent basis that
    /// could come out flipped or turned. Perimeter tiles (from the province topology aggregate) are drawn
    /// a shade darker so the outline reads. Cheap enough per-frame: a province is a few hundred tiles.
    /// </summary>
    public static class RegionOutlineDrawer
    {
        public static void Draw(Rect rect, GeographicProvince province, Color fill)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.10f, 0.11f, 0.13f, 0.9f));
            Widgets.DrawBox(rect);

            WorldGrid grid = Find.WorldGrid;
            if (grid == null || province?.tiles == null || province.tiles.Count == 0) return;

            // Project every tile to its on-screen UI position. UI space is y-down, the same as our rect,
            // so normalising these straight in (no flip) reproduces the region the same way up as the map.
            List<int> tiles = province.tiles;
            int count = tiles.Count;
            var px = new float[count];
            var py = new float[count];
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                Vector2 ui = GenWorldUI.WorldToUIPosition(grid.GetTileCenter(tiles[i]));
                px[i] = ui.x; py[i] = ui.y;
                if (ui.x < minX) minX = ui.x; if (ui.x > maxX) maxX = ui.x;
                if (ui.y < minY) minY = ui.y; if (ui.y > maxY) maxY = ui.y;
            }

            float spanX = Mathf.Max(1e-4f, maxX - minX);
            float spanY = Mathf.Max(1e-4f, maxY - minY);
            const float pad = 6f;
            float scale = Mathf.Min((rect.width - 2f * pad) / spanX, (rect.height - 2f * pad) / spanY);

            // Cell pitch from the on-screen tile size, scaled by the fit, so the silhouette fills the box.
            float cell = Mathf.Clamp(GenWorldUI.CurUITileSize() * scale, 2f, rect.width * 0.25f);
            float offX = rect.x + (rect.width - spanX * scale) / 2f;
            float offY = rect.y + (rect.height - spanY * scale) / 2f;

            var perimeter = province.perimeterTiles != null ? new HashSet<int>(province.perimeterTiles) : null;
            Color edge = new Color(fill.r * 0.55f, fill.g * 0.55f, fill.b * 0.55f, 1f);

            for (int i = 0; i < count; i++)
            {
                float sx = offX + (px[i] - minX) * scale;
                float sy = offY + (py[i] - minY) * scale;   // UI y-down: matches the screen, no flip
                var c = perimeter != null && perimeter.Contains(tiles[i]) ? edge : fill;
                Widgets.DrawBoxSolid(new Rect(sx - cell / 2f, sy - cell / 2f, cell, cell), c);
            }
        }
    }
}
