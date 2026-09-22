using System;
using RimWorld;
using Verse;

namespace RegionsAndSocieties.Demographics
{
    /// <summary>The home-region demographics handed to a consumer when a pawn is generated "from" a region
    /// (#28). Everything Core has already modelled about where the pawn is from, for the consumer to map onto
    /// the pawn — education → skills, SES → gear, and it can lean on the age/xenotype already modelled.</summary>
    public class PawnDemographicContext
    {
        public Pawn pawn;
        public Faction faction;               // the pawn's faction, or null
        public int homeTile;                  // the world tile the pawn was generated for, or -1 (faction-wide)
        public RegionDemographics demographics;   // the home region's (or faction's) demographics — never null when fired
        public bool isPlayerPawn;             // so a consumer can leave the player's own colonists alone
    }

    /// <summary>
    /// The public seam a consumer (a CP) drives to make generated pawns inherit their home region's
    /// demographics (#28). The demographic axes are modelled and displayed by Core but otherwise inert —
    /// this is where they get teeth: a visitor/trader/quest-pawn/recruit from an Advanced-education region
    /// can roll higher skills, one from an illiterate tribe lower. Core simulates and hands over the numbers;
    /// the consumer decides what to do with the pawn.
    ///
    /// <para>No-op with nothing hooked — the base game is unchanged and Core pays nothing (the generation
    /// patch checks <see cref="HasConsumer"/> and returns before any work). Same reflection-friendly pattern
    /// as <see cref="DemographicHooks"/> / PopulationDynamics: a consumer sets <see cref="OnPawnGenerated"/>
    /// after load. Core never rewrites a pawn itself; it only reports.</para>
    /// </summary>
    public static class PawnDemographicHooks
    {
        /// <summary>Set by a consumer to receive every pawn generated from a region, with that region's
        /// demographics. Unset = no-op. A consumer that wants to leave the player's own colonists untouched
        /// checks <see cref="PawnDemographicContext.isPlayerPawn"/>.</summary>
        public static Action<PawnDemographicContext> OnPawnGenerated;

        /// <summary>True when a consumer is listening — the generation patch's fast-out, so the base game and
        /// a no-consumer install pay nothing.</summary>
        public static bool HasConsumer => OnPawnGenerated != null;

        /// <summary>Deliver one pawn's context to the consumer, swallowing (and logging) any throw so a
        /// consumer bug never breaks pawn generation.</summary>
        public static void Fire(PawnDemographicContext ctx)
        {
            Action<PawnDemographicContext> h = OnPawnGenerated;
            if (h == null || ctx == null) return;
            try { h(ctx); }
            catch (Exception e) { Log.Error("[RegionsAndSocieties] PawnDemographicHooks consumer threw: " + e); }
        }
    }
}
