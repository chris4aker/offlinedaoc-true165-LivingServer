using System;
using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>Round-2 review fixes (owner 2026-11-10): HoT conflict rule, relic-road realms, group camp bench,
    /// inventory reinsert outcome, learned off-topic words.</summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_Round2ReviewFixes20261110
    {
        private static Spell Regen(int id, string type, int effectGroup, double value, int concentration = 0) =>
            new(new DbSpell { SpellID = id, Type = type, Target = "Realm", Duration = 20, Frequency = 30, Value = value,
                EffectGroup = effectGroup, Concentration = concentration }, 1);

        [Test]
        public void ASongOfRestNeverBlocksAShamanRegen() => Assert.Multiple(() =>
        {
            // opendaoc-165 data: Skald Song of Rest = HealthRegenBuff, EffectGroup 72; Shaman Frigg's = EffectGroup 71
            Spell friggs = Regen(1, "HealthRegenBuff", 71, 30);
            Spell heavenlySong = Regen(2, "HealthRegenBuff", 72, 42);
            Assert.That(BotHotPrecheck.Conflicts(friggs, heavenlySong), Is.False, "groups 71 and 72 stack on the server");
            Assert.That(BotHotPrecheck.PresentBlocksHot(friggs, heavenlySong, 30, 42, false), Is.False,
                "a stronger song of another effect group does not hold the regen back");
            Assert.That(BotHotPrecheck.PresentBlocksHot(friggs, Regen(3, "HealthRegenBuff", 71, 39), 30, 39, false), Is.True,
                "a stronger regen of the same group does");
            Assert.That(BotHotPrecheck.PresentBlocksHot(friggs, Regen(4, "HealthRegenBuff", 71, 20), 30, 20, false), Is.False,
                "a weaker one of the same group does not");
            Assert.That(BotHotPrecheck.PresentBlocksHot(friggs, Regen(1, "HealthRegenBuff", 71, 30), 30, 10, false), Is.True,
                "the same spell always does");
            Assert.That(BotHotPrecheck.PresentBlocksHot(friggs, null, 30, 0, false), Is.True, "an effect with no spell is kept as a block");
            Assert.That(BotHotPrecheck.PresentBlocksHot(friggs, Regen(5, "HealthRegenBuff", 71, 5), 30, 5, true), Is.True,
                "two concentration effects never contest");
        });

        [Test]
        public void WithNoEffectGroupTheSpellTypeDecides() => Assert.Multiple(() =>
        {
            Assert.That(BotHotPrecheck.Conflicts(Regen(1, "HealOverTime", 0, 30), Regen(2, "HealOverTime", 0, 50)), Is.True);
            Assert.That(BotHotPrecheck.Conflicts(Regen(1, "HealOverTime", 0, 30), Regen(2, "HealthRegenBuff", 0, 50)), Is.False);
            Assert.That(BotHotPrecheck.Conflicts(Regen(1, "HealOverTime", 0, 30), Regen(2, "HealOverTime", 9, 50)), Is.False,
                "one with an effect group: the groups decide");
            Assert.That(BotHotPrecheck.Conflicts(Regen(1, "HealOverTime", 0, 30), null), Is.False);
        });

        [Test]
        public void OnARelicRoadTheCarrierAttacksAndTheOwnerDefends() => Assert.Multiple(() =>
        {
            string id = "rvr-relic-carrier-1-0"; // an Albion relic
            Assert.That(AutonomousRvrEventLayer.RelicHomeOfCarrierId(id), Is.EqualTo(eRealm.Albion));
            Assert.That(AutonomousRvrEventLayer.RelicHomeOfCarrierId("rvr-relic-carrier-3-1"), Is.EqualTo(eRealm.Hibernia));
            Assert.That(AutonomousRvrEventLayer.RelicHomeOfCarrierId("keep-12"), Is.EqualTo(eRealm.None));
            Assert.That(AutonomousRvrEventLayer.RelicHomeOfCarrierId(null), Is.EqualTo(eRealm.None));

            // Midgard carries Albion's relic; Midgard is not first in the participants
            var realms = AutonomousRvrEventLayer.CarrierEventRealms(eRealm.Midgard, eRealm.Albion,
                new[] { eRealm.Hibernia, eRealm.Albion, eRealm.Midgard });
            Assert.That(realms, Is.EqualTo((eRealm.Midgard, eRealm.Albion)));
            Assert.That(BanterLibrary165.EnemyOf(eRealm.Hibernia, realms.Attacker, realms.Defender), Is.EqualTo(eRealm.Albion),
                "a third-realm bot faces the relic's owner");
            Assert.That(BanterLibrary165.EnemyOf(eRealm.Albion, realms.Attacker, realms.Defender), Is.EqualTo(eRealm.Midgard));

            // Albion takes its own relic home from Midgard's keep: the other side defends
            var home = AutonomousRvrEventLayer.CarrierEventRealms(eRealm.Albion, eRealm.Albion,
                new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia });
            Assert.That(home, Is.EqualTo((eRealm.Albion, eRealm.Midgard)));

            Assert.That(AutonomousRvrEventLayer.CarrierEventRealms(eRealm.None, eRealm.Albion, new[] { eRealm.Albion, eRealm.Hibernia }),
                Is.EqualTo((eRealm.Hibernia, eRealm.Albion)), "no known carrier: the first other realm");
        });

        [Test]
        public void GroupReportsBenchACampForTheFixedTimeOnly()
        {
            bool saved = AutonomousCampRouteQuarantine.GroupsFixedBench;
            AutonomousCampRouteQuarantine.ResetForTests();
            try
            {
                AutonomousCampRouteQuarantine.GroupsFixedBench = true;
                const string camp = "test:review:group";
                DateTime now = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
                DateTime Bench(DateTime at, bool group)
                {
                    AutonomousCampRouteQuarantine.ReportFailure(camp, 1, at, group);
                    AutonomousCampRouteQuarantine.ReportFailure(camp, 2, at.AddMinutes(1), group);
                    Assert.That(AutonomousCampRouteQuarantine.ReportFailure(camp, 3, at.AddMinutes(2), group), Is.True);
                    return at.AddMinutes(2);
                }

                DateTime first = Bench(now, false);
                DateTime second = Bench(first.AddHours(3), false);
                Assert.That(AutonomousCampRouteQuarantine.BenchDuration(camp), Is.EqualTo(TimeSpan.FromHours(6)), "solo repeats escalate");
                Assert.That(AutonomousCampRouteQuarantine.IsQuarantined(camp, second.AddHours(5)), Is.True, "solo bots see 6 h");
                Assert.That(AutonomousCampRouteQuarantine.IsQuarantined(camp, second.AddHours(5), forGroup: true), Is.False,
                    "groups see at most the fixed 2 h");
                Assert.That(AutonomousCampRouteQuarantine.IsQuarantined(camp, second.AddHours(1), forGroup: true), Is.True);

                DateTime group = Bench(second.AddHours(7), true);
                Assert.That(AutonomousCampRouteQuarantine.BenchDuration(camp), Is.EqualTo(TimeSpan.FromHours(2)),
                    "a group bench is 2 h even after solo strikes, and the log reads it");
                Assert.That(AutonomousCampRouteQuarantine.IsQuarantined(camp, group.AddHours(2)), Is.False);

                AutonomousCampRouteQuarantine.GroupsFixedBench = false;
                Assert.That(AutonomousCampRouteQuarantine.BenchEndFor(now, now.AddHours(6), true, false), Is.EqualTo(now.AddHours(6)),
                    "switch off: groups see the whole bench");
                Assert.That(AutonomousCampRouteQuarantine.BenchEndFor(now, now.AddHours(6), true, true), Is.EqualTo(now.AddHours(2)));
                Assert.That(AutonomousCampRouteQuarantine.BenchEndFor(now, now.AddHours(6), false, true), Is.EqualTo(now.AddHours(6)));
                Assert.That(AutonomousCampRouteQuarantine.BenchEndFor(default, now, true, true), Is.EqualTo(now));
            }
            finally
            {
                AutonomousCampRouteQuarantine.GroupsFixedBench = saved;
                AutonomousCampRouteQuarantine.ResetForTests();
            }
        }

        [Test]
        public void AFailedUpdateIsRecoveredOnlyByAMissingRow() => Assert.Multiple(() =>
        {
            Assert.That(BotInventory.MissingRowsExplainFailedUpdate(1, false), Is.True);
            Assert.That(BotInventory.MissingRowsExplainFailedUpdate(0, false), Is.False,
                "no dirty row left (relation save failed, commit rolled back): still a failure");
            Assert.That(BotInventory.MissingRowsExplainFailedUpdate(2, true), Is.False, "another row failed too");
        });

        [TestCase("anyone see the epic movie", false)]
        [TestCase("rally at school tomorrow", false)]
        [TestCase("horn on tv", false)]
        [TestCase("good job ty", true)]
        [TestCase("ty", true)]
        public void LearnedOffTopicWordsAreNeverRescued(string message, bool expected)
        {
            bool saved = AutonomousChatIntentModel.LearnedOffTopicBlocksRescue;
            try
            {
                AutonomousChatIntentModel.LearnedOffTopicBlocksRescue = true;
                Assert.That(BanterLibrary165.PeriodWordRescues(message), Is.EqualTo(expected));
            }
            finally
            {
                AutonomousChatIntentModel.LearnedOffTopicBlocksRescue = saved;
            }
        }

        [Test]
        public void TheLearnedListFollowsItsSwitch() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousChatIntentModel.HasOutOfWorldWord("the epic movie", true), Is.True);
            Assert.That(AutonomousChatIntentModel.HasOutOfWorldWord("the epic movie", false), Is.False);
            Assert.That(AutonomousChatIntentModel.HasOutOfWorldWord("the epic football game", false), Is.True, "hard words always");
            Assert.That(AutonomousChatIntentModel.HasOutOfWorldWord("nice job on that pull", true), Is.False, "game praise");
            Assert.That(AutonomousChatIntentModel.HasOutOfWorldWord("my job starts at nine", true), Is.True);
            Assert.That(AutonomousChatIntentModel.HasOutOfWorldWord(null, true), Is.False);
        });

        [Test]
        public void AnEpicMovieGetsTheOffTopicReply()
        {
            bool saved = AutonomousChatIntentModel.LearnedOffTopicBlocksRescue;
            try
            {
                AutonomousChatIntentModel.LearnedOffTopicBlocksRescue = true;
                Assert.That(BanterLibrary165.ClassifyPlayerMessage("anyone see the epic movie", false)[0], Is.EqualTo("reply_offtopic"));
                Assert.That(BanterLibrary165.ClassifyPlayerMessage("ty", false)[0], Is.Not.EqualTo("reply_offtopic"));
            }
            finally
            {
                AutonomousChatIntentModel.LearnedOffTopicBlocksRescue = saved;
            }
        }
    }
}
