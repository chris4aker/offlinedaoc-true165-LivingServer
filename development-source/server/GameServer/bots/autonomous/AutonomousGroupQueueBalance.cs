using System;
using System.Collections.Generic;
using System.Threading;
using DOL.GS.ServerProperties;
using OfflineDaoc.Configuration;

namespace DOL.GS
{
    /// <summary>What a queued bot can give a PvE roster: a Healer slot, a Tank slot, both (Paladin), or neither (damage).</summary>
    [Flags]
    public enum eQueueCapability { None = 0, Healer = 1, Tank = 2 }

    /// <summary>
    /// Owner 2026-11-10: role-aware group goals. Every roster needs a tank and a healer, and 90% of the
    /// 20-minute matchmaking timeouts were damage bots (healers about 2%), because every class drew Group PvE at the
    /// same 40%. Healer and tank bots now favour Group PvE (40% setting: healers 70%, tanks 60%); damage bots draw
    /// exactly as before. Goal choice only: no diversion of damage bots, no change to rosters, timers or movement.
    /// </summary>
    public static class AutonomousGroupQueueBalance
    {
        [ServerProperty("bots", "bot_group_goal_role_aware",
            "Do healer and tank bots favour Group PvE goals more than damage bots (40% setting: healers 70%, tanks 60%)? Damage bots always draw the configured weights. Off: every class draws the configured weights.", true)]
        public static bool RoleAwareGoals = true;

        public const double HealerBoost = 1.75, TankBoost = 1.5, HealerCap = 0.80, TankCap = 0.75;
        private const long ReportIntervalMilliseconds = 10 * 60_000;

        /// <summary>Pure rule: Healer when the class can fill a PvE Healer slot (a Bard too, with its switch), Tank when it can fill a Tank slot.</summary>
        public static eQueueCapability CapabilityFor(eCharacterClass characterClass, bool bardHeals)
        {
            eQueueCapability capability = eQueueCapability.None;
            if (BotPartyRoles.CanFill(characterClass, BotPveGroupRole.Healer) ||
                BardGroupHealer165.FillsSlot(characterClass, BotPveGroupRole.Healer, bardHeals))
                capability |= eQueueCapability.Healer;
            if (BotPartyRoles.CanFill(characterClass, BotPveGroupRole.Tank))
                capability |= eQueueCapability.Tank;
            return capability;
        }

        public static eQueueCapability CapabilityFor(eCharacterClass characterClass) =>
            CapabilityFor(characterClass, BardGroupHealer165.Enabled);

        /// <summary>The capability goal choice uses: None (the plain weights) when the switch is off or the class is unknown.</summary>
        public static eQueueCapability GoalCapabilityFor(GameBot bot) =>
            RoleAwareGoals && bot?.CharacterClass != null ? CapabilityFor((eCharacterClass)bot.CharacterClass.ID) : eQueueCapability.None;

        /// <summary>Log label; a Paladin counts as a healer.</summary>
        public static string RoleLabel(eQueueCapability capability) =>
            (capability & eQueueCapability.Healer) != 0 ? "Healer" : (capability & eQueueCapability.Tank) != 0 ? "Tank" : "Damage";

        public static string RoleLabel(GameBot bot) =>
            bot?.CharacterClass == null ? "Damage" : RoleLabel(CapabilityFor((eCharacterClass)bot.CharacterClass.ID));

        /// <summary>
        /// Pure rule: the Group PvE share for a role. 0 stays 0 and 1 stays 1; healers get configured x 1.75 (at most 0.80),
        /// tanks x 1.5 (at most 0.75), never below the configured share; damage keeps the configured share.
        /// </summary>
        public static double GroupShareFor(eQueueCapability capability, double configuredShare)
        {
            if (!(configuredShare > 0)) return 0;
            if (configuredShare >= 1) return 1;
            if ((capability & eQueueCapability.Healer) != 0)
                return Math.Max(configuredShare, Math.Min(HealerCap, configuredShare * HealerBoost));
            if ((capability & eQueueCapability.Tank) != 0)
                return Math.Max(configuredShare, Math.Min(TankCap, configuredShare * TankBoost));
            return configuredShare;
        }

        /// <summary>
        /// Pure rule: a goal kind (0 solo, 1 group, 2 RvR, 3 battlegrounds) for a role. Damage, excludeGroup, a 0% group
        /// setting or a group-only row return weights.Choose exactly. Otherwise the group share is raised by
        /// <see cref="GroupShareFor"/> and solo, RvR and battlegrounds split the rest in their configured proportions,
        /// in Choose's order.
        /// </summary>
        public static int ChooseKindForRole(BotGoalWeights weights, eQueueCapability capability, double roll,
            bool excludeGroup = false, bool excludeBattlegrounds = false)
        {
            if (capability == eQueueCapability.None || excludeGroup || weights.GroupPve <= 0)
                return weights.Choose(roll, excludeGroup, excludeBattlegrounds);
            int battlegrounds = excludeBattlegrounds ? 0 : weights.Battlegrounds;
            int others = weights.SoloPve + weights.RvR + battlegrounds;
            if (others <= 0)
                return weights.Choose(roll, excludeGroup, excludeBattlegrounds);
            if (!double.IsFinite(roll) || roll < 0 || roll >= 1) throw new ArgumentOutOfRangeException(nameof(roll));
            double group = GroupShareFor(capability, (double)weights.GroupPve / (weights.GroupPve + others));
            double rest = 1 - group;
            double soloEnd = rest * weights.SoloPve / others;
            if (roll < soloEnd) return 0;
            double groupEnd = soloEnd + group;
            if (roll < groupEnd) return 1;
            if (roll < groupEnd + rest * weights.RvR / others) return 2;
            // rounding at the very top falls to the last kind that has a weight
            return battlegrounds > 0 ? 3 : weights.RvR > 0 ? 2 : weights.SoloPve > 0 ? 0 : 1;
        }

        /// <summary>
        /// Pure rule for the one-minute allocator: damage is exactly ChoosePveObjective (same random draws). A healer or
        /// tank raises the still-needed group share by <see cref="GroupShareFor"/>; when nothing is needed the existing
        /// branch runs unchanged.
        /// </summary>
        public static eAutonomousObjectiveKind ChooseRolePveObjective(AutonomousObjectiveAssignments.Allocation target,
            int assignedSolo, int assignedGroup, eQueueCapability capability, Random random = null)
        {
            if (capability == eQueueCapability.None)
                return AutonomousObjectiveAssignments.ChoosePveObjective(target, assignedSolo, assignedGroup, random);
            int soloNeeded = Math.Max(0, target.SoloPve - assignedSolo);
            int groupNeeded = Math.Max(0, target.GroupPve - assignedGroup);
            if (soloNeeded + groupNeeded == 0)
                return AutonomousObjectiveAssignments.ChoosePveObjective(target, assignedSolo, assignedGroup, random);
            double share = GroupShareFor(capability, (double)groupNeeded / (soloNeeded + groupNeeded));
            return (random ?? Random.Shared).NextDouble() < share ? eAutonomousObjectiveKind.GroupPve : eAutonomousObjectiveKind.SoloPve;
        }

        /// <summary>The allocator's PvE pick for one bot, counted for the ten-minute report.</summary>
        public static eAutonomousObjectiveKind ChoosePveFor(GameBot bot, AutonomousObjectiveAssignments.Allocation target,
            int assignedSolo, int assignedGroup)
        {
            eQueueCapability capability = GoalCapabilityFor(bot);
            eAutonomousObjectiveKind kind = ChooseRolePveObjective(target, assignedSolo, assignedGroup, capability);
            NoteGoalRoll(bot, kind);
            return kind;
        }

        // Ten-minute counters per realm (index 0-3 by eRealm value): [damage, healer, tank] rolls and group rolls.
        private static readonly long[,] Rolls = new long[4, 3];
        private static readonly long[,] GroupRolls = new long[4, 3];
        private static long _nextReportTick;

        private static int RoleIndex(eQueueCapability capability) =>
            (capability & eQueueCapability.Healer) != 0 ? 1 : (capability & eQueueCapability.Tank) != 0 ? 2 : 0;

        private static int RealmIndex(eRealm realm) => (int)realm is >= 0 and < 4 ? (int)realm : 0;

        /// <summary>Counts one goal draw (Interlocked; no per-bot log).</summary>
        public static void NoteGoalRoll(GameBot bot, eAutonomousObjectiveKind kind)
        {
            if (bot?.CharacterClass == null) return;
            int realm = RealmIndex(bot.Realm);
            int role = RoleIndex(CapabilityFor((eCharacterClass)bot.CharacterClass.ID));
            Interlocked.Increment(ref Rolls[realm, role]);
            if (kind == eAutonomousObjectiveKind.GroupPve)
                Interlocked.Increment(ref GroupRolls[realm, role]);
        }

        /// <summary>Queue counts for one realm: ungrouped Group PvE bots by role (a Paladin counts as a healer only).</summary>
        public readonly record struct QueueCounts(int Damage, int Healers, int Tanks, int BardHealers);

        public static Dictionary<eRealm, QueueCounts> CountQueues(IEnumerable<GameBot> roster)
        {
            var counts = new Dictionary<eRealm, QueueCounts>();
            foreach (GameBot bot in roster)
            {
                if (bot?.CharacterClass == null || bot.Group != null ||
                    !AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.GroupPve) ||
                    AutonomousObjectiveAssignments.IsBetweenPveTasks(bot))
                    continue;
                var characterClass = (eCharacterClass)bot.CharacterClass.ID;
                int role = RoleIndex(CapabilityFor(characterClass));
                counts.TryGetValue(bot.Realm, out QueueCounts c);
                counts[bot.Realm] = c with
                {
                    Damage = c.Damage + (role == 0 ? 1 : 0),
                    Healers = c.Healers + (role == 1 ? 1 : 0),
                    Tanks = c.Tanks + (role == 2 ? 1 : 0),
                    BardHealers = c.BardHealers + (role == 1 && characterClass == eCharacterClass.Bard ? 1 : 0),
                };
            }
            return counts;
        }

        /// <summary>
        /// Called from the one-minute allocator pass with the roster it already built; every ten minutes writes one
        /// AUTONOMOUS_GROUP_QUEUE_BALANCE line per realm and resets the roll counters. Counts only, no decisions.
        /// </summary>
        public static void ReportIfDue(IEnumerable<GameBot> roster, long nowTick)
        {
            if (_nextReportTick == 0)
            {
                _nextReportTick = nowTick + ReportIntervalMilliseconds;
                return;
            }
            if (nowTick < _nextReportTick)
                return;
            _nextReportTick = nowTick + ReportIntervalMilliseconds;
            Dictionary<eRealm, QueueCounts> counts = CountQueues(roster);
            var log = DOL.Logging.LoggerManager.Create(typeof(AutonomousGroupQueueBalance));
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            {
                int r = RealmIndex(realm);
                counts.TryGetValue(realm, out QueueCounts c);
                long damageRolls = Interlocked.Exchange(ref Rolls[r, 0], 0), damageGroup = Interlocked.Exchange(ref GroupRolls[r, 0], 0);
                long healerRolls = Interlocked.Exchange(ref Rolls[r, 1], 0), healerGroup = Interlocked.Exchange(ref GroupRolls[r, 1], 0);
                long tankRolls = Interlocked.Exchange(ref Rolls[r, 2], 0), tankGroup = Interlocked.Exchange(ref GroupRolls[r, 2], 0);
                log.Warn($"AUTONOMOUS_GROUP_QUEUE_BALANCE realm={GlobalConstants.RealmToName(realm)} roleAware={RoleAwareGoals} " +
                         $"bardHealers={BardGroupHealer165.Enabled} queuedDamage={c.Damage} queuedHealers={c.Healers} " +
                         $"queuedBardHealers={c.BardHealers} queuedTanks={c.Tanks} " +
                         $"damageRolls10m={damageRolls} damageGroupRolls10m={damageGroup} " +
                         $"healerRolls10m={healerRolls} healerGroupRolls10m={healerGroup} " +
                         $"tankRolls10m={tankRolls} tankGroupRolls10m={tankGroup}");
            }
        }
    }
}
