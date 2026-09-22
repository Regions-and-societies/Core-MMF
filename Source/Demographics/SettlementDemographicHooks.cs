using System;
using Verse;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>
    /// The seam a consumer (Districts-EP) drives to give a settlement its OWN demographic composition,
    /// aggregated from its real districts (#74). Core projects each settlement as a deterministic
    /// per-settlement perturbation of its faction baseline — the cheap half that carries most of the
    /// fidelity — and a consumer that models a settlement's districts can override that with the real thing
    /// through <see cref="CompositionProvider"/>.
    ///
    /// <para>No-op with nothing hooked (Core's perturbation stands). Same reflection-friendly pattern as the
    /// other hooks. The provider must be DETERMINISTIC for a given settlement tile (no per-call randomness) so
    /// the pressure field stays stable across saves and <see cref="RegionDemographicsUtility.VerifyCulling"/>
    /// keeps reporting zero mismatches.</para>
    /// </summary>
    public static class SettlementDemographicHooks
    {
        /// <summary>Set by a consumer to supply a settlement's own composition (as a
        /// <see cref="FactionDemographicProfile"/>) by its world tile. Return null for a settlement the
        /// consumer has no district data for, to fall through to Core's per-settlement perturbation.</summary>
        public static Func<int, FactionDemographicProfile> CompositionProvider;

        internal static FactionDemographicProfile TryGet(int settlementTile)
        {
            Func<int, FactionDemographicProfile> p = CompositionProvider;
            if (p == null) return null;
            try { return p(settlementTile); }
            catch (Exception e) { Log.Error("[RegionsAndSocieties] SettlementDemographicHooks provider threw: " + e); return null; }
        }
    }
}
