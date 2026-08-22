using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RegionsAndSocieties.Integration;
using UnityEngine;
using Verse;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>One tile's people: a race, a wealth level, an ideology, a sex — all derived, never stored.</summary>
    public struct TileDemographicSample
    {
        public Faction owner;      // the faction exerting the most pressure here (for reference/aggregation)
        public XenotypeDef race;   // null when Biotech is off (plain human)
        public int wealth;         // silver-ish, reflects the race's socioeconomic class
        public Ideo ideo;          // null when Ideology is off
        public Gender sex;
        public AgeBucket ageBucket; // child / working-age / elder, drawn from the local age pyramid (#10)
    }

    /// <summary>A region's aggregated makeup, computed from its tile samples. Purely derived.</summary>
    public class RegionDemographics
    {
        public int tileCount;
        public int settledTiles;   // tiles under some settlement's demographic pressure
        public readonly Dictionary<Faction, float> factionShares = new Dictionary<Faction, float>();   // dominant-pressure owner per tile
        public readonly Dictionary<XenotypeDef, float> raceShares = new Dictionary<XenotypeDef, float>();
        public readonly Dictionary<XenotypeDef, int> medianWealthByRace = new Dictionary<XenotypeDef, int>();
        public readonly Dictionary<MemeDef, float> memeShares = new Dictionary<MemeDef, float>();
        public float femaleFraction;
        public int overallMedianWealth;
        // Age structure (#10): the share of settled tiles in each bucket, and the resulting median age.
        // Indexed by (int)AgeBucket — [child, working-age, elder]. All zero for an unsettled region.
        public readonly float[] ageShares = new float[AgeStructureRules.BucketCount];
        public int medianAge;
        public bool biotechActive;
        public bool ideologyActive;
    }

    /// <summary>
    /// The demographics facade (0.8, #36). Every faction has a <b>target demographic</b> — its normal
    /// make-up (Pig Union = pigskins with their ideology), captured in <see cref="FactionDemographicProfile"/>.
    /// Each of its settlements <b>broadcasts that target as pressure</b> over a radius equal to the
    /// settlement's population, falling off linearly with straight-line ("crow flies") distance. A
    /// tile's <b>realized</b> demographic is the pressure-weighted blend of every settlement reaching it,
    /// so overlapping cities mix naturally and no tile has a single owner. As a city's population drifts
    /// its reach breathes with it.
    ///
    /// <para>Nothing is scribed: the per-tile draw is deterministic from the world seed, and the region
    /// aggregate is those draws counted. Only deliberate changes (the stress API,
    /// <see cref="RegionDemographicsStress"/>) cost memory. Degrades cleanly — no Biotech → plain human,
    /// no Ideology → no meme axis.</para>
    /// </summary>
    public static class RegionDemographicsUtility
    {
        private const int RaceSalt = 11;
        private const int WealthSalt = 13;
        private const int SexSalt = 17;
        private const int IdeoSalt = 19;
        private const int AgeSalt = 23;

        private static readonly Dictionary<Faction, FactionDemographicProfile> profileCache = new Dictionary<Faction, FactionDemographicProfile>();
        private static readonly Dictionary<int, RegionDemographics> regionCache = new Dictionary<int, RegionDemographics>();
        private static readonly Dictionary<Faction, RegionDemographics> factionCache = new Dictionary<Faction, RegionDemographics>();
        private static List<PressureSource> sources;
        private static int cacheVersion = -1;
        private static float avgTileSize = -1f;

        public static void InvalidateCache()
        {
            profileCache.Clear();
            regionCache.Clear();
            factionCache.Clear();
            sources = null;
            cacheVersion = -1;
        }

        /// <summary>Drop only the aggregated region cache — used when a stress override changes.</summary>
        public static void InvalidateRegionCache()
        {
            regionCache.Clear();
        }

        private static void EnsureFresh()
        {
            int v = PopulationDensityUtility.CacheVersion;
            if (v != cacheVersion)
            {
                profileCache.Clear();
                regionCache.Clear();
                factionCache.Clear();
                sources = null;
                cacheVersion = v;
            }
        }

        private static int WorldSeed => Find.World?.info?.Seed ?? 0;

        // A settlement broadcasting its faction's target demographic. Population is the pressure radius.
        private struct PressureSource
        {
            public int tile;
            public Faction faction;
            public int population;
        }

        /// <summary>
        /// True only for a tile that participates in the SURFACE demographic pressure field: a valid,
        /// in-range tile on the root surface layer. Off-surface tiles (orbital / space / gravship on an
        /// Odyssey planet with extra <see cref="PlanetLayer"/>s) and out-of-range tiles are rejected so their
        /// per-layer <c>tileId</c> is never indexed against the surface grid — doing so makes vanilla
        /// <c>PlanetLayer.GetTileCenter</c> log "Attempted to access a tile ... out of range (count: N)" once
        /// per call and return zero, which spammed the log during play (#77). <see cref="PlanetTile.Valid"/>
        /// only checks <c>tileId &gt;= 0</c>, so it is not sufficient on its own.
        /// </summary>
        public static bool IsSurfaceSampleTile(PlanetTile tile)
        {
            WorldGrid grid = Find.WorldGrid;
            return grid != null
                && tile.Valid
                && tile.tileId >= 0
                && tile.tileId < grid.TilesCount
                && tile.Layer.IsRootSurface;
        }

        /// <summary>The deterministic people of one tile: the pressure-weighted blend of the settlements
        /// reaching it, then a fixed draw from that blend by the tile seed.</summary>
        public static TileDemographicSample SampleTile(int tileId)
        {
            var sample = new TileDemographicSample();

            uint sexState = DemographicsRules.TileSeed(WorldSeed, tileId, SexSalt);
            sample.sex = DemographicsRules.NextFloat(ref sexState) < 0.5f ? Gender.Female : Gender.Male;

            WorldGrid grid = Find.WorldGrid;
            List<PressureSource> srcs = Sources();
            if (grid == null || srcs.Count == 0) return sample;   // wilderness: a sex, nothing else

            // A demographic sample is a SURFACE phenomenon. An off-surface / out-of-range tileId — e.g. an
            // orbital or otherwise non-surface origin handed in by pawn generation on an Odyssey planet with
            // extra PlanetLayers — must never reach the surface grid: GetTileCenter (via CrowTiles below)
            // logs "out of range (count: <surface>)" once per call and returns zero, spamming the log (#77).
            if (tileId < 0 || tileId >= grid.TilesCount) return sample;
            Tile tile = grid[tileId];   // the land itself pushes on race (biome affinity), below
            if (tile == null) return sample;

            // Accumulate the overlapping pressures into a blended race distribution + per-race wealth,
            // and a faction pressure list for the ideology draw.
            var raceWeight = new Dictionary<XenotypeDef, float>();
            var raceWealthAcc = new Dictionary<XenotypeDef, double>();
            var raceWealthWt = new Dictionary<XenotypeDef, double>();
            float humanWeight = 0f; double humanWealthAcc = 0, humanWealthWt = 0;   // null-race bucket (Biotech off)
            var pf = new List<Faction>(); var pw = new List<float>();
            Faction top = null; float topP = 0f;

            for (int i = 0; i < srcs.Count; i++)
            {
                PressureSource s = srcs[i];
                float pressure = Pressure(s.population, CrowTiles(grid, s.tile, tileId));
                if (pressure <= 0f) continue;

                FactionDemographicProfile prof = ProfileFor(s.faction);
                pf.Add(s.faction); pw.Add(pressure);
                if (pressure > topP) { topP = pressure; top = s.faction; }

                float wsum = 0f;
                for (int r = 0; r < prof.raceWeights.Length; r++) wsum += prof.raceWeights[r];

                if (prof.races.Length == 0 || wsum <= 0f)
                {
                    humanWeight += pressure;
                    humanWealthAcc += pressure * prof.fallbackWealth; humanWealthWt += pressure;
                }
                else
                {
                    for (int r = 0; r < prof.races.Length; r++)
                    {
                        XenotypeDef race = prof.races[r];
                        // Second pressure layer: the land favours races engineered for it. A tox-adapted
                        // race weighs more on polluted ground, a cold-gene race in the tundra, etc.
                        float contrib = pressure * (prof.raceWeights[r] / wsum) * BiomeAffinity(race, tile);
                        raceWeight.TryGetValue(race, out float rw); raceWeight[race] = rw + contrib;
                        raceWealthAcc.TryGetValue(race, out double wa); raceWealthAcc[race] = wa + contrib * prof.raceMedianWealth[r];
                        raceWealthWt.TryGetValue(race, out double ww); raceWealthWt[race] = ww + contrib;
                    }
                }
            }

            if (pf.Count == 0) return sample;   // no city reaches this tile
            sample.owner = top;

            // Deterministic race pick from the blend. Options are sorted by defName so the pick is stable
            // across machines (dictionary order is not).
            var races = new List<XenotypeDef>(raceWeight.Keys);
            races.Sort((a, b) => string.CompareOrdinal(a.defName, b.defName));
            int optionCount = races.Count + (humanWeight > 0f ? 1 : 0);
            var weights = new float[optionCount];
            for (int i = 0; i < races.Count; i++) weights[i] = raceWeight[races[i]];
            if (humanWeight > 0f) weights[optionCount - 1] = humanWeight;   // human bucket last

            uint raceState = DemographicsRules.TileSeed(WorldSeed, tileId, RaceSalt);
            int pick = DemographicsRules.WeightedPick(ref raceState, weights);
            XenotypeDef chosen = (pick >= 0 && pick < races.Count) ? races[pick] : null;
            sample.race = chosen;

            double baseWealth;
            if (chosen != null && raceWealthWt.TryGetValue(chosen, out double wt) && wt > 0)
                baseWealth = raceWealthAcc[chosen] / wt;
            else
                baseWealth = humanWealthWt > 0 ? humanWealthAcc / humanWealthWt : ProfileFor(top).fallbackWealth;

            uint wealthState = DemographicsRules.TileSeed(WorldSeed, tileId, WealthSalt);
            sample.wealth = DemographicsRules.RangeInt(ref wealthState, (int)(baseWealth * 0.6), (int)(baseWealth * 1.4));

            // Ideology and age both hang off the same pressure-weighted faction draw (factions sorted by
            // load id for determinism) — the society whose norms and demographics dominate this tile.
            Faction ageFaction = PressureWeightedFaction(tileId, pf, pw, top);
            FactionDemographicProfile ageProf = ProfileFor(ageFaction);
            sample.ideo = ageProf.primaryIdeo;

            // Age bucket: draw from the faction's tech/ideology pyramid, bent by the chosen race's
            // longevity (a long-lived caste holds more elders). Independent salt so it doesn't correlate
            // with the race/wealth/sex/ideo draws.
            float[] agePyramid = AgeStructureRules.Pyramid(ageProf.techLevel, ageProf.natalistSkew, LongevityOf(chosen));
            uint ageState = DemographicsRules.TileSeed(WorldSeed, tileId, AgeSalt);
            int agePick = DemographicsRules.WeightedPick(ref ageState, agePyramid);
            sample.ageBucket = agePick >= 0 ? (AgeBucket)agePick : AgeBucket.WorkingAge;
            return sample;
        }

        private static Faction PressureWeightedFaction(int tileId, List<Faction> factions, List<float> pressures, Faction fallback)
        {
            int n = factions.Count;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort(order, (a, b) => string.CompareOrdinal(
                factions[a].GetUniqueLoadID(), factions[b].GetUniqueLoadID()));

            var w = new float[n];
            for (int i = 0; i < n; i++) w[i] = pressures[order[i]];

            uint state = DemographicsRules.TileSeed(WorldSeed, tileId, IdeoSalt);
            int pick = DemographicsRules.WeightedPick(ref state, w);
            return pick >= 0 ? factions[order[pick]] : fallback;
        }

        /// <summary>The aggregated makeup of a region, cached. Recomputed when populations/objects change.</summary>
        public static RegionDemographics ForRegion(GeographicProvince province)
        {
            EnsureFresh();
            if (province?.tiles == null || province.tiles.Count == 0) return new RegionDemographics();
            if (regionCache.TryGetValue(province.id, out RegionDemographics cached)) return cached;

            var demo = Aggregate(province);
            RegionDemographicsStress.Apply(province.id, demo);   // sparse overrides on top of the baseline
            regionCache[province.id] = demo;
            return demo;
        }

        private static RegionDemographics Aggregate(GeographicProvince province)
        {
            var demo = new RegionDemographics
            {
                biotechActive = ModLister.BiotechInstalled,
                ideologyActive = ModLister.IdeologyInstalled
            };

            var raceCounts = new Dictionary<XenotypeDef, int>();
            var factionCounts = new Dictionary<Faction, int>();
            var wealthByRace = new Dictionary<XenotypeDef, List<int>>();
            var memeCounts = new Dictionary<MemeDef, int>();
            var allWealth = new List<int>();
            var ageCounts = new int[AgeStructureRules.BucketCount];
            float longevityAcc = 0f;
            int female = 0;

            List<int> tiles = province.tiles;
            demo.tileCount = tiles.Count;
            for (int i = 0; i < tiles.Count; i++)
            {
                TileDemographicSample s = SampleTile(tiles[i]);
                if (s.sex == Gender.Female) female++;
                if (s.owner == null) continue;   // no pressure here — contributes only to the sex ratio

                demo.settledTiles++;
                ageCounts[(int)s.ageBucket]++;
                longevityAcc += LongevityOf(s.race);
                factionCounts.TryGetValue(s.owner, out int fc); factionCounts[s.owner] = fc + 1;
                if (s.race != null)
                {
                    raceCounts.TryGetValue(s.race, out int rc); raceCounts[s.race] = rc + 1;
                    if (!wealthByRace.TryGetValue(s.race, out List<int> wl)) { wl = new List<int>(); wealthByRace[s.race] = wl; }
                    wl.Add(s.wealth);
                }
                allWealth.Add(s.wealth);

                if (s.ideo?.memes != null)
                    foreach (MemeDef m in s.ideo.memes)
                        if (m != null) { memeCounts.TryGetValue(m, out int mc); memeCounts[m] = mc + 1; }
            }

            demo.femaleFraction = demo.tileCount > 0 ? (float)female / demo.tileCount : 0f;

            if (demo.settledTiles > 0)
            {
                foreach (var kv in factionCounts)
                    demo.factionShares[kv.Key] = (float)kv.Value / demo.settledTiles;
                foreach (var kv in raceCounts)
                {
                    demo.raceShares[kv.Key] = (float)kv.Value / demo.settledTiles;
                    int[] arr = wealthByRace[kv.Key].ToArray();
                    demo.medianWealthByRace[kv.Key] = DemographicsRules.Median(arr, arr.Length);
                }
                foreach (var kv in memeCounts)
                    demo.memeShares[kv.Key] = (float)kv.Value / demo.settledTiles;

                int[] wealthArr = allWealth.ToArray();
                demo.overallMedianWealth = DemographicsRules.Median(wealthArr, wealthArr.Length);

                FillAgeStructure(demo, ageCounts, longevityAcc);
            }

            return demo;
        }

        /// <summary>
        /// A compact age-structure readout for a region — median age and the three bucket shares — or
        /// null when it has no settled tiles. One formatter shared by the map overlay tooltip, the
        /// region selection panel and the debug report, so those three never drift apart.
        /// </summary>
        public static string AgeStructureSummary(GeographicProvince province)
        {
            if (province == null) return null;
            RegionDemographics demo = ForRegion(province);
            if (demo.settledTiles <= 0) return null;
            return $"Age structure (median {demo.medianAge}):\n"
                + $"  Children {demo.ageShares[(int)AgeBucket.Child]:P0}"
                + $"   Working-age {demo.ageShares[(int)AgeBucket.WorkingAge]:P0}"
                + $"   Elders {demo.ageShares[(int)AgeBucket.Elder]:P0}";
        }

        /// <summary>
        /// A one-line sex-ratio readout for a region — percent female / male, with a note when a
        /// mod-driven skew (draft, war losses) is currently bending it off the baseline — or null when
        /// the region has no settled tiles. Shared by the overlay tooltip and the region panel (#11).
        /// </summary>
        public static string SexRatioSummary(GeographicProvince province)
        {
            if (province == null) return null;
            RegionDemographics demo = ForRegion(province);
            if (demo.settledTiles <= 0) return null;

            int femalePct = Mathf.RoundToInt(demo.femaleFraction * 100f);
            string line = $"Sex ratio: {femalePct}% female / {100 - femalePct}% male";
            float skew = RegionDemographicsStress.CurrentFemaleDelta(province.id);
            if (Mathf.Abs(skew) >= 0.01f)
                line += skew > 0f ? "  (skewed female — men drafted or lost)" : "  (skewed male)";
            return line;
        }

        /// <summary>Turn per-bucket tile counts into shares and a median age, using the region's average
        /// longevity to stretch the elder band. Shared by the region and faction aggregators.</summary>
        private static void FillAgeStructure(RegionDemographics demo, int[] ageCounts, float longevityAcc)
        {
            if (demo.settledTiles <= 0) return;
            for (int i = 0; i < AgeStructureRules.BucketCount; i++)
                demo.ageShares[i] = (float)ageCounts[i] / demo.settledTiles;
            float avgLongevity = longevityAcc / demo.settledTiles;
            demo.medianAge = AgeStructureRules.MedianAge(demo.ageShares, avgLongevity);
        }

        /// <summary>
        /// A faction's overall demographics — every tile it dominates across the world, aggregated into
        /// one make-up. This is the summary the faction info tab shows. Cached per faction.
        /// </summary>
        public static RegionDemographics ForFaction(Faction faction)
        {
            EnsureFresh();
            if (faction == null) return new RegionDemographics();
            if (factionCache.TryGetValue(faction, out RegionDemographics cached)) return cached;

            var demo = AggregateFaction(faction);
            factionCache[faction] = demo;
            return demo;
        }

        private static RegionDemographics AggregateFaction(Faction faction)
        {
            var demo = new RegionDemographics
            {
                biotechActive = ModLister.BiotechInstalled,
                ideologyActive = ModLister.IdeologyInstalled
            };
            var mgr = Find.World?.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null) return demo;

            var raceCounts = new Dictionary<XenotypeDef, int>();
            var wealthByRace = new Dictionary<XenotypeDef, List<int>>();
            var memeCounts = new Dictionary<MemeDef, int>();
            var allWealth = new List<int>();
            var ageCounts = new int[AgeStructureRules.BucketCount];
            float longevityAcc = 0f;
            int female = 0;

            foreach (GeographicProvince p in mgr.Provinces)
            {
                if (p?.tiles == null || p.provinceType != ProvinceType.Land) continue;
                if (!RegionalOwnershipUtility.HoldsTerritory(p, faction)) continue;

                for (int i = 0; i < p.tiles.Count; i++)
                {
                    TileDemographicSample s = SampleTile(p.tiles[i]);
                    if (s.owner != faction) continue;   // only this faction's people

                    demo.tileCount++;
                    demo.settledTiles++;
                    ageCounts[(int)s.ageBucket]++;
                    longevityAcc += LongevityOf(s.race);
                    if (s.sex == Gender.Female) female++;
                    if (s.race != null)
                    {
                        raceCounts.TryGetValue(s.race, out int rc); raceCounts[s.race] = rc + 1;
                        if (!wealthByRace.TryGetValue(s.race, out List<int> wl)) { wl = new List<int>(); wealthByRace[s.race] = wl; }
                        wl.Add(s.wealth);
                    }
                    allWealth.Add(s.wealth);
                    if (s.ideo?.memes != null)
                        foreach (MemeDef m in s.ideo.memes)
                            if (m != null) { memeCounts.TryGetValue(m, out int mc); memeCounts[m] = mc + 1; }
                }
            }

            demo.femaleFraction = demo.tileCount > 0 ? (float)female / demo.tileCount : 0f;
            if (demo.settledTiles > 0)
            {
                demo.factionShares[faction] = 1f;
                foreach (var kv in raceCounts)
                {
                    demo.raceShares[kv.Key] = (float)kv.Value / demo.settledTiles;
                    int[] arr = wealthByRace[kv.Key].ToArray();
                    demo.medianWealthByRace[kv.Key] = DemographicsRules.Median(arr, arr.Length);
                }
                foreach (var kv in memeCounts)
                    demo.memeShares[kv.Key] = (float)kv.Value / demo.settledTiles;
                int[] wealthArr = allWealth.ToArray();
                demo.overallMedianWealth = DemographicsRules.Median(wealthArr, wealthArr.Length);
                FillAgeStructure(demo, ageCounts, longevityAcc);
            }
            return demo;
        }

        // --- pressure sources --------------------------------------------------

        private static List<PressureSource> Sources()
        {
            EnsureFresh();
            if (sources != null) return sources;

            sources = new List<PressureSource>();
            WorldGrid grid = Find.WorldGrid;
            if (Find.WorldObjects != null && grid != null)
            {
                List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
                for (int i = 0; i < all.Count; i++)
                {
                    WorldObject o = all[i];
                    if (o == null || o.Faction == null) continue;
                    if (WorldObjectClassifier.Classify(o) != WorldObjectKind.Settlement) continue;
                    // The pressure field is a SURFACE field: a settlement not on the root surface, or whose
                    // tile is out of the surface range (orbital habitats / space sites on an Odyssey planet),
                    // cannot seed it — its per-layer tileId would later be indexed against the surface grid and
                    // log an out-of-range error (#77).
                    PlanetTile pt = o.Tile;
                    if (!IsSurfaceSampleTile(pt)) continue;
                    int pop = DemographicPopulation(o);
                    if (pop > 0) sources.Add(new PressureSource { tile = pt.tileId, faction = o.Faction, population = pop });
                }
            }
            return sources;
        }

        /// <summary>A settlement's demographic reach = its current modelled population. Tier-driven for
        /// NPCs (drifts toward ⅔ of the cap), live colonists for the player; falls back to the pre-0.8
        /// estimate when tiers are off.</summary>
        private static int DemographicPopulation(WorldObject settlement)
        {
            int target = Sizing.SettlementSizeUtility.TargetPopulationOf(settlement);
            if (target > 0) return target;
            Settlement s = settlement as Settlement;
            return s != null ? PopulationDensityUtility.GetSettlementPopulation(s) : 0;
        }

        /// <summary>
        /// A settlement's demographic pressure at a given crow-flies distance. Reach = population ×
        /// <see cref="WorldObjectIntegrationSettings.demographicReach"/>; pressure falls from the full
        /// population at the centre to 0 at the reach, shaped by
        /// <see cref="WorldObjectIntegrationSettings.demographicFalloff"/> (1 = linear). Tuning these two
        /// is how border regions are dialled to ~50–60% their own make-up.
        /// </summary>
        private static float Pressure(int population, float distanceTiles)
        {
            float reach = population * Mathf.Max(0.01f, WorldObjectIntegrationSettings.demographicReach);
            var model = (DemographicsRules.FalloffModel)WorldObjectIntegrationSettings.demographicFalloffModel;
            return DemographicsRules.Pressure(model, population, distanceTiles, reach, WorldObjectIntegrationSettings.demographicFalloff);
        }

        // --- biome affinity (the second pressure layer) ------------------------

        private class RaceEnv { public float cold; public float heat; public float tox; }
        private static readonly Dictionary<XenotypeDef, RaceEnv> envCache = new Dictionary<XenotypeDef, RaceEnv>();
        private static StatDef tempMinStat, tempMaxStat, toxResStat, toxEnvResStat;
        private static bool envStatsResolved;

        private static void ResolveEnvStats()
        {
            if (envStatsResolved) return;
            envStatsResolved = true;
            tempMinStat = StatDefOf.ComfyTemperatureMin;
            tempMaxStat = StatDefOf.ComfyTemperatureMax;
            toxResStat = DefDatabase<StatDef>.GetNamedSilentFail("ToxicResistance");
            toxEnvResStat = DefDatabase<StatDef>.GetNamedSilentFail("ToxicEnvironmentResistance");
        }

        /// <summary>A race's environmental adaptation, summed from its genes' stat offsets: cold/heat
        /// tolerance (ComfyTemperature min/max) and toxic resistance. Cached; defs are stable.</summary>
        private static RaceEnv EnvOf(XenotypeDef race)
        {
            if (race == null) return null;
            if (envCache.TryGetValue(race, out RaceEnv e)) return e;

            ResolveEnvStats();
            e = new RaceEnv();
            if (race.genes != null)
            {
                foreach (GeneDef g in race.genes)
                {
                    if (g?.statOffsets == null) continue;
                    foreach (StatModifier so in g.statOffsets)
                    {
                        if (so?.stat == null) continue;
                        if (so.stat == tempMinStat) e.cold += -so.value;      // lower comfy-min = cold-adapted
                        else if (so.stat == tempMaxStat) e.heat += so.value;  // higher comfy-max = heat-adapted
                        else if (so.stat == toxResStat || so.stat == toxEnvResStat) e.tox += so.value;
                    }
                }
            }
            envCache[race] = e;
            return e;
        }

        /// <summary>
        /// How much the land itself favours a race, 0.3–2.5 (1 = neutral). A cold-gene race weighs more
        /// in the tundra, a heat-gene race in the desert, a tox-adapted race on polluted ground —
        /// derived from the race's genes, so it works for any modded xenotype. Weights are first-pass
        /// and tunable.
        /// </summary>
        private static float BiomeAffinity(XenotypeDef race, Tile tile)
        {
            if (race == null || tile == null) return 1f;
            RaceEnv env = EnvOf(race);
            if (env == null) return 1f;

            float aff = 1f;
            float temp = tile.temperature;
            if (temp < 5f) aff += env.cold * 0.02f * Mathf.Min(1f, (5f - temp) / 35f);
            if (temp > 25f) aff += env.heat * 0.02f * Mathf.Min(1f, (temp - 25f) / 25f);
            float pollution = SafePollution(tile);
            if (pollution > 0f) aff += env.tox * 0.5f * pollution;

            return Mathf.Clamp(aff, 0.3f, 2.5f);
        }

        private static float SafePollution(Tile tile)
        {
            try { return Mathf.Clamp01(tile.pollution); }
            catch { return 0f; }
        }

        // --- xenotype longevity (the age pyramid's third signal) ---------------

        private static readonly Dictionary<XenotypeDef, float> longevityCache = new Dictionary<XenotypeDef, float>();

        /// <summary>
        /// How long-lived a race is, 0 (mortal baseline) to 1 (effectively ageless). Detected from the
        /// xenotype's genes by name — an Ageless / Deathless / Longevity gene marks a caste that
        /// accumulates elders (e.g. sanguophage-adjacent). Keyword-based so it also catches modded
        /// longevity genes; first-pass and tunable, like the biome-affinity and underclass detectors.
        /// Returns 0 when Biotech is off (null race) so the pyramid degrades to the plain tech baseline.
        /// </summary>
        private static float LongevityOf(XenotypeDef race)
        {
            if (race?.genes == null) return 0f;
            if (longevityCache.TryGetValue(race, out float cached)) return cached;

            float longevity = 0f;
            for (int i = 0; i < race.genes.Count; i++)
            {
                string n = race.genes[i]?.defName;
                if (n == null) continue;
                if (n.IndexOf("Ageless", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Deathless", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Longevity", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    longevity = 1f;   // strongest signal wins; no need to keep scanning
                    break;
                }
            }
            longevityCache[race] = longevity;
            return longevity;
        }

        // Straight-line ("crow flies") great-circle distance between two tiles, in tile-widths.
        private static float CrowTiles(WorldGrid grid, int a, int b)
        {
            if (a == b) return 0f;
            Vector3 va = grid.GetTileCenter(a);
            Vector3 vb = grid.GetTileCenter(b);
            float arc = Vector3.Angle(va, vb) * Mathf.Deg2Rad * va.magnitude;   // surface distance on the planet
            return arc / AvgTileSize(grid);
        }

        private static float AvgTileSize(WorldGrid grid)
        {
            if (avgTileSize > 0f) return avgTileSize;
            var nb = new List<PlanetTile>();
            grid.GetTileNeighbors(0, nb);
            avgTileSize = nb.Count > 0
                ? Mathf.Max(0.0001f, (grid.GetTileCenter(0) - grid.GetTileCenter(nb[0].tileId)).magnitude)
                : 1f;
            return avgTileSize;
        }

        // --- faction profiles --------------------------------------------------

        public static FactionDemographicProfile ProfileFor(Faction faction)
        {
            EnsureFresh();
            if (faction == null) return FactionDemographicProfile.Empty;
            if (profileCache.TryGetValue(faction, out FactionDemographicProfile p)) return p;
            p = FactionDemographicProfile.Build(faction);
            profileCache[faction] = p;
            return p;
        }
    }

    /// <summary>A faction's TARGET demographic — the make-up it broadcasts from each of its settlements.</summary>
    public class FactionDemographicProfile
    {
        public static readonly FactionDemographicProfile Empty = new FactionDemographicProfile
        {
            races = new XenotypeDef[0], raceWeights = new float[0], raceMedianWealth = new int[0], fallbackWealth = 300,
            techLevel = (int)TechLevel.Industrial, natalistSkew = 0f
        };

        public XenotypeDef[] races;
        public float[] raceWeights;
        public int[] raceMedianWealth;
        public int fallbackWealth;
        public Ideo primaryIdeo;
        public int techLevel;        // RimWorld TechLevel ordinal — shapes the base age pyramid (#10)
        public float natalistSkew;   // 0..1, from pro-natalist memes; pushes the pyramid toward children

        public static FactionDemographicProfile Build(Faction faction)
        {
            var p = new FactionDemographicProfile();
            TechLevel tech = faction.def?.techLevel ?? TechLevel.Industrial;
            int baseWealth = BaseWealth(tech);
            p.fallbackWealth = baseWealth;
            p.techLevel = (int)tech;   // seeds the age pyramid (#10): tribal birth-heavy vs spacer flat

            var races = new List<XenotypeDef>();
            var weights = new List<float>();
            Dictionary<XenotypeDef, float> table = XenotypesFor(faction);
            if (table != null)
            {
                foreach (var kv in table)
                {
                    if (kv.Key == null || kv.Value <= 0f) continue;
                    races.Add(kv.Key);
                    weights.Add(kv.Value);
                }
            }

            // A faction with no resolvable xenotype table (no basicMemberKind, etc.) is baseliner — show
            // that explicitly rather than a blank axis.
            if (races.Count == 0 && ModLister.BiotechInstalled && XenotypeDefOf.Baseliner != null)
            {
                races.Add(XenotypeDefOf.Baseliner);
                weights.Add(1f);
            }

            p.races = races.ToArray();
            p.raceWeights = weights.ToArray();
            p.raceMedianWealth = new int[p.races.Length];
            for (int i = 0; i < p.races.Length; i++)
                p.raceMedianWealth[i] = (int)(baseWealth * ClassFactor(p.races[i]));

            if (ModLister.IdeologyInstalled)
            {
                try { p.primaryIdeo = faction.ideos?.PrimaryIdeo; }
                catch { p.primaryIdeo = null; }
            }
            p.natalistSkew = NatalistSkew(p.primaryIdeo);

            return p;
        }

        /// <summary>
        /// How strongly an ideology pushes for children, 0 (neutral) to 1 (pro-natalist), read from its
        /// memes by name — a Natalist / Fertility / Fecund meme skews the pyramid young. Keyword-based so
        /// it catches modded memes too; returns 0 with no Ideology, so the age model degrades to the bare
        /// tech pyramid. First-pass and tunable.
        /// </summary>
        private static float NatalistSkew(Ideo ideo)
        {
            if (ideo?.memes == null) return 0f;
            foreach (MemeDef m in ideo.memes)
            {
                string n = m?.defName;
                if (n == null) continue;
                if (n.IndexOf("Natal", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Fertil", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Fecund", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return 1f;
                }
            }
            return 0f;
        }

        private static int BaseWealth(TechLevel tech)
        {
            switch (tech)
            {
                case TechLevel.Animal:
                case TechLevel.Neolithic: return 120;
                case TechLevel.Medieval: return 250;
                case TechLevel.Industrial: return 500;
                case TechLevel.Spacer: return 1000;
                case TechLevel.Ultra:
                case TechLevel.Archotech: return 2000;
                default: return 400;
            }
        }

        /// <summary>Socioeconomic multiplier on a race's wealth. Engineered-addiction races read as a poor
        /// underclass (their labour is coerced by dependency); everyone else is baseline.</summary>
        private static float ClassFactor(XenotypeDef race)
        {
            return IsAddictionUnderclass(race) ? 0.3f : 1.0f;
        }

        /// <summary>
        /// True when a race carries an engineered chemical dependency (Hussar → go-juice, Waster → tox,
        /// …) — the "slave caste" whose labour is coerced by addiction. Detected by the dependency gene's
        /// <c>geneClass</c> (Gene_ChemicalDependency), which is how Biotech models it — there is no
        /// <c>GeneDef.chemicalDependency</c> field (the earlier reflection on that name found nothing,
        /// which is why the underclass never read poor).
        /// </summary>
        private static bool IsAddictionUnderclass(XenotypeDef race)
        {
            if (race?.genes == null) return false;
            for (int i = 0; i < race.genes.Count; i++)
            {
                GeneDef g = race.genes[i];
                System.Type cls = g?.geneClass;
                if (cls != null && cls.Name.IndexOf("ChemicalDependency", StringComparison.Ordinal) >= 0)
                    return true;
            }
            return false;
        }

        // PawnGenerator.XenotypesAvailableFor(kind, factionDef, faction) : Dictionary<XenotypeDef,float>
        // — the verified vanilla weighted table (spike #35). Reflected for accessibility safety.
        private static MethodInfo xenoAvailMethod;
        private static bool xenoResolved;

        private static Dictionary<XenotypeDef, float> XenotypesFor(Faction faction)
        {
            if (!ModLister.BiotechInstalled) return null;
            // Many factions define their pawns through pawnGroupMakers rather than basicMemberKind;
            // fall back to a generic kind so XenotypesAvailableFor can still read the faction's set.
            PawnKindDef kind = faction.def?.basicMemberKind ?? PawnKindDefOf.Colonist;
            if (kind == null) return null;

            if (!xenoResolved)
            {
                xenoAvailMethod = AccessTools.Method(typeof(PawnGenerator), "XenotypesAvailableFor",
                    new[] { typeof(PawnKindDef), typeof(FactionDef), typeof(Faction) });
                xenoResolved = true;
            }
            if (xenoAvailMethod == null) return null;

            try { return xenoAvailMethod.Invoke(null, new object[] { kind, faction.def, faction }) as Dictionary<XenotypeDef, float>; }
            catch { return null; }
        }
    }
}
