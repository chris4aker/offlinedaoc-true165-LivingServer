using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Owner 2026-11-11 (item 20): ~300 Albion bots a night stood on the cliff edge at 548762,495343 (Black Mtns. South), 51
    /// units above the crossing into Camelot Hills they were sent to; Detour's partial path ends there and the mover kept it
    /// (within 256 of the target) without asking the zone roads, which have the real way round. A stall there teaches the
    /// spot; later walks to it take the roads.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_NearMissDeadEnds
    {
        private static readonly Vector3 Crossing = new(548_800, 495_328, 3_260);
        private static readonly Vector3 CliffEdge = new(548_762, 495_343, 3_290);

        [SetUp]
        public void Reset() => AutonomousNearMissDeadEnds.Clear();

        [TearDown]
        public void Cleanup() => AutonomousNearMissDeadEnds.Clear();

        [Test]
        public void TheCamelotHillsCliffEdgeIsANearMissStall()
        {
            Assert.That(AutonomousNearMissDeadEnds.IsNearMissStall(PathfindingStatus.PartialPathFound, CliffEdge, Crossing, true), Is.True);
            // another zone (no road graph joins them), a full path, or a stall far from the target are not
            Assert.That(AutonomousNearMissDeadEnds.IsNearMissStall(PathfindingStatus.PartialPathFound, CliffEdge, Crossing, false), Is.False);
            Assert.That(AutonomousNearMissDeadEnds.IsNearMissStall(PathfindingStatus.PathFound, CliffEdge, Crossing, true), Is.False);
            Assert.That(AutonomousNearMissDeadEnds.IsNearMissStall(PathfindingStatus.PartialPathFound,
                new Vector3(314_846, 471_535, 5_263), new Vector3(317_100, 470_673, 6_417), true), Is.False);
        }

        [Test]
        public void ASpotIsLearnedOnceRenewedAndForgottenAfterSixHours()
        {
            long now = 1_000_000;
            Assert.That(AutonomousNearMissDeadEnds.IsKnown(2, Crossing, now), Is.False);
            Assert.That(AutonomousNearMissDeadEnds.Remember(2, Crossing, now), Is.True);
            Assert.That(AutonomousNearMissDeadEnds.Remember(2, Crossing + new Vector3(20, -10, 0), now + 1_000), Is.False, "same crossing");
            Assert.That(AutonomousNearMissDeadEnds.IsKnown(2, Crossing + new Vector3(30, 30, 0), now), Is.True);
            Assert.That(AutonomousNearMissDeadEnds.IsKnown(2, Crossing + new Vector3(200, 0, 0), now), Is.False);
            Assert.That(AutonomousNearMissDeadEnds.IsKnown(0, Crossing, now), Is.False, "another zone");
            long renewed = now + 1_000 + AutonomousNearMissDeadEnds.MemoryMilliseconds;
            Assert.That(AutonomousNearMissDeadEnds.IsKnown(2, Crossing, renewed - 1), Is.True);
            Assert.That(AutonomousNearMissDeadEnds.IsKnown(2, Crossing, renewed), Is.False);
            Assert.That(AutonomousNearMissDeadEnds.Remember(2, Crossing, renewed), Is.True, "learned again after expiry");
        }

        [Test]
        public void TheMemoryIsBounded()
        {
            for (int i = 0; i < AutonomousNearMissDeadEnds.Capacity; i++)
                Assert.That(AutonomousNearMissDeadEnds.Remember(2, new Vector3(i * 1_000, 0, 0), 0), Is.True);
            Assert.That(AutonomousNearMissDeadEnds.Remember(2, new Vector3(-5_000, 0, 0), 0), Is.False);
            Assert.That(AutonomousNearMissDeadEnds.IsKnown(2, new Vector3(-5_000, 0, 0), 0), Is.False);
            // expired spots free their room
            Assert.That(AutonomousNearMissDeadEnds.Remember(2, new Vector3(-5_000, 0, 0), AutonomousNearMissDeadEnds.MemoryMilliseconds), Is.True);
        }

        [Test]
        public void AnUnknownNearMissKeepsItsPartialPathWithoutAskingTheRoads()
        {
            var zone = new Zone(null, 2, "zone2", 483_328, 434_176, 65_536, 65_536, 2, false, 0, false, 0, 0, 0, 0, 0);
            var nodes = new WrappedPathfindingNode[8];
            nodes[0] = new WrappedPathfindingNode(new Vector3(516_852, 497_979, 3_370), default);
            nodes[1] = new WrappedPathfindingNode(CliffEdge, default);
            var direct = new PathfindingResult(PathfindingStatus.PartialPathFound, 2);
            // nav null: had the roads been asked they would also say no, so only the gate is under test here
            Assert.That(Pathfinder.TryRoadPath(null, zone, nodes[0].Position, Crossing, null, direct, nodes, nodes.Length, out var result), Is.False);
            Assert.That(result.Status, Is.EqualTo(PathfindingStatus.PartialPathFound));
            Assert.That(result.NodeCount, Is.EqualTo(2));
        }
    }
}
