namespace DOL.GS.Effects
{
    public class SpeedOfSoundECSEffect : ECSGameAbilityEffect
    {
        public SpeedOfSoundECSEffect(in ECSGameEffectInitParams initParams) : base(initParams)
        {
            EffectType = eEffect.SpeedOfSound;
        }

        public override ushort Icon => 4249;

        public override string Name => "Speed Of Sound";

        public override bool HasPositiveEffect => true;

        // The player, or a gamebot in a gamebot's group (True 1.65 bot realm abilities); a pet is left as before.
        private GameLiving Target => OwnerPlayer != null ? OwnerPlayer : Owner as GameBot;

        public override void OnStartEffect()
        {
            GameLiving owner = Target;
            if (owner == null)
                return;

            foreach (ECSGameSpellEffect speedBuff in owner.effectListComponent.GetSpellEffects(eEffect.MovementSpeedBuff))
            {
                if (speedBuff.GetType() != typeof(SpeedOfSoundECSEffect))
                    speedBuff.Disable();
            }

            foreach (ECSGameSpellEffect root in owner.effectListComponent.GetSpellEffects(eEffect.MovementSpeedDebuff))
                root.Disable();

            foreach (ECSGameSpellEffect ichor in owner.effectListComponent.GetSpellEffects(eEffect.Ichor))
                ichor.Disable();

            owner.BuffBonusMultCategory1.Set((int) eProperty.MaxSpeed, this, PropertyCalc.MaxSpeedCalculator.SPEED4);
            if (OwnerPlayer != null)
                OwnerPlayer.Out.SendUpdateMaxSpeed();
            else
                owner.OnMaxSpeedChange();
        }

        public override void OnStopEffect()
        {
            GameLiving owner = Target;
            if (owner == null)
                return;

            owner.BuffBonusMultCategory1.Remove((int)eProperty.MaxSpeed, this);

            foreach (ECSGameSpellEffect speedBuff in owner.effectListComponent.GetSpellEffects(eEffect.MovementSpeedBuff))
                speedBuff.Enable();

            foreach (ECSGameSpellEffect root in owner.effectListComponent.GetSpellEffects(eEffect.MovementSpeedDebuff))
                root.Enable();

            foreach (ECSGameSpellEffect ichor in owner.effectListComponent.GetSpellEffects(eEffect.Ichor))
                ichor.Enable();

            if (OwnerPlayer != null)
                OwnerPlayer.Out.SendUpdateMaxSpeed();
            else
                owner.OnMaxSpeedChange();
        }
    }
}
