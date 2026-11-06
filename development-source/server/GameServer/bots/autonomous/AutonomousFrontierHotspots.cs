using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS.Keeps;

namespace DOL.GS;

/// <summary>
/// True 1.65 Mode: where the old-frontier fights happened, taken from the installed door and keep data (nothing invented).
/// Milegates are the frontier's free-standing gates (a door that is not part of any keep or border keep: two in Hadrian's
/// Wall, two in Emain Macha, the one in Odin's Gate). Outside an enemy portal keep is the road from that portal keep
/// toward the nearest milegate, where the arriving realm meets the defenders (both enemy portal keeps of each frontier
/// stand in Hadrian's Wall, Odin's Gate and Emain Macha). The installed data has no bridge markers, so bridges are not
/// guessed at. Built once per region; the caller projects a point onto the navmesh before using it.
/// </summary>
public static class AutonomousFrontierHotspots
{
    public enum Kind { Milegate, OutsidePortalKeep }

    public readonly record struct Anchor(string Id, string Name, Kind Kind, ushort RegionId, int X, int Y, int Z);

    public readonly record struct DoorPoint(Vector3 Position, string ZoneName);
    public readonly record struct KeepPoint(Vector3 Position, string Name, bool IsPortalKeep);

    // A door this close to a keep (or a border keep) belongs to it; the milegates stand ~25,000 units from any keep.
    public const int KeepDoorRadius = 6_000;
    // Door leaves of one gate are merged.
    public const int GateMergeRadius = 1_500;
    // Out of the portal keep's guard range, on the road toward the milegate.
    public const int OutsidePortalKeepDistance = 3_000;

    // A hotspot is this many times as likely a roaming pick as a keep's surroundings or a frontier clearing.
    public const int RoamWeight = 6;

    private static readonly ConcurrentDictionary<ushort, Anchor[]> Cache = new();

    public static bool IsOldFrontier(ushort region) => region is 1 or 100 or 200;

    public static IReadOnlyList<Anchor> For(ushort region)
    {
        if (!IsOldFrontier(region) || GameServer.KeepManager == null) return [];
        return Cache.GetOrAdd(region, Build);
    }

    private static Anchor[] Build(ushort regionId)
    {
        Region region = WorldMgr.GetRegion(regionId);
        if (region == null) return [];
        var doors = DoorMgr.Snapshot()
            .Where(door => door is GameDoor && door.CurrentRegionID == regionId)
            .Select(door => (door, zone: region.GetZone(door.X, door.Y)))
            .Where(entry => entry.zone != null && AutonomousWorldBotController.IsFrontierZone(regionId, entry.zone.ID))
            .Select(entry => new DoorPoint(new(entry.door.X, entry.door.Y, entry.door.Z), entry.zone.Description));
        var keeps = GameServer.KeepManager.GetKeepsOfRegion(regionId)
            .Select(keep => new KeepPoint(new(keep.X, keep.Y, keep.Z), keep.Name, keep.IsPortalKeep));
        var borderKeeps = new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia }
            .SelectMany(AutonomousRvrStaging.BorderKeeps).Where(keep => keep.RegionId == regionId).Select(keep => keep.Position);
        return Derive(regionId, doors, keeps, borderKeeps);
    }

    /// <summary>The anchors of one frontier region from its doors and keeps (pure; see the class summary).</summary>
    public static Anchor[] Derive(ushort regionId, IEnumerable<DoorPoint> doors, IEnumerable<KeepPoint> keeps,
        IEnumerable<Vector3> borderKeeps)
    {
        KeepPoint[] keepList = keeps.ToArray();
        Vector3[] blockers = keepList.Select(keep => keep.Position).Concat(borderKeeps).ToArray();
        var gates = new List<(Vector3 Sum, int Count, string Zone)>();
        foreach (DoorPoint door in doors)
        {
            if (blockers.Any(point => Distance2(point, door.Position) <= KeepDoorRadius)) continue;
            int index = gates.FindIndex(gate => Distance2(gate.Sum / gate.Count, door.Position) <= GateMergeRadius);
            if (index < 0) gates.Add((door.Position, 1, door.ZoneName));
            else gates[index] = (gates[index].Sum + door.Position, gates[index].Count + 1, gates[index].Zone);
        }
        var anchors = new List<Anchor>();
        Vector3[] milegates = gates.Select(gate => gate.Sum / gate.Count).ToArray();
        for (int i = 0; i < gates.Count; i++)
        {
            Vector3 p = milegates[i];
            anchors.Add(new($"rvr-milegate-{regionId}-{(int)p.X}-{(int)p.Y}", $"the milegate in {gates[i].Zone}", Kind.Milegate,
                regionId, (int)p.X, (int)p.Y, (int)p.Z));
        }
        foreach (KeepPoint portal in keepList.Where(keep => keep.IsPortalKeep))
        {
            if (milegates.Length == 0) break;
            Vector3 gate = milegates.OrderBy(m => Distance2(m, portal.Position)).First();
            Vector2 direction = new(gate.X - portal.Position.X, gate.Y - portal.Position.Y);
            float length = direction.Length();
            if (length < OutsidePortalKeepDistance * 2) continue;
            Vector2 point = new Vector2(portal.Position.X, portal.Position.Y) + direction / length * OutsidePortalKeepDistance;
            anchors.Add(new($"rvr-outside-{regionId}-{(int)portal.Position.X}-{(int)portal.Position.Y}", $"outside {portal.Name}",
                Kind.OutsidePortalKeep, regionId, (int)point.X, (int)point.Y, (int)portal.Position.Z));
        }
        return anchors.ToArray();
    }

    private static readonly ConcurrentDictionary<(string Id, long Revision), Vector3?> Floors = new();

    /// <summary>The walkable floor at an anchor with a way out (answered once per anchor and mesh revision).</summary>
    public static bool TryFloor(Anchor anchor, out Vector3 floor)
    {
        floor = default;
        IPathfindingMgr nav = PathfindingProvider.Instance;
        Zone zone = WorldMgr.GetRegion(anchor.RegionId)?.GetZone(anchor.X, anchor.Y);
        if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) return false;
        Vector3? found = Floors.GetOrAdd((anchor.Id, NavigationGeometryRevision.Read(zone)), _ =>
        {
            Vector3? point = nav.GetClosestPoint(zone, new(anchor.X, anchor.Y, anchor.Z), 256, 256, 1_500, nav.DefaultFilters);
            return point.HasValue && AutonomousRendezvousNavigation.HasLocalExit(nav, zone, point.Value) ? point : null;
        });
        if (!found.HasValue) return false;
        floor = found.Value;
        return true;
    }

    private static float Distance2(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));
}
