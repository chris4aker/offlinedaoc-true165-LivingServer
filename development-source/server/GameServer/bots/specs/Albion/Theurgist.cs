namespace DOL.GS
{
    public class TheurgistBotSpec : BotSpec
    {
        public TheurgistBotSpec(eSpecType spec)
        {
            SpecName = "TheurgistBotSpec";

            WeaponOneType = eObjectType.Staff;
            Is2H = true;

            // Air is about half of period Theurgists (family weights in BotSpec).
            var randVariance = spec switch
            {
                eSpecType.AirTheur => 0,
                eSpecType.IceTheur => Util.Random(1, 2),
                eSpecType.EarthTheur => 3,
                _ => Pick(2, 1, 0, 1),
            };

            switch (randVariance)
            {
                case 0:
                SpecType = eSpecType.AirTheur;
                Add(Specs.Earth_Magic, 28, 0.1f);
                Add(Specs.Cold_Magic, 20, 0.0f);
                Add(Specs.Wind_Magic, 45, 1.0f);
                break;

                // Ice with Earth second (period: Ice 50 / Earth 26 / rest Air; 1494 points fit 50/20 or 46/26/10).
                case 1:
                SpecType = eSpecType.IceTheur;
                Add(Specs.Earth_Magic, 20, 0.1f);
                Add(Specs.Cold_Magic, 50, 1.0f);
                Add(Specs.Wind_Magic, 4, 0.0f);
                break;

                case 2:
                SpecType = eSpecType.IceTheur;
                Add(Specs.Earth_Magic, 26, 0.1f);
                Add(Specs.Cold_Magic, 46, 1.0f);
                Add(Specs.Wind_Magic, 12, 0.0f);
                break;

                case 3:
                SpecType = eSpecType.EarthTheur;
                Add(Specs.Earth_Magic, 50, 1.0f);
                Add(Specs.Cold_Magic, 4, 0.0f);
                Add(Specs.Wind_Magic, 20, 0.1f);
                break;
            }
        }
    }
}
