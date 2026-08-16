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
            list.Add(VanillaOutpostsExpanded());
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

    }
}
