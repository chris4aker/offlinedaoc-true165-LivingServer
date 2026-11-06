using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Item 51: True 1.65 warbands lean toward eight and take a healer, a speed class and a CC class.</summary>
    [TestFixture]
    public class UT_RvrWarbandMakeup
    {
        private sealed record Bot(string Name, bool Healer = false, bool Speed = false, bool Cc = false);

        private static string[] Choose(Bot leader, int size, params Bot[] pool) =>
            AutonomousRvrStaging.ChooseWarbandMembers(leader, pool, size, b => b.Healer, b => b.Speed, b => b.Cc)
                .Select(b => b.Name).ToArray();

        [Test]
        public void SizeNeverOneAndLeansTowardEight()
        {
            int[] counts = new int[9];
            for (int i = 0; i < 1000; i++)
                counts[AutonomousRvrStaging.RollClassicWarbandSize(8, i / 1000d)]++;
            Assert.That(counts[0] + counts[1], Is.Zero);
            Assert.That(counts[8], Is.GreaterThan(350), "a full eight is the most common warband");
            Assert.That(Enumerable.Range(2, 6).All(size => counts[size] < counts[8]), Is.True);
            Assert.That(counts.Skip(2).Sum(), Is.EqualTo(1000));
        }

        [Test]
        public void SizeStaysWithinTheAllowedMaximum()
        {
            Assert.That(AutonomousRvrStaging.RollClassicWarbandSize(3, 0), Is.EqualTo(3));
            Assert.That(AutonomousRvrStaging.RollClassicWarbandSize(3, 0.99), Is.EqualTo(2));
            Assert.That(AutonomousRvrStaging.RollClassicWarbandSize(2, 0.5), Is.EqualTo(2));
            Assert.That(AutonomousRvrStaging.RollClassicWarbandSize(20, 0), Is.EqualTo(8));
            Assert.That(AutonomousRvrStaging.RollClassicWarbandSize(8, 1), Is.EqualTo(2));
        }

        [Test]
        public void AHealerASpeedClassAndACcClassComeFirst() =>
            Assert.That(Choose(new("leader"), 5,
                    new("a"), new("b"), new("cc", Cc: true), new("c"), new("speed", Speed: true), new("healer", Healer: true)),
                Is.EqualTo(new[] { "healer", "speed", "cc", "a" }));

        [Test]
        public void OneBotCanFillTwoNeeds() =>
            Assert.That(Choose(new("leader"), 4, new("a"), new("bard", Speed: true, Cc: true), new("healer", Healer: true), new("b")),
                Is.EqualTo(new[] { "healer", "bard", "a" }));

        [Test]
        public void AHealerLeaderNeedsNoOtherHealer() =>
            Assert.That(Choose(new("leader", Healer: true), 3, new("a"), new("healer", Healer: true), new("b")),
                Is.EqualTo(new[] { "a", "healer" }));

        [Test]
        public void NoHealerAvailableStillFormsInPoolOrder() =>
            Assert.That(Choose(new("leader"), 3, new("a"), new("b"), new("c")), Is.EqualTo(new[] { "a", "b" }));

        [Test]
        public void ShortPoolFormsASmallerWarband() =>
            Assert.That(Choose(new("leader"), 8, new("a"), new("healer", Healer: true)), Is.EqualTo(new[] { "healer", "a" }));

        [Test]
        public void ATwoBotWarbandTakesTheHealer() =>
            Assert.That(Choose(new("leader"), 2, new("a"), new("speed", Speed: true), new("healer", Healer: true)),
                Is.EqualTo(new[] { "healer" }));

        [Test]
        public void SpeedClassesAreThe165Singers()
        {
            Assert.That(AutonomousRvrStaging.IsSpeedClass(eCharacterClass.Minstrel), Is.True);
            Assert.That(AutonomousRvrStaging.IsSpeedClass(eCharacterClass.Skald), Is.True);
            Assert.That(AutonomousRvrStaging.IsSpeedClass(eCharacterClass.Bard), Is.True);
            Assert.That(AutonomousRvrStaging.IsSpeedClass(eCharacterClass.Cleric), Is.False);
        }
    }
}
