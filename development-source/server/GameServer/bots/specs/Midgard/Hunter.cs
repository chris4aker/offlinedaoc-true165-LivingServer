namespace DOL.GS
{
    public class HunterBotSpec : BotSpec
    {
        public HunterBotSpec(eSpecType spec = eSpecType.None)
        {
            SpecName = "HunterBotSpec";

            if (spec == eSpecType.SpearHunter)
            {
                // Spear first, the melee Hunter (owner 2026-10-29), on the same points as the others.
                WeaponOneType = eObjectType.Spear;
                Is2H = true;
                SpecType = eSpecType.None;
                Add(Specs.Spear, 50, 1.0f);
                Add(Specs.Beastcraft, 39, 0.7f);
                Add(Specs.Stealth, 35, 0.4f);
                Add(Specs.CompositeBow, 25, 0.2f);
                return;
            }

            // About 9 in 10 period Hunters used a spear: 8 in 10 here, plus the Spear build (always spear).
            switch (Pick(4, 1))
            {
                case 0: WeaponOneType = eObjectType.Spear; break;
                case 1: WeaponOneType = eObjectType.Sword; break;
            }

            Is2H = true;

            int randVariance = Util.Random(4);

            SpecType = eSpecType.None;

            switch (randVariance)
            {
                case 0:
                case 1:
                Add(ObjToSpec(WeaponOneType), 39, 0.8f);
                Add(Specs.CompositeBow, 35, 0.9f);
                Add(Specs.Beastcraft, 40, 0.6f);
                Add(Specs.Stealth, 38, 0.3f);
                break;

                case 2:
                case 3:
                Add(ObjToSpec(WeaponOneType), 39, 0.8f);
                Add(Specs.CompositeBow, 45, 0.9f);
                Add(Specs.Beastcraft, 32, 0.6f);
                Add(Specs.Stealth, 38, 0.3f);
                break;

                // Melee Hunter (period): weapon 44 / bow 35 / Beastcraft 32 / stealth 40.
                case 4:
                Add(ObjToSpec(WeaponOneType), 44, 0.9f);
                Add(Specs.CompositeBow, 35, 0.7f);
                Add(Specs.Beastcraft, 32, 0.6f);
                Add(Specs.Stealth, 40, 0.5f);
                break;
            }
        }
    }
}
