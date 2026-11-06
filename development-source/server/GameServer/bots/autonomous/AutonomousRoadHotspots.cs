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

        private readonly record struct Death(int X, int Y, long Bot, int KillerLevel, long Tick);

        /// <summary>A hotspot; replaced, never changed, so readers need no lock.</summary>
        public sealed record Hotspot(ushort Region, int X, int Y, int KillerLevel, long Until);

        private static readonly object Sync = new();
        // deaths per region, so a new death only scans its own region's list
        private static readonly Dictionary<ushort, List<Death>> Deaths = new();
        private static int _deathCount;
        // the current hotspots, published as a new array on every change and read without the lock (camp and service
        // planning check it for every candidate)
        private static volatile Hotspot[] _hotspots = [];

        /// <summary>Records a bot's death; returns the hotspot it made or renewed, if any.</summary>
        public static Hotspot RecordDeath(ushort region, int x, int y, long bot, int botLevel, int killerLevel, long now,
            out bool created)
        {
            created = false;
            if (killerLevel < botLevel + KillerLevelsAbove)
                return null;
            lock (Sync)
            {
                if (!Deaths.TryGetValue(region, out List<Death> deaths))
                    Deaths[region] = deaths = new();
                _deathCount -= deaths.RemoveAll(d => now - d.Tick > WindowMilliseconds);
                if (_deathCount >= MaxDeaths)
                {
                    foreach (List<Death> other in Deaths.Values)
                        _deathCount -= other.RemoveAll(d => now - d.Tick > WindowMilliseconds);
                    if (_deathCount >= MaxDeaths && deaths.Count > 0)
                    {
                        deaths.RemoveAt(0);
                        _deathCount--;
                    }
                }
                deaths.Add(new Death(x, y, bot, killerLevel, now));
                _deathCount++;
                HashSet<long> bots = new();
                int maxKiller = 0;
                foreach (Death d in deaths)
                    if (Near(d.X, d.Y, x, y, Radius))
                    {
                        bots.Add(d.Bot);
                        maxKiller = Math.Max(maxKiller, d.KillerLevel);
                    }
                if (bots.Count < DistinctBots)
                    return null;
                List<Hotspot> next = new(_hotspots.Length + 1);
                Hotspot old = null;
                foreach (Hotspot h in _hotspots)
                {
                    if (h.Until <= now)
                        continue;
                    if (old == null && h.Region == region && Near(h.X, h.Y, x, y, Radius))
                        old = h;
                    else
                        next.Add(h);
                }
                created = old == null;
                Hotspot spot = new(region, old?.X ?? x, old?.Y ?? y, Math.Max(old?.KillerLevel ?? 0, maxKiller), now + ActiveMilliseconds);
                next.Add(spot);
                _hotspots = next.ToArray();
                return spot;
            }
        }

        /// <summary>
        /// The live hotspots dangerous to a bot of this level (an empty array when none), read once per planning pass and
        /// handed to <see cref="Blocks(Hotspot[], ushort, int, int, ushort, int, int)"/> for each candidate.
        /// </summary>
        public static Hotspot[] DangerousTo(int botLevel, long now)
        {
            Hotspot[] all = _hotspots;
            if (all.Length == 0)
                return all;
            int count = 0;
            foreach (Hotspot h in all)
                if (Dangerous(h, botLevel, now))
                    count++;
            if (count == 0)
                return [];
            if (count == all.Length)
                return all;
            Hotspot[] result = new Hotspot[count];
            int i = 0;
            foreach (Hotspot h in all)
                if (Dangerous(h, botLevel, now))
                    result[i++] = h;
            return result;
        }

        /// <summary>
        /// True when a bot of this level should not travel to (toX, toY) in its region: the destination is near a hotspot
        /// dangerous to it, or the straight way there passes one. A destination in another region is checked against the
        /// hotspots of that region only.
        /// </summary>
        public static bool Blocks(ushort fromRegion, int fromX, int fromY, int botLevel, ushort toRegion, int toX, int toY, long now)
        {
            Hotspot[] all = _hotspots;
            if (all.Length == 0)
                return false;
            foreach (Hotspot h in all)
                if (Dangerous(h, botLevel, now) && Blocks(h, fromRegion, fromX, fromY, toRegion, toX, toY))
                    return true;
            return false;
        }

        /// <summary>The same check against hotspots already filtered by <see cref="DangerousTo"/>.</summary>
        public static bool Blocks(Hotspot[] dangerous, ushort fromRegion, int fromX, int fromY, ushort toRegion, int toX, int toY)
        {
            foreach (Hotspot h in dangerous)
                if (Blocks(h, fromRegion, fromX, fromY, toRegion, toX, toY))
                    return true;
            return false;
        }

        private static bool Dangerous(Hotspot h, int botLevel, long now) =>
            h.Until > now && botLevel <= h.KillerLevel - KillerLevelsAbove;

        private static bool Blocks(Hotspot h, ushort fromRegion, int fromX, int fromY, ushort toRegion, int toX, int toY)
        {
            if (h.Region != toRegion)
                return false;
            if (Near(h.X, h.Y, toX, toY, Radius))
                return true;
            // already inside it: only destinations near it are refused, so the bot can still choose its way out
            return fromRegion == toRegion && !Near(h.X, h.Y, fromX, fromY, Radius) &&
                SegmentDistanceSquared(fromX, fromY, toX, toY, h.X, h.Y) <= (double)Radius * Radius;
        }

        public static void Clear()
        {
            lock (Sync)
            {
                Deaths.Clear();
                _deathCount = 0;
                _hotspots = [];
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
