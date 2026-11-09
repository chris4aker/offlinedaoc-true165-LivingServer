using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>A saved, offline bot the scheduler may log in (or the retirement may pick).</summary>
    public sealed record ArchiveCandidate(long BotId, eRealm Realm, int Level, int ClassId, DateTime LastSeenUtc, BotPopulationProfile Profile);

    /// <summary>The scheduler's per-bot play data. Persona: <see cref="PopulationPersona"/>. Status: 0 active, 1 lapsed (quit for now).</summary>
    public sealed record BotPopulationProfile(byte Persona, byte PlayDaysMask, DateTime NextWantUtc, DateTime ReturnUtc,
        int PlayedMinutes, int TodayMinutes, DateTime TodayDate, byte Status, bool WasOnline);

    /// <summary>Where a saved bot's data lives (directory column State).</summary>
    public static class BotArchiveState
    {
        public const int Warm = 0;       // full rows in the world database
        public const int Cold = 1;       // compressed blob in the archive file
        public const int Retiring = 2;   // being deleted for good (finished at the next start after a crash)
        public const int Archiving = 3;  // blob written, world rows still to delete
        public const int Restoring = 4;  // world rows being written back from the blob
    }

    /// <summary>The world-side pieces the archive needs (live bots, locks, Exchange cache). Tests use fakes.</summary>
    public interface IBotArchiveHooks
    {
        /// <summary>Online, logging in or out, or a logout save still waiting.</summary>
        bool IsBusy(long botId);

        bool IsHired(long botId);

        /// <summary>Runs work under the Exchange, economy and database-write locks, only if all are free right now.</summary>
        bool TryLocked(Action work);

        /// <summary>Takes the bot's listings off the Exchange cache; the token puts them back.</summary>
        object RemoveListings(string ownerId);

        void RestoreListings(object token);

        /// <summary>Offers a restored bot's listings again.</summary>
        void AddListings(string ownerId);

        /// <summary>A bot was deleted for good: guild list, name cool-down, caches.</summary>
        void OnRetired(long botId, string name);

        /// <summary>A bot moved into (true) or out of (false) the cold archive (its name's state follows).</summary>
        void OnColdChanged(long botId, string name, bool cold);
    }

    /// <summary>
    /// Saved bots: a directory row for every saved gamebot (warm or cold) in runtime\data\bot-archive.sqlite3.db, with
    /// realm / level / online / want-time columns, the scheduler's profile, a counts table kept by triggers, and for cold
    /// bots a compressed blob of everything they own. Hired companions are never in it. All methods are thread-safe;
    /// database methods belong on background threads, except the cached counts.
    /// </summary>
    public sealed class BotArchive : IDisposable
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(BotArchive));
        public static readonly TimeSpan WarmIdleAge = TimeSpan.FromHours(36);

        private readonly string _worldConnectionString;
        private readonly IBotArchiveHooks _hooks;
        private readonly object _gate = new();
        private readonly SQLiteConnection _archive;
        private readonly ConcurrentDictionary<long, PendingUpdate> _pending = new();
        // [realm 0-3, level 0-50, online 0-1]
        private volatile long[,,] _counts = new long[4, 51, 2];
        private volatile bool _countsDirty = true;

        private sealed class PendingUpdate
        {
            public BotPopulationProfile Profile;
            public bool? Online;
            public int? Level;
            public DateTime? LastSeenUtc;
        }

        public BotArchive(string worldConnectionString, string archivePath, IBotArchiveHooks hooks)
        {
            _worldConnectionString = worldConnectionString;
            _hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
            ArchivePath = Path.GetFullPath(archivePath);
            Directory.CreateDirectory(Path.GetDirectoryName(ArchivePath)!);
            _archive = new SQLiteConnection($"Data Source={ArchivePath};Version=3;Pooling=False;");
            _archive.Open();
            CreateSchema();
            RefreshCounts();
        }

        public string ArchivePath { get; }

        public string WorldConnectionString => _worldConnectionString;

        public void Dispose()
        {
            lock (_gate)
                _archive.Dispose();
        }

        private void CreateSchema()
        {
            lock (_gate)
            {
                Exec("PRAGMA journal_mode=WAL;");
                Exec("PRAGMA synchronous=NORMAL;");
                Exec("PRAGMA busy_timeout=5000;");
                Exec("CREATE TABLE IF NOT EXISTS meta (Key TEXT PRIMARY KEY, Value TEXT NOT NULL)");
                Exec("""
                    CREATE TABLE IF NOT EXISTS bots (
                        BotId INTEGER PRIMARY KEY,
                        Name TEXT NOT NULL COLLATE NOCASE,
                        Realm INTEGER NOT NULL, Level INTEGER NOT NULL, ClassId INTEGER NOT NULL,
                        RaceId INTEGER NOT NULL DEFAULT 0, Gender INTEGER NOT NULL DEFAULT 0,
                        State INTEGER NOT NULL, Online INTEGER NOT NULL DEFAULT 0, LastSeenUtc INTEGER NOT NULL,
                        Persona INTEGER NOT NULL DEFAULT 0, PlayDaysMask INTEGER NOT NULL DEFAULT 0,
                        NextWantUtc INTEGER NOT NULL DEFAULT 0, ReturnUtc INTEGER NOT NULL DEFAULT 0,
                        PlayedMinutes INTEGER NOT NULL DEFAULT 0, TodayMinutes INTEGER NOT NULL DEFAULT 0,
                        TodayDate INTEGER NOT NULL DEFAULT 0, Status INTEGER NOT NULL DEFAULT 0, WasOnline INTEGER NOT NULL DEFAULT 0,
                        BlobVersion INTEGER NOT NULL DEFAULT 0, Blob BLOB)
                    """);
                Exec("CREATE INDEX IF NOT EXISTS bots_band ON bots (Realm, Level, NextWantUtc) WHERE State IN (0,1) AND Online=0");
                Exec("CREATE INDEX IF NOT EXISTS bots_idle ON bots (LastSeenUtc) WHERE State=0 AND Online=0");
                Exec("CREATE INDEX IF NOT EXISTS bots_busy_state ON bots (State) WHERE State>=2");
                Exec("CREATE INDEX IF NOT EXISTS bots_name ON bots (Name)");
                Exec("CREATE TABLE IF NOT EXISTS counts (Realm INTEGER NOT NULL, Level INTEGER NOT NULL, Online INTEGER NOT NULL, N INTEGER NOT NULL, PRIMARY KEY (Realm, Level, Online))");
                Exec("""
                    CREATE TRIGGER IF NOT EXISTS bots_counts_insert AFTER INSERT ON bots WHEN NEW.State<>2 BEGIN
                        INSERT INTO counts (Realm, Level, Online, N) VALUES (NEW.Realm, NEW.Level, NEW.Online, 1)
                        ON CONFLICT (Realm, Level, Online) DO UPDATE SET N=N+1;
                    END
                    """);
                Exec("""
                    CREATE TRIGGER IF NOT EXISTS bots_counts_delete AFTER DELETE ON bots WHEN OLD.State<>2 BEGIN
                        UPDATE counts SET N=N-1 WHERE Realm=OLD.Realm AND Level=OLD.Level AND Online=OLD.Online;
                    END
                    """);
                Exec("""
                    CREATE TRIGGER IF NOT EXISTS bots_counts_update AFTER UPDATE OF Realm, Level, Online, State ON bots BEGIN
                        UPDATE counts SET N=N-1 WHERE OLD.State<>2 AND Realm=OLD.Realm AND Level=OLD.Level AND Online=OLD.Online;
                        INSERT INTO counts (Realm, Level, Online, N) SELECT NEW.Realm, NEW.Level, NEW.Online, 1 WHERE NEW.State<>2
                        ON CONFLICT (Realm, Level, Online) DO UPDATE SET N=N+1;
                    END
                    """);
            }
        }

        private void Exec(string sql, params (string, object)[] parameters) =>
            BotRowSql.Execute(_archive, null, sql, parameters);

        // ---- meta

        public string GetMeta(string key)
        {
            lock (_gate)
                return BotRowSql.Scalar(_archive, null, "SELECT Value FROM meta WHERE Key=@k", ("@k", key)) as string;
        }

        public void SetMeta(string key, string value)
        {
            lock (_gate)
                Exec("INSERT INTO meta (Key, Value) VALUES (@k, @v) ON CONFLICT (Key) DO UPDATE SET Value=excluded.Value", ("@k", key), ("@v", value));
        }

        // ---- counts (cached, safe on any thread)

        public long SavedCount
        {
            get
            {
                long[,,] counts = CurrentCounts();
                long total = 0;
                foreach (long n in counts)
                    total += n;
                return total;
            }
        }

        /// <summary>Saved bots of the band that are not online.</summary>
        public long Count(eRealm realm, int minLevel, int maxLevel) => CountWhere(realm, minLevel, maxLevel, online: 0);

        /// <summary>Saved bots of the band marked online in the directory.</summary>
        public long CountOnline(eRealm realm, int minLevel, int maxLevel) => CountWhere(realm, minLevel, maxLevel, online: 1);

        private long CountWhere(eRealm realm, int minLevel, int maxLevel, int online)
        {
            int r = (int)realm;
            if (r < 0 || r > 3)
                return 0;
            long[,,] counts = CurrentCounts();
            long total = 0;
            for (int level = Math.Max(0, minLevel); level <= Math.Min(50, maxLevel); level++)
                total += counts[r, level, online];
            return total;
        }

        /// <summary>The cached counts, re-read (at most about 300 rows) after a write changed them.</summary>
        private long[,,] CurrentCounts()
        {
            if (_countsDirty)
            {
                try
                {
                    RefreshCounts();
                }
                catch (Exception exception)
                {
                    PopulationLog.Warn(Log, $"BOT_ARCHIVE counts refresh failed: {exception.Message}");
                }
            }
            return _counts;
        }

        public void RefreshCounts()
        {
            _countsDirty = false;   // before the read: a write landing during it marks the counts dirty again
            var counts = new long[4, 51, 2];
            lock (_gate)
            {
                foreach (BotRow row in BotRowSql.Select(_archive, null, "SELECT Realm, Level, Online, N FROM counts WHERE N<>0"))
                {
                    long realm = row.Long("Realm"), level = row.Long("Level"), online = row.Long("Online");
                    if (realm is >= 0 and <= 3 && level is >= 0 and <= 50 && online is 0 or 1)
                        counts[realm, level, online] = row.Long("N");
                }
            }
            _counts = counts;
        }

        // ---- directory rows

        /// <summary>The bot's state (<see cref="BotArchiveState"/>), or -1 when it has no directory row.</summary>
        public int StateOf(long botId)
        {
            lock (_gate)
                return BotRowSql.Scalar(_archive, null, "SELECT State FROM bots WHERE BotId=@id", ("@id", botId)) is long state ? (int)state : -1;
        }

        /// <summary>Saved (warm or cold, not being retired).</summary>
        public bool IsSaved(long botId) => StateOf(botId) is BotArchiveState.Warm or BotArchiveState.Cold or BotArchiveState.Archiving or BotArchiveState.Restoring;

        public bool IsCold(long botId) => StateOf(botId) is BotArchiveState.Cold or BotArchiveState.Archiving;

        /// <summary>Adds (or refreshes) the directory row of a warm bot from its world row.</summary>
        public void UpsertWarm(BotRow record, BotPopulationProfile profile, bool online)
        {
            long botId = record.Long("BotId");
            if (botId <= 0)
                return;
            profile ??= new BotPopulationProfile(0, 0, DateTime.UtcNow, DateTime.MinValue, 0, 0, DateTime.UtcNow.Date, 0, online);
            lock (_gate)
            {
                Exec("""
                    INSERT INTO bots (BotId, Name, Realm, Level, ClassId, RaceId, Gender, State, Online, LastSeenUtc,
                        Persona, PlayDaysMask, NextWantUtc, ReturnUtc, PlayedMinutes, TodayMinutes, TodayDate, Status, WasOnline)
                    VALUES (@id, @name, @realm, @level, @class, @race, @gender, 0, @online, @seen,
                        @persona, @days, @want, @return, @played, @today, @todayDate, @status, @was)
                    ON CONFLICT (BotId) DO UPDATE SET Name=excluded.Name, Realm=excluded.Realm, Level=excluded.Level,
                        ClassId=excluded.ClassId, RaceId=excluded.RaceId, Gender=excluded.Gender, Online=excluded.Online
                    """,
                    ("@id", botId), ("@name", record.Text("Name") ?? string.Empty), ("@realm", record.Long("Realm")),
                    ("@level", Math.Clamp(record.Long("Level"), 1, 50)), ("@class", record.Long("ClassId")), ("@race", record.Long("RaceId")),
                    ("@gender", record.Long("Gender")), ("@online", online ? 1 : 0), ("@seen", DateTime.UtcNow.Ticks),
                    ("@persona", (long)profile.Persona), ("@days", (long)profile.PlayDaysMask), ("@want", profile.NextWantUtc.Ticks),
                    ("@return", profile.ReturnUtc.Ticks), ("@played", (long)profile.PlayedMinutes), ("@today", (long)profile.TodayMinutes),
                    ("@todayDate", profile.TodayDate.Ticks), ("@status", (long)profile.Status), ("@was", profile.WasOnline ? 1 : 0));
            }
            _countsDirty = true;
        }

        public void DeleteDirectoryRow(long botId)
        {
            lock (_gate)
                Exec("DELETE FROM bots WHERE BotId=@id", ("@id", botId));
            _countsDirty = true;
        }

        public string NameOf(long botId)
        {
            lock (_gate)
                return BotRowSql.Scalar(_archive, null, "SELECT Name FROM bots WHERE BotId=@id", ("@id", botId)) as string;
        }

        public IReadOnlyList<long> IdsInState(int state, int limit = int.MaxValue)
        {
            lock (_gate)
                return BotRowSql.Select(_archive, null, "SELECT BotId FROM bots WHERE State=@s ORDER BY BotId LIMIT @n", ("@s", state), ("@n", limit))
                    .Select(row => row.Long("BotId")).ToArray();
        }

        /// <summary>Every saved bot is offline (server start: no bot is in the world yet).</summary>
        public void ClearOnlineFlags()
        {
            lock (_gate)
                Exec("UPDATE bots SET Online=0 WHERE Online<>0");
            _countsDirty = true;
        }

        // ---- queued updates (profiles, online flags, levels): batched on a background flush

        public void QueueProfile(long botId, BotPopulationProfile profile)
        {
            if (botId > 0 && profile != null)
                _pending.AddOrUpdate(botId, _ => new PendingUpdate { Profile = profile }, (_, p) => { p.Profile = profile; return p; });
        }

        public void QueueOnline(long botId, bool online, int level, DateTime utcNow)
        {
            if (botId <= 0)
                return;
            _pending.AddOrUpdate(botId,
                _ => new PendingUpdate { Online = online, Level = level, LastSeenUtc = utcNow },
                (_, p) => { p.Online = online; p.Level = level; p.LastSeenUtc = utcNow; return p; });
        }

        public void QueueLevel(long botId, int level)
        {
            if (botId > 0)
                _pending.AddOrUpdate(botId, _ => new PendingUpdate { Level = level }, (_, p) => { p.Level = level; return p; });
        }

        /// <summary>Writes the queued updates in one transaction (rows without a directory row are skipped).</summary>
        public int FlushQueued(int maximum = 512)
        {
            var batch = new List<(long BotId, PendingUpdate Update)>();
            foreach (long botId in _pending.Keys.Take(maximum).ToArray())
                if (_pending.TryRemove(botId, out PendingUpdate update))
                    batch.Add((botId, update));
            if (batch.Count > 0)
            {
                lock (_gate)
                {
                    using SQLiteTransaction transaction = _archive.BeginTransaction();
                    foreach ((long botId, PendingUpdate update) in batch)
                    {
                        if (update.Level is int level)
                            BotRowSql.Execute(_archive, transaction, "UPDATE bots SET Level=@l WHERE BotId=@id AND Level<>@l", ("@l", Math.Clamp(level, 1, 50)), ("@id", botId));
                        if (update.Online is bool online)
                            BotRowSql.Execute(_archive, transaction, "UPDATE bots SET Online=@o WHERE BotId=@id AND Online<>@o", ("@o", online ? 1 : 0), ("@id", botId));
                        if (update.LastSeenUtc is DateTime seen)
                            BotRowSql.Execute(_archive, transaction, "UPDATE bots SET LastSeenUtc=@t WHERE BotId=@id", ("@t", seen.Ticks), ("@id", botId));
                        if (update.Profile is { } p)
                            BotRowSql.Execute(_archive, transaction, """
                                UPDATE bots SET Persona=@persona, PlayDaysMask=@days, NextWantUtc=@want, ReturnUtc=@return, PlayedMinutes=@played,
                                    TodayMinutes=@today, TodayDate=@todayDate, Status=@status, WasOnline=@was WHERE BotId=@id
                                """,
                                ("@persona", (long)p.Persona), ("@days", (long)p.PlayDaysMask), ("@want", p.NextWantUtc.Ticks), ("@return", p.ReturnUtc.Ticks),
                                ("@played", (long)p.PlayedMinutes), ("@today", (long)p.TodayMinutes), ("@todayDate", p.TodayDate.Ticks),
                                ("@status", (long)p.Status), ("@was", p.WasOnline ? 1 : 0), ("@id", botId));
                    }
                    transaction.Commit();
                }
                _countsDirty = true;
            }
            if (_countsDirty)
                RefreshCounts();
            return batch.Count;
        }

        public BotPopulationProfile GetProfile(long botId)
        {
            lock (_gate)
            {
                BotRow row = BotRowSql.Select(_archive, null, "SELECT * FROM bots WHERE BotId=@id", ("@id", botId)).FirstOrDefault();
                return row == null ? null : ProfileOf(row);
            }
        }

        private static BotPopulationProfile ProfileOf(BotRow row) =>
            new((byte)row.Long("Persona"), (byte)row.Long("PlayDaysMask"), Ticks(row.Long("NextWantUtc")), Ticks(row.Long("ReturnUtc")),
                (int)row.Long("PlayedMinutes"), (int)row.Long("TodayMinutes"), Ticks(row.Long("TodayDate")), (byte)row.Long("Status"),
                row.Long("WasOnline") != 0);

        private static DateTime Ticks(long ticks) =>
            new(Math.Clamp(ticks, DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks), DateTimeKind.Utc);

        private static ArchiveCandidate CandidateOf(BotRow row) =>
            new(row.Long("BotId"), (eRealm)row.Long("Realm"), (int)row.Long("Level"), (int)row.Long("ClassId"), Ticks(row.Long("LastSeenUtc")), ProfileOf(row));

        // ---- candidate queries

        public IReadOnlyList<ArchiveCandidate> Candidates(eRealm realm, int minLevel, int maxLevel, int limit, DateTime wantBeforeUtc)
        {
            if (limit <= 0)
                return Array.Empty<ArchiveCandidate>();
            List<BotRow> rows;
            lock (_gate)
                rows = BotRowSql.Select(_archive, null, """
                    SELECT BotId, Realm, Level, ClassId, LastSeenUtc, Persona, PlayDaysMask, NextWantUtc, ReturnUtc, PlayedMinutes,
                        TodayMinutes, TodayDate, Status, WasOnline
                    FROM bots WHERE Realm=@r AND Level BETWEEN @a AND @b AND State IN (0,1) AND Online=0 AND NextWantUtc<=@t
                    ORDER BY NextWantUtc LIMIT @n
                    """,
                    ("@r", (long)realm), ("@a", minLevel), ("@b", maxLevel), ("@t", wantBeforeUtc.Ticks), ("@n", limit * 2 + 8));
            return rows.Select(CandidateOf).Where(c => !_hooks.IsBusy(c.BotId) && !_hooks.IsHired(c.BotId)).Take(limit).ToArray();
        }

        public IReadOnlyList<long> RandomRetireCandidates(int count, Func<ArchiveCandidate, bool> eligible, Random random = null)
        {
            if (count <= 0)
                return Array.Empty<long>();
            random ??= Random.Shared;
            long min, max;
            lock (_gate)
            {
                min = BotRowSql.Scalar(_archive, null, "SELECT MIN(BotId) FROM bots") is long a ? a : 0;
                max = BotRowSql.Scalar(_archive, null, "SELECT MAX(BotId) FROM bots") is long b ? b : 0;
            }
            var chosen = new List<long>(count);
            var seen = new HashSet<long>();
            if (max <= 0)
                return chosen;
            for (int attempt = 0; attempt < count * 8 + 16 && chosen.Count < count; attempt++)
            {
                long start = min + (long)(random.NextDouble() * (max - min + 1));
                List<BotRow> rows;
                lock (_gate)
                    rows = BotRowSql.Select(_archive, null, """
                        SELECT BotId, Realm, Level, ClassId, LastSeenUtc, Persona, PlayDaysMask, NextWantUtc, ReturnUtc, PlayedMinutes,
                            TodayMinutes, TodayDate, Status, WasOnline
                        FROM bots WHERE BotId>=@s AND State IN (0,1) AND Online=0 ORDER BY BotId LIMIT 8
                        """, ("@s", start));
                foreach (BotRow row in rows)
                {
                    ArchiveCandidate candidate = CandidateOf(row);
                    if (!seen.Add(candidate.BotId) || _hooks.IsBusy(candidate.BotId) || _hooks.IsHired(candidate.BotId))
                        continue;
                    if (eligible != null && !eligible(candidate))
                        continue;
                    chosen.Add(candidate.BotId);
                    break;   // one per random window keeps the pick spread out
                }
            }
            return chosen;
        }

        /// <summary>Warm offline bots last seen before the given time, oldest first.</summary>
        public IReadOnlyList<long> IdleWarm(int limit, DateTime lastSeenBeforeUtc)
        {
            lock (_gate)
                return BotRowSql.Select(_archive, null,
                        "SELECT BotId FROM bots WHERE State=0 AND Online=0 AND LastSeenUtc<@t ORDER BY LastSeenUtc LIMIT @n",
                        ("@t", lastSeenBeforeUtc.Ticks), ("@n", limit))
                    .Select(row => row.Long("BotId")).ToArray();
        }

        public long WarmOfflineCount()
        {
            lock (_gate)
                return BotRowSql.Scalar(_archive, null, "SELECT COUNT(*) FROM bots WHERE State=0 AND Online=0") is long n ? n : 0;
        }

        // ---- warm -> cold

        /// <summary>Moves an idle warm bot into the cold archive. False when it is busy, the locks are taken, or it failed.</summary>
        public bool ArchiveToCold(long botId)
        {
            if (StateOf(botId) != BotArchiveState.Warm || _hooks.IsBusy(botId) || _hooks.IsHired(botId))
                return false;
            bool moved = false;
            bool ran = _hooks.TryLocked(() => moved = ArchiveLocked(botId));
            return ran && moved;
        }

        private bool ArchiveLocked(long botId)
        {
            if (_hooks.IsBusy(botId))
                return false;
            using SQLiteConnection world = BotRowSql.Open(_worldConnectionString);
            BotArchiveBlob blob = BotRowSql.ReadOwned(world, null, botId);
            if (blob == null || blob.Record.Long("IsOnline") != 0)
                return false;
            byte[] data = blob.Encode();
            lock (_gate)
                Exec("UPDATE bots SET State=3, Blob=@b, BlobVersion=@v, Level=@l, Online=0 WHERE BotId=@id AND State=0",
                    ("@b", data), ("@v", (long)BotArchiveBlob.CurrentVersion), ("@l", Math.Clamp(blob.Record.Long("Level"), 1, 50)), ("@id", botId));
            if (StateOf(botId) != BotArchiveState.Archiving)
                return false;

            object token = _hooks.RemoveListings(AutonomousBotEconomy.GetOwnerId(botId));
            try
            {
                using SQLiteTransaction transaction = world.BeginTransaction();
                BotRowSql.DeleteOwned(world, transaction, botId, record: true, guild: false);
                // The guild row and pending commands stay: an archived bot keeps its guild.
                transaction.Commit();
            }
            catch (Exception exception)
            {
                _hooks.RestoreListings(token);
                lock (_gate)
                    Exec("UPDATE bots SET State=0, Blob=NULL, BlobVersion=0 WHERE BotId=@id AND State=3", ("@id", botId));
                PopulationLog.Error(Log, $"BOT_ARCHIVE_FAILED id={botId}: the bot stays warm", exception);
                return false;
            }

            lock (_gate)
                Exec("UPDATE bots SET State=1 WHERE BotId=@id AND State=3", ("@id", botId));
            _countsDirty = true;
            NotifyCold(botId, blob.Record.Text("Name"), true);
            return true;
        }

        private void NotifyCold(long botId, string name, bool cold)
        {
            try
            {
                _hooks.OnColdChanged(botId, name, cold);
            }
            catch (Exception exception)
            {
                PopulationLog.Warn(Log, $"BOT_ARCHIVE name state update failed for {botId}: {exception.Message}");
            }
        }

        // ---- cold -> warm

        /// <summary>Puts a cold bot back into the world database (offline, ready to log in). True when it is warm now.</summary>
        public bool RestoreToWarm(long botId)
        {
            int state = StateOf(botId);
            if (state == BotArchiveState.Warm)
                return true;
            if (state != BotArchiveState.Cold)
                return false;
            bool restored = false;
            bool ran = _hooks.TryLocked(() => restored = RestoreLocked(botId));
            return ran && restored;
        }

        private bool RestoreLocked(long botId)
        {
            byte[] data;
            lock (_gate)
            {
                data = BotRowSql.Scalar(_archive, null, "SELECT Blob FROM bots WHERE BotId=@id AND State=1", ("@id", botId)) as byte[];
                if (data == null)
                    return false;
                Exec("UPDATE bots SET State=4 WHERE BotId=@id AND State=1", ("@id", botId));
            }

            try
            {
                BotArchiveBlob blob = BotArchiveBlob.Decode(data);
                using SQLiteConnection world = BotRowSql.Open(_worldConnectionString);
                using (SQLiteTransaction transaction = world.BeginTransaction())
                {
                    if (!BotRowSql.InsertOwned(world, transaction, blob))
                        throw new InvalidOperationException($"bot {botId} already has a world record");
                    transaction.Commit();
                }
                lock (_gate)
                    Exec("UPDATE bots SET State=0, Blob=NULL, BlobVersion=0 WHERE BotId=@id AND State=4", ("@id", botId));
                _countsDirty = true;
            }
            catch (Exception exception)
            {
                lock (_gate)
                    Exec("UPDATE bots SET State=1 WHERE BotId=@id AND State=4", ("@id", botId));
                PopulationLog.Error(Log, $"BOT_RESTORE_FAILED id={botId}: the bot stays archived", exception);
                return false;
            }

            _hooks.AddListings(AutonomousBotEconomy.GetOwnerId(botId));
            NotifyCold(botId, NameOf(botId), false);
            return true;
        }

        /// <summary>The archived blob of a cold bot (tests, launcher search), or null.</summary>
        public BotArchiveBlob ReadBlob(long botId)
        {
            byte[] data;
            lock (_gate)
                data = BotRowSql.Scalar(_archive, null, "SELECT Blob FROM bots WHERE BotId=@id", ("@id", botId)) as byte[];
            return data == null ? null : BotArchiveBlob.Decode(data);
        }

        // ---- retirement (plan 3.4)

        public enum RetireResult { Retired, Busy, Hired, Failed }

        /// <summary>
        /// Deletes a bot for good: marks it Retiring, takes its listings off the Exchange, deletes relist and listing-hours
        /// rows, unique items, items, guild row, pending commands and its record (or archive blob), then frees its name.
        /// A crash after the mark is finished by <see cref="RecoverInterrupted"/>.
        /// </summary>
        public RetireResult RetireOne(long botId, bool allowHired = false, long keepCommandId = 0)
        {
            if (botId <= 0)
                return RetireResult.Failed;
            if (!allowHired && _hooks.IsHired(botId))
                return RetireResult.Hired;
            if (_hooks.IsBusy(botId))
                return RetireResult.Busy;

            string name = NameOf(botId);
            int state = StateOf(botId);
            try
            {
                using SQLiteConnection world = BotRowSql.Open(_worldConnectionString);
                BotRow record = BotRowSql.ReadBot(world, null, botId);
                name ??= record?.Text("Name");
                if (state < 0 && record != null)
                    UpsertWarm(record, null, online: false);
                lock (_gate)
                    Exec("UPDATE bots SET State=2 WHERE BotId=@id", ("@id", botId));
                _countsDirty = true;

                bool done = false;
                bool ran = _hooks.TryLocked(() =>
                {
                    if (_hooks.IsBusy(botId))
                        return;
                    object token = _hooks.RemoveListings(AutonomousBotEconomy.GetOwnerId(botId));
                    try
                    {
                        using SQLiteTransaction transaction = world.BeginTransaction();
                        BotRowSql.DeleteOwned(world, transaction, botId, record: true, guild: true, keepCommandId);
                        transaction.Commit();
                        done = true;
                    }
                    catch
                    {
                        _hooks.RestoreListings(token);
                        throw;
                    }
                });
                if (!ran || !done)
                    return RetireResult.Busy;   // stays Retiring; the worker or the next start finishes it
            }
            catch (Exception exception)
            {
                PopulationLog.Error(Log, $"BOT_RETIRE_FAILED id={botId}", exception);
                return RetireResult.Failed;
            }

            DeleteDirectoryRow(botId);
            try
            {
                _hooks.OnRetired(botId, name);
            }
            catch (Exception exception)
            {
                PopulationLog.Warn(Log, $"BOT_RETIRE_HOOK_FAILED id={botId}: {exception.Message}");
            }
            return RetireResult.Retired;
        }

        // ---- crash recovery and reconciliation (server start, background)

        /// <summary>Finishes moves and retirements a crash interrupted. Returns how many rows it settled.</summary>
        public int RecoverInterrupted()
        {
            int settled = 0;
            foreach (long botId in IdsInState(BotArchiveState.Retiring))
                if (RetireOne(botId, allowHired: true) == RetireResult.Retired)
                    settled++;

            using SQLiteConnection world = BotRowSql.Open(_worldConnectionString);
            foreach (long botId in IdsInState(BotArchiveState.Archiving))
            {
                // The blob is complete: finish deleting the world rows.
                if (_hooks.TryLocked(() =>
                    {
                        object token = _hooks.RemoveListings(AutonomousBotEconomy.GetOwnerId(botId));
                        using SQLiteTransaction transaction = world.BeginTransaction();
                        BotRowSql.DeleteOwned(world, transaction, botId, record: true, guild: false);
                        transaction.Commit();
                    }))
                {
                    lock (_gate)
                        Exec("UPDATE bots SET State=1 WHERE BotId=@id AND State=3", ("@id", botId));
                    settled++;
                }
            }

            foreach (long botId in IdsInState(BotArchiveState.Restoring))
            {
                bool warm = BotRowSql.ReadBot(world, null, botId) != null;
                lock (_gate)
                    Exec(warm ? "UPDATE bots SET State=0, Blob=NULL, BlobVersion=0 WHERE BotId=@id" : "UPDATE bots SET State=1 WHERE BotId=@id", ("@id", botId));
                settled++;
            }
            _countsDirty = true;
            return settled;
        }

        /// <summary>
        /// Keeps the directory in step with the world table: warm rows deleted while the server was stopped leave the
        /// directory, warm gamebots without a row get one (hired companions never). Returns (added, removed).
        /// </summary>
        public (int Added, int Removed) ReconcileWarm(Func<BotRow, BotPopulationProfile> profileForNew)
        {
            using SQLiteConnection world = BotRowSql.Open(_worldConnectionString);
            List<BotRow> worldRows = BotRowSql.Select(world, null,
                $"SELECT BotId, Name, Realm, Level, ClassId, RaceId, Gender, IsOnline, IsRetired FROM `{BotRowSql.BotTable}`");
            var worldIds = new HashSet<long>(worldRows.Select(row => row.Long("BotId")));
            var directoryWarm = new HashSet<long>(IdsInState(BotArchiveState.Warm));
            var directoryAll = new HashSet<long>();
            lock (_gate)
                foreach (BotRow row in BotRowSql.Select(_archive, null, "SELECT BotId FROM bots"))
                    directoryAll.Add(row.Long("BotId"));

            int removed = 0;
            foreach (long botId in directoryWarm.Where(id => !worldIds.Contains(id)))
            {
                DeleteDirectoryRow(botId);
                removed++;
            }

            int added = 0;
            foreach (BotRow row in worldRows)
            {
                long botId = row.Long("BotId");
                if (directoryAll.Contains(botId) || _hooks.IsHired(botId) || row.Long("IsRetired") != 0)
                    continue;
                UpsertWarm(row, profileForNew?.Invoke(row), online: false);
                added++;
            }
            RefreshCounts();
            return (added, removed);
        }
    }

    /// <summary>
    /// The server's archive (static facade over <see cref="BotArchive"/>). Opens runtime\data\bot-archive.sqlite3.db next to
    /// the world database on first use.
    /// </summary>
    public static class BotArchiveStore
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(BotArchiveStore));
        public const string ArchiveFileName = "bot-archive.sqlite3.db";
        private static readonly object OpenGate = new();
        private static BotArchive _current;
        private static bool _openFailed;
        private static volatile bool _ready;
        private static readonly ConcurrentQueue<RetireRequest> RetireQueue = new();
        private static readonly ConcurrentDictionary<long, byte> RetireQueued = new();

        private sealed record RetireRequest(long BotId, bool AllowHired, long CommandId, int Attempts, Action<bool, string> Done);

        /// <summary>The open archive, or null (MySQL, open failure, not started).</summary>
        public static BotArchive Current
        {
            get
            {
                if (_current != null || _openFailed)
                    return _current;
                lock (OpenGate)
                {
                    if (_current != null || _openFailed)
                        return _current;
                    try
                    {
                        string world = WorldConnectionString();
                        if (world == null)
                        {
                            _openFailed = true;
                            PopulationLog.Warn(Log, "BOT_ARCHIVE disabled: the world database is not SQLite");
                            return null;
                        }
                        string dataSource = new SQLiteConnectionStringBuilder(world).DataSource;
                        string archivePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dataSource))!, ArchiveFileName);
                        _current = new BotArchive(world, archivePath, new WorldArchiveHooks());
                        PopulationLog.Info(Log, $"BOT_ARCHIVE opened {archivePath} saved={_current.SavedCount}");
                    }
                    catch (Exception exception)
                    {
                        _openFailed = true;
                        PopulationLog.Error(Log, "BOT_ARCHIVE could not be opened; the population system stays off", exception);
                    }
                    return _current;
                }
            }
        }

        /// <summary>Tests: use this archive (null resets).</summary>
        public static void UseForTests(BotArchive archive, bool ready = true)
        {
            lock (OpenGate)
            {
                _current = archive;
                _openFailed = archive == null;
                _ready = archive != null && ready;
            }
        }

        private static string WorldConnectionString()
        {
            var config = GameServer.Instance?.Configuration;
            if (config == null || config.DBType != DOL.Database.Connection.EConnectionType.DATABASE_SQLITE)
                return null;
            return config.DBConnectionString;
        }

        /// <summary>True once the archive is open and the one-time migration has finished.</summary>
        public static bool IsReady => _ready && _current != null;

        internal static void MarkReady(bool ready) => _ready = ready;

        /// <summary>Warm + cold, non-retired, non-hired.</summary>
        public static long SavedCount => _current?.SavedCount ?? 0;

        /// <summary>Saved bots of this realm and level band that are not online.</summary>
        public static long Count(eRealm realm, int minLevel, int maxLevel) => _current?.Count(realm, minLevel, maxLevel) ?? 0;

        /// <summary>Offline saved bots of the band whose NextWantUtc is before the given time, oldest want first. Background thread.</summary>
        public static IReadOnlyList<ArchiveCandidate> Candidates(eRealm realm, int minLevel, int maxLevel, int limit, DateTime wantBeforeUtc) =>
            IsReady ? _current.Candidates(realm, minLevel, maxLevel, limit, wantBeforeUtc) : Array.Empty<ArchiveCandidate>();

        /// <summary>Queues a profile write (batched in the background).</summary>
        public static void SaveProfile(long botId, BotPopulationProfile profile) => Current?.QueueProfile(botId, profile);

        /// <summary>The stored profile, or null. Background thread.</summary>
        public static BotPopulationProfile GetProfile(long botId) => Current?.GetProfile(botId);

        /// <summary>Whether a bot is saved (warm or cold, not being retired). Background thread.</summary>
        public static bool IsSaved(long botId) => Current?.IsSaved(botId) == true;

        /// <summary>Random offline saved bots across all levels that pass <paramref name="eligible"/>. Background thread.</summary>
        public static IReadOnlyList<long> RandomRetireCandidates(int count, Func<ArchiveCandidate, bool> eligible) =>
            IsReady ? _current.RandomRetireCandidates(count, eligible) : Array.Empty<long>();

        /// <summary>Deletes these bots for good (listings, items, unique items, guild row, commands, record or archive row).
        /// Crash-safe; skips hired companions and bots that are online. Queued to a background worker.</summary>
        public static void Retire(IReadOnlyCollection<long> botIds)
        {
            if (botIds == null)
                return;
            foreach (long botId in botIds)
                EnqueueRetire(botId, allowHired: false, commandId: 0, done: null);
        }

        /// <summary>A launcher delete or a released companion: retire, then report the outcome.</summary>
        internal static void EnqueueRetire(long botId, bool allowHired, long commandId, Action<bool, string> done)
        {
            if (botId <= 0)
                return;
            if (!RetireQueued.TryAdd(botId, 0) && done == null)
                return;
            RetireQueue.Enqueue(new RetireRequest(botId, allowHired, commandId, 0, done));
        }

        internal static int PendingRetirements => RetireQueue.Count;

        /// <summary>Runs up to <paramref name="maximum"/> queued retirements (background worker tick).</summary>
        internal static int ProcessRetireQueue(int maximum)
        {
            BotArchive archive = Current;
            if (archive == null)
                return 0;
            int handled = 0;
            var retry = new List<RetireRequest>();
            while (handled < maximum && RetireQueue.TryDequeue(out RetireRequest request))
            {
                handled++;
                BotArchive.RetireResult result = archive.RetireOne(request.BotId, request.AllowHired, request.CommandId);
                switch (result)
                {
                    case BotArchive.RetireResult.Retired:
                        RetireQueued.TryRemove(request.BotId, out _);
                        request.Done?.Invoke(true, null);
                        break;
                    case BotArchive.RetireResult.Busy when request.Attempts < 240:
                        retry.Add(request with { Attempts = request.Attempts + 1 });
                        break;
                    default:
                        RetireQueued.TryRemove(request.BotId, out _);
                        request.Done?.Invoke(false, result == BotArchive.RetireResult.Hired
                            ? "A hired companion is never retired by the population system."
                            : "The bot stayed online or busy, or the deletion failed (see the server log).");
                        break;
                }
            }
            foreach (RetireRequest request in retry)
                RetireQueue.Enqueue(request);
            return handled;
        }
    }
}
