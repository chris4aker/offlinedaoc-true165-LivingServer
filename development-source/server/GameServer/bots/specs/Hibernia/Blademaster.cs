namespace DOL.GS
{
    public class BlademasterBotSpec : BotSpec
    {
        public BlademasterBotSpec(eSpecType spec)
        {
            SpecName = "BlademasterBotSpec";
            Is2H = false;

            int randBaseWeap = Util.Random(2);

            switch (randBaseWeap)
            {
                case 0: WeaponOneType = eObjectType.Blades; break;
                case 1: WeaponOneType = eObjectType.Piercing; break;
                case 2: WeaponOneType = eObjectType.Blunt; break;
            }

            var randVariance = spec switch
            {
                eSpecType.DualWield => Util.Random(1),
                eSpecType.DualWieldAndShield => Util.Random(2, 3),
                _ => Pick(2, 2, 1, 1),
            };

            switch (randVariance)
            {
                case 0:
                case 1:
                SpecType = eSpecType.DualWield;
                Add(ObjToSpec(WeaponOneType), 50, 0.8f);
                Add(Specs.Celtic_Dual, 50, 1.0f);
                Add(Specs.Parry, 28, 0.2f);
                break;

                case 2:
                SpecType = eSpecType.DualWieldAndShield;
                Add(ObjToSpec(WeaponOneType), 39, 0.8f);
                Add(Specs.Celtic_Dual, 50, 1.0f);
                Add(Specs.Shields, 42, 0.5f);
                Add(Specs.Parry, 6, 0.1f);
                break;

                // Period shield Blademaster: weapon 50 / shield 42 / parry 36, Celtic Dual low.
                case 3:
                SpecType = eSpecType.DualWieldAndShield;
                Add(ObjToSpec(WeaponOneType), 50, 1.0f);
                Add(Specs.Celtic_Dual, 16, 0.1f);
                Add(Specs.Shields, 42, 0.7f);
                Add(Specs.Parry, 36, 0.5f);
                break;
            }
        }
    }
}
