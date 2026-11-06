namespace DOL.GS
{
    public class ShamanBotSpec : BotSpec
    {
        public ShamanBotSpec(eSpecType spec)
        {
            SpecName = "ShamanBotSpec";

            WeaponOneType = eObjectType.Hammer;
            Is2H = Util.Random(1) == 0;

            // Period Shamans trained Augmentation or Subterranean (Cave) first with a little Mending; a Mending-first
            // build was not a period template, so MendShaman is no longer rolled (saved plans keep theirs).
            var randVariance = spec switch
            {
                eSpecType.AugShaman => Util.Random(0, 3),
                eSpecType.SubtShaman or eSpecType.MendShaman => Util.Random(4, 7),
                _ => Util.Random(7),
            };

            switch (randVariance)
            {
                case 0:
                case 1:
                SpecType = eSpecType.AugShaman;
                Add(Specs.Mending, 8, 0.2f);
                Add(Specs.Augmentation, 46, 0.8f);
                Add(Specs.Subterranean, 27, 0.5f);
                break;

                // 43 Aug / 27-31 Cave / the rest Mending.
                case 2:
                SpecType = eSpecType.AugShaman;
                Add(Specs.Mending, 14, 0.2f);
                Add(Specs.Augmentation, 43, 0.8f);
                Add(Specs.Subterranean, 27, 0.5f);
                break;

                case 3:
                SpecType = eSpecType.AugShaman;
                Add(Specs.Mending, 9, 0.2f);
                Add(Specs.Augmentation, 43, 0.8f);
                Add(Specs.Subterranean, 31, 0.5f);
                break;

                // 37 Aug / 39 Cave / 4 Mending.
                case 4:
                SpecType = eSpecType.SubtShaman;
                Add(Specs.Mending, 4, 0.1f);
                Add(Specs.Augmentation, 37, 0.75f);
                Add(Specs.Subterranean, 39, 0.8f);
                break;

                case 5:
                SpecType = eSpecType.SubtShaman;
                Add(Specs.Mending, 4, 0.2f);
                Add(Specs.Augmentation, 28, 0.5f);
                Add(Specs.Subterranean, 46, 0.8f);
                break;

                // 41 Cave / 32 Aug / 14 Mending.
                case 6:
                SpecType = eSpecType.SubtShaman;
                Add(Specs.Mending, 14, 0.2f);
                Add(Specs.Augmentation, 32, 0.6f);
                Add(Specs.Subterranean, 41, 0.8f);
                break;

                // 46 Cave / 22 Aug, the rest Mending at 50.
                case 7:
                SpecType = eSpecType.SubtShaman;
                Add(Specs.Mending, 17, 0.0f);
                Add(Specs.Augmentation, 22, 0.5f);
                Add(Specs.Subterranean, 46, 0.8f);
                break;
            }
        }
    }
}
