namespace DOL.GS
{
    public class ClericBotSpec : BotSpec
    {
        public ClericBotSpec(eSpecType spec)
        {
            SpecName = "ClericBotSpec";

            WeaponOneType = eObjectType.CrushingWeapon;

            var randVariance = spec switch
            {
                eSpecType.EnhanceCleric => Util.Random(0, 3),
                eSpecType.RejuvCleric => Util.Random(4, 5),
                eSpecType.SmiteCleric => Util.Random(6, 7),
                _ => Util.Random(7),
            };

            switch (randVariance)
            {
                case 0:
                case 1:
                SpecType = eSpecType.EnhanceCleric;
                Add(Specs.Rejuvenation, 33, 0.5f);
                Add(Specs.Enhancement, 42, 0.8f);
                Add(Specs.Smite, 7, 0.0f);
                break;

                case 2:
                case 3:
                SpecType = eSpecType.EnhanceCleric;
                Add(Specs.Rejuvenation, 36, 0.5f);
                Add(Specs.Enhancement, 40, 0.8f);
                Add(Specs.Smite, 4, 0.0f);
                break;

                case 4:
                SpecType = eSpecType.RejuvCleric;
                Add(Specs.Rejuvenation, 46, 0.8f);
                Add(Specs.Enhancement, 28, 0.5f);
                Add(Specs.Smite, 4, 0.0f);
                break;

                case 5:
                SpecType = eSpecType.RejuvCleric;
                Add(Specs.Rejuvenation, 50, 0.8f);
                Add(Specs.Enhancement, 20, 0.5f);
                Add(Specs.Smite, 4, 0.0f);
                break;

                // Period smite Clerics: Smite 44-48, Rejuvenation about 23 for heals, the rest Enhancement.
                case 6:
                SpecType = eSpecType.SmiteCleric;
                Add(Specs.Rejuvenation, 23, 0.5f);
                Add(Specs.Enhancement, 16, 0.1f);
                Add(Specs.Smite, 46, 0.8f);
                break;

                case 7:
                SpecType = eSpecType.SmiteCleric;
                Add(Specs.Rejuvenation, 23, 0.5f);
                Add(Specs.Enhancement, 20, 0.1f);
                Add(Specs.Smite, 44, 0.8f);
                break;
            }
        }
    }
}
