using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>
    /// Builds a live <see cref="RegionStage"/> from a real region's derived signals (#58 step 3): a read-only
    /// adapter that pulls the region's wealth, education, employment, sector mix, age structure, biome, size
    /// and population out of <see cref="RegionDemographicsUtility"/> + <see cref="GeographicProvince"/> and
    /// packs them into the shared stage the cohort container advances against — replacing the synthetic stand-in.
    ///
    /// <para>Game-coupled (reads Find / the derived model), so it is type-checked rather than unit-run. A few
    /// signals with no cheap derived source yet (conflict, pollution, roads, ideology tolerance, slavery
    /// stance) use documented first-pass defaults, to be wired to their real sources in a later slice.</para>
    /// </summary>
    public static class RegionStageBuilder
    {
        // First-pass defaults for signals not yet read from a live source.
        public const float DefaultConflict = 0f, DefaultPollution = 0.1f, DefaultRoads = 0.3f;
        public const float DefaultIdeoTolerance = 0.3f, DefaultSlaveryStance = 0f, DefaultPublicSector = 0.1f;
        public const float DefaultCrime = 0.05f;

        // #58: a subsistence floor so an unowned / undeveloped region (derived wealth ~0) holds a rural
        // population instead of death-spiralling — the countryside is poor, not lethal (aligns with the #79
        // Frontier that carries ~30/tile). A hand-to-mouth farming economy is fed and mostly employed.
        public const float SubsistenceWealth = 0.15f, SubsistenceEmployment = 0.6f;

        /// <summary>Build the stage for a land province from its current derived demographics.</summary>
        public static RegionStage Build(GeographicProvince province)
        {
            var stage = new RegionStage();
            if (province == null) return stage;

            RegionDemographics demo = RegionDemographicsUtility.ForRegion(province);
            Faction owner = DominantFaction(demo, province);
            FactionDemographicProfile profile = owner != null ? FactionDemographicProfile.Build(owner) : FactionDemographicProfile.Empty;

            // Floored at subsistence: even an unowned frontier feeds and mostly employs itself, so it holds
            // a poor rural population rather than collapsing (the death-spiral the live projection exposed).
            stage.wealth = Clamp01(Max(SubsistenceWealth, demo.sesIndex / 100f));
            stage.employmentRate = Clamp01(Max(SubsistenceEmployment, demo.employmentRate / 100f));
            stage.urbanisation = demo.tileCount > 0 ? Clamp01((float)demo.settledTiles / demo.tileCount) : 0f;
            stage.biomeFertility = province.primaryBiome != null ? Clamp01(province.primaryBiome.plantDensity) : 0.5f;
            stage.ageWorking = ReadShare(demo.ageShares, (int)AgeBucket.WorkingAge, 0.6f);
            stage.natalism = Clamp01(profile.natalistSkew);

            // #29: stratification + slavery from the owning faction's character — the Empire reads rigidly
            // stratified and slave-holding (its education shape polarises bimodal and its underclass is capped
            // at Primary), a tribe egalitarian. Drives the missing-middle growth throttle.
            if (owner?.def != null)
            {
                FactionArchetype arch = FactionCharacterRules.Classify(owner.def.defName, (int)owner.def.techLevel, owner.def.permanentEnemy);
                FactionCharacterRules.Character character = FactionCharacterRules.CharacterOf(arch);
                stage.stratification = character.stratification;
                stage.slaveryStance = character.slaveryStance;
            }

            // education: the region distribution, copied in tier order (Illiterate..Postgrad).
            stage.education = new float[EducationRules.TierCount];
            if (demo.educationShares != null && demo.educationShares.Length == EducationRules.TierCount)
                for (int i = 0; i < EducationRules.TierCount; i++) stage.education[i] = demo.educationShares[i];
            else stage.education[(int)EducationTier.Primary] = 1f;   // degrade to all-primary if unset

            // sector mix mapped from the region's occupation shares (Agriculture/Industry/Military/Trade).
            stage.sectorServices = ReadShare(demo.occupationShares, (int)OccupationSector.Trade, 0.3f);
            stage.sectorManufacturing = ReadShare(demo.occupationShares, (int)OccupationSector.Industry, 0.3f);
            stage.sectorMilitary = ReadShare(demo.occupationShares, (int)OccupationSector.Military, 0.05f);
            stage.sectorPublic = DefaultPublicSector;

            stage.tiles = province.tiles != null ? province.tiles.Count : 1;
            stage.population = province.currentPopulation > 0 ? province.currentPopulation : 1f;

            // not yet read from a live source — first-pass defaults.
            stage.conflict = DefaultConflict;
            stage.pollution = DefaultPollution;
            stage.roads = DefaultRoads;
            stage.ideoTolerance = DefaultIdeoTolerance;
            // stage.slaveryStance is set above from the owning faction's character (#29); an unowned region
            // keeps the neutral default (0).
            stage.crime = DefaultCrime;
            return stage;
        }

        /// <summary>The faction that dominates a province — used to seed its cohort roster.</summary>
        public static Faction OwnerOf(GeographicProvince province)
        {
            if (province == null) return null;
            return DominantFaction(RegionDemographicsUtility.ForRegion(province), province);
        }

        /// <summary>The faction that dominates the region — the largest demographic pressure share, else the
        /// first listed owner. Used for the faction-character signals (natalism, roster).</summary>
        private static Faction DominantFaction(RegionDemographics demo, GeographicProvince province)
        {
            Faction best = null; float share = 0f;
            if (demo?.factionShares != null)
                foreach (var kv in demo.factionShares)
                    if (kv.Value > share) { share = kv.Value; best = kv.Key; }
            if (best != null) return best;

            // fall back to the province's first listed owner.
            if (province?.owningFactionIds != null && province.owningFactionIds.Count > 0)
            {
                List<Faction> all = Find.FactionManager?.AllFactionsListForReading;
                if (all != null)
                    for (int i = 0; i < all.Count; i++)
                        if (all[i] != null && all[i].GetUniqueLoadID() == province.owningFactionIds[0]) return all[i];
            }
            return null;
        }

        private static float ReadShare(float[] arr, int index, float fallback)
            => (arr != null && index >= 0 && index < arr.Length) ? arr[index] : fallback;

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        private static float Max(float a, float b) => a > b ? a : b;
    }
}
