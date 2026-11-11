using System.Collections.Generic;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>What the party puller's camp turn does after a party ranged pull attempt (owner 2026-11-10).</summary>
    public enum PartyPullFallback
    {
        /// <summary>The ranged pull runs, waits for its retry window, or retries an unreachable target: stand and report.</summary>
        Handled,
        /// <summary>Too far for the short proven approach: never melee-charge it; look for a nearer target.</summary>
        SkipTooFar,
        /// <summary>The designed close-in pull: contact fight, no ranged pull, or released after 3 unreachable tries.</summary>
        OrdinaryPull
    }

    public enum PullStallAction { None, Log, End }

    /// <summary>
    /// Party puller camp turn (owner 2026-11-10). The pre-arrival ranged-pull intercept stamped the search timer
    /// every tick inside the camp, so WorkCamp never ran for party pullers: no close-in pull after 3 unreachable
    /// tries, no empty-camp wait or move, and parties stood until the 15-minute watchdog teleported them home.
    /// </summary>
    public static class AutonomousPartyPullCamp
    {
        [ServerProperty("bots", "bot_party_puller_camp_turn",
            "Party puller camp turn (owner 2026-11-10): the ranged-pull intercept runs only while walking in, a puller next to an arrived leader counts as arrived, and the camp turn skips too-far targets and closes in after 3 unreachable tries.", true)]
        public static bool CampTurn = true;

        [ServerProperty("bots", "bot_party_pull_toofar_advance",
            "After about 45 s of only too-far targets, move the party's camp point onto the target's floor (at most 2 moves per camp per 10 minutes). Owner 2026-11-10.", true)]
        public static bool TooFarAdvance = true;

        [ServerProperty("bots", "bot_party_puller_empty_camp_wait",
            "A party puller that is not the leader waits beside the party at an empty outdoor camp instead of patrolling away from it. Owner 2026-11-10.", true)]
        public static bool PullerWaitsAtEmptyCamp = true;

        [ServerProperty("bots", "bot_party_pull_stall_log",
            "Log AUTONOMOUS_GROUP_PULL_STALL, naming the pull gate, when a grinding party has had no fight or experience for 6 minutes. Owner 2026-11-10.", true)]
        public static bool StallLog = true;

        [ServerProperty("bots", "bot_party_pull_stall_end",
            "End a grinding party cleanly when it has had no fight or experience for 11 minutes, instead of the 15-minute watchdog teleport. Owner 2026-11-10.", true)]
        public static bool StallEnd = true;

        [ServerProperty("bots", "bot_party_pull_claim_on_start",
            "A grouped bot claims its camp target only when a pull actually starts, not on too-far, waiting or unreachable turns (which locked monsters away from other parties). Owner 2026-11-10.", true)]
        public static bool ClaimOnPullStart = true;

        [ServerProperty("bots", "bot_party_pull_toofar_counts_as_empty",
            "A party camp turn that only skips a too-far target keeps the empty-camp clock running, so the 75 s wait and 180 s move still happen. Owner 2026-11-10.", true)]
        public static bool TooFarCountsAsEmpty = true;

        public const int InterceptRadius = 2000;
        public const long TooFarSkipMilliseconds = 60_000;
        public const long TooFarAdvanceAfterMilliseconds = 45_000;
        // A skip this much older than the last one is not "consecutive" (one too-far target is re-found every 60 s).
        public const long TooFarEpisodeGapMilliseconds = TooFarSkipMilliseconds + 15_000;
        public const long TooFarLogMilliseconds = 3 * 60_000L;
        public const int MaximumAdvancesPerWindow = 2;
        public const long AdvanceWindowMilliseconds = 10 * 60_000L;
        public const long StallLogMilliseconds = 6 * 60_000L;
        public const long StallEndMilliseconds = 11 * 60_000L;
        // A pulse gap longer than this means the stall clock was not watched; start it again.
        public const long StallObserveGapMilliseconds = 30_000;

        /// <summary>The ranged-pull intercept runs only on ticks that would walk toward the camp (its purpose).</summary>
        public static bool RunsPreArrivalPullIntercept(int campDistance, int arrivalRadius, bool dungeonApproach,
            bool certifiedDarknessFalls, bool pullerArrivedWithLeader = false) =>
            !certifiedDarknessFalls && !pullerArrivedWithLeader &&
            (campDistance > arrivalRadius || dungeonApproach) && campDistance <= InterceptRadius;

        /// <summary>A non-leader puller is at the camp when it is inside the ring, or beside a leader who is.</summary>
        public static bool PartyPullerHasArrived(int pullerCampDistance, int leaderCampDistance, int pullerToLeader,
            int arrivalRadius, int cohesionRadius) =>
            pullerCampDistance <= arrivalRadius ||
            leaderCampDistance <= arrivalRadius && pullerToLeader <= cohesionRadius;

        /// <summary>
        /// After TryBegin: the bool it returned, why, and whether this switch is on. Off keeps the old fall-through.
        /// TooFar never reaches the ordinary (melee) pull (owner 2026-10-06: a tank walking into a pack never works).
        /// </summary>
        public static PartyPullFallback AfterPartyPull(bool rangedHandled, PartyPullStart why, bool campTurn)
        {
            if (!campTurn)
                return rangedHandled ? PartyPullFallback.Handled : PartyPullFallback.OrdinaryPull;
            if (why == PartyPullStart.TooFar)
                return PartyPullFallback.SkipTooFar;
            if (why is PartyPullStart.ContactFight or PartyPullStart.NoRangedPull or
                PartyPullStart.UnreachableReleased or PartyPullStart.NotApplicable)
                return PartyPullFallback.OrdinaryPull;
            return PartyPullFallback.Handled;   // Started, Waiting, Unreachable (tries < 3)
        }

        public static bool ShouldAdvanceAfterTooFar(long tooFarMilliseconds) =>
            tooFarMilliseconds >= TooFarAdvanceAfterMilliseconds;

        /// <summary>With the switch on, a grouped bot's camp target is claimed only once a pull starts (owner 2026-11-10).</summary>
        public static bool DefersCampTargetClaim(bool grouped, bool claimOnPullStart) => grouped && claimOnPullStart;

        /// <summary>The camp turn outcomes that really start a pull of this target: claim it then.</summary>
        public static bool PullStartsOnOutcome(PartyPullStart why, PartyPullFallback next) =>
            next == PartyPullFallback.OrdinaryPull || why == PartyPullStart.Started;

        /// <summary>The empty-camp clock after a turn that only skipped a too-far target: it keeps running, or starts.</summary>
        public static long EmptyClockAfterTooFarSkip(long emptySinceBefore, long now) =>
            emptySinceBefore == 0 ? now : emptySinceBefore;

        /// <summary>
        /// Whether this too-far skip continues the episode (owner 2026-11-10): only consecutive too-far turns count
        /// toward the 45 s advance. A cleared clock, a new camp, group combat since the last skip, or a long gap
        /// starts a new episode. Pull outcomes and truly empty turns clear the clock in the controller.
        /// </summary>
        public static bool TooFarEpisodeContinues(long since, bool sameCamp, long lastSkipTick, long lastGroupCombatTick,
            long now) =>
            since != 0 && sameCamp && lastGroupCombatTick <= lastSkipTick &&
            now - lastSkipTick <= TooFarEpisodeGapMilliseconds;

        /// <summary>A turn with no target at all ends the too-far episode once no too-far skip is still pending.</summary>
        public static bool EmptyTurnEndsTooFarEpisode(long skipPendingUntil, long now) => now >= skipPendingUntil;

        /// <summary>AUTONOMOUS_PARTY_PULL_TOO_FAR: at most one line per puller per 3 minutes.</summary>
        public static bool ShouldLogTooFar(long lastLogTick, long now) =>
            lastLogTick == 0 || now - lastLogTick >= TooFarLogMilliseconds;

        /// <summary>Advances still allowed at this camp: fewer than 2 in the last 10 minutes. Prunes old ticks.</summary>
        public static bool CanAdvanceCamp(List<long> advanceTicks, long now)
        {
            if (advanceTicks == null) return true;
            advanceTicks.RemoveAll(tick => now - tick >= AdvanceWindowMilliseconds || tick > now);
            return advanceTicks.Count < MaximumAdvancesPerWindow;
        }

        public static PullStallAction PullStallStep(long sinceProgressMilliseconds) =>
            sinceProgressMilliseconds >= StallEndMilliseconds ? PullStallAction.End
            : sinceProgressMilliseconds >= StallLogMilliseconds ? PullStallAction.Log
            : PullStallAction.None;

        /// <summary>
        /// The stall clock (0 = stopped). It runs while the party grinds, and keeps running through a camp move
        /// (advance or empty-camp reposition: "Traveling" with the same camp), which is not progress. Combat,
        /// experience, a new camp or an unwatched gap start it again; any other phase stops it.
        /// </summary>
        public static long NextStallClock(long since, string phase, bool sameCamp, bool progress, long gapMilliseconds, long now)
        {
            bool grinding = phase == "Grinding";
            bool movingSameCamp = phase == "Traveling" && since > 0 && sameCamp;
            if (!grinding && !movingSameCamp) return 0;
            if (since == 0 || !sameCamp || progress || gapMilliseconds > StallObserveGapMilliseconds) return now;
            return since;
        }

        /// <summary>The pull gate's resource check for one member, named for the log; null when ready.</summary>
        public static string ResourceGate(string name, int health, int power, int endurance, bool usesPower, bool chainPull)
        {
            bool ready = chainPull
                ? AutonomousBotGroupCoordinator.ReadyForChainPull(health, power, endurance, usesPower)
                : AutonomousBotGroupCoordinator.ReadyForGroupPull(health, power, endurance, usesPower);
            return ready ? null : $"resources:{name} {health}/{(usesPower ? power.ToString() : "-")}/{endurance}";
        }

        /// <summary>The pull gate's placement check for one member, named for the log; null when it passes.</summary>
        public static string PlacementGate(string name, bool riding, bool sameRegion, int distanceToLeader, int cohesionRadius) =>
            riding ? $"riding:{name}"
            : !sameRegion ? $"region:{name}"
            : distanceToLeader > cohesionRadius ? $"cohesion:{name} {distanceToLeader}"
            : null;
    }
}
