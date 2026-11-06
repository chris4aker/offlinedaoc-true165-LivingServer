using DOL.AI.Brain;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>Group PvE fixes (2026-11-06 list): don't break mez, pulls, main assist, chain pulls, group make-up.</summary>
    [TestFixture]
    public sealed class UT_GroupPve165
    {
        [Test]
        public void TanksNeverProtectAgainstAMesmerizedMonster() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.IsProtectionThreat(true, false), Is.True, "a monster hitting a member is taken off it");
            Assert.That(BotBrain.IsProtectionThreat(true, true), Is.False, "a mezzed monster still points at its last victim");
            Assert.That(BotBrain.IsProtectionThreat(false, false), Is.False);
        });

        [Test]
        public void PartiesChainPullAtNinetyHealthAndEightyPower() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotGroupCoordinator.ReadyForChainPull(90, 80, 40, true), Is.True);
            Assert.That(AutonomousBotGroupCoordinator.ReadyForChainPull(89, 100, 100, true), Is.False);
            Assert.That(AutonomousBotGroupCoordinator.ReadyForChainPull(100, 79, 100, true), Is.False);
            Assert.That(AutonomousBotGroupCoordinator.ReadyForChainPull(100, 0, 100, false), Is.True, "no power: power ignored");
            Assert.That(AutonomousBotGroupCoordinator.ReadyForChainPull(100, 100, AutonomousRestPolicy.EnduranceRestThreshold, true),
                Is.True, "endurance only above the ordinary rest threshold");
            Assert.That(AutonomousBotGroupCoordinator.ReadyForChainPull(100, 100, AutonomousRestPolicy.EnduranceRestThreshold - 1, true),
                Is.False);
            // once recovering, a 1.65 party no longer waits for 100/100/100
            Assert.That(AutonomousBotGroupCoordinator.ShouldRecoverAfterGroupCombat(true, 92, 85, 60, true, true), Is.False);
            Assert.That(AutonomousBotGroupCoordinator.ShouldRecoverAfterGroupCombat(true, 92, 85, 60, true), Is.True,
                "normal mode keeps the full rest");
            Assert.That(AutonomousBotGroupCoordinator.ShouldRecoverAfterGroupCombat(false, 85, 100, 100, true, true), Is.True);
            // the owner's solo rest rule is untouched
            Assert.That(AutonomousRestPolicy.PowerRestThreshold, Is.EqualTo(70));
        });
    }
}
