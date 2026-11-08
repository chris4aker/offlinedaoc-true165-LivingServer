using System;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>Bot-only rest policy. Does not change the game's combat flag or human regen.</summary>
    public static class BotRestRecovery
    {
        public const int QuietMilliseconds = 2000;
        public const int TickMilliseconds = 1000;

        // A companion already beside its stationary player need not finish tiny
        // formation corrections before recovering. Combat/cast/order gates are
        // still checked independently by the caller.
        public static bool CanSettleForRest(bool temporary, bool autonomous, bool atPlayer,
            bool leaderMoving, bool needsRecovery) =>
            temporary && !autonomous && atPlayer && !leaderMoving && needsRecovery;

        public static bool RecentlyFought(long now, long lastCombatTick) =>
            lastCombatTick > 0 && now - lastCombatTick < QuietMilliseconds;

        public static long LatestRestActivityTick(long playerActivityTick, long companionActivityTick) =>
            Math.Max(playerActivityTick, companionActivityTick);

        // Fast-rest floor, not a cap: retain stronger legitimate regen bonuses.
        public static int RecoveryAmount(int nativeAmount, int maximum) =>
            Math.Max(nativeAmount, Math.Max(1, (int)Math.Ceiling(maximum * 0.10)));

        public static bool HasAnyResourceDeficit(
            int health, int maxHealth,
            int mana, int maxMana,
            int endurance, int maxEndurance) =>
            maxHealth > 0 && health < maxHealth ||
            maxMana > 0 && mana < maxMana ||
            maxEndurance > 0 && endurance < maxEndurance;

        /// <summary>
        /// Temporary /spawn companions recover whenever the player parks their
        /// party, rather than waiting for autonomous-world emergency thresholds.
        /// This is deliberately value-only so the player-led FOLLOW state remains
        /// the sole owner of movement, combat, and group-membership decisions.
        /// </summary>
        public static bool ShouldTemporaryCompanionRest(
            bool isTemporaryCompanion,
            bool atPlayer,
            bool movementBlocksRest,
            bool combatBlocksRest,
            long now,
            long lastPlayerActivityTick,
            int health,
            int maxHealth,
            int mana,
            int maxMana,
            int endurance,
            int maxEndurance) =>
            isTemporaryCompanion && atPlayer && !movementBlocksRest && !combatBlocksRest &&
            now >= lastPlayerActivityTick && now - lastPlayerActivityTick >= QuietMilliseconds &&
            HasAnyResourceDeficit(health, maxHealth, mana, maxMana, endurance, maxEndurance);

        /// <summary>
        /// Persistent autonomous playerbots use the same two-second quiet
        /// window as temporary companions, but their regeneration must not be
        /// tied to a leader, a rest point, or a stationary pose.  This is a
        /// logical recovery window only: it never stops movement, casts, or
        /// changes the bot's combat state.  Any current attack, cast, aggro,
        /// party combat, or controlled-pet combat immediately disqualifies it.
        /// </summary>
        public static bool ShouldAutonomousPlayerBotRecover(GameBot bot, long now)
        {
            if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper ||
                !bot.IsAlive || !HasAnyResourceDeficit(bot.Health, bot.MaxHealth,
                    bot.Mana, bot.MaxMana, bot.Endurance, bot.MaxEndurance) ||
                BlocksRest(bot))
                return false;

            long lastCombatTick = Math.Max(bot.LastAttackTick, bot.LastAttackedByEnemyTick);
            if (lastCombatTick <= 0)
                lastCombatTick = bot.AutonomousRecoveryStartTick;
            return !RecentlyFought(now, lastCombatTick);
        }

        /// <summary>
        /// A Necromancer does not sit down while its servant finishes a buff or a power gift (owner 2026-11-08: Aldith sat,
        /// stood to gift, sat again and the gift kept being cut off). A stuck command is dropped after 10 s
        /// (NecromancerPetBrain.DropStuckCommand), so this cannot hold off a rest for good.
        /// </summary>
        public static bool ServantBusyHelping(GameBot bot) =>
            bot?.ControlledBrain is DOL.AI.Brain.NecromancerPetBrain servantBrain && servantBrain.HasPendingHelpfulCommand;

        public static bool BlocksRest(GameBot bot)
        {
            if (bot == null || !bot.IsAlive || bot.IsCrowdControlled || bot.IsAttacking ||
                bot.Brain is BotBrain { HasAggro: true } ||
                (bot.IsCasting || bot.castingComponent?.HasPendingSkillRequests == true) &&
                    !BotSongTwistPolicy.HasMobileSongCast(bot) && !KeepsRestThroughOwnCast(bot) ||
                bot.IsOnStableMasterRoute || HasCombat(bot, GameLoop.GameLoopTime)) return true;
            if (bot.Group == null) return false;
            // Only the local party matters. A distant casualty cannot prevent rest
            // indefinitely; nearby group attacks still wake every bot immediately.
            foreach (GameLiving member in bot.Group.GetMembersInTheGroup())
                if (member != bot && member.IsAlive && member.CurrentRegionID == bot.CurrentRegionID &&
                    bot.IsWithinRadius(member, BotBrain.GROUP_DEFENSE_ASSIST_RADIUS) &&
                    HasCombat(member, GameLoop.GameLoopTime)) return true;
            return false;
        }

        private static bool HasCombat(GameLiving living, long now)
        {
            if (RecentlyFought(now, Math.Max(living.LastAttackTick, living.LastAttackedByEnemyTick)) ||
                living.IsAttacking || living.IsCrowdControlled ||
                living.IsCasting && living.castingComponent?.SpellHandler?.Spell?.IsHarmful == true ||
                living.InCombat) return true;

            GameNPC pet = living.ControlledBrain?.Body;
            if (PetHasCombat(pet, now)) return true;
            // Bonedancer subordinate pets can fight independently of their commander.
            if (pet?.ControlledNpcList != null)
                foreach (IControlledBrain subPet in pet.ControlledNpcList)
                    if (PetHasCombat(subPet?.Body, now)) return true;
            return false;
        }

        private static bool PetHasCombat(GameNPC pet, long now) => pet?.IsAlive == true &&
            (RecentlyFought(now, Math.Max(pet.LastAttackTick, pet.LastAttackedByEnemyTick)) ||
             pet.IsAttacking || pet.IsCasting && pet.castingComponent?.SpellHandler?.Spell?.IsHarmful == true ||
             pet.InCombat);

        public static bool NeedsOrContinuesRest(GameBot bot) => bot.IsRecoveryResting ||
            AutonomousRestPolicy.NeedsRecovery(bot.HealthPercent, bot.ManaPercent, bot.EndurancePercent, bot.MaxMana > 0);

        public static bool DeferOptionalCasterUpkeep(GameBot bot) => bot != null &&
            (BotSpellPower.IsOffensiveCaster(bot) && bot.IsEnhancedResting || SeatedInPlayerGroup165(bot));

        /// <summary>
        /// True 1.65 Mode (owner 2026-11-06: "make it so they can use all available power for buffs without sitting outside
        /// combat"): a bot in its player's group spends its power on buffs first and sits only when it can't cast the next
        /// one. It stays standing while a buff or heal it just cast is followed by another (GameBot.SupportCastGraceUntil),
        /// and once seated it rests until full before the optional buffs and pet buffs go on again (a missing pet is still
        /// summoned, a heal still stands it up), so it never stands up and sits down again for every buff.
        /// </summary>
        public static bool BuffingBeforeRest(GameBot bot, long now) => Classic165Mode.IsOn && bot != null &&
            bot.IsPlayerLedGroup && !bot.IsTemporaryGroupHelper && !bot.IsRecoveryResting && now < bot.SupportCastGraceUntil;

        /// <summary>
        /// Pure rule, True 1.65 Mode: a bot in its player's group keeps its rest (the "rest until full" lock) through an
        /// out-of-combat heal or buff - it stands up to cast, as a player does, and sits down again after (BeginRecoveryRest
        /// re-seats a locked bot). Before, every out-of-combat heal ended the rest, so a Healer or Shaman topping up the
        /// group sat down and stood up again without end and never got its power back. Combat still ends the rest.
        /// </summary>
        public static bool KeepsRestThroughCast(bool classic165, bool playerLedGroup, bool temporary, bool helpful, bool combat) =>
            classic165 && playerLedGroup && !temporary && helpful && !combat;

        /// <summary>The bot's current, queued and requested casts are all helpful and keep its rest (see KeepsRestThroughCast).</summary>
        private static bool KeepsRestThroughOwnCast(GameBot bot)
        {
            if (!KeepsRestThroughCast(Classic165Mode.IsOn, bot.IsPlayerLedGroup, bot.IsTemporaryGroupHelper, true, false))
                return false;
            CastingComponent casting = bot.castingComponent;
            if (casting == null)
                return false;
            Spell active = casting.SpellHandler?.Spell, queued = casting.QueuedSpellHandler?.Spell;
            bool pending = casting.TryPeekPendingSpell(out Spell requested);
            return active?.IsHarmful != true && queued?.IsHarmful != true && (!pending || requested is { IsHarmful: false });
        }

        private static bool SeatedInPlayerGroup165(GameBot bot) => Classic165Mode.IsOn &&
            bot.IsPlayerLedGroup && !bot.IsTemporaryGroupHelper && bot.IsRecoveryResting;
    }
}
