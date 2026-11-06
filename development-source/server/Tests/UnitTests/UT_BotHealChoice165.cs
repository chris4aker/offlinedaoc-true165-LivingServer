using System.Collections.Generic;
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
    }
}
