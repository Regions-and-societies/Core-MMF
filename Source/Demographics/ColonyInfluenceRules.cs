namespace RegionsAndSocieties.Demographics
{
    /// <summary>
    /// The math of colony → regional influence (#81): the player's TREATMENT of a xenotype is an example the
    /// planet copies. Accepting a xenotype as a free colony member signals "they can live free here" and
    /// raises how welcome that xenotype is in the region; enslaving them lowers it. That acceptance then
    /// spreads outward region to region, so one map tile shifts a whole area over time.
    ///
    /// <para>Pure and dependency-free: the caller supplies the colony's free/slave head-counts per xenotype
    /// and each region's neighbours' acceptance; this module turns those into the acceptance a region holds,
    /// which <see cref="CohortYearRules"/> applies to a cohort's standing.</para>
    /// </summary>
    public static class ColonyInfluenceRules
    {
        /// <summary>How fast the player's own region adopts the colony's example each year (first-order).</summary>
        public const float SelfRelaxRate = 0.15f;

        /// <summary>How fast a region drifts toward its neighbours' acceptance each year — the outward spread,
        /// slower than the self-adoption so influence radiates gradually rather than teleporting.</summary>
        public const float SpreadRate = 0.06f;

        /// <summary>Acceptance is bounded so no amount of example flips a region past total welcome/rejection.</summary>
        public const float MaxAcceptance = 1f;

        /// <summary>
        /// The player's treatment signal for one xenotype, −1 (every member enslaved) to +1 (every member
        /// free). Zero when the colony has none of that xenotype (no example either way). This is the TARGET
        /// the player's region relaxes toward, not an instant change.
        /// </summary>
        public static float TreatmentSignal(int freeCount, int slaveCount)
        {
            int total = freeCount + slaveCount;
            if (total <= 0) return 0f;
            return (float)(freeCount - slaveCount) / total;
        }

        /// <summary>Relax a stored acceptance one step toward a target (the colony example, or the neighbour
        /// mean for spread), first-order at <paramref name="rate"/>, clamped to ±<see cref="MaxAcceptance"/>.</summary>
        public static float Relax(float current, float target, float rate)
        {
            float f = current + rate * (target - current);
            return f < -MaxAcceptance ? -MaxAcceptance : (f > MaxAcceptance ? MaxAcceptance : f);
        }
    }
}
