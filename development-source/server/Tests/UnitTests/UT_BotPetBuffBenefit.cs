using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>GitHub #28 (owner 2026-11-10): no wasted buffs on pets, and heals over time without stacking.</summary>
    [TestFixture]
    public sealed class UT_BotPetBuffBenefit
    {
        private static Spell Buff(string type, string target = "Realm", int concentration = 0) =>
            new(new DbSpell { Type = type, Target = target, Duration = 1200, Value = 10, Concentration = concentration }, 1);

        [Test]
        public void PetsGetNothingFromRegenAcuityOrFatigue() => Assert.Multiple(() =>
        {
            Assert.That(BotPetBuffBenefit.Helps(Buff("EnduranceRegenBuff", concentration: 5)), Is.False, "pets pay no endurance");
            Assert.That(BotPetBuffBenefit.Helps(Buff("EnduranceRegenBuff", "Group")), Is.False);
            Assert.That(BotPetBuffBenefit.Helps(Buff("PowerRegenBuff", concentration: 5)), Is.False, "GameNPC power is fixed");
            Assert.That(BotPetBuffBenefit.Helps(Buff("AcuityBuff", concentration: 12)), Is.False, "acuity only adds to a class mana stat");
            Assert.That(BotPetBuffBenefit.Helps(Buff("FatigueConsumptionBuff", "Self")), Is.False);
        });

        [Test]
        public void PetsKeepTheBuffsThatHelpThem() => Assert.Multiple(() =>
        {
            foreach (string type in new[] { "StrengthBuff", "StrengthConstitutionBuff", "DexterityQuicknessBuff", "BaseArmorFactorBuff",
                         "SpecArmorFactorBuff", "CombatSpeedBuff", "HealthRegenBuff", "DamageAdd", "DamageShield",
                         "PowerHealthEnduranceRegenBuff", "HeatResistBuff" })
                Assert.That(BotPetBuffBenefit.Helps(Buff(type)), Is.True, type);
        });

        [Test]
        public void APetIsAnNpcThatIsNotAPlayerLikeBot() => Assert.Multiple(() =>
        {
            // GameObject's static quest cache needs a test server when this runs alone (owner 2026-11-10).
            using var server = new DOL.UnitTests.EpicTestServerScope();
            Assert.That(BotPetBuffBenefit.IsPetLike((GameLiving)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC))), Is.True);
            Assert.That(BotPetBuffBenefit.IsPetLike((GameLiving)RuntimeHelpers.GetUninitializedObject(typeof(GameBot))), Is.False);
            Assert.That(BotPetBuffBenefit.IsPetLike(null), Is.False);
        });

        [Test]
        public void PetsComeAfterEveryPlayerAndBot() =>
            Assert.That(BotPetBuffBenefit.PetRank, Is.GreaterThan(BotBrain.BuffTargetRank(new DOL.GS.PlayerClass.ClassWizard())));

        [Test]
        public void AHealOverTimeNeverStacksOrOverwrites() => Assert.Multiple(() =>
        {
            Assert.That(BotHealTriage165.HotWorthCasting(false, false, 60), Is.True);
            Assert.That(BotHealTriage165.HotWorthCasting(true, false, 60), Is.False, "one already running, from anyone");
            Assert.That(BotHealTriage165.HotWorthCasting(false, true, 60), Is.False, "a groupmate is casting one at it");
            Assert.That(BotHealTriage165.HotWorthCasting(false, false, 100), Is.False, "never on full health");
        });

        [Test]
        public void TheTankGetsAHealOverTimeAsTheDamageStarts() => Assert.Multiple(() =>
        {
            Assert.That(BotHealTriage165.PreHotTank(true, true, 85, false, 80), Is.True);
            Assert.That(BotHealTriage165.PreHotTank(true, true, 95, false, 80), Is.False, "not hurt yet");
            Assert.That(BotHealTriage165.PreHotTank(false, true, 85, false, 80), Is.False, "no fight");
            Assert.That(BotHealTriage165.PreHotTank(true, false, 85, false, 80), Is.False, "tank not fighting");
            Assert.That(BotHealTriage165.PreHotTank(true, true, 85, true, 80), Is.False, "one running or coming");
            Assert.That(BotHealTriage165.PreHotTank(true, true, 85, false, 30), Is.False, "power kept for real heals");
            Assert.That(BotHealTriage165.PreHotTank(true, true, 30, false, 80), Is.False, "a spike: the big heal (emergency branch)");
        });

        private static Spell Hot(int id, string type, int group = 0, int pulse = 0) =>
            new(new DbSpell { SpellID = id, Type = type, Target = "Realm", Duration = 20, Frequency = 30, Value = 30,
                SpellGroup = group, Pulse = pulse }, 1);

        [Test]
        public void OnlyAHealOverTimeOfTheSameKindBlocksOne() => Assert.Multiple(() =>
        {
            // review 2026-11-10: Shaman regenerations are HealthRegenBuff, the family of songs and other regen buffs
            Spell shamanRegen = Hot(1, "HealthRegenBuff", group: 77);
            Assert.That(BotHealTriage165.SameKindHot(shamanRegen, Hot(2, "HealthRegenBuff", pulse: 1), false), Is.False,
                "a regen song running never blocks it");
            Assert.That(BotHealTriage165.SameKindHot(shamanRegen, Hot(3, "HealthRegenBuff", group: 12), false), Is.False,
                "another line's regen buff");
            Assert.That(BotHealTriage165.SameKindHot(shamanRegen, Hot(1, "HealthRegenBuff", group: 77), false), Is.True, "the same spell");
            Assert.That(BotHealTriage165.SameKindHot(shamanRegen, Hot(4, "HealthRegenBuff", group: 77), false), Is.True,
                "another rank of its line");
            Assert.That(BotHealTriage165.SameKindHot(shamanRegen, Hot(5, "HealthRegenBuff"), true), Is.True, "one of the bot's own HoTs");
            Assert.That(BotHealTriage165.SameKindHot(Hot(6, "HealOverTime"), Hot(7, "HealOverTime", group: 9), false), Is.True,
                "heals over time from anyone do not stack");
            Assert.That(BotHealTriage165.SameKindHot(Hot(6, "HealOverTime"), Hot(8, "HealOverTime", pulse: 1), true), Is.False,
                "never a pulse");
            Assert.That(BotHealTriage165.SameKindHot(shamanRegen, null, false), Is.False);
        });

        [Test]
        public void ThePreHotNeverStopsTheHealer() => Assert.Multiple(() =>
        {
            // review 2026-11-10: range and sight are checked at the call; here the stop
            Assert.That(BotHealTriage165.PreHotHealerFree(true, true, true, true), Is.True, "an instant one stops nothing");
            Assert.That(BotHealTriage165.PreHotHealerFree(false, false, false, false), Is.True, "a cast one on a healer standing free");
            Assert.That(BotHealTriage165.PreHotHealerFree(false, true, false, false), Is.False, "never stops a swing for it");
            Assert.That(BotHealTriage165.PreHotHealerFree(false, false, true, false), Is.False, "never stops a walk");
            Assert.That(BotHealTriage165.PreHotHealerFree(false, false, false, true), Is.False, "never ends a follow");
        });
    }
}
