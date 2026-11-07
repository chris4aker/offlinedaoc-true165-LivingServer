using System.Runtime.CompilerServices;
using DOL.AI.Brain;

namespace DOL.GS
{
    public static class CompanionEngagementMode
    {
        public const int DefensiveRadius = 350;
        private sealed class Mode { public bool Defensive; public GameLiving Pull; }
        private static readonly ConditionalWeakTable<GamePlayer, Mode> Modes = new();
        public static void Set(GamePlayer player, bool defensive)
        {
            Mode mode = Modes.GetOrCreateValue(player);
            mode.Defensive = defensive;
            mode.Pull = null;
            CompanionPvpEngagement.Reset(player);
            // The new client's group-window stance button shows it (op 30 of the DebugMode 'H' packet: 1 defensive,
            // 0 aggressive; owner 2026-10-30). The original client keeps /aggressive and /defensive.
            Hire165.Send(player, 30, [(byte)(defensive ? 1 : 0)]);
        }
        public static GamePlayer DefensiveLeader(GameLiving actor)
        {
            // Include companion pets, never human pets or autonomous bots.
            for (int depth = 0; depth < 8 && actor != null; depth++)
            {
                if (actor is GameBot bot)
                {
                    GamePlayer leader = bot.PlayerGroupLeader ?? bot.Owner;
                    return bot.IsPlayerCompanion &&
                        leader != null && bot.Group != null && bot.Group == leader.Group &&
                        Modes.TryGetValue(leader, out Mode mode) && mode.Defensive ? leader : null;
                }
                actor = (actor as GameNPC)?.Brain is IControlledBrain controlled ? controlled.Owner : null;
            }
            return null;
        }
        public static bool Allows(GameLiving actor, GameLiving target)
        {
            GamePlayer leader = DefensiveLeader(actor);
            // The /pull puller may reach past the leash for the monster it was sent for (owner 2026-11-06; aggressive
            // mode has no leash and is unchanged).
            return leader == null || PlayerLedPullCoordinator.IsOrderedPuller(actor, target) ||
                target != null && target.CurrentRegionID == leader.CurrentRegionID &&
                (leader.IsWithinRadius(target, DefensiveRadius) || CompanionPvpEngagement.Defending(actor, target) ||
                 CompanionPvpEngagement.FrontierRoamerInRange(leader, target, true));
        }
        public static void RememberPull(GamePlayer player, GameLiving target)
        {
            if (player != null && Modes.TryGetValue(player, out Mode mode) && mode.Defensive) mode.Pull = target;
        }
        public static GameLiving NearbyPull(GameBot bot)
        {
            GamePlayer player = DefensiveLeader(bot);
            if (player == null || !Modes.TryGetValue(player, out Mode mode)) return null;
            GameLiving target = mode.Pull;
            if (target?.IsAlive != true || target.ObjectState != GameObject.eObjectState.Active ||
                target.CurrentRegionID != player.CurrentRegionID)
            {
                mode.Pull = null;
                return null;
            }
            return Allows(bot, target) ? target : null;
        }
    }
}
