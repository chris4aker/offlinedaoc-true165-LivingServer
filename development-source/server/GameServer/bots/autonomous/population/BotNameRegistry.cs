using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace DOL.GS
{
    /// <summary>
    /// The name-claim table offline_bot_names (NameKey PRIMARY KEY NOCASE, BotId, State, ReleasedUtc, Pool) is the single
    /// source of truth for bot, companion and player names. Claims are one atomic insert-if-absent, saved together with
    /// the pool's Feistel counter. Releases, archive marks and bot ids are queued and written by a background thread
    /// (or by the next claim, in its own transaction). All writes take the shared database write gate with a try-lock.
    /// </summary>
    public sealed class BotNameRegistry : IDisposable
    {
        public const string NamesTable = "offline_bot_names";
        public const string CursorTable = "offline_bot_name_cursors";
        public const string MetaTable = "offline_bot_name_meta";

        public enum NameState : byte { Warm = 0, Archived = 1, Cooling = 2, Player = 3, Reserved = 4 }

        /// <summary>Owner 2026-11-08: a retired bot's (or deleted character's) name may be reused after 30 days.</summary>
        public static readonly TimeSpan CoolDown = TimeSpan.FromDays(30);

        public sealed class Options
        {
            public string ConnectionString { get; init; }
            /// <summary>The server's write gate (SqlObjectDatabase.WriteSerializationLock); a private lock when null.</summary>
            public object WriteGate { get; init; }
            /// <summary>Fixed world seed (tests); otherwise a random one is stored in the meta table on first use.</summary>
            public ulong? Seed { get; init; }
            public Func<DateTime> UtcNow { get; init; } = () => DateTime.UtcNow;
            /// <summary>Live invalid-names check (InvalidNamesManager), on top of the generator's own blocklist.</summary>
            public Func<string, bool> LiveBlocked { get; init; }
            /// <summary>Names on any player's friend or ignore list: never reused.</summary>
            public Func<IEnumerable<string>> ProtectedNames { get; init; }
            /// <summary>Old-table name check, used while the table is being seeded.</summary>
            public Func<string, bool> LegacyTaken { get; init; }
            public Random Random { get; init; }
            public int ClaimWaitMs { get; init; } = 250;
            public int PlayerWaitMs { get; init; } = 2000;
            public bool StartWorker { get; init; } = true;
        }

        private enum OpKind : byte { Release, SetArchived, SetBotId, ClaimPlayer }

        private readonly record struct Op(OpKind Kind, string Name, long Value);

        private readonly Options _options;
        private readonly string _connectionString;
        private readonly object _gate;
        private readonly object _readLock = new();
        private readonly object _claimLock = new();
        private readonly object _pendingLock = new();
        private readonly List<Op> _pending = new();
        private readonly Dictionary<string, (long Counter, long Signature)> _cursors = new(StringComparer.Ordinal);
        private readonly Random _random;
        private readonly AutoResetEvent _wake = new(false);
        private SQLiteConnection _read, _write;
        private Thread _worker;
        private volatile bool _stopping;
        private volatile bool _ready;
        private volatile BotNameGenerator _generator;
        private ulong _seed;

        /// <summary>Called by the background thread about every 30 s (table reload checks).</summary>
        public Action Housekeeping { get; set; }

        public BotNameRegistry(Options options, BotNameGenerator generator)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            var builder = new SQLiteConnectionStringBuilder(options.ConnectionString);
            builder.Remove("Journal Mode");
            builder.Pooling = false;
            builder.DefaultTimeout = 5;
            _connectionString = builder.ConnectionString;
            _gate = options.WriteGate ?? new object();
            _random = options.Random ?? new Random();
        }

        public BotNameGenerator Generator => _generator;

        /// <summary>True once the first-run seeding is done; new bot names are only handed out after that.</summary>
        public bool Ready => _ready;

        public ulong Seed => _seed;

        /// <summary>Swaps in new tables (live). Pools whose parts changed restart their counter.</summary>
        public void ReplaceGenerator(BotNameGenerator generator)
        {
            if (generator != null)
                _generator = generator;
        }

        // ---- opening and seeding

        /// <summary>Creates the tables, reads the counters and the world seed. Throws when the database cannot be used.</summary>
        public void Open()
        {
            bool done = false;
            for (int attempt = 0; attempt < 200 && !done; attempt++)
            {
                done = TryWrite(250, (c, tx) =>
                {
                    Exec(c, tx, $"CREATE TABLE IF NOT EXISTS {NamesTable}(NameKey TEXT NOT NULL PRIMARY KEY COLLATE NOCASE, " +
                        "BotId INTEGER NOT NULL DEFAULT 0, State INTEGER NOT NULL DEFAULT 0, ReleasedUtc INTEGER, Pool INTEGER NOT NULL DEFAULT -1) WITHOUT ROWID");
                    Exec(c, tx, $"CREATE INDEX IF NOT EXISTS {NamesTable}_cooling ON {NamesTable}(Pool, ReleasedUtc) WHERE State = 2");
                    Exec(c, tx, $"CREATE TABLE IF NOT EXISTS {CursorTable}(Pool TEXT NOT NULL PRIMARY KEY, Counter INTEGER NOT NULL, Signature INTEGER NOT NULL)");
                    Exec(c, tx, $"CREATE TABLE IF NOT EXISTS {MetaTable}(Key TEXT NOT NULL PRIMARY KEY, Value TEXT NOT NULL)");
                    string stored = Scalar(c, tx, $"SELECT Value FROM {MetaTable} WHERE Key='seed'") as string;
                    if (_options.Seed is { } fixedSeed)
                        _seed = fixedSeed;
                    else if (stored != null && ulong.TryParse(stored, out ulong s))
                        _seed = s;
                    else
                    {
                        _seed = (ulong)Random.Shared.NextInt64() ^ (ulong)DateTime.UtcNow.Ticks;
                        Exec(c, tx, $"INSERT OR REPLACE INTO {MetaTable}(Key, Value) VALUES('seed', @v)", ("@v", _seed.ToString()));
                    }
                    lock (_claimLock)
                    {
                        _cursors.Clear();
                        using var cmd = Command(c, tx, $"SELECT Pool, Counter, Signature FROM {CursorTable}");
                        using var reader = cmd.ExecuteReader();
                        while (reader.Read())
                            _cursors[reader.GetString(0)] = (reader.GetInt64(1), reader.GetInt64(2));
                    }
                    return true;
                });
            }
            if (!done)
                throw new InvalidOperationException("The bot name tables could not be opened (database busy).");
            if (_options.StartWorker && _worker == null)
            {
                _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "BotNames" };
                _worker.Start();
            }
        }

        /// <summary>
        /// Seeds (and re-checks) the claim table from the existing bots, player characters and one-word NPC names. Runs
        /// at each start on a background thread; it only adds missing rows, so it is cheap after the first time.
        /// </summary>
        public void SeedFromWorld(IEnumerable<string> extraReserved = null)
        {
            var npcNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Read(c =>
            {
                foreach (string table in new[] { "Mob", "NpcTemplate" })
                {
                    if (!HasTable(c, null, table))
                        continue;
                    using var cmd = Command(c, null, $"SELECT DISTINCT Name FROM `{table}` WHERE Name IS NOT NULL");
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                        if (!reader.IsDBNull(0) && IsOneWordProperName(reader.GetString(0)))
                            npcNames.Add(reader.GetString(0));
                }
                return 0;
            });
            foreach (string extra in extraReserved ?? [])
                if (IsOneWordProperName(extra))
                    npcNames.Add(extra);

            RetryWrite(c =>
            {
                if (HasTable(c.Connection, c.Transaction, "offline_world_bots"))
                    Exec(c.Connection, c.Transaction,
                        $"INSERT INTO {NamesTable}(NameKey, BotId, State, ReleasedUtc, Pool) " +
                        "SELECT Name, BotId, 0, NULL, -1 FROM offline_world_bots WHERE Name <> '' " +
                        "ON CONFLICT(NameKey) DO UPDATE SET " +
                        "State = CASE WHEN State IN (2, 4) THEN 0 ELSE State END, BotId = excluded.BotId, " +
                        "ReleasedUtc = CASE WHEN State IN (2, 4) THEN NULL ELSE ReleasedUtc END " +
                        "WHERE State IN (2, 4) OR (State IN (0, 1) AND BotId = 0)");
                if (HasTable(c.Connection, c.Transaction, "DOLCharacters"))
                    Exec(c.Connection, c.Transaction,
                        $"INSERT INTO {NamesTable}(NameKey, BotId, State, ReleasedUtc, Pool) " +
                        "SELECT Name, 0, 3, NULL, -1 FROM DOLCharacters WHERE Name <> '' " +
                        "ON CONFLICT(NameKey) DO UPDATE SET State = 3, BotId = 0, ReleasedUtc = NULL, Pool = -1 WHERE State IN (2, 4)");
            });
            // NPC names in chunks, so the write gate is never held for long
            string[] all = npcNames.ToArray();
            for (int start = 0; start < all.Length; start += 1000)
            {
                string[] chunk = all.Skip(start).Take(1000).ToArray();
                RetryWrite(c =>
                {
                    using var cmd = Command(c.Connection, c.Transaction,
                        $"INSERT INTO {NamesTable}(NameKey, BotId, State, ReleasedUtc, Pool) VALUES(@n, 0, 4, NULL, -1) ON CONFLICT(NameKey) DO NOTHING");
                    var p = cmd.Parameters.Add("@n", System.Data.DbType.String);
                    foreach (string name in chunk)
                    {
                        p.Value = name;
                        cmd.ExecuteNonQuery();
                    }
                });
            }
            RetryWrite(c => Exec(c.Connection, c.Transaction,
                $"INSERT OR REPLACE INTO {MetaTable}(Key, Value) VALUES('seeded', @v)", ("@v", _options.UtcNow().ToString("O"))));
            _ready = true;
        }

        /// <summary>Marks the table usable without seeding (tests).</summary>
        public void MarkReady() => _ready = true;

        private static bool IsOneWordProperName(string name) =>
            name is { Length: >= 3 and <= 20 } && char.IsUpper(name[0]) && name.All(ch => ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z');

        // ---- claims

        /// <summary>
        /// Claims a fresh name for a new bot of this race and gender (another race of its realm only when this one has
        /// walked all its names and has nothing cooled down). False when the database is busy (try again later) or
        /// everything is used up. Never throws.
        /// </summary>
        public bool TryClaimNewBotName(eRealm realm, eRace race, eGender gender, long botId, out string name)
        {
            name = null;
            if (!_ready)
                return false;
            try
            {
                if (BotNameGenerator.RealmOf(race) == eRealm.None || realm != eRealm.None && BotNameGenerator.RealmOf(race) != realm)
                {
                    var realmRaces = BotNameGenerator.StyleRaces.Where(r => BotNameGenerator.RealmOf(r) == realm).ToArray();
                    if (realmRaces.Length == 0)
                        return false;
                    lock (_claimLock)
                        race = realmRaces[_random.Next(realmRaces.Length)];
                }
                if (gender != eGender.Female)
                    gender = eGender.Male;

                var races = new List<eRace> { race };
                lock (_claimLock)
                    races.AddRange(BotNameGenerator.StyleRaces.Where(r => r != race && BotNameGenerator.RealmOf(r) == BotNameGenerator.RealmOf(race))
                        .OrderBy(_ => _random.Next()));
                foreach (eRace candidate in races)
                {
                    ClaimResult result = ClaimFor(candidate, gender, botId, out name);
                    if (result == ClaimResult.Claimed)
                        return true;
                    if (result == ClaimResult.Busy)
                        return false;
                }
                return false;
            }
            catch (Exception)
            {
                name = null;
                return false;
            }
        }

        private enum ClaimResult { Claimed, Busy, Exhausted }

        private ClaimResult ClaimFor(eRace race, eGender gender, long botId, out string claimed)
        {
            claimed = null;
            string found = null;
            bool exhausted = false;
            lock (_claimLock)
            {
                BotNameGenerator generator = _generator;
                IReadOnlyList<BotNameGenerator.SubPool> pools = generator.PoolsFor(race, gender);
                var advanced = new Dictionary<string, (long Counter, long Signature)>(StringComparer.Ordinal);
                long Counter(BotNameGenerator.SubPool p)
                {
                    if (advanced.TryGetValue(p.Key, out var a))
                        return a.Counter;
                    return _cursors.TryGetValue(p.Key, out var c) && c.Signature == unchecked((long)p.Signature) ? c.Counter : 0;
                }

                bool committed = TryWrite(_options.ClaimWaitMs, (c, tx) =>
                {
                    var watch = Stopwatch.StartNew();
                    for (int attempt = 0; attempt < 600 && watch.ElapsedMilliseconds < 15; attempt++)
                    {
                        BotNameGenerator.SubPool pool = BotNameGenerator.PickWeighted(pools, _random, p => Counter(p) < p.Size);
                        if (pool == null)
                        {
                            exhausted = true;
                            break;
                        }
                        long counter = Counter(pool);
                        advanced[pool.Key] = (counter + 1, unchecked((long)pool.Signature));
                        string name = generator.Decode(pool, BotNameGenerator.Permute(counter, pool.Size, BotNameGenerator.KeyFor(pool, _seed)));
                        if (name == null || IsLiveBlocked(name))
                            continue;
                        if (Exec(c, tx, $"INSERT INTO {NamesTable}(NameKey, BotId, State, ReleasedUtc, Pool) VALUES(@n, @b, 0, NULL, @p) ON CONFLICT(NameKey) DO NOTHING",
                                ("@n", name), ("@b", botId), ("@p", pool.PoolId)) == 1)
                        {
                            found = name;
                            break;
                        }
                    }
                    if (found == null && exhausted)
                        found = ReuseCooled(c, tx, pools.Select(p => p.PoolId).Distinct().ToArray(), botId, generator);
                    foreach (var (key, value) in advanced)
                        Exec(c, tx, $"INSERT INTO {CursorTable}(Pool, Counter, Signature) VALUES(@k, @c, @s) " +
                            "ON CONFLICT(Pool) DO UPDATE SET Counter = excluded.Counter, Signature = excluded.Signature",
                            ("@k", key), ("@c", value.Counter), ("@s", value.Signature));
                    return true;
                });
                if (!committed)
                    return ClaimResult.Busy;
                foreach (var (key, value) in advanced)
                    _cursors[key] = value;
            }
            claimed = found;
            if (found != null)
                return ClaimResult.Claimed;
            return exhausted ? ClaimResult.Exhausted : ClaimResult.Busy;
        }

        /// <summary>Second pass: a name of the same pool whose bot retired 30+ days ago and is on nobody's friend or
        /// ignore list.</summary>
        private string ReuseCooled(SQLiteConnection c, SQLiteTransaction tx, int[] poolIds, long botId, BotNameGenerator generator)
        {
            long cut = new DateTimeOffset(_options.UtcNow() - CoolDown).ToUnixTimeSeconds();
            HashSet<string> keep = null;
            foreach (int poolId in poolIds)
            {
                var candidates = new List<string>();
                using (var cmd = Command(c, tx, $"SELECT NameKey FROM {NamesTable} WHERE State = 2 AND Pool = @p AND ReleasedUtc <= @cut ORDER BY ReleasedUtc LIMIT 64",
                           ("@p", poolId), ("@cut", cut)))
                using (var reader = cmd.ExecuteReader())
                    while (reader.Read())
                        candidates.Add(reader.GetString(0));
                foreach (string name in candidates)
                {
                    keep ??= new HashSet<string>(SafeProtectedNames(), StringComparer.OrdinalIgnoreCase);
                    if (keep.Contains(name) || IsLiveBlocked(name) || !generator.Ok(name.ToLowerInvariant()))
                        continue;
                    if (Exec(c, tx, $"UPDATE {NamesTable} SET State = 0, BotId = @b, ReleasedUtc = NULL WHERE NameKey = @n AND State = 2",
                            ("@b", botId), ("@n", name)) == 1)
                        return name;
                }
            }
            return null;
        }

        private IEnumerable<string> SafeProtectedNames()
        {
            try
            {
                return _options.ProtectedNames?.Invoke()?.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToArray() ?? [];
            }
            catch (Exception)
            {
                return [];
            }
        }

        private bool IsLiveBlocked(string name)
        {
            try
            {
                return _options.LiveBlocked?.Invoke(name) ?? false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Character creation: claims the name for a player. Allowed for free names, cooling-down names and
        /// NPC names; refused for any bot's (warm or archived) or player's name.</summary>
        public bool TryClaimPlayerName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            name = name.Trim();
            if (!_ready && SafeLegacyTaken(name))
                return false;
            bool claimed = false;
            bool committed = TryWrite(_options.PlayerWaitMs, (c, tx) =>
            {
                claimed = Exec(c, tx, $"INSERT INTO {NamesTable}(NameKey, BotId, State, ReleasedUtc, Pool) VALUES(@n, 0, 3, NULL, -1) " +
                    "ON CONFLICT(NameKey) DO UPDATE SET State = 3, BotId = 0, ReleasedUtc = NULL, Pool = -1 WHERE State IN (2, 4)", ("@n", name)) == 1;
                return true;
            });
            if (committed)
                return claimed;
            // the database stayed busy: decide from a read and write the claim in the background
            if (IsNameTaken(name))
                return false;
            Enqueue(new Op(OpKind.ClaimPlayer, name, 0));
            return true;
        }

        /// <summary>A name chosen by hand for a bot (a hired companion): same rules as a player's name.</summary>
        public bool TryClaimBotName(string name, long botId)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            name = name.Trim();
            if (!_ready && SafeLegacyTaken(name))
                return false;
            bool claimed = false;
            bool committed = TryWrite(_options.PlayerWaitMs, (c, tx) =>
            {
                claimed = Exec(c, tx, $"INSERT INTO {NamesTable}(NameKey, BotId, State, ReleasedUtc, Pool) VALUES(@n, @b, 0, NULL, -1) " +
                    "ON CONFLICT(NameKey) DO UPDATE SET State = 0, BotId = @b, ReleasedUtc = NULL, Pool = -1 WHERE State IN (2, 4)",
                    ("@n", name), ("@b", botId)) == 1;
                return true;
            });
            return committed ? claimed : false;
        }

        /// <summary>True when a bot (warm or archived) or a player wears the name. Cooling-down and NPC names are free
        /// for players. A read only (no write lock).</summary>
        public bool IsNameTaken(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            name = name.Trim();
            NameState? state = GetState(name);
            if (state is NameState.Warm or NameState.Archived or NameState.Player)
                return true;
            return !_ready && SafeLegacyTaken(name);
        }

        private bool SafeLegacyTaken(string name)
        {
            try
            {
                return _options.LegacyTaken?.Invoke(name) ?? false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public NameState? GetState(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            object value = Read(c => Scalar(c, null, $"SELECT State FROM {NamesTable} WHERE NameKey = @n", ("@n", name.Trim())));
            return value == null || value is DBNull ? null : (NameState)Convert.ToInt32(value);
        }

        public (long BotId, NameState State, long? ReleasedUnix, int Pool)? GetRow(string name)
        {
            return Read(c =>
            {
                using var cmd = Command(c, null, $"SELECT BotId, State, ReleasedUtc, Pool FROM {NamesTable} WHERE NameKey = @n", ("@n", name));
                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                    return ((long, NameState, long?, int)?)null;
                return (reader.GetInt64(0), (NameState)reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetInt64(2), reader.GetInt32(3));
            });
        }

        public long Count(NameState state) =>
            Convert.ToInt64(Read(c => Scalar(c, null, $"SELECT COUNT(*) FROM {NamesTable} WHERE State = @s", ("@s", (int)state))));

        // ---- queued writes

        public void ReleaseBotName(string name) => Enqueue(new Op(OpKind.Release, name, 1));
        public void ReleasePlayerName(string name) => Enqueue(new Op(OpKind.Release, name, 3));
        public void MarkArchived(string name, bool archived) => Enqueue(new Op(OpKind.SetArchived, name, archived ? 1 : 0));
        public void SetBotId(string name, long botId) => Enqueue(new Op(OpKind.SetBotId, name, botId));

        public int PendingCount
        {
            get
            {
                lock (_pendingLock)
                    return _pending.Count;
            }
        }

        private void Enqueue(Op op)
        {
            if (string.IsNullOrWhiteSpace(op.Name))
                return;
            lock (_pendingLock)
                _pending.Add(op with { Name = op.Name.Trim() });
            _wake.Set();
        }

        /// <summary>Writes the queued releases and marks now (tests, shutdown). True when the queue is empty.</summary>
        public bool Flush(int timeoutMs)
        {
            var watch = Stopwatch.StartNew();
            while (PendingCount > 0)
            {
                TryWrite(50, (_, _) => true);   // every write transaction applies the queue first
                if (PendingCount > 0)
                {
                    if (watch.ElapsedMilliseconds > timeoutMs)
                        return false;
                    Thread.Sleep(10);
                }
            }
            return true;
        }

        private void ApplyPending(SQLiteConnection c, SQLiteTransaction tx, List<Op> taken)
        {
            long now = new DateTimeOffset(_options.UtcNow()).ToUnixTimeSeconds();
            foreach (Op op in taken)
            {
                switch (op.Kind)
                {
                    case OpKind.Release when op.Value == 3:
                        Exec(c, tx, $"UPDATE {NamesTable} SET State = 2, ReleasedUtc = @t WHERE NameKey = @n AND State = 3", ("@t", now), ("@n", op.Name));
                        break;
                    case OpKind.Release:
                        Exec(c, tx, $"UPDATE {NamesTable} SET State = 2, ReleasedUtc = @t WHERE NameKey = @n AND State IN (0, 1)", ("@t", now), ("@n", op.Name));
                        break;
                    case OpKind.SetArchived:
                        Exec(c, tx, $"UPDATE {NamesTable} SET State = @s WHERE NameKey = @n AND State IN (0, 1)", ("@s", op.Value), ("@n", op.Name));
                        break;
                    case OpKind.SetBotId:
                        Exec(c, tx, $"UPDATE {NamesTable} SET BotId = @b WHERE NameKey = @n AND State IN (0, 1)", ("@b", op.Value), ("@n", op.Name));
                        break;
                    case OpKind.ClaimPlayer:
                        Exec(c, tx, $"INSERT INTO {NamesTable}(NameKey, BotId, State, ReleasedUtc, Pool) VALUES(@n, 0, 3, NULL, -1) " +
                            "ON CONFLICT(NameKey) DO UPDATE SET State = 3, BotId = 0, ReleasedUtc = NULL, Pool = -1 WHERE State IN (2, 4)", ("@n", op.Name));
                        break;
                }
            }
        }

        private void WorkerLoop()
        {
            var housekeeping = Stopwatch.StartNew();
            while (!_stopping)
            {
                _wake.WaitOne(1000);
                if (_stopping)
                    break;
                try
                {
                    if (PendingCount > 0)
                    {
                        // try-lock: never wait behind a long save batch; come back shortly instead
                        if (!TryWrite(0, (_, _) => true))
                        {
                            Thread.Sleep(25);
                            _wake.Set();
                        }
                    }
                    if (housekeeping.Elapsed.TotalSeconds >= 30)
                    {
                        housekeeping.Restart();
                        Housekeeping?.Invoke();
                    }
                }
                catch (Exception)
                {
                    Thread.Sleep(250);
                }
            }
        }

        // ---- database plumbing

        private readonly record struct WriteContext(SQLiteConnection Connection, SQLiteTransaction Transaction);

        private void RetryWrite(Action<WriteContext> work)
        {
            for (int attempt = 0; attempt < 400; attempt++)
            {
                if (TryWrite(50, (c, tx) =>
                    {
                        work(new WriteContext(c, tx));
                        return true;
                    }))
                    return;
                Thread.Sleep(20);
            }
            throw new InvalidOperationException("The bot name table stayed busy.");
        }

        /// <summary>
        /// One write transaction under the shared write gate (try-lock with <paramref name="waitMs"/>). Queued releases
        /// go first in the same transaction. The work returns true to commit. False when the gate stayed busy or it failed.
        /// </summary>
        private bool TryWrite(int waitMs, Func<SQLiteConnection, SQLiteTransaction, bool> work)
        {
            if (!Monitor.TryEnter(_gate, Math.Max(0, waitMs)))
                return false;
            List<Op> taken = null;
            try
            {
                _write ??= OpenConnection();
                lock (_pendingLock)
                {
                    if (_pending.Count > 0)
                    {
                        taken = new List<Op>(_pending);
                        _pending.Clear();
                    }
                }
                using SQLiteTransaction tx = _write.BeginTransaction();
                if (taken != null)
                    ApplyPending(_write, tx, taken);
                if (!work(_write, tx))
                {
                    tx.Rollback();
                    Requeue(taken);
                    return false;
                }
                tx.Commit();
                return true;
            }
            catch (Exception)
            {
                Requeue(taken);
                try
                {
                    _write?.Dispose();
                }
                catch (Exception) { }
                _write = null;
                return false;
            }
            finally
            {
                Monitor.Exit(_gate);
            }
        }

        private void Requeue(List<Op> taken)
        {
            if (taken == null || taken.Count == 0)
                return;
            lock (_pendingLock)
                _pending.InsertRange(0, taken);
        }

        private T Read<T>(Func<SQLiteConnection, T> work)
        {
            lock (_readLock)
            {
                try
                {
                    _read ??= OpenConnection();
                    return work(_read);
                }
                catch (Exception)
                {
                    try
                    {
                        _read?.Dispose();
                    }
                    catch (Exception) { }
                    _read = null;
                    throw;
                }
            }
        }

        /// <summary>Runs a read-only query on the registry's read connection (seeding helpers, friend lists).</summary>
        public T Query<T>(Func<SQLiteConnection, T> work) => Read(work);

        private SQLiteConnection OpenConnection()
        {
            var connection = new SQLiteConnection(_connectionString);
            connection.Open();
            using var busy = connection.CreateCommand();
            busy.CommandText = "PRAGMA busy_timeout=5000;";
            busy.ExecuteNonQuery();
            return connection;
        }

        internal static bool HasTable(SQLiteConnection c, SQLiteTransaction tx, string table) =>
            Convert.ToInt64(Scalar(c, tx, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @t COLLATE NOCASE", ("@t", table))) > 0;

        private static SQLiteCommand Command(SQLiteConnection c, SQLiteTransaction tx, string sql, params (string Name, object Value)[] args)
        {
            var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            foreach (var (n, v) in args)
                cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
            return cmd;
        }

        private static int Exec(SQLiteConnection c, SQLiteTransaction tx, string sql, params (string Name, object Value)[] args)
        {
            using var cmd = Command(c, tx, sql, args);
            return cmd.ExecuteNonQuery();
        }

        private static object Scalar(SQLiteConnection c, SQLiteTransaction tx, string sql, params (string Name, object Value)[] args)
        {
            using var cmd = Command(c, tx, sql, args);
            return cmd.ExecuteScalar();
        }

        public void Dispose()
        {
            _stopping = true;
            _wake.Set();
            _worker?.Join(2000);
            Flush(2000);
            lock (_readLock)
            {
                _read?.Dispose();
                _read = null;
            }
            lock (_gate)
            {
                _write?.Dispose();
                _write = null;
            }
        }
    }
}
