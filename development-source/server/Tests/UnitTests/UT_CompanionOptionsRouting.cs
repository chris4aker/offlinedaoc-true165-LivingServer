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
    }
}
