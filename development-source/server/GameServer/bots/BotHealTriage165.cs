namespace DOL.GS
{
    /// <summary>
    /// True 1.65 heal triage. Prima (Healer tips): "Over-healing your party paints a target on you. Try to keep everyone at
    /// two-thirds Health or better, and stick to Minor Heals instead of large Heals." Freddyshouse healer threads
    /// (2002-2003): small heals by default, big and instant heals for emergencies, no group heal on the pull (it draws
    /// aggro from every monster). Normal mode keeps its own numbers.
    /// </summary>
    public static class BotHealTriage165
    {
        /// <summary>Where every healer starts healing ("two-thirds Health"); the player's "Heal at" choice overrides it.</summary>
        public const int HealAtPercent = 65;
        public const int GroupHealMembers = 3;
        /// <summary>A group heal waits until the fight has run this long (not on the pull).</summary>
        public const long GroupHealFightMilliseconds = 4_000;

        /// <summary>
        /// A group heal (not a group heal-over-time, which may still go on two): three or more members under the heal line,
        /// and in a fight only once it has run a few seconds.
        /// </summary>
        public static bool GroupHealWorthwhile(int membersHurt, bool inFight, long fightMilliseconds) =>
            membersHurt >= GroupHealMembers && (!inFight || fightMilliseconds >= GroupHealFightMilliseconds);

        /// <summary>The heal line for a healing class: the player's choice, else 65% in True 1.65, 80% in normal mode.</summary>
        public static int HealerNeedAt(int healAtChoice, bool classic165) =>
            healAtChoice == 1 ? 90 : healAtChoice == 2 ? 50 : classic165 ? HealAtPercent : 80;
    }
}
