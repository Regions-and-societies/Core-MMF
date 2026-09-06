using System;
using System.Collections.Generic;

namespace RegionsAndSocieties.Placement
{
    /// <summary>The broad kind of a faction as far as territory clustering is concerned (#46).</summary>
    public enum FactionKind
    {
        Other = 0,    // civil unions, traders, salvagers, spacer factions: one contiguous nation
        Pirate,       // pirate gangs and waster bands: scattered hideouts
        Empire,       // the shattered Empire: a few small holdings
        Tribe,        // tribes, fierce and gentle
        RoughUnion,   // rough outlander unions
    }

    /// <summary>
    /// Territory clustering (#46): how many territories a faction's holdings may cluster together — the
    /// largest contiguous body it builds. A discrete cap of 1, 3, 5, 7 or 9+ (9 = no limit). The cap is a
    /// <b>soft maximum</b>: placement ranks candidates that keep every body within the cap ahead of any
    /// candidate that would overflow one, whatever the land is worth, and falls back to overflow only when
    /// nothing else is left. Segmented factions (small caps) are seeded first so they can find isolated
    /// ground before the map fills. Pure: numbers and ids in, ranks out.
    /// </summary>
    public static class ClusteringRules
    {
        /// <summary>The "9+" stop: no cap at all.</summary>
        public const int Unbounded = 9;
        public static readonly int[] Stops = { 1, 3, 5, 7, Unbounded };

        // RimWorld's TechLevel ordinals: Undefined 0, Animal 1, Neolithic 2, Medieval 3, Industrial 4, Spacer 5 ...
        public const int TechIndustrial = 4;

        public static bool IsUnbounded(int cap) { return cap >= Unbounded; }

        /// <summary>Snap any value onto the nearest slider stop; non-positive (unset) reads as unbounded.</summary>
        public static int Snap(int value)
        {
            if (value <= 0) return Unbounded;
            if (value >= Unbounded) return Unbounded;
            int best = Stops[0];
            int bestDist = Math.Abs(value - best);
            for (int i = 1; i < Stops.Length; i++)
            {
                int d = Math.Abs(value - Stops[i]);
                if (d < bestDist) { best = Stops[i]; bestDist = d; }
            }
            return best;
        }

        public static string Label(int cap)
        {
            return IsUnbounded(cap) ? "9+ (no limit)" : cap.ToString();
        }

        /// <summary>
        /// Which kind a faction is, from its def: pirates by name (the same test placement has always
        /// used, so waster bands count), the Empire by defName, tribes by tech, rough unions by name or by
        /// being an industrial faction hostile to factionless humanlikes without being a permanent enemy.
        /// </summary>
        public static FactionKind ClassifyKind(string defName, string label, int techLevel, bool permanentEnemy, bool hostileToFactionless)
        {
            string dn = defName ?? string.Empty;
            string lb = label ?? string.Empty;
            if (dn.IndexOf("pirate", StringComparison.OrdinalIgnoreCase) >= 0 || lb.IndexOf("pirate", StringComparison.OrdinalIgnoreCase) >= 0)
                return FactionKind.Pirate;
            if (dn == "Empire") return FactionKind.Empire;
            if (techLevel < TechIndustrial) return FactionKind.Tribe;
            if (dn.IndexOf("Rough", StringComparison.OrdinalIgnoreCase) >= 0) return FactionKind.RoughUnion;
            if (techLevel == TechIndustrial && hostileToFactionless && !permanentEnemy) return FactionKind.RoughUnion;
            return FactionKind.Other;
        }

        /// <summary>The owner's defaults: pirates and the shattered Empire 3, tribes 5, rough unions 7,
        /// everyone else no limit.</summary>
        public static int DefaultClusterSize(FactionKind kind)
        {
            switch (kind)
            {
                case FactionKind.Pirate: return 3;
                case FactionKind.Empire: return 3;
                case FactionKind.Tribe: return 5;
                case FactionKind.RoughUnion: return 7;
                default: return Unbounded;
            }
        }

        /// <summary>The body a candidate would form: itself plus every body it touches (touching two
        /// bodies merges them through it).</summary>
        public static int MergedSize(IEnumerable<int> touchedBodySizes)
        {
            int size = 1;
            if (touchedBodySizes != null) foreach (int s in touchedBodySizes) size += s;
            return size;
        }

        public static bool WithinCap(int mergedSize, int cap)
        {
            return IsUnbounded(cap) || mergedSize <= cap;
        }

        /// <summary>Primary sort key for candidates: within-cap ones (0) rank ahead of overflow ones (1).</summary>
        public static int CapRank(bool withinCap) { return withinCap ? 0 : 1; }

        /// <summary>Primary seeding key: ascending cluster size, so the most segmented factions place first.</summary>
        public static int SeedingKey(int clusterSize) { return Snap(clusterSize); }
    }

    /// <summary>
    /// A faction's bodies of territory: the connected components of its provinces under land
    /// adjacency (#46). Provinces are ids; adjacency comes from the caller (province border shares), so
    /// this is pure and order-independent — adding provinces in any order yields the same bodies.
    /// </summary>
    public sealed class TerritoryBodies
    {
        private readonly Dictionary<int, int> bodyOf = new Dictionary<int, int>();
        private readonly List<HashSet<int>> bodies = new List<HashSet<int>>();   // null once merged away

        public int ProvinceCount { get { return bodyOf.Count; } }

        public int Count
        {
            get { int n = 0; foreach (var b in bodies) if (b != null) n++; return n; }
        }

        public int Largest
        {
            get { int n = 0; foreach (var b in bodies) if (b != null && b.Count > n) n = b.Count; return n; }
        }

        public bool Contains(int provinceId) { return bodyOf.ContainsKey(provinceId); }

        /// <summary>The distinct bodies among a set of neighbouring province ids.</summary>
        public List<int> TouchedBodies(IEnumerable<int> neighbourIds)
        {
            var touched = new List<int>();
            if (neighbourIds == null) return touched;
            foreach (int n in neighbourIds)
            {
                if (bodyOf.TryGetValue(n, out int b) && !touched.Contains(b)) touched.Add(b);
            }
            return touched;
        }

        /// <summary>The size of the body a new province with these neighbours would form.</summary>
        public int MergedSizeIfAdded(IEnumerable<int> neighbourIds)
        {
            var sizes = new List<int>();
            foreach (int b in TouchedBodies(neighbourIds)) sizes.Add(bodies[b].Count);
            return ClusteringRules.MergedSize(sizes);
        }

        /// <summary>Add a province, merging every body it touches into one.</summary>
        public void Add(int provinceId, IEnumerable<int> neighbourIds)
        {
            if (bodyOf.ContainsKey(provinceId)) return;
            var touched = TouchedBodies(neighbourIds);
            if (touched.Count == 0)
            {
                bodies.Add(new HashSet<int> { provinceId });
                bodyOf[provinceId] = bodies.Count - 1;
                return;
            }
            int target = touched[0];
            for (int i = 1; i < touched.Count; i++)
            {
                int other = touched[i];
                foreach (int p in bodies[other]) { bodies[target].Add(p); bodyOf[p] = target; }
                bodies[other] = null;
            }
            bodies[target].Add(provinceId);
            bodyOf[provinceId] = target;
        }
    }
}
