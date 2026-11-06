using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>True 1.65 Mode siege ammunition (owner 2026-11-06: "No ammo, no shot"; SiegeAmmo165).</summary>
    [TestFixture, NonParallelizable]
    public class UT_SiegeAmmo165
    {
        private static readonly IObjectDatabase Empty = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private sealed class Server : GameServer { protected override IObjectDatabase DataBaseImpl => Empty; }
        private GameServer _previous;
        private bool _mode;

        [SetUp]
        public void Setup()
        {
            _mode = Properties.CLASSIC165_MODE;
            Properties.CLASSIC165_MODE = true;
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
        }

        [TearDown]
        public void Cleanup()
        {
            GameServer.LoadTestDouble(_previous);
            Properties.CLASSIC165_MODE = _mode;
        }

        private static DbItemTemplate Ammo(string id, string name) => new() { Id_nb = id, Name = name, MaxCount = 20 };
        private static readonly DbItemTemplate Stone = Ammo("catapult_stone", "catapult stone");
        private static readonly DbItemTemplate Bolt = Ammo("ballista_bolt2", "ballista bolt");
        private static readonly DbItemTemplate Grapeshot = Ammo("grapeshot3", "grapeshot");

        private static void BeginLoading(GameSiegeWeapon weapon, DbItemTemplate piece) =>
            typeof(GameSiegeWeapon).GetField("_loadingAmmo", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(weapon, piece);

        // ---- which ammunition, which engine

        [TestCase("catapult_stone", SiegeAmmoKind.CatapultStone)]
        [TestCase("catapult_stone2", SiegeAmmoKind.CatapultStone)]
        [TestCase("ballista_bolt3", SiegeAmmoKind.BallistaBolt)]
        [TestCase("bullet", SiegeAmmoKind.Bullet)]
        [TestCase("grapeshot2", SiegeAmmoKind.Grapeshot)]
        [TestCase("ball_of_ice3", SiegeAmmoKind.BallOfIce)]
        [TestCase("greek_fire", SiegeAmmoKind.None)]          // a later item, not 1.6x
        [TestCase("deploy_siege_catapult", SiegeAmmoKind.None)]
        [TestCase(null, SiegeAmmoKind.None)]
        public void KindOf_ReadsTheRealmCopies(string id, SiegeAmmoKind kind) =>
            Assert.That(SiegeAmmo165.KindOf(id), Is.EqualTo(kind));

        [TestCase(BotSiegeKind.Ram, false)]   // "ram doesn't need ammo" (FH 125966)
        [TestCase(BotSiegeKind.Catapult, true)]
        [TestCase(BotSiegeKind.Trebuchet, true)]
        [TestCase(BotSiegeKind.Ballista, true)]
        public void OnlyTheRamFiresWithoutAmmunition(BotSiegeKind kind, bool needs)
        {
            Assert.That(SiegeAmmo165.NeedsAmmo(kind), Is.EqualTo(needs));
            Assert.That(SiegeAmmo165.Required(kind), Is.EqualTo(needs));
            Properties.CLASSIC165_MODE = false;
            Assert.That(SiegeAmmo165.Required(kind), Is.False, "normal mode: no ammunition");
        }

        [TestCase(BotSiegeKind.Ballista, SiegeAmmoKind.BallistaBolt, true)]
        [TestCase(BotSiegeKind.Ballista, SiegeAmmoKind.CatapultStone, false)]
        [TestCase(BotSiegeKind.Catapult, SiegeAmmoKind.CatapultStone, true)]
        [TestCase(BotSiegeKind.Catapult, SiegeAmmoKind.Bullet, true)]
        [TestCase(BotSiegeKind.Trebuchet, SiegeAmmoKind.Grapeshot, true)]
        [TestCase(BotSiegeKind.Trebuchet, SiegeAmmoKind.BallOfIce, true)]
        [TestCase(BotSiegeKind.Trebuchet, SiegeAmmoKind.BallistaBolt, false)]
        [TestCase(BotSiegeKind.Ram, SiegeAmmoKind.CatapultStone, false)]
        public void AmmunitionFitsItsEngine(BotSiegeKind engine, SiegeAmmoKind ammo, bool fits) =>
            Assert.That(SiegeAmmo165.Fits(engine, ammo), Is.EqualTo(fits));

        [TestCase(eRealm.Albion, BotSiegeKind.Catapult, "catapult_stone")]
        [TestCase(eRealm.Midgard, BotSiegeKind.Trebuchet, "catapult_stone2")]
        [TestCase(eRealm.Hibernia, BotSiegeKind.Ballista, "ballista_bolt3")]
        public void BotsUseTheMerchantAmmunitionOfTheirRealm(eRealm realm, BotSiegeKind kind, string id) =>
            Assert.That(SiegeAmmo165.AmmoId(realm, SiegeAmmo165.BasicAmmo(kind)), Is.EqualTo(id));

        [Test]
        public void RamHasNoAmmunition() => Assert.That(SiegeAmmo165.AmmoId(eRealm.Albion, SiegeAmmo165.BasicAmmo(BotSiegeKind.Ram)), Is.Null);

        // ---- what a shot does

        [Test]
        public void StoneAndNoAmmunitionKeepTheEngineNumbers()
        {
            foreach (SiegeAmmoKind kind in new[] { SiegeAmmoKind.None, SiegeAmmoKind.CatapultStone, SiegeAmmoKind.BallistaBolt })
            {
                Assert.That(SiegeAmmo165.ShotDamage(200, kind), Is.EqualTo(200));
                Assert.That(SiegeAmmo165.ShotRadius(150, kind), Is.EqualTo(150));
                Assert.That(SiegeAmmo165.ShotDamageType(eDamageType.Crush, kind), Is.EqualTo(eDamageType.Crush));
            }
        }

        [Test]
        public void BulletHitsHarderInASmallerArea()
        {
            Assert.That(SiegeAmmo165.ShotDamage(200, SiegeAmmoKind.Bullet), Is.EqualTo(370));
            Assert.That(SiegeAmmo165.ShotRadius(150, SiegeAmmoKind.Bullet), Is.EqualTo(75));
        }

        [Test]
        public void GrapeshotHitsSofterInAWideArea()
        {
            Assert.That(SiegeAmmo165.ShotDamage(200, SiegeAmmoKind.Grapeshot), Is.EqualTo(140));
            Assert.That(SiegeAmmo165.ShotRadius(150, SiegeAmmoKind.Grapeshot), Is.EqualTo(450));
        }

        [Test]
        public void BallOfIceIsAColdStone()
        {
            Assert.That(SiegeAmmo165.ShotDamage(200, SiegeAmmoKind.BallOfIce), Is.EqualTo(200));
            Assert.That(SiegeAmmo165.ShotDamageType(eDamageType.Crush, SiegeAmmoKind.BallOfIce), Is.EqualTo(eDamageType.Cold));
        }

        // ---- restocking

        [TestCase(0, 1_000_000L, 1820L, 20)]  // a full stack
        [TestCase(12, 1_000_000L, 1820L, 8)]  // tops up
        [TestCase(0, 10_000L, 1820L, 5)]      // what it can pay for
        [TestCase(0, 1_000L, 1820L, 0)]
        [TestCase(25, 1_000_000L, 1820L, 0)]
        [TestCase(0, 1_000_000L, 0L, 0)]      // no price, no purchase
        public void RestockBuysUpToAStackItCanPayFor(int held, long money, long unit, int count) =>
            Assert.That(SiegeAmmo165.RestockCount(held, money, unit), Is.EqualTo(count));

        // ---- the engine

        [Test]
        public void EnginesTakeOnlyTheirAmmunitionUpToEightPieces()
        {
            var catapult = new GameSiegeCatapult();
            Assert.That(catapult.StoreAmmo(Bolt, 1), Is.EqualTo(0), "bolts don't fit a catapult");
            Assert.That(catapult.StoreAmmo(Stone, 5), Is.EqualTo(5));
            Assert.That(catapult.StoreAmmo(Grapeshot, 5), Is.EqualTo(3), "eight pieces at most");
            Assert.That(catapult.StoredAmmo, Is.EqualTo(8));
            Assert.That(catapult.AmmoRoom, Is.EqualTo(0));
            Assert.That(catapult.Ammo.Count, Is.EqualTo(2), "one list entry per kind");

            var ballista = new GameSiegeBallista();
            Assert.That(ballista.StoreAmmo(Stone, 1), Is.EqualTo(0));
            Assert.That(ballista.StoreAmmo(Bolt, 1), Is.EqualTo(1));

            Assert.That(new GameSiegeRam().StoreAmmo(Stone, 1), Is.EqualTo(0), "a ram takes none");
            Assert.That(new GameSiegeTrebuchet().StoreAmmo(Stone, 1), Is.EqualTo(1));
        }

        [Test]
        public void LoadingMovesTheSelectedPieceAndEachShotSpendsIt()
        {
            var trebuchet = new GameSiegeTrebuchet();
            trebuchet.StoreAmmo(Stone, 2);
            trebuchet.StoreAmmo(Grapeshot, 1);
            Assert.That(trebuchet.SpendLoadedRound(), Is.False, "no ammunition loaded, no shot");

            Assert.That(trebuchet.AmmoAt(1).Id_nb, Is.EqualTo("grapeshot3"), "the siege window's selection");
            Assert.That(trebuchet.AmmoAt(20).Id_nb, Is.EqualTo("catapult_stone"), "no valid selection: the first");
            BeginLoading(trebuchet, trebuchet.AmmoAt(1));
            trebuchet.AmmoLoaded();
            Assert.That(trebuchet.LoadedAmmo?.Id_nb, Is.EqualTo("grapeshot3"));
            Assert.That(trebuchet.StoredAmmo, Is.EqualTo(2));
            Assert.That(trebuchet.AmmoRoom, Is.EqualTo(5), "the loaded piece takes a place");

            Assert.That(trebuchet.SpendLoadedRound(), Is.True);
            Assert.That(trebuchet.ShotAmmo, Is.EqualTo(SiegeAmmoKind.Grapeshot));
            Assert.That(trebuchet.LoadedAmmo, Is.Null);
            Assert.That(trebuchet.SpendLoadedRound(), Is.False, "one shot per piece");
            Assert.That(trebuchet.HasAmmo, Is.True);

            BeginLoading(trebuchet, trebuchet.AmmoAt(0));
            trebuchet.AmmoLoaded();
            BeginLoading(trebuchet, trebuchet.AmmoAt(0));
            trebuchet.AmmoLoaded();   // already loaded: the second piece stays in the engine
            Assert.That(trebuchet.StoredAmmo, Is.EqualTo(1));
            Assert.That(trebuchet.SpendLoadedRound(), Is.True);
            Assert.That(trebuchet.ShotAmmo, Is.EqualTo(SiegeAmmoKind.CatapultStone));
        }

        [Test]
        public void LoadingTakesFiveSecondsInTrue165()
        {
            var catapult = new GameSiegeCatapult();
            Assert.That(catapult.GetActionDelay(SiegeTimer.eAction.Loading), Is.EqualTo(5000));
            Properties.CLASSIC165_MODE = false;
            Assert.That(catapult.GetActionDelay(SiegeTimer.eAction.Loading), Is.EqualTo(0));
        }

        [Test]
        public void NormalModeFiresWithoutAmmunition()
        {
            Properties.CLASSIC165_MODE = false;
            var catapult = new GameSiegeCatapult();
            Assert.That(SiegeAmmo165.UsesAmmo(catapult), Is.False);
            Assert.That(catapult.SpendLoadedRound(), Is.True);
            Assert.That(catapult.ShotAmmo, Is.EqualTo(SiegeAmmoKind.None));
        }

        [Test]
        public void RamsAndCauldronsNeverNeedAmmunition()
        {
            Assert.That(SiegeAmmo165.UsesAmmo(new GameSiegeRam()), Is.False);
            Assert.That(SiegeAmmo165.UsesAmmo(new GameSiegeCauldron()), Is.False);
            Assert.That(new GameSiegeRam().SpendLoadedRound(), Is.True);
        }

        [Test]
        public void BotCrewWithoutAmmunitionIsNotReadyAndRamCrewAlwaysIs()
        {
            // No bot, no engine, nothing carried: an artillery crew has nothing to fire (and makes no restock attempt).
            Assert.That(SiegeAmmo165.Ready(null, null, BotSiegeKind.Catapult, 0), Is.False);
            Assert.That(SiegeAmmo165.Ready(null, null, BotSiegeKind.Ram, 0), Is.True);
            var engine = new GameSiegeCatapult();
            engine.StoreAmmo(Stone, 1);
            Assert.That(SiegeAmmo165.Ready(null, engine, BotSiegeKind.Catapult, 0), Is.True, "ammunition left in the engine");
            Properties.CLASSIC165_MODE = false;
            Assert.That(SiegeAmmo165.Ready(null, null, BotSiegeKind.Catapult, 0), Is.True, "normal mode");
        }
    }
}
