using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS.Keeps;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>The True 1.65 gate tests run against a database-less server double (game objects need one) and put the
/// True 1.65 Mode switch back after each test.</summary>
public abstract class RealmGateFixture
{
    private static readonly IObjectDatabase Empty = DispatchProxy.Create<IObjectDatabase, DOL.UnitTests.UT_UnobservedConcentration.EmptyReads>();
    private sealed class Server : GameServer { protected override IObjectDatabase DataBaseImpl => Empty; }
    private GameServer _previous;
    private bool _mode;

    [SetUp]
    public void UseTestServer()
    {
        _previous = GameServer.Instance;
        _mode = Properties.CLASSIC165_MODE;
        GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
    }

    [TearDown]
    public void RestoreServer()
    {
        GameServer.LoadTestDouble(_previous);
        Properties.CLASSIC165_MODE = _mode;
    }
}

/// <summary>Gates, keep doors and bots for the True 1.65 gate tests, made without a world.</summary>
internal static class RealmGateTestKit
{
    internal sealed class Gate : GameRealmGate
    {
        public override eRealm Realm { get; set; }
        public override eDoorState State { get; set; } = eDoorState.Closed;
        public override int X { get; set; }
        public override int Y { get; set; }
        public override int Z { get; set; }
        public bool Standing = true;
        public override bool IsAlive => Standing;
    }

    internal sealed class KeepDoor : GameKeepDoor
    {
        public override eDoorState State { get; set; } = eDoorState.Closed;
    }

    internal sealed class Bot : GameBot
    {
        private Bot() : base((OfflineWorldBotRecord)null) { }
        public override eRealm Realm { get; set; }
        public override bool IsAlive => true;
        public override ushort CurrentRegionID { get => 1; set { } }
    }

    /// <summary>Hadrian's Wall's milegate door 01.</summary>
    internal const int MilegateId = 15000401;
    /// <summary>Castle Sauvage's border keep door 01.</summary>
    internal const int BorderKeepId = 11020501;

    internal static Gate NewGate(eRealm realm, int x = 0, int y = 0, int z = 0, int doorId = MilegateId)
    {
        var gate = (Gate)RuntimeHelpers.GetUninitializedObject(typeof(Gate));
        gate.DoorId = doorId;
        gate.Realm = realm;
        gate.State = eDoorState.Closed;
        gate.Standing = true;
        gate.X = x; gate.Y = y; gate.Z = z;
        return gate;
    }

    internal static Bot NewBot(eRealm realm, bool world = true, bool companion = false)
    {
        var bot = (Bot)RuntimeHelpers.GetUninitializedObject(typeof(Bot));
        bot.Realm = realm;
        typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot)).SetValue(bot, world);
        typeof(GameBot).GetProperty(nameof(GameBot.IsTemporaryGroupHelper)).SetValue(bot, companion);
        typeof(GameBot).GetProperty(nameof(GameBot.PersistentRecord)).SetValue(bot, new OfflineWorldBotRecord { ObjectiveKind = "SoloPve" });
        return bot;
    }

    internal static KeepDoor NewKeepDoor(byte keepRealm, bool portalKeep = false)
    {
        var door = (KeepDoor)RuntimeHelpers.GetUninitializedObject(typeof(KeepDoor));
        door.State = eDoorState.Closed;
        door.Component = new GameKeepComponent
        {
            Keep = new GameKeep { DBKeep = new DbKeep { Realm = keepRealm, BaseLevel = (byte)(portalKeep ? 255 : 50) } }
        };
        return door;
    }
}
