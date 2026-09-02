using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace RegionsAndSocieties
{
    public class SynapseRegionManager : WorldComponent
    {
        private List<GeographicProvince> provinces = new List<GeographicProvince>();
        private int[] tileToProvinceId;
        private Dictionary<int, int> settlementPlacementOrder = new Dictionary<int, int>();

        // Modeled population per NPC settlement (keyed by tile id), grown over time by the birthrate
        // model (#6). Scribed so a settlement's size persists across saves. The player colony is never
        // in here — its size is the real free-colonist count.
        private Dictionary<int, float> settlementModeledPop = new Dictionary<int, float>();

        // One in-game day between growth ticks — growth is measured in years, so a daily step is smooth
        // and keeps the per-settlement sweep off the hot path.
        private const int GrowthTickInterval = 60000;

        public int GetSettlementPlacementOrder(int tileId)
        {
            if (settlementPlacementOrder != null && settlementPlacementOrder.TryGetValue(tileId, out int order))
            {
                return order;
            }
            return -1;
        }

        public void SetSettlementPlacementOrder(int tileId, int order)
        {
            if (settlementPlacementOrder == null)
            {
                settlementPlacementOrder = new Dictionary<int, int>();
            }
            settlementPlacementOrder[tileId] = order;
        }

        public int GetNextPlacementOrderForFaction(Faction faction)
        {
            int count = 0;
            foreach (var obj in Find.WorldObjects.AllWorldObjects)
            {
                // 0.7: classification is mod-agnostic — see Integration.WorldObjectClassifier.
                if (Integration.WorldObjectClassifier.IsSettlement(obj) && obj.Faction == faction)
                {
                    count++;
                }
            }
            return count + 1;
        }

        public List<GeographicProvince> Provinces
        {
            get
            {
                if (provinces == null || provinces.Count == 0)
                {
                    GenerateProvinces();
                }
                return provinces;
            }
        }

        public SynapseRegionManager(World world) : base(world)
        {
            InitializeData();
        }

        private void InitializeData()
        {
            if (tileToProvinceId == null && Find.WorldGrid != null)
            {
                tileToProvinceId = new int[Find.WorldGrid.TilesCount];
                for (int i = 0; i < tileToProvinceId.Length; i++)
                {
                    tileToProvinceId[i] = -1;
                }
            }
        }

        public int GetProvinceId(int tileId)
        {
            InitializeData();
            if (tileId < 0 || tileId >= tileToProvinceId.Length) return -1;
            return tileToProvinceId[tileId];
        }

        public GeographicProvince GetProvince(int provinceId)
        {
            return provinces.FirstOrDefault(p => p.id == provinceId);
        }

        public GeographicProvince GetProvinceForTile(int tileId)
        {
            int pid = GetProvinceId(tileId);
            if (pid == -1) return null;
            return GetProvince(pid);
        }

        // -1 unresolved, 0 compatibility (non-strict), 1 strict. An int rather than a bool because
        // "absent from this save" has to be distinguishable from "saved as false" — that
        // distinction is the whole mechanism for adopting a save R&T was not present for.
        private int strictTerritorialOwnershipRaw = -1;

        // Which population-density algorithm this world uses. Population is derived, not scribed, so
        // it recomputes on every load — which means a 0.7.1 world loaded under 0.7.2 would silently
        // switch to the new numbers. Stamping the world lets an existing save keep the density it was
        // built with. -1 unresolved; 1 legacy (0.7.1 and earlier: uncapped pockets, smeared totals
        // incl. the #55 overcount); 2 current (0.7.2+: capped/landmark-biased pockets, source totals).
        public const int DensityAlgorithmLegacy = 1;
        public const int DensityAlgorithmCurrent = 2;
        private int densityAlgorithmVersionRaw = -1;

        // Which region-partition algorithm built this world's provinces. Provinces ARE scribed, so an old
        // save keeps its shapes on load without re-partitioning; this stamp exists so a REGEN (the debug
        // action, or any future forced rebuild) reproduces the world with the algorithm it was born under,
        // and so new worlds get the new method by default. -1 unresolved; 1 legacy (anchor-Voronoi
        // PartitionLand, 0.2.x–early 0.3.0); 2 current (contain-then-subdivide PartitionByBasins).
        public const int PartitionAlgorithmLegacy = 1;
        public const int PartitionAlgorithmCurrent = 2;
        private int partitionAlgorithmVersionRaw = -1;

        /// <summary>
        /// The partition algorithm in force for this world. Only a save explicitly resolved to legacy (a
        /// world whose provinces predate the stamp) reports legacy; an unstamped live new world defaults
        /// to current, so new games get the contain-then-subdivide partition.
        /// </summary>
        public int PartitionAlgorithmVersion
        {
            get { return partitionAlgorithmVersionRaw == PartitionAlgorithmLegacy ? PartitionAlgorithmLegacy : PartitionAlgorithmCurrent; }
        }

        /// <summary>
        /// The density algorithm in force for this world. Only a save explicitly resolved to legacy
        /// (a pre-0.7.2 world) reports legacy; an unstamped live new world defaults to current.
        /// </summary>
        public int DensityAlgorithmVersion
        {
            get { return densityAlgorithmVersionRaw == DensityAlgorithmLegacy ? DensityAlgorithmLegacy : DensityAlgorithmCurrent; }
        }

        /// <summary>
        /// Whether this world enforces R&amp;T's placement rules for settlements and outposts.
        ///
        /// <para><b>Strict</b> (worlds generated with R&amp;T): buffers, supply, footholds and the
        /// one-holding-per-province assumptions all apply, as they have since 0.7.</para>
        ///
        /// <para><b>Compatibility</b> (R&amp;T added to a world already in progress): placement is
        /// left entirely to vanilla and to whatever other mods are doing it. Provinces are still
        /// generated and territory is still owned and drawn — only the rules that would refuse a
        /// placement stand down, because a world that was built without them is already full of
        /// settlements those rules would have forbidden.</para>
        /// </summary>
        public bool StrictTerritorialOwnership
        {
            get { return strictTerritorialOwnershipRaw != 0; }
            set { strictTerritorialOwnershipRaw = value ? 1 : 0; }
        }

        /// <summary>True once the mode has been decided for this world, either on load or at worldgen.</summary>
        public bool StrictOwnershipResolved
        {
            get { return strictTerritorialOwnershipRaw != -1; }
        }

        /// <summary>
        /// Decide the mode for a save that predates the flag.
        ///
        /// <para>The discriminator is <b>provinces, not the flag</b>. A save made with R&amp;T 0.7
        /// also has no flag yet, but it does have generated provinces — that world was built under
        /// the placement rules and must keep them. A save with neither is one R&amp;T has just been
        /// added to, and its existing settlements were placed with no regard for our rules, so
        /// enforcing them now would refuse placements next to towns that already exist.</para>
        /// </summary>
        /// <summary>
        /// Test seam: the province list without the lazy generation the <see cref="Provinces"/>
        /// getter performs. A case that needs to simulate "this save had no provinces" cannot use
        /// the getter, because reading it is what builds them.
        /// <para>Public rather than internal because the TestRunner is a separate assembly.</para>
        /// </summary>
        public List<GeographicProvince> ProvincesRaw
        {
            get { return provinces; }
        }

        /// <summary>
        /// Test seam: put the flag back to unresolved so the load-time decision can be exercised.
        /// Not part of normal operation — a live world has already decided.
        /// </summary>
        public void ResetStrictOwnershipForTesting()
        {
            strictTerritorialOwnershipRaw = -1;

            // A test that exercises the compat branch would otherwise arm the player notice and
            // drop a letter into the live test colony on the next tick. Restore what we touch.
            pendingCompatibilityNotice = false;
        }

        /// <summary>Test seam: run the load-time decision directly, without a save round trip.</summary>
        public void ResolveStrictOwnershipForTesting()
        {
            ResolveStrictOwnershipForLoadedSave();

            // The compat branch arms a player-facing letter. Tests run inside a live colony, so
            // leaving it armed would drop that letter on the next tick. The decision is what these
            // cases exercise; the notice is deliberately not.
            pendingCompatibilityNotice = false;
        }

        private void ResolveStrictOwnershipForLoadedSave()
        {
            if (strictTerritorialOwnershipRaw != -1) return;

            bool hadProvinces = provinces != null && provinces.Count > 0;
            strictTerritorialOwnershipRaw = hadProvinces ? 1 : 0;

            Log.Message(hadProvinces
                ? "[RegionsAndSocieties] Save predates the territorial-ownership flag but has generated provinces: treating as strict."
                : "[RegionsAndSocieties] Save has no province data: adopting it in compatibility mode. Regions will be generated; placement rules stand down.");

            // Tell the player, not just the log. Somebody who installs mid-playthrough gets a
            // reduced mode and would otherwise have no way to know: the map modes look right, so
            // nothing on screen says placement governance is off. Deferred rather than shown here
            // because PostLoadInit runs before the UI is ready to take a letter.
            if (!hadProvinces) pendingCompatibilityNotice = true;
        }

        /// <summary>
        /// Decide the density algorithm for a save that predates the stamp. Same discriminator as the
        /// ownership mode: <b>provinces, not the flag</b>. A pre-0.7.2 world already has generated
        /// provinces and a population the player has been living with, so it keeps the legacy
        /// algorithm. A save with no provinces is one R&amp;T is generating regions for now, for the
        /// first time, so it gets the current algorithm — there is no prior population to preserve.
        /// </summary>
        private void ResolveDensityAlgorithmForLoadedSave()
        {
            if (densityAlgorithmVersionRaw != -1) return;

            bool hadProvinces = provinces != null && provinces.Count > 0;
            densityAlgorithmVersionRaw = hadProvinces ? DensityAlgorithmLegacy : DensityAlgorithmCurrent;

            Log.Message(hadProvinces
                ? "[RegionsAndSocieties] Save predates the density-algorithm stamp but has provinces: keeping the legacy (pre-0.7.2) population algorithm so this world's numbers do not shift."
                : "[RegionsAndSocieties] Save has no province data: regions will be generated with the current population algorithm.");
        }

        /// <summary>Test seam: force the density algorithm back to unresolved so the load-time decision can be exercised.</summary>
        public void ResetDensityAlgorithmForTesting()
        {
            densityAlgorithmVersionRaw = -1;
        }

        /// <summary>Test seam: run the density load-time decision directly, without a save round trip.</summary>
        public void ResolveDensityAlgorithmForTesting()
        {
            ResolveDensityAlgorithmForLoadedSave();
        }

        /// <summary>
        /// Decide the partition algorithm for a save that predates the stamp. Same discriminator as the
        /// density stamp — <b>provinces, not the flag</b>: a world that already has generated provinces
        /// was built by the legacy partition and keeps it (its shapes are scribed and must not shift if
        /// regenerated), while a save with no provinces is one R&amp;T is partitioning now for the first
        /// time and gets the current contain-then-subdivide algorithm.
        /// </summary>
        private void ResolvePartitionAlgorithmForLoadedSave()
        {
            if (partitionAlgorithmVersionRaw != -1) return;

            bool hadProvinces = provinces != null && provinces.Count > 0;
            partitionAlgorithmVersionRaw = hadProvinces ? PartitionAlgorithmLegacy : PartitionAlgorithmCurrent;

            Log.Message(hadProvinces
                ? "[RegionsAndSocieties] Save predates the partition-algorithm stamp but has provinces: keeping the legacy (anchor-Voronoi) region shapes so a regenerate would not repartition this world."
                : "[RegionsAndSocieties] Save has no province data: regions will be built with the current contain-then-subdivide partition.");
        }

        /// <summary>Test seam: force the partition algorithm back to unresolved.</summary>
        public void ResetPartitionAlgorithmForTesting()
        {
            partitionAlgorithmVersionRaw = -1;
        }

        /// <summary>Test seam: run the partition load-time decision directly, without a save round trip.</summary>
        public void ResolvePartitionAlgorithmForTesting()
        {
            ResolvePartitionAlgorithmForLoadedSave();
        }

        /// <summary>Set when a save is adopted into compatibility mode; cleared once the player has been told.</summary>
        private bool pendingCompatibilityNotice;

        // WorldComponent has no FinalizeInit, so the notice rides the first tick instead. Ticks only
        // run once the game is actually playing, which is exactly when the letter stack is ready.
        // Coarse cadence for decaying demographic skews (#11): ~2500 ticks (an in-game hour) is ample
        // granularity for a decay measured in years, and keeps the override sweep off the hot path.
        private const int DemographicDecayInterval = 2500;

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();

            if (Find.TickManager != null && Find.TickManager.TicksGame % DemographicDecayInterval == 0)
            {
                Demographics.RegionDemographicsStress.Tick(DemographicDecayInterval);
            }

            if (Find.TickManager != null && Find.TickManager.TicksGame % GrowthTickInterval == 0)
            {
                AdvanceSettlementGrowth(GrowthTickInterval);
            }

            if (!pendingCompatibilityNotice) return;
            pendingCompatibilityNotice = false;

            Find.LetterStack?.ReceiveLetter(
                "Regions and Territories: compatibility mode",
                "This world was created before Regions and Territories was installed, so it has been adopted in " +
                "compatibility mode.\n\n" +
                "Provinces have been generated and territory ownership is drawn on the world map as normal. What is " +
                "switched off is placement: the mod will not decide where settlements and outposts may be built. Your " +
                "world is already full of settlements that were placed with no regard for those rules, and applying " +
                "them now would refuse ground that has been settled since long before the mod arrived. Vanilla and " +
                "your other mods keep control of placement, and more than one settlement may share a province.\n\n" +
                "For the full experience — including faction placement governed by region occupancy, border buffers " +
                "and sequential expansion — start a new colony with the mod already installed. That is what the mod " +
                "is designed around; compatibility mode exists so an existing save is usable, not equivalent.\n\n" +
                "You can review this under 'Strict territorial ownership' in the mod settings.",
                LetterDefOf.NeutralEvent);
        }

        /// <summary>
        /// The modeled population of an NPC settlement (#6), seeding a fresh one at a third of its
        /// target on first read and clamping to its current cap. The player colony is never modeled —
        /// callers read its real free-colonist count instead.
        /// </summary>
        public int GetModeledSettlementPopulation(WorldObject settlement)
        {
            if (settlement == null) return 0;
            if (settlementModeledPop == null) settlementModeledPop = new Dictionary<int, float>();

            int tile = settlement.Tile;
            if (!settlementModeledPop.TryGetValue(tile, out float pop))
            {
                pop = Sizing.SettlementGrowthUtility.SeedPopulation(settlement);
                settlementModeledPop[tile] = pop;
            }

            // Growth capacity is the ⅔-max TARGET, not the tier max. Full births run up to the target;
            // above it births taper, stagnating at 150% of the target — which, since target = ⅔ max, is
            // exactly the tier max. So a healthy settlement crowds toward but never past its tier max.
            int capacity = Sizing.SettlementSizeUtility.TargetPopulationOf(settlement);
            return ClampToCeiling((int)Math.Round(pop, MidpointRounding.AwayFromZero), capacity);
        }

        /// <summary>
        /// Advance every NPC settlement's modeled population one growth step (#6): net rate from the
        /// birthrate factor model, applied as a logistic drift toward the settlement's ⅔-max target over
        /// the elapsed years. Prunes settlements that no longer exist and marks the population cache
        /// dirty so overlays reflect the new sizes. The player colony is skipped — real pawns only.
        /// </summary>
        // Population may crowd above the ⅔-max target up to the birth-stagnation ceiling (150% of the
        // target = the tier max); clamp at the ceiling, so a well-fed settlement can grow past its
        // comfortable size but never past its tier max (#6).
        private static int ClampToCeiling(int v, int capacity)
        {
            if (v < 0) v = 0;
            int ceil = (int)Math.Round(capacity * Sizing.BirthrateRules.BirthStagnationRatio, MidpointRounding.AwayFromZero);
            if (capacity > 0 && v > ceil) v = ceil;
            return v;
        }

        private void AdvanceSettlementGrowth(int intervalTicks)
        {
            if (Find.WorldObjects == null) return;
            if (settlementModeledPop == null) settlementModeledPop = new Dictionary<int, float>();

            float years = intervalTicks / (float)GenDate.TicksPerYear;
            var live = new HashSet<int>();

            foreach (var obj in Find.WorldObjects.AllWorldObjects)
            {
                if (obj == null || !Integration.WorldObjectClassifier.IsSettlement(obj)) continue;
                if (obj.Faction != null && obj.Faction.IsPlayer) continue;   // player = real pawns

                int tile = obj.Tile;
                live.Add(tile);

                if (!settlementModeledPop.TryGetValue(tile, out float pop))
                    pop = Sizing.SettlementGrowthUtility.SeedPopulation(obj);

                // Capacity is the ⅔-max target; births taper above it and stagnate at 150% of it (= tier max).
                int capacity = Sizing.SettlementSizeUtility.TargetPopulationOf(obj);
                var inputs = Sizing.SettlementGrowthUtility.BuildInputs(obj);
                // Scale births and deaths together by the pacing multiplier — the balance point is
                // unchanged, only the speed. Growth runs toward the target and stagnates at the tier max.
                float mult = Integration.WorldObjectIntegrationSettings.growthRateMultiplier;
                float fertility = Sizing.BirthrateRules.Fertility(inputs) * mult;
                float mortality = Sizing.BirthrateRules.Mortality(inputs) * mult;
                float next = Sizing.BirthrateRules.GrowStep(pop, capacity, fertility, mortality, years);
                settlementModeledPop[tile] = next;

                // Publish the change at the integer level so a consumer sees growth events (no-op with
                // no consumer). Rounded+clamped the same way GetModeledSettlementPopulation reports it.
                int before = ClampToCeiling((int)Math.Round(pop, MidpointRounding.AwayFromZero), capacity);
                int after = ClampToCeiling((int)Math.Round(next, MidpointRounding.AwayFromZero), capacity);
                Sizing.SettlementGrowthHooks.Report(obj, before, after);
            }

            if (settlementModeledPop.Count > live.Count)
            {
                var stale = settlementModeledPop.Keys.Where(k => !live.Contains(k)).ToList();
                foreach (int k in stale) settlementModeledPop.Remove(k);
            }

            PopulationDensityUtility.MarkCacheDirty();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref strictTerritorialOwnershipRaw, "strictTerritorialOwnership", -1);

            // Stamp worlds generated under 0.7.2+ as they are first saved. If the algorithm is still
            // unresolved at save time this is a live world running current code that never went
            // through the load-time resolver (i.e. a new game), so it is current by construction. A
            // loaded pre-0.7.2 save has already been resolved to legacy before any save happens.
            if (Scribe.mode == LoadSaveMode.Saving && densityAlgorithmVersionRaw == -1)
            {
                densityAlgorithmVersionRaw = DensityAlgorithmCurrent;
            }
            Scribe_Values.Look(ref densityAlgorithmVersionRaw, "densityAlgorithmVersion", -1);

            // Same stamp-on-first-save rule as the density version: an unresolved algorithm at save time
            // is a live new world running current code (it never hit the load-time resolver), so it is
            // current by construction; a loaded pre-stamp save was resolved to legacy before any save.
            if (Scribe.mode == LoadSaveMode.Saving && partitionAlgorithmVersionRaw == -1)
            {
                partitionAlgorithmVersionRaw = PartitionAlgorithmCurrent;
            }
            Scribe_Values.Look(ref partitionAlgorithmVersionRaw, "partitionAlgorithmVersion", -1);

            Scribe_Collections.Look(ref provinces, "provinces", LookMode.Deep);
            if (provinces == null)
            {
                provinces = new List<GeographicProvince>();
            }

            // 0.8: sparse demographic stress overrides. The demographic baseline is deterministic
            // (regenerated from the world seed), so only deliberate changes are stored here.
            Demographics.RegionDemographicsStress.ExposeData();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ResolveStrictOwnershipForLoadedSave();
                ResolveDensityAlgorithmForLoadedSave();
                ResolvePartitionAlgorithmForLoadedSave();

                // Population is cached statically and survives across loads within one process; drop
                // it so the next read rebuilds under the algorithm just resolved for this world.
                PopulationDensityUtility.MarkCacheDirty();
            }

            Scribe_Collections.Look(ref settlementPlacementOrder, "settlementPlacementOrder", LookMode.Value, LookMode.Value);
            if (settlementPlacementOrder == null)
            {
                settlementPlacementOrder = new Dictionary<int, int>();
            }

            // Modeled NPC settlement populations (#6): the size a settlement has grown to, persisted so
            // growth continues across saves rather than reseeding.
            Scribe_Collections.Look(ref settlementModeledPop, "settlementModeledPop", LookMode.Value, LookMode.Value);
            if (settlementModeledPop == null)
            {
                settlementModeledPop = new Dictionary<int, float>();
            }

            List<int> tempList = null;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (tileToProvinceId != null)
                {
                    tempList = tileToProvinceId.ToList();
                }
            }
            Scribe_Collections.Look(ref tempList, "tileToProvinceId", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (tempList != null && Find.WorldGrid != null)
                {
                    tileToProvinceId = tempList.ToArray();
                }
                else
                {
                    InitializeData();
                }

                // Repair the reverse index from the authoritative province.tiles lists. Older saves
                // scribed a tile->province index that could fall out of sync with the provinces, or
                // never scribed one at all, leaving GetProvinceId returning -1 for tiles that plainly
                // belong to a province. Ownership buckets world objects by GetProvinceId, so that
                // silently zeroed all ownership on such saves (#67). Rebuilding here is a one-off
                // repair that then persists on the next save; it is idempotent on a healthy world.
                if (provinces != null && provinces.Count > 0 && Find.WorldGrid != null)
                {
                    RebuildTileIndexFromProvinces();
                    MarkOwnersDirty();
                }
            }
        }

        /// <summary>
        /// Rebuild the tile-&gt;province reverse index from the provinces' own tile lists — the
        /// authoritative partition (deep-scribed). Idempotent on a healthy world; a repair on a save
        /// whose scribed index was stale or absent (#67).
        /// </summary>
        private void RebuildTileIndexFromProvinces()
        {
            if (Find.WorldGrid == null || provinces == null) return;

            int n = Find.WorldGrid.TilesCount;
            if (tileToProvinceId == null || tileToProvinceId.Length != n)
            {
                tileToProvinceId = new int[n];
            }
            for (int i = 0; i < n; i++) tileToProvinceId[i] = -1;

            int mapped = 0;
            foreach (var p in provinces)
            {
                if (p?.tiles == null) continue;
                foreach (int t in p.tiles)
                {
                    if (t >= 0 && t < n)
                    {
                        tileToProvinceId[t] = p.id;
                        mapped++;
                    }
                }
            }

            Log.Message($"[RegionsAndSocieties] Rebuilt tile->province index from {provinces.Count} provinces ({mapped} tiles mapped).");
        }

        private BiomeDef GetPrimaryBiome(List<int> chunk)
        {
            if (chunk == null || chunk.Count == 0) return null;
            return chunk
                .Select(t => Find.WorldGrid[t].PrimaryBiome)
                .Where(b => b != null)
                .GroupBy(b => b)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();
        }

        public void GenerateProvinces()
        {
            Log.Message("[RegionsAndSocieties] Generating Geographic Domains (Boundary-First Priority)...");

            // A world generating provinces with the flag still unresolved is a brand new world:
            // a loaded save resolves it in PostLoadInit, which runs before anything can reach the
            // lazy Provinces getter. New worlds take the configured default, which is strict.
            if (strictTerritorialOwnershipRaw == -1)
            {
                // Static on the settings class, like every other field there.
                bool strict = FactionPlacementSettings.strictTerritorialOwnershipDefault;
                strictTerritorialOwnershipRaw = strict ? 1 : 0;
                Log.Message($"[RegionsAndSocieties] New world: territorial ownership set to {(strict ? "strict" : "compatibility")}.");
            }

            if (Find.WorldGrid == null) return;
            int totalTiles = Find.WorldGrid.TilesCount;
            tileToProvinceId = new int[totalTiles];
            for (int i = 0; i < totalTiles; i++)
            {
                tileToProvinceId[i] = -1;
            }

            provinces.Clear();

            // The derived adjacency map describes the province layout we are about to replace, and
            // it is keyed on the world instance rather than on the provinces — so regenerating
            // inside one world is the one case the key cannot catch.
            ProvinceAdjacency.ClearCache();

            int provinceIdCounter = 0;

            // Rivers no longer form their own provinces (#20). Under the border-first, river-basin
            // model a river is the CENTRE of a land province, not a boundary — the old river-segment
            // provinces and their Phase 4.5 absorption (which produced the 1-tile river tails) are
            // gone. River tiles instead seed the basin markers inside BorderPartitioner.

            int baseMin = FactionPlacementSettings.minRegionSize;
            int baseMax = FactionPlacementSettings.maxRegionSize;

            int minWithFeatures = baseMin - 5;
            int minNoFeatures = baseMin + 5;

            // Phase 2.5: Water. Flood-fill every contiguous WATER body — ocean, sea ice, lakes — into
            // its own Ocean-type province and claim the tiles, so the land partition skips them (water
            // is the hard wall the border-first fill never spans) and the open sea is owned rather than
            // left as a black hole. NOTE: RimWorld's Ocean biome is itself flagged impassable, so this
            // must NOT filter on biome.impassable (that skipped the entire ocean and left it unclaimed,
            // #20) — WaterCovered alone selects water. Impassable LAND (mountain peaks) is a different
            // case and is left for AbsorbEnclosedGaps below. Small inland lakes claimed here are folded
            // back into their surrounding land by AbsorbInlandLakes; the big ocean bodies stay as
            // provinces.
            {
                var waterNbrs = new List<RimWorld.Planet.PlanetTile>();
                for (int i = 0; i < totalTiles; i++)
                {
                    if (tileToProvinceId[i] != -1) continue;
                    Tile td = Find.WorldGrid[i];
                    if (!td.WaterCovered) continue;

                    var body = new List<int>();
                    var bq = new Queue<int>();
                    bq.Enqueue(i);
                    tileToProvinceId[i] = provinceIdCounter;
                    while (bq.Count > 0)
                    {
                        int cur = bq.Dequeue();
                        body.Add(cur);
                        waterNbrs.Clear();
                        Find.WorldGrid.GetTileNeighbors(cur, waterNbrs);
                        foreach (var n in waterNbrs)
                        {
                            int nid = n.tileId;
                            if (tileToProvinceId[nid] != -1) continue;
                            if (!Find.WorldGrid[nid].WaterCovered) continue;
                            tileToProvinceId[nid] = provinceIdCounter;
                            bq.Enqueue(nid);
                        }
                    }

                    var waterDom = new GeographicProvince(provinceIdCounter);
                    waterDom.tiles = body;
                    waterDom.provinceType = ProvinceType.Ocean;
                    waterDom.primaryBiome = GetPrimaryBiome(body);
                    waterDom.name = GenerateProvinceName(provinceIdCounter, waterDom.primaryBiome, waterDom.provinceType);
                    provinces.Add(waterDom);
                    provinceIdCounter++;
                }
            }

            // Phase 4: border-first land partition (#20). The water/ocean provinces claimed above are
            // the hard walls; BorderPartitioner floods the remaining land into cells bounded by
            // natural feature transitions (ridges, biome edges, forest bands, coasts) and splits any
            // oversized cell into river basins by a marker-controlled watershed — so borders sit on
            // features, basins centre on rivers, and region size varies with the terrain. This
            // replaces the grow-first frontier and its Phase 4.5 river absorption in one pass.
            // New worlds (and regens of new-partition worlds) use the grid-and-recombine partition: lay a
            // biome-weighted grid over the land, clip each box to one biome and a barrier-free patch, and
            // let ridges/coasts be the seams. A legacy world keeps the anchor-Voronoi PartitionLand so a
            // regenerate never reshapes an existing save.
            bool legacyPartition = PartitionAlgorithmVersion == PartitionAlgorithmLegacy;
            var swPartition = System.Diagnostics.Stopwatch.StartNew();
            var landGroups = legacyPartition
                ? Partition.BorderPartitioner.PartitionLand(tileToProvinceId, baseMin, baseMax)
                : Partition.BorderPartitioner.PartitionByGrid(tileToProvinceId, baseMin, baseMax);
            swPartition.Stop();
            Log.Message($"[RegionsAndSocieties] Land partition: {(legacyPartition ? "legacy anchor-Voronoi" : "grid-and-recombine")} produced {landGroups.Count} land groups in {swPartition.ElapsedMilliseconds} ms.");
            foreach (var group in landGroups)
            {
                if (group.Count == 0) continue;

                GeographicProvince domain = new GeographicProvince(provinceIdCounter);
                domain.tiles = group.ToList();
                domain.provinceType = ProvinceType.Land;
                domain.primaryBiome = GetPrimaryBiome(group);
                domain.name = GenerateProvinceName(provinceIdCounter, domain.primaryBiome, domain.provinceType);

                foreach (int tileId in group)
                {
                    tileToProvinceId[tileId] = provinceIdCounter;
                }
                provinces.Add(domain);
                provinceIdCounter++;
            }

            // Deduplicate tiles to ensure thread-safety for Map Mode Framework rendering
            HashSet<int> assignedTiles = new HashSet<int>();
            foreach (var p in provinces)
            {
                p.tiles = p.tiles.Distinct().ToList();
                List<int> uniqueTiles = new List<int>();
                foreach (int tileId in p.tiles)
                {
                    if (!assignedTiles.Contains(tileId))
                    {
                        assignedTiles.Add(tileId);
                        uniqueTiles.Add(tileId);
                        tileToProvinceId[tileId] = p.id;
                    }
                }
                p.tiles = uniqueTiles;
            }

            // Phase 5: Consolidation & Merging (Pass 2)
            Log.Message("[RegionsAndSocieties] Starting MergeTinyDomains...");
            MergeTinyDomains(minWithFeatures, minNoFeatures);
            Log.Message("[RegionsAndSocieties] Finished MergeTinyDomains.");

            // Phase 5b1: dissolve small inland lakes into the surrounding land (#20). Phase 2.5 floods
            // every barren water body — including a small inland lake — into its own water province; a
            // lake ringed entirely by land reads better as part of that land region than as a stranded
            // pond province, so fold it into its dominant land neighbour.
            AbsorbInlandLakes();

            // Phase 5b2: fold impassable-mountain (and other unclaimed, non-water) pockets that are
            // fully enclosed by a single region INTO that region, so they read as owned terrain rather
            // than holes punched in the map (#3).
            AbsorbEnclosedGaps();

            // Phase 5b3: split ribbon-shaped provinces (#20). A cell sized just over the guide rounds
            // to one basin and can stay a long snaking valley; break any province whose principal-axis
            // ratio is too high into compact halves across its short axis. Runs AFTER the merge so the
            // halves are not immediately re-absorbed. The viability floor is deliberately below the
            // merge minimum so a moderately-sized ribbon still splits — a pair of small blobs reads
            // far better than one long snake.
            SplitElongatedProvinces(FactionPlacementSettings.minRegionSize * 2 / 3);

            // Phase 5c: erode pendant tails and single-tile protrusions (#20). Border-first cells
            // follow natural features, but the watershed clips and feature-edge zigzags still leave
            // 1-tile-wide appendages; a light majority-vote relaxation folds a tile wrapped more by a
            // neighbour than by its own province back into that neighbour, straightening the ragged
            // edges without touching feature borders (water/impassable neighbours never vote).
            SmoothRegionBoundaries(5);

            // Naming Phase: Contextual Name Resolution
            Log.Message("[RegionsAndSocieties] Running contextual province naming...");
            ResolveContextualNames();

            // Aggregate the now-fixed topology once, so every later draw/ownership pass reads
            // perimeters and border shares instead of rescanning tiles (#48).
            BuildProvinceTopology();

            Log.Message($"[RegionsAndSocieties] Generated {provinces.Count} Geographic Domains.");
        }

        /// <summary>Largest inland lake (in tiles) still folded into its surrounding land (#20). Bigger
        /// water bodies stay their own provinces.</summary>
        private const int InlandLakeMaxTiles = 40;

        /// <summary>
        /// Dissolve small inland lakes into their dominant land neighbour (#20). A water province that is
        /// small and touches no other water province is a pond ringed by land; its tiles read better as
        /// part of that land region. Larger lakes and any water touching the sea are left alone.
        /// </summary>
        private void AbsorbInlandLakes()
        {
            if (provinces == null || tileToProvinceId == null || Find.WorldGrid == null) return;

            var byId = provinces.ToDictionary(p => p.id, p => p);
            var neighbors = new List<RimWorld.Planet.PlanetTile>();
            var toRemove = new List<GeographicProvince>();
            int absorbed = 0;

            foreach (var lake in provinces)
            {
                if (lake.provinceType != ProvinceType.Ocean || lake.tiles == null) continue;
                if (lake.tiles.Count == 0 || lake.tiles.Count > InlandLakeMaxTiles) continue;

                // Tally land neighbours by shared edges; bail if it touches any other water province
                // (then it is a sea inlet, not an enclosed pond).
                var landEdges = new Dictionary<int, int>();
                bool touchesWater = false;
                foreach (int t in lake.tiles)
                {
                    neighbors.Clear();
                    Find.WorldGrid.GetTileNeighbors(t, neighbors);
                    foreach (var n in neighbors)
                    {
                        int npid = GetProvinceId(n.tileId);
                        if (npid < 0 || npid == lake.id) continue;
                        if (!byId.TryGetValue(npid, out var np)) continue;
                        if (np.provinceType == ProvinceType.Ocean) { touchesWater = true; break; }
                        if (np.provinceType == ProvinceType.Land)
                        {
                            int c; landEdges.TryGetValue(npid, out c); landEdges[npid] = c + 1;
                        }
                    }
                    if (touchesWater) break;
                }
                if (touchesWater || landEdges.Count == 0) continue;

                int bestId = -1, bestEdges = -1;
                foreach (var kv in landEdges)
                    if (kv.Value > bestEdges || (kv.Value == bestEdges && kv.Key < bestId)) { bestEdges = kv.Value; bestId = kv.Key; }
                if (bestId < 0 || !byId.TryGetValue(bestId, out var host)) continue;

                foreach (int t in lake.tiles) { host.tiles.Add(t); tileToProvinceId[t] = host.id; }
                toRemove.Add(lake);
                absorbed += lake.tiles.Count;
            }

            foreach (var p in toRemove) provinces.Remove(p);
            if (absorbed > 0)
                Log.Message($"[RegionsAndSocieties] Absorbed {toRemove.Count} inland lake(s) ({absorbed} tiles) into surrounding land.");
        }

        /// <summary>
        /// Fold unclaimed, non-water tile pockets that are fully enclosed by a single land region into
        /// that region (#3). Impassable mountains are excluded from region growth and otherwise sit as
        /// unowned holes; when such a pocket touches exactly one land region (and neither water nor an
        /// ocean province), it belongs to that region and is absorbed. A pocket bordering two or more
        /// regions is a genuine natural boundary and is left alone.
        /// </summary>
        private void AbsorbEnclosedGaps()
        {
            if (provinces == null || tileToProvinceId == null || Find.WorldGrid == null) return;

            var byId = provinces.ToDictionary(p => p.id, p => p);
            int total = tileToProvinceId.Length;
            var visited = new bool[total];
            var neighbors = new List<RimWorld.Planet.PlanetTile>();
            int absorbed = 0;

            for (int t = 0; t < total; t++)
            {
                if (visited[t]) continue;
                visited[t] = true;
                if (tileToProvinceId[t] != -1) continue;
                if (Find.WorldGrid[t].WaterCovered) continue;   // water gaps are not "enclosed by land"

                // Flood the connected unclaimed, non-water pocket, recording which land regions ring it.
                var pocket = new List<int> { t };
                var queue = new Queue<int>();
                queue.Enqueue(t);
                var ringRegions = new HashSet<int>();
                bool openToWater = false;

                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    neighbors.Clear();
                    Find.WorldGrid.GetTileNeighbors(cur, neighbors);
                    foreach (var n in neighbors)
                    {
                        int nid = n.tileId;
                        int npid = tileToProvinceId[nid];
                        if (npid == -1)
                        {
                            if (Find.WorldGrid[nid].WaterCovered) { openToWater = true; continue; }
                            if (!visited[nid]) { visited[nid] = true; pocket.Add(nid); queue.Enqueue(nid); }
                        }
                        else if (byId.TryGetValue(npid, out var np) && np.provinceType == ProvinceType.Land)
                        {
                            ringRegions.Add(npid);
                        }
                        else
                        {
                            openToWater = true;   // bordered by ocean / a water province
                        }
                    }
                }

                if (!openToWater && ringRegions.Count == 1)
                {
                    var region = byId[System.Linq.Enumerable.First(ringRegions)];
                    foreach (int c in pocket)
                    {
                        tileToProvinceId[c] = region.id;
                        region.tiles.Add(c);
                    }
                    absorbed += pocket.Count;
                }
            }

            if (absorbed > 0)
            {
                Log.Message($"[RegionsAndSocieties] Absorbed {absorbed} enclosed impassable/unclaimed tiles into their surrounding regions.");
            }
        }

        // Split a province when its principal-axis ratio exceeds this — a long ribbon rather than a
        // basin. ~1.7 is the target (golden-ish) shape; 2.2 is where it reads as a fail.
        private const float ElongationTrigger = 2.2f;
        private const float ElongationTarget = 1.7f;

        /// <summary>
        /// Break ribbon-shaped land provinces into compact pieces (#20). Region size is allowed to vary,
        /// but a province stretched into a long valley reads as a partition failure even at a normal
        /// size. For each land province whose <see cref="Partition.BorderPartitioner.Elongation"/>
        /// exceeds <see cref="ElongationTrigger"/> and which is big enough for the pieces to stay viable,
        /// split it across its short axis into 2-3 blobs. Deterministic; runs after the merge so the
        /// pieces survive, and its seams are tidied by the smoothing pass that follows.
        /// </summary>
        private void SplitElongatedProvinces(int minViable)
        {
            if (provinces == null || tileToProvinceId == null || Find.WorldGrid == null) return;
            if (minViable < 20) minViable = 20;

            int nextId = provinces.Count > 0 ? provinces.Max(p => p.id) + 1 : 0;
            var toAdd = new List<GeographicProvince>();
            int split = 0;

            // Snapshot: we mutate the list as we go.
            foreach (var p in provinces.ToList())
            {
                if (p.provinceType != ProvinceType.Land || p.tiles == null) continue;
                if (p.tiles.Count < 2 * minViable) continue;

                float aspect = Partition.BorderPartitioner.Elongation(p.tiles);
                if (aspect < ElongationTrigger) continue;

                int byAspect = Mathf.RoundToInt(aspect / ElongationTarget);
                int byViable = p.tiles.Count / minViable;
                int pieces = Mathf.Clamp(Mathf.Min(byAspect, byViable), 2, 3);
                if (pieces < 2) continue;

                var groups = Partition.BorderPartitioner.SplitTiles(p.tiles, pieces);
                if (groups.Count < 2) continue;

                // Largest piece keeps p's identity; the rest become new provinces.
                groups.Sort((a, b) => b.Count.CompareTo(a.Count));
                p.tiles = groups[0];
                foreach (int t in p.tiles) tileToProvinceId[t] = p.id;
                p.primaryBiome = GetPrimaryBiome(p.tiles);

                for (int g = 1; g < groups.Count; g++)
                {
                    var np = new GeographicProvince(nextId++);
                    np.tiles = groups[g];
                    np.provinceType = ProvinceType.Land;
                    np.primaryBiome = GetPrimaryBiome(groups[g]);
                    np.name = GenerateProvinceName(np.id, np.primaryBiome, np.provinceType);
                    foreach (int t in groups[g]) tileToProvinceId[t] = np.id;
                    toAdd.Add(np);
                }
                split++;
            }

            provinces.AddRange(toAdd);
            if (split > 0)
                Log.Message($"[RegionsAndSocieties] Split {split} elongated province(s) into {split + toAdd.Count} pieces.");
        }

        /// <summary>
        /// Erode pendant tails and 1-tile protrusions from land provinces (#20). A majority-vote
        /// relaxation: a land tile wrapped by a neighbouring land province more than by its own
        /// (bestCount &gt; same, with same &lt;= 2 so straight and gently-curved edges are left alone) sits
        /// on a spike or a chain-tip, and moving it to that neighbour shortens the border. Water,
        /// rivers-as-edges and impassable tiles never vote, so real coastlines and feature borders are
        /// preserved. Iterated over a few passes so multi-tile tails resolve from the tip inward. This
        /// is the border-first counterpart to the grow-first smoothing that was removed with the
        /// grower — kept deliberately light, targeting only the raggedness the audit flags.
        /// </summary>
        private void SmoothRegionBoundaries(int passes)
        {
            if (provinces == null || tileToProvinceId == null || Find.WorldGrid == null) return;

            var landIds = new HashSet<int>(provinces
                .Where(p => p.provinceType == ProvinceType.Land)
                .Select(p => p.id));
            if (landIds.Count < 2) return;

            var neighbors = new List<RimWorld.Planet.PlanetTile>();
            var counts = new Dictionary<int, int>();

            for (int pass = 0; pass < passes; pass++)
            {
                var reassign = new Dictionary<int, int>();
                for (int t = 0; t < tileToProvinceId.Length; t++)
                {
                    int pid = tileToProvinceId[t];
                    if (pid < 0 || !landIds.Contains(pid)) continue;

                    neighbors.Clear();
                    Find.WorldGrid.GetTileNeighbors(t, neighbors);
                    counts.Clear();
                    int same = 0, landNeighbours = 0, bestId = -1, bestCount = 0;
                    foreach (var n in neighbors)
                    {
                        int np = tileToProvinceId[n.tileId];
                        if (np < 0 || !landIds.Contains(np)) continue;   // coast/river/impassable edge: keep it
                        landNeighbours++;
                        if (np == pid) { same++; continue; }
                        int c; counts.TryGetValue(np, out c); c++; counts[np] = c;
                        if (c > bestCount) { bestCount = c; bestId = np; }
                    }

                    // Two erosion cases, both requiring a foreign land neighbour to move into:
                    //   spike   — one neighbour wraps this tile more than its own province does;
                    //   tendril — more of this tile's land neighbours are foreign than are its own, i.e.
                    //             it sits on a 1-wide chain, even one running BETWEEN two provinces
                    //             (which the spike rule alone misses, since neither foreign province
                    //             need out-wrap the two chain neighbours). Both keep same<=2 so straight
                    //             and gently-curved borders are untouched; iterated, they shorten a
                    //             tail one tile per pass from the tip inward.
                    if (bestId != -1 && same <= 2)
                    {
                        int foreign = landNeighbours - same;
                        bool spike = landNeighbours >= 3 && bestCount > same;
                        bool tendril = foreign > same;
                        if (spike || tendril) reassign[t] = bestId;
                    }
                }

                if (reassign.Count == 0) break;
                foreach (var kv in reassign) tileToProvinceId[kv.Key] = kv.Value;
            }

            // Rebuild land tile lists from the corrected map; water/river provinces are untouched
            // above so their lists stay valid. Drop any land province emptied by the relaxation.
            var byId = provinces.ToDictionary(p => p.id, p => p);
            foreach (var p in provinces)
                if (landIds.Contains(p.id)) p.tiles = new List<int>();
            for (int t = 0; t < tileToProvinceId.Length; t++)
            {
                int pid = tileToProvinceId[t];
                GeographicProvince prov;
                if (pid >= 0 && landIds.Contains(pid) && byId.TryGetValue(pid, out prov))
                    prov.tiles.Add(t);
            }
            provinces.RemoveAll(p => landIds.Contains(p.id) && p.tiles.Count == 0);
        }

        /// <summary>Usable-tile count for a province, as an allocation-free loop (no LINQ closure).
        /// Called in the tight merge loop, where a per-call Count(predicate) closure was a memory sink.</summary>
        private int UsableTileCount(GeographicProvince p)
        {
            if (p?.tiles == null) return 0;
            int count = 0;
            List<int> tiles = p.tiles;
            for (int i = 0; i < tiles.Count; i++)
                if (IsTileUsable(tiles[i])) count++;
            return count;
        }

        private void MergeTinyDomains(int minWithFeatures, int minNoFeatures)
        {
            Log.Message($"[RegionsAndSocieties] MergeTinyDomains started. Initial region count: {provinces.Count}");
            List<RimWorld.Planet.PlanetTile> neighbors = new List<RimWorld.Planet.PlanetTile>();
            // Cache province types
            var provinceTypeMap = provinces.ToDictionary(p => p.id, p => p.provinceType);

            // Pass 0: Small Island Absorption (islands < 5 tiles, closest landmass < 3 tiles away)
            List<GeographicProvince> islandsToRemove = new List<GeographicProvince>();
            var initialProvinceMap = provinces.ToDictionary(p => p.id, p => p);
            int totalMerged = 0;

            foreach (var p in provinces)
            {
                if (p.provinceType == ProvinceType.Land && p.tiles.Count > 0 && p.tiles.Count < 5)
                {
                    int targetPid = FindClosestLandProvinceWithinDistance(p, 2, provinceTypeMap);
                    if (targetPid != -1 && initialProvinceMap.TryGetValue(targetPid, out var targetProv))
                    {
                        // Per-merge logging removed: a full world has thousands of tiny islands, and one
                        // Log.Message each stalled worldgen and ballooned memory until it crashed. The
                        // one-line summary at the end of MergeTinyDomains reports the total instead.
                        foreach (int tileId in p.tiles)
                        {
                            targetProv.tiles.Add(tileId);
                            tileToProvinceId[tileId] = targetProv.id;
                        }
                        islandsToRemove.Add(p);
                        totalMerged++;
                    }
                }
            }

            foreach (var p in islandsToRemove)
            {
                provinces.Remove(p);
            }

            int pass = 0;
            while (pass < 10) // Safety limit of 10 passes
            {
                pass++;
                bool mergedAnyInThisPass = false;
                // HashSet, not List: Contains() is hit once per province per pass, and an O(n) list scan
                // over 1000+ provinces across 10 passes was an O(n²) stall that helped starve worldgen.
                HashSet<GeographicProvince> toRemove = new HashSet<GeographicProvince>();

                // Build a quick map of province ID to the actual province object
                var provinceMap = provinces.ToDictionary(p => p.id, p => p);

                foreach (var p in provinces)
                {
                    if (p.provinceType == ProvinceType.Ocean) continue;
                    if (toRemove.Contains(p)) continue;

                    int pSize = p.tiles.Count;
                    bool isFeature = p.provinceType == ProvinceType.River || p.provinceType == ProvinceType.Lake || p.provinceType == ProvinceType.MountainRange;
                    int baseThreshold = isFeature ? 30 : minNoFeatures;

                    // Scale threshold dynamically based on tile resource density
                    float resWeight = GetResourceWeight(p);
                    float scale = Mathf.Clamp(1.5f / Mathf.Max(resWeight, 0.1f), 1f, 5f);
                    int threshold = Mathf.RoundToInt(baseThreshold * scale);

                    if (pSize >= threshold) continue;

                    // Find adjacent neighbors
                    Dictionary<int, int> neighborWeights = new Dictionary<int, int>();

                    foreach (int tile in p.tiles)
                    {
                        neighbors.Clear();
                        Find.WorldGrid.GetTileNeighbors(tile, neighbors);
                        foreach (var n in neighbors)
                        {
                            int neighborId = n.tileId;
                            int neighborProvinceId = GetProvinceId(neighborId);
                            if (neighborProvinceId != -1 && neighborProvinceId != p.id)
                            {
                                // If the neighbor province was already marked to be removed in this pass, ignore it
                                if (provinceMap.TryGetValue(neighborProvinceId, out var neighborProv))
                                {
                                    if (neighborProv.provinceType == ProvinceType.Ocean || toRemove.Contains(neighborProv)) continue;

                                    int weight = 1;
                                    if (neighborProv.provinceType == ProvinceType.Land)
                                    {
                                        weight = 100;
                                    }

                                    if (!neighborWeights.ContainsKey(neighborProvinceId))
                                    {
                                        neighborWeights[neighborProvinceId] = 0;
                                    }
                                    neighborWeights[neighborProvinceId] += weight;
                                }
                            }
                        }
                    }

                    if (neighborWeights.Any())
                    {
                        var sortedNeighbors = neighborWeights.OrderByDescending(kv => kv.Value).ToList();
                        GeographicProvince bestNeighbor = null;
                        GeographicProvince dominantLand = null;

                        // Compute p's usable-tile count once, not once per neighbour: the per-neighbour
                        // LINQ Count(predicate) allocated a closure every call and was the small-allocation
                        // storm the OOM crash dump showed.
                        int pUsable = UsableTileCount(p);

                        foreach (var kvp in sortedNeighbors)
                        {
                            if (provinceMap.TryGetValue(kvp.Key, out var neighborProv))
                            {
                                // Remember the highest-weight (most shared edges) land neighbour as a
                                // rescue target, regardless of the size cap.
                                if (dominantLand == null && neighborProv.provinceType == ProvinceType.Land)
                                    dominantLand = neighborProv;

                                if (UsableTileCount(neighborProv) + pUsable <= FactionPlacementSettings.maxRegionSize + 50)
                                {
                                    bestNeighbor = neighborProv;
                                    break;
                                }
                            }
                        }

                        // Orphan rescue (#3, widened #20): a small province whose only neighbours are
                        // already at or past the size cap would otherwise survive as a stranded sliver
                        // next to a big region. Fold it into its dominant land neighbour anyway — an
                        // oversized region reads far better than a too-small one, and large sparse
                        // regions are natural here. Bounded to genuinely small provinces (< the target
                        // minimum) so a medium region is never chained into a runaway monster.
                        if (bestNeighbor == null && dominantLand != null &&
                            p.tiles.Count < FactionPlacementSettings.minRegionSize)
                        {
                            bestNeighbor = dominantLand;
                        }

                        if (bestNeighbor != null)
                        {
                            // Merge p into bestNeighbor
                            foreach (int tileId in p.tiles)
                            {
                                bestNeighbor.tiles.Add(tileId);
                                tileToProvinceId[tileId] = bestNeighbor.id;
                            }
                            toRemove.Add(p);
                            mergedAnyInThisPass = true;
                            totalMerged++;
                        }
                    }
                }

                if (!mergedAnyInThisPass)
                {
                    break;
                }

                // Remove the merged provinces
                foreach (var p in toRemove)
                {
                    provinces.Remove(p);
                }
            }

            Log.Message($"[RegionsAndSocieties] MergeTinyDomains finished. Merged {totalMerged} regions in {pass} passes. Final region count: {provinces.Count}");
        }

        private float GetResourceWeight(GeographicProvince p)
        {
            if (p.tiles == null || p.tiles.Count == 0 || Find.WorldGrid == null) return 1.0f;
            float total = 0f;
            foreach (int tileId in p.tiles)
            {
                Tile t = Find.WorldGrid[tileId];
                var b = t.PrimaryBiome;
                if (b != null)
                {
                    total += b.plantDensity + b.forageability + b.TreeDensity;
                }
                if (t.hilliness == Hilliness.SmallHills) total += 0.5f;
                else if (t.hilliness == Hilliness.LargeHills) total += 1.0f;
                else if (t.hilliness == Hilliness.Mountainous) total += 1.5f;
            }
            return total / p.tiles.Count;
        }

        private int FindClosestLandProvinceWithinDistance(GeographicProvince island, int maxDistance, Dictionary<int, ProvinceType> provinceTypeMap)
        {
            Queue<KeyValuePair<int, int>> queue = new Queue<KeyValuePair<int, int>>();
            HashSet<int> visited = new HashSet<int>();

            foreach (int t in island.tiles)
            {
                queue.Enqueue(new KeyValuePair<int, int>(t, 0));
                visited.Add(t);
            }

            List<RimWorld.Planet.PlanetTile> neighbors = new List<RimWorld.Planet.PlanetTile>();

            while (queue.Count > 0)
            {
                var currentKvp = queue.Dequeue();
                int currentTile = currentKvp.Key;
                int currentDepth = currentKvp.Value;

                if (currentDepth > maxDistance) continue;

                neighbors.Clear();
                Find.WorldGrid.GetTileNeighbors(currentTile, neighbors);
                foreach (var n in neighbors)
                {
                    int nid = n.tileId;
                    if (visited.Contains(nid)) continue;
                    visited.Add(nid);

                    int pid = tileToProvinceId[nid];
                    if (pid != -1 && pid != island.id)
                    {
                        if (provinceTypeMap.TryGetValue(pid, out var type) && type == ProvinceType.Land)
                        {
                            return pid;
                        }
                    }

                    if (Find.WorldGrid[nid].WaterCovered && currentDepth < maxDistance)
                    {
                        queue.Enqueue(new KeyValuePair<int, int>(nid, currentDepth + 1));
                    }
                }
            }

            return -1;
        }

        private void ResolveContextualNames()
        {
            if (Find.WorldFeatures == null || Find.WorldFeatures.features.NullOrEmpty()) return;

            // Cache centroids of all vanilla WorldFeatures
            var featureCentroids = new Dictionary<WorldFeature, Vector3>();
            foreach (var wf in Find.WorldFeatures.features)
            {
                if (!wf.Tiles.Any()) continue;
                Vector3 center = Vector3.zero;
                foreach (int t in wf.Tiles)
                {
                    center += Find.WorldGrid.GetTileCenter(t);
                }
                featureCentroids[wf] = center / wf.Tiles.Count();
            }

            foreach (var province in provinces)
            {
                if (province.tiles.Count == 0) continue;

                // Calculate province centroid
                Vector3 provinceCenter = Vector3.zero;
                foreach (int t in province.tiles)
                {
                    provinceCenter += Find.WorldGrid.GetTileCenter(t);
                }
                provinceCenter /= province.tiles.Count;

                // Find the closest WorldFeature
                WorldFeature closestFeature = null;
                float minSqrDist = float.MaxValue;
                foreach (var kvp in featureCentroids)
                {
                    float sqrDist = (provinceCenter - kvp.Value).sqrMagnitude;
                    if (sqrDist < minSqrDist)
                    {
                        minSqrDist = sqrDist;
                        closestFeature = kvp.Key;
                    }
                }

                if (closestFeature != null)
                {
                    // If directly overlapping a vanilla feature, use its name — but land regions keep the
                    // simple "Region <id>" / "<settlement> Region" scheme (0.7.3); only water/mountain
                    // features carry a geographic name.
                    var directOverlap = Find.WorldFeatures.features
                        .FirstOrDefault(wf => wf.Tiles.Any(t => province.tiles.Contains(t)));

                    if (directOverlap != null && province.provinceType != ProvinceType.Land)
                    {
                        province.name = directOverlap.name;
                    }
                    else
                    {
                        // Infer name based on closest feature
                        if (province.provinceType == ProvinceType.Lake)
                        {
                            province.name = closestFeature.name.Contains("Lake") || closestFeature.name.Contains("Sea") 
                                ? closestFeature.name 
                                : $"{closestFeature.name} Lake";
                        }
                        else if (province.provinceType == ProvinceType.Ocean)
                        {
                            province.name = closestFeature.name.Contains("Ocean") 
                                ? closestFeature.name 
                                : $"{closestFeature.name} Ocean";
                        }
                        else if (province.provinceType == ProvinceType.MountainRange)
                        {
                            province.name = closestFeature.name.Contains("Mountains") || closestFeature.name.Contains("Range") 
                                ? closestFeature.name 
                                : $"{closestFeature.name} Mountains";
                        }
                        else if (province.provinceType == ProvinceType.River)
                        {
                            province.name = GenerateRiverName(province.id, closestFeature.name);
                        }
                    }
                }
            }
        }

        private string GenerateRiverName(int id, string nearbyFeatureName)
        {
            var prefixes = new[] { "Silent", "Whispering", "Shimmering", "Roaring", "Winding", "Deep", "Swift", "Cold", "Grey", "Green", "Red", "Silver", "Golden", "Muddy", "Black", "Wild", "Broad", "Shadow", "Serpent", "Ghost", "Sun", "Moon", "Star", "Glimmering", "Ember", "Frost" };
            var suffixes = new[] { "River", "Creek", "Flow", "Fork", "Run", "Torrent", "Stream", "Waters", "Channel" };

            System.Random rand = new System.Random(id * 79 + 37);

            // 50% chance to name after nearby feature, 50% to generate a generic beautiful name
            if (rand.NextDouble() < 0.5f && !string.IsNullOrEmpty(nearbyFeatureName))
            {
                string cleanName = nearbyFeatureName
                    .Replace("Mountains", "")
                    .Replace("Mountain Range", "")
                    .Replace("Scrubland", "")
                    .Replace("Scrublands", "")
                    .Replace("Forest", "")
                    .Replace("Tangle", "")
                    .Replace("Basin", "")
                    .Replace("Swamp", "")
                    .Replace("Bog", "")
                    .Trim();

                string suffix = suffixes[rand.Next(suffixes.Length)];
                return $"{cleanName} {suffix}";
            }
            else
            {
                string prefix = prefixes[rand.Next(prefixes.Length)];
                string suffix = suffixes[rand.Next(suffixes.Length)];
                return $"{prefix} {suffix}";
            }
        }

        private string GenerateProvinceName(int provinceId, BiomeDef biome, ProvinceType type)
        {
            if (type == ProvinceType.Ocean) return "Ocean Region " + provinceId;
            if (type == ProvinceType.Lake) return "Lake Region " + provinceId;
            if (type == ProvinceType.River) return "River Region " + provinceId;
            if (type == ProvinceType.MountainRange) return "Mountain Region " + provinceId;

            return GenerateProvinceName(provinceId, biome);
        }

        private string GenerateProvinceName(int provinceId, BiomeDef biome)
        {
            // 0.7.3: a land region is simply "Region <id>". If it holds a settlement, RecalculateProvinceOwners
            // renames it "<settlement> Region"; the id is always shown in the expanded region details.
            return "Region " + provinceId;
        }

        /// <summary>
        /// 0.7.3 naming: a land region is named after the settlement standing in it ("&lt;settlement&gt; Region"),
        /// or "Region &lt;id&gt;" when it holds none. Called each ownership recompute so the name tracks a
        /// settlement being founded or lost. Water/mountain regions keep their geographic feature names.
        /// The id itself is always shown in the expanded region details, independent of the name.
        /// </summary>
        private void UpdateProvinceName(GeographicProvince province, List<RimWorld.Planet.WorldObject> regionObjects)
        {
            if (province == null || province.provinceType != ProvinceType.Land) return;

            RimWorld.Planet.WorldObject settlement = null;
            if (regionObjects != null)
            {
                foreach (var o in regionObjects)
                {
                    if (o != null && o.Faction != null && Integration.WorldObjectClassifier.IsSettlement(o)) { settlement = o; break; }
                }
            }
            province.name = settlement != null ? $"{settlement.LabelCap} Region" : "Region " + province.id;
        }

        private bool topologyBuilt;

        /// <summary>
        /// Precompute every province's perimeter tiles and per-neighbour border-edge counts in a
        /// single pass over the world grid. Province topology is fixed once the provinces exist, so
        /// this runs once — at generation, and rebuilt lazily after a load — and every later
        /// perimeter/border query reads the aggregate instead of rescanning tiles. Replaces the
        /// per-call flood-fill in <see cref="RegionalOwnershipUtility.GetPerimeterTiles"/> and gives
        /// the border-share data the ownership scoring consumes.
        /// </summary>
        public void BuildProvinceTopology()
        {
            if (provinces == null || tileToProvinceId == null || Find.WorldGrid == null) return;

            // Local id map: GetProvince is an O(provinces) scan, so calling it per tile would make
            // this O(tiles * provinces).
            var byId = new Dictionary<int, GeographicProvince>(provinces.Count);
            foreach (var p in provinces)
            {
                p.perimeterTiles = new List<int>();
                p.borderShares = new Dictionary<int, int>();
                p.perimeterEdgeCount = 0;
                p.naturalBorderEdges = 0;
                byId[p.id] = p;
            }

            var neighbors = new List<RimWorld.Planet.PlanetTile>();
            for (int t = 0; t < tileToProvinceId.Length; t++)
            {
                int pid = tileToProvinceId[t];
                if (pid < 0) continue;
                GeographicProvince prov;
                if (!byId.TryGetValue(pid, out prov)) continue;
                // Water provinces are never owned or contested, so they need no perimeter/border-share
                // topology. Skipping them also stops the (huge, claimed) ocean from accumulating a
                // border-share to every coastal land province — the source of the "coastal faction
                // holds the sea" ownership bleed once the ocean became a real province (#20).
                if (prov.provinceType == ProvinceType.Ocean) continue;

                neighbors.Clear();
                Find.WorldGrid.GetTileNeighbors(t, neighbors);
                bool boundary = false;
                foreach (var n in neighbors)
                {
                    int npid = tileToProvinceId[n.tileId];
                    if (npid == pid) continue;      // interior edge
                    boundary = true;
                    prov.perimeterEdgeCount++;

                    // A frontier against water or an impassable mountain is a secure natural border —
                    // it counts for this region's own owner, not as a contestable land border (#44).
                    Tile nt = Find.WorldGrid[n.tileId];
                    bool naturalBarrier = nt.WaterCovered || nt.hilliness == Hilliness.Impassable
                        || (nt.PrimaryBiome != null && nt.PrimaryBiome.impassable);
                    if (naturalBarrier)
                    {
                        prov.naturalBorderEdges++;
                    }
                    else if (npid >= 0)             // contestable edge to another land province
                    {
                        int c;
                        prov.borderShares.TryGetValue(npid, out c);
                        prov.borderShares[npid] = c + 1;
                    }
                    // else: unassigned non-natural land (rare) — not counted
                }
                if (boundary) prov.perimeterTiles.Add(t);
            }
            topologyBuilt = true;
        }

        /// <summary>Build the topology aggregate once per session (covers both generation and load).</summary>
        public void EnsureTopology()
        {
            if (!topologyBuilt) BuildProvinceTopology();
        }

        private static readonly List<RimWorld.Planet.WorldObject> EmptyWorldObjects = new List<RimWorld.Planet.WorldObject>();

        // Bumped whenever a territorial holding (settlement/outpost/military/camp) is added or
        // removed — the only world-object changes that alter ownership. Static so it survives the
        // fresh component a load creates and so the PostAdd/PostRemove patch can bump it without an
        // instance. Population changes (which do not affect ownership) deliberately do not bump it.
        private static int ownershipEpoch;
        public static void BumpOwnershipEpoch()
        {
            ownershipEpoch++;
            // A territorial holding changed, so the global border overlay's per-region colours are stale.
            // Bump its build version so the world layer rebuilds its mesh next frame (#72). Without this the
            // overlay — which only rebuilds on a version change — keeps whatever colours it first painted.
            UI.RegionBorderOverlay.Invalidate();
        }

        private int ownersComputedVersion = -1;
        private int ownersFactionCount = -1;

        /// <summary>Force the next <see cref="RecalculateProvinceOwners"/> to recompute rather than
        /// reuse the cache — for inputs the epoch/count gate does not observe (e.g. a demographic
        /// provider registering, or a settlement changing faction without an add/remove).</summary>
        public void MarkOwnersDirty()
        {
            ownersComputedVersion = -1;
        }

        public void RecalculateProvinceOwners()
        {
            if (Find.WorldObjects == null || provinces == null) return;
            EnsureTopology();

            // Ownership depends only on the territorial holdings present (add/remove bumps
            // ownershipEpoch) and on which factions exist (defeat/creation changes the count). When
            // neither has changed since the last pass the cached ownershipData/owningFactionIds are
            // still valid, so the entire recompute — bucketing, perimeter owner mapping, scoring — is
            // skipped. This is what turns "recompute on every draw" into "recompute only on change"
            // (#48). MarkOwnersDirty covers the inputs this gate cannot see.
            int epoch = ownershipEpoch;
            int factionCount = Find.FactionManager?.AllFactionsListForReading?.Count ?? 0;
            if (ownersComputedVersion == epoch && ownersFactionCount == factionCount) return;
            ownersComputedVersion = epoch;
            ownersFactionCount = factionCount;

            // Bucket every world object into its province in one pass (O(worldObjects)), so each
            // province's ownership reads its own objects instead of filtering AllWorldObjects with a
            // List.Contains over its tiles — which was O(worldObjects * tiles) per province (#48).
            var objectsByProvince = new Dictionary<int, List<RimWorld.Planet.WorldObject>>();
            foreach (var obj in Find.WorldObjects.AllWorldObjects)
            {
                if (obj == null) continue;
                int opid = GetProvinceId(obj.Tile);
                if (opid < 0) continue;
                List<RimWorld.Planet.WorldObject> bucket;
                if (!objectsByProvince.TryGetValue(opid, out bucket))
                {
                    bucket = new List<RimWorld.Planet.WorldObject>();
                    objectsByProvince[opid] = bucket;
                }
                bucket.Add(obj);
            }

            // Pass 1: each province's ownership from its own holdings only, plus its dominant owner —
            // what neighbours read when computing their border scores.
            var ownerByProvince = new Dictionary<int, Faction>(provinces.Count);
            foreach (var province in provinces)
            {
                // Open water is never owned — skip it so a coastal faction is not written in as
                // "holding" the sea (which would leak supply anchors and foothold adjacency along the
                // whole coastline now that the ocean is a real province, #20).
                if (province.provinceType == ProvinceType.Ocean) { province.owningFactionIds.Clear(); continue; }

                List<RimWorld.Planet.WorldObject> regionObjects;
                if (!objectsByProvince.TryGetValue(province.id, out regionObjects)) regionObjects = EmptyWorldObjects;
                province.ownershipData = RegionalOwnershipUtility.CalculateOwnershipBase(province, regionObjects);
                ownerByProvince[province.id] = RegionalOwnershipUtility.DominantBaseOwner(province.ownershipData);
                UpdateProvinceName(province, regionObjects);
            }

            // Pass 2: fold in border influence from neighbours' owners over the static borderShares,
            // normalize, and publish the owning-faction list. The geometry is precomputed, so this is
            // where "region 487 changed owner -> recompute 326's borders" stays cheap (#44).
            foreach (var province in provinces)
            {
                if (province.provinceType == ProvinceType.Ocean) continue;
                RegionalOwnershipUtility.ApplyBordersAndNormalize(province.ownershipData, province, ownerByProvince);

                province.owningFactionIds.Clear();
                var data = province.ownershipData;
                if (data != null && data.factionScores != null)
                {
                    foreach (var fs in data.factionScores)
                    {
                        if (fs.faction != null && fs.TotalScore > Placement.PlacementRules.PresenceFloor)
                        {
                            string fid = fs.faction.GetUniqueLoadID();
                            if (!province.owningFactionIds.Contains(fid))
                            {
                                province.owningFactionIds.Add(fid);
                            }
                        }
                    }
                }
            }
        }

        public bool AreProvincesAdjacent(GeographicProvince a, GeographicProvince b)
        {
            if (a == null || b == null) return false;
            if (a.id == b.id) return true;

            // Check if any tile in 'a' shares a neighbor with any tile in 'b'
            foreach (int tileA in a.tiles)
            {
                foreach (int tileB in b.tiles)
                {
                    if (Find.WorldGrid.IsNeighbor(tileA, tileB))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static bool IsTileUsable(int tileId)
        {
            if (Find.WorldGrid == null) return false;
            Tile tileData = Find.WorldGrid[tileId];
            if (tileData == null) return false;
            if (tileData.WaterCovered || tileData.hilliness == Hilliness.Impassable) return false;
            if (tileData.PrimaryBiome != null && (tileData.PrimaryBiome.impassable || tileData.PrimaryBiome.defName == "SeaIce")) return false;
            return true;
        }
    }
}
