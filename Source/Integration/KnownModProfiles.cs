using System.Collections.Generic;

namespace RegionsAndSocieties.Integration
{
    /// <summary>
    /// The declarative table of mods Regions &amp; Territories knows how to govern.
    ///
    /// Adding support for "another mod that acts like Empire Refactored" should be an entry here.
    /// Nothing outside this file and the adapters may name a foreign type.
    /// </summary>
    public static class KnownModProfiles
    {
        public static List<WorldObjectAdapterProfile> All()
        {
            var list = new List<WorldObjectAdapterProfile>();
            list.Add(Empire());
            list.Add(VanillaOutpostsExpanded());
            list.Add(VanillaFactionsExpanded());
            list.Add(VanillaFactionsExpandedMedieval());
            list.Add(WorldDomination());
            return list;

            // #71: the rest of the VFE faction suite needs NO profile — verified by reflection over the
            // 1.6 assemblies, none of these introduce a WorldObject subclass of their own; their faction
            // bases are plain vanilla Settlement objects with modded defs, which the vanilla adapter
            // already classifies. Recording them explicitly rather than shipping speculative profiles
            // (the standing #31/#33 discipline):
            //   oskarpotocki.vfe.empire      (VFEEmpire.dll)     — Honor/HonorWorker types, none a WorldObject
            //   settlersmodule               (VFE_Settlers.dll)  — only a SitePartWorker, not a WorldObject
            //   oskarpotocki.vfe.classical   (VFEC.dll)          — no WorldObject subclass at all
            //   oskarpotocki.vfe.deserters   (VFED.dll)          — only SitePartWorkers
            //   oskarpotocki.vfe.tribals     (VFETribals.dll)    — only a ThingComp (CompFireOverlay)
            //   oskarpotocki.vfe.insectoid2  (VFEInsectoids.dll) — only a SitePartWorker (insect hive site)
            // Only VFE Medieval 2 contributes one (VFEMedieval.MerchantGuild), handled above.
        }

        /// <summary>
        /// Empire Refactored (packageId Matathias.Empire, assembly namespace FactionColonies).
        /// Settlements are WorldSettlementFC, which derives from vanilla Settlement.
        /// </summary>
        public static WorldObjectAdapterProfile Empire()
        {
            var p = new WorldObjectAdapterProfile
            {
                adapterId = "empire",
                packageId = "Matathias.Empire",
                displayName = "Empire Refactored",
                priority = 100,
                markerTypes = new[]
                {
                    "FactionColonies.FindFC",
                    "FactionColonies.WorldSettlementFC"
                },
                // Empire has no concept called "population" — the three names previously listed here
                // (population / Population / settlementPopulation) do not appear anywhere in its
                // source, so TryGetPopulation returned false for every Empire settlement and every
                // consumer read a plausible zero (#30).
                //
                // What it has is workers: a public double property on WorldSettlementFC, summed from
                // the workers assigned to each ResourceFC. workersMax is the capacity for the same
                // quantity. TryGetInt already narrows a double, so these read directly with no
                // adapter change.
                //
                // Read against Empire Refactored 1.6.20, whose Workshop copy ships its source.
                populationMembers = new[] { "workers", "workersMax" },
                // settlementLevel is declared on WorldSettlementFC itself, not on a separate
                // SettlementFC — there is no such type in Empire. The old comment describing one was
                // the reason nobody questioned the population names beside it.
                levelMembers = new[] { "settlementLevel" },
                // maxSettlementLevel exists, but on WorldSettlementDef rather than on the world
                // object, and the adapter only reads members of the instance's own type. The real
                // ceiling is min(FCSettings.settlementMaxLevel, def.maxSettlementLevel) — per-def and
                // player-configurable, so no single number is right for every settlement. 10 is the
                // clamp Empire's own tests use and stays the assumed ceiling.
                maxLevelMembers = new string[0],
                assumedMaxLevel = 10,
                // Empire runs the player's own colonies as WorldSettlementFC objects.
                playerOwnedByDefault = true,
                enabledGetter = () => WorldObjectIntegrationSettings.masterEnabled && WorldObjectIntegrationSettings.empireEnabled
            };

            p.Rule(TypeMatch.ExactType, "FactionColonies.WorldSettlementFC", WorldObjectKind.Settlement)
             .Rule(TypeMatch.NamespacePrefix, "FactionColonies.WorldSettlement", WorldObjectKind.Settlement)
             .Rule(TypeMatch.TypeNameContains, "MilitaryFC", WorldObjectKind.Military);

            return p;
        }

        /// <summary>
        /// Vanilla Outposts Expanded (packageId vanillaexpanded.outposts, namespace Outposts).
        /// All outposts derive from Outposts.Outpost; PawnCount is the resident count.
        /// </summary>
        public static WorldObjectAdapterProfile VanillaOutpostsExpanded()
        {
            var p = new WorldObjectAdapterProfile
            {
                adapterId = "voe",
                packageId = "vanillaexpanded.outposts",
                displayName = "Vanilla Outposts Expanded",
                priority = 110,
                markerTypes = new[] { "Outposts.Outpost" },
                populationMembers = new[] { "PawnCount", "occupants" },
                levelMembers = new[] { "level", "Level", "upgradeLevel" },
                assumedMaxLevel = 0,
                enabledGetter = () => WorldObjectIntegrationSettings.masterEnabled && WorldObjectIntegrationSettings.voeEnabled
            };

            p.Rule(TypeMatch.ExactType, "Outposts.Outpost", WorldObjectKind.Outpost)
             .Rule(TypeMatch.NamespacePrefix, "Outposts.", WorldObjectKind.Outpost);

            return p;
        }

        /// <summary>
        /// Vanilla Expanded Framework (packageId <c>OskarPotocki.VanillaFactionsExpanded.Core</c>).
        ///
        /// <para><b>The assembly was renamed.</b> Under 1.6 this mod ships <c>VEF.dll</c> with
        /// namespaces <c>VEF.Planet</c>, <c>VEF.Factions</c>, <c>VEF.Buildings</c> and so on. The
        /// string <c>VFECore</c> does not occur anywhere in it. Every marker this profile previously
        /// declared resolved to nothing, so the adapter was inert for as long as it has existed and
        /// nothing said so (#31).</para>
        ///
        /// <para><b>It contributes exactly one world object of its own.</b> Enumerated from the live
        /// assembly: <c>Outposts.Outpost</c>, <c>Outposts.Outpost_ChooseResult</c> and
        /// <c>VEF.Planet.MovingBase</c>. The first two are Vanilla Outposts Expanded, which now ships
        /// inside the framework and is already governed by the VOE profile at priority 110. So the
        /// premise this profile was written on — that the framework adds settlement-like and
        /// camp-like world objects — is simply not true of 1.6.</para>
        ///
        /// <para><b>Which is why the rules are now narrow, and that is the point of the change
        /// rather than a detail of it.</b> The previous rules were four bare
        /// <see cref="TypeMatch.TypeNameContains"/> matches on "Settlement", "Camp", "Outpost" and
        /// "Base". Rules are not scoped to the declaring mod's assembly — <c>TryClassify</c> offers
        /// every world object to every active adapter in priority order and takes the first
        /// non-Unknown answer. At priority 120 this adapter runs before World Domination's 130, so
        /// "Settlement" would have claimed <c>WorldObject_Traveler_SettlementBuy</c> and
        /// <c>WorldObject_Traveler_SettlementGift</c> — moving purchase parties — as settlements
        /// holding territory, and "Base" would have taken <c>MovingBase</c> as Military. Fixing the
        /// marker names without narrowing the rules would have switched that on (see #33).</para>
        ///
        /// <para>A base that moves cannot hold a province stably, so <c>MovingBase</c> is a
        /// <see cref="WorldObjectKind.Caravan"/> — the same judgement made for World Domination's
        /// travelers, and what the vanilla adapter does with caravans.</para>
        /// </summary>
        public static WorldObjectAdapterProfile VanillaFactionsExpanded()
        {
            var p = new WorldObjectAdapterProfile
            {
                adapterId = "vfe",
                packageId = "OskarPotocki.VanillaFactionsExpanded.Core",
                displayName = "Vanilla Expanded Framework",
                priority = 120,
                markerTypes = new[] { "VEF.Planet.MovingBase" },
                // Empty by observation, not by omission: neither PawnCount nor population exists on
                // MovingBase, and it is the only world object this mod contributes. Declaring names
                // that do not resolve is what left Empire reading zero for every settlement (#30);
                // declaring none says "this mod publishes no headcount", which is true and is what
                // the accessor's documented default already means.
                populationMembers = new string[0],
                enabledGetter = () => WorldObjectIntegrationSettings.masterEnabled && WorldObjectIntegrationSettings.vfeEnabled
            };

            // One rule, for the one type. No namespace fallback: VEF.Planet contains nothing else
            // today, and a speculative rule for types that do not exist is exactly what this profile
            // is being repaired for. WorldObjectClassifier logs anything it cannot classify, so a
            // future VEF world object announces itself rather than being silently miscategorised.
            p.Rule(TypeMatch.ExactType, "VEF.Planet.MovingBase", WorldObjectKind.Caravan);

            return p;
        }

        /// <summary>
        /// Vanilla Factions Expanded — Medieval 2 (packageId <c>oskarpotocki.vfe.medieval2</c>,
        /// assembly/namespace <c>VFEMedieval</c>).
        ///
        /// <para>Its faction bases are plain vanilla <c>Settlement</c> objects, already classified by the
        /// vanilla adapter — no profile is needed for those. The one world object it introduces is
        /// <c>VFEMedieval.MerchantGuild</c>, a subclass of <c>VEF.Planet.MovingBase</c>
        /// (<c>MovingBase : MapParent : WorldObject</c>, verified by reflection over the 1.6 assemblies).
        /// A merchant guild that travels the map cannot hold a province, so it is a
        /// <see cref="WorldObjectKind.Caravan"/> — the same judgement the VFE Core profile makes for
        /// <c>MovingBase</c> itself, whose <see cref="TypeMatch.ExactType"/> rule does not reach this
        /// subclass in another namespace. No population or level member: a moving base publishes no
        /// headcount R&amp;T reads.</para>
        /// </summary>
        public static WorldObjectAdapterProfile VanillaFactionsExpandedMedieval()
        {
            var p = new WorldObjectAdapterProfile
            {
                adapterId = "vfe_medieval",
                packageId = "oskarpotocki.vfe.medieval2",
                displayName = "VFE - Medieval 2",
                priority = 121,   // just after VFE Core (120), before World Domination (130)
                markerTypes = new[] { "VFEMedieval.MerchantGuild" },
                populationMembers = new string[0],
                enabledGetter = () => WorldObjectIntegrationSettings.masterEnabled && WorldObjectIntegrationSettings.vfeMedievalEnabled
            };

            p.Rule(TypeMatch.ExactType, "VFEMedieval.MerchantGuild", WorldObjectKind.Caravan);

            return p;
        }

        /// <summary>
        /// World Domination 2.0 (TSA, packageId <c>TSA.WorldDominationExperimental</c>, assembly and
        /// namespace <c>TSA_WorldDomination</c>).
        ///
        /// <para>Pinned against the installed mod. The previous version of this profile was written
        /// without the mod present and was wrong in every particular — the namespace
        /// (<c>WorldDomination.</c>), both marker types, the author, and the claim that it depends on
        /// Vanilla Outposts Expanded. It declares Harmony and VFE Core only, so nothing of its
        /// resolves through the VOE adapter and this profile carries the whole integration.</para>
        ///
        /// <para><b>The rule ordering is load-bearing.</b> Roughly half of this mod's world objects
        /// are <c>WorldObject_Traveler</c> subclasses — raids, drop pods, road builders, expansion
        /// and purchase parties. They move. The old namespace-prefix rule mapped every
        /// <c>WorldDomination.*</c> type to <see cref="WorldObjectKind.Settlement"/>, which would have
        /// made an in-flight raid a territory-holding settlement. Travelers are matched first and
        /// classified as <see cref="WorldObjectKind.Caravan"/>, which is what they are.
        /// <c>WorldObject_Traveler_Outpost_Delivery</c> contains both words; travelling to an outpost
        /// is not being one.</para>
        ///
        /// <para><b>There is no level member, and that is a finding rather than a gap.</b> This mod
        /// has two independent ladders and neither is a scalar on the world object. Settlement grade
        /// is encoded in the def name (<c>TSA_Generic_T1_Farming</c> through
        /// <c>TSA_Generic_T4_Citadel</c>), and upgrades are 34 separate purchases across lines each
        /// carrying their own <c>lineTier</c> of 1-3. A single <c>assumedMaxLevel</c> cannot describe
        /// that, so the level input stays at 0 — permanently unused for this mod, by observation.</para>
        ///
        /// <para>Its faction bases are vanilla <c>Settlement</c> objects with modded defs, so the
        /// vanilla adapter already classifies them correctly; this profile covers only the types the
        /// mod introduces.</para>
        /// </summary>
        public static WorldObjectAdapterProfile WorldDomination()
        {
            var p = new WorldObjectAdapterProfile
            {
                adapterId = "worlddomination",
                packageId = "TSA.WorldDominationExperimental",
                displayName = "World Domination 2.0",
                priority = 130,
                markerTypes = new[]
                {
                    "TSA_WorldDomination.WorldObject_WD_Outpost",
                    "TSA_WorldDomination.WorldObject_Traveler"
                },
                // Pinned by Regions_AdapterProfilesMatchRealAssemblies against the installed mod:
                // of the four candidates tried (pawnCount, PawnCount, occupants, garrison), these
                // two resolve and the other two do not. The mod ships no source, so this was
                // established by live reflection rather than by reading it.
                //
                // They are the same names Vanilla Outposts Expanded uses, which is worth knowing but
                // not worth assuming a shared base class over: World Domination declares Harmony and
                // VFE Core as its dependencies, not VOE. Treat the coincidence as a coincidence.
                populationMembers = new[] { "PawnCount", "occupants" },
                levelMembers = new string[0],
                assumedMaxLevel = 0,
                enabledGetter = () => WorldObjectIntegrationSettings.masterEnabled && WorldObjectIntegrationSettings.worldDominationEnabled
            };

            // Order matters: first match wins, and every Traveler_Outpost_* type would otherwise be
            // caught by the outpost rules below.
            p.Rule(TypeMatch.TypeNameContains, "Traveler", WorldObjectKind.Caravan)
             .Rule(TypeMatch.ExactType, "TSA_WorldDomination.WorldObject_WD_Outpost", WorldObjectKind.Outpost)
             .Rule(TypeMatch.TypeNameContains, "Outpost", WorldObjectKind.Outpost)
             // Anything else this mod introduces later: an outpost is the safer default than a
             // settlement, since outposts carry less territorial weight than settlements do.
             .Rule(TypeMatch.NamespacePrefix, "TSA_WorldDomination.", WorldObjectKind.Outpost);

            return p;
        }
    }
}
