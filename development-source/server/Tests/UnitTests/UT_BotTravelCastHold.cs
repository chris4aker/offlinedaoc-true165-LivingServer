using NUnit.Framework;
using static DOL.GS.BotTravelCastHold;

namespace DOL.GS.Tests
{
    /// <summary>Owner 2026-11-10: an autonomous party holds out-of-combat upkeep while it walks, never in a fight.</summary>
    [TestFixture]
    public sealed class UT_BotTravelCastHold
    {
        [Test]
        public void AWalkingPartyHoldsItsUpkeep() => Assert.Multiple(() =>
        {
            Assert.That(Holds(true, false, true, false, 0), Is.True, "leader or bot walking");
            Assert.That(Holds(true, false, true, true, 0), Is.True, "walking even if someone sits");
            Assert.That(Holds(true, false, false, false, 500), Is.True, "a short pause between legs");
            Assert.That(Holds(true, false, false, false, SettleMilliseconds - 1), Is.True);
        });

        [Test]
        public void APauseBetweenLegsOutlastsTheSlowestTravelThink() =>
            // review 2026-11-10: thinks come up to 5 s (+1/8 stagger) apart at 1,500 bots; the settle must be longer
            Assert.That(SettleMilliseconds, Is.GreaterThan(5_000 + 5_000 / 8));

        [Test]
        public void AStandingPartyBuffs() => Assert.Multiple(() =>
        {
            Assert.That(Holds(true, false, false, false, SettleMilliseconds), Is.False, "settled after the leader's last step");
            Assert.That(Holds(true, false, false, false, -1), Is.False, "never walked");
            Assert.That(Holds(true, false, false, true, 500), Is.False, "sitting or resting: a known stop, no settle wait");
        });

        [Test]
        public void FightsAndPlayerPartiesAreNeverHeld() => Assert.Multiple(() =>
        {
            Assert.That(Holds(true, true, true, false, 0), Is.False, "anyone fighting: heals and combat casts go on");
            Assert.That(Holds(false, false, true, false, 0), Is.False, "a player's party keeps CompanionFollowPolicy");
        });
    }
}
