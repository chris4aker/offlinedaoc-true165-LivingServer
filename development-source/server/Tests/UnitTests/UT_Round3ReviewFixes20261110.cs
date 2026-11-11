using System;
using DOL.AI.Brain;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>Round-3 review fixes (owner 2026-11-10): group camp benches beside solo benches, relic-road defender,
    /// game praise, the learned off-topic switch, the HoT stack log throttle.</summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_Round3ReviewFixes20261110
    {
        private static DateTime Bench(string camp, DateTime at, bool group)
        {
            AutonomousCampRouteQuarantine.ReportFailure(camp, 1, at, group);
            AutonomousCampRouteQuarantine.ReportFailure(camp, 2, at.AddMinutes(1), group);
            Assert.That(AutonomousCampRouteQuarantine.ReportFailure(camp, 3, at.AddMinutes(2), group), Is.True,
                $"three reporters bench {camp} at {at:HH:mm} (group={group})");
            return at.AddMinutes(2);
        }

        [Test]
        public void GroupReportsAreRecordedWhileALongSoloBenchRuns()
        {
            bool saved = AutonomousCampRouteQuarantine.GroupsFixedBench;
            AutonomousCampRouteQuarantine.ResetForTests();
            try
            {
                AutonomousCampRouteQuarantine.GroupsFixedBench = true;
                const string camp = "test:round3:group";
                DateTime now = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
                DateTime first = Bench(camp, now, false);
                DateTime second = Bench(camp, first.AddHours(3), false);
                DateTime third = Bench(camp, second.AddHours(7), false);
                Assert.That(AutonomousCampRouteQuarantine.BenchDuration(camp), Is.EqualTo(TimeSpan.FromHours(24)));

                // groups saw the solo bench end at +2 h; their reports at +3 h must bench the camp for groups again
                DateTime group = Bench(camp, third.AddHours(3), true);
                Assert.Multiple(() =>
                {
                    Assert.That(AutonomousCampRouteQuarantine.IsQuarantined(camp, group.AddHours(1), forGroup: true), Is.True);
                    Assert.That(AutonomousCampRouteQuarantine.IsQuarantined(camp, group.AddHours(2), forGroup: true), Is.False,
                        "a group bench is the fixed 2 h");
                    Assert.That(AutonomousCampRouteQuarantine.IsQuarantined(camp, group.AddHours(2)), Is.True,
                        "the solo 24 h bench is untouched");
                    Assert.That(AutonomousCampRouteQuarantine.BenchDuration(camp), Is.EqualTo(TimeSpan.FromHours(2)),
                        "the log reads the group bench");
                    Assert.That(AutonomousCampRouteQuarantine.IsQuarantined(camp, third.AddHours(24)), Is.False);
                });

                // 20 h after the solo bench ended (39 h after the group one): still a repeat, so the 24 h bench again
                Bench(camp, third.AddHours(44), false);
                Assert.That(AutonomousCampRouteQuarantine.BenchDuration(camp), Is.EqualTo(TimeSpan.FromHours(24)),
                    "a group bench never moves the solo bench end that NextStrike reads");
            }
            finally
            {
                AutonomousCampRouteQuarantine.GroupsFixedBench = saved;
                AutonomousCampRouteQuarantine.ResetForTests();
            }
        }

        [Test]
        public void GroupAndSoloReportsCountApartAndBothBenchesKeepEveryoneAway()
        {
            bool saved = AutonomousCampRouteQuarantine.GroupsFixedBench;
            AutonomousCampRouteQuarantine.ResetForTests();
            try
            {
                AutonomousCampRouteQuarantine.GroupsFixedBench = true;
                DateTime now = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
                const string mixed = "test:round3:mixed";
                AutonomousCampRouteQuarantine.ReportFailure(mixed, 1, now);
                AutonomousCampRouteQuarantine.ReportFailure(mixed, 2, now.AddMinutes(1));
                Assert.That(AutonomousCampRouteQuarantine.ReportFailure(mixed, 3, now.AddMinutes(2), groupReport: true), Is.False,
                    "a grouped bot's give-up adds no solo strike");
                Assert.That(AutonomousCampRouteQuarantine.IsQuarantined(mixed, now.AddMinutes(3)), Is.False);

                const string groupOnly = "test:round3:grouponly";
                DateTime group = Bench(groupOnly, now, true);
                Assert.That(AutonomousCampRouteQuarantine.IsQuarantined(groupOnly, group.AddHours(1)), Is.True,
                    "a group bench keeps solo bots away too");
                Assert.That(AutonomousCampRouteQuarantine.IsQuarantined(groupOnly, group.AddHours(2)), Is.False);
                // the next solo bench is a first strike: the group bench left the strike count alone
                Bench(groupOnly, group.AddHours(3), false);
                Assert.That(AutonomousCampRouteQuarantine.BenchDuration(groupOnly), Is.EqualTo(TimeSpan.FromHours(2)));

                AutonomousCampRouteQuarantine.GroupsFixedBench = false;
                const string off = "test:round3:off";
                DateTime first = Bench(off, now, false);
                Bench(off, first.AddHours(3), true);
                Assert.That(AutonomousCampRouteQuarantine.BenchDuration(off), Is.EqualTo(TimeSpan.FromHours(6)),
                    "switch off: a group report is an ordinary escalating report");
            }
            finally
            {
                AutonomousCampRouteQuarantine.GroupsFixedBench = saved;
                AutonomousCampRouteQuarantine.ResetForTests();
            }
        }

        [Test]
        public void ARelicRoadIsDefendedByTheRealmThatHeldTheRelic()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRvrEventLayer.CarrierDefender(eRealm.Hibernia, "rvr-relic-carrier-2-1"), Is.EqualTo(eRealm.Hibernia));
                Assert.That(AutonomousRvrEventLayer.CarrierDefender(eRealm.None, "rvr-relic-carrier-2-1"), Is.EqualTo(eRealm.Midgard),
                    "nothing saved: the relic's home realm");
                Assert.That(AutonomousRvrEventLayer.CarrierEventRealms(eRealm.Albion,
                        AutonomousRvrEventLayer.CarrierDefender(eRealm.Hibernia, "rvr-relic-carrier-2-1"),
                        new[] { eRealm.Albion, eRealm.Hibernia, eRealm.Midgard }),
                    Is.EqualTo((eRealm.Albion, eRealm.Hibernia)));
            });

            // Hibernia holds Midgard's relic in Dun Dagda and Albion takes it: Hibernia defends the road, not Midgard
            RvrEventTestState.Clear();
            try
            {
                var dunDagda = new AutonomousRvrEventLayer.LiveObjective("rvr-keep-110", "Dun Dagda",
                    AutonomousRvrEventLayer.Intent.AssaultRelicKeep, eRealm.Hibernia, 200, 401036, 465147, 2888, true, 0, 0, 12, 9);
                const string carrier = "rvr-relic-carrier-2-1";
                Assert.That(AutonomousRvrEventLayer.ForceStart(dunDagda, eRealm.Albion, 1_000, out string reason), Is.True, reason);
                AutonomousRvrEventLayer.TransferToRelicCarrier(dunDagda.Id, carrier, 2_000);
                Assert.That(AutonomousRvrEventLayer.EventRealms(carrier), Is.EqualTo((eRealm.Albion, eRealm.Hibernia)));
            }
            finally
            {
                RvrEventTestState.Clear();
            }
        }

        [TestCase("ty awesome job")]
        [TestCase("job well done ty")]
        [TestCase("excellent job on that pull")]
        [TestCase("amazing job")]
        [TestCase("fine job all")]
        public void GamePraiseIsNotRealLifeWork(string message)
        {
            bool saved = AutonomousChatIntentModel.LearnedOffTopicBlocksRescue;
            try
            {
                AutonomousChatIntentModel.LearnedOffTopicBlocksRescue = true;
                Assert.That(AutonomousChatIntentModel.HasOutOfWorldWord(message, true), Is.False);
                Assert.That(AutonomousChatIntentModel.HasOutOfWorldWord("my job starts at nine", true), Is.True);
                if (BanterLibrary165.HasPeriodChatWord(message))
                    Assert.That(BanterLibrary165.PeriodWordRescues(message), Is.True);
            }
            finally
            {
                AutonomousChatIntentModel.LearnedOffTopicBlocksRescue = saved;
            }
        }

        [TestCase("the epic football game")]
        [TestCase("anyone watch the epic football game")]
        [TestCase("anyone see the epic movie")]
        public void TheUnsureOffTopicBranchFollowsItsSwitch(string message)
        {
            bool saved = AutonomousChatIntentModel.LearnedOffTopicBlocksRescue;
            try
            {
                // only the Unknown-reading branch is switched; an IgnoreOutOfWorld reading is the older hard-word rule
                if (AutonomousChatIntentModel.Predict(message) != eAutonomousChatIntent.Unknown)
                    Assert.Pass("the model does not read this as Unknown");
                AutonomousChatIntentModel.LearnedOffTopicBlocksRescue = false;
                Assert.That(BanterLibrary165.ClassifyPlayerMessage(message, false)[0], Is.Not.EqualTo("reply_offtopic"),
                    "switch off: the unsure branch is gone");
                AutonomousChatIntentModel.LearnedOffTopicBlocksRescue = true;
                if (BanterLibrary165.HasPeriodChatWord(message))
                    Assert.That(BanterLibrary165.ClassifyPlayerMessage(message, false)[0], Is.EqualTo("reply_offtopic"));
            }
            finally
            {
                AutonomousChatIntentModel.LearnedOffTopicBlocksRescue = saved;
            }
        }

        [Test]
        public void TheHotStackLineIsKeptOncePerSpellPerHalfHour() => Assert.Multiple(() =>
        {
            const int spell = 990_301;
            long now = 5_000_000_000;
            Assert.That(BotBrain.TakeHotStackLogSlot(spell, now), Is.True);
            Assert.That(BotBrain.TakeHotStackLogSlot(spell, now + 60_000), Is.False, "another bot a minute later: no line");
            Assert.That(BotBrain.TakeHotStackLogSlot(spell + 1, now + 60_000), Is.True, "another spell");
            Assert.That(BotBrain.TakeHotStackLogSlot(spell, now + BotBrain.HotStackLogMilliseconds), Is.True);
        });
    }
}
