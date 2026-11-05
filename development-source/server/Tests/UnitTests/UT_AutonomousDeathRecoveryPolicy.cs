using System.Collections.Generic;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_AutonomousDeathRecoveryPolicy
    {
        // 2026-11-05: the after-death difficulty relaxes after a level-up or 30 minutes without a death.
        [TestCase(8, 8, 60_000, false)]
        [TestCase(9, 8, 60_000, true)]
        [TestCase(8, 8, 30 * 60_000, true)]
        public void DeathDifficultyRelaxesAfterALevelOrQuietTime(int level, int levelAtChange, long since, bool relaxes) =>
            Assert.That(AutonomousDeathRecoveryPolicy.RelaxesDifficulty(level, levelAtChange, since, 30 * 60_000), Is.EqualTo(relaxes));

        [Test]
        public void RepeatedZeroExperienceDeathsPauseButNewProgressRestartsTheStreak()
        {
            const long firstDeath = 100_000;
            int second = AutonomousDeathRecoveryPolicy.NextNoExperienceDeathStreak(1,
                firstDeath, firstDeath + 40_000, false);
            int third = AutonomousDeathRecoveryPolicy.NextNoExperienceDeathStreak(second,
                firstDeath + 40_000, firstDeath + 80_000, false);

            Assert.Multiple(() =>
            {
                Assert.That(second, Is.EqualTo(2));
                Assert.That(AutonomousDeathRecoveryPolicy.RetryDelayMilliseconds(second), Is.Zero);
                Assert.That(third, Is.EqualTo(3));
                Assert.That(AutonomousDeathRecoveryPolicy.RetryDelayMilliseconds(third), Is.EqualTo(30_000));
                Assert.That(AutonomousDeathRecoveryPolicy.RetryDelayMilliseconds(8), Is.EqualTo(120_000));
                Assert.That(AutonomousDeathRecoveryPolicy.NextNoExperienceDeathStreak(third,
                    firstDeath + 80_000, firstDeath + 100_000, true), Is.EqualTo(1));
                Assert.That(AutonomousDeathRecoveryPolicy.NextNoExperienceDeathStreak(third,
                    firstDeath + 80_000, firstDeath + 1_100_000, false), Is.EqualTo(1));
            });
        }

        [Test]
        public void RecentLethalTargetIsSkippedWhenAnotherValidTargetExists()
        {
            var failed = new AutonomousBotDecisionEngine.Camp("failed", "Vale", "river sprite", eRealm.Albion,
                1, ConColor.GREEN, ConColor.GREEN, true, false, false, 1, 0, 0);
            var fresh = failed with { Id = "fresh", MonsterName = "young boar" };
            var blocked = new Dictionary<string, long> { ["river sprite"] = 100_000 };

            Assert.Multiple(() =>
            {
                Assert.That(AutonomousDeathRecoveryPolicy.PreferFreshTargets([failed, fresh], blocked, 50_000),
                    Is.EquivalentTo(new[] { fresh }));
                Assert.That(AutonomousDeathRecoveryPolicy.PreferFreshTargets([failed, fresh], blocked, 100_000),
                    Is.EquivalentTo(new[] { failed, fresh }));
                Assert.That(AutonomousDeathRecoveryPolicy.PreferFreshTargets([failed], blocked, 50_000),
                    Is.EquivalentTo(new[] { failed }), "Recovery must not remove the last XP-bearing camp");
            });
        }

        [Test]
        public void SalisburySpiritPackRequiresAFormedPartyWithoutBanningOtherSpirits()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousAuditedCampPolicy.CanAssignToParty("capnbry:1:1:3:spirit", 1), Is.False);
                Assert.That(AutonomousAuditedCampPolicy.CanAssignToParty("capnbry:1:1:3:spirit", 3), Is.False);
                Assert.That(AutonomousAuditedCampPolicy.CanAssignToParty("capnbry:1:1:3:spirit", 4), Is.True);
                Assert.That(AutonomousAuditedCampPolicy.CanAssignToParty("capnbry:2:1:3:spirit", 1), Is.True);
            });
        }

        [Test]
        public void YellowFallbackCanActuallyBePulledAfterGreenWasUnavailable()
        {
            var maximum = AutonomousDeathRecoveryPolicy.EncounterMaximum(
                ConColor.GREEN, ConColor.YELLOW, ConColor.YELLOW, false);
            Assert.That(AutonomousDeathRecoveryPolicy.IsEligible(ConColor.YELLOW, maximum), Is.True);
            Assert.That(AutonomousDeathRecoveryPolicy.IsEligible(ConColor.ORANGE, maximum), Is.False);
            Assert.That(AutonomousDeathRecoveryPolicy.IsEligible(ConColor.GREY, maximum), Is.False);
        }

        [Test]
        public void ANewOrdinaryCampRetainsItsLowerCeiling()
        {
            var maximum = AutonomousDeathRecoveryPolicy.EncounterMaximum(ConColor.GREEN, ConColor.YELLOW, null, false);
            Assert.That(maximum, Is.EqualTo(ConColor.GREEN));
            Assert.That(AutonomousDeathRecoveryPolicy.IsEligible(ConColor.YELLOW, maximum), Is.False);
        }

        [Test]
        public void BlueFallbackDoesNotPermitYellowAndDoesNotChangeDynamicGroupPolicy()
        {
            Assert.That(AutonomousDeathRecoveryPolicy.EncounterMaximum(ConColor.GREEN, ConColor.YELLOW,
                ConColor.BLUE, false), Is.EqualTo(ConColor.BLUE));
            Assert.That(AutonomousDeathRecoveryPolicy.EncounterMaximum(ConColor.ORANGE, ConColor.PURPLE,
                ConColor.YELLOW, true), Is.EqualTo(ConColor.ORANGE));
        }

        [Test]
        public void FallbackCannotExceedNaturalCeilingOrChooseGrey()
        {
            Assert.That(AutonomousDeathRecoveryPolicy.EncounterMaximum(ConColor.GREEN, ConColor.YELLOW,
                ConColor.PURPLE, false), Is.EqualTo(ConColor.YELLOW));
            Assert.That(AutonomousDeathRecoveryPolicy.EncounterMaximum(ConColor.GREEN, ConColor.YELLOW,
                ConColor.GREY, false), Is.EqualTo(ConColor.GREEN));
        }
    }
}
