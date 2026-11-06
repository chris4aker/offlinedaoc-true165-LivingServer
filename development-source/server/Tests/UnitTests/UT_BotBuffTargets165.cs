using DOL.AI.Brain;
using DOL.Database;
using DOL.GS.PlayerClass;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>True 1.65 buff targets: tanks first, and no haste or power regeneration where it does nothing.</summary>
    [TestFixture]
    public sealed class UT_BotBuffTargets165
    {
        private static Spell Buff(string type) => new(new DbSpell { Type = type, Target = "Realm", Duration = 1200, Value = 10 }, 1);

        [Test]
        public void TanksFirstThenMeleeHealersCasters() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.BuffTargetRank(new ClassArmsman()), Is.EqualTo(0));
            Assert.That(BotBrain.BuffTargetRank(new ClassMercenary()), Is.EqualTo(1));
            Assert.That(BotBrain.BuffTargetRank(null), Is.EqualTo(1), "a pet fights in melee");
            Assert.That(BotBrain.BuffTargetRank(new ClassCleric()), Is.EqualTo(2));
            Assert.That(BotBrain.BuffTargetRank(new ClassWizard()), Is.EqualTo(3));
        });

        // Companions buff through the same path (FindRandomMissingRealmBuffTarget): the player is ranked by their class,
        // pets (the player's and the companions') as melee.
        [Test]
        public void ThePlayerIsRankedByClassLikeAnyMember() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.BuffTargetRank(new ClassHero()), Is.EqualTo(0), "a Hero player: tank");
            Assert.That(BotBrain.BuffTargetRank(new ClassFriar()), Is.EqualTo(1), "a Friar player: melee");
            Assert.That(BotBrain.BuffTargetRank(new ClassDruid()), Is.EqualTo(2), "a Druid player: healer");
            Assert.That(BotBrain.BuffTargetRank(new ClassTheurgist()), Is.EqualTo(3), "a Theurgist player: caster");
        });

        [Test]
        public void HasteSkipsCastersInTrue165() => Assert.Multiple(() =>
        {
            Spell haste = Buff("CombatSpeedBuff");
            Assert.That(BotBrain.BuffHelps(new ClassWizard(), haste, true), Is.False);
            Assert.That(BotBrain.BuffHelps(new ClassArmsman(), haste, true), Is.True);
            Assert.That(BotBrain.BuffHelps(new ClassWizard(), haste, false), Is.True, "normal mode unchanged");
        });

        // The Healer's Purity of Offense and the Druid's Hunter's line: a concentration haste kept up between fights
        // (it was never cast); Celerity (group, 20 s, no concentration) stays a fight buff.
        [Test]
        public void ConcentrationHasteIsMaintainedInTrue165() => Assert.Multiple(() =>
        {
            Spell purity = new(new DbSpell { Type = "CombatSpeedBuff", Target = "Realm", Duration = 0, Concentration = 11, Value = 15 }, 30);
            Spell celerity = new(new DbSpell { Type = "CombatSpeedBuff", Target = "Group", Duration = 20, Value = 24 }, 18);
            Assert.That(BotBrain.IsMaintainableClassBuff(purity, true), Is.True);
            Assert.That(BotBrain.IsMaintainableClassBuff(celerity, true), Is.False, "Celerity is cast in the fight");
            Assert.That(BotBrain.IsMaintainableClassBuff(purity, false), Is.False, "normal mode unchanged");
            Assert.That(BotBrain.BuffHelps(new ClassWizard(), purity, true), Is.False, "never on a pure caster");
            Assert.That(BotBrain.BuffHelps(new ClassArmsman(), purity, true), Is.True);
        });

        [Test]
        public void PowerRegenerationOnlyForClassesThatUsePower() => Assert.Multiple(() =>
        {
            Spell regen = Buff("PowerRegenBuff");
            Assert.That(BotBrain.BuffHelps(new ClassMercenary(), regen, true), Is.False);
            Assert.That(BotBrain.BuffHelps(new ClassWizard(), regen, true), Is.True);
            Assert.That(BotBrain.BuffHelps(new ClassMercenary(), regen, false), Is.True, "normal mode unchanged");
        });
    }
}
