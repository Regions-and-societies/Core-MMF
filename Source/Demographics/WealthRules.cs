using System;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>The economic sector a cohort's labour sits in — the source its income flows from
    /// (DEMOGRAPHIC_MODEL §5). Which one a cohort draws depends on its education and standing.</summary>
    public enum IncomeSource
    {
        Agriculture,   // farming, forestry — the default
        Extraction,    // mining/labour — where the drug-dependent and low-standing end up
        Manufacturing, // skilled trades
        Services,      // clerks, admins, medics — the educated
    }

    /// <summary>A cohort's decomposed wealth for one year, all in silver (DEMOGRAPHIC_MODEL §5).</summary>
    public struct WealthProfile
    {
        public IncomeSource source;
        public float income;      // silver/day earned
        public float costLiving;  // silver/day to keep up the local lifestyle
        public float netDaily;    // income − cost
        public float assets;      // accumulated net worth, floored at 0 (no debt)
    }

    /// <summary>
    /// Wealth as <b>income (from a sector) − cost of living → assets</b>, per cohort (DEMOGRAPHIC_MODEL §5).
    /// Not one scalar: a cohort earns from the sector its labour sits in (scaled by education, employment,
    /// standing, slavery), pays a cost of living that <b>rises with regional prosperity</b> (RimWorld's
    /// wealth-expectations treadmill) plus a role premium and drug cost, and banks the difference. The
    /// emergent behaviours the sim shows — <b>relative poverty</b> (a low earner in a rich region dissaves to
    /// 0) and <b>widening inequality</b> (positive-net cohorts compound while zero-net never start) — fall out
    /// of this, they are not coded in.
    ///
    /// <para>Pure and deterministic (no game types), ported from the calibration sim so its tuned constants
    /// carry over. Standing arrives as 0..1 (the sim's <c>(standing+1)/2</c>), education as a 0..1 index.</para>
    /// </summary>
    public static class WealthRules
    {
        // --- income ---------------------------------------------------------
        public const float IncomeBase = 2f;         // silver/day floor before education
        public const float IncomePerEdu = 16f;      // education's contribution to the wage
        public const float SlaveIncomePenalty = 0.4f;   // an enslaved share earns this much less

        // wage multiplier by sector, and the education/standing gates that route a cohort to a sector.
        public const float ServicesWage = 1.45f, ManufacturingWage = 1.10f, ExtractionWage = 0.75f, AgricultureWage = 0.90f;
        public const float ServicesEdu = 0.55f, ManufacturingEdu = 0.38f, LowStanding = 0.4f;

        // --- cost of living -------------------------------------------------
        public const float Subsistence = 1.5f;      // the irreducible floor
        public const float ProsperityTreadmill = 4f;    // how hard regional wealth lifts expectations
        public const float RolePremiumStanding = 0.72f; // above this standing, nobles/clergy keep up appearances
        public const float RolePremiumScale = 3f;       // the role premium, × regional wealth
        public const float DrugCost = 4f;               // silver/day the dependency diverts

        // --- assets ---------------------------------------------------------
        public const float AccrualDaysPerYear = 30f;    // ~a month's net banks per demographic year

        /// <summary>Which sector a cohort's labour sits in, and its wage multiplier, from education,
        /// standing (0..1) and drug burden. The educated go to services, then skilled trades; the
        /// drug-dependent and low-standing fall to extraction/labour; everyone else farms.</summary>
        public static IncomeSource SourceFor(float eduIndex, float standing01, float drugBurden, out float wageMult)
        {
            if (eduIndex > ServicesEdu) { wageMult = ServicesWage; return IncomeSource.Services; }
            if (eduIndex > ManufacturingEdu) { wageMult = ManufacturingWage; return IncomeSource.Manufacturing; }
            if (drugBurden > 0f || standing01 < LowStanding) { wageMult = ExtractionWage; return IncomeSource.Extraction; }
            wageMult = AgricultureWage; return IncomeSource.Agriculture;
        }

        /// <summary>Silver/day a cohort earns: education-driven base × sector wage × employment × standing,
        /// docked for the enslaved share.</summary>
        public static float Income(float eduIndex, float wageMult, float employmentRate, float standing01, float slaveShare)
        {
            return (IncomeBase + IncomePerEdu * eduIndex) * wageMult * employmentRate
                 * (0.6f + 0.4f * standing01) * (1f - SlaveIncomePenalty * slaveShare);
        }

        /// <summary>Silver/day it costs to live: subsistence + the prosperity treadmill (rises with regional
        /// wealth) scaled by education taste, + the role premium for high-standing roles, + the drug cost.</summary>
        public static float CostOfLiving(float regionalWealth, float eduIndex, float standing01, float drugBurden)
        {
            float rolePremium = standing01 > RolePremiumStanding ? RolePremiumScale * regionalWealth : 0f;
            return (Subsistence + ProsperityTreadmill * regionalWealth) * (0.7f + 0.5f * eduIndex)
                 + rolePremium + drugBurden * DrugCost;
        }

        /// <summary>Bank a year's net onto prior assets, floored at 0 — RimWorld has no debt.</summary>
        public static float AccrueAssets(float priorAssets, float netDaily)
        {
            float a = (priorAssets < 0f ? 0f : priorAssets) + netDaily * AccrualDaysPerYear;
            return a < 0f ? 0f : a;
        }

        /// <summary>The full one-year wealth picture for a cohort: source, income, cost, net, and the new
        /// asset balance. Standing is 0..1; education is a 0..1 index.</summary>
        public static WealthProfile Compute(float eduIndex, float standing01, float drugBurden, float slaveShare,
            float regionalWealth, float employmentRate, float priorAssets)
        {
            float wageMult;
            IncomeSource source = SourceFor(eduIndex, standing01, drugBurden, out wageMult);
            float income = Income(eduIndex, wageMult, employmentRate, standing01, slaveShare);
            float cost = CostOfLiving(regionalWealth, eduIndex, standing01, drugBurden);
            float net = income - cost;
            return new WealthProfile
            {
                source = source,
                income = income,
                costLiving = cost,
                netDaily = net,
                assets = AccrueAssets(priorAssets, net),
            };
        }
    }
}
