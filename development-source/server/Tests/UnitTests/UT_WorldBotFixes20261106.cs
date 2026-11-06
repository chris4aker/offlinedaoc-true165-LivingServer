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
    }
}
