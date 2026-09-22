using System;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>
    /// Per-settlement demographic variation (#74). A pressure source used to project its <b>faction's</b>
    /// make-up, so every settlement of a faction broadcast an identical population and the demographic map
    /// was really a faction map with a blur. This gives each settlement its OWN make-up: a deterministic
    /// perturbation of the faction baseline, seeded by the settlement, so two Empire cities a thousand tiles
    /// apart push measurably different xenotype mixes and wealth while staying recognizably Imperial.
    ///
    /// <para>Deterministic (seeded, no per-call randomness), so it is identical under full and culled source
    /// aggregation — <see cref="RegionDemographicsUtility.VerifyCulling"/> still reports zero mismatches — and
    /// stable across saves. Pure and dependency-free; the game-coupled builder combines the world seed with a
    /// settlement's tile to make the seed. The deeper "aggregate from real districts" path (Districts-EP) can
    /// override this per settlement through the hook; this is the cheap half that carries most of the fidelity.</para>
    /// </summary>
    public static class SettlementCompositionRules
    {
        /// <summary>Max fractional deviation a settlement's per-race weight takes from the faction baseline.</summary>
        public const float RaceSpread = 0.35f;

        /// <summary>Max fractional deviation on a settlement's wealth from the faction baseline.</summary>
        public const float WealthSpread = 0.20f;

        /// <summary>Max fractional deviation on a settlement's ideo weights (which of the faction's ideos lead here).</summary>
        public const float IdeoSpread = 0.45f;

        /// <summary>A deterministic multiplier in [1-spread, 1+spread] from a settlement seed and a salt.</summary>
        public static float Deviation(int settlementSeed, int salt, float spread)
        {
            uint h = Hash(settlementSeed, salt);
            float u = (h % 10000u) / 10000f;          // 0..1
            return 1f + spread * (2f * u - 1f);        // [1-spread, 1+spread]
        }

        /// <summary>
        /// Perturb a weight array for one settlement: each entry scaled by an INDEPENDENT deterministic
        /// deviation (so the mix shifts, not just the scale), clamped non-negative, then renormalised back to
        /// the array's original total so downstream weighting is unchanged in aggregate. Returns a new array;
        /// the input is untouched. A null/empty array is returned as a fresh copy.
        /// </summary>
        public static float[] PerturbWeights(float[] weights, int settlementSeed, float spread = RaceSpread)
        {
            if (weights == null) return new float[0];
            var w = new float[weights.Length];
            float orig = 0f, sum = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                orig += weights[i];
                float v = weights[i] * Deviation(settlementSeed, 101 + i, spread);
                if (v < 0f) v = 0f;
                w[i] = v;
                sum += v;
            }
            if (sum > 0f && orig > 0f)
                for (int i = 0; i < w.Length; i++) w[i] *= orig / sum;   // preserve the original total
            return w;
        }

        /// <summary>Perturb a wealth value for one settlement, deterministically, within
        /// ±<see cref="WealthSpread"/>. Never negative.</summary>
        public static int PerturbWealth(int wealth, int settlementSeed, float spread = WealthSpread)
        {
            int v = (int)Math.Round(wealth * Deviation(settlementSeed, 7, spread));
            return v < 0 ? 0 : v;
        }

        private static uint Hash(int a, int b)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)a) * 16777619u;
                h = (h ^ (uint)b) * 16777619u;
                return h;
            }
        }
    }
}
