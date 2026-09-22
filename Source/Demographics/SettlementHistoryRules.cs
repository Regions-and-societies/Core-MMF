using System;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>Pure sizing for a settlement-history legacy (#35): how many people an expired site leaves
    /// behind, from its threat points. A small camp leaves a homestead's worth; a big one more, capped so a
    /// single site never rivals a real settlement — the fidelity comes from many ignored sites over a long
    /// game, not one big one.</summary>
    public static class SettlementHistoryRules
    {
        public const int BaseLegacyPopulation = 90;    // ~a homestead
        public const int MaxLegacyPopulation = 400;    // a large ignored site, still below a village
        public const float PointsToPopulation = 0.15f; // threat points → people

        public static int LegacyPopulation(float sitePoints)
        {
            int p = BaseLegacyPopulation + (int)Math.Round(Math.Max(0f, sitePoints) * PointsToPopulation);
            return p > MaxLegacyPopulation ? MaxLegacyPopulation : p;
        }
    }
}
