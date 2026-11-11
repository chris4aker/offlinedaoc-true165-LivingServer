namespace DOL.GS
{
    /// <summary>
    /// GitHub #28 (owner 2026-11-10): bots spent concentration on pet buffs that do nothing for a pet. In this server a
    /// pet, charmed monster or necromancer servant (a plain GameNPC) has a fixed power pool (GameNPC.Mana is always 5000),
    /// pays no endurance for styles (StyleProcessor charges only players and bots), and gets no acuity (StatCalculator
    /// adds acuity only to a player class's mana stat). So power and endurance regeneration, acuity and fatigue
    /// buffs are wasted on it. Strength, constitution, dexterity, quickness, armor, resists, haste, health regeneration,
    /// damage adds and procs do help a pet.
    /// </summary>
    public static class BotPetBuffBenefit
    {
        /// <summary>A pet-like target: an NPC that is not a player-like bot.</summary>
        public static bool IsPetLike(GameLiving living) => living is GameNPC && living is not IGamePlayer;

        /// <summary>Pure: whether a buff of this type does anything for a pet.</summary>
        public static bool Helps(Spell spell) => spell?.SpellType switch
        {
            eSpellType.EnduranceRegenBuff or eSpellType.PowerRegenBuff or eSpellType.AcuityBuff or
                eSpellType.FatigueConsumptionBuff => false,
            _ => true,
        };

        /// <summary><see cref="Helps"/>, counting each pet buff skipped as useless (FEATURE_COUNTERS_165, issue #28).</summary>
        public static bool HelpsCounted(Spell spell)
        {
            bool helps = Helps(spell);
            if (!helps)
                FeatureCounters165.Add("pet_buff_useless_skip");
            return helps;
        }

        /// <summary>The realm-buff order rank of a pet in True 1.65: after every player and bot (BotBrain.BuffTargetRank).</summary>
        public const int PetRank = 4;
    }
}
