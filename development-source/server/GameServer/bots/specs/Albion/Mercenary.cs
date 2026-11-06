namespace DOL.GS
{
    public class MercenaryBotSpec : BotSpec
    {
        public MercenaryBotSpec(eSpecType spec)
        {
            SpecName = "MercenaryBotSpec";

            // Period Mercenary templates: slash 36, thrust 22, crush 11.
            switch (Pick(36, 22, 11))
            {
                case 0: WeaponOneType = eObjectType.SlashingWeapon; break;
                case 1: WeaponOneType = eObjectType.ThrustWeapon; break;
                case 2: WeaponOneType = eObjectType.CrushingWeapon; break;
            }

            // DW and shield is about 1 in 8 Mercenaries (family weights in BotSpec).
            var randVariance = spec switch
            {
                eSpecType.DualWield => Util.Random(0, 2),
                eSpecType.DualWieldAndShield => 3,
                _ => Pick(7, 0, 0, 1),
            };

            switch (randVariance)
            {
                case 0:
                case 1:
                case 2:
                SpecType = eSpecType.DualWield;
                Add(ObjToSpec(WeaponOneType), 50, 0.8f);
                Add(Specs.Dual_Wield, 50, 1.0f);
                Add(Specs.Parry, 28, 0.2f);
                break;

                case 3:
                SpecType = eSpecType.DualWieldAndShield;
                Add(ObjToSpec(WeaponOneType), 39, 0.8f);
                Add(Specs.Dual_Wield, 50, 0.9f);
                Add(Specs.Shields, 42, 0.5f);
                Add(Specs.Parry, 6, 0.1f);
                break;
            }
        }
    }
}
