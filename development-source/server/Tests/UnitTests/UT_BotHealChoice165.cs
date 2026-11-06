using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// True 1.65 heal choice by what a heal really heals: Value x U(min, 1.25), min = 0.25 + (spec - 1) / spell level
    /// (HealSpellHandler). Rated by raw Value, a level 40 Shaman with Mending 14 chose Minor Reconstruction over Major Recovery.
    /// </summary>
    [TestFixture]
    public sealed class UT_BotHealChoice165
    {
        [Test]
        public void ExpectedHealFollowsTheSpecVariance() => Assert.Multiple(() =>
        {
            Assert.That(GameBot.ExpectedHeal165(95, 14, 37), Is.EqualTo(87.9).Within(0.1), "Minor Reconstruction at Mending 14");
            Assert.That(GameBot.ExpectedHeal165(136, 14, 14), Is.EqualTo(165.1).Within(0.1), "Major Recovery at Mending 14");
            Assert.That(GameBot.ExpectedHeal165(100, 50, 20), Is.EqualTo(125), "the variance tops out at 1.25");
            Assert.That(GameBot.ExpectedHeal165(100, 0, 20), Is.EqualTo(75), "no spec: 0.25 to 1.25");
        });

        [Test]
        public void ALevel40ShamanWithMending14HealsWithMajorRecovery()
        {
            const int spec = 14;
            // (name, value, spell level, power, cast seconds) of the single-target heals it knows
            (string Name, double Value, int Level, int Power, double Cast)[] known =
            [
                ("Major Reconstitution", 55, 5, 10, 3.2), ("Major Amelioration", 82, 8, 15, 3.2),
                ("Major Purification", 109, 11, 20, 3.2), ("Major Recovery", 136, 14, 24, 3.2),
                ("Minor Restoration", 75, 29, 12, 2.2), ("Restoration", 158, 31, 28, 2.8),
                ("Reconstruction", 183, 36, 33, 2.8), ("Minor Reconstruction", 95, 37, 14, 2.2),
            ];
            List<(double, int, double)> rated = new();
            foreach (var heal in known)
                rated.Add((GameBot.ExpectedHeal165(heal.Value, spec, heal.Level), heal.Power, heal.Cast));

            (int efficient, int big) = GameBot.ChooseHeals165(rated);
            Assert.Multiple(() =>
            {
                Assert.That(known[efficient].Name, Is.EqualTo("Major Recovery"), "best expected health per power");
                Assert.That(known[big].Name, Is.EqualTo("Reconstruction"), "best expected health per second");
            });
        }

        [Test]
        public void ATinyCheapHealIsNotTheEfficientHeal()
        {
            // 40 hp for 4 power beats 200 hp for 30 power per power, but is less than half the big heal
            (int efficient, _) = GameBot.ChooseHeals165([(40, 4, 2.2), (200, 30, 3.0)]);
            Assert.That(efficient, Is.EqualTo(1));
            Assert.That(GameBot.ChooseHeals165([]), Is.EqualTo((-1, -1)));
        }

        // A bot casts every spell through the "Mob Spells" line, which is no spec: its heals varied 0.25-1.25 whatever its
        // Mending. The heal handler now takes the spec of the bot's learned line, as a player's does.
        private sealed class SpecBot : GameBot
        {
            private SpecBot() : base((OfflineWorldBotRecord)null) { }
            public override int GetModifiedSpecLevel(string keyName) => keyName == "Mending" ? 14 : 0;
        }

        [Test]
        public void ABotsHealVariesWithTheSpecOfItsLearnedLine()
        {
            bool mode = DOL.GS.ServerProperties.Properties.CLASSIC165_MODE;
            using var server = new DOL.UnitTests.EpicTestServerScope();   // GameObject's quest cache needs a database
            try
            {
                DOL.GS.ServerProperties.Properties.CLASSIC165_MODE = true;
                var bot = (SpecBot)RuntimeHelpers.GetUninitializedObject(typeof(SpecBot));
                Spell recovery = new(new DbSpell { SpellID = 99971, Name = "Major Recovery", Type = "Heal", Target = "Realm", Value = 136 }, 14);
                var mob = new SpellLine(GlobalSpellsLines.Mob_Spells, GlobalSpellsLines.Mob_Spells, GlobalSpellsLines.Mob_Spells, true);
                var mending = new SpellLine("Shaman Mend Spec", "Mending", "Mending", false);
                typeof(GameBot).GetField("_powerSpellLines", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(bot, new Dictionary<int, SpellLine> { [recovery.ID] = mending });

                new HealSpellHandler(bot, recovery, mob).CalculateDamageVariance(bot, out double min, out double max);
                Assert.Multiple(() =>
                {
                    Assert.That(min, Is.EqualTo(HealSpellHandler.SpecVarianceMin(14, 14)).Within(1e-9), "Mending 14, as a player's");
                    Assert.That(min, Is.EqualTo(0.25 + 13.0 / 14).Within(1e-9));
                    Assert.That(max, Is.EqualTo(1.25));
                    Assert.That(HealSpellHandler.SpecLineFor(bot, recovery, mob), Is.SameAs(mending));
                });

                // a spell it has no learned line for keeps the line it was cast from
                Spell unknown = new(new DbSpell { SpellID = 99972, Name = "Unknown", Type = "Heal", Target = "Realm", Value = 50 }, 10);
                Assert.That(HealSpellHandler.SpecLineFor(bot, unknown, mob), Is.SameAs(mob));
            }
            finally
            {
                DOL.GS.ServerProperties.Properties.CLASSIC165_MODE = mode;
            }
        }

        [Test]
        public void TheHealVarianceBoundIsThePlayerFormula() => Assert.Multiple(() =>
        {
            Assert.That(HealSpellHandler.SpecVarianceMin(0, 20), Is.EqualTo(0.25), "no spec");
            Assert.That(HealSpellHandler.SpecVarianceMin(11, 20), Is.EqualTo(0.75));
            Assert.That(HealSpellHandler.SpecVarianceMin(50, 20), Is.EqualTo(1.25), "capped at the upper bound");
            Assert.That(HealSpellHandler.SpecVarianceMin(30, 0), Is.EqualTo(0.25), "a level 0 spell");
        });
    }
}
