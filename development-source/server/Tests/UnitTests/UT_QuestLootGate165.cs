using DOL.Database;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // True 1.65 Mode (owner 2026-11-06): a classic quest's item drops only for a player on a quest that uses it.
    [TestFixture]
    public class UT_QuestLootGate165
    {
        private bool _mode;

        [SetUp]
        public void Save() => _mode = Properties.CLASSIC165_MODE;

        [TearDown]
        public void Restore() => Properties.CLASSIC165_MODE = _mode;

        [Test]
        public void ClassicQuestItemsAreRecognised()
        {
            Assert.That(QuestLootGate165.IsQuestItem(new DbItemTemplate { Id_nb = "cq_golden_chain", PackageID = "ClassicQuest" }), Is.True);
            Assert.That(QuestLootGate165.IsQuestItem(new DbItemTemplate { Id_nb = "cq_rat_tail" }), Is.True);
            Assert.That(QuestLootGate165.IsQuestItem(new DbItemTemplate { Id_nb = "Golden_Chain_Necklace", PackageID = "AtlasXPItem" }), Is.False);
            Assert.That(QuestLootGate165.IsQuestItem(null), Is.False);
        }

        [Test]
        public void OrdinaryLootAlwaysDrops()
        {
            Properties.CLASSIC165_MODE = true;
            Assert.That(QuestLootGate165.MayDrop(new DbItemTemplate { Id_nb = "boar_tusk" }, []), Is.True);
        }

        [Test]
        public void NormalModeLeavesQuestLootAlone()
        {
            Properties.CLASSIC165_MODE = false;
            Assert.That(QuestLootGate165.MayDrop(new DbItemTemplate { Id_nb = "cq_golden_chain", PackageID = "ClassicQuest" }, []), Is.True);
        }
    }
}
