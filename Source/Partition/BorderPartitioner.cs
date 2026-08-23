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
            var weights = BoundaryWeights.Default;

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

            // Mountain passes / isthmuses: a passable tile pinched between two hard walls on OPPOSITE
            // sides within a few tiles is a neck, and a neck is an extension of the hard border (#20).
            // Excluded from the cell flood so the two basins it joins fall into separate cells; the neck
            // tiles themselves are handed back to the flanking basins afterwards, so the border runs
            // through the pass and coverage stays complete.
            // Pass-neck detection is gated OFF pending a selective saddle/bottleneck rule: the
            // opposite-sides-within-K primitive over-fires massively in rolling terrain (~37% of land),
            // fragmenting the map. The detector is kept for iteration against the fixed test world.
            var isNeck = EnableNeckDetection ? MarkPassNecks(grid, isLand, signals, total) : new bool[total];
            if (EnableNeckDetection)
            {
                int neckCount = 0; for (int i = 0; i < total; i++) if (isNeck[i]) neckCount++;
                Log.Message($"[RegionsAndSocieties] Border-first: {neckCount} pass-neck tiles walled off.");
            }

            // Wall-flood into cells: connect two adjacent floodable tiles only across a non-wall edge,
            // so each connected component is bounded by coasts, ridges, biome edges, forest bands and
            // pass necks.
            var cellVisited = new bool[total];
            var neighbors = new List<PlanetTile>();
            for (int seed = 0; seed < total; seed++)
            {
                if (!isLand[seed] || isNeck[seed] || cellVisited[seed]) continue;

                var cell = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(seed);
                cellVisited[seed] = true;
                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    cell.Add(cur);
                    neighbors.Clear();
                    grid.GetTileNeighbors(cur, neighbors);
                    foreach (var n in neighbors)
                    {
                        int nid = n.tileId;
                        if (!isLand[nid] || isNeck[nid] || cellVisited[nid]) continue;
                        float strength = BorderRules.BoundaryStrength(signals[cur], signals[nid], weights);
                        if (BorderRules.IsWall(strength, BorderRules.DefaultWallThreshold)) continue; // border edge
                        cellVisited[nid] = true;
                        queue.Enqueue(nid);
                    }
                }

                if (cell.Count <= maxRegionTiles)
                {
                    result.Add(cell);            // within the size band: kept whole, size follows terrain
                }
                else
                {
                    result.AddRange(SubdivideCell(grid, cell, signals, weights, maxRegionTiles));
                }
            }

            AssignNeckTiles(grid, isLand, isNeck, result, total);
            return result;
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
        /// Split an oversized cell into basins. River tiles seed the markers (a province per river
        /// system); a featureless cell with no river is split into evenly-spaced anchors. A
        /// marker-controlled watershed — least accumulated <c>1 + boundary strength</c> — then assigns
        /// every tile to its nearest marker, so the divides fall on the strongest interior high ground.
        /// </summary>
        private static List<List<int>> SubdivideCell(
            WorldGrid grid, List<int> cell, TileSignal[] signals, BoundaryWeights weights, int maxRegionTiles)
        {
            var markers = SelectMarkers(grid, cell, maxRegionTiles);
            if (markers.Count <= 1)
            {
                return new List<List<int>> { cell };   // a single natural basin: kept whole
            }

            var cellSet = new HashSet<int>(cell);
            var owner = new Dictionary<int, int>();       // tile -> marker id
            var cost = new Dictionary<int, float>();
            var pq = new SimplePriorityQueue<int>();
            foreach (int m in markers)
            {
                owner[m] = m;
                cost[m] = 0f;
                pq.Enqueue(m, 0f);
            }

            var neighbors = new List<PlanetTile>();
            while (pq.Count > 0)
            {
                int cur = pq.Dequeue();
                float curCost = cost[cur];
                int curOwner = owner[cur];

                neighbors.Clear();
                grid.GetTileNeighbors(cur, neighbors);
                foreach (var n in neighbors)
                {
                    int nid = n.tileId;
                    if (!cellSet.Contains(nid)) continue;
                    float step = 1f + BorderRules.BoundaryStrength(signals[cur], signals[nid], weights);
                    float nc = curCost + step;
                    // Relax on strictly-lower cost; on an exact tie prefer the smaller marker id so the
                    // partition is regenerate-identical regardless of dequeue order.
                    if (!cost.TryGetValue(nid, out float existing)
                        || nc < existing
                        || (nc == existing && curOwner < owner[nid]))
                    {
                        cost[nid] = nc;
                        owner[nid] = curOwner;
                        pq.Enqueue(nid, nc);
                    }
                }
            }

            var groups = new Dictionary<int, List<int>>();
            foreach (int m in markers) groups[m] = new List<int>();
            foreach (int t in cell)
            {
                int o = owner.TryGetValue(t, out int oo) ? oo : markers[0];
                groups[o].Add(t);
            }

            var outGroups = new List<List<int>>();
            foreach (int m in markers)
            {
                if (groups[m].Count > 0) outGroups.Add(groups[m]);
            }
            return outGroups;
        }

        /// <summary>
        /// Choose the watershed markers for an oversized cell. Each connected river system in the cell
        /// contributes one marker (basins centre on rivers); if the cell still wants more basins than
        /// its rivers provide — or has no river at all — the rest are farthest-point anchors so an open
        /// basin divides evenly. Marker ids are the seeding tile ids, returned in ascending order for
        /// determinism.
        /// </summary>
        private static List<int> SelectMarkers(WorldGrid grid, List<int> cell, int maxRegionTiles)
        {
            int desired = BorderRules.AnchorCount(cell.Count, maxRegionTiles);

            var markers = new List<int>();
            var claimed = new HashSet<int>();

            // River systems: cluster connected river tiles, one marker (smallest tile id) per cluster.
            var cellSet = new HashSet<int>(cell);
            var riverVisited = new HashSet<int>();
            var neighbors = new List<PlanetTile>();
            var sortedCell = new List<int>(cell);
            sortedCell.Sort();
            foreach (int t in sortedCell)
            {
                if (riverVisited.Contains(t) || !HasRiver(grid, t)) continue;

                int rep = t;
                var q = new Queue<int>();
                q.Enqueue(t);
                riverVisited.Add(t);
                while (q.Count > 0)
                {
                    int cur = q.Dequeue();
                    if (cur < rep) rep = cur;
                    claimed.Add(cur);
                    neighbors.Clear();
                    grid.GetTileNeighbors(cur, neighbors);
                    foreach (var n in neighbors)
                    {
                        int nid = n.tileId;
                        if (cellSet.Contains(nid) && !riverVisited.Contains(nid) && HasRiver(grid, nid))
                        {
                            riverVisited.Add(nid);
                            q.Enqueue(nid);
                        }
                    }
                }
                markers.Add(rep);
            }

            // Enough basins already? A cell with more river systems than the size guide asks for keeps
            // one basin per river — its size is set by geography, which is the intent.
            if (markers.Count >= desired) return Sorted(markers);

            // Supplement with farthest-point anchors so an open (or under-seeded) basin divides evenly.
            foreach (int m in markers) claimed.Add(m);
            while (markers.Count < desired)
            {
                int best = -1;
                float bestDist = -1f;
                foreach (int t in sortedCell)
                {
                    if (claimed.Contains(t)) continue;
                    float minDist = float.MaxValue;
                    if (markers.Count == 0)
                    {
                        minDist = 0f;   // first anchor: any tile; ascending order makes it deterministic
                    }
                    else
                    {
                        foreach (int m in markers)
                        {
                            float d = grid.ApproxDistanceInTiles(t, m);
                            if (d < minDist) minDist = d;
                        }
                    }
                    if (minDist > bestDist)
                    {
                        bestDist = minDist;
                        best = t;
                    }
                }
                if (best == -1) break;
                markers.Add(best);
                claimed.Add(best);
            }

            return Sorted(markers);
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

        private static bool HasRiver(WorldGrid grid, int tileId)
        {
            var neighbors = new List<PlanetTile>();
            grid.GetTileNeighbors(tileId, neighbors);
            foreach (var n in neighbors)
            {
                if (grid.GetRiverDef(tileId, n.tileId) != null || grid.GetRiverDef(n.tileId, tileId) != null)
                {
                    return true;
                }
            }
            return false;
        }

        private static List<int> Sorted(List<int> ids)
        {
            ids.Sort();
            return ids;
        }
    }
}
