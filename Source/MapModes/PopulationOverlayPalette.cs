using UnityEngine;
using RegionsAndSocieties.Sizing;

namespace RegionsAndSocieties
{
    /// <summary>
    /// Shared colouring for the population and dwellings world overlays (#30/#79). The countryside now
    /// carries people everywhere, so a flat density tint would paint the whole planet; instead the ramp
    /// is calibrated to show only what stands out from the Frontier floor:
    /// <list type="bullet">
    ///   <item>truly empty land (0 people) reads <b>white</b> — a positive "nobody could live here" mark,
    ///   not the absence of the overlay;</item>
    ///   <item>the Frontier countryside — up to a bit more than the wilderness mean — reads <b>clear</b>,
    ///   so the ubiquitous rural baseline is nothing to look at;</item>
    ///   <item>above that a magma ramp rises through the settlement tiers to the densest tile (the peak).</item>
    /// </list>
    /// One palette, so the two overlays agree on what "empty", "countryside" and "peak" look like.
    /// </summary>
    public static class PopulationOverlayPalette
    {
        /// <summary>People per tile at or below which a tile is Frontier countryside and reads clear.
        /// Set above the wilderness mean (so the whole countryside disappears) but below a homestead (so
        /// even the smallest settlement picks up the first colour). Tied to the #79 target density, not
        /// an invented number.</summary>
        public static int FrontierCeiling =>
            Mathf.Max(1, Mathf.RoundToInt(WildernessPopulationRules.TargetMeanPerTile * 1.5f));

        /// <summary>Dwellings per tile equivalent of <see cref="FrontierCeiling"/>, for the dwellings
        /// overlay — the homes a frontier-ceiling population resolves into.</summary>
        public static int FrontierCeilingDwellings => Demographics.ResidenceRules.For(FrontierCeiling).dwellings;

        /// <summary>White — habitable land with no people at all. Opaque-ish so it reads as a statement,
        /// not a faded gap in the overlay.</summary>
        public static readonly Color Empty = new Color(0.95f, 0.95f, 0.95f, 0.72f);

        /// <summary>The colored ramp for the settled zone, just-above-frontier → peak: the "magma" ramp
        /// (violet → yellow), hues that occur nowhere in the planet's own green/blue/tan/grey palette so
        /// the hotspots read against the terrain.</summary>
        public static readonly Color[] Ramp = new Color[]
        {
            new Color(0.45f, 0.20f, 0.75f, 0.48f),   // 0: violet — just above the countryside
            new Color(0.75f, 0.20f, 0.70f, 0.55f),   // 1: magenta
            new Color(0.95f, 0.30f, 0.42f, 0.62f),   // 2: hot red-pink
            new Color(0.98f, 0.58f, 0.15f, 0.68f),   // 3: orange
            new Color(1.00f, 0.90f, 0.25f, 0.74f),   // 4: bright yellow — the densest tile (the peak)
        };

        // Band cuts on the log fraction from the frontier ceiling up to the peak.
        private static readonly float[] Thresholds = new float[] { 0.30f, 0.50f, 0.70f, 0.88f };

        /// <summary>The band for a tile: <b>-2</b> empty (white), <b>-1</b> Frontier (clear), else
        /// <b>0..Ramp.Length-1</b> up the ramp. <paramref name="value"/>, <paramref name="ceiling"/> and
        /// <paramref name="peak"/> are the same metric (people, or dwellings), so one call serves both
        /// overlays. Log-scaled from the ceiling to the peak so settlement cores step up through the
        /// colours instead of every non-peak tile collapsing into the bottom band.</summary>
        public static int Band(int value, int ceiling, int peak)
        {
            if (value <= 0) return -2;              // white — genuinely nobody
            if (ceiling < 1) ceiling = 1;
            if (value < ceiling) return -1;         // clear — countryside
            float bottom = Mathf.Log(1f + ceiling);
            float top = Mathf.Log(1f + Mathf.Max(peak, ceiling + 1));
            float span = top - bottom;
            float fraction = span > 0f ? Mathf.Clamp01((Mathf.Log(1f + value) - bottom) / span) : 1f;
            int seg = 0;
            for (int s = Thresholds.Length - 1; s >= 0; s--)
                if (fraction >= Thresholds[s]) { seg = s + 1; break; }
            return seg >= Ramp.Length ? Ramp.Length - 1 : seg;
        }
    }
}
