using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RegionsAndSocieties.Partition
{
    /// <summary>
    /// The game-coupled half of the border-first partition (#20). Turns the world grid into land
    /// provinces whose borders sit on natural feature boundaries and whose centres are river basins.
    ///
    /// <para>Pipeline: classify every land tile into a <see cref="TileSignal"/>; build the boundary
    /// field across land-land edges with <see cref="BorderRules.BoundaryStrength"/>; flood land into
    /// <b>cells</b> separated by wall edges (border-first); then split any cell larger than the size
    /// guide into river basins by a marker-controlled watershed (rivers seed the markers), or, in a
    /// featureless cell with no river, into evenly-spaced anchors whose divide falls at the midpoint.
    /// A cell within the size band is kept whole, so region size varies with the terrain.</para>
    ///
    /// <para>Everything that reads <c>Find.WorldGrid</c> lives here; the pure decisions are delegated
    /// to <see cref="BorderRules"/>. Not unit-testable without a game — covered by the in-game audit.
    /// Deterministic: tiles are visited and markers chosen in id order, so a world regenerates
    /// identically.</para>
    /// </summary>
    public static class BorderPartitioner
    {
        // Tree-density cut points for the forest bucket (0 open, 1 wooded, 2 thick forest). BiomeDef
        // tree density runs ~0 (desert) to ~1 (dense jungle); these split it into three bands.
        private const float WoodedTreeDensity = 0.25f;
        private const float ThickTreeDensity = 0.6f;

        // Master switch for the pass-neck feature (#20). Off until a selective pass rule replaces the
        // over-firing opposite-sides primitive.
        private const bool EnableNeckDetection = false;

        /// <summary>
        /// Partition the unclaimed land of the world into province tile-groups. Water, ocean and lake
        /// tiles are expected to be already claimed in <paramref name="tileToProvinceId"/> (their
        /// entries are &gt;= 0) and are treated as hard walls; impassable land is left unclaimed here
        /// and folded in later by the caller's enclosed-gap pass. Returns one tile list per land
        /// province, ready for the caller to wrap in <c>GeographicProvince</c>s.
        /// </summary>
        public static List<List<int>> PartitionLand(int[] tileToProvinceId, int minRegionTiles, int maxRegionTiles)
        {
            var result = new List<List<int>>();
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tileToProvinceId == null) return result;

            int total = grid.TilesCount;
            int target = maxRegionTiles > 0 ? maxRegionTiles : 150;

            // Stable biome identity: first-encounter order over ascending tile ids is deterministic.
            var biomeIds = new Dictionary<BiomeDef, int>();
            var signals = new TileSignal[total];
            var isLand = new bool[total];
            for (int t = 0; t < total; t++)
            {
                signals[t] = Classify(grid, t, biomeIds);
                // A land tile eligible for a province: unclaimed, and neither water nor impassable
                // (those are the hard walls the fill never spans).
                isLand[t] = tileToProvinceId[t] < 0 && !signals[t].Water && !signals[t].Impassable;
            }

            // Pass-neck detection (gated OFF pending a selective saddle rule; kept for iteration).
            var isNeck = EnableNeckDetection ? MarkPassNecks(grid, isLand, signals, total) : new bool[total];
#pragma warning disable 0162 // unreachable while EnableNeckDetection is const false
            if (EnableNeckDetection)
            {
                int neckCount = 0; for (int i = 0; i < total; i++) if (isNeck[i]) neckCount++;
                Log.Message($"[RegionsAndSocieties] Border-first: {neckCount} pass-neck tiles walled off.");
            }
#pragma warning restore 0162

            var fillable = new bool[total];
            for (int t = 0; t < total; t++) fillable[t] = isLand[t] && !isNeck[t];

            // Anchors: one region centre per ~target tiles, per connected land component, spaced by
            // farthest-point so cells come out evenly sized. Nearest-anchor cells give the clean,
            // convex ~6-sided borders; biomes/forests only nudge a border (below), never draw it.
            var anchors = SelectAnchors(grid, fillable, total, target);
            if (anchors.Count == 0) return result;

            // Hybrid fill: multi-source Dijkstra from the anchors. The step cost is dominated by
            // distance (1 per tile) with a SMALL surcharge for crossing a biome / forest edge, so a
            // border sits on the straight bisector between two anchors unless a real terrain edge runs
            // close to it — in which case the two floods meet ON that edge and the border snaps to it.
            // Hard walls (water / impassable) and neck tiles are never traversed, so a cell hugs a
            // coastline or mountain range for free without that irregularity distorting its open sides.
            var owner = new int[total];
            var cost = new float[total];
            for (int i = 0; i < total; i++) { owner[i] = -1; cost[i] = float.PositiveInfinity; }
            var heap = new MinHeap(anchors.Count + 16);
            foreach (int a in anchors) { owner[a] = a; cost[a] = 0f; heap.Push(a, 0f); }
            var nb = new List<PlanetTile>();
            while (heap.Count > 0)
            {
                heap.Pop(out int cur, out float cc);
                if (cc > cost[cur]) continue;   // stale heap entry
                nb.Clear();
                grid.GetTileNeighbors(cur, nb);
                foreach (var n in nb)
                {
                    int nid = n.tileId;
                    if (!fillable[nid]) continue;
                    float step = 1f
                        + (signals[cur].BiomeId != signals[nid].BiomeId ? BiomeSnapWeight : 0f)
                        + System.Math.Abs(signals[cur].ForestBucket - signals[nid].ForestBucket) * ForestSnapWeight;
                    float nc = cc + step;
                    // Relax on lower cost; on a tie prefer the smaller owning-anchor id for determinism.
                    if (nc < cost[nid] || (nc == cost[nid] && owner[cur] < owner[nid]))
                    {
                        cost[nid] = nc;
                        owner[nid] = owner[cur];
                        heap.Push(nid, nc);
                    }
                }
            }

            // Group tiles by owning anchor.
            var groupByAnchor = new Dictionary<int, List<int>>();
            for (int t = 0; t < total; t++)
            {
                if (!fillable[t] || owner[t] < 0) continue;
                if (!groupByAnchor.TryGetValue(owner[t], out var g)) { g = new List<int>(); groupByAnchor[owner[t]] = g; }
                g.Add(t);
            }
            foreach (var kv in groupByAnchor) result.Add(kv.Value);

            AssignNeckTiles(grid, isLand, isNeck, result, total);
            return result;
        }

        // Small surcharges (distance-dominant) that let a border SNAP onto a nearby biome / forest edge
        // without chasing it — the hybrid of clean convex cells and terrain-faithful borders (#20).
        private const float BiomeSnapWeight = 0.35f;
        private const float ForestSnapWeight = 0.15f;

        /// <summary>
        /// One anchor per ~<paramref name="target"/> tiles, chosen per connected land component by
        /// farthest-point sampling so region centres spread evenly and every component (island included)
        /// gets at least one. Components are the maximal runs of fillable land — bounded only by hard
        /// walls, since biomes no longer cut them. Returned in ascending id order for determinism.
        /// </summary>
        private static List<int> SelectAnchors(WorldGrid grid, bool[] fillable, int total, int target)
        {
            var anchors = new List<int>();
            var seen = new bool[total];
            var nb = new List<PlanetTile>();
            for (int s = 0; s < total; s++)
            {
                if (!fillable[s] || seen[s]) continue;
                var comp = new List<int>();
                var q = new Queue<int>();
                q.Enqueue(s); seen[s] = true;
                while (q.Count > 0)
                {
                    int c = q.Dequeue();
                    comp.Add(c);
                    nb.Clear();
                    grid.GetTileNeighbors(c, nb);
                    foreach (var n in nb)
                    {
                        int nid = n.tileId;
                        if (fillable[nid] && !seen[nid]) { seen[nid] = true; q.Enqueue(nid); }
                    }
                }
                int k = System.Math.Max(1, (int)System.Math.Round((double)comp.Count / target));
                anchors.AddRange(FarthestPointAnchors(grid, comp, k));
            }
            anchors.Sort();
            return anchors;
        }

        /// <summary>Pick <paramref name="k"/> farthest-point anchors from a tile set: the first is the
        /// smallest id (determinism), each subsequent is the tile maximising the minimum distance to
        /// those already chosen. Uses a running per-tile min-distance array so it is O(area * k) — the
        /// naive re-scan is O(area * k^2) and hangs a big landmass with hundreds of anchors.</summary>
        private static List<int> FarthestPointAnchors(WorldGrid grid, List<int> tiles, int k)
        {
            var sorted = new List<int>(tiles); sorted.Sort();
            int n = sorted.Count;
            var anchors = new List<int> { sorted[0] };
            var minD = new float[n];
            for (int i = 0; i < n; i++) minD[i] = grid.ApproxDistanceInTiles(sorted[i], sorted[0]);
            while (anchors.Count < k)
            {
                int bi = -1; float bd = -1f;
                for (int i = 0; i < n; i++) { if (minD[i] > bd) { bd = minD[i]; bi = i; } }
                if (bi < 0 || bd <= 0f) break;   // no tile left with positive distance to all anchors
                int a = sorted[bi];
                anchors.Add(a);
                minD[bi] = 0f;
                for (int i = 0; i < n; i++)
                {
                    if (minD[i] <= 0f) continue;
                    float d = grid.ApproxDistanceInTiles(sorted[i], a);
                    if (d < minD[i]) minD[i] = d;
                }
            }
            return anchors;
        }

        /// <summary>Binary min-heap keyed by float priority — Dijkstra's frontier. .NET Framework has no
        /// built-in PriorityQueue, and the list-scan queue used for the small per-cell fills is O(n) per
        /// pop, far too slow for a whole-world Dijkstra.</summary>
        private sealed class MinHeap
        {
            private int[] items;
            private float[] prio;
            private int count;
            public MinHeap(int cap) { cap = System.Math.Max(cap, 16); items = new int[cap]; prio = new float[cap]; }
            public int Count { get { return count; } }
            public void Push(int item, float p)
            {
                if (count == items.Length) { System.Array.Resize(ref items, count * 2); System.Array.Resize(ref prio, count * 2); }
                int i = count++;
                items[i] = item; prio[i] = p;
                while (i > 0) { int par = (i - 1) / 2; if (prio[par] <= prio[i]) break; Swap(i, par); i = par; }
            }
            public void Pop(out int item, out float p)
            {
                item = items[0]; p = prio[0];
                count--;
                items[0] = items[count]; prio[0] = prio[count];
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = 2 * i + 2, m = i;
                    if (l < count && prio[l] < prio[m]) m = l;
                    if (r < count && prio[r] < prio[m]) m = r;
                    if (m == i) break;
                    Swap(i, m); i = m;
                }
            }
            private void Swap(int a, int b)
            {
                int ti = items[a]; items[a] = items[b]; items[b] = ti;
                float tp = prio[a]; prio[a] = prio[b]; prio[b] = tp;
            }
        }

        /// <summary>
        /// Hand the excluded pass-neck tiles back to the flanking basins (#20): each neck tile joins the
        /// group most of its already-assigned neighbours belong to, so the pass splits down the middle
        /// between the two provinces it connects and no tile is left unassigned. Iterated so a
        /// multi-tile neck resolves inward from both ends; ties break to the lower group index for
        /// determinism.
        /// </summary>
        private static void AssignNeckTiles(WorldGrid grid, bool[] isLand, bool[] isNeck, List<List<int>> groups, int total)
        {
            var groupOf = new Dictionary<int, int>();
            for (int g = 0; g < groups.Count; g++)
                foreach (int t in groups[g]) groupOf[t] = g;

            var pending = new List<int>();
            for (int t = 0; t < total; t++) if (isLand[t] && isNeck[t] && !groupOf.ContainsKey(t)) pending.Add(t);

            var neighbors = new List<PlanetTile>();
            var counts = new Dictionary<int, int>();
            int safety = 0;
            while (pending.Count > 0 && safety++ < 16)
            {
                var next = new List<int>();
                bool any = false;
                foreach (int t in pending)
                {
                    neighbors.Clear();
                    grid.GetTileNeighbors(t, neighbors);
                    counts.Clear();
                    int best = -1, bestCount = 0;
                    foreach (var n in neighbors)
                    {
                        if (!groupOf.TryGetValue(n.tileId, out int g)) continue;
                        int c; counts.TryGetValue(g, out c); c++; counts[g] = c;
                        if (c > bestCount || (c == bestCount && g < best)) { bestCount = c; best = g; }
                    }
                    if (best >= 0) { groups[best].Add(t); groupOf[t] = best; any = true; }
                    else next.Add(t);
                }
                pending = next;
                if (!any) break;
            }
            // Any neck tile still unplaced (ringed only by walls/necks) is left for AbsorbEnclosedGaps.
        }

        // Pass-neck detection. K=3 tiles matches the agreed "within two or three tiles of the next
        // border"; the dot cutoff means the two flanking walls point more than ~105 degrees apart, i.e.
        // they pinch the tile from genuinely opposing sides rather than lying on a single flank. A tile
        // counts as a flanking "wall" if it is a hard border (water / impassable) OR high ground
        // (LargeHills+) — because a RimWorld mountain range is mostly PASSABLE Mountainous tiles, so a
        // pass is a low saddle between high ground, not between impassable peaks.
        private const int NeckRadius = 3;
        private const float NeckOppositeDot = -0.25f;
        private const int NeckWallHillClass = 2;   // LargeHills and above flank a pass
        private const int NeckLowHillClass = 1;    // only Flat / SmallHills tiles can BE a saddle

        /// <summary>
        /// Flag low saddle tiles pinched between high ground / hard walls on opposite sides — mountain
        /// passes and isthmuses (#20). Only a low tile (Flat/SmallHills) is a candidate; a bounded BFS
        /// (depth <see cref="NeckRadius"/>, travelling only over other low land) collects the bearings
        /// to any flanking wall — water, impassable, or high ground — it reaches, and if two bearings
        /// oppose each other the tile sits in a neck and becomes an extension of the border. High ground
        /// on only one flank (a foothill where a plain meets a range) is never flagged, so a region
        /// still flows up into the mountains; a plateau interior is excluded because it is not low.
        /// </summary>
        private static bool[] MarkPassNecks(WorldGrid grid, bool[] isLand, TileSignal[] signals, int total)
        {
            var isNeck = new bool[total];
            var neighbors = new List<PlanetTile>();
            var dirs = new List<UnityEngine.Vector3>();
            var depth = new Dictionary<int, int>();
            var q = new Queue<int>();
            for (int t = 0; t < total; t++)
            {
                // Only a low, passable land tile can be a saddle.
                if (!isLand[t] || signals[t].HillClass > NeckLowHillClass) continue;

                dirs.Clear(); depth.Clear(); q.Clear();
                q.Enqueue(t); depth[t] = 0;
                UnityEngine.Vector3 ct = grid.GetTileCenter(t);
                bool neck = false;
                while (q.Count > 0 && !neck)
                {
                    int cur = q.Dequeue();
                    int d = depth[cur];
                    neighbors.Clear();
                    grid.GetTileNeighbors(cur, neighbors);
                    foreach (var n in neighbors)
                    {
                        int nid = n.tileId;
                        bool flankWall = !isLand[nid] || signals[nid].HillClass >= NeckWallHillClass;
                        if (flankWall)
                        {
                            // A flanking wall reached within the radius: note its bearing.
                            UnityEngine.Vector3 dir = grid.GetTileCenter(nid) - ct;
                            if (dir.sqrMagnitude < 1e-6f) continue;
                            dir = dir.normalized;
                            foreach (var e in dirs) { if (UnityEngine.Vector3.Dot(e, dir) < NeckOppositeDot) { neck = true; break; } }
                            if (neck) break;
                            dirs.Add(dir);
                        }
                        else if (d < NeckRadius && !depth.ContainsKey(nid))
                        {
                            // Traverse only low land while measuring the saddle's width.
                            depth[nid] = d + 1;
                            q.Enqueue(nid);
                        }
                    }
                }
                isNeck[t] = neck;
            }
            return isNeck;
        }

        /// <summary>Reduce a tile to the classified signals the boundary rules read.</summary>
        private static TileSignal Classify(WorldGrid grid, int tileId, Dictionary<BiomeDef, int> biomeIds)
        {
            Tile t = grid[tileId];
            BiomeDef biome = t.PrimaryBiome;

            int biomeId = 0;
            if (biome != null && !biomeIds.TryGetValue(biome, out biomeId))
            {
                biomeId = biomeIds.Count + 1;   // 0 reserved for "no biome"
                biomeIds[biome] = biomeId;
            }

            int hillClass;
            switch (t.hilliness)
            {
                case Hilliness.SmallHills: hillClass = 1; break;
                case Hilliness.LargeHills: hillClass = 2; break;
                case Hilliness.Mountainous: hillClass = 3; break;
                case Hilliness.Impassable: hillClass = 4; break;
                default: hillClass = 0; break;
            }

            float treeDensity = biome != null ? biome.TreeDensity : 0f;
            int forestBucket = treeDensity >= ThickTreeDensity ? 2 : (treeDensity >= WoodedTreeDensity ? 1 : 0);

            bool swamp = t.swampiness > 0.1f
                || (biome != null && (biome.defName.Contains("Swamp") || biome.defName.Contains("Marsh")));
            bool impassable = t.hilliness == Hilliness.Impassable
                || (biome != null && (biome.impassable || biome.defName == "SeaIce"));

            return new TileSignal
            {
                BiomeId = biomeId,
                HillClass = hillClass,
                ForestBucket = forestBucket,
                Swamp = swamp,
                Water = t.WaterCovered,
                Impassable = impassable,
                Temperature = t.temperature,
                Rainfall = t.rainfall,
            };
        }

        /// <summary>
        /// Bounding-box-free elongation of a tile set: the ratio of its two principal axes in the local
        /// tangent plane (1 = round, higher = a long ribbon). Computed by PCA over the tiles' 3D centres
        /// projected onto an east/north frame at their centroid, so it is independent of world
        /// orientation and of the equirectangular distortion a lon/lat box would carry.
        /// </summary>
        public static float Elongation(List<int> tiles)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || tiles == null || tiles.Count < 3) return 1f;

            UnityEngine.Vector3 c = UnityEngine.Vector3.zero;
            foreach (int t in tiles) c += grid.GetTileCenter(t);
            c /= tiles.Count;

            UnityEngine.Vector3 up = c.normalized;
            UnityEngine.Vector3 refA = UnityEngine.Mathf.Abs(UnityEngine.Vector3.Dot(up, UnityEngine.Vector3.up)) > 0.99f
                ? UnityEngine.Vector3.right : UnityEngine.Vector3.up;
            UnityEngine.Vector3 east = UnityEngine.Vector3.Cross(up, refA).normalized;
            UnityEngine.Vector3 north = UnityEngine.Vector3.Cross(east, up).normalized;

            double sxx = 0, syy = 0, sxy = 0;
            foreach (int t in tiles)
            {
                UnityEngine.Vector3 d = grid.GetTileCenter(t) - c;
                double x = UnityEngine.Vector3.Dot(d, east), y = UnityEngine.Vector3.Dot(d, north);
                sxx += x * x; syy += y * y; sxy += x * y;
            }
            double n = tiles.Count;
            double a = sxx / n, b = sxy / n, cc = syy / n;
            double tr = a + cc, det = a * cc - b * b;
            double disc = System.Math.Sqrt(System.Math.Max(0, tr * tr / 4 - det));
            double l1 = tr / 2 + disc, l2 = tr / 2 - disc;
            if (l2 <= 1e-6) return 6f;
            return (float)System.Math.Sqrt(l1 / l2);
        }

        /// <summary>
        /// Split a connected tile set into <paramref name="pieces"/> compact groups: farthest-point
        /// anchors (which land at the extremes of the long axis) claimed by a hop-count watershed, so an
        /// elongated province divides across its short axis into blob-like halves. Deterministic (anchors
        /// resolved in id order). Returns the whole set unsplit if pieces &lt;= 1 or it is too small.
        /// </summary>
        public static List<List<int>> SplitTiles(List<int> tiles, int pieces)
        {
            WorldGrid grid = Find.WorldGrid;
            if (grid == null || pieces <= 1 || tiles == null || tiles.Count < pieces)
                return new List<List<int>> { tiles };

            var set = new HashSet<int>(tiles);
            var sorted = new List<int>(tiles); sorted.Sort();

            // Farthest-point sampling for the anchors.
            var anchors = new List<int> { sorted[0] };
            var neigh = new List<PlanetTile>();
            while (anchors.Count < pieces)
            {
                int best = -1; float bestD = -1f;
                foreach (int t in sorted)
                {
                    if (anchors.Contains(t)) continue;
                    float md = float.MaxValue;
                    foreach (int a in anchors) { float d = grid.ApproxDistanceInTiles(t, a); if (d < md) md = d; }
                    if (md > bestD) { bestD = md; best = t; }
                }
                if (best == -1) break;
                anchors.Add(best);
            }
            anchors.Sort();

            // Multi-source hop-count BFS confined to the set; nearest anchor wins, ties to smaller id.
            var owner = new Dictionary<int, int>();
            var dist = new Dictionary<int, int>();
            var q = new Queue<int>();
            foreach (int a in anchors) { owner[a] = a; dist[a] = 0; q.Enqueue(a); }
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                neigh.Clear(); grid.GetTileNeighbors(cur, neigh);
                foreach (var nb in neigh)
                {
                    int nid = nb.tileId;
                    if (!set.Contains(nid)) continue;
                    int nd = dist[cur] + 1;
                    if (!dist.TryGetValue(nid, out int old) || nd < old || (nd == old && owner[cur] < owner[nid]))
                    {
                        dist[nid] = nd; owner[nid] = owner[cur]; q.Enqueue(nid);
                    }
                }
            }

            var groups = new Dictionary<int, List<int>>();
            foreach (int a in anchors) groups[a] = new List<int>();
            foreach (int t in tiles) { int o = owner.TryGetValue(t, out int oo) ? oo : anchors[0]; groups[o].Add(t); }
            var outG = new List<List<int>>();
            foreach (int a in anchors) if (groups[a].Count > 0) outG.Add(groups[a]);
            return outG;
        }

    }
}
