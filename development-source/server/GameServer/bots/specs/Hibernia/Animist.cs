namespace DOL.GS
{
    public class AnimistBotSpec : BotSpec
    {
        public AnimistBotSpec(eSpecType spec)
        {
            SpecName = "AnimistBotSpec";
            WeaponOneType = eObjectType.Staff;
            Is2H = true;

            SpecType = spec switch
            {
                eSpecType.ArborealAnimist => eSpecType.ArborealAnimist,
                eSpecType.VerdantAnimist => eSpecType.VerdantAnimist,
                eSpecType.CreepingAnimist => eSpecType.CreepingAnimist,
                _ => BotSpec.ChooseRandomSpecialization(eCharacterClass.Animist),
            };

            switch (SpecType)
            {
                // Period Animists (about 85%): Arboreal 50 / Creeping 19 / Verdant 7, the Verdant points early (by the
                // 30s) and Creeping with the rest at 50. Arboreal at 0.9 leaves room for them while levelling.
                case eSpecType.ArborealAnimist:
                    Add(Specs.Arboreal_Path, 50, 0.9f);
                    Add(Specs.Creeping_Path, 19, 0.0f);
                    Add(Specs.Verdant_Path, 7, 0.2f);
                    break;

                case eSpecType.VerdantAnimist:
                    Add(Specs.Arboreal_Path, 12, 0.1f);
                    Add(Specs.Creeping_Path, 22, 0.2f);
                    Add(Specs.Verdant_Path, 49, 1.0f);
                    break;

                default:
                    Add(Specs.Arboreal_Path, 12, 0.1f);
                    Add(Specs.Creeping_Path, 49, 1.0f);
                    Add(Specs.Verdant_Path, 22, 0.2f);
                    break;
            }
        }
    }
}
