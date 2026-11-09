using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DOL.Database;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>BotNameService.TryClaimNewBotName's shape (tests pass their own).</summary>
    public delegate bool BotNameClaim(eRealm realm, eRace race, eGender gender, long botId, out string name);

    /// <summary>Server-side creation of a new level-1 bot (the launcher's "+ Lv.1" moved into the server).</summary>
    public static class BotCreation
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(BotCreation));
        public const string PlaceholderPrefix = "~new";

        public static long CreateLevelOne(string worldConnectionString, BotArchive archive, eRealm realm, eCharacterClass characterClass,
            eRace race, eGender gender, BotNameClaim claimName, BotPopulationProfile profile, Func<Action, bool> tryLocked = null)
        {
            tryLocked ??= BotPopulationMaintenance.TryWriteLocked;
            string now = DateTime.UtcNow.ToString("O");
            string className = SafeClassName(characterClass);
            long botId = 0;
            using SQLiteConnection world = BotRowSql.Open(worldConnectionString);
            // A placeholder name first: the name claim needs the new BotId. RegionId 0 and no save time make the first
            // login place it at a validated start point and (True 1.65 Mode) turn it into its base class.
            var row = new BotRow
            {
                ["Name"] = PlaceholderPrefix + Guid.NewGuid().ToString("N")[..16],
                ["Realm"] = (long)realm, ["ClassId"] = (long)characterClass, ["ClassName"] = className,
                ["RaceId"] = (long)race, ["RaceName"] = race.ToString(), ["Gender"] = (long)gender,
                ["Level"] = 1L, ["Experience"] = 0L, ["RealmPoints"] = 0L, ["ZoneName"] = "Classic starting area",
                ["Activity"] = "Queued at a new start", ["IsOnline"] = 0L, ["IsAlive"] = 1L, ["LastUpdateUtc"] = now,
                ["MoneyCopper"] = 0L, ["InventoryRevision"] = 0L, ["RegionId"] = 0L, ["Health"] = 1L,
                ["CurrentGoal"] = "Awaiting login", ["ObjectiveProgress"] = "Created by the server", ["LastMeaningfulProgressUtc"] = now,
                ["IsRetired"] = 0L, ["LastSavedUtc"] = string.Empty,
            };
            if (!Retry(tryLocked, () =>
                {
                    BotRowSql.Insert(world, null, BotRowSql.BotTable, row);
                    botId = (long)(BotRowSql.Scalar(world, null, "SELECT last_insert_rowid()") ?? 0L);
                }) || botId <= 0)
                return 0;

            string name = null;
            bool claimed;
            try
            {
                claimed = claimName(realm, race, gender, botId, out name) && !string.IsNullOrWhiteSpace(name);
            }
            catch (Exception exception)
            {
                PopulationLog.Warn(Log, $"BOT_CREATE name claim failed: {exception.Message}");
                claimed = false;
            }

            bool named = claimed && Retry(tryLocked, () =>
                BotRowSql.Execute(world, null, $"UPDATE `{BotRowSql.BotTable}` SET Name=@n WHERE BotId=@id", ("@n", name), ("@id", botId)));
            if (!named)
            {
                Retry(tryLocked, () => BotRowSql.Execute(world, null, $"DELETE FROM `{BotRowSql.BotTable}` WHERE BotId=@id", ("@id", botId)));
                if (claimed)
                {
                    try { BotNameService.ReleaseBotName(name); } catch { }
                }
                PopulationLog.Warn(Log, $"BOT_CREATE_FAILED realm={GlobalConstants.RealmToName(realm)} class={characterClass}: no name could be given");
                return 0;
            }

            row["BotId"] = botId;
            row["Name"] = name;
            archive.UpsertWarm(row, profile, online: false);
            PopulationLog.Info(Log, $"BOT_CREATED id={botId} \"{name}\" {GlobalConstants.RealmToName(realm)} {race} {className} level 1");
            return botId;
        }

        private static string SafeClassName(eCharacterClass characterClass)
        {
            try
            {
                return ScriptMgr.FindCharacterClass((int)characterClass)?.Name ?? characterClass.ToString();
            }
            catch
            {
                return characterClass.ToString();
            }
        }

        private static bool Retry(Func<Action, bool> tryLocked, Action work)
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                if (tryLocked(work))
                    return true;
                Thread.Sleep(10 + attempt);
            }
            return false;
        }
    }

    /// <summary>
    /// Server-start housekeeping for the population system, on a background thread: world indexes, archive crash
    /// recovery, the one-time migration of the existing bots, and the orphan clean-up. The lifecycle is ready (and the
    /// scheduler may act) only after it finishes.
    /// </summary>
    public static class BotPopulationMaintenance
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(BotPopulationMaintenance));
        public const string MigrationKey = "migration_v1";

        public static readonly (string Name, string Sql)[] WorldIndexes =
        [
            // Name is COLLATE NOCASE in the table, so this index is unique without regard to case.
            (SqliteIndexPrefix + "offline_world_bots_name", "CREATE UNIQUE INDEX IF NOT EXISTS `{0}` ON `offline_world_bots` (`Name`)"),
            (SqliteIndexPrefix + "offline_world_bots_realm_level", "CREATE INDEX IF NOT EXISTS `{0}` ON `offline_world_bots` (`Realm`, `Level`)"),
            (SqliteIndexPrefix + "offline_world_bots_online", "CREATE INDEX IF NOT EXISTS `{0}` ON `offline_world_bots` (`IsOnline`)"),
            (SqliteIndexPrefix + "inventory_exchange_owner_lot", "CREATE INDEX IF NOT EXISTS `{0}` ON `Inventory` (`OwnerID`, `OwnerLot`) WHERE `OwnerLot` <> 0"),
            (SqliteIndexPrefix + "offline_bot_commands_state", "CREATE INDEX IF NOT EXISTS `{0}` ON `offline_bot_commands` (`State`)"),
        ];

        private const string SqliteIndexPrefix = DOL.Database.Handlers.SqliteObjectDatabase.CustomIndexPrefix;

        internal static void StartInBackground(IReadOnlyCollection<long> onlineAtLastStop)
        {
            BotArchiveStore.MarkReady(false);
            Task.Run(() =>
            {
                try
                {
                    BotArchive archive = BotArchiveStore.Current;
                    if (archive == null)
                        return;
                    // the hired companions first: none may be sorted as a gamebot (if this fails, nothing is touched)
                    HiredCompanions165.EnsureLoaded();
                    Run(archive, new HashSet<long>(onlineAtLastStop ?? Array.Empty<long>()), HiredCompanions165.ForgetOrphan);
                    BotArchiveStore.MarkReady(true);
                    PopulationLog.Info(Log, $"BOT_POPULATION ready: saved={archive.SavedCount}");
                }
                catch (Exception exception)
                {
                    PopulationLog.Error(Log, "BOT_POPULATION start-up housekeeping failed; the population system stays off this run", exception);
                }
            });
        }

        /// <summary>Everything the start needs, in order. Public for tests (temp databases).</summary>
        public static MigrationReport Run(BotArchive archive, ISet<long> onlineAtLastStop, Action<long> forgetHired,
            Func<Action, bool> tryLocked = null, Random random = null)
        {
            tryLocked ??= TryWriteLocked;
            random ??= Random.Shared;
            var report = new MigrationReport();
            EnsureWorldIndexes(archive.WorldConnectionString, tryLocked, report);
            archive.ClearOnlineFlags();
            report.Recovered = archive.RecoverInterrupted();
            report.PlaceholdersRemoved = DeletePlaceholders(archive.WorldConnectionString, tryLocked);
            Migrate(archive, onlineAtLastStop, forgetHired, tryLocked, random, report);
            CleanupOrphans(archive.WorldConnectionString, tryLocked, DateTime.UtcNow.AddHours(-1), report);
            archive.FlushQueued(int.MaxValue);
            PopulationLog.Info(Log, $"BOT_POPULATION start-up: {report}");
            return report;
        }

        public sealed class MigrationReport
        {
            public bool Migrated;
            public int ProfilesAdded, DirectoryRemoved, OrphanCompanionsRemoved, Recovered, PlaceholdersRemoved;
            public int OrphanUniques, OrphanRelists, OrphanListingHours;
            public List<string> IndexFailures = new();

            public override string ToString() =>
                $"migrated={Migrated} added={ProfilesAdded} removedDirectory={DirectoryRemoved} orphanCompanions={OrphanCompanionsRemoved} " +
                $"recovered={Recovered} placeholders={PlaceholdersRemoved} orphanUniques={OrphanUniques} orphanRelists={OrphanRelists} " +
                $"orphanListingHours={OrphanListingHours} indexFailures={IndexFailures.Count}";
        }

        /// <summary>Try-lock on the shared database write lock (never waits; callers retry).</summary>
        public static bool TryWriteLocked(Action work)
        {
            bool taken = false;
            try
            {
                Monitor.TryEnter(AutonomousBotStatusPersistence.DatabaseWriteLock, 0, ref taken);
                if (!taken)
                    return false;
                work();
                return true;
            }
            finally
            {
                if (taken)
                    Monitor.Exit(AutonomousBotStatusPersistence.DatabaseWriteLock);
            }
        }

        private static bool Locked(Func<Action, bool> tryLocked, Action work)
        {
            for (int attempt = 0; attempt < 500; attempt++)
            {
                if (tryLocked(work))
                    return true;
                Thread.Sleep(20);
            }
            return false;
        }

        public static void EnsureWorldIndexes(string worldConnectionString, Func<Action, bool> tryLocked, MigrationReport report)
        {
            using SQLiteConnection world = BotRowSql.Open(worldConnectionString);
            foreach ((string name, string sql) in WorldIndexes)
            {
                string table = sql.Split('`')[3];
                if (!BotRowSql.HasTable(world, table))
                    continue;
                try
                {
                    Locked(tryLocked, () => BotRowSql.Execute(world, null, string.Format(sql, name)));
                }
                catch (Exception exception)
                {
                    // A unique index fails on duplicate names: keep a plain one so lookups stay fast.
                    report?.IndexFailures.Add($"{name}: {exception.Message}");
                    PopulationLog.Warn(Log, $"BOT_INDEX {name} could not be created: {exception.Message}");
                    if (sql.Contains("UNIQUE"))
                        Locked(tryLocked, () => BotRowSql.Execute(world, null,
                            string.Format(sql.Replace("UNIQUE ", string.Empty), name + "_plain")));
                }
            }
        }

        /// <summary>Rows a crash left half-made by <see cref="BotCreation"/> (never named, never logged in).</summary>
        private static int DeletePlaceholders(string worldConnectionString, Func<Action, bool> tryLocked)
        {
            int removed = 0;
            using SQLiteConnection world = BotRowSql.Open(worldConnectionString);
            Locked(tryLocked, () => removed = BotRowSql.Execute(world, null,
                $"DELETE FROM `{BotRowSql.BotTable}` WHERE Name LIKE '{BotCreation.PlaceholderPrefix}%' AND (LastSavedUtc IS NULL OR LastSavedUtc='')"));
            return removed;
        }

        /// <summary>
        /// One-time migration (owner 2026-11-08): every existing gamebot stays an Active warm bot and gets a persona;
        /// hired-companion rows whose owner character no longer exists are deleted. Later starts only add directory rows
        /// for warm bots that lack one (newcomer-style personas) and drop rows of bots deleted while the server was off.
        /// </summary>
        public static void Migrate(BotArchive archive, ISet<long> onlineAtLastStop, Action<long> forgetHired,
            Func<Action, bool> tryLocked, Random random, MigrationReport report)
        {
            bool firstRun = archive.GetMeta(MigrationKey) == null;
            using SQLiteConnection world = BotRowSql.Open(archive.WorldConnectionString);

            if (BotRowSql.HasTable(world, BotRowSql.HiredTable) && BotRowSql.HasTable(world, "DOLCharacters"))
            {
                List<long> orphans = BotRowSql.Select(world, null,
                        $"SELECT h.BotId FROM `{BotRowSql.HiredTable}` h WHERE NOT EXISTS (SELECT 1 FROM DOLCharacters d WHERE d.DOLCharacters_ID=h.OwnerCharacterID)")
                    .Select(row => row.Long("BotId")).ToList();
                foreach (long botId in orphans)
                {
                    Locked(tryLocked, () => BotRowSql.Execute(world, null, $"DELETE FROM `{BotRowSql.HiredTable}` WHERE BotId=@id", ("@id", botId)));
                    forgetHired?.Invoke(botId);
                    if (archive.RetireOne(botId, allowHired: true) == BotArchive.RetireResult.Retired)
                        report.OrphanCompanionsRemoved++;
                    PopulationLog.Info(Log, $"BOT_MIGRATION removed hired companion {botId}: its owner character no longer exists");
                }
            }

            var guildmates = BotRowSql.HasTable(world, BotRowSql.GuildTable)
                ? new HashSet<long>(BotRowSql.Select(world, null, $"SELECT BotId FROM `{BotRowSql.GuildTable}`").Select(row => row.Long("BotId")))
                : new HashSet<long>();
            DateTime now = DateTime.UtcNow;
            (int added, int removed) = archive.ReconcileWarm(row =>
            {
                long botId = row.Long("BotId");
                bool wasOnline = onlineAtLastStop?.Contains(botId) == true;
                return firstRun
                    ? ExistingBotProfile(guildmates.Contains(botId), wasOnline, now, random)
                    : NewcomerProfile(now, random) with
                    {
                        WasOnline = wasOnline,
                        NextWantUtc = wasOnline ? PopulationPersonas.OnlineMarkerUtc : now,
                    };
            });
            report.ProfilesAdded = added;
            report.DirectoryRemoved = removed;
            if (firstRun)
            {
                archive.SetMeta(MigrationKey, $"{now:O} added={added} orphanCompanions={report.OrphanCompanionsRemoved}");
                report.Migrated = true;
            }
        }

        /// <summary>
        /// An existing bot (the 2026-11-08 roster): established players, no trial newcomers; guildmates play often. A bot
        /// online at the last stop sorts first (the scheduler's online marker), like a player reconnecting.
        /// </summary>
        public static BotPopulationProfile ExistingBotProfile(bool guildmate, bool wasOnline, DateTime utcNow, Random random)
        {
            double roll = random.NextDouble();
            PopulationPersona persona = guildmate
                ? roll < 0.40 ? PopulationPersona.Veteran : PopulationPersona.Regular
                : roll < 0.20 ? PopulationPersona.Veteran : roll < 0.60 ? PopulationPersona.Regular : PopulationPersona.Casual;
            DateTime want = wasOnline ? PopulationPersonas.OnlineMarkerUtc : utcNow;
            return new BotPopulationProfile((byte)persona, PopulationPersonas.RandomPlayDays(persona, random), want, utcNow, 0, 0,
                utcNow.Date, 0, wasOnline);
        }

        /// <summary>A brand-new bot: persona by the plan's shares (the scheduler's NewProfile).</summary>
        public static BotPopulationProfile NewcomerProfile(DateTime utcNow, Random random = null) =>
            PopulationPersonas.NewProfile(random ?? Random.Shared, utcNow);

        /// <summary>
        /// Removes unique-item rows no item, house item or wall item uses (older than the cut-off, so one being created
        /// right now is safe), and relist / listing-hours rows whose item is gone. Batches of 500 under the write try-lock.
        /// </summary>
        public static void CleanupOrphans(string worldConnectionString, Func<Action, bool> tryLocked, DateTime olderThanUtc, MigrationReport report)
        {
            using SQLiteConnection world = BotRowSql.Open(worldConnectionString);
            if (BotRowSql.HasTable(world, BotRowSql.UniqueTable) && BotRowSql.HasTable(world, BotRowSql.InventoryTable))
            {
                string houses = string.Empty;
                if (BotRowSql.HasTable(world, "DBIndoorItem"))
                    houses += " AND NOT EXISTS (SELECT 1 FROM DBIndoorItem h WHERE h.BaseItemID=u.Id_nb)";
                if (BotRowSql.HasTable(world, "DBOutdoorItem"))
                    houses += " AND NOT EXISTS (SELECT 1 FROM DBOutdoorItem o WHERE o.BaseItemID=u.Id_nb)";
                string cutoff = olderThanUtc.ToString("yyyy-MM-dd HH:mm:ss");
                report.OrphanUniques = DeleteInBatches(world, tryLocked,
                    $"DELETE FROM `{BotRowSql.UniqueTable}` WHERE Id_nb IN (SELECT u.Id_nb FROM `{BotRowSql.UniqueTable}` u " +
                    $"WHERE NOT EXISTS (SELECT 1 FROM `{BotRowSql.InventoryTable}` i WHERE i.UTemplate_Id=u.Id_nb){houses} " +
                    $"AND (u.LastTimeRowUpdated IS NULL OR u.LastTimeRowUpdated < '{cutoff}') LIMIT 500)");
            }
            if (BotRowSql.HasTable(world, BotRowSql.RelistTable))
                report.OrphanRelists = DeleteInBatches(world, tryLocked,
                    $"DELETE FROM `{BotRowSql.RelistTable}` WHERE InventoryId IN (SELECT r.InventoryId FROM `{BotRowSql.RelistTable}` r " +
                    $"WHERE NOT EXISTS (SELECT 1 FROM `{BotRowSql.InventoryTable}` i WHERE i.Inventory_ID=r.InventoryId) LIMIT 500)");
            if (BotRowSql.HasTable(world, BotRowSql.ListingHoursTable))
                report.OrphanListingHours = DeleteInBatches(world, tryLocked,
                    $"DELETE FROM `{BotRowSql.ListingHoursTable}` WHERE InventoryId IN (SELECT h.InventoryId FROM `{BotRowSql.ListingHoursTable}` h " +
                    $"WHERE NOT EXISTS (SELECT 1 FROM `{BotRowSql.InventoryTable}` i WHERE i.Inventory_ID=h.InventoryId) LIMIT 500)");
        }

        private static int DeleteInBatches(SQLiteConnection world, Func<Action, bool> tryLocked, string sql)
        {
            int total = 0;
            for (int batch = 0; batch < 10_000; batch++)
            {
                int deleted = 0;
                if (!Locked(tryLocked, () => deleted = BotRowSql.Execute(world, null, sql)) || deleted == 0)
                    break;
                total += deleted;
                Thread.Sleep(5);   // let game-loop writers in between batches
            }
            return total;
        }
    }
}
