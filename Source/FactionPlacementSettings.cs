using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RegionsAndSocieties
{
    public class FactionPlacementProfile : IExposable
    {
        public string factionDefName;
        public float mineralWeight = 1.0f;
        public float nutritionWeight = 1.0f;
        public float forageWeight = 1.0f;
        public float grazingWeight = 1.0f;
        public float huntingWeight = 1.0f;
        public float marginWeight = 0.0f;
        public IntRange baseCountRange = new IntRange(5, 15);
        public int placementOrder = 3;

        /// <summary>#46: how many territories may cluster together — the largest contiguous body this
        /// faction builds: 1, 3, 5, 7 or 9 (= no limit). 0 = unset, resolved to the faction kind's
        /// default on first read so a profile saved before 0.4.0 picks up the owner's table.</summary>
        public int clusterSize = 0;

        public FactionPlacementProfile() { }

        public FactionPlacementProfile(string defName, float mineral, float nutrition, float forage, float grazing, float hunting, float margin, int minB, int maxB, int order)
        {
            this.factionDefName = defName;
            this.mineralWeight = mineral;
            this.nutritionWeight = nutrition;
            this.forageWeight = forage;
            this.grazingWeight = grazing;
            this.huntingWeight = hunting;
            this.marginWeight = margin;
            this.baseCountRange = new IntRange(minB, maxB);
            this.placementOrder = order;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref factionDefName, "factionDefName");
            Scribe_Values.Look(ref mineralWeight, "mineralWeight", 1.0f);
            Scribe_Values.Look(ref nutritionWeight, "nutritionWeight", 1.0f);
            Scribe_Values.Look(ref forageWeight, "forageWeight", 1.0f);
            Scribe_Values.Look(ref grazingWeight, "grazingWeight", 1.0f);
            Scribe_Values.Look(ref huntingWeight, "huntingWeight", 1.0f);
            Scribe_Values.Look(ref marginWeight, "marginWeight", 0.0f);
            Scribe_Values.Look(ref baseCountRange, "baseCountRange", new IntRange(5, 15));
            Scribe_Values.Look(ref placementOrder, "placementOrder", 3);
            Scribe_Values.Look(ref clusterSize, "clusterSize", 0);
        }
    }

    public class FactionPlacementSettings : ModSettings
    {
        public static Dictionary<string, FactionPlacementProfile> profiles = new Dictionary<string, FactionPlacementProfile>();
        public static int minRegionSize = 75;
        public static int maxRegionSize = 150;

        /// <summary>The world-partition algorithm applied to NEWLY generated worlds, by
        /// <see cref="Partition.IRegionPartitioner.AlgorithmId"/>. An existing save keeps the algorithm it
        /// was generated with (stamped on the world), so changing this never re-cuts a live map.</summary>
        public static string partitionAlgorithmId = Partition.RegionPartitionerRegistry.DefaultAlgorithmId;
        public static float maxThreatPercent = 0.50f;

        /// <summary>
        /// Dev-only knobs, not in the settings UI (set them in the mod-settings XML). They exist for the
        /// #38 worldgen perf matrix: <c>devQuicktestCoverage</c> above 0 overrides the planet coverage of a
        /// <c>-quicktest</c> launch (vanilla quicktest fixes it at 30%) and <c>devQuicktestSeed</c> pins the
        /// world seed, so a scripted run can generate the same world at 30/50/100%. Neither has any effect
        /// on a normal game.
        /// </summary>
        public static float devQuicktestCoverage = 0f;
        public static string devQuicktestSeed = "";

        /// <summary>
        /// #51: the single density knob — the target fraction of livable LAND area claimed by territories.
        /// Worldgen sizes total settlement volume to this (against the count of land provinces, the unit of
        /// claimed ground), instead of the old raw tile-count scaling that made planets wall-to-wall and
        /// exploded on large worlds. It is area-weighted (land provinces, ocean excluded) and drives the
        /// total both up and down, so the per-faction counts set only the distribution. Scribed under the
        /// legacy key so existing saves keep their value.
        /// </summary>
        public static float claimedLandAreaPercent = 0.50f;

        /// <summary>
        /// #19: how strongly territory growth prefers squaring off over spidering, 0..1. Candidate
        /// provinces below the desired embeddedness ratio have their suitability scaled down in
        /// proportion, blended in by this weight — 0 is the legacy purely-greedy behaviour, 1 the full
        /// shape penalty. A preference, never a rule: a cornered faction still takes the awkward
        /// province when its land is dramatically better.
        /// </summary>
        public static float territoryCompactness = 0.6f;

        /// <summary>
        /// Whether <b>newly generated</b> worlds enforce R&amp;T's settlement and outpost placement
        /// rules. Worlds already in progress decide for themselves on load and are not affected by
        /// this — a world built without the rules keeps compatibility mode, and one built with them
        /// keeps strict. See <c>SynapseRegionManager.StrictTerritorialOwnership</c>.
        /// </summary>
        public static bool strictTerritorialOwnershipDefault = true;

        /// <summary>
        /// Show the derivation breakdowns in region tooltips (ownership now; economics and produced
        /// goods later) so the numbers can be inspected without Development mode. Off by default (#54).
        /// </summary>
        public static bool showCalculationBreakdowns = false;

        /// <summary>True when calculation breakdowns should be shown — the setting, or Dev Mode.</summary>
        public static bool ShowCalculations => showCalculationBreakdowns || Prefs.DevMode;

        /// <summary>
        /// Which modifier opens a region comparison panel on click (#53): Shift+click when true,
        /// Ctrl+click when false. Configurable so it can be moved off a key that conflicts.
        /// </summary>
        public static bool regionPanelUseShift = false;

        /// <summary>
        /// How many region comparison panels may be open at once (#53). Default 2 for a side-by-side
        /// compare; raise it to experiment with more. When exceeded the oldest panel closes (FIFO).
        /// </summary>
        public static int maxRegionPanels = 2;

        /// <summary>
        /// Set when the player dismisses the "no map-mode framework loaded" popup with "Don't show this
        /// again" (#81), so the either-or warning never nags on subsequent launches once acknowledged.
        /// </summary>
        public static bool mapFrameworkWarningDismissed = false;

        /// <summary>#57: whether worldgen splits a scattered fractious faction (pirates, tribes, rough
        /// unions) into loosely-related regional kin sub-factions. Default on.</summary>
        public static bool splitScatteredFactions = true;

        /// <summary>#53: the master switch for the whole Societies layer — population, demographics and
        /// economy. Off means Regions only: the partition, territories, borders, placement and their map
        /// modes still run, but nothing models or draws population/demographics/economy, and none of it
        /// ticks. Default on. Read everywhere through <see cref="RegionsAndSocietiesMod.SocietiesEnabled"/>.</summary>
        public static bool societiesEnabled = true;

        /// <summary>#51: keep tiny land regions (&le; TinyRegionMaxTiles) instead of dropping them. Off
        /// (default) drops a 1-6 tile region — too small to serve a regional society — so its tiles become
        /// unassigned. On keeps it as a real, settle-able region that simply earns NO regional benefits
        /// (marked <see cref="GeographicProvince.benefitsSuppressed"/>): settlements may spawn there, but it
        /// gets no demographics/economy. A settlement/outpost speck is never orphaned either way.</summary>
        public static bool enableSmallRegions = false;

        /// <summary>#40 follow-up: how a large biome container is cut into regions. 0 = balanced cells
        /// (current), 1 = pie slices from the centroid, 2 = relaxed honeycomb (centroidal Voronoi). A
        /// comparison knob for now — flip it and regenerate to see each style.</summary>
        public static int subdivisionStyle = 0;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref minRegionSize, "minRegionSize", 75);
            Scribe_Values.Look(ref maxRegionSize, "maxRegionSize", 150);
            Scribe_Values.Look(ref maxThreatPercent, "maxThreatPercent", 0.50f);
            Scribe_Values.Look(ref devQuicktestCoverage, "devQuicktestCoverage", 0f);
            Scribe_Values.Look(ref devQuicktestSeed, "devQuicktestSeed", "");
            Scribe_Values.Look(ref claimedLandAreaPercent, "maxSettlementPercentOfRegions", 0.50f);
            Scribe_Values.Look(ref territoryCompactness, "territoryCompactness", 0.6f);
            Scribe_Values.Look(ref partitionAlgorithmId, "partitionAlgorithmId", Partition.RegionPartitionerRegistry.DefaultAlgorithmId);
            Scribe_Values.Look(ref strictTerritorialOwnershipDefault, "strictTerritorialOwnershipDefault", true);
            Scribe_Values.Look(ref showCalculationBreakdowns, "showCalculationBreakdowns", false);
            Scribe_Values.Look(ref regionPanelUseShift, "regionPanelUseShift", false);
            Scribe_Values.Look(ref maxRegionPanels, "maxRegionPanels", 2);
            Scribe_Values.Look(ref mapFrameworkWarningDismissed, "mapFrameworkWarningDismissed", false);
            Scribe_Values.Look(ref splitScatteredFactions, "splitScatteredFactions", true);
            Scribe_Values.Look(ref societiesEnabled, "societiesEnabled", true);
            Scribe_Values.Look(ref enableSmallRegions, "enableSmallRegions", false);
            Scribe_Values.Look(ref subdivisionStyle, "subdivisionStyle", 0);

            // 0.7: world-object governance / mod-integration switches.
            Integration.WorldObjectIntegrationSettings.ExposeData();


            List<FactionPlacementProfile> list = profiles.Values.ToList();
            Scribe_Collections.Look(ref list, "profiles", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && list != null)
            {
                profiles.Clear();
                foreach (var p in list)
                {
                    if (p.factionDefName != null)
                    {
                        profiles[p.factionDefName] = p;
                    }
                }
            }
        }

        public static FactionPlacementProfile GetProfile(FactionDef def)
        {
            if (def == null) return null;
            if (!profiles.TryGetValue(def.defName, out var p))
            {
                p = GetDefaultProfile(def);
                profiles[def.defName] = p;
            }
            // #46: a profile saved before cluster size existed carries 0; resolve it to the kind's default.
            if (p.clusterSize <= 0) p.clusterSize = DefaultClusterSize(def);
            return p;
        }

        /// <summary>#46: the owner's default cluster size for a faction — pirates and the Empire 3,
        /// tribes 5, rough unions 7, everyone else 9 (no limit).</summary>
        public static int DefaultClusterSize(FactionDef def)
        {
            if (def == null) return Placement.ClusteringRules.Unbounded;
            var kind = Placement.ClusteringRules.ClassifyKind(def.defName, def.label, (int)def.techLevel, def.permanentEnemy, def.hostileToFactionlessHumanlikes);
            return Placement.ClusteringRules.DefaultClusterSize(kind);
        }

        public static FactionPlacementProfile GetDefaultProfile(FactionDef def)
        {
            float mineral = 1.0f;
            float nutrition = 1.0f;
            float forage = 1.0f;
            float grazing = 1.0f;
            float hunting = 1.0f;
            float margin = 0.0f;
            int minB = 5;
            int maxB = 15;

            if (def.techLevel >= TechLevel.Spacer)
            {
                mineral = 2.5f;
                nutrition = 0.5f;
                forage = 0.1f;
                grazing = 0.1f;
                hunting = 0.2f;
                margin = 0.0f;
            }
            else if (def.techLevel == TechLevel.Industrial)
            {
                mineral = 1.0f;
                nutrition = 2.0f;
                forage = 0.2f;
                grazing = 0.8f;
                hunting = 0.8f;
                margin = 0.0f;
            }
            else
            {
                mineral = 0.2f;
                nutrition = 0.2f;
                forage = 2.0f;
                if (def.hostileToFactionlessHumanlikes || def.permanentEnemy)
                {
                    grazing = 0.2f;
                    hunting = 2.0f;
                }
                else
                {
                    grazing = 2.0f;
                    hunting = 0.2f;
                }
                margin = 0.1f;
            }

            int order = 3;
            if (def.defName == "Empire")
            {
                order = 2;
            }
            else if (def.techLevel == TechLevel.Industrial)
            {
                order = 1;
            }
            else if (def.techLevel >= TechLevel.Spacer)
            {
                order = 3;
            }
            else
            {
                order = 4;
            }

            if (def.hostileToFactionlessHumanlikes || def.permanentEnemy)
            {
                margin = 2.5f;
                minB = 3;
                maxB = 8;
            }

            var profile = new FactionPlacementProfile(def.defName, mineral, nutrition, forage, grazing, hunting, margin, minB, maxB, order);
            profile.clusterSize = DefaultClusterSize(def);   // #46
            return profile;
        }
    }
}
