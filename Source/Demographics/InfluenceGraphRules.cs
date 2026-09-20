using System;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>How an influence edge shapes its source before it is weighted (DEMOGRAPHIC_MODEL §2).</summary>
    public enum InfluenceCurve
    {
        /// <summary>The source value passes through unchanged.</summary>
        Linear,
        /// <summary>Diminishing returns past ~0.5: x/(1+x). A little of the source matters a lot, more matters less.</summary>
        Saturating,
        /// <summary>No effect until the source crosses a threshold, then full effect.</summary>
        Threshold,
    }

    /// <summary>The per-factor dynamics of a node in the influence graph: how fast and how reluctantly its
    /// current value chases its target. All five are tunable endpoints ported from the calibration sim.</summary>
    public struct FactorDynamics
    {
        public float velocity;     // how hard the gap pushes the velocity each year
        public float inertia;      // how much of last year's velocity carries over (momentum)
        public float deadband;     // gap must exceed this to START moving (the harder threshold)
        public float releaseBand;  // once moving, it keeps moving until the gap falls below this (easier)
        public float maxStep;      // the most a factor can move in one year

        public FactorDynamics(float velocity, float inertia, float deadband, float releaseBand, float maxStep)
        {
            this.velocity = velocity; this.inertia = inertia;
            this.deadband = deadband; this.releaseBand = releaseBand; this.maxStep = maxStep;
        }
    }

    /// <summary>The mutable state a factor carries between years: its velocity (for inertia) and whether it is
    /// currently in motion (for the start/stop hysteresis). Scribed per factor per cohort.</summary>
    public struct HysteresisState
    {
        public float velocity;
        public bool moving;
    }

    /// <summary>
    /// The pure heart of the demographic influence graph (0.5.0 keystone, #58): a node's <b>current</b> value
    /// chases its <b>target</b> with hysteresis, inertia and a per-year step cap — the stress step every mutable
    /// factor runs once per demographic year. Ported edge-for-edge from the locked calibration simulator
    /// (<c>Design/sim/graph.js</c>: <c>curve</c> / <c>stepScalar</c> / <c>stepDist</c> / <c>normalize</c>), so
    /// the weights tuned there port straight to the Defs.
    ///
    /// <para>Pure and deterministic — no game types — so it unit-tests against nothing but arithmetic. Target
    /// aggregation (reading edges, sources and baselines from the Def graph) and the per-cohort container are
    /// separate layers built on top of this; this file is only the step math.</para>
    /// </summary>
    public static class InfluenceGraphRules
    {
        /// <summary>Shape a raw source value by an edge's curve before it is weighted.</summary>
        public static float Curve(InfluenceCurve kind, float x, float threshold)
        {
            switch (kind)
            {
                case InfluenceCurve.Saturating: return x <= 0f ? x : x / (1f + x);
                case InfluenceCurve.Threshold: return x < threshold ? 0f : x;
                default: return x;   // Linear
            }
        }

        /// <summary>Convenience: a Saturating/Threshold curve at the sim's default 0.5 threshold.</summary>
        public static float Curve(InfluenceCurve kind, float x) => Curve(kind, x, 0.5f);

        /// <summary>
        /// One year's step of a scalar factor toward its (already-aggregated) target, with start/stop
        /// hysteresis and inertia, clamped to [0,1] and to <see cref="FactorDynamics.maxStep"/>.
        ///
        /// <para>Hysteresis: a still factor only starts moving once the gap exceeds <c>deadband</c>; once
        /// moving it keeps moving until the gap falls below the smaller <c>releaseBand</c> — harder to start
        /// than to stop, so it settles instead of jittering. Inertia carries a share of last year's velocity,
        /// so a factor eases into and out of motion rather than snapping.</para>
        /// </summary>
        public static float StepScalar(float current, float target, FactorDynamics d, ref HysteresisState st)
        {
            target = Clamp01(target);
            float gap = target - current;
            float ag = gap < 0f ? -gap : gap;
            st.moving = st.moving ? ag > d.releaseBand : ag > d.deadband;
            st.velocity = d.inertia * st.velocity + d.velocity * gap;
            float step = st.moving ? Clamp(st.velocity, -d.maxStep, d.maxStep) : 0f;
            return Clamp01(current + step);
        }

        /// <summary>
        /// One year's step of a distribution factor (e.g. education tiers, strata) toward a target
        /// distribution. All tiers share one <paramref name="moving"/> flag (the whole distribution moves or
        /// holds together), each tier carries its own velocity, and the result is renormalised so the shares
        /// still sum to one. <paramref name="target"/> is assumed already clamped and normalised by the caller.
        /// </summary>
        public static void StepDistribution(float[] current, float[] target, FactorDynamics d, float[] velocities, ref bool moving)
        {
            if (current == null || target == null || velocities == null) return;
            int n = current.Length;
            if (target.Length != n || velocities.Length != n) return;

            float maxGap = 0f;
            for (int i = 0; i < n; i++)
            {
                float g = target[i] - current[i];
                if (g < 0f) g = -g;
                if (g > maxGap) maxGap = g;
            }
            moving = moving ? maxGap > d.releaseBand : maxGap > d.deadband;
            if (!moving) return;

            for (int i = 0; i < n; i++)
            {
                float gap = target[i] - current[i];
                velocities[i] = d.inertia * velocities[i] + d.velocity * gap;
                current[i] = Clamp01(current[i] + Clamp(velocities[i], -d.maxStep, d.maxStep));
            }
            Normalize(current);
        }

        /// <summary>
        /// Enforce a cumulative-share ceiling on a distribution (DEMOGRAPHIC_MODEL §2 <c>mode=Ceiling</c>):
        /// the combined share of tiers at or above <paramref name="fromIndex"/> may not exceed
        /// <paramref name="bound"/>; any excess is scaled out of those tiers and spilled into
        /// <paramref name="spillIndex"/>. Used for the slavery education cap (§3): the enslaved share is held
        /// at Primary. A no-op when the share is already within bound.
        /// </summary>
        public static void ApplyCeiling(float[] dist, int fromIndex, float bound, int spillIndex)
        {
            if (dist == null || fromIndex < 0 || fromIndex >= dist.Length || spillIndex < 0 || spillIndex >= dist.Length) return;
            if (bound < 0f) bound = 0f;
            float above = 0f;
            for (int i = fromIndex; i < dist.Length; i++) above += dist[i];
            if (above > bound && above > 0f)
            {
                float scale = bound / above;
                float excess = above - bound;
                for (int i = fromIndex; i < dist.Length; i++) dist[i] *= scale;
                dist[spillIndex] += excess;
            }
        }

        /// <summary>Scale a distribution so its shares sum to 1. A non-positive total is left untouched.</summary>
        public static void Normalize(float[] dist)
        {
            if (dist == null) return;
            float t = 0f;
            for (int i = 0; i < dist.Length; i++) t += dist[i];
            if (t <= 0f) return;
            for (int i = 0; i < dist.Length; i++) dist[i] /= t;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
