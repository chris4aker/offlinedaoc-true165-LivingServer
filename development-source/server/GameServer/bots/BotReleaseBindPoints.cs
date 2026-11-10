using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>Immutable region snapshots, populated by the existing world-load query.
    /// Death timers must never block on SQLite or race against character saves.</summary>
    public static class BotReleaseBindPoints
    {
        private sealed record Bind(Point3D Point, eRealm Realm);
        private static readonly ConcurrentDictionary<ushort, Bind[]> Regions = new();

        public static void Replace(ushort region, IEnumerable<DbBindPoint> points) =>
            Regions[region] = points.Select(p => new Bind(new Point3D(p.X, p.Y, p.Z), Owner(region, (eRealm)p.Realm))).ToArray();

        public static void Replace(Region region, IEnumerable<DbBindPoint> points, IPathfindingMgr nav)
        {
            // Once at startup, not every death. Reject mesh islands/prop tops
            // instead of repeatedly releasing bots onto the same bad bind.
            Regions[region.ID] = points.Select(p => new Bind(Resolve(nav, region.GetZone(p.X, p.Y),
                    new(p.X, p.Y, p.Z)), Owner(region.ID, (eRealm)p.Realm)))
                .Where(p => p.Point != null).ToArray();
        }

        // Frontier access is not permission to bind at the host realm's border
        // keep. Classic data often leaves those stones tagged Realm=None.
        // Explicit realm tags still allow the invader's own portal bindstones.
        public static eRealm Owner(ushort region, eRealm declaredRealm) => declaredRealm != eRealm.None
            ? declaredRealm : AutonomousWorldBotController.ProtectedRealm(region, ushort.MaxValue);

        public static bool IsEnemyBindPosition(ushort region, int x, int y, int z, eRealm realm) =>
            realm != eRealm.None && Regions.TryGetValue(region, out Bind[] points) &&
            points.Any(bind => bind.Realm != eRealm.None && bind.Realm != realm &&
                System.Math.Abs((long)z - bind.Point.Z) <= 128 &&
                System.Math.Pow((double)x - bind.Point.X, 2) + System.Math.Pow((double)y - bind.Point.Y, 2) <= 96 * 96);

        /// <summary>True within <paramref name="clearance"/> of any bindstone of the region (its collision is not in the navmesh).</summary>
        public static bool IsNearBindstone(ushort region, float x, float y, float z, float clearance) =>
            Regions.TryGetValue(region, out Bind[] points) &&
            points.Any(bind => System.Math.Abs(z - bind.Point.Z) <= 200 &&
                (x - bind.Point.X) * (x - bind.Point.X) + (y - bind.Point.Y) * (y - bind.Point.Y) <= clearance * clearance);

        public static Point3D? Resolve(IPathfindingMgr nav, Zone zone, Vector3 anchor)
        {
            if (zone == null) return null;
            if (!nav.IsAvailable || !nav.HasNavmesh(zone))
                return new Point3D((int)anchor.X, (int)anchor.Y, (int)anchor.Z);
            Vector3? floor = nav.GetClosestPoint(zone, anchor, 48, 48, 128, nav.DefaultFilters);
            if (!floor.HasValue || !AutonomousRendezvousNavigation.HasLocalExit(nav, zone, floor.Value))
                return null;
            return new Point3D((int)System.Math.Round(floor.Value.X),
                (int)System.Math.Round(floor.Value.Y), (int)System.Math.Round(floor.Value.Z));
        }

        public static Point3D? Nearest(ushort region, int x, int y, eRealm realm = eRealm.None) =>
            Nearest(region, x, y, realm, null);

        /// <summary>The nearest bindstone of the realm that <paramref name="accept"/> allows (all of them when null).</summary>
        public static Point3D? Nearest(ushort region, int x, int y, eRealm realm, System.Func<Point3D, bool> accept)
        {
            if (!Regions.TryGetValue(region, out Bind[] points)) return null;
            Point3D? nearest = null;
            double best = double.MaxValue;
            foreach (Bind bind in points)
            {
                // Unclassified stones fail closed for autonomous bots. The
                // existing safe-capital fallback handles a missing friendly bind.
                if (realm != eRealm.None && bind.Realm != realm) continue;
                Point3D point = bind.Point;
                double dx = (double)x - point.X, dy = (double)y - point.Y;
                double distance = dx * dx + dy * dy;
                if (distance >= best || accept != null && !accept(point)) continue;
                best = distance;
                nearest = point;
            }
            return nearest == null ? null : new Point3D(nearest.X, nearest.Y, nearest.Z);
        }

        /// <summary>
        /// A release point on a bindstone moved a short way off it onto walkable ground (owner 2026-11-10: gamebots stood up
        /// on top of Huginfell's bindstone after a release - its collision is a client object, not in the navmesh). Tries a
        /// ring around the stone at <paramref name="radius"/>, then a wider one; keeps the point when none is free.
        /// </summary>
        public static Point3D BesideBindstone(ushort region, Point3D release, int seed, float clearance = 120f, float radius = 190f)
        {
            if (release == null || !IsNearBindstone(region, release.X, release.Y, release.Z, clearance))
                return release;
            Region reg = WorldMgr.GetRegion(region);
            Zone zone = reg?.GetZone(release.X, release.Y);
            IPathfindingMgr nav = PathfindingProvider.Instance;
            if (zone == null || nav == null || !nav.IsAvailable || !nav.HasNavmesh(zone))
                return release;
            foreach (float ring in new[] { radius, radius + 90f })
                foreach (Vector2 p in RingPoints(release.X, release.Y, ring, 8, seed))
                {
                    Vector3? ground = nav.GetClosestPoint(zone, new Vector3(p.X, p.Y, release.Z), 48, 48, 160, nav.DefaultFilters);
                    if (!ground.HasValue || IsNearBindstone(region, ground.Value.X, ground.Value.Y, ground.Value.Z, clearance) ||
                        !AutonomousRendezvousNavigation.HasLocalExit(nav, zone, ground.Value))
                        continue;
                    return new Point3D((int)System.Math.Round(ground.Value.X), (int)System.Math.Round(ground.Value.Y),
                        (int)System.Math.Round(ground.Value.Z));
                }
            return release;
        }

        /// <summary>Pure rule: <paramref name="count"/> points evenly round a circle, starting at an angle picked by the seed.</summary>
        public static IEnumerable<Vector2> RingPoints(float x, float y, float radius, int count, int seed)
        {
            double start = (seed & 0x7fffffff) % 360 * System.Math.PI / 180;
            for (int k = 0; k < count; k++)
            {
                double angle = start + k * 2 * System.Math.PI / count;
                yield return new Vector2(x + (float)(System.Math.Cos(angle) * radius), y + (float)(System.Math.Sin(angle) * radius));
            }
        }
    }
}
