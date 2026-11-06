using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // 2026-11-06 world-bot list: gamebot world behavior and planning cost.
    [TestFixture, NonParallelizable]
    public class UT_WorldBotFixes20261106
    {
        [Test]
        public void AShoppingTripFollowsItsPlanToTheNewMerchant()
        {
            object heorot = new(), mif = new();
            // the plan moved on after Heorot Kenway was skipped
            Assert.That(AutonomousWorldBotController.ShopTripMovedOn(eWorldServiceKind.Shop, heorot, mif), Is.SameAs(mif));
            // same merchant, no plan, no current merchant, or not a shopping trip: nothing changes
            Assert.That(AutonomousWorldBotController.ShopTripMovedOn(eWorldServiceKind.Shop, heorot, heorot), Is.Null);
            Assert.That(AutonomousWorldBotController.ShopTripMovedOn<object>(eWorldServiceKind.Shop, heorot, null), Is.Null);
            Assert.That(AutonomousWorldBotController.ShopTripMovedOn(eWorldServiceKind.Shop, null, mif), Is.Null);
            Assert.That(AutonomousWorldBotController.ShopTripMovedOn(eWorldServiceKind.Vendor, heorot, mif), Is.Null);
        }

        [Test]
        public void AFarMerchantIsWalkedToAndSearchedOnlyWhenNearOrStalled()
        {
            var far = new AutonomousServiceReachability.FarApproach();
            // near: always searched (the failure cache limits repeats)
            Assert.That(far.ShouldSearch("bline", 700, 0), Is.True);
            // far and getting closer: just walk
            Assert.That(far.ShouldSearch("bline", 5_000, 0), Is.False);
            Assert.That(far.ShouldSearch("bline", 4_400, 20_000), Is.False);
            Assert.That(far.ShouldSearch("bline", 4_300, 40_000), Is.False);
            // no progress for 25 s: one search, then wait again
            Assert.That(far.ShouldSearch("bline", 4_300, 45_000), Is.True);
            Assert.That(far.ShouldSearch("bline", 4_300, 46_000), Is.False);
            Assert.That(far.ShouldSearch("bline", 4_300, 70_000), Is.True);
            // another merchant starts over
            Assert.That(far.ShouldSearch("feren", 4_300, 71_000), Is.False);
        }

        [Test]
        public void AFailedServiceSearchIsKeptForTheServiceWhereverTheBotStands()
        {
            Vector3 merchant = new(507_581, 477_378, 0), there = new(503_000, 470_000, 0), moved = new(504_000, 471_000, 0);
            long until = 1_000 + AutonomousWorldBotController.ServiceApproachFailureMilliseconds;
            Assert.That(AutonomousWorldBotController.FailedApproachCached("service:x", merchant, there, until, "service:x", merchant, moved, 20_000), Is.True);
            Assert.That(AutonomousWorldBotController.FailedApproachCached("service:x", merchant, there, until, "service:y", merchant, moved, 20_000), Is.False);
            Assert.That(AutonomousWorldBotController.FailedApproachCached("service:x", merchant, there, until, "service:x", merchant, moved, until), Is.False);
            // the old 3 s cache without a key still needs the same spot
            Assert.That(AutonomousWorldBotController.FailedApproachCached(null, merchant, there, 4_000, null, merchant, there, 2_000), Is.True);
            Assert.That(AutonomousWorldBotController.FailedApproachCached(null, merchant, there, 4_000, null, merchant, moved, 2_000), Is.False);
            Assert.That(AutonomousWorldBotController.FailedApproachCached("service:x", merchant, there, until, null, merchant, there, 2_000), Is.False);
        }
    }
}
