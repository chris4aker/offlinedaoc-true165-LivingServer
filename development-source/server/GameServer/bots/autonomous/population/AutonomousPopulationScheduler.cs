using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>Hidden play style of a bot (plan 3.3). Time of day does not matter (owner 2026-11-08).</summary>
    public enum PopulationPersona : byte { Veteran = 0, Regular = 1, Casual = 2, Newcomer = 3 }

    /// <summary>Persona numbers and the profile maths: play days, session lengths, daily caps, quitting and coming back.</summary>
    public static class PopulationPersonas
    {
        public static readonly double[] Mix = { 0.15, 0.30, 0.35, 0.20 };
        public static readonly double[] SessionHours = { 5.0, 3.0, 1.6, 1.2 };
        public static readonly double[] DailyCapHours = { 16, 10, 6, 4 };
        public static readonly double[] GapHours = { 10, 30, 70, 60 };
        public static readonly double[] QuitPerDay = { 0.001, 0.0025, 0.006, 0.04 };
        public static readonly double[] ReturnOdds = { 0.6, 0.5, 0.4, 0.25 };
        public static readonly double[] ReturnDays = { 45, 60, 75, 90 };
        private static readonly int[] MinDays = { 6, 4, 2, 3 };
        private static readonly int[] MaxDays = { 7, 5, 3, 5 };

        /// <summary>NextWantUtc of a bot while it plays: sorts first in the archive's candidate order, so after a crash the
        /// bots that were online come back first, like players reconnecting.</summary>
        public static readonly DateTime OnlineMarkerUtc = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>A quitter who never comes back still gets a date, far away, so "returning quitters" can find them last.</summary>
        public static readonly TimeSpan NeverReturns = TimeSpan.FromDays(3650);

        public static PopulationPersona Effective(byte persona, bool playerGuildmate)
        {
            var p = (PopulationPersona)Math.Min(persona, (byte)3);
            // Bots in the player's guild play regular-or-better schedules, so the player sees them often.
            return playerGuildmate && p > PopulationPersona.Regular ? PopulationPersona.Regular : p;
        }

        public static int DailyCapMinutes(PopulationPersona p) => (int)(DailyCapHours[(int)p] * 60);

        public static PopulationPersona RandomPersona(Random rng)
        {
            double roll = rng.NextDouble(), sum = 0;
            for (int i = 0; i < Mix.Length; i++)
            {
                sum += Mix[i];
                if (roll < sum) return (PopulationPersona)i;
            }
            return PopulationPersona.Casual;
        }

        /// <summary>A week mask (bit per DayOfWeek) with the persona's number of play days.</summary>
        public static byte RandomPlayDays(PopulationPersona p, Random rng)
        {
            int days = rng.Next(MinDays[(int)p], MaxDays[(int)p] + 1);
            var order = Enumerable.Range(0, 7).OrderBy(_ => rng.Next()).Take(days);
            byte mask = 0;
            foreach (int d in order) mask |= (byte)(1 << d);
            return mask;
        }

        /// <summary>The profile of a brand-new bot: wants to play at once.</summary>
        public static BotPopulationProfile NewProfile(Random rng, DateTime nowUtc)
        {
            PopulationPersona p = RandomPersona(rng);
            return new BotPopulationProfile((byte)p, RandomPlayDays(p, rng), nowUtc, nowUtc, 0, 0, nowUtc.Date, 0, false);
        }

        /// <summary>A stable made-up profile for a bot that has none (seeded by its id).</summary>
        public static BotPopulationProfile ForUnknown(long botId, DateTime nowUtc)
        {
            var rng = new Random(unchecked((int)(botId * 2654435761L)));
            PopulationPersona p = RandomPersona(rng);
            return new BotPopulationProfile((byte)p, RandomPlayDays(p, rng), nowUtc, nowUtc, 0, 0, nowUtc.Date, 0, false);
        }

        public static int TodayMinutes(BotPopulationProfile p, DateTime nowUtc) =>
            p.TodayDate.Date == nowUtc.Date ? p.TodayMinutes : 0;

        /// <summary>Minutes played today by a bot that logged in at loginUtc and is still online.</summary>
        public static int LiveTodayMinutes(BotPopulationProfile p, DateTime loginUtc, DateTime nowUtc)
        {
            if (nowUtc.Date == loginUtc.Date)
                return TodayMinutes(p, loginUtc) + (int)Math.Max(0, (nowUtc - loginUtc).TotalMinutes);
            return (int)(nowUtc - nowUtc.Date).TotalMinutes;
        }

        public static double DrawSessionHours(PopulationPersona p, Random rng) =>
            Math.Exp(Math.Log(SessionHours[(int)p]) + 0.5 * Gaussian(rng));

        /// <summary>The next time the bot wants to play: an exponential gap, moved onto one of its play days.</summary>
        public static DateTime NextWant(PopulationPersona p, byte playDays, DateTime fromUtc, Random rng)
        {
            double hours = -Math.Log(1 - rng.NextDouble()) * GapHours[(int)p];
            DateTime next = fromUtc.AddHours(Math.Min(hours, 24 * 60));
            if (playDays == 0 || (playDays & 0x7F) == 0x7F) return next;
            for (int i = 0; i < 7 && (playDays & (1 << (int)next.DayOfWeek)) == 0; i++)
                next = next.Date.AddDays(1).AddMinutes(rng.Next(0, 24 * 60));   // any time of that day
            return next;
        }

        /// <summary>Marks a profile as playing now (also what crash recovery looks for: WasOnline).</summary>
        public static BotPopulationProfile StartSession(BotPopulationProfile p, DateTime nowUtc)
        {
            bool sameDay = p.TodayDate.Date == nowUtc.Date;
            return p with
            {
                Status = 0,
                WasOnline = true,
                NextWantUtc = OnlineMarkerUtc,
                TodayMinutes = sameDay ? p.TodayMinutes : 0,
                TodayDate = nowUtc.Date,
            };
        }

        /// <summary>A clean logout: books the minutes, picks the next play time and maybe quits for a while.</summary>
        public static BotPopulationProfile EndSession(BotPopulationProfile p, DateTime loginUtc, DateTime nowUtc, bool playerGuildmate, Random rng)
        {
            PopulationPersona persona = Effective(p.Persona, playerGuildmate);
            int minutes = (int)Math.Max(0, (nowUtc - loginUtc).TotalMinutes);
            int today = LiveTodayMinutes(p, loginUtc, nowUtc);
            DateTime next = NextWant(persona, p.PlayDaysMask, nowUtc, rng);
            var ended = p with
            {
                PlayedMinutes = p.PlayedMinutes + minutes,
                TodayMinutes = today,
                TodayDate = nowUtc.Date,
                NextWantUtc = next,
                Status = 0,
                WasOnline = false,
            };
            // Daily quit odds over the time until the next session (the same odds as a daily roll while offline).
            double days = Math.Max(0.25, (next - nowUtc).TotalDays);
            double quit = 1 - Math.Pow(1 - QuitPerDay[(int)persona], days);
            if (rng.NextDouble() < quit)
            {
                DateTime back = rng.NextDouble() < ReturnOdds[(int)persona]
                    ? nowUtc.AddDays(-Math.Log(1 - rng.NextDouble()) * ReturnDays[(int)persona])
                    : nowUtc + NeverReturns;
                ended = ended with { Status = 1, ReturnUtc = back, NextWantUtc = back };
            }
            return ended;
        }

        private static double Gaussian(Random rng)
        {
            double u1 = 1 - rng.NextDouble(), u2 = rng.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }
    }

    /// <summary>
    /// The true online world (plan 3.3): keeps each realm at its goal from population-goals.json, spread over the level
    /// brackets 12/13/13/13/14/35 with floors and a downward cascade. Bots log in and out like players: sessions end,
    /// quitters come back, new level 1s arrive in the starting areas. Decisions every 10 s per realm on a background timer;
    /// logins and logouts are each limited to 2 a second in total. It only asks CORE's lifecycle to log bots in or out.
    /// </summary>
    public sealed class AutonomousPopulationScheduler
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
        private static void LogInfo(string message) { if (Log.IsInfoEnabled) Log.Info(message); }
        private static void LogWarn(string message) { if (Log.IsWarnEnabled) Log.Warn(message); }
        private static void LogError(string message, Exception exception) { if (Log.IsErrorEnabled) Log.Error(message, exception); }

        public const int BracketCount = 6;
        public static readonly int[] BracketMin = { 1, 10, 20, 30, 40, 50 };
        public static readonly int[] BracketMax = { 9, 19, 29, 39, 49, 50 };
        private static readonly double[] LowerShareWeights = { 12, 13, 13, 13, 14 };
        public static readonly eRealm[] Realms = { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia };

        public static readonly TimeSpan DecisionInterval = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan StatusInterval = TimeSpan.FromSeconds(15);
        public static readonly TimeSpan ArchiveCountInterval = TimeSpan.FromSeconds(60);
        public const double MaxLoginsPerSecond = 2;
        public const double MaxLogoutsPerSecond = 2;
        public const int QueueCapPerRealm = 20;
        public const double RampFractionPerMinute = 0.02;
        public const int PoolMaxLimit = 256;
        private static readonly TimeSpan PoolMaxAge = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan PoolMinAge = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan PendingLoginCounts = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan PendingLoginKept = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan PendingLogoutKept = TimeSpan.FromMinutes(35);
        private static readonly TimeSpan RefusedLogoutWait = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan RefusedLoginWait = TimeSpan.FromMinutes(10);
        // A bot that just logged out is not called straight back in.
        private static readonly TimeSpan RestAfterLogout = TimeSpan.FromMinutes(30);
        private const int Big = 1_000_000_000;
        // Rotation pace (0 Off, 1 Slow, 2 Normal, 3 Lively): session length and swap budget multipliers.
        private static readonly double[] PaceSession = { 0, 1.5, 1.0, 0.67 };
        private static readonly double[] PaceSwap = { 0, 0.5, 1.0, 1.5 };

        #region Pure bracket maths (unit tested)

        public static int BracketOf(int level)
        {
            if (level >= 50) return 5;
            if (level < 10) return 0;
            return Math.Min(4, level / 10);
        }

        /// <summary>Percent of the goal per bracket (sums to 100). The five lower brackets share what the level-50 cap leaves.</summary>
        public static double[] BracketShares(int level50MaxPercent, int floorPercent)
        {
            double cap = Math.Clamp(level50MaxPercent, 0, 100);
            double floor = Math.Clamp(floorPercent, 0, 20);
            double rest = 100 - cap;
            if (5 * floor > rest) floor = rest / 5;
            var s = new double[BracketCount];
            s[5] = cap;
            for (int i = 0; i < 5; i++) s[i] = Math.Max(floor, rest * LowerShareWeights[i] / 65.0);
            double sum = s.Take(5).Sum();
            if (sum > rest + 1e-9)
            {
                double adjustable = s.Take(5).Sum(x => x - floor);
                double excess = sum - rest;
                for (int i = 0; i < 5; i++) s[i] -= adjustable > 0 ? (s[i] - floor) * excess / adjustable : 0;
            }
            return s;
        }

        /// <summary>The live minimum per bracket: max(2, floor% of the goal); never more than a sixth of the goal.</summary>
        public static int FloorCount(int goal, int floorPercent)
        {
            if (goal <= 0 || floorPercent <= 0) return 0;
            int floor = Math.Max(2, (int)Math.Ceiling(goal * floorPercent / 100.0 - 1e-9));
            return Math.Min(floor, goal / BracketCount);
        }

        /// <summary>Bracket targets that add up to the goal, each at least its floor.</summary>
        public static int[] BracketTargets(int goal, int level50MaxPercent, int floorPercent)
        {
            var t = new int[BracketCount];
            if (goal <= 0) return t;
            double[] shares = BracketShares(level50MaxPercent, floorPercent);
            for (int b = 0; b < BracketCount; b++) t[b] = (int)Math.Floor(goal * shares[b] / 100.0 + 1e-9);
            t[0] += goal - t.Sum();
            int floor = FloorCount(goal, floorPercent);
            for (int b = 0; b < BracketCount; b++)
            {
                if (b == 5 && level50MaxPercent <= 0) continue;   // a 0% cap means no level 50 target at all
                while (t[b] < floor)
                {
                    int donor = -1;
                    for (int d = 0; d < BracketCount; d++)
                        if (d != b && t[d] > floor && (donor < 0 || t[d] > t[donor])) donor = d;
                    if (donor < 0) break;
                    t[donor]--;
                    t[b]++;
                }
            }
            return t;
        }

        /// <summary>A share that cannot be filled (too few saved bots at those levels) passes down to the next lower bracket.</summary>
        public static int[] Cascade(int[] targets, int[] supply)
        {
            var eff = (int[])targets.Clone();
            for (int b = BracketCount - 1; b > 0; b--)
            {
                if (supply[b] < eff[b])
                {
                    eff[b - 1] += eff[b] - supply[b];
                    eff[b] = Math.Max(0, supply[b]);
                }
            }
            return eff;
        }

        /// <summary>A bracket is over-full only when above its target by more than this.</summary>
        public static int Hysteresis(int effectiveTarget) => Math.Max(2, (int)Math.Ceiling(effectiveTarget * 0.05));

        /// <summary>How far the applied goal may move toward a new goal: about 2% of the goal a minute.</summary>
        public static double RampStep(double fromGoal, int toGoal, double seconds) =>
            Math.Max(1.0, RampFractionPerMinute * Math.Max(fromGoal, toGoal)) * seconds / 60.0;

        public static int SwapsPer30Minutes(int goal) => goal <= 0 ? 0 : Math.Max(4, (int)Math.Ceiling(goal * 0.04));

        /// <summary>New level 1s once running: at most 1% of the goal per 30 minutes.</summary>
        public static int NewPer30Minutes(int goal) => goal <= 0 ? 0 : Math.Max(1, (int)Math.Round(goal * 0.01));

        #endregion

        #region State

        private enum Leave : byte { None, Queued, Pending }

        private sealed class ManagedBot
        {
            public long BotId;
            public int Level;
            public BotEngagement Engagement;
            public BotPopulationProfile Profile;
            public DateTime LoginUtc;
            public DateTime SessionEndUtc;
            public Leave Leave;
            public DateTime LeaveUtc;
        }

        private sealed record LoginOrder(long BotId, int Level, BotPopulationProfile Profile, bool Returning, bool IsNew);
        private sealed record LogoutOrder(long BotId, BotLogoutReason Reason);

        private sealed class PendingLogin
        {
            public DateTime RequestedUtc;
            public BotPopulationProfile Profile;
            public bool Returning;
            public int Level;
        }

        private sealed class CandidatePool
        {
            public List<ArchiveCandidate> Items = new();
            public DateTime FetchedUtc = DateTime.MinValue;
            public DateTime DeepFetchedUtc = DateTime.MinValue;
            public bool Full;
        }

        private sealed class RealmState
        {
            public eRealm Realm;
            public bool Initialized;
            public double RampedGoal;
            public double RampBase;
            public int LastSettingsGoal;
            public bool FillPhase;
            public double SwapTokens;
            public double NewTokens;
            // owner 2026-11-10: level1 fill pace (AutonomousPopulationFillPace)
            public double FillNewTokens = AutonomousPopulationFillPace.PerMinute;
            public DateTime NextFillPaceLogUtc;
            public DateTime LastDecisionUtc;
            public DateTime NextDecisionUtc;
            public readonly Dictionary<long, ManagedBot> Live = new();
            public readonly HashSet<long> Exempt = new();
            public readonly Dictionary<long, PendingLogin> PendingLogins = new();
            public readonly Dictionary<long, DateTime> RefusedUntil = new();
            public readonly Dictionary<long, DateTime> RecentlyLeft = new();
            public readonly Dictionary<long, int> Refusals = new();
            public readonly List<LoginOrder> LoginQueue = new();
            public readonly List<LogoutOrder> LogoutQueue = new();
            public readonly CandidatePool[] Pools = Enumerable.Range(0, BracketCount).Select(_ => new CandidatePool()).ToArray();
            public int[] Targets = new int[BracketCount];
            public int[] Effective = new int[BracketCount];
            public int Floor;
            public long[] ArchivedByBracket = new long[BracketCount];
            public long ArchivedTotal;
            public readonly HourCounter Created = new(), Archived = new(), Returned = new(), Retired = new();
        }

        /// <summary>Counts per minute over the last hour.</summary>
        public sealed class HourCounter
        {
            private readonly long[] _minute = new long[60];
            private readonly int[] _count = new int[60];

            public void Add(DateTime nowUtc, int n = 1)
            {
                lock (_count)
                {
                    long m = nowUtc.Ticks / TimeSpan.TicksPerMinute;
                    int i = (int)(m % 60);
                    if (_minute[i] != m) { _minute[i] = m; _count[i] = 0; }
                    _count[i] += n;
                }
            }

            public int Total(DateTime nowUtc)
            {
                lock (_count)
                {
                    long m = nowUtc.Ticks / TimeSpan.TicksPerMinute;
                    int sum = 0;
                    for (int i = 0; i < 60; i++)
                        if (m - _minute[i] < 60 && m >= _minute[i]) sum += _count[i];
                    return sum;
                }
            }
        }

        private readonly IPopulationLifecycle _lifecycle;
        private readonly IPopulationArchive _archive;
        private readonly IPopulationWorld _world;
        private readonly IPopulationClock _clock;
        private readonly Random _rng;
        private readonly string _stateDir;
        private readonly PopulationGoalWatcher _watcher;
        private readonly Dictionary<eRealm, RealmState> _realms = new();
        private readonly ConcurrentDictionary<long, byte> _busy = new();
        private readonly ConcurrentQueue<long> _refusedLogouts = new();
        private readonly ConcurrentQueue<long> _retiredIds = new();
        private readonly List<LiveBotInfo> _liveBuffer = new();
        private readonly HashSet<long> _seen = new();
        private readonly List<long> _scratch = new();
        private double _loginTokens, _logoutTokens;
        private DateTime _lastExecuteUtc = DateTime.MinValue;
        private DateTime _nextStatusUtc = DateTime.MinValue;
        private DateTime _nextArchiveCountUtc = DateTime.MinValue;
        private int _loginRealm, _logoutRealm;
        private bool _enabled = true;
        private bool _populationEnabled = true;
        private long _savedCap = 1_000_000;

        public AutonomousPopulationScheduler(IPopulationLifecycle lifecycle, IPopulationArchive archive, IPopulationWorld world,
            IPopulationClock clock, Random rng, string stateDir)
        {
            _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            _archive = archive ?? throw new ArgumentNullException(nameof(archive));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _clock = clock ?? SystemPopulationClock.Instance;
            _rng = rng ?? new Random();
            _stateDir = stateDir;
            _watcher = stateDir == null ? null : new PopulationGoalWatcher(stateDir);
            DateTime now = _clock.UtcNow;
            for (int i = 0; i < Realms.Length; i++)
            {
                // Realms decide at different seconds so the work is spread out.
                _realms[Realms[i]] = new RealmState { Realm = Realms[i], NextDecisionUtc = now.AddSeconds(1 + i * 3) };
            }
            _lastExecuteUtc = now;
            _lifecycle.LogoutRefused += id => _refusedLogouts.Enqueue(id);
        }

        public PopulationGoalSettings Settings { get; private set; } = PopulationGoalSettings.Default;
        public string GoalError { get; private set; }
        public long AppliedRevision { get; private set; }
        public bool Enabled => _enabled;

        /// <summary>Uses new goals (null keeps the last good ones and only records the error).</summary>
        public void ApplySettings(PopulationGoalSettings settings, string error)
        {
            if (settings != null)
            {
                Settings = settings;
                AppliedRevision = settings.Revision;
            }
            GoalError = error;
        }

        public void SetSavedCap(long cap) => _savedCap = cap;

        /// <summary>The launcher's master switch (population_enabled). Off: no bot is logged in or out by the scheduler.</summary>
        public void SetPopulationEnabled(bool enabled) => _populationEnabled = enabled;
        public bool PopulationEnabled => _populationEnabled;

        /// <summary>Turning off hands the world back to the old controller; turning on adopts whoever is online.</summary>
        public void SetEnabled(bool enabled)
        {
            if (enabled == _enabled) return;
            _enabled = enabled;
            foreach (RealmState r in _realms.Values)
            {
                r.Initialized = false;
                r.Live.Clear();
                r.Exempt.Clear();
                r.PendingLogins.Clear();
                r.LoginQueue.Clear();
                r.LogoutQueue.Clear();
                r.RefusedUntil.Clear();
                r.RecentlyLeft.Clear();
                r.Refusals.Clear();
            }
            _busy.Clear();
            LogInfo($"POPULATION_SCHEDULER {(enabled ? "enabled" : "disabled")}");
        }

        /// <summary>True while a login for this bot is queued or under way (retirement never touches it).</summary>
        public bool IsBusy(long botId) => _busy.ContainsKey(botId);

        /// <summary>The configured live level-50 target of a realm (the retirement reserve is 1.5x this).</summary>
        public int Live50Target(eRealm realm)
        {
            PopulationGoalSettings s = Settings;
            return BracketTargets(s.GoalFor(realm), s.Level50MaxPercent, s.BracketFloorPercent)[5];
        }

        /// <summary>From the retirement thread.</summary>
        public void NotifyRetired(eRealm realm, IReadOnlyCollection<long> botIds)
        {
            foreach (long id in botIds) _retiredIds.Enqueue(id);
            if (_realms.TryGetValue(realm, out RealmState r)) r.Retired.Add(_clock.UtcNow, botIds.Count);
        }

        #endregion

        #region Read-only views for tests and the status file

        public int ManagedLive(eRealm realm) => _realms[realm].Live.Count;
        public int LiveNotLeaving(eRealm realm) => _realms[realm].Live.Values.Count(m => m.Leave == Leave.None);
        public int LeavingCount(eRealm realm) => _realms[realm].Live.Values.Count(m => m.Leave != Leave.None);
        public int InboundCount(eRealm realm)
        {
            RealmState r = _realms[realm];
            DateTime now = _clock.UtcNow;
            return r.LoginQueue.Count + r.PendingLogins.Values.Count(p => now - p.RequestedUtc < PendingLoginCounts);
        }
        public double RampedGoal(eRealm realm) => _realms[realm].RampedGoal;
        public bool InFillPhase(eRealm realm) => _realms[realm].FillPhase;
        public int[] Targets(eRealm realm) => (int[])_realms[realm].Targets.Clone();
        public int[] EffectiveTargets(eRealm realm) => (int[])_realms[realm].Effective.Clone();
        public int[] LiveByBracket(eRealm realm)
        {
            var c = new int[BracketCount];
            foreach (ManagedBot m in _realms[realm].Live.Values) c[BracketOf(m.Level)]++;
            return c;
        }

        #endregion

        #region The loop

        /// <summary>Called once a second (background timer). Never blocks on the game loop.</summary>
        public void Tick()
        {
            DateTime now = _clock.UtcNow;
            PollGoals();
            DrainNotifications(now);
            if (_enabled && _populationEnabled && _lifecycle.Ready)
            {
                Execute(now);
                foreach (RealmState r in _realms.Values)
                {
                    if (now < r.NextDecisionUtc) continue;
                    r.NextDecisionUtc = now + DecisionInterval;
                    try
                    {
                        Decide(r, now);
                    }
                    catch (Exception exception)
                    {
                        LogError($"Population decision for {PopulationGoalSettings.RealmName(r.Realm)} failed", exception);
                    }
                }
            }
            if (_stateDir != null && now >= _nextStatusUtc)
            {
                _nextStatusUtc = now + StatusInterval;
                WriteStatus(now);
            }
        }

        private void PollGoals()
        {
            if (_watcher == null) return;
            try
            {
                if (_watcher.Poll(out PopulationGoalSettings settings, out string error))
                {
                    ApplySettings(settings, error);
                    if (error == null)
                        LogInfo($"POPULATION_GOALS applied revision={settings.Revision} alb={settings.Albion} mid={settings.Midgard} hib={settings.Hibernia} rotation={settings.Rotation}");
                    else
                        LogWarn($"POPULATION_GOALS kept the last good goals: {error}");
                }
            }
            catch (Exception exception)
            {
                LogError("Could not read the population goals", exception);
            }
        }

        private void DrainNotifications(DateTime now)
        {
            while (_refusedLogouts.TryDequeue(out long id))
            {
                foreach (RealmState r in _realms.Values)
                {
                    if (!r.Live.TryGetValue(id, out ManagedBot m)) continue;
                    m.Leave = Leave.None;
                    NoteRefusedLogout(r, id, now);
                }
            }
            if (_retiredIds.IsEmpty) return;
            var retired = new HashSet<long>();
            while (_retiredIds.TryDequeue(out long id)) retired.Add(id);
            foreach (RealmState r in _realms.Values)
                foreach (CandidatePool pool in r.Pools)
                    pool.Items.RemoveAll(c => retired.Contains(c.BotId));
        }

        /// <summary>Runs the queued logins and logouts, each at most 2 a second in total, realms taking turns.</summary>
        public void Execute(DateTime now)
        {
            double seconds = Math.Clamp((now - _lastExecuteUtc).TotalSeconds, 0, 1);
            _lastExecuteUtc = now;
            _loginTokens = Math.Min(MaxLoginsPerSecond, _loginTokens + seconds * MaxLoginsPerSecond);
            _logoutTokens = Math.Min(MaxLogoutsPerSecond, _logoutTokens + seconds * MaxLogoutsPerSecond);

            while (_loginTokens >= 1)
            {
                RealmState r = NextWith(ref _loginRealm, x => x.LoginQueue.Count > 0);
                if (r == null) break;
                LoginOrder order = r.LoginQueue[0];
                r.LoginQueue.RemoveAt(0);
                if (!order.IsNew && (r.Live.ContainsKey(order.BotId) || r.PendingLogins.ContainsKey(order.BotId) || _lifecycle.IsOnline(order.BotId)))
                {
                    if (!r.PendingLogins.ContainsKey(order.BotId)) _busy.TryRemove(order.BotId, out _);
                    continue;   // stale order: no token spent
                }
                _loginTokens -= 1;
                DoLogin(r, order, now);
            }

            while (_logoutTokens >= 1)
            {
                RealmState r = NextWith(ref _logoutRealm, x => x.LogoutQueue.Count > 0);
                if (r == null) break;
                LogoutOrder order = r.LogoutQueue[0];
                r.LogoutQueue.RemoveAt(0);
                if (!r.Live.TryGetValue(order.BotId, out ManagedBot m) || m.Leave != Leave.Queued) continue;
                _logoutTokens -= 1;
                bool accepted;
                try
                {
                    accepted = _lifecycle.RequestLogout(order.BotId, order.Reason);
                }
                catch (Exception exception)
                {
                    LogError($"Logout request for bot {order.BotId} failed", exception);
                    accepted = false;
                }
                if (accepted)
                {
                    m.Leave = Leave.Pending;
                    m.LeaveUtc = now;
                }
                else
                {
                    m.Leave = Leave.None;
                    NoteRefusedLogout(r, order.BotId, now);
                }
            }
        }

        private RealmState NextWith(ref int cursor, Func<RealmState, bool> has)
        {
            for (int i = 0; i < Realms.Length; i++)
            {
                RealmState r = _realms[Realms[(cursor + i) % Realms.Length]];
                if (!has(r)) continue;
                cursor = (cursor + i + 1) % Realms.Length;
                return r;
            }
            return null;
        }

        private void DoLogin(RealmState r, LoginOrder order, DateTime now)
        {
            long id = order.BotId;
            BotPopulationProfile profile = order.Profile;
            int level = order.Level;
            try
            {
                if (order.IsNew)
                {
                    id = _lifecycle.CreateLevelOne(r.Realm);
                    if (id <= 0)
                    {
                        LogWarn($"POPULATION_CREATE_FAILED realm={PopulationGoalSettings.RealmName(r.Realm)}");
                        return;
                    }
                    profile = PopulationPersonas.NewProfile(_rng, now);
                    level = 1;
                    r.Created.Add(now);
                    _busy[id] = 0;
                }
                BotPopulationProfile started = PopulationPersonas.StartSession(profile, now);
                BotLoginPriority priority = profile.WasOnline ? BotLoginPriority.Returning : BotLoginPriority.Normal;
                if (_lifecycle.RequestLogin(id, priority))
                {
                    r.PendingLogins[id] = new PendingLogin { RequestedUtc = now, Profile = started, Returning = profile.Status == 1, Level = level };
                    // Saved at login so a crash leaves WasOnline set: those bots log in first at the next start.
                    _archive.SaveProfile(id, started);
                }
                else
                {
                    _busy.TryRemove(id, out _);
                    r.RefusedUntil[id] = now + RefusedLoginWait;
                    if (order.IsNew) _archive.SaveProfile(id, profile);
                }
            }
            catch (Exception exception)
            {
                _busy.TryRemove(id, out _);
                LogError($"Login request for bot {id} in {PopulationGoalSettings.RealmName(r.Realm)} failed", exception);
            }
        }

        #endregion

        #region Decisions

        private void Decide(RealmState r, DateTime now)
        {
            double dt = r.LastDecisionUtc == default ? DecisionInterval.TotalSeconds : Math.Clamp((now - r.LastDecisionUtc).TotalSeconds, 0, 60);
            r.LastDecisionUtc = now;
            Reconcile(r, now);

            PopulationGoalSettings s = Settings;
            int settingsGoal = s.GoalFor(r.Realm);
            if (!r.Initialized)
            {
                // Start: below the goal fills at the login rate (crash recovery); above it ramps down, never a mass logout.
                int live = r.Live.Count;
                r.RampedGoal = Math.Max(live, settingsGoal);
                r.RampBase = r.RampedGoal;
                r.FillPhase = live < settingsGoal;
                r.LastSettingsGoal = settingsGoal;
                r.Initialized = true;
            }
            else
            {
                if (settingsGoal != r.LastSettingsGoal)
                {
                    if (settingsGoal > r.LastSettingsGoal) r.FillPhase = true;
                    r.RampBase = Math.Max(r.RampedGoal, settingsGoal);   // 2% of the larger goal a minute, a straight ramp
                }
                r.LastSettingsGoal = settingsGoal;
                if (settingsGoal < r.RampedGoal)
                {
                    // Lowering starts from who is really there (or coming), never from an old goal nobody reached.
                    int present = r.LoginQueue.Count + r.PendingLogins.Values.Count(p => now - p.RequestedUtc < PendingLoginCounts) +
                                  r.Live.Values.Count(m => m.Leave == Leave.None);
                    r.RampedGoal = Math.Min(r.RampedGoal, Math.Max(settingsGoal, present));
                }
                double step = RampStep(r.RampBase, settingsGoal, dt);
                r.RampedGoal = r.RampedGoal < settingsGoal
                    ? Math.Min(settingsGoal, r.RampedGoal + step)
                    : Math.Max(settingsGoal, r.RampedGoal - step);
            }
            int goal = (int)Math.Round(r.RampedGoal);

            var have = new int[BracketCount];
            int liveTotal = 0;
            foreach (ManagedBot m in r.Live.Values)
            {
                if (m.Leave != Leave.None) continue;
                have[BracketOf(m.Level)]++;
                liveTotal++;
            }
            int inbound = 0;
            foreach (LoginOrder o in r.LoginQueue) { have[BracketOf(o.Level)]++; inbound++; }
            foreach (PendingLogin p in r.PendingLogins.Values)
            {
                if (now - p.RequestedUtc >= PendingLoginCounts) continue;
                have[BracketOf(p.Level)]++;
                inbound++;
            }

            int[] targets = BracketTargets(goal, s.Level50MaxPercent, s.BracketFloorPercent);
            r.Targets = targets;
            r.Floor = FloorCount(goal, s.BracketFloorPercent);

            // Candidate pools (background, indexed reads), then what each bracket could really be filled with.
            var lists = new CandidateLists();
            var supply = new int[BracketCount];
            for (int b = 0; b < BracketCount; b++)
            {
                int need = Math.Max(0, targets[b] - have[b]);
                EnsurePool(r, b, now, need + 8);
                lists.Build(b, r.Pools[b], now, this, r);
                supply[b] = r.Pools[b].Full ? Big : have[b] + lists.Eligible(b);
            }
            supply[0] = Big;
            int[] eff = Cascade(targets, supply);
            r.Effective = eff;

            int newPer30 = NewPer30Minutes(goal);
            r.NewTokens = Math.Min(newPer30, r.NewTokens + newPer30 * dt / 1800.0);
            r.FillNewTokens = AutonomousPopulationFillPace.NextTokens(r.FillNewTokens, dt);
            int pace = s.RotationStep;
            int swapPer30 = (int)Math.Ceiling(SwapsPer30Minutes(goal) * PaceSwap[pace]);
            r.SwapTokens = Math.Min(swapPer30, r.SwapTokens + swapPer30 * dt / 1800.0);

            var avail = new int[BracketCount];
            for (int b = 0; b < BracketCount; b++) avail[b] = r.Pools[b].Full ? Big : lists.Eligible(b);
            // owner 2026-11-10: while filling, session ends and swaps count only the free new bots, never the paced tokens
            // (those are Fill's; sharing them kept the realm flat below its goal)
            int newBudget = r.FillPhase
                ? AutonomousPopulationFillPace.ReplacementBudget(AutonomousPopulationFillPace.Enabled, liveTotal + inbound, goal, have[0], targets[0])
                : (int)Math.Floor(r.NewTokens);
            avail[0] = (int)Math.Min(Big, (long)avail[0] + newBudget);

            if (pace > 0)
            {
                EndSessions(r, now, have, eff, avail, ref liveTotal);
                Swap(r, now, have, eff, avail, ref liveTotal);
            }
            LowerToGoal(r, now, goal, have, eff, ref liveTotal, ref inbound);
            Fill(r, now, goal, have, eff, lists, ref liveTotal, ref inbound);

            if (r.FillPhase && (int)Math.Round(r.RampedGoal) == settingsGoal && liveTotal + inbound >= settingsGoal)
                r.FillPhase = false;
        }

        private void Reconcile(RealmState r, DateTime now)
        {
            _liveBuffer.Clear();
            _lifecycle.LiveBots(r.Realm, _liveBuffer);
            _seen.Clear();
            foreach (LiveBotInfo info in _liveBuffer)
            {
                long id = info.BotId;
                _seen.Add(id);
                if (r.Live.TryGetValue(id, out ManagedBot known))
                {
                    known.Level = info.Level;
                    known.Engagement = info.Engagement;
                    continue;
                }
                if (r.Exempt.Contains(id)) continue;
                if (_world.IsHired(id) || _world.IsGoalExempt(id))
                {
                    r.Exempt.Add(id);
                    continue;
                }
                if (r.PendingLogins.Remove(id, out PendingLogin pending))
                {
                    _busy.TryRemove(id, out _);
                    Adopt(r, id, info, pending.Profile, now, 1.0);
                    if (pending.Returning) r.Returned.Add(now);
                    continue;
                }
                if (!r.Initialized)
                {
                    // Online when the scheduler took over: adopt, with sessions that end at staggered times.
                    BotPopulationProfile stored = null;
                    try { stored = _archive.GetProfile(id); } catch (Exception exception) { LogError($"Could not read the profile of bot {id}", exception); }
                    BotPopulationProfile profile = PopulationPersonas.StartSession(stored ?? PopulationPersonas.ForUnknown(id, now), now);
                    Adopt(r, id, info, profile, now, _rng.NextDouble());
                    continue;
                }
                r.Exempt.Add(id);   // logged in by someone else (Horn guests): never counted, never logged out by us
            }

            _scratch.Clear();
            foreach (long id in r.Live.Keys)
                if (!_seen.Contains(id)) _scratch.Add(id);
            bool open = _lifecycle.Ready;
            foreach (long id in _scratch)
            {
                ManagedBot m = r.Live[id];
                r.Live.Remove(id);
                r.RefusedUntil.Remove(id);
                r.Refusals.Remove(id);
                r.RecentlyLeft[id] = now;
                if (!open) continue;   // shutting down: WasOnline stays set for crash recovery
                try
                {
                    BotPopulationProfile ended = PopulationPersonas.EndSession(m.Profile, m.LoginUtc, now, _world.IsPlayerGuildmate(id), _rng);
                    _archive.SaveProfile(id, ended);
                }
                catch (Exception exception)
                {
                    LogError($"Could not save the population profile of bot {id}", exception);
                }
                r.Archived.Add(now);
            }
            r.Exempt.RemoveWhere(id => !_seen.Contains(id));

            _scratch.Clear();
            foreach (KeyValuePair<long, PendingLogin> p in r.PendingLogins)
                if (now - p.Value.RequestedUtc >= PendingLoginKept) _scratch.Add(p.Key);
            foreach (long id in _scratch)
            {
                r.PendingLogins.Remove(id);
                _busy.TryRemove(id, out _);
                r.RefusedUntil[id] = now + RefusedLoginWait;
            }
            foreach (ManagedBot m in r.Live.Values)
                if (m.Leave == Leave.Pending && now - m.LeaveUtc >= PendingLogoutKept) m.Leave = Leave.None;
            if (r.RecentlyLeft.Count > 0)
            {
                _scratch.Clear();
                foreach (KeyValuePair<long, DateTime> x in r.RecentlyLeft)
                    if (now - x.Value >= RestAfterLogout) _scratch.Add(x.Key);
                foreach (long id in _scratch) r.RecentlyLeft.Remove(id);
            }
            if (r.RefusedUntil.Count > 0)
            {
                _scratch.Clear();
                foreach (KeyValuePair<long, DateTime> x in r.RefusedUntil)
                    if (x.Value <= now) _scratch.Add(x.Key);
                foreach (long id in _scratch) r.RefusedUntil.Remove(id);
            }
        }

        private void Adopt(RealmState r, long id, LiveBotInfo info, BotPopulationProfile profile, DateTime now, double sessionFraction)
        {
            PopulationPersona persona = PopulationPersonas.Effective(profile.Persona, _world.IsPlayerGuildmate(id));
            double pace = PaceSession[Math.Max(1, Settings.RotationStep)];
            double minutes = PopulationPersonas.DrawSessionHours(persona, _rng) * 60 * pace;
            int left = PopulationPersonas.DailyCapMinutes(persona) - PopulationPersonas.TodayMinutes(profile, now);
            minutes = Math.Max(10, Math.Min(minutes, left)) * Math.Clamp(sessionFraction, 0.05, 1.0);
            r.Live[id] = new ManagedBot
            {
                BotId = id, Level = info.Level, Engagement = info.Engagement, Profile = profile, LoginUtc = now,
                SessionEndUtc = now.AddMinutes(minutes),
            };
        }

        /// <summary>A refused logout waits 1, 2, 4 ... up to 30 minutes before that bot is asked again; others are picked meanwhile.</summary>
        private static void NoteRefusedLogout(RealmState r, long id, DateTime now)
        {
            int n = r.Refusals.TryGetValue(id, out int count) ? count + 1 : 1;
            r.Refusals[id] = n;
            double seconds = Math.Min(30 * 60, RefusedLogoutWait.TotalSeconds * Math.Pow(2, Math.Min(n - 1, 10)));
            r.RefusedUntil[id] = now.AddSeconds(seconds);
        }

        private bool Selectable(RealmState r, ManagedBot m, DateTime now) =>
            m.Leave == Leave.None && !(r.RefusedUntil.TryGetValue(m.BotId, out DateTime until) && until > now);

        private bool QueueLogout(RealmState r, ManagedBot m, BotLogoutReason reason)
        {
            if (r.LogoutQueue.Count >= QueueCapPerRealm) return false;
            m.Leave = Leave.Queued;
            r.LogoutQueue.Add(new LogoutOrder(m.BotId, reason));
            return true;
        }

        /// <summary>Sessions that are over end, unless nobody can replace the bot ("one more pull", up to its daily cap).</summary>
        private void EndSessions(RealmState r, DateTime now, int[] have, int[] eff, int[] avail, ref int liveTotal)
        {
            var done = new List<(ManagedBot Bot, bool Hard, int Today)>[BracketCount];
            foreach (ManagedBot m in r.Live.Values)
            {
                if (!Selectable(r, m, now)) continue;
                PopulationPersona persona = PopulationPersonas.Effective(m.Profile.Persona, _world.IsPlayerGuildmate(m.BotId));
                int today = PopulationPersonas.LiveTodayMinutes(m.Profile, m.LoginUtc, now);
                bool hard = today >= PopulationPersonas.DailyCapMinutes(persona);
                if (!hard && now < m.SessionEndUtc) continue;
                if (!hard && m.Engagement == BotEngagement.OnObjective)
                {
                    // On a group, RvR or battleground objective: finish it first (unless the daily cap is reached).
                    m.SessionEndUtc = now.AddMinutes(5 + _rng.Next(6));
                    continue;
                }
                int b = BracketOf(m.Level);
                (done[b] ??= new()).Add((m, hard, today));
            }
            // Replacements anywhere in the realm (saved bots that can play, plus the new level-1 budget).
            long spare = 0;
            for (int b = 0; b < BracketCount; b++) spare = Math.Min(Big, spare + avail[b]);
            for (int b = 0; b < BracketCount; b++)
            {
                List<(ManagedBot Bot, bool Hard, int Today)> list = done[b];
                if (list == null) continue;
                int need = eff[b] - (have[b] - list.Count);   // how many of them this bracket must get back
                // Hard (daily cap) first, then the longest played today; the least played stay for one more pull.
                list.Sort((x, y) => x.Hard != y.Hard ? (x.Hard ? -1 : 1)
                    : x.Bot.Engagement != y.Bot.Engagement ? x.Bot.Engagement.CompareTo(y.Bot.Engagement)
                    : y.Today.CompareTo(x.Today));
                int leaving = 0;
                foreach ((ManagedBot m, bool hard, _) in list)
                {
                    bool inBracket = leaving < need;
                    if (!hard && (spare <= 0 || inBracket && avail[b] <= 0))
                    {
                        // Nobody can take the slot: one more pull, looked at again in 10-20 minutes.
                        m.SessionEndUtc = now.AddMinutes(10 + _rng.Next(11));
                        continue;
                    }
                    if (!QueueLogout(r, m, BotLogoutReason.SessionOver)) return;
                    if (inBracket && avail[b] > 0) avail[b]--;
                    if (spare > 0) spare--;
                    have[b]--;
                    liveTotal--;
                    leaving++;
                }
            }
        }

        /// <summary>Over-full brackets swap bots out, but only when a replacement exists elsewhere; the live total never dips.</summary>
        private void Swap(RealmState r, DateTime now, int[] have, int[] eff, int[] avail, ref int liveTotal)
        {
            int fillable = 0;
            for (int b = 0; b < BracketCount; b++)
                fillable += Math.Min(Math.Max(0, eff[b] - have[b]), avail[b]);
            for (int b = BracketCount - 1; b >= 0 && fillable > 0 && r.SwapTokens >= 1; b--)
            {
                int over = have[b] - eff[b] - Hysteresis(eff[b]);
                if (over <= 0) continue;
                int k = Math.Min(over, Math.Min((int)r.SwapTokens, fillable));
                // Idle and solo bots first (never one on a group, RvR or battleground objective), then the longest played today.
                List<ManagedBot> pick = r.Live.Values
                    .Where(m => BracketOf(m.Level) == b && Selectable(r, m, now) && m.Engagement != BotEngagement.OnObjective)
                    .OrderBy(m => m.Engagement)
                    .ThenByDescending(m => PopulationPersonas.LiveTodayMinutes(m.Profile, m.LoginUtc, now))
                    .Take(k)
                    .ToList();
                foreach (ManagedBot m in pick)
                {
                    if (!QueueLogout(r, m, BotLogoutReason.Rebalance)) return;
                    have[b]--;
                    liveTotal--;
                    fillable--;
                    r.SwapTokens -= 1;
                }
            }
        }

        /// <summary>Above the (ramped) goal: cancel queued logins, then log out from the most over-full brackets, ending sessions first.</summary>
        private void LowerToGoal(RealmState r, DateTime now, int goal, int[] have, int[] eff, ref int liveTotal, ref int inbound)
        {
            int excess = liveTotal + inbound - goal;
            for (int i = r.LoginQueue.Count - 1; i >= 0 && excess > 0; i--)
            {
                LoginOrder o = r.LoginQueue[i];
                r.LoginQueue.RemoveAt(i);
                if (!o.IsNew) _busy.TryRemove(o.BotId, out _);
                have[BracketOf(o.Level)]--;
                inbound--;
                excess--;
            }
            while (excess > 0 && r.LogoutQueue.Count < QueueCapPerRealm)
            {
                // Per bracket the best bot to leave: idle/solo first, then the session that ends soonest.
                var picks = new ManagedBot[BracketCount];
                foreach (ManagedBot m in r.Live.Values)
                {
                    if (!Selectable(r, m, now)) continue;
                    int b = BracketOf(m.Level);
                    ManagedBot p = picks[b];
                    if (p == null || m.Engagement < p.Engagement || m.Engagement == p.Engagement && m.SessionEndUtc < p.SessionEndUtc)
                        picks[b] = m;
                }
                // The most over-full bracket with a bot not on an objective; objective bots only when nobody else can go.
                int best = -1;
                for (int b = 0; b < BracketCount; b++)
                {
                    if (picks[b] == null) continue;
                    if (best < 0) { best = b; continue; }
                    bool objB = picks[b].Engagement == BotEngagement.OnObjective;
                    bool objBest = picks[best].Engagement == BotEngagement.OnObjective;
                    if (objB != objBest ? !objB : have[b] - eff[b] > have[best] - eff[best]) best = b;
                }
                if (best < 0) break;
                if (!QueueLogout(r, picks[best], BotLogoutReason.GoalLowered)) break;
                have[best]--;
                liveTotal--;
                excess--;
            }
        }

        /// <summary>Fills shortfalls: wanting-now saved bots, rested early logins, returning quitters, then new level 1s (1-9 only).</summary>
        private void Fill(RealmState r, DateTime now, int goal, int[] have, int[] eff, CandidateLists lists, ref int liveTotal, ref int inbound)
        {
            int fill = Math.Min(goal - liveTotal - inbound, QueueCapPerRealm - r.LoginQueue.Count);
            // Highest bracket first, so saved bots (crash recovery too) come before new level 1s, which are last.
            for (int b = BracketCount - 1; b >= 0 && fill > 0; b--)
            {
                int need = Math.Min(eff[b] - have[b], fill);
                while (need > 0 && lists.Take(b, out ArchiveCandidate c))
                {
                    QueueLogin(r, c);
                    have[b]++;
                    need--;
                    fill--;
                    inbound++;
                }
                if (b == 0 && need > 0)
                {
                    int k = AllowNew(r, need, goal, liveTotal + inbound, have[0], now);
                    QueueNew(r, k);
                    have[0] += k;
                    fill -= k;
                    inbound += k;
                }
            }
            // Still short (brackets at target but the realm is not): any saved bot that can play, then new level 1s.
            while (fill > 0 && lists.TakeAny(out ArchiveCandidate c))
            {
                QueueLogin(r, c);
                have[BracketOf(c.Level)]++;
                fill--;
                inbound++;
            }
            if (fill > 0)
            {
                int k = AllowNew(r, fill, goal, liveTotal + inbound, have[0], now);
                QueueNew(r, k);
                have[0] += k;
                inbound += k;
            }
        }

        private void QueueLogin(RealmState r, ArchiveCandidate c)
        {
            BotPopulationProfile profile = c.Profile ?? PopulationPersonas.ForUnknown(c.BotId, _clock.UtcNow);
            _busy[c.BotId] = 0;
            r.LoginQueue.Add(new LoginOrder(c.BotId, Math.Clamp(c.Level, 1, 50), profile, profile.Status == 1, false));
            r.Pools[BracketOf(c.Level)].Items.Remove(c);
        }

        /// <summary>How many new level 1s Fill may queue now (owner 2026-11-10: at most 4 a realm a minute while filling,
        /// once the realm is half full and its 1-9 share is met; AutonomousPopulationFillPace). Spends the paced tokens.</summary>
        private int AllowNew(RealmState r, int want, int goal, int liveAndInbound, int bracket0, DateTime now)
        {
            if (want <= 0) return 0;
            if (!r.FillPhase) return Math.Min(want, (int)Math.Floor(r.NewTokens));
            if (!AutonomousPopulationFillPace.Enabled) return want;
            int target0 = r.Targets.Length > 0 ? r.Targets[0] : 0;
            int free = AutonomousPopulationFillPace.FreeNew(liveAndInbound, goal, bracket0, target0);
            int k = AutonomousPopulationFillPace.Allowed(want, free, r.FillNewTokens);
            if (k > free) r.FillNewTokens = Math.Max(0, r.FillNewTokens - (k - free));
            if (k < want && now >= r.NextFillPaceLogUtc)
            {
                r.NextFillPaceLogUtc = now.AddMinutes(1);
                LogInfo($"POPULATION_FILL_PACE realm={PopulationGoalSettings.RealmName(r.Realm)} new_now={k} held={want - k} " +
                        $"live_and_inbound={liveAndInbound} goal={goal} level1_9={bracket0}/{target0} per_minute={AutonomousPopulationFillPace.PerMinute}");
            }
            return k;
        }

        private void QueueNew(RealmState r, int count)
        {
            if (count <= 0) return;
            for (int i = 0; i < count; i++) r.LoginQueue.Add(new LoginOrder(0, 1, null, false, true));
            if (!r.FillPhase) r.NewTokens = Math.Max(0, r.NewTokens - count);
        }

        private void EnsurePool(RealmState r, int b, DateTime now, int want)
        {
            CandidatePool pool = r.Pools[b];
            TimeSpan age = now - pool.FetchedUtc;
            int usable = 0;
            foreach (ArchiveCandidate c in pool.Items)
                if (Classify(r, c, now) != CandidateClass.None) usable++;
            bool stale = age >= PoolMaxAge;
            bool short_ = usable < want && age >= PoolMinAge;
            if (!stale && !short_) return;
            int limit = Math.Clamp(want * 2 + 16, 16, PoolMaxLimit);
            try
            {
                IReadOnlyList<ArchiveCandidate> fetched = _archive.Candidates(r.Realm, BracketMin[b], BracketMax[b], limit, now.AddDays(3));
                var items = new List<ArchiveCandidate>(fetched ?? Array.Empty<ArchiveCandidate>());
                pool.Full = items.Count >= limit;
                // Not enough soon-wanting bots: look deeper (quitters and bots that want to play later).
                if (!pool.Full && items.Count < want && now - pool.DeepFetchedUtc >= TimeSpan.FromSeconds(60))
                {
                    pool.DeepFetchedUtc = now;
                    var ids = new HashSet<long>(items.Select(c => c.BotId));
                    IReadOnlyList<ArchiveCandidate> deep = _archive.Candidates(r.Realm, BracketMin[b], BracketMax[b], limit, DateTime.MaxValue);
                    foreach (ArchiveCandidate c in deep ?? Array.Empty<ArchiveCandidate>())
                        if (ids.Add(c.BotId)) items.Add(c);
                    pool.Full = deep != null && deep.Count >= limit;
                }
                pool.Items = items;
                pool.FetchedUtc = now;
            }
            catch (Exception exception)
            {
                pool.FetchedUtc = now;   // retry after the normal wait, not every decision
                LogError($"Could not read archive candidates for {PopulationGoalSettings.RealmName(r.Realm)} {BracketMin[b]}-{BracketMax[b]}", exception);
            }
        }

        private enum CandidateClass : byte { None, WantingNow, Rested, Lapsed }

        private CandidateClass Classify(RealmState r, ArchiveCandidate c, DateTime now)
        {
            long id = c.BotId;
            if (id <= 0 || _busy.ContainsKey(id) || r.Live.ContainsKey(id) || r.Exempt.Contains(id)) return CandidateClass.None;
            if (r.RefusedUntil.TryGetValue(id, out DateTime until) && until > now) return CandidateClass.None;
            if (r.RecentlyLeft.TryGetValue(id, out DateTime left) && now - left < RestAfterLogout) return CandidateClass.None;
            if (_world.IsHired(id) || _lifecycle.IsOnline(id)) return CandidateClass.None;
            BotPopulationProfile p = c.Profile ?? PopulationPersonas.ForUnknown(id, now);
            if (p.Status == 1) return p.ReturnUtc <= now ? CandidateClass.WantingNow : CandidateClass.Lapsed;
            PopulationPersona persona = PopulationPersonas.Effective(p.Persona, false);
            // A rested bot needs at least 20 minutes left of its daily cap.
            if (PopulationPersonas.TodayMinutes(p, now) >= PopulationPersonas.DailyCapMinutes(persona) - 20) return CandidateClass.None;
            if (p.WasOnline || p.NextWantUtc <= now) return CandidateClass.WantingNow;
            return CandidateClass.Rested;
        }

        /// <summary>Per-decision candidate lists per bracket, in fill order.</summary>
        private sealed class CandidateLists
        {
            private readonly List<ArchiveCandidate>[] _wanting = new List<ArchiveCandidate>[BracketCount];
            private readonly List<ArchiveCandidate>[] _rested = new List<ArchiveCandidate>[BracketCount];
            private readonly List<ArchiveCandidate>[] _lapsed = new List<ArchiveCandidate>[BracketCount];
            private readonly int[] _wi = new int[BracketCount], _ri = new int[BracketCount], _li = new int[BracketCount];

            public void Build(int b, CandidatePool pool, DateTime now, AutonomousPopulationScheduler s, RealmState r)
            {
                var w = new List<ArchiveCandidate>();
                var re = new List<ArchiveCandidate>();
                var l = new List<ArchiveCandidate>();
                foreach (ArchiveCandidate c in pool.Items)
                {
                    switch (s.Classify(r, c, now))
                    {
                        case CandidateClass.WantingNow: w.Add(c); break;
                        case CandidateClass.Rested: re.Add(c); break;
                        case CandidateClass.Lapsed: l.Add(c); break;
                    }
                }
                // Crash recovery first (bots that were online), then the longest waiting.
                w.Sort((x, y) =>
                {
                    bool xo = x.Profile?.WasOnline == true, yo = y.Profile?.WasOnline == true;
                    if (xo != yo) return xo ? -1 : 1;
                    return Want(x).CompareTo(Want(y));
                });
                re.Sort((x, y) => Want(x).CompareTo(Want(y)));
                l.Sort((x, y) => (x.Profile?.ReturnUtc ?? DateTime.MaxValue).CompareTo(y.Profile?.ReturnUtc ?? DateTime.MaxValue));
                _wanting[b] = w;
                _rested[b] = re;
                _lapsed[b] = l;
            }

            private static DateTime Want(ArchiveCandidate c) => c.Profile?.NextWantUtc ?? DateTime.MinValue;

            public int Eligible(int b) => _wanting[b].Count - _wi[b] + _rested[b].Count - _ri[b] + _lapsed[b].Count - _li[b];

            public bool Take(int b, out ArchiveCandidate c)
            {
                if (_wi[b] < _wanting[b].Count) { c = _wanting[b][_wi[b]++]; return true; }
                if (_ri[b] < _rested[b].Count) { c = _rested[b][_ri[b]++]; return true; }
                if (_li[b] < _lapsed[b].Count) { c = _lapsed[b][_li[b]++]; return true; }
                c = null;
                return false;
            }

            /// <summary>Any bracket, highest first, in the same class order.</summary>
            public bool TakeAny(out ArchiveCandidate c)
            {
                for (int b = BracketCount - 1; b >= 0; b--)
                    if (_wi[b] < _wanting[b].Count) { c = _wanting[b][_wi[b]++]; return true; }
                for (int b = BracketCount - 1; b >= 0; b--)
                    if (_ri[b] < _rested[b].Count) { c = _rested[b][_ri[b]++]; return true; }
                for (int b = BracketCount - 1; b >= 0; b--)
                    if (_li[b] < _lapsed[b].Count) { c = _lapsed[b][_li[b]++]; return true; }
                c = null;
                return false;
            }
        }

        #endregion

        #region Status file

        /// <summary>The launcher's view (population-status.json, protocol 1).</summary>
        public PopulationStatusReport BuildStatus(DateTime now)
        {
            if (now >= _nextArchiveCountUtc)
            {
                _nextArchiveCountUtc = now + ArchiveCountInterval;
                foreach (RealmState r in _realms.Values)
                {
                    try
                    {
                        r.ArchivedTotal = _archive.Count(r.Realm, 1, 50);
                        for (int b = 0; b < BracketCount; b++) r.ArchivedByBracket[b] = _archive.Count(r.Realm, BracketMin[b], BracketMax[b]);
                    }
                    catch (Exception exception)
                    {
                        LogError($"Could not count archived bots of {PopulationGoalSettings.RealmName(r.Realm)}", exception);
                    }
                }
            }

            var realms = new List<RealmPopulationStatus>();
            PopulationGoalSettings s = Settings;
            foreach (eRealm realm in Realms)
            {
                RealmState r = _realms[realm];
                var liveB = new int[BracketCount];
                int live;
                if (_enabled)
                {
                    foreach (ManagedBot m in r.Live.Values) liveB[BracketOf(m.Level)]++;
                    live = r.Live.Count;
                }
                else
                {
                    _liveBuffer.Clear();
                    _lifecycle.LiveBots(realm, _liveBuffer);
                    foreach (LiveBotInfo b in _liveBuffer) liveB[BracketOf(b.Level)]++;
                    live = _liveBuffer.Count;
                }
                var brackets = new BracketStatus[BracketCount];
                for (int b = 0; b < BracketCount; b++)
                    brackets[b] = new BracketStatus(BracketMin[b], BracketMax[b], liveB[b], r.ArchivedByBracket[b],
                        _enabled ? r.Effective[b] : 0, _enabled ? r.Floor : 0);
                realms.Add(new RealmPopulationStatus(realm, s.GoalFor(realm), live,
                    _enabled ? InboundCount(realm) : 0, _enabled ? LeavingCount(realm) : 0, r.ArchivedTotal,
                    r.Created.Total(now), r.Archived.Total(now), r.Returned.Total(now), r.Retired.Total(now), brackets));
            }
            string error = GoalError;
            if (_enabled && !_lifecycle.Ready)
                error ??= "Waiting for the bot archive to be ready.";
            if (_enabled && !_populationEnabled)
                error = "Bots are off (population_enabled = false): no bot logs in until a goal above 0 turns them on.";
            if (!_enabled)
                error = "The population scheduler is off (population_scheduler_enabled = false): every saved bot is online.";
            long saved = 0;
            try { saved = _archive.SavedCount; } catch (Exception exception) { LogError("Could not read the saved bot count", exception); }
            return new PopulationStatusReport(PopulationStatusReport.CurrentProtocol, now, AppliedRevision, error,
                realms.ToArray(), saved, _savedCap);
        }

        private void WriteStatus(DateTime now)
        {
            try
            {
                PopulationStatusReport.Save(_stateDir, BuildStatus(now));
            }
            catch (Exception exception)
            {
                LogError("Could not write population-status.json", exception);
            }
        }

        #endregion
    }

    /// <summary>Cheap file-change check of population-goals.json (time stamp and length), re-read only when it changed.</summary>
    public sealed class PopulationGoalWatcher
    {
        private readonly string _stateDir;
        private long _stamp = long.MinValue;
        private long _length = long.MinValue;

        public PopulationGoalWatcher(string stateDir) => _stateDir = stateDir;

        /// <summary>True when the file changed: settings are the new goals (null if bad) and error says why it was refused.</summary>
        public bool Poll(out PopulationGoalSettings settings, out string error)
        {
            settings = null;
            error = null;
            string path = PopulationGoalSettings.PathIn(_stateDir);
            var info = new FileInfo(path);
            long stamp = info.Exists ? info.LastWriteTimeUtc.Ticks : 0;
            long length = info.Exists ? info.Length : -1;
            if (stamp == _stamp && length == _length) return false;
            _stamp = stamp;
            _length = length;
            if (PopulationGoalSettings.TryLoad(_stateDir, out PopulationGoalSettings loaded, out string loadError))
            {
                settings = loaded;
                return true;
            }
            error = loadError ?? "population-goals.json could not be read.";
            return true;
        }
    }
}
