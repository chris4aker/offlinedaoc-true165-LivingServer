namespace DOL.GS;

/// <summary>
/// True 1.65 Mode (owner 2026-11-06): an RvR bot whose route an enemy milegate shuts breaks the gate, as players did.
/// No search of its own: the bot's pathfinder recorded the gate when the route met it (Pathfinder.BlockingGate) and
/// walked the bot up to it. The bot fights it through the normal combat pipeline (TryEngageFrontierThreat), enemies
/// in sight first; with the gate broken (open) the route replots through it.
/// </summary>
public static class AutonomousRouteGates
{
    /// <summary>As keep doors battered by hand (AutonomousWorldBotController.DoorBatterReach): melee walks in, casters
    /// and archers hit it from their approach.</summary>
    public const int BatterReach = 1_200;
    /// <summary>A gate the route met longer ago than this no longer counts (the bot's orders changed since).</summary>
    public const long FreshMilliseconds = 30_000;

    public static bool ShouldBatter(eRealm bot, eRealm gate, bool gateAlive, eDoorState gateState, float distance,
        long blockedAt, long now) =>
        blockedAt > 0 && now - blockedAt <= FreshMilliseconds && distance <= BatterReach &&
        GameRealmGate.MayBreak(bot, gate, gateAlive, gateState);

    /// <summary>The enemy milegate shutting this bot's route, while it is near and still stands; else null.</summary>
    public static GameRealmGate Find(GameBot bot)
    {
        if (bot?.movementComponent is not NpcMovementComponent movement || movement.RouteBlockingGate is not { } gate ||
            gate.CurrentRegionID != bot.CurrentRegionID)
            return null;
        return ShouldBatter(bot.Realm, gate.Realm, gate.IsAlive, gate.State, bot.GetDistanceTo(gate),
            movement.RouteBlockingGateTick, GameLoop.GameLoopTime) &&
            GameServer.ServerRules.IsAllowedToAttack(bot, gate, true)
            ? gate : null;
    }
}
