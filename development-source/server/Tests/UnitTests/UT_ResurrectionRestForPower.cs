using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // owner 2026-11-10: a resurrector short only of power sits for it; the corpse it sits for waits 120 s
    public class UT_ResurrectionRestForPower
    {
        [Test]
        public void ShortOfPowerOutOfCombatRests() =>
            Assert.That(BotGroupSupport.ShouldRestForResurrection(true, false, false, false, false, 11, 75, 37), Is.True);

        [TestCase(false, false, false, false, false, 11, 75, 37)] // no corpse in range
        [TestCase(true, true, false, false, false, 11, 75, 37)]   // party fighting
        [TestCase(true, false, true, false, false, 11, 75, 37)]   // casting
        [TestCase(true, false, false, true, false, 11, 75, 37)]   // interrupted or controlled
        [TestCase(true, false, false, false, true, 11, 75, 37)]   // spell on recast
        [TestCase(true, false, false, false, false, 40, 75, 37)]  // already has the power
        [TestCase(true, false, false, false, false, 11, 75, 90)]  // the cost is beyond its pool
        public void OtherwiseDoesNotRest(bool corpse, bool combat, bool casting, bool interrupted, bool recast,
            int mana, int maxMana, int required) =>
            Assert.That(BotGroupSupport.ShouldRestForResurrection(corpse, combat, casting, interrupted, recast, mana, maxMana,
                required), Is.False);

        [Test]
        public void PowerCostIsTheReservationCostOrTheSpellCost()
        {
            Assert.That(BotGroupSupport.ResurrectionPowerCost(75, 10, 10, 18), Is.EqualTo(37));
            Assert.That(BotGroupSupport.ResurrectionPowerCost(75, 10, 5, 18), Is.EqualTo(18));
            Assert.That(BotGroupSupport.ResurrectionPowerCost(100, 10, 15, 0), Is.EqualTo(100));
            Assert.That(BotGroupSupport.ResurrectionPowerCost(100, 20, 1, 0), Is.EqualTo(10));
        }

        [Test]
        public void ReleaseWaitDoublesWhileAResurrectorRestsForIt()
        {
            Assert.That(BotGroupSupport.ResurrectionReleaseWait(false), Is.EqualTo(60_000));
            Assert.That(BotGroupSupport.ResurrectionReleaseWait(true), Is.EqualTo(120_000));
        }

        [Test]
        public void RestingClaimLapsesWithoutRefresh()
        {
            long lapse = BotGroupSupport.RestingForResurrectionMilliseconds;
            Assert.That(BotGroupSupport.IsRestingClaimCurrent(1000, 1000), Is.True);
            Assert.That(BotGroupSupport.IsRestingClaimCurrent(1000, 1000 + lapse - 1), Is.True);
            Assert.That(BotGroupSupport.IsRestingClaimCurrent(1000, 1000 + lapse), Is.False);
            Assert.That(BotGroupSupport.IsRestingClaimCurrent(0, 5000), Is.False);
        }
    }
}
