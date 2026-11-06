using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;
using static DOL.GS.AutonomousFrontierHotspots;

namespace DOL.UnitTests
{
    /// <summary>Item 52: True 1.65 roaming weighs the old-frontier milegates and the roads out of the enemy portal keeps.</summary>
    [TestFixture]
    public class UT_FrontierHotspots165
    {
        // Albion's frontier (region 1) as installed: Hadrian's Wall doors and the keeps around them (opendaoc-165 Door/Keep rows).
        private static readonly DoorPoint[] AlbionDoors =
        {
            new(new(610032, 319259, 3872), "Hadrian's Wall"), new(new(610032, 319678, 3872), "Hadrian's Wall"),  // west milegate
            new(new(652307, 320470, 4312), "Hadrian's Wall"), new(new(652111, 320874, 4312), "Hadrian's Wall"),  // east milegate
            new(new(654562, 293915, 4859), "Hadrian's Wall"), new(new(606557, 294445, 4824), "Hadrian's Wall"),  // portal keep gates
            new(new(652009, 346442, 6346), "Hadrian's Wall"),                                                     // Caer Benowyc
            new(new(585178, 478294, 2600), "Forest Sauvage"),                                                     // Castle Sauvage
        };

        private static readonly KeepPoint[] AlbionKeeps =
        {
            new(new(605589, 293789, 4839), "Hibernia Portal Keep", true),
            new(new(655269, 293142, 4879), "Midgard Portal Keep", true),
            new(new(653430, 345890, 6280), "Caer Benowyc", false),
        };

        private static Anchor[] Albion() => Derive(1, AlbionDoors, AlbionKeeps, new[] { new Vector3(585085, 477504, 2600) });

        [Test]
        public void TheTwoHadriansWallMilegatesAreFound()
        {
            Anchor[] gates = Albion().Where(a => a.Kind == Kind.Milegate).ToArray();
            Assert.That(gates.Length, Is.EqualTo(2), "keep, portal keep and border keep doors are not milegates");
            Assert.That(gates.All(g => g.Name == "the milegate in Hadrian's Wall"), Is.True);
            Assert.That(gates.Any(g => g.X == 610032 && g.Y == (319259 + 319678) / 2), Is.True, "the two leaves are one gate");
        }

        [Test]
        public void OutsideEachEnemyPortalKeepOnTheRoadToTheNearestMilegate()
        {
            Anchor[] outside = Albion().Where(a => a.Kind == Kind.OutsidePortalKeep).ToArray();
            Assert.That(outside.Select(a => a.Name), Is.EquivalentTo(new[] { "outside Hibernia Portal Keep", "outside Midgard Portal Keep" }));
            Anchor hib = outside.Single(a => a.Name.Contains("Hibernia"));
            float fromKeep = Vector2.Distance(new(hib.X, hib.Y), new(605589, 293789));
            Assert.That(fromKeep, Is.EqualTo(OutsidePortalKeepDistance).Within(2));
            Assert.That(hib.Y, Is.GreaterThan(293789), "toward the west milegate, south of the keep");
        }

        [Test]
        public void NoMilegateNoInventedPlaces() =>
            Assert.That(Derive(1, AlbionDoors.Where(d => d.Position.Y > 340_000 || d.Position.Y < 300_000), AlbionKeeps, new[] { new Vector3(585085, 477504, 2600) }),
                Is.Empty);

        [Test]
        public void OnlyTheOldFrontierRegions()
        {
            Assert.That(IsOldFrontier(1) && IsOldFrontier(100) && IsOldFrontier(200), Is.True);
            Assert.That(IsOldFrontier(163), Is.False);
            Assert.That(For(51), Is.Empty);
        }

        private static AutonomousRvrEventLayer.LiveObjective Roam(string id, int weight = 1) =>
            new(id, id, AutonomousRvrEventLayer.Intent.Roam, eRealm.None, 1, 0, 0, 0, false, 0, 0, 0, 0, Weight: weight);

        [Test]
        public void HotspotsAreWeightedRoamingPicks()
        {
            var objectives = new[] { Roam("keep"), Roam("camp"), Roam("milegate", RoamWeight) };
            int milegate = Enumerable.Range(0, 800)
                .Count(i => AutonomousRvrEventLayer.PickRoam(objectives, i / 800d).Id == "milegate");
            Assert.That(milegate, Is.EqualTo(800 * RoamWeight / (RoamWeight + 2)).Within(2));
        }

        [Test]
        public void UnweightedRoamingStaysEven()
        {
            var objectives = new[] { Roam("a"), Roam("b"), Roam("c"), Roam("d") };
            var counts = Enumerable.Range(0, 400).Select(i => AutonomousRvrEventLayer.PickRoam(objectives, i / 400d).Id)
                .GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
            Assert.That(counts.Values, Is.All.EqualTo(100));
        }

        [Test]
        public void NoRoamNoPick()
        {
            var hunt = new AutonomousRvrEventLayer.LiveObjective("e", "e", AutonomousRvrEventLayer.Intent.HuntEnemy, eRealm.Midgard, 1,
                0, 0, 0, false, 1, 0, 0, 0);
            Assert.That(AutonomousRvrEventLayer.PickRoam(new[] { hunt }, 0.5), Is.Null);
        }
    }
}
