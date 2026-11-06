using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // 2026-11-06 world-bot list: gamebot world behavior and planning cost.
    [TestFixture, NonParallelizable]
    public class UT_WorldBotFixes20261106
    {
        [Test]
        public void AShoppingTripFollowsItsPlanToTheNewMerchant()
        {
            object heorot = new(), mif = new();
            // the plan moved on after Heorot Kenway was skipped
            Assert.That(AutonomousWorldBotController.ShopTripMovedOn(eWorldServiceKind.Shop, heorot, mif), Is.SameAs(mif));
            // same merchant, no plan, no current merchant, or not a shopping trip: nothing changes
            Assert.That(AutonomousWorldBotController.ShopTripMovedOn(eWorldServiceKind.Shop, heorot, heorot), Is.Null);
            Assert.That(AutonomousWorldBotController.ShopTripMovedOn<object>(eWorldServiceKind.Shop, heorot, null), Is.Null);
            Assert.That(AutonomousWorldBotController.ShopTripMovedOn(eWorldServiceKind.Shop, null, mif), Is.Null);
            Assert.That(AutonomousWorldBotController.ShopTripMovedOn(eWorldServiceKind.Vendor, heorot, mif), Is.Null);
        }

        [Test]
        public void AFarMerchantIsWalkedToAndSearchedOnlyWhenNearOrStalled()
        {
            var far = new AutonomousServiceReachability.FarApproach();
            // near: always searched (the failure cache limits repeats)
            Assert.That(far.ShouldSearch("bline", 700, 0), Is.True);
            // far and getting closer: just walk
            Assert.That(far.ShouldSearch("bline", 5_000, 0), Is.False);
            Assert.That(far.ShouldSearch("bline", 4_400, 20_000), Is.False);
            Assert.That(far.ShouldSearch("bline", 4_300, 40_000), Is.False);
            // no progress for 25 s: one search, then wait again
            Assert.That(far.ShouldSearch("bline", 4_300, 45_000), Is.True);
            Assert.That(far.ShouldSearch("bline", 4_300, 46_000), Is.False);
            Assert.That(far.ShouldSearch("bline", 4_300, 70_000), Is.True);
            // another merchant starts over
            Assert.That(far.ShouldSearch("feren", 4_300, 71_000), Is.False);
        }

        [Test]
        public void AFailedServiceSearchIsKeptForTheServiceWhereverTheBotStands()
        {
            Vector3 merchant = new(507_581, 477_378, 0), there = new(503_000, 470_000, 0), moved = new(504_000, 471_000, 0);
            long until = 1_000 + AutonomousWorldBotController.ServiceApproachFailureMilliseconds;
            Assert.That(AutonomousWorldBotController.FailedApproachCached("service:x", merchant, there, until, "service:x", merchant, moved, 20_000), Is.True);
            Assert.That(AutonomousWorldBotController.FailedApproachCached("service:x", merchant, there, until, "service:y", merchant, moved, 20_000), Is.False);
            Assert.That(AutonomousWorldBotController.FailedApproachCached("service:x", merchant, there, until, "service:x", merchant, moved, until), Is.False);
            // the old 3 s cache without a key still needs the same spot
            Assert.That(AutonomousWorldBotController.FailedApproachCached(null, merchant, there, 4_000, null, merchant, there, 2_000), Is.True);
            Assert.That(AutonomousWorldBotController.FailedApproachCached(null, merchant, there, 4_000, null, merchant, moved, 2_000), Is.False);
            Assert.That(AutonomousWorldBotController.FailedApproachCached("service:x", merchant, there, until, null, merchant, there, 2_000), Is.False);
        }

        [Test]
        public void ACampTargetSearchChecksAFewRoutesAndStopsWhenTheFirstFail()
        {
            AutonomousCampTargetRouteChecks checks = default;
            for (int i = 0; i < AutonomousCampTargetRouteChecks.FailuresBeforeStop - 1; i++)
            {
                Assert.That(checks.TryBegin(), Is.True);
                Assert.That(checks.Failed(), Is.False);
            }
            Assert.That(checks.TryBegin(), Is.True);
            Assert.That(checks.Failed(), Is.True, "the third failure in a row repairs the floor once and stops");
            Assert.That(checks.TryBegin(), Is.False);
            Assert.That(checks.Failed(), Is.False, "the repair runs only once");

            // successes in between: still at most six checks
            AutonomousCampTargetRouteChecks capped = default;
            int ran = 0;
            while (capped.TryBegin())
            {
                ran++;
                capped.Succeeded();
            }
            Assert.That(ran, Is.EqualTo(AutonomousCampTargetRouteChecks.MaxChecks));
        }

        [Test]
        public void AnUnreachableCampTargetIsSkippedForAWhileUnlessTheBotOrMonsterMoved()
        {
            var cache = new AutonomousCampTargetRouteChecks.UnreachableTargets();
            Vector3 bot = new(100_000, 100_000, 0), wolf = new(101_000, 100_000, 0);
            cache.Add(7, bot, wolf, 1_000);
            Assert.That(cache.IsCached(7, bot, wolf, 30_000), Is.True);
            Assert.That(cache.IsCached(7, bot + new Vector3(600, 0, 0), wolf, 30_000), Is.False, "the bot walked away");
            Assert.That(cache.IsCached(7, bot, wolf + new Vector3(100, 0, 0), 30_000), Is.False, "the monster moved");
            Assert.That(cache.IsCached(7, bot, wolf, 1_000 + AutonomousCampTargetRouteChecks.UnreachableTargets.Milliseconds), Is.False);
            Assert.That(cache.IsCached(8, bot, wolf, 30_000), Is.False);
            cache.Prune(100_000);
            Assert.That(cache.Count, Is.Zero);
        }

        private static AutonomousBotDecisionEngine.Camp Camp(string id, ConColor con) =>
            new(id, "Zone", id, eRealm.Midgard, 100, con, con, true, false, false, 4, 0, 1);

        [Test]
        public void TheDeathFallbackLooksFartherOutBeforeTakingAYellowCamp()
        {
            var yellowNear = Camp("yellow-near", ConColor.YELLOW);
            var blueFar = Camp("blue-far", ConColor.BLUE);
            var greenRegion = Camp("green-elsewhere", ConColor.GREEN);
            AutonomousBotDecisionEngine.Camp Safest(AutonomousBotDecisionEngine.Camp[] set) =>
                AutonomousBotDecisionEngine.SelectSafestAvailableAfterDeath(set, null, null, new System.Random(1));

            // the nearest ring has only yellow; the second ring has a blue one
            Assert.That(AutonomousDeathRecoveryPolicy.SafestWidening(
                [[yellowNear], [yellowNear, blueFar], [yellowNear, blueFar, greenRegion]], Safest), Is.SameAs(blueFar));
            // empty near rings are skipped
            Assert.That(AutonomousDeathRecoveryPolicy.SafestWidening([[], [], [greenRegion, yellowNear]], Safest), Is.SameAs(greenRegion));
            // yellow everywhere: the nearest yellow, as before
            var yellowFar = Camp("yellow-far", ConColor.YELLOW);
            Assert.That(AutonomousDeathRecoveryPolicy.SafestWidening([[yellowNear], [yellowNear, yellowFar]], Safest), Is.SameAs(yellowNear));
            Assert.That(AutonomousDeathRecoveryPolicy.SafestWidening([[], []], Safest), Is.Null);
        }

        [Test]
        public void ABotThatDiesBesideItsBindThreeTimesInFifteenMinutesBindsElsewhere()
        {
            var deaths = new AutonomousBindDeaths();
            const ushort midgard = 100;
            // two deaths beside the bind, one far away: not yet
            deaths.Record(midgard, 806_000, 702_000, midgard, 805_790, 701_700, 1_000);
            deaths.Record(midgard, 806_600, 704_000, midgard, 805_790, 701_700, 60_000);
            deaths.Record(midgard, 760_000, 650_000, midgard, 805_790, 701_700, 90_000);
            deaths.Record(1, 806_000, 702_000, midgard, 805_790, 701_700, 100_000);
            Assert.That(deaths.ShouldMove(120_000), Is.False);
            deaths.Record(midgard, 805_900, 702_100, midgard, 805_790, 701_700, 120_000);
            Assert.That(deaths.ShouldMove(120_000), Is.True);
            // three deaths spread over more than 15 minutes do not count
            Assert.That(deaths.ShouldMove(1_000 + AutonomousBindDeaths.WindowMilliseconds + 1), Is.False);

            deaths.Moved(midgard, 805_790, 701_700);
            Assert.That(deaths.ShouldMove(120_000), Is.False, "the count starts over");
            Assert.That(deaths.Avoids(midgard, 804_487, 701_766), Is.True, "the stone beside the old bind");
            Assert.That(deaths.Avoids(midgard, 804_518, 724_128), Is.False);
            Assert.That(deaths.Avoids(101, 804_487, 701_766), Is.False);
        }

        [Test]
        public void CampsOfTheSameSpawnsShareTheirBotsAndMonsters()
        {
            const string a = "t1106:202:69:152:haunted driftwood", b = "t1106:202:70:152:haunted driftwood",
                c = "t1106:202:71:153:haunted driftwood", far = "t1106:202:90:152:haunted driftwood",
                other = "t1106:202:70:152:river sprite";
            try
            {
                AutonomousCampCompetition.PublishCatalog([
                    (a, 202, 293_000, 640_000, "haunted driftwood", 2, false),
                    (b, 202, 295_668, 641_600, "Haunted Driftwood", 3, false),
                    (c, 202, 298_000, 643_000, "haunted driftwood", 2, false),
                    (far, 202, 380_000, 640_000, "haunted driftwood", 6, false),
                    (other, 202, 295_700, 641_600, "river sprite", 5, false)]);
                Assert.That(AutonomousCampCompetition.SameSpawns(a, b), Is.True);
                Assert.That(AutonomousCampCompetition.SameSpawns(a, far), Is.False);
                Assert.That(AutonomousCampCompetition.SameSpawns(b, other), Is.False, "another monster does not share");
                Assert.That(AutonomousCampCompetition.SharedLiveMobs(b, 3), Is.EqualTo(7));
                Assert.That(AutonomousCampCompetition.SharedLiveMobs(far, 6), Is.EqualTo(6));

                AutonomousCampCompetition.Occupancy(a);   // first count
                int before = AutonomousCampCompetition.Occupancy(b);
                AutonomousCampCompetition.NoteAssigned(a, 3);
                AutonomousCampCompetition.NoteAssigned(c, 2);
                Assert.That(AutonomousCampCompetition.Occupancy(b) - before, Is.EqualTo(5), "bots at the neighbouring camps count");
                Assert.That(AutonomousCampCompetition.Occupancy(far), Is.Zero);

                // b alone (3 monsters, 0 bots) looked quiet; with its neighbours' 5 bots on 7 monsters it is crowded
                string[] quiet = AutonomousCampCompetition.Quiet([b, far], id => id, id => id == b ? 3 : 6);
                Assert.That(quiet, Is.EqualTo(new[] { far }));
            }
            finally
            {
                AutonomousCampCompetition.PublishCatalog([]);
            }
        }

        [Test]
        public void ACampWhereThreeBotsStallIsBenched()
        {
            AutonomousCampRouteQuarantine.ResetForTests();
            try
            {
                var now = new System.DateTime(2026, 11, 6, 3, 0, 0, System.DateTimeKind.Utc);
                const string camp = "capnbry:201:6:0";
                // one bot stalling over and over is not enough
                Assert.That(AutonomousCampRouteQuarantine.ReportStall(camp, 1, now), Is.False);
                Assert.That(AutonomousCampRouteQuarantine.ReportStall(camp, 1, now.AddMinutes(5)), Is.False);
                Assert.That(AutonomousCampRouteQuarantine.ReportStall(camp, 2, now.AddMinutes(10)), Is.False);
                Assert.That(AutonomousCampRouteQuarantine.IsQuarantined(camp, now.AddMinutes(10)), Is.False);
                Assert.That(AutonomousCampRouteQuarantine.ReportStall(camp, 3, now.AddMinutes(20)), Is.True);
                Assert.That(AutonomousCampRouteQuarantine.IsQuarantined(camp, now.AddMinutes(21)), Is.True);
                // stalls more than 30 minutes apart don't add up
                Assert.That(AutonomousCampRouteQuarantine.ReportStall("other", 1, now), Is.False);
                Assert.That(AutonomousCampRouteQuarantine.ReportStall("other", 2, now.AddMinutes(20)), Is.False);
                Assert.That(AutonomousCampRouteQuarantine.ReportStall("other", 3, now.AddMinutes(45)), Is.False);
            }
            finally
            {
                AutonomousCampRouteQuarantine.ResetForTests();
            }
        }

        [Test]
        public void AMeleePullerThatNeverReachedItsTargetBlamesTheCamp()
        {
            Assert.That(AutonomousCampRouteQuarantine.StallBlamesCamp(true, 900, 150), Is.True);
            Assert.That(AutonomousCampRouteQuarantine.StallBlamesCamp(true, 120, 150), Is.False, "in reach: the bot stalled itself");
            Assert.That(AutonomousCampRouteQuarantine.StallBlamesCamp(false, 900, 150), Is.False, "a caster may just be out of power");
        }

        [Test]
        public void UncoveredSpawnsOfMixedLevelsInOneCampCellBecomeACamp()
        {
            // 19 level 4-5 spraggons spread over 1,500-unit cells: one ordinary 4,200-unit cell holds four of them
            var spawns = new (ushort Zone, ushort Region, int X, int Y, string Name)[]
            {
                (201, 200, 340_300, 551_000, "spraggon"), (201, 200, 341_900, 552_100, "spraggon"),
                (201, 200, 343_500, 553_900, "spraggon"), (201, 200, 344_300, 554_000, "spraggon"),
                (201, 200, 340_400, 551_200, "eirebug"), (201, 200, 341_000, 551_500, "eirebug"),
                (201, 200, 360_000, 551_000, "spraggon"),
            };
            var clusters = AutonomousWorldBotController.MixedLevelClusters(spawns, 4_200, 3);
            Assert.That(clusters, Has.Count.EqualTo(1));
            Assert.That(clusters[0], Is.EquivalentTo(new[] { 0, 1, 2, 3 }));
        }
    }
}
