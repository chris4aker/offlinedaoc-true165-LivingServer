using System.Collections.Generic;
using System.Threading;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Level-1 safety for solo bots (owner 2026-11-10, "level1"). Logs 11/08-11/10: 3,831 deaths by 577 level-1 bots;
    /// a level-1 bot against a YELLOW (level-1) monster won about 1 fight in 30, and the solo pull sort even put yellow
    /// first (|YELLOW| = 0). A level-1 solo bot now hunts BLUE only: camps, the death fallback and the pull filter all use
    /// a BLUE ceiling. When no blue camp is anywhere it can go but a yellow-inclusive search would have found one, it gets
    /// today's YELLOW ceiling back for 10 minutes (the yellow fallback). Foresters were already BLUE; level 2+ unchanged.
    /// </summary>
    public static class AutonomousLowLevelTargetPolicy
    {
        [ServerProperty("bots", "bot_level1_blue_ceiling",
            "Do level-1 solo bots hunt only blue monsters (camps and pulls), with 10 minutes of yellow only when no blue camp can be found but a yellow one can? Off: level-1 solo bots hunt up to yellow, as before.", true)]
        public static bool Level1BlueCeiling = true;

        public const int BlueOnlyMaxLevel = 1;
        public const long YellowFallbackMilliseconds = 10 * 60_000;
        public const long SummaryMilliseconds = 10 * 60_000;

        /// <summary>The hardest con a solo bot hunts (the switch read from <see cref="Level1BlueCeiling"/>).</summary>
        public static ConColor SoloCeiling(int classId, int level, bool yellowFallbackActive) =>
            SoloCeiling(classId, level, yellowFallbackActive, Level1BlueCeiling);

        /// <summary>Pure rule: Forester BLUE always; level 1 BLUE unless its yellow fallback runs; else YELLOW.</summary>
        public static ConColor SoloCeiling(int classId, int level, bool yellowFallbackActive, bool blueOnlyEnabled) =>
            classId == (int)eCharacterClass.Forester ? ConColor.BLUE
            : blueOnlyEnabled && level <= BlueOnlyMaxLevel && !yellowFallbackActive ? ConColor.BLUE
            : ConColor.YELLOW;

        /// <summary>True when the blue-only ceiling holds this bot (so a yellow fallback could change anything).</summary>
        public static bool BlueOnlyApplies(int classId, int level, bool solo, bool yellowFallbackActive, bool blueOnlyEnabled) =>
            blueOnlyEnabled && solo && classId != (int)eCharacterClass.Forester && level <= BlueOnlyMaxLevel && !yellowFallbackActive;

        /// <summary>Start the yellow fallback: a solo level-1 bot (not a Forester) found no camp, its fallback is not
        /// already running, and a camp would have qualified with a yellow ceiling (critic: never for unrelated no-camp
        /// causes such as the catalog not being ready).</summary>
        public static bool StartsYellowFallback(int classId, int level, bool solo, bool campChosen, bool fallbackActive,
            bool yellowCampExists, bool blueOnlyEnabled) =>
            BlueOnlyApplies(classId, level, solo, fallbackActive, blueOnlyEnabled) && !campChosen && yellowCampExists;

        /// <summary>Would a camp with these spawn levels qualify for a solo bot under <paramref name="ceiling"/>?
        /// Same level and camp rules as SelectCamp (green or harder, at most the ceiling, solo floor, half the spawns).</summary>
        public static bool QualifiesUpTo(int botEffectiveLevel, int botLevel, IReadOnlyList<int> levels, ConColor ceiling)
        {
            if (levels == null || levels.Count == 0)
                return false;
            int valid = 0;
            foreach (int level in levels)
            {
                ConColor con = ConLevels.GetConColor(ConLevels.GetConLevel(botEffectiveLevel, level));
                if (con >= ConColor.GREEN && con <= ceiling && AutonomousSoloCampFloor.Allows(botLevel, level))
                    valid++;
            }
            return valid > 0 && AutonomousSoloCampFloor.CampQualifies(botLevel, valid, levels.Count);
        }

        /// <summary>owner 2026-11-10: would SelectCamp really choose this yellow camp for a level-1 solo bot under the
        /// yellow fallback? Same gates as the camp loop and the legal filter: live spawns, no Darkness Falls (solo level
        /// 25+ and blue only), no dungeon while it waits for matchmaking (outside the death fallback), not dangerous by the
        /// threat policy, and qualifying with a yellow ceiling. AvoidNoGo, AvoidEmptySpots and the environment roll never
        /// empty a non-empty list, so they cannot stop a camp from being chosen.</summary>
        public static bool YellowCampWouldBeChosen(int botEffectiveLevel, int botLevel, IReadOnlyList<int> levels,
            int liveMobCount, bool isDungeon, bool dungeonsAllowed, bool darknessFalls, bool dangerous) =>
            liveMobCount > 0 && !darknessFalls && (dungeonsAllowed || !isDungeon) && !dangerous &&
            QualifiesUpTo(botEffectiveLevel, botLevel, levels, ConColor.YELLOW);

        /// <summary>owner 2026-11-10: a live monster the pull filter skipped only because of the level-1 blue ceiling
        /// (it would pass the normal solo ceiling). Such a camp is not empty for other bots.</summary>
        public static bool HiddenByLevel1Ceiling(ConColor con, ConColor ceiling, ConColor normalSoloCeiling) =>
            con > ceiling && con <= normalSoloCeiling;

        /// <summary>owner 2026-11-10: the empty wait at <paramref name="campId"/> saw live monsters hidden only by the
        /// level-1 ceiling, so leaving it must not teach the shared empty-camp memory.</summary>
        public static bool HidLiveTargets(string hiddenCampId, string campId, long hiddenSeenTick, long emptySinceTick) =>
            emptySinceTick > 0 && hiddenSeenTick >= emptySinceTick && !string.IsNullOrEmpty(campId) &&
            string.Equals(hiddenCampId, campId, System.StringComparison.Ordinal);

        /// <summary>owner 2026-11-10: GOAL_ATTEMPT.choice. Level-1 death-fallback camps carry "+death_fallback" so they can
        /// be told apart from normal blue picks; every level-1 label still starts with "level1".</summary>
        public static string ChoiceLabel(bool widened, bool level1Blue, bool level1YellowFallback, bool deathFallback)
        {
            if (widened)
                return "crowd_widened";
            string level1 = level1Blue ? "level1_blue" : level1YellowFallback ? "level1_yellow_fallback" : null;
            if (level1 == null)
                return deathFallback ? "death_fallback" : string.Empty;
            return deathFallback ? level1 + "+death_fallback" : level1;
        }

        // ---- abort check (critic, owner 2026-11-10): one Warn per summary window when a line is crossed ----
        /// <summary>Fewest level-1 attempts in a realm window before any abort line is judged.</summary>
        public const int AbortMinimumAttempts = 20;
        /// <summary>A level-1 goal farther than this (or in another region) counts as far travel.</summary>
        public const int AbortFarTravelDistance = 40_000;
        /// <summary>Abort line: more than this share of level-1 attempts sent far (baseline 10-31: median 72,600 units).</summary>
        public const double AbortFarTravelShare = 0.25;
        /// <summary>Abort line: more than this share of level-1 attempts ended by a death before arrival.</summary>
        public const double AbortPreArrivalDeathShare = 0.20;
        /// <summary>Abort line: more than this share of Hibernia region 200 level-1 attempts ended EmptyCamp.</summary>
        public const double AbortHibernia200EmptyShare = 0.35;
        public const int Hibernia200Region = 200;

        // ---- counters for AUTONOMOUS_LEVEL1_CAMP_SUMMARY (index = realm 1-3) ----
        private static readonly int[] BluePlans = new int[4], NoCamp = new int[4], YellowFallbacks = new int[4],
            HiddenEmpties = new int[4], Attempts = new int[4], FarAttempts = new int[4], PreArrivalDeaths = new int[4];
        private static int _hib200Attempts, _hib200Empty;
        private static long _nextSummaryTick;

        /// <summary>A level-1 bot left a camp whose live monsters were only above its blue ceiling (not recorded as empty).</summary>
        public static void CountHiddenEmpty(eRealm realm) => Interlocked.Increment(ref HiddenEmpties[Slot(realm)]);

        /// <summary>A finished goal attempt (AutonomousGoalDiagnostics); counted only for level-1 bots on a level-1 choice.</summary>
        public static void NoteAttemptEnd(int realm, int level, string choice, int goalRegion, int startGoalDistance,
            bool arrived, GoalAttemptEnd reason)
        {
            if (level > BlueOnlyMaxLevel || choice == null || !choice.StartsWith("level1", System.StringComparison.Ordinal))
                return;
            int slot = Slot((eRealm)realm);
            Interlocked.Increment(ref Attempts[slot]);
            if (startGoalDistance < 0 || startGoalDistance > AbortFarTravelDistance)
                Interlocked.Increment(ref FarAttempts[slot]);
            if (reason == GoalAttemptEnd.Defeated && !arrived)
                Interlocked.Increment(ref PreArrivalDeaths[slot]);
            if (slot == (int)eRealm.Hibernia && goalRegion == Hibernia200Region)
            {
                Interlocked.Increment(ref _hib200Attempts);
                if (reason == GoalAttemptEnd.EmptyCamp)
                    Interlocked.Increment(ref _hib200Empty);
            }
        }

        /// <summary>The abort lines crossed by one realm window, or null (pure; for the summary and tests).</summary>
        public static string AbortCheck(string realm, int attempts, int far, int preArrivalDeaths)
        {
            if (attempts < AbortMinimumAttempts)
                return null;
            var crossed = new List<string>(2);
            if (far > attempts * AbortFarTravelShare)
                crossed.Add($"{realm} far_travel={far}/{attempts}");
            if (preArrivalDeaths > attempts * AbortPreArrivalDeathShare)
                crossed.Add($"{realm} pre_arrival_deaths={preArrivalDeaths}/{attempts}");
            return crossed.Count == 0 ? null : string.Join(" ", crossed);
        }

        /// <summary>The Hibernia region 200 EmptyCamp abort line, or null.</summary>
        public static string AbortCheckHibernia200(int attempts, int empty) =>
            attempts >= AbortMinimumAttempts && empty > attempts * AbortHibernia200EmptyShare
                ? $"hib region200_empty_camp={empty}/{attempts}" : null;

        private static int Slot(eRealm realm) => (int)realm is >= 1 and <= 3 ? (int)realm : 0;

        /// <summary>One blue-only camp plan: whether it found a camp, and whether it started the yellow fallback.</summary>
        public static void CountPlan(eRealm realm, bool campChosen, bool startedFallback)
        {
            int slot = Slot(realm);
            Interlocked.Increment(ref BluePlans[slot]);
            if (!campChosen)
                Interlocked.Increment(ref NoCamp[slot]);
            if (startedFallback)
                Interlocked.Increment(ref YellowFallbacks[slot]);
        }

        /// <summary>True at most once per <see cref="SummaryMilliseconds"/> (the first call arms the timer).</summary>
        public static bool TryTakeSummary(long now, out string line) => TryTakeSummary(now, out line, out _);

        /// <summary>The summary, plus the abort lines crossed in the same window (null when none; logged as a Warn).</summary>
        public static bool TryTakeSummary(long now, out string line, out string abortWarning)
        {
            line = null;
            abortWarning = null;
            long next = Interlocked.Read(ref _nextSummaryTick);
            if (next == 0)
            {
                Interlocked.CompareExchange(ref _nextSummaryTick, now + SummaryMilliseconds, 0);
                return false;
            }
            if (now < next || Interlocked.CompareExchange(ref _nextSummaryTick, now + SummaryMilliseconds, next) != next)
                return false;
            var parts = new List<string>(3);
            var warnings = new List<string>(4);
            foreach ((int slot, string name) in new[] { (1, "alb"), (2, "mid"), (3, "hib") })
            {
                int attempts = Interlocked.Exchange(ref Attempts[slot], 0);
                int far = Interlocked.Exchange(ref FarAttempts[slot], 0);
                int preArrival = Interlocked.Exchange(ref PreArrivalDeaths[slot], 0);
                parts.Add($"{name}: blue_plans={Interlocked.Exchange(ref BluePlans[slot], 0)} " +
                          $"no_camp={Interlocked.Exchange(ref NoCamp[slot], 0)} " +
                          $"yellow_fallbacks={Interlocked.Exchange(ref YellowFallbacks[slot], 0)} " +
                          $"hidden_not_empty={Interlocked.Exchange(ref HiddenEmpties[slot], 0)} " +
                          $"attempts={attempts} far={far} pre_arrival_deaths={preArrival}");
                if (AbortCheck(name, attempts, far, preArrival) is string crossed)
                    warnings.Add(crossed);
            }
            if (AbortCheckHibernia200(Interlocked.Exchange(ref _hib200Attempts, 0), Interlocked.Exchange(ref _hib200Empty, 0)) is string hib200)
                warnings.Add(hib200);
            line = $"minutes={SummaryMilliseconds / 60_000} " + string.Join(" | ", parts);
            if (warnings.Count > 0)
                abortWarning = $"minutes={SummaryMilliseconds / 60_000} {string.Join(" ", warnings)} " +
                               $"(lines: far>{AbortFarTravelShare:0%} beyond {AbortFarTravelDistance}, pre-arrival deaths>{AbortPreArrivalDeathShare:0%}, " +
                               $"hib region 200 EmptyCamp>{AbortHibernia200EmptyShare:0%}; min {AbortMinimumAttempts} attempts; consider bot_level1_blue_ceiling=false)";
            return true;
        }

        /// <summary>Test hook: forget the summary timer and counters.</summary>
        public static void ResetForTests()
        {
            Interlocked.Exchange(ref _nextSummaryTick, 0);
            for (int i = 0; i < 4; i++)
                BluePlans[i] = NoCamp[i] = YellowFallbacks[i] = HiddenEmpties[i] = Attempts[i] = FarAttempts[i] = PreArrivalDeaths[i] = 0;
            _hib200Attempts = _hib200Empty = 0;
        }
    }
}
