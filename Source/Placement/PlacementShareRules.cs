using System;
using System.Collections.Generic;

namespace RegionsAndSocieties.Placement
{
    /// <summary>
    /// The share model behind the simplified placement settings (#47): each faction stores a raw
    /// <em>share weight</em> (shown to the player as a "%"), the weights need not sum to 100, and worldgen
    /// normalises across the active factions to distribute the placed territories. A share is a weight,
    /// not a hard quota — it is the faction's slice of whatever total the density knob
    /// (<c>claimedLandAreaPercent</c>) allows. Pure and unit-tested; the dialog, the debug report and the
    /// worldgen distributor all read the SAME arithmetic here so their numbers agree.
    /// </summary>
    public static class PlacementShareRules
    {
        /// <summary>The share weight a fresh profile carries when it has none yet (0 = unset). A neutral
        /// middle weight so an untouched faction sits at "one equal slice" until the player moves it — the
        /// same magnitude the old default Settlement Range midpoint (5..15) produced.</summary>
        public const float DefaultShareWeight = 10f;

        /// <summary>Convert an old <c>baseCountRange</c> (min..max) to a share weight: the midpoint. Relative
        /// proportions between faction kinds are preserved (a hostile 3..8 → 5.5 stays smaller than a
        /// civil 5..15 → 10), which is exactly what a normalised share needs. Never negative.</summary>
        public static float MigrateRangeToShareWeight(int min, int max)
        {
            float mid = (min + max) / 2f;
            return mid < 0f ? 0f : mid;
        }

        /// <summary>This faction's normalised fraction of the placed total: its weight over the sum of all
        /// weights. Returns 0 when the total is non-positive.</summary>
        public static float NormalizedFraction(float shareWeight, float totalWeight)
        {
            if (totalWeight <= 0f || shareWeight <= 0f) return 0f;
            return shareWeight / totalWeight;
        }

        /// <summary>The live per-faction estimate for the settings dialog: how many regions this faction is
        /// expected to receive. <paramref name="placedTotal"/> is the total number of territories worldgen
        /// will place (≈ expected land regions × claimed-land-area fraction). Rounded, never negative.</summary>
        public static int EstimatedFactionCount(float shareWeight, float totalWeight, int placedTotal)
        {
            if (placedTotal <= 0) return 0;
            float frac = NormalizedFraction(shareWeight, totalWeight);
            int n = (int)Math.Round(frac * placedTotal);
            return n < 0 ? 0 : n;
        }

        /// <summary>The number of territories placed in total: the expected land-region count scaled by the
        /// claimed-land-area fraction (the density knob). This is the denominator every per-faction estimate
        /// shares. Rounded, never negative.</summary>
        public static int PlacedTotal(int expectedRegions, float claimedLandAreaFraction)
        {
            if (expectedRegions <= 0) return 0;
            if (claimedLandAreaFraction < 0f) claimedLandAreaFraction = 0f;
            if (claimedLandAreaFraction > 1f) claimedLandAreaFraction = 1f;
            int n = (int)Math.Round(expectedRegions * (double)claimedLandAreaFraction);
            return n < 0 ? 0 : n;
        }

        /// <summary>Apportion <paramref name="total"/> whole territories across the given share weights by
        /// the largest-remainder (Hamilton) method, so the counts sum to exactly <paramref name="total"/>
        /// and each faction lands within one of its ideal share. Weights ≤ 0 are treated as 0 (no slice).
        /// The result is index-aligned with <paramref name="weights"/>. Used by the debug share report so its
        /// "estimate" column reproduces worldgen's distribution exactly.</summary>
        public static int[] Apportion(IList<float> weights, int total)
        {
            int n = weights?.Count ?? 0;
            var result = new int[n];
            if (n == 0 || total <= 0) return result;

            float sum = 0f;
            for (int i = 0; i < n; i++) if (weights[i] > 0f) sum += weights[i];
            if (sum <= 0f) return result;

            // Floor each ideal share; track the fractional remainders to hand out the leftover seats.
            var remainder = new double[n];
            int assigned = 0;
            for (int i = 0; i < n; i++)
            {
                double ideal = weights[i] > 0f ? weights[i] / sum * total : 0.0;
                int floor = (int)Math.Floor(ideal);
                result[i] = floor;
                remainder[i] = ideal - floor;
                assigned += floor;
            }

            int leftover = total - assigned;
            // Give the remaining seats to the largest remainders, one each, highest first.
            while (leftover > 0)
            {
                int best = -1;
                double bestRem = double.NegativeInfinity;
                for (int i = 0; i < n; i++)
                {
                    if (remainder[i] > bestRem)
                    {
                        bestRem = remainder[i];
                        best = i;
                    }
                }
                if (best < 0) break;
                result[best]++;
                remainder[best] = double.NegativeInfinity; // one seat per faction per round
                leftover--;

                // If every faction has taken a leftover seat and seats remain, reset for another round.
                if (leftover > 0)
                {
                    bool anyLeft = false;
                    for (int i = 0; i < n; i++) if (remainder[i] > double.NegativeInfinity) { anyLeft = true; break; }
                    if (!anyLeft)
                    {
                        for (int i = 0; i < n; i++) if (weights[i] > 0f) remainder[i] = weights[i] / sum;
                    }
                }
            }
            return result;
        }
    }
}
