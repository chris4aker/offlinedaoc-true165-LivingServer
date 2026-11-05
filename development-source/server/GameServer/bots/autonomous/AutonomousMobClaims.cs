using System.Runtime.CompilerServices;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>
    /// One fight per monster (owner 2026-11-05: "make it so gamebots stop attacking same mobs and sharing xp from mobs.
    /// bots shouldnt attack mobs that other bots are fighting unless they are in the same group"). A gamebot that pulls
    /// a monster claims it for itself or its group for 30 s (enough to reach it and land the first blow); after that
    /// the monster's attackers say whose it is. Another gamebot outside that group leaves it alone - claimed, hit by a
    /// bot, a player or their pets from another group. Self-defense is untouched (a monster attacking a bot is fought).
    /// </summary>
    public static class AutonomousMobClaims
    {
        public const int ClaimMilliseconds = 30_000;

        private sealed class Claim
        {
            public object Key;
            public long Until;
        }

        private static readonly ConditionalWeakTable<GameNPC, Claim> Claims = new();

        /// <summary>The bot's side: its group, or itself when solo.</summary>
        public static object SideOf(GameLiving living) => (object)living?.Group ?? living;

        public static void Take(GameBot bot, GameNPC npc)
        {
            if (bot == null || npc == null)
                return;
            Claim claim = Claims.GetValue(npc, _ => new Claim());
            lock (claim)
            {
                claim.Key = SideOf(bot);
                claim.Until = GameLoop.GameLoopTime + ClaimMilliseconds;
            }
        }

        /// <summary>Someone outside the bot's side has this monster: a fresh claim, or an attacker (or its pet).</summary>
        public static bool TakenByOther(GameBot bot, GameNPC npc)
        {
            if (bot == null || npc == null)
                return false;
            object mine = SideOf(bot);
            if (Claims.TryGetValue(npc, out Claim claim))
            {
                lock (claim)
                {
                    if (claim.Until > GameLoop.GameLoopTime && claim.Key != null && !ReferenceEquals(claim.Key, mine) &&
                        (claim.Key is not GameLiving holder || holder.IsAlive && holder.ObjectState == GameObject.eObjectState.Active))
                        return true;
                }
            }
            var attackers = npc.attackComponent?.AttackerTracker?.Attackers;
            if (attackers == null)
                return false;
            foreach (GameLiving attacker in attackers)
            {
                if (attacker == null || !attacker.IsAlive || attacker == bot)
                    continue;
                GameLiving side = attacker is GameNPC { Brain: IControlledBrain controlled } && controlled.Owner != null
                    ? controlled.Owner : attacker;
                if (side == bot || side is not (GameBot or GamePlayer))
                    continue;
                if (!ReferenceEquals(SideOf(side), mine))
                    return true;
            }
            return false;
        }
    }
}
