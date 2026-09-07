using System;
using System.Collections.Generic;
using System.Linq;

namespace RegionsAndSocieties.Partition
{
    /// <summary>
    /// Shore-proportional split of an inland lake between the land regions on its shores (#48). A lake is
    /// not a border: the polities on its shores share it, so the region seam runs ACROSS the water, not
    /// around it. Each shore region's share of the lake equals its share of the shoreline — "lake
    /// dominance = number of bordering tiles" — realised as largest-remainder quotas filled by a
    /// capacity-bounded flood from each region's shore.
    ///
    /// <para>Pure by design like the rest of the Partition layer: a lake tile set, its internal adjacency,
    /// and the land regions each lake tile borders go in; a tile-to-region assignment comes out. No game
    /// state, so worldgen and any test share one rule. The BFS is grid-agnostic — a hex world and a test's
    /// hand-built adjacency run the identical code.</para>
    /// </summary>
    public static class LakeSplitRules
    {
        /// <summary>Inland water bodies of this many tiles or fewer are split among their shores; a bigger
        /// inland body stays an Ocean province — an inland sea is a genuine barrier. Tune against the
        /// quicktest world.</summary>
        public const int LakeMaxTiles = 450;

        /// <summary><c>shore_r</c>: how many lake tiles border region <c>r</c> (a lake tile touching two
        /// regions counts once for each). This is the lake-dominance measure the shares are built from.</summary>
        public static Dictionary<int, int> ShoreCounts(IEnumerable<int> lakeTiles, Dictionary<int, List<int>> shoreLabels)
        {
            var counts = new Dictionary<int, int>();
            foreach (int t in lakeTiles)
            {
                if (!shoreLabels.TryGetValue(t, out var regions) || regions == null) continue;
                var seen = new HashSet<int>();
                foreach (int r in regions)
                    if (seen.Add(r)) { int c; counts.TryGetValue(r, out c); counts[r] = c + 1; }
            }
            return counts;
        }

        /// <summary>The dominant shore region: highest shore count, ties to the lowest id.</summary>
        public static int DominantRegion(Dictionary<int, int> shoreCounts)
        {
            int best = -1, bestC = -1;
            foreach (var kv in shoreCounts.OrderBy(k => k.Key))
                if (kv.Value > bestC) { bestC = kv.Value; best = kv.Key; }
            return best;
        }

        /// <summary>
        /// Per-region tile quotas that sum EXACTLY to <paramref name="lakeTileCount"/>, proportional to
        /// shore count and rounded by largest remainder (ties to the lowest id). Floor-at-1: a region with
        /// any shore gets at least one tile when the lake is large enough to spare it, the tile taken from
        /// the dominant region's quota.
        /// </summary>
        public static Dictionary<int, int> Quotas(int lakeTileCount, Dictionary<int, int> shoreCounts)
        {
            var quota = new Dictionary<int, int>();
            if (shoreCounts.Count == 0 || lakeTileCount <= 0) return quota;
            int totalShore = shoreCounts.Values.Sum();
            if (totalShore <= 0) return quota;

            var remainders = new List<KeyValuePair<int, double>>();
            int assigned = 0;
            foreach (var kv in shoreCounts.OrderBy(k => k.Key))
            {
                double ideal = (double)lakeTileCount * kv.Value / totalShore;
                int floor = (int)Math.Floor(ideal);
                quota[kv.Key] = floor;
                assigned += floor;
                remainders.Add(new KeyValuePair<int, double>(kv.Key, ideal - floor));
            }
            int left = lakeTileCount - assigned;
            foreach (var kv in remainders.OrderByDescending(k => k.Value).ThenBy(k => k.Key))
            {
                if (left <= 0) break;
                quota[kv.Key]++;
                left--;
            }

            int dom = DominantRegion(shoreCounts);
            foreach (var kv in shoreCounts.OrderBy(k => k.Key))
            {
                if (kv.Value > 0 && quota[kv.Key] == 0 && kv.Key != dom && quota[dom] > 1)
                {
                    quota[kv.Key] = 1;
                    quota[dom]--;
                }
            }
            return quota;
        }

        /// <summary>
        /// Split the lake: every tile gets a shore region id. A capacity-bounded multi-source flood expands
        /// each region one hop-ring at a time from its shore tiles, claiming a tile only while under quota;
        /// rings are processed in shore-descending, then id order for determinism. Tiles walled off behind
        /// saturated neighbours (leftovers) flood inward from the assigned boundary, preferring the
        /// neighbouring region with the most spare quota; anything still stranded goes to the dominant
        /// region. Returns empty when the lake has no shore at all (the caller then leaves it as water).
        /// </summary>
        public static Dictionary<int, int> Split(List<int> lakeTiles,
            Dictionary<int, List<int>> lakeAdjacency, Dictionary<int, List<int>> shoreLabels)
        {
            var assign = new Dictionary<int, int>();
            var shore = ShoreCounts(lakeTiles, shoreLabels);
            if (shore.Count == 0) return assign;

            int dom = DominantRegion(shore);
            var quota = Quotas(lakeTiles.Count, shore);
            var remaining = new Dictionary<int, int>(quota);

            var priority = shore.Keys.OrderByDescending(r => shore[r]).ThenBy(r => r).ToList();
            var prioIndex = new Dictionary<int, int>();
            for (int i = 0; i < priority.Count; i++) prioIndex[priority[i]] = i;

            var frontier = new Dictionary<int, Queue<int>>();
            foreach (int r in priority) frontier[r] = new Queue<int>();
            foreach (int t in lakeTiles.OrderBy(x => x))
            {
                if (!shoreLabels.TryGetValue(t, out var regs) || regs == null) continue;
                var seen = new HashSet<int>();
                foreach (int r in regs)
                    if (seen.Add(r) && frontier.ContainsKey(r)) frontier[r].Enqueue(t);
            }

            bool progress = true;
            while (progress)
            {
                progress = false;
                foreach (int r in priority)
                {
                    var q = frontier[r];
                    int ring = q.Count;
                    var next = new List<int>();
                    for (int k = 0; k < ring; k++)
                    {
                        int t = q.Dequeue();
                        if (assign.ContainsKey(t)) continue;
                        if (remaining[r] <= 0) continue;
                        assign[t] = r; remaining[r]--; progress = true;
                        if (lakeAdjacency.TryGetValue(t, out var nbs))
                            foreach (int nb in nbs) if (!assign.ContainsKey(nb)) next.Add(nb);
                    }
                    foreach (int nb in next.OrderBy(x => x)) q.Enqueue(nb);
                }
            }

            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (int t in lakeTiles.OrderBy(x => x))
                {
                    if (assign.ContainsKey(t)) continue;
                    if (!lakeAdjacency.TryGetValue(t, out var nbs)) continue;
                    int best = -1;
                    foreach (int nb in nbs)
                    {
                        if (!assign.TryGetValue(nb, out int r)) continue;
                        if (best == -1 || Better(r, best, remaining, prioIndex)) best = r;
                    }
                    if (best != -1)
                    {
                        assign[t] = best;
                        if (remaining[best] > 0) remaining[best]--;
                        changed = true;
                    }
                }
            }

            foreach (int t in lakeTiles) if (!assign.ContainsKey(t)) assign[t] = dom;
            return assign;
        }

        private static bool Better(int r, int cur, Dictionary<int, int> remaining, Dictionary<int, int> prio)
        {
            bool rSpare = remaining[r] > 0, cSpare = remaining[cur] > 0;
            if (rSpare != cSpare) return rSpare;
            if (remaining[r] != remaining[cur]) return remaining[r] > remaining[cur];
            return prio[r] < prio[cur];
        }
    }
}
