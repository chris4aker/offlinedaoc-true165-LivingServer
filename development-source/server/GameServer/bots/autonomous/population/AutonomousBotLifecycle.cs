using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using DOL.Database;
using DOL.Logging;

namespace DOL.GS
{
    public enum BotLogoutReason : byte { SessionOver, Rebalance, GoalLowered, Rally, Retire, Shutdown }

    public enum BotLoginPriority : byte { Normal, Returning, Rally }

    /// <summary>
    /// Logging autonomous gamebots in and out like players (plan 3.2).
    /// - Logout: only through <see cref="CanLogOutNow"/> (alive, out of combat 15-20 s, no human group, no raid / RvR /
    ///   battleground, no horse route, no errand). About 70% walk to their bind point and sit 10-30 s; the rest log off
    ///   where they stand once no player can see them. An all-bot group leaves one member at a time. Never forced: a
    ///   logout that can't happen within 30 minutes is dropped (<see cref="LogoutRefused"/>).
    /// - Login: read fresh by id on a background thread, then re-checked on the game loop under the Exchange and economy
    ///   locks (an offline sale or returned listing is never lost), and placed at its saved spot, or its bind or capital
    ///   when that spot is in the frontier, a dungeon or invalid.
    /// - At most 2 logins and 2 logouts a second in total; Rally logins go first.
    /// Hired companions are never touched.
    /// </summary>
    public static class AutonomousBotLifecycle
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousBotLifecycle));
        public const int MaxLoginsPerSecond = 2;
        public const int MaxLogoutsPerSecond = 2;
        public static readonly TimeSpan LogoutGiveUpAfter = TimeSpan.FromMinutes(30);
        private const int QuietCombatMilliseconds = 15_000;
        private const int TickMilliseconds = 250;

        public static event Action<GameBot> LoggedIn;
        public static event Action<long, eRealm, BotLogoutReason> LoggedOut;
        public static event Action<long, eRealm, int> LevelChanged;
        /// <summary>A requested logout was dropped (the bot never became free to leave within 30 minutes).</summary>
        public static event Action<long> LogoutRefused;

        private static volatile bool _running;
        private static volatile bool _shuttingDown;
        private static System.Threading.Timer _timer;
        private static int _ticking;
        private static long[] _onlineAtLastStop = Array.Empty<long>();
        private static long _nextLoginTick;
        private static long _nextLogoutTick;
        private static long _nextFlushTick, _nextRetireTick, _nextArchiveTick, _nextSweepTick;
        private static int _loginGeneration;

        // ---- state

        private sealed class LoginRequest
        {
            public long BotId;
            public BotLoginPriority Priority;
            public Vector3? Placement;
            public ushort Region;
            public eRealm Realm;
            public int Attempts;
        }

        private enum LogoutPhase { Walk, Sit, Vanish, Leave, Leaving }

        private sealed class LogoutIntent
        {
            public GameBot Bot;
            public BotLogoutReason Reason;
            public LogoutPhase Phase;
            public Vector3 Target;
            public long PhaseStartTick;
            public long SitUntilTick;
            public DateTime GiveUpUtc;
        }

        private static readonly ConcurrentDictionary<long, LoginRequest> Logins = new();
        private static readonly ConcurrentQueue<long>[] LoginLanes = [new(), new(), new()];   // Normal, Returning, Rally
        private static readonly ConcurrentDictionary<long, LogoutIntent> Logouts = new();
        private static readonly ConcurrentDictionary<Group, long> GroupNextDeparture = new();

        /// <summary>True once the archive is ready and the migration of the existing bots has finished.</summary>
        public static bool IsReady => _running && !_shuttingDown && BotArchiveStore.IsReady;

        /// <summary>False while the server starts up or shuts down: the scheduler then makes no decisions.</summary>
        public static bool WorldOpen => IsReady;

        /// <summary>Bots that were online when the server last stopped (they log in first, like reconnecting players).</summary>
        public static IReadOnlyCollection<long> OnlineAtLastStop => _onlineAtLastStop;

        public static int PendingLogins => Logins.Count;
        public static int PendingLogouts => Logouts.Count;
        public static int PendingLoginCount(eRealm realm) => Logins.Values.Count(r => r.Realm == realm);
        public static int PendingLogoutCount(eRealm realm) => Logouts.Values.Count(i => i.Bot?.Realm == realm);
        public static bool IsLoggingIn(long botId) => Logins.ContainsKey(botId);
        public static bool IsLoggingOut(long botId) => Logouts.ContainsKey(botId) || AutonomousBotStatusPersistence.IsLogoutPending(botId);
        public static bool IsOnline(long botId) => AutonomousBotRegistry.TryGet(botId, out GameBot bot) && bot.ObjectState == GameObject.eObjectState.Active;

        // ---- server start / stop (called by AutonomousPopulationController)

        internal static void Start(IEnumerable<long> onlineAtLastStop)
        {
            _onlineAtLastStop = onlineAtLastStop?.ToArray() ?? Array.Empty<long>();
            _shuttingDown = false;
            Interlocked.Increment(ref _loginGeneration);
            Logins.Clear();
            foreach (ConcurrentQueue<long> lane in LoginLanes)
                lane.Clear();
            Logouts.Clear();
            AutonomousBotStatusPersistence.LogoutSaved -= OnLogoutSaved;
            AutonomousBotStatusPersistence.LogoutSaved += OnLogoutSaved;
            _running = true;
            BotPopulationMaintenance.StartInBackground(_onlineAtLastStop);
            _timer?.Dispose();
            _timer = new System.Threading.Timer(Tick, null, 1_000, TickMilliseconds);
        }

        internal static void Stop()
        {
            _shuttingDown = true;
            Interlocked.Increment(ref _loginGeneration);
            _timer?.Dispose();
            _timer = null;
            try
            {
                BotArchiveStore.Current?.FlushQueued(int.MaxValue);
            }
            catch (Exception exception)
            {
                PopulationLog.Warn(Log, $"BOT_ARCHIVE final flush failed: {exception.Message}");
            }
            _running = false;
        }

        /// <summary>
        /// Whether a bot leaving the world saves at once instead of through the queued logout save: hired companions, the
        /// server shutting down, a lifecycle that isn't running (tests), or a record not stored yet.
        /// </summary>
        public static bool SaveSynchronouslyOnRemoval(GameBot bot) =>
            !_running || _shuttingDown || HiredCompanions165.IsHired(bot) || bot?.PersistentRecord?.IsPersisted != true ||
            GameServer.Instance == null || GameServer.Instance.ServerStatus != EGameServerStatus.GSS_Open;

        // ---- counts

        public static int LiveCount(eRealm realm) =>
            AutonomousBotRegistry.Snapshot().Count(bot => bot.Realm == realm && IsRosterBot(bot));

        public static int LiveCount(eRealm realm, int minLevel, int maxLevel) =>
            AutonomousBotRegistry.Snapshot().Count(bot => bot.Realm == realm && bot.Level >= minLevel && bot.Level <= maxLevel && IsRosterBot(bot));

        public static IReadOnlyCollection<GameBot> LiveBots(eRealm realm) =>
            AutonomousBotRegistry.Snapshot().Where(bot => bot.Realm == realm && IsRosterBot(bot)).ToArray();

        private static bool IsRosterBot(GameBot bot) =>
            bot?.ObjectState == GameObject.eObjectState.Active && bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper &&
            !HiredCompanions165.IsHired(bot);

        // ---- the logout gate

        public static bool CanLogOutNow(GameBot bot, out string why) => Gate(bot, waitForGroup: false, out why);

        /// <summary>
        /// The gate. With waitForGroup, a bot whose only blocker is its bot group (or the Group PvE task it is on) passes:
        /// a requested logout then waits until that group ends (Bot Goals objectives are never broken up).
        /// </summary>
        private static bool Gate(GameBot bot, bool waitForGroup, out string why)
        {
            why = null;
            if (bot == null || !bot.IsAutonomousWorldBot || bot.DatabaseID <= 0 || bot.PersistentRecord == null)
                why = "not an autonomous gamebot";
            else if (HiredCompanions165.IsHired(bot) || bot.IsTemporaryGroupHelper)
                why = "a hired companion stays with its player";
            else if (bot.ObjectState != GameObject.eObjectState.Active)
                why = "not in the world";
            else if (!bot.IsAlive)
                why = "dead";
            else if (bot.InCombat || bot.InCombatInLast(QuietCombatMilliseconds + (int)(bot.DatabaseID % 5_000)))
                why = "in combat or just out of it";
            else if (bot.IsPlayerLedGroup || bot.Group?.GetMembersInTheGroup().Any(member => member is GamePlayer) == true)
                why = "grouped with a player";
            else if (AutonomousRealmRaid.IsReserved(bot) || AutonomousRealmRaid.IsInRally(bot) ||
                     bot.Group != null && AutonomousRealmRaid.GetView(bot.Group) != null)
                why = "in a realm raid";
            else if (bot.PersistentRecord.ObjectiveKind is nameof(eAutonomousObjectiveKind.RvR) or nameof(eAutonomousObjectiveKind.Battleground))
                why = "on an RvR or battleground task";
            else if (!waitForGroup && bot.Group != null)
                why = "in a group: logs off when the group ends";
            else if (bot.CurrentRegion?.IsFrontier == true || bot.CurrentRegion?.IsRvR == true)
                why = "in the frontier (siege, keep defence or relic run)";
            else if (bot.TempProperties.GetProperty<GameRelic>(GameRelic.PLAYER_CARRY_RELIC_WEAK) != null)
                why = "carrying a relic";
            else if (bot.IsOnStableMasterRoute || bot.IsOnHorse)
                why = "riding a horse route";
            else if (AutonomousWorldBotController.ShoppingPlanFor(bot) != null)
                why = "on an errand (merchant, repair or purchase)";
            else if (bot.IsCasting)
                why = "casting";
            return why == null;
        }

        // ---- logout

        public static bool RequestLogout(GameBot bot, BotLogoutReason reason)
        {
            if (!_running || _shuttingDown || !Gate(bot, waitForGroup: true, out _))
                return false;
            long now = GameLoop.GameLoopTime;
            var intent = new LogoutIntent
            {
                Bot = bot,
                Reason = reason,
                PhaseStartTick = now,
                GiveUpUtc = DateTime.UtcNow + LogoutGiveUpAfter,
            };
            if (reason != BotLogoutReason.Shutdown && TryBindTarget(bot, out Vector3 target) &&
                (reason == BotLogoutReason.Rally || Random.Shared.NextDouble() < 0.70))
            {
                intent.Phase = LogoutPhase.Walk;
                intent.Target = target;
            }
            else
                intent.Phase = reason == BotLogoutReason.Shutdown ? LogoutPhase.Leave : LogoutPhase.Vanish;
            return Logouts.TryAdd(bot.DatabaseID, intent) || Logouts.ContainsKey(bot.DatabaseID);
        }

        /// <summary>Its bind point, when it is in the same (non-frontier) region and within walking reach.</summary>
        private static bool TryBindTarget(GameBot bot, out Vector3 target)
        {
            target = default;
            OfflineWorldBotRecord record = bot.PersistentRecord;
            if (record == null || record.BindRegionId != bot.CurrentRegionID || bot.CurrentRegion?.IsFrontier == true)
                return false;
            target = new Vector3(record.BindX, record.BindY, record.BindZ);
            float distance = Vector2.Distance(new Vector2(bot.X, bot.Y), new Vector2(target.X, target.Y));
            return distance <= 8_000;
        }

        /// <summary>
        /// Called by BotBrain.Think (possibly in parallel, one bot each). True while the logout owns the bot's turn
        /// (walking home, sitting); false lets the normal AI run (combat, or waiting unseen to vanish).
        /// </summary>
        public static bool DriveLogout(GameBot bot)
        {
            if (bot == null || Logouts.IsEmpty || !Logouts.TryGetValue(bot.DatabaseID, out LogoutIntent intent) || !ReferenceEquals(intent.Bot, bot))
                return false;
            long now = GameLoop.GameLoopTime;
            if (!CanLogOutNow(bot, out _))
            {
                // A grouped bot logs off when its group ends, however long that takes.
                if (bot.Group != null && Gate(bot, waitForGroup: true, out _))
                    intent.GiveUpUtc = DateTime.UtcNow + LogoutGiveUpAfter;
                if (DateTime.UtcNow >= intent.GiveUpUtc)
                    DropLogout(bot.DatabaseID, intent);
                else if (intent.Phase is LogoutPhase.Sit or LogoutPhase.Leave)
                    intent.Phase = LogoutPhase.Vanish;   // interrupted: try again later, quietly
                return false;
            }

            switch (intent.Phase)
            {
                case LogoutPhase.Walk:
                {
                    float distance = Vector2.Distance(new Vector2(bot.X, bot.Y), new Vector2(intent.Target.X, intent.Target.Y));
                    if (distance <= 250)
                    {
                        bot.StopMoving();
                        bot.BeginRecoveryRest();   // sits down in True 1.65 Mode
                        intent.Phase = LogoutPhase.Sit;
                        intent.SitUntilTick = now + Random.Shared.Next(10_000, 30_001);
                    }
                    else if (now - intent.PhaseStartTick > 5 * 60_000)
                    {
                        intent.Phase = LogoutPhase.Vanish;   // the walk takes too long: leave quietly instead
                        intent.PhaseStartTick = now;
                        return false;
                    }
                    else if (!bot.IsMoving)
                        bot.PathTo(intent.Target, bot.MaxSpeed);
                    if (bot.PersistentRecord != null)
                        bot.PersistentRecord.Activity = "Heading home to log out";
                    return true;
                }
                case LogoutPhase.Sit:
                    if (now >= intent.SitUntilTick)
                        intent.Phase = LogoutPhase.Leave;
                    return true;
                case LogoutPhase.Vanish:
                    if (bot.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE).Count > 0)
                        return false;   // keeps playing until nobody can see it
                    intent.Phase = LogoutPhase.Leave;
                    goto case LogoutPhase.Leave;
                case LogoutPhase.Leave:
                    if (bot.Group is { } group && GroupNextDeparture.TryGetValue(group, out long next) && now < next)
                        return true;
                    if (!TryTakeSlot(ref _nextLogoutTick, now, 1_000 / MaxLogoutsPerSecond))
                        return true;
                    intent.Phase = LogoutPhase.Leaving;
                    if (bot.Group is { } leavingGroup)
                        GroupNextDeparture[leavingGroup] = now + Random.Shared.Next(20_000, 75_001);
                    NpcService.Instance.Post(FinishLogout, bot);
                    return true;
                default:
                    return true;
            }
        }

        private static bool TryTakeSlot(ref long nextTick, long now, int intervalMs)
        {
            long next = Volatile.Read(ref nextTick);
            return now >= next && Interlocked.CompareExchange(ref nextTick, now + intervalMs, next) == next;
        }

        private static void FinishLogout(GameBot bot)
        {
            if (!Logouts.TryGetValue(bot.DatabaseID, out LogoutIntent intent) || !ReferenceEquals(intent.Bot, bot))
                return;
            try
            {
                if (!CanLogOutNow(bot, out _))
                {
                    intent.Phase = LogoutPhase.Vanish;
                    return;
                }
                if (bot.PersistentRecord != null)
                    bot.PersistentRecord.CurrentGoal = string.Empty;
                bot.Delete();   // one queued logout save (GameBot.RemoveFromWorld)
            }
            catch (Exception exception)
            {
                PopulationLog.Error(Log, $"BOT_LOGOUT_FAILED bot={bot.Name} id={bot.DatabaseID}", exception);
                DropLogout(bot.DatabaseID, intent);
            }
        }

        private static void DropLogout(long botId, LogoutIntent intent)
        {
            if (!Logouts.TryRemove(new KeyValuePair<long, LogoutIntent>(botId, intent)))
                return;
            if (intent.Bot?.IsRecoveryResting == true)
                intent.Bot.WakeRecoveryRest();
            PopulationLog.Info(Log, $"BOT_LOGOUT_DROPPED bot={intent.Bot?.Name} id={botId} reason={intent.Reason}: not free to leave in time");
            try
            {
                LogoutRefused?.Invoke(botId);
            }
            catch (Exception exception)
            {
                PopulationLog.Warn(Log, $"LogoutRefused handler failed: {exception.Message}");
            }
        }

        /// <summary>GameBot.RemoveFromWorld: the bot left the world (any path).</summary>
        internal static void NoteRemovedFromWorld(GameBot bot)
        {
            if (bot != null)
                AutonomousBotDyes.Forget(bot.DatabaseID);   // per-bot dye rest leaves with the bot (owner 2026-11-10)
            if (bot == null || HiredCompanions165.IsHired(bot) || bot.IsTemporaryGroupHelper)
                return;
            BotLogoutReason reason = Logouts.TryRemove(bot.DatabaseID, out LogoutIntent intent) ? intent.Reason
                : _shuttingDown ? BotLogoutReason.Shutdown : BotLogoutReason.SessionOver;
            if (!_running || _shuttingDown)
                return;
            BotArchiveStore.Current?.QueueOnline(bot.DatabaseID, false, bot.Level, DateTime.UtcNow);
            try
            {
                LoggedOut?.Invoke(bot.DatabaseID, bot.Realm, reason);
            }
            catch (Exception exception)
            {
                PopulationLog.Warn(Log, $"LoggedOut handler failed: {exception.Message}");
            }
        }

        private static void OnLogoutSaved(GameBot bot)
        {
            // The directory's online flag was cleared at removal; only the last-seen time is refreshed here.
            if (_running && bot != null && !HiredCompanions165.IsHired(bot))
                BotArchiveStore.Current?.QueueOnline(bot.DatabaseID, false, bot.Level, DateTime.UtcNow);
        }

        /// <summary>GameBot level-up.</summary>
        internal static void NoteLevelChanged(GameBot bot)
        {
            if (bot == null || !bot.IsAutonomousWorldBot || HiredCompanions165.IsHired(bot) || bot.IsTemporaryGroupHelper)
                return;
            if (_running)
                BotArchiveStore.Current?.QueueLevel(bot.DatabaseID, bot.Level);
            try
            {
                LevelChanged?.Invoke(bot.DatabaseID, bot.Realm, bot.Level);
            }
            catch (Exception exception)
            {
                PopulationLog.Warn(Log, $"LevelChanged handler failed: {exception.Message}");
            }
        }

        // ---- login

        public static bool RequestLogin(long botId, BotLoginPriority priority, Vector3? placement = null, ushort region = 0)
        {
            if (!IsReady || botId <= 0 || HiredCompanions165.IsHired(botId) || AutonomousBotRegistry.Contains(botId) ||
                !AutonomousPopulationController.PopulationEnabled)
                return false;
            var request = new LoginRequest { BotId = botId, Priority = priority, Placement = placement, Region = region };
            if (!Logins.TryAdd(botId, request))
                return true;   // already on its way
            LoginLanes[(int)priority].Enqueue(botId);
            return true;
        }

        private static void Tick(object state)
        {
            if (Interlocked.Exchange(ref _ticking, 1) != 0)
                return;
            try
            {
                long now = Environment.TickCount64;
                BotArchive archive = BotArchiveStore.Current;
                if (archive != null && now >= _nextFlushTick)
                {
                    _nextFlushTick = now + 2_000;
                    archive.FlushQueued();
                }
                if (!BotArchiveStore.IsReady || _shuttingDown)
                    return;
                if (now >= _nextRetireTick)
                {
                    _nextRetireTick = now + 1_000;
                    BotArchiveStore.ProcessRetireQueue(16);
                }
                ProcessOneLogin(now);
                if (now >= _nextSweepTick)
                {
                    _nextSweepTick = now + 30_000;
                    SweepStaleLogouts();
                }
                if (now >= _nextArchiveTick)
                {
                    _nextArchiveTick = now + 30_000;
                    if (AutonomousPopulationController.SchedulerEnabled)
                        ArchiveIdleWarm(archive);
                    else
                    {
                        // The old behaviour logs in the whole warm roster: bring archived bots back a few at a time.
                        foreach (long botId in archive.IdsInState(BotArchiveState.Cold, 4))
                            archive.RestoreToWarm(botId);
                    }
                }
            }
            catch (Exception exception)
            {
                PopulationLog.Error(Log, "Bot lifecycle tick failed", exception);
            }
            finally
            {
                Volatile.Write(ref _ticking, 0);
            }
        }

        /// <summary>At most one login per 500 ms (2 a second); Rally first, then returning players, then the rest.</summary>
        private static void ProcessOneLogin(long now)
        {
            if (!TryTakeSlot(ref _nextLoginTick, now, 1_000 / MaxLoginsPerSecond))
                return;
            for (int lane = LoginLanes.Length - 1; lane >= 0; lane--)
            {
                while (LoginLanes[lane].TryDequeue(out long botId))
                {
                    if (!Logins.TryGetValue(botId, out LoginRequest request))
                        continue;
                    PrepareScheduledLogin(request);
                    return;
                }
            }
            Volatile.Write(ref _nextLoginTick, 0);   // nothing waited: the slot is not used up
        }

        private static void PrepareScheduledLogin(LoginRequest request)
        {
            long botId = request.BotId;
            try
            {
                if (AutonomousBotRegistry.Contains(botId) || HiredCompanions165.IsHired(botId))
                {
                    Logins.TryRemove(botId, out _);
                    return;
                }
                // A logout save still waiting, or a cold bot whose restore found the locks busy: try again shortly.
                BotArchive archive = BotArchiveStore.Current;
                bool retry = AutonomousBotStatusPersistence.IsLogoutPending(botId) ||
                             archive != null && archive.IsCold(botId) && !archive.RestoreToWarm(botId);
                if (retry)
                {
                    if (++request.Attempts > 120)
                    {
                        Logins.TryRemove(botId, out _);
                        PopulationLog.Warn(Log, $"BOT_LOGIN_DROPPED id={botId}: still busy or archived after {request.Attempts} tries");
                        return;
                    }
                    LoginLanes[(int)request.Priority].Enqueue(botId);
                    return;
                }

                PreparedLogin prepared = PrepareLogin(botId, scheduled: true, request.Placement, request.Region, request.Priority);
                if (prepared == null)
                {
                    Logins.TryRemove(botId, out _);
                    return;
                }
                request.Realm = (eRealm)prepared.Realm;
                NpcService.Instance.Post(SpawnScheduled, prepared);
            }
            catch (Exception exception)
            {
                Logins.TryRemove(botId, out _);
                PopulationLog.Error(Log, $"BOT_LOGIN_PREPARE_FAILED id={botId}", exception);
            }
        }

        private static void SpawnScheduled(PreparedLogin prepared)
        {
            try
            {
                SpawnOnGameLoop(prepared);
            }
            finally
            {
                Logins.TryRemove(prepared.BotId, out _);
            }
        }

        /// <summary>What a login read on a background thread: the bot's revision and items at that moment.</summary>
        public sealed record PreparedLogin(long BotId, int Realm, int Revision, IList Items, int Generation, bool Scheduled,
            Vector3? Placement, ushort Region, BotLoginPriority Priority = BotLoginPriority.Normal);

        /// <summary>Fresh primary-key read of a bot and its items. Background thread only. Null if it can't log in.</summary>
        public static PreparedLogin PrepareLogin(long botId, bool scheduled, Vector3? placement = null, ushort region = 0,
            BotLoginPriority priority = BotLoginPriority.Normal)
        {
            int generation = Volatile.Read(ref _loginGeneration);
            OfflineWorldBotRecord record = DOLDB<OfflineWorldBotRecord>.SelectObject(DB.Column("BotId").IsEqualTo(botId));
            if (record == null || record.IsRetired)
                return null;
            string ownerId = AutonomousBotEconomy.GetOwnerId(botId);
            IList items = new BotInventory(ownerId).SelectPersonalItems(ownerId);
            return new PreparedLogin(botId, record.Realm, record.InventoryRevision, items, generation, scheduled, placement, region, priority);
        }

        public static bool IsCurrentGeneration(int generation) => generation == Volatile.Read(ref _loginGeneration);

        /// <summary>
        /// Brings a prepared bot into the world. Game loop only. Under the Exchange and economy locks it re-reads the
        /// record by id and reloads the items when an offline write bumped the revision since the background read, then
        /// registers the bot before those locks are released, so no sale or returned listing can slip between.
        /// </summary>
        public static GameBot SpawnOnGameLoop(PreparedLogin prepared)
        {
            long botId = prepared.BotId;
            if (!IsCurrentGeneration(prepared.Generation) && _running)
                return null;
            try
            {
                lock (RealmExchangeBroker.TransactionLock)
                lock (typeof(AutonomousBotEconomy))
                {
                    if (AutonomousBotRegistry.Contains(botId) || AutonomousBotStatusPersistence.IsLogoutPending(botId))
                        return null;
                    OfflineWorldBotRecord record = DOLDB<OfflineWorldBotRecord>.SelectObject(DB.Column("BotId").IsEqualTo(botId));
                    if (record == null || record.IsRetired || HiredCompanions165.IsHired(botId))
                        return null;
                    IList items = prepared.Items;
                    if (record.InventoryRevision != prepared.Revision)
                    {
                        string ownerId = AutonomousBotEconomy.GetOwnerId(botId);
                        items = new BotInventory(ownerId).SelectPersonalItems(ownerId);
                        PopulationLog.Info(Log, $"BOT_LOGIN_RELOAD id={botId}: written offline during login (revision {prepared.Revision} -> {record.InventoryRevision})");
                    }

                    // True 1.65 Mode: a new bot starts as the base class of the class it rolled (BaseClassBots165).
                    bool fresh = AutonomousPopulationController.IsFreshUnplacedLevelOneRecord(record);
                    if (fresh && BaseClassBots165.PrepareFresh(record))
                        GameServer.Database.SaveObject(record);
                    GameBot bot = new(record, items);
                    if (prepared.Placement is { } spot && prepared.Region != 0 && WorldMgr.GetRegion(prepared.Region) != null)
                    {
                        bot.CurrentRegionID = prepared.Region;
                        bot.X = (int)spot.X;
                        bot.Y = (int)spot.Y;
                        bot.Z = (int)spot.Z;
                    }
                    else if (fresh)
                    {
                        if (!AutonomousPopulationController.TryPlaceFreshBotAtAuthoritativeStart(bot, record))
                            AutonomousPopulationController.PlaceAtCapitalFallback(bot, record);
                    }
                    else if (record.RegionId == 0)
                        AutonomousPopulationController.PlaceAtCapitalFallback(bot, record);
                    else if (prepared.Scheduled && prepared.Priority == BotLoginPriority.Rally)
                        PlaceAtBindOrCapital(bot, record);
                    else if (prepared.Scheduled)
                        PlaceAwayFromFrontierAndDungeons(bot, record);

                    // GameNPC keeps a separate interpolated movement position; sync it before validation (see the controller).
                    bot.SynchronizePositionForLoginValidation();
                    AutonomousStuckWatchdog.RepairInvalidLoginPosition(bot);
                    // Living Server: a beggar (bot_living_beggars) always logs in in its own capital.
                    if (AutonomousBeggars.PlaceAtLogin(bot, record))
                        bot.SynchronizePositionForLoginValidation();
                    if (string.IsNullOrWhiteSpace(record.LastSavedUtc) && record.Experience == 0)
                    {
                        bot.Health = bot.MaxHealth;
                        bot.Mana = bot.MaxMana;
                        bot.Endurance = bot.MaxEndurance;
                    }

                    if (!bot.AddToWorld())
                    {
                        PopulationLog.Warn(Log, $"AUTONOMOUS_LOGIN_FAILED bot=\"{record.Name}\" id={botId} level={record.Level} region={bot.CurrentRegionID} " +
                                 $"x={bot.X} y={bot.Y} z={bot.Z} saved={record.RegionId}:{record.X},{record.Y},{record.Z} alive={bot.IsAlive}");
                        record.IsOnline = false;
                        record.Activity = "Login attempt failed; waiting to retry";
                        record.LastUpdateUtc = DateTime.UtcNow.ToString("O");
                        record.Dirty = true;
                        AutonomousBotStatusPersistence.Queue(bot);
                        return null;
                    }

                    record.IsOnline = true;
                    // owner 2026-11-10: errands saved at logout restart their window here (no false between-task timeouts)
                    AutonomousObjectiveAssignments.RestampBetweenTaskWindowAtLogin(record, DateTime.UtcNow);
                    record.Activity = "Choosing first live goal";
                    record.CurrentGoal = "Find a reachable level-appropriate XP camp";
                    record.ObjectiveProgress = prepared.Scheduled ? "Logged in" : "Entered world through staggered login queue";
                    bot.MarkAutonomousStateDirty();
                    AutonomousBotStatusPersistence.QueueLogin(bot);
                    if (_running)
                        BotArchiveStore.Current?.QueueOnline(botId, true, bot.Level, DateTime.UtcNow);
                    PopulationLog.Info(Log, $"Autonomous character {record.Name} entered the live world{(prepared.Scheduled ? " (scheduled login)" : " from the staggered queue")}.");
                    try
                    {
                        LoggedIn?.Invoke(bot);
                    }
                    catch (Exception exception)
                    {
                        PopulationLog.Warn(Log, $"LoggedIn handler failed: {exception.Message}");
                    }
                    return bot;
                }
            }
            catch (Exception exception)
            {
                PopulationLog.Error(Log, $"Could not load autonomous bot {botId}", exception);
                return null;
            }
        }

        /// <summary>A Horn answerer logs in at its bindstone (or its capital), never next to the player, then travels.</summary>
        private static void PlaceAtBindOrCapital(GameBot bot, OfflineWorldBotRecord record)
        {
            Region bind = record.BindRegionId > 0 ? WorldMgr.GetRegion((ushort)record.BindRegionId) : null;
            if (bind != null && !bind.IsFrontier && !bind.IsRvR && !bind.IsDungeon && (record.BindX != 0 || record.BindY != 0))
            {
                bot.CurrentRegionID = (ushort)record.BindRegionId;
                bot.X = record.BindX;
                bot.Y = record.BindY;
                bot.Z = record.BindZ;
                return;
            }
            AutonomousPopulationController.PlaceAtCapitalFallback(bot, record);
        }

        /// <summary>A scheduled login never appears in the frontier or a dungeon: it starts at its bind, or its capital.</summary>
        private static void PlaceAwayFromFrontierAndDungeons(GameBot bot, OfflineWorldBotRecord record)
        {
            Region region = WorldMgr.GetRegion((ushort)record.RegionId);
            bool unsafeSpot = region == null || region.IsFrontier || region.IsRvR || region.IsDungeon ||
                              region.GetZone(record.X, record.Y)?.IsDungeon == true;
            if (!unsafeSpot)
                return;
            Region bind = record.BindRegionId > 0 ? WorldMgr.GetRegion((ushort)record.BindRegionId) : null;
            if (bind != null && !bind.IsFrontier && !bind.IsRvR && !bind.IsDungeon)
            {
                bot.CurrentRegionID = (ushort)record.BindRegionId;
                bot.X = record.BindX;
                bot.Y = record.BindY;
                bot.Z = record.BindZ;
                return;
            }
            AutonomousPopulationController.PlaceAtCapitalFallback(bot, record);
        }

        // ---- level-1 creation

        /// <summary>
        /// Creates a new level-1 warm bot: a Classic + SI class and race of the realm, a name from BotNameService, no
        /// position (the login places it at a validated start point). Database work: background thread only. Returns its
        /// BotId, or 0 when no name could be claimed or the write failed.
        /// </summary>
        public static long CreateLevelOne(eRealm realm)
        {
            if (!IsReady || realm is not (eRealm.Albion or eRealm.Midgard or eRealm.Hibernia))
                return 0;
            BotArchive archive = BotArchiveStore.Current;
            if (archive == null)
                return 0;
            IReadOnlyCollection<eCharacterClass> classes = AutonomousBotIdentityGenerator.GetEraClasses(realm);
            if (classes.Count == 0)
                return 0;
            eCharacterClass characterClass = classes.ElementAt(Random.Shared.Next(classes.Count));
            IReadOnlyCollection<eRace> races = AutonomousBotIdentityGenerator.GetEligibleRaces(characterClass);
            if (races.Count == 0)
                return 0;
            eRace race = races.ElementAt(Random.Shared.Next(races.Count));
            eGender gender = Random.Shared.Next(2) == 0 ? eGender.Male : eGender.Female;
            return BotCreation.CreateLevelOne(archive.WorldConnectionString, archive, realm, characterClass, race, gender,
                BotNameService.TryClaimNewBotName, BotPopulationMaintenance.NewcomerProfile(DateTime.UtcNow));
        }

        // ---- background chores

        private static void SweepStaleLogouts()
        {
            DateTime now = DateTime.UtcNow;
            foreach ((long botId, LogoutIntent intent) in Logouts.ToArray())
            {
                if (intent.Bot?.ObjectState != GameObject.eObjectState.Active)
                    Logouts.TryRemove(new KeyValuePair<long, LogoutIntent>(botId, intent));
                else if (now >= intent.GiveUpUtc)
                    DropLogout(botId, intent);
            }
            foreach (Group group in GroupNextDeparture.Keys.ToArray())
                if (GroupNextDeparture.TryGetValue(group, out long next) && GameLoop.GameLoopTime > next + 600_000)
                    GroupNextDeparture.TryRemove(group, out _);
        }

        /// <summary>Warm bots idle 36 h or more move to the cold archive, a few at a time; more when warm rows pile up.</summary>
        private static void ArchiveIdleWarm(BotArchive archive)
        {
            if (archive == null)
                return;
            int live = AutonomousBotRegistry.RosterCount;
            long warmOffline = archive.WarmOfflineCount();
            int limit = warmOffline > 2L * live + 500 ? 16 : 8;
            DateTime before = warmOffline > 2L * live + 500 ? DateTime.UtcNow : DateTime.UtcNow - BotArchive.WarmIdleAge;
            foreach (long botId in archive.IdleWarm(limit, before))
                archive.ArchiveToCold(botId);
        }
    }

    /// <summary>The live world as the archive sees it.</summary>
    internal sealed class WorldArchiveHooks : IBotArchiveHooks
    {
        public bool IsBusy(long botId) =>
            AutonomousBotRegistry.Contains(botId) || AutonomousBotLifecycle.IsLoggingIn(botId) ||
            AutonomousBotStatusPersistence.IsLogoutPending(botId);

        public bool IsHired(long botId) => HiredCompanions165.IsHired(botId);

        public bool TryLocked(Action work)
        {
            // Never wait: game-loop Exchange and inventory work holds these; the caller retries later.
            if (!RealmExchangeBroker.TransactionLock.TryEnter())
                return false;
            try
            {
                bool economy = false, write = false;
                try
                {
                    Monitor.TryEnter(typeof(AutonomousBotEconomy), 0, ref economy);
                    if (!economy)
                        return false;
                    Monitor.TryEnter(AutonomousBotStatusPersistence.DatabaseWriteLock, 0, ref write);
                    if (!write)
                        return false;
                    work();
                    return true;
                }
                finally
                {
                    if (write)
                        Monitor.Exit(AutonomousBotStatusPersistence.DatabaseWriteLock);
                    if (economy)
                        Monitor.Exit(typeof(AutonomousBotEconomy));
                }
            }
            finally
            {
                RealmExchangeBroker.TransactionLock.Exit();
            }
        }

        public object RemoveListings(string ownerId)
        {
            DbInventoryItem[] listings = MarketCache.SearchItems(new ItemQuery { Owner = ownerId })
                .Where(item => RealmExchangeBroker.IsExchangeOwnerLot(item.OwnerLot)).ToArray();
            foreach (DbInventoryItem item in listings)
                MarketCache.RemoveItem(item);
            return listings;
        }

        public void RestoreListings(object token)
        {
            if (token is DbInventoryItem[] listings)
                foreach (DbInventoryItem item in listings)
                    MarketCache.AddItem(item);
        }

        public void AddListings(string ownerId)
        {
            foreach (DbInventoryItem item in DOLDB<DbInventoryItem>.SelectObjects(DB.Column("OwnerID").IsEqualTo(ownerId)))
                if (RealmExchangeBroker.IsExchangeOwnerLot(item.OwnerLot))
                    MarketCache.AddItem(GameInventoryItem.Create(item));
        }

        public void OnColdChanged(long botId, string name, bool cold)
        {
            if (!string.IsNullOrWhiteSpace(name))
                BotNameService.MarkArchived(name, cold);
        }

        public void OnRetired(long botId, string name)
        {
            AutonomousBotStatusPersistence.Forget(botId);
            AutonomousBotEconomy.ForgetCachedDecision(botId);
            AutonomousBotDyes.Forget(botId);
            if (!string.IsNullOrWhiteSpace(name))
                BotNameService.ReleaseBotName(name);
            // The guild list and its social-window entry live on the game loop.
            NpcService.Instance.Post<long>(id => AutonomousBotGuilds.Remove(id, null), botId);
        }
    }
}
