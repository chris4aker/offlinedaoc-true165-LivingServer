using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Owner 2026-11-10 (round-2 review): before a heal over time, a present effect of the same family only holds it back
    /// when the server would really make them clash (SpellHandler.HasConflictingEffectWith: the same EffectGroup when
    /// either has one, else the same SpellType). A Skald's Song of Rest (EffectGroup 72) no longer stops a Shaman's
    /// regeneration (EffectGroup 71): the two stack on the server.
    /// </summary>
    public static class BotHotPrecheck
    {
        [ServerProperty("bots", "bot_hot_precheck_conflict_rule",
            "Before a heal over time, does a present effect hold it back only when the server would make them conflict (same effect group, else same spell type)? Off: any equal-or-stronger effect of the same family holds it back.", true)]
        public static bool ConflictRule = true;

        /// <summary>Pure rule: the server's conflict test (SpellHandler.HasConflictingEffectWith), plus the same spell.</summary>
        public static bool Conflicts(Spell hot, Spell existing) =>
            hot != null && existing != null &&
            (existing.ID == hot.ID ||
             (hot.EffectGroup != 0 || existing.EffectGroup != 0
                 ? hot.EffectGroup == existing.EffectGroup
                 : hot.SpellType == existing.SpellType));

        /// <summary>
        /// Pure rule: a present effect holds the heal over time back. An effect with no spell is kept as a block (as
        /// before); one that does not conflict never blocks; a conflicting one blocks when it is the same spell, both are
        /// concentration, or it is at least as strong (value x effectiveness, as the server weighs it).
        /// </summary>
        public static bool PresentBlocksHot(Spell hot, Spell existing, double hotStrength, double existingStrength,
            bool bothConcentration)
        {
            if (existing == null)
                return true;
            if (!Conflicts(hot, existing))
                return false;
            return existing.ID == hot.ID || bothConcentration || existingStrength >= hotStrength;
        }
    }
}
