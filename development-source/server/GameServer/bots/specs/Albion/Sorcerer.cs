namespace DOL.GS
{
    public class SorcererBotSpec : BotSpec
    {
        public SorcererBotSpec(eSpecType spec)
        {
            SpecName = "SorcererBotSpec";

            WeaponOneType = eObjectType.Staff;
            Is2H = true;

            // Mind builds are about half of period Sorcerers (family weights in BotSpec).
            var randVariance = spec switch
            {
                eSpecType.MindSorc => Util.Random(0, 3),
                eSpecType.BodySorc => Util.Random(4, 6),
                eSpecType.MatterSorc => Util.Random(7, 8),
                _ => Util.Random(1) == 0 ? Util.Random(0, 3) : Util.Random(4, 8),
            };

            switch (randVariance)
            {
                case 0:
                case 1:
                SpecType = eSpecType.MindSorc;
                Add(Specs.Matter_Magic, 8, 0.0f);
                Add(Specs.Body_Magic, 30, 0.6f);
                Add(Specs.Mind_Magic, 44, 0.8f);
                break;

                case 2:
                SpecType = eSpecType.MindSorc;
                Add(Specs.Matter_Magic, 26, 0.5f);
                Add(Specs.Body_Magic, 5, 0.0f);
                Add(Specs.Mind_Magic, 47, 0.8f);
                break;

                case 3:
                SpecType = eSpecType.MindSorc;
                Add(Specs.Matter_Magic, 5, 0.0f);
                Add(Specs.Body_Magic, 22, 0.6f);
                Add(Specs.Mind_Magic, 49, 0.8f);
                break;

                // Body and Matter builds take Mind 20-24 second (period templates), the rest in the third line.
                case 4:
                SpecType = eSpecType.BodySorc;
                Add(Specs.Matter_Magic, 17, 0.0f);
                Add(Specs.Body_Magic, 46, 0.8f);
                Add(Specs.Mind_Magic, 22, 0.5f);
                break;

                case 5:
                SpecType = eSpecType.BodySorc;
                Add(Specs.Matter_Magic, 14, 0.0f);
                Add(Specs.Body_Magic, 48, 0.8f);
                Add(Specs.Mind_Magic, 20, 0.5f);
                break;

                case 6:
                SpecType = eSpecType.BodySorc;
                Add(Specs.Matter_Magic, 17, 0.0f);
                Add(Specs.Body_Magic, 45, 0.8f);
                Add(Specs.Mind_Magic, 24, 0.5f);
                break;

                case 7:
                SpecType = eSpecType.MatterSorc;
                Add(Specs.Matter_Magic, 46, 1.0f);
                Add(Specs.Body_Magic, 17, 0.1f);
                Add(Specs.Mind_Magic, 22, 0.2f);
                break;

                case 8:
                SpecType = eSpecType.MatterSorc;
                Add(Specs.Matter_Magic, 45, 1.0f);
                Add(Specs.Body_Magic, 17, 0.1f);
                Add(Specs.Mind_Magic, 24, 0.2f);
                break;
            }
        }
    }
}
