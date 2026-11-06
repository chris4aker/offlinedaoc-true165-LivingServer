using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Item 49: one stealth detection range for players and gamebots (GamePlayer.CanDetect, bot target filters).</summary>
    [TestFixture]
    public class UT_StealthDetection
    {
        [Test]
        public void NormalRangeIsLevelMinusStealthTimesTwentyPlus125() =>
            Assert.That(StealthDetection.Range(50, 30, false, false, 0), Is.EqualTo(20 * 20 + 125));

        [Test]
        public void DetectHiddenIsTimesFiftyPlus250() =>
            Assert.That(StealthDetection.Range(50, 30, true, false, 0), Is.EqualTo(20 * 50 + 250));

        [Test]
        public void SeeHiddenReachesFartherAgainstLowStealth() =>
            Assert.That(StealthDetection.Range(50, 30, false, true, 0), Is.EqualTo(2700 - 36 * 30));

        [Test]
        public void SeeHiddenNeverShortensTheRange() =>
            Assert.That(StealthDetection.Range(50, 0, true, true, 0), Is.EqualTo(StealthDetection.HardCap));

        [Test]
        public void HigherStealthThanLevelLeavesTheBase125() =>
            Assert.That(StealthDetection.Range(20, 40, false, false, 0), Is.EqualTo(125));

        [Test]
        public void StealthSpecCountsAtMostFifty() =>
            Assert.That(StealthDetection.Range(50, 60, false, true, 0), Is.EqualTo(2700 - 36 * 50));

        [Test]
        public void BonusIsAddedBeforeTheHardCap()
        {
            Assert.That(StealthDetection.Range(50, 30, false, false, 100), Is.EqualTo(625));
            Assert.That(StealthDetection.Range(50, 0, true, false, 500), Is.EqualTo(1900));
        }

        [Test]
        public void NoTargetIsNotSeen() => Assert.That(StealthDetection.BotCanSee(null, null), Is.False);
    }
}
