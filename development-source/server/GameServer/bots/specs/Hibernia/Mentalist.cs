namespace DOL.GS
{
    public class MentalistBotSpec : BotSpec
    {
        public MentalistBotSpec(eSpecType spec)
        {
            SpecName = "MentalistBotSpec";

            WeaponOneType = eObjectType.Staff;
            Is2H = true;

            // Period Mentalists: Light or Mana first (about 45% each), Mentalism first about 10% (family weights in BotSpec).
            var randVariance = spec switch
            {
                eSpecType.LightMenta => Util.Random(0, 4),
                eSpecType.ManaMenta => Util.Random(5, 9),
                eSpecType.MentaMenta => Util.Random(10, 13),
                _ => Pick(9, 9, 2) switch { 0 => Util.Random(0, 4), 1 => Util.Random(5, 9), _ => Util.Random(10, 13) },
            };

            // The main line is below 1.0 so the small lines train while levelling, as players did: Mana 4 (Holism, the
            // first power regeneration) by the 20s and Mentalism (heal, mez) about 10 at 20 and 15 by 30 when the build
            // takes it far. A 1.0 main line takes every point a 1x caster gets until it caps.
            switch (randVariance)
            {
                case 0:
                SpecType = eSpecType.LightMenta;
                Add(Specs.Light, 45, 0.8f);
                Add(Specs.Mentalism, 28, 0.6f);
                Add(Specs.Mana, 10, 0.15f);
                break;

                case 1:
                SpecType = eSpecType.LightMenta;
                Add(Specs.Light, 45, 0.8f);
                Add(Specs.Mentalism, 17, 0.6f);
                Add(Specs.Mana, 24, 0.15f);
                break;

                case 2:
                SpecType = eSpecType.LightMenta;
                Add(Specs.Light, 45, 0.8f);
                Add(Specs.Mentalism, 6, 0.2f);
                Add(Specs.Mana, 29, 0.15f);
                break;

                case 3:
                SpecType = eSpecType.LightMenta;
                Add(Specs.Light, 42, 0.8f);
                Add(Specs.Mentalism, 33, 0.6f);
                Add(Specs.Mana, 7, 0.15f);
                break;

                case 4:
                SpecType = eSpecType.LightMenta;
                Add(Specs.Light, 42, 0.8f);
                Add(Specs.Mentalism, 23, 0.6f);
                Add(Specs.Mana, 24, 0.15f);
                break;

                case 5:
                SpecType = eSpecType.ManaMenta;
                Add(Specs.Mana, 48, 0.8f);
                Add(Specs.Mentalism, 24, 0.6f);
                Add(Specs.Light, 6, 0.0f);
                break;

                case 6:
                SpecType = eSpecType.ManaMenta;
                Add(Specs.Mana, 48, 0.8f);
                Add(Specs.Mentalism, 6, 0.2f);
                Add(Specs.Light, 24, 0.1f);
                break;

                case 7:
                SpecType = eSpecType.ManaMenta;
                Add(Specs.Mana, 46, 0.8f);
                Add(Specs.Mentalism, 28, 0.6f);
                Add(Specs.Light, 4, 0.0f);
                break;

                case 8:
                SpecType = eSpecType.ManaMenta;
                Add(Specs.Mana, 44, 0.8f);
                Add(Specs.Mentalism, 31, 0.6f);
                Add(Specs.Light, 4, 0.0f);
                break;

                case 9:
                SpecType = eSpecType.ManaMenta;
                Add(Specs.Mana, 44, 0.8f);
                Add(Specs.Mentalism, 4, 0.2f);
                Add(Specs.Light, 31, 0.1f);
                break;

                case 10:
                SpecType = eSpecType.MentaMenta;
                Add(Specs.Mentalism, 50, 0.9f);
                Add(Specs.Mana, 4, 0.15f);
                Add(Specs.Light, 20, 0.1f);
                break;

                case 11:
                SpecType = eSpecType.MentaMenta;
                Add(Specs.Mentalism, 50, 0.9f);
                Add(Specs.Mana, 14, 0.15f);
                Add(Specs.Light, 14, 0.0f);
                break;

                case 12:
                SpecType = eSpecType.MentaMenta;
                Add(Specs.Mentalism, 42, 0.9f);
                Add(Specs.Mana, 7, 0.15f);
                Add(Specs.Light, 33, 0.1f);
                break;

                case 13:
                SpecType = eSpecType.MentaMenta;
                Add(Specs.Mentalism, 41, 0.9f);
                Add(Specs.Mana, 34, 0.15f);
                Add(Specs.Light, 8, 0.0f);
                break;
            }
        }
    }
}
