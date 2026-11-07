using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    public class UT_CompanionFollowPolicy
    {
        [Test]
        public void CatchupIsSmoothMonotonicAndBoundedAtTwentyPercent()
        {
            int last = 0;
            for (int distance = 0; distance <= 4000; distance++)
            {
                int speed = CompanionFollowPolicy.CalculateSpeed(191, 390, distance);
                Assert.That(speed, Is.InRange(390, 468));
                Assert.That(speed, Is.GreaterThanOrEqualTo(last));
                if (distance > 0) Assert.That(speed - last, Is.LessThanOrEqualTo(1));
                last = speed;
            }
            Assert.That(CompanionFollowPolicy.CalculateSpeed(0, 390, 3000), Is.Zero);
            Assert.That(CompanionFollowPolicy.CalculateSpeed(500, 390, 0), Is.EqualTo(500));
        }

        [Test]
        public void FasterFollowerClosesAPathDelayWithoutTeleportingOrOvershooting()
        {
            double leader = 500, follower = 0;
            for (int tick = 0; tick < 1200; tick++)
            {
                leader += 390 * 0.1;
                double slot = leader - 140;
                double gap = slot - follower;
                follower += System.Math.Min(gap, CompanionFollowPolicy.CalculateSpeed(191, 390, gap) * 0.1);
                Assert.That(follower, Is.LessThanOrEqualTo(slot));
            }
            Assert.That(leader - 140 - follower, Is.LessThan(80));
        }

        [Test]
        public void BuffWaitReleasesAfterStopAndResetsOnMovementOrPositionChange()
        {
            var state = new CompanionFollowPolicy.State();
            Assert.That(state.Observe(Vector3.Zero, true, 1000), Is.True);
            Assert.That(state.Observe(Vector3.Zero, false, 1300), Is.True);
            Assert.That(state.Observe(Vector3.Zero, false, 1600), Is.False);
            Assert.That(state.Observe(new(20, 0, 0), false, 1700), Is.True);
            Assert.That(state.Observe(new(20, 0, 0), false, 2300), Is.False);
            for (int i = 0; i < 100; i++)
            {
                Assert.That(state.Observe(new(20, 0, 0), true, 3000 + i * 1000), Is.True);
                Assert.That(state.Observe(new(20, 0, 0), false, 3600 + i * 1000), Is.False);
            }
        }

        [Test]
        public void PredictionIsShortBoundedAndTracksActualMotionNotFacing()
        {
            Assert.That(CompanionFollowPolicy.Predict(Vector3.Zero, new(0, 390, 0), 390), Is.EqualTo(new Vector3(0, 78, 0)));
            Assert.That(CompanionFollowPolicy.Predict(Vector3.Zero, new(10000, 0, 0), 390), Is.EqualTo(new Vector3(78, 0, 0)));
            Assert.That(CompanionFollowPolicy.Predict(Vector3.Zero, new(10000, 0, 0), 3000), Is.EqualTo(new Vector3(80, 0, 0)));
            Assert.That(CompanionFollowPolicy.Predict(new(1, 2, 3), Vector3.Zero, 390), Is.EqualTo(new Vector3(1, 2, 3)));
        }

        // owner 2026-11-06: companions stopped at the Lyonesse / Cornwall border (Lyonesse: x 286720-352256, y 630784-696320)
        [Test]
        public void ZoneExitIsWhereTheLineLeavesTheZone()
        {
            const int x0 = 286720, y0 = 630784, size = 65536;
            Assert.That(CompanionFollowPolicy.TryZoneExit(new(350300, 667330, 6100), new(353900, 667460, 6150), x0, y0, size, size,
                out Vector3 exit), Is.True, "Lyonesse into Cornwall (east)");
            Assert.That(exit.X, Is.EqualTo(352256).Within(0.5));
            Assert.That(exit.Y, Is.InRange(667330f, 667460f));
            Assert.That(CompanionFollowPolicy.TryZoneExit(new(300000, 640000, 0), new(300000, 620000, 0), x0, y0, size, size,
                out exit), Is.True, "out to the north");
            Assert.That(exit.Y, Is.EqualTo(630784).Within(0.5));
            Assert.That(CompanionFollowPolicy.TryZoneExit(new(300000, 640000, 0), new(310000, 650000, 0), x0, y0, size, size, out _),
                Is.False, "the spot is in the same zone");
            Assert.That(CompanionFollowPolicy.TryZoneExit(new(353000, 640000, 0), new(360000, 640000, 0), x0, y0, size, size, out _),
                Is.False, "the companion is not in this zone");
        }
    }
}
