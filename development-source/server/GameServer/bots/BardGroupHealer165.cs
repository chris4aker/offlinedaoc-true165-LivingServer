using System.Collections.Generic;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Owner 2026-11-10: a Hibernian Bard may fill a bot group's Healer slot. Period Bards carry the Regrowth heals from
    /// level 1, and Hibernia had 6-8 damage bots per healer at levels 6-15 (half of all matchmaking timeouts). Only the PvE
    /// roster matcher uses this; BotPartyRoles.CanFill and the class-wide healer flag stay as they were, so a solo Bard
    /// or a Bard in an attacker or buffer slot plays exactly as before. A Bard holding the slot heals as the group's
    /// healer (BotBrain.IsHealer) for as long as it stays in that group. No spell numbers change.
    /// </summary>
    public static class BardGroupHealer165
    {
        [ServerProperty("bots", "bot_bard_fills_healer_slot",
            "Can a Hibernian Bard fill a bot group's Healer slot (period Regrowth heals), and heal as the group's healer while it holds the slot? Off: only the healing classes fill it.", true)]
        public static bool Enabled = true;

        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(BardGroupHealer165));

        /// <summary>Pure rule: a Bard fills a Healer slot when the switch is on. Every other class and slot is unchanged.</summary>
        public static bool FillsSlot(eCharacterClass characterClass, BotPveGroupRole role, bool enabled) =>
            enabled && role == BotPveGroupRole.Healer && characterClass == eCharacterClass.Bard;

        /// <summary>
        /// The Bard's healer duty (BotBrain.IsHealer): it holds the slot of the group it is in, and the switch is on.
        /// Checked on every read, so turning the switch off stops Bard healing in live groups at once (owner 2026-11-10).
        /// </summary>
        public static bool HoldsHealerSlot(Group slotGroup, Group currentGroup, bool enabled) =>
            enabled && slotGroup != null && ReferenceEquals(slotGroup, currentGroup);

        public static bool HoldsHealerSlot(Group slotGroup, Group currentGroup) =>
            HoldsHealerSlot(slotGroup, currentGroup, Enabled);

        /// <summary>
        /// Gives each Bard holding a Healer slot the healer duty for this group, and takes it away from a Bard moved out
        /// of the slot. The duty ends by itself when the Bard leaves the group (BotBrain compares the group).
        /// </summary>
        public static void MarkHealerSlots(string groupId, Group group, IEnumerable<GameBot> members,
            IReadOnlyDictionary<long, BotPveGroupRole> roles, System.Func<GameBot, long> memberKey)
        {
            if (group == null || members == null || roles == null)
                return;
            foreach (GameBot member in members)
            {
                if (member?.CharacterClass?.ID != (int)eCharacterClass.Bard || member.Brain is not DOL.AI.Brain.BotBrain brain)
                    continue;
                bool healerSlot = Enabled && roles.TryGetValue(memberKey(member), out BotPveGroupRole role) &&
                    role == BotPveGroupRole.Healer;
                if (healerSlot && !ReferenceEquals(brain.HealerSlotGroup, group))
                {
                    brain.HealerSlotGroup = group;
                    Log.Info($"AUTONOMOUS_BARD_HEALER_SLOT group={groupId} bot=\"{member.Name}\" id={member.DatabaseID} " +
                             $"level={member.Level} size={group.MemberCount}");
                }
                else if (!healerSlot && ReferenceEquals(brain.HealerSlotGroup, group))
                    brain.HealerSlotGroup = null;
            }
        }
    }
}
