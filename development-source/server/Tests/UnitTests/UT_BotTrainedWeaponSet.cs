using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // 2026-11-07 (owner): a bot carrying a two-hander and a one-hander (and shield) fights with the set whose weapon line
    // it has trained higher, by default. Osbert (Paladin, Two Handed 9, Slash/Crush/Thrust 1) went back to sword and
    // shield after a relog because the readied choice is not saved and his generation-time build said sword and shield.
    [TestFixture]
    public class UT_BotTrainedWeaponSet
    {
        [Test]
        public void TheHigherTrainedLineWins() => Assert.Multiple(() =>
        {
            Assert.That(BotWeaponSetPolicy.TrainedSet(9, 1, false), Is.True, "Osbert: Two Handed 9 vs Slash 1");
            Assert.That(BotWeaponSetPolicy.TrainedSet(1, 22, false), Is.False, "a sword-and-board Armsman keeps his sword");
        });

        [Test]
        public void NoOpinionLeavesItToTheBuild() => Assert.Multiple(() =>
        {
            Assert.That(BotWeaponSetPolicy.TrainedSet(10, 10, false), Is.Null, "the same line (a Midgard sword) or a tie");
            Assert.That(BotWeaponSetPolicy.TrainedSet(0, 0, false), Is.Null, "specializations not loaded yet");
            Assert.That(BotWeaponSetPolicy.TrainedSet(30, 5, true), Is.Null, "a dual-wield set is never pushed into the two-hander");
        });

        [Test]
        public void AGamebotsOwnPlanAgrees() => Assert.Multiple(() =>
        {
            // a gamebot trains its plan's line: a two-handed plan carrying its starting one-hander, a one-handed plan that
            // looted a two-hander - the trained set is the plan's set, so nothing changes for gamebots
            Assert.That(BotWeaponSetPolicy.TrainedSet(25, 1, false), Is.True);
            Assert.That(BotWeaponSetPolicy.TrainedSet(1, 25, false), Is.False);
        });

        [Test]
        public void AnUninitialisedBotHasNoOpinion() =>
            Assert.That(BotWeaponSetPolicy.TrainedSet((GameBot)null), Is.Null);
    }
}
