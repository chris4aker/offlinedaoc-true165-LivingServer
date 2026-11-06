using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

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

        private sealed class Record
        {
            public readonly object Gate = new();
            public readonly Dictionary<long, DateTime> FailedBots = new();
            public DateTime QuarantinedUntilUtc;
        }

        private static readonly ConcurrentDictionary<string, Record> Records = new(StringComparer.OrdinalIgnoreCase);
        // bots that gave a camp up after pulls in which they never moved, kept apart from route failures
        private static readonly ConcurrentDictionary<string, Record> Stalls = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Returns true when this report put the camp on the bench.</summary>
        public static bool ReportFailure(string campId, long botId, DateTime? nowUtc = null) =>
            Report(Records, campId, botId, nowUtc);

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
                record.QuarantinedUntilUtc = (nowUtc ?? DateTime.UtcNow) + QuarantineDuration;
            }
            return true;
        }

        /// <summary>
        /// A pull stall that says something about the camp, not the bot: a melee puller whose target stayed beyond its
        /// reach while following it moved the bot nowhere.
        /// </summary>
        public static bool StallBlamesCamp(bool meleePuller, int targetDistance, int meleeRange) =>
            meleePuller && targetDistance > meleeRange;

        private static bool Report(ConcurrentDictionary<string, Record> records, string campId, long botId, DateTime? nowUtc)
        {
            if (string.IsNullOrWhiteSpace(campId) || botId <= 0)
                return false;
            DateTime now = nowUtc ?? DateTime.UtcNow;
            Record record = records.GetOrAdd(campId, _ => new Record());
            lock (record.Gate)
            {
                if (record.QuarantinedUntilUtc > now)
                    return false;
                record.FailedBots[botId] = now;
                List<long> stale = null;
                foreach (KeyValuePair<long, DateTime> pair in record.FailedBots)
                    if (now - pair.Value > FailureWindow)
                        (stale ??= new()).Add(pair.Key);
                if (stale != null)
                    foreach (long id in stale)
                        record.FailedBots.Remove(id);
                if (record.FailedBots.Count < DistinctBotsToQuarantine)
                    return false;
                record.FailedBots.Clear();
                record.QuarantinedUntilUtc = now + QuarantineDuration;
                return true;
            }
        }

        public static bool IsQuarantined(string campId, DateTime? nowUtc = null) =>
            !string.IsNullOrWhiteSpace(campId) && Records.TryGetValue(campId, out Record record) &&
            record.QuarantinedUntilUtc > (nowUtc ?? DateTime.UtcNow);

        public static void ResetForTests()
        {
            Records.Clear();
            Stalls.Clear();
        }
    }
}
