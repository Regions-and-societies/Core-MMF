using System;
using System.Collections.Generic;
using System.Linq;
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
        public AgeBucket ageBucket;         // child / working-age / elder, drawn from the local age pyramid (#10)
        public EducationTier educationTier; // drawn from the local education distribution (#15)
    }

    /// <summary>A region's aggregated makeup, computed from its tile samples. Purely derived.</summary>
    public class RegionDemographics
    {
        public int tileCount;
        public int settledTiles;   // tiles under some settlement's demographic pressure
        public readonly Dictionary<Faction, float> factionShares = new Dictionary<Faction, float>();   // dominant-pressure owner per tile
        public readonly Dictionary<XenotypeDef, float> raceShares = new Dictionary<XenotypeDef, float>();
        public readonly Dictionary<XenotypeDef, int> medianWealthByRace = new Dictionary<XenotypeDef, int>();
        // Def-less xenotypes — a player custom xenotype (in-game editor) or one whose defining mod was
        // removed — keyed by name, since they have no XenotypeDef. Only OverlayCohorts fills these (from the
        // living cohorts); the derived tile path has defs only, so they stay empty there and in VerifyCulling.
        // Surfaced alongside raceShares in the panel and reports (#58 combine-the-lists custom-xenotype support).
        public readonly Dictionary<string, float> customRaceShares = new Dictionary<string, float>();
        public readonly Dictionary<string, int> customMedianWealthByName = new Dictionary<string, int>();
        public readonly Dictionary<MemeDef, float> memeShares = new Dictionary<MemeDef, float>();
        // Ideology structure (#13): the share of settled tiles under each ideo (primary + minor), the
        // deepening of the meme layer into an ethnicity-style breakdown. Empty with Ideology off.
        public readonly Dictionary<Ideo, float> ideoShares = new Dictionary<Ideo, float>();
        public float femaleFraction;
        public int overallMedianWealth;
        // Age structure (#10): the share of settled tiles in each bucket, and the resulting median age.
        // Indexed by (int)AgeBucket — [child, working-age, elder]. All zero for an unsettled region.
        public readonly float[] ageShares = new float[AgeStructureRules.BucketCount];
        public int medianAge;
        // Education structure (#15): the share of settled tiles in each tier, indexed by (int)EducationTier
        // [illiterate, basic, skilled, advanced], plus the collapsed 0-100 attainment index.
        public readonly float[] educationShares = new float[EducationRules.TierCount];
        public int educationIndex;
        // Socioeconomic structure (#14): the share of settled tiles in each SES tier, indexed by
        // (int)SesTier [subsistence, modest, prosperous, affluent], plus the collapsed 0-100 index.
        public readonly float[] sesShares = new float[SocioeconomicRules.TierCount];
        public int sesIndex;
        // Employment structure (#16): the workforce split across occupation sectors, indexed by
        // (int)OccupationSector [agriculture, industry, military, trade], plus the 0-100 employment rate.
        public readonly float[] occupationShares = new float[EmploymentRules.SectorCount];
        public int employmentRate;
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
        private const int EduSalt = 29;
        private const int IdeoPickSalt = 31;   // which of a faction's ideos (primary/minor) a tile follows

        /// <summary>#79: a wilderness (Frontier) tile's people are the owning society's, but poorer — the
        /// countryside is subsistence. Their wealth is the faction's median at this discount.</summary>
        private const float RuralWealthFactor = 0.7f;

        private static readonly Dictionary<Faction, FactionDemographicProfile> profileCache = new Dictionary<Faction, FactionDemographicProfile>();
        private static readonly Dictionary<int, RegionDemographics> regionCache = new Dictionary<int, RegionDemographics>();
        private static readonly Dictionary<Faction, RegionDemographics> factionCache = new Dictionary<Faction, RegionDemographics>();
        // #79: the ambient (owning) faction whose people fill a province's wilderness, by province id.
        // Derived from the province's stable ownership, not the cullable source list, so it is identical
        // under full and culled aggregation — VerifyCulling stays result-identical.
        private static readonly Dictionary<int, Faction> ambientCache = new Dictionary<int, Faction>();
        // #33: per-region density stats (its densest tile's population + population-weighted mean urbanity),
        // so a per-tile local demographic read is O(1) after the region is first touched.
        private static readonly Dictionary<int, RegionUrbanity> urbanityCache = new Dictionary<int, RegionUrbanity>();
        private static List<PressureSource> sources;
        private static int cacheVersion = -1;
        private static float avgTileSize = -1f;

        private struct RegionUrbanity { public float maxTilePop; public float meanUrbanity; }

        /// <summary>#33: one tile's location-based demographics — the region aggregate skewed by how densely
        /// that tile is settled (denser = younger, better educated, wealthier, more employed). The
        /// population-weighted mean of these over a region equals the region aggregate.</summary>
        public struct LocalDemographicSample
        {
            public int medianAge;
            public int educationIndex;
            public int overallWealth;
            public int employmentRate;
            public float urbanity;       // 0 rural .. 1 the region's densest tile
            public int localPopulation;  // dwellings modelled on this tile
        }

        public static void InvalidateCache()
        {
            profileCache.Clear();
            regionCache.Clear();
            factionCache.Clear();
            ambientCache.Clear();
            urbanityCache.Clear();
            sources = null;
            cacheVersion = -1;
        }

        /// <summary>Drop only the aggregated region cache — used when a stress override changes.</summary>
        public static void InvalidateRegionCache()
        {
            regionCache.Clear();
            ambientCache.Clear();
            urbanityCache.Clear();
        }

        private static void EnsureFresh()
        {
            int v = PopulationDensityUtility.CacheVersion;
            if (v != cacheVersion)
            {
                profileCache.Clear();
                regionCache.Clear();
                factionCache.Clear();
                ambientCache.Clear();
                urbanityCache.Clear();
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
            public float reach;   // #70: sqrt-of-population influence radius in tiles; Pressure() is 0 beyond it
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
        /// reaching it, then a fixed draw from that blend by the tile seed. When no settlement reaches
        /// the tile it is wilderness (#79) — filled from the province's owning society, shaped by the land.</summary>
        public static TileDemographicSample SampleTile(int tileId) => SampleTile(tileId, Sources(), AmbientForTile(tileId));

        /// <summary>As <see cref="SampleTile(int)"/>, but over a caller-supplied source list — the region
        /// aggregation passes the sources pre-culled to those that can actually reach the region, turning
        /// the per-tile pressure loop from O(all settlements) into O(nearby settlements). Since Pressure is
        /// exactly 0 beyond a source's reach, culling changes no result; it only skips zero-contribution work.</summary>
        private static TileDemographicSample SampleTile(int tileId, List<PressureSource> srcs, Faction ambient)
        {
            var sample = new TileDemographicSample();

            uint sexState = DemographicsRules.TileSeed(WorldSeed, tileId, SexSalt);
            sample.sex = DemographicsRules.NextFloat(ref sexState) < 0.5f ? Gender.Female : Gender.Male;

            // A demographic sample is a SURFACE phenomenon. An off-surface / out-of-range tileId — e.g. an
            // orbital or otherwise non-surface origin handed in by pawn generation on an Odyssey planet with
            // extra PlanetLayers — must never reach the surface grid: GetTileCenter (via CrowTiles below)
            // logs "out of range (count: <surface>)" once per call and returns zero, spamming the log (#77).
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tileId < 0 || tileId >= grid.TilesCount) return sample;
            Tile tile = grid[tileId];   // the land itself pushes on race (biome affinity), below
            if (tile == null) return sample;

            // No settlement pressure list at all: pure wilderness (#79). If the land is claimed, its
            // people are the owning society's, shaped by the land; unclaimed frontier stays a bare sex.
            if (srcs == null || srcs.Count == 0)
                return ambient != null ? SampleWilderness(sample, tileId, tile, ambient) : sample;

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

            // No settlement reaches this tile — it is wilderness (#79). Claimed land takes the owning
            // society's make-up shaped by the land; unclaimed no-man's-land stays a bare sex.
            if (pf.Count == 0)
                return ambient != null ? SampleWilderness(sample, tileId, tile, ambient) : sample;
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
            // Ideology (#13): draw among the faction's ideos — its primary plus any minors — rather than
            // always its primary, so a region reads as a belief mix. Own salt, independent of the draws
            // above and of the faction pick.
            sample.ideo = PickIdeo(tileId, ageProf);

            // Age bucket: draw from the faction's tech/ideology pyramid, bent by the chosen race's
            // longevity (a long-lived caste holds more elders). Independent salt so it doesn't correlate
            // with the race/wealth/sex/ideo draws.
            float[] agePyramid = AgeStructureRules.Pyramid(ageProf.techLevel, ageProf.natalistSkew, LongevityOf(chosen));
            uint ageState = DemographicsRules.TileSeed(WorldSeed, tileId, AgeSalt);
            int agePick = DemographicsRules.WeightedPick(ref ageState, agePyramid);
            sample.ageBucket = agePick >= 0 ? (AgeBucket)agePick : AgeBucket.WorkingAge;

            // Education tier: draw from the faction's tech/ideology distribution, raised by the chosen
            // race's engineered intellect. Its own salt so it doesn't correlate with the other draws.
            float[] eduDist = EducationRules.Pyramid(ageProf.techLevel, ageProf.researchSkew, AptitudeOf(chosen));
            uint eduState = DemographicsRules.TileSeed(WorldSeed, tileId, EduSalt);
            int eduPick = DemographicsRules.WeightedPick(ref eduState, eduDist);
            sample.educationTier = eduPick >= 0 ? (EducationTier)eduPick : EducationTier.Primary;
            return sample;
        }

        /// <summary>
        /// The people of a wilderness (Frontier) tile (#79) — one with a base population but no settlement
        /// reaching it. Their make-up is the owning society's, shaped by the land, at a rural skew: the
        /// countryside of a pigskin nation is pigskins, poorer than its towns. Drawn deterministically from
        /// the same tile seed and salts as a settled tile, so the field is stable across reloads and free.
        /// </summary>
        private static TileDemographicSample SampleWilderness(TileDemographicSample sample, int tileId, Tile tile, Faction ambient)
        {
            FactionDemographicProfile prof = ProfileFor(ambient);
            sample.owner = ambient;

            // Race: the faction's races, weighted by the land (biome affinity); else the plain-human bucket.
            var raceWeight = new Dictionary<XenotypeDef, float>();
            float wsum = 0f;
            for (int r = 0; r < prof.raceWeights.Length; r++) wsum += prof.raceWeights[r];
            if (prof.races.Length > 0 && wsum > 0f)
            {
                for (int r = 0; r < prof.races.Length; r++)
                {
                    XenotypeDef race = prof.races[r];
                    float contrib = (prof.raceWeights[r] / wsum) * BiomeAffinity(race, tile);
                    raceWeight.TryGetValue(race, out float rw); raceWeight[race] = rw + contrib;
                }
            }

            XenotypeDef chosen = null;
            if (raceWeight.Count > 0)
            {
                var races = new List<XenotypeDef>(raceWeight.Keys);
                races.Sort((a, b) => string.CompareOrdinal(a.defName, b.defName));   // stable across machines
                var weights = new float[races.Count];
                for (int i = 0; i < races.Count; i++) weights[i] = raceWeight[races[i]];
                uint raceState = DemographicsRules.TileSeed(WorldSeed, tileId, RaceSalt);
                int pick = DemographicsRules.WeightedPick(ref raceState, weights);
                if (pick >= 0 && pick < races.Count) chosen = races[pick];
            }
            sample.race = chosen;

            // Wealth: the race's (or fallback) median, at a rural discount — the countryside is subsistence.
            double baseWealth = prof.fallbackWealth;
            if (chosen != null)
            {
                for (int r = 0; r < prof.races.Length; r++)
                    if (prof.races[r] == chosen) { baseWealth = prof.raceMedianWealth[r]; break; }
            }
            baseWealth *= RuralWealthFactor;
            uint wealthState = DemographicsRules.TileSeed(WorldSeed, tileId, WealthSalt);
            sample.wealth = DemographicsRules.RangeInt(ref wealthState, (int)(baseWealth * 0.6), (int)(baseWealth * 1.4));

            // Ideology, age, education: from the owning faction, same salts and pyramids as a settled tile.
            sample.ideo = PickIdeo(tileId, prof);
            float[] agePyramid = AgeStructureRules.Pyramid(prof.techLevel, prof.natalistSkew, LongevityOf(chosen));
            uint ageState = DemographicsRules.TileSeed(WorldSeed, tileId, AgeSalt);
            int agePick = DemographicsRules.WeightedPick(ref ageState, agePyramid);
            sample.ageBucket = agePick >= 0 ? (AgeBucket)agePick : AgeBucket.WorkingAge;

            float[] eduDist = EducationRules.Pyramid(prof.techLevel, prof.researchSkew, AptitudeOf(chosen));
            uint eduState = DemographicsRules.TileSeed(WorldSeed, tileId, EduSalt);
            int eduPick = DemographicsRules.WeightedPick(ref eduState, eduDist);
            sample.educationTier = eduPick >= 0 ? (EducationTier)eduPick : EducationTier.Primary;
            return sample;
        }

        /// <summary>The society whose people fill a province's wilderness: its first listed owner that
        /// still exists (#79). Null for unclaimed land, which stays empty no-man's-land. Cached per
        /// province and derived from the province's stable ownership — not the cullable source list — so
        /// full and culled aggregation see the same ambient and <see cref="VerifyCulling"/> holds.</summary>
        private static Faction AmbientFactionFor(GeographicProvince province)
        {
            if (province == null) return null;
            if (ambientCache.TryGetValue(province.id, out Faction cached)) return cached;
            Faction f = ResolveOwner(province);
            ambientCache[province.id] = f;
            return f;
        }

        private static Faction ResolveOwner(GeographicProvince province)
        {
            if (province?.owningFactionIds == null || province.owningFactionIds.Count == 0) return null;
            var fm = Find.FactionManager;
            if (fm == null) return null;
            List<Faction> all = fm.AllFactionsListForReading;
            for (int i = 0; i < province.owningFactionIds.Count; i++)
            {
                string id = province.owningFactionIds[i];
                for (int j = 0; j < all.Count; j++)
                    if (all[j] != null && all[j].GetUniqueLoadID() == id) return all[j];
            }
            return null;
        }

        private static Faction AmbientForTile(int tileId)
        {
            var mgr = Find.World?.GetComponent<SynapseRegionManager>();
            GeographicProvince prov = mgr?.GetProvinceForTile(tileId);
            return AmbientFactionFor(prov);
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

        /// <summary>Which of a faction's ideos a tile follows: a deterministic weighted draw over its
        /// primary (heavy) and minor (lighter) ideos, so regions read as a belief mix rather than a
        /// single monoculture. Falls back to the primary when there is nothing to weight.</summary>
        private static Ideo PickIdeo(int tileId, FactionDemographicProfile prof)
        {
            if (prof?.ideos == null || prof.ideos.Length == 0) return prof?.primaryIdeo;
            uint state = DemographicsRules.TileSeed(WorldSeed, tileId, IdeoPickSalt);
            int pick = DemographicsRules.WeightedPick(ref state, prof.ideoWeights);
            return pick >= 0 ? prof.ideos[pick] : prof.primaryIdeo;
        }

        /// <summary>The aggregated makeup of a region, cached. Recomputed when populations/objects change.</summary>
        public static RegionDemographics ForRegion(GeographicProvince province)
        {
            // #53: Societies off — no demographics are modelled; every region reads empty.
            if (!RegionsAndSocietiesMod.SocietiesEnabled) return new RegionDemographics();
            EnsureFresh();
            if (province?.tiles == null || province.tiles.Count == 0) return new RegionDemographics();
            // #51: a small region kept only via the "enable small regions" option earns no regional
            // benefits — it is too small to sustain a society, so it reads as empty demographics.
            if (province.benefitsSuppressed) return new RegionDemographics();
            // Only land has demographics. Skipping water avoids aggregating the (now real, ~50k-tile)
            // ocean province — an O(tiles × settlement sources) walk that would freeze on first read and
            // report a fabricated ocean population/age/wealth (#20).
            if (province.provinceType != ProvinceType.Land) return new RegionDemographics();
            if (regionCache.TryGetValue(province.id, out RegionDemographics cached)) return cached;

            var demo = Aggregate(province);
            OverlayCohorts(demo, province);                      // #58: living cohorts are the source of truth for the people-axes
            RegionDemographicsStress.Apply(province.id, demo);   // sparse overrides on top of the baseline
            regionCache[province.id] = demo;
            return demo;
        }

        /// <summary>
        /// #33: one tile's LOCATION-BASED demographics — the region's (population-weighted) aggregate skewed
        /// by how densely this particular tile is settled. A city tile reads younger, better educated,
        /// wealthier and more employed than its region's rural fringe; the population-weighted mean of all a
        /// region's tiles reproduces the region aggregate (see <see cref="LocationalDemographicsRules"/>), so
        /// the per-tile read and the region overlay never disagree. Returns the plain region aggregate when
        /// the region is unsettled or Societies is off.
        /// </summary>
        public static LocalDemographicSample LocalDemographics(int tileId)
        {
            var mgr = Find.World?.GetComponent<SynapseRegionManager>();
            GeographicProvince province = mgr?.GetProvinceForTile(tileId);
            RegionDemographics demo = ForRegion(province);
            var sample = new LocalDemographicSample
            {
                medianAge = demo.medianAge,
                educationIndex = demo.educationIndex,
                overallWealth = demo.overallMedianWealth,
                employmentRate = demo.employmentRate,
                urbanity = 0f,
                localPopulation = PopulationDensityUtility.GetSourcePopulationAtTile(tileId),
            };
            if (province == null || demo.settledTiles <= 0) return sample;   // no gradient to apply

            RegionUrbanity u = UrbanityStats(province);
            float ur = LocationalDemographicsRules.Urbanity(sample.localPopulation, u.maxTilePop);
            sample.urbanity = ur;
            sample.medianAge = LocationalDemographicsRules.LocalMedianAge(demo.medianAge, ur, u.meanUrbanity);
            sample.educationIndex = LocationalDemographicsRules.LocalEducationIndex(demo.educationIndex, ur, u.meanUrbanity);
            sample.overallWealth = LocationalDemographicsRules.LocalWealth(demo.overallMedianWealth, ur, u.meanUrbanity);
            sample.employmentRate = LocationalDemographicsRules.LocalEmploymentRate(demo.employmentRate, ur, u.meanUrbanity);
            return sample;
        }

        /// <summary>A region's density stats for the location gradient (#33): its densest tile's population
        /// and the population-weighted mean urbanity, computed once per region and cached with the demographic
        /// caches. Reads the per-tile source-population field (<see cref="PopulationDensityUtility"/>).</summary>
        private static RegionUrbanity UrbanityStats(GeographicProvince province)
        {
            if (urbanityCache.TryGetValue(province.id, out RegionUrbanity cached)) return cached;

            List<int> tiles = province.tiles;
            float max = 0f;
            for (int i = 0; i < tiles.Count; i++)
            {
                float pop = PopulationDensityUtility.GetSourcePopulationAtTile(tiles[i]);
                if (pop > max) max = pop;
            }
            float wsum = 0f, psum = 0f;
            for (int i = 0; i < tiles.Count; i++)
            {
                float pop = PopulationDensityUtility.GetSourcePopulationAtTile(tiles[i]);
                if (pop <= 0f) continue;
                wsum += pop * LocationalDemographicsRules.Urbanity(pop, max);
                psum += pop;
            }
            var u = new RegionUrbanity { maxTilePop = max, meanUrbanity = psum > 0f ? wsum / psum : 0f };
            urbanityCache[province.id] = u;
            return u;
        }

        /// <summary>
        /// Precompute and cache the aggregate for EVERY land region in one pass, so no demographic
        /// overlay ever pays the cold O(tiles × sources) aggregation on the interactive frame it is
        /// opened. Called once when a world is finalized (new game or load) — the cost then lands on the
        /// loading screen instead of freezing the first overlay. Cheap to re-call (all cache hits until
        /// the population cache version changes); a later settlement change re-warms only what it touched
        /// on next read. Water/impassable provinces are skipped (they have no demographics).
        /// </summary>
        public static void WarmAllRegions(SynapseRegionManager manager)
        {
            if (!RegionsAndSocietiesMod.SocietiesEnabled) return;   // #53: nothing to warm when Societies is off
            if (manager == null) return;
            var provinces = manager.Provinces;
            if (provinces == null) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int warmed = 0;
            for (int i = 0; i < provinces.Count; i++)
            {
                GeographicProvince p = provinces[i];
                if (p != null && p.provinceType == ProvinceType.Land) { ForRegion(p); warmed++; }
            }
            sw.Stop();
            Log.Message($"[RegionsAndSocieties] Warmed demographics for {warmed} land region(s) in {sw.ElapsedMilliseconds} ms.");
        }

        private static RegionDemographics Aggregate(GeographicProvince province)
            => AggregateWith(province, RelevantSources(province, Sources()));

        /// <summary>Aggregate a region over an explicit pressure-source list — the region's culled subset in
        /// normal use, or the full list for the culling self-check (<see cref="VerifyCulling"/>).</summary>
        private static RegionDemographics AggregateWith(GeographicProvince province, List<PressureSource> srcs)
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
            var ideoCounts = new Dictionary<Ideo, int>();
            var allWealth = new List<int>();
            var ageCounts = new int[AgeStructureRules.BucketCount];
            var eduCounts = new int[EducationRules.TierCount];
            float longevityAcc = 0f;
            int female = 0;

            // #79: the society that fills this province's wilderness — computed once, from the province's
            // stable ownership (not the cullable srcs), so full and culled aggregation agree.
            Faction ambient = AmbientFactionFor(province);

            List<int> tiles = province.tiles;
            demo.tileCount = tiles.Count;
            for (int i = 0; i < tiles.Count; i++)
            {
                TileDemographicSample s = SampleTile(tiles[i], srcs, ambient);
                if (s.sex == Gender.Female) female++;
                if (s.owner == null) continue;   // no pressure here — contributes only to the sex ratio

                demo.settledTiles++;
                ageCounts[(int)s.ageBucket]++;
                eduCounts[(int)s.educationTier]++;
                longevityAcc += LongevityOf(s.race);
                factionCounts.TryGetValue(s.owner, out int fc); factionCounts[s.owner] = fc + 1;
                if (s.race != null)
                {
                    raceCounts.TryGetValue(s.race, out int rc); raceCounts[s.race] = rc + 1;
                    if (!wealthByRace.TryGetValue(s.race, out List<int> wl)) { wl = new List<int>(); wealthByRace[s.race] = wl; }
                    wl.Add(s.wealth);
                }
                allWealth.Add(s.wealth);

                if (s.ideo != null)
                {
                    ideoCounts.TryGetValue(s.ideo, out int ic); ideoCounts[s.ideo] = ic + 1;
                    if (s.ideo.memes != null)
                        foreach (MemeDef m in s.ideo.memes)
                            if (m != null) { memeCounts.TryGetValue(m, out int mc); memeCounts[m] = mc + 1; }
                }
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
                foreach (var kv in ideoCounts)
                    demo.ideoShares[kv.Key] = (float)kv.Value / demo.settledTiles;

                int[] wealthArr = allWealth.ToArray();
                demo.overallMedianWealth = DemographicsRules.Median(wealthArr, wealthArr.Length);

                FillAgeStructure(demo, ageCounts, longevityAcc);
                FillEducation(demo, eduCounts);
                FillSes(demo, allWealth, RegionWealthMultiplier(province));
                FillEmployment(demo, province);
            }

            return demo;
        }

        /// <summary>
        /// #58 aggregation replace: once a region's per-xenotype cohorts are seeded and evolving, THEY — not
        /// the per-tile derived draw — are the <b>source of truth</b> for the region's PEOPLE axes. This
        /// rewrites the freshly derived baseline's race, age, education, sex and per-race wealth from the
        /// cohorts (population-weighted), so the overlays and panels show the living population as it drifts:
        /// a germline caste grows its share, a drug-dependent one fades, elders accumulate where lifespans
        /// are long. The PLACE-derived axes (ideology, meme, faction, occupation, overall wealth/SES) are
        /// left untouched — the cohort model does not track them (§13/§16 stay derived, per the design lock).
        ///
        /// <para>The per-tile aggregate remains the cold-start seed (the cohorts were built from it) and the
        /// fallback for a region not yet ticked. Applied in <see cref="ForRegion"/> ONLY, after
        /// <see cref="Aggregate"/> and before the stress layer — so <see cref="VerifyCulling"/> (which
        /// compares two <see cref="AggregateWith"/> runs) never sees it and stays result-identical, and a
        /// region with no seeded cohorts reads exactly the pre-cohort numbers (the no-Biotech regression).</para>
        /// </summary>
        private static void OverlayCohorts(RegionDemographics demo, GeographicProvince province)
        {
            if (demo == null || demo.settledTiles <= 0) return;   // only refine a region that already reads populated
            RegionCohorts rc = province?.cohorts;
            if (rc == null || !rc.seeded || rc.cohorts == null || rc.cohorts.Count == 0) return;
            float total = rc.TotalPopulation;
            if (total <= 0f) return;

            var racePop = new Dictionary<XenotypeDef, float>();
            var raceWealthAcc = new Dictionary<XenotypeDef, double>();   // pop-weighted 0..1 wealth level, per race
            var customPop = new Dictionary<string, float>();            // def-less xenotypes (custom / removed-mod), by name
            var customWealthAcc = new Dictionary<string, double>();
            float ageChild = 0f, ageWorking = 0f, ageElder = 0f;
            var edu = new float[EducationRules.TierCount];
            float female = 0f, longevityAcc = 0f;
            bool biotech = demo.biotechActive;

            for (int i = 0; i < rc.cohorts.Count; i++)
            {
                RegionCohort c = rc.cohorts[i];
                if (c?.state == null) continue;
                float pop = c.state.pop;
                if (pop <= 0f) continue;

                if (biotech)
                {
                    ClassifyCohortRace(c.xenoDefName, out XenotypeDef xeno, out string customName);
                    if (xeno != null)
                    {
                        racePop.TryGetValue(xeno, out float rp); racePop[xeno] = rp + pop;
                        raceWealthAcc.TryGetValue(xeno, out double wa); raceWealthAcc[xeno] = wa + pop * c.state.wealthLevel;
                    }
                    else if (customName != null)
                    {
                        customPop.TryGetValue(customName, out float cp); customPop[customName] = cp + pop;
                        customWealthAcc.TryGetValue(customName, out double cwa); customWealthAcc[customName] = cwa + pop * c.state.wealthLevel;
                    }
                }

                ageChild += pop * c.state.ageChild;
                ageWorking += pop * c.state.ageWorking;
                ageElder += pop * c.state.ageElder;
                float[] ce = c.state.education;
                if (ce != null)
                    for (int t = 0; t < edu.Length && t < ce.Length; t++) edu[t] += pop * ce[t];
                female += pop * c.state.femaleFraction;
                longevityAcc += pop * LongevitySkew(c.state.lifespan);
            }

            // The per-year derived fields (age, education, sex, wealth level) are NOT scribed — only the
            // cohorts' intrinsics and populations survive a save. So after a load a region's cohorts carry
            // headcounts but zeroed derived fields until the next demographic-year tick re-steps them. A
            // present age pyramid is the "has been stepped this session" signal: without it, keep the whole
            // coherent derived baseline rather than overwriting axes with stale zeros (which would read as an
            // all-male, ageless, uneducated region for up to a year after load).
            float ageSum = ageChild + ageWorking + ageElder;
            if (ageSum <= 0f) return;

            // --- age structure (#10): pop-weighted cohort pyramids, median stretched by cohort longevity ---
            demo.ageShares[(int)AgeBucket.Child] = ageChild / ageSum;
            demo.ageShares[(int)AgeBucket.WorkingAge] = ageWorking / ageSum;
            demo.ageShares[(int)AgeBucket.Elder] = ageElder / ageSum;
            demo.medianAge = AgeStructureRules.MedianAge(demo.ageShares, longevityAcc / total);

            // --- education (#15): pop-weighted cohort distributions ---
            float eduSum = 0f; for (int t = 0; t < edu.Length; t++) eduSum += edu[t];
            if (eduSum > 0f)
            {
                for (int t = 0; t < EducationRules.TierCount; t++) demo.educationShares[t] = edu[t] / eduSum;
                demo.educationIndex = EducationRules.Index(demo.educationShares);
            }

            // --- sex ratio (#11): pop-weighted cohort female fraction (stress adds its skew on top, later) ---
            demo.femaleFraction = Mathf.Clamp01(female / total);

            // --- xenotypes (#12) + per-race wealth (#14). Biotech only; the region's overall wealth stays the
            // derived regional anchor, per-race relative wealth comes from the cohort's evolved wealth level. ---
            if (biotech && (racePop.Count > 0 || customPop.Count > 0))
            {
                demo.raceShares.Clear();
                demo.medianWealthByRace.Clear();
                demo.customRaceShares.Clear();
                demo.customMedianWealthByName.Clear();
                int anchor = demo.overallMedianWealth > 0 ? demo.overallMedianWealth : 300;
                foreach (var kv in racePop)
                {
                    demo.raceShares[kv.Key] = kv.Value / total;
                    double wl = raceWealthAcc[kv.Key] / kv.Value;                 // avg 0..1 wealth level for the race
                    demo.medianWealthByRace[kv.Key] = (int)System.Math.Round(anchor * (0.4 + 1.2 * wl));
                }
                foreach (var kv in customPop)
                {
                    demo.customRaceShares[kv.Key] = kv.Value / total;
                    double wl = customWealthAcc[kv.Key] / kv.Value;
                    demo.customMedianWealthByName[kv.Key] = (int)System.Math.Round(anchor * (0.4 + 1.2 * wl));
                }
            }
        }

        /// <summary>Classify a cohort's <c>xenoDefName</c> for the race axis. Three cases (Biotech only):
        /// an empty name is the baseliner / hybrid / "other" bucket (→ Baseliner def); a name that resolves
        /// is that xenotype (→ <paramref name="def"/>); a non-empty name that does NOT resolve is a def-less
        /// xenotype — a player custom xenotype or one whose mod was removed — surfaced by name
        /// (→ <paramref name="customName"/>) rather than silently folded into Baseliner. Both out-params are
        /// null when Biotech is off, so the race axis stays empty exactly as the derived path leaves it.</summary>
        private static void ClassifyCohortRace(string xenoDefName, out XenotypeDef def, out string customName)
        {
            def = null; customName = null;
            if (!ModLister.BiotechInstalled) return;
            if (string.IsNullOrEmpty(xenoDefName)) { def = XenotypeDefOf.Baseliner; return; }
            XenotypeDef d = DefDatabase<XenotypeDef>.GetNamedSilentFail(xenoDefName);
            if (d != null) { def = d; return; }
            customName = xenoDefName;
        }

        /// <summary>Map a cohort's intrinsic lifespan to the 0..1 elder-band stretch the age median uses:
        /// a human (~80) reads 0, a longevity/ageless caste reads 1.</summary>
        private static float LongevitySkew(float lifespan)
        {
            float t = (lifespan - CohortFactory.BaselineLifespan) / (CohortFactory.LongevityLifespan - CohortFactory.BaselineLifespan);
            return t < 0f ? 0f : (t > 1f ? 1f : t);
        }

        /// <summary>
        /// Classify the region's per-tile wealth into SES tiers and collapse to a 0-100 index (#14). The
        /// per-tile wealth already carries faction tech level and settlement size; <paramref name="multiplier"/>
        /// folds in the region-level signals (resource richness, trade access) before classifying, so a
        /// rich or well-connected region reads a tier higher. Shared by both aggregators (faction passes 1).
        /// </summary>
        private static void FillSes(RegionDemographics demo, List<int> wealth, float multiplier)
        {
            if (demo.settledTiles <= 0 || wealth == null || wealth.Count == 0) return;
            var counts = new int[SocioeconomicRules.TierCount];
            for (int i = 0; i < wealth.Count; i++)
                counts[(int)SocioeconomicRules.TierFor((int)(wealth[i] * multiplier))]++;
            for (int t = 0; t < SocioeconomicRules.TierCount; t++)
                demo.sesShares[t] = (float)counts[t] / wealth.Count;
            demo.sesIndex = SocioeconomicRules.Index(demo.sesShares);
        }

        // --- region-level SES signals (#14) ------------------------------------

        // Per-tile resource ceiling (nutrition + biomass + minerals) that reads as "neutral" richness.
        private const float RichnessReferenceCapPerTile = 700f;

        /// <summary>The region wealth multiplier from the two region-level #14 signals: how rich the
        /// terrain is and whether trade roads reach it. Neutral (1.0) baseline; a rich, well-connected
        /// region reads wealthier, a barren isolated one poorer.</summary>
        private static float RegionWealthMultiplier(GeographicProvince province)
        {
            return ResourceRichness(province) * (1f + 0.2f * TradeAccess(province));
        }

        /// <summary>Terrain richness as a wealth multiplier ~0.7..1.4, from the region's resource
        /// ceilings per tile (nutrition + biomass + minerals). Neutral until the region's economy has
        /// been assessed, so a demographic read never forces economic initialisation as a side effect.</summary>
        private static float ResourceRichness(GeographicProvince province)
        {
            if (province == null || !province.initializedEconomics) return 1f;
            int tiles = province.tiles != null ? province.tiles.Count : 0;
            if (tiles <= 0) return 1f;
            float perTile = (province.CapOf(Economy.ResourceKind.Nutrition)
                + province.CapOf(Economy.ResourceKind.Biomass)
                + province.CapOf(Economy.ResourceKind.Minerals)) / tiles;
            return Mathf.Clamp(perTile / RichnessReferenceCapPerTile, 0.7f, 1.4f);
        }

        /// <summary>Trade-road access, 0..1: the share of the region's tiles a road passes through.
        /// Connected regions trade richer, so this feeds a small wealth boost. Reads the grid only, so
        /// it is safe on any world and needs no economic initialisation.</summary>
        private static float TradeAccess(GeographicProvince province)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || province?.tiles == null || province.tiles.Count == 0) return 0f;
            List<int> tiles = province.tiles;
            int roaded = 0;
            for (int i = 0; i < tiles.Count; i++)
            {
                int t = tiles[i];
                if (t < 0 || t >= grid.TilesCount) continue;
                var roads = grid[t].Roads;
                if (roads != null && roads.Count > 0) roaded++;
            }
            return (float)roaded / tiles.Count;
        }

        // --- employment (#16) --------------------------------------------------

        /// <summary>
        /// Compute a region's occupation-sector mix and employment rate (#16) from region-level facts:
        /// its dominant faction's tech level, the mix of world objects on it (a military base pulls to
        /// the military sector, extraction outposts/camps to industry, cities to trade), and what its
        /// terrain supports (fertile land to agriculture, mineral-rich to industry).
        /// </summary>
        private static void FillEmployment(RegionDemographics demo, GeographicProvince province)
        {
            if (demo.settledTiles <= 0 || province == null) return;
            int tech = DominantFactionTech(demo);

            CountWorldObjects(province, out int settlements, out int outposts, out int camps, out int military);
            int total = settlements + outposts + camps + military;

            int tiles = province.tiles != null ? province.tiles.Count : 0;
            float agTerrain = 0f, indTerrain = 0f;
            if (province.initializedEconomics && tiles > 0)
            {
                agTerrain = Mathf.Clamp(province.CapOf(Economy.ResourceKind.Nutrition) / tiles / 500f, 0f, 1.5f);
                indTerrain = Mathf.Clamp(province.CapOf(Economy.ResourceKind.Minerals) / tiles / 400f, 0f, 1.5f);
            }

            float milFrac = total > 0 ? (float)military / total : 0f;
            float setFrac = total > 0 ? (float)settlements / total : 0f;
            float extractFrac = total > 0 ? (float)(outposts + camps) / total : 0f;
            float road = TradeAccess(province);

            float agSignal = agTerrain;
            float indSignal = indTerrain + 0.7f * extractFrac;
            float milSignal = milFrac;
            float tradeSignal = 0.6f * setFrac + road;
            float development = tiles > 0 ? Mathf.Clamp01((float)total / tiles * 4f) : 0f;

            float[] shares = EmploymentRules.SectorShares(tech, agSignal, indSignal, milSignal, tradeSignal);
            for (int i = 0; i < EmploymentRules.SectorCount; i++) demo.occupationShares[i] = shares[i];
            demo.employmentRate = EmploymentRules.EmploymentRate(tech, development);
        }

        /// <summary>Faction-wide employment: no single region's terrain or object mix, so just the tech
        /// baseline for the faction. Keeps the faction info readout populated (#16).</summary>
        private static void FillEmploymentForFaction(RegionDemographics demo, Faction faction)
        {
            if (demo.settledTiles <= 0) return;
            int tech = (int)(faction?.def?.techLevel ?? TechLevel.Industrial);
            float[] shares = EmploymentRules.SectorShares(tech, 0f, 0f, 0f, 0f);
            for (int i = 0; i < EmploymentRules.SectorCount; i++) demo.occupationShares[i] = shares[i];
            demo.employmentRate = EmploymentRules.EmploymentRate(tech, 0f);
        }

        private static int DominantFactionTech(RegionDemographics demo)
        {
            Faction best = null;
            float share = 0f;
            foreach (var kv in demo.factionShares)
                if (kv.Value > share) { share = kv.Value; best = kv.Key; }
            return (int)(best?.def?.techLevel ?? TechLevel.Industrial);
        }

        /// <summary>Count the territorial world objects on a region's tiles, by kind — the labour-structure
        /// signal for employment (#16).</summary>
        private static void CountWorldObjects(GeographicProvince province, out int settlements, out int outposts, out int camps, out int military)
        {
            settlements = outposts = camps = military = 0;
            if (Find.WorldObjects == null || province?.tiles == null || province.tiles.Count == 0) return;

            var tileSet = new HashSet<int>(province.tiles);
            List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                WorldObject o = all[i];
                if (o == null) continue;
                PlanetTile pt = o.Tile;
                if (!pt.Valid || !tileSet.Contains(pt.tileId)) continue;
                switch (WorldObjectClassifier.Classify(o))
                {
                    case Integration.WorldObjectKind.Settlement: settlements++; break;
                    case Integration.WorldObjectKind.Outpost: outposts++; break;
                    case Integration.WorldObjectKind.Camp: camps++; break;
                    case Integration.WorldObjectKind.Military: military++; break;
                }
            }
        }

        /// <summary>Turn per-tier tile counts into shares and the collapsed 0-100 education index.
        /// Shared by the region and faction aggregators.</summary>
        private static void FillEducation(RegionDemographics demo, int[] eduCounts)
        {
            if (demo.settledTiles <= 0) return;
            for (int i = 0; i < EducationRules.TierCount; i++)
                demo.educationShares[i] = (float)eduCounts[i] / demo.settledTiles;
            demo.educationIndex = EducationRules.Index(demo.educationShares);
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

        /// <summary>
        /// A xenotype breakdown for a region — the top races by share — or a plain statement that with
        /// Biotech off everyone is Baseliner (so the overlay says so rather than painting a flat map as
        /// if it were data, #12). Null when the region has no settled tiles. Shared by the overlay
        /// tooltip and the region panel.
        /// </summary>
        public static string XenotypeSummary(GeographicProvince province)
        {
            if (province == null) return null;
            RegionDemographics demo = ForRegion(province);
            if (demo.settledTiles <= 0) return null;

            if (!demo.biotechActive) return null;   // Biotech absent: omit the line entirely (graceful degradation)
            if (demo.raceShares.Count == 0) return "Xenotypes: (no data)";

            var top = demo.raceShares.OrderByDescending(k => k.Value).Take(5)
                .Select(k => $"{k.Key.LabelCap} {k.Value:P0}");
            return "Xenotypes:\n  " + string.Join("   ", top);
        }

        /// <summary>
        /// An education breakdown for a region — the 0-100 attainment index and the four tier shares —
        /// or null when the region has no settled tiles. Shared by the overlay tooltip and the region
        /// panel (#15).
        /// </summary>
        public static string EducationSummary(GeographicProvince province)
        {
            if (province == null) return null;
            RegionDemographics demo = ForRegion(province);
            if (demo.settledTiles <= 0) return null;
            return $"Education (index {demo.educationIndex}/100):\n"
                + $"  Illiterate {demo.educationShares[(int)EducationTier.Illiterate]:P0}"
                + $"   Primary {demo.educationShares[(int)EducationTier.Primary]:P0}"
                + $"   Secondary {demo.educationShares[(int)EducationTier.Secondary]:P0}"
                + $"   Undergrad {demo.educationShares[(int)EducationTier.Undergrad]:P0}"
                + $"   Postgrad {demo.educationShares[(int)EducationTier.Postgrad]:P0}";
        }

        /// <summary>
        /// A socioeconomic breakdown for a region — the 0-100 wealth index and the four SES-tier shares —
        /// or null when the region has no settled tiles. Shared by the overlay tooltip and the region
        /// panel (#14).
        /// </summary>
        public static string SocioeconomicSummary(GeographicProvince province)
        {
            if (province == null) return null;
            RegionDemographics demo = ForRegion(province);
            if (demo.settledTiles <= 0) return null;
            return $"Socioeconomic (index {demo.sesIndex}/100):\n"
                + $"  Subsistence {demo.sesShares[(int)SesTier.Subsistence]:P0}"
                + $"   Modest {demo.sesShares[(int)SesTier.Modest]:P0}"
                + $"   Prosperous {demo.sesShares[(int)SesTier.Prosperous]:P0}"
                + $"   Affluent {demo.sesShares[(int)SesTier.Affluent]:P0}";
        }

        /// <summary>
        /// An ideology breakdown for a region — the top ideos by share and how similar its beliefs are to
        /// its neighbours' — or a plain statement that with Ideology off everyone is secular (so the
        /// overlay says so rather than painting a flat map, #13). Null when the region has no settled tiles.
        /// Shared by the overlay tooltip and the region panel.
        /// </summary>
        public static string IdeologySummary(GeographicProvince province)
        {
            if (province == null) return null;
            RegionDemographics demo = ForRegion(province);
            if (demo.settledTiles <= 0) return null;

            if (!demo.ideologyActive) return null;   // Ideology absent: omit the line entirely (graceful degradation)
            if (demo.ideoShares.Count == 0) return "Ideology: (no data)";

            var top = demo.ideoShares.OrderByDescending(k => k.Value).Take(4)
                .Select(k => $"{k.Key.name} {k.Value:P0}");
            string line = "Ideology:\n  " + string.Join("   ", top);

            float sim = AverageNeighborSimilarity(province);
            if (sim >= 0f) line += $"\n  Belief similarity to neighbours: {sim:P0}";
            return line;
        }

        /// <summary>The single most common ideo in a region and its share, or null when there is none
        /// (Ideology off, or unsettled). For the dominant-ideology overlay (#13).</summary>
        public static Ideo DominantIdeo(RegionDemographics demo, out float share)
        {
            share = 0f;
            Ideo best = null;
            if (demo?.ideoShares == null) return null;
            foreach (var kv in demo.ideoShares)
                if (kv.Value > share) { share = kv.Value; best = kv.Key; }
            return best;
        }

        /// <summary>Meme-level similarity between two regions' belief mixes, 0..1 (#13): the cosine of
        /// their meme-share vectors over the union of memes present. 1 = the same beliefs, 0 = none shared.</summary>
        public static float MemeSimilarity(GeographicProvince a, GeographicProvince b)
        {
            if (a == null || b == null) return 0f;
            RegionDemographics da = ForRegion(a), db = ForRegion(b);
            if (da.memeShares.Count == 0 || db.memeShares.Count == 0) return 0f;

            var keys = new List<MemeDef>(da.memeShares.Keys);
            foreach (MemeDef k in db.memeShares.Keys) if (!da.memeShares.ContainsKey(k)) keys.Add(k);

            var va = new float[keys.Count];
            var vb = new float[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                da.memeShares.TryGetValue(keys[i], out va[i]);
                db.memeShares.TryGetValue(keys[i], out vb[i]);
            }
            return DemographicsRules.Cosine(va, vb);
        }

        /// <summary>A region's average meme similarity to its adjacent land regions, or -1 when it has no
        /// comparable neighbour. How culturally distinct a region is from its surroundings (#13).</summary>
        public static float AverageNeighborSimilarity(GeographicProvince province)
        {
            var mgr = Find.World?.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null || province == null) return -1f;

            float sum = 0f;
            int n = 0;
            foreach (GeographicProvince p in mgr.Provinces)
            {
                if (p == null || p.id == province.id || p.provinceType != ProvinceType.Land) continue;
                if (!ProvinceAdjacency.AreAdjacent(mgr, province.id, p.id)) continue;
                sum += MemeSimilarity(province, p);
                n++;
            }
            return n > 0 ? sum / n : -1f;
        }

        /// <summary>
        /// An employment breakdown for a region — the workforce split across occupation sectors and the
        /// employment rate — or null when the region has no settled tiles. Shared by the overlay tooltip
        /// and the region panel (#16).
        /// </summary>
        public static string EmploymentSummary(GeographicProvince province)
        {
            if (province == null) return null;
            RegionDemographics demo = ForRegion(province);
            if (demo.settledTiles <= 0) return null;
            return $"Employment (rate {demo.employmentRate}%):\n"
                + $"  Agriculture {demo.occupationShares[(int)OccupationSector.Agriculture]:P0}"
                + $"   Industry {demo.occupationShares[(int)OccupationSector.Industry]:P0}"
                + $"   Military {demo.occupationShares[(int)OccupationSector.Military]:P0}"
                + $"   Trade {demo.occupationShares[(int)OccupationSector.Trade]:P0}";
        }

        /// <summary>The largest occupation sector in a region and its share. For the employment overlay (#16).</summary>
        public static OccupationSector DominantSector(RegionDemographics demo, out float share)
        {
            share = 0f;
            int best = 0;
            if (demo?.occupationShares == null) return OccupationSector.Agriculture;
            for (int i = 0; i < EmploymentRules.SectorCount; i++)
                if (demo.occupationShares[i] > share) { share = demo.occupationShares[i]; best = i; }
            return (OccupationSector)best;
        }

        /// <summary>The single most common xenotype in a region and its share, or null when there is no
        /// xenotype data (Biotech off, or unsettled). For the dominant-xenotype overlay (#12).</summary>
        public static XenotypeDef DominantXenotype(RegionDemographics demo, out float share)
        {
            share = 0f;
            XenotypeDef best = null;
            if (demo?.raceShares == null) return null;
            foreach (var kv in demo.raceShares)
                if (kv.Value > share) { share = kv.Value; best = kv.Key; }
            return best;
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
            if (!RegionsAndSocietiesMod.SocietiesEnabled) return new RegionDemographics();   // #53
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
            var ideoCounts = new Dictionary<Ideo, int>();
            var allWealth = new List<int>();
            var ageCounts = new int[AgeStructureRules.BucketCount];
            var eduCounts = new int[EducationRules.TierCount];
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
                    eduCounts[(int)s.educationTier]++;
                    longevityAcc += LongevityOf(s.race);
                    if (s.sex == Gender.Female) female++;
                    if (s.race != null)
                    {
                        raceCounts.TryGetValue(s.race, out int rc); raceCounts[s.race] = rc + 1;
                        if (!wealthByRace.TryGetValue(s.race, out List<int> wl)) { wl = new List<int>(); wealthByRace[s.race] = wl; }
                        wl.Add(s.wealth);
                    }
                    allWealth.Add(s.wealth);
                    if (s.ideo != null)
                    {
                        ideoCounts.TryGetValue(s.ideo, out int ic); ideoCounts[s.ideo] = ic + 1;
                        if (s.ideo.memes != null)
                            foreach (MemeDef m in s.ideo.memes)
                                if (m != null) { memeCounts.TryGetValue(m, out int mc); memeCounts[m] = mc + 1; }
                    }
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
                foreach (var kv in ideoCounts)
                    demo.ideoShares[kv.Key] = (float)kv.Value / demo.settledTiles;
                int[] wealthArr = allWealth.ToArray();
                demo.overallMedianWealth = DemographicsRules.Median(wealthArr, wealthArr.Length);
                FillAgeStructure(demo, ageCounts, longevityAcc);
                FillEducation(demo, eduCounts);
                FillSes(demo, allWealth, 1f);   // faction-wide: no single region's richness/trade to apply
                FillEmploymentForFaction(demo, faction);
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
                    if (pop > 0)
                    {
                        float reach = InfluenceReach(pop);
                        sources.Add(new PressureSource { tile = pt.tileId, faction = o.Faction, population = pop, reach = reach });
                    }
                }
            }
            return sources;
        }

        /// <summary>
        /// The subset of <paramref name="all"/> pressure sources that could reach <paramref name="province"/>
        /// — every source within (its own reach + the province's bounding radius) of a representative
        /// province tile. A superset of the sources that actually contribute (Pressure is exactly 0 beyond a
        /// source's reach, so any extra costs only a skipped iteration), so the region's demographics are
        /// identical — this only turns the per-tile pressure loop from O(all settlements) into O(nearby),
        /// which is what keeps the aggregation linear on large worlds.
        /// </summary>
        private static List<PressureSource> RelevantSources(GeographicProvince province, List<PressureSource> all)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || all == null || all.Count == 0 || province?.tiles == null || province.tiles.Count == 0)
                return all;

            int rep = province.tiles[0];
            float radius = 0f;
            for (int i = 1; i < province.tiles.Count; i++)
            {
                float d = CrowTiles(grid, rep, province.tiles[i]);
                if (d > radius) radius = d;
            }

            var rel = new List<PressureSource>();
            for (int i = 0; i < all.Count; i++)
            {
                PressureSource s = all[i];
                if (CrowTiles(grid, s.tile, rep) <= s.reach + radius) rel.Add(s);
            }
            return rel;
        }

        /// <summary>
        /// Self-check that the pressure-source culling is result-identical: aggregate every land region BOTH
        /// ways — full source list vs the reach-culled subset — and confirm every demographic field matches.
        /// Culling only drops sources whose Pressure is 0 for the region and adding 0 is a float no-op, so
        /// this must report zero mismatches; it exists to PROVE that empirically after any change to
        /// RelevantSources. Dev-only (it runs the slow full aggregation), surfaced via a debug action.
        /// </summary>
        public static string VerifyCulling()
        {
            EnsureFresh();
            var mgr = Find.World?.GetComponent<SynapseRegionManager>();
            if (mgr?.Provinces == null) return "[RegionsAndSocieties] VerifyCulling: no region manager.";

            List<PressureSource> full = Sources();
            int checkedN = 0, mismatches = 0, maxCulled = 0, minCulled = int.MaxValue;
            var sb = new System.Text.StringBuilder();
            foreach (var p in mgr.Provinces)
            {
                if (p == null || p.provinceType != ProvinceType.Land || p.tiles == null || p.tiles.Count == 0) continue;
                List<PressureSource> culled = RelevantSources(p, full);
                maxCulled = Math.Max(maxCulled, culled.Count);
                minCulled = Math.Min(minCulled, culled.Count);
                RegionDemographics a = AggregateWith(p, full);
                RegionDemographics b = AggregateWith(p, culled);
                checkedN++;
                if (!DemographicsEqual(a, b))
                {
                    mismatches++;
                    if (mismatches <= 8) sb.AppendLine($"  MISMATCH region {p.id}: {p.tiles.Count}t, full={full.Count} culled={culled.Count}");
                }
            }
            string head = $"[RegionsAndSocieties] VerifyCulling: {checkedN} land regions checked, {mismatches} mismatch(es). "
                        + $"Sources per region: full={full.Count}, culled {(minCulled == int.MaxValue ? 0 : minCulled)}..{maxCulled}.";
            return mismatches == 0 ? head + " CULLING IS RESULT-IDENTICAL." : head + "\n" + sb.ToString();
        }

        private static bool DemographicsEqual(RegionDemographics a, RegionDemographics b)
        {
            if (a.tileCount != b.tileCount || a.settledTiles != b.settledTiles) return false;
            if (a.femaleFraction != b.femaleFraction || a.overallMedianWealth != b.overallMedianWealth) return false;
            if (a.medianAge != b.medianAge || a.educationIndex != b.educationIndex) return false;
            if (a.sesIndex != b.sesIndex || a.employmentRate != b.employmentRate) return false;
            if (!ArrEq(a.ageShares, b.ageShares) || !ArrEq(a.educationShares, b.educationShares)) return false;
            if (!ArrEq(a.sesShares, b.sesShares) || !ArrEq(a.occupationShares, b.occupationShares)) return false;
            if (!DictEqF(a.factionShares, b.factionShares) || !DictEqF(a.raceShares, b.raceShares)) return false;
            if (!DictEqF(a.ideoShares, b.ideoShares) || !DictEqF(a.memeShares, b.memeShares)) return false;
            if (!DictEqI(a.medianWealthByRace, b.medianWealthByRace)) return false;
            // Custom (def-less) xenotypes are only ever filled by the cohort overlay in ForRegion, never by
            // AggregateWith — so they are empty on both sides here; compared for completeness/future-proofing.
            if (!DictEqF(a.customRaceShares, b.customRaceShares) || !DictEqI(a.customMedianWealthByName, b.customMedianWealthByName)) return false;
            return true;
        }

        private static bool ArrEq(float[] a, float[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static bool DictEqF<T>(Dictionary<T, float> a, Dictionary<T, float> b)
        {
            if (a == null || b == null) return a == b;
            if (a.Count != b.Count) return false;
            foreach (var kv in a) { if (!b.TryGetValue(kv.Key, out float v) || v != kv.Value) return false; }
            return true;
        }

        private static bool DictEqI<T>(Dictionary<T, int> a, Dictionary<T, int> b)
        {
            if (a == null || b == null) return a == b;
            if (a.Count != b.Count) return false;
            foreach (var kv in a) { if (!b.TryGetValue(kv.Key, out int v) || v != kv.Value) return false; }
            return true;
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
        /// #70: a settlement's influence radius in tiles, growing as the SQUARE ROOT of its population.
        /// The old formula multiplied raw population, so a 150-person settlement reached 150 tiles and
        /// the reach-based culling could never actually cull.
        /// </summary>
        private static float InfluenceReach(int population)
        {
            return Sizing.DistrictRules.InfluenceRadiusTiles(population, WorldObjectIntegrationSettings.demographicInfluence);
        }

        /// <summary>
        /// A settlement's demographic pressure at a given crow-flies distance. Reach comes from
        /// <see cref="InfluenceReach"/>; pressure falls from the full population at the centre to 0 at
        /// the reach, shaped by <see cref="WorldObjectIntegrationSettings.demographicFalloff"/>
        /// (1 = linear). Tuning these is how border regions are dialled to ~50-60% their own make-up.
        /// </summary>
        private static float Pressure(int population, float distanceTiles)
        {
            float reach = InfluenceReach(population);
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

        // --- xenotype intellectual aptitude (the education pyramid's third signal) ---

        private static readonly Dictionary<XenotypeDef, float> aptitudeCache = new Dictionary<XenotypeDef, float>();
        private static SkillDef intellectualSkill;
        private static StatDef learningStat;
        private static bool aptitudeStatsResolved;

        private static void ResolveAptitudeStats()
        {
            if (aptitudeStatsResolved) return;
            aptitudeStatsResolved = true;
            intellectualSkill = DefDatabase<SkillDef>.GetNamedSilentFail("Intellectual");
            learningStat = DefDatabase<StatDef>.GetNamedSilentFail("GlobalLearningFactor");
        }

        /// <summary>
        /// How engineered-for-intellect a race is, 0 (baseline) to 1, from its genes: an intellectual
        /// aptitude (a Genie-style caste) or a global learning-factor boost. Derived from the genes so it
        /// works for any modded xenotype, like the biome-affinity and longevity detectors; first-pass and
        /// tunable. Returns 0 when Biotech is off (null race) so the education pyramid degrades to the
        /// plain tech baseline.
        /// </summary>
        private static float AptitudeOf(XenotypeDef race)
        {
            if (race?.genes == null) return 0f;
            if (aptitudeCache.TryGetValue(race, out float cached)) return cached;

            ResolveAptitudeStats();
            float aptitude = 0f;
            for (int i = 0; i < race.genes.Count; i++)
            {
                GeneDef g = race.genes[i];
                if (g == null) continue;

                if (g.aptitudes != null && intellectualSkill != null)
                    foreach (Aptitude a in g.aptitudes)
                        if (a.skill == intellectualSkill) aptitude += a.level * 0.12f;   // ~4-level aptitude ≈ 0.5

                if (learningStat != null && g.statOffsets != null)
                    foreach (StatModifier so in g.statOffsets)
                        if (so?.stat == learningStat) aptitude += so.value * 0.5f;
            }
            aptitude = Mathf.Clamp01(aptitude);
            aptitudeCache[race] = aptitude;
            return aptitude;
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
            techLevel = (int)TechLevel.Industrial, natalistSkew = 0f, researchSkew = 0f
        };

        public XenotypeDef[] races;
        public float[] raceWeights;
        public int[] raceMedianWealth;
        public int fallbackWealth;
        public Ideo primaryIdeo;
        public Ideo[] ideos = new Ideo[0];       // primary + minors, the pool a tile's belief is drawn from (#13)
        public float[] ideoWeights = new float[0];
        public int techLevel;        // RimWorld TechLevel ordinal — shapes the base age/education spreads (#10/#15)
        public float natalistSkew;   // 0..1, from pro-natalist memes; pushes the age pyramid toward children
        public float researchSkew;   // -1..1, from tech vs primitivist memes; bends the education distribution

        public static FactionDemographicProfile Build(Faction faction)
        {
            var p = new FactionDemographicProfile();
            TechLevel tech = faction.def?.techLevel ?? TechLevel.Industrial;

            // Faction character (#27): a pirate band runs on looted tech — its people are not schooled or
            // prosperous the way its tech level alone implies. Skew knowledge and wealth by archetype, so
            // raiders read down and traders/empires read up. Unknown (modded/VFE) factions fall back to a
            // trait guess a compatibility patch can override.
            FactionArchetype archetype = FactionCharacterRules.Classify(
                faction.def?.defName, (int)tech, faction.def?.permanentEnemy ?? false);
            FactionCharacterRules.Character character = FactionCharacterRules.CharacterOf(archetype);

            int baseWealth = (int)System.Math.Round(BaseWealth(tech) * character.wealthMultiplier);
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
                try
                {
                    p.primaryIdeo = faction.ideos?.PrimaryIdeo;
                    BuildIdeoPool(p, faction);
                }
                catch { p.primaryIdeo = null; }
            }
            p.natalistSkew = NatalistSkew(p.primaryIdeo);
            // Fold the faction-character knowledge skew (#27) into the education research skew, clamped.
            p.researchSkew = Mathf.Clamp(ResearchSkew(p.primaryIdeo) + character.knowledgeSkew, -1f, 1f);

            return p;
        }

        /// <summary>
        /// Gather the faction's ideos into the draw pool a tile's belief is picked from (#13): its
        /// primary at full weight plus each minor ideo at a lighter weight, so most of a faction's land
        /// follows the primary while pockets follow the minors. Degrades to primary-only (or empty) when
        /// a faction has no minors or no ideos at all.
        /// </summary>
        private static void BuildIdeoPool(FactionDemographicProfile p, Faction faction)
        {
            var ideos = new List<Ideo>();
            var weights = new List<float>();
            if (p.primaryIdeo != null) { ideos.Add(p.primaryIdeo); weights.Add(1f); }

            List<Ideo> minors = faction.ideos?.IdeosMinorListForReading;
            if (minors != null)
                foreach (Ideo io in minors)
                    if (io != null && io != p.primaryIdeo) { ideos.Add(io); weights.Add(0.35f); }

            p.ideos = ideos.ToArray();
            p.ideoWeights = weights.ToArray();
        }

        /// <summary>
        /// How an ideology bends education, -1 (primitivist) to +1 (tech/transhumanist), read from its
        /// memes by name — a transhumanist/tech/progress meme lifts attainment, a primitivist/nature/
        /// tunneler/tree meme pulls it down. Keyword-based so it catches modded memes too; returns 0 with
        /// no Ideology, so the education model degrades to the bare tech baseline (#15). First-pass, tunable.
        /// </summary>
        private static float ResearchSkew(Ideo ideo)
        {
            if (ideo?.memes == null) return 0f;
            float skew = 0f;
            foreach (MemeDef m in ideo.memes)
            {
                string n = m?.defName;
                if (n == null) continue;
                if (n.IndexOf("Transhuman", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Tech", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Progress", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Research", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    skew += 1f;
                }
                if (n.IndexOf("Primitiv", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Nature", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Tunnel", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Tree", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    skew -= 1f;
                }
            }
            return Mathf.Clamp(skew, -1f, 1f);
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
            FactionDef fdef = faction?.def;
            if (fdef == null) return null;

            if (!xenoResolved)
            {
                xenoAvailMethod = AccessTools.Method(typeof(PawnGenerator), "XenotypesAvailableFor",
                    new[] { typeof(PawnKindDef), typeof(FactionDef), typeof(Faction) });
                xenoResolved = true;
            }
            if (xenoAvailMethod == null) return null;

            // A faction fields its members through many pawnkinds — its basicMemberKind AND every pawnkind
            // in its pawnGroupMakers (options, guards, traders, carriers). A modded faction that assigns its
            // xenotypes only through pawnGroupMakers (VFE-style) is invisible to basicMemberKind alone, which
            // is why such a faction read as plain Baseliner. Union the xenotype table over ALL its kinds so
            // the faction's real genetic spread surfaces (#58 modded-xenotype support). Cached per faction.
            var kinds = new HashSet<PawnKindDef>();
            if (fdef.basicMemberKind != null) kinds.Add(fdef.basicMemberKind);
            if (fdef.pawnGroupMakers != null)
            {
                for (int i = 0; i < fdef.pawnGroupMakers.Count; i++)
                {
                    PawnGroupMaker pgm = fdef.pawnGroupMakers[i];
                    if (pgm == null) continue;
                    AddKinds(kinds, pgm.options);
                    AddKinds(kinds, pgm.guards);
                    AddKinds(kinds, pgm.traders);
                    AddKinds(kinds, pgm.carriers);
                }
            }
            if (kinds.Count == 0) kinds.Add(PawnKindDefOf.Colonist);   // last-resort generic kind

            var union = new Dictionary<XenotypeDef, float>();
            foreach (PawnKindDef kind in kinds)
            {
                if (kind == null) continue;
                Dictionary<XenotypeDef, float> table;
                try { table = xenoAvailMethod.Invoke(null, new object[] { kind, fdef, faction }) as Dictionary<XenotypeDef, float>; }
                catch { continue; }
                if (table == null) continue;
                foreach (var kv in table)
                {
                    if (kv.Key == null || kv.Value <= 0f) continue;
                    union.TryGetValue(kv.Key, out float w); union[kv.Key] = w + kv.Value;
                }
            }
            return union.Count > 0 ? union : null;
        }

        private static void AddKinds(HashSet<PawnKindDef> set, List<PawnGenOption> options)
        {
            if (options == null) return;
            for (int i = 0; i < options.Count; i++)
                if (options[i]?.kind != null) set.Add(options[i].kind);
        }
    }
}
