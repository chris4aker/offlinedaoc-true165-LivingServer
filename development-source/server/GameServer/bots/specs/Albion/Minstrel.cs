namespace DOL.GS
{
    public class MinstrelBotSpec : BotSpec
    {
        public MinstrelBotSpec()
        {
            SpecName = "MinstrelBotSpec";

            // Period Minstrel templates: slash 160, thrust 62.
            switch (Pick(160, 62))
            {
                case 0: WeaponOneType = eObjectType.SlashingWeapon; break;
                case 1: WeaponOneType = eObjectType.ThrustWeapon; break;
            }

            int randVariance = Util.Random(4);
            SpecType = eSpecType.Instrument;

            // Period Minstrels kept stealth 18-31 for their stealth and detection.
            switch (randVariance)
            {
                case 0:
                case 1:
                Add(ObjToSpec(WeaponOneType), 39, 0.6f);
                Add(Specs.Instruments, 50, 1.0f);
                Add(Specs.Stealth, 18, 0.1f);
                break;

                case 2:
                Add(ObjToSpec(WeaponOneType), 34, 0.6f);
                Add(Specs.Instruments, 50, 1.0f);
                Add(Specs.Stealth, 26, 0.1f);
                break;

                case 3:
                Add(ObjToSpec(WeaponOneType), 29, 0.6f);
                Add(Specs.Instruments, 50, 1.0f);
                Add(Specs.Stealth, 31, 0.1f);
                break;

                case 4:
                Add(ObjToSpec(WeaponOneType), 44, 0.6f);
                Add(Specs.Instruments, 44, 1.0f);
                Add(Specs.Stealth, 21, 0.1f);
                break;
            }
        }
    }
}
