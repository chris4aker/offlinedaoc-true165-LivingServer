using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>True 1.65 Mode: a crafted engine takes its tier from the 1.64 item level (elm 1, oaken 2, ironwood 3).</summary>
    [TestFixture, NonParallelizable]
    public class UT_SiegeCraftTier165
    {
        private static readonly IObjectDatabase Empty = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private sealed class Server : GameServer { protected override IObjectDatabase DataBaseImpl => Empty; }
        private GameServer _previous;
        [SetUp] public void Setup()
        { _previous = GameServer.Instance; GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server))); }
        [TearDown] public void Cleanup() => GameServer.LoadTestDouble(_previous);

        [TestCase(0, 1)]   // a level-less product is still a light engine
        [TestCase(1, 1)]   // elm
        [TestCase(2, 2)]   // oaken
        [TestCase(3, 3)]   // ironwood
        [TestCase(9, 3)]   // nothing above heavy
        public void CraftedTier_FollowsTheItemLevel(int itemLevel, int tier)
        {
            Assert.That(SiegeCrafting.CraftedTier(new DbItemTemplate { Level = itemLevel }), Is.EqualTo(tier));
        }

        [Test]
        public void CraftedTier_NullProductIsLight() => Assert.That(SiegeCrafting.CraftedTier(null), Is.EqualTo(1));

        [Test]
        public void IronwoodRam_IsTheHeavyRam()
        {
            var ram = new GameSiegeRam { Level = SiegeCrafting.CraftedTier(new DbItemTemplate { Level = 3 }) };
            Assert.That(ram.MAX_PASSENGERS, Is.EqualTo(12));
            Assert.That(ram.BaseDamage, Is.EqualTo(200));
        }
    }
}
