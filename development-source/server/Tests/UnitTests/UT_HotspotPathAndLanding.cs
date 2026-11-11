using System.Collections.Generic;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Owner 2026-11-10: live death hotspots are checked against the real path (corners) of a solo bot, with a replan
    /// budget; solo bots skip a horse whose landing they cannot survive on the way elsewhere (the Parthanan Farm horse for
    /// Hibernians below 15), judged from spawn data; and landing deaths are watched.
    /// </summary>
    [TestFixture]
    public class UT_HotspotPathAndLanding
    {
        private static readonly AutonomousRoadHotspots.Hotspot Spot = new(200, 333_900, 609_500, 22, long.MaxValue);

        [Test]
        public void AnLShapedPathWhoseCornerCrossesIsBlockedThoughTheStraightLineMisses()
        {
            // straight from (320k,600k) to (320k,620k) passes 13,900 away; the corner goes through the hotspot
            Vector3[] path = [new(320_000, 600_000, 0), new(334_000, 609_000, 0), new(320_000, 620_000, 0)];
            Assert.That(AutonomousRoadHotspots.SegmentDistanceSquared(320_000, 600_000, 320_000, 620_000, Spot.X, Spot.Y),
                Is.GreaterThan(2_500d * 2_500d));
            Assert.That(AutonomousRoadHotspots.FirstBlockedLeg(path, 200, [Spot], out var hit), Is.EqualTo(0));
            Assert.That(hit, Is.SameAs(Spot));
        }

        [Test]
        public void TheFirstBlockedLegIsReported()
        {
            Vector3[] path = [new(300_000, 600_000, 0), new(310_000, 600_000, 0), new(333_000, 609_000, 0)];
            Assert.That(AutonomousRoadHotspots.FirstBlockedLeg(path, 200, [Spot], out _), Is.EqualTo(1));
        }

        [Test]
        public void StartingInsideAndWalkingOutIsNotBlocked()
        {
            Vector3[] path = [new(334_000, 609_000, 0), new(345_000, 620_000, 0)];
            Assert.That(AutonomousRoadHotspots.FirstBlockedLeg(path, 200, [Spot], out _), Is.EqualTo(-1));
        }

        [Test]
        public void EndingInsideIsBlocked()
        {
            Vector3[] path = [new(320_000, 600_000, 0), new(333_500, 609_200, 0)];
            Assert.That(AutonomousRoadHotspots.FirstBlockedLeg(path, 200, [Spot], out _), Is.EqualTo(0));
        }

        [Test]
        public void AnotherRegionOrNoHotspotIsIgnored()
        {
            Vector3[] path = [new(320_000, 600_000, 0), new(333_500, 609_200, 0)];
            Assert.That(AutonomousRoadHotspots.FirstBlockedLeg(path, 1, [Spot], out _), Is.EqualTo(-1));
            Assert.That(AutonomousRoadHotspots.FirstBlockedLeg(path, 200, [], out _), Is.EqualTo(-1));
            Assert.That(AutonomousRoadHotspots.FirstBlockedLeg(path[..1], 200, [Spot], out _), Is.EqualTo(-1));
        }

        [Test]
        public void WalkToAHorseThroughAHotspot()
        {
            Assert.That(AutonomousRoadHotspots.WalkLegUnsafe(200, 320_000, 600_000, 345_000, 618_000, [Spot]), Is.True);
            Assert.That(AutonomousRoadHotspots.WalkLegUnsafe(200, 320_000, 600_000, 320_000, 620_000, [Spot]), Is.False);
        }

        [Test]
        public void LandingNearAHotspot()
        {
            Assert.That(AutonomousRoadHotspots.LandingNearHotspot(200, 333_900 + 3_000, 609_500, [Spot]), Is.True);
            Assert.That(AutonomousRoadHotspots.LandingNearHotspot(200, 333_900 + 3_400, 609_500, [Spot]), Is.False);
            Assert.That(AutonomousRoadHotspots.LandingNearHotspot(1, 333_900, 609_500, [Spot]), Is.False);
        }

        [Test]
        public void TwoReplansPerFifteenMinutes()
        {
            long m = 60_000;
            Assert.That(AutonomousRoadHotspots.ReplanAllowed(new long[] { 0, 0 }, 10 * m), Is.True);
            Assert.That(AutonomousRoadHotspots.ReplanAllowed(new long[] { 1 * m, 0 }, 10 * m), Is.True);
            Assert.That(AutonomousRoadHotspots.ReplanAllowed(new long[] { 1 * m, 5 * m }, 10 * m), Is.False);
            Assert.That(AutonomousRoadHotspots.ReplanAllowed(new long[] { 1 * m, 5 * m }, 16 * m), Is.True);
            Assert.That(AutonomousRoadHotspots.HotspotSkipMilliseconds, Is.LessThanOrEqualTo(15 * m));
        }

        // owner 2026-11-11 (item 18): the margin is 2 now; threat 18 against level 15 is unsafe, against 16 it is not
        [TestCase(18, 14, 20_000d, ExpectedResult = true)]
        [TestCase(18, 15, 20_000d, ExpectedResult = true)]
        [TestCase(18, 16, 20_000d, ExpectedResult = false)]
        [TestCase(18, 15, 2_000d, ExpectedResult = false, TestName = "LandingUnsafe: level 15 going to the farm itself")]
        [TestCase(18, 9, 20_000d, ExpectedResult = true)]
        [TestCase(18, 9, 2_000d, ExpectedResult = false, TestName = "LandingUnsafe: going there, not through")]
        [TestCase(0, 1, 20_000d, ExpectedResult = false)]
        public bool LandingUnsafeMatchesTheIntent(int threat, int botLevel, double landingToGoal) =>
            AutonomousStableRoutePlanner.LandingUnsafe(threat, botLevel, landingToGoal);

        [Test]
        public void SpawnThreatFromSpawnData()
        {
            // parthanan: mob row 18/100/500, template "17-18" aggro 60/500 replacing mob values
            Assert.That(AutonomousStableRoutePlanner.SpawnThreatLevel(0, 18, 100, 500, 0, true, "17-18", 60, 500), Is.EqualTo(18));
            // a realm NPC, a peaceful one, aggro level 0 and no aggro range are no threat
            Assert.That(AutonomousStableRoutePlanner.SpawnThreatLevel(3, 30, 100, 500, 0, false, null, 0, 0), Is.EqualTo(0));
            Assert.That(AutonomousStableRoutePlanner.SpawnThreatLevel(0, 30, 100, 500, 0x10, false, null, 0, 0), Is.EqualTo(0));
            Assert.That(AutonomousStableRoutePlanner.SpawnThreatLevel(0, 30, 0, 500, 0, false, null, 0, 0), Is.EqualTo(0));
            Assert.That(AutonomousStableRoutePlanner.SpawnThreatLevel(0, 5, 200, 500, 0, true, "5", 0, 0), Is.EqualTo(0));
            // the mob row stands when the template does not replace it
            Assert.That(AutonomousStableRoutePlanner.SpawnThreatLevel(0, 12, 50, 400, 0, false, "40", 0, 0), Is.EqualTo(12));
            Assert.That(AutonomousStableRoutePlanner.SpawnThreatLevel(0, 1, 50, 400, 0, true, "46;47;48", 50, 400), Is.EqualTo(48));
        }

        [Test]
        public void LandingThreatIsAPackNotOneLoneNamed()
        {
            Assert.That(AutonomousStableRoutePlanner.LandingThreat(new[] { 18, 17, 18, 17, 5 }, 3), Is.EqualTo(17));
            Assert.That(AutonomousStableRoutePlanner.LandingThreat(new[] { 50, 8, 8 }, 3), Is.EqualTo(8));
            Assert.That(AutonomousStableRoutePlanner.LandingThreat(new[] { 50, 40 }, 3), Is.EqualTo(0));
        }

        [Test]
        public void AnUnusableLegIsSkippedFirstAndAsAConnection()
        {
            // leg 0: a fast ride to the goal; leg 1: a fast ride to leg 0's origin area
            List<AutonomousStableRoutePlanner.LegMetric> legs =
            [
                new(0, 1_000, 0, 9_900, 0, 5, 0),
                new(1, 0, 0, 1_000, 0, 1, 0),
                new(2, 0, 0, 9_000, 0, 30, 0)
            ];
            var normal = AutonomousStableRoutePlanner.ChooseFirstLeg(0, 0, 10_000, 0, 100, 0, legs);
            Assert.That(normal, Is.Not.Null);
            Assert.That(normal!.Value.FirstLegIndex, Is.Not.EqualTo(2));
            // leg 0 unusable: neither boarded first nor reached through leg 1
            var safe = AutonomousStableRoutePlanner.ChooseFirstLeg(0, 0, 10_000, 0, 100, 0, legs, null, new HashSet<int> { 0 });
            Assert.That(safe, Is.Not.Null);
            Assert.That(safe!.Value.FirstLegIndex, Is.EqualTo(2));
            Assert.That(safe.Value.HopCount, Is.EqualTo(1));
            // every ride unusable: walk
            Assert.That(AutonomousStableRoutePlanner.ChooseFirstLeg(0, 0, 10_000, 0, 100, 0, legs, null,
                new HashSet<int> { 0, 1, 2 }), Is.Null);
        }

        // overnight 2026-11-11 positions: the Howth landing, Eilwen (Realm Exchange near Connla), the Connla landing, the
        // Silvermine road hotspot (spraggonilt 20) and Lethe's (32) beside Connla
        private static readonly AutonomousRoadHotspots.Hotspot Silvermine = new(200, 334_328, 608_977, 20, long.MaxValue);
        private static readonly AutonomousRoadHotspots.Hotspot Lethe = new(200, 320_494, 630_445, 32, long.MaxValue);

        [Test]
        public void HowthToEilwenWalksThroughTheSilvermineRoadButConnlaToEilwenDoesNot()
        {
            Assert.That(AutonomousRoadHotspots.FinishWalkUnsafe(200, 342_466, 593_220, 315_236, 635_972, [Silvermine]), Is.True);
            Assert.That(AutonomousRoadHotspots.FinishWalkUnsafe(200, 342_466, 593_220, 315_236, 635_972, [Lethe]), Is.True);
            Assert.That(AutonomousRoadHotspots.FinishWalkUnsafe(200, 294_355, 641_554, 315_236, 635_972, [Silvermine, Lethe]), Is.False);
            Assert.That(AutonomousRoadHotspots.FinishWalkUnsafe(1, 342_466, 593_220, 315_236, 635_972, [Silvermine]), Is.False);
            Assert.That(AutonomousRoadHotspots.FinishWalkUnsafe(200, 342_466, 593_220, 315_236, 635_972, []), Is.False);
            Assert.That(AutonomousRoadHotspots.FinishWalkUnsafe(200, 342_466, 593_220, 315_236, 635_972, null), Is.False);
        }

        [Test]
        public void AFinishWalkStartingOrEndingInTheHotspotDoesNotCount()
        {
            // from inside: the landing rule judges that; to inside: the bot was sent there, no landing gets it there safer
            Assert.That(AutonomousRoadHotspots.FinishWalkUnsafe(200, 334_000, 609_000, 300_000, 640_000, [Silvermine]), Is.False);
            Assert.That(AutonomousRoadHotspots.FinishWalkUnsafe(200, 342_466, 593_220, 334_500, 609_500, [Silvermine]), Is.False);
        }

        [Test]
        public void AFinishThroughAHotspotLosesToASaferLandingAtMostTenMinutesSlower()
        {
            // walking all the way takes 1,000 s. leg 0: the quick ride whose landing walks through the hotspot (60 s ride +
            // 100 s walk); leg 1: a slower ride landing beside the goal (405 s); leg 2: a very slow safe ride (902 s)
            List<AutonomousStableRoutePlanner.LegMetric> legs =
            [
                new(0, 0, 0, 0, 18_000, 60, 0),
                new(1, 0, 0, 0, 19_900, 400, 0),
                new(2, 0, 0, 0, 19_950, 900, 0)
            ];
            var normal = AutonomousStableRoutePlanner.ChooseFirstLeg(0, 0, 0, 20_000, 20, 0, legs);
            Assert.That(normal!.Value.FirstLegIndex, Is.EqualTo(0));
            var safer = AutonomousStableRoutePlanner.ChooseFirstLeg(0, 0, 0, 20_000, 20, 0, legs, null, null, new HashSet<int> { 0 });
            Assert.That(safer!.Value.FirstLegIndex, Is.EqualTo(1));
            Assert.That(safer.Value.EstimatedSeconds, Is.EqualTo(405).Within(0.01), "the estimate is the real time, no penalty");
            // only the very slow safe ride is left: more than ten minutes slower, so the quick ride stays (not crippled)
            var kept = AutonomousStableRoutePlanner.ChooseFirstLeg(0, 0, 0, 20_000, 20, 0, legs, new HashSet<int> { 1 }, null,
                new HashSet<int> { 0 });
            Assert.That(kept!.Value.FirstLegIndex, Is.EqualTo(0));
            // every finish unsafe: nothing changes
            var all = AutonomousStableRoutePlanner.ChooseFirstLeg(0, 0, 0, 20_000, 20, 0, legs, null, null,
                new HashSet<int> { 0, 1, 2 });
            Assert.That(all!.Value.FirstLegIndex, Is.EqualTo(0));
        }

        [Test]
        public void AnUnsafeLandingStillServesAsAConnection()
        {
            // Howth (leg 0) walks through the hotspot to the goal, but its master's Connla horse (leg 1) lands beside it
            List<AutonomousStableRoutePlanner.LegMetric> legs =
            [
                new(0, 0, 0, 0, 10_000, 60, 0),
                new(1, 0, 10_000, 0, 19_900, 120, 0)
            ];
            var plan = AutonomousStableRoutePlanner.ChooseFirstLeg(0, 0, 0, 20_000, 100, 0, legs, null, null, new HashSet<int> { 0 });
            Assert.That(plan!.Value.FirstLegIndex, Is.EqualTo(0));
            Assert.That(plan.Value.HopCount, Is.EqualTo(2));
        }

        [Test]
        public void AnUnsafeStraightWalkTakesASafeRideUpToTenMinutesSlower()
        {
            // walking takes 200 s; the safe ride 500 s
            List<AutonomousStableRoutePlanner.LegMetric> legs = [new(0, 0, 0, 0, 19_900, 499, 0)];
            Assert.That(AutonomousStableRoutePlanner.ChooseFirstLeg(0, 0, 0, 20_000, 100, 0, legs), Is.Null);
            var ride = AutonomousStableRoutePlanner.ChooseFirstLeg(0, 0, 0, 20_000, 100, 0, legs, null, null, null, true);
            Assert.That(ride!.Value.FirstLegIndex, Is.EqualTo(0));
            List<AutonomousStableRoutePlanner.LegMetric> slow = [new(0, 0, 0, 0, 19_900, 1_000, 0)];
            Assert.That(AutonomousStableRoutePlanner.ChooseFirstLeg(0, 0, 0, 20_000, 100, 0, slow, null, null, null, true), Is.Null);
        }

        /// <summary>Review 2026-11-10: the unrestricted search behind the landing-skip log line runs once per bot per few minutes.</summary>
        [Test]
        public void LandingSkipLogSampleIsThrottledPerBot()
        {
            const int bot = -987_654, other = -987_655;
            long m = 60_000, start = 1_000_000_000L;
            Assert.That(AutonomousStableRoutePlanner.TryTakeLandingSkipSample(bot, start), Is.True);
            Assert.That(AutonomousStableRoutePlanner.TryTakeLandingSkipSample(bot, start + 1 * m), Is.False);
            Assert.That(AutonomousStableRoutePlanner.TryTakeLandingSkipSample(other, start + 1 * m), Is.True);
            Assert.That(AutonomousStableRoutePlanner.TryTakeLandingSkipSample(bot,
                start + AutonomousStableRoutePlanner.LandingSkipSampleMilliseconds), Is.True);
        }

        [Test]
        public void LandingDeathWindow()
        {
            Assert.That(AutonomousStableLandingWatch.CountsAsLandingDeath(1_000, 1_000 + 95_000), Is.True);
            Assert.That(AutonomousStableLandingWatch.CountsAsLandingDeath(1_000, 1_000 + 5 * 60_000), Is.True);
            Assert.That(AutonomousStableLandingWatch.CountsAsLandingDeath(1_000, 1_000 + 5 * 60_000 + 1), Is.False);
            Assert.That(AutonomousStableLandingWatch.CountsAsLandingDeath(0, 10), Is.False);
        }
    }
}
