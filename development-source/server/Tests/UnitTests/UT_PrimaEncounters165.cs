using System;
using System.Linq;
using DOL.AI.Brain;
using DOL.GS.Scripts;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // 2026-11-07: the Prima encounter scripts (scripts/true165/PrimaEncounters165.cs) - the pure parts.
    [TestFixture]
    public class UT_PrimaEncounters165
    {
        [Test]
        public void CuinnSeedlingsComeOutOfTownAroundHisSpawnPoint()
        {
            var ring = PrimaEncounter165.Ring(1000, 2000, 220, 6).ToList();
            Assert.That(ring, Has.Count.EqualTo(6));
            foreach ((int x, int y) in ring)
                Assert.That(Math.Sqrt((x - 1000) * (x - 1000) + (y - 2000) * (y - 2000)), Is.EqualTo(220).Within(1));
            Assert.That(ring.Distinct().Count(), Is.EqualTo(6));
        }

        [Test]
        public void HeadingFacesTheTarget() => Assert.Multiple(() =>
        {
            Assert.That(PrimaEncounter165.HeadingTo(0, 0, 0, -100), Is.EqualTo(0), "north");
            Assert.That(PrimaEncounter165.HeadingTo(0, 0, 0, 100), Is.EqualTo(2048), "south");
        });

        [Test]
        public void IxthiarCreeperStaysInTheIxthiarWoodAndAwayFromAalidFeie()
        {
            var rnd = new Random(7);
            int x = 21000, y = 19000;
            for (int i = 0; i < 500; i++)
            {
                (x, y) = IxthiarCreeperBrain.NextLeg(x, y, i == 0, rnd);
                Assert.That(x, Is.InRange(IxthiarCreeperBrain.MinX, IxthiarCreeperBrain.MaxX));
                Assert.That(y, Is.InRange(IxthiarCreeperBrain.MinY, IxthiarCreeperBrain.MaxY));
                double d = Math.Sqrt(Math.Pow(x - IxthiarCreeperBrain.AalidX, 2) + Math.Pow(y - IxthiarCreeperBrain.AalidY, 2));
                Assert.That(d, Is.GreaterThanOrEqualTo(IxthiarCreeperBrain.AalidKeepOut - 2), $"leg {i} entered the grove");
            }
        }

        [Test]
        public void IxthiarCreeperFirstLegLeavesTheBrood()
        {
            var rnd = new Random(3);
            for (int i = 0; i < 50; i++)
            {
                (int x, int y) = IxthiarCreeperBrain.NextLeg(21000, 19000, true, rnd);
                double before = Math.Sqrt(Math.Pow(21000 - IxthiarCreeperBrain.NestX, 2) + Math.Pow(19000 - IxthiarCreeperBrain.NestY, 2));
                double after = Math.Sqrt(Math.Pow(x - IxthiarCreeperBrain.NestX, 2) + Math.Pow(y - IxthiarCreeperBrain.NestY, 2));
                Assert.That(after, Is.GreaterThan(before), "the first leg leads away from the nest");
            }
        }
        [Test]
        public void CelainComesBackAtNineInTheMorning() => Assert.Multiple(() =>
        {
            const uint hour = 60 * 60 * 1000;
            Assert.That(CelainEirich165.MillisecondsToNineAm(8 * hour, 1), Is.EqualTo(hour), "08:00 -> one game hour");
            Assert.That(CelainEirich165.MillisecondsToNineAm(10 * hour, 1), Is.EqualTo(23L * hour), "10:00 -> tomorrow");
            Assert.That(CelainEirich165.MillisecondsToNineAm(9 * hour, 1), Is.EqualTo(24L * hour), "at 09:00 -> the next day");
            Assert.That(CelainEirich165.MillisecondsToNineAm(8 * hour, 24), Is.EqualTo(hour / 24), "game time runs 24x faster");
        });
    }
}
