namespace DOL.GS
{
    public class SpiritmasterBotSpec : BotSpec
    {
        public SpiritmasterBotSpec(eSpecType spec)
        {
            SpecName = "SpiritmasterBotSpec";

            WeaponOneType = eObjectType.Staff;

            var randVariance = spec switch
            {
                eSpecType.DarkSpirit => Util.Random(0, 3),
                eSpecType.SuppSpirit => Util.Random(4, 7),
                eSpecType.SummSpirit => Util.Random(8, 9),
                _ => Util.Random(9),
            };

            switch (randVariance)
            {
                case 0:
                case 1:
                SpecType = eSpecType.DarkSpirit;
                Add(Specs.Darkness, 47, 1.0f);
                Add(Specs.Suppression, 5, 0.0f);
                Add(Specs.Summoning, 26, 0.1f);
                break;

                // Suppression early (about 0.6 of the level until 26) for its shields; Darkness keeps most points.
                case 2:
                case 3:
                SpecType = eSpecType.DarkSpirit;
                Add(Specs.Darkness, 47, 0.85f);
                Add(Specs.Suppression, 26, 0.6f);
                Add(Specs.Summoning, 6, 0.0f);
                break;

                // Suppression is the main line (it had ratio 0, so it trained nothing before 50).
                case 4:
                case 5:
                SpecType = eSpecType.SuppSpirit;
                Add(Specs.Darkness, 5, 0.0f);
                Add(Specs.Suppression, 49, 1.0f);
                Add(Specs.Summoning, 22, 0.1f);
                break;

                case 6:
                SpecType = eSpecType.SuppSpirit;
                Add(Specs.Darkness, 35, 0.1f);
                Add(Specs.Suppression, 41, 1.0f);
                Add(Specs.Summoning, 3, 0.0f);
                break;

                // Suppression first, Summoning second (it was the other way round).
                case 7:
                SpecType = eSpecType.SuppSpirit;
                Add(Specs.Darkness, 12, 0.0f);
                Add(Specs.Suppression, 43, 1.0f);
                Add(Specs.Summoning, 30, 0.1f);
                break;

                case 8:
                SpecType = eSpecType.SummSpirit;
                Add(Specs.Darkness, 24, 0.1f);
                Add(Specs.Suppression, 6, 0.0f);
                Add(Specs.Summoning, 48, 1.0f);
                break;

                case 9:
                SpecType = eSpecType.SummSpirit;
                Add(Specs.Darkness, 28, 0.1f);
                Add(Specs.Suppression, 10, 0.0f);
                Add(Specs.Summoning, 45, 1.0f);
                break;
            }
        }
    }
}
