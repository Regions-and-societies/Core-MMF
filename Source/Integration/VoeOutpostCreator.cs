using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RegionsAndSocieties.Sizing;
using Verse;

namespace RegionsAndSocieties.Integration
{
    /// <summary>
    /// Builds a Vanilla Outposts Expanded outpost (<c>Outposts.Outpost</c>) for a faction on a tile.
    /// The only place in the mod that names a VOE type, resolved by reflection so R&amp;T still loads
    /// with VOE absent — the same soft-dependency discipline the adapters and the Empire patches keep.
    ///
    /// <para>The recipe was established against the decompiled 1.6 <c>Outposts.dll</c>:
    /// <c>WorldObjectMaker.MakeWorldObject(def)</c> → set Tile → <c>SetFaction</c> (must be non-null,
    /// or the world-map material getter throws) → set the public <c>Name</c> field →
    /// <c>Find.WorldObjects.Add</c> → attach at least one occupant with the public <c>AddPawn</c>. An
    /// outpost that ends a tick with no occupants deletes itself, so the occupant is not optional.</para>
    ///
    /// <para>The archetype picks the def, and thematic selection already matches terrain
    /// (mining in hills, logging in forest), so VOE's own <c>CanSpawnOnWithExt</c> is deliberately not
    /// consulted: that check validates a would-be caravan's pawn skills and headcount, not the tile,
    /// and would reject every seeding here for having no caravan. The constraint-free
    /// <c>Outpost_Encampment</c> is the fallback whenever a themed def is missing.</para>
    /// </summary>
    public class VoeOutpostCreator : IHoldingCreator
    {
        public string CreatorId => "voe";

        // Matches the VOE read adapter's priority so the read and write sides read as one integration.
        public int Priority => 110;

        private static Type outpostType;
        private static bool outpostTypeResolved;

        /// <summary>Resolve <c>Outposts.Outpost</c> once, lazily — VEF force-loads late, so a ctor-time
        /// lookup could miss it; a seeding-time lookup never does.</summary>
        private static Type OutpostType()
        {
            if (!outpostTypeResolved)
            {
                outpostType = AccessTools.TypeByName("Outposts.Outpost");
                outpostTypeResolved = true;
            }
            return outpostType;
        }

        public bool IsActive
        {
            get
            {
                return WorldObjectIntegrationSettings.masterEnabled
                    && WorldObjectIntegrationSettings.voeEnabled
                    && OutpostType() != null;
            }
        }

        public bool CanCreate(WorldObjectKind kind) => kind == WorldObjectKind.Outpost;

        public bool TryCreate(WorldObjectKind kind, OutpostArchetype archetype, Faction faction, int tile, out WorldObject created)
        {
            created = null;
            if (kind != WorldObjectKind.Outpost || faction == null) return false;

            Type type = OutpostType();
            if (type == null)
            {
                Log.ErrorOnce("[RegionsAndSocieties] VoeOutpostCreator: Outposts.Outpost did not resolve; VOE outpost seeding is off. This is expected without Vanilla Outposts Expanded.", 0x5B0101);
                return false;
            }

            WorldObjectDef def = ResolveDef(type, archetype);
            if (def == null)
            {
                Log.ErrorOnce("[RegionsAndSocieties] VoeOutpostCreator: no VOE outpost WorldObjectDef found (looked for the archetype def and Outpost_Encampment). Seeding cannot proceed.", 0x5B0102);
                return false;
            }

            WorldObject wo = WorldObjectMaker.MakeWorldObject(def);
            if (wo == null || !type.IsInstanceOfType(wo))
            {
                Log.ErrorOnce("[RegionsAndSocieties] VoeOutpostCreator: def '" + def.defName + "' did not produce an Outposts.Outpost instance.", 0x5B0103);
                return false;
            }

            wo.Tile = tile;
            wo.SetFaction(faction);
            SetName(type, wo, faction);
            Find.WorldObjects.Add(wo);

            if (!AttachOccupant(type, wo, faction, tile))
            {
                // No occupant means the outpost self-destructs next tick; better to remove it now
                // cleanly than to leave a doomed object and an abandonment letter at world start.
                if (!wo.Destroyed) wo.Destroy();
                Log.ErrorOnce("[RegionsAndSocieties] VoeOutpostCreator: could not attach an occupant; outpost removed. AddPawn(Pawn) may have changed in this VOE build.", 0x5B0104);
                return false;
            }

            created = wo;
            return true;
        }

        /// <summary>
        /// Pick the VOE def for an archetype, falling back to the constraint-free Encampment and then
        /// to any def whose class is an Outpost, so a renamed or reduced VOE def set still yields
        /// something placeable rather than a hard failure.
        /// </summary>
        private static WorldObjectDef ResolveDef(Type outpostBaseType, OutpostArchetype archetype)
        {
            WorldObjectDef byName = ValidOutpostDef(outpostBaseType, DefName(archetype));
            if (byName != null) return byName;

            WorldObjectDef encampment = ValidOutpostDef(outpostBaseType, "Outpost_Encampment");
            if (encampment != null) return encampment;

            foreach (WorldObjectDef d in DefDatabase<WorldObjectDef>.AllDefsListForReading)
            {
                if (d?.worldObjectClass != null && outpostBaseType.IsAssignableFrom(d.worldObjectClass))
                {
                    return d;
                }
            }
            return null;
        }

        private static WorldObjectDef ValidOutpostDef(Type outpostBaseType, string defName)
        {
            if (string.IsNullOrEmpty(defName)) return null;
            WorldObjectDef d = DefDatabase<WorldObjectDef>.GetNamedSilentFail(defName);
            if (d?.worldObjectClass != null && outpostBaseType.IsAssignableFrom(d.worldObjectClass)) return d;
            return null;
        }

        private static string DefName(OutpostArchetype archetype)
        {
            switch (archetype)
            {
                case OutpostArchetype.Mining: return "Outpost_Mining";
                case OutpostArchetype.Logging: return "Outpost_Logging";
                case OutpostArchetype.Farming: return "Outpost_Farming";
                case OutpostArchetype.Hunting: return "Outpost_Hunting";
                default: return "Outpost_Encampment";
            }
        }

        private static void SetName(Type outpostBaseType, WorldObject wo, Faction faction)
        {
            FieldInfo nameField = AccessTools.Field(outpostBaseType, "Name");
            if (nameField == null) return;

            string name = null;
            try
            {
                RulePackDef maker = faction?.def?.settlementNameMaker;
                if (maker != null) name = NameGenerator.GenerateName(maker);
            }
            catch
            {
                name = null;
            }
            if (string.IsNullOrEmpty(name)) name = (faction?.Name ?? "Outpost") + " outpost";

            nameField.SetValue(wo, name);
        }

        /// <summary>
        /// Generate one pawn for the faction and hand it to the outpost's public <c>AddPawn</c>, which
        /// takes ownership (it removes the pawn from any caravan or the world-pawn pool and deep-saves
        /// it inside the outpost). Returns false if AddPawn cannot be found or reports failure.
        /// </summary>
        private static bool AttachOccupant(Type outpostBaseType, WorldObject wo, Faction faction, int tile)
        {
            MethodInfo addPawn = AccessTools.Method(outpostBaseType, "AddPawn", new[] { typeof(Pawn) });
            if (addPawn == null) return false;

            PawnKindDef kind = faction.def?.basicMemberKind ?? PawnKindDefOf.Colonist;
            Pawn pawn = null;
            try
            {
                pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction, PawnGenerationContext.NonPlayer));
                object result = addPawn.Invoke(wo, new object[] { pawn });
                bool ok = !(addPawn.ReturnType == typeof(bool)) || (result is bool b && b);
                if (!ok)
                {
                    DiscardPawn(pawn);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[RegionsAndSocieties] VoeOutpostCreator: AddPawn threw: " + ex, 0x5B0105);
                DiscardPawn(pawn);
                return false;
            }
        }

        private static void DiscardPawn(Pawn pawn)
        {
            if (pawn == null) return;
            try
            {
                if (Find.WorldPawns != null && Find.WorldPawns.Contains(pawn))
                {
                    Find.WorldPawns.RemovePawn(pawn);
                }
                if (!pawn.Destroyed) pawn.Destroy();
            }
            catch
            {
                // A pawn we could not clean up is a leak, not a crash; swallow so seeding continues.
            }
        }
    }
}
