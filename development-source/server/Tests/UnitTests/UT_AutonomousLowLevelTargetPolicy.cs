using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Owner 2026-11-10 "level1": level-1 solo bots hunt blue only, with a yellow fallback only when a yellow
    /// camp would have been found.</summary>
    [TestFixture]
    public class UT_AutonomousLowLevelTargetPolicy
    {
        private const int Mystic = (int)eCharacterClass.Mystic, Viking = (int)eCharacterClass.Viking,
            Forester = (int)eCharacterClass.Forester;

        [Test]
        public void LevelOneSoloBotsHuntBlueAndEveryoneElseKeepsTheirCeiling()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousLowLevelTargetPolicy.SoloCeiling(Mystic, 1, false, true), Is.EqualTo(ConColor.BLUE));
                Assert.That(AutonomousLowLevelTargetPolicy.SoloCeiling(Viking, 1, false, true), Is.EqualTo(ConColor.BLUE));
                Assert.That(AutonomousLowLevelTargetPolicy.SoloCeiling(Mystic, 1, true, true), Is.EqualTo(ConColor.YELLOW), "yellow fallback");
                Assert.That(AutonomousLowLevelTargetPolicy.SoloCeiling(Mystic, 2, false, true), Is.EqualTo(ConColor.YELLOW), "level 2 unchanged");
                Assert.That(AutonomousLowLevelTargetPolicy.SoloCeiling(Mystic, 0, false, true), Is.EqualTo(ConColor.BLUE));
                Assert.That(AutonomousLowLevelTargetPolicy.SoloCeiling(Mystic, 1, false, false), Is.EqualTo(ConColor.YELLOW), "switch off");
                foreach (int level in new[] { 1, 7 })
                foreach (bool fallback in new[] { false, true })
                foreach (bool on in new[] { false, true })
                    Assert.That(AutonomousLowLevelTargetPolicy.SoloCeiling(Forester, level, fallback, on), Is.EqualTo(ConColor.BLUE),
                        $"Forester {level} {fallback} {on}");
                Assert.That(AutonomousLowLevelTargetPolicy.SoloCeiling(0, 50, false, true), Is.EqualTo(ConColor.YELLOW), "no bot (level 50)");
            });
        }

        [Test]
        public void TheYellowFallbackStartsOnlyWhenAYellowCampWouldHaveBeenFound()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousLowLevelTargetPolicy.StartsYellowFallback(Mystic, 1, true, false, false, true, true), Is.True);
                Assert.That(AutonomousLowLevelTargetPolicy.StartsYellowFallback(Mystic, 1, true, false, true, true, true), Is.False, "already active");
                Assert.That(AutonomousLowLevelTargetPolicy.StartsYellowFallback(Mystic, 2, true, false, false, true, true), Is.False, "level 2");
                Assert.That(AutonomousLowLevelTargetPolicy.StartsYellowFallback(Mystic, 1, false, false, false, true, true), Is.False, "grouped");
                Assert.That(AutonomousLowLevelTargetPolicy.StartsYellowFallback(Mystic, 1, true, true, false, true, true), Is.False, "camp chosen");
                Assert.That(AutonomousLowLevelTargetPolicy.StartsYellowFallback(Mystic, 1, true, false, false, false, true), Is.False,
                    "no yellow camp either: today's no-camp path");
                Assert.That(AutonomousLowLevelTargetPolicy.StartsYellowFallback(Forester, 1, true, false, false, true, true), Is.False, "Forester");
                Assert.That(AutonomousLowLevelTargetPolicy.StartsYellowFallback(Mystic, 1, true, false, false, true, false), Is.False, "switch off");
            });
        }

        [Test]
        public void ACampOfLevelOneSpawnsQualifiesOnlyUnderAYellowCeilingForALevelOneBot()
        {
            int[] levelOnes = { 1, 1, 1 }, levelZeros = { 0, 0, 0 };
            Assert.Multiple(() =>
            {
                Assert.That(ConLevels.GetConColor(ConLevels.GetConLevel(1, 0)), Is.EqualTo(ConColor.BLUE), "con table: level 0 is blue at 1");
                Assert.That(ConLevels.GetConColor(ConLevels.GetConLevel(1, 1)), Is.EqualTo(ConColor.YELLOW));
                Assert.That(AutonomousLowLevelTargetPolicy.QualifiesUpTo(1, 1, levelOnes, ConColor.BLUE), Is.False);
                Assert.That(AutonomousLowLevelTargetPolicy.QualifiesUpTo(1, 1, levelOnes, ConColor.YELLOW), Is.True);
                Assert.That(AutonomousLowLevelTargetPolicy.QualifiesUpTo(1, 1, levelZeros, ConColor.BLUE), Is.True);
                Assert.That(AutonomousLowLevelTargetPolicy.QualifiesUpTo(1, 1, new int[0], ConColor.YELLOW), Is.False);
            });
        }

        [Test]
        public void TheSummaryCountsPlansAndComesAtMostOnceEveryTenMinutes()
        {
            AutonomousLowLevelTargetPolicy.ResetForTests();
            try
            {
                Assert.That(AutonomousLowLevelTargetPolicy.TryTakeSummary(500, out _), Is.False);
                AutonomousLowLevelTargetPolicy.CountPlan(eRealm.Hibernia, true, false);
                AutonomousLowLevelTargetPolicy.CountPlan(eRealm.Hibernia, false, true);
                Assert.That(AutonomousLowLevelTargetPolicy.TryTakeSummary(500 + 9 * 60_000, out _), Is.False);
                Assert.That(AutonomousLowLevelTargetPolicy.TryTakeSummary(500 + 10 * 60_000, out string line), Is.True);
                Assert.That(line, Does.Contain("hib: blue_plans=2 no_camp=1 yellow_fallbacks=1"));
            }
            finally
            {
                AutonomousLowLevelTargetPolicy.ResetForTests();
            }
        }

        [Test]
        public void AYellowCampStartsTheFallbackOnlyWhenTheRealChoiceWouldTakeIt()
        {
            int[] levelOnes = { 1, 1, 1 };
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousLowLevelTargetPolicy.YellowCampWouldBeChosen(1, 1, levelOnes, 3, false, false, false, false), Is.True);
                Assert.That(AutonomousLowLevelTargetPolicy.YellowCampWouldBeChosen(1, 1, levelOnes, 0, false, true, false, false), Is.False, "no live spawns");
                Assert.That(AutonomousLowLevelTargetPolicy.YellowCampWouldBeChosen(1, 1, levelOnes, 3, false, true, true, false), Is.False, "Darkness Falls");
                Assert.That(AutonomousLowLevelTargetPolicy.YellowCampWouldBeChosen(1, 1, levelOnes, 3, false, true, false, true), Is.False, "threat policy");
                Assert.That(AutonomousLowLevelTargetPolicy.YellowCampWouldBeChosen(1, 1, levelOnes, 3, true, false, false, false), Is.False,
                    "dungeon while waiting for matchmaking");
                Assert.That(AutonomousLowLevelTargetPolicy.YellowCampWouldBeChosen(1, 1, levelOnes, 3, true, true, false, false), Is.True);
                Assert.That(AutonomousLowLevelTargetPolicy.YellowCampWouldBeChosen(1, 1, new[] { 5, 5, 5 }, 3, false, true, false, false), Is.False,
                    "orange is not yellow");
            });
        }

        [Test]
        public void MonstersHiddenOnlyByTheLevelOneCeilingKeepTheCampOutOfTheSharedEmptyMemory()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousLowLevelTargetPolicy.HiddenByLevel1Ceiling(ConColor.YELLOW, ConColor.BLUE, ConColor.YELLOW), Is.True);
                Assert.That(AutonomousLowLevelTargetPolicy.HiddenByLevel1Ceiling(ConColor.BLUE, ConColor.BLUE, ConColor.YELLOW), Is.False, "pullable");
                Assert.That(AutonomousLowLevelTargetPolicy.HiddenByLevel1Ceiling(ConColor.ORANGE, ConColor.BLUE, ConColor.YELLOW), Is.False,
                    "orange is too hard for anyone solo");
                Assert.That(AutonomousLowLevelTargetPolicy.HiddenByLevel1Ceiling(ConColor.YELLOW, ConColor.BLUE, ConColor.BLUE), Is.False,
                    "a Forester's own blue ceiling hides nothing new");
                Assert.That(AutonomousLowLevelTargetPolicy.HiddenByLevel1Ceiling(ConColor.YELLOW, ConColor.YELLOW, ConColor.YELLOW), Is.False,
                    "switch off or yellow fallback");

                Assert.That(AutonomousLowLevelTargetPolicy.HidLiveTargets("camp", "camp", 5_000, 1_000), Is.True);
                Assert.That(AutonomousLowLevelTargetPolicy.HidLiveTargets("camp", "camp", 500, 1_000), Is.False, "seen before this empty wait");
                Assert.That(AutonomousLowLevelTargetPolicy.HidLiveTargets("other", "camp", 5_000, 1_000), Is.False, "another camp");
                Assert.That(AutonomousLowLevelTargetPolicy.HidLiveTargets("camp", "camp", 5_000, 0), Is.False, "no empty wait");
                Assert.That(AutonomousLowLevelTargetPolicy.HidLiveTargets(null, null, 5_000, 1_000), Is.False);
            });
        }

        [Test]
        public void TheGoalChoiceTellsLevelOneDeathFallbacksApart()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousLowLevelTargetPolicy.ChoiceLabel(true, false, false, false), Is.EqualTo("crowd_widened"));
                Assert.That(AutonomousLowLevelTargetPolicy.ChoiceLabel(false, true, false, false), Is.EqualTo("level1_blue"));
                Assert.That(AutonomousLowLevelTargetPolicy.ChoiceLabel(false, true, false, true), Is.EqualTo("level1_blue+death_fallback"));
                Assert.That(AutonomousLowLevelTargetPolicy.ChoiceLabel(false, false, true, false), Is.EqualTo("level1_yellow_fallback"));
                Assert.That(AutonomousLowLevelTargetPolicy.ChoiceLabel(false, false, true, true), Is.EqualTo("level1_yellow_fallback+death_fallback"));
                Assert.That(AutonomousLowLevelTargetPolicy.ChoiceLabel(false, false, false, true), Is.EqualTo("death_fallback"));
                Assert.That(AutonomousLowLevelTargetPolicy.ChoiceLabel(false, false, false, false), Is.EqualTo(string.Empty));
            });
        }

        [Test]
        public void TheAbortLinesWarnOnlyWhenCrossedWithEnoughAttempts()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousLowLevelTargetPolicy.AbortCheck("hib", 19, 19, 19), Is.Null, "too few attempts");
                Assert.That(AutonomousLowLevelTargetPolicy.AbortCheck("hib", 100, 25, 20), Is.Null, "on the lines");
                Assert.That(AutonomousLowLevelTargetPolicy.AbortCheck("hib", 100, 26, 0), Is.EqualTo("hib far_travel=26/100"));
                Assert.That(AutonomousLowLevelTargetPolicy.AbortCheck("alb", 100, 0, 21), Is.EqualTo("alb pre_arrival_deaths=21/100"));
                Assert.That(AutonomousLowLevelTargetPolicy.AbortCheckHibernia200(100, 35), Is.Null);
                Assert.That(AutonomousLowLevelTargetPolicy.AbortCheckHibernia200(100, 36), Is.EqualTo("hib region200_empty_camp=36/100"));
            });
        }

        [Test]
        public void TheSummaryCarriesTheAbortWarningForLevelOneAttemptsOnly()
        {
            AutonomousLowLevelTargetPolicy.ResetForTests();
            try
            {
                Assert.That(AutonomousLowLevelTargetPolicy.TryTakeSummary(0 + 1, out _, out _), Is.False, "arms the timer");
                for (int i = 0; i < 20; i++)
                {
                    // Hibernia region 200: every attempt EmptyCamp, far away
                    AutonomousLowLevelTargetPolicy.NoteAttemptEnd((int)eRealm.Hibernia, 1, "level1_blue", 200, 50_000, true,
                        GoalAttemptEnd.EmptyCamp);
                    // ignored: level 2, or not a level-1 choice
                    AutonomousLowLevelTargetPolicy.NoteAttemptEnd((int)eRealm.Albion, 2, "level1_blue", 1, 50_000, false, GoalAttemptEnd.Defeated);
                    AutonomousLowLevelTargetPolicy.NoteAttemptEnd((int)eRealm.Albion, 1, string.Empty, 1, 50_000, false, GoalAttemptEnd.Defeated);
                }
                AutonomousLowLevelTargetPolicy.CountHiddenEmpty(eRealm.Hibernia);
                Assert.That(AutonomousLowLevelTargetPolicy.TryTakeSummary(1 + 10 * 60_000, out string line, out string warning), Is.True);
                Assert.That(line, Does.Contain("hib: blue_plans=0 no_camp=0 yellow_fallbacks=0 hidden_not_empty=1 attempts=20 far=20 pre_arrival_deaths=0"));
                Assert.That(line, Does.Contain("alb: blue_plans=0 no_camp=0 yellow_fallbacks=0 hidden_not_empty=0 attempts=0"));
                Assert.That(warning, Does.Contain("hib far_travel=20/20").And.Contain("hib region200_empty_camp=20/20"));
                Assert.That(warning, Does.Not.Contain("alb far_travel").And.Not.Contain("alb pre_arrival_deaths"));
            }
            finally
            {
                AutonomousLowLevelTargetPolicy.ResetForTests();
            }
        }

        [Test]
        public void TheSwitchDefaultsOn() => Assert.That(AutonomousLowLevelTargetPolicy.Level1BlueCeiling, Is.True);
    }
}
