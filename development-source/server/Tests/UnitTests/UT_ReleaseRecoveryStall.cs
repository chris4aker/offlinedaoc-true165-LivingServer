using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // owner 2026-11-10: "Recovering after release" ends five minutes past the expected recovery
    public class UT_ReleaseRecoveryStall
    {
        private const long Minute = 60_000;

        [Test]
        public void HoldEndsFiveMinutesPastExpectedRecovery()
        {
            long start = 1_000_000;
            Assert.That(AutonomousDeathRecoveryPolicy.ReleaseRecoveryStalled(start, start, start + 8 * Minute - 1), Is.False);
            Assert.That(AutonomousDeathRecoveryPolicy.ReleaseRecoveryStalled(start, start, start + 8 * Minute), Is.True);
        }

        [Test]
        public void LongerRetryPauseMovesTheCap()
        {
            long start = 1_000_000;
            long pauseUntil = start + 4 * Minute;
            Assert.That(AutonomousDeathRecoveryPolicy.ReleaseRecoveryStalled(start, pauseUntil, start + 8 * Minute), Is.False);
            Assert.That(AutonomousDeathRecoveryPolicy.ReleaseRecoveryStalled(start, pauseUntil, start + 9 * Minute), Is.True);
        }

        [Test]
        public void CapAlwaysBeatsTheFifteenMinuteStuckWatchdog()
        {
            long start = 1_000_000;
            long longestPause = start + AutonomousDeathRecoveryPolicy.RetryDelayMilliseconds(10);
            Assert.That(AutonomousDeathRecoveryPolicy.ReleaseRecoveryStalled(start, longestPause, start + 15 * Minute), Is.True);
        }

        [Test]
        public void NoHoldStartNeverStalls() =>
            Assert.That(AutonomousDeathRecoveryPolicy.ReleaseRecoveryStalled(0, 0, long.MaxValue / 2), Is.False);

        [TestCase((byte)100, (byte)100, (byte)8, true, false, false, "endurance")]
        [TestCase((byte)90, (byte)50, (byte)100, true, false, false, "health,power")]
        [TestCase((byte)100, (byte)0, (byte)100, false, false, false, "none")]
        [TestCase((byte)100, (byte)100, (byte)100, true, true, false, "sickness")]
        [TestCase((byte)100, (byte)100, (byte)100, true, false, true, "pause")]
        public void BlockerNamesWhatIsMissing(byte hp, byte power, byte endurance, bool usesPower, bool sick, bool pausing,
            string expected) =>
            Assert.That(AutonomousDeathRecoveryPolicy.ReleaseRecoveryBlocker(hp, power, endurance, usesPower, sick, pausing),
                Is.EqualTo(expected));
    }
}
