// Behaviour tests for the per-cohort vital statistics (#58 §4): one mortality decomposition into life
// expectancy, infant mortality, and the leading cause of death. Ported from Design/sim/graph.js. Pure.
using System;
using RegionsAndSocieties.Demographics;

namespace VitalsRulesTests
{
    public static class Program
    {
        private static int failures;

        // A comfortable baseline: good care, fed, peaceful, no addiction, accepted.
        private static CauseHazards Good() => VitalsRules.Hazards(0.9f, 80f, 0.9f, 0.3f, 0.8f, 0.05f, 0.05f, 0f, 0f, 0.05f, 0.5f);

        public static int Main()
        {
            Section("healthcare access — the elite get better care");
            float med = VitalsRules.RegionMedicine(0.7f, 0.3f, 0.4f);
            Check("region medicine is in range", med > 0f && med <= 1f);
            Check("a higher income buys more access",
                VitalsRules.HealthcareAccess(med, 12f) > VitalsRules.HealthcareAccess(med, 0f));

            Section("hazards respond to conditions");
            Check("starvation raises the malnutrition hazard",
                VitalsRules.Hazards(0.9f, 80f, 0.1f, 0.3f, 0.8f, 0.05f, 0f, 0f, 0f, 0f, 0.5f).malnutrition > 0f);
            Check("sanitation cuts disease",
                VitalsRules.Hazards(0.5f, 80f, 0.9f, 0.5f, 0.9f, 0.1f, 0f, 0f, 0f, 0f, 0.5f).disease
                < VitalsRules.Hazards(0.5f, 80f, 0.9f, 0.5f, 0.0f, 0.1f, 0f, 0f, 0f, 0f, 0.5f).disease);
            Check("pollution raises disease",
                VitalsRules.Hazards(0.5f, 80f, 0.9f, 0.5f, 0.5f, 0.9f, 0f, 0f, 0f, 0f, 0.5f).disease
                > VitalsRules.Hazards(0.5f, 80f, 0.9f, 0.5f, 0.5f, 0.0f, 0f, 0f, 0f, 0f, 0.5f).disease);
            Check("a hated cohort faces a xenophobia hazard",
                VitalsRules.Hazards(0.9f, 80f, 0.9f, 0.3f, 0.8f, 0.05f, 0f, 0f, 0f, 0f, -0.8f).xenophobia > 0f);
            Check("an accepted cohort faces none",
                VitalsRules.Hazards(0.9f, 80f, 0.9f, 0.3f, 0.8f, 0.05f, 0f, 0f, 0f, 0f, 0.8f).xenophobia == 0f);

            Section("old age scales inverse to genetic lifespan");
            float mortal = VitalsRules.Hazards(0.9f, 80f, 0.9f, 0.3f, 0.8f, 0.05f, 0f, 0f, 0f, 0f, 0.5f).oldAge;
            float longLived = VitalsRules.Hazards(0.9f, 800f, 0.9f, 0.3f, 0.8f, 0.05f, 0f, 0f, 0f, 0f, 0.5f).oldAge;
            Check("a near-immortal barely dies of old age", longLived < mortal);

            Section("life expectancy");
            int leGood = VitalsRules.LifeExpectancy(80f, 0.9f, Good().Exogenous);
            int leWar = VitalsRules.LifeExpectancy(80f, 0.9f, VitalsRules.Hazards(0.9f, 80f, 0.9f, 0.3f, 0.8f, 0.05f, 0.3f, 0.8f, 0f, 0f, 0.5f).Exogenous);
            Check("a peaceful, cared-for cohort lives long", leGood > 60);
            Check("war and violence cut it short", leWar < leGood);
            Check("life expectancy is floored at 15",
                VitalsRules.LifeExpectancy(80f, 0f, 5f) == 15);
            Check("better care lengthens life",
                VitalsRules.LifeExpectancy(80f, 0.9f, 0.05f) > VitalsRules.LifeExpectancy(80f, 0.2f, 0.05f));

            Section("infant mortality");
            Check("good care + food => low IMR", VitalsRules.InfantMortalityPer1000(0.95f, 0.95f, 0f) < 60);
            Check("poor care + famine => high IMR", VitalsRules.InfantMortalityPer1000(0.1f, 0.1f, 0.05f) > 200);
            Check("genetic fragility raises IMR",
                VitalsRules.InfantMortalityPer1000(0.5f, 0.5f, 0.1f) > VitalsRules.InfantMortalityPer1000(0.5f, 0.5f, 0f));

            Section("leading cause of death is the biggest hazard");
            var warTorn = VitalsRules.Hazards(0.9f, 80f, 0.9f, 0.3f, 0.8f, 0.05f, 0.1f, 1f, 0f, 0f, 0.5f);
            Check("in a war zone, war leads", VitalsRules.LeadingCause(warTorn) == DeathCause.War);
            var starving = VitalsRules.Hazards(0.9f, 80f, 0.0f, 0.3f, 0.8f, 0.05f, 0f, 0f, 0f, 0f, 0.5f);
            Check("in a famine, malnutrition leads", VitalsRules.LeadingCause(starving) == DeathCause.Malnutrition);
            var peaceful = Good();
            Check("in comfort, old age leads", VitalsRules.LeadingCause(peaceful) == DeathCause.OldAge);

            Section("compute ties it together");
            var v = VitalsRules.Compute(0.9f, 80f, 0.9f, 0.3f, 0.8f, 0.05f, 0.05f, 0f, 0f, 0.05f, 0.5f, 0f);
            Check("mortality hazard is the sum", Math.Abs(v.mortalityHazard - v.hazards.Total) < 1e-6f);
            Check("a comfortable cohort dies of old age", v.leadingCause == DeathCause.OldAge && v.lifeExpectancy > 60);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL VITALS TESTS PASSED" : failures + " VITALS TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static void Section(string name)
        {
            Console.WriteLine();
            Console.WriteLine("-- " + name);
        }

        private static void Check(string label, bool ok)
        {
            if (!ok) failures++;
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label);
        }
    }
}
