namespace DOL.GS
{
    public static class BotCasterPriority
    {
        // owner 2026-11-08: a bomber is damage only when its payload is (root bombers carry Damage 50 too)
        public static bool IsDamage(Spell spell) => spell != null &&
            (spell.Damage > 0 && !BotControlSpells.IsControlBomber(spell) ||
             spell.SpellType is eSpellType.DirectDamage or eSpellType.Lifedrain or
                eSpellType.Bolt or eSpellType.DirectDamageWithDebuff or eSpellType.DamageSpeedDecrease);

        /// <summary>IsDamage with an explicit bomber payload lookup (tests).</summary>
        public static bool IsDamageWith(Spell spell, System.Func<int, Spell> resolve) => spell != null &&
            (spell.Damage > 0 && !BotControlSpells.IsControlBomberWith(spell, resolve) ||
             spell.SpellType is eSpellType.DirectDamage or eSpellType.Lifedrain or
                eSpellType.Bolt or eSpellType.DirectDamageWithDebuff or eSpellType.DamageSpeedDecrease);

        public static bool AllowInstant(Spell spell, bool rangedCaster, bool meleePressure, bool debuffOpening = false) =>
            !rangedCaster || IsDamage(spell) || meleePressure || debuffOpening && IsDebuff(spell);

        /// <summary>
        /// A debuff a caster opens with before nuking (Prima: Runemaster, Spiritmaster, Sorcerer, Cabalist debuff first,
        /// then nuke): stat, attack-speed, armor and resist debuffs, and disease (stops the monster's regeneration).
        /// </summary>
        public static bool IsDebuff(Spell spell) => spell != null && !IsDamage(spell) && spell.SpellType is
            eSpellType.StrengthDebuff or eSpellType.DexterityDebuff or eSpellType.ConstitutionDebuff or
            eSpellType.StrengthConstitutionDebuff or eSpellType.DexterityQuicknessDebuff or
            eSpellType.DexterityConstitutionDebuff or eSpellType.WeaponSkillConstitutionDebuff or
            eSpellType.CombatSpeedDebuff or eSpellType.MeleeDamageDebuff or eSpellType.ArmorAbsorptionDebuff or
            eSpellType.ArmorFactorDebuff or eSpellType.AllStatsPercentDebuff or eSpellType.CrushSlashThrustDebuff or
            eSpellType.EffectivenessDebuff or eSpellType.Disease or
            eSpellType.SlashResistDebuff or eSpellType.CrushResistDebuff or eSpellType.ThrustResistDebuff or
            eSpellType.BodyResistDebuff or eSpellType.SpiritResistDebuff or eSpellType.EnergyResistDebuff or
            eSpellType.HeatResistDebuff or eSpellType.ColdResistDebuff or eSpellType.MatterResistDebuff;

        /// <summary>At most this many debuffs open a fight on one target; then the caster nukes.</summary>
        public const int OpeningDebuffs = 2;

        /// <summary>
        /// Debuffing is worth its cast on a fresh target that will last: a player-like enemy, a monster of the caster's level
        /// or above, or any monster when grouped (the group's damage gains from it). Not on greys and blues solo, and not
        /// below 30% power, where the power belongs to the nukes.
        /// </summary>
        public static bool DebuffOpeningWorthwhile(int casterLevel, int casterPowerPercent, bool grouped,
            int targetLevel, int targetHealthPercent, bool targetIsPlayerLike, int debuffsCastOnTarget) =>
            debuffsCastOnTarget < OpeningDebuffs && casterPowerPercent >= 30 && targetHealthPercent >= 60 &&
            (targetIsPlayerLike || grouped || targetLevel >= casterLevel);

        public const int SavePowerBelowPercent = 50;
        public const int FinishTargetBelowPercent = 30;

        /// <summary>
        /// True 1.65 (period forums, "Theurgist tips": stop casting at about half power, power comes back faster above it):
        /// with the group's tank holding the monster a caster casts no optional nukes under half power, unless the monster
        /// is nearly dead. Crowd control, debuffs and heals are not affected.
        /// </summary>
        public static bool SavePowerInGroup(int casterPowerPercent, int targetHealthPercent, bool tankHoldsTarget) =>
            tankHoldsTarget && casterPowerPercent < SavePowerBelowPercent && targetHealthPercent >= FinishTargetBelowPercent;

        /// <summary>
        /// A player's companion saves power unless the player switched "Save power" off (AI Options choice 1) or made a
        /// healer an attacker (role "Attack, heal only emergencies"): that companion nukes freely as before.
        /// </summary>
        public static bool CompanionSavesPower(int savePowerChoice, bool attackingHealer) =>
            savePowerChoice != 1 && !attackingHealer;
    }
}
