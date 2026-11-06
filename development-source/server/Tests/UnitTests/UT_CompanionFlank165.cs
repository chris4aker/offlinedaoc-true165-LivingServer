using DOL.AI.Brain;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Stepping behind or beside a monster for back/side styles (owner 2026-11-06): companion tanks flank when someone else
    /// is the main tank, a turned or moved monster is flanked again, and the "Positioning" AI Option.
    /// </summary>
    [TestFixture]
    public sealed class UT_CompanionFlank165
    {
        [Test]
        public void GamebotsKeepTheirRule() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.FlankAllowed(0, false, false, false), Is.True, "a gamebot damage dealer flanks");
            Assert.That(BotBrain.FlankAllowed(0, false, true, true), Is.False, "a gamebot tank keeps facing its monster");
        });

        [Test]
        public void AutomaticFlanksAMonsterTheMainTankHolds() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.FlankAllowed(0, true, false, true), Is.True);
            Assert.That(BotBrain.FlankAllowed(0, true, true, true), Is.True,
                "a companion tank flanks when someone else (the player) is the main tank");
            Assert.That(BotBrain.FlankAllowed(0, true, false, false), Is.False, "a loose monster on the healer: no dance");
            Assert.That(BotBrain.FlankAllowed(0, true, true, false), Is.False, "the companion tank is the main tank");
        });

        [Test]
        public void FlankAndStayInFrontAreThePlayersChoice() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.FlankAllowed(1, true, false, false), Is.True, "Flank: any monster not on itself");
            Assert.That(BotBrain.FlankAllowed(1, true, true, false), Is.False, "never while it is the main tank");
            Assert.That(BotBrain.FlankAllowed(1, true, true, true), Is.True);
            Assert.That(BotBrain.FlankAllowed(2, true, false, true), Is.False, "Stay in front");
            Assert.That(BotBrain.FlankAllowed(2, true, true, true), Is.False);
        });

        [Test]
        public void ATurnedOrMovedMonsterIsFlankedAgainAtMostEveryThreeSeconds() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.FlankRetry(BotBrain.FlankRetryMilliseconds - 1, 0, 2048, 500), Is.False, "too soon");
            Assert.That(BotBrain.FlankRetry(BotBrain.FlankRetryMilliseconds, 0, 0, 0), Is.False, "it stood still");
            Assert.That(BotBrain.FlankRetry(BotBrain.FlankRetryMilliseconds, 0, 512, 0), Is.False, "45 degrees exactly");
            Assert.That(BotBrain.FlankRetry(BotBrain.FlankRetryMilliseconds, 0, 513, 0), Is.True, "turned past 45 degrees");
            Assert.That(BotBrain.FlankRetry(BotBrain.FlankRetryMilliseconds, 4000, 600, 0), Is.True, "turned across north");
            Assert.That(BotBrain.FlankRetry(BotBrain.FlankRetryMilliseconds, 4000, 100, 0), Is.False, "a small turn across north");
            Assert.That(BotBrain.FlankRetry(BotBrain.FlankRetryMilliseconds, 0, 0, 101), Is.True, "moved more than 100");
            Assert.That(BotBrain.FlankRetry(BotBrain.FlankRetryMilliseconds, 0, 0, 100), Is.False);
        });

        [Test]
        public void ThePositioningRowIsRoutedLikeTheOtherAiOptions()
        {
            var option = CompanionOptions165.Option(CompanionOptions165.KindPositioning);
            Assert.That(option, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(CompanionOptions165.KindPositioning, Is.GreaterThanOrEqualTo(5), "a generic row the client draws");
                Assert.That(CompanionOptions165.Caption(CompanionOptions165.KindPositioning), Is.EqualTo("Positioning"));
                Assert.That(option.Value.Key, Is.EqualTo("position"));
                Assert.That(option.Value.Choices, Is.EqualTo(new[] { "Automatic", "Flank (side/back styles)", "Stay in front" }));
                Assert.That(BotBrain.KnowsFlankStyle(null), Is.False);
            });
        }
    }
}
