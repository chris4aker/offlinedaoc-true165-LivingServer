using System.Collections.Generic;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;
using Crossing = DOL.GS.AutonomousZoneSeams.Crossing;
using Step = DOL.GS.AutonomousZoneBoundaryRouting.Step;

namespace DOL.UnitTests
{
    /// <summary>
    /// owner 2026-11-10: the Albion seam ping-pong (Camelot Hills 0 / Salisbury 1 / Black Mtns. South 2 / Campacorentin 8).
    /// The seam table is the region-1 ground from navmesh/seams.json (area ids per side), built in memory: no meshes.
    /// </summary>
    [TestFixture]
    public class UT_SeamReversalAndTargetArea
    {
        // Camelot Hills: main 51653, west strip 70035, east 70235. Salisbury: main 45257, strip 28859, east 57205.
        // Black Mtns. South: 63673. Campacorentin: main 76704, north band 7103.
        private static readonly Dictionary<(int, int), Crossing[]> Region1 = new()
        {
            [(0, 1)] = [new(552_000, 3600, 3600, 70035, 28859), new(570_000, 2300, 2300, 51653, 45257), new(605_000, 2300, 2300, 70235, 57205)],
            [(1, 0)] = [new(552_000, 3600, 3600, 28859, 70035), new(570_000, 2300, 2300, 45257, 51653), new(605_000, 2300, 2300, 57205, 70235)],
            [(2, 0)] = [new(490_000, 3300, 3300, 63673, 51653), new(497_000, 3600, 3600, 63673, 70035)],
            [(0, 2)] = [new(490_000, 3300, 3300, 51653, 63673), new(497_000, 3600, 3600, 70035, 63673)],
            [(1, 8)] = [new(582_500, 2400, 2400, 28859, 7103), new(595_000, 2400, 2400, 45257, 76704)],
            [(8, 1)] = [new(582_500, 2400, 2400, 7103, 28859), new(595_000, 2400, 2400, 76704, 45257)],
        };

        private static Crossing[] Seams(ushort from, ushort to) => Region1.TryGetValue((from, to), out var c) ? c : null;

        private static bool? Reaches(ushort[] route, int outside, int target) =>
            AutonomousSeamChains.ChainReachesArea(Seams, route, outside, target);

        [Test]
        public void StripCrossingFromBlackMountainsNeverReachesCampacorentinMain() =>
            Assert.That(Reaches([2, 0, 1, 8], 70035, 76704), Is.False);

        [Test]
        public void MainCrossingFromBlackMountainsReachesCampacorentinMain() =>
            Assert.That(Reaches([2, 0, 1, 8], 51653, 76704), Is.True);

        [Test]
        public void SalisburyStripNeverReachesCampacorentinMain() =>
            Assert.That(Reaches([0, 1, 8], 28859, 76704), Is.False);

        [Test]
        public void LoopExitFromTheStripThroughBlackMountainsReachesTheGoal() =>
            Assert.That(Reaches([0, 2, 0, 1, 8], 63673, 76704), Is.True);

        [Test]
        public void CampOnTheStripKeepsItsStripCrossings()
        {
            Assert.That(Reaches([2, 0, 1], 70035, 28859), Is.True);
            Assert.That(Reaches([2, 0, 1], 51653, 28859), Is.False);
        }

        [Test]
        public void MissingDataGivesNoAnswer()
        {
            Assert.That(Reaches([2, 0, 5], 51653, 76704), Is.Null, "missing border");
            Assert.That(Reaches([2, 0, 1], -1, 45257), Is.Null, "unknown outside area");
            Assert.That(Reaches([2], 63673, 63673), Is.Null, "no border at all");
            Crossing[] Unknown(ushort from, ushort to) => [new(1, 0, 0, -1, -1)];
            Assert.That(AutonomousSeamChains.ChainReachesArea(Unknown, new ushort[] { 2, 0, 1 }, 51653, 45257), Is.Null, "inside area -1");
        }

        [Test]
        public void SingleBorderRouteNeedsTheTargetGroundItself()
        {
            Assert.That(Reaches([0, 1], 28859, 45257), Is.False);
            Assert.That(Reaches([0, 1], 45257, 45257), Is.True);
        }

        [Test]
        public void FilterOnlyWhenSomeCrossingIsProvenToReachTheTarget()
        {
            Assert.That(AutonomousSeamChains.RequireTarget(new bool?[] { false, null }), Is.False);
            Assert.That(AutonomousSeamChains.RequireTarget(new bool?[] { false, true }), Is.True);
            Assert.That(AutonomousSeamChains.RequireTarget(new bool?[0]), Is.False);
            Assert.That(AutonomousSeamChains.RequireTarget(null), Is.False);
        }

        [Test]
        public void TargetGroundOnlyWhenSure()
        {
            Assert.That(AutonomousSeamChains.ConfidentArea(new List<(float, int)>()), Is.Null, "no anchors: unknown, no filtering");
            Assert.That(AutonomousSeamChains.ConfidentArea(null), Is.Null);
            Assert.That(AutonomousSeamChains.ConfidentArea([(1_600f, 45257)]), Is.Null, "nearest anchor too far");
            Assert.That(AutonomousSeamChains.ConfidentArea([(531f, 45257), (1_748f, 45257)]), Is.EqualTo(45257));
            Assert.That(AutonomousSeamChains.ConfidentArea([(531f, 45257), (900f, 28859)]), Is.Null, "other ground close by");
            Assert.That(AutonomousSeamChains.ConfidentArea([(300f, 45257), (1_000f, 28859)]), Is.EqualTo(45257), "other ground well beyond");
            Assert.That(AutonomousSeamChains.ConfidentArea([(300f, -1)]), Is.Null, "anchor without an area id");
        }

        [Test]
        public void SwitchesDefaultOn()
        {
            Assert.That(AutonomousSeamChains.TargetGround, Is.True);
            Assert.That(AutonomousSeamReversalGuard.Enabled, Is.True);
        }

        // Black Mtns. South / Camelot Hills corner (x=548864): the logged pair 548800,499552 <> 548928,499552.
        private static readonly Step BmsToCh = new(new(548_800, 499_552, 3629), new(548_928, 499_552, 3642));
        private static readonly Step ChToBms = new(new(548_928, 499_552, 3642), new(548_800, 499_552, 3629));
        private static readonly Vector3 SalisburyCamp = new(559_456, 573_158, 2310);

        [Test]
        public void SecondReversalTriggers()
        {
            var state = new AutonomousSeamReversalGuard.State();
            Assert.That(AutonomousSeamReversalGuard.Note(ref state, BmsToCh, SalisburyCamp, 1_000), Is.EqualTo(0));
            Assert.That(AutonomousSeamReversalGuard.Note(ref state, ChToBms, SalisburyCamp, 5_000), Is.EqualTo(1));
            Assert.That(AutonomousSeamReversalGuard.ShouldFail(ref state, BmsToCh, SalisburyCamp, 9_000), Is.True);
        }

        [Test]
        public void SalisburyCornerPingPongTriggers()
        {
            var campa = new Vector3(530_731, 614_128, 2402);
            var chToSal = new Step(new(548_960, 548_800, 3637), new(548_960, 548_928, 3638));
            var salToCh = new Step(new(548_960, 548_928, 3638), new(548_960, 548_800, 3637));
            var state = new AutonomousSeamReversalGuard.State();
            Assert.That(AutonomousSeamReversalGuard.ShouldFail(ref state, chToSal, campa, 0), Is.False);
            Assert.That(AutonomousSeamReversalGuard.ShouldFail(ref state, salToCh, campa, 3_000), Is.False);
            Assert.That(AutonomousSeamReversalGuard.ShouldFail(ref state, chToSal, campa, 6_000), Is.True);
        }

        [Test]
        public void MovedDestinationResets()
        {
            var state = new AutonomousSeamReversalGuard.State();
            AutonomousSeamReversalGuard.Note(ref state, BmsToCh, SalisburyCamp, 1_000);
            AutonomousSeamReversalGuard.Note(ref state, ChToBms, SalisburyCamp, 2_000);
            Assert.That(AutonomousSeamReversalGuard.Note(ref state, BmsToCh, SalisburyCamp + new Vector3(200, 0, 0), 3_000), Is.EqualTo(0));
        }

        [Test]
        public void LongGapResets()
        {
            var state = new AutonomousSeamReversalGuard.State();
            AutonomousSeamReversalGuard.Note(ref state, BmsToCh, SalisburyCamp, 1_000);
            AutonomousSeamReversalGuard.Note(ref state, ChToBms, SalisburyCamp, 2_000);
            Assert.That(AutonomousSeamReversalGuard.Note(ref state, BmsToCh, SalisburyCamp, 62_001), Is.EqualTo(0));
        }

        [Test]
        public void DifferentCrossingInBetweenResets()
        {
            var other = new Step(new(570_000, 548_800, 2300), new(570_000, 548_928, 2300));
            var state = new AutonomousSeamReversalGuard.State();
            AutonomousSeamReversalGuard.Note(ref state, BmsToCh, SalisburyCamp, 1_000);
            Assert.That(AutonomousSeamReversalGuard.Note(ref state, ChToBms, SalisburyCamp, 2_000), Is.EqualTo(1));
            Assert.That(AutonomousSeamReversalGuard.Note(ref state, other, SalisburyCamp, 3_000), Is.EqualTo(0));
            Assert.That(AutonomousSeamReversalGuard.Note(ref state, BmsToCh, SalisburyCamp, 4_000), Is.EqualTo(0));
        }

        [Test]
        public void ToleranceIsSixtyFourUnits()
        {
            var near = new Step(ChToBms.Inside + new Vector3(0, 50, 0), ChToBms.Outside + new Vector3(0, 50, 0));
            var far = new Step(ChToBms.Inside + new Vector3(0, 100, 0), ChToBms.Outside + new Vector3(0, 100, 0));
            Assert.That(AutonomousSeamReversalGuard.IsReversal(BmsToCh, near), Is.True);
            Assert.That(AutonomousSeamReversalGuard.IsReversal(BmsToCh, far), Is.False);
            Assert.That(AutonomousSeamReversalGuard.IsReversal(BmsToCh, BmsToCh), Is.False, "the same crossing again is not a reversal");
        }

        [Test]
        public void DestinationAtTheSeamIsExempt()
        {
            var atSeam = new Vector3(549_200, 499_700, 3600);
            Assert.That(AutonomousSeamReversalGuard.Exempt(BmsToCh, atSeam), Is.True);
            Assert.That(AutonomousSeamReversalGuard.Exempt(BmsToCh, SalisburyCamp), Is.False);
            var state = new AutonomousSeamReversalGuard.State();
            AutonomousSeamReversalGuard.ShouldFail(ref state, BmsToCh, atSeam, 0);
            AutonomousSeamReversalGuard.ShouldFail(ref state, ChToBms, atSeam, 1_000);
            Assert.That(AutonomousSeamReversalGuard.ShouldFail(ref state, BmsToCh, atSeam, 2_000), Is.False);
            Assert.That(state.Reversals, Is.EqualTo(2), "still counted, only not failed");
        }

        [Test]
        public void DistinctForwardCrossingsNeverTrigger()
        {
            var a = new Step(new(518_000, 499_648, 3300), new(518_000, 499_776, 3300));
            var c = new Step(new(560_000, 548_800, 2300), new(560_000, 548_928, 2300));
            var e = new Step(new(548_928, 590_000, 2400), new(548_800, 590_000, 2400));
            var state = new AutonomousSeamReversalGuard.State();
            Assert.That(AutonomousSeamReversalGuard.Note(ref state, a, SalisburyCamp, 0), Is.EqualTo(0));
            Assert.That(AutonomousSeamReversalGuard.Note(ref state, c, SalisburyCamp, 1_000), Is.EqualTo(0));
            Assert.That(AutonomousSeamReversalGuard.Note(ref state, e, SalisburyCamp, 2_000), Is.EqualTo(0));
        }

        [Test]
        public void LegitimateDeferredLoopThroughDifferentCrossingsNeverTrips()
        {
            // Critic 2026-11-10: the area loop [0>2>0>1] leaves Camelot Hills into Black Mtns. South at one crossing,
            // comes back at another and goes on to Salisbury; it must never count as a reversal.
            var chToBms = new Step(new(548_928, 497_000, 3600), new(548_800, 497_000, 3600));
            var bmsToCh = new Step(new(548_800, 490_000, 3300), new(548_928, 490_000, 3300));
            var chToSal = new Step(new(570_000, 548_800, 2300), new(570_000, 548_928, 2300));
            var state = new AutonomousSeamReversalGuard.State();
            Assert.That(AutonomousSeamReversalGuard.ShouldFail(ref state, chToBms, SalisburyCamp, 0), Is.False);
            Assert.That(state.Reversals, Is.EqualTo(0));
            Assert.That(AutonomousSeamReversalGuard.ShouldFail(ref state, bmsToCh, SalisburyCamp, 20_000), Is.False);
            Assert.That(state.Reversals, Is.EqualTo(0));
            Assert.That(AutonomousSeamReversalGuard.ShouldFail(ref state, chToSal, SalisburyCamp, 50_000), Is.False);
            Assert.That(state.Reversals, Is.EqualTo(0));
            // Back crossing only 200 along from the way in: still a different crossing.
            var nearBack = new Step(new(548_800, 497_200, 3600), new(548_928, 497_200, 3600));
            state = new AutonomousSeamReversalGuard.State();
            AutonomousSeamReversalGuard.Note(ref state, chToBms, SalisburyCamp, 0);
            Assert.That(AutonomousSeamReversalGuard.Note(ref state, nearBack, SalisburyCamp, 1_000), Is.EqualTo(0));
        }
    }
}
