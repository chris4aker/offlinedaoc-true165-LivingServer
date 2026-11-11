using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>The selected fallback and its live pull filter must agree.</summary>
    public static class AutonomousDeathRecoveryPolicy
    {
        public const int FailureMemoryMilliseconds = 15 * 60_000;

        public static int NextNoExperienceDeathStreak(int previousStreak, long previousDeathTick,
            long currentTick, bool earnedExperience)
        {
            if (earnedExperience || previousDeathTick <= 0 || currentTick < previousDeathTick ||
                currentTick - previousDeathTick > FailureMemoryMilliseconds)
                return 1;
            return Math.Min(10, Math.Max(1, previousStreak) + 1);
        }

        // The first two defeats still retry promptly after recovering resources.
        // Subsequent zero-XP deaths pause briefly instead of feeding a tight
        // release -> route -> death loop indefinitely.
        public static int RetryDelayMilliseconds(int noExperienceDeathStreak) =>
            noExperienceDeathStreak < 3 ? 0 : Math.Min(120_000, (noExperienceDeathStreak - 2) * 30_000);

        // owner 2026-11-10: "Recovering after release" had no time limit; 36 bots held it for hours at 0% endurance while the
        // 15-minute stuck watchdog teleported them to the same capital spot. Sitting from a release to full takes about two
        // minutes; the hold ends five minutes past the expected recovery (or past its retry pause, when that is longer).
        public const long ExpectedReleaseRecoveryMilliseconds = 3 * 60_000;
        public const long ReleaseRecoveryStallGraceMilliseconds = 5 * 60_000;

        public static bool ReleaseRecoveryStalled(long holdStartedTick, long pauseUntilTick, long nowTick) =>
            holdStartedTick > 0 &&
            nowTick >= Math.Max(holdStartedTick + ExpectedReleaseRecoveryMilliseconds, pauseUntilTick) +
                ReleaseRecoveryStallGraceMilliseconds;

        /// <summary>What still holds a released bot ("endurance", "health,power", "sickness", "pause"), for the stall log.</summary>
        public static string ReleaseRecoveryBlocker(byte healthPercent, byte manaPercent, byte endurancePercent,
            bool usesPower, bool sick, bool pausing)
        {
            List<string> blocking = [];
            if (healthPercent < 100) blocking.Add("health");
            if (usesPower && manaPercent < 100) blocking.Add("power");
            if (endurancePercent < 100) blocking.Add("endurance");
            if (sick) blocking.Add("sickness");
            if (pausing) blocking.Add("pause");
            return blocking.Count == 0 ? "none" : string.Join(",", blocking);
        }

        /// <summary>
        /// The death fallback over widening sets of camps (nearest ring first): the first set whose safest camp is blue or
        /// easier wins; with none anywhere, the nearest set's safest camp (it may be yellow) as before.
        /// </summary>
        public static AutonomousBotDecisionEngine.Camp SafestWidening(IEnumerable<AutonomousBotDecisionEngine.Camp[]> widening,
            Func<AutonomousBotDecisionEngine.Camp[], AutonomousBotDecisionEngine.Camp> safestIn)
        {
            AutonomousBotDecisionEngine.Camp nearest = null;
            foreach (AutonomousBotDecisionEngine.Camp[] set in widening)
            {
                if (set == null || set.Length == 0)
                    continue;
                AutonomousBotDecisionEngine.Camp camp = safestIn(set);
                if (camp == null)
                    continue;
                if (camp.TypicalCon <= ConColor.BLUE)
                    return camp;
                nearest ??= camp;
            }
            return nearest;
        }

        public static AutonomousBotDecisionEngine.Camp[] PreferFreshTargets(
            IEnumerable<AutonomousBotDecisionEngine.Camp> candidates,
            IReadOnlyDictionary<string, long> failedTargetUntil,
            long currentTick)
        {
            AutonomousBotDecisionEngine.Camp[] available = candidates?.ToArray() ?? [];
            if (available.Length == 0 || failedTargetUntil == null || failedTargetUntil.Count == 0)
                return available;
            AutonomousBotDecisionEngine.Camp[] fresh = available.Where(camp =>
                string.IsNullOrWhiteSpace(camp.MonsterName) ||
                !failedTargetUntil.TryGetValue(camp.MonsterName, out long until) || until <= currentTick).ToArray();
            return fresh.Length > 0 ? fresh : available;
        }

        public static ConColor EncounterMaximum(ConColor requested, ConColor natural,
            ConColor? selectedFallback, bool dynamicGroup)
        {
            if (dynamicGroup || !selectedFallback.HasValue || selectedFallback.Value <= ConColor.GREY)
                return requested;
            return (ConColor)System.Math.Clamp((int)selectedFallback.Value, (int)ConColor.GREEN, (int)natural);
        }

        public static bool IsEligible(ConColor target, ConColor maximum) =>
            target > ConColor.GREY && target <= maximum;

        /// <summary>The after-death difficulty steps back up once the bot has gained a level or gone long enough without a death.</summary>
        public static bool RelaxesDifficulty(int level, int levelAtChange, long millisecondsSinceChange, long relaxAfterMilliseconds) =>
            level > levelAtChange || millisecondsSinceChange >= relaxAfterMilliseconds;
    }
}
