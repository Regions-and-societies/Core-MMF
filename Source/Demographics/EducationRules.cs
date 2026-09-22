using System;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>The five education levels a region's people are bucketed into (#15/#26), a real-world
    /// schooling ladder from no schooling to a research elite. A structure, not a per-pawn roll — see
    /// <see cref="EducationRules.Profiles"/> for what each level means for a pawn (skills, passion) and
    /// for the economy (the capability it unlocks).</summary>
    public enum EducationTier
    {
        Illiterate = 0,  // no formal schooling
        Primary = 1,     // basic literacy — primary school
        Secondary = 2,   // skilled labour — secondary / vocational
        Undergrad = 3,   // higher education — undergraduate
        Postgrad = 4     // research elite — postgraduate
    }

    /// <summary>
    /// What one education level means for the people in it and for the economy (#26 → #28/#29). A
    /// machine-usable skill shape a pawn-generation consumer applies: how many skills the pawn is deep in,
    /// their level range, how many carry a burning vs minor passion, what the rest sit at, and a hard cap.
    /// Plus the per-capita <see cref="economicValue"/> that drives regional economic efficiency. Single
    /// source of truth for the pawn-gen hook (#28), the economy read (#29) and the region panel.
    ///
    /// <para><b>Which</b> skills a pawn specialises in is not fixed here — the consumer picks them from the
    /// region's dominant sector (a mining region's specialists are miners/crafters, an agri region's are
    /// growers), so blue-collar depth is still high skill. This struct is the DEPTH; the sector is the domain.</para>
    /// </summary>
    public struct EducationProfile
    {
        public string label;             // the real-world schooling level
        public int specialties;          // how many skills a pawn of this level is genuinely deep in
        public int specialtyLow;         // the level range of those specialties
        public int specialtyHigh;
        public int burningPassions;      // burning (major) passions among the specialties
        public int minorPassions;        // minor passions among the specialties / rest
        public int baselineLow;          // every OTHER skill sits in this range
        public int baselineHigh;
        public int skillCap;             // hard cap on any single skill at this level (illiterate ≤ 3)
        public float economicValue;      // per-capita economic value, illiterate = 1 (geometric ×1.67 / step)
        public string economicRole;      // the economic capability this level unlocks (display)
    }

    /// <summary>
    /// The deterministic education-structure core (0.2.0, #15). A region's education distribution — how
    /// its people split across the five levels — is a pure function of a few signals: the dominant
    /// faction's tech level (the dominant signal: a tribe is mostly illiterate, a spacer polity mostly
    /// skilled), an ideology research skew (transhumanist/tech memes lift it, primitivist/nature memes
    /// pull it down), and a xenotype aptitude skew where Biotech gives a caste engineered intellect.
    /// No signal is required: with none supplied the tech baseline stands, so it degrades cleanly to
    /// plain humans with no DLC.
    ///
    /// <para>Pure by design, like <see cref="AgeStructureRules"/> and <see cref="DemographicsRules"/>:
    /// it works on plain numbers (a tech ordinal and two skew scalars), testable without a game and
    /// identical on every machine. The game-side glue that reads a faction's tech level, its ideology
    /// memes and a xenotype's aptitudes lives in <see cref="RegionDemographicsUtility"/>.</para>
    /// </summary>
    public static class EducationRules
    {
        public const int TierCount = 5;

        // A 0-100 attainment score per tier, used to collapse a distribution to one index for shading.
        private static readonly float[] TierScore = { 0f, 25f, 50f, 75f, 100f };

        /// <summary>The per-step economic-value multiplier between education tiers (#28): each level of
        /// schooling is worth ~⅔ more than the one below. Compounding, deliberately — in RimWorld a
        /// specialist doesn't just work faster, they MONOPOLISE the job (a mediocre pawn means botched
        /// surgeries, food poisoning, junk goods — mistakes whose costs compound), so a skilled worker's
        /// value is super-linear. Postgrad therefore lands ~7.75× an illiterate.</summary>
        public const float ValueStep = 1.6667f;

        /// <summary>The meaning of each <see cref="EducationTier"/>, indexed by tier ordinal. Calibrated
        /// against real-world compensation-by-attainment (OECD relative earnings + World-Bank returns-to-
        /// schooling) then set to a clean geometric ×<see cref="ValueStep"/> per step; first-pass and tunable.
        /// The skill shape feeds the pawn-gen hook (#28); economicValue drives the economy read (#29); the
        /// economic role gates capability (industrial growth needs Secondary+, critical systems Undergrad+,
        /// high-tech Postgrad).</summary>
        public static readonly EducationProfile[] Profiles =
        {
            new EducationProfile { label = "Illiterate", specialties = 0, specialtyLow = 0,  specialtyHigh = 0,  burningPassions = 0, minorPassions = 0, baselineLow = 0, baselineHigh = 3, skillCap = 3,  economicValue = 1.00f, economicRole = "Subsistence labour only — cannot operate machinery" },
            new EducationProfile { label = "Primary",    specialties = 1, specialtyLow = 3,  specialtyHigh = 5,  burningPassions = 0, minorPassions = 1, baselineLow = 0, baselineHigh = 3, skillCap = 6,  economicValue = 1.67f, economicRole = "Manual & agricultural labour" },
            new EducationProfile { label = "Secondary",  specialties = 2, specialtyLow = 6,  specialtyHigh = 9,  burningPassions = 0, minorPassions = 1, baselineLow = 1, baselineHigh = 4, skillCap = 10, economicValue = 2.78f, economicRole = "Runs industrial workshops — production output" },
            new EducationProfile { label = "Undergrad",  specialties = 2, specialtyLow = 9,  specialtyHigh = 12, burningPassions = 1, minorPassions = 1, baselineLow = 2, baselineHigh = 5, skillCap = 14, economicValue = 4.64f, economicRole = "Runs & maintains critical systems (hydroponics, power) — industrial growth & resiliency" },
            new EducationProfile { label = "Postgrad",   specialties = 3, specialtyLow = 12, specialtyHigh = 16, burningPassions = 2, minorPassions = 1, baselineLow = 3, baselineHigh = 6, skillCap = 20, economicValue = 7.75f, economicRole = "R&D — unlocks high-tech production & innovation" },
        };

        /// <summary>The per-capita economic value of an education tier (illiterate = 1). See
        /// <see cref="Profiles"/> / <see cref="ValueStep"/>.</summary>
        public static float EconomicValue(int tier)
            => (tier >= 0 && tier < TierCount) ? Profiles[tier].economicValue : 1f;

        /// <summary>The population-weighted per-capita economic value of a region's education distribution —
        /// how much economic capacity its people carry, before condition (labour efficiency) is applied. A
        /// polarised region with a hollow middle reads low; a broadly-schooled one high (#28/#29).</summary>
        public static float RegionEconomicValue(float[] eduShares)
        {
            if (eduShares == null || eduShares.Length < TierCount) return 1f;
            float v = 0f, total = 0f;
            for (int i = 0; i < TierCount; i++) { v += eduShares[i] * Profiles[i].economicValue; total += eduShares[i]; }
            return total > 0f ? v / total : 1f;
        }

        /// <summary>Labour efficiency 0.15..1 — how much of a person's skill is actually REALISED, from their
        /// condition (#29): a free, healthy, content worker performs near their capacity; a coerced, sick,
        /// miserable one (a slave) performs far below it whatever their skill. Orthogonal to education, so
        /// applying both is not double-counting. Floored so even the worst still produces something.</summary>
        public static float LabourEfficiency(float freedom, float health, float contentment)
        {
            float e = 0.45f * Clamp01(freedom) + 0.35f * Clamp01(health) + 0.20f * Clamp01(contentment);
            return e < 0.15f ? 0.15f : (e > 1f ? 1f : e);
        }

        /// <summary>
        /// The baseline distribution for a tech level, as normalized [illiterate, basic, skilled,
        /// advanced] shares. <paramref name="techLevel"/> is RimWorld's <c>TechLevel</c> ordinal
        /// (Animal=1 … Archotech=7); anything unrecognised falls back to the industrial shape. First-pass
        /// values, tunable.
        /// </summary>
        public static float[] BasePyramid(int techLevel)
        {
            // Shares over [illiterate, primary, secondary, undergrad, postgrad].
            switch (techLevel)
            {
                case 1: // Animal
                case 2: // Neolithic — oral culture, almost no formal schooling
                    return new[] { 0.50f, 0.38f, 0.10f, 0.02f, 0.00f };
                case 3: // Medieval
                    return new[] { 0.35f, 0.40f, 0.20f, 0.05f, 0.00f };
                case 4: // Industrial
                    return new[] { 0.10f, 0.28f, 0.35f, 0.22f, 0.05f };
                case 5: // Spacer
                    return new[] { 0.03f, 0.15f, 0.32f, 0.35f, 0.15f };
                case 6: // Ultra
                    return new[] { 0.01f, 0.08f, 0.25f, 0.40f, 0.26f };
                case 7: // Archotech — a research civilisation
                    return new[] { 0.00f, 0.04f, 0.16f, 0.40f, 0.40f };
                default:
                    return new[] { 0.10f, 0.28f, 0.35f, 0.22f, 0.05f };   // treat unknown as industrial
            }
        }

        /// <summary>
        /// The realized distribution for a region: the tech baseline bent by a research skew (positive =
        /// tech/transhumanist ideology lifting attainment, negative = primitivist pulling it down; clamped
        /// to [-1,1]) and a xenotype aptitude skew (0..1, an engineered-intellect caste raising the top).
        /// Weight shifts toward the higher tiers as the combined push rises and toward the lower tiers as
        /// it falls, hinged around the middle of the ladder. Always returns a normalized four-tier array.
        /// </summary>
        public static float[] Pyramid(int techLevel, float researchSkew, float aptitudeSkew)
        {
            float[] p = BasePyramid(techLevel);
            float shift = 0.40f * Clamp(researchSkew, -1f, 1f) + 0.55f * Clamp01(aptitudeSkew);

            // Tier rank runs 0..3; hinge at 1.5 so the two low tiers scale opposite the two high tiers.
            const float hinge = (TierCount - 1) / 2f;
            float sum = 0f;
            var w = new float[TierCount];
            for (int i = 0; i < TierCount; i++)
            {
                float factor = 1f + shift * ((i - hinge) / hinge);
                w[i] = p[i] * (factor < 0f ? 0f : factor);
                sum += w[i];
            }
            if (sum <= 0f) return new[] { 0f, 1f, 0f, 0f, 0f };   // degenerate: call it all primary
            for (int i = 0; i < TierCount; i++) w[i] /= sum;
            return w;
        }

        /// <summary>
        /// A single 0-100 education index for a distribution: the share-weighted mean of the per-tier
        /// attainment scores. 0 = wholly illiterate, 100 = wholly advanced. Returns 0 for a null/empty
        /// distribution. This is what the overlay shades by.
        /// </summary>
        public static int Index(float[] pyramid)
        {
            if (pyramid == null || pyramid.Length < TierCount) return 0;
            float total = 0f, acc = 0f;
            for (int i = 0; i < TierCount; i++) { total += pyramid[i]; acc += pyramid[i] * TierScore[i]; }
            if (total <= 0f) return 0;
            return (int)Math.Round(acc / total);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
