using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>The companions' stance (op 30 byte [4] of the DebugMode 'H' packet sends this number).</summary>
    public enum CompanionStance : byte { Aggressive = 0, Defensive = 1, Passive = 2 }

    public static class CompanionEngagementMode
    {
        public const int DefensiveRadius = 350;
        private sealed class Mode { public CompanionStance Stance; public GameLiving Pull; }
        private static readonly ConditionalWeakTable<GamePlayer, Mode> Modes = new();
        private static readonly Logger Log = LoggerManager.Create(typeof(CompanionEngagementMode));

        /// <summary>/defensive (true) or /aggressive (false); kept for the existing callers.</summary>
        public static void Set(GamePlayer player, bool defensive) =>
            Set(player, defensive ? CompanionStance.Defensive : CompanionStance.Aggressive);

        public static void Set(GamePlayer player, CompanionStance stance)
        {
            if (player == null) return;
            Mode mode = Modes.GetOrCreateValue(player);
            CompanionStance previous = mode.Stance;
            mode.Stance = stance;
            mode.Pull = null;
            CompanionPvpEngagement.Reset(player);
            // The new client's group-window stance button shows it (op 30 of the DebugMode 'H' packet, byte [4]:
            // 0 aggressive, 1 defensive, 2 passive; owner 2026-10-30, passive 2026-11-11). The original client keeps
            // /aggressive, /defensive and /passive.
            Hire165.Send(player, 30, [StanceByte(stance)]);
            if (previous == CompanionStance.Passive && stance != CompanionStance.Passive)
                RestorePets(player);
            if (previous != stance && Log.IsInfoEnabled)
                Log.Info($"COMPANION_STANCE player=\"{player.Name}\" stance={stance.ToString().ToLowerInvariant()} " +
                         $"was={previous.ToString().ToLowerInvariant()}");
        }

        /// <summary>
        /// Leaving /passive: the companions' pets defend again at once (outside a capital, where companion pets stay
        /// passive anyway), instead of waiting for their next attack order.
        /// </summary>
        private static void RestorePets(GamePlayer player)
        {
            if (player.Group == null) return;
            foreach (GameBot bot in player.Group.GetMembersInTheGroup().OfType<GameBot>())
            {
                if (!bot.IsPlayerCompanion || (bot.PlayerGroupLeader ?? bot.Owner) != player || bot.CurrentRegion?.IsCapitalCity == true)
                    continue;
                foreach (GameNPC pet in BotGroupPetBuffTargets.AttachedTree(bot.ControlledBrain, bot, new HashSet<IControlledBrain>()))
                    if (pet.Brain is IControlledBrain brain && brain.AggressionState == eAggressionState.Passive)
                        brain.SetAggressionState(eAggressionState.Defensive);
            }
        }

        /// <summary>The op-30 payload byte for a stance: 0 aggressive, 1 defensive, 2 passive.</summary>
        public static byte StanceByte(CompanionStance stance) => (byte)stance;

        public static CompanionStance StanceOf(GamePlayer player) =>
            player != null && Modes.TryGetValue(player, out Mode mode) ? mode.Stance : CompanionStance.Aggressive;

        /// <summary>The player has /defensive on.</summary>
        public static bool IsDefensive(GamePlayer player) => StanceOf(player) == CompanionStance.Defensive;

        /// <summary>
        /// The player has /passive on (owner 2026-11-11: "for retreating or walking through areas without stopping to
        /// fight anything"): their companions and the companions' pets follow and fight nothing.
        /// </summary>
        public static bool IsPassive(GamePlayer player) => StanceOf(player) == CompanionStance.Passive;

        public static GamePlayer DefensiveLeader(GameLiving actor) => LeaderWith(actor, CompanionStance.Defensive);

        /// <summary>The player whose /passive holds this companion or companion pet; never a human's pet or a gamebot.</summary>
        public static GamePlayer PassiveLeader(GameLiving actor) => LeaderWith(actor, CompanionStance.Passive);

        private static GamePlayer LeaderWith(GameLiving actor, CompanionStance stance)
        {
            // Include companion pets, never human pets or autonomous bots.
            for (int depth = 0; depth < 8 && actor != null; depth++)
            {
                if (actor is GameBot bot)
                {
                    GamePlayer leader = bot.PlayerGroupLeader ?? bot.Owner;
                    return bot.IsPlayerCompanion &&
                        leader != null && bot.Group != null && bot.Group == leader.Group &&
                        Modes.TryGetValue(leader, out Mode mode) && mode.Stance == stance ? leader : null;
                }
                actor = (actor as GameNPC)?.Brain is IControlledBrain controlled ? controlled.Owner : null;
            }
            return null;
        }
        public static bool Allows(GameLiving actor, GameLiving target)
        {
            // /passive: nothing at all, not even an ordered /pull target (the order is refused and cancelled anyway).
            if (PassiveLeader(actor) != null) return false;
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
            if (player != null && Modes.TryGetValue(player, out Mode mode) && mode.Stance == CompanionStance.Defensive) mode.Pull = target;
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
