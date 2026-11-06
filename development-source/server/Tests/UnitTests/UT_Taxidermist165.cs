using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // True 1.65 Mode: the market taxidermist cleanses Daemon Diamond Seals (owner 2026-11-06).
    [TestFixture]
    public class UT_Taxidermist165
    {
        [Test]
        public void OnlyDaemonDiamondSealsAreTaken()
        {
            Assert.That(Taxidermist165.IsSeal(new DbInventoryItem { Id_nb = "DiamondSeal" }), Is.True);
            Assert.That(Taxidermist165.IsSeal(new DbInventoryItem { Id_nb = "diamondseal" }), Is.True);
            Assert.That(Taxidermist165.IsSeal(new DbInventoryItem { Id_nb = "cleansed_diamond_seal" }), Is.False);
            Assert.That(Taxidermist165.IsSeal(null), Is.False);
        }
    }
}
