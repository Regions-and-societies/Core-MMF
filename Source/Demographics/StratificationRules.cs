namespace RegionsAndSocieties.Demographics
{
    /// <summary>
    /// Social stratification and the balance it distorts (#29). A society is not defined by the MEAN of its
    /// education/wealth but by the SHAPE. An economy runs on its <b>skilled middle</b> — the Secondary- and
    /// Undergrad-educated movers. A healthy spread (broad middle) can grow and run complex systems; a
    /// <b>polarised</b> society — a thin wealthy elite over a mass of illiterates with the middle hollowed
    /// out — has no engine, so its growth stalls no matter how sophisticated the elite. Imbalance is a growth
    /// risk, not just flavour.
    ///
    /// <para>Pure and dependency-free: operates on a 5-tier education distribution
    /// [Illiterate, Primary, Secondary, Undergrad, Postgrad]. Stratification (the Empire's rigid order) is
    /// modelled as a reshaping toward a bimodal elite+underclass; slavery hollows the middle from the other
    /// direction (capped elsewhere at Primary). The growth capacity it yields is what the economy consumes.</para>
    /// </summary>
    public static class StratificationRules
    {
        public const int Illiterate = 0, Primary = 1, Secondary = 2, Undergrad = 3, Postgrad = 4;

        /// <summary>Full skilled-middle share (a balanced society) — the reference the balance index reads
        /// against. Half the population in the skilled middle is treated as maximally balanced.</summary>
        public const float ReferenceMiddle = 0.5f;

        /// <summary>Growth capacity floor: even a fully polarised region keeps this fraction of its growth,
        /// and never boosts above 1 — a healthy middle is "full", imbalance only throttles.</summary>
        public const float MinGrowthCapacity = 0.30f;

        /// <summary>The skilled-middle share of a distribution — the movers (Secondary + Undergrad). NOT the
        /// Postgrad elite (too few, and their presence over a hollow middle is exactly the failure mode).</summary>
        public static float SkilledMiddleShare(float[] edu)
        {
            if (edu == null || edu.Length <= Undergrad) return 0f;
            float m = edu[Secondary] + edu[Undergrad];
            return m < 0f ? 0f : (m > 1f ? 1f : m);
        }

        /// <summary>Growth capacity 0.30..1.0 from a distribution's skilled middle: a missing middle throttles
        /// growth toward the floor, a broad middle reads full. This is the multiplier the birth/growth path
        /// and (later) the economy apply — the concrete "imbalance is a growth risk".</summary>
        public static float GrowthCapacity(float[] edu)
        {
            float mid = SkilledMiddleShare(edu);
            float c = MinGrowthCapacity + (1f - MinGrowthCapacity) * (mid / ReferenceMiddle);
            return c < MinGrowthCapacity ? MinGrowthCapacity : (c > 1f ? 1f : c);
        }

        /// <summary>A 0..1 balance index for display/economy: 1 = a broad skilled middle, 0 = fully polarised
        /// (no middle). The readable face of <see cref="GrowthCapacity"/>.</summary>
        public static float BalanceIndex(float[] edu)
        {
            float b = SkilledMiddleShare(edu) / ReferenceMiddle;
            return b < 0f ? 0f : (b > 1f ? 1f : b);
        }

        /// <summary>
        /// Reshape an education distribution toward a bimodal elite+underclass as <paramref name="stratification"/>
        /// (0 egalitarian .. 1 rigid) rises: pull mass out of the skilled middle (Secondary/Undergrad) and push
        /// it to the extremes — mostly down to Illiterate (the underclass), a little up to Postgrad (the elite).
        /// The mean barely moves; the SHAPE hollows. Renormalised into <paramref name="output"/> (sums to 1).
        /// </summary>
        public static void Polarize(float[] baseDist, float stratification, float[] output)
        {
            int n = output.Length;
            for (int i = 0; i < n; i++) output[i] = i < baseDist.Length ? baseDist[i] : 0f;
            if (stratification <= 0f) { Normalize(output); return; }
            float s = stratification > 1f ? 1f : stratification;

            // Up to 65% of the middle is displaced at full stratification.
            float moveSec = output[Secondary] * s * 0.65f;
            float moveUnd = output[Undergrad] * s * 0.65f;
            float moved = moveSec + moveUnd;
            output[Secondary] -= moveSec;
            output[Undergrad] -= moveUnd;
            output[Illiterate] += moved * 0.75f;   // bottom-heavy...
            output[Postgrad] += moved * 0.25f;     // ...over a thin elite

            Normalize(output);
        }

        /// <summary>
        /// Shove a distribution bottom-heavy by <paramref name="strength"/> (0..1) — the World-Domination-CP
        /// "decapitation" case, where a toppled nation loses its top strata (elite flight/killed) and the
        /// economy suffers until a new middle regrows. Drains Undergrad/Postgrad down into Illiterate/Primary.
        /// Renormalised into <paramref name="output"/>.
        /// </summary>
        public static void ShiftBottomHeavy(float[] baseDist, float strength, float[] output)
        {
            int n = output.Length;
            for (int i = 0; i < n; i++) output[i] = i < baseDist.Length ? baseDist[i] : 0f;
            if (strength <= 0f) { Normalize(output); return; }
            float s = strength > 1f ? 1f : strength;

            float lostPost = output[Postgrad] * s;
            float lostUnder = output[Undergrad] * s * 0.7f;
            output[Postgrad] -= lostPost;
            output[Undergrad] -= lostUnder;
            float lost = lostPost + lostUnder;
            output[Illiterate] += lost * 0.6f;
            output[Primary] += lost * 0.4f;

            Normalize(output);
        }

        private static void Normalize(float[] a)
        {
            float sum = 0f;
            for (int i = 0; i < a.Length; i++) { if (a[i] < 0f) a[i] = 0f; sum += a[i]; }
            if (sum > 0f) for (int i = 0; i < a.Length; i++) a[i] /= sum;
        }
    }
}
