using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using DOL.GS.RealmAbilities;
using NUnit.Framework;
using static DOL.GS.AutonomousBotRealmAbilities;

namespace DOL.UnitTests
{
    /// <summary>Item 53: True 1.65 bot realm abilities - tank builds and the class/caster actives with their use rules.</summary>
    [TestFixture]
    public class UT_BotRealmAbilities165
    {
        private static Dictionary<string, int> Catalog(params string[] keys) =>
            keys.Select(k => k.StartsWith("AtlasOF_") ? k : "AtlasOF_" + k).ToDictionary(k => k, k => k switch
            {
                Purge or PurgeReduced or SecondWind or IgnorePain or IgnorePainTank or MasteryOfConcentration or BunkerOfFaith or
                    PerfectRecovery or SpeedOfSound or Vanish or AmelioratingMelodies => 1,
                FirstAid or MysticCrystalLore => 3,
                _ => 5,
            });

        private static int Cost(string key, int level) => key switch
        {
            Purge or SecondWind or BunkerOfFaith or SpeedOfSound or Vanish => 10,
            PurgeReduced => 4,
            MasteryOfConcentration or PerfectRecovery or IgnorePain => 14,
            IgnorePainTank => 8,
            FirstAid or MysticCrystalLore => AtlasRAHelpers.GetCommonUpgradeCostFor3LevelsRA(level),
            _ => AtlasRAHelpers.GetCommonUpgradeCostFor5LevelsRA(level),
        };

        private static readonly Dictionary<string, int> Armsman = Catalog("AugStr", "AugCon", "AugDex", "AugQui", "Toughness",
            "AvoidanceOfMagic", "Determination", "MasteryOfPain", "MasteryOfParrying", "MasteryOfBlocking", "MasteryOfArms",
            PurgeReduced, SecondWind, FirstAid, IgnorePainTank);

        [Test]
        public void TanksBuildAsTanksOnlyInTrue165Mode()
        {
            Assert.That(BuildArchetype(Archetype.Melee, (int)eCharacterClass.Armsman, true), Is.EqualTo(Archetype.Tank));
            Assert.That(BuildArchetype(Archetype.Melee, (int)eCharacterClass.Warrior, true), Is.EqualTo(Archetype.Tank));
            Assert.That(BuildArchetype(Archetype.Melee, (int)eCharacterClass.Armsman, false), Is.EqualTo(Archetype.Melee));
            Assert.That(BuildArchetype(Archetype.Melee, (int)eCharacterClass.Infiltrator, true), Is.EqualTo(Archetype.Melee));
            Assert.That(BuildArchetype(Archetype.Caster, (int)eCharacterClass.Wizard, true), Is.EqualTo(Archetype.Caster));
        }

        [Test]
        public void ATankTakesPurgeFirstAndDeterminationRightAfter()
        {
            var four = Plan(Armsman, Archetype.Tank, (int)eCharacterClass.Armsman, 4, Cost, true);
            Assert.That(four.Keys, Is.EquivalentTo(new[] { PurgeReduced }));
            var five = Plan(Armsman, Archetype.Tank, (int)eCharacterClass.Armsman, 5, Cost, true);
            Assert.That(five.GetValueOrDefault("AtlasOF_Determination"), Is.EqualTo(1));
            var old = Plan(Armsman, Archetype.Melee, (int)eCharacterClass.Armsman, 5, Cost);
            Assert.That(old.ContainsKey(PurgeReduced) || old.ContainsKey("AtlasOF_Determination"), Is.False, "normal mode build unchanged");
        }

        [Test]
        public void TankBuildGrowsAndMeetsPrerequisites()
        {
            Dictionary<string, int> previous = new();
            for (int points = 0; points <= 120; points++)
            {
                var plan = Plan(Armsman, Archetype.Tank, (int)eCharacterClass.Armsman, points, Cost, true);
                int spent = plan.Sum(pair => Enumerable.Range(0, pair.Value).Sum(level => Cost(pair.Key, level)));
                Assert.That(spent, Is.LessThanOrEqualTo(points));
                foreach (var (key, _) in plan)
                    if (Requirement(key, (int)eCharacterClass.Armsman) is { } need)
                        Assert.That(plan.GetValueOrDefault(need.Key), Is.GreaterThanOrEqualTo(need.Level), key);
                foreach (var (key, level) in previous)
                    Assert.That(plan.GetValueOrDefault(key), Is.GreaterThanOrEqualTo(level), $"{key} never shrinks");
                previous = plan;
            }
        }

        [Test]
        public void ClassActivesAreBoughtAfterPurgeAndMocAfterAcuityThree()
        {
            var cleric = Catalog("AugAcuity", "AugCon", "AugDex", "MasteryOfHealing", "WildHealing", "Serenity", "Toughness",
                Purge, MasteryOfConcentration, BunkerOfFaith, MysticCrystalLore);
            var plan = Plan(cleric, Archetype.Healer, (int)eCharacterClass.Cleric, 80, Cost, true);
            Assert.That(plan.ContainsKey(BunkerOfFaith), Is.True);
            Assert.That(plan.ContainsKey(MasteryOfConcentration), Is.True);
            Assert.That(plan[AugAcuity], Is.GreaterThanOrEqualTo(3));
            var normal = Plan(cleric, Archetype.Healer, (int)eCharacterClass.Cleric, 40, Cost);
            Assert.That(normal.ContainsKey(BunkerOfFaith), Is.False, "normal mode has no class actives in its build");
        }

        [Test]
        public void ClassicActivesAreOnlyAddedInTrue165Mode()
        {
            Assert.That(UsableActivesFor(false), Is.EqualTo(UsableActives));
            Assert.That(UsableActivesFor(true), Is.SupersetOf(UsableActives));
            Assert.That(UsableActivesFor(true), Does.Contain("AtlasOF_MCL"), "the Atlas OF key of Mystic Crystal Lore");
            Assert.That(UsableActivesFor(true), Is.SupersetOf(new[] { MasteryOfConcentration, BunkerOfFaith, PerfectRecovery,
                SpeedOfSound, AmelioratingMelodies, Vanish }));
        }

        private static Situation S(bool combat = true, bool frontier = true, int health = 100, int mana = 100, bool controlled = false,
            bool sitting = false, bool casting = false, bool interrupted = false, int near = 8, int hurt = 0, bool fighting = true,
            bool dead = false) =>
            new(combat, frontier, health, mana, controlled, sitting, casting, interrupted, near, hurt, fighting, dead);

        [Test]
        public void GroupBurstCallsForBunkerAndSpeedOfSound()
        {
            Assert.That(Wants(BunkerOfFaith, S(hurt: 4)), Is.True);
            Assert.That(Wants(BunkerOfFaith, S(hurt: 3)), Is.False);
            Assert.That(Wants(BunkerOfFaith, S(near: 2, hurt: 2)), Is.True);
            Assert.That(Wants(BunkerOfFaith, S(near: 1, hurt: 1)), Is.False, "at least two hurt");
            Assert.That(Wants(BunkerOfFaith, S(hurt: 8, fighting: false)), Is.False);
            Assert.That(Wants(SpeedOfSound, S(hurt: 4)), Is.True);
            Assert.That(Wants(SpeedOfSound, S(hurt: 4, frontier: false)), Is.False);
            Assert.That(Wants(AmelioratingMelodies, S(hurt: 2)), Is.True);
            Assert.That(Wants(AmelioratingMelodies, S(hurt: 1)), Is.False);
        }

        [Test]
        public void MocWhenHitWhileCastingMclWhenLowOutOfCombat()
        {
            Assert.That(Wants(MasteryOfConcentration, S(interrupted: true)), Is.True);
            Assert.That(Wants(MasteryOfConcentration, S(interrupted: false)), Is.False);
            Assert.That(Wants(MasteryOfConcentration, S(interrupted: true, frontier: false)), Is.False);
            Assert.That(Wants(MasteryOfConcentration, S(interrupted: true, frontier: false, health: 50)), Is.True);
            Assert.That(Wants(MysticCrystalLore, S(combat: false, mana: 20)), Is.True);
            Assert.That(Wants(MysticCrystalLore, S(combat: true, mana: 20)), Is.False);
            Assert.That(Wants(MysticCrystalLore, S(combat: false, mana: 20, sitting: true)), Is.False);
            Assert.That(Wants(MysticCrystalLore, S(combat: false, mana: 50)), Is.False);
        }

        [Test]
        public void VanishEscapesAFrontierFightGoingBadly()
        {
            Assert.That(Wants(Vanish, S(health: 30)), Is.True);
            Assert.That(Wants(Vanish, S(health: 50)), Is.False);
            Assert.That(Wants(Vanish, S(health: 30, frontier: false)), Is.False);
            Assert.That(Wants(Vanish, S(health: 30, controlled: true)), Is.False, "Vanish cannot be used mezzed or stunned");
        }

        [Test]
        public void PerfectRecoveryNeedsADeadMemberAndAFight()
        {
            Assert.That(Wants(PerfectRecovery, S()), Is.False);
            Assert.That(Wants(PerfectRecovery, S(dead: true)), Is.True);
            Assert.That(Wants(PerfectRecovery, S(dead: true, fighting: false)), Is.False);
            Assert.That(Wants(PerfectRecovery, S(dead: true, casting: true)), Is.False);
        }
    }
}
