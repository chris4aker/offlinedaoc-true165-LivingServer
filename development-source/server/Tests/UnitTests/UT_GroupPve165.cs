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

        [Test]
        public void APlayerLedPullIsBroughtBackToThePlayer() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousDefensivePull.PlayerLedArrived(false, 1500, 1200), Is.False, "still going out to tag it");
            Assert.That(AutonomousDefensivePull.PlayerLedArrived(true, 900, 600), Is.False, "on the way back");
            Assert.That(AutonomousDefensivePull.PlayerLedArrived(true, AutonomousDefensivePull.ContactRadius, 500), Is.True,
                "the monster reached the player");
            Assert.That(AutonomousDefensivePull.PlayerLedArrived(true, 800, AutonomousDefensivePull.PlayerLedArrivalRadius), Is.True,
                "the tank is back; the monster is behind it");
            Assert.That(AutonomousDefensivePull.PlayerLedArrived(false, 900, 100), Is.False, "back without a tag is no arrival");
            // its blows on the tank on the way back don't end the pull at the spawn; anything else does
            Assert.That(AutonomousDefensivePull.IgnoresThreatWhileBringingBack(true, true, true), Is.True);
            Assert.That(AutonomousDefensivePull.IgnoresThreatWhileBringingBack(true, false, true), Is.False, "an add");
            Assert.That(AutonomousDefensivePull.IgnoresThreatWhileBringingBack(true, true, false), Is.False, "it hit someone else");
            Assert.That(AutonomousDefensivePull.IgnoresThreatWhileBringingBack(false, true, true), Is.False, "gamebot party pull");
        });

        [Test]
        public void PartyMembersFollowTheKillTargetUnlessLowAndHit() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.AssistsKillTarget(true, false, 100, true), Is.True, "hit but healthy: stay on the kill target");
            Assert.That(BotBrain.AssistsKillTarget(true, false, 40, false), Is.True);
            Assert.That(BotBrain.AssistsKillTarget(true, false, 40, true), Is.False, "low and being hit: fight back");
            Assert.That(BotBrain.AssistsKillTarget(true, true, 100, false), Is.False, "never onto a mezzed kill target");
            Assert.That(BotBrain.AssistsKillTarget(false, false, 100, false), Is.False);
        });

        [Test]
        public void TheFirstTanksTargetStaysTheKillTarget() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.ReplacesKillTarget(true, false, true, 1_000), Is.False, "a second tank on an add does not take over");
            Assert.That(BotBrain.ReplacesKillTarget(false, false, true, 1_000), Is.True, "the kill target died");
            Assert.That(BotBrain.ReplacesKillTarget(true, true, true, 1_000), Is.True, "the main tank switched");
            Assert.That(BotBrain.ReplacesKillTarget(true, false, false, 1_000), Is.True, "the main tank left it (dead or elsewhere)");
            Assert.That(BotBrain.ReplacesKillTarget(true, false, true, BotBrain.KillTargetStaleMilliseconds + 1), Is.True);
        });

        [Test]
        public void PartiesPullTheMonsterStandingApartFirst() => Assert.Multiple(() =>
        {
            var monsters = new (System.Numerics.Vector3 Position, int AggroRange)[]
            {
                (new(0, 0, 0), 500), (new(300, 0, 0), 500), (new(0, 300, 0), 500), (new(3000, 0, 0), 500),
                (new(6000, 0, 0), 0),
            };
            int[] counts = AutonomousGroupPullOrder.NeighbourCounts(monsters);
            Assert.That(counts[0], Is.EqualTo(2), "two others within its aggro range");
            Assert.That(counts[1], Is.EqualTo(2), "(300,0)-(0,300) is 424 apart");
            Assert.That(counts[3], Is.Zero, "the lone one comes first");
            Assert.That(counts[4], Is.Zero, "no aggro range: nobody counted");
        });

        [Test]
        public void AMezzerIsPreferredForTheBufferSlot()
        {
            eCharacterClass[] pool =
            [
                eCharacterClass.Thane, eCharacterClass.Healer, eCharacterClass.Healer, eCharacterClass.Shaman,
                eCharacterClass.Berserker, eCharacterClass.Savage, eCharacterClass.Hunter
            ];
            bool[] mez = [false, true, false, false, false, false, false];
            for (int run = 0; run < 20; run++)
            {
                BotPveGroupRole?[] roles = AutonomousBotGroupCoordinator.PveFormationRoles(eCharacterClass.Warrior, pool, mez);
                Assert.That(roles, Is.Not.Null);
                Assert.That(roles[1], Is.EqualTo(BotPveGroupRole.Buffer), "the Healer with mez takes the buffer slot");
                Assert.That(roles, Has.All.Not.Null, "still the full eight");
            }
            Assert.That(AutonomousBotGroupCoordinator.PreferredCrowdControlCandidate(pool, null), Is.EqualTo(-1),
                "normal mode: no preference");
        }

        [Test]
        public void TheMezPreferenceNeverShrinksTheParty()
        {
            // the only mezzer is one of the two healers the full eight needs: it stays a healer
            eCharacterClass[] pool =
            [
                eCharacterClass.Thane, eCharacterClass.Healer, eCharacterClass.Shaman, eCharacterClass.Skald,
                eCharacterClass.Berserker, eCharacterClass.Savage, eCharacterClass.Hunter
            ];
            bool[] mez = [false, true, false, false, false, false, false];
            for (int run = 0; run < 20; run++)
            {
                BotPveGroupRole?[] roles = AutonomousBotGroupCoordinator.PveFormationRoles(eCharacterClass.Warrior, pool, mez);
                Assert.That(roles, Is.Not.Null);
                Assert.That(roles, Has.All.Not.Null);
                Assert.That(roles[1], Is.EqualTo(BotPveGroupRole.Healer));
            }
        }
    }
}
