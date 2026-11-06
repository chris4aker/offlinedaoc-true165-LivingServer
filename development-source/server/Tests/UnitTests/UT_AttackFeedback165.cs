using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Owner 2026-11-06: the player draws a weapon next to a monster and doesn't swing, with no feedback. The silent cases
    /// now say why in the system window, at most every 1.5 seconds for the same reason.
    /// </summary>
    [TestFixture]
    public sealed class UT_AttackFeedback165
    {
        [Test]
        public void TheReasonASwingIsHeldBack() => Assert.Multiple(() =>
        {
            Assert.That(AttackAction.HeldBackReason(false, false, false, false), Is.Null, "nothing holds the swing");
            Assert.That(AttackAction.HeldBackReason(true, false, false, false), Is.EqualTo(AttackHeldBack.Stunned));
            Assert.That(AttackAction.HeldBackReason(false, true, false, false), Is.EqualTo(AttackHeldBack.Mezzed));
            Assert.That(AttackAction.HeldBackReason(false, false, true, false), Is.EqualTo(AttackHeldBack.Engaging));
            Assert.That(AttackAction.HeldBackReason(false, false, false, true), Is.EqualTo(AttackHeldBack.Casting));
            Assert.That(AttackAction.HeldBackReason(true, true, true, true), Is.EqualTo(AttackHeldBack.Stunned), "stun first");
            Assert.That(AttackAction.HeldBackReason(false, false, true, true), Is.EqualTo(AttackHeldBack.Casting),
                "the cast is what the player can stop");
        });

        [Test]
        public void HeldBackMessagesAreThrottledLikeTooFarAway() => Assert.Multiple(() =>
        {
            Assert.That(PlayerAttackAction.HeldBackMessageMilliseconds, Is.EqualTo(1500));
            Assert.That(PlayerAttackAction.ShouldSayHeldBack(AttackHeldBack.Casting, null, 0, 0), Is.True, "the first one");
            Assert.That(PlayerAttackAction.ShouldSayHeldBack(AttackHeldBack.Casting, AttackHeldBack.Casting, 1000, 1500), Is.False);
            Assert.That(PlayerAttackAction.ShouldSayHeldBack(AttackHeldBack.Casting, AttackHeldBack.Casting, 1500, 1500), Is.True);
            Assert.That(PlayerAttackAction.ShouldSayHeldBack(AttackHeldBack.NoTarget, AttackHeldBack.Casting, 1000, 1500), Is.True,
                "a new reason is said at once");
        });

        [Test]
        public void ASilentServerRulesRefusalIsNamed() =>
            Assert.That(AttackComponent.RefusedAttackMessage("the guard"), Is.EqualTo("You can't attack the guard!"));
    }
}
