using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties
{
    public class Dialog_FactionPlacementSettings : Window
    {
        private Vector2 scrollPosition = Vector2.zero;
        private List<FactionDef> activeFactions;

        // 0.7 added the world-object integration panel, so the window needs a little more height.
        public override Vector2 InitialSize => new Vector2(860f, 780f);

        public Dialog_FactionPlacementSettings()
        {
            this.closeOnClickedOutside = true;
            this.absorbInputAroundWindow = true;

            activeFactions = DefDatabase<FactionDef>.AllDefs
                .Where(f => !f.isPlayer && !f.hidden)
                .OrderBy(f => f.defName)
                .ToList();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 35f), "Regions and Societies - Geographic Placement Settings");
            Text.Font = GameFont.Small;

            // Retrieve current planet coverage from Page_CreateWorldParams if open
            float coverage = 0.3f;
            var page = Find.WindowStack.WindowOfType<Page_CreateWorldParams>();
            if (page != null)
            {
                var field = typeof(Page_CreateWorldParams).GetField("planetCoverage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (field != null)
                {
                    coverage = (float)field.GetValue(page);
                }
            }

            int target = FactionPlacementSettings.targetRegionSize;

            // Land tiles: count the real grid when a world exists (exact); otherwise estimate from planet
            // size × a typical land fraction (pre-gen — shown to the player as an assumption).
            int landTiles;
            bool landFromGrid = false;
            if (Find.WorldGrid != null && Find.WorldGrid.TilesCount > 0)
            {
                int lt = 0, tc = Find.WorldGrid.TilesCount;
                for (int i = 0; i < tc; i++) if (!Find.WorldGrid[i].WaterCovered) lt++;
                landTiles = lt;
                landFromGrid = true;
            }
            else
            {
                int totalTiles = Mathf.RoundToInt(100000f * coverage);
                landTiles = Placement.PlacementEstimates.EstimateLandTiles(totalTiles, Placement.PlacementEstimates.TypicalLandFraction);
            }

            // If the world's regions are already generated, report the ACTUAL count; else the estimate band.
            int actualRegions = -1;
            var regionMgr = Find.World != null ? Find.World.GetComponent<SynapseRegionManager>() : null;
            if (regionMgr != null && regionMgr.Provinces != null)
            {
                int c = 0;
                foreach (var pr in regionMgr.Provinces)
                    if (pr.provinceType == ProvinceType.Land && pr.tiles != null && pr.tiles.Count > 0) c++;
                if (c > 0) actualRegions = c;
            }
            int estLo = Placement.PlacementEstimates.ExpectedRegionCountLow(landTiles, target);
            int estHi = Placement.PlacementEstimates.ExpectedRegionCountHigh(landTiles, target);

            // #47: the shared denominators for every per-faction "≈ N regions" estimate. The region basis is
            // the actual count once a world exists, else the mid estimate; the placed total scales that by
            // the density knob; the total share weight normalises the per-faction slices.
            int regionBasis = actualRegions > 0 ? actualRegions : Placement.PlacementEstimates.ExpectedRegionCount(landTiles, target);
            int placedTotal = Placement.PlacementShareRules.PlacedTotal(regionBasis, FactionPlacementSettings.claimedLandAreaPercent);
            float totalShareWeight = 0f;
            foreach (var d in activeFactions) totalShareWeight += FactionPlacementSettings.GetProfile(d).placementShare;

            // Global Map Region Parameters Panel
            Rect globalBoxRect = new Rect(0f, 40f, inRect.width - 15f, 160f);
            Widgets.DrawMenuSection(globalBoxRect);

            Rect globalTitleRect = new Rect(10f, 44f, 300f, 22f);
            Widgets.Label(globalTitleRect, "<b>Global Map Region Parameters</b>");

            // #47: basic/advanced view toggle, top-right of the global box.
            bool advanced = FactionPlacementSettings.placementUiAdvanced;
            Rect toggleRect = new Rect(globalBoxRect.xMax - 150f, 44f, 140f, 24f);
            if (Widgets.ButtonText(toggleRect, advanced ? "View: Advanced" : "View: Basic"))
            {
                FactionPlacementSettings.placementUiAdvanced = !advanced;
                advanced = FactionPlacementSettings.placementUiAdvanced;
            }
            TooltipHandler.TipRegion(toggleRect,
                "Basic: one row per faction — just the share of the map each faction gets.\n\n" +
                "Advanced: the full per-faction controls (resource weights, placement order, clustering) plus mod-integration governance.");

            // Left Column: one Target size knob (the merge floor derives as half of it).
            float colWidth = (globalBoxRect.width - 30f) / 2f;
            Rect targetLabelRect = new Rect(10f, 68f, 135f, 22f);
            Widgets.Label(targetLabelRect, $"Target size: {FactionPlacementSettings.targetRegionSize}");
            Rect targetSliderRect = new Rect(150f, 70f, colWidth - 155f, 18f);
            float tempTarget = Widgets.HorizontalSlider(targetSliderRect, FactionPlacementSettings.targetRegionSize, 50f, 400f, false, null, null, null, 1f);
            FactionPlacementSettings.targetRegionSize = Mathf.RoundToInt(tempTarget);

            // Right Column: what the target means — sparse biomes scale up on their own.
            float rightColStart = 10f + colWidth + 10f;
            Rect noteRect = new Rect(rightColStart, 64f, colWidth - 145f, 34f);
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(noteRect, "tiles per region. Sparse biomes (desert, tundra, ice) scale up automatically to fewer, larger regions.");
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            // Second Row (Max Threat / Max Occupancy)
            Rect threatLabelRect = new Rect(10f, 98f, 150f, 22f);
            Widgets.Label(threatLabelRect, $"Max Threat: {Mathf.RoundToInt(FactionPlacementSettings.maxThreatPercent * 100f)}%");
            Rect threatSliderRect = new Rect(165f, 100f, colWidth - 170f, 18f);
            float tempThreat = Widgets.HorizontalSlider(threatSliderRect, FactionPlacementSettings.maxThreatPercent, 0.10f, 1.00f, false, null, null, null, 0.01f);
            FactionPlacementSettings.maxThreatPercent = tempThreat;

            Rect occupLabelRect = new Rect(rightColStart, 98f, 165f, 22f);
            Widgets.Label(occupLabelRect, $"Claimed land area: {Mathf.RoundToInt(FactionPlacementSettings.claimedLandAreaPercent * 100f)}%");
            Rect occupSliderRect = new Rect(rightColStart + 165f, 100f, colWidth - 170f, 18f);
            float tempOccup = Widgets.HorizontalSlider(occupSliderRect, FactionPlacementSettings.claimedLandAreaPercent, 0.10f, 0.90f, false, null, null, null, 0.01f);
            FactionPlacementSettings.claimedLandAreaPercent = tempOccup;

            // Estimates row
            Rect estRect = new Rect(10f, 135f, globalBoxRect.width - 20f, 22f);
            string landPart = landFromGrid
                ? $"Land tiles: <color=cyan>{landTiles}</color>"
                : $"Est. land tiles: <color=cyan>{landTiles}</color> (~{Mathf.RoundToInt(Placement.PlacementEstimates.TypicalLandFraction * 100f)}% of a {Mathf.RoundToInt(coverage * 100f)}%-coverage planet)";
            string countPart = actualRegions > 0
                ? $"Regions: <color=green>{actualRegions}</color> (this world)"
                : $"Expected regions: <color=green>{estLo}–{estHi}</color>";
            Widgets.Label(estRect, landPart + "  |  " + countPart + $"  |  Territories placed: <color=orange>{placedTotal}</color>");

            if (advanced)
            {
                // Box is tall enough for the "Detected:" status line at the bottom (title + master +
                // four 24px rows + the status row need ~178px); outRect starts below it so the label
                // can't spill onto the Faction Geography scroll panel (#47).
                DrawIntegrationPanel(new Rect(0f, 205f, inRect.width - 15f, 178f));
                DrawAdvancedCards(inRect, 388f, totalShareWeight, placedTotal);
            }
            else
            {
                DrawBasicRows(inRect, 205f, totalShareWeight, placedTotal);
            }

            Rect closeButtonRect = new Rect(inRect.width / 2f - 75f, inRect.height - 45f, 150f, 35f);
            if (Widgets.ButtonText(closeButtonRect, "Close"))
            {
                this.Close();
            }
        }

        /// <summary>
        /// #47 basic view: one compact row per faction — its share of the placed territories and the live
        /// "≈ N regions" that share works out to. Nothing else; the point of basic mode is that most
        /// players only want to say how much of the map each faction gets. Fits ≤ 12 factions without
        /// scrolling.
        /// </summary>
        private void DrawBasicRows(Rect inRect, float top, float totalShareWeight, int placedTotal)
        {
            // Header: what the shares mean and the running total (normalised at worldgen).
            Rect headerRect = new Rect(0f, top, inRect.width - 15f, 22f);
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            Widgets.Label(headerRect,
                $"Share of placed territories — shares need not add to 100%; worldgen normalises by the total " +
                $"(<color=cyan>{Mathf.RoundToInt(totalShareWeight)}</color>).");
            GUI.color = Color.white;

            float rowH = 34f;
            Rect outRect = new Rect(0f, top + 26f, inRect.width, inRect.height - (top + 26f) - 55f);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 25f, activeFactions.Count * rowH);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            float curY = 0f;
            foreach (var def in activeFactions)
            {
                var profile = FactionPlacementSettings.GetProfile(def);

                Rect rowRect = new Rect(0f, curY, viewRect.width, rowH - 4f);
                Widgets.DrawMenuSection(rowRect);

                // Name (left).
                Rect nameRect = new Rect(rowRect.x + 8f, rowRect.y + 3f, 230f, 24f);
                Widgets.Label(nameRect, $"<b>{def.LabelCap}</b>");

                // Share slider (middle).
                float sliderX = nameRect.xMax + 8f;
                float sliderW = 200f;
                Rect shareLabelRect = new Rect(sliderX, rowRect.y + 3f, 70f, 24f);
                Widgets.Label(shareLabelRect, $"Share: {Mathf.RoundToInt(profile.placementShare)}%");
                Rect shareSliderRect = new Rect(sliderX + 72f, rowRect.y + 6f, sliderW, 18f);
                float tempShare = Widgets.HorizontalSlider(shareSliderRect, profile.placementShare, 0f, 100f, false, null, null, null, 1f);
                profile.placementShare = tempShare;

                // Estimate (right).
                int est = Placement.PlacementShareRules.EstimatedFactionCount(profile.placementShare, totalShareWeight, placedTotal);
                Rect estCellRect = new Rect(shareSliderRect.xMax + 12f, rowRect.y + 3f, 130f, 24f);
                Widgets.Label(estCellRect, $"≈ <color=green>{est}</color> regions");

                // Reset (far right).
                Rect resetRect = new Rect(rowRect.xMax - 96f, rowRect.y + 3f, 88f, 22f);
                if (Widgets.ButtonText(resetRect, "Reset"))
                {
                    profile.placementShare = FactionPlacementSettings.DefaultShare(def);
                }

                curY += rowH;
            }
            Widgets.EndScrollView();
        }

        /// <summary>
        /// #47 advanced view: the full per-faction card — resource weights, placement order, clustering —
        /// with the old Settlement Range replaced by the same share row basic mode shows.
        /// </summary>
        private void DrawAdvancedCards(Rect inRect, float top, float totalShareWeight, int placedTotal)
        {
            Rect outRect = new Rect(0f, top, inRect.width, inRect.height - top - 55f);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 25f, activeFactions.Count * 295f);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            float curY = 0f;

            foreach (var def in activeFactions)
            {
                var profile = FactionPlacementSettings.GetProfile(def);

                Rect boxRect = new Rect(0f, curY, viewRect.width, 285f);
                Widgets.DrawMenuSection(boxRect);

                Rect titleRect = new Rect(10f, curY + 10f, boxRect.width - 20f, 25f);
                Widgets.Label(titleRect, $"<b>{def.LabelCap} ({def.defName}) - Tech: {def.techLevel}</b>");

                Rect resetRect = new Rect(boxRect.width - 120f, curY + 8f, 100f, 22f);
                if (Widgets.ButtonText(resetRect, "Reset Default"))
                {
                    var defaultProfile = FactionPlacementSettings.GetDefaultProfile(def);
                    profile.mineralWeight = defaultProfile.mineralWeight;
                    profile.nutritionWeight = defaultProfile.nutritionWeight;
                    profile.forageWeight = defaultProfile.forageWeight;
                    profile.grazingWeight = defaultProfile.grazingWeight;
                    profile.huntingWeight = defaultProfile.huntingWeight;
                    profile.marginWeight = defaultProfile.marginWeight;
                    profile.placementShare = defaultProfile.placementShare;
                    profile.placementOrder = defaultProfile.placementOrder;
                    profile.clusterSize = defaultProfile.clusterSize;
                }

                // Left column sliders
                float leftY = curY + 40f;
                DrawWeightSlider(ref leftY, boxRect.width / 2f - 15f, 10f, "Mineral (Mountains/Hills)", ref profile.mineralWeight, 0f, 5f);
                DrawWeightSlider(ref leftY, boxRect.width / 2f - 15f, 10f, "Nutrition (Agricultural Plains)", ref profile.nutritionWeight, 0f, 5f);
                DrawWeightSlider(ref leftY, boxRect.width / 2f - 15f, 10f, "Forage (Neolithic Foraging)", ref profile.forageWeight, 0f, 5f);

                // Right column sliders
                float rightY = curY + 40f;
                DrawWeightSlider(ref rightY, boxRect.width / 2f - 15f, boxRect.width / 2f + 5f, "Grazing (Open Grasslands)", ref profile.grazingWeight, 0f, 5f);
                DrawWeightSlider(ref rightY, boxRect.width / 2f - 15f, boxRect.width / 2f + 5f, "Hunting (Forests/Wilds)", ref profile.huntingWeight, 0f, 5f);
                DrawWeightSlider(ref rightY, boxRect.width / 2f - 15f, boxRect.width / 2f + 5f, "Margin (Desert/Tundra Edges)", ref profile.marginWeight, 0f, 5f);

                // #47: share of placed territories + live estimate — replaces the old Settlement Range.
                // baseCountRange is no longer a setting; the count is derived from this share at worldgen.
                Rect shareRect = new Rect(10f, curY + 180f, boxRect.width - 20f, 24f);
                int est = Placement.PlacementShareRules.EstimatedFactionCount(profile.placementShare, totalShareWeight, placedTotal);
                Rect shareLabelRect = new Rect(shareRect.x, shareRect.y, 300f, 24f);
                Widgets.Label(shareLabelRect, $"Share of placed territories: {Mathf.RoundToInt(profile.placementShare)}%  (≈ <color=green>{est}</color> regions)");
                TooltipHandler.TipRegion(shareLabelRect,
                    "This faction's slice of the placed territories. Shares across factions need not add to 100% — " +
                    "worldgen normalises by the total, so a share is a relative weight, not a hard quota. " +
                    "The estimate is this share of the territories the map allows (region count × claimed land area).");
                float tempShare = Widgets.HorizontalSlider(new Rect(shareRect.x + 310f, shareRect.y, shareRect.width - 320f, 18f), profile.placementShare, 0f, 100f, false, null, "0%", "100%", 1f);
                profile.placementShare = tempShare;

                // Placement Order
                Rect orderRect = new Rect(10f, curY + 215f, boxRect.width - 20f, 24f);
                Widgets.Label(new Rect(orderRect.x, orderRect.y, 250f, 24f), $"Placement Turn Order (Priority): {profile.placementOrder}");
                float tempOrder = Widgets.HorizontalSlider(new Rect(orderRect.x + 260f, orderRect.y, orderRect.width - 270f, 18f), (float)profile.placementOrder, 1f, 10f, false, null, null, null, 1f);
                profile.placementOrder = Mathf.RoundToInt(tempOrder);

                // #46 cluster size: 1 / 3 / 5 / 7 / 9+ — how many territories may cluster together (the
                // largest contiguous body). A soft maximum: the faction looks for ground where it cannot
                // cluster first and fills in against itself only when nothing else is left.
                Rect clusterRect = new Rect(10f, curY + 245f, boxRect.width - 20f, 24f);
                int cap = Placement.ClusteringRules.Snap(profile.clusterSize);
                Widgets.Label(new Rect(clusterRect.x, clusterRect.y, 250f, 24f), $"Cluster size (territories together): {Placement.ClusteringRules.Label(cap)}");
                TooltipHandler.TipRegion(new Rect(clusterRect.x, clusterRect.y, 250f, 24f),
                    "The largest contiguous body of territory this faction builds. 1 = every holding stands alone, 9+ = one contiguous nation. " +
                    "A maximum, not a wall: the faction prefers ground where it cannot cluster, even slightly worse ground, and only fills in against itself when nothing else is left. " +
                    "Smaller clusters are seeded first so they can find isolated ground. Defaults: pirates and the Empire 3, tribes 5, rough unions 7, everyone else 9+.");
                Rect clusterSlider = new Rect(clusterRect.x + 260f, clusterRect.y, clusterRect.width - 270f, 18f);
                float tempCluster = Widgets.HorizontalSlider(clusterSlider, (float)cap, 1f, 9f, false, null, "1", "9+", 2f);
                profile.clusterSize = Placement.ClusteringRules.Snap(Mathf.RoundToInt(tempCluster));

                curY += 295f;
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// 0.7: switches for the world-object governance layer. Every mod integration and every
        /// governed mechanic is optional, so a player who only wants vanilla behaviour can get it
        /// from this panel without uninstalling anything.
        /// </summary>
        private void DrawIntegrationPanel(Rect box)
        {
            Widgets.DrawMenuSection(box);

            Rect titleRect = new Rect(box.x + 10f, box.y + 4f, box.width - 20f, 22f);
            Widgets.Label(titleRect, "<b>World Object Mod Integration</b>");

            Rect masterRect = new Rect(box.x + 10f, box.y + 28f, box.width - 20f, 22f);
            Widgets.CheckboxLabeled(masterRect, "Govern world objects added by other mods",
                ref Integration.WorldObjectIntegrationSettings.masterEnabled);
            TooltipHandler.TipRegion(masterRect,
                "Master switch. When off, Regions & Territories recognises only vanilla settlements and leaves modded outposts, camps, and bases entirely alone.");

            bool on = Integration.WorldObjectIntegrationSettings.masterEnabled;
            float colW = (box.width - 20f) / 4f;

            // Row 1 — per-mod integrations.
            float y = box.y + 52f;
            // Row 1 held the four per-mod integration toggles until the compatibility inversion
            // (Core-MMF#3) moved every foreign-mod integration into its own patch. A patch is
            // enabled by being installed, so no per-mod toggles remain — only the master switch
            // and the per-mechanic rows below.

            // Row 2 — which mechanics the integration is allowed to drive.
            y += 24f;
            IntegrationToggle(new Rect(box.x + 10f + colW * 0f, y, colW - 8f, 22f), "Placement rules",
                ref Integration.WorldObjectIntegrationSettings.placementGovernance, on,
                "Apply region ownership, buffer distance, and supply range to where modded objects can be built.");
            IntegrationToggle(new Rect(box.x + 10f + colW * 1f, y, colW - 8f, 22f), "Economy rules",
                ref Integration.WorldObjectIntegrationSettings.economyGovernance, on,
                "Scale modded production by regional security, local resource richness, and surrounding population.");
            IntegrationToggle(new Rect(box.x + 10f + colW * 2f, y, colW - 8f, 22f), "Military rules",
                ref Integration.WorldObjectIntegrationSettings.militaryGovernance, on,
                "Apply adjacency and supply-line limits to modded military and expansion actions.");
            IntegrationToggle(new Rect(box.x + 10f + colW * 3f, y, colW - 8f, 22f), "Settlement tiers",
                ref Integration.WorldObjectIntegrationSettings.settlementTiers, on,
                "Classify settlements as village, town, city, or major city based on population.");

            // Row 3 — territorial ownership mode.
            y += 24f;
            var manager = Find.World?.GetComponent<SynapseRegionManager>();
            if (manager != null)
            {
                // A world is loaded, so edit that world's own flag rather than the default for new
                // ones. Changing it mid-playthrough is legitimate but not free: the rules start or
                // stop applying around settlements that were placed under the other regime.
                bool strict = manager.StrictTerritorialOwnership;
                bool before = strict;
                Rect worldRect = new Rect(box.x + 10f, y, box.width - 20f, 22f);
                Widgets.CheckboxLabeled(worldRect, "Strict territorial ownership (this world)", ref strict);
                TooltipHandler.TipRegion(worldRect,
                    "On: Regions & Territories decides where settlements and outposts may be built — buffers, supply range and footholds.\n\n" +
                    "Off (compatibility): placement is left entirely to vanilla and other mods, and more than one settlement may share a province. " +
                    "Regions are still generated and territory is still owned and drawn.\n\n" +
                    "Worlds that existed before Regions & Territories was installed start in compatibility mode, because their settlements were " +
                    "placed with no regard for these rules. Turning it on now will start refusing placements near towns that are already there.\n\n" +
                    "Compatibility mode exists so an existing save is usable, not so it is equivalent. For the full experience, start a new " +
                    "colony with the mod already installed.");
                if (strict != before) manager.StrictTerritorialOwnership = strict;
            }
            else
            {
                Rect defRect = new Rect(box.x + 10f, y, box.width - 20f, 22f);
                Widgets.CheckboxLabeled(defRect, "Strict territorial ownership (new worlds)",
                    ref FactionPlacementSettings.strictTerritorialOwnershipDefault);
                TooltipHandler.TipRegion(defRect,
                    "Whether newly generated worlds enforce Regions & Territories' placement rules. " +
                    "Worlds already in progress keep whatever mode they were adopted under; load a save to change that world's setting.");
            }

            // Row 4 — diagnostics.
            y += 24f;
            Rect logRect = new Rect(box.x + 10f, y, box.width - 20f, 22f);
            Widgets.CheckboxLabeled(logRect, "Log world object types no integration recognises",
                ref Integration.WorldObjectIntegrationSettings.logUnknownWorldObjects, !on);
            TooltipHandler.TipRegion(logRect,
                "Writes one message per unrecognised type. Useful when reporting a mod that Regions & Territories should support.");

            // Status line — what actually got detected in this game.
            y += 24f;
            Rect statusRect = new Rect(box.x + 10f, y, box.width - 20f, 22f);
            Widgets.Label(statusRect, "Detected: " + DescribeActiveIntegrations());
        }

        private static void IntegrationToggle(Rect rect, string label, ref bool value, bool enabled, string tooltip)
        {
            Widgets.CheckboxLabeled(rect, label, ref value, !enabled);
            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }
        }

        private static string DescribeActiveIntegrations()
        {
            var names = new List<string>();
            foreach (var adapter in Integration.WorldObjectAdapterRegistry.Adapters)
            {
                bool active;
                try { active = adapter.IsActive; }
                catch (Exception) { active = false; }

                if (active && adapter.AdapterId != "vanilla")
                {
                    names.Add(adapter.DisplayName);
                }
            }

            if (names.Count == 0)
            {
                return "<color=grey>vanilla world objects only</color>";
            }
            return "<color=green>" + string.Join(", ", names.ToArray()) + "</color>";
        }

        private void DrawWeightSlider(ref float y, float width, float startX, string label, ref float value, float min, float max)
        {
            Rect labelRect = new Rect(startX, y, width, 22f);
            Widgets.Label(labelRect, $"{label}: {value:F2}");
            y += 20f;

            Rect sliderRect = new Rect(startX, y, width, 18f);
            value = Widgets.HorizontalSlider(sliderRect, value, min, max, false, null, null, null, -1f);
            y += 25f;
        }
    }
}
