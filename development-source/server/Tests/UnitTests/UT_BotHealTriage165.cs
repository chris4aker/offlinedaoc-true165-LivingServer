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

        [Test]
        public void TheTankComesFirstOutsideEmergencies() => Assert.Multiple(() =>
        {
            Assert.That(TriageHealth(60, true), Is.EqualTo(50));
            Assert.That(TriageHealth(60, false), Is.EqualTo(60));
            Assert.That(Prefer(58, true, 52, false), Is.True, "the tank at 58% before a caster at 52%");
            Assert.That(Prefer(52, false, 58, true), Is.False);
            Assert.That(Prefer(70, true, 52, false), Is.False, "not over someone much lower");
            Assert.That(Prefer(45, true, 39, false), Is.False, "an emergency stays first");
            Assert.That(TriageHealth(45, true), Is.EqualTo(EmergencyPercent), "a tank is never pushed into an emergency");
            Assert.That(TriageHealth(30, true), Is.EqualTo(30), "an emergency is triaged on real health");
            Assert.That(TriageHealth(70, true) < HealAtPercent, Is.True, "a tank at 70% already needs a heal");
        });
    }
}
