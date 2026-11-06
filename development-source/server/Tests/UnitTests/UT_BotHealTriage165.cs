using NUnit.Framework;
using static DOL.GS.BotHealTriage165;

namespace DOL.GS.Tests
{
    /// <summary>True 1.65 heal triage (Prima: two-thirds health, minor heals; period forums: no group heal on the pull).</summary>
    [TestFixture]
    public sealed class UT_BotHealTriage165
    {
        [Test]
        public void HealersStartAtTwoThirdsUnlessThePlayerChoseOtherwise() => Assert.Multiple(() =>
        {
            Assert.That(HealerNeedAt(0, true), Is.EqualTo(65), "True 1.65 default");
            Assert.That(HealerNeedAt(0, false), Is.EqualTo(80), "normal mode keeps 80%");
            Assert.That(HealerNeedAt(1, true), Is.EqualTo(90), "the player's Early choice wins");
            Assert.That(HealerNeedAt(2, true), Is.EqualTo(50), "the player's Late choice wins");
        });

        [Test]
        public void AGroupHealNeedsThreeHurtAndNotOnThePull() => Assert.Multiple(() =>
        {
            Assert.That(GroupHealWorthwhile(2, true, 10_000), Is.False, "two hurt: single heals");
            Assert.That(GroupHealWorthwhile(3, true, 1_000), Is.False, "the pull: a group heal draws every monster");
            Assert.That(GroupHealWorthwhile(3, true, GroupHealFightMilliseconds), Is.True);
            Assert.That(GroupHealWorthwhile(3, false, 0), Is.True, "topping up after the fight");
        });
    }
}
