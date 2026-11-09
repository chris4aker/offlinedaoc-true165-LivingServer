using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using DOL.Database;
using DOL.Events;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Runs the population scheduler and the retirement on two background timers (1 s each) while the server runs.
    /// Re-reads population_scheduler_enabled and population_saved_cap from the database every 10 s, so both work as
    /// live switches without a restart.
    /// </summary>
    public static class PopulationSchedulerHost
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
        private static void LogInfo(string message) { if (Log.IsInfoEnabled) Log.Info(message); }
        private static void LogWarn(string message) { if (Log.IsWarnEnabled) Log.Warn(message); }
        private static void LogError(string message, Exception exception) { if (Log.IsErrorEnabled) Log.Error(message, exception); }
        private static readonly TimeSpan PropertyInterval = TimeSpan.FromSeconds(10);
        private static Timer _schedulerTimer;
        private static Timer _retirementTimer;
        private static int _schedulerBusy;
        private static int _retirementBusy;
        private static DateTime _nextPropertyReadUtc;
        private static CorePopulationLifecycle _lifecycle;

        public static AutonomousPopulationScheduler Scheduler { get; private set; }
        public static PopulationRetirement Retirement { get; private set; }

        /// <summary>The live value of population_scheduler_enabled (default true; false = the old behaviour).</summary>
        public static bool IsEnabled => PopulationSchedulerProperties.POPULATION_SCHEDULER_ENABLED;

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
        {
            try
            {
                string stateDir = Classic165Mode.StateDirectory(AppContext.BaseDirectory, Classic165Mode.IsOn);
                _lifecycle = new CorePopulationLifecycle();
                var archive = new CorePopulationArchive();
                var world = new CorePopulationWorld();
                var scheduler = new AutonomousPopulationScheduler(_lifecycle, archive, world, SystemPopulationClock.Instance, new Random(), stateDir);
                scheduler.SetSavedCap(PopulationSchedulerProperties.POPULATION_SAVED_CAP);
                scheduler.SetEnabled(PopulationSchedulerProperties.POPULATION_SCHEDULER_ENABLED);
                scheduler.SetPopulationEnabled(AutonomousPopulationProperties.POPULATION_ENABLED);
                Scheduler = scheduler;
                Retirement = new PopulationRetirement(archive, _lifecycle, world, scheduler.IsBusy, scheduler.Live50Target,
                    () => PopulationSchedulerProperties.POPULATION_SAVED_CAP, SystemPopulationClock.Instance, new Random(),
                    scheduler.NotifyRetired);
                _nextPropertyReadUtc = DateTime.UtcNow + PropertyInterval;
                _schedulerTimer = new Timer(SchedulerTick, null, 1000, 1000);
                _retirementTimer = new Timer(RetirementTick, null, 1500, 1000);
                LogInfo($"POPULATION_SCHEDULER started enabled={PopulationSchedulerProperties.POPULATION_SCHEDULER_ENABLED} cap={PopulationSchedulerProperties.POPULATION_SAVED_CAP} state={stateDir}");
            }
            catch (Exception exception)
            {
                LogError("Could not start the population scheduler", exception);
            }
        }

        [GameServerStoppedEvent]
        public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
        {
            _schedulerTimer?.Dispose();
            _schedulerTimer = null;
            _retirementTimer?.Dispose();
            _retirementTimer = null;
            _lifecycle?.Dispose();
        }

        private static void SchedulerTick(object state)
        {
            if (Interlocked.Exchange(ref _schedulerBusy, 1) != 0) return;
            try
            {
                AutonomousPopulationScheduler scheduler = Scheduler;
                if (scheduler == null) return;
                if (DateTime.UtcNow >= _nextPropertyReadUtc)
                {
                    _nextPropertyReadUtc = DateTime.UtcNow + PropertyInterval;
                    ReadLiveProperties();
                }
                scheduler.SetSavedCap(PopulationSchedulerProperties.POPULATION_SAVED_CAP);
                scheduler.SetEnabled(PopulationSchedulerProperties.POPULATION_SCHEDULER_ENABLED);
                scheduler.SetPopulationEnabled(AutonomousPopulationProperties.POPULATION_ENABLED);
                scheduler.Tick();
            }
            catch (Exception exception)
            {
                LogError("Population scheduler tick failed", exception);
            }
            finally
            {
                Volatile.Write(ref _schedulerBusy, 0);
            }
        }

        private static void RetirementTick(object state)
        {
            if (Interlocked.Exchange(ref _retirementBusy, 1) != 0) return;
            try
            {
                if (Retirement != null && PopulationSchedulerProperties.POPULATION_SCHEDULER_ENABLED &&
                    AutonomousPopulationProperties.POPULATION_ENABLED && _lifecycle?.Ready == true)
                    Retirement.Tick();
            }
            catch (Exception exception)
            {
                LogError("Population retirement tick failed", exception);
            }
            finally
            {
                Volatile.Write(ref _retirementBusy, 0);
            }
        }

        /// <summary>One indexed row each; also updates the static fields every other system reads.</summary>
        private static void ReadLiveProperties()
        {
            try
            {
                string enabled = DOLDB<DbServerProperty>.SelectObject(DB.Column("Key").IsEqualTo(PopulationSchedulerProperties.EnabledKey))?.Value;
                if (bool.TryParse(enabled, out bool on)) PopulationSchedulerProperties.POPULATION_SCHEDULER_ENABLED = on;
                string cap = DOLDB<DbServerProperty>.SelectObject(DB.Column("Key").IsEqualTo(PopulationSchedulerProperties.SavedCapKey))?.Value;
                if (int.TryParse(cap, out int value) && value > 0) PopulationSchedulerProperties.POPULATION_SAVED_CAP = value;
                // The launcher turns this on (a small DB update) when a goal is above 0.
                string master = DOLDB<DbServerProperty>.SelectObject(DB.Column("Key").IsEqualTo("population_enabled"))?.Value;
                if (bool.TryParse(master, out bool populationOn)) AutonomousPopulationProperties.POPULATION_ENABLED = populationOn;
            }
            catch (Exception exception)
            {
                LogError("Could not re-read the population server properties", exception);
            }
        }
    }

    /// <summary>CORE's lifecycle behind the scheduler's interface.</summary>
    internal sealed class CorePopulationLifecycle : IPopulationLifecycle, IDisposable
    {
        public event Action<long> LogoutRefused;
        public volatile bool Stopping;

        // The first shutdown logout stops all decisions, so bots leaving for the shutdown keep WasOnline (crash recovery).
        public CorePopulationLifecycle() => AutonomousBotLifecycle.LoggedOut += OnLoggedOut;

        private void OnLoggedOut(long botId, eRealm realm, BotLogoutReason reason)
        {
            if (reason == BotLogoutReason.Shutdown) Stopping = true;
        }

        public void Dispose()
        {
            Stopping = true;
            AutonomousBotLifecycle.LoggedOut -= OnLoggedOut;
        }

        public bool Ready => !Stopping && GameServer.Instance?.ServerStatus == EGameServerStatus.GSS_Open &&
                             AutonomousBotLifecycle.IsReady && BotArchiveStore.IsReady;

        public void LiveBots(eRealm realm, List<LiveBotInfo> into)
        {
            foreach (GameBot bot in AutonomousBotLifecycle.LiveBots(realm))
            {
                if (bot == null || !bot.IsAutonomousWorldBot || bot.DatabaseID <= 0 || HiredCompanions165.IsHired(bot)) continue;
                into.Add(new LiveBotInfo(bot.DatabaseID, bot.Level, EngagementOf(bot)));
            }
        }

        // Plain reads of fields the game loop writes; a stale answer only changes the logout order a little.
        private static BotEngagement EngagementOf(GameBot bot)
        {
            if (bot.Group != null) return BotEngagement.OnObjective;
            eAutonomousObjectiveKind kind = AutonomousObjectiveAssignments.KindFor(bot);
            if (kind is eAutonomousObjectiveKind.GroupPve or eAutonomousObjectiveKind.RvR or eAutonomousObjectiveKind.Battleground)
                return BotEngagement.OnObjective;
            return bot.InCombat ? BotEngagement.Fighting : BotEngagement.Idle;
        }

        public bool IsOnline(long botId) => AutonomousBotRegistry.Contains(botId) || AutonomousBotLifecycle.IsLoggingIn(botId);

        public bool RequestLogin(long botId, BotLoginPriority priority) => AutonomousBotLifecycle.RequestLogin(botId, priority);

        // The logout check and request touch the live bot, so they run on the game loop; a refusal comes back as an event.
        public bool RequestLogout(long botId, BotLogoutReason reason)
        {
            NpcService.Instance.Post(LogoutOnGameLoop, (botId, reason));
            return true;
        }

        private void LogoutOnGameLoop((long BotId, BotLogoutReason Reason) request)
        {
            bool accepted = false;
            try
            {
                accepted = AutonomousBotRegistry.TryGet(request.BotId, out GameBot bot) && AutonomousBotLifecycle.RequestLogout(bot, request.Reason);
            }
            finally
            {
                if (!accepted) LogoutRefused?.Invoke(request.BotId);
            }
        }

        public long CreateLevelOne(eRealm realm) => AutonomousBotLifecycle.CreateLevelOne(realm);
    }

    internal sealed class CorePopulationArchive : IPopulationArchive
    {
        public long SavedCount => BotArchiveStore.SavedCount;
        public long Count(eRealm realm, int minLevel, int maxLevel) => BotArchiveStore.Count(realm, minLevel, maxLevel);
        public IReadOnlyList<ArchiveCandidate> Candidates(eRealm realm, int minLevel, int maxLevel, int limit, DateTime wantBeforeUtc) =>
            BotArchiveStore.Candidates(realm, minLevel, maxLevel, limit, wantBeforeUtc);
        public void SaveProfile(long botId, BotPopulationProfile profile) => BotArchiveStore.SaveProfile(botId, profile);
        public BotPopulationProfile GetProfile(long botId) => BotArchiveStore.GetProfile(botId);
        public IReadOnlyList<long> RandomRetireCandidates(int count, Func<ArchiveCandidate, bool> eligible) =>
            BotArchiveStore.RandomRetireCandidates(count, eligible);
        public void Retire(IReadOnlyCollection<long> botIds) => BotArchiveStore.Retire(botIds);
    }

    internal sealed class CorePopulationWorld : IPopulationWorld
    {
        public bool IsHired(long botId) => HiredCompanions165.IsHired(botId);
        public bool IsPlayerGuildmate(long botId) => AutonomousBotGuilds.GuildOf(botId) != null;
        public bool IsGoalExempt(long botId) => RallyGuests165.IsRallyGuest(botId);
    }
}
