using System;
using System.Globalization;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Owner 2026-11-10: idle exchange visits, false between-task timeouts after a restart, solo melee at flyer camps.</summary>
    [TestFixture, NonParallelizable]
    public class UT_GamebotLogFixes20261110
    {
        [Test]
        public void TheExchangeStepEndsAfterThreeFruitlessChecksUnlessAListingWaits()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousServiceFallback.FruitlessExchangeChecksBeforeGivingUp, Is.EqualTo(3));
                Assert.That(AutonomousServiceFallback.GivesUpOnExchange(0, false), Is.False);
                Assert.That(AutonomousServiceFallback.GivesUpOnExchange(2, false), Is.False);
                Assert.That(AutonomousServiceFallback.GivesUpOnExchange(3, false), Is.True);
                Assert.That(AutonomousServiceFallback.GivesUpOnExchange(4, false), Is.True);
                Assert.That(AutonomousServiceFallback.GivesUpOnExchange(5, true), Is.False, "a listing still waits on its chatter");
            });
        }

        [Test]
        public void ASavedBetweenTaskErrandGetsAFreshWindowAtLogin()
        {
            DateTime now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
            var record = new OfflineWorldBotRecord
            {
                ObjectiveAssignmentId = "between-pve-services-I-7-100",
                ObjectiveExpiresUtc = now.AddHours(-9).ToString("O"),
            };
            Assert.That(AutonomousObjectiveAssignments.RestampBetweenTaskWindowAtLogin(record, now), Is.True);
            DateTime due = DateTime.Parse(record.ObjectiveExpiresUtc, null, DateTimeStyles.RoundtripKind).ToUniversalTime();
            Assert.That(due, Is.EqualTo(now + AutonomousObjectiveAssignments.MaximumBetweenTaskDuration));
        }

        [Test]
        public void AnOrdinaryTaskKeepsItsDeadlineAtLogin()
        {
            DateTime now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
            string expires = now.AddMinutes(-5).ToString("O");
            var record = new OfflineWorldBotRecord { ObjectiveAssignmentId = "solo-7-100", ObjectiveExpiresUtc = expires };
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousObjectiveAssignments.RestampBetweenTaskWindowAtLogin(record, now), Is.False);
                Assert.That(record.ObjectiveExpiresUtc, Is.EqualTo(expires));
                Assert.That(AutonomousObjectiveAssignments.RestampBetweenTaskWindowAtLogin(null, now), Is.False);
            });
        }

        [Test]
        public void AFlyerCampIsOneWhereMostMonstersFly()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousFlyerCampPolicy.IsFlyerCamp(5, 5), Is.True);
                Assert.That(AutonomousFlyerCampPolicy.IsFlyerCamp(3, 5), Is.True);
                Assert.That(AutonomousFlyerCampPolicy.IsFlyerCamp(2, 4), Is.False, "half is not most");
                Assert.That(AutonomousFlyerCampPolicy.IsFlyerCamp(0, 6), Is.False);
                Assert.That(AutonomousFlyerCampPolicy.IsFlyerCamp(0, 0), Is.False);
            });
        }

        [Test]
        public void OnlySoloBotsWithoutARangedAttackSkipFlyerCamps()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousFlyerCampPolicy.SkipsCamp(true, true, false), Is.True, "solo melee");
                Assert.That(AutonomousFlyerCampPolicy.SkipsCamp(true, true, true), Is.False, "solo ranged keeps it");
                Assert.That(AutonomousFlyerCampPolicy.SkipsCamp(true, false, false), Is.False, "a group keeps it");
                Assert.That(AutonomousFlyerCampPolicy.SkipsCamp(false, true, false), Is.False, "not a flyer camp");
                Assert.That(AutonomousFlyerCampPolicy.HasRangedAttack(true, false), Is.True, "caster");
                Assert.That(AutonomousFlyerCampPolicy.HasRangedAttack(false, true), Is.True, "archer with a bow");
                Assert.That(AutonomousFlyerCampPolicy.HasRangedAttack(false, false), Is.False, "melee, thrown weapon or not");
            });
        }

        [Test]
        public void ASoloMeleeBotTakesOnlyAnAggressiveFlyerItCanOpenOnFromRange()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousFlyerCampPolicy.SoloCanTakeFlyer(true, false, false), Is.True);
                Assert.That(AutonomousFlyerCampPolicy.SoloCanTakeFlyer(false, true, true), Is.True);
                Assert.That(AutonomousFlyerCampPolicy.SoloCanTakeFlyer(false, true, false), Is.False, "a passive mygga never comes down");
                Assert.That(AutonomousFlyerCampPolicy.SoloCanTakeFlyer(false, false, true), Is.False);
            });
        }

        [Test]
        public void ASoloMeleePullerProvesTheRouteOnlyAfterAFailedPullAtTheCamp()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousFlyerCampPolicy.VerifiesMeleePullRoute(true, true, 1), Is.True);
                Assert.That(AutonomousFlyerCampPolicy.VerifiesMeleePullRoute(true, true, 0), Is.False, "ordinary camp: no extra navmesh work");
                Assert.That(AutonomousFlyerCampPolicy.VerifiesMeleePullRoute(false, true, 2), Is.False, "groups unchanged");
                Assert.That(AutonomousFlyerCampPolicy.VerifiesMeleePullRoute(true, false, 2), Is.False, "ranged pullers unchanged");
            });
        }

        [Test]
        public void RepeatBenchesGrowFromTwoToSixToTwentyFourHours()
        {
            DateTime now = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousCampRouteQuarantine.QuarantineDurationFor(1), Is.EqualTo(TimeSpan.FromHours(2)));
                Assert.That(AutonomousCampRouteQuarantine.QuarantineDurationFor(2), Is.EqualTo(TimeSpan.FromHours(6)));
                Assert.That(AutonomousCampRouteQuarantine.QuarantineDurationFor(3), Is.EqualTo(TimeSpan.FromHours(24)));
                Assert.That(AutonomousCampRouteQuarantine.QuarantineDurationFor(9), Is.EqualTo(TimeSpan.FromHours(24)));
                Assert.That(AutonomousCampRouteQuarantine.NextStrike(0, default, now), Is.EqualTo(1));
                Assert.That(AutonomousCampRouteQuarantine.NextStrike(1, now.AddHours(-3), now), Is.EqualTo(2));
                Assert.That(AutonomousCampRouteQuarantine.NextStrike(2, now.AddHours(-25), now), Is.EqualTo(1), "a day clean starts over");
            });
        }

        [Test]
        public void ACampBenchedAgainSoonStaysOutLonger()
        {
            AutonomousCampRouteQuarantine.ResetForTests();
            try
            {
                const string camp = "capnbry:151:6:10:mygga";
                DateTime now = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
                DateTime Bench(DateTime at)
                {
                    AutonomousCampRouteQuarantine.ReportFailure(camp, 1, at);
                    AutonomousCampRouteQuarantine.ReportFailure(camp, 2, at.AddMinutes(1));
                    Assert.That(AutonomousCampRouteQuarantine.ReportFailure(camp, 3, at.AddMinutes(2)), Is.True);
                    return at.AddMinutes(2);
                }

                DateTime first = Bench(now);
                Assert.That(AutonomousCampRouteQuarantine.IsQuarantined(camp, first.AddHours(2).AddMinutes(-1)), Is.True);
                Assert.That(AutonomousCampRouteQuarantine.IsQuarantined(camp, first.AddHours(2)), Is.False);

                DateTime second = Bench(first.AddHours(3));
                Assert.That(AutonomousCampRouteQuarantine.BenchDuration(camp), Is.EqualTo(TimeSpan.FromHours(6)));
                Assert.That(AutonomousCampRouteQuarantine.IsQuarantined(camp, second.AddHours(6).AddMinutes(-1)), Is.True);

                DateTime third = Bench(second.AddHours(7));
                Assert.That(AutonomousCampRouteQuarantine.IsQuarantined(camp, third.AddHours(24).AddMinutes(-1)), Is.True);

                DateTime fresh = Bench(third.AddHours(24 + 25));
                Assert.That(AutonomousCampRouteQuarantine.BenchDuration(camp), Is.EqualTo(TimeSpan.FromHours(2)), "a day clean starts over");
                Assert.That(AutonomousCampRouteQuarantine.IsQuarantined(camp, fresh.AddHours(2)), Is.False);
            }
            finally
            {
                AutonomousCampRouteQuarantine.ResetForTests();
            }
        }
    }
}
