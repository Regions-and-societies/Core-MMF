// Behaviour tests for the deterministic education-structure core (0.2.0, #15): the tech-level base
// distributions, the research/aptitude skews, and the 0-100 index. All pure, so this runs without a
// game — it is what guarantees a region's education mix is the same on every machine.
using System;
using RegionsAndSocieties.Demographics;

namespace EducationRulesTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("base distributions are normalized and tech-shaped");
            Check("neolithic sums to 1", Sums1(EducationRules.BasePyramid(2)));
            Check("spacer sums to 1", Sums1(EducationRules.BasePyramid(5)));
            Check("unknown tech falls back to industrial", Same(EducationRules.BasePyramid(99), EducationRules.BasePyramid(4)));
            // Tribal societies are mostly illiterate; spacer societies mostly educated.
            Check("tribal has more illiterate than spacer",
                EducationRules.BasePyramid(2)[(int)EducationTier.Illiterate] > EducationRules.BasePyramid(5)[(int)EducationTier.Illiterate]);
            Check("spacer has more undergrad than tribal",
                EducationRules.BasePyramid(5)[(int)EducationTier.Undergrad] > EducationRules.BasePyramid(2)[(int)EducationTier.Undergrad]);

            Section("the index rises with tech level");
            int tribal = EducationRules.Index(EducationRules.BasePyramid(2));
            int industrial = EducationRules.Index(EducationRules.BasePyramid(4));
            int spacer = EducationRules.Index(EducationRules.BasePyramid(5));
            Check($"tribal ({tribal}) < industrial ({industrial}) < spacer ({spacer})", tribal < industrial && industrial < spacer);
            Check("index stays in 0..100", tribal >= 0 && spacer <= 100);

            Section("skews bend the distribution the right way");
            float[] flat = EducationRules.Pyramid(4, 0f, 0f);
            Check("no skew leaves the baseline (normalized)", Close(flat[0], EducationRules.BasePyramid(4)[0]));
            Check("realized distribution always sums to 1", Sums1(EducationRules.Pyramid(4, 1f, 1f)));

            int flatIdx = EducationRules.Index(flat);
            Check("positive research skew raises the index", EducationRules.Index(EducationRules.Pyramid(4, 1f, 0f)) > flatIdx);
            Check("negative research skew lowers the index", EducationRules.Index(EducationRules.Pyramid(4, -1f, 0f)) < flatIdx);
            Check("aptitude skew raises the index", EducationRules.Index(EducationRules.Pyramid(4, 0f, 1f)) > flatIdx);
            Check("positive skew raises the postgrad share", EducationRules.Pyramid(4, 1f, 0f)[(int)EducationTier.Postgrad] > flat[(int)EducationTier.Postgrad]);

            Check("research skew clamps: over-1 equals 1", Same(EducationRules.Pyramid(4, 5f, 0f), EducationRules.Pyramid(4, 1f, 0f)));
            Check("aptitude skew clamps: negative equals 0", Same(EducationRules.Pyramid(4, 0f, -3f), EducationRules.Pyramid(4, 0f, 0f)));

            Section("index edge cases");
            Check("all illiterate -> 0", EducationRules.Index(new[] { 1f, 0f, 0f, 0f, 0f }) == 0);
            Check("all postgrad -> 100", EducationRules.Index(new[] { 0f, 0f, 0f, 0f, 1f }) == 100);
            Check("null -> 0", EducationRules.Index(null) == 0);
            Check("empty -> 0", EducationRules.Index(new[] { 0f, 0f, 0f, 0f, 0f }) == 0);

            Section("economic value: geometric x1.67 per education step (#28)");
            Check("illiterate = 1", Close(EducationRules.EconomicValue(0), 1f));
            Check("each step is ~1.67x the one below",
                Close(EducationRules.EconomicValue(1) / EducationRules.EconomicValue(0), EducationRules.ValueStep, 0.03f)
                && Close(EducationRules.EconomicValue(4) / EducationRules.EconomicValue(3), EducationRules.ValueStep, 0.03f));
            Check("postgrad lands ~7.75x an illiterate", Close(EducationRules.EconomicValue(4), 7.75f, 0.2f));
            Check("value is monotonic up the ladder",
                EducationRules.EconomicValue(0) < EducationRules.EconomicValue(1)
                && EducationRules.EconomicValue(3) < EducationRules.EconomicValue(4));
            Check("the specialty depth rises with tier (illiterate none, postgrad several at 12+)",
                EducationRules.Profiles[0].specialties == 0 && EducationRules.Profiles[0].skillCap <= 3
                && EducationRules.Profiles[4].specialties >= 2 && EducationRules.Profiles[4].specialtyLow >= 12
                && EducationRules.Profiles[4].burningPassions >= 2);

            Section("region economic value & labour efficiency (#28/#29)");
            float eduLow = EducationRules.RegionEconomicValue(new[] { 0.7f, 0.3f, 0f, 0f, 0f });   // illiterate/primary
            float eduHigh = EducationRules.RegionEconomicValue(new[] { 0f, 0f, 0.2f, 0.4f, 0.4f }); // skilled/advanced
            Check("a schooled region carries far more economic value", eduHigh > eduLow * 3f);
            Check("a free, healthy, content worker realises most of their skill", EducationRules.LabourEfficiency(1f, 1f, 1f) > 0.95f);
            Check("a coerced, sick, miserable one (a slave) realises far less",
                EducationRules.LabourEfficiency(0.1f, 0.2f, 0.2f) < EducationRules.LabourEfficiency(1f, 1f, 1f) * 0.5f);
            Check("efficiency is floored, never zero", EducationRules.LabourEfficiency(0f, 0f, 0f) >= 0.15f - 1e-4f);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL EDUCATION TESTS PASSED" : failures + " EDUCATION TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static bool Sums1(float[] p)
        {
            float s = 0f; for (int i = 0; i < p.Length; i++) s += p[i];
            return Close(s, 1f);
        }

        private static bool Same(float[] a, float[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (!Close(a[i], b[i])) return false;
            return true;
        }

        private static bool Close(float a, float b) => Math.Abs(a - b) < 0.0005f;
        private static bool Close(float a, float b, float tol) => Math.Abs(a - b) <= tol;

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
