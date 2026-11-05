using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // 2026-11-05: low Hibernian bots died 5,690 times at one spot south of Mag Mell on their way to a merchant.
    [TestFixture]
    public class UT_AutonomousRoadHotspots
    {
        [SetUp] public void Reset() => AutonomousRoadHotspots.Clear();

        [Test]
        public void FourBotsKilledByMuchStrongerMonstersMakeAHotspot()
        {
            for (int bot = 1; bot <= 3; bot++)
                Assert.That(AutonomousRoadHotspots.RecordDeath(200, 345_000 + bot * 100, 505_000, bot, 4, 9, 1_000, out _), Is.Null);
            // a death to an even monster does not count
            Assert.That(AutonomousRoadHotspots.RecordDeath(200, 345_000, 505_000, 9, 8, 9, 1_000, out _), Is.Null);
            Assert.That(AutonomousRoadHotspots.RecordDeath(200, 345_200, 505_100, 4, 5, 9, 2_000, out bool created), Is.Not.Null);
            Assert.That(created, Is.True);
        }

        [Test]
        public void LowBotsAvoidTheSpotAndTheWayPastItHigherOnesDoNot()
        {
            for (int bot = 1; bot <= 4; bot++)
                AutonomousRoadHotspots.RecordDeath(200, 345_000, 505_000, bot, 4, 9, 1_000, out _);
            // a merchant beside it
            Assert.That(AutonomousRoadHotspots.Blocks(200, 345_000, 560_000, 5, 200, 345_500, 505_500, 2_000), Is.True);
            // straight past it (Mag Mell north of it, Elith south of it)
            Assert.That(AutonomousRoadHotspots.Blocks(200, 345_000, 560_000, 5, 200, 344_400, 530_000, 2_000), Is.False);
            Assert.That(AutonomousRoadHotspots.Blocks(200, 345_000, 520_000, 5, 200, 344_400, 490_000, 2_000), Is.True);
            // a level 7 bot is past the danger (killers level 9)
            Assert.That(AutonomousRoadHotspots.Blocks(200, 345_000, 520_000, 7, 200, 344_400, 490_000, 2_000), Is.False);
            // standing inside it, the way out is allowed
            Assert.That(AutonomousRoadHotspots.Blocks(200, 345_000, 505_000, 5, 200, 345_000, 540_000, 2_000), Is.False);
            // another region, or expired
            Assert.That(AutonomousRoadHotspots.Blocks(201, 345_000, 520_000, 5, 201, 344_400, 490_000, 2_000), Is.False);
            Assert.That(AutonomousRoadHotspots.Blocks(200, 345_000, 520_000, 5, 200, 344_400, 490_000,
                1_000 + AutonomousRoadHotspots.ActiveMilliseconds + 1), Is.False);
        }
    }
}
