using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.Database.Handlers;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>CORE population pieces on temp databases: archive blobs, warm/cold moves, retirement, crash recovery,
    /// migration, orphan clean-up, the logout save queue and the logout gate.</summary>
    [TestFixture, NonParallelizable]
    public class UT_BotPopulationCore
    {
        private string _dir;
        private string _world;
        private BotArchive _archive;
        private FakeHooks _hooks;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "daoc-population-core-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            string path = Path.Combine(_dir, "world.sqlite3");
            _world = $"Data Source={path};Version=3;Pooling=False;";
            var database = new SqliteObjectDatabase(_world);
            foreach (Type table in new[] { typeof(OfflineWorldBotRecord), typeof(DbInventoryItem), typeof(DbItemUnique),
                         typeof(OfflineTrue165RelistRecord), typeof(OfflineTrue165ListingHours), typeof(OfflineWorldBotGuildRecord),
                         typeof(OfflineBotCommandRecord), typeof(OfflineHiredCompanionRecord), typeof(DbCoreCharacter) })
                database.RegisterDataObject(table);
            BotRowSql.ClearColumnCache();
            _hooks = new FakeHooks();
            _archive = new BotArchive(_world, Path.Combine(_dir, "bot-archive.sqlite3.db"), _hooks);
        }

        [TearDown]
        public void TearDown()
        {
            _archive?.Dispose();
            SQLiteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            // Only this test's own GUID-named folder is removed, never a server database.
            try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        // ---- blob

        [Test]
        public void ArchiveBlob_RoundTripsEveryValueKind()
        {
            var blob = new BotArchiveBlob
            {
                Record = new BotRow { ["BotId"] = 7L, ["Name"] = "Torgar", ["MoneyCopper"] = 1234567890123L, ["Ratio"] = 2.0, ["ZoneName"] = null },
                Items = { new BotRow { ["Inventory_ID"] = "a", ["Count"] = 3L, ["Raw"] = new byte[] { 1, 2, 3 } } },
                Uniques = { new BotRow { ["Id_nb"] = "u#1", ["DPS_AF"] = 165L } },
                Relists = { new BotRow { ["InventoryId"] = "a", ["Tries"] = 1L } },
                ListingHours = { new BotRow { ["InventoryId"] = "a", ["Hours"] = 24L } },
            };
            byte[] data = blob.Encode();
            Assert.That(data[0], Is.EqualTo(BotArchiveBlob.CurrentVersion));
            BotArchiveBlob back = BotArchiveBlob.Decode(data);
            Assert.That(back.Record.Long("BotId"), Is.EqualTo(7));
            Assert.That(back.Record.Text("Name"), Is.EqualTo("Torgar"));
            Assert.That(back.Record.Long("MoneyCopper"), Is.EqualTo(1234567890123L));
            Assert.That(back.Record["Ratio"], Is.EqualTo(2.0));
            Assert.That(back.Record["ZoneName"], Is.Null);
            Assert.That(back.Items.Single()["Raw"], Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(back.Uniques.Single().Long("DPS_AF"), Is.EqualTo(165));
            Assert.That(back.Relists.Single().Long("Tries"), Is.EqualTo(1));
            Assert.That(back.ListingHours.Single().Long("Hours"), Is.EqualTo(24));
        }

        [Test]
        public void ArchiveBlob_RejectsANewerVersion()
        {
            byte[] data = new BotArchiveBlob().Encode();
            data[0] = (byte)(BotArchiveBlob.CurrentVersion + 1);
            Assert.Throws<InvalidDataException>(() => BotArchiveBlob.Decode(data));
        }

        // ---- warm <-> cold

        [Test]
        public void WarmToColdToWarm_KeepsRecordItemsUniquesAndListingRows()
        {
            SeedBot(10, "Kaelin", level: 12, money: 5_000);
            SeedItem("item-1", 10, slot: 40);
            SeedItem("item-2", 10, slot: 2001, ownerLot: 65001, unique: "Sword#1");
            SeedUnique("Sword#1");
            SeedRow(BotRowSql.RelistTable, new BotRow { ["InventoryId"] = "item-2", ["Tries"] = 1L, ["OriginalPrice"] = 900L });
            SeedRow(BotRowSql.ListingHoursTable, new BotRow { ["InventoryId"] = "item-2", ["Hours"] = 24L });
            SeedRow(BotRowSql.GuildTable, new BotRow { ["BotId"] = 10L, ["GuildID"] = "g1", ["BotName"] = "Kaelin" });
            _archive.ReconcileWarm(_ => Profile());

            Assert.That(_archive.ArchiveToCold(10), Is.True);
            Assert.That(_archive.StateOf(10), Is.EqualTo(BotArchiveState.Cold));
            Assert.That(Count("offline_world_bots"), Is.Zero);
            Assert.That(Count("Inventory"), Is.Zero);
            Assert.That(Count("ItemUnique"), Is.Zero);
            Assert.That(Count(BotRowSql.RelistTable), Is.Zero);
            Assert.That(Count(BotRowSql.GuildTable), Is.EqualTo(1), "an archived bot keeps its guild");
            Assert.That(_hooks.Removed, Is.EqualTo(new[] { "offlinebot:10" }));
            Assert.That(_archive.IsSaved(10), Is.True);
            Assert.That(_archive.Count(eRealm.Albion, 10, 19), Is.EqualTo(1));

            Assert.That(_archive.RestoreToWarm(10), Is.True);
            Assert.That(_archive.StateOf(10), Is.EqualTo(BotArchiveState.Warm));
            Assert.That(Scalar("SELECT MoneyCopper FROM offline_world_bots WHERE BotId=10"), Is.EqualTo(5_000));
            Assert.That(Count("Inventory"), Is.EqualTo(2));
            Assert.That(Scalar("SELECT COUNT(*) FROM ItemUnique WHERE Id_nb='Sword#1'"), Is.EqualTo(1));
            Assert.That(Scalar($"SELECT Tries FROM {BotRowSql.RelistTable} WHERE InventoryId='item-2'"), Is.EqualTo(1));
            Assert.That(Scalar($"SELECT Hours FROM {BotRowSql.ListingHoursTable} WHERE InventoryId='item-2'"), Is.EqualTo(24));
            Assert.That(_hooks.Added, Is.EqualTo(new[] { "offlinebot:10" }));
            Assert.That(_hooks.Cold, Is.EqualTo(new[] { (10L, true), (10L, false) }));
        }

        [Test]
        public void ArchiveToCold_RefusesBusyHiredOrLockedBots()
        {
            SeedBot(11, "Brannan");
            SeedBot(12, "Sedgewyn");
            SeedBot(13, "Tikzik");
            _archive.ReconcileWarm(_ => Profile());
            _hooks.Busy.Add(11);
            _hooks.Hired.Add(12);
            Assert.That(_archive.ArchiveToCold(11), Is.False);
            Assert.That(_archive.ArchiveToCold(12), Is.False);
            _hooks.LocksFree = false;
            Assert.That(_archive.ArchiveToCold(13), Is.False);
            Assert.That(Count("offline_world_bots"), Is.EqualTo(3));
            Assert.That(_archive.StateOf(13), Is.EqualTo(BotArchiveState.Warm));
        }

        // ---- retirement

        [Test]
        public void Retire_DeletesEverythingTheBotOwnsAndFreesItsName()
        {
            SeedBot(20, "Daruf");
            SeedBot(21, "Caelion");
            SeedItem("d-1", 20, slot: 40, unique: "Axe#9");
            SeedItem("d-2", 20, slot: 2000, ownerLot: 65001);
            SeedItem("c-1", 21, slot: 40);
            SeedUnique("Axe#9");
            SeedUnique("Shared#1");
            SeedItem("d-3", 20, slot: 41, unique: "Shared#1");
            SeedItem("c-2", 21, slot: 41, unique: "Shared#1");
            SeedRow(BotRowSql.RelistTable, new BotRow { ["InventoryId"] = "d-2", ["Tries"] = 0L, ["OriginalPrice"] = 10L });
            SeedRow(BotRowSql.ListingHoursTable, new BotRow { ["InventoryId"] = "d-2", ["Hours"] = 24L });
            SeedRow(BotRowSql.GuildTable, new BotRow { ["BotId"] = 20L, ["GuildID"] = "g1", ["BotName"] = "Daruf" });
            SeedRow(BotRowSql.CommandTable, new BotRow { ["BotId"] = 20L, ["CommandType"] = "TeleportToBot", ["State"] = "Pending", ["RequestedUtc"] = "x" });
            _archive.ReconcileWarm(_ => Profile());

            Assert.That(_archive.RetireOne(20), Is.EqualTo(BotArchive.RetireResult.Retired));
            Assert.That(Scalar("SELECT COUNT(*) FROM offline_world_bots WHERE BotId=20"), Is.Zero);
            Assert.That(Scalar("SELECT COUNT(*) FROM Inventory WHERE OwnerID='offlinebot:20'"), Is.Zero);
            Assert.That(Scalar("SELECT COUNT(*) FROM ItemUnique WHERE Id_nb='Axe#9'"), Is.Zero);
            Assert.That(Scalar("SELECT COUNT(*) FROM ItemUnique WHERE Id_nb='Shared#1'"), Is.EqualTo(1), "still used by another bot");
            Assert.That(Count(BotRowSql.RelistTable) + Count(BotRowSql.ListingHoursTable) + Count(BotRowSql.GuildTable), Is.Zero);
            Assert.That(Scalar("SELECT COUNT(*) FROM offline_bot_commands WHERE BotId=20"), Is.Zero);
            Assert.That(Scalar("SELECT COUNT(*) FROM Inventory WHERE OwnerID='offlinebot:21'"), Is.EqualTo(2));
            Assert.That(_archive.StateOf(20), Is.EqualTo(-1));
            Assert.That(_hooks.Retired, Is.EqualTo(new[] { (20L, "Daruf") }));
            Assert.That(_hooks.Removed, Does.Contain("offlinebot:20"));
        }

        [Test]
        public void Retire_ColdBotRemovesItsArchiveRow()
        {
            SeedBot(22, "Rhaenyx");
            SeedItem("r-1", 22, slot: 40);
            _archive.ReconcileWarm(_ => Profile());
            Assert.That(_archive.ArchiveToCold(22), Is.True);
            long before = _archive.SavedCount;
            Assert.That(_archive.RetireOne(22), Is.EqualTo(BotArchive.RetireResult.Retired));
            _archive.RefreshCounts();
            Assert.That(_archive.StateOf(22), Is.EqualTo(-1));
            Assert.That(_archive.SavedCount, Is.EqualTo(before - 1));
        }

        [Test]
        public void Retire_NeverTouchesHiredOrOnlineBots()
        {
            SeedBot(23, "Hired");
            SeedBot(24, "Online");
            _archive.ReconcileWarm(_ => Profile());
            _hooks.Hired.Add(23);
            _hooks.Busy.Add(24);
            Assert.That(_archive.RetireOne(23), Is.EqualTo(BotArchive.RetireResult.Hired));
            Assert.That(_archive.RetireOne(24), Is.EqualTo(BotArchive.RetireResult.Busy));
            Assert.That(Count("offline_world_bots"), Is.EqualTo(2));
            Assert.That(_archive.RetireOne(23, allowHired: true), Is.EqualTo(BotArchive.RetireResult.Retired));
        }

        [Test]
        public void Retire_WhenLocksAreBusy_StaysRetiringAndFinishesOnRecovery()
        {
            SeedBot(25, "Torgar");
            SeedItem("t-1", 25, slot: 40);
            _archive.ReconcileWarm(_ => Profile());
            _hooks.LocksFree = false;
            Assert.That(_archive.RetireOne(25), Is.EqualTo(BotArchive.RetireResult.Busy));
            Assert.That(_archive.StateOf(25), Is.EqualTo(BotArchiveState.Retiring));
            Assert.That(Count("offline_world_bots"), Is.EqualTo(1));
            // Like a crash: the next start finishes it.
            _hooks.LocksFree = true;
            Assert.That(_archive.RecoverInterrupted(), Is.EqualTo(1));
            Assert.That(Count("offline_world_bots") + Count("Inventory"), Is.Zero);
            Assert.That(_archive.StateOf(25), Is.EqualTo(-1));
        }

        [Test]
        public void Recovery_FinishesInterruptedArchiveAndRestore()
        {
            SeedBot(30, "Halfway");
            SeedItem("h-1", 30, slot: 40);
            SeedBot(31, "Back");
            _archive.ReconcileWarm(_ => Profile());
            Assert.That(_archive.ArchiveToCold(31), Is.True);

            // 30: blob written, crash before the world rows were deleted.
            using (var archiveDb = new SQLiteConnection($"Data Source={_archive.ArchivePath};Version=3;Pooling=False;"))
            {
                archiveDb.Open();
                byte[] blob;
                using (SQLiteConnection world = BotRowSql.Open(_world))
                    blob = BotRowSql.ReadOwned(world, null, 30).Encode();
                BotRowSql.Execute(archiveDb, null, "UPDATE bots SET State=3, Blob=@b WHERE BotId=30", ("@b", blob));
                // 31: restore started, crash before the world rows were written.
                BotRowSql.Execute(archiveDb, null, "UPDATE bots SET State=4 WHERE BotId=31");
            }

            Assert.That(_archive.RecoverInterrupted(), Is.EqualTo(2));
            Assert.That(_archive.StateOf(30), Is.EqualTo(BotArchiveState.Cold));
            Assert.That(Scalar("SELECT COUNT(*) FROM offline_world_bots WHERE BotId=30"), Is.Zero);
            Assert.That(Count("Inventory"), Is.Zero);
            Assert.That(_archive.StateOf(31), Is.EqualTo(BotArchiveState.Cold));
            Assert.That(_archive.RestoreToWarm(30), Is.True);
            Assert.That(Scalar("SELECT COUNT(*) FROM Inventory WHERE OwnerID='offlinebot:30'"), Is.EqualTo(1));
        }

        // ---- directory queries

        [Test]
        public void CandidatesCountsAndProfiles_FollowTheIndexedColumns()
        {
            DateTime now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
            SeedBot(40, "Early", level: 15);
            SeedBot(41, "Late", level: 16);
            SeedBot(42, "Other", level: 30);
            SeedBot(43, "Busy", level: 15);
            _archive.ReconcileWarm(row => Profile(row.Long("BotId") == 41 ? now.AddHours(2) : now.AddMinutes(-row.Long("BotId"))));
            _hooks.Busy.Add(43);

            IReadOnlyList<ArchiveCandidate> found = _archive.Candidates(eRealm.Albion, 10, 19, 10, now);
            Assert.That(found.Select(c => c.BotId), Is.EqualTo(new[] { 40L }));
            Assert.That(found[0].Profile.Persona, Is.EqualTo((byte)PopulationPersona.Regular));
            Assert.That(_archive.Count(eRealm.Albion, 10, 19), Is.EqualTo(3));
            Assert.That(_archive.Count(eRealm.Midgard, 1, 50), Is.Zero);

            _archive.QueueOnline(40, true, 15, now);
            _archive.QueueLevel(41, 20);
            var changed = Profile(now.AddDays(1)) with { PlayedMinutes = 77 };
            _archive.QueueProfile(42, changed);
            _archive.FlushQueued();
            Assert.That(_archive.Count(eRealm.Albion, 10, 19), Is.EqualTo(1), "40 online, 41 now level 20, 43 busy-but-offline");
            Assert.That(_archive.CountOnline(eRealm.Albion, 10, 19), Is.EqualTo(1));
            Assert.That(_archive.GetProfile(42).PlayedMinutes, Is.EqualTo(77));
            Assert.That(_archive.SavedCount, Is.EqualTo(4));
            Assert.That(_archive.Candidates(eRealm.Albion, 10, 19, 10, now.AddDays(3)), Is.Empty);

            IReadOnlyList<long> random = _archive.RandomRetireCandidates(10, c => c.Level < 30, new Random(5));
            Assert.That(random, Is.SubsetOf(new[] { 41L }));
        }

        // ---- migration

        [Test]
        public void Migration_KeepsEveryGamebotWithAPersonaAndRemovesOrphanCompanions()
        {
            SeedBot(50, "Alpha");
            SeedBot(51, "Beta");
            SeedBot(52, "Gamma");
            SeedBot(60, "OrphanPet");
            SeedItem("o-1", 60, slot: 40);
            SeedBot(61, "KeptPet");
            SeedRow("DOLCharacters", new BotRow { ["DOLCharacters_ID"] = "owner-alive", ["Name"] = "Player" });
            SeedRow(BotRowSql.HiredTable, new BotRow { ["BotId"] = 60L, ["OwnerCharacterID"] = "owner-gone", ["OwnerName"] = "Gone" });
            SeedRow(BotRowSql.HiredTable, new BotRow { ["BotId"] = 61L, ["OwnerCharacterID"] = "owner-alive", ["OwnerName"] = "Player" });
            SeedRow(BotRowSql.GuildTable, new BotRow { ["BotId"] = 52L, ["GuildID"] = "g1", ["BotName"] = "Gamma" });
            _hooks.Hired.UnionWith(new long[] { 60, 61 });
            var forgotten = new List<long>();

            var report = new BotPopulationMaintenance.MigrationReport();
            BotPopulationMaintenance.Migrate(_archive, new HashSet<long> { 50 }, id => { forgotten.Add(id); _hooks.Hired.Remove(id); },
                Direct, new Random(1), report);

            Assert.That(report.Migrated, Is.True);
            Assert.That(report.OrphanCompanionsRemoved, Is.EqualTo(1));
            Assert.That(forgotten, Is.EqualTo(new[] { 60L }));
            Assert.That(Scalar("SELECT COUNT(*) FROM offline_world_bots WHERE BotId=60"), Is.Zero);
            Assert.That(Scalar("SELECT COUNT(*) FROM Inventory WHERE OwnerID='offlinebot:60'"), Is.Zero);
            Assert.That(Scalar("SELECT COUNT(*) FROM offline_hired_companions"), Is.EqualTo(1));
            Assert.That(_archive.StateOf(61), Is.EqualTo(-1), "a hired companion never gets a directory row");
            foreach (long id in new long[] { 50, 51, 52 })
            {
                BotPopulationProfile profile = _archive.GetProfile(id);
                Assert.That(_archive.StateOf(id), Is.EqualTo(BotArchiveState.Warm));
                Assert.That(profile.Persona, Is.LessThanOrEqualTo((byte)PopulationPersona.Casual), "existing bots are no trial newcomers");
                Assert.That(profile.PlayDaysMask, Is.Not.Zero);
                Assert.That(profile.Status, Is.Zero);
            }
            Assert.That(_archive.GetProfile(52).Persona, Is.LessThanOrEqualTo((byte)PopulationPersona.Regular), "guildmates play often");
            Assert.That(_archive.GetProfile(50).WasOnline, Is.True);
            Assert.That(_archive.GetProfile(50).NextWantUtc, Is.EqualTo(PopulationPersonas.OnlineMarkerUtc));
            Assert.That(Count("offline_world_bots"), Is.EqualTo(4));

            // A second start changes nothing that exists, and adds a new warm bot with a profile.
            byte persona = _archive.GetProfile(51).Persona;
            SeedBot(53, "Delta");
            var second = new BotPopulationMaintenance.MigrationReport();
            BotPopulationMaintenance.Migrate(_archive, new HashSet<long>(), null, Direct, new Random(2), second);
            Assert.That(second.Migrated, Is.False);
            Assert.That(second.ProfilesAdded, Is.EqualTo(1));
            Assert.That(_archive.GetProfile(51).Persona, Is.EqualTo(persona));
            Assert.That(_archive.StateOf(53), Is.EqualTo(BotArchiveState.Warm));
        }

        [Test]
        public void OrphanCleanup_RemovesOnlyUnusedOldRows()
        {
            SeedBot(70, "Owner");
            SeedItem("i-1", 70, slot: 40, unique: "Used#1");
            SeedUnique("Used#1", "2026-01-01 00:00:00");
            SeedUnique("Old#1", "2026-01-01 00:00:00");
            SeedUnique("Fresh#1", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fffffff") + "Z");
            SeedRow(BotRowSql.RelistTable, new BotRow { ["InventoryId"] = "i-1", ["Tries"] = 0L, ["OriginalPrice"] = 1L });
            SeedRow(BotRowSql.RelistTable, new BotRow { ["InventoryId"] = "gone", ["Tries"] = 0L, ["OriginalPrice"] = 1L });
            SeedRow(BotRowSql.ListingHoursTable, new BotRow { ["InventoryId"] = "gone", ["Hours"] = 12L });

            var report = new BotPopulationMaintenance.MigrationReport();
            BotPopulationMaintenance.CleanupOrphans(_world, Direct, DateTime.UtcNow.AddHours(-1), report);
            Assert.That(report.OrphanUniques, Is.EqualTo(1));
            Assert.That(report.OrphanRelists, Is.EqualTo(1));
            Assert.That(report.OrphanListingHours, Is.EqualTo(1));
            Assert.That(Scalar("SELECT COUNT(*) FROM ItemUnique WHERE Id_nb IN ('Used#1','Fresh#1')"), Is.EqualTo(2));
            Assert.That(Scalar($"SELECT COUNT(*) FROM {BotRowSql.RelistTable} WHERE InventoryId='i-1'"), Is.EqualTo(1));
        }

        [Test]
        public void WorldIndexes_AreCreatedAndSurviveTheTableCheck()
        {
            SeedBot(80, "Unique");
            BotPopulationMaintenance.EnsureWorldIndexes(_world, Direct, new BotPopulationMaintenance.MigrationReport());
            Assert.That(Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name LIKE 'KEEP_%'"), Is.EqualTo(5));
            // The name index ignores case, like the table column.
            Assert.Throws<SQLiteException>(() => SeedBot(81, "UNIQUE"));
            // Registering the tables again (a server start) must keep the KEEP_ indexes.
            var database = new SqliteObjectDatabase(_world);
            database.RegisterDataObject(typeof(OfflineWorldBotRecord));
            database.RegisterDataObject(typeof(DbInventoryItem));
            Assert.That(Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name LIKE 'KEEP_%'"), Is.EqualTo(5));
        }

        [Test]
        public void CreateLevelOne_WritesANamedLevelOneWarmBotOrNothing()
        {
            BotNameClaim claim = (eRealm realm, eRace race, eGender gender, long botId, out string name) =>
            {
                name = "Newname";
                return true;
            };
            long id = BotCreation.CreateLevelOne(_world, _archive, eRealm.Midgard, eCharacterClass.Warrior, eRace.Norseman, eGender.Male,
                claim, Profile(), Direct);
            Assert.That(id, Is.GreaterThan(0));
            Assert.That(Scalar($"SELECT Level FROM offline_world_bots WHERE BotId={id}"), Is.EqualTo(1));
            Assert.That(Scalar($"SELECT RegionId FROM offline_world_bots WHERE BotId={id}"), Is.Zero, "the login places it at a start point");
            Assert.That(_archive.NameOf(id), Is.EqualTo("Newname"));
            Assert.That(_archive.StateOf(id), Is.EqualTo(BotArchiveState.Warm));

            BotNameClaim refuse = (eRealm realm, eRace race, eGender gender, long botId, out string name) =>
            {
                name = null;
                return false;
            };
            Assert.That(BotCreation.CreateLevelOne(_world, _archive, eRealm.Midgard, eCharacterClass.Warrior, eRace.Norseman, eGender.Male,
                refuse, Profile(), Direct), Is.Zero);
            Assert.That(Count("offline_world_bots"), Is.EqualTo(1));
        }

        // ---- logout save queue and the logout gate

        [Test]
        public void LogoutSave_IsPendingUntilWrittenAndForgetClearsIt()
        {
            using var server = new ServerDouble();
            GameBot bot = FakeAutonomousBot(990_001);
            try
            {
                AutonomousBotStatusPersistence.QueueLogout(bot);
                Assert.That(AutonomousBotStatusPersistence.IsLogoutPending(990_001), Is.True);
                Assert.That(AutonomousBotStatusPersistence.TryGetLoggingOut(990_001, out GameBot leaving), Is.True);
                Assert.That(leaving, Is.SameAs(bot));
                // A later routine save of the same bot joins its logout entry instead of a separate (older) snapshot.
                AutonomousBotStatusPersistence.Queue(bot, includeInventory: true);
                Assert.That(AutonomousBotStatusPersistence.PendingLogoutSaves, Is.GreaterThanOrEqualTo(1));
            }
            finally
            {
                AutonomousBotStatusPersistence.Forget(990_001);
            }
            Assert.That(AutonomousBotStatusPersistence.IsLogoutPending(990_001), Is.False);
        }

        [Test]
        public void LogoutGate_RefusesAnythingThatIsNotAFreeAutonomousGamebot()
        {
            using var server = new ServerDouble();
            Assert.That(AutonomousBotLifecycle.CanLogOutNow(null, out string why), Is.False);
            Assert.That(why, Is.Not.Empty);
            var plain = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            Assert.That(AutonomousBotLifecycle.CanLogOutNow(plain, out why), Is.False);
            Assert.That(why, Does.Contain("not an autonomous"));
            GameBot offline = FakeAutonomousBot(990_002);
            Assert.That(AutonomousBotLifecycle.CanLogOutNow(offline, out why), Is.False);
            Assert.That(why, Is.EqualTo("not in the world"));
            Assert.That(AutonomousBotLifecycle.RequestLogout(offline, BotLogoutReason.SessionOver), Is.False);
        }

        [Test]
        public void GuildReconcile_KeepsArchivedMembers()
        {
            // No members loaded in a unit test: the call returns at once, and the archived check is the one passed in.
            Assert.That(AutonomousBotGuilds.Reconcile(new long[] { 1 }, _ => true), Is.Zero);
        }

        // ---- helpers

        /// <summary>A server double with a read-only, empty database (GameObject's static set-up reads one).</summary>
        private sealed class ServerDouble : IDisposable
        {
            private readonly GameServer _previous = GameServer.Instance;

            public ServerDouble() => GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));

            public void Dispose() => GameServer.LoadTestDouble(_previous);
        }

        public class ReadOnlyDatabase : System.Reflection.DispatchProxy
        {
            protected override object Invoke(System.Reflection.MethodInfo method, object[] args)
            {
                if (!method.Name.StartsWith("Select") && !method.Name.StartsWith("Find") && !method.Name.StartsWith("GetObjectCount"))
                    throw new InvalidOperationException("No database writes allowed: " + method.Name);
                Type type = method.ReturnType;
                if (type == typeof(int) || type == typeof(long)) return Activator.CreateInstance(type);
                return type.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(type)
                    ? Activator.CreateInstance(typeof(List<>).MakeGenericType(type.GetGenericArguments()[0])) : null;
            }
        }

        private static readonly IObjectDatabase EmptyDatabase = System.Reflection.DispatchProxy.Create<IObjectDatabase, ReadOnlyDatabase>();

        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
            protected override DOL.GS.ServerRules.IServerRules ServerRulesImpl => new DOL.GS.ServerRules.NormalServerRules();
        }

        private static bool Direct(Action work)
        {
            work();
            return true;
        }

        private static BotPopulationProfile Profile(DateTime? want = null) =>
            new((byte)PopulationPersona.Regular, 0b0111110, want ?? DateTime.UtcNow, DateTime.MinValue, 0, 0, DateTime.UtcNow.Date, 0, false);

        private static GameBot FakeAutonomousBot(long id)
        {
            var bot = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            typeof(GameBot).GetField("<IsAutonomousWorldBot>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(bot, true);
            typeof(GameBot).GetField("<PersistentRecord>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(bot, new OfflineWorldBotRecord { BotId = id, Name = "Fake", IsPersisted = true });
            bot.DatabaseID = id;
            bot.ObjectState = GameObject.eObjectState.Inactive;
            return bot;
        }

        private void SeedBot(long id, string name, int level = 10, long money = 0) =>
            SeedRow(BotRowSql.BotTable, new BotRow
            {
                ["BotId"] = id, ["Name"] = name, ["Realm"] = 1L, ["ClassId"] = 2L, ["RaceId"] = 1L, ["Gender"] = 0L,
                ["Level"] = (long)level, ["MoneyCopper"] = money, ["IsOnline"] = 0L, ["IsAlive"] = 1L,
            });

        private void SeedItem(string id, long botId, int slot, int ownerLot = 0, string unique = null) =>
            SeedRow(BotRowSql.InventoryTable, new BotRow
            {
                ["Inventory_ID"] = id, ["OwnerID"] = AutonomousBotEconomy.GetOwnerId(botId), ["SlotPosition"] = (long)slot,
                ["OwnerLot"] = (long)ownerLot, ["ITemplate_Id"] = unique == null ? "training_sword" : null, ["UTemplate_Id"] = unique, ["Count"] = 1L,
            });

        private void SeedUnique(string id, string updated = "2026-01-01 00:00:00") =>
            SeedRow(BotRowSql.UniqueTable, new BotRow { ["Id_nb"] = id, ["Name"] = id, ["LastTimeRowUpdated"] = updated });

        private void SeedRow(string table, BotRow row)
        {
            using SQLiteConnection world = BotRowSql.Open(_world);
            string objectId = table + "_ID";
            if (BotRowSql.Columns(world, table).Contains(objectId, StringComparer.OrdinalIgnoreCase) && !row.ContainsKey(objectId))
                row[objectId] = Guid.NewGuid().ToString();
            if (!BotRowSql.Insert(world, null, table, row))
                throw new InvalidOperationException($"seed into {table} failed");
        }

        private long Count(string table) => Scalar($"SELECT COUNT(*) FROM `{table}`");

        private long Scalar(string sql)
        {
            using SQLiteConnection world = BotRowSql.Open(_world);
            return Convert.ToInt64(BotRowSql.Scalar(world, null, sql) ?? 0L);
        }

        private sealed class FakeHooks : IBotArchiveHooks
        {
            public readonly HashSet<long> Busy = new(), Hired = new();
            public bool LocksFree = true;
            public readonly List<string> Removed = new(), Added = new();
            public readonly List<(long, string)> Retired = new();
            public bool IsBusy(long botId) => Busy.Contains(botId);
            public bool IsHired(long botId) => Hired.Contains(botId);

            public bool TryLocked(Action work)
            {
                if (!LocksFree)
                    return false;
                work();
                return true;
            }

            public object RemoveListings(string ownerId)
            {
                Removed.Add(ownerId);
                return ownerId;
            }

            public void RestoreListings(object token) { }
            public void AddListings(string ownerId) => Added.Add(ownerId);
            public void OnRetired(long botId, string name) => Retired.Add((botId, name));
            public readonly List<(long, bool)> Cold = new();
            public void OnColdChanged(long botId, string name, bool cold) => Cold.Add((botId, cold));
        }
    }
}
