using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // 2026-11-05: the AI Options "option" verb was not routed, so every generic row answered "Unknown hiring command".
    [TestFixture]
    public class UT_CompanionOptionsRouting
    {
        [Test]
        public void EveryAiOptionsVerbReachesTheCompanionWindows()
        {
            foreach (string verb in CompanionOptions165.Verbs)
                Assert.That(CompanionWindows165.Verbs, Does.Contain(verb), verb);
            Assert.That(CompanionWindows165.Verbs, Does.Contain("option"));
        }

        [Test]
        public void EveryGenericOptionKindIsKnown()
        {
            for (byte kind = 5; kind <= CompanionOptions165.KindPositioning; kind++)
                Assert.That(CompanionOptions165.Option(kind), Is.Not.Null, $"kind {kind}");
            Assert.That(CompanionOptions165.Option(CompanionOptions165.KindPositioning + 1), Is.Null);
        }

        // owner 2026-11-06: charming starts off for a hired Hunter only (it keeps its wolf); on for every other charm class
        [Test]
        public void CharmDefaultsOffForHuntersOnly() => Assert.Multiple(() =>
        {
            Assert.That(CompanionOptions165.CharmAllowed(null, true), Is.False, "a Hunter never set: off (its wolf)");
            Assert.That(CompanionOptions165.CharmAllowed("1", true), Is.True, "a Hunter switched on");
            Assert.That(CompanionOptions165.CharmAllowed("0", true), Is.False);
            Assert.That(CompanionOptions165.CharmAllowed(null, false), Is.True, "a Sorcerer never set: on");
            Assert.That(CompanionOptions165.CharmAllowed("0", false), Is.False, "switched off");
            Assert.That(CompanionOptions165.CharmAllowed("1", false), Is.True);
        });

        // owner 2026-11-06: the pre-fight charm only takes a monster nobody has started on
        [Test]
        public void PreFightCharmNeedsAnUntouchedMonster() => Assert.Multiple(() =>
        {
            Assert.That(Charm165.Untouched(100, false, false), Is.True);
            Assert.That(Charm165.Untouched(99, false, false), Is.False, "already hurt");
            Assert.That(Charm165.Untouched(100, true, false), Is.False, "already fighting");
            Assert.That(Charm165.Untouched(100, false, true), Is.False, "someone is hitting it");
        });

        // owner 2026-11-06: a companion's realm ability tooltip ends with its own level and the next cost
        [Test]
        public void RealmAbilityDelveStatusLine()
        {
            Assert.That(CompanionWindows165.RealmAbilityStatus("Isolde", 1, 3, 3, 5),
                Is.EqualTo("Isolde has it at level 1 of 3. Level 2 costs 3 realm ability points; Isolde has 5."));
            Assert.That(CompanionWindows165.RealmAbilityStatus("Isolde", 0, 5, 1, 0),
                Does.Contain("level 0 of 5").And.Contain("Level 1 costs 1"));
            Assert.That(CompanionWindows165.RealmAbilityStatus("Isolde", 3, 3, 0, 9),
                Is.EqualTo("Isolde has it at level 3 of 3 (the highest)."));
        }
    }
}
