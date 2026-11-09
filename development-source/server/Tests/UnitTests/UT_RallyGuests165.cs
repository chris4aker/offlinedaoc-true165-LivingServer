using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>Horn of Rallying answer pool (plan 3.7, owner 6b item 8): order, caps, cooldown, partial start, guest release.</summary>
public class UT_RallyGuests165
{
    private sealed class FakeBackend : IRallyGuestBackend
    {
        public bool ArchiveAvailable { get; set; } = true;
        public readonly HashSet<long> Live = new();
        public readonly List<long> LoginRequests = new();
        public readonly List<long> LogoutRequests = new();
        public readonly List<(string Event, long Bot)> Reserved = new();
        public readonly Dictionary<long, string> Guilds = new();
        public readonly HashSet<long> RefuseLogin = new();
        public bool LoginGoesLive;

        public IReadOnlyList<RallyPoolEntry> LoadPool(eRealm realm, int limit) => Array.Empty<RallyPoolEntry>();

        public bool RequestLogin(long botId)
        {
            if (RefuseLogin.Contains(botId)) return false;
            LoginRequests.Add(botId);
            if (LoginGoesLive) Live.Add(botId);
            return true;
        }

        public bool IsLoggingIn(long botId) => false;

        public bool RequestLogout(long botId)
        {
            LogoutRequests.Add(botId);
            return true;
        }

        public bool IsLive(long botId) => Live.Contains(botId);

        public bool Reserve(string eventId, long botId)
        {
            Reserved.Add((eventId, botId));
            return true;
        }

        public string GuildIdOf(long botId) => Guilds.GetValueOrDefault(botId);
    }

    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private static List<RallyPoolEntry> Pool(int count, eRealm realm = eRealm.Albion, long firstId = 1000) =>
        Enumerable.Range(0, count).Select(i => new RallyPoolEntry(firstId + i, realm, Now.AddDays(-30), false)).ToList();

    private static RallyGuestBook Book(FakeBackend backend, int pool = 400, eRealm realm = eRealm.Albion)
    {
        var book = new RallyGuestBook(backend, new Random(1));
        book.SetPool(realm, Pool(pool, realm));
        return book;
    }

    // ---------- selection order ----------

    [Test]
    public void IdleLiveFirstThenArchivedThenBusy()
    {
        RallyAnswer a = RallyAnswerPolicy.Plan(idleLive: 120, archivedFree: 500, busyLive: 200, realmGuests: 0, totalGuests: 0, archiveAllowed: true);
        Assert.That((a.Idle, a.Guests, a.Busy), Is.EqualTo((120, 180, 0)), "archived 50s come before busy live ones");
        a = RallyAnswerPolicy.Plan(120, 100, 200, 0, 0, true);
        Assert.That((a.Idle, a.Guests, a.Busy), Is.EqualTo((120, 100, 80)), "busy live 50s fill what the archive cannot");
        a = RallyAnswerPolicy.Plan(400, 500, 200, 0, 0, true);
        Assert.That((a.Idle, a.Guests, a.Busy, a.Total), Is.EqualTo((300, 0, 0, 300)), "never more than 300");
    }

    [Test]
    public void LiveOnlyWhenTheArchiveMayNotBeCalled()
    {
        RallyAnswer a = RallyAnswerPolicy.Plan(120, 500, 200, 0, 0, archiveAllowed: false);
        Assert.That((a.Idle, a.Guests, a.Busy), Is.EqualTo((120, 0, 180)));
    }

    [Test]
    public void GuestCapsAre340PerRealmAnd600InTotal()
    {
        Assert.That(RallyAnswerPolicy.Plan(0, 500, 0, realmGuests: 300, totalGuests: 300, true).Guests, Is.EqualTo(40));
        Assert.That(RallyAnswerPolicy.Plan(0, 500, 0, realmGuests: 0, totalGuests: 550, true).Guests, Is.EqualTo(50));
        Assert.That(RallyAnswerPolicy.Plan(0, 500, 0, realmGuests: 340, totalGuests: 340, true).Guests, Is.EqualTo(0));
        Assert.That(RallyAnswerPolicy.RealmGuestCap, Is.EqualTo(340));
        Assert.That(RallyAnswerPolicy.TotalGuestCap, Is.EqualTo(600));
    }

    [Test]
    public void SummonNeverPassesTheRealmCap()
    {
        var backend = new FakeBackend();
        RallyGuestBook book = Book(backend, pool: 600);
        Assert.That(book.Summon("a", eRealm.Albion, 300, null, 1, Vector3.Zero, 0, Now), Is.EqualTo(300));
        Assert.That(book.Summon("b", eRealm.Albion, 300, null, 1, Vector3.Zero, 0, Now), Is.EqualTo(40), "340 a realm");
        Assert.That(book.GuestCount(eRealm.Albion), Is.EqualTo(340));
    }

    [Test]
    public void PoolScoreFavoursGuildSteadyRecentAndNearbyBind()
    {
        var hub = new Vector3(10_000, 10_000, 0);
        var plain = new RallyPoolEntry(1, eRealm.Albion, Now.AddDays(-30), false);
        Assert.That(RallyAnswerPolicy.Score(plain, null, "G", 1, hub, Now), Is.EqualTo(0));
        Assert.That(RallyAnswerPolicy.Score(plain, "G", "G", 1, hub, Now), Is.EqualTo(40));
        Assert.That(RallyAnswerPolicy.Score(plain with { SteadyPlayer = true }, null, "G", 1, hub, Now), Is.EqualTo(20));
        Assert.That(RallyAnswerPolicy.Score(plain with { LastSeenUtc = Now.AddDays(-6) }, null, "G", 1, hub, Now), Is.EqualTo(15));
        Assert.That(RallyAnswerPolicy.Score(plain with { BindRegion = 1, BindX = 12_000, BindY = 10_000 }, null, "G", 1, hub, Now), Is.EqualTo(10));
        Assert.That(RallyAnswerPolicy.Score(plain with { BindRegion = 2, BindX = 10_000, BindY = 10_000 }, null, "G", 1, hub, Now), Is.EqualTo(0));
        Assert.That(RallyAnswerPolicy.Score(plain, null, null, 1, hub, Now), Is.EqualTo(0), "no guild, no guild bonus");
    }

    [Test]
    public void PickTakesTheBestScoredFreeArchivedBots()
    {
        var backend = new FakeBackend();
        var book = new RallyGuestBook(backend, new Random(1));
        var pool = Pool(10);
        pool[7] = pool[7] with { SteadyPlayer = true };
        pool[3] = pool[3] with { LastSeenUtc = Now.AddDays(-1) };
        backend.Guilds[pool[5].BotId] = "G";
        backend.Live.Add(pool[5].BotId - 1); // id 1004 is online already
        book.SetPool(eRealm.Albion, pool);
        IReadOnlyList<long> picked = book.Pick(eRealm.Albion, 3, "G", 1, Vector3.Zero, Now);
        Assert.That(picked, Is.EqualTo(new[] { pool[5].BotId, pool[7].BotId, pool[3].BotId }));
        Assert.That(book.ArchivedFree(eRealm.Albion), Is.EqualTo(9), "an online bot is not in the free pool");
    }

    [Test]
    public void SteadyPersonaIsVeteranOrRegular()
    {
        Assert.That(CoreRallyBackend.SteadyPersona(0), Is.True);
        Assert.That(CoreRallyBackend.SteadyPersona(1), Is.True);
        Assert.That(CoreRallyBackend.SteadyPersona(2), Is.False);
        Assert.That(CoreRallyBackend.SteadyPersona(3), Is.False);
        Assert.That(CoreRallyBackend.SteadyPersona(null), Is.False);
    }

    // ---------- partial start ----------

    [Test]
    public void HornRaidStartsAt300OrAt200AfterThreeMinutes()
    {
        const long t200 = 100_000;
        Assert.That(RallyAnswerPolicy.HornReady(300, 300, t200, t200, landed: true), Is.True, "300 staged: at once");
        Assert.That(RallyAnswerPolicy.HornReady(250, 300, t200, t200 + 179_000, true), Is.False, "200+ waits for late arrivals");
        Assert.That(RallyAnswerPolicy.HornReady(250, 300, t200, t200 + 180_000, true), Is.True, "200+ after 3 minutes");
        Assert.That(RallyAnswerPolicy.HornReady(199, 300, t200, t200 + 3_600_000, true), Is.False, "never under 200");
        Assert.That(RallyAnswerPolicy.HornReady(230, 230, t200, t200, true), Is.True, "everyone expected is there: no wait");
        Assert.That(RallyAnswerPolicy.HornReady(300, 300, t200, t200, landed: false), Is.False, "a dragon must still land");
        Assert.That(RallyAnswerPolicy.HornReady(250, 300, 0, 10_000_000, true), Is.False, "the wait starts when 200 are staged");
    }

    [Test]
    public void ExistingForcedStartRuleIsUnchanged()
    {
        Assert.That(RealmRaidRecruitmentPolicy.Ready(true, 0, 200, true), Is.True);
        Assert.That(RealmRaidRecruitmentPolicy.Ready(true, 0, 199, true), Is.False);
    }

    // ---------- cooldown ----------

    [Test, NonParallelizable]
    public void ThirtyMinuteCooldownPerPlayerAndAStopStillCounts()
    {
        string key = "ut-rally-" + Guid.NewGuid();
        Assert.That(RallyGuests165.CooldownRemaining(key, 1_000), Is.EqualTo(0));
        RallyGuests165.NoteSound(key, 1_000);
        // the raid is stopped at once: nothing refunds the cooldown
        RallyGuests165.OnRaidEnded("ut-no-such-event", started: false, 2_000);
        Assert.That(RallyGuests165.CooldownRemaining(key, 2_000), Is.EqualTo(30 * 60_000L - 1_000));
        Assert.That(RallyGuests165.CooldownRemaining(key, 1_000 + 30 * 60_000L), Is.EqualTo(0));
        Assert.That(RallyGuests165.CooldownRemaining("ut-rally-other-" + Guid.NewGuid(), 2_000), Is.EqualTo(0), "per player");
    }

    // ---------- logins ----------

    [Test]
    public void GuestsLogInAtAboutFiveASecond()
    {
        var backend = new FakeBackend();
        RallyGuestBook book = Book(backend);
        book.Summon("e", eRealm.Albion, 100, null, 1, Vector3.Zero, 0, Now);
        book.Pulse(1, Now);
        Assert.That(backend.LoginRequests.Count, Is.EqualTo(5));
        book.Pulse(201, Now);
        Assert.That(backend.LoginRequests.Count, Is.EqualTo(6));
        book.Pulse(1_201, Now);
        Assert.That(backend.LoginRequests.Count, Is.EqualTo(11));
        Assert.That(book.Pending("e"), Is.EqualTo(100), "queued and logging-in guests are still coming");
    }

    [Test]
    public void ALiveGuestJoinsItsRaidReserve()
    {
        var backend = new FakeBackend { LoginGoesLive = true };
        RallyGuestBook book = Book(backend);
        book.Summon("e", eRealm.Albion, 2, null, 1, Vector3.Zero, 0, Now);
        book.Pulse(1, Now);      // logins
        book.Pulse(1_000, Now);  // they are online: reserved for the raid
        Assert.That(backend.Reserved.Select(r => r.Event).Distinct(), Is.EqualTo(new[] { "e" }));
        Assert.That(backend.Reserved.Count, Is.EqualTo(2));
        Assert.That(book.Pending("e"), Is.EqualTo(0));
        Assert.That(book.IsGuest(backend.LoginRequests[0]), Is.True, "a guest does not count toward the realm goal");
    }

    [Test]
    public void FailedLoginsAreReplacedFromThePoolWhileTheRaidAssembles()
    {
        var backend = new FakeBackend();
        RallyGuestBook book = Book(backend, pool: 10);
        book.Summon("e", eRealm.Albion, 2, null, 1, Vector3.Zero, 0, Now);
        long first = Pool(10)[0].BotId;
        backend.RefuseLogin.Add(first);
        for (int i = 1; i <= 4; i++) book.Pulse(i * 1_000, Now);
        Assert.That(book.IsGuest(first), Is.False, "three refusals drop a guest");
        Assert.That(book.GuestCount(eRealm.Albion), Is.EqualTo(2), "and another archived 50 takes its place");
    }

    // ---------- release ----------

    [Test]
    public void AfterTheRaidGuestsLootLingerAndLogOut()
    {
        var backend = new FakeBackend { LoginGoesLive = true };
        RallyGuestBook book = Book(backend);
        book.Summon("e", eRealm.Albion, 3, null, 1, Vector3.Zero, 0, Now);
        book.Pulse(1, Now);
        book.Pulse(1_000, Now);
        long end = 3_600_000;
        book.OnRaidEnded("e", started: true, end);
        long[] guests = backend.LoginRequests.ToArray();
        Assert.That(guests.All(id => book.Get(id).State == RallyGuestBook.GuestState.Lingering));
        long earliest = end + RallyAnswerPolicy.LootMilliseconds + RallyAnswerPolicy.LingerMinMilliseconds;
        book.Pulse(earliest - 1, Now);
        Assert.That(backend.LogoutRequests, Is.Empty, "60 s loot plus at least 5 minutes first");
        book.Pulse(end + RallyAnswerPolicy.LootMilliseconds + RallyAnswerPolicy.LingerMaxMilliseconds, Now);
        Assert.That(backend.LogoutRequests, Is.EquivalentTo(guests), "all gone within 20 minutes");
        foreach (long id in guests) backend.Live.Remove(id);
        book.Pulse(end + 30 * 60_000L, Now);
        Assert.That(book.TotalGuests, Is.EqualTo(0));
    }

    [Test]
    public void LingerIsBetweenFiveAndTwentyMinutesAfterLoot()
    {
        Assert.That(RallyAnswerPolicy.LogoutDelay(true, 0), Is.EqualTo(60_000 + 5 * 60_000L));
        Assert.That(RallyAnswerPolicy.LogoutDelay(true, 1), Is.EqualTo(60_000 + 20 * 60_000L));
        Assert.That(RallyAnswerPolicy.LogoutDelay(false, 0.5), Is.EqualTo(10 * 60_000L), "stopped before the start: 10 minutes");
    }

    [Test]
    public void AStoppedRaidKeepsGuestsTenMinutesForAReSound()
    {
        var backend = new FakeBackend { LoginGoesLive = true };
        RallyGuestBook book = Book(backend);
        book.Summon("e", eRealm.Albion, 1, null, 1, Vector3.Zero, 0, Now);
        book.Pulse(1, Now);
        book.Pulse(1_000, Now);
        long id = backend.LoginRequests.Single();
        book.OnRaidEnded("e", started: false, 10_000);
        book.Pulse(10_000 + 9 * 60_000L, Now);
        Assert.That(backend.LogoutRequests, Is.Empty);
        // sounded again within the 10 minutes: the guest answers again and stays
        book.Attach(id, "e2");
        book.Pulse(10_000 + 11 * 60_000L, Now);
        Assert.That(backend.LogoutRequests, Is.Empty);
        Assert.That(book.Get(id).EventId, Is.EqualTo("e2"));
        // not sounded again: it logs out after 10 minutes
        book.OnRaidEnded("e2", started: false, 20 * 60_000L);
        book.Pulse(31 * 60_000L, Now);
        Assert.That(backend.LogoutRequests, Is.EqualTo(new[] { id }));
    }

    [Test]
    public void QueuedGuestsAreNotLoggedInAfterTheRaidEnds()
    {
        var backend = new FakeBackend();
        RallyGuestBook book = Book(backend);
        book.Summon("e", eRealm.Albion, 50, null, 1, Vector3.Zero, 0, Now);
        book.Pulse(1, Now);                         // 5 logins requested
        book.OnRaidEnded("e", started: false, 100);
        Assert.That(book.TotalGuests, Is.EqualTo(5), "the 45 still queued are dropped");
        book.Pulse(10_000, Now);
        Assert.That(backend.LoginRequests.Count, Is.EqualTo(5), "no further logins");
    }

    [Test]
    public void ALogoutRefusalIsRetried()
    {
        var backend = new RefusingBackend();
        var book = new RallyGuestBook(backend, new Random(1));
        book.SetPool(eRealm.Albion, Pool(1));
        book.Summon("e", eRealm.Albion, 1, null, 1, Vector3.Zero, 0, Now);
        book.Pulse(1, Now);
        book.Pulse(1_000, Now);
        book.OnRaidEnded("e", started: false, 2_000);
        book.Pulse(2_000 + 10 * 60_000L, Now);
        book.Pulse(2_000 + 10 * 60_000L + 10_000, Now);
        Assert.That(backend.Logouts, Is.EqualTo(1), "not before 30 s");
        book.Pulse(2_000 + 10 * 60_000L + 30_000, Now);
        Assert.That(backend.Logouts, Is.EqualTo(2));
    }

    private sealed class RefusingBackend : IRallyGuestBackend
    {
        public int Logouts;
        public bool ArchiveAvailable => true;
        public IReadOnlyList<RallyPoolEntry> LoadPool(eRealm realm, int limit) => Array.Empty<RallyPoolEntry>();
        private readonly HashSet<long> _live = new();
        public bool RequestLogin(long botId) => _live.Add(botId);
        public bool IsLoggingIn(long botId) => false;
        public bool RequestLogout(long botId) { Logouts++; return false; }
        public bool IsLive(long botId) => _live.Contains(botId);
        public bool Reserve(string eventId, long botId) => true;
        public string GuildIdOf(long botId) => null;
    }

    // ---------- window text and fallback ----------

    [Test]
    public void StatusTextFitsTheHornWindow()
    {
        Assert.That(RallyAnswerPolicy.Status(new RallyAnswer(120, 180, 0), 0), Is.EqualTo("Ready - 120 in the field, 180 more will answer"));
        foreach (string text in new[]
                 {
                     RallyAnswerPolicy.Status(new RallyAnswer(300, 0, 0), 0),
                     RallyAnswerPolicy.Status(new RallyAnswer(250, 0, 50), 29 * 60_000L),
                     RallyAnswerPolicy.Status(new RallyAnswer(100, 0, 99), 0),
                     RallyAnswerPolicy.Status(new RallyAnswer(100, 0, 99), 30 * 60_000L),
                     RallyAnswerPolicy.Status(new RallyAnswer(120, 180, 0), 0),
                 })
            Assert.That(Encoding.Latin1.GetByteCount(text), Is.LessThanOrEqualTo(63), text);
        Assert.That(RallyAnswerPolicy.Status(new RallyAnswer(100, 0, 99), 0), Does.StartWith("Not enough"));
    }

    [Test, NonParallelizable]
    public void WithTheSchedulerOffTheHornIsLiveOnly()
    {
        bool previous = PopulationSchedulerProperties.POPULATION_SCHEDULER_ENABLED;
        try
        {
            PopulationSchedulerProperties.POPULATION_SCHEDULER_ENABLED = false;
            Assert.That(RallyGuests165.NewRulesOn, Is.False);
            Assert.That(RallyGuests165.ArchiveAllowed, Is.False);
            Assert.That(new CoreRallyBackend().ArchiveAvailable, Is.False);
            Assert.That(new CoreRallyBackend().RequestLogin(1), Is.False);
        }
        finally
        {
            PopulationSchedulerProperties.POPULATION_SCHEDULER_ENABLED = previous;
        }
    }
}
