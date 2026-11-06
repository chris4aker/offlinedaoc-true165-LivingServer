using System.Collections.Generic;
using System.Numerics;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using static DOL.GS.Tests.RealmGateTestKit;

namespace DOL.GS.Tests;

/// <summary>
/// True 1.65 Mode gate and keep door pathing (owner 2026-11-06): own-realm bots, companions and pets walk through, an enemy
/// bot's route stops in front of the gate (it breaks it), and the navmesh state and the mover's decision agree per realm.
/// </summary>
[TestFixture, NonParallelizable]
public class UT_GatePathing165 : RealmGateFixture
{
    [Test]
    public void ShutGateFortGateBlocksTheEnemyBotButNotItsOwnRealm()
    {
        Gate gate = NewGate(eRealm.Albion);
        Assert.That(PathfindingMgrBase.IsBlockingDoor(gate), Is.True, "a shut gate is a BlockingDoor polygon");
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Albion), gate), Is.False, "own realm PvE gamebot walks through");
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Albion, world: false, companion: true), gate), Is.False, "own realm companion");
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Midgard), gate), Is.True, "enemy bot");
        Assert.That(Pathfinder.WalksUpToGate(NewBot(eRealm.Midgard), gate), Is.True, "the enemy RvR bot goes up to it to break it");
        Assert.That(Pathfinder.WalksUpToGate(NewBot(eRealm.Albion), gate), Is.False);
        Assert.That(Pathfinder.WalksUpToGate(NewBot(eRealm.Midgard, world: false, companion: true), gate), Is.False,
            "an enemy player's companion only follows its player");
    }

    [Test]
    public void OpenOrBrokenGateFortGateLetsEveryoneThroughAndItsPolygonsOpen()
    {
        Gate gate = NewGate(eRealm.Albion);
        gate.State = eDoorState.Open;
        gate.Standing = false;
        Assert.That(PathfindingMgrBase.IsBlockingDoor(gate), Is.False);
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Midgard), gate), Is.False);
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Albion), gate), Is.False);
        Assert.That(Pathfinder.WalksUpToGate(NewBot(eRealm.Midgard), gate), Is.False, "nothing left to break");
    }

    [Test]
    public void NavmeshStateAndMoverAgreePerRealmForEveryGateState()
    {
        foreach (eDoorState state in new[] { eDoorState.Closed, eDoorState.Open })
        foreach (eRealm owner in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
        foreach (eRealm actor in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
        {
            Gate gate = NewGate(owner);
            gate.State = state;
            bool blocked = Pathfinder.BlocksActor(NewBot(actor), gate);
            // An open (Door) polygon never blocks anyone; a blocked actor always faces a BlockingDoor polygon, only an
            // enemy is blocked, and that enemy may break the gate.
            if (!PathfindingMgrBase.IsBlockingDoor(gate)) Assert.That(blocked, Is.False);
            Assert.That(blocked, Is.EqualTo(state == eDoorState.Closed && actor != owner), $"{actor} at {owner}'s {state} gate");
            if (blocked) Assert.That(GameRealmGate.MayBreak(actor, owner, true, state), Is.True);
        }
    }

    [Test]
    public void KeepDoorLetsItsRealmsBotsAndCompanionsThroughInTrue165Mode()
    {
        KeepDoor door = NewKeepDoor(2);
        Properties.CLASSIC165_MODE = true;
        Assert.That(PathfindingMgrBase.IsBlockingDoor(door), Is.True);
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Midgard), door), Is.False, "own realm PvE gamebot");
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Midgard, world: false, companion: true), door), Is.False, "own realm companion");
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Albion), door), Is.True, "enemy bot");
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Albion, world: false, companion: true), door), Is.True, "enemy companion");
        Properties.CLASSIC165_MODE = false;
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Midgard), door), Is.True, "normal mode: a PvE gamebot is still held");
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Midgard, world: false, companion: true), door), Is.True, "normal mode unchanged");
    }

    [Test]
    public void BrokenKeepDoorStandsOpenForEveryone()
    {
        KeepDoor door = NewKeepDoor(2);
        door.State = eDoorState.Open;
        Properties.CLASSIC165_MODE = true;
        Assert.That(PathfindingMgrBase.IsBlockingDoor(door), Is.False);
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Albion), door), Is.False);
    }

    [Test]
    public void EnemyRouteStopsInFrontOfTheGate()
    {
        GameRealmGate gate = NewGate(eRealm.Hibernia);
        GameRealmGate other = NewGate(eRealm.Hibernia);
        var nodes = new RingQueue<WrappedPathfindingNode>();
        var doors = new Dictionary<WrappedPathfindingNode, List<GameDoorBase>>();
        var a = new WrappedPathfindingNode(new Vector3(0, 0, 0), EDtPolyFlags.Walk);
        var b = new WrappedPathfindingNode(new Vector3(100, 0, 0), EDtPolyFlags.BlockingDoor);
        var c = new WrappedPathfindingNode(new Vector3(200, 0, 0), EDtPolyFlags.BlockingDoor);
        var d = new WrappedPathfindingNode(new Vector3(300, 0, 0), EDtPolyFlags.Walk);
        foreach (var node in new[] { a, b, c, d }) nodes.Enqueue(node);
        doors[b] = [gate];
        doors[c] = [gate];

        Assert.That(Pathfinder.StopAtDoor(nodes, doors, other), Is.False, "the route does not reach that door");
        Assert.That(nodes.Count, Is.EqualTo(4));

        Assert.That(Pathfinder.StopAtDoor(nodes, doors, gate), Is.True);
        Assert.That(nodes.Count, Is.EqualTo(2));
        Assert.That(nodes.Peek(0), Is.EqualTo(a));
        Assert.That(nodes.Peek(1), Is.EqualTo(b), "the walk ends where the gate's polygons begin");
        Assert.That(doors, Is.Empty, "nothing on the cut route leads through the gate any more");
    }

    [Test]
    public void AWalkRunsThroughAGateWithin140AndItsHeight()
    {
        var door = new Vector3(500, 0, 0);
        Assert.That(GameRealmGate.TryCrossing(new(0, 0, 0), new(1000, 0, 0), door, out float stop), Is.True);
        Assert.That(stop * 1000, Is.EqualTo(360).Within(1), "the stop is 140 in front of the gate");
        Assert.That(GameRealmGate.TryCrossing(new(0, 100, 0), new(1000, 100, 0), door, out _), Is.True, "100 to the side: in the passage");
        Assert.That(GameRealmGate.TryCrossing(new(0, 200, 0), new(1000, 200, 0), door, out _), Is.False, "beside the gate fort");
        Assert.That(GameRealmGate.TryCrossing(new(0, 0, 400), new(1000, 0, 400), door, out _), Is.False, "the wall-walk above it");
        Assert.That(GameRealmGate.TryCrossing(new(0, 0, 0), new(300, 0, 0), door, out _), Is.False, "the walk ends before it");
        Assert.That(GameRealmGate.TryCrossing(new(450, 0, 0), new(1000, 0, 0), door, out stop), Is.True);
        Assert.That(stop, Is.EqualTo(0), "already at it: stop where the walk starts");
    }

    [Test]
    public void GatesOffTheNavmeshAreFoundOnTheWalk()
    {
        Gate outer = NewGate(eRealm.Albion, 500, 0, 0), inner = NewGate(eRealm.Albion, 930, 0, 0), far = NewGate(eRealm.Albion, 5000, 5000, 0);
        var nodes = new RingQueue<WrappedPathfindingNode>();
        var doors = new Dictionary<WrappedPathfindingNode, List<GameDoorBase>>();
        nodes.Enqueue(new(new Vector3(1500, 0, 0), EDtPolyFlags.Walk));
        nodes.Enqueue(new(new Vector3(1500, 800, 0), EDtPolyFlags.Walk));

        int marked = Pathfinder.MarkGatesOffMesh(nodes, doors, Vector3.Zero, new GameRealmGate[] { inner, outer, far });

        Assert.That(marked, Is.EqualTo(2), "both gates of the gate fort, not the one far away");
        Assert.That(nodes.Count, Is.EqualTo(4));
        Assert.That(nodes.Peek(0).Position.X, Is.EqualTo(360).Within(1), "a stop in front of the outer gate first");
        Assert.That(doors[nodes.Peek(0)], Is.EqualTo(new List<GameDoorBase> { outer }));
        Assert.That(nodes.Peek(1).Position.X, Is.EqualTo(790).Within(1), "then in front of the inner gate");
        Assert.That(doors[nodes.Peek(1)], Is.EqualTo(new List<GameDoorBase> { inner }));
        Assert.That(nodes.Peek(2).Position, Is.EqualTo(new Vector3(1500, 0, 0)));

        Assert.That(Pathfinder.CrossesShutGate(NewBot(eRealm.Midgard), doors), Is.True, "an enemy cannot take that walk as a way round");
        Assert.That(Pathfinder.CrossesShutGate(NewBot(eRealm.Albion), doors), Is.False, "its own realm can");
        Assert.That(Pathfinder.StopAtDoor(nodes, doors, outer), Is.True);
        Assert.That(nodes.Count, Is.EqualTo(1), "the enemy walks to the stop in front of the outer gate");
    }
}
