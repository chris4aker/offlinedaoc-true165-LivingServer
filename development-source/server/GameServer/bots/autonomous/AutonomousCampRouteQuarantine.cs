using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// A camp whose route defeats several different bots is benched for a
    /// while so the planner stops sending more bots into the same blocked
    /// terrain (the overnight run had 37 bots in turn give up on one Lough Derg
    /// camp). One bot's failure never benches a camp: that bot already avoids
    /// it through its own recent-failure list. Nothing is changed in the world;
    /// the camp simply becomes selectable again once the bench expires.
    /// </summary>
    public static class AutonomousCampRouteQuarantine
    {
        public const int DistinctBotsToQuarantine = 3;
        public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan QuarantineDuration = TimeSpan.FromHours(2);
        /// <summary>Owner 2026-11-10: a camp benched again within <see cref="StrikeMemory"/> of its last bench ending stays
        /// out longer each time (mygga camps were benched 13 times in 48 h, 2 h each).</summary>
        public static readonly TimeSpan[] RepeatQuarantineDurations =
            [TimeSpan.FromHours(2), TimeSpan.FromHours(6), TimeSpan.FromHours(24)];
        public static readonly TimeSpan StrikeMemory = TimeSpan.FromHours(24);

        [ServerProperty("bots", "bot_camp_bench_groups_fixed",
            "Do group camp rejections bench a camp for the fixed 2 hours, and do groups see any camp bench as at most 2 hours? The longer repeat benches (6 h, 24 h) are then for solo bots only.", true)]
        public static bool GroupsFixedBench = true;

        private sealed class Record
        {
            public readonly object Gate = new();
            public readonly Dictionary<long, DateTime> FailedBots = new();
            public DateTime QuarantinedUntilUtc;
            public DateTime BenchStartUtc;
            public TimeSpan LastBench;
            public int Strikes;
            // owner 2026-11-10 (round 3): group reports keep their own failures and fixed bench, so they are
            // recorded while a long solo bench runs and never move the solo bench end that NextStrike reads
            public readonly Dictionary<long, DateTime> GroupFailedBots = new();
            public DateTime GroupBenchStartUtc;
            public DateTime GroupBenchUntilUtc;
        }

        /// <summary>Pure rule (owner 2026-11-10): the bench length for this camp's nth bench in a row.</summary>
        public static TimeSpan QuarantineDurationFor(int strike) =>
            RepeatQuarantineDurations[Math.Clamp(strike, 1, RepeatQuarantineDurations.Length) - 1];

        /// <summary>Pure rule (owner 2026-11-10): a new bench counts as a repeat when the last one ended at most
        /// <see cref="StrikeMemory"/> ago; otherwise the count starts over.</summary>
        public static int NextStrike(int previousStrikes, DateTime lastBenchEndUtc, DateTime nowUtc) =>
            previousStrikes > 0 && lastBenchEndUtc != default && nowUtc - lastBenchEndUtc <= StrikeMemory
                ? previousStrikes + 1 : 1;

        // the solo (escalating) bench; group reports set the separate group bench in Report (owner 2026-11-10, round 3)
        private static void Bench(Record record, DateTime now)
        {
            record.Strikes = NextStrike(record.Strikes, record.QuarantinedUntilUtc, now);
            record.LastBench = QuarantineDurationFor(record.Strikes);
            record.BenchStartUtc = now;
            record.QuarantinedUntilUtc = now + record.LastBench;
        }

        /// <summary>Pure rule (owner 2026-11-10, review): when a bench ends for this asker - a group sees at most the
        /// fixed <see cref="QuarantineDuration"/> from the bench's start; a solo bot sees the whole (escalated) bench.</summary>
        public static DateTime BenchEndFor(DateTime benchStartUtc, DateTime benchEndUtc, bool forGroup, bool groupsFixed) =>
            forGroup && groupsFixed && benchStartUtc != default && benchStartUtc + QuarantineDuration < benchEndUtc
                ? benchStartUtc + QuarantineDuration : benchEndUtc;

        /// <summary>How long this camp's current or last bench lasts (for the log lines).</summary>
        public static TimeSpan BenchDuration(string campId) =>
            !string.IsNullOrWhiteSpace(campId) && Records.TryGetValue(campId, out Record record) && record.LastBench > TimeSpan.Zero &&
            record.GroupBenchStartUtc <= record.BenchStartUtc
                ? record.LastBench : QuarantineDuration;

        /// <summary>When the bench this asker sees ends (owner 2026-11-10, round 3): the solo bench (capped for groups)
        /// or the group bench, whichever is later; both keep solo bots and groups away.</summary>
        private static DateTime VisibleBenchEnd(Record record, bool forGroup) =>
            Max(BenchEndFor(record.BenchStartUtc, record.QuarantinedUntilUtc, forGroup, GroupsFixedBench), record.GroupBenchUntilUtc);

        private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

        private static readonly ConcurrentDictionary<string, Record> Records = new(StringComparer.OrdinalIgnoreCase);
        // bots that gave a camp up after pulls in which they never moved, kept apart from route failures
        private static readonly ConcurrentDictionary<string, Record> Stalls = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Returns true when this report put the camp on the bench. A group report (groupReport) benches for
        /// the fixed time only while bot_camp_bench_groups_fixed is on (owner 2026-11-10, review).</summary>
        public static bool ReportFailure(string campId, long botId, DateTime? nowUtc = null, bool groupReport = false) =>
            Report(Records, campId, botId, nowUtc, !(groupReport && GroupsFixedBench));

        /// <summary>
        /// A bot gave the camp up after pulls in which it never moved. One such bot may only have stalled on its own side
        /// (no weapon, nothing to cast), but <see cref="DistinctBotsToQuarantine"/> different bots within
        /// <see cref="FailureWindow"/> mean the camp is the problem (2026-11-06: a navmesh pocket at a lough wolf cadger
        /// camp held up to 31 bots, 395 pull timeouts and 72 camp abandons, and was never benched). Returns true when this
        /// report put the camp on the bench.
        /// </summary>
        public static bool ReportStall(string campId, long botId, DateTime? nowUtc = null)
        {
            if (IsQuarantined(campId, nowUtc) || !Report(Stalls, campId, botId, nowUtc))
                return false;
            Record record = Records.GetOrAdd(campId, _ => new Record());
            lock (record.Gate)
            {
                record.FailedBots.Clear();
                Bench(record, nowUtc ?? DateTime.UtcNow);
            }
            return true;
        }

        /// <summary>
        /// A pull stall that says something about the camp, not the bot: a melee puller whose target stayed beyond its
        /// reach while following it moved the bot nowhere.
        /// </summary>
        public static bool StallBlamesCamp(bool meleePuller, int targetDistance, int meleeRange) =>
            meleePuller && targetDistance > meleeRange;

        private static bool Report(ConcurrentDictionary<string, Record> records, string campId, long botId, DateTime? nowUtc,
            bool escalate = true)
        {
            if (string.IsNullOrWhiteSpace(campId) || botId <= 0)
                return false;
            DateTime now = nowUtc ?? DateTime.UtcNow;
            Record record = records.GetOrAdd(campId, _ => new Record());
            lock (record.Gate)
            {
                // owner 2026-11-10 (round 3): a group report waits only for the bench groups see, not the long solo one
                if (VisibleBenchEnd(record, forGroup: !escalate) > now)
                    return false;
                Dictionary<long, DateTime> failed = escalate ? record.FailedBots : record.GroupFailedBots;
                failed[botId] = now;
                List<long> stale = null;
                foreach (KeyValuePair<long, DateTime> pair in failed)
                    if (now - pair.Value > FailureWindow)
                        (stale ??= new()).Add(pair.Key);
                if (stale != null)
                    foreach (long id in stale)
                        failed.Remove(id);
                if (failed.Count < DistinctBotsToQuarantine)
                    return false;
                failed.Clear();
                if (escalate)
                    Bench(record, now);
                else
                {
                    record.GroupBenchStartUtc = now;
                    record.GroupBenchUntilUtc = now + QuarantineDuration;
                }
                return true;
            }
        }

        public static bool IsQuarantined(string campId, DateTime? nowUtc = null, bool forGroup = false) =>
            !string.IsNullOrWhiteSpace(campId) && Records.TryGetValue(campId, out Record record) &&
            VisibleBenchEnd(record, forGroup) > (nowUtc ?? DateTime.UtcNow);

        public static void ResetForTests()
        {
            Records.Clear();
            Stalls.Clear();
        }
    }
}
