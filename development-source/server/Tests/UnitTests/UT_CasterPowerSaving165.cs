using DOL.Database;
using NUnit.Framework;
using static DOL.GS.BotCasterPriority;

namespace DOL.GS.Tests
{
    /// <summary>True 1.65: a grouped caster stops optional nukes under half power while the tank holds the monster.</summary>
    [TestFixture]
    public sealed class UT_CasterPowerSaving165
    {
        [Test]
        public void UnderHalfPowerTheTankDoesTheWork() => Assert.Multiple(() =>
        {
            Assert.That(SavePowerInGroup(40, 80, true), Is.True);
            Assert.That(SavePowerInGroup(SavePowerBelowPercent, 80, true), Is.False, "half power or more: nuke");
            Assert.That(SavePowerInGroup(40, 80, false), Is.False, "nobody holds it: the caster fights");
            Assert.That(SavePowerInGroup(40, FinishTargetBelowPercent - 1, true), Is.False, "finish a nearly dead monster");
        });

        [Test]
        public void ACompanionSavesPowerUnlessThePlayerSaidOtherwise() => Assert.Multiple(() =>
        {
            Assert.That(CompanionSavesPower(0, false), Is.True, "Automatic: on");
            Assert.That(CompanionSavesPower(1, false), Is.False, "Save power: Off - nukes freely");
            Assert.That(CompanionSavesPower(0, true), Is.False, "a healer set to attack keeps nuking");
        });

        [Test]
        public void TheSavePowerRowIsAnAiOptionForCastersAndHybrids() => Assert.Multiple(() =>
        {
            var option = CompanionOptions165.Option(CompanionOptions165.KindSavePower);
            Assert.That(option, Is.Not.Null);
            Assert.That(option.Value.Key, Is.EqualTo("savepower"), "the key SavePowerForGroup reads");
            Assert.That(option.Value.Choices, Is.EqualTo(new[] { "Automatic (on)", "Off" }), "choice 1 is Off");
            // owner 2026-11-11: the pure casters' row (same key) adds choice 2, Strict (UT_CompanionSpellOptions165)
            var caster = CompanionOptions165.Option(CompanionOptions165.KindSavePowerCaster);
            Assert.That(caster.Value.Key, Is.EqualTo("savepower"));
            Assert.That(caster.Value.Choices[..2], Is.EqualTo(new[] { "Automatic (on)", "Off" }), "0 and 1 unchanged");
            Assert.That(caster.Value.Choices[2], Does.StartWith("Strict"));
            Assert.That(CompanionOptions165.KindSavePower, Is.GreaterThanOrEqualTo(5), "a generic row: the client shows it as sent");
            Spell nuke = new(new DbSpell { Type = "DirectDamage", Target = "Enemy", Damage = 50 }, 1);
            Spell mez = new(new DbSpell { Type = "Mesmerize", Target = "Enemy", Duration = 30 }, 1);
            Assert.That(CompanionOptions165.HasDamageSpell([mez, nuke]), Is.True);
            Assert.That(CompanionOptions165.HasDamageSpell([mez]), Is.False, "no damage spell: no row");
        });
    }
}
