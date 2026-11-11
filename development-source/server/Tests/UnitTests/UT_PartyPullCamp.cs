using System;
using System.Collections.Generic;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Party puller camp turn (owner 2026-11-10): grinding parties no longer deadlock at camp.</summary>
    [TestFixture]
    public class UT_PartyPullCamp
    {
        private const int Arrival = 1250;
        private const int Cohesion = 500;

        [TestCase(1300, false, false, false, true, TestName = "Intercept_RunsWhileWalkingIn")]
        [TestCase(900, false, false, false, false, TestName = "Intercept_NeverInsideTheCamp")]
        [TestCase(2100, false, false, false, false, TestName = "Intercept_NotBeyond2000")]
        [TestCase(900, true, false, false, true, TestName = "Intercept_RunsOnDungeonApproach")]
        [TestCase(1300, false, true, false, false, TestName = "Intercept_NeverOnCertifiedDarknessFalls")]
        [TestCase(1300, false, false, true, false, TestName = "Intercept_NotForPullerArrivedWithLeader")]
        public void RunsPreArrivalPullIntercept(int campDistance, bool dungeonApproach, bool certifiedDf,
            bool arrivedWithLeader, bool expected) =>
            Assert.That(AutonomousPartyPullCamp.RunsPreArrivalPullIntercept(campDistance, Arrival, dungeonApproach,
                certifiedDf, arrivedWithLeader), Is.EqualTo(expected));

        [TestCase(1289, 1228, 120, true)]
        [TestCase(1289, 1300, 120, false)]
        [TestCase(1289, 1228, 600, false)]
        [TestCase(1100, 5000, 5000, true)]
        public void PartyPullerHasArrived(int pullerCamp, int leaderCamp, int toLeader, bool expected) =>
            Assert.That(AutonomousPartyPullCamp.PartyPullerHasArrived(pullerCamp, leaderCamp, toLeader, Arrival, Cohesion),
                Is.EqualTo(expected));

        [TestCase(2400f, 1500, 0, false, PartyPullStart.TooFar)]
        [TestCase(1700f, 1500, 0, false, PartyPullStart.Unreachable)]
        [TestCase(1700f, 1500, 1, true, PartyPullStart.Unreachable)]
        [TestCase(1700f, 1500, 2, true, PartyPullStart.UnreachableReleased)]
        [TestCase(1700f, 1500, 2, false, PartyPullStart.Unreachable)]
        [TestCase(1850f, 1500, 0, false, PartyPullStart.Unreachable)]
        [TestCase(1851f, 1500, 0, false, PartyPullStart.TooFar)]
        public void ClassifyNoFiringPoint_KeepsTodaysRules(float distance, int range, int tries, bool same, PartyPullStart expected) =>
            Assert.That(AutonomousDefensivePull.ClassifyNoFiringPoint(distance, range, tries, same), Is.EqualTo(expected));

        [Test]
        public void ReleaseHappensOnTheThirdTryOfTheSameTarget()
        {
            Assert.That(AutonomousDefensivePull.UnreachableTriesBeforeRelease, Is.EqualTo(3));
            int tries = 0;
            var results = new List<PartyPullStart>();
            for (int attempt = 0; attempt < 3; attempt++)
            {
                results.Add(AutonomousDefensivePull.ClassifyNoFiringPoint(1700, 1500, tries, attempt > 0));
                tries = attempt > 0 ? tries + 1 : 1;
            }
            Assert.That(results, Is.EqualTo(new[]
                { PartyPullStart.Unreachable, PartyPullStart.Unreachable, PartyPullStart.UnreachableReleased }));
        }

        // Critic's required test: a party puller with a TooFar target never reaches the ordinary pull
        // (AddToAggroList / BeginSavageMeleePull in WorkCamp), whatever TryBegin's bool says.
        [TestCase(true)]
        [TestCase(false)]
        public void TooFarTarget_NeverReachesTheOrdinaryMeleePull(bool tryBeginReturned)
        {
            Assert.That(AutonomousPartyPullCamp.AfterPartyPull(tryBeginReturned, PartyPullStart.TooFar, true),
                Is.EqualTo(PartyPullFallback.SkipTooFar));
            Assert.That(AutonomousPartyPullCamp.AfterPartyPull(tryBeginReturned, PartyPullStart.TooFar, true),
                Is.Not.EqualTo(PartyPullFallback.OrdinaryPull));
        }

        [Test]
        public void TooFarFromTheRealFiringPointCheck_EndsInASkipNotACharge()
        {
            // The same mesh and distance as UT_AutonomousDefensivePull's "too-far" case.
            var nav = new UT_RealmRaidFormation.Mesh();
            Vector3 origin = new(10000, 10000, 1000), enemy = origin + new Vector3(2000, 0, 0);
            Assert.That(AutonomousDefensivePull.TryFiringPoint(nav, UT_RealmRaidFormation.Zone(), origin, enemy, 1500, out _),
                Is.False);
            PartyPullStart why = AutonomousDefensivePull.ClassifyNoFiringPoint(Vector3.Distance(origin, enemy), 1500, 0, false);
            Assert.That(why, Is.EqualTo(PartyPullStart.TooFar));
            // TryBegin returns false for TooFar; the camp turn skips the target instead of falling through.
            Assert.That(AutonomousPartyPullCamp.AfterPartyPull(false, why, true), Is.EqualTo(PartyPullFallback.SkipTooFar));
        }

        [TestCase(PartyPullStart.Started, true, PartyPullFallback.Handled)]
        [TestCase(PartyPullStart.Waiting, true, PartyPullFallback.Handled)]
        [TestCase(PartyPullStart.Unreachable, true, PartyPullFallback.Handled)]
        [TestCase(PartyPullStart.UnreachableReleased, false, PartyPullFallback.OrdinaryPull)]
        [TestCase(PartyPullStart.ContactFight, false, PartyPullFallback.OrdinaryPull)]
        [TestCase(PartyPullStart.NoRangedPull, false, PartyPullFallback.OrdinaryPull)]
        [TestCase(PartyPullStart.NotApplicable, false, PartyPullFallback.OrdinaryPull)]
        [TestCase(PartyPullStart.TooFar, false, PartyPullFallback.SkipTooFar)]
        public void OnlyTheDesignedCasesCloseIn(PartyPullStart why, bool handled, PartyPullFallback expected) =>
            Assert.That(AutonomousPartyPullCamp.AfterPartyPull(handled, why, true), Is.EqualTo(expected));

        [Test]
        public void EveryOutcomeThatClosesInIsADesignedOne()
        {
            foreach (PartyPullStart why in Enum.GetValues<PartyPullStart>())
                foreach (bool handled in new[] { true, false })
                    if (AutonomousPartyPullCamp.AfterPartyPull(handled, why, true) == PartyPullFallback.OrdinaryPull)
                        Assert.That(why, Is.AnyOf(PartyPullStart.ContactFight, PartyPullStart.NoRangedPull,
                            PartyPullStart.UnreachableReleased, PartyPullStart.NotApplicable));
        }

        [TestCase(true, PartyPullStart.Unreachable, PartyPullFallback.Handled)]
        [TestCase(false, PartyPullStart.TooFar, PartyPullFallback.OrdinaryPull)]
        [TestCase(false, PartyPullStart.UnreachableReleased, PartyPullFallback.OrdinaryPull)]
        public void SwitchOff_KeepsTheOldFallThrough(bool handled, PartyPullStart why, PartyPullFallback expected) =>
            Assert.That(AutonomousPartyPullCamp.AfterPartyPull(handled, why, false), Is.EqualTo(expected));

        [TestCase(44_999, false)]
        [TestCase(45_000, true)]
        public void AdvanceOnlyAfter45SecondsOfTooFar(long ms, bool expected) =>
            Assert.That(AutonomousPartyPullCamp.ShouldAdvanceAfterTooFar(ms), Is.EqualTo(expected));

        [Test]
        public void AtMostTwoAdvancesPerCampPerTenMinutes()
        {
            long now = 1_000_000;
            var ticks = new List<long>();
            Assert.That(AutonomousPartyPullCamp.CanAdvanceCamp(ticks, now), Is.True);
            ticks.Add(now);
            Assert.That(AutonomousPartyPullCamp.CanAdvanceCamp(ticks, now + 1_000), Is.True);
            ticks.Add(now + 1_000);
            Assert.That(AutonomousPartyPullCamp.CanAdvanceCamp(ticks, now + 2_000), Is.False);
            Assert.That(AutonomousPartyPullCamp.CanAdvanceCamp(ticks, now + 10 * 60_000 - 1), Is.False);
            Assert.That(AutonomousPartyPullCamp.CanAdvanceCamp(ticks, now + 10 * 60_000), Is.True);
            Assert.That(ticks.Count, Is.EqualTo(1), "old advances are pruned");
        }

        // Round-3 review (owner 2026-11-10): a party claims its camp target only when a pull really starts.
        [TestCase(PartyPullStart.Started, PartyPullFallback.Handled, true)]
        [TestCase(PartyPullStart.UnreachableReleased, PartyPullFallback.OrdinaryPull, true)]
        [TestCase(PartyPullStart.ContactFight, PartyPullFallback.OrdinaryPull, true)]
        [TestCase(PartyPullStart.NotApplicable, PartyPullFallback.OrdinaryPull, true)]
        [TestCase(PartyPullStart.TooFar, PartyPullFallback.SkipTooFar, false)]
        [TestCase(PartyPullStart.Waiting, PartyPullFallback.Handled, false)]
        [TestCase(PartyPullStart.Unreachable, PartyPullFallback.Handled, false)]
        public void Claim_OnlyWhenAPullStarts(PartyPullStart why, PartyPullFallback next, bool expected) =>
            Assert.That(AutonomousPartyPullCamp.PullStartsOnOutcome(why, next), Is.EqualTo(expected));

        [Test]
        public void Claim_EveryCampTurnOutcomeThatClaimsIsAPullStart()
        {
            foreach (PartyPullStart why in Enum.GetValues<PartyPullStart>())
                foreach (bool handled in new[] { true, false })
                {
                    PartyPullFallback next = AutonomousPartyPullCamp.AfterPartyPull(handled, why, true);
                    if (next == PartyPullFallback.SkipTooFar || why is PartyPullStart.Waiting or PartyPullStart.Unreachable)
                        Assert.That(AutonomousPartyPullCamp.PullStartsOnOutcome(why, next), Is.False, $"{why}/{handled}");
                }
        }

        [TestCase(true, true, true)]
        [TestCase(false, true, false, TestName = "Claim_SoloBotsKeepTheEarlyClaim")]
        [TestCase(true, false, false, TestName = "Claim_SwitchOffKeepsTheEarlyClaim")]
        public void Claim_DeferredOnlyForGroupsWithTheSwitch(bool grouped, bool on, bool expected) =>
            Assert.That(AutonomousPartyPullCamp.DefersCampTargetClaim(grouped, on), Is.EqualTo(expected));

        [Test]
        public void EmptyClock_KeepsRunningThroughTooFarSkips()
        {
            Assert.That(AutonomousPartyPullCamp.EmptyClockAfterTooFarSkip(0, 500_000), Is.EqualTo(500_000));
            Assert.That(AutonomousPartyPullCamp.EmptyClockAfterTooFarSkip(420_000, 500_000), Is.EqualTo(420_000));
        }

        /// <summary>
        /// One too-far copy, re-found every 60 s, empty turns between: the empty clock reaches 75 s (and 180 s)
        /// instead of restarting at each re-found skip (review: the 60 s skip was shorter than the 75 s limit).
        /// </summary>
        [Test]
        public void EmptyClock_OneTooFarTargetStillReachesTheEmptyCampFlow()
        {
            const long t0 = 1_000_000;
            long emptySince = 0;
            bool reported = false, repositioned = false;
            for (long now = t0; now <= t0 + 200_000; now += 1_000)
            {
                bool skipTurn = (now - t0) % AutonomousPartyPullCamp.TooFarSkipMilliseconds == 0;
                if (skipTurn)
                {
                    long before = emptySince;
                    emptySince = 0;   // WorkCamp's target branch clears it ...
                    emptySince = AutonomousPartyPullCamp.EmptyClockAfterTooFarSkip(before, now);   // ... the skip restores it
                    continue;
                }
                emptySince = emptySince == 0 ? now : emptySince;
                reported |= now - emptySince >= 75_000;
                repositioned |= now - emptySince >= 180_000;
            }
            Assert.That(reported, Is.True);
            Assert.That(repositioned, Is.True);
        }

        [Test]
        public void TooFarEpisode_OnlyConsecutiveSkipsCount()
        {
            const long t = 2_000_000;
            Assert.That(AutonomousPartyPullCamp.TooFarEpisodeContinues(t - 30_000, true, t - 1_000, t - 90_000, t), Is.True);
            // the same target re-found after its 60 s skip still continues the episode
            Assert.That(AutonomousPartyPullCamp.TooFarEpisodeContinues(t - 60_000, true, t - 60_000, 0, t), Is.True);
            Assert.That(AutonomousPartyPullCamp.TooFarEpisodeContinues(0, true, t - 1_000, 0, t), Is.False, "cleared clock");
            Assert.That(AutonomousPartyPullCamp.TooFarEpisodeContinues(t - 30_000, false, t - 1_000, 0, t), Is.False, "new camp");
            Assert.That(AutonomousPartyPullCamp.TooFarEpisodeContinues(t - 30_000, true, t - 10_000, t - 5_000, t), Is.False,
                "group combat since the last skip");
            Assert.That(AutonomousPartyPullCamp.TooFarEpisodeContinues(t - 300_000, true, t - 300_000, 0, t), Is.False,
                "five minutes of something else (review example) never advances on the first new skip");
        }

        [Test]
        public void TooFarEpisode_EmptyTurnEndsItOnlyWhenNoSkipIsPending()
        {
            Assert.That(AutonomousPartyPullCamp.EmptyTurnEndsTooFarEpisode(100_000, 99_999), Is.False);
            Assert.That(AutonomousPartyPullCamp.EmptyTurnEndsTooFarEpisode(100_000, 100_000), Is.True);
        }

        [Test]
        public void TooFarLog_AtMostOncePerThreeMinutes()
        {
            Assert.That(AutonomousPartyPullCamp.ShouldLogTooFar(0, 5_000), Is.True);
            Assert.That(AutonomousPartyPullCamp.ShouldLogTooFar(5_000, 5_000 + 3 * 60_000 - 1), Is.False);
            Assert.That(AutonomousPartyPullCamp.ShouldLogTooFar(5_000, 5_000 + 3 * 60_000), Is.True);
        }

        [TestCase(5 * 60_000 + 59_000, PullStallAction.None)]
        [TestCase(6 * 60_000, PullStallAction.Log)]
        [TestCase(10 * 60_000 + 59_000, PullStallAction.Log)]
        [TestCase(11 * 60_000, PullStallAction.End)]
        public void PullStallStep(long ms, PullStallAction expected) =>
            Assert.That(AutonomousPartyPullCamp.PullStallStep(ms), Is.EqualTo(expected));

        [Test]
        public void StallClock_RunsWhileGrindingAndThroughCampMoves()
        {
            const long t0 = 100_000;
            long since = AutonomousPartyPullCamp.NextStallClock(0, "Grinding", false, false, 0, t0);
            Assert.That(since, Is.EqualTo(t0), "arrival starts the clock");
            Assert.That(AutonomousPartyPullCamp.NextStallClock(since, "Grinding", true, false, 1_000, t0 + 1_000), Is.EqualTo(t0));
            // an advance or empty-camp reposition is "Traveling" with the same camp: not progress
            Assert.That(AutonomousPartyPullCamp.NextStallClock(since, "Traveling", true, false, 1_000, t0 + 2_000), Is.EqualTo(t0));
            Assert.That(AutonomousPartyPullCamp.NextStallClock(since, "Grinding", true, true, 1_000, t0 + 3_000), Is.EqualTo(t0 + 3_000),
                "a fight or experience restarts it");
            Assert.That(AutonomousPartyPullCamp.NextStallClock(since, "Grinding", false, false, 1_000, t0 + 4_000), Is.EqualTo(t0 + 4_000),
                "a new camp restarts it");
            Assert.That(AutonomousPartyPullCamp.NextStallClock(since, "Grinding", true, false, 31_000, t0 + 40_000), Is.EqualTo(t0 + 40_000),
                "an unwatched gap restarts it");
        }

        [TestCase("Traveling", 0L)]
        [TestCase("Waiting for resurrection", 100_000L)]
        [TestCase("Regrouping", 100_000L)]
        [TestCase("Choosing group target", 100_000L)]
        [TestCase("Meeting up", 100_000L)]
        public void StallClock_StopsOutsideGrinding(string phase, long since) =>
            Assert.That(AutonomousPartyPullCamp.NextStallClock(since, phase, true, false, 1_000, 200_000), Is.EqualTo(0));

        [Test]
        public void ResourceGate_NamesMemberAndPercentages()
        {
            Assert.That(AutonomousPartyPullCamp.ResourceGate("Fergalan", 85, 40, 100, true, false),
                Is.EqualTo("resources:Fergalan 85/40/100"));
            Assert.That(AutonomousPartyPullCamp.ResourceGate("Ragarmund", 50, 0, 100, false, false),
                Is.EqualTo("resources:Ragarmund 50/-/100"));
            Assert.That(AutonomousPartyPullCamp.ResourceGate("Niainna", 95, 90, 90, true, false), Is.Null);
            // True 1.65 chain pull: 90% health, 80% power, endurance above the rest threshold
            Assert.That(AutonomousPartyPullCamp.ResourceGate("Niainna", 95, 85, 50, true, true), Is.Null);
            Assert.That(AutonomousPartyPullCamp.ResourceGate("Niainna", 95, 85, 50, true, false),
                Is.EqualTo("resources:Niainna 95/85/50"));
        }

        [TestCase(true, true, 100, "riding:A")]
        [TestCase(false, false, 100, "region:A")]
        [TestCase(false, true, 812, "cohesion:A 812")]
        [TestCase(false, true, 500, null)]
        [TestCase(true, false, 9000, "riding:A")]
        public void PlacementGate_InTheGatesOrder(bool riding, bool sameRegion, int distance, string expected) =>
            Assert.That(AutonomousPartyPullCamp.PlacementGate("A", riding, sameRegion, distance, Cohesion), Is.EqualTo(expected));

        [Test]
        public void SwitchesDefaultOn()
        {
            Assert.That(AutonomousPartyPullCamp.CampTurn, Is.True);
            Assert.That(AutonomousPartyPullCamp.TooFarAdvance, Is.True);
            Assert.That(AutonomousPartyPullCamp.PullerWaitsAtEmptyCamp, Is.True);
            Assert.That(AutonomousPartyPullCamp.StallLog, Is.True);
            Assert.That(AutonomousPartyPullCamp.StallEnd, Is.True);
            Assert.That(AutonomousPartyPullCamp.ClaimOnPullStart, Is.True);
            Assert.That(AutonomousPartyPullCamp.TooFarCountsAsEmpty, Is.True);
            Assert.That(AutonomousBotGroupCoordinator.PartyCohesionRadius, Is.EqualTo(Cohesion));
        }
    }
}
