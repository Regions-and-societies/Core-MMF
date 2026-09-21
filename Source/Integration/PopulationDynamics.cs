using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RegionsAndSocieties.Integration
{
    /// <summary>What a completed population-dynamics pass reports to a consumer (#5).</summary>
    public class PopulationMigrationArgs
    {
        public int colonyRegionId;   // the attractor this pass drifted toward, or -1 if none
        public float totalMoved;     // people migrated this pass (conserved: this left the source regions)
        public int passSerial;       // increments each pass, so a consumer can tell passes apart
    }

    /// <summary>
    /// Makes the population field respond to play (#5 migration, #8 accretion). Both passes mutate one
    /// shared per-region "dynamic delta" the region manager scribes, so they COMPOSE instead of fighting;
    /// the effective population of a region is its derived base plus this delta. The write order is fixed:
    /// growth (#6, elsewhere) → accrete (#8) → migrate (#5).
    ///
    /// <para><b>Scaffold.</b> The structure, cadence, governance gate, conservation and the public endpoint
    /// are in; every RATE / THRESHOLD / FALLOFF is a placeholder to tune in a later milestone (#30). The
    /// accretion body and the migration distance-falloff are stubbed and marked. The delta is not yet fused
    /// into the visible per-tile heatmap — that wiring is the follow-up.</para>
    ///
    /// <para>Public endpoint, reflection-friendly like <see cref="TerritoryClaimHooks"/>: core holds no
    /// reference to a consumer, so a consumer (Empire-CP, a storyteller) sets <see cref="OnMigrationPass"/>
    /// after load. A no-op with nothing hooked.</para>
    /// </summary>
    public static class PopulationDynamics
    {
        /// <summary>Optional consumer, fired at the end of every pass. Unset = no-op.</summary>
        public static Action<PopulationMigrationArgs> OnMigrationPass;

        // ---- tuning (#36; calibrated from the placeholders #30 left) ----
        /// <summary>Cadence: every 10 in-game days (the region manager ticks at 60000/day).</summary>
        public const int CadenceTicks = 600000;
        /// <summary>Fraction of a region's movable population drawn toward the colony per pass, before the
        /// distance falloff. A far region moves this × <see cref="PopulationDynamicsRules.DistanceFalloff"/>.
        /// Kept gentle: the pull sums over every reachable region, so a small per-region rate still fills the
        /// colony to its ceiling in a handful of passes.</summary>
        public const float MigrationRate = 0.03f;
        /// <summary>A region never migrates below this effective population.</summary>
        public const float RegionFloor = 5f;
        /// <summary>People a population centre accretes into each habitable neighbour per pass (#8), bounded
        /// by the neighbour's accretion cap.</summary>
        public const float AccretionStep = 4f;
        /// <summary>A region is a "population centre" that radiates accretion once its base reaches this — a
        /// homestead-scale settlement (see DistrictRules). Below it, a region is hinterland and only receives.</summary>
        public const float AccretionCentreThreshold = 100f;

        private static int passSerial;

        /// <summary>Run the dynamics passes in the documented order (accrete → migrate) on the shared delta.
        /// No-op when placement governance is off — the field then never moves. Returns people migrated.</summary>
        public static float RunPasses(SynapseRegionManager mgr, Dictionary<int, float> delta)
        {
            if (mgr == null || delta == null) return 0f;
            if (!mgr.StrictTerritorialOwnership) return 0f;   // governance off → the heatmap never moves

            Accrete(mgr, delta);
            int colony = ColonyRegion(mgr);
            float moved = Migrate(mgr, delta, colony);

            passSerial++;
            Fire(new PopulationMigrationArgs { colonyRegionId = colony, totalMoved = moved, passSerial = passSerial });
            return moved;
        }

        /// <summary>#8 — population centres cause their habitable neighbours to fill in (hinterland accretion),
        /// each bounded by the neighbour's accretion cap so it tops out instead of running away, and can never
        /// exceed the caps migration also respects. Net growth in place (not conserved), unlike migration.</summary>
        private static void Accrete(SynapseRegionManager mgr, Dictionary<int, float> delta)
        {
            if (AccretionStep <= 0f) return;
            var provinces = mgr.Provinces;
            for (int i = 0; i < provinces.Count; i++)
            {
                GeographicProvince centre = provinces[i];
                if (centre == null || centre.provinceType != ProvinceType.Land) continue;
                if (centre.currentPopulation < AccretionCentreThreshold) continue;   // only real centres radiate

                foreach (int nb in ProvinceAdjacency.NeighboursOf(mgr, centre.id))
                {
                    GeographicProvince np = mgr.GetProvince(nb);
                    if (np == null || np.provinceType != ProvinceType.Land) continue;
                    float nBase = np.currentPopulation;
                    float add = PopulationDynamicsRules.AccretionInto(AccretionStep, nBase + Get(delta, nb), nBase);
                    if (add > 0f) Add(delta, nb, +add);
                }
            }
        }

        /// <summary>#5 — draw movable population from every reachable region toward the colony, falling off
        /// with adjacency-hop distance (nearby regions feed it, the far side barely notices) and stopping once
        /// the colony hits its ceiling, so a long game cannot pile the planet onto one tile. Conserving: what
        /// leaves the sources is added to the colony, and no region drops below its floor.</summary>
        private static float Migrate(SynapseRegionManager mgr, Dictionary<int, float> delta, int colonyRegion)
        {
            if (colonyRegion < 0) return 0f;

            GeographicProvince colonyProv = mgr.GetProvince(colonyRegion);
            float colonyBase = colonyProv != null ? colonyProv.currentPopulation : 0f;
            float room = PopulationDynamicsRules.ColonyRoom(colonyBase, Get(delta, colonyRegion));
            if (room <= 0f) return 0f;   // colony is at its ceiling — nothing more migrates in

            Dictionary<int, int> hops = HopDistances(mgr, colonyRegion);
            float totalMoved = 0f;
            var provinces = mgr.Provinces;
            for (int i = 0; i < provinces.Count; i++)
            {
                GeographicProvince p = provinces[i];
                if (p.provinceType != ProvinceType.Land || p.id == colonyRegion) continue;
                if (!hops.TryGetValue(p.id, out int h)) continue;   // unreachable (a separate landmass) — no pull

                float here = p.currentPopulation + Get(delta, p.id);
                float movable = PopulationDynamicsRules.Movable(here, RegionFloor);
                if (movable <= 0f) continue;

                float move = MigrationRate * movable * PopulationDynamicsRules.DistanceFalloff(h);
                if (move <= 0f) continue;
                if (move > room) move = room;   // clip to the colony's remaining ceiling room

                Add(delta, p.id, -move);
                Add(delta, colonyRegion, +move);
                totalMoved += move;
                room -= move;
                if (room <= 0f) break;
            }
            return totalMoved;   // conserved: every unit removed above was added to the colony
        }

        /// <summary>Adjacency-hop distance from the colony region to every reachable land region (BFS over the
        /// province graph). Regions on a separate landmass are absent — they exert/receive no migration pull.</summary>
        private static Dictionary<int, int> HopDistances(SynapseRegionManager mgr, int start)
        {
            var dist = new Dictionary<int, int> { { start, 0 } };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                int d = dist[cur];
                foreach (int nb in ProvinceAdjacency.NeighboursOf(mgr, cur))
                    if (!dist.ContainsKey(nb)) { dist[nb] = d + 1; queue.Enqueue(nb); }
            }
            return dist;
        }

        /// <summary>The region holding the player's colony — the migration attractor. -1 if the player has
        /// no world settlement yet.</summary>
        public static int ColonyRegion(SynapseRegionManager mgr)
        {
            var settlements = Find.WorldObjects?.Settlements;
            if (settlements == null) return -1;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s?.Faction != null && s.Faction.IsPlayer)
                {
                    var prov = mgr.GetProvinceForTile(s.Tile);
                    if (prov != null) return prov.id;
                }
            }
            return -1;
        }

        private static float Get(Dictionary<int, float> d, int id) => d.TryGetValue(id, out float v) ? v : 0f;
        private static void Add(Dictionary<int, float> d, int id, float v) => d[id] = Get(d, id) + v;

        private static void Fire(PopulationMigrationArgs a)
        {
            try { OnMigrationPass?.Invoke(a); }
            catch (Exception e) { Log.Error("[RegionsAndSocieties] PopulationDynamics consumer threw: " + e); }
        }
    }
}
