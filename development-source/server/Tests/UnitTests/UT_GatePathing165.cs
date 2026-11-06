using System.Collections.Generic;
using System.Numerics;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using static DOL.GS.Tests.RealmGateTestKit;

namespace DOL.GS.Tests;

/// <summary>
/// True 1.65 Mode door pathing, the owner's final rules for bots (2026-11-06): bots walk through every door without opening
/// it; a milegate passes every realm; a border keep door passes its realm only (enemy bots never route through it); an
/// enemy keep door holds enemy bots until it is broken (own realm is ported through). The navmesh state and the mover's
/// decision agree per realm for each kind.
/// </summary>
[TestFixture, NonParallelizable]
public class UT_GatePathing165 : RealmGateFixture
{
    [Test]
    public void EveryRealmWalksThroughAShutMilegate()
    {
        Gate milegate = NewGate(eRealm.Albion);
        Assert.That(PathfindingMgrBase.IsBlockingDoor(milegate), Is.False, "its polygons are never shut");
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Albion), milegate), Is.False, "own realm");
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Midgard), milegate), Is.False, "enemy realm: no stop, no attack");
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Hibernia, world: false, companion: true), milegate), Is.False, "a companion");
    }

    [Test]
    public void ShutBorderKeepDoorPassesItsRealmOnly()
    {
        Gate door = NewGate(eRealm.Albion, doorId: BorderKeepId);
        Assert.That(PathfindingMgrBase.IsBlockingDoor(door), Is.True, "a shut border keep door is a BlockingDoor polygon");
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Albion), door), Is.False, "own realm PvE gamebot walks through");
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Albion, world: false, companion: true), door), Is.False, "own realm companion");
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Midgard), door), Is.True, "enemy bot");
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Midgard, world: false, companion: true), door), Is.True, "enemy companion");
        Assert.That(door.MayPass(eRealm.Albion), Is.True, "its realm's planner goes through");
        Assert.That(door.MayPass(eRealm.Hibernia), Is.False, "an enemy planner never does");
    }

    [Test]
    public void OpenBorderKeepDoorLetsEveryoneThrough()
    {
        Gate door = NewGate(eRealm.Albion, doorId: BorderKeepId);
        door.State = eDoorState.Open;
        Assert.That(PathfindingMgrBase.IsBlockingDoor(door), Is.False);
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Midgard), door), Is.False);
    }

    [Test]
    public void NavmeshStateAndMoverAgreePerRealmForEveryGate()
    {
        foreach (int id in new[] { MilegateId, BorderKeepId })
        foreach (eDoorState state in new[] { eDoorState.Closed, eDoorState.Open })
        foreach (eRealm owner in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
        foreach (eRealm actor in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
        {
            Gate gate = NewGate(owner, doorId: id);
            gate.State = state;
            bool blocked = Pathfinder.BlocksActor(NewBot(actor), gate);
            // An open (Door) polygon never blocks anyone; only an enemy at a shut border keep door is blocked.
            if (!PathfindingMgrBase.IsBlockingDoor(gate)) Assert.That(blocked, Is.False);
            Assert.That(blocked, Is.EqualTo(id == BorderKeepId && state == eDoorState.Closed && actor != owner),
                $"{actor} at {owner}'s {state} door {id}");
        }
    }

    [Test]
    public void KeepDoorPortsItsRealmsBotsAndCompanionsAndHoldsEnemies()
    {
        KeepDoor door = NewKeepDoor(2);
        Properties.CLASSIC165_MODE = true;
        Assert.That(PathfindingMgrBase.IsBlockingDoor(door), Is.True);
        Assert.That(door.IsAttackableDoor, Is.True, "enemies break it");
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Midgard), door), Is.False, "own realm PvE gamebot (traverse)");
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Midgard, world: false, companion: true), door), Is.False, "own realm companion");
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Albion), door), Is.True, "enemy bot");
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Albion, world: false, companion: true), door), Is.True, "enemy companion");
        Properties.CLASSIC165_MODE = false;
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Midgard), door), Is.True, "normal mode: a PvE gamebot is still held");
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
    public void ARegisteredGateTakesItsWholeDoorwayButNoOtherDoor()
    {
        // BuildNav DoorSize of the border keep gates: up to 619 wide; a border keep's doors stand 1,300 and more apart.
        Assert.That(GameRealmGate.DoorwayHalfWidth, Is.GreaterThanOrEqualTo(619 / 2));
        Assert.That(GameRealmGate.DoorwayHalfWidth, Is.LessThan(1_300 / 2));
        Assert.That(Pathfinder.WIDE_DOOR_SEARCH_DISTANCE, Is.GreaterThanOrEqualTo(619 / 2),
            "a shut door's side polygon still finds its door row");
    }

    [Test]
    public void AWalkRunsThroughAGateWithin140AndItsHeight()
    {
        var door = new Vector3(500, 0, 0);
        Assert.That(GameRealmGate.TryCrossing(new(0, 0, 0), new(1000, 0, 0), door, out float stop), Is.True);
        Assert.That(stop * 1000, Is.EqualTo(360).Within(1), "the stop is 140 in front of the gate");
        Assert.That(GameRealmGate.TryCrossing(new(0, 100, 0), new(1000, 100, 0), door, out _), Is.True, "100 to the side: in the passage");
        Assert.That(GameRealmGate.TryCrossing(new(0, 200, 0), new(1000, 200, 0), door, out _), Is.False, "beside it");
        Assert.That(GameRealmGate.TryCrossing(new(0, 0, 400), new(1000, 0, 400), door, out _), Is.False, "a floor above it");
        Assert.That(GameRealmGate.TryCrossing(new(0, 0, 0), new(300, 0, 0), door, out _), Is.False, "the walk ends before it");
    }

    [Test]
    public void AWalkBesideAWideBorderKeepDoorStillRunsThroughIt()
    {
        var door = new Vector3(500, 0, 0);
        Assert.That(GameRealmGate.TryCrossing(new(0, 250, 0), new(1000, 250, 0), door, out _), Is.False, "beside a milegate");
        Assert.That(GameRealmGate.TryCrossing(new(0, 250, 0), new(1000, 250, 0), door, out _, GameRealmGate.DoorwayHalfWidth), Is.True,
            "the side of a 619-wide border keep doorway");
        Gate border = NewGate(eRealm.Hibernia, 500, 0, 0, BorderKeepId);
        var nodes = new RingQueue<WrappedPathfindingNode>();
        var doors = new Dictionary<WrappedPathfindingNode, List<GameDoorBase>>();
        nodes.Enqueue(new(new Vector3(1000, 250, 0), EDtPolyFlags.Walk));
        Assert.That(Pathfinder.MarkGatesOffMesh(nodes, doors, new Vector3(0, 250, 0), new GameRealmGate[] { border }), Is.EqualTo(1));
        Assert.That(Pathfinder.CrossesShutGate(NewBot(eRealm.Albion), doors), Is.True, "an enemy cannot slip past beside the doors");
        Assert.That(Pathfinder.CrossesShutGate(NewBot(eRealm.Hibernia), doors), Is.False);
    }

    [Test]
    public void BorderKeepDoorsOffTheNavmeshAreFoundOnTheWalk()
    {
        Gate outer = NewGate(eRealm.Albion, 500, 0, 0, BorderKeepId), inner = NewGate(eRealm.Albion, 930, 0, 0, BorderKeepId + 1);
        Gate far = NewGate(eRealm.Albion, 5000, 5000, 0, BorderKeepId + 2);
        var nodes = new RingQueue<WrappedPathfindingNode>();
        var doors = new Dictionary<WrappedPathfindingNode, List<GameDoorBase>>();
        nodes.Enqueue(new(new Vector3(1500, 0, 0), EDtPolyFlags.Walk));
        nodes.Enqueue(new(new Vector3(1500, 800, 0), EDtPolyFlags.Walk));

        int marked = Pathfinder.MarkGatesOffMesh(nodes, doors, Vector3.Zero, new GameRealmGate[] { inner, outer, far });

        Assert.That(marked, Is.EqualTo(2), "both doors on the walk, not the one far away");
        Assert.That(nodes.Count, Is.EqualTo(4));
        Assert.That(nodes.Peek(0).Position.X, Is.EqualTo(500 - GameRealmGate.DoorwayHalfWidth).Within(1),
            "a stop in front of the outer door's doorway first");
        Assert.That(doors[nodes.Peek(0)], Is.EqualTo(new List<GameDoorBase> { outer }));
        Assert.That(doors[nodes.Peek(1)], Is.EqualTo(new List<GameDoorBase> { inner }));
        Assert.That(Pathfinder.CrossesShutGate(NewBot(eRealm.Midgard), doors), Is.True, "an enemy cannot take that walk as a way round");
        Assert.That(Pathfinder.CrossesShutGate(NewBot(eRealm.Albion), doors), Is.False, "its own realm can");
    }
}
