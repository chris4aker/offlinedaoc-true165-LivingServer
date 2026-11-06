using System.Linq;
using DOL.GS;
using NUnit.Framework;
using static DOL.GS.AutonomousRvrStealthers;

namespace DOL.UnitTests
{
    /// <summary>Item 50: True 1.65 stealthers hunt alone or in pairs, hidden, from the period posts, easy kills first.</summary>
    [TestFixture]
    public class UT_RvrStealthers165
    {
        [TestCase(eCharacterClass.Infiltrator)]
        [TestCase(eCharacterClass.Shadowblade)]
        [TestCase(eCharacterClass.Nightshade)]
        public void EveryAssassinHuntsAlone(eCharacterClass characterClass) =>
            Assert.That(Enumerable.Range(1, 200).All(id => PrefersSolo(characterClass, id)), Is.True);

        [TestCase(eCharacterClass.Scout)]
        [TestCase(eCharacterClass.Hunter)]
        [TestCase(eCharacterClass.Ranger)]
        public void MostArchersHuntAloneSomeGroup(eCharacterClass characterClass)
        {
            int solo = Enumerable.Range(1, 2000).Count(id => PrefersSolo(characterClass, id));
            Assert.That(solo, Is.InRange(1350, 1650), "about three in four");
        }

        [TestCase(eCharacterClass.Minstrel)]
        [TestCase(eCharacterClass.Armsman)]
        [TestCase(eCharacterClass.Cleric)]
        [TestCase(eCharacterClass.AlbionRogue)]
        public void OthersGroupAsBefore(eCharacterClass characterClass) =>
            Assert.That(Enumerable.Range(1, 200).Any(id => PrefersSolo(characterClass, id)), Is.False);

        [Test]
        public void SomeStealthersPairUp()
        {
            int duos = Enumerable.Range(1, 2000).Count(id => FormsDuo(id));
            Assert.That(duos, Is.InRange(300, 500), "about one in five");
        }

        [Test]
        public void TravelsHiddenOnlyInTheFrontierWhenFreeToHide()
        {
            Assert.That(TravelsHidden(true, true, true, true, false, false, false, 1), Is.True);
            Assert.That(TravelsHidden(true, true, true, true, false, false, false, 2), Is.True, "a pair hides too");
            Assert.That(TravelsHidden(false, true, true, true, false, false, false, 1), Is.False, "normal mode unchanged");
            Assert.That(TravelsHidden(true, false, true, true, false, false, false, 1), Is.False, "not a stealther");
            Assert.That(TravelsHidden(true, true, false, true, false, false, false, 1), Is.False, "no stealth spec yet");
            Assert.That(TravelsHidden(true, true, true, false, false, false, false, 1), Is.False, "outside the frontier");
            Assert.That(TravelsHidden(true, true, true, true, true, false, false, 1), Is.False, "in a safe area");
            Assert.That(TravelsHidden(true, true, true, true, false, true, false, 1), Is.False, "fighting or casting");
            Assert.That(TravelsHidden(true, true, true, true, false, false, true, 1), Is.False, "on a horse");
            Assert.That(TravelsHidden(true, true, true, true, false, false, false, 3), Is.False, "in a warband");
        }

        [Test]
        public void EasyKillsFirst()
        {
            Assert.That(Vulnerability(true, true, 100, 100), Is.EqualTo(0), "sitting");
            Assert.That(Vulnerability(false, true, 20, 100), Is.EqualTo(1), "a caster out of power");
            Assert.That(Vulnerability(false, false, 0, 100), Is.EqualTo(3), "no power bar is not low power");
            Assert.That(Vulnerability(false, true, 80, 40), Is.EqualTo(2), "hurt");
            Assert.That(Vulnerability(false, true, 80, 90), Is.EqualTo(3));
        }

        [Test]
        public void HuntingPostsOutweighOrdinaryRoaming() =>
            Assert.That(PostWeight, Is.GreaterThan(AutonomousFrontierHotspots.RoamWeight));
    }
}
