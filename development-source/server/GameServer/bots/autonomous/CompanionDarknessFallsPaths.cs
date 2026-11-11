using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// GitHub #38: Darkness Falls' certified-corridor gate is for unled autonomous world bots only. A player's companion
/// (a True 1.65 hired companion, a bot in a player-led group, a /spawn helper) follows its player on the ordinary
/// Detour path there, as it does everywhere else; the gate turned every uncertified path into NoPathFound, so a hired
/// companion stood still until the stuck check warped it beside its player (COMPANION_STUCK_165). The certified
/// one-way entrance falls stay available to companions so they can follow the player down those drops.
/// </summary>
public static class CompanionDarknessFallsPaths
{
    private static readonly Logger log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    /// <summary>One path line and one bypassed-rejection line per companion per this interval.</summary>
    public const long LogIntervalMilliseconds = 30_000;

    private static readonly ConcurrentDictionary<long, long> NextPathLog = new();
    private static readonly ConcurrentDictionary<long, long> NextBypassLog = new();

    /// <summary>The strict DF route gate applies to an autonomous world bot that no player leads.</summary>
    public static bool UsesCertifiedRouteGate(bool autonomousWorldBot, bool playerLed) => autonomousWorldBot && !playerLed;

    /// <summary>The certified DF fall step (walk off the lip, descend to the landing): autonomous bots, as before, and
    /// every player-led companion.</summary>
    public static bool MayUseCertifiedFall(bool autonomousWorldBot, bool playerLed) => autonomousWorldBot || playerLed;

    /// <summary>A hired companion, a bot in a player-led group, or a bot with a player owner (a /spawn helper).</summary>
    public static bool IsPlayerLed(GameBot bot) =>
        bot != null && (HiredCompanions165.IsHired(bot) || bot.IsPlayerLedGroup || bot.Owner != null);

    public static bool UsesCertifiedRouteGate(GameBot bot) =>
        bot != null && UsesCertifiedRouteGate(bot.IsAutonomousWorldBot, IsPlayerLed(bot));

    public static bool MayUseCertifiedFall(GameBot bot) =>
        bot != null && MayUseCertifiedFall(bot.IsAutonomousWorldBot, IsPlayerLed(bot));

    /// <summary>True at most once per <paramref name="interval"/> for one key (a bot's own path work runs on its own
    /// tick, so one key is never claimed from two threads at once; a rare duplicate line is harmless).</summary>
    public static bool TryClaimLogSlot(ConcurrentDictionary<long, long> next, long key, long now, long interval)
    {
        if (next.TryGetValue(key, out long due) && now < due)
            return false;
        next[key] = now + interval;
        return true;
    }

    /// <summary>Logs a companion's ordinary DF path (rate-limited), and a separate line when the old certified gate
    /// would have rejected it (the companion then stood still, then was warped).</summary>
    public static void NotePath(GameBot bot, PathfindingStatus status, int nodeCount, bool oldGateWouldReject,
        Vector3 from, Vector3 to)
    {
        if (bot == null || !log.IsInfoEnabled)
            return;
        long key = bot.DatabaseID > 0 ? bot.DatabaseID : -bot.ObjectID;
        long now = GameLoop.GameLoopTime;
        string where = $"bot=\"{bot.Name}\" id={bot.DatabaseID} region={bot.CurrentRegionID} status={status} nodes={nodeCount} " +
                       $"from={(int) from.X},{(int) from.Y},{(int) from.Z} to={(int) to.X},{(int) to.Y},{(int) to.Z} " +
                       $"hired={HiredCompanions165.IsHired(bot)} led={bot.IsPlayerLedGroup}";
        if (TryClaimLogSlot(NextPathLog, key, now, LogIntervalMilliseconds))
            log.Info($"COMPANION_PATH_DF {where} old_gate={(oldGateWouldReject ? "reject" : "pass")}");
        if (oldGateWouldReject && TryClaimLogSlot(NextBypassLog, key, now, LogIntervalMilliseconds))
            log.Info($"COMPANION_PATH_DF_UNGATED {where} note=\"the certified DF gate would have returned NoPathFound\"");
    }
}
