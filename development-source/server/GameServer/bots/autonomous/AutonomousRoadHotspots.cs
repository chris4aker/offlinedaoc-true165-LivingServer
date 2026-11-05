using System;
using System.Collections.Generic;

namespace DOL.GS
{
    /// <summary>
    /// Spots where many gamebots die to monsters far above them (2026-11-05 audit: about 12,700 units south of Mag Mell in
    /// Lough Derg, lunantishee and blackthorn of level 8-9 killed low Hibernian bots 5,690 times since Nov 3 - 577 bots,
    /// 200-250 deaths an hour - because their merchant and camp trips walked straight through). A death counts when the
    /// killer is at least <see cref="KillerLevelsAbove"/> levels above the bot; once <see cref="DistinctBots"/> different
    /// bots died within <see cref="Radius"/> of each other in <see cref="WindowMilliseconds"/>, the spot is a hotspot for
    /// <see cref="ActiveMilliseconds"/> (renewed by every new death there). Bots below the danger level then choose no
    /// merchant, healer, trainer or camp near it or straight past it. Monsters are not touched.
    /// </summary>
    public static class AutonomousRoadHotspots
    {
        public const int KillerLevelsAbove = 3;
        public const int DistinctBots = 4;
        public const int Radius = 2_500;
        public const long WindowMilliseconds = 60 * 60_000;
        public const long ActiveMilliseconds = 2 * 60 * 60_000;
        private const int MaxDeaths = 4_000;

        private readonly record struct Death(ushort Region, int X, int Y, long Bot, int KillerLevel, long Tick);

        public sealed class Hotspot
        {
            public ushort Region;
            public int X, Y;
            public int KillerLevel;
            public long Until;
        }

        private static readonly object Sync = new();
        private static readonly List<Death> Deaths = new();
        private static readonly List<Hotspot> Hotspots = new();

        /// <summary>Records a bot's death; returns the hotspot it made or renewed, if any.</summary>
        public static Hotspot RecordDeath(ushort region, int x, int y, long bot, int botLevel, int killerLevel, long now,
            out bool created)
        {
            created = false;
            if (killerLevel < botLevel + KillerLevelsAbove)
                return null;
            lock (Sync)
            {
                Deaths.RemoveAll(d => now - d.Tick > WindowMilliseconds);
                if (Deaths.Count >= MaxDeaths)
                    Deaths.RemoveAt(0);
                Deaths.Add(new Death(region, x, y, bot, killerLevel, now));
                HashSet<long> bots = new();
                int maxKiller = 0;
                foreach (Death d in Deaths)
                    if (d.Region == region && Near(d.X, d.Y, x, y, Radius))
                    {
                        bots.Add(d.Bot);
                        maxKiller = Math.Max(maxKiller, d.KillerLevel);
                    }
                if (bots.Count < DistinctBots)
                    return null;
                Hotspots.RemoveAll(h => h.Until <= now);
                Hotspot spot = Hotspots.Find(h => h.Region == region && Near(h.X, h.Y, x, y, Radius));
                if (spot == null)
                {
                    Hotspots.Add(spot = new Hotspot { Region = region, X = x, Y = y });
                    created = true;
                }
                spot.KillerLevel = Math.Max(spot.KillerLevel, maxKiller);
                spot.Until = now + ActiveMilliseconds;
                return spot;
            }
        }

        /// <summary>
        /// True when a bot of this level should not travel to (toX, toY) in its region: the destination is near a hotspot
        /// dangerous to it, or the straight way there passes one. A destination in another region is checked against the
        /// hotspots of that region only.
        /// </summary>
        public static bool Blocks(ushort fromRegion, int fromX, int fromY, int botLevel, ushort toRegion, int toX, int toY, long now)
        {
            lock (Sync)
            {
                if (Hotspots.Count == 0)
                    return false;
                foreach (Hotspot h in Hotspots)
                {
                    if (h.Until <= now || botLevel > h.KillerLevel - KillerLevelsAbove || h.Region != toRegion)
                        continue;
                    if (Near(h.X, h.Y, toX, toY, Radius))
                        return true;
                    // already inside it: only destinations near it are refused, so the bot can still choose its way out
                    if (fromRegion == toRegion && !Near(h.X, h.Y, fromX, fromY, Radius) && SegmentDistanceSquared(fromX, fromY, toX, toY, h.X, h.Y) <= (double)Radius * Radius)
                        return true;
                }
                return false;
            }
        }

        public static void Clear()
        {
            lock (Sync)
            {
                Deaths.Clear();
                Hotspots.Clear();
            }
        }

        private static bool Near(int ax, int ay, int bx, int by, int radius)
        {
            double dx = ax - bx, dy = ay - by;
            return dx * dx + dy * dy <= (double)radius * radius;
        }

        public static double SegmentDistanceSquared(int ax, int ay, int bx, int by, int px, int py)
        {
            double vx = bx - ax, vy = by - ay;
            double len = vx * vx + vy * vy;
            double t = len <= 0 ? 0 : Math.Clamp(((px - ax) * vx + (py - ay) * vy) / len, 0, 1);
            double cx = ax + t * vx - px, cy = ay + t * vy - py;
            return cx * cx + cy * cy;
        }
    }
}
