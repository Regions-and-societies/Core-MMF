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
        private Vector2 tableScroll = Vector2.zero;
        private List<FactionDef> activeFactions;

        // Per-cell edit buffers for the experimental table view, keyed "defName:column".
        private readonly Dictionary<string, string> tableBuffers = new Dictionary<string, string>();

        // 0.7 added the world-object integration panel (height); the #47 follow-up added a right-hand pie
        // strip showing each faction's share of the world (width).
        public override Vector2 InitialSize => new Vector2(1080f, 780f);

        /// <summary>Width of the right-hand strip that holds the mode toggles, the share pie and its legend.</summary>
        private const float PieStripW = 220f;

        // The "pressed" fill for the currently-selected size category in the basic view.
        private static readonly Color SelectedFill = new Color(0.24f, 0.42f, 0.30f);

        /// <summary>Compact button label for a size category (the prose form lives in PlacementShareRules).</summary>
        private static string CategoryButtonLabel(Placement.ShareCategory cat)
        {
            switch (cat)
            {
                case Placement.ShareCategory.Tiny: return "Tiny";
                case Placement.ShareCategory.Small: return "Small";
                case Placement.ShareCategory.Medium: return "Med";
                case Placement.ShareCategory.Large: return "Large";
                default: return "V.Large";
            }
        }

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
            // Title kept clear of the top-right Layout/View buttons (it used to run under them).
            Widgets.Label(new Rect(0f, 2f, inRect.width - 320f, 34f), "Geographic Placement Settings");
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
            // Find.WorldGrid dereferences Find.World internally, so it THROWS (not returns null) when no
            // world exists yet — which is exactly this dialog's common case, opened pre-generation from the
            // world-creation screen. Guard on Find.World first, then fall through to the pre-gen estimate.
            if (Find.World != null && Find.WorldGrid != null && Find.WorldGrid.TilesCount > 0)
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

            // #47: the region basis is the actual count once a world exists, else the mid estimate. The value
            // MODE + BASIS then decide how each faction's stored number becomes a region count. Build the
            // aligned weight list once and run the SAME pure distribution the pie chart and worldgen use, so
            // the estimate, the pie and the generated world all agree.
            int regionBasis = actualRegions > 0 ? actualRegions : Placement.PlacementEstimates.ExpectedRegionCount(landTiles, target);
            var valueMode = FactionPlacementSettings.placementValueMode;
            const Placement.PlacementPercentBasis percentBasis = Placement.PlacementPercentBasis.SettledNormalized;
            float claimedFraction = FactionPlacementSettings.claimedLandAreaPercent;
            var shareWeights = new List<float>(activeFactions.Count);
            foreach (var d in activeFactions) shareWeights.Add(FactionPlacementSettings.GetProfile(d).placementShare);
            int[] dist = Placement.PlacementShareRules.DistributeRegions(valueMode, percentBasis, shareWeights, regionBasis, claimedFraction);
            int demand = Placement.PlacementShareRules.DemandRegions(valueMode, percentBasis, shareWeights, regionBasis, claimedFraction);
            int placedTotal = 0; foreach (int v in dist) placedTotal += v;
            int wilderness = Placement.PlacementShareRules.WildernessRegions(regionBasis, dist);

            // Left content is narrowed to leave a fixed pie strip on the right.
            float contentW = inRect.width - PieStripW;

            // Global Map Region Parameters Panel
            Rect globalBoxRect = new Rect(0f, 40f, contentW - 15f, 160f);
            Widgets.DrawMenuSection(globalBoxRect);

            Rect globalTitleRect = new Rect(10f, 44f, 300f, 22f);
            Widgets.Label(globalTitleRect, "<b>Global Map Region Parameters</b>");

            // #47: basic/advanced view toggle — moved ABOVE the global box (the title row's right side) so it
            // no longer sits on top of the box's own note.
            bool advanced = FactionPlacementSettings.placementUiAdvanced;
            Rect toggleRect = new Rect(inRect.width - 165f, 4f, 155f, 30f);
            if (Widgets.ButtonText(toggleRect, advanced ? "View: Advanced" : "View: Basic"))
            {
                FactionPlacementSettings.placementUiAdvanced = !advanced;
                advanced = FactionPlacementSettings.placementUiAdvanced;
            }
            TooltipHandler.TipRegion(toggleRect,
                "Basic: one row per faction — just the size of the map each faction gets.\n\n" +
                "Advanced: the full per-faction controls (resource weights, clustering) plus mod-integration governance.");

            // Left Column: one Target size knob (the merge floor derives as half of it). The full explanation
            // is a tooltip rather than an inline note, which used to overflow onto the row below.
            float colWidth = (globalBoxRect.width - 30f) / 2f;
            Rect targetLabelRect = new Rect(10f, 68f, 135f, 22f);
            Widgets.Label(targetLabelRect, $"Target size: {FactionPlacementSettings.targetRegionSize}");
            Rect targetSliderRect = new Rect(150f, 70f, colWidth - 155f, 18f);
            float tempTarget = Widgets.HorizontalSlider(targetSliderRect, FactionPlacementSettings.targetRegionSize, 50f, 400f, false, null, null, null, 1f);
            FactionPlacementSettings.targetRegionSize = Mathf.RoundToInt(tempTarget);

            // Right Column: a short one-line note; the detail lives in the target-size tooltip.
            float rightColStart = 10f + colWidth + 10f;
            Rect noteRect = new Rect(rightColStart, 68f, globalBoxRect.width - rightColStart - 10f, 22f);
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(noteRect, "tiles/region — sparse biomes auto-scale up");
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            TooltipHandler.TipRegion(new Rect(10f, 68f, colWidth - 10f, 22f),
                "The size the subdivision aims for, in tiles per region. Sparse biomes (desert, tundra, ice) scale up automatically to fewer, larger regions; the merge floor (regions smaller than half the target are merged away) derives from this.");

            // Second Row: claimed land area (density). The Max Threat cap was dropped — hostility is now shown
            // per faction (the coloured dot on each row) so the player caps threats by sizing them directly.
            // The density knob drives the fill in percent mode; in count mode the counts are already absolute,
            // so it is dimmed and marked unused rather than left looking live.
            bool densityUsed = valueMode == Placement.PlacementValueMode.Percent;
            Rect occupLabelRect = new Rect(10f, 98f, 235f, 22f);
            GUI.color = densityUsed ? Color.white : new Color(0.6f, 0.6f, 0.6f);
            Widgets.Label(occupLabelRect, densityUsed
                ? $"Claimed land area: {Mathf.RoundToInt(FactionPlacementSettings.claimedLandAreaPercent * 100f)}%"
                : $"Claimed land area: {Mathf.RoundToInt(FactionPlacementSettings.claimedLandAreaPercent * 100f)}% (unused in count mode)");
            GUI.color = Color.white;
            TooltipHandler.TipRegion(occupLabelRect,
                "In percent mode: the share of livable land the factions collectively claim — the single fill knob. Raise it for a busier world, lower it for a frontier. Ignored in region-count mode, where each faction's number is a literal count.");
            Rect occupSliderRect = new Rect(250f, 100f, colWidth - 255f, 18f);
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
            Widgets.Label(estRect, landPart + "  |  " + countPart + $"  |  Claimed: <color=orange>{placedTotal}</color>  Wilderness: <color=grey>{wilderness}</color>");

            // Regional kin is now a per-faction toggle (each faction's card / the table's Kin column), so the
            // old blanket "split scattered factions" checkbox is gone.
            Rect kinHintRect = new Rect(10f, 162f, globalBoxRect.width - 20f, 22f);
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Widgets.Label(kinHintRect, "Regional kin (whether a faction's clusters become separate kin factions) is set per faction in Advanced.");
            GUI.color = Color.white;

            // The faction editors lay out inside the narrowed content area; the pie strip owns the rest.
            Rect contentRect = new Rect(0f, 0f, contentW, inRect.height);
            if (advanced)
            {
                // Layout toggle (cards ↔ table) sits on the title row, left of the view toggle.
                Rect layoutRect = new Rect(inRect.width - 165f - 130f, 4f, 125f, 30f);
                if (Widgets.ButtonText(layoutRect, FactionPlacementSettings.placementUiTable ? "Layout: Table" : "Layout: Cards"))
                    FactionPlacementSettings.placementUiTable = !FactionPlacementSettings.placementUiTable;
                TooltipHandler.TipRegion(layoutRect,
                    "Cards: one tall card per faction.\nTable: every faction and setting in one grid — denser, better for comparing factions while tuning.");

                // The World Object Integration panel now scrolls WITH the faction editor instead of taking a
                // fixed slab of the window, so the whole area below the global box is usable for tuning (#47).
                if (FactionPlacementSettings.placementUiTable)
                    DrawAdvancedTable(contentRect, 205f);
                else
                    DrawAdvancedCards(contentRect, 205f);
            }
            else
            {
                DrawBasicRows(contentRect, 205f, dist, demand, regionBasis);
            }

            // Right strip: value-mode + basis toggles, then the share pie and its legend.
            DrawPieStrip(new Rect(contentW, 40f, PieStripW - 10f, inRect.height - 40f - 50f), dist, wilderness, regionBasis);

            Rect closeButtonRect = new Rect(contentW / 2f - 75f, inRect.height - 45f, 150f, 35f);
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
        private void DrawBasicRows(Rect inRect, float top, int[] dist, int demand, int regionBasis)
        {
            bool isCount = FactionPlacementSettings.placementValueMode == Placement.PlacementValueMode.Count;
            string presetUnit = isCount ? " reg" : "";

            // Prose header — the meaning of the per-faction number depends on the value mode.
            Rect headerRect = new Rect(0f, top, inRect.width - 15f, 22f);
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            Widgets.Label(headerRect, isCount
                ? "Pick each faction's size — the exact number of regions it gets. Advanced = type any number."
                : "Pick each faction's share of the land — it self-scales to the planet, so the world always fills. Advanced = type any number.");
            GUI.color = Color.white;

            // Column layout, shared by the header and every row.
            const float colNameW = 150f, colBtnW = 62f;
            float colPickX0 = 8f + colNameW + 6f;
            // The raw computed-region count is hidden in basic (kept in Advanced and the pie); the kin column
            // takes its place, so the row reads name → size presets → kin without a lonely number between.
            float kinX = colPickX0 + 5 * colBtnW + 12f;
            var headerCats = new[] { Placement.ShareCategory.Tiny, Placement.ShareCategory.Small, Placement.ShareCategory.Medium, Placement.ShareCategory.Large, Placement.ShareCategory.VeryLarge };

            // Column header row, a clear band below the prose.
            TextAnchor hdrAnchor = Text.Anchor;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerCenter;
            GUI.color = new Color(0.6f, 0.85f, 0.6f);
            for (int i = 0; i < headerCats.Length; i++)
                Widgets.Label(new Rect(colPickX0 + i * colBtnW, top + 26f, colBtnW - 3f, 16f),
                    $"{Placement.PlacementShareRules.CategoryToRegionCap(headerCats[i])}{presetUnit}");
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Text.Anchor = TextAnchor.LowerLeft;
            Widgets.Label(new Rect(kinX, top + 26f, 200f, 16f), "kin?  → factions");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = hdrAnchor;

            // Capacity readout — green, yellow at 80% (crowding), red over the planet's regions (won't gen).
            // In the normalised-percent basis demand is a fraction of the planet by construction, so it never
            // goes over; the gate only bites in count mode or the planet-absolute basis.
            bool over = Placement.PlacementShareRules.IsOverCapacity(demand, regionBasis);
            bool warn = Placement.PlacementShareRules.IsCrowdingWarning(demand, regionBasis);
            Rect capRect = new Rect(0f, top + 46f, inRect.width - 15f, 22f);
            GUI.color = over ? new Color(1f, 0.4f, 0.4f) : (warn ? new Color(1f, 0.85f, 0.3f) : new Color(0.6f, 0.85f, 0.6f));
            Widgets.Label(capRect, over
                ? $"OVER CAPACITY: {demand} regions demanded vs ~{regionBasis} the planet supports — placement will be scaled down to fit. Reduce sizes or enlarge the planet."
                : warn
                    ? $"Crowding: {demand} of ~{regionBasis} regions demanded ({Mathf.RoundToInt(Placement.PlacementShareRules.CapacityFraction(demand, regionBasis) * 100f)}%) — little free space left, expect tight borders."
                    : $"Regions claimed: {demand} of ~{regionBasis} the planet supports.");
            GUI.color = Color.white;

            float rowH = 34f;
            Rect outRect = new Rect(0f, top + 70f, inRect.width, inRect.height - (top + 70f) - 55f);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 25f, activeFactions.Count * rowH);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            float curY = 0f;
            for (int fi = 0; fi < activeFactions.Count; fi++)
            {
                var def = activeFactions[fi];
                var profile = FactionPlacementSettings.GetProfile(def);

                Rect rowRect = new Rect(0f, curY, viewRect.width, rowH - 4f);
                Widgets.DrawMenuSection(rowRect);

                // Hostility indicator toward the player: green friendly / yellow neutral / red hostile.
                var host = HostilityOf(def);
                Rect dotRect = new Rect(rowRect.x + 8f, rowRect.y + 9f, 12f, 12f);
                Widgets.DrawBoxSolid(dotRect, HostilityColor(host));
                TooltipHandler.TipRegion(dotRect, HostilityTooltip(host, def));

                Rect nameRect = new Rect(rowRect.x + 26f, rowRect.y + 3f, colNameW - 18f, 24f);
                Widgets.Label(nameRect, $"<b>{def.LabelCap}</b>");
                TooltipHandler.TipRegion(nameRect, $"{def.LabelCap} ({def.defName})");

                // Size preset picker — the current size drawn "pressed"; advanced view types any integer.
                var current = Placement.PlacementShareRules.CategoryForShare(profile.placementShare);
                float pickX = nameRect.xMax + 6f;
                foreach (Placement.ShareCategory cat in System.Enum.GetValues(typeof(Placement.ShareCategory)))
                {
                    Rect btn = new Rect(pickX, rowRect.y + 3f, colBtnW - 3f, 24f);
                    bool clicked;
                    if (cat == current)
                    {
                        Widgets.DrawBoxSolid(btn, SelectedFill);
                        Widgets.DrawBox(btn);
                        TextAnchor prevAnchor = Text.Anchor;
                        Text.Anchor = TextAnchor.MiddleCenter;
                        Widgets.Label(btn, CategoryButtonLabel(cat));
                        Text.Anchor = prevAnchor;
                        clicked = Widgets.ButtonInvisible(btn);
                    }
                    else
                    {
                        clicked = Widgets.ButtonText(btn, CategoryButtonLabel(cat));
                    }
                    if (clicked) profile.placementShare = Placement.PlacementShareRules.CategoryToRegionCap(cat);
                    pickX += colBtnW;
                }

                // Computed regions this faction receives — used for the kin estimate; the raw number is shown
                // in Advanced and the pie, not on this row.
                int est = fi < dist.Length ? dist[fi] : 0;

                // Kin checkbox + the computed likely number of kin factions (regions ÷ cluster size).
                bool kinOn = FactionPlacementSettings.EffectiveEnableKin(profile, def);
                bool kinBefore = kinOn;
                Widgets.Checkbox(kinX, rowRect.y + 2f, ref kinOn, 22f);
                if (kinOn != kinBefore) profile.enableKinRaw = kinOn ? 1 : 0;
                int kinCount = kinOn ? Placement.SubFactionRules.EstimateKinCount(est, Placement.ClusteringRules.Snap(profile.clusterSize)) : 1;
                bool actuallySplits = kinOn && kinCount >= 2;
                GUI.color = actuallySplits ? Color.white : new Color(0.6f, 0.6f, 0.6f);
                Widgets.Label(new Rect(kinX + 28f, rowRect.y + 3f, 150f, 24f),
                    actuallySplits ? $"→ <color=cyan>{kinCount}</color> factions" : "one faction");
                GUI.color = Color.white;
                TooltipHandler.TipRegion(new Rect(kinX, rowRect.y, 180f, rowRect.height - 4f),
                    actuallySplits
                        ? $"Kin on: this faction's {est} regions split into ~{kinCount} kin factions (regions ÷ cluster size {Placement.ClusteringRules.Snap(profile.clusterSize)})."
                        : kinOn
                            ? $"Kin is on, but {est} region{(est == 1 ? "" : "s")} fit inside one cluster (size {Placement.ClusteringRules.Snap(profile.clusterSize)}), so it stays ONE faction. Raise its size or lower its cluster size (Advanced) to split it into kin factions."
                            : "Kin is off — this faction stays whole.");

                Rect resetRect = new Rect(rowRect.xMax - 96f, rowRect.y + 3f, 88f, 22f);
                if (Widgets.ButtonText(resetRect, "Reset"))
                    profile.placementShare = FactionPlacementSettings.DefaultShare(def);

                curY += rowH;
            }
            Widgets.EndScrollView();
        }

        /// <summary>
        /// #47 advanced view: the full per-faction card — resource weights, placement order, clustering —
        /// with the old Settlement Range replaced by the same share row basic mode shows.
        /// </summary>
        private void DrawAdvancedCards(Rect inRect, float top)
        {
            const float integrationH = 250f;
            Rect outRect = new Rect(0f, top, inRect.width, inRect.height - top - 55f);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 25f, integrationH + 6f + activeFactions.Count * 295f);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);

            // The World Object Integration panel scrolls as the first block, so it no longer eats a fixed
            // slab of the window.
            DrawIntegrationPanel(new Rect(0f, 0f, viewRect.width, integrationH));
            float curY = integrationH + 6f;

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

                // The per-faction placement number — meaning depends on the value mode (count = literal
                // regions; percent = a share). Same stored field either way; the basic presets fill it too.
                bool advIsCount = FactionPlacementSettings.placementValueMode == Placement.PlacementValueMode.Count;
                string advLabel = advIsCount ? "Regions (target count):" : "Share of land (weight):";
                float advMin = advIsCount ? 1f : 0f;
                float advMax = 300f;
                Rect shareRect = new Rect(10f, curY + 180f, boxRect.width - 20f, 24f);
                Rect shareLabelRect = new Rect(shareRect.x, shareRect.y, 240f, 24f);
                Widgets.Label(shareLabelRect, advLabel);
                TooltipHandler.TipRegion(shareLabelRect, advIsCount
                    ? "Literal number of regions this faction is given at world generation. The basic view's size presets (Tiny 3 … Very large 15) fill this in. The sum across all factions is capped at the planet's regions."
                    : "This faction's relative weight in the split of the land. Weights need not sum to anything — they normalise across whoever is present, then fill the claimed area. The basic presets fill this in.");
                float rcf = profile.placementShare;
                int rc = Mathf.RoundToInt(rcf);
                string rkey = def.defName + ":adv_reg";
                if (!tableBuffers.TryGetValue(rkey, out var rbuf)) rbuf = rc.ToString();
                Widgets.TextFieldNumeric(new Rect(shareRect.x + 250f, shareRect.y, 70f, 24f), ref rc, ref rbuf, advMin, advMax);
                tableBuffers[rkey] = rbuf;
                profile.placementShare = rc;

                // Placement Turn Order is hidden by request — the stored value still tie-breaks seeding order
                // at worldgen, but it is an implementation detail, not a knob most players want.

                // Per-faction kin toggle (replaces the blanket switch). Kin = this faction's scattered
                // territory is organised into regional groups; clustering (below) is a separate idea.
                Rect kinRect = new Rect(10f, curY + 215f, boxRect.width - 20f, 24f);
                bool kinOn = FactionPlacementSettings.EffectiveEnableKin(profile, def);
                bool kinBefore = kinOn;
                Widgets.CheckboxLabeled(kinRect, "Regional kin (split scattered territory into N/S/E/W groups)", ref kinOn, placeCheckboxNearText: true);
                if (kinOn != kinBefore) profile.enableKinRaw = kinOn ? 1 : 0;
                TooltipHandler.TipRegion(kinRect,
                    "When this faction's settlements scatter into separate clusters, organise them into regional kin groups " +
                    "(north/south, or west/east/central) instead of one undifferentiated blob. A separate idea from clustering. " +
                    "Default on for scattered low-tech factions (pirates, tribes, rough unions), off for the Empire and cohesive " +
                    "civilisations — but you can turn it on for any faction.");

                // #46 cluster size: any whole number 1..8, where 8 = 8+ (no limit) — how many territories may
                // cluster together (the largest contiguous body). A soft maximum: the faction prefers ground
                // where it cannot cluster and fills in against itself only when nothing else is left.
                Rect clusterRect = new Rect(10f, curY + 245f, boxRect.width - 20f, 24f);
                Rect clusterLabelRect = new Rect(clusterRect.x, clusterRect.y, 240f, 24f);
                Widgets.Label(clusterLabelRect, "Cluster size (largest body, 0 = no limit):");
                TooltipHandler.TipRegion(clusterLabelRect,
                    "The largest contiguous body of territory this faction builds — any whole number from 1 to 8, where 8 means one contiguous nation. " +
                    "A maximum, not a wall: the faction prefers ground where it cannot cluster and only fills in against itself when nothing else is left. " +
                    "A separate idea from kin. Defaults: pirates and the Empire 3, tribes 5, rough unions 7, everyone else 8.");
                int cl = Placement.ClusteringRules.Snap(profile.clusterSize);
                string ckey = def.defName + ":adv_cl";
                if (!tableBuffers.TryGetValue(ckey, out var cbuf)) cbuf = cl.ToString();
                Widgets.TextFieldNumeric(new Rect(clusterRect.x + 250f, clusterRect.y, 60f, 24f), ref cl, ref cbuf, 0f, 99f);
                tableBuffers[ckey] = cbuf;
                profile.clusterSize = Placement.ClusteringRules.Snap(cl);

                curY += 295f;
            }

            Widgets.EndScrollView();
        }

        // Column layout for the experimental table: x positions and widths, index-aligned with the headers.
        private static readonly float[] TblX = { 4f, 134f, 182f, 230f, 278f, 326f, 374f, 424f, 476f, 524f, 568f };
        private static readonly float[] TblW = { 126f, 44f, 44f, 44f, 44f, 44f, 44f, 48f, 44f, 40f, 56f };
        private static readonly string[] TblHead = { "Faction", "Min", "Nut", "For", "Grz", "Hun", "Mrg", "Regns", "Clstr", "Kin", "" };

        /// <summary>Experimental table layout for the advanced faction editor (#47): every faction a row,
        /// every tuning value a column, so a custom setup can be compared across factions at a glance.</summary>
        private void DrawAdvancedTable(Rect inRect, float top)
        {
            // Fixed column-header row above the scroll.
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            Text.Font = GameFont.Tiny;
            TextAnchor prev = Text.Anchor;
            Text.Anchor = TextAnchor.LowerCenter;
            for (int i = 1; i < TblHead.Length; i++)
                Widgets.Label(new Rect(TblX[i], top, TblW[i], 22f), TblHead[i]);
            Text.Anchor = TextAnchor.LowerLeft;
            Widgets.Label(new Rect(TblX[0], top, TblW[0], 22f), TblHead[0]);
            Text.Anchor = prev;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            const float integrationH = 250f, rowH = 28f;
            Rect outRect = new Rect(0f, top + 24f, inRect.width, inRect.height - (top + 24f) - 55f);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 25f, activeFactions.Count * rowH + 16f + integrationH);
            Widgets.BeginScrollView(outRect, ref tableScroll, viewRect);

            // Faction rows first, right under the fixed column header; the integration panel follows below.
            float curY = 0f;
            for (int fi = 0; fi < activeFactions.Count; fi++)
            {
                var def = activeFactions[fi];
                var profile = FactionPlacementSettings.GetProfile(def);
                Rect row = new Rect(0f, curY, viewRect.width, rowH - 2f);
                if (fi % 2 == 0) Widgets.DrawLightHighlight(row);

                Widgets.Label(new Rect(TblX[0] + 2f, curY + 4f, TblW[0], 22f), def.LabelCap);
                TooltipHandler.TipRegion(new Rect(TblX[0], curY, TblW[0], rowH), $"{def.LabelCap} ({def.defName})");

                float cy = curY + 3f, ch = rowH - 6f;
                NumCell(new Rect(TblX[1], cy, TblW[1], ch), def.defName + ":min", ref profile.mineralWeight, 0f, 5f);
                NumCell(new Rect(TblX[2], cy, TblW[2], ch), def.defName + ":nut", ref profile.nutritionWeight, 0f, 5f);
                NumCell(new Rect(TblX[3], cy, TblW[3], ch), def.defName + ":for", ref profile.forageWeight, 0f, 5f);
                NumCell(new Rect(TblX[4], cy, TblW[4], ch), def.defName + ":grz", ref profile.grazingWeight, 0f, 5f);
                NumCell(new Rect(TblX[5], cy, TblW[5], ch), def.defName + ":hun", ref profile.huntingWeight, 0f, 5f);
                NumCell(new Rect(TblX[6], cy, TblW[6], ch), def.defName + ":mrg", ref profile.marginWeight, 0f, 5f);
                NumCell(new Rect(TblX[7], cy, TblW[7], ch), def.defName + ":shr", ref profile.placementShare, 1f, 300f);

                int clv = Placement.ClusteringRules.Snap(profile.clusterSize);
                string ckey = def.defName + ":cl";
                if (!tableBuffers.TryGetValue(ckey, out var cbuf)) cbuf = clv.ToString();
                Widgets.TextFieldNumeric(new Rect(TblX[8], cy, TblW[8], ch), ref clv, ref cbuf, 0f, 99f);
                tableBuffers[ckey] = cbuf;
                profile.clusterSize = Placement.ClusteringRules.Snap(clv);

                bool kinOn = FactionPlacementSettings.EffectiveEnableKin(profile, def);
                bool kb = kinOn;
                Widgets.Checkbox(TblX[9] + 10f, curY + 3f, ref kinOn, 20f);
                if (kinOn != kb) profile.enableKinRaw = kinOn ? 1 : 0;

                if (Widgets.ButtonText(new Rect(TblX[10], curY + 2f, TblW[10], rowH - 6f), "Reset"))
                {
                    var dp = FactionPlacementSettings.GetDefaultProfile(def);
                    profile.mineralWeight = dp.mineralWeight; profile.nutritionWeight = dp.nutritionWeight;
                    profile.forageWeight = dp.forageWeight; profile.grazingWeight = dp.grazingWeight;
                    profile.huntingWeight = dp.huntingWeight; profile.marginWeight = dp.marginWeight;
                    profile.placementShare = dp.placementShare; profile.clusterSize = dp.clusterSize;
                    profile.enableKinRaw = -1;
                    foreach (var k in new[] { "min", "nut", "for", "grz", "hun", "mrg", "shr", "cl" }) tableBuffers.Remove(def.defName + ":" + k);
                }

                curY += rowH;
            }

            // World Object Integration below the faction table, scrolling with it.
            DrawIntegrationPanel(new Rect(0f, curY + 16f, viewRect.width, integrationH));

            Widgets.EndScrollView();
        }

        private void NumCell(Rect r, string key, ref float v, float min, float max)
        {
            if (!tableBuffers.TryGetValue(key, out var buf)) buf = v.ToString("0.##");
            Widgets.TextFieldNumeric(r, ref v, ref buf, min, max);
            tableBuffers[key] = buf;
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
            Widgets.Label(titleRect, "<b>World Objects to Add During World Generation:</b>");

            float y = box.y + 30f;

            // Strict ownership shown here as a read-only status; the control itself lives in the mod's
            // settings (Options → Mod Settings → Regions and Societies), where it can be changed at any
            // time, including mid-game. Reads the loaded world's flag when there is one, else the new-world
            // default.
            var manager = Find.World?.GetComponent<SynapseRegionManager>();
            bool strictActive = manager != null ? manager.StrictTerritorialOwnership
                                                : FactionPlacementSettings.strictTerritorialOwnershipDefault;
            string strictWord = strictActive ? "<color=#7CFC7C>enabled</color>" : "<color=#FF7C7C>disabled</color>";
            Rect ownRect = new Rect(box.x + 10f, y, box.width - 20f, 22f);
            Widgets.Label(ownRect, $"Strict territorial ownership is {strictWord} — change it in mod settings. (default enabled)");
            TooltipHandler.TipRegion(ownRect,
                "Whether Regions & Societies governs where settlements and outposts may be built (buffers, supply range, footholds, region locks). " +
                "Set it under Options → Mod Settings → Regions and Societies; it can be changed mid-game.");
            y += 26f;

            // World maturity: how many NON-PLAYER outposts and other holdings are pre-placed at world
            // generation. Off = none; Full = each territory's whole allowance (a ready-to-play, fully
            // settled world). Seeding is implied by maturity > 0 — no separate on/off switch.
            bool on = Integration.WorldObjectIntegrationSettings.masterEnabled;
            Rect matLabelRect = new Rect(box.x + 10f, y, 235f, 22f);
            GUI.color = on ? Color.white : new Color(1f, 1f, 1f, 0.4f);
            Widgets.Label(matLabelRect, $"NPC outposts and world objects: {Sizing.SeedingMaturityRules.Label(Integration.WorldObjectIntegrationSettings.seedingMaturity)}");
            GUI.color = Color.white;
            Rect matSliderRect = new Rect(box.x + 255f, y + 2f, box.width - 275f, 18f);
            float tempMat = Widgets.HorizontalSlider(matSliderRect, Integration.WorldObjectIntegrationSettings.seedingMaturity, 0f, 1f, false, null, "Off", "Full", 0.05f);
            if (on) Integration.WorldObjectIntegrationSettings.seedingMaturity = tempMat;
            TooltipHandler.TipRegion(matLabelRect,
                "How built-up a new world starts — how many non-player (NPC) outposts and other holdings are pre-placed around settlements at world generation. " +
                "Off pre-places none; Full pre-places each territory's whole allowance — a ready-to-play, fully-settled world (e.g. a World Domination start). " +
                "Requires a compatibility mod that builds the outposts. Stamped per world so a regenerate reproduces it.");
            y += 30f;

            // Detected compatibility mods and the world objects each contributes. FRAMEWORK PREVIEW: no
            // compatibility patch populates these columns yet, so with none installed we show a dimmed
            // example of the shape a patch would fill. See Design/world-object-integration.md.
            DrawDetectedModsTable(new Rect(box.x + 10f, y, box.width - 20f, box.yMax - y - 6f));
        }

        /// <summary>The detected-mods framework table (design: Design/world-object-integration.md). Lists the
        /// active world-object integrations and, per mod, which world objects it contributes. The capability
        /// columns are a preview — no compatibility patch reports them yet — so with nothing detected we draw
        /// a dimmed worked example rather than an empty grid.</summary>
        private void DrawDetectedModsTable(Rect area)
        {
            var active = new List<string>();
            foreach (var adapter in Integration.WorldObjectAdapterRegistry.Adapters)
            {
                bool ok;
                try { ok = adapter.IsActive; }
                catch (Exception) { ok = false; }
                if (ok && adapter.AdapterId != "vanilla") active.Add(adapter.DisplayName);
            }

            Text.Font = GameFont.Tiny;
            float y = area.y;
            Widgets.Label(new Rect(area.x, y, area.width, 18f), "<b>Detected compatibility mods</b>");
            y += 18f;

            if (active.Count == 0)
            {
                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                Widgets.Label(new Rect(area.x, y, area.width, 18f),
                    "Nothing to post here without a compatibility patch. With one installed it would look like:");
                GUI.color = Color.white;
                y += 18f;
            }

            // Columns: Mod | Economy | Military bases | Roads.
            float[] cx = { area.x, area.x + 190f, area.x + 300f, area.x + 430f };
            string[] head = { "Mod", "Economy", "Military bases", "Roads" };
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            for (int i = 0; i < head.Length; i++) Widgets.Label(new Rect(cx[i], y, 130f, 18f), head[i]);
            GUI.color = Color.white;
            y += 16f;
            Widgets.DrawLineHorizontal(area.x, y, area.width);
            y += 2f;

            if (active.Count == 0)
            {
                // Dimmed worked example — none of this is wired; it illustrates the table's shape only.
                GUI.color = new Color(0.55f, 0.55f, 0.55f);
                DrawExampleRow(cx, y, "VOE-CP", true, false, true);   y += 16f;
                DrawExampleRow(cx, y, "Empire-CP", true, true, true); y += 16f;
                DrawExampleRow(cx, y, "VFE-CP", false, true, false);  y += 16f;
                GUI.color = new Color(0.5f, 0.5f, 0.5f);
                Widgets.Label(new Rect(area.x, y, area.width, 18f), "<i>Framework example — not yet implemented.</i>");
                GUI.color = Color.white;
            }
            else
            {
                // A real integration is present but no patch reports capability columns yet, so we can only
                // confirm the mod, not which world objects it contributes.
                foreach (var name in active)
                {
                    Widgets.Label(new Rect(cx[0], y, 180f, 18f), "<color=#7CFC7C>" + name + "</color>");
                    for (int i = 1; i < cx.Length; i++) Widgets.Label(new Rect(cx[i], y, 120f, 18f), "—");
                    y += 16f;
                }
            }

            Text.Font = GameFont.Small;
        }

        private static void DrawExampleRow(float[] cx, float y, string mod, bool eco, bool mil, bool road)
        {
            Widgets.Label(new Rect(cx[0], y, 180f, 18f), mod);
            Widgets.Label(new Rect(cx[1], y, 120f, 18f), eco ? "Yes" : "No");
            Widgets.Label(new Rect(cx[2], y, 120f, 18f), mil ? "Yes" : "No");
            Widgets.Label(new Rect(cx[3], y, 120f, 18f), road ? "Yes" : "No");
        }

        // Pie view: 0 = by faction, 1 = by hostility. Session-sticky (static), not persisted.
        private static int pieView = 0;

        private enum Hostility { Hostile, Neutral, Friendly }

        /// <summary>A faction's likely starting stance toward the player, read from its def before any world
        /// exists (1.6 has no starting-goodwill range, so this reads the stance flags): permanent/natural
        /// enemies are hostile; of the rest, those that raid unaligned humanlikes read as neutral (prickly
        /// but dealable), and the genuinely civil ones as friendly.</summary>
        private static Hostility HostilityOf(FactionDef def)
        {
            if (def == null) return Hostility.Neutral;
            if (def.permanentEnemy || def.naturalEnemy) return Hostility.Hostile;
            return def.hostileToFactionlessHumanlikes ? Hostility.Neutral : Hostility.Friendly;
        }

        private static Color HostilityColor(Hostility h)
        {
            switch (h)
            {
                case Hostility.Hostile: return new Color(0.85f, 0.35f, 0.35f);
                case Hostility.Friendly: return new Color(0.45f, 0.80f, 0.45f);
                default: return new Color(0.85f, 0.78f, 0.38f);
            }
        }

        private static string HostilityTooltip(Hostility h, FactionDef def)
        {
            string word = h == Hostility.Hostile ? "Hostile" : h == Hostility.Friendly ? "Friendly" : "Neutral";
            string basis = def == null ? ""
                : def.permanentEnemy ? " (permanent enemy)"
                : def.naturalEnemy ? " (natural enemy)"
                : def.hostileToFactionlessHumanlikes ? " (raids the unaligned — prickly, but can be dealt with)"
                : " (starts on good terms)";
            return $"{word} toward the player at world generation{basis}. Starting stance only — it can change in play.";
        }

        // A distinct, stable colour per faction slice — golden-ratio hue spacing keeps adjacent slices apart.
        private static Color SliceColor(int index)
        {
            float hue = (index * 0.61803399f) % 1f;
            return Color.HSVToRGB(hue, 0.55f, 0.92f);
        }

        private static readonly Color WildernessColor = new Color(0.42f, 0.42f, 0.42f);

        /// <summary>The right-hand strip: the value-mode + pie-view toggles up top (co-located with the pie
        /// they drive), then the share pie chart and a legend. The pie reads the SAME distribution as the
        /// faction rows and worldgen, so it is an honest preview of how the world's regions will divide —
        /// either one slice per faction, or grouped into hostile / neutral / friendly.</summary>
        private void DrawPieStrip(Rect strip, int[] dist, int wilderness, int regionBasis)
        {
            float y = strip.y;

            // Value-mode toggle (Percent ↔ Region count).
            bool isCount = FactionPlacementSettings.placementValueMode == Placement.PlacementValueMode.Count;
            Rect valRect = new Rect(strip.x, y, strip.width, 28f);
            if (Widgets.ButtonText(valRect, isCount ? "Values: Region count" : "Values: Percent"))
                FactionPlacementSettings.placementValueMode = isCount ? Placement.PlacementValueMode.Percent : Placement.PlacementValueMode.Count;
            TooltipHandler.TipRegion(valRect,
                "Percent (default): each faction's number is a share of the total land regions that self-scales — a small vanilla world still fills.\n\n" +
                "Region count: each number is a literal target region count (exact control).");
            y += 32f;

            // Pie-view toggle (By faction ↔ By hostility).
            Rect pieViewRect = new Rect(strip.x, y, strip.width, 28f);
            if (Widgets.ButtonText(pieViewRect, pieView == 0 ? "Pie: By faction" : "Pie: By hostility"))
                pieView = pieView == 0 ? 1 : 0;
            TooltipHandler.TipRegion(pieViewRect,
                "By faction: one slice per faction.\n\nBy hostility: slices grouped into hostile / neutral / friendly, plus wilderness — the shape of the threat you'll face.");
            y += 34f;

            // Build the slices for the chosen view (colour, label, region count).
            var slices = new List<(Color color, string label, int count)>();
            if (pieView == 0)
            {
                for (int i = 0; i < activeFactions.Count; i++)
                    if (i < dist.Length && dist[i] > 0)
                        slices.Add((SliceColor(i), activeFactions[i].LabelCap, dist[i]));
            }
            else
            {
                int hostile = 0, neutral = 0, friendly = 0;
                for (int i = 0; i < activeFactions.Count; i++)
                {
                    int c = i < dist.Length ? dist[i] : 0;
                    if (c <= 0) continue;
                    switch (HostilityOf(activeFactions[i]))
                    {
                        case Hostility.Hostile: hostile += c; break;
                        case Hostility.Friendly: friendly += c; break;
                        default: neutral += c; break;
                    }
                }
                if (hostile > 0) slices.Add((HostilityColor(Hostility.Hostile), "Hostile", hostile));
                if (neutral > 0) slices.Add((HostilityColor(Hostility.Neutral), "Neutral", neutral));
                if (friendly > 0) slices.Add((HostilityColor(Hostility.Friendly), "Friendly", friendly));
            }
            if (wilderness > 0) slices.Add((WildernessColor, "Wilderness", wilderness));

            // Pie.
            float radius = Mathf.Min(82f, (strip.width - 20f) / 2f);
            Vector2 center = new Vector2(strip.x + strip.width / 2f, y + radius + 4f);
            if (regionBasis > 0 && slices.Count > 0)
            {
                float ang = -90f;
                foreach (var s in slices)
                {
                    float sweep = 360f * s.count / regionBasis;
                    DrawWedge(center, radius, ang, ang + sweep, s.color);
                    ang += sweep;
                }
            }
            else
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                Widgets.Label(new Rect(strip.x, y, strip.width, radius * 2f), "no regions yet");
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
            }
            y = center.y + radius + 10f;

            // Legend.
            Text.Font = GameFont.Tiny;
            float rowH = 18f, maxY = strip.yMax - 4f;
            int hidden = 0;
            foreach (var s in slices)
            {
                if (y + rowH > maxY) { hidden++; continue; }
                DrawLegendRow(strip.x, ref y, strip.width, rowH, s.color, s.label, s.count, regionBasis);
            }
            if (hidden > 0 && y + rowH <= maxY)
            {
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                Widgets.Label(new Rect(strip.x + 16f, y, strip.width - 16f, rowH), $"+{hidden} more…");
                GUI.color = Color.white;
            }
            Text.Font = GameFont.Small;
        }

        // Fill a pie wedge with fine radial lines — no texture or mesh needed, robust in IMGUI.
        private static void DrawWedge(Vector2 center, float radius, float startDeg, float endDeg, Color color)
        {
            for (float a = startDeg; a < endDeg; a += 0.6f)
            {
                float rad = a * Mathf.Deg2Rad;
                Vector2 edge = new Vector2(center.x + Mathf.Cos(rad) * radius, center.y + Mathf.Sin(rad) * radius);
                Widgets.DrawLine(center, edge, color, 2.4f);
            }
        }

        private static void DrawLegendRow(float x, ref float y, float width, float rowH, Color color, string label, int count, int total)
        {
            Widgets.DrawBoxSolid(new Rect(x, y + 3f, 11f, 11f), color);
            int pct = total > 0 ? Mathf.RoundToInt(100f * count / total) : 0;
            string txt = label != null && label.Length > 16 ? label.Substring(0, 15) + "…" : label;
            Widgets.Label(new Rect(x + 16f, y, width - 16f, rowH), $"{txt}  <color=grey>{count} ({pct}%)</color>");
            y += rowH;
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
