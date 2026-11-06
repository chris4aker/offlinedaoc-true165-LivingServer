namespace DOL.GS
{
    /// <summary>
    /// True 1.65 heal triage. Prima (Healer tips): "Over-healing your party paints a target on you. Try to keep everyone at
    /// two-thirds Health or better, and stick to Minor Heals instead of large Heals." Freddyshouse healer threads
    /// (2002-2003): small heals by default, big and instant heals for emergencies. Normal mode keeps its own numbers.
    /// </summary>
    public static class BotHealTriage165
    {
        /// <summary>Where every healer starts healing ("two-thirds Health"); the player's "Heal at" choice overrides it.</summary>
        public const int HealAtPercent = 65;

        /// <summary>The heal line for a healing class: the player's choice, else 65% in True 1.65, 80% in normal mode.</summary>
        public static int HealerNeedAt(int healAtChoice, bool classic165) =>
            healAtChoice == 1 ? 90 : healAtChoice == 2 ? 50 : classic165 ? HealAtPercent : 80;
    }
}
