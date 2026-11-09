using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using DOL.Database;
using DOL.Events;

namespace DOL.GS
{
    /// <summary>One archived level-50 bot in a realm's answer pool (plan 3.7).</summary>
    public sealed record RallyPoolEntry(long BotId, eRealm Realm, DateTime LastSeenUtc, bool SteadyPlayer,
        ushort BindRegion = 0, int BindX = 0, int BindY = 0);

    /// <summary>What the answer pool needs from the rest of the server; the live one is <see cref="CoreRallyBackend"/>,
    /// tests use a fake.</summary>
    public interface IRallyGuestBackend
    {
        /// <summary>The archive and lifecycle exist (CORE built and the population scheduler is on).</summary>
        bool ArchiveAvailable { get; }
        /// <summary>Archived level-50s of a realm (background thread; index read, no table scan).</summary>
        IReadOnlyList<RallyPoolEntry> LoadPool(eRealm realm, int limit);
        bool RequestLogin(long botId);
        /// <summary>The lifecycle still has this login queued (the login lane runs at 2 a second in total).</summary>
        bool IsLoggingIn(long botId);
        bool RequestLogout(long botId);
        bool IsLive(long botId);
        /// <summary>A guest that came online joins its raid's reserve (returns false while it cannot yet).</summary>
        bool Reserve(string eventId, long botId);
        string GuildIdOf(long botId);
    }

    /// <summary>A Horn of Rallying sound under the new rules (AutonomousRealmRaid.Start): input, and what it got.</summary>
    public sealed class RallyRequest
    {
        public string SounderGuildId;
        /// <summary>This sound may log archived 50s in (option on, archive ready, sounder not on cooldown).</summary>
        public bool ArchiveAllowed;
        public RallyAnswer Answer;
        public int GuestsSummoned;
    }

    /// <summary>How many answer a horn: idle live 50s first, then archived 50s, then busy live 50s (owner 6b item 8).</summary>
    public readonly record struct RallyAnswer(int Idle, int Guests, int Busy)
    {
        public int Live => Idle + Busy;
        public int Total => Idle + Guests + Busy;
    }

    /// <summary>The horn's numbers and pure rules (plan 3.7, owner answers 6b item 8).</summary>
    public static class RallyAnswerPolicy
    {
        public const int MaximumAnswerers = RealmRaidRecruitmentPolicy.MaximumBots;          // 300
        public const int MinimumToSound = RealmRaidRecruitmentPolicy.AutonomousMinimumPresent; // 200
        public const int RealmGuestCap = 340;
        public const int TotalGuestCap = 600;
        public const long CooldownMilliseconds = 30 * 60_000L;
        public const long LootMilliseconds = 60_000L;
        public const long LingerMinMilliseconds = 5 * 60_000L;
        public const long LingerMaxMilliseconds = 20 * 60_000L;
        public const long ResoundKeepMilliseconds = 10 * 60_000L;
        public const double LoginsPerSecond = 5;
        public const long LoginTimeoutMilliseconds = 6 * 60_000L;
        public const long LogoutRetryMilliseconds = 30_000L;
        public const long ReserveRetryMilliseconds = 5_000L;
        public const int LoginAttempts = 3;
        public const long FailedLoginSkipMilliseconds = 10 * 60_000L;
        public const int PoolLimit = 600;

        public const long FullStartWaitMilliseconds = 3 * 60_000L;

        /// <summary>A horn raid goes in with 300 staged, or with 200+ staged once 3 minutes have passed since 200 were
        /// first staged, or with 200+ when everyone still assigned is already there (no reason to wait).</summary>
        public static bool HornReady(int present, int coming, long minimumReachedTick, long now, bool landed) =>
            landed && (present >= MaximumAnswerers ||
                       present >= MinimumToSound &&
                       (minimumReachedTick > 0 && now - minimumReachedTick >= FullStartWaitMilliseconds || present >= coming));

        /// <summary>Who answers, in the owner's order, capped at 300 answerers and the guest caps.</summary>
        public static RallyAnswer Plan(int idleLive, int archivedFree, int busyLive, int realmGuests, int totalGuests, bool archiveAllowed)
        {
            int idle = Math.Clamp(idleLive, 0, MaximumAnswerers);
            int room = MaximumAnswerers - idle;
            int guestRoom = archiveAllowed
                ? Math.Max(0, Math.Min(RealmGuestCap - realmGuests, TotalGuestCap - totalGuests)) : 0;
            int guests = Math.Clamp(Math.Min(archivedFree, guestRoom), 0, room);
            int busy = Math.Clamp(busyLive, 0, room - guests);
            return new RallyAnswer(idle, guests, busy);
        }

        /// <summary>Answer-pool score: the sounder's guild +40, a veteran or regular +20, seen within 7 days +15,
        /// bound near the muster hub up to +10.</summary>
        public static int Score(RallyPoolEntry entry, string guildId, string sounderGuildId, ushort hubRegion, Vector3 hub, DateTime nowUtc)
        {
            int score = 0;
            if (!string.IsNullOrEmpty(sounderGuildId) && string.Equals(guildId, sounderGuildId, StringComparison.Ordinal))
                score += 40;
            if (entry.SteadyPlayer)
                score += 20;
            if (nowUtc - entry.LastSeenUtc <= TimeSpan.FromDays(7))
                score += 15;
            if (entry.BindRegion != 0 && entry.BindRegion == hubRegion)
            {
                double distance = Math.Sqrt((entry.BindX - hub.X) * (double)(entry.BindX - hub.X) + (entry.BindY - hub.Y) * (double)(entry.BindY - hub.Y));
                score += distance <= 20_000 ? 10 : distance <= 60_000 ? 5 : 2;
            }
            return score;
        }

        /// <summary>When a guest logs out after its raid ended: 60 s loot plus a 5-20 minute linger after a raid that
        /// went in; 10 minutes (a chance to sound again) after a raid stopped before it started.</summary>
        public static long LogoutDelay(bool started, double random01) => started
            ? LootMilliseconds + LingerMinMilliseconds + (long)((LingerMaxMilliseconds - LingerMinMilliseconds) * Math.Clamp(random01, 0, 1))
            : ResoundKeepMilliseconds;

        /// <summary>The horn window's line for an available event (at most 63 bytes).</summary>
        public static string Status(RallyAnswer answer, long cooldownRemaining)
        {
            string rest = cooldownRemaining > 0 ? $" (horn rests {Math.Max(1, (cooldownRemaining + 59_999) / 60_000)} min)" : string.Empty;
            if (answer.Total < MinimumToSound)
                return cooldownRemaining > 0
                    ? $"Horn rests {Math.Max(1, (cooldownRemaining + 59_999) / 60_000)} min - only {answer.Live} in the field"
                    : $"Not enough champions: {answer.Total} of {MinimumToSound} would answer";
            return answer.Guests > 0
                ? $"Ready - {answer.Live} in the field, {answer.Guests} more will answer"
                : $"Ready - {answer.Live} in the field will answer{rest}";
        }
    }

    /// <summary>The guests the horn has logged in, by bot id (pure; ids and a backend only, so tests need no world).</summary>
    public sealed class RallyGuestBook
    {
        public enum GuestState : byte { Queued, LoggingIn, Live, Lingering, LoggingOut }

        public sealed class Guest
        {
            public long BotId;
            public eRealm Realm;
            public string EventId;
            public GuestState State;
            public long Since;
            public long LogoutAt;
            public int Failures;
            public bool Reserved;
            public bool EverLive;
            public long NextReserveTry;
        }

        private sealed class RallyEvent
        {
            public string Id;
            public eRealm Realm;
            public int Wanted;
            public string SounderGuildId;
            public ushort HubRegion;
            public Vector3 Hub;
        }

        private readonly IRallyGuestBackend _backend;
        private readonly Random _random;
        private readonly object _sync = new();
        private readonly ConcurrentDictionary<long, Guest> _guests = new();
        // bots whose logins failed or timed out are not picked again for a while
        private readonly ConcurrentDictionary<long, long> _skipUntil = new();
        private long _clock;
        private readonly Dictionary<string, RallyEvent> _events = new();
        private readonly IReadOnlyList<RallyPoolEntry>[] _pools = new IReadOnlyList<RallyPoolEntry>[4];
        private double _tokens = RallyAnswerPolicy.LoginsPerSecond;
        private long _lastPulse;

        public RallyGuestBook(IRallyGuestBackend backend, Random random = null)
        {
            _backend = backend;
            _random = random ?? new Random();
        }

        public IRallyGuestBackend Backend => _backend;

        public bool IsGuest(long botId) => _guests.ContainsKey(botId);

        public Guest Get(long botId) => _guests.TryGetValue(botId, out Guest guest) ? guest : null;

        public int GuestCount(eRealm realm) => _guests.Values.Count(g => g.Realm == realm);

        public int TotalGuests => _guests.Count;

        /// <summary>Guests of an event still on their way to log in (counted as coming by the raid's start rule).</summary>
        public int Pending(string eventId) =>
            _guests.Values.Count(g => g.EventId == eventId && g.State is GuestState.Queued or GuestState.LoggingIn);

        public void SetPool(eRealm realm, IReadOnlyList<RallyPoolEntry> pool)
        {
            int index = (int)realm;
            if (index is >= 1 and <= 3)
                Volatile.Write(ref _pools[index], pool ?? Array.Empty<RallyPoolEntry>());
        }

        private IReadOnlyList<RallyPoolEntry> Pool(eRealm realm)
        {
            int index = (int)realm;
            return index is >= 1 and <= 3 ? Volatile.Read(ref _pools[index]) ?? Array.Empty<RallyPoolEntry>() : Array.Empty<RallyPoolEntry>();
        }

        /// <summary>Archived 50s of the realm that could answer now (not online, not already a guest).</summary>
        public int ArchivedFree(eRealm realm)
        {
            long clock = Interlocked.Read(ref _clock);
            return Pool(realm).Count(e => Free(e.BotId, clock));
        }

        // not already a guest, not recently failed, not online or being logged in by the scheduler
        private bool Free(long botId, long clock) =>
            !_guests.ContainsKey(botId) && !Skipped(botId, clock) && !_backend.IsLive(botId) && !_backend.IsLoggingIn(botId);

        /// <summary>The best <paramref name="count"/> free archived 50s by score (then most recently seen).</summary>
        public IReadOnlyList<long> Pick(eRealm realm, int count, string sounderGuildId, ushort hubRegion, Vector3 hub, DateTime nowUtc)
        {
            if (count <= 0) return Array.Empty<long>();
            long clock = Interlocked.Read(ref _clock);
            return Pool(realm).Where(e => Free(e.BotId, clock))
                .Select(e => (e, Score: RallyAnswerPolicy.Score(e, _backend.GuildIdOf(e.BotId), sounderGuildId, hubRegion, hub, nowUtc)))
                .OrderByDescending(p => p.Score).ThenByDescending(p => p.e.LastSeenUtc).ThenBy(p => p.e.BotId)
                .Take(count).Select(p => p.e.BotId).ToArray();
        }

        private bool Skipped(long botId, long now) => _skipUntil.TryGetValue(botId, out long until) && now < until;

        private void Drop(Guest guest, long now)
        {
            _guests.TryRemove(guest.BotId, out _);
            _skipUntil[guest.BotId] = now + RallyAnswerPolicy.FailedLoginSkipMilliseconds;
            if (_skipUntil.Count > 2_000)
                foreach (var old in _skipUntil.Where(p => p.Value <= now).ToArray()) _skipUntil.TryRemove(old.Key, out _);
        }

        private int RoomFor(eRealm realm) => Math.Max(0, Math.Min(RallyAnswerPolicy.RealmGuestCap - GuestCount(realm),
            RallyAnswerPolicy.TotalGuestCap - TotalGuests));

        /// <summary>A horn sounded: queue up to <paramref name="count"/> archived 50s to log in for the event. Returns how many.</summary>
        public int Summon(string eventId, eRealm realm, int count, string sounderGuildId, ushort hubRegion, Vector3 hub, long now, DateTime nowUtc)
        {
            lock (_sync)
            {
                _events[eventId] = new RallyEvent { Id = eventId, Realm = realm, Wanted = Math.Max(0, count), SounderGuildId = sounderGuildId,
                    HubRegion = hubRegion, Hub = hub };
                return QueueLocked(_events[eventId], Math.Min(count, RoomFor(realm)), now, nowUtc);
            }
        }

        private int QueueLocked(RallyEvent rally, int count, long now, DateTime nowUtc)
        {
            int queued = 0;
            foreach (long id in Pick(rally.Realm, count, rally.SounderGuildId, rally.HubRegion, rally.Hub, nowUtc))
                if (_guests.TryAdd(id, new Guest { BotId = id, Realm = rally.Realm, EventId = rally.Id, State = GuestState.Queued, Since = now }))
                    queued++;
            return queued;
        }

        /// <summary>A guest still online answered a new sound (re-sound after a stop): it belongs to the new raid now.</summary>
        public void Attach(long botId, string eventId)
        {
            lock (_sync)
                if (_guests.TryGetValue(botId, out Guest guest))
                {
                    guest.EventId = eventId;
                    guest.State = GuestState.Live;
                    guest.Reserved = true;
                    guest.EverLive = true;
                    guest.LogoutAt = 0;
                }
        }

        /// <summary>The raid ended (any reason): queued guests are not logged in; the others log out after the delay.</summary>
        public void OnRaidEnded(string eventId, bool started, long now)
        {
            lock (_sync)
            {
                _events.Remove(eventId);
                foreach (Guest guest in _guests.Values.Where(g => g.EventId == eventId).ToArray())
                {
                    if (guest.State == GuestState.Queued)
                    {
                        _guests.TryRemove(guest.BotId, out _);
                        continue;
                    }
                    if (guest.State == GuestState.LoggingOut) continue;
                    guest.EventId = null;
                    if (guest.State == GuestState.Live) guest.Since = now;
                    guest.State = GuestState.Lingering;
                    guest.LogoutAt = now + RallyAnswerPolicy.LogoutDelay(started, _random.NextDouble());
                }
            }
        }

        /// <summary>The raid went into battle: no more top-ups for it.</summary>
        public void OnRaidStarted(string eventId)
        {
            lock (_sync) _events.Remove(eventId);
        }

        private enum Act : byte { Login, Reserve, Logout }

        /// <summary>Logs guests in (about 5 a second), hands live ones to their raid, tops up failed logins while the raid
        /// assembles, and logs lingering guests out. Backend calls happen outside the book's lock.</summary>
        public void Pulse(long now, DateTime nowUtc)
        {
            var acts = new List<(Act Act, Guest Guest, string EventId)>();
            lock (_sync)
            {
                Interlocked.Exchange(ref _clock, now);
                if (_lastPulse > 0)
                    _tokens = Math.Min(RallyAnswerPolicy.LoginsPerSecond, _tokens + (now - _lastPulse) * RallyAnswerPolicy.LoginsPerSecond / 1000.0);
                _lastPulse = now;
                if (_guests.IsEmpty && _events.Count == 0) return;
                int logins = (int)_tokens;
                foreach (Guest guest in _guests.Values.OrderBy(g => g.Since).ToArray())
                {
                    bool live = guest.State != GuestState.Queued && _backend.IsLive(guest.BotId);
                    if (live) guest.EverLive = true;
                    switch (guest.State)
                    {
                        case GuestState.Queued:
                            if (logins > 0) { logins--; acts.Add((Act.Login, guest, null)); }
                            break;
                        case GuestState.LoggingIn:
                            if (live) { guest.State = GuestState.Live; guest.Since = now; goto case GuestState.Live; }
                            if (now - guest.Since > RallyAnswerPolicy.LoginTimeoutMilliseconds && !_backend.IsLoggingIn(guest.BotId))
                                Drop(guest, now);
                            break;
                        case GuestState.Live:
                            if (!live) { _guests.TryRemove(guest.BotId, out _); break; }
                            if (!guest.Reserved && guest.EventId != null && now >= guest.NextReserveTry)
                            {
                                guest.NextReserveTry = now + RallyAnswerPolicy.ReserveRetryMilliseconds;
                                acts.Add((Act.Reserve, guest, guest.EventId));
                            }
                            break;
                        case GuestState.Lingering:
                            if (!live)
                            {
                                // a guest whose login was still on its way when the raid ended waits for it
                                if (guest.EverLive || now - guest.Since > RallyAnswerPolicy.LoginTimeoutMilliseconds)
                                    _guests.TryRemove(guest.BotId, out _);
                                break;
                            }
                            if (now >= guest.LogoutAt) acts.Add((Act.Logout, guest, null));
                            break;
                        case GuestState.LoggingOut:
                            if (!live && guest.EverLive) _guests.TryRemove(guest.BotId, out _);
                            else if (now - guest.Since > 30 * 60_000L) { guest.State = GuestState.Lingering; guest.LogoutAt = now; }
                            break;
                    }
                }
                // top-ups: logins that failed are replaced from the pool while the raid still assembles
                foreach (RallyEvent rally in _events.Values)
                {
                    int have = _guests.Values.Count(g => g.EventId == rally.Id);
                    int missing = Math.Min(rally.Wanted - have, RoomFor(rally.Realm));
                    if (missing > 0) QueueLocked(rally, missing, now, nowUtc);
                }
            }
            foreach (var (act, guest, eventId) in acts)
            {
                bool ok = act switch
                {
                    Act.Login => SafeCall(() => _backend.RequestLogin(guest.BotId)),
                    Act.Reserve => SafeCall(() => _backend.Reserve(eventId, guest.BotId)),
                    _ => SafeCall(() => _backend.RequestLogout(guest.BotId)),
                };
                lock (_sync)
                {
                    if (!_guests.TryGetValue(guest.BotId, out Guest current) || current != guest) continue;
                    switch (act)
                    {
                        case Act.Login:
                            _tokens = Math.Max(0, _tokens - 1);
                            if (ok) { guest.State = GuestState.LoggingIn; guest.Since = now; }
                            else if (++guest.Failures >= RallyAnswerPolicy.LoginAttempts) Drop(guest, now);
                            break;
                        case Act.Reserve:
                            if (ok && guest.EventId == eventId) guest.Reserved = true;
                            break;
                        case Act.Logout:
                            if (ok) { guest.State = GuestState.LoggingOut; guest.Since = now; }
                            else guest.LogoutAt = now + RallyAnswerPolicy.LogoutRetryMilliseconds;
                            break;
                    }
                }
            }
        }

        private static bool SafeCall(Func<bool> call)
        {
            try { return call(); }
            catch { return false; }
        }
    }

    /// <summary>
    /// The Horn of Rallying's answer pool (plan 3.7, owner 6b item 8): archived level-50 bots of each realm, kept in memory
    /// and refreshed every minute in the background, so sounding the horn never scans a table. Guests log in through the
    /// lifecycle's Rally lane, travel to the muster like any recruit, and log out after the raid. They do not count
    /// toward the realm goal (<see cref="IsRallyGuest"/>). With population_scheduler_enabled false the horn is live-only,
    /// exactly as before.
    /// </summary>
    public static class RallyGuests165
    {
        private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(RallyGuests165));
        private static readonly RallyGuestBook Book = new(new CoreRallyBackend());
        private static readonly ConcurrentDictionary<string, long> Cooldowns = new();
        private const long PulseMilliseconds = 200;
        private const long PoolRefreshMilliseconds = 60_000;
        private static long _nextPulse;
        private static long _nextPoolRefresh;
        private static int _refreshing;
        private static long _settingsCheckedTick = long.MinValue;
        private static bool _hornCallsArchived = true;

        /// <summary>The new horn rules (partial start, guests, cooldown) are on: population_scheduler_enabled.</summary>
        public static bool NewRulesOn => CoreRallyBackend.SchedulerEnabled;

        /// <summary>The horn may call archived 50s now: new rules, the launcher's option, and the archive exists.</summary>
        public static bool ArchiveAllowed => NewRulesOn && HornCallsArchived() && Book.Backend.ArchiveAvailable;

        /// <summary>For the scheduler: a horn guest does not count toward the realm goal.</summary>
        public static bool IsRallyGuest(long botId) => Book.IsGuest(botId);

        public static int GuestCount(eRealm realm) => Book.GuestCount(realm);
        public static int TotalGuests => Book.TotalGuests;
        public static int ArchivedFree(eRealm realm) => ArchiveAllowed ? Book.ArchivedFree(realm) : 0;
        public static int Pending(string eventId) => Book.Pending(eventId);

        /// <summary>Milliseconds before this player may sound a horn that logs guests in again (0 = may).</summary>
        public static long CooldownRemaining(GamePlayer player, long now) => player == null ? 0 : CooldownRemaining(CooldownKey(player), now);

        public static long CooldownRemaining(string key, long now) =>
            key != null && Cooldowns.TryGetValue(key, out long until) ? Math.Max(0, until - now) : 0;

        // the character, so relogging does not reset it (memory only: a server restart does)
        private static string CooldownKey(GamePlayer player) => player.InternalID ?? player.Name ?? string.Empty;

        /// <summary>A sound that logged guests in starts the player's 30 minutes; stopping the raid does not refund it.</summary>
        public static void NoteSound(GamePlayer player, long now)
        {
            if (player != null) NoteSound(CooldownKey(player), now);
        }

        public static void NoteSound(string key, long now)
        {
            if (key == null) return;
            Cooldowns[key] = now + RallyAnswerPolicy.CooldownMilliseconds;
            if (Cooldowns.Count > 256)
                foreach (var old in Cooldowns.Where(p => p.Value <= now).ToArray()) Cooldowns.TryRemove(old.Key, out _);
        }

        /// <summary>Whether this player's sound may log guests in now.</summary>
        public static bool MayCallArchived(GamePlayer player, long now) => ArchiveAllowed && CooldownRemaining(player, now) == 0;

        public static int Summon(string eventId, eRealm realm, int count, string sounderGuildId, ushort hubRegion, Vector3 hub, long now)
        {
            int queued = Book.Summon(eventId, realm, count, sounderGuildId, hubRegion, hub, now, DateTime.UtcNow);
            Log.Info($"RALLY_GUESTS_SUMMON event={eventId} realm={realm} wanted={count} queued={queued} realmGuests={Book.GuestCount(realm)} total={Book.TotalGuests}");
            return queued;
        }

        public static void Attach(long botId, string eventId) => Book.Attach(botId, eventId);

        public static void OnRaidEnded(string eventId, bool started, long now) => Book.OnRaidEnded(eventId, started, now);

        public static void OnRaidStarted(string eventId) => Book.OnRaidStarted(eventId);

        /// <summary>Called every coordinator tick; runs every 200 ms (logins at about 5 a second) and refreshes the pools
        /// once a minute on a background thread.</summary>
        public static void Pulse(long now)
        {
            if (now < Interlocked.Read(ref _nextPulse)) return;
            Interlocked.Exchange(ref _nextPulse, now + PulseMilliseconds);
            try
            {
                Book.Pulse(now, DateTime.UtcNow);
            }
            catch (Exception exception)
            {
                Log.Error("RALLY_GUESTS_PULSE failed", exception);
            }
            if (now < _nextPoolRefresh || !NewRulesOn || !Book.Backend.ArchiveAvailable) return;
            _nextPoolRefresh = now + PoolRefreshMilliseconds;
            if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0) return;
            Task.Run(() =>
            {
                try
                {
                    foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
                        Book.SetPool(realm, Book.Backend.LoadPool(realm, RallyAnswerPolicy.PoolLimit));
                }
                catch (Exception exception)
                {
                    Log.Warn($"RALLY_GUESTS_POOL refresh failed: {exception.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _refreshing, 0);
                }
            });
        }

        // The launcher's "Horn may call archived level 50s" (population-goals.json), re-read at most every 30 s.
        private static bool HornCallsArchived()
        {
            long now = Environment.TickCount64;
            if (now - Interlocked.Read(ref _settingsCheckedTick) < 30_000) return _hornCallsArchived;
            Interlocked.Exchange(ref _settingsCheckedTick, now);
            try
            {
                string dir = Classic165Mode.StateDirectory(AppContext.BaseDirectory, Classic165Mode.IsOn);
                if (PopulationGoalSettings.TryLoad(dir, out PopulationGoalSettings settings, out _))
                    _hornCallsArchived = settings.HornCallsArchived;
            }
            catch
            {
                // keep the last good value
            }
            return _hornCallsArchived;
        }

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
        {
            CoreRallyBackend.SubscribeLoggedIn(AutonomousBotGuilds.AnnounceLogin);
            Log.Info($"RALLY_GUESTS ready newRules={NewRulesOn} archive={Book.Backend.ArchiveAvailable}");
        }
    }

    /// <summary>Thin adapter to CORE's AutonomousBotLifecycle / BotArchiveStore and the scheduler's server property.</summary>
    public sealed class CoreRallyBackend : IRallyGuestBackend
    {
        private static readonly ConcurrentDictionary<long, (ushort Region, int X, int Y)> Binds = new();

        public static bool SchedulerEnabled => PopulationSchedulerProperties.POPULATION_SCHEDULER_ENABLED;

        /// <summary>The archive is open and the migration done (CORE); until then the horn calls live 50s only.</summary>
        public bool ArchiveAvailable => SchedulerEnabled && AutonomousBotLifecycle.IsReady && BotArchiveStore.IsReady;

        public static void SubscribeLoggedIn(Action<GameBot> handler)
        {
            AutonomousBotLifecycle.LoggedIn -= handler;
            AutonomousBotLifecycle.LoggedIn += handler;
        }

        public IReadOnlyList<RallyPoolEntry> LoadPool(eRealm realm, int limit)
        {
            if (!ArchiveAvailable) return Array.Empty<RallyPoolEntry>();
            var list = new List<RallyPoolEntry>();
            foreach (ArchiveCandidate row in BotArchiveStore.Candidates(realm, 50, 50, limit, DateTime.MaxValue) ?? Array.Empty<ArchiveCandidate>())
            {
                if (row == null || row.Level != 50 || HiredCompanions165.IsHired(row.BotId)) continue;
                list.Add(new RallyPoolEntry(row.BotId, realm, row.LastSeenUtc, SteadyPersona(row.Profile?.Persona)));
            }
            FillBinds(list);
            return list.Select(e => Binds.TryGetValue(e.BotId, out var bind) && bind.Region != 0
                ? e with { BindRegion = bind.Region, BindX = bind.X, BindY = bind.Y } : e).ToArray();
        }

        /// <summary>Veteran (0) or regular (1), in the plan's persona order (3.3).</summary>
        public static bool SteadyPersona(byte? persona) => persona is 0 or 1;

        // Warm bots keep their bind on the roster row; cold-archived ones have none (no bind score). Looked up once per bot.
        private static void FillBinds(List<RallyPoolEntry> entries)
        {
            if (Binds.Count > 20_000) Binds.Clear();
            long[] unknown = entries.Select(e => e.BotId).Where(id => !Binds.ContainsKey(id)).ToArray();
            foreach (long[] chunk in unknown.Chunk(400))
            {
                try
                {
                    foreach (OfflineWorldBotRecord record in DOLDB<OfflineWorldBotRecord>.SelectObjects(DB.Column("BotId").IsIn(chunk)))
                        Binds[record.BotId] = ((ushort)record.BindRegionId, record.BindX, record.BindY);
                }
                catch
                {
                    return;
                }
                foreach (long id in chunk) Binds.TryAdd(id, (0, 0, 0));
            }
        }

        // No placement: CORE logs the guest in at its saved spot, or its bind / home town when that spot is in the
        // frontier, a dungeon or invalid; it then walks to the muster like any recruit.
        public bool RequestLogin(long botId) => ArchiveAvailable && AutonomousBotLifecycle.RequestLogin(botId, BotLoginPriority.Rally);

        public bool IsLoggingIn(long botId) => AutonomousBotLifecycle.IsLoggingIn(botId);

        public bool RequestLogout(long botId) =>
            AutonomousBotRegistry.TryGet(botId, out GameBot bot) && !HiredCompanions165.IsHired(bot) &&
            AutonomousBotLifecycle.RequestLogout(bot, BotLogoutReason.Rally);

        // the registry keeps a bot across region moves (a brief remove/add), so a guest is not lost on a zone change
        public bool IsLive(long botId) => AutonomousBotRegistry.Contains(botId);

        public bool Reserve(string eventId, long botId) =>
            AutonomousBotRegistry.TryGet(botId, out GameBot bot) && AutonomousRealmRaid.ReserveGuest(eventId, bot);

        public string GuildIdOf(long botId) => AutonomousBotGuilds.GuildIdOf(botId);
    }
}
