using System.Collections.Concurrent;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Owner 2026-11-10 (P3 watch): one line when a gamebot dies within <see cref="WatchMilliseconds"/> of a horse landing,
    /// so the 5-minute death rate per landing can be measured (East Lough Derg, and the Mag Mell / Tir na mBeo landings
    /// solo bots take instead). Logging only; nothing is decided here.
    /// </summary>
    public static class AutonomousStableLandingWatch
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousStableLandingWatch));
        public const long WatchMilliseconds = 5 * 60_000;
        private const int MaxEntries = 10_000;

        private readonly record struct Landing(string Destination, ushort Region, int X, int Y, long Tick);

        private static readonly ConcurrentDictionary<long, Landing> Landings = new();

        /// <summary>Pure rule: a death this soon after the landing counts.</summary>
        public static bool CountsAsLandingDeath(long landedTick, long now) =>
            landedTick > 0 && now >= landedTick && now - landedTick <= WatchMilliseconds;

        public static void Arrived(GameBot bot, string destination)
        {
            if (bot == null || bot.DatabaseID <= 0)
                return;
            long now = GameLoop.GameLoopTime;
            if (Landings.Count >= MaxEntries)
                foreach (var entry in Landings)
                    if (!CountsAsLandingDeath(entry.Value.Tick, now))
                        Landings.TryRemove(entry.Key, out _);
            Landings[bot.DatabaseID] = new Landing(destination ?? string.Empty, bot.CurrentRegionID, bot.X, bot.Y, now);
        }

        public static void Died(GameBot bot, GameObject killer)
        {
            if (bot == null || bot.DatabaseID <= 0 || !Landings.TryRemove(bot.DatabaseID, out Landing landing))
                return;
            long now = GameLoop.GameLoopTime;
            if (!CountsAsLandingDeath(landing.Tick, now))
                return;
            Log.Info($"AUTONOMOUS_STABLE_LANDING_DEATH bot=\"{bot.Name}\" id={bot.DatabaseID} level={bot.Level} " +
                     $"solo={bot.Group == null} destination=\"{landing.Destination}\" seconds={(now - landing.Tick) / 1000} " +
                     $"killer=\"{killer?.Name}\" killer_level={(killer as GameLiving)?.Level ?? 0} " +
                     $"landing={landing.Region}:{landing.X},{landing.Y} at={bot.CurrentRegionID}:{bot.X},{bot.Y}");
        }
    }
}
