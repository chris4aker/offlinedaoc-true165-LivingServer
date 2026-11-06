using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// True 1.65 Mode realm gates on installed (or staged) navmeshes, read-only (meshes are loaded into memory only), with the
/// period rules (owner 2026-11-06):
/// - every gate fort (milegate) has a walk through it and blocks no bot of any realm (it is never registered, so the mesh
///   never shuts it);
/// - every border keep door is shut by the mesh (registered) or found on the walk (off the mesh): its realm's bots walk
///   through, an enemy bot is held, and a sealed walk past its shut doors is reported.
/// OFFLINE_DAOC_NAV_ROOT = a folder with lib\Detour.dll and navmesh(-165)\zoneNNN.nav, OFFLINE_DAOC_DB_PATH = a 1.65 database with
/// the gates content (build_gates165.py) applied.
/// </summary>
[TestFixture, Explicit("Read-only installed navmesh check for the True 1.65 gates"), NonParallelizable]
public class UT_GateNavmeshInstalled165
{
    private static readonly DOL.Database.IObjectDatabase Empty =
        DispatchProxy.Create<DOL.Database.IObjectDatabase, DOL.UnitTests.UT_UnobservedConcentration.EmptyReads>();
    private sealed class Server : GameServer { protected override DOL.Database.IObjectDatabase DataBaseImpl => Empty; }

    private sealed class Gate : GameRealmGate
    {
        public override int X { get; set; }
        public override int Y { get; set; }
        public override int Z { get; set; }
        public override Region CurrentRegion { get; set; }
        public override eRealm Realm { get; set; }
        public override eDoorState State { get; set; } = eDoorState.Closed;
        public override bool IsAlive => true;
    }

    private sealed class Walker : GameBot
    {
        private Walker() : base((OfflineWorldBotRecord)null) { }
        public override eRealm Realm { get; set; }
        public override bool IsAlive => true;
    }

    [Test]
    public void MilegatesPassEveryoneBorderKeepDoorsOnlyTheirRealm()
    {
        string previous = Environment.CurrentDirectory;
        var loaded = new List<Zone>();
        var regions = new Dictionary<ushort, Region>();
        var failures = new List<string>();
        var previousServer = GameServer.Instance;
        var previousNav = PathfindingProvider.Instance;
        GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
        bool mode = DOL.GS.ServerProperties.Properties.CLASSIC165_MODE;
        DOL.GS.ServerProperties.Properties.CLASSIC165_MODE = true; // the True 1.65 meshes (navmesh-165) where installed
        try
        {
            Environment.CurrentDirectory = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
            using var db = new SQLiteConnection($"Data Source={Environment.GetEnvironmentVariable("OFFLINE_DAOC_DB_PATH")};Read Only=True;Pooling=False;");
            db.Open();
            var rows = new List<(int Id, int Region, int Realm, Vector3 Point)>();
            using (var q = db.CreateCommand())
            {
                q.CommandText = "select d.InternalID, z.RegionID, d.Realm, d.X, d.Y, d.Z from Door d join Zones z on z.ZoneID = d.InternalID / 1000000 " +
                                "where d.Realm in (1,2,3) order by d.InternalID";
                using var r = q.ExecuteReader();
                while (r.Read()) rows.Add((r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), new(r.GetInt32(3), r.GetInt32(4), r.GetInt32(5))));
            }
            foreach (ushort id in rows.Select(row => (ushort)row.Region).Distinct())
            {
                var region = (Region)RuntimeHelpers.GetUninitializedObject(typeof(Region));
                typeof(Region).GetField("m_regionData", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(region, new RegionData { Id = id });
                typeof(Region).GetField("m_zones", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(region, new List<Zone>());
                regions[id] = region;
                using var q = db.CreateCommand();
                q.CommandText = $"select ZoneID,Name,OffsetX,OffsetY,Width,Height from Zones where RegionID={id}";
                using var r = q.ExecuteReader();
                while (r.Read())
                {
                    ushort zid = (ushort)r.GetInt32(0);
                    var zone = new Zone(region, zid, r.GetString(1), r.GetInt32(2) * 8192, r.GetInt32(3) * 8192, r.GetInt32(4) * 8192,
                        r.GetInt32(5) * 8192, zid, false, 0, false, 0, 0, 0, 0, 0);
                    region.Zones.Add(zone);
                    LocalPathfindingMgr.LoadNavMesh(zone);
                    loaded.Add(zone);
                }
            }
            var nav = PathfindingProvider.LocalPathfindingMgr;
            PathfindingProvider.SetPathfindingMgr(nav);
            var gates = new Dictionary<int, Gate>();
            var offMesh = new List<GameRealmGate>();
            foreach (var row in rows)
            {
                var gate = (Gate)RuntimeHelpers.GetUninitializedObject(typeof(Gate));
                gate.CurrentRegion = regions[(ushort)row.Region];
                gate.X = (int)row.Point.X; gate.Y = (int)row.Point.Y; gate.Z = (int)row.Point.Z;
                gate.Realm = (eRealm)row.Realm;
                gate.State = eDoorState.Closed;
                gate.DoorId = row.Id;
                // As DoorMgr.RegisterDoor: only a door nobody opens by interaction (a border keep door) is registered.
                bool registered = !gate.CanBeOpenedViaInteraction && nav.RegisterDoor(gate);
                if (gate.IsBorderKeep) offMesh.Add(gate); // GameRealmGate.WalkCheckedInRegion
                TestContext.WriteLine($"DOOR {row.Id} {(eRealm)row.Realm} border={gate.IsBorderKeep} doorPolygons={registered}");
                gates[row.Id] = gate;
            }
            var nodes = new WrappedPathfindingNode[512];
            foreach (int first in gates.Keys.Where(id => id % 100 == 1 && gates.ContainsKey(id + 1)))
            {
                Gate a = gates[first], b = gates[first + 1];
                Vector3 pa = new(a.X, a.Y, a.Z), pb = new(b.X, b.Y, b.Z);
                Vector3 axis = Vector3.Normalize(new(pb.X - pa.X, pb.Y - pa.Y, 0));
                Zone zone = a.CurrentZone;
                if (zone == null || !nav.HasNavmesh(zone)) { TestContext.WriteLine($"GATE {first}: no mesh loaded"); continue; }
                Vector3? start = nav.GetClosestPoint(zone, pa - axis * 600, 200, 200, 400, nav.DefaultFilters);
                Vector3? end = nav.GetClosestPoint(zone, pb + axis * 600, 200, 200, 400, nav.DefaultFilters);
                if (start == null || end == null) { failures.Add($"{first}: no floor either side"); continue; }
                var walk = nav.GetPathStraight(zone, start.Value, end.Value, nav.DefaultFilters, nodes);
                int count = Math.Min(walk.NodeCount, nodes.Length);
                // The mover's door list for this walk: doors near door polygons' nodes (wide search for a shut one), plus
                // the off-mesh border keep doors found on the walk.
                var doors = new Dictionary<WrappedPathfindingNode, List<GameDoorBase>>();
                var queue = new RingQueue<WrappedPathfindingNode>();
                for (int i = 1; i < count; i++)
                {
                    queue.Enqueue(nodes[i]);
                    if ((nodes[i].Flags & EDtPolyFlags.AnyDoor) == 0) continue;
                    float reach = (nodes[i].Flags & EDtPolyFlags.BlockingDoor) != 0 ? Pathfinder.WIDE_DOOR_SEARCH_DISTANCE : Pathfinder.DOOR_SEARCH_DISTANCE;
                    var near = gates.Values.Where(g => g.CurrentRegion == a.CurrentRegion &&
                        Vector3.Distance(new(g.X, g.Y, g.Z), nodes[i].Position) <= reach).Cast<GameDoorBase>().ToList();
                    if (near.Count > 0) doors[nodes[i]] = near;
                }
                Pathfinder.MarkGatesOffMesh(queue, doors, start.Value, offMesh);
                var friend = (Walker)RuntimeHelpers.GetUninitializedObject(typeof(Walker)); friend.Realm = a.Realm;
                var enemy = (Walker)RuntimeHelpers.GetUninitializedObject(typeof(Walker)); enemy.Realm = a.Realm == eRealm.Albion ? eRealm.Midgard : eRealm.Albion;
                bool friendBlocked = doors.Values.Any(list => list.Any(door => Pathfinder.BlocksActor(friend, door)));
                bool enemyBlocked = doors.Values.Any(list => list.Any(door => Pathfinder.BlocksActor(enemy, door)));
                var sealedNodes = new WrappedPathfindingNode[512];
                var sealedWalk = nav.GetPathStraight(zone, start.Value, end.Value, nav.BlockingDoorAvoidanceFilters, sealedNodes);
                if (a.IsBorderKeep && sealedWalk.Status == PathfindingStatus.PathFound)
                {
                    // The mover's detour check (Pathfinder.CrossesShutGate) must still find the shut doors on that walk.
                    var roundQueue = new RingQueue<WrappedPathfindingNode>();
                    var roundDoors = new Dictionary<WrappedPathfindingNode, List<GameDoorBase>>();
                    for (int i = 1; i < sealedWalk.NodeCount; i++) roundQueue.Enqueue(sealedNodes[i]);
                    Pathfinder.MarkGatesOffMesh(roundQueue, roundDoors, start.Value, offMesh);
                    var outsider = (Walker)RuntimeHelpers.GetUninitializedObject(typeof(Walker));
                    outsider.Realm = a.Realm == eRealm.Albion ? eRealm.Midgard : eRealm.Albion;
                    if (!Pathfinder.CrossesShutGate(outsider, roundDoors)) failures.Add($"{first}: an enemy walks round its shut doors");
                    var sealedDoors = sealedNodes.Take(sealedWalk.NodeCount).Where(n => (n.Flags & EDtPolyFlags.AnyDoor) != 0)
                        .Select(n => gates.Values.Where(g => g.CurrentRegion == a.CurrentRegion)
                            .Min(g => Vector3.Distance(new(g.X, g.Y, g.Z), n.Position))).ToArray();
                    TestContext.WriteLine($"GATE {first}: walk round its shut doors " + string.Join(" ", sealedNodes.Take(sealedWalk.NodeCount)
                        .Select(n => $"{n.Position.X:0},{n.Position.Y:0},{n.Position.Z:0}/{n.Flags}")) +
                        $" doorNodesToNearestDoorRow={string.Join(",", sealedDoors.Select(d => d.ToString("0")))}");
                }
                TestContext.WriteLine($"GATE {first} {a.Realm} border={a.IsBorderKeep}: walk={walk.Status}/{walk.NodeCount} doorsOnWalk={doors.Count} " +
                                      $"friendBlocked={friendBlocked} enemyBlocked={enemyBlocked} sealedByMesh={sealedWalk.Status != PathfindingStatus.PathFound}");
                if (walk.Status != PathfindingStatus.PathFound) failures.Add($"{first}: no walk through");
                if (friendBlocked) failures.Add($"{first}: its own realm is held");
                if (a.IsMilegate && enemyBlocked) failures.Add($"{first}: a milegate holds an enemy bot");
                if (a.IsBorderKeep && !enemyBlocked) failures.Add($"{first}: an enemy bot walks through the border keep");
            }
        }
        finally
        {
            foreach (Zone zone in loaded) LocalPathfindingMgr.UnloadNavMesh(zone);
            Environment.CurrentDirectory = previous;
            GameServer.LoadTestDouble(previousServer);
            DOL.GS.ServerProperties.Properties.CLASSIC165_MODE = mode;
            PathfindingProvider.SetPathfindingMgr(previousNav);
        }
        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }
}
