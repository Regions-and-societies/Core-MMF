using System;

namespace RegionsAndSocieties.Sizing
{
    /// <summary>
    /// Birthrate-informed growth of a settlement's modeled population (#6). Where
    /// <see cref="PopulationCapRules.StepToward"/> is a fixed-size hysteresis step, this gives the drift
    /// a cause: a settlement grows toward its target (⅔ of cap) on a <b>demographic</b> curve — slow
    /// when tiny, fastest in the middle, tapering as it fills — at a rate set by the faction's
    /// tech-level birthrate. A settlement over its target (its tier dropped, or its region can no longer
    /// feed it) shrinks back toward it on the same curve.
    ///
    /// <para>Logistic by construction: <c>dP = rate · P · (1 − P/K) · Δyears</c> toward carrying
    /// capacity <c>K</c> (the target), clamped to <c>[0, max]</c> and never overshooting <c>K</c> in a
    /// step, so it settles instead of oscillating. Pure — plain numbers in and out, no game state, no
    /// Unity — so it is unit-tested without a game and the same call drives both the live tick and the
    /// debug fast-forward. For the player this only informs R&amp;T's model; it never adds or removes
    /// real colonists (that gate lives in the caller).</para>
    /// </summary>
    public static class BirthrateRules
    {
        /// <summary>A modeled settlement holds at least this population, so logistic growth can start
        /// from a fresh (near-zero) settlement instead of being stuck at zero.</summary>
        public const float SeedFloor = 1f;

        /// <summary>
        /// Annual fractional growth rate for a faction of the given tech level (RimWorld
        /// <c>TechLevel</c> as an int: 2 Neolithic … 5 Spacer). Pre-industrial societies model a higher
        /// natural birthrate; spacer/ultra populations grow slowly. First-pass values, tunable.
        /// </summary>
        public static float AnnualGrowthRate(int techLevel)
        {
            switch (techLevel)
            {
                case 2: return 0.030f;   // Neolithic
                case 3: return 0.024f;   // Medieval
                case 4: return 0.016f;   // Industrial
                case 5: return 0.011f;   // Spacer
                case 6:
                case 7: return 0.008f;   // Ultra / Archotech
                default: return 0.020f;  // Undefined / Animal / anything else
            }
        }

        /// <summary>
        /// Advance a modeled population one step toward <paramref name="carryingCapacity"/> (the target)
        /// over <paramref name="yearsElapsed"/> years, at <paramref name="annualRate"/>, clamped to
        /// <c>[0, <paramref name="max"/>]</c>. Logistic: fastest mid-range, tapering to zero at the
        /// capacity; a population above the capacity decays toward it. Never crosses the capacity within
        /// a single step (settles, no oscillation) and never leaves <c>[0, max]</c>.
        /// </summary>
        public static float GrowStep(float current, int carryingCapacity, int max, float annualRate, float yearsElapsed)
        {
            if (max < 0) max = 0;
            float p = current < 0f ? 0f : current;

            // No capacity or no elapsed time: nothing to drift toward / nothing happens. Still clamp.
            if (carryingCapacity <= 0 || yearsElapsed <= 0f || annualRate == 0f)
            {
                return Clamp(p, 0f, max);
            }

            float k = carryingCapacity;
            if (p < SeedFloor) p = SeedFloor;   // seed so a near-zero settlement can begin growing

            float dP = annualRate * p * (1f - p / k) * yearsElapsed;
            float next = p + dP;

            // Do not overshoot the capacity in one step, in either direction — this is what makes the
            // approach monotonic and self-settling rather than oscillating on a large time step.
            if (p <= k) { if (next > k) next = k; }
            else { if (next < k) next = k; }

            return Clamp(next, 0f, max);
        }

        private static float Clamp(float v, float lo, float hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
