using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// owner 2026-11-10 (review): the seam reversal guard's follow-ups. A caught crossing leaves the shared step memory
    /// (other bots are not handed it), a group member fails only its own step, and the goal-ground lookup is memoized.
    /// </summary>
    [TestFixture]
    public class UT_SeamReversalFollowUps
    {
        // A region id no seam file lists, so no seam or road data is read and nothing is shared with other tests.
        private const ushort TestRegion = 977;

        private static (Region Region, Zone West, Zone East) World()
        {
            var region = new Region(new RegionData { Id = TestRegion, Name = "seam test", Description = "seam test", Mobs = [] });
            var west = new Zone(region, 9770, "west", 0, 0, 65536, 65536, 9770, false, 0, false, 0, 0, 0, 0, 0);
            var east = new Zone(region, 9771, "east", 65536, 0, 65536, 65536, 9771, false, 0, false, 0, 0, 0, 0, 0);
            region.Zones.Add(west);
            region.Zones.Add(east);
            return (region, west, east);
        }

        [Test]
        public void OnlyANonLeaderMemberOfADynamicGroupFailsAlone()
        {
            Assert.That(AutonomousSeamReversalGuard.FailsMemberOnly(dynamicGroup: true, isLeader: false, inRealmRaid: false), Is.True);
            Assert.That(AutonomousSeamReversalGuard.FailsMemberOnly(dynamicGroup: true, isLeader: true, inRealmRaid: false), Is.False,
                "the leader's route is the group's route: full no-seam chain");
            Assert.That(AutonomousSeamReversalGuard.FailsMemberOnly(dynamicGroup: false, isLeader: false, inRealmRaid: false), Is.False,
                "solo bots run the full chain");
            Assert.That(AutonomousSeamReversalGuard.FailsMemberOnly(dynamicGroup: true, isLeader: false, inRealmRaid: true), Is.False,
                "realm raids keep their own expedition retry");
        }

        [Test]
        public void MemberHoldStaysUnderTheRouteStallTime()
        {
            // The controller's RouteStallReplotMilliseconds is 12 s; a longer hold would start stall recovery.
            Assert.That(AutonomousSeamReversalGuard.MemberHoldMilliseconds, Is.LessThan(12_000));
            Assert.That(AutonomousSeamReversalGuard.MemberHoldMilliseconds, Is.GreaterThan(0));
        }

        [Test]
        public void ForgetStepSwitchDefaultsOn() => Assert.That(AutonomousSeamReversalGuard.ForgetSteps, Is.True);

        [Test]
        public void ForgottenStepFailsAsReversedWithoutPlanning()
        {
            var (region, west, east) = World();
            Vector3 start = new(60_000, 30_000, 1_000), goal = new(90_000, 30_000, 1_000);
            AutonomousZoneItinerary.ForgetStep(region, west, east, start, goal);
            // nav is null: a remembered failure must answer before any planning touches it.
            Assert.That(AutonomousZoneItinerary.TryNextStep(region, west, east, start, goal, null, out _), Is.False);
            Assert.That(AutonomousZoneItinerary.LastStepWasReversed, Is.True);
            // Same 512 start cell and 2,048 goal cell: the same shared key.
            Assert.That(AutonomousZoneItinerary.TryNextStep(region, west, east, start + new Vector3(100, 100, 0),
                goal - new Vector3(500, 0, 0), null, out _), Is.False);
            Assert.That(AutonomousZoneItinerary.LastStepWasReversed, Is.True);
            // The next call clears the flag, whatever its outcome.
            Assert.That(AutonomousZoneItinerary.TryNextStep(null, west, east, start, goal, null, out _), Is.False);
            Assert.That(AutonomousZoneItinerary.LastStepWasReversed, Is.False);
        }

        [Test]
        public void ForgetStepIgnoresMissingOrSameZones()
        {
            var (region, west, _) = World();
            Assert.DoesNotThrow(() => AutonomousZoneItinerary.ForgetStep(null, west, west, default, default));
            Assert.DoesNotThrow(() => AutonomousZoneItinerary.ForgetStep(region, west, west, default, default));
            Assert.DoesNotThrow(() => AutonomousZoneItinerary.ForgetStep(region, west, null, default, default));
        }

        [Test]
        public void TargetAreaMemoStillHonoursTheSwitchAndTheZone()
        {
            var (_, west, _) = World();
            Vector3 inside = new(30_000, 30_000, 1_000), outside = new(70_000, 30_000, 1_000);
            bool saved = AutonomousSeamChains.TargetGround;
            try
            {
                AutonomousSeamChains.TargetGround = true;
                int? first = AutonomousSeamChains.ConfidentTargetArea(TestRegion, west, inside);
                Assert.That(AutonomousSeamChains.ConfidentTargetArea(TestRegion, west, inside), Is.EqualTo(first), "memoized answer");
                Assert.That(first, Is.Null, "region without seam data: unknown ground");
                Assert.That(AutonomousSeamChains.ConfidentTargetArea(TestRegion, west, outside), Is.Null, "goal outside the zone");
                AutonomousSeamChains.TargetGround = false;
                Assert.That(AutonomousSeamChains.ConfidentTargetArea(TestRegion, west, inside), Is.Null, "switch off even after a memo");
            }
            finally
            {
                AutonomousSeamChains.TargetGround = saved;
            }
        }
    }
}
