using System;
using System.Collections.Generic;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>The broad character a faction's people have, beyond their raw tech level (#27).</summary>
    public enum FactionArchetype
    {
        Generic = 0,      // no strong character — neutral modifiers
        Outlander,        // settled productive townsfolk
        Tribe,            // oral-culture tribal peoples
        Raider,           // pirates/raiders — live off others, not off knowledge or production
        Imperial,         // stratified high-culture polity (the Empire)
        Merchant,         // wealthy trading society
        Scavenger,        // practical salvagers — technical but not schooled
        AncientElite,     // ancient, elite, long-lived remnants
        Cult,             // secretive, anti-rational cult
    }

    /// <summary>
    /// Faction-character modifiers to the demographic model (#27). Tech level alone made a pirate band
    /// read as an educated, prosperous populace — pirates run on <b>looted</b> industrial gear, they do
    /// not school or produce. This layer gives each base/DLC faction a <b>character</b> that skews
    /// knowledge (education) and wealth (socioeconomic tier) away from what its tech level alone implies.
    ///
    /// <para>Pure: defName + a couple of def flags in, two scalars out — no game types — so it is
    /// unit-tested without a game and the same table drives every faction. Base game + DLC factions are
    /// classified by defName; anything unknown (a modded or Vanilla-Factions-Expanded faction) falls back
    /// to a trait-based guess, which a compatibility patch can override with a better mapping. First-pass
    /// values, tunable.</para>
    /// </summary>
    public static class FactionCharacterRules
    {
        /// <summary>The two modifiers a character applies: a knowledge skew added to the education
        /// research-skew (−1 pulls attainment down, +1 up), and a multiplier on base wealth (&lt;1 poorer,
        /// &gt;1 richer) feeding the socioeconomic tier.</summary>
        public struct Character
        {
            public float knowledgeSkew;
            public float wealthMultiplier;
            // #29: how rigidly stratified the society is, 0 (egalitarian, broad middle) to 1 (a rigid
            // noble/commoner/serf order). Higher = the education/wealth shape reads bimodal — a thin elite
            // over a large underclass with the skilled middle hollowed — which throttles growth.
            public float stratification;
            // #29: how much the faction represses an underclass into slavery/serfdom (0..1). Feeds the
            // stage's slavery stance, which caps enslaved cohorts' education at Primary — hollowing the
            // middle from the bottom, on top of the stratification reshaping from the top.
            public float slaveryStance;
            public Character(float knowledgeSkew, float wealthMultiplier, float stratification = 0.2f, float slaveryStance = 0f)
            {
                this.knowledgeSkew = knowledgeSkew;
                this.wealthMultiplier = wealthMultiplier;
                this.stratification = stratification;
                this.slaveryStance = slaveryStance;
            }
        }

        /// <summary>The modifiers for an archetype. Raiders and cults read down, traders and empires up;
        /// stratification and slavery are highest for rigid, repressive polities (Empire, raiders, cults).</summary>
        public static Character CharacterOf(FactionArchetype a)
        {
            switch (a)
            {
                case FactionArchetype.Outlander:    return new Character(+0.10f, 1.05f, 0.15f, 0.00f);   // fairly egalitarian townsfolk
                case FactionArchetype.Tribe:        return new Character(-0.15f, 0.85f, 0.10f, 0.05f);   // egalitarian kin society
                case FactionArchetype.Raider:       return new Character(-0.60f, 0.65f, 0.45f, 0.45f);   // warlord over enslaved grunts
                case FactionArchetype.Imperial:     return new Character(+0.45f, 1.30f, 0.80f, 0.35f);   // rigid order, serfs/slaves
                case FactionArchetype.Merchant:     return new Character(+0.20f, 1.45f, 0.35f, 0.10f);   // wealth gradient, working middle
                case FactionArchetype.Scavenger:    return new Character(-0.05f, 0.95f, 0.15f, 0.05f);   // practical, flat
                case FactionArchetype.AncientElite: return new Character(+0.50f, 1.15f, 0.70f, 0.20f);   // an elite over few
                case FactionArchetype.Cult:         return new Character(-0.35f, 0.80f, 0.55f, 0.30f);   // priesthood over followers
                default:                            return new Character(0f, 1f, 0.20f, 0.00f);
            }
        }

        /// <summary>Where a faction's archetype came from, for the demographics debug dump (#55).</summary>
        public enum ArchetypeSource
        {
            Registered,   // a compatibility patch (or core) registered it by defName
            BuiltIn,      // core's base-game / DLC defName table
            TraitGuess,   // unknown faction: guessed from hostility + tech level
        }

        private static readonly Dictionary<string, FactionArchetype> registered =
            new Dictionary<string, FactionArchetype>(StringComparer.Ordinal);

        /// <summary>
        /// Register (or replace) the archetype for a faction by defName (#55). Consulted by
        /// <see cref="Classify(string,int,bool)"/> before the built-in table and the trait guess, so a
        /// compatibility patch can give a modded faction its real character instead of the guess. Later
        /// registrations for the same defName win. Called from a patch's Mod constructor; static for
        /// the process lifetime.
        /// </summary>
        public static void RegisterArchetype(string factionDefName, FactionArchetype archetype)
        {
            if (string.IsNullOrEmpty(factionDefName)) return;
            registered[factionDefName] = archetype;
        }

        public static bool TryGetRegisteredArchetype(string factionDefName, out FactionArchetype archetype)
        {
            archetype = FactionArchetype.Generic;
            return !string.IsNullOrEmpty(factionDefName) && registered.TryGetValue(factionDefName, out archetype);
        }

        /// <summary>Every registered defName, for the debug report.</summary>
        public static IEnumerable<string> RegisteredDefNames
        {
            get { return registered.Keys; }
        }

        /// <summary>Test-only: forget every registration.</summary>
        public static void ResetRegistrations()
        {
            registered.Clear();
        }

        /// <summary>
        /// Classify a faction into its archetype. A registered archetype (#55) wins; then known base-game
        /// and DLC factions are matched by defName; an unknown faction (modded / VFE) falls back to a
        /// trait guess: a permanent-enemy band of medieval-or-better tech reads as raiders, a
        /// neolithic-or-below faction as a tribe, everything else neutral. <paramref name="techLevel"/>
        /// is RimWorld's TechLevel ordinal (Animal=1 … Archotech=7).
        /// </summary>
        public static FactionArchetype Classify(string defName, int techLevel, bool permanentEnemy)
        {
            return Classify(defName, techLevel, permanentEnemy, out _);
        }

        /// <summary>As <see cref="Classify(string,int,bool)"/>, also reporting where the answer came from.</summary>
        public static FactionArchetype Classify(string defName, int techLevel, bool permanentEnemy, out ArchetypeSource source)
        {
            if (TryGetRegisteredArchetype(defName, out FactionArchetype registeredArchetype))
            {
                source = ArchetypeSource.Registered;
                return registeredArchetype;
            }

            source = ArchetypeSource.BuiltIn;
            switch (defName)
            {
                // Raiders — Core + Ideology + Biotech pirate variants.
                case "Pirate":
                case "CannibalPirate":
                case "PirateWaster":
                case "PirateYttakin":
                    return FactionArchetype.Raider;

                // Settled outlander unions (Core + Biotech pig union).
                case "OutlanderCivil":
                case "OutlanderRough":
                case "OutlanderRoughPig":
                    return FactionArchetype.Outlander;

                // Tribes — Core + Ideology + Biotech variants.
                case "TribeCivil":
                case "TribeRough":
                case "TribeSavage":
                case "TribeCannibal":
                case "NudistTribe":
                case "TribeRoughNeanderthal":
                case "TribeSavageImpid":
                    return FactionArchetype.Tribe;

                case "Empire":        return FactionArchetype.Imperial;      // Royalty
                case "TradersGuild":  return FactionArchetype.Merchant;      // Odyssey
                case "Salvagers":     return FactionArchetype.Scavenger;     // Odyssey

                // Ancient, elite remnants (Core ancients + Biotech sanguophages).
                case "Ancients":
                case "AncientsHostile":
                case "Sanguophages":
                    return FactionArchetype.AncientElite;

                case "HoraxCult":     return FactionArchetype.Cult;          // Anomaly
            }

            // Unknown faction (modded / VFE): guess from traits; a CP overrides via RegisterArchetype.
            source = ArchetypeSource.TraitGuess;
            if (permanentEnemy && techLevel >= 3) return FactionArchetype.Raider;
            if (techLevel <= 2) return FactionArchetype.Tribe;
            return FactionArchetype.Generic;
        }
    }
}
