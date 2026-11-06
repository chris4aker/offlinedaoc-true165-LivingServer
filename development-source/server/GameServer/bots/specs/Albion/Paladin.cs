namespace DOL.GS
{
    public class PaladinBotSpec : BotSpec
    {
        public PaladinBotSpec(eSpecType spec)
        {
            SpecName = "PaladinBotSpec";

            // Period Paladins were mostly slash (about 8 in 10).
            switch (Pick(8, 1, 1))
            {
                case 0:
                WeaponOneType = eObjectType.SlashingWeapon;
                DamageType = eWeaponDamageType.Slash;
                break;

                case 1:
                WeaponOneType = eObjectType.ThrustWeapon;
                DamageType = eWeaponDamageType.Thrust;
                break;

                case 2:
                WeaponOneType = eObjectType.CrushingWeapon;
                DamageType = eWeaponDamageType.Crush;
                break;
            }

            WeaponTwoType = eObjectType.TwoHandedWeapon;

            // Two-handed is about 15% (family weights in BotSpec).
            var randVariance = spec switch
            {
                eSpecType.TwoHanded => Util.Random(0, 1),
                eSpecType.TwoHandHybrid => Util.Random(0, 1),
                eSpecType.OneHanded => Util.Random(2, 7),
                eSpecType.OneHandAndShield => Util.Random(2, 7),
                _ => Util.Random(5) == 0 ? Util.Random(0, 1) : Util.Random(2, 7),
            };

            // One-hand builds: chants 46-48, shield 42, weapon 29-44, the rest parry (period templates).
            switch (randVariance)
            {
                case 0:
                Is2H = true;
                SpecType = eSpecType.TwoHandHybrid;
                Add(ObjToSpec(WeaponOneType), 39, 0.6f);
                Add(ObjToSpec(WeaponTwoType), 44, 0.8f);
                Add(Specs.Chants, 48, 0.9f);
                Add(Specs.Parry, 19, 0.2f);
                break;

                case 1:
                Is2H = true;
                SpecType = eSpecType.TwoHandHybrid;
                Add(ObjToSpec(WeaponOneType), 34, 0.6f);
                Add(ObjToSpec(WeaponTwoType), 50, 0.8f);
                Add(Specs.Chants, 48, 0.9f);
                Add(Specs.Parry, 13, 0.2f);
                break;

                case 2:
                case 3:
                SpecType = eSpecType.OneHandAndShield;
                Add(ObjToSpec(WeaponOneType), 44, 0.6f);
                Add(Specs.Chants, 46, 1.0f);
                Add(Specs.Shields, 42, 0.7f);
                Add(Specs.Parry, 3, 0.1f);
                break;

                case 4:
                case 5:
                SpecType = eSpecType.OneHandAndShield;
                Add(ObjToSpec(WeaponOneType), 39, 0.6f);
                Add(Specs.Chants, 48, 1.0f);
                Add(Specs.Shields, 42, 0.7f);
                Add(Specs.Parry, 15, 0.1f);
                break;

                case 6:
                SpecType = eSpecType.OneHandAndShield;
                Add(ObjToSpec(WeaponOneType), 34, 0.6f);
                Add(Specs.Chants, 48, 1.0f);
                Add(Specs.Shields, 42, 0.7f);
                Add(Specs.Parry, 24, 0.1f);
                break;

                case 7:
                SpecType = eSpecType.OneHandAndShield;
                Add(ObjToSpec(WeaponOneType), 29, 0.6f);
                Add(Specs.Chants, 48, 1.0f);
                Add(Specs.Shields, 42, 0.7f);
                Add(Specs.Parry, 30, 0.1f);
                break;
            }
        }
    }
}
