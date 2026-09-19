namespace RegionsAndSocieties.Sizing
{
    /// <summary>How full a settled place is against its nominal population (#69): the single axis that
    /// runs from a district falling to ruin, through comfortable, to overcrowded.</summary>
    public enum DistrictOccupancy
    {
        Ruined,
        Sparse,
        Nominal,
        Crowded,
        Overcrowded
    }

    /// <summary>
    /// How a settlement occupies its world tile (#69). A tile is ~374 local maps of ground
    /// (<see cref="WorldScaleRules"/>), but people do not spread evenly across 23 km²: they cluster,
    /// with farmland and wilderness around them. So the settled part of a tile is modelled as a
    /// <b>hex cluster of districts</b>, one district being exactly one local map, growing outward in
    /// rings — the same arrangement RimWorld's hex tiles already imply:
    /// <code>
    ///         [NW] [NE]
    ///       [W]  [X]  [E]
    ///         [SW] [SE]
    /// </code>
    /// <c>X</c> is the rendered map. Ring 1 adds the six neighbours, and district counts follow the
    /// centered hexagonal numbers 1, 7, 19, 37, 61.
    ///
    /// <para><b>Why this exists.</b> Taken literally for population, a tile of 374 maps says a
    /// late-game colony of 30-40 pawns sits on ground holding tens of thousands, so the world map
    /// calls the player a rounding error. Districts fix that: a fully built local map is ~100 people
    /// at the measured build density, so a developed 40-pawn colony is within ~2.5x of the model
    /// rather than 400x, and every tier still leaves most of the tile as hinterland.</para>
    ///
    /// <para><b>The player rule.</b> The rendered map is district <c>X</c> and <b>its population is
    /// exactly the pawn count</b>; the model never overrides or penalises it. The player's tier comes
    /// from developed area and wealth, not head count, and promotion adds <i>surrounding</i> districts,
    /// so building a city grows the tile by acquiring suburbs rather than by declaring the player
    /// short of people. See <see cref="PlayerTilePopulation"/>.</para>
    ///
    /// <para><b>Districts are a unit of measure, never objects.</b> 374 districts × 119,904 tiles is
    /// ~45 million; nothing here instantiates one. Every answer is closed-form from population. Only
    /// the sub-map expansion materialises a district, lazily, when a player visits it.</para>
    ///
    /// <para>Pure and deterministic (no game types), so it unit-tests against the hand-written
    /// doubles. Every number is a tunable endpoint, not a hidden constant.</para>
    /// </summary>
    public static class DistrictRules
    {
        /// <summary>People per km² in a developed district. Measured, not guessed: a late-game
        /// advanced settlement (individual rooms, luxury goods, quarters for an archduke and two
        /// knights) held 25 pawns in a quarter of a 250x250 map, which is 625 m² per pawn. In real
        /// terms that is a low-density suburb, so it reads as a comfortable developed density rather
        /// than a ceiling. RimWorld cannot build vertically, so denser than a few times this is not
        /// reachable in vanilla.</summary>
        public const float BuildDensityPerKm2 = 1600f;

        /// <summary>Population living outside the settled districts — the farms and outlying
        /// homesteads that feed the place — as a share of the settled population. Proportional rather
        /// than a flat density, so a homestead's tile stays genuinely empty (~1 person/km²) while a
        /// metropolis carries real farmland (~78/km²). First-pass; validated in #30.</summary>
        public const float HinterlandShare = 0.25f;

        /// <summary>Rings of districts a metropolis may reach. The cap that keeps tile totals within
        /// an explainable multiple of what a player can actually see and build; raising it toward the
        /// tile's full 374 districts trends toward true geographic scale.</summary>
        public const int DefaultRingCap = 4;

        // Tier thresholds for classifying a player's settlement by how developed it is, rather than
        // by head count (which RimWorld's engine caps far below a real city).
        public const float HamletDevelopment = 0.05f;
        public const float VillageDevelopment = 0.20f;
        public const float TownDevelopment = 0.45f;
        public const float CityDevelopment = 0.75f;

        // -- ring geometry -----------------------------------------------------

        /// <summary>Districts in a hex cluster of <paramref name="rings"/> rings: the centered
        /// hexagonal numbers 1 + 3k(k+1) — 1, 7, 19, 37, 61, 91, 127.</summary>
        public static int DistrictsInRings(int rings)
        {
            if (rings <= 0) return 1;
            // long math: 3k(k+1) overflows int well before the ring count becomes meaningless, and a
            // silent negative would invert every cap that reads this.
            long n = 1L + 3L * rings * (rings + 1L);
            return n > int.MaxValue ? int.MaxValue : (int)n;
        }

        /// <summary>The smallest ring count whose cluster holds at least this many districts.</summary>
        public static int RingsForDistricts(int districts)
        {
            if (districts <= 1) return 0;
            int rings = 0;
            while (DistrictsInRings(rings) < districts) rings++;
            return rings;
        }

        /// <summary>Rings a tier occupies: Homestead 0 through City 4. The rung index and the
        /// ring count are the same number, which is why the ladder is exactly five rungs long and why
        /// dropping a rung shortens both together.</summary>
        public static int RingsForTier(SettlementTier tier)
        {
            int rung = (int)tier;
            if (rung < 0) return 0;
            return rung > TierPyramidRules.MaxTier ? TierPyramidRules.MaxTier : rung;
        }

        /// <summary>Districts a tier occupies: 1, 7, 19, 37, 61.</summary>
        public static int DistrictsForTier(SettlementTier tier)
        {
            return DistrictsInRings(RingsForTier(tier));
        }

        // -- population --------------------------------------------------------

        /// <summary>People a single developed district holds: 100 on a default 250x250 map, 400 on a
        /// 500x500 one, because a district is one local map and rescales with the player's map size.</summary>
        public static float PeoplePerDistrict(int mapEdgeCells)
        {
            float areaKm2 = WorldScaleRules.DistrictAreaKm2(mapEdgeCells);
            if (areaKm2 <= 0f) return 0f;
            return BuildDensityPerKm2 * areaKm2;
        }

        /// <summary>The settled population of a tier: 100 / 700 / 1,900 / 3,700 / 6,100 at the default
        /// map size. Excludes hinterland; see <see cref="TilePopulation"/>.</summary>
        public static int PopulationForTier(SettlementTier tier, int mapEdgeCells)
        {
            return RoundToInt(DistrictsForTier(tier) * PeoplePerDistrict(mapEdgeCells));
        }

        /// <summary>Districts a settled population needs, capped at <paramref name="ringCap"/> rings so
        /// tile totals stay explainable. Any population above zero occupies at least one district.</summary>
        public static int DistrictsForPopulation(int population, int mapEdgeCells, int ringCap)
        {
            if (population <= 0) return 0;
            float per = PeoplePerDistrict(mapEdgeCells);
            if (per <= 0f) return 0;
            int needed = (int)System.Math.Ceiling(population / per);
            if (needed < 1) needed = 1;
            int cap = DistrictsInRings(ringCap < 0 ? 0 : ringCap);
            return needed > cap ? cap : needed;
        }

        /// <summary>Districts a settled population needs with no ring cap at all — the true built
        /// extent, which the radius maths (#70) needs rather than the display-capped cluster.</summary>
        public static int DistrictsForPopulationUncapped(int population, int mapEdgeCells)
        {
            if (population <= 0) return 0;
            float per = PeoplePerDistrict(mapEdgeCells);
            if (per <= 0f) return 0;
            long needed = (long)System.Math.Ceiling(population / per);
            if (needed < 1L) needed = 1L;
            return needed > int.MaxValue ? int.MaxValue : (int)needed;
        }

        /// <summary>Districts a settled population needs, at the default ring cap.</summary>
        public static int DistrictsForPopulation(int population, int mapEdgeCells)
        {
            return DistrictsForPopulation(population, mapEdgeCells, DefaultRingCap);
        }

        /// <summary>The tier a settled population reads as. Below one full district it is a homestead;
        /// otherwise the highest tier whose settled population it has reached.</summary>
        public static SettlementTier TierForPopulation(int population, int mapEdgeCells)
        {
            SettlementTier result = SettlementTier.Homestead;
            for (int t = (int)SettlementTier.City; t >= 0; t--)
            {
                var tier = (SettlementTier)t;
                if (population >= PopulationForTier(tier, mapEdgeCells)) { result = tier; break; }
            }
            return result;
        }

        // -- how full a place actually is --------------------------------------

        /// <summary>Days to build one district on ordinary ground. 5 days is RimWorld's
        /// <c>GenDate.TicksPerTwelfth</c>, the nearest thing it has to a week.</summary>
        public const float BaseBuildDays = 5f;

        /// <summary>Days for one empty district to fall to ruin. Slower than building on purpose: the
        /// asymmetry is what stops a settlement flapping between building and ruining.</summary>
        public const float BaseRuinDays = 10f;

        /// <summary>The slowest a district can be to build, however hostile the ground.</summary>
        public const float MaxBuildDays = BaseBuildDays * 10f;

        /// <summary>
        /// The most a place can be crowded before births stop entirely. <b>Not a new number</b>: it is
        /// <see cref="BirthrateRules.BirthStagnationRatio"/>, the ceiling the growth model already
        /// clamps to, so the district model and the population model cannot disagree about how full
        /// "full" is. Raising it to 2.0 is a change to that constant, and it changes growth behaviour
        /// as well as capacity.
        /// </summary>
        public static float MaxOccupancy { get { return BirthrateRules.BirthStagnationRatio; } }

        /// <summary>Below this share of nominal, a district reads as abandoned and is a candidate to
        /// fall to ruin. The low end of the same axis overcrowding sits at the top of.</summary>
        public const float RuinedOccupancy = 0.15f;

        /// <summary>Below this, a district is standing but thinly settled.</summary>
        public const float SparseOccupancy = 0.60f;

        /// <summary>Above nominal but bearable; past this it reads as overcrowded.</summary>
        public const float CrowdedOccupancy = 1.25f;

        /// <summary>
        /// The population a tier holds at a given land quality and crowding.
        ///
        /// <para><see cref="PopulationForTier"/> is the <b>nominal</b> figure: every district built to
        /// the measured comfortable density, at exactly 100% occupancy. Real places sit either side of
        /// it, and crowding is the axis they sit on.</para>
        ///
        /// <para>Hostile ground does <b>not</b> shrink a district. Even a city occupies only a sixth of
        /// its tile, so terrain rarely makes the area impossible — it makes it slower and costlier to
        /// develop. Mountains and marsh therefore lengthen <see cref="BuildDaysForToil"/> instead of
        /// reducing capacity, which keeps a district meaning one thing everywhere.</para>
        /// </summary>
        public static int PopulationAt(SettlementTier tier, int mapEdgeCells, float occupancy)
        {
            float nominal = PopulationForTier(tier, mapEdgeCells);
            float crowd = occupancy < 0f ? 0f : (occupancy > MaxOccupancy ? MaxOccupancy : occupancy);
            return RoundToInt(nominal * crowd);
        }

        /// <summary>
        /// Days to add one district on ground of this <paramref name="toil"/>, which is
        /// <c>BiomeHabitabilityRules.Toil</c>: 1 on open ground, falling as movement difficulty rises,
        /// and already tech-aware, so an industrial society drives a road through a swamp faster than a
        /// tribe does. Passed as a plain number so this layer stays free of the placement layer.
        ///
        /// <para>This is where terrain lives. A tribe in a swamp (toil 0.25) takes four times as long
        /// per district as the same tribe on open ground, and ends up smaller not because its districts
        /// hold fewer people but because it never finished building them.</para>
        /// </summary>
        public static float BuildDaysForToil(float toil)
        {
            if (toil <= 0f) return MaxBuildDays;
            float clamped = toil > 1f ? 1f : toil;
            float days = BaseBuildDays / clamped;
            return days > MaxBuildDays ? MaxBuildDays : days;
        }

        /// <summary>The most a tier can hold: every district built, on ordinary ground, crowded to the
        /// point where births stop. The honest ceiling, against the nominal target.</summary>
        public static int MaxPopulationForTier(SettlementTier tier, int mapEdgeCells)
        {
            return PopulationAt(tier, mapEdgeCells, MaxOccupancy);
        }

        /// <summary>How full a place is, as a share of its nominal population. 1 is comfortable, above
        /// 1 is crowded, near 0 is abandoned. Returns 0 when the tier could hold nobody.</summary>
        public static float OccupancyOf(int population, SettlementTier tier, int mapEdgeCells)
        {
            float nominal = PopulationForTier(tier, mapEdgeCells);
            if (nominal <= 0f || population <= 0) return 0f;
            return population / nominal;
        }

        /// <summary>The band an occupancy falls in, so consumers can speak about a place in words
        /// rather than ratios: ruin candidates at the bottom, overcrowding pressure at the top.</summary>
        public static DistrictOccupancy OccupancyBand(float occupancy)
        {
            if (occupancy < RuinedOccupancy) return DistrictOccupancy.Ruined;
            if (occupancy < SparseOccupancy) return DistrictOccupancy.Sparse;
            if (occupancy <= 1f) return DistrictOccupancy.Nominal;
            if (occupancy <= CrowdedOccupancy) return DistrictOccupancy.Crowded;
            return DistrictOccupancy.Overcrowded;
        }

        // -- the tile as a whole -----------------------------------------------

        /// <summary>The fraction of a tile's ground a district cluster covers. Even a metropolis uses
        /// about a sixth at the default map size, which is what leaves room for suburbs and farmland
        /// to be real rather than a fudge.</summary>
        public static float SettledShareOfTile(int districts, int mapEdgeCells)
        {
            float maps = WorldScaleRules.MapsPerTile(mapEdgeCells);
            if (maps <= 0f || districts <= 0) return 0f;
            float share = districts / maps;
            return share > 1f ? 1f : share;
        }

        /// <summary>People living outside the settled districts: farms and outlying homesteads.</summary>
        public static int HinterlandPopulation(int settledPopulation)
        {
            if (settledPopulation <= 0) return 0;
            return RoundToInt(settledPopulation * HinterlandShare);
        }

        /// <summary>The density the hinterland works out to, people per km², as a check that the share
        /// above stays sane across tiers: about 1/km² around a homestead, about 65/km² around a
        /// metropolis.</summary>
        public static float HinterlandDensityPerKm2(int settledPopulation, int settledDistricts, int mapEdgeCells)
        {
            float tileKm2 = WorldScaleRules.TileAreaKm2;
            float settledKm2 = settledDistricts * WorldScaleRules.DistrictAreaKm2(mapEdgeCells);
            float openKm2 = tileKm2 - settledKm2;
            if (openKm2 <= 0f) return 0f;
            return HinterlandPopulation(settledPopulation) / openKm2;
        }

        /// <summary>Everyone on a tile: the settled districts plus the hinterland that feeds them.</summary>
        public static int TilePopulation(int settledPopulation)
        {
            if (settledPopulation <= 0) return 0;
            return settledPopulation + HinterlandPopulation(settledPopulation);
        }

        // -- the player's own tile ---------------------------------------------

        /// <summary>
        /// The tier a player's settlement has earned, read from how much of its map is developed and
        /// how wealthy that development is — never from head count, which RimWorld's engine caps far
        /// below a real city. <paramref name="developedFraction"/> is built ground over map ground;
        /// <paramref name="wealthMultiplier"/> is 1 for an ordinary build and above 1 for a rich one.
        /// </summary>
        public static SettlementTier TierFromDevelopment(float developedFraction, float wealthMultiplier)
        {
            if (developedFraction <= 0f) return SettlementTier.Homestead;
            float score = developedFraction * (wealthMultiplier <= 0f ? 1f : wealthMultiplier);
            if (score >= CityDevelopment) return SettlementTier.City;
            if (score >= TownDevelopment) return SettlementTier.Town;
            if (score >= VillageDevelopment) return SettlementTier.Village;
            if (score >= HamletDevelopment) return SettlementTier.Hamlet;
            return SettlementTier.Homestead;
        }

        /// <summary>
        /// The population of the player's tile, under the rule that <b>the player's own count is
        /// authoritative for their own district and is never contradicted</b>. The rendered map
        /// contributes exactly <paramref name="colonistCount"/>; the surrounding districts their
        /// modelled share; the hinterland its usual share of the total. Developing the colony raises
        /// the tier, which adds suburbs around the player rather than declaring them short of people.
        /// </summary>
        public static int PlayerTilePopulation(int colonistCount, SettlementTier tier, int mapEdgeCells)
        {
            if (colonistCount < 0) colonistCount = 0;
            int surrounding = DistrictsForTier(tier) - 1;
            if (surrounding < 0) surrounding = 0;
            int settled = colonistCount + RoundToInt(surrounding * PeoplePerDistrict(mapEdgeCells));
            return TilePopulation(settled);
        }

        /// <summary>The share of the player's tile population that is actually on screen — the
        /// reconciliation line, so the gap is stated rather than hidden.</summary>
        public static float RenderedShare(int colonistCount, SettlementTier tier, int mapEdgeCells)
        {
            int total = PlayerTilePopulation(colonistCount, tier, mapEdgeCells);
            if (total <= 0) return 0f;
            return (float)colonistCount / total;
        }

        // -- radii, for the pressure falloff (#70) -----------------------------

        /// <summary>The built radius of a district cluster, in districts: the radius of a circle with
        /// the cluster's area. Tracks the ring count closely (ring k gives about k + 0.5).</summary>
        public static float BuiltRadiusDistricts(int districts)
        {
            if (districts <= 0) return 0f;
            return (float)System.Math.Sqrt(districts / System.Math.PI);
        }

        /// <summary>
        /// The built radius of a settlement in world tiles — the quantity #70's pressure falloff scales
        /// its reach from. Grows as the square root of population, because built area is proportional
        /// to population and radius goes as the square root of area.
        /// </summary>
        public static float BuiltRadiusTiles(int population, int mapEdgeCells)
        {
            int districts = DistrictsForPopulationUncapped(population, mapEdgeCells);
            float acrossTile = WorldScaleRules.DistrictsAcrossTile(mapEdgeCells);
            if (districts <= 0 || acrossTile <= 0f) return 0f;
            return BuiltRadiusDistricts(districts) / acrossTile;
        }

        // -- demographic influence (#70) ---------------------------------------

        /// <summary>
        /// How far a settlement's demographic pull reaches beyond its own built edge, as a multiple
        /// of that edge. Dimensionless, unlike the population multiplier it replaced.
        ///
        /// <para>The band worth tuning within: at 13 a village reaches ~1 tile and a metropolis ~3;
        /// at ~70 a town reaches a whole region radius, which is what the pre-0.5.0 tuning target of
        /// "border regions at 50-60% their own make-up" implies. 26 sits between them (a village
        /// ~2 tiles, a town ~3.3, a metropolis ~5.9) and is a starting point, not a measured answer.
        /// #30 tunes it against the border-blend target in a live world.</para>
        /// </summary>
        public const float DefaultInfluenceMultiplier = 26f;

        /// <summary>The floor on influence, in tiles. Even a hamlet's people are known in the
        /// countryside around them, and without a floor a small settlement would colour nothing but
        /// its own tile. Roughly a few hours' walk.</summary>
        public const float MinInfluenceTiles = 1.5f;

        /// <summary>
        /// The radius, in world tiles, within which a settlement colours the demographics around it.
        /// <b>Grows as the square root of population</b>, because built area is proportional to
        /// population and radius goes as the square root of area - the fix #70 exists for. The reach
        /// it replaced was linear in population and measured in tiles, so a 150-person settlement
        /// projected pressure ~150 tiles and the culling that depends on reach could never fire.
        ///
        /// <para>Pressure is exactly zero beyond this distance, and must stay that way: the region
        /// aggregation culls sources by reach, so a non-zero tail would make every source relevant to
        /// every tile again. Population living out in the open country is modelled by
        /// <see cref="HinterlandPopulation"/> instead, not by stretching every settlement's reach.</para>
        /// </summary>
        public static float InfluenceRadiusTiles(int population, int mapEdgeCells, float influenceMultiplier)
        {
            if (population <= 0) return 0f;
            float mult = influenceMultiplier > 0f ? influenceMultiplier : DefaultInfluenceMultiplier;
            float reach = BuiltRadiusTiles(population, mapEdgeCells) * mult;
            return reach < MinInfluenceTiles ? MinInfluenceTiles : reach;
        }

        /// <summary>Influence radius at the default multiplier.</summary>
        public static float InfluenceRadiusTiles(int population, int mapEdgeCells)
        {
            return InfluenceRadiusTiles(population, mapEdgeCells, DefaultInfluenceMultiplier);
        }

        private static int RoundToInt(float v)
        {
            return (int)System.Math.Round(v, System.MidpointRounding.AwayFromZero);
        }
    }
}
