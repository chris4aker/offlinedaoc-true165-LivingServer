using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.Tests.Unit.Population
{
    // Fakes for CORE's lifecycle and archive: bots log in and out at once, the archive is a dictionary.
    public sealed class FakePopulationClock : IPopulationClock
    {
        public DateTime UtcNow { get; set; } = new DateTime(2026, 11, 9, 0, 0, 1, DateTimeKind.Utc);   // a Monday
    }

    public sealed class FakeBotRecord
    {
        public long Id;
        public eRealm Realm;
        public int Level;
        public BotPopulationProfile Profile;
    }

    public sealed class FakePopulationArchive : IPopulationArchive
    {
        public readonly Dictionary<long, FakeBotRecord> Bots = new();
        public Func<long, bool> IsOnline = _ => false;
        public long SavedOffset;
        public readonly List<long> RetiredIds = new();
        public int SaveProfileCalls;
        private long _nextId = 1;
        private readonly Random _rng = new(5);

        public FakeBotRecord Add(eRealm realm, int level, BotPopulationProfile profile = null, DateTime? now = null)
        {
            var r = new FakeBotRecord
            {
                Id = _nextId++, Realm = realm, Level = level,
                Profile = profile ?? new BotPopulationProfile(1, 0x7F, (now ?? new DateTime(2026, 11, 8)).AddHours(-1), DateTime.MinValue, 0, 0, DateTime.MinValue, 0, false),
            };
            Bots[r.Id] = r;
            return r;
        }

        public long SavedCount => SavedOffset + Bots.Count;

        public long Count(eRealm realm, int minLevel, int maxLevel) =>
            Bots.Values.Count(b => b.Realm == realm && b.Level >= minLevel && b.Level <= maxLevel && !IsOnline(b.Id));

        public IReadOnlyList<ArchiveCandidate> Candidates(eRealm realm, int minLevel, int maxLevel, int limit, DateTime wantBeforeUtc) =>
            Bots.Values.Where(b => b.Realm == realm && b.Level >= minLevel && b.Level <= maxLevel && !IsOnline(b.Id) && b.Profile.NextWantUtc < wantBeforeUtc)
                .OrderBy(b => b.Profile.NextWantUtc).Take(limit).Select(ToCandidate).ToList();

        public static ArchiveCandidate ToCandidate(FakeBotRecord b) => new(b.Id, b.Realm, b.Level, 1, DateTime.MinValue, b.Profile);

        public void SaveProfile(long botId, BotPopulationProfile profile)
        {
            SaveProfileCalls++;
            if (Bots.TryGetValue(botId, out FakeBotRecord r)) r.Profile = profile;
        }

        public BotPopulationProfile GetProfile(long botId) => Bots.TryGetValue(botId, out FakeBotRecord r) ? r.Profile : null;

        public IReadOnlyList<long> RandomRetireCandidates(int count, Func<ArchiveCandidate, bool> eligible)
        {
            var result = new List<long>();
            foreach (FakeBotRecord b in Bots.Values.Where(b => !IsOnline(b.Id)).OrderBy(_ => _rng.Next()))
            {
                if (result.Count >= count) break;
                if (eligible(ToCandidate(b))) result.Add(b.Id);
            }
            return result;
        }

        public void Retire(IReadOnlyCollection<long> botIds)
        {
            foreach (long id in botIds)
            {
                Bots.Remove(id);
                RetiredIds.Add(id);
            }
        }
    }

    public sealed class FakePopulationLifecycle : IPopulationLifecycle
    {
        private readonly FakePopulationArchive _archive;
        private readonly FakePopulationClock _clock;
        public readonly Dictionary<long, FakeBotRecord> Live = new();
        public readonly List<DateTime> LoginTimes = new();
        public readonly List<(DateTime Time, long Id, BotLogoutReason Reason)> Logouts = new();
        public readonly HashSet<long> RefuseLogout = new();
        public readonly Dictionary<long, BotEngagement> Engagement = new();
        public int Created;
        public bool Ready { get; set; } = true;
        public event Action<long> LogoutRefused;

        public FakePopulationLifecycle(FakePopulationArchive archive, FakePopulationClock clock)
        {
            _archive = archive;
            _clock = clock;
            archive.IsOnline = id => Live.ContainsKey(id);
        }

        public void PutOnline(FakeBotRecord r) => Live[r.Id] = r;

        public void LiveBots(eRealm realm, List<LiveBotInfo> into)
        {
            foreach (FakeBotRecord r in Live.Values)
                if (r.Realm == realm) into.Add(new LiveBotInfo(r.Id, r.Level, Engagement.TryGetValue(r.Id, out BotEngagement e) ? e : BotEngagement.Idle));
        }

        public bool IsOnline(long botId) => Live.ContainsKey(botId);

        public bool RequestLogin(long botId, BotLoginPriority priority)
        {
            if (!_archive.Bots.TryGetValue(botId, out FakeBotRecord r) || Live.ContainsKey(botId)) return false;
            Live[botId] = r;
            LoginTimes.Add(_clock.UtcNow);
            return true;
        }

        public bool RequestLogout(long botId, BotLogoutReason reason)
        {
            if (RefuseLogout.Contains(botId) || !Live.ContainsKey(botId)) return false;
            Live.Remove(botId);
            Logouts.Add((_clock.UtcNow, botId, reason));
            return true;
        }

        public void RaiseRefused(long id) => LogoutRefused?.Invoke(id);

        public long CreateLevelOne(eRealm realm)
        {
            Created++;
            return _archive.Add(realm, 1, now: _clock.UtcNow).Id;
        }

        public int LiveCount(eRealm realm) => Live.Values.Count(r => r.Realm == realm);
    }

    public sealed class FakePopulationWorld : IPopulationWorld
    {
        public readonly HashSet<long> Hired = new(), Guildmates = new(), Exempt = new();
        public bool IsHired(long botId) => Hired.Contains(botId);
        public bool IsPlayerGuildmate(long botId) => Guildmates.Contains(botId);
        public bool IsGoalExempt(long botId) => Exempt.Contains(botId);
    }

    [TestFixture]
    public class UT_PopulationBrackets
    {
        [Test]
        public void DefaultTargetsAreTheSharesOfTheGoal()
        {
            Assert.That(AutonomousPopulationScheduler.BracketTargets(500, 35, 4), Is.EqualTo(new[] { 60, 65, 65, 65, 70, 175 }));
            Assert.That(AutonomousPopulationScheduler.BracketTargets(2000, 35, 4), Is.EqualTo(new[] { 240, 260, 260, 260, 280, 700 }));
            Assert.That(AutonomousPopulationScheduler.BracketTargets(0, 35, 4), Is.EqualTo(new int[6]));
        }

        [Test]
        public void TargetsAlwaysAddUpToTheGoalAndKeepTheirFloors()
        {
            foreach (int goal in new[] { 1, 5, 7, 12, 13, 30, 99, 101, 333, 500, 1499, 5000 })
            foreach ((int cap, int floor) in new[] { (35, 4), (50, 10), (0, 4), (80, 4), (20, 0) })
            {
                int[] t = AutonomousPopulationScheduler.BracketTargets(goal, cap, floor);
                Assert.That(t.Sum(), Is.EqualTo(goal), $"goal {goal} cap {cap} floor {floor}");
                int f = AutonomousPopulationScheduler.FloorCount(goal, floor);
                for (int b = 0; b < 5; b++) Assert.That(t[b], Is.GreaterThanOrEqualTo(f), $"goal {goal} bracket {b}");
                if (cap == 0) Assert.That(t[5], Is.EqualTo(0));
            }
        }

        [Test]
        public void FloorIsTwoOrFourPercentNeverMoreThanASixth()
        {
            Assert.That(AutonomousPopulationScheduler.FloorCount(500, 4), Is.EqualTo(20));
            Assert.That(AutonomousPopulationScheduler.FloorCount(30, 4), Is.EqualTo(2));
            Assert.That(AutonomousPopulationScheduler.FloorCount(10, 4), Is.EqualTo(1));
            Assert.That(AutonomousPopulationScheduler.FloorCount(0, 4), Is.EqualTo(0));
            Assert.That(AutonomousPopulationScheduler.FloorCount(500, 0), Is.EqualTo(0));
        }

        [Test]
        public void SharesFollowTheLevel50CapAndFloors()
        {
            double[] s = AutonomousPopulationScheduler.BracketShares(35, 4);
            Assert.That(s, Is.EqualTo(new double[] { 12, 13, 13, 13, 14, 35 }).Within(1e-9));
            double[] high = AutonomousPopulationScheduler.BracketShares(80, 4);
            Assert.That(high.Sum(), Is.EqualTo(100).Within(1e-9));
            Assert.That(high.Take(5), Is.All.EqualTo(4).Within(1e-9));
        }

        [Test]
        public void UnfillableSharesCascadeDownward()
        {
            const int big = 1_000_000_000;
            int[] eff = AutonomousPopulationScheduler.Cascade(new[] { 60, 65, 65, 65, 70, 175 }, new[] { big, 1000, 30, 0, 0, 0 });
            Assert.That(eff, Is.EqualTo(new[] { 60, 410, 30, 0, 0, 0 }));
            int[] none = AutonomousPopulationScheduler.Cascade(new[] { 60, 65, 65, 65, 70, 175 }, new[] { big, 0, 0, 0, 0, 0 });
            Assert.That(none, Is.EqualTo(new[] { 500, 0, 0, 0, 0, 0 }));
            int[] all = AutonomousPopulationScheduler.Cascade(new[] { 60, 65, 65, 65, 70, 175 }, new[] { big, big, big, big, big, big });
            Assert.That(all, Is.EqualTo(new[] { 60, 65, 65, 65, 70, 175 }));
        }

        [Test]
        public void HysteresisRampAndBudgets()
        {
            Assert.That(AutonomousPopulationScheduler.Hysteresis(10), Is.EqualTo(2));
            Assert.That(AutonomousPopulationScheduler.Hysteresis(500), Is.EqualTo(25));
            Assert.That(AutonomousPopulationScheduler.RampStep(500, 1000, 60), Is.EqualTo(20).Within(1e-9));
            Assert.That(AutonomousPopulationScheduler.RampStep(10, 0, 60), Is.EqualTo(1).Within(1e-9));
            Assert.That(AutonomousPopulationScheduler.NewPer30Minutes(500), Is.EqualTo(5));
            Assert.That(AutonomousPopulationScheduler.NewPer30Minutes(0), Is.EqualTo(0));
            Assert.That(AutonomousPopulationScheduler.SwapsPer30Minutes(500), Is.EqualTo(20));
        }

        [TestCase(1, 0)] [TestCase(9, 0)] [TestCase(10, 1)] [TestCase(19, 1)] [TestCase(20, 2)] [TestCase(39, 3)]
        [TestCase(40, 4)] [TestCase(49, 4)] [TestCase(50, 5)]
        public void Brackets(int level, int bracket) => Assert.That(AutonomousPopulationScheduler.BracketOf(level), Is.EqualTo(bracket));
    }

    [TestFixture]
    public class UT_PopulationPersonas
    {
        [Test]
        public void NewProfilesFollowThePersonaMix()
        {
            var rng = new Random(1);
            var counts = new int[4];
            DateTime now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < 20000; i++) counts[PopulationPersonas.NewProfile(rng, now).Persona]++;
            for (int p = 0; p < 4; p++)
                Assert.That(counts[p] / 20000.0, Is.EqualTo(PopulationPersonas.Mix[p]).Within(0.02));
        }

        [Test]
        public void NextPlayTimeLandsOnAPlayDay()
        {
            var rng = new Random(2);
            DateTime now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
            byte wednesdayOnly = (byte)(1 << (int)DayOfWeek.Wednesday);
            for (int i = 0; i < 200; i++)
            {
                DateTime next = PopulationPersonas.NextWant(PopulationPersona.Casual, wednesdayOnly, now, rng);
                Assert.That(next.DayOfWeek, Is.EqualTo(DayOfWeek.Wednesday));
                Assert.That(next, Is.GreaterThan(now));
            }
        }

        [Test]
        public void SessionsBookMinutesAndQuittersComeBackLater()
        {
            var rng = new Random(3);
            DateTime login = new(2026, 11, 9, 10, 0, 0, DateTimeKind.Utc);
            var p = new BotPopulationProfile((byte)PopulationPersona.Newcomer, 0x7F, login, login, 0, 30, login.Date, 0, false);
            BotPopulationProfile started = PopulationPersonas.StartSession(p, login);
            Assert.That(started.WasOnline, Is.True);
            int quits = 0;
            for (int i = 0; i < 2000; i++)
            {
                BotPopulationProfile ended = PopulationPersonas.EndSession(started, login, login.AddMinutes(90), false, rng);
                Assert.That(ended.WasOnline, Is.False);
                Assert.That(ended.TodayMinutes, Is.EqualTo(120));
                Assert.That(ended.PlayedMinutes, Is.EqualTo(90));
                if (ended.Status == 1)
                {
                    quits++;
                    Assert.That(ended.ReturnUtc, Is.GreaterThan(login.AddMinutes(90)));
                    Assert.That(ended.NextWantUtc, Is.EqualTo(ended.ReturnUtc));
                }
            }
            // Newcomers: 4% a day over a gap of about 60 h, so roughly 10% quit per session.
            Assert.That(quits, Is.InRange(80, 400));
        }

        [Test]
        public void GuildmatesPlayRegularOrBetter()
        {
            Assert.That(PopulationPersonas.Effective((byte)PopulationPersona.Newcomer, true), Is.EqualTo(PopulationPersona.Regular));
            Assert.That(PopulationPersonas.Effective((byte)PopulationPersona.Veteran, true), Is.EqualTo(PopulationPersona.Veteran));
            Assert.That(PopulationPersonas.Effective((byte)PopulationPersona.Casual, false), Is.EqualTo(PopulationPersona.Casual));
        }
    }

    [TestFixture]
    public class UT_PopulationScheduler
    {
        private FakePopulationClock _clock;
        private FakePopulationArchive _archive;
        private FakePopulationLifecycle _life;
        private FakePopulationWorld _world;
        private AutonomousPopulationScheduler _s;
        private int _minLiveAlbion;

        [SetUp]
        public void Setup()
        {
            _clock = new FakePopulationClock();
            _archive = new FakePopulationArchive();
            _life = new FakePopulationLifecycle(_archive, _clock);
            _world = new FakePopulationWorld();
        }

        private void Start(int alb, int mid, int hib, string rotation = "Normal", string stateDir = null)
        {
            _s = new AutonomousPopulationScheduler(_life, _archive, _world, _clock, new Random(11), stateDir);
            if (stateDir == null)
                _s.ApplySettings(PopulationGoalSettings.Default with { Albion = alb, Midgard = mid, Hibernia = hib, Rotation = rotation }, null);
        }

        private void Run(int seconds, bool trackMin = false)
        {
            for (int i = 0; i < seconds; i++)
            {
                _clock.UtcNow = _clock.UtcNow.AddSeconds(1);
                _s.Tick();
                if (trackMin) _minLiveAlbion = Math.Min(_minLiveAlbion, _life.LiveCount(eRealm.Albion));
            }
        }

        private void AddArchived(eRealm realm, int count, int minLevel, int maxLevel, Random rng = null)
        {
            rng ??= new Random(9);
            for (int i = 0; i < count; i++)
            {
                BotPopulationProfile p = PopulationPersonas.NewProfile(rng, _clock.UtcNow.AddHours(-rng.Next(1, 48)));
                _archive.Add(realm, rng.Next(minLevel, maxLevel + 1), p);
            }
        }

        private void AddLive(eRealm realm, int count, int level)
        {
            for (int i = 0; i < count; i++) _life.PutOnline(_archive.Add(realm, level, now: _clock.UtcNow));
        }

        private int MaxPerSecond(IEnumerable<DateTime> times) =>
            times.GroupBy(t => t.Ticks / TimeSpan.TicksPerSecond).Select(g => g.Count()).DefaultIfEmpty(0).Max();

        [Test]
        public void FirstStartFromTheArchiveIsSmoothAndRateLimited()
        {
            // Today's world: 1,500 saved bots at levels 10-19, nobody online after the restart, goals 500/500/500.
            foreach (eRealm realm in AutonomousPopulationScheduler.Realms) AddArchived(realm, 500, 10, 19);
            Start(500, 500, 500);

            Run(5 * 60);
            Assert.That(_life.Logouts, Is.Empty, "no logouts in the first minutes");
            Assert.That(_life.LoginTimes.Count, Is.LessThanOrEqualTo(2 * 5 * 60));
            Run(15 * 60);

            Assert.That(MaxPerSecond(_life.LoginTimes), Is.LessThanOrEqualTo(2));
            Assert.That(MaxPerSecond(_life.Logouts.Select(x => x.Time)), Is.LessThanOrEqualTo(2));
            foreach (eRealm realm in AutonomousPopulationScheduler.Realms)
            {
                Assert.That(_life.LiveCount(realm), Is.InRange(490, 505), realm.ToString());
                // The 1-9 bracket (12%) fills with new level 1s; the rest are the saved bots.
                Assert.That(_s.LiveByBracket(realm)[0], Is.InRange(50, 70), realm + " level 1-9");
            }
            Assert.That(_life.Created, Is.InRange(150, 210));
        }

        [Test]
        public void CrashRecoveryLogsInBotsThatWereOnlineFirst()
        {
            AddArchived(eRealm.Albion, 300, 10, 19);
            Start(20, 0, 0);
            Run(60);
            var before = _life.Live.Keys.ToHashSet();
            Assert.That(before.Count, Is.EqualTo(20));

            // Crash: everyone vanishes without a clean logout, a new scheduler starts.
            _life.Live.Clear();
            Start(20, 0, 0);
            Run(60);
            Assert.That(_life.Live.Keys.ToHashSet(), Is.EquivalentTo(before));
        }

        [Test]
        public void TakingOverAboveTheGoalRampsDownWithoutAMassLogout()
        {
            AddLive(eRealm.Albion, 600, 15);
            Start(500, 0, 0);
            Run(60);
            Assert.That(_life.LiveCount(eRealm.Albion), Is.GreaterThanOrEqualTo(585), "about 2% of the goal a minute");
            Run(11 * 60);
            Assert.That(_life.LiveCount(eRealm.Albion), Is.InRange(495, 505));
            Assert.That(MaxPerSecond(_life.Logouts.Select(x => x.Time)), Is.LessThanOrEqualTo(2));
        }

        [Test]
        public void GoalRaiseRampsAboutTwoPercentAMinute()
        {
            AddArchived(eRealm.Albion, 2000, 10, 19);
            Start(500, 0, 0);
            Run(6 * 60);
            Assert.That(_life.LiveCount(eRealm.Albion), Is.InRange(495, 505));
            _s.ApplySettings(_s.Settings with { Albion = 1000, Revision = 2 }, null);
            Run(60);
            Assert.That(_s.RampedGoal(eRealm.Albion), Is.InRange(512.0, 528.0));
            Run(60);
            Assert.That(_life.LiveCount(eRealm.Albion), Is.InRange(520, 545));
            Run(30 * 60);
            Assert.That(_life.LiveCount(eRealm.Albion), Is.InRange(990, 1005));
        }

        [Test]
        public void GoalZeroEmptiesTheRealmOverTheRamp()
        {
            AddLive(eRealm.Albion, 500, 15);
            Start(500, 0, 0);
            Run(30);
            _s.ApplySettings(_s.Settings with { Albion = 0 }, null);
            Run(60);
            Assert.That(_life.LiveCount(eRealm.Albion), Is.GreaterThanOrEqualTo(485));
            Run(60 * 60);
            Assert.That(_life.LiveCount(eRealm.Albion), Is.EqualTo(0));
            Assert.That(_life.Logouts.Select(x => x.Reason), Has.Some.EqualTo(BotLogoutReason.GoalLowered));
        }

        [Test]
        public void SwapsHappenOnlyWithAReplacementAndTheTotalNeverDips()
        {
            AddLive(eRealm.Albion, 100, 15);
            Start(100, 0, 0);
            _minLiveAlbion = int.MaxValue;
            Run(10 * 60, trackMin: true);
            // Nobody can replace them (no saved bots, no new level-1 budget yet): nobody leaves.
            Assert.That(_life.Logouts, Is.Empty);
            Assert.That(_minLiveAlbion, Is.EqualTo(100));

            AddArchived(eRealm.Albion, 40, 2, 8);
            Run(30 * 60, trackMin: true);
            Assert.That(_life.Logouts.Select(x => x.Reason), Has.Some.EqualTo(BotLogoutReason.Rebalance));
            Assert.That(_minLiveAlbion, Is.GreaterThanOrEqualTo(98));
            Assert.That(_s.LiveByBracket(eRealm.Albion)[0], Is.InRange(8, 16));
        }

        [Test]
        public void NoSwapInsideTheHysteresis()
        {
            AddLive(eRealm.Albion, 14, 5);    // bracket 1-9 target 12: over by 2, inside the hysteresis
            AddLive(eRealm.Albion, 86, 15);
            AddArchived(eRealm.Albion, 50, 10, 19);
            Start(100, 0, 0, rotation: "Normal");
            Run(10 * 60);
            Assert.That(_life.Logouts.Select(x => x.Reason), Has.None.EqualTo(BotLogoutReason.Rebalance));
        }

        [Test]
        public void IrreplaceableBotsPlayOnButNeverPastTheirDailyCap()
        {
            AddLive(eRealm.Albion, 10, 15);
            Start(10, 0, 0);
            _s.ApplySettings(_s.Settings with { Albion = 10, BracketFloorPercent = 0, Level50MaxPercent = 0 }, null);
            Run(60);
            var originals = _life.Live.Keys.ToHashSet();
            // No saved bot can replace them and the new level-1 budget (1 per 30 minutes here) is not there yet:
            // sessions that end become one more pull.
            Run(28 * 60);
            Assert.That(_life.Logouts, Is.Empty);
            Run(17 * 60 * 60);
            Assert.That(originals.Where(id => _life.Live.ContainsKey(id)), Is.Empty, "16 h is the longest daily cap");
        }

        [Test]
        public void HornGuestsAndHiredCompanionsAreNeverCountedOrLoggedOut()
        {
            AddLive(eRealm.Albion, 10, 15);
            Start(10, 0, 0);
            Run(30);
            FakeBotRecord guest = _archive.Add(eRealm.Albion, 50, now: _clock.UtcNow);
            _life.PutOnline(guest);
            FakeBotRecord hired = _archive.Add(eRealm.Albion, 20, now: _clock.UtcNow);
            _world.Hired.Add(hired.Id);
            _life.PutOnline(hired);
            Run(20 * 60);
            Assert.That(_s.ManagedLive(eRealm.Albion), Is.EqualTo(10));
            Assert.That(_life.Live.ContainsKey(guest.Id), Is.True);
            Assert.That(_life.Live.ContainsKey(hired.Id), Is.True);
            Assert.That(_life.Logouts.Select(x => x.Id), Has.None.EqualTo(guest.Id).And.None.EqualTo(hired.Id));
        }

        [Test]
        public void RefusedLogoutsPickAnotherBot()
        {
            AddLive(eRealm.Albion, 50, 15);
            foreach (long id in _life.Live.Keys.Take(25)) _life.RefuseLogout.Add(id);
            Start(50, 0, 0);
            Run(20);
            _s.ApplySettings(_s.Settings with { Albion = 30 }, null);
            Run(30 * 60);
            Assert.That(_life.LiveCount(eRealm.Albion), Is.InRange(29, 31));
            Assert.That(_life.RefuseLogout.All(id => _life.Live.ContainsKey(id)), Is.True);
        }

        [Test]
        public void BotGoalsAreLeftAloneAndIdleBotsLeaveBeforeGroupedOnes()
        {
            string dir = Path.Combine(Path.GetTempPath(), "population-botgoals-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                // The launcher's Bot Goals file sits beside the population files; the scheduler never reads or writes it.
                string botGoals = Path.Combine(dir, OfflineDaoc.Configuration.BotGoalSettings.FileName);
                OfflineDaoc.Configuration.BotGoalSettings.Defaults.Save(botGoals, () => true);
                byte[] before = File.ReadAllBytes(botGoals);
                DateTime stamp = File.GetLastWriteTimeUtc(botGoals);
                PopulationGoalSettings.Save(dir, PopulationGoalSettings.Default with { Revision = 1, Albion = 40, Midgard = 0, Hibernia = 0 });

                AddLive(eRealm.Albion, 40, 15);
                var grouped = _life.Live.Keys.Take(20).ToHashSet();
                foreach (long id in grouped) _life.Engagement[id] = BotEngagement.OnObjective;
                Start(0, 0, 0, stateDir: dir);
                Run(30);
                PopulationGoalSettings.Save(dir, PopulationGoalSettings.Default with { Revision = 2, Albion = 20, Midgard = 0, Hibernia = 0 });
                Run(30 * 60);

                Assert.That(_life.LiveCount(eRealm.Albion), Is.InRange(19, 21));
                Assert.That(grouped.All(id => _life.Live.ContainsKey(id)), Is.True, "bots on group/RvR/battleground objectives stay");
                Assert.That(_life.Logouts.Select(x => x.Id), Has.None.Matches<long>(id => grouped.Contains(id)));
                Assert.That(File.ReadAllBytes(botGoals), Is.EqualTo(before));
                Assert.That(File.GetLastWriteTimeUtc(botGoals), Is.EqualTo(stamp));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void StatusIsStillWrittenWhenOffAndPopulationEnabledIsRespected()
        {
            string dir = Path.Combine(Path.GetTempPath(), "population-off-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                AddArchived(eRealm.Albion, 50, 10, 19);
                Start(0, 0, 0, stateDir: dir);
                _s.SetPopulationEnabled(false);
                Run(20);
                Assert.That(_life.LoginTimes, Is.Empty, "population_enabled off: nobody logs in");
                Assert.That(PopulationStatusReport.TryLoad(dir, out PopulationStatusReport report, out string error), Is.True, error);
                Assert.That(report.Error, Does.Contain("population_enabled"));

                _s.SetPopulationEnabled(true);
                _s.SetEnabled(false);
                Run(20);
                Assert.That(_life.LoginTimes, Is.Empty, "scheduler off: nobody logs in");
                Assert.That(PopulationStatusReport.TryLoad(dir, out report, out error), Is.True, error);
                Assert.That(report.Error, Does.Contain("scheduler is off"));
                Assert.That(report.UpdatedUtc, Is.GreaterThan(_clock.UtcNow.AddSeconds(-16)));

                _s.SetEnabled(true);
                Run(30);
                Assert.That(_life.LoginTimes, Is.Not.Empty);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void NothingHappensUntilTheArchiveIsReady()
        {
            AddArchived(eRealm.Albion, 100, 10, 19);
            _life.Ready = false;
            Start(100, 0, 0);
            Run(120);
            Assert.That(_life.LoginTimes, Is.Empty);
            Assert.That(_life.Created, Is.EqualTo(0));
            _life.Ready = true;
            Run(120);
            Assert.That(_life.LoginTimes, Is.Not.Empty);
        }

        [Test]
        public void TurningTheSchedulerOffStopsAllWork()
        {
            AddArchived(eRealm.Albion, 100, 10, 19);
            Start(100, 0, 0);
            _s.SetEnabled(false);
            Run(120);
            Assert.That(_life.LoginTimes, Is.Empty);
        }

        [Test]
        public void StatusFileAndLiveGoalReload()
        {
            string dir = Path.Combine(Path.GetTempPath(), "population-status-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                AddArchived(eRealm.Albion, 50, 10, 19);
                Start(0, 0, 0, stateDir: dir);
                _s.SetSavedCap(1_000_000);
                Run(2);
                Assert.That(_s.Settings.Albion, Is.EqualTo(500), "missing file = the 500/500/500 default");
                Assert.That(PopulationStatusReport.TryLoad(dir, out PopulationStatusReport report, out string error), Is.True, error);
                Assert.That(report.Protocol, Is.EqualTo(1));
                Assert.That(report.Realms, Has.Length.EqualTo(3));
                Assert.That(report.Realms.All(r => r.Brackets.Length == 6), Is.True);
                Assert.That(report.SavedCap, Is.EqualTo(1_000_000));
                Assert.That(report.Realms[0].Brackets[5].MinLevel, Is.EqualTo(50));

                PopulationGoalSettings.Save(dir, PopulationGoalSettings.Default with { Revision = 42, Albion = 30, Midgard = 0, Hibernia = 0 });
                Run(1);
                Assert.That(_s.Settings.Albion, Is.EqualTo(30));
                Assert.That(_s.AppliedRevision, Is.EqualTo(42));
                Run(120);
                Assert.That(PopulationStatusReport.TryLoad(dir, out report, out error), Is.True, error);
                Assert.That(report.AppliedRevision, Is.EqualTo(42));
                RealmPopulationStatus alb = report.For(eRealm.Albion);
                Assert.That(alb.Goal, Is.EqualTo(30));
                Assert.That(alb.Live + alb.LoggingIn, Is.EqualTo(30));
                Assert.That(alb.Brackets.Sum(b => b.Target), Is.EqualTo(30));
                Assert.That(alb.Brackets.Sum(b => b.Live), Is.EqualTo(alb.Live));
                Assert.That(report.Error, Is.Null);

                // A bad file keeps the last good goals and reports why.
                File.WriteAllText(PopulationGoalSettings.PathIn(dir), "{ \"Version\": 1, \"Albion\": 999999 }");
                Run(16);
                Assert.That(_s.Settings.Albion, Is.EqualTo(30));
                Assert.That(_s.GoalError, Is.Not.Null);
                Assert.That(PopulationStatusReport.TryLoad(dir, out report, out _), Is.True);
                Assert.That(report.Error, Is.Not.Null);
                Assert.That(report.AppliedRevision, Is.EqualTo(42));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [TestFixture]
    public class UT_PopulationRetirement
    {
        private FakePopulationClock _clock;
        private FakePopulationArchive _archive;
        private FakePopulationLifecycle _life;
        private FakePopulationWorld _world;
        private readonly HashSet<long> _busy = new();
        private int _live50Target;
        private long _cap;
        private PopulationRetirement _r;
        private readonly List<DateTime> _batchTimes = new();

        [SetUp]
        public void Setup()
        {
            _clock = new FakePopulationClock();
            _archive = new FakePopulationArchive();
            _life = new FakePopulationLifecycle(_archive, _clock);
            _world = new FakePopulationWorld();
            _busy.Clear();
            _batchTimes.Clear();
            _live50Target = 0;
            _cap = 1000;
            _r = new PopulationRetirement(_archive, _life, _world, id => _busy.Contains(id), _ => _live50Target, () => _cap, _clock,
                new Random(4), null);
        }

        private void Fill(int count, int level, eRealm realm = eRealm.Albion)
        {
            for (int i = 0; i < count; i++) _archive.Add(realm, level);
        }

        private int Run(int seconds)
        {
            int total = 0;
            for (int i = 0; i < seconds; i++)
            {
                _clock.UtcNow = _clock.UtcNow.AddSeconds(1);
                int n = _r.Tick();
                if (n > 0) _batchTimes.Add(_clock.UtcNow);
                Assert.That(n, Is.LessThanOrEqualTo(PopulationRetirement.BatchSize));
                total += n;
            }
            return total;
        }

        [Test]
        public void NothingIsRetiredBelowTheCap()
        {
            Fill(980, 20);
            Assert.That(Run(60), Is.EqualTo(0));
            Assert.That(_r.Engaged, Is.False);
        }

        [Test]
        public void AtTheCapRetiresDownTo99PercentInBatchesThenMatchesInflow()
        {
            Fill(1100, 20);
            Run(200);
            Assert.That(_archive.SavedCount, Is.EqualTo(PopulationRetirement.TargetFor(1000)));
            Assert.That(_r.Engaged, Is.True);
            for (int i = 1; i < _batchTimes.Count; i++)
                Assert.That((_batchTimes[i] - _batchTimes[i - 1]).TotalSeconds, Is.InRange(2, 6));

            Fill(5, 20);   // new arrivals
            Run(30);
            Assert.That(_archive.SavedCount, Is.EqualTo(990));
        }

        [Test]
        public void ProtectedBotsAreNeverRetired()
        {
            Fill(1200, 20);
            var ids = _archive.Bots.Keys.Take(40).ToList();
            _world.Hired.UnionWith(ids.Take(10));
            _world.Guildmates.UnionWith(ids.Skip(10).Take(10));
            _busy.UnionWith(ids.Skip(20).Take(10));
            foreach (long id in ids.Skip(30).Take(10)) _life.PutOnline(_archive.Bots[id]);
            _cap = 300;   // retire most of them
            Run(2000);
            Assert.That(ids.All(id => _archive.Bots.ContainsKey(id)), Is.True);
            Assert.That(_archive.RetiredIds.Intersect(ids), Is.Empty);
        }

        [Test]
        public void Level50sHaveAQuarterOfTheOdds()
        {
            Fill(4000, 50);
            Fill(4000, 25);
            _cap = 7000;   // engaged, retire to 6930: 1,070 bots
            Run(500);
            int retired50 = _archive.RetiredIds.Count(id => id <= 4000);
            double share = retired50 / (double)_archive.RetiredIds.Count;
            Assert.That(_archive.RetiredIds.Count, Is.EqualTo(1070));
            Assert.That(share, Is.InRange(0.15, 0.25), "0.25 / (1 + 0.25) = 20%");
        }

        [Test]
        public void TheOfflineLevel50ReserveIsNeverTouched()
        {
            Fill(100, 50, eRealm.Albion);
            Fill(100, 50, eRealm.Midgard);
            _live50Target = 60;   // reserve 90 each
            _cap = 100;           // wants to retire 101 bots, only 2 x 10 are allowed
            Run(3000);
            Assert.That(_archive.Count(eRealm.Albion, 50, 50), Is.EqualTo(90));
            Assert.That(_archive.Count(eRealm.Midgard, 50, 50), Is.EqualTo(90));
        }
    }
}
