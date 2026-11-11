using DOL.GS.Spells;

namespace DOL.GS
{
    public class StatBuffECSEffect : ECSGameSpellEffect
    {
        public StatBuffECSEffect(in ECSGameEffectInitParams initParams) : base(initParams) { }

        // owner 2026-11-10: what this instance has put on its owner; a replaced instance never adds or removes again
        private readonly StatBuffBonusLedger _bonus = new();

        public override void OnStartEffect()
        {
            base.OnStartEffect();

            if (OwnerPlayer != null && GamePlayer.SelfBuffChargeIDs.Contains(SpellHandler.Spell.ID))
                OwnerPlayer.ActiveBuffCharges++;

            if (SpellHandler is not PropertyChangingSpell propertyChangingSpell)
                return;

            if (_bonus.BeginApply(Effectiveness))
                foreach (eProperty property in EffectHelper.GetPropertiesFromEffect(EffectType))
                    ApplyBonus(Owner, propertyChangingSpell.BonusCategory1, property, SpellHandler.Spell.Value, Effectiveness, false);

            // Let's not bother checking the effect type and simply attempt to start every regeneration timer instead.
            // This will also update health, endurance, and power if they're above their max amount.
            Owner.StartHealthRegeneration();
            Owner.StartEnduranceRegeneration();
            Owner.StartPowerRegeneration();

            // "You feel more dexterous!"
            // "{0} looks more agile!"
            OnEffectStartsMsg(true, true, true);
        }

        /// <summary>
        /// A pulsing chant or song renews its buff by swapping in a new effect instance without starting it (EffectListComponent),
        /// and only the new one is stopped later. Its bonus is the spell value times ITS effectiveness, which differs from the
        /// one applied when the caster's effectiveness changed in between (resurrection illness wearing off). The bonus then
        /// drifted: a Paladin's endurance chant left group members at a negative endurance regeneration, stuck at 0% even
        /// sitting (2026-11-05: Albion bots "Recovering after release" for 15 hours; the owner's Necromancer in a group with two
        /// Paladins). The bonus is handed over: what this instance applied comes off, the next instance's amount goes on.
        /// </summary>
        public void HandOverBonusTo(StatBuffECSEffect next)
        {
            if (next == null || next == this || SpellHandler is not PropertyChangingSpell current ||
                next.SpellHandler is not PropertyChangingSpell following)
                return;
            // owner 2026-11-10: only what is really on comes off (a start still queued has put nothing on yet), and the
            // replaced instance is marked so its late start or its singer's pulse ending it cannot touch the bonus again
            bool removeOld = _bonus.Supersede(out double oldEffectiveness);
            bool addNext = next._bonus.BeginApply(next.Effectiveness);
            foreach (eProperty property in EffectHelper.GetPropertiesFromEffect(EffectType))
            {
                if (removeOld)
                    ApplyBonus(Owner, current.BonusCategory1, property, SpellHandler.Spell.Value, oldEffectiveness, true);
                if (addNext)
                    ApplyBonus(Owner, following.BonusCategory1, property, next.SpellHandler.Spell.Value, next.Effectiveness, false);
            }
            Owner.StartHealthRegeneration();
            Owner.StartEnduranceRegeneration();
            Owner.StartPowerRegeneration();
        }

        public override void OnStopEffect()
        {
            base.OnStopEffect();

            if (OwnerPlayer != null && GamePlayer.SelfBuffChargeIDs.Contains(SpellHandler.Spell.ID))
                OwnerPlayer.ActiveBuffCharges--;

            if (SpellHandler is not PropertyChangingSpell propertyChangingSpell)
                return;

            if (_bonus.BeginRemove(out double appliedEffectiveness))
                foreach (eProperty property in EffectHelper.GetPropertiesFromEffect(EffectType))
                    ApplyBonus(Owner, propertyChangingSpell.BonusCategory1, property, SpellHandler.Spell.Value, appliedEffectiveness, true);

            // Let's not bother checking the effect type and simply attempt to start every regeneration timer instead.
            // This will also update health, endurance, and power if they're above their max amount.
            Owner.StartHealthRegeneration();
            Owner.StartEnduranceRegeneration();
            Owner.StartPowerRegeneration();

            // "Your agility returns to normal."
            // "{0} loses their graceful edge.""
            OnEffectExpiresMsg(true, false, true);
        }
    }
}
