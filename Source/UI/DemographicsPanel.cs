using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using RegionsAndSocieties.Demographics;

namespace RegionsAndSocieties.UI
{
    /// <summary>
    /// The visual demographic inspection panel (#26) — the user-facing face of the demographic models.
    /// Renders a <see cref="RegionDemographics"/> as labelled charts: stacked bars for the band/tier
    /// axes (age, sex, education, socioeconomic, employment) and pies for the categorical ones
    /// (xenotype, ideology). Degrades cleanly — no Biotech / no Ideology axes say so rather than drawing
    /// a blank chart, and an unsettled region renders a single "no population" line. Pure layout over a
    /// derived aggregate, so the same panel can later render a tile sample.
    /// </summary>
    public static class DemographicsPanel
    {
        private const float SectionGap = 10f;
        private const float HeaderH = 20f;
        private const float PieSize = 66f;

        /// <summary>
        /// Draw the panel from the top of <paramref name="rect"/> and return the height used, so a
        /// scrolling host can size its view. <paramref name="cacheKeyBase"/> (e.g. the province id) keys
        /// the pie textures; include a mix signature so a changed make-up rebuilds them.
        /// </summary>
        public static float Draw(Rect rect, RegionDemographics demo, string cacheKeyBase)
        {
            float y = rect.y;
            if (demo == null || demo.settledTiles <= 0)
            {
                Widgets.Label(new Rect(rect.x, y, rect.width, HeaderH), "No settled population in this region.");
                return HeaderH;
            }

            y = BarSection(rect, y, $"Age  —  median {demo.medianAge}", new List<BarSegment>
            {
                new BarSegment("Children", demo.ageShares[(int)AgeBucket.Child], DemographicColors.Age[0]),
                new BarSegment("Working-age", demo.ageShares[(int)AgeBucket.WorkingAge], DemographicColors.Age[1]),
                new BarSegment("Elders", demo.ageShares[(int)AgeBucket.Elder], DemographicColors.Age[2]),
            });

            y = BarSection(rect, y, "Sex ratio", new List<BarSegment>
            {
                new BarSegment("Female", demo.femaleFraction, DemographicColors.Female),
                new BarSegment("Male", 1f - demo.femaleFraction, DemographicColors.Male),
            });

            y = BarSection(rect, y, $"Education  —  index {demo.educationIndex}/100", new List<BarSegment>
            {
                new BarSegment("Illiterate", demo.educationShares[(int)EducationTier.Illiterate], DemographicColors.Education[0]),
                new BarSegment("Basic", demo.educationShares[(int)EducationTier.Basic], DemographicColors.Education[1]),
                new BarSegment("Skilled", demo.educationShares[(int)EducationTier.Skilled], DemographicColors.Education[2]),
                new BarSegment("Advanced", demo.educationShares[(int)EducationTier.Advanced], DemographicColors.Education[3]),
            });

            y = BarSection(rect, y, $"Socioeconomic  —  index {demo.sesIndex}/100", new List<BarSegment>
            {
                new BarSegment("Subsistence", demo.sesShares[(int)SesTier.Subsistence], DemographicColors.Ses[0]),
                new BarSegment("Modest", demo.sesShares[(int)SesTier.Modest], DemographicColors.Ses[1]),
                new BarSegment("Prosperous", demo.sesShares[(int)SesTier.Prosperous], DemographicColors.Ses[2]),
                new BarSegment("Affluent", demo.sesShares[(int)SesTier.Affluent], DemographicColors.Ses[3]),
            });

            y = BarSection(rect, y, $"Employment  —  {demo.employmentRate}% employed", new List<BarSegment>
            {
                new BarSegment("Agriculture", demo.occupationShares[(int)OccupationSector.Agriculture], DemographicColors.Employment[0]),
                new BarSegment("Industry", demo.occupationShares[(int)OccupationSector.Industry], DemographicColors.Employment[1]),
                new BarSegment("Military", demo.occupationShares[(int)OccupationSector.Military], DemographicColors.Employment[2]),
                new BarSegment("Trade", demo.occupationShares[(int)OccupationSector.Trade], DemographicColors.Employment[3]),
            });

            y += SectionGap;
            if (!demo.biotechActive)
                y = NoteSection(rect, y, "Xenotypes", "All Baseliner (Biotech not active).");
            else if (demo.raceShares.Count == 0)
                y = NoteSection(rect, y, "Xenotypes", "No data.");
            else
                y = PieSection(rect, y, "Xenotypes", demo.raceShares
                    .OrderByDescending(k => k.Value)
                    .Select(k => new PieSlice { label = k.Key.LabelCap, fraction = k.Value, color = DemographicColors.Xenotype(k.Key) })
                    .ToList(), cacheKeyBase + "_xeno");

            y += SectionGap;
            if (!demo.ideologyActive)
                y = NoteSection(rect, y, "Ideology", "Secular (Ideology not active).");
            else if (demo.ideoShares.Count == 0)
                y = NoteSection(rect, y, "Ideology", "No data.");
            else
                y = PieSection(rect, y, "Ideology", demo.ideoShares
                    .OrderByDescending(k => k.Value)
                    .Select(k => new PieSlice { label = k.Key.name, fraction = k.Value, color = DemographicColors.Ideology(k.Key) })
                    .ToList(), cacheKeyBase + "_ideo");

            return y - rect.y;
        }

        private static float BarSection(Rect rect, float y, string header, List<BarSegment> segs)
        {
            y += SectionGap;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x, y, rect.width, HeaderH), header);
            y += HeaderH;

            DemographicBars.DrawStackedBar(new Rect(rect.x, y, rect.width, DemographicBars.BarHeight), segs);
            y += DemographicBars.BarHeight + 2f;
            y += DemographicBars.DrawSwatchLegend(new Rect(rect.x, y, rect.width, 40f), segs);
            return y;
        }

        private static float PieSection(Rect rect, float y, string header, List<PieSlice> slices, string cacheKeyBase)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x, y, rect.width, HeaderH), header);
            y += HeaderH;

            var pieRect = new Rect(rect.x, y, PieSize, PieSize);
            // Signature the key on the mix so a changed make-up (e.g. via population growth) rebuilds it.
            string key = $"{cacheKeyBase}_{slices.Count}_{(slices.Count > 0 ? slices[0].fraction : 0f):F2}";
            PieChartDrawer.DrawPieChart(pieRect, slices, key);
            RegionalPieChartWindow.DrawLegend(new Rect(pieRect.xMax + 10f, y, rect.width - PieSize - 10f, PieSize), slices);
            return y + PieSize + 2f;
        }

        private static float NoteSection(Rect rect, float y, string header, string note)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x, y, rect.width, HeaderH), header);
            y += HeaderH;

            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(rect.x, y, rect.width, HeaderH), note);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            return y + HeaderH;
        }
    }
}
