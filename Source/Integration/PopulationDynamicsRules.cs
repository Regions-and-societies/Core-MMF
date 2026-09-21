namespace RegionsAndSocieties.Integration
{
    /// <summary>
    /// The tunable math of the population-dynamics passes (#36), pulled out of <see cref="PopulationDynamics"/>
    /// so the rates/falloffs/ceilings are unit-testable without a running game. The passes themselves stay in
    /// PopulationDynamics (they need the region graph); this is just the arithmetic they apply per region.
    /// </summary>
    public static class PopulationDynamicsRules
    {
        /// <summary>Fraction of the migration pull retained per adjacency hop from the colony. &lt;1, so pull
        /// decays with distance — nearby regions feed the colony, the far side of the planet barely notices.</summary>
        public const float DefaultHopRetention = 0.55f;

        /// <summary>The colony can draw migrants until its effective population reaches this multiple of its
        /// base — a ceiling so a long game cannot pile the whole planet onto one tile (#5).</summary>
        public const float DefaultColonyCeilingMult = 2f;

        /// <summary>A region accretes (fills in around a growing settlement, #8) only up to this multiple of
        /// its own base population, so accretion tops out instead of running away.</summary>
        public const float DefaultAccretionCeilingMult = 1.5f;

        /// <summary>Migration pull at <paramref name="hops"/> adjacency steps from the colony: geometric decay
        /// (retention^hops). 1 at the colony itself; smaller the farther out. Never negative.</summary>
        public static float DistanceFalloff(int hops, float perHopRetention = DefaultHopRetention)
        {
            if (hops <= 0) return 1f;
            if (perHopRetention <= 0f) return 0f;
            float f = 1f;
            for (int i = 0; i < hops; i++) f *= perHopRetention;
            return f;
        }

        /// <summary>The population a region can shed this pass: everything above the floor, or zero.</summary>
        public static float Movable(float effectiveHere, float floor)
        {
            float m = effectiveHere - floor;
            return m > 0f ? m : 0f;
        }

        /// <summary>How many more people the colony can still absorb under its ceiling
        /// (<paramref name="ceilingMult"/> × base). Zero once full — migration then stops adding to it.</summary>
        public static float ColonyRoom(float colonyBase, float colonyDelta, float ceilingMult = DefaultColonyCeilingMult)
        {
            float ceiling = (colonyBase > 0f ? colonyBase : 1f) * ceilingMult;
            float room = ceiling - (colonyBase + colonyDelta);
            return room > 0f ? room : 0f;
        }

        /// <summary>How much of an accretion <paramref name="step"/> a neighbour can take before hitting its
        /// accretion cap (<paramref name="ceilingMult"/> × its base). Bounds the #8 pass so it cannot exceed
        /// the region cap or double-count against migration.</summary>
        public static float AccretionInto(float step, float neighbourEffective, float neighbourBase, float ceilingMult = DefaultAccretionCeilingMult)
        {
            if (step <= 0f) return 0f;
            float cap = (neighbourBase > 0f ? neighbourBase : 1f) * ceilingMult;
            float room = cap - neighbourEffective;
            if (room <= 0f) return 0f;
            return step < room ? step : room;
        }
    }
}
