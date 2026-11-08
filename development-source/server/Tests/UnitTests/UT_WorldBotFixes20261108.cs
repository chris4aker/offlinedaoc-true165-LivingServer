using System;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Owner 2026-11-08: backpack/Realm Exchange loop, empty camps, road-death detours.</summary>
    [TestFixture]
    public class UT_WorldBotFixes20261108
    {
        [Test]
        public void ExchangeGoodsAreKeptUnlessNoBrokerIsInReachAndTheBagIsNearlyFull()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousServiceFallback.KeepsForExchange(true, false, true), Is.True);
                Assert.That(AutonomousServiceFallback.KeepsForExchange(true, true, false), Is.True, "room left: keep them");
                Assert.That(AutonomousServiceFallback.KeepsForExchange(true, true, true), Is.False);
                Assert.That(AutonomousServiceFallback.KeepsForExchange(false, false, false), Is.False);
            });
        }

        [Test]
        public void AnUnreachableBrokerIsRememberedPerBotForTwentyMinutes()
        {
            AutonomousServiceFallback.Clear();
            try
            {
                AutonomousServiceFallback.MarkExchangeUnreachable(7, 1_000);
                Assert.That(AutonomousServiceFallback.IsExchangeUnreachable(7, 2_000), Is.True);
                Assert.That(AutonomousServiceFallback.IsExchangeUnreachable(8, 2_000), Is.False, "per bot only");
                Assert.That(AutonomousServiceFallback.IsExchangeUnreachable(7,
                    1_000 + AutonomousServiceFallback.ExchangeUnreachableMilliseconds), Is.False);
            }
            finally
            {
                AutonomousServiceFallback.Clear();
            }
        }

        [Test]
        public void AFullBagOrACostlyDeathEndsASoloHuntAfterItsFirstTenMinutes()
        {
            TimeSpan hour = TimeSpan.FromMinutes(60), early = TimeSpan.FromMinutes(5);
            long grace = AutonomousObjectiveAssignments.FullBackpackGraceMilliseconds;
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousObjectiveAssignments.ShouldEndHuntToUnload(true, true, hour, grace, false, false, false), Is.True);
                Assert.That(AutonomousObjectiveAssignments.ShouldEndHuntToUnload(true, true, hour, grace - 1, false, false, false), Is.False,
                    "a moment's full bag is not yet a reason");
                Assert.That(AutonomousObjectiveAssignments.ShouldEndHuntToUnload(true, true, early, grace, false, false, false), Is.False,
                    "never in the first ten minutes of a hunt");
                Assert.That(AutonomousObjectiveAssignments.ShouldEndHuntToUnload(false, true, hour, grace, false, false, false), Is.False,
                    "parties keep their own timer");
                Assert.That(AutonomousObjectiveAssignments.ShouldEndHuntToUnload(true, true, hour, null, true, true, false), Is.True,
                    "a death that cost Constitution worth buying back goes to the healer");
                Assert.That(AutonomousObjectiveAssignments.ShouldEndHuntToUnload(true, true, hour, null, true, false, true), Is.True,
                    "a death with a nearly full bag goes to sell");
                Assert.That(AutonomousObjectiveAssignments.ShouldEndHuntToUnload(true, true, hour, null, true, false, false), Is.False,
                    "an ordinary death keeps hunting");
                Assert.That(AutonomousObjectiveAssignments.ShouldEndHuntToUnload(true, true, hour, null, false, true, true), Is.False);
            });
        }

        [Test]
        public void EmptyCampMemoryRetiresASpotThenForgetsIt()
        {
            AutonomousCampEmptyMemory.Clear();
            try
            {
                string toads = AutonomousCampEmptyMemory.Key(200, "Hill Toad", 334_406, 589_584);
                Assert.That(AutonomousCampEmptyMemory.Key(200, "hill toad", 334_900, 589_100), Is.EqualTo(toads), "same cell, any id");
                for (int i = 0; i < 4; i++)
                    AutonomousCampEmptyMemory.RecordEmpty(toads, 0);
                Assert.That(AutonomousCampEmptyMemory.IsRetired(toads, 0), Is.True);
                Assert.That(AutonomousCampEmptyMemory.Factor(toads, 0), Is.EqualTo(0.2).Within(1e-9));
                long halfLife = (long)(AutonomousCampEmptyMemory.HalfLifeMinutes * 60_000);
                Assert.That(AutonomousCampEmptyMemory.CurrentScore(toads, halfLife), Is.EqualTo(2).Within(1e-9), "halves every half-life");
                Assert.That(AutonomousCampEmptyMemory.IsRetired(toads, halfLife), Is.False);

                string beetles = AutonomousCampEmptyMemory.Key(200, "water beetle", 100_000, 100_000);
                for (int i = 0; i < 4; i++)
                    AutonomousCampEmptyMemory.RecordEmpty(beetles, 0);
                for (int i = 0; i < 8; i++)
                    AutonomousCampEmptyMemory.RecordKill(beetles, 0);
                Assert.That(AutonomousCampEmptyMemory.CurrentScore(beetles, 0), Is.EqualTo(2).Within(1e-9), "kills offset empty hunts");
                Assert.That(AutonomousCampEmptyMemory.Factor(AutonomousCampEmptyMemory.Key(1, "adder", 0, 0), 0), Is.EqualTo(1));
            }
            finally
            {
                AutonomousCampEmptyMemory.Clear();
            }
        }

        [Test]
        public void OverlappingCampsOfOneMonsterMergeIntoTheBestSeed()
        {
            AutonomousCampDedupe.Candidate C(string id, int x, int y, int mobs, string name = "hill toad", bool mergeable = true) =>
                new(200, name, x, y, AutonomousCampDedupe.SourcePriority(id), mobs, mergeable, id);
            var camps = new[]
            {
                C("audited-live:201:hill toad:7:222:393", 334_406, 589_584, 2),
                C("audited-live:201:hill toad:6:223:393", 335_300, 589_900, 3),
                C("capnbry:200:7:9:hill toad", 333_800, 588_700, 1),
                C("audited-live:201:hill toad:8:222:390", 333_806, 585_765, 2),   // 2,900 from the seed: its own camp
                C("audited-live:201:feccan:7:222:393", 334_400, 589_600, 4, "feccan"),
                C("dungeon-live:x", 334_410, 589_590, 9, mergeable: false),
            };
            int[] seed = AutonomousCampDedupe.SeedOf(camps);
            Assert.Multiple(() =>
            {
                Assert.That(seed[0], Is.EqualTo(2), "the authored camp is the seed");
                Assert.That(seed[1], Is.EqualTo(2));
                Assert.That(seed[2], Is.EqualTo(2));
                Assert.That(seed[3], Is.EqualTo(3), "farther than the merge radius");
                Assert.That(seed[4], Is.EqualTo(4), "another monster never merges");
                Assert.That(seed[5], Is.EqualTo(5), "not a mergeable source");
                Assert.That(seed.Distinct().Count(), Is.EqualTo(4));
            });
        }

        [Test]
        public void ACampAmongMuchStrongerAggressiveMonstersIsOnlyForBotsItIsSafeFor()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousCampThreatPolicy.IsDangerousFor(35, 8, 8, 1), Is.True, "spore seeds beside cniogcrag cutters");
                Assert.That(AutonomousCampThreatPolicy.IsDangerousFor(35, 8, 33, 1), Is.False, "a bot the cutters can't out-level");
                Assert.That(AutonomousCampThreatPolicy.IsDangerousFor(17, 8, 8, 1), Is.False, "under ten levels above the camp");
                Assert.That(AutonomousCampThreatPolicy.IsDangerousFor(30, 20, 26, 1), Is.True);
                Assert.That(AutonomousCampThreatPolicy.IsDangerousFor(30, 20, 26, 8), Is.False, "a full party copes");
                Assert.That(AutonomousCampThreatPolicy.IsDangerousFor(0, 8, 8, 1), Is.False);
            });
        }

        [Test]
        public void TheDrawSharesAMonstersWeightOverItsCampsAndShunsEmptyAndCrowdedSpots()
        {
            int alone = AutonomousCampSpread.Weight(6, 4, 0, 1, 1);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousCampSpread.Weight(6, 4, 0, 16, 1), Is.EqualTo(alone / 4), "16 camps of one monster share");
                Assert.That(AutonomousCampSpread.Weight(6, 4, 3, 1, 1), Is.EqualTo(alone / 4), "three bots already there");
                Assert.That(AutonomousCampSpread.Weight(6, 4, 0, 1, 0.2), Is.EqualTo(alone / 5), "a spot found empty");
                Assert.That(AutonomousCampSpread.Weight(1, 1, 50, 100, 0), Is.EqualTo(1), "never zero");
            });
        }

        [Test]
        public void BotsOnARetiredCampIdStillCountAtItsSpotAndPoolsAreNotAddedTwice()
        {
            const string old = "t1108:audited-live:201:hill toad:7:222:393", now = "t1108:audited-live:201:hill toad:6:223:393",
                far = "t1108:audited-live:201:hill toad:6:250:393";
            try
            {
                AutonomousCampCompetition.PublishCatalogWithPools([
                    (old, 200, 334_406, 589_584, "hill toad", 2, false, 13),
                    (now, 200, 335_300, 589_900, "hill toad", 3, false, 13),
                    (far, 200, 375_000, 589_900, "hill toad", 3, false, 4)]);
                // the next catalog no longer has the old id (its toads were dead at the capture)
                AutonomousCampCompetition.PublishCatalogWithPools([
                    (now, 200, 335_300, 589_900, "hill toad", 3, false, 13),
                    (far, 200, 375_000, 589_900, "hill toad", 3, false, 4)]);
                Assert.That(AutonomousCampCompetition.TryGetSpot(old, out _), Is.True, "a recent id keeps its spot");
                int before = AutonomousCampCompetition.Occupancy(now);
                AutonomousCampCompetition.NoteAssigned(old, 5);
                Assert.Multiple(() =>
                {
                    Assert.That(AutonomousCampCompetition.Occupancy(now) - before, Is.EqualTo(5),
                        "bots holding the retired id compete for the same toads");
                    Assert.That(AutonomousCampCompetition.Occupancy(far), Is.Zero);
                    Assert.That(AutonomousCampCompetition.SameSpawns(old, now), Is.True);
                    Assert.That(AutonomousCampCompetition.SharedLiveMobs(now, 3), Is.EqualTo(13), "the pool, not 2+3+... again");
                    Assert.That(AutonomousCampCompetition.Crowded(before + 5 + 2, 13), Is.True);
                });
            }
            finally
            {
                AutonomousCampCompetition.PublishCatalog([]);
            }
        }

        [Test]
        public void ASoloBotGoesAroundAPackOrWhereItJustDiedInsteadOfPulling()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRouteThreatPolicy.PreferDetour(RouteThreatAction.Pull, false, 2, false, true, 0),
                    Is.EqualTo(RouteThreatAction.Detour), "a pack of pixie scouts");
                Assert.That(AutonomousRouteThreatPolicy.PreferDetour(RouteThreatAction.Pull, false, 1, true, true, 0),
                    Is.EqualTo(RouteThreatAction.Detour), "the monster that killed it minutes ago");
                Assert.That(AutonomousRouteThreatPolicy.PreferDetour(RouteThreatAction.Pull, false, 1, false, true, 0),
                    Is.EqualTo(RouteThreatAction.Pull), "a lone even fight is still pulled");
                Assert.That(AutonomousRouteThreatPolicy.PreferDetour(RouteThreatAction.Pull, false, 3, true, false, 0),
                    Is.EqualTo(RouteThreatAction.Pull), "no way around: the ordinary decision stands");
                Assert.That(AutonomousRouteThreatPolicy.PreferDetour(RouteThreatAction.Pull, false, 3, false, true,
                    AutonomousRouteThreatPolicy.MaxDetoursPerThreat), Is.EqualTo(RouteThreatAction.Pull), "detours used up");
                Assert.That(AutonomousRouteThreatPolicy.PreferDetour(RouteThreatAction.Pull, true, 3, true, true, 0),
                    Is.EqualTo(RouteThreatAction.Pull), "parties keep their rules");
                Assert.That(AutonomousRouteThreatPolicy.PreferDetour(RouteThreatAction.Retarget, false, 3, true, true, 0),
                    Is.EqualTo(RouteThreatAction.Retarget));
            });
        }
        [Test]
        public void TheFullBagClockKeepsTheFirstRefusalAndStartsOverAfterAnUnload()
        {
            const long bot = 99_108_001;
            AutonomousBotEconomy.ClearBackpackFull(bot);
            try
            {
                AutonomousBotEconomy.NoteBackpackFull(bot, 1_000);
                AutonomousBotEconomy.NoteBackpackFull(bot, 5_000);
                Assert.That(AutonomousBotEconomy.BackpackFullSinceTick(bot), Is.EqualTo(1_000), "later refusals keep the first tick");
                AutonomousBotEconomy.ClearBackpackFull(bot);   // a vendor sale or the end of the errands
                Assert.That(AutonomousBotEconomy.BackpackFullSinceTick(bot), Is.Null,
                    "a bag left with a few free slots does not end the next hunt without a fresh refusal");
                AutonomousBotEconomy.NoteBackpackFull(bot, 900_000);
                Assert.That(AutonomousBotEconomy.BackpackFullSinceTick(bot), Is.EqualTo(900_000));
            }
            finally
            {
                AutonomousBotEconomy.ClearBackpackFull(bot);
            }
        }

        [Test]
        public void OnlySoloTasksEndEarlyToUnloadOrShop()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousObjectiveAssignments.IsSoloHunt("SoloPve", false), Is.True);
                Assert.That(AutonomousObjectiveAssignments.IsSoloHunt("GroupPve", false), Is.False,
                    "grinding while it waits for a party keeps the group task");
                Assert.That(AutonomousObjectiveAssignments.IsSoloHunt("Battleground", false), Is.False);
                Assert.That(AutonomousObjectiveAssignments.IsSoloHunt("SoloPve", true), Is.False);
            });
        }

        [Test]
        public void LeavingACrowdedCampEarlyIsNotAnEmptyHunt()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousCampEmptyMemory.CountsAsEmpty(15_000, 75_000), Is.False, "the crowded 15 s abandon");
                Assert.That(AutonomousCampEmptyMemory.CountsAsEmpty(75_000, 75_000), Is.True);
            });
            AutonomousCampEmptyMemory.Clear();
            try
            {
                string spot = AutonomousCampEmptyMemory.Key(1, "adder", 10, 10);
                AutonomousCampEmptyMemory.RecordKill(spot, 0);
                Assert.That(AutonomousCampEmptyMemory.CurrentScore(spot, 0), Is.Zero, "kills alone keep no entry");
            }
            finally
            {
                AutonomousCampEmptyMemory.Clear();
            }
        }

        [Test]
        public void ACampWithItsOwnRuleIsNeverMerged()
        {
            const string spirit = "capnbry:1:1:3:spirit";
            Assert.That(AutonomousCampDedupe.IsMergeableSource(spirit), Is.False);
            Assert.That(AutonomousCampDedupe.IsMergeableSource("capnbry:1:1:4:spirit"), Is.True);
            var camps = new[]
            {
                new AutonomousCampDedupe.Candidate(1, "spirit", 500_000, 600_000, 3, 9, AutonomousCampDedupe.IsMergeableSource(spirit), spirit),
                new AutonomousCampDedupe.Candidate(1, "spirit", 500_500, 600_000, 2, 2, true, "audited-live:1:spirit:3:1:1"),
            };
            Assert.That(AutonomousCampDedupe.SeedOf(camps), Is.EqualTo(new[] { 0, 1 }));
        }
    }
}
