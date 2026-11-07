using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // 2026-11-07 (player report via the owner): a dead gamebot in the player's group waits for a raise only while someone
    // in the group can raise it; with nobody to raise it, it releases after the short wait (it lay dead 5 minutes in 0.3).
    [TestFixture]
    public class UT_InvitedGamebotDeath
    {
        [Test]
        public void ReleasesSoonWithNobodyToRaiseIt() => Assert.Multiple(() =>
        {
            Assert.That(GameBot.InvitedGamebotKeepsWaiting165(false, 10_000), Is.True, "the short wait first");
            Assert.That(GameBot.InvitedGamebotKeepsWaiting165(false, GameBot.NoRaiserReleaseMilliseconds), Is.False);
            Assert.That(GameBot.InvitedGamebotKeepsWaiting165(false, 60_000), Is.False, "no five-minute wait");
        });

        [Test]
        public void WaitsUpToFiveMinutesForARaiser() => Assert.Multiple(() =>
        {
            Assert.That(GameBot.InvitedGamebotKeepsWaiting165(true, 60_000), Is.True);
            Assert.That(GameBot.InvitedGamebotKeepsWaiting165(true, HiredCompanions165.BodyFadeMilliseconds - 1), Is.True);
            Assert.That(GameBot.InvitedGamebotKeepsWaiting165(true, HiredCompanions165.BodyFadeMilliseconds), Is.False);
        });

        [Test]
        public void ARaiserWhoDiesEndsTheWait() =>
            // two minutes in, the healer dies: the next check finds no raiser and releases at once
            Assert.That(GameBot.InvitedGamebotKeepsWaiting165(false, 120_000), Is.False);
    }
}
