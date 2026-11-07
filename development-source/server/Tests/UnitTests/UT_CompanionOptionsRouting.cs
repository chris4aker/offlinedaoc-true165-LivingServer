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
