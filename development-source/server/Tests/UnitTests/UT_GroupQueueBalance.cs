using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using NUnit.Framework;
using OfflineDaoc.Configuration;

namespace DOL.UnitTests
{
    /// <summary>Owner 2026-11-10: role-aware group goals and the Hibernian Bard in the Healer slot.</summary>
    [TestFixture, NonParallelizable]
    public class UT_GroupQueueBalance
    {
        private static readonly BotGoalWeights[] DefaultRows =
            [new BotGoalWeights(60, 40, 0), new BotGoalWeights(40, 40, 20), new BotGoalWeights(20, 40, 40)];

        [TestCase(eCharacterClass.Cleric)] [TestCase(eCharacterClass.Friar)] [TestCase(eCharacterClass.Druid)]
        [TestCase(eCharacterClass.Warden)] [TestCase(eCharacterClass.Healer)] [TestCase(eCharacterClass.Shaman)]
        [TestCase(eCharacterClass.Acolyte)] [TestCase(eCharacterClass.Seer)] [TestCase(eCharacterClass.Naturalist)]
        public void HealingClassesAreHealers(eCharacterClass characterClass)
        {
            Assert.That(AutonomousGroupQueueBalance.CapabilityFor(characterClass, false), Is.EqualTo(eQueueCapability.Healer));
            Assert.That(AutonomousGroupQueueBalance.CapabilityFor(characterClass, true), Is.EqualTo(eQueueCapability.Healer));
        }

        [Test]
        public void APaladinIsHealerAndTank() =>
            Assert.That(AutonomousGroupQueueBalance.CapabilityFor(eCharacterClass.Paladin, true),
                Is.EqualTo(eQueueCapability.Healer | eQueueCapability.Tank));

        [TestCase(eCharacterClass.Armsman)] [TestCase(eCharacterClass.Hero)] [TestCase(eCharacterClass.Champion)]
        [TestCase(eCharacterClass.Viking)] [TestCase(eCharacterClass.Guardian)] [TestCase(eCharacterClass.Thane)]
        public void TankClassesAreTanks(eCharacterClass characterClass) =>
            Assert.That(AutonomousGroupQueueBalance.CapabilityFor(characterClass, true), Is.EqualTo(eQueueCapability.Tank));

        [TestCase(eCharacterClass.Nightshade)] [TestCase(eCharacterClass.Eldritch)] [TestCase(eCharacterClass.Animist)]
        [TestCase(eCharacterClass.Minstrel)] [TestCase(eCharacterClass.Skald)] [TestCase(eCharacterClass.Mentalist)]
        public void DamageClassesHaveNoCapability(eCharacterClass characterClass) =>
            Assert.That(AutonomousGroupQueueBalance.CapabilityFor(characterClass, true), Is.EqualTo(eQueueCapability.None));

        [Test]
        public void ABardIsAHealerOnlyWithItsSwitch()
        {
            Assert.That(AutonomousGroupQueueBalance.CapabilityFor(eCharacterClass.Bard, true), Is.EqualTo(eQueueCapability.Healer));
            Assert.That(AutonomousGroupQueueBalance.CapabilityFor(eCharacterClass.Bard, false), Is.EqualTo(eQueueCapability.None));
        }

        [Test]
        public void RoleLabels()
        {
            Assert.That(AutonomousGroupQueueBalance.RoleLabel(eQueueCapability.Healer | eQueueCapability.Tank), Is.EqualTo("Healer"));
            Assert.That(AutonomousGroupQueueBalance.RoleLabel(eQueueCapability.Healer), Is.EqualTo("Healer"));
            Assert.That(AutonomousGroupQueueBalance.RoleLabel(eQueueCapability.Tank), Is.EqualTo("Tank"));
            Assert.That(AutonomousGroupQueueBalance.RoleLabel(eQueueCapability.None), Is.EqualTo("Damage"));
            Assert.That(AutonomousGroupQueueBalance.RoleLabel((GameBot)null), Is.EqualTo("Damage"));
            Assert.That(AutonomousGroupQueueBalance.GoalCapabilityFor(null), Is.EqualTo(eQueueCapability.None));
        }

        [TestCase(eQueueCapability.None, 0.4, 0.4)]
        [TestCase(eQueueCapability.Healer, 0.4, 0.70)]
        [TestCase(eQueueCapability.Tank, 0.4, 0.60)]
        [TestCase(eQueueCapability.Healer | eQueueCapability.Tank, 0.4, 0.70)]
        [TestCase(eQueueCapability.Healer, 0.6, 0.80)]
        [TestCase(eQueueCapability.Tank, 0.6, 0.75)]
        [TestCase(eQueueCapability.Healer, 0.0, 0.0)]
        [TestCase(eQueueCapability.Tank, 0.0, 0.0)]
        [TestCase(eQueueCapability.Healer, 1.0, 1.0)]
        [TestCase(eQueueCapability.Healer, 0.9, 0.9)]
        [TestCase(eQueueCapability.Tank, 0.9, 0.9)]
        public void GroupShares(eQueueCapability capability, double configured, double expected) =>
            Assert.That(AutonomousGroupQueueBalance.GroupShareFor(capability, configured), Is.EqualTo(expected).Within(1e-9));

        [Test]
        public void AShareIsNeverBelowTheConfiguredShare()
        {
            foreach (eQueueCapability capability in new[] { eQueueCapability.None, eQueueCapability.Healer, eQueueCapability.Tank })
                for (int percent = 0; percent <= 100; percent++)
                    Assert.That(AutonomousGroupQueueBalance.GroupShareFor(capability, percent / 100d),
                        Is.GreaterThanOrEqualTo(percent / 100d - 1e-12));
        }

        [Test]
        public void DamageDrawsExactlyThePlainWeights()
        {
            // the regression guard: a damage bot's goal is weights.Choose for every roll
            var random = new Random(20261110);
            foreach (BotGoalWeights row in DefaultRows)
                for (int index = 0; index < 10_000; index++)
                {
                    double roll = random.NextDouble();
                    foreach (bool excludeBattlegrounds in new[] { false, true })
                        Assert.That(AutonomousGroupQueueBalance.ChooseKindForRole(row, eQueueCapability.None, roll, false, excludeBattlegrounds),
                            Is.EqualTo(row.Choose(roll, false, excludeBattlegrounds)));
                }
        }

        private static double[] Shares(BotGoalWeights row, eQueueCapability capability, bool excludeGroup = false,
            bool excludeBattlegrounds = false)
        {
            var counts = new double[4];
            const int steps = 100_000;
            for (int index = 0; index < steps; index++)
                counts[AutonomousGroupQueueBalance.ChooseKindForRole(row, capability, (index + 0.5) / steps, excludeGroup, excludeBattlegrounds)]++;
            for (int kind = 0; kind < 4; kind++)
                counts[kind] /= steps;
            return counts;
        }

        [Test]
        public void AHealerOnTheLowLevelRowChoosesGroupSeventyPercent()
        {
            double[] shares = Shares(new BotGoalWeights(60, 40, 0), eQueueCapability.Healer);
            Assert.That(shares[1], Is.EqualTo(0.70).Within(0.001));
            Assert.That(shares[0], Is.EqualTo(0.30).Within(0.001));
            Assert.That(shares[2] + shares[3], Is.Zero);
        }

        [Test]
        public void AHealerOnTheMiddleRowSplitsTheRestInProportion()
        {
            double[] shares = Shares(new BotGoalWeights(40, 40, 20), eQueueCapability.Healer);
            Assert.That(shares[1], Is.EqualTo(0.70).Within(0.001));
            Assert.That(shares[0], Is.EqualTo(0.20).Within(0.001));
            Assert.That(shares[2], Is.EqualTo(0.10).Within(0.001));
            Assert.That(shares[3], Is.Zero);
        }

        [Test]
        public void ATankOnTheLowLevelRowChoosesGroupSixtyPercent() =>
            Assert.That(Shares(new BotGoalWeights(60, 40, 0), eQueueCapability.Tank)[1], Is.EqualTo(0.60).Within(0.001));

        [Test]
        public void ZeroGroupAndExcludeGroupNeverGiveGroupAndGroupOnlyAlwaysDoes()
        {
            foreach (eQueueCapability capability in new[] { eQueueCapability.None, eQueueCapability.Healer, eQueueCapability.Tank })
            {
                Assert.That(Shares(new BotGoalWeights(100, 0, 0), capability)[1], Is.Zero);
                Assert.That(Shares(new BotGoalWeights(60, 0, 40), capability)[1], Is.Zero);
                Assert.That(Shares(new BotGoalWeights(40, 40, 20), capability, excludeGroup: true)[1], Is.Zero);
                Assert.That(Shares(new BotGoalWeights(0, 100, 0), capability)[1], Is.EqualTo(1));
            }
        }

        [Test]
        public void TheBattlegroundExclusionStillHolds()
        {
            var row = new BotGoalWeights(30, 40, 10, 20);
            Assert.That(Shares(row, eQueueCapability.Healer, excludeBattlegrounds: true)[3], Is.Zero);
            double[] withBattlegrounds = Shares(row, eQueueCapability.Healer);
            Assert.That(withBattlegrounds[3], Is.EqualTo(0.30 * 20 / 60).Within(0.001));
            Assert.That(withBattlegrounds[1], Is.EqualTo(0.70).Within(0.001));
        }

        [Test]
        public void AnInvalidRollIsRejected() =>
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                AutonomousGroupQueueBalance.ChooseKindForRole(new BotGoalWeights(60, 40, 0), eQueueCapability.Healer, 1.0));

        [TestCase(40, 60, 0, 0)] [TestCase(40, 60, 10, 30)] [TestCase(40, 60, 40, 60)] [TestCase(40, 60, 50, 70)]
        [TestCase(0, 0, 0, 0)] [TestCase(10, 0, 0, 0)]
        public void DamageAllocatorPickIsExactlyChoosePveObjective(int solo, int group, int assignedSolo, int assignedGroup)
        {
            var target = new AutonomousObjectiveAssignments.Allocation(solo, group, 0);
            var first = new Random(77);
            var second = new Random(77);
            for (int index = 0; index < 2_000; index++)
                Assert.That(AutonomousGroupQueueBalance.ChooseRolePveObjective(target, assignedSolo, assignedGroup, eQueueCapability.None, first),
                    Is.EqualTo(AutonomousObjectiveAssignments.ChoosePveObjective(target, assignedSolo, assignedGroup, second)));
        }

        [Test]
        public void AHealerGetsMoreGroupPicksWhileGroupIsNeeded()
        {
            var target = new AutonomousObjectiveAssignments.Allocation(60, 40, 0);
            var random = new Random(5);
            int healerGroup = 0, damageGroup = 0;
            for (int index = 0; index < 10_000; index++)
            {
                if (AutonomousGroupQueueBalance.ChooseRolePveObjective(target, 0, 0, eQueueCapability.Healer, random) == eAutonomousObjectiveKind.GroupPve)
                    healerGroup++;
                if (AutonomousGroupQueueBalance.ChooseRolePveObjective(target, 0, 0, eQueueCapability.None, random) == eAutonomousObjectiveKind.GroupPve)
                    damageGroup++;
            }
            Assert.That(healerGroup / 10_000d, Is.EqualTo(0.70).Within(0.03));
            Assert.That(damageGroup / 10_000d, Is.EqualTo(0.40).Within(0.03));
        }

        [Test]
        public void AllocatorEdgesKeepTheExistingRules()
        {
            var target = new AutonomousObjectiveAssignments.Allocation(60, 40, 0);
            var random = new Random(9);
            for (int index = 0; index < 500; index++)
            {
                // no group needed: always solo; no solo needed: always group
                Assert.That(AutonomousGroupQueueBalance.ChooseRolePveObjective(target, 0, 40, eQueueCapability.Healer, random),
                    Is.EqualTo(eAutonomousObjectiveKind.SoloPve));
                Assert.That(AutonomousGroupQueueBalance.ChooseRolePveObjective(target, 60, 0, eQueueCapability.Tank, random),
                    Is.EqualTo(eAutonomousObjectiveKind.GroupPve));
            }
            // nothing needed: the existing branch, draw for draw
            var first = new Random(11);
            var second = new Random(11);
            for (int index = 0; index < 500; index++)
                Assert.That(AutonomousGroupQueueBalance.ChooseRolePveObjective(target, 60, 40, eQueueCapability.Healer, first),
                    Is.EqualTo(AutonomousObjectiveAssignments.ChoosePveObjective(target, 60, 40, second)));
            Assert.That(AutonomousGroupQueueBalance.ChooseRolePveObjective(new AutonomousObjectiveAssignments.Allocation(0, 0, 5), 0, 0,
                eQueueCapability.Healer, new Random(1)), Is.EqualTo(eAutonomousObjectiveKind.RvR));
        }

        [Test]
        public void BardSlotRule()
        {
            Assert.That(BardGroupHealer165.FillsSlot(eCharacterClass.Bard, BotPveGroupRole.Healer, true), Is.True);
            Assert.That(BardGroupHealer165.FillsSlot(eCharacterClass.Bard, BotPveGroupRole.Healer, false), Is.False);
            Assert.That(BardGroupHealer165.FillsSlot(eCharacterClass.Bard, BotPveGroupRole.Tank, true), Is.False);
            Assert.That(BardGroupHealer165.FillsSlot(eCharacterClass.Minstrel, BotPveGroupRole.Healer, true), Is.False);
            Assert.That(BardGroupHealer165.FillsSlot(eCharacterClass.Skald, BotPveGroupRole.Healer, true), Is.False);
            // the class-wide rule is unchanged: a solo Bard is no healer class
            Assert.That(BotPartyRoles.CanFill(eCharacterClass.Bard, BotPveGroupRole.Healer), Is.False);
            Assert.That(BotPartyRoles.IsHealingClass(eCharacterClass.Bard), Is.False);
        }

        [Test]
        public void ABardFillsAHibernianRostersHealerSlotOnlyWithTheSwitch()
        {
            bool saved = BardGroupHealer165.Enabled;
            try
            {
                BardGroupHealer165.Enabled = true;
                Assert.That(BotPartyRoles.CanFillPveSlot(eCharacterClass.Bard, BotPveGroupRole.Healer), Is.True);
                Assert.That(AutonomousBotGroupCoordinator.PveFormationSize(eCharacterClass.Hero,
                    eCharacterClass.Bard, eCharacterClass.Nightshade, eCharacterClass.Eldritch, eCharacterClass.Ranger), Is.EqualTo(5));
                BotPveGroupRole?[] roles = AutonomousBotGroupCoordinator.PveFormationRoles(eCharacterClass.Hero,
                    [eCharacterClass.Bard, eCharacterClass.Nightshade, eCharacterClass.Eldritch, eCharacterClass.Ranger], null);
                Assert.That(roles[0], Is.EqualTo(BotPveGroupRole.Healer));
                // with a Druid too, a six forms (one heals, the other buffs or heals)
                Assert.That(AutonomousBotGroupCoordinator.PveFormationSize(eCharacterClass.Hero,
                    eCharacterClass.Druid, eCharacterClass.Bard, eCharacterClass.Blademaster, eCharacterClass.Eldritch,
                    eCharacterClass.Ranger), Is.EqualTo(6));

                BardGroupHealer165.Enabled = false;
                Assert.That(BotPartyRoles.CanFillPveSlot(eCharacterClass.Bard, BotPveGroupRole.Healer), Is.False);
                Assert.That(AutonomousBotGroupCoordinator.PveFormationSize(eCharacterClass.Hero,
                    eCharacterClass.Bard, eCharacterClass.Nightshade, eCharacterClass.Eldritch, eCharacterClass.Ranger), Is.Zero);
            }
            finally
            {
                BardGroupHealer165.Enabled = saved;
            }
        }

        [Test]
        public void AHealingClassKeepsTheHealerSlotOverABardInEveryPoolOrder()
        {
            // owner 2026-11-10 (round-3 review): a Druid or Warden heals; a Bard heals only in a slot no healer can fill
            bool saved = BardGroupHealer165.Enabled;
            try
            {
                BardGroupHealer165.Enabled = true;
                // the plain rule fills a six: the Druid heals, the Bard buffs or attacks
                AssertHealerHeals(eCharacterClass.Druid, 6, false, eCharacterClass.Druid, eCharacterClass.Bard,
                    eCharacterClass.Blademaster, eCharacterClass.Eldritch, eCharacterClass.Ranger);
                // a seven needs the Bard rule (two Healer slots, one healer): the healer still heals, one Bard too
                AssertHealerHeals(eCharacterClass.Druid, 7, true, eCharacterClass.Druid, eCharacterClass.Bard,
                    eCharacterClass.Bard, eCharacterClass.Blademaster, eCharacterClass.Eldritch, eCharacterClass.Ranger);
                AssertHealerHeals(eCharacterClass.Warden, 7, true, eCharacterClass.Warden, eCharacterClass.Bard,
                    eCharacterClass.Bard, eCharacterClass.Blademaster, eCharacterClass.Eldritch, eCharacterClass.Ranger);
            }
            finally
            {
                BardGroupHealer165.Enabled = saved;
            }
        }

        private static void AssertHealerHeals(eCharacterClass healer, int size, bool bardHeals, params eCharacterClass[] pool)
        {
            foreach (eCharacterClass[] order in Permutations(pool))
                for (int repeat = 0; repeat < 2; repeat++) // the leader slot order is random
                {
                    BotPveGroupRole?[] roles = AutonomousBotGroupCoordinator.PveFormationRoles(eCharacterClass.Hero, order, null);
                    Assert.That(roles, Is.Not.Null);
                    Assert.That(roles.Count(role => role != null), Is.EqualTo(size - 1));
                    Assert.That(roles[Array.IndexOf(order, healer)], Is.EqualTo(BotPveGroupRole.Healer), string.Join(",", order));
                    bool anyBardHeals = order.Select((c, i) => c == eCharacterClass.Bard && roles[i] == BotPveGroupRole.Healer)
                        .Any(heals => heals);
                    Assert.That(anyBardHeals, Is.EqualTo(bardHeals), string.Join(",", order));
                }
        }

        private static IEnumerable<eCharacterClass[]> Permutations(eCharacterClass[] items)
        {
            if (items.Length <= 1)
            {
                yield return items;
                yield break;
            }
            for (int first = 0; first < items.Length; first++)
            {
                eCharacterClass[] rest = items.Where((_, index) => index != first).ToArray();
                foreach (eCharacterClass[] tail in Permutations(rest))
                    yield return new[] { items[first] }.Concat(tail).ToArray();
            }
        }

        [Test]
        public void TurningTheSwitchOffEndsBardHealingInLiveGroups()
        {
            // owner 2026-11-10 (round-3 review): BotBrain.IsHealer reads the switch on every check
            var group = new Group((GameLiving)null);
            Assert.That(BardGroupHealer165.HoldsHealerSlot(group, group, true), Is.True);
            Assert.That(BardGroupHealer165.HoldsHealerSlot(group, group, false), Is.False);
            Assert.That(BardGroupHealer165.HoldsHealerSlot(group, new Group((GameLiving)null), true), Is.False);
            Assert.That(BardGroupHealer165.HoldsHealerSlot(null, null, true), Is.False);
        }

        [Test]
        public void OtherRealmsRostersAreUnchanged()
        {
            // a Skald or Minstrel never takes the Healer slot: no healer, no party
            Assert.That(AutonomousBotGroupCoordinator.PveFormationSize(eCharacterClass.Thane,
                eCharacterClass.Skald, eCharacterClass.Berserker, eCharacterClass.Savage, eCharacterClass.Hunter), Is.Zero);
            Assert.That(AutonomousBotGroupCoordinator.PveFormationSize(eCharacterClass.Armsman,
                eCharacterClass.Minstrel, eCharacterClass.Mercenary, eCharacterClass.Wizard, eCharacterClass.Scout), Is.Zero);
        }
    }
}
