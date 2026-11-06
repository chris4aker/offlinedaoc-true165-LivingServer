using DOL.GS.Keeps;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Item 55: boiling oil is not a True 1.65 keep hookpoint (2002-2003 posters still wished for it).</summary>
    [TestFixture]
    public class UT_BoilingOil165
    {
        [Test]
        public void BoilingOilIsHiddenOnlyInTrue165Mode()
        {
            Assert.That(HookPointInventory.OfferedInMode(HookPointInventory.BoilingOilType, true), Is.False);
            Assert.That(HookPointInventory.OfferedInMode(HookPointInventory.BoilingOilType, false), Is.True, "normal mode unchanged");
        }

        [TestCase("DOL.GS.Keeps.GuardFighter")]
        [TestCase("DOL.GS.GameSiegeTrebuchet")]
        [TestCase("DOL.GS.GameSiegeCatapult")]
        [TestCase("DOL.GS.GameSiegeBallista")]
        public void OtherHookpointItemsStay(string type) =>
            Assert.That(HookPointInventory.OfferedInMode(type, true), Is.True);
    }
}
