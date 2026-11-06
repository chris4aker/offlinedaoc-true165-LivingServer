using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Companion taunting (owner 2026-11-06: "they can have like zero hps and they will still use a taunt style to taunt off
    /// you. Need a way to disable them taunting"): the "Taunt" AI Option, the player as main tank, the 40% health floor.
    /// </summary>
    [TestFixture]
    public sealed class UT_CompanionTaunt165
    {
        [Test]
        public void AutomaticTauntsOnlyForATankCompanionThatIsTheMainTank() => Assert.Multiple(() =>
        {
            Assert.That(BotMeleeStylePolicy.CompanionMayTaunt(0, true, false, 100), Is.True, "tank companion, player not tanking");
            Assert.That(BotMeleeStylePolicy.CompanionMayTaunt(0, true, true, 100), Is.False, "never off the player as main tank");
            Assert.That(BotMeleeStylePolicy.CompanionMayTaunt(0, false, false, 100), Is.False,
                "a damage dealer never taunts on Automatic, even with no tank in the group");
            Assert.That(BotMeleeStylePolicy.CompanionMayTaunt(0, true, false,
                BotMeleeStylePolicy.AutomaticTauntMinHealthPercent), Is.True);
            Assert.That(BotMeleeStylePolicy.CompanionMayTaunt(0, true, false,
                BotMeleeStylePolicy.AutomaticTauntMinHealthPercent - 1), Is.False, "no taunting under 40% health");
            Assert.That(BotMeleeStylePolicy.AutomaticTauntMinHealthPercent, Is.EqualTo(40));
        });

        [Test]
        public void AlwaysAndNeverAreThePlayersChoice() => Assert.Multiple(() =>
        {
            Assert.That(BotMeleeStylePolicy.CompanionMayTaunt(1, false, true, 5), Is.True, "Always: whatever the class or health");
            Assert.That(BotMeleeStylePolicy.CompanionMayTaunt(2, true, false, 100), Is.False, "Never (detaunt)");
            Assert.That(BotMeleeStylePolicy.CompanionDetaunts(2, true, false, false), Is.True);
            Assert.That(BotMeleeStylePolicy.CompanionDetaunts(1, false, true, true), Is.False, "Always never sheds it");
        });

        [Test]
        public void OnAutomaticACompanionGivesTheMonsterBackToTheMainTank() => Assert.Multiple(() =>
        {
            Assert.That(BotMeleeStylePolicy.CompanionDetaunts(0, false, false, true), Is.True, "the player tanks");
            Assert.That(BotMeleeStylePolicy.CompanionDetaunts(0, true, false, true), Is.True, "even a tank companion");
            Assert.That(BotMeleeStylePolicy.CompanionDetaunts(0, false, true, false), Is.True, "a tank companion holds it");
            Assert.That(BotMeleeStylePolicy.CompanionDetaunts(0, true, false, false), Is.False, "it is the main tank");
            Assert.That(BotMeleeStylePolicy.CompanionDetaunts(0, false, false, false), Is.False, "nobody else to take it");
        });

        [Test]
        public void ThePlayerIsMainTankAsATankClassOrWhenTheMonsterStartsOnThemInMelee() => Assert.Multiple(() =>
        {
            Assert.That(BotMeleeStylePolicy.MainTankAtStart(true, false, false), Is.True, "an Armsman, Warrior, Hero ...");
            Assert.That(BotMeleeStylePolicy.MainTankAtStart(false, true, false), Is.True, "a Berserker tanking the pull");
            Assert.That(BotMeleeStylePolicy.MainTankAtStart(false, false, false), Is.False, "the companion tank has it");
            Assert.That(BotMeleeStylePolicy.MainTankAtStart(false, true, true), Is.False,
                "a caster the monster ran to is not tanking: its companions still take it off");
            Assert.That(BotMeleeStylePolicy.PlayerIsMainTank(null, null), Is.False);
        });

        [Test]
        public void TheAggroRowIsNowTauntWithItsStoredValuesKept()
        {
            var option = CompanionOptions165.Option(CompanionOptions165.KindAggro);
            Assert.That(option, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(CompanionOptions165.Caption(CompanionOptions165.KindAggro), Is.EqualTo("Taunt"));
                Assert.That(option.Value.Key, Is.EqualTo("aggro"), "stored key unchanged: existing hire records still read");
                Assert.That(option.Value.Choices, Is.EqualTo(new[] { "Automatic", "Always", "Never (detaunt)" }),
                    "index 1 was Hold it (taunt), 2 Shed it (detaunt)");
            });
        }
    }
}
