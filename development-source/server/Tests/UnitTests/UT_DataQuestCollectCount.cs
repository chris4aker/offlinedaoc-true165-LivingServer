using System.Collections.Generic;
using System.Linq;
using DOL.GS.Quests;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// A hand-in step takes every copy of its item the quest handed out (tester 0.5: "Learn the Hunt" took one of the
    /// five bear claws). The rows are the True 1.65 world's.
    /// </summary>
    [TestFixture]
    public class UT_DataQuestCollectCount
    {
        private static int Needed(string types, string stepItems, string collectItems, int step)
        {
            List<DataQuest.eStepType> t = types.Split('|').Select(x => (DataQuest.eStepType)byte.Parse(x)).ToList();
            return DataQuest.CollectCountNeeded(t, stepItems.Split('|'), collectItems.Split('|'), step);
        }

        [Test]
        public void FiveKillStepsThatEachHandOutAClawNeedFive()
        {
            // 20096 Learn the Hunt: "Return to Aegan with all five bear claws."
            Assert.That(Needed("0|0|0|0|0|11", "cq_bear_claw|cq_bear_claw|cq_bear_claw|cq_bear_claw|cq_bear_claw|", "|||||cq_bear_claw", 6), Is.EqualTo(5));
        }

        [Test]
        public void ADropChanceOnTheTemplateIsNotPartOfItsName()
        {
            // 20010 Lost Potion: two damp skeleton bones, then a delivery of its own
            const string types = "4|0|0|10|3";
            const string items = "|cq_damp_skeleton_bone;50|cq_damp_skeleton_bone;50||cq_potion_of_healing";
            const string collect = "|||cq_damp_skeleton_bone|cq_potion_of_healing";
            Assert.That(Needed(types, items, collect, 4), Is.EqualTo(2));
            Assert.That(Needed(types, items, collect, 5), Is.EqualTo(1));
        }

        [Test]
        public void ALaterHandInCountsOnlyItsOwnItem()
        {
            // 20645 Evening the Odds: three vials of sea slime, then one of fly venom, then one of mongrel saliva
            const string types = "0|0|0|10|0|10|0|11";
            const string items = "cq_vial_of_sea_slime;40|cq_vial_of_sea_slime;40|cq_vial_of_sea_slime;40||cq_vial_of_inishail_fly_venom;40||cq_vial_of_tidal_mongrel_saliva;40|";
            const string collect = "|||cq_vial_of_sea_slime||cq_vial_of_inishail_fly_venom||cq_vial_of_tidal_mongrel_saliva";
            Assert.That(Needed(types, items, collect, 4), Is.EqualTo(3));
            Assert.That(Needed(types, items, collect, 6), Is.EqualTo(1));
            Assert.That(Needed(types, items, collect, 8), Is.EqualTo(1));
        }

        [Test]
        public void ADeliveryCountsTheCopyHandedOutAsItBegins()
        {
            // 21332 Price of Courage: "Take the three goblin heads to Commander Jusen" - two kills hand one out each and
            // the delivery step hands out the third as it begins; the token after it is a plain one-item delivery
            const string types = "4|4|0|0|0|2|2|4|4|9";
            const string items = "||cq_goblin_head|cq_goblin_head||cq_goblin_head|cq_king_s_token|||";
            const string collect = "|||||cq_goblin_head|cq_king_s_token|||";
            Assert.That(Needed(types, items, collect, 6), Is.EqualTo(3));
            Assert.That(Needed(types, items, collect, 7), Is.EqualTo(1));
        }

        [Test]
        public void TheSameItemHandedInTwiceCountsFromTheLastHandIn()
        {
            Assert.That(Needed("0|0|10|0|11", "cq_x|cq_x||cq_x|", "||cq_x||cq_x", 3), Is.EqualTo(2));
            Assert.That(Needed("0|0|10|0|11", "cq_x|cq_x||cq_x|", "||cq_x||cq_x", 5), Is.EqualTo(1));
        }

        [Test]
        public void AnItemFromLootOrAStepWithNoHandInNeedsOne()
        {
            Assert.That(Needed("4|11", "|", "|cq_wolf_pelt", 2), Is.EqualTo(1));        // the item comes from loot
            Assert.That(Needed("4|11", "|", "|cq_wolf_pelt", 1), Is.EqualTo(1));        // not a hand-in step
            Assert.That(Needed("4|11", "|", "|cq_wolf_pelt", 9), Is.EqualTo(1));        // past the end
            Assert.That(DataQuest.CollectCountNeeded(new List<DataQuest.eStepType>(), new[] { "" }, new[] { "Coral" }, 1), Is.EqualTo(1));   // a collection quest
        }
    }
}
