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
/// True 1.65 Mode milegates on the installed navmeshes (read-only; the meshes are loaded into memory only). For every gate
/// fort the shortest walk between its two sides goes through it; a gate with door polygons shuts that walk on the mesh,
/// one without (the installed gate fort meshes, 2026-11-06) is found on the walk by the mover (Pathfinder.MarkGatesOffMesh):
/// own realm walks on, an enemy stops in front of the outer gate. Also reports whether the wall can be walked round when the
/// gate fort's floor is taken away. OFFLINE_DAOC_NAV_ROOT = runtime\server, OFFLINE_DAOC_DB_PATH = a 1.65 database with
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

    private delegate EDtStatus UpdateFlagsFn(IntPtr mesh, ReadOnlySpan<ulong> polys, int count, EDtPolyFlags remove, EDtPolyFlags add);

    private sealed class Walker : GameBot
    {
        private Walker() : base((OfflineWorldBotRecord)null) { }
        public override eRealm Realm { get; set; }
        public override bool IsAlive => true;
    }

    [Test]
    public void GateFortsShutTheirWalkForEnemiesOnly()
    {
        string previous = Environment.CurrentDirectory;
        var loaded = new List<Zone>();
        var regions = new Dictionary<ushort, Region>();
        var failures = new List<string>();
        var previousServer = GameServer.Instance;
        var previousNav = PathfindingProvider.Instance;
        GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
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
                bool registered = nav.RegisterDoor(gate);
                if (!registered) offMesh.Add(gate);
                TestContext.WriteLine($"DOOR {row.Id} {(eRealm)row.Realm} at {row.Point}: doorPolygons={registered}");
                gates[row.Id] = gate;
            }
            bool registeredBoth(Gate x, Gate y) => !offMesh.Contains(x) && !offMesh.Contains(y);
            var getPolys = typeof(LocalPathfindingMgr).GetMethod("GetNearestPolys", BindingFlags.Static | BindingFlags.NonPublic);
            var meshPtr = typeof(LocalPathfindingMgr).GetMethod("TryGetMeshPtr", BindingFlags.Static | BindingFlags.NonPublic);
            var updateFlags = typeof(LocalPathfindingMgr).GetMethod("UpdateFlags", BindingFlags.Static | BindingFlags.NonPublic)
                ?.CreateDelegate<UpdateFlagsFn>();
            var nodes = new WrappedPathfindingNode[512];
            foreach (int first in gates.Keys.Where(id => id % 100 == 1 && gates.ContainsKey(id + 1) && !GameDoor.IsBorderKeepDoor(id)))
            {
                Gate a = gates[first], b = gates[first + 1];
                Vector3 pa = new(a.X, a.Y, a.Z), pb = new(b.X, b.Y, b.Z);
                Vector3 axis = Vector3.Normalize(new(pb.X - pa.X, pb.Y - pa.Y, 0));
                Zone zone = a.CurrentZone;
                Vector3? start = nav.GetClosestPoint(zone, pa - axis * 600, 200, 200, 400, nav.DefaultFilters);
                Vector3? end = nav.GetClosestPoint(zone, pb + axis * 600, 200, 200, 400, nav.DefaultFilters);
                if (start == null || end == null) { failures.Add($"{first}: no floor either side"); continue; }
                var through = nav.GetPathStraight(zone, start.Value, end.Value, nav.DefaultFilters, nodes);
                var queue = new RingQueue<WrappedPathfindingNode>();
                var doors = new Dictionary<WrappedPathfindingNode, List<GameDoorBase>>();
                for (int i = 1; i < Math.Min(through.NodeCount, nodes.Length); i++) queue.Enqueue(nodes[i]);
                bool meshDoor = Enumerable.Range(0, Math.Min(through.NodeCount, nodes.Length)).Any(i => (nodes[i].Flags & EDtPolyFlags.AnyDoor) != 0);
                // The mover finds a door object within Pathfinder.DOOR_SEARCH_DISTANCE of a door polygon's node.
                float nearestDoorNode = Enumerable.Range(0, Math.Min(through.NodeCount, nodes.Length))
                    .Where(i => (nodes[i].Flags & EDtPolyFlags.AnyDoor) != 0)
                    .Select(i => Math.Min(Vector3.Distance(nodes[i].Position, pa), Vector3.Distance(nodes[i].Position, pb)))
                    .DefaultIfEmpty(-1).Min();
                var sealedNodes = new WrappedPathfindingNode[512];
                var sealedWalk = nav.GetPathStraight(zone, start.Value, end.Value, nav.BlockingDoorAvoidanceFilters, sealedNodes);
                if (sealedWalk.Status == PathfindingStatus.PathFound)
                    TestContext.WriteLine($"GATE {first}: sealed walk " + string.Join(" ", sealedNodes.Take(sealedWalk.NodeCount)
                        .Select(n => $"{n.Position.X:0},{n.Position.Y:0},{n.Position.Z:0}/{n.Flags}")));
                int marked = Pathfinder.MarkGatesOffMesh(queue, doors, start.Value, offMesh);
                var friend = (Walker)RuntimeHelpers.GetUninitializedObject(typeof(Walker)); friend.Realm = a.Realm;
                var enemy = (Walker)RuntimeHelpers.GetUninitializedObject(typeof(Walker)); enemy.Realm = a.Realm == eRealm.Albion ? eRealm.Midgard : eRealm.Albion;
                bool friendBlocked = doors.Values.Any(list => list.Any(door => Pathfinder.BlocksActor(friend, door)));
                bool enemyBlocked = doors.Values.Any(list => list.Any(door => Pathfinder.BlocksActor(enemy, door)));
                int beforeStop = queue.Count;
                GameDoorBase firstBlock = null;
                for (int i = 0; i < queue.Count && firstBlock == null; i++)
                    if (doors.TryGetValue(queue.Peek(i), out var near))
                        firstBlock = near.FirstOrDefault(door => Pathfinder.BlocksActor(enemy, door));
                bool stopped = firstBlock != null && Pathfinder.StopAtDoor(queue, doors, firstBlock);
                Vector3 last = queue.Count > 0 ? queue.Peek(queue.Count - 1).Position : start.Value;
                float gap = stopped ? Vector2.Distance(new(last.X, last.Y), new(firstBlock.X, firstBlock.Y)) : -1;

                // Information: with the gate fort's floor taken away (in memory), is there a walk round the wall?
                string round = "-";
                if (meshPtr != null && getPolys != null && updateFlags != null)
                {
                    object[] args = [zone, default(nint)];
                    if ((bool)meshPtr.Invoke(null, args))
                    {
                        var polys = new HashSet<ulong>();
                        foreach (Vector3 p in new[] { pa, (pa + pb) / 2, pb })
                            foreach (ulong poly in (ulong[])getPolys.Invoke(null, [zone, p, new[] { EDtPolyFlags.Walk, (EDtPolyFlags)0 }, 12, 64f]))
                                polys.Add(poly);
                        ulong[] cut = polys.ToArray();
                        updateFlags((nint)args[1], cut, cut.Length, (EDtPolyFlags)0, EDtPolyFlags.Disabled);
                        var around = nav.GetPathStraight(zone, start.Value, end.Value, [EDtPolyFlags.All ^ EDtPolyFlags.Disabled, EDtPolyFlags.Disabled], nodes);
                        float length = 0;
                        for (int i = 1; i < around.NodeCount; i++) length += Vector3.Distance(nodes[i - 1].Position, nodes[i].Position);
                        round = $"{around.Status}/{around.NodeCount} length={length:0} (cut {cut.Length} polygons)";
                        updateFlags((nint)args[1], cut, cut.Length, EDtPolyFlags.Disabled, (EDtPolyFlags)0);
                    }
                }
                TestContext.WriteLine($"GATE {first} {a.Realm}: walk={through.Status}/{through.NodeCount} meshDoor={meshDoor} offMeshGatesOnWalk={marked} " +
                                      $"friendBlocked={friendBlocked} enemyBlocked={enemyBlocked} enemyStops={gap:0} from door {firstBlock?.DoorId} " +
                                      $"(nodes {beforeStop}->{queue.Count}); round the wall without the gate fort: {round}");
                TestContext.WriteLine($"GATE {first}: doorNodeToGate={nearestDoorNode:0} shutGates={sealedWalk.Status}");
                if (meshDoor && (nearestDoorNode < 0 || nearestDoorNode > Pathfinder.DOOR_SEARCH_DISTANCE))
                    failures.Add($"{first}: the gate's door polygons lie {nearestDoorNode:0} from its door rows, the mover would not see it");
                if (meshDoor && registeredBoth(a, b) && sealedWalk.Status == PathfindingStatus.PathFound)
                    failures.Add($"{first}: a way round its shut gates");
                if (through.Status != PathfindingStatus.PathFound) failures.Add($"{first}: no walk through the gate fort");
                if (!meshDoor && marked < 1) failures.Add($"{first}: the walk through is not seen as running through the gate");
                if (friendBlocked) failures.Add($"{first}: its own realm is shut out");
                if (!meshDoor && !enemyBlocked) failures.Add($"{first}: an enemy is not stopped");
                if (stopped && gap > 200) failures.Add($"{first}: the enemy stops {gap:0} from the gate, out of melee reach");
            }
        }
        finally
        {
            foreach (Zone zone in loaded) LocalPathfindingMgr.UnloadNavMesh(zone);
            Environment.CurrentDirectory = previous;
            GameServer.LoadTestDouble(previousServer);
            PathfindingProvider.SetPathfindingMgr(previousNav);
        }
        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }
}
