using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    /// <summary>
    /// #34: the per-tile demographic readout, drawn on the globe instead of as a hidden text tooltip. On a
    /// value-bearing overlay the tile under the cursor shows its value in <b>bold</b>, and the surrounding
    /// tiles out to an adjustable radius (default 1) show theirs — so hovering reveals a neighbourhood of
    /// values at a glance. A small radius panel appears while such an overlay is active; a map mode opts in
    /// by calling <see cref="Draw"/> from its <c>MapModeOnGUI</c> with a value function.
    /// </summary>
    public static class HoverNeighborhoodOverlay
    {
        /// <summary>How many tile-rings around the cursor also show their value. Adjustable in-panel; a
        /// static default (not scribed) — a per-session convenience, reset to 1 each launch.</summary>
        public static int Radius = 1;
        public const int MinRadius = 0;
        public const int MaxRadius = 4;

        private static readonly List<PlanetTile> nbScratch = new List<PlanetTile>();

        /// <summary>Draw the radius panel and, when a tile is hovered, the value neighbourhood around it.
        /// <paramref name="valueFor"/> returns a tile's display value, or null to leave that tile blank
        /// (e.g. ocean). Call once per frame from an active overlay's <c>MapModeOnGUI</c>.</summary>
        public static void Draw(Func<int, string> valueFor)
        {
            if (valueFor == null) return;
            DrawRadiusPanel();

            if (!WorldRendererUtility.WorldRendered) return;
            WorldGrid grid = Find.WorldGrid;
            if (grid == null) return;

            PlanetTile mouse = GenWorld.MouseTile(false);
            if (!mouse.Valid || mouse.tileId < 0 || mouse.tileId >= grid.TilesCount) return;

            List<int> tiles = Neighborhood(grid, mouse, Radius);

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;

            float half = Mathf.Max(18f, GenWorldUI.CurUITileSize());
            for (int i = 0; i < tiles.Count; i++)
            {
                int t = tiles[i];
                string val = valueFor(t);
                if (string.IsNullOrEmpty(val)) continue;

                Vector3 center = grid.GetTileCenter(t);
                if (WorldRendererUtility.HiddenBehindTerrainNow(center)) continue;   // on the far side of the globe

                Vector2 ui = GenWorldUI.WorldToUIPosition(center);
                var rect = new Rect(ui.x - half, ui.y - 11f, half * 2f, 22f);
                bool hovered = t == mouse.tileId;
                // A dark plate behind the text so it reads over any terrain colour, brighter for the hovered tile.
                GUI.color = new Color(0f, 0f, 0f, hovered ? 0.55f : 0.35f);
                GUI.DrawTexture(rect, BaseContent.BlackTex);
                GUI.color = hovered ? Color.white : new Color(1f, 1f, 1f, 0.8f);
                Widgets.Label(rect, hovered ? "<b>" + val + "</b>" : val);
            }

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        /// <summary>All tiles within <paramref name="radius"/> adjacency hops of the centre (a disk), by BFS
        /// over the world grid. Radius 0 = just the hovered tile, 1 = it and its immediate neighbours.</summary>
        private static List<int> Neighborhood(WorldGrid grid, PlanetTile center, int radius)
        {
            var result = new List<int> { center.tileId };
            if (radius <= 0) return result;

            var seen = new HashSet<int> { center.tileId };
            var frontier = new List<PlanetTile> { center };
            for (int r = 0; r < radius; r++)
            {
                var next = new List<PlanetTile>();
                for (int i = 0; i < frontier.Count; i++)
                {
                    grid.GetTileNeighbors(frontier[i], nbScratch);
                    for (int j = 0; j < nbScratch.Count; j++)
                    {
                        PlanetTile nb = nbScratch[j];
                        if (nb.tileId >= 0 && seen.Add(nb.tileId)) { result.Add(nb.tileId); next.Add(nb); }
                    }
                }
                frontier = next;
            }
            return result;
        }

        private static void DrawRadiusPanel()
        {
            var panel = new Rect(12f, 92f, 180f, 60f);
            Widgets.DrawWindowBackground(panel);
            var inner = panel.ContractedBy(8f);

            var label = new Rect(inner.x, inner.y, inner.width, 22f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(label, "Inspect radius: " + Radius);
            Text.Anchor = TextAnchor.UpperLeft;

            var row = new Rect(inner.x, inner.yMax - 24f, inner.width, 24f);
            var minus = new Rect(row.x, row.y, 40f, row.height);
            var plus = new Rect(row.xMax - 40f, row.y, 40f, row.height);
            if (Widgets.ButtonText(minus, "-")) Radius = Mathf.Max(MinRadius, Radius - 1);
            if (Widgets.ButtonText(plus, "+")) Radius = Mathf.Min(MaxRadius, Radius + 1);
        }
    }
}
