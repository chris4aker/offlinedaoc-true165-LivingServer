using System.Threading.Tasks;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Owner 2026-11-10: route spots that 3 solo bots gave a camp up for are shared for 30 minutes.</summary>
    [TestFixture]
    public class UT_RouteBlockMemory
    {
        private const long Minute = 60_000;

        [SetUp] public void Reset() => AutonomousRouteBlockMemory.Clear();
        [TearDown] public void Clean() => AutonomousRouteBlockMemory.Clear();

        [Test]
        public void TwoBotsMakeNoBlockThreeDo()
        {
            Assert.That(AutonomousRouteBlockMemory.RecordRetarget(200, 345_000, 505_000, 1, 10, 1_000, out _, out _), Is.Null);
            Assert.That(AutonomousRouteBlockMemory.RecordRetarget(200, 345_300, 505_000, 2, 11, 2_000, out _, out _), Is.Null);
            var block = AutonomousRouteBlockMemory.RecordRetarget(200, 345_000, 505_400, 3, 9, 3_000, out bool created, out int bots);
            Assert.That(block, Is.Not.Null);
            Assert.That(created, Is.True);
            Assert.That(bots, Is.EqualTo(3));
            Assert.That(block.MaxBotLevel, Is.EqualTo(11));
            Assert.That(AutonomousRouteBlockMemory.AnyActive(4_000), Is.True);
        }

        [Test]
        public void TheSameBotThreeTimesMakesNoBlock()
        {
            for (int i = 0; i < 3; i++)
                Assert.That(AutonomousRouteBlockMemory.RecordRetarget(200, 345_000, 505_000, 7, 10, 1_000 + i, out _, out _), Is.Null);
        }

        [Test]
        public void BotsTooFarApartMakeNoBlock()
        {
            for (int bot = 0; bot < 3; bot++)
                Assert.That(AutonomousRouteBlockMemory.RecordRetarget(200, 345_000 + bot * 1_000, 505_000, bot, 10, 1_000, out _, out _), Is.Null);
        }

        [Test]
        public void ARetargetOlderThanThirtyMinutesDoesNotCount()
        {
            AutonomousRouteBlockMemory.RecordRetarget(200, 345_000, 505_000, 1, 10, 0, out _, out _);
            AutonomousRouteBlockMemory.RecordRetarget(200, 345_000, 505_000, 2, 10, 20 * Minute, out _, out _);
            Assert.That(AutonomousRouteBlockMemory.RecordRetarget(200, 345_000, 505_000, 3, 10, 31 * Minute, out _, out _), Is.Null);
        }

        [Test]
        public void BlockExpiresIsLevelGatedAndRenewRaisesTheLevel()
        {
            for (int bot = 1; bot <= 3; bot++)
                AutonomousRouteBlockMemory.RecordRetarget(200, 345_000, 505_000, bot, 10, 1_000, out _, out _);
            long until = 1_000 + AutonomousRouteBlockMemory.ActiveMilliseconds;
            Assert.That(AutonomousRouteBlockMemory.ActiveFor(10, 200, 2_000), Has.Length.EqualTo(1));
            Assert.That(AutonomousRouteBlockMemory.ActiveFor(5, 200, 2_000), Has.Length.EqualTo(1));
            Assert.That(AutonomousRouteBlockMemory.ActiveFor(11, 200, 2_000), Is.Empty);
            Assert.That(AutonomousRouteBlockMemory.ActiveFor(10, 1, 2_000), Is.Empty);
            Assert.That(AutonomousRouteBlockMemory.ActiveFor(10, 200, until), Is.Empty);
            var renewed = AutonomousRouteBlockMemory.RecordRetarget(200, 345_100, 505_000, 4, 13, 5_000, out bool created, out _);
            Assert.That(created, Is.False);
            Assert.That(renewed.MaxBotLevel, Is.EqualTo(13));
            Assert.That(renewed.Until, Is.EqualTo(5_000 + AutonomousRouteBlockMemory.ActiveMilliseconds));
            Assert.That(AutonomousRouteBlockMemory.ActiveFor(13, 200, 6_000), Has.Length.EqualTo(1));
        }

        [Test]
        public void BlocksPerRegionAreCapped()
        {
            for (int spot = 0; spot < AutonomousRouteBlockMemory.MaxBlocksPerRegion + 5; spot++)
            for (int bot = 0; bot < 3; bot++)
                AutonomousRouteBlockMemory.RecordRetarget(200, 100_000 + spot * 5_000, 505_000, spot * 10 + bot, 10, 1_000 + spot, out _, out _);
            Assert.That(AutonomousRouteBlockMemory.ActiveFor(1, 200, 2_000).Length, Is.EqualTo(AutonomousRouteBlockMemory.MaxBlocksPerRegion));
        }

        private static readonly AutonomousRouteBlockMemory.Block Block = new(200, 345_000, 505_000, 12, long.MaxValue);

        [Test]
        public void DestinationNearTheBlockIsBlocked() =>
            Assert.That(AutonomousRouteBlockMemory.Blocks(Block, 200, 345_000, 560_000, 200, 345_000, 506_000), Is.True);

        [Test]
        public void RoutePassingAt500IsBlockedAt1000IsNot()
        {
            Assert.That(AutonomousRouteBlockMemory.Blocks(Block, 200, 340_000, 505_500, 200, 350_000, 505_500), Is.True);
            Assert.That(AutonomousRouteBlockMemory.Blocks(Block, 200, 340_000, 506_000, 200, 350_000, 506_000), Is.False);
        }

        [Test]
        public void ABotAlreadyAtTheBlockOnlyChecksTheDestination()
        {
            Assert.That(AutonomousRouteBlockMemory.Blocks(Block, 200, 345_200, 505_000, 200, 350_000, 505_000), Is.False);
            Assert.That(AutonomousRouteBlockMemory.Blocks(Block, 200, 345_200, 505_000, 200, 345_500, 505_500), Is.True);
        }

        [Test]
        public void AnotherRegionIsNeverBlocked() =>
            Assert.That(AutonomousRouteBlockMemory.Blocks(Block, 1, 345_000, 505_000, 1, 345_000, 505_000), Is.False);

        [Test]
        public void ParallelUseDoesNotThrow()
        {
            Assert.DoesNotThrow(() => Parallel.For(0, 2_000, i =>
            {
                AutonomousRouteBlockMemory.RecordRetarget((ushort)(i % 3), 345_000 + i % 7 * 100, 505_000, i % 50, 10, i, out _, out _);
                AutonomousRouteBlockMemory.ActiveFor(10, (ushort)(i % 3), i);
                AutonomousRouteBlockMemory.AnyActive(i);
            }));
        }
    }
}
