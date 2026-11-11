using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>GitHub #38: hired companions follow on ordinary paths in Darkness Falls; unled autonomous bots keep the
/// certified-corridor gate; the certified entrance fall stays available to companions.</summary>
[TestFixture]
public class UT_CompanionDarknessFallsPaths
{
    private const long HiredBotId = 987_654_321_038;

    private sealed class TestServer : GameServer
    {
        public IObjectDatabase TestDatabase;
        protected override IObjectDatabase DataBaseImpl => TestDatabase;
    }

    private GameServer _previousServer;

    [SetUp]
    public void SetUp()
    {
        _previousServer = GameServer.Instance;
        TestServer server = (TestServer) RuntimeHelpers.GetUninitializedObject(typeof(TestServer));
        server.TestDatabase = DispatchProxy.Create<IObjectDatabase, DOL.UnitTests.UT_UnobservedConcentration.EmptyReads>();
        GameServer.LoadTestDouble(server);
    }

    [TearDown]
    public void TearDown()
    {
        HiredMap().TryRemove(HiredBotId, out _);
        GameServer.LoadTestDouble(_previousServer);
    }

    [Test]
    public void RouteGate_AppliesOnlyToUnledAutonomousBots()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CompanionDarknessFallsPaths.UsesCertifiedRouteGate(true, false), Is.True);
            Assert.That(CompanionDarknessFallsPaths.UsesCertifiedRouteGate(true, true), Is.False);
            Assert.That(CompanionDarknessFallsPaths.UsesCertifiedRouteGate(false, false), Is.False);
            Assert.That(CompanionDarknessFallsPaths.UsesCertifiedRouteGate(false, true), Is.False);
        });
    }

    [Test]
    public void CertifiedFall_StaysAvailableToAutonomousBotsAndCompanions()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CompanionDarknessFallsPaths.MayUseCertifiedFall(true, false), Is.True);
            Assert.That(CompanionDarknessFallsPaths.MayUseCertifiedFall(true, true), Is.True);
            Assert.That(CompanionDarknessFallsPaths.MayUseCertifiedFall(false, true), Is.True);
            Assert.That(CompanionDarknessFallsPaths.MayUseCertifiedFall(false, false), Is.False);
        });
    }

    [Test]
    public void HiredCompanion_IsNotGatedButKeepsTheFall()
    {
        GameBot bot = AutonomousBot(HiredBotId);
        HiredMap()[HiredBotId] = new OfflineHiredCompanionRecord { BotId = HiredBotId };
        Assert.Multiple(() =>
        {
            Assert.That(HiredCompanions165.IsHired(bot), Is.True);
            Assert.That(CompanionDarknessFallsPaths.IsPlayerLed(bot), Is.True);
            Assert.That(CompanionDarknessFallsPaths.UsesCertifiedRouteGate(bot), Is.False);
            Assert.That(CompanionDarknessFallsPaths.MayUseCertifiedFall(bot), Is.True);
        });
    }

    [Test]
    public void UnledAutonomousBot_IsStillGated()
    {
        GameBot bot = AutonomousBot(HiredBotId);
        Assert.Multiple(() =>
        {
            Assert.That(CompanionDarknessFallsPaths.IsPlayerLed(bot), Is.False);
            Assert.That(CompanionDarknessFallsPaths.UsesCertifiedRouteGate(bot), Is.True);
            Assert.That(CompanionDarknessFallsPaths.MayUseCertifiedFall(bot), Is.True);
            Assert.That(CompanionDarknessFallsPaths.UsesCertifiedRouteGate((GameBot) null), Is.False);
        });
    }

    [Test]
    public void BotInPlayerLedGroup_IsNotGated()
    {
        GameBot bot = AutonomousBot(HiredBotId);
        GamePlayer leader = (GamePlayer) RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));
        SetProperty(bot, nameof(GameBot.PlayerGroupLeader), leader);
        Assert.Multiple(() =>
        {
            Assert.That(CompanionDarknessFallsPaths.UsesCertifiedRouteGate(bot), Is.False);
            Assert.That(CompanionDarknessFallsPaths.MayUseCertifiedFall(bot), Is.True);
        });
    }

    [Test]
    public void LogSlot_IsRateLimitedPerBot()
    {
        ConcurrentDictionary<long, long> next = new();
        const long interval = CompanionDarknessFallsPaths.LogIntervalMilliseconds;
        Assert.Multiple(() =>
        {
            Assert.That(CompanionDarknessFallsPaths.TryClaimLogSlot(next, 1, 1_000, interval), Is.True);
            Assert.That(CompanionDarknessFallsPaths.TryClaimLogSlot(next, 1, 1_000 + interval - 1, interval), Is.False);
            Assert.That(CompanionDarknessFallsPaths.TryClaimLogSlot(next, 2, 1_500, interval), Is.True);
            Assert.That(CompanionDarknessFallsPaths.TryClaimLogSlot(next, 1, 1_000 + interval, interval), Is.True);
        });
    }

    private static GameBot AutonomousBot(long id)
    {
        GameBot bot = (GameBot) RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
        SetProperty(bot, nameof(GameBot.IsAutonomousWorldBot), true);
        bot.DatabaseID = id;
        return bot;
    }

    private static void SetProperty(GameBot bot, string name, object value) =>
        typeof(GameBot).GetProperty(name, BindingFlags.Instance | BindingFlags.Public)!.SetValue(bot, value);

    private static ConcurrentDictionary<long, OfflineHiredCompanionRecord> HiredMap() =>
        (ConcurrentDictionary<long, OfflineHiredCompanionRecord>) typeof(HiredCompanions165)
            .GetField("Hired", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
}
