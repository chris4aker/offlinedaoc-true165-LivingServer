using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // 2026-11-07 (player report): a level 1-4 Viking trained Sword but got no styles - starter classes have none of their own.
    // They now use the weapon line's styles of their advanced classes (Specialization.AdvancedClassIds).
    [TestFixture]
    public class UT_StarterClassStyles
    {
        [Test]
        public void StarterClassesKnowTheirAdvancedClasses() => Assert.Multiple(() =>
        {
            Assert.That(Specialization.AdvancedClassIds((int)eCharacterClass.Viking),
                Does.Contain((int)eCharacterClass.Warrior).And.Contain((int)eCharacterClass.Berserker)
                    .And.Contain((int)eCharacterClass.Thane).And.Contain((int)eCharacterClass.Skald));
            Assert.That(Specialization.AdvancedClassIds((int)eCharacterClass.Fighter),
                Does.Contain((int)eCharacterClass.Armsman).And.Contain((int)eCharacterClass.Mercenary)
                    .And.Contain((int)eCharacterClass.Paladin));
            Assert.That(Specialization.AdvancedClassIds((int)eCharacterClass.Guardian),
                Does.Contain((int)eCharacterClass.Hero).And.Contain((int)eCharacterClass.Champion)
                    .And.Contain((int)eCharacterClass.Blademaster));
            Assert.That(Specialization.AdvancedClassIds((int)eCharacterClass.Warrior), Is.Empty, "an advanced class has none");
        });
    }
}
