using System;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// How fast the population scheduler makes NEW level-1 bots while a realm fills (owner 2026-11-10, "level1").
    /// Logs 11/10: goals went 500 to 600 at the 12:19 restart and 262 new level-1 bots were made in 3 minutes (the usual
    /// rate is 22-33 an hour); kills per death at level 1 halved for everyone in that hour. Every restart is a fill phase
    /// (nobody is online at start), not only goal raises.
    /// While filling, new level 1s are capped at <see cref="PerMinute"/> a realm a minute, except:
    /// - while the realm is under half its goal (a mostly empty or fresh world fills at the login rate as before);
    /// - the new bots that only bring the 1-9 bracket up to its own share of the goal.
    /// Saved-bot logins, crash recovery and the 2-a-second login cap are unchanged. Pure rules; the scheduler holds state.
    /// </summary>
    public static class AutonomousPopulationFillPace
    {
        [ServerProperty("bots", "bot_level1_fill_pace",
            "While a realm fills (every restart, or a goal raise), are new level-1 bots made at most 4 a realm a minute once the realm is half full and its level 1-9 share is met? Off: new level-1 bots are made at the login rate, as before.", true)]
        public static bool Enabled = true;

        public const int PerMinute = 4;
        /// <summary>No limit (the scheduler's own "Big").</summary>
        public const int Unlimited = 1_000_000_000;

        /// <summary>Token bucket for paced new bots: <see cref="PerMinute"/> a minute, at most <see cref="PerMinute"/> saved.</summary>
        public static double NextTokens(double tokens, double seconds) =>
            Math.Min(PerMinute, Math.Max(0, tokens) + PerMinute * Math.Max(0, seconds) / 60.0);

        /// <summary>New level 1s allowed without a token: all while the realm (live + coming) is under half its goal,
        /// otherwise only those that fill the 1-9 bracket up to its own target.</summary>
        public static int FreeNew(int liveAndInbound, int goal, int bracket0Have, int bracket0Target) =>
            (long)liveAndInbound * 2 < goal ? Unlimited : Math.Max(0, bracket0Target - bracket0Have);

        /// <summary>How many of <paramref name="want"/> new bots may be made now: the free ones plus whole tokens.</summary>
        public static int Allowed(int want, int free, double tokens)
        {
            if (want <= 0)
                return 0;
            if (free >= want)
                return want;
            return Math.Max(0, free) + Math.Min(want - Math.Max(0, free), (int)Math.Floor(Math.Max(0, tokens)));
        }

        /// <summary>The fill-phase new-bot budget session ends and swaps may count on as replacements (Unlimited when not
        /// paced). Only the free part: the paced tokens are Fill's alone (owner 2026-11-10, round-3 review: counting them
        /// here too let sessions end as fast as Fill could replace them, so a realm could stay flat below its goal).</summary>
        public static int ReplacementBudget(bool enabled, int liveAndInbound, int goal, int bracket0Have, int bracket0Target) =>
            enabled ? FreeNew(liveAndInbound, goal, bracket0Have, bracket0Target) : Unlimited;
    }
}
